using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Shabpa.Core;

namespace Shabpa.Pages;

/// <summary>Small builders shared by the gaming pages.</summary>
internal static class Ui2
{
    public static (Border card, StackPanel body) Card(string title, string? subtitle = null)
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.Resources["H2"] });
        if (subtitle is not null)
            body.Children.Add(new TextBlock
            {
                Text = subtitle, Style = (Style)Application.Current.Resources["Body"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0),
            });
        var card = new Border { Style = (Style)Application.Current.Resources["Card"], Margin = new Thickness(0, 12, 0, 0), Child = body };
        return (card, body);
    }

    public static TextBlock Label(string text, double top = 12, double bottom = 6) => new()
    {
        Text = text, Style = (Style)Application.Current.Resources["Label"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, bottom),
    };

    /// <summary>A label, a slider and its current value on one row.</summary>
    public static (Grid row, Slider slider, TextBlock value) SliderRow(string label, double min, double max, double step, Func<double, string> format)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        grid.Children.Add(new TextBlock { Text = label, Style = (Style)Application.Current.Resources["Body"], VerticalAlignment = VerticalAlignment.Center });
        var slider = new Slider
        {
            Style = (Style)Application.Current.Resources["Slide"], Minimum = min, Maximum = max, SmallChange = step, LargeChange = step * 5,
            TickFrequency = step, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(slider, 1);
        grid.Children.Add(slider);
        var value = new TextBlock
        {
            Style = (Style)Application.Current.Resources["Body"], FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right, FlowDirection = FlowDirection.LeftToRight,
        };
        Grid.SetColumn(value, 2);
        grid.Children.Add(value);
        slider.ValueChanged += (_, _) => value.Text = format(slider.Value);
        return (grid, slider, value);
    }
}

/// <summary>Settings and a live preview of the in-game overlay.</summary>
public sealed class OverlayPage : PageBase
{
    private static readonly (string key, string fa, string en)[] Anchors =
    [
        ("tl", "بالا چپ", "Top left"), ("tc", "بالا وسط", "Top centre"), ("tr", "بالا راست", "Top right"),
        ("bl", "پایین چپ", "Bottom left"), ("bc", "پایین وسط", "Bottom centre"), ("br", "پایین راست", "Bottom right"),
    ];

    private static readonly (string key, string fa, string en)[] Groups =
    [
        ("general", "عمومی", "General"), ("gpu", "کارت گرافیک (GPU)", "Graphics card (GPU)"), ("cpu", "پردازنده (CPU)", "Processor (CPU)"),
        ("ram", "حافظه (RAM)", "Memory (RAM)"), ("net", "شبکه (NET)", "Network (NET)"),
    ];

    private readonly DispatcherTimer _save = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly Border _previewStage = new();
    private readonly WrapPanel _modeChips = new();
    private readonly WrapPanel _layoutChips = new();
    private readonly WrapPanel _lookChips = new();
    private readonly WrapPanel _anchorChips = new();
    private readonly StackPanel _custom = new();
    private readonly TextBox _accentBox = Input(), _textBox = Input(), _backBox = Input();
    private readonly TextBox _toggleKey = Input(), _moveKey = Input();
    private readonly Button _moveButton = new();
    private readonly TextBlock _note = new();
    private readonly TextBlock _boardNote = new();
    private CheckBox _board = null!;
    private Slider _scale = null!, _opacity = null!, _offsetX = null!, _offsetY = null!;
    private readonly Dictionary<string, CheckBox> _items = [];
    private CheckBox _colorByValue = null!, _labels = null!, _shadow = null!, _rounded = null!;
    private bool _loading;
    private List<TelemetrySample> _fakeHistory = [];

    public override string PageTitle => L.T("اورلی بازی", "Game overlay");
    public override string PageSubtitle => L.T("اطلاعات سیستم روی بازی، با ظاهر و رنگ دلخواه.", "Live system stats over your game, looking the way you want.");

    private static TextBox Input() => new()
    {
        Style = (Style)Application.Current.Resources["Input"], FlowDirection = FlowDirection.LeftToRight, Width = 110, FontFamily = new FontFamily("Consolas"),
        Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 8, 0),
    };

    public OverlayPage()
    {
        _save.Tick += (_, _) =>
        {
            _save.Stop();
            Hub.Store.Save();
        };

        var root = new StackPanel();

        // ---- preview + mode -----------------------------------------------------------------------
        var (previewCard, preview) = Ui2.Card(L.T("پیش‌نمایش", "Preview"));
        previewCard.Margin = new Thickness(0);
        _previewStage.Height = 210;
        _previewStage.CornerRadius = new CornerRadius(12);
        _previewStage.ClipToBounds = true;
        _previewStage.FlowDirection = FlowDirection.LeftToRight;
        _previewStage.Margin = new Thickness(0, 12, 0, 0);
        _previewStage.Background = new LinearGradientBrush(
            [new GradientStop(Color.FromRgb(18, 24, 44), 0), new GradientStop(Color.FromRgb(46, 31, 74), 0.55), new GradientStop(Color.FromRgb(14, 62, 74), 1)],
            new Point(0, 0), new Point(1, 1));
        preview.Children.Add(_previewStage);
        preview.Children.Add(Ui2.Label(L.T("چه موقع نشون داده بشه؟", "When should it show?")));
        preview.Children.Add(_modeChips);
        _note.Style = St("Label");
        _note.TextWrapping = TextWrapping.Wrap;
        _note.Margin = new Thickness(0, 8, 0, 0);
        preview.Children.Add(_note);
        root.Children.Add(previewCard);

        // ---- layout -----------------------------------------------------------------------------
        var (layoutCard, layout) = Ui2.Card(L.T("طرح", "Layout"), L.T("شش طرح متفاوت؛ همه با همین اطلاعات انتخابی کار می‌کنن.", "Six different layouts; all of them show the items you pick."));
        layout.Children.Add(Ui2.Label("", 4, 4));
        layout.Children.Add(_layoutChips);
        root.Children.Add(layoutCard);

        // ---- colours -----------------------------------------------------------------------------
        var (lookCard, look) = Ui2.Card(L.T("رنگ و تم", "Colours"));
        look.Children.Add(Ui2.Label("", 0, 4));
        look.Children.Add(_lookChips);
        _custom.Children.Add(Ui2.Label(L.T("رنگ‌ها را به شکل ۶ رقم هگز بنویس (مثلاً 3EE6A0):", "Enter colours as 6 hex digits (for example 3EE6A0):")));
        var customRow = new WrapPanel();
        void Field(string label, TextBox box)
        {
            var cell = new StackPanel { Margin = new Thickness(0, 0, 12, 6) };
            cell.Children.Add(new TextBlock { Text = label, Style = St("Label") });
            box.Margin = new Thickness(0, 4, 0, 0);
            cell.Children.Add(box);
            customRow.Children.Add(cell);
        }
        Field(L.T("رنگ اصلی", "Accent"), _accentBox);
        Field(L.T("رنگ متن", "Text"), _textBox);
        Field(L.T("رنگ پس‌زمینه", "Background"), _backBox);
        _custom.Children.Add(customRow);
        look.Children.Add(_custom);
        root.Children.Add(lookCard);

        // ---- size, opacity, position -----------------------------------------------------------------
        var (sizeCard, size) = Ui2.Card(L.T("اندازه، شفافیت و جای قرارگیری", "Size, opacity and position"));
        var scale = Ui2.SliderRow(L.T("اندازه", "Size"), 0.6, 2.2, 0.05, v => $"{v * 100:0}%");
        var opacity = Ui2.SliderRow(L.T("تیرگی پس‌زمینه", "Background opacity"), 0.1, 1.0, 0.05, v => $"{v * 100:0}%");
        var offsetX = Ui2.SliderRow(L.T("فاصله از کنار", "Offset from the side"), 0, 600, 4, v => $"{v:0} px");
        var offsetY = Ui2.SliderRow(L.T("فاصله از بالا/پایین", "Offset from top/bottom"), 0, 600, 4, v => $"{v:0} px");
        (_scale, _opacity, _offsetX, _offsetY) = (scale.slider, opacity.slider, offsetX.slider, offsetY.slider);
        foreach (var row in new[] { scale.row, opacity.row, offsetX.row, offsetY.row }) size.Children.Add(row);
        size.Children.Add(Ui2.Label(L.T("گوشه‌ی صفحه:", "Corner of the screen:"), 14));
        size.Children.Add(_anchorChips);
        _moveButton.Style = St("Btn");
        _moveButton.Margin = new Thickness(0, 12, 0, 0);
        _moveButton.HorizontalAlignment = HorizontalAlignment.Left;
        _moveButton.Click += OnMove;
        size.Children.Add(_moveButton);
        root.Children.Add(sizeCard);

        // ---- look options ------------------------------------------------------------------------
        var (optionCard, options) = Ui2.Card(L.T("جزئیات ظاهر", "Details"));
        var optionRow = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        CheckBox Option(string text) => new() { Style = St("Check"), Content = text, Margin = new Thickness(0, 0, 8, 8) };
        _colorByValue = Option(L.T("رنگ هشدار (سبز/زرد/قرمز)", "Warning colours (green / amber / red)"));
        _labels = Option(L.T("نمایش نام هر مورد", "Show item names"));
        _shadow = Option(L.T("سایه‌ی متن", "Text shadow"));
        _rounded = Option(L.T("گوشه‌های گرد", "Rounded corners"));
        foreach (var box in new[] { _colorByValue, _labels, _shadow, _rounded })
        {
            box.Click += OnOption;
            optionRow.Children.Add(box);
        }
        options.Children.Add(optionRow);
        root.Children.Add(optionCard);

        // ---- items -------------------------------------------------------------------------------
        // Readings this particular card does not report (checked once, on a short-lived connection).
        GpuReading? card = null;
        if (!Hub.Demo)
        {
            try
            {
                using var probe = GpuProbe.Open();
                card = probe?.Read();
            }
            catch { /* no card information, nothing is marked */ }
        }

        var (itemCard, items) = Ui2.Card(L.T("چی نشون داده بشه؟", "What to show"),
            L.T("فقط چیزهایی که این سیستم می‌تونه بخونه نشون داده می‌شن. فریم بر ثانیه به دسترسی ادمین نیاز داره.",
                "Only readings this PC can provide are shown. Frame rate needs administrator rights."));
        foreach (var (key, fa, en) in Groups)
        {
            items.Children.Add(Ui2.Label(L.T(fa, en), 12, 4));
            var wrap = new WrapPanel();
            foreach (var def in OverlayMetrics.All.Where(m => m.Group == key))
            {
                var box = new CheckBox { Style = St("Check"), Content = def.Label, Margin = new Thickness(0, 0, 8, 8), Tag = def.Key };
                if (def.Advanced)
                {
                    // Fan speeds and memory temperature are not available through Windows itself.
                    box.IsEnabled = false;
                    box.ToolTip = L.T("این مقدار به حسگر سخت‌افزاری پیشرفته نیاز داره که هنوز فعال نیست.",
                        "This reading needs an advanced hardware sensor module that is not enabled yet.");
                    box.Content = def.Label + L.T(" (به‌زودی)", " (soon)");
                }
                else if (card is { Name.Length: > 0 } && Unsupported(def.Key, card))
                {
                    box.ToolTip = L.T("این کارت گرافیک این مقدار رو گزارش نمی‌ده.", "This graphics card does not report this reading.");
                    box.Content = def.Label + L.T(" (پشتیبانی نمی‌شه)", " (not reported)");
                    box.Opacity = 0.55;
                }
                box.Click += OnItem;
                _items[def.Key] = box;
                wrap.Children.Add(box);
            }
            items.Children.Add(wrap);
        }
        root.Children.Add(itemCard);

        // ---- optional advanced sensors ------------------------------------------------------------
        var (boardCard, board) = Ui2.Card(L.T("حسگرهای پیشرفته (اختیاری)", "Advanced sensors (optional)"),
            L.T("دمای واقعی CPU و دور و درصد فن CPU از حسگر مادربورد خونده می‌شن. به اجرای شب‌پا با دسترسی ادمین و درایور PawnIO نیاز داره؛ شب‌پا چیزی نصب نمی‌کنه.",
                "Real CPU temperature and CPU fan speed are read from the motherboard chip. Needs Shabpa running as administrator and the PawnIO driver; Shabpa installs nothing."));
        _board = new CheckBox { Style = St("Check"), Content = L.T("فعال‌سازی حسگرهای پیشرفته", "Enable advanced sensors"), Margin = new Thickness(0, 10, 0, 6) };
        _board.Click += (_, _) =>
        {
            if (_loading) return;
            Hub.Settings.Overlay.BoardSensors = _board.IsChecked == true;
            Hub.Telemetry.UseBoardSensors = Hub.Settings.Overlay.BoardSensors && !Hub.Demo;
            Hub.OverlayChanged();
            Hub.Telemetry.Acquire();
            Hub.Telemetry.Release();
        };
        _boardNote.Style = St("Label");
        _boardNote.TextWrapping = TextWrapping.Wrap;
        board.Children.Add(_board);
        board.Children.Add(_boardNote);
        root.Children.Add(boardCard);

        // ---- hotkeys ---------------------------------------------------------------------------
        var (keyCard, keys) = Ui2.Card(L.T("میان‌بر کیبورد", "Keyboard shortcuts"),
            L.T("همیشه با کلید Alt: یک حرف یا عدد بنویس. اگه خالی باشه آن میان‌بر غیرفعاله.", "Always used with Alt: type one letter or digit. Leave it empty to turn that shortcut off."));
        var keyRow = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        void Key(string label, TextBox box)
        {
            box.Width = 70;
            box.MaxLength = 1;
            var cell = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
            cell.Children.Add(new TextBlock { Text = label, Style = St("Label") });
            box.Margin = new Thickness(0, 4, 0, 0);
            cell.Children.Add(box);
            keyRow.Children.Add(cell);
        }
        Key(L.T("نمایش / پنهان", "Show / hide"), _toggleKey);
        Key(L.T("جابجایی با ماوس", "Move with the mouse"), _moveKey);
        keys.Children.Add(keyRow);
        root.Children.Add(keyCard);

        root.Children.Add(new TextBlock
        {
            Style = St("Label"), Margin = new Thickness(4, 12, 4, 0), TextWrapping = TextWrapping.Wrap,
            Text = L.T("اورلی روی بازی‌های «بدون کادر» و «پنجره‌ای» نشون داده می‌شه؛ بازی‌های تمام‌صفحه‌ی اختصاصی (exclusive) پنجره‌ی روی خودشون رو اجازه نمی‌دن. چیزی داخل بازی تزریق نمی‌شه، پس با آنتی‌چیت مشکلی نداره.",
                "The overlay shows over borderless and windowed games; true exclusive full-screen games do not allow a window over them. Nothing is injected into any game, so anti-cheat has nothing to object to."),
        });
        Content = root;

        Chips(_modeChips, [(OverlayMode.Off, L.T("خاموش", "Off")), (OverlayMode.InGame, L.T("فقط موقع بازی", "Only while gaming")), (OverlayMode.Always, L.T("همیشه", "Always"))],
            Hub.Settings.Overlay.Mode, v => Change(o => o.Mode = v));
        Chips(_layoutChips, OverlayLayouts.All.Select(l => (l.key, L.T(l.fa, l.en))), Hub.Settings.Overlay.Layout, v => Change(o => o.Layout = v));
        Chips(_lookChips, OverlayLook.All.Select(l => (l.Key, l.Name)), Hub.Settings.Overlay.Look, v => Change(o => o.Look = v));
        Chips(_anchorChips, Anchors.Select(a => (a.key, L.T(a.fa, a.en))), Hub.Settings.Overlay.Anchor, v => Change(o => o.Anchor = v));

        scale.slider.ValueChanged += (_, _) => Change(o => o.Scale = scale.slider.Value);
        opacity.slider.ValueChanged += (_, _) => Change(o => o.BackOpacity = opacity.slider.Value);
        offsetX.slider.ValueChanged += (_, _) => Change(o => o.X = offsetX.slider.Value);
        offsetY.slider.ValueChanged += (_, _) => Change(o => o.Y = offsetY.slider.Value);
        _accentBox.TextChanged += (_, _) => ColourBox(_accentBox, (o, v) => o.CustomAccent = v);
        _textBox.TextChanged += (_, _) => ColourBox(_textBox, (o, v) => o.CustomText = v);
        _backBox.TextChanged += (_, _) => ColourBox(_backBox, (o, v) => o.CustomBack = v);
        _toggleKey.TextChanged += (_, _) => Change(o => o.ToggleKey = _toggleKey.Text.Trim().ToUpperInvariant());
        _moveKey.TextChanged += (_, _) => Change(o => o.MoveKey = _moveKey.Text.Trim().ToUpperInvariant());

        Hub.OverlayMoving += _ => Dispatcher.BeginInvoke(UpdateMoveButton);
        IsVisibleChanged += (_, _) => { if (IsVisible) Refresh(); };
    }

    private void Change(Action<OverlaySettings> edit)
    {
        if (_loading) return;
        edit(Hub.Settings.Overlay);
        Hub.Overlay?.Evaluate();
        UpdatePreview();
        UpdateVisibility();
        _save.Stop();
        _save.Start();
    }

    private void ColourBox(TextBox box, Action<OverlaySettings, string> set)
    {
        var text = box.Text.Trim().TrimStart('#').ToUpperInvariant();
        box.Foreground = OverlayLook.IsHex(text) || text.Length == 0 ? Res("Text") : Res("Red");
        if (OverlayLook.IsHex(text)) Change(o => set(o, text));
    }

    private void OnOption(object sender, RoutedEventArgs e) => Change(o =>
    {
        o.ColorByValue = _colorByValue.IsChecked == true;
        o.ShowLabels = _labels.IsChecked == true;
        o.Shadow = _shadow.IsChecked == true;
        o.Rounded = _rounded.IsChecked == true;
    });

    private static bool Unsupported(string key, GpuReading g) => key switch
    {
        "memtemp" => g.MemTempC is null,
        "hotspot" => g.HotspotC is null,
        "gpuvolt" => g.VoltageMv is null,
        "gpupower" => g.PowerW is null,
        "gpufan" => g.FanPercent is null,
        "gpufanrpm" => g.FanRpm is null,
        _ => false,
    };

    private void OnItem(object sender, RoutedEventArgs e) => Change(o =>
    {
        // Keep the catalogue order so the overlay reads the same way every time.
        o.Items = [.. OverlayMetrics.All.Where(m => _items[m.Key].IsChecked == true && !m.Advanced).Select(m => m.Key)];
    });

    private void OnMove(object sender, RoutedEventArgs e)
    {
        if (Hub.Overlay is null) return;
        Hub.Overlay.SetMoveMode(!Hub.Overlay.MoveMode);
        UpdateMoveButton();
    }

    private void UpdateMoveButton()
    {
        var moving = Hub.Overlay?.MoveMode == true;
        _moveButton.Content = moving
            ? L.T("تموم شد (جای اورلی ذخیره می‌شه)", "Done (position is saved)")
            : L.T("جابجایی اورلی با ماوس", "Move the overlay with the mouse");
        _moveButton.Style = St(moving ? "BtnPrimary" : "Btn");
    }

    private void UpdateVisibility() => _custom.Visibility = Hub.Settings.Overlay.Look == "custom" ? Visibility.Visible : Visibility.Collapsed;

    public override void Refresh()
    {
        _loading = true;
        var o = Hub.Settings.Overlay;
        _scale.Value = o.Scale;
        _opacity.Value = o.BackOpacity;
        _offsetX.Value = o.X;
        _offsetY.Value = o.Y;
        _colorByValue.IsChecked = o.ColorByValue;
        _labels.IsChecked = o.ShowLabels;
        _shadow.IsChecked = o.Shadow;
        _rounded.IsChecked = o.Rounded;
        foreach (var (key, box) in _items) box.IsChecked = o.Items.Contains(key);
        _accentBox.Text = o.CustomAccent;
        _textBox.Text = o.CustomText;
        _backBox.Text = o.CustomBack;
        _toggleKey.Text = o.ToggleKey;
        _moveKey.Text = o.MoveKey;
        _loading = false;

        _board.IsChecked = Hub.Settings.Overlay.BoardSensors;
        UpdateBoardNote();
        _note.Text = Hub.Elevated
            ? L.T($"میان‌بر Alt+{o.ToggleKey} نمایش و پنهان می‌کنه و Alt+{o.MoveKey} اجازه‌ی جابجایی می‌ده.", $"Alt+{o.ToggleKey} shows or hides it; Alt+{o.MoveKey} lets you drag it.")
            : L.T("برای دیدن فریم بر ثانیه، شب‌پا رو با دسترسی ادمین اجرا کن. بقیه‌ی اطلاعات بدون ادمین هم کار می‌کنن.",
                "Run Shabpa as administrator to see frame rates. Everything else works without it.");
        UpdateMoveButton();
        UpdateVisibility();
        UpdatePreview();
    }

    private void UpdateBoardNote()
    {
        _boardNote.Text = !Hub.Settings.Overlay.BoardSensors ? L.T("خاموشه؛ دمای CPU و فن CPU نشون داده نمی‌شن.", "Off; CPU temperature and CPU fan are not shown.")
            : !Hub.Elevated ? L.T("برای کار کردن شب‌پا رو با دسترسی ادمین اجرا کن.", "Run Shabpa as administrator for this to work.")
            : Hub.Telemetry.BoardProblem switch
            {
                "nodata" => L.T("حسگری خونده نشد؛ درایور PawnIO نصب نیست یا این مادربورد پشتیبانی نمی‌شه.", "No sensor could be read; PawnIO is not installed or this board is not supported."),
                "error" => L.T("خوندن حسگرها خطا داد.", "Reading the sensors failed."),
                _ => L.T("فعال و در حال خوندنه.", "Active and reading."),
            };
    }

    public override void OnSecond()
    {
        if (!IsVisible) return;
        UpdatePreview();
        UpdateBoardNote();
    }

    private void UpdatePreview()
    {
        var o = Hub.Settings.Overlay;
        var now = DateTime.Now;
        // A believable minute of frame-rate history for the graph layout.
        if (_fakeHistory.Count == 0 || now - _fakeHistory[^1].Time > TimeSpan.FromSeconds(1))
        {
            var random = new Random(now.Second);
            _fakeHistory.Add(new TelemetrySample { Time = now, Fps = 150 + random.Next(-18, 18) });
            if (_fakeHistory.Count > 60) _fakeHistory.RemoveAt(0);
            while (_fakeHistory.Count < 60)
                _fakeHistory.Insert(0, new TelemetrySample { Time = now.AddSeconds(-60 + _fakeHistory.Count), Fps = 150 + random.Next(-18, 18) });
        }

        var metrics = OverlayMetrics.Build(OverlayMetrics.Demo(now), o.Items);
        var element = OverlayRenderer.Build(o, metrics, _fakeHistory);
        var host = new Grid { Margin = new Thickness(12) };
        element.HorizontalAlignment = o.Anchor switch { "tr" or "br" => HorizontalAlignment.Right, "tc" or "bc" => HorizontalAlignment.Center, _ => HorizontalAlignment.Left };
        element.VerticalAlignment = o.Anchor is "bl" or "br" or "bc" ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        host.Children.Add(element);
        _previewStage.Child = host;
    }
}
