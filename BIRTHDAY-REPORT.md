# Birthday feature (branch feature/birthday, worktree Shabpa-birthday)

## What it does
- **Settings card «نام و تاریخ تولد / Name and birthday»** (Pages/BirthdayCard.cs, column 1 of Settings): name (max 30, trimmed, any script), calendar switch (Jalali default in fa, Gregorian in en; switching converts the date), month chips, day box, optional year, Save, **Preview the birthday**, Clear (confirm via the project's Ask), «تم تولد در روز تولد» switch (default on), privacy note. Esfand 30 / Feb 29 are accepted and, in common years, celebrated on the last day of that month; a birth year in which the day did not exist is rejected.
- **Storage**: `Settings.Birthday` (Core/Birthday.cs, `BirthdaySettings`), additive; a tolerant JSON converter ignores wrongly typed or out-of-range values instead of failing the whole file. Old state.json files load with an empty birthday. No network code.
- **Birthday day**: `Hub.CheckBirthday` runs on the existing 1-second tick (a date compare; no new timer). It flips the look at midnight, and shows the greeting once per day (`LastGreeted`). Never while a game runs or game mode is active (retried on later ticks). Window shown: glass card + confetti over the window (MainWindow.Birthday.cs). Window hidden: tray balloon through `Hub.Notify` (so game-mode quiet applies), once per day (`LastBalloon`), the card follows when the window is shown.
- **Confetti** (Kit/Confetti.cs): 56 pre-built pieces, recycled, transform and opacity only, one render hook that removes itself after 6 s; with Motion disabled it is a still decoration and nothing runs.
- **Theme for the day**: `Birthday.Tint(palette)` gives a pink/gold/violet version of the chosen theme (dark and light) with key `<theme>-birthday` and `BaseKey`. Applied through the 3.6.0 mechanism (`Ui.SetTheme`, live brushes, circle reveal; `MainWindow.ApplyBirthdayLook`). The saved theme is never changed; it reverts on the next day, when the birthday is cleared or the option is switched off. Home header shows «🎂 تولدت مبارک {name}», a static confetti strip decorates the title bar. Overlay and tray menu use `Ui.BaseTheme` (unchanged). Other app windows share the app resource dictionary and therefore follow the look.
- **Test aids**: `SHABPA_FAKE_TODAY=yyyy-MM-dd` (or full date-time) honoured only in Debug / SHABPA_TESTHOOKS builds; `--birthday-probe <folder>` (same builds) scripts the whole flow with screenshots and a log. `run-bday.sh <fa|en> <night|white>` runs it offscreen with scratch data.

## Files
New: Core/Birthday.cs, Kit/Confetti.cs, Pages/BirthdayCard.cs, Pages/BirthdayProbe.cs, MainWindow.Birthday.cs, tests/Shabpa.Tests/BirthdayTests.cs, run-bday.sh.
Changed: Core/Models.cs, Core/Palette.cs, Core/GlassTokens.cs, Hub.cs, Ui.cs, MainWindow.Theme.cs, MainWindow.xaml.cs, App.xaml.cs, Overlay.cs, Tray.cs, Pages/HomePage.cs, Pages/SettingsPage.cs, Pages/PageBase.cs.

## How to preview
`SHABPA_DATA=<scratch> SHABPA_FAKE_TODAY=2026-11-06 ./run-bday.sh fa night` (Debug build), or fill the Settings card and press Preview. Screenshots: design-check/birthday (settings empty/filled, greeting mid-shower and static, home, control, playtime, settings, home next day; fa/en, dark/light) and probe-*.txt.

## Verified (Debug build, offscreen, fake day 2026-11-06)
Greeting opens once, shower runs then stops (running=false, 0 visible pieces after 6 s), ~0.4-1.2% of one core while the settled card stays open, no second greeting the same day, look on during the day and reverts the next day with the saved theme untouched, in all four fa/en x dark/light runs.

## NOT verified
- **The .NET 10 SDK disappeared from this machine during the work (only SDK 8.0.426 is installed), so after the last edit nothing could be rebuilt.** The last edit (Core/GlassTokens.cs: light birthday look now uses the "white" surfaces with a pink/gold wallpaper because the generic light derivation looked yellow) and tests/Shabpa.Tests/BirthdayTests.cs have **never been compiled or run**. The light screenshots in design-check/birthday predate that fix (yellowish). `dotnet build -c Release` and `dotnet test -c Release` (595 existing + the new tests) still have to be run, then `./run-bday.sh en white` and `fa white` re-run.
- Release-build memory comparison against main was not done.
- Tray balloon path and the game-defer path only by code reading (demo run has no game).
- Birthday is not part of the settings export file not checked: it travels through the generic Settings copy; check if undesired).
