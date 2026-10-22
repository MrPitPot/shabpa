using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Shabpa.Kit;

/// <summary>A gentle confetti shower for the birthday greeting: about 56 paper pieces that are built once, recycled while
/// they fall and moved by transform and opacity only. One render hook drives them for about six seconds, then it is removed
/// and the layer is emptied of motion: a settled layer costs nothing. With animations off (<see cref="Motion.Enabled"/>)
/// nothing moves: <see cref="Settle"/> lays the pieces out as a still decoration instead.</summary>
public sealed class ConfettiLayer : Canvas
{
    public const int Count = 56;
    public const double Seconds = 6.0;
    private const double StopSpawning = 4.2;

    private sealed class Piece
    {
        public Border Shape = null!;
        public TranslateTransform Move = new();
        public RotateTransform Spin = new();
        public double X, Delay, Speed, Sway, SwayRate, Phase, Turn, Start;
    }

    private static readonly Color[] Palette =
    [
        Color.FromRgb(0xFF, 0x8C, 0xC6), Color.FromRgb(0xFF, 0xD3, 0x6B), Color.FromRgb(0xB8, 0x6B, 0xFF),
        Color.FromRgb(0x6B, 0xE0, 0xC4), Color.FromRgb(0xFF, 0x6F, 0x7F), Color.FromRgb(0x7B, 0xB6, 0xFF),
    ];

    private static readonly Brush[] Brushes = Palette.Select(c => { var b = new SolidColorBrush(c); b.Freeze(); return (Brush)b; }).ToArray();

    private readonly List<Piece> _pieces = [];
    private readonly Stopwatch _clock = new();
    private bool _hooked;

    public ConfettiLayer()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        var rng = new Random(2610); // fixed seed: the same shower every time (and in screenshots)
        for (var i = 0; i < Count; i++)
        {
            var round = i % 4 == 0;
            var w = round ? 7 + rng.NextDouble() * 4 : 5 + rng.NextDouble() * 5;
            var h = round ? w : 9 + rng.NextDouble() * 7;
            var p = new Piece
            {
                X = rng.NextDouble(), Delay = rng.NextDouble() * 1.4, Speed = 110 + rng.NextDouble() * 90,
                Sway = 10 + rng.NextDouble() * 26, SwayRate = 1.2 + rng.NextDouble() * 1.8, Phase = rng.NextDouble() * 6.28,
                Turn = (rng.NextDouble() - 0.5) * 540, Start = rng.NextDouble(),
            };
            p.Shape = new Border
            {
                Width = w, Height = h, CornerRadius = new CornerRadius(round ? w / 2 : 1.5), Background = Brushes[i % Brushes.Length],
                Opacity = 0, RenderTransformOrigin = new Point(0.5, 0.5), IsHitTestVisible = false,
                RenderTransform = new TransformGroup { Children = { p.Spin, p.Move } },
            };
            _pieces.Add(p);
            Children.Add(p.Shape);
        }
    }

    /// <summary>True while the render hook is attached (test aid: must be false once the shower is over).</summary>
    public bool Running => _hooked;

    /// <summary>Number of pieces currently visible (test aid).</summary>
    public int Visible => _pieces.Count(p => p.Shape.Opacity > 0.01);

    public void Start()
    {
        Stop();
        if (!Motion.Enabled) { Settle(); return; }
        _clock.Restart();
        CompositionTarget.Rendering += OnRender;
        _hooked = true;
        Paint(0);
    }

    /// <summary>Ends the shower now: the hook is removed and every piece is hidden.</summary>
    public void Stop()
    {
        if (_hooked) CompositionTarget.Rendering -= OnRender;
        _hooked = false;
        _clock.Reset();
        foreach (var p in _pieces) p.Shape.Opacity = 0;
    }

    /// <summary>The still decoration used when animations are off: the pieces rest where they would be mid-shower, no clock.</summary>
    public void Settle() => Hold(2.4);

    /// <summary>Paints the shower as it looks <paramref name="seconds"/> in, without running anything (screenshots, still mode).</summary>
    public void Hold(double seconds)
    {
        if (_hooked) { CompositionTarget.Rendering -= OnRender; _hooked = false; }
        Paint(seconds);
    }

    private void OnRender(object? sender, EventArgs e)
    {
        var t = _clock.Elapsed.TotalSeconds;
        if (t >= Seconds) { Stop(); return; }
        Paint(t);
    }

    private void Paint(double t)
    {
        var w = ActualWidth > 0 ? ActualWidth : 900;
        var h = ActualHeight > 0 ? ActualHeight : 600;
        var lap = h + 60;
        var fade = t > Seconds - 1.2 ? Math.Max(0, (Seconds - t) / 1.2) : 1;
        foreach (var p in _pieces)
        {
            var local = t - p.Delay;
            if (local < 0) { p.Shape.Opacity = 0; continue; }
            // starts spread over the upper part of the window, falls, and re-enters at the top (recycled) until the spawning stops
            var travel = local * p.Speed + p.Start * h * 0.5;
            var round = Math.Floor(travel / lap);
            if (round >= 1 && t > StopSpawning) { p.Shape.Opacity = 0; continue; }
            var y = travel - round * lap - 30;
            var appear = Math.Min(1, local / 0.35);
            p.Shape.Opacity = appear * fade * 0.95;
            p.Move.X = p.X * w + Math.Sin(local * p.SwayRate + p.Phase) * p.Sway;
            p.Move.Y = y;
            p.Spin.Angle = local * p.Turn;
        }
    }
}

/// <summary>A static string of small paper pieces along the top edge of the window for the birthday look: drawn once, never
/// animated, no hit-testing.</summary>
public sealed class Bunting : Canvas
{
    public Bunting()
    {
        IsHitTestVisible = false;
        Height = 40;
        var rng = new Random(1405);
        for (var i = 0; i < 26; i++)
        {
            var round = i % 3 == 0;
            var size = round ? 5 + rng.NextDouble() * 3 : 4 + rng.NextDouble() * 3;
            var piece = new Border
            {
                Width = size, Height = round ? size : size * 2, CornerRadius = new CornerRadius(round ? size : 1.2),
                Background = Brush(i), Opacity = 0.55 + rng.NextDouble() * 0.35, IsHitTestVisible = false,
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(rng.NextDouble() * 180),
            };
            _fractions.Add(Math.Min(0.99, (i + rng.NextDouble() * 0.8) / 26.0));
            SetTop(piece, 3 + rng.NextDouble() * 30);
            Children.Add(piece);
        }
        ClipToBounds = true;
        SizeChanged += (_, _) => { for (var i = 0; i < Children.Count; i++) SetLeft(Children[i], _fractions[i] * ActualWidth); };
    }

    private readonly List<double> _fractions = [];

    private static Brush Brush(int i)
    {
        Color[] colours = [Color.FromRgb(0xFF, 0x8C, 0xC6), Color.FromRgb(0xFF, 0xD3, 0x6B), Color.FromRgb(0xB8, 0x6B, 0xFF), Color.FromRgb(0x6B, 0xE0, 0xC4)];
        var b = new SolidColorBrush(colours[i % colours.Length]);
        b.Freeze();
        return b;
    }
}
