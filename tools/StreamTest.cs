using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using DsChat;

internal static class StreamTest
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
        cfg.BaseUrl = "http://127.0.0.1:8848";
        cfg.ApiPath = "/v1/chat/completions";
        cfg.ApiKey = "mock";
        cfg.Model = "mock";
        cfg.Stream = true;
        cfg.TrustEnv = false;

        ChatForm f = new ChatForm(cfg);
        f.StartPosition = FormStartPosition.Manual;
        f.Location = new Point(0, 0);
        f.Show();
        Pump(400);

        TextBox input = (TextBox)Field(f, "input");
        List<MessageView> views = (List<MessageView>)Field(f, "views");
        input.Text = "给我讲一段长回答";
        typeof(ChatForm).GetMethod("OnSendClicked", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(f, null);

        List<int> samples = new List<int>();
        int last = -1;
        DateTime end = DateTime.Now.AddSeconds(8);
        while (DateTime.Now < end)
        {
            Pump(80);
            if (views.Count == 0) continue;
            string t = views[views.Count - 1].GetText();
            int len = t == null ? 0 : t.Length;
            if (len != last) { samples.Add(len); last = len; }
            bool streaming = (bool)Field(f, "streaming");
            if (!streaming && len > 0) break;
        }

        string finalView = views[views.Count - 1].GetText();
        Console.WriteLine("samples=" + string.Join(",", samples.ConvertAll(x => x.ToString()).ToArray()));
        Console.WriteLine("final view length=" + finalView.Length);
        int intermediate = 0;
        foreach (int n in samples) if (n > 0 && n < finalView.Length) intermediate++;
        Console.WriteLine("intermediate samples=" + intermediate);
        if (intermediate < 3) throw new Exception("not a gradual stream (samples too few)");

        System.Collections.IList sessions = (System.Collections.IList)Field(f, "sessions");
        int active = (int)Field(f, "active");
        object session = sessions[active];
        System.Collections.IList msgs = (System.Collections.IList)session.GetType().GetField("Messages").GetValue(session);
        object lastMsg = msgs[msgs.Count - 1];
        string saved = (string)lastMsg.GetType().GetField("Content").GetValue(lastMsg);
        Console.WriteLine("saved length=" + saved.Length + " equals final=" + (saved == finalView));
        if (saved != finalView) throw new Exception("saved content differs from displayed content");

        Console.WriteLine("STREAM OK");
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
