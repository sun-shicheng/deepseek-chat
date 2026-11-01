using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DsChat
{
    /// <summary>极简 Markdown 渲染：代码块、行内代码、加粗、列表符号。流式输出时反复重绘。</summary>
    public static class Markdown
    {
        public static void Render(RichTextBox rtb, string text, Color fg, Color codeBg, Color codeFg, Font body, Font mono)
        {
            rtb.SuspendLayout();
            rtb.Clear();
            rtb.ForeColor = fg;

            if (text == null) text = "";
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");
            string[] lines = text.Split('\n');

            bool inCode = false;
            for (int li = 0; li < lines.Length; li++)
            {
                string line = lines[li];
                string trimmed = line.TrimStart();

                if (trimmed.StartsWith("```"))
                {
                    inCode = !inCode;
                    if (inCode && li + 1 < lines.Length && !lines[li + 1].TrimStart().StartsWith("```"))
                    {
                        // 跳过语言标识行由下一轮自然处理
                    }
                    continue;
                }

                if (inCode)
                {
                    AppendCode(rtb, line, mono, codeBg, codeFg);
                }
                else
                {
                    string render = line;
                    if (render.StartsWith("- ") || render.StartsWith("* ")) render = "• " + render.Substring(2);
                    else if (render.StartsWith("### ")) { AppendBold(rtb, render.Substring(4), body); if (li < lines.Length - 1) rtb.AppendText("\n"); continue; }
                    else if (render.StartsWith("## ")) { AppendBold(rtb, render.Substring(3), body); if (li < lines.Length - 1) rtb.AppendText("\n"); continue; }
                    else if (render.StartsWith("# ")) { AppendBold(rtb, render.Substring(2), body); if (li < lines.Length - 1) rtb.AppendText("\n"); continue; }
                    RenderInline(rtb, render, fg, codeBg, codeFg, body, mono);
                }

                if (li < lines.Length - 1) rtb.AppendText("\n");
            }
            rtb.ResumeLayout();
        }

        private static void AppendCode(RichTextBox rtb, string line, Font mono, Color bg, Color fg)
        {
            rtb.SelectionFont = mono;
            rtb.SelectionColor = fg;
            rtb.SelectionBackColor = bg;
            rtb.AppendText(line);
            rtb.SelectionBackColor = rtb.BackColor;
        }

        private static void AppendBold(RichTextBox rtb, string text, Font body)
        {
            using (Font bold = new Font(body, FontStyle.Bold))
            {
                rtb.SelectionFont = bold;
                rtb.SelectionColor = rtb.ForeColor;
                rtb.AppendText(text);
            }
        }

        private static void RenderInline(RichTextBox rtb, string s, Color fg, Color codeBg, Color codeFg, Font body, Font mono)
        {
            int i = 0;
            System.Text.StringBuilder buf = new System.Text.StringBuilder();

            while (i < s.Length)
            {
                if (s[i] == '`')
                {
                    int end = s.IndexOf('`', i + 1);
                    if (end > i)
                    {
                        Flush(rtb, buf, body, fg);
                        rtb.SelectionFont = mono;
                        rtb.SelectionColor = codeFg;
                        rtb.SelectionBackColor = codeBg;
                        rtb.AppendText(s.Substring(i + 1, end - i - 1));
                        rtb.SelectionBackColor = rtb.BackColor;
                        i = end + 1;
                        continue;
                    }
                }
                else if (s[i] == '*' && i + 1 < s.Length && s[i + 1] == '*')
                {
                    int end = s.IndexOf("**", i + 2, StringComparison.Ordinal);
                    if (end > i + 1)
                    {
                        Flush(rtb, buf, body, fg);
                        using (Font bold = new Font(body, FontStyle.Bold))
                        {
                            rtb.SelectionFont = bold;
                            rtb.SelectionColor = fg;
                            rtb.AppendText(s.Substring(i + 2, end - i - 2));
                        }
                        i = end + 2;
                        continue;
                    }
                }
                buf.Append(s[i]);
                i++;
            }
            Flush(rtb, buf, body, fg);
        }

        private static void Flush(RichTextBox rtb, System.Text.StringBuilder buf, Font body, Color fg)
        {
            if (buf.Length == 0) return;
            rtb.SelectionFont = body;
            rtb.SelectionColor = fg;
            rtb.AppendText(buf.ToString());
            buf.Length = 0;
        }
    }

    public static class Round
    {
        public static GraphicsPath Path(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            if (d <= 0 || d > Math.Min(r.Width, r.Height)) d = Math.Min(r.Width, r.Height);
            if (d <= 1) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    /// <summary>单条消息视图：用户消息为右侧圆角气泡，助手消息为通栏文本。</summary>
    public class MessageView : Panel
    {
        public ChatMsg Msg;
        public bool IsUser;
        public bool IsError;
        public bool IsTyping;
        public bool IsTool;

        private RichTextBox rtb;
        private Theme th;
        private Font body;
        private Font mono;
        private string text = "";

        public MessageView(ChatMsg msg, bool isUser, Theme theme)
        {
            Msg = msg;
            IsUser = isUser;
            th = theme;
            body = new Font("Microsoft YaHei UI", 10f);
            mono = new Font("Consolas", 9.5f);
            text = msg.Content == null ? "" : msg.Content;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = th.Bg;

            rtb = new RichTextBox();
            rtb.BorderStyle = BorderStyle.None;
            rtb.ReadOnly = true;
            rtb.ScrollBars = RichTextBoxScrollBars.None;
            rtb.DetectUrls = true;
            rtb.TabStop = false;
            rtb.WordWrap = true;
            rtb.BackColor = isUser ? th.UserBubble : th.Bg;
            rtb.ForeColor = isUser ? th.UserText : th.BotText;
            rtb.Cursor = Cursors.IBeam;
            rtb.ShortcutsEnabled = true;
            Controls.Add(rtb);
            Render();
        }

        public void ApplyTheme(Theme theme)
        {
            th = theme;
            BackColor = th.Bg;
            rtb.BackColor = IsUser ? th.UserBubble : th.Bg;
            Render();
        }

        public void SetText(string s)
        {
            text = s == null ? "" : s;
            Render();
            Reflow(Width);
        }

        public string GetText() { return text; }

        public void SetError()
        {
            IsError = true;
            Render();
            Reflow(Width);
        }

        /// <summary>工具调用的提示行：用次要色，视觉上像系统提示。</summary>
        public void SetTool()
        {
            IsTool = true;
            Render();
            Reflow(Width);
        }

        private void Render()
        {
            Color fg = IsError ? th.Danger : (IsUser ? th.UserText : (IsTool ? th.SubText : th.BotText));
            rtb.ForeColor = fg;
            if (IsError)
            {
                rtb.SelectionFont = body;
                rtb.SelectionColor = fg;
                rtb.Text = text;
                return;
            }
            Markdown.Render(rtb, text, fg, th.CodeBg, th.CodeText, body, mono);
        }

        /// <summary>按可用宽度重排，返回占用的高度。</summary>
        public int Reflow(int maxWidth)
        {
            int padL = IsUser ? Ui.Px(14) : Ui.Px(2);
            int padR = IsUser ? Ui.Px(14) : Ui.Px(2);
            int padT = IsUser ? Ui.Px(10) : Ui.Px(2);
            int padB = IsUser ? Ui.Px(10) : Ui.Px(2);
            int limit = Math.Max(Ui.Px(120), maxWidth);

            int inner;
            if (IsUser)
            {
                string plain = text.Replace("\n", " ");
                Size natural = TextRenderer.MeasureText(plain, body, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                int want = natural.Width + padL + padR + Ui.Px(6);
                inner = Math.Min(limit - padL - padR, Math.Max(Ui.Px(180), want));
            }
            else
            {
                inner = limit - padL - padR;
            }

            rtb.SetBounds(padL, padT, Math.Max(Ui.Px(80), inner), Ui.Px(10));
            int h = MeasureHeight(Math.Max(Ui.Px(80), inner));
            rtb.Height = h;

            int w = IsUser ? Math.Max(Ui.Px(80), inner) + padL + padR : limit;
            Width = w;
            Height = h + padT + padB;
            return Height;
        }

        private int MeasureHeight(int width)
        {
            if (rtb.TextLength == 0) return body.Height + Ui.Px(8);
            try
            {
                int idx = rtb.TextLength - 1;
                while (idx > 0)
                {
                    string ch = rtb.Text.Substring(idx, 1);
                    if (ch != "\n" && ch != "\r") break;
                    idx--;
                }
                Point p = rtb.GetPositionFromCharIndex(idx);
                return Math.Max(body.Height + Ui.Px(6), p.Y + body.Height + Ui.Px(6));
            }
            catch { return body.Height * 2; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush bg = new SolidBrush(th.Bg))
                e.Graphics.FillRectangle(bg, ClientRectangle);

            if (IsUser)
            {
                Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
                using (GraphicsPath path = Round.Path(r, Ui.Px(16)))
                using (SolidBrush b = new SolidBrush(th.UserBubble))
                    e.Graphics.FillPath(b, path);
            }
            base.OnPaint(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                body.Dispose();
                mono.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
