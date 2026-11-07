using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Shabpa.Core;

namespace Shabpa.Kit;

// The painted backdrops. Each premium theme has one still painting (the "plate", a JPEG behind everything) and a few small
// pictures (sprites) that move over it: snow, sparks, lanterns, fish. The paintings and the motion are made by
// tools/theme-art (render.js), which also writes Assets/themes/scenes.json: where each sprite sits on the painting and how it moves.
// The same rules play in tools/theme-art/preview.html, so what is checked there is what the app shows.
// Only transforms and opacity are animated, at the scene's low frame rate; nothing runs per frame in code.
public sealed partial class ThemeScene
{
    private static Dictionary<string, JsonElement>? _specs;
    private static readonly Dictionary<string, BitmapSource> Sprites = new();
    private static readonly List<(string file, int width, BitmapSource image)> Plates = new();

    private static JsonElement? SpecFor(string key)
    {
        if (_specs is null)
        {
            try
            {
                using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/themes/scenes.json"))!.Stream;
                using var doc = JsonDocument.Parse(stream);
                _specs = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
            }
            catch { _specs = new(); } // no application resources (tests, design time)
        }
        return _specs.TryGetValue(key, out var s) ? s : null;
    }

    private static BitmapSource? Load(string file, int width = 0)
    {
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri($"pack://application:,,,/Assets/themes/{file}", UriKind.Absolute);
            if (width > 0) img.DecodePixelWidth = width;
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch { return null; }
    }

    private static BitmapSource? Sprite(string file)
    {
        if (Sprites.TryGetValue(file, out var hit)) return hit;
        var img = Load(file);
        if (img is not null) Sprites[file] = img;
        return img;
    }

    /// <summary>The painting decoded no wider than it is shown (a 2400-pixel picture behind a small preview would waste memory).
    /// Only the last two are kept: the running theme's and the one in the store preview.</summary>
    private static BitmapSource? Plate(string key, double pixels)
    {
        var width = pixels >= 2000 ? 0 : (int)Math.Ceiling(pixels / 320) * 320;
        var file = key + ".jpg";
        foreach (var p in Plates) if (p.file == file && p.width == width) return p.image;
        var img = Load(file, width);
        if (img is null) return null;
        Plates.Insert(0, (file, width, img));
        if (Plates.Count > 2) Plates.RemoveAt(2);
        if (Sprites.Count > 64) Sprites.Clear();
        return img;
    }

    /// <summary>The small still picture of a theme for the store's grid (480 by 240).</summary>
    public static ImageSource? Thumb(string key)
    {
        var file = key + ".thumb.jpg";
        if (Sprites.TryGetValue(file, out var hit)) return hit;
        var img = Load(file);
        if (img is not null) Sprites[file] = img;
        return img;
    }

    private static double Num(JsonElement l, string name, double fallback) =>
        l.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : fallback;

    /// <summary>A value that is either one number or a [low, high] range to pick from.</summary>
    private double Pick(JsonElement l, string name, double fallback) =>
        !l.TryGetProperty(name, out var v) ? fallback
        : v.ValueKind == JsonValueKind.Array ? R(v[0].GetDouble(), v[1].GetDouble())
        : v.ValueKind == JsonValueKind.Number ? v.GetDouble() : fallback;

    private static double Frac(double x) => x - Math.Floor(x);

    private static double Sine(double s) => (1 - Math.Cos(Math.PI * s)) / 2;

    /// <summary>Where an endless low→high→low sine swing is at time zero (for the still picture).</summary>
    private static double SwingAt0(double phase, double lo, double hi) { var u = Frac(phase); var s = u < .5 ? u * 2 : 2 - u * 2; return lo + (hi - lo) * Sine(s); }

    private static double KeysAt(JsonElement frames, double u)
    {
        var prev = (at: frames[0][0].GetDouble(), v: frames[0][1].GetDouble());
        if (u <= prev.at) return prev.v;
        foreach (var k in frames.EnumerateArray())
        {
            var at = k[0].GetDouble(); var v = k[1].GetDouble();
            if (u <= at) return at == prev.at ? v : prev.v + (v - prev.v) * (u - prev.at) / (at - prev.at);
            prev = (at, v);
        }
        return prev.v;
    }

    private static DoubleAnimationUsingKeyFrames Frames(double seconds, double phase, int fps)
    {
        var a = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = D(seconds), BeginTime = TimeSpan.FromSeconds(-Frac(phase) * seconds) };
        Timeline.SetDesiredFrameRate(a, fps);
        return a;
    }

    /// <summary>Lays out the painting and its moving parts. False when the theme has no painting (then the drawn scene is used).</summary>
    private bool Art(double w, double h)
    {
        var key = PremiumTheme.All.FirstOrDefault(t => t.Scene == _scene)?.Key;
        if (key is null || SpecFor(key) is not { } spec) return false;
        double W = spec.GetProperty("w").GetDouble(), H = spec.GetProperty("h").GetDouble();
        var s = Math.Max(w / W, h / H);
        double ox = (w - W * s) * Num(spec, "ax", .5), oy = (h - H * s) * Num(spec, "ay", .5);
        var dpi = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        if (Plate(key, W * s * dpi) is not { } plate) return false;
        var bg = new Image { Source = plate, Width = W * s, Height = H * s, Stretch = Stretch.Fill, IsHitTestVisible = false };
        RenderOptions.SetBitmapScalingMode(bg, BitmapScalingMode.HighQuality);
        SetLeft(bg, ox);
        SetTop(bg, oy);
        Children.Add(bg);

        var sizes = spec.GetProperty("sprites");
        var li = 0;
        foreach (var l in spec.GetProperty("layers").EnumerateArray())
        {
            _rng = new Random(li++ * 7919 + 17);
            var fps = (int)Num(l, "fps", SceneFps);
            var names = l.GetProperty("sprite") is { ValueKind: JsonValueKind.Array } arr ? arr.EnumerateArray().Select(e => e.GetString()!).ToArray() : [l.GetProperty("sprite").GetString()!];
            var rect = names[0].StartsWith('#');
            List<Point>? at = null;
            if (l.TryGetProperty("at", out var atEl)) at = atEl.EnumerateArray().Select(p => new Point(p[0].GetDouble(), p[1].GetDouble())).ToList();
            else if (l.TryGetProperty("grid", out var gr))
            {
                at = [];
                for (var row = 0; row < gr.GetProperty("rows").GetInt32(); row++)
                    for (var col = 0; col < gr.GetProperty("cols").GetInt32(); col++)
                        at.Add(new Point(gr.GetProperty("x").GetDouble() + col * gr.GetProperty("dx").GetDouble(), gr.GetProperty("y").GetDouble() + row * gr.GetProperty("dy").GetDouble()));
            }
            var n = at?.Count ?? (int)Num(l, "n", 1);
            double sw, sh;
            if (rect) { var rr = l.TryGetProperty("rect", out var re) ? (re[0].GetDouble(), re[1].GetDouble()) : (W, H); (sw, sh) = rr; }
            else { var sz = sizes.GetProperty(names[0]); (sw, sh) = (sz[0].GetDouble(), sz[1].GetDouble()); }
            var images = rect ? null : names.Select(nm => Sprite($"{key}.{nm}.png")).ToArray();
            if (images is not null && images.Any(i => i is null)) continue;
            l.TryGetProperty("move", out var move);
            l.TryGetProperty("fade", out var fade);
            l.TryGetProperty("sway", out var sway);
            l.TryGetProperty("twinkle", out var twinkle);
            l.TryGetProperty("spin", out var spin);
            l.TryGetProperty("pulse", out var pulse);
            l.TryGetProperty("wag", out var wag);
            l.TryGetProperty("path", out var path);
            l.TryGetProperty("grow", out var grow);
            l.TryGetProperty("frames", out var framesEl);
            for (var i = 0; i < n; i++)
            {
                var ew = rect ? sw : Pick(l, "size", sw);
                var eh = ew * sh / sw;
                double px, py;
                if (at is not null) (px, py) = (at[i].X, at[i].Y);
                else if (rect && !l.TryGetProperty("x", out _)) (px, py) = (W / 2, H / 2);
                else { px = Pick(l, "x", R(0, 1)) * W; py = Pick(l, "y", R(0, 1)) * H; }
                // the phases, in the order the preview takes them (each animation its own, unless the layer fixes it)
                double Phase() => l.TryGetProperty("phase", out var ph) ? Frac(ph.GetDouble() + i * Num(l, "phaseStep", 0)) : R(0, 1);
                var rot0 = Pick(l, "rot", 0);
                var flip = l.TryGetProperty("flip", out var fl) ? fl.ValueKind == JsonValueKind.String ? (R(0, 1) < .5 ? -1 : 1) : fl.ValueKind == JsonValueKind.True ? -1 : 1 : 1;
                var op = Pick(l, "op", 1);

                var origin = l.TryGetProperty("origin", out var org) ? new Point(org[0].GetDouble(), org[1].GetDouble()) : new Point(.5, .5);
                var holder = new Grid { Width = ew * s, Height = eh * s, IsHitTestVisible = false, RenderTransformOrigin = origin, Opacity = op };
                var inner = new List<UIElement>();
                if (rect) inner.Add(new Rectangle { Fill = Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(names[0]))) });
                else foreach (var img in images!) inner.Add(new Image { Source = img, Stretch = Stretch.Fill });
                foreach (var e in inner) holder.Children.Add(e);
                var flipT = new ScaleTransform(flip, Num(l, "squash", 1));
                var growT = new ScaleTransform(1, 1);
                var pulseT = new ScaleTransform(1, 1);
                var rotT = new RotateTransform(rot0);
                var spinT = new RotateTransform();
                var faceT = new RotateTransform();
                var wagT = new RotateTransform();
                var swayT = new TranslateTransform();
                var moveT = new TranslateTransform();
                var group = new TransformGroup { Children = { flipT, growT, pulseT, rotT, spinT, faceT, wagT } };
                if (l.TryGetProperty("tilt", out var tilt))
                {
                    // a disc seen at an angle: turned in its own plane first, then flattened and leaned
                    group.Children.Add(new ScaleTransform(1, Num(tilt, "squash", 1)));
                    group.Children.Add(new RotateTransform(Num(tilt, "rot", 0)));
                }
                group.Children.Add(swayT);
                group.Children.Add(moveT);
                holder.RenderTransform = group;
                SetLeft(holder, ox + px * s - ew * s / 2);
                SetTop(holder, oy + py * s - eh * s / 2);
                Children.Add(holder);
                var anim = Animated;

                if (move.ValueKind == JsonValueKind.Object)
                {
                    var mph = Phase();
                    var dur = Pick(move, "dur", 10);
                    double dx = Pick(move, "dx", 0) * s, dy = Pick(move, "dy", 0) * s;
                    var travel = Num(move, "travel", 1);
                    var ease = move.TryGetProperty("ease", out var ee) ? ee.GetString() : null;
                    double Ease(double p) => ease == "in" ? p * p : ease == "out" ? 1 - (1 - p) * (1 - p) : p;
                    if (anim)
                    {
                        foreach (var (dp, d) in new[] { (TranslateTransform.XProperty, dx), (TranslateTransform.YProperty, dy) })
                        {
                            if (d == 0) continue;
                            var a = Frames(dur, mph, fps);
                            a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
                            var k = new EasingDoubleKeyFrame(d, KeyTime.FromPercent(travel)) { EasingFunction = ease == "in" ? new QuadraticEase { EasingMode = EasingMode.EaseIn } : ease == "out" ? new QuadraticEase { EasingMode = EasingMode.EaseOut } : null };
                            a.KeyFrames.Add(k);
                            if (travel < 1) a.KeyFrames.Add(new LinearDoubleKeyFrame(d, KeyTime.FromPercent(1)));
                            moveT.BeginAnimation(dp, a);
                        }
                        if (fade.ValueKind == JsonValueKind.Array)
                        {
                            var a = Frames(dur, mph, fps);
                            foreach (var kf in fade.EnumerateArray()) a.KeyFrames.Add(new LinearDoubleKeyFrame(kf[1].GetDouble() * op, KeyTime.FromPercent(kf[0].GetDouble())));
                            holder.BeginAnimation(OpacityProperty, a);
                        }
                        if (grow.ValueKind == JsonValueKind.Array)
                            foreach (var dp in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
                            {
                                var a = Frames(dur, mph, fps);
                                a.KeyFrames.Add(new LinearDoubleKeyFrame(grow[0].GetDouble(), KeyTime.FromPercent(0)));
                                a.KeyFrames.Add(new LinearDoubleKeyFrame(grow[1].GetDouble(), KeyTime.FromPercent(1)));
                                growT.BeginAnimation(dp, a);
                            }
                    }
                    else
                    {
                        var p = Ease(Math.Min(mph / travel, 1));
                        moveT.X = dx * p; moveT.Y = dy * p;
                        if (fade.ValueKind == JsonValueKind.Array) holder.Opacity = op * KeysAt(fade, mph);
                        if (grow.ValueKind == JsonValueKind.Array) growT.ScaleX = growT.ScaleY = grow[0].GetDouble() + (grow[1].GetDouble() - grow[0].GetDouble()) * mph;
                    }
                }
                else if (fade.ValueKind == JsonValueKind.Array)
                {
                    var fph = Phase();
                    var fdur = Pick(l, "fadeDur", 6);
                    if (anim)
                    {
                        var a = Frames(fdur, fph, fps);
                        foreach (var kf in fade.EnumerateArray()) a.KeyFrames.Add(new LinearDoubleKeyFrame(kf[1].GetDouble() * op, KeyTime.FromPercent(kf[0].GetDouble())));
                        holder.BeginAnimation(OpacityProperty, a);
                    }
                    else holder.Opacity = op * KeysAt(fade, fph);
                }
                if (sway.ValueKind == JsonValueKind.Object)
                {
                    double ax = Pick(sway, "x", 0), ay = Pick(sway, "y", 0);
                    var sdx = Pick(sway, "dur", 5);
                    var hasY = sway.TryGetProperty("dury", out _);
                    var sdy = hasY ? Pick(sway, "dury", 5) : sdx;
                    var phx = Phase();
                    var phy = hasY ? Phase() : phx;
                    if (anim)
                    {
                        if (ax != 0) Loop(swayT, TranslateTransform.XProperty, -ax * s, ax * s, sdx, phx, reverse: true, smooth: true, fps: fps);
                        if (ay != 0) Loop(swayT, TranslateTransform.YProperty, -ay * s, ay * s, sdy, phy, reverse: true, smooth: true, fps: fps);
                    }
                    else { swayT.X = SwingAt0(phx, -ax, ax) * s; swayT.Y = SwingAt0(phy, -ay, ay) * s; }
                }
                if (twinkle.ValueKind == JsonValueKind.Object)
                {
                    var tdur = Pick(twinkle, "dur", 3);
                    var tph = Phase();
                    double lo = Num(twinkle, "lo", .3), hi = Num(twinkle, "hi", 1);
                    if (inner.Count == 1)
                    {
                        if (anim) Loop(inner[0], OpacityProperty, lo, hi, tdur, tph, reverse: true, smooth: true, fps: fps);
                        else inner[0].Opacity = SwingAt0(tph, lo, hi);
                    }
                }
                if (spin.ValueKind == JsonValueKind.Object)
                {
                    var deg = Pick(spin, "deg", 360);
                    if (spin.TryGetProperty("either", out var ei) && ei.GetBoolean() && R(0, 1) < .5) deg = -deg;
                    var sdur = Pick(spin, "dur", 20);
                    var sph = Phase();
                    var back = spin.TryGetProperty("back", out var bk) && bk.GetBoolean();
                    if (anim)
                    {
                        if (back) Loop(spinT, RotateTransform.AngleProperty, -deg, deg, sdur, sph, reverse: true, smooth: true, fps: fps);
                        else Loop(spinT, RotateTransform.AngleProperty, 0, deg, sdur, sph, fps: fps);
                    }
                    else spinT.Angle = back ? SwingAt0(sph, -deg, deg) : deg * sph;
                }
                if (pulse.ValueKind == JsonValueKind.Object)
                {
                    var pdur = Pick(pulse, "dur", 4);
                    var pph = Phase();
                    double lo = Num(pulse, "lo", .9), hi = Num(pulse, "hi", 1.1);
                    if (anim)
                    {
                        Loop(pulseT, ScaleTransform.ScaleXProperty, lo, hi, pdur, pph, reverse: true, smooth: true, fps: fps);
                        Loop(pulseT, ScaleTransform.ScaleYProperty, lo, hi, pdur, pph, reverse: true, smooth: true, fps: fps);
                    }
                    else pulseT.ScaleX = pulseT.ScaleY = SwingAt0(pph, lo, hi);
                }
                if (wag.ValueKind == JsonValueKind.Object)
                {
                    var deg = Pick(wag, "deg", 5);
                    var wdur = Pick(wag, "dur", 1);
                    var wph = Phase();
                    if (anim) Loop(wagT, RotateTransform.AngleProperty, -deg, deg, wdur, wph, reverse: true, smooth: true, fps: fps);
                    else wagT.Angle = SwingAt0(wph, -deg, deg);
                }
                if (path.ValueKind == JsonValueKind.Object) FollowPath(path, px, py, s, moveT, faceT, fps, anim, Phase);
                if (framesEl.ValueKind == JsonValueKind.Object && inner.Count > 1)
                {
                    var ffps = Num(framesEl, "fps", 2);
                    var cycle = inner.Count / ffps;
                    var fph = Phase();
                    for (var k = 0; k < inner.Count; k++)
                    {
                        if (anim)
                        {
                            var a = Frames(cycle, fph, fps);
                            a.KeyFrames.Add(new DiscreteDoubleKeyFrame(k == 0 ? 1 : 0, KeyTime.FromPercent(0)));
                            if (k > 0) a.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromPercent(k / (double)inner.Count)));
                            if (k < inner.Count - 1) a.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromPercent((k + 1) / (double)inner.Count)));
                            inner[k].BeginAnimation(OpacityProperty, a);
                        }
                        else inner[k].Opacity = (int)(fph * inner.Count) % inner.Count == k ? 1 : 0;
                    }
                }
                else if (inner.Count > 1) for (var k = 1; k < inner.Count; k++) inner[k].Opacity = 0;
            }
        }
        if (Veiled) Scrim(key, w, h);   // last: over the moving parts too, so a cloud or a lily pad passing behind a label never washes it out
        return true;
    }

    /// <summary>Text sits straight on the painting in the sidebar, the page header and the footer: a soft veil in the theme's own background colour
    /// keeps it readable on any plate (strong where the words are, almost nothing in the middle where the cards already carry the text).</summary>
    /// <summary>How much of the theme's own background colour is laid over the whole painting so text that sits straight on it stays readable.
    /// Worked out per theme from screenshots of every page (the muted text against the brightest 5 % of the backdrop); bright, busy paintings need more.</summary>
    private static readonly Dictionary<string, double> Dim = new()
    {
        ["sakura"] = .14, ["synthwave"] = .14, ["dunes"] = .18, ["rainglass"] = .28, ["storm"] = .22, ["orbit"] = .22, ["battlefield"] = .22, ["prism"] = .26, ["embers"] = .30,
        ["christmas"] = .36, ["snowfall"] = .36, ["teahouse"] = .38, ["yalda"] = .40, ["fireflies"] = .42, ["koi"] = .44, ["tilework"] = .46, ["persepolis"] = .46, ["deep"] = .46,
        ["arcade"] = .45, ["radiant"] = .38, ["blocky"] = .50,
    };

    internal static double DimFor(string key) => Dim.GetValueOrDefault(key, .12);

    private void Scrim(string key, double w, double h)
    {
        var (r, g, b) = Ui.Theme.Rgb("Bg");
        Color At(double a) => Color.FromArgb((byte)Math.Round(a * 255), r, g, b);
        void Veil(double x, double y, double vw, double vh, Brush fill)
        {
            var veil = new Rectangle { Width = vw, Height = vh, Fill = fill, IsHitTestVisible = false };
            SetLeft(veil, x);
            SetTop(veil, y);
            Children.Add(veil);
        }
        LinearGradientBrush Fade(Point from, Point to, params (double at, double alpha)[] stops)
        {
            var brush = new LinearGradientBrush { StartPoint = from, EndPoint = to };
            foreach (var (at, alpha) in stops) brush.GradientStops.Add(new GradientStop(At(alpha), at));
            brush.Freeze();
            return brush;
        }
        Veil(0, 0, w, h, Freeze(new SolidColorBrush(At(DimFor(key)))));                          // the whole painting a little calmer
        var side = Math.Min(w, 262);
        if (Ui.Flow == FlowDirection.RightToLeft) Veil(w - side, 0, side, h, Fade(new Point(1, 0), new Point(0, 0), (0, .78), (.86, .78), (1, 0)));   // the sidebar is on the right in Persian
        else Veil(0, 0, side, h, Fade(new Point(0, 0), new Point(1, 0), (0, .78), (.86, .78), (1, 0)));                                                  // the sidebar
        Veil(0, 0, w, Math.Min(h, 150), Fade(new Point(0, 0), new Point(0, 1), (0, .62), (1, 0)));                  // the page title
        Veil(0, Math.Max(0, h - 64), w, Math.Min(h, 64), Fade(new Point(0, 0), new Point(0, 1), (0, 0), (1, .72)));   // the footer
    }

    /// <summary>A sprite that swims or flies along a loop: key frames for its position and its heading, so it always faces
    /// the way it is going (the sprites are drawn facing right).</summary>
    private void FollowPath(JsonElement p, double px, double py, double s, TranslateTransform moveT, RotateTransform faceT, int fps, bool anim, Func<double> phase)
    {
        double cx = Pick(p, "cx", px), cy = Pick(p, "cy", py), rx = Pick(p, "rx", 200), ry = Pick(p, "ry", 120);
        var dir = p.TryGetProperty("dir", out var d) && d.GetDouble() != 0 ? Math.Sign(d.GetDouble()) : (R(0, 1) < .5 ? -1 : 1);
        var dur = Pick(p, "dur", 30);
        var ph = phase();
        var wob = Pick(p, "wob", 0);
        var lobes = Num(p, "lobes", 3);
        var w0 = R(0, Math.PI * 2);
        var face = !p.TryGetProperty("face", out var fc) || fc.ValueKind != JsonValueKind.False;
        var steps = (int)Num(p, "steps", 72);
        Point At(double u) { var a = u * Math.PI * 2 * dir; var m = 1 + wob * Math.Sin(lobes * a + w0); return new Point(cx + rx * m * Math.Cos(a), cy + ry * m * Math.Sin(a)); }
        var xs = Frames(dur, ph, fps); var ys = Frames(dur, ph, fps); var angs = Frames(dur, ph, fps);
        double? prev = null;
        double sx = 0, sy = 0, sa = 0;
        for (var k = 0; k <= steps; k++)
        {
            var u = k / (double)steps;
            var pt = At(u); var a = At(u + .5 / steps); var b = At(u - .5 / steps);
            var ang = Math.Atan2(a.Y - b.Y, a.X - b.X) * 180 / Math.PI;
            if (prev is { } pv) { while (ang - pv > 180) ang -= 360; while (ang - pv < -180) ang += 360; }
            prev = ang;
            var kt = KeyTime.FromPercent(u);
            xs.KeyFrames.Add(new LinearDoubleKeyFrame((pt.X - px) * s, kt));
            ys.KeyFrames.Add(new LinearDoubleKeyFrame((pt.Y - py) * s, kt));
            angs.KeyFrames.Add(new LinearDoubleKeyFrame(ang, kt));
            if (k == (int)Math.Round(ph * steps)) (sx, sy, sa) = ((pt.X - px) * s, (pt.Y - py) * s, ang);
        }
        if (anim)
        {
            moveT.BeginAnimation(TranslateTransform.XProperty, xs);
            moveT.BeginAnimation(TranslateTransform.YProperty, ys);
            if (face) faceT.BeginAnimation(RotateTransform.AngleProperty, angs);
        }
        else { moveT.X = sx; moveT.Y = sy; if (face) faceT.Angle = sa; }
    }
}
