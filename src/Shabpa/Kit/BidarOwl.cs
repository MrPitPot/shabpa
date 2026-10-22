using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Shabpa.Core;

namespace Shabpa.Kit;

/// <summary>Bidar the owl as a small vector rig (the character sheet's parts as separate groups: hat, head, eyes, beak,
/// wings, lantern; blink, talking beak, squash). Pure geometry, no bitmaps: about 3 KB of paths, drawn at any size.
/// Animation uses the render thread's clock and ends when the pose is reached, so a settled owl costs nothing;
/// the only timers are one-shot (the next blink, the nap) and run only while <see cref="IdleLife"/> is on and the owl is shown.
/// With <see cref="Motion.Enabled"/> off every change is instant.</summary>
public sealed partial class BidarOwl : Decorator
{
    // character colours from the model sheet
    private static readonly Brush Body = B("#3d3b94"), Dark = B("#2a2766"), Belly = B("#5652b8"), WingLine = B("#7d7ae0"), Face = B("#ffe2a0"),
        Orange = B("#f7a93b"), Ink = B("#141432"), Gold = B("#ffd27a"), Cheek = B("#ff8fa3"), Star = B("#d9dcff"), Brim = B("#1b1946");

    private static Brush B(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    private static Geometry G(string data)
    {
        var g = Geometry.Parse(data);
        g.Freeze();
        return g;
    }

    // ---- animatable numbers (every change redraws the transforms once; no loops of our own) --------------------------------
    public static readonly DependencyProperty WingLProperty = Num(nameof(WingL), 14), WingRProperty = Num(nameof(WingR), 14), TiltProperty = Num(nameof(Tilt), 0),
        SquashProperty = Num(nameof(Squash), 1), LiftProperty = Num(nameof(Lift), 0), BeakProperty = Num(nameof(Beak), 0), BlinkProperty = Num(nameof(Blink), 0),
        LookXProperty = Num(nameof(LookX), 0), LookYProperty = Num(nameof(LookY), 0), BlushProperty = Num(nameof(Blush), 0.45), LidsProperty = Num(nameof(Lids), 0), GlowProperty = Num(nameof(Glow), 0.8),
        HatLiftProperty = Num(nameof(HatLift), 0), HatTiltProperty = Num(nameof(HatTilt), 0), SlideProperty = Num(nameof(Slide), 0), SpinProperty = Num(nameof(Spin), 0);

    private static DependencyProperty Num(string name, double def) =>
        DependencyProperty.Register(name, typeof(double), typeof(BidarOwl), new PropertyMetadata(def, (d, _) => ((BidarOwl)d).Apply()));

    public double WingL { get => (double)GetValue(WingLProperty); set => SetValue(WingLProperty, value); }
    public double WingR { get => (double)GetValue(WingRProperty); set => SetValue(WingRProperty, value); }
    public double Tilt { get => (double)GetValue(TiltProperty); set => SetValue(TiltProperty, value); }
    public double Squash { get => (double)GetValue(SquashProperty); set => SetValue(SquashProperty, value); }
    public double Lift { get => (double)GetValue(LiftProperty); set => SetValue(LiftProperty, value); }
    public double Beak { get => (double)GetValue(BeakProperty); set => SetValue(BeakProperty, value); }
    public double Blink { get => (double)GetValue(BlinkProperty); set => SetValue(BlinkProperty, value); }
    public double LookX { get => (double)GetValue(LookXProperty); set => SetValue(LookXProperty, value); }
    public double LookY { get => (double)GetValue(LookYProperty); set => SetValue(LookYProperty, value); }
    public double Blush { get => (double)GetValue(BlushProperty); set => SetValue(BlushProperty, value); }
    public double Lids { get => (double)GetValue(LidsProperty); set => SetValue(LidsProperty, value); }
    public double Glow { get => (double)GetValue(GlowProperty); set => SetValue(GlowProperty, value); }
    /// <summary>The hat lifts (negative = up, in rig units) and tilts (degrees) for the hat-tip and the fidget.</summary>
    public double HatLift { get => (double)GetValue(HatLiftProperty); set => SetValue(HatLiftProperty, value); }
    public double HatTilt { get => (double)GetValue(HatTiltProperty); set => SetValue(HatTiltProperty, value); }
    /// <summary>Sideways shift of the body inside the owl (rig units): the peek from the edge.</summary>
    public double Slide { get => (double)GetValue(SlideProperty); set => SetValue(SlideProperty, value); }
    /// <summary>A turn of the whole body in degrees (the easter-egg spin).</summary>
    public double Spin { get => (double)GetValue(SpinProperty); set => SetValue(SpinProperty, value); }

    // ---- parts ------------------------------------------------------------------------------------------------------------------
    private readonly Canvas _art = new() { Width = 100, Height = 120, Background = Brushes.Transparent };
    private readonly ScaleTransform _squash = new(1, 1, 50, 112);
    private readonly TranslateTransform _lift = new();
    private readonly RotateTransform _spin = new(0, 50, 70);
    private readonly TranslateTransform _hatMove = new();
    private readonly RotateTransform _hatTilt = new(0, 50, 40);
    private readonly Dictionary<string, Path> _props = [];
    private readonly RotateTransform _wingL = new(14, 0, 0), _wingR = new(-14, 0, 0), _head = new(0, 50, 60);
    private readonly TranslateTransform _lowerBeak = new();
    private readonly Path _mouth, _lantern;
    private readonly Ellipse _glow, _cheekL, _cheekR, _shadow;
    private readonly Eye _eyeL, _eyeR;
    private readonly Canvas _fx = new() { IsHitTestVisible = false, Opacity = 0 };
    private readonly TextBlock _fxText = new() { FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Star, Visibility = Visibility.Collapsed };
    private readonly Path _fxHeart, _fxStar;
    private readonly TranslateTransform _fxMove = new();

    private readonly DispatcherTimer _blinkTimer = new(), _napTimer = new();
    private readonly BidarMoodLogic _logic = new();
    private BidarMood _mood = BidarMood.Idle;
    private readonly Random _rng = new();

    /// <summary>True for the popup owl: it blinks now and then and dozes off when left alone. The small dock owl keeps this off (no timers at all).</summary>
    public bool IdleLife { get; set; }

    /// <summary>Raised when the person clicks the owl, after it has reacted.</summary>
    public event Action? Clicked;

    /// <summary>Raised at the very start of a click, before the owl reacts (a flight lands first).</summary>
    public event Action? PreClick;

    public BidarMood Mood => _mood;

    private sealed class Eye
    {
        public readonly Canvas Root = new();
        public readonly Ellipse Disc, Pupil, Shine;
        public readonly Path Arc, Closed, Heart, Spiral;
        public readonly ScaleTransform PupilScale = new();
        public readonly TranslateTransform Look = new();
        public readonly ScaleTransform DiscScale = new();

        public Eye(double cx)
        {
            Disc = new Ellipse { Width = 25, Height = 25, Fill = Face, RenderTransform = DiscScale };
            Canvas.SetLeft(Disc, cx - 12.5);
            Canvas.SetTop(Disc, 43.5);
            DiscScale.CenterX = 12.5;
            DiscScale.CenterY = 12.5;
            var pupil = new Canvas { Width = 0, Height = 0 };
            Pupil = new Ellipse { Width = 11, Height = 11, Fill = Ink, Margin = new Thickness(-5.5) };
            Shine = new Ellipse { Width = 3.6, Height = 3.6, Fill = Brushes.White, Margin = new Thickness(-3.2, -3.4, 0, 0) };
            pupil.Children.Add(Pupil);
            pupil.Children.Add(Shine);
            Canvas.SetLeft(pupil, cx);
            Canvas.SetTop(pupil, 57);
            pupil.RenderTransform = new TransformGroup { Children = { PupilScale, Look } };
            Arc = Stroke($"M{cx - 6},58.5 Q{cx},50.5 {cx + 6},58.5");
            Closed = Stroke($"M{cx - 6},56 Q{cx},62 {cx + 6},56");
            Heart = new Path { Data = G("M0,3 C-6,-2 -5,-7 -2.2,-7 C-1,-7 0,-6 0,-5 C0,-6 1,-7 2.2,-7 C5,-7 6,-2 0,3 Z"), Fill = B("#ff5d86"), Visibility = Visibility.Collapsed, RenderTransform = new ScaleTransform(1.7, 1.7) };
            Canvas.SetLeft(Heart, cx);
            Canvas.SetTop(Heart, 58);
            Spiral = new Path
            {
                Data = G($"M{cx},57 m-1,0 a1,1 0 1 1 2,0 a2.4,2.4 0 1 1 -4.8,0 a3.8,3.8 0 1 1 7.6,0 a5.2,5.2 0 1 1 -10.4,0"),
                Stroke = Ink, StrokeThickness = 1.5, Visibility = Visibility.Collapsed, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            };
            Root.Children.Add(Disc);
            Root.Children.Add(pupil);
            Root.Children.Add(Arc);
            Root.Children.Add(Closed);
            Root.Children.Add(Heart);
            Root.Children.Add(Spiral);
            PupilGroup = pupil;
        }

        public Canvas PupilGroup { get; }

        private static Path Stroke(string d) => new()
        {
            Data = G(d), Stroke = Ink, StrokeThickness = 2.3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Visibility = Visibility.Collapsed,
        };
    }

    public BidarOwl()
    {
        SnapsToDevicePixels = false;
        _shadow = new Ellipse { Width = 64, Height = 8, Fill = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)) };
        Canvas.SetLeft(_shadow, 18);
        Canvas.SetTop(_shadow, 108);
        _art.Children.Add(_shadow);

        var root = new Canvas { Width = 100, Height = 120, RenderTransform = new TransformGroup { Children = { _squash, _lift, _spin } } };

        // feet
        foreach (var x in new[] { 33.0, 55.0 })
        {
            var foot = new Ellipse { Width = 12, Height = 6, Fill = Orange };
            Canvas.SetLeft(foot, x);
            Canvas.SetTop(foot, 107);
            root.Children.Add(foot);
        }

        // body and belly with the little chevrons
        var body = new Ellipse { Width = 70, Height = 72, Fill = Body };
        Canvas.SetLeft(body, 15);
        Canvas.SetTop(body, 38);
        var belly = new Ellipse { Width = 44, Height = 44, Fill = Belly };
        Canvas.SetLeft(belly, 28);
        Canvas.SetTop(belly, 62);
        root.Children.Add(body);
        root.Children.Add(belly);
        foreach (var dy in new[] { 0.0, 6 })
            root.Children.Add(new Path { Data = G($"M43,{82 + dy} L50,{88 + dy} L57,{82 + dy}"), Stroke = WingLine, StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Opacity = dy == 0 ? 0.9 : 0.6 });

        // wings hang from the shoulders; the angle turns them outward (guide: -45 on the chest ... 160 above the head)
        var wingShape = G("M0,0 C-9,8 -11,26 -3,38 C5,30 10,12 6,0 Z");
        var wl = new Path { Data = wingShape, Fill = Dark, Stroke = WingLine, StrokeThickness = 1.2, StrokeLineJoin = PenLineJoin.Round, RenderTransform = _wingL };
        Canvas.SetLeft(wl, 20);
        Canvas.SetTop(wl, 58);
        var wr = new Path { Data = wingShape, Fill = Dark, Stroke = WingLine, StrokeThickness = 1.2, StrokeLineJoin = PenLineJoin.Round, RenderTransform = new TransformGroup { Children = { new ScaleTransform(-1, 1), _wingR } } };
        Canvas.SetLeft(wr, 80);
        Canvas.SetTop(wr, 58);
        root.Children.Add(wl);
        root.Children.Add(wr);

        // lantern in the right wing: it always hangs straight down
        _glow = new Ellipse
        {
            Width = 34, Height = 34, IsHitTestVisible = false,
            Fill = new RadialGradientBrush(Color.FromArgb(150, 255, 210, 122), Color.FromArgb(0, 255, 210, 122)),
        };
        Canvas.SetLeft(_glow, 69);
        Canvas.SetTop(_glow, 78);
        _lantern = new Path { Data = G("M86,90 L86,93 M82,93 L90,93 L90,96 L82,96 Z M82.6,96 L89.4,96 L89.4,104 L82.6,104 Z M82,104 L90,104 L90,106 L82,106 Z"), Fill = Gold, Stroke = Orange, StrokeThickness = 0.9, StrokeLineJoin = PenLineJoin.Round };
        root.Children.Add(_glow);
        root.Children.Add(_lantern);

        // head: hat, two face discs, eyes, beak, cheeks
        var head = new Canvas { Width = 100, Height = 120, RenderTransform = _head };
        // the hat is its own group so it can lift and tilt (hat-tip, fidget)
        var hat = new Canvas { Width = 100, Height = 120, RenderTransform = new TransformGroup { Children = { _hatTilt, _hatMove } } };
        hat.Children.Add(new Path { Data = G("M22,42 C20,10 80,10 78,42 Z"), Fill = Dark });
        var moon = new Path { Data = new CombinedGeometry(GeometryCombineMode.Exclude, new EllipseGeometry(new Point(44, 28), 6.2, 6.2), new EllipseGeometry(new Point(47.2, 26), 5.6, 5.6)), Fill = Gold };
        moon.Data.Freeze();
        hat.Children.Add(moon);
        hat.Children.Add(new Border { Width = 66, Height = 7, CornerRadius = new CornerRadius(3.5), Background = Brim, Margin = new Thickness(17, 38, 0, 0) });
        head.Children.Add(hat);
        _cheekL = new Ellipse { Width = 9, Height = 6, Fill = Cheek };
        _cheekR = new Ellipse { Width = 9, Height = 6, Fill = Cheek };
        Canvas.SetLeft(_cheekL, 22);
        Canvas.SetTop(_cheekL, 61);
        Canvas.SetLeft(_cheekR, 69);
        Canvas.SetTop(_cheekR, 61);
        _eyeL = new Eye(37);
        _eyeR = new Eye(63);
        head.Children.Add(_cheekL);
        head.Children.Add(_cheekR);
        head.Children.Add(_eyeL.Root);
        head.Children.Add(_eyeR.Root);
        _mouth = new Path { Data = G("M46.5,62.5 L53.5,62.5 L50,72 Z"), Fill = Ink, Opacity = 0 };
        head.Children.Add(_mouth);
        head.Children.Add(new Path { Data = G("M46,61.5 L54,61.5 L50,67 Z"), Fill = Orange, StrokeLineJoin = PenLineJoin.Round });
        head.Children.Add(new Path { Data = G("M47.5,67 L52.5,67 L50,70.4 Z"), Fill = Orange, RenderTransform = _lowerBeak });
        root.Children.Add(head);
        _art.Children.Add(root);

        // one floating mark at a time (a heart, stars, "z", "?", "!")
        _fxHeart = new Path { Data = G("M0,4 C-8,-2 -6,-9 -2.6,-9 C-1,-9 0,-8 0,-6.5 C0,-8 1,-9 2.6,-9 C6,-9 8,-2 0,4 Z"), Fill = B("#ff5d86"), Visibility = Visibility.Collapsed };
        _fxStar = new Path { Data = G("M0,-6 L1.6,-1.6 L6,0 L1.6,1.6 L0,6 L-1.6,1.6 L-6,0 L-1.6,-1.6 Z M10,-9 L11,-6.6 L13.4,-6 L11,-5 L10,-2.6 L9,-5 L6.6,-6 L9,-6.6 Z"), Fill = Star, Visibility = Visibility.Collapsed };
        _fx.RenderTransform = _fxMove;
        BuildProps();
        _fx.Children.Add(_fxHeart);
        _fx.Children.Add(_fxStar);
        _fx.Children.Add(_fxText);
        Canvas.SetLeft(_fx, 72);
        Canvas.SetTop(_fx, 18);
        _art.Children.Add(_fx);

        Child = new Viewbox { Stretch = Stretch.Uniform, Child = _art };
        FlowDirection = FlowDirection.LeftToRight; // the character never mirrors (the lantern is always in the right wing)
        Cursor = Cursors.Hand;
        Focusable = false;
        UseLayoutRounding = false;

        _blinkTimer.Tick += (_, _) => { _blinkTimer.Stop(); DoBlink(); ArmBlink(); };
        _napTimer.Tick += (_, _) => { _napTimer.Stop(); if (IdleLife && IsVisible) SetMood(BidarMood.Sleepy); };
        IsVisibleChanged += (_, e) => { if ((bool)e.NewValue) Wake(false); else StopTimers(); };
        Unloaded += (_, _) => StopTimers();
        MouseLeftButtonDown += (_, e) => { e.Handled = true; React(); };
        Apply();
        ApplyPose(BidarPoses.For(BidarMood.Idle));
    }

    // ---- poses and reactions ----------------------------------------------------------------------------------------------------

    /// <summary>Shows a mood: the numbers glide there (instantly with motion off) and the eyes and mark switch at once.</summary>
    public void SetMood(BidarMood mood)
    {
        _mood = mood;
        ApplyPose(BidarPoses.For(mood));
    }

    /// <summary>The reaction to a click: a mood from the cycle plus a squash-and-bounce; a nap ends first.</summary>
    public void React()
    {
        PreClick?.Invoke();
        var mood = _logic.Click(DateTime.Now);
        SetMood(mood);
        Bounce(mood == BidarMood.Dizzy);
        Wake(true);
        Clicked?.Invoke();
        // go back to the calm face a moment later (not while the host has set a mood of its own since)
        var back = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(mood == BidarMood.Dizzy ? 2200 : 1800) };
        back.Tick += (_, _) =>
        {
            back.Stop();
            if (_mood == mood && IsVisible) SetMood(BidarMood.Idle);
        };
        back.Start();
    }

    /// <summary>Records activity (typing, a click elsewhere) so the owl does not doze off.</summary>
    public void Touch()
    {
        _logic.Touch(DateTime.Now);
        if (_mood == BidarMood.Sleepy) SetMood(BidarMood.Idle);
        ArmNap();
    }

    private void Wake(bool touched)
    {
        if (touched) _logic.Touch(DateTime.Now);
        else if (_logic.LastTouch == DateTime.MinValue) _logic.Touch(DateTime.Now);
        if (!IdleLife) return;
        ArmBlink();
        ArmNap();
    }

    private void ArmBlink()
    {
        if (!IdleLife || !IsVisible || !Motion.Enabled) return;
        _blinkTimer.Interval = TimeSpan.FromSeconds(2.8 + _rng.NextDouble() * 3);
        _blinkTimer.Start();
    }

    private void ArmNap()
    {
        if (!IdleLife || !IsVisible) return;
        _napTimer.Stop();
        _napTimer.Interval = BidarMoodLogic.SleepAfter;
        _napTimer.Start();
    }

    private void StopTimers()
    {
        _blinkTimer.Stop();
        _napTimer.Stop();
    }

    /// <summary>One blink (a short close and open); a no-op with motion off or while the eyes are closed anyway.</summary>
    public void DoBlink()
    {
        if (!Motion.Enabled || _mood is BidarMood.Sleepy or BidarMood.Dizzy) return;
        BeginAnimation(BlinkProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(80)) { AutoReverse = true, FillBehavior = FillBehavior.Stop });
    }

    /// <summary>Squash down, spring up and settle. Motion off: nothing moves.</summary>
    public void Bounce(bool big = false)
    {
        if (!Motion.Enabled) return;
        var s = Squash;
        var squash = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        squash.KeyFrames.Add(new EasingDoubleKeyFrame(s * 0.88, KeyTime.FromPercent(0.18), new QuadraticEase()));
        squash.KeyFrames.Add(new EasingDoubleKeyFrame(s * 1.1, KeyTime.FromPercent(0.42), new QuadraticEase()));
        squash.KeyFrames.Add(new EasingDoubleKeyFrame(s, KeyTime.FromPercent(1), new ElasticEase { Oscillations = 2, Springiness = 6, EasingMode = EasingMode.EaseOut }));
        squash.Duration = TimeSpan.FromMilliseconds(big ? 900 : 620);
        BeginAnimation(SquashProperty, squash);
        var lift = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop, Duration = TimeSpan.FromMilliseconds(big ? 900 : 620) };
        lift.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0.18)));
        lift.KeyFrames.Add(new EasingDoubleKeyFrame(big ? -13 : -9, KeyTime.FromPercent(0.45), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        lift.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), new BounceEase { Bounces = 1, Bounciness = 3, EasingMode = EasingMode.EaseOut }));
        BeginAnimation(LiftProperty, lift);
        if (big)
        {
            var tilt = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop, Duration = TimeSpan.FromMilliseconds(900) };
            foreach (var (t, v) in new[] { (0.15, -12.0), (0.35, 12.0), (0.55, -8.0), (0.75, 6.0), (1.0, Tilt) })
                tilt.KeyFrames.Add(new EasingDoubleKeyFrame(v, KeyTime.FromPercent(t), new SineEase()));
            BeginAnimation(TiltProperty, tilt);
        }
    }

    /// <summary>Talking: the beak opens and closes a few times; the caller says when (a result line appears).</summary>
    public void Talk(int syllables = 4)
    {
        if (!Motion.Enabled) return;
        var a = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(110)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(Math.Clamp(syllables, 1, 8)), FillBehavior = FillBehavior.Stop };
        BeginAnimation(BeakProperty, a);
    }

    /// <summary>Pupils look toward a point (offset from the owl's centre in pixels); (0,0) centres them.</summary>
    public void LookAt(double dx, double dy)
    {
        if (_mood is BidarMood.Sleepy or BidarMood.Dizzy) return;
        var (x, y) = BidarMoodLogic.Look(dx, dy, 3.2);
        if (Math.Abs(x - LookX) < 0.05 && Math.Abs(y - LookY) < 0.05) return;
        LookX = x;
        LookY = y;
    }

    // ---- drawing ----------------------------------------------------------------------------------------------------------------

    private void ApplyPose(BidarPose p)
    {
        var motion = Motion.Enabled;
        void To(DependencyProperty dp, double value, double ms = 320)
        {
            var from = (double)GetValue(dp);
            if (!motion || Math.Abs(from - value) < 1e-6)
            {
                BeginAnimation(dp, null);
                SetValue(dp, value);
                return;
            }
            SetValue(dp, value);
            BeginAnimation(dp, new DoubleAnimation(from, value, TimeSpan.FromMilliseconds(ms)) { EasingFunction = SpringEase.I, FillBehavior = FillBehavior.Stop });
        }
        To(WingLProperty, p.WingL);
        To(WingRProperty, p.WingR);
        To(TiltProperty, p.Tilt);
        To(SquashProperty, p.Squash);
        To(BeakProperty, p.Beak, 200);
        To(BlushProperty, p.Blush, 400);
        To(LidsProperty, p.Lids, 300);
        if (p.Eyes is BidarEyes.Closed or BidarEyes.Arc or BidarEyes.Hearts or BidarEyes.Spiral) { LookX = 0; LookY = 0; }

        void Show(Eye e, bool right)
        {
            var wink = p.WinkRight && right;
            var kind = wink ? BidarEyes.Arc : p.Eyes;
            e.PupilGroup.Visibility = kind is BidarEyes.Open or BidarEyes.Wide ? Visibility.Visible : Visibility.Collapsed;
            e.Arc.Visibility = kind == BidarEyes.Arc ? Visibility.Visible : Visibility.Collapsed;
            e.Closed.Visibility = kind == BidarEyes.Closed ? Visibility.Visible : Visibility.Collapsed;
            e.Heart.Visibility = kind == BidarEyes.Hearts ? Visibility.Visible : Visibility.Collapsed;
            e.Spiral.Visibility = kind == BidarEyes.Spiral ? Visibility.Visible : Visibility.Collapsed;
            var wide = kind == BidarEyes.Wide;
            e.DiscScale.ScaleX = e.DiscScale.ScaleY = wide ? 1.12 : 1;
            e.PupilScale.ScaleX = e.PupilScale.ScaleY = wide ? 0.78 : 1;
        }
        Show(_eyeL, false);
        Show(_eyeR, true);
        ShowFx(p.Fx);
        Apply();
    }

    private void ShowFx(string? fx)
    {
        _fxHeart.Visibility = fx == "heart" ? Visibility.Visible : Visibility.Collapsed;
        _fxStar.Visibility = fx is "star" or "stars" ? Visibility.Visible : Visibility.Collapsed;
        _fxText.Visibility = fx is "z" or "?" or "!" ? Visibility.Visible : Visibility.Collapsed;
        foreach (var prop in _props) prop.Value.Visibility = prop.Key == fx ? Visibility.Visible : Visibility.Collapsed;
        if (fx is "z" or "?" or "!") _fxText.Text = fx;
        _fx.BeginAnimation(OpacityProperty, null);
        _fxMove.BeginAnimation(TranslateTransform.YProperty, null);
        if (fx is null) { _fx.Opacity = 0; return; }
        _fx.Opacity = 1;
        if (!Motion.Enabled || fx == "z" || _props.ContainsKey(fx)) return; // "z" stays while asleep, a prop while the pose is held
        _fxMove.Y = 0;
        _fxMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, -4, TimeSpan.FromMilliseconds(700)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
        _fx.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(500)) { BeginTime = TimeSpan.FromMilliseconds(1300), FillBehavior = FillBehavior.HoldEnd });
    }

    private void Apply()
    {
        if (_wingL is null || _eyeR is null) return; // a property default is being set while the parts are still being built
        _wingL.Angle = WingL;
        _wingR.Angle = -WingR;
        _head.Angle = Tilt;
        _squash.ScaleX = 1 + (1 - Squash) * 0.5;
        _squash.ScaleY = Squash;
        _lift.Y = Lift;
        _lift.X = Slide;
        _spin.Angle = Spin;
        _hatMove.Y = HatLift;
        _hatTilt.Angle = HatTilt;
        _shadow.Opacity = _airborne ? 0 : 0.9 - Math.Max(0, -Lift) * 0.02;
        _lowerBeak.Y = Beak * 2.6;
        _mouth.Opacity = Beak > 0.05 ? 0.9 : 0;
        _cheekL.Opacity = _cheekR.Opacity = Math.Clamp(Blush, 0, 1) * 0.75;
        _glow.Opacity = Math.Clamp(Glow, 0, 1);
        foreach (var e in new[] { _eyeL, _eyeR })
        {
            e.Look.X = LookX;
            e.Look.Y = LookY;
            e.PupilScale.ScaleY = Math.Max(0.06, (1 - Blink) * (1 - 0.55 * Lids)) * (e.PupilScale.ScaleX);
        }
    }
}
