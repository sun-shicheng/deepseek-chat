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
        private HttpWebRequest request;
        private volatile bool cancelled;

        public event Action<string> OnDelta;
        public event Action<string> OnError;
        public event Action OnDone;

        /// <summary>本轮模型要求调用的工具（流式/非流式都会累积到这里）</summary>
        public List<ToolCall> ToolCalls = new List<ToolCall>();

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
            string url = BuildUrl();
            HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
            request = r;
            r.Method = "POST";
            r.ContentType = "application/json; charset=utf-8";
            r.Accept = cfg.Stream ? "text/event-stream" : "application/json";
            r.Headers["Authorization"] = "Bearer " + cfg.ApiKey;
            r.KeepAlive = false;
            r.Timeout = Math.Max(10, cfg.TimeoutSeconds) * 1000;
            r.ReadWriteTimeout = Math.Max(10, cfg.TimeoutSeconds) * 1000;
            r.Proxy = BuildProxy();
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

        private IWebProxy BuildProxy()
        {
            if (string.IsNullOrEmpty(cfg.Proxy))
            {
                // TrustEnv=false 时强制直连，忽略 IE/系统代理与环境变量代理（等价 trust_env=False）
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

        private string BuildBody(List<ChatMsg> msgs, bool stream)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"model\":\"").Append(Json.Escape(cfg.Model)).Append("\",\"messages\":[");
            bool first = true;
            if (!string.IsNullOrEmpty(cfg.SystemPrompt))
            {
                sb.Append("{\"role\":\"system\",\"content\":\"").Append(Json.Escape(cfg.SystemPrompt)).Append("\"}");
                first = false;
            }
            foreach (ChatMsg m in msgs)
            {
                string part = MsgJson(m);
                if (part == null) continue;
                if (!first) sb.Append(',');
                sb.Append(part);
                first = false;
            }
            sb.Append("],\"stream\":").Append(stream ? "true" : "false");
            sb.Append(",\"temperature\":").Append(cfg.Temperature.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (cfg.MaxTokens > 0) sb.Append(",\"max_tokens\":").Append(cfg.MaxTokens);
            if (cfg.EnableTools && Workspace.IsReady(cfg.Workspace))
                sb.Append(",\"tool_choice\":\"auto\",\"tools\":").Append(ToolsJson());
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>序列化单条消息，支持 tool 角色与 assistant.tool_calls。</summary>
        private static string MsgJson(ChatMsg m)
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
            if (m.Content == null || m.Content.Length == 0) sb.Append("null");
            else sb.Append('"').Append(Json.Escape(m.Content)).Append('"');
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
            sb.Append(ToolJson("list_files", "列出工作区里的文件与子目录（相对路径）",
                "{\"type\":\"object\",\"properties\":{\"subdir\":{\"type\":\"string\",\"description\":\"相对工作区的子目录，留空表示根目录\"}},\"required\":[]}"));
            sb.Append(',');
            sb.Append(ToolJson("read_file", "读取工作区里的文本文件内容",
                "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"相对工作区的文件路径\"}},\"required\":[\"path\"]}"));
            sb.Append(',');
            sb.Append(ToolJson("write_file", "把文本写入工作区文件，覆盖同名文件，目录不存在会自动创建",
                "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"相对工作区的文件路径\"},\"content\":{\"type\":\"string\",\"description\":\"完整文本内容\"}},\"required\":[\"path\",\"content\"]}"));
            sb.Append(',');
            sb.Append(ToolJson("delete_file", "删除工作区里的文件或空目录",
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
                    index = acc.Count;
                    object idxObj;
                    if (d.TryGetValue("index", out idxObj) && idxObj != null)
                    {
                        try { index = Convert.ToInt32(idxObj); } catch { index = acc.Count; }
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
                        if (fn.TryGetValue("name", out nObj) && nObj is string) tc.Name = (string)nObj;
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

        private void FireDelta(string s) { if (OnDelta != null) OnDelta(s); }
        private void FireError(string s) { if (OnError != null) OnError(s); }
        private void FireDone() { if (OnDone != null) OnDone(); }
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
