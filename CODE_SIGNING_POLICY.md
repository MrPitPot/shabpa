# Code signing policy

Shabpa's release installer and application are meant to be digitally signed so that Windows can show who published them.

> Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org).

The project has applied to the SignPath Foundation for this. Until the application is accepted, releases are unsigned and
Windows SmartScreen may show a warning: *More info → Run anyway*. The SHA-256 of every installer is published next to it.

## What is signed

- `ShabpaSetup.exe` (the installer) and `Shabpa.exe` (the application) of every official release.
- Only files built from this repository's public source by the project's own build. Nothing from third parties is signed
  with the project's identity; third-party components (WinDivert, RNNoise, LibreHardwareMonitorLib) are left as their
  authors released them.

## Roles

| Role | Who |
|---|---|
| Author / committer | [MrPitPot](https://github.com/MrPitPot) |
| Reviewer of changes | [MrPitPot](https://github.com/MrPitPot) (pull requests from others are reviewed before merging) |
| Approver of a signing request | [MrPitPot](https://github.com/MrPitPot) |

Everyone with a role uses two-factor authentication on GitHub and SignPath.

## Privacy

Shabpa works on your PC and sends nothing anywhere on its own. Only features you switch on use the internet: the update
check (GitHub), the weather page (Open-Meteo), the Telegram link and support tickets. Microphone sound, taught clicks,
tasks and settings stay on the PC. The full statement is in the app under *Settings* and in [SECURITY.md](SECURITY.md).

## Reporting a problem

If you think a signed file is not what it should be, open an issue at https://github.com/MrPitPot/shabpa/issues or follow
[SECURITY.md](SECURITY.md).
