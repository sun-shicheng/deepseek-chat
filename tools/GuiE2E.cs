using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using DsChat;

internal static class GuiE2E
{
    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    [STAThread]
    private static void Main()
    {
        try { SetProcessDPIAware(); } catch { }
        Ui.Detect();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        string root = Path.Combine(Path.GetTempPath(), "ds_gui_ws");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(root);

        Config cfg = new Config();
        cfg.BaseUrl = "http://127.0.0.1:8848";
        cfg.ApiPath = "/v1/chat/completions";
        cfg.ApiKey = "mock";
        cfg.Model = "mock";
        cfg.TrustEnv = false;      // 直连
        cfg.EnableTools = true;
        cfg.ConfirmWrites = false; // e2e 不弹窗
        cfg.Workspace = root;

        ChatForm f = new ChatForm(cfg);
        f.Show();
        Pump(600);

        Send(f, "tooltest:write");
        bool ok = WaitFile(Path.Combine(root, "ai_test.txt"), 25000);
        Console.WriteLine("write_file -> " + ok + " exists=" + File.Exists(Path.Combine(root, "ai_test.txt")));
        if (ok)
        {
            string text = File.ReadAllText(Path.Combine(root, "ai_test.txt"));
            Console.WriteLine("content = " + text.Replace("\r", "").Replace("\n", "|"));
        }

        Send(f, "tooltest:evil");
        Thread.Sleep(6000);
        Pump(200);
        string evil = Path.Combine(Path.GetDirectoryName(root), "evil.txt");
        Console.WriteLine("traversal blocked = " + (!File.Exists(evil)));
        Console.WriteLine("root files = " + string.Join(", ", Directory.GetFiles(root)));

        f.Hide();
        Environment.Exit(ok && !File.Exists(evil) ? 0 : 1);
    }

    private static void Send(ChatForm f, string text)
    {
        FieldInfo fi = typeof(ChatForm).GetField("input", BindingFlags.NonPublic | BindingFlags.Instance);
        TextBox input = (TextBox)fi.GetValue(f);
        input.Text = text;
        MethodInfo mi = typeof(ChatForm).GetMethod("OnSendClicked", BindingFlags.NonPublic | BindingFlags.Instance);
        mi.Invoke(f, null);
        Pump(300);
    }

    private static bool WaitFile(string path, int ms)
    {
        DateTime end = DateTime.Now.AddMilliseconds(ms);
        while (DateTime.Now < end)
        {
            Pump(50);
            if (File.Exists(path)) return true;
        }
        return false;
    }

    private static void Pump(int ms)
    {
        DateTime end = DateTime.Now.AddMilliseconds(ms);
        while (DateTime.Now < end)
        {
            Application.DoEvents();
            Thread.Sleep(20);
        }
    }
}
