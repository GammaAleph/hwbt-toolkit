// Arcbank texture <-> DDS conversion.
// Texture entry = 20-byte header { u32 width, height, numMips, hash, format } + D3D9 mip chain.
// "format" is a D3DFORMAT value (21 = A8R8G8B8, 25 = A1R5G5B5, 26 = A4R4G4B4, 41 = P8, 'DXT1'...).
using System;
using System.IO;
using System.Text;

namespace Hwbt
{
    public class TexHeader
    {
        public uint Width, Height, Mips, Hash, Format;
        public const int Size = 20;

        public static TexHeader Read(byte[] d, int p)
        {
            var h = new TexHeader();
            h.Width = BitConverter.ToUInt32(d, p);
            h.Height = BitConverter.ToUInt32(d, p + 4);
            h.Mips = BitConverter.ToUInt32(d, p + 8);
            h.Hash = BitConverter.ToUInt32(d, p + 12);
            h.Format = BitConverter.ToUInt32(d, p + 16);
            return h;
        }

        public void Write(byte[] d, int p)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(Width), 0, d, p, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(Height), 0, d, p + 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(Mips), 0, d, p + 8, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(Hash), 0, d, p + 12, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(Format), 0, d, p + 16, 4);
        }

        public string FormatName { get { return Tex.FormatName(Format); } }
    }

    public static class Tex
    {
        public const uint A8R8G8B8 = 21, X8R8G8B8 = 22, R5G6B5 = 23, X1R5G5B5 = 24, A1R5G5B5 = 25, A4R4G4B4 = 26,
            A8 = 28, P8 = 41, L8 = 50, A8L8 = 51;
        public static readonly uint DXT1 = FourCC("DXT1"), DXT3 = FourCC("DXT3"), DXT5 = FourCC("DXT5");

        public static uint FourCC(string s) { return (uint)(s[0] | s[1] << 8 | s[2] << 16 | s[3] << 24); }

        public static string FormatName(uint f)
        {
            switch (f)
            {
                case A8R8G8B8: return "A8R8G8B8";
                case X8R8G8B8: return "X8R8G8B8";
                case R5G6B5: return "R5G6B5";
                case X1R5G5B5: return "X1R5G5B5";
                case A1R5G5B5: return "A1R5G5B5";
                case A4R4G4B4: return "A4R4G4B4";
                case A8: return "A8";
                case P8: return "P8";
                case L8: return "L8";
                case A8L8: return "A8L8";
            }
            if (f > 0xFFFF) return Encoding.ASCII.GetString(BitConverter.GetBytes(f));
            return "fmt" + f;
        }

        public static bool IsBlock(uint f) { return f == DXT1 || f == DXT3 || f == DXT5; }

        public static int Bpp(uint f)
        {
            switch (f)
            {
                case A8R8G8B8: case X8R8G8B8: return 32;
                case R5G6B5: case X1R5G5B5: case A1R5G5B5: case A4R4G4B4: case A8L8: return 16;
                case A8: case P8: case L8: return 8;
            }
            return 0;
        }

        public static long MipSize(uint f, uint w, uint h)
        {
            if (w == 0) w = 1;
            if (h == 0) h = 1;
            if (IsBlock(f)) return Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * (f == DXT1 ? 8 : 16);
            int bpp = Bpp(f);
            if (bpp == 0) return -1;
            return (long)w * h * bpp / 8;
        }

        public static long ChainSize(uint f, uint w, uint h, uint mips)
        {
            long total = 0;
            for (int i = 0; i < Math.Max(1, mips); i++)
            {
                long s = MipSize(f, Math.Max(1, w >> i), Math.Max(1, h >> i));
                if (s < 0) return -1;
                total += s;
            }
            return total;
        }

        // Byte count of the palette block in front of P8 pixel data (u32 count + count*256 BGRA).
        public static int PaletteBlock(byte[] d, int p) { return 4 + (int)BitConverter.ToUInt32(d, p) * 1024; }

        // ---------- DDS ----------
        const uint DDSD_CAPS = 0x1, DDSD_HEIGHT = 0x2, DDSD_WIDTH = 0x4, DDSD_PITCH = 0x8, DDSD_PIXELFORMAT = 0x1000,
            DDSD_MIPMAPCOUNT = 0x20000, DDSD_LINEARSIZE = 0x80000;
        const uint DDPF_ALPHAPIXELS = 0x1, DDPF_ALPHA = 0x2, DDPF_FOURCC = 0x4, DDPF_RGB = 0x40, DDPF_LUMINANCE = 0x20000;
        const uint DDSCAPS_COMPLEX = 0x8, DDSCAPS_TEXTURE = 0x1000, DDSCAPS_MIPMAP = 0x400000;

        // Returns DDS bytes for a texture entry payload. P8 textures are expanded to A8R8G8B8.
        public static byte[] ToDds(byte[] entry)
        {
            var h = TexHeader.Read(entry, 0);
            int dataStart = TexHeader.Size;
            uint fmt = h.Format;
            byte[] pixels;
            if (fmt == P8)
            {
                int pal = dataStart + 4;
                int idx = dataStart + PaletteBlock(entry, dataStart);
                int count = entry.Length - idx;
                pixels = new byte[count * 4];
                for (int i = 0; i < count; i++)
                    Buffer.BlockCopy(entry, pal + entry[idx + i] * 4, pixels, i * 4, 4);
                fmt = A8R8G8B8;
            }
            else
            {
                pixels = new byte[entry.Length - dataStart];
                Buffer.BlockCopy(entry, dataStart, pixels, 0, pixels.Length);
            }

            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(FourCC("DDS "));
            w.Write(124u);
            uint flags = DDSD_CAPS | DDSD_HEIGHT | DDSD_WIDTH | DDSD_PIXELFORMAT;
            if (h.Mips > 1) flags |= DDSD_MIPMAPCOUNT;
            flags |= IsBlock(fmt) ? DDSD_LINEARSIZE : DDSD_PITCH;
            w.Write(flags);
            w.Write(h.Height);
            w.Write(h.Width);
            w.Write(IsBlock(fmt) ? (uint)MipSize(fmt, h.Width, h.Height) : h.Width * (uint)Bpp(fmt) / 8);
            w.Write(0u);              // depth
            w.Write(Math.Max(1u, h.Mips));
            for (int i = 0; i < 11; i++) w.Write(0u);
            WritePixelFormat(w, fmt);
            uint caps = DDSCAPS_TEXTURE;
            if (h.Mips > 1) caps |= DDSCAPS_COMPLEX | DDSCAPS_MIPMAP;
            w.Write(caps);
            w.Write(0u); w.Write(0u); w.Write(0u); w.Write(0u);
            w.Write(pixels);
            return ms.ToArray();
        }

        static void WritePixelFormat(BinaryWriter w, uint fmt)
        {
            w.Write(32u);
            if (IsBlock(fmt))
            {
                w.Write(DDPF_FOURCC); w.Write(fmt); w.Write(0u); w.Write(0u); w.Write(0u); w.Write(0u); w.Write(0u);
                return;
            }
            uint pf, bits, r, g, b, a;
            switch (fmt)
            {
                case A8R8G8B8: pf = DDPF_RGB | DDPF_ALPHAPIXELS; bits = 32; r = 0xff0000; g = 0xff00; b = 0xff; a = 0xff000000; break;
                case X8R8G8B8: pf = DDPF_RGB; bits = 32; r = 0xff0000; g = 0xff00; b = 0xff; a = 0; break;
                case R5G6B5: pf = DDPF_RGB; bits = 16; r = 0xf800; g = 0x07e0; b = 0x1f; a = 0; break;
                case X1R5G5B5: pf = DDPF_RGB; bits = 16; r = 0x7c00; g = 0x03e0; b = 0x1f; a = 0; break;
                case A1R5G5B5: pf = DDPF_RGB | DDPF_ALPHAPIXELS; bits = 16; r = 0x7c00; g = 0x03e0; b = 0x1f; a = 0x8000; break;
                case A4R4G4B4: pf = DDPF_RGB | DDPF_ALPHAPIXELS; bits = 16; r = 0x0f00; g = 0x00f0; b = 0x0f; a = 0xf000; break;
                case A8: pf = DDPF_ALPHA; bits = 8; r = 0; g = 0; b = 0; a = 0xff; break;
                case L8: pf = DDPF_LUMINANCE; bits = 8; r = 0xff; g = 0; b = 0; a = 0; break;
                case A8L8: pf = DDPF_LUMINANCE | DDPF_ALPHAPIXELS; bits = 16; r = 0xff; g = 0; b = 0; a = 0xff00; break;
                default: throw new InvalidDataException("Unsupported texture format " + FormatName(fmt));
            }
            w.Write(pf); w.Write(0u); w.Write(bits); w.Write(r); w.Write(g); w.Write(b); w.Write(a);
        }

        // Reads a DDS and returns (format, width, height, mips, pixel data).
        public static void ReadDds(byte[] dds, out uint fmt, out uint width, out uint height, out uint mips, out byte[] pixels)
        {
            if (dds.Length < 128 || BitConverter.ToUInt32(dds, 0) != FourCC("DDS "))
                throw new InvalidDataException("Not a DDS file");
            height = BitConverter.ToUInt32(dds, 12);
            width = BitConverter.ToUInt32(dds, 16);
            mips = Math.Max(1, BitConverter.ToUInt32(dds, 28));
            uint pf = BitConverter.ToUInt32(dds, 80);
            uint four = BitConverter.ToUInt32(dds, 84);
            uint bits = BitConverter.ToUInt32(dds, 88);
            uint r = BitConverter.ToUInt32(dds, 92), g = BitConverter.ToUInt32(dds, 96),
                 b = BitConverter.ToUInt32(dds, 100), a = BitConverter.ToUInt32(dds, 104);
            int dataStart = 128;
            if ((pf & DDPF_FOURCC) != 0)
            {
                if (four == FourCC("DX10")) throw new InvalidDataException("DX10 DDS header not supported - save as legacy DDS (DXT1/DXT3/DXT5 or A8R8G8B8)");
                fmt = four;
                if (!IsBlock(fmt)) throw new InvalidDataException("Unsupported DDS FourCC " + FormatName(four));
            }
            else if (bits == 32 && r == 0xff0000 && g == 0xff00 && b == 0xff)
                fmt = (pf & DDPF_ALPHAPIXELS) != 0 ? A8R8G8B8 : X8R8G8B8;
            else if (bits == 16 && r == 0xf800) fmt = R5G6B5;
            else if (bits == 16 && r == 0x7c00) fmt = (pf & DDPF_ALPHAPIXELS) != 0 ? A1R5G5B5 : X1R5G5B5;
            else if (bits == 16 && r == 0x0f00) fmt = A4R4G4B4;
            else if (bits == 8 && (pf & DDPF_ALPHA) != 0) fmt = A8;
            else if (bits == 8 && (pf & DDPF_LUMINANCE) != 0) fmt = L8;
            else if (bits == 16 && (pf & DDPF_LUMINANCE) != 0) fmt = A8L8;
            else throw new InvalidDataException(string.Format("Unsupported DDS pixel format (bits={0} masks={1:X}/{2:X}/{3:X}/{4:X})", bits, r, g, b, a));

            long need = ChainSize(fmt, width, height, mips);
            if (dds.Length - dataStart < need)
                throw new InvalidDataException("DDS is truncated (expected " + need + " bytes of pixels)");
            pixels = new byte[need];
            Buffer.BlockCopy(dds, dataStart, pixels, 0, (int)need);
        }

        // Builds a texture entry payload from a DDS, keeping the original hash.
        // Format/size may differ from the original; the arcbank is rebuilt if the size changes.
        public static byte[] FromDds(byte[] dds, TexHeader original)
        {
            uint fmt, w, h, mips;
            byte[] px;
            ReadDds(dds, out fmt, out w, out h, out mips, out px);
            var hdr = new TexHeader { Width = w, Height = h, Mips = mips, Hash = original.Hash, Format = fmt };
            var outb = new byte[TexHeader.Size + px.Length];
            hdr.Write(outb, 0);
            Buffer.BlockCopy(px, 0, outb, TexHeader.Size, px.Length);
            return outb;
        }
    }
}
