# InterfaceTools - sources

C# (.NET Framework 4.8, Windows Forms), one executable. Games: Earth 2150, KnightShift, Earth 2160, TwoWorlds, Two Worlds II, and with the Earth 2150 format Heli Heroes, World War III: Black Gold and World War II: Panzer Claws / Frontline Attack: War over Europe.

| Folder / file | Contents |
|---|---|
| `src/Program.cs` | entry point: the editor without arguments, otherwise a console command |
| `src/Core/` | compiler (`Compiler.cs`), `.dat` reader/writer (`DatFile.cs`), decompiler, dump/diff, names database (`NameDb.cs`), JSON, PNG, DDS tool, texture loading |
| `src/Tools/` | AddFileInfo, tiles per row, the Work folder pipeline (`Pipeline.cs`: export-all / import-all, TexEnv) |
| `src/Gui/` | the editor |
| `res/` | the names database `interfacemap_names.json` (embedded into the exe), icon, manifest |
| `tools/build_names_tw.py` | development only: adds the TwoWorlds spreadsheet `GameInterface.xls` to `res/interfacemap_names.json` (Python + `xlrd`) |
| `tools/make_icon.py` | development only: generates `res/InterfaceTools.ico` |

Building:

- Windows: install the .NET SDK and run `build.cmd` (`dotnet build -c Release InterfaceTools.csproj`); the result is `bin\Release\InterfaceTools.exe`.
- Linux (how the shipped exe was built): `./build.sh` - the Roslyn compiler of the .NET SDK with the reference assemblies of Mono; `TARGET=exe ./build.sh` builds a console variant for testing with `mono`.

The output of the compiler, decompiler, dump, diff and DDS tool is identical to the former Python tools (checked byte for byte on files of all five games).
