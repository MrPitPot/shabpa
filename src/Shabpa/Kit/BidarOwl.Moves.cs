using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Shabpa.Core;

namespace Shabpa.Kit;

// ---- Boofi's ten small acts. Each one is a set of smooth curves for the main motion (body, head, wings); everything that is worn
// then reacts by itself through the spring physics (BidarOwl.Physics.cs). Curves start from the rest values, never from where an
// earlier act happens to be, and only one act runs at a time, so quick repeated requests can no longer pile up. ----
public sealed partial class BidarOwl
{
    private DateTime _actUntil = DateTime.MinValue;
    private int _pendingAct = -1;
    private readonly DispatcherTimer _pendingTimer = new(), _glanceTimer = new();

    private static double Sm(double x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }
    /// <summary>0 → 1 from a to b, held until c, back to 0 at d (smooth).</summary>
    private static double Win(double t, double a, double b, double c, double d) => Sm((t - a) / (b - a)) - Sm((t - c) / (d - c));
    /// <summary>Rises with a little overshoot (a spring), so a wing lifts, passes the mark and settles.</summary>
    private static double Spr(double x) { x = Math.Clamp(x, 0, 1); var k = x - 1; return 1 + 1.6 * k * k * k + 0.6 * k * k; }
    private static double SWin(double t, double a, double b, double c, double d) => Spr((t - a) / (b - a)) - Sm((t - c) / (d - c));
    private static double Decay(double t, double hz, double damp) => Math.Sin(2 * Math.PI * hz * t) * Math.Exp(-damp * t);
    private static double Hop(double t, double start, double length)
    {
        var u = (t - start) / length;
        return u is < 0 or > 1 ? 0 : 4 * u * (1 - u);
    }
    private static double Env(double t, double total) => Sm(t / .4) * Sm((total - t) / .6);

    private static DoubleAnimationUsingKeyFrames Curve(double seconds, Func<double, double> f)
    {
        var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(seconds), FillBehavior = FillBehavior.Stop };
        Timeline.SetDesiredFrameRate(a, ActFps);
        var n = (int)Math.Ceiling(seconds * 30);
        for (var i = 0; i <= n; i++)
        {
            var t = Math.Min(seconds, i / 30.0);
            a.KeyFrames.Add(new LinearDoubleKeyFrame(f(t), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t))));
        }
        return a;
    }

    private void Go(DependencyProperty dp, double seconds, Func<double, double> f) => BeginAnimation(dp, Curve(seconds, f));

    private double Rest(DependencyProperty dp) => (double)GetAnimationBaseValue(dp);

    private void ArmPending()
    {
        _pendingTimer.Stop();
        _pendingTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(80, (_actUntil - DateTime.UtcNow).TotalMilliseconds + 60));
        _pendingTimer.Start();
    }

    private void InitMoves()
    {
        _pendingTimer.Tick += (_, _) =>
        {
            _pendingTimer.Stop();
            if (DateTime.UtcNow < _actUntil) { ArmPending(); return; }
            var act = _pendingAct;
            _pendingAct = -1;
            if (act >= 0) IdleAct(act);
        };
        _glanceTimer.Tick += (_, _) => { Glance(); _glanceTimer.Interval = TimeSpan.FromSeconds(1.6 + ActRng.NextDouble() * 2.8); };
    }

    private void StartGlances()
    {
        if (!Motion.Enabled || !IsVisible) return;
        _glanceTimer.Interval = TimeSpan.FromSeconds(1.2 + ActRng.NextDouble() * 2);
        _glanceTimer.Start();
    }

    private void StopGlances() { _glanceTimer.Stop(); _pendingTimer.Stop(); }

    /// <summary>The eyes dart to a side for a moment (a living gaze between the bigger acts).</summary>
    private void Glance()
    {
        if (!Motion.Enabled || _mood is not BidarMood.Idle || DateTime.UtcNow < _actUntil) return;
        var x = (ActRng.NextDouble() * 2 - 1) * 2.6;
        var y = (ActRng.NextDouble() * 2 - 1) * 1.4;
        var len = .7 + ActRng.NextDouble() * .8;
        Go(LookXProperty, len, t => x * Win(t, .0, .12, len - .3, len));
        Go(LookYProperty, len, t => y * Win(t, .0, .12, len - .3, len));
    }

    /// <summary>One of ten small acts (never the same twice in a row; skipped while flying, asleep, in a reaction, or while another act runs).</summary>
    public void IdleAct(int? which = null)
    {
        if (!Motion.Enabled || _airborne || _mood is not BidarMood.Idle || DateTime.UtcNow < _actUntil) return;
        int n;
        do { n = which ?? ActRng.Next(10); } while (which is null && n == _lastAct);
        _lastAct = n;
        double wl = Rest(WingLProperty), wr = Rest(WingRProperty), tl = Rest(TiltProperty), sq = Rest(SquashProperty);
        void Len(double s) => _actUntil = DateTime.UtcNow.AddSeconds(s);
        switch (n)
        {
            case 0: // look left, then right, the eyes leading the head
                Len(3.0);
                Go(TiltProperty, 3.0, t => tl - 7 * Win(t, .25, .8, 1.15, 1.7) + 7 * Win(t, 1.7, 2.2, 2.4, 2.9));
                Go(LookXProperty, 3.0, t => -3.2 * Win(t, .05, .4, 1.2, 1.55) + 3.2 * Win(t, 1.5, 1.85, 2.4, 2.8));
                break;
            case 1: // two hops: crouch, spring, land, settle (the wings, hat and scarf follow by physics)
                Len(1.8);
                Go(LiftProperty, 1.8, t => -11 * Hop(t, .28, .5) - 8 * Hop(t, .95, .45));
                Go(SquashProperty, 1.8, t => sq - .09 * Win(t, .0, .24, .24, .3) + .06 * Win(t, .26, .34, .5, .66) - .07 * Win(t, .74, .84, .88, 1.0) + .05 * Win(t, .9, 1.0, 1.15, 1.35) - .06 * Win(t, 1.35, 1.45, 1.45, 1.65));
                Go(WingLProperty, 1.8, t => wl + 22 * SWin(t, .24, .5, .6, .9) + 22 * SWin(t, .9, 1.15, 1.2, 1.5));
                Go(WingRProperty, 1.8, t => wr + 22 * SWin(t, .26, .52, .62, .92) + 22 * SWin(t, .92, 1.17, 1.22, 1.52));
                break;
            case 2: // shake the feathers out
                Len(1.4);
                Go(TiltProperty, 1.4, t => tl + 6 * Decay(t, 4.5, 3.0) * Sm(t / .1));
                Go(WingLProperty, 1.4, t => wl + 16 * Math.Max(0, Decay(t, 6, 2.6)) * Sm(t / .1));
                Go(WingRProperty, 1.4, t => wr + 16 * Math.Max(0, Decay(t, 6, 2.6)) * Sm(t / .1));
                Go(SquashProperty, 1.4, t => sq + .03 * Decay(t, 8, 3.5));
                break;
            case 3: // big stretch with a yawn
                Len(3.0);
                Go(WingLProperty, 3.0, t => wl + 100 * SWin(t, .15, 1.0, 1.8, 2.5));
                Go(WingRProperty, 3.0, t => wr + 100 * SWin(t, .2, 1.05, 1.8, 2.5));
                Go(SquashProperty, 3.0, t => sq + .06 * Win(t, .2, 1.0, 1.7, 2.5));
                Go(BeakProperty, 3.0, t => Win(t, .8, 1.1, 1.5, 1.85));
                Go(LidsProperty, 3.0, t => .5 * Win(t, .8, 1.1, 1.5, 1.95));
                break;
            case 4: // tip the hat with the left wing
                Len(2.4);
                Go(WingLProperty, 2.4, t => wl + 118 * SWin(t, .1, .75, 1.6, 2.15));
                Go(HatTiltProperty, 2.4, t => -6 * Win(t, .6, .95, 1.4, 1.85));
                Go(TiltProperty, 2.4, t => tl + 5 * Win(t, .2, .8, 1.5, 2.1));
                Go(BlushProperty, 2.4, t => .45 + .4 * Win(t, .7, 1.1, 1.5, 2.1));
                break;
            case 5: // lift the lantern a little and let it swing (the pendulum does the rest)
                Len(3.4);
                Go(WingRProperty, 3.4, t => wr + 34 * Win(t, .0, .3, .38, .75));
                Go(LookXProperty, 3.4, t => 2.4 * Win(t, .2, .5, 1.8, 2.4));
                Go(GlowProperty, 3.4, t => .8 + .2 * Win(t, .2, .6, 1.2, 2.8));
                break;
            case 6: // look at the wrist
                Len(3.0);
                Go(WingLProperty, 3.0, t => wl + 62 * SWin(t, .1, .7, 2.1, 2.7));
                Go(TiltProperty, 3.0, t => tl - 7 * Win(t, .25, .8, 2.0, 2.6));
                Go(LookXProperty, 3.0, t => -3 * Win(t, .2, .55, 2.0, 2.4));
                Go(LookYProperty, 3.0, t => 2.6 * Win(t, .2, .55, 2.0, 2.4));
                Dispatcher.BeginInvoke(DoBlink, DispatcherPriority.Background);
                break;
            case 7: // push the glasses up with the right wing
                Len(2.4);
                Go(WingRProperty, 2.4, t => wr + 78 * SWin(t, .1, .65, 1.6, 2.1));
                Go(TiltProperty, 2.4, t => tl + 4 * Win(t, .3, .8, 1.6, 2.1));
                break;
            case 8: // a little dance
                Len(3.2);
                Go(TiltProperty, 3.2, t => tl + 7 * Math.Sin(2 * Math.PI * 1.6 * t) * Env(t, 3.2));
                Go(LiftProperty, 3.2, t => -3 * Math.Abs(Math.Sin(2 * Math.PI * 1.6 * t)) * Env(t, 3.2));
                Go(SquashProperty, 3.2, t => sq + .04 * Math.Cos(2 * Math.PI * 3.2 * t) * Env(t, 3.2));
                Go(WingLProperty, 3.2, t => wl + 36 * Math.Max(0, Math.Sin(2 * Math.PI * 1.6 * t)) * Env(t, 3.2));
                Go(WingRProperty, 3.2, t => wr + 36 * Math.Max(0, -Math.Sin(2 * Math.PI * 1.6 * t)) * Env(t, 3.2));
                Go(BlushProperty, 3.2, t => .45 + .4 * Env(t, 3.2));
                break;
            default: // drowsy, a yawn, then a small shake awake
                Len(4.2);
                Go(SquashProperty, 4.2, t => sq - .05 * Win(t, .2, 1.1, 2.6, 3.3));
                Go(TiltProperty, 4.2, t => tl + 9 * Win(t, .2, 1.2, 2.4, 3.1) + 4 * Decay(Math.Max(0, t - 3.1), 4.5, 4) * Sm((t - 3.1) / .06));
                Go(LidsProperty, 4.2, t => .8 * Win(t, .3, 1.1, 2.6, 3.3));
                Go(BeakProperty, 4.2, t => Win(t, 1.4, 1.7, 2.1, 2.45));
                break;
        }
    }
}
