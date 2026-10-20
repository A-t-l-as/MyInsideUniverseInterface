@echo off
rem Builds bin\Release\InterfaceTools.exe with the .NET SDK (https://dotnet.microsoft.com/download).
cd /d "%~dp0"
dotnet build -c Release InterfaceTools.csproj
pause
