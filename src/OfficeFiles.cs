using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace DsChat
{
    /// <summary>
    /// 不依赖 Office / 第三方库的 .xlsx 与 .docx 读写。
    /// - .xlsx：读出所有工作表为 TSV 文本；写入时按 CSV / TSV / Markdown 表格解析，生成单工作表。
    /// - .docx：读出段落、标题和表格为文本；写入时按纯文本段落生成，Markdown 的 # / ## / ### 会映射为 Word 标题。
    /// - .py / .c 等纯文本仍由 Workspace 直接读写，这里不处理。
    /// </summary>
    public static class OfficeFiles
    {
        public const int MaxExtractChars = 200000;
        public const int MaxSheetRows = 2000;
        public const int MaxSheetCols = 100;
        public const int MaxSheets = 20;
        public const int MaxWriteRows = 20000;
        public const int MaxWriteCols = 256;
        private const int MaxEntryBytes = 30 * 1024 * 1024;
        private const int MaxCellChars = 32767;

        private const string MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private const string PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        private const string WordNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        private const string XmlHead = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n";
        private static readonly DateTimeOffset ZipTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public static bool IsWorkbook(string path)
        {
            return ExtIs(path, ".xlsx");
        }

        public static bool IsDocument(string path)
        {
            return ExtIs(path, ".docx");
        }

        public static bool IsOffice(string path)
        {
            return IsWorkbook(path) || IsDocument(path);
        }

        /// <summary>旧版二进制 / 宏 / 模板格式：不做转换，提示用户另存为 .xlsx / .docx。</summary>
        public static bool IsLegacy(string path)
        {
            return ExtIs(path, ".xls") || ExtIs(path, ".doc")
                || ExtIs(path, ".xlsm") || ExtIs(path, ".docm") || ExtIs(path, ".xlsb")
                || ExtIs(path, ".xlt") || ExtIs(path, ".xltx")
                || ExtIs(path, ".dot") || ExtIs(path, ".dotx");
        }

        private static bool ExtIs(string path, string ext)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try { return string.Equals(Path.GetExtension(path), ext, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        /// <summary>读取 Office 文件为文本；不是 .xlsx/.docx 时返回 false。</summary>
        public static bool TryRead(string fullPath, out string text, out bool truncated)
        {
            text = null;
            truncated = false;
            if (!IsOffice(fullPath)) return false;
            if (!File.Exists(fullPath)) throw new Exception("文件不存在：" + Path.GetFileName(fullPath));

            text = IsWorkbook(fullPath) ? ReadWorkbook(fullPath) : ReadDocument(fullPath);
            if (text == null) text = "";
            if (text.Length > MaxExtractChars)
            {
                text = text.Substring(0, MaxExtractChars);
                truncated = true;
            }
            return true;
        }

        public static void Write(string fullPath, string content)
        {
            string text = content == null ? "" : content;
            byte[] bytes;
            if (IsWorkbook(fullPath)) bytes = BuildWorkbook(text);
            else if (IsDocument(fullPath)) bytes = BuildDocument(text);
            else throw new Exception("不是支持的 Office 扩展名：" + fullPath);
            WriteBytesAtomic(fullPath, bytes);
        }

        /// <summary>用于 write_file 的重复写入判断：生成同样的字节就视为内容未变化。</summary>
        public static bool SameText(string fullPath, string content)
        {
            try
            {
                if (!File.Exists(fullPath)) return false;
                string text = content == null ? "" : content;
                byte[] want = IsWorkbook(fullPath) ? BuildWorkbook(text) : BuildDocument(text);
                byte[] have = File.ReadAllBytes(fullPath);
                if (have.Length != want.Length) return false;
                for (int i = 0; i < have.Length; i++)
                    if (have[i] != want[i]) return false;
                return true;
            }
            catch { return false; }
        }

        // ================= xlsx 读取 =================

        private static string ReadWorkbook(string fullPath)
        {
            using (FileStream fs = OpenRead(fullPath))
            using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                string wbXml = ReadEntryText(zip, "xl/workbook.xml");
                if (string.IsNullOrEmpty(wbXml))
                    throw new Exception("不是有效的 .xlsx（缺少 xl/workbook.xml）：" + Path.GetFileName(fullPath));

                XmlDocument wb = LoadXml(wbXml);
                XmlNamespaceManager ns = new XmlNamespaceManager(wb.NameTable);
                ns.AddNamespace("m", MainNs);
                ns.AddNamespace("r", RelNs);

                XmlNodeList sheets = wb.SelectNodes("/m:workbook/m:sheets/m:sheet", ns);
                Dictionary<string, string> rels = ReadRels(zip, "xl/_rels/workbook.xml.rels");
                List<string> shared = ReadSharedStrings(zip);
                StringBuilder sb = new StringBuilder();
                int sheetCount = sheets == null ? 0 : sheets.Count;
                sb.Append("工作簿：").Append(Path.GetFileName(fullPath))
                  .Append("（").Append(sheetCount).Append(" 个工作表；单元格按 TSV 输出，日期为 Excel 序列值）\n");

                int shown = 0;
                if (sheets != null)
                {
                    foreach (XmlNode sheet in sheets)
                    {
                        if (shown >= MaxSheets)
                        {
                            sb.Append("\n…（工作表过多，只显示前 ").Append(MaxSheets).Append(" 个）");
                            break;
                        }
                        string name = Attr(sheet, "name");
                        string rid = AttrNs(sheet, "id", RelNs);
                        string target = null;
                        if (!string.IsNullOrEmpty(rid) && rels.ContainsKey(rid)) target = rels[rid];
                        if (string.IsNullOrEmpty(target)) target = "worksheets/sheet" + (shown + 1) + ".xml";
                        string part = ResolvePart("xl", target);
                        string sheetXml = ReadEntryText(zip, part);
                        if (string.IsNullOrEmpty(sheetXml))
                        {
                            sb.Append("\n=== 工作表：").Append(name).Append(" ===\n（无法读取工作表数据）\n");
                            shown++;
                            continue;
                        }

                        XmlDocument sheetDoc = LoadXml(sheetXml);
                        XmlNamespaceManager sns = new XmlNamespaceManager(sheetDoc.NameTable);
                        sns.AddNamespace("m", MainNs);
                        int rowCount, colCount;
                        bool cut;
                        string table = ReadSheet(sheetDoc, sns, shared, out rowCount, out colCount, out cut);
                        sb.Append("\n=== 工作表：").Append(name)
                          .Append("（").Append(rowCount).Append(" 行 × ").Append(colCount).Append(" 列")
                          .Append(cut ? "，已截断" : "").Append("）===\n");
                        sb.Append(table);
                        if (table.Length == 0 || table[table.Length - 1] != '\n') sb.Append('\n');
                        shown++;
                    }
                }
                if (shown == 0) sb.Append("\n（没有可读取的工作表）");
                return sb.ToString();
            }
        }

        private static List<string> ReadSharedStrings(ZipArchive zip)
        {
            List<string> list = new List<string>();
            string xml = ReadEntryText(zip, "xl/sharedStrings.xml");
            if (string.IsNullOrEmpty(xml)) return list;
            XmlDocument doc = LoadXml(xml);
            XmlNamespaceManager ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("m", MainNs);
            XmlNodeList items = doc.SelectNodes("/m:sst/m:si", ns);
            if (items == null) return list;
            foreach (XmlNode si in items)
            {
                StringBuilder sb = new StringBuilder();
                XmlNodeList texts = si.SelectNodes(".//m:t", ns);
                if (texts != null) foreach (XmlNode t in texts) sb.Append(t.InnerText);
                list.Add(sb.ToString());
            }
            return list;
        }

        private static Dictionary<string, string> ReadRels(ZipArchive zip, string path)
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            string xml = ReadEntryText(zip, path);
            if (string.IsNullOrEmpty(xml)) return d;
            XmlDocument doc = LoadXml(xml);
            XmlNamespaceManager ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("r", PackageRelNs);
            XmlNodeList nodes = doc.SelectNodes("/r:Relationships/r:Relationship", ns);
            if (nodes == null) return d;
            foreach (XmlNode n in nodes)
            {
                string id = Attr(n, "Id");
                string target = Attr(n, "Target");
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(target)) d[id] = target;
            }
            return d;
        }

        private static string ReadSheet(XmlDocument doc, XmlNamespaceManager ns, List<string> shared,
            out int rowCount, out int colCount, out bool truncated)
        {
            rowCount = 0;
            colCount = 0;
            truncated = false;
            XmlNodeList rows = doc.SelectNodes("/m:worksheet/m:sheetData/m:row", ns);
            if (rows == null || rows.Count == 0) return "";

            StringBuilder sb = new StringBuilder();
            int emitted = 0;
            foreach (XmlNode row in rows)
            {
                if (emitted >= MaxSheetRows) { truncated = true; break; }
                SortedDictionary<int, string> cells = new SortedDictionary<int, string>();
                int seq = 0;
                XmlNodeList cellNodes = row.SelectNodes("m:c", ns);
                if (cellNodes != null)
                {
                    foreach (XmlNode c in cellNodes)
                    {
                        string r = Attr(c, "r");
                        int col = seq;
                        if (!string.IsNullOrEmpty(r))
                        {
                            int ci = ColumnIndex(r);
                            if (ci >= 0) col = ci;
                        }
                        seq = col + 1;
                        if (col >= MaxSheetCols) { truncated = true; continue; }
                        string val = CellText(c, ns, shared);
                        if (val.Length == 0) continue;
                        cells[col] = val;
                    }
                }
                if (cells.Count == 0) continue;

                int max = 0;
                foreach (int k in cells.Keys) if (k > max) max = k;
                StringBuilder line = new StringBuilder();
                for (int i = 0; i <= max; i++)
                {
                    if (i > 0) line.Append('\t');
                    string v;
                    if (cells.TryGetValue(i, out v)) line.Append(v);
                }
                sb.Append(line).Append('\n');
                emitted++;
                if (max + 1 > colCount) colCount = max + 1;
            }
            rowCount = emitted;
            return sb.ToString();
        }

        private static string CellText(XmlNode c, XmlNamespaceManager ns, List<string> shared)
        {
            string t = Attr(c, "t");
            if (t == "s")
            {
                string v = ChildText(c, "v", ns);
                int idx;
                if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out idx)
                    && idx >= 0 && idx < shared.Count)
                    return CleanCell(shared[idx]);
                return "";
            }
            if (t == "inlineStr")
            {
                StringBuilder sb = new StringBuilder();
                XmlNodeList texts = c.SelectNodes(".//m:t", ns);
                if (texts != null) foreach (XmlNode n in texts) sb.Append(n.InnerText);
                return CleanCell(sb.ToString());
            }
            if (t == "b")
            {
                string v = ChildText(c, "v", ns);
                return v == "1" ? "TRUE" : (v == "0" ? "FALSE" : v);
            }
            if (t == "e") return CleanCell(ChildText(c, "v", ns));

            string formula = ChildText(c, "f", ns);
            string value = ChildText(c, "v", ns);
            if (value.Length > 0) return CleanCell(value);
            if (formula.Length > 0) return "=" + CleanCell(formula);
            return "";
        }

        private static string CleanCell(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length);
            foreach (char ch in s)
            {
                if (ch == '\r' || ch == '\n' || ch == '\t') sb.Append(' ');
                else sb.Append(ch);
            }
            return sb.ToString().Trim();
        }

        // ================= docx 读取 =================

        private static string ReadDocument(string fullPath)
        {
            using (FileStream fs = OpenRead(fullPath))
            using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                string xml = ReadEntryText(zip, "word/document.xml");
                if (string.IsNullOrEmpty(xml))
                    throw new Exception("不是有效的 .docx（缺少 word/document.xml）：" + Path.GetFileName(fullPath));

                XmlDocument doc = LoadXml(xml);
                XmlNamespaceManager ns = new XmlNamespaceManager(doc.NameTable);
                ns.AddNamespace("w", WordNs);
                XmlNode body = doc.SelectSingleNode("/w:document/w:body", ns);
                if (body == null) throw new Exception("不是有效的 .docx（缺少正文）：" + Path.GetFileName(fullPath));

                StringBuilder sb = new StringBuilder();
                AppendBlocks(body, sb, ns);
                return sb.ToString();
            }
        }

        private static void AppendBlocks(XmlNode parent, StringBuilder sb, XmlNamespaceManager ns)
        {
            foreach (XmlNode node in parent.ChildNodes)
            {
                if (node.NodeType != XmlNodeType.Element) continue;
                switch (node.LocalName)
                {
                    case "p":
                        AppendParagraph(node, sb, ns);
                        break;
                    case "tbl":
                        AppendTable(node, sb, ns);
                        break;
                    case "sectPr":
                        break;
                    case "sdt":
                    case "sdtContent":
                    case "customXml":
                    case "ins":
                        AppendBlocks(node, sb, ns);
                        break;
                }
            }
        }

        private static void AppendParagraph(XmlNode p, StringBuilder sb, XmlNamespaceManager ns)
        {
            string text = ParagraphText(p, ns);
            string prefix = "";
            string style = "";
            XmlNode pStyle = p.SelectSingleNode("w:pPr/w:pStyle", ns);
            if (pStyle != null) style = AttrNs(pStyle, "val", WordNs);
            if (style == "Title" || style == "Heading1" || style == "1") prefix = "# ";
            else if (style == "Subtitle" || style == "Heading2" || style == "2") prefix = "## ";
            else if (style == "Heading3" || style == "3") prefix = "### ";
            else if (p.SelectSingleNode("w:pPr/w:numPr", ns) != null) prefix = "- ";
            sb.Append(prefix).Append(text).Append('\n');
        }

        private static string ParagraphText(XmlNode p, XmlNamespaceManager ns)
        {
            StringBuilder sb = new StringBuilder();
            XmlNodeList nodes = p.SelectNodes(".//w:t | .//w:tab | .//w:br | .//w:cr", ns);
            if (nodes == null) return "";
            foreach (XmlNode n in nodes)
            {
                if (n.LocalName == "t") sb.Append(n.InnerText);
                else if (n.LocalName == "tab") sb.Append('\t');
                else sb.Append('\n');
            }
            return sb.ToString();
        }

        private static void AppendTable(XmlNode table, StringBuilder sb, XmlNamespaceManager ns)
        {
            sb.Append("[表格]\n");
            XmlNodeList rows = table.SelectNodes("w:tr", ns);
            if (rows != null)
            {
                foreach (XmlNode tr in rows)
                {
                    List<string> cells = new List<string>();
                    XmlNodeList tcs = tr.SelectNodes("w:tc", ns);
                    if (tcs != null)
                    {
                        foreach (XmlNode tc in tcs) cells.Add(CellTextBlock(tc, ns));
                    }
                    sb.Append(string.Join("\t", cells.ToArray())).Append('\n');
                }
            }
            sb.Append('\n');
        }

        private static string CellTextBlock(XmlNode tc, XmlNamespaceManager ns)
        {
            StringBuilder sb = new StringBuilder();
            XmlNodeList ps = tc.SelectNodes(".//w:p", ns);
            if (ps != null)
            {
                foreach (XmlNode p in ps)
                {
                    string t = ParagraphText(p, ns).Replace("\r", " ").Replace("\n", " ").Trim();
                    if (t.Length == 0) continue;
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(t);
                }
            }
            return sb.ToString();
        }

        // ================= 写入 xlsx =================

        private static byte[] BuildWorkbook(string content)
        {
            int maxCols;
            List<string[]> rows = ParseTable(content, out maxCols);
            string sheetXml = BuildWorksheetXml(rows);

            string contentTypes = XmlHead +
                "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'>" +
                "<Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/>" +
                "<Default Extension='xml' ContentType='application/xml'/>" +
                "<Override PartName='/xl/workbook.xml' ContentType='application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml'/>" +
                "<Override PartName='/xl/worksheets/sheet1.xml' ContentType='application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml'/>" +
                "</Types>";

            string rootRels = XmlHead +
                "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>" +
                "<Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument' Target='xl/workbook.xml'/>" +
                "</Relationships>";

            string workbook = XmlHead +
                "<workbook xmlns='" + MainNs + "' xmlns:r='" + RelNs + "'>" +
                "<sheets><sheet name='Sheet1' sheetId='1' r:id='rId1'/></sheets>" +
                "</workbook>";

            string workbookRels = XmlHead +
                "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>" +
                "<Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet' Target='worksheets/sheet1.xml'/>" +
                "</Relationships>";

            List<KeyValuePair<string, string>> parts = new List<KeyValuePair<string, string>>();
            parts.Add(new KeyValuePair<string, string>("[Content_Types].xml", contentTypes));
            parts.Add(new KeyValuePair<string, string>("_rels/.rels", rootRels));
            parts.Add(new KeyValuePair<string, string>("xl/workbook.xml", workbook));
            parts.Add(new KeyValuePair<string, string>("xl/_rels/workbook.xml.rels", workbookRels));
            parts.Add(new KeyValuePair<string, string>("xl/worksheets/sheet1.xml", sheetXml));
            return ZipParts(parts);
        }

        private static string BuildWorksheetXml(List<string[]> rows)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(XmlHead);
            sb.Append("<worksheet xmlns='").Append(MainNs).Append("'><sheetData>");
            for (int r = 0; r < rows.Count; r++)
            {
                string[] row = rows[r];
                if (row == null) continue;
                int last = row.Length - 1;
                while (last >= 0 && string.IsNullOrEmpty(row[last])) last--;
                if (last < 0) continue;
                sb.Append("<row r='").Append(r + 1).Append("'>");
                for (int c = 0; c <= last; c++)
                {
                    string val = row[c];
                    if (string.IsNullOrEmpty(val)) continue;
                    if (val.Length > MaxCellChars) val = val.Substring(0, MaxCellChars - 1) + "…";
                    string cellRef = ColumnName(c) + (r + 1);
                    if (IsNumber(val))
                        sb.Append("<c r='").Append(cellRef).Append("'><v>").Append(val.Trim()).Append("</v></c>");
                    else
                        sb.Append("<c r='").Append(cellRef).Append("' t='inlineStr'><is><t xml:space='preserve'>")
                          .Append(XmlEscape(CleanXml(val))).Append("</t></is></c>");
                }
                sb.Append("</row>");
            }
            sb.Append("</sheetData></worksheet>");
            return sb.ToString();
        }

        private static List<string[]> ParseTable(string content, out int maxCols)
        {
            maxCols = 0;
            string text = content == null ? "" : content.Replace("\r\n", "\n").Replace('\r', '\n');
            if (text.Trim().Length == 0) return new List<string[]>();

            List<string> lines = new List<string>(text.Split('\n'));
            if (text.IndexOf('\t') < 0 && HasMarkdownTable(lines))
            {
                List<string[]> md = ParseMarkdownTable(lines, out maxCols);
                if (md.Count > 0) return md;
            }

            bool hasTab = false, hasComma = false;
            foreach (string line in lines)
            {
                if (line.IndexOf('\t') >= 0) hasTab = true;
                if (line.IndexOf(',') >= 0) hasComma = true;
            }
            char delim = hasTab ? '\t' : (hasComma ? ',' : '\0');
            List<string[]> rows = ParseDelimited(text, delim);
            foreach (string[] row in rows) if (row.Length > maxCols) maxCols = row.Length;
            if (rows.Count > MaxWriteRows) throw new Exception("表格行数超过上限（" + MaxWriteRows + " 行）");
            if (maxCols > MaxWriteCols) throw new Exception("表格列数超过上限（" + MaxWriteCols + " 列）");
            return rows;
        }

        private static bool HasMarkdownTable(List<string> lines)
        {
            foreach (string line in lines)
            {
                string t = line.Trim();
                if (t.Length >= 3 && t[0] == '|' && t.IndexOf('|', 1) > 0) return true;
            }
            return false;
        }

        private static List<string[]> ParseMarkdownTable(List<string> lines, out int maxCols)
        {
            maxCols = 0;
            List<string[]> rows = new List<string[]>();
            foreach (string line in lines)
            {
                string t = line.Trim();
                if (t.Length < 2 || t[0] != '|') continue;
                if (t.Length > 0 && t[t.Length - 1] == '|') t = t.Substring(1, t.Length - 2);
                else t = t.Substring(1);
                string[] cells = t.Split('|');
                for (int i = 0; i < cells.Length; i++) cells[i] = cells[i].Trim();
                if (IsSeparatorRow(cells)) continue;
                rows.Add(cells);
                if (cells.Length > maxCols) maxCols = cells.Length;
            }
            if (rows.Count > MaxWriteRows) throw new Exception("表格行数超过上限（" + MaxWriteRows + " 行）");
            if (maxCols > MaxWriteCols) throw new Exception("表格列数超过上限（" + MaxWriteCols + " 列）");
            return rows;
        }

        private static bool IsSeparatorRow(string[] cells)
        {
            if (cells.Length == 0) return false;
            foreach (string raw in cells)
            {
                string s = raw.Trim();
                if (s.Length < 3) return false;
                int i = 0, j = s.Length - 1;
                if (s[i] == ':') i++;
                if (j >= i && s[j] == ':') j--;
                if (j - i + 1 < 3) return false;
                for (int k = i; k <= j; k++) if (s[k] != '-') return false;
            }
            return true;
        }

        private static List<string[]> ParseDelimited(string text, char delim)
        {
            List<string[]> rows = new List<string[]>();
            List<string> row = new List<string>();
            StringBuilder cell = new StringBuilder();
            bool quoted = false;
            bool any = false;

            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (quoted)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                        else quoted = false;
                    }
                    else cell.Append(ch);
                    any = true;
                    continue;
                }

                if (ch == '"' && cell.Length == 0) { quoted = true; any = true; continue; }
                if (delim != '\0' && ch == delim) { row.Add(cell.ToString()); cell.Length = 0; any = true; continue; }
                if (ch == '\n')
                {
                    row.Add(cell.ToString());
                    cell.Length = 0;
                    AddRow(rows, row);
                    row = new List<string>();
                    any = false;
                    continue;
                }
                cell.Append(ch);
                any = true;
            }
            if (any || cell.Length > 0 || row.Count > 0)
            {
                row.Add(cell.ToString());
                AddRow(rows, row);
            }
            return rows;
        }

        private static void AddRow(List<string[]> rows, List<string> row)
        {
            bool hasValue = false;
            foreach (string s in row) if (!string.IsNullOrEmpty(s)) { hasValue = true; break; }
            if (hasValue) rows.Add(row.ToArray());
        }

        private static bool IsNumber(string raw)
        {
            string t = raw == null ? "" : raw.Trim();
            if (t.Length == 0) return false;
            if (t[0] == '+') return false;
            if (t.Length > 1 && t[0] == '0' && t[1] != '.') return false;
            double d;
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return false;
            if (double.IsNaN(d) || double.IsInfinity(d)) return false;
            return true;
        }

        private static string ColumnName(int index)
        {
            StringBuilder sb = new StringBuilder();
            int n = index + 1;
            while (n > 0)
            {
                int rem = (n - 1) % 26;
                sb.Insert(0, (char)('A' + rem));
                n = (n - 1) / 26;
            }
            return sb.ToString();
        }

        // ================= 写入 docx =================

        private static byte[] BuildDocument(string content)
        {
            string text = content == null ? "" : content.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = text.Split('\n');
            StringBuilder body = new StringBuilder();
            foreach (string raw in lines)
            {
                string line = raw;
                string style = null;
                if (line.StartsWith("### ")) { style = "Heading3"; line = line.Substring(4); }
                else if (line.StartsWith("## ")) { style = "Heading2"; line = line.Substring(3); }
                else if (line.StartsWith("# ")) { style = "Heading1"; line = line.Substring(2); }

                body.Append("<w:p>");
                if (style != null)
                    body.Append("<w:pPr><w:pStyle w:val='").Append(style).Append("'/></w:pPr>");
                if (line.Length > 0)
                {
                    body.Append("<w:r>");
                    AppendRunText(body, line);
                    body.Append("</w:r>");
                }
                body.Append("</w:p>");
            }

            string document = XmlHead +
                "<w:document xmlns:w='" + WordNs + "'><w:body>" + body +
                "<w:sectPr><w:pgSz w:w='11906' w:h='16838'/>" +
                "<w:pgMar w:top='1440' w:right='1440' w:bottom='1440' w:left='1440' w:header='708' w:footer='708' w:gutter='0'/>" +
                "</w:sectPr></w:body></w:document>";

            string styles = XmlHead +
                "<w:styles xmlns:w='" + WordNs + "'>" +
                "<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii='Microsoft YaHei' w:eastAsia='Microsoft YaHei' w:hAnsi='Microsoft YaHei'/>" +
                "<w:sz w:val='21'/><w:szCs w:val='21'/></w:rPr></w:rPrDefault>" +
                "<w:pPrDefault><w:pPr><w:spacing w:after='120' w:line='276' w:lineRule='auto'/></w:pPr></w:pPrDefault></w:docDefaults>" +
                "<w:style w:type='paragraph' w:default='1' w:styleId='Normal'><w:name w:val='Normal'/><w:qFormat/></w:style>" +
                "<w:style w:type='paragraph' w:styleId='Heading1'><w:name w:val='heading 1'/><w:basedOn w:val='Normal'/><w:next w:val='Normal'/><w:qFormat/>" +
                "<w:pPr><w:keepNext/><w:spacing w:before='240' w:after='120'/><w:outlineLvl w:val='0'/></w:pPr>" +
                "<w:rPr><w:b/><w:sz w:val='32'/><w:szCs w:val='32'/></w:rPr></w:style>" +
                "<w:style w:type='paragraph' w:styleId='Heading2'><w:name w:val='heading 2'/><w:basedOn w:val='Normal'/><w:next w:val='Normal'/><w:qFormat/>" +
                "<w:pPr><w:keepNext/><w:spacing w:before='200' w:after='100'/><w:outlineLvl w:val='1'/></w:pPr>" +
                "<w:rPr><w:b/><w:sz w:val='28'/><w:szCs w:val='28'/></w:rPr></w:style>" +
                "<w:style w:type='paragraph' w:styleId='Heading3'><w:name w:val='heading 3'/><w:basedOn w:val='Normal'/><w:next w:val='Normal'/><w:qFormat/>" +
                "<w:pPr><w:keepNext/><w:spacing w:before='160' w:after='80'/><w:outlineLvl w:val='2'/></w:pPr>" +
                "<w:rPr><w:b/><w:sz w:val='24'/><w:szCs w:val='24'/></w:rPr></w:style>" +
                "</w:styles>";

            string contentTypes = XmlHead +
                "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'>" +
                "<Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/>" +
                "<Default Extension='xml' ContentType='application/xml'/>" +
                "<Override PartName='/word/document.xml' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml'/>" +
                "<Override PartName='/word/styles.xml' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml'/>" +
                "</Types>";

            string rootRels = XmlHead +
                "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>" +
                "<Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument' Target='word/document.xml'/>" +
                "</Relationships>";

            string documentRels = XmlHead +
                "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>" +
                "<Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles' Target='styles.xml'/>" +
                "</Relationships>";

            List<KeyValuePair<string, string>> parts = new List<KeyValuePair<string, string>>();
            parts.Add(new KeyValuePair<string, string>("[Content_Types].xml", contentTypes));
            parts.Add(new KeyValuePair<string, string>("_rels/.rels", rootRels));
            parts.Add(new KeyValuePair<string, string>("word/document.xml", document));
            parts.Add(new KeyValuePair<string, string>("word/_rels/document.xml.rels", documentRels));
            parts.Add(new KeyValuePair<string, string>("word/styles.xml", styles));
            return ZipParts(parts);
        }

        private static void AppendRunText(StringBuilder sb, string line)
        {
            int start = 0;
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] != '\t') continue;
                AppendTextRun(sb, line.Substring(start, i - start));
                sb.Append("<w:tab/>");
                start = i + 1;
            }
            AppendTextRun(sb, line.Substring(start));
        }

        private static void AppendTextRun(StringBuilder sb, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            sb.Append("<w:t xml:space='preserve'>").Append(XmlEscape(CleanXml(text))).Append("</w:t>");
        }

        // ================= ZIP / XML 工具 =================

        private static byte[] ZipParts(List<KeyValuePair<string, string>> parts)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (ZipArchive zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    foreach (KeyValuePair<string, string> part in parts)
                    {
                        ZipArchiveEntry entry = zip.CreateEntry(part.Key, CompressionLevel.Optimal);
                        entry.LastWriteTime = ZipTime;
                        byte[] data = new UTF8Encoding(false).GetBytes(part.Value);
                        using (Stream s = entry.Open()) s.Write(data, 0, data.Length);
                    }
                }
                return ms.ToArray();
            }
        }

        private static void WriteBytesAtomic(string fullPath, byte[] data)
        {
            string dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string tmp = fullPath + ".tmp_" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllBytes(tmp, data);
                if (File.Exists(fullPath))
                {
                    try { File.Replace(tmp, fullPath, null); return; }
                    catch { }
                }
                File.Copy(tmp, fullPath, true);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        private static FileStream OpenRead(string fullPath)
        {
            return new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }

        private static string ReadEntryText(ZipArchive zip, string name)
        {
            ZipArchiveEntry entry = null;
            try { entry = zip.GetEntry(name); } catch { }
            if (entry == null)
            {
                foreach (ZipArchiveEntry e in zip.Entries)
                {
                    if (string.Equals(e.FullName, name, StringComparison.OrdinalIgnoreCase)) { entry = e; break; }
                }
            }
            if (entry == null) return null;
            if (entry.Length > MaxEntryBytes)
                throw new Exception("Office 文件内容过大：" + name);
            using (Stream s = entry.Open())
            using (StreamReader r = new StreamReader(s, Encoding.UTF8, true))
                return r.ReadToEnd();
        }

        private static string ResolvePart(string baseDir, string target)
        {
            string t = (target == null ? "" : target).Replace('\\', '/');
            if (t.StartsWith("/")) return t.TrimStart('/');
            string combined = baseDir.TrimEnd('/') + "/" + t;
            string[] parts = combined.Split('/');
            List<string> stack = new List<string>();
            foreach (string p in parts)
            {
                if (p.Length == 0 || p == ".") continue;
                if (p == "..") { if (stack.Count > 0) stack.RemoveAt(stack.Count - 1); }
                else stack.Add(p);
            }
            return string.Join("/", stack.ToArray());
        }

        private static XmlDocument LoadXml(string xml)
        {
            XmlReaderSettings settings = new XmlReaderSettings();
            settings.DtdProcessing = DtdProcessing.Prohibit;
            settings.XmlResolver = null;
            using (StringReader sr = new StringReader(xml))
            using (XmlReader xr = XmlReader.Create(sr, settings))
            {
                XmlDocument doc = new XmlDocument();
                doc.XmlResolver = null;
                doc.Load(xr);
                return doc;
            }
        }

        private static string Attr(XmlNode node, string name)
        {
            XmlElement e = node as XmlElement;
            return e == null ? "" : e.GetAttribute(name);
        }

        private static string AttrNs(XmlNode node, string name, string ns)
        {
            XmlElement e = node as XmlElement;
            return e == null ? "" : e.GetAttribute(name, ns);
        }

        private static string ChildText(XmlNode parent, string name, XmlNamespaceManager ns)
        {
            if (parent == null) return "";
            XmlNode n = parent.SelectSingleNode("m:" + name, ns);
            return n == null ? "" : n.InnerText;
        }

        private static int ColumnIndex(string cellRef)
        {
            if (string.IsNullOrEmpty(cellRef)) return -1;
            int col = 0;
            int i = 0;
            while (i < cellRef.Length && char.IsLetter(cellRef[i]))
            {
                char ch = char.ToUpperInvariant(cellRef[i]);
                if (ch < 'A' || ch > 'Z') return -1;
                col = col * 26 + (ch - 'A' + 1);
                i++;
            }
            return col > 0 ? col - 1 : -1;
        }

        private static string CleanXml(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length);
            foreach (char ch in s)
            {
                if (ch == '\t' || ch == '\n' || ch == '\r' || ch >= 0x20) sb.Append(ch);
            }
            return sb.ToString();
        }

        private static string XmlEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
