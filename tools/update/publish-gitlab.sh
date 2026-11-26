#!/usr/bin/env bash
# Publishes a Shabpa release on GitLab: uploads the installer, its checksum, latest.json and latest.json.sig to the project's package
# registry (generic packages) and creates the release for the tag with those files as its downloads.
#
#   tools/update/publish-gitlab.sh <version> [--go]
#
# Without --go nothing is sent: it only prints what it would do. The tag vX.Y.Z must already be pushed to GitLab.
# The access token (GitLab > Preferences > Access tokens, scope: api) is read from the GITLAB_TOKEN environment variable or from
# the file named by GITLAB_TOKEN_FILE. It is never printed and never written anywhere.
set -euo pipefail

version="${1:?usage: publish-gitlab.sh <version> [--go]}"
go="${2:-}"
project="${SHABPA_GITLAB_PROJECT:-MrPitPot/shabpa_app}"
enc="${project//\//%2F}"
api="https://gitlab.com/api/v4/projects/$enc"
here="$(cd "$(dirname "$0")/../.." && pwd)"
out="${SHABPA_OUT:-$here/installer}"
tag="v$version"
notes="$here/patch-note-$version.fa.md"
notes_en="$here/patch-note-$version.en.md"

files=("$out/ShabpaSetup.exe" "$out/ShabpaSetup.exe.sha256" "$out/latest.json" "$out/latest.json.sig")
for f in "${files[@]}"; do [ -f "$f" ] || { echo "missing: $f (run build-installer.sh first)"; exit 1; }; done
[ -f "$notes" ] || { echo "missing: $notes"; exit 1; }

body="$(cat "$notes")"
[ -f "$notes_en" ] && body="$body"$'\n\n---\n\n'"$(cat "$notes_en")"

echo "release $tag on gitlab.com/$project"
for f in "${files[@]}"; do echo "  file: $(basename "$f") ($(wc -c <"$f") bytes)"; done
if [ "$go" != "--go" ]; then echo "(dry run: add --go to publish)"; exit 0; fi

token="${GITLAB_TOKEN:-}"
if [ -z "$token" ] && [ -n "${GITLAB_TOKEN_FILE:-}" ]; then token="$(tr -d '\r\n' <"$GITLAB_TOKEN_FILE")"; fi
[ -n "$token" ] || { echo "no token: set GITLAB_TOKEN or GITLAB_TOKEN_FILE"; exit 1; }

pkg="$api/packages/generic/shabpa/$version"
for f in "${files[@]}"; do
  name="$(basename "$f")"
  curl -fsS -H "PRIVATE-TOKEN: $token" --upload-file "$f" "$pkg/$name" >/dev/null
  echo "uploaded $name"
done

links="$(PKG="https://gitlab.com/api/v4/projects/$enc/packages/generic/shabpa/$version" python -c '
import os, json
pkg = os.environ["PKG"]
names = ["ShabpaSetup.exe", "ShabpaSetup.exe.sha256", "latest.json", "latest.json.sig"]
print(json.dumps([{"name": n, "url": pkg + "/" + n, "direct_asset_path": "/" + n, "link_type": "package"} for n in names]))
')"
payload="$(TAG="$tag" BODY="$body" LINKS="$links" python -c '
import os, json
print(json.dumps({"name": os.environ["TAG"], "tag_name": os.environ["TAG"], "description": os.environ["BODY"], "assets": {"links": json.loads(os.environ["LINKS"])}}))
')"
if curl -fsS -H "PRIVATE-TOKEN: $token" "$api/releases/$tag" >/dev/null 2>&1; then
  echo "release $tag already exists: its files were replaced in the package registry"
else
  curl -fsS -H "PRIVATE-TOKEN: $token" -H "Content-Type: application/json" -d "$payload" "$api/releases" >/dev/null
  echo "created release $tag"
fi
echo "done: https://gitlab.com/$project/-/releases/$tag"
