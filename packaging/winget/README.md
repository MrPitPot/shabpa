# winget package

`manifests/` holds the three YAML files winget needs for Shabpa (`MrPitPot.Shabpa`), in the folder layout of the
[microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) repository. The version in them always points at an
already **published** GitHub release; the installer's SHA-256 comes from that release's `ShabpaSetup.exe.sha256`.

Check them locally (needs no admin rights):

```
winget validate --manifest packaging\winget\manifests\m\MrPitPot\Shabpa\<version>
```

## First submission (needs your GitHub account, done once by the maintainer)

1. Fork `microsoft/winget-pkgs`.
2. Copy `manifests\m\MrPitPot\Shabpa\<version>` into the same path in the fork and open a pull request.
3. winget's bots validate the package (download, hash, install in a sandbox). When it is merged, `winget install MrPitPot.Shabpa` works.

## Every release after that

Publish the GitHub release first, then either copy the folder to a new version number and change the version, URL, hash and
release date by hand, or let Microsoft's tool do it:

```
wingetcreate update MrPitPot.Shabpa --version <new version> --urls https://github.com/MrPitPot/shabpa/releases/download/v<new version>/ShabpaSetup.exe --submit
```

## Notes

- The installer is a normal setup program, so the manifest uses `InstallerType: exe` with the silent switch `--quiet`
  (the same switch the app's own updater uses). It needs administrator rights and installs for the whole machine.
- Until the setup is code-signed (see `CODE_SIGNING_POLICY.md`), winget will still install it, but Windows SmartScreen
  can warn about the file.
