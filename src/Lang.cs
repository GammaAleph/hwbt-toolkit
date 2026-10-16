// GUI localisation: English / Russian. The choice is stored in %APPDATA%\HWBT-Toolkit\settings.ini.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Hwbt
{
    public static class Lang
    {
        public static readonly string[] Names = { "English", "Русский" };
        public static int Current; // 0 = English, 1 = Russian

        static readonly Dictionary<string, string[]> S = new Dictionary<string, string[]>
        {
            { "admin", new[] { " [Administrator]", " [Администратор]" } },
            { "language", new[] { "Language:", "Язык:" } },
            { "gameDir", new[] { "Game folder:", "Папка игры:" } },
            { "modsDir", new[] { "Mods folder:", "Папка модов:" } },
            { "modsGroup", new[] { "Mods (each sub-folder is a mod; checked mods are installed, lower ones win)",
                                   "Моды (каждая подпапка = мод; отмеченные будут установлены, нижние в списке приоритетнее)" } },
            { "btnInstall", new[] { "Install / uninstall mods", "Установить / удалить моды" } },
            { "btnRestore", new[] { "Restore original", "Вернуть оригинал" } },
            { "btnRefresh", new[] { "Refresh list", "Обновить список" } },
            { "btnOpenMods", new[] { "Open mods folder", "Открыть папку модов" } },
            { "btnUnpack", new[] { "Unpack archive…", "Распаковать архив…" } },
            { "chkSaveLogs", new[] { "Save a log file when installing / uninstalling mods (logs\\ folder)",
                                     "Сохранять лог при установке / удалении модов (папка logs\\)" } },
            { "chkRebuild", new[] { "Rebuild the archive when installing (replaced files are removed, the original archive is backed up)",
                                    "Пересобирать архив при установке (заменённые файлы удаляются, оригинал архива сохраняется в бэкап)" } },
            { "rebuildInfo", new[] { "Rebuild mode: the archives are written anew without the replaced files; the originals are kept as ARCHIVE0.AR.hwbt_original (\"Restore original\" puts them back).",
                                     "Режим пересборки: архивы собираются заново без заменённых файлов; оригиналы сохраняются как ARCHIVE0.AR.hwbt_original («Вернуть оригинал» возвращает их)." } },
            { "logSaved", new[] { "Log saved: ", "Лог сохранён: " } },
            { "logSaveFail", new[] { "Could not save the log: ", "Не удалось сохранить лог: " } },
            { "chkAllPng", new[] { "Convert all textures to PNG (otherwise .tga textures stay .tga)",
                                   "Конвертировать все текстуры в PNG (иначе .tga остаются .tga)" } },
            { "btnDecompile", new[] { "Unpack and decompile…", "Распаковать и декомпилировать…" } },
            { "askDecompileDir", new[] { "Where to unpack and decompile? (Data and lData sub-folders will be created)",
                                         "Куда распаковать и декомпилировать? (будут созданы подпапки Data и lData)" } },
            { "decompileInfo", new[] {
                "Full unpack: raw files -> files\\, textures -> PNG/TGA, fonts -> PNG/TXT, 3D models -> OBJ, texts -> TXT, sounds -> WAV, speech and music -> OGG, other bank entries -> .bin (needs ~6 GB, takes several minutes)…",
                "Полная распаковка: файлы -> files\\, текстуры -> PNG/TGA, шрифты -> PNG/TXT, 3D-модели -> OBJ, тексты -> TXT, звуки -> WAV, речь и музыка -> OGG, прочие записи банков -> .bin (нужно ~6 ГБ, займёт несколько минут)…" } },
            { "pickHdr", new[] { "Select sound banks (.HDR from the unpacked archive; .RAW/.SPD must be next to them)",
                                 "Выберите звуковые банки (.HDR из распакованного архива; рядом должны лежать .RAW/.SPD)" } },
            { "askSoundDir", new[] { "Where to save the sounds?", "Куда сохранить звуки?" } },
            { "soundCount", new[] { "{0}: {1} file(s)", "{0}: файлов: {1}" } },
            { "soundMusicIndex", new[] { "{0}: music index only (the tracks are the .OGG files of the archive)", "{0}: только индекс музыки (сами треки — файлы .OGG в архиве)" } },
            { "soundNoData", new[] { "{0}: {1} not found next to it - skipped", "{0}: рядом нет {1} - пропущен" } },
            { "soundHint", new[] {
                "To mod: put the edited .wav/.ogg (same names) into mods\\<YourMod>\\<BANK>.HDR\\",
                "Для мода: положите изменённые .wav/.ogg (с теми же именами) в mods\\<ВашМод>\\<БАНК>.HDR\\" } },
            { "pickArcFonts", new[] { "Select .ARC files with fonts (*_FONTSARCHIVE.ARC, GLOBALMENUASSETS*.ARC)",
                                      "Выберите .ARC файлы со шрифтами (*_FONTSARCHIVE.ARC, GLOBALMENUASSETS*.ARC)" } },
            { "askFontDir", new[] { "Where to save the fonts?", "Куда сохранить шрифты?" } },
            { "fontNone", new[] { "{0}: no fonts in this bank", "{0}: в этом банке нет шрифтов" } },
            { "btnTex", new[] { "Textures ARC → PNG/TGA…", "Текстуры ARC → PNG/TGA…" } },
            { "btnFonts", new[] { "Fonts ARC → PNG/TXT…", "Шрифты ARC → PNG/TXT…" } },
            { "btnSounds", new[] { "Sounds HDR → WAV/OGG…", "Звуки HDR → WAV/OGG…" } },
            { "btnLda", new[] { "Texts LDA → TXT…", "Тексты LDA → TXT…" } },
            { "btnModels", new[] { "Models ARC → OBJ…", "Модели ARC → OBJ…" } },
            { "pickArcModels", new[] { "Select .ARC files with 3D models (cars: *_LOD0_GRA / *_LODX_GRA, tracks: ZONE*A)",
                                       "Выберите .ARC файлы с 3D-моделями (машины: *_LOD0_GRA / *_LODX_GRA, трассы: ZONE*A)" } },
            { "askModelDir", new[] { "Where to save the models?", "Куда сохранить модели?" } },
            { "modelCount", new[] { "{0}: {1} model(s), {2:N0} triangles, {3} texture(s) -> {4}", "{0}: моделей {1}, треугольников {2:N0}, текстур {3} -> {4}" } },
            { "modelNone", new[] { "{0}: no 3D models in this bank", "{0}: в этом банке нет 3D-моделей" } },
            { "modelHint", new[] { "Open the .obj in Blender (File > Import > Wavefront). Cars: LOD0 = close-up, _lod1.._lod3 = distance versions.",
                                   "Откройте .obj в Blender (File > Import > Wavefront). Машины: LOD0 — вблизи, _lod1.._lod3 — для расстояния." } },
            { "communityGroup", new[] { "Community patches by Kyubikkumasuta - V1: music fixes; V2: V1 + track order, look-behind camera, full controller support",
                                        "Патчи сообщества от Kyubikkumasuta - V1: исправление музыки; V2: V1 + порядок треков, камера назад, поддержка геймпада" } },
            { "btnCpInstallV1", new[] { "Install Patch V1", "Установить патч V1" } },
            { "btnCpInstallV2", new[] { "Install Patch V2", "Установить патч V2" } },
            { "btnCpRestore", new[] { "Remove patch", "Удалить патч" } },
            { "btnCpSite", new[] { "Official website", "Официальный сайт" } },
            { "cpInstalledV1", new[] { "Status: Patch V1 installed", "Статус: установлен патч V1" } },
            { "cpInstalledV2", new[] { "Status: Patch V2 installed", "Статус: установлен патч V2" } },
            { "cpNotInstalled", new[] { "Status: not installed", "Статус: не установлен" } },
            { "cpUnknown", new[] { "Status: unknown (game folder not found)", "Статус: неизвестен (папка игры не найдена)" } },
            { "credits", new[] { "Archive format research: Technyx by Lukas Cone (PredatorCZ)  ·  Community Patches V1/V2: Kyubikkumasuta (Beat That! Revival)  ·  Please support the original authors!",
                                 "Формат архивов: Technyx от Lukas Cone (PredatorCZ)  ·  Патчи сообщества V1/V2: Kyubikkumasuta (Beat That! Revival)  ·  Поддержите оригинальных авторов!" } },
            { "supportLink", new[] { "Please support the original authors!", "Поддержите оригинальных авторов!" } },
            { "cpV2HasV1", new[] { "Note: Patch V2 already contains all Patch V1 fixes - installing V2 alone is enough.",
                                   "Примечание: патч V2 уже содержит все исправления V1 - достаточно установить только V2." } },
            { "gameFound", new[] { "Game: found", "Игра: найдена" } },
            { "gameNotFound", new[] { "Game: NOT found - select the game folder", "Игра: НЕ найдена - укажите папку игры" } },
            { "done", new[] { "Done.", "Готово." } },
            { "error", new[] { "ERROR: ", "ОШИБКА: " } },
            { "noAccess", new[] { "ERROR: access denied ", "ОШИБКА: нет доступа " } },
            { "noModsChecked", new[] { "No mods checked - restoring the original archives.", "Ни один мод не отмечен - возвращаю оригинальные архивы." } },
            { "askUnpackDir", new[] { "Where to unpack? (Data and lData sub-folders will be created)", "Куда распаковать архив? (будут созданы подпапки Data и lData)" } },
            { "pickArc", new[] { "Select .ARC files (from the unpacked archive)", "Выберите .ARC файлы (из распакованного архива)" } },
            { "askTexDir", new[] { "Where to save the textures?", "Куда сохранить текстуры?" } },
            { "templateCreated", new[] { "Mod template created: {0} (copy it, rename the copy and put your files into Data\\ / lData\\)",
                                         "Создан шаблон мода: {0} (скопируйте, переименуйте копию и кладите файлы в Data\\ / lData\\)" } },
            { "fontsExported", new[] { "{0}: {1} font(s)", "{0}: шрифтов: {1}" } },
            { "fontHint", new[] {
                "To mod: copy <BANK.ARC>\\<name>.font\\ into mods\\<YourMod>\\ and edit font.txt and/or atlas.png there.",
                "Для мода: скопируйте <БАНК.ARC>\\<имя>.font\\ в mods\\<ВашМод>\\ и правьте там font.txt и/или atlas.png." } },
            { "texCount", new[] { "{0}: {1} textures", "{0}: {1} текстур" } },
            { "texHint1", new[] { "To put textures into the game: in your mod folder create a folder named like the bank (e.g. CAR_LOD0_GRA.ARC)",
                                  "Чтобы вставить текстуры в игру: создайте в папке мода папку с именем банка (например CAR_LOD0_GRA.ARC)" } },
            { "texHint2", new[] { "and put ONLY the edited .png/.tga/.dds files there (keep the file names; PNG/TGA are compressed into the original format).",
                                  "и положите туда ТОЛЬКО изменённые .png/.tga/.dds (имена не менять; PNG/TGA сжимаются в исходный формат)." } },
            { "ldaFilter", new[] { "Texts (*.LDA)|*.LDA", "Тексты (*.LDA)|*.LDA" } },
            { "ldaHint", new[] { "Put the edited TEXTxxxx.txt into a mod folder - it is compiled during installation.",
                                 "Положите изменённый TEXTxxxx.txt в папку мода - он будет скомпилирован при установке." } },
            { "gameDirBad", new[] { "Data\\CDFILES.DAT not found in the game folder: ", "В папке игры не найден Data\\CDFILES.DAT: " } },
            { "gameRunning", new[] { "The game (HOT.exe) is running - close it first.", "Игра запущена (HOT.exe) - закройте её." } },
            { "elevateTitle", new[] { "Access denied", "Нет доступа" } },
            { "elevateText", new[] { "Administrator rights are required to write into the game folder (Program Files).\nRestart the program as administrator?",
                                     "Для записи в папку игры (Program Files) нужны права администратора.\nПерезапустить программу от имени администратора?" } },
            { "elevateFail", new[] { "Could not restart: ", "Не удалось перезапуститься: " } },
            { "cpTitle", new[] { "Community patch", "Патч сообщества" } },
            { "cpAskDownload", new[] {
                "Community {0} by Kyubikkumasuta is not downloaded yet.\n\nDownload it now from the author's official link (Google Drive)?\n{1}",
                "{0} сообщества от Kyubikkumasuta ещё не скачан.\n\nСкачать его сейчас по официальной ссылке автора (Google Drive)?\n{1}" } },
            { "cpDownloading", new[] { "Downloading community {0}…", "Скачиваю {0}…" } },
            { "cpDownloaded", new[] { "Downloaded and verified: ", "Скачан и проверен: " } },
            { "cpHashMismatch", new[] {
                "The downloaded file differs from the version verified by HWBT Toolkit (the author may have published an update).\n\nSHA-256: {0}\n\nUse it anyway?",
                "Скачанный файл отличается от версии, проверенной HWBT Toolkit (возможно, автор выпустил обновление).\n\nSHA-256: {0}\n\nВсё равно использовать?" } },
            { "cpAskInstall", new[] {
                "Install community {0}?\n\nThe patch files will be copied to\n{1}\nand the author's installer will run in a console window (it backs up HOT.exe first).\nThe game must be closed.",
                "Установить {0} сообщества?\n\nФайлы патча будут скопированы в\n{1}\nи запустится установщик автора в окне консоли (он сначала делает копию HOT.exe).\nИгра должна быть закрыта." } },
            { "cpAskRestore", new[] { "Remove community {0} and restore HOT.exe from the author's backup?", "Удалить {0} сообщества и восстановить HOT.exe из резервной копии автора?" } },
            { "Patch V1", new[] { "Patch V1", "Патч V1" } },
            { "Patch V2", new[] { "Patch V2", "Патч V2" } },
            { "cpNothing", new[] { "No community patch is installed.", "Патч сообщества не установлен." } },
            { "cpRunning", new[] { "Running the patch script (see the console window)…", "Запущен скрипт патча (см. окно консоли)…" } },
            { "cpExit", new[] { "Patch script finished, exit code {0}.", "Скрипт патча завершён, код {0}." } },
            { "cpNoRestore", new[] { "{0} not found in Bin - cannot remove.", "В Bin нет {0} - удалить невозможно." } },
        };

        public static string T(string key)
        {
            string[] v;
            return S.TryGetValue(key, out v) ? v[Current] : key;
        }

        public static string F(string key, params object[] args) { return string.Format(T(key), args); }

        static string SettingsFile
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HWBT-Toolkit", "settings.ini"); }
        }

        // Other persisted GUI options.
        public static bool AllPng; // export .tga-named textures as PNG too
        public static bool SaveLogs; // write logs\*.log for mod installs / restores
        public static bool Rebuild;  // install mods by rebuilding the archives (Archive.Repack) instead of appending

        public static void Load()
        {
            Current = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? 1 : 0;
            try
            {
                foreach (var line in File.ReadAllLines(SettingsFile))
                {
                    if (line.StartsWith("lang=")) Current = line.Substring(5).Trim() == "ru" ? 1 : 0;
                    if (line.StartsWith("allpng=")) AllPng = line.Substring(7).Trim() == "1";
                    if (line.StartsWith("savelogs=")) SaveLogs = line.Substring(9).Trim() == "1";
                    if (line.StartsWith("rebuild=")) Rebuild = line.Substring(8).Trim() == "1";
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
                File.WriteAllText(SettingsFile, "lang=" + (Current == 1 ? "ru" : "en") + "\r\nallpng=" + (AllPng ? "1" : "0")
                    + "\r\nsavelogs=" + (SaveLogs ? "1" : "0") + "\r\nrebuild=" + (Rebuild ? "1" : "0") + "\r\n");
            }
            catch { }
        }
    }
}
