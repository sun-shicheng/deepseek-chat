using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DsChat;

internal static class FormShot
{
    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    [STAThread]
    private static void Main()
    {
        try { SetProcessDPIAware(); } catch { }
        Ui.Detect();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        string root = Path.Combine(Path.GetTempPath(), "ds_ws_shot");
        if (!Directory.Exists(root)) Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "示例.txt"), "hello");
        Directory.CreateDirectory(Path.Combine(root, "子目录"));

        Config cfg = Config.Load();
        cfg.Workspace = root;
        cfg.EnableTools = true;
        cfg.ConfirmWrites = true;
        cfg.Dark = false;
        Theme th = Theme.Create(false);

        using (SettingsForm f = new SettingsForm(cfg, th))
        {
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(0, 0);
            f.Show();
            Application.DoEvents();
            Shot(f, "settings.png");
            f.Close();
        }

        using (WorkspaceForm f = new WorkspaceForm(cfg, th, "示例回复内容"))
        {
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(0, 0);
            f.Show();
            Application.DoEvents();
            Shot(f, "workspace.png");
            f.Close();
        }

        using (ChatForm f = new ChatForm(cfg))
        {
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(0, 0);
            f.Show();
            Application.DoEvents();
            Shot(f, "chat.png");
            f.Close();
        }
    }

    private static void Shot(Form f, string name)
    {
        Bitmap bmp = new Bitmap(f.Width, f.Height);
        f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
        string path = Path.Combine(Path.GetTempPath(), name);
        bmp.Save(path, ImageFormat.Png);
        Console.WriteLine(path + " " + bmp.Width + "x" + bmp.Height);
    }
}
