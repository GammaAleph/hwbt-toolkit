using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Hwbt
{
    static class Program
    {
        public const string Version = "1.2.2";

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                Gui.Run();
                return 0;
            }
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            try
            {
                string cmd = args[0].ToLowerInvariant().TrimStart('-', '/');
                switch (cmd)
                {
                    case "info": return Cmd.Info(Arg(args, 1));
                    case "list": return Cmd.List(Arg(args, 1));
                    case "unpack": Archive.Unpack(Arg(args, 1), Arg(args, 2), args.Length > 3 ? args[3] : null); return 0;
                    case "pack": Archive.Pack(Arg(args, 1), Arg(args, 2), Arg(args, 3)); return 0;
                    case "patch": Archive.Patch(Arg(args, 1), args.Skip(2).ToList()); return 0;
                    case "restore": Archive.Restore(Arg(args, 1), false); return 0;
                    case "uninstall": Archive.Uninstall(Arg(args, 1)); return 0;
                    case "install-mods": Archive.InstallGame(Arg(args, 1), Arg(args, 2)); return 0;
                    case "uninstall-mods": Archive.UninstallGame(Arg(args, 1)); return 0;
                    case "lda2txt": return Cmd.Lda2Txt(Arg(args, 1), args.Length > 2 ? args[2] : null);
                    case "txt2lda": return Cmd.Txt2Lda(Arg(args, 1), args.Length > 2 ? args[2] : null);
                    case "arc-list": return Cmd.ArcList(Arg(args, 1));
                    case "tex-export": return Cmd.TexExport(Arg(args, 1), Arg(args, 2), args.Length > 3 ? args[3].ToLowerInvariant() : "image");
                    case "tex-import": return Cmd.TexImport(Arg(args, 1), Arg(args, 2), args.Length > 3 ? args[3] : null);
                    case "unpack-all": Decompiler.UnpackAll(Arg(args, 1), Arg(args, 2), args.Contains("--dds"), args.Contains("--png")); return 0;
                    case "decompile": Decompiler.DecompileFolder(Arg(args, 1), Arg(args, 2), args.Contains("--dds"), args.Contains("--png")); return 0;
                    case "hdr2wav":
                        {
                            string hdr = Arg(args, 1);
                            int n = Sound.HdrToWav(File.ReadAllBytes(hdr), Path.ChangeExtension(hdr, ".RAW"),
                                args.Length > 2 ? args[2] : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(hdr)), Path.GetFileNameWithoutExtension(hdr)));
                            Console.WriteLine("Wrote {0} .wav file(s)", n);
                            return 0;
                        }
                    case "font-export":
                        {
                            int n = ArcTools.ExportFonts(File.ReadAllBytes(Arg(args, 1)), Arg(args, 2), args.Contains("--dds"));
                            Console.WriteLine("Exported {0} font(s) to {1}", n, args[2]);
                            return 0;
                        }
                    case "font-import":
                        {
                            int n;
                            byte[] result = ArcTools.ImportFonts(File.ReadAllBytes(Arg(args, 1)), Arg(args, 2), out n);
                            string outArc = args.Length > 3 ? args[3] : args[1];
                            File.WriteAllBytes(outArc, result);
                            Console.WriteLine("Imported {0} font(s) into {1}", n, outArc);
                            return 0;
                        }
                    case "make-soundbank": return Cmd.MakeBank(Arg(args, 1), Arg(args, 2), args.Length > 3 ? args[3] : null, false);
                    case "make-speechbank": return Cmd.MakeBank(Arg(args, 1), Arg(args, 2), Arg(args, 3), true);
                    case "survey": return Cmd.Survey(Arg(args, 1));
                    case "gui": Gui.Run(); return 0;
                    default: Usage(); return 1;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                return 2;
            }
        }

        static string Arg(string[] a, int i)
        {
            if (i >= a.Length) { Usage(); throw new ArgumentException("missing argument #" + i); }
            return a[i];
        }

        static void Usage()
        {
            Console.WriteLine(
@"HWBT Toolkit v" + Version + @" - Hot Wheels: Beat That! (PC) archive tools
Run without arguments to open the GUI.

<data> = a folder with CDFILES.DAT + ARCHIVE0.AR (game\Data or game\lData)

Archive:
  info   <data>                       summary of the archive
  list   <data>                       list all files
  unpack <data> <outDir> [mask]       extract files (mask: *.OGG, TEXT*.LDA ...)
  pack   <data> <modDir> <outDir>     build a NEW CDFILES.DAT+ARCHIVE0.AR with mod files
  patch  <data> <modDir> [modDir...]  patch IN PLACE (auto-backup; re-run = re-apply)
  unpack-all <data> <outDir> [--dds] [--png]  unpack + decompile everything (textures->PNG/TGA,
                                      texts->TXT, sounds->WAV, speech->OGG, other entries->.bin)
  decompile  <filesDir> <outDir> [--dds] [--png]  decompile an already unpacked folder
                                      (--png: write .tga-named textures as PNG too)
  font-export <file.ARC> <outDir> [--dds]    fonts -> <name>.font\ (font.txt + atlas.png)
  font-import <file.ARC> <dir> [out.ARC]     <name>.font\ folders under dir -> fonts in the bank
  hdr2wav    <file.HDR> [outDir]      sound bank -> .wav files
  make-soundbank  <wavDir> <out.HDR> [template.HDR]
                                      .wav files -> NEW sound bank (out.HDR + out.RAW); with a template
                                      only the matching sounds are replaced and extra .wav are added
  make-speechbank <oggDir> <out.HDR> <template.HDR>
                                      .ogg files -> speech bank (out.HDR + out.SPD), based on a template
  restore   <data>                    revert to the original (keeps backup)
  uninstall <data>                    revert and delete the backup

Whole game (Data + lData):
  install-mods   <gameDir> <modsRoot> apply every sub-folder of modsRoot as a mod
  uninstall-mods <gameDir>            revert both archives to original

Texts (.LDA):
  lda2txt <file.LDA|folder> [out]     export to editable UTF-8 .txt
  txt2lda <file.txt> [out.LDA]        compile back

Arcbanks (.ARC) / textures:
  arc-list   <file.ARC>               list entries of a bank
  tex-export <file.ARC|folder> <out> [image|png|dds]  textures -> images (image = keep .tga/.png as
                                      originally named, png = everything as PNG; folder: one sub-folder per bank)
  tex-import <file.ARC> <imgDir> [out.ARC]      .png/.tga/.dds -> bank (PNG/TGA are compressed into the
                                      original format with the original mip-map count)

Mod folder layout (used by patch / pack / install-mods):
  SOMEFILE.OGG, CAR.ARC ...  replaces the archive file with the same name
  TEXT0000.txt               compiled to TEXT0000.LDA
  CAR_LOD0_GRA.ARC\*.png     textures (.png or .dds) injected into that bank");
        }
    }

    static class Cmd
    {
        // Accepts either a CDFILES.DAT path or the folder that contains it.
        static CdFiles Open(string path)
        {
            return CdFiles.Load(Directory.Exists(path) ? Archive.DatIn(path) : path);
        }

        public static int List(string dat)
        {
            var cd = Open(dat);
            foreach (var e in cd.Entries)
            {
                if (e.HasData)
                    Console.WriteLine("{0,5} 0x{1:X9} {2,10} {3}", e.Index, cd.DataOffset(e.DataIndex), cd.FileSizes[e.DataIndex], e.DiskPath);
                else
                    Console.WriteLine("{0,5} {1,-12} id={2} {3}", e.Index, e.Type, e.DataIndex, e.Name);
            }
            return 0;
        }

        public static int Info(string dat)
        {
            var cd = Open(dat);
            Console.WriteLine("Magic/version : {0} v{1}", cd.Magic, cd.Version);
            Console.WriteLine("Archive       : {0}", cd.ArchivePath);
            Console.WriteLine("Alignment     : {0}", cd.Alignment);
            Console.WriteLine("Files         : {0}", cd.NumFiles);
            var exts = cd.Entries.Where(e => e.HasData).GroupBy(e => Path.GetExtension(e.Name).ToUpperInvariant())
                .OrderByDescending(g => g.Sum(x => (long)cd.FileSizes[x.DataIndex]));
            foreach (var g in exts)
                Console.WriteLine("  {0,-8} {1,5} files {2,10:N0} KB", g.Key == "" ? "(none)" : g.Key, g.Count(),
                    g.Sum(x => (long)cd.FileSizes[x.DataIndex]) / 1024);
            return 0;
        }

        public static int Lda2Txt(string input, string output)
        {
            if (Directory.Exists(input))
            {
                string outDir = output ?? input;
                Directory.CreateDirectory(outDir);
                int n = 0;
                foreach (var f in Directory.GetFiles(input, "*.LDA"))
                {
                    File.WriteAllText(Path.Combine(outDir, Path.GetFileNameWithoutExtension(f) + ".txt"),
                        Lda.Parse(File.ReadAllBytes(f)).ToText(), new UTF8Encoding(true));
                    n++;
                }
                Console.WriteLine("Converted {0} file(s) to {1}", n, outDir);
                return 0;
            }
            output = output ?? Path.ChangeExtension(input, ".txt");
            File.WriteAllText(output, Lda.Parse(File.ReadAllBytes(input)).ToText(), new UTF8Encoding(true));
            Console.WriteLine("Wrote " + output);
            return 0;
        }

        public static int Txt2Lda(string input, string output)
        {
            output = output ?? Path.ChangeExtension(input, ".LDA");
            File.WriteAllBytes(output, Lda.FromText(File.ReadAllText(input)).Build());
            Console.WriteLine("Wrote " + output);
            return 0;
        }

        public static int ArcList(string file)
        {
            var b = ArcBank.Load(file);
            Console.WriteLine("{0}: {1} entries", Path.GetFileName(file), b.Entries.Count);
            foreach (var e in b.Entries)
            {
                string extra = "";
                if (ArcTools.IsTexture(e))
                {
                    var h = TexHeader.Read(b.Raw, b.AbsOffset(e));
                    extra = string.Format("  [{0}x{1} {2} mips={3}]", h.Width, h.Height, h.FormatName, h.Mips);
                }
                Console.WriteLine("{0,4} {1,-17} off=0x{2:X6} size={3,8} {4}{5}", e.Index, e.TypeName, e.Offset, e.Size, e.Name, extra);
            }
            return 0;
        }

        public static int TexExport(string input, string outDir, string format)
        {
            if (format != "image" && format != "png" && format != "dds") throw new ArgumentException("format must be image, png or dds");
            bool allPng = format == "png";
            if (Directory.Exists(input))
            {
                int total = 0, banks = 0;
                foreach (var f in Directory.GetFiles(input, "*.ARC"))
                {
                    int n = ArcTools.ExportTextures(File.ReadAllBytes(f), Path.Combine(outDir, Path.GetFileName(f)), format == "dds" ? "dds" : "image", allPng);
                    if (n > 0) { total += n; banks++; }
                }
                Console.WriteLine("Exported {0} textures from {1} banks to {2}", total, banks, outDir);
                return 0;
            }
            int c = ArcTools.ExportTextures(File.ReadAllBytes(input), outDir, format == "dds" ? "dds" : "image", allPng);
            Console.WriteLine("Exported {0} textures to {1}", c, outDir);
            return 0;
        }

        public static int TexImport(string arc, string ddsDir, string output)
        {
            int n;
            byte[] result = ArcTools.ImportTextures(File.ReadAllBytes(arc), ddsDir, out n);
            File.WriteAllBytes(output ?? arc, result);
            Console.WriteLine("Imported {0} texture(s) into {1}", n, output ?? arc);
            return 0;
        }

        // Builds out.HDR + out.RAW (sound) or out.HDR + out.SPD (speech). The template's data file
        // (.RAW / .SPD) must lie next to the template .HDR.
        public static int MakeBank(string dir, string outHdr, string template, bool speech)
        {
            byte[] tHdr = null, tData = null;
            if (template != null)
            {
                tHdr = File.ReadAllBytes(template);
                tData = File.ReadAllBytes(Path.ChangeExtension(template, speech ? ".SPD" : ".RAW"));
            }
            byte[] hdr, data;
            int n;
            if (speech) Sound.BuildSpeechBank(tHdr, tData, dir, out hdr, out data, out n);
            else Sound.BuildSoundBank(tHdr, tData, dir, out hdr, out data, out n);
            File.WriteAllBytes(outHdr, hdr);
            string dataFile = Path.ChangeExtension(outHdr, speech ? ".SPD" : ".RAW");
            File.WriteAllBytes(dataFile, data);
            Console.WriteLine("Wrote {0} and {1} ({2} replaced)", outHdr, dataFile, n);
            return 0;
        }

        // Developer diagnostics: validates texture/layout assumptions over a folder of unpacked .ARC files.
        public static int Survey(string dir)
        {
            var types = new SortedDictionary<string, int>();
            var fmts = new SortedDictionary<string, int>();
            int bad = 0, unsorted = 0, overlap = 0, files = 0;
            foreach (var f in Directory.GetFiles(dir, "*.ARC"))
            {
                ArcBank b;
                try { b = ArcBank.Load(f); } catch (Exception ex) { Console.WriteLine(Path.GetFileName(f) + ": " + ex.Message); continue; }
                files++;
                foreach (var e in b.Entries)
                {
                    int c; types.TryGetValue(e.TypeName, out c); types[e.TypeName] = c + 1;
                    if (!ArcTools.IsTexture(e)) continue;
                    var h = TexHeader.Read(b.Raw, b.AbsOffset(e));
                    fmts.TryGetValue(h.FormatName, out c); fmts[h.FormatName] = c + 1;
                    long expect = Tex.ChainSize(h.Format, h.Width, h.Height, h.Mips);
                    if (h.Format == Tex.P8) expect += Tex.PaletteBlock(b.Raw, b.AbsOffset(e) + TexHeader.Size);
                    if (expect != e.Size - TexHeader.Size && bad++ < 15)
                        Console.WriteLine("SIZE {0} #{1} {2}: expect {3} got {4}", Path.GetFileName(f), e.Index, e.Name, expect, e.Size - TexHeader.Size);
                }
                long prevEnd = 0; uint prevOff = 0;
                foreach (var e in b.Entries)
                {
                    if (e.Size == 0) continue;
                    if (e.Offset < prevOff) unsorted++;
                    if (e.Offset < prevEnd) overlap++;
                    prevOff = e.Offset; prevEnd = Math.Max(prevEnd, e.Offset + e.Size);
                }
            }
            Console.WriteLine("banks={0} badTexSize={1} unsorted={2} overlap={3}", files, bad, unsorted, overlap);
            foreach (var kv in types) Console.WriteLine("  type {0,-18} {1}", kv.Key, kv.Value);
            foreach (var kv in fmts) Console.WriteLine("  tex  {0,-18} {1}", kv.Key, kv.Value);
            return 0;
        }
    }
}
