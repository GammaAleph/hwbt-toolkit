// Texture entry <-> PNG. Decodes the top mip level to 32-bit BGRA; PNG import produces an
// uncompressed A8R8G8B8 texture (a format the game already uses) with a box-filtered mip chain.
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace Hwbt
{
    public static class TexImage
    {
        // ---------- decode ----------
        public static Bitmap Decode(byte[] entry)
        {
            var h = TexHeader.Read(entry, 0);
            int w = (int)h.Width, ht = (int)h.Height;
            var px = new byte[w * ht * 4]; // BGRA
            int p = TexHeader.Size;
            uint f = h.Format;

            if (f == Tex.P8)
            {
                int pal = p + 4;
                int idx = p + Tex.PaletteBlock(entry, p);
                for (int i = 0; i < w * ht; i++) Buffer.BlockCopy(entry, pal + entry[idx + i] * 4, px, i * 4, 4);
            }
            else if (Tex.IsBlock(f)) DecodeDxt(entry, p, f, w, ht, px);
            else
            {
                for (int i = 0; i < w * ht; i++)
                {
                    uint b, g, r, a;
                    switch (f)
                    {
                        case Tex.A8R8G8B8: case Tex.X8R8G8B8:
                            b = entry[p + i * 4]; g = entry[p + i * 4 + 1]; r = entry[p + i * 4 + 2];
                            a = f == Tex.X8R8G8B8 ? 255u : entry[p + i * 4 + 3];
                            break;
                        case Tex.A4R4G4B4:
                            { uint v = BitConverter.ToUInt16(entry, p + i * 2); b = (v & 0xf) * 17; g = (v >> 4 & 0xf) * 17; r = (v >> 8 & 0xf) * 17; a = (v >> 12) * 17; }
                            break;
                        case Tex.A1R5G5B5: case Tex.X1R5G5B5:
                            { uint v = BitConverter.ToUInt16(entry, p + i * 2); b = X5(v & 31); g = X5(v >> 5 & 31); r = X5(v >> 10 & 31); a = f == Tex.X1R5G5B5 || (v & 0x8000) != 0 ? 255u : 0u; }
                            break;
                        case Tex.R5G6B5:
                            { uint v = BitConverter.ToUInt16(entry, p + i * 2); b = X5(v & 31); g = X6(v >> 5 & 63); r = X5(v >> 11); a = 255; }
                            break;
                        case Tex.A8: b = g = r = 255; a = entry[p + i]; break;
                        case Tex.L8: b = g = r = entry[p + i]; a = 255; break;
                        case Tex.A8L8: b = g = r = entry[p + i * 2]; a = entry[p + i * 2 + 1]; break;
                        default: throw new InvalidDataException("Cannot decode " + h.FormatName);
                    }
                    px[i * 4] = (byte)b; px[i * 4 + 1] = (byte)g; px[i * 4 + 2] = (byte)r; px[i * 4 + 3] = (byte)a;
                }
            }
            return ToBitmap(px, w, ht);
        }

        static uint X5(uint v) { return v << 3 | v >> 2; }
        static uint X6(uint v) { return v << 2 | v >> 4; }

        static void DecodeDxt(byte[] d, int p, uint f, int w, int h, byte[] px)
        {
            int bw = Math.Max(1, (w + 3) / 4), bh = Math.Max(1, (h + 3) / 4);
            int blockSize = f == Tex.DXT1 ? 8 : 16;
            var col = new uint[4, 4];
            var alpha = new byte[16];
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++)
                {
                    int b = p + (by * bw + bx) * blockSize;
                    int c = b;
                    if (f == Tex.DXT3)
                    {
                        for (int i = 0; i < 16; i++) { int nib = (d[b + i / 2] >> (i % 2 * 4)) & 0xf; alpha[i] = (byte)(nib * 17); }
                        c = b + 8;
                    }
                    else if (f == Tex.DXT5)
                    {
                        int a0 = d[b], a1 = d[b + 1];
                        var lut = new int[8];
                        lut[0] = a0; lut[1] = a1;
                        if (a0 > a1) for (int i = 1; i < 7; i++) lut[i + 1] = ((7 - i) * a0 + i * a1) / 7;
                        else { for (int i = 1; i < 5; i++) lut[i + 1] = ((5 - i) * a0 + i * a1) / 5; lut[6] = 0; lut[7] = 255; }
                        ulong bits = 0;
                        for (int i = 0; i < 6; i++) bits |= (ulong)d[b + 2 + i] << (8 * i);
                        for (int i = 0; i < 16; i++) alpha[i] = (byte)lut[(int)(bits >> (3 * i) & 7)];
                        c = b + 8;
                    }
                    else for (int i = 0; i < 16; i++) alpha[i] = 255;

                    uint c0 = BitConverter.ToUInt16(d, c), c1 = BitConverter.ToUInt16(d, c + 2);
                    var pal = new uint[4][];
                    pal[0] = Rgb565(c0); pal[1] = Rgb565(c1);
                    bool fourColor = c0 > c1 || f != Tex.DXT1;
                    pal[2] = new uint[3]; pal[3] = new uint[3];
                    for (int k = 0; k < 3; k++)
                    {
                        if (fourColor) { pal[2][k] = (2 * pal[0][k] + pal[1][k]) / 3; pal[3][k] = (pal[0][k] + 2 * pal[1][k]) / 3; }
                        else { pal[2][k] = (pal[0][k] + pal[1][k]) / 2; pal[3][k] = 0; }
                    }
                    uint idx = BitConverter.ToUInt32(d, c + 4);
                    for (int i = 0; i < 16; i++)
                    {
                        int x = bx * 4 + i % 4, y = by * 4 + i / 4;
                        if (x >= w || y >= h) continue;
                        int s = (int)(idx >> (2 * i) & 3);
                        int o = (y * w + x) * 4;
                        px[o] = (byte)pal[s][2]; px[o + 1] = (byte)pal[s][1]; px[o + 2] = (byte)pal[s][0];
                        px[o + 3] = (!fourColor && s == 3) ? (byte)0 : alpha[i];
                    }
                }
        }

        static uint[] Rgb565(uint v) { return new[] { X5(v >> 11), X6(v >> 5 & 63), X5(v & 31) }; }

        static Bitmap ToBitmap(byte[] bgra, int w, int h)
        {
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < h; y++) Marshal.Copy(bgra, y * w * 4, data.Scan0 + y * data.Stride, w * 4);
            bmp.UnlockBits(data);
            return bmp;
        }

        // ---------- encode ----------
        // PNG/BMP/... -> A8R8G8B8 entry payload with as many mip levels as the original had.
        public static byte[] FromImage(string file, TexHeader original)
        {
            using (var src = new Bitmap(file))
            {
                int w = src.Width, h = src.Height;
                uint mips = original.Mips <= 1 ? 1u : (uint)Math.Min(original.Mips, MaxMips(w, h));
                byte[] level = ReadBgra(src);
                var ms = new MemoryStream();
                var hdr = new TexHeader { Width = (uint)w, Height = (uint)h, Mips = mips, Hash = original.Hash, Format = Tex.A8R8G8B8 };
                var hb = new byte[TexHeader.Size];
                hdr.Write(hb, 0);
                ms.Write(hb, 0, hb.Length);
                for (int m = 0; m < mips; m++)
                {
                    ms.Write(level, 0, level.Length);
                    if (m + 1 < mips) { level = Half(level, w, h); w = Math.Max(1, w / 2); h = Math.Max(1, h / 2); }
                }
                return ms.ToArray();
            }
        }

        static int MaxMips(int w, int h) { int n = 1; while (w > 1 || h > 1) { w = Math.Max(1, w / 2); h = Math.Max(1, h / 2); n++; } return n; }

        static byte[] ReadBgra(Bitmap src)
        {
            int w = src.Width, h = src.Height;
            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp)) { g.DrawImage(src, new Rectangle(0, 0, w, h)); }
                var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                var px = new byte[w * h * 4];
                for (int y = 0; y < h; y++) Marshal.Copy(data.Scan0 + y * data.Stride, px, y * w * 4, w * 4);
                bmp.UnlockBits(data);
                return px;
            }
        }

        static byte[] Half(byte[] px, int w, int h)
        {
            int nw = Math.Max(1, w / 2), nh = Math.Max(1, h / 2);
            var o = new byte[nw * nh * 4];
            for (int y = 0; y < nh; y++)
                for (int x = 0; x < nw; x++)
                    for (int c = 0; c < 4; c++)
                    {
                        int sx = Math.Min(w - 1, x * 2), sy = Math.Min(h - 1, y * 2);
                        int sx1 = Math.Min(w - 1, sx + 1), sy1 = Math.Min(h - 1, sy + 1);
                        int sum = px[(sy * w + sx) * 4 + c] + px[(sy * w + sx1) * 4 + c] + px[(sy1 * w + sx) * 4 + c] + px[(sy1 * w + sx1) * 4 + c];
                        o[(y * nw + x) * 4 + c] = (byte)((sum + 2) / 4);
                    }
            return o;
        }
    }
}
