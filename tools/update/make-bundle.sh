#!/bin/bash
# Builds the offline update bundle (ShabpaSetup.exe + latest.json + latest.json.sig in one .zip) for USB sticks or Telegram.
#   usage: tools/update/make-bundle.sh [installer-dir] [out.zip]
set -euo pipefail
dir="${1:-installer}"
for f in ShabpaSetup.exe latest.json latest.json.sig; do [ -f "$dir/$f" ] || { echo "missing $dir/$f" >&2; exit 1; }; done
out="$(cd "$(dirname "${2:-$dir/ShabpaUpdate.zip}")" && pwd)/$(basename "${2:-$dir/ShabpaUpdate.zip}")"
rm -f "$out"
(cd "$dir" && if command -v zip >/dev/null; then zip -q "$out" ShabpaSetup.exe latest.json latest.json.sig; else
  powershell -NoProfile -Command "Compress-Archive -Path ShabpaSetup.exe,latest.json,latest.json.sig -DestinationPath '$(cygpath -w "$out")'"; fi)
echo "bundle: $out"
