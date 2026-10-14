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

        // Used by the installer: switch start-with-Windows on or off for this exe, then exit.
        if (Value(e.Args, "--autostart") is { } autostart)
        {
            Log.File = Path.Combine(new Store().Folder, "shabpa.log");
            var error = Autostart.Set(autostart == "on", Hub.ExePath, Elevation.IsElevated);
            if (error is not null) Log.Write($"autostart: {error}");
            Shutdown(error is null ? 0 : 1);
            return;
        }

        var shots = Value(e.Args, "--screenshots");
        var demo = args.Contains("--demo") || shots is not null;
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
        else store.Load();

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
            _ = CaptureAll(shots, Value(e.Args, "--only"));
            return;
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
                await Task.Delay(900);
                var name = page.Replace(':', '-');
                Save(_window.CaptureRoot, Path.Combine(folder, $"{name}.png"));
                var extra = 0;
                while (only is null && _window.ScrollPage())
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
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(folder, "error.txt"), ex.ToString());
        }
        Hub.I.Shutdown();
        Shutdown();
    }

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
