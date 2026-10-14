# Hot Wheels: Beat That! (PC) file formats

[Русская версия](FORMATS.ru.md)

All numbers are little-endian unless stated otherwise. The archive and arcbank layouts were researched from
[Technyx](https://github.com/PredatorCZ/Technyx) by Lukas Cone (PredatorCZ). The rest was verified on the retail
PC data: every format below survives an unpack → rebuild round trip byte-for-byte.

## CDFILES.DAT (`filC`, version 5): archive index

| Offset   | Type         | Field                                              |
|---------:|--------------|----------------------------------------------------|
| 0x00     | char[4]      | `filC` (C = PC; P = PS2, X = Xbox, E = X360, N = Wii) |
| 0x04     | u32          | version = 5                                        |
| 0x08     | u32          | 0x40000000 (float 2.0, code version)               |
| 0x0C     | u32[11]      | unk2, unk4, rootPathSize, unk3, numSearchPaths, workingPathSize, **numTotalFiles**, archivePathLength, **alignment (2048)**, **numFiles**, unkSize |
| …        | u32[numSearchPaths] | search path offsets (0 here)                |
| …        | u32[2]       | 0                                                  |
| …        | char[workingPathSize] |                                           |
| 0x40     | char[16]     | `\ARCHIVE0.AR`                                     |
| 0x50     | u32[numTotalFiles] | data offset **in 2048-byte blocks**          |
| …        | u32[numTotalFiles] | data size in bytes                           |
| …        | u32[numFiles] | name offset in the name tree                      |
| …        | u32[numFiles] | id: bits 0–27 = data index, 28–31 = type (4 = StreamFile) |
| …        | u32[numFiles] | extra data                                        |
| …        | u32 n, u32 size, u32[n] offsets, char[size] | name fragment table |
| …        | u32 + 128 bytes | reserved                                        |
| …        | name tree    | each name is a sequence of fragment indices (1 byte, or 2 bytes big-endian when the high bit is set), 0-terminated; indices are 1-based |

The game has 2,141 files with flat names (no folders). Two names occur twice (`DEFAULT_STANDARD2D.FX/.FXC`).
When unpacking, the second copy is saved as `NAME~1.EXT`.

## ARCHIVE0.AR: data

Files are stored back to back, aligned to 2048 bytes. The gaps are filled with **spaces (0x20)**. There is no
compression and no encryption. `Data\` is the high-detail version and `lData\` the low-detail one: the file names
are the same, but the textures inside `.ARC` are smaller.

## .ARC: arcbank (`ARCC`)

| Offset   | Type    | Field                                     |
|---------:|---------|-------------------------------------------|
| 0x00     | char[4] | `ARCC`                                    |
| 0x04     | u32     | entry count (low 24 bits)                 |
| 0x08     | u16[…]  | counters (textures, models, …)            |
| 0x80     | entry[n]| 16-byte entries                           |
| 0x80+16n | …       | data; entry offsets are relative to this point |

Entry: `u32 id/hash, u32 offset, i32 nameOffset (-1 = no name), u8 type, u8[3] size (big-endian)`.
The data is sequential, aligned to 16 bytes and padded with zeros. **One entry can hold at most 16 MB**,
because the size field is 24 bits wide.

Entry types: 0x00 PlainData, 0x01 Texture, 0x02 Material, 0x09 Mesh, 0x0F IndexBuffer, 0x10 VertexBuffer,
0x1C Attachment, 0x1D Model, 0x1E DeformedModel, 0x21 Skeleton, 0x29 Animation, 0xFD EntryNames (name strings),
0xFF Group.

### Texture

```
u32 width, height, numMips, hash, format
mip0, mip1, ...   // same layout as D3D9 / DDS
```
`format` is a D3DFORMAT value: 21 = A8R8G8B8, 25 = A1R5G5B5, 26 = A4R4G4B4, 41 = P8 (palette), or the FourCC
`DXT1`/`DXT3`/`DXT5`. Counts in the game: A4R4G4B4 7,661, DXT3 2,414, DXT1 435, A8R8G8B8 23, A1R5G5B5 1.
Texture names keep the artists' source file names (`image001.tga`, `16AngelsBodyDA.png`). The largest
retail textures are 1024×512.

## .LDA: string table

```
char[4] 'lda0' (cp1252) | 'lda1' (UTF-16LE)
u32 fileSize, u32 id0, u32 id1, u32 count
u32 offsets[count], u32 stringsSize
strings, each NUL-terminated
```
`TEXTxxyy.LDA`: `xx` = language (00 English, 01 French, 02 German, 03 Italian, …), `yy` = table.

## .HDR + .RAW: sound banks (`snda`)

```
char[4] 'snda', f32 version (1.0), u32 count, 20 reserved bytes
item[count], 64 bytes each:
  char name[16], u16 (0x6400), u16 channels, u32 sampleRate, u32 dataStart, u32 0, u32 dataSize,
  u16, u16, u32, f32, u16, ...
```
The `.RAW` data is signed 16-bit PCM, usually mono 22050 Hz, with the items stored back to back. A few `ZONE*`
banks have 0 items, and their `.RAW` holds floats (parameters) rather than audio. `MUSICARC`, `IGMUSICARC`
and `RESULTSMUSIC` are indexes of the `.OGG` music and have no `.RAW`.

## .HDR + .SPD: commentator speech (`spea`)

```
char[4] 'spea', f32 version, u32 phraseCount, u32 streamCount, 16 reserved bytes
item[phraseCount], 36 bytes each: char name[16], f32, u32, u32, u32 sampleRate, u32 firstStream
stream[streamCount], 16 bytes each: u32 start (offset in .SPD), f32 duration (seconds), u32 0, u32 0
u32 end[streamCount]   // offset of the last byte of each stream (inclusive)
```
`.SPD` is a sequence of Ogg Vorbis streams, each beginning with a BOS page. A phrase owns the streams from its
`firstStream` up to the next phrase's `firstStream`: these are several takes of the same line.
`duration` = granule position of the last Ogg page / sample rate, exactly as the original tools compute it.

## Other files

* `.OGG`: music (Ogg Vorbis), replaced directly.
* `.FX` / `.FXC` / `.FXH`: HLSL shaders, as source and compiled.
* `.WMV`: videos, copies of the files in `videos\`.
* `.TGA` (in the archive): two small mask images.
