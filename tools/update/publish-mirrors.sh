#!/bin/bash
# Copies ShabpaSetup.exe + latest.json + latest.json.sig to the mirrors. Nothing is pushed or deployed by this script
# unless you pass --go; without it, it only prints what it would do.
#   usage: tools/update/publish-mirrors.sh [--go] [installer-dir]    (default installer-dir: ./installer)
# Targets (all optional, from the environment):
#   SHABPA_SITE_DIR   checkout of the website repo; files go to $SHABPA_SITE_DIR/public/update/  (then run `npx wrangler deploy` there)
#   SHABPA_DL_TARGET  scp/rsync destination of the domestic host, e.g. user@dl.example.ir:/var/www/dl/update/
#   SHABPA_GH_TAG     GitHub release tag (e.g. v3.10.1): uploads latest.json and latest.json.sig with `gh release upload --clobber`
# Always upload the installer BEFORE the manifest, so no mirror ever announces a file it does not have yet.
set -euo pipefail
go=0; [ "${1:-}" = "--go" ] && { go=1; shift; }
dir="${1:-installer}"
for f in ShabpaSetup.exe latest.json latest.json.sig; do [ -f "$dir/$f" ] || { echo "missing $dir/$f (run build-installer.sh with SHABPA_SIGN_KEY set)" >&2; exit 1; }; done
run() { echo "+ $*"; if [ "$go" = 1 ]; then "$@"; fi; }

if [ -n "${SHABPA_SITE_DIR:-}" ]; then
  dest="$SHABPA_SITE_DIR/public/update"
  run mkdir -p "$dest"
  run cp "$dir/ShabpaSetup.exe" "$dest/ShabpaSetup.exe"
  run cp "$dir/latest.json" "$dest/latest.json"
  run cp "$dir/latest.json.sig" "$dest/latest.json.sig"
  echo "site: commit and run 'npx wrangler deploy' in $SHABPA_SITE_DIR to publish https://shabpa.ir/update/"
fi
if [ -n "${SHABPA_DL_TARGET:-}" ]; then
  run scp "$dir/ShabpaSetup.exe" "$SHABPA_DL_TARGET"
  run scp "$dir/latest.json" "$dir/latest.json.sig" "$SHABPA_DL_TARGET"
fi
if [ -n "${SHABPA_GH_TAG:-}" ]; then
  run gh release upload "$SHABPA_GH_TAG" "$dir/latest.json" "$dir/latest.json.sig" --clobber
fi
[ "$go" = 1 ] || echo "(dry run: pass --go to execute)"
