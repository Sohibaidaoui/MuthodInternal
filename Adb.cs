using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AotForms
{
    /// <summary>
    /// Main helper class for ADB operations
    /// </summary>
    public static class AdbHelper
    {
        static Adb? instance;

        public static string? GetEmulatorAdbFolder()
        {
            string[] emulatorProcesses = { "HD-Player", "Bluestacks", "DnPlayer", "Nox", "Memu", "MuMuPlayer", "LDPlayer" };

            foreach (var name in emulatorProcesses)
            {
                var proc = Process.GetProcessesByName(name).FirstOrDefault();
                if (proc != null && !proc.HasExited)
                {
                    try
                    {
                        string path = proc.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(path))
                            return Path.GetDirectoryName(path);
                    }
                    catch { }
                }
            }

            return null;
        }

        public static async void Initilize()
        {
            BsAdbResolver.InitAdbPort();
            try
            {
                string? adbFolder = GetEmulatorAdbFolder();
                if (string.IsNullOrEmpty(adbFolder))
                {
                    MessageBox.Show("No supported emulator (HD-Player, LDPlayer, Nox, etc.) is running.\nPlease start the emulator first.",
                        "Emulator not found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string adbPath = Path.Combine(adbFolder, "adb.exe");
                if (!File.Exists(adbPath))
                {
                    adbPath = Path.Combine(adbFolder, "HD-Adb.exe");
                    if (!File.Exists(adbPath))
                    {
                        MessageBox.Show($"ADB not found in:\n{adbFolder}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }

                instance = new Adb(adbPath, BsAdbResolver.AdbPort);
                await instance.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Initialization failed:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public static async Task<ulong> ModuleAddrAsync(string package, string module)
        {
            if (instance == null)
                return 0;
            return await instance.FindModule(package, module);
        }

        public static async Task<string> GetFreeFirePKG()
        {
            if (instance == null)
                return string.Empty;
            return await instance.GetRunningPackage();
        }

        public static async void Close()
        {
            if (instance != null)
                await instance.Kill();
        }

        public static async Task<ulong> TryResolveIl2CppViaAdbAsync()
        {
            string? adbPath = AdbLocator.FindExecutable();
            if (adbPath == null)
                return 0;

            var adb = new Adb(adbPath);
            await adb.PrepareForConnectAsync().ConfigureAwait(false);
            if (!await adb.Start().ConfigureAwait(false))
                return 0;

            try
            {
                foreach (string package in AdbLocator.PackageCandidates)
                {
                    uint baseAddr = await adb.FindModule(package, "libil2cpp.so").ConfigureAwait(false);
                    if (baseAddr == 0)
                        continue;

                    ulong addr = baseAddr;
                    // Validate the module base
                    if (addr > 0x1000 && addr < 0x7F0000000000)
                        return addr;
                }
            }
            finally
            {
                adb.EndSession();
            }

            return 0;
        }
    }

    /// <summary>
    /// ADB implementation - Working version
    /// </summary>
    internal sealed class Adb
    {
        private const int ShellReadyTimeoutMs = 20000;
        private const int CommandTimeoutMs = 15000;

        private readonly string _path;
        private readonly StringBuilder _output = new();
        private readonly object _outputLock = new();
        private Process? _shellProcess;
        private int _port = 5555;
        private string _adbTarget = "";

        internal Adb(string path, int port = 5555)
        {
            _path = path;
            _port = port;
            _adbTarget = $"-s 127.0.0.1:{port}";
        }

        internal Task PrepareForConnectAsync()
        {
            return Task.Run(TryCloseShell);
        }

        internal async Task<bool> Start()
        {
            return await Task.Run(async () =>
            {
                try
                {
                    TryCloseShell();
                    ClearOutput();

                    ExecuteAdbCommand("kill-server");
                    ExecuteAdbCommand("start-server");
                    ExecuteAdbCommand($"connect 127.0.0.1:{_port}");
                    ExecuteAdbCommand("devices");

                    if (!StartShell())
                        return false;

                    return await WaitForShellReadyAsync().ConfigureAwait(false);
                }
                catch
                {
                    TryCloseShell();
                    return false;
                }
            }).ConfigureAwait(false);
        }

        internal async Task<uint> FindModule(string process, string module)
        {
            return await Task.Run(async () =>
            {
                try
                {
                    if (_shellProcess == null || _shellProcess.HasExited)
                        return 0U;

                    // First try pidof
                    string? pid = await ResolvePidAsync(process).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(pid))
                        return 0U;

                    ClearOutput();
                    _shellProcess.StandardInput.WriteLine($"grep -m 1 '{module}' /proc/{pid}/maps");

                    string? line = await WaitForMapLineAsync(module).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(line))
                    {
                        // Fallback: try cat
                        ClearOutput();
                        _shellProcess.StandardInput.WriteLine($"cat /proc/{pid}/maps | grep {module}");
                        line = await WaitForMapLineAsync(module).ConfigureAwait(false);
                    }

                    if (string.IsNullOrEmpty(line))
                        return 0U;

                    var parts = line.Split('-');
                    if (parts.Length < 1)
                        return 0U;

                    return Convert.ToUInt32(parts[0].Trim(), 16);
                }
                catch
                {
                    return 0U;
                }
            }).ConfigureAwait(false);
        }

        internal void EndSession() => TryCloseShell();

        internal async Task Kill()
        {
            await Task.Run(() =>
            {
                try
                {
                    TryCloseShell();
                    ExecuteAdbCommand("kill-server");

                    foreach (var p in Process.GetProcessesByName("adb"))
                    {
                        try { p.Kill(); p.WaitForExit(1000); } catch { }
                    }
                    foreach (var p in Process.GetProcessesByName("HD-Adb"))
                    {
                        try { p.Kill(); p.WaitForExit(1000); } catch { }
                    }
                }
                catch { }
            });
        }

        internal async Task<string> GetRunningPackage()
        {
            if (!await EnsureShellReady())
                return string.Empty;

            return await Task.Run(() =>
            {
                ClearOutput();
                var tcs = new TaskCompletionSource<string>();

                DataReceivedEventHandler handler = (s, e) =>
                {
                    if (e.Data == null) return;

                    if (e.Data.Contains("mResumedActivity") || e.Data.Contains("topResumedActivity"))
                    {
                        int idx = e.Data.IndexOf("u0 ");
                        if (idx >= 0)
                        {
                            string part = e.Data.Substring(idx + 3);
                            int slash = part.IndexOf('/');
                            if (slash > 0)
                            {
                                string pkg = part.Substring(0, slash).Trim();
                                if (pkg == "com.dts.freefireth" || pkg == "com.dts.freefiremax")
                                    tcs.TrySetResult(pkg);
                            }
                        }
                    }
                };

                try
                {
                    _shellProcess!.OutputDataReceived += handler;
                    _shellProcess.StandardInput.WriteLine(
                        "dumpsys activity activities | grep -m 1 -E 'mResumedActivity|topResumedActivity'");
                    _shellProcess.StandardInput.Flush();

                    if (tcs.Task.Wait(1200))
                        return tcs.Task.Result;

                    // Fallback: check pidof
                    foreach (string pkg in AdbLocator.PackageCandidates)
                    {
                        ClearOutput();
                        _shellProcess.StandardInput.WriteLine($"pidof {pkg}");
                        _shellProcess.StandardInput.Flush();
                        Thread.Sleep(200);
                        if (!string.IsNullOrEmpty(_output.ToString().Trim()))
                            return pkg;
                    }

                    return string.Empty;
                }
                finally
                {
                    _shellProcess.OutputDataReceived -= handler;
                }
            });
        }

        private async Task<bool> EnsureShellReady()
        {
            if (_shellProcess != null && !_shellProcess.HasExited)
                return true;

            return await Start();
        }

        private void EnsureDeviceConnected()
        {
            if (HasOnlineDevice())
                return;

            ExecuteAdbCommand($"connect 127.0.0.1:{_port}");
            Thread.Sleep(500);

            if (!HasOnlineDevice())
            {
                ExecuteAdbCommand("devices");
            }
        }

        private bool HasOnlineDevice()
        {
            string output = RunAdbCapture("devices", 5000);
            if (string.IsNullOrWhiteSpace(output))
                return false;

            foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Contains("List of devices", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (line.Contains("offline", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (line.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (line.Contains('\t') && line.Contains("device", StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private bool StartShell()
        {
            TryCloseShell();
            ClearOutput();

            _shellProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _path,
                    Arguments = $"{_adbTarget} shell \"getprop ro.secure ; /boot/android/android/system/xbin/bstk/su\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };

            _shellProcess.OutputDataReceived += OnShellOutput;
            _shellProcess.ErrorDataReceived += OnShellOutput;

            if (!_shellProcess.Start())
                return false;

            _shellProcess.BeginOutputReadLine();
            _shellProcess.BeginErrorReadLine();
            _shellProcess.StandardInput.AutoFlush = true;
            return true;
        }

        private async Task<bool> WaitForShellReadyAsync()
        {
            int waited = 0;
            while (waited < ShellReadyTimeoutMs)
            {
                if (_shellProcess == null || _shellProcess.HasExited)
                    return false;

                lock (_outputLock)
                {
                    if (_output.Length > 0)
                        return true;
                }

                if (waited >= 600)
                    return true;

                await Task.Delay(10).ConfigureAwait(false);
                waited += 10;
            }

            return _shellProcess != null && !_shellProcess.HasExited;
        }

        private async Task<string?> ResolvePidAsync(string package)
        {
            ClearOutput();
            _shellProcess!.StandardInput.WriteLine($"pidof {package}");
            _shellProcess.StandardInput.Flush();

            string? pid = await WaitForPidTokenAsync().ConfigureAwait(false);
            if (!string.IsNullOrEmpty(pid))
                return pid;

            ClearOutput();
            _shellProcess.StandardInput.WriteLine("ps");
            _shellProcess.StandardInput.Flush();
            return await WaitForPidFromPsAsync(package).ConfigureAwait(false);
        }

        private async Task<string?> WaitForPidTokenAsync()
        {
            int waited = 0;
            while (waited < CommandTimeoutMs)
            {
                string output = SnapshotOutput();
                foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var token = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    if (!string.IsNullOrEmpty(token) && int.TryParse(token, out _))
                        return token;
                }

                await Task.Delay(5).ConfigureAwait(false);
                waited += 5;
            }

            return null;
        }

        private async Task<string?> WaitForPidFromPsAsync(string process)
        {
            int waited = 0;
            while (waited < CommandTimeoutMs)
            {
                var procLines = SnapshotOutput().Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var procLine in procLines)
                {
                    if (!procLine.Contains(process, StringComparison.Ordinal))
                        continue;

                    var splitted = procLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var tempLine = procLine.Replace(" ", "");

                    if (splitted.Length == 0 || !tempLine.StartsWith(splitted[0], StringComparison.Ordinal))
                        continue;

                    int startIndex = splitted[0].Length;
                    if (tempLine.Length <= startIndex)
                        continue;

                    string pid = tempLine.Substring(startIndex, Math.Min(4, tempLine.Length - startIndex));
                    if (int.TryParse(pid, out _))
                        return pid;
                }

                await Task.Delay(5).ConfigureAwait(false);
                waited += 5;
            }

            return null;
        }

        private async Task<string?> WaitForMapLineAsync(string module)
        {
            int waited = 0;
            while (waited < CommandTimeoutMs)
            {
                string output = SnapshotOutput();
                foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.Contains(module, StringComparison.Ordinal) && line.Contains('-'))
                        return line.Trim();
                }

                await Task.Delay(5).ConfigureAwait(false);
                waited += 5;
            }

            return null;
        }

        private void OnShellOutput(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null)
                return;

            lock (_outputLock)
                _output.AppendLine(e.Data);
        }

        private void ClearOutput()
        {
            lock (_outputLock)
                _output.Clear();
        }

        private string SnapshotOutput()
        {
            lock (_outputLock)
                return _output.ToString();
        }

        private void TryCloseShell()
        {
            try
            {
                if (_shellProcess == null)
                    return;

                if (!_shellProcess.HasExited)
                {
                    try { _shellProcess.StandardInput.WriteLine("exit"); } catch { }
                    if (!_shellProcess.WaitForExit(500))
                        _shellProcess.Kill(true);
                }
            }
            catch { }
            finally
            {
                try { _shellProcess?.Dispose(); } catch { }
                _shellProcess = null;
            }
        }

        private void ExecuteAdbCommand(string command)
        {
            try
            {
                using var proc = CreateAdbProcess(command);
                proc.Start();
                if (!proc.WaitForExit(CommandTimeoutMs))
                {
                    try { proc.Kill(true); } catch { }
                }
            }
            catch { }
        }

        private string RunAdbCapture(string args, int timeoutMs)
        {
            try
            {
                using var proc = CreateAdbProcess(args);
                proc.StartInfo.RedirectStandardOutput = true;
                proc.StartInfo.RedirectStandardError = true;

                if (!proc.Start())
                    return "";

                if (!proc.WaitForExit(timeoutMs))
                {
                    try { proc.Kill(true); } catch { }
                }

                return proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
            }
            catch
            {
                return "";
            }
        }

        private Process CreateAdbProcess(string args)
        {
            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _path,
                    Arguments = $"{_adbTarget} {args}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            return proc;
        }
    }

    /// <summary>
    /// ADB Locator - Finds ADB executable
    /// </summary>
    internal static class AdbLocator
    {
        internal static readonly string[] PackageCandidates =
        {
            "com.dts.freefireth",
            "com.dts.freefiremax",
            "com.garena.game.kgvn",
            "com.garena.game.kgid",
        };

        internal static string? FindExecutable()
        {
            foreach (var path in EnumerateCandidatePaths())
            {
                if (File.Exists(path))
                    return path;
            }

            return null;
        }

        static IEnumerable<string> EnumerateCandidatePaths()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var paths = new List<string>();

            void Add(string? path)
            {
                if (string.IsNullOrWhiteSpace(path))
                    return;
                if (seen.Add(path))
                    paths.Add(path);
            }

            // Check running emulator processes
            foreach (var name in new[] { "HD-Player", "MSIAppPlayer", "BlueStacks" })
            {
                Process[] processes = Array.Empty<Process>();
                try
                {
                    processes = Process.GetProcessesByName(name);
                }
                catch { }

                foreach (var proc in processes)
                {
                    string? dir = null;
                    try
                    {
                        dir = Path.GetDirectoryName(proc.MainModule?.FileName);
                    }
                    catch { }

                    if (string.IsNullOrEmpty(dir))
                        continue;

                    Add(Path.Combine(dir, "HD-Adb.exe"));
                    Add(Path.Combine(dir, "adb.exe"));
                }
            }

            // Check common installation paths
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            Add(Path.Combine(programFiles, "BlueStacks_nxt", "HD-Adb.exe"));
            Add(Path.Combine(programFilesX86, "BlueStacks_nxt", "HD-Adb.exe"));
            Add(Path.Combine(programFiles, "BlueStacks", "HD-Adb.exe"));
            Add(Path.Combine(programFilesX86, "BlueStacks", "HD-Adb.exe"));
            Add(Path.Combine(programFiles, "BlueStacks_msi5", "HD-Adb.exe"));
            Add(Path.Combine(programFilesX86, "BlueStacks_msi5", "HD-Adb.exe"));

            // LDPlayer
            Add(Path.Combine(programFilesX86, "LDPlayer", "adb.exe"));

            // Nox
            Add(Path.Combine(programFilesX86, "Nox", "bin", "adb.exe"));

            // Memu
            Add(Path.Combine(programFilesX86, "Memu", "adb.exe"));

            return paths;
        }
    }

    /// <summary>
    /// BlueStacks ADB Port Resolver
    /// </summary>
    internal static class BsAdbResolver
    {
        public static int AdbPort = 5555;

        private const string BS_CONFIG = @"C:\ProgramData\BlueStacks_nxt\bluestacks.conf";
        private const string MSI5_CONFIG = @"C:\ProgramData\BlueStacks_msi5\bluestacks.conf";

        public static bool InitAdbPort()
        {
            var hd = Process.GetProcessesByName("HD-Player").FirstOrDefault();
            if (hd == null)
                return false;

            string windowTitle = hd.MainWindowTitle;
            if (string.IsNullOrEmpty(windowTitle))
                return false;

            string exePath;
            try
            {
                exePath = hd.MainModule?.FileName?.ToLower() ?? "";
            }
            catch
            {
                return false;
            }

            string? configPath = null;

            if (exePath.Contains("bluestacks_msi5"))
                configPath = MSI5_CONFIG;
            else if (exePath.Contains("bluestacks_nxt"))
                configPath = BS_CONFIG;

            if (configPath == null || !File.Exists(configPath))
                return false;

            return ResolvePortFromConfig(configPath, windowTitle);
        }

        private static bool ResolvePortFromConfig(string configPath, string displayName)
        {
            try
            {
                string[] lines = File.ReadAllLines(configPath);
                string? instanceKey = null;

                foreach (string line in lines)
                {
                    if (line.Contains("display_name") && line.Contains($"\"{displayName}\""))
                    {
                        int idx = line.IndexOf(".display_name", StringComparison.Ordinal);
                        if (idx > 0)
                        {
                            instanceKey = line.Substring(0, idx);
                            break;
                        }
                    }
                }

                if (instanceKey == null)
                    return false;

                foreach (string line in lines)
                {
                    if (line.StartsWith(instanceKey) && line.Contains("status.adb_port"))
                    {
                        var m = Regex.Match(line, "\"(\\d+)\"");
                        if (m.Success)
                        {
                            AdbPort = int.Parse(m.Groups[1].Value);
                            return true;
                        }
                    }
                }
            }
            catch
            {
                return false;
            }

            return false;
        }
    }
}