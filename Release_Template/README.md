# User Manual:

Extract the interface files of a given `Reality Pump` game from the `.wd` file and place them into the `Work` directory. Interface `.wd` files in `Reality Pump` games are often named `Interface.wd` and `InterfaceEx.wd`.

Supported games: Earth 2150, KnightShift, Earth 2160, TwoWorlds, Two Worlds II, and - with the Earth 2150 format - Heli Heroes, World War III: Black Gold and World War II: Panzer Claws (Frontline Attack: War over Europe).

Everything is done by one program, `Bin\InterfaceTools.exe` (no Python needed; it uses the .NET Framework 4.8 that comes with Windows 10 and 11). `Bin\TexEnv.exe` handles the `.tex` textures.

* `EXPORT_ALL_INTERFACES.bat` -- Exports all interfaces placed in the `Work` directory: `.dat`, `.int`, `.dds` and `.tex` files, enabling their subsequent editing.
* `IMPORT_ALL_INTERFACES.bat` -- Imports all interfaces placed in the `Work` directory. It packs the files previously unpacked by `EXPORT_ALL_INTERFACES.bat` back into their original format.
* `RUN_INTERFACE_MAP_GUI.bat` -- The editor for interface layout files (`dialogsINGAME.dat`, `GameInterface.dat`, ...) and their `.txt` definition files. Its *Tools* menu also runs the export and import of the `Work` directory, with a log window.
* `RUN_TEX_EDIT.bat` -- A program used to edit `.tex` texture files.
* `RUN_TEX_EDIT_SHELL.bat` -- A shell environment supporting `.tex` texture processing.
* `RUN_TEX_VIEWER.bat` -- A viewer for `.tex` texture files.

**Important note:** After exporting `.tex` textures via `EXPORT_ALL_INTERFACES.bat`, you must keep the `.tex` files, as `IMPORT_ALL_INTERFACES.bat` will require them.

**Export each `.dat` file into its own folder** inside `Work` (e.g. `Work\GameInterface` and `Work\GameInterfaceX`): the bitmaps are named after the textures, so two files that use the same textures would overwrite each other's bitmaps in one folder.

More: `Bin\README.md` (the program, the editor and all commands) and `Bin\FORMAT.md` (the file formats). The C# sources are in `Source` (`build.cmd` builds the program with the .NET SDK).
