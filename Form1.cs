using Reborn;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;


namespace Client
{

    public partial class Form1 : Form
    {
        IntPtr mainHandle;
        public static api KeyAuthApp = new api(
             name: "ULTRA",
             ownerid: "6sOaDjtN9k",
             secret: "3e23a5e3c452ec7e481d64b4106625d44fd5308b6892c56526db178e8d6dec45",
             version: "1.0");

        public Form1(IntPtr handle)
        {
            mainHandle = handle;
            InitializeComponent();

        }

        private void guna2Button4_Click(object sender, EventArgs e)
        {

        }

        private void guna2Button5_Click(object sender, EventArgs e)
        {

        }

        private void guna2Button6_Click(object sender, EventArgs e)
        {


        }

        private void guna2TextBox1_TextChanged(object sender, EventArgs e)
        {

        }

        private void guna2TextBox2_TextChanged(object sender, EventArgs e)
        {

        }

        private void guna2TextBox3_TextChanged(object sender, EventArgs e)
        {

        }

        private void guna2TextBox4_TextChanged(object sender, EventArgs e)
        {

        }

        private void guna2TextBox5_TextChanged(object sender, EventArgs e)
        {

        }

        private void Form1_Load(object sender, EventArgs e)
        {
            KeyAuthApp.init();
            //400, 124
        }

        private void guna2Button1_Click(object sender, EventArgs e)
        {

        }

        private void guna2Button2_Click(object sender, EventArgs e)
        {

        }

        private void guna2Button3_Click(object sender, EventArgs e)
        {

        }

        private void guna2Button7_Click(object sender, EventArgs e)
        {

        }

        private void guna2ControlBox1_Click(object sender, EventArgs e)
        {
            Application.Exit();
            Environment.Exit(0);
        }

        private void guna2Button4_Click_1(object sender, EventArgs e)
        {
            KeyAuthApp.login(usr.Text, pass.Text);
            if (KeyAuthApp.response.success)
            {
                hack abc = new hack(mainHandle);
                abc.Show();
                this.Hide();

            }
            else
            {
                MessageBox.Show("Wrong User !! or Password !!");
                guna2Panel2.Visible = true;
                guna2Panel3.Visible = false;
                guna2Panel2.BringToFront();
            }
        }

        private void guna2Button2_Click_1(object sender, EventArgs e)
        {

        }

        private void guna2Panel2_Paint(object sender, PaintEventArgs e)
        {

        }

        private void guna2Button1_Click_1(object sender, EventArgs e)
        {
            guna2Panel2.Visible = false;
            guna2Panel3.Visible = true;
            guna2Panel3.BringToFront();

        }

        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void guna2Panel3_Paint(object sender, PaintEventArgs e)
        {

        }
    }
}
