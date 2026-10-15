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
        AnimCheck.IsChecked = Hub.Settings.Animations;
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
