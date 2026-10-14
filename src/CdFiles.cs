// CDFILES.DAT / ARCHIVE0.AR reader-writer for Hot Wheels: Beat That! (PC, Eutechnyx engine).
// Format knowledge based on PredatorCZ/Technyx (cdfiles_extract.cpp, GPL-3.0), "version 5" layout.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Hwbt
{
    public enum EntryType
    {
        Stream = 0,
        HddFile = 2,
        StreamFile = 4,
        StreamHdFile = 5,
    }

    public class CdEntry
    {
        public int Index;        // position in the tree tables
        public string Name;      // full in-game path, e.g. \DATA\CARS\FOO.ARC
        public int DataIndex;    // index into offset/size tables
        public EntryType Type;
        public uint VarData;

        public bool HasData { get { return Type == EntryType.StreamFile; } }

        // Relative path used on disk when unpacking / packing. Unique per archive:
        // the archive contains a few same-named blobs, later ones become NAME~1.EXT, NAME~2.EXT ...
        public string DiskPath;

        internal static string Sanitize(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (char c in name.Replace('/', '\\').TrimStart('\\'))
            {
                if (c == ':' || c == '*' || c == '?' || c == '"' || c == '<' || c == '>' || c == '|' || c < 32)
                    sb.Append('_');
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }
    }

    public class CdFiles
    {
        public byte[] Raw;
        public string Magic;
        public uint Version;
        public uint Unk1;
        public uint NumTotalFiles;
        public uint NumFiles;
        public uint Alignment;
        public string ArchivePath;

        public uint[] FileOffsets;   // in Alignment-sized blocks
        public uint[] FileSizes;     // in bytes
        public List<string> Names = new List<string>();
        public List<CdEntry> Entries = new List<CdEntry>();

        int fileOffsetsPos;
        int fileSizesPos;

        public static CdFiles Load(string path)
        {
            var cd = new CdFiles();
            cd.Raw = File.ReadAllBytes(path);
            cd.Parse();
            return cd;
        }

        uint U32(int pos) { return BitConverter.ToUInt32(Raw, pos); }

        void Parse()
        {
            int p = 0;
            Magic = Encoding.ASCII.GetString(Raw, 0, 4);
            if (!Magic.StartsWith("fil"))
                throw new InvalidDataException("Not a CDFILES.DAT (magic '" + Magic + "')");
            if (Magic != "filC")
                throw new InvalidDataException("Only PC (filC) CDFILES are supported, got '" + Magic + "'");
            Version = U32(4);
            if (Version != 5)
                throw new InvalidDataException("Unsupported CDFILES version " + Version + " (expected 5)");
            p = 8;

            Unk1 = U32(p); p += 4;
            if (Unk1 < 4) p += 4; // extra float

            uint[] h = new uint[11];
            for (int i = 0; i < 11; i++) { h[i] = U32(p); p += 4; }
            uint rootPathSize = h[2];
            uint numSearchPaths = h[4];
            uint workingPathSize = h[5];
            NumTotalFiles = h[6];
            uint archivePathLength = h[7];
            Alignment = h[8];
            NumFiles = h[9];

            if (Unk1 > 4) p += (int)numSearchPaths * 4;
            else p += (int)rootPathSize;

            p += 8;                       // unk2[2]
            p += (int)workingPathSize;

            ArchivePath = Encoding.ASCII.GetString(Raw, p, (int)archivePathLength).TrimEnd('\0');
            p += (int)archivePathLength;
            if (ArchivePath.StartsWith("#/")) ArchivePath = ArchivePath.Substring(2);

            fileOffsetsPos = p;
            FileOffsets = ReadArray(ref p, NumTotalFiles);
            fileSizesPos = p;
            FileSizes = ReadArray(ref p, NumTotalFiles);
            uint[] treeOffsets = ReadArray(ref p, NumFiles);
            uint[] fileIds = ReadArray(ref p, NumFiles);
            uint[] varData = ReadArray(ref p, NumFiles);
            if (Unk1 < 4) p += (int)NumFiles * ((Unk1 == 3 ? 1 : 0) + 1) * 4;

            uint numNames = U32(p); p += 4;
            uint namesBufferSize = U32(p); p += 4;
            int namesBegin = p + (int)numNames * 4;
            for (int i = 0; i < numNames; i++)
            {
                int off = namesBegin + (int)U32(p); p += 4;
                int end = off;
                while (Raw[end] != 0) end++;
                Names.Add(Encoding.GetEncoding(1252).GetString(Raw, off, end - off));
            }
            p = namesBegin + (int)namesBufferSize;
            p += 4 + 128;                 // unk666 + reserved block
            int treeOrigin = p;

            for (int f = 0; f < NumFiles; f++)
            {
                var e = new CdEntry();
                e.Index = f;
                e.DataIndex = (int)(fileIds[f] & 0x0FFFFFFF);
                e.Type = (EntryType)(fileIds[f] >> 28);
                e.VarData = varData[f];
                e.Name = CatName(treeOrigin + (int)treeOffsets[f]);
                Entries.Add(e);
            }

            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in Entries)
            {
                string path = CdEntry.Sanitize(e.Name);
                int count;
                if (seen.TryGetValue(path, out count))
                {
                    seen[path] = count + 1;
                    string dir = Path.GetDirectoryName(path);
                    path = Path.Combine(dir, Path.GetFileNameWithoutExtension(path) + "~" + count + Path.GetExtension(path));
                }
                else seen[path] = 1;
                e.DiskPath = path;
            }
        }

        uint[] ReadArray(ref int p, uint count)
        {
            var a = new uint[count];
            for (int i = 0; i < count; i++) { a[i] = U32(p); p += 4; }
            return a;
        }

        // Names are stored as a sequence of 1-based indices into the name-fragment table.
        // One byte per index, or two bytes (big-endian, high bit set) for indices >= 128.
        string CatName(int p)
        {
            var sb = new StringBuilder();
            while (p < Raw.Length)
            {
                int c = Raw[p++];
                if (c == 0) break;
                int index = 0;
                if ((c & 0x80) != 0)
                {
                    index = (c & 0x7f) << 8;
                    c = Raw[p++];
                }
                index |= c;
                sb.Append(Names[index - 1]);
            }
            return sb.ToString();
        }

        public long DataOffset(int dataIndex) { return (long)FileOffsets[dataIndex] * Alignment; }

        // Serialise with the current FileOffsets/FileSizes; everything else is kept byte-for-byte.
        public byte[] Build()
        {
            var outb = (byte[])Raw.Clone();
            for (int i = 0; i < NumTotalFiles; i++)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(FileOffsets[i]), 0, outb, fileOffsetsPos + i * 4, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(FileSizes[i]), 0, outb, fileSizesPos + i * 4, 4);
            }
            return outb;
        }

        // Archive file that sits next to CDFILES.DAT.
        public string ArchiveFileIn(string dir)
        {
            return Path.Combine(dir, Path.GetFileName(ArchivePath.Replace('/', '\\')));
        }
    }
}
