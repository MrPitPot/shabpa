#!/usr/bin/env bash
# Publishes a Shabpa release on Codeberg (the main home of the code) through its API: creates the release for the tag and
# attaches the installer, its checksum, latest.json and latest.json.sig.
#
#   tools/update/publish-codeberg.sh <version> [--go]
#
# Without --go nothing is sent: it only prints what it would do. Run it after build-installer.sh and make-manifest.sh.
# The access token (Codeberg > Settings > Applications, scope: write:repository) is read from the CODEBERG_TOKEN environment
# variable or from the file named by CODEBERG_TOKEN_FILE. It is never printed and never written anywhere.
set -euo pipefail

version="${1:?usage: publish-codeberg.sh <version> [--go]}"
go="${2:-}"
repo="${SHABPA_CODEBERG_REPO:-MrPitPot/Shabpa_app}"
api="https://codeberg.org/api/v1/repos/$repo"
here="$(cd "$(dirname "$0")/../.." && pwd)"
out="${SHABPA_OUT:-$here/installer}"
tag="v$version"
notes="$here/patch-note-$version.fa.md"
notes_en="$here/patch-note-$version.en.md"

files=("$out/ShabpaSetup.exe" "$out/ShabpaSetup.exe.sha256" "$out/latest.json" "$out/latest.json.sig")
for f in "${files[@]}"; do [ -f "$f" ] || { echo "missing: $f (run build-installer.sh and make-manifest.sh first)"; exit 1; }; done
[ -f "$notes" ] || { echo "missing: $notes"; exit 1; }

body="$(cat "$notes")"
[ -f "$notes_en" ] && body="$body"$'\n\n---\n\n'"$(cat "$notes_en")"

echo "release $tag on codeberg.org/$repo"
for f in "${files[@]}"; do echo "  asset: $(basename "$f") ($(wc -c <"$f") bytes)"; done
if [ "$go" != "--go" ]; then echo "(dry run: add --go to publish)"; exit 0; fi

token="${CODEBERG_TOKEN:-}"
if [ -z "$token" ] && [ -n "${CODEBERG_TOKEN_FILE:-}" ]; then token="$(tr -d '\r\n' <"$CODEBERG_TOKEN_FILE")"; fi
[ -n "$token" ] || { echo "no token: set CODEBERG_TOKEN or CODEBERG_TOKEN_FILE"; exit 1; }
auth=(-H "Authorization: token $token")

# the release (a second run for the same tag reuses it)
id="$(curl -fsS "${auth[@]}" "$api/releases/tags/$tag" 2>/dev/null | python -c 'import sys,json; print(json.load(sys.stdin).get("id",""))' 2>/dev/null || true)"
if [ -z "$id" ]; then
  payload="$(TAG="$tag" BODY="$body" python -c 'import os,json; print(json.dumps({"tag_name":os.environ["TAG"],"name":os.environ["TAG"],"body":os.environ["BODY"],"draft":False,"prerelease":False}))')"
  id="$(curl -fsS "${auth[@]}" -H "Content-Type: application/json" -d "$payload" "$api/releases" | python -c 'import sys,json; print(json.load(sys.stdin)["id"])')"
  echo "created release $id"
else
  echo "release $id already exists: attaching missing files"
fi

for f in "${files[@]}"; do
  name="$(basename "$f")"
  curl -fsS "${auth[@]}" -F "attachment=@$f" "$api/releases/$id/assets?name=$name" >/dev/null
  echo "uploaded $name"
done
echo "done: https://codeberg.org/$repo/releases/tag/$tag"
