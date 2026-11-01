using System;
using System.IO;
using DsChat;

/// <summary>
/// xlsx / docx / py / c 读写回归测试。不依赖 Office，直接调用 Workspace / OfficeFiles。
/// csc /target:exe /out:OfficeFileTest.exe /r:Lumi.exe tools\OfficeFileTest.cs
/// </summary>
internal static class OfficeFileTest
{
    private static void Main(string[] args)
    {
        bool keep = args != null && args.Length > 0 && args[0] == "--keep";
        string root = Path.Combine(Path.GetTempPath(), "ds_office_test");
        if (!keep && Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(root);
        Console.WriteLine("fixture root = " + root);

        try
        {
            TestXlsxCsv(root);
            TestXlsxTsv(root);
            TestXlsxMarkdown(root);
            TestDocx(root);
            TestCodeFiles(root);
            TestListTags(root);
            TestLegacyRejected(root);
            TestFixtures(root);
            Console.WriteLine("OFFICE FILES OK");
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL: " + ex.Message);
            Environment.Exit(1);
        }
    }

    private static void TestXlsxCsv(string root)
    {
        string csv = "姓名,年龄,城市\r\n张三,18,北京\r\n李四,20,\"上海, 中国\"\r\n";
        int n = Workspace.WriteText(root, "people.xlsx", csv);
        if (n != csv.Length) throw new Exception("xlsx write length wrong");

        string text = Workspace.ReadText(root, "people.xlsx");
        Console.WriteLine("xlsx csv ->\n" + text);
        if (text.IndexOf("工作表：Sheet1") < 0) throw new Exception("xlsx sheet header missing");
        if (text.IndexOf("姓名\t年龄\t城市") < 0) throw new Exception("xlsx header row wrong");
        if (text.IndexOf("上海, 中国") < 0) throw new Exception("quoted comma cell lost");
        if (!Workspace.SameText(root, "people.xlsx", csv)) throw new Exception("xlsx SameText should be true");
        if (Workspace.SameText(root, "people.xlsx", csv + "x")) throw new Exception("xlsx SameText should be false");

        int a, b, total;
        string page = Workspace.ReadTextPage(root, "people.xlsx", 1, 2, out a, out b, out total);
        if (a != 1 || b != 2) throw new Exception("xlsx page range wrong: " + a + "-" + b);
        if (page.IndexOf("共 " + total + " 行") < 0) throw new Exception("xlsx page header missing");

        // 带引号换行的单元格，读取时压成一行空格
        string multi = "a,b\n\"line1\nline2\",2\n";
        Workspace.WriteText(root, "multiline.xlsx", multi);
        string mt = Workspace.ReadText(root, "multiline.xlsx");
        if (mt.IndexOf("line1 line2") < 0) throw new Exception("quoted newline cell lost");

        // 空表格和空文档也要能生成并读回
        Workspace.WriteText(root, "empty.xlsx", "");
        string empty = Workspace.ReadText(root, "empty.xlsx");
        if (empty.IndexOf("0 行 × 0 列") < 0) throw new Exception("empty xlsx not readable");
        Workspace.WriteText(root, "empty.docx", "");
        Workspace.ReadText(root, "empty.docx");
    }

    private static void TestXlsxTsv(string root)
    {
        string tsv = "a\tb\n1\t2\n";
        Workspace.WriteText(root, "tsv.xlsx", tsv);
        string text = Workspace.ReadText(root, "tsv.xlsx");
        Console.WriteLine("xlsx tsv ->\n" + text);
        if (text.IndexOf("a\tb") < 0 || text.IndexOf("1\t2") < 0) throw new Exception("tsv xlsx content wrong");
    }

    private static void TestXlsxMarkdown(string root)
    {
        string md = "| 名称 | 数量 |\n| --- | --- |\n| 苹果 | 3 |\n| 香蕉 | 5 |\n";
        Workspace.WriteText(root, "markdown.xlsx", md);
        string text = Workspace.ReadText(root, "markdown.xlsx");
        Console.WriteLine("xlsx markdown ->\n" + text);
        if (text.IndexOf("名称\t数量") < 0) throw new Exception("markdown header wrong");
        if (text.IndexOf("苹果\t3") < 0) throw new Exception("markdown row wrong");
        if (text.IndexOf("| ---") >= 0) throw new Exception("markdown separator was written");
    }

    private static void TestDocx(string root)
    {
        string doc = "# 标题\n\n第一段\t带制表符\n## 二级标题\n正文内容\n";
        Workspace.WriteText(root, "doc.docx", doc);
        string text = Workspace.ReadText(root, "doc.docx");
        Console.WriteLine("docx ->\n" + text);
        if (text.IndexOf("# 标题") < 0) throw new Exception("docx heading 1 lost");
        if (text.IndexOf("## 二级标题") < 0) throw new Exception("docx heading 2 lost");
        if (text.IndexOf("第一段\t带制表符") < 0) throw new Exception("docx tab lost");
        if (text.IndexOf("正文内容") < 0) throw new Exception("docx body lost");
        if (!Workspace.SameText(root, "doc.docx", doc)) throw new Exception("docx SameText should be true");
        if (Workspace.SameText(root, "doc.docx", doc + "x")) throw new Exception("docx SameText should be false");
    }

    private static void TestCodeFiles(string root)
    {
        string py = "def main():\n    print('你好')\n";
        Workspace.WriteText(root, "main.py", py);
        if (Workspace.ReadText(root, "main.py") != py) throw new Exception("py read/write mismatch");

        string c = "#include <stdio.h>\nint main() { return 0; }\n";
        Workspace.WriteText(root, "main.c", c);
        if (Workspace.ReadText(root, "main.c") != c) throw new Exception("c read/write mismatch");
    }

    private static void TestListTags(string root)
    {
        string list = Workspace.ListFiles(root, "");
        Console.WriteLine(list);
        if (list.IndexOf("[表格] people.xlsx") < 0) throw new Exception("xlsx tag missing");
        if (list.IndexOf("[文档] doc.docx") < 0) throw new Exception("docx tag missing");
        if (list.IndexOf("[代码] main.py") < 0) throw new Exception("py tag missing");
    }

    private static void TestLegacyRejected(string root)
    {
        bool threw = false;
        try { Workspace.WriteText(root, "old.doc", "x"); }
        catch (Exception ex)
        {
            threw = true;
            Console.WriteLine("legacy write rejected: " + ex.Message);
        }
        if (!threw) throw new Exception("legacy .doc write was not rejected");

        string fake = Path.Combine(root, "old.xls");
        File.WriteAllBytes(fake, new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0, 0, 0, 0 });
        threw = false;
        try { Workspace.ReadText(root, "old.xls"); }
        catch (Exception ex)
        {
            threw = true;
            Console.WriteLine("legacy read rejected: " + ex.Message);
        }
        if (!threw) throw new Exception("legacy .xls read was not rejected");
    }

    private static void TestFixtures(string root)
    {
        string excel = Path.Combine(root, "excel_fixture.xlsx");
        if (File.Exists(excel))
        {
            string text = Workspace.ReadText(root, "excel_fixture.xlsx");
            Console.WriteLine("Excel fixture ->\n" + text);
            if (text.IndexOf("SharedStr") < 0) throw new Exception("shared strings not read");
            if (text.IndexOf("42") < 0) throw new Exception("number cell not read");
        }

        string word = Path.Combine(root, "word_fixture.docx");
        if (File.Exists(word))
        {
            string text = Workspace.ReadText(root, "word_fixture.docx");
            Console.WriteLine("Word fixture ->\n" + text);
            if (text.IndexOf("表格标题") < 0) throw new Exception("docx table text not read");
            if (text.IndexOf("单元格A") < 0) throw new Exception("docx table cell not read");
            if (text.IndexOf("[表格]") < 0) throw new Exception("docx table marker missing");
        }
    }
}
