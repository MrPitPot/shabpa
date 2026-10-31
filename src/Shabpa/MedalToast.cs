using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa;

/// <summary>The "medal unlocked" pop-up. It rises out of the taskbar in the bottom-right corner, sits right on the taskbar's
/// edge for five seconds, then sinks back under it and fades. The window is exactly as tall as the card and its bottom is
/// the top of the taskbar, so the card is cut off at that edge while it moves: it looks like it slides out from behind the
/// taskbar. It never takes focus; a click opens the medals page. Several medals wait in line and come one after another,
/// and nothing pops up while a game runs (they are shown when it ends).</summary>
public sealed class MedalToast : Window
{
    private const double CardWidth = 340, CardHeight = 88, Gap = 12, HoldSeconds = 5;
    private static readonly Queue<(Medal medal, string title, string text)> Waiting = new();
    private static MedalToast? _showing;
    private static bool _hooked;

    private readonly Border _card;
    private readonly TranslateTransform _move = new(0, CardHeight);
    private readonly ScaleTransform _bar = new(1, 1);
    private readonly DispatcherTimerLite _hold;
    private bool _leaving;

    /// <summary>Puts a medal in line for the corner pop-up.</summary>
    public static void Enqueue(Medal medal) =>
        Enqueue(medal, L.T("مدال جدید!", "Medal unlocked!"), medal.Name, medal.How);

    /// <summary>Several medals at once (the first check after an update): one pop-up with the count, showing the rarest.</summary>
    public static void EnqueueMany(IReadOnlyList<Medal> medals)
    {
        if (medals.Count == 0) return;
        var best = medals.OrderByDescending(m => m.Tier).First();
        Enqueue(best, L.T("مدال‌های تازه!", "New medals!"),
            L.T($"{Fa.Digits(medals.Count.ToString())} مدال گرفتی", $"You earned {medals.Count} medals"),
            L.T("حقت بود! همه رو تو صفحه‌ی «مدال‌ها» ببین.", "Well deserved! See them all on the Medals page."));
    }

    private static void Enqueue(Medal medal, string label, string title, string text)
    {
        if (Hub.I is not { } hub || !hub.Settings.MedalToasts) return;
        Waiting.Enqueue((medal, label + "\n" + title, text));
        if (!_hooked)
        {
            _hooked = true;
            hub.Second += Pump; // retried once a second while a game is running
        }
        Pump();
    }

    private static void Pump()
    {
        if (_showing is not null || Waiting.Count == 0 || Hub.I is not { } hub) return;
        if (hub.Snapshot.GameRunning || hub.GameModeActive) return; // never over a game
        var (medal, title, text) = Waiting.Dequeue();
        var parts = title.Split('\n', 2);
        try
        {
            _showing = new MedalToast(medal, parts[0], parts[1], text);
            _showing.Show();
        }
        catch (Exception ex)
        {
            Log.Write($"medal toast failed: {ex.Message}");
            _showing = null;
        }
    }

    private MedalToast(Medal medal, string label, string title, string text)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Width = CardWidth;
        Height = CardHeight;
        FlowDirection = Ui.Flow;
        FontFamily = (FontFamily)Application.Current.Resources["Ui"];
        Cursor = Cursors.Hand;

        var tier = MedalArt.TierColor(medal.Tier);
        var amber = Color.FromRgb(0xFF, 0xD2, 0x7A);
        var grid = new Grid { Margin = new Thickness(14, 10, 14, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var art = new MedalArt(medal, true, 62) { VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(art);
        var texts = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = label, FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(amber) });
        texts.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) });
        texts.Children.Add(new TextBlock { Text = text, FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0xB9, 0xBB, 0xE8)), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) });
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);

        // the time left, as a thin line in the medal's colour along the bottom edge
        var line = new Border
        {
            Height = 3, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(14, 0, 14, 5),
            Background = new SolidColorBrush(tier), Opacity = 0.85, RenderTransform = _bar, RenderTransformOrigin = new Point(0, 0.5), // shrinks towards the start side (mirrored in RTL)
        };
        var layers = new Grid();
        layers.Children.Add(grid);
        layers.Children.Add(line);
        _card = new Border
        {
            Child = layers,
            CornerRadius = new CornerRadius(14, 14, 0, 0), // it sits on the taskbar: only the top corners are round
            BorderThickness = new Thickness(1, 1, 1, 0),
            BorderBrush = new LinearGradientBrush(Color.FromArgb(0xC0, tier.R, tier.G, tier.B), Color.FromArgb(0x30, tier.R, tier.G, tier.B), 90),
            Background = new LinearGradientBrush(Color.FromRgb(0x24, 0x22, 0x5C), Color.FromRgb(0x0D, 0x0E, 0x24), 90),
            RenderTransform = _move,
        };
        Content = _card;

        _hold = new DispatcherTimerLite(TimeSpan.FromSeconds(HoldSeconds), Leave);
        MouseLeftButtonUp += (_, _) =>
        {
            (Application.Current as App)?.ShowWindow();
            Hub.I?.RequestPage("medals");
            Leave();
        };
        Loaded += (_, _) => Arrive();
        Closed += (_, _) =>
        {
            _hold.Stop();
            if (ReferenceEquals(_showing, this)) _showing = null;
            Application.Current?.Dispatcher.BeginInvoke(Pump); // the next one in line, if any
        };
    }

    private void Place()
    {
        // WorkArea ends where the taskbar begins, so the bottom of this window is the taskbar's top edge
        var area = SystemParameters.WorkArea;
        Left = area.Right - CardWidth - Gap;
        Top = area.Bottom - CardHeight;
    }

    private void Arrive()
    {
        Place();
        UiSound.Play(UiSound.Kind.Medal);
        if (!Motion.Enabled)
        {
            _move.Y = 0;
            _hold.Start();
            return;
        }
        _move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(CardHeight, 0, TimeSpan.FromMilliseconds(520))
        {
            EasingFunction = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut },
        });
        _bar.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0, TimeSpan.FromSeconds(HoldSeconds)) { BeginTime = TimeSpan.FromMilliseconds(400) });
        _hold.Start();
    }

    private void Leave()
    {
        if (_leaving) return;
        _leaving = true;
        _hold.Stop();
        if (!Motion.Enabled) { Close(); return; }
        var down = new DoubleAnimation(_move.Y, CardHeight, TimeSpan.FromMilliseconds(420)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        down.Completed += (_, _) => Close();
        _move.BeginAnimation(TranslateTransform.YProperty, down);
        _card.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300)) { BeginTime = TimeSpan.FromMilliseconds(140) });
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW: never steals focus from what the person is doing, and no Alt+Tab entry
        var style = GetWindowLongPtr(handle, -20).ToInt64() | 0x08000000 | 0x80;
        SetWindowLongPtr(handle, -20, new IntPtr(style));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    /// <summary>A one-shot timer on the UI thread.</summary>
    private sealed class DispatcherTimerLite(TimeSpan after, Action then)
    {
        private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = after };
        private bool _wired;
        public void Start()
        {
            if (!_wired) { _wired = true; _timer.Tick += (_, _) => { _timer.Stop(); then(); }; }
            _timer.Start();
        }
        public void Stop() => _timer.Stop();
    }
}
