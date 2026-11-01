using System.Drawing;

namespace DsChat
{
    /// <summary>配色方案：深色 / 浅色两套，风格参考 DeepSeek 网页端。</summary>
    public class Theme
    {
        public bool IsDark;
        public Color Bg;            // 主区域背景
        public Color Sidebar;       // 侧边栏
        public Color SidebarHover;
        public Color SidebarActive;
        public Color Text;
        public Color SubText;
        public Color Border;
        public Color InputBg;
        public Color Accent;
        public Color AccentHover;
        public Color UserBubble;
        public Color UserText;
        public Color BotText;
        public Color CodeBg;
        public Color CodeText;
        public Color Danger;

        public static Theme Create(bool dark)
        {
            Theme t = new Theme();
            t.IsDark = dark;
            if (dark)
            {
                t.Bg = Color.FromArgb(26, 26, 30);
                t.Sidebar = Color.FromArgb(20, 20, 24);
                t.SidebarHover = Color.FromArgb(33, 33, 39);
                t.SidebarActive = Color.FromArgb(40, 41, 50);
                t.Text = Color.FromArgb(236, 236, 241);
                t.SubText = Color.FromArgb(146, 148, 160);
                t.Border = Color.FromArgb(52, 53, 62);
                t.InputBg = Color.FromArgb(34, 34, 40);
                t.Accent = Color.FromArgb(77, 107, 254);
                t.AccentHover = Color.FromArgb(101, 128, 255);
                t.UserBubble = Color.FromArgb(43, 45, 58);
                t.UserText = Color.FromArgb(236, 236, 241);
                t.BotText = Color.FromArgb(226, 227, 234);
                t.CodeBg = Color.FromArgb(32, 33, 40);
                t.CodeText = Color.FromArgb(214, 222, 235);
                t.Danger = Color.FromArgb(240, 110, 110);
            }
            else
            {
                t.Bg = Color.FromArgb(255, 255, 255);
                t.Sidebar = Color.FromArgb(248, 249, 251);
                t.SidebarHover = Color.FromArgb(238, 240, 245);
                t.SidebarActive = Color.FromArgb(232, 236, 247);
                t.Text = Color.FromArgb(28, 29, 35);
                t.SubText = Color.FromArgb(110, 114, 128);
                t.Border = Color.FromArgb(226, 229, 235);
                t.InputBg = Color.FromArgb(247, 248, 250);
                t.Accent = Color.FromArgb(77, 107, 254);
                t.AccentHover = Color.FromArgb(59, 88, 240);
                t.UserBubble = Color.FromArgb(238, 242, 255);
                t.UserText = Color.FromArgb(24, 26, 35);
                t.BotText = Color.FromArgb(32, 34, 42);
                t.CodeBg = Color.FromArgb(244, 245, 248);
                t.CodeText = Color.FromArgb(38, 42, 54);
                t.Danger = Color.FromArgb(200, 60, 60);
            }
            return t;
        }
    }
}
