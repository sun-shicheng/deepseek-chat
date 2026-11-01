using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using DsChat;

/// <summary>通过 ChatForm 的 write_file 工具生成 xlsx/docx 的回归测试。</summary>
internal static class OfficeToolTest
{
    [STAThread]
    private static void Main()
    {
        string home = Path.Combine(Path.GetTempPath(), "ds_office_tool_home");
        string ws = Path.Combine(Path.GetTempPath(), "ds_office_tool_ws");
        if (Directory.Exists(home)) Directory.Delete(home, true);
        if (Directory.Exists(ws)) Directory.Delete(ws, true);
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(ws);
        Environment.SetEnvironmentVariable("DSCHAT_HOME", home);

        int code = 0;
        try
        {
            Application.EnableVisualStyles();
            Config cfg = new Config();
            cfg.Workspace = ws;
            cfg.EnableTools = true;
            cfg.ConfirmWrites = false;

            using (ChatForm f = new ChatForm(cfg))
            {
                MethodInfo mi = typeof(ChatForm).GetMethod("ExecuteTool", BindingFlags.NonPublic | BindingFlags.Instance);

                object[] xlsxArgs = new object[] { WriteCall("ai.xlsx", "a,b\n1,2\n"), null };
                string xlsxResult = (string)mi.Invoke(f, xlsxArgs);
                Console.WriteLine("xlsx summary=" + xlsxArgs[1]);
                Console.WriteLine("xlsx result=" + xlsxResult);
                if (((string)xlsxArgs[1]).IndexOf("表格") < 0) throw new Exception("xlsx summary missing 表格");
                string xlsxText = Workspace.ReadText(ws, "ai.xlsx");
                if (xlsxText.IndexOf("a\tb") < 0) throw new Exception("xlsx content wrong");

                object[] again = new object[] { WriteCall("ai.xlsx", "a,b\n1,2\n"), null };
                string againResult = (string)mi.Invoke(f, again);
                Console.WriteLine("xlsx repeat=" + againResult);
                if (againResult.IndexOf("无需重复写入") < 0) throw new Exception("xlsx SameText path not used");

                object[] docxArgs = new object[] { WriteCall("ai.docx", "# 标题\n正文\n"), null };
                string docxResult = (string)mi.Invoke(f, docxArgs);
                Console.WriteLine("docx summary=" + docxArgs[1]);
                Console.WriteLine("docx result=" + docxResult);
                if (((string)docxArgs[1]).IndexOf("文档") < 0) throw new Exception("docx summary missing 文档");
                string docxText = Workspace.ReadText(ws, "ai.docx");
                if (docxText.IndexOf("标题") < 0) throw new Exception("docx content wrong");
            }
            Console.WriteLine("OFFICE TOOL OK");
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL: " + ex.Message);
            code = 1;
        }
        finally
        {
            try { Directory.Delete(ws, true); } catch { }
            try { Directory.Delete(home, true); } catch { }
        }
        if (code != 0) Environment.Exit(code);
    }

    private static ToolCall WriteCall(string path, string content)
    {
        ToolCall tc = new ToolCall();
        tc.Id = "call_write";
        tc.Name = "write_file";
        tc.Arguments = "{\"path\":\"" + path.Replace("\\", "\\\\") + "\",\"content\":\""
            + content.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n")
            + "\"}";
        return tc;
    }
}
