using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DsChat
{
    /// <summary>DPI 缩放：界面按 96 DPI 的逻辑尺寸编写，运行时整体放大。</summary>
    public static class Ui
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public static void ApplyTitleBarTheme(Form form, bool dark)
        {
            if (form == null || !form.IsHandleCreated) return;
            int value = dark ? 1 : 0;
            try { DwmSetWindowAttribute(form.Handle, 20, ref value, 4); } catch { }
            try { DwmSetWindowAttribute(form.Handle, 19, ref value, 4); } catch { }
        }
        /// <summary>当前屏幕缩放比例（1.0 = 100%，2.0 = 200%）。</summary>
        public static float S = 1f;

        public static void Detect()
        {
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    float dpi = g.DpiX;
                    if (dpi > 0f) S = dpi / 96f;
                }
            }
            catch { S = 1f; }
            if (S < 0.75f) S = 0.75f;
            if (S > 4f) S = 4f;
        }

        public static int Px(int v)
        {
            return (int)Math.Round(v * S);
        }
    }

    /// <summary>双缓冲面板，减少聊天区滚动与输入框变高时的闪烁。</summary>
    public class BufferedPanel : Panel
    {
        public BufferedPanel()
        {
            DoubleBuffered = true;
        }
    }

    /// <summary>
    /// 自绘圆角按钮。继承 Control 而不是 Button，避免系统按钮的方形边框/底色透过圆角露出来。
    /// 四角直接用父容器背景重绘，不需要 Region，也不会有方形残影。
    /// </summary>
    public class RoundedButton : Control, IButtonControl
    {
        public int Radius = 10;
        public Color BorderColor = Color.Transparent;

        private bool hover;
        private bool pressed;
        private DialogResult dialogResult;

        public RoundedButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            Font = new Font("Microsoft YaHei UI", 9.75f);
            AccessibleRole = AccessibleRole.PushButton;
        }

        public DialogResult DialogResult
        {
            get { return dialogResult; }
            set { dialogResult = value; }
        }

        public void NotifyDefault(bool value) { }

        public void PerformClick()
        {
            if (Enabled) OnClick(EventArgs.Empty);
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (dialogResult != System.Windows.Forms.DialogResult.None)
            {
                Form f = FindForm();
                if (f != null) f.DialogResult = dialogResult;
            }
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { pressed = true; Focus(); Invalidate(); }
            base.OnMouseDown(e);
        }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                PerformClick();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Color bg = Parent == null ? SystemColors.Control : Parent.BackColor;
            e.Graphics.Clear(bg);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color parentBg = Parent == null ? SystemColors.Control : Parent.BackColor;
            Color fill = BackColor;
            if (!Enabled) fill = Mix(fill, parentBg);
            else if (pressed) fill = Darken(fill);
            else if (hover) fill = Lighten(fill);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Round.Path(r, Ui.Px(Radius)))
            {
                using (SolidBrush b = new SolidBrush(fill)) e.Graphics.FillPath(b, p);
                if (BorderColor.A > 0 && BorderColor != Color.Transparent)
                {
                    using (Pen pen = new Pen(BorderColor, 1f)) e.Graphics.DrawPath(pen, p);
                }
            }

            Color textColor = Enabled ? ForeColor : Color.FromArgb(150, ForeColor);
            TextRenderer.DrawText(e.Graphics, Text, Font, r, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            if (Focused && ShowFocusCues)
            {
                Rectangle fr = new Rectangle(Ui.Px(4), Ui.Px(4), Math.Max(1, Width - Ui.Px(9)), Math.Max(1, Height - Ui.Px(9)));
                ControlPaint.DrawFocusRectangle(e.Graphics, fr, ForeColor, fill);
            }
        }

        private static Color Lighten(Color c)
        {
            return Color.FromArgb(c.A, Math.Min(255, c.R + 16), Math.Min(255, c.G + 16), Math.Min(255, c.B + 16));
        }

        private static Color Darken(Color c)
        {
            return Color.FromArgb(c.A, Math.Max(0, c.R - 14), Math.Max(0, c.G - 14), Math.Max(0, c.B - 14));
        }

        private static Color Mix(Color a, Color b)
        {
            return Color.FromArgb((a.R + b.R) / 2, (a.G + b.G) / 2, (a.B + b.B) / 2);
        }
    }
}
