using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Shabpa.Core;

namespace Shabpa.Pages;

/// <summary>An animated backdrop for the weather page: rain streaks, snow, drifting clouds, a glowing sun,
/// twinkling stars, fog and lightning, chosen from the sky and whether it is day. It only animates while visible.</summary>
public sealed class WeatherScene : Grid
{
    private sealed class Particle
    {
        public required UIElement Element;
        public double X, Y, SpeedX, SpeedY, Phase, Size;
        public TranslateTransform Move = new();
    }

    private readonly Canvas _canvas = new() { IsHitTestVisible = false };
    private readonly Border _flash = new() { Background = Brushes.White, Opacity = 0, IsHitTestVisible = false };
    private readonly List<Particle> _particles = [];
    private readonly Random _dice = new();
    private Sky _sky = Sky.Clear;
    private bool _day = true;
    private bool _running;
    private TimeSpan _last;
    private double _flashLeft;
    private double _nextFlash = 4;
    private double _time;

    public WeatherScene()
    {
        ClipToBounds = true;
        FlowDirection = FlowDirection.LeftToRight; // physical coordinates for the animation, whatever the UI language
        Children.Add(_canvas);
        Children.Add(_flash);
        SizeChanged += (_, _) => Rebuild();
        IsVisibleChanged += (_, _) => Toggle();
        Loaded += (_, _) => Toggle();
        Unloaded += (_, _) => Stop();
        Background = Gradient(Sky.Clear, true);
    }

    public void Set(Sky sky, bool day)
    {
        _sky = sky;
        _day = day;
        Background = Gradient(sky, day);
        Rebuild();
    }

    /// <summary>The sky colours: a vertical two-colour gradient per condition and time of day.</summary>
    internal static (Color top, Color bottom) Colours(Sky sky, bool day) => (sky, day) switch
    {
        (Sky.Clear, true) => (Color.FromRgb(0x1E, 0x6F, 0xD9), Color.FromRgb(0x7F, 0xC8, 0xF8)),
        (Sky.Clear, false) => (Color.FromRgb(0x05, 0x0B, 0x24), Color.FromRgb(0x1B, 0x2A, 0x5C)),
        (Sky.PartlyCloudy, true) => (Color.FromRgb(0x3A, 0x7B, 0xC8), Color.FromRgb(0x9C, 0xC7, 0xE8)),
        (Sky.PartlyCloudy, false) => (Color.FromRgb(0x0B, 0x13, 0x2E), Color.FromRgb(0x2A, 0x38, 0x66)),
        (Sky.Cloudy, true) => (Color.FromRgb(0x5B, 0x6B, 0x7C), Color.FromRgb(0xA7, 0xB4, 0xC1)),
        (Sky.Cloudy, false) => (Color.FromRgb(0x14, 0x1A, 0x26), Color.FromRgb(0x36, 0x40, 0x52)),
        (Sky.Fog, true) => (Color.FromRgb(0x7C, 0x88, 0x92), Color.FromRgb(0xC4, 0xCB, 0xD0)),
        (Sky.Fog, false) => (Color.FromRgb(0x25, 0x2C, 0x35), Color.FromRgb(0x4A, 0x53, 0x5E)),
        (Sky.Drizzle or Sky.Rain or Sky.Showers, true) => (Color.FromRgb(0x2D, 0x3E, 0x56), Color.FromRgb(0x6C, 0x84, 0x9E)),
        (Sky.Drizzle or Sky.Rain or Sky.Showers, false) => (Color.FromRgb(0x0A, 0x11, 0x1E), Color.FromRgb(0x24, 0x33, 0x4A)),
        (Sky.Snow, true) => (Color.FromRgb(0x6F, 0x86, 0xA3), Color.FromRgb(0xDD, 0xE8, 0xF3)),
        (Sky.Snow, false) => (Color.FromRgb(0x1C, 0x26, 0x3A), Color.FromRgb(0x5A, 0x6B, 0x86)),
        (Sky.Thunder, _) => (Color.FromRgb(0x10, 0x12, 0x2A), Color.FromRgb(0x3B, 0x3A, 0x5E)),
        _ => (Color.FromRgb(0x1E, 0x6F, 0xD9), Color.FromRgb(0x7F, 0xC8, 0xF8)),
    };

    private static Brush Gradient(Sky sky, bool day)
    {
        var (top, bottom) = Colours(sky, day);
        return new LinearGradientBrush(top, bottom, 90);
    }

    private void Toggle()
    {
        if (IsVisible && IsLoaded) Start();
        else Stop();
    }

    private void Start()
    {
        if (_running) return;
        _running = true;
        _last = TimeSpan.Zero;
        CompositionTarget.Rendering += OnFrame;
    }

    private void Stop()
    {
        if (!_running) return;
        _running = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    private void Rebuild()
    {
        _canvas.Children.Clear();
        _particles.Clear();
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 10 || h < 10) return;

        switch (_sky)
        {
            case Sky.Clear:
                if (_day) AddSun(w, h);
                else AddStars(w, h, 70, moon: true);
                break;
            case Sky.PartlyCloudy:
                if (_day) AddSun(w, h);
                else AddStars(w, h, 35, moon: true);
                AddClouds(w, h, 3, 0.55);
                break;
            case Sky.Cloudy:
                AddClouds(w, h, 6, 0.75);
                break;
            case Sky.Fog:
                AddFog(w, h);
                AddClouds(w, h, 2, 0.4);
                break;
            case Sky.Drizzle:
                AddClouds(w, h, 4, 0.7);
                AddRain(w, h, 45, 0.7);
                break;
            case Sky.Rain:
                AddClouds(w, h, 5, 0.8);
                AddRain(w, h, 90, 1.0);
                break;
            case Sky.Showers:
                AddClouds(w, h, 5, 0.85);
                AddRain(w, h, 140, 1.2);
                break;
            case Sky.Snow:
                AddClouds(w, h, 3, 0.6);
                AddSnow(w, h, 80);
                break;
            case Sky.Thunder:
                AddClouds(w, h, 6, 0.9);
                AddRain(w, h, 120, 1.3);
                break;
        }
    }

    private void Add(Particle p)
    {
        if (p.SpeedY > 0)
        {
            // Falling things move by transform only, from the canvas corner.
            p.Element.RenderTransform = p.Move;
            p.Move.X = p.X;
            p.Move.Y = p.Y;
        }
        else
        {
            Canvas.SetLeft(p.Element, p.X);
            Canvas.SetTop(p.Element, p.Y);
        }
        _canvas.Children.Add(p.Element);
        _particles.Add(p);
    }

    private double Rand(double min, double max) => min + _dice.NextDouble() * (max - min);

    private void AddSun(double w, double h)
    {
        var size = Math.Min(h * 0.9, 220);
        var glow = new Ellipse
        {
            Width = size * 1.8, Height = size * 1.8,
            Fill = new RadialGradientBrush(Color.FromArgb(200, 255, 236, 150), Color.FromArgb(0, 255, 220, 120)),
        };
        var sx = L.En ? w - size * 1.5 : -size * 0.35;
        Add(new Particle { Element = glow, X = sx, Y = -size * 0.5, Size = size, Phase = 1 });
        var core = new Ellipse { Width = size * 0.55, Height = size * 0.55, Fill = new RadialGradientBrush(Color.FromRgb(255, 250, 215), Color.FromRgb(255, 214, 90)) };
        Add(new Particle { Element = core, X = sx + size * 0.625, Y = -size * 0.5 + size * 0.625, Phase = 2 });
    }

    private void AddStars(double w, double h, int count, bool moon)
    {
        for (var i = 0; i < count; i++)
        {
            var star = new Ellipse { Width = Rand(1.2, 3), Height = Rand(1.2, 3), Fill = Brushes.White, Opacity = Rand(0.3, 1) };
            Add(new Particle { Element = star, X = Rand(0, w), Y = Rand(0, h * 0.85), Phase = Rand(0, 6.28), SpeedX = Rand(0.8, 2.4) });
        }
        if (!moon) return;
        var size = Math.Min(h * 0.38, 90);
        var disc = new Ellipse { Width = size, Height = size, Fill = new RadialGradientBrush(Color.FromRgb(255, 255, 240), Color.FromRgb(225, 225, 205)) };
        Add(new Particle { Element = disc, X = L.En ? w - size * 1.6 : size * 0.6, Y = h * 0.14, Phase = 3 });
    }

    private void AddClouds(double w, double h, int count, double alpha)
    {
        var tint = _day ? Color.FromArgb((byte)(255 * alpha * 0.85), 255, 255, 255) : Color.FromArgb((byte)(255 * alpha * 0.35), 190, 200, 220);
        if (_sky is Sky.Rain or Sky.Showers or Sky.Thunder or Sky.Drizzle)
            tint = _day ? Color.FromArgb((byte)(255 * alpha * 0.55), 70, 82, 100) : Color.FromArgb((byte)(255 * alpha * 0.5), 60, 70, 90);
        for (var i = 0; i < count; i++)
        {
            var scale = Rand(0.8, 1.6);
            var cloud = Cloud(scale, tint);
            Add(new Particle
            {
                Element = cloud, X = Rand(-100, w), Y = Rand(-5, h * 0.55), SpeedX = Rand(6, 22) * (i % 2 == 0 ? 1 : 0.6), Size = 190 * scale,
            });
        }
    }

    private static UIElement Cloud(double scale, Color tint)
    {
        var brush = new SolidColorBrush(tint);
        var canvas = new Canvas { Width = 190 * scale, Height = 80 * scale };
        void Blob(double x, double y, double d) =>
            canvas.Children.Add(new Ellipse { Width = d * scale, Height = d * scale, Fill = brush, Margin = new Thickness(x * scale, y * scale, 0, 0) });
        Blob(10, 28, 52);
        Blob(40, 8, 66);
        Blob(88, 20, 56);
        Blob(118, 30, 50);
        canvas.Children.Add(new Border
        {
            Width = 150 * scale, Height = 34 * scale, Background = brush, CornerRadius = new CornerRadius(17 * scale), Margin = new Thickness(18 * scale, 44 * scale, 0, 0),
        });
        return canvas;
    }

    private void AddRain(double w, double h, int count, double strength)
    {
        var brush = new SolidColorBrush(_day ? Color.FromArgb(150, 215, 232, 255) : Color.FromArgb(130, 170, 195, 235));
        for (var i = 0; i < count; i++)
        {
            var len = Rand(9, 18) * strength;
            var drop = new Line { X1 = 0, Y1 = 0, X2 = -len * 0.25, Y2 = len, Stroke = brush, StrokeThickness = Rand(0.9, 1.6) };
            Add(new Particle { Element = drop, X = Rand(0, w + 60), Y = Rand(-h, h), SpeedX = -Rand(40, 70) * strength, SpeedY = Rand(520, 780) * strength });
        }
    }

    private void AddSnow(double w, double h, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var d = Rand(2.5, 6.5);
            var flake = new Ellipse { Width = d, Height = d, Fill = Brushes.White, Opacity = Rand(0.6, 1) };
            Add(new Particle { Element = flake, X = Rand(0, w), Y = Rand(-h, h), SpeedX = Rand(-8, 8), SpeedY = Rand(28, 70), Phase = Rand(0, 6.28), Size = d });
        }
    }

    private void AddFog(double w, double h)
    {
        for (var i = 0; i < 5; i++)
        {
            var band = new Border
            {
                Width = w * 1.4, Height = Rand(26, 50),
                Background = new LinearGradientBrush(
                [
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0),
                    new GradientStop(Color.FromArgb((byte)(_day ? 120 : 55), 255, 255, 255), 0.5),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 1),
                ], 0),
                CornerRadius = new CornerRadius(20),
            };
            Add(new Particle { Element = band, X = Rand(-w * 0.4, 0), Y = h * (0.18 + i * 0.16), SpeedX = Rand(5, 14) * (i % 2 == 0 ? 1 : -1) });
        }
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        var dt = _last == TimeSpan.Zero ? 0.016 : Math.Min(0.05, (now - _last).TotalSeconds);
        _last = now;
        _time += dt;
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 10 || h < 10) return;

        foreach (var p in _particles)
        {
            switch (p.Element)
            {
                case Line or Ellipse when p.SpeedY > 0 && p.SpeedX <= 0 || p.SpeedY > 0: // rain and snow fall
                    p.X += p.SpeedX * dt;
                    p.Y += p.SpeedY * dt;
                    if (_sky == Sky.Snow) p.X += Math.Sin(_time * 1.3 + p.Phase) * 18 * dt;
                    if (p.Y > h + 20) { p.Y = -20; p.X = Rand(0, w + 60); }
                    if (p.X < -40) p.X = w + 20;
                    if (p.X > w + 60) p.X = -20;
                    p.Move.X = p.X;
                    p.Move.Y = p.Y;
                    break;
                case Ellipse when p.SpeedX > 0 && p.Size == 0: // stars twinkle
                    p.Element.Opacity = 0.35 + 0.65 * (0.5 + 0.5 * Math.Sin(_time * p.SpeedX + p.Phase));
                    break;
                case Ellipse when p.Phase == 1: // sun glow breathes
                    p.Element.Opacity = 0.82 + 0.18 * Math.Sin(_time * 0.9);
                    break;
                default: // clouds and fog drift sideways
                    if (p.SpeedX != 0)
                    {
                        p.X += p.SpeedX * dt;
                        if (p.SpeedX > 0 && p.X > w + 20) p.X = -(p.Size > 0 ? p.Size : w * 1.4);
                        if (p.SpeedX < 0 && p.X < -(p.Size > 0 ? p.Size : w * 1.4) - 20) p.X = w;
                        Canvas.SetLeft(p.Element, p.X);
                    }
                    break;
            }
        }

        if (_sky == Sky.Thunder)
        {
            _nextFlash -= dt;
            if (_nextFlash <= 0)
            {
                _flashLeft = 0.35;
                _nextFlash = Rand(3, 9);
            }
            if (_flashLeft > 0)
            {
                _flashLeft -= dt;
                // two quick pulses, like real lightning
                var phase = 0.35 - _flashLeft;
                _flash.Opacity = phase is < 0.06 or (> 0.14 and < 0.2) ? 0.55 : 0.08;
            }
            else _flash.Opacity = 0;
        }
        else _flash.Opacity = 0;
    }
}
