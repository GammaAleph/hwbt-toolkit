# Форматы файлов Hot Wheels: Beat That! (PC)

Все числа little-endian, если не указано иное.

## CDFILES.DAT (`filC`, версия 5) — индекс архива

| Смещение | Тип          | Поле                                               |
|---------:|--------------|----------------------------------------------------|
| 0x00     | char[4]      | `filC` (C = PC; P = PS2, X = Xbox, E = X360, N = Wii) |
| 0x04     | u32          | версия = 5                                          |
| 0x08     | u32          | 0x40000000 (float 2.0, версия кода)                |
| 0x0C     | u32[11]      | unk2, unk4, rootPathSize, unk3, numSearchPaths, workingPathSize, **numTotalFiles**, archivePathLength, **alignment (2048)**, **numFiles**, unkSize |
| …        | u32[numSearchPaths] | смещения путей поиска (здесь 0)             |
| …        | u32[2]       | 0                                                  |
| …        | char[workingPathSize] |                                           |
| 0x40     | char[16]     | `\ARCHIVE0.AR`                                     |
| 0x50     | u32[numTotalFiles] | смещение данных **в блоках по 2048 байт**     |
| …        | u32[numTotalFiles] | размер данных в байтах                       |
| …        | u32[numFiles] | смещение имени в дереве имён                     |
| …        | u32[numFiles] | id: биты 0–27 = индекс данных, 28–31 = тип (4 = StreamFile) |
| …        | u32[numFiles] | доп. данные                                       |
| …        | u32 n, u32 size, u32[n] offsets, char[size] | таблица фрагментов имён |
| …        | u32 + 128 байт | резерв                                           |
| …        | дерево имён  | каждое имя — последовательность индексов фрагментов (1 байт, или 2 байта big-endian при старшем бите), завершается 0; индексы с 1 |

В игре 2141 файл, имена плоские, без папок. Два имени повторяются (`DEFAULT_STANDARD2D.FX/.FXC`); при распаковке
второй экземпляр получает имя `ИМЯ~1.EXT`.

## ARCHIVE0.AR — данные

Файлы лежат друг за другом и выравниваются на 2048 байт. Промежутки заполнены **пробелами (0x20)**.
Ни сжатия, ни шифрования нет. `Data\` содержит версию высокой детализации, `lData\` — низкой: те же имена файлов,
но текстуры в `.ARC` меньше.

## .ARC — arcbank (`ARCC`)

| Смещение | Тип     | Поле                                      |
|---------:|---------|-------------------------------------------|
| 0x00     | char[4] | `ARCC`                                    |
| 0x04     | u32     | число записей (младшие 24 бита)           |
| 0x08     | u16[…]  | счётчики (текстуры, модели, …)            |
| 0x80     | entry[n]| записи по 16 байт                          |
| 0x80+16n | …       | данные; смещения в записях считаются от этой точки |

Запись: `u32 id/hash, u32 offset, i32 nameOffset (-1 = без имени), u8 type, u8[3] size (big-endian)`.
Данные идут по порядку, с выравниванием на 16 байт и заполнением нулями.

Типы записей: 0x00 PlainData, 0x01 Texture, 0x02 Material, 0x09 Mesh, 0x0F IndexBuffer, 0x10 VertexBuffer,
0x1C Attachment, 0x1D Model, 0x1E DeformedModel, 0x21 Skeleton, 0x29 Animation, 0xFD EntryNames (строки имён),
0xFF Group.

### Текстура

```
u32 width, height, numMips, hash, format
mip0, mip1, ...   // как в D3D9 / DDS
```
`format` — это значение D3DFORMAT: 21 = A8R8G8B8, 25 = A1R5G5B5, 26 = A4R4G4B4, 41 = P8 (палитра),
`DXT1`/`DXT3`/`DXT5` (FourCC). Форматы текстур в игре: A4R4G4B4 — 7661, DXT3 — 2414, DXT1 — 435,
A8R8G8B8 — 23, A1R5G5B5 — 1.

## .LDA — таблица строк

```
char[4] 'lda0' (cp1252) | 'lda1' (UTF-16LE)
u32 fileSize, u32 id0, u32 id1, u32 count
u32 offsets[count], u32 stringsSize
строки, каждая завершается NUL
```

## Прочее

* `.OGG` — музыка (Ogg Vorbis), заменяется напрямую.
* `.HDR` + `.RAW` — звуковые банки (см. `hdr_to_wav` в Technyx).
* `.FX` / `.FXC` / `.FXH` — шейдеры HLSL: исходник и скомпилированная версия.
* `.WMV` — ролики, копии файлов из папки `videos\`.
