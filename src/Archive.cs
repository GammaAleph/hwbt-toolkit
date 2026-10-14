using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Hwbt
{
    // One replacement for one blob of the archive.
    public class ModItem
    {
        public int DataIndex;
        public string Source;        // file or folder in the mod
        public string FilePath;      // raw replacement file (streamed), or null
        public byte[] Data;          // built replacement (LDA from .txt, ARC with injected .dds)
        public long Length { get { return Data != null ? Data.Length : new FileInfo(FilePath).Length; } }
    }

    // High-level operations on a "game data folder" = a folder with CDFILES.DAT + ARCHIVE0.AR
    // (the game has two of them: Data\ = high detail, lData\ = low detail).
    public static class Archive
    {
        public const string BackupDat = "CDFILES.DAT.hwbt_backup";
        public const string BackupInfo = "hwbt_backup.txt";
        public static readonly string[] GameDataDirs = { "Data", "lData" };
        const long MaxSafeArchiveSize = 0x7FFFFFFFL; // game most likely uses signed 32-bit offsets

        public static Action<string> Log = Console.WriteLine;

        public static string DatIn(string dir) { return Path.Combine(dir, "CDFILES.DAT"); }

        // ---------- unpack ----------
        public static int Unpack(string dataDir, string outDir, string filter)
        {
            var cd = CdFiles.Load(DatIn(dataDir));
            string arPath = cd.ArchiveFileIn(dataDir);
            var wanted = cd.Entries.Where(e => e.HasData && Matches(e.Name, filter))
                                   .OrderBy(e => cd.FileOffsets[e.DataIndex]).ToList();
            Directory.CreateDirectory(outDir);
            var buf = new byte[1 << 20];
            int n = 0;
            using (var ar = OpenRead(arPath))
            {
                foreach (var e in wanted)
                {
                    string dst = Path.Combine(outDir, e.DiskPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    ar.Position = cd.DataOffset(e.DataIndex);
                    using (var o = new FileStream(dst, FileMode.Create, FileAccess.Write))
                        Copy(ar, o, cd.FileSizes[e.DataIndex], buf);
                    n++;
                    if (n % 200 == 0) Log(string.Format("  {0}/{1} ...", n, wanted.Count));
                }
            }
            Log(string.Format("Extracted {0} files to {1}", n, outDir));
            return n;
        }

        public static byte[] ReadBlob(CdFiles cd, Stream ar, int dataIndex)
        {
            var b = new byte[cd.FileSizes[dataIndex]];
            ar.Position = cd.DataOffset(dataIndex);
            int got = 0;
            while (got < b.Length)
            {
                int r = ar.Read(b, got, b.Length - got);
                if (r <= 0) throw new EndOfStreamException();
                got += r;
            }
            return b;
        }

        // ---------- full rebuild ----------
        // Writes a fresh CDFILES.DAT + ARCHIVE0.AR into outDir, with mod files replacing originals.
        public static void Pack(string dataDir, string modDir, string outDir)
        {
            var cd = CdFiles.Load(DatIn(dataDir));
            string srcAr = cd.ArchiveFileIn(dataDir);
            Directory.CreateDirectory(outDir);
            string dstAr = cd.ArchiveFileIn(outDir);
            if (Path.GetFullPath(dstAr).Equals(Path.GetFullPath(srcAr), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Output folder must differ from the source folder (use 'patch' for in-place).");

            Dictionary<int, ModItem> mods;
            using (var src = OpenRead(srcAr)) mods = CollectMods(cd, src, new[] { modDir });

            var order = Enumerable.Range(0, (int)cd.NumTotalFiles).OrderBy(i => cd.FileOffsets[i]).ToList();
            var buf = new byte[1 << 20];
            uint[] newOff = new uint[cd.NumTotalFiles];
            uint[] newSize = new uint[cd.NumTotalFiles];
            using (var src = OpenRead(srcAr))
            using (var dst = new FileStream(dstAr, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            {
                int done = 0;
                foreach (int i in order)
                {
                    newOff[i] = (uint)(dst.Position / cd.Alignment);
                    ModItem m;
                    if (mods.TryGetValue(i, out m)) newSize[i] = WriteMod(m, dst, buf);
                    else
                    {
                        src.Position = cd.DataOffset(i);
                        newSize[i] = cd.FileSizes[i];
                        Copy(src, dst, cd.FileSizes[i], buf);
                    }
                    Pad(dst, cd.Alignment);
                    if (++done % 500 == 0) Log(string.Format("  {0}/{1} ...", done, order.Count));
                }
                CheckSize(dst.Length);
            }
            cd.FileOffsets = newOff;
            cd.FileSizes = newSize;
            File.WriteAllBytes(DatIn(outDir), cd.Build());
            Log(string.Format("Packed {0} files ({1} replaced) into {2}", order.Count, mods.Count, outDir));
        }

        // ---------- in-place patch ----------
        // Appends modded files to the end of the existing archive and repoints CDFILES.DAT.
        // Always starts from the pristine backup, so re-running with a different mod set is safe.
        public static int Patch(string dataDir, IList<string> modDirs)
        {
            string dat = DatIn(dataDir);
            EnsureBackup(dataDir);
            Restore(dataDir, true);

            var cd = CdFiles.Load(dat);
            string arPath = cd.ArchiveFileIn(dataDir);
            Dictionary<int, ModItem> mods;
            using (var ar = OpenRead(arPath)) mods = CollectMods(cd, ar, modDirs);
            if (mods.Count == 0) { Log("Nothing to patch in " + dataDir); return 0; }

            var buf = new byte[1 << 20];
            using (var ar = new FileStream(arPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, 1 << 16))
            {
                ar.Position = ar.Length;
                Pad(ar, cd.Alignment);
                foreach (var m in mods.Values.OrderBy(x => x.DataIndex))
                {
                    cd.FileOffsets[m.DataIndex] = (uint)(ar.Position / cd.Alignment);
                    cd.FileSizes[m.DataIndex] = WriteMod(m, ar, buf);
                    Pad(ar, cd.Alignment);
                }
                CheckSize(ar.Length);
            }
            File.WriteAllBytes(dat, cd.Build());
            Log(string.Format("Patched {0} file(s) in {1}", mods.Count, dataDir));
            return mods.Count;
        }

        // Applies every sub-folder of modsRoot (alphabetical order, later wins) to Data\ and lData\.
        public static void InstallGame(string gameDir, string modsRoot)
        {
            var modDirs = ModFolders(modsRoot);
            if (modDirs.Count == 0) Log("No mod folders found in " + modsRoot + " - restoring original data.");
            foreach (var m in modDirs) Log("Mod: " + Path.GetFileName(m));
            foreach (var d in GameDataDirs)
            {
                string dir = Path.Combine(gameDir, d);
                if (!File.Exists(DatIn(dir))) continue;
                Log("== " + dir);
                if (modDirs.Count == 0) Restore(dir, false);
                else Patch(dir, modDirs);
            }
        }

        public static List<string> ModFolders(string modsRoot)
        {
            if (!Directory.Exists(modsRoot)) return new List<string>();
            return Directory.GetDirectories(modsRoot)
                .Where(d => !Path.GetFileName(d).StartsWith("_") && !Path.GetFileName(d).StartsWith("."))
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static void UninstallGame(string gameDir)
        {
            foreach (var d in GameDataDirs)
            {
                string dir = Path.Combine(gameDir, d);
                if (File.Exists(DatIn(dir))) Uninstall(dir);
            }
        }

        public static void EnsureBackup(string dataDir)
        {
            string bdat = Path.Combine(dataDir, BackupDat);
            string binfo = Path.Combine(dataDir, BackupInfo);
            if (File.Exists(bdat) && File.Exists(binfo)) return;
            var cd = CdFiles.Load(DatIn(dataDir));
            long len = new FileInfo(cd.ArchiveFileIn(dataDir)).Length;
            File.Copy(DatIn(dataDir), bdat, true);
            File.WriteAllText(binfo, "archive_length=" + len + "\r\n");
            Log("Backup created: " + bdat);
        }

        // Restores the pristine state: original CDFILES.DAT and truncates appended data.
        public static bool Restore(string dataDir, bool quiet)
        {
            string bdat = Path.Combine(dataDir, BackupDat);
            string binfo = Path.Combine(dataDir, BackupInfo);
            if (!File.Exists(bdat) || !File.Exists(binfo))
            {
                if (!quiet) Log("No hwbt backup in " + dataDir + " - nothing to restore.");
                return false;
            }
            long len = -1;
            foreach (var line in File.ReadAllLines(binfo))
                if (line.StartsWith("archive_length=")) len = long.Parse(line.Substring(15));
            if (len <= 0) throw new InvalidDataException("Corrupt " + binfo);

            var cd = CdFiles.Load(bdat);
            string arPath = cd.ArchiveFileIn(dataDir);
            using (var ar = new FileStream(arPath, FileMode.Open, FileAccess.Write))
            {
                if (ar.Length < len) throw new InvalidDataException("Archive is shorter than at backup time - was it replaced?");
                ar.SetLength(len);
            }
            File.Copy(bdat, DatIn(dataDir), true);
            if (!quiet) Log("Restored original archive in " + dataDir);
            return true;
        }

        // Restores and removes the backup (leaves the folder exactly as it was before any patching).
        public static void Uninstall(string dataDir)
        {
            if (Restore(dataDir, false))
            {
                File.Delete(Path.Combine(dataDir, BackupDat));
                File.Delete(Path.Combine(dataDir, BackupInfo));
            }
        }

        // ---------- mod folder scanning ----------
        // Recognised items in a mod folder (any sub-folder depth):
        //   NAME.EXT          file with the same name as an archive entry -> replaces it
        //   TEXTxxxx.txt      text exported by lda2txt -> compiled into TEXTxxxx.LDA
        //   NAME.ARC\*.png|tga|dds  folder named like an arcbank -> textures injected into the original bank
        //   NAME.HDR\*.wav      sound bank rebuilt (NAME.HDR + NAME.RAW), names as written by "decompile"
        //   NAME.HDR\*.ogg      speech bank rebuilt (NAME.HDR + NAME.SPD); NAME.SPD\ works too
        public static Dictionary<int, ModItem> CollectMods(CdFiles cd, Stream originalArchive, IEnumerable<string> modDirs)
        {
            var result = new Dictionary<int, ModItem>();
            var byName = new Dictionary<string, CdEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in cd.Entries) if (e.HasData && !byName.ContainsKey(Path.GetFileName(e.DiskPath))) byName[Path.GetFileName(e.DiskPath)] = e;

            foreach (var modDir in modDirs)
            {
                if (!Directory.Exists(modDir)) { Log("  ! mod folder not found: " + modDir); continue; }
                string root = Path.GetFullPath(modDir).TrimEnd('\\') + "\\";
                foreach (var f in Directory.GetFiles(modDir, "*", SearchOption.AllDirectories))
                {
                    string rel = Path.GetFullPath(f).Substring(root.Length);
                    string parent = Path.GetFileName(Path.GetDirectoryName(f));
                    string fname = Path.GetFileName(f);
                    CdEntry e;

                    if (parent.EndsWith(".ARC", StringComparison.OrdinalIgnoreCase) && ArcTools.IsImage(fname))
                        continue; // handled per folder below
                    if ((parent.EndsWith(".HDR", StringComparison.OrdinalIgnoreCase) || parent.EndsWith(".SPD", StringComparison.OrdinalIgnoreCase)) &&
                        (fname.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || fname.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)))
                        continue; // sound / speech bank folders, handled below
                    if (byName.TryGetValue(fname, out e))
                    {
                        result[e.DataIndex] = new ModItem { DataIndex = e.DataIndex, Source = rel, FilePath = f };
                        continue;
                    }
                    if (fname.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) &&
                        byName.TryGetValue(Path.GetFileNameWithoutExtension(fname) + ".LDA", out e))
                    {
                        var lda = Lda.FromText(File.ReadAllText(f));
                        var orig = Lda.Parse(ReadBlob(cd, originalArchive, e.DataIndex));
                        if (lda.Strings.Count != orig.Strings.Count)
                            Log(string.Format("  ! {0}: {1} lines, original has {2} - the game may misbehave", rel, lda.Strings.Count, orig.Strings.Count));
                        result[e.DataIndex] = new ModItem { DataIndex = e.DataIndex, Source = rel, Data = lda.Build() };
                        continue;
                    }
                    if (fname.StartsWith("readme", StringComparison.OrdinalIgnoreCase) || fname.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                        || fname.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                        continue;
                    Log("  ! skipped (no such file in archive): " + rel);
                }

                foreach (var d in Directory.GetDirectories(modDir, "*.ARC", SearchOption.AllDirectories))
                {
                    CdEntry e;
                    if (!byName.TryGetValue(Path.GetFileName(d), out e))
                    {
                        Log("  ! skipped folder (no such bank in archive): " + d.Substring(root.Length));
                        continue;
                    }
                    if (!Directory.GetFiles(d).Any(ArcTools.IsImage)) continue;
                    ModItem prev;
                    byte[] baseArc = result.TryGetValue(e.DataIndex, out prev)
                        ? (prev.Data ?? File.ReadAllBytes(prev.FilePath))
                        : ReadBlob(cd, originalArchive, e.DataIndex);
                    int n;
                    byte[] arc = ArcTools.ImportTextures(baseArc, d, out n);
                    Log(string.Format("  {0}: {1} texture(s) injected", Path.GetFileName(d), n));
                    result[e.DataIndex] = new ModItem { DataIndex = e.DataIndex, Source = d.Substring(root.Length), Data = arc };
                }

                // sound banks: BANK.HDR\*.wav -> BANK.HDR + BANK.RAW; speech banks: BANK.HDR\*.ogg (or BANK.SPD\) -> BANK.HDR + BANK.SPD
                var bankDirs = Directory.GetDirectories(modDir, "*.HDR", SearchOption.AllDirectories)
                    .Concat(Directory.GetDirectories(modDir, "*.SPD", SearchOption.AllDirectories));
                foreach (var d in bankDirs)
                {
                    string bank = Path.GetFileNameWithoutExtension(d);
                    CdEntry hdrEntry;
                    if (!byName.TryGetValue(bank + ".HDR", out hdrEntry))
                    {
                        Log("  ! skipped folder (no such sound bank in archive): " + d.Substring(root.Length));
                        continue;
                    }
                    byte[] hdr = ReadBlob(cd, originalArchive, hdrEntry.DataIndex);
                    bool speech = Sound.IsSpeechBank(hdr);
                    CdEntry dataEntry;
                    if (!byName.TryGetValue(bank + (speech ? ".SPD" : ".RAW"), out dataEntry))
                    {
                        Log("  ! " + bank + ": bank has no " + (speech ? ".SPD" : ".RAW") + " data file - cannot rebuild");
                        continue;
                    }
                    if (!Directory.GetFiles(d, speech ? "*.ogg" : "*.wav").Any()) continue;
                    byte[] data = ReadBlob(cd, originalArchive, dataEntry.DataIndex);
                    byte[] newHdr, newData;
                    int n;
                    if (speech) Sound.BuildSpeechBank(hdr, data, d, out newHdr, out newData, out n);
                    else Sound.BuildSoundBank(hdr, data, d, out newHdr, out newData, out n);
                    Log(string.Format("  {0}: {1} {2} replaced", bank, n, speech ? "speech take(s)" : "sound(s)"));
                    string src = d.Substring(root.Length);
                    result[hdrEntry.DataIndex] = new ModItem { DataIndex = hdrEntry.DataIndex, Source = src + " (HDR)", Data = newHdr };
                    result[dataEntry.DataIndex] = new ModItem { DataIndex = dataEntry.DataIndex, Source = src + (speech ? " (SPD)" : " (RAW)"), Data = newData };
                }
            }
            foreach (var m in result.Values.OrderBy(x => x.Source)) Log("  + " + m.Source);
            return result;
        }

        // ---------- helpers ----------
        static uint WriteMod(ModItem m, Stream dst, byte[] buf)
        {
            if (m.Data != null) { dst.Write(m.Data, 0, m.Data.Length); return (uint)m.Data.Length; }
            using (var f = File.OpenRead(m.FilePath))
            {
                Copy(f, dst, f.Length, buf);
                return (uint)f.Length;
            }
        }

        static FileStream OpenRead(string path)
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
        }

        static bool Matches(string name, string filter)
        {
            if (string.IsNullOrEmpty(filter) || filter == "*") return true;
            var rx = "^" + Regex.Escape(filter).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(name, rx, RegexOptions.IgnoreCase);
        }

        static void Copy(Stream src, Stream dst, long count, byte[] buf)
        {
            while (count > 0)
            {
                int r = src.Read(buf, 0, (int)Math.Min(buf.Length, count));
                if (r <= 0) throw new EndOfStreamException("Unexpected end of archive");
                dst.Write(buf, 0, r);
                count -= r;
            }
        }

        // The original tools pad blobs with spaces (0x20), not zeros.
        static void Pad(Stream s, uint align)
        {
            long rem = s.Position % align;
            if (rem == 0) return;
            var pad = new byte[align - rem];
            for (int i = 0; i < pad.Length; i++) pad[i] = 0x20;
            s.Write(pad, 0, pad.Length);
        }

        static void CheckSize(long len)
        {
            if (len > MaxSafeArchiveSize)
                Log("WARNING: archive is larger than 2 GB - the game may fail to read files past that point.");
        }
    }
}
