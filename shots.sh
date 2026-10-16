#!/bin/bash
# Captures the app's screens to PNG with demo data (nothing is executed, saved or read from this PC's files).
#   ./shots.sh <out-folder> [extra app args, e.g. --lang en --theme white --only home,tasks:todo]
cd "$(dirname "$0")"
OUT="${1:-$LOCALAPPDATA/Temp/shabpa-shots}"
shift
dotnet build src/Shabpa/Shabpa.csproj -c Release -nologo -v q 2>&1 | grep -E " error |Build succeeded" | sort -u | head -20
rm -rf "$OUT"
./src/Shabpa/bin/Release/net10.0-windows/Shabpa.exe --screenshots "$(cygpath -w "$OUT")" "$@"
ls "$OUT" | tr '\n' ' '
