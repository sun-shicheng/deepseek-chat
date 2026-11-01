using System.Drawing;
using Microsoft.Win32;

namespace DsChat
{
    /// <summary>配色方案：浅色 / 深色两套；主背景保持中性色，强调色统一使用浅黄。</summary>
    public class Theme
    {
        public const int ModeSystem = 0;
        public const int ModeLight = 1;
        public const int ModeDark = 2;

        public bool IsDark;
        public Color Bg;            // 主画布
        public Color Sidebar;       // 侧边栏，比主画布深一档
        public Color SidebarHover;
        public Color SidebarActive;
        public Color Text;
        public Color SubText;
        public Color Border;
        public Color InputBg;       // 次级表面：设置输入框 / 工具卡片 / 列表底色
        public Color ComposerBg;    // 用户输入框填充
        public Color ComposerBorder;
        public Color FocusBorder;
        public Color Accent;
        public Color AccentHover;
        public Color AccentText;
        public Color UserBubble;
        public Color UserText;
        public Color BotText;
        public Color CodeBg;
        public Color CodeText;
        public Color CodeBorder;
        public Color CodeKeyword;
        public Color CodeString;
        public Color CodeNumber;
        public Color CodeComment;
        public Color TableBg;
        public Color TableBorder;
        public Color QuoteText;
        public Color QuoteBar;
        public Color Danger;

        public static Theme Create(bool dark)
        {
            Theme t = new Theme();
            t.IsDark = dark;
            if (dark)
            {
                t.Bg = Color.FromArgb(21, 25, 34);          // #151922
                t.Sidebar = Color.FromArgb(18, 19, 22);     // #121316
                t.SidebarHover = Color.FromArgb(28, 30, 36);
                t.SidebarActive = Color.FromArgb(58, 51, 32);
                t.Text = Color.FromArgb(227, 226, 230);     // #E3E2E6
                t.SubText = Color.FromArgb(142, 144, 153);  // #8E9099
                t.Border = Color.FromArgb(42, 45, 53);
                t.InputBg = Color.FromArgb(27, 27, 31);     // #1B1B1F
                t.ComposerBg = Color.FromArgb(31, 31, 35);  // #1F1F23
                t.ComposerBorder = Color.FromArgb(58, 58, 64);
                t.FocusBorder = Color.FromArgb(245, 215, 110); // #F5D76E
                t.Accent = Color.FromArgb(245, 215, 110);
                t.AccentHover = Color.FromArgb(248, 223, 140);
                t.AccentText = Color.FromArgb(43, 36, 18);
                t.UserBubble = Color.FromArgb(58, 51, 32);  // #3A3320
                t.UserText = Color.FromArgb(227, 226, 230);
                t.BotText = Color.FromArgb(227, 226, 230);
                t.CodeBg = Color.FromArgb(27, 31, 39);      // #1B1F27
                t.CodeText = Color.FromArgb(218, 219, 224);
                t.CodeBorder = Color.FromArgb(44, 48, 56);
                t.CodeKeyword = Color.FromArgb(238, 194, 105);
                t.CodeString = Color.FromArgb(226, 160, 122);
                t.CodeNumber = Color.FromArgb(205, 178, 116);
                t.CodeComment = Color.FromArgb(150, 164, 122);
                t.TableBg = Color.FromArgb(27, 27, 31);
                t.TableBorder = Color.FromArgb(44, 48, 56);
                t.QuoteText = Color.FromArgb(185, 187, 194);
                t.QuoteBar = Color.FromArgb(245, 215, 110);
                t.Danger = Color.FromArgb(240, 122, 114);
            }
            else
            {
                t.Bg = Color.FromArgb(255, 255, 255);       // #FFFFFF
                t.Sidebar = Color.FromArgb(247, 248, 250);  // #F7F8FA
                t.SidebarHover = Color.FromArgb(238, 240, 244);
                t.SidebarActive = Color.FromArgb(255, 243, 204);
                t.Text = Color.FromArgb(21, 23, 27);        // #15171B
                t.SubText = Color.FromArgb(119, 123, 132);  // #777B84
                t.Border = Color.FromArgb(228, 230, 235);
                t.InputBg = Color.FromArgb(247, 248, 250);  // #F7F8FA
                t.ComposerBg = Color.FromArgb(255, 248, 225); // #FFF8E1
                t.ComposerBorder = Color.FromArgb(240, 226, 182); // #F0E2B6
                t.FocusBorder = Color.FromArgb(242, 201, 76); // #F2C94C
                t.Accent = Color.FromArgb(242, 201, 76);
                t.AccentHover = Color.FromArgb(247, 214, 108);
                t.AccentText = Color.FromArgb(43, 36, 18);
                t.UserBubble = Color.FromArgb(255, 248, 225); // #FFF8E1
                t.UserText = Color.FromArgb(21, 23, 27);
                t.BotText = Color.FromArgb(21, 23, 27);
                t.CodeBg = Color.FromArgb(245, 246, 248);   // #F5F6F8
                t.CodeText = Color.FromArgb(36, 38, 43);
                t.CodeBorder = Color.FromArgb(229, 231, 235);
                t.CodeKeyword = Color.FromArgb(154, 107, 10);
                t.CodeString = Color.FromArgb(176, 92, 54);
                t.CodeNumber = Color.FromArgb(124, 106, 42);
                t.CodeComment = Color.FromArgb(110, 122, 90);
                t.TableBg = Color.FromArgb(247, 248, 250);
                t.TableBorder = Color.FromArgb(229, 231, 235);
                t.QuoteText = Color.FromArgb(95, 99, 107);
                t.QuoteBar = Color.FromArgb(242, 201, 76);
                t.Danger = Color.FromArgb(211, 74, 70);
            }
            return t;
        }

        /// <summary>读取 Windows 应用主题：AppsUseLightTheme = 0 表示深色。</summary>
        public static bool IsSystemDark()
        {
            try
            {
                object v = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme", 1);
                if (v is int) return (int)v == 0;
            }
            catch { }
            return false;
        }

        public static bool ResolveDark(int mode)
        {
            if (mode == ModeLight) return false;
            if (mode == ModeDark) return true;
            return IsSystemDark();
        }
    }
}
