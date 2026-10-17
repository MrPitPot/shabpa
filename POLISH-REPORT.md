# Polish report (branch fix/overlay-ring-theme-reveal)

## 1. Overlay "rings" layout (Overlay.cs)
- Cause: the value was a fixed 14 pt bold text in a 62 px ring whose track (radius 28) and arc (radius 25.5) did not even share a centre line, so 3-4 character values touched the arc.
- Now: ring 68 px, stroke 5.5, track and arc share one radius; the number is auto-fitted (FormattedText, biggest font <= 17 whose box fits the free disc inside the ring with a margin; "9.2" large, "156"/"1.4k"/"1234" smaller, min 8); label under the ring is shrunk to the cell if needed, row height follows (no empty strip with labels off); many gauges wrap into balanced rows (7 = 4+3); values stay forced LTR (Persian digits tested). Everything scales with the size slider as one piece (LayoutTransform), so stroke/radius/text stay proportional.
- Other layouts (card, bar, compact, minimal, chart) were not touched; their sheets are in the after folder for comparison.
- Test aid: `--screenshots <tmp> --overlay-gallery <dir> [--lang fa|en] [--theme x]` draws all layouts x 4 plates (theme, sunset, mono, light custom) x metric sets (1, 3, 7, many, Persian digits) and a scale sheet (0.5/1/1.5/2.5).
- Frames: design-check/polish/ring-before (old renderer; en = night theme, fa = white theme) and ring-after (en and fa).

## 2. Live theme switch with circular reveal
- The title-bar button no longer asks for confirmation and no longer restarts: `MainWindow.SwitchTheme` (new file MainWindow.Theme.cs). Verified same PID, same window object, window stays visible (report-*.txt).
- Flow, all in one dispatcher frame: RenderTargetBitmap snapshot of the window root (DPI-correct, RTL un-mirrored, stand-in backing under glass/mica) -> topmost Image in the root -> `Ui.SetTheme` -> clip animation (Exclude of full rect minus growing ellipse from the button centre to the farthest corner, 680 ms quad ease-in-out) -> overlay and bitmap released. Same animation both directions (new theme always grows from the button).
- Animations off / low-memory / Windows animation effects off: instant swap. Resize, state change or hide during the animation: finishes cleanly at once. A new toggle during a reveal finishes the old one first.
- Ui.cs: XAML brush references were StaticResource (resolved once, and WPF freezes brushes used in templates), so brush keys in Theme.xaml, MainWindow.xaml and CountdownWindow.xaml became DynamicResource and `SetTheme` replaces the dictionary entries; code-built screens get long-lived brushes through `K.Brush` / `Ui.Live` that are rewritten in place. Confirm, countdown, toast, HUD windows and the tray follow (shared resources; HUD reads Ui.Theme on its next draw).
- The persisted setting is saved (Hub.Settings.Theme + Store.Save). The 21 themes in Settings now switch live too (reveal starts from the clicked chip); language change still restarts, hint text updated.
- Test aid: `--screenshots <tmp> --theme-reveal-test <dir> [--lang x] [--page x]` saves frames at 0/150/300/450/700 ms for dark->light and light->dark, toggles 20 times and writes report-<lang>.txt (memory, pid, running flag).
- Frames: design-check/polish/reveal (fa + en).
- Memory (private bytes, after 6 warm-up toggles vs after 20 more, forced GC): en 337.9 -> 217.4 MB, fa 231.5 -> 191.5 MB (no growth; private bytes swing with the GPU driver; managed heap flat at 11.3/11.5 MB; working set 223->209 and 234->210 MB).

## Not verified / limits
- Not tested with real Mica/Acrylic on screen (off-screen runs use glass "off"); the glass path uses a flat stand-in under the old snapshot, so during those 0.7 s the old area is flat instead of true Mica.
- kit:Shadow shadows already baked into the XAML shell (nav pill, toast, countdown) keep their old colour until restart; they are black-based so hardly visible. Pages that stay cached in memory (mic, overlay, settings, weather, ping, dns, download) keep any colours baked at build time (e.g. chart gradients) until reopened after a restart; styles and K.Brush colours do follow.
- Overlay HUD in a real game was not run (same renderer as the gallery).
- Tests: dotnet test -c Release, 580/580 green.
