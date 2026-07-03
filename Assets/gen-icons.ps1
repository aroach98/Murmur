# Generates murmur.ico (violet, idle/app icon) and murmur-rec.ico (red, recording
# tray state), plus 256px PNG previews. Multi-size ICO: uncompressed DIB frames for
# 16-64px (GDI+/WinForms-safe), PNG frame for 256px. Rerun after design tweaks.
# All byte-level work is in C# — PowerShell pipelines mangle byte arrays.
Add-Type -ReferencedAssemblies System.Drawing @"
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class MurmurIconGen
{
    public static Bitmap Frame(int size, Color c1, Color c2)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int r = Math.Max(2, (int)(size * 0.22));
            int d = r * 2;
            using (var path = new GraphicsPath())
            {
                path.AddArc(0, 0, d, d, 180, 90);
                path.AddArc(size - d, 0, d, d, 270, 90);
                path.AddArc(size - d, size - d, d, d, 0, 90);
                path.AddArc(0, size - d, d, d, 90, 90);
                path.CloseFigure();
                using (var brush = new LinearGradientBrush(new Point(0, 0), new Point(size, size), c1, c2))
                    g.FillPath(brush, path);
            }

            // Five white sound bars — a quiet waveform ("murmur")
            double[] rel = { 0.30, 0.52, 0.78, 0.52, 0.30 };
            float barW = Math.Max(1.5f, size * 0.085f);
            float gap = size * 0.07f;
            float x = (size - (5 * barW + 4 * gap)) / 2f;
            foreach (double h in rel)
            {
                float barH = Math.Max(2f, (float)(h * size * 0.62));
                float y = (size - barH) / 2f;
                using (var bp = new GraphicsPath())
                {
                    bp.AddArc(x, y, barW, barW, 180, 180);
                    bp.AddArc(x, y + barH - barW, barW, barW, 0, 180);
                    bp.CloseFigure();
                    g.FillPath(Brushes.White, bp);
                }
                x += barW + gap;
            }
        }
        return bmp;
    }

    static byte[] DibFrame(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var pixels = new byte[data.Stride * h];
        System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
        bmp.UnlockBits(data);

        using (var ms = new MemoryStream())
        using (var bw = new BinaryWriter(ms))
        {
            bw.Write(40u); bw.Write(w); bw.Write(h * 2);          // header; height doubled for AND mask
            bw.Write((ushort)1); bw.Write((ushort)32); bw.Write(0u);
            bw.Write((uint)(w * h * 4)); bw.Write(0); bw.Write(0);
            bw.Write(0u); bw.Write(0u);
            for (int row = h - 1; row >= 0; row--)                 // BGRA rows, bottom-up
                bw.Write(pixels, row * data.Stride, w * 4);
            int maskRowBytes = ((w + 31) / 32) * 4;                // empty AND mask (alpha rules)
            bw.Write(new byte[maskRowBytes * h]);
            bw.Flush();
            return ms.ToArray();
        }
    }

    public static void WriteIco(string path, Color c1, Color c2)
    {
        int[] sizes = { 256, 64, 48, 32, 16 };
        var blobs = new List<byte[]>();
        foreach (int s in sizes)
        {
            using (var bmp = Frame(s, c1, c2))
            {
                if (s == 256)
                {
                    using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); blobs.Add(ms.ToArray()); }
                }
                else blobs.Add(DibFrame(bmp));
            }
        }
        using (var fs = File.Create(path))
        using (var w = new BinaryWriter(fs))
        {
            w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                w.Write((byte)(s % 256)); w.Write((byte)(s % 256)); // 0 means 256
                w.Write((byte)0); w.Write((byte)0);
                w.Write((ushort)1); w.Write((ushort)32);
                w.Write((uint)blobs[i].Length); w.Write((uint)offset);
                offset += blobs[i].Length;
            }
            foreach (var b in blobs) w.Write(b);
        }
    }
}
"@

$violet1 = [System.Drawing.Color]::FromArgb(124, 58, 237)   # violet-600
$violet2 = [System.Drawing.Color]::FromArgb(67, 56, 202)    # indigo-700
$red1    = [System.Drawing.Color]::FromArgb(239, 68, 68)    # red-500
$red2    = [System.Drawing.Color]::FromArgb(153, 27, 27)    # red-800

[MurmurIconGen]::WriteIco("$PSScriptRoot\murmur.ico", $violet1, $violet2)
[MurmurIconGen]::WriteIco("$PSScriptRoot\murmur-rec.ico", $red1, $red2)

$p = [MurmurIconGen]::Frame(256, $violet1, $violet2)
$p.Save("$PSScriptRoot\murmur-preview.png", [System.Drawing.Imaging.ImageFormat]::Png); $p.Dispose()
$p = [MurmurIconGen]::Frame(256, $red1, $red2)
$p.Save("$PSScriptRoot\murmur-rec-preview.png", [System.Drawing.Imaging.ImageFormat]::Png); $p.Dispose()

Get-ChildItem $PSScriptRoot\*.ico, $PSScriptRoot\*.png | ForEach-Object { "{0}  {1} bytes" -f $_.Name, $_.Length }
