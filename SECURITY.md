# Security

## Reporting a problem
Please report security issues privately through GitHub: **Security → Report a vulnerability** on this repository.
Do not open a public issue for something that could be abused.

## What Shabpa does and does not do
- Update downloads are only run when they match the SHA-256 that GitHub publishes for the release asset; the
  file is re-checked while locked right before it starts. Installers are not code-signed yet.
- Telegram control goes through the project's bot, @ShabPa_Bot. The app never asks for or holds a bot token; it only
  makes outgoing HTTPS calls to the bot's server, and nothing is sent until you press "Connect to the bot".
- Only Telegram chats that sent the one-time code (single use, 10 minutes) can control the PC. The device secret is
  stored encrypted for your Windows user (DPAPI); the server keeps only its hash.
- The bot can only ask for a fixed list of commands (status, power actions with an on-screen countdown, cancel, free
  RAM, dim, volume, sleep mode); a command older than 3 minutes is dropped and never runs.
- Launchers are only started from the paths found by the launcher search, not from a path read blindly from a file.
- Nothing is injected into games; frame rate comes from Windows' own graphics trace events.
- Optional advanced sensors need the PawnIO driver, which you install yourself; Shabpa does not install drivers.
