using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        public static readonly string[] ImageExts = { ".dds", ".png", ".tga", ".bmp", ".jpg" };

        public static bool IsImage(string file)
        {
            return Array.IndexOf(ImageExts, Path.GetExtension(file).ToLowerInvariant()) >= 0;
        }

        // Image format for an exported texture: the artists' original extension (.tga / .png) is kept unless
        // allPng is set. Banks name textures after their source files ("image001.tga", "16AngelsBodyDA.png").
        public static string ImageExtFor(ArcEntry e, bool allPng)
        {
            if (!allPng && (e.Name ?? "").EndsWith(".tga", StringComparison.OrdinalIgnoreCase)) return ".tga";
            return ".png";
        }

        // Writes the top mip level of a texture entry as .png or .tga.
        public static void SaveImage(byte[] entry, string path)
        {
            if (path.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
            {
                int w, h;
                byte[] px = TexImage.DecodeBgra(entry, out w, out h);
                File.WriteAllBytes(path, Tga.Write(px, w, h));
            }
            else
                using (var bmp = TexImage.Decode(entry)) bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }

        // format: "dds" (lossless, original compression) or "image" (.png / .tga for image editors).
        public static int ExportTextures(byte[] arc, string outDir, string format, bool allPng)
        {
            var b = ArcBank.Parse(arc);
            int n = 0;
            foreach (var kv in TextureFileNames(b))
            {
                Directory.CreateDirectory(outDir);
                var data = b.GetData(kv.Key);
                if (format == "dds")
                    File.WriteAllBytes(Path.Combine(outDir, kv.Value + ".dds"), Tex.ToDds(data));
                else
                    SaveImage(data, Path.Combine(outDir, kv.Value + ImageExtFor(kv.Key, allPng)));
                n++;
            }
            return n;
        }

        // ================= fonts =================

        public static bool IsFont(ArcEntry e) { return e.Type == GameFont.TypeFont; }

        public static Dictionary<ArcEntry, string> FontFileNames(ArcBank b)
        {
            var result = new Dictionary<ArcEntry, string>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in b.Entries)
            {
                if (!IsFont(e)) continue;
                var sb = new StringBuilder();
                foreach (char c in e.Name ?? "") sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
                string name = sb.Length > 0 ? sb.ToString() : "font" + e.Index;
                if (!used.Add(name)) { name = name + "~" + e.Index; used.Add(name); }
                result[e] = name;
            }
            return result;
        }

        // Writes <outDir>\<name>.font\font.txt + atlas.png (+ atlas.dds) for every font of the bank.
        public static int ExportFonts(byte[] arc, string outDir, bool dds)
        {
            var b = ArcBank.Parse(arc);
            int n = 0;
            foreach (var kv in FontFileNames(b))
            {
                ExportFont(GameFont.Parse(b.GetData(kv.Key)), kv.Value, Path.Combine(outDir, kv.Value + ".font"), dds);
                n++;
            }
            return n;
        }

        public static void ExportFont(GameFont font, string name, string dir, bool dds)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "font.txt"), font.ToText(name, "atlas.png"), new UTF8Encoding(true));
            SaveImage(font.Texture, Path.Combine(dir, "atlas.png"));
            if (dds) File.WriteAllBytes(Path.Combine(dir, "atlas.dds"), Tex.ToDds(font.Texture));
        }

        public static bool HasFontFolders(string bankDir)
        {
            return Directory.GetDirectories(bankDir, "*.font", SearchOption.AllDirectories).Length > 0;
        }

        // Applies every <name>.font\ folder (font.txt and/or atlas image) found under dir to the matching font.
        // Without font.txt only the atlas is replaced; without an atlas image only the metrics change.
        public static byte[] ImportFonts(byte[] arc, string dir, out int count)
        {
            var b = ArcBank.Parse(arc);
            var byName = new Dictionary<string, ArcEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in FontFileNames(b)) byName[kv.Value] = kv.Key;
            count = 0;
            foreach (var fd in Directory.GetDirectories(dir, "*.font", SearchOption.AllDirectories))
            {
                string name = Path.GetFileNameWithoutExtension(fd);
                ArcEntry e;
                if (!byName.TryGetValue(name, out e)) { Archive.Log("  ! no font named " + name + " in this bank"); continue; }
                var orig = GameFont.Parse(b.GetData(e));

                // newest atlas image wins (decompile may have written both atlas.png and atlas.dds)
                var atlas = Directory.GetFiles(fd).Where(f => IsImage(f) && Path.GetFileNameWithoutExtension(f).Equals("atlas", StringComparison.OrdinalIgnoreCase))
                                     .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                byte[] tex = null;
                if (atlas != null)
                {
                    var oh = orig.TexInfo;
                    tex = atlas.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) ? Tex.FromDds(File.ReadAllBytes(atlas), oh) : TexImage.FromImage(atlas, oh);
                    var nh = TexHeader.Read(tex, 0);
                    if (nh.Width != oh.Width || nh.Height != oh.Height || nh.Format != oh.Format)
                        Archive.Log(string.Format("  i {0} atlas: {1}x{2} {3} -> {4}x{5} {6}", name, oh.Width, oh.Height, oh.FormatName, nh.Width, nh.Height, nh.FormatName));
                }
                string txt = Path.Combine(fd, "font.txt");
                GameFont font;
                if (File.Exists(txt)) font = GameFont.FromText(File.ReadAllText(txt), orig, tex);
                else if (tex != null) { font = orig; font.Texture = tex; }
                else continue;

                byte[] payload = font.Build();
                if (payload.Length > EntryLimit)
                    throw new InvalidDataException(string.Format("{0}: font is {1:N0} bytes - an arcbank entry can hold at most 16 MB (use a smaller atlas)", name, payload.Length));
                if (font.Glyphs.Count != orig.Glyphs.Count)
                    Archive.Log(string.Format("  i {0}: {1} -> {2} glyphs", name, orig.Glyphs.Count, font.Glyphs.Count));
                b.SetData(e, payload);
                count++;
            }
            return b.Raw;
        }

        const long EntryLimit = 0xFFFFFF;   // 24-bit size field of an arcbank entry

        // Warnings for textures that may be too big for the game (see README "High-resolution textures").
        static void CheckSize(string file, TexHeader orig, TexHeader nh, int payload)
        {
            if (payload > EntryLimit)
                throw new InvalidDataException(string.Format("{0}: {1:N0} bytes - an arcbank entry can hold at most 16 MB. " +
                    "Use a smaller size, or a DXT1 .dds (half the size of DXT3/DXT5).",
                    Path.GetFileName(file), payload));
            if (nh.Width > 4096 || nh.Height > 4096)
                Archive.Log(string.Format("  ! {0}: {1}x{2} is above 4096 - many GPUs/drivers refuse such textures in Direct3D 9.", Path.GetFileName(file), nh.Width, nh.Height));
            else if (nh.Width > 2048 || nh.Height > 2048)
                Archive.Log(string.Format("  ! {0}: {1}x{2} - larger than anything in the retail game; test in-game before using it widely.", Path.GetFileName(file), nh.Width, nh.Height));
            long origBytes = Tex.ChainSize(orig.Format, orig.Width, orig.Height, orig.Mips);
            if (origBytes > 0 && payload > origBytes * 4 && payload > 1 << 20)
                Archive.Log(string.Format("  ! {0}: {1:N1} MB, {2:N0}x the original - the game is a 32-bit program (2 GB of memory); test it in-game before using many of them.",
                    Path.GetFileName(file), payload / 1048576.0, (double)payload / origBytes));
            if (Tex.IsBlock(orig.Format) && !Tex.IsBlock(nh.Format))
                Archive.Log(string.Format("  ! {0}: DXT texture replaced by uncompressed {1} (4-8x more memory).", Path.GetFileName(file), nh.FormatName));
        }

        // Applies every <name>.dds / .png / .tga in dir to the matching texture; returns the new bank bytes.
        // PNG/TGA are encoded into the original format with the original mip count; DDS is taken as-is.
        public static byte[] ImportTextures(byte[] arc, string dir, out int count)
        {
            var b = ArcBank.Parse(arc);
            int originalSize = arc.Length;
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
                CheckSize(f, orig, nh, payload.Length);
                b.SetData(e, payload);
                count++;
            }
            if (b.Raw.Length > originalSize * 2 && b.Raw.Length > 8 << 20)
                Archive.Log(string.Format("  ! bank grew from {0:N1} MB to {1:N1} MB - if the game crashes while loading it, use smaller or compressed textures.",
                    originalSize / 1048576.0, b.Raw.Length / 1048576.0));
            return b.Raw;
        }
    }
}
