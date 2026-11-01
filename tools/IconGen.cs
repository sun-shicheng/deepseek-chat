using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using DsChat;

// 生成多尺寸 PNG 压缩 ICO：tools\..\app.ico
internal static class IconGen
{
    private static void Main(string[] args)
    {
        string outPath = args.Length > 0 ? args[0] : "app.ico";
        int[] sizes = new int[] { 16, 24, 32, 48, 64, 128, 256 };
        List<byte[]> pngs = new List<byte[]>();

        foreach (int s in sizes)
        {
            using (Bitmap bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    Brand.Draw(g, new RectangleF(0.5f, 0.5f, s - 1f, s - 1f));
                }

                using (MemoryStream ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    pngs.Add(ms.ToArray());
                }
            }
        }

        using (FileStream fs = new FileStream(outPath, FileMode.Create, FileAccess.Write))
        using (BinaryWriter bw = new BinaryWriter(fs))
        {
            bw.Write((ushort)0);
            bw.Write((ushort)1);
            bw.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                bw.Write((byte)(s >= 256 ? 0 : s));
                bw.Write((byte)(s >= 256 ? 0 : s));
                bw.Write((byte)0);
                bw.Write((byte)0);
                bw.Write((ushort)1);
                bw.Write((ushort)32);
                bw.Write((uint)pngs[i].Length);
                bw.Write((uint)offset);
                offset += pngs[i].Length;
            }
            foreach (byte[] p in pngs) bw.Write(p);
        }
        Console.WriteLine("icon written: " + Path.GetFullPath(outPath) + " " + new FileInfo(outPath).Length + " bytes");
    }
}
