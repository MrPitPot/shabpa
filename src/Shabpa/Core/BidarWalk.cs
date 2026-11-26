namespace Shabpa.Core;

/// <summary>A waddle along the bottom of the page from one resting place to another on the same row.</summary>
public sealed record WalkPlan(Pt From, Pt To, double Seconds)
{
    /// <summary>+1 walking right, -1 walking left.</summary>
    public int Direction => To.X >= From.X ? 1 : -1;
}

public static class WalkPlanner
{
    /// <summary>Pixels per second at full pace (a 50 px owl takes about two and a half steps a second).</summary>
    public const double Speed = 46;
    public const double MinDistance = 70, MaxDistance = 380;

    /// <summary>Plans a walk, or returns null when the two places are not on the same row, too close, too far, or when anything
    /// the person reads or presses lies anywhere along the strip he would cross (he never walks over content; he flies instead).</summary>
    public static WalkPlan? Plan(Pt from, Pt to, double w, double h, Box area, IReadOnlyList<Box> noGo)
    {
        if (Math.Abs(from.Y - to.Y) > 0.5) return null;
        var dist = Math.Abs(to.X - from.X);
        if (dist < MinDistance || dist > MaxDistance) return null;
        var strip = new Box(Math.Min(from.X, to.X), from.Y, dist + w, h);
        if (!area.Contains(strip)) return null;
        var padded = strip.Inflate(Perches.Pad);
        foreach (var n in noGo)
            if (padded.Intersects(n)) return null;
        return new WalkPlan(from, to with { Y = from.Y }, WalkMotion.Start + dist / Speed + WalkMotion.Stop);
    }

    /// <summary>Spots on the same row as <paramref name="current"/> he could walk to, nearest first is not wanted (he would only
    /// shuffle), so the caller shuffles them; this just keeps the reachable ones.</summary>
    public static IEnumerable<WalkPlan> Options(Pt current, IReadOnlyList<Pt> valid, double w, double h, Box area, IReadOnlyList<Box> noGo)
    {
        foreach (var p in valid)
            if (Plan(current, p, w, h, area, noGo) is { } plan) yield return plan;
    }
}

/// <summary>One frame of a walk: where the owl is, which foot is lifted and how the body sways. Pure maths of the elapsed time.</summary>
public readonly record struct WalkFrame(Pt Pos, double StepL, double StepR, double Bob, double Sway, double Look, bool Done);

public static class WalkMotion
{
    /// <summary>A short glance in the walking direction before the first step, and a settle after the last.</summary>
    public const double Start = 0.25, Stop = 0.3;
    /// <summary>Steps per second (one foot each), the foot lift (rig units) and the hip sway (degrees).</summary>
    public const double StepsPerSecond = 2.4, Lift = 3.2, SwayDegrees = 6;

    public static double Total(WalkPlan plan) => plan.Seconds;

    public static WalkFrame Frame(WalkPlan plan, double elapsed)
    {
        var dir = plan.Direction;
        if (elapsed >= plan.Seconds) return new WalkFrame(plan.To, 0, 0, 0, 0, 0, true);
        if (elapsed < Start)
        {
            var k = elapsed / Start; // eyes turn toward where he is going
            return new WalkFrame(plan.From, 0, 0, 0, 0, dir * k, false);
        }
        var moving = plan.Seconds - Start - Stop;
        if (elapsed >= Start + moving)
        {
            var k = (elapsed - Start - moving) / Stop; // feet together, eyes back to the middle
            return new WalkFrame(plan.To, 0, 0, 0, 0, dir * (1 - k), false);
        }
        var t = (elapsed - Start) / moving;
        // speeds up over the first fifth and slows over the last (quadratic ends that meet the steady middle at the same pace)
        const double a = 3.125; // 1 / (2 b (1 - b)) for b = 0.2
        var u = t < 0.2 ? a * t * t : t > 0.8 ? 1 - a * (1 - t) * (1 - t) : 0.125 + (t - 0.2) * 1.25;
        var pos = new Pt(plan.From.X + (plan.To.X - plan.From.X) * u, plan.From.Y);
        // whole steps only: the phase is stretched so the last step lands exactly as he arrives
        var steps = Math.Max(2, Math.Round(moving * StepsPerSecond));
        var ph = Math.PI * steps * t;
        var s = Math.Sin(ph);
        return new WalkFrame(pos, Lift * Math.Max(0, s), Lift * Math.Max(0, -s), -1.6 * Math.Abs(s), SwayDegrees * s, dir, false);
    }
}
