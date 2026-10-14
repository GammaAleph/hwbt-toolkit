// Encoders for the texture formats used by the game: DXT1/DXT3/DXT5 (block compression) and the
// uncompressed D3D9 formats. Used when a PNG/TGA is imported, so a texture keeps its original format
// and mip-map count instead of becoming a 4-8x larger A8R8G8B8 texture.
using System;
using System.IO;
using System.Linq;

namespace Hwbt
{
    public static class TexEncode
    {
        // Encodes a full mip chain (BGRA top level) into the given format; returns pixel data without header.
        public static byte[] EncodeChain(byte[] bgra, int w, int h, uint fmt, int mips)
        {
            var ms = new MemoryStream();
            for (int m = 0; m < mips; m++)
            {
                byte[] level = Encode(bgra, w, h, fmt);
                ms.Write(level, 0, level.Length);
                if (m + 1 < mips) { bgra = TexImage.Half(bgra, w, h); w = Math.Max(1, w / 2); h = Math.Max(1, h / 2); }
            }
            return ms.ToArray();
        }

        public static bool CanEncode(uint f)
        {
            return Tex.IsBlock(f) || (f != Tex.P8 && Tex.Bpp(f) > 0);
        }

        public static byte[] Encode(byte[] px, int w, int h, uint f)
        {
            if (Tex.IsBlock(f)) return EncodeDxt(px, w, h, f);
            int n = w * h;
            var o = new byte[n * Tex.Bpp(f) / 8];
            for (int i = 0; i < n; i++)
            {
                int b = px[i * 4], g = px[i * 4 + 1], r = px[i * 4 + 2], a = px[i * 4 + 3];
                switch (f)
                {
                    case Tex.A8R8G8B8: Buffer.BlockCopy(px, i * 4, o, i * 4, 4); break;
                    case Tex.X8R8G8B8: o[i * 4] = (byte)b; o[i * 4 + 1] = (byte)g; o[i * 4 + 2] = (byte)r; o[i * 4 + 3] = 255; break;
                    case Tex.A4R4G4B4: Put16(o, i, Q(a, 15) << 12 | Q(r, 15) << 8 | Q(g, 15) << 4 | Q(b, 15)); break;
                    case Tex.A1R5G5B5: Put16(o, i, (a >= 128 ? 0x8000 : 0) | Q(r, 31) << 10 | Q(g, 31) << 5 | Q(b, 31)); break;
                    case Tex.X1R5G5B5: Put16(o, i, 0x8000 | Q(r, 31) << 10 | Q(g, 31) << 5 | Q(b, 31)); break;
                    case Tex.R5G6B5: Put16(o, i, Q(r, 31) << 11 | Q(g, 63) << 5 | Q(b, 31)); break;
                    case Tex.A8: o[i] = (byte)a; break;
                    case Tex.L8: o[i] = (byte)((r * 77 + g * 151 + b * 28 + 128) >> 8); break;
                    case Tex.A8L8: o[i * 2] = (byte)((r * 77 + g * 151 + b * 28 + 128) >> 8); o[i * 2 + 1] = (byte)a; break;
                    default: throw new InvalidDataException("Cannot encode " + Tex.FormatName(f));
                }
            }
            return o;
        }

        static int Q(int v, int max) { return (v * max + 127) / 255; }
        static void Put16(byte[] o, int i, int v) { o[i * 2] = (byte)v; o[i * 2 + 1] = (byte)(v >> 8); }

        // ---------- DXT ----------
        static byte[] EncodeDxt(byte[] px, int w, int h, uint f)
        {
            int bw = Math.Max(1, (w + 3) / 4), bh = Math.Max(1, (h + 3) / 4);
            int bs = f == Tex.DXT1 ? 8 : 16;
            var o = new byte[bw * bh * bs];
            var blk = new int[16 * 4]; // b,g,r,a per pixel
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++)
                {
                    for (int i = 0; i < 16; i++)
                    {
                        int x = Math.Min(w - 1, bx * 4 + i % 4), y = Math.Min(h - 1, by * 4 + i / 4);
                        int s = (y * w + x) * 4;
                        blk[i * 4] = px[s]; blk[i * 4 + 1] = px[s + 1]; blk[i * 4 + 2] = px[s + 2]; blk[i * 4 + 3] = px[s + 3];
                    }
                    int at = (by * bw + bx) * bs;
                    if (f == Tex.DXT1)
                    {
                        bool punch = false;
                        for (int i = 0; i < 16; i++) if (blk[i * 4 + 3] < 128) punch = true;
                        ColorBlock(blk, punch, o, at);
                    }
                    else
                    {
                        if (f == Tex.DXT3) AlphaDxt3(blk, o, at); else AlphaDxt5(blk, o, at);
                        ColorBlock(blk, false, o, at + 8);
                    }
                }
            return o;
        }

        static void AlphaDxt3(int[] blk, byte[] o, int at)
        {
            for (int i = 0; i < 16; i += 2)
                o[at + i / 2] = (byte)(Q(blk[i * 4 + 3], 15) | Q(blk[(i + 1) * 4 + 3], 15) << 4);
        }

        static void AlphaDxt5(int[] blk, byte[] o, int at)
        {
            int lo = 255, hi = 0;
            for (int i = 0; i < 16; i++) { int a = blk[i * 4 + 3]; lo = Math.Min(lo, a); hi = Math.Max(hi, a); }
            o[at] = (byte)hi; o[at + 1] = (byte)lo;
            var lut = new int[8];
            lut[0] = hi; lut[1] = lo;
            for (int i = 1; i < 7; i++) lut[i + 1] = ((7 - i) * hi + i * lo) / 7;
            ulong bits = 0;
            if (hi != lo)
                for (int i = 0; i < 16; i++)
                {
                    int a = blk[i * 4 + 3], best = 0, bestErr = int.MaxValue;
                    for (int k = 0; k < 8; k++) { int e = Math.Abs(lut[k] - a); if (e < bestErr) { bestErr = e; best = k; } }
                    bits |= (ulong)best << (3 * i);
                }
            for (int i = 0; i < 6; i++) o[at + 2 + i] = (byte)(bits >> (8 * i));
        }

        // Colour part of a DXT block: principal-axis endpoints, nearest-index assignment, one least-squares refinement.
        static void ColorBlock(int[] blk, bool punch, byte[] o, int at)
        {
            // pixels that matter (opaque ones in punch-through mode)
            var use = new bool[16];
            int cnt = 0;
            double mr = 0, mg = 0, mb = 0;
            for (int i = 0; i < 16; i++)
            {
                use[i] = !punch || blk[i * 4 + 3] >= 128;
                if (!use[i]) continue;
                cnt++; mb += blk[i * 4]; mg += blk[i * 4 + 1]; mr += blk[i * 4 + 2];
            }
            if (cnt == 0) { WriteColor(o, at, 0, 0xFFFF, 0xFFFFFFFFu); return; } // fully transparent: c0<=c1, all index 3
            mr /= cnt; mg /= cnt; mb /= cnt;

            double crr = 0, crg = 0, crb = 0, cgg = 0, cgb = 0, cbb = 0;
            for (int i = 0; i < 16; i++)
            {
                if (!use[i]) continue;
                double r = blk[i * 4 + 2] - mr, g = blk[i * 4 + 1] - mg, b = blk[i * 4] - mb;
                crr += r * r; crg += r * g; crb += r * b; cgg += g * g; cgb += g * b; cbb += b * b;
            }
            // start the power iteration from the covariance row with the largest norm: unlike a fixed vector
            // or the bounding-box diagonal it is never orthogonal to the spread (e.g. anti-correlated red/blue)
            double[][] rows = { new[] { crr, crg, crb }, new[] { crg, cgg, cgb }, new[] { crb, cgb, cbb } };
            double[] start = rows.OrderByDescending(v => v[0] * v[0] + v[1] * v[1] + v[2] * v[2]).First();
            double ar = start[0], ag = start[1], ab = start[2];
            if (Math.Abs(ar) + Math.Abs(ag) + Math.Abs(ab) < 1e-9) { ar = ag = ab = 1; }
            for (int it = 0; it < 8; it++)
            {
                double nr = crr * ar + crg * ag + crb * ab, ng = crg * ar + cgg * ag + cgb * ab, nb = crb * ar + cgb * ag + cbb * ab;
                double len = Math.Sqrt(nr * nr + ng * ng + nb * nb);
                if (len < 1e-9) break;
                ar = nr / len; ag = ng / len; ab = nb / len;
            }
            double tmin = double.MaxValue, tmax = double.MinValue;
            for (int i = 0; i < 16; i++)
            {
                if (!use[i]) continue;
                double t = (blk[i * 4 + 2] - mr) * ar + (blk[i * 4 + 1] - mg) * ag + (blk[i * 4] - mb) * ab;
                tmin = Math.Min(tmin, t); tmax = Math.Max(tmax, t);
            }
            double inset = (tmax - tmin) / 16;
            tmin += inset; tmax -= inset;
            var e0 = new[] { mr + ar * tmax, mg + ag * tmax, mb + ab * tmax };
            var e1 = new[] { mr + ar * tmin, mg + ag * tmin, mb + ab * tmin };

            int c0 = To565(e0), c1 = To565(e1);
            uint idx; int err = Assign(blk, use, punch, ref c0, ref c1, out idx);

            // second candidate: the two most distant pixels (exact for two-colour blocks)
            {
                int pa = -1, pb = -1, best = -1;
                for (int i = 0; i < 16; i++)
                {
                    if (!use[i]) continue;
                    for (int j = i + 1; j < 16; j++)
                    {
                        if (!use[j]) continue;
                        int dr = blk[i * 4 + 2] - blk[j * 4 + 2], dg = blk[i * 4 + 1] - blk[j * 4 + 1], db = blk[i * 4] - blk[j * 4];
                        int d = dr * dr + dg * dg + db * db;
                        if (d > best) { best = d; pa = i; pb = j; }
                    }
                }
                if (pa >= 0)
                {
                    int b0 = To565(new double[] { blk[pa * 4 + 2], blk[pa * 4 + 1], blk[pa * 4] });
                    int b1 = To565(new double[] { blk[pb * 4 + 2], blk[pb * 4 + 1], blk[pb * 4] });
                    uint bidx; int berr = Assign(blk, use, punch, ref b0, ref b1, out bidx);
                    if (berr < err) { c0 = b0; c1 = b1; idx = bidx; err = berr; }
                }
            }

            // refinement: least squares endpoints for the chosen indices
            double[] r0, r1;
            if (Refine(blk, use, punch, idx, out r0, out r1))
            {
                int d0 = To565(r0), d1 = To565(r1);
                uint idx2; int err2 = Assign(blk, use, punch, ref d0, ref d1, out idx2);
                if (err2 < err) { c0 = d0; c1 = d1; idx = idx2; }
            }
            WriteColor(o, at, c0, c1, idx);
        }

        static int To565(double[] c)
        {
            int r = Clamp((int)Math.Round(c[0] * 31 / 255)), g = Math.Max(0, Math.Min(63, (int)Math.Round(c[1] * 63 / 255))), b = Clamp((int)Math.Round(c[2] * 31 / 255));
            return r << 11 | g << 5 | b;
        }

        static int Clamp(int v) { return Math.Max(0, Math.Min(31, v)); }

        static int[] From565(int c) { int r = c >> 11 & 31, g = c >> 5 & 63, b = c & 31; return new[] { r << 3 | r >> 2, g << 2 | g >> 4, b << 3 | b >> 2 }; }

        // Orders endpoints for the block mode, builds the palette and picks the nearest entry per pixel.
        static int Assign(int[] blk, bool[] use, bool punch, ref int c0, ref int c1, out uint idx)
        {
            if (punch ? c0 > c1 : c0 < c1) { int t = c0; c0 = c1; c1 = t; }
            var p0 = From565(c0); var p1 = From565(c1);
            var pal = new int[4][];
            pal[0] = p0; pal[1] = p1;
            if (!punch && c0 != c1)
            {
                pal[2] = new[] { (2 * p0[0] + p1[0]) / 3, (2 * p0[1] + p1[1]) / 3, (2 * p0[2] + p1[2]) / 3 };
                pal[3] = new[] { (p0[0] + 2 * p1[0]) / 3, (p0[1] + 2 * p1[1]) / 3, (p0[2] + 2 * p1[2]) / 3 };
            }
            else
            {
                pal[2] = new[] { (p0[0] + p1[0]) / 2, (p0[1] + p1[1]) / 2, (p0[2] + p1[2]) / 2 };
                pal[3] = null; // transparent (punch) / unused
            }
            idx = 0; int total = 0;
            for (int i = 0; i < 16; i++)
            {
                int best = 0, bestErr = int.MaxValue;
                if (!use[i]) { best = 3; bestErr = 0; }
                else
                    for (int k = 0; k < 4; k++)
                    {
                        if (pal[k] == null) continue;
                        int dr = pal[k][0] - blk[i * 4 + 2], dg = pal[k][1] - blk[i * 4 + 1], db = pal[k][2] - blk[i * 4];
                        int e = dr * dr + dg * dg + db * db;
                        if (e < bestErr) { bestErr = e; best = k; }
                    }
                idx |= (uint)best << (2 * i);
                total += bestErr;
            }
            return total;
        }

        static bool Refine(int[] blk, bool[] use, bool punch, uint idx, out double[] e0, out double[] e1)
        {
            e0 = e1 = null;
            if (punch) return false; // keep it simple for the 3-colour mode
            double[] wts = { 1, 0, 2.0 / 3, 1.0 / 3 };
            double aa = 0, ab = 0, bb = 0;
            double[] ax = new double[3], bx = new double[3];
            for (int i = 0; i < 16; i++)
            {
                if (!use[i]) continue;
                double a = wts[idx >> (2 * i) & 3], b = 1 - a;
                aa += a * a; ab += a * b; bb += b * b;
                for (int c = 0; c < 3; c++) { double v = blk[i * 4 + 2 - c]; ax[c] += a * v; bx[c] += b * v; }
            }
            double det = aa * bb - ab * ab;
            if (Math.Abs(det) < 1e-9) return false;
            e0 = new double[3]; e1 = new double[3];
            for (int c = 0; c < 3; c++)
            {
                e0[c] = Math.Max(0, Math.Min(255, (ax[c] * bb - bx[c] * ab) / det));
                e1[c] = Math.Max(0, Math.Min(255, (bx[c] * aa - ax[c] * ab) / det));
            }
            return true;
        }

        static void WriteColor(byte[] o, int at, int c0, int c1, uint idx)
        {
            o[at] = (byte)c0; o[at + 1] = (byte)(c0 >> 8);
            o[at + 2] = (byte)c1; o[at + 3] = (byte)(c1 >> 8);
            o[at + 4] = (byte)idx; o[at + 5] = (byte)(idx >> 8); o[at + 6] = (byte)(idx >> 16); o[at + 7] = (byte)(idx >> 24);
        }
    }
}
