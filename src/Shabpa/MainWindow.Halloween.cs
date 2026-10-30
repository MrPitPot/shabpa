using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa;

/// <summary>The Halloween jump scare and greeting: a drawn pumpkin leaps out of the dark with one white flash and a short scream, then a
/// glass card says Happy Halloween. It is built when needed and removed afterwards; with animations off the scare (and its sound) is
/// skipped and only the card shows, and it is never shown over a game.</summary>
public partial class MainWindow
{
    private Grid? _halloween;
    private DispatcherTimer? _halloweenTimer;
    private KeyEventHandler? _halloweenKeys;

    /// <summary>Test aid: the scare or the greeting is on screen.</summary>
    public bool HalloweenOpen => _halloween is not null;

    /// <summary>Removes the scare or greeting (and everything it hooked up) if it is open.</summary>
    public void CloseHalloween()
    {
        _halloweenTimer?.Stop();
        _halloweenTimer = null;
        if (_halloweenKeys is not null) { PreviewKeyDown -= _halloweenKeys; _halloweenKeys = null; }
        if (_halloween is null) return;
        Root.Children.Remove(_halloween);
        _halloween = null;
    }

    /// <summary>Test aid: how many Halloween layers are on the window (never more than one).</summary>
    public int CaptureRootOverlaysHalloween => Root.Children.OfType<Grid>().Count(g => ReferenceEquals(g, _halloween));

    /// <summary>Test aid: the greeting card (null while the scare is still showing).</summary>
    public FrameworkElement? HalloweenCard => _halloween?.Children.OfType<GlassCard>().FirstOrDefault();

    /// <summary>Shows the scare and then the card. <paramref name="scare"/> false shows the card alone (used by the settings preview of a quiet run).</summary>
    public void ShowHalloween(bool scare = true)
    {
        if (_halloween is not null) return;
        var dark = new SolidColorBrush(Color.FromRgb(0x0B, 0x04, 0x10));
        dark.Freeze();
        var host = new Grid { Background = dark, Focusable = false };
        Panel.SetZIndex(host, 950);
        _halloween = host;

        void Close()
        {
            if (!ReferenceEquals(_halloween, host)) return;
            CloseHalloween();
        }
        void OnKey(object? s, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close();
        }
        _halloweenKeys = OnKey;
        PreviewKeyDown += _halloweenKeys;
        host.MouseLeftButtonDown += (_, _) => { if (host.Children.OfType<GlassCard>().Any() && !host.Children.OfType<GlassCard>().First().IsMouseOver) Close(); };
        Root.Children.Add(host);

        if (!scare || !Motion.Enabled)
        {
            host.Background = new SolidColorBrush(Color.FromArgb(0xC8, 0x0B, 0x04, 0x10));
            ShowGreetingCard(host, Close);
            return;
        }

        // 1. the pumpkin leaps out
        var pumpkin = Pumpkin();
        var scale = new ScaleTransform(0.12, 0.12);
        var shake = new TranslateTransform();
        pumpkin.RenderTransformOrigin = new Point(0.5, 0.5);
        pumpkin.RenderTransform = new TransformGroup { Children = { scale, shake } };
        host.Children.Add(pumpkin);
        var flash = new Border { Background = Brushes.White, Opacity = 0, IsHitTestVisible = false };
        host.Children.Add(flash);

        Halloween.PlayScream();
        var leap = new DoubleAnimation(0.12, 1.0, TimeSpan.FromMilliseconds(170)) { EasingFunction = new BackEase { Amplitude = 0.55, EasingMode = EasingMode.EaseOut } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, leap);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, leap.Clone());
        // a single white flash (no flicker): up at once, down in a quarter of a second
        flash.BeginAnimation(OpacityProperty, new DoubleAnimationUsingKeyFrames
        {
            KeyFrames =
            {
                new LinearDoubleKeyFrame(0.85, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(40))),
                new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300))),
            },
        });
        // a short tremble while it screams
        shake.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-9, 9, TimeSpan.FromMilliseconds(45)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(TimeSpan.FromMilliseconds(900)) });

        // 2. after a moment it fades and the greeting appears
        _halloweenTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1250) };
        _halloweenTimer.Tick += (_, _) =>
        {
            _halloweenTimer?.Stop();
            _halloweenTimer = null;
            if (!ReferenceEquals(_halloween, host)) return;
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(320));
            fade.Completed += (_, _) =>
            {
                if (!ReferenceEquals(_halloween, host)) return;
                host.Children.Remove(pumpkin);
                host.Children.Remove(flash);
                pumpkin.BeginAnimation(OpacityProperty, null);
                host.Background = new SolidColorBrush(Color.FromArgb(0xC8, 0x0B, 0x04, 0x10));
                ShowGreetingCard(host, Close);
            };
            pumpkin.BeginAnimation(OpacityProperty, fade);
        };
        _halloweenTimer.Start();
    }

    private static void ShowGreetingCard(Grid host, Action close)
    {
        var (card, body) = K.Card();
        card.Margin = new Thickness(28);
        card.Padding = new Thickness(34, 28, 34, 26);
        card.MaxWidth = 470;
        card.HorizontalAlignment = HorizontalAlignment.Center;
        card.VerticalAlignment = VerticalAlignment.Center;
        var icon = ColorEmoji.Icon("🎃", 62);
        ((FrameworkElement)icon).HorizontalAlignment = HorizontalAlignment.Center;
        ((FrameworkElement)icon).Margin = new Thickness(0, 0, 0, 8);
        body.Children.Add(icon);
        body.Children.Add(new TextBlock { Text = Halloween.Title().Replace(" 🎃", ""), Style = K.Style("H1"), FontSize = 28, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Foreground = K.Brush("Accent") });
        var wish = new TextBlock { Style = K.Style("Body"), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 20), Foreground = K.Brush("Muted") };
        ColorEmoji.Fill(wish, Halloween.Wish(DateTime.Now));
        body.Children.Add(wish);
        var ok = new Button { Style = K.Style("BtnPrimary"), Content = L.T("ترسیدم، ممنون 😅", "You got me, thanks 😅"), HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(30, 9, 30, 9) };
        ok.Click += (_, _) => close();
        body.Children.Add(ok);
        host.Children.Add(card);
        if (Motion.Enabled) Motion.Rise(card, 0, 26, 0.7);
    }

    /// <summary>A pumpkin face drawn from shapes: glowing orange body, triangle eyes and a jagged grin on a dark background.</summary>
    private static FrameworkElement Pumpkin()
    {
        var canvas = new Canvas { Width = 400, Height = 400 };
        Shape Poly(string points, Brush fill)
        {
            var polygon = new Polygon { Fill = fill, Points = PointCollection.Parse(points) };
            return polygon;
        }
        var glow = new Ellipse { Width = 520, Height = 520, Fill = new RadialGradientBrush(Color.FromArgb(0xB0, 0xFF, 0x7A, 0x00), Color.FromArgb(0, 0xFF, 0x7A, 0x00)) };
        Canvas.SetLeft(glow, -60); Canvas.SetTop(glow, -90);
        canvas.Children.Add(glow);
        var stem = new Polygon { Fill = new SolidColorBrush(Color.FromRgb(0x4A, 0x7A, 0x1E)), Points = PointCollection.Parse("185,70 215,70 232,26 200,40 176,22") };
        canvas.Children.Add(stem);
        var body = new Ellipse
        {
            Width = 380, Height = 310,
            Fill = new RadialGradientBrush { GradientOrigin = new Point(0.4, 0.3), Center = new Point(0.5, 0.5), GradientStops = { new GradientStop(Color.FromRgb(0xFF, 0xA0, 0x2E), 0), new GradientStop(Color.FromRgb(0xF0, 0x6A, 0x00), 0.6), new GradientStop(Color.FromRgb(0xA8, 0x3A, 0x00), 1) } },
        };
        Canvas.SetLeft(body, 10); Canvas.SetTop(body, 60);
        canvas.Children.Add(body);
        var face = new SolidColorBrush(Color.FromRgb(0x1A, 0x06, 0x00));
        var fire = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x6B));
        canvas.Children.Add(Poly("90,170 160,170 125,110", face));            // left eye
        canvas.Children.Add(Poly("240,170 310,170 275,110", face));           // right eye
        canvas.Children.Add(Poly("185,215 215,215 200,190", face));            // nose
        canvas.Children.Add(Poly("70,250 105,285 135,255 165,300 200,262 235,300 265,255 295,285 330,250 305,335 270,310 235,345 200,315 165,345 130,310 95,335", face)); // grin
        canvas.Children.Add(Poly("105,163 145,163 125,128", fire));              // flames inside the eyes
        canvas.Children.Add(Poly("255,163 295,163 275,128", fire));
        return new Viewbox { Stretch = Stretch.Uniform, Margin = new Thickness(30), Child = canvas };
    }
}
