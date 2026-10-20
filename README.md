# MyInsideUniverseInterface
Editor and tools for the interface files of Reality Pump games: Earth 2150, KnightShift, Earth 2160, Two Worlds, Two Worlds II, Heli Heroes, WWIII: Black Gold and WWII: Panzer Claws. InterfaceMap compiler/decompiler (byte-for-byte), DDS and .int tools, batch export/import.

## InterfaceTools

One program, `InterfaceTools.exe`, for the interface files of **Earth 2150**, **KnightShift**, **Earth 2160**, **TwoWorlds** and **Two Worlds II**, and of the other Reality Pump games whose interface uses the Earth 2150 format: **Heli Heroes**, **World War III: Black Gold** and **World War II: Panzer Claws** (*Frontline Attack: War over Europe*):

- a graphical editor for the interface layout files (`.dat`),
- the InterfaceMap compiler (`.txt` + bitmaps → `.dat`) and decompiler (`.dat` → `.txt` + bitmaps, verified byte for byte),
- `dump` and `diff` reports of `.dat` files,
- a DDS tool (`.dds` ↔ `.png` + `.json`),
- AddFileInfo (the header of the `.int` files),
- the Work folder pipeline: **Export all** / **Import all** (the former `EXPORT_ALL_INTERFACES.py` / `IMPORT_ALL_INTERFACES.py`).

The files of Heli Heroes, World War III: Black Gold and World War II: Panzer Claws are detected as the Earth 2150 format and handled exactly like it (`compile` without a switch). The built-in item names are those of Earth 2150 (the core control types 0–22 are the same in all games); if they do not fit, turn them off with *View → Show item and state names* or load the names of an original definition file. Colours are shown as in Earth 2150 (`0xAABBGGRR`); *View → Colour byte order* changes it if a game turns out to differ.

It replaces all the former Python scripts. It needs only the .NET Framework 4.8, which is part of Windows 10 and 11 (nothing to install). The names database (item and state names, presets and descriptions) is built into the program.

`.tex` textures are handled by **TexEnv.exe**, a separate program: put it next to `InterfaceTools.exe` (in this `Bin` folder). Without it, export and import skip the `.tex` files and say so in the log.

| File | Purpose |
|---|---|
| `InterfaceTools.exe` | the program (editor and all command line tools) |
| `TexEnv.exe` | the `.tex` texture tool (not part of this package; copy it here) |
| `FORMAT.md` | description of the definition files and the `.dat` files |
| `MISSING_DATS_FOR_OVERLAP\` | your own text `.dat` files: the textures they name are exported / imported with the Overlap tileset commands (see *Export all / Import all*) |

## Starting

- Double-click `InterfaceTools.exe` (or `RUN_INTERFACE_MAP_GUI.bat`) to open the editor. A `.dat` or `.txt` file dropped onto the program (or onto the window) is opened.
- `EXPORT_ALL_INTERFACES.bat` / `IMPORT_ALL_INTERFACES.bat` run `InterfaceTools.exe export-all` / `import-all` on the `Work` folder. The same is available in the editor: *Tools → Export all* / *Import all*, with a log window, options and a Cancel button.
- With a command (see below), the program works as a console tool in `cmd`, PowerShell or a `.bat` file.

## Export all / Import all

```
InterfaceTools export-all [WorkFolder] [--no-tex] [--no-dds] [--no-int] [--dry-run] [--tex-timeout S] [--no-pause]
InterfaceTools import-all [WorkFolder] [--mode text|binary] [--no-tex] [--no-map] [--no-dds] [--no-int]
                          [--dry-run] [--tex-timeout S] [--no-pause]
```

The default Work folder is `..\Work` next to the program. Without `--no-pause` the program waits for Enter at the end when it runs in its own console window.

**Export** processes every subfolder of the Work folder (at any depth). Each folder is handled on its own (the Work folder may hold interfaces of several games), so the tiles per row and the Overlap textures come only from the text `.dat` files of that folder and its subfolders:

1. `.tex`: TexEnv `getBestWidthFromInterfaceTileset` → width hints for the decompiler,
2. `.dat`: decompiled into the same folder (`.txt`, colour-map bitmaps `name@.bmp`, `resource.h` / `DialogsResource.h`). Files that are not interface layouts are reported and skipped,
3. tiles per row of every `.tex`: from the decompiled bitmaps and from the dialog definitions of the text `.dat` files (KnightShift: `dialog Name = Interface\File.tex 1008,756`; tile 256 px, overlap 4 px). The numbers are passed straight to step 4 (no `DialogsTiles.csv` any more).,
4. `.tex` → `.png`: TexEnv `exportTilesetFromInterface N`, and if that fails `exportTexToImg 0`. Textures named in the text `.dat` files of the same folder and its subfolders (dialog and background definitions, e.g. `Interface\DlgGoalsNoTabs.tex`; comments after `//` do not count) use `exportTilesetFromInterfaceOverlap N` instead. Also the textures named in your own text `.dat` files in `Bin\MISSING_DATS_FOR_OVERLAP` (and its subfolders) use the Overlap commands, in every folder - put there hand-written `.dat` files for textures that no `.dat` of the game names (edge cases). Only the texture names are taken from them, not the tiles per row,
5. `.int` → `.def` (content, CRLF) + `.addfileinfo` (header: name, attrib, GUID),
6. at the end, all `.dds` files of the Work folder → `.png` + `.json`.

**Import** processes the Work folder and all its subfolders:

1. `.def` + `.addfileinfo` → `.int`. A `mode: text|binary` line in the `.addfileinfo` file overrides `--mode` (default: text, i.e. CRLF → LF),
2. `.tex` + `.png` → `.tex`: TexEnv `importTilesetToInterface` (`importTilesetToInterfaceOverlap` for the textures named in the text `.dat` files of the same folder and its subfolders, as in the export), and if that fails `importImgToTex 0`. Keep the original `.tex` files after the export: the import writes into them,
3. `.txt` → `.dat`: the format comes from the `// Format:` line in the header of the `.txt` (written by the decompiler); folders exported by the former Python scripts are recognized by their `format_info.format` file. A `.txt` without either is not an interface definition and is skipped,
4. at the end, all `.json` + `.png` → `.dds`.

TexEnv is run with a temporary script file in the TEMP folder, so nothing (`Socket.txt`) is written into the program folder. The log ends with a summary of the files that failed or were skipped, and the exit code is 0 only when everything succeeded.

## Graphical editor

The editor opens a `.dat` file of any of the five formats (the format is detected automatically) and edits its contents directly, so no colour-map bitmaps are needed.

- **Tree on the left:** header, materials, sounds, fonts, CustomData, DialogTemplates and all items with their states, with names such as *Item 3 - Button* / *State 0x10 - Mouseover* (see *Names* below). *Go to item* jumps to an item by number.
- **State editor:**
  - properties (font, sound, property records, FaceColor, particle file, Two Worlds II text colour and texture),
  - the table of the 9 slots of the 9-slice (material, tile, L/T/R/B),
  - the real game texture with the slot rectangles drawn on top. Click a rectangle to select its slot, drag it to move it, and drag a handle to resize it. Arrow keys move the selected slot by 1 pixel, and Shift+arrows resize it. Ctrl+mouse wheel zooms,
  - an approximate 9-slice preview at any size.
- **Items:** flags (ConstX/Y, StretchX/Y, alignment, ScaleBorder, ScaleBackground, Two Worlds II InterfaceSounds/DissTexture), SizeScale and ShowStartTime, and one-click presets from the developers' spreadsheet (`$Transparent`, `$common`, `$ConstXY`, `$Margins4`, ...).
- **Special state sets (TwoWorlds):** the buttons `$Special_Skill`, `$Special_Inventory` and `$Special_ScrollInventory` in the item panel add the tinted special states of the developers' spreadsheet (0x20-0x30, and 0x40-0x70 for inventory buttons). Each one is a copy of a base state with its own FaceColor, exactly like `Identity` in the definition file (slots, font, records and particle file are copied, the sound is not).
- **Filled-in rectangles:** the compiler computes the rectangles of slots without a material from the NoLeftBorder..NoBackground settings and the margins. These are not stored in the `.dat`, so the editor detects them from the rectangles and shows them in the item panel. Changing them, or moving a slot that has a material, recalculates the empty slots exactly like the compiler.
- **Items and states:** add, duplicate, delete and renumber. When an item is renumbered, the editor offers to update the references to it in DialogTemplates.
- **Materials, sounds, fonts:** add, rename, reorder and delete. Every reference in the slots and states is updated automatically. Elements that are still in use cannot be deleted.
- **Light presets, fonts, CustomData:** named `Light` settings can be picked from a list (and are recognized in existing records). Each game has its own list, because TwoWorlds uses the same names as Earth 2160 with other values (e.g. `$LAnimB`). The face colour presets (`$Disabled`; TwoWorlds: `$DisabledColor`, `$PressedColor`) are one click away. Earth 2160 and TwoWorlds fonts show their spreadsheet descriptions and the `<F#>` text tags they serve (e.g. "Console", "Dialogs <F0> <F2>"), and CustomData entries show what they do.
- **Records:** `Uses` and `Selection` records show the names of the items they refer to.
- **Colours:** colour fields have a swatch (click it to pick a colour), and the `Color` / `Light` record editor previews its colours. Earth 2150 and KnightShift store colours as `0xAABBGGRR` (blue = `0xFFFF0000`), the later games as `0xAARRGGBB`; the editor chooses automatically by the file format (*View → Colour byte order* to override). See `FORMAT.md`.
- **CustomData and DialogTemplates** (Earth 2160 and later): identifiers are shown by name when `resource.h` / `DialogsResource.h` is found next to the `.dat` file, or loaded with *File → Load identifier names*. Double-clicking a control opens its item. In TwoWorlds the developers' notes on dialogs and controls are shown too (e.g. *IDC_COMMAND_WEAPONTYPE_6: Crossbow - unused, can be the bow*).
- **Undo / redo:** Ctrl+Z / Ctrl+Y, up to 100 steps.
- **Saving:** Ctrl+S. Before overwriting a file for the first time, the editor keeps a copy with the `.bak` extension. Every save is checked by reading the result back. A file opened and saved without changes is identical to the original.
- **File → Open .txt (compile):** compiles a definition file and opens the result (the format is proposed from the `// Format:` line of the file).
- **File → Export as .txt project:** writes the definition file, bitmaps and `resource.h`.
- **View → Check file:** missing materials, fonts and sounds, repeated items and a write/read check.

### Names

Item and state names come from the built-in names database: the core control types 0–22 (the same in all games), the names of Earth 2150, Earth 2160 and TwoWorlds (with the item groups of the developers' spreadsheets), and generic state names (0x00 Normal, 0x10 Mouseover, 0x04 Selected, 0x08 Pressed, 0x01 Disabled, 0x20.. Special: ...). Item 50 (fonts of the `<F#>` text tag) shows which tag each state serves.

Different files of the same game may number their items differently, so the matching version is chosen by the item numbers of the opened file: Earth 2160 as released vs. the newer definition file, TwoWorlds PC vs. Xbox 360 (e.g. item 170 *Dialog Commands (bar)* vs. *Dialog XCommands (radial menu)*). Developers' notes (e.g. items possibly used directly by the game code) are shown in the item panel, and the decompiler writes them, and the notes on dialog controls, as `//` comments into the definition file. With *File → Load item/state names from .txt* the names of an original definition file are used instead (remembered for that `.dat` file). *View → Show item and state names* turns them off.

### Textures

To show the real textures, the editor looks for the material files (`.dds`, `.tga`, `.png`, `.bmp`) in:

1. the file chosen for a material with *Materials → Set texture file...* (or a double-click on the material),
2. the folders from *View → Texture folders...* (add the game data folder, the one that contains `Interface`),
3. the folder of the opened `.dat` file (or of the `.txt` file opened with *Open .txt*) and up to three parent folders.

If the textures are not found, the status bar says so and the texture view marks the picture as a colour map.

Material paths such as `Interface\GameInterface\GameInterface_1.dds` are resolved relative to these folders, case-insensitively, also with the `Textures\` prefix that TwoWorlds uses in its archives (`Interface.wd` unpacks the texture as `Textures\Interface\GameInterface\GameInterface_1.dds` and the `.dat` as `Interface\GameInterface.dat`; unpack all archives into one folder). Real textures are searched in all the folders first; only if none is found, the colour-map bitmap `name@.bmp` is used, and if that is missing too, a grey placeholder. *View → Show colour maps (name@.bmp) instead of textures* reverses this order, which is handy because a colour map shows the exact area of every slot.

Supported textures: DDS (every format of the DDS tool: uncompressed, DXT1/3/5, BC4, BC5), 8/24/32-bit BMP, 24/32-bit TGA and PNG. In Earth 2150 and KnightShift the slot rectangles are stored in tile coordinates. The editor converts them to texture pixels and back automatically. The tile size of each material is guessed, and can be changed with *Materials → Tile size...* (it is an editor setting, not stored in the `.dat`).

Editor settings are stored in `.interfacemap_gui.json` in the user's home folder (the same file as the former Python editor used).

## Command line

```
InterfaceTools compile [--ks | --e2160 | --tw | --tw2] dialogs.txt dialogs.dat
InterfaceTools decompile dialogs.dat [output_folder] [--resource resource.h] [--names original.txt | --no-names]
               [--tile name=pixels ...] [--width pixels] [--width name=pixels ...] [--format f]
InterfaceTools dump dialogs.dat
InterfaceTools diff old.dat new.dat [--resource resource.h] [--names original.txt] [--brief]
InterfaceTools dds export|import|create|info|formats ...      (InterfaceTools dds --help)
InterfaceTools addfileinfo <source> <dest> [-text|-binary] [-name N] [-attrib V] [-guid G | -guidCreate | -guidNone]
InterfaceTools addfileinfo -extract <file.int> <output> [-crlf]
InterfaceTools tiles DAT_DIR [TEXTURE_NAME] [--tex FILE.tex] [--csv FILE]  /  tiles DAT_DIR --textures
InterfaceTools export-all ... / import-all ...                 (see above)
InterfaceTools edit [file.dat]                                 (the editor)
```

Paths are relative to the current folder, and paths with spaces must be quoted. Output redirected to a file (`InterfaceTools dump x.dat > report.txt`) goes into the file.

- The compiler looks for the bitmaps and `resource.h` / `DialogsResource.h` **next to the `.txt` file** first, and then in the current folder (the original InterfaceMap only used the current folder, so both ways of calling work).
- `decompile` without an output folder writes into `<name>_src` next to the `.dat` file, and looks for the identifier file next to the `.dat` file and in the current folder.
- `tiles` prints the tile layout of the `.tex` textures defined in the text `.dat` files (the former `CheckNumberOfTiles.py`); `--tex` checks it against the header of a `.tex` file; `--textures` lists the `.tex` files named in the text `.dat` files (the textures that use the Overlap commands).

**Export every `.dat` file into its own folder.** The bitmaps are named after the materials (e.g. `GameInterface_1@.bmp`), not after the `.dat` file, so two files with the same materials (TwoWorlds PC and Xbox, two versions of one file) exported into one folder overwrite each other's bitmaps (and `resource.h`). The decompiler warns when that happens. It also writes `// Bitmap <name> crc32 0x...` comment lines into the definition file: the compiler checks them, warns when a bitmap is not the one written together with the `.txt` (edited or replaced) and looks for the right one in the subfolders and the parent folder. An error such as *Some rects remain undefined* names the missing colour and lists the colours found on every bitmap.

For Earth 2150 and KnightShift, `--tile` and `--width` control the layout of the generated bitmaps: the number of tiles per row is `width / tile size`. This layout is not stored in the `.dat` file (it depends on the real game texture), so by default the width is 256 px. `--width 512` sets the default for all bitmaps, `--width dial64JS=512` sets it for one material only, and both forms can be combined. The generated files always compile back to an identical `.dat`, whatever layout is chosen.

### DDS tool

`dds export` writes a `.png` (the largest mip level) and a `.json` next to every `.dds`; `dds import` rebuilds the `.dds` from them and generates the mip maps (box filter, in linear light for sRGB formats). The `.json` has three sections: `editable` (format, mip maps, ...), `info` (read-only details) and `do_not_edit` (header fields needed to rebuild the file exactly). Supported: the formats of the Reality Pump games — uncompressed formats up to 16 bits per channel, DXT1/DXT3/DXT5, BC4 and BC5. BC6H, BC7 and floating-point formats are reported as not supported. `dds formats` lists them; `dds create image.png -f DXT5 -o out.dds` makes a new file (`--like other.dds` copies the format of another file).

### .int files (AddFileInfo)

An `.int` file is a text file with a small header (name, attribute, GUID). `export-all` writes the content as `.def` and the header as `.addfileinfo`:

```
name: Interface\Dialogs.int
attrib: (none)
guid: 943B5D94-BD56-49E1-A8AF-0A97DEDF0366
```

`import-all` puts them back together. `guid: create` makes a new GUID, `(none)` leaves a section out, and an optional `mode: binary` line stores the content without converting CRLF to LF.

See `FORMAT.md` for the syntax of the definition files and the structure of the `.dat` files.
