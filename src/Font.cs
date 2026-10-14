// Bitmap fonts stored in arcbanks (entry type 0x0C; *_FONTSARCHIVE.ARC, GLOBALMENUASSETS*.ARC).
// Layout (verified on all 78 fonts of the PC release):
//   u16 glyphCount, u16 lineHeight, u16 unk1 (1), u16 unk2 (0xFFFF)
//   glyph[glyphCount], 28 bytes: u32 0, u32 width, f32 u0, f32 u1, f32 v0, f32 v1, u32 0   (UVs into the atlas)
//   u8 advance[glyphCount][glyphCount]   pen advance in pixels: row = current glyph, column = next glyph
//   u8 charMap[256]                      byte code -> glyph index; 0xFE = space, 0xFF = no glyph
//   texture (standard 20-byte texture header + pixel data): the glyph atlas
//
// Text form (font.txt) + atlas.png: see ToText(); the advance matrix is written as a default per glyph
// (the most common value of its row) plus "kern" lines for the pairs that differ, so it stays editable.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Hwbt
{
    public class FontGlyph
    {
        public uint Pad0, Pad1;   // always 0 in the retail data, preserved anyway
        public uint Width;
        public float U0, U1, V0, V1;
        public List<int> Codes = new List<int>();
    }

    public class GameFont
    {
        public const byte TypeFont = 0x0C;
        public const int MaxGlyphs = 0xFD; // 0xFE / 0xFF are reserved in the char map

        public ushort LineHeight, Unk1, Unk2;
        public List<FontGlyph> Glyphs = new List<FontGlyph>();
        public byte[,] Advance;
        public List<int> SpaceCodes = new List<int>();
        public byte[] Texture;     // texture payload (header + pixels)

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public TexHeader TexInfo { get { return TexHeader.Read(Texture, 0); } }

        public static GameFont Parse(byte[] d)
        {
            var f = new GameFont();
            int n = BitConverter.ToUInt16(d, 0);
            f.LineHeight = BitConverter.ToUInt16(d, 2);
            f.Unk1 = BitConverter.ToUInt16(d, 4);
            f.Unk2 = BitConverter.ToUInt16(d, 6);
            int p = 8;
            for (int i = 0; i < n; i++, p += 28)
                f.Glyphs.Add(new FontGlyph
                {
                    Pad0 = BitConverter.ToUInt32(d, p), Width = BitConverter.ToUInt32(d, p + 4),
                    U0 = BitConverter.ToSingle(d, p + 8), U1 = BitConverter.ToSingle(d, p + 12),
                    V0 = BitConverter.ToSingle(d, p + 16), V1 = BitConverter.ToSingle(d, p + 20),
                    Pad1 = BitConverter.ToUInt32(d, p + 24),
                });
            f.Advance = new byte[n, n];
            for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) f.Advance[i, j] = d[p++];
            for (int c = 0; c < 256; c++, p++)
            {
                int g = d[p];
                if (g == 0xFE) f.SpaceCodes.Add(c);
                else if (g < n) f.Glyphs[g].Codes.Add(c);
                else if (g != 0xFF) throw new InvalidDataException("Font char map points to glyph " + g + " of " + n);
            }
            f.Texture = new byte[d.Length - p];
            Buffer.BlockCopy(d, p, f.Texture, 0, f.Texture.Length);
            var th = f.TexInfo;
            if (TexHeader.Size + Tex.ChainSize(th.Format, th.Width, th.Height, th.Mips) != f.Texture.Length)
                throw new InvalidDataException("Font atlas size does not match its header");
            return f;
        }

        public byte[] Build()
        {
            int n = Glyphs.Count;
            if (n > MaxGlyphs) throw new InvalidDataException("A font can have at most " + MaxGlyphs + " glyphs (has " + n + ")");
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write((ushort)n); w.Write(LineHeight); w.Write(Unk1); w.Write(Unk2);
            foreach (var g in Glyphs) { w.Write(g.Pad0); w.Write(g.Width); w.Write(g.U0); w.Write(g.U1); w.Write(g.V0); w.Write(g.V1); w.Write(g.Pad1); }
            for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) w.Write(Advance[i, j]);
            var map = Enumerable.Repeat((byte)0xFF, 256).ToArray();
            foreach (int c in SpaceCodes) map[c] = 0xFE;
            for (int i = 0; i < n; i++)
                foreach (int c in Glyphs[i].Codes)
                {
                    if (map[c] != 0xFF) throw new InvalidDataException(string.Format("Character code 0x{0:X2} is assigned twice", c));
                    map[c] = (byte)i;
                }
            w.Write(map);
            w.Write(Texture);
            return ms.ToArray();
        }

        // ---------- text form ----------
        public string ToText(string name, string atlasFile)
        {
            var th = TexInfo;
            int n = Glyphs.Count;
            var enc = Encoding.GetEncoding(1252);
            var sb = new StringBuilder();
            sb.AppendLine("# HWBT Toolkit font: " + name);
            sb.AppendLine("# Edit this file and/or the atlas, then put this folder into mods\\<YourMod>\\<BANK.ARC>\\" + name + ".font\\");
            sb.AppendLine("# Coordinates are pixels in the atlas. Codes are Windows-1252 byte values (the text tables are 8-bit).");
            sb.AppendLine("# advance = how far the pen moves after this glyph; 'kern A B n' overrides it when glyph B follows glyph A.");
            sb.AppendLine("# Delete the atlas file from this folder to keep the original atlas and change only the metrics.");
            sb.AppendLine();
            sb.AppendFormat("lineheight {0}\r\n", LineHeight);
            sb.AppendFormat("header {0} 0x{1:X4}\r\n", Unk1, Unk2);
            sb.AppendFormat("atlas {0}   ; original format {1}, mips={2}\r\n", atlasFile, th.FormatName, th.Mips);
            sb.AppendLine("# atlassize = size of the atlas the glyph coordinates below refer to. If you only upscale the");
            sb.AppendLine("# atlas image (same layout), keep this line as it is and the coordinates are scaled automatically.");
            sb.AppendFormat("atlassize {0} {1}\r\n", th.Width, th.Height);
            sb.AppendFormat("space {0}\r\n", string.Join(" ", SpaceCodes.Select(Hex)));
            sb.AppendLine();
            sb.AppendLine("# glyph <index> <codes,comma separated> <x> <y> <w> <h> <width> <advance>   ; character");

            var defaults = new int[n];
            for (int i = 0; i < n; i++)
                defaults[i] = Enumerable.Range(0, n).Select(j => (int)Advance[i, j]).GroupBy(v => v).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
            for (int i = 0; i < n; i++)
            {
                var g = Glyphs[i];
                string chars = string.Join(" ", g.Codes.Select(c => Printable(enc.GetString(new[] { (byte)c }))));
                sb.AppendFormat(Inv, "glyph {0} {1} {2} {3} {4} {5} {6} {7}", i, g.Codes.Count > 0 ? string.Join(",", g.Codes.Select(Hex)) : "-",
                    Px(g.U0, th.Width), Px(g.V0, th.Height), Px(g.U1 - g.U0, th.Width, g.U0, g.U1), Px(g.V1 - g.V0, th.Height, g.V0, g.V1), g.Width, defaults[i]);
                if (g.Pad0 != 0 || g.Pad1 != 0) sb.AppendFormat(" pad={0},{1}", g.Pad0, g.Pad1);
                sb.Append("   ; ").Append(chars).Append("\r\n");
            }
            sb.AppendLine();
            sb.AppendLine("# kern <glyph> <next glyph> <advance>");
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    if (Advance[i, j] != defaults[i]) sb.AppendFormat("kern {0} {1} {2}\r\n", i, j, Advance[i, j]);
            return sb.ToString();
        }

        static string Hex(int c) { return "0x" + c.ToString("X2"); }

        static string Printable(string s)
        {
            char c = s[0];
            return c < 32 || c == 0x7F ? "\\x" + ((int)c).ToString("X2") : s;
        }

        // Pixel value written so that value / size gives back exactly the same float.
        static string Px(float uv, uint size) { return ((double)uv * size).ToString("R", Inv); }
        static string Px(float span, uint size, float a, float b) // width as (b*size - a*size): exact for power-of-two atlases
        { return ((double)b * size - (double)a * size).ToString("R", Inv); }

        // Parses font.txt. 'original' supplies the texture when the folder has no atlas image.
        public static GameFont FromText(string text, GameFont original, byte[] newTexture)
        {
            var f = new GameFont { LineHeight = original.LineHeight, Unk1 = original.Unk1, Unk2 = original.Unk2, Texture = newTexture ?? original.Texture };
            var th = f.TexInfo;
            var glyphs = new SortedDictionary<int, FontGlyph>();
            var defaults = new Dictionary<int, int>();
            var kerns = new List<int[]>();
            var coords = new Dictionary<int, double[]>();
            double atlasW = th.Width, atlasH = th.Height; // coordinate space of the glyph lines
            int lineNo = 0;
            foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                lineNo++;
                string line = raw;
                int sc = line.IndexOf(';'); if (sc >= 0) line = line.Substring(0, sc);
                line = line.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var t = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                try
                {
                    switch (t[0].ToLowerInvariant())
                    {
                        case "lineheight": f.LineHeight = ushort.Parse(t[1], Inv); break;
                        case "header": f.Unk1 = (ushort)Num(t[1]); f.Unk2 = (ushort)Num(t[2]); break;
                        case "atlas": break;
                        case "atlassize": atlasW = D(t[1]); atlasH = D(t[2]); break;
                        case "space": f.SpaceCodes = t.Skip(1).Select(Code).ToList(); break;
                        case "glyph":
                            {
                                int idx = int.Parse(t[1], Inv);
                                double x = D(t[3]), y = D(t[4]), w = D(t[5]), h = D(t[6]);
                                var g = new FontGlyph
                                {
                                    Codes = t[2] == "-" ? new List<int>() : t[2].Split(',').Select(Code).ToList(),
                                    Width = uint.Parse(t[7], Inv),
                                };
                                coords[idx] = new[] { x, y, w, h };
                                defaults[idx] = int.Parse(t[8], Inv);
                                foreach (var extra in t.Skip(9))
                                    if (extra.StartsWith("pad="))
                                    {
                                        var pv = extra.Substring(4).Split(',');
                                        g.Pad0 = uint.Parse(pv[0], Inv); g.Pad1 = uint.Parse(pv[1], Inv);
                                    }
                                if (glyphs.ContainsKey(idx)) throw new InvalidDataException("glyph " + idx + " is defined twice");
                                glyphs[idx] = g;
                                break;
                            }
                        case "kern": kerns.Add(new[] { int.Parse(t[1], Inv), int.Parse(t[2], Inv), int.Parse(t[3], Inv) }); break;
                        default: throw new InvalidDataException("unknown keyword '" + t[0] + "'");
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException("font.txt line " + lineNo + ": " + raw.Trim() + " (" + ex.Message + ")");
                }
            }
            int n = glyphs.Count;
            if (n == 0) throw new InvalidDataException("font.txt defines no glyphs");
            if (glyphs.Keys.First() != 0 || glyphs.Keys.Last() != n - 1) throw new InvalidDataException("glyph indices must be 0.." + (n - 1) + " without gaps");
            // UVs: pixel coordinates in the atlassize space -> 0..1 (an upscaled atlas keeps the same layout)
            foreach (var kv in glyphs)
            {
                var c = coords[kv.Key];
                kv.Value.U0 = (float)(c[0] / atlasW); kv.Value.U1 = (float)((c[0] + c[2]) / atlasW);
                kv.Value.V0 = (float)(c[1] / atlasH); kv.Value.V1 = (float)((c[1] + c[3]) / atlasH);
            }
            f.Glyphs = glyphs.Values.ToList();
            f.Advance = new byte[n, n];
            for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) f.Advance[i, j] = (byte)defaults[i];
            foreach (var k in kerns)
            {
                if (k[0] >= n || k[1] >= n) throw new InvalidDataException(string.Format("kern {0} {1}: no such glyph", k[0], k[1]));
                f.Advance[k[0], k[1]] = (byte)k[2];
            }
            return f;
        }

        static double D(string s) { return double.Parse(s, Inv); }

        static int Num(string s)
        {
            s = s.Trim();
            int v = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? int.Parse(s.Substring(2), NumberStyles.HexNumber, Inv) : int.Parse(s, Inv);
            if (v < 0 || v > 0xFFFF) throw new InvalidDataException("value out of range: " + s);
            return v;
        }

        static int Code(string s)
        {
            int v = Num(s);
            if (v > 0xFF) throw new InvalidDataException("character code must be 0x00..0xFF: " + s);
            return v;
        }
    }
}
