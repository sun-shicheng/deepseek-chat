using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DsChat
{
    public class WorkspaceEntry
    {
        public string Path;
        public long Size;
        public bool IsDir;
        public DateTime Modified;
    }

    /// <summary>
    /// 工作区文件操作。所有路径都会被限制在已配置的工作区目录内，
    /// 超出范围的相对路径（含 ..）一律拒绝。
    /// </summary>
    public static class Workspace
    {
        public const int MaxReadBytes = 200 * 1024;
        /// <summary>界面文件面板最多列出多少项。</summary>
        public const int MaxListEntries = 300;
        /// <summary>给模型的 list_files 最多返回多少项，避免超大目录占满上下文。</summary>
        public const int MaxModelListEntries = 120;
        /// <summary>read_file 单次最多返回的字符数（约 3~6K token 的中文），超出时请模型分页读取。</summary>
        public const int MaxReadChars = 5600;
        public const int DefaultReadLines = 200;
        public const int MaxReadLines = 400;

        public static bool IsReady(string root)
        {
            if (string.IsNullOrEmpty(root)) return false;
            try { return Directory.Exists(root); } catch { return false; }
        }

        /// <summary>把相对路径解析成绝对路径；越界抛异常。</summary>
        public static string Resolve(string root, string rel)
        {
            if (!IsReady(root)) throw new Exception("还没有设置工作区目录（点「工作区」按钮选择）");

            string r = rel == null ? "" : rel.Trim().Replace('/', Path.DirectorySeparatorChar);
            string rootFull = Path.GetFullPath(root);
            if (!rootFull.EndsWith(Path.DirectorySeparatorChar.ToString())) rootFull += Path.DirectorySeparatorChar;
            if (r.Length == 0) return rootFull;

            string full = Path.GetFullPath(Path.Combine(rootFull, r));
            if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                throw new Exception("路径超出工作区范围，已拒绝：" + rel);
            return full;
        }

        public static string RelOf(string root, string full)
        {
            string rootFull = Path.GetFullPath(root);
            if (!rootFull.EndsWith(Path.DirectorySeparatorChar.ToString())) rootFull += Path.DirectorySeparatorChar;
            if (full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) return full.Substring(rootFull.Length);
            return full;
        }

        public static List<WorkspaceEntry> ListEntries(string root, string subdir)
        {
            return ListEntries(root, subdir, MaxListEntries);
        }

        private static List<WorkspaceEntry> ListEntries(string root, string subdir, int limit)
        {
            if (limit < 1) limit = 1;
            List<WorkspaceEntry> list = new List<WorkspaceEntry>();
            string dir = Resolve(root, subdir);
            if (!Directory.Exists(dir)) throw new Exception("目录不存在：" + subdir);

            foreach (string d in Directory.GetDirectories(dir))
            {
                if (list.Count >= limit) break;
                DirectoryInfo di = new DirectoryInfo(d);
                if (di.Name == ".git" || di.Name == "node_modules" || di.Name == "bin" || di.Name == "obj") continue;
                if ((di.Attributes & FileAttributes.Hidden) != 0) continue;
                WorkspaceEntry e = new WorkspaceEntry();
                e.Path = RelOf(root, d);
                e.IsDir = true;
                e.Modified = di.LastWriteTime;
                list.Add(e);
            }
            foreach (string f in Directory.GetFiles(dir))
            {
                if (list.Count >= limit) break;
                FileInfo fi = new FileInfo(f);
                if ((fi.Attributes & FileAttributes.Hidden) != 0) continue;
                WorkspaceEntry e = new WorkspaceEntry();
                e.Path = RelOf(root, f);
                e.Size = fi.Length;
                e.IsDir = false;
                e.Modified = fi.LastWriteTime;
                list.Add(e);
            }
            list.Sort(delegate (WorkspaceEntry a, WorkspaceEntry b)
            {
                if (a.IsDir != b.IsDir) return a.IsDir ? -1 : 1;
                return string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        /// <summary>给模型看的文字版目录列表。</summary>
        public static string ListFiles(string root, string subdir)
        {
            List<WorkspaceEntry> list = ListEntries(root, subdir, MaxModelListEntries);
            StringBuilder sb = new StringBuilder();
            string baseRel = string.IsNullOrEmpty(subdir) ? "." : subdir.Trim().TrimEnd('/', '\\');
            sb.Append("工作区目录：").Append(baseRel).AppendLine();
            if (list.Count == 0) { sb.Append("（空目录）"); return sb.ToString(); }
            foreach (WorkspaceEntry e in list)
            {
                sb.Append(KindOf(e)).Append(' ').Append(e.Path.Replace('\\', '/'));
                if (!e.IsDir) sb.Append("  ").Append(FormatSize(e.Size));
                sb.AppendLine();
            }
            sb.Append("共 ").Append(list.Count).Append(" 项");
            if (list.Count >= MaxModelListEntries) sb.Append("（只显示前 ").Append(MaxModelListEntries).Append(" 项，请用 subdir 缩小范围）");
            return sb.ToString();
        }

        public static string ReadText(string root, string rel)
        {
            long fileLength;
            bool truncated;
            bool office;
            string text = ReadAny(root, rel, out fileLength, out truncated, out office);
            if (truncated)
            {
                if (office)
                    text += "\r\n\r\n…（内容较大，只提取了前 " + OfficeFiles.MaxExtractChars + " 字符；完整内容请用 Excel / Word 打开）";
                else
                    text += "\r\n\r\n…（文件较大，只读取了前 " + FormatSize(MaxReadBytes) + "，全文 " + FormatSize(fileLength) + "）";
            }
            return text;
        }

        /// <summary>
        /// 给模型用的分页读取：只返回指定行范围，并限制总字符数，避免一次把 200 KB 全塞进上下文。
        /// startLine 从 1 开始；maxLines 默认 200，最大 400。
        /// </summary>
        public static string ReadTextPage(string root, string rel, int startLine, int maxLines,
            out int firstLine, out int lastLine, out int totalLines)
        {
            firstLine = 1;
            lastLine = 0;
            totalLines = 0;

            long fileLength;
            bool truncated;
            bool office;
            string raw = ReadAny(root, rel, out fileLength, out truncated, out office);
            string normalized = raw.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Split('\n');
            totalLines = lines.Length;
            while (totalLines > 1 && lines[totalLines - 1].Length == 0) totalLines--;

            if (startLine < 1) startLine = 1;
            if (maxLines < 1) maxLines = DefaultReadLines;
            if (maxLines > MaxReadLines) maxLines = MaxReadLines;

            string sizeText = FormatSize(fileLength);
            string scanText = truncated
                ? (office ? "，内容较大已截断" : "，仅扫描前 " + FormatSize(MaxReadBytes))
                : "";
            if (startLine > totalLines)
            {
                firstLine = lastLine = totalLines;
                return "文件：" + rel + "（共 " + totalLines + " 行" + scanText + "；" + sizeText + "）\n"
                     + "start_line=" + startLine + " 超出范围。";
            }

            int limit = Math.Min(totalLines, startLine + maxLines - 1);
            StringBuilder body = new StringBuilder();
            int last = startLine - 1;
            bool clipped = false;
            for (int i = startLine - 1; i < limit; i++)
            {
                string line = lines[i];
                int need = (body.Length == 0 ? 0 : 1) + line.Length;
                if (body.Length > 0 && body.Length + need > MaxReadChars) break;
                if (body.Length > 0) body.Append('\n');
                body.Append(line);
                last = i + 1;
                if (body.Length >= MaxReadChars)
                {
                    if (body.Length > MaxReadChars)
                    {
                        body.Length = MaxReadChars;
                        body.Append('…');
                        clipped = true;
                    }
                    break;
                }
            }
            if (last < startLine)
            {
                // 单行就超过上限：保留开头，避免直接返回空
                string line = lines[startLine - 1];
                if (line.Length > MaxReadChars)
                {
                    line = line.Substring(0, MaxReadChars) + "…";
                    clipped = true;
                }
                body.Append(line);
                last = startLine;
            }

            firstLine = startLine;
            lastLine = last;
            StringBuilder sb = new StringBuilder();
            sb.Append("文件：").Append(rel).Append("（第 ").Append(firstLine);
            if (lastLine > firstLine) sb.Append('-').Append(lastLine);
            sb.Append(" 行 / 共 ").Append(totalLines).Append(" 行").Append(scanText).Append("；").Append(sizeText).Append("）\n");
            sb.Append(body);
            if (lastLine < totalLines)
                sb.Append("\n\n…（还有 ").Append(totalLines - lastLine).Append(" 行；继续读取请传 start_line=").Append(lastLine + 1).Append("）");
            else if (truncated)
                sb.Append("\n\n…（").Append(office
                    ? "内容较大，只提取了前面部分"
                    : "文件超过 " + FormatSize(MaxReadBytes) + "，后续内容暂未扫描").Append("）");
            if (clipped) sb.Append("\n…（本行过长已截断）");
            return sb.ToString();
        }

        /// <summary>读取任意支持的文件：xlsx/docx 走 OfficeFiles，其余按文本读取。</summary>
        private static string ReadAny(string root, string rel, out long fileLength, out bool truncated, out bool office)
        {
            string full = Resolve(root, rel);
            if (Directory.Exists(full)) throw new Exception("这是目录，不是文件：" + rel);
            if (!File.Exists(full)) throw new Exception("文件不存在：" + rel);
            if (OfficeFiles.IsLegacy(full))
                throw new Exception("暂不支持旧版 / 宏 / 模板格式（.doc/.xls/.docm 等），请先用 Word / Excel 另存为 .docx/.xlsx：" + rel);

            string officeText;
            bool officeCut;
            if (OfficeFiles.TryRead(full, out officeText, out officeCut))
            {
                office = true;
                fileLength = new FileInfo(full).Length;
                truncated = officeCut;
                return officeText;
            }

            office = false;
            return ReadRawFull(full, rel, out fileLength, out truncated);
        }

        private static string ReadRawFull(string full, string rel, out long fileLength, out bool truncated)
        {
            using (FileStream fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                fileLength = fs.Length;
                truncated = fs.Length > MaxReadBytes;
                int len = (int)Math.Min(fs.Length, (long)MaxReadBytes);
                byte[] buf = new byte[len];
                int read = 0;
                while (read < len)
                {
                    int n = fs.Read(buf, read, len - read);
                    if (n <= 0) break;
                    read += n;
                }
                if (read < buf.Length) Array.Resize(ref buf, read);
                if (LooksBinary(buf))
                    throw new Exception("这是二进制文件，暂不支持读取：" + rel
                        + "（支持 .xlsx/.docx，以及 .txt/.py/.c 等文本）");
                return Decode(buf);
            }
        }

        public static int WriteText(string root, string rel, string content)
        {
            string full = Resolve(root, rel);
            if (IsRootPath(root, full)) throw new Exception("不能写入工作区根目录");
            if (Directory.Exists(full)) throw new Exception("同名目录已存在：" + rel);
            if (OfficeFiles.IsLegacy(full))
                throw new Exception("暂不支持写入旧版 / 宏 / 模板格式（.doc/.xls/.docm 等），请改用 .docx/.xlsx：" + rel);

            string dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string text = content == null ? "" : content;
            if (OfficeFiles.IsOffice(full))
            {
                OfficeFiles.Write(full, text);
                return text.Length;
            }

            File.WriteAllText(full, text, new UTF8Encoding(false));
            return text.Length;
        }

        /// <summary>文件是否已经与要写入的内容完全一致（用于跳过重复写入）。</summary>
        public static bool SameText(string root, string rel, string content)
        {
            try
            {
                string full = Resolve(root, rel);
                if (!File.Exists(full)) return false;
                if (OfficeFiles.IsOffice(full)) return OfficeFiles.SameText(full, content);

                byte[] want = new UTF8Encoding(false).GetBytes(content == null ? "" : content);
                byte[] have = File.ReadAllBytes(full);
                if (have.Length != want.Length) return false;
                for (int i = 0; i < have.Length; i++)
                    if (have[i] != want[i]) return false;
                return true;
            }
            catch { return false; }
        }

        private static string KindOf(WorkspaceEntry e)
        {
            if (e.IsDir) return "[目录]";
            if (OfficeFiles.IsWorkbook(e.Path)) return "[表格]";
            if (OfficeFiles.IsDocument(e.Path)) return "[文档]";
            if (IsCodeFile(e.Path)) return "[代码]";
            return "[文件]";
        }

        private static bool IsCodeFile(string path)
        {
            string ext = Path.GetExtension(path);
            return string.Equals(ext, ".py", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ext, ".c", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ext, ".h", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ext, ".cpp", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ext, ".cs", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ext, ".js", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ext, ".ts", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ext, ".java", StringComparison.OrdinalIgnoreCase);
        }

        public static bool Exists(string root, string rel)
        {
            try
            {
                string full = Resolve(root, rel);
                return File.Exists(full) || Directory.Exists(full);
            }
            catch { return false; }
        }

        public static void Delete(string root, string rel)
        {
            string full = Resolve(root, rel);
            if (IsRootPath(root, full)) throw new Exception("不能删除工作区根目录");
            if (Directory.Exists(full)) Directory.Delete(full, false);
            else if (File.Exists(full)) File.Delete(full);
            else throw new Exception("不存在：" + rel);
        }

        public static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
            return (bytes / 1024.0 / 1024.0).ToString("0.0") + " MB";
        }

        private static bool IsRootPath(string root, string full)
        {
            return string.Equals(full.TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksBinary(byte[] buf)
        {
            int n = Math.Min(buf.Length, 4096);
            for (int i = 0; i < n; i++) if (buf[i] == 0) return true;
            return false;
        }

        /// <summary>优先按 UTF-8 严格解码，失败则按 GBK(936) 兜底（内网老文件常是 GBK）。</summary>
        private static string Decode(byte[] buf)
        {
            int start = 0;
            if (buf.Length >= 3 && buf[0] == 0xEF && buf[1] == 0xBB && buf[2] == 0xBF) start = 3;
            try
            {
                UTF8Encoding strict = new UTF8Encoding(false, true);
                return strict.GetString(buf, start, buf.Length - start);
            }
            catch
            {
                try { return Encoding.GetEncoding(936).GetString(buf); }
                catch { return Encoding.Default.GetString(buf); }
            }
        }
    }
}
