using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Shabpa.Core;

namespace Shabpa.Pages;

/// <summary>«نورپردازی»: one place for the RGB lights of the PC (through OpenRGB): ready-made looks and palettes, your own
/// colours, speed and brightness, and a choice per device. The preview below the looks is drawn by the same code that
/// drives the real lights.</summary>
public sealed class RgbPage : PageBase
{
    private const int PreviewLeds = 28;

    private readonly TextBlock _status = new() { Style = St("Body"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly CheckBox _enabled = new() { Style = St("Check"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
    private readonly WrapPanel _effects = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly WrapPanel _palettes = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly StackPanel _custom = new();
    private readonly WrapPanel _colorBoxes = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly StackPanel _devices = new();
    private readonly Rectangle[] _preview = new Rectangle[PreviewLeds];
    private readonly DispatcherTimer _frame = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private readonly DateTime _started = DateTime.UtcNow;
    private Slider _speed = null!, _bright = null!;
    private bool _loading;

    public override string PageTitle => L.T("نورپردازی RGB", "RGB lighting");
    public override string PageSubtitle => L.T("نور قطعاتی که RGB دارن رو با هم کنترل کن: طرح‌های آماده، رنگ دلخواه و تنظیم هر دستگاه.",
        "Control every lit part together: ready-made looks, your own colours and a choice per device.");

    private static RgbSettings S => Hub.Settings.Rgb;

    public RgbPage()
    {
        var root = new StackPanel();

        // ---- connection -----------------------------------------------------------------------------
        var (connectCard, connect) = Ui2.Card(L.T("اتصال به OpenRGB", "Connection to OpenRGB"),
            L.T("شب‌پا خودش به سخت‌افزار نور دست نمی‌زنه؛ از برنامه‌ی رایگان و متن‌باز OpenRGB (که از بیشتر کیبورد، ماوس، فن، رم و مادربوردها پشتیبانی می‌کنه) رنگ می‌خواد. ارتباط فقط روی همین کامپیوتره.",
                "Shabpa does not touch the lighting hardware itself; it asks the free, open-source OpenRGB (which supports most keyboards, mice, fans, RAM and motherboards) to show the colours. The link stays on this PC."));
        _enabled.Content = L.T("نورپردازی شب‌پا روشن باشه", "Shabpa lighting on");
        _enabled.Click += (_, _) =>
        {
            if (_loading) return;
            S.Enabled = _enabled.IsChecked == true;
            Hub.Store.Save();
            Hub.ApplyRgb();
            ShowStatus();
        };
        connect.Children.Add(_enabled);
        connect.Children.Add(_status);
        var steps = new TextBlock
        {
            Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0),
            Text = L.T("راه‌اندازی: ۱) OpenRGB رو از openrgb.org نصب و باز کن. ۲) تب SDK Server رو بزن و Start Server. ۳) این‌جا «نورپردازی» رو روشن کن. اگه iCUE یا Armoury Crate/Aura بازه، اون‌ها هم سخت‌افزار رو قفل می‌کنن؛ برای دستگاه‌های همون برند بهتره بسته باشن.",
                "Setup: 1) install and open OpenRGB from openrgb.org. 2) Open its SDK Server tab and press Start Server. 3) Switch “Shabpa lighting” on here. If iCUE or Armoury Crate/Aura is open they also hold the hardware; close them for devices of that brand."),
        };
        connect.Children.Add(steps);
        var open = new Button { Style = St("Btn"), Content = L.T("باز کردن openrgb.org", "Open openrgb.org"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        open.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("https://openrgb.org") { UseShellExecute = true }); }
            catch (Exception ex) { Log.Write($"open url: {ex.GetType().Name}"); }
        };
        connect.Children.Add(open);
        root.Children.Add(connectCard);

        // ---- look -------------------------------------------------------------------------------------
        var (lookCard, look) = Ui2.Card(L.T("طرح و رنگ", "Look and colours"));
        look.Children.Add(Ui2.Label(L.T("طرح", "Effect"), 8, 0));
        look.Children.Add(_effects);
        look.Children.Add(Ui2.Label(L.T("رنگ‌ها", "Palette"), 10, 0));
        look.Children.Add(_palettes);
        _custom.Children.Add(Ui2.Label(L.T("رنگ‌های خودت (شش رقم هگز، تا شش رنگ؛ مثلاً 3EE6A0):", "Your colours (6 hex digits, up to six; for example 3EE6A0):"), 8, 0));
        _custom.Children.Add(_colorBoxes);
        look.Children.Add(_custom);
        var speed = Ui2.SliderRow(L.T("سرعت", "Speed"), 0, 100, 5, v => Fa.Digits(((int)v).ToString()) + "%");
        var bright = Ui2.SliderRow(L.T("روشنایی", "Brightness"), 0, 100, 5, v => Fa.Digits(((int)v).ToString()) + "%");
        (_speed, _bright) = (speed.slider, bright.slider);
        speed.row.Margin = new Thickness(0, 14, 0, 0);
        look.Children.Add(speed.row);
        look.Children.Add(bright.row);
        _speed.ValueChanged += (_, _) => { if (!_loading) { S.Speed = _speed.Value / 100; Hub.Store.Save(); } };
        _bright.ValueChanged += (_, _) => { if (!_loading) { S.Brightness = _bright.Value / 100; Hub.Store.Save(); } };

        var strip = new UniformGrid { Rows = 1, Columns = PreviewLeds, Height = 26, Margin = new Thickness(0, 14, 0, 0), FlowDirection = FlowDirection.LeftToRight };
        for (var i = 0; i < PreviewLeds; i++)
        {
            _preview[i] = new Rectangle { Margin = new Thickness(1.5, 0, 1.5, 0), RadiusX = 4, RadiusY = 4, Fill = Brushes.Black };
            strip.Children.Add(_preview[i]);
        }
        look.Children.Add(new Border { Child = strip, Background = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), CornerRadius = new CornerRadius(10), Padding = new Thickness(8), Margin = new Thickness(0, 10, 0, 0) });
        var off = new Button { Style = St("Btn"), Content = L.T("همه‌ی نورها خاموش", "All lights off"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
        off.Click += (_, _) => { S.Effect = "off"; Hub.Store.Save(); Refresh(); };
        look.Children.Add(off);
        root.Children.Add(lookCard);

        // ---- devices ---------------------------------------------------------------------------------
        var (deviceCard, device) = Ui2.Card(L.T("دستگاه‌ها", "Devices"),
            L.T("هر دستگاه یا از طرح عمومی پیروی می‌کنه، یا خاموشه، یا یه رنگ ثابت داره. دستگاهی که تیکش رو برداری اصلاً دست‌کاری نمی‌شه.",
                "Each device either follows the general look, is off, or shows one fixed colour. A device you untick is left alone."));
        device.Children.Add(_devices);
        root.Children.Add(deviceCard);
        Content = root;

        _frame.Tick += (_, _) => DrawPreview();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) _frame.Start();
            else _frame.Stop();
        };
    }

    private void DrawPreview()
    {
        var s = S;
        var ring = RgbEffects.PaletteOf(s);
        var seconds = (DateTime.UtcNow - _started).TotalSeconds;
        var load = Hub.Snapshot.CpuLoad / 100.0;
        for (var i = 0; i < PreviewLeds; i++)
        {
            var c = RgbEffects.Pixel(s, ring, seconds, i, PreviewLeds, load, Hub.Snapshot.GameRunning);
            _preview[i].Fill = new SolidColorBrush(Color.FromRgb(c.r, c.g, c.b));
        }
    }

    private void ShowStatus()
    {
        _status.Text = !S.Enabled
            ? L.T("خاموشه. تیک بالا رو بزن تا به OpenRGB وصل بشه.", "Off. Tick the box above to connect to OpenRGB.")
            : Hub.Rgb.Connected ? Hub.Rgb.Status
            : Hub.Rgb.Status.Length > 0 ? Hub.Rgb.Status
            : L.T("در حال اتصال…", "Connecting…");
    }

    public override void OnSecond()
    {
        if (!IsVisible) return;
        ShowStatus();
        if (Hub.Rgb.Devices.Count != _shownDevices) ShowDevices();
    }

    private int _shownDevices = -1;

    private void ShowDevices()
    {
        var list = Hub.Rgb.Devices;
        _shownDevices = list.Count;
        _devices.Children.Clear();
        if (list.Count == 0)
        {
            _devices.Children.Add(Ui2.Label(L.T("هنوز دستگاهی دیده نشده. OpenRGB باز و سرور SDK روشن باشه.", "No device seen yet. OpenRGB must be open with its SDK server running."), 8, 0));
            return;
        }
        foreach (var d in list)
        {
            var name = d.Name;
            var choice = S.Devices.FirstOrDefault(x => x.Name == name);
            var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 8) };
            var head = new CheckBox
            {
                Style = St("Check"), Content = $"{d.Name}  ·  {(d.Vendor.Length > 0 ? d.Vendor + " · " : "")}{Fa.Digits(d.Leds.ToString())} LED", IsChecked = !S.Ignored.Contains(name),
                HorizontalAlignment = HorizontalAlignment.Left, FlowDirection = FlowDirection.LeftToRight,
            };
            head.Click += (_, _) =>
            {
                S.Ignored.Remove(name);
                if (head.IsChecked != true) S.Ignored.Add(name);
                Hub.Store.Save();
            };
            panel.Children.Add(head);
            var chips = new WrapPanel { Margin = new Thickness(26, 4, 0, 0) };
            var box = new TextBox { Style = St("Input"), Width = 90, FlowDirection = FlowDirection.LeftToRight, FontFamily = new FontFamily("Consolas"), Text = choice?.Color ?? "FFFFFF", Margin = new Thickness(0, 0, 8, 6) };
            Chips(chips, [("follow", L.T("طرح عمومی", "General look")), ("off", L.T("خاموش", "Off")), ("color", L.T("رنگ ثابت", "Fixed colour"))], choice?.Mode ?? "follow", mode =>
            {
                if (_loading) return;
                var c = S.Devices.FirstOrDefault(x => x.Name == name);
                if (c is null) S.Devices.Add(c = new RgbDeviceChoice { Name = name });
                c.Mode = mode;
                if (mode == "follow") S.Devices.Remove(c);
                box.Visibility = mode == "color" ? Visibility.Visible : Visibility.Collapsed;
                Hub.Store.Save();
            });
            box.Visibility = choice?.Mode == "color" ? Visibility.Visible : Visibility.Collapsed;
            box.TextChanged += (_, _) =>
            {
                box.Foreground = RgbEffects.IsHex(box.Text) ? Res("Text") : Res("Red");
                if (!RgbEffects.IsHex(box.Text)) return;
                var c = S.Devices.FirstOrDefault(x => x.Name == name);
                if (c is null) return;
                c.Color = box.Text.Trim().TrimStart('#').ToUpperInvariant();
                Hub.Store.Save();
            };
            chips.Children.Add(box);
            panel.Children.Add(chips);
            _devices.Children.Add(panel);
        }
    }

    private void ShowColors()
    {
        _colorBoxes.Children.Clear();
        for (var i = 0; i < 6; i++)
        {
            var index = i;
            var box = new TextBox
            {
                Style = St("Input"), Width = 96, FlowDirection = FlowDirection.LeftToRight, FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0, 0, 8, 6),
                Text = index < S.Colors.Count ? S.Colors[index] : "", ToolTip = L.T("خالی = استفاده نمی‌شه", "Empty = not used"),
            };
            box.TextChanged += (_, _) =>
            {
                if (_loading) return;
                var text = box.Text.Trim().TrimStart('#').ToUpperInvariant();
                box.Foreground = text.Length == 0 || RgbEffects.IsHex(text) ? Res("Text") : Res("Red");
                if (text.Length != 0 && !RgbEffects.IsHex(text)) return;
                while (S.Colors.Count <= index) S.Colors.Add("");
                S.Colors[index] = text;
                S.Colors = [.. S.Colors.Where(c => c.Length > 0)];
                Hub.Store.Save();
            };
            _colorBoxes.Children.Add(box);
        }
    }

    public override void Refresh()
    {
        _loading = true;
        _enabled.IsChecked = S.Enabled;
        _speed.Value = Math.Round(S.Speed * 100);
        _bright.Value = Math.Round(S.Brightness * 100);
        _loading = false;
        Chips(_effects, RgbEffects.EffectNames.Select(e => (e.key, L.T(e.fa, e.en))), S.Effect, key => { if (!_loading) { S.Effect = key; Hub.Store.Save(); } });
        Chips(_palettes, RgbEffects.Palettes.Select(p => (p.key, L.T(p.fa, p.en))), S.Palette, key =>
        {
            if (_loading) return;
            S.Palette = key;
            _custom.Visibility = key == "custom" ? Visibility.Visible : Visibility.Collapsed;
            Hub.Store.Save();
        });
        _custom.Visibility = S.Palette == "custom" ? Visibility.Visible : Visibility.Collapsed;
        ShowColors();
        ShowStatus();
        ShowDevices();
    }
}
