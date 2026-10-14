# Changelog

## v1.2.1
* Fixed: a font folder was silently ignored when it was not inside a `<BANK.ARC>\` folder, or when the mod
  folder itself was the bank folder (`mods\A_MAINMENU_FONTSARCHIVE.ARC\`). Now:
  * `mods\X\<name>.font\` is applied to **every bank** that has a font with that name (`mainmenu`: 14 banks);
  * a mod folder named like a bank (`*.ARC`, `*.HDR`) is treated as that bank;
  * a folder with an HWBT `font.txt` is recognised even without the `.font` suffix;
  * a mod that matches nothing is reported, with examples of the expected layout.

## v1.2
* **New buttons**: "Fonts ARC → PNG/TXT…" and "Sounds HDR → WAV/OGG…". "Textures ARC → PNG/TGA…" now writes only
  images (PNG, or TGA for textures that were TGA). All three export straight into `<BANK.ARC>\` / `<BANK>.HDR\`
  folders, the same layout a mod uses. Renamed "Install / uninstall mods" (unchecked mods are removed) and
  "Unpack and decompile…".
* **Fonts**: the bitmap fonts (entry type 0x0C in `*_FONTSARCHIVE.ARC` and `GLOBALMENUASSETS*.ARC`, 78 fonts)
  are exported as `<name>.font\` folders: `font.txt` (glyph rectangles, character codes, width, advance and
  kerning pairs) plus `atlas.png`. Put such a folder into `mods\X\<BANK.ARC>\` to rebuild the font. Glyphs can
  be added (up to 253), and the atlas can be resized: `atlassize` keeps the coordinates valid when only the
  image is upscaled. Unpack and decompile writes `fonts\` instead of `.bin` for them. CLI: `font-export`,
  `font-import`.

## v1.1.1
* Unpack & decompile also writes the music: `decompiled\music\*.OGG` plus an `index.txt` that says where each
  track plays (main menu / race / results screen, from the `MUSICARC`, `IGMUSICARC` and `RESULTSMUSIC` indexes).
  Videos are not copied, because they are already in the game's `videos\` folder.

## v1.1
* **Unpack & decompile**: full extraction of everything inside the game files. Textures go to PNG (optionally
  DDS), texts to TXT, sound banks (`.HDR/.RAW`) to WAV, and speech banks (`.HDR/.SPD`) to separate OGG files
  named after the phrases. Every other arcbank entry is written as `.bin`, with an `index.txt` per bank.
  New CLI commands: `unpack-all`, `decompile`, `hdr2wav`.
* **Sound and speech banks in mods**: a `NAME.HDR\` folder with `.wav` files rebuilds `NAME.HDR` + `NAME.RAW`,
  and one with `.ogg` files rebuilds a speech bank (`NAME.HDR` + `NAME.SPD`, including the stream offset and
  duration tables). Extra files are added as new sounds or phrases. New CLI commands: `make-soundbank`,
  `make-speechbank`.
* **Texture compression**: PNG/TGA imports are encoded into the original texture's format (DXT1/DXT3/DXT5,
  A4R4G4B4, A1R5G5B5, R5G6B5, A8R8G8B8…) with the original mip-map count, instead of becoming uncompressed.
* **TGA support**: textures named `*.tga` in the game are exported as TGA (option: convert everything to PNG),
  and `.tga` can be imported.
* **High-resolution warnings**: entries over 16 MB are refused; textures above 2048, large growth and big banks
  are reported.
* Fixed: importing PNGs with semi-transparent pixels changed their colours (GDI+ alpha blending).
* `docs/FORMATS.md` is now English, with a Russian copy in `docs/FORMATS.ru.md`, and documents the sound and
  speech banks.
* Folder selection now uses the modern Explorer-style dialog instead of the old tree dialog.

## v1.0
* Unpack and repack `CDFILES.DAT` + `ARCHIVE0.AR` (byte-identical rebuild of the retail archives).
* In-place mod installation for `Data` and `lData` with backup and rollback.
* Mod folders: replace files, compile `TEXTxxxx.txt` into `.LDA`, inject `.png`/`.dds` into `.ARC` banks.
* Export textures to PNG/DDS (DXT1/3/5, A8R8G8B8, A4R4G4B4, A1R5G5B5, ...) and text tables to TXT.
* Community Patches V1 and V2 by Kyubikkumasuta (Beat That! Revival): download from the official links
  with a SHA-256 check, install and remove them with the author's own scripts, and show their status.
* English / Russian interface.
* Credits line with links to all authors (Technyx by Lukas Cone (PredatorCZ); community patches by
  Kyubikkumasuta, linking to the official website).
* GUI and command-line interface.
