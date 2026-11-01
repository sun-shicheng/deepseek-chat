using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace DsChat
{
    public class ToolCall
    {
        public string Id = "";
        public string Name = "";
        public string Arguments = "";
    }

    public class ChatMsg
    {
        public string Role;
        public string Content;
        /// <summary>role=tool 时对应的 tool_call_id</summary>
        public string ToolCallId;
        /// <summary>role=assistant 且模型要求调用工具时，这里非空</summary>
        public List<ToolCall> ToolCalls;
        /// <summary>思考模型的 reasoning_content；工具调用回放时需要带回去</summary>
        public string ReasoningContent;

        public ChatMsg(string role, string content) { Role = role; Content = content; }
    }

    /// <summary>直连代理：等价于 Python httpx 的 trust_env=False，忽略系统/环境变量里的代理设置。</summary>
    public sealed class DirectProxy : IWebProxy
    {
        public ICredentials Credentials { get; set; }
        public Uri GetProxy(Uri destination) { return destination; }
        public bool IsBypassed(Uri host) { return true; }
    }

    /// <summary>OpenAI 兼容协议的流式客户端（SSE），支持代理与自签名证书。</summary>
    public class ChatClient
    {
        private Config cfg;
        private volatile HttpWebRequest request;
        private volatile bool cancelled;

        public event Action<string> OnDelta;
        public event Action<string> OnError;
        public event Action OnDone;

        /// <summary>本轮模型要求调用的工具（流式/非流式都会累积到这里）</summary>
        public List<ToolCall> ToolCalls = new List<ToolCall>();
        /// <summary>本轮模型返回的 reasoning_content（如果有）</summary>
        public StringBuilder Reasoning = new StringBuilder();
        public string ReasoningContent { get { return Reasoning.ToString(); } }

        public ChatClient(Config c) { cfg = c.Clone(); }

        public void Cancel()
        {
            cancelled = true;
            try { if (request != null) request.Abort(); } catch { }
        }

        public void Send(List<ChatMsg> messages)
        {
            cancelled = false;
            ToolCalls.Clear();
            Reasoning.Length = 0;
            Thread t = new Thread(delegate ()
            {
                try { Run(messages); }
                catch (WebException wex)
                {
                    if (cancelled) { FireDone(); return; }
                    string msg = wex.Message;
                    if (wex.Response != null)
                    {
                        try
                        {
                            using (Stream s = wex.Response.GetResponseStream())
                            using (StreamReader sr = new StreamReader(s, Encoding.UTF8))
                                msg = sr.ReadToEnd();
                        }
                        catch { }
                    }
                    FireError(msg);
                }
                catch (Exception ex)
                {
                    if (cancelled) { FireDone(); return; }
                    FireError(ex.Message);
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void Run(List<ChatMsg> messages)
        {
            if (cancelled) { FireDone(); return; }
            string url = BuildUrl();
            HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
            request = r;
            if (cancelled) { try { r.Abort(); } catch { } FireDone(); return; }
            r.Method = "POST";
            r.ContentType = "application/json; charset=utf-8";
            r.Accept = cfg.Stream ? "text/event-stream" : "application/json";
            r.Headers["Authorization"] = "Bearer " + cfg.ApiKey;
            r.KeepAlive = false;
            r.Timeout = Math.Max(10, cfg.TimeoutSeconds) * 1000;
            r.ReadWriteTimeout = Math.Max(10, cfg.TimeoutSeconds) * 1000;
            IWebProxy proxy = BuildProxy();
            if (proxy != null) r.Proxy = proxy;   // null = 保持系统默认（使用 IE/系统代理）
            try { ServicePointManager.Expect100Continue = false; } catch { }
            if (cfg.IgnoreCert)
                ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
            else
                ServicePointManager.ServerCertificateValidationCallback = null;
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }

            byte[] body = Encoding.UTF8.GetBytes(BuildBody(messages, cfg.Stream));
            r.ContentLength = body.Length;
            using (Stream rs = r.GetRequestStream()) rs.Write(body, 0, body.Length);

            using (HttpWebResponse resp = (HttpWebResponse)r.GetResponse())
            using (Stream stream = resp.GetResponseStream())
            using (StreamReader sr = new StreamReader(stream, Encoding.UTF8))
            {
                string ct = resp.ContentType == null ? "" : resp.ContentType.ToLowerInvariant();
                bool sse = ct.Contains("event-stream");

                if (!sse)
                {
                    string all = sr.ReadToEnd();
                    ExtractFullReasoning(all, Reasoning);
                    string text = ExtractFull(all);
                    ExtractFullToolCalls(all, ToolCalls);
                    if (text == null) text = ExtractDelta(all);
                    if (text == null && ToolCalls.Count == 0)
                    {
                        FireError(all.Length > 800 ? all.Substring(0, 800) : all);
                        return;
                    }
                    if (text != null && text.Length > 0) FireDelta(text);
                    FireDone();
                    return;
                }

                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    if (cancelled) break;
                    if (line.Length == 0) continue;
                    if (!line.StartsWith("data:")) continue;
                    string payload = line.Substring(5).Trim();
                    if (payload.Length == 0) continue;
                    if (payload == "[DONE]") break;
                    ExtractDeltaToolCalls(payload, ToolCalls);
                    string think = ExtractDeltaReasoning(payload);
                    if (!string.IsNullOrEmpty(think)) Reasoning.Append(think);
                    string piece = ExtractDelta(payload);
                    if (piece != null && piece.Length > 0) FireDelta(piece);
                }
            }
            FireDone();
        }

        private string BuildUrl()
        {
            string p = cfg.ApiPath == null ? "" : cfg.ApiPath.Trim();
            if (p.StartsWith("http://") || p.StartsWith("https://")) return p;
            string b = (cfg.BaseUrl == null ? "" : cfg.BaseUrl.Trim()).TrimEnd('/');
            if (!p.StartsWith("/")) p = "/" + p;
            return b + p;
        }

        /// <summary>
        /// 启动时的轻量自检：请求 /v1/models 判断接口/Key/网络是否可用，不消耗对话 token。
        /// 返回：就绪 / Key 无效 / 连接失败 / 未配置接口 / 未配置 Key。
        /// </summary>
        public static string CheckConnection(Config c)
        {
            if (c == null || string.IsNullOrEmpty(c.BaseUrl)) return "未配置接口";
            if (string.IsNullOrEmpty(c.ApiKey)) return "未配置 Key";

            try
            {
                string url = BuildModelsUrl(c);
                if (url.Length == 0) return "未配置接口";

                HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
                r.Method = "GET";
                r.Accept = "application/json";
                r.Headers["Authorization"] = "Bearer " + (c.ApiKey == null ? "" : c.ApiKey);
                r.KeepAlive = false;
                r.Timeout = 8000;
                r.ReadWriteTimeout = 8000;

                ChatClient probe = new ChatClient(c);
                IWebProxy proxy = probe.BuildProxy();
                if (proxy != null) r.Proxy = proxy;
                if (c.IgnoreCert)
                    ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
                try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
                try { ServicePointManager.Expect100Continue = false; } catch { }

                using (HttpWebResponse resp = (HttpWebResponse)r.GetResponse())
                    return "就绪";
            }
            catch (WebException wex)
            {
                HttpWebResponse resp = wex.Response as HttpWebResponse;
                if (resp != null)
                {
                    try
                    {
                        int code = (int)resp.StatusCode;
                        // 有些网关不提供 /v1/models 或不允许列模型，但对话接口本身可用
                        if (code == 400 || code == 403 || code == 404 || code == 405) return "就绪";
                        if (code == 401) return "Key 无效";
                    }
                    catch { }
                    finally { try { resp.Close(); } catch { } }
                }
                return "连接失败";
            }
            catch { return "连接失败"; }
        }

        private static string BuildModelsUrl(Config c)
        {
            string path = c.ApiPath == null ? "" : c.ApiPath.Trim();
            if (path.StartsWith("http://") || path.StartsWith("https://"))
            {
                int idx = path.LastIndexOf('/');
                if (idx > 0) return path.Substring(0, idx) + "/models";
            }
            string b = (c.BaseUrl == null ? "" : c.BaseUrl.Trim()).TrimEnd('/');
            if (b.Length == 0) return "";
            if (b.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) return b + "/models";
            return b + "/v1/models";
        }

        private IWebProxy BuildProxy()
        {
            if (string.IsNullOrEmpty(cfg.Proxy))
            {
                // TrustEnv=false 时强制直连，忽略 IE/系统代理与环境变量代理（等价 trust_env=False）；
                // TrustEnv=true 返回 null，交给 HttpWebRequest 使用系统默认代理。
                return cfg.TrustEnv ? null : (IWebProxy)new DirectProxy();
            }
            string hp = cfg.Proxy.Trim();
            if (hp.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) hp = hp.Substring(7);
            else if (hp.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) hp = hp.Substring(8);
            string host = hp;
            int port = 8080;
            int c = hp.LastIndexOf(':');
            if (c > 0)
            {
                host = hp.Substring(0, c);
                int.TryParse(hp.Substring(c + 1), out port);
            }
            WebProxy wp = new WebProxy(host, port);
            wp.BypassProxyOnLocal = false;
            if (!string.IsNullOrEmpty(cfg.ProxyUser))
                wp.Credentials = new NetworkCredential(cfg.ProxyUser, cfg.ProxyPass);
            return wp;
        }

        /// <summary>
        /// 防御性清理：确保每个 tool 消息都紧跟在发起它的 assistant.tool_calls 后面，
        /// 缺失的工具结果补占位，避免网关报 “role 'tool' must be ... tool_calls”。
        /// </summary>
        private static List<ChatMsg> SanitizeMessages(List<ChatMsg> msgs)
        {
            List<ChatMsg> list = new List<ChatMsg>();
            foreach (ChatMsg m in msgs)
            {
                if (m == null) continue;
                if (m.Role == "tool")
                {
                    int j = list.Count - 1;
                    while (j >= 0 && list[j].Role == "tool") j--;
                    if (j < 0) continue;
                    ChatMsg a = list[j];
                    if (a.Role != "assistant" || a.ToolCalls == null) continue;

                    bool match = false;
                    foreach (ToolCall tc in a.ToolCalls)
                        if (tc != null && tc.Id == m.ToolCallId) { match = true; break; }
                    if (!match) continue;

                    bool dup = false;
                    for (int k = j + 1; k < list.Count; k++)
                        if (list[k].Role == "tool" && list[k].ToolCallId == m.ToolCallId) { dup = true; break; }
                    if (dup) continue;

                    list.Add(m);
                }
                else
                {
                    list.Add(m);
                }
            }

            List<ChatMsg> result = new List<ChatMsg>();
            for (int i = 0; i < list.Count; i++)
            {
                ChatMsg m = list[i];
                result.Add(m);
                if (m.Role != "assistant" || m.ToolCalls == null || m.ToolCalls.Count == 0) continue;

                int end = i + 1;
                while (end < list.Count && list[end].Role == "tool") end++;

                foreach (ToolCall tc in m.ToolCalls) {
                    if (tc == null) continue;
                    bool found = false;
                    for (int k = i + 1; k < end; k++)
                        if (list[k].ToolCallId == tc.Id) { found = true; break; }
                    if (!found)
                    {
                        ChatMsg missing = new ChatMsg("tool", "（工具未执行）");
                        missing.ToolCallId = tc.Id == null ? "" : tc.Id;
                        result.Add(missing);
                    }
                }
            }
            return result;
        }

        /// <summary>“简洁回答”追加在系统提示词末尾，减少输出 token。</summary>
        public const string ConciseSuffix = "回答简洁：先给结论，省略寒暄、重复和无关细节。";
        /// <summary>开启文件工具时强调“先调用工具、再汇报结果”，避免模型只说已写入但没调用。</summary>
        public const string FileToolRule = "涉及读取、写入、创建、修改、删除文件时，必须先实际调用工具；不要在未调用工具时声称已写入、已保存或已完成。写入后以工具结果为准。";

        public static string ComposeSystemPrompt(Config c)
        {
            string sys = c == null || c.SystemPrompt == null ? "" : c.SystemPrompt;
            if (c != null && c.Concise)
                sys = sys.Length == 0 ? ConciseSuffix : sys + "\n" + ConciseSuffix;
            if (c != null && c.EnableTools && Workspace.IsReady(c.Workspace))
                sys = sys.Length == 0 ? FileToolRule : sys + "\n" + FileToolRule;
            return sys;
        }

        private string BuildBody(List<ChatMsg> msgs, bool stream)
        {
            msgs = SanitizeMessages(msgs);
            bool useTools = cfg.EnableTools && Workspace.IsReady(cfg.Workspace);
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"model\":\"").Append(Json.Escape(cfg.Model)).Append("\",\"messages\":[");
            bool first = true;
            string sys = ComposeSystemPrompt(cfg);
            if (sys.Length > 0)
            {
                sb.Append("{\"role\":\"system\",\"content\":\"").Append(Json.Escape(sys)).Append("\"}");
                first = false;
            }
            foreach (ChatMsg m in msgs)
            {
                string part = MsgJson(m, useTools);
                if (part == null) continue;
                if (!first) sb.Append(',');
                sb.Append(part);
                first = false;
            }
            sb.Append("],\"stream\":").Append(stream ? "true" : "false");
            sb.Append(",\"temperature\":").Append(cfg.Temperature.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (cfg.MaxTokens > 0) sb.Append(",\"max_tokens\":").Append(cfg.MaxTokens);
            if (useTools)
                sb.Append(",\"tool_choice\":\"auto\",\"tools\":").Append(ToolsJson());
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>
        /// 序列化单条消息，支持 tool 角色与 assistant.tool_calls。
        /// sendReasoning：只有请求携带 tools 时才回传 reasoning_content（思考模式 + 工具调用的协议要求）；
        /// 不带 tools 时 API 会忽略它，回传只会白花输入 token。
        /// </summary>
        private static string MsgJson(ChatMsg m, bool sendReasoning)
        {
            if (m == null) return null;
            if (m.Role == "tool")
            {
                return "{\"role\":\"tool\",\"tool_call_id\":\"" + Json.Escape(m.ToolCallId == null ? "" : m.ToolCallId)
                     + "\",\"content\":\"" + Json.Escape(m.Content == null ? "" : m.Content) + "\"}";
            }
            bool hasCalls = m.ToolCalls != null && m.ToolCalls.Count > 0;
            if (!hasCalls && (m.Content == null || m.Content.Length == 0)) return null;

            StringBuilder sb = new StringBuilder();
            sb.Append("{\"role\":\"").Append(Json.Escape(m.Role)).Append("\",\"content\":");
            if (m.Content == null || m.Content.Length == 0) sb.Append("\"\"");
            else sb.Append('"').Append(Json.Escape(m.Content)).Append('"');
            if (sendReasoning && m.Role == "assistant" && !string.IsNullOrEmpty(m.ReasoningContent))
                sb.Append(",\"reasoning_content\":\"").Append(Json.Escape(m.ReasoningContent)).Append('"');
            if (hasCalls)
            {
                sb.Append(",\"tool_calls\":[");
                for (int i = 0; i < m.ToolCalls.Count; i++)
                {
                    ToolCall tc = m.ToolCalls[i];
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"id\":\"").Append(Json.Escape(tc.Id == null ? "" : tc.Id))
                      .Append("\",\"type\":\"function\",\"function\":{\"name\":\"").Append(Json.Escape(tc.Name == null ? "" : tc.Name))
                      .Append("\",\"arguments\":\"").Append(Json.Escape(tc.Arguments == null ? "" : tc.Arguments)).Append("\"}}");
                }
                sb.Append(']');
            }
            sb.Append('}');
            return sb.ToString();
        }

        private static string ToolsJson()
        {
            StringBuilder sb = new StringBuilder("[");
            sb.Append(ToolJson("list_files", "列出工作区文件和目录（相对路径）",
                "{\"type\":\"object\",\"properties\":{\"subdir\":{\"type\":\"string\",\"description\":\"子目录，留空为根目录\"}},\"required\":[]}"));
            sb.Append(',');
            sb.Append(ToolJson("read_file", "读取工作区文件：文本（.py/.c/.txt 等）或 .xlsx/.docx；分页",
                "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"相对工作区的文件路径\"},\"start_line\":{\"type\":\"integer\",\"description\":\"起始行，默认 1\"},\"max_lines\":{\"type\":\"integer\",\"description\":\"最多读取行数，默认 200，最大 400\"}},\"required\":[\"path\"]}"));
            sb.Append(',');
            sb.Append(ToolJson("write_file", "写入文件（覆盖），目录自动创建；文本原样写入，.xlsx 用 CSV/TSV，.docx 用纯文本段落",
                "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"相对工作区的文件路径（可含 .xlsx/.docx/.py/.c 等扩展名）\"},\"content\":{\"type\":\"string\",\"description\":\"完整文本内容；.xlsx 用 CSV/TSV/ Markdown 表格；.docx 用纯文本段落\"}},\"required\":[\"path\",\"content\"]}"));
            sb.Append(',');
            sb.Append(ToolJson("delete_file", "删除工作区文件或空目录",
                "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"相对工作区的路径\"}},\"required\":[\"path\"]}"));
            sb.Append(']');
            return sb.ToString();
        }

        private static string ToolJson(string name, string desc, string paramsJson)
        {
            return "{\"type\":\"function\",\"function\":{\"name\":\"" + name + "\",\"description\":\"" + Json.Escape(desc)
                 + "\",\"parameters\":" + paramsJson + "}}";
        }

        // ---------- 解析 ----------
        private static string ExtractDelta(string json)
        {
            try
            {
                JavaScriptSerializer js = new JavaScriptSerializer();
                Dictionary<string, object> root = js.DeserializeObject(json) as Dictionary<string, object>;
                if (root == null) return null;
                object choicesObj;
                if (!root.TryGetValue("choices", out choicesObj)) return null;
                object[] choices = choicesObj as object[];
                if (choices == null || choices.Length == 0) return null;
                Dictionary<string, object> c0 = choices[0] as Dictionary<string, object>;
                if (c0 == null) return null;
                object deltaObj;
                if (c0.TryGetValue("delta", out deltaObj))
                {
                    Dictionary<string, object> d = deltaObj as Dictionary<string, object>;
                    if (d != null)
                    {
                        object content;
                        if (d.TryGetValue("content", out content) && content is string) return (string)content;
                    }
                }
                return null;
            }
            catch { return null; }
        }

        private static string ExtractFull(string json)
        {
            try
            {
                JavaScriptSerializer js = new JavaScriptSerializer();
                Dictionary<string, object> root = js.DeserializeObject(json) as Dictionary<string, object>;
                if (root == null) return null;
                object choicesObj;
                if (!root.TryGetValue("choices", out choicesObj)) return null;
                object[] choices = choicesObj as object[];
                if (choices == null || choices.Length == 0) return null;
                Dictionary<string, object> c0 = choices[0] as Dictionary<string, object>;
                if (c0 == null) return null;
                object msgObj;
                if (c0.TryGetValue("message", out msgObj))
                {
                    Dictionary<string, object> m = msgObj as Dictionary<string, object>;
                    if (m != null)
                    {
                        object content;
                        if (m.TryGetValue("content", out content) && content is string) return (string)content;
                    }
                }
                return null;
            }
            catch { return null; }
        }

        private static Dictionary<string, object> FirstChoice(string json)
        {
            try
            {
                JavaScriptSerializer js = new JavaScriptSerializer();
                Dictionary<string, object> root = js.DeserializeObject(json) as Dictionary<string, object>;
                if (root == null) return null;
                object choicesObj;
                if (!root.TryGetValue("choices", out choicesObj)) return null;
                object[] choices = choicesObj as object[];
                if (choices == null || choices.Length == 0) return null;
                return choices[0] as Dictionary<string, object>;
            }
            catch { return null; }
        }

        private static string ExtractDeltaReasoning(string json)
        {
            Dictionary<string, object> c0 = FirstChoice(json);
            if (c0 == null) return null;
            object deltaObj;
            if (!c0.TryGetValue("delta", out deltaObj)) return null;
            Dictionary<string, object> d = deltaObj as Dictionary<string, object>;
            if (d == null) return null;
            object v;
            if (d.TryGetValue("reasoning_content", out v) && v is string) return (string)v;
            if (d.TryGetValue("reasoning", out v) && v is string) return (string)v;
            return null;
        }

        private static void ExtractFullReasoning(string json, StringBuilder sb)
        {
            Dictionary<string, object> c0 = FirstChoice(json);
            if (c0 == null) return;
            object msgObj;
            if (!c0.TryGetValue("message", out msgObj)) return;
            Dictionary<string, object> m = msgObj as Dictionary<string, object>;
            if (m == null) return;
            object v;
            if (m.TryGetValue("reasoning_content", out v) && v is string) sb.Append((string)v);
            else if (m.TryGetValue("reasoning", out v) && v is string) sb.Append((string)v);
        }

        private static void ExtractDeltaToolCalls(string json, List<ToolCall> acc)
        {
            Dictionary<string, object> c0 = FirstChoice(json);
            if (c0 == null) return;
            object deltaObj;
            if (!c0.TryGetValue("delta", out deltaObj)) return;
            Dictionary<string, object> d = deltaObj as Dictionary<string, object>;
            if (d == null) return;
            object tcObj;
            if (!d.TryGetValue("tool_calls", out tcObj)) return;
            ParseToolCalls(tcObj, acc, true);
        }

        private static void ExtractFullToolCalls(string json, List<ToolCall> acc)
        {
            Dictionary<string, object> c0 = FirstChoice(json);
            if (c0 == null) return;
            object msgObj;
            if (!c0.TryGetValue("message", out msgObj)) return;
            Dictionary<string, object> m = msgObj as Dictionary<string, object>;
            if (m == null) return;
            object tcObj;
            if (!m.TryGetValue("tool_calls", out tcObj)) return;
            ParseToolCalls(tcObj, acc, false);
        }

        /// <summary>流式按 index 拼接 arguments，非流式整体读取。</summary>
        private static void ParseToolCalls(object raw, List<ToolCall> acc, bool streamed)
        {
            object[] arr = raw as object[];
            if (arr == null) return;
            foreach (object o in arr)
            {
                Dictionary<string, object> d = o as Dictionary<string, object>;
                if (d == null) continue;

                int index;
                if (streamed)
                {
                    index = acc.Count <= 1 ? 0 : acc.Count;
                    object idxObj;
                    if (d.TryGetValue("index", out idxObj) && idxObj != null)
                    {
                        try { index = Convert.ToInt32(idxObj); } catch { index = acc.Count <= 1 ? 0 : acc.Count; }
                    }
                    if (index < 0) index = 0;
                    while (acc.Count <= index) acc.Add(new ToolCall());
                }
                else
                {
                    acc.Add(new ToolCall());
                    index = acc.Count - 1;
                }

                ToolCall tc = acc[index];
                object idObj;
                if (d.TryGetValue("id", out idObj) && idObj is string && ((string)idObj).Length > 0) tc.Id = (string)idObj;
                object fnObj;
                if (d.TryGetValue("function", out fnObj))
                {
                    Dictionary<string, object> fn = fnObj as Dictionary<string, object>;
                    if (fn != null)
                    {
                        object nObj;
                        if (fn.TryGetValue("name", out nObj) && nObj is string)
                        {
                            // 部分网关会在后续分片里补发空字符串，不能覆盖第一片的真实函数名
                            string nm = ((string)nObj).Trim();
                            if (nm.Length > 0) tc.Name = nm;
                        }
                        object aObj;
                        if (fn.TryGetValue("arguments", out aObj) && aObj is string)
                        {
                            if (streamed) tc.Arguments = (tc.Arguments == null ? "" : tc.Arguments) + (string)aObj;
                            else tc.Arguments = (string)aObj;
                        }
                    }
                }
                if (tc.Id == null || tc.Id.Length == 0) tc.Id = "call_" + index;
            }
        }

        /// <summary>清理无效/重复的工具调用，保证 assistant.tool_calls 与 tool 结果能一一对应。</summary>
        private void NormalizeToolCalls()
        {
            if (ToolCalls.Count == 0) return;
            HashSet<string> seen = new HashSet<string>();
            for (int i = ToolCalls.Count - 1; i >= 0; i--)
            {
                ToolCall tc = ToolCalls[i];
                if (tc == null) { ToolCalls.RemoveAt(i); continue; }
                string name = tc.Name == null ? "" : tc.Name.Trim();
                if (name.Length == 0) { ToolCalls.RemoveAt(i); continue; }
                tc.Name = name;

                if (tc.Id == null || tc.Id.Length == 0) tc.Id = "call_" + i;
                string baseId = tc.Id;
                string id = baseId;
                int n = 1;
                while (seen.Contains(id)) { id = baseId + "_" + n; n++; }
                tc.Id = id;
                seen.Add(id);
            }
        }

        private void FireDelta(string s) { if (OnDelta != null) OnDelta(s); }
        private void FireError(string s) { if (OnError != null) OnError(s); }
        private void FireDone() { NormalizeToolCalls(); if (OnDone != null) OnDone(); }
    }

    public static class Json
    {
        public static string Escape(string s)
        {
            if (s == null) return "";
            StringBuilder sb = new StringBuilder(s.Length + 8);
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (ch < ' ') sb.Append("\\u").Append(((int)ch).ToString("x4"));
                        else sb.Append(ch);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
