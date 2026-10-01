using System;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AotForms
{
    public static class DiscordRPCManager
    {
        // ==========================================================
        // 🔥 KRISHU X CHEATS - Discord RPC
        // ==========================================================
        private const string ApplicationId = "1411819599019638927";

        // ==========================================================
        // 🎨 IMAGES
        // ==========================================================
        private const string LargeImageUrl = "https://files.catbox.moe/p2e5cx.gif";
        private const string SmallImageUrl = "https://files.catbox.moe/3z46o8.gif";

        private static NamedPipeClientStream pipeClient;
        private static CancellationTokenSource cts;
        private static long startTime;

        // ==========================================================
        // 📊 STATS
        // ==========================================================
        private static string _username = "Player";
        private static int _enemyCount = 0;
        private static int _sessionKills = 0;
        private static int _sessionHeadshots = 0;
        private static string _currentDetails = "🔥 KRISHU X CHEATS | FreeFire";
        private static string _currentState = "🚀 Initializing...";

        // ==========================================================
        // 🚀 INITIALIZE
        // ==========================================================
        public static void Initialize(string username)
        {
            _username = string.IsNullOrEmpty(username) ? "Player" : username;

            if (startTime == 0)
                startTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            if (pipeClient != null && pipeClient.IsConnected)
            {
                PushActivity();
                return;
            }

            cts?.Cancel();
            cts = new CancellationTokenSource();

            Task.Run(async () =>
            {
                try
                {
                    int pipeIndex = 0;
                    while (pipeIndex < 10)
                    {
                        try
                        {
                            pipeClient = new NamedPipeClientStream(".", $"discord-ipc-{pipeIndex}", PipeDirection.InOut, PipeOptions.Asynchronous);
                            await pipeClient.ConnectAsync(2000);
                            if (pipeClient.IsConnected) break;
                        }
                        catch
                        {
                            pipeClient?.Dispose();
                            pipeClient = null;
                            pipeIndex++;
                        }
                    }

                    if (pipeClient != null && pipeClient.IsConnected)
                    {
                        string handshakeJson = $"{{\"v\": 1, \"client_id\": \"{ApplicationId}\"}}";
                        SendPacket(0, handshakeJson);
                        await Task.Delay(600);

                        // Set initial activity
                        _currentDetails = "🔥 KRISHU X CHEATS";
                        _currentState = "🚀 Ready to DOMINATE!";
                        PushActivity();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("KRISHU X CHEATS Init Error: " + ex.Message);
                }
            }, cts.Token);
        }

        // ==========================================================
        // 📊 UPDATE STATS
        // ==========================================================
        public static void UpdateStats(int enemyCount, int kills = -1, int headshots = -1)
        {
            _enemyCount = enemyCount;
            if (kills >= 0) _sessionKills = kills;
            if (headshots >= 0) _sessionHeadshots = headshots;

            UpdateState();
            PushActivity();
        }

        // ==========================================================
        // 🔄 UPDATE STATE
        // ==========================================================
        private static void UpdateState()
        {
            // Update details
            _currentDetails = $"🔥 KRISHU X CHEATS | User: {_username}";

            // Update state based on enemy count
            if (_enemyCount == 0)
                _currentState = "🔍 Scanning... No enemies nearby";
            else if (_enemyCount == 1)
                _currentState = $"⚠️ {_enemyCount} Enemy Detected!";
            else if (_enemyCount <= 5)
                _currentState = $"🔴 {_enemyCount} Enemies Detected!";
            else if (_enemyCount <= 10)
                _currentState = $"🔥 {_enemyCount} Enemies! FIGHT!";
            else
                _currentState = $"💀 {_enemyCount} Enemies! Let's GO!";

            // Add kill stats
            if (_sessionKills > 0)
            {
                _currentState += $" | ☠️ {_sessionKills} Kills";
                if (_sessionHeadshots > 0)
                    _currentState += $" | 🎯 {_sessionHeadshots} HS";
            }
        }

        // ==========================================================
        // 📤 PUSH ACTIVITY
        // ==========================================================
        private static void PushActivity()
        {
            if (pipeClient == null || !pipeClient.IsConnected) return;

            int pid = Process.GetCurrentProcess().Id;

            string activityJson = $$"""
            {
              "cmd": "SET_ACTIVITY",
              "args": {
                "pid": {{pid}},
                "activity": {
                  "details": "{{_currentDetails}}",
                  "state":   "{{_currentState}}",
                  "assets": {
                    "large_image": "{{LargeImageUrl}}",
                    "large_text":  "🔥 KRISHU X CHEATS",
                    "small_image": "{{SmallImageUrl}}",
                    "small_text":  "{{(_enemyCount > 0 ? $"⚔️ {_enemyCount} enemies nearby" : "🔍 Scanning...")}}"
                  },
                  "timestamps": {
                    "start": {{startTime}}
                  },
                  "buttons": [
                    {
                      "label": "🔥 KRISHU X CHEATS SERVER",
                      "url":   "https://discord.com/invite/xmfKB7nJ8T"
                    },
                    {
                      "label": "YOUTUBE CHENNAL -:",
                      "url":   "https://www.youtube.com/@Krishu_X_Cheats"
                    }
                  ]
                }
              },
              "nonce": "{{Guid.NewGuid()}}"
            }
            """;

            SendPacket(1, activityJson);
        }

        // ==========================================================
        // 📨 SEND PACKET
        // ==========================================================
        private static void SendPacket(int opcode, string payload)
        {
            try
            {
                if (pipeClient == null || !pipeClient.IsConnected) return;

                byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);
                byte[] buffer = new byte[8 + payloadBytes.Length];

                // Opcode (4 bytes)
                buffer[0] = (byte)(opcode & 0xFF);
                buffer[1] = (byte)((opcode >> 8) & 0xFF);
                buffer[2] = (byte)((opcode >> 16) & 0xFF);
                buffer[3] = (byte)((opcode >> 24) & 0xFF);

                // Length (4 bytes)
                int length = payloadBytes.Length;
                buffer[4] = (byte)(length & 0xFF);
                buffer[5] = (byte)((length >> 8) & 0xFF);
                buffer[6] = (byte)((length >> 16) & 0xFF);
                buffer[7] = (byte)((length >> 24) & 0xFF);

                Array.Copy(payloadBytes, 0, buffer, 8, payloadBytes.Length);

                pipeClient.Write(buffer, 0, buffer.Length);
                pipeClient.Flush();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("KRISHU X CHEATS Send Error: " + ex.Message);
                try { pipeClient?.Dispose(); pipeClient = null; } catch { }
            }
        }

        // ==========================================================
        // 🛑 DEINITIALIZE
        // ==========================================================
        public static void Deinitialize()
        {
            try
            {
                cts?.Cancel();
                cts?.Dispose();

                if (pipeClient != null)
                {
                    SendPacket(2, "{}");
                    pipeClient.Dispose();
                    pipeClient = null;
                }
            }
            catch { }
        }

        // ==========================================================
        // 🎯 SET CUSTOM STATUS
        // ==========================================================
        public static void SetStatus(string details, string state)
        {
            if (!string.IsNullOrEmpty(details))
                _currentDetails = details;
            if (!string.IsNullOrEmpty(state))
                _currentState = state;
            PushActivity();
        }

        // ==========================================================
        // 🏆 SET KILLS
        // ==========================================================
        public static void SetKills(int kills)
        {
            _sessionKills = kills;
            UpdateState();
            PushActivity();
        }

        // ==========================================================
        // 🎯 SET HEADSHOTS
        // ==========================================================
        public static void SetHeadshots(int headshots)
        {
            _sessionHeadshots = headshots;
            UpdateState();
            PushActivity();
        }

        // ==========================================================
        // 👤 SET USERNAME
        // ==========================================================
        public static void SetUsername(string username)
        {
            _username = string.IsNullOrEmpty(username) ? "Player" : username;
            UpdateState();
            PushActivity();
        }
    }
}