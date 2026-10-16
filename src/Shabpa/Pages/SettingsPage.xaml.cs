using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Shabpa.Core;

namespace Shabpa.Pages;

public partial class SettingsPage : PageBase
{
    public override string PageTitle => L.T("تنظیمات", "Settings");
    public override string PageSubtitle => L.T("زبان، ظاهر و رفتار برنامه.", "Language, look and behaviour.");

    public SettingsPage()
    {
        InitializeComponent();
        var s = Hub.Settings;

        Chips(LanguageChips, [("fa", "فارسی"), ("en", "English")], L.En ? "en" : "fa", language =>
        {
            s.Language = language;
            Apply();
        });
        BuildThemes();
        GlassLevelSlider.Value = Math.Round(s.GlassLevel * 100);
        GlassLevelText.Text = $"{GlassLevelSlider.Value:0}%";
        GlassLevelRow.Visibility = Ui.IsGlass ? Visibility.Visible : Visibility.Collapsed;
        var glassTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        glassTimer.Tick += (_, _) => { glassTimer.Stop(); Apply(); }; // the look is built at start, so the app reopens once the slider rests
        GlassLevelSlider.ValueChanged += (_, _) =>
        {
            GlassLevelText.Text = $"{GlassLevelSlider.Value:0}%";
            if (Math.Abs(s.GlassLevel - GlassLevelSlider.Value / 100) < 0.001) return;
            s.GlassLevel = GlassLevelSlider.Value / 100;
            glassTimer.Stop();
            glassTimer.Start();
        };
        Chips(GlassChips, [("off", L.T("خاموش", "Off")), ("mica", L.T("میکا (نرم)", "Mica (soft)")), ("acrylic", L.T("آکریلیک (شیشه‌ای)", "Acrylic (frosted)"))], Ui.CleanGlass(s.Glass), glass =>
        {
            if (glass == Ui.CleanGlass(s.Glass)) return;
            s.Glass = glass;
            Apply();
        });

        Chips(CountdownChips,
            [(10, Fa.Duration(TimeSpan.FromSeconds(10))), (30, Fa.Duration(TimeSpan.FromSeconds(30))), (60, Fa.Duration(TimeSpan.FromMinutes(1))), (120, Fa.Duration(TimeSpan.FromMinutes(2)))],
            s.CountdownSeconds, v =>
            {
                s.CountdownSeconds = v;
                Hub.Store.Save();
            });
        GpuMaxBox.Text = s.GpuMaxWatts.ToString("0");
        GpuIdleBox.Text = s.GpuIdleWatts.ToString("0");
        BaseBox.Text = s.BaseWatts.ToString("0");
        CpuMaxBox.Text = s.CpuMaxWatts.ToString("0");
        RecorderBox.Text = s.RecorderProcesses;
        var root = (StackPanel)Content;
        root.Children.Insert(root.Children.Count - 1, new RatingCard());
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
                Width = 44, Height = 28, CornerRadius = new CornerRadius(7), Background = Paint(palette, "Bg"),
                BorderBrush = Paint(palette, "Line"), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 10, 0),
                Child = new Ellipse { Width = 12, Height = 12, Fill = Paint(palette, "Accent") },
            };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(swatch);
            content.Children.Add(new TextBlock { Text = palette.Name, VerticalAlignment = VerticalAlignment.Center });

            var key = palette.Key;
            var chip = new RadioButton
            {
                Style = St("Chip"), GroupName = "themes", Content = content, Padding = new Thickness(10, 8, 14, 8),
                Margin = new Thickness(0, 0, 8, 8), IsChecked = Ui.Theme.Key == key,
            };
            chip.Checked += (_, _) =>
            {
                Hub.Settings.Theme = key;
                Apply();
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
        try { AutostartCheck.IsChecked = Autostart.IsEnabled(); }
        catch { AutostartCheck.IsEnabled = false; }
        MinimizedCheck.IsChecked = Hub.Settings.StartMinimized;
        ConfirmCheck.IsChecked = Hub.Settings.ConfirmActions;
        ReportCheck.IsChecked = Hub.Settings.GameReports;
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
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            BackupHint.Foreground = Res("Red");
            BackupHint.Text = L.T("ذخیره نشد.", "Could not save.");
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
            NoticeList.Children.Add(new TextBlock { Text = L.T("اعلانی نبوده.", "No notices yet."), Style = St("Body") });
        foreach (var n in items)
        {
            var box = new StackPanel { Margin = new Thickness(0, 5, 0, 5) };
            box.Children.Add(new TextBlock { Text = $"{Fa.Clock(n.Time)} · {n.Title}", Style = St("Body"), FontWeight = FontWeights.SemiBold });
            if (n.Text.Length > 0) box.Children.Add(new TextBlock { Text = n.Text, Style = St("Label"), TextWrapping = TextWrapping.Wrap });
            NoticeList.Children.Add(box);
        }
    }

    private void OnLowMemory(object sender, RoutedEventArgs e)
    {
        Hub.Settings.LowMemory = LowMemoryCheck.IsChecked == true;
        Apply(); // the drawing mode is chosen once, when the app starts
    }

    private void OnBehaviour(object sender, RoutedEventArgs e)
    {
        Hub.Settings.StartMinimized = MinimizedCheck.IsChecked == true;
        Hub.Settings.ConfirmActions = ConfirmCheck.IsChecked == true;
        Hub.Settings.GameReports = ReportCheck.IsChecked == true;
        Hub.Settings.Animations = AnimCheck.IsChecked == true;
        Hub.Settings.UiSounds = SoundCheck.IsChecked == true;
        Hub.Store.Save();
        if (Hub.Demo) return;
        try
        {
            if (Autostart.Set(AutostartCheck.IsChecked == true, Hub.ExePath, Hub.Elevated) is { } error)
                Log.Write($"autostart: {error}");
        }
        catch (Exception ex) { Log.Write($"autostart: {ex.Message}"); }
        Refresh();
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
    }

    private void OnSaveRecorders(object sender, RoutedEventArgs e)
    {
        Hub.Settings.RecorderProcesses = RecorderBox.Text.Trim();
        Hub.Store.Save();
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
