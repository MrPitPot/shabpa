# Updates that survive a cut-off internet

Shabpa checks an ordered list of **sources**. Each one serves a tiny signed manifest (`latest.json` + `latest.json.sig`)
and the installer. The first source that answers wins (6 s timeout each); the one that worked last time is tried first.

| # | Source | Manifest | Notes |
|---|--------|----------|-------|
| 1 | GitHub release | `https://github.com/MrPitPot/shabpa/releases/latest/download/latest.json` | attach `latest.json` and `latest.json.sig` to every release |
| 2 | shabpa.ir | `https://shabpa.ir/update/latest.json` | static files in the site repo (`public/update/`) |
| 3 | dl.shabpa.ir | `https://dl.shabpa.ir/update/latest.json` | domestic host (placeholder until provisioned) |
| 4 | Extra mirror | whatever the person types in Updates, Update settings | HTTPS only |
| 5 | GitHub API | `api.github.com/.../releases/latest` | legacy, unsigned but GitHub-only and checksum-pinned; keeps old releases working |

## Trust model

* Mirrors are **untrusted**. A manifest counts only if its bytes carry a valid ECDSA P-256 / SHA-256 signature from the release key
  whose public half is compiled into the app (`UpdateKeys.PublicKeyBase64`, `src/Shabpa/Core/UpdateManifest.cs`).
* The manifest holds the installer SHA-256 and size; the downloaded file must match both, whichever mirror delivered it.
  A mismatch deletes the download and the next address is tried.
* Refused: bad/missing signature, wrong key, older version than installed (downgrade), `minAppVersion` above the installed version,
  non-HTTPS addresses (also HTTPS to HTTP redirects), oversize manifest (128 KB) or installer (200 MB or more than the signed size).
* Until `PublicKeyBase64` is filled in, every signed source is rejected (fails closed) and only the legacy GitHub path works.
* Authenticode / installer behaviour is unchanged: the installer still runs with `--quiet --update --launch`.

## One-time setup (owner)

1. `tools/update/keygen.sh /safe/place/shabpa-update-key.pem` (needs `openssl`). Keep the file out of the repo, on encrypted
   storage, with an offline backup. Losing it means shipping a new app build with a new public key.
2. Paste the printed public key into `UpdateKeys.PublicKeyBase64` and ship that build (it must reach people **before** the first
   mirror-only release; people on older builds keep using GitHub).
3. `export SHABPA_SIGN_KEY=/safe/place/shabpa-update-key.pem` in the shell you release from.
4. DNS for `dl.shabpa.ir`: an A/AAAA record to a domestic (Iranian) host with a valid HTTPS certificate, serving the folder
   `/update/` as static files. The domain must resolve from inside Iran; `.ir` names do.
   `shabpa.ir` already works through Cloudflare, but may be unreachable if international routes are cut: that is why #3 exists.

## Each release

```bash
export SHABPA_SIGN_KEY=/safe/place/shabpa-update-key.pem
export SHABPA_NOTES_FA=notes.fa.txt SHABPA_NOTES_EN=notes.en.txt   # optional
./build-installer.sh          # also writes installer/latest.json and installer/latest.json.sig
export SHABPA_SITE_DIR="D:/Projects/Shutdown Scheduler/Shabpa-site"
export SHABPA_DL_TARGET=user@dl-host:/var/www/dl/update/            # once the host exists
export SHABPA_GH_TAG=v3.10.1                                         # after the GitHub release exists
tools/update/publish-mirrors.sh          # dry run: prints what it would do
tools/update/publish-mirrors.sh --go     # copies; then run `npx wrangler deploy` in the site repo
tools/update/make-bundle.sh              # installer/ShabpaUpdate.zip for USB or Telegram
```

* Without `SHABPA_SIGN_KEY` the manifest is written unsigned with a warning, and the app will reject it on mirrors.
* The installer is always uploaded **before** the manifest. `latest.json` lists the installer addresses (GitHub, shabpa.ir, dl.shabpa.ir);
  relative addresses are resolved against the manifest's own URL.
* The Cloudflare site serves files up to 25 MB; the installer is about 2 MB. Give `/update/*` `Cache-Control: no-cache` in the site's `_headers`.
* Manifest format: `{"schema":1,"version","minAppVersion","published","sha256","size","urls":[...],"notes":{"fa","en"}}`.
  The signature is base64 of the DER ECDSA signature over the exact bytes of `latest.json`
  (`openssl dgst -sha256 -sign key.pem latest.json | base64 -w0`).

## When everything is blocked

The Updates page shows the failure and three buttons: **Open download page** (shabpa.ir), **Install from file...** and
**Import update bundle...**.

* A bundle is a `.zip` with `ShabpaSetup.exe`, `latest.json` and `latest.json.sig` (made by `make-bundle.sh`). Signature,
  version, size and checksum are verified before anything runs.
* Install from file: if `latest.json` + `.sig` sit in the same folder they are verified as above; otherwise copy the published
  SHA-256 to the clipboard and confirm; the file must match it, be newer than the installed version and be a Shabpa installer.
  The person's file is never deleted on a mismatch.

## Code map

`Core/UpdateManifest.cs` (parse, signature, key) | `Core/UpdateSources.cs` (source order, timeouts, selection)
| `Core/UpdateDownload.cs` (resumable download, per-address fallback) | `Core/UpdateBundle.cs` (offline import)
| `Core/Updater.cs` (state and installer launch) | tests: `UpdateMirrorTests.cs` (fake HTTP handler).
