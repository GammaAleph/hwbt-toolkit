// "Unpack & decompile": extracts the archive and then unpacks everything inside the extracted files
// into readable formats:
//   files\                         raw archive files (same as "unpack")
//   decompiled\arc\<BANK.ARC>\     every arcbank entry: textures\*.png (+ .dds), entries\*.bin, index.txt
//   decompiled\texts\*.txt         .LDA string tables
//   decompiled\sounds\<BANK>\*.wav .HDR/.RAW sound banks
//   decompiled\speech\<BANK>\*.ogg .HDR/.SPD speech banks
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Hwbt
{
    public static class Decompiler
    {
        public static void UnpackAll(string dataDir, string outDir, bool dds, bool allPng)
        {
            string files = Path.Combine(outDir, "files");
            Archive.Unpack(dataDir, files, null);
            DecompileFolder(files, Path.Combine(outDir, "decompiled"), dds, allPng);
        }

        public static void DecompileFolder(string filesDir, string outDir, bool dds, bool allPng)
        {
            Directory.CreateDirectory(outDir);
            int texts = 0, sounds = 0, banks = 0, textures = 0, entries = 0, errors = 0;

            // texts
            foreach (var f in Directory.GetFiles(filesDir, "*.LDA"))
            {
                try
                {
                    string dst = Path.Combine(outDir, "texts", Path.GetFileNameWithoutExtension(f) + ".txt");
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    File.WriteAllText(dst, Lda.Parse(File.ReadAllBytes(f)).ToText(), new UTF8Encoding(true));
                    texts++;
                }
                catch (Exception ex) { errors++; Archive.Log("  ! " + Path.GetFileName(f) + ": " + ex.Message); }
            }
            Archive.Log(string.Format("Texts: {0}", texts));

            // sounds
            foreach (var f in Directory.GetFiles(filesDir, "*.HDR"))
            {
                string raw = Path.ChangeExtension(f, ".RAW");
                if (!File.Exists(raw)) continue; // music indexes (.OGG) and speech indexes (.SPD) have no .RAW
                try { sounds += Sound.HdrToWav(File.ReadAllBytes(f), raw, Path.Combine(outDir, "sounds", Path.GetFileNameWithoutExtension(f))); }
                catch (Exception ex) { errors++; Archive.Log("  ! " + Path.GetFileName(f) + ": " + ex.Message); }
            }
            Archive.Log(string.Format("Sounds: {0} .wav", sounds));

            // speech (commentary) banks
            int speech = 0;
            foreach (var f in Directory.GetFiles(filesDir, "*.SPD"))
            {
                string hdr = Path.ChangeExtension(f, ".HDR");
                if (!File.Exists(hdr)) continue;
                try { speech += Sound.SpdToOgg(File.ReadAllBytes(hdr), f, Path.Combine(outDir, "speech", Path.GetFileNameWithoutExtension(f))); }
                catch (Exception ex) { errors++; Archive.Log("  ! " + Path.GetFileName(f) + ": " + ex.Message); }
            }
            Archive.Log(string.Format("Speech: {0} .ogg", speech));

            // arcbanks (parallel - texture decoding dominates)
            var arcs = Directory.GetFiles(filesDir, "*.ARC");
            Parallel.ForEach(arcs, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount) }, f =>
            {
                try
                {
                    int t, e;
                    DecompileBank(File.ReadAllBytes(f), Path.Combine(outDir, "arc", Path.GetFileName(f)), dds, allPng, out t, out e);
                    Interlocked.Add(ref textures, t);
                    Interlocked.Add(ref entries, e);
                }
                catch (Exception ex) { Interlocked.Increment(ref errors); Archive.Log("  ! " + Path.GetFileName(f) + ": " + ex.Message); }
                int n = Interlocked.Increment(ref banks);
                if (n % 100 == 0) Archive.Log(string.Format("  banks {0}/{1} ...", n, arcs.Length));
            });
            Archive.Log(string.Format("Arcbanks: {0} ({1} textures, {2} other entries)", banks, textures, entries));

            File.WriteAllText(Path.Combine(outDir, "README.txt"), Readme(), Encoding.UTF8);
            Archive.Log(string.Format("Decompiled into {0}{1}", outDir, errors > 0 ? " - " + errors + " error(s), see log" : ""));
        }

        // Splits one bank: textures -> PNG (+DDS), every other entry -> raw .bin, plus an index.
        public static void DecompileBank(byte[] arc, string outDir, bool dds, bool allPng, out int textures, out int entries)
        {
            var b = ArcBank.Parse(arc);
            var texNames = ArcTools.TextureFileNames(b);
            textures = entries = 0;
            Directory.CreateDirectory(outDir);
            var index = new StringBuilder();
            index.AppendLine("# HWBT Toolkit - arcbank index");
            index.AppendLine("# idx  type               offset     size  name  [texture info] -> file");

            foreach (var e in b.Entries)
            {
                string file = "";
                string info = "";
                if (ArcTools.IsTexture(e))
                {
                    var data = b.GetData(e);
                    var h = TexHeader.Read(data, 0);
                    info = string.Format("  [{0}x{1} {2} mips={3}]", h.Width, h.Height, h.FormatName, h.Mips);
                    string tdir = Path.Combine(outDir, "textures");
                    Directory.CreateDirectory(tdir);
                    file = "textures\\" + texNames[e] + ArcTools.ImageExtFor(e, allPng);
                    ArcTools.SaveImage(data, Path.Combine(outDir, file));
                    if (dds) File.WriteAllBytes(Path.Combine(tdir, texNames[e] + ".dds"), Tex.ToDds(data));
                    textures++;
                }
                else if (e.Size > 0)
                {
                    string edir = Path.Combine(outDir, "entries");
                    Directory.CreateDirectory(edir);
                    file = "entries\\" + EntryFileName(e);
                    File.WriteAllBytes(Path.Combine(outDir, file), b.GetData(e));
                    entries++;
                }
                index.AppendFormat("{0,5}  {1,-17} 0x{2:X8} {3,8}  {4}{5}{6}\r\n", e.Index, e.TypeName, e.Offset, e.Size, e.Name, info,
                    file.Length > 0 ? " -> " + file : "");
            }
            File.WriteAllText(Path.Combine(outDir, "index.txt"), index.ToString(), Encoding.UTF8);
        }

        static string EntryFileName(ArcEntry e)
        {
            var sb = new StringBuilder();
            foreach (char c in e.Name ?? "") sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 || c == ' ' ? '_' : c);
            string name = sb.ToString();
            if (name.Length > 60) name = name.Substring(0, 60);
            string ext = e.Type == ArcBank.T_EntryNames ? ".names.txt" : ".bin";
            return string.Format("{0:D4}_{1}{2}{3}", e.Index, e.TypeName, name.Length > 0 ? "_" + name : "", ext);
        }

        static string Readme()
        {
            return
@"HWBT Toolkit - decompiled game data

texts\TEXTxxyy.txt           string tables (.LDA), xx = language (00 English, 01 French, 02 German, 03 Italian ...)
                             edit and put into a mod folder to change in-game texts
sounds\<BANK>\*.wav          sound banks (.HDR + .RAW), 16-bit PCM
                             to mod: mods\<YourMod>\<BANK>.HDR\<same name>.wav
speech\<BANK>\*.ogg          commentary speech banks (.HDR + .SPD), one Ogg Vorbis file per take
                             to mod: mods\<YourMod>\<BANK>.HDR\<same name>.ogg
arc\<BANK.ARC>\index.txt     list of every entry of the bank (type, offset, size, name, texture format/mips)
arc\<BANK.ARC>\textures\     textures as PNG/TGA (and DDS if requested); to mod a texture put the edited file into
                             mods\<YourMod>\<BANK.ARC>\<same name>.png|.tga - it is compressed into the format
                             and mip count shown in index.txt
arc\<BANK.ARC>\entries\      all other entries as raw binary (models, meshes, vertex/index buffers, materials,
                             animations, plain data ...); their internal formats are only partially documented

Raw archive files are in ..\files. Music (.OGG), videos (.WMV) and shaders (.FX source) are already in
standard formats there. .FXC are compiled shaders.
";
        }
    }
}
