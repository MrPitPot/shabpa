# Changelog

## How versions are numbered

`MAJOR.MINOR.PATCH`, applied strictly from 3.0.1 on:

- **PATCH** (3.0.1 → 3.0.2): fixes only, no new features.
- **MINOR** (3.0 → 3.1): a planned batch of new features, released only after it has been tested for a while.
  Work in progress collects under "Unreleased" below; one minor release, not one per feature.
- **MAJOR** (3 → 4): only for a real break (settings or data that older versions cannot read, Windows support
  dropped, and the like). Not expected any time soon.
- Test builds can be shared as GitHub pre-releases (`3.1.0-beta.1`); the in-app updater only offers full releases.

The 2.x and 3.0 line moved quickly while the app was taking shape; from here on releases are fewer and larger.

## Unreleased (planned 3.1.0)

- Telegram control goes through the project's bot, @ShabPa_Bot: press "Connect to the bot", send the one-time
  code to the bot, done. Personal bot tokens are no longer accepted.
- Much less RAM: while the window is hidden next to the clock Shabpa gives its idle memory back to Windows
  (about 135 MB → 25–35 MB in Task Manager), pages are only built when first opened, and a hidden window is no
  longer redrawn every second.
- New setting "Use less RAM": draws the window without the graphics card (about 120 → 75 MB while it is open,
  a little more CPU).
- Weather: a new animated sky. Soft layered clouds with depth, a sun with a glow and slowly turning rays, a
  shaded moon, twinkling stars and shooting stars, rain in two layers with splashes, snow with depth, drifting fog
  and real lightning bolts; rounded corners and a soft shade behind the text so it stays readable.
- After an update Shabpa opens again by itself, comes to the front and says which version is installed.
- Notices show inside the window while it is open (the tray balloon is only used when it is hidden).
- The window opens where you left it, at the same size (or maximised).
- Keyboard: Ctrl+1 … Ctrl+9 open the sections in order, Ctrl+, opens the settings.

- **Tasks & focus** (new): a to-do list with deadlines, reminders, categories and today / upcoming / done views;
  a focus and break timer; a weekly report that also summarises games, internet and power. Stored only on this PC.

- **Files & backup** (new): see where disk space went, find duplicate files, and back folders up on a schedule
  with a preview, verification and a restore that never overwrites. Deleting only ever sends files to the Recycle Bin
  after a preview and your confirmation.

- **Health & security** (new): disk space, pending restarts, Windows Update age, key services and recent event-log
  errors in plain language; a few Windows security settings (firewall, Defender, UAC, SmartScreen, Remote Desktop,
  Secure Boot); a watch for new or changed startup programs with a one-click turn-off for your own entries; what
  access each Shabpa feature needs; and a report with personal details removed that you review before sharing.
  It reports status only: it is not an antivirus and never claims to have fixed anything.

- **Profiles & automation** (new): one click runs several existing actions (screen dimming, volume, focus timer,
  break reminders, RAM, a message, sleep mode or a delayed power action) with an undo for the last run; triggers run a
  profile at a time of day, when a game starts or ends, or when Shabpa starts; run history; export and import of
  profiles. A profile that can switch the PC off is always started by hand and confirmed, and an imported file can only
  contain this fixed list of actions.

- **Settings backup** (new): save your settings (and, if you choose, profiles and tasks) to a file and restore them
  after a reinstall or on another PC. The file never contains the bot link, device secret or support key.
- Recent notices are kept (in memory only) in Settings; the bot can start a profile, start or stop a focus session and
  tell you how many tasks you have today (titles only if you switch that on). Profiles that can switch the PC off are
  never started remotely.
- The privacy line in Settings now says exactly what uses the internet.

- Safety review of the new features: guard tests keep them free of network code, make every delete go through a
  confirmation and the Recycle Bin (Windows now asks if a file is too big for the bin), keep notices and task
  reminders away from the bot, and refuse odd backup destinations and service names.

- Two new themes: **Forest green** (calm, mossy) and **Neon green** (black with a vivid glow).

- **Clean-up** (new, in Files & backup): temporary files, Windows Update downloads, shader caches, browser caches
  (cache only), thumbnails and crash dumps, shown with sizes first. Nothing outside those folders is touched.
- **Game mode** (new, in Comfort): when a game starts it switches to the best power plan, frees RAM and keeps Shabpa's
  notices quiet; when the game ends everything goes back (also after a restart in the middle).
- **Microphone** (new): level and mute, a noise test with plain advice, noise suppressors found on this PC (AMD,
  NVIDIA), and which programs used the microphone.

## 3.0.1

- Screen dimming no longer flashes back to full brightness once a second.

## 3.0.0 and earlier

See the [GitHub releases](https://github.com/MrPitPot/shabpa/releases).
