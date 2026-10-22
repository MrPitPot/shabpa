using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Shabpa.Core;
using Shabpa.Kit;

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
        var card = new GlassCard { Margin = new Thickness(0, 12, 0, 0), Child = body };
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

/// <summary>Settings and a live preview of the in-game overlay. Glass Depth layout: preview + "when to show" on top, then
/// layout, colours, size, position, details, sensors, the zone editor, shortcuts, profiles and history as glass cards.</summary>
public sealed class OverlayPage : PageBase
{
    private static readonly (string key, string fa, string en)[] Anchors =
    [
        ("tl", "بالا چپ", "Top left"), ("tc", "بالا وسط", "Top centre"), ("tr", "بالا راست", "Top right"),
        ("bl", "پایین چپ", "Bottom left"), ("bc", "پایین وسط", "Bottom centre"), ("br", "پایین راست", "Bottom right"),
    ];

    private static readonly (string key, string fa, string en)[] Groups =
    [
        ("general", "عمومی", "General"), ("time", "ساعت و مدت بازی", "Clock & game time"), ("gpu", "کارت گرافیک (GPU)", "Graphics card (GPU)"), ("cpu", "پردازنده (CPU)", "Processor (CPU)"),
        ("ram", "حافظه (RAM)", "Memory (RAM)"), ("board", "مادربورد (نیاز به حسگر پیشرفته)", "Motherboard (needs advanced sensors)"), ("net", "شبکه (NET)", "Network (NET)"),
    ];

    private readonly DispatcherTimer _save = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly Border _previewStage = new();
    private readonly Border _previewHost = new();
    private readonly WrapPanel _modeChips = new();
    private readonly WrapPanel _layoutChips = new();
    private readonly WrapPanel _lookChips = new();
    private AnchorPicker _anchorPicker = null!;
    private readonly TextBlock _anchorName = new();
    private readonly StackPanel _custom = new();
    private readonly OverlayColorField _accentBox = new(), _textBox = new(), _backBox = new();
    private readonly HotkeyBox _toggleKey = new(), _moveKey = new();
    private readonly TextBlock _keyNote = new();
    private readonly Border _adminBanner = new();
    private readonly Dictionary<string, WrapPanel> _groupWraps = [];
    private OverlayEditor _editor = null!;
    private readonly TextBox _profileGame = new();
    private readonly TextBlock _profileNote = new();
    private readonly StackPanel _profileList = new();
    private readonly StackPanel _historyList = new();
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

    public OverlayPage()
    {
        _save.Tick += (_, _) =>
        {
            _save.Stop();
            Hub.Store.Save();
        };

        var root = new BlockGrid();

        // ---- preview ---------------------------------------------------------------------------------
        var (previewCard, preview) = Ui2.Card(L.T("پیش‌نمایش", "Preview"), L.T("هر تغییری همان لحظه این‌جا دیده می‌شه.", "Every change shows here right away."));
        _previewStage.Height = 250;
        _previewStage.CornerRadius = new CornerRadius(14);
        _previewStage.ClipToBounds = true;
        _previewStage.FlowDirection = FlowDirection.LeftToRight;
        _previewStage.Margin = new Thickness(0, 14, 0, 0);
        var stage = new Grid();
        stage.Children.Add(SceneArt.Build(14));
        stage.Children.Add(_previewHost);
        _previewStage.Child = stage;
        preview.Children.Add(_previewStage);
        root.Add(previewCard, 0, 0, 7);

        // ---- when to show ----------------------------------------------------------------------------
        var (showCard, show) = Ui2.Card(L.T("نمایش", "Showing"));
        show.Children.Add(Ui2.Label(L.T("چه موقع نشون داده بشه؟", "When should it show?"), 10, 6));
        show.Children.Add(_modeChips);
        _note.Style = St("Label");
        _note.TextWrapping = TextWrapping.Wrap;
        _note.Margin = new Thickness(0, 6, 0, 0);
        show.Children.Add(_note);
        show.Children.Add(Ui2.Label(L.T("بازی‌ای که شب‌پا نمی‌شناسه؟ اسم برنامه‌ش رو این‌جا بنویس (با کاما جدا کن، مثلاً cs2, valorant)", "A game Shabpa does not recognise? Type its program name here (comma separated, for example cs2, valorant)"), 16, 6));
        var gameList = new TextBox { Style = St("Input"), FlowDirection = FlowDirection.LeftToRight, FontFamily = new FontFamily("Consolas"), Text = Hub.Settings.GameList, Margin = new Thickness(0, 0, 0, 0) };
        gameList.TextChanged += (_, _) => { Hub.Settings.GameList = gameList.Text; _save.Stop(); _save.Start(); };
        show.Children.Add(gameList);
        root.Add(showCard, 0, 7, 5);

        // ---- layout ------------------------------------------------------------------------------
        var (layoutCard, layout) = Ui2.Card(L.T("طرح", "Layout"), L.T("شش طرح متفاوت؛ همه با همین اطلاعات انتخابی کار می‌کنن.", "Six different layouts; all of them show the items you pick."));
        layout.Children.Add(Ui2.Label("", 6, 4));
        layout.Children.Add(_layoutChips);
        root.Add(layoutCard, 1, 0, 6);

        // ---- colours -----------------------------------------------------------------------------
        var (lookCard, look) = Ui2.Card(L.T("رنگ و تم", "Colours"));
        look.Children.Add(Ui2.Label("", 0, 4));
        look.Children.Add(_lookChips);
        _custom.Children.Add(Ui2.Label(L.T("روی هر رنگ کلیک کن و از طیف رنگ، اشباع و روشنی انتخابش کن؛ اورلی همان لحظه عوض می‌شه.", "Click a colour and pick it from the spectrum, saturation and brightness; the overlay follows live.")));
        var customRow = new WrapPanel();
        customRow.Children.Add(GroupB.Field(L.T("رنگ اصلی", "Accent"), _accentBox));
        customRow.Children.Add(GroupB.Field(L.T("رنگ متن", "Text"), _textBox));
        customRow.Children.Add(GroupB.Field(L.T("رنگ پس‌زمینه", "Background"), _backBox));
        _custom.Children.Add(customRow);
        look.Children.Add(_custom);
        root.Add(lookCard, 1, 6, 6);

        // ---- size and opacity --------------------------------------------------------------------------
        var (sizeCard, size) = Ui2.Card(L.T("اندازه و شفافیت", "Size and opacity"));
        var scale = Ui2.SliderRow(L.T("اندازه", "Size"), 0.6, 2.2, 0.05, v => $"{v * 100:0}%");
        var opacity = Ui2.SliderRow(L.T("تیرگی پس‌زمینه", "Background opacity"), 0.1, 1.0, 0.05, v => $"{v * 100:0}%");
        var offsetX = Ui2.SliderRow(L.T("فاصله از کنار", "Offset from the side"), 0, 600, 4, v => $"{v:0} px");
        var offsetY = Ui2.SliderRow(L.T("فاصله از بالا/پایین", "Offset from top/bottom"), 0, 600, 4, v => $"{v:0} px");
        (_scale, _opacity, _offsetX, _offsetY) = (scale.slider, opacity.slider, offsetX.slider, offsetY.slider);
        scale.row.Margin = new Thickness(0, 12, 0, 0);
        foreach (var row in new[] { scale.row, opacity.row, offsetX.row, offsetY.row }) size.Children.Add(row);
        root.Add(sizeCard, 2, 0, 7);

        // ---- position ----------------------------------------------------------------------------------
        var (positionCard, position) = Ui2.Card(L.T("جای قرارگیری", "Position"));
        position.Children.Add(Ui2.Label(L.T("گوشه‌ی صفحه:", "Corner of the screen:"), 10, 8));
        _anchorPicker = new AnchorPicker(Anchors.Select(a => (a.key, L.T(a.fa, a.en))).ToList(), Hub.Settings.Overlay.Anchor);
        _anchorName.Style = St("Label");
        _anchorName.Margin = new Thickness(0, 8, 0, 0);
        position.Children.Add(_anchorPicker);
        position.Children.Add(_anchorName);
        _moveButton.Style = St("Btn");
        _moveButton.Margin = new Thickness(0, 14, 0, 0);
        _moveButton.HorizontalAlignment = HorizontalAlignment.Left;
        _moveButton.Click += OnMove;
        position.Children.Add(_moveButton);
        root.Add(positionCard, 2, 7, 5);

        // ---- look options ------------------------------------------------------------------------
        var (optionCard, options) = Ui2.Card(L.T("جزئیات ظاهر", "Details"));
        _colorByValue = new Switch();
        _labels = new Switch();
        _shadow = new Switch();
        _rounded = new Switch();
        var optionRows = new[]
        {
            (L.T("رنگ هشدار (سبز/زرد/قرمز)", "Warning colours (green / amber / red)"), _colorByValue),
            (L.T("نمایش نام هر مورد", "Show item names"), _labels),
            (L.T("سایه‌ی متن", "Text shadow"), _shadow),
            (L.T("گوشه‌های گرد", "Rounded corners"), _rounded),
        };
        for (var i = 0; i < optionRows.Length; i++)
        {
            optionRows[i].Item2.Click += OnOption;
            var row = GroupB.SwitchRow(optionRows[i].Item1, optionRows[i].Item2, first: i == 0);
            if (i == 0) row.Margin = new Thickness(0, 8, 0, 0);
            options.Children.Add(row);
        }
        root.Add(optionCard, 3, 0, 6);

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

        var (itemCard, items) = Ui2.Card(L.T("چیدمان اورلی", "Overlay layout"),
            L.T("هر بخش (کارت گرافیک، پردازنده، رم، شبکه، ساعت…) یه کاشیه. با کشیدن و رها کردن جاش رو بین ناحیه‌ها عوض کن یا بالا و پایینش کن؛ پایین‌تر هر چیزی رو که می‌خوای با تیک روشن یا خاموش کن. فقط چیزهایی که این سیستم می‌تونه بخونه نشون داده می‌شن.",
                "Every group (graphics card, processor, RAM, network, clock…) is a tile. Drag it to another zone or up and down; below, tick or untick anything you want shown. Only readings this PC can provide are shown."));
        var adminText = new TextBlock
        {
            Style = St("Body"), TextWrapping = TextWrapping.Wrap, Foreground = Res("Gold"),
            Text = L.T("چند تا از گزینه‌ها (فریم بر ثانیه، دمای CPU، فن‌ها، حسگرهای مادربورد) فقط با دسترسی ادمین کار می‌کنن. اگه تیکشون رو زدی و چیزی نمی‌بینی، دلیلش همینه.",
                "Several readings (frame rate, CPU temperature, fans, board sensors) only work with administrator rights. If you ticked them and see nothing, this is why."),
        };
        var adminButton = new Button { Style = St("Btn"), Content = L.T("اجرای دوباره با دسترسی ادمین", "Restart as administrator"), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        adminButton.Click += (_, _) =>
        {
            if (Application.Current is App app && !app.Restart(elevated: true, page: "gaming"))
                _note.Text = L.T("اجازه‌ی ادمین داده نشد.", "Administrator permission was not given.");
        };
        var adminPanel = new StackPanel();
        adminPanel.Children.Add(adminText);
        adminPanel.Children.Add(adminButton);
        var adminRow = new Grid();
        adminRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        adminRow.ColumnDefinitions.Add(new ColumnDefinition());
        adminRow.Children.Add(new IconBadge("bolt", "Gold", 34, 18, 10) { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 12, 0) });
        Grid.SetColumn(adminPanel, 1);
        adminRow.Children.Add(adminPanel);
        _adminBanner.Child = adminRow;
        _adminBanner.Padding = new Thickness(14, 12, 14, 14);
        _adminBanner.CornerRadius = new CornerRadius(14);
        _adminBanner.Margin = new Thickness(0, 14, 0, 0);
        _adminBanner.Background = Res("GoldSoft");
        items.Children.Add(_adminBanner);
        foreach (var (key, fa, en) in Groups)
        {
            var wrap = new WrapPanel();
            _groupWraps[key] = wrap;
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
                else if (!Hub.Elevated && OverlayMetrics.NeedsAdmin(def.Key))
                {
                    box.ToolTip = L.T("بدون دسترسی ادمین این مقدار خونده نمی‌شه و روی اورلی «ADMIN» می‌نویسه. شب‌پا رو با ادمین اجرا کن.",
                        "This cannot be read without administrator rights; the overlay shows “ADMIN” instead. Run Shabpa as administrator.");
                    box.Content = def.Label + L.T(" (نیاز به ادمین)", " (needs admin)");
                }
                if (def.Key == "fps" && box.ToolTip is null)
                    box.ToolTip = L.T("بازی یا برنامه‌ای که جلوته: تعداد فریم‌هایی که واقعاً نشون می‌ده (از رویدادهای گرافیک ویندوز). روی دسکتاپ: تعداد فریم‌هایی که کامپوزیتور ویندوز (DWM) در ثانیه ساخته؛ دسکتاپ ساکن کمتر از نرخ مانیتوره، چون DWM فقط وقتی چیزی عوض شه فریم می‌سازه.",
                        "A game or program in front: the frames it really presents (from Windows graphics events). On the desktop: the frames per second Windows' compositor (DWM) produced; a still desktop is below the monitor rate because DWM only composes when something changes.");
                else if (def.Key == "hz")
                    box.ToolTip = L.T("نرخ تازه‌سازی فعلی مانیتوری که پنجرهٔ جلو روشه (مثلاً ۱۶۵ هرتز).", "Current refresh rate of the monitor the window in front is on (for example 165 Hz).");
                box.Click += OnItem;
                _items[def.Key] = box;
                wrap.Children.Add(box);
            }
        }
        _editor = new OverlayEditor(() => Hub.Settings.Overlay, Groups, () =>
        {
            Hub.OverlayChanged();
            UpdatePreview();
            _save.Stop();
            _save.Start();
        }, key => _groupWraps[key]);
        items.Children.Add(_editor);

        // ---- optional advanced sensors ------------------------------------------------------------
        var (boardCard, board) = Ui2.Card(L.T("حسگرهای پیشرفته (اختیاری)", "Advanced sensors (optional)"),
            L.T("دمای واقعی CPU و دور و درصد فن CPU از حسگر مادربورد خونده می‌شن. به اجرای شب‌پا با دسترسی ادمین و درایور PawnIO نیاز داره؛ شب‌پا چیزی نصب نمی‌کنه.",
                "Real CPU temperature and CPU fan speed are read from the motherboard chip. Needs Shabpa running as administrator and the PawnIO driver; Shabpa installs nothing."));
        _board = new Switch();
        _board.Click += (_, _) =>
        {
            if (_loading) return;
            Hub.Settings.Overlay.BoardSensors = _board.IsChecked == true;
            Hub.Telemetry.UseBoardSensors = Hub.Settings.Overlay.BoardSensors && !Hub.Demo;
            Hub.OverlayChanged();
            Hub.Telemetry.Acquire();
            Hub.Telemetry.Release();
        };
        var boardRow = GroupB.SwitchRow(L.T("فعال‌سازی حسگرهای پیشرفته", "Enable advanced sensors"), _board, first: true);
        boardRow.Margin = new Thickness(0, 8, 0, 0);
        board.Children.Add(boardRow);
        _boardNote.Style = St("Label");
        _boardNote.TextWrapping = TextWrapping.Wrap;
        _boardNote.Margin = new Thickness(0, 8, 0, 0);
        board.Children.Add(_boardNote);
        root.Add(boardCard, 3, 6, 6);
        root.Add(itemCard, 4, 0, 12);

        // ---- hotkeys ---------------------------------------------------------------------------
        var (keyCard, keys) = Ui2.Card(L.T("میان‌بر کیبورد", "Keyboard shortcuts"),
            L.T("روی دکمه کلیک کن و ترکیب جدید رو بزن. حتماً Ctrl یا Alt توش باشه؛ Shift اختیاریه. Backspace میان‌بر رو خاموش می‌کنه و Esc لغو می‌کنه.",
                "Click a button and press the new combination. It must include Ctrl or Alt; Shift is optional. Backspace turns it off, Esc cancels."));
        var showRow = K.SettingRow(L.T("نمایش / پنهان", "Show / hide"), _toggleKey, first: true);
        showRow.Margin = new Thickness(0, 8, 0, 0);
        keys.Children.Add(showRow);
        keys.Children.Add(K.SettingRow(L.T("جابجایی با ماوس", "Move with the mouse"), _moveKey));
        _keyNote.Style = St("Label");
        _keyNote.TextWrapping = TextWrapping.Wrap;
        _keyNote.Margin = new Thickness(0, 8, 0, 0);
        keys.Children.Add(_keyNote);
        root.Add(keyCard, 5, 0, 6);

        // ---- per-game profiles ----------------------------------------------------------------------
        var (profileCard, profiles) = Ui2.Card(L.T("پروفایل هر بازی", "Per-game profiles"),
            L.T("چیدمان، رنگ و آیتم‌های الان رو برای یه بازی ذخیره کن؛ هر وقت اون بازی باز بشه خودکار همون اعمال می‌شه.",
                "Save the current layout, colours and items for one game; they are applied by themselves whenever that game runs."));
        _profileGame.Style = St("Input");
        _profileGame.Width = 220;
        _profileGame.FlowDirection = FlowDirection.LeftToRight;
        _profileGame.Margin = new Thickness(0, 0, 8, 0);
        var saveProfile = new Button { Style = St("Btn"), Content = L.T("ذخیره برای این بازی", "Save for this game") };
        saveProfile.Click += (_, _) => SaveProfile();
        var profileRow = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        profileRow.Children.Add(_profileGame);
        profileRow.Children.Add(saveProfile);
        profiles.Children.Add(profileRow);
        _profileNote.Style = St("Label");
        _profileNote.TextWrapping = TextWrapping.Wrap;
        _profileNote.Margin = new Thickness(0, 8, 0, 0);
        profiles.Children.Add(_profileNote);
        profiles.Children.Add(_profileList);
        root.Add(profileCard, 5, 6, 6);

        // ---- history -----------------------------------------------------------------------------------
        var (historyCard, history) = Ui2.Card(L.T("تاریخچه‌ی بازی‌ها", "Game history"),
            L.T("آخرین بازی‌هایی که شب‌پا گزارششون رو ساخته؛ برای مقایسه‌ی فریم، پینگ و دما.", "The latest games Shabpa made a report for, to compare frame rate, ping and temperature."));
        history.Children.Add(_historyList);
        root.Add(historyCard, 6, 0, 12);

        var footer = new TextBlock
        {
            Style = St("Label"), Margin = new Thickness(4, 4, 4, 0), TextWrapping = TextWrapping.Wrap,
            Text = L.T("اورلی روی بازی‌های «بدون کادر» و «پنجره‌ای» نشون داده می‌شه؛ بازی‌های تمام‌صفحه‌ی اختصاصی (exclusive) پنجره‌ی روی خودشون رو اجازه نمی‌دن. چیزی داخل بازی تزریق نمی‌شه، پس با آنتی‌چیت مشکلی نداره.",
                "The overlay shows over borderless and windowed games; true exclusive full-screen games do not allow a window over them. Nothing is injected into any game, so anti-cheat has nothing to object to."),
        };
        root.Add(footer, 7, 0, 12);
        // the grid's own margin pulls its blocks to the page edge; the footer needs no card gap below it
        Content = root;

        Chips(_modeChips, [(OverlayMode.Off, L.T("خاموش", "Off")), (OverlayMode.InGame, L.T("فقط موقع بازی", "Only while gaming")), (OverlayMode.Always, L.T("همیشه", "Always"))],
            Hub.Settings.Overlay.Mode, v => Change(o => o.Mode = v, pop: true));
        Chips(_layoutChips, OverlayLayouts.All.Select(l => (l.key, L.T(l.fa, l.en))), Hub.Settings.Overlay.Layout, v => Change(o => o.Layout = v, pop: true));
        Chips(_lookChips, OverlayLook.All.Select(l => (l.Key, l.Name)), Hub.Settings.Overlay.Look, v => Change(o => o.Look = v, pop: true));
        _anchorPicker.Picked += v => { UpdateAnchorName(); Change(o => o.Anchor = v, pop: true); };

        scale.slider.ValueChanged += (_, _) => Change(o => o.Scale = scale.slider.Value);
        opacity.slider.ValueChanged += (_, _) => Change(o => o.BackOpacity = opacity.slider.Value);
        offsetX.slider.ValueChanged += (_, _) => Change(o => o.X = offsetX.slider.Value);
        offsetY.slider.ValueChanged += (_, _) => Change(o => o.Y = offsetY.slider.Value);
        _accentBox.Picked += v => Change(o => o.CustomAccent = v);
        _textBox.Picked += v => Change(o => o.CustomText = v);
        _backBox.Picked += v => Change(o => o.CustomBack = v);
        _toggleKey.Changed += value => Change(o => o.ToggleKey = value);
        _moveKey.Changed += value => Change(o => o.MoveKey = value);

        Hub.OverlayMoving += _ => Dispatcher.BeginInvoke(UpdateMoveButton);
        IsVisibleChanged += (_, _) => { if (IsVisible) Refresh(); };
    }

    private void UpdateAnchorName()
    {
        var (_, fa, en) = Anchors.FirstOrDefault(a => a.key == _anchorPicker.Value);
        _anchorName.Text = fa is null ? "" : L.T(fa, en);
    }

    private void Change(Action<OverlaySettings> edit, bool pop = false)
    {
        if (_loading) return;
        edit(Hub.Settings.Overlay);
        Hub.Overlay?.Evaluate();
        _editor?.Rebuild();
        UpdatePreview(pop);
        UpdateVisibility();
        _save.Stop();
        _save.Start();
    }

    private void OnOption(object sender, RoutedEventArgs e) => Change(o =>
    {
        o.ColorByValue = _colorByValue.IsChecked == true;
        o.ShowLabels = _labels.IsChecked == true;
        o.Shadow = _shadow.IsChecked == true;
        o.Rounded = _rounded.IsChecked == true;
    }, pop: true);

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

    private void SaveProfile()
    {
        var name = _profileGame.Text.Trim();
        if (name.Length == 0)
        {
            _profileNote.Text = L.T("اسم برنامه‌ی بازی رو بنویس (مثلاً cs2). وقتی بازی بالا باشه خودش پر می‌شه.", "Type the game's process name (for example cs2). It fills in by itself while a game runs.");
            return;
        }
        var o = Hub.Settings.Overlay;
        o.Profiles.RemoveAll(p => string.Equals(p.Game, name, StringComparison.OrdinalIgnoreCase));
        o.Profiles.Add(o.Snapshot(name));
        Hub.OverlayChanged();
        _profileNote.Text = L.T($"برای «{name}» ذخیره شد.", $"Saved for “{name}”.");
        GroupB.Appear(_profileNote);
        ShowProfiles(animate: true);
    }

    private void ShowProfiles(bool animate = false)
    {
        _profileList.Children.Clear();
        var profiles = Hub.Settings.Overlay.Profiles.ToList();
        for (var i = 0; i < profiles.Count; i++)
        {
            var profile = profiles[i];
            var game = profile.Game;
            FrameworkElement? shown = null;
            var delete = IconButton("trash", L.T("حذف", "Delete"), () =>
            {
                void Remove()
                {
                    Hub.Settings.Overlay.Profiles.RemoveAll(p => p.Game == game);
                    Hub.OverlayChanged();
                    ShowProfiles();
                }
                if (shown is null) Remove();
                else GroupB.Vanish(shown, Remove);
            });
            if (i > 0) _profileList.Children.Add(GroupB.Line());
            var row = Row("play", "HueA", game,
                L.T($"{profile.Items.Count} مورد · طرح {profile.Layout}", $"{profile.Items.Count} items · {profile.Layout} layout"), delete);
            shown = row;
            _profileList.Children.Add(row);
            if (animate) GroupB.Appear(row, i);
        }
        _profileList.Margin = new Thickness(0, profiles.Count > 0 ? 8 : 0, 0, 0);
    }

    private void ShowHistory()
    {
        _historyList.Children.Clear();
        var entries = Hub.State.GameHistory.AsEnumerable().Reverse().Take(12).ToList();
        if (entries.Count == 0)
        {
            _historyList.Children.Add(Ui2.Label(L.T("هنوز بازی‌ای ثبت نشده. بعد از هر بازی بیشتر از چند دقیقه اینجا اضافه می‌شه.",
                "No games yet. Every game longer than a few minutes is added here.")));
            return;
        }
        var bestFps = entries.Max(e => e.AvgFps);
        _historyList.Margin = new Thickness(0, 8, 0, 0);
        var first = true;
        foreach (var e in entries)
        {
            var parts = new List<string> { Fa.Duration(TimeSpan.FromMinutes(e.Minutes)) };
            if (e.AvgFps > 0) parts.Add($"FPS {Fa.N(e.AvgFps, 0)} / 1% {Fa.N(e.Low1Fps, 0)}");
            if (e.AvgPing > 0) parts.Add($"{L.T("پینگ", "ping")} {Fa.N(e.AvgPing, 0)}");
            if (e.MaxGpuTemp > 0) parts.Add($"GPU {Fa.N(e.MaxGpuTemp, 0)}°");
            var bar = new ProgressBar { Style = St("Bar"), Minimum = 0, Maximum = Math.Max(1, bestFps), Value = e.AvgFps, Height = 6, Width = 90, Margin = new Thickness(0, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            if (!first) _historyList.Children.Add(GroupB.Line());
            first = false;
            bar.Value = 0;
            Fx.Anim(bar, System.Windows.Controls.Primitives.RangeBase.ValueProperty, e.AvgFps, Fx.Out, 800); // the bar grows to its value
            _historyList.Children.Add(Row("mon", "HueB", $"{e.Game} · {Fa.RelativeDay(e.Start, DateTime.Now)} {Fa.Clock(e.Start)}", string.Join(" · ", parts), bar));
        }
    }

    private void OnItem(object sender, RoutedEventArgs e) => Change(o =>
    {
        // Keep the catalogue order so the overlay reads the same way every time.
        o.Items = [.. OverlayMetrics.All.Where(m => _items[m.Key].IsChecked == true && !m.Advanced).Select(m => m.Key)];
        _editor?.Rebuild();
    }, pop: true);

    private void OnMove(object sender, RoutedEventArgs e)
    {
        if (Hub.Overlay is null) return;
        Hub.Overlay.SetMoveMode(!Hub.Overlay.MoveMode);
        UpdateMoveButton();
    }

    private void UpdateMoveButton()
    {
        var moving = Hub.Overlay?.MoveMode == true;
        _moveButton.Content = GroupB.IconText("mon", moving
            ? L.T("تموم شد (جای اورلی ذخیره می‌شه)", "Done (position is saved)")
            : L.T("جابجایی اورلی با ماوس", "Move the overlay with the mouse"));
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
        _accentBox.Set(o.CustomAccent);
        _textBox.Set(o.CustomText);
        _backBox.Set(o.CustomBack);
        _toggleKey.Set(o.ToggleKey);
        _moveKey.Set(o.MoveKey);
        _anchorPicker.Pick(o.Anchor, raise: false);
        UpdateAnchorName();
        _loading = false;

        _board.IsChecked = Hub.Settings.Overlay.BoardSensors;
        UpdateBoardNote();
        _note.Text = Hub.Elevated
            ? L.T("شب‌پا با دسترسی ادمین اجرا شده؛ فریم بر ثانیه و حسگرهای مادربورد کار می‌کنن.", "Shabpa is running as administrator; frame rate and board sensors can work.")
            : L.T("شب‌پا بدون ادمین اجرا شده. فریم بر ثانیه، دمای CPU و فن‌ها که کنارشون «نیاز به ادمین» نوشته، تا با ادمین اجرا نکنی خالی می‌مونن. بقیه بدون ادمین هم کار می‌کنن.",
                "Shabpa is not running as administrator. Frame rate, CPU temperature and fans marked “needs admin” stay empty until you run it as admin. Everything else works without it.");
        _adminBanner.Visibility = Hub.Elevated ? Visibility.Collapsed : Visibility.Visible;
        _keyNote.Text = Hub.Overlay is { FailedHotkeys.Count: > 0 } w
            ? L.T($"این میان‌بر رو برنامه‌ی دیگه‌ای گرفته و کار نمی‌کنه: {string.Join("، ", w.FailedHotkeys)}. یه ترکیب دیگه انتخاب کن.",
                $"Another program already uses: {string.Join(", ", w.FailedHotkeys)}. Pick a different combination.")
            : "";
        _keyNote.Foreground = Res("Gold");
        _keyNote.Visibility = _keyNote.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateMoveButton();
        UpdateVisibility();
        UpdatePreview();
        if (_profileGame.Text.Length == 0 && Hub.Snapshot.GameProcess.Length > 0) _profileGame.Text = Hub.Snapshot.GameProcess;
        ShowProfiles();
        ShowHistory();
        _editor.Rebuild();
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

    private void UpdatePreview(bool pop = false)
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

        // The preview shows the main block; groups placed on their own are listed under it.
        var all = OverlayMetrics.Build(OverlayMetrics.Demo(now), o.Items);
        string GroupOf(MetricView m) => OverlayMetrics.Find(m.Key)?.Group ?? "general";
        var metrics = all.OrderBy(m => o.OrderOf(GroupOf(m))).Where(m => o.PlaceOf(GroupOf(m)).Anchor == "main").ToList();
        var element = OverlayRenderer.Build(o, metrics, _fakeHistory);
        var host = new Grid { Margin = new Thickness(14) };
        element.HorizontalAlignment = o.Anchor switch { "tr" or "br" => HorizontalAlignment.Right, "tc" or "bc" => HorizontalAlignment.Center, _ => HorizontalAlignment.Left };
        element.VerticalAlignment = o.Anchor is "bl" or "br" or "bc" ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        host.Children.Add(element);
        _previewHost.Child = host;
        if (pop) Motion.Pop(element);
    }
}
