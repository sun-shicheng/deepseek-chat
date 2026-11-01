using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DsChat
{
    internal static class Program
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
            Config cfg = Config.Load();
            ChatForm f = new ChatForm(cfg);
            try
            {
                Icon ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (ico != null) f.Icon = ico;
            }
            catch { }
            Application.Run(f);
        }
    }
}
