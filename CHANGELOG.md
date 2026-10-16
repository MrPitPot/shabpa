# Changelog

All notable changes to Shabpa are listed here, newest first. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [Semantic Versioning](https://semver.org/):
a patch release only fixes bugs, a minor release adds a tested batch of features, a major release is for breaking changes.

## [Unreleased]

### Added
- **Rate Shabpa** - one to five stars in Settings with a playful confirmation; kept on this PC only, with buttons for a real GitHub star and for written feedback.
- **Glass look (Windows 11)** - Mica or Acrylic backdrop with translucent panels, chosen in Settings.
- Taught launcher clicks travel with the settings backup.

## [3.3.4] - 2026-10-16

### Fixed
- Teaching or testing Steam and Riot opened the Documents folder instead of the launcher: their paths use forward slashes, which Explorer cannot open. Paths are now written the Windows way.

## [3.3.3] - 2026-10-16

### Fixed
- Overlay editor: every group's readings are listed with tick boxes again, so any of them can be switched on or off without opening anything first.

## [3.3.2] - 2026-10-16

### Fixed
- Overlay editor: a group can always be dragged back into the main frame (it used to stay a separate block lying over it once the main frame had been moved by hand). The main frame now has its own area in the editor.

## [3.3.1] - 2026-10-16

### Fixed
- An overlay block moved away and back no longer lands on top of the main block; blocks now stack and never overlap.

## [3.3.0] - 2026-10-16

### Added
- **RNNoise neural noise suppression** - the live microphone cleaner now uses the RNNoise network (hiss, fans and keyboard clatter); the classic suppressor remains as a fallback.

## [3.2.0] - 2026-10-16

### Added
- **Overlay editor** - one screen to arrange the overlay: drag each group (graphics card, processor, clock...) between six zones or up and down, and click it to choose its readings.
- **Live noise suppression** - cleans the microphone in real time and can send the clean sound to a virtual audio cable for other programs.
- **Manual launchers** - add a launcher's program file yourself when it is not found.
- **Teach by hand** - show a launcher once where to click to start its download; the scheduled download repeats it (Esc stops it).
- **Interface sounds** (experimental) - a soft tick on hover, a click and a notice chime; off in games and in "Use less RAM" mode.

### Changed
- The side menu is grouped by purpose (System & power, Gaming & network, Maintenance, Everyday, App).
- Launcher search also reads the installed-programs list, start-menu shortcuts, running programs and every drive.
- "Use less RAM" now also turns off animations and sounds and hands memory back after each page change.
- Cards light up softly on hover, chosen options pop, and the side menu slides in.
- Launchers are opened with normal rights even when Shabpa runs as administrator.

### Fixed
- A scheduled download no longer clicks while the screen is locked.

## [3.1.0] - 2026-10-16

### Added
- **Tasks & focus** - to-do list with deadlines, reminders and categories; focus and break timer; weekly report.
- **Files & backup** - disk space analysis, duplicate finder, clean-up of temporary files and caches, scheduled folder backup with verification and a restore that never overwrites.
- **Health & security** - disk, update and service checks, recent event-log errors in plain language, Windows security settings, startup-program watch, and a report with personal details removed.
- **Program and driver updates** - outdated programs through winget, graphics driver versions with the maker's official page, driver updates found by Windows Update.
- **Profiles & automation** - one-click profiles (study, work, rest, night), triggers by time or game start/end, run history, undo, import and export.
- **Microphone** - level and mute, noise test with advice, noise suppressors found on the PC, programs that used the microphone.
- **Game mode** - best power plan, RAM clean-up and quiet notices while a game runs; everything is restored afterwards.
- **Settings backup** - save and restore settings, profiles and tasks (never keys or the bot link).
- Two themes: **Forest green** and **Neon green**.

### Changed
- **Telegram** - linking is now a one-time code sent to @ShabPa_Bot; personal bot tokens are no longer accepted. The bot can start a profile or a focus session and report how many tasks are left.
- **Memory** - about 135 MB drops to 25-35 MB while the window is hidden; a "Use less RAM" option draws the window in software.
- **Weather** - redrawn animated sky: layered clouds, sun rays, moon, stars, rain splashes, snow, fog and lightning.
- Shabpa reopens in front after an update; notices appear inside the window; the window position is remembered; `Ctrl+1` to `Ctrl+9` switch sections.
- The privacy note in Settings now lists exactly what uses the internet.

### Fixed
- Progress bars follow the selected theme.

## [3.0.1] - 2026-10-15

### Fixed
- Screen dimming no longer flashes back to full brightness every second.

## [3.0.0] - 2026-10-15

### Added
- Motion design: a distinct entrance for every tab, animated selection controls and an Animations switch.

## [2.9.0] - 2026-10-15

### Added
- Support page: tickets through a pre-filled GitHub issue or the project server, replies, and an owner view.

## [2.8.0] - 2026-10-15

### Added
- Each overlay group (GPU, CPU, RAM, network, clock) can be placed on its own.

## [2.7.0] - 2026-10-15

### Added
- Dim screen, optional break reminders, sleep mode, waking the PC for scheduled downloads, per-game overlay profiles and game history.

## [2.6.0] - 2026-10-15

### Added
- Custom hotkey combinations, eight new themes, and administrator hints where a reading needs them.

### Changed
- A calmer green theme with a soft halo; overlay items stay listed until unticked; friendlier greetings.

### Fixed
- Frame rate falls back to the busiest program when the foreground window is not the game.

## [2.5.1] - 2026-10-15

### Fixed
- Colour emoji rendering and weather animation timing.

## [2.5.0] - 2026-10-15

### Added
- Weather with an animated sky, greetings and a tabular DNS list.

## [2.4.0] - 2026-10-15

### Added
- DNS tab and download resume for Steam, Epic and Riot launchers.

## [2.3.1] - 2026-10-15

### Security
- Mandatory update checksum, locked installer file, verified launcher paths and pairing lockout.

## [2.3.0] - 2026-10-15

### Added
- Fully customisable launcher download plans: weekly repeat, thresholds, open-only and restart.

## [2.2.1] - 2026-10-15

### Added
- Motherboard readings in the overlay.

## [2.2.0] - 2026-10-14

### Added
- Scheduled launcher downloads, optional advanced sensors and a more accurate frame rate.

## [2.1.2] - 2026-10-14

### Changed
- Short English overlay captions; unreliable temperature readings removed or marked unsupported.

## [2.1.1] - 2026-10-14

### Added
- Overlay with every reading and live status cards on the home page.

## [2.1.0] - 2026-10-14

### Added
- Gaming overlay, game reports and a monthly data allowance.

## [2.0.2] - 2026-10-14

### Fixed
- Confirmations before every action, no repeated shutdown, and missed commands are dropped instead of run late.

## [2.0.1] - 2026-10-14

### Fixed
- Small stability fixes.

## [2.0.0] - 2026-10-14

### Added
- First public release: scheduled and conditional power commands, wake timers, power use, per-app speed limits, RAM clean-up, Telegram control and in-app updates.
