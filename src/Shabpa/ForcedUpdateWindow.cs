using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa;

/// <summary>«به‌روزرسانی لازمه»: shown when a newer, signed version is out. It starts the update by itself after a short countdown (or at once on the button): the same
/// download, the same signature and checksum checks and the same installer as the Updates page, nothing else. It has no close button while it counts or downloads;
/// if the update fails it says why and lets go, so a broken connection never traps the person. It never appears while a game is running.</summary>
public sealed class ForcedUpdateWindow : Window
{
    private static ForcedUpdateWindow? _open;

    public static void Show(UpdateInfo info)
    {
        if (_open is { IsLoaded: true }) return;
        _open = new ForcedUpdateWindow(info);
        if (MainOrNull() is { } main) _open.Owner = main;
        _open.Show();
        _open.Activate();
    }

    private readonly TextBlock _message = new() { Style = (Style)Application.Current.Resources["Body"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 16) };
    private readonly ProgressBar _bar = new() { Height = 8, Minimum = 0, Maximum = 1, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 14) };
    private readonly Button _go = new() { Style = (Style)Application.Current.Resources["BtnPrimary"], Padding = new Thickness(22, 10, 22, 10), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly UpdateInfo _info;
    private int _left = 20;
    private bool _busy, _canClose;

    /// <summary>The main window when it is shown and belongs to this thread (anything else: no owner, the dialog simply centres on the screen).</summary>
    private static Window? MainOrNull()
    {
        try { return Application.Current?.MainWindow is { IsVisible: true } m ? m : null; }
        catch (InvalidOperationException) { return null; }
    }

    private static Hub Hub => Hub.I;

    private ForcedUpdateWindow(UpdateInfo info)
    {
        _info = info;
        FlowDirection = Ui.Flow;
        Title = L.T("به‌روزرسانی شب‌پا", "Shabpa update");
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = true;
        Topmost = true;
        WindowStartupLocation = MainOrNull() is not null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        Background = (Brush)Application.Current.Resources["Bg"];
        FontFamily = (FontFamily)Application.Current.Resources["Ui"];
        SourceInitialized += (_, _) => Ui.Chrome(this);
        Closing += (_, e) => { if (!_canClose) e.Cancel = true; };

        var version = info.Version.ToString(3);
        var root = new StackPanel { Margin = new Thickness(28, 24, 28, 24) };
        root.Children.Add(new TextBlock { Text = L.T("نسخه‌ی تازه‌ی شب‌پا لازمه", "A new version of Shabpa is needed"), FontSize = 20, FontWeight = FontWeights.Bold, Foreground = (Brush)Application.Current.Resources["Text"] });
        root.Children.Add(_message);
        root.Children.Add(_bar);
        _go.Content = L.T("الان به‌روز کن", "Update now");
        _go.Click += (_, _) => Start();
        root.Children.Add(_go);
        Content = root;
        _message.Text = Countdown(version);

        Hub.Updater.Changed += OnChanged;
        Closed += (_, _) => { _timer.Stop(); Hub.Updater.Changed -= OnChanged; _open = null; };
        _timer.Tick += (_, _) =>
        {
            if (_busy) return;
            if (--_left <= 0) { Start(); return; }
            _message.Text = Countdown(version);
        };
        _timer.Start();
    }

    private string Countdown(string version) =>
        L.T($"نسخه‌ی {Fa.Digits(version)} اومده و برای ادامه باید نصب بشه. تنظیمات و اطلاعاتت سر جاشون می‌مونه. {Fa.Digits(_left.ToString())} ثانیه‌ی دیگه خودکار شروع می‌شه.",
            $"Version {version} is out and has to be installed to go on. Your settings and data stay. It starts by itself in {_left} seconds.");

    private void OnChanged() => Dispatcher.BeginInvoke(() => { _bar.Value = Hub.Updater.Progress; });

    private async void Start()
    {
        if (_busy) return;
        _busy = true;
        _timer.Stop();
        _go.IsEnabled = false;
        _bar.Visibility = Visibility.Visible;
        _message.Text = L.T("در حال دانلود و بررسی امضا و چک‌سام؛ بعدش نصب‌کننده باز می‌شه و شب‌پا دوباره بالا میاد.", "Downloading and checking the signature and checksum; then the installer opens and Shabpa comes back by itself.");
        var problem = await Hub.Updater.DownloadAndInstallAsync(Hub.Elevated);
        if (problem is null) return;                                   // the installer is running and will close this app
        _bar.Visibility = Visibility.Collapsed;
        _message.Text = problem + "\n" + L.T("بعداً خودکار دوباره امتحان می‌شه؛ تا اون موقع می‌تونی از شب‌پا استفاده کنی.", "It will be tried again later by itself; you can keep using Shabpa until then.");
        _canClose = true;
        _go.Content = L.T("باشه", "OK");
        _go.IsEnabled = true;
        _go.Click += (_, _) => Close();
    }
}
