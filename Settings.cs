using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using UAssetAPI;
using UAssetAPI.UnrealTypes;

namespace BPEditor
{
    public partial class Settings : Form
    {
        private int clickCount = 0;
        public Settings()
        {
            InitializeComponent();
            Load += Settings_Load;
            foreach (string e in Enum.GetNames(typeof(EngineVersion)))
            {
                comboBox1.Items.Add(e.StartsWith("VER_") ? e.Substring(4).Replace('_', '.') : e);
            }
            comboBox1.SelectedIndex = 0;
            foreach (string e in Enum.GetNames(typeof(EngineVersion)))
            {
                comboBox2.Items.Add(e.StartsWith("VER_") ? e.Substring(4).Replace('_', '.') : e);
            }
            comboBox2.SelectedIndex = 0;
            foreach (string e in Enum.GetNames(typeof(PakVersion)))
            {
                comboBox3.Items.Add(e);
            }
            comboBox3.SelectedIndex = 0;
        }

        private void Settings_Load(object? sender, EventArgs e)
        {
            try
            {
                textBox1.Text = ((Form1)Owner).PakKey;
            }
            catch { }
            try
            {
                textBox2.Text = ((Form1)Owner).WorkDir;
            }
            catch { }
            try
            {
                comboBox1.SelectedIndex = (int)((Form1)Owner).engineVersion;
            }
            catch { }
            try
            {
                textBox3.Text = ((Form1)Owner).UsmapPath;
            }
            catch { }
            try
            {
                checkBox1.Checked = ((Form1)Owner).DontReCom;
            }
            catch { }
            try
            {
                comboBox2.SelectedIndex = (int)((Form1)Owner).exportEngineVersion;
            }
            catch { }
            try
            {
                comboBox3.SelectedIndex = (int)((Form1)Owner).exportPakVersion;
            }
            catch { }
            try
            {
                Form1 f = (Form1)Owner;
                label7.Text = "BPEditor " + f.Version + " " + (f.IsDebug ? "Debug" : (f.IsUserDebug ? "UserDebug" : "Release"));
                if (f.IsUserDebug) checkBox1.Visible = true;
            }
            catch { }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            try
            {
                ((Form1)Owner).PakKey = textBox1.Text;
            }
            catch { }
            try
            {
                if (textBox2.Text != "" && textBox2.Text != null)
                {
                    if (Directory.Exists(textBox2.Text)) ((Form1)Owner).WorkDir = textBox2.Text;
                    else
                    {
                        _ = MessageBox.Show("错误：指定的文件夹不存在。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
            catch { }
            try
            {
                ((Form1)Owner).engineVersion = (EngineVersion)comboBox1.SelectedIndex;
            }
            catch { }
            try
            {
                if (textBox3.Text != "" && textBox3.Text != null)
                {
                    if (File.Exists(textBox3.Text)) ((Form1)Owner).UsmapPath = textBox3.Text;
                    else
                    {
                        _ = MessageBox.Show("错误：指定的文件不存在。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
            catch { }
            try
            {
                ((Form1)Owner).DontReCom = checkBox1.Checked;
            }
            catch { }
            try
            {
                ((Form1)Owner).exportEngineVersion = (EngineVersion)comboBox2.SelectedIndex;
            }
            catch { }
            try
            {
                ((Form1)Owner).exportPakVersion = (PakVersion)comboBox3.SelectedIndex;
            }
            catch { }
            Close();
        }

        private void button3_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog dialog = new()
            {
                Description = "选择工作目录",
                ShowNewFolderButton = true,
                UseDescriptionForTitle = true,
                AutoUpgradeEnabled = true
            };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                if (Directory.Exists(dialog.SelectedPath))
                {
                    if (Directory.GetDirectories(dialog.SelectedPath).Length != 0)
                    {
                        if (MessageBox.Show("警告：指定的文件夹不为空。\n确定要将其设为工作目录吗？\n（有误删文件的风险）", "警告", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.Cancel)
                        {
                            return;
                        }
                    }
                    textBox2.Text = dialog.SelectedPath;
                }
                else
                {
                    _ = MessageBox.Show("错误：指定的文件夹不存在。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void button4_Click(object sender, EventArgs e)
        {
            OpenFileDialog dialog = new()
            {
                Filter = "映射文件（*.usmap）|*.usmap|所有文件（*.*）|*.*",
                CheckFileExists = true,
                CheckPathExists = true,
                Multiselect = false,
                Title = "打开映射文件"
            };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                if (File.Exists(dialog.FileName)) textBox3.Text = dialog.FileName;
                else
                {
                    _ = MessageBox.Show("错误：指定的文件不存在。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void label7_Click(object sender, EventArgs e)
        {
            clickCount++;
            if (clickCount == 10)
            {
                try
                {
                    Form1 f = (Form1)Owner;
                    f.IsUserDebug = true;
                    label7.Text = "BPEditor " + f.Version + " " + (f.IsDebug ? "Debug" : (f.IsUserDebug ? "UserDebug" : "Release"));
                    if (f.IsUserDebug)
                    {
                        checkBox1.Visible = true;
                        f.blueprintDatajsonToolStripMenuItem.Visible = true;
                    }
                }
                catch { }
            }
        }
    }
}
