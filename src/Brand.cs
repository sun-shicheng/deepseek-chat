using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace DsChat
{
    /// <summary>Lumi 品牌图形：浅黄色圆角方块 + 四角星光。图标与界面左上角共用同一套绘制。</summary>
    public static class Brand
    {
        public static readonly Color TileTop = Color.FromArgb(255, 248, 224);
        public static readonly Color TileBottom = Color.FromArgb(246, 209, 120);
        public static readonly Color TileBorder = Color.FromArgb(226, 190, 105);
        public static readonly Color Star = Color.FromArgb(122, 88, 24);

        public static void Draw(Graphics g, RectangleF r)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                float radius = r.Width * 0.24f;
                using (GraphicsPath tile = Round(r, radius))
                {
                    using (LinearGradientBrush b = new LinearGradientBrush(r, TileTop, TileBottom, 45f))
                        g.FillPath(b, tile);
                    using (Pen pen = new Pen(TileBorder, Math.Max(1f, r.Width * 0.018f)))
                        g.DrawPath(pen, tile);
                }

                float cx = r.X + r.Width / 2f;
                float cy = r.Y + r.Height / 2f + r.Height * 0.01f;
                float outer = r.Width * 0.31f;
                float inner = outer * 0.30f;
                using (GraphicsPath star = Sparkle(cx, cy, outer, inner))
                using (SolidBrush b = new SolidBrush(Star))
                    g.FillPath(b, star);

                // 右上角的小光点，让图标在浅色背景上更有层次
                float dx = r.X + r.Width * 0.73f;
                float dy = r.Y + r.Height * 0.25f;
                float dr = Math.Max(0.7f, r.Width * 0.038f);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(210, 255, 255, 255)))
                    g.FillEllipse(b, dx - dr, dy - dr, dr * 2f, dr * 2f);
            }
            finally
            {
                g.SmoothingMode = old;
            }
        }

        private static GraphicsPath Sparkle(float cx, float cy, float outer, float inner)
        {
            GraphicsPath p = new GraphicsPath();
            p.AddPolygon(new PointF[]
            {
                new PointF(cx, cy - outer),
                new PointF(cx + inner, cy - inner),
                new PointF(cx + outer, cy),
                new PointF(cx + inner, cy + inner),
                new PointF(cx, cy + outer),
                new PointF(cx - inner, cy + inner),
                new PointF(cx - outer, cy),
                new PointF(cx - inner, cy - inner)
            });
            p.CloseFigure();
            return p;
        }

        private static GraphicsPath Round(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = radius * 2f;
            if (d <= 0 || d > Math.Min(r.Width, r.Height)) d = Math.Min(r.Width, r.Height);
            if (d <= 1f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
