#!/bin/bash
# usage: run-bday.sh <lang> <theme>   (test aid: runs the birthday probe on the fake day, offscreen, scratch data)
cd "$(dirname "$0")"
export SHABPA_DATA="$(cygpath -w "$TEMP/bd-data-$1-$2")" SHABPA_INSTANCE=bday-$1-$2 SHABPA_FAKE_TODAY=2026-11-06
mkdir -p design-check/birthday
OUT="$(cygpath -w "$PWD/design-check/birthday")"
timeout 120 src/Shabpa/bin/Debug/net10.0-windows/Shabpa.exe --screenshots "$OUT" --birthday-probe "$OUT" --lang $1 --theme $2
