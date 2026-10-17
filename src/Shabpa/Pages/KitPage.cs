using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>Hidden "Design kit" page: every shared component of the Glass Depth foundation in one place, so styles can be
/// checked by eye (and by screenshot: <c>--screenshots dir --only kit</c>, or <c>--page kit</c> / Ctrl+Shift+K). Not in the
/// menu. When you add or change a shared component, show it here.</summary>
public sealed class KitPage : PageBase
{
    public override string PageTitle => L.T("کیت طراحی", "Design kit");
    public override string PageSubtitle => L.T("همه‌ی اجزای مشترک شیشه‌ی عمق، یک‌جا برای بررسی چشمی.", "Every shared Glass Depth component in one place, for checking by eye.");
    public override UIElement? HeaderAccessory => new StatusBadge(L.T("فقط برای توسعه", "Dev only"), "Gold", null);

    public KitPage()
    {
        var root = new StackPanel();

        // ---- type scale -----------------------------------------------------------------------
        var (type, t) = Ui2.Card(L.T("تایپوگرافی", "Typography"));
        t.Children.Add(new TextBlock { Text = L.T("تیتر صفحه ۲۷ بولد", "Page title 27 bold"), Style = St("H1") });
        t.Children.Add(new TextBlock { Text = L.T("تیتر بخش ۱۴ بولد (H2)", "Section title 14 bold (H2)"), Style = St("H2"), Margin = new Thickness(0, 8, 0, 0) });
        t.Children.Add(new TextBlock { Text = L.T("متن معمولی ۱۳٫۵ با ارتفاع خط ۲۴. شب‌پا مراقب سیستم تو هست؛ هر کاری با ساعت دقیق تأیید می‌شه.", "Body 13.5 with a 24 px line. Shabpa watches your PC; every action is confirmed with the exact time."), Style = St("Body"), Margin = new Thickness(0, 6, 0, 0) });
        t.Children.Add(new TextBlock { Text = L.T("برچسب ۱۲٫۵ (Label)", "Label 12.5"), Style = St("Label"), Margin = new Thickness(0, 6, 0, 0) });
        t.Children.Add(new TextBlock { Text = L.T("نکته‌ی کوچک ۱۲ (Tip) برای توضیح زیر کنترل‌ها.", "Small tip 12 (Tip) for the explanation under controls."), Style = St("Tip"), Margin = new Thickness(0, 6, 0, 0) });
        t.Children.Add(new TextBlock { Text = Fa.Digits("0123456789  ۲۳:۴۰  ۱۲٫۵") + "  Ctrl+Shift+O", Style = St("Big"), FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) });
        type.Margin = new Thickness(0);
        root.Children.Add(type);

        // ---- buttons --------------------------------------------------------------------------
        var (buttons, b) = Ui2.Card(L.T("دکمه‌ها", "Buttons"));
        var row = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        row.Children.Add(Btn("BtnPrimary", L.T("تأیید و زمان‌بندی", "Confirm & schedule"), "pw"));
        row.Children.Add(Btn("Btn", L.T("تغییر زمان", "Change time"), null));
        row.Children.Add(Btn("BtnDanger", L.T("حذف", "Delete"), "trash"));
        var icon = new Button { Style = St("BtnIcon"), Content = new Icon("set", 18), Margin = new Thickness(0, 0, 12, 0), ToolTip = L.T("دکمه‌ی آیکونی", "Icon button") };
        row.Children.Add(icon);
        var disabled = Btn("Btn", L.T("غیرفعال", "Disabled"), null);
        disabled.IsEnabled = false;
        row.Children.Add(disabled);
        b.Children.Add(row);
        buttons.Margin = new Thickness(0, 16, 0, 0);
        root.Children.Add(buttons);

        // ---- choosing -------------------------------------------------------------------------
        var (choose, c) = Ui2.Card(L.T("انتخاب: سگمنت، چیپ، چک‌باکس، سوییچ", "Choosing: segmented, chips, checkbox, switch"));
        var segs = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        segs.Children.Add(new Segmented([L.T("خاموش", "Shut down"), L.T("خواب", "Sleep"), L.T("راه‌اندازی مجدد", "Restart")], 0) { Margin = new Thickness(0, 0, 12, 8) });
        segs.Children.Add(new Segmented([L.T("تیره", "Dark"), L.T("روشن", "Light")], 0, compact: true) { Margin = new Thickness(0, 0, 12, 8) });
        c.Children.Add(segs);
        var chips = new WrapPanel();
        Chips(chips, Enumerable.Range(1, 7).Select(i => (i, L.T($"گزینه {Fa.Digits(i.ToString())}", $"Option {i}"))), 3, _ => { });
        c.Children.Add(chips);
        var checks = new WrapPanel();
        checks.Children.Add(new CheckBox { Style = St("Check"), Content = L.T("چک‌باکس روشن", "Checked"), IsChecked = true, Margin = new Thickness(0, 0, 8, 8) });
        checks.Children.Add(new CheckBox { Style = St("Check"), Content = L.T("چک‌باکس خاموش", "Unchecked"), Margin = new Thickness(0, 0, 8, 8) });
        // the tick draws from the left short stroke to the top right in both languages: frames at 20 / 45 / 70 / 100 %, and one that replays on click
        var ticks = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8), FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var f in new[] { 0.2, 0.45, 0.7, 1.0 })
            ticks.Children.Add(new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(9), Margin = new Thickness(0, 0, 8, 0), Background = Res("AccentGradient"), Child = new TickMark { Width = 18, Height = 18, Weight = 2.6, Progress = f, Foreground = Res("White"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
        var replay = new TickMark { Width = 18, Height = 18, Weight = 2.6, Progress = 1, Foreground = Res("White"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var replayBox = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(9), Background = Res("AccentGradient"), Cursor = System.Windows.Input.Cursors.Hand, Child = replay };
        replayBox.MouseLeftButtonUp += (_, _) => replay.Replay();
        ticks.Children.Add(replayBox);
        c.Children.Add(ticks);
        var days = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        DayToggles(days);
        c.Children.Add(checks);
        c.Children.Add(days);
        var rows = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        rows.Children.Add(K.SettingRow(L.T("تأیید هر اقدام با ساعت دقیق", "Confirm every action with the exact time"), new Switch { IsChecked = true }, first: true));
        rows.Children.Add(K.SettingRow(L.T("کاهش حرکت", "Reduce motion"), new Switch()));
        rows.Children.Add(K.SettingRow(L.T("میان‌بر", "Hotkey"), KeyCap.Row("Ctrl+Shift+O")));
        rows.Children.Add(K.SettingRow(L.T("رنگ تأکید", "Accent colour"), new Swatches([Color.FromRgb(0x80, 0x87, 0xFF), Color.FromRgb(0x5A, 0xA9, 0xFF), Color.FromRgb(0xB8, 0x92, 0xFF), Color.FromRgb(0xDB, 0x3A, 0x8A)], 0)));
        c.Children.Add(rows);
        choose.Margin = new Thickness(0, 16, 0, 0);
        root.Children.Add(choose);

        // ---- inputs ---------------------------------------------------------------------------
        var (inputs, i2) = Ui2.Card(L.T("ورودی: متن، عدد، اسلایدر، نوار", "Inputs: text, number, slider, bars"));
        var inRow = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        inRow.Children.Add(new TextBox { Style = St("Input"), Text = L.T("متن نمونه", "Sample text"), Width = 200, Margin = new Thickness(0, 0, 12, 8) });
        inRow.Children.Add(new TextBox { Style = St("NumberInput"), Text = "23", Margin = new Thickness(0, 0, 8, 8) });
        inRow.Children.Add(new TextBox { Style = St("NumberInput"), Text = "40", Margin = new Thickness(0, 0, 8, 8) });
        i2.Children.Add(inRow);
        var (sliderRow, slider, _) = Ui2.SliderRow(L.T("شفافیت", "Opacity"), 0, 100, 5, v => $"{v:0}%");
        slider.Value = 80;
        i2.Children.Add(sliderRow);
        var bar1 = new ProgressBar { Style = St("Bar"), Maximum = 100, Value = 62, Margin = new Thickness(0, 14, 0, 0) };
        var bar2 = new ProgressBar { Style = St("BarWarn"), Maximum = 100, Value = 79, Margin = new Thickness(0, 10, 0, 0) };
        i2.Children.Add(bar1);
        i2.Children.Add(bar2);
        inputs.Margin = new Thickness(0, 16, 0, 0);
        root.Children.Add(inputs);

        // ---- badges, ring, stat, quick --------------------------------------------------------
        var (misc, m) = Ui2.Card(L.T("نشان‌ها و حلقه", "Badges and ring"));
        var badges = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        badges.Children.Add(new StatusBadge(L.T("همه‌چیز سالمه", "All healthy"), "Green") { Margin = new Thickness(0, 0, 10, 8) });
        badges.Children.Add(new StatusBadge(L.T("نیاز به توجه", "Needs attention"), "Gold", "temp") { Margin = new Thickness(0, 0, 10, 8) });
        badges.Children.Add(new StatusBadge(L.T("خطا", "Error"), "Red", "x") { Margin = new Thickness(0, 0, 10, 8) });
        foreach (var hue in new[] { "HueA", "HueB", "HueC", "HueD", "HueE" })
            badges.Children.Add(new IconBadge("bolt", hue) { Margin = new Thickness(0, 0, 10, 8) });
        m.Children.Add(badges);
        var rings = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        rings.Children.Add(new RingGauge { Width = 180, Height = 180, Fraction = 0.45, Margin = new Thickness(0, 0, 24, 0) });
        rings.Children.Add(new RingGauge { Width = 140, Height = 140, ViewSize = 200, RingRadius = 84, Thickness = 12, Ticks = false, Knob = false, Fraction = 0.92 });
        m.Children.Add(rings);
        var spark = new Sparkline { Height = 60, Fill = true, Thickness = 2.5, Line = Res("Accent"), Margin = new Thickness(0, 12, 0, 0) };
        spark.Set([30, 34, 33, 38, 41, 40, 39, 44, 42, 43, 41, 40, 38, 37, 39, 42, 45, 44]);
        m.Children.Add(spark);
        misc.Margin = new Thickness(0, 16, 0, 0);
        root.Children.Add(misc);

        var cards = new UniformGrid { Columns = 4, Margin = new Thickness(-8, 8, -8, 0) };
        var stat = new StatCard(L.T("پردازنده", "Processor"), "cpu", "HueA") { Margin = new Thickness(8) };
        stat.Set(Fa.N(18, 0), L.T("٪", "%"), [20, 22, 19, 25, 24, 21, 18, 17, 22, 19, 18]);
        var stat2 = new StatCard(L.T("حافظه", "Memory"), "ram", "HueB") { Margin = new Thickness(8) };
        stat2.Set(Fa.N(6.2, 1), L.T("گیگ از ۱۶", "GB of 16"), [6.0, 6.1, 6.3, 6.2, 6.4, 6.1, 6.2, 6.0, 6.2]);
        var meter = new StatCard(L.T("دیسک D", "Drive D"), "dl", "HueD", meter: true) { Margin = new Thickness(8) };
        meter.Set(Fa.N(79, 0), L.T("٪", "%"), meter: 79);
        var quick = new QuickCard("pw", "HueE", L.T("خاموشی فوری", "Shut down now"), L.T("همین حالا", "Right now"), () => (Application.Current.MainWindow as MainWindow)?.ShowToast(L.T("نمونه‌ی اعلان", "Sample notice"), L.T("این یک اعلان درون‌برنامه‌ایه.", "This is an in-app notice."))) { Margin = new Thickness(8) };
        cards.Children.Add(stat);
        cards.Children.Add(stat2);
        cards.Children.Add(meter);
        cards.Children.Add(quick);
        root.Children.Add(cards);

        // ---- icons ----------------------------------------------------------------------------
        var (icons, ic) = Ui2.Card(L.T("آیکون‌ها (Icons.xaml)", "Icons (Icons.xaml)"));
        var wrap = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        foreach (var name in new[] { "home", "mon", "tasks", "dl", "ecg", "shield", "mic", "net", "bolt", "set", "pw", "moon", "sun", "cpu", "ram", "temp", "clk", "brush", "chk", "x", "user", "globe", "folder", "cloud", "life", "refresh", "trash", "lock", "plus", "alarm", "star", "edit", "chev", "bell", "play", "heart" })
        {
            var cell = new StackPanel { Width = 74, Margin = new Thickness(0, 0, 0, 10), ToolTip = name };
            cell.Children.Add(new Icon(name, 22, Res("Text")) { HorizontalAlignment = HorizontalAlignment.Center });
            cell.Children.Add(new TextBlock { Text = name, Style = St("Tip"), FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center, FlowDirection = FlowDirection.LeftToRight });
            wrap.Children.Add(cell);
        }
        ic.Children.Add(wrap);
        icons.Margin = new Thickness(0, 8, 0, 0);
        root.Children.Add(icons);

        Content = root;
    }

    private static Button Btn(string style, string text, string? icon)
    {
        var b = new Button { Style = St(style), Margin = new Thickness(0, 0, 12, 8), Height = 40, Padding = new Thickness(20, 0, 20, 0) };
        if (icon is null) b.Content = text;
        else
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Icon(icon, 18) { Margin = new Thickness(0, 0, 8, 0) });
            row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            b.Content = row;
        }
        return b;
    }
}
