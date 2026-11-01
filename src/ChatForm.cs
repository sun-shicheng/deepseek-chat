using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace DsChat
{
    public class Session
    {
        public string Title = "新对话";
        public List<ChatMsg> Messages = new List<ChatMsg>();
    }

    /// <summary>圆角按钮（实心 / 幽灵两种）</summary>
    public class PillButton : Control
    {
        public Color FillColor = Color.FromArgb(77, 107, 254);
        public Color HoverColor = Color.FromArgb(101, 128, 255);
        public Color TextColor = Color.White;
        public Color BorderColor = Color.Transparent;
        public int Radius = 10;
        public bool Ghost;
        private bool hover;

        public PillButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Font = new Font("Microsoft YaHei UI", 9.75f);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Round.Path(r, Ui.Px(Radius)))
            {
                if (!Ghost)
                {
                    using (SolidBrush b = new SolidBrush(hover ? HoverColor : FillColor))
                        e.Graphics.FillPath(b, p);
                }
                else
                {
                    if (hover)
                    {
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(28, FillColor)))
                            e.Graphics.FillPath(b, p);
                    }
                    if (BorderColor != Color.Transparent)
                    {
                        using (Pen pen = new Pen(BorderColor, 1f))
                            e.Graphics.DrawPath(pen, p);
                    }
                }
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, r,
                Ghost ? TextColor : TextColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>侧边栏里的一个会话条目</summary>
    public class SessionItem : Control
    {
        public string Title = "新对话";
        public bool Selected;
        private bool hover;
        private Theme th;

        public SessionItem(Theme theme)
        {
            th = theme;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Height = Ui.Px(38);
            Font = new Font("Microsoft YaHei UI", 9.5f);
        }

        public void ApplyTheme(Theme theme) { th = theme; Invalidate(); }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Round.Path(r, Ui.Px(9)))
            {
                Color fill = Selected ? th.SidebarActive : (hover ? th.SidebarHover : th.Sidebar);
                using (SolidBrush b = new SolidBrush(fill))
                    e.Graphics.FillPath(b, p);
            }
            Rectangle textRect = new Rectangle(Ui.Px(12), 0, Width - Ui.Px(20), Height);
            TextRenderer.DrawText(e.Graphics, Title, Font, textRect,
                Selected ? th.Text : th.SubText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    public class ChatForm : Form, IMessageFilter
    {
        private Config cfg;
        private Theme th;
        private List<Session> sessions = new List<Session>();
        private int active = 0;

        private Panel sidebar, header, msgHost, composerBox;
        private Panel sessionList;
        private Panel main, composerWrap;
        private TextBox input;
        private PillButton btnSend, btnNew, btnTheme, btnSettings, btnFiles;
        private Label lblModel, lblStatus, lblHint;
        private List<MessageView> views = new List<MessageView>();

        private ChatClient client;
        private bool streaming;
        private int streamingIndex = -1;
        private StringBuilder pending = new StringBuilder();
        private bool dirty;
        private bool userStopped;
        private Timer tick;
        private bool layingOut;
        private bool atBottom = true;
        /// <summary>本轮对话里临时的 tool_calls / tool 结果消息（不写进会话文件）</summary>
        private List<ChatMsg> turnExtra = new List<ChatMsg>();
        private int toolRounds;
        private const int MaxToolRounds = 6;

        private const int SidebarWidth = 252;

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string app, string idlist);

        private void ApplyScrollbarTheme()
        {
            try
            {
                string theme = th.IsDark ? "DarkMode_Explorer" : null;
                if (msgHost != null && msgHost.IsHandleCreated) SetWindowTheme(msgHost.Handle, theme, null);
                if (sessionList != null && sessionList.IsHandleCreated) SetWindowTheme(sessionList.Handle, theme, null);
            }
            catch { }
        }

        public ChatForm(Config c)
        {
            cfg = c;
            th = Theme.Create(cfg.Dark);
            BuildUi();
            LoadSessions();
            ApplyThemeAll();
            UpdateStatusIdle();

            tick = new Timer();
            tick.Interval = 55;
            tick.Tick += delegate { if (dirty) { dirty = false; if (streaming) FlushStream(); } };
            tick.Start();
            Application.AddMessageFilter(this);
        }

        // ================= 鼠标滚轮 =================
        // 消息正文是 RichTextBox，滚轮事件会被子控件吃掉，导致消息区无法用滚轮滑动。
        // 这里在消息分发前截获 WM_MOUSEWHEEL：只要鼠标在消息区上方，就手动滚动 msgHost。
        public bool PreFilterMessage(ref Message m)
        {
            const int WM_MOUSEWHEEL = 0x020A;
            if (m.Msg != WM_MOUSEWHEEL) return false;
            if (Form.ActiveForm != this) return false;
            if (msgHost == null || !msgHost.IsHandleCreated) return false;

            if (!msgHost.ClientRectangle.Contains(msgHost.PointToClient(Cursor.Position))) return false;

            long wp = m.WParam.ToInt64();
            short delta = (short)((wp >> 16) & 0xFFFF);
            if (delta == 0) return false;
            ScrollMessagesByWheel(delta);
            return true;
        }

        private void ScrollMessagesByWheel(int delta)
        {
            int max = Math.Max(0, msgHost.AutoScrollMinSize.Height - msgHost.ClientSize.Height);
            if (max <= 0) return;

            int lines = SystemInformation.MouseWheelScrollLines;
            int step = lines < 0 ? msgHost.ClientSize.Height : Ui.Px(18) * Math.Max(1, lines);

            int current = -msgHost.AutoScrollPosition.Y;
            int target = current - (int)Math.Round(delta / 120.0 * step);
            if (target < 0) target = 0;
            if (target > max) target = max;

            msgHost.AutoScrollPosition = new Point(0, target);
            atBottom = target >= max - Ui.Px(48);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try { input.Focus(); } catch { }
            ApplyScrollbarTheme();
            LayoutMessages(false);
            ScrollToBottom();
        }

        // ================= 界面构建 =================
        private void BuildUi()
        {
            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;
            Text = "DeepSeek Chat · 本地客户端";
            Font = new Font("Microsoft YaHei UI", 9.75f);
            ClientSize = new Size(1180, 780);
            MinimumSize = new Size(880, 600);
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            KeyPreview = true;

            sidebar = new Panel();
            sidebar.SetBounds(0, 0, SidebarWidth, ClientSize.Height);
            Controls.Add(sidebar);

            main = new Panel();
            main.SetBounds(SidebarWidth, 0, ClientSize.Width - SidebarWidth, ClientSize.Height);
            Controls.Add(main);

            // ---- 侧边栏 ----
            Label brand = new Label();
            brand.Text = "deepseek";
            brand.Font = new Font("Segoe UI Semibold", 16f, FontStyle.Bold);
            brand.AutoSize = true;
            brand.Location = new Point(22, 22);
            sidebar.Controls.Add(brand);

            Label brandSub = new Label();
            brandSub.Text = "本地客户端";
            brandSub.Font = new Font("Microsoft YaHei UI", 8.5f);
            brandSub.AutoSize = true;
            brandSub.Location = new Point(24, 52);
            sidebar.Controls.Add(brandSub);

            btnNew = new PillButton();
            btnNew.Text = "＋   新对话";
            btnNew.Height = 40;
            btnNew.Location = new Point(16, 84);
            btnNew.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            btnNew.Width = SidebarWidth - 32;
            btnNew.Radius = 11;
            btnNew.Click += delegate { NewSession(); };
            sidebar.Controls.Add(btnNew);

            Label histTitle = new Label();
            histTitle.Text = "历史对话";
            histTitle.Font = new Font("Microsoft YaHei UI", 8.5f);
            histTitle.AutoSize = true;
            histTitle.Location = new Point(22, 140);
            sidebar.Controls.Add(histTitle);

            sessionList = new Panel();
            sessionList.Location = new Point(10, 162);
            sessionList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            sessionList.Size = new Size(SidebarWidth - 20, ClientSize.Height - 162 - 110);
            sessionList.AutoScroll = true;
            sidebar.Controls.Add(sessionList);

            btnTheme = new PillButton();
            btnTheme.Text = "◐  切换主题";
            btnTheme.Ghost = true;
            btnTheme.Height = 34;
            btnTheme.Radius = 9;
            btnTheme.Font = new Font("Microsoft YaHei UI", 9f);
            btnTheme.Location = new Point(16, ClientSize.Height - 92);
            btnTheme.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            btnTheme.Width = 104;
            btnTheme.Click += delegate { ToggleTheme(); };
            sidebar.Controls.Add(btnTheme);

            btnSettings = new PillButton();
            btnSettings.Text = "⚙  设置";
            btnSettings.Ghost = true;
            btnSettings.Height = 34;
            btnSettings.Radius = 9;
            btnSettings.Font = new Font("Microsoft YaHei UI", 9f);
            btnSettings.Location = new Point(126, ClientSize.Height - 92);
            btnSettings.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            btnSettings.Width = 104;
            btnSettings.Click += delegate { OpenSettings(); };
            sidebar.Controls.Add(btnSettings);

            // ---- 主区 ----
            header = new Panel();
            header.SetBounds(0, 0, main.ClientSize.Width, 56);
            main.Controls.Add(header);

            lblModel = new Label();
            lblModel.Text = cfg.Model;
            lblModel.Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
            lblModel.AutoSize = true;
            lblModel.Location = new Point(26, 18);
            header.Controls.Add(lblModel);

            lblStatus = new Label();
            lblStatus.Text = "● 未配置";
            lblStatus.Font = new Font("Microsoft YaHei UI", 9f);
            lblStatus.AutoSize = true;
            lblStatus.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            lblStatus.TextAlign = ContentAlignment.MiddleRight;
            header.Controls.Add(lblStatus);

            btnFiles = new PillButton();
            btnFiles.Text = "📁  工作区";
            btnFiles.Ghost = true;
            btnFiles.Height = 34;
            btnFiles.Radius = 9;
            btnFiles.Font = new Font("Microsoft YaHei UI", 9f);
            btnFiles.Width = 116;
            btnFiles.Click += delegate { OpenWorkspace(); };
            header.Controls.Add(btnFiles);

            msgHost = new Panel();
            msgHost.SetBounds(0, 56, main.ClientSize.Width, Math.Max(0, main.ClientSize.Height - 56 - 104));
            msgHost.AutoScroll = true;
            main.Controls.Add(msgHost);
            msgHost.Resize += delegate { if (!layingOut) LayoutMessages(true); };
            msgHost.Scroll += delegate
            {
                if (layingOut) return;
                atBottom = msgHost.VerticalScroll.Value >= msgHost.VerticalScroll.Maximum - Ui.Px(48);
            };

            composerWrap = new Panel();
            composerWrap.SetBounds(0, Math.Max(56, main.ClientSize.Height - 104), main.ClientSize.Width, 104);
            main.Controls.Add(composerWrap);

            composerBox = new Panel();
            composerBox.SetBounds(26, 10, Math.Max(200, composerWrap.ClientSize.Width - 52), 64);
            composerWrap.Controls.Add(composerBox);
            composerBox.Paint += ComposerBox_Paint;
            composerBox.Resize += delegate { LayoutComposer(); };

            input = new TextBox();
            input.Multiline = true;
            input.BorderStyle = BorderStyle.None;
            input.Font = new Font("Microsoft YaHei UI", 10.5f);
            input.BackColor = th.InputBg;
            input.ForeColor = th.Text;
            input.ScrollBars = ScrollBars.None;
            input.KeyDown += Input_KeyDown;
            composerBox.Controls.Add(input);

            btnSend = new PillButton();
            btnSend.Text = "发送";
            btnSend.Size = new Size(76, 38);
            btnSend.Radius = 10;
            btnSend.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
            btnSend.Click += delegate { OnSendClicked(); };
            composerBox.Controls.Add(btnSend);

            lblHint = new Label();
            lblHint.Text = "Enter 发送 · Shift+Enter 换行 · Ctrl+N 新对话 · Ctrl+O 工作区 · Ctrl+T 主题 · Ctrl+, 设置";
            lblHint.Font = new Font("Microsoft YaHei UI", 8.5f);
            lblHint.AutoSize = true;
            lblHint.Location = new Point(30, 78);
            composerWrap.Controls.Add(lblHint);

            KeyDown += ChatForm_KeyDown;
            Resize += delegate { LayoutAll(); };

            // 按屏幕 DPI 整体放大（字体按磅值自动跟随 DPI，这里放大像素尺寸）
            if (Math.Abs(Ui.S - 1f) > 0.01f) Scale(new SizeF(Ui.S, Ui.S));
            MinimumSize = new Size(Ui.Px(900), Ui.Px(620));

            LayoutAll();
            ResumeLayout();
        }

        /// <summary>手动布局，避免 Dock 顺序在不同 DPI/尺寸下的歧义。</summary>
        private void LayoutAll()
        {
            if (sidebar == null || main == null) return;
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            int sideW = Ui.Px(SidebarWidth);
            int headerH = Ui.Px(56);
            int composerH = Ui.Px(104);

            sidebar.SetBounds(0, 0, sideW, h);
            main.SetBounds(sideW, 0, Math.Max(Ui.Px(200), w - sideW), h);

            int mw = main.ClientSize.Width;
            int mh = main.ClientSize.Height;
            header.SetBounds(0, 0, mw, headerH);
            composerWrap.SetBounds(0, Math.Max(headerH, mh - composerH), mw, composerH);
            msgHost.SetBounds(0, headerH, mw, Math.Max(0, mh - headerH - composerH));

            LayoutHeader();
            LayoutComposer();
        }

        private void LayoutHeader()
        {
            if (lblStatus == null || header == null) return;
            lblStatus.Location = new Point(Math.Max(Ui.Px(200), header.Width - lblStatus.Width - Ui.Px(28)), Ui.Px(19));
            if (btnFiles != null)
            {
                int x = Math.Max(Ui.Px(200), lblStatus.Left - btnFiles.Width - Ui.Px(16));
                btnFiles.Location = new Point(x, Ui.Px(11));
            }
        }

        private void LayoutComposer()
        {
            if (composerBox == null || composerWrap == null) return;
            composerBox.Width = Math.Max(Ui.Px(200), composerWrap.ClientSize.Width - Ui.Px(52));
            btnSend.Location = new Point(composerBox.Width - btnSend.Width - Ui.Px(12), (composerBox.Height - btnSend.Height) / 2);
            input.SetBounds(Ui.Px(18), Ui.Px(14), Math.Max(Ui.Px(80), composerBox.Width - btnSend.Width - Ui.Px(54)), composerBox.Height - Ui.Px(28));
            using (GraphicsPath p = Round.Path(new Rectangle(0, 0, composerBox.Width - 1, composerBox.Height - 1), Ui.Px(16)))
                composerBox.Region = new Region(p);
        }

        private void ComposerBox_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, composerBox.Width - 1, composerBox.Height - 1);
            using (GraphicsPath p = Round.Path(r, 16))
            {
                using (SolidBrush b = new SolidBrush(th.InputBg)) e.Graphics.FillPath(b, p);
                using (Pen pen = new Pen(th.Border, 1f)) e.Graphics.DrawPath(pen, p);
            }
        }

        // ================= 主题 =================
        private void ToggleTheme()
        {
            cfg.Dark = !cfg.Dark;
            th = Theme.Create(cfg.Dark);
            ApplyThemeAll();
            cfg.Save();
        }

        private void ApplyThemeAll()
        {
            BackColor = th.Bg;
            sidebar.BackColor = th.Sidebar;
            header.BackColor = th.Bg;
            msgHost.BackColor = th.Bg;
            input.BackColor = th.InputBg;
            input.ForeColor = th.Text;
            lblHint.ForeColor = th.SubText;
            lblModel.ForeColor = th.Text;
            lblStatus.ForeColor = th.SubText;
            foreach (Control c in sidebar.Controls)
            {
                if (c is Label) ((Label)c).ForeColor = ((Label)c).Text == "deepseek" ? th.Text : th.SubText;
            }
            foreach (Control c in sessionList.Controls)
            {
                SessionItem si = c as SessionItem;
                if (si != null) si.ApplyTheme(th);
            }
            foreach (Control c in header.Controls) if (c is Label) c.ForeColor = (c == lblModel) ? th.Text : th.SubText;

            btnNew.FillColor = th.Accent;
            btnNew.HoverColor = th.AccentHover;
            btnNew.Invalidate();
            btnTheme.TextColor = th.SubText;
            btnTheme.FillColor = th.Accent;
            btnTheme.BorderColor = th.Border;
            btnTheme.Invalidate();
            btnSettings.TextColor = th.SubText;
            btnSettings.FillColor = th.Accent;
            btnSettings.BorderColor = th.Border;
            btnSettings.Invalidate();
            btnFiles.TextColor = th.SubText;
            btnFiles.FillColor = th.Accent;
            btnFiles.BorderColor = th.Border;
            btnFiles.Invalidate();
            btnSend.FillColor = th.Accent;
            btnSend.HoverColor = th.AccentHover;
            btnSend.Invalidate();

            foreach (MessageView v in views) v.ApplyTheme(th);
            LayoutMessages(false);
            composerBox.Invalidate();
            ApplyScrollbarTheme();
            Invalidate(true);
        }

        // ================= 会话 =================
        private void LoadSessions()
        {
            sessions = SessionStore.Load();
            if (sessions == null || sessions.Count == 0)
            {
                sessions = new List<Session>();
                sessions.Add(new Session());
            }
            active = sessions.Count - 1;
            RebuildSessionList();
            SelectSession(active);
        }

        private void RebuildSessionList()
        {
            sessionList.SuspendLayout();
            foreach (Control c in new List<Control>(GetSessionItems())) sessionList.Controls.Remove(c);
            int y = 0;
            for (int i = 0; i < sessions.Count; i++)
            {
                SessionItem it = new SessionItem(th);
                it.Title = sessions[i].Title;
                it.Selected = (i == active);
                it.Width = sessionList.ClientSize.Width - 2;
                it.Location = new Point(0, y);
                it.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                int idx = i;
                it.Click += delegate { SelectSession(idx); };
                it.MouseUp += delegate (object s, MouseEventArgs me)
                {
                    if (me.Button == MouseButtons.Right)
                    {
                        ContextMenuStrip menu = new ContextMenuStrip();
                        menu.Items.Add("删除该对话", null, delegate
                        {
                            if (MessageBox.Show(this, "确定删除该对话？", "删除对话", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                                DeleteSession(idx);
                        });
                        menu.Show(it, me.Location);
                    }
                };
                sessionList.Controls.Add(it);
                y += it.Height + 4;
            }
            sessionList.ResumeLayout();
        }

        private List<Control> GetSessionItems()
        {
            List<Control> list = new List<Control>();
            foreach (Control c in sessionList.Controls) if (c is SessionItem) list.Add(c);
            return list;
        }

        private void NewSession()
        {
            if (streaming) return;
            sessions.Add(new Session());
            active = sessions.Count - 1;
            RebuildSessionList();
            SelectSession(active);
            SaveSessions();
            input.Focus();
        }

        private void DeleteSession(int idx)
        {
            if (streaming || idx < 0 || idx >= sessions.Count) return;
            sessions.RemoveAt(idx);
            if (sessions.Count == 0) sessions.Add(new Session());
            active = Math.Min(active, sessions.Count - 1);
            if (active < 0) active = 0;
            RebuildSessionList();
            SelectSession(active);
            SaveSessions();
        }

        private void SelectSession(int idx)
        {
            if (streaming) return;
            if (idx < 0 || idx >= sessions.Count) return;
            active = idx;
            foreach (Control c in sessionList.Controls)
            {
                SessionItem it = c as SessionItem;
                if (it != null)
                {
                    it.Selected = (sessionList.Controls.GetChildIndex(it) == idx);
                    it.Invalidate();
                }
            }
            RenderSession();
            UpdateModelLabel();
        }

        private void UpdateModelLabel()
        {
            lblModel.Text = cfg.Model.Length == 0 ? "未设置模型" : cfg.Model;
            LayoutHeader();
        }

        private void RenderSession()
        {
            msgHost.SuspendLayout();
            foreach (MessageView v in views) { msgHost.Controls.Remove(v); v.Dispose(); }
            views.Clear();

            Session s = sessions[active];
            if (s.Messages.Count == 0)
            {
                ShowWelcome();
            }
            else
            {
                foreach (ChatMsg m in s.Messages)
                {
                    AddView(m, m.Role == "user");
                }
            }
            msgHost.ResumeLayout();
            LayoutMessages(false);
            ScrollToBottom();
        }

        private void ShowWelcome()
        {
            Label tip = new Label();
            tip.Name = "welcome";
            tip.AutoSize = false;
            tip.TextAlign = ContentAlignment.MiddleCenter;
            tip.Font = new Font("Microsoft YaHei UI", 10f);
            tip.ForeColor = th.SubText;
            tip.Text = "开始一段新对话\r\n\r\n输入问题后按 Enter 发送";
            tip.Size = new Size(Math.Max(Ui.Px(320), msgHost.ClientSize.Width - Ui.Px(120)), Ui.Px(120));
            tip.Location = new Point(Ui.Px(60), Ui.Px(80));
            msgHost.Controls.Add(tip);
        }

        private void ClearWelcome()
        {
            for (int i = msgHost.Controls.Count - 1; i >= 0; i--)
            {
                Control c = msgHost.Controls[i];
                if (c.Name == "welcome") { msgHost.Controls.Remove(c); c.Dispose(); }
            }
        }

        private MessageView AddView(ChatMsg m, bool isUser)
        {
            ClearWelcome();
            MessageView v = new MessageView(m, isUser, th);
            msgHost.Controls.Add(v);
            views.Add(v);
            v.Msg = m;
            return v;
        }

        private void LayoutMessages(bool keepScroll)
        {
            if (msgHost == null || layingOut) return;
            layingOut = true;
            try
            {
                ClearWelcomeIfNeeded();

                // AutoScroll 面板滚动后，子控件的 Location 会被系统加上滚动偏移。
                // 直接按内容坐标重新布局会把偏移算两遍，出现大片空白/滚动条乱跳，
                // 所以先回到顶部布局，再恢复原来的滚动位置。
                bool wasAtBottom = atBottom;
                int oldScroll = -msgHost.AutoScrollPosition.Y;
                if (oldScroll != 0) msgHost.AutoScrollPosition = new Point(0, 0);

                int pad = Ui.Px(26);
                int scrollW = msgHost.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0;
                int maxW = Math.Max(Ui.Px(240), msgHost.ClientSize.Width - pad * 2 - scrollW);

                int y = Ui.Px(18);
                foreach (MessageView v in views)
                {
                    int h = v.Reflow(maxW);
                    int x = v.IsUser ? pad + Math.Max(0, maxW - v.Width) : pad;
                    v.Location = new Point(x, y);
                    y += h + Ui.Px(18);
                }
                msgHost.AutoScrollMinSize = new Size(0, y + Ui.Px(8));

                // 欢迎标语居中
                foreach (Control c in msgHost.Controls)
                {
                    if (c.Name == "welcome")
                        c.Location = new Point(Ui.Px(60), Math.Max(Ui.Px(40), (msgHost.ClientSize.Height - c.Height) / 2 - Ui.Px(40)));
                }

                int max = Math.Max(0, msgHost.AutoScrollMinSize.Height - msgHost.ClientSize.Height);
                int target = (keepScroll && wasAtBottom) ? max : Math.Min(oldScroll, max);
                if (target < 0) target = 0;
                if (-msgHost.AutoScrollPosition.Y != target) msgHost.AutoScrollPosition = new Point(0, target);
                atBottom = max <= 0 || target >= max - Ui.Px(48);
            }
            finally { layingOut = false; }
        }

        private void ClearWelcomeIfNeeded()
        {
            if (views.Count > 0) ClearWelcome();
        }

        private void ScrollToBottom()
        {
            atBottom = true;
            if (msgHost == null || !msgHost.IsHandleCreated) return;
            int max = Math.Max(0, msgHost.AutoScrollMinSize.Height - msgHost.ClientSize.Height);
            msgHost.AutoScrollPosition = new Point(0, max);
        }

        // ================= 发送 =================
        private void Input_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true;
                OnSendClicked();
            }
        }

        private void ChatForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.N) { NewSession(); e.SuppressKeyPress = true; }
            else if (e.Control && e.KeyCode == Keys.O) { OpenWorkspace(); e.SuppressKeyPress = true; }
            else if (e.Control && e.KeyCode == Keys.Oemcomma) { OpenSettings(); e.SuppressKeyPress = true; }
            else if (e.Control && e.KeyCode == Keys.T) { ToggleTheme(); e.SuppressKeyPress = true; }
        }

        private void OnSendClicked()
        {
            if (streaming) { StopStreaming(); return; }
            SendCurrent();
        }

        private void SendCurrent()
        {
            string text = input.Text.Trim();
            if (text.Length == 0) return;

            Session s = sessions[active];
            ChatMsg userMsg = new ChatMsg("user", text);
            s.Messages.Add(userMsg);
            if (s.Title == "新对话" || s.Title.Length == 0)
            {
                string t = text.Replace("\r", " ").Replace("\n", " ");
                s.Title = t.Length > 18 ? t.Substring(0, 18) + "…" : t;
                RebuildSessionList();
            }

            input.Clear();
            AddView(userMsg, true);
            LayoutMessages(true);
            ScrollToBottom();

            turnExtra.Clear();
            toolRounds = 0;
            userStopped = false;
            StartRequest();
        }

        /// <summary>发起一轮请求（首轮和工具回填后都走这里）。</summary>
        private void StartRequest()
        {
            ChatMsg botMsg = new ChatMsg("assistant", "");
            AddView(botMsg, false);
            streamingIndex = views.Count - 1;
            pending.Length = 0;
            dirty = false;
            streaming = true;
            atBottom = true;
            SetBusyUi(true);
            LayoutMessages(true);
            ScrollToBottom();

            ChatClient cli = new ChatClient(cfg);
            client = cli;

            cli.OnDelta += delegate (string d)
            {
                pending.Append(d);
                dirty = true;
            };
            cli.OnError += delegate (string err)
            {
                SafeInvoke(delegate
                {
                    streaming = false;
                    SetBusyUi(false);
                    string msg = err == null ? "未知错误" : err.Trim();
                    if (msg.Length > 600) msg = msg.Substring(0, 600) + "…";
                    MessageView ev = AddView(new ChatMsg("assistant", "请求失败：" + msg), false);
                    ev.SetError();
                    LayoutMessages(true);
                    ScrollToBottom();
                    SetStatus("● 连接失败", false);
                    client = null;
                });
            };
            cli.OnDone += delegate ()
            {
                SafeInvoke(delegate
                {
                    streaming = false;
                    dirty = false;
                    if (!userStopped && cfg.EnableTools && cli.ToolCalls.Count > 0 && toolRounds < MaxToolRounds)
                    {
                        RunTools(cli);
                        return;
                    }
                    FinishTurn();
                });
            };

            cli.Send(BuildPayload());
        }

        private List<ChatMsg> BuildPayload()
        {
            Session s = sessions[active];
            List<ChatMsg> payload = new List<ChatMsg>();
            int start = Math.Max(0, s.Messages.Count - 20);
            for (int i = start; i < s.Messages.Count; i++) payload.Add(s.Messages[i]);
            payload.AddRange(turnExtra);
            return payload;
        }

        private void FinishTurn()
        {
            Session s = sessions[active];
            SetBusyUi(false);
            FlushStream();
            string content = pending.ToString();
            if (content.Trim().Length == 0)
            {
                if (userStopped)
                {
                    RemoveStreamingView();
                }
                else
                {
                    string hint = toolRounds > 0
                        ? "（本轮进行了 " + toolRounds + " 轮工具调用，但模型没有返回文字结果；可以再发一句「继续」，或检查工作区设置）"
                        : "（没有收到内容，请检查接口路径、模型名称或代理设置）";
                    MessageView ev = AddView(new ChatMsg("assistant", hint), false);
                    ev.SetError();
                }
            }
            else
            {
                s.Messages.Add(new ChatMsg("assistant", content));
            }
            pending.Length = 0;
            turnExtra.Clear();
            LayoutMessages(true);
            ScrollToBottom();
            SetStatus(userStopped ? "● 已停止" : "● 已连接", !userStopped);
            SaveSessions();
            client = null;
        }

        // ================= 工作区工具 =================
        private void RunTools(ChatClient cli)
        {
            toolRounds++;
            string partial = pending.ToString();
            List<ToolCall> calls = cli.ToolCalls;

            if (partial.Trim().Length == 0) RemoveStreamingView();
            pending.Length = 0;

            ChatMsg asst = new ChatMsg("assistant", partial);
            asst.ToolCalls = calls;
            turnExtra.Add(asst);

            foreach (ToolCall tc in calls)
            {
                string summary;
                string result = ExecuteTool(tc, out summary);
                ChatMsg tool = new ChatMsg("tool", result);
                tool.ToolCallId = tc.Id;
                turnExtra.Add(tool);
                MessageView v = AddView(new ChatMsg("assistant", summary), false);
                v.SetTool();
            }

            LayoutMessages(true);
            ScrollToBottom();
            if (userStopped) { FinishTurn(); return; }
            StartRequest();
        }

        private string ExecuteTool(ToolCall tc, out string summary)
        {
            summary = "🔧 调用工具：" + tc.Name;
            try
            {
                string name = tc.Name == null ? "" : tc.Name.Trim();
                if (name == "list_files")
                {
                    string sub = ArgOf(tc.Arguments, "subdir");
                    string text = Workspace.ListFiles(cfg.Workspace, sub);
                    summary = "🔧 列出工作区" + (string.IsNullOrEmpty(sub) ? "" : " / " + sub);
                    return text;
                }
                if (name == "read_file")
                {
                    string rel = ArgOf(tc.Arguments, "path");
                    string text = Workspace.ReadText(cfg.Workspace, rel);
                    summary = "🔧 读取 " + rel + "（" + Workspace.FormatSize(text.Length) + "）";
                    return text;
                }
                if (name == "write_file")
                {
                    string rel = ArgOf(tc.Arguments, "path");
                    string content = ArgOf(tc.Arguments, "content");
                    if (cfg.ConfirmWrites && !ConfirmWrite(rel, content)) return "用户拒绝了这次写入，请不要重复尝试。";
                    int n = Workspace.WriteText(cfg.Workspace, rel, content);
                    summary = "🔧 写入 " + rel + "（" + n + " 字符）";
                    return "写入成功：" + rel + "，" + n + " 个字符。";
                }
                if (name == "delete_file")
                {
                    string rel = ArgOf(tc.Arguments, "path");
                    if (cfg.ConfirmWrites && !ConfirmDelete(rel)) return "用户拒绝了这次删除，请不要重复尝试。";
                    Workspace.Delete(cfg.Workspace, rel);
                    summary = "🔧 删除 " + rel;
                    return "已删除：" + rel;
                }
                return "未知工具：" + name;
            }
            catch (Exception ex)
            {
                summary = "⚠ 工具执行失败：" + tc.Name;
                return "执行失败：" + ex.Message;
            }
        }

        private bool ConfirmWrite(string rel, string content)
        {
            string preview = content == null ? "" : content.Replace("\r", "");
            if (preview.Length > 400) preview = preview.Substring(0, 400) + "\n…";
            string msg = "AI 想写入文件：\n" + rel + "\n\n预览：\n" + preview + "\n\n允许写入吗？";
            return MessageBox.Show(this, msg, "确认写入", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        private bool ConfirmDelete(string rel)
        {
            string msg = "AI 想删除：" + rel + "\n\n这个操作不可撤销，允许吗？";
            return MessageBox.Show(this, msg, "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
        }

        /// <summary>从工具调用的 JSON 参数里取字符串字段。</summary>
        private static string ArgOf(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return "";
            try
            {
                System.Web.Script.Serialization.JavaScriptSerializer js = new System.Web.Script.Serialization.JavaScriptSerializer();
                Dictionary<string, object> d = js.DeserializeObject(json) as Dictionary<string, object>;
                if (d != null)
                {
                    object v;
                    if (d.TryGetValue(key, out v) && v != null) return Convert.ToString(v);
                }
            }
            catch { }
            return "";
        }

        private void StopStreaming()
        {
            userStopped = true;
            if (client != null) client.Cancel();
            SetBusyUi(false);
            streaming = false;
            SetStatus("● 已停止", false);
        }

        private void RemoveStreamingView()
        {
            if (streamingIndex < 0 || streamingIndex >= views.Count) return;
            MessageView v = views[streamingIndex];
            views.RemoveAt(streamingIndex);
            msgHost.Controls.Remove(v);
            v.Dispose();
            streamingIndex = -1;
        }

        private void FlushStream()
        {
            if (streamingIndex < 0 || streamingIndex >= views.Count) return;
            MessageView v = views[streamingIndex];
            v.SetText(pending.ToString());
            bool nearBottom = atBottom;
            LayoutMessages(nearBottom);
            if (nearBottom) ScrollToBottom();
        }

        private void SetBusyUi(bool busy)
        {
            btnSend.Text = busy ? "停止" : "发送";
            btnSend.FillColor = busy ? th.Danger : th.Accent;
            btnSend.HoverColor = busy ? Color.FromArgb(220, 90, 90) : th.AccentHover;
            btnSend.Invalidate();
            if (busy) SetStatus("● 生成中…", false);
            else UpdateStatusIdle();
        }

        private void SetStatus(string text, bool ok)
        {
            lblStatus.Text = text;
            Color c = ok ? Color.FromArgb(46, 180, 120) : th.SubText;
            if (!ok && text.Contains("失败")) c = th.Danger;
            lblStatus.ForeColor = c;
            LayoutHeader();
        }

        private void UpdateStatusIdle()
        {
            if (cfg.ApiKey.Length == 0) SetStatus("● 未配置 Key", false);
            else SetStatus("● 就绪", true);
        }

        private void SafeInvoke(MethodInvoker a)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(a); } catch { }
        }

        // ================= 设置 =================
        private void OpenSettings()
        {
            if (streaming) return;
            using (SettingsForm f = new SettingsForm(cfg, th))
            {
                if (f.ShowDialog(this) == DialogResult.OK && f.Result != null)
                {
                    cfg = f.Result;
                    cfg.Save();
                    th = Theme.Create(cfg.Dark);
                    ApplyThemeAll();
                    UpdateModelLabel();
                    UpdateStatusIdle();
                }
            }
        }

        // ================= 工作区 =================
        private void OpenWorkspace()
        {
            using (WorkspaceForm f = new WorkspaceForm(cfg, th, LastAssistantText()))
            {
                f.OnInsert = delegate (string text)
                {
                    if (string.IsNullOrEmpty(text)) return;
                    if (input.TextLength > 0) input.AppendText("\r\n\r\n");
                    input.AppendText(text);
                    input.SelectionStart = input.TextLength;
                    input.Focus();
                };
                f.OnChanged = delegate (Config updated)
                {
                    if (updated == null) return;
                    cfg.Workspace = updated.Workspace;
                    cfg.EnableTools = updated.EnableTools;
                    cfg.ConfirmWrites = updated.ConfirmWrites;
                    cfg.Save();
                    UpdateStatusIdle();
                };
                f.ShowDialog(this);
            }
        }

        private string LastAssistantText()
        {
            Session s = sessions[active];
            for (int i = s.Messages.Count - 1; i >= 0; i--)
            {
                ChatMsg m = s.Messages[i];
                if (m.Role == "assistant" && m.Content != null && m.Content.Trim().Length > 0) return m.Content;
            }
            return "";
        }

        // ================= 持久化 =================
        private void SaveSessions()
        {
            SessionStore.Save(sessions);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                if (client != null) client.Cancel();
                cfg.Save();
                SaveSessions();
            }
            catch { }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { Application.RemoveMessageFilter(this); } catch { }
            base.OnFormClosed(e);
        }
    }

    /// <summary>会话落盘：每行 B(会话) / M(消息)，字段用 Tab 分隔，正文 Base64，避免转义问题。</summary>
    public static class SessionStore
    {
        public static void Save(List<Session> sessions)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                foreach (Session s in sessions)
                {
                    sb.Append("B\t").Append(B64(s.Title)).Append('\n');
                    foreach (ChatMsg m in s.Messages)
                        sb.Append("M\t").Append(m.Role).Append('\t').Append(B64(m.Content)).Append('\n');
                }
                File.WriteAllText(Config.SessionPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        public static List<Session> Load()
        {
            List<Session> list = new List<Session>();
            try
            {
                if (!File.Exists(Config.SessionPath)) return list;
                string[] lines = File.ReadAllLines(Config.SessionPath, Encoding.UTF8);
                Session cur = null;
                foreach (string line in lines)
                {
                    if (line.StartsWith("B\t"))
                    {
                        cur = new Session();
                        cur.Title = UnB64(line.Substring(2));
                        list.Add(cur);
                    }
                    else if (line.StartsWith("M\t") && cur != null)
                    {
                        string[] parts = line.Split('\t');
                        if (parts.Length >= 3)
                            cur.Messages.Add(new ChatMsg(parts[1], UnB64(parts[2])));
                    }
                }
            }
            catch { }
            return list;
        }

        private static string B64(string s)
        {
            if (s == null) s = "";
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
        }
        private static string UnB64(string s)
        {
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(s)); }
            catch { return ""; }
        }
    }
}
