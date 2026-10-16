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

        // Test aid: lists program updates through winget and graphics drivers, read-only, and writes a summary.
        if (Value(e.Args, "--updates-test") is { } updReport)
        {
            var log = new System.Text.StringBuilder();
            log.AppendLine($"winget available: {Core.Winget.IsAvailable()}");
            try
            {
                var apps = Task.Run(() => Core.Winget.ListUpgradesAsync()).GetAwaiter().GetResult();
                log.AppendLine($"updates found: {apps.Count}");
                foreach (var a in apps.Take(25)) log.AppendLine($"  {a.Name} | {a.Id} | {a.Installed} -> {a.Available} | {a.Source}");
            }
            catch (Exception ex) { log.AppendLine("winget list failed: " + ex.GetType().Name + " " + ex.Message); }
            foreach (var g in Core.Drivers.Gpus()) log.AppendLine($"gpu: {g.Name} | {g.Vendor} | {g.Version} -> {g.FriendlyVersion} | {g.Date:yyyy-MM-dd} | {g.DownloadPage}");
            try
            {
                var offers = Task.Run(() => Core.Drivers.WindowsUpdateOffersAsync()).GetAwaiter().GetResult();
                log.AppendLine($"driver offers: {offers.Count}");
                foreach (var o in offers.Take(10)) log.AppendLine($"  {o.Title} | {o.Manufacturer} | {o.Class}");
            }
            catch (Exception ex) { log.AppendLine("driver search failed: " + ex.GetType().Name + " " + ex.Message); }
            File.WriteAllText(updReport, log.ToString());
            Shutdown();
            return;
        }

        // Test aid: reads the default microphone and listens for 2 seconds; only loudness numbers are written.
        if (Value(e.Args, "--mic-test") is { } micReport)
        {
            var log = new System.Text.StringBuilder();
            log.AppendLine($"device: {Core.Microphone.DeviceName()}");
            log.AppendLine($"level/muted: {Core.Microphone.Read()}");
            log.AppendLine($"desktop apps allowed: {Core.Microphone.DesktopAppsAllowed()}");
            log.AppendLine($"recent apps: {string.Join(", ", Core.Microphone.RecentApps().Take(5).Select(a => a.Name))}");
            var values = new List<double>();
            var error = Core.Microphone.Listen(2000, (rms, peak) => values.Add(rms));
            log.AppendLine($"listen error: {error ?? "none"}; blocks: {values.Count}; rms min {(values.Count > 0 ? values.Min() : 0):0.0} max {(values.Count > 0 ? values.Max() : 0):0.0} avg {(values.Count > 0 ? values.Average() : 0):0.0} dB");
            log.AppendLine($"verdict for the average: {(values.Count > 0 ? Core.Microphone.Judge(values.Average()) : Core.MicVerdict.Silent)}");
            File.WriteAllText(micReport, log.ToString());
            Shutdown();
            return;
        }

        // Test aid: switches game mode on and off against the real power plan and writes what it saw.
        if (Value(e.Args, "--gamemode-test") is { } gameReport)
        {
            var log = new System.Text.StringBuilder();
            var gHub = new Hub(new Store { ReadOnly = true }, true, false);
            gHub.Settings.Comfort.GameModeOn = true;
            gHub.Settings.Comfort.GameModeRam = false;
            var before = Core.PowerPlan.Active();
            log.AppendLine($"installed plans: {string.Join(", ", Core.PowerPlan.Installed())}");
            log.AppendLine($"plan before: {before}");
            gHub.GameModeStep(true);
            log.AppendLine($"plan during game mode: {Core.PowerPlan.Active()} (active flag {gHub.GameModeActive}, remembered {gHub.Settings.Comfort.PendingPowerPlan})");
            gHub.GameModeStep(false);
            log.AppendLine($"plan after: {Core.PowerPlan.Active()} (active flag {gHub.GameModeActive}, remembered '{gHub.Settings.Comfort.PendingPowerPlan}')");
            log.AppendLine($"restored exactly: {Core.PowerPlan.Active() == before}");
            File.WriteAllText(gameReport, log.ToString());
            Shutdown();
            return;
        }

        // Test aid: sends the app the kinds of commands the bot server can send and records the answers.
        if (Value(e.Args, "--remote-test") is { } remoteReport)
        {
            var log = new System.Text.StringBuilder();
            var rHub = new Hub(new Store { ReadOnly = true }, true, false);
            rHub.Tasks.Add("secret doctor appointment", DateTime.Now.AddHours(1));
            rHub.State.Profiles.Add(new Core.WorkProfile { Name = "Sleepy", Actions = [new Core.ProfileAction { Kind = Core.ActionKinds.PowerLater, Value = 10, Value2 = 0 }] });
            rHub.State.Profiles.Add(new Core.WorkProfile { Name = "Calm", Actions = [new Core.ProfileAction { Kind = Core.ActionKinds.Breaks, Value = 0 }] });
            rHub.Settings.ProfilesSeeded = true;
            System.Text.Json.JsonElement Args(string json) => System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();
            void Try(string type, string json)
            {
                var r = rHub.ExecuteRemote(type, Args(json));
                log.AppendLine($"{type} {json} -> ok={r.Ok} | {r.En.Replace('\n', ' ')}");
            }
            Try("tasks", "{}");
            rHub.Settings.TasksToBot = true;
            Try("tasks", "{}");
            rHub.Settings.TasksToBot = false;
            Try("profile", "{\"name\":\"Sleepy\"}");
            Try("profile", "{\"name\":\"Calm\"}");
            Try("profile", "{\"name\":\"nope\"}");
            Try("power", "{\"action\":\"format-disk\",\"delaySeconds\":0}");
            Try("volume", "{}");
            Try("run-program", "{\"path\":\"calc.exe\"}");
            Try("focus", "{\"action\":\"stop\"}");
            File.WriteAllText(remoteReport, log.ToString());
            Shutdown();
            return;
        }

        // Test aid: runs a profile against the real volume control, then undoes it, and writes what happened.
        if (Value(e.Args, "--profile-test") is { } profileReport)
        {
            var log = new System.Text.StringBuilder();
            var testHub = new Hub(new Store { ReadOnly = true }, true, false);
            var before = Core.MasterVolume.Get();
            log.AppendLine($"volume before: {before:0.00}");
            var profile = new Core.WorkProfile
            {
                Name = "test",
                Actions =
                [
                    new Core.ProfileAction { Kind = Core.ActionKinds.Volume, Value = 17 },
                    new Core.ProfileAction { Kind = Core.ActionKinds.Breaks, Value = 1 },
                    new Core.ProfileAction { Kind = Core.ActionKinds.Dim, Value = 50, Value2 = 1 },
                    new Core.ProfileAction { Kind = Core.ActionKinds.Notify, Text = "hi" },
                ],
            };
            testHub.Settings.Comfort.BreakOn = false;
            var outcome = testHub.RunProfile(profile, "manual");
            log.AppendLine($"ok={outcome.Ok} steps={string.Join(" | ", outcome.Steps)}");
            log.AppendLine($"volume after run: {Core.MasterVolume.Get():0.00} (expected 0.17), breaks on: {testHub.Settings.Comfort.BreakOn}, dim on: {testHub.Settings.Comfort.DimOn} level {testHub.Settings.Comfort.DimLevel:0.00}");
            log.AppendLine($"undo available: {testHub.CanUndoProfile}");
            testHub.UndoProfile();
            log.AppendLine($"volume after undo: {Core.MasterVolume.Get():0.00} (expected {before:0.00}), breaks on: {testHub.Settings.Comfort.BreakOn}, dim on: {testHub.Settings.Comfort.DimOn}");
            log.AppendLine($"run log entries: {testHub.State.RunLog.Count}");
            File.WriteAllText(profileReport, log.ToString());
            Shutdown();
            return;
        }

        // Test aid: builds the health report for this PC (redacted) so it can be read.
        if (Value(e.Args, "--health-test") is { } healthReport)
        {
            var reg = new Core.WindowsRegistryProbe();
            var health = new List<Core.HealthCheck>();
            health.AddRange(Core.Health.DisksNow());
            health.Add(Core.Health.PendingReboot(reg));
            health.Add(Core.Health.WindowsUpdate(reg, DateTime.UtcNow));
            health.AddRange(Core.Health.Services(reg, Core.Health.ServiceStateNow));
            var text = Core.HealthReport.Build(health, Core.Health.RecentErrors(), Core.Health.Security(reg, Core.Health.ServiceStateNow, false),
                Core.Health.Startup(reg, Core.Health.StartupFolders()), Core.Updater.Current.ToString(3), Environment.OSVersion.VersionString, DateTime.Now);
            File.WriteAllText(healthReport, Core.Redactor.Redact(text));
            Shutdown();
            return;
        }

        // Test aid: links a throwaway device to the real bot server, waits for its code, then removes it again.
        if (Value(e.Args, "--bot-test") is { } botReport)
        {
            _ = RunBotTest(botReport);
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
        if (args.Contains("--software-render")) settings.LowMemory = true; // test aid (screenshot runs never save)
        // "Use less RAM": no graphics-card device at all (that is where the memory goes); the small overlay and dim
        // windows are cheap to draw either way.
        if (settings.LowMemory) System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        L.En = (Value(e.Args, "--lang") ?? settings.Language) == "en";
        Resources.MergedDictionaries.Add(Ui.Resources(Palette.Find(Value(e.Args, "--theme") ?? settings.Theme)));
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Shabpa;component/Theme.xaml", UriKind.Relative) });

        var hub = new Hub(store, dryRun, demo);
        if (demo) hub.FillDemo();
        hub.Start();

        _window = new MainWindow();
        // test aid: --size 960x620 shows the window at its smallest, to check nothing is cut off
        if (Value(e.Args, "--size") is { } size && size.Split('x') is [var w, var h] && double.TryParse(w, out var width) && double.TryParse(h, out var height))
        {
            _window.Width = width;
            _window.Height = height;
        }
        if (Value(e.Args, "--page") is { } page) _window.Navigate(page);
        if (shots is not null)
        {
            _window.Show();
            // --interactive: just show the window (real sensors, nothing saved) so it can be driven by hand or by a test.
            if (Value(e.Args, "--toast") is { } toast) // test aid: shows an in-app notice for the screenshot
                Dispatcher.BeginInvoke(() => _window.ShowToast(toast.Split('|')[0], toast.Split('|').ElementAtOrDefault(1) ?? ""), DispatcherPriority.ApplicationIdle);
            if (Value(e.Args, "--motion-test") is { } motionReport) _ = MotionProbe(shots, motionReport);
            else if (!args.Contains("--interactive")) _ = CaptureAll(shots, Value(e.Args, "--only"));
            if (Value(e.Args, "--mem-report") is { } memReport)
            {
                // Test aid: where the memory goes, 20 s after start (managed heap vs the rest, and what is loaded).
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    var info = GC.GetGCMemoryInfo();
                    using var self = System.Diagnostics.Process.GetCurrentProcess();
                    var asm = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name ?? "").OrderBy(n => n).ToList();
                    File.WriteAllText(memReport,
                        $"heap {info.HeapSizeBytes >> 20} MB, committed {info.TotalCommittedBytes >> 20} MB, fragmented {info.FragmentedBytes >> 20} MB\n" +
                        $"private bytes {self.PrivateMemorySize64 >> 20} MB, working set {self.WorkingSet64 >> 20} MB, threads {self.Threads.Count}\n" +
                        $"modules {self.Modules.Count}, assemblies {asm.Count}:\n{string.Join("\n", asm)}\n\nnative modules:\n" +
                        string.Join("\n", self.Modules.Cast<System.Diagnostics.ProcessModule>().OrderByDescending(m => m.ModuleMemorySize).Take(25).Select(m => $"{m.ModuleMemorySize >> 10,7} KB {m.ModuleName}")));
                };
                timer.Start();
            }
            if (Value(e.Args, "--hide-at") is { } hideAt && int.TryParse(hideAt, out var hideSeconds))
            {
                // Test aid: hides the window after a while, as closing it to the tray does (memory measurements).
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(hideSeconds) };
                timer.Tick += (_, _) => { timer.Stop(); _window.Hide(); };
                timer.Start();
            }
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
        hub.Notified += (title, text) => { if (_window?.ShowToast(title, text) != true) _tray?.Balloon(title, text); };
        hub.Changed += () => _tray.Refresh();
        _tray.Refresh();

        // Just updated: always come to the front and say so, even if the app normally starts hidden in the tray.
        var updated = args.Contains("--updated");
        if (!updated && !args.Contains("--restart") && (args.Contains("--minimized") || hub.Settings.StartMinimized)) return;
        ShowWindow();
        if (updated)
        {
            // started by the installer, which has no window: without this Windows may only flash the taskbar button
            _window.Topmost = true;
            _window.Topmost = false;
            var version = Updater.Current.ToString(3);
            hub.Notify(L.T("شب‌پا به‌روز شد", "Shabpa is up to date"),
                L.T($"نسخه‌ی {Fa.Digits(version)} نصب شد و همه‌ی تنظیماتت سر جاشه.", $"Version {version} is installed and all your settings are kept."));
        }
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
        if (_window is { IsVisible: true }) _window.SavePlacement();
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
        steps.Enqueue(() => { var built = dim.Builds; dim.Apply(); dim.Apply(); dim.Apply(); log.AppendLine($"flicker check: builds {built} -> {dim.Builds} after 3 more Apply calls (must be equal)"); });
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

    private async Task RunBotTest(string report)
    {
        var log = new System.Text.StringBuilder();
        var settings = new Settings();
        using var link = new BotLink(() => settings, () => { }, new BotHost { Execute = (_, _) => RemoteReply.Fail("", "") });
        link.Changed += () => { lock (log) log.AppendLine($"{DateTime.Now:HH:mm:ss.fff} state={link.State} code={(link.Code.Length > 0 ? "yes" : "none")} chats={link.Chats} {link.Detail}"); };
        link.Enable();
        for (var i = 0; i < 100 && link.State is BotState.Connecting; i++) await Task.Delay(100);
        lock (log)
            log.AppendLine($"code shape ok: {System.Text.RegularExpressions.Regex.IsMatch(link.Code, "^[A-Z2-9]{4}-[A-Z2-9]{4}$")}, " +
                           $"device id set: {settings.BotDeviceId.Length > 0}, secret stored encrypted: {settings.BotSecretProtected.Length > 0}");
        var first = link.Code;
        link.NewCode();
        for (var i = 0; i < 100 && (link.Code.Length == 0 || link.Code == first); i++) await Task.Delay(100);
        lock (log) log.AppendLine($"new code issued: {link.Code.Length > 0 && link.Code != first}");
        await link.RemoveAsync();
        lock (log) log.AppendLine($"removed: state={link.State} device id cleared: {settings.BotDeviceId.Length == 0}");
        File.WriteAllText(report, log.ToString());
        Shutdown();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        Hub.I?.Store.Save();
        base.OnSessionEnding(e);
    }

    // ---- self-screenshots: lets the UI be reviewed without touching the real desktop ---------

    /// <summary>Test aid: opens every tab and samples how far its first block has faded in, 30 ms, 200 ms and 1.5 s later.</summary>
    private async Task MotionProbe(string folder, string report)
    {
        var log = new System.Text.StringBuilder();
        try
        {
            Directory.CreateDirectory(folder);
            await Task.Delay(2500);
            Hub.I.Settings.Animations = true;
            foreach (var key in new[] { "home", "system", "ping", "gaming", "weather", "dns", "download", "comfort", "support", "update", "settings", "home" })
            {
                _window!.Navigate(key);
                await Task.Delay(30);
                var a = _window.FirstBlockOpacity();
                await Task.Delay(170);
                var b = _window.FirstBlockOpacity();
                if (key is "home" or "support" or "weather" or "settings" or "download") Save(_window.CaptureRoot, Path.Combine(folder, $"motion-{key}.png"));
                await Task.Delay(1300);
                var c = _window.FirstBlockOpacity();
                var transform = _window.FirstBlockHasTransform();
                log.AppendLine($"{key}: opacity {a:0.00} -> {b:0.00} -> {c:0.00}, transform left: {transform}");
            }
        }
        catch (Exception ex)
        {
            log.AppendLine("error: " + ex);
        }
        File.WriteAllText(report, log.ToString());
        Shutdown();
    }

    private async Task CaptureAll(string folder, string? only)
    {
        Hub.I.Settings.Animations = false; // screenshots show the settled page
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
