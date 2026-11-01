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
        private NumericUpDown nTimeout, nTemp, nMaxTokens, nContext;
        private CheckBox cStream, cIgnoreCert, cShowKey, cTrust, cConcise;
        private ComboBox cbTheme;
        private Label lblStatus;
        private RoundedButton btnTest, btnSave, btnCancel;
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
            ClientSize = new Size(620, 692);
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

            cbTheme.SelectedIndexChanged += delegate
            {
                th = Theme.Create(Theme.ResolveDark(cbTheme.SelectedIndex));
                ApplyTheme();
            };
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

            Label lTheme = new Label();
            lTheme.Text = "主题";
            lTheme.AutoSize = true;
            lTheme.Location = new Point(ClientSize.Width - 190, y + 4);
            Controls.Add(lTheme);

            cbTheme = new ComboBox();
            cbTheme.DropDownStyle = ComboBoxStyle.DropDownList;
            cbTheme.FlatStyle = FlatStyle.Flat;
            cbTheme.DrawMode = DrawMode.OwnerDrawFixed;
            cbTheme.ItemHeight = Ui.Px(22);
            cbTheme.DrawItem += ComboTheme_DrawItem;
            cbTheme.Items.AddRange(new object[] { "跟随系统", "浅色", "深色" });
            cbTheme.Location = new Point(ClientSize.Width - 150, y - 1);
            cbTheme.Size = new Size(124, 26);
            Controls.Add(cbTheme);
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
            AddHint("例如 https://api.openai.com 或 http://10.0.0.5:8000", x, y); y += 24;

            // 路径 + 模型（左右两列）
            AddLabel("接口路径", x, y);
            AddLabel("模型名称", x2, y);
            y += 20;
            tPath = AddBox(x, y, colW);
            tModel = AddBox(x2, y, colW);
            y += 30;
            AddHint("默认 /v1/chat/completions，可填完整 URL", x, y);
            AddHint("例如 gpt-4o-mini、qwen2.5-72b-instruct", x2, y);
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
            nTimeout = AddNumber("超时（秒）", x, y, 80, 10, 600, 120, 1);
            nTemp = AddNumber("温度", x + 100, y, 70, 0, 2, 0.7m, 0.1m);
            nMaxTokens = AddNumber("最大 token（0=默认）", x + 190, y, 90, 0, 32000, 0, 100);
            AddLabel("上下文 token（0=不限）", x + 370, y);
            nContext = AddNumberBox(x + 370, y + 22, 110, 0, 64000, 12000, 1000);
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

            cTrust = new CheckBox();
            cTrust.Text = "忽略系统/环境代理（内网直连）";
            cTrust.AutoSize = true;
            cTrust.Location = new Point(x, y);
            Controls.Add(cTrust);

            cConcise = new CheckBox();
            cConcise.Text = "简洁回答（省输出 token）";
            cConcise.AutoSize = true;
            cConcise.Location = new Point(x + 240, y);
            Controls.Add(cConcise);
            y += 30;

            btnTest = new RoundedButton();
            btnTest.Text = "测试连接";
            btnTest.Size = new Size(100, 32);
            btnTest.Location = new Point(x, y);
            btnTest.BackColor = th.InputBg;
            btnTest.ForeColor = th.Text;
            btnTest.BorderColor = th.Border;
            btnTest.Click += delegate { DoTest(); };
            Controls.Add(btnTest);

            btnSave = new RoundedButton();
            btnSave.Text = "保存";
            btnSave.Size = new Size(96, 32);
            btnSave.Location = new Point(ClientSize.Width - x - 96 - 104, y);
            btnSave.BackColor = th.Accent;
            btnSave.ForeColor = th.AccentText;
            btnSave.BorderColor = th.Accent;
            btnSave.Click += delegate { SaveAndClose(); };
            Controls.Add(btnSave);

            btnCancel = new RoundedButton();
            btnCancel.Text = "取消";
            btnCancel.Size = new Size(96, 32);
            btnCancel.Location = new Point(ClientSize.Width - x - 96, y);
            btnCancel.BackColor = th.InputBg;
            btnCancel.ForeColor = th.Text;
            btnCancel.BorderColor = th.Border;
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
            return AddNumberBox(x, y + 22, w, min, max, val, step);
        }

        private NumericUpDown AddNumberBox(int x, int y, int w, decimal min, decimal max, decimal val, decimal step)
        {
            NumericUpDown n = new NumericUpDown();
            n.Location = new Point(x, y);
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
            cTrust.Checked = !cfg.TrustEnv;
            nTimeout.Value = Math.Min(600, Math.Max(10, cfg.TimeoutSeconds));
            nTemp.Value = (decimal)Math.Min(2, Math.Max(0, cfg.Temperature));
            nMaxTokens.Value = Math.Min(32000, Math.Max(0, cfg.MaxTokens));
            nContext.Value = Math.Min(64000, Math.Max(0, cfg.ContextTokens));
            cConcise.Checked = cfg.Concise;
            cbTheme.SelectedIndex = cfg.ThemeMode < Theme.ModeSystem || cfg.ThemeMode > Theme.ModeDark
                ? Theme.ModeSystem : cfg.ThemeMode;
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
            c.TrustEnv = !cTrust.Checked;
            c.TimeoutSeconds = (int)nTimeout.Value;
            c.Temperature = (double)nTemp.Value;
            c.MaxTokens = (int)nMaxTokens.Value;
            c.ContextTokens = (int)nContext.Value;
            c.Concise = cConcise.Checked;
            c.ThemeMode = cbTheme.SelectedIndex < 0 ? Theme.ModeSystem : cbTheme.SelectedIndex;
            c.Dark = Theme.ResolveDark(c.ThemeMode);
            return c;
        }

        private void SaveAndClose()
        {
            Config c = Collect();
            if (c.BaseUrl.Length == 0)
            {
                MessageBox.Show(this, "请填写接口地址，例如 https://api.openai.com", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

        private void ComboTheme_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush b = new SolidBrush(sel ? th.SidebarActive : th.InputBg))
                e.Graphics.FillRectangle(b, e.Bounds);
            TextRenderer.DrawText(e.Graphics, Convert.ToString(cbTheme.Items[e.Index]), cbTheme.Font, e.Bounds,
                th.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
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
                    l.BackColor = th.Bg;
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
                else if (c is ComboBox)
                {
                    c.BackColor = th.InputBg;
                    c.ForeColor = th.Text;
                }
                else if (c is CheckBox)
                {
                    c.ForeColor = th.Text;
                    c.BackColor = th.Bg;
                }
                else if (c is RoundedButton)
                {
                    RoundedButton b = (RoundedButton)c;
                    b.BackColor = th.InputBg;
                    b.ForeColor = th.Text;
                    b.BorderColor = th.Border;
                }
            }
            btnSave.BackColor = th.Accent;
            btnSave.ForeColor = th.AccentText;
            btnSave.BorderColor = th.Accent;
            lblStatus.ForeColor = th.SubText;
            if (IsHandleCreated) Ui.ApplyTitleBarTheme(this, th.IsDark);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Ui.ApplyTitleBarTheme(this, th.IsDark);
        }
    }
}
