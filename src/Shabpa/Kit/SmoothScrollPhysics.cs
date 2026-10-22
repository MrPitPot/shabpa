namespace Shabpa.Kit;

/// <summary>The maths of the smooth wheel scroll, with no WPF in it so it can be unit tested: a critically damped spring pulls the
/// offset to a target that every wheel notch moves. It eases in, never overshoots, and slows down gradually once the wheel stops.</summary>
public static class SmoothScrollPhysics
{
    /// <summary>Roughly how long (seconds) the offset takes to cover most of the distance to the target.</summary>
    public const double SmoothTime = 0.22;

    /// <summary>Pixels per wheel notch (120 delta units); one notch is ~3 lines in Windows.</summary>
    public const double PixelsPerNotch = 96;

    /// <summary>Below this distance and speed the animation is finished and the offset snaps to the target.</summary>
    public const double RestDistance = 0.4, RestSpeed = 8;

    /// <summary>A step longer than this (a stalled UI thread, a debugger) is cut so the offset does not jump.</summary>
    public const double MaxStep = 0.05;

    public readonly record struct State(double Position, double Velocity);

    /// <summary>The exact solution of a critically damped spring over <paramref name="dt"/> seconds (stable for any dt).</summary>
    public static State Step(State s, double target, double dt, double smoothTime = SmoothTime)
    {
        if (dt <= 0) return s;
        dt = Math.Min(dt, MaxStep);
        var w = 2.0 / Math.Max(smoothTime, 0.01);
        var x = s.Position - target;
        var e = Math.Exp(-w * dt);
        var temp = (s.Velocity + w * x) * dt;
        var velocity = (s.Velocity - w * temp) * e;
        var position = target + (x + temp) * e;
        if (Math.Abs(position - target) < RestDistance && Math.Abs(velocity) < RestSpeed) return new State(target, 0);
        return new State(position, velocity);
    }

    public static bool AtRest(State s, double target) => s.Position == target && s.Velocity == 0;

    /// <summary>The new target after a wheel delta (positive delta = wheel away from the user = towards the start), kept inside the range.</summary>
    public static double AddWheel(double target, int delta, double max)
        => Math.Clamp(target - delta / 120.0 * PixelsPerNotch, 0, Math.Max(0, max));

    /// <summary>A precision touchpad (and any high-resolution wheel) reports many small deltas that already carry the system's own
    /// inertia; those are applied as they are instead of being smoothed a second time.</summary>
    public static bool IsPrecise(int delta) => delta % 120 != 0 || Math.Abs(delta) < 120;

    /// <summary>Whether a ScrollViewer should take the wheel in this direction or leave it to its parent: it can still move, or it
    /// is still gliding (so a long flick does not spill into the parent the moment the inner list hits its end).</summary>
    public static bool ShouldHandle(double target, double max, int delta, bool animating)
    {
        if (max <= 0) return false;
        var canMove = delta > 0 ? target > 0 : target < max;
        return canMove || animating;
    }
}
