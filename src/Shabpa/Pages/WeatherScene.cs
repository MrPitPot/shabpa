using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Shabpa.Core;

namespace Shabpa.Pages;

/// <summary>The animated sky behind the current weather: soft layered clouds, a sun with a corona and turning rays,
/// a shaded moon, twinkling stars with the odd shooting star, two layers of rain with splashes, snow with depth,
/// drifting fog and real lightning bolts. Everything is drawn from gradients (no image files) and only animates
/// while the page is visible.</summary>
public sealed class WeatherScene : Grid
{
    private enum Kind { Cloud, Drop, Flake, Star, Glow, Rays, Fog, Splash, Shooting }

    private sealed class Particle
    {
        public required UIElement Element;
        public Kind Kind;
        public double X, Y, SpeedX, SpeedY, Phase, Size, Life, Depth;
        public readonly TranslateTransform Move = new();
    }

    private const double Radius = 17; // matches the rounded card around the scene

    private readonly Canvas _canvas = new() { IsHitTestVisible = false };
    private readonly Canvas _bolt = new() { IsHitTestVisible = false, Opacity = 0 };
    private readonly Border _flash = new() { Background = Brushes.White, Opacity = 0, IsHitTestVisible = false };
    private readonly Rectangle _scrim = new() { IsHitTestVisible = false };
    private readonly List<Particle> _particles = [];
    private readonly List<Particle> _splashes = [];
    private Particle? _shooting;
    private readonly Random _dice = new();
    private Sky _sky = Sky.Clear;
    private bool _day = true;
    private bool _running;
    private TimeSpan _last;
    private double _flashLeft;
    private double _nextFlash = 3;
    private double _nextShooting = 5;
    private double _time;

    public WeatherScene()
    {
        FlowDirection = FlowDirection.LeftToRight; // physical coordinates for the animation, whatever the UI language
        Children.Add(_canvas);
        Children.Add(_bolt);
        Children.Add(_flash);
        Children.Add(_scrim);
        SizeChanged += (_, _) =>
        {
            Clip = new RectangleGeometry(new Rect(RenderSize), Radius, Radius);
            Rebuild();
        };
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

    /// <summary>The sky colours: top and bottom of the gradient per condition and time of day.</summary>
    internal static (Color top, Color bottom) Colours(Sky sky, bool day) => (sky, day) switch
    {
        (Sky.Clear, true) => (Color.FromRgb(0x17, 0x5C, 0xC6), Color.FromRgb(0x8C, 0xCF, 0xF7)),
        (Sky.Clear, false) => (Color.FromRgb(0x03, 0x07, 0x1C), Color.FromRgb(0x1D, 0x2B, 0x5E)),
        (Sky.PartlyCloudy, true) => (Color.FromRgb(0x2F, 0x6E, 0xBE), Color.FromRgb(0xA3, 0xCC, 0xEA)),
        (Sky.PartlyCloudy, false) => (Color.FromRgb(0x08, 0x0F, 0x28), Color.FromRgb(0x2A, 0x37, 0x63)),
        (Sky.Cloudy, true) => (Color.FromRgb(0x4E, 0x5E, 0x72), Color.FromRgb(0xA9, 0xB6, 0xC3)),
        (Sky.Cloudy, false) => (Color.FromRgb(0x11, 0x16, 0x22), Color.FromRgb(0x34, 0x3E, 0x50)),
        (Sky.Fog, true) => (Color.FromRgb(0x74, 0x80, 0x8B), Color.FromRgb(0xC9, 0xCF, 0xD3)),
        (Sky.Fog, false) => (Color.FromRgb(0x20, 0x26, 0x2F), Color.FromRgb(0x4A, 0x53, 0x5E)),
        (Sky.Drizzle or Sky.Rain or Sky.Showers, true) => (Color.FromRgb(0x27, 0x36, 0x4C), Color.FromRgb(0x68, 0x80, 0x9A)),
        (Sky.Drizzle or Sky.Rain or Sky.Showers, false) => (Color.FromRgb(0x07, 0x0D, 0x18), Color.FromRgb(0x22, 0x30, 0x46)),
        (Sky.Snow, true) => (Color.FromRgb(0x68, 0x80, 0x9E), Color.FromRgb(0xE2, 0xEA, 0xF4)),
        (Sky.Snow, false) => (Color.FromRgb(0x18, 0x22, 0x36), Color.FromRgb(0x58, 0x69, 0x85)),
        (Sky.Thunder, _) => (Color.FromRgb(0x0C, 0x0E, 0x22), Color.FromRgb(0x38, 0x36, 0x5A)),
        _ => (Color.FromRgb(0x17, 0x5C, 0xC6), Color.FromRgb(0x8C, 0xCF, 0xF7)),
    };

    private static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private static Color Alpha(Color c, double a) => Color.FromArgb((byte)Math.Clamp(a * 255, 0, 255), c.R, c.G, c.B);

    private static Brush Gradient(Sky sky, bool day)
    {
        var (top, bottom) = Colours(sky, day);
        var brush = new LinearGradientBrush(
        [
            new GradientStop(top, 0),
            new GradientStop(Mix(top, bottom, 0.45), 0.5),
            new GradientStop(bottom, 1),
        ], 90);
        brush.Freeze();
        return brush;
    }

    private bool Wet => _sky is Sky.Drizzle or Sky.Rain or Sky.Showers or Sky.Thunder;

    private void Toggle()
    {
        if (IsVisible && IsLoaded && Motion.Enabled) Start();
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

    private double Rand(double min, double max) => min + _dice.NextDouble() * (max - min);

    // ---- building the scene ---------------------------------------------------------------------

    private void Rebuild()
    {
        _canvas.Children.Clear();
        _particles.Clear();
        _splashes.Clear();
        _shooting = null;
        _bolt.Children.Clear();
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 10 || h < 10) return;

        AddHorizon(w, h);
        switch (_sky)
        {
            case Sky.Clear:
                if (_day) AddSun(w, h);
                else AddNight(w, h, 90);
                break;
            case Sky.PartlyCloudy:
                if (_day) AddSun(w, h);
                else AddNight(w, h, 45);
                AddClouds(w, h, far: 2, near: 2, density: 0.8);
                break;
            case Sky.Cloudy:
                AddClouds(w, h, far: 4, near: 4, density: 1);
                break;
            case Sky.Fog:
                AddClouds(w, h, far: 2, near: 0, density: 0.6);
                AddFog(w, h);
                break;
            case Sky.Drizzle:
                AddClouds(w, h, far: 3, near: 3, density: 0.9);
                AddRain(w, h, 55, 0.7);
                break;
            case Sky.Rain:
                AddClouds(w, h, far: 3, near: 4, density: 1);
                AddRain(w, h, 110, 1);
                break;
            case Sky.Showers:
                AddClouds(w, h, far: 3, near: 4, density: 1);
                AddRain(w, h, 160, 1.2);
                break;
            case Sky.Snow:
                AddClouds(w, h, far: 3, near: 2, density: 0.85);
                AddSnow(w, h, 110);
                break;
            case Sky.Thunder:
                AddClouds(w, h, far: 4, near: 4, density: 1);
                AddRain(w, h, 140, 1.3);
                break;
        }
        UpdateScrim();
    }

    private void Add(Particle p)
    {
        p.Element.RenderTransform = p.Move;
        p.Move.X = p.X;
        p.Move.Y = p.Y;
        _canvas.Children.Add(p.Element);
        _particles.Add(p);
    }

    /// <summary>The text sits on one side of the scene; a soft shade there keeps it readable over any sky.</summary>
    private void UpdateScrim()
    {
        var dark = Color.FromArgb(_day && !Wet ? (byte)70 : (byte)95, 0, 10, 30);
        var clear = Alpha(dark, 0);
        // Persian text is on the right, English on the left.
        var brush = new LinearGradientBrush(L.En ? dark : clear, L.En ? clear : dark, 0);
        brush.GradientStops[0].Offset = L.En ? 0 : 0.35;
        brush.GradientStops[1].Offset = L.En ? 0.65 : 1;
        brush.Freeze();
        _scrim.Fill = brush;
    }

    /// <summary>A wide soft light near the horizon: warm in clear daylight, cool at night, grey under clouds.</summary>
    private void AddHorizon(double w, double h)
    {
        var tint = (_sky, _day) switch
        {
            (Sky.Clear or Sky.PartlyCloudy, true) => Color.FromArgb(110, 255, 246, 220),
            (Sky.Clear or Sky.PartlyCloudy, false) => Color.FromArgb(70, 90, 120, 220),
            (Sky.Snow, _) => Color.FromArgb(90, 240, 246, 255),
            (Sky.Thunder, _) => Color.FromArgb(60, 140, 120, 220),
            _ => Color.FromArgb(55, 200, 210, 225),
        };
        var glow = new Ellipse
        {
            Width = w * 1.6, Height = h * 0.9,
            Fill = Frozen(new RadialGradientBrush(tint, Alpha(tint, 0))),
        };
        _canvas.Children.Add(glow);
        Canvas.SetLeft(glow, -w * 0.3);
        Canvas.SetTop(glow, h * 0.6);
    }

    private static T Frozen<T>(T brush) where T : Freezable
    {
        brush.Freeze();
        return brush;
    }

    private double SunX(double w, double size) => L.En ? w - size * 1.05 : size * 0.05;

    private void AddSun(double w, double h)
    {
        var size = Math.Min(h * 0.62, 170);
        var x = SunX(w, size);
        var y = h * 0.06;
        var centre = new Point(x + size / 2, y + size / 2);

        // corona: a very wide, faint glow, then a brighter close one
        foreach (var (scale, alpha, phase) in new[] { (4.2, 0.28, 0.0), (2.2, 0.55, 1.7) })
        {
            var d = size * scale;
            var corona = new Ellipse
            {
                Width = d, Height = d,
                Fill = Frozen(new RadialGradientBrush(
                [
                    new GradientStop(Color.FromArgb((byte)(255 * alpha), 255, 240, 190), 0),
                    new GradientStop(Color.FromArgb((byte)(255 * alpha * 0.35), 255, 226, 150), 0.45),
                    new GradientStop(Color.FromArgb(0, 255, 220, 140), 1),
                ])),
            };
            Add(new Particle { Element = corona, Kind = Kind.Glow, X = centre.X - d / 2, Y = centre.Y - d / 2, Phase = phase });
        }

        // rays: thin wedges that fade outwards and turn very slowly
        var raySize = size * 2.7;
        var group = new DrawingGroup();
        var rayBrush = Frozen(new RadialGradientBrush(
        [
            new GradientStop(Color.FromArgb(80, 255, 248, 210), 0.2),
            new GradientStop(Color.FromArgb(0, 255, 248, 210), 1),
        ]) { MappingMode = BrushMappingMode.Absolute, Center = new Point(0, 0), GradientOrigin = new Point(0, 0), RadiusX = raySize / 2, RadiusY = raySize / 2 });
        for (var i = 0; i < 14; i++)
        {
            var angle = i * Math.PI * 2 / 14;
            var spread = i % 2 == 0 ? 0.07 : 0.045;
            var len = raySize / 2 * (i % 2 == 0 ? 1 : 0.75);
            var figure = new PathFigure(new Point(0, 0),
            [
                new LineSegment(new Point(Math.Cos(angle - spread) * len, Math.Sin(angle - spread) * len), false),
                new LineSegment(new Point(Math.Cos(angle + spread) * len, Math.Sin(angle + spread) * len), false),
            ], true);
            group.Children.Add(new GeometryDrawing(rayBrush, null, new PathGeometry([figure])));
        }
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(-raySize / 2, -raySize / 2, raySize, raySize))));
        group.Freeze();
        var rays = new Image { Source = Frozen(new DrawingImage(group)), Width = raySize, Height = raySize, Opacity = 0.35 };
        var spin = new RotateTransform(0, raySize / 2, raySize / 2);
        var rayParticle = new Particle { Element = rays, Kind = Kind.Rays, X = centre.X - raySize / 2, Y = centre.Y - raySize / 2 };
        Add(rayParticle);
        rays.RenderTransform = new TransformGroup { Children = [spin, rayParticle.Move] };

        // the disc itself, with a soft edge instead of a hard circle
        var core = new Ellipse
        {
            Width = size * 0.62, Height = size * 0.62,
            Fill = Frozen(new RadialGradientBrush(
            [
                new GradientStop(Color.FromRgb(255, 255, 246), 0),
                new GradientStop(Color.FromRgb(255, 240, 170), 0.55),
                new GradientStop(Color.FromRgb(255, 214, 92), 0.86),
                new GradientStop(Color.FromArgb(0, 255, 205, 80), 1),
            ]) { GradientOrigin = new Point(0.42, 0.38) }),
        };
        Add(new Particle { Element = core, Kind = Kind.Glow, X = centre.X - size * 0.31, Y = centre.Y - size * 0.31, Phase = 3.1, Size = -1 });
    }

    private void AddNight(double w, double h, int stars)
    {
        // a faint band of the galaxy across the sky
        var band = new Ellipse
        {
            Width = w * 1.3, Height = h * 0.5,
            Fill = Frozen(new RadialGradientBrush(Color.FromArgb(34, 170, 180, 255), Color.FromArgb(0, 170, 180, 255))),
            RenderTransform = new RotateTransform(-14),
        };
        _canvas.Children.Add(band);
        Canvas.SetLeft(band, -w * 0.15);
        Canvas.SetTop(band, h * 0.05);

        for (var i = 0; i < stars; i++)
        {
            var big = i % 12 == 0;
            var d = big ? Rand(3.5, 5) : Rand(1.2, 2.8);
            var tint = i % 5 == 0 ? Color.FromRgb(255, 236, 210) : i % 7 == 0 ? Color.FromRgb(200, 220, 255) : Colors.White;
            UIElement star = new Ellipse { Width = d, Height = d, Fill = Frozen(new RadialGradientBrush(tint, Alpha(tint, 0)) { GradientStops = { new GradientStop(tint, 0.35) } }) };
            if (big)
            {
                // a few bright stars get a small cross sparkle
                var sparkle = new Canvas { Width = d * 4, Height = d * 4 };
                var stroke = Frozen(new LinearGradientBrush(Alpha(tint, 0), tint, 0) { GradientStops = { new GradientStop(Alpha(tint, 0), 1) } });
                var vstroke = Frozen(new LinearGradientBrush(Alpha(tint, 0), tint, 90) { GradientStops = { new GradientStop(Alpha(tint, 0), 1) } });
                sparkle.Children.Add(new Rectangle { Width = d * 4, Height = 1, Fill = stroke, Margin = new Thickness(0, d * 2 - 0.5, 0, 0) });
                sparkle.Children.Add(new Rectangle { Width = 1, Height = d * 4, Fill = vstroke, Margin = new Thickness(d * 2 - 0.5, 0, 0, 0) });
                sparkle.Children.Add(new Ellipse { Width = d, Height = d, Fill = Frozen(new RadialGradientBrush(Colors.White, Alpha(tint, 0))), Margin = new Thickness(d * 1.5, d * 1.5, 0, 0) });
                star = sparkle;
                d *= 4;
            }
            Add(new Particle { Element = star, Kind = Kind.Star, X = Rand(0, w) - d / 2, Y = Rand(0, h * 0.82) - d / 2, Phase = Rand(0, 6.28), SpeedX = Rand(0.7, 2.2) });
        }

        // the moon: a lit disc shaded towards one side, a few soft maria, and a halo
        var size = Math.Min(h * 0.34, 84);
        var mx = L.En ? w - size * 1.9 : size * 0.9;
        var my = h * 0.13;
        var halo = new Ellipse
        {
            Width = size * 3.4, Height = size * 3.4,
            Fill = Frozen(new RadialGradientBrush(Color.FromArgb(70, 210, 222, 255), Color.FromArgb(0, 210, 222, 255))),
        };
        Add(new Particle { Element = halo, Kind = Kind.Glow, X = mx - size * 1.2, Y = my - size * 1.2, Phase = 0.6 });
        var moon = new Canvas { Width = size, Height = size };
        moon.Children.Add(new Ellipse
        {
            Width = size, Height = size,
            Fill = Frozen(new RadialGradientBrush(
            [
                new GradientStop(Color.FromRgb(255, 255, 246), 0),
                new GradientStop(Color.FromRgb(236, 236, 222), 0.7),
                new GradientStop(Color.FromRgb(196, 200, 196), 1),
            ]) { GradientOrigin = new Point(0.35, 0.35), Center = new Point(0.42, 0.42) }),
        });
        foreach (var (cx, cy, cd) in new[] { (0.52, 0.30, 0.22), (0.28, 0.55, 0.16), (0.60, 0.62, 0.12), (0.40, 0.34, 0.08) })
            moon.Children.Add(new Ellipse
            {
                Width = size * cd, Height = size * cd, Margin = new Thickness(size * cx, size * cy, 0, 0),
                Fill = Frozen(new RadialGradientBrush(Color.FromArgb(46, 120, 126, 130), Color.FromArgb(0, 120, 126, 130))),
            });
        _canvas.Children.Add(moon);
        Canvas.SetLeft(moon, mx);
        Canvas.SetTop(moon, my);
    }

    /// <summary>One cloud as a single frozen picture: many soft puffs of the lit colour melt into one body (no puff has
    /// an edge of its own), then a shade is laid over the lower half so it looks round and lit from above.</summary>
    private ImageSource CloudImage(double width, Color light, Color shade, double alpha)
    {
        var height = width * 0.55;
        var body = new DrawingGroup();
        var puffs = (int)Rand(7, 11);
        for (var i = 0; i < puffs; i++)
        {
            var t = (i + 0.5) / puffs;
            var bulge = 1 - Math.Pow(Math.Abs(t - 0.5) * 2, 1.6) * 0.7; // tallest in the middle
            var r = width * Rand(0.12, 0.17) * bulge;
            var cx = width * (0.14 + t * 0.72) + Rand(-width * 0.025, width * 0.025);
            var cy = height * 0.66 - r * Rand(0.5, 1.0) * bulge;
            var brush = new RadialGradientBrush(
            [
                new GradientStop(Alpha(light, alpha), 0),
                new GradientStop(Alpha(light, alpha * 0.92), 0.55),
                new GradientStop(Alpha(light, 0), 1),
            ]);
            body.Children.Add(new GeometryDrawing(brush, null, new EllipseGeometry(new Point(cx, cy), r * 1.5, r * 1.4)));
        }
        // the flat base the puffs sit on
        body.Children.Add(new GeometryDrawing(new RadialGradientBrush(
        [
            new GradientStop(Alpha(light, alpha), 0),
            new GradientStop(Alpha(light, alpha * 0.85), 0.5),
            new GradientStop(Alpha(light, 0), 1),
        ]), null, new EllipseGeometry(new Point(width / 2, height * 0.68), width * 0.46, height * 0.2)));

        // shading: darker towards the bottom, only where the cloud is (the body itself is the mask)
        var shading = new DrawingGroup
        {
            OpacityMask = new DrawingBrush(body.Clone()) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, width, height), ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, width, height) },
        };
        shading.Children.Add(new GeometryDrawing(new LinearGradientBrush(
        [
            new GradientStop(Alpha(shade, 0), 0.35),
            new GradientStop(Alpha(shade, 0.55), 0.85),
            new GradientStop(Alpha(shade, 0.7), 1),
        ], 90), null, new RectangleGeometry(new Rect(0, 0, width, height))));

        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, width, height))));
        group.Children.Add(body);
        group.Children.Add(shading);
        group.Freeze();
        return Frozen(new DrawingImage(group));
    }

    private void AddClouds(double w, double h, int far, int near, double density)
    {
        Color light, shade;
        if (Wet) (light, shade) = _day ? (Color.FromRgb(150, 160, 176), Color.FromRgb(62, 72, 90)) : (Color.FromRgb(84, 94, 114), Color.FromRgb(24, 30, 44));
        else if (_sky == Sky.Snow) (light, shade) = _day ? (Color.FromRgb(250, 252, 255), Color.FromRgb(170, 186, 206)) : (Color.FromRgb(150, 162, 186), Color.FromRgb(56, 66, 90));
        else (light, shade) = _day ? (Color.FromRgb(255, 255, 255), Color.FromRgb(176, 192, 214)) : (Color.FromRgb(120, 132, 160), Color.FromRgb(38, 46, 70));

        void Layer(int count, double depth)
        {
            for (var i = 0; i < count; i++)
            {
                var width = Rand(170, 260) * (0.6 + depth * 0.7);
                var image = new Image
                {
                    Source = CloudImage(width, light, shade, (0.55 + depth * 0.4) * density * (_day ? 1 : 0.8)),
                    Width = width, Height = width * 0.55,
                    CacheMode = new BitmapCache(), // moving a cached picture is cheap
                };
                // spread across the width so they do not bunch up, then drift with parallax: near ones are faster
                var slot = w / Math.Max(1, count);
                Add(new Particle
                {
                    Element = image, Kind = Kind.Cloud, Depth = depth, Size = width,
                    X = slot * i + Rand(-slot * 0.3, slot * 0.3) - width * 0.3,
                    Y = depth < 0.5 ? Rand(-h * 0.08, h * 0.22) : Rand(h * 0.02, h * 0.38),
                    SpeedX = Rand(5, 9) * (0.5 + depth * 1.6),
                });
            }
        }
        Layer(far, 0.2);
        Layer(near, 0.85);
    }

    private void AddRain(double w, double h, int count, double strength)
    {
        var tint = _day ? Color.FromRgb(214, 230, 255) : Color.FromRgb(170, 196, 236);
        var near = Frozen(new LinearGradientBrush(Alpha(tint, 0), Alpha(tint, 0.85), 90));
        var far = Frozen(new LinearGradientBrush(Alpha(tint, 0), Alpha(tint, 0.4), 90));
        for (var i = 0; i < count; i++)
        {
            var depth = i % 3 == 0 ? 1.0 : 0.45; // a third of the drops are close and bigger
            var len = Rand(12, 22) * strength * (0.6 + depth * 0.5);
            var drop = new Line
            {
                X1 = 0, Y1 = 0, X2 = -len * 0.18, Y2 = len, Stroke = depth > 0.9 ? near : far,
                StrokeThickness = depth > 0.9 ? Rand(1.3, 1.9) : Rand(0.7, 1.1), StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            };
            Add(new Particle
            {
                Element = drop, Kind = Kind.Drop, Depth = depth, X = Rand(0, w + 60), Y = Rand(-h, h),
                SpeedX = -Rand(50, 80) * strength * depth, SpeedY = Rand(560, 820) * strength * (0.55 + depth * 0.45),
            });
        }
        // a small pool of splash rings, reused
        var ring = Frozen(new SolidColorBrush(Alpha(tint, 0.75)));
        for (var i = 0; i < 18; i++)
        {
            var splash = new Ellipse { Width = 14, Height = 4, Stroke = ring, StrokeThickness = 1, Opacity = 0, RenderTransformOrigin = new Point(0.5, 0.5) };
            var p = new Particle { Element = splash, Kind = Kind.Splash, X = -50, Y = -50 };
            Add(p);
            splash.RenderTransform = new TransformGroup { Children = [new ScaleTransform(0, 0), p.Move] };
            _splashes.Add(p);
        }
    }

    private void AddSnow(double w, double h, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var depth = Rand(0.2, 1);
            var d = 2 + depth * 6;
            var flake = new Ellipse
            {
                Width = d, Height = d,
                Fill = Frozen(new RadialGradientBrush(
                [
                    new GradientStop(Color.FromArgb((byte)(200 + 55 * depth), 255, 255, 255), 0),
                    new GradientStop(Color.FromArgb((byte)(150 * depth + 60), 255, 255, 255), depth > 0.75 ? 0.35 : 0.55),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 1),
                ])),
            };
            Add(new Particle
            {
                Element = flake, Kind = Kind.Flake, Depth = depth, X = Rand(0, w), Y = Rand(-h, h),
                SpeedX = Rand(-6, 6), SpeedY = 22 + depth * 52, Phase = Rand(0, 6.28), Size = d,
            });
        }
    }

    private void AddFog(double w, double h)
    {
        var tint = _day ? Color.FromRgb(255, 255, 255) : Color.FromRgb(190, 200, 214);
        for (var i = 0; i < 7; i++)
        {
            var width = w * Rand(0.7, 1.2);
            var height = Rand(40, 90);
            var bank = new Ellipse
            {
                Width = width, Height = height,
                Fill = Frozen(new RadialGradientBrush(Alpha(tint, _day ? 0.55 : 0.28), Alpha(tint, 0))),
                CacheMode = new BitmapCache(),
            };
            Add(new Particle
            {
                Element = bank, Kind = Kind.Fog, Size = width,
                X = Rand(-width * 0.5, w - width * 0.3), Y = h * (0.25 + i * 0.1) - height / 2,
                SpeedX = Rand(6, 15) * (i % 2 == 0 ? 1 : -1),
            });
        }
        // a denser haze near the ground
        var ground = new Rectangle
        {
            Width = w, Height = h * 0.45,
            Fill = Frozen(new LinearGradientBrush(Alpha(tint, 0), Alpha(tint, _day ? 0.5 : 0.25), 90)),
        };
        _canvas.Children.Add(ground);
        Canvas.SetTop(ground, h * 0.55);
    }

    /// <summary>A jagged bolt from the clouds down, with a branch or two, drawn once per strike.</summary>
    private void Strike(double w, double h)
    {
        _bolt.Children.Clear();
        var glow = Frozen(new SolidColorBrush(Color.FromArgb(90, 190, 200, 255)));
        var core = Frozen(new SolidColorBrush(Color.FromRgb(246, 248, 255)));
        var start = new Point(Rand(w * 0.2, w * 0.8), Rand(h * 0.08, h * 0.2));

        void Bolt(Point from, double length, double thickness, int depth)
        {
            var points = new PointCollection { from };
            var p = from;
            var steps = (int)(length / 14);
            for (var i = 0; i < steps; i++)
            {
                p = new Point(p.X + Rand(-11, 11), p.Y + Rand(9, 17));
                points.Add(p);
                if (depth < 2 && i > 2 && _dice.NextDouble() < 0.12)
                    Bolt(p, length * 0.35, thickness * 0.55, depth + 1);
            }
            _bolt.Children.Add(new Polyline { Points = points, Stroke = glow, StrokeThickness = thickness * 4, StrokeLineJoin = PenLineJoin.Round });
            _bolt.Children.Add(new Polyline { Points = points.Clone(), Stroke = core, StrokeThickness = thickness, StrokeLineJoin = PenLineJoin.Round });
        }

        Bolt(start, h * Rand(0.6, 0.85), 2.2, 0);
    }

    // ---- animation --------------------------------------------------------------------------------

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _last) return; // WPF can raise this twice for one frame
        var dt = _last == TimeSpan.Zero ? 0.016 : Math.Min(0.05, (now - _last).TotalSeconds);
        _last = now;
        _time += dt;
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 10 || h < 10) return;

        foreach (var p in _particles)
        {
            switch (p.Kind)
            {
                case Kind.Drop:
                    p.X += p.SpeedX * dt;
                    p.Y += p.SpeedY * dt;
                    if (p.Y > h - 6)
                    {
                        if (p.Depth > 0.9) Splash(p.X, h - Rand(2, 10));
                        p.Y = Rand(-60, -20);
                        p.X = Rand(0, w + 80);
                    }
                    p.Move.X = p.X;
                    p.Move.Y = p.Y;
                    break;
                case Kind.Flake:
                    p.Y += p.SpeedY * dt;
                    p.X += (p.SpeedX + Math.Sin(_time * (0.8 + p.Depth) + p.Phase) * 16 * p.Depth) * dt;
                    if (p.Y > h + 10) { p.Y = -10; p.X = Rand(-10, w + 10); }
                    if (p.X < -12) p.X = w + 10;
                    if (p.X > w + 12) p.X = -10;
                    p.Move.X = p.X;
                    p.Move.Y = p.Y;
                    break;
                case Kind.Star:
                    p.Element.Opacity = 0.3 + 0.7 * (0.5 + 0.5 * Math.Sin(_time * p.SpeedX + p.Phase));
                    break;
                case Kind.Glow:
                    p.Element.Opacity = p.Size < 0 ? 1 : 0.85 + 0.15 * Math.Sin(_time * 0.8 + p.Phase);
                    break;
                case Kind.Rays:
                    if (p.Element.RenderTransform is TransformGroup { Children: [RotateTransform spin, ..] }) spin.Angle = _time * 2.4 % 360;
                    p.Element.Opacity = 0.3 + 0.08 * Math.Sin(_time * 0.6);
                    break;
                case Kind.Cloud:
                case Kind.Fog:
                    p.X += p.SpeedX * dt;
                    if (p.SpeedX > 0 && p.X > w + 10) p.X = -p.Size - Rand(0, 60);
                    if (p.SpeedX < 0 && p.X < -p.Size - 10) p.X = w + Rand(0, 60);
                    p.Move.X = p.X;
                    break;
                case Kind.Splash:
                    if (p.Life <= 0) break;
                    p.Life -= dt;
                    var t = 1 - Math.Max(0, p.Life) / 0.38;
                    if (p.Element.RenderTransform is TransformGroup { Children: [ScaleTransform grow, ..] })
                    {
                        grow.ScaleX = 0.3 + t * 1.1;
                        grow.ScaleY = 0.3 + t * 1.1;
                    }
                    p.Element.Opacity = 0.7 * (1 - t);
                    break;
            }
        }

        if (!_day && _sky is Sky.Clear or Sky.PartlyCloudy) RunShootingStar(w, h, dt);

        if (_sky == Sky.Thunder)
        {
            _nextFlash -= dt;
            if (_nextFlash <= 0)
            {
                _flashLeft = 0.42;
                _nextFlash = Rand(3.5, 9);
                Strike(w, h);
            }
            if (_flashLeft > 0)
            {
                _flashLeft -= dt;
                // two quick pulses, like real lightning, with the bolt visible through both
                var phase = 0.42 - _flashLeft;
                var on = phase is < 0.07 or (> 0.15 and < 0.23);
                _flash.Opacity = on ? 0.42 : 0.05;
                _bolt.Opacity = on ? 1 : phase < 0.3 ? 0.35 : 0;
            }
            else
            {
                _flash.Opacity = 0;
                _bolt.Opacity = 0;
            }
        }
        else
        {
            _flash.Opacity = 0;
            _bolt.Opacity = 0;
        }
    }

    private void Splash(double x, double y)
    {
        foreach (var s in _splashes)
        {
            if (s.Life > 0) continue;
            s.Life = 0.38;
            s.X = x - 7;
            s.Y = y - 2;
            s.Move.X = s.X;
            s.Move.Y = s.Y;
            return;
        }
    }

    private void RunShootingStar(double w, double h, double dt)
    {
        if (_shooting is null)
        {
            _nextShooting -= dt;
            if (_nextShooting > 0) return;
            _nextShooting = Rand(6, 14);
            var length = Rand(70, 120);
            var line = new Line
            {
                X1 = 0, Y1 = 0, X2 = length, Y2 = length * 0.35, StrokeThickness = 1.6, StrokeEndLineCap = PenLineCap.Round,
                Stroke = Frozen(new LinearGradientBrush(Color.FromArgb(0, 255, 255, 255), Colors.White, new Point(0, 0), new Point(1, 0.35))),
            };
            _shooting = new Particle { Element = line, Kind = Kind.Shooting, X = Rand(w * 0.1, w * 0.6), Y = Rand(0, h * 0.3), SpeedX = 520, SpeedY = 180, Life = 0.7 };
            Add(_shooting);
            return;
        }
        var s = _shooting;
        s.Life -= dt;
        s.X += s.SpeedX * dt;
        s.Y += s.SpeedY * dt;
        s.Move.X = s.X;
        s.Move.Y = s.Y;
        s.Element.Opacity = Math.Clamp(s.Life / 0.35, 0, 1);
        if (s.Life > 0) return;
        _canvas.Children.Remove(s.Element);
        _particles.Remove(s);
        _shooting = null;
    }
}
