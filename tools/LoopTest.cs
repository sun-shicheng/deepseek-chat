using System;
using System.Collections;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using DsChat;

internal static class LoopTest
{
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();

    [STAThread]
    private static void Main()
    {
        string home = Path.Combine(Path.GetTempPath(), "ds_loop_home");
        if (Directory.Exists(home)) Directory.Delete(home, true);
        Directory.CreateDirectory(home);
        Environment.SetEnvironmentVariable("DSCHAT_HOME", home);

        string ws = Path.Combine(Path.GetTempPath(), "ds_loop_ws");
        if (Directory.Exists(ws)) Directory.Delete(ws, true);
        Directory.CreateDirectory(ws);

        try { SetProcessDPIAware(); } catch { }
        Ui.Detect();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Config cfg = new Config();
        cfg.BaseUrl = "http://127.0.0.1:8848";
        cfg.ApiPath = "/v1/chat/completions";
        cfg.ApiKey = "mock";
        cfg.Model = "mock";
        cfg.Stream = true;
        cfg.TrustEnv = true;
        cfg.EnableTools = true;
        cfg.ConfirmWrites = false;
        cfg.Workspace = ws;

        ChatForm f = new ChatForm(cfg);
        f.StartPosition = FormStartPosition.Manual;
        f.Location = new Point(0, 0);
        f.Show();
        f.Activate();
        Pump(400);

        TextBox input = (TextBox)Field(f, "input");
        input.Text = "loopforever";
        typeof(ChatForm).GetMethod("OnSendClicked", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(f, null);

        DateTime end = DateTime.Now.AddSeconds(40);
        while (DateTime.Now < end)
        {
            Pump(100);
            if (!(bool)Field(f, "streaming")) break;
        }

        int rounds = (int)Field(f, "toolRounds");
        bool summarizing = (bool)Field(f, "summarizing");
        IList sessions = (IList)Field(f, "sessions");
        int active = (int)Field(f, "active");
        object session = sessions[active];
        IList msgs = (IList)session.GetType().GetField("Messages").GetValue(session);
        string last = "";
        for (int i = msgs.Count - 1; i >= 0; i--)
        {
            object m = msgs[i];
            string role = (string)m.GetType().GetField("Role").GetValue(m);
            string content = (string)m.GetType().GetField("Content").GetValue(m);
            if (role == "assistant" && !string.IsNullOrEmpty(content)) { last = content; break; }
        }

        Console.WriteLine("rounds=" + rounds + " summarizing=" + summarizing + " last=[" + last + "]");
        int max = (int)typeof(ChatForm).GetField("MaxToolRounds", BindingFlags.NonPublic | BindingFlags.Static).GetRawConstantValue();
        if (rounds != max) throw new Exception("expected " + max + " tool rounds, got " + rounds);
        if (last.IndexOf("总结") < 0) throw new Exception("no final summary text");
        Console.WriteLine("LOOP OK");
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
        while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(10); }
    }
}
