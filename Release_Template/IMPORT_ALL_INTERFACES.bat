@echo off
rem Packs everything in the Work folder back (.txt, .png, .def, .json)
"%~dp0Bin\InterfaceTools.exe" import-all "%~dp0Work" --no-pause
pause
