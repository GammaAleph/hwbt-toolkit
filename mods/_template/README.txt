HWBT Toolkit - mod template
===========================

HOW TO USE
  1. Copy this folder and rename the copy (the name must NOT start with "_": such folders are never installed).
  2. Put only the files you changed into it, laid out as below.
  3. Click "Install / uninstall mods".

  Data\    high-detail game data  (...\Hot Wheels\Data)
  lData\   low-detail game data   (...\Hot Wheels\lData)
  Files placed directly in the mod folder (outside Data\ and lData\) go into BOTH.

  Inside Data\ and lData\ the layout is the same as the output of "Unpack and decompile", so files can be
  copied straight from there (its Data\decompiled\... and lData\decompiled\... folders):

    arc\<BANK.ARC>\textures\<name>.png|tga|dds   textures
    arc\<BANK.ARC>\fonts\<name>.font\             fonts (font.txt and/or atlas.png)
    texts\TEXTxxyy.txt                            texts
    sounds\<BANK>\<name>.wav                      sound effects
    speech\<BANK>\<name>.ogg                      commentator
    music\<TRACK>.OGG                             music
    files\<NAME>                                  any archive file, replaced as a whole

  You may also copy complete folders: files identical to the game are detected and skipped, only what you
  edited is installed.

WHAT DIFFERS BETWEEN Data AND lData (checked on the PC release)
  - 288 of the 1820 .ARC banks: low-detail models and textures (other texture names, sizes and formats)
  - 17 zone sound banks (ZONE*.HDR / .RAW): other sounds and sample rates
  - music (.OGG) and commentary (.SPD): the same audio in both
  - texts, shaders, videos, car engine sounds: identical
  So textures, models and zone sounds usually need their own lData version; texts and music can go into the
  mod folder itself and are then installed into both.


HWBT Toolkit - шаблон мода
==========================

КАК ПОЛЬЗОВАТЬСЯ
  1. Скопируйте эту папку и переименуйте копию (имя НЕ должно начинаться с "_": такие папки не устанавливаются).
  2. Положите в неё только изменённые файлы, как показано ниже.
  3. Нажмите "Установить / удалить моды".

  Data\    данные высокой детализации  (...\Hot Wheels\Data)
  lData\   данные низкой детализации   (...\Hot Wheels\lData)
  Файлы, лежащие прямо в папке мода (вне Data\ и lData\), ставятся в ОБЕ.

  Внутри Data\ и lData\ всё устроено так же, как после "Распаковать и декомпилировать", поэтому файлы можно
  копировать прямо оттуда (из папок Data\decompiled\... и lData\decompiled\...):

    arc\<БАНК.ARC>\textures\<имя>.png|tga|dds     текстуры
    arc\<БАНК.ARC>\fonts\<имя>.font\               шрифты (font.txt и/или atlas.png)
    texts\TEXTxxyy.txt                             тексты
    sounds\<БАНК>\<имя>.wav                        звуки
    speech\<БАНК>\<имя>.ogg                        комментатор
    music\<ТРЕК>.OGG                               музыка
    files\<ИМЯ>                                    любой файл архива целиком

  Можно копировать и папки целиком: файлы, совпадающие с игрой, распознаются и пропускаются, ставится только то,
  что вы изменили.

ЧЕМ ОТЛИЧАЮТСЯ Data И lData (проверено на PC-версии)
  - 288 из 1820 банков .ARC: модели и текстуры низкой детализации (другие имена, размеры и форматы текстур)
  - 17 звуковых банков уровней (ZONE*.HDR / .RAW): другие звуки и частоты
  - музыка (.OGG) и комментатор (.SPD): звук одинаковый
  - тексты, шейдеры, видео, звуки моторов: полностью одинаковые
  Поэтому текстурам, моделям и звукам уровней обычно нужна отдельная версия для lData, а тексты и музыку можно
  класть прямо в папку мода — тогда они ставятся в обе.
