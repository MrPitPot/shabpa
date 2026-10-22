using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Shabpa.Core;

namespace Shabpa.Kit;

// ---- Bidar's small repertoire: props beside the head and short animated acts. Every act is a keyframe animation on the render
// thread that ends on its own and falls back to the held pose (FillBehavior.Stop), so nothing here runs once it is over. ----
public sealed partial class BidarOwl
{
    private bool _airborne;

    /// <summary>True while flying: the little ground shadow is hidden.</summary>
    public bool Airborne
    {
        get => _airborne;
        set { _airborne = value; Apply(); }
    }

    private static Brush Hex(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    private void BuildProps()
    {
        void Add(string name, string data, Brush? fill, Brush? stroke, double thickness = 1.8)
        {
            var p = new Path
            {
                Data = G(data), Fill = fill, Stroke = stroke, StrokeThickness = thickness, Visibility = Visibility.Collapsed, IsHitTestVisible = false,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
            };
            _props[name] = p;
            _fx.Children.Add(p);
        }
        var light = Hex("#d9dcff");
        Add("magnifier", "M3,-3 A5.5,5.5 0 1 1 -8,-3 A5.5,5.5 0 1 1 3,-3 M1,1 L8,8", null, light, 2);
        Add("clock", "M7,0 A7,7 0 1 1 -7,0 A7,7 0 1 1 7,0 M0,-4 L0,0 L3,2", Hex("#2a2766"), Hex("#ffd27a"), 1.6);
        Add("plane", "M-9,-1 L9,-8 L5,8 L-1,2 Z M-1,2 L9,-8", Hex("#7cc4ff"), Hex("#d9f0ff"), 0.9);
        Add("power", "M-4.5,-4 A6.5,6.5 0 1 0 4.5,-4 M0,-9 L0,0", null, Hex("#ff8a7a"), 2.2);
        Add("down", "M0,-8 L0,5 M-5,0 L0,6 L5,0", null, Hex("#5fe3a1"), 2.4);
        Add("signal", "M-6,6 L-6,3 M-2,6 L-2,0 M2,6 L2,-3 M6,6 L6,-6", null, Hex("#ff9d6b"), 2.4);
        Add("shield", "M0,-8 L7,-5 C7,2 4,6 0,8 C-4,6 -7,2 -7,-5 Z", Hex("#6cc3ff"), Hex("#d9f0ff"), 1);
        Add("sweat", "M0,-7 C4,-1 5,2 0,5 C-5,2 -4,-1 0,-7 Z", Hex("#8fd8ff"), null);
        Add("confetti", "M-8,-4 L-5,-6 L-3,-3 Z M4,-8 L7,-7 L6,-4 Z M8,0 L10,2 L7,3 Z M-6,3 L-3,2 L-4,6 Z M0,-1 L2,0 L1,2 Z", Hex("#ffd27a"), Hex("#ff8fa3"), 0.6);
    }

    private static DoubleAnimationUsingKeyFrames Keys(double ms, params (double At, double Value)[] frames)
    {
        var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(ms), FillBehavior = FillBehavior.Stop };
        foreach (var (at, value) in frames) a.KeyFrames.Add(new EasingDoubleKeyFrame(value, KeyTime.FromPercent(at), new SineEase { EasingMode = EasingMode.EaseInOut }));
        return a;
    }

    /// <summary>One slow breath cycle or two: the body swells a hair.</summary>
    public void Breathe()
    {
        if (!Motion.Enabled) return;
        var s = Squash;
        BeginAnimation(SquashProperty, Keys(3600, (0.25, s * 1.035), (0.5, s), (0.75, s * 1.035), (1, s)));
    }

    /// <summary>Two quick blinks in a row.</summary>
    public void DoubleBlink()
    {
        if (!Motion.Enabled || _mood is BidarMood.Sleepy or BidarMood.Dizzy) return;
        BeginAnimation(BlinkProperty, Keys(420, (0.2, 1), (0.4, 0), (0.7, 1), (1, 0)));
    }

    /// <summary>The lantern's flame gutters and recovers.</summary>
    public void FlickerLantern()
    {
        if (!Motion.Enabled) return;
        var g = Glow;
        BeginAnimation(GlowProperty, Keys(1500, (0.15, 0.35), (0.3, 0.95), (0.5, 0.5), (0.7, 1), (1, g)));
    }

    /// <summary>Glances left, then right, then back to the middle.</summary>
    public void LookAround()
    {
        if (!Motion.Enabled || _mood is BidarMood.Sleepy or BidarMood.Dizzy) return;
        BeginAnimation(LookXProperty, Keys(2600, (0.2, -3.2), (0.5, -3.2), (0.7, 3.2), (0.9, 3.2), (1, LookX)));
        BeginAnimation(LookYProperty, Keys(2600, (0.2, -0.8), (0.5, -0.8), (0.7, 1), (0.9, 1), (1, LookY)));
    }

    /// <summary>The right wing waves hello.</summary>
    public void WaveWing()
    {
        if (!Motion.Enabled) return;
        var w = WingR;
        BeginAnimation(WingRProperty, Keys(1800, (0.18, 125), (0.32, 90), (0.46, 125), (0.6, 90), (0.74, 125), (1, w)));
        BeginAnimation(TiltProperty, Keys(1800, (0.2, 5), (0.8, 5), (1, Tilt)));
    }

    /// <summary>A hand to the hat: lifts, tips and straightens it.</summary>
    public void FidgetHat()
    {
        if (!Motion.Enabled) return;
        BeginAnimation(WingRProperty, Keys(2200, (0.2, 150), (0.75, 150), (1, WingR)));
        BeginAnimation(HatLiftProperty, Keys(2200, (0.25, -5), (0.4, -5), (0.55, -2), (0.7, -5), (1, HatLift)));
        BeginAnimation(HatTiltProperty, Keys(2200, (0.25, 0), (0.4, 12), (0.55, -8), (0.7, 10), (0.85, 0), (1, HatTilt)));
    }

    /// <summary>Leans out of the corner toward <paramref name="direction"/> (-1 left, +1 right) to look around, then slips back.</summary>
    public void PeekOut(int direction)
    {
        if (!Motion.Enabled) return;
        var d = direction < 0 ? -1 : 1;
        BeginAnimation(SlideProperty, Keys(2600, (0.25, 14 * d), (0.8, 14 * d), (1, Slide)));
        BeginAnimation(TiltProperty, Keys(2600, (0.25, 14 * d), (0.8, 14 * d), (1, Tilt)));
        BeginAnimation(LookXProperty, Keys(2600, (0.3, 3.2 * -d), (0.8, 3.2 * -d), (1, LookX)));
    }

    /// <summary>A nervous shiver.</summary>
    public void Shiver()
    {
        if (!Motion.Enabled) return;
        BeginAnimation(SlideProperty, Keys(700, (0.14, -2.5), (0.3, 2.5), (0.46, -2.5), (0.62, 2.5), (0.8, -1.5), (1, Slide)));
    }

    /// <summary>Leans the head toward a point (curiosity) and lets the pupils follow it.</summary>
    public void Curious(double dx, double dy)
    {
        if (!Motion.Enabled || _mood is BidarMood.Sleepy or BidarMood.Dizzy) return;
        var (x, y) = BidarMoodLogic.Look(dx, dy, 3.2);
        BeginAnimation(TiltProperty, Keys(1800, (0.2, dx < 0 ? -11 : 11), (0.8, dx < 0 ? -11 : 11), (1, Tilt)));
        BeginAnimation(LookXProperty, Keys(1800, (0.2, x), (0.8, x), (1, LookX)));
        BeginAnimation(LookYProperty, Keys(1800, (0.2, y), (0.8, y), (1, LookY)));
    }

    /// <summary>The easter egg: a full spin with the hat flying up, then a bow of the hat.</summary>
    public void SpinAround()
    {
        if (!Motion.Enabled) return;
        BeginAnimation(SpinProperty, new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(900)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }, FillBehavior = FillBehavior.Stop });
        BeginAnimation(HatLiftProperty, Keys(1500, (0.3, -9), (0.6, -9), (1, HatLift)));
        BeginAnimation(HatTiltProperty, Keys(1500, (0.3, 20), (0.6, -14), (1, HatTilt)));
        Bounce(true);
    }

    /// <summary>Drops every running animation and goes back to the calm held pose. Used before a flight and when something interrupts.</summary>
    public void Settle()
    {
        foreach (var dp in new[] { WingLProperty, WingRProperty, TiltProperty, SquashProperty, LiftProperty, BeakProperty, BlinkProperty, LookXProperty, LookYProperty, GlowProperty, HatLiftProperty, HatTiltProperty, SlideProperty, SpinProperty })
            BeginAnimation(dp, null);
        Airborne = false;
        LookX = LookY = 0;
        Glow = 0.8;
        HatLift = HatTilt = Slide = Spin = 0;
        SetMood(BidarMood.Idle);
    }

    /// <summary>Applies one flight frame straight to the rig (no animation objects; the caller drives it from the render clock).</summary>
    public void ApplyFlight(FlightFrame f)
    {
        void Set(DependencyProperty dp, double v)
        {
            BeginAnimation(dp, null);
            SetValue(dp, v);
        }
        Set(WingLProperty, f.WingL);
        Set(WingRProperty, f.WingR);
        Set(TiltProperty, f.Tilt);
        Set(SquashProperty, f.Squash);
        Airborne = f.Airborne;
    }
}
