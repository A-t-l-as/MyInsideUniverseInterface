@echo off
rem Exports everything in the Work folder (.dat, .tex, .int, .dds)
"%~dp0Bin\InterfaceTools.exe" export-all "%~dp0Work" --no-pause
pause
