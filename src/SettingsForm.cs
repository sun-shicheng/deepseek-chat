using System;
using System.Drawing;
using System.Windows.Forms;

namespace DsChat
{
    /// <summary>设置窗口：接口地址、Key、模型、代理、生成参数，带"测试连接"。</summary>
    public class SettingsForm : Form
    {
        private Theme th;
        private Config cfg;

        private TextBox tBase, tPath, tKey, tModel, tProxy, tProxyUser, tProxyPass, tSystem;
        private NumericUpDown nTimeout, nTemp, nMaxTokens;
        private CheckBox cStream, cIgnoreCert, cShowKey;
        private Label lblStatus;
        private Button btnTest, btnSave, btnCancel;
        private bool testing;

        public Config Result;

        public SettingsForm(Config c, Theme theme)
        {
            cfg = c.Clone();
            th = theme;
            Text = "设置";
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(620, 664);
            BackColor = th.Bg;
            ForeColor = th.Text;
            Font = new Font("Microsoft YaHei UI", 9.75f);
            DoubleBuffered = true;
            KeyPreview = true;
            KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.F5) { DoTest(); e.SuppressKeyPress = true; }
            };

            BuildUi();
            if (Math.Abs(Ui.S - 1f) > 0.01f) Scale(new SizeF(Ui.S, Ui.S));
            LoadValues();
            ApplyTheme();
        }

        private void BuildUi()
        {
            int x = 26;
            int w = ClientSize.Width - 52;
            int colW = (w - 20) / 2;
            int x2 = x + colW + 20;
            int y = 18;

            Label title = new Label();
            title.Text = "连接设置";
            title.Font = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(x, y);
            Controls.Add(title);
            y += 46;

            Label sub = new Label();
            sub.Text = "配置保存在本机，API Key 使用 Windows 凭据加密。";
            sub.AutoSize = false;
            sub.Size = new Size(w, 20);
            sub.Location = new Point(x, y);
            Controls.Add(sub);
            y += 24;

            // 接口地址
            AddLabel("接口地址", x, y); y += 20;
            tBase = AddBox(x, y, w); y += 30;
            AddHint("例如 https://api.deepseek.com 或 http://10.0.0.5:8000", x, y); y += 24;

            // 路径 + 模型（左右两列）
            AddLabel("接口路径", x, y);
            AddLabel("模型名称", x2, y);
            y += 20;
            tPath = AddBox(x, y, colW);
            tModel = AddBox(x2, y, colW);
            y += 30;
            AddHint("默认 /v1/chat/completions，可填完整 URL", x, y);
            AddHint("例如 deepseek-chat", x2, y);
            y += 24;

            // API Key
            AddLabel("API Key", x, y); y += 20;
            tKey = AddBox(x, y, w - 84);
            tKey.UseSystemPasswordChar = true;
            cShowKey = new CheckBox();
            cShowKey.Text = "显示";
            cShowKey.Location = new Point(x + w - 76, y + 2);
            cShowKey.AutoSize = true;
            cShowKey.CheckedChanged += delegate { tKey.UseSystemPasswordChar = !cShowKey.Checked; };
            Controls.Add(cShowKey);
            y += 32;

            // 代理
            AddLabel("HTTP 代理（可留空）", x, y); y += 20;
            tProxy = AddBox(x, y, w); y += 30;
            AddHint("host:port，例如 127.0.0.1:7890", x, y); y += 22;

            AddLabel("代理账号（可留空）", x, y);
            AddLabel("代理密码（可留空）", x2, y);
            y += 20;
            tProxyUser = AddBox(x, y, colW);
            tProxyPass = AddBox(x2, y, colW);
            tProxyPass.UseSystemPasswordChar = true;
            y += 30;

            // 系统提示词
            AddLabel("系统提示词", x, y); y += 20;
            tSystem = new TextBox();
            tSystem.Location = new Point(x, y);
            tSystem.Size = new Size(w, 50);
            tSystem.Multiline = true;
            tSystem.ScrollBars = ScrollBars.Vertical;
            tSystem.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(tSystem);
            y += 56;

            // 数字参数
            nTimeout = AddNumber("超时（秒）", x, y, 90, 10, 600, 120, 1);
            nTemp = AddNumber("温度", x + 120, y, 90, 0, 2, 0.7m, 0.1m);
            nMaxTokens = AddNumber("最大 token（0=默认）", x + 260, y, 120, 0, 32000, 0, 100);
            y += 52;

            cStream = new CheckBox();
            cStream.Text = "流式输出（打字机效果）";
            cStream.AutoSize = true;
            cStream.Location = new Point(x, y);
            Controls.Add(cStream);

            cIgnoreCert = new CheckBox();
            cIgnoreCert.Text = "忽略 HTTPS 证书错误（内网自签名）";
            cIgnoreCert.AutoSize = true;
            cIgnoreCert.Location = new Point(x + 236, y);
            Controls.Add(cIgnoreCert);
            y += 30;

            btnTest = new Button();
            btnTest.Text = "测试连接";
            btnTest.Size = new Size(100, 32);
            btnTest.Location = new Point(x, y);
            btnTest.FlatStyle = FlatStyle.Flat;
            btnTest.Click += delegate { DoTest(); };
            Controls.Add(btnTest);

            btnSave = new Button();
            btnSave.Text = "保存";
            btnSave.Size = new Size(96, 32);
            btnSave.Location = new Point(ClientSize.Width - x - 96 - 104, y);
            btnSave.FlatStyle = FlatStyle.Flat;
            btnSave.Click += delegate { SaveAndClose(); };
            Controls.Add(btnSave);

            btnCancel = new Button();
            btnCancel.Text = "取消";
            btnCancel.Size = new Size(96, 32);
            btnCancel.Location = new Point(ClientSize.Width - x - 96, y);
            btnCancel.FlatStyle = FlatStyle.Flat;
            btnCancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(btnCancel);
            y += 38;

            lblStatus = new Label();
            lblStatus.AutoSize = false;
            lblStatus.Size = new Size(w, 40);
            lblStatus.Location = new Point(x, y);
            Controls.Add(lblStatus);
        }

        private Label AddLabel(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Location = new Point(x, y);
            Controls.Add(l);
            return l;
        }

        private Label AddHint(string text, int x, int y)
        {
            Label h = new Label();
            h.Text = text;
            h.AutoSize = true;
            h.ForeColor = th.SubText;
            h.Location = new Point(x, y);
            Controls.Add(h);
            return h;
        }

        private TextBox AddBox(int x, int y, int w)
        {
            TextBox t = new TextBox();
            t.Location = new Point(x, y);
            t.Size = new Size(w, 28);
            t.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(t);
            return t;
        }

        private NumericUpDown AddNumber(string label, int x, int y, int w, decimal min, decimal max, decimal val, decimal step)
        {
            AddLabel(label, x, y);
            NumericUpDown n = new NumericUpDown();
            n.Location = new Point(x, y + 22);
            n.Size = new Size(w, 26);
            n.Minimum = min;
            n.Maximum = max;
            n.Value = val;
            n.Increment = step;
            n.DecimalPlaces = step < 1 ? 1 : 0;
            n.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(n);
            return n;
        }

        private void LoadValues()
        {
            tBase.Text = cfg.BaseUrl;
            tPath.Text = cfg.ApiPath;
            tKey.Text = cfg.ApiKey;
            tModel.Text = cfg.Model;
            tProxy.Text = cfg.Proxy;
            tProxyUser.Text = cfg.ProxyUser;
            tProxyPass.Text = cfg.ProxyPass;
            tSystem.Text = cfg.SystemPrompt;
            cStream.Checked = cfg.Stream;
            cIgnoreCert.Checked = cfg.IgnoreCert;
            nTimeout.Value = Math.Min(600, Math.Max(10, cfg.TimeoutSeconds));
            nTemp.Value = (decimal)Math.Min(2, Math.Max(0, cfg.Temperature));
            nMaxTokens.Value = Math.Min(32000, Math.Max(0, cfg.MaxTokens));
        }

        private Config Collect()
        {
            Config c = cfg.Clone();
            c.BaseUrl = tBase.Text.Trim();
            c.ApiPath = tPath.Text.Trim();
            c.ApiKey = tKey.Text.Trim();
            c.Model = tModel.Text.Trim();
            c.Proxy = tProxy.Text.Trim();
            c.ProxyUser = tProxyUser.Text.Trim();
            c.ProxyPass = tProxyPass.Text;
            c.SystemPrompt = tSystem.Text;
            c.Stream = cStream.Checked;
            c.IgnoreCert = cIgnoreCert.Checked;
            c.TimeoutSeconds = (int)nTimeout.Value;
            c.Temperature = (double)nTemp.Value;
            c.MaxTokens = (int)nMaxTokens.Value;
            return c;
        }

        private void SaveAndClose()
        {
            Config c = Collect();
            if (c.BaseUrl.Length == 0)
            {
                MessageBox.Show(this, "请填写接口地址，例如 https://api.deepseek.com", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                tBase.Focus();
                return;
            }
            if (!c.BaseUrl.StartsWith("http://") && !c.BaseUrl.StartsWith("https://") && !c.ApiPath.StartsWith("http"))
            {
                MessageBox.Show(this, "接口地址需要以 http:// 或 https:// 开头", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                tBase.Focus();
                return;
            }
            Result = c;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void DoTest()
        {
            if (testing) return;
            testing = true;
            btnTest.Enabled = false;
            btnSave.Enabled = false;
            lblStatus.ForeColor = th.SubText;
            lblStatus.Text = "正在测试，请稍候…";

            Config c = Collect();
            c.Stream = false;
            c.TimeoutSeconds = Math.Min(30, c.TimeoutSeconds);

            ChatClient cli = new ChatClient(c);
            System.Text.StringBuilder got = new System.Text.StringBuilder();
            bool finished = false;

            Action finish = delegate ()
            {
                if (finished) return;
                finished = true;
                testing = false;
                if (IsDisposed) return;
                BeginInvoke((MethodInvoker)delegate
                {
                    btnTest.Enabled = true;
                    btnSave.Enabled = true;
                    if (got.Length > 0)
                    {
                        lblStatus.ForeColor = Color.FromArgb(46, 180, 120);
                        lblStatus.Text = "连接成功。模型回复：" + Preview(got.ToString());
                    }
                });
            };

            cli.OnDelta += delegate (string s) { got.Append(s); };
            cli.OnError += delegate (string err)
            {
                if (finished) return;
                finished = true;
                testing = false;
                if (IsDisposed) return;
                BeginInvoke((MethodInvoker)delegate
                {
                    btnTest.Enabled = true;
                    btnSave.Enabled = true;
                    lblStatus.ForeColor = th.Danger;
                    lblStatus.Text = "连接失败：" + Preview(err);
                });
            };
            cli.OnDone += delegate ()
            {
                finish();
                if (got.Length == 0 && !IsDisposed)
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        lblStatus.ForeColor = th.Danger;
                        lblStatus.Text = "已连上接口，但没有收到内容，请检查模型名称是否正确。";
                    });
                }
            };
            cli.Send(new System.Collections.Generic.List<ChatMsg> { new ChatMsg("user", "ping") });
        }

        private static string Preview(string s)
        {
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            if (s.Length > 90) s = s.Substring(0, 90) + "…";
            return s;
        }

        private void ApplyTheme()
        {
            BackColor = th.Bg;
            ForeColor = th.Text;
            foreach (Control c in Controls)
            {
                if (c is Label)
                {
                    Label l = (Label)c;
                    if (l.Font.Bold && l.Font.Size > 13) l.ForeColor = th.Text;
                    else if (l == lblStatus) { }
                    else l.ForeColor = th.SubText;
                }
                else if (c is TextBox)
                {
                    c.BackColor = th.InputBg;
                    c.ForeColor = th.Text;
                }
                else if (c is NumericUpDown)
                {
                    c.BackColor = th.InputBg;
                    c.ForeColor = th.Text;
                }
                else if (c is CheckBox)
                {
                    c.ForeColor = th.Text;
                    c.BackColor = th.Bg;
                }
                else if (c is Button)
                {
                    Button b = (Button)c;
                    b.FlatAppearance.BorderColor = th.Border;
                    b.BackColor = th.InputBg;
                    b.ForeColor = th.Text;
                }
            }
            btnSave.BackColor = th.Accent;
            btnSave.ForeColor = Color.White;
            btnSave.FlatAppearance.BorderColor = th.Accent;
            lblStatus.ForeColor = th.SubText;
        }
    }
}
