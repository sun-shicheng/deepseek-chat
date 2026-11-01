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
    public class ChatMsg
    {
        public string Role;
        public string Content;
        public ChatMsg(string role, string content) { Role = role; Content = content; }
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

        public ChatClient(Config c) { cfg = c.Clone(); }

        public void Cancel()
        {
            cancelled = true;
            try { if (request != null) request.Abort(); } catch { }
        }

        public void Send(List<ChatMsg> messages)
        {
            cancelled = false;
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
                    if (text == null) text = ExtractDelta(all);
                    if (text == null)
                    {
                        FireError(all.Length > 800 ? all.Substring(0, 800) : all);
                        return;
                    }
                    if (text.Length > 0) FireDelta(text);
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
            if (string.IsNullOrEmpty(cfg.Proxy)) return null;
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
                if (m.Content == null || m.Content.Length == 0) continue;
                if (!first) sb.Append(',');
                sb.Append("{\"role\":\"").Append(Json.Escape(m.Role)).Append("\",\"content\":\"").Append(Json.Escape(m.Content)).Append("\"}");
                first = false;
            }
            sb.Append("],\"stream\":").Append(stream ? "true" : "false");
            sb.Append(",\"temperature\":").Append(cfg.Temperature.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (cfg.MaxTokens > 0) sb.Append(",\"max_tokens\":").Append(cfg.MaxTokens);
            sb.Append('}');
            return sb.ToString();
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
