using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using DsChat;

internal static class ScrollBugTest
{
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, int dx, int dy, int data, UIntPtr extra);
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X; public int Y; }
    private const uint MOUSEEVENTF_WHEEL = 0x0800;

    [STAThread]
    private static void Main()
    {
        POINT old; GetCursorPos(out old);
        try
        {
            try { SetProcessDPIAware(); } catch { }
            Ui.Detect();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Config cfg = new Config();
            cfg.BaseUrl = "http://127.0.0.1:1";
            cfg.ApiKey = "x";

            ChatForm f = new ChatForm(cfg);
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(0, 0);
            f.Show();
            f.Activate();
            Pump(400);

            FieldInfo abField = typeof(ChatForm).GetField("atBottom", BindingFlags.NonPublic | BindingFlags.Instance);

            MethodInfo add = typeof(ChatForm).GetMethod("AddView", BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo layout = typeof(ChatForm).GetMethod("LayoutMessages", BindingFlags.NonPublic | BindingFlags.Instance);

            StringBuilder sb = new StringBuilder();
            for (int i = 1; i <= 60; i++) sb.AppendLine("第 " + i + " 行：这是模型回复的内容，用来模拟一个比较长的回答。");
            add.Invoke(f, new object[] { new ChatMsg("user", "帮我写一段长回答"), true });
            add.Invoke(f, new object[] { new ChatMsg("assistant", sb.ToString()), false });
            abField.SetValue(f, true);
            layout.Invoke(f, new object[] { true });
            Pump(1200);

            FieldInfo hostField = typeof(ChatForm).GetField("msgHost", BindingFlags.NonPublic | BindingFlags.Instance);
            Panel host = (Panel)hostField.GetValue(f);
            FieldInfo viewsField = typeof(ChatForm).GetField("views", BindingFlags.NonPublic | BindingFlags.Instance);
            System.Collections.IList views = (System.Collections.IList)viewsField.GetValue(f);

            Dump("after layout", host, views);
            Console.WriteLine("atBottom=" + abField.GetValue(f));
            Shot(host, "scroll_after_layout.png");

            Point p = host.PointToScreen(new Point(host.ClientSize.Width / 2, host.ClientSize.Height / 2));
            SetForegroundWindow(f.Handle);
            SetCursorPos(p.X, p.Y);
            Pump(150);
            mouse_event(MOUSEEVENTF_WHEEL, 0, 0, 360, UIntPtr.Zero);
            Pump(700);
            Dump("after wheel up", host, views);
            Shot(host, "scroll_after_wheel.png");

            for (int i = 0; i < 25; i++) { mouse_event(MOUSEEVENTF_WHEEL, 0, 0, 360, UIntPtr.Zero); Pump(40); }
            Pump(400);
            Dump("after scroll to top", host, views);
            Console.WriteLine("atBottom=" + abField.GetValue(f));
            if (-host.AutoScrollPosition.Y != 0) throw new Exception("did not reach top");
            Control firstView = (Control)views[0];
            int firstY = firstView.PointToScreen(Point.Empty).Y - host.PointToScreen(Point.Empty).Y;
            Console.WriteLine("top firstViewY=" + firstY);
            if (firstY < 0 || firstY > 60) throw new Exception("top gap wrong: " + firstY);

            for (int i = 0; i < 30; i++) { mouse_event(MOUSEEVENTF_WHEEL, 0, 0, -360, UIntPtr.Zero); Pump(40); }
            Pump(400);
            Dump("after scroll to bottom", host, views);
            Console.WriteLine("atBottom=" + abField.GetValue(f));
            if (-host.AutoScrollPosition.Y != host.AutoScrollMinSize.Height - host.ClientSize.Height) throw new Exception("did not reach bottom");

            layout.Invoke(f, new object[] { true });
            Pump(500);
            Dump("after re-layout", host, views);
            Shot(host, "scroll_after_relayout.png");
        }
        catch (Exception ex) { Console.WriteLine("FAIL " + ex); }
        finally { SetCursorPos(old.X, old.Y); }
        Environment.Exit(0);
    }

    private static void Dump(string tag, Panel host, System.Collections.IList views)
    {
        int firstTop = -1;
        for (int i = 0; i < views.Count; i++)
        {
            Control c = (Control)views[i];
            int y = c.PointToScreen(Point.Empty).Y - host.PointToScreen(Point.Empty).Y;
            if (y + c.Height > 0 && y < host.ClientSize.Height) { firstTop = y; break; }
        }
        Console.WriteLine(tag + " scroll=" + (-host.AutoScrollPosition.Y) + " max=" + (host.AutoScrollMinSize.Height - host.ClientSize.Height) + " vmax=" + host.VerticalScroll.Maximum + " vval=" + host.VerticalScroll.Value + " vis=" + host.VerticalScroll.Visible + " firstVisibleTop=" + firstTop + " views=" + views.Count);
        for (int i = 0; i < Math.Min(3, views.Count); i++)
        {
            Control c = (Control)views[i];
            int y = c.PointToScreen(Point.Empty).Y - host.PointToScreen(Point.Empty).Y;
            Console.WriteLine("  v" + i + " loc=" + c.Location.Y + " screenY=" + y + " h=" + c.Height);
        }
        if (views.Count > 0)
        {
            Control c = (Control)views[views.Count - 1];
            int y = c.PointToScreen(Point.Empty).Y - host.PointToScreen(Point.Empty).Y;
            Console.WriteLine("  last loc=" + c.Location.Y + " screenY=" + y + " h=" + c.Height);
        }
    }

    private static void Shot(Control c, string name)
    {
        Bitmap bmp = new Bitmap(c.ClientSize.Width, c.ClientSize.Height);
        c.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
        string path = Path.Combine(Path.GetTempPath(), name);
        bmp.Save(path, ImageFormat.Png);
        Console.WriteLine("shot " + path);
    }

    private static void Pump(int ms) { DateTime end = DateTime.Now.AddMilliseconds(ms); while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(20); } }
}
