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

internal static class ShrinkTest
{
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();

    [STAThread]
    private static void Main()
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
        f.MinimumSize = Size.Empty;
        f.Size = new Size(1600, 900);
        f.Show();
        f.Activate();
        Pump(500);

        Panel host = (Panel)Field(f, "msgHost");
        Panel wrap = (Panel)Field(f, "composerWrap");
        TextBox input = (TextBox)Field(f, "input");
        System.Collections.IList views = (System.Collections.IList)Field(f, "views");
        FieldInfo ab = typeof(ChatForm).GetField("atBottom", BindingFlags.NonPublic | BindingFlags.Instance);

        MethodInfo add = typeof(ChatForm).GetMethod("AddView", BindingFlags.NonPublic | BindingFlags.Instance);
        MethodInfo layout = typeof(ChatForm).GetMethod("LayoutMessages", BindingFlags.NonPublic | BindingFlags.Instance);
        StringBuilder sb = new StringBuilder();
        for (int i = 1; i <= 12; i++) sb.AppendLine("第 " + i + " 行：这是用来验证输入框缩小时聊天记录是否被遮挡的内容。");
        for (int i = 0; i < 8; i++)
            add.Invoke(f, new object[] { new ChatMsg(i % 2 == 0 ? "user" : "assistant", sb.ToString()), i % 2 == 0 });
        ab.SetValue(f, true);
        layout.Invoke(f, new object[] { true });
        Pump(700);
        Dump("initial", f, host, wrap, views);

        input.Text = string.Join("\r\n", new string[12]);
        Pump(700);
        Dump("grown", f, host, wrap, views);

        input.Clear();
        Pump(900);
        Dump("shrunk", f, host, wrap, views);
        Shot(f, host, "shrink_after.png");
        ShotScreen(f, "form_screen_after.png");
        ShotForm(f, "form_after_shrink.png");

        Rectangle hr = host.RectangleToScreen(host.ClientRectangle);
        Rectangle wr = wrap.RectangleToScreen(wrap.ClientRectangle);
        if (hr.Bottom != wr.Top) throw new Exception("host and composer overlap/gap: " + hr.Bottom + " vs " + wr.Top);

        Control last = (Control)views[views.Count - 1];
        int lastBottom = last.PointToScreen(Point.Empty).Y + last.Height - hr.Top;
        Console.WriteLine("last bottom=" + lastBottom + " host height=" + hr.Height);
        // 最后一条消息下方本来就有 18px 间距 + 8px 底部留白（约 58px @225%）
        if (lastBottom < hr.Height - 80) throw new Exception("last message not at bottom after shrink: " + lastBottom + " < " + hr.Height);

        Console.WriteLine("SHRINK OK");
        f.Hide();
        Environment.Exit(0);
    }

    private static void Dump(string tag, ChatForm f, Panel host, Panel wrap, System.Collections.IList views)
    {
        Rectangle hr = host.RectangleToScreen(host.ClientRectangle);
        Rectangle wr = wrap.RectangleToScreen(wrap.ClientRectangle);
        Console.WriteLine(tag + ": host=" + hr.Top + ".." + hr.Bottom + " h=" + hr.Height + " wrapTop=" + wr.Top + " scroll=" + (-host.AutoScrollPosition.Y) + " max=" + (host.AutoScrollMinSize.Height - host.ClientSize.Height));
        if (views.Count > 0)
        {
            Control first = (Control)views[0];
            Control last = (Control)views[views.Count - 1];
            Console.WriteLine("  firstY=" + (first.PointToScreen(Point.Empty).Y - hr.Top) + " lastBottom=" + (last.PointToScreen(Point.Empty).Y + last.Height - hr.Top));
        }
    }

    private static void Shot(Form f, Control c, string name)
    {
        Rectangle r = c.RectangleToScreen(c.ClientRectangle);
        Bitmap bmp = new Bitmap(r.Width, r.Height);
        using (Graphics g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
        string path = Path.Combine(Path.GetTempPath(), name);
        bmp.Save(path, ImageFormat.Png);
        Console.WriteLine("shot " + path);
    }

    private static void ShotScreen(Form f, string name)
    {
        Rectangle r = Rectangle.Intersect(f.Bounds, Screen.PrimaryScreen.Bounds);
        Bitmap bmp = new Bitmap(r.Width, r.Height);
        using (Graphics g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
        string path = Path.Combine(Path.GetTempPath(), name);
        bmp.Save(path, ImageFormat.Png);
        Console.WriteLine("screen shot " + path);
    }

    private static void ShotForm(Form f, string name)
    {
        Bitmap bmp = new Bitmap(f.Width, f.Height);
        f.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
        string path = Path.Combine(Path.GetTempPath(), name);
        bmp.Save(path, ImageFormat.Png);
        Console.WriteLine("form shot " + path);
    }

    private static object Field(object o, string name)
    {
        return o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(o);
    }

    private static void Pump(int ms)
    {
        DateTime end = DateTime.Now.AddMilliseconds(ms);
        while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(10); }
    }
}
