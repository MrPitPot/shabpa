# SignPath Foundation application: what to fill in

Apply at <https://signpath.org/apply> with the project owner's own account (only the owner can do this). Suggested answers:

| Field | Answer |
|---|---|
| Project name | Shabpa |
| Repository | https://github.com/MrPitPot/shabpa |
| Homepage | https://github.com/MrPitPot/shabpa |
| License | MIT (OSI approved) |
| Short description | Free, open-source, local-first Windows assistant: shutdown scheduling, gaming overlay, scheduled launcher downloads, tasks and focus, backups, system health. No telemetry, no account. |
| Download page | https://github.com/MrPitPot/shabpa/releases |
| Code signing policy | https://github.com/MrPitPot/shabpa/blob/main/CODE_SIGNING_POLICY.md |
| Privacy statement | in the policy above and in `SECURITY.md` |
| Team | one maintainer (MrPitPot) with two-factor authentication |

## What they will also ask for (and the current state)

- **Public source, OSI license:** yes (MIT).
- **Actively maintained, documented:** yes (README in English and Persian, changelog, CI).
- **Built by a public CI:** the workflow `.github/workflows/release-build.yml` builds `ShabpaSetup.exe` on a GitHub-hosted
  runner straight from this repository (`build-installer.sh`) and uploads it as an artifact. The submit-to-SignPath step is
  added after approval, because it needs the organisation id and a secret token that SignPath issues.
- **Two signing steps:** the installer carries the application inside it, so the application is signed first, then packed,
  then the installer is signed.
- **No malware or potentially unwanted behaviour:** the app has no ads, no telemetry and no hidden network use; the
  WinDivert driver (per-app speed limits) is the official signed release and is only used when the person turns the feature on.
