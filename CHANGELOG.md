# Changelog

All notable changes to Shabpa are listed here, newest first. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [Semantic Versioning](https://semver.org/):
a patch release only fixes bugs, a minor release adds a tested batch of features, a major release is for breaking changes.

## [3.15.1] - 2026-11-06

### Added
- A one-time gift code typed to Boofi pays 5,000 feathers. The code is used up on Shabpa's server, so it works once in the whole world; with no answer from the server nothing is paid. Only a salted hash of the code is in the program and only its SHA-256 is sent.

### Changed
- Premium themes the person owns are listed under their own heading in Settings, apart from the free themes.

### Fixed
- Speed limits: when another program (a proxy or VPN client) already holds the WinDivert driver, the message names that program, and Shabpa tries again every half minute until it lets go.
- The overlay jumped to another monitor when Shabpa's own window was clicked on a second screen; it now stays on the monitor of the game.
- Animated backdrops of premium themes were washed out by the window's veil in the real app; they are drawn above it now.

## [3.15.0] - 2026-11-03

### Added
- Reminders are an alarm: when a task reminder is due, a window opens above everything and an alarm sound repeats until it is closed («in ten minutes» snoozes it). It works with the main window closed to the tray. A switch on the Tasks page turns it off; during a game in quiet mode the reminder waits until the game is over.
- Every optional tab can be switched off in Settings («Parts of the app»): it leaves the list and what it did in the background stops (lighting, overlay, speed limits, night light and breaks, task reminders, microphone, scheduled backups, triggers, download plans). Home, Settings and Updates always stay.
- «Report for fixing a problem» in Settings: one click saves a zip with Shabpa's log, the lighting server's log and a few lines about Windows on the desktop, with the Windows user name taken out of the paths. Nothing is sent by Shabpa.
- Every feather payment (quests, medals, bonuses, the hour with Shabpa) is 30 percent more.
- An owner's key for unlimited feathers, typed to Boofi; only a salted hash of the passphrase is in the program.

### Changed
- Updates are no longer forced: a newer version is only suggested (a notice), and the person installs it from the Updates page when they want. The forced-update window and its setting are gone.

### Fixed
- Lighting that was left on did not start after a restart or a boot until it was switched off and on by hand: nothing started the lighting loop when Shabpa started. It starts with Shabpa now.
- Lighting: after switching the lights off and on while the server was not reachable, every frame stayed black; the sleep and shutdown black-out could leave memory and the board lit; devices the person left out were switched dark; a server that finished finding hardware after Shabpa connected never showed its devices; own-device music and temperature effects did not react; a lighting server that kept ending right away was started again every few seconds (it now pauses for five minutes); the program file of the lighting server is checked before every start; refused connections left sockets behind; Persian digits in the header size box are accepted; the devices window texts are readable.
- Speed limits: the limiter stopped and started again at every Alt+Tab (the game still running now counts), packets of an app could be lost while its limit was being changed, and a failed apply could freeze all later changes.
- The window waited for the disk in some saves (the settings saver held a lock the UI needed; the once-a-minute usage save ran on the window's thread). Both are in the background now.
- The seven-night gift was paid twice, and switching the interface language in the middle of a week paid the weekly quests again.
- Reminders: the alarm sound was cut by any other sound, a reminder due during a game in quiet mode was lost, two reminders at once opened two windows, and «remind me» with reminders switched off said it was set but never fired.
- Updates: a mirror that is behind no longer hides a newer version, the unsigned GitHub answer can no longer start a forced update, the installer is downloaded into a fresh random folder, and the installer stops only its own driver (matching the folder exactly) and waits until it has really stopped.
- Settings export no longer carries the proxy address, the support contact, program paths or the weather city, and an imported file cannot change the support server.
- The build's folder path is no longer written into the program files.
## [3.14.3] - 2026-11-03

### Fixed
- Lighting could not be enabled on networks that block codeberg.org (the download of the lighting server failed every time): when the official address cannot be reached, the same file is fetched from the project's GitHub mirror. It is still accepted only with the fixed SHA-256, and the log now says which address failed and why.
- A lighting server that was not running caused a failed connection every few seconds, each leaving an "unobserved task exception" (hundreds a day, filling the log): the failed attempt is now cleaned up, and repeats of the same unobserved exception are counted instead of written each time.

## [3.14.2] - 2026-11-02

### Fixed
- Lighting did nothing on some PCs (mostly a clean Windows 10) and only said "The effects are not reaching the lights": the lighting server needs the Microsoft Visual C++ runtime and Windows 10 1809 or newer. Shabpa now checks for them before starting the server, says which one is missing, offers the official Microsoft download for the runtime, and shows the server's exit code when it closes by itself.

## [3.14.1] - 2026-11-02

### Fixed
- Speed limits did not apply after an update («WinDivert driver failed to open», code 0x8032000A): a driver left loaded by the earlier run is now stopped and opened again by itself, and if that is not possible the message says to restart Windows once.
- The window froze for seconds on a slow disk or while an antivirus scanned: settings are now written to disk in the background.
- After installing, the window could stay behind other windows with only the taskbar button flashing; it now always comes to the front when it opens.

## [3.14.0] - 2026-11-02

### Added
- Thirty special themes you buy with feathers (new «Themes» tab in the shop): lantern festival, rainy window, desert night, snowfall, embers, tilework, ink wash, prism, misty morning, meteor shower, Persepolis, Cyrus the Great, Christmas, Warzone, Frontline, Radiant, Blocky world, Arcade, Synthwave, cherry blossom, fireflies, the deep, storm, galaxy, space station, teahouse, Yalda night, koi pond, code rain and Bauhaus. Each has its own palette and a slow animated backdrop; bought ones also appear in Settings.
- The fur backdrop no longer shows behind the little Boofi.
- **The feather store and «بوفی من»** (medals page, two new tabs). 90 parts for Boofi in six slots (hat, glasses, neck, watch, lantern, fur colour) and five rarities, bought with feathers (a common part costs a few days of ordinary use, a legendary one a few months; the default night fur is free). Laid out like the design: a top bar with your balance and what you earned today, a filter pop-up (type, rarity, status, sort), tiles grouped by rarity with price range and waiting time, a try-on bar and three saved looks («ست ۱ تا ۳»). Part names and notes follow the app language. Each picture is drawn once and cached, so scrolling stays smooth, and Boofi is never mirrored in Persian. «بوفی من» shows Boofi dressed in what you wear and lets you put on or take off any owned part; everything is saved with your progress (and in the optional cloud save). Spending feathers never lowers your level. Two more one-time quests (change the look three times, buy a first part) make 24 in all (227 feathers).
- **Optional cloud save** («ذخیره‌ی ابری»). A person can log in with Telegram (no email, no password) so feathers, medals and counters survive a Windows reinstall or a new PC. It is never required and never forced: after some real use the app suggests it at most three times, two weeks apart, and «no thanks» stops it for good. Only the progress file is sent (no name, no paths, no usage details); two PCs are merged instead of overwriting each other, and a person can log out or erase everything from the server. The feature stays hidden until a server address is set (`AccountService.DefaultServer`); the server is `server/account-worker` (a Cloudflare Worker, free plan).
- A card on the medals page shows the cloud save state: log in, sync now, log out, delete the account.

- **The little Boofi (dock, assistant) and the big one in «بوفی من» wear what you wear** on top of the same drawn Boofi as before (wings up, heart eyes, flights all stay); the fur colour repaints him with the same gradients and patterns as in the shop (lava cracks, galaxy, gold scales…), not a flat colour. Everything he wears has its own physics: hats lag and flop (a wizard hat more than a cap, a crown is heavy), scarves and capes sway, pendants swing short and quick, the lantern is a pendulum that swings from the hand, the watch rides the wing, and his wings and head follow through when he hops. The loop runs only while something is moving. He breathes through the day, glances around now and then and does one of ten acts (look around, two hops, ruffle, stretch, tip the hat, swing the lantern, check the watch, push up the glasses, a dance, a sleepy yawn). In «بوفی من», putting on or trying on a part plays the act that shows it off, and a click makes him react.

- **Boofi can do more.** New things he works out from what you type, in Persian or English: a strong random password (never sent through the Telegram bot), unit conversion (length, weight, volume, data size, speed, temperature: «۵ کیلومتر چند مایله؟», "100 c to f"), the time in another city, days until Nowruz / Yalda / Christmas / Halloween / the new year / Friday, when to go to bed for a given wake-up time (90-minute sleep cycles), a random number between two numbers, a choice between options («بین چای یا قهوه انتخاب کن») and the battery level. Each is recognised only by its own clear words, so power and time commands read exactly as before.
- **More to say.** Boofi now has 61 jokes and 57 facts in each language (about the owl, computers and their history, sleep and the body, space and nature, Iran). Ask in Persian and the answer is Persian; ask in English and it is English. None repeats until the whole set has been told.

- **A better lighting page.** The picture is now the PC and the desk: the case with its cooler, memory, graphics card, three fans, strip and board accents, and beside it a mouse pad with a keyboard and a mouse, each drawn in the colour its effect gives it right now. Click any part (or its chip in «connected parts») to set it on its own; **keyboard and mouse are separate parts now.** A «Devices» button opens a window with everything that was found: name, kind, number of LEDs, where it is plugged in, a switch to leave a device alone, the part of the PC it is driven as (changeable), a **«Test» that blinks it white**, a button to give it a **look of its own** (the whole effect page then edits just that device), and **a size for every addressable header** (a header left at 0 LEDs lights nothing, the usual reason a fan stays dark). The window also says plainly why a part may be missing (memory and motherboard need the PawnIO driver and administrator rights; USB fans and coolers need their own program closed). Effects can run **in the other direction** and be shown as **stars** instead of the smooth flow, and there are **20 more flowing looks** (mint breeze, arctic glow, rose gold, ocean waves, emerald forest, fireflies, autumn leaves, supernova, Saturn's rings, starfield, vaporwave, synthwave, fire and ice, volt, razor, Kashan tile, pomegranate, Christmas, Valentine). You can tell Boofi about one part: «keyboard red», «mouse rainbow», «نور فن‌ها سبز», «نور کیبورد خاموش».
- **Lighting: the whole case moves as one.** With «same look for every light» on, a rainbow (or any flowing effect) is now ONE flow laid along all the lights of the PC (fans, memory, cooler, strips, board, graphics card, keyboard, mouse), each device taking its own stretch, instead of every device showing its own copy. **Addressable headers are set to their maximum automatically** (for example 200 and 240 LEDs on a motherboard), so a whole fan chain or strip lights; a size typed in the devices window is remembered and wins. A **gear button** on the page opens the options: pause the effects while a game runs, lights off while Windows sleeps (and back on waking), an effect when Shabpa starts and when Windows shuts down, keep other lighting programs closed, smoother movement, and the **fade in / fade out effect** (Blaze, Sunrise, From the centre, Dark wave, Soft, None), which also plays whenever the lights are switched on or off.
- **The rainbow flows instead of cycling.** With the lights synchronised, each device takes a stretch of the flow about as long as a fan (a 400-LED board no longer swallows the whole rainbow) and that stretch is a part of the colour circle, so every device shows two or three neighbouring colours that run and turn, all in step, instead of one colour that cycles.
- **Switching the lights off always ends dark.** «Off» used to be skipped by a device that had just been given a frame (and could be lost when no page was open), so some lights stayed lit; it is now sent without the usual rate limits, three times, and the loop stays alive until every device is out.
- **Every light changes at the same moment, by itself.** Frames used to pile up behind slow devices (a motherboard header with 400 LEDs, memory over a slow bus), which made those lights run up to a second behind the others. Now Shabpa waits for the server to finish each frame before it sends the next one to that device, measures how long that device takes, never sends faster than it can take, and shows the effect that much ahead on it, so all the lights change together. There is nothing to set. A device with a few LEDs that looks out of step can show one colour (switch in the devices window).
- **Updates are installed by Shabpa.** When a newer version from a signed manifest is out, a window opens, counts 20 seconds (or the button starts it at once) and runs the same checked download and installer as the Updates page; it never appears while a game runs, cannot be closed while it works, and lets go with a message if the update fails. (`ForceUpdates` in the settings file switches it off.)
- **Shabpa opens in front when you start it.** Installing it or opening it from the shortcut always shows the window; only the start at Windows login (`--minimized`) stays in the tray.
- **Less memory while the window is open.** Pages you have not used for 90 seconds are let go (the two you left last stay, so going back is instant, and a page opens again in a moment when you click its tab) and the memory is handed back to Windows every two minutes.
- **Lighting is gentler on the hardware and the PC.** A device only gets a frame when its colours changed (or every two seconds), memory and motherboards (slow SMBus devices) at most ten a second, and when every light shows one fixed colour Shabpa looks four times a second instead of thirty. Listing the devices changes nothing on them: they are taken over only when the first frame is sent. The picture is drawn at most 20 times a second and only while the window is in front.
- **Lighting (RGB) control from inside Shabpa, in one click.** The «نورپردازی / Lighting» page is back in the menu and its typed commands work again. A single «Enable lighting» button (with a confirmation) fetches the official, free and open-source OpenRGB 1.1 once (22 MB, from its official release page, accepted only if it matches the SHA-256 fixed inside Shabpa), keeps it in Shabpa's own folder and runs it hidden as a lighting server that listens on this PC only (127.0.0.1, its own config, no window). It starts with the lights, stops when they are turned off or Shabpa quits, and nobody has to install, open or configure anything. Nothing is sent anywhere. Keyboards, mice, fans and many USB parts work at once; memory and motherboard lighting (and some graphics cards) additionally need the separate signed PawnIO driver and administrator rights, which Shabpa does not install by itself (OpenRGB says so in its log and the page explains). The server can take up to about a minute to find the hardware the first time.
- **Feathers while you work.** Every full hour you really spend at the PC with Shabpa open (typing or moving the mouse; an idle PC earns nothing) pays 3 feathers, up to 8 hours (24 feathers) a day, with a small quiet notice (none while game mode is quiet). Saving up for the shop is now a matter of days, not weeks.
- **A welcome gift of 200 feathers.** A new first one-time quest: write your name and your birthday in Settings and get 200 feathers, enough to dress Boofi in his first parts. It is paid once, to anyone who has both set (also those who set them before this update). With it the one-time quests are 25 in all (427 feathers).
- **Medal story pictures show Boofi exactly as you dressed him** (the «Boofi's cheer» design and the signature on the certificate), still and sharp, with his hat, glasses, neck piece, watch, lantern and fur.
- **A tidier footer.** «Made by PitPot Studio» instead of «Made by Mr.PitPot» (the program and installer subtitle).

### Fixed
- **Game tuning: the «windowed and borderless boost» no longer overwrites your other graphics preferences.** It used to replace the whole Windows graphics-preferences value (and so could drop settings like auto HDR); now it adds or removes only its own pair and keeps the rest, and a pair you already had is left in place when you switch it off. Every tweak was checked on and off against the real registry writer (including the full 32-bit network-brake value).
- The X3D pinning switch, which could never be turned on, is no longer shown until the feature exists.

### Changed
- **Much lighter on the GPU.** Measured with Windows' own GPU counters, the new owl and the animated themes cost about 5-8 % of the GPU (3.13.0: about 1 %), which can hurt a weak PC. Now: the owl is drawn once and only scaled while he breathes, breathing runs at 12 frames a second, his acts at 30 and his physics at 30 steps a second (a 144 Hz screen no longer runs them at 144), he acts every 5-11 seconds instead of every 2-6, and **nothing animates while the Shabpa window is not the one in front** (behind a game or another program: about 0.1 %). All 30 animated theme backdrops follow the same rules and more: each moving shape is drawn once into a texture and only moved, they run at 10-18 frames a second (not 60 or 144), they are not drawn while the window is behind, and **they freeze into a still picture after a minute without a key or mouse move** (Boofi rests after a minute and a half). Measured per theme with the window in front and the PC in use: before, 4-15 % of the GPU (the Matrix theme 15 %, Meteors 14 %); now mostly 0.2-3 %, in front about 2 % with the default theme (3.13.0: about 1 %); the tile-work theme, with its two big turning rosettes, was the heaviest at about 6 % before its last slow-down.
- The pictures of the shop and the wardrobe (tens of megabytes) are released when you leave those pages.
- «Programs using the most memory» on the memory page (free the idle memory of one program, or close it politely; Windows' own parts and security programs cannot be closed there; a forced close asks a second time).
- «My Boofi»: the six slot tiles are smaller and always sit in one row; in a window that is not full screen the stage moves above the parts instead of squeezing them. The leftover space of the shop grid is shared on both sides.
- **Feathers are easier to earn and the quests are richer.** The daily pool has 12 quests (three a day, never two of the same kind, e.g. one hour or two hours of games), with +4 feathers extra when all three are done. Every week now has three quests (one easy, one medium, one hard, from 9) with +15 extra for all three. There are 24 one-time quests that walk a new person through the app (227 feathers), and a regular person earns about 20 feathers a day. New counters are kept on this PC only. Paid one-time quests are never forgotten, even in a very long history.
- The owl is now called **Boofi** («بوفی», from «بوف», the Persian word for owl) instead of Bidar, everywhere in the app: the assistant, medals, levels, the settings switch, the tour and the medal story pictures. Typing «بوفی» to it is understood as a friendly hello. Saved progress is not affected.

## [3.13.0] - 2026-10-31

### Added
- **Medals, feathers and levels** (new page «مدال‌ها و مأموریت‌ها» / "Medals & quests"). Everything you do with Shabpa earns feathers and medals and makes Bidar grow
  through 100 levels. There is no daily cap, but every level costs more than the one before (level 10 is about a month of ordinary use, level 100 a goal for years).
  All of it is counted and kept on this PC only; nothing is sent anywhere.
  - 82 medals in tiers from bronze through silver, gold, platinum and diamond to legendary (the top rungs take years), plus 8 hidden ones (true owl, midnight, birthday, Halloween, early bird, Nowruz, Yalda...).
  - Daily, weekly and one-time quests, and a night streak (7 nights in a row pays a bonus).
  - A small notice above the taskbar when a medal is earned (it never takes focus and waits while game mode is quiet).
  - A medal becomes a **story picture** (1080×1920) in one of 10 designs you choose from (spotlight, collector card, Bidar's cheer, minimal poster, constellation, month in review, Persian tile, arcade, screen photo, certificate); the text is in the app's language only. Nothing is sent anywhere.
  - A «فروشگاه / Shop» tab that says "coming soon".
- **Bidar answers in the language you write in** (Persian in, Persian out; English in, English out), whatever the app's language is.
- **Bidar is funnier and can do more**: about 60 new jokes without repeats, true facts, tips, a pep talk, a calculator («۱۲*۳ حساب کن», "calc 12*3"), coin and dice, and answers about your level, today's tasks, uptime and disk space. The «what can you do» list is updated.

### Fixed
- The tour's game-tuning step no longer flickers: the tour used to re-open the page on every tick because the step names a tab inside it.

### Notes
- The new settings data (`Rewards`) is additive; older files load unchanged.

## [3.12.1] - 2026-10-31

### Added
- **Power estimate learns from every play session**: the highest steady draw the card showed is remembered and the maximum never stays below it (even a typed one).
- **Internet speed unit** in Settings (drop-down): megabit (Mb/s) or megabyte (MB/s). Used by the overlay, Home and Ping stability, whose per-app and in-game caps now use that unit with even-number presets and one custom box (faint hint, tick button).
- **Own colour for each part of the overlay** (frame rate, GPU, CPU, RAM, NET, clock) with a reset button, and a switch to hide the small part names (LOAD, TEMP, HOT, CLK...) without hiding the group names.
- **Clock with seconds** in the overlay.
- 11 more DNS servers (public, secure and family filters).
- Health: the 7-day error counter now opens its details in a card right under it instead of scrolling to a big card at the bottom.

### Changed
- Overlay strip: every number has one size and sits on one line with its name and unit; the plate is only as tall as its text; the word FPS sits next to its number. Card, compact, minimal and graph layouts use the same font and size.
- DNS ping figures are coloured again (green, amber, red) even when the network answers for the servers.
- Ping stability: the "ping right now" card no longer stretches in a full-screen window.

### Fixed / performance
- Weather scene animates for everyone: only the in-app animations switch and low-memory mode stop it, no longer Windows' global animation switch or the window being inactive.
- Page switching no longer waits for a disk write (the visit counter is saved a moment later) or for a PATH scan for winget.
- The overlay draws at most one frame per sample and never queues several while the window is busy.

### Added
- **Halloween theme** in the list of themes (orange and violet), next to the seasonal look.
- **Bidar the owl understands much more** - open any page («برو تنظیمات», "open DNS"), the overlay (on, off, only in game, layout, size), the theme, free memory, game mode, and quick questions: the time, the date, CPU and GPU temperature, memory, ping, system status, a help list, small talk and jokes. Many more phrasings in both languages for the old commands too.
- **More greeting lines on Home** - about 150 new lines for the hours of the day, plus lines for each season, each day of the week, special days (Nowruz, Yalda, New Year, Halloween) and the games: while one runs, after one is logged (with its name), after a long day of play, when the last game ran hot, and own lines for about 25 well-known games.
- **Tick sounds** - the little interface sounds are now crisp switch-like ticks (a light tick on hover, a soft "thock" on click, rising and falling pairs for on and off).
- **Halloween (28 October to 1 November, on by default)** - an orange-and-violet look, and the first time the window opens in the season (once a year) a drawn pumpkin leaps out with one flash and a short scream made in the app, then a greeting card. No sound while a game runs; with animations off only the card shows. One switch and a preview button in Settings.
- **Game tips in the tour** - the guided tour now covers game tuning, scheduled downloads, the new overlay sizes, the warm night light, the DNS warning and clickable health errors.
- **Game tuning (System control → Game tuning)** - ten documented Windows settings in three tabs (processor & memory, Windows & system, graphics), each with an "i" that explains it on hover: game gets the processor first, game scheduling priority, background share of the processor, Windows' own Game Mode, stop Game Bar's hidden recording, keep Store apps asleep, stop sharing updates with others, lift the multimedia network brake, merge service processes, hardware-scheduled graphics (HAGS), keep the PCIe lane awake, windowed/borderless game boost. Each asks first, keeps your own value before changing it and puts exactly that back when switched off; "put everything back" undoes all. Nothing disables a program, service or file. The X3D pinning row only shows whether such a processor exists (not built yet).
- **Overlay bar layout redesigned** - big numbers with small units, the frame rate large on the left and one coloured name per group (GPU, CPU, RAM, NET) instead of a label on every number.
- **Overlay size and opacity** - size 25-200 % and background opacity 0-100 % in one-percent steps; at 0 % there is no plate and no outline (the numbers keep a shadow).
- **Health: error groups are clickable** - a click shows what Windows wrote, when, and what it usually means; the counters jump to their list. Groups that did not come back since Windows last started, or that you marked "fixed", are set aside (they return if a newer event happens).
- **Downloads:** any program (not only launchers) can be added and taught clicks; the texts say so.

### Changed
- **Every overlay layout is bigger and easier to read** (card, compact, minimal, graph, rings); the bar names what each number is (LOAD, TEMP, CLK...) and sits closer to its edges.
- **Confirmation dialogs are only the sheet** - no Windows frame, title bar or close button around them; the Windows outline of the main window is removed too (it already had its own minimise, maximise and close buttons).
- **Night light warm colour** now warms the display's colour curve like Windows' Night Light (the amber plate over the screen greyed the picture). The darkness stays a separate layer. A screen that refuses the curve falls back to the old plate. The curve is put back on switch-off and on exit.

### Fixed
- **DNS ping numbers were wrong behind a VPN/proxy** - with a tunnel client (or a router/ISP) that answers every DNS question itself, all servers, even a non-existent one, showed under 30 ms. The test now detects this (a question to the reserved address 192.0.2.53 gets an answer) and shows the numbers dimmed with "~" and an explanation.
- **Window freezes of many seconds** - the first network request of the weather, support and update pages ran on the window thread (proxy lookup and name resolution took up to 21 s in a real log); they now start on a background thread.
- **Ping stability list scrolled slowly on weak PCs** - unchanged text and animations are no longer rewritten every second, the list is not rebuilt unless its order really changed, and numbers wait while the wheel is turning.
- **Launchers (for example the Riot Client) opened the Documents folder** - they are now started through Explorer's desktop shell, with normal rights, in their own folder.
### Added (earlier, same release)
- **Command box (assistant, phase 0)** - type a command in Persian or English («یه ساعت و ربع دیگه خاموش کن»، «ساعت ۲ و نیم شب بخوابون»، «وقتی دانلود تموم شد خاموش کن»، "remind me at 5pm to call mom"). It works fully offline with no language model: shutdown, sleep, hibernate, restart, lock, cancel, wake timers, reminders, focus timer, night light, sleep mode and the list of what is scheduled, plus the conditions the rules already have (download or upload done, idle, a program closed, heavy work done). Every command shows a card with the exact time first; anything unclear (2 o'clock morning or afternoon?, a question, «خاموش نکن») offers choices instead and runs nothing. Open it from the tray menu or with Ctrl+Alt+Space (changeable in Settings). Bidar the owl shows what it is doing.
- **Telegram:** the app understands a new `text` bot command with the same parser, so free text sent to the bot can be routed to it once the bot server forwards it.

## [3.11.5] - 2026-10-24

### Fixed
- **Game reports did not reach Telegram when `bot.shabpa.ir` was cut on the network** - the log showed hours of `bot event game: failed, will retry (HttpRequestException)`. The app now has a second address for the same bot server (on another domain) and switches to it when the first does not answer; the one that answered last is tried first. A custom address in Settings is still used alone. Failure lines in the log now say what the socket reported (name not found, connection reset, timeout).

## [3.11.4] - 2026-10-24

### Fixed
- **Internet usage (downloads and uploads) was shown about twice as high behind a VPN or proxy** - the per-app meter counted traffic to this PC itself (loopback and the 198.18.x tunnel range of Clash/Mihomo-style clients) as well as the proxy's own connection that carries the same bytes to the internet. That traffic still counts for an app's live speed (so limits keep working) but no longer for the daily totals, the usage history, the monthly cap and game reports. Totals recorded before this version stay as they were.

## [3.11.3] - 2026-10-24

### Fixed
- **Telegram game reports that waited for the connection now say when the game really ended** - if the PC could not reach the bot when a game closed, the report stayed in the queue and arrived later (hours, all at once) looking like a fresh report. The app now sends the time the event happened, and the bot adds a line with it when a report arrives late.

## [3.11.2] - 2026-10-23

### Fixed
- **Birthday preview (and a few other buttons) did nothing when the night dim was on at start** - the dim cover is a window too, and it opens before the real one; WPF keeps the first window ever created as the application's "main window", so everything that looked the window up that way (the birthday preview, the Home page quick cards, the theme switch, the glass backdrop) silently found the dim cover instead and did nothing. The real window is now made the main window explicitly, and the log says when another window was first. Proven with a live run: with dimming on, the log shows `Window "Shabpa dim" opened first`.

## [3.11.1] - 2026-10-23

### Fixed
- **Crash when the mouse wheel turned over a text box** - on Settings (name / birthday, power fields, recorders), Support and Update, scrolling with the pointer over any text box ended the app at once with "A new guard page for the stack cannot be created". The wheel handler passed the turn on to the page behind by re-raising it, and for the ScrollViewer inside a text box that re-raise came straight back to itself until the stack ran out. The wheel now goes to the nearest viewer above, found by walking the tree, and the hand-over is capped. Found from a crash dump; covered by new tests.
- **Internet cut for a few seconds, and window stalls, while a speed limit is active** - the speed limiter held its packet lock while it read the table of ports and looked up every process name, which can take seconds on a busy PC and stops the traffic of the whole computer. That work now runs outside the lock.
- **Ping page stalls** - program icons were read from disk on the window thread (535 ms in a real log); they now load in the background and appear when ready.
- **Main window lookup** - a few buttons (birthday preview, quick cards, theme switch) found the window through the application's "main window", which is the first window ever created. The real window is now set explicitly.
- Update signature: the published `latest.json.sig` of 3.11.0 did not match the manifest, so the mirror update path could not verify it. Re-signed (no change to the installer).

## [3.11.0] - 2026-10-22

### Added
- **Bidar, the night-guard owl** - a small animated owl lives in the corner of every page. Click it to open a speech bubble and type a short Persian or English command (offline, no network, no language model): "shut down at 2 am", "sleep when the download is done", "remind me in half an hour". Every power action still goes through the confirmation card with the exact time, and 12-hour ambiguity asks you to choose. The owl has idle life (blinks, looks around, yawns, naps, flies to another perch), reacts to events and to your clicks, and stays still while you type, during games, when the window is hidden or when animations are off. It can be switched off in Settings, together with its idle life.
- **Per-PC power estimate** - the power-estimate dialog detects this PC's own numbers (CPU, GPU, RAM, disks, laptop or desktop; NVIDIA power now read through NVML), learns the real GPU peak and idle power over time, and never overwrites values you typed. Fields show Detected / Learned / Edited badges with reset buttons.
- **Update mirrors that survive a cut internet** - updates are looked up on GitHub, shabpa.ir and an ArvanCloud storage box in Iran, with an extra mirror you can add. The update manifest is signed (ECDSA P-256) and the installer hash is checked, so a hostile mirror cannot push a file. If every source fails, the Updates page offers the download page, "Install from file..." and "Import update bundle...".
- **Crash tracking** - if Shabpa ends unexpectedly, the next start logs the version, the last page, the last navigation step and the time; Windows shutdown and installer closes are told apart from real crashes. Slow steps (250 ms and up) and long window stalls are logged with evidence.

### Fixed
- **Overlay:** choosing a corner after dragging the overlay could send it to the opposite side; positions are now reset to clean margins. After placement the overlay no longer moves or changes size when numbers change (fixed-width slots, size measured once).
- **Security page:** a crash ("already the logical child of another element") when running as administrator.
- **Freezes:** the startup-entry scan and a sleeping-disk check no longer run on the window's thread; the DNS page no longer starts nested message loops; page exit animations cannot wait forever; refresh errors in one page no longer take the window down.
- **Pages left listening to app events after they were unloaded** and kept refreshing in the background.
- **Bot link:** failing connections back off and are logged once per kind; lost game-end messages are queued and retried.
- **Layouts and sliders:** ping destination box size, Home button spacing, weather city card, and sliders you can press anywhere and drag.
- **Theme change:** shadows under buttons, tab thumbs and glass cards follow the new theme.

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
