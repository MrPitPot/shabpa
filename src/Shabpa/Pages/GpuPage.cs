using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Shabpa.Core;

namespace Shabpa.Pages;

/// <summary>Graphics card: live readings, and (on AMD cards with Overdrive 8) undervolt, VRAM clock, power limit and a smart fan.</summary>
public sealed class GpuPage : PageBase
{
    private const int TrialSeconds = 20;

    private readonly StackPanel _root = new();
    private readonly TextBlock _name = new();
    private readonly Dictionary<string, TextBlock> _tiles = [];
    private readonly Canvas _chart = new() { Height = 90, ClipToBounds = true, FlowDirection = FlowDirection.LeftToRight };
    private readonly Polyline _tempLine = new() { Stroke = Brushes.Transparent, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
    private readonly Polyline _loadLine = new() { Stroke = Brushes.Transparent, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
    private readonly StackPanel _tuning = new();
    private readonly DispatcherTimer _trial = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _acquired;
    private bool _built;
    private GpuTuneProfile _draft = new();
    private Dictionary<int, int>? _before;
    private GpuTuneProfile? _previous;
    private int _trialLeft;
    private Border? _trialBanner;
    private TextBlock? _trialText;
    private Od8State? _state;
    private bool _loading;
    private bool _advancedOn;

    // controls of the tuning card
    private Slider _core = null!, _voltage = null!, _power = null!, _vram = null!;
    private CheckBox _fast = null!, _zero = null!, _advanced = null!, _auto = null!;
    private readonly WrapPanel _fanModeChips = new(), _fanPresetChips = new();
    private readonly StackPanel _customFan = new(), _smartFan = new();
    private readonly List<Slider> _curveTemp = [], _curveSpeed = [];
    private Slider _target = null!;
    private readonly Canvas _curveChart = new() { Width = 300, Height = 120, FlowDirection = FlowDirection.LeftToRight };
    private TextBlock _status = null!;
    private TextBlock _presetLabel = new();

    public override string PageTitle => L.T("بازی", "Gaming");
    public override string PageSubtitle => L.T("دما و کلاک کارت گرافیک، آندرولت، اورکلاک حافظه و فن هوشمند.", "Graphics card readings, undervolt, memory clock and a smart fan.");

    private GpuTuner Tuner => Hub.Tuner;
    private GpuTuneProfile Profile => Hub.Settings.GpuTune;

    public GpuPage()
    {
        _name.Style = St("H2");
        var (liveCard, live) = Ui2.Card(L.T("کارت گرافیک", "Graphics card"));
        liveCard.Margin = new Thickness(0);
        live.Children.Clear();
        live.Children.Add(_name);

        var tiles = new UniformGrid { Columns = 3, Margin = new Thickness(0, 12, 0, 0) };
        void Tile(string key, string label)
        {
            var value = new TextBlock
            {
                FontFamily = (FontFamily)Application.Current.Resources["Num"], FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Res("Text"),
                FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left, Text = "—", Margin = new Thickness(0, 3, 0, 0),
            };
            var cell = new Border { Background = Res("Panel2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 8, 8) };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = label, Style = St("Label") });
            stack.Children.Add(value);
            cell.Child = stack;
            tiles.Children.Add(cell);
            _tiles[key] = value;
        }
        Tile("temp", L.T("دما", "Temperature"));
        Tile("hotspot", L.T("هات‌اسپات", "Hotspot"));
        Tile("memtemp", L.T("دمای حافظه", "Memory temp"));
        Tile("core", L.T("کلاک گرافیک", "Core clock"));
        Tile("mem", L.T("کلاک حافظه", "Memory clock"));
        Tile("power", L.T("توان", "Power"));
        Tile("fan", L.T("فن", "Fan"));
        Tile("load", L.T("بار گرافیک", "Load"));
        Tile("vram", L.T("حافظه‌ی استفاده‌شده", "Memory in use"));
        live.Children.Add(tiles);

        _chart.Background = Res("Panel2");
        var chartFrame = new Border { CornerRadius = new CornerRadius(12), ClipToBounds = true, Child = _chart, Margin = new Thickness(0, 4, 0, 0) };
        _chart.SizeChanged += (_, _) => DrawChart();
        _chart.Children.Add(_tempLine);
        _chart.Children.Add(_loadLine);
        live.Children.Add(chartFrame);
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        legend.Children.Add(Dot("Gold", L.T("دما (°C)", "Temp (°C)")));
        legend.Children.Add(Dot("Accent", L.T("بار (٪)", "Load (%)")));
        live.Children.Add(legend);
        _root.Children.Add(liveCard);

        _root.Children.Add(_tuning);
        Content = _root;

        _trial.Tick += (_, _) => TrialTick();
        Hub.Telemetry.Updated += sample => Dispatcher.BeginInvoke(DispatcherPriority.Background, () => Show(sample));
        IsVisibleChanged += (_, _) => Visibility_();
        Unloaded += (_, _) => Release();
    }

    private static UIElement Dot(string tint, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 0) };
        row.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(3), Background = Res(tint), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        row.Children.Add(new TextBlock { Text = text, Style = St("Label"), VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private void Visibility_()
    {
        if (IsVisible)
        {
            if (!_acquired)
            {
                _acquired = true;
                Hub.Telemetry.Acquire();
            }
            Refresh();
        }
        else Release();
    }

    private void Release()
    {
        if (!_acquired) return;
        _acquired = false;
        Hub.Telemetry.Release();
    }

    // ---- live readings ---------------------------------------------------------------------------

    private void Show(TelemetrySample s)
    {
        if (!IsVisible) return;
        var g = s.Gpu;
        string F(double? v, string unit, int decimals = 0) => v is { } x ? x.ToString("F" + decimals, System.Globalization.CultureInfo.InvariantCulture) + unit : "—";
        _name.Text = g.Name.Length > 0 ? g.Name : L.T("کارت گرافیک پیدا نشد", "No graphics card found");
        _tiles["temp"].Text = F(g.TempC, " °C");
        _tiles["hotspot"].Text = F(g.HotspotC, " °C");
        _tiles["memtemp"].Text = F(g.MemTempC, " °C");
        _tiles["core"].Text = F(g.CoreMHz, " MHz");
        _tiles["mem"].Text = F(g.MemMHz, " MHz");
        _tiles["power"].Text = F(g.PowerW, " W");
        _tiles["fan"].Text = g.FanPercent is { } fan && g.FanRpm is { } rpm && rpm > 0 ? $"{fan:0}% · {rpm:0} rpm" : F(g.FanPercent, " %");
        _tiles["load"].Text = F(g.LoadPercent, " %");
        _tiles["vram"].Text = g.VramUsedMb is { } used
            ? $"{used / 1024:0.0} / {(g.VramTotalMb ?? 0) / 1024:0} GB"
            : "—";
        DrawChart();
    }

    private void DrawChart()
    {
        var history = Hub.Telemetry.History();
        _tempLine.Stroke = Res("Gold");
        _loadLine.Stroke = Res("Accent");
        var width = _chart.ActualWidth;
        var height = _chart.ActualHeight;
        if (history.Count < 2 || width <= 0 || height <= 0) return;
        PointCollection Line(Func<TelemetrySample, double?> pick, double top)
        {
            var points = new PointCollection(history.Count);
            for (var i = 0; i < history.Count; i++)
            {
                var v = Math.Clamp(pick(history[i]) ?? 0, 0, top);
                points.Add(new Point(width * i / Math.Max(1, history.Count - 1), height - 4 - v / top * (height - 8)));
            }
            return points;
        }
        _tempLine.Points = Line(h => h.Gpu.HotspotC ?? h.Gpu.TempC, 110);
        _loadLine.Points = Line(h => h.Gpu.LoadPercent, 100);
    }

    // ---- tuning ----------------------------------------------------------------------------------

    public override void Refresh()
    {
        if (Hub.Demo) Show(OverlayMetrics.Demo(DateTime.Now));
        if (!_built || _state is null) TryBuild();
        else SyncControls();
    }

    public override void OnSecond()
    {
        if (!_built) TryBuild();
    }

    private void TryBuild()
    {
        var gpu = Hub.Telemetry.Gpu as AmdGpu;
        _tuning.Children.Clear();
        _built = false;

        if (Hub.Telemetry.Gpu is null)
        {
            // The provider opens in the background a moment after start.
            AddNote(L.T("در حال خوندن اطلاعات کارت گرافیک…", "Reading the graphics card…"));
            return;
        }
        if (gpu is null)
        {
            AddNote(L.T($"{Hub.Telemetry.Gpu.Vendor}: دما و کلاک خونده می‌شه؛ تنظیم آندرولت و فن فعلاً فقط برای کارت‌های AMD موجوده.",
                $"{Hub.Telemetry.Gpu.Vendor}: temperatures and clocks are shown; undervolt and fan control are available for AMD cards only for now."));
            _built = true;
            return;
        }
        _state = gpu.ReadOd8();
        if (_state is null || !gpu.CanTune)
        {
            AddNote(L.T("درایور این کارت تنظیمات پیشرفته (Overdrive 8) رو در اختیار نمی‌ذاره.", "This card's driver does not expose advanced tuning (Overdrive 8)."));
            _built = true;
            return;
        }
        if (!Hub.CanLimit && !Hub.Demo)
        {
            var (card, body) = Ui2.Card(L.T("برای تنظیم کارت دسترسی ادمین لازمه", "Tuning needs administrator rights"),
                L.T("دما و کلاک بدون ادمین هم خونده می‌شن، ولی تغییر تنظیمات کارت فقط با دسترسی ادمین ممکنه.", "Readings work without it, but changing the card's settings needs administrator rights."));
            var button = new Button { Style = St("BtnPrimary"), Content = L.T("اجرا با دسترسی ادمین", "Run as administrator"), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            button.Click += (_, _) =>
            {
                if (Application.Current is App app && !app.Restart(elevated: true, page: "gaming:gpu"))
                    Hub.Notify(L.T("دسترسی داده نشد", "Access was not granted"), L.T("ویندوز اجازه‌ی اجرا با دسترسی ادمین رو نداد.", "Windows did not allow running as administrator."));
            };
            body.Children.Add(button);
            _tuning.Children.Add(card);
            _built = true;
            return;
        }
        BuildTuning(_state);
        _built = true;
        SyncControls();
    }

    private void AddNote(string text)
    {
        var (card, body) = Ui2.Card(text);
        card.Margin = new Thickness(0, 12, 0, 0);
        _ = body;
        _tuning.Children.Add(card);
    }

    private void BuildTuning(Od8State state)
    {
        _tuning.Children.Add(_trialBanner ??= BuildTrialBanner());

        var (card, body) = Ui2.Card(L.T("آندرولت و اورکلاک", "Undervolt and overclock"),
            L.T("تغییرها همین لحظه به درایور داده می‌شن و با ریبوت یا «بازنشانی» به حالت کارخانه برمی‌گردن. بعد از اعمال ۲۰ ثانیه وقت داری تایید کنی؛ وگرنه خودکار برمی‌گرده.",
                "Changes go to the driver right away and return to factory values on reboot or “Reset”. After applying you have 20 seconds to confirm; otherwise they revert by themselves."));
        var coreRange = state.Get(AmdGpu.GfxClkFmax);
        var voltRange = state.Get(AmdGpu.OdVoltage);
        var powerRange = state.Get(AmdGpu.PowerPercentage);
        var vramRange = state.Get(AmdGpu.UclkFmax);

        _advanced = new CheckBox
        {
            Style = St("Check"), Content = L.T("نمایش کل بازه‌ی درایور (پرخطر)", "Show the driver's full range (risky)"),
            Margin = new Thickness(0, 12, 0, 4), IsChecked = _advancedOn,
        };
        _advanced.Click += (_, _) =>
        {
            _advancedOn = _advanced.IsChecked == true;
            RebuildRanges();
        };
        body.Children.Add(_advanced);

        // By default the sliders only cover sensible territory around the factory value.
        (double min, double max) Soft(Od8Range range, double below, double above) =>
            _advancedOn ? (range.Min, range.Max)
                : (Math.Max(range.Min, range.Default - below), Math.Min(range.Max, range.Default + above));

        Slider Make(string label, Od8Range? range, string unit, string? hint = null, double below = 0, double above = 0)
        {
            if (range is null) return new Slider { Minimum = 0, Maximum = 0 };
            var (lo, hi) = Soft(range, below, above);
            var row = Ui2.SliderRow(label, lo, Math.Max(hi, lo + 1), 1, v => $"{v:0} {unit}");
            if (hint is not null) row.row.ToolTip = hint;
            body.Children.Add(row.row);
            row.slider.ValueChanged += (_, _) => { if (!_loading) UpdateDraft(); };
            row.value.Text = $"{range.Current} {unit}";
            return row.slider;
        }
        _core = Make(L.T("کلاک بیشینه‌ی گرافیک", "Maximum core clock"), coreRange, "MHz", null, (coreRange?.Default ?? 0) * 0.4, (coreRange?.Default ?? 0) * 0.08);
        _voltage = Make(L.T("ولتاژ بیشینه (آندرولت)", "Maximum voltage (undervolt)"), voltRange, "mV",
            L.T("کم کردن ولتاژ گرمای کارت رو پایین میاره؛ خیلی کم کنی بازی کرش می‌کنه.", "Lowering voltage lowers heat; too low and games crash."), 250, 0);
        _power = Make(L.T("حد توان", "Power limit"), powerRange, "%", null, 100, 100);
        _vram = Make(L.T("کلاک بیشینه‌ی حافظه (VRAM)", "Maximum memory (VRAM) clock"), vramRange, "MHz", null, 0, (vramRange?.Default ?? 0) * 0.1);

        var toggles = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        _fast = new CheckBox { Style = St("Check"), Content = L.T("تایمینگ سریع حافظه", "Fast memory timing"), Margin = new Thickness(0, 0, 8, 8), IsEnabled = state.Get(14) is not null };
        _auto = new CheckBox { Style = St("Check"), Content = L.T("با هر بار اجرای شب‌پا دوباره اعمال بشه", "Apply again every time Shabpa starts"), Margin = new Thickness(0, 0, 8, 8) };
        _fast.Click += (_, _) => UpdateDraft();
        _auto.Click += (_, _) =>
        {
            Profile.AutoApply = _auto.IsChecked == true;
            Hub.Store.Save();
        };
        toggles.Children.Add(_fast);
        toggles.Children.Add(_auto);
        body.Children.Add(toggles);

        var presets = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        void Preset(string label, Action fill)
        {
            var b = new Button { Style = St("Btn"), Content = label, Margin = new Thickness(0, 0, 8, 8) };
            b.Click += (_, _) => { fill(); UpdateDraft(); };
            presets.Children.Add(b);
        }
        Preset(L.T("پیش‌فرض کارخانه", "Factory"), () => SetSliders(coreRange?.Default, voltRange?.Default, powerRange?.Default, vramRange?.Default));
        Preset(L.T("خنک و بی‌صدا", "Cool and quiet"), () => SetSliders(coreRange?.Default, (voltRange?.Default ?? 0) - 100, -10, vramRange?.Default));
        Preset(L.T("متعادل", "Balanced"), () => SetSliders(coreRange?.Default, (voltRange?.Default ?? 0) - 50, 0, vramRange?.Default));
        Preset(L.T("کارایی", "Performance"), () => SetSliders(coreRange?.Default, voltRange?.Default, powerRange?.Max, (vramRange?.Default ?? 0) + 50));
        body.Children.Add(Ui2.Label(L.T("پیش‌تنظیم‌ها (فقط اسلایدرها رو پر می‌کنن؛ تا «اعمال» نزنی چیزی عوض نمی‌شه):", "Presets (they only fill the sliders; nothing changes until you press Apply):"), 6, 6));
        body.Children.Add(presets);

        var buttons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var apply = new Button { Style = St("BtnPrimary"), Content = L.T("اعمال", "Apply"), Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(22, 10, 22, 10) };
        apply.Click += OnApply;
        var reset = new Button { Style = St("BtnDanger"), Content = L.T("بازنشانی همه به حالت کارخانه", "Reset everything to factory") };
        reset.Click += OnReset;
        buttons.Children.Add(apply);
        buttons.Children.Add(reset);
        body.Children.Add(buttons);
        _status = new TextBlock { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        body.Children.Add(_status);
        _tuning.Children.Add(card);

        BuildFan(state);
    }

    private Border BuildTrialBanner()
    {
        _trialText = new TextBlock { Style = St("Body"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var keep = new Button { Style = St("BtnPrimary"), Content = L.T("نگه دار", "Keep"), Margin = new Thickness(8, 0, 0, 0) };
        var revert = new Button { Style = St("Btn"), Content = L.T("برگردون", "Revert") };
        keep.Click += (_, _) => EndTrial(keep: true);
        revert.Click += (_, _) => EndTrial(keep: false);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(revert);
        buttons.Children.Add(keep);
        var row = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        row.Children.Add(buttons);
        row.Children.Add(_trialText);
        return new Border
        {
            Style = St("Card"), Margin = new Thickness(0, 12, 0, 0), Background = Res("GoldSoft"), BorderBrush = Res("Gold"),
            Visibility = Visibility.Collapsed, Child = row,
        };
    }

    private void RebuildRanges()
    {
        TryBuild();
    }

    private void SetSliders(int? core, int? voltage, int? power, int? vram)
    {
        _loading = true;
        void Set(Slider slider, int? value)
        {
            if (value is { } v && slider.Maximum > slider.Minimum) slider.Value = Math.Clamp(v, slider.Minimum, slider.Maximum);
        }
        Set(_core, core);
        Set(_voltage, voltage);
        Set(_power, power);
        Set(_vram, vram);
        _loading = false;
    }

    private void SyncControls()
    {
        if (_state is null || _core is null) return;
        _loading = true;
        var p = Profile;
        var state = _state;
        void Pos(Slider slider, Od8Range? range, int? chosen)
        {
            if (range is null || slider.Maximum <= slider.Minimum) return;
            // The soft range keeps the sliders in sensible territory unless "full range" is ticked.
            slider.Value = Math.Clamp(chosen ?? range.Current, slider.Minimum, slider.Maximum);
        }
        Pos(_core, state.Get(AmdGpu.GfxClkFmax), p.CoreMax);
        Pos(_voltage, state.Get(AmdGpu.OdVoltage), p.Voltage);
        Pos(_power, state.Get(AmdGpu.PowerPercentage), p.PowerPercent);
        Pos(_vram, state.Get(AmdGpu.UclkFmax), p.VramMax);
        _fast.IsChecked = p.FastTiming ?? state.Get(14)?.Current == 1;
        _auto.IsChecked = p.AutoApply;
        _zero.IsChecked = p.ZeroRpm ?? state.Get(AmdGpu.FanZeroRpm)?.Current == 1;
        _target.Value = p.TargetTemp;
        for (var i = 0; i < _curveTemp.Count; i++)
        {
            _curveTemp[i].Value = Math.Clamp(p.CurveTemp.ElementAtOrDefault(i), _curveTemp[i].Minimum, _curveTemp[i].Maximum);
            _curveSpeed[i].Value = Math.Clamp(p.CurveSpeed.ElementAtOrDefault(i), _curveSpeed[i].Minimum, _curveSpeed[i].Maximum);
        }
        _loading = false;
        _draft = Clone(p);
        UpdateFanVisibility();
        DrawCurve();
        _status.Text = L.T($"وضعیت فعلی: حد توان {state.Get(AmdGpu.PowerPercentage)?.Current:+0;-0;0}٪ · ولتاژ بیشینه {state.Get(AmdGpu.OdVoltage)?.Current} mV · کلاک حافظه {state.Get(AmdGpu.UclkFmax)?.Current} MHz",
            $"Current: power limit {state.Get(AmdGpu.PowerPercentage)?.Current:+0;-0;0}% · max voltage {state.Get(AmdGpu.OdVoltage)?.Current} mV · memory clock {state.Get(AmdGpu.UclkFmax)?.Current} MHz");
    }

    private static GpuTuneProfile Clone(GpuTuneProfile p) => new()
    {
        CoreMax = p.CoreMax, Voltage = p.Voltage, PowerPercent = p.PowerPercent, VramMax = p.VramMax, FastTiming = p.FastTiming, ZeroRpm = p.ZeroRpm,
        FanMode = p.FanMode, FanPreset = p.FanPreset, CurveTemp = [.. p.CurveTemp], CurveSpeed = [.. p.CurveSpeed], TargetTemp = p.TargetTemp,
        AutoApply = p.AutoApply, Pending = p.Pending,
    };

    private void UpdateDraft()
    {
        if (_loading || _state is null) return;
        var s = _state;
        int? Pick(Slider slider, int id)
        {
            var range = s.Get(id);
            return range is null ? null : (int)Math.Round(slider.Value);
        }
        _draft.CoreMax = Pick(_core, AmdGpu.GfxClkFmax);
        _draft.Voltage = Pick(_voltage, AmdGpu.OdVoltage);
        _draft.PowerPercent = Pick(_power, AmdGpu.PowerPercentage);
        _draft.VramMax = Pick(_vram, AmdGpu.UclkFmax);
        _draft.FastTiming = s.Get(14) is null ? null : _fast.IsChecked == true;
        _draft.ZeroRpm = s.Get(AmdGpu.FanZeroRpm) is null ? null : _zero.IsChecked == true;
        _draft.TargetTemp = (int)_target.Value;
        for (var i = 0; i < _curveTemp.Count; i++)
        {
            _draft.CurveTemp[i] = (int)_curveTemp[i].Value;
            _draft.CurveSpeed[i] = (int)_curveSpeed[i].Value;
        }
        DrawCurve();
    }

    private string Summary(GpuTuneProfile p)
    {
        var parts = new List<string>();
        if (p.CoreMax is { } c) parts.Add(L.T($"کلاک گرافیک ≤ {c} MHz", $"core clock ≤ {c} MHz"));
        if (p.Voltage is { } v) parts.Add(L.T($"ولتاژ ≤ {v} mV", $"voltage ≤ {v} mV"));
        if (p.PowerPercent is { } w) parts.Add(L.T($"حد توان {w:+0;-0;0}٪", $"power limit {w:+0;-0;0}%"));
        if (p.VramMax is { } m) parts.Add(L.T($"کلاک حافظه ≤ {m} MHz", $"memory clock ≤ {m} MHz"));
        if (p.FastTiming is { } f) parts.Add(L.T($"تایمینگ سریع {(f ? "روشن" : "خاموش")}", $"fast timing {(f ? "on" : "off")}"));
        parts.Add(p.FanMode switch
        {
            "preset" => L.T($"فن: {FanCurves.Presets.First(x => x.key == p.FanPreset).fa}", $"fan: {FanCurves.Presets.First(x => x.key == p.FanPreset).en}"),
            "custom" => L.T("فن: منحنی دلخواه", "fan: custom curve"),
            "smart" => L.T($"فن هوشمند، هدف {p.TargetTemp}°", $"smart fan, target {p.TargetTemp}°"),
            _ => L.T("فن: دست‌نخورده", "fan: untouched"),
        });
        return string.Join(" · ", parts);
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        UpdateDraft();
        if (_state is null) return;
        if (!Ask(L.T("این تنظیمات روی کارت گرافیک اعمال بشه؟", "Apply these settings to the graphics card?"),
                L.T("تغییر ولتاژ و کلاک ممکنه موقع بازی باعث کرش یا سیاه شدن صفحه بشه. بعد از اعمال ۲۰ ثانیه وقت داری «نگه دار» رو بزنی؛ اگه نزنی خودکار به مقدار قبلی برمی‌گرده. با ریبوت هم همه چیز به حالت کارخانه برمی‌گرده.",
                    "Changing voltage or clocks can make a game crash or the screen go black. After applying you have 20 seconds to press Keep; if you do not, the previous values come back by themselves. A reboot also restores factory values."),
                L.T("تایید و اعمال", "Confirm and apply"), detail: Summary(_draft)))
            return;
        _before = Tuner.Snapshot();
        _previous = Clone(Profile);
        var error = Tuner.Apply(_draft);
        if (error is not null)
        {
            _status.Text = L.T($"اعمال نشد: {error}", $"Not applied: {error}");
            return;
        }
        Hub.Settings.GpuTune = Clone(_draft);
        Hub.Settings.GpuTune.Pending = false;
        StartTrial();
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        if (!Ask(L.T("همه‌ی تنظیمات کارت به حالت کارخانه برگرده؟", "Put every graphics setting back to factory?"),
                L.T("ولتاژ، کلاک‌ها، حد توان و منحنی فن به مقدار پیش‌فرض درایور برمی‌گردن.", "Voltage, clocks, power limit and the fan curve go back to the driver's defaults."),
                L.T("بازنشانی کن", "Reset"), danger: true))
            return;
        var error = Tuner.ResetAll();
        EndTrialSilently();
        var keepAuto = Profile.AutoApply;
        Hub.Settings.GpuTune = new GpuTuneProfile { AutoApply = false };
        _ = keepAuto;
        Hub.Store.Save();
        _built = false;
        Refresh();
        if (error is not null) _status.Text = L.T($"بازنشانی کامل نشد: {error}", $"Reset was not complete: {error}");
    }

    // ---- confirm-or-revert ----------------------------------------------------------------------------

    private void StartTrial()
    {
        _trialLeft = TrialSeconds;
        UpdateTrial();
        if (_trialBanner is not null) _trialBanner.Visibility = Visibility.Visible;
        _trial.Start();
    }

    private void TrialTick()
    {
        _trialLeft--;
        if (_trialLeft <= 0)
        {
            EndTrial(keep: false);
            return;
        }
        UpdateTrial();
    }

    private void UpdateTrial()
    {
        if (_trialText is null) return;
        _trialText.Text = L.T($"تنظیمات اعمال شد. اگه همه‌چی عادیه «نگه دار» رو بزن؛ تا {_trialLeft} ثانیه‌ی دیگه خودکار برمی‌گرده.",
            $"Settings applied. If everything looks normal press Keep; they revert by themselves in {_trialLeft} s.");
    }

    private void EndTrial(bool keep)
    {
        _trial.Stop();
        if (_trialBanner is not null) _trialBanner.Visibility = Visibility.Collapsed;
        if (keep)
        {
            Hub.Store.Save();
            Hub.Notify(L.T("کارت گرافیک", "Graphics card"), L.T("تنظیمات نگه داشته شد.", "Settings kept."));
        }
        else
        {
            if (_before is not null) Tuner.Restore(_before);
            if (_previous is not null) Hub.Settings.GpuTune = _previous;
            Hub.Notify(L.T("کارت گرافیک", "Graphics card"), L.T("به تنظیمات قبلی برگشت.", "Went back to the previous settings."));
        }
        _built = false;
        Refresh();
    }

    private void EndTrialSilently()
    {
        _trial.Stop();
        if (_trialBanner is not null) _trialBanner.Visibility = Visibility.Collapsed;
    }

    // ---- fan -------------------------------------------------------------------------------------

    private void BuildFan(Od8State state)
    {
        var (card, body) = Ui2.Card(L.T("فن", "Fan"),
            L.T("منحنی فن ۵ نقطه داره: در هر دما، فن با چند درصد بچرخه. حالت هوشمند خودش منحنی رو بالا و پایین می‌کنه تا دمای هات‌اسپات روی هدف بمونه.",
                "The fan curve has 5 points: at each temperature, how fast the fan spins. Smart mode raises and lowers the whole curve to hold the hot spot at your target."));
        var temp0 = state.Get(AmdGpu.FanCurveTemp1);
        var speed0 = state.Get(AmdGpu.FanCurveSpeed1);
        if (temp0 is null || speed0 is null)
        {
            body.Children.Add(Ui2.Label(L.T("این کارت منحنی فن قابل‌تنظیم نداره.", "This card has no adjustable fan curve."), 10, 0));
            _zero = new CheckBox();
            _target = new Slider();
            _tuning.Children.Add(card);
            return;
        }

        _zero = new CheckBox
        {
            Style = St("Check"), Content = L.T("توقف کامل فن در دمای پایین (Zero RPM)", "Stop the fan completely when cool (Zero RPM)"),
            Margin = new Thickness(0, 12, 0, 4), IsEnabled = state.Get(AmdGpu.FanZeroRpm) is not null,
        };
        _zero.Click += (_, _) => UpdateDraft();
        body.Children.Add(_zero);

        body.Children.Add(Ui2.Label(L.T("حالت:", "Mode:"), 8, 6));
        body.Children.Add(_fanModeChips);
        Chips(_fanModeChips,
            [("driver", L.T("پیش‌فرض کارت", "Card default")), ("preset", L.T("پیش‌تنظیم", "Preset")), ("custom", L.T("دلخواه", "Custom")), ("smart", L.T("هوشمند", "Smart"))],
            Profile.FanMode, mode =>
            {
                _draft.FanMode = mode;
                UpdateFanVisibility();
                DrawCurve();
            });
        _presetLabel = Ui2.Label(L.T("پیش‌تنظیم:", "Preset:"), 4, 6);
        body.Children.Add(_presetLabel);
        body.Children.Add(_fanPresetChips);
        Chips(_fanPresetChips, FanCurves.Presets.Select(p => (p.key, L.T(p.fa, p.en))), Profile.FanPreset, key =>
        {
            _draft.FanPreset = key;
            DrawCurve();
        });

        for (var i = 0; i < AmdGpu.FanCurvePoints; i++)
        {
            var t = Ui2.SliderRow(L.T($"نقطه‌ی {i + 1}: دما", $"Point {i + 1}: temperature"), temp0.Min, temp0.Max, 1, v => $"{v:0} °C");
            var sp = Ui2.SliderRow(L.T("   سرعت فن", "   fan speed"), speed0.Min, speed0.Max, 1, v => $"{v:0} %");
            t.slider.ValueChanged += (_, _) => UpdateDraft();
            sp.slider.ValueChanged += (_, _) => UpdateDraft();
            _curveTemp.Add(t.slider);
            _curveSpeed.Add(sp.slider);
            _customFan.Children.Add(t.row);
            _customFan.Children.Add(sp.row);
        }
        body.Children.Add(_customFan);

        var target = Ui2.SliderRow(L.T("دمای هدف هات‌اسپات", "Hot spot target"), 60, 100, 1, v => $"{v:0} °C");
        _target = target.slider;
        _target.ValueChanged += (_, _) => UpdateDraft();
        _smartFan.Children.Add(target.row);
        _smartFan.Children.Add(new TextBlock
        {
            Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
            Text = L.T("حالت هوشمند تا وقتی شب‌پا بازه کار می‌کنه؛ با بستن کامل برنامه فن به منحنی پیش‌فرض کارت برمی‌گرده.",
                "Smart mode works while Shabpa is running; when you exit the app completely the fan returns to the card's own curve."),
        });
        body.Children.Add(_smartFan);

        _curveChart.Background = Res("Panel2");
        var frame = new Border { CornerRadius = new CornerRadius(12), Child = _curveChart, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
        body.Children.Add(frame);

        var apply = new Button
        {
            Style = St("BtnPrimary"), Content = L.T("اعمال تنظیم فن", "Apply fan settings"), Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(20, 10, 20, 10),
        };
        apply.Click += OnApplyFan;
        body.Children.Add(apply);
        _tuning.Children.Add(card);
    }

    private void UpdateFanVisibility()
    {
        var mode = _draft.FanMode;
        _fanPresetChips.Visibility = mode == "preset" ? Visibility.Visible : Visibility.Collapsed;
        _presetLabel.Visibility = _fanPresetChips.Visibility;
        _customFan.Visibility = mode == "custom" ? Visibility.Visible : Visibility.Collapsed;
        _smartFan.Visibility = mode == "smart" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void DrawCurve()
    {
        if (_curveChart.Children.Count == 0 && _curveChart.Width <= 0) return;
        _curveChart.Children.Clear();
        if (_state?.Get(AmdGpu.FanCurveTemp1) is not { } t0 || _state.Get(AmdGpu.FanCurveSpeed1) is not { } s0) return;
        const double w = 300, h = 120;
        int[] temps, speeds;
        switch (_draft.FanMode)
        {
            case "preset":
                (temps, speeds) = FanCurves.Preset(_draft.FanPreset);
                break;
            case "custom":
                (temps, speeds) = (_draft.CurveTemp.ToArray(), _draft.CurveSpeed.ToArray());
                break;
            case "smart":
                (temps, speeds) = (FanCurves.Balanced.temps, FanCurves.Balanced.speeds);
                break;
            default:
                var features = _state.Features;
                temps = Enumerable.Range(0, AmdGpu.FanCurvePoints).Select(i => features.TryGetValue(AmdGpu.FanCurveTemp1 + i * 2, out var r) ? r.Default : 0).ToArray();
                speeds = Enumerable.Range(0, AmdGpu.FanCurvePoints).Select(i => features.TryGetValue(AmdGpu.FanCurveSpeed1 + i * 2, out var r) ? r.Default : 0).ToArray();
                break;
        }
        (temps, speeds) = FanCurves.Sanitize(temps, speeds, t0.Min, t0.Max, s0.Min, s0.Max);
        if (_draft.FanMode == "smart") speeds = FanSmart.Apply(speeds, Tuner.SmartOffset, s0.Min, s0.Max);

        double X(int temp) => 8 + (temp - t0.Min) / (double)(t0.Max - t0.Min) * (w - 16);
        double Y(int speed) => h - 8 - (speed - 0) / 100.0 * (h - 16);
        var points = new PointCollection();
        for (var i = 0; i < temps.Length; i++) points.Add(new Point(X(temps[i]), Y(speeds[i])));
        _curveChart.Children.Add(new Polyline { Points = points, Stroke = Res("Accent"), StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round });
        foreach (var p in points)
        {
            var dot = new Ellipse { Width = 8, Height = 8, Fill = Res("Accent") };
            Canvas.SetLeft(dot, p.X - 4);
            Canvas.SetTop(dot, p.Y - 4);
            _curveChart.Children.Add(dot);
        }
        _curveChart.Children.Add(new TextBlock { Text = "°C →", Style = St("Label"), FontSize = 10.5, Margin = new Thickness(w - 36, h - 18, 0, 0) });
    }

    private void OnApplyFan(object sender, RoutedEventArgs e)
    {
        UpdateDraft();
        if (!Ask(L.T("تنظیم فن اعمال بشه؟", "Apply the fan setting?"),
                L.T("منحنی فن کارت همین الان عوض می‌شه. فن خیلی کند ممکنه باعث داغ شدن کارت بشه؛ دما رو بالا نگه دار زیر نظر داشته باش.",
                    "The card's fan curve changes right now. A very slow fan can let the card overheat; keep an eye on the temperature above."),
                L.T("تایید و اعمال", "Confirm and apply"), detail: Summary(_draft)))
            return;
        // Only the fan part of the draft is written here; clock and voltage keep whatever is applied now.
        var fanOnly = new GpuTuneProfile
        {
            FanMode = _draft.FanMode, FanPreset = _draft.FanPreset, CurveTemp = [.. _draft.CurveTemp], CurveSpeed = [.. _draft.CurveSpeed],
            TargetTemp = _draft.TargetTemp, ZeroRpm = _draft.ZeroRpm,
        };
        string? error;
        if (_draft.FanMode == "driver")
        {
            error = Tuner.ResetFan();
        }
        else
        {
            error = Tuner.Apply(fanOnly);
        }
        if (error is not null)
        {
            _status.Text = L.T($"اعمال نشد: {error}", $"Not applied: {error}");
            return;
        }
        var saved = Hub.Settings.GpuTune;
        saved.FanMode = _draft.FanMode;
        saved.FanPreset = _draft.FanPreset;
        saved.CurveTemp = [.. _draft.CurveTemp];
        saved.CurveSpeed = [.. _draft.CurveSpeed];
        saved.TargetTemp = _draft.TargetTemp;
        saved.ZeroRpm = _draft.ZeroRpm;
        Hub.Store.Save();
        Hub.Notify(L.T("فن", "Fan"), L.T("تنظیم فن اعمال شد.", "Fan setting applied."));
    }
}
