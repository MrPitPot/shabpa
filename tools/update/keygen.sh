#!/bin/bash
# One-time: creates the release-signing key (ECDSA P-256) and prints the PUBLIC key to paste into
# src/Shabpa/Core/UpdateManifest.cs (UpdateKeys.PublicKeyBase64).
# The private key file never goes into the repository: keep it on an encrypted drive plus an offline backup.
#   usage: tools/update/keygen.sh /safe/place/shabpa-update-key.pem
set -euo pipefail
key="${1:-${SHABPA_SIGN_KEY:-}}"
[ -n "$key" ] || { echo "usage: $0 <path-for-private-key.pem>" >&2; exit 2; }
[ ! -e "$key" ] || { echo "refusing to overwrite existing $key" >&2; exit 1; }
umask 077
openssl ecparam -name prime256v1 -genkey -noout -out "$key"
echo "Private key written to: $key  (back it up offline; losing it means shipping a new app build with a new public key)"
echo
echo "Public key (paste as UpdateKeys.PublicKeyBase64):"
openssl ec -in "$key" -pubout -outform DER 2>/dev/null | base64 -w0
echo
