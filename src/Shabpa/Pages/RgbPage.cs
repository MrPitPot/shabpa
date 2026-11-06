using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Shabpa.Core;
using Shabpa.Kit;
using Switch = Shabpa.Kit.Switch;

namespace Shabpa.Pages;

/// <summary>«نورپردازی»: choose how the lights of the PC look (fans, memory, cooler, strips, motherboard, graphics card, keyboard and mouse), all together or
/// each on its own. Effects are worked out here and sent to a running OpenRGB, which talks to the hardware; the page says plainly when the lights cannot be
/// reached and when another program (MSI Center, iCUE, Armoury Crate...) also drives them, and can close that program.</summary>
public sealed class RgbPage : PageBase
{
    private readonly RgbPreview _preview = new() { Height = 340, MinWidth = 320 };
    private readonly WrapPanel _partChips = new() { Margin = new Thickness(0, 0, 0, 10) };
    private readonly Button _devicesButton = new();
    private readonly Button _gearButton = new();
    private readonly System.Windows.Controls.Primitives.Popup _gearPopup = new() { StaysOpen = false, AllowsTransparency = true, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, PopupAnimation = System.Windows.Controls.Primitives.PopupAnimation.Fade };
    private readonly Border _deviceBanner = new() { Visibility = Visibility.Collapsed, CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 10) };
    private readonly TextBlock _deviceBannerText = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly WrapPanel _direction = new(), _style = new();
    private long _lastDraw;
    private readonly StackPanel _alerts = new();
    private readonly Switch _master = new(), _sync = new();
    private readonly WrapPanel _tabs = new();
    private readonly WrapPanel _basic = new(), _special = new(), _custom = new(), _groupChips = new();
    private readonly WrapPanel _swatches = new();
    private readonly Dictionary<string, Border> _tiles = [];
    private readonly OverlayColorField _colorField = new();
    private Slider _brightness = null!, _speed = null!;
    private readonly StackPanel _editor = new() { Visibility = Visibility.Collapsed };
    private readonly TextBlock _customNote = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly DispatcherTimer _save = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private string _tab = "all";
    private string _group = RgbEffects.GroupCalm;
    private bool _loading, _previewing, _frameQueued, _scanning;
    private long _lastScan;
    private List<(RgbRival rival, List<string> processes, List<string> services)> _rivals = [];

    public override string PageTitle => L.T("نورپردازی (RGB)", "Lighting (RGB)");
    public override string PageSubtitle => L.T("نور فن‌ها، رم، کولر، نوارها و بقیه‌ی قطعه‌ها رو یک‌جا یا جدا از هم تنظیم کن.", "Set the light of the fans, memory, cooler, strips and the rest, all together or one by one.");

    private RgbSettings Cfg => Hub.Settings.Rgb;
    private bool OnDevice => _tab.StartsWith("dev:", StringComparison.Ordinal);
    private RgbZoneSetting Cur => OnDevice ? Cfg.DeviceSetting(_tab[4..]).Look : _tab == "all" || Cfg.Sync ? Cfg.All : Cfg.ZoneSetting(_tab);

    public RgbPage()
    {
        _save.Tick += (_, _) => { _save.Stop(); Hub.Store.Save(); };
        var root = new StackPanel();

        // ---- alerts: can the lights be reached, who else drives them ------------------------------------
        root.Children.Add(_alerts);

        // ---- the main card -------------------------------------------------------------------------------
        var main = new Grid();
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star), MinWidth = 380 });
        var (card, body) = Ui2.Card(L.T("نورپردازی", "Lighting"));
        body.Children.Clear();

        var head = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(new TextBlock { Text = L.T("نورپردازی", "Lighting"), Style = St("H2"), VerticalAlignment = VerticalAlignment.Center });
        _master.Margin = new Thickness(12, 0, 0, 0);
        _master.ToolTip = L.T("وقتی روشنه، شب‌پا نور قطعه‌ها رو کنترل می‌کنه (تا وقتی که اجراست).", "While on, Shabpa drives the lights (as long as it is running).");
        _master.Click += (_, _) => { Cfg.Enabled = _master.IsChecked == true; Changed(blackOutIfOff: true); };
        titleRow.Children.Add(_master);
        head.Children.Add(titleRow);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _devicesButton.Style = St("Btn");
        _devicesButton.Padding = new Thickness(14, 7, 14, 7);
        _devicesButton.Content = L.T("دستگاه‌ها", "Devices");
        _devicesButton.ToolTip = L.T("ببین چه دستگاه‌هایی پیدا شد، تستشون کن و جدا تنظیمشون کن.", "See which devices were found, test them and set them one by one.");
        _devicesButton.Click += (_, _) => RgbDevicesWindow.Open(ControlDevice);
        _gearButton.Style = St("Btn");
        _gearButton.Padding = new Thickness(10, 7, 10, 7);
        _gearButton.Margin = new Thickness(8, 0, 0, 0);
        _gearButton.Content = new Icon("set", 17);
        _gearButton.ToolTip = L.T("تنظیمات نورپردازی: خواب، بازی، افکت روشن و خاموش شدن…", "Lighting options: sleep, games, the on/off effect…");
        _gearButton.Click += (_, _) => OpenGear();
        right.Children.Add(_devicesButton);
        right.Children.Add(_gearButton);
        Grid.SetColumn(right, 1);
        head.Children.Add(right);
        body.Children.Add(head);

        var split = new StackPanel();

        var left = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        left.Children.Add(Ui2.Label(L.T("قطعه‌های وصل‌شده", "Connected parts"), 0, 6));
        left.Children.Add(_partChips);
        left.Children.Add(new Border { Child = _preview, CornerRadius = new CornerRadius(16), ClipToBounds = true, Background = Res("Panel2"), FlowDirection = FlowDirection.LeftToRight });
        _sync.Content = new TextBlock { Text = L.T("همه‌ی نورها یکی باشن (همگام)", "Same look for every light (sync)"), FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.Wrap };
        _sync.Margin = new Thickness(0, 14, 0, 0);
        _sync.Click += (_, _) => { Cfg.Sync = _sync.IsChecked == true; _tab = "all"; Changed(); Rebuild(); };
        left.Children.Add(_sync);
        left.Children.Add(new TextBlock
        {
            Text = L.T("خاموش باشه، برای هر قطعه یه تب جدا باز می‌شه و هر کدوم افکت و رنگ خودش رو داره.", "Off gives every part its own tab with its own effect and colour."),
            Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
        });
        split.Children.Add(left);

        var controls = new StackPanel();
        _deviceBanner.Background = Res("AccentSoft");
        var bannerRow = new DockPanel();
        var bannerBack = new Button { Style = St("Btn"), Content = L.T("برگرد به همه", "Back to all"), Padding = new Thickness(12, 5, 12, 5), VerticalAlignment = VerticalAlignment.Center };
        bannerBack.Click += (_, _) => { _tab = "all"; Rebuild(); };
        DockPanel.SetDock(bannerBack, Dock.Left);
        bannerRow.Children.Add(bannerBack);
        bannerRow.Children.Add(_deviceBannerText);
        _deviceBanner.Child = bannerRow;
        controls.Children.Add(_deviceBanner);
        controls.Children.Add(_tabs);
        controls.Children.Add(Ui2.Label(L.T("افکت", "Effect"), 6, 6));
        controls.Children.Add(_basic);
        controls.Children.Add(Ui2.Label(L.T("افکت‌های ویژه", "Special looks"), 14, 6));
        controls.Children.Add(_groupChips);
        controls.Children.Add(_special);
        controls.Children.Add(Ui2.Label(L.T("افکت‌های من", "My effects"), 14, 6));
        controls.Children.Add(_custom);
        controls.Children.Add(_customNote);
        controls.Children.Add(_editor);
        controls.Children.Add(Ui2.Label(L.T("رنگ", "Colour"), 14, 6));
        var colourRow = _swatches;
        foreach (var hex in RgbEffects.Swatches)
        {
            var dot = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 8, 6), Cursor = System.Windows.Input.Cursors.Hand, BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, Background = new SolidColorBrush(Color.FromRgb(Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..], 16))), Tag = hex };
            dot.MouseLeftButtonUp += (_, _) => PickColor(hex);
            colourRow.Children.Add(dot);
        }
        _colorField.Margin = new Thickness(0, 0, 0, 6);
        _colorField.Picked += hex => PickColor(hex, fromField: true);
        colourRow.Children.Add(_colorField);
        controls.Children.Add(colourRow);
        var bright = Ui2.SliderRow(L.T("روشنایی", "Brightness"), 0, 100, 1, v => $"{Math.Round(v):0}%");
        var speed = Ui2.SliderRow(L.T("سرعت", "Speed"), 1, 10, 1, v => $"{Math.Round(v):0}");
        (_brightness, _speed) = (bright.slider, speed.slider);
        controls.Children.Add(bright.row);
        controls.Children.Add(speed.row);
        controls.Children.Add(Ui2.Label(L.T("جهت", "Direction"), 12, 4));
        controls.Children.Add(_direction);
        controls.Children.Add(Ui2.Label(L.T("حالت", "Style"), 8, 4));
        controls.Children.Add(_style);
        _brightness.ValueChanged += (_, _) => { if (!_loading) { Cur.Brightness = (int)Math.Round(_brightness.Value); Changed(); } };
        _speed.ValueChanged += (_, _) => { if (!_loading) { Cur.Speed = (int)Math.Round(_speed.Value); Changed(); } };
        split.Children.Add(controls);
        body.Children.Add(split);
        root.Children.Add(card);

        // ---- how it works ---------------------------------------------------------------------------------
        var (help, helpBody) = Ui2.Card(L.T("چطور کار می‌کنه؟", "How does it work?"),
            L.T("شب‌پا افکت‌ها رو حساب می‌کنه و به یه سرور نور محلی می‌ده که با سخت‌افزارهای بیشتر از ۱۰۰۰ مدل حرف می‌زنه. اون سرور برنامه‌ی رایگان و متن‌باز OpenRGBه که دکمه‌ی «فعال‌سازی» یک بار از سایت رسمی‌ش دانلودش می‌کنه (فقط همون یک فایل، با اثر انگشت ثابت چک می‌شه) و بی‌صدا و فقط روی همین کامپیوتر اجراش می‌کنه؛ هر وقت نورها رو خاموش کنی اونم بسته می‌شه. لازم نیست خودت چیزی نصب یا باز کنی. چیزی به جایی فرستاده نمی‌شه. اگه برنامه‌ی دیگه‌ای (مثل MSI Center یا iCUE) هم نور رو کنترل کنه، با هم کشمکش می‌کنن؛ بالای صفحه بهت می‌گم و می‌تونی همون‌جا ببندیش.",
                "Shabpa works the effects out and hands them to a local lighting server that speaks to over a thousand kinds of hardware. That server is the free, open-source OpenRGB: the “Enable” button fetches it once from its official site (just that one file, checked against a fixed fingerprint) and runs it silently, on this PC only; when you turn the lights off it stops too. You never install or open anything yourself. Nothing is sent anywhere. If another program (such as MSI Center or iCUE) also drives the lights, the two fight; the top of this page says so and lets you close it right there."));
        helpBody.Children.Add(Ui2.Label("", 0, 0));
        root.Children.Add(help);
        Content = root;

        _preview.Show(zone => Hub.Rgb.FrameOf(zone));
        _preview.ZonePicked += PickZone;
        Loaded += (_, _) => { if (_previewing) return; _previewing = true; Hub.Rgb.PreviewStart(); Hub.Rgb.FrameReady += OnFrame; Hub.Rgb.LinkChanged += OnLinkChanged; };
        Unloaded += (_, _) => { if (!_previewing) return; _previewing = false; Hub.Rgb.FrameReady -= OnFrame; Hub.Rgb.LinkChanged -= OnLinkChanged; Hub.Rgb.PreviewStop(); };
    }

    private void OnFrame()
    {
        if (_frameQueued) return;
        _frameQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _frameQueued = false;
            if (!_previewing || !IsVisible || Window.GetWindow(this) is { IsActive: false }) return;     // nobody looks at it: no drawing
            if (Environment.TickCount64 - _lastDraw < 50) return;                                       // 20 pictures a second are plenty
            _lastDraw = Environment.TickCount64;
            _preview.Show(zone => Hub.Rgb.FrameOf(zone));
        }, DispatcherPriority.Background);
    }

    private void OnLinkChanged() => Dispatcher.BeginInvoke(DrawAlerts);

    // ---- state -> controls ---------------------------------------------------------------------------------------

    public override void Refresh()
    {
        _loading = true;
        _master.IsChecked = Cfg.Enabled;
        _sync.IsChecked = Cfg.Sync;
        _loading = false;
        Rebuild();
        Scan();
        DrawAlerts();
    }

    public override void OnSecond()
    {
        if (!IsVisible) return;
        if (Environment.TickCount64 - _lastScan > 8000) Scan();
        UpdateChip();
    }

    private string _chipSignature = "";

    private void UpdateChip()
    {
        var devices = Hub.Rgb.Devices;
        var n = devices.Count;
        _devicesButton.Content = n > 0 ? L.T($"دستگاه‌ها ({Fa.Digits(n.ToString())})", $"Devices ({n})") : L.T("دستگاه‌ها", "Devices");
        var sig = Hub.Rgb.Link + "|" + string.Join(";", devices.Select(d => d.Key + d.Zone)) + "|" + _tab + "|" + Cfg.Sync;
        if (sig == _chipSignature) return;
        _chipSignature = sig;
        _partChips.Children.Clear();
        var zones = devices.GroupBy(d => Cfg.FindDevice(d.Key) is { Zone.Length: > 0 } z ? z.Zone : d.Zone).ToDictionary(g => g.Key, g => g.Count());
        if (zones.Count == 0)
        {
            _partChips.Children.Add(new TextBlock { Text = L.T("هنوز قطعه‌ای پیدا نشده؛ «دستگاه‌ها» رو بزن.", "No part found yet; press «Devices»."), Style = St("Label"), VerticalAlignment = VerticalAlignment.Center });
            return;
        }
        foreach (var zone in RgbZones.All.Where(z => zones.ContainsKey(z.Key)))
        {
            var key = zone.Key;
            var colour = Hub.Rgb.FrameOf(key) is { Length: > 0 } f ? f[f.Length / 2] : new Rgb(120, 120, 140);
            var dot = new System.Windows.Shapes.Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(Color.FromRgb(colour.R, colour.G, colour.B)), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(dot);
            row.Children.Add(new TextBlock { Text = zone.Name + (zones[key] > 1 ? $" ×{Fa.Digits(zones[key].ToString())}" : ""), FontSize = 12.5, FontWeight = FontWeights.SemiBold });
            var chip = new Border
            {
                Child = row, CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 6, 14, 6), Margin = new Thickness(0, 0, 8, 6), Cursor = System.Windows.Input.Cursors.Hand,
                BorderThickness = new Thickness(1.3), BorderBrush = _tab == key ? Res("Accent") : Res("Line"), Background = _tab == key ? Res("AccentSoft") : Res("Panel2"),
            };
            chip.MouseLeftButtonUp += (_, _) => PickZone(key);
            _partChips.Children.Add(chip);
        }
    }

    private void Rebuild()
    {
        _loading = true;
        Chips(_tabs, Cfg.Sync || OnDevice ? [] : new[] { ("all", L.T("همه", "All")) }.Concat(RgbZones.All.Select(z => (z.Key, z.Name))), _tab, key => { _tab = key; Rebuild(); });
        _tabs.Visibility = Cfg.Sync || OnDevice ? Visibility.Collapsed : Visibility.Visible;
        _deviceBanner.Visibility = OnDevice ? Visibility.Visible : Visibility.Collapsed;
        if (OnDevice)
        {
            var name = Hub.Rgb.Devices.FirstOrDefault(d => d.Key == _tab[4..])?.Name ?? _tab[4..].Split('|').LastOrDefault() ?? "";
            _deviceBannerText.Text = L.T($"داری فقط «{name}» رو تنظیم می‌کنی؛ بقیه همون‌طور که بودن می‌مونن.", $"You are setting only “{name}”; the others stay as they were.");
        }
        _preview.Select(OnDevice ? "" : _tab == "all" || Cfg.Sync ? "" : _tab);
        FillOptions();
        _tiles.Clear();
        _basic.Children.Clear();
        foreach (var e in RgbEffects.Basic) _basic.Children.Add(Tile(e.Key, e.Name, e));
        Chips(_groupChips, RgbEffects.Groups.Select(g => (g, RgbEffects.GroupName(g))), _group, g => { _group = g; FillSpecial(); MarkSelected(); });
        FillSpecial();
        FillCustom();
        _colorField.Set(Cur.Color);
        _brightness.Value = Cur.Brightness;
        _speed.Value = Cur.Speed;
        _loading = false;
        MarkSelected();
        MarkColour();
    }

    private void OpenGear()
    {
        var panel = new StackPanel { Width = 330 };
        Border Row(string label, FrameworkElement control, bool last = false)
        {
            var row = new DockPanel { Margin = new Thickness(14, 10, 14, 10) };
            DockPanel.SetDock(control, Dock.Left);
            row.Children.Add(control);
            row.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left });
            return new Border { Child = row, BorderThickness = new Thickness(0, 0, 0, last ? 0 : 1), BorderBrush = Res("Line") };
        }
        Switch Sw(bool on, Action<bool> set)
        {
            var sw = new Switch { IsChecked = on, VerticalAlignment = VerticalAlignment.Center };
            sw.Click += (_, _) => { set(sw.IsChecked == true); Hub.Store.Save(); Hub.Rgb.Refresh(); };
            return sw;
        }
        panel.Children.Add(Row(L.T("موقع بازی افکت‌ها متوقف بشن", "Pause the effects while a game runs"), Sw(Cfg.PauseInGame, v => Cfg.PauseInGame = v)));
        panel.Children.Add(Row(L.T("موقع خواب سیستم خاموش بشن", "Lights off when the PC sleeps"), Sw(Cfg.OffOnSleep, v => Cfg.OffOnSleep = v)));
        panel.Children.Add(Row(L.T("افکت هنگام شروع شب‌پا و خاموش شدن ویندوز", "An effect when Shabpa starts and Windows shuts down"), Sw(Cfg.StartupEffect, v => Cfg.StartupEffect = v)));
        panel.Children.Add(Row(L.T("برنامه‌های نورپردازی دیگه همیشه بسته بمونن", "Keep other lighting programs closed"), Sw(Cfg.KeepRivalsClosed, v => Cfg.KeepRivalsClosed = v)));
        panel.Children.Add(Row(L.T("افکت‌ها نرم‌تر حرکت کنن", "Smoother movement"), Sw(Cfg.Smooth, v => Cfg.Smooth = v)));
        panel.Children.Add(Row(L.T("رنگ‌های غنی‌تر (اصلاح رنگ برای LED)", "Richer colours (colour correction for LEDs)"), Sw(Cfg.RichColours, v => Cfg.RichColours = v)));

        // the on/off effect: a row that opens a list of its own
        var list = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(10, 0, 10, 8) };
        var current = new TextBlock { Text = RgbTransitions.Name(Cfg.Transition) + "  ▾", FontWeight = FontWeights.SemiBold, FontSize = 13 };
        var picker = new Border { Child = current, CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 6, 12, 6), BorderThickness = new Thickness(1), BorderBrush = Res("Line"), Background = Res("Panel2"), Cursor = System.Windows.Input.Cursors.Hand, MinWidth = 120, VerticalAlignment = VerticalAlignment.Center };
        picker.MouseLeftButtonUp += (_, _) => list.Visibility = list.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        panel.Children.Add(Row(L.T("افکت خاموش و روشن شدن", "Fade in / fade out effect"), picker, last: true));
        foreach (var (key, fa, en) in RgbTransitions.Styles)
        {
            var k = key;
            var item = new Border { Child = new TextBlock { Text = L.T(fa, en), FontSize = 13, FontWeight = Cfg.Transition == key ? FontWeights.Bold : FontWeights.Normal }, Padding = new Thickness(12, 7, 12, 7), CornerRadius = new CornerRadius(8), Cursor = System.Windows.Input.Cursors.Hand, Background = Cfg.Transition == key ? Res("AccentSoft") : Brushes.Transparent };
            item.MouseLeftButtonUp += (_, _) =>
            {
                Cfg.Transition = k;
                Hub.Store.Save();
                current.Text = RgbTransitions.Name(k) + "  ▾";
                list.Visibility = Visibility.Collapsed;
                foreach (var b in list.Children.OfType<Border>()) { var on = ReferenceEquals(b, item); b.Background = on ? Res("AccentSoft") : Brushes.Transparent; ((TextBlock)b.Child).FontWeight = on ? FontWeights.Bold : FontWeights.Normal; }
            };
            list.Children.Add(item);
        }
        panel.Children.Add(list);
        _gearPopup.PlacementTarget = _gearButton;
        _gearPopup.Child = new Border { Child = panel, CornerRadius = new CornerRadius(14), Background = Res("Panel"), BorderBrush = Res("Line"), BorderThickness = new Thickness(1), Margin = new Thickness(0, 8, 0, 0), FlowDirection = Ui.Flow, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, Opacity = 0.4, ShadowDepth = 4 } };
        _gearPopup.IsOpen = true;
    }

    private void FillOptions()
    {
        Chips(_direction, [("fwd", L.T("→ عادی", "→ Forward")), ("rev", L.T("← برعکس", "← Reverse"))], Cur.Reverse ? "rev" : "fwd", k => { Cur.Reverse = k == "rev"; if (!Cfg.Sync && _tab == "all") CopyToZones(Cur); Changed(); });
        Chips(_style, [("flow", L.T("گرادیان (روان)", "Gradient (smooth)")), ("star", L.T("ستاره‌ای", "Starry"))], Cur.Starry ? "star" : "flow", k => { Cur.Starry = k == "star"; if (!Cfg.Sync && _tab == "all") CopyToZones(Cur); Changed(); });
    }

    /// <summary>A part was clicked in the picture (or its chip): from now on it has a look of its own, and its settings are shown.</summary>
    private void PickZone(string zone)
    {
        if (Cfg.Sync) { Cfg.Sync = false; _loading = true; _sync.IsChecked = false; _loading = false; }
        _tab = zone;
        Changed();
        Rebuild();
    }

    /// <summary>The devices window asked for one device to be controlled on its own.</summary>
    private void ControlDevice(string key)
    {
        Cfg.DeviceSetting(key).Own = true;
        _tab = "dev:" + key;
        Changed();
        Rebuild();
    }

    private void FillSpecial()
    {
        _special.Children.Clear();
        foreach (var e in RgbEffects.Special.Where(x => x.Group == _group)) _special.Children.Add(Tile(e.Key, e.Name, e));
    }

    private void FillCustom()
    {
        _custom.Children.Clear();
        foreach (var c in Cfg.Custom)
        {
            var look = new RgbLook(RgbEffects.Basic[0], Rgb.Black, 1, 1, [.. c.Colors.Select(h => Rgb.Parse(h, Rgb.Black))], Enum.TryParse<RgbPattern>(c.Pattern, out var p) ? p : RgbPattern.Wave);
            _custom.Children.Add(Tile("custom:" + c.Id, c.Name.Length > 0 ? c.Name : L.T("بدون نام", "Unnamed"), null, look));
        }
        var add = new Border
        {
            Width = 112, Height = 70, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 8, 8), Cursor = System.Windows.Input.Cursors.Hand,
            BorderThickness = new Thickness(1), BorderBrush = Res("Line"), Background = Res("Panel2"),
            Child = new TextBlock { Text = "+  " + L.T("افکت جدید", "New effect"), Style = St("Label"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        add.MouseLeftButtonUp += (_, _) => OpenEditor(null);
        _custom.Children.Add(add);
        _customNote.Text = Cfg.Custom.Count >= RgbSettings.MaxCustom ? L.T("به سقف افکت‌های شخصی رسیدی؛ یکی رو حذف کن.", "You reached the limit of own effects; delete one.") : "";
    }

    private Border Tile(string key, string name, RgbEffect? effect, RgbLook? look = null)
    {
        // the strip is the effect itself, drawn for a moment of time, so what you pick is what you see
        var stripLook = look ?? new RgbLook(effect!, effect!.UsesColor ? Rgb.Parse(Cur.Color, new Rgb(255, 45, 85)) : Rgb.Black, 1, 1);
        var leds = RgbEngine.Frame(stripLook, 14, 1.3, 0.6);
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        for (var i = 0; i < leds.Length; i++) brush.GradientStops.Add(new GradientStop(Color.FromRgb(leds[i].R, leds[i].G, leds[i].B), i / (double)(leds.Length - 1)));
        var strip = new Border { Height = 22, CornerRadius = new CornerRadius(7), Background = brush, Margin = new Thickness(6, 6, 6, 4) };
        var text = new TextBlock { Text = name, FontSize = 11.5, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(4, 0, 4, 6) };
        var stack = new StackPanel();
        stack.Children.Add(strip);
        stack.Children.Add(text);
        var tile = new Border
        {
            Width = 112, Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1.5), Cursor = System.Windows.Input.Cursors.Hand,
            BorderBrush = Res("Line"), Background = Res("Panel2"), Child = stack, Tag = key, ToolTip = name,
        };
        tile.MouseLeftButtonUp += (_, _) => Pick(key);
        _tiles[key] = tile;
        return tile;
    }

    private void MarkSelected()
    {
        foreach (var (key, tile) in _tiles)
            tile.BorderBrush = key == Cur.Effect ? Res("Accent") : Res("Line");
    }

    private void MarkColour()
    {
        foreach (var dot in _swatches.Children.OfType<Border>())
            dot.BorderBrush = dot.Tag is string hex && string.Equals(hex, Cur.Color, StringComparison.OrdinalIgnoreCase) ? Res("Text") : Brushes.Transparent;
        var usesColour = RgbEffects.Find(Cur.Effect).UsesColor;
        _colorField.IsEnabled = usesColour;
        _colorField.Opacity = usesColour ? 1 : 0.4;
    }

    // ---- actions ---------------------------------------------------------------------------------------------------

    private void Pick(string effect)
    {
        var target = Cur;
        target.Effect = effect;
        if (!Cfg.Sync && _tab == "all") CopyToZones(target);
        Changed();
        MarkSelected();
        MarkColour();
    }

    private void PickColor(string hex, bool fromField = false)
    {
        var target = Cur;
        target.Color = hex.ToUpperInvariant();
        if (!Cfg.Sync && _tab == "all") CopyToZones(target);
        if (!fromField) { _loading = true; _colorField.Set(hex); _loading = false; }
        Changed();
        MarkColour();
        // basic tiles show the chosen colour
        foreach (var e in RgbEffects.Basic)
            if (_tiles.TryGetValue(e.Key, out var tile) && tile.Child is StackPanel { Children: [Border strip, ..] })
            {
                var leds = RgbEngine.Frame(new RgbLook(e, Rgb.Parse(hex, Rgb.Black), 1, 1), 14, 1.3, 0.6);
                var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
                for (var i = 0; i < leds.Length; i++) brush.GradientStops.Add(new GradientStop(Color.FromRgb(leds[i].R, leds[i].G, leds[i].B), i / (double)(leds.Length - 1)));
                strip.Background = brush;
            }
    }

    private void CopyToZones(RgbZoneSetting from)
    {
        foreach (var z in RgbZones.All)
        {
            var s = Cfg.ZoneSetting(z.Key);
            (s.Effect, s.Color, s.Brightness, s.Speed, s.Reverse, s.Starry) = (from.Effect, from.Color, from.Brightness, from.Speed, from.Reverse, from.Starry);
        }
    }

    private void Changed(bool blackOutIfOff = false)
    {
        if (_loading) return;
        if (OnDevice) Cfg.DeviceSetting(_tab[4..]).Own = true;
        _save.Stop();
        _save.Start();
        Hub.Rgb.Refresh();
        if (blackOutIfOff && !Cfg.Enabled) Hub.Rgb.BlackOut();
        DrawAlerts();
    }

    // ---- own effects -----------------------------------------------------------------------------------------------

    private void OpenEditor(RgbCustomEffect? existing)
    {
        if (existing is null && Cfg.Custom.Count >= RgbSettings.MaxCustom) return;
        var working = existing ?? new RgbCustomEffect { Name = L.T("افکت من", "My effect"), Colors = ["FF2D55", "8B5CF6"], Pattern = "Wave" };
        _editor.Children.Clear();
        _editor.Visibility = Visibility.Visible;
        var name = new TextBox { Style = St("Input"), Text = working.Name, MaxLength = 24, Margin = new Thickness(0, 6, 0, 8) };
        _editor.Children.Add(Ui2.Label(L.T("اسم افکت", "Effect name"), 10, 2));
        _editor.Children.Add(name);
        var colours = new WrapPanel();
        void Draw()
        {
            colours.Children.Clear();
            for (var i = 0; i < working.Colors.Count; i++)
            {
                var index = i;
                var field = new OverlayColorField { Margin = new Thickness(0, 0, 8, 6) };
                field.Set(working.Colors[i]);
                field.Picked += hex => working.Colors[index] = hex;
                colours.Children.Add(field);
            }
            var plus = new Button { Style = St("Btn"), Content = "+", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 6, 6), IsEnabled = working.Colors.Count < 5 };
            plus.Click += (_, _) => { working.Colors.Add(working.Colors[^1]); Draw(); };
            var minus = new Button { Style = St("Btn"), Content = "−", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 6, 6), IsEnabled = working.Colors.Count > 1 };
            minus.Click += (_, _) => { working.Colors.RemoveAt(working.Colors.Count - 1); Draw(); };
            colours.Children.Add(plus);
            colours.Children.Add(minus);
        }
        Draw();
        _editor.Children.Add(Ui2.Label(L.T("رنگ‌ها (۱ تا ۵)", "Colours (1 to 5)"), 4, 2));
        _editor.Children.Add(colours);
        var patterns = new WrapPanel();
        _editor.Children.Add(Ui2.Label(L.T("حرکت", "Movement"), 4, 2));
        var choices = new[] { (RgbPattern.Wave, L.T("موج", "Wave")), (RgbPattern.Breathe, L.T("تنفسی", "Breathing")), (RgbPattern.Chase, L.T("تعقیب", "Chase")), (RgbPattern.Cycle, L.T("چرخش", "Cycle")), (RgbPattern.Sparkle, L.T("جرقه", "Sparkle")), (RgbPattern.Meteor, L.T("شهاب", "Meteor")), (RgbPattern.Solid, L.T("ثابت", "Static")), (RgbPattern.Fire, L.T("آتش", "Fire")) };
        Chips(patterns, choices.Select(c => (c.Item1.ToString(), c.Item2)), working.Pattern, v => working.Pattern = v);
        _editor.Children.Add(patterns);
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var save = new Button { Style = St("BtnPrimary"), Content = L.T("ذخیره", "Save"), Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(0, 0, 8, 0) };
        save.Click += (_, _) =>
        {
            working.Name = name.Text.Trim();
            if (existing is null) Cfg.Custom.Add(working);
            Cur.Effect = "custom:" + working.Id;
            if (!Cfg.Sync && _tab == "all") CopyToZones(Cur);
            _editor.Visibility = Visibility.Collapsed;
            Changed();
            Rebuild();
        };
        var cancel = new Button { Style = St("Btn"), Content = L.T("انصراف", "Cancel"), Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => _editor.Visibility = Visibility.Collapsed;
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        if (existing is not null)
        {
            var delete = new Button { Style = St("BtnDanger"), Content = L.T("حذف", "Delete"), Padding = new Thickness(18, 8, 18, 8) };
            delete.Click += (_, _) =>
            {
                if (!Ask(L.T("این افکت حذف بشه؟", "Delete this effect?"), existing.Name, L.T("حذف کن", "Delete"), danger: true)) return;
                Cfg.Custom.Remove(existing);
                foreach (var s in new[] { Cfg.All }.Concat(Cfg.Zones)) if (s.Effect == "custom:" + existing.Id) s.Effect = "static";
                _editor.Visibility = Visibility.Collapsed;
                Changed();
                Rebuild();
            };
            buttons.Children.Add(delete);
        }
        _editor.Children.Add(buttons);
    }

    // ---- alerts ----------------------------------------------------------------------------------------------------

    private void Scan()
    {
        if (_scanning) return;
        _scanning = true;
        _lastScan = Environment.TickCount64;
        _ = Task.Run(() =>
        {
            var found = RgbRivals.Scan();
            Dispatcher.BeginInvoke(() =>
            {
                _scanning = false;
                var before = string.Join("|", _rivals.Select(r => r.rival.Name));
                _rivals = found;
                if (before != string.Join("|", found.Select(r => r.rival.Name))) DrawAlerts();
            });
        });
    }

    private static string? OpenRgbPath()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OpenRGB", "OpenRGB.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "OpenRGB", "OpenRGB.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "OpenRGB", "OpenRGB.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenRGB", "OpenRGB.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private Border Alert(string tint, string title, string text, params (string label, Action click)[] actions)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, FontSize = 14, Foreground = Res(tint), TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(new TextBlock { Text = text, Style = St("Body"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        if (actions.Length > 0)
        {
            var row = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            foreach (var (label, click) in actions)
            {
                var b = new Button { Style = St("Btn"), Content = label, Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(0, 0, 8, 4) };
                b.Click += (_, _) => click();
                row.Children.Add(b);
            }
            stack.Children.Add(row);
        }
        return new Border { Background = Res(tint + "Soft"), CornerRadius = new CornerRadius(14), Padding = new Thickness(18, 14, 18, 12), Margin = new Thickness(0, 0, 0, 12), Child = stack };
    }

    private void DrawAlerts()
    {
        _alerts.Children.Clear();
        UpdateChip();
        foreach (var (rival, processes, services) in _rivals)
        {
            var captured = (rival, processes, services);
            _alerts.Children.Add(Alert("Gold",
                L.T($"«{rival.Name}» هم داره نور رو کنترل می‌کنه", $"“{rival.Name}” is also driving the lights"),
                L.T("دو برنامه که با هم به نورها فرمان بدن، باعث چشمک‌زدن می‌شن یا افکت‌های شب‌پا اصلاً اعمال نمی‌شه. اگه می‌خوای شب‌پا کنترل کنه، اون برنامه رو ببند (بعد از ری‌استارت دوباره خودش بالا میاد، مگه اینکه از استارتاپ ویندوز برش داری).",
                    "Two programs giving orders to the same lights make them flicker or make Shabpa's effects not show at all. To let Shabpa drive them, close that program (it comes back after a restart unless you remove it from Windows startup)."),
                (L.T("بستن این برنامه", "Close this program"), () => CloseRival(captured.rival, captured.processes, captured.services))));
        }
        if (!Cfg.Enabled && !OpenRgbHost.Installed && !_installing)
            _alerts.Children.Add(Alert("Accent", L.T("اول نورپردازی رو فعال کن", "Enable lighting first"),
                L.T("برای کنترل نور قطعه‌ها یک بار روی دکمه‌ی زیر بزن؛ شب‌پا خودش همه‌چیز رو آماده می‌کنه و لازم نیست برنامه‌ی دیگه‌ای نصب یا باز کنی.",
                    "To control the lights of your parts press the button below once; Shabpa prepares everything and you do not need to install or open any other program."),
                (L.T("فعال‌سازی نورپردازی (دانلود ۲۲ مگابایت)", "Enable lighting (22 MB download)"), EnableLighting)));
        if (!Cfg.Enabled) return;
        switch (Hub.Rgb.Link)
        {
            case RgbLink.NoServer:
            {
                var path = OpenRgbPath();
                var actions = new List<(string, Action)>();
                if (!OpenRgbHost.Installed) actions.Add((L.T("فعال‌سازی نورپردازی (دانلود ۲۲ مگابایت)", "Enable lighting (22 MB download)"), EnableLighting));
                else actions.Add((L.T("دوباره راه‌انداز", "Start it again"), () => { OpenRgbHost.Start(Cfg.Port, force: true); Mo.Say(_customNote, L.T("در حال راه‌اندازی؛ چند ثانیه صبر کن.", "Starting; give it a few seconds.")); }));
                if (path is not null && !OpenRgbHost.Installed) actions.Add((L.T("استفاده از OpenRGB خودم", "Use my own OpenRGB"), () => StartOpenRgb(path)));
                _alerts.Children.Add(Alert("Red",
                    L.T("نور به قطعه‌ها نمی‌رسه", "The effects are not reaching the lights"),
                    OpenRgbHost.Installed
                        ? L.T("سرور نور شب‌پا بالا نیومده یا هنوز داره قطعه‌ها رو پیدا می‌کنه. روی بعضی کامپیوترها تا یک دقیقه طول می‌کشه؛ اگه نشد، دکمه‌ی زیر دوباره راهش می‌اندازه.",
                            "Shabpa's lighting server is not up yet or is still finding the parts. On some PCs it takes up to a minute; if it does not come, the button below starts it again.")
                          + (OpenRgbHost.LastExit is { } exit && exit != 0 ? L.T($" (سرور خودش بسته شده؛ کد {Fa.Digits(exit.ToString())})", $" (the server closed by itself; code {exit})") : "")
                        : L.T("برای حرف زدن با قطعه‌ها شب‌پا یه سرور نور کوچیک لازم داره. با یک کلیک خودش می‌گیره‌ش و بی‌صدا اجراش می‌کنه؛ لازم نیست چیزی نصب کنی.",
                            "To talk to the parts Shabpa needs a small lighting server. One click and it fetches and runs it silently; you install nothing."),
                    [.. actions]));
                break;
            }
            case RgbLink.NeedsRuntime:
                if (OpenRgbHost.Blocked() == "windows")
                    _alerts.Children.Add(Alert("Red", L.T("ویندوزت برای نورپردازی خیلی قدیمیه", "Your Windows is too old for lighting"),
                        L.T("سرور نور به ویندوز ۱۰ نسخه‌ی ۱۸۰۹ (ساخت ۱۷۷۶۳) یا جدیدتر نیاز داره. ویندوز رو آپدیت کن و دوباره امتحان کن.",
                            "The lighting server needs Windows 10 version 1809 (build 17763) or newer. Update Windows and try again.")));
                else
                    _alerts.Children.Add(Alert("Red", L.T("یه فایل ویندوز کمه", "A Windows component is missing"),
                        L.T("سرور نور به بسته‌ی رایگان «Microsoft Visual C++ Redistributable» نیاز داره که روی این کامپیوتر نصب نیست (روی ویندوز ۱۰ تمیز معمولاً نیست). از سایت خود مایکروسافت دانلودش کن و نصب کن، بعد برگرد؛ شب‌پا خودش دوباره وصل می‌شه.",
                            "The lighting server needs the free “Microsoft Visual C++ Redistributable”, which is not installed on this PC (a clean Windows 10 often lacks it). Download it from Microsoft's own site and install it, then come back; Shabpa connects again by itself."),
                        (L.T("دانلود از سایت مایکروسافت", "Download from Microsoft"), () =>
                        {
                            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://aka.ms/vs/17/release/vc_redist.x64.exe") { UseShellExecute = true }); }
                            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { /* no browser */ }
                        })));
                break;
            case RgbLink.NoDevices:
                _alerts.Children.Add(Alert("Gold", L.T("OpenRGB هیچ نوری پیدا نکرده", "OpenRGB found no lights"),
                    L.T("به سرور وصلم ولی دستگاهی نمی‌بینه. توی OpenRGB چک کن قطعه‌ها شناسایی شدن (بعضی مادربوردها اول باید برنامه‌ی سازنده بسته بشه تا نشون داده بشن).",
                        "Connected to the server, but it sees no device. Check in OpenRGB that your parts are detected (some boards only show up after the maker's program is closed).")));
                break;
            case RgbLink.Connecting:
                _alerts.Children.Add(Alert("Accent", L.T("در حال وصل شدن به OpenRGB…", "Connecting to OpenRGB…"), ""));
                break;
        }
    }

    private bool _installing;

    private async void EnableLighting()
    {
        if (_installing) return;
        if (!Ask(L.T("نورپردازی فعال بشه؟", "Enable lighting?"),
                L.T("شب‌پا یک بار برنامه‌ی رایگان و متن‌باز OpenRGB (نسخه‌ی ۱٫۱، حدود ۲۲ مگابایت) رو از سایت رسمی‌ش دانلود می‌کنه، با اثر انگشت ثابتِ توی برنامه چک می‌کنه و توی پوشه‌ی خودش بی‌صدا، فقط روی همین کامپیوتر اجرا می‌کنه. هیچ چیز دیگه‌ای نصب یا فرستاده نمی‌شه. بعضی قطعه‌ها (مثل رم و مادربرد) علاوه بر این به درایور جداگانه‌ای به اسم PawnIO نیاز دارن؛ کیبورد، ماوس، فن‌ها و خیلی از قطعه‌های USB بدون اون کار می‌کنن.",
                    "Shabpa will fetch the free, open-source OpenRGB (version 1.1, about 22 MB) once from its official site, check it against the fixed fingerprint inside the program and run it silently in its own folder, on this PC only. Nothing else is installed or sent. Some parts (such as memory and the motherboard) additionally need a separate driver called PawnIO; keyboards, mice, fans and many USB parts work without it."),
                L.T("دانلود و فعال کن", "Download and enable"))) return;
        _installing = true;
        var progress = new Progress<int>(p => Mo.Say(_customNote, L.T($"در حال دانلود… {Fa.Digits(p.ToString())}٪", $"Downloading… {p}%")));
        var result = await OpenRgbHost.InstallAsync(progress);
        _installing = false;
        if (result.Ok)
        {
            Cfg.Enabled = true;
            Hub.Store.Save();
            Hub.Rgb.Refresh();
            _master.IsChecked = true;
            Mo.Say(_customNote, L.T("آماده شد؛ سرور نور داره بالا میاد (تا یک دقیقه) و بعد قطعه‌ها وصل می‌شن.", "Done; the lighting server is starting (up to a minute) and then the parts connect."));
        }
        else
            Mo.Say(_customNote, result.Error switch
            {
                "hash" => L.T("فایل دانلودشده اون فایل رسمیِ مورد انتظار نبود و رد شد؛ چیزی نصب نشد.", "The downloaded file was not the expected official one and was refused; nothing was installed."),
                "download" => L.T("دانلود نشد؛ اینترنت یا دسترسی به سایت OpenRGB رو چک کن و دوباره امتحان کن.", "The download failed; check your internet or access to the OpenRGB site and try again."),
                _ => L.T("باز کردن فایل دانلودشده انجام نشد؛ دوباره امتحان کن.", "The downloaded file could not be unpacked; try again."),
            });
        DrawAlerts();
    }

    private void StartOpenRgb(string path)
    {
        try
        {
            LauncherDetector.Open(path);                       // normal rights, in its own folder
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            Mo.Say(_customNote, L.T("اجرا نشد؛ OpenRGB رو خودت باز کن و Start Server رو بزن.", "It did not start; open OpenRGB yourself and press Start Server."));
        }
    }

    private void CloseRival(RgbRival rival, List<string> processes, List<string> services)
    {
        var detail = string.Join("، ", processes.Concat(services));
        if (!Ask(L.T($"«{rival.Name}» بسته بشه؟", $"Close “{rival.Name}”?"),
                L.T("این برنامه (و سرویس‌هاش) الان بسته می‌شن تا شب‌پا بتونه نور رو کنترل کنه. تنظیمات خودش پاک نمی‌شه و بعداً دوباره بازش می‌کنی.", "This program (and its services) are closed now so Shabpa can drive the lights. Its own settings are not touched and you can open it again later."),
                L.T("ببند", "Close it"), detail: detail)) return;
        _ = Task.Run(() =>
        {
            var n = RgbRivals.Close(rival, processes, services);
            Dispatcher.BeginInvoke(() =>
            {
                Scan();
                Mo.Say(_customNote, n > 0 ? L.T("بسته شد.", "Closed.") : L.T("بسته نشد؛ شاید ادمین لازم باشه (شب‌پا رو با ادمین اجرا کن) یا برنامه محافظت‌شده‌ست.", "It did not close; it may need administrator rights (run Shabpa as administrator) or be protected."));
            });
        });
    }
}
