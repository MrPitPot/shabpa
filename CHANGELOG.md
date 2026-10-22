# Changelog

All notable changes to Shabpa are listed here, newest first. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [Semantic Versioning](https://semver.org/):
a patch release only fixes bugs, a minor release adds a tested batch of features, a major release is for breaking changes.

## [3.10.0] - 2026-10-22

### Added
- **Weather: several cities** - save up to 8 favourite cities and pick a default that opens at startup. The clicked city shows full details, the others show a compact tile (name, icon, temperature, high/low). Cities load lazily without blocking the window.
- **Crash tracking** - if Shabpa closes unexpectedly, the next start writes a line to the log with the version, the last page and the last-seen time. Unhandled errors are logged with their type and stack.

### Fixed
- **Playtime:** the trash button on a session or game row froze the app and deleted nothing.
- **Telegram:** game-end messages were silently lost when the bot link had a hiccup; they are now queued (max 50, kept across restarts for 12 hours) and retried, and every send, retry and skip is written to the log with its reason. An empty poll answer no longer makes the bot link spin.
- **Theme change:** the glow under the main buttons, tab thumbs and glass cards now follows the theme instead of keeping the old colour.
- **Layouts:** the ping destination box is no longer oversized; the Home buttons have proper spacing; the weather city card is slimmer.
- **Sliders:** pressing anywhere on a slider track (e.g. glass opacity) sets the value and lets you drag, not only the knob.
- **Leaks:** ten pages kept listening to app events after they were unloaded and kept refreshing in the background.
- **Freezes:** nested message pumps are capped, refreshes that arrive while one is running are skipped, the stall stack is read on its own thread, and bot-link failures back off instead of retrying fast.

## [3.9.0] - 2026-10-22

### Added
- **Guided tour** - on first launch a spotlight tour shows each part of the app with short animated hints; click the highlighted spot to continue, skip any time (Esc), and replay it from Settings. People updating from an older version get a small offer instead of a forced tour.
- **Birthday date picker** - an iOS-style wheel picker (day, month, year) in the Shabpa look; the name is used in friendly dashboard texts and there are many more birthday greetings.
- **Smooth scrolling** - mouse-wheel scrolling glides and slows down gradually instead of stopping dead (off when animations are off).
- **Support** - the support server address is built in, so nobody has to type it; the owner card is hidden from everyone else.
- The log records a stall as soon as the window has not answered for 2 seconds, and (at most every 10 minutes) which methods the window thread is stuck in (method names only, local file only).

### Fixed
- Freezes ("not responding"): programs Windows needs to ask (the start-with-Windows task, power plans, wake timers, DNS changes), the sound driver (volume, microphone), the graphics-card library, process names for the per-app network list and the startup-program check no longer run on the window's thread.
- Settings: switching any option no longer re-registers the start-with-Windows task every time.
- A network adapter that Windows cannot read for a moment (error 10043) can no longer stop the sensors or the DNS page.
- A double click on "Apply DNS" or "Add wake" no longer runs the action twice; power plan and volume are restored reliably when the app closes.
- The wheel picker selected the wrong row when a faraway row was clicked; day 29 no longer sticks to 28 after passing a non-leap year.

## [3.8.1] - 2026-10-22

### Fixed
- Playtime: the game list could disappear while a game was running.
- Overlay: dragging no longer jumps back to the old place, and it now snaps flush to the corners and edges.
- The glass darkness slider now changes the window while you drag it.

## [3.8.0] - 2026-10-22

### Added
- An optional "Name and birthday" section in Settings.

### Fixed
- The Playtime page could fail to redraw while a game was running and its list changed.

## [3.7.0] - 2026-10-22

### Added
- **Overlay** - bigger values, a full colour picker for custom colours (no hex codes to type), snap to screen corners and edges, and a monitor refresh-rate item.
- **DNS** - a note for every DNS (what worked for which game), shown beside the ping with a speech-bubble view for long notes.
- **Night light** - ready-made presets (Work, Reading, Movie, Night, Warm, Cool), each editable with a reset to default; the page is renamed "Night light & rest".
- **Playtime** - a live "game running" card with current usage and its effect on the system; the session list fits its content.
- **Health** - an "ignore" button for every warning, and third-party antivirus programs are recognised.
- **Support** - better ticket categories and cards, status filters, close and delete.
- **Scheduled downloads** - an optional end time.
- **Quick actions** - the most-used pages appear automatically (counted on this PC only).
- **Microphone noise filter** - official VB-Cable download links and install steps.
- Cleaner, better-organised game summary message in Telegram.

### Changed
- Cards now have a clean, even border in every theme.
- The glass theme is available on Windows 11 22H2 and newer; it is switched off with a note on Windows 10.
- The CPU temperature in the overlay is labelled "CPU".
- The desktop FPS reading shows the frames Windows really draws.

### Fixed
- Low-contrast text in several themes (green in particular), caused by a colour-mixing overflow.
- DNS table columns were misaligned, and the delete icon broke the table.
- The hourly weather strip did not scroll with the mouse wheel; the search box was too big.
- The overlay could end up partly off-screen after a size change.
- Health no longer reports "restart needed" after the restart has happened.
- Possible UI stall when switching tabs quickly; the tab highlight is re-aligned if it is ever left behind.

## [3.6.0] - 2026-10-17

### Added
- **Playtime page** - records each game session (length, temperatures, background programs) with Today / week / month / all-time totals and per-game detail; an end-of-game notification gives a short summary.
- **Circular theme change** - switching theme from the title-bar button now reveals the new colours as an expanding circle, live, with no restart or confirmation.

### Changed
- Lower idle RAM and CPU use.
- Pages follow a live theme change.

### Fixed
- Overlay ring layouts: values now fit inside the gauges.

## [3.5.1] - 2026-10-17

### Fixed
- Cards could stay slightly smaller or lower after a page was entered repeatedly.
- The tab highlight could stay on the previously selected tab.
- The game play timer restarted when switching windows with Alt+Tab.
- Header spacing in the glass theme.

## [3.5.0] - 2026-10-17

### Added
- **Glass Depth design** for every page, dialog, tray menu, countdown window and the installer; **motion everywhere** (honours the animation switch, "Use less RAM" and reduced motion; nothing animates when idle).

### Changed
- Lighter start-up and memory release; pages are dropped in "Use less RAM" mode.
- Hardened silent installer: no UI, persistent log in %ProgramData%\Shabpa\setup-log.txt, exit codes 10-16, 10-minute watchdog, .NET Framework 4.7.2 target (runs on Windows 10 1809).
- Code signing policy now states honestly that releases are unsigned (SignPath application was not accepted).

### Fixed
- Check marks drawn from the wrong end in right-to-left layouts; mirrored icons in Persian; several layout glitches.

## [3.4.0] - 2026-10-16

### Added
- **Rate Shabpa** - one to five stars in Settings with a playful confirmation; kept on this PC only, with buttons for a real GitHub star and for written feedback.
- **Glass look (Windows 11)** - Mica or Acrylic backdrop; the whole window turns neutral and see-through with no theme colour on it. A slider sets how dark the glass is and applies instantly; Mica and Acrylic switch without restarting.
- **Graphics card option** - draw the window with the processor instead of the GPU (separate from "Use less RAM").
- **Better game detection** - borderless and windowed games are recognised (by their game folder or graphics libraries), and a box lets you list a program Shabpa does not know.
- **Game name and play time** in the side card while a game runs.
- Taught launcher clicks travel with the settings backup.
- Winget manifest and code signing policy (SignPath Foundation application) in the repository.

### Changed
- A little lighter: no background garbage-collection thread and memory is trimmed sooner.

### Fixed
- Saving settings no longer fails when an antivirus or another program holds the file for a moment: it retries in the background. A daily copy of the data file is kept (the last seven) and used if the file cannot be read.
- Searching for launchers no longer freezes the window; page changes in "Use less RAM" mode no longer stutter; freezes are written to the log with the page they happened on.

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
- A little lighter: no background garbage-collection thread and memory is trimmed sooner.

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
