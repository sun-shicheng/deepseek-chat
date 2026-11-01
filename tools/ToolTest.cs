using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using DsChat;

internal static class ToolTest
{
    private static ManualResetEvent done;
    private static StringBuilder got;
    private static string err;

    private static void Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "ds_ws_test");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(root);
        Console.WriteLine("root = " + root);

        // 1. 越界路径
        ExpectFail(root, "../evil.txt");
        ExpectFail(root, @"C:\Windows\win.ini");

        // 2. 读写删
        int n = Workspace.WriteText(root, "sub/a.txt", "你好\r\nline2");
        string back = Workspace.ReadText(root, "sub/a.txt");
        Console.WriteLine("write " + n + " chars, read back = " + back.Replace("\r", "").Replace("\n", "|"));
        if (back.IndexOf("你好") < 0) throw new Exception("read mismatch");
        Console.WriteLine(Workspace.ListFiles(root, ""));
        Workspace.Delete(root, "sub/a.txt");
        Console.WriteLine("delete ok, exists=" + File.Exists(Path.Combine(root, "sub", "a.txt")));

        Config cfg = new Config();
        cfg.BaseUrl = "http://127.0.0.1:8848";
        cfg.ApiPath = "/v1/chat/completions";
        cfg.ApiKey = "mock";
        cfg.Model = "mock";
        cfg.TrustEnv = true;          // 直连，忽略系统代理
        cfg.EnableTools = true;
        cfg.Workspace = root;
        cfg.Stream = true;

        // 3. 工具调用：第一轮模型要求 write_file
        List<ChatMsg> msgs = new List<ChatMsg>();
        msgs.Add(new ChatMsg("user", "tooltest:write"));
        List<ToolCall> calls;
        string text;
        Run(cfg, msgs, out calls, out text);
        Console.WriteLine("turn1 text=[" + text + "] toolcalls=" + calls.Count);
        foreach (ToolCall tc in calls)
            Console.WriteLine("  call id=" + tc.Id + " name=" + tc.Name + " args=" + tc.Arguments);
        if (calls.Count != 1 || calls[0].Name != "write_file") throw new Exception("tool_calls parse failed");
        string args = calls[0].Arguments;
        string path = ArgOf(args, "path");
        string content = ArgOf(args, "content");
        Console.WriteLine("parsed path=" + path + " content=" + content.Replace("\n", "|"));
        Workspace.WriteText(cfg.Workspace, path, content);

        // 4. 回填 tool 结果后再请求
        ChatMsg asst = new ChatMsg("assistant", "");
        asst.ToolCalls = calls;
        msgs.Add(asst);
        ChatMsg toolMsg = new ChatMsg("tool", "写入成功：" + path);
        toolMsg.ToolCallId = calls[0].Id;
        msgs.Add(toolMsg);
        Run(cfg, msgs, out calls, out text);
        Console.WriteLine("turn2 text=[" + text + "] toolcalls=" + calls.Count);
        if (text.IndexOf("任务完成") < 0) throw new Exception("second turn failed");

        // 5. 写入结果
        string written = File.ReadAllText(Path.Combine(root, "ai_test.txt"), Encoding.UTF8);
        Console.WriteLine("file content = " + written.Replace("\r", "").Replace("\n", "|"));

        // 6. 模拟 AI 试图越界
        ExpectFail(root, "../evil.txt");
        Console.WriteLine("ALL OK");
    }

    private static void ExpectFail(string root, string rel)
    {
        try
        {
            Workspace.Resolve(root, rel);
            throw new Exception("FAIL: allowed " + rel);
        }
        catch (Exception ex)
        {
            if (ex.Message.StartsWith("FAIL:")) throw;
            Console.WriteLine("OK rejected [" + rel + "] -> " + ex.Message);
        }
    }

    private static void Run(Config cfg, List<ChatMsg> msgs, out List<ToolCall> calls, out string text)
    {
        done = new ManualResetEvent(false);
        got = new StringBuilder();
        err = null;
        calls = null;
        ChatClient cli = new ChatClient(cfg);
        cli.OnDelta += delegate (string s) { lock (got) got.Append(s); };
        cli.OnError += delegate (string e) { err = e; done.Set(); };
        cli.OnDone += delegate () { done.Set(); };
        cli.Send(msgs);
        if (!done.WaitOne(20000)) throw new Exception("timeout");
        if (err != null) throw new Exception("api error: " + err);
        calls = cli.ToolCalls;
        text = got.ToString();
    }

    private static string ArgOf(string json, string key)
    {
        System.Web.Script.Serialization.JavaScriptSerializer js = new System.Web.Script.Serialization.JavaScriptSerializer();
        Dictionary<string, object> d = js.DeserializeObject(json) as Dictionary<string, object>;
        object v;
        if (d != null && d.TryGetValue(key, out v) && v != null) return Convert.ToString(v);
        return "";
    }
}
