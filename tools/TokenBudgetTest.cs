using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using DsChat;

/// <summary>
/// 上下文预算 / 工具结果压缩的离线单元测试。编译方式见 README 的“从源码编译”一节：
/// csc /target:exe /out:TokenBudgetTest.exe /r:Lumi.exe tools\TokenBudgetTest.cs
/// </summary>
internal static class TokenBudgetTest
{
    private static void Main()
    {
        try { Run(); }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL: " + ex.Message);
            Environment.Exit(1);
        }
    }

    private static void Run()
    {
        TestEstimate();
        TestClamp();
        TestArguments();
        TestHistoryTrim();
        TestToolCompaction();
        TestSavings();
        TestReadPage();
        TestBuildBody();
        TestConfig();
        Console.WriteLine("TOKEN BUDGET ALL OK");
    }

    private static void TestEstimate()
    {
        if (ContextBudget.EstimateTokens("") != 0) throw new Exception("empty estimate");
        int zh = ContextBudget.EstimateTokens(new string('中', 1000));
        int en = ContextBudget.EstimateTokens(new string('a', 1000));
        Console.WriteLine("estimate zh=" + zh + " en=" + en);
        if (zh < 500 || zh > 700) throw new Exception("zh estimate out of range");
        if (en < 250 || en > 350) throw new Exception("en estimate out of range");
    }

    private static void TestClamp()
    {
        string s = new string('中', 1000);
        string c = ContextBudget.Clamp(s, 100, "消息");
        Console.WriteLine("clamp len=" + c.Length + " text=" + c.Substring(0, 10));
        if (c.Length > 100) throw new Exception("clamp too long: " + c.Length);
        if (c.IndexOf("省略") < 0) throw new Exception("clamp marker missing");
    }

    private static void TestArguments()
    {
        string args = "{\"path\":\"a.txt\",\"content\":\"" + new string('x', 20000) + "\",\"n\":3}";
        string c = ContextBudget.CompactArguments("write_file", args, 300);
        System.Web.Script.Serialization.JavaScriptSerializer js = new System.Web.Script.Serialization.JavaScriptSerializer();
        Dictionary<string, object> d = js.DeserializeObject(c) as Dictionary<string, object>;
        if (d == null) throw new Exception("compacted args is not valid JSON: " + c);
        string content = d["content"] as string;
        Console.WriteLine("args compacted len=" + c.Length + " contentLen=" + (content == null ? -1 : content.Length));
        if (c.Length > 300) throw new Exception("args still too long");
        if (content == null || content.Length >= 20000) throw new Exception("content not compacted");
        if (!(d["n"] is int) || (int)d["n"] != 3) throw new Exception("number field lost");

        string cn = "{\"path\":\"中文.txt\",\"content\":\"" + new string('中', 3000) + "\"}";
        string cc = ContextBudget.CompactArguments("write_file", cn, 400);
        if (cc.IndexOf("\\u") >= 0) throw new Exception("chinese was escaped: " + cc.Substring(0, 40));
        if (cc.IndexOf("中文") < 0) throw new Exception("chinese key lost");
    }

    private static void TestHistoryTrim()
    {
        List<ChatMsg> history = new List<ChatMsg>();
        for (int i = 0; i < 60; i++)
        {
            string role = (i % 2 == 0) ? "user" : "assistant";
            history.Add(new ChatMsg(role, "第" + i + "条：" + new string('中', 1500)));
        }
        history[history.Count - 1] = new ChatMsg("user", "最后的问题：" + new string('问', 500));

        int before = history.Count;
        List<ChatMsg> built = ContextBudget.Build(history, null, 3000);
        int tokens = ContextBudget.EstimateMessages(built);
        Console.WriteLine("history trim: " + before + " -> " + built.Count + ", estimate=" + tokens);
        if (built.Count >= before) throw new Exception("history not trimmed");
        if (built[built.Count - 1].Content.IndexOf("最后的问题") < 0) throw new Exception("last user message dropped");
        if (built[0].Role == "tool") throw new Exception("payload starts with tool message");
        // 原文不能被改动
        if (history[history.Count - 1].Content.Length != 506) throw new Exception("original history mutated");
        if (tokens > 4500) throw new Exception("trimmed payload still too big: " + tokens);
    }

    private static void TestToolCompaction()
    {
        List<ChatMsg> history = new List<ChatMsg>();
        history.Add(new ChatMsg("user", "把大文件改一下"));
        List<ChatMsg> extra = new List<ChatMsg>();
        for (int round = 1; round <= 5; round++)
        {
            string bigWrite = "{\"path\":\"big.txt\",\"content\":\"" + new string('写', 8000) + "\"}";
            List<ToolCall> calls = new List<ToolCall>();
            calls.Add(new ToolCall { Id = "call_" + round, Name = "write_file", Arguments = bigWrite });
            ChatMsg asst = new ChatMsg("assistant", "第" + round + "轮说明");
            asst.ToolCalls = calls;
            asst.ReasoningContent = "思考" + round + "：" + new string('想', 100);
            extra.Add(asst);

            ChatMsg tool = new ChatMsg("tool", "结果" + round + "：" + new string('果', 8000));
            tool.ToolCallId = "call_" + round;
            extra.Add(tool);
        }

        List<ChatMsg> built = ContextBudget.Build(history, extra, 12000);
        int tokens = ContextBudget.EstimateMessages(built);
        Console.WriteLine("tool compaction: " + extra.Count + " -> " + built.Count + ", estimate=" + tokens);
        if (tokens > 12000) throw new Exception("tool payload over budget: " + tokens);
        AssertPairs(built);

        int oldResults = 0, fullResults = 0, oldArgs = 0;
        bool sawReasoning = false;
        foreach (ChatMsg m in built)
        {
            if (m.Role == "tool")
            {
                if (m.Content.Length > 700) fullResults++;
                else oldResults++;
            }
            if (m.ReasoningContent != null && m.ReasoningContent.Length > 0) sawReasoning = true;
            if (m.ToolCalls != null)
            {
                foreach (ToolCall tc in m.ToolCalls)
                {
                    if (tc.Arguments.Length > 1000) oldArgs++;
                    if (tc.Name == "write_file" && tc.Arguments.Length > 4000) throw new Exception("old write args not compacted");
                }
            }
        }
        Console.WriteLine("oldResults(short)=" + oldResults + " fullResults=" + fullResults + " longArgs=" + oldArgs);
        if (oldResults == 0) throw new Exception("old tool results were not compacted");
        if (!sawReasoning) throw new Exception("reasoning content lost");

        // 原始 turnExtra 不能被修改
        if (extra[1].Content.Length != 8004) throw new Exception("original tool result mutated");
        if (extra[0].ToolCalls[0].Arguments.Length < 8000) throw new Exception("original tool args mutated");
    }

    private static void TestSavings()
    {
        // 模拟一次“读大文件 → 写大文件”重复 12 轮的极端情况，对比压缩前后的估算 token
        List<ChatMsg> history = new List<ChatMsg>();
        history.Add(new ChatMsg("user", "请分析这个大文件并重写一遍"));
        List<ChatMsg> extra = new List<ChatMsg>();
        for (int round = 1; round <= 12; round++)
        {
            string args = "{\"path\":\"big.txt\",\"content\":\"" + new string('写', 15000) + "\"}";
            List<ToolCall> calls = new List<ToolCall>();
            calls.Add(new ToolCall { Id = "call_" + round, Name = "write_file", Arguments = args });
            ChatMsg asst = new ChatMsg("assistant", "");
            asst.ToolCalls = calls;
            asst.ReasoningContent = new string('想', 400);
            extra.Add(asst);
            ChatMsg tool = new ChatMsg("tool", new string('果', 20000));
            tool.ToolCallId = "call_" + round;
            extra.Add(tool);
        }

        List<ChatMsg> all = new List<ChatMsg>(history);
        all.AddRange(extra);
        int oldTokens = ContextBudget.EstimateMessages(all);
        List<ChatMsg> built = ContextBudget.Build(history, extra, ContextBudget.DefaultTokens);
        int newTokens = ContextBudget.EstimateMessages(built);
        Console.WriteLine("savings: old=" + oldTokens + " new=" + newTokens + " = " + (100 - newTokens * 100 / oldTokens) + "%");
        if (newTokens >= oldTokens / 4) throw new Exception("savings too small: " + newTokens + " / " + oldTokens);
        if (newTokens > ContextBudget.DefaultTokens) throw new Exception("new payload over budget");
        if (built[0].Role != "user" || built[0].Content.IndexOf("分析") < 0) throw new Exception("task message dropped");
        AssertPairs(built);

        // 预算再收紧：必须丢掉较早的工具轮次，但当前任务和工具调用对仍要保留
        List<ChatMsg> tight = ContextBudget.Build(history, extra, 6000);
        int tightTools = CountRole(tight, "tool");
        Console.WriteLine("tight: tools=" + tightTools + " estimate=" + ContextBudget.EstimateMessages(tight));
        if (tightTools >= 12) throw new Exception("old tool rounds were not dropped under tight budget");
        if (tight[0].Role != "user" || tight[0].Content.IndexOf("分析") < 0) throw new Exception("task message dropped in tight budget");
        AssertPairs(tight);
    }

    private static void TestReadPage()
    {
        string root = Path.Combine(Path.GetTempPath(), "ds_page_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 1; i <= 1000; i++) sb.Append("line ").Append(i).Append('\n');
            Workspace.WriteText(root, "f.txt", sb.ToString());
            int a, b, total;
            string p1 = Workspace.ReadTextPage(root, "f.txt", 1, 10, out a, out b, out total);
            Console.WriteLine("page1: " + a + "-" + b + "/" + total + " len=" + p1.Length);
            if (a != 1 || b != 10) throw new Exception("page1 range wrong");
            if (total < 1000) throw new Exception("total lines wrong");
            if (p1.IndexOf("line 1") < 0 || p1.IndexOf("\nline 11") >= 0) throw new Exception("page1 body wrong");
            if (p1.IndexOf("start_line=11") < 0) throw new Exception("page1 footer missing");

            string p2 = Workspace.ReadTextPage(root, "f.txt", 101, 10, out a, out b, out total);
            if (a != 101 || b != 110) throw new Exception("page2 range wrong");
            if (p2.IndexOf("line 101") < 0) throw new Exception("page2 body wrong");

            string beyond = Workspace.ReadTextPage(root, "f.txt", 5000, 10, out a, out b, out total);
            if (beyond.IndexOf("超出范围") < 0) throw new Exception("beyond range not reported");

            // 长行：单页字符数必须被限制住
            StringBuilder lb = new StringBuilder();
            for (int i = 0; i < 500; i++) lb.Append(new string('x', 200)).Append('\n');
            Workspace.WriteText(root, "long.txt", lb.ToString());
            string lp = Workspace.ReadTextPage(root, "long.txt", 1, 400, out a, out b, out total);
            Console.WriteLine("long page: " + a + "-" + b + "/" + total + " len=" + lp.Length);
            if (lp.Length > 6500) throw new Exception("long page not capped: " + lp.Length);
            if (b >= 500) throw new Exception("long page did not stop at char cap");

            // 目录列表上限
            string many = Path.Combine(root, "many");
            Directory.CreateDirectory(many);
            for (int i = 0; i < 130; i++) File.WriteAllText(Path.Combine(many, "f" + i + ".txt"), "x");
            string list = Workspace.ListFiles(root, "many");
            Console.WriteLine("list length=" + list.Length);
            if (list.IndexOf("只显示前 120 项") < 0) throw new Exception("list cap message missing");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static void TestBuildBody()
    {
        string root = Path.Combine(Path.GetTempPath(), "ds_body_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Config c = new Config();
            c.Workspace = root;
            c.EnableTools = true;
            c.Concise = true;
            List<ChatMsg> msgs = new List<ChatMsg>();
            msgs.Add(new ChatMsg("user", "hi"));
            ChatMsg asst = new ChatMsg("assistant", "");
            asst.ToolCalls = new List<ToolCall>();
            asst.ToolCalls.Add(new ToolCall { Id = "call_1", Name = "list_files", Arguments = "{}" });
            asst.ReasoningContent = "内部思考";
            msgs.Add(asst);
            ChatMsg tool = new ChatMsg("tool", "ok");
            tool.ToolCallId = "call_1";
            msgs.Add(tool);

            string body = InvokeBuildBody(new ChatClient(c), msgs);
            if (body.IndexOf("reasoning_content") < 0) throw new Exception("reasoning missing when tools are on");
            if (body.IndexOf("回答简洁") < 0) throw new Exception("concise suffix missing");
            if (body.IndexOf("start_line") < 0) throw new Exception("read_file pagination schema missing");
            Console.WriteLine("body with tools len=" + body.Length);

            c.EnableTools = false;
            string body2 = InvokeBuildBody(new ChatClient(c), msgs);
            if (body2.IndexOf("reasoning_content") >= 0) throw new Exception("reasoning wasted when tools are off");
            Console.WriteLine("body without tools len=" + body2.Length);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static string InvokeBuildBody(ChatClient cli, List<ChatMsg> msgs)
    {
        MethodInfo mi = typeof(ChatClient).GetMethod("BuildBody", BindingFlags.NonPublic | BindingFlags.Instance);
        return (string)mi.Invoke(cli, new object[] { msgs, false });
    }

    private static void TestConfig()
    {
        string home = Path.Combine(Path.GetTempPath(), "ds_cfg_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        Environment.SetEnvironmentVariable("DSCHAT_HOME", home);
        try
        {
            Config c = new Config();
            c.ContextTokens = 4321;
            c.Concise = false;
            c.Save();
            Config loaded = Config.Load();
            Console.WriteLine("config roundtrip: context=" + loaded.ContextTokens + " concise=" + loaded.Concise);
            if (loaded.ContextTokens != 4321 || loaded.Concise) throw new Exception("config roundtrip failed");
        }
        finally
        {
            try { Directory.Delete(home, true); } catch { }
        }
    }

    private static int CountRole(List<ChatMsg> msgs, string role)
    {
        int n = 0;
        foreach (ChatMsg m in msgs) if (m.Role == role) n++;
        return n;
    }

    private static void AssertPairs(List<ChatMsg> msgs)
    {
        for (int i = 0; i < msgs.Count; i++)
        {
            ChatMsg m = msgs[i];
            if (m.Role != "tool") continue;

            int j = i - 1;
            while (j >= 0 && msgs[j].Role == "tool") j--;
            if (j < 0) throw new Exception("orphan tool at " + i);
            ChatMsg a = msgs[j];
            if (a.Role != "assistant" || a.ToolCalls == null) throw new Exception("tool without assistant at " + i);
            bool found = false;
            foreach (ToolCall tc in a.ToolCalls) if (tc != null && tc.Id == m.ToolCallId) found = true;
            if (!found) throw new Exception("tool id not matched at " + i + ": " + m.ToolCallId);
        }

        for (int i = 0; i < msgs.Count; i++)
        {
            ChatMsg m = msgs[i];
            if (m.Role != "assistant" || m.ToolCalls == null) continue;
            int end = i + 1;
            while (end < msgs.Count && msgs[end].Role == "tool") end++;
            foreach (ToolCall tc in m.ToolCalls)
            {
                if (tc == null) continue;
                bool found = false;
                for (int k = i + 1; k < end; k++) if (msgs[k].ToolCallId == tc.Id) found = true;
                if (!found) throw new Exception("tool result missing for " + tc.Id);
            }
        }
    }
}
