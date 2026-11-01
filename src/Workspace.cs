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
        public const int MaxListEntries = 300;

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
            List<WorkspaceEntry> list = new List<WorkspaceEntry>();
            string dir = Resolve(root, subdir);
            if (!Directory.Exists(dir)) throw new Exception("目录不存在：" + subdir);

            foreach (string d in Directory.GetDirectories(dir))
            {
                if (list.Count >= MaxListEntries) break;
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
                if (list.Count >= MaxListEntries) break;
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
            List<WorkspaceEntry> list = ListEntries(root, subdir);
            StringBuilder sb = new StringBuilder();
            string baseRel = string.IsNullOrEmpty(subdir) ? "." : subdir.Trim().TrimEnd('/', '\\');
            sb.Append("工作区目录：").Append(baseRel).AppendLine();
            if (list.Count == 0) { sb.Append("（空目录）"); return sb.ToString(); }
            foreach (WorkspaceEntry e in list)
            {
                sb.Append(e.IsDir ? "[目录] " : "[文件] ").Append(e.Path.Replace('\\', '/'));
                if (!e.IsDir) sb.Append("  ").Append(FormatSize(e.Size));
                sb.AppendLine();
            }
            sb.Append("共 ").Append(list.Count).Append(" 项");
            if (list.Count >= MaxListEntries) sb.Append("（已达上限，可能被截断）");
            return sb.ToString();
        }

        public static string ReadText(string root, string rel)
        {
            string full = Resolve(root, rel);
            if (Directory.Exists(full)) throw new Exception("这是目录，不是文件：" + rel);
            if (!File.Exists(full)) throw new Exception("文件不存在：" + rel);

            using (FileStream fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
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
                if (LooksBinary(buf)) throw new Exception("这是二进制文件，暂不支持读取：" + rel);

                string text = Decode(buf);
                if (fs.Length > MaxReadBytes)
                    text += "\r\n\r\n…（文件较大，只读取了前 " + FormatSize(MaxReadBytes) + "，全文 " + FormatSize(fs.Length) + "）";
                return text;
            }
        }

        public static int WriteText(string root, string rel, string content)
        {
            string full = Resolve(root, rel);
            if (IsRootPath(root, full)) throw new Exception("不能写入工作区根目录");
            if (Directory.Exists(full)) throw new Exception("同名目录已存在：" + rel);

            string dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string text = content == null ? "" : content;
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
                byte[] want = new UTF8Encoding(false).GetBytes(content == null ? "" : content);
                byte[] have = File.ReadAllBytes(full);
                if (have.Length != want.Length) return false;
                for (int i = 0; i < have.Length; i++)
                    if (have[i] != want[i]) return false;
                return true;
            }
            catch { return false; }
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
