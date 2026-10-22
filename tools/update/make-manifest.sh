#!/bin/bash
# Writes latest.json (+ latest.json.sig when SHABPA_SIGN_KEY is set) next to the installer.
#   usage: tools/update/make-manifest.sh <ShabpaSetup.exe> <version> [outdir]
# Optional env: SHABPA_NOTES_FA / SHABPA_NOTES_EN (files with release notes), SHABPA_MIN_APP (default 0.0.0),
#               SHABPA_SIGN_KEY (private key path; without it the manifest is left unsigned and mirrors will reject it),
#               SHABPA_URLS (space separated installer URLs; default: GitHub release, shabpa.ir, dl.shabpa.ir).
set -euo pipefail
installer="$1"; version="$2"; out="${3:-$(dirname "$installer")}"
[ -f "$installer" ] || { echo "missing $installer" >&2; exit 1; }

json_escape() { # stdin -> JSON string body
  python3 -c 'import json,sys; print(json.dumps(sys.stdin.read().strip())[1:-1])' 2>/dev/null \
    || python -c 'import json,sys; print(json.dumps(sys.stdin.read().strip())[1:-1])'
}
note() { if [ -n "${1:-}" ] && [ -f "$1" ]; then json_escape < "$1"; fi; }

sha=$(sha256sum "$installer" | cut -d' ' -f1)
size=$(stat -c %s "$installer")
urls="${SHABPA_URLS:-https://github.com/MrPitPot/shabpa/releases/download/v$version/ShabpaSetup.exe https://shabpa.ir/update/ShabpaSetup.exe https://shabpa-dl.s3.ir-central1.arvanstorage.ir/update/ShabpaSetup.exe https://dl.shabpa.ir/update/ShabpaSetup.exe}"
url_json=""
for u in $urls; do url_json="$url_json${url_json:+, }\"$u\""; done

mkdir -p "$out"
cat > "$out/latest.json" <<JSON
{
  "schema": 1,
  "version": "$version",
  "minAppVersion": "${SHABPA_MIN_APP:-0.0.0}",
  "published": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "sha256": "$sha",
  "size": $size,
  "urls": [$url_json],
  "notes": { "fa": "$(note "${SHABPA_NOTES_FA:-}")", "en": "$(note "${SHABPA_NOTES_EN:-}")" }
}
JSON

if [ -n "${SHABPA_SIGN_KEY:-}" ]; then
  [ -f "$SHABPA_SIGN_KEY" ] || { echo "SHABPA_SIGN_KEY points to a missing file" >&2; exit 1; }
  # Signature = base64 of the DER ECDSA/SHA-256 signature over the exact bytes of latest.json.
  openssl dgst -sha256 -sign "$SHABPA_SIGN_KEY" "$out/latest.json" | base64 -w0 > "$out/latest.json.sig"
  # Self-check with the matching public key before anything is published.
  openssl ec -in "$SHABPA_SIGN_KEY" -pubout -out "$out/.pub.pem" 2>/dev/null
  openssl base64 -d -A -in "$out/latest.json.sig" -out "$out/.sig.bin"
  openssl dgst -sha256 -verify "$out/.pub.pem" -signature "$out/.sig.bin" "$out/latest.json" >/dev/null
  rm -f "$out/.pub.pem" "$out/.sig.bin"
  echo "signed: $out/latest.json.sig"
else
  rm -f "$out/latest.json.sig"
  echo "WARNING: SHABPA_SIGN_KEY not set - latest.json is UNSIGNED (the app will refuse it on mirrors)" >&2
fi
echo "manifest: $out/latest.json (sha256 $sha, $size bytes)"
