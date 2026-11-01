using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DsChat
{
    /// <summary>配置项：接口地址 / 路径 / Key / 模型 / 代理 / 主题等，保存到 %APPDATA%\DeepSeekDesktop\config.ini</summary>
    public class Config
    {
        public string BaseUrl = "https://api.deepseek.com";
        public string ApiPath = "/v1/chat/completions";
        public string ApiKey = "";
        public string Model = "deepseek-chat";
        public string Proxy = "";
        public string ProxyUser = "";
        public string ProxyPass = "";
        public string SystemPrompt = "你是一个乐于助人的 AI 助手，请用中文回答。";
        public bool Stream = true;
        public bool Dark = true;
        public bool IgnoreCert = false;
        public int TimeoutSeconds = 120;
        public double Temperature = 0.7;
        public int MaxTokens = 0;

        public static string Dir
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeepSeekDesktop");
                if (!Directory.Exists(d)) Directory.CreateDirectory(d);
                return d;
            }
        }

        public static string FilePath { get { return Path.Combine(Dir, "config.ini"); } }
        public static string SessionPath { get { return Path.Combine(Dir, "sessions.dat"); } }

        public Config Clone()
        {
            Config c = new Config();
            c.BaseUrl = BaseUrl; c.ApiPath = ApiPath; c.ApiKey = ApiKey; c.Model = Model;
            c.Proxy = Proxy; c.ProxyUser = ProxyUser; c.ProxyPass = ProxyPass;
            c.SystemPrompt = SystemPrompt; c.Stream = Stream; c.Dark = Dark; c.IgnoreCert = IgnoreCert;
            c.TimeoutSeconds = TimeoutSeconds; c.Temperature = Temperature; c.MaxTokens = MaxTokens;
            return c;
        }

        public static Config Load()
        {
            Config c = new Config();
            try
            {
                if (!File.Exists(FilePath)) return c;
                string[] lines = File.ReadAllLines(FilePath, Encoding.UTF8);
                foreach (string raw in lines)
                {
                    string line = raw;
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int idx = line.IndexOf('=');
                    if (idx <= 0) continue;
                    string k = line.Substring(0, idx).Trim();
                    string v = Unescape(line.Substring(idx + 1));
                    switch (k)
                    {
                        case "BaseUrl": c.BaseUrl = v; break;
                        case "ApiPath": c.ApiPath = v; break;
                        case "ApiKeyEnc": c.ApiKey = Unprotect(v); break;
                        case "Model": c.Model = v; break;
                        case "Proxy": c.Proxy = v; break;
                        case "ProxyUser": c.ProxyUser = v; break;
                        case "ProxyPassEnc": c.ProxyPass = Unprotect(v); break;
                        case "SystemPrompt": c.SystemPrompt = v; break;
                        case "Stream": c.Stream = v == "1"; break;
                        case "Dark": c.Dark = v == "1"; break;
                        case "IgnoreCert": c.IgnoreCert = v == "1"; break;
                        case "TimeoutSeconds": c.TimeoutSeconds = ParseInt(v, 120); break;
                        case "Temperature": c.Temperature = ParseDouble(v, 0.7); break;
                        case "MaxTokens": c.MaxTokens = ParseInt(v, 0); break;
                    }
                }
            }
            catch { }
            return c;
        }

        public void Save()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# DeepSeek Desktop 配置（API Key 使用当前用户 DPAPI 加密）");
                sb.AppendLine("BaseUrl=" + Escape(BaseUrl));
                sb.AppendLine("ApiPath=" + Escape(ApiPath));
                sb.AppendLine("ApiKeyEnc=" + Protect(ApiKey));
                sb.AppendLine("Model=" + Escape(Model));
                sb.AppendLine("Proxy=" + Escape(Proxy));
                sb.AppendLine("ProxyUser=" + Escape(ProxyUser));
                sb.AppendLine("ProxyPassEnc=" + Protect(ProxyPass));
                sb.AppendLine("SystemPrompt=" + Escape(SystemPrompt));
                sb.AppendLine("Stream=" + (Stream ? "1" : "0"));
                sb.AppendLine("Dark=" + (Dark ? "1" : "0"));
                sb.AppendLine("IgnoreCert=" + (IgnoreCert ? "1" : "0"));
                sb.AppendLine("TimeoutSeconds=" + TimeoutSeconds);
                sb.AppendLine("Temperature=" + Temperature.ToString(System.Globalization.CultureInfo.InvariantCulture));
                sb.AppendLine("MaxTokens=" + MaxTokens);
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        // ---------- 工具 ----------
        private static int ParseInt(string s, int def)
        {
            int r; if (int.TryParse(s, out r)) return r; return def;
        }
        private static double ParseDouble(string s, double def)
        {
            double r;
            if (double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out r)) return r;
            return def;
        }
        private static string Escape(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\r", "").Replace("\n", "\\n");
        }
        private static string Unescape(string s)
        {
            if (s == null) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char n = s[i + 1];
                    if (n == 'n') { sb.Append('\n'); i++; continue; }
                    if (n == '\\') { sb.Append('\\'); i++; continue; }
                }
                sb.Append(s[i]);
            }
            return sb.ToString();
        }
        private static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(plain);
                byte[] enc = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(enc);
            }
            catch { return ""; }
        }
        private static string Unprotect(string b64)
        {
            if (string.IsNullOrEmpty(b64)) return "";
            try
            {
                byte[] enc = Convert.FromBase64String(b64);
                byte[] data = ProtectedData.Unprotect(enc, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch { return ""; }
        }
    }
}
