using System;
using System.Collections;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using DsChat;

internal static class ConfirmFlowTest
{
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    private static int confirms;
    private static volatile bool allowTurn;

    [STAThread]
    private static void Main()
    {
        string home = Path.Combine(Path.GetTempPath(), "ds_confirm_home");
        if (Directory.Exists(home)) Directory.Delete(home, true);
        Directory.CreateDirectory(home);
        Environment.SetEnvironmentVariable("DSCHAT_HOME", home);
        string ws = Path.Combine(Path.GetTempPath(), "ds_confirm_ws_" + DateTime.Now.Ticks);
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
        cfg.ConfirmWrites = true;
        cfg.Workspace = ws;

        ChatForm f = new ChatForm(cfg);
        f.StartPosition = FormStartPosition.Manual;
        f.Location = new Point(0, 0);
        f.Show();
        f.Activate();
        Pump(400);

        System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
        t.Interval = 100;
        t.Tick += delegate
        {
            Form dlg = Form.ActiveForm;
            if (dlg == null || dlg.Text != "确认写入") return;
            confirms++;
            string want = allowTurn ? "本轮全部允许" : "允许";
            foreach (Control c in dlg.Controls)
            {
                Button b = c as Button;
                if (b != null && b.Text == want) { b.PerformClick(); return; }
            }
            foreach (Control c in dlg.Controls)
            {
                Button b = c as Button;
                if (b != null && b.Text == "允许") { b.PerformClick(); return; }
            }
        };
        t.Start();

        TextBox input = (TextBox)Field(f, "input");
        input.Text = "tooltest:write";
        typeof(ChatForm).GetMethod("OnSendClicked", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(f, null);

        DateTime end = DateTime.Now.AddSeconds(25);
        while (DateTime.Now < end)
        {
            Pump(100);
            if (!(bool)Field(f, "streaming")) break;
        }
        bool streaming = (bool)Field(f, "streaming");
        string last = LastAssistant(f);
        bool exists = File.Exists(Path.Combine(ws, "ai_test.txt"));
        Console.WriteLine("confirms=" + confirms + " streaming=" + streaming + " file=" + exists + " last=[" + last + "]");
        if (confirms != 1) throw new Exception("expected exactly one confirmation, got " + confirms);
        if (streaming) throw new Exception("still streaming after allow");
        if (!exists) throw new Exception("file not written");
        if (last.IndexOf("任务完成") < 0) throw new Exception("no final assistant text");

        // 重复写入：点一次“本轮全部允许”，后续不再弹窗，第 3 轮自动总结
        allowTurn = true;
        input.Text = "writeloop";
        typeof(ChatForm).GetMethod("OnSendClicked", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(f, null);
        end = DateTime.Now.AddSeconds(30);
        while (DateTime.Now < end)
        {
            Pump(100);
            if (!(bool)Field(f, "streaming")) break;
        }
        t.Stop();
        streaming = (bool)Field(f, "streaming");
        last = LastAssistant(f);
        int rounds = (int)Field(f, "toolRounds");
        bool loopFile = File.Exists(Path.Combine(ws, "loop.txt"));
        Console.WriteLine("writeloop confirms=" + confirms + " rounds=" + rounds + " streaming=" + streaming + " file=" + loopFile + " last=[" + last + "]");
        if (confirms != 2) throw new Exception("expected 2 confirmations total, got " + confirms);
        if (rounds != 3) throw new Exception("expected 3 write rounds, got " + rounds);
        if (streaming) throw new Exception("still streaming after write loop");
        if (!loopFile) throw new Exception("loop.txt not written");
        if (last.IndexOf("总结") < 0) throw new Exception("no write-loop summary");

        Console.WriteLine("CONFIRM FLOW OK");
        f.Hide();
        Environment.Exit(0);
    }

    private static int PendingLen(ChatForm f)
    {
        return ((System.Text.StringBuilder)Field(f, "pending")).Length;
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
