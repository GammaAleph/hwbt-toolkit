// "Arcbank" (.ARC, magic ARCC on PC) - container for textures, models, materials, etc.
// Layout (PC): 0x00 'ARCC', 0x04 u32 entryCount (low 24 bits), 0x08.. counters,
// 0x80 entry table (16 bytes each), data follows the table; entry offsets are relative to it.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Hwbt
{
    public class ArcEntry
    {
        public int Index;
        public uint Id;          // hash / index field
        public uint Offset;      // relative to DataOrigin
        public int NameOffset;   // into EntryNames blob, -1 = none
        public byte Type;
        public uint Size;        // 24-bit big-endian on disk
        public string Name;

        public string TypeName { get { return ArcBank.TypeName(Type); } }
    }

    public class ArcBank
    {
        public const byte T_PlainData = 0x00, T_Texture = 0x01, T_Material = 0x02, T_LightmapTexture = 0x11,
            T_ReferencedTexture = 0x34, T_EntryNames = 0xfd, T_Group = 0xff;

        public byte[] Raw;
        public List<ArcEntry> Entries = new List<ArcEntry>();
        public int DataOrigin;

        public static ArcBank Load(string path) { return Parse(File.ReadAllBytes(path)); }

        public static ArcBank Parse(byte[] raw)
        {
            var b = new ArcBank();
            b.Raw = raw;
            if (raw.Length < 0x80 || Encoding.ASCII.GetString(raw, 0, 4) != "ARCC")
                throw new InvalidDataException("Not a PC arcbank (ARCC)");
            int count = (int)(BitConverter.ToUInt32(raw, 4) & 0xFFFFFF);
            b.DataOrigin = 0x80 + count * 16;
            byte[] names = null;
            for (int i = 0; i < count; i++)
            {
                int p = 0x80 + i * 16;
                var e = new ArcEntry();
                e.Index = i;
                e.Id = BitConverter.ToUInt32(raw, p);
                e.Offset = BitConverter.ToUInt32(raw, p + 4);
                e.NameOffset = BitConverter.ToInt32(raw, p + 8);
                e.Type = raw[p + 12];
                e.Size = (uint)(raw[p + 13] << 16 | raw[p + 14] << 8 | raw[p + 15]);
                b.Entries.Add(e);
                if (e.Type == T_EntryNames && names == null)
                {
                    names = new byte[e.Size];
                    Buffer.BlockCopy(raw, b.DataOrigin + (int)e.Offset, names, 0, (int)e.Size);
                }
            }
            foreach (var e in b.Entries)
            {
                if (names != null && e.NameOffset >= 0 && e.NameOffset < names.Length)
                {
                    int end = e.NameOffset;
                    while (end < names.Length && names[end] != 0) end++;
                    e.Name = Encoding.GetEncoding(1252).GetString(names, e.NameOffset, end - e.NameOffset);
                }
                else e.Name = "";
            }
            return b;
        }

        public int AbsOffset(ArcEntry e) { return DataOrigin + (int)e.Offset; }

        public byte[] GetData(ArcEntry e)
        {
            var d = new byte[e.Size];
            Buffer.BlockCopy(Raw, AbsOffset(e), d, 0, (int)e.Size);
            return d;
        }

        static uint Align16(uint v) { return (v + 15u) & ~15u; }

        // Replaces an entry's payload. Same size: in place. Different size: everything stored after
        // the entry is shifted. (All retail banks are laid out sequentially, 16-byte aligned, zero padded.)
        public void SetData(ArcEntry target, byte[] data)
        {
            if (data.Length > 0xFFFFFF) throw new InvalidDataException("Entry too large (max 16 MB)");
            if (data.Length == target.Size)
            {
                Buffer.BlockCopy(data, 0, Raw, AbsOffset(target), data.Length);
                return;
            }
            uint oldEnd = Align16(target.Offset + target.Size);
            uint newEnd = Align16(target.Offset + (uint)data.Length);
            int tailStart = DataOrigin + (int)Math.Min(oldEnd, (uint)(Raw.Length - DataOrigin));
            int tailLen = Raw.Length - tailStart;

            var outb = new byte[DataOrigin + newEnd + tailLen];
            Buffer.BlockCopy(Raw, 0, outb, 0, AbsOffset(target));
            Buffer.BlockCopy(data, 0, outb, AbsOffset(target), data.Length);
            Buffer.BlockCopy(Raw, tailStart, outb, DataOrigin + (int)newEnd, tailLen);

            long delta = (long)newEnd - oldEnd;
            foreach (var e in Entries)
                if (e != target && e.Offset > target.Offset) e.Offset = (uint)(e.Offset + delta);
            target.Size = (uint)data.Length;
            Raw = outb;
            foreach (var e in Entries) WriteEntry(e);
        }

        void WriteEntry(ArcEntry e)
        {
            int p = 0x80 + e.Index * 16;
            Buffer.BlockCopy(BitConverter.GetBytes(e.Offset), 0, Raw, p + 4, 4);
            Raw[p + 13] = (byte)(e.Size >> 16);
            Raw[p + 14] = (byte)(e.Size >> 8);
            Raw[p + 15] = (byte)e.Size;
        }

        public static string TypeName(byte t)
        {
            switch (t)
            {
                case 0x00: return "PlainData";
                case 0x01: return "Texture";
                case 0x02: return "Material";
                case 0x09: return "Mesh";
                case 0x0c: return "Font";
                case 0x0f: return "IndexBuffer";
                case 0x10: return "VertexBuffer";
                case 0x11: return "LightmapTexture";
                case 0x15: return "SkinnedMesh";
                case 0x19: return "DeformedMesh";
                case 0x1c: return "Attachment";
                case 0x1d: return "Model";
                case 0x1e: return "DeformedModel";
                case 0x1f: return "SkinnedModel";
                case 0x20: return "AnimatedModel";
                case 0x21: return "Skeleton";
                case 0x25: return "Camera";
                case 0x27: return "RigNode";
                case 0x28: return "InstancedModel";
                case 0x29: return "Animation";
                case 0x31: return "AnimatedNode";
                case 0x34: return "ReferencedTexture";
                case 0x35: return "UnkNode";
                case 0x36: return "LightNode";
                case 0xfd: return "EntryNames";
                case 0xff: return "Group";
                default: return "0x" + t.ToString("X2");
            }
        }
    }
}
