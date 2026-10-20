#!/bin/sh
# Builds InterfaceTools.exe (.NET Framework 4.8, a single file) with the Roslyn compiler of the
# .NET SDK and the .NET Framework reference assemblies of Mono. On Windows the same sources can
# be built with build.cmd (csc.exe of the .NET Framework or of Visual Studio).
set -e
cd "$(dirname "$0")"
CSC=$(ls /usr/lib/dotnet/sdk/*/Roslyn/bincore/csc.dll | head -1)
R=/usr/lib/mono/4.8-api
OUT=${OUT:-bin/InterfaceTools.exe}
TARGET=${TARGET:-winexe}
WIN32ICON=${WIN32ICON-res/InterfaceTools.ico}
WIN32MANIFEST=${WIN32MANIFEST-res/app.manifest}
mkdir -p "$(dirname "$OUT")"
SRC=$(find src -name '*.cs' | sort)
dotnet "$CSC" -nologo -langversion:latest -optimize+ -target:$TARGET -platform:anycpu -out:"$OUT" \
  -nostdlib -noconfig -warn:4 -nowarn:1591,0649,0169,8019 \
  -r:$R/mscorlib.dll -r:$R/System.dll -r:$R/System.Core.dll -r:$R/System.Drawing.dll \
  -r:$R/System.Windows.Forms.dll -r:$R/System.Numerics.dll -r:$R/Facades/System.ValueTuple.dll \
  -r:$R/Facades/System.Runtime.dll \
  -resource:res/interfacemap_names.json,InterfaceTools.interfacemap_names.json \
  ${WIN32ICON:+-win32icon:$WIN32ICON} ${WIN32MANIFEST:+-win32manifest:$WIN32MANIFEST} \
  $SRC
ls -la "$OUT"
