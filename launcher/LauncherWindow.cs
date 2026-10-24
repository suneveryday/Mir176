using System;
using System.Drawing;
using System.Windows.Forms;

namespace Mir176.Launcher
{
    public sealed class LauncherWindow : Form
    {
        public readonly TextBox AddressBox = new TextBox();
        public readonly TextBox PortBox = new TextBox();
        public readonly Button SaveButton = new Button();
        public readonly Button PlayButton = new Button();
        public readonly Label Status = new Label();
        private readonly LauncherService service;

        public LauncherWindow(LauncherService launcher)
        {
            service = launcher;
            Text = "Mir176 经典版登录器";
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(520, 346);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(246, 247, 249);
            AddLabel("连接你的经典服务器", 24, 22, 470, 30, 15F);
            AddLabel("填写服务器提供者给出的地址与登录端口。", 24, 58, 470, 24, 9F);
            AddLabel("服务器 IPv4 / 域名", 24, 102, 330, 24, 9F);
            AddLabel("登录端口", 382, 102, 112, 24, 9F);
            AddressBox.SetBounds(24, 132, 338, 30);
            AddressBox.AccessibleName = "服务器地址";
            AddressBox.MaxLength = 30;
            AddressBox.TabIndex = 0;
            PortBox.SetBounds(382, 132, 112, 30);
            PortBox.AccessibleName = "登录端口";
            PortBox.MaxLength = 5;
            PortBox.TabIndex = 1;
            AddLabel("仅保存连接设置；游戏账号和密码请在客户端内输入。", 24, 174, 470, 24, 9F);
            Status.SetBounds(24, 210, 470, 52);
            Status.AccessibleName = "连接设置状态";
            SaveButton.SetBounds(244, 282, 112, 36);
            SaveButton.Text = "保存设置";
            SaveButton.TabIndex = 2;
            PlayButton.SetBounds(370, 282, 124, 36);
            PlayButton.Text = "开始游戏";
            PlayButton.TabIndex = 3;
            PlayButton.BackColor = Color.FromArgb(38, 93, 72);
            PlayButton.ForeColor = Color.White;
            PlayButton.FlatStyle = FlatStyle.Flat;
            Controls.AddRange(new Control[] { AddressBox, PortBox, Status, SaveButton, PlayButton });
            AcceptButton = PlayButton;
            SaveButton.Click += delegate { Execute(false); };
            PlayButton.Click += delegate { Execute(true); };
            try
            {
                string[] current = ClientConfiguration.Load(service.DirectoryPath);
                AddressBox.Text = current[0]; PortBox.Text = current[1];
                Status.Text = current[0] == "" ? "首次使用：请输入服务器地址。" : "已读取本机设置，可以修改或直接开始游戏。";
            }
            catch (Exception)
            {
                PortBox.Text = "7000";
                ShowError("本机配置无法读取，请先检查文件权限与编码；原文件未修改。");
                SaveButton.Enabled = PlayButton.Enabled = false;
            }
        }

        private void AddLabel(string text, int x, int y, int width, int height, float size)
        {
            var label = new Label { Text = text, Font = new Font(Font.FontFamily, size) };
            label.SetBounds(x, y, width, height); Controls.Add(label);
        }

        private void ShowError(string text) { Status.ForeColor = Color.Firebrick; Status.Text = text; }

        private void Execute(bool launch)
        {
            try
            {
                service.Execute(AddressBox.Text, PortBox.Text, launch);
                Status.ForeColor = Color.FromArgb(38, 93, 72);
                Status.Text = launch ? "客户端已启动。请在游戏窗口中登录账号。" : "连接设置已保存。";
            }
            catch (ArgumentException error) { ShowError(error.Message); }
            catch (InvalidOperationException error) { ShowError(error.Message); }
            catch (Exception) { ShowError("保存或启动失败，请检查目录写入权限、配置和客户端完整性。"); }
        }
    }
}
