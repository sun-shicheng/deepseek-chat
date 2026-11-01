using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using DsChat;

internal static class WheelTest
{
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, int dx, int dy, int data, UIntPtr extra);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private const uint MOUSEEVENTF_WHEEL = 0x0800;

    [STAThread]
    private static void Main()
    {
        POINT old;
        GetCursorPos(out old);
        int exit = 1;
        try
        {
            try { SetProcessDPIAware(); } catch { }
            Ui.Detect();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Config cfg = new Config();
            cfg.BaseUrl = "http://127.0.0.1:1";
            cfg.ApiKey = "x";
            cfg.Workspace = "";
            cfg.EnableTools = false;

            ChatForm f = new ChatForm(cfg);
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(0, 0);
            f.Show();
            f.Activate();
            Pump(500);

            MethodInfo add = typeof(ChatForm).GetMethod("AddView", BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo layout = typeof(ChatForm).GetMethod("LayoutMessages", BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < 30; i++)
                add.Invoke(f, new object[] { new ChatMsg(i % 2 == 0 ? "user" : "assistant", "第 " + i + " 条消息：这是一段用来撑高聊天区域、验证鼠标滚轮能否滑动的测试文字。"), i % 2 == 0 });
            layout.Invoke(f, new object[] { true });
            Pump(800);

            FieldInfo hostField = typeof(ChatForm).GetField("msgHost", BindingFlags.NonPublic | BindingFlags.Instance);
            Panel host = (Panel)hostField.GetValue(f);
            Console.WriteLine("host height=" + host.ClientSize.Height + " scrollMax=" + host.AutoScrollMinSize.Height);
            host.AutoScrollPosition = new Point(0, 0);
            Pump(300);
            if (-host.AutoScrollPosition.Y != 0) throw new Exception("expected start at top");

            Point p = host.PointToScreen(new Point(host.ClientSize.Width / 2, host.ClientSize.Height / 2));
            SetForegroundWindow(f.Handle);
            SetCursorPos(p.X, p.Y);
            Pump(200);

            mouse_event(MOUSEEVENTF_WHEEL, 0, 0, -360, UIntPtr.Zero);
            Pump(600);
            int afterDown = -host.AutoScrollPosition.Y;
            Console.WriteLine("after wheel down: " + afterDown);
            if (afterDown <= 0) throw new Exception("wheel down did not scroll");

            mouse_event(MOUSEEVENTF_WHEEL, 0, 0, 360, UIntPtr.Zero);
            Pump(600);
            int afterUp = -host.AutoScrollPosition.Y;
            Console.WriteLine("after wheel up: " + afterUp);
            if (afterUp >= afterDown) throw new Exception("wheel up did not scroll back");

            // 鼠标在输入区时不应滚动消息区
            FieldInfo inputField = typeof(ChatForm).GetField("input", BindingFlags.NonPublic | BindingFlags.Instance);
            TextBox input = (TextBox)inputField.GetValue(f);
            Point ip = input.PointToScreen(new Point(input.ClientSize.Width / 2, input.ClientSize.Height / 2));
            SetCursorPos(ip.X, ip.Y);
            Pump(150);
            mouse_event(MOUSEEVENTF_WHEEL, 0, 0, -360, UIntPtr.Zero);
            Pump(400);
            int afterInput = -host.AutoScrollPosition.Y;
            Console.WriteLine("after wheel over input: " + afterInput);
            if (afterInput != afterUp) throw new Exception("wheel over input should not scroll messages");

            Console.WriteLine("WHEEL OK");
            exit = 0;
            f.Hide();
            Environment.Exit(exit);
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL: " + ex.Message);
        }
        finally
        {
            SetCursorPos(old.X, old.Y);
        }
        Environment.Exit(exit);
    }

    private static void Pump(int ms)
    {
        DateTime end = DateTime.Now.AddMilliseconds(ms);
        while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(20); }
    }
}
