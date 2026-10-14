# HWBT Toolkit: modding tools for Hot Wheels: Beat That! (PC)

[Русская версия](README.ru.md)

HWBT Toolkit unpacks and repacks the game's archives, converts textures to and from PNG/DDS, converts text
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

1. Download `HWBT-Toolkit-v1.0.zip` from [Releases](https://github.com/GammaAleph/hwbt-toolkit/releases)
   and unzip it anywhere.
2. Run `hwbt.exe`. The language selector is at the top of the window.

The toolkit requires .NET Framework 4.5 or later, which is built into Windows 8, 10 and 11.

## Features

| Button                         | What it does                                                         |
|--------------------------------|----------------------------------------------------------------------|
| Install mods                   | installs every checked mod from `mods\` into `Data\` and `lData\`    |
| Restore original               | reverts both archives byte-for-byte                                  |
| Unpack archive…                | extracts all 2,141 game files                                        |
| Textures ARC → PNG/DDS…        | exports the textures of `.ARC` banks                                 |
| Texts LDA → TXT…               | converts string tables to editable text                              |
| Install Patch V1 / V2          | downloads the author's patch (SHA-256 verified) and runs its installer |
| Remove patch                   | runs the author's restore script for the installed patch            |

### Mod folder layout

Each sub-folder of `mods\` is one mod. Folders whose names start with `_` are ignored.

| Put this in the mod folder            | Result                                          |
|---------------------------------------|-------------------------------------------------|
| `NAME.EXT` (any archive file)         | replaces that file (`TITLE.OGG`, `CAR.ARC`, …)  |
| `TEXT0000.txt`                        | compiled into `TEXT0000.LDA`                    |
| folder `NAME.ARC\` with `*.png/*.dds` | textures injected into the original bank        |

Example: `mods\BlueCar\16ANGELS_LOD0_GRA.ARC\16AngelsBodyDA.png`.
The formats are described in [docs/FORMATS.md](docs/FORMATS.md).

### Command line

```
hwbt info | list <Data>
hwbt unpack <Data> <out> [mask]
hwbt pack <Data> <modDir> <out>
hwbt patch <Data> <modDir> [modDir2 ...]     hwbt restore | uninstall <Data>
hwbt install-mods <GameDir> <mods>           hwbt uninstall-mods <GameDir>
hwbt lda2txt <file.LDA|dir> [out]            hwbt txt2lda <file.txt> [out.LDA]
hwbt arc-list <file.ARC>
hwbt tex-export <file.ARC|dir> <out> [png|dds]
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
| `src/TexImage.cs`        | DXT1/3/5 and 16/32-bit decoders, PNG            |
| `src/Lda.cs`             | `.LDA` string tables                            |
| `src/CommunityPatch.cs`  | download/install of community patches V1/V2     |
| `src/Lang.cs`            | EN/RU localisation                              |
| `src/Gui.cs`             | window                                          |
| `src/Program.cs`         | command line                                    |

## Verified

* Rebuilding `Data` and `lData` without changes reproduces the original archives byte-for-byte.
* All 50 text tables and all 10,534 textures survive an export/import round trip unchanged.
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
