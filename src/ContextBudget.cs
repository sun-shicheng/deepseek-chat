using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Web.Script.Serialization;

namespace DsChat
{
    /// <summary>
    /// 上下文预算：真正发给模型之前，把历史消息、工具参数、工具结果压缩到可控的 token 规模。
    /// 只影响请求内容，不改动会话里保存的原文，也不影响界面上显示的内容。
    /// </summary>
    public static class ContextBudget
    {
        /// <summary>默认输入预算（估算 token）。0 表示不做总量限制。</summary>
        public const int DefaultTokens = 12000;
        /// <summary>普通消息（用户 / 助手）单条最多保留的字符数。</summary>
        public const int MaxMessageChars = 6000;
        /// <summary>最新一轮工具结果最多保留的字符数。</summary>
        public const int MaxToolResultChars = 6000;
        /// <summary>较早工具轮次的结果压缩到多少字符（保留开头结论，省掉大段正文）。</summary>
        public const int OldToolResultChars = 400;
        /// <summary>最新一轮工具调用参数最多保留的字符数（write_file 的 content 通常最大）。</summary>
        public const int MaxToolArgsChars = 6000;
        /// <summary>较早工具轮次的参数压缩到多少字符。</summary>
        public const int OldToolArgChars = 200;

        private const string OmitMark = "…（已省略 {0} 字符）";

        public static int EstimateTokens(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int cjk = 0, other = 0;
            for (int i = 0; i < s.Length; i++)
            {
                // 中文 / 全角标点约 0.6 token，英文、代码、空白约 0.3 token；宁可高估
                if (s[i] >= 0x2E80) cjk++;
                else other++;
            }
            return (int)Math.Ceiling(cjk * 0.62 + other * 0.30);
        }

        public static int EstimateMessages(List<ChatMsg> msgs)
        {
            if (msgs == null) return 0;
            int total = 0;
            foreach (ChatMsg m in msgs) total += EstimateMessage(m);
            return total;
        }

        private static int EstimateMessage(ChatMsg m)
        {
            if (m == null) return 0;
            int total = 4 + EstimateTokens(m.Content) + EstimateTokens(m.ReasoningContent);
            if (m.ToolCalls != null)
            {
                foreach (ToolCall tc in m.ToolCalls)
                {
                    if (tc == null) continue;
                    total += 8 + EstimateTokens(tc.Name) + EstimateTokens(tc.Arguments);
                }
            }
            return total;
        }

        public static string FormatTokens(int tokens)
        {
            if (tokens >= 1000) return (tokens / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "K";
            return tokens.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 组装本轮请求：先按规则压缩，再按预算裁掉较早的整段对话。
        /// history 通常是会话里最近的消息，extra 是本轮工具循环产生的 assistant.tool_calls / tool 消息。
        /// </summary>
        public static List<ChatMsg> Build(List<ChatMsg> history, List<ChatMsg> extra, int budgetTokens)
        {
            List<ChatMsg> raw = new List<ChatMsg>();
            if (history != null)
                foreach (ChatMsg m in history) if (m != null) raw.Add(m);
            if (extra != null)
                foreach (ChatMsg m in extra) if (m != null) raw.Add(m);
            if (raw.Count == 0) return raw;
            if (budgetTokens < 0) budgetTokens = 0;

            // 0 = 完全关闭压缩，按原样发送（只复制，不改动会话里的消息）
            if (budgetTokens == 0)
            {
                List<ChatMsg> unchanged = new List<ChatMsg>(raw.Count);
                foreach (ChatMsg m in raw) unchanged.Add(Copy(m));
                return unchanged;
            }

            List<Group> groups = BuildGroups(raw);
            int lastToolGroup = FindLastToolGroup(raw, groups);
            List<ChatMsg> compact = Compact(raw, groups, lastToolGroup);
            return Trim(compact, budgetTokens);
        }

        /// <summary>按字符数截断，保留开头和结尾，中间用提示代替。</summary>
        public static string Clamp(string s, int maxChars, string label)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (maxChars < 32) maxChars = 32;
            if (s.Length <= maxChars) return s;

            string tailMark = "\n…（" + (label == null ? "内容" : label) + "过长，已省略 {0} 字符）…\n";
            int head = (int)(maxChars * 0.72);
            if (head < 1) head = 1;
            // 不要把代理对（emoji 等）从中间切开
            if (head < s.Length && head > 0 && char.IsHighSurrogate(s[head - 1])) head--;

            string mark = string.Format(tailMark, Math.Max(0, s.Length - head));
            int tail = maxChars - head - mark.Length;
            if (tail < 0) tail = 0;
            if (tail > 0 && tail < s.Length)
            {
                int start = s.Length - tail;
                if (char.IsLowSurrogate(s[start])) start++;
                tail = s.Length - start;
                mark = string.Format(tailMark, Math.Max(0, s.Length - head - tail));
            }
            string result = s.Substring(0, head) + mark + (tail > 0 ? s.Substring(s.Length - tail) : "");
            return result.Length <= maxChars ? result : s.Substring(0, Math.Max(32, maxChars - 1)) + "…";
        }

        /// <summary>
        /// 压缩工具调用参数。write_file 的 content 可能有几万字符，直接回传每一轮会成倍放大 token。
        /// 仍保持合法 JSON，只把过长的字符串值换成省略提示。
        /// </summary>
        public static string CompactArguments(string name, string args, int maxChars)
        {
            if (string.IsNullOrEmpty(args)) return args;
            if (maxChars < 48) maxChars = 48;
            if (args.Length <= maxChars) return args;

            Dictionary<string, object> d = null;
            try
            {
                JavaScriptSerializer js = new JavaScriptSerializer();
                d = js.DeserializeObject(args) as Dictionary<string, object>;
            }
            catch { }
            if (d == null || d.Count == 0)
                return "{\"_note\":\"工具参数过长（" + args.Length + " 字符），已省略\"}";

            int perValue = Math.Max(32, (maxChars - 48) / Math.Max(1, d.Count));
            while (perValue >= 24)
            {
                Dictionary<string, object> outD = new Dictionary<string, object>();
                foreach (KeyValuePair<string, object> kv in d)
                {
                    string v = kv.Value as string;
                    if (v != null && v.Length > perValue)
                    {
                        int keep = Math.Max(8, perValue - 20);
                        if (char.IsHighSurrogate(v[keep - 1])) keep--;
                        outD[kv.Key] = v.Substring(0, keep) + string.Format(OmitMark, v.Length - keep);
                    }
                    else
                    {
                        outD[kv.Key] = kv.Value;
                    }
                }
                string rebuilt = SerializeFlat(outD);
                if (rebuilt.Length <= maxChars) return rebuilt;
                perValue = perValue / 2;
            }
            // 极端情况下（参数不是普通 JSON 对象，或键值本身过长）给一个合法 JSON，
            // 避免把 JSON 从中间截断导致严格的网关拒绝整条历史。
            return "{\"_note\":\"工具参数过长（" + args.Length + " 字符），已省略\"}";
        }

        // ================= 内部实现 =================

        private sealed class Group
        {
            public int Start;
            public int End;
            public Group(int s, int e) { Start = s; End = e; }
        }

        private static List<Group> BuildGroups(List<ChatMsg> msgs)
        {
            List<Group> groups = new List<Group>();
            int i = 0;
            while (i < msgs.Count)
            {
                ChatMsg m = msgs[i];
                if (m != null && m.Role == "assistant" && HasToolCalls(m))
                {
                    int j = i + 1;
                    while (j < msgs.Count && msgs[j] != null && msgs[j].Role == "tool") j++;
                    groups.Add(new Group(i, j));
                    i = j;
                }
                else
                {
                    groups.Add(new Group(i, i + 1));
                    i++;
                }
            }
            return groups;
        }

        private static bool HasToolCalls(ChatMsg m)
        {
            return m != null && m.Role == "assistant" && m.ToolCalls != null && m.ToolCalls.Count > 0;
        }

        private static int FindLastToolGroup(List<ChatMsg> msgs, List<Group> groups)
        {
            int last = -1;
            for (int g = 0; g < groups.Count; g++)
            {
                Group gr = groups[g];
                for (int i = gr.Start; i < gr.End; i++)
                {
                    if (HasToolCalls(msgs[i])) { last = g; break; }
                }
            }
            return last;
        }

        private static ChatMsg Copy(ChatMsg m)
        {
            ChatMsg c = new ChatMsg(m.Role, m.Content);
            c.ToolCallId = m.ToolCallId;
            c.ReasoningContent = m.ReasoningContent;
            if (m.ToolCalls != null)
            {
                c.ToolCalls = new List<ToolCall>(m.ToolCalls.Count);
                foreach (ToolCall t in m.ToolCalls)
                {
                    if (t == null) { c.ToolCalls.Add(null); continue; }
                    ToolCall n = new ToolCall();
                    n.Id = t.Id;
                    n.Name = t.Name;
                    n.Arguments = t.Arguments;
                    c.ToolCalls.Add(n);
                }
            }
            return c;
        }

        private static List<ChatMsg> Compact(List<ChatMsg> raw, List<Group> groups, int lastToolGroup)
        {
            bool[] keepFull = new bool[raw.Count];
            if (lastToolGroup >= 0)
            {
                Group g = groups[lastToolGroup];
                for (int i = g.Start; i < g.End; i++) keepFull[i] = true;
            }

            List<ChatMsg> list = new List<ChatMsg>(raw.Count);
            for (int i = 0; i < raw.Count; i++)
            {
                ChatMsg m = raw[i];
                ChatMsg c = Copy(m);
                if (c.Role == "tool")
                {
                    c.Content = Clamp(c.Content, keepFull[i] ? MaxToolResultChars : OldToolResultChars, "工具结果");
                }
                else if (HasToolCalls(c))
                {
                    c.Content = Clamp(c.Content, 2000, "助手回复");
                    for (int k = 0; k < c.ToolCalls.Count; k++)
                    {
                        ToolCall tc = c.ToolCalls[k];
                        if (tc == null) continue;
                        tc.Arguments = CompactArguments(tc.Name, tc.Arguments, keepFull[i] ? MaxToolArgsChars : OldToolArgChars);
                    }
                }
                else
                {
                    c.Content = Clamp(c.Content, MaxMessageChars, "消息");
                }
                list.Add(c);
            }
            return list;
        }

        private static List<ChatMsg> Trim(List<ChatMsg> msgs, int budgetTokens)
        {
            List<Group> groups = BuildGroups(msgs);
            int[] costs = new int[groups.Count];
            int total = 0;
            for (int g = 0; g < groups.Count; g++)
            {
                costs[g] = EstimateRange(msgs, groups[g]);
                total += costs[g];
            }
            if (total <= budgetTokens) return msgs;

            int anchor = FindLastUserGroup(msgs, groups);
            int anchorPrev = FindPreviousUserGroup(msgs, groups, anchor);
            // 只有“工具调用总结”这种场景（两条用户消息之间夹着工具轮次）才额外保留上一条用户消息，
            // 普通聊天里不要把很久以前的用户消息硬塞回来。
            if (anchorPrev >= 0 && !HasToolCallGroupBetween(msgs, groups, anchorPrev, anchor)) anchorPrev = -1;

            // 先强制保留最后 1~2 条用户消息和最近一轮工具调用：工具循环里当前任务在最前面，
            // 如果只按“连续后缀”裁剪，会把整段对话都带上，或者把任务本身裁掉。
            bool[] keep = new bool[groups.Count];
            int acc = 0;
            if (anchor >= 0) { keep[anchor] = true; acc += costs[anchor]; }
            if (anchorPrev >= 0) { keep[anchorPrev] = true; acc += costs[anchorPrev]; }
            int lastTool = FindLastToolGroup(msgs, groups);
            if (lastTool >= 0 && !keep[lastTool]) { keep[lastTool] = true; acc += costs[lastTool]; }

            // 从最新一组往前装，装不下就停：更早的整轮工具调用会被丢掉；
            // 强制保留的用户消息仍会按时间顺序拼回去。
            for (int g = groups.Count - 1; g >= 0; g--)
            {
                if (keep[g]) continue;
                if (acc + costs[g] <= budgetTokens)
                {
                    keep[g] = true;
                    acc += costs[g];
                }
                else
                {
                    break;
                }
            }

            List<ChatMsg> selected = new List<ChatMsg>();
            for (int g = 0; g < groups.Count; g++)
            {
                if (!keep[g]) continue;
                for (int i = groups[g].Start; i < groups[g].End; i++) selected.Add(msgs[i]);
            }
            if (selected.Count == 0)
            {
                Group last = groups[groups.Count - 1];
                for (int i = last.Start; i < last.End; i++) selected.Add(msgs[i]);
            }
            if (EstimateMessages(selected) > budgetTokens)
                selected = Shrink(selected, budgetTokens);
            return selected;
        }

        private static List<ChatMsg> Shrink(List<ChatMsg> msgs, int budgetTokens)
        {
            // 1) 先压缩工具参数（写文件的大段 content 最占地方）
            int[] argCaps = new int[] { 1500, 500 };
            foreach (int cap in argCaps)
            {
                foreach (ChatMsg m in msgs)
                {
                    if (!HasToolCalls(m)) continue;
                    foreach (ToolCall tc in m.ToolCalls)
                    {
                        if (tc == null) continue;
                        tc.Arguments = CompactArguments(tc.Name, tc.Arguments, cap);
                    }
                }
                if (EstimateMessages(msgs) <= budgetTokens) return msgs;
            }

            // 2) 再逐步压缩工具结果（最新一轮也一起压，优先保住最近的结论）
            int[] resultCaps = new int[] { 3000, 1500, 800, 400 };
            foreach (int cap in resultCaps)
            {
                foreach (ChatMsg m in msgs)
                    if (m.Role == "tool") m.Content = Clamp(m.Content, cap, "工具结果");
                if (EstimateMessages(msgs) <= budgetTokens) return msgs;
            }
            return msgs;
        }

        private static int EstimateRange(List<ChatMsg> msgs, Group g)
        {
            int total = 0;
            for (int i = g.Start; i < g.End; i++) total += EstimateMessage(msgs[i]);
            return total;
        }

        private static int FindLastUserGroup(List<ChatMsg> msgs, List<Group> groups)
        {
            for (int i = msgs.Count - 1; i >= 0; i--)
            {
                if (msgs[i] == null || msgs[i].Role != "user") continue;
                int g = GroupOf(groups, i);
                if (g >= 0) return g;
            }
            return -1;
        }

        private static int FindPreviousUserGroup(List<ChatMsg> msgs, List<Group> groups, int beforeGroup)
        {
            if (beforeGroup < 0) return -1;
            for (int i = msgs.Count - 1; i >= 0; i--)
            {
                if (msgs[i] == null || msgs[i].Role != "user") continue;
                int g = GroupOf(groups, i);
                if (g >= 0 && g < beforeGroup) return g;
            }
            return -1;
        }

        private static int GroupOf(List<Group> groups, int index)
        {
            for (int g = 0; g < groups.Count; g++)
                if (index >= groups[g].Start && index < groups[g].End) return g;
            return -1;
        }

        private static bool HasToolCallGroupBetween(List<ChatMsg> msgs, List<Group> groups, int fromGroup, int toGroup)
        {
            for (int g = fromGroup + 1; g < toGroup; g++)
                for (int i = groups[g].Start; i < groups[g].End; i++)
                    if (HasToolCalls(msgs[i])) return true;
            return false;
        }

        /// <summary>把字典按 JSON 重新拼出来，避免 JavaScriptSerializer 把中文转成 \uXXXX 反而变长。</summary>
        private static string SerializeFlat(Dictionary<string, object> d)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append('{');
            bool first = true;
            foreach (KeyValuePair<string, object> kv in d)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(Json.Escape(kv.Key)).Append("\":");
                object v = kv.Value;
                if (v == null) sb.Append("null");
                else if (v is string) sb.Append('"').Append(Json.Escape((string)v)).Append('"');
                else if (v is bool) sb.Append(((bool)v) ? "true" : "false");
                else if (v is int || v is long || v is decimal || v is double || v is float)
                    sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture));
                else sb.Append('"').Append(Json.Escape(Convert.ToString(v, CultureInfo.InvariantCulture))).Append('"');
            }
            sb.Append('}');
            return sb.ToString();
        }
    }
}
