using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>Settings in the "Glass Depth" design: two columns of glass cards (one column when the window is narrow), every
/// option a 44 px row with the label at the start and a switch or a segmented control at the end. Built in code from the
/// shared kit; the options, their tooltips and what they do are exactly the ones of the previous screen.</summary>
public sealed class SettingsPage : PageBase
{
    public override string PageTitle => L.T("تنظیمات", "Settings");
    public override string PageSubtitle => L.T("زبان، ظاهر و رفتار برنامه.", "Language, look and behaviour.");

    // appearance
    private readonly WrapPanel LanguageChips = new(), ThemePanel = new(), GlassChips = new(), CountdownChips = new();
    private readonly Slider GlassLevelSlider;
    private readonly TextBlock GlassLevelText;
    private readonly FrameworkElement GlassLevelRow;

    // behaviour
    private readonly Switch AutostartCheck = new(), ConfirmCheck = new(), ReportCheck = new(), GameLogCheck = new(), AnimCheck = new(), SoundCheck = new(), SoftwareCheck = new(), LowMemoryCheck = new(), MinimizedCheck = new();
    private readonly TextBlock AutostartHint = new() { Style = St("Label"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 2) };

    // power estimate
    private readonly TextBlock PowerHint = new() { Style = St("Body"), Margin = new Thickness(0, 4, 0, 12) };
    private readonly TextBox GpuMaxBox = new(), GpuIdleBox = new(), BaseBox = new(), CpuMaxBox = new();
    private readonly StackPanel CpuMaxPanel = new() { Margin = new Thickness(6) };

    // recorders, backup, notices
    private readonly TextBox RecorderBox = new();
    private readonly HotkeyBox AssistantKeyBox = new();
    private readonly Switch BackupProfiles = new() { IsChecked = true }, BackupTasks = new();
    private readonly TextBlock BackupHint = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly StackPanel NoticeList = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly HashSet<(long, string)> _noticesSeen = [];
    private bool _noticesPrimed;

    // every card is a direct child, so the page entrance staggers them one by one
    private readonly ColumnsPanel _cols = new() { Margin = new Thickness(0, 8, 0, 8) };
    private readonly List<(int order, int column, UIElement card)> _cards = [];

    private void Put(UIElement card, int column, int order) => _cards.Add((order, column, card));

    public SettingsPage()
    {
        var s = Hub.Settings;

        // ---- language and look ------------------------------------------------------------------------
        var (look, lookBody) = Card(L.T("زبان و ظاهر", "Language and look"));
        lookBody.Children.Add(K.SettingRow(L.T("زبان", "Language"), LanguageChips, first: true));
        Chips(LanguageChips, [("fa", "فارسی"), ("en", "English")], L.En ? "en" : "fa", language =>
        {
            s.Language = language;
            Apply();
        });
        lookBody.Children.Add(Stacked(L.T("تم رنگی", "Colour theme"), ThemePanel));
        BuildThemes();

        var glassRow = K.SettingRow(L.T("شیشه‌ای (ویندوز ۱۱)", "Glass (Windows 11)"), GlassChips);
        glassRow.ToolTip = L.T("پس‌زمینه‌ی پنجره نیمه‌شفاف می‌شه و رنگ میز کار یا عکس پس‌زمینه کم‌رنگ از زیرش پیداست. در حالت «رم کمتر» خاموشه.", "The window background turns translucent and your wallpaper shows faintly through. Off in “Use less RAM” mode.");
        glassRow.Background = Res("Transparent");
        lookBody.Children.Add(glassRow);
        if (!Ui.GlassSupported())
        {
            GlassChips.IsEnabled = false;
            GlassChips.Opacity = 0.5;
            lookBody.Children.Add(new TextBlock
            {
                Text = L.T("روی ویندوز ۱۰ و نسخه‌های قدیمی‌تر از ویندوز ۱۱ (۲۲H2) پشتیبانی نمی‌شود.", "Not supported on Windows 10 or Windows 11 before 22H2."),
                Foreground = Res("Muted"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
            });
        }
        Chips(GlassChips, [("off", L.T("خاموش", "Off")), ("mica", L.T("میکا (نرم)", "Mica (soft)")), ("acrylic", L.T("آکریلیک (شیشه‌ای)", "Acrylic (frosted)"))], Ui.CleanGlass(s.Glass), glass =>
        {
            if (glass == Ui.CleanGlass(s.Glass)) return;
            var wasGlass = s.Glass != "off";
            s.Glass = glass;
            // between the two glass materials nothing restarts; to or from "off" the look is rebuilt, which needs one restart
            if (wasGlass && glass != "off" && Ui.SetBackdrop(glass)) { Hub.Store.Save(); return; }
            Apply();
        });

        var (levelRow, slider, levelText) = Ui2.SliderRow(L.T("تیرگی شیشه", "Glass darkness"), 0, 100, 5, v => $"{v:0}%");
        GlassLevelSlider = slider;
        GlassLevelText = levelText;
        GlassLevelRow = levelRow;
        levelRow.Margin = new Thickness(0, 4, 0, 6);
        GlassLevelSlider.Value = Math.Round(s.GlassLevel * 100);
        GlassLevelText.Text = $"{GlassLevelSlider.Value:0}%";
        GlassLevelRow.Visibility = Ui.IsGlass ? Visibility.Visible : Visibility.Collapsed;
        var glassSave = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        glassSave.Tick += (_, _) => { glassSave.Stop(); Hub.Store.Save(); };
        GlassLevelSlider.ValueChanged += (_, _) =>
        {
            GlassLevelText.Text = $"{GlassLevelSlider.Value:0}%";
            if (Math.Abs(s.GlassLevel - GlassLevelSlider.Value / 100) < 0.001) return;
            s.GlassLevel = GlassLevelSlider.Value / 100;
            Ui.SetGlassLevel(s.GlassLevel); // changes the window right now, no restart
            glassSave.Stop();
            glassSave.Start();
        };
        lookBody.Children.Add(levelRow);
        lookBody.Children.Add(new TextBlock
        {
            Text = L.T("با عوض کردن زبان، برنامه یک لحظه بسته و دوباره باز می‌شه. تم رنگی، تیرگی شیشه و جابه‌جایی بین میکا و آکریلیک بدون بازشدن دوباره انجام می‌شن.", "Changing the language briefly closes and reopens the app. The colour theme, glass darkness and switching between Mica and Acrylic apply right away."),
            Style = St("Tip"), Margin = new Thickness(0, 4, 0, 0),
        });
        Put(look, 0, 0);

        // ---- behaviour --------------------------------------------------------------------------------
        var (behaviour, behaviourBody) = Card(L.T("رفتار برنامه", "Behaviour"));
        behaviourBody.Children.Add(SwitchRow(AutostartCheck, L.T("همراه ویندوز اجرا بشه", "Start with Windows"),
            L.T("بدون این، بعد از روشن شدن سیستم زمان‌بندی‌ها و قانون‌ها کار نمی‌کنن تا خودت برنامه رو باز کنی.", "Without this, schedules and rules do nothing after a reboot until you open the app yourself."), true));
        behaviourBody.Children.Add(AutostartHint);
        behaviourBody.Children.Add(SwitchRow(ConfirmCheck, L.T("قبل از هر کار تایید بگیر", "Ask before every action"),
            L.T("قبل از زمان‌بندی، حذف، قفل کردن و خالی کردن رم یه پیام تایید با زمان دقیق نشون می‌ده.", "Shows a confirmation with the exact time before scheduling, deleting, locking or freeing RAM.")));
        behaviourBody.Children.Add(SwitchRow(ReportCheck, L.T("بعد از هر بازی گزارش بده", "Report after every game"),
            L.T("وقتی بازی بسته شد: مدت بازی، اینترنت مصرفی، دماها و بیشترین مصرف؛ با کلیک روش گزارش کامل باز می‌شه. با تلگرام هم می‌فرسته.", "When a game closes: play time, internet used, temperatures and peak load; click it for the full log. Also sent to Telegram.")));
        behaviourBody.Children.Add(SwitchRow(GameLogCheck, L.T("ثبت نوبت‌های بازی (زمان بازی)", "Log game sessions (Playtime)"),
            L.T("زمان بازی، دماها، مصرف و برنامه‌های پس‌زمینه‌ی هر نوبت رو روی همین کامپیوتر ذخیره می‌کنه و تو صفحه‌ی «زمان بازی» نشون می‌ده. چیزی جایی فرستاده نمی‌شه.",
                "Keeps play time, temperatures, load and background programs of each session on this PC for the Playtime page. Nothing is uploaded.")));
        behaviourBody.Children.Add(SwitchRow(AnimCheck, L.T("انیمیشن‌ها", "Animations"),
            L.T("حرکت نرم صفحه‌ها و دکمه‌ها. اگه ویندوز انیمیشن‌ها رو خاموش کرده باشه، این‌جا هم خاموش می‌مونن.", "Smooth motion for pages and buttons. If Windows has animations turned off, they stay off here too.")));
        behaviourBody.Children.Add(SwitchRow(SoundCheck, L.T("صدای ریز موقع حرکت ماوس (آزمایشی)", "Tiny sounds on hover (experimental)"),
            L.T("یه تیک خیلی آروم موقع رفتن ماوس روی گزینه‌ها، یه کلیک و یه زنگ کوتاه برای اعلان‌ها. موقع بازی و در حالت «رم کمتر» ساکته.", "A very soft tick when the mouse moves over options, a click and a short chime for notices. Silent during games and in “Use less RAM” mode.")));
        behaviourBody.Children.Add(SwitchRow(SoftwareCheck, L.T("رسم ظاهر با کارت گرافیک (GPU)", "Draw the window with the graphics card (GPU)"),
            L.T("روشن (پیش‌فرض): ظاهر برنامه با شتاب سخت‌افزاری کشیده می‌شه، روان‌تره. خاموش: پردازنده می‌کشه و کارت گرافیک و حدود ۴۵ مگ رم آزاد می‌مونه، با کمی کار بیشتر برای پردازنده. حالت «رم کمتر» این رو خودش خاموش می‌کنه و علاوه بر اون انیمیشن و صدا رو هم قطع می‌کنه. برنامه دوباره باز می‌شه.",
                "On (default): the window is drawn with hardware acceleration, smoother. Off: the processor draws it, leaving the graphics card and about 45 MB of RAM free, at a little more CPU. “Use less RAM” turns this off by itself and also stops animations and sounds. The app restarts.")));
        behaviourBody.Children.Add(SwitchRow(LowMemoryCheck, L.T("رم کمتر", "Use less RAM"),
            L.T("کم‌مصرف‌ترین حالت: انیمیشن‌ها و صدای رابط خاموش می‌شن، پنجره به‌جای کارت گرافیک با پردازنده کشیده می‌شه (حدود ۴۵ مگ رم کمتر) و حافظه‌ی بلااستفاده زودتر برگردونده می‌شه. برنامه دوباره باز می‌شه.",
                "The lightest mode: animations and interface sounds are off, the window is drawn by the processor instead of the graphics card (about 45 MB less RAM) and unused memory is handed back sooner. The app restarts.")));
        behaviourBody.Children.Add(SwitchRow(MinimizedCheck, L.T("همیشه بی‌صدا کنار ساعت باز بشه", "Always start quietly in the tray"), null));
        behaviourBody.Children.Add(K.SettingRow(L.T("کلید فرمان متنی", "Command box shortcut"), AssistantKeyBox,
            hint: L.T("از هر جا پنجره‌ی فرمان رو باز می‌کنه («ساعت ۲ شب خاموش کن»). از منوی کنار ساعت هم باز می‌شه.", "Opens the command box from anywhere (“shut down at 2am”). The tray menu opens it too.")));
        AssistantKeyBox.Changed += v =>
        {
            Hub.Settings.AssistantKey = v;
            Hub.Store.Save();
            Hub.AssistantKey?.Apply(v);
        };
        foreach (var sw in new[] { AutostartCheck, ConfirmCheck, ReportCheck, GameLogCheck, AnimCheck, SoundCheck, MinimizedCheck }) sw.Click += OnBehaviour;
        SoftwareCheck.Click += OnSoftware;
        LowMemoryCheck.Click += OnLowMemory;
        SoftwareCheck.IsChecked = !s.SoftwareRender && !s.LowMemory; // on = the graphics card draws the window
        SoftwareCheck.IsEnabled = !s.LowMemory;

        behaviourBody.Children.Add(Stacked(L.T("قبل از اجرای دستورهای زمان‌بندی‌شده چقدر فرصت لغو بده؟", "How long to wait for a cancel before a scheduled command runs?"), CountdownChips));
        Chips(CountdownChips,
            [(10, Fa.Duration(TimeSpan.FromSeconds(10))), (30, Fa.Duration(TimeSpan.FromSeconds(30))), (60, Fa.Duration(TimeSpan.FromMinutes(1))), (120, Fa.Duration(TimeSpan.FromMinutes(2)))],
            s.CountdownSeconds, v =>
            {
                s.CountdownSeconds = v;
                Hub.Store.Save();
            });
        Put(behaviour, 0, 2);

        // ---- name and birthday (optional) -------------------------------------------------------------
        Put(new BirthdayCard(), 0, 3);

        // ---- screen recorders -------------------------------------------------------------------------
        var (recorders, recordersBody) = Card(L.T("برنامه‌های ضبط صفحه", "Screen recorders"),
            L.T("وقتی یکی از این‌ها باز باشه، قانون‌هایی که «موقع ضبط صفحه دست نگه دار» دارن صبر می‌کنن. اسم‌ها رو با ویرگول جدا کن.", "While one of these is running, rules set to hold during screen recording wait. Separate names with commas."));
        var recorderRow = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var saveRecorders = new Button { Style = St("Btn"), Content = L.T("ذخیره", "Save"), Margin = new Thickness(10, 0, 0, 0) };
        saveRecorders.Click += OnSaveRecorders;
        DockPanel.SetDock(saveRecorders, Dock.Right);
        RecorderBox.Style = St("Input");
        RecorderBox.FlowDirection = FlowDirection.LeftToRight;
        recorderRow.Children.Add(saveRecorders);
        recorderRow.Children.Add(RecorderBox);
        recordersBody.Children.Add(recorderRow);
        Put(recorders, 0, 4);

        // ---- power estimate ---------------------------------------------------------------------------
        var (power, powerBody) = Card(L.T("تخمین مصرف برق", "Power estimate"));
        powerBody.Children.Add(PowerHint);
        var fields = new UniformGrid { Columns = 2, Margin = new Thickness(-6, 0, -6, 0) };
        fields.Children.Add(Field(L.T("بیشترین توان گرافیک (وات)", "GPU maximum (W)"), GpuMaxBox));
        fields.Children.Add(Field(L.T("توان گرافیک در بیکاری", "GPU when idle (W)"), GpuIdleBox));
        fields.Children.Add(Field(L.T("بقیه‌ی قطعات (وات)", "Everything else (W)"), BaseBox));
        CpuMaxPanel.Children.Add(K.Text(L.T("بیشترین توان پردازنده", "CPU maximum (W)"), "Label"));
        ((TextBlock)CpuMaxPanel.Children[0]).Margin = new Thickness(0, 0, 0, 6);
        StyleNumberBox(CpuMaxBox);
        CpuMaxPanel.Children.Add(CpuMaxBox);
        fields.Children.Add(CpuMaxPanel);
        powerBody.Children.Add(fields);
        var savePower = new Button { Style = St("BtnPrimary"), Content = L.T("ذخیره", "Save"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0), Padding = new Thickness(26, 9, 26, 9) };
        savePower.Click += OnSavePower;
        powerBody.Children.Add(savePower);
        GpuMaxBox.Text = s.GpuMaxWatts.ToString("0");
        GpuIdleBox.Text = s.GpuIdleWatts.ToString("0");
        BaseBox.Text = s.BaseWatts.ToString("0");
        CpuMaxBox.Text = s.CpuMaxWatts.ToString("0");
        RecorderBox.Text = s.RecorderProcesses;
        Put(power, 1, 1);

        // ---- back up and restore ----------------------------------------------------------------------
        var (backup, backupBody) = Card(L.T("ذخیره و بازیابی تنظیمات", "Back up and restore settings"),
            L.T("یه فایل از تنظیمات (و اگه بخوای پروفایل‌ها و کارها) می‌سازه که روی سیستم دیگه یا بعد از نصب دوباره برمی‌گردونیش. اطلاعات اتصال به ربات و کلیدهای خصوصی توش نیست.", "Makes a file of your settings (and, if you choose, profiles and tasks) to bring back on another PC or after a reinstall. Bot link details and private keys are not included."));
        var backupRows = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        backupRows.Children.Add(SwitchRow(BackupProfiles, L.T("پروفایل‌ها و خودکارسازی‌ها هم باشن", "Include profiles and automations"), null, true));
        backupRows.Children.Add(SwitchRow(BackupTasks, L.T("کارها (لیست کارهای من) هم باشن", "Include my tasks"), null));
        backupBody.Children.Add(backupRows);
        var backupButtons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var export = new Button { Style = St("Btn"), Content = L.T("ذخیره‌ی تنظیمات…", "Save settings…"), Margin = new Thickness(0, 0, 8, 0) };
        export.Click += OnExportSettings;
        var import = new Button { Style = St("Btn"), Content = L.T("بازیابی از فایل…", "Restore from a file…") };
        import.Click += OnImportSettings;
        backupButtons.Children.Add(export);
        backupButtons.Children.Add(import);
        backupBody.Children.Add(backupButtons);
        backupBody.Children.Add(BackupHint);
        Put(backup, 1, 3);

        // ---- rating -----------------------------------------------------------------------------------
        Put(new RatingCard(), 1, 5);

        // ---- recent notices ---------------------------------------------------------------------------
        var (notices, noticesBody) = Card(L.T("اعلان‌های اخیر", "Recent notices"), L.T("فقط توی حافظه نگه داشته می‌شن و با بستن برنامه پاک می‌شن.", "Kept in memory only and gone when the app closes."));
        noticesBody.Children.Add(NoticeList);
        Put(notices, 1, 6);

        // ---- about ------------------------------------------------------------------------------------
        var (about, aboutBody) = Card(L.T("شب‌پا", "Shabpa"),
            L.T("ساخته‌ی Mr.PitPot. داده‌هات روی همین سیستم می‌مونن. فقط چیزهایی که خودت روشن می‌کنی به اینترنت وصل می‌شن: بررسی به‌روزرسانی (GitHub)، آب‌وهوا، ربات تلگرام و ارسال تیکت پشتیبانی. صدای میکروفون (نویزگیر) فقط لحظه‌ای توی حافظه پردازش می‌شه و ذخیره نمی‌شه؛ «یادگیری دستی» هم فقط جای کلیک‌های ماوس روی پنجره‌ی لانچر رو نگه می‌داره، نه کلید یا متن.",
                "Made by Mr.PitPot. Your data stays on this PC. Only what you switch on uses the internet: the update check (GitHub), weather, the Telegram bot and support tickets. Microphone sound (noise suppression) is only processed for a moment in memory and never stored; “Teach by hand” keeps only where you clicked in a launcher window, never keys or text."));
        var folder = new Button { Style = St("Btn"), Content = L.T("پوشه‌ی داده‌ها", "Data folder"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
        folder.Click += OnOpenFolder;
        var tour = new Button { Style = St("Btn"), Content = L.T("دیدن دوباره‌ی تور آموزشی", "See the guided tour again"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(tour, Tour.TourSteps.ReplayId);
        tour.Click += (_, _) => (Window.GetWindow(this) as MainWindow)?.StartTour();
        aboutBody.Children.Add(new WrapPanel { Children = { folder, tour } });
        folder.Margin = new Thickness(0, 12, 8, 0);
        Put(about, 1, 7);

        foreach (var (_, column, card) in _cards.OrderBy(c => c.order))
        {
            ColumnsPanel.SetColumn(card, column);
            _cols.Children.Add(card);
        }
        Content = _cols;
    }

    /// <summary>A glass card whose cards sit one under the other: title, optional subtitle and a body.</summary>
    private static (GlassCard card, StackPanel body) Card(string title, string? subtitle = null)
    {
        var (card, body) = K.Card(title, subtitle);
        card.Margin = new Thickness(0);
        card.Padding = new Thickness(22, 20, 22, 18);
        return (card, body);
    }

    /// <summary>A label above a control that needs the whole width (the wrapping theme chips, long questions).</summary>
    private static UIElement Stacked(string label, UIElement control)
    {
        var box = new StackPanel();
        box.Children.Add(new Border { Height = 1, Background = Res("Line") });
        box.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 11, 0, 9) });
        box.Children.Add(control);
        ((FrameworkElement)control).Margin = new Thickness(0, 0, 0, 8);
        return box;
    }

    /// <summary>A 44 px option row that is itself the switch: label at the start, the switch at the end, hairline above.</summary>
    private static Grid SwitchRow(Switch toggle, string label, string? tip, bool first = false)
    {
        toggle.Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.Medium, Margin = new Thickness(0, 6, 0, 6) };
        toggle.HorizontalAlignment = HorizontalAlignment.Stretch;
        toggle.MinHeight = 44;
        if (tip is not null) toggle.ToolTip = tip;
        var grid = new Grid();
        grid.Children.Add(toggle);
        if (!first) grid.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Background = Res("Line"), IsHitTestVisible = false });
        return grid;
    }

    private static StackPanel Field(string label, TextBox box)
    {
        var panel = new StackPanel { Margin = new Thickness(6) };
        panel.Children.Add(new TextBlock { Text = label, Style = St("Label"), Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap });
        StyleNumberBox(box);
        panel.Children.Add(box);
        return panel;
    }

    private static void StyleNumberBox(TextBox box)
    {
        box.Style = St("Input");
        box.FlowDirection = FlowDirection.LeftToRight;
        box.TextAlignment = TextAlignment.Center;
    }

    /// <summary>One selectable swatch per theme, drawn in that theme's own colours.</summary>
    private void BuildThemes()
    {
        foreach (var palette in Palette.All)
        {
            static Brush Paint(Palette p, string key)
            {
                var (r, g, b) = p.Rgb(key);
                return new SolidColorBrush(Color.FromRgb(r, g, b));
            }

            var swatch = new Border
            {
                Width = 40, Height = 26, CornerRadius = new CornerRadius(8), Background = Paint(palette, "Bg"),
                BorderBrush = Paint(palette, "Line"), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 10, 0),
                Child = new System.Windows.Shapes.Ellipse { Width = 11, Height = 11, Fill = Paint(palette, "Accent") },
            };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(swatch);
            content.Children.Add(new TextBlock { Text = palette.Name, VerticalAlignment = VerticalAlignment.Center });

            var key = palette.Key;
            var chip = new RadioButton
            {
                Style = St("Chip"), GroupName = "themes", Content = content, Padding = new Thickness(9, 6, 14, 6),
                Margin = new Thickness(0, 0, 8, 8), IsChecked = Ui.BaseTheme.Key == key,
            };
            chip.Checked += (_, _) =>
            {
                if (Application.Current.MainWindow is MainWindow main) main.SwitchTheme(key, chip); // live, no restart
                else { Hub.Settings.Theme = key; Apply(); }
            };
            ThemePanel.Children.Add(chip);
        }
    }

    /// <summary>Language and theme are baked into every screen, so the app restarts to apply them.</summary>
    private void Apply()
    {
        Hub.Store.Save();
        if (Hub.Store.ReadOnly) return; // demo runs do not persist, so a restart would change nothing
        (Application.Current as App)?.Restart(page: "settings");
    }

    public override void Refresh()
    {
        // never a program on the window's thread: the logon task is asked in the background and the switch follows when it answers
        try
        {
            _autostartOn = Autostart.IsEnabledCached(now => Dispatcher.BeginInvoke(() =>
            {
                if (_autostartBusy || _autostartOn == now) return;
                _autostartOn = now;
                AutostartCheck.IsChecked = now;
            }));
            AutostartCheck.IsChecked = _autostartOn;
        }
        catch { AutostartCheck.IsEnabled = false; }
        MinimizedCheck.IsChecked = Hub.Settings.StartMinimized;
        ConfirmCheck.IsChecked = Hub.Settings.ConfirmActions;
        AssistantKeyBox.Set(Hub.Settings.AssistantKey);
        ReportCheck.IsChecked = Hub.Settings.GameReports;
        GameLogCheck.IsChecked = Hub.Settings.GameLog;
        RefreshNotices();
        AnimCheck.IsChecked = Hub.Settings.Animations;
        SoundCheck.IsChecked = Hub.Settings.UiSounds;
        LowMemoryCheck.IsChecked = Hub.Settings.LowMemory;
        AutostartHint.Text = Hub.Elevated
            ? L.T("برنامه الان با دسترسی ادمین بازه؛ اگه این رو روشن کنی، دفعه‌های بعد هم با همین دسترسی بالا میاد.", "The app is running as administrator now; turn this on and it will start that way next time too.")
            : "";
        AutostartHint.Visibility = AutostartHint.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public override void OnTick(SensorSnapshot s)
    {
        // With a real CPU sensor the CPU ceiling is irrelevant.
        CpuMaxPanel.Visibility = s.CpuMeasured ? Visibility.Collapsed : Visibility.Visible;
        PowerHint.Text = s.CpuMeasured
            ? L.T("توان پردازنده واقعی خونده می‌شه. گرافیک و بقیه‌ی قطعات تخمینی‌ان؛ اگه عدد دقیق کارت گرافیکت رو می‌دونی اینجا وارد کن.",
                "CPU power is read from the real sensor. GPU and the rest are estimates; enter your graphics card's real figures here if you know them.")
            : L.T("همه‌ی عددها تخمینی‌ان؛ هر چی به سیستم خودت نزدیک‌تر باشن، هزینه دقیق‌تر درمیاد.", "All figures are estimates; the closer they are to your PC, the more accurate the cost.");
    }

    private void OnExportSettings(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.SaveFileDialog
        {
            FileName = $"shabpa-settings-{DateTime.Now:yyyyMMdd}.json", Filter = "Shabpa settings|*.json",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        try
        {
            System.IO.File.WriteAllText(dialog.FileName, SettingsBackup.Export(Hub.State, BackupProfiles.IsChecked == true, BackupTasks.IsChecked == true, DateTime.Now));
            BackupHint.Foreground = Res("Muted");
            BackupHint.Text = L.T("ذخیره شد.", "Saved.");
            Motion.Rise(BackupHint, 0, 8, 0.45);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            BackupHint.Foreground = Res("Red");
            BackupHint.Text = L.T("ذخیره نشد.", "Could not save.");
            Motion.Rise(BackupHint, 0, 8, 0.45);
        }
    }

    private void OnImportSettings(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.OpenFileDialog { Filter = "Shabpa settings|*.json", CheckFileExists = true };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        SettingsPack? pack;
        try
        {
            if (new System.IO.FileInfo(dialog.FileName).Length > SettingsBackup.MaxBytes) pack = null;
            else pack = SettingsBackup.Read(System.IO.File.ReadAllText(dialog.FileName));
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            pack = null;
        }
        if (pack is null)
        {
            BackupHint.Foreground = Res("Red");
            BackupHint.Text = L.T("این فایل تنظیمات شب‌پا نیست یا خونده نشد.", "This is not a Shabpa settings file, or it could not be read.");
            Motion.Rise(BackupHint, 0, 8, 0.45);
            return;
        }
        var detail = L.T($"تنظیمات{(pack.Profiles is not null ? $"، {Fa.Digits(pack.Profiles.Count.ToString())} پروفایل" : "")}{(pack.Tasks is not null ? $"، {Fa.Digits(pack.Tasks.Count.ToString())} کار" : "")}",
            $"settings{(pack.Profiles is not null ? $", {pack.Profiles.Count} profile(s)" : "")}{(pack.Tasks is not null ? $", {pack.Tasks.Count} task(s)" : "")}");
        if (!ConfirmWindow.AskAlways(L.T("بازیابی تنظیمات", "Restore settings"),
                L.T($"تنظیمات فعلی با محتوای فایل ({pack.Created}) جایگزین می‌شن: {detail}. برنامه یه لحظه بسته و دوباره باز می‌شه. اطلاعات اتصال ربات و کلیدهای این سیستم دست‌نخورده می‌مونن.",
                    $"Your current settings are replaced by the file ({pack.Created}): {detail}. The app restarts for a moment. The bot link and keys of this PC are kept."),
                L.T("بازیابی کن", "Restore"))) return;
        SettingsBackup.Apply(pack, Hub.State);
        Hub.Store.Save();
        if (Hub.Store.ReadOnly) return;
        (Application.Current as App)?.Restart(page: "settings");
    }

    private void RefreshNotices()
    {
        NoticeList.Children.Clear();
        var items = Hub.Notices.AsEnumerable().Reverse().Take(15).ToList();
        if (items.Count == 0)
            NoticeList.Children.Add(new TextBlock { Text = L.T("اعلانی نبوده.", "No notices yet."), Style = St("Body"), Margin = new Thickness(0, 6, 0, 0) });
        var first = true;
        var fresh = 0;
        foreach (var n in items)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new IconBadge("bell", "HueA", 30, 15, 10) { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 0, 0) });
            var box = new StackPanel { Margin = new Thickness(12, 8, 0, 8) };
            box.Children.Add(new TextBlock { Text = $"{Fa.Clock(n.Time)} · {n.Title}", Style = St("Body"), FontWeight = FontWeights.SemiBold, Foreground = Res("Text"), LineHeight = 22 });
            if (n.Text.Length > 0) box.Children.Add(new TextBlock { Text = n.Text, Style = St("Label"), TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(box, 1);
            row.Children.Add(box);
            if (!first) row.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Background = Res("Line"), IsHitTestVisible = false });
            NoticeList.Children.Add(row);
            first = false;
            // a notice that arrived while the page is open slides in; the first draw is left to the page entrance
            if (_noticesSeen.Add((n.Time.Ticks, n.Title)) && _noticesPrimed) Motion.Rise(row, fresh++, 10, 0.5);
        }
        _noticesPrimed = true;
    }

    private void OnSoftware(object sender, RoutedEventArgs e)
    {
        Hub.Settings.SoftwareRender = SoftwareCheck.IsChecked != true;
        Apply(); // the drawing mode is chosen once, when the app starts
    }

    private void OnLowMemory(object sender, RoutedEventArgs e)
    {
        Hub.Settings.LowMemory = LowMemoryCheck.IsChecked == true;
        Apply(); // the drawing mode is chosen once, when the app starts
    }

    private bool _autostartOn, _autostartBusy;

    private void OnBehaviour(object sender, RoutedEventArgs e)
    {
        Hub.Settings.StartMinimized = MinimizedCheck.IsChecked == true;
        Hub.Settings.ConfirmActions = ConfirmCheck.IsChecked == true;
        Hub.Settings.GameReports = ReportCheck.IsChecked == true;
        Hub.Settings.GameLog = GameLogCheck.IsChecked == true;
        Hub.Telemetry.LogWhileGameAlive = Hub.Settings.GameLog;
        Hub.Settings.Animations = AnimCheck.IsChecked == true;
        Hub.Settings.UiSounds = SoundCheck.IsChecked == true;
        Hub.Store.Save();
        if (Hub.Demo) return;
        var wanted = AutostartCheck.IsChecked == true;
        if (wanted == _autostartOn || _autostartBusy) { Refresh(); return; } // another switch was clicked: nothing to change for Windows
        _autostartOn = wanted;
        _autostartBusy = true;
        var exe = Hub.ExePath;
        var elevated = Hub.Elevated;
        // the registry and the logon task are touched off the window's thread (schtasks can take seconds)
        _ = Task.Run(() =>
        {
            try
            {
                if (Autostart.Set(wanted, exe, elevated) is { } error)
                    Log.Write($"autostart: {error}");
            }
            catch (Exception ex) { Log.Write($"autostart: {ex.Message}"); }
            Dispatcher.BeginInvoke(() =>
            {
                _autostartBusy = false;
                Refresh();
            });
        });
    }

    private void OnSavePower(object sender, RoutedEventArgs e)
    {
        var s = Hub.Settings;
        s.GpuMaxWatts = Number(GpuMaxBox, 0, 1000, s.GpuMaxWatts);
        s.GpuIdleWatts = Number(GpuIdleBox, 0, s.GpuMaxWatts, s.GpuIdleWatts);
        s.BaseWatts = Number(BaseBox, 0, 1000, s.BaseWatts);
        s.CpuMaxWatts = Number(CpuMaxBox, 0, 1000, s.CpuMaxWatts);
        GpuMaxBox.Text = s.GpuMaxWatts.ToString("0");
        GpuIdleBox.Text = s.GpuIdleWatts.ToString("0");
        BaseBox.Text = s.BaseWatts.ToString("0");
        CpuMaxBox.Text = s.CpuMaxWatts.ToString("0");
        Hub.Store.Save();
        // the four boxes pulse once: the values were taken (and clamped, if they were out of range)
        foreach (var box in new[] { GpuMaxBox, GpuIdleBox, BaseBox, CpuMaxBox }) Motion.Pop(box);
    }

    private void OnSaveRecorders(object sender, RoutedEventArgs e)
    {
        Hub.Settings.RecorderProcesses = RecorderBox.Text.Trim();
        Hub.Store.Save();
        Motion.Pop(RecorderBox);
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(Hub.Store.Folder);
            Process.Start(new ProcessStartInfo(Hub.Store.Folder) { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Write($"open folder: {ex.Message}"); }
    }
}
