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

## 3.0.1

- Screen dimming no longer flashes back to full brightness once a second.

## 3.0.0 and earlier

See the [GitHub releases](https://github.com/MrPitPot/shabpa/releases).
