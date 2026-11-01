using System;
using System.Drawing;
using System.Windows.Forms;

namespace DsChat
{
    /// <summary>DPI 缩放：界面按 96 DPI 的逻辑尺寸编写，运行时整体放大。</summary>
    public static class Ui
    {
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
}
