# Security

## Reporting a problem
Please report security issues privately through GitHub: **Security → Report a vulnerability** on this repository.
Do not open a public issue for something that could be abused.

## What Shabpa does and does not do
- Update downloads are only run when they match the SHA-256 that GitHub publishes for the release asset; the
  file is re-checked while locked right before it starts. Installers are not code-signed yet.
- The Telegram token is stored encrypted for your Windows user (DPAPI) and is never written to the log.
- Only the Telegram chat that entered the 6-digit pairing code can control the PC; wrong guesses lock pairing for 10 minutes.
- Launchers are only started from the paths found by the launcher search, not from a path read blindly from a file.
- Nothing is injected into games; frame rate comes from Windows' own graphics trace events.
- Optional advanced sensors need the PawnIO driver, which you install yourself; Shabpa does not install drivers.
