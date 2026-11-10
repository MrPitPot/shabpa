# Game log / Playtime (branch feature/game-log, for 3.6.0)

## Built
- **Recorder** (`Core/GameLog.cs`): `GameSessionRecorder` is fed by the existing Telemetry tick (no new timers). It takes one look every 5 s while a game process lives (also when another window is in front: Telemetry then ticks every 5 s without FPS counting). Keeps min/avg/max plus a series of at most 120 points for CPU/GPU temp, CPU/GPU load, RAM, net down/up and FPS (only if the overlay FPS meter has it). Raw buffer is bounded (neighbours merge). Net bytes come from the per-app counters (admin), otherwise the whole PC's traffic is integrated and marked approximate. Other programs: two process listings 8 s apart, first at 40 s and then every 6 min (max 40), top 4 by CPU plus top 4 by RAM, aggregated by name, at most 8 kept, skipping system processes, the game and Shabpa. Local only. Sessions under 1 min are dropped.
- **Storage**: `%AppData%\Shabpa\games.json` (summaries only) and `games-series\<id>.json` (curves, read only when a session is opened, so RAM stays tiny). Atomic write with a `.bak`, corrupt file moved to `.broken` and the backup used. Caps: 200 sessions per game, 1000 in total. `state.json` is untouched (new setting `GameLog` defaults to true, old states load fine).
- **Notification**: the existing "report after every game" setting (default on) now shows game, time, internet, avg/max temps, peak load. Click on the toast or tray balloon opens that game's log (`Hub.Notify(..., target)`, `OpenPage`). New setting "Log game sessions" in Settings > behaviour.
- **Page «زمان بازی / Playtime»** (`Pages/GameLogPage.cs`, nav under Gaming & network, new icons `game` and `back`): Today / Week / Month / All filter, 4 stat cards, game list with bar and percentage, per-row delete (AskAlways confirm), empty states, detail view with back button, session list, per-session mini charts (Sparkline), background programs, delete session / delete game. Animations guarded by Motion.Enabled, Fx.Attach on clickable rows.
- **Demo**: `--demo-games` (needs SHABPA_DATA, writes fake sessions into that scratch folder), demo/screenshot runs seed in memory, `--empty-games` shows the empty state.

## Try it
```
$env:SHABPA_DATA="C:\scratch\d"; $env:SHABPA_INSTANCE="t"
Shabpa.exe --demo-games --dry-run --page playtime
```
Screenshots: `design-check\gamelog` (fa dark: list, detail, scrolled), `en-light`, `empty`.

## Optimisation (tray idle, Release, 40 s span, 3 runs each, base = commit 6a9809e)
| | before | after |
|---|---|---|
| private WS, tray idle | 37.7-38.5 MB | 23.8-24.4 MB |
| CPU, tray idle | 0.049-0.090 % | 0.031-0.055 % |
| private WS, off-screen window (2 runs, before the optimisations) | 122-124 MB | 120 MB |
Changes: GPU engine counters (about 800 instances, the heaviest part of the 3 s tick) are read every third tick when nothing live is on screen, no game is alive and Windows reports normal desktop use; adapter list cached (refreshed on network change or every minute); process names via one ToolHelp snapshot instead of Process objects; FPS process name cached per pid; dim layer no longer enumerates screens and builds strings every second. In-game overhead could not be measured without a foreground game window; the recorder costs one cheap look per 5 s plus a 2-listing process look every 6 min. Window-mode numbers after the optimisations were not re-measured.

## Tests
595/595 (15 new in `GameLogTests.cs`: start/end, short sessions, downsampling and bounded memory, midnight counter reset, process aggregation, caps, delete, corrupt/backup, old state, read-only, totals, summary text). Page smoke is the screenshot runs above.
