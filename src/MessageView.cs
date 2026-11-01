using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DsChat
{
    public class CodeBlockInfo
    {
        public int HeaderStart;
        public int Start;
        public int End;
        public string Language = "";
        public string Code = "";
        public bool Closed;
    }

    /// <summary>表格在正文中的位置和原始单元格，用于整块表格覆盖层。</summary>
    public class TableBlockInfo
    {
        public int Start;
        public int End;
        public List<int> LineStarts = new List<int>();
        public List<string[]> Rows = new List<string[]>();
    }

    /// <summary>极简 Markdown 渲染：代码块、行内代码、加粗、列表符号。流式输出时反复重绘。</summary>
    public static class Markdown
    {
        private const int WM_SETREDRAW = 0x000B;
        private static readonly Dictionary<string, Font> fontCache = new Dictionary<string, Font>();

        private static Font GetFont(Font baseFont, float size, FontStyle style)
        {
            string key = baseFont.FontFamily.Name + "|" + size.ToString("0.##") + "|" + (int)style;
            Font f;
            if (!fontCache.TryGetValue(key, out f))
            {
                f = new Font(baseFont.FontFamily, size, style, GraphicsUnit.Point);
                fontCache[key] = f;
            }
            return f;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private static void Redraw(Control c, bool on)
        {
            try
            {
                if (c.IsHandleCreated)
                    SendMessage(c.Handle, WM_SETREDRAW, on ? (IntPtr)1 : IntPtr.Zero, IntPtr.Zero);
            }
            catch { }
        }

        /// <summary>先追加再对新增范围套用字体/颜色，避免 RichTextBox 在插入点设置字体时丢失格式。</summary>
        public static void AppendStyled(RichTextBox rtb, string text, Font font, Color color, Color back)
        {
            if (string.IsNullOrEmpty(text)) return;
            int start = rtb.TextLength;
            rtb.SelectionStart = start;
            rtb.SelectionLength = 0;
            rtb.AppendText(text);
            int end = rtb.TextLength;
            if (end <= start) return;
            rtb.Select(start, end - start);
            if (font != null) rtb.SelectionFont = font;
            rtb.SelectionColor = color;
            rtb.SelectionBackColor = back == Color.Empty ? rtb.BackColor : back;
            rtb.SelectionStart = end;
            rtb.SelectionLength = 0;
        }

        public static void Render(RichTextBox rtb, string text, Color fg, Theme th, Font body, Font mono, System.Collections.Generic.List<CodeBlockInfo> blocks, System.Collections.Generic.List<TableBlockInfo> tables)
        {
            rtb.SuspendLayout();
            bool redraw = rtb.IsHandleCreated;
            if (redraw) Redraw(rtb, false);
            try
            {
                RenderCore(rtb, text, fg, th, body, mono, blocks, tables);
            }
            finally
            {
                rtb.ResumeLayout();
                if (redraw)
                {
                    Redraw(rtb, true);
                    rtb.Invalidate();
                }
            }
        }

        private static void RenderCore(RichTextBox rtb, string text, Color fg, Theme th, Font body, Font mono, System.Collections.Generic.List<CodeBlockInfo> blocks, System.Collections.Generic.List<TableBlockInfo> tables)
        {
            Color codeBg = th.CodeBg;
            Color codeFg = th.CodeText;
            rtb.Clear();
            rtb.ForeColor = fg;

            if (text == null) text = "";
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");
            string[] lines = text.Split('\n');

            bool inCode = false;
            string codeLang = "";
            System.Text.StringBuilder codeText = new System.Text.StringBuilder();
            int headerStart = -1;
            int codeStart = -1;

            for (int li = 0; li < lines.Length; li++)
            {
                string line = lines[li];
                string trimmed = line.TrimStart();

                if (trimmed.StartsWith("```"))
                {
                    if (!inCode)
                    {
                        inCode = true;
                        codeLang = trimmed.Length > 3 ? trimmed.Substring(3).Trim() : "";
                        headerStart = rtb.TextLength;
                        AppendCodeHeader(rtb, codeLang, mono, codeBg, codeFg);
                        codeStart = rtb.TextLength;
                        codeText.Length = 0;
                    }
                    else
                    {
                        inCode = false;
                        if (blocks != null)
                        {
                            string code = codeText.ToString();
                            if (code.EndsWith("\n")) code = code.Substring(0, code.Length - 1);
                            CodeBlockInfo info = new CodeBlockInfo();
                            info.HeaderStart = headerStart;
                            info.Start = codeStart;
                            info.End = rtb.TextLength;
                            info.Language = codeLang;
                            info.Code = code;
                            info.Closed = true;
                            blocks.Add(info);
                        }
                    }
                    continue;
                }

                if (inCode)
                {
                    codeText.Append(line).Append('\n');
                    AppendCodeLine(rtb, line, mono, codeBg, codeFg, codeLang, th);
                }
                else
                {
                    if (IsTableStart(lines, li))
                    {
                        li = RenderTablePlaceholder(rtb, lines, li, fg, body, tables);
                        continue;
                    }

                    int hlevel;
                    string htext;
                    if (TryHeading(trimmed, out hlevel, out htext))
                    {
                        RenderHeading(rtb, htext, hlevel, fg, codeBg, codeFg, body, mono);
                    }
                    else if (trimmed.StartsWith(">"))
                    {
                        RenderQuote(rtb, trimmed, th, body, mono, codeBg, codeFg);
                    }
                    else if (IsHorizontalRule(trimmed))
                    {
                        RenderHorizontalRule(rtb, th, body);
                    }
                    else if (!TryRenderList(rtb, line, th, body, mono, fg, codeBg, codeFg))
                    {
                        RenderInline(rtb, line, fg, codeBg, codeFg, body, mono);
                    }
                }

                if (li < lines.Length - 1)
                {
                    if (inCode)
                    {
                        AppendStyled(rtb, "\n", mono, codeFg, codeBg);
                    }
                    else rtb.AppendText("\n");
                }
            }

            if (inCode && blocks != null && headerStart >= 0)
            {
                string code = codeText.ToString();
                if (code.EndsWith("\n")) code = code.Substring(0, code.Length - 1);
                CodeBlockInfo info = new CodeBlockInfo();
                info.HeaderStart = headerStart;
                info.Start = codeStart;
                info.End = rtb.TextLength;
                info.Language = codeLang;
                info.Code = code;
                info.Closed = false;
                blocks.Add(info);
            }
        }

        public static void RenderCodeBlock(RichTextBox rtb, string lang, string code, Theme th, Font mono)
        {
            rtb.SuspendLayout();
            bool redraw = rtb.IsHandleCreated;
            if (redraw) Redraw(rtb, false);
            try
            {
                rtb.Clear();
                rtb.BackColor = th.CodeBg;
                rtb.ForeColor = th.CodeText;
                AppendCodeHeader(rtb, string.IsNullOrEmpty(lang) ? "code" : lang, mono, th.CodeBg, th.CodeText);

                string body = code == null ? "" : code.Replace("\r\n", "\n").Replace("\r", "\n");
                string[] lines = body.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    AppendCodeLine(rtb, lines[i], mono, th.CodeBg, th.CodeText, lang, th);
                    if (i < lines.Length - 1 || body.Length > 0)
                    {
                        AppendStyled(rtb, "\n", mono, th.CodeText, th.CodeBg);
                    }
                }
            }
            finally
            {
                rtb.ResumeLayout();
                if (redraw)
                {
                    Redraw(rtb, true);
                    rtb.Invalidate();
                }
            }
        }

        private static bool IsTableStart(string[] lines, int li)
        {
            if (li + 1 >= lines.Length) return false;
            if (lines[li].IndexOf('|') < 0) return false;
            return IsTableSeparator(lines[li + 1]);
        }

        private static bool IsTableSeparator(string line)
        {
            string t = line.Trim();
            if (t.Length == 0 || t.IndexOf('|') < 0) return false;
            string[] cells = SplitTableRow(t);
            bool any = false;
            foreach (string raw in cells)
            {
                string c = raw.Trim();
                if (c.Length == 0) continue;
                any = true;
                string core = c.Trim(':');
                if (core.Length < 3) return false;
                foreach (char ch in core) if (ch != '-') return false;
            }
            return any;
        }

        private static string[] SplitTableRow(string line)
        {
            string t = line.Trim();
            if (t.StartsWith("|")) t = t.Substring(1);
            if (t.EndsWith("|")) t = t.Substring(0, t.Length - 1);
            string[] parts = t.Split('|');
            for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim();
            return parts;
        }

        private static int RenderTablePlaceholder(RichTextBox rtb, string[] lines, int start, Color fg, Font body, List<TableBlockInfo> tables)
        {
            List<string[]> rows = new List<string[]>();
            rows.Add(SplitTableRow(lines[start]));
            int end = start + 1;
            for (int j = start + 2; j < lines.Length; j++)
            {
                if (string.IsNullOrWhiteSpace(lines[j]) || lines[j].IndexOf('|') < 0) break;
                if (IsTableSeparator(lines[j])) break;
                rows.Add(SplitTableRow(lines[j]));
                end = j;
            }
            if (rows.Count == 0) return start;

            TableBlockInfo info = new TableBlockInfo();
            info.Rows = rows;
            for (int r = 0; r < rows.Count; r++)
            {
                if (r > 0) rtb.AppendText("\n");
                info.LineStarts.Add(rtb.TextLength);
                AppendStyled(rtb, " ", body, fg, rtb.BackColor);
            }
            info.Start = info.LineStarts[0];
            info.End = rtb.TextLength;
            if (tables != null) tables.Add(info);
            if (end < lines.Length - 1) rtb.AppendText("\n");
            return end;
        }

        private static bool TryHeading(string trimmed, out int level, out string text)
        {
            int n = 0;
            while (n < trimmed.Length && trimmed[n] == '#') n++;
            if (n >= 1 && n <= 6 && n < trimmed.Length && trimmed[n] == ' ')
            {
                level = n;
                text = trimmed.Substring(n + 1).Trim();
                return true;
            }
            level = 0;
            text = "";
            return false;
        }

        private static void RenderHeading(RichTextBox rtb, string text, int level, Color fg, Color codeBg, Color codeFg, Font body, Font mono)
        {
            float[] scales = new float[] { 1.8f, 1.55f, 1.3f, 1.15f, 1.05f, 1.0f };
            float size = body.Size * scales[Math.Max(0, Math.Min(scales.Length - 1, level - 1))];
            if (size > 22f) size = 22f;
            Font hf = GetFont(body, size, FontStyle.Bold);
            RenderInline(rtb, text, fg, codeBg, codeFg, hf, mono);
        }

        private static void RenderQuote(RichTextBox rtb, string trimmed, Theme th, Font body, Font mono, Color codeBg, Color codeFg)
        {
            string content = trimmed.Length > 1 ? trimmed.Substring(1) : "";
            if (content.StartsWith(" ")) content = content.Substring(1);
            AppendStyled(rtb, "│ ", body, th.QuoteBar, rtb.BackColor);
            RenderInline(rtb, content, th.QuoteText, codeBg, codeFg, body, mono);
        }

        private static bool IsHorizontalRule(string trimmed)
        {
            string s = trimmed.Trim();
            if (s.Length < 3) return false;
            char c = s[0];
            if (c != '-' && c != '*' && c != '_') return false;
            bool any = false;
            foreach (char ch in s)
            {
                if (ch == ' ') continue;
                if (ch != c) return false;
                any = true;
            }
            return any;
        }

        private static void RenderHorizontalRule(RichTextBox rtb, Theme th, Font body)
        {
            AppendStyled(rtb, new string('─', 40), body, th.Border, rtb.BackColor);
        }

        private static bool TryRenderList(RichTextBox rtb, string line, Theme th, Font body, Font mono, Color fg, Color codeBg, Color codeFg)
        {
            int indentLen = line.Length - line.TrimStart().Length;
            string indent = new string(' ', indentLen);
            string c = line.Substring(indentLen);
            string prefix = null;
            string rest = null;
            if (c.StartsWith("- [ ] "))
            {
                prefix = "☐ ";
                rest = c.Substring(6);
            }
            else if (c.StartsWith("- [x] ") || c.StartsWith("- [X] "))
            {
                prefix = "☑ ";
                rest = c.Substring(6);
            }
            else if (c.StartsWith("- ") || c.StartsWith("* ") || c.StartsWith("+ "))
            {
                prefix = "• ";
                rest = c.Substring(2);
            }
            else
            {
                int d = 0;
                while (d < c.Length && char.IsDigit(c[d])) d++;
                if (d > 0 && d + 1 < c.Length && (c[d] == '.' || c[d] == ')') && c[d + 1] == ' ')
                {
                    prefix = c.Substring(0, d + 2);
                    rest = c.Substring(d + 2);
                }
            }
            if (prefix == null) return false;

            AppendStyled(rtb, indent + prefix, body, th.SubText, rtb.BackColor);
            RenderInline(rtb, rest, fg, codeBg, codeFg, body, mono);
            return true;
        }

        private static void AppendCodeHeader(RichTextBox rtb, string lang, Font mono, Color bg, Color fg)
        {
            AppendStyled(rtb, "  " + (string.IsNullOrEmpty(lang) ? "code" : lang) + "\n\n",
                mono, Color.FromArgb(150, fg), bg);
        }

        private static void AppendCodeLine(RichTextBox rtb, string line, Font mono, Color bg, Color fg, string lang, Theme th)
        {
            AppendHighlighted(rtb, line, lang, mono, bg, fg, th);
        }

        private static readonly string[] Keywords = new string[]
        {
            "var","if","else","for","while","return","class","public","private","protected","static","void",
            "int","long","float","double","string","bool","byte","char","object","def","import","from","as",
            "function","const","let","new","try","catch","finally","throw","switch","case","break","continue",
            "true","false","null","None","True","False","async","await","using","namespace","struct","enum",
            "interface","extends","implements","package","func","go","defer","type","map","chan","range",
            "SELECT","FROM","WHERE","INSERT","UPDATE","DELETE","CREATE","TABLE","JOIN","GROUP","ORDER",
            "readonly","ref","out","in","is","not","and","or","elif","lambda","with","yield","pass"
        };

        private static bool IsKeyword(string s)
        {
            for (int i = 0; i < Keywords.Length; i++)
                if (Keywords[i] == s) return true;
            return false;
        }

        private static void AppendHighlighted(RichTextBox rtb, string line, string lang, Font mono, Color bg, Color fg, Theme th)
        {
            int i = 0;
            while (i < line.Length)
            {
                char c = line[i];
                if ((c == '/' && i + 1 < line.Length && line[i + 1] == '/') || c == '#')
                {
                    AppendStyled(rtb, line.Substring(i), mono, th.CodeComment, bg);
                    return;
                }
                if (c == '"' || c == '\'')
                {
                    int j = i + 1;
                    while (j < line.Length)
                    {
                        if (line[j] == '\\') { j += 2; continue; }
                        if (line[j] == c) { j++; break; }
                        j++;
                    }
                    AppendStyled(rtb, line.Substring(i, Math.Min(line.Length, j) - i), mono, th.CodeString, bg);
                    i = j;
                    continue;
                }
                if (char.IsDigit(c))
                {
                    int j = i + 1;
                    while (j < line.Length && (char.IsDigit(line[j]) || line[j] == '.' || line[j] == '_')) j++;
                    AppendStyled(rtb, line.Substring(i, j - i), mono, th.CodeNumber, bg);
                    i = j;
                    continue;
                }
                if (char.IsLetter(c) || c == '_')
                {
                    int j = i + 1;
                    while (j < line.Length && (char.IsLetterOrDigit(line[j]) || line[j] == '_')) j++;
                    string word = line.Substring(i, j - i);
                    AppendStyled(rtb, word, mono, IsKeyword(word) ? th.CodeKeyword : fg, bg);
                    i = j;
                    continue;
                }
                AppendStyled(rtb, c.ToString(), mono, fg, bg);
                i++;
            }
        }

        private static void AppendCode(RichTextBox rtb, string line, Font mono, Color bg, Color fg)
        {
            AppendStyled(rtb, line, mono, fg, bg);
        }

        private static void AppendBold(RichTextBox rtb, string text, Font body)
        {
            Font bold = GetFont(body, body.Size, FontStyle.Bold);
            AppendStyled(rtb, text, bold, rtb.ForeColor, rtb.BackColor);
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
                        AppendStyled(rtb, s.Substring(i + 1, end - i - 1), mono, codeFg, codeBg);
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
                        Font bold = GetFont(body, body.Size, FontStyle.Bold);
                        AppendStyled(rtb, s.Substring(i + 2, end - i - 2), bold, fg, rtb.BackColor);
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
            AppendStyled(rtb, buf.ToString(), body, fg, rtb.BackColor);
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

    /// <summary>按行比较文本，输出 + / - / 空格 前缀的简易 diff。</summary>
    public static class LineDiff
    {
        public static string Build(string oldText, string newText)
        {
            string a = Normalize(oldText);
            string b = Normalize(newText);
            if (a == b) return "";
            if (a.Length == 0) return PrefixAll(b, "+ ");

            string[] aa = a.Split('\n');
            string[] bb = b.Split('\n');
            int n = aa.Length;
            int m = bb.Length;

            int start = 0;
            while (start < n && start < m && aa[start] == bb[start]) start++;
            int endA = n - 1;
            int endB = m - 1;
            while (endA >= start && endB >= start && aa[endA] == bb[endB]) { endA--; endB--; }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < start; i++) sb.Append("  ").Append(Clip(aa[i])).Append('\n');

            int midN = endA - start + 1;
            int midM = endB - start + 1;
            if (midN <= 0)
            {
                for (int j = start; j <= endB; j++) sb.Append("+ ").Append(Clip(bb[j])).Append('\n');
            }
            else if (midM <= 0)
            {
                for (int i = start; i <= endA; i++) sb.Append("- ").Append(Clip(aa[i])).Append('\n');
            }
            else if ((long)midN * midM <= 1000000)
            {
                AppendLcs(sb, aa, bb, start, endA, start, endB);
            }
            else
            {
                for (int i = start; i <= endA; i++) sb.Append("- ").Append(Clip(aa[i])).Append('\n');
                for (int j = start; j <= endB; j++) sb.Append("+ ").Append(Clip(bb[j])).Append('\n');
            }

            for (int i = endA + 1; i < n; i++) sb.Append("  ").Append(Clip(aa[i])).Append('\n');
            return sb.ToString();
        }

        private static void AppendLcs(System.Text.StringBuilder sb, string[] a, string[] b, int a0, int a1, int b0, int b1)
        {
            int n = a1 - a0 + 1;
            int m = b1 - b0 + 1;
            int[,] dp = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
                for (int j = m - 1; j >= 0; j--)
                    dp[i, j] = a[a0 + i] == b[b0 + j] ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);

            int x = 0, y = 0;
            while (x < n && y < m)
            {
                if (a[a0 + x] == b[b0 + y]) { sb.Append("  ").Append(Clip(a[a0 + x])).Append('\n'); x++; y++; }
                else if (dp[x + 1, y] >= dp[x, y + 1]) { sb.Append("- ").Append(Clip(a[a0 + x])).Append('\n'); x++; }
                else { sb.Append("+ ").Append(Clip(b[b0 + y])).Append('\n'); y++; }
            }
            while (x < n) { sb.Append("- ").Append(Clip(a[a0 + x])).Append('\n'); x++; }
            while (y < m) { sb.Append("+ ").Append(Clip(b[b0 + y])).Append('\n'); y++; }
        }

        private static string PrefixAll(string text, string prefix)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (string line in text.Split('\n'))
                sb.Append(prefix).Append(Clip(line)).Append('\n');
            return sb.ToString();
        }

        private static string Normalize(string s)
        {
            if (s == null) return "";
            return s.Replace("\r\n", "\n").Replace("\r", "\n");
        }

        private static string Clip(string s)
        {
            if (s == null) return "";
            return s.Length > 400 ? s.Substring(0, 400) + "…" : s;
        }
    }

    /// <summary>整块代码块视图：铺满宽度的背景 + 语法高亮 + 复制按钮。</summary>
    public class CodeBlockView : Panel
    {
        private Theme th;
        private RichTextBox code;
        private Label copy;
        private string raw;
        private Font textFont;
        public event MouseEventHandler MenuRequested;

        public CodeBlockView(Theme theme, Font textFont, Font baseFont, string lang, string codeText)
        {
            th = theme;
            raw = codeText == null ? "" : codeText;
            this.textFont = textFont;
            BackColor = th.CodeBg;
            DoubleBuffered = true;
            TabStop = false;

            code = new RichTextBox();
            code.BorderStyle = BorderStyle.None;
            code.ReadOnly = true;
            code.TabStop = false;
            code.WordWrap = true;
            code.ScrollBars = RichTextBoxScrollBars.None;
            code.DetectUrls = false;
            code.ShortcutsEnabled = true;
            code.Font = baseFont == null ? textFont : baseFont;
            code.BackColor = th.CodeBg;
            code.ForeColor = th.CodeText;
            code.MouseUp += ForwardMenu;
            Markdown.RenderCodeBlock(code, lang, raw, th, textFont);
            Controls.Add(code);

            copy = new Label();
            copy.Text = "复制";
            copy.AutoSize = false;
            copy.TextAlign = ContentAlignment.MiddleCenter;
            copy.Font = new Font("Microsoft YaHei UI", 8f);
            copy.BackColor = th.CodeBg;
            copy.ForeColor = th.SubText;
            copy.Cursor = Cursors.Hand;
            copy.MouseEnter += delegate { copy.ForeColor = th.Text; };
            copy.MouseLeave += delegate { copy.ForeColor = th.SubText; };
            copy.MouseUp += ForwardMenu;
            copy.Click += delegate
            {
                try
                {
                    Clipboard.SetText(raw);
                    copy.Text = "已复制";
                    System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
                    t.Interval = 900;
                    t.Tick += delegate { t.Stop(); if (!copy.IsDisposed) copy.Text = "复制"; t.Dispose(); };
                    t.Start();
                }
                catch { }
            };
            Controls.Add(copy);

            MouseUp += ForwardMenu;
        }

        private void ForwardMenu(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right && MenuRequested != null) MenuRequested(this, e);
        }

        /// <summary>放到指定位置并铺满整块背景。</summary>
        public void LayoutAt(int x, int y, int width, int height, int lineHeight)
        {
            width = Math.Max(Ui.Px(80), width);
            height = Math.Max(Ui.Px(28), height);
            SetBounds(x, y, width, height);
            code.SetBounds(1, 1, Math.Max(1, width - 2), Math.Max(1, height - 2));

            int lh = Math.Max(Ui.Px(8), lineHeight);
            for (int i = 0; i < 3; i++)
            {
                SetLineHeight(lh);
                int needed = MeasureNeeded();
                if (needed <= height || lh <= code.Font.Height + Ui.Px(4)) break;
                int next = (int)Math.Floor(lh * (height - Ui.Px(2)) / (double)needed);
                next = Math.Max(code.Font.Height + Ui.Px(4), next);
                if (next >= lh) break;
                lh = next;
            }

            int cw = Ui.Px(56);
            int ch = Ui.Px(22);
            copy.SetBounds(Math.Max(0, width - cw - Ui.Px(4)), Ui.Px(2), cw, ch);
            copy.BringToFront();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen pen = new Pen(th.CodeBorder, 1f))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        private int MeasureNeeded()
        {
            try
            {
                string all = code.Text;
                int idx = all.Length - 1;
                while (idx > 0 && (all[idx] == '\n' || all[idx] == '\r')) idx--;
                if (idx < 0) return 0;
                Point p = code.GetPositionFromCharIndex(idx);
                return p.Y + code.Font.Height + Ui.Px(6);
            }
            catch { return 0; }
        }

        // RichEdit 的段落行距：让覆盖层代码与主 RichTextBox 的行高完全一致。
        private const int EM_SETPARAFORMAT = 0x0447;
        private const uint PFM_LINESPACING = 0x00000100;
        private const int SCF_ALL = 4;

        [StructLayout(LayoutKind.Sequential)]
        private struct PARAFORMAT2
        {
            public int cbSize;
            public uint dwMask;
            public short wNumbering;
            public short wReserved;
            public int dxStartIndent;
            public int dxRightIndent;
            public int dxOffset;
            public short wAlignment;
            public short cTabCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public int[] rgxTabs;
            public int dySpaceBefore;
            public int dySpaceAfter;
            public int dyLineSpacing;
            public short sStyle;
            public byte bLineSpacingRule;
            public byte bOutlineLevel;
            public short wShadingWeight;
            public short wShadingStyle;
            public ushort wNumberingStart;
            public ushort wNumberingStyle;
            public ushort wNumberingTab;
            public ushort wBorderSpace;
            public ushort wBorderWidth;
            public ushort wBorders;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref PARAFORMAT2 lParam);

        private void SetLineHeight(int px)
        {
            if (px <= 0) return;
            try
            {
                IntPtr h = code.Handle;
                using (Graphics g = Graphics.FromHwnd(h))
                {
                    float dpi = g.DpiY <= 0 ? 96f : g.DpiY;
                    PARAFORMAT2 pf = new PARAFORMAT2();
                    pf.cbSize = Marshal.SizeOf(typeof(PARAFORMAT2));
                    pf.dwMask = PFM_LINESPACING;
                    pf.bLineSpacingRule = 4;
                    pf.dyLineSpacing = (int)Math.Round(px * 1440.0 / dpi);
                    pf.rgxTabs = new int[32];
                    SendMessage(h, EM_SETPARAFORMAT, (IntPtr)SCF_ALL, ref pf);
                }
            }
            catch { }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && textFont != null)
            {
                textFont.Dispose();
                textFont = null;
            }
        }
    }


    /// <summary>表格覆盖层：用绘制的网格替代正文里的占位行，保证 CJK 混排也能对齐。</summary>
    public class TableBlockView : Panel
    {
        private Theme th;
        private List<string[]> rows;
        private Font bodyFont;
        private Font headerFont;
        private bool isUser;
        private int[] widths;
        public event MouseEventHandler MenuRequested;

        public TableBlockView(Theme theme, Font body, List<string[]> rows, bool isUser)
        {
            th = theme;
            this.rows = rows;
            this.isUser = isUser;
            bodyFont = body;
            headerFont = new Font(body, FontStyle.Bold);
            BackColor = isUser ? th.UserBubble : th.Bg;
            DoubleBuffered = true;
            TabStop = false;
            Cursor = Cursors.Default;
            MouseUp += ForwardMenu;
        }

        private void ForwardMenu(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right && MenuRequested != null) MenuRequested(this, e);
        }

        public void LayoutAt(int x, int y, int width, int height)
        {
            SetBounds(x, y, Math.Max(Ui.Px(120), width), Math.Max(Ui.Px(28), height));
            BuildWidths();
        }

        private void BuildWidths()
        {
            int cols = 1;
            if (rows != null) foreach (string[] row in rows) if (row.Length > cols) cols = row.Length;
            widths = new int[cols];

            int pad = Ui.Px(10);
            int minCell = Ui.Px(28);
            int total = 0;
            for (int c = 0; c < cols; c++)
            {
                int maxText = 0;
                for (int r = 0; r < rows.Count; r++)
                {
                    string cell = c < rows[r].Length ? rows[r][c] : "";
                    Font f = r == 0 ? headerFont : bodyFont;
                    Size sz = TextRenderer.MeasureText(cell, f, new Size(int.MaxValue, int.MaxValue),
                        TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                    if (sz.Width > maxText) maxText = sz.Width;
                }
                widths[c] = Math.Max(minCell, maxText + pad * 2);
                total += widths[c];
            }

            int available = Math.Max(cols * Ui.Px(20), Width - (cols + 1));
            if (total > 0 && total != available)
            {
                int used = 0;
                for (int c = 0; c < cols; c++)
                {
                    widths[c] = Math.Max(Ui.Px(20), (int)Math.Round(widths[c] * (double)available / total));
                    used += widths[c];
                }
                int widest = 0;
                for (int c = 1; c < cols; c++) if (widths[c] > widths[widest]) widest = c;
                while (used < available) { widths[widest]++; used++; }
                while (used > available)
                {
                    int idx = -1;
                    for (int c = 0; c < cols; c++) if (widths[c] > Ui.Px(20) && (idx < 0 || widths[c] > widths[idx])) idx = c;
                    if (idx < 0) break;
                    widths[idx]--;
                    used--;
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (rows == null || rows.Count == 0 || widths == null) return;
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            int rowH = Math.Max(Ui.Px(20), Height / rows.Count);
            int pad = Ui.Px(10);

            using (SolidBrush hb = new SolidBrush(th.TableBg))
                g.FillRectangle(hb, 0, 0, Width, rowH);

            using (Pen pen = new Pen(th.TableBorder, 1f))
            {
                g.DrawLine(pen, 0, 0, Width - 1, 0);
                for (int r = 1; r <= rows.Count; r++)
                {
                    int y = Math.Min(Height - 1, r * rowH);
                    g.DrawLine(pen, 0, y, Width - 1, y);
                }
                int x = 0;
                g.DrawLine(pen, 0, 0, 0, Height - 1);
                for (int c = 0; c < widths.Length; c++)
                {
                    x += widths[c] + 1;
                    if (x >= Width) x = Width - 1;
                    g.DrawLine(pen, x, 0, x, Height - 1);
                }
            }

            int cx = 1;
            for (int c = 0; c < widths.Length; c++)
            {
                for (int r = 0; r < rows.Count; r++)
                {
                    string cell = c < rows[r].Length ? rows[r][c] : "";
                    if (cell.Length == 0) continue;
                    Font f = r == 0 ? headerFont : bodyFont;
                    Color color = r == 0 ? th.Text : (isUser ? th.UserText : th.BotText);
                    Rectangle rect = new Rectangle(cx + pad, r * rowH + 1,
                        Math.Max(1, widths[c] - pad * 2), Math.Max(1, rowH - 2));
                    TextRenderer.DrawText(g, cell, f, rect, color,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }
                cx += widths[c] + 1;
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && headerFont != null)
            {
                headerFont.Dispose();
                headerFont = null;
            }
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
        public bool IsToolCard;
        public bool IsStreamingView;
        public event MouseEventHandler MenuRequested;
        public event EventHandler HeightChanged;

        private RichTextBox rtb;
        private System.Collections.Generic.List<CodeBlockInfo> codeBlocks = new System.Collections.Generic.List<CodeBlockInfo>();
        private System.Collections.Generic.List<TableBlockInfo> tableBlocks = new System.Collections.Generic.List<TableBlockInfo>();
        private System.Collections.Generic.List<Control> codeTools = new System.Collections.Generic.List<Control>();
        private Label toolHeader;
        private RichTextBox toolDetail;
        private string toolSummary = "";
        private string toolArgs = "";
        private string toolResultText = "";
        private string toolDiff = "";
        private long toolMs;
        private bool toolExpanded;
        private Theme th;
        private Font body;
        private Font mono;
        private string text = "";
        private bool needsReflow = true;
        private int lastReflowWidth = -1;
        private int lastRenderWidth = -1;

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
            rtb.MouseUp += ChildMenuRequested;
            MouseUp += ChildMenuRequested;
            Render();
        }

        private void ChildMenuRequested(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right && MenuRequested != null) MenuRequested(this, e);
        }

        public void ApplyTheme(Theme theme)
        {
            th = theme;
            BackColor = th.Bg;
            rtb.BackColor = IsUser ? th.UserBubble : th.Bg;
            if (IsToolCard)
            {
                UpdateToolHeader();
                RenderToolDetail();
                needsReflow = true;
                Reflow(Width);
                Invalidate();
                return;
            }
            Render();
        }

        public void SetText(string s)
        {
            SetText(s, false);
        }

        public void SetText(string s, bool force)
        {
            s = s == null ? "" : s;
            if (!force && s == text) return;
            text = s;
            needsReflow = true;
            Render();
        }

        /// <summary>
        /// 流式输出：只在标记块闭合时做完整 Markdown 渲染；块内部按增量追加纯文本。
        /// 这样不会对每个字符都重新解析整篇回复。
        /// </summary>
        public void SetStreamText(string s)
        {
            s = s == null ? "" : s;
            if (!IsStreamingView || s == text) return;
            if (!s.StartsWith(text, StringComparison.Ordinal))
            {
                SetText(s, true);
                return;
            }

            string delta = s.Substring(text.Length);
            if (delta.Length == 0) return;
            if (NeedsFullRender(delta))
            {
                SetText(s, true);
                return;
            }

            AppendStreamDelta(delta);
            text = s;
            needsReflow = true;
        }

        private bool NeedsFullRender(string delta)
        {
            if (delta.IndexOf("```", StringComparison.Ordinal) >= 0) return true;

            bool inCode = IsOpenCodeBlock(text);
            if (inCode) return false;

            if (delta.IndexOf("\n\n", StringComparison.Ordinal) >= 0) return true;
            if (delta.IndexOf("\n#", StringComparison.Ordinal) >= 0) return true;
            if (delta.IndexOf("\n>", StringComparison.Ordinal) >= 0) return true;
            if (delta.IndexOf("\n|", StringComparison.Ordinal) >= 0) return true;
            if (delta.IndexOf("\n- ", StringComparison.Ordinal) >= 0) return true;
            if (delta.IndexOf("\n* ", StringComparison.Ordinal) >= 0) return true;
            if (delta.IndexOf("\n+ ", StringComparison.Ordinal) >= 0) return true;

            if (delta.IndexOf('\n') >= 0)
            {
                int nl = text.LastIndexOf('\n');
                string currentLine = nl >= 0 ? text.Substring(nl + 1) : text;
                if (StartsBlockMarker(currentLine)) return true;
            }
            return false;
        }

        private bool IsOpenCodeBlock(string s)
        {
            int count = 0;
            int idx = 0;
            while (true)
            {
                idx = s.IndexOf("```", idx, StringComparison.Ordinal);
                if (idx < 0) break;
                count++;
                idx += 3;
            }
            return (count % 2) == 1;
        }

        private static bool StartsBlockMarker(string line)
        {
            string t = line.TrimStart();
            if (t.StartsWith("#") || t.StartsWith(">") || t.StartsWith("|") || t.StartsWith("```")) return true;
            if (t.StartsWith("- ") || t.StartsWith("* ") || t.StartsWith("+ ")) return true;
            int d = 0;
            while (d < t.Length && char.IsDigit(t[d])) d++;
            return d > 0 && d + 1 < t.Length && (t[d] == '.' || t[d] == ')') && t[d + 1] == ' ';
        }

        private void AppendStreamDelta(string delta)
        {
            bool inCode = IsOpenCodeBlock(text);
            Font f = inCode ? mono : body;
            Color color = inCode ? th.CodeText : (IsUser ? th.UserText : th.BotText);
            Color back = inCode ? th.CodeBg : rtb.BackColor;
            Markdown.AppendStyled(rtb, delta, f, color, back);
        }

        public void FinishStreaming()
        {
            IsStreamingView = false;
            LayoutCodeTools();
        }

        public string GetText() { return text; }

        public void SetError()
        {
            IsError = true;
            needsReflow = true;
            Render();
            Reflow(Width);
        }

        /// <summary>工具调用的提示行：用次要色，视觉上像系统提示。</summary>
        public void SetTool()
        {
            IsTool = true;
            needsReflow = true;
            Render();
            Reflow(Width);
        }

        /// <summary>把一条消息变成可展开的工具调用卡片。</summary>
        public void SetToolCard(string name, string summary, string args, string result, string diff, long elapsedMs)
        {
            IsTool = true;
            IsToolCard = true;
            toolSummary = summary == null ? "" : summary;
            toolArgs = args == null ? "" : args;
            toolResultText = result == null ? "" : result;
            toolDiff = diff == null ? "" : diff;
            toolMs = elapsedMs;
            text = toolResultText;

            rtb.Visible = false;
            ClearCodeTools();
            if (toolHeader == null)
            {
                toolHeader = new Label();
                toolHeader.AutoSize = false;
                toolHeader.TextAlign = ContentAlignment.MiddleLeft;
                toolHeader.Padding = new Padding(Ui.Px(10), 0, Ui.Px(8), 0);
                toolHeader.Font = new Font("Microsoft YaHei UI", 9.5f);
                toolHeader.Cursor = Cursors.Hand;
                toolHeader.Click += delegate
                {
                    toolExpanded = !toolExpanded;
                    needsReflow = true;
                    UpdateToolHeader();
                    if (HeightChanged != null) HeightChanged(this, EventArgs.Empty);
                };
                toolHeader.MouseUp += ChildMenuRequested;
                Controls.Add(toolHeader);

                toolDetail = new RichTextBox();
                toolDetail.BorderStyle = BorderStyle.None;
                toolDetail.ReadOnly = true;
                toolDetail.TabStop = false;
                toolDetail.WordWrap = true;
                toolDetail.ScrollBars = RichTextBoxScrollBars.None;
                toolDetail.Font = new Font("Consolas", 9f);
                toolDetail.MouseUp += ChildMenuRequested;
                Controls.Add(toolDetail);
                toolDetail.Visible = false;
            }

            UpdateToolHeader();
            RenderToolDetail();
            needsReflow = true;
            Reflow(Width);
        }

        private void UpdateToolHeader()
        {
            if (toolHeader == null) return;
            toolHeader.Text = (toolExpanded ? "▾  " : "▸  ") + toolSummary
                + (toolMs >= 0 ? "    总耗时 " + toolMs + " ms" : "");
            toolHeader.BackColor = th.InputBg;
            toolHeader.ForeColor = th.Text;
            if (toolDetail != null) toolDetail.Visible = toolExpanded;
        }

        private void RenderToolDetail()
        {
            if (toolDetail == null) return;
            toolDetail.BackColor = th.InputBg;
            toolDetail.ForeColor = th.SubText;
            toolDetail.Clear();
            if (!string.IsNullOrEmpty(toolArgs))
            {
                AppendDetail("参数\n", th.SubText, true);
                AppendDetail(toolArgs + "\n\n", th.Text, false);
            }
            AppendDetail("结果\n", th.SubText, true);
            string result = toolResultText;
            if (result.Length > 4000) result = result.Substring(0, 4000) + "\n…（已截断）";
            AppendDetail(result + "\n", th.Text, false);
            if (!string.IsNullOrEmpty(toolDiff))
            {
                AppendDetail("\n变化\n", th.SubText, true);
                string[] lines = toolDiff.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
                foreach (string line in lines)
                {
                    Color c = th.SubText;
                    if (line.StartsWith("+ ")) c = Color.FromArgb(70, 170, 110);
                    else if (line.StartsWith("- ")) c = th.Danger;
                    AppendDetail(line + "\n", c, false);
                }
            }
        }

        private void AppendDetail(string s, Color c, bool bold)
        {
            int start = toolDetail.TextLength;
            toolDetail.AppendText(s);
            int end = toolDetail.TextLength;
            if (end <= start) return;
            toolDetail.Select(start, end - start);
            toolDetail.SelectionColor = c;
            if (bold)
            {
                using (Font bf = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold))
                    toolDetail.SelectionFont = bf;
            }
            else toolDetail.SelectionFont = toolDetail.Font;
            toolDetail.SelectionStart = end;
            toolDetail.SelectionLength = 0;
        }

        private void Render()
        {
            if (IsToolCard) return;
            codeBlocks.Clear();
            tableBlocks.Clear();
            Color fg = IsError ? th.Danger : (IsUser ? th.UserText : (IsTool ? th.SubText : th.BotText));
            rtb.ForeColor = fg;
            if (IsError)
            {
                ClearCodeTools();
                rtb.SelectionFont = body;
                rtb.SelectionColor = fg;
                rtb.Text = text;
                return;
            }
            Markdown.Render(rtb, text, fg, th, body, mono, codeBlocks, tableBlocks);
            lastRenderWidth = rtb.Width;
            LayoutCodeTools();
        }

        private void ClearCodeTools()
        {
            for (int i = 0; i < codeTools.Count; i++)
            {
                Control c = codeTools[i];
                Controls.Remove(c);
                c.Dispose();
            }
            codeTools.Clear();
        }

        private void LayoutCodeTools()
        {
            ClearCodeTools();
            if (IsToolCard || (codeBlocks.Count == 0 && tableBlocks.Count == 0) || rtb.Width <= 0) return;

            foreach (CodeBlockInfo block in codeBlocks)
            {
                if (block.HeaderStart < 0) continue;
                if (IsStreamingView && !block.Closed) continue;
                int top;
                int blockH;
                int lineH = 0;
                try
                {
                    int headerY = rtb.GetPositionFromCharIndex(block.HeaderStart).Y;
                    if (block.Start < 0 || block.Start >= rtb.TextLength) continue;
                    int startY = rtb.GetPositionFromCharIndex(block.Start).Y;
                    lineH = Math.Max(Ui.Px(8), (startY - headerY) / 2);
                    string all = rtb.Text;
                    int last = Math.Min(block.End, all.Length) - 1;
                    while (last > block.Start && (all[last] == '\n' || all[last] == '\r')) last--;
                    top = rtb.Top + headerY;
                    if (last < block.Start)
                    {
                        blockH = startY - headerY;
                    }
                    else
                    {
                        int lastY = rtb.GetPositionFromCharIndex(last).Y;
                        int trailing = 0;
                        string codeBody = block.Code == null ? "" : block.Code.Replace("\r\n", "\n").Replace("\r", "\n");
                        for (int ci = codeBody.Length - 1; ci >= 0 && codeBody[ci] == '\n'; ci--) trailing++;
                        blockH = lastY + lineH * (1 + trailing) - headerY;
                    }
                }
                catch { continue; }

                if (top >= rtb.Bottom - Ui.Px(4) || blockH <= 0) continue;
                blockH = Math.Min(blockH, rtb.Bottom - top);

                Font codeFont = new Font(mono.FontFamily, mono.Size, mono.Style, GraphicsUnit.Point);

                CodeBlockView view = new CodeBlockView(th, codeFont, rtb.Font, block.Language, block.Code);
                view.MenuRequested += ChildMenuRequested;
                Controls.Add(view);
                view.LayoutAt(rtb.Left, top, rtb.Width, blockH, lineH);
                view.BringToFront();
                codeTools.Add(view);
            }

            foreach (TableBlockInfo table in tableBlocks)
            {
                if (table.Rows == null || table.Rows.Count == 0 || table.LineStarts == null || table.LineStarts.Count == 0) continue;
                int top;
                int lineH;
                try
                {
                    top = rtb.Top + rtb.GetPositionFromCharIndex(table.LineStarts[0]).Y;
                    if (table.LineStarts.Count > 1)
                    {
                        int second = rtb.Top + rtb.GetPositionFromCharIndex(table.LineStarts[1]).Y;
                        lineH = Math.Max(Ui.Px(12), second - top);
                    }
                    else
                    {
                        lineH = Math.Max(Ui.Px(26), body.Height + Ui.Px(12));
                    }
                }
                catch { continue; }

                int tableH = lineH * table.Rows.Count;
                if (top >= rtb.Bottom - Ui.Px(4) || tableH <= 0) continue;
                TableBlockView view = new TableBlockView(th, body, table.Rows, IsUser);
                view.MenuRequested += ChildMenuRequested;
                Controls.Add(view);
                view.LayoutAt(rtb.Left, top, rtb.Width, tableH);
                view.BringToFront();
                codeTools.Add(view);
            }
        }

        /// <summary>按可用宽度重排，返回占用的高度。</summary>
        public int Reflow(int maxWidth)
        {
            if (!needsReflow && lastReflowWidth == maxWidth) return Height;

            if (IsToolCard)
            {
                int pad = Ui.Px(8);
                int toolLimit = Math.Max(Ui.Px(240), maxWidth);
                int headerH = Ui.Px(38);
                int toolInnerW = Math.Max(Ui.Px(120), toolLimit - pad * 2);
                int detailH = 0;

                if (toolHeader != null) toolHeader.SetBounds(pad, pad, toolInnerW, headerH);
                if (toolExpanded && toolDetail != null)
                {
                    toolDetail.Visible = true;
                    toolDetail.SetBounds(pad, pad + headerH + Ui.Px(4), toolInnerW, Ui.Px(20));
                    detailH = MeasureToolDetail(toolInnerW);
                    int maxH = Ui.Px(340);
                    toolDetail.ScrollBars = detailH > maxH ? RichTextBoxScrollBars.Vertical : RichTextBoxScrollBars.None;
                    if (detailH > maxH) detailH = maxH;
                    toolDetail.Height = Math.Max(Ui.Px(16), detailH);
                }
                else if (toolDetail != null)
                {
                    toolDetail.Visible = false;
                }

                Width = toolLimit;
                Height = pad * 2 + headerH + (toolExpanded ? Ui.Px(4) + Math.Max(Ui.Px(16), detailH) : 0);
                lastReflowWidth = maxWidth;
                needsReflow = false;
                return Height;
            }

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

            int innerW = Math.Max(Ui.Px(80), inner);
            if (rtb.Left != padL || rtb.Top != padT || rtb.Width != innerW)
                rtb.SetBounds(padL, padT, innerW, rtb.Height);

            // RichTextBox 在改变高度时会丢掉部分字符格式；预留足够高度，
            // 让内容增长只改变外层 MessageView 的高度，不再反复缩放 rtb。
            int minH = Ui.Px(1600);
            if (rtb.Height < minH) rtb.Height = minH;

            if (lastRenderWidth != innerW) Render();

            int h = MeasureHeight(innerW);
            if (h > rtb.Height - Ui.Px(4))
            {
                rtb.Height = h + Ui.Px(400);
                Render();
            }

            int w = IsUser ? Math.Max(Ui.Px(80), inner) + padL + padR : limit;
            if (Width != w) Width = w;
            int total = h + padT + padB;
            if (Height != total) Height = total;

            lastReflowWidth = maxWidth;
            needsReflow = false;
            LayoutCodeTools();
            return Height;
        }

        private int MeasureToolDetail(int width)
        {
            if (toolDetail == null || toolDetail.TextLength == 0) return 0;
            try
            {
                int idx = toolDetail.TextLength - 1;
                Point p = toolDetail.GetPositionFromCharIndex(idx);
                return Math.Max(toolDetail.Font.Height + Ui.Px(10), p.Y + toolDetail.Font.Height + Ui.Px(10));
            }
            catch { return toolDetail.Font.Height * 4; }
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
            else if (IsToolCard)
            {
                Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
                using (GraphicsPath path = Round.Path(r, Ui.Px(10)))
                {
                    using (SolidBrush b = new SolidBrush(th.InputBg)) e.Graphics.FillPath(b, path);
                    using (Pen pen = new Pen(th.Border, 1f)) e.Graphics.DrawPath(pen, path);
                }
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
