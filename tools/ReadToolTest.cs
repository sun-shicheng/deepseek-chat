using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using DsChat;

/// <summary>
/// read_file 分页 + 重复读取去重的回归测试（不联网）。
/// csc /target:exe /out:ReadToolTest.exe /r:Lumi.exe tools\ReadToolTest.cs
/// </summary>
internal static class ReadToolTest
{
    [STAThread]
    private static void Main()
    {
        string home = Path.Combine(Path.GetTempPath(), "ds_read_home_" + Guid.NewGuid().ToString("N"));
        string ws = Path.Combine(Path.GetTempPath(), "ds_read_ws_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(ws);
        Environment.SetEnvironmentVariable("DSCHAT_HOME", home);

        int code = 0;
        try
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 1; i <= 1000; i++) sb.Append("line ").Append(i).Append('\n');
            Workspace.WriteText(ws, "big.txt", sb.ToString());

            Application.EnableVisualStyles();
            Config cfg = new Config();
            cfg.Workspace = ws;
            cfg.EnableTools = true;
            cfg.ConfirmWrites = false;

            using (ChatForm f = new ChatForm(cfg))
            {
                MethodInfo mi = typeof(ChatForm).GetMethod("ExecuteTool", BindingFlags.NonPublic | BindingFlags.Instance);

                object[] args = new object[] { ReadCall("big.txt", 201, 10), null };
                string result = (string)mi.Invoke(f, args);
                string summary = (string)args[1];
                Console.WriteLine("summary=" + summary);
                Console.WriteLine("page len=" + result.Length);
                if (result.IndexOf("line 201") < 0 || result.IndexOf("line 211") >= 0) throw new Exception("pagination body wrong");
                if (result.IndexOf("第 201-210 行") < 0) throw new Exception("page header wrong: " + result.Substring(0, Math.Min(80, result.Length)));
                if (result.IndexOf("start_line=211") < 0) throw new Exception("page footer wrong");
                if (result.Length > 6500) throw new Exception("page too long: " + result.Length);

                // 同样的页再读一次：命中缓存（短结果直接复用）
                object[] args2 = new object[] { ReadCall("big.txt", 201, 10), null };
                string again = (string)mi.Invoke(f, args2);
                Console.WriteLine("dedup summary=" + args2[1]);
                if (((string)args2[1]).IndexOf("与上次相同") < 0) throw new Exception("duplicate read did not hit cache");

                // 大页再读一次：结果较长，用省略提示代替全文
                object[] wide1 = new object[] { ReadCall("big.txt", 1, 400), null };
                string wide = (string)mi.Invoke(f, wide1);
                Console.WriteLine("wide page len=" + wide.Length);
                if (wide.Length > 6500) throw new Exception("page not capped: " + wide.Length);
                if (wide.IndexOf("还有") < 0) throw new Exception("truncation hint missing");
                object[] wide2 = new object[] { ReadCall("big.txt", 1, 400), null };
                string wideAgain = (string)mi.Invoke(f, wide2);
                Console.WriteLine("wide dedup=" + wideAgain);
                if (wideAgain.IndexOf("结果已省略") < 0) throw new Exception("large duplicate read was not skipped");

                // 文件变化后必须重新读取，不能命中旧缓存
                File.AppendAllText(Path.Combine(ws, "big.txt"), "changed\n", new UTF8Encoding(false));
                object[] args3 = new object[] { ReadCall("big.txt", 201, 10), null };
                string fresh = (string)mi.Invoke(f, args3);
                if (fresh.IndexOf("line 201") < 0) throw new Exception("cache did not invalidate after file change");
            }
            Console.WriteLine("READ TOOL OK");
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

    private static ToolCall ReadCall(string path, int start, int maxLines)
    {
        ToolCall tc = new ToolCall();
        tc.Id = "call_read";
        tc.Name = "read_file";
        tc.Arguments = "{\"path\":\"" + path + "\",\"start_line\":" + start + ",\"max_lines\":" + maxLines + "}";
        return tc;
    }
}
