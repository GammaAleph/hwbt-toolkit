// .LDA localisation tables (TEXTxxyy.LDA). xx = language, yy = table.
// Layout: 'lda0' (8-bit, cp1252) or 'lda1' (UTF-16LE), u32 fileSize, u32 id0, u32 id1, u32 count,
// u32 offsets[count], u32 stringsSize, then NUL-terminated strings.
// Text form: first line "#HWBT-LDA lda0 <id0> <id1>", then one string per line.
// Escapes: \n \r \t \\  (so multi-line strings stay on one line).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Hwbt
{
    public class Lda
    {
        public string Magic = "lda0";
        public uint Id0, Id1;
        public List<string> Strings = new List<string>();

        bool Wide { get { return Magic == "lda1"; } }
        Encoding Enc { get { return Wide ? Encoding.Unicode : Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ReplacementFallback); } }

        public static Lda Parse(byte[] d)
        {
            var l = new Lda();
            l.Magic = Encoding.ASCII.GetString(d, 0, 4);
            if (l.Magic != "lda0" && l.Magic != "lda1") throw new InvalidDataException("Not an LDA file");
            l.Id0 = BitConverter.ToUInt32(d, 8);
            l.Id1 = BitConverter.ToUInt32(d, 12);
            int n = (int)BitConverter.ToUInt32(d, 16);
            int bufStart = 20 + (n + 1) * 4;
            for (int i = 0; i < n; i++)
            {
                int p = bufStart + (int)BitConverter.ToUInt32(d, 20 + i * 4);
                int end = p;
                if (l.Wide) { while (d[end] != 0 || d[end + 1] != 0) end += 2; }
                else { while (d[end] != 0) end++; }
                l.Strings.Add(l.Enc.GetString(d, p, end - p));
            }
            return l;
        }

        public byte[] Build()
        {
            var buf = new MemoryStream();
            var offs = new List<uint>();
            int term = Wide ? 2 : 1;
            foreach (var s in Strings)
            {
                offs.Add((uint)buf.Length);
                byte[] b;
                try { b = Enc.GetBytes(s); }
                catch (EncoderFallbackException)
                {
                    throw new InvalidDataException("String contains characters not available in the game's 8-bit (Windows-1252) encoding: \"" + s + "\"");
                }
                buf.Write(b, 0, b.Length);
                for (int i = 0; i < term; i++) buf.WriteByte(0);
            }
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(Encoding.ASCII.GetBytes(Magic));
            w.Write(0u); // size, patched below
            w.Write(Id0);
            w.Write(Id1);
            w.Write((uint)Strings.Count);
            foreach (var o in offs) w.Write(o);
            w.Write((uint)buf.Length);
            w.Write(buf.ToArray());
            var outb = ms.ToArray();
            Buffer.BlockCopy(BitConverter.GetBytes((uint)outb.Length), 0, outb, 4, 4);
            return outb;
        }

        public string ToText()
        {
            var sb = new StringBuilder();
            sb.AppendFormat("#HWBT-LDA {0} {1} {2}\r\n", Magic, Id0, Id1);
            foreach (var s in Strings) sb.Append(Escape(s)).Append("\r\n");
            return sb.ToString();
        }

        public static Lda FromText(string text)
        {
            var lines = new List<string>(text.Replace("\r\n", "\n").Split('\n'));
            if (lines.Count > 0 && lines[lines.Count - 1] == "") lines.RemoveAt(lines.Count - 1);
            if (lines.Count == 0 || !lines[0].StartsWith("#HWBT-LDA "))
                throw new InvalidDataException("Missing '#HWBT-LDA' header line");
            var hdr = lines[0].Split(' ');
            var l = new Lda { Magic = hdr[1], Id0 = uint.Parse(hdr[2]), Id1 = uint.Parse(hdr[3]) };
            for (int i = 1; i < lines.Count; i++) l.Strings.Add(Unescape(lines[i]));
            return l;
        }

        static string Escape(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }

        static string Unescape(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char c = s[++i];
                    if (c == 'n') sb.Append('\n');
                    else if (c == 'r') sb.Append('\r');
                    else if (c == 't') sb.Append('\t');
                    else sb.Append(c);
                }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }
    }
}
