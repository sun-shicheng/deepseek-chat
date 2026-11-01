using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using DsChat;

internal static class ComposerTest
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
        f.Show();
        Pump(500);

        Panel box = (Panel)Field(f, "composerBox");
        Panel wrap = (Panel)Field(f, "composerWrap");
        Panel host = (Panel)Field(f, "msgHost");
        TextBox input = (TextBox)Field(f, "input");
        Label hint = (Label)Field(f, "lblHint");
        Control send = (Control)Field(f, "btnSend");

        int h0 = box.Height, i0 = input.Height, m0 = host.Height, w0 = wrap.Height;
        Console.WriteLine("one line: box=" + h0 + " input=" + i0 + " host=" + m0 + " wrap=" + w0);
        if (h0 != Ui.Px(64)) throw new Exception("min height wrong");

        input.Text = "第一行\r\n第二行\r\n第三行";
        Pump(300);
        int h3 = box.Height, i3 = input.Height, m3 = host.Height;
        Console.WriteLine("three lines: box=" + h3 + " input=" + i3 + " host=" + m3);
        if (h3 <= h0) throw new Exception("composer did not grow");
        if (i3 <= i0) throw new Exception("input did not grow");
        if (m3 >= m0) throw new Exception("message area did not shrink");
        if (hint.Top < box.Bottom) throw new Exception("hint overlaps composer");
        if (send.Bottom > box.Height) throw new Exception("send button outside composer");
        if (wrap.Height != h3 + Ui.Px(40)) throw new Exception("wrap height mismatch");

        using (Bitmap bmp = new Bitmap(wrap.Width, wrap.Height))
        {
            wrap.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "composer_3lines.png");
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine("shot " + path);
        }

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < 30; i++) sb.AppendLine("第 " + i + " 行内容");
        input.Text = sb.ToString();
        Pump(300);
        Console.WriteLine("30 lines: box=" + box.Height + " input=" + input.Height + " cap=" + Ui.Px(180));
        if (box.Height != Ui.Px(180)) throw new Exception("max height not capped");

        input.Text = "这一行很长很长很长很长很长很长很长很长很长很长很长很长很长很长很长很长很长很长很长很长很长很长很长很长很长很长很长";
        Pump(300);
        Console.WriteLine("long wrapped line: box=" + box.Height + " input=" + input.Height);
        if (box.Height <= h0) throw new Exception("wrapped line did not grow");

        input.Clear();
        Pump(300);
        Console.WriteLine("cleared: box=" + box.Height + " input=" + input.Height + " host=" + host.Height);
        if (box.Height != h0 || input.Height != i0 || host.Height != m0) throw new Exception("did not shrink back");

        // 重新检查消息区滚动/布局没有因发送框变高而错乱
        MethodInfo add = typeof(ChatForm).GetMethod("AddView", BindingFlags.NonPublic | BindingFlags.Instance);
        MethodInfo layout = typeof(ChatForm).GetMethod("LayoutMessages", BindingFlags.NonPublic | BindingFlags.Instance);
        for (int i = 0; i < 20; i++)
            add.Invoke(f, new object[] { new ChatMsg(i % 2 == 0 ? "user" : "assistant", "用于验证发送框变高后消息区滚动的测试内容 " + i), i % 2 == 0 });
        FieldInfo ab = typeof(ChatForm).GetField("atBottom", BindingFlags.NonPublic | BindingFlags.Instance);
        ab.SetValue(f, true);
        layout.Invoke(f, new object[] { true });
        Pump(400);
        int bottom1 = -host.AutoScrollPosition.Y;
        input.Text = "第一行\r\n第二行\r\n第三行\r\n第四行";
        Pump(500);
        int bottom2 = -host.AutoScrollPosition.Y;
        Console.WriteLine("bottom before=" + bottom1 + " after grow=" + bottom2 + " max=" + (host.AutoScrollMinSize.Height - host.ClientSize.Height));
        if (Math.Abs((host.AutoScrollMinSize.Height - host.ClientSize.Height) - bottom2) > 2) throw new Exception("not staying at bottom after composer grows");

        Console.WriteLine("COMPOSER OK");
        f.Hide();
        Environment.Exit(0);
    }

    private static object Field(object o, string name)
    {
        return o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(o);
    }

    private static void Pump(int ms)
    {
        DateTime end = DateTime.Now.AddMilliseconds(ms);
        while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(20); }
    }
}
