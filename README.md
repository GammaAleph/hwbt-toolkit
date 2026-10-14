# HWBT Toolkit: modding tools for Hot Wheels: Beat That! (PC)

[Русская версия](README.ru.md)

HWBT Toolkit unpacks and repacks the game's archives, converts textures to and from PNG/TGA/DDS, converts fonts, text
tables, and installs mods with automatic rollback. It can also install the **Beat That! Revival** community
patches V1 and V2. The interface is available in English and Russian.

Hot Wheels: Beat That! was developed by Eutechnyx in 2007 and published by Activision. It runs on Eutechnyx's
in-house engine.

## ❤️ Support the original authors

This toolkit stands on the work of other people. If you enjoy it, please support them:

* **Kyubikkumasuta** created the **[Beat That! Revival](https://hot-wheels-beat-that-revival.com/)**
  community patches, which restore the music and add controller support, the look-behind camera and more.
  Please visit the [official website](https://hot-wheels-beat-that-revival.com/) and support the author there.
  HWBT Toolkit only *downloads* the patches from the author's official links and runs the author's own
  installers. The patches are not redistributed here.
* **Lukas Cone (PredatorCZ)** wrote **[Technyx](https://github.com/PredatorCZ/Technyx)**, a toolset for
  Eutechnyx games. The archive formats used here were worked out from its sources. Please give the
  project a star.

## Credits

* **[Technyx](https://github.com/PredatorCZ/Technyx) by [Lukas Cone (PredatorCZ)](https://github.com/PredatorCZ)**
  (GPL-3.0). The formats of `CDFILES.DAT` / `ARCHIVE.AR` and of the `.ARC` banks were researched from its source
  code. HWBT Toolkit is an independent C# implementation that adds repacking.
* **Community Patches V1 and V2 by [Kyubikkumasuta](https://hot-wheels-beat-that-revival.com/)**
  ([Beat That! Revival](https://hot-wheels-beat-that-revival.com/)):
  * **V1** fixes soundtrack selection, music looping and the results-screen music.
  * **V2** includes everything in V1 and adds the correct track order, music after "GO", a look-behind camera,
    a new control scheme and full controller support.
* **aluigi**, together with the ZenHAX and ResHax communities, did the early format research and wrote the
  `namco_games_ar.bms` QuickBMS script.

## Install

1. Download `HWBT-Toolkit-v1.2.2.zip` from [Releases](https://github.com/GammaAleph/hwbt-toolkit/releases)
   and unzip it anywhere.
2. Run `hwbt.exe`. The language selector is at the top of the window.

The toolkit requires .NET Framework 4.5 or later, which is built into Windows 8, 10 and 11.

## Features

| Button                         | What it does                                                         |
|--------------------------------|----------------------------------------------------------------------|
| Install / uninstall mods       | installs every checked mod from `mods\` into `Data\` and `lData\`; unchecked mods are removed |
| Restore original               | reverts both archives byte-for-byte                                  |
| Unpack archive…                | extracts all 2,141 game files                                        |
| Unpack and decompile…          | extracts everything and unpacks the files' contents: textures → PNG/TGA, fonts → PNG/TXT, texts → TXT, sounds → WAV, speech and music → OGG, all other `.ARC` entries → `.bin` + `index.txt` |
| Textures ARC → PNG/TGA…        | exports the textures of selected `.ARC` banks into `<BANK.ARC>\`     |
| Fonts ARC → PNG/TXT…           | exports the fonts of selected `.ARC` banks into `<BANK.ARC>\<name>.font\` |
| Sounds HDR → WAV/OGG…          | exports selected sound banks (`.wav`) and speech banks (`.ogg`) into `<BANK>.HDR\` |
| Texts LDA → TXT…               | converts string tables to editable text                              |
| ☐ Convert all textures to PNG  | textures named `*.tga` in the game are exported as TGA unless this is checked |
| Install Patch V1 / V2          | downloads the author's patch (SHA-256 verified) and runs its installer |
| Remove patch                   | runs the author's restore script for the installed patch            |

### Mod folder layout

Each sub-folder of `mods\` is one mod. Folders whose names start with `_` are ignored.

| Put this in the mod folder                    | Result                                          |
|-----------------------------------------------|-------------------------------------------------|
| `NAME.EXT` (any archive file)                 | replaces that file (`TITLE.OGG`, `CAR.ARC`, …)  |
| `TEXT0000.txt`                                | compiled into `TEXT0000.LDA`                    |
| folder `NAME.ARC\` with `*.png` `*.tga` `*.dds` | textures injected into the original bank     |
| folder `NAME.ARC\FONT.font\` (`font.txt` and/or `atlas.png`) | font rebuilt (glyphs, spacing, atlas)  |
| folder `NAME.HDR\` with `*.wav`               | sound bank rebuilt (`NAME.HDR` + `NAME.RAW`)    |
| folder `NAME.HDR\` with `*.ogg`               | speech bank rebuilt (`NAME.HDR` + `NAME.SPD`)   |

Examples:
* `mods\BlueCar\16ANGELS_LOD0_GRA.ARC\16AngelsBodyDA.png`
* `mods\NewEngine\16ANGELS_SND.HDR\h3_4k_2.wav`
* `mods\Commentary\COMM.HDR\321GO.ogg`

Use the file names that **Unpack and decompile** produces. You can also copy a whole bank folder from its
output (`arc\<BANK.ARC>\` with `textures\`) into `mods\`. Images that weren't changed are detected and left
untouched, and `index.txt` / `entries\` are ignored. PNG and TGA textures are compressed into the original
texture's format (DXT1/DXT3/A4R4G4B4…) with the same number of mip-maps, so the bank keeps its size. A `.dds` is
stored exactly as it is (use it to pick a different format). Extra `.wav`/`.ogg` files that don't match an
existing name are added to the bank as new sounds or phrases. The game only plays them if something
references them, though.
The formats are described in [docs/FORMATS.md](docs/FORMATS.md).

### Fonts

The menu and HUD fonts are bitmap fonts in `*_FONTSARCHIVE.ARC` and `GLOBALMENUASSETS*.ARC`. Unpack and decompile
(or the **Fonts ARC → PNG/TXT…** button) writes each one as a `<name>.font\` folder:

* `atlas.png` is the picture with all the characters. Redraw them, move them, or add new ones.
* `font.txt` lists one line per glyph: character code(s), rectangle in the atlas, width and advance. It also
  holds `kern` lines with the spacing exceptions for particular pairs.

Copy the folder to `mods\YourMod\A_MAINMENU_FONTSARCHIVE.ARC\mainmenu.font\`, edit it, and install.
* Without `font.txt`, only the atlas is replaced.
* Without the atlas, only the metrics change.
* If you upscale the atlas but keep the layout, leave `atlassize` as it is and the coordinates are scaled for you.
* A `<name>.font\` folder placed directly in the mod (not inside a `<BANK.ARC>\` folder) is applied to **every
  bank that has a font with that name**. For example, `mods\MyFont\mainmenu.font\` changes all 14 copies of `mainmenu`.

Character codes are the bytes of the 8-bit text tables, so new glyphs can use free codes 0x80–0xFF.

### High-resolution textures

The game accepts larger textures in principle, because each texture stores its own size. There are hard limits:

* **16 MB per texture** (an arcbank entry has a 24-bit size). DXT1 4096×4096, DXT3/DXT5 4096×2048 and
  uncompressed 2048×1024 fit. The toolkit refuses anything bigger.
* `HOT.exe` is a 32-bit program **without Large Address Aware**, so it has only 2 GB of memory. Keep big
  textures compressed (PNG/TGA are compressed automatically).
* The engine may use fixed-size load buffers. Test a single upscaled car first (for example 2048×1024),
  then go further.
* Keep the aspect ratio, because the UVs are relative.

The toolkit warns about textures above 2048, entries much larger than the original, and banks that grow a lot.

### Command line

```
hwbt info | list <Data>
hwbt unpack <Data> <out> [mask]
hwbt unpack-all <Data> <out> [--dds]         full unpack + decompile
hwbt decompile <unpackedDir> <out> [--dds]   decompile an already unpacked folder
hwbt font-export <file.ARC> <out> [--dds]    fonts -> <name>.font\ (font.txt + atlas.png)
hwbt font-import <file.ARC> <dir> [out.ARC]  <name>.font\ folders -> fonts in the bank
hwbt hdr2wav <file.HDR> [out]                sound bank -> .wav
hwbt make-soundbank <wavDir> <out.HDR> [template.HDR]     .wav -> new/updated sound bank
hwbt make-speechbank <oggDir> <out.HDR> <template.HDR>    .ogg -> speech bank
hwbt pack <Data> <modDir> <out>
hwbt patch <Data> <modDir> [modDir2 ...]     hwbt restore | uninstall <Data>
hwbt install-mods <GameDir> <mods>           hwbt uninstall-mods <GameDir>
hwbt lda2txt <file.LDA|dir> [out]            hwbt txt2lda <file.txt> [out.LDA]
hwbt arc-list <file.ARC>
hwbt tex-export <file.ARC|dir> <out> [image|png|dds]   image = .tga/.png as originally named
hwbt tex-import <file.ARC> <imgDir> [out.ARC]
```

`<Data>` is a folder containing `CDFILES.DAT` and `ARCHIVE0.AR`, i.e. `…\Hot Wheels\Data` or `…\Hot Wheels\lData`.

## How to build

No Visual Studio is needed. The build uses the C# compiler that ships with Windows (.NET Framework 4.x):

```bat
git clone https://github.com/GammaAleph/hwbt-toolkit.git
cd hwbt-toolkit
build.bat
```

`build.bat` runs `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` on `src\*.cs` and writes
`hwbt.exe`. The equivalent manual command:

```bat
csc /nologo /optimize+ /target:exe /out:hwbt.exe /win32manifest:src\app.manifest ^
    /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
    /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll src\*.cs
```

The code is plain C# 5, so it also builds with Visual Studio or MSBuild (target `net48`).

| File                     | Purpose                                         |
|--------------------------|-------------------------------------------------|
| `src/CdFiles.cs`         | `CDFILES.DAT` index reader/writer               |
| `src/Archive.cs`         | unpack, repack, in-place patch, rollback, mods  |
| `src/ArcBank.cs`         | `.ARC` banks (ARCC)                             |
| `src/Texture.cs`         | texture header, DDS                             |
| `src/TexImage.cs`        | DXT1/3/5 and 16/32-bit decoders, image import   |
| `src/TexEncode.cs`       | DXT1/3/5 and 16/32-bit encoders, mip-maps       |
| `src/Tga.cs`             | TGA reader/writer                               |
| `src/Lda.cs`             | `.LDA` string tables                            |
| `src/Sound.cs`           | `.HDR/.RAW` sound banks → WAV, `.HDR/.SPD` speech → OGG |
| `src/Decompiler.cs`      | full unpack & decompile                         |
| `src/FolderPicker.cs`    | Explorer-style folder dialog                    |
| `src/CommunityPatch.cs`  | download/install of community patches V1/V2     |
| `src/Lang.cs`            | EN/RU localisation                              |
| `src/Gui.cs`             | window                                          |
| `src/Program.cs`         | command line                                    |

## Verified

* Rebuilding `Data` and `lData` without changes reproduces the original archives byte-for-byte.
* All 50 text tables and all 10,534 textures survive an export/import round trip unchanged.
* Re-encoding every game texture from PNG/TGA: uncompressed formats are lossless; DXT averages 49 dB (DXT1)
  and 59 dB (DXT3) PSNR, and every result has exactly the original size.
* All 59 sound and speech banks rebuild byte-for-byte from the extracted `.wav`/`.ogg` files.
* Mod install and rollback were tested on a copy of the game; after rollback the files match the original
  SHA-1 hashes.
* Patch V2 installs on the English `HOT.exe` (SHA-256 `a557a1ca…`) and passes its installer's own
  verification; restoring returns the original hash.
* Patch V1 has been downloaded and its hash verified, but its installer has not been run in testing: it needs
  administrator rights.
* **The mods have not been tested in-game yet**, because the development environment has no Direct3D 9.
  Feedback is welcome.

## Limitations

* `lda0` text tables are 8-bit Windows-1252, so they have no Cyrillic. A translation would also require new fonts.
* Models, physics and scripts inside `.ARC` files can only be replaced as whole files.
* Keep the archive below 2 GB, because the engine most likely uses 32-bit offsets.

## License

[GPL-3.0](LICENSE), the same as Technyx, whose sources were used to research the formats. Hot Wheels is a
trademark of Mattel. The game belongs to Activision and Eutechnyx. This repository contains no game files and
no community patch files.
