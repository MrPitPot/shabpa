using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shabpa.Core;
using Shabpa.Kit;
using Switch = Shabpa.Kit.Switch;

namespace Shabpa.Pages;

/// <summary>«بهینه‌سازی بازی»: a short list of documented Windows settings that help games, three tabs of switches. Each has a
/// small "i" with a plain explanation, asks before it changes anything, keeps the person's own value and puts exactly that back
/// when switched off. Nothing here disables a program, a service or a file.</summary>
public sealed class GameTunePage : PageBase
{
    private readonly GameTuner _tuner;
    private readonly StackPanel _rows = new();
    private readonly WrapPanel _tabs = new();
    private readonly TextBlock _status = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly Border _adminBanner = new();
    private readonly Dictionary<string, bool> _state = [];
    private readonly string _cpuName = ReadCpuName();
    private string _tab = GameTweaks.TabCpu;
    private bool _busy;

    public override string PageTitle => L.T("بهینه‌سازی بازی", "Game tuning");
    public override string PageSubtitle => L.T("چند تنظیم شناخته‌شده‌ی ویندوز که به بازی کمک می‌کنن؛ هر کدوم رو خودت روشن می‌کنی و هر وقت خواستی دقیقاً به حالت قبل برمی‌گرده.",
        "A few well-known Windows settings that help games; you switch each one on yourself and it goes back exactly as it was whenever you like.");

    public GameTunePage()
    {
        _tuner = new GameTuner(new WindowsTuneRegistry(), new WindowsPcieLink(), Hub.State.TuneBackup, () => Hub.Store.Save());

        var root = new StackPanel();
        var (intro, body) = Ui2.Card(L.T("پیش از شروع", "Before you start"),
            L.T("هیچ کدوم برنامه، سرویس یا فایلی رو حذف یا غیرفعال نمی‌کنن. قبل از هر تغییر، مقدار خودت ذخیره می‌شه و با خاموش‌کردن همون برمی‌گرده. بعضی‌ها بعد از ری‌استارت اثر می‌کنن و بعضی‌ها دسترسی ادمین می‌خوان.",
                "None of them removes or disables a program, a service or a file. Your own value is kept before every change and put back when you switch it off. Some need a restart and some need administrator rights."));
        _adminBanner.CornerRadius = new CornerRadius(12);
        _adminBanner.Padding = new Thickness(14, 10, 14, 10);
        _adminBanner.Margin = new Thickness(0, 12, 0, 0);
        _adminBanner.Background = Res("GoldSoft");
        _adminBanner.Child = new TextBlock
        {
            Text = L.T("شب‌پا الان با دسترسی عادی اجرا شده؛ تنظیم‌هایی که ادمین می‌خوان قفل‌ان. برای باز شدنشون شب‌پا رو با دسترسی ادمین اجرا کن (از تنظیمات).",
                "Shabpa is running with normal rights; the settings that need administrator rights are locked. Run Shabpa as administrator (in Settings) to unlock them."),
            Style = St("Body"), TextWrapping = TextWrapping.Wrap, Foreground = Res("Gold"),
        };
        _adminBanner.Visibility = Hub.Elevated ? Visibility.Collapsed : Visibility.Visible;
        body.Children.Add(_adminBanner);
        var restore = new Button { Style = St("Btn"), Content = L.T("برگردوندن همه به حالت قبل", "Put everything back"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 14, 0, 0) };
        restore.Click += (_, _) => RestoreAll();
        body.Children.Add(restore);
        body.Children.Add(_status);
        root.Children.Add(intro);

        var (listCard, list) = Ui2.Card(L.T("تنظیم‌ها", "Settings"));
        Chips(_tabs, Tabs().Select(t => (t.key, t.name)), _tab, key => { _tab = key; Draw(); });
        _tabs.Margin = new Thickness(0, 12, 0, 6);
        list.Children.Add(_tabs);
        list.Children.Add(_rows);
        root.Children.Add(listCard);
        Content = root;
    }

    private static IEnumerable<(string key, string name)> Tabs() =>
    [
        (GameTweaks.TabCpu, L.T("پردازنده و رم", "Processor & memory")),
        (GameTweaks.TabSystem, L.T("ویندوز و سیستم", "Windows & system")),
        (GameTweaks.TabGraphics, L.T("گرافیک", "Graphics")),
    ];

    private static string ReadCpuName()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return key?.GetValue("ProcessorNameString") as string ?? "";
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return ""; }
    }

    public override void Refresh()
    {
        if (_busy) return;
        _busy = true;
        _ = Task.Run(() =>
        {
            var now = new Dictionary<string, bool>();
            foreach (var t in GameTweaks.All())
                now[t.Id] = t.Kind == TuneKind.CpuPriority ? Hub.Settings.Comfort.GameModePriority : _tuner.IsOn(t);
            Dispatcher.BeginInvoke(() =>
            {
                _busy = false;
                _state.Clear();
                foreach (var (k, v) in now) _state[k] = v;
                Draw();
            });
        });
    }

    private void Draw()
    {
        _rows.Children.Clear();
        var first = true;
        foreach (var tweak in GameTweaks.All().Where(t => t.Tab == _tab && t.Kind != TuneKind.X3dLock)) // the X3D pinning is not built yet, so its dead switch is not shown
        {
            _rows.Children.Add(Row(tweak, first));
            first = false;
        }
        if (_tab == GameTweaks.TabSystem)
            _rows.Children.Add(new TextBlock
            {
                Text = L.T("خاموش‌کردن سرویس‌ها، کارهای زمان‌بندی‌شده و برنامه‌های استارتاپ ویندوز عمداً اینجا نیست؛ استارتاپ‌ها رو می‌تونی توی «سلامت و امنیت ← امنیت و استارتاپ» ببینی و خودت تغییر بدی.",
                    "Turning off Windows services and scheduled tasks is left out on purpose; you can see and change your startup programs in Health & security → Security & startup."),
                Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0),
            });
    }

    private FrameworkElement Row(Tweak tweak, bool first)
    {
        var on = _state.GetValueOrDefault(tweak.Id);
        var x3d = tweak.Kind == TuneKind.X3dLock;
        var locked = (tweak.NeedsAdmin && !Hub.Elevated) || x3d;
        var toggle = new Switch { IsChecked = on, IsEnabled = !locked, VerticalAlignment = VerticalAlignment.Center };
        toggle.Click += (_, _) => Flip(tweak, toggle);

        var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock { Text = tweak.Name, FontWeight = FontWeights.SemiBold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Opacity = locked ? 0.55 : 1 });
        title.Children.Add(InfoIcon(tweak.Tip));

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 14, 8) };
        texts.Children.Add(title);
        var note = Note(tweak, on, x3d);
        if (note.Length > 0) texts.Children.Add(new TextBlock { Text = note, Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });

        var grid = new Grid { MinHeight = 54 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(texts);
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(toggle);
        if (!first)
            grid.Children.Add(new Border { Height = 1, Background = Res("Line"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 0, 0) });
        return grid;
    }

    private string Note(Tweak tweak, bool on, bool x3d)
    {
        if (x3d) return GamePriority.IsDualX3d(_cpuName) ? L.T("پردازنده‌ی X3D پیدا شد؛ این قابلیت در نسخه‌ی بعد می‌آد.", "An X3D processor was found; this arrives in a later version.") : L.T("پردازنده‌ی X3D دوتکه روی این کامپیوتر پیدا نشد.", "No dual-chiplet X3D processor found on this PC.");
        if (tweak.NeedsAdmin && !Hub.Elevated) return L.T("نیاز به دسترسی ادمین", "Needs administrator rights");
        if (tweak.NeedsRestart && on) return L.T("روشنه؛ بعد از ری‌استارت کامل اعمال می‌شه", "On; fully applied after a restart");
        if (tweak.NeedsRestart) return L.T("بعد از ری‌استارت اثر می‌کنه", "Takes effect after a restart");
        if (on && _tuner.HasBackup(tweak)) return L.T("روشن؛ مقدار قبلی‌ات ذخیره شده", "On; your previous value is saved");
        return "";
    }

    private static FrameworkElement InfoIcon(string tip)
    {
        var mark = new Border
        {
            Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Background = Res("Line"), Cursor = System.Windows.Input.Cursors.Help, FlowDirection = FlowDirection.LeftToRight,
            Child = new TextBlock { Text = "i", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Res("Muted"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var text = new TextBlock { Text = tip, TextWrapping = TextWrapping.Wrap, MaxWidth = 340, LineHeight = 20 };
        mark.ToolTip = new ToolTip { Content = text, Padding = new Thickness(12, 9, 12, 9) };
        ToolTipService.SetInitialShowDelay(mark, 100);
        ToolTipService.SetShowDuration(mark, 40000);
        return mark;
    }

    private void Flip(Tweak tweak, Switch toggle)
    {
        var want = toggle.IsChecked == true;
        var verb = want ? L.T("روشن بشه؟", "Turn on?") : L.T("خاموش و به حالت قبل برگرده؟", "Turn off and put it back?");
        var detail = tweak.NeedsRestart ? L.T("اثرش بعد از ری‌استارت ویندوز دیده می‌شه.", "It shows after Windows restarts.") : null;
        if (!Ask(tweak.Name + " — " + verb, tweak.Tip, want ? L.T("روشن کن", "Turn on") : L.T("خاموش کن", "Turn off"), detail: detail))
        {
            toggle.IsChecked = !want;
            return;
        }
        if (tweak.Kind == TuneKind.CpuPriority)
        {
            Hub.Settings.Comfort.GameModePriority = want;
            Hub.Store.Save();
            Finish(tweak, want, TuneOutcome.Done);
            return;
        }
        toggle.IsEnabled = false;
        _ = Task.Run(() =>
        {
            var outcome = want ? _tuner.Apply(tweak) : _tuner.Revert(tweak);
            Dispatcher.BeginInvoke(() =>
            {
                toggle.IsEnabled = true;
                Finish(tweak, want, outcome);
            });
        });
    }

    private void Finish(Tweak tweak, bool want, TuneOutcome outcome)
    {
        switch (outcome)
        {
            case TuneOutcome.Done:
                _state[tweak.Id] = want;
                Mo.Say(_status, want ? L.T($"«{tweak.Name}» روشن شد.", $"“{tweak.Name}” is on.") : L.T($"«{tweak.Name}» برگشت به حالت قبل.", $"“{tweak.Name}” is back as it was."));
                break;
            case TuneOutcome.NeedsAdmin:
                Mo.Say(_status, L.T("ویندوز اجازه نداد؛ شب‌پا رو با دسترسی ادمین اجرا کن.", "Windows did not allow it; run Shabpa as administrator."));
                break;
            default:
                Mo.Say(_status, L.T("انجام نشد؛ جزئیات توی لاگ شب‌پا ثبت شد و چیزی عوض نشده.", "It did not work; the details are in Shabpa's log and nothing was changed."));
                break;
        }
        Draw();
    }

    private void RestoreAll()
    {
        var active = GameTweaks.All().Where(t => _state.GetValueOrDefault(t.Id)).ToList();
        if (active.Count == 0)
        {
            Mo.Say(_status, L.T("چیزی روشن نیست که برگردونم.", "Nothing is on, so there is nothing to put back."));
            return;
        }
        if (!Ask(L.T("همه به حالت قبل برگردن؟", "Put everything back?"), L.T($"{Fa.Digits(active.Count.ToString())} تنظیم خاموش می‌شن و مقدار خودت دوباره می‌شینه.", $"{active.Count} setting(s) are switched off and your own values return."), L.T("برگردون", "Put back"))) return;
        _ = Task.Run(() =>
        {
            var failed = 0;
            foreach (var t in active)
            {
                if (t.Kind == TuneKind.CpuPriority) { Hub.Settings.Comfort.GameModePriority = false; continue; }
                if (_tuner.Revert(t) != TuneOutcome.Done) failed++;
            }
            Hub.Store.Save();
            Dispatcher.BeginInvoke(() =>
            {
                Mo.Say(_status, failed == 0 ? L.T("همه برگشتن.", "Everything is back.") : L.T($"{Fa.Digits(failed.ToString())} مورد برنگشت (ادمین لازمه یا لاگ رو ببین).", $"{failed} item(s) could not be put back (needs administrator rights, see the log)."));
                Refresh();
            });
        });
    }
}
