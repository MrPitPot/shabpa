using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Shabpa.Kit;

// ---- Physics for Boofi and everything he wears. Each worn part is a small spring or pendulum that follows the body it hangs on, so a hop
// makes the hat lag and the scarf swing, a raised wing carries the watch, and the lantern swings from the hand and settles by itself.
// The loop runs only while something is still moving (it stops itself, so a still owl costs nothing). ----
public sealed partial class BidarOwl
{
    /// <summary>A damped spring that follows a target (semi-implicit Euler, stable for our frame steps).</summary>
    internal sealed class Spring
    {
        public double X, V;
        public void Step(double dt, double target, double omega, double zeta)
        {
            var a = omega * omega * (target - X) - 2 * zeta * omega * V;
            V += a * dt;
            X += V * dt;
        }
        public void Reset(double x) { X = x; V = 0; }
        public bool Calm(double target) => Math.Abs(V) < 0.04 && Math.Abs(X - target) < 0.03;
    }

    // where the right hand hangs when the wing rests (14 degrees out): the lantern's hanging point
    private const double ArmLength = 34, RestHandX = 80 + ArmLength * 0.2419, RestHandY = 58 + ArmLength * 0.9703;

    private readonly ScaleTransform _breath = new(1, 1, 50, 112);
    private readonly RotateTransform _watchFollow = new(0, 20, 58), _watchDangle = new(0, 16, 78);
    private readonly TranslateTransform _lanMove = new(), _neckMove = new();
    private readonly RotateTransform _neckSwing = new(0, 50, 70);

    // followers
    private readonly Spring _wingLo = new(), _wingRo = new(), _bodyY = new(), _hatY = new(), _hatA = new(), _glassY = new(),
        _neckX = new(), _neckY = new(), _lanPhi = new(), _watchA = new();
    private readonly Spring _headS = new();

    // the character of what is worn: (omega, zeta, gain) per part
    private (double W, double Z, double G) _hatFeel = (12, .35, .9), _glassFeel = (18, .3, .4), _neckFeel = (6, .25, 1), _lanFeel = (7.5, .14, 1), _watchFeel = (12, .35, .25);

    private bool _physOn;
    private TimeSpan _lastRender;
    private int _calmFrames;

    /// <summary>Picks how each worn part moves from what it is: floppy hats flop, crowns are heavy, scarves flow, jewellery swings short and quick.</summary>
    private void Characterize(string? hat, string? glasses, string? neck, string? watch, string? lantern)
    {
        static bool Has(string? id, params string[] parts) => id is not null && parts.Any(id.Contains);
        _hatFeel = Has(hat, "wizard", "party", "yalda", "earflap", "nowruz") ? (9, .22, 1.2)
            : Has(hat, "halo") ? (6, .18, 1.4)
            : Has(hat, "crown", "royal", "sun", "astronaut") ? (11, .55, .6)
            : Has(hat, "headset", "cap", "aviator") ? (16, .5, .45) : (12, .35, .9);
        _glassFeel = Has(glasses, "monocle") ? (11, .25, .8) : Has(glasses, "party", "flower", "star") ? (14, .28, .6) : (18, .3, .4);
        _neckFeel = Has(neck, "cape", "galacape") ? (4.5, .3, 1.4)
            : Has(neck, "scarf", "knit", "fire", "bandana", "yalda", "flags") ? (5.5, .22, 1.0)
            : Has(neck, "bowtie", "collar") ? (18, .6, .2) : (8, .28, .8); // jewellery: short and quick
        _lanFeel = Has(lantern, "oil", "moroccan", "jar", "bulb", "blue", "candle") ? (7.5, .12, 1.0)
            : Has(lantern, "star", "party", "pom", "fish", "bamboo") ? (9, .1, 1.1)
            : Has(lantern, "moon", "sun", "plasma", "phoenix") ? (5, .2, .7) : (7.5, .14, 1);
        _watchFeel = Has(watch, "fabric", "classic") ? (10, .3, .35) : Has(watch, "holo", "armillary", "crystal", "stardial") ? (7, .22, .25) : (14, .5, .15);
    }

    private double WingLTotal => Math.Max(-10, WingL + _wingLo.X);
    private double WingRTotal => Math.Max(-10, WingR + _wingRo.X);

    /// <summary>Writes every rig transform from the animated numbers and the spring states.</summary>
    private void ApplyRig()
    {
        _wingL.Angle = WingLTotal;
        _wingR.Angle = -WingRTotal;
        _head.Angle = _physOn ? _headS.X : Tilt;
        _watchFollow.Angle = WingLTotal - 14;
        _watchDangle.Angle = (_watchA.X - WingLTotal) * _watchFeel.G;
        var hx = ArmLength * (Math.Sin(WingRTotal * Math.PI / 180) - Math.Sin(14 * Math.PI / 180));
        var hy = ArmLength * (Math.Cos(WingRTotal * Math.PI / 180) - Math.Cos(14 * Math.PI / 180));
        _lanMove.X = hx; _lanMove.Y = hy;
        // the lantern is a pendulum hanging from the hand: its angle is a spring pushed by the hand's sideways acceleration
        _lanSwing.Angle = Math.Clamp(_lanPhi.X * _lanFeel.G, -40, 40);
        _hatMove.Y = HatLift + _hatY.X * _hatFeel.G;
        _hatTilt.Angle = HatTilt + _hatA.X * _hatFeel.G;
        _glassNudge.Y = _glassY.X * _glassFeel.G;
        _neckSwing.Angle = _neckX.X * _neckFeel.G;
        _neckMove.Y = _neckY.X * _neckFeel.G * 0.5;
    }

    /// <summary>Called whenever a driving number changes: make sure the spring loop is running.</summary>
    private void WakePhysics()
    {
        _calmFrames = 0;
        if (_physOn || !IsVisible || !Motion.Enabled) return;
        _physOn = true;
        _lastRender = TimeSpan.Zero;
        CompositionTarget.Rendering += OnPhysics;
    }

    private void SleepPhysics()
    {
        if (!_physOn) return;
        _physOn = false;
        CompositionTarget.Rendering -= OnPhysics;
    }

    private void OnPhysics(object? sender, EventArgs e)
    {
        var now = (e as RenderingEventArgs)?.RenderingTime ?? TimeSpan.Zero;
        var dt = _lastRender == TimeSpan.Zero ? 1 / 60.0 : Math.Clamp((now - _lastRender).TotalSeconds, 0.001, 0.05);
        if (now == _lastRender) return; // two events in one frame
        _lastRender = now;
        if (!IsVisible) { SleepPhysics(); return; }

        // the drivers, in rig units (pixels of the 100x120 rig, degrees)
        var by = Lift;                                   // body height (negative = up)
        var tilt = Tilt;
        var wl = WingL; var wr = WingR;
        var hx = ArmLength * Math.Sin(WingRTotal * Math.PI / 180);
        var hy = ArmLength * Math.Cos(WingRTotal * Math.PI / 180);
        var handX = 80 + hx + Slide;

        // the bob hangs under the hand; the springs below are the loose parts, driven by the accelerations of what carries them
        var vyRaw = (by - _prevBodyY) / dt;
        var vtRaw = (tilt - _prevTilt) / dt;
        var vwlRaw = (wl - _prevWl) / dt; var vwrRaw = (wr - _prevWr) / dt;
        _prevBodyY = by; _prevTilt = tilt; _prevWl = wl; _prevWr = wr;
        const double a = 0.35; // smooth the velocities so a 30 fps keyframe curve does not kick the springs at every key
        var vy = _vy += a * (vyRaw - _vy); var vt = _vt += a * (vtRaw - _vt);
        _vwl += a * (vwlRaw - _vwl); _vwr += a * (vwrRaw - _vwr);
        var ay = (vy - _prevVy) / dt; _prevVy = vy;
        var at = (vt - _prevVt) / dt; _prevVt = vt;
        ay = Math.Clamp(ay, -900, 900); at = Math.Clamp(at, -1500, 1500);

        var vhx = handX - _prevHandX; _prevHandX = handX;
        _vhx += a * (vhx / dt - _vhx);
        var ahx = Math.Clamp((_vhx - _prevVhx) / dt, -600, 600); _prevVhx = _vhx;

        var steps = dt > 1 / 90.0 ? 2 : 1;
        var h = dt / steps;
        for (var i = 0; i < steps; i++)
        {
            _headS.Step(h, tilt, 15, .5);                                     // the head follows with a little overshoot
            _bodyY.Step(h, by, 11, .3);
            var lagY = _bodyY.X - by;                                         // positive while the body is rising
            _wingLo.Step(h, -lagY * 1.7, 13, .35);                            // wings trail when he hops, flare when he drops
            _wingRo.Step(h, -lagY * 1.7, 13, .35);
            // a loose mass on a spring in an accelerating frame: x'' = -w^2 x - 2 z w x' - a
            _hatY.V += -ay * h * 0.35; _hatY.Step(h, 0, _hatFeel.W, _hatFeel.Z);
            _hatA.V += -at * h * 0.45; _hatA.Step(h, 0, _hatFeel.W, _hatFeel.Z);
            _glassY.V += -ay * h * 0.5; _glassY.Step(h, 0, _glassFeel.W, _glassFeel.Z);
            _neckX.V += -at * h * 0.8; _neckX.Step(h, 0, _neckFeel.W, _neckFeel.Z);
            _neckY.V += -ay * h * 0.5; _neckY.Step(h, 0, _neckFeel.W * 1.3, Math.Min(.9, _neckFeel.Z + .1));
            // pendulum: phi'' = -w^2 phi - 2 z w phi' - (a_hand / L), in degrees, L = 34 px of rope (the effective length that gives a natural swing)
            _lanPhi.V += -(ahx / 34.0) * 57.3 * h; _lanPhi.Step(h, 0, _lanFeel.W, _lanFeel.Z);
            _watchA.Step(h, WingLTotal, 14, .4);
        }
        _hatY.X = Math.Clamp(_hatY.X, -4, 4);
        _hatA.X = Math.Clamp(_hatA.X, -22, 22);
        _glassY.X = Math.Clamp(_glassY.X, -2, 2);
        _neckX.X = Math.Clamp(_neckX.X, -35, 35);
        _neckY.X = Math.Clamp(_neckY.X, -4, 4);
        _lanPhi.X = Math.Clamp(_lanPhi.X, -60, 60);

        ApplyRig();

        var calm = _headS.Calm(tilt) && _bodyY.Calm(by) && _wingLo.Calm(-(_bodyY.X - by) * 1.7) && _wingRo.Calm(-(_bodyY.X - by) * 1.7)
            && _hatY.Calm(0) && _hatA.Calm(0) && _glassY.Calm(0) && _neckX.Calm(0) && _neckY.Calm(0)
            && _lanPhi.Calm(0) && _watchA.Calm(WingLTotal) && Math.Abs(_vy) + Math.Abs(_vt) + Math.Abs(_vwl) + Math.Abs(_vwr) < 0.5;
        if (calm) { if (++_calmFrames > 20) { SnapToRest(); SleepPhysics(); } }
        else _calmFrames = 0;
    }

    private double _prevBodyY, _prevTilt, _prevWl = 14, _prevWr = 14, _prevVy, _prevVt, _prevHandX = 89, _vhx, _prevVhx, _vy, _vt, _vwl, _vwr;

    private void SnapToRest()
    {
        foreach (var s in new[] { _wingLo, _wingRo, _hatY, _hatA, _glassY, _neckX, _neckY }) s.Reset(0);
        _bodyY.Reset(Lift);
        _headS.Reset(Tilt);
        _watchA.Reset(WingLTotal);
        _lanPhi.Reset(0);
        _prevHandX = 80 + ArmLength * Math.Sin(WingRTotal * Math.PI / 180) + Slide; _vhx = _prevVhx = 0;
        _vy = _vt = _vwl = _vwr = _prevVy = _prevVt = 0;
        _prevBodyY = Lift; _prevTilt = Tilt; _prevWl = WingL; _prevWr = WingR;
        ApplyRig();
    }

    private void StartBreathing()
    {
        if (!Motion.Enabled || !IsVisible) return;
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        _breath.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 1.022, TimeSpan.FromMilliseconds(1900)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
        _breath.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0.992, TimeSpan.FromMilliseconds(1900)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
    }

    private void StopBreathing()
    {
        _breath.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _breath.BeginAnimation(ScaleTransform.ScaleXProperty, null);
    }
}
