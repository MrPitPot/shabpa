using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Shabpa.Setup;

public partial class SetupWindow : Window
{
    private enum Stage { Ready, Working, Done, Failed }

    private readonly bool _uninstall;
    private readonly InstallTarget _target = InstallTarget.Machine;
    private string? _installed;
    private string _version = "3.11.2";
    private Stage _stage = Stage.Ready;
    private string _error = "";
    private bool _en;
    private bool _runtimeReady = Runtime.IsInstalled();
    private double _progress;
    private bool _leaving;
    private readonly Segmented _language;

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
            catch (Exception ex) { _stage = Stage.Failed; _error = ex.Message; SetupLog.Error("payload could not be read", ex); }
        }

        _language = new Segmented(new[] { "فارسی", "English" }, _en ? 1 : 0);
        _language.Changed += i =>
        {
            _en = i == 1;
            Go();
        };
        LangHost.Content = _language;
        Fx.SetLift(MinButton, 0);
        Fx.SetLift(CloseWindowButton, 0);
        Fx.SetPress(MinButton, 0.92);
        Fx.SetPress(CloseWindowButton, 0.92);
        RingCtl.FractionChanged += f => PercentText.Text = Digits(((int)Math.Round(f * 100)).ToString()) + (_en ? "%" : "٪");

        Render();
        Loaded += (_, __) => Enter(first: true);
    }

    /// <summary>Follow the language the user already picked inside the app, Persian otherwise.</summary>
    private static string ReadLanguage()
    {
        try
        {
            var file = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Shabpa", "state.json");
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
        var round = 2; // DWMWCP_ROUND: Windows 11 rounds the window like the design's plate (ignored where unsupported)
        DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
        var border = 0x0057312B; // hairline around the plate (COLORREF)
        DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int));
    }

    private void Render()
    {
        FlowDirection = _en ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;
        Title = _uninstall ? T("حذف شب‌پا", "Uninstall Shabpa") : T("نصب شب‌پا", "Shabpa Setup");
        WindowTitleText.Text = Title;
        _language.Select(_en ? 1 : 0, raise: false);
        LangHost.Visibility = _stage == Stage.Ready ? Visibility.Visible : Visibility.Hidden;
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
        OptionsCard.Visibility = Options.Visibility == Visibility.Visible || RemoveDataCheck.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        ProgressPanel.Visibility = _stage == Stage.Working ? Visibility.Visible : Visibility.Collapsed;
        Progress.Shimmer(_stage == Stage.Working);

        NoteText.Foreground = (Brush)FindResource(_stage == Stage.Failed ? "Red" : _stage == Stage.Done ? "Green" : "Muted");
        NoteText.Text = _stage switch
        {
            Stage.Failed => T($"انجام نشد: {_error}", $"Something went wrong: {_error}"),
            Stage.Done when _uninstall => T("شب‌پا حذف شد.", "Shabpa was removed."),
            Stage.Done => T("نصب شد. شب‌پا رو از منوی استارت یا میان‌بر دسکتاپ باز کن.", "Installed. Open Shabpa from the Start menu or the desktop shortcut."),
            Stage.Working => "",
            _ when _uninstall => T("شب‌پا، زمان‌بندی‌ها، بیدارباش‌ها و میان‌برهاش از این سیستم حذف می‌شن.", "Shabpa, its schedules, wake timers and shortcuts will be removed from this PC."),
            _ when _installed is not null => T($"نسخه‌ی {Digits(Short(_installed))} نصبه و به {Digits(Short(_version))} به‌روز می‌شه. تنظیمات و تاریخچه‌ات سر جاش می‌مونه.",
                $"Version {Short(_installed)} is installed and will be updated to {Short(_version)}. Your settings and history are kept."),
            _ when !_runtimeReady => T("این سیستم ‎.NET 10 رو نداره؛ نصب‌کننده خودش نسخه‌ی رسمیش رو از سایت مایکروسافت دانلود و نصب می‌کنه (حدود ۶۰ مگابایت، یک بار برای همیشه).",
                "This PC does not have .NET 10 yet; the installer downloads and installs Microsoft's official package (about 60 MB, once)."),
            _ => T("خاموشی زمان‌بندی‌شده، بیدارباش، مصرف برق، پایداری پینگ، محدودیت سرعت هر برنامه و خالی کردن رم، همه توی یه برنامه‌ی سبک.",
                "Scheduled shutdowns, wake timers, power use, ping stability, per-app speed limits and RAM cleanup in one light app."),
        };
        NoteText.Visibility = NoteText.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        HeadText.Text = _stage switch
        {
            Stage.Failed when _uninstall => T("حذف کامل نشد", "Removal did not finish"),
            Stage.Failed => T("نصب کامل نشد", "Setup did not finish"),
            Stage.Done when _uninstall => T("حذف کامل شد", "Removed"),
            Stage.Done => T("همه‌چیز آماده‌ست", "You're all set"),
            Stage.Working => _uninstall ? T("در حال حذف…", "Removing…") : T("در حال نصب…", "Installing…"),
            _ when _uninstall => T("شب‌پا حذف بشه؟", "Remove Shabpa?"),
            _ when _installed is not null => T("آماده‌ی به‌روزرسانی", "Ready to update"),
            _ => T("آماده‌ی نصب", "Ready to install"),
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

        RenderMark();
    }

    /// <summary>Badge / ring / result mark and the card layout (compact row while choosing, centred hero otherwise).</summary>
    private void RenderMark()
    {
        var hero = _stage != Stage.Ready;
        Mark.Width = Mark.Height = hero ? 124 : 88;
        Mark.Margin = hero ? new Thickness(0, 4, 0, 12) : new Thickness(0, 0, 20, 0);
        MainCard.MinHeight = hero ? 380 : _uninstall ? 312 : 0;
        Grid.SetColumn(Mark, 0);
        Grid.SetColumnSpan(Mark, hero ? 2 : 1);
        Grid.SetRow(Mark, 0);
        Mark.HorizontalAlignment = hero ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        Mark.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(TextStack, hero ? 0 : 1);
        Grid.SetColumnSpan(TextStack, hero ? 2 : 1);
        Grid.SetRow(TextStack, hero ? 1 : 0);
        TextStack.HorizontalAlignment = hero ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        HeadText.TextAlignment = hero ? TextAlignment.Center : TextAlignment.Left;
        HeadText.FontSize = hero ? 20 : 18;
        NoteText.TextAlignment = hero ? TextAlignment.Center : TextAlignment.Left;
        TextStack.MaxWidth = hero ? 400 : double.PositiveInfinity;

        ReadyBadge.Visibility = _stage == Stage.Ready ? Visibility.Visible : Visibility.Collapsed;
        WorkRing.Visibility = _stage == Stage.Working ? Visibility.Visible : Visibility.Collapsed;
        ResultHost.Visibility = _stage is Stage.Done or Stage.Failed ? Visibility.Visible : Visibility.Collapsed;

        if (_stage == Stage.Ready)
        {
            ReadyIcon.Glyph = _uninstall ? "trash" : _installed is not null ? "refresh" : !_runtimeReady ? "cloud" : "dl";
            ReadyBg.Background = (Brush)FindResource(_uninstall ? "DangerGradient" : "AccentGradient");
            ReadyGlow.Color = _uninstall ? Color.FromArgb(0x99, 0xE0, 0x47, 0x5E) : K.C("AccentGlowC");
        }
        else if (_stage == Stage.Working)
        {
            RingCtl.Width = RingCtl.Height = 116;
            PercentText.FlowDirection = _en ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;
            PercentText.Text = Digits(((int)Math.Round(RingCtl.Fraction * 100)).ToString()) + (_en ? "%" : "٪");
        }
        else
        {
            var failed = _stage == Stage.Failed;
            ResultBg.Background = (Brush)FindResource(failed ? "DangerGradient" : "OkGradient");
            ResultGlow.Color = failed ? Color.FromArgb(0x99, 0xE0, 0x47, 0x5E) : K.C("GreenGlowC");
            CheckPath.Visibility = failed ? Visibility.Collapsed : Visibility.Visible;
            AlertIcon.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
            Pulse1.Stroke = Pulse2.Stroke = (Brush)FindResource(failed ? "Red" : "Green");
            // rest state; the moment itself is played after the cards have arrived
            // with motion the tick starts hidden (Celebrate draws it left to right); without motion it is simply there
            CheckPath.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
            CheckPath.BeginAnimation(OpacityProperty, null);
            CheckPath.StrokeDashOffset = Fx.Enabled && !failed ? 9 : 0;
            CheckPath.Opacity = Fx.Enabled && !failed ? 0 : 1;
            Pulse1.Opacity = Pulse2.Opacity = 0;
        }
    }

    // ---- motion -----------------------------------------------------------------------------------------------

    /// <summary>Show the next state: the current cards fade and slide out, Render() swaps the content, the new cards rise in.
    /// Render() always reads the latest state, so overlapping requests collapse into one swap.</summary>
    private void Go()
    {
        if (!Fx.Enabled || !IsLoaded)
        {
            Render();
            return;
        }
        if (_leaving) return;
        _leaving = true;
        Root.IsHitTestVisible = false;
        var (_, stageMove) = Fx.Rig(StageHost);
        var (_, rowMove) = Fx.Rig(ButtonsRow);
        void Out(UIElement e, TranslateTransform move)
        {
            e.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(130)) { FillBehavior = FillBehavior.HoldEnd });
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -8, TimeSpan.FromMilliseconds(130)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }, FillBehavior = FillBehavior.HoldEnd });
        }
        Out(StageHost, stageMove);
        Out(ButtonsRow, rowMove);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(135) };
        timer.Tick += (_, __) =>
        {
            timer.Stop();
            foreach (var (e, move) in new[] { ((UIElement)StageHost, stageMove), (ButtonsRow, rowMove) })
            {
                e.BeginAnimation(OpacityProperty, null);
                e.Opacity = 1;
                move.BeginAnimation(TranslateTransform.YProperty, null);
                move.Y = 0;
            }
            _leaving = false;
            Root.IsHitTestVisible = true;
            Render();
            Enter(first: false);
        };
        timer.Start();
    }

    /// <summary>Staggered rise of the cards (design: .page.in .r, 55 ms apart) and the state's own moment.</summary>
    private void Enter(bool first)
    {
        var step = 0;
        if (first)
        {
            Fx.Rise(Header, 0, 16, 700);
            step = 1;
        }
        Fx.Rise(MainCard, step++ * 60 + (first ? 20 : 0));
        if (OptionsCard.Visibility == Visibility.Visible) Fx.Rise(OptionsCard, step++ * 60 + (first ? 20 : 0));
        Fx.Rise(ButtonsRow, step * 60 + (first ? 20 : 0), 16, 700);

        var stage = _stage;
        Later(first ? 380 : 330, () =>
        {
            if (_stage != stage) return;
            switch (stage)
            {
                case Stage.Ready: Fx.Pop(ReadyBadge, 0.55, 750); break;
                case Stage.Done: Celebrate(); break;
                case Stage.Failed: Shake(); break;
            }
        });
    }

    private static void Later(int ms, Action action)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, __) => { timer.Stop(); action(); };
        timer.Start();
    }

    /// <summary>Completion moment: the badge springs in, the check draws itself, rings pulse out, dots burst (install only), the button gleams.</summary>
    private void Celebrate()
    {
        if (!Fx.Enabled) return;
        Fx.Pop(ResultBadge, 0.25, 850);
        var ease = Fx.Out;
        // the tick draws itself (stroke dash), fading in so the dash start cap never shows as a dot
        void Draw(DependencyProperty p, double from, double to, int ms, IEasingFunction? e)
        {
            CheckPath.SetValue(p, from);
            var a = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { BeginTime = TimeSpan.FromMilliseconds(260), EasingFunction = e, FillBehavior = FillBehavior.HoldEnd };
            a.Completed += (_, __) => { CheckPath.BeginAnimation(p, null); CheckPath.SetValue(p, to); };
            CheckPath.BeginAnimation(p, a);
        }
        Draw(Shape.StrokeDashOffsetProperty, 9, 0, 520, ease); // dash 9 x 2.8 > the tick (24 units): the visible part grows from the left start to the top right, never the other way
        Draw(OpacityProperty, 0, 1, 90, null);
        Pulse(Pulse1, 0);
        Pulse(Pulse2, 260);
        if (!_uninstall) Confetti();
        Later(520, () => MainButton.Sweep());
    }

    private static void Pulse(Ellipse ring, int delayMs)
    {
        var rig = Fx.Rig(ring);
        var time = TimeSpan.FromMilliseconds(1200);
        var begin = TimeSpan.FromMilliseconds(delayMs);
        var easeOut = Fx.Out;
        foreach (var p in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
            rig.scale.BeginAnimation(p, new DoubleAnimation(0.9, 1.9, time) { BeginTime = begin, EasingFunction = easeOut, FillBehavior = FillBehavior.Stop });
        var fade = new DoubleAnimationUsingKeyFrames { BeginTime = begin, Duration = time, FillBehavior = FillBehavior.Stop };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.85, KeyTime.FromPercent(0)));
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 3 }));
        ring.BeginAnimation(OpacityProperty, fade);
    }

    private void Confetti()
    {
        var colors = new[] { "AccentC", "GreenC", "GoldC", "PinkC", "AccentC", "GreenC" };
        var rnd = new Random(7);
        const int count = 18;
        for (var i = 0; i < count; i++)
        {
            var angle = i * 2 * Math.PI / count + (rnd.NextDouble() - 0.5) * 0.35;
            var dist = 78 + rnd.NextDouble() * 62;
            var size = 5 + rnd.NextDouble() * 5;
            var square = i % 3 == 0;
            var color = K.C(colors[i % colors.Length]);
            FrameworkElement dot = square
                ? new Rectangle { Width = size, Height = size * 0.6, RadiusX = 1.5, RadiusY = 1.5, Fill = new SolidColorBrush(color) }
                : new Ellipse { Width = size, Height = size, Fill = new SolidColorBrush(color) };
            dot.IsHitTestVisible = false;
            dot.Opacity = 0;
            var move = new TranslateTransform();
            var spin = new RotateTransform();
            var group = new TransformGroup();
            group.Children.Add(spin);
            group.Children.Add(move);
            dot.RenderTransformOrigin = new Point(0.5, 0.5);
            dot.RenderTransform = group;
            Canvas.SetLeft(dot, -dot.Width / 2);
            Canvas.SetTop(dot, -dot.Height / 2);
            Burst.Children.Add(dot);

            var delay = TimeSpan.FromMilliseconds(120 + rnd.Next(0, 90));
            var fly = TimeSpan.FromMilliseconds(950);
            var tx = Math.Cos(angle) * dist;
            var ty = Math.Sin(angle) * dist;
            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, tx, fly) { BeginTime = delay, EasingFunction = Fx.Out, FillBehavior = FillBehavior.HoldEnd });
            // a little gravity: the dots drift down while they fade
            var fall = new DoubleAnimationUsingKeyFrames { BeginTime = delay, FillBehavior = FillBehavior.HoldEnd };
            fall.KeyFrames.Add(new EasingDoubleKeyFrame(ty, KeyTime.FromTimeSpan(fly), new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 }));
            fall.KeyFrames.Add(new EasingDoubleKeyFrame(ty + 26, KeyTime.FromTimeSpan(fly + TimeSpan.FromMilliseconds(500)), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
            move.BeginAnimation(TranslateTransform.YProperty, fall);
            spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, (rnd.NextDouble() - 0.5) * 540, fly + TimeSpan.FromMilliseconds(500)) { BeginTime = delay, FillBehavior = FillBehavior.HoldEnd });
            var opacity = new DoubleAnimationUsingKeyFrames { BeginTime = delay, FillBehavior = FillBehavior.HoldEnd };
            opacity.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(650))));
            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1450))));
            dot.BeginAnimation(OpacityProperty, opacity);
        }
        // nothing is left behind once the burst is over
        Later(1800, () => Burst.Children.Clear());
    }

    private void Shake()
    {
        if (!Fx.Enabled) return;
        Fx.Pop(ResultBadge, 0.6, 600);
        var (_, move) = Fx.Rig(ResultBadge);
        var shake = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        double[] xs = { 0, -9, 8, -6, 4, -2, 0 };
        for (var i = 0; i < xs.Length; i++)
            shake.KeyFrames.Add(new EasingDoubleKeyFrame(xs[i], KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(i * 75)), new SineEase { EasingMode = EasingMode.EaseInOut }));
        move.BeginAnimation(TranslateTransform.XProperty, shake);
        Pulse(Pulse1, 0);
    }

    // ---- behaviour (unchanged) --------------------------------------------------------------------------------

    private void SetProgress(double v)
    {
        _progress = Math.Max(_progress, v);
        Progress.GlideTo(_progress);
        RingCtl.GlideTo(_progress);
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

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
        _progress = 0;
        RingCtl.Fraction = 0;
        Progress.Value = 0.02;
        _progress = 0.02;
        RingCtl.Fraction = 0.02;
        StatusText.Text = T("بستن نسخه‌ی در حال اجرا…", "Closing the running copy…");
        MainButton.IsEnabled = false;
        Go();

        var progress = new Progress<double>(SetProgress);
        try
        {
#if DEBUG || SHABPA_PREVIEW
            if (_preview != null)
            {
                await Simulate(progress);
            }
            else
#endif
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
            SetProgress(1);
            if (!_uninstall && launch && !IsPreview)
            {
                try { Installer.Launch(_target); } catch { /* the user can open it from the shortcut */ }
            }
        }
        catch (Exception ex)
        {
            _stage = Stage.Failed;
            _error = ex.Message;
            SetupLog.Error("the installer window run failed", ex);
        }
        Go();
    }

    private bool IsPreview
    {
        get
        {
#if DEBUG || SHABPA_PREVIEW
            return _preview != null;
#else
            return false;
#endif
        }
    }
}
