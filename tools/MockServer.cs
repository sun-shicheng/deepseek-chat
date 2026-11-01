using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

// 本地模拟 OpenAI 兼容接口（裸 TCP 实现，无需 URL ACL），用于离线验证流式解析与渲染
internal static class MockServer
{
    private static void Main(string[] args)
    {
        int port = args.Length > 0 ? int.Parse(args[0]) : 8848;
        string logPath = args.Length > 1 ? args[1] : "mock.log";
        TcpListener listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        Console.WriteLine("listening on " + port);
        while (true)
        {
            TcpClient client = listener.AcceptTcpClient();
            ThreadPool.QueueUserWorkItem(delegate (object o) { Handle((TcpClient)o, logPath); }, client);
        }
    }

    private static void Handle(TcpClient client, string logPath)
    {
        try
        {
            using (client)
            using (NetworkStream ns = client.GetStream())
            {
                ns.ReadTimeout = 10000;
                string header = ReadHeader(ns);
                if (header.Length == 0) return;

                int contentLength = 0;
                string[] lines = header.Split(new string[] { "\r\n" }, StringSplitOptions.None);
                string reqLine = lines.Length > 0 ? lines[0] : "";
                foreach (string l in lines)
                {
                    if (l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        int.TryParse(l.Substring(15).Trim(), out contentLength);
                }
                byte[] bodyBytes = new byte[contentLength];
                int read = 0;
                while (read < contentLength)
                {
                    int n = ns.Read(bodyBytes, read, contentLength - read);
                    if (n <= 0) break;
                    read += n;
                }
                string body = Encoding.UTF8.GetString(bodyBytes, 0, read);
                Log(logPath, DateTime.Now.ToString("HH:mm:ss") + " " + reqLine + " | " + body);

                bool wantSse = body.Contains("\"stream\":true");
                if (wantSse)
                {
                    Write(ns, "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream; charset=utf-8\r\nCache-Control: no-cache\r\nConnection: close\r\n\r\n");

                    // 工具轮数上限测试：只要请求里还有 tools 就一直要求调用工具
                    if (body.Contains("loopforever"))
                    {
                        if (body.Contains("\"tools\":["))
                        {
                            EmitToolCall(ns, "list_files", "{\"subdir\":\"\"}");
                        }
                        else
                        {
                            string done = "工具调用次数已达上限，这是最后的文字总结：本轮操作已完成。";
                            foreach (string piece in Split(done, 4))
                                Write(ns, "data: {\"choices\":[{\"delta\":{\"content\":\"" + Escape(piece) + "\"}}]}\n\n");
                        }
                        Write(ns, "data: [DONE]\n\n");
                        ns.Flush();
                        return;
                    }

                    // 工具调用联调分支：用户消息里带 tooltest 时返回 tool_calls
                    if (body.Contains("\"role\":\"tool\""))
                    {
                        string done = "工具已执行完毕，任务完成。";
                        foreach (string piece in Split(done, 4))
                            Write(ns, "data: {\"choices\":[{\"delta\":{\"content\":\"" + Escape(piece) + "\"}}]}\n\n");
                        Write(ns, "data: [DONE]\n\n");
                        ns.Flush();
                        return;
                    }
                    if (body.Contains("tooltest"))
                    {
                        if (body.Contains("tooltest:evil"))
                            EmitToolCall(ns, "write_file", "{\"path\":\"../evil.txt\",\"content\":\"should be rejected\"}");
                        else if (body.Contains("tooltest:empty"))
                            EmitToolCallEmptyName(ns, "list_files", "{\"subdir\":\"\"}");
                        else if (body.Contains("tooltest:think"))
                        {
                            Write(ns, "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"内部思考：先看看工作区。\"}}]}\n\n");
                            EmitToolCall(ns, "list_files", "{\"subdir\":\"\"}");
                        }
                        else if (body.Contains("tooltest:write"))
                            EmitToolCall(ns, "write_file", "{\"path\":\"ai_test.txt\",\"content\":\"hello from mock server\\n第二行中文\"}");
                        else if (body.Contains("tooltest:read"))
                            EmitToolCall(ns, "read_file", "{\"path\":\"readme_probe.txt\"}");
                        else
                            EmitToolCall(ns, "list_files", "{\"subdir\":\"\"}");
                        Write(ns, "data: [DONE]\n\n");
                        ns.Flush();
                        return;
                    }

                    string answer = "你好！我是本地模拟服务，用来验证流式输出。\n\n" +
                                    "**重点**：界面支持 `行内代码`、加粗和代码块。\n\n" +
                                    "- 第一项\n- 第二项\n\n" +
                                    "```csharp\nConsole.WriteLine(\"hello\");\n```\n\n" +
                                    "以上就是测试内容，收到请回复。";
                    int step = 3;
                    for (int i = 0; i < answer.Length; i += step)
                    {
                        int len = Math.Min(step, answer.Length - i);
                        string piece = answer.Substring(i, len);
                        string data = "data: {\"choices\":[{\"delta\":{\"content\":\"" + Escape(piece) + "\"}}]}\n\n";
                        Write(ns, data);
                        Thread.Sleep(25);
                    }
                    Write(ns, "data: [DONE]\n\n");
                }
                else
                {
                    string answer = "非流式模拟回复：收到你的消息。";
                    byte[] data = Encoding.UTF8.GetBytes("{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"" + Escape(answer) + "\"}}]}");
                    Write(ns, "HTTP/1.1 200 OK\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: " + data.Length + "\r\nConnection: close\r\n\r\n");
                    ns.Write(data, 0, data.Length);
                }
                ns.Flush();
            }
        }
        catch (Exception ex)
        {
            Log(logPath, "ERR " + ex.Message);
        }
    }

    /// <summary>模拟流式 tool_calls：先发 name/id，再按小块发 arguments。</summary>
    private static void EmitToolCall(NetworkStream ns, string name, string argsJson)
    {
        Write(ns, "data: {\"choices\":[{\"delta\":{\"role\":\"assistant\",\"tool_calls\":[{\"index\":0,\"id\":\"call_mock_1\",\"type\":\"function\",\"function\":{\"name\":\"" + name + "\",\"arguments\":\"\"}}]}}]}\n\n");
        foreach (string piece in Split(argsJson, 7))
            Write(ns, "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"" + Escape(piece) + "\"}}]}}]}\n\n");
        Write(ns, "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"tool_calls\"}]}\n\n");
    }

    /// <summary>模拟某些网关：后续分片补 id:"" / name:""，真实值只在第一片。</summary>
    private static void EmitToolCallEmptyName(NetworkStream ns, string name, string argsJson)
    {
        Write(ns, "data: {\"choices\":[{\"delta\":{\"role\":\"assistant\",\"tool_calls\":[{\"index\":0,\"id\":\"call_mock_empty\",\"type\":\"function\",\"function\":{\"name\":\"" + name + "\",\"arguments\":\"\"}}]}}]}\n\n");
        foreach (string piece in Split(argsJson, 5))
            Write(ns, "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"\",\"type\":\"function\",\"function\":{\"name\":\"\",\"arguments\":\"" + Escape(piece) + "\"}}]}}]}\n\n");
        Write(ns, "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"tool_calls\"}]}\n\n");
    }

    private static System.Collections.Generic.List<string> Split(string s, int size)
    {
        System.Collections.Generic.List<string> list = new System.Collections.Generic.List<string>();
        for (int i = 0; i < s.Length; i += size)
            list.Add(s.Substring(i, Math.Min(size, s.Length - i)));
        return list;
    }

    private static string ReadHeader(NetworkStream ns)
    {
        MemoryStream ms = new MemoryStream();
        byte[] one = new byte[1];
        int matched = 0;
        while (true)
        {
            int n = ns.Read(one, 0, 1);
            if (n <= 0) break;
            ms.WriteByte(one[0]);
            if ((matched == 0 || matched == 2) && one[0] == 13) matched++;
            else if ((matched == 1 || matched == 3) && one[0] == 10) matched++;
            else matched = 0;
            if (matched == 4) break;
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void Write(NetworkStream ns, string s)
    {
        byte[] b = Encoding.UTF8.GetBytes(s);
        ns.Write(b, 0, b.Length);
        ns.Flush();
    }

    private static void Log(string path, string line)
    {
        try { File.AppendAllText(path, line + "\r\n", Encoding.UTF8); } catch { }
    }

    private static string Escape(string s)
    {
        StringBuilder sb = new StringBuilder();
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
