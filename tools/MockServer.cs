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
