using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DsChat
{
    /// <summary>配置项：接口地址 / 路径 / Key / 模型 / 代理 / 主题等，保存到 %APPDATA%\Lumi\config.ini</summary>
    public class Config
    {
        public string BaseUrl = "";
        public string ApiPath = "/v1/chat/completions";
        public string ApiKey = "";
        public string Model = "";
        public string Proxy = "";
        public string ProxyUser = "";
        public string ProxyPass = "";
        public string SystemPrompt = "你是一个乐于助人的 AI 助手，请用中文回答。";
        public bool Stream = true;
        /// <summary>兼容旧版：true = 深色。新配置请使用 ThemeMode。</summary>
        public bool Dark = true;
        /// <summary>0 = 跟随系统，1 = 浅色，2 = 深色。</summary>
        public int ThemeMode = Theme.ModeSystem;
        public bool IgnoreCert = false;
        public int TimeoutSeconds = 120;
        public double Temperature = 0.7;
        public int MaxTokens = 0;
        /// <summary>发给模型的上下文预算（估算 token）；0 = 不限制。默认 12000，超出后自动省略较早消息。</summary>
        public int ContextTokens = 12000;
        /// <summary>是否在系统提示词末尾追加“回答简洁”，减少输出 token。</summary>
        public bool Concise = true;

        /// <summary>true = 使用系统/环境变量代理；false 等价 Python 的 trust_env=False（直连，内网必需）</summary>
        public bool TrustEnv = false;
        /// <summary>工作区目录，AI 与文件面板只能在这个目录内操作</summary>
        public string Workspace = "";
        /// <summary>是否给模型提供工作区文件工具（OpenAI function calling）</summary>
        public bool EnableTools = false;
        /// <summary>AI 写文件/删文件前是否弹窗确认</summary>
        public bool ConfirmWrites = true;

        public static string Dir
        {
            get
            {
                string custom = Environment.GetEnvironmentVariable("LUMI_HOME");
                if (string.IsNullOrEmpty(custom))
                    custom = Environment.GetEnvironmentVariable("AGENTCHAT_HOME");
                if (string.IsNullOrEmpty(custom))
                    custom = Environment.GetEnvironmentVariable("DSCHAT_HOME");

                if (!string.IsNullOrEmpty(custom))
                {
                    if (!Directory.Exists(custom)) Directory.CreateDirectory(custom);
                    return custom;
                }

                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string d = Path.Combine(appData, "Lumi");
                if (!Directory.Exists(d))
                {
                    Directory.CreateDirectory(d);
                    MigrateLegacyData(Path.Combine(appData, "AgentChat"), d);
                    MigrateLegacyData(Path.Combine(appData, "DeepSeekDesktop"), d);
                }
                return d;
            }
        }

        /// <summary>从旧版数据目录迁移配置和会话，避免升级后丢失历史。</summary>
        private static void MigrateLegacyData(string oldDir, string newDir)
        {
            try
            {
                if (!Directory.Exists(oldDir)) return;
                foreach (string name in new string[] { "config.ini", "sessions.dat" })
                {
                    string from = Path.Combine(oldDir, name);
                    string to = Path.Combine(newDir, name);
                    if (File.Exists(from) && !File.Exists(to)) File.Copy(from, to, false);
                }
            }
            catch { }
        }

        public static string FilePath { get { return Path.Combine(Dir, "config.ini"); } }
        public static string SessionPath { get { return Path.Combine(Dir, "sessions.dat"); } }

        public Config Clone()
        {
            Config c = new Config();
            c.BaseUrl = BaseUrl; c.ApiPath = ApiPath; c.ApiKey = ApiKey; c.Model = Model;
            c.Proxy = Proxy; c.ProxyUser = ProxyUser; c.ProxyPass = ProxyPass;
            c.SystemPrompt = SystemPrompt; c.Stream = Stream; c.Dark = Dark; c.ThemeMode = ThemeMode; c.IgnoreCert = IgnoreCert;
            c.TimeoutSeconds = TimeoutSeconds; c.Temperature = Temperature; c.MaxTokens = MaxTokens;
            c.ContextTokens = ContextTokens; c.Concise = Concise;
            c.TrustEnv = TrustEnv; c.Workspace = Workspace; c.EnableTools = EnableTools; c.ConfirmWrites = ConfirmWrites;
            return c;
        }

        public static Config Load()
        {
            Config c = new Config();
            bool sawThemeMode = false;
            bool sawDark = false;
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
                        case "Dark": c.Dark = v == "1"; sawDark = true; break;
                        case "ThemeMode": c.ThemeMode = ParseInt(v, Theme.ModeSystem); sawThemeMode = true; break;
                        case "IgnoreCert": c.IgnoreCert = v == "1"; break;
                        case "TimeoutSeconds": c.TimeoutSeconds = ParseInt(v, 120); break;
                        case "Temperature": c.Temperature = ParseDouble(v, 0.7); break;
                        case "MaxTokens": c.MaxTokens = ParseInt(v, 0); break;
                        case "ContextTokens": c.ContextTokens = ParseInt(v, 12000); break;
                        case "Concise": c.Concise = v == "1"; break;
                        case "TrustEnv": c.TrustEnv = v == "1"; break;
                        case "Workspace": c.Workspace = v; break;
                        case "EnableTools": c.EnableTools = v == "1"; break;
                        case "ConfirmWrites": c.ConfirmWrites = v == "1"; break;
                    }
                }
            }
            catch { }
            if (c.ThemeMode < Theme.ModeSystem || c.ThemeMode > Theme.ModeDark) c.ThemeMode = Theme.ModeSystem;
            // 旧配置只有 Dark，没有 ThemeMode：保持用户原来的明暗选择。
            if (!sawThemeMode && sawDark) c.ThemeMode = c.Dark ? Theme.ModeDark : Theme.ModeLight;
            c.Dark = Theme.ResolveDark(c.ThemeMode);
            return c;
        }

        public void Save()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# Lumi 配置（API Key 使用当前用户 DPAPI 加密）");
                sb.AppendLine("BaseUrl=" + Escape(BaseUrl));
                sb.AppendLine("ApiPath=" + Escape(ApiPath));
                sb.AppendLine("ApiKeyEnc=" + Protect(ApiKey));
                sb.AppendLine("Model=" + Escape(Model));
                sb.AppendLine("Proxy=" + Escape(Proxy));
                sb.AppendLine("ProxyUser=" + Escape(ProxyUser));
                sb.AppendLine("ProxyPassEnc=" + Protect(ProxyPass));
                sb.AppendLine("SystemPrompt=" + Escape(SystemPrompt));
                sb.AppendLine("Stream=" + (Stream ? "1" : "0"));
                sb.AppendLine("ThemeMode=" + ThemeMode);
                sb.AppendLine("Dark=" + (Theme.ResolveDark(ThemeMode) ? "1" : "0"));
                sb.AppendLine("IgnoreCert=" + (IgnoreCert ? "1" : "0"));
                sb.AppendLine("TimeoutSeconds=" + TimeoutSeconds);
                sb.AppendLine("Temperature=" + Temperature.ToString(System.Globalization.CultureInfo.InvariantCulture));
                sb.AppendLine("MaxTokens=" + MaxTokens);
                sb.AppendLine("ContextTokens=" + ContextTokens);
                sb.AppendLine("Concise=" + (Concise ? "1" : "0"));
                sb.AppendLine("TrustEnv=" + (TrustEnv ? "1" : "0"));
                sb.AppendLine("Workspace=" + Escape(Workspace));
                sb.AppendLine("EnableTools=" + (EnableTools ? "1" : "0"));
                sb.AppendLine("ConfirmWrites=" + (ConfirmWrites ? "1" : "0"));
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
