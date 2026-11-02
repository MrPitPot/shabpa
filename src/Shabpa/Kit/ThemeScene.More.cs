using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Shabpa.Core;

namespace Shabpa.Kit;

// The twenty later backdrops (history, games, nature, space, festivals). Same rules as the first ten: plain shapes,
// transforms and opacity only, nothing per frame, still pictures when animations are off.
public sealed partial class ThemeScene
{
    private void More(double w, double h)
    {
        switch (_scene)
        {
            case Scene.Persepolis: Persepolis(w, h); break;
            case Scene.Cyrus: Cyrus(w, h); break;
            case Scene.Christmas: Christmas(w, h); break;
            case Scene.Tactical: Tactical(w, h); break;
            case Scene.Smoke: Smoke(w, h); break;
            case Scene.Shards: Shards(w, h); break;
            case Scene.Pixels: Pixels(w, h); break;
            case Scene.Invaders: Invaders(w, h); break;
            case Scene.Synth: Synth(w, h); break;
            case Scene.Petals: Petals(w, h); break;
            case Scene.Fireflies: Fireflies(w, h); break;
            case Scene.Deep: Deep(w, h); break;
            case Scene.Storm: Storm(w, h); break;
            case Scene.Galaxy: Galaxy(w, h); break;
            case Scene.Orbit: Orbit(w, h); break;
            case Scene.Steam: Steam(w, h); break;
            case Scene.Yalda: Yalda(w, h); break;
            case Scene.Ripples: Ripples(w, h); break;
            case Scene.Matrix: Matrix(w, h); break;
            case Scene.Shapes: Shapes(w, h); break;
        }
    }

    private static Brush Solid(string hex, byte a = 255) => Freeze(new SolidColorBrush(C(hex, a)));

    private static DoubleAnimationUsingKeyFrames Keys(double seconds, double phase, params (double at, double value)[] frames)
    {
        var a = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = D(seconds), BeginTime = TimeSpan.FromSeconds(-phase * seconds) };
        foreach (var (at, value) in frames) a.KeyFrames.Add(new LinearDoubleKeyFrame(value, KeyTime.FromPercent(at)));
        return a;
    }

    private static Polygon Poly(Brush fill, params (double x, double y)[] pts)
    {
        var p = new Polygon { Fill = fill, IsHitTestVisible = false };
        foreach (var (x, y) in pts) p.Points.Add(new Point(x, y));
        return p;
    }

    // ---- history ----------------------------------------------------------------------------------------------

    private void Persepolis(double w, double h)
    {
        var sun = Dot(Math.Min(w, h) * .7, Radial(C("FFE2A0", 150), C("E2B04A", 0)), w * .72, -h * .18, .9);
        if (Animated) Twinkle(sun, .6, 1, 7, 0);
        var stone = Grad(new Point(0, 0), new Point(1, 0), (C("5A4128", 150), 0), (C("8A6A44", 175), .45), (C("4A3420", 160), 1));
        var flute = Solid("1B140D", 90);
        var step = Math.Max(150, w / 7);
        var n = (int)(w / step) + 1;
        for (var i = 0; i < n; i++)
        {
            var x = i * step + step * .35;
            var top = h * (.34 + .05 * ((i * 5) % 3));
            var cw = 34.0;
            var shaft = new Rectangle { Width = cw, Height = h - top - h * .1, Fill = stone, IsHitTestVisible = false };
            Add(shaft, x, top + 22);
            for (var f = 1; f < 4; f++) Add(new Rectangle { Width = 1.4, Height = shaft.Height, Fill = flute, IsHitTestVisible = false }, x + f * cw / 4, top + 22);
            Add(new Rectangle { Width = cw + 18, Height = 12, Fill = stone, RadiusX = 2, RadiusY = 2, IsHitTestVisible = false }, x - 9, top + 10);
            Add(Poly(stone, (0, 12), (cw + 30, 12), (cw + 22, 0), (8, 0)), x - 15, top - 2);
            Add(new Rectangle { Width = cw + 14, Height = 10, Fill = stone, IsHitTestVisible = false }, x - 7, h - h * .1 - 4);
        }
        for (var s = 0; s < 4; s++) Add(new Rectangle { Width = w, Height = h * .035, Fill = Solid("2A1C10", (byte)(200 - s * 20)), IsHitTestVisible = false }, 0, h * (.9 + s * .028));
        for (var i = 0; i < 26; i++)
        {
            var mote = Dot(R(1.5, 3.5), Solid("FFE2A0", (byte)R(90, 200)), R(0, 1) * w, R(.2, 1) * h);
            var t = Move(mote);
            if (Animated)
            {
                Loop(t, TranslateTransform.XProperty, 0, R(120, 260), R(14, 26), R(0, 1));
                Twinkle(mote, .1, .9, R(2, 5), R(0, 1));
            }
        }
    }

    private void Cyrus(double w, double h)
    {
        var gold = Solid("F0C75E");
        var cx = w * .5;
        var cy = h * .42;
        var scale = Math.Clamp(Math.Min(w, h) / 520, .6, 1.5);
        var holder = new Canvas { IsHitTestVisible = false, Opacity = .3 };
        var breathe = new ScaleTransform(1, 1);
        holder.RenderTransform = new TransformGroup { Children = { breathe, new TranslateTransform(cx, cy) } };
        Add(holder, 0, 0);
        void Ring(double r, double th) => holder.Children.Add(new Ellipse { Width = r * 2 * scale, Height = r * 2 * scale, Stroke = gold, StrokeThickness = th, RenderTransform = new TranslateTransform(-r * scale, -r * scale) });
        Ring(34, 3); Ring(23, 1.6);
        foreach (var dir in new[] { 1, -1 })
            for (var tier = 0; tier < 4; tier++)
            {
                var len = (150 - tier * 22) * scale;
                var y0 = (-14 + tier * 9) * scale;
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(new Point(dir * 38 * scale, y0), false, false);
                    ctx.BezierTo(new Point(dir * (38 * scale + len * .5), y0 - 26 * scale), new Point(dir * (38 * scale + len * .8), y0 - 12 * scale), new Point(dir * (38 * scale + len), y0 + 6 * scale), true, false);
                    ctx.BezierTo(new Point(dir * (38 * scale + len * .75), y0 + 4 * scale), new Point(dir * (38 * scale + len * .4), y0 + 16 * scale), new Point(dir * 38 * scale, y0 + 14 * scale), true, false);
                }
                g.Freeze();
                holder.Children.Add(new Path { Data = g, Stroke = gold, StrokeThickness = 2.2 - tier * .3 });
            }
        // tail feathers below the disc
        holder.Children.Add(Poly(Solid("F0C75E", 120), (-10 * scale, 36 * scale), (10 * scale, 36 * scale), (24 * scale, 86 * scale), (0, 74 * scale), (-24 * scale, 86 * scale)));
        if (Animated) { Loop(breathe, ScaleTransform.ScaleXProperty, .96, 1.05, 6, 0, reverse: true, smooth: true); Loop(breathe, ScaleTransform.ScaleYProperty, .96, 1.05, 6, 0, reverse: true, smooth: true); }
        // rows of wedge marks (a nod to cuneiform) that glow in turn along both edges
        foreach (var side in new[] { 0.035, 0.935 })
            for (var row = 0; row < (int)(h / 34); row++)
            {
                var line = new Canvas { IsHitTestVisible = false, Opacity = .1 };
                for (var k = 0; k < 4; k++)
                {
                    var wedge = Poly(gold, (0, 0), (9, 3), (0, 6));
                    wedge.RenderTransform = new RotateTransform(((row * 7 + k * 3) % 4) * 45 - 45, 4, 3);
                    SetLeft(wedge, k * 14);
                    line.Children.Add(wedge);
                }
                Add(line, w * side, 20 + row * 34);
                if (Animated) Twinkle(line, .05, .5, 5, row / (double)(h / 34));
            }
        for (var i = 0; i < 24; i++)
        {
            var mote = Dot(R(1.4, 3), gold, R(0, 1) * w, h);
            var t = Move(mote);
            if (Animated) { Loop(t, TranslateTransform.YProperty, 0, -h * R(.5, 1), R(9, 18), R(0, 1)); mote.BeginAnimation(OpacityProperty, Keys(R(9, 18), R(0, 1), (0, 0), (.2, .8), (1, 0))); }
            else { SetTop(mote, R(.2, 1) * h); mote.Opacity = .5; }
        }
    }

    // ---- festivals --------------------------------------------------------------------------------------------

    private void Christmas(double w, double h)
    {
        // snow
        var n = (int)Math.Clamp(w / 30, 20, 50);
        for (var i = 0; i < n; i++)
        {
            var flake = Dot(R(2, 5), Solid("FFFFFF", (byte)R(150, 230)), R(0, 1) * w, -10);
            var fall = new TranslateTransform();
            var sway = new TranslateTransform();
            flake.RenderTransform = new TransformGroup { Children = { sway, fall } };
            if (Animated) { Loop(fall, TranslateTransform.YProperty, 0, h + 20, R(8, 16), R(0, 1)); Loop(sway, TranslateTransform.XProperty, -20, 20, R(3, 6), R(0, 1), reverse: true, smooth: true); }
            else SetTop(flake, R(0, 1) * h);
        }
        // pines along the bottom
        var trunk = Solid("3B2414");
        for (var i = 0; i < Math.Max(5, (int)(w / 170)); i++)
        {
            var tw = R(70, 130);
            var th = tw * R(1.5, 2.1);
            var x = (i + R(.1, .6)) * (w / Math.Max(5, (int)(w / 170)));
            var tree = new Canvas { Width = tw, Height = th, IsHitTestVisible = false, Opacity = .9 };
            for (var tier = 0; tier < 3; tier++)
            {
                var ty = th * (.05 + tier * .27);
                var half = tw * (.28 + tier * .11);
                tree.Children.Add(Poly(Solid(tier == 1 ? "14502B" : "0F4023"), (tw / 2, ty), (tw / 2 + half, ty + th * .38), (tw / 2 - half, ty + th * .38)));
                tree.Children.Add(Poly(Solid("F4F8FF", 220), (tw / 2, ty), (tw / 2 + half * .35, ty + th * .13), (tw / 2 - half * .35, ty + th * .13)));
            }
            tree.Children.Add(new Rectangle { Width = tw * .1, Height = th * .1, Fill = trunk, RenderTransform = new TranslateTransform(tw * .45, th * .9) });
            Add(tree, x, h - th * .95);
        }
        // the string of lights, glowing one after another
        string[] cols = ["FF4D4D", "FFD25E", "4DD68C", "5EA8FF"];
        var count = (int)Math.Clamp(w / 52, 10, 30);
        var wire = new StreamGeometry();
        using (var ctx = wire.Open())
        {
            ctx.BeginFigure(new Point(0, 10), false, false);
            for (var i = 1; i <= count; i++) ctx.QuadraticBezierTo(new Point(w * (i - .5) / count, 10 + 26), new Point(w * i / count, 10), true, false);
        }
        wire.Freeze();
        Add(new Path { Data = wire, Stroke = Solid("1D3A28", 220), StrokeThickness = 1.6, IsHitTestVisible = false }, 0, 0);
        for (var i = 0; i < count; i++)
        {
            var c = cols[i % 4];
            var x = w * (i + .5) / count;
            var bulb = new Grid { Width = 30, Height = 30, IsHitTestVisible = false };
            bulb.Children.Add(new Ellipse { Fill = Radial(C(c, 190), C(c, 0)) });
            bulb.Children.Add(new Ellipse { Width = 8, Height = 11, Fill = Solid(c) });
            Add(bulb, x - 15, 10 + 17);
            if (Animated) Twinkle(bulb, .25, 1, 2.6, i / (double)count); else bulb.Opacity = .8;
        }
    }

    private void Yalda(double w, double h)
    {
        var star = Solid("FFE9D0");
        for (var i = 0; i < 50; i++) Twinkle(Dot(R(1, 2.8), star, R(0, 1) * w, R(0, .7) * h, R(.2, .8)), .1, .95, R(2, 6), R(0, 1));
        for (var i = 0; i < 14; i++)
        {
            var size = R(12, 30);
            var seed = new Grid { Width = size, Height = size, IsHitTestVisible = false, Opacity = R(.45, .85) };
            seed.Children.Add(new Ellipse { Fill = Radial(C("FF8A96", 255), C("B3122F", 255)) });
            seed.Children.Add(new Ellipse { Width = size * .3, Height = size * .3, Fill = Solid("FFFFFF", 150), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(size * .2, size * .15, 0, 0) });
            var rise = new TranslateTransform();
            var sway = new TranslateTransform();
            seed.RenderTransform = new TransformGroup { Children = { sway, rise } };
            Add(seed, R(0, 1) * w, h + 20);
            if (Animated) { Loop(rise, TranslateTransform.YProperty, 0, -(h + 80), R(18, 32), R(0, 1)); Loop(sway, TranslateTransform.XProperty, -22, 22, R(4, 8), R(0, 1), reverse: true, smooth: true); }
            else SetTop(seed, R(.2, .9) * h);
        }
        for (var c = 0; c < 3; c++)
        {
            var x = w * (.2 + c * .3);
            var hgt = R(54, 90);
            Add(new Rectangle { Width = 16, Height = hgt, RadiusX = 2, RadiusY = 2, IsHitTestVisible = false, Fill = Grad(new Point(0, 0), new Point(1, 0), (C("FFE9D0", 220), 0), (C("D8B890", 220), 1)) }, x, h - hgt - 10);
            var glow = new Grid { Width = 90, Height = 90, IsHitTestVisible = false, RenderTransformOrigin = new Point(.5, .75) };
            glow.Children.Add(new Ellipse { Fill = Radial(C("FFB347", 140), C("FF4D6A", 0)) });
            glow.Children.Add(new Ellipse { Width = 9, Height = 20, Fill = Radial(C("FFF6D0", 255), C("FF8A1F", 230)), VerticalAlignment = VerticalAlignment.Center });
            var flick = new ScaleTransform(1, 1);
            glow.RenderTransform = flick;
            Add(glow, x - 37, h - hgt - 10 - 62);
            if (Animated)
            {
                Loop(flick, ScaleTransform.ScaleYProperty, .86, 1.14, R(.35, .7), R(0, 1), reverse: true, smooth: true);
                Twinkle(glow, .7, 1, R(.5, 1), R(0, 1));
            }
        }
    }

    // ---- games ------------------------------------------------------------------------------------------------

    private void Tactical(double w, double h)
    {
        var line = Solid("9DB04A", 26);
        for (var x = 0.0; x < w; x += 54) Add(new Rectangle { Width = 1, Height = h, Fill = line, IsHitTestVisible = false }, x, 0);
        for (var y = 0.0; y < h; y += 54) Add(new Rectangle { Width = w, Height = 1, Fill = line, IsHitTestVisible = false }, 0, y);
        var scan = new Rectangle { Width = w, Height = 70, IsHitTestVisible = false, Fill = Grad(new Point(0, 0), new Point(0, 1), (C("F2A33A", 0), 0), (C("F2A33A", 70), .9), (C("FFE0A0", 150), 1)) };
        var t = Move(scan);
        Add(scan, 0, -70);
        if (Animated) Loop(t, TranslateTransform.YProperty, 0, h + 70, 7, 0);
        var brackets = Solid("F2A33A", 200);
        foreach (var (cx, cy, sx, sy) in new[] { (14.0, 14.0, 1, 1), (w - 14, 14, -1, 1), (14, h - 14, 1, -1), (w - 14, h - 14, -1, -1) })
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open()) { ctx.BeginFigure(new Point(0, 34 * sy), false, false); ctx.LineTo(new Point(0, 0), true, false); ctx.LineTo(new Point(34 * sx, 0), true, false); }
            g.Freeze();
            var br = new Path { Data = g, Stroke = brackets, StrokeThickness = 2.4, IsHitTestVisible = false };
            Add(br, cx, cy);
            if (Animated) Twinkle(br, .25, 1, 2.4, 0);
        }
        var ring = new Ellipse { Width = 150, Height = 150, Stroke = Solid("F2A33A", 120), StrokeThickness = 1.4, StrokeDashArray = [3, 5], IsHitTestVisible = false, RenderTransformOrigin = new Point(.5, .5) };
        var rot = new RotateTransform();
        ring.RenderTransform = rot;
        Add(ring, w * .5 - 75, h * .5 - 75);
        foreach (var (dx, dy) in new[] { (0, -92), (0, 78), (-92, 0), (78, 0) })
            Add(new Rectangle { Width = dx == 0 ? 1.6 : 14, Height = dx == 0 ? 14 : 1.6, Fill = brackets, IsHitTestVisible = false }, w * .5 + dx - (dx == 0 ? .8 : 0), h * .5 + dy - (dx == 0 ? 0 : .8));
        if (Animated) Loop(rot, RotateTransform.AngleProperty, 0, 360, 42, 0);
        var tracer = Grad(new Point(0, .5), new Point(1, .5), (C("FFE0A0", 0), 0), (C("FFD27A", 230), 1));
        for (var i = 0; i < 4; i++)
        {
            var streak = new Rectangle { Width = 90, Height = 2, Fill = tracer, IsHitTestVisible = false, Opacity = 0 };
            var mv = Move(streak);
            Add(streak, -100, R(.1, .9) * h);
            if (!Animated) continue;
            var cycle = R(5, 11);
            var ph = R(0, 1);
            mv.BeginAnimation(TranslateTransform.XProperty, Keys(cycle, ph, (0, 0), (.06, w + 220), (1, w + 220)));
            streak.BeginAnimation(OpacityProperty, Keys(cycle, ph, (0, 0), (.01, 1), (.06, 0), (1, 0)));
        }
    }

    private void Smoke(double w, double h)
    {
        for (var i = 0; i < 4; i++)
        {
            var left = i % 2 == 0;
            var beam = Poly(Grad(new Point(.5, 1), new Point(.5, 0), (C("EAF2FF", 70), 0), (C("EAF2FF", 0), 1)), (0, 0), (-70, h * 1.1), (70, h * 1.1));
            beam.RenderTransformOrigin = new Point(0, 0);
            var rot = new RotateTransform(0);
            beam.RenderTransform = new TransformGroup { Children = { rot, new TranslateTransform() } };
            Add(beam, left ? w * (.1 + i * .06) : w * (.9 - i * .05), h * -.05);
            beam.Opacity = .55;
            if (Animated) Loop(rot, RotateTransform.AngleProperty, -22, 22, R(9, 15), R(0, 1), reverse: true, smooth: true);
            else rot.Angle = left ? -10 : 10;
        }
        for (var i = 0; i < 8; i++)
        {
            var size = R(260, 520);
            var puff = new Ellipse { Width = size, Height = size * R(.4, .6), Fill = Radial(C("A4ACB8", (byte)R(55, 95)), C("A4ACB8", 0)), IsHitTestVisible = false };
            var t = Move(puff);
            Add(puff, R(-.2, .9) * w, R(.35, 1.0) * h);
            if (Animated) { Loop(t, TranslateTransform.XProperty, -80, 220, R(34, 60), R(0, 1), reverse: true, smooth: true); Twinkle(puff, .35, 1, R(8, 14), R(0, 1)); }
        }
        for (var i = 0; i < 14; i++)
        {
            var spark = Dot(R(1.8, 3.5), Solid("FF8A3C"), R(0, 1) * w, h);
            var t = Move(spark);
            if (Animated) { Loop(t, TranslateTransform.YProperty, 0, -h * R(.4, .9), R(4, 9), R(0, 1)); spark.BeginAnimation(OpacityProperty, Keys(R(4, 9), R(0, 1), (0, 0), (.1, 1), (1, 0))); }
            else { SetTop(spark, R(.4, 1) * h); spark.Opacity = .6; }
        }
    }

    private void Shards(double w, double h)
    {
        string[][] cols = [["FF4655", "FF8A94"], ["3DE0C0", "9CF5E4"], ["FF4655", "FFFFFF"]];
        for (var i = 0; i < 12; i++)
        {
            var len = R(220, 560);
            var th = R(10, 46);
            var c = cols[i % cols.Length];
            var shard = Poly(Grad(new Point(0, 0), new Point(1, 0), (C(c[0], 0), 0), (C(c[0], (byte)R(70, 130)), .55), (C(c[1], 20), 1)), (0, 0), (len, 0), (len - th * 1.4, th), (-th * 1.4, th));
            shard.RenderTransform = new TransformGroup { Children = { new RotateTransform(-24), new TranslateTransform() } };
            Add(shard, R(-.2, .9) * w, R(.05, 1) * h);
            if (Animated)
            {
                var tt = (TranslateTransform)((TransformGroup)shard.RenderTransform).Children[1];
                Loop(tt, TranslateTransform.XProperty, -60, 160, R(7, 14), R(0, 1), reverse: true, smooth: true);
                Twinkle(shard, .25, 1, R(3, 7), R(0, 1));
            }
        }
        for (var i = 0; i < 3; i++)
        {
            var slash = new Rectangle { Width = w * 1.4, Height = 2, Fill = Grad(new Point(0, 0), new Point(1, 0), (C("FFFFFF", 0), 0), (C("FFFFFF", 200), .5), (C("FFFFFF", 0), 1)), IsHitTestVisible = false, Opacity = 0, RenderTransformOrigin = new Point(.5, .5) };
            slash.RenderTransform = new TransformGroup { Children = { new RotateTransform(-24), new TranslateTransform() } };
            Add(slash, -w * .2, R(.2, .8) * h);
            if (Animated) slash.BeginAnimation(OpacityProperty, Keys(R(6, 10), R(0, 1), (0, 0), (.02, .8), (.05, 0), (1, 0)));
        }
    }

    private void Pixels(double w, double h)
    {
        string[] cols = ["6FD14F", "5CBF3F", "4A9A33", "B07A3E", "8A5A2A", "6A4420", "7A7A7A"];
        var n = (int)Math.Clamp(w / 40, 14, 36);
        for (var i = 0; i < n; i++)
        {
            var size = (int)R(2, 5) * 6;
            var block = new Rectangle { Width = size, Height = size, Fill = Solid(cols[i % cols.Length], (byte)R(140, 235)), IsHitTestVisible = false, Stroke = Solid("000000", 60), StrokeThickness = 1 };
            var t = Move(block);
            Add(block, Math.Round(R(0, 1) * w / 18) * 18, -size);
            if (Animated)
            {
                var seconds = R(12, 24);
                var steps = 22;
                var a = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = D(seconds), BeginTime = TimeSpan.FromSeconds(-R(0, 1) * seconds) };
                for (var s = 0; s <= steps; s++) a.KeyFrames.Add(new DiscreteDoubleKeyFrame((h + size * 2) * s / steps, KeyTime.FromPercent((double)s / steps)));
                t.BeginAnimation(TranslateTransform.YProperty, a);
            }
            else SetTop(block, R(0, .9) * h);
        }
        // the ground: a row of grass over earth
        var grass = new GeometryGroup();
        var soil = new GeometryGroup();
        var cell = 20.0;
        var cols2 = (int)(w / cell) + 1;
        for (var x = 0; x < cols2; x++)
        {
            grass.Children.Add(new RectangleGeometry(new Rect(x * cell, h - cell * 3, cell - 1, cell - 1)));
            for (var y = 1; y < 3; y++) if (_rng.NextDouble() > .12) soil.Children.Add(new RectangleGeometry(new Rect(x * cell, h - cell * (3 - y), cell - 1, cell - 1)));
        }
        grass.Freeze(); soil.Freeze();
        Add(new Path { Data = soil, Fill = Solid("6A4420", 190), IsHitTestVisible = false }, 0, 0);
        Add(new Path { Data = grass, Fill = Solid("4A9A33", 220), IsHitTestVisible = false }, 0, 0);
    }

    private static readonly string[] Invader =
    [
        "..#.....#..", "...#...#...", "..#######..", ".##.###.##.", "###########", "#.#######.#", "#.#.....#.#", "...##.##...",
    ];

    private void Invaders(double w, double h)
    {
        var g = new GeometryGroup();
        for (var y = 0; y < Invader.Length; y++)
            for (var x = 0; x < Invader[y].Length; x++)
                if (Invader[y][x] == '#') g.Children.Add(new RectangleGeometry(new Rect(x * 4, y * 4, 3.4, 3.4)));
        g.Freeze();
        for (var i = 0; i < 40; i++) Twinkle(Dot(R(1, 2.2), Solid("FFFFFF"), R(0, 1) * w, R(0, 1) * h, R(.2, .7)), .1, .9, R(2, 6), R(0, 1));
        string[] cols = ["FF4FD8", "38E8FF", "B8FF5E", "FFD25E"];
        var fleet = new Canvas { IsHitTestVisible = false, Opacity = .42 };
        var cols8 = 8;
        for (var row = 0; row < 4; row++)
            for (var c = 0; c < cols8; c++)
                fleet.Children.Add(new Path { Data = g, Fill = Solid(cols[row]), RenderTransform = new TranslateTransform(c * 68, row * 46) });
        var move = new TranslateTransform();
        fleet.RenderTransform = move;
        Add(fleet, w * .5 - cols8 * 34, h * .12);
        if (Animated)
        {
            var span = Math.Max(60, w - cols8 * 68 - 60) / 2;
            var seconds = 16.0;
            var steps = 14;
            var a = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = D(seconds), AutoReverse = true };
            for (var s = 0; s <= steps; s++) a.KeyFrames.Add(new DiscreteDoubleKeyFrame(-span + 2 * span * s / steps, KeyTime.FromPercent((double)s / steps)));
            move.BeginAnimation(TranslateTransform.XProperty, a);
            var b = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = D(seconds * 2) };
            for (var s = 0; s <= 8; s++) b.KeyFrames.Add(new DiscreteDoubleKeyFrame(s * 6, KeyTime.FromPercent(s / 8.0 * .5)));
            for (var s = 1; s <= 8; s++) b.KeyFrames.Add(new DiscreteDoubleKeyFrame(48 - s * 6, KeyTime.FromPercent(.5 + s / 8.0 * .5)));
            move.BeginAnimation(TranslateTransform.YProperty, b);
        }
        for (var i = 0; i < 3; i++)
        {
            var laser = new Rectangle { Width = 2.4, Height = 14, Fill = Solid("FFFFFF"), IsHitTestVisible = false, Opacity = 0 };
            var t = Move(laser);
            Add(laser, R(.1, .9) * w, h);
            if (!Animated) continue;
            var cycle = R(3, 6);
            var ph = R(0, 1);
            t.BeginAnimation(TranslateTransform.YProperty, Keys(cycle, ph, (0, 0), (.3, -h), (1, -h)));
            laser.BeginAnimation(OpacityProperty, Keys(cycle, ph, (0, .9), (.3, .9), (.31, 0), (1, 0)));
        }
    }

    private void Synth(double w, double h)
    {
        var horizon = h * .6;
        var sunSize = Math.Min(w, h) * .5;
        var sun = new Ellipse { Width = sunSize, Height = sunSize, IsHitTestVisible = false, Fill = Grad(new Point(0, 0), new Point(0, 1), (C("FFD25E", 235), 0), (C("FF4FA8", 235), .6), (C("A02CC8", 235), 1)) };
        Add(sun, w * .5 - sunSize / 2, horizon - sunSize * .85);
        var (br, bg, bb) = Pal.Rgb("Bg");
        var bgBrush = Freeze(new SolidColorBrush(Color.FromRgb(br, bg, bb)));
        for (var i = 0; i < 7; i++)
        {
            var th = 2 + i * 2.4;
            Add(new Rectangle { Width = sunSize, Height = th, Fill = bgBrush, IsHitTestVisible = false }, w * .5 - sunSize / 2, horizon - sunSize * .85 + sunSize * (.46 + i * .075));
        }
        Add(new Rectangle { Width = w, Height = h - horizon, Fill = bgBrush, IsHitTestVisible = false }, 0, horizon);
        Add(new Rectangle { Width = w, Height = 2, Fill = Solid("FF4FD8", 230), IsHitTestVisible = false }, 0, horizon);
        var gridLine = Solid("FF4FD8", 120);
        for (var i = -12; i <= 12; i++)
        {
            var x2 = w * .5 + i * w * .22;
            var l = new Line { X1 = w * .5 + i * w * .018, Y1 = horizon, X2 = x2, Y2 = h, Stroke = gridLine, StrokeThickness = 1.2, IsHitTestVisible = false };
            Add(l, 0, 0);
        }
        for (var i = 0; i < 9; i++)
        {
            var bar = new Rectangle { Width = w, Height = 1.6, Fill = gridLine, IsHitTestVisible = false, Opacity = 0 };
            var t = Move(bar);
            Add(bar, 0, horizon);
            if (!Animated) { SetTop(bar, horizon + Math.Pow(i / 9.0, 2) * (h - horizon)); bar.Opacity = .7; continue; }
            var seconds = 4.5;
            var ph = i / 9.0;
            var a = new DoubleAnimation(0, h - horizon, D(seconds)) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(-ph * seconds), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
            t.BeginAnimation(TranslateTransform.YProperty, a);
            bar.BeginAnimation(OpacityProperty, Keys(seconds, ph, (0, 0), (.2, .5), (1, .9)));
        }
        for (var i = 0; i < 30; i++) Twinkle(Dot(R(1, 2.4), Solid("FFFFFF"), R(0, 1) * w, R(0, .55) * h, R(.2, .8)), .1, .9, R(2, 5), R(0, 1));
    }

    private void Matrix(double w, double h)
    {
        const string chars = "01ABCDEFGHIJKLMNOPQRSTUVWXYZ+=<>#$%ｱｲｳｴｵｶｷｸｹｺｻｼｽｾｿ";
        var cols = (int)Math.Clamp(w / 26, 12, 46);
        for (var i = 0; i < cols; i++)
        {
            var len = _rng.Next(10, 22);
            var text = string.Join("\n", Enumerable.Range(0, len).Select(_ => chars[_rng.Next(chars.Length)]));
            var col = new TextBlock
            {
                Text = text, FontFamily = new FontFamily("Consolas"), FontSize = 15, LineHeight = 17, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, IsHitTestVisible = false,
                Foreground = Grad(new Point(0, 0), new Point(0, 1), (C("3DFF7A", 0), 0), (C("3DFF7A", 150), .75), (C("D8FFE6", 255), 1)),
            };
            var t = Move(col);
            Add(col, (i + R(0, .6)) * (w / cols), -len * 17);
            if (Animated) Loop(t, TranslateTransform.YProperty, 0, h + len * 17, R(6, 14), R(0, 1));
            else SetTop(col, R(-.2, .6) * h);
        }
    }

    // ---- nature -----------------------------------------------------------------------------------------------

    private void Petals(double w, double h)
    {
        var branch = new StreamGeometry();
        using (var ctx = branch.Open())
        {
            ctx.BeginFigure(new Point(w + 10, h * .12), false, false);
            ctx.BezierTo(new Point(w * .86, h * .1), new Point(w * .8, h * .2), new Point(w * .66, h * .17), true, false);
            ctx.BezierTo(new Point(w * .6, h * .16), new Point(w * .56, h * .22), new Point(w * .5, h * .21), true, false);
        }
        branch.Freeze();
        var holder = new Canvas { IsHitTestVisible = false, RenderTransformOrigin = new Point(1, 0) };
        var sway = new RotateTransform();
        holder.RenderTransform = sway;
        holder.Children.Add(new Path { Data = branch, Stroke = Solid("3A2422", 230), StrokeThickness = 6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        var bloom = Radial(C("FFD3E0", 255), C("F59AB4", 235));
        foreach (var (fx, fy) in new[] { (.92, .115), (.86, .12), (.8, .175), (.73, .185), (.68, .165), (.62, .19), (.57, .205), (.52, .215), (.88, .15), (.76, .19) })
            holder.Children.Add(new Ellipse { Width = R(16, 26), Height = R(16, 26), Fill = bloom, RenderTransform = new TranslateTransform(w * fx - 10, h * fy - 12) });
        Add(holder, 0, 0);
        if (Animated) Loop(sway, RotateTransform.AngleProperty, -1.4, 1.4, 7, 0, reverse: true, smooth: true);
        var n = (int)Math.Clamp(w / 28, 20, 44);
        for (var i = 0; i < n; i++)
        {
            var petal = new Ellipse { Width = R(8, 14), Height = R(5, 8), Fill = Radial(C("FFD3E0", 255), C("F59AB4", 220)), IsHitTestVisible = false, Opacity = R(.6, .95), RenderTransformOrigin = new Point(.5, .5) };
            var fall = new TranslateTransform();
            var drift = new TranslateTransform();
            var spin = new RotateTransform(R(0, 360));
            petal.RenderTransform = new TransformGroup { Children = { spin, drift, fall } };
            Add(petal, R(.1, 1.1) * w, -20);
            if (Animated)
            {
                Loop(fall, TranslateTransform.YProperty, 0, h + 40, R(10, 20), R(0, 1));
                Loop(drift, TranslateTransform.XProperty, -R(40, 90), R(10, 60), R(3, 6), R(0, 1), reverse: true, smooth: true);
                Loop(spin, RotateTransform.AngleProperty, spin.Angle, spin.Angle + 360 * (i % 2 == 0 ? 1 : -1), R(5, 10), 0);
            }
            else SetTop(petal, R(0, 1) * h);
        }
    }

    private void Fireflies(double w, double h)
    {
        Add(new Ellipse { Width = w * .5, Height = w * .5, Fill = Radial(C("D9F25E", 55), C("D9F25E", 0)), IsHitTestVisible = false }, w * .62, -w * .2);
        var grass = new StreamGeometry();
        using (var ctx = grass.Open())
        {
            ctx.BeginFigure(new Point(0, h), true, true);
            for (var x = 0.0; x <= w; x += 9) ctx.LineTo(new Point(x, h - R(14, 56) * (.5 + .5 * Math.Sin(x / 140.0))), true, false);
            ctx.LineTo(new Point(w, h), true, false);
        }
        grass.Freeze();
        Add(new Path { Data = grass, Fill = Solid("04100A", 235), IsHitTestVisible = false }, 0, 0);
        for (var i = 0; i < 26; i++)
        {
            var fly = new Grid { Width = 26, Height = 26, IsHitTestVisible = false };
            fly.Children.Add(new Ellipse { Fill = Radial(C("E8FF7A", 190), C("D9F25E", 0)) });
            fly.Children.Add(new Ellipse { Width = 4, Height = 4, Fill = Solid("F6FFB0") });
            var tx = new TranslateTransform();
            var ty = new TranslateTransform();
            fly.RenderTransform = new TransformGroup { Children = { tx, ty } };
            Add(fly, R(0, 1) * w, R(.25, .95) * h);
            if (Animated)
            {
                Loop(tx, TranslateTransform.XProperty, -R(30, 90), R(30, 90), R(7, 15), R(0, 1), reverse: true, smooth: true);
                Loop(ty, TranslateTransform.YProperty, -R(20, 60), R(20, 60), R(5, 11), R(0, 1), reverse: true, smooth: true);
                Twinkle(fly, .05, 1, R(1.4, 3.4), R(0, 1));
            }
            else fly.Opacity = R(.4, 1);
        }
    }

    private void Deep(double w, double h)
    {
        for (var i = 0; i < 5; i++)
        {
            var wd = R(70, 150);
            var ray = Poly(Grad(new Point(.5, 0), new Point(.5, 1), (C("BFF3FF", 70), 0), (C("BFF3FF", 0), 1)), (wd * .35, 0), (wd * .65, 0), (wd, h * .9), (0, h * .9));
            ray.RenderTransformOrigin = new Point(.5, 0);
            var rot = new RotateTransform();
            ray.RenderTransform = rot;
            Add(ray, w * R(.05, .9), -10);
            if (Animated) { Loop(rot, RotateTransform.AngleProperty, -R(3, 7), R(3, 7), R(8, 14), R(0, 1), reverse: true, smooth: true); Twinkle(ray, .4, 1, R(5, 9), R(0, 1)); }
        }
        for (var i = 0; i < 6; i++)
        {
            var kelp = new StreamGeometry();
            var kh = R(.18, .38) * h;
            using (var ctx = kelp.Open())
            {
                ctx.BeginFigure(new Point(0, 0), false, false);
                ctx.BezierTo(new Point(-18, -kh * .3), new Point(18, -kh * .6), new Point(0, -kh), true, false);
            }
            kelp.Freeze();
            var p = new Path { Data = kelp, Stroke = Solid("0E5A4A", 200), StrokeThickness = R(5, 9), StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false, RenderTransformOrigin = new Point(0, 0) };
            var rot = new RotateTransform();
            p.RenderTransform = rot;
            Add(p, w * (i + R(.1, .8)) / 6, h);
            if (Animated) Loop(rot, RotateTransform.AngleProperty, -6, 6, R(4, 8), R(0, 1), reverse: true, smooth: true);
        }
        for (var i = 0; i < 4; i++)
        {
            var dir = i % 2 == 0 ? 1 : -1;
            var fish = new Canvas { IsHitTestVisible = false, Opacity = .5 };
            var len = R(36, 70);
            fish.Children.Add(new Ellipse { Width = len, Height = len * .38, Fill = Solid("07263A", 235) });
            fish.Children.Add(Poly(Solid("07263A", 235), (len * .86, len * .19), (len * 1.2, 0), (len * 1.2, len * .38)));
            var move = new TranslateTransform();
            fish.RenderTransform = new TransformGroup { Children = { new ScaleTransform(dir, 1, len * .5, 0), move } };
            Add(fish, dir > 0 ? 0 : w, R(.25, .75) * h);
            if (Animated) Loop(move, TranslateTransform.XProperty, dir > 0 ? -len * 1.4 : len * 1.4, dir > 0 ? w + len * 1.4 : -(w + len * 1.4), R(26, 46), R(0, 1));
            else SetLeft(fish, R(.1, .8) * w);
        }
        for (var i = 0; i < 24; i++)
        {
            var size = R(5, 14);
            var bubble = new Ellipse { Width = size, Height = size, Stroke = Solid("DFF8FF", 190), StrokeThickness = 1.2, Fill = Radial(C("FFFFFF", 50), C("FFFFFF", 0)), IsHitTestVisible = false };
            var rise = new TranslateTransform();
            var sway = new TranslateTransform();
            bubble.RenderTransform = new TransformGroup { Children = { sway, rise } };
            Add(bubble, R(0, 1) * w, h);
            if (Animated) { Loop(rise, TranslateTransform.YProperty, 0, -(h + 30), R(8, 18), R(0, 1)); Loop(sway, TranslateTransform.XProperty, -12, 12, R(2, 4), R(0, 1), reverse: true, smooth: true); }
            else SetTop(bubble, R(0, 1) * h);
        }
    }

    private void Storm(double w, double h)
    {
        for (var i = 0; i < 6; i++)
        {
            var size = R(300, 600);
            var cloud = new Ellipse { Width = size, Height = size * .4, Fill = Radial(C("3A4258", 200), C("3A4258", 0)), IsHitTestVisible = false };
            var t = Move(cloud);
            Add(cloud, R(-.1, .9) * w, -size * .15 + R(0, .12) * h);
            if (Animated) Loop(t, TranslateTransform.XProperty, -90, 130, R(24, 44), R(0, 1), reverse: true, smooth: true);
        }
        var n = (int)Math.Clamp(w / 22, 24, 60);
        var streak = Linear(C("BFD0F0", 0), C("BFD0F0", 150));
        for (var i = 0; i < n; i++)
        {
            var rain = new Rectangle { Width = 1.4, Height = R(20, 44), Fill = streak, IsHitTestVisible = false, Opacity = R(.4, .85) };
            var group = new TransformGroup();
            var t = new TranslateTransform();
            group.Children.Add(new RotateTransform(14));
            group.Children.Add(t);
            rain.RenderTransform = group;
            Add(rain, R(-.1, 1.1) * w, -50);
            if (Animated) Loop(t, TranslateTransform.YProperty, 0, h + 100, R(.5, .9), R(0, 1)); else SetTop(rain, R(0, 1) * h);
        }
        // a bolt and a flash of the whole sky
        var x = w * R(.25, .75);
        var bolt = new StreamGeometry();
        using (var ctx = bolt.Open())
        {
            ctx.BeginFigure(new Point(x, 0), false, false);
            var y = 0.0;
            var cx = x;
            while (y < h * .65) { y += R(30, 70); cx += R(-34, 34); ctx.LineTo(new Point(cx, y), true, false); }
        }
        bolt.Freeze();
        var path = new Path { Data = bolt, Stroke = Solid("EAF0FF"), StrokeThickness = 2.4, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false, Opacity = 0 };
        var flash = new Rectangle { Width = w, Height = h, Fill = Solid("DCE6FF", 60), IsHitTestVisible = false, Opacity = 0 };
        Add(flash, 0, 0);
        Add(path, 0, 0);
        if (Animated)
        {
            var cycle = 9.0;
            path.BeginAnimation(OpacityProperty, Keys(cycle, .5, (0, 0), (.9, 0), (.905, 1), (.92, .2), (.935, .9), (.97, 0), (1, 0)));
            flash.BeginAnimation(OpacityProperty, Keys(cycle, .5, (0, 0), (.9, 0), (.905, 1), (.92, .15), (.935, .8), (.99, 0), (1, 0)));
        }
    }

    // ---- space ------------------------------------------------------------------------------------------------

    private void Galaxy(double w, double h)
    {
        for (var i = 0; i < 50; i++) Twinkle(Dot(R(1, 2.2), Solid("FFFFFF"), R(0, 1) * w, R(0, 1) * h, R(.2, .7)), .1, .9, R(2, 6), R(0, 1));
        var holder = new Canvas { IsHitTestVisible = false };
        var rot = new RotateTransform();
        holder.RenderTransform = new TransformGroup { Children = { rot, new TranslateTransform(w * .5, h * .5) } };
        Add(holder, 0, 0);
        var span = Math.Min(w, h) * .62;
        holder.Children.Add(new Ellipse { Width = span * .7, Height = span * .7, Fill = Radial(C("FFE9FF", 190), C("A97BFF", 0)), RenderTransform = new TranslateTransform(-span * .35, -span * .35) });
        string[] cols = ["FFE9FF", "C9A0FF", "FF7AD0", "7AA8FF"];
        for (var arm = 0; arm < 3; arm++)
            for (var i = 0; i < 56; i++)
            {
                var t = i / 56.0;
                var r = span * (.06 + t * .94);
                var a = arm * Math.PI * 2 / 3 + t * 4.2 + R(-.18, .18);
                var size = R(1.4, 3.6) * (1.3 - t * .6);
                holder.Children.Add(new Ellipse { Width = size, Height = size, Fill = Solid(cols[(i + arm) % 4], (byte)(210 - t * 130)), RenderTransform = new TranslateTransform(Math.Cos(a) * r * 1.25, Math.Sin(a) * r * .8) });
            }
        holder.Opacity = .9;
        if (Animated) Loop(rot, RotateTransform.AngleProperty, 0, 360, 190, 0);
        holder.RenderTransformOrigin = new Point(0, 0);
    }

    private void Orbit(double w, double h)
    {
        var cx = w * .74;
        var cy = h * .56;
        var scale = Math.Min(w, h) / 600;
        var ink = Solid("7FE3FF", 110);
        double[] radii = [90, 150, 215, 290];
        for (var i = 0; i < radii.Length; i++)
        {
            var r = radii[i] * scale;
            var ringBox = new Canvas { IsHitTestVisible = false };
            ringBox.Children.Add(new Ellipse { Width = r * 2, Height = r * 2, Stroke = ink, StrokeThickness = 1.2, StrokeDashArray = i % 2 == 0 ? [] : [2, 5], RenderTransform = new TranslateTransform(-r, -r) });
            ringBox.Children.Add(new Ellipse { Width = 9 + i * 1.5, Height = 9 + i * 1.5, Fill = Solid(i % 2 == 0 ? "7FE3FF" : "8FA8FF"), RenderTransform = new TranslateTransform(r - 5, -4.5) });
            var rot = new RotateTransform(i * 70);
            ringBox.RenderTransform = new TransformGroup { Children = { rot, new TranslateTransform(cx, cy) } };
            Add(ringBox, 0, 0);
            ringBox.Opacity = .75;
            if (Animated) Loop(rot, RotateTransform.AngleProperty, i * 70, i * 70 + (i % 2 == 0 ? 360 : -360), 30 + i * 18, 0);
        }
        Add(new Ellipse { Width = 46 * scale + 16, Height = 46 * scale + 16, Fill = Radial(C("BFF3FF", 230), C("52D0F5", 0)), IsHitTestVisible = false }, cx - 23 * scale - 8, cy - 23 * scale - 8);
        var sweep = Poly(Grad(new Point(0, .5), new Point(1, .5), (C("7FE3FF", 0), 0), (C("7FE3FF", 100), 1)), (0, 0), (radii[3] * scale, -radii[3] * scale * .28), (radii[3] * scale, 0));
        var sRot = new RotateTransform();
        sweep.RenderTransform = new TransformGroup { Children = { sRot, new TranslateTransform(cx, cy) } };
        sweep.Opacity = .55;
        Add(sweep, 0, 0);
        if (Animated) Loop(sRot, RotateTransform.AngleProperty, 0, 360, 9, 0);
        for (var i = 0; i < 40; i++) Twinkle(Dot(R(1, 2.2), Solid("FFFFFF"), R(0, 1) * w, R(0, 1) * h, R(.2, .7)), .1, .9, R(2, 6), R(0, 1));
    }

    // ---- warm places ------------------------------------------------------------------------------------------

    private void Steam(double w, double h)
    {
        var lamps = (int)Math.Clamp(w / 260, 3, 6);
        for (var i = 0; i < lamps; i++)
        {
            var x = w * (i + .5) / lamps;
            var drop = h * R(.12, .28);
            var holder = new Canvas { IsHitTestVisible = false, RenderTransformOrigin = new Point(0, 0) };
            var rot = new RotateTransform();
            holder.RenderTransform = rot;
            holder.Children.Add(new Rectangle { Width = 1.4, Height = drop, Fill = Solid("5A3A22", 230), RenderTransform = new TranslateTransform(-.7, 0) });
            holder.Children.Add(new Ellipse { Width = 130, Height = 130, Fill = Radial(C("FFC27A", 120), C("E8A25A", 0)), RenderTransform = new TranslateTransform(-65, drop - 54) });
            holder.Children.Add(Poly(Solid("C77B34", 235), (-4, drop - 20), (4, drop - 20), (18, drop), (-18, drop)));
            holder.Children.Add(new Ellipse { Width = 22, Height = 20, Fill = Radial(C("FFF0C8", 255), C("FFB347", 240)), RenderTransform = new TranslateTransform(-11, drop - 2) });
            Add(holder, x, -4);
            if (Animated) { Loop(rot, RotateTransform.AngleProperty, -4, 4, R(4, 6), R(0, 1), reverse: true, smooth: true); Twinkle(holder, .8, 1, R(1.5, 3), R(0, 1)); }
        }
        for (var i = 0; i < 10; i++)
        {
            var wisp = new Ellipse { Width = R(36, 70), Height = R(110, 190), IsHitTestVisible = false, Fill = Radial(C("FFEBD0", 70), C("FFEBD0", 0)), RenderTransformOrigin = new Point(.5, 1) };
            var rise = new TranslateTransform();
            var sway = new TranslateTransform();
            var grow = new ScaleTransform(.7, 1);
            wisp.RenderTransform = new TransformGroup { Children = { grow, sway, rise } };
            Add(wisp, w * R(.05, .95), h * .85);
            if (Animated)
            {
                var seconds = R(7, 13);
                var ph = R(0, 1);
                Loop(rise, TranslateTransform.YProperty, 0, -h * .7, seconds, ph);
                Loop(sway, TranslateTransform.XProperty, -26, 26, R(3, 6), R(0, 1), reverse: true, smooth: true);
                Loop(grow, ScaleTransform.ScaleXProperty, .6, 1.6, seconds, ph);
                wisp.BeginAnimation(OpacityProperty, Keys(seconds, ph, (0, 0), (.25, .9), (1, 0)));
            }
            else SetTop(wisp, R(.3, .8) * h);
        }
    }

    private void Ripples(double w, double h)
    {
        var ring = Solid("FFD27A", 160);
        for (var i = 0; i < 7; i++)
        {
            var size = R(180, 360);
            var circle = new Ellipse { Width = size, Height = size * .62, Stroke = ring, StrokeThickness = 1.4, IsHitTestVisible = false, RenderTransformOrigin = new Point(.5, .5) };
            var scale = new ScaleTransform(.1, .1);
            circle.RenderTransform = scale;
            Add(circle, R(.05, .95) * w - size / 2, R(.1, .9) * h - size * .31);
            if (Animated)
            {
                var seconds = R(6, 10);
                var ph = i / 7.0;
                Loop(scale, ScaleTransform.ScaleXProperty, .08, 1, seconds, ph);
                Loop(scale, ScaleTransform.ScaleYProperty, .08, 1, seconds, ph);
                circle.BeginAnimation(OpacityProperty, Keys(seconds, ph, (0, .8), (1, 0)));
            }
            else { scale.ScaleX = scale.ScaleY = .6; circle.Opacity = .35; }
        }
        for (var i = 0; i < 4; i++)
        {
            var pad = new Ellipse { Width = R(46, 84), Height = R(30, 52), Fill = Solid("1E7A4A", 200), Stroke = Solid("0E4A2C", 220), StrokeThickness = 1.2, IsHitTestVisible = false };
            var t = Move(pad);
            Add(pad, R(.05, .9) * w, R(.1, .9) * h);
            if (Animated) Loop(t, TranslateTransform.XProperty, -18, 18, R(10, 18), R(0, 1), reverse: true, smooth: true);
        }
        string[] cols = ["FF6A33", "FFD27A", "FF8A55"];
        for (var i = 0; i < 3; i++)
        {
            var r = Math.Min(w, h) * (.22 + i * .09);
            var orbit = new Canvas { IsHitTestVisible = false, Opacity = .85 };
            var fish = new Canvas { RenderTransform = new TranslateTransform(r, 0) };
            var body = new Ellipse { Width = 12, Height = 30, Fill = Solid(cols[i]), RenderTransform = new TranslateTransform(-6, -15) };
            var tailRot = new RotateTransform(0, 0, 14);
            var tail = Poly(Solid(cols[i], 220), (0, 0), (7, 18), (-7, 18));
            tail.RenderTransform = new TransformGroup { Children = { new TranslateTransform(0, 14), tailRot } };
            fish.Children.Add(tail);
            fish.Children.Add(body);
            fish.Children.Add(new Ellipse { Width = 6, Height = 9, Fill = Solid("FFFFFF", 200), RenderTransform = new TranslateTransform(-3, -8) });
            orbit.Children.Add(fish);
            var rot = new RotateTransform(i * 120);
            var squash = new ScaleTransform(1, .62);
            orbit.RenderTransform = new TransformGroup { Children = { rot, squash, new TranslateTransform(w * .5, h * .5) } };
            Add(orbit, 0, 0);
            if (Animated)
            {
                Loop(rot, RotateTransform.AngleProperty, i * 120, i * 120 + (i % 2 == 0 ? 360 : -360), 28 + i * 9, 0);
                Loop(tailRot, RotateTransform.AngleProperty, -16, 16, .7, 0, reverse: true, smooth: true);
            }
        }
    }

    // ---- art --------------------------------------------------------------------------------------------------

    private void Shapes(double w, double h)
    {
        string[] cols = ["D93A2B", "2A56C6", "F2B705", "171717"];
        for (var i = 0; i < 16; i++)
        {
            var size = R(44, 130);
            var c = cols[i % 4];
            var brush = Solid(c, (byte)R(110, 190));
            Shape shape = (i % 4) switch
            {
                0 => new Ellipse { Width = size, Height = size, Fill = brush },
                1 => new Rectangle { Width = size, Height = size, Fill = brush },
                2 => Poly(brush, (0, size * .87), (size / 2, 0), (size, size * .87)),
                _ => new Ellipse { Width = size, Height = size, Stroke = Solid(c, 200), StrokeThickness = 3 },
            };
            shape.IsHitTestVisible = false;
            shape.RenderTransformOrigin = new Point(.5, .5);
            var rot = new RotateTransform(R(0, 360));
            var drift = new TranslateTransform();
            shape.RenderTransform = new TransformGroup { Children = { rot, drift } };
            Add(shape, R(0, 1) * w, R(0, 1) * h);
            if (Animated)
            {
                Loop(drift, TranslateTransform.XProperty, -R(30, 80), R(30, 80), R(12, 24), R(0, 1), reverse: true, smooth: true);
                Loop(drift, TranslateTransform.YProperty, -R(20, 60), R(20, 60), R(10, 20), R(0, 1), reverse: true, smooth: true);
                Loop(rot, RotateTransform.AngleProperty, rot.Angle, rot.Angle + (i % 2 == 0 ? 360 : -360), R(40, 90), 0);
            }
        }
        for (var i = 0; i < 5; i++)
        {
            var bar = new Rectangle { Width = R(120, 340), Height = 2.4, Fill = Solid("171717", 150), IsHitTestVisible = false, RenderTransformOrigin = new Point(.5, .5) };
            var drift = new TranslateTransform();
            bar.RenderTransform = new TransformGroup { Children = { new RotateTransform(R(-60, 60)), drift } };
            Add(bar, R(0, 1) * w, R(0, 1) * h);
            if (Animated) Loop(drift, TranslateTransform.XProperty, -60, 60, R(14, 26), R(0, 1), reverse: true, smooth: true);
        }
    }
}
