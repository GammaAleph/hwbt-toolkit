// Minimal Truevision TGA reader/writer (BGRA). Reads uncompressed and RLE true-colour/greyscale
// images (8/16/24/32 bit); writes uncompressed 32-bit top-left images.
using System;
using System.IO;

namespace Hwbt
{
    public static class Tga
    {
        public static byte[] Write(byte[] bgra, int w, int h)
        {
            var o = new byte[18 + bgra.Length];
            o[2] = 2;                                   // uncompressed true-colour
            o[12] = (byte)w; o[13] = (byte)(w >> 8);
            o[14] = (byte)h; o[15] = (byte)(h >> 8);
            o[16] = 32;
            o[17] = 0x28;                               // 8 alpha bits, top-left origin
            Buffer.BlockCopy(bgra, 0, o, 18, bgra.Length);
            return o;
        }

        public static byte[] Read(byte[] d, out int w, out int h)
        {
            if (d.Length < 18) throw new InvalidDataException("Not a TGA file");
            int idLen = d[0], cmapType = d[1], type = d[2];
            int cmapLen = d[5] | d[6] << 8, cmapBits = d[7];
            w = d[12] | d[13] << 8; h = d[14] | d[15] << 8;
            int bpp = d[16], desc = d[17];
            if (type == 1 || type == 9)
                throw new InvalidDataException("Colour-mapped TGA is not supported - save as 24/32-bit TGA or PNG");
            if (type != 2 && type != 10 && type != 3 && type != 11) throw new InvalidDataException("Unsupported TGA type " + type);
            int bytes = bpp / 8;
            if (bytes < 1 || bytes > 4) throw new InvalidDataException("Unsupported TGA bit depth " + bpp);
            int p = 18 + idLen + (cmapType != 0 ? cmapLen * ((cmapBits + 7) / 8) : 0);
            int n = w * h;
            var px = new byte[n * 4];
            bool rle = type == 10 || type == 11;
            int i = 0;
            while (i < n)
            {
                if (p >= d.Length) throw new InvalidDataException("Truncated TGA");
                int count = 1; bool repeat = false;
                if (rle) { int c = d[p++]; count = (c & 0x7f) + 1; repeat = (c & 0x80) != 0; }
                for (int k = 0; k < count && i < n; k++, i++)
                {
                    int src = p;
                    if (!repeat || k == 0) { if (p + bytes > d.Length) throw new InvalidDataException("Truncated TGA"); }
                    else src = p - bytes;
                    Pixel(d, src, bytes, px, i * 4);
                    if (!repeat || k == 0) p += bytes;
                }
            }
            if ((desc & 0x20) == 0) Flip(px, w, h); // bottom-left origin
            return px;
        }

        static void Pixel(byte[] d, int s, int bytes, byte[] px, int o)
        {
            switch (bytes)
            {
                case 1: px[o] = px[o + 1] = px[o + 2] = d[s]; px[o + 3] = 255; break;
                case 2:
                    int v = d[s] | d[s + 1] << 8;
                    int r = v >> 10 & 31, g = v >> 5 & 31, b = v & 31;
                    px[o] = (byte)(b << 3 | b >> 2); px[o + 1] = (byte)(g << 3 | g >> 2); px[o + 2] = (byte)(r << 3 | r >> 2);
                    px[o + 3] = 255; // the attribute bit is unreliable (many tools write 0), treat as opaque
                    break;
                case 3: px[o] = d[s]; px[o + 1] = d[s + 1]; px[o + 2] = d[s + 2]; px[o + 3] = 255; break;
                default: px[o] = d[s]; px[o + 1] = d[s + 1]; px[o + 2] = d[s + 2]; px[o + 3] = d[s + 3]; break;
            }
        }

        static void Flip(byte[] px, int w, int h)
        {
            var row = new byte[w * 4];
            for (int y = 0; y < h / 2; y++)
            {
                int a = y * w * 4, b = (h - 1 - y) * w * 4;
                Buffer.BlockCopy(px, a, row, 0, row.Length);
                Buffer.BlockCopy(px, b, px, a, row.Length);
                Buffer.BlockCopy(row, 0, px, b, row.Length);
            }
        }
    }
}
