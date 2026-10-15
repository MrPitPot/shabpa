using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Shabpa.Core;

namespace Shabpa;

public partial class App : Application
{
    private const string MutexName = "Shabpa.SingleInstance";
    private const string ShowEventName = "Shabpa.Show";
    /// <summary>Set by the installer to make a running copy exit cleanly before files are replaced.</summary>
    public const string QuitEventName = "Shabpa.Quit";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private Tray? _tray;
    private MainWindow? _window;
    private string[] _args = [];

    private static string? Value(string[] args, string name)
    {
        var index = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // All user-facing text is formatted by hand (Fa). Without this, a Persian regional format makes
        // .NET print Jalali dates and "/" decimals into things like the Task Scheduler XML.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        _args = e.Args;
        var args = e.Args.Select(a => a.ToLowerInvariant()).ToList();

        // The wake task's whole job: runs next to a possibly already-running instance, then exits.
        if (args.Contains("--wake"))
        {
            Log.File = Path.Combine(new Store().Folder, "shabpa.log");
            WakeScheduler.HandleWake();
            Shutdown();
            return;
        }

        if (Value(e.Args, "--diagnose") is { } report)
        {
            File.WriteAllText(report, Core.Diagnostics.Run());
            Shutdown();
            return;
        }

        // Test aid: press Steam's resume button once (as the scheduled download does) and write what happened.
        if (Value(e.Args, "--steam-resume-test") is { } steamReport)
        {
            var folder = Path.GetDirectoryName(Core.LauncherDetector.Detect().FirstOrDefault(l => l.Key == "steam")?.ExePath) ?? "";
            var outcome = Core.SteamResume.Run(() => Core.SteamResume.LogShowsDownload(folder, DateTime.Now));
            File.WriteAllText(steamReport, outcome.ToString());
            Shutdown();
            return;
        }

        if (Value(e.Args, "--epic-resume-test") is { } epicReport)
        {
            File.WriteAllText(epicReport, Core.EpicResume.Run().ToString());
            Shutdown();
            return;
        }

        if (Value(e.Args, "--riot-resume-test") is { } riotReport)
        {
            File.WriteAllText(riotReport, Core.RiotResume.Run().ToString());
            Shutdown();
            return;
        }

        // Test aid (needs admin): sets a DNS on the main adapter, reads it back, then restores what was there.
        if (Value(e.Args, "--dns-test") is { } dnsReport)
        {
            var log = new System.Text.StringBuilder();
            var adapters = Core.DnsConfig.Adapters();
            foreach (var a in adapters) log.AppendLine($"adapter {a.Name} #{a.Index} main={a.IsLikelyMain} auto={a.IsAutomatic} dns=[{string.Join(", ", a.Servers)}]");
            var main = adapters.FirstOrDefault(a => a.IsLikelyMain) ?? adapters.FirstOrDefault();
            if (main is null) log.AppendLine("no adapter");
            else
            {
                var before = main;
                var pings = Core.DnsCatalog.All.Take(12).Select(d => (d, ms: Task.Run(() => Core.DnsProbe.PingAsync(d.Primary)).GetAwaiter().GetResult())).ToList();
                foreach (var (d, ms) in pings) log.AppendLine($"ping {d.Name} {d.Primary}: {(ms is null ? "timeout" : $"{ms:0} ms")}");
                log.AppendLine($"elevated={Core.Elevation.IsElevated}");
                log.AppendLine("apply 1.1.1.1/1.0.0.1: " + (Core.DnsConfig.Apply(main, "1.1.1.1", "1.0.0.1") ?? "OK (read back)"));
                var mid = Core.DnsConfig.Adapters().First(a => a.Index == main.Index);
                log.AppendLine($"now: auto={mid.IsAutomatic} dns=[{string.Join(", ", mid.Servers)}]");
                var restore = before.IsAutomatic ? [] : before.Servers;
                log.AppendLine("restore: " + (Core.DnsConfig.Apply(main, restore) ?? "OK (read back)"));
                var after = Core.DnsConfig.Adapters().First(a => a.Index == main.Index);
                log.AppendLine($"after: auto={after.IsAutomatic} dns=[{string.Join(", ", after.Servers)}]");
            }
            File.WriteAllText(dnsReport, log.ToString());
            Shutdown();
            return;
        }

        // Test aid: dims the screen, measures how much darker a few screen pixels got (only numbers are written, no picture),
        // then presses the toggle shortcut for real and measures again.
        if (Value(e.Args, "--dim-test") is { } dimReport)
        {
            RunDimTest(dimReport);
            return;
        }

        // Test aid: draws the overlay with groups placed apart and writes where each overlay window ended up.
        if (Value(e.Args, "--blocks-test") is { } blocksReport)
        {
            Resources.MergedDictionaries.Add(Ui.Resources(Palette.Find(null)));
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Shabpa;component/Theme.xaml", UriKind.Relative) });
            var bs = new Store { ReadOnly = true };
            var bh = new Hub(bs, true, true);
            var oo = bh.Settings.Overlay;
            oo.Mode = OverlayMode.Always;
            oo.Items = ["fps", "gpu", "gputemp", "cpu", "ram", "ping", "clock"];
            oo.Places.Add(new GroupPlace { Group = "gpu", Anchor = "tr" });
            oo.Places.Add(new GroupPlace { Group = "cpu", Anchor = "tr" });
            oo.Places.Add(new GroupPlace { Group = "ram", Anchor = "bl" });
            var win = new OverlayWindow(bh);
            new System.Windows.Interop.WindowInteropHelper(win).EnsureHandle();
            win.Evaluate();
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                var lines = Windows.OfType<Window>().Where(w => w.IsVisible)
                    .Select(w => $"{w.Title}: x={w.Left:0} y={w.Top:0} w={w.ActualWidth:0} h={w.ActualHeight:0}");
                File.WriteAllText(blocksReport, string.Join(Environment.NewLine, lines));
                Shutdown();
            };
            t.Start();
            return;
        }

        // Used by the installer: switch start-with-Windows on or off for this exe, then exit.
        if (Value(e.Args, "--autostart") is { } autostart)
        {
            Log.File = Path.Combine(new Store().Folder, "shabpa.log");
            var error = Autostart.Set(autostart == "on", Hub.ExePath, Elevation.IsElevated);
            if (error is not null) Log.Write($"autostart: {error}");
            Shutdown(error is null ? 0 : 1);
            return;
        }

        if (Value(e.Args, "--weather-code") is { } wc && int.TryParse(wc, out var code)) Pages.WeatherPage.DemoCode = code;
        Pages.WeatherPage.DemoNight = args.Contains("--weather-night");

        var shots = Value(e.Args, "--screenshots");
        // --live keeps real sensors and the real graphics card in a screenshot run (nothing is saved or executed).
        var demo = args.Contains("--demo") || (shots is not null && !args.Contains("--live"));
        var dryRun = args.Contains("--dry-run") || demo;

        if (shots is null)
        {
            _mutex = new Mutex(true, MutexName, out var first);
            if (!first && args.Contains("--restart"))
            {
                // The instance that launched us (language/theme change, or elevation) is still shutting down.
                try { first = _mutex.WaitOne(TimeSpan.FromSeconds(25)); }
                catch (AbandonedMutexException) { first = true; }
            }
            if (!first)
            {
                // Already running: ask that instance to come forward, then leave.
                try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { /* it is shutting down */ }
                Shutdown();
                return;
            }
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            new Thread(() =>
            {
                while (_showEvent.WaitOne())
                    Dispatcher.BeginInvoke(ShowWindow);
            }) { IsBackground = true, Name = "show-listener" }.Start();
            var quitEvent = new EventWaitHandle(false, EventResetMode.ManualReset, QuitEventName);
            new Thread(() =>
            {
                quitEvent.WaitOne();
                Dispatcher.BeginInvoke(Quit);
            }) { IsBackground = true, Name = "quit-listener" }.Start();
        }

        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Write($"unhandled: {ex.Exception}");
            ex.Handled = true;
        };

        var store = new Store();
        Log.File = Path.Combine(store.Folder, "shabpa.log");
        if (demo) store.ReadOnly = true;
        else
        {
            store.Load();
            if (shots is not null) store.ReadOnly = true;
        }

        // Language and theme are fixed for the lifetime of the process (command line wins, for screenshots).
        var settings = store.State.Settings;
        L.En = (Value(e.Args, "--lang") ?? settings.Language) == "en";
        Resources.MergedDictionaries.Add(Ui.Resources(Palette.Find(Value(e.Args, "--theme") ?? settings.Theme)));
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Shabpa;component/Theme.xaml", UriKind.Relative) });

        var hub = new Hub(store, dryRun, demo);
        if (demo) hub.FillDemo();
        hub.Start();

        _window = new MainWindow();
        if (Value(e.Args, "--page") is { } page) _window.Navigate(page);
        if (shots is not null)
        {
            _window.Show();
            // --interactive: just show the window (real sensors, nothing saved) so it can be driven by hand or by a test.
            if (!args.Contains("--interactive")) _ = CaptureAll(shots, Value(e.Args, "--only"));
            return;
        }

        if (!demo)
        {
            var overlay = new OverlayWindow(hub);
            new System.Windows.Interop.WindowInteropHelper(overlay).EnsureHandle(); // registers the hotkeys while hidden
            hub.Overlay = overlay;
            overlay.Evaluate();
        }

        _tray = new Tray(ShowWindow, Quit);
        hub.Notified += _tray.Balloon;
        hub.Changed += () => _tray.Refresh();
        _tray.Refresh();

        if (!args.Contains("--restart") && (args.Contains("--minimized") || hub.Settings.StartMinimized)) return;
        _window.Show();
    }

    public void ShowWindow()
    {
        if (_window is null) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public void Quit()
    {
        _tray?.Dispose();
        _window?.Hide();
        Hub.I.Shutdown();
        try { _mutex?.ReleaseMutex(); } catch { /* not owned */ }
        Shutdown();
    }

    /// <summary>Starts a fresh copy and hands over to it. With <paramref name="elevated"/> Windows asks for
    /// admin rights first; returns false if the user said no.</summary>
    public bool Restart(bool elevated = false, string? page = null)
    {
        // Keep the mode flags the app was started with (--dry-run, --demo), drop one-shot ones.
        var keep = _args.Where(a => a.ToLowerInvariant() is "--dry-run" or "--demo").ToList();
        keep.Add("--restart");
        if (page is not null) keep.AddRange(["--page", page]);
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Hub.ExePath, string.Join(' ', keep))
            {
                UseShellExecute = true,
                Verb = elevated ? "runas" : "",
            });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
        Quit();
        return true;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);

    private void RunDimTest(string report)
    {
        var log = new System.Text.StringBuilder();
        var store = new Store { ReadOnly = true };
        var hub = new Hub(store, true, false);
        var comfort = hub.Settings.Comfort;
        comfort.DimOn = false;
        comfort.DimLevel = 0.7;
        comfort.DimWarm = false;
        comfort.DimKey = "Ctrl+Alt+F11";
        var dim = new DimController(hub);

        static double Brightness()
        {
            var bounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
            using var bmp = new System.Drawing.Bitmap(1, 1);
            using var g = System.Drawing.Graphics.FromImage(bmp);
            double sum = 0;
            var points = new[] { (0.2, 0.3), (0.5, 0.5), (0.8, 0.7), (0.3, 0.8), (0.7, 0.2) };
            foreach (var (fx, fy) in points)
            {
                g.CopyFromScreen(bounds.X + (int)(bounds.Width * fx), bounds.Y + (int)(bounds.Height * fy), 0, 0, new System.Drawing.Size(1, 1));
                var c = bmp.GetPixel(0, 0);
                sum += 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
            }
            return sum / points.Length;
        }

        void Press()
        {
            keybd_event(0x11, 0, 0, UIntPtr.Zero);   // Ctrl
            keybd_event(0x12, 0, 0, UIntPtr.Zero);   // Alt
            keybd_event(0x7A, 0, 0, UIntPtr.Zero);   // F11
            keybd_event(0x7A, 0, 2, UIntPtr.Zero);
            keybd_event(0x12, 0, 2, UIntPtr.Zero);
            keybd_event(0x11, 0, 2, UIntPtr.Zero);
        }

        var steps = new Queue<Action>();
        double before = 0, dimmed = 0;
        steps.Enqueue(() => { before = Brightness(); dim.Apply(); log.AppendLine($"hotkey failed: [{string.Join(",", dim.FailedHotkeys)}]"); });
        steps.Enqueue(() => { Press(); });          // toggles on (it was off)
        steps.Enqueue(() => { dimmed = Brightness(); log.AppendLine($"active after hotkey={dim.Active} before={before:0.0} dimmed={dimmed:0.0}"); Press(); }); // toggles off
        steps.Enqueue(() => { var back = Brightness(); log.AppendLine($"active after second hotkey={dim.Active} back={back:0.0}"); });
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        timer.Tick += (_, _) =>
        {
            if (steps.Count > 0)
            {
                steps.Dequeue()();
                return;
            }
            timer.Stop();
            dim.Dispose();
            File.WriteAllText(report, log.ToString());
            Shutdown();
        };
        timer.Start();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        Hub.I?.Store.Save();
        base.OnSessionEnding(e);
    }

    // ---- self-screenshots: lets the UI be reviewed without touching the real desktop ---------

    private async Task CaptureAll(string folder, string? only)
    {
        try
        {
            Directory.CreateDirectory(folder);
            await Task.Delay(5000); // two sensor ticks so the live numbers are real
            foreach (var page in _window!.CaptureKeys.Where(k => only is null || only.Split(',').Contains(k)))
            {
                _window.Navigate(page);
                await Task.Delay(page == "gaming" ? 2500 : 900);
                var name = page.Replace(':', '-');
                Save(_window.CaptureRoot, Path.Combine(folder, $"{name}.png"));
                var extra = 0;
                while ((only is null || page == "gaming") && _window.ScrollPage())
                {
                    await Task.Delay(300);
                    Save(_window.CaptureRoot, Path.Combine(folder, $"{name}-{++extra}.png"));
                }
            }

            if (only is null)
            {
                _window.Navigate("ping");
                await Task.Delay(8000); // a few ping samples for the chart
                _window.OpenFirstPingEditor();
                await Task.Delay(500);
                Save(_window.CaptureRoot, Path.Combine(folder, "ping-editor.png"));

                Hub.I.ActivateRule(new Rule { Kind = RuleKind.Network, WaitForStart = false, HoldMinutes = 5, Action = PowerAction.Sleep });
                _window.Navigate("system:rules");
                await Task.Delay(4000);
                Save(_window.CaptureRoot, Path.Combine(folder, "rules-active.png"));

                var countdown = new CountdownWindow(PowerAction.Shutdown, L.T("دستور زمان‌بندی‌شده", "Scheduled command"), 30);
                countdown.Show();
                await Task.Delay(1500);
                Save((FrameworkElement)countdown.Content, Path.Combine(folder, "countdown.png"));
                countdown.Finish(CountdownOutcome.Cancel);

                var due = DateTime.Now.AddMinutes(30);
                var confirm = ConfirmWindow.Preview(L.T("این زمان‌بندی ثبت بشه؟", "Schedule this?"),
                    L.T("سیستم خاموش می‌شه. ۳۰ ثانیه قبلش شمارش معکوس می‌بینی و می‌تونی لغوش کنی. هر وقت خواستی از لیست «بعدی چیه؟» با دکمه‌ی حذف پاکش کن.",
                        "The PC shuts down. You get a 30-second countdown first and can cancel. Delete it any time from the “What's next?” list."),
                    L.T("تایید و ثبت", "Confirm and schedule"), L.T("انصراف", "Cancel"), false,
                    L.T($"شروع: امروز ساعت {Fa.Clock(due)} (۳۰ دقیقه دیگه)", $"Runs: today at {Fa.Clock(due)} (in 30 minutes)"));
                await Task.Delay(600);
                Save(confirm, Path.Combine(folder, "confirm.png"));
                confirm.Close();
                var del = ConfirmWindow.Preview(L.T("این دستور حذف بشه؟", "Delete this command?"),
                    L.T("برای همیشه از لیست پاک می‌شه و اجرا نمی‌شه؛ هیچ شمارش معکوسی هم نمی‌بینی.", "It is removed from the list for good and will not run; no countdown will appear either."),
                    L.T("حذف کن", "Delete"), L.T("انصراف", "Cancel"), true, L.T("خاموش کردن · امروز ساعت ۰۱:۳۰", "Shut down · today at 01:30"));
                await Task.Delay(600);
                Save(del, Path.Combine(folder, "confirm-delete.png"));
                del.Close();
            }

            if (Environment.GetCommandLineArgs().Contains("--overlay-test"))
                await OverlaySelfTest(folder);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(folder, "error.txt"), ex.ToString());
        }
        Hub.I.Shutdown();
        Shutdown();
    }

    /// <summary>Shows the real overlay window for a moment and photographs that part of the screen.</summary>
    private async Task OverlaySelfTest(string folder)
    {
        var hub = Hub.I;
        var report = new System.Text.StringBuilder();
        var previous = hub.Settings.Overlay.Mode;
        hub.Settings.Overlay.Mode = OverlayMode.Always;
        hub.Settings.Overlay.Anchor = "tr";
        var window = new OverlayWindow(hub);
        new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
        hub.Overlay = window;
        window.Evaluate();
        await Task.Delay(3500);
        report.AppendLine($"visible={window.IsVisible} left={window.Left:0} top={window.Top:0} width={window.ActualWidth:0} height={window.ActualHeight:0}");
        report.AppendLine($"topmost={window.Topmost} source={hub.Telemetry.Latest.Gpu.Name} fps={hub.Telemetry.Latest.Fps:0}");

        var dpi = VisualTreeHelper.GetDpi(window);
        var rect = new System.Drawing.Rectangle((int)(window.Left * dpi.DpiScaleX), (int)(window.Top * dpi.DpiScaleY),
            (int)(window.ActualWidth * dpi.DpiScaleX), (int)(window.ActualHeight * dpi.DpiScaleY));
        if (rect.Width > 0 && rect.Height > 0)
        {
            using var bitmap = new System.Drawing.Bitmap(rect.Width, rect.Height);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(rect.Location, System.Drawing.Point.Empty, rect.Size);
            bitmap.Save(Path.Combine(folder, "overlay-on-screen.png"));
        }

        // Click-through: the window must not be what a click at its centre lands on.
        var centre = new System.Drawing.Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
        var hit = WindowFromPoint(centre.X, centre.Y);
        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        report.AppendLine($"click-through={(hit != handle)} (window at centre: 0x{hit:X}, overlay: 0x{handle:X})");

        window.SetMoveMode(true);
        await Task.Delay(300);
        var movingHit = WindowFromPoint(centre.X, centre.Y);
        report.AppendLine($"move-mode catches clicks={(movingHit == handle)}");
        window.SetMoveMode(false);
        hub.Settings.Overlay.Mode = previous;
        window.Close();
        File.WriteAllText(Path.Combine(folder, "overlay-test.txt"), report.ToString());
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(System.Drawing.Point point);

    private static IntPtr WindowFromPoint(int x, int y) => WindowFromPoint(new System.Drawing.Point(x, y));

    private static void Save(FrameworkElement element, string file)
    {
        element.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
        var dpi = VisualTreeHelper.GetDpi(element);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        // Rendering a right-to-left element off-screen skips the mirroring its window applies, so undo it here.
        BitmapSource image = element.FlowDirection == FlowDirection.RightToLeft
            ? new TransformedBitmap(bitmap, new ScaleTransform(-1, 1))
            : bitmap;
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(file);
        encoder.Save(stream);
    }
}
