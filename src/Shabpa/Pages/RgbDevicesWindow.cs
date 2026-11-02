using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Shabpa.Core;
using Shabpa.Kit;
using Switch = Shabpa.Kit.Switch;

namespace Shabpa.Pages;

/// <summary>«دستگاه‌های پیدا‌شده»: everything the lighting server found, one card each: its name, what it is, how many LEDs it has and where it is plugged in; a
/// switch to leave it alone, the part of the PC it is driven as, a «test» that blinks it white so the person sees which one it is, a button to give it a look of its own,
/// and a size for every header whose number of LEDs can be set (an addressable header that is left at 0 lights nothing, which is the usual reason a fan stays dark).
/// Under the list it says plainly what is missing and why (memory and the motherboard need the PawnIO driver and administrator rights).</summary>
public sealed class RgbDevicesWindow : Window
{
    private static RgbDevicesWindow? _open;

    /// <summary>Opens the window (or brings the open one forward). <paramref name="control"/> is called with a device's key when the person wants it controlled on its own.</summary>
    public static void Open(Action<string> control)
    {
        if (_open is { IsLoaded: true }) { _open.Activate(); return; }
        _open = new RgbDevicesWindow(control);
        if (MainOrNull() is { } main) _open.Owner = main;
        _open.Show();
    }

    private readonly Action<string> _control;
    private readonly StackPanel _list = new();
    private readonly TextBlock _status = new() { Style = (Style)Application.Current.Resources["Body"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 14) };
    private readonly TextBlock _note = new() { Style = (Style)Application.Current.Resources["Tip"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private string _signature = "";
    private int _dragging;                                   // a header size is being typed: do not redraw under the person's hands

    /// <summary>The main window when it is shown and belongs to this thread (anything else: no owner, the dialog simply centres on the screen).</summary>
    private static Window? MainOrNull()
    {
        try { return Application.Current?.MainWindow is { IsVisible: true } m ? m : null; }
        catch (InvalidOperationException) { return null; }
    }

    private static Hub Hub => Hub.I;

    private RgbDevicesWindow(Action<string> control)
    {
        _control = control;
        FlowDirection = Ui.Flow;
        Title = L.T("دستگاه‌های نورپردازی", "Lighting devices");
        Width = 760;
        Height = 700;
        MinWidth = 520;
        MinHeight = 420;
        ShowInTaskbar = false;
        WindowStartupLocation = MainOrNull() is not null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        Background = (Brush)Application.Current.Resources["Bg"];
        FontFamily = (FontFamily)Application.Current.Resources["Ui"];
        UseLayoutRounding = true;
        SourceInitialized += (_, _) => Ui.Chrome(this);
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };

        var root = new StackPanel { Margin = new Thickness(26, 22, 26, 20) };
        root.Children.Add(new TextBlock { Text = L.T("دستگاه‌هایی که پیدا شد", "Devices that were found"), FontSize = 22, FontWeight = FontWeights.Bold, Foreground = (Brush)Application.Current.Resources["Text"] });
        root.Children.Add(_status);
        root.Children.Add(_list);
        root.Children.Add(_note);
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        Hub.Rgb.PreviewStart();                              // the loop runs (and connects) while this window is open, even with the lights off in Shabpa
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Closed += (_, _) => { _timer.Stop(); Hub.Rgb.PreviewStop(); _open = null; };
        Refresh(true);
    }

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    private static string TypeName(int type) => type switch
    {
        0 => L.T("مادربرد", "Motherboard"), 1 => L.T("رم", "Memory"), 2 => L.T("کارت گرافیک", "Graphics card"), 3 => L.T("خنک‌کننده / فن", "Cooler / fan"),
        4 => L.T("نوار ال‌ای‌دی", "LED strip"), 5 => L.T("کیبورد", "Keyboard"), 6 => L.T("موس", "Mouse"), 7 => L.T("پد موس", "Mouse mat"), 8 => L.T("هدست", "Headset"),
        9 => L.T("پایه‌ی هدست", "Headset stand"), 10 => L.T("دسته‌ی بازی", "Gamepad"), 11 => L.T("چراغ", "Light"), 12 => L.T("اسپیکر", "Speaker"),
        14 => L.T("حافظه‌ی ذخیره‌سازی", "Storage"), 15 => L.T("کیس", "Case"), 16 => L.T("میکروفون", "Microphone"), 18 => L.T("کی‌پد", "Keypad"), 19 => L.T("لپ‌تاپ", "Laptop"),
        _ => L.T("لوازم جانبی", "Accessory"),
    };

    private void Refresh(bool force = false)
    {
        if (_dragging > 0) return;
        var devices = Hub.Rgb.Devices;
        var rgb = Hub.Settings.Rgb;
        var sig = $"{Hub.Rgb.Link}|{string.Join(";", devices.Select(d => $"{d.Key}:{d.Leds}:{string.Join(",", d.Zones.Select(z => z.Count))}"))}|{string.Join(";", rgb.Devices.Select(d => $"{d.Key}{d.Enabled}{d.Zone}{d.Own}"))}";
        if (!force && sig == _signature) return;
        _signature = sig;

        _status.Text = Hub.Rgb.Link switch
        {
            RgbLink.Sending => L.T($"{Fa.Digits(devices.Count.ToString())} دستگاه پیدا شد و نور بهشون می‌رسه.", $"{devices.Count} device(s) found and the light reaches them."),
            RgbLink.NoDevices => L.T("به سرور نور وصلم ولی هیچ دستگاهی نمی‌بینه (پایین ببین چرا).", "Connected to the lighting server but it sees no device (see below why)."),
            RgbLink.Connecting => L.T("در حال وصل شدن به سرور نور…", "Connecting to the lighting server…"),
            _ => devices.Count > 0
                ? L.T($"{Fa.Digits(devices.Count.ToString())} دستگاه پیدا شد (نورپردازی توی شب‌پا خاموشه؛ برای «تست» لازم نیست روشن باشه).", $"{devices.Count} device(s) found (lighting is off in Shabpa; «Test» works anyway).")
                : OpenRgbHost.Installed
                    ? L.T("سرور نور داره بالا میاد؛ بار اول تا یک دقیقه طول می‌کشه. این پنجره خودش به‌روز می‌شه.", "The lighting server is starting; the first time it takes up to a minute. This window updates itself.")
                    : L.T("هنوز سرور نور فعال نشده؛ توی صفحه‌ی نورپردازی روی «فعال‌سازی» بزن.", "The lighting server is not enabled yet; press «Enable» on the Lighting page."),
        };

        _list.Children.Clear();
        foreach (var device in devices) _list.Children.Add(Card(device, rgb));
        _note.Text = Missing(devices);
    }

    private string Missing(IReadOnlyList<RgbDevice> devices)
    {
        var lines = new List<string>
        {
            L.T("چرا یه قطعه پیدا نمی‌شه؟", "Why is a part missing?"),
            L.T("• رم، مادربرد و بعضی کارت‌های گرافیک فقط با درایور امضاشده‌ی PawnIO و دسترسی ادمین دیده می‌شن. شب‌پا الان " + (Hub.Elevated ? "با ادمین اجرا شده؛ فقط درایور PawnIO لازمه." : "با دسترسی عادی اجرا شده؛ برای این قطعه‌ها باید با ادمین اجراش کنی و PawnIO رو نصب کنی."),
                "• Memory, the motherboard and some graphics cards are only seen with the signed PawnIO driver and administrator rights. Shabpa is now running " + (Hub.Elevated ? "as administrator; only the PawnIO driver is needed." : "with normal rights; for those parts run it as administrator and install PawnIO.")),
            L.T("• فن و کولر USB (مثل iCUE، NZXT CAM، Lian Li) فقط وقتی دیده می‌شن که برنامه‌ی خودشون بسته باشه؛ اون برنامه دستگاه رو قفل می‌کنه.", "• USB fans and coolers (iCUE, NZXT CAM, Lian Li...) show up only while their own program is closed; it locks the device."),
            L.T("• فن‌های وصل‌شده به هدر آدرس‌پذیر مادربرد تا اندازه‌ی هدر رو ندی (بالا، «خروجی‌ها») روشن نمی‌شن.", "• Fans on the board's addressable header stay dark until you give the header its size (the «outputs» above."),
        };
        if (devices.Count == 0) lines.Add(L.T("• اگه سرور نور تازه بالا اومده، چند ثانیه صبر کن.", "• If the server has just started, give it a few seconds."));
        return string.Join("\n", lines);
    }

    private FrameworkElement Card(RgbDevice device, RgbSettings rgb)
    {
        var setting = rgb.FindDevice(device.Key);
        var enabled = setting?.Enabled ?? true;
        var card = new Border
        {
            CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 10), BorderThickness = new Thickness(1),
            Background = Res("Panel"), BorderBrush = Res("Line"), Opacity = enabled ? 1 : 0.6,
        };
        var stack = new StackPanel();
        card.Child = stack;

        // title row: name and facts on one side, the switch on the other
        var head = new DockPanel();
        var toggle = new Switch { IsChecked = enabled, VerticalAlignment = VerticalAlignment.Center, ToolTip = L.T("خاموش باشه، شب‌پا به این دستگاه دست نمی‌زنه.", "Off: Shabpa does not touch this device.") };
        toggle.Click += (_, _) => { rgb.DeviceSetting(device.Key).Enabled = toggle.IsChecked == true; Hub.Store.Save(); Refresh(true); };
        DockPanel.SetDock(toggle, Dock.Left);
        head.Children.Add(toggle);
        var titles = new StackPanel();
        titles.Children.Add(new TextBlock { Text = device.Name, FontWeight = FontWeights.Bold, FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Left });
        var facts = $"{TypeName(device.Type)}  ·  {Fa.Digits(device.Leds.ToString())} {L.T("ال‌ای‌دی", "LEDs")}" + (device.Vendor.Length > 0 ? $"  ·  {device.Vendor}" : "");
        titles.Children.Add(new TextBlock { Text = facts, Style = (Style)Application.Current.Resources["Label"], TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Left });
        if (device.Location.Length > 0 || device.Description.Length > 0)
            titles.Children.Add(new TextBlock { Text = string.Join("  ·  ", new[] { device.Description, device.Location }.Where(x => x.Length > 0)), Style = (Style)Application.Current.Resources["Label"], FontSize = 11, Opacity = .7, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Left, FlowDirection = FlowDirection.LeftToRight });
        head.Children.Add(titles);
        stack.Children.Add(head);

        // which part of the PC it is driven as
        var zoneNow = setting is { Zone.Length: > 0 } ? setting.Zone : device.Zone;
        var zones = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        zones.Children.Add(new TextBlock { Text = L.T("به‌عنوان:", "Driven as:"), Style = (Style)Application.Current.Resources["Label"], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        foreach (var z in RgbZones.All)
        {
            var on = z.Key == zoneNow;
            var chip = new Border
            {
                CornerRadius = new CornerRadius(9), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 4), Cursor = System.Windows.Input.Cursors.Hand, BorderThickness = new Thickness(1.3),
                BorderBrush = on ? Res("Accent") : Res("Line"), Background = on ? Res("AccentSoft") : Res("Panel2"),
                Child = new TextBlock { Text = z.Name, FontSize = 12, FontWeight = on ? FontWeights.Bold : FontWeights.Normal },
            };
            var key = z.Key;
            chip.MouseLeftButtonUp += (_, _) => { rgb.DeviceSetting(device.Key).Zone = key == device.Zone ? "" : key; Hub.Store.Save(); Refresh(true); };
            zones.Children.Add(chip);
        }
        stack.Children.Add(zones);

        // outputs whose size can be set
        foreach (var zone in device.Zones.Where(z => z.Resizable)) stack.Children.Add(SizeRow(device, zone));

        // a device with a few LEDs that looks out of step as a gradient can show one colour
        var sync = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var one = new Switch { IsChecked = setting is { OneColour: true }, VerticalAlignment = VerticalAlignment.Center, ToolTip = L.T("همه‌ی ال‌ای‌دی‌های این دستگاه یک رنگ باشن؛ برای قطعه‌هایی مثل کولر که چند ال‌ای‌دی کم دارن و توی گرادیان از بقیه جدا به نظر میان.", "All the LEDs of this device show one colour; for parts such as a cooler with few LEDs that look out of step as a gradient.") };
        one.Click += (_, _) => { rgb.DeviceSetting(device.Key).OneColour = one.IsChecked == true; Hub.Store.Save(); };
        DockPanel.SetDock(one, Dock.Left);
        sync.Children.Add(one);
        sync.Children.Add(new TextBlock { Text = L.T("تک‌رنگ: همه‌ی ال‌ای‌دی‌های این دستگاه یک رنگ باشن", "One colour: all the LEDs of this device show one colour"), Style = (Style)Application.Current.Resources["Label"], TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left });
        stack.Children.Add(sync);

        // actions
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var test = new Button { Style = (Style)Application.Current.Resources["Btn"], Content = L.T("تست (چشمک سفید)", "Test (white blink)"), Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        test.Click += (_, _) => Hub.Rgb.Identify(device.Index);
        actions.Children.Add(test);
        var own = new Button { Style = (Style)Application.Current.Resources["Btn"], Content = setting is { Own: true } ? L.T("تنظیم جدا (روشن)", "Own look (on)") : L.T("جدا تنظیمش کن", "Control it separately"), Padding = new Thickness(14, 6, 14, 6) };
        own.Click += (_, _) => { _control(device.Key); Close(); };
        actions.Children.Add(own);
        if (setting is { Own: true })
        {
            var back = new Button { Style = (Style)Application.Current.Resources["Btn"], Content = L.T("برگرده به حالت گروه", "Back to the group look"), Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(8, 0, 0, 0) };
            back.Click += (_, _) => { setting.Own = false; Hub.Store.Save(); Refresh(true); };
            actions.Children.Add(back);
        }
        stack.Children.Add(actions);
        return card;
    }

    private FrameworkElement SizeRow(RgbDevice device, RgbZoneInfo zone)
    {
        var row = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var label = new TextBlock
        {
            Text = L.T($"خروجی «{zone.Name}»: اندازه‌ی هدر ({Fa.Digits(zone.Min.ToString())} تا {Fa.Digits(zone.Max.ToString())} ال‌ای‌دی). خودکار روی حداکثر ({Fa.Digits(zone.Max.ToString())}) می‌مونه، مگه خودت عددی بزنی.",
                $"Output “{zone.Name}”: header size ({zone.Min} to {zone.Max} LEDs). It stays at the maximum ({zone.Max}) by itself unless you type a number."),
            Style = (Style)Application.Current.Resources["Label"], VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Left,
        };
        var box = new TextBox { Style = (Style)Application.Current.Resources["Input"], Text = zone.Count.ToString(), Width = 96, Margin = new Thickness(8, 0, 8, 0), FlowDirection = FlowDirection.LeftToRight, VerticalAlignment = VerticalAlignment.Center };
        box.GotKeyboardFocus += (_, _) => _dragging++;
        box.LostKeyboardFocus += (_, _) => _dragging = Math.Max(0, _dragging - 1);
        var apply = new Button { Style = (Style)Application.Current.Resources["Btn"], Content = L.T("اعمال", "Apply"), Padding = new Thickness(12, 5, 12, 5), VerticalAlignment = VerticalAlignment.Center };
        apply.Click += (_, _) =>
        {
            if (!int.TryParse(box.Text.Trim(), out var n)) return;
            n = Math.Clamp(n, zone.Min, zone.Max);
            box.Text = n.ToString();
            Hub.Settings.Rgb.ZoneSizes[RgbService.SizeKey(device, zone)] = n;       // remembered: the automatic maximum does not undo it
            Hub.Store.Save();
            Hub.Rgb.ResizeZone(device.Index, zone.Index, n, ok => Dispatcher.BeginInvoke(() =>
            {
                _status.Text = ok ? L.T("اندازه‌ی هدر عوض شد.", "The header size was changed.") : L.T("دستگاه اندازه رو قبول نکرد.", "The device did not accept the size.");
                Refresh(true);
            }));
        };
        DockPanel.SetDock(apply, Dock.Left);
        DockPanel.SetDock(box, Dock.Left);
        row.Children.Add(apply);
        row.Children.Add(box);
        row.Children.Add(label);
        return row;
    }
}
