using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        /// <summary>该会话最近一次请求的估算输入 token，切换会话时恢复显示。</summary>
        public int LastInputTokens;
        public bool Pinned;
        public DateTime Created = DateTime.Now;
        public DateTime Updated = DateTime.Now;
    }

    /// <summary>圆角按钮（实心 / 幽灵两种）</summary>
    public class PillButton : Control
    {
        public Color FillColor = Color.FromArgb(242, 199, 92);
        public Color HoverColor = Color.FromArgb(248, 215, 126);
        public Color TextColor = Color.FromArgb(61, 47, 14);
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

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Color bg = Parent == null ? SystemColors.Control : Parent.BackColor;
            e.Graphics.Clear(bg);
        }

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

    /// <summary>左上角品牌图标：浅黄色圆角方块 + 四角星光。</summary>
    public class LogoMark : Control
    {
        public LogoMark()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Size = new Size(30, 30);
            TabStop = false;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? SystemColors.Control : Parent.BackColor);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Brand.Draw(e.Graphics, new RectangleF(0, 0, Width - 1, Height - 1));
        }
    }

    /// <summary>圆形发送按钮：空闲画向上箭头，生成中画停止方块。</summary>
    public class CircleButton : Control
    {
        public Color FillColor = Color.FromArgb(242, 199, 92);
        public Color HoverColor = Color.FromArgb(248, 215, 126);
        public Color GlyphColor = Color.FromArgb(61, 47, 14);
        private bool hover;
        private bool stop;

        public bool IsStop
        {
            get { return stop; }
            set { if (stop != value) { stop = value; Invalidate(); } }
        }

        public CircleButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Size = new Size(40, 40);
            AccessibleRole = AccessibleRole.PushButton;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Color bg = Parent == null ? SystemColors.Control : Parent.BackColor;
            e.Graphics.Clear(bg);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush b = new SolidBrush(hover ? HoverColor : FillColor))
                e.Graphics.FillEllipse(b, 0, 0, Width - 1, Height - 1);

            float cx = Width / 2f;
            float cy = Height / 2f;
            if (stop)
            {
                float s = Width * 0.20f;
                using (SolidBrush b = new SolidBrush(Color.White))
                    e.Graphics.FillRectangle(b, cx - s / 2f, cy - s / 2f, s, s);
            }
            else
            {
                float h = Width * 0.17f;
                float w = Width * 0.105f;
                using (Pen p = new Pen(GlyphColor, Math.Max(2f, Width / 18f)))
                {
                    p.StartCap = LineCap.Round;
                    p.EndCap = LineCap.Round;
                    e.Graphics.DrawLine(p, cx, cy + h, cx, cy - h);
                    e.Graphics.DrawLine(p, cx - w, cy - h + w, cx, cy - h);
                    e.Graphics.DrawLine(p, cx + w, cy - h + w, cx, cy - h);
                }
            }
        }
    }

    /// <summary>侧边栏里的一个会话条目；右侧三个点打开删除菜单。</summary>
    public class SessionItem : Control
    {
        public string Title = "新对话";
        public bool Selected;
        public int SessionIndex;
        public event EventHandler MenuClick;

        private bool hover;
        private bool dotsHover;
        private bool menuPressed;
        private bool suppressClick;
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

        private Rectangle DotsRect
        {
            get
            {
                int w = Ui.Px(28);
                int h = Ui.Px(28);
                return new Rectangle(Width - w - Ui.Px(4), (Height - h) / 2, w, h);
            }
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; dotsHover = false; menuPressed = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool hit = DotsRect.Contains(e.Location);
            if (hit != dotsHover) { dotsHover = hit; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && DotsRect.Contains(e.Location))
            {
                menuPressed = true;
                Invalidate();
                return;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (menuPressed)
            {
                bool inside = DotsRect.Contains(e.Location);
                menuPressed = false;
                if (!inside)
                {
                    suppressClick = false;
                    Invalidate();
                    base.OnMouseUp(e);
                    return;
                }
                suppressClick = true;
                Invalidate();
                if (MenuClick != null) MenuClick(this, EventArgs.Empty);
                return;
            }
            base.OnMouseUp(e);
        }

        protected override void OnClick(EventArgs e)
        {
            if (suppressClick) { suppressClick = false; return; }
            base.OnClick(e);
        }

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

            Rectangle textRect = new Rectangle(Ui.Px(12), 0, Math.Max(20, Width - Ui.Px(46)), Height);
            TextRenderer.DrawText(e.Graphics, Title, Font, textRect,
                Selected ? th.Text : th.SubText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            Rectangle dr = DotsRect;
            if (dotsHover)
            {
                using (GraphicsPath hp = Round.Path(dr, Ui.Px(8)))
                using (SolidBrush hb = new SolidBrush(th.SidebarHover))
                    e.Graphics.FillPath(hb, hp);
            }
            int radius = Math.Max(1, Ui.Px(2));
            int gap = Ui.Px(4);
            int cx = dr.Left + dr.Width / 2;
            int cy = dr.Top + dr.Height / 2;
            Color dotColor = dotsHover ? th.Text : (Selected ? th.SubText : Color.FromArgb(150, th.SubText));
            using (SolidBrush b = new SolidBrush(dotColor))
            {
                for (int i = -1; i <= 1; i++)
                    e.Graphics.FillEllipse(b, cx + i * gap - radius, cy - radius, radius * 2, radius * 2);
            }
        }
    }

    /// <summary>会话菜单里的可点击行。</summary>
    public class MenuRow : Control
    {
        private Theme th;
        private Color textColor;
        private bool hover;

        public MenuRow(Theme theme, string text, Color color)
        {
            th = theme;
            textColor = color;
            Text = text;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Font = new Font("Microsoft YaHei UI", 9.75f);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (hover)
            {
                using (GraphicsPath p = Round.Path(r, Ui.Px(8)))
                using (SolidBrush b = new SolidBrush(th.SidebarHover))
                    e.Graphics.FillPath(b, p);
            }
            Rectangle tr = new Rectangle(Ui.Px(12), 0, Width - Ui.Px(24), Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, tr, textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>会话右侧三个点菜单：重命名 / 置顶 / 删除，无系统菜单边框。</summary>
    public class SessionMenu : Form
    {
        private Theme th;
        private MenuRow deleteRow;
        private bool armed;
        private Action onDelete;

        public SessionMenu(Theme theme, bool pinned, Action renameAction, Action pinAction, Action deleteAction)
        {
            th = theme;
            onDelete = deleteAction;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = th.Bg;
            DoubleBuffered = true;
            KeyPreview = true;
            ClientSize = new Size(220, 138);

            MenuRow rename = new MenuRow(th, "重命名", th.Text);
            rename.SetBounds(8, 8, 204, 38);
            rename.Click += delegate { Close(); if (renameAction != null) renameAction(); };
            Controls.Add(rename);

            MenuRow pin = new MenuRow(th, pinned ? "取消置顶" : "置顶", th.Text);
            pin.SetBounds(8, 50, 204, 38);
            pin.Click += delegate { Close(); if (pinAction != null) pinAction(); };
            Controls.Add(pin);

            deleteRow = new MenuRow(th, "删除对话", th.Danger);
            deleteRow.SetBounds(8, 92, 204, 38);
            deleteRow.Click += delegate
            {
                if (!armed)
                {
                    armed = true;
                    deleteRow.Text = "再次点击确认删除";
                    deleteRow.Invalidate();
                    return;
                }
                Close();
                if (onDelete != null) onDelete();
            };
            Controls.Add(deleteRow);

            Paint += delegate (object s, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath p = Round.Path(new Rectangle(0, 0, Width - 1, Height - 1), Ui.Px(10)))
                using (Pen pen = new Pen(th.Border, 1f))
                    e.Graphics.DrawPath(pen, p);
            };
            KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
            Deactivate += delegate { Close(); };
            Resize += delegate { ApplyRegion(); };
            if (Math.Abs(Ui.S - 1f) > 0.01f) Scale(new SizeF(Ui.S, Ui.S));
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ApplyRegion(); }

        private void ApplyRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            using (GraphicsPath p = Round.Path(new Rectangle(0, 0, Width, Height), Ui.Px(10)))
            {
                Region old = Region;
                Region = new Region(p);
                if (old != null) old.Dispose();
            }
        }
    }

    /// <summary>消息右键菜单：无系统菜单边框，和主界面同色。</summary>
    public class ActionMenu : Form
    {
        private Theme th;

        public ActionMenu(Theme theme, string[] labels, Color[] colors, Action[] actions)
        {
            th = theme;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = th.Bg;
            DoubleBuffered = true;
            KeyPreview = true;
            int n = labels == null ? 0 : labels.Length;
            ClientSize = new Size(210, 8 + n * 42 + 4);
            for (int i = 0; i < n; i++)
            {
                MenuRow row = new MenuRow(th, labels[i], colors == null ? th.Text : colors[i]);
                row.SetBounds(8, 8 + i * 42, 194, 38);
                Action action = actions == null ? null : actions[i];
                row.Click += delegate { Close(); if (action != null) action(); };
                Controls.Add(row);
            }
            Paint += delegate (object s, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath p = Round.Path(new Rectangle(0, 0, Width - 1, Height - 1), Ui.Px(10)))
                using (Pen pen = new Pen(th.Border, 1f))
                    e.Graphics.DrawPath(pen, p);
            };
            KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
            Deactivate += delegate { Close(); };
            Resize += delegate { ApplyRegion(); };
            if (Math.Abs(Ui.S - 1f) > 0.01f) Scale(new SizeF(Ui.S, Ui.S));
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ApplyRegion(); }

        private void ApplyRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            using (GraphicsPath p = Round.Path(new Rectangle(0, 0, Width, Height), Ui.Px(10)))
            {
                Region old = Region;
                Region = new Region(p);
                if (old != null) old.Dispose();
            }
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
        private LogoMark logo;
        private Panel main, composerWrap;
        private int composerBoxH = Ui.Px(124);
        private TextBox input;
        private ListBox mentionList;
        private List<string> mentionPaths = new List<string>();
        private int mentionStart = -1;
        private bool mentionVisible;
        private CircleButton btnSend;
        private PillButton btnNew, btnSettings, btnFiles, btnScrollBottom;
        private Label lblModel, lblStatus, lblTokens, lblHint;
        private List<MessageView> views = new List<MessageView>();
        private SessionMenu sessionMenu;
        private ActionMenu messageMenu;
        private int editMessageIndex = -1;

        private ChatClient client;
        private bool streaming;
        private int streamingIndex = -1;
        private StringBuilder pending = new StringBuilder();
        private readonly object pendingLock = new object();
        /// <summary>已经“打字”显示出来的字符数（用于平滑流式输出）</summary>
        private int revealed;
        private bool networkDone;
        private bool turnFinished;
        private int requestSeq;
        private DateTime lastReveal = DateTime.UtcNow;
        private double revealCarry;
        private const double RevealCharsPerSecond = 140.0;
        private bool userStopped;
        private Timer tick;
        private Timer themeWatch;
        private bool layingOut;
        private bool atBottom = true;
        /// <summary>本轮对话里临时的 tool_calls / tool 结果消息（不写进会话文件）</summary>
        private List<ChatMsg> turnExtra = new List<ChatMsg>();
        /// <summary>本轮已经读取过的文件（按 内容长度+修改时间 去重，避免重复把大文件塞进上下文）</summary>
        private Dictionary<string, string> toolCache = new Dictionary<string, string>();
        /// <summary>最近一次请求的估算输入 token，用于状态栏展示</summary>
        private int lastInputTokens;
        private int toolRounds;
        private const int MaxToolRounds = 12;
        private bool toolsAllowed = true;
        private bool summarizing;
        private bool autoApproveThisTurn;
        private HashSet<string> approvedWritePaths = new HashSet<string>();
        private string lastWriteSig;
        private int sameWriteRepeats;
        private bool fileChangedThisTurn;
        private bool writeDeniedThisTurn;
        private bool autoNudgedThisTurn;
        private bool userWantsWrite;

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
            th = Theme.Create(Theme.ResolveDark(cfg.ThemeMode));
            BuildUi();
            LoadSessions();
            ApplyThemeAll();
            UpdateStatusIdle();

            tick = new Timer();
            tick.Interval = 60;
            tick.Tick += delegate { StreamTick(); };
            tick.Start();

            themeWatch = new Timer();
            themeWatch.Interval = 1500;
            themeWatch.Tick += delegate { CheckSystemTheme(); };
            themeWatch.Start();
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
            UpdateScrollButton();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyScrollbarTheme();
            Ui.ApplyTitleBarTheme(this, th.IsDark);
            try { input.Focus(); } catch { }
            LayoutMessages(false);
            ScrollToBottom();
            StartReadyCheck();
        }

        // ================= 界面构建 =================
        private void BuildUi()
        {
            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;
            Text = "Lumi";
            Font = new Font("Microsoft YaHei UI", 9.75f);
            ClientSize = new Size(1180, 780);
            MinimumSize = new Size(880, 600);
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            KeyPreview = true;

            sidebar = new BufferedPanel();
            sidebar.SetBounds(0, 0, SidebarWidth, ClientSize.Height);
            Controls.Add(sidebar);

            main = new BufferedPanel();
            main.SetBounds(SidebarWidth, 0, ClientSize.Width - SidebarWidth, ClientSize.Height);
            Controls.Add(main);

            // ---- 侧边栏 ----
            logo = new LogoMark();
            logo.Size = new Size(30, 30);
            logo.Location = new Point(22, 28);
            sidebar.Controls.Add(logo);

            Label brand = new Label();
            brand.Text = "Lumi";
            brand.Font = new Font("Segoe UI Semibold", 16f, FontStyle.Bold);
            brand.AutoSize = true;
            brand.Location = new Point(60, 28);
            sidebar.Controls.Add(brand);

            btnNew = new PillButton();
            btnNew.Text = "新对话";
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

            sessionList = new BufferedPanel();
            sessionList.Location = new Point(10, 162);
            sessionList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            sessionList.Size = new Size(SidebarWidth - 20, ClientSize.Height - 162 - 110);
            sessionList.AutoScroll = true;
            sidebar.Controls.Add(sessionList);

            btnSettings = new PillButton();
            btnSettings.Text = "设置";
            btnSettings.Ghost = true;
            btnSettings.Height = 34;
            btnSettings.Radius = 9;
            btnSettings.Font = new Font("Microsoft YaHei UI", 9f);
            btnSettings.Location = new Point(16, ClientSize.Height - 92);
            btnSettings.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            btnSettings.Width = SidebarWidth - 32;
            btnSettings.Click += delegate { OpenSettings(); };
            sidebar.Controls.Add(btnSettings);

            // ---- 主区 ----
            header = new BufferedPanel();
            header.SetBounds(0, 0, main.ClientSize.Width, 56);
            main.Controls.Add(header);

            lblStatus = new Label();
            lblStatus.Text = "● 未配置";
            lblStatus.Font = new Font("Microsoft YaHei UI", 9f);
            lblStatus.AutoSize = true;
            lblStatus.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            lblStatus.TextAlign = ContentAlignment.MiddleRight;
            header.Controls.Add(lblStatus);

            lblTokens = new Label();
            lblTokens.Text = "Token ≈ —";
            lblTokens.Font = new Font("Microsoft YaHei UI", 9f);
            lblTokens.AutoSize = true;
            lblTokens.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            lblTokens.TextAlign = ContentAlignment.MiddleRight;
            header.Controls.Add(lblTokens);

            msgHost = new BufferedPanel();
            msgHost.SetBounds(0, 56, main.ClientSize.Width, Math.Max(0, main.ClientSize.Height - 56 - 104));
            msgHost.AutoScroll = true;
            main.Controls.Add(msgHost);
            msgHost.Resize += delegate { if (!layingOut) LayoutMessages(true); };
            msgHost.Scroll += delegate
            {
                if (layingOut) return;
                atBottom = msgHost.VerticalScroll.Value >= msgHost.VerticalScroll.Maximum - Ui.Px(48);
                UpdateScrollButton();
            };

            composerWrap = new BufferedPanel();
            composerWrap.SetBounds(0, Math.Max(56, main.ClientSize.Height - 104), main.ClientSize.Width, 104);
            composerWrap.BackColor = th.Bg;
            main.Controls.Add(composerWrap);

            btnScrollBottom = new PillButton();
            btnScrollBottom.Text = "↓";
            btnScrollBottom.Ghost = true;
            btnScrollBottom.Radius = 19;
            btnScrollBottom.Size = new Size(38, 38);
            btnScrollBottom.Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold);
            btnScrollBottom.TextColor = th.SubText;
            btnScrollBottom.FillColor = th.Accent;
            btnScrollBottom.BorderColor = th.Border;
            btnScrollBottom.Visible = false;
            btnScrollBottom.Click += delegate { ScrollToBottom(); };
            main.Controls.Add(btnScrollBottom);
            btnScrollBottom.BringToFront();

            composerBox = new BufferedPanel();
            composerBox.SetBounds(26, 10, Math.Max(200, composerWrap.ClientSize.Width - 52), 124);
            composerBox.BackColor = th.ComposerBg;
            composerWrap.Controls.Add(composerBox);
            composerBox.Paint += ComposerBox_Paint;
            composerBox.Resize += delegate { LayoutComposer(); };

            input = new TextBox();
            input.Multiline = true;
            input.BorderStyle = BorderStyle.None;
            input.Font = new Font("Microsoft YaHei UI", 10.5f);
            input.BackColor = th.ComposerBg;
            input.ForeColor = th.Text;
            input.ScrollBars = ScrollBars.None;
            input.AllowDrop = true;
            input.KeyDown += Input_KeyDown;
            input.TextChanged += delegate { RecalcComposerHeight(); UpdateMention(); };
            input.GotFocus += delegate { if (composerBox != null) composerBox.Invalidate(); };
            input.LostFocus += delegate
            {
                if (composerBox != null) composerBox.Invalidate();
                if (!mentionVisible || mentionList == null) return;
                Point p = mentionList.PointToClient(Cursor.Position);
                if (!mentionList.ClientRectangle.Contains(p)) HideMention();
            };
            input.DragEnter += Input_DragEnter;
            input.DragDrop += Input_DragDrop;
            composerBox.Controls.Add(input);
            composerBox.AllowDrop = true;
            composerBox.DragEnter += Input_DragEnter;
            composerBox.DragDrop += Input_DragDrop;

            mentionList = new ListBox();
            mentionList.DrawMode = DrawMode.OwnerDrawFixed;
            mentionList.BorderStyle = BorderStyle.FixedSingle;
            mentionList.Font = new Font("Microsoft YaHei UI", 9f);
            mentionList.BackColor = th.InputBg;
            mentionList.ForeColor = th.Text;
            mentionList.Visible = false;
            mentionList.IntegralHeight = false;
            mentionList.DrawItem += MentionList_DrawItem;
            mentionList.DoubleClick += delegate { AcceptMention(); };
            main.Controls.Add(mentionList);
            mentionList.BringToFront();

            btnSend = new CircleButton();
            btnSend.Size = new Size(40, 40);
            btnSend.AccessibleName = "发送";
            btnSend.Click += delegate { OnSendClicked(); };
            composerBox.Controls.Add(btnSend);

            lblModel = new Label();
            lblModel.AutoSize = false;
            lblModel.Height = Ui.Px(20);
            lblModel.TextAlign = ContentAlignment.MiddleRight;
            lblModel.AutoEllipsis = true;
            lblModel.Font = new Font("Microsoft YaHei UI", 9f);
            lblModel.ForeColor = th.SubText;
            lblModel.BackColor = th.ComposerBg;
            lblModel.Text = cfg.Model;
            composerBox.Controls.Add(lblModel);

            btnFiles = new PillButton();
            btnFiles.Text = "\U0001F6E1  工作区";
            btnFiles.Ghost = true;
            btnFiles.Height = 30;
            btnFiles.Width = 112;
            btnFiles.Radius = 8;
            btnFiles.Font = new Font("Microsoft YaHei UI", 9f);
            btnFiles.Click += delegate { OpenWorkspace(); };
            composerBox.Controls.Add(btnFiles);

            lblHint = new Label();
            lblHint.Text = "Enter 发送 · Shift+Enter 换行 · 右键消息操作 · Ctrl+N 新对话 · Ctrl+O 工作区 · Ctrl+, 设置";
            lblHint.Font = new Font("Microsoft YaHei UI", 8.5f);
            lblHint.AutoSize = false;
            lblHint.AutoEllipsis = true;
            lblHint.TextAlign = ContentAlignment.MiddleLeft;
            lblHint.Height = 20;
            lblHint.Location = new Point(30, 78);
            main.Controls.Add(lblHint);

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
            SuspendLayout();
            try
            {
                int w = ClientSize.Width;
                int h = ClientSize.Height;
                int sideW = Ui.Px(SidebarWidth);
                int headerH = Ui.Px(56);

                sidebar.SetBounds(0, 0, sideW, h);
                main.SetBounds(sideW, 0, Math.Max(Ui.Px(200), w - sideW), h);

                int mw = main.ClientSize.Width;
                int mh = main.ClientSize.Height;
                header.SetBounds(0, 0, mw, headerH);

                int colX, colW;
                MessageColumnFor(mw, out colX, out colW);
                int boxW = Math.Max(Ui.Px(200), colW);
                int inputW = Math.Max(Ui.Px(80), boxW - Ui.Px(36));
                composerBoxH = MeasureComposerHeight(inputW);

                // 提示行固定在窗口底部，不随输入框增高/降低而移动，避免闪烁
                int wrapH = composerBoxH + Ui.Px(14);
                int composerH = wrapH + Ui.Px(4) + lblHint.Height + Ui.Px(6);

                composerWrap.SetBounds(0, Math.Max(headerH, mh - composerH), mw, wrapH);
                msgHost.SetBounds(0, headerH, mw, Math.Max(0, mh - headerH - composerH));
                if (btnScrollBottom != null)
                {
                    int scx, scw;
                    CurrentMessageColumn(out scx, out scw);
                    int sbx = Math.Max(0, scx + scw - btnScrollBottom.Width - Ui.Px(22));
                    int sby = Math.Max(0, msgHost.Bottom - btnScrollBottom.Height - Ui.Px(24));
                    btnScrollBottom.Location = new Point(sbx, sby);
                }

                composerBox.Left = colX;
                composerBox.Width = boxW;
                composerBox.Top = Ui.Px(10);
                composerBox.Height = composerBoxH;
                lblHint.Left = colX;
                lblHint.Width = colW;
                lblHint.Top = composerWrap.Bottom + Ui.Px(4);

                LayoutHeader();
                LayoutComposer();
            }
            finally { ResumeLayout(true); }
        }

        private void MessageColumnFor(int available, out int left, out int width)
        {
            int pad = Ui.Px(26);
            int avail = Math.Max(Ui.Px(200), available);
            int maxW = Math.Max(Ui.Px(200), avail - pad * 2);
            int wanted = (int)Math.Round(avail * 0.75);
            width = Math.Min(maxW, Math.Max(Ui.Px(240), wanted));
            left = Math.Max(pad, (avail - width) / 2);
        }

        private void CurrentMessageColumn(out int left, out int width)
        {
            int avail = msgHost == null ? main.ClientSize.Width : msgHost.ClientSize.Width;
            int scrollW = msgHost != null && msgHost.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0;
            MessageColumnFor(Math.Max(Ui.Px(200), avail - scrollW), out left, out width);
        }

        /// <summary>根据输入内容（含自动换行）计算发送框高度，限制在 1～约 10 行之间。</summary>
        private int MeasureComposerHeight(int inputWidth)
        {
            // 输入框下面固定留出 60px：圆形发送按钮 + 当前模型文字
            int min = Ui.Px(124);
            int max = Ui.Px(240);
            if (input == null) return min;

            string t = input.Text == null ? "" : input.Text;
            if (t.Length == 0) return min;
            if (t.EndsWith("\n") || t.EndsWith("\r")) t += " ";

            Size sz = TextRenderer.MeasureText(t, input.Font,
                new Size(Math.Max(Ui.Px(40), inputWidth - Ui.Px(6)), int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            int h = sz.Height + Ui.Px(82);
            if (h < min) h = min;
            if (h > max) h = max;
            return h;
        }

        private void MentionList_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= mentionList.Items.Count) return;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush b = new SolidBrush(sel ? th.SidebarActive : th.InputBg))
                e.Graphics.FillRectangle(b, e.Bounds);
            TextRenderer.DrawText(e.Graphics, Convert.ToString(mentionList.Items[e.Index]), e.Font, e.Bounds,
                th.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private void HideMention()
        {
            if (mentionList == null) return;
            mentionVisible = false;
            mentionStart = -1;
            mentionList.Visible = false;
            mentionList.Items.Clear();
        }

        private void UpdateMention()
        {
            if (input == null || mentionList == null) return;
            if (!Workspace.IsReady(cfg.Workspace)) { HideMention(); return; }

            int caret = input.SelectionStart;
            if (caret <= 0) { HideMention(); return; }
            int at = input.Text.LastIndexOf('@', caret - 1);
            if (at < 0) { HideMention(); return; }
            string token = input.Text.Substring(at, caret - at);
            if (token.IndexOfAny(new char[] { ' ', '\t', '\r', '\n' }) >= 0) { HideMention(); return; }

            if (!mentionVisible) mentionPaths = GetWorkspaceFiles();
            string q = token.Length > 1 ? token.Substring(1) : "";
            mentionList.BeginUpdate();
            mentionList.Items.Clear();
            foreach (string p in mentionPaths)
            {
                if (q.Length == 0 || p.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    mentionList.Items.Add(p);
                if (mentionList.Items.Count >= 60) break;
            }
            mentionList.EndUpdate();

            if (mentionList.Items.Count == 0) { HideMention(); return; }
            mentionStart = at;
            mentionVisible = true;
            mentionList.SelectedIndex = 0;
            mentionList.ItemHeight = Ui.Px(24);
            PositionMention();
            mentionList.Visible = true;
            mentionList.BringToFront();
        }

        private void PositionMention()
        {
            int count = Math.Min(8, Math.Max(1, mentionList.Items.Count));
            int h = count * Ui.Px(24) + Ui.Px(6);
            int w = Math.Min(Ui.Px(520), Math.Max(Ui.Px(300), composerBox.Width - Ui.Px(80)));
            Point local;
            try
            {
                int pos = Math.Min(Math.Max(mentionStart, 0), Math.Max(0, input.TextLength - 1));
                Point cp = input.GetPositionFromCharIndex(pos);
                local = main.PointToClient(input.PointToScreen(cp));
            }
            catch { local = new Point(Ui.Px(26), msgHost.Bottom - Ui.Px(40)); }

            int x = Math.Max(Ui.Px(8), Math.Min(local.X, main.ClientSize.Width - w - Ui.Px(8)));
            int y = local.Y - h - Ui.Px(6);
            if (y < Ui.Px(8)) y = local.Y + Ui.Px(26);
            mentionList.SetBounds(x, y, w, h);
        }

        private List<string> GetWorkspaceFiles()
        {
            List<string> list = new List<string>();
            try
            {
                string root = cfg.Workspace;
                foreach (string f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    string rel = Workspace.RelOf(root, f);
                    string lower = rel.Replace('\\', '/');
                    if (lower.IndexOf("/.git/", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (lower.StartsWith(".git/", StringComparison.OrdinalIgnoreCase)) continue;
                    if (lower.IndexOf("/node_modules/", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (lower.IndexOf("/bin/", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (lower.IndexOf("/obj/", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    list.Add(lower);
                    if (list.Count >= 500) break;
                }
            }
            catch { }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        private void AcceptMention()
        {
            if (!mentionVisible || mentionList.SelectedIndex < 0) return;
            string rel = Convert.ToString(mentionList.SelectedItem);
            if (string.IsNullOrEmpty(rel)) { HideMention(); return; }
            int start = mentionStart;
            int end = input.SelectionStart;
            HideMention();
            string full = Workspace.Resolve(cfg.Workspace, rel);
            string block = BuildFileBlock(rel, full);
            if (start < 0 || end < start || end > input.TextLength) start = input.TextLength;
            input.Text = input.Text.Substring(0, start) + block + input.Text.Substring(Math.Min(end, input.TextLength));
            input.SelectionStart = start + block.Length;
            input.Focus();
        }

        private void Input_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
        }

        private void Input_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null) return;
            foreach (string f in files)
            {
                string display = f;
                try { if (Workspace.IsReady(cfg.Workspace) && f.StartsWith(Path.GetFullPath(cfg.Workspace), StringComparison.OrdinalIgnoreCase)) display = Workspace.RelOf(cfg.Workspace, f); }
                catch { }
                string block = BuildFileBlock(display, f);
                int pos = input.SelectionStart;
                input.Text = input.Text.Substring(0, pos) + block + input.Text.Substring(pos);
                input.SelectionStart = pos + block.Length;
            }
            input.Focus();
        }

        private string BuildFileBlock(string display, string full)
        {
            string text = null;
            try
            {
                if (OfficeFiles.IsOffice(full))
                {
                    string extracted;
                    bool truncated;
                    if (OfficeFiles.TryRead(full, out extracted, out truncated)) text = extracted;
                }
                else
                {
                    byte[] bytes = File.ReadAllBytes(full);
                    if (bytes.Length > 512 * 1024) Array.Resize(ref bytes, 512 * 1024);
                    text = DecodeText(bytes);
                }
            }
            catch { }
            if (text == null) text = "（无法读取这个文件，可能是二进制文件）";
            if (text.Length > 12000) text = text.Substring(0, 12000) + "\r\n…（内容已截断）";
            return "【引用文件：" + display + "】\r\n" + text + "\r\n【/引用文件】\r\n\r\n";
        }

        private static string DecodeText(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return "";
            for (int i = 0; i < Math.Min(bytes.Length, 4096); i++) if (bytes[i] == 0) return null;
            int start = 0;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) start = 3;
            try { return new UTF8Encoding(false, true).GetString(bytes, start, bytes.Length - start); }
            catch { try { return Encoding.GetEncoding(936).GetString(bytes); } catch { return Encoding.Default.GetString(bytes); } }
        }

        private void RecalcComposerHeight()
        {
            if (composerBox == null || main == null || btnSend == null) return;
            int mw = main.ClientSize.Width;
            int boxW = Math.Max(Ui.Px(200), mw - Ui.Px(52));
            int inputW = Math.Max(Ui.Px(80), boxW - Ui.Px(36));
            if (MeasureComposerHeight(inputW) == composerBoxH) return;

            LayoutAll();
            composerWrap.Invalidate(true);
            msgHost.Invalidate(true);
        }

        private void LayoutHeader()
        {
            if (lblStatus == null || header == null) return;
            lblStatus.Location = new Point(Ui.Px(26), Ui.Px(18));
            if (lblTokens != null)
                lblTokens.Location = new Point(Math.Max(Ui.Px(200), header.Width - lblTokens.Width - Ui.Px(28)), Ui.Px(18));
        }

        private void LayoutComposer()
        {
            if (composerBox == null || composerWrap == null) return;
            int colX, colW;
            CurrentMessageColumn(out colX, out colW);
            composerBox.Left = colX;
            composerBox.Width = Math.Max(Ui.Px(200), colW);
            composerBox.Height = composerBoxH;

            int pad = Ui.Px(12);
            btnSend.Location = new Point(composerBox.Width - btnSend.Width - pad, composerBox.Height - btnSend.Height - pad);

            int inputH = Math.Max(Ui.Px(20), composerBox.Height - Ui.Px(74));
            input.SetBounds(Ui.Px(18), Ui.Px(14), Math.Max(Ui.Px(80), composerBox.Width - Ui.Px(36)), inputH);

            btnFiles.SetBounds(Ui.Px(18),
                btnSend.Top + Math.Max(0, (btnSend.Height - btnFiles.Height) / 2),
                btnFiles.Width, btnFiles.Height);

            int modelRight = btnSend.Left - Ui.Px(10);
            int modelLeft = Math.Max(btnFiles.Right + Ui.Px(8), modelRight - Ui.Px(260));
            int modelW = Math.Max(Ui.Px(60), modelRight - modelLeft);
            lblModel.SetBounds(modelRight - modelW,
                btnSend.Top + Math.Max(0, (btnSend.Height - lblModel.Height) / 2),
                modelW, lblModel.Height);

            using (GraphicsPath p = Round.Path(new Rectangle(0, 0, composerBox.Width - 1, composerBox.Height - 1), Ui.Px(16)))
                composerBox.Region = new Region(p);
        }

        private void ComposerBox_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, composerBox.Width - 1, composerBox.Height - 1);
            bool focused = input != null && input.Focused;
            using (GraphicsPath p = Round.Path(r, 16))
            {
                using (SolidBrush b = new SolidBrush(th.ComposerBg)) e.Graphics.FillPath(b, p);
                using (Pen pen = new Pen(focused ? th.FocusBorder : th.ComposerBorder, focused ? 1.8f : 1f))
                    e.Graphics.DrawPath(pen, p);
            }
        }

        // ================= 主题 =================
        private void ApplyThemeAll()
        {
            BackColor = th.Bg;
            sidebar.BackColor = th.Sidebar;
            main.BackColor = th.Bg;
            if (sessionList != null) sessionList.BackColor = th.Sidebar;
            header.BackColor = th.Bg;
            msgHost.BackColor = th.Bg;
            composerWrap.BackColor = th.Bg;
            composerBox.BackColor = th.ComposerBg;
            input.BackColor = th.ComposerBg;
            input.ForeColor = th.Text;
            lblHint.ForeColor = th.SubText;
            lblModel.ForeColor = th.SubText;
            lblModel.BackColor = th.ComposerBg;
            lblTokens.ForeColor = th.SubText;
            lblStatus.ForeColor = th.SubText;
            if (mentionList != null)
            {
                mentionList.BackColor = th.InputBg;
                mentionList.ForeColor = th.Text;
            }
            foreach (Control c in sidebar.Controls)
            {
                if (c is Label) ((Label)c).ForeColor = ((Label)c).Text == "Lumi" ? th.Text : th.SubText;
            }
            foreach (Control c in sessionList.Controls)
            {
                SessionItem si = c as SessionItem;
                if (si != null) si.ApplyTheme(th);
            }
            foreach (Control c in header.Controls)
                if (c is Label && c != lblStatus && c != lblTokens) c.ForeColor = th.SubText;

            if (logo != null) logo.Invalidate();
            btnNew.FillColor = th.Accent;
            btnNew.HoverColor = th.AccentHover;
            btnNew.TextColor = th.AccentText;
            btnNew.Invalidate();
            btnSettings.TextColor = th.SubText;
            btnSettings.FillColor = th.Accent;
            btnSettings.BorderColor = th.Border;
            btnSettings.Invalidate();
            btnFiles.TextColor = th.SubText;
            btnFiles.FillColor = th.Accent;
            btnFiles.BorderColor = th.ComposerBorder;
            btnFiles.Invalidate();
            if (btnScrollBottom != null)
            {
                btnScrollBottom.TextColor = th.SubText;
                btnScrollBottom.FillColor = th.Accent;
                btnScrollBottom.BorderColor = th.Border;
                btnScrollBottom.Invalidate();
            }
            btnSend.FillColor = th.Accent;
            btnSend.HoverColor = th.AccentHover;
            btnSend.GlyphColor = th.AccentText;
            btnSend.Invalidate();

            foreach (MessageView v in views) v.ApplyTheme(th);
            LayoutMessages(false);
            composerBox.Invalidate();
            ApplyScrollbarTheme();
            RebuildSessionList();
            if (IsHandleCreated) Ui.ApplyTitleBarTheme(this, th.IsDark);
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

        private void AddSessionGroup(string text, ref int y)
        {
            Label g = new Label();
            g.Text = text;
            g.AutoSize = false;
            g.Font = new Font("Microsoft YaHei UI", 8f);
            g.ForeColor = th.SubText;
            g.BackColor = th.Sidebar;
            g.SetBounds(0, y, Math.Max(20, sessionList.ClientSize.Width - 2), Ui.Px(22));
            g.TextAlign = ContentAlignment.MiddleLeft;
            g.Padding = new Padding(Ui.Px(8), 0, 0, 0);
            sessionList.Controls.Add(g);
            y += Ui.Px(24);
        }

        private void RebuildSessionList()
        {
            if (sessionMenu != null) { sessionMenu.Close(); sessionMenu = null; }
            sessionList.SuspendLayout();
            List<Control> oldControls = new List<Control>();
            foreach (Control c in sessionList.Controls) oldControls.Add(c);
            foreach (Control c in oldControls)
            {
                sessionList.Controls.Remove(c);
                c.Dispose();
            }

            List<int> order = new List<int>();
            for (int i = 0; i < sessions.Count; i++)
            {
                order.Add(i);
            }
            order.Sort(delegate (int a, int b)
            {
                Session sa = sessions[a];
                Session sb = sessions[b];
                if (sa.Pinned != sb.Pinned) return sa.Pinned ? -1 : 1;
                int c = sb.Updated.CompareTo(sa.Updated);
                if (c != 0) return c;
                return a.CompareTo(b);
            });

            DateTime today = DateTime.Today;
            string lastGroup = null;
            int y = 0;
            foreach (int i in order)
            {
                Session s = sessions[i];
                string group = s.Pinned ? "置顶" : (s.Updated.Date == today ? "今天" : (s.Updated.Date == today.AddDays(-1) ? "昨天" : "更早"));
                if (group != lastGroup)
                {
                    AddSessionGroup(group, ref y);
                    lastGroup = group;
                }

                SessionItem it = new SessionItem(th);
                it.Title = s.Title;
                it.SessionIndex = i;
                it.Selected = (i == active);
                it.Width = sessionList.ClientSize.Width - 2;
                it.Location = new Point(0, y);
                it.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                int idx = i;
                it.Click += delegate { SelectSession(idx); };
                it.MenuClick += delegate { ShowSessionMenu(it, idx); };
                sessionList.Controls.Add(it);
                y += it.Height + 4;
            }
            if (order.Count == 0)
            {
                Label empty = new Label();
                empty.Text = "还没有历史对话";
                empty.AutoSize = false;
                empty.Font = new Font("Microsoft YaHei UI", 8.5f);
                empty.ForeColor = th.SubText;
                empty.BackColor = th.Sidebar;
                empty.SetBounds(0, y, Math.Max(20, sessionList.ClientSize.Width - 2), Ui.Px(28));
                empty.TextAlign = ContentAlignment.MiddleLeft;
                empty.Padding = new Padding(Ui.Px(8), 0, 0, 0);
                sessionList.Controls.Add(empty);
            }
            sessionList.ResumeLayout();
        }

        private void ShowSessionMenu(SessionItem item, int idx)
        {
            if (streaming || idx < 0 || idx >= sessions.Count) return;
            if (sessionMenu != null) { sessionMenu.Close(); sessionMenu = null; }

            Session s = sessions[idx];
            SessionMenu menu = new SessionMenu(th, s.Pinned,
                delegate { RenameSession(idx); },
                delegate { TogglePin(idx); },
                delegate { DeleteSession(idx); });
            sessionMenu = menu;
            menu.FormClosed += delegate { if (sessionMenu == menu) sessionMenu = null; };

            Point p = item.PointToScreen(new Point(item.Width - menu.Width - Ui.Px(4), item.Height + Ui.Px(4)));
            Rectangle wa = Screen.FromControl(this).WorkingArea;
            int x = Math.Max(wa.Left + 4, Math.Min(p.X, wa.Right - menu.Width - 4));
            int y = Math.Max(wa.Top + 4, Math.Min(p.Y, wa.Bottom - menu.Height - 4));
            menu.Location = new Point(x, y);
            menu.Show(this);
            menu.Activate();
        }

        private void RenameSession(int idx)
        {
            if (streaming || idx < 0 || idx >= sessions.Count) return;
            Session s = sessions[idx];
            string name = Prompt.Ask(this, th, "重命名对话", "输入新的会话名称", s.Title);
            if (name == null || name.Length == 0) return;
            s.Title = name;
            RebuildSessionList();
            SaveSessions();
        }

        private void TogglePin(int idx)
        {
            if (streaming || idx < 0 || idx >= sessions.Count) return;
            Session s = sessions[idx];
            s.Pinned = !s.Pinned;
            RebuildSessionList();
            SaveSessions();
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
            editMessageIndex = -1;
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
            editMessageIndex = -1;
            sessions.RemoveAt(idx);
            if (sessions.Count == 0) sessions.Add(new Session());
            if (idx < active) active--;
            else if (idx == active) active = Math.Min(active, sessions.Count - 1);
            if (active < 0) active = 0;
            RebuildSessionList();
            SelectSession(active);
            SaveSessions();
        }

        private void SelectSession(int idx)
        {
            if (streaming) return;
            if (idx < 0 || idx >= sessions.Count) return;
            editMessageIndex = -1;
            if (messageMenu != null) { messageMenu.Close(); messageMenu = null; }
            active = idx;
            foreach (Control c in sessionList.Controls)
            {
                SessionItem it = c as SessionItem;
                if (it != null)
                {
                    it.Selected = (it.SessionIndex == idx);
                    it.Invalidate();
                    if (it.Selected) sessionList.ScrollControlIntoView(it);
                }
            }
            RenderSession();
            UpdateModelLabel();
            lastInputTokens = sessions[active].LastInputTokens;
            UpdateTokenLabel();
        }

        private void UpdateModelLabel()
        {
            lblModel.Text = string.IsNullOrEmpty(cfg.Model) ? "未设置模型" : cfg.Model;
            LayoutComposer();
        }

        private void RenderSession()
        {
            if (messageMenu != null) { messageMenu.Close(); messageMenu = null; }
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
            v.MenuRequested += delegate { ShowMessageMenu(v); };
            v.HeightChanged += delegate { if (!layingOut) LayoutMessages(atBottom); };
            return v;
        }

        private void ShowMessageMenu(MessageView v)
        {
            if (v == null) return;
            if (messageMenu != null) { messageMenu.Close(); messageMenu = null; }

            List<string> labels = new List<string>();
            List<Color> colors = new List<Color>();
            List<Action> actions = new List<Action>();

            labels.Add("复制文本");
            colors.Add(th.Text);
            actions.Add(delegate { CopyMessage(v); });

            if (!v.IsToolCard && !streaming)
            {
                if (v.IsUser)
                {
                    labels.Add("编辑并重发");
                    colors.Add(th.Text);
                    actions.Add(delegate { EditMessage(v); });
                }
                else if (v.Msg != null && v.Msg.Role == "assistant")
                {
                    labels.Add("重新生成");
                    colors.Add(th.Text);
                    actions.Add(delegate { RegenerateMessage(v); });
                }
                labels.Add("从此处分支");
                colors.Add(th.Text);
                actions.Add(delegate { BranchFrom(v); });
            }

            ActionMenu menu = new ActionMenu(th, labels.ToArray(), colors.ToArray(), actions.ToArray());
            messageMenu = menu;
            menu.FormClosed += delegate { if (messageMenu == menu) messageMenu = null; };
            Point p = Cursor.Position;
            Rectangle wa = Screen.FromControl(this).WorkingArea;
            int x = Math.Max(wa.Left + 4, Math.Min(p.X, wa.Right - menu.Width - 4));
            int y = Math.Max(wa.Top + 4, Math.Min(p.Y, wa.Bottom - menu.Height - 4));
            menu.Location = new Point(x, y);
            menu.Show(this);
            menu.Activate();
        }

        private void CopyMessage(MessageView v)
        {
            string t = v.GetText();
            if (string.IsNullOrEmpty(t)) return;
            try { Clipboard.SetText(t); SetStatus("● 已复制", true); }
            catch { SetStatus("● 复制失败", false); }
        }

        private void EditMessage(MessageView v)
        {
            if (streaming) return;
            Session s = sessions[active];
            int mi = s.Messages.IndexOf(v.Msg);
            if (mi < 0 || s.Messages[mi].Role != "user") return;
            editMessageIndex = mi;
            input.Text = v.Msg.Content == null ? "" : v.Msg.Content;
            input.Focus();
            input.SelectionStart = input.TextLength;
            input.ScrollToCaret();
            SetStatus("● 编辑后发送将替换原消息", true);
        }

        private void RegenerateMessage(MessageView v)
        {
            if (streaming) return;
            Session s = sessions[active];
            int mi = s.Messages.IndexOf(v.Msg);
            if (mi < 0) return;
            int ui = mi - 1;
            while (ui >= 0 && s.Messages[ui].Role != "user") ui--;
            if (ui < 0) return;

            s.Messages.RemoveRange(ui + 1, s.Messages.Count - (ui + 1));
            s.Updated = DateTime.Now;
            ResetTurnState();
            userWantsWrite = WantsFileWrite(s.Messages[ui].Content);
            RenderSession();
            StartRequest(true);
        }

        private void BranchFrom(MessageView v)
        {
            if (streaming) return;
            Session s = sessions[active];
            int mi = s.Messages.IndexOf(v.Msg);
            if (mi < 0) return;

            Session ns = new Session();
            string t = s.Title == null ? "新对话" : s.Title;
            ns.Title = (t.Length > 16 ? t.Substring(0, 16) : t) + " · 分支";
            for (int i = 0; i <= mi; i++)
                ns.Messages.Add(new ChatMsg(s.Messages[i].Role, s.Messages[i].Content));
            sessions.Add(ns);
            active = sessions.Count - 1;
            editMessageIndex = -1;
            RebuildSessionList();
            SelectSession(active);
            SaveSessions();
        }

        private void LayoutMessages(bool keepScroll)
        {
            if (msgHost == null || layingOut) return;
            layingOut = true;
            try
            {
                ClearWelcomeIfNeeded();

                // AutoScroll 面板滚动后，子控件 Location 是“可见区域坐标”。
                // 布局时按内容坐标减去当前滚动量来摆放，避免先滚到顶部再滚回来产生闪烁。
                bool wasAtBottom = atBottom;
                int oldScroll = Math.Max(0, -msgHost.AutoScrollPosition.Y);

                int colX, colW;
                CurrentMessageColumn(out colX, out colW);
                int maxW = Math.Max(Ui.Px(240), colW);

                int y = Ui.Px(18);
                foreach (MessageView v in views)
                {
                    int h = v.Reflow(maxW);
                    int x = v.IsUser ? colX + Math.Max(0, colW - v.Width) : colX;
                    Point loc = new Point(x, y - oldScroll);
                    if (v.Location != loc) v.Location = loc;
                    y += h + Ui.Px(18);
                }
                Size scrollSize = new Size(0, y + Ui.Px(8));
                if (msgHost.AutoScrollMinSize != scrollSize) msgHost.AutoScrollMinSize = scrollSize;

                // 欢迎标语居中
                foreach (Control c in msgHost.Controls)
                {
                    if (c.Name == "welcome")
                    {
                        Point wl = new Point(Ui.Px(60), Math.Max(Ui.Px(40), (msgHost.ClientSize.Height - c.Height) / 2 - Ui.Px(40)));
                        if (c.Location != wl) c.Location = wl;
                    }
                }

                int max = Math.Max(0, msgHost.AutoScrollMinSize.Height - msgHost.ClientSize.Height);
                int target = (keepScroll && wasAtBottom) ? max : Math.Min(oldScroll, max);
                if (target < 0) target = 0;
                if (-msgHost.AutoScrollPosition.Y != target) msgHost.AutoScrollPosition = new Point(0, target);
                atBottom = max <= 0 || target >= max - Ui.Px(48);
                UpdateScrollButton();
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
            if (-msgHost.AutoScrollPosition.Y != max) msgHost.AutoScrollPosition = new Point(0, max);
            UpdateScrollButton();
        }

        private void UpdateScrollButton()
        {
            if (btnScrollBottom == null) return;
            bool show = !atBottom && views.Count > 0 && msgHost != null && msgHost.ClientSize.Height > 0;
            if (btnScrollBottom.Visible != show) btnScrollBottom.Visible = show;
            if (show) btnScrollBottom.BringToFront();
        }

        // ================= 发送 =================
        private void Input_KeyDown(object sender, KeyEventArgs e)
        {
            if (mentionVisible)
            {
                if (e.KeyCode == Keys.Down)
                {
                    if (mentionList.Items.Count > 0)
                        mentionList.SelectedIndex = Math.Min(mentionList.Items.Count - 1, mentionList.SelectedIndex + 1);
                    e.SuppressKeyPress = true;
                    return;
                }
                if (e.KeyCode == Keys.Up)
                {
                    if (mentionList.Items.Count > 0)
                        mentionList.SelectedIndex = Math.Max(0, mentionList.SelectedIndex - 1);
                    e.SuppressKeyPress = true;
                    return;
                }
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Tab)
                {
                    AcceptMention();
                    e.SuppressKeyPress = true;
                    return;
                }
                if (e.KeyCode == Keys.Escape)
                {
                    HideMention();
                    e.SuppressKeyPress = true;
                    return;
                }
            }
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
            if (editMessageIndex >= 0 && editMessageIndex < s.Messages.Count)
            {
                s.Messages.RemoveRange(editMessageIndex, s.Messages.Count - editMessageIndex);
                editMessageIndex = -1;
                RenderSession();
            }
            editMessageIndex = -1;

            ChatMsg userMsg = new ChatMsg("user", text);
            s.Messages.Add(userMsg);
            s.Updated = DateTime.Now;
            if (s.Title == "新对话" || s.Title.Length == 0)
            {
                string t = text.Replace("\r", " ").Replace("\n", " ");
                s.Title = t.Length > 18 ? t.Substring(0, 18) + "…" : t;
            }
            RebuildSessionList();

            input.Clear();
            AddView(userMsg, true);
            LayoutMessages(true);
            ScrollToBottom();

            ResetTurnState();
            userWantsWrite = WantsFileWrite(text);
            StartRequest(true);
        }

        private void ResetTurnState()
        {
            turnExtra.Clear();
            toolCache.Clear();
            toolRounds = 0;
            summarizing = false;
            autoApproveThisTurn = false;
            approvedWritePaths.Clear();
            lastWriteSig = null;
            sameWriteRepeats = 0;
            fileChangedThisTurn = false;
            writeDeniedThisTurn = false;
            autoNudgedThisTurn = false;
            userStopped = false;
            editMessageIndex = -1;
        }

        /// <summary>发起一轮请求（首轮和工具回填后都走这里）。</summary>
        private void StartRequest(bool allowTools)
        {
            readyCheckSeq++;
            int reqSeq = ++requestSeq;
            toolsAllowed = allowTools;
            List<ChatMsg> payload = BuildPayload();
            ChatMsg botMsg = new ChatMsg("assistant", "");
            AddView(botMsg, false);
            views[views.Count - 1].IsStreamingView = true;
            streamingIndex = views.Count - 1;
            ClearPending();
            revealed = 0;
            revealCarry = 0;
            networkDone = false;
            turnFinished = false;
            lastReveal = DateTime.UtcNow;
            streaming = true;
            atBottom = true;
            SetBusyUi(true);
            LayoutMessages(true);
            ScrollToBottom();

            Config reqCfg = cfg;
            if (!allowTools)
            {
                reqCfg = cfg.Clone();
                reqCfg.EnableTools = false;
            }
            ChatClient cli = new ChatClient(reqCfg);
            client = cli;

            cli.OnDelta += delegate (string d)
            {
                if (reqSeq != requestSeq) return;
                if (string.IsNullOrEmpty(d)) return;
                lock (pendingLock) pending.Append(d);
            };
            cli.OnError += delegate (string err)
            {
                SafeInvoke(delegate
                {
                    if (reqSeq != requestSeq) return;
                    if (turnFinished) return;
                    turnFinished = true;
                    streaming = false;
                    networkDone = false;
                    int total = PendingLength();
                    if (revealed < total) revealed = total;
                    FlushStream(true);
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
                    if (reqSeq != requestSeq) return;
                    if (turnFinished) return;
                    networkDone = true;

                    if (userStopped)
                    {
                        revealed = PendingLength();
                        FinishTurn();
                        return;
                    }
                    if (toolsAllowed && cfg.EnableTools && cli.ToolCalls.Count > 0)
                    {
                        revealed = PendingLength();
                        FlushStream();
                        if (toolRounds < MaxToolRounds) RunTools(cli);
                        else StartFinalSummary("工具调用次数已经达到 " + MaxToolRounds + " 轮上限");
                        return;
                    }
                    if (ShouldNudgeWrite())
                    {
                        StartWriteNudge();
                        return;
                    }
                    if (revealed >= PendingLength()) FinishTurn();
                    // 否则等打字机把剩余文字显示完，再由 StreamTick 调 FinishTurn
                });
            };

            cli.Send(payload);
        }

        /// <summary>
        /// 组装请求：按上下文预算裁掉较早的消息、压缩旧工具结果，而不是固定只发最近 20 条。
        /// 这样既能把 token 压下来，也不会因为某几条消息特别长而爆上下文。
        /// </summary>
        private List<ChatMsg> BuildPayload()
        {
            Session s = sessions[active];
            List<ChatMsg> payload = ContextBudget.Build(s.Messages, turnExtra, cfg.ContextTokens);

            int tokens = ContextBudget.EstimateMessages(payload);
            string sys = ChatClient.ComposeSystemPrompt(cfg);
            if (sys.Length > 0) tokens += ContextBudget.EstimateTokens(sys) + 4;
            if (toolsAllowed && cfg.EnableTools && Workspace.IsReady(cfg.Workspace)) tokens += 260;
            lastInputTokens = tokens;
            s.LastInputTokens = tokens;
            UpdateTokenLabel();
            return payload;
        }

        private void FinishTurn()
        {
            if (turnFinished) return;
            turnFinished = true;
            streaming = false;
            networkDone = false;

            Session s = sessions[active];
            SetBusyUi(false);
            if (revealed < PendingLength()) revealed = PendingLength();
            FlushStream(true);
            if (streamingIndex >= 0 && streamingIndex < views.Count) views[streamingIndex].FinishStreaming();
            string content = PendingText();

            // 兜底：没有实际写入成功，却仍然声称已写入时，不用假成功覆盖用户判断
            if (!fileChangedThisTurn && userWantsWrite && LooksLikeWriteClaim(content))
            {
                if (writeDeniedThisTurn)
                    content = "（写入被拒绝，文件没有修改。）";
                else if (autoNudgedThisTurn)
                    content = "（自动重试后仍未检测到实际的 write_file 调用，文件可能没有写入成功。请检查工作区路径和权限，或再明确要求“调用 write_file 写入 <文件名>”。）";
            }

            if (content.Trim().Length == 0)
            {
                if (userStopped || (toolRounds > 0 && !summarizing))
                {
                    // 工具轮次已经产生了可见的工具消息，这里不再追加一段空结果提示
                    RemoveStreamingView();
                }
                else
                {
                    string hint = summarizing
                        ? "（工具调用已达上限，模型仍未返回文字总结；可以缩小任务范围，或者分多次让我处理）"
                        : "（没有收到内容，请检查接口路径、模型名称或代理设置）";
                    MessageView ev = AddView(new ChatMsg("assistant", hint), false);
                    ev.SetError();
                }
            }
            else
            {
                s.Messages.Add(new ChatMsg("assistant", content));
                s.Updated = DateTime.Now;
            }
            ClearPending();
            revealed = 0;
            revealCarry = 0;
            turnExtra.Clear();
            toolCache.Clear();
            LayoutMessages(true);
            ScrollToBottom();
            SetStatus(userStopped ? "● 已停止" : "● 已连接", !userStopped);
            SaveSessions();
            RebuildSessionList();
            client = null;
        }

        // ================= 工作区工具 =================
        private void RunTools(ChatClient cli)
        {
            toolRounds++;
            SetStatus("● 工具调用 " + toolRounds + "/" + MaxToolRounds + "…", false);
            List<ToolCall> calls = cli.ToolCalls;

            if (IsWriteOnly(calls))
            {
                string sig = WriteSignature(calls);
                if (sig == lastWriteSig) sameWriteRepeats++;
                else { lastWriteSig = sig; sameWriteRepeats = 1; }
                if (sameWriteRepeats >= 3)
                {
                    StartFinalSummary("检测到连续重复写入相同内容，已经停止重复操作");
                    return;
                }
            }
            else
            {
                lastWriteSig = null;
                sameWriteRepeats = 0;
            }

            string partial = PendingText();

            if (partial.Trim().Length == 0) RemoveStreamingView();
            ClearPending();
            revealed = 0;
            revealCarry = 0;

            ChatMsg asst = new ChatMsg("assistant", partial);
            asst.ToolCalls = calls;
            asst.ReasoningContent = cli.ReasoningContent;
            turnExtra.Add(asst);

            foreach (ToolCall tc in calls)
            {
                string summary;
                string diff;
                Stopwatch sw = Stopwatch.StartNew();
                string result = ExecuteTool(tc, out summary, out diff);
                sw.Stop();
                ChatMsg tool = new ChatMsg("tool", result);
                tool.ToolCallId = tc.Id;
                turnExtra.Add(tool);
                MessageView v = AddView(new ChatMsg("assistant", summary), false);
                v.SetToolCard(tc.Name, summary, tc.Arguments, result, diff, sw.ElapsedMilliseconds);
            }

            LayoutMessages(true);
            ScrollToBottom();
            if (userStopped) { FinishTurn(); return; }
            StartRequest(true);
        }

        /// <summary>达到工具轮数上限后，关闭工具再请求一次文字总结，避免直接空手结束。</summary>
        private void StartFinalSummary(string reason)
        {
            if (summarizing) { FinishTurn(); return; }
            summarizing = true;

            string partial = PendingText();
            if (partial.Trim().Length == 0) RemoveStreamingView();
            ClearPending();
            revealed = 0;
            revealCarry = 0;
            if (partial.Trim().Length > 0)
                turnExtra.Add(new ChatMsg("assistant", partial));

            turnExtra.Add(new ChatMsg("user",
                reason + "。请停止调用工具，直接根据上面已经完成的工具结果，用中文总结：已经完成了什么、写入或修改了哪些文件、还有哪些没有完成。不要再请求调用工具。"));

            StartRequest(false);
            SetStatus("● 工具调用已停止，正在生成总结…", false);
        }

        /// <summary>模型声称写好了文件，但本轮没有实际写入成功时，自动补一轮“真正调用工具”。</summary>
        private bool ShouldNudgeWrite()
        {
            if (!toolsAllowed || !cfg.EnableTools || !userWantsWrite) return false;
            if (fileChangedThisTurn || writeDeniedThisTurn || autoNudgedThisTurn) return false;
            return LooksLikeWriteClaim(PendingText());
        }

        private void StartWriteNudge()
        {
            autoNudgedThisTurn = true;
            string partial = PendingText();
            if (partial.Trim().Length == 0)
            {
                FinishTurn();
                return;
            }

            RemoveStreamingView();
            ClearPending();
            revealed = 0;
            revealCarry = 0;
            networkDone = false;

            turnExtra.Add(new ChatMsg("assistant", partial));
            turnExtra.Add(new ChatMsg("user",
                "你刚才只回复了文字，没有实际调用 write_file，文件并没有写入。请立刻调用 write_file 写入完整内容；调用成功后再根据工具结果回答。"));
            StartRequest(true);
            SetStatus("● 检测到未实际写入，正在自动重试…", false);
        }

        private static bool WantsFileWrite(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string[] verbs = new string[]
            {
                "写入", "写到", "写进", "保存到", "保存为", "保存成",
                "创建文件", "新建文件", "生成文件", "生成一个文件", "修改文件",
                "覆盖", "导出到", "输出到", "写一个文件", "写个文件"
            };
            foreach (string v in verbs) if (text.IndexOf(v, StringComparison.OrdinalIgnoreCase) >= 0) return true;

            string lower = text.ToLowerInvariant();
            string[] exts = new string[]
            {
                ".txt", ".md", ".py", ".c", ".h", ".cpp", ".cs", ".xlsx", ".docx",
                ".json", ".csv", ".log", ".ini", ".html", ".js", ".ts", ".java"
            };
            foreach (string ext in exts)
            {
                int idx = lower.IndexOf(ext, StringComparison.Ordinal);
                while (idx >= 0)
                {
                    int after = idx + ext.Length;
                    if (after >= lower.Length || !IsAsciiLetterOrDigit(lower[after])) return true;
                    idx = lower.IndexOf(ext, idx + 1, StringComparison.Ordinal);
                }
            }
            return false;
        }

        private static bool IsAsciiLetterOrDigit(char ch)
        {
            return (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9');
        }

        private static bool LooksLikeWriteClaim(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string[] claims = new string[]
            {
                "已写入", "已经写入", "已保存", "已经保存", "已创建", "已经创建",
                "已生成", "已经生成", "已修改", "已经修改", "已更新", "已经更新",
                "写入成功", "保存成功", "已完成写入"
            };
            foreach (string c in claims) if (text.IndexOf(c, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private static bool ArgExists(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return false;
            try
            {
                System.Web.Script.Serialization.JavaScriptSerializer js = new System.Web.Script.Serialization.JavaScriptSerializer();
                Dictionary<string, object> d = js.DeserializeObject(json) as Dictionary<string, object>;
                object value;
                return d != null && d.TryGetValue(key, out value) && value != null;
            }
            catch { return false; }
        }

        private static bool IsWriteOnly(List<ToolCall> calls)
        {
            if (calls == null || calls.Count == 0) return false;
            foreach (ToolCall tc in calls)
            {
                string n = tc.Name == null ? "" : tc.Name.Trim();
                if (n != "write_file" && n != "delete_file") return false;
            }
            return true;
        }

        private static string WriteSignature(List<ToolCall> calls)
        {
            StringBuilder sb = new StringBuilder();
            foreach (ToolCall tc in calls)
                sb.Append(tc.Name).Append('\u0001').Append(tc.Arguments).Append('\u0002');
            return sb.ToString();
        }

        private string ExecuteTool(ToolCall tc, out string summary, out string diff)
        {
            summary = "• 调用工具：" + tc.Name;
            diff = "";
            try
            {
                string name = tc.Name == null ? "" : tc.Name.Trim();
                if (name == "list_files")
                {
                    string sub = ArgOf(tc.Arguments, "subdir");
                    string text = Workspace.ListFiles(cfg.Workspace, sub);
                    summary = "• 列出工作区" + (string.IsNullOrEmpty(sub) ? "" : " / " + sub);
                    return text;
                }
                if (name == "read_file")
                {
                    string rel = ArgOf(tc.Arguments, "path");
                    int start = ArgInt(tc.Arguments, "start_line", 1);
                    int maxLines = ArgInt(tc.Arguments, "max_lines", Workspace.DefaultReadLines);

                    string cacheKey = ToolCacheKey(tc, rel);
                    if (cacheKey != null && toolCache.ContainsKey(cacheKey))
                    {
                        string old = toolCache[cacheKey];
                        summary = "• 读取 " + rel + "（与上次相同，已省略）";
                        if (old.Length <= 300) return old;
                        return "该文件与上次读取时完全一致，结果已省略（" + old.Length + " 字符）。如需重新查看，请说明。";
                    }

                    int first, last, total;
                    string text = Workspace.ReadTextPage(cfg.Workspace, rel, start, maxLines, out first, out last, out total);
                    if (cacheKey != null) toolCache[cacheKey] = text;
                    summary = "• 读取 " + rel + "（第 " + first + (last > first ? "-" + last : "") + " 行 / 共 " + total + " 行）";
                    return text;
                }
                if (name == "write_file")
                {
                    string rel = ArgOf(tc.Arguments, "path");
                    if (!ArgExists(tc.Arguments, "content"))
                        return "写入失败：工具参数缺少 content。请提供完整文件内容后重新调用 write_file。";
                    string content = ArgOf(tc.Arguments, "content");
                    if (Workspace.SameText(cfg.Workspace, rel, content))
                    {
                        fileChangedThisTurn = true;
                        summary = "• 内容未变化，跳过写入 " + rel;
                        return "文件内容与请求完全相同，无需重复写入：" + rel;
                    }

                    string oldText = "";
                    if (!OfficeFiles.IsOffice(rel))
                    {
                        try { if (Workspace.Exists(cfg.Workspace, rel) && !Directory.Exists(Workspace.Resolve(cfg.Workspace, rel))) oldText = Workspace.ReadText(cfg.Workspace, rel); }
                        catch { oldText = ""; }
                        diff = LineDiff.Build(oldText, content);
                    }

                    if (cfg.ConfirmWrites && !ConfirmWrite(rel, diff, content))
                    {
                        writeDeniedThisTurn = true;
                        return "用户拒绝了这次写入，请不要重复尝试。";
                    }
                    int n = Workspace.WriteText(cfg.Workspace, rel, content);
                    fileChangedThisTurn = true;
                    if (OfficeFiles.IsWorkbook(rel))
                    {
                        summary = "• 写入 " + rel + "（表格）";
                        return "写入成功：" + rel + "（已生成 .xlsx，内容按 CSV/TSV/表格解析）。";
                    }
                    if (OfficeFiles.IsDocument(rel))
                    {
                        summary = "• 写入 " + rel + "（文档）";
                        return "写入成功：" + rel + "（已生成 .docx，按文本段落写入）。";
                    }
                    summary = "• 写入 " + rel + "（" + n + " 字符）";
                    return "写入成功：" + rel + "，" + n + " 个字符。";
                }
                if (name == "delete_file")
                {
                    string rel = ArgOf(tc.Arguments, "path");
                    string full = Workspace.Resolve(cfg.Workspace, rel);
                    if (!File.Exists(full) && !Directory.Exists(full)) return "文件或目录本来就不存在，无需删除：" + rel;
                    if (cfg.ConfirmWrites && !ConfirmDelete(rel))
                    {
                        writeDeniedThisTurn = true;
                        return "用户拒绝了这次删除，请不要重复尝试。";
                    }
                    Workspace.Delete(cfg.Workspace, rel);
                    fileChangedThisTurn = true;
                    summary = "• 删除 " + rel;
                    return "已删除：" + rel;
                }
                return "未知工具：" + name;
            }
            catch (Exception ex)
            {
                summary = "• 工具执行失败：" + tc.Name;
                return "执行失败：" + ex.Message;
            }
        }

        private bool ConfirmWrite(string rel, string diff, string content)
        {
            if (autoApproveThisTurn) return true;
            string key = WriteKey(rel);
            if (approvedWritePaths.Contains(key)) return true;

            bool diffMode = !string.IsNullOrEmpty(diff);
            string detail = diffMode ? diff : content;
            ConfirmChoice c = Confirm.AskWrite(this, th, rel, detail, diffMode);
            if (c == ConfirmChoice.AllowTurn) autoApproveThisTurn = true;
            else if (c == ConfirmChoice.Allow) approvedWritePaths.Add(key);
            return c != ConfirmChoice.Deny;
        }

        private string WriteKey(string rel)
        {
            try { return Workspace.Resolve(cfg.Workspace, rel).ToLowerInvariant(); }
            catch { return (rel == null ? "" : rel.Trim()).ToLowerInvariant(); }
        }

        private bool ConfirmDelete(string rel)
        {
            if (autoApproveThisTurn) return true;
            ConfirmChoice c = Confirm.AskDelete(this, th, rel);
            if (c == ConfirmChoice.AllowTurn) autoApproveThisTurn = true;
            return c != ConfirmChoice.Deny;
        }

        /// <summary>读取文件的去重键：路径 + 参数 + 文件长度/修改时间；文件变了就不会命中旧结果。</summary>
        private string ToolCacheKey(ToolCall tc, string rel)
        {
            if (tc == null || string.IsNullOrEmpty(rel)) return null;
            try
            {
                string full = Workspace.Resolve(cfg.Workspace, rel);
                if (File.Exists(full))
                {
                    FileInfo fi = new FileInfo(full);
                    return "read\u0001" + (tc.Arguments == null ? "" : tc.Arguments)
                         + "\u0001" + fi.Length + "\u0001" + fi.LastWriteTimeUtc.Ticks;
                }
            }
            catch { }
            return null;
        }

        private static int ArgInt(string json, string key, int def)
        {
            int v;
            return int.TryParse(ArgOf(json, key), out v) ? v : def;
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
            if (!streaming && turnFinished) return;
            userStopped = true;
            requestSeq++; // 让旧请求的 OnDelta/OnDone 全部失效
            ChatClient c = client;
            client = null;
            if (c != null) { try { c.Cancel(); } catch { } }

            if (turnFinished) { SetBusyUi(false); SetStatus("● 已停止", false); return; }
            int total = PendingLength();
            if (revealed < total) revealed = total;
            FinishTurn();
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

        private int PendingLength()
        {
            lock (pendingLock) return pending.Length;
        }

        private string PendingText()
        {
            lock (pendingLock) return pending.ToString();
        }

        private string PendingText(int len)
        {
            lock (pendingLock)
            {
                if (len > pending.Length) len = pending.Length;
                if (len < 0) len = 0;
                return pending.ToString(0, len);
            }
        }

        private void ClearPending()
        {
            lock (pendingLock) pending.Length = 0;
        }

        /// <summary>流式输出的“打字机”：网络数据先缓冲，再按稳定速度逐字显示。</summary>
        private void StreamTick()
        {
            if (!streaming) return;
            int total = PendingLength();
            if (revealed < total)
            {
                int backlog = total - revealed;
                DateTime now = DateTime.UtcNow;
                double dt = (now - lastReveal).TotalSeconds;
                lastReveal = now;
                if (dt <= 0) dt = 0.035;
                if (dt > 0.12) dt = 0.12; // 网络停顿后不要一次吐一大段

                revealCarry += RevealCharsPerSecond * dt;
                int step = (int)revealCarry;
                revealCarry -= step;

                // 积压较多时适当追赶，避免网络结束后还要等太久
                int catchup = backlog / 24;
                if (catchup > step) step = catchup;
                if (step < 1) step = 1;

                revealed += step;
                if (revealed > total) revealed = total;
                FlushStream();
            }
            else
            {
                lastReveal = DateTime.UtcNow;
            }

            if (networkDone && revealed >= PendingLength()) FinishTurn();
        }

        private void FlushStream()
        {
            FlushStream(false);
        }

        private void FlushStream(bool forceFull)
        {
            if (streamingIndex < 0 || streamingIndex >= views.Count) return;
            MessageView v = views[streamingIndex];
            string full = PendingText(revealed);
            if (forceFull) v.SetText(full, true);
            else v.SetStreamText(full);
            LayoutMessages(atBottom);
        }

        private void SetBusyUi(bool busy)
        {
            btnSend.IsStop = busy;
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
            if (!ok && (text.Contains("失败") || text.Contains("无效") || text.Contains("错误"))) c = th.Danger;
            lblStatus.ForeColor = c;
            LayoutHeader();
        }

        private void UpdateStatusIdle()
        {
            if (string.IsNullOrEmpty(cfg.BaseUrl)) SetStatus("● 未配置接口", false);
            else if (string.IsNullOrEmpty(cfg.ApiKey)) SetStatus("● 未配置 Key", false);
            else SetStatus("● 就绪", true);
        }

        private void UpdateTokenLabel()
        {
            if (lblTokens == null) return;
            lblTokens.Text = lastInputTokens > 0
                ? "Token ≈ " + ContextBudget.FormatTokens(lastInputTokens)
                : "Token ≈ —";
            lblTokens.ForeColor = th.SubText;
            LayoutHeader();
        }

        private int readyCheckSeq;
        private void StartReadyCheck()
        {
            if (streaming) return;
            if (string.IsNullOrEmpty(cfg.BaseUrl)) { SetStatus("● 未配置接口", false); return; }
            if (string.IsNullOrEmpty(cfg.ApiKey)) { SetStatus("● 未配置 Key", false); return; }

            int seq = ++readyCheckSeq;
            SetStatus("● 检查中…", false);
            Config snapshot = cfg.Clone();
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string state = ChatClient.CheckConnection(snapshot);
                SafeInvoke(delegate
                {
                    if (seq != readyCheckSeq || streaming) return;
                    SetStatus("● " + state, state == "就绪");
                });
            });
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
                    th = Theme.Create(Theme.ResolveDark(cfg.ThemeMode));
                    ApplyThemeAll();
                    UpdateModelLabel();
                    UpdateStatusIdle();
                    StartReadyCheck();
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
            try { if (themeWatch != null) { themeWatch.Stop(); themeWatch.Dispose(); themeWatch = null; } } catch { }
            try { if (tick != null) { tick.Stop(); tick.Dispose(); tick = null; } } catch { }
            try { Application.RemoveMessageFilter(this); } catch { }
            base.OnFormClosed(e);
        }

        private void CheckSystemTheme()
        {
            if (cfg.ThemeMode != Theme.ModeSystem) return;
            bool dark = Theme.IsSystemDark();
            if (dark == th.IsDark) return;
            th = Theme.Create(dark);
            ApplyThemeAll();
            if (!streaming) UpdateStatusIdle();
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
                    sb.Append("B\t").Append(B64(s.Title))
                      .Append('\t').Append(s.Pinned ? "1" : "0")
                      .Append('\t').Append(s.Created.Ticks.ToString())
                      .Append('\t').Append(s.Updated.Ticks.ToString())
                      .Append('\n');
                    if (s.LastInputTokens > 0)
                        sb.Append("T\t").Append(s.LastInputTokens).Append('\n');
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
                        cur.Title = UnB64(line.Substring(2).Split('\t')[0]);
                        string[] bp = line.Substring(2).Split('\t');
                        if (bp.Length >= 4)
                        {
                            cur.Pinned = bp[1] == "1";
                            long ticks;
                            if (long.TryParse(bp[2], out ticks)) { try { cur.Created = new DateTime(ticks); } catch { } }
                            if (long.TryParse(bp[3], out ticks)) { try { cur.Updated = new DateTime(ticks); } catch { } }
                        }
                        list.Add(cur);
                    }
                    else if (line.StartsWith("T\t") && cur != null)
                    {
                        int n;
                        if (int.TryParse(line.Substring(2), out n)) cur.LastInputTokens = n;
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
