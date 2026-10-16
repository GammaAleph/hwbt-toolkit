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
            using (var src = OpenRead(srcAr)) mods = CollectMods(cd, src, new[] { modDir }, DataDirName(dataDir));

            BuildArchive(cd, srcAr, dstAr, mods);
            File.WriteAllBytes(DatIn(outDir), cd.Build());
            Log(string.Format("Packed {0} files ({1} replaced) into {2}", cd.NumTotalFiles, mods.Count, outDir));
        }

        // Writes a complete new archive: every blob in its original order, mod items instead of the files they
        // replace (the replaced originals are not kept). Updates cd's offsets/sizes; returns the archive length.
        static long BuildArchive(CdFiles cd, string srcAr, string dstAr, Dictionary<int, ModItem> mods)
        {
            var order = Enumerable.Range(0, (int)cd.NumTotalFiles).OrderBy(i => cd.FileOffsets[i]).ToList();
            var buf = new byte[1 << 20];
            uint[] newOff = new uint[cd.NumTotalFiles];
            uint[] newSize = new uint[cd.NumTotalFiles];
            long length;
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
                length = dst.Length;
                CheckSize(length);
            }
            cd.FileOffsets = newOff;
            cd.FileSizes = newSize;
            return length;
        }

        // ---------- in-place rebuild ----------
        // Like Patch, but the archive is written anew: replaced files are dropped instead of staying in the archive,
        // so big texture mods take less room (the game most likely cannot read past 2 GB). The pristine archive is
        // kept as ARCHIVE0.AR.hwbt_original (renamed, not copied) and Restore puts it back.
        public static int Repack(string dataDir, IList<string> modDirs)
        {
            string dat = DatIn(dataDir);
            EnsureBackup(dataDir);
            Restore(dataDir, true);

            var cd = CdFiles.Load(dat);
            string arPath = OnDisk(cd.ArchiveFileIn(dataDir));
            string origPath = arPath + OriginalArchiveSuffix;
            Dictionary<int, ModItem> mods;
            using (var ar = OpenRead(arPath)) mods = CollectMods(cd, ar, modDirs, DataDirName(dataDir));
            if (mods.Count == 0) { Log("Nothing to install in " + dataDir); return 0; }

            long origLen = new FileInfo(arPath).Length;
            long replaced = mods.Keys.Sum(i => (long)cd.FileSizes[i]);
            long need = origLen - replaced + mods.Values.Sum(m => m.Length + cd.Alignment) + (16L << 20);
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(arPath)));
            if (drive.AvailableFreeSpace < need)
                throw new IOException(string.Format("Not enough disk space to rebuild {0}: need {1:N0} MB free, have {2:N0} MB " +
                    "(the original archive is kept as a backup while the new one is written).", arPath, need >> 20, drive.AvailableFreeSpace >> 20));

            Log("  rebuilding the archive (original kept as " + Path.GetFileName(origPath) + ") ...");
            File.Move(arPath, origPath);
            string tmp = arPath + ".hwbt_tmp";
            long newLen;
            try
            {
                newLen = BuildArchive(cd, origPath, tmp, mods);
                File.Move(tmp, arPath);
            }
            catch
            {
                // put the original back so the game keeps working
                if (File.Exists(tmp)) File.Delete(tmp);
                if (!File.Exists(arPath)) File.Move(origPath, arPath);
                throw;
            }
            File.WriteAllBytes(dat, cd.Build());
            Log(string.Format("Rebuilt {0} with {1} replaced file(s)", dataDir, mods.Count));
            ReportSize(newLen, origLen);
            return mods.Count;
        }

        public const string OriginalArchiveSuffix = ".hwbt_original";

        // The file as it is spelled on disk (CDFILES.DAT says ARCHIVE0.AR, the retail file is archive0.ar),
        // so renaming it back and forth keeps the original name.
        static string OnDisk(string path)
        {
            string dir = Path.GetDirectoryName(path), name = Path.GetFileName(path);
            if (!Directory.Exists(dir)) return path;
            return Directory.GetFiles(dir, name).FirstOrDefault(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase)) ?? path;
        }

        static void ReportSize(long len, long origLen)
        {
            Log(string.Format("  archive: {0:N0} MB (original {1:N0} MB, {2:+#,0;-#,0;0} MB)",
                len >> 20, origLen >> 20, (len - origLen) >> 20));
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
            using (var ar = OpenRead(arPath)) mods = CollectMods(cd, ar, modDirs, DataDirName(dataDir));
            if (mods.Count == 0) { Log("Nothing to patch in " + dataDir); return 0; }

            var buf = new byte[1 << 20];
            long origLen, newLen;
            using (var ar = new FileStream(arPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, 1 << 16))
            {
                origLen = ar.Length;
                ar.Position = ar.Length;
                Pad(ar, cd.Alignment);
                foreach (var m in mods.Values.OrderBy(x => x.DataIndex))
                {
                    cd.FileOffsets[m.DataIndex] = (uint)(ar.Position / cd.Alignment);
                    cd.FileSizes[m.DataIndex] = WriteMod(m, ar, buf);
                    Pad(ar, cd.Alignment);
                }
                newLen = ar.Length;
                CheckSize(newLen);
            }
            File.WriteAllBytes(dat, cd.Build());
            Log(string.Format("Patched {0} file(s) in {1}", mods.Count, dataDir));
            ReportSize(newLen, origLen);
            return mods.Count;
        }

        // Applies every sub-folder of modsRoot (alphabetical order, later wins) to Data\ and lData\.
        public static void InstallGame(string gameDir, string modsRoot, bool rebuild = false)
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
                else if (rebuild) Repack(dir, modDirs);
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
            string origPath = OnDisk(arPath + OriginalArchiveSuffix);
            if (File.Exists(origPath))
            {
                // rebuilt archive (Repack): put the untouched original back, under its original name
                if (new FileInfo(origPath).Length != len)
                    throw new InvalidDataException("The backup " + origPath + " does not have the recorded size - not restoring automatically.");
                if (File.Exists(arPath)) File.Delete(arPath);
                File.Move(origPath, origPath.Substring(0, origPath.Length - OriginalArchiveSuffix.Length));
            }
            else
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
        //   NAME.ARC\FONT.font\ font rebuilt from font.txt and/or atlas.png|tga|dds
        //   NAME.HDR\*.wav      sound bank rebuilt (NAME.HDR + NAME.RAW), names as written by "decompile"
        //   NAME.HDR\*.ogg      speech bank rebuilt (NAME.HDR + NAME.SPD); NAME.SPD\ works too
        //   Data\..., lData\... sub-folders of a mod apply only to that data folder (the low-detail lData banks
        //                      often contain different textures than Data); everything else applies to both
        public static Dictionary<int, ModItem> CollectMods(CdFiles cd, Stream originalArchive, IEnumerable<string> modDirs, string dataDirName = null)
        {
            var result = new Dictionary<int, ModItem>();
            var byName = new Dictionary<string, CdEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in cd.Entries) if (e.HasData && !byName.ContainsKey(Path.GetFileName(e.DiskPath))) byName[Path.GetFileName(e.DiskPath)] = e;

            // expand every mod into (root, excluded sub-folders): the mod itself without its Data\ / lData\ parts,
            // plus the part for the data folder being patched
            var roots = new List<KeyValuePair<string, string[]>>();
            var withOwnPart = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // mods that have a part for this data folder
            foreach (var md in modDirs)
            {
                if (!Directory.Exists(md)) { Log("  ! mod folder not found: " + md); continue; }
                var parts = GameDataDirs.Select(n => Path.Combine(md, n)).Where(Directory.Exists).ToArray();
                roots.Add(new KeyValuePair<string, string[]>(md, parts.Select(p => Path.GetFullPath(p).TrimEnd('\\') + "\\").ToArray()));
                if (dataDirName != null)
                    foreach (var p in parts)
                        if (Path.GetFileName(p).Equals(dataDirName, StringComparison.OrdinalIgnoreCase))
                        {
                            roots.Add(new KeyValuePair<string, string[]>(p, new string[0]));
                            withOwnPart.Add(md);
                        }
            }

            foreach (var rootEntry in roots)
            {
                string modDir = rootEntry.Key;
                var excluded = rootEntry.Value;
                Func<string, bool> keep = p => !excluded.Any(x => (Path.GetFullPath(p).TrimEnd('\\') + "\\").StartsWith(x, StringComparison.OrdinalIgnoreCase));
                string root = Path.GetFullPath(modDir).TrimEnd('\\') + "\\";
                // real content = anything but notes (README.txt etc.); a mod that only has Data / lData parts has no shared part to warn about
                bool hasOwnContent = Directory.GetFiles(modDir, "*", SearchOption.AllDirectories).Where(keep).Any(x => !IsNoteFile(x));
                int itemsBefore = result.Count;
                bool recognised = false; // something in the mod matched the game, even if it turned out unchanged
                int unchanged = 0; // files identical to the original game data (skipped)
                foreach (var f in Directory.GetFiles(modDir, "*", SearchOption.AllDirectories).Where(keep))
                {
                    string rel = Path.GetFullPath(f).Substring(root.Length);
                    string parent = Path.GetFileName(Path.GetDirectoryName(f));
                    string fname = Path.GetFileName(f);
                    CdEntry e;

                    if (HasArcAncestor(f, modDir))
                        continue; // anything inside a NAME.ARC\ folder (textures, fonts, decompile leftovers) is handled per bank below
                    if (ArcTools.IsFontDir(Path.GetDirectoryName(f)))
                        continue; // font folders (font.txt + atlas) inside NAME.ARC\, handled below
                    if (IsSoundBankDir(Path.GetDirectoryName(f), byName) &&
                        (fname.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || fname.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)))
                        continue; // sound / speech bank folders (BANK.HDR\ or decompile's sounds\BANK\, speech\BANK\), handled below
                    if (byName.TryGetValue(fname, out e))
                    {
                        recognised = true;
                        // whole files copied from "files\" of the decompile output are usually unchanged - skip those
                        if (SameAsOriginal(cd, originalArchive, e.DataIndex, f)) { unchanged++; continue; }
                        result[e.DataIndex] = new ModItem { DataIndex = e.DataIndex, Source = rel, FilePath = f };
                        continue;
                    }
                    if (fname.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) &&
                        byName.TryGetValue(Path.GetFileNameWithoutExtension(fname) + ".LDA", out e))
                    {
                        recognised = true;
                        var lda = Lda.FromText(File.ReadAllText(f));
                        byte[] origLda = ReadBlob(cd, originalArchive, e.DataIndex);
                        var orig = Lda.Parse(origLda);
                        if (lda.Strings.Count != orig.Strings.Count)
                            Log(string.Format("  ! {0}: {1} lines, original has {2} - the game may misbehave", rel, lda.Strings.Count, orig.Strings.Count));
                        byte[] built = lda.Build();
                        if (built.SequenceEqual(origLda)) { unchanged++; continue; }
                        result[e.DataIndex] = new ModItem { DataIndex = e.DataIndex, Source = rel, Data = built };
                        continue;
                    }
                    if (fname.StartsWith("readme", StringComparison.OrdinalIgnoreCase) || fname.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                        || fname.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                        continue;
                    Log("  ! skipped (no such file in archive): " + rel);
                }

                Func<string, string> relOf = p =>
                {
                    string full = Path.GetFullPath(p).TrimEnd('\\') + "\\";
                    return full.Length > root.Length ? full.Substring(root.Length).TrimEnd('\\') : Path.GetFileName(modDir.TrimEnd('\\'));
                };
                // the mod folder itself may be a bank folder (e.g. mods\A_MAINMENU_FONTSARCHIVE.ARC\)
                Func<string, IEnumerable<string>> dirsNamed = ext =>
                    Directory.GetDirectories(modDir, "*" + ext, SearchOption.AllDirectories).Where(keep)
                        .Concat(modDir.TrimEnd('\\').EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? new[] { modDir } : new string[0]);

                foreach (var d in dirsNamed(".ARC"))
                {
                    CdEntry e;
                    if (!byName.TryGetValue(Path.GetFileName(d.TrimEnd('\\')), out e))
                    {
                        Log("  ! skipped folder (no such bank in archive): " + relOf(d));
                        continue;
                    }
                    var images = ArcTools.TextureImageFiles(d);
                    bool hasImages = images.Count > 0;
                    bool hasFonts = ArcTools.HasFontFolders(d);
                    // files that can't go back into a bank (e.g. index.txt / entries\*.bin copied from decompile)
                    var fontRoots = ArcTools.FindFontDirs(d).Select(x => Path.GetFullPath(x).TrimEnd('\\') + "\\").ToList();
                    int ignored = Directory.GetFiles(d, "*", SearchOption.AllDirectories).Count(x =>
                        !ArcTools.IsImage(x) && !Path.GetFileName(x).Equals("index.txt", StringComparison.OrdinalIgnoreCase) &&
                        Path.GetFullPath(x).IndexOf(@"\entries\", StringComparison.OrdinalIgnoreCase) < 0 && // decompile output, cannot be imported
                        !fontRoots.Any(fr => Path.GetFullPath(x).StartsWith(fr, StringComparison.OrdinalIgnoreCase)));
                    if (ignored > 0)
                        Log(string.Format("  i {0}: {1} other file(s) ignored (raw entries such as entries\\*.bin cannot be imported)", relOf(d), ignored));
                    if (!hasImages && !hasFonts) continue;
                    recognised = true;
                    ModItem prev;
                    byte[] arc = result.TryGetValue(e.DataIndex, out prev)
                        ? (prev.Data ?? File.ReadAllBytes(prev.FilePath))
                        : ReadBlob(cd, originalArchive, e.DataIndex);
                    int n, changes = 0;
                    if (hasImages)
                    {
                        int missing;
                        arc = ArcTools.ImportTextures(arc, d, out n, out missing, !withOwnPart.Contains(modDir));
                        if (n > 0)
                            Log(string.Format("  {0}: {1} texture(s) injected", Path.GetFileName(d.TrimEnd('\\')), n));
                        changes += n;
                        bool isDataPart = GameDataDirs.Any(g => g.Equals(Path.GetFileName(modDir.TrimEnd('\\')), StringComparison.OrdinalIgnoreCase));
                        if (missing > 0 && dataDirName != null && !isDataPart && !withOwnPart.Contains(modDir))
                            Log(string.Format("  i {0} has different textures in {1}\\ - put the {1} versions into <mod>\\{1}\\{2}\\ (export them from {1}\\ with \"Textures ARC\" or \"Unpack and decompile\")",
                                Path.GetFileName(d.TrimEnd('\\')), dataDirName, Path.GetFileName(d.TrimEnd('\\'))));
                    }
                    if (hasFonts)
                    {
                        arc = ArcTools.ImportFonts(arc, d, out n);
                        if (n > 0) Log(string.Format("  {0}: {1} font(s) rebuilt", Path.GetFileName(d.TrimEnd('\\')), n));
                        changes += n;
                    }
                    // a folder whose images all equal the original changes nothing - don't rewrite the bank
                    if (changes > 0 || prev != null) result[e.DataIndex] = new ModItem { DataIndex = e.DataIndex, Source = relOf(d), Data = arc };
                }

                // <name>.font folders that are not inside a NAME.ARC folder: apply them to every bank that has a font
                // with that name (e.g. "mainmenu" exists in 13 menu banks)
                var orphanFonts = ArcTools.FindFontDirs(modDir).Concat(ArcTools.IsFontDir(modDir) ? new[] { modDir } : new string[0])
                    .Where(fd => keep(fd) && !HasArcAncestor(fd, modDir)).ToList();
                if (orphanFonts.Count > 0) ApplyFontsEverywhere(cd, originalArchive, orphanFonts, result, relOf);

                // sound banks: BANK.HDR\*.wav -> BANK.HDR + BANK.RAW; speech banks: BANK.HDR\*.ogg (or BANK.SPD\) -> BANK.HDR + BANK.SPD
                var bankDirs = dirsNamed(".HDR").Concat(dirsNamed(".SPD"))
                    .Concat(Directory.GetDirectories(modDir, "*", SearchOption.AllDirectories).Where(x => keep(x) && IsDecompiledSoundDir(x, byName)));
                foreach (var d in bankDirs)
                {
                    string dn = Path.GetFileName(d.TrimEnd('\\'));
                    string bank = dn.EndsWith(".HDR", StringComparison.OrdinalIgnoreCase) || dn.EndsWith(".SPD", StringComparison.OrdinalIgnoreCase) ? dn.Substring(0, dn.Length - 4) : dn;
                    CdEntry hdrEntry;
                    if (!byName.TryGetValue(bank + ".HDR", out hdrEntry))
                    {
                        Log("  ! skipped folder (no such sound bank in archive): " + relOf(d));
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
                    recognised = true;
                    if (newHdr.SequenceEqual(hdr) && newData.SequenceEqual(data)) { unchanged++; continue; } // copied, not edited
                    Log(string.Format("  {0}: {1} {2} replaced", bank, n, speech ? "speech take(s)" : "sound(s)"));
                    string src = relOf(d);
                    result[hdrEntry.DataIndex] = new ModItem { DataIndex = hdrEntry.DataIndex, Source = src + " (HDR)", Data = newHdr };
                    result[dataEntry.DataIndex] = new ModItem { DataIndex = dataEntry.DataIndex, Source = src + (speech ? " (SPD)" : " (RAW)"), Data = newData };
                }

                if (unchanged > 0)
                    Log(string.Format("  i {0}: {1} file(s)/bank(s) identical to the original game data - skipped", Path.GetFileName(modDir.TrimEnd('\\')), unchanged));
                if (unchanged > 0) recognised = true;
                if (result.Count == itemsBefore && !recognised && hasOwnContent)
                {
                    Log("  ! " + Path.GetFileName(modDir.TrimEnd('\\')) + ": nothing here matches the game files. Expected layout (see mods\\_template):");
                    Log("      <mod>\\Data\\arc\\<BANK>.ARC\\textures\\<name>.png   <mod>\\Data\\arc\\<BANK>.ARC\\fonts\\<name>.font\\");
                    Log("      <mod>\\Data\\texts\\TEXT0000.txt   <mod>\\Data\\sounds\\<BANK>\\<name>.wav   <mod>\\Data\\music\\TITLE.OGG   (lData\\ the same)");
                }
            }
            foreach (var m in result.Values.OrderBy(x => x.Source)) Log("  + " + m.Source);
            return result;
        }

        // README / notes that people (and the template) keep in mod folders - never game content.
        static bool IsNoteFile(string path)
        {
            string n = Path.GetFileName(path);
            return n.StartsWith("readme", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                || (n.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) && !n.StartsWith("TEXT", StringComparison.OrdinalIgnoreCase));
        }

        // True when the mod file has exactly the original content of that archive entry.
        static bool SameAsOriginal(CdFiles cd, Stream originalArchive, int dataIndex, string file)
        {
            if (new FileInfo(file).Length != cd.FileSizes[dataIndex]) return false;
            return File.ReadAllBytes(file).SequenceEqual(ReadBlob(cd, originalArchive, dataIndex));
        }

        // NAME.HDR\ / NAME.SPD\, or a folder written by decompile: sounds\<BANK>\ or speech\<BANK>\.
        static bool IsSoundBankDir(string dir, Dictionary<string, CdEntry> byName)
        {
            string n = Path.GetFileName(dir.TrimEnd('\\'));
            return n.EndsWith(".HDR", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".SPD", StringComparison.OrdinalIgnoreCase)
                || IsDecompiledSoundDir(dir, byName);
        }

        static bool IsDecompiledSoundDir(string dir, Dictionary<string, CdEntry> byName)
        {
            string parent = Path.GetFileName(Path.GetDirectoryName(dir.TrimEnd('\\')) ?? "");
            string n = Path.GetFileName(dir.TrimEnd('\\'));
            return (parent.Equals("sounds", StringComparison.OrdinalIgnoreCase) || parent.Equals("speech", StringComparison.OrdinalIgnoreCase))
                && byName.ContainsKey(n + ".HDR");
        }

        // "Data" or "lData" for a data folder path (used to pick the matching part of a mod).
        static string DataDirName(string dataDir)
        {
            return Path.GetFileName(Path.GetFullPath(dataDir).TrimEnd(Path.DirectorySeparatorChar));
        }

        static bool HasArcAncestor(string dir, string modDir)
        {
            string stop = Path.GetFullPath(modDir).TrimEnd('\\');
            for (string d = Path.GetDirectoryName(Path.GetFullPath(dir)); d != null; d = Path.GetDirectoryName(d))
            {
                if (d.EndsWith(".ARC", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(d.TrimEnd('\\'), stop, StringComparison.OrdinalIgnoreCase)) break;
            }
            return false;
        }

        // Banks that contain fonts in the retail game (see docs/FORMATS.md).
        static bool MayHaveFonts(string bankName)
        {
            string n = bankName.ToUpperInvariant();
            return n.EndsWith(".ARC") && (n.Contains("FONTSARCHIVE") || n.StartsWith("GLOBALMENUASSETS"));
        }

        static void ApplyFontsEverywhere(CdFiles cd, Stream originalArchive, List<string> fontDirs, Dictionary<int, ModItem> result, Func<string, string> relOf)
        {
            var applied = fontDirs.ToDictionary(fd => fd, fd => 0);
            foreach (var e in cd.Entries.Where(x => x.HasData && MayHaveFonts(x.DiskPath)))
            {
                ModItem prev;
                byte[] arc = result.TryGetValue(e.DataIndex, out prev) ? (prev.Data ?? File.ReadAllBytes(prev.FilePath)) : ReadBlob(cd, originalArchive, e.DataIndex);
                var names = ArcTools.FontNamesIn(arc);
                var mine = fontDirs.Where(fd => names.Contains(ArcTools.FontNameOf(fd))).ToList();
                if (mine.Count == 0) continue;
                int n;
                arc = ArcTools.ImportFontDirs(arc, mine, false, out n);
                foreach (var fd in mine) applied[fd]++;
                result[e.DataIndex] = new ModItem { DataIndex = e.DataIndex, Source = e.DiskPath + " <- " + string.Join(", ", mine.Select(relOf)), Data = arc };
            }
            foreach (var kv in applied)
                Log(kv.Value > 0
                    ? string.Format("  {0}: font rebuilt in {1} bank(s)", relOf(kv.Key), kv.Value)
                    : string.Format("  ! {0}: the game has no font named '{1}'", relOf(kv.Key), ArcTools.FontNameOf(kv.Key)));
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
                Log("WARNING: archive is larger than 2 GB - not fully tested yet. If the game crashes, try \"Rebuild the archive when installing\"" +
                    " (see the wiki page Experimental: https://github.com/GammaAleph/hwbt-toolkit/wiki/Experimental).");
        }
    }
}
