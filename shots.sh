#!/bin/bash
# Builds the app and captures screens to PNG (demo data, nothing is executed or saved).
# usage: shots.sh <out-folder> [extra app args, e.g. --lang en --theme pink --only home,ping]
cd "$(dirname "$0")"
OUT="${1:-$LOCALAPPDATA/Temp/shabpa-shots}"; shift
dotnet build src/Shabpa/Shabpa.csproj -c Debug -nologo -v q 2>&1 | grep -E " error |Build succeeded" | sort -u | head -20
rm -rf "$OUT"
./src/Shabpa/bin/Debug/net10.0-windows/Shabpa.exe --screenshots "$(cygpath -w "$OUT")" "$@"
ls "$OUT" | tr '\n' ' '
