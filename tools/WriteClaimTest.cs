using System;
using System.Collections;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using DsChat;

/// <summary>
/// 回归测试：模型第一轮只说“已写入”但没有调用 write_file，客户端应自动补一轮真正调用工具。
/// 需要 MockServer 运行在 8848。
/// </summary>
internal static class WriteClaimTest
{
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();

    [STAThread]
    private static void Main()
    {
        string home = Path.Combine(Path.GetTempPath(), "ds_writeclaim_home");
        string ws = Path.Combine(Path.GetTempPath(), "ds_writeclaim_ws");
        if (Directory.Exists(home)) Directory.Delete(home, true);
        if (Directory.Exists(ws)) Directory.Delete(ws, true);
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(ws);
        Environment.SetEnvironmentVariable("AGENTCHAT_HOME", home);

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

        using (ChatForm f = new ChatForm(cfg))
        {
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(0, 0);
            f.Show();
            f.Activate();
            Pump(300);

            TextBox input = (TextBox)Field(f, "input");
            input.Text = "writeclaim 请写入 claimed.txt";
            Invoke(f, "OnSendClicked");
            WaitIdle(f, 40);

            string path = Path.Combine(ws, "claimed.txt");
            Console.WriteLine("success file exists=" + File.Exists(path));
            if (!File.Exists(path)) throw new Exception("auto-nudge did not write file");
            if (File.ReadAllText(path).IndexOf("written by mock") < 0) throw new Exception("written content wrong");
            if (!(bool)Field(f, "autoNudgedThisTurn")) throw new Exception("auto nudge flag false");
            string last = LastAssistant(f);
            Console.WriteLine("success last=[" + last + "]");
            if (last.IndexOf("仍未检测到实际") >= 0) throw new Exception("success was marked as failed");

            // 再测一次：自动重试后模型仍然只说不做，应显示本地警告而不是假成功
            File.Delete(path);
            input.Text = "writeclaimfail 请写入 claimed.txt";
            Invoke(f, "OnSendClicked");
            WaitIdle(f, 40);

            Console.WriteLine("fail file exists=" + File.Exists(path));
            if (File.Exists(path)) throw new Exception("fail case unexpectedly wrote file");
            last = LastAssistant(f);
            Console.WriteLine("fail last=[" + last + "]");
            if (last.IndexOf("仍未检测到实际的 write_file 调用") < 0) throw new Exception("missing local warning");
        }

        Console.WriteLine("WRITE CLAIM OK");
        Environment.Exit(0);
    }

    private static void Invoke(ChatForm f, string method)
    {
        typeof(ChatForm).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(f, null);
    }

    private static void WaitIdle(ChatForm f, int seconds)
    {
        DateTime end = DateTime.Now.AddSeconds(seconds);
        while (DateTime.Now < end)
        {
            Pump(100);
            if (!(bool)Field(f, "streaming")) return;
        }
        throw new Exception("timeout waiting for turn");
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
