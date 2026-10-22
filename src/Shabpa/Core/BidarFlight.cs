namespace Shabpa.Core;

public readonly record struct Pt(double X, double Y)
{
    public static double Distance(Pt a, Pt b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}

/// <summary>A rectangle (x, y = top-left). Pure numbers so the planner needs no WPF.</summary>
public readonly record struct Box(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;
    public bool Intersects(Box o) => X < o.Right && Right > o.X && Y < o.Bottom && Bottom > o.Y;
    public bool Contains(Box inner) => inner.X >= X - 1e-6 && inner.Y >= Y - 1e-6 && inner.Right <= Right + 1e-6 && inner.Bottom <= Bottom + 1e-6;
    public Box Inflate(double d) => new(X - d, Y - d, W + 2 * d, H + 2 * d);
}

/// <summary>Where Bidar may rest: the corners, the bottom margin and the side gutters of the page, kept only where nothing
/// the person reads or presses is underneath. Positions are the owl's top-left corner in the layer's own pixels.</summary>
public static class Perches
{
    public const double SideMargin = 26, BottomMargin = 12, Pad = 4;

    /// <summary>The owl's home corner: the end side of the page (the left in Persian), as the first version docked it.</summary>
    public static Pt Home(Box area, double w, double h, bool rtl) =>
        new(rtl ? area.X + SideMargin : area.Right - w - SideMargin, area.Bottom - h - BottomMargin);

    /// <summary>Every place worth considering, safe or not (home first).</summary>
    public static IReadOnlyList<Pt> Candidates(Box area, double w, double h, bool rtl)
    {
        var list = new List<Pt> { Home(area, w, h, rtl) };
        var left = area.X + SideMargin;
        var right = area.Right - w - SideMargin;
        var bottom = area.Bottom - h - BottomMargin;
        list.Add(new Pt(rtl ? right : left, bottom)); // the far bottom corner
        foreach (var f in new[] { 0.2, 0.4, 0.6, 0.8 }) list.Add(new Pt(left + (right - left) * f, bottom));
        var top = area.Y + 8;
        list.Add(new Pt(area.X + 8, top));
        list.Add(new Pt(area.Right - w - 8, top));
        foreach (var f in new[] { 0.3, 0.5, 0.7 }) // the side margins, where the page leaves a gutter (the no-go check decides)
        {
            var y = area.Y + (area.H - h) * f;
            list.Add(new Pt(area.X + 6, y));
            list.Add(new Pt(area.Right - w - 6, y));
        }
        return list;
    }

    /// <summary>True when the owl's rectangle at <paramref name="p"/> is inside the area and clear of every no-go rectangle.</summary>
    public static bool IsClear(Pt p, double w, double h, Box area, IReadOnlyList<Box> noGo)
    {
        var rect = new Box(p.X, p.Y, w, h);
        if (!area.Contains(rect)) return false;
        var padded = rect.Inflate(Pad);
        foreach (var n in noGo)
            if (padded.Intersects(n)) return false;
        return true;
    }

    public static IReadOnlyList<Pt> Valid(Box area, double w, double h, bool rtl, IReadOnlyList<Box> noGo) =>
        [.. Candidates(area, w, h, rtl).Where(p => IsClear(p, w, h, area, noGo))];

    /// <summary>A random safe place at least <paramref name="minDistance"/> away from <paramref name="current"/>; null if none.</summary>
    public static Pt? Choose(IReadOnlyList<Pt> valid, Pt current, Random rng, double minDistance = 120)
    {
        var far = valid.Where(p => Pt.Distance(p, current) >= minDistance).ToList();
        return far.Count == 0 ? null : far[rng.Next(far.Count)];
    }

    /// <summary>After a resize or a page change: the current place if it is still safe, else the nearest safe one, else the
    /// home corner clamped into the area (the owl never leaves the window).</summary>
    public static Pt Revalidate(Pt current, Box area, double w, double h, bool rtl, IReadOnlyList<Box> noGo)
    {
        if (IsClear(current, w, h, area, noGo)) return current;
        var valid = Valid(area, w, h, rtl, noGo);
        if (valid.Count > 0) return valid.OrderBy(p => Pt.Distance(p, current)).First();
        return Clamp(Home(area, w, h, rtl), area, w, h);
    }

    public static Pt Clamp(Pt p, Box area, double w, double h) =>
        new(Math.Clamp(p.X, area.X, Math.Max(area.X, area.Right - w)), Math.Clamp(p.Y, area.Y, Math.Max(area.Y, area.Bottom - h)));
}

/// <summary>A smooth cubic curve from one resting place to another.</summary>
public sealed record FlightPlan(Pt From, Pt To, Pt C1, Pt C2, double Seconds)
{
    public Pt At(double u)
    {
        u = Math.Clamp(u, 0, 1);
        var v = 1 - u;
        var a = v * v * v;
        var b = 3 * v * v * u;
        var c = 3 * v * u * u;
        var d = u * u * u;
        return new Pt(a * From.X + b * C1.X + c * C2.X + d * To.X, a * From.Y + b * C1.Y + c * C2.Y + d * To.Y);
    }
}

public static class FlightPlanner
{
    /// <summary>Plans the curve, or returns null when no arc keeps the owl inside the area and off every no-go rectangle.
    /// The control points are clamped into the area, and a Bezier curve never leaves the hull of its control points, so the
    /// whole path stays inside; the no-go check samples the curve.</summary>
    public static FlightPlan? Plan(Pt from, Pt to, double w, double h, Box area, IReadOnlyList<Box> noGo, Random rng)
    {
        var dist = Pt.Distance(from, to);
        if (dist < 1) return null;
        var allowed = new Box(area.X, area.Y, Math.Max(0, area.W - w), Math.Max(0, area.H - h)); // where the top-left may be
        Pt Fit(Pt p) => new(Math.Clamp(p.X, allowed.X, allowed.Right), Math.Clamp(p.Y, allowed.Y, allowed.Bottom));
        from = Fit(from);
        to = Fit(to);
        var seconds = Math.Clamp(0.8 + dist / 520, 0.9, 2.2);
        // arc heights as a fraction of the distance; positive = up. A high arc first, then flatter and lower ones.
        foreach (var f in new[] { 0.4, 0.75, 1.1, 0.22, 0.08, -0.25, -0.5 })
        {
            var jitter = 0.9 + rng.NextDouble() * 0.2;
            var lift = f > 0 ? Math.Max(36, dist * f * jitter) : dist * f * jitter;
            var c1 = Fit(new Pt(from.X + (to.X - from.X) * 0.25, from.Y + (to.Y - from.Y) * 0.25 - lift));
            var c2 = Fit(new Pt(from.X + (to.X - from.X) * 0.75, from.Y + (to.Y - from.Y) * 0.75 - lift));
            var plan = new FlightPlan(from, to, c1, c2, seconds);
            if (IsClear(plan, w, h, noGo)) return plan;
        }
        return null;
    }

    /// <summary>Samples the curve; the first and last tenth are ignored (the owl is leaving or arriving at a place that is safe by itself).</summary>
    public static bool IsClear(FlightPlan plan, double w, double h, IReadOnlyList<Box> noGo)
    {
        if (noGo.Count == 0) return true;
        const int steps = 40;
        for (var i = 4; i <= steps - 4; i++)
        {
            var p = plan.At(i / (double)steps);
            var rect = new Box(p.X, p.Y, w, h).Inflate(2);
            foreach (var n in noGo)
                if (rect.Intersects(n)) return false;
        }
        return true;
    }
}

/// <summary>One frame of the whole flight: where the owl is and how its body looks. Pure maths of the elapsed time, so the
/// path and the poses can be checked without a window.</summary>
public readonly record struct FlightFrame(Pt Pos, double WingL, double WingR, double Tilt, double Squash, bool Airborne, bool Done);

public static class FlightMotion
{
    public const double Crouch = 0.24, Landing = 0.3, FlapsPerSecond = 5.5;

    public static double Total(FlightPlan plan) => Crouch + plan.Seconds + Landing;

    private static double Ease(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;

    public static FlightFrame Frame(FlightPlan plan, double elapsed)
    {
        var total = Total(plan);
        if (elapsed >= total) return new FlightFrame(plan.To, 14, 14, 0, 1, false, true);
        if (elapsed < Crouch)
        {
            // crouch before the jump: squash down, wings swept up
            var k = elapsed / Crouch;
            return new FlightFrame(plan.From, 14 + 110 * k, 14 + 110 * k, 0, 1 - 0.12 * Math.Sin(k * Math.PI / 2), false, false);
        }
        if (elapsed >= Crouch + plan.Seconds)
        {
            // touchdown: a squash that springs back, wings folding
            var k = (elapsed - Crouch - plan.Seconds) / Landing;
            var wing = 14 + 70 * (1 - k);
            var squash = 1 - 0.14 * Math.Sin(k * Math.PI) + 0.04 * Math.Sin(k * Math.PI * 2) * k;
            return new FlightFrame(plan.To, wing, wing, 0, squash, false, false);
        }
        var t = (elapsed - Crouch) / plan.Seconds;
        var u = Ease(t);
        var pos = plan.At(u);
        var ahead = plan.At(Math.Min(1, u + 0.02));
        var behind = plan.At(Math.Max(0, u - 0.02));
        var bank = Math.Clamp((ahead.X - behind.X) / Math.Max(1, plan.Seconds * 8), -14, 14); // lean into the direction of travel
        var flap = Math.Sin(2 * Math.PI * FlapsPerSecond * (elapsed - Crouch));
        var wings = 82 + 52 * flap;
        var stretch = 1 + 0.05 * Math.Sin(Math.Clamp(t, 0, 1) * Math.PI);
        return new FlightFrame(pos, wings, wings, bank, stretch, true, false);
    }
}
