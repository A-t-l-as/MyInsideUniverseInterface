# InterfaceMap: file formats

The program supports five versions. The Earth 2150 format is used by other Reality Pump games too: **Heli Heroes**, **World War III: Black Gold** and **World War II: Panzer Claws** (*Frontline Attack: War over Europe*); their files are handled as Earth 2150 (no switch).

| Format | `InterfaceTools compile` switch | Detection by the decompiler |
|---|---|---|
| Earth 2150 (also Heli Heroes, World War III: Black Gold, World War II: Panzer Claws / Frontline Attack: War over Europe) | (default) | header `FF A1 D0 10`, version 1 |
| KnightShift | `--ks` | header `FF A1 D0 10`, version 2 |
| Earth 2160 | `--e2160` | no stream header |
| TwoWorlds | `--tw` | no stream header and no resolution |
| Two Worlds II | `--tw2` | like TwoWorlds, but without a sound list and with extended states |

The behaviour of Earth 2150, Earth 2160 and TwoWorlds is reconstructed from the code of the original programs and verified byte for byte against the original files. We do not have the KnightShift compiler: its `.dat` format is known from the game loader, and the text syntax is assumed to be the same as in Earth 2160. No InterfaceMap was ever made for Two Worlds II: its format was reconstructed from the game loader (see below).

## Definition file: common part

Every line is normalized first. Runs of whitespace become a single space, and spaces after `,` and `:` are removed, as are spaces before them. The exception are the bodies of `CustomData` and `DialogTemplates` blocks (Earth 2160 and later), where lines are only trimmed of whitespace at both ends. Empty lines and lines starting with `//` are skipped. Keywords are case-insensitive.

```
Fonts
{
nr: name, size, weight, italic, colour, shadowX, shadowY, shadowColour   // everything from "size" on is optional
}

Materials
{
bitmapName:tileSize              // Earth 2150 / KS: file "bitmapName@.bmp"; default tile 64
}

Item:N description
{
<item flags>
<item properties>               // inherited by all states
State:N description
	{
	1..9: colour                 // colour number from the bitmap palette for a 9-slice slot
	<state properties>
	Identity N                   // copies colours and properties from state N (the sound is not copied)
	}
}
```

Item flags:

| Keyword | Bit | Notes |
|---|---|---|
| `ConstX 0/1`, `ConstY`, `StretchX`, `StretchY` | 0x01, 0x02, 0x04, 0x08 | |
| `LeftX` / `CenterX` / `RandomX` / `RightX` | bits 4–5 = 0..3 | |
| `LeftY` / `CenterY` / `RandomY` / `RightY` | bits 6–7 = 0..3 | |
| `NoBorderX` / `NoBorderY` | 0x300 / 0xC00 | |
| `NoRightBorder`, `NoTopBorder`, `NoBottomBorder` | 0x200, 0x400, 0x800 | |
| `NoBackground` | 0x1000 | |
| `NoLeftBorder` | **0x1000** | bug in the original, see below |
| `ScaleBorder 0/1` | 0x100 in the file | KS, Earth 2160, TwoWorlds |
| `ScaleBackground 0/1` | 0x200 in the file | KS, Earth 2160, TwoWorlds |
| `SizeScale 0.625` | float stored with the item | KS, Earth 2160 (not TwoWorlds) |

**Bug in the original.** The original computes the flag bit as `1 << (index-1)` and additionally subtracts 4 from indices greater than 13. Because of that `NoLeftBorder` (index 13) sets the background bit 0x1000, i.e. it works like `NoBackground`. The "no left border" bit alone (0x100) can only be set with `NoBorderX 1` followed by `NoRightBorder 0`. The program reproduces this bug faithfully, because otherwise the output would differ from the original.

Bits 0x100–0x1000 from the table above (except `ScaleBorder` and `ScaleBackground`) are only used to fill in missing rectangles and are not written to the file.

Properties: `Font nr`, `Sound name`, `Color x`, `Selection n`, `Uses a,b,...`, `Moving a,b,...`, `Light a,b,...`. Values beyond the 64th are silently dropped, as in the original. `Effect:KEY [v1,v2,...]` creates a record with an arbitrary key number.

The font colour ends up either in the font or in the state (as a Color record):
- no shadow, or the shadow has the text colour: in the state, and the shadow is then cleared (-1),
- black shadow `0xFF000000` and opaque text: in the state, and the shadow stays in the font,
- in all other cases the colour stays in the font and the state gets no colour.

Font numbers form an array from 0 to the highest defined number. `Font N` with an undefined number within that range gives font 0 with colour 0.

Slots: 1 background, 2 top, 3 bottom, 4 left, 5 right, 6 top-left, 7 top-right, 8 bottom-left, 9 bottom-right.

Top-level directives (KS, Earth 2160):
- `Scale 640, 480, 1.0, 1.0`: base resolution and aspect ratio X / Y (the names come from the developers' spreadsheet, where the four values are called "Scale X / Y" and "Aspect ratio X / Y").
- `DefaultScaleBorder 1` and `DefaultScaleBackground 1`: global flags 0x100 and 0x200. They are written to the header and into the flags of every item.

## Earth 2160: additions

```
Materials
{
	Interface\InterfaceMainMenu\Interface_E2160_1.dds        // bitmap: Interface_E2160_1@.bmp
	Interface\Other.dds:2                                    // ":2" = base of the colour numbering
}

ParticlesRoot Games\Earth2160\           // only used to check whether the .prt files exist
ParticlesPrefixPath Particles\Interface\ // prepended to the particle file name

CustomData name value                    // single-line form: only the first word of the value
CustomData name
{
	any lines                            // stored in lower case; a line ending with "\"
	...                                  // is joined with the next one using a space
}

Item:N
{
LeftMargin 4  TopMargin 4  RightMargin 4  BottomMargin 4   // for particle states
ShowStartTime 1.0                                           // float, default -1
State:0x00
	{
	AnimatedButton.prt                   // every line containing ".prt" = particle file
	FaceColor 0xFFB0B0B0
	1: 11                                // colour = base*256 + palette index
	}
}

DialogTemplates IDD_NAME                 // identifiers from resource.h
{
	IDD_NAME = 62                        // item number of the dialog itself
	IDC_BUTTON = 64 [x [y]]              // control: item number and its X / Y offset
	IDC_LIST_[0-13] = 368                // range: IDC_LIST_0 .. IDC_LIST_13
	ForceNoFullScreenTransparentBackground   // also Force(No)MessageBoxBackground,
}                                            // ForceFullScreenTransparentBackground
```

How the individual elements work:
- **Bitmaps:** both sides must be a power of two from 32 to 512 px. Rectangles are stored in pixels of the whole texture; there are no tiles any more.
- **Colours:** counted separately for each bitmap. In the text a slot colour is `base*256 + index`, where the base is given in the Materials section. This allows more than 255 colours in total.
- **State with a particle file:** has zero rectangles, and the item margins are written into the right and bottom coordinates of the edges and corners. Another bug in the original: the bottom edge gets `BottomMargin` only when `RightMargin` is non-zero.
- **`Identity`:** additionally copies the particle file and `FaceColor` (if the state has none of its own).
- **`resource.h`:** read from the working directory (or next to the `.txt` file). Lines of the form `#define NAME number` count. IDOK=1 … IDHELP=9 are built in.
- **Unused font:** in Earth 2160 this is only a warning, not an error.

## TwoWorlds

The text file syntax is the same as in Earth 2160; this is confirmed by the parser code and the string table of the original program. Differences:
- **Identifier file:** it is called `DialogsResource.h` instead of `resource.h`.
- **No `Scale` and no `SizeScale`:** both keywords were removed. The `.dat` header only contains the global flags (`DefaultScaleBorder`, `DefaultScaleBackground`), and an item has a single float field, `ShowStartTime` (default -1).

Everything else is identical to Earth 2160: flags, margins (including the `BottomMargin` bug), states, `FaceColor`, particles, `CustomData` and `DialogTemplates`.

## Two Worlds II

No separate InterfaceMap was ever made for Two Worlds II. The `.dat` format was reconstructed from the game loader (`FUN_0097e750`, `FUN_009793d0`, `FUN_0096c7a0`, `FUN_009699b0`). The text syntax is the TwoWorlds syntax plus a few **invented** keywords for the new fields. Differences in the `.dat` file:

```
u32 globalFlags
materials (u8 len + name ...), u8 0
                                        NO sound list
fonts: as before + u8 extra, ..., u8 0xFF
items: u16 number, u32 flags, f32 ShowStartTime
  states: as in TwoWorlds, but after the 9 slots:
    u32 FaceColor
    u32 TextColor                       NEW: text colour
    u8 len, particleFile
    u8 len, TextTexture                 NEW: texture filling the glyphs (Interface\FontBkg.dds)
    u32 TextTextureParam                NEW (20 with the texture, otherwise 0)
CustomData, DialogTemplates: as in TwoWorlds
```

New keywords in the text file (names invented, but based on the game code). The old names (`FaceColor2`, `Texture`/`TextBackground`, `TextureMargin`/`TextBackgroundMargin`, `SecondTexture`, `ExtFlags 0x800`/`0x1000`) are still accepted:

| Keyword | Where | Field |
|---|---|---|
| 9th value of a font definition | Fonts | "lazy": 1 = font created only on first use (`book`/`letter`/`diary`), 0 = created immediately on load |
| `TextColor 0x...` | state | text colour (default -1). In Two Worlds II fonts have no colour and there are no `Color` records, so this is the only source of the text colour. The game reads it with the getters `FUN_009724f0`/`FUN_00983910` |
| `TextTexture path` | state | texture filling the glyphs. The only one used is `Interface\FontBkg.dds`: a golden, horizontally tileable 256×32 px gradient (a shimmering text effect). The game passes it together with the text colour to the glyph drawing function |
| `TextTextureParam n` | state | number stored after the texture (default 0, always 20 with the texture; meaning unknown, e.g. scale or scroll speed of the texture) |
| `InterfaceSounds 1` | item | bit 0x800: the control plays the standard interface sounds (`CustomData interfacewave…`) on focus, click, slider change, list selection and toggle |
| `DissTexture 1` | item | bit 0x1000: the item's slots also use the `_DISS` variant of the material texture. For every material the loader loads a pair: the normal texture and the file with the `_DISS` suffix before the extension (`GameInterface_1.dds` → `GameInterface_1_DISS.dds`, `FUN_009683d0`), but only when the game's global option `DAT_00d6c068` is enabled. Only slots of items with this bit get the `_DISS` variant (`FUN_009836f0`). Old name: `SecondTexture` |

**Fonts.** The font number in the `.dat` file corresponds to a row of `Interface\FontsConfig.txt` (columns: Nr, name, size, weight, italic). If the row exists, the game overrides the name, size, weight and italic from the `.dat` with it (e.g. `main` 24 → `main_R` 26). Language versions differ only in this file (e.g. the Japanese one uses `main_J` everywhere), so the `.dat` is shared by all languages. These four font values in the `.dat` are therefore only defaults. Colours, shadow and the "lazy" flag always come from the `.dat`.

There is no `Sound` keyword in Two Worlds II, because the file contains no sound list.

## Bitmap

The bitmap must be an uncompressed 8-bit palettized BMP. For every palette index the program computes the bounding rectangle of its pixels, with R and B exclusive. In Earth 2150 and KS the dimensions must be a multiple of the tile size, and the rectangle is stored in tile coordinates. The tile number is `(y/tile)*tilesPerRow + x/tile`.

## The .dat file (little-endian)

```
[Earth 2150, KS]     u32 0x10D0A1FF, u32 0x01004449 (2150) / 0x02004449 (KS)
[not TW, TW2]        i16 width, i16 height, f32 aspectRatioX, f32 aspectRatioY
[KS, 2160, TW, TW2]  u32 globalFlags
{u8 len, char[len]}...  u8 0x00          used materials (in order of first use)
[not TW2] {u8 len, char[len]}...  u8 0x00          sounds
{u8 len, char[len], u16 size, u16 weight, u8 italic, u8 shadowX, u8 shadowY,
 u32 colour, u32 shadowColour, [TW2] u8 lazy}...  u8 0xFF     unique fonts
for every item (ascending):
  [2150]     u8 number,  u32 flags & 0xFF
  [KS]       u16 number, u32 flags, f32 SizeScale
  [2160]     u16 number, u32 flags, f32 SizeScale, f32 ShowStartTime
  [TW, TW2]  u16 number, u32 flags, f32 ShowStartTime
             (flags = low byte | ScaleBorder/ScaleBackground (+ TW2 bits) | globalFlags)
  for every state (ascending):
    u16 stateNumber
    u8 n, u32[n]  property block:
        [font, sound] + records (key<<16 | length, values...) sorted by key
        keys: 0 Color (also from Font), 1 Selection, 2 Uses, 3 Moving, 4 Light, others: Effect:N
        n = 0 when the state has no properties at all
    9 × { u16 material, u16 L, T, R, B }
        [2150, KS]      material = (tile<<8 | index) or 0xFFFF
        [2160, TW, TW2] material = index or 0xFFFF; rectangle in texture pixels
    [2160, TW]  u32 FaceColor, u8 len, char[len] particleFile
    [TW2]       u32 FaceColor, u32 TextColor, u8 len, char[len] particleFile,
                u8 len, char[len] TextTexture, u32 TextTextureParam
  u16 0xFFFF
[2150] u8 0xFF (+ a second 0xFF appended by the original)   [KS, 2160, TW, TW2] u16 0xFFFF
[2160, TW, TW2] u32 count, { u32 len, name, u32 n, { u32 len, text } × n }        CustomData
[2160, TW, TW2] u32 count, { u32 id, u32 item, u32 force[4], u32 n,
                          { u32 id, u32 item, u32 xOffset, u32 yOffset } × n (ascending by id) }   DialogTemplates
```

## Colour byte order

All colours (font and shadow colours, `Color`, `FaceColor`, the colours of `Light`) are 32-bit values written as `0xAA......`, but the byte order of the three colour bytes depends on the engine:

- **Earth 2150 and KnightShift: `0xAABBGGRR`.** Blue is `0xFFFF0000`, red is `0xFF0000FF` (InsideEarth wiki: "Earth codes are more like BGR than RGB", e.g. the highlighting colours in `Light` of *Item:20 Animated button* and the EarthNet chat colour codes). The same in KnightShift (confirmed).
- **Earth 2160, TwoWorlds, Two Worlds II: `0xAARRGGBB`** (the usual Direct3D order; confirmed in all three games). The TwoWorlds developers' spreadsheet agrees: its hidden sheet converts the font colours to cell colours as ARGB (e.g. font 0 `0xFFD7D7C6` = cell colour 13031383 = R 0xD7, G 0xD7, B 0xC6).

The files store the numbers unchanged; only the preview in the editor depends on this (*View → Colour byte order*, automatic by default).

## Item and state names

The `.dat` files store only numbers. The names used by the decompiler and the editor come from the names database built into `InterfaceTools.exe` (`interfacemap_names.json` in the sources), built from the developers' spreadsheets `InterfaceEarth2160.xls` and `GameInterface.xls` (TwoWorlds) and the original definition files of Earth 2150, Earth 2160 and TwoWorlds. The spreadsheets were the developers' source: a macro generated the definition `.txt` from them, which InterfaceMap then compiled.

- **Core item types:** items 0–22 have the same numbers in all games of this engine. They are control types defined in the game code: 0 Transparent dialog (Earth 2150: Dialog window), 2 Static, 3 Button, 4 Check box, 5 Radio button, 6 Edit box, 7 Group box, 8 Tab edges, 9 Tab, 10/11 vertical scrollbar bar/button, 12 scrollbar corner, 13/14 horizontal scrollbar bar/button, 15/16 slider bar/button, 17/18 progress bar, 19 Tooltip, 20 Animated button, 21 Tab dialog, 22 List selection. Higher numbers depend on the game and file.
- **Game-specific names:** Earth 2150 (72 items), Earth 2160 (303 items, with the groups of the spreadsheet) and TwoWorlds (218 items PC / 212 Xbox 360, with the groups of the spreadsheet). For KnightShift and Two Worlds II only the core names are used: only 102 of the 290 Two Worlds II item numbers exist in TwoWorlds, and many of them have other states.
- **Generic state names** (from the spreadsheet):

| State | Name |
|---|---|
| 0x00 | Normal |
| 0x10 | Mouseover |
| 0x04 | Selected |
| 0x08 | Pressed |
| 0x0C | Selected Not Pressed |
| 0x01 | Disabled |
| 0x09 | Disabled Checked |
| 0x21 | Disabled special |
| 0x02 | Scrollbar Pressed |

  TwoWorlds adds special variants: 0x20 + state = "Special", 0x40 + state = "Special2", 0x60 + state = "Special12". They are made with the spreadsheet macros `$Special_Skill`, `$Special_Inventory` and `$Special_ScrollInventory`: each special state is `Identity <base state>` plus its own `FaceColor` (e.g. 0x20 = copy of 0x00 tinted `0xFFFF8080`, 0x21 = copy of 0x01 tinted `0xFFCF0000`; inventory buttons add 0x40–0x50 and 0x60–0x70 tinted green `0xFF00FF00`). In item 50 (*Fonts for the `<F#>` text tag*) the state number is the tag number: state N gives the font of `<FN>` in game texts. Earth 2150 names states per item (e.g. the menu item 1: 0x30 "Single standard", 0x10 "Upper standard").
- **File versions:** different files of the same game may number their items differently. TwoWorlds PC and Xbox 360 differ in items 170–175, 178 and 189 (the Xbox command bar is a radial menu) and in the F1–F5 dialog backgrounds (PC 210–223, Xbox 230–243). The Earth 2160 `InterfaceMainMenu.dat` shipped with the game (`Interface.wd`, 297 items) was built from an older definition file than the developers' spreadsheet and `.txt` (303 items): the statistics buttons are items 190–199 there, while the newer file moved them to 393–402 and uses 190–194 for the formation buttons. The tools choose the version whose item numbers match the opened file best; the names of the release version were derived by matching identical particle files between the two versions.
- **Names from the original definition file:** `InterfaceTools decompile --names original.txt` and the editor (*File → Load item/state names from .txt*) can use the `Item:N name` and `State:0xNN name` lines of any original definition file instead.

The spreadsheet also provides presets (flag sets such as `$Transparent` = NoBorderX + NoBorderY + NoBackground, `$common` = ConstX + ConstY + NoBorderX + NoBorderY + ScaleBorder, `$Margins4`, `$Size` = SizeScale 0.625 for 640×480 → 1024×768), 21 named `Light` settings (e.g. `$LAnimB` "Anim Buttons"), the grey `$Disabled` face colour `0xFFB0B0B0`, descriptions of the Earth 2160 fonts (e.g. 6 "Console", 9 "Subtitles") and of the CustomData entries (e.g. `ResLineColor`: colour of the lines connecting research buttons).

The TwoWorlds spreadsheet uses the same flag presets, but its `Light` presets have the same names with other values (e.g. `$LAnimB` = `0, 0xFFFFFFFF, 0, 0, 0xFFFFFFFF, 39, 52`; new: `$LAnimBdisabled`, `$LightBlack`), so the presets are kept per game; all 3 `Light` values of the TwoWorlds file match its own list and none the Earth 2160 one. It also gives the face colours `$DisabledColor` `0xFFB0B0B0` and `$PressedColor` `0xFFE0E0E0`, the fonts with their `<F#>` tags (e.g. font 9 "Dialogs" = `<F0>` and `<F2>`), the meaning of the CustomData entries `MapMaskCoordinates` (width, height, midX, midY, rangeMask, rangeMapSign) and `CustomWave*` (interface sounds: gamepad selection, list scrolling, combo box, slider, hero type selection, quests), notes on dialog controls (e.g. `IDC_COMMAND_WEAPONTYPE_0` "Knife - unused, can be the sword") and a list of items possibly used directly by the game code or unused (51, 52, 64, 120–140, 186, 314, 315, 350–353). Not used by the tools: the `BkgLinks` sheet (background files of the main menu screens), an alternative message box layout for a 388×512 texture, and the skill icon coordinates of the particle textures.

## Extension: repeated items

Some files, e.g. `DialogsINGAME2/4` from KnightShift Gold Edition, contain the same item written twice in a row with identical content. The original compiler does not do that, so it is most likely a trace of another tool. The game reads items one by one, so the second copy changes nothing.

To make such a file reproducible byte for byte, the decompiler writes this line after the item definition:

```
RepeatItem 530        // item 530 will be written to the .dat once more, right after itself
```

If a repeated item has different content, or the items are not in ascending order, the decompiler reports a warning, because such a file cannot be reproduced exactly.

## Reverse operation (InterfaceTools decompile)

```
InterfaceTools decompile file.dat [directory] [--tile name=size ...] [--width pixels] [--width name=pixels ...]
                         [--resource resource.h] [--format f]
```

The program rebuilds the definition file and the bitmaps (plus `resource.h` / `DialogsResource.h` for Earth 2160, TwoWorlds and Two Worlds II), and finally checks by itself that recompiling gives an identical `.dat`.

What cannot be recovered and how it is rebuilt:
- **Comments, descriptions, original colour and font numbers, the look of the bitmaps:** this information is not in the `.dat`. The program rebuilds an equivalent version.
- **Tile size and tiles per row (Earth 2150, KnightShift):** the `.dat` stores only the tile number and the rectangle inside the tile. The tile size is guessed (it must be at least the largest rectangle), and the number of tiles per row is `width / tile size` with a default width of 256 px. Both can be set per material with `--tile name=size` and `--width name=pixels`; a plain `--width pixels` sets the default width.
- **Identifier names (Earth 2160, TwoWorlds, Two Worlds II):** taken from the given `resource.h` / `DialogsResource.h`. Missing ones are added as `_ID_<number>`.
- **`ParticlesRoot` (Earth 2160 and later):** it is not stored in the file, so it is not rebuilt.
