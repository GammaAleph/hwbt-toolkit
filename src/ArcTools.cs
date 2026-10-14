using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Hwbt
{
    public static class ArcTools
    {
        public static bool IsTexture(ArcEntry e) { return e.Type == ArcBank.T_Texture; }

        // Stable, unique file base name (without extension) for each texture entry of a bank.
        public static Dictionary<ArcEntry, string> TextureFileNames(ArcBank b)
        {
            var result = new Dictionary<ArcEntry, string>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in b.Entries)
            {
                if (!IsTexture(e)) continue;
                string baseName = Path.GetFileNameWithoutExtension(e.Name ?? "");
                var sb = new StringBuilder();
                foreach (char c in baseName) sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
                baseName = sb.Length > 0 ? sb.ToString() : "tex" + e.Index;
                string name = baseName;
                if (!used.Add(name)) { name = baseName + "~" + e.Index; used.Add(name); }
                result[e] = name;
            }
            return result;
        }

        public static readonly string[] ImageExts = { ".dds", ".png", ".bmp", ".jpg" };

        public static bool IsImage(string file)
        {
            return Array.IndexOf(ImageExts, Path.GetExtension(file).ToLowerInvariant()) >= 0;
        }

        // format: "dds" (lossless, original compression) or "png" (for image editors).
        public static int ExportTextures(byte[] arc, string outDir, string format)
        {
            var b = ArcBank.Parse(arc);
            int n = 0;
            foreach (var kv in TextureFileNames(b))
            {
                Directory.CreateDirectory(outDir);
                var data = b.GetData(kv.Key);
                if (format == "png")
                    using (var bmp = TexImage.Decode(data)) bmp.Save(Path.Combine(outDir, kv.Value + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                else
                    File.WriteAllBytes(Path.Combine(outDir, kv.Value + ".dds"), Tex.ToDds(data));
                n++;
            }
            return n;
        }

        // Applies every <name>.dds / .png in dir to the matching texture; returns the new bank bytes.
        public static byte[] ImportTextures(byte[] arc, string dir, out int count)
        {
            var b = ArcBank.Parse(arc);
            var byName = new Dictionary<string, ArcEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in TextureFileNames(b)) byName[kv.Value] = kv.Key;
            count = 0;
            foreach (var f in Directory.GetFiles(dir))
            {
                if (!IsImage(f)) continue;
                ArcEntry e;
                if (!byName.TryGetValue(Path.GetFileNameWithoutExtension(f), out e))
                {
                    Archive.Log("  ! no texture named " + Path.GetFileNameWithoutExtension(f) + " in this bank");
                    continue;
                }
                var orig = TexHeader.Read(b.Raw, b.AbsOffset(e));
                byte[] payload = f.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)
                    ? Tex.FromDds(File.ReadAllBytes(f), orig)
                    : TexImage.FromImage(f, orig);
                var nh = TexHeader.Read(payload, 0);
                if (nh.Format != orig.Format || nh.Width != orig.Width || nh.Height != orig.Height || nh.Mips != orig.Mips)
                    Archive.Log(string.Format("  i {0}: {1}x{2} {3} m{4} -> {5}x{6} {7} m{8}", Path.GetFileName(f),
                        orig.Width, orig.Height, orig.FormatName, orig.Mips, nh.Width, nh.Height, nh.FormatName, nh.Mips));
                b.SetData(e, payload);
                count++;
            }
            return b.Raw;
        }
    }
}
