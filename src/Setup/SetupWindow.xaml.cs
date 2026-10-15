using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace Shabpa.Setup;

public partial class SetupWindow : Window
{
    private enum Stage { Ready, Working, Done, Failed }

    private readonly bool _uninstall;
    private readonly InstallTarget _target = InstallTarget.Machine;
    private readonly string? _installed;
    private string _version = "2.5.1";
    private Stage _stage = Stage.Ready;
    private string _error = "";
    private bool _en;
    private readonly bool _runtimeReady = Runtime.IsInstalled();

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public SetupWindow(bool uninstall)
    {
        InitializeComponent();
        _uninstall = uninstall;
        _installed = Installer.InstalledVersion(_target);
        _en = ReadLanguage() == "en";
        if (!uninstall)
        {
            try { _version = Payload.Unpack(Installer.EmbeddedPayload()).version; }
            catch (Exception ex) { _stage = Stage.Failed; _error = ex.Message; }
        }
        Render();
    }

    /// <summary>Follow the language the user already picked inside the app, Persian otherwise.</summary>
    private static string ReadLanguage()
    {
        try
        {
            var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Shabpa", "state.json");
            var match = Regex.Match(File.ReadAllText(file), "\"Language\"\\s*:\\s*\"(\\w+)\"");
            return match.Success ? match.Groups[1].Value : "fa";
        }
        catch
        {
            return "fa";
        }
    }

    private string T(string fa, string en) => _en ? en : fa;

    /// <summary>"2.0.0" reads better as "2.0".</summary>
    private static string Short(string version) => version.EndsWith(".0", StringComparison.Ordinal) && version.Count(c => c == '.') > 1 ? version.Substring(0, version.Length - 2) : version;

    private string Digits(string text) =>
        _en ? text : string.Concat(text.Select(c => c is >= '0' and <= '9' ? (char)('۰' + (c - '0')) : c == '.' ? '٫' : c));

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var dark = 1;
        DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        var caption = 0x00180D0A;
        DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));
    }

    private void Render()
    {
        FlowDirection = _en ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;
        Title = _uninstall ? T("حذف شب‌پا", "Uninstall Shabpa") : T("نصب شب‌پا", "Shabpa Setup");
        LanguageButton.Content = _en ? "فارسی" : "English";
        LanguageButton.Visibility = _stage == Stage.Ready ? Visibility.Visible : Visibility.Hidden;
        TitleText.Text = _uninstall ? T("حذف شب‌پا", "Uninstall Shabpa") : T($"شب‌پا {Digits(Short(_version))}", $"Shabpa {Short(_version)}");
        SubtitleText.Text = T("نگهبان شبانه‌ی سیستم · ساخته‌ی Mr.PitPot", "Your PC night watch · by Mr.PitPot");
        PathLabel.Text = T("محل نصب", "Install location");
        PathText.Text = _target.Dir;

        DesktopCheck.Content = T("میان‌بر روی دسکتاپ", "Desktop shortcut");
        AutostartCheck.Content = T("همراه ویندوز اجرا بشه (با دسترسی ادمین، برای محدودیت سرعت)", "Start with Windows (as administrator, for speed limits)");
        LaunchCheck.Content = T("بعد از نصب باز بشه", "Open when done");
        RemoveDataCheck.Content = T("تنظیمات و تاریخچه هم پاک بشه", "Also delete settings and history");

        Options.Visibility = !_uninstall && _stage == Stage.Ready ? Visibility.Visible : Visibility.Collapsed;
        RemoveDataCheck.Visibility = _uninstall && _stage == Stage.Ready ? Visibility.Visible : Visibility.Collapsed;
        ProgressPanel.Visibility = _stage == Stage.Working ? Visibility.Visible : Visibility.Collapsed;

        NoteText.Foreground = (System.Windows.Media.Brush)FindResource(_stage == Stage.Failed ? "Red" : _stage == Stage.Done ? "Green" : "Muted");
        NoteText.Text = _stage switch
        {
            Stage.Failed => T($"انجام نشد: {_error}", $"Something went wrong: {_error}"),
            Stage.Done when _uninstall => T("شب‌پا حذف شد.", "Shabpa was removed."),
            Stage.Done => T("نصب شد. شب‌پا رو از منوی استارت یا میان‌بر دسکتاپ باز کن.", "Installed. Open Shabpa from the Start menu or the desktop shortcut."),
            Stage.Working => _uninstall ? T("در حال حذف…", "Removing…") : T("در حال نصب…", "Installing…"),
            _ when _uninstall => T("شب‌پا، زمان‌بندی‌ها، بیدارباش‌ها و میان‌برهاش از این سیستم حذف می‌شن.", "Shabpa, its schedules, wake timers and shortcuts will be removed from this PC."),
            _ when _installed is not null => T($"نسخه‌ی {Digits(Short(_installed))} نصبه و به {Digits(Short(_version))} به‌روز می‌شه. تنظیمات و تاریخچه‌ات سر جاش می‌مونه.",
                $"Version {Short(_installed)} is installed and will be updated to {Short(_version)}. Your settings and history are kept."),
            _ when !_runtimeReady => T("این سیستم ‎.NET 10 رو نداره؛ نصب‌کننده خودش نسخه‌ی رسمیش رو از سایت مایکروسافت دانلود و نصب می‌کنه (حدود ۶۰ مگابایت، یک بار برای همیشه).",
                "This PC does not have .NET 10 yet; the installer downloads and installs Microsoft's official package (about 60 MB, once)."),
            _ => T("خاموشی زمان‌بندی‌شده، بیدارباش، مصرف برق، پایداری پینگ، محدودیت سرعت هر برنامه و خالی کردن رم، همه توی یه برنامه‌ی سبک.",
                "Scheduled shutdowns, wake timers, power use, ping stability, per-app speed limits and RAM cleanup in one light app."),
        };

        MainButton.Content = _stage switch
        {
            Stage.Done when !_uninstall => T("باز کردن شب‌پا", "Open Shabpa"),
            Stage.Done or Stage.Failed => T("بستن", "Close"),
            _ when _uninstall => T("حذف کن", "Uninstall"),
            _ when _installed is not null => T("به‌روزرسانی", "Update"),
            _ => T("نصب", "Install"),
        };
        MainButton.IsEnabled = _stage != Stage.Working;
        CloseButton.Content = T("انصراف", "Cancel");
        CloseButton.Visibility = _stage == Stage.Ready ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumnSpan(MainButton, _stage == Stage.Ready ? 1 : 3);
    }

    private void OnLanguage(object sender, RoutedEventArgs e)
    {
        _en = !_en;
        Render();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private async void OnMain(object sender, RoutedEventArgs e)
    {
        switch (_stage)
        {
            case Stage.Done when !_uninstall:
                try { Installer.Launch(_target); } catch { /* opened from the shortcut instead */ }
                Close();
                return;
            case Stage.Done or Stage.Failed:
                Close();
                return;
        }

        var desktop = DesktopCheck.IsChecked == true;
        var autostart = AutostartCheck.IsChecked == true;
        var launch = LaunchCheck.IsChecked == true;
        var removeData = RemoveDataCheck.IsChecked == true;
        _stage = Stage.Working;
        Progress.Value = 0.02;
        StatusText.Text = T("بستن نسخه‌ی در حال اجرا…", "Closing the running copy…");
        Render();

        var progress = new Progress<double>(v => Progress.Value = Math.Max(Progress.Value, v));
        try
        {
            await Task.Run(() =>
            {
                Installer.StopRunningApp();
                if (_uninstall)
                {
                    Installer.Uninstall(_target, removeData, scheduleFolderRemoval: true);
                    ((IProgress<double>)progress).Report(1);
                    return;
                }
                if (!Runtime.IsInstalled())
                {
                    Dispatcher.Invoke(() => StatusText.Text = T("دانلود ‎.NET 10 از سایت مایکروسافت (حدود ۶۰ مگابایت)…", "Downloading .NET 10 from Microsoft (about 60 MB)…"));
                    var runtimeError = Runtime.InstallAsync(v => ((IProgress<double>)progress).Report(0.05 + 0.6 * v), T).GetAwaiter().GetResult();
                    if (runtimeError is not null) throw new InvalidOperationException(runtimeError);
                }
                Dispatcher.Invoke(() => StatusText.Text = T("کپی فایل‌ها…", "Copying files…"));
                Installer.Install(_target, Installer.EmbeddedPayload(), new InstallOptions(desktop, autostart), App.SelfPath, ((IProgress<double>)progress).Report);
                Dispatcher.Invoke(() => StatusText.Text = T("تنظیم اجرا همراه ویندوز…", "Setting up start with Windows…"));
                Installer.SetAutostart(_target, autostart);
            });
            _stage = Stage.Done;
            StatusText.Text = "";
            if (!_uninstall && launch)
            {
                try { Installer.Launch(_target); } catch { /* the user can open it from the shortcut */ }
            }
        }
        catch (Exception ex)
        {
            _stage = Stage.Failed;
            _error = ex.Message;
        }
        Render();
    }
}
