// Sound banks: NAME.HDR ('snda' index) + NAME.RAW (PCM data). Layout as documented by Technyx (hdr_to_wav).
// Header: 'snda', f32 version, u32 count, 20 bytes reserved, then 64-byte items:
//   char name[16], u16 unk0, u16 channels(?), u32 sampleRate, u32 dataStart, u32 0, u32 dataSize, ...
// Samples are 16-bit signed little-endian PCM, stored back to back in .RAW.
//
// Speech banks: NAME.HDR ('spea' index) + NAME.SPD (concatenated Ogg Vorbis streams).
// Header: 'spea', f32 version, u32 phraseCount, u32 streamCount, 16 bytes reserved, then 36-byte items:
//   char name[16], f32, u32, u32, u32 sampleRate, u32 firstStream. A phrase owns the streams up to
//   the next phrase's firstStream (several takes of the same line).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Hwbt
{
    public static class Sound
    {
        const int HeaderSize = 32, ItemSize = 64;
        const int SpeechHeader = 32, SpeechItem = 36;

        // ================= sound banks (snda) =================

        public static bool IsSoundBank(byte[] hdr) { return hdr.Length >= HeaderSize && Encoding.ASCII.GetString(hdr, 0, 4) == "snda"; }
        public static bool IsSpeechBank(byte[] hdr) { return hdr.Length >= SpeechHeader && Encoding.ASCII.GetString(hdr, 0, 4) == "spea"; }

        // File name (without .wav) used for every item - shared by export and import so they always match.
        public static string[] SoundItemNames(byte[] hdr)
        {
            int count = (int)BitConverter.ToUInt32(hdr, 8);
            var names = new string[count];
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < count; i++)
            {
                string n = SafeName(Path.GetFileNameWithoutExtension(ItemName(hdr, HeaderSize + i * ItemSize)), i);
                if (!used.Add(n)) { n = n + "~" + i; used.Add(n); }
                names[i] = n;
            }
            return names;
        }

        // Writes one .wav per item; returns the number of files written.
        public static int HdrToWav(byte[] hdr, string rawPath, string outDir)
        {
            if (!IsSoundBank(hdr)) throw new InvalidDataException("Not a sound bank header (snda)");
            int count = (int)BitConverter.ToUInt32(hdr, 8);
            if (HeaderSize + count * ItemSize > hdr.Length) throw new InvalidDataException("Truncated sound bank header");
            if (count == 0) return 0; // a few zone banks are empty (their .RAW holds parameters, not audio)
            Directory.CreateDirectory(outDir);
            var names = SoundItemNames(hdr);
            byte[] raw = File.ReadAllBytes(rawPath);
            int written = 0;
            for (int i = 0; i < count; i++)
            {
                int p = HeaderSize + i * ItemSize;
                uint start = BitConverter.ToUInt32(hdr, p + 24), size = BitConverter.ToUInt32(hdr, p + 32);
                if (start + (long)size > raw.Length) { Archive.Log("  ! " + names[i] + ": data outside .RAW, skipped"); continue; }
                var pcm = new byte[size];
                Buffer.BlockCopy(raw, (int)start, pcm, 0, (int)size);
                File.WriteAllBytes(Path.Combine(outDir, names[i] + ".wav"), Wav(pcm, (int)BitConverter.ToUInt32(hdr, p + 20), Channels(hdr, p), 16));
                written++;
            }
            return written;
        }

        static int Channels(byte[] hdr, int item)
        {
            int c = BitConverter.ToUInt16(hdr, item + 18);
            return c == 2 ? 2 : 1;
        }

        // Builds a sound bank from .wav files.
        //   template != null: items whose <name>.wav exists in wavDir are replaced, the others keep their audio;
        //                     extra .wav files are appended as new items (settings copied from the first item).
        //   template == null: a brand-new bank with one item per .wav (alphabetical order).
        // Item names are limited to 15 characters.
        public static void BuildSoundBank(byte[] templateHdr, byte[] templateRaw, string wavDir, out byte[] hdr, out byte[] raw, out int replaced)
        {
            var wavs = Directory.GetFiles(wavDir, "*.wav").ToDictionary(f => Path.GetFileNameWithoutExtension(f), f => f, StringComparer.OrdinalIgnoreCase);
            var items = new List<byte[]>();    // 64-byte item records
            var pcms = new List<byte[]>();
            replaced = 0;
            byte[] header;
            if (templateHdr != null)
            {
                if (!IsSoundBank(templateHdr)) throw new InvalidDataException("Template is not a sound bank (snda)");
                header = templateHdr.Take(HeaderSize).ToArray();
                var names = SoundItemNames(templateHdr);
                for (int i = 0; i < names.Length; i++)
                {
                    var item = templateHdr.Skip(HeaderSize + i * ItemSize).Take(ItemSize).ToArray();
                    string wav;
                    if (wavs.TryGetValue(names[i], out wav))
                    {
                        pcms.Add(LoadWavFor(wav, item));
                        wavs.Remove(names[i]);
                        replaced++;
                    }
                    else
                    {
                        uint start = BitConverter.ToUInt32(item, 24), size = BitConverter.ToUInt32(item, 32);
                        pcms.Add(templateRaw.Skip((int)start).Take((int)size).ToArray());
                    }
                    items.Add(item);
                }
            }
            else
            {
                header = new byte[HeaderSize];
                Encoding.ASCII.GetBytes("snda").CopyTo(header, 0);
                BitConverter.GetBytes(1.0f).CopyTo(header, 4);
            }
            // new items
            byte[] proto = items.Count > 0 ? items[0] : DefaultSoundItem();
            foreach (var kv in wavs.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                var item = (byte[])proto.Clone();
                SetName(item, kv.Key + ".wav");
                pcms.Add(LoadWavFor(kv.Value, item));
                items.Add(item);
                Archive.Log("  + new sound: " + kv.Key);
            }

            var ms = new MemoryStream();
            ms.Write(header, 0, header.Length);
            uint pos = 0;
            for (int i = 0; i < items.Count; i++)
            {
                BitConverter.GetBytes(pos).CopyTo(items[i], 24);
                BitConverter.GetBytes((uint)pcms[i].Length).CopyTo(items[i], 32);
                pos += (uint)pcms[i].Length;
                ms.Write(items[i], 0, ItemSize);
            }
            hdr = ms.ToArray();
            BitConverter.GetBytes((uint)items.Count).CopyTo(hdr, 8);
            // keep whatever followed the audio in the template .RAW (some zone banks store parameters there)
            byte[] tail = new byte[0];
            if (templateHdr != null && templateRaw != null)
            {
                long end = 0;
                int count = (int)BitConverter.ToUInt32(templateHdr, 8);
                for (int i = 0; i < count; i++)
                {
                    int p = HeaderSize + i * ItemSize;
                    end = Math.Max(end, (long)BitConverter.ToUInt32(templateHdr, p + 24) + BitConverter.ToUInt32(templateHdr, p + 32));
                }
                if (end < templateRaw.Length) tail = templateRaw.Skip((int)end).ToArray();
            }
            raw = pcms.SelectMany(x => x).Concat(tail).ToArray();
        }

        static byte[] DefaultSoundItem()
        {
            // values taken from the retail car engine banks
            var item = new byte[ItemSize];
            BitConverter.GetBytes((ushort)0x6400).CopyTo(item, 16);
            BitConverter.GetBytes((ushort)1).CopyTo(item, 18);
            BitConverter.GetBytes(22050u).CopyTo(item, 20);
            BitConverter.GetBytes((ushort)10).CopyTo(item, 36);
            BitConverter.GetBytes((ushort)250).CopyTo(item, 38);
            BitConverter.GetBytes(711.1111f).CopyTo(item, 44);
            BitConverter.GetBytes((ushort)1).CopyTo(item, 48);
            return item;
        }

        // Reads a .wav, converts it to 16-bit PCM with the item's channel count and stores its sample rate in the item.
        static byte[] LoadWavFor(string file, byte[] item)
        {
            int rate, channels;
            short[] samples = ReadWav(File.ReadAllBytes(file), out rate, out channels);
            int want = Channels(item, 0);
            if (channels != want)
            {
                Archive.Log(string.Format("  i {0}: {1} -> {2} channel(s)", Path.GetFileName(file), channels, want));
                samples = Remix(samples, channels, want);
            }
            uint oldRate = BitConverter.ToUInt32(item, 20);
            if (oldRate != 0 && oldRate != rate)
                Archive.Log(string.Format("  i {0}: sample rate {1} Hz (original {2} Hz)", Path.GetFileName(file), rate, oldRate));
            BitConverter.GetBytes((uint)rate).CopyTo(item, 20);
            var pcm = new byte[samples.Length * 2];
            Buffer.BlockCopy(samples, 0, pcm, 0, pcm.Length);
            return pcm;
        }

        static short[] Remix(short[] s, int from, int to)
        {
            int frames = s.Length / from;
            var o = new short[frames * to];
            for (int f = 0; f < frames; f++)
            {
                int sum = 0;
                for (int c = 0; c < from; c++) sum += s[f * from + c];
                for (int c = 0; c < to; c++) o[f * to + c] = to < from ? (short)(sum / from) : s[f * from + Math.Min(c, from - 1)];
            }
            return o;
        }

        // WAV reader: PCM 8/16/24/32-bit and IEEE float 32-bit (also WAVE_FORMAT_EXTENSIBLE). Returns 16-bit samples.
        public static short[] ReadWav(byte[] d, out int rate, out int channels)
        {
            if (d.Length < 12 || Encoding.ASCII.GetString(d, 0, 4) != "RIFF" || Encoding.ASCII.GetString(d, 8, 4) != "WAVE")
                throw new InvalidDataException("Not a WAV file");
            int fmt = 0, bits = 0; rate = 0; channels = 0;
            int p = 12, dataPos = -1, dataLen = 0;
            while (p + 8 <= d.Length)
            {
                string id = Encoding.ASCII.GetString(d, p, 4);
                int len = BitConverter.ToInt32(d, p + 4);
                if (id == "fmt ")
                {
                    fmt = BitConverter.ToUInt16(d, p + 8);
                    channels = BitConverter.ToUInt16(d, p + 10);
                    rate = BitConverter.ToInt32(d, p + 12);
                    bits = BitConverter.ToUInt16(d, p + 22);
                    if (fmt == 0xFFFE && len >= 40) fmt = BitConverter.ToUInt16(d, p + 32); // sub-format GUID starts with the format tag
                }
                else if (id == "data") { dataPos = p + 8; dataLen = Math.Min(len, d.Length - dataPos); }
                p += 8 + len + (len & 1);
            }
            if (dataPos < 0 || channels < 1) throw new InvalidDataException("WAV has no fmt/data chunk");
            if (fmt != 1 && fmt != 3) throw new InvalidDataException("Compressed WAV (format " + fmt + ") - save it as PCM");
            int bps = bits / 8, n = dataLen / bps;
            var s = new short[n];
            for (int i = 0; i < n; i++)
            {
                int q = dataPos + i * bps;
                if (fmt == 3 && bits == 32) s[i] = (short)Math.Max(-32768, Math.Min(32767, BitConverter.ToSingle(d, q) * 32767));
                else if (bits == 8) s[i] = (short)((d[q] - 128) << 8);
                else if (bits == 16) s[i] = BitConverter.ToInt16(d, q);
                else if (bits == 24) s[i] = (short)(d[q + 1] | d[q + 2] << 8);
                else if (bits == 32) s[i] = (short)(BitConverter.ToInt32(d, q) >> 16);
                else throw new InvalidDataException("Unsupported WAV bit depth " + bits);
            }
            return s;
        }

        public static byte[] Wav(byte[] pcm, int rate, int channels, int bits)
        {
            var ms = new MemoryStream(44 + pcm.Length);
            var w = new BinaryWriter(ms);
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + pcm.Length);
            w.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16);
            w.Write((short)1); w.Write((short)channels); w.Write(rate);
            w.Write(rate * channels * bits / 8); w.Write((short)(channels * bits / 8)); w.Write((short)bits);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(pcm.Length); w.Write(pcm);
            return ms.ToArray();
        }

        // ================= speech banks (spea) =================

        // File name (without .ogg) of every stream: phrase name, or phrase_N when a phrase has several takes.
        public static string[] SpeechStreamNames(byte[] hdr, int streamCount)
        {
            var names = new string[streamCount];
            int phrases = (int)BitConverter.ToUInt32(hdr, 8);
            if (SpeechHeader + phrases * SpeechItem <= hdr.Length)
            {
                for (int i = 0; i < phrases; i++)
                {
                    int p = SpeechHeader + i * SpeechItem;
                    string name = SafeName(ItemName(hdr, p), i);
                    int first = (int)BitConverter.ToUInt32(hdr, p + 32);
                    int next = i + 1 < phrases ? (int)BitConverter.ToUInt32(hdr, p + SpeechItem + 32) : streamCount;
                    if (first < 0 || next > streamCount || next < first) return Fallback(streamCount);
                    for (int s = first; s < next; s++) names[s] = next - first > 1 ? name + "_" + (s - first + 1) : name;
                }
            }
            for (int s = 0; s < streamCount; s++) if (names[s] == null) names[s] = string.Format("stream_{0:D3}", s);
            return names;
        }

        static string[] Fallback(int n) { return Enumerable.Range(0, n).Select(s => string.Format("stream_{0:D3}", s)).ToArray(); }

        public static int SpdToOgg(byte[] hdr, string spdPath, string outDir)
        {
            if (!IsSpeechBank(hdr)) throw new InvalidDataException("Not a speech bank header (spea)");
            var streams = SplitOgg(File.ReadAllBytes(spdPath));
            var names = SpeechStreamNames(hdr, streams.Count);
            Directory.CreateDirectory(outDir);
            for (int s = 0; s < streams.Count; s++) File.WriteAllBytes(Path.Combine(outDir, names[s] + ".ogg"), streams[s]);
            return streams.Count;
        }

        // Rebuilds a speech bank: every take whose <name>.ogg exists in oggDir is replaced. New phrases can be
        // added as NEWNAME.ogg (one take) or NEWNAME_1.ogg, NEWNAME_2.ogg ... (several takes).
        public static void BuildSpeechBank(byte[] templateHdr, byte[] templateSpd, string oggDir, out byte[] hdr, out byte[] spd, out int replaced)
        {
            if (!IsSpeechBank(templateHdr)) throw new InvalidDataException("Template is not a speech bank (spea)");
            var streams = SplitOgg(templateSpd);
            var origStreams = new List<byte[]>(streams);
            var names = SpeechStreamNames(templateHdr, streams.Count);
            var files = Directory.GetFiles(oggDir, "*.ogg").ToDictionary(f => Path.GetFileNameWithoutExtension(f), f => f, StringComparer.OrdinalIgnoreCase);
            int phrases = (int)BitConverter.ToUInt32(templateHdr, 8);
            replaced = 0;
            for (int s = 0; s < streams.Count; s++)
            {
                string f;
                if (!files.TryGetValue(names[s], out f)) continue;
                streams[s] = ReadOgg(f);
                files.Remove(names[s]);
                replaced++;
            }
            var items = new List<byte[]>();
            for (int i = 0; i < phrases; i++) items.Add(templateHdr.Skip(SpeechHeader + i * SpeechItem).Take(SpeechItem).ToArray());

            // new phrases, grouped by name without the _N take suffix
            foreach (var g in files.Keys.GroupBy(k => System.Text.RegularExpressions.Regex.Replace(k, "_\\d+$", ""), StringComparer.OrdinalIgnoreCase)
                                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                var item = items.Count > 0 ? (byte[])items[0].Clone() : new byte[SpeechItem];
                SetName(item, g.Key);
                BitConverter.GetBytes((uint)streams.Count).CopyTo(item, 32);
                foreach (var take in g.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
                {
                    var data = ReadOgg(files[take]);
                    int rate = OggRate(data);
                    if (rate > 0) BitConverter.GetBytes((uint)rate).CopyTo(item, 28);
                    streams.Add(data);
                }
                items.Add(item);
                Archive.Log("  + new phrase: " + g.Key + " (" + g.Count() + " take(s))");
            }

            // Stream tables: 16 bytes per stream (u32 start in .SPD, f32 duration in seconds, u32 0, u32 0),
            // then u32 per stream = last byte of the stream (inclusive). Unchanged takes keep their record.
            int oldStreams = (int)BitConverter.ToUInt32(templateHdr, 12);
            int recBase = SpeechHeader + phrases * SpeechItem;
            var ms = new MemoryStream();
            ms.Write(templateHdr, 0, SpeechHeader);
            foreach (var it in items) ms.Write(it, 0, SpeechItem);
            uint pos = 0;
            var ends = new List<uint>();
            for (int s = 0; s < streams.Count; s++)
            {
                var rec = new byte[16];
                bool original = s < oldStreams && s < origStreams.Count && streams[s].SequenceEqual(origStreams[s]) && recBase + (s + 1) * 16 <= templateHdr.Length;
                if (original) Buffer.BlockCopy(templateHdr, recBase + s * 16, rec, 0, 16);
                else BitConverter.GetBytes(OggDuration(streams[s])).CopyTo(rec, 4);
                BitConverter.GetBytes(pos).CopyTo(rec, 0);
                ms.Write(rec, 0, 16);
                pos += (uint)streams[s].Length;
                ends.Add(pos - 1);
            }
            foreach (var e in ends) ms.Write(BitConverter.GetBytes(e), 0, 4);
            hdr = ms.ToArray();
            BitConverter.GetBytes((uint)items.Count).CopyTo(hdr, 8);
            BitConverter.GetBytes((uint)streams.Count).CopyTo(hdr, 12);
            spd = streams.SelectMany(x => x).ToArray();
        }

        // Duration in seconds: granule position of the last page / sample rate.
        public static float OggDuration(byte[] d)
        {
            int rate = OggRate(d);
            if (rate <= 0) return 0;
            long granule = 0;
            for (int pos = 0; pos + 27 <= d.Length && d[pos] == 'O' && d[pos + 1] == 'g' && d[pos + 2] == 'g' && d[pos + 3] == 'S';)
            {
                long g = BitConverter.ToInt64(d, pos + 6);
                if (g > 0) granule = g;
                int segs = d[pos + 26], body = 0;
                for (int i = 0; i < segs && pos + 27 + i < d.Length; i++) body += d[pos + 27 + i];
                pos += 27 + segs + body;
            }
            return (float)granule / rate;
        }

        static byte[] ReadOgg(string file)
        {
            var d = File.ReadAllBytes(file);
            var parts = SplitOgg(d);
            if (parts.Count != 1) throw new InvalidDataException(Path.GetFileName(file) + ": expected one Ogg Vorbis stream, found " + parts.Count);
            return d;
        }

        // Sample rate from the Vorbis identification header of the first page.
        static int OggRate(byte[] d)
        {
            for (int i = 0; i + 16 < Math.Min(d.Length, 200); i++)
                if (d[i] == 1 && Encoding.ASCII.GetString(d, i + 1, 6) == "vorbis") return BitConverter.ToInt32(d, i + 12);
            return 0;
        }

        // Splits concatenated Ogg data into logical streams (a new stream starts at a BOS page).
        public static List<byte[]> SplitOgg(byte[] d)
        {
            var result = new List<byte[]>();
            int pos = 0, start = -1;
            while (pos + 27 <= d.Length && d[pos] == 'O' && d[pos + 1] == 'g' && d[pos + 2] == 'g' && d[pos + 3] == 'S')
            {
                bool bos = (d[pos + 5] & 0x02) != 0;
                int segs = d[pos + 26];
                if (pos + 27 + segs > d.Length) break;
                int body = 0;
                for (int i = 0; i < segs; i++) body += d[pos + 27 + i];
                int len = 27 + segs + body;
                if (pos + len > d.Length) break;
                if (bos && start >= 0) { result.Add(Slice(d, start, pos)); start = pos; }
                if (start < 0) start = pos;
                pos += len;
            }
            if (start >= 0) result.Add(Slice(d, start, pos));
            return result;
        }

        // ================= helpers =================

        static string ItemName(byte[] d, int p)
        {
            int end = p;
            while (end < p + 16 && d[end] != 0) end++;
            return Encoding.ASCII.GetString(d, p, end - p);
        }

        static void SetName(byte[] item, string name)
        {
            var b = Encoding.ASCII.GetBytes(name);
            if (b.Length > 15) throw new InvalidDataException("Name too long for the bank (max 15 characters): " + name);
            Array.Clear(item, 0, 16);
            b.CopyTo(item, 0);
        }

        static byte[] Slice(byte[] d, int from, int to)
        {
            var r = new byte[to - from];
            Buffer.BlockCopy(d, from, r, 0, r.Length);
            return r;
        }

        static string SafeName(string s, int index)
        {
            var sb = new StringBuilder();
            foreach (char c in s) sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            return sb.Length > 0 ? sb.ToString() : "sound" + index;
        }
    }
}
