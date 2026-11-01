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
        string last = LastAssistant(f);

        Console.WriteLine("rounds=" + rounds + " summarizing=" + summarizing + " last=[" + last + "]");
        int max = (int)typeof(ChatForm).GetField("MaxToolRounds", BindingFlags.NonPublic | BindingFlags.Static).GetRawConstantValue();
        if (rounds != max) throw new Exception("expected " + max + " tool rounds, got " + rounds);
        if (last.IndexOf("总结") < 0) throw new Exception("no final summary text");

        // 重复写入同一文件：第 3 轮应自动停止并生成总结
        input.Text = "writeloop";
        typeof(ChatForm).GetMethod("OnSendClicked", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(f, null);
        end = DateTime.Now.AddSeconds(30);
        while (DateTime.Now < end)
        {
            Pump(100);
            if (!(bool)Field(f, "streaming")) break;
        }
        rounds = (int)Field(f, "toolRounds");
        summarizing = (bool)Field(f, "summarizing");
        last = LastAssistant(f);
        Console.WriteLine("writeloop rounds=" + rounds + " summarizing=" + summarizing + " last=[" + last + "]");
        if (rounds != 3) throw new Exception("expected 3 write rounds, got " + rounds);
        if (last.IndexOf("总结") < 0) throw new Exception("no write-loop summary");
        string fpath = Path.Combine(ws, "loop.txt");
        if (!File.Exists(fpath) || File.ReadAllText(fpath) != "same content") throw new Exception("loop file wrong");

        Console.WriteLine("LOOP OK");
        f.Hide();
        Environment.Exit(0);
    }

    private static string LastAssistant(ChatForm f)
    {
        IList sessions = (IList)Field(f, "sessions");
        int active = (int)Field(f, "active");
        object session = sessions[active];
        IList msgs = (IList)session.GetType().GetField("Messages").GetValue(session);
        for (int i = msgs.Count - 1; i >= 0; i--)
        {
            object m = msgs[i];
            string role = (string)m.GetType().GetField("Role").GetValue(m);
            string content = (string)m.GetType().GetField("Content").GetValue(m);
            if (role == "assistant" && !string.IsNullOrEmpty(content)) return content;
        }
        return "";
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
