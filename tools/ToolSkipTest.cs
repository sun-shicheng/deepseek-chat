using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DsChat;

internal static class ToolSkipTest
{
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();

    [STAThread]
    private static void Main()
    {
        string home = Path.Combine(Path.GetTempPath(), "ds_skip_home");
        if (Directory.Exists(home)) Directory.Delete(home, true);
        Directory.CreateDirectory(home);
        Environment.SetEnvironmentVariable("DSCHAT_HOME", home);
        string ws = Path.Combine(Path.GetTempPath(), "ds_skip_ws");
        if (Directory.Exists(ws)) Directory.Delete(ws, true);
        Directory.CreateDirectory(ws);
        Workspace.WriteText(ws, "same.txt", "abc");

        try { SetProcessDPIAware(); } catch { }
        Ui.Detect();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Config cfg = new Config();
        cfg.BaseUrl = "http://127.0.0.1:1";
        cfg.ApiKey = "x";
        cfg.EnableTools = true;
        cfg.ConfirmWrites = true;
        cfg.Workspace = ws;

        ChatForm f = new ChatForm(cfg);
        MethodInfo exec = typeof(ChatForm).GetMethod("ExecuteTool", BindingFlags.NonPublic | BindingFlags.Instance);

        ToolCall write = new ToolCall();
        write.Name = "write_file";
        write.Arguments = "{\"path\":\"same.txt\",\"content\":\"abc\"}";
        object[] args = new object[] { write, null };
        string result = (string)exec.Invoke(f, args);
        Console.WriteLine("same write -> " + result);
        if (result.IndexOf("无需重复写入") < 0) throw new Exception("same-content write should skip confirmation");

        ToolCall del = new ToolCall();
        del.Name = "delete_file";
        del.Arguments = "{\"path\":\"missing.txt\"}";
        args = new object[] { del, null };
        result = (string)exec.Invoke(f, args);
        Console.WriteLine("missing delete -> " + result);
        if (result.IndexOf("本来就不存在") < 0) throw new Exception("missing delete should skip confirmation");

        // 同一轮已允许的路径不再弹窗
        FieldInfo ap = typeof(ChatForm).GetField("approvedWritePaths", BindingFlags.NonPublic | BindingFlags.Instance);
        System.Collections.Generic.HashSet<string> set = (System.Collections.Generic.HashSet<string>)ap.GetValue(f);
        set.Add(Workspace.Resolve(ws, "other.txt").ToLowerInvariant());
        ToolCall write2 = new ToolCall();
        write2.Name = "write_file";
        write2.Arguments = "{\"path\":\"other.txt\",\"content\":\"new content\"}";
        args = new object[] { write2, null };
        result = (string)exec.Invoke(f, args);
        Console.WriteLine("approved path -> " + result);
        if (result.IndexOf("写入成功") < 0) throw new Exception("approved path should write without dialog");

        Console.WriteLine("SKIP OK");
        Environment.Exit(0);
    }
}
