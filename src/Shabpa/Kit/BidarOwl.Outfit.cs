using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Shabpa.Core;
using Shabpa.Pages;

namespace Shabpa.Kit;

// ---- The little Boofi wears the store's parts. The drawn rig keeps everything it had (eyes, hearts, wings, flights); the part pictures
// are laid over it on the places that move (head, left wing, right hand) and the fur colour repaints the body. On top of that he
// breathes and, every few seconds, does one of ten small acts. Everything is a render-thread animation that ends by itself. ----
public sealed partial class BidarOwl
{
    // frame of the part pictures (240 units, -20..220) laid on the 100x120 rig: the eyes line up (37/63 vs 78/122) and the feet match
    private const double Sc = 0.63, Ox = -13, Oy = -6.6;
    private const double Side = 240 * Sc, Left = Ox - 20 * Sc, Top = Oy - 20 * Sc;

    private SolidColorBrush _cBody = new(Color.FromRgb(0x3d, 0x3b, 0x94)), _cBelly = new(Color.FromRgb(0x56, 0x52, 0xb8)),
        _cWing = new(Color.FromRgb(0x2a, 0x27, 0x66)), _cEdge = new(Color.FromRgb(0x7d, 0x7a, 0xe0));
    private Canvas? _vHat;
    private readonly Canvas _gBack = new() { Width = 100, Height = 120, IsHitTestVisible = false }, _gNeck = new() { IsHitTestVisible = false },
        _gWatch = new() { IsHitTestVisible = false }, _gLantern = new() { IsHitTestVisible = false },
        _gGlasses = new() { Width = 100, Height = 120, IsHitTestVisible = false }, _gHat = new() { Width = 100, Height = 120, IsHitTestVisible = false };
    private readonly RotateTransform _lanSwing = new(0, 86, 90);
    private readonly RotateTransform _watchFollow = new(0, 20, 58), _lanFollow = new(0, 80, 58);
    private readonly TranslateTransform _glassNudge = new();
    private readonly DispatcherTimer _idleTimer = new();
    private bool _useOutfit, _hasHat, _hasLantern, _hasWatch, _hasGlasses;
    private int _lastAct = -1;
    private static readonly Random ActRng = new();

    private static readonly Dictionary<string, (string Body, string Belly, string Wing, string Edge)> Fur = new()
    {
        ["fu-mist"] = ("#6b7396", "#a3acce", "#4f5679", "#9aa3c4"), ["fu-sand"] = ("#b88c5a", "#e6c594", "#8c6538", "#dba96d"),
        ["fu-emerald"] = ("#34c98a", "#7ff0c0", "#17805c", "#3ddc97"), ["fu-ruby"] = ("#e8455d", "#ff9aa9", "#a8203a", "#ff6b81"),
        ["fu-turquoise"] = ("#35d6e6", "#9ff5ff", "#1590b8", "#4cc9f0"), ["fu-galaxy"] = ("#2a1f66", "#ff7ad9", "#24195a", "#9b7cff"),
        ["fu-aurora"] = ("#0f5c6b", "#5ff2c4", "#0e4a56", "#5ff2c4"), ["fu-lava"] = ("#2a1428", "#ff6b3d", "#240f1c", "#ff6b3d"),
        ["fu-gold"] = ("#e8a82c", "#fff0b0", "#c98a1a", "#fff3c4"), ["fu-phoenix"] = ("#ff8a3d", "#ffe08a", "#e8455d", "#ffd27a"),
        ["fu-moonlit"] = ("#b8c0f0", "#ffffff", "#a8b0f0", "#ffffff"), ["fu-yalda"] = ("#c8243c", "#ff9aa9", "#a01a34", "#ffd27a"),
        ["fu-nowruz"] = ("#7de8b0", "#fff8ea", "#3fc08a", "#ffb3c7"), ["fu-party"] = ("#8a4fe0", "#ffd0f0", "#5a2fb0", "#ffd27a"),
    };

    private static Color Hx(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    /// <summary>Called while the rig is built: lays the (empty) part layers into it at the right depth.</summary>
    private void AttachOutfit(Canvas root, Canvas head, Canvas hat)
    {
        root.Children.Insert(0, _gBack);
        // the left wing's group follows the wing's own turn (pivot = shoulder), so a watch rides on the wrist
        _gWatch.RenderTransform = _watchFollow;        // the watch turns with the left wing (shoulder at 20,58)
        _gLantern.RenderTransform = new TransformGroup { Children = { _lanSwing, _lanFollow } }; // and the lantern with the right one (80,58)
        var firstWing = root.Children.OfType<Path>().First(p => p.RenderTransform == _wingL);
        root.Children.Insert(root.Children.IndexOf(firstWing), _gNeck);
        root.Children.Insert(root.Children.IndexOf(_glow), _gWatch);
        root.Children.Insert(root.Children.IndexOf(_lantern) + 1, _gLantern);
        _gGlasses.RenderTransform = _glassNudge;
        head.Children.Add(_gGlasses);
        _gHat.RenderTransform = new TransformGroup { Children = { _hatTilt, _hatMove } };
        head.Children.Add(_gHat);
    }

    /// <summary>True for the Boofi that wears the person's outfit (set once, before it is shown).</summary>
    public bool UseOutfit
    {
        get => _useOutfit;
        set
        {
            if (_useOutfit == value) return;
            _useOutfit = value;
            if (value)
            {
                BoofiOutfit.Changed += OnOutfit;
                Loaded += (_, _) => { RefreshOutfit(); StartOutfitLife(); };
                Unloaded += (_, _) => { StopOutfitLife(); BoofiOutfit.Changed -= OnOutfit; };
                RefreshOutfit();
            }
            else
            {
                BoofiOutfit.Changed -= OnOutfit;
                StopOutfitLife();
                Dress(new Dictionary<string, string>());
            }
        }
    }

    /// <summary>A Boofi that is not tied to what the person wears (the wardrobe stage): it is dressed by <see cref="ShowLook"/> and keeps itself alive.</summary>
    public bool Standalone { get; set; }

    public void ShowLook(IReadOnlyDictionary<string, string> look) => Dress(look);

    /// <summary>The act that shows off a slot: tip the hat, push up the glasses, check the watch, swing the lantern, ruffle the scarf, stretch the new fur.</summary>
    public void Showcase(string slot) => IdleAct(slot switch { "hat" => 4, "glasses" => 7, "watch" => 6, "lantern" => 5, "neck" => 2, _ => 3 });

    private void OnOutfit() => Dispatcher.BeginInvoke(RefreshOutfit);

    public void RefreshOutfit()
    {
        if (!_useOutfit) return;
        Dress(BoofiOutfit.Current);
    }

    private Image? Pic(string file)
    {
        if (BoofiArt.Load(file, BoofiArt.Bucket(Side / 100 * (double.IsNaN(Width) ? 60 : Width) * Math.Clamp(VisualTreeHelper.GetDpi(this).DpiScaleX, 1, 2))) is not { } picture) return null;
        var image = new Image { Source = picture, Width = Side, Height = Side, Stretch = Stretch.Fill, IsHitTestVisible = false };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        Canvas.SetLeft(image, Left);
        Canvas.SetTop(image, Top);
        return image;
    }

    private void Dress(IReadOnlyDictionary<string, string> worn)
    {
        foreach (var g in new[] { _gBack, _gNeck, _gWatch, _gLantern, _gGlasses, _gHat }) g.Children.Clear();
        BoofiPart? Get(string slot) => worn.TryGetValue(slot, out var id) ? BoofiCatalog.Find(id) : null;
        _hasHat = Get("hat") is not null; _hasLantern = Get("lantern") is not null; _hasWatch = Get("watch") is not null; _hasGlasses = Get("glasses") is not null;

        // fur: repaint the drawn body; the glow behind (epic and above) is a picture
        var fur = Get("fur");
        var c = fur is not null && Fur.TryGetValue(fur.Id, out var col) ? col : ("#3d3b94", "#5652b8", "#2a2766", "#7d7ae0");
        _cBody.Color = Hx(c.Item1); _cBelly.Color = Hx(c.Item2); _cWing.Color = Hx(c.Item3); _cEdge.Color = Hx(c.Item4);
        if (fur is { FurIndex: >= 0 } && BoofiCatalog.FurWithBehind.Contains(fur.FurIndex) && Pic($"k_{fur.FurIndex}.png") is { } glow) _gBack.Children.Add(glow);

        // the drawn cap and candle give way to a worn hat or lantern
        if (_vHat is not null) _vHat.Visibility = _hasHat ? Visibility.Collapsed : Visibility.Visible;
        _lantern.Visibility = _hasLantern ? Visibility.Collapsed : Visibility.Visible;
        _glow.Visibility = _hasLantern ? Visibility.Collapsed : Visibility.Visible;

        foreach (var slot in new[] { "neck", "glasses", "hat", "watch", "lantern" })
        {
            if (Get(slot) is not { } part) continue;
            if (part.HasBehind && Pic($"{part.Id}_b.png") is { } back) _gBack.Children.Add(back);
            if (!part.HasFront || Pic($"{part.Id}.png") is not { } front) continue;
            switch (slot)
            {
                case "neck": _gNeck.Children.Add(front); break;
                case "glasses": _gGlasses.Children.Add(front); break;
                case "hat": _gHat.Children.Add(front); break;
                case "watch":
                    _gWatch.Children.Add(front);
                    break;
                default:
                    _gLantern.Children.Add(front);
                    break;
            }
        }
    }

    // ---- life --------------------------------------------------------------------------------------------------------------------

    private void StartOutfitLife()
    {
        if (!(_useOutfit || Standalone) || !IsVisible || !Motion.Enabled) return;
        ArmIdle();
    }

    private void StopOutfitLife() => _idleTimer.Stop();

    private void ArmIdle()
    {
        if (!(_useOutfit || Standalone) || !IsVisible || !Motion.Enabled) return;
        _idleTimer.Interval = TimeSpan.FromSeconds(2.5 + ActRng.NextDouble() * 3.5);
        _idleTimer.Start();
    }

    // ---- motion: curves sampled at 30 fps so every move eases in and out, overshoots and settles like something alive ----------------

    private static double Sm(double x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }
    /// <summary>0 → 1 from a to b, held until c, back to 0 at d (smooth).</summary>
    private static double Win(double t, double a, double b, double c, double d) => Sm((t - a) / (b - a)) - Sm((t - c) / (d - c));
    /// <summary>Rises with a little overshoot (a spring), so a wing lifts, passes the mark and settles.</summary>
    private static double Spr(double x) { x = Math.Clamp(x, 0, 1); var k = x - 1; return 1 + 2.4 * k * k * k + 1.4 * k * k; }
    private static double SWin(double t, double a, double b, double c, double d) => Spr((t - a) / (b - a)) - Sm((t - c) / (d - c));
    private static double Decay(double t, double hz, double damp) => Math.Sin(2 * Math.PI * hz * t) * Math.Exp(-damp * t);

    private static DoubleAnimationUsingKeyFrames Curve(double seconds, Func<double, double> f)
    {
        var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(seconds), FillBehavior = FillBehavior.Stop };
        var n = (int)Math.Ceiling(seconds * 30);
        for (var i = 0; i <= n; i++)
        {
            var t = Math.Min(seconds, i / 30.0);
            a.KeyFrames.Add(new LinearDoubleKeyFrame(f(t), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t))));
        }
        return a;
    }

    private void Go(DependencyProperty dp, double seconds, Func<double, double> f) => BeginAnimation(dp, Curve(seconds, f));

    /// <summary>One of ten small acts (never the same twice in a row; skipped while flying, asleep or in the middle of a reaction).</summary>
    public void IdleAct(int? which = null)
    {
        if (!Motion.Enabled || _airborne || _mood is not BidarMood.Idle) return;
        int n;
        do { n = which ?? ActRng.Next(10); } while (which is null && n == _lastAct);
        _lastAct = n;
        double wl = WingL, wr = WingR, tl = Tilt, sq = Squash;
        switch (n)
        {
            case 0: // look left, then right, eyes leading the head
                Go(TiltProperty, 3.0, t => tl - 9 * Win(t, .25, .75, 1.15, 1.6) + 9 * Win(t, 1.6, 2.05, 2.35, 2.9));
                Go(LookXProperty, 3.0, t => -3.2 * Win(t, .05, .4, 1.15, 1.5) + 3.2 * Win(t, 1.45, 1.8, 2.35, 2.8));
                break;
            case 1: // two hops: crouch, spring, wings up, land, settle
                Go(LiftProperty, 1.7, t => -11 * Hop(t, .25, .5) - 8 * Hop(t, .85, .45));
                Go(SquashProperty, 1.7, t => sq - .09 * Win(t, .0, .22, .22, .26) + .06 * Win(t, .22, .3, .45, .6) - .08 * Win(t, .72, .82, .85, .95) + .05 * Win(t, .85, .95, 1.1, 1.3) - .05 * Win(t, 1.3, 1.4, 1.4, 1.6));
                Go(WingLProperty, 1.7, t => wl + 46 * SWin(t, .22, .5, .6, .9) + 46 * SWin(t, .85, 1.1, 1.2, 1.5));
                Go(WingRProperty, 1.7, t => wr + 46 * SWin(t, .24, .52, .62, .92) + 46 * SWin(t, .87, 1.12, 1.22, 1.52));
                break;
            case 2: // shake the feathers out
                Go(TiltProperty, 1.2, t => tl + 9 * Decay(t, 5, 3.2) * Sm(t / .08));
                Go(WingLProperty, 1.2, t => wl + 22 * Math.Max(0, Decay(t, 7, 2.5)) * Sm(t / .08));
                Go(WingRProperty, 1.2, t => wr + 22 * Math.Max(0, Decay(t, 7, 2.5)) * Sm(t / .08));
                Go(SquashProperty, 1.2, t => sq + .035 * Decay(t, 10, 3.5));
                break;
            case 3: // big stretch with a yawn
                Go(WingLProperty, 2.8, t => wl + 108 * SWin(t, .15, .9, 1.7, 2.4));
                Go(WingRProperty, 2.8, t => wr + 108 * SWin(t, .2, .95, 1.7, 2.4));
                Go(SquashProperty, 2.8, t => sq + .07 * Win(t, .2, .9, 1.6, 2.4) - .02 * Win(t, 2.2, 2.4, 2.4, 2.7));
                Go(BeakProperty, 2.8, t => Win(t, .7, 1.0, 1.5, 1.8));
                Go(LidsProperty, 2.8, t => .55 * Win(t, .7, 1.0, 1.5, 1.9));
                break;
            case 4: // tip the hat
                Go(WingLProperty, 2.2, t => wl + 130 * SWin(t, .1, .6, 1.5, 2.0));
                Go(HatTiltProperty, 2.2, t => -10 * Win(t, .45, .8, 1.3, 1.7));
                Go(HatLiftProperty, 2.2, t => -2.5 * Win(t, .45, .8, 1.3, 1.7));
                Go(TiltProperty, 2.2, t => tl + 6 * Win(t, .2, .7, 1.4, 2.0));
                Go(BlushProperty, 2.2, t => .45 + .45 * Win(t, .6, 1.0, 1.4, 2.0));
                break;
            case 5: // nudge the lantern and let it swing
                Go(WingRProperty, 3.2, t => wr + 26 * Win(t, .0, .25, .3, .7) - 6 * Win(t, .7, .9, .9, 1.2));
                _lanSwing.BeginAnimation(RotateTransform.AngleProperty, Curve(3.2, t => 15 * Decay(t, 1.15, .75) * Sm(t / .25)));
                Go(GlowProperty, 3.2, t => .8 + .2 * Win(t, .2, .5, .8, 2.2));
                Go(LookXProperty, 3.2, t => 2.4 * Win(t, .2, .5, 1.6, 2.2));
                break;
            case 6: // look at the wrist
                Go(WingLProperty, 3.0, t => wl + 66 * SWin(t, .1, .65, 2.1, 2.7));
                Go(TiltProperty, 3.0, t => tl - 8 * Win(t, .2, .7, 2.0, 2.6));
                Go(LookXProperty, 3.0, t => -3 * Win(t, .2, .55, 2.0, 2.4));
                Go(LookYProperty, 3.0, t => 2.6 * Win(t, .2, .55, 2.0, 2.4));
                Dispatcher.BeginInvoke(DoBlink, DispatcherPriority.Background);
                break;
            case 7: // push the glasses up with a wing
                Go(WingRProperty, 2.2, t => wr + 86 * SWin(t, .1, .55, 1.5, 2.0));
                _glassNudge.BeginAnimation(TranslateTransform.YProperty, Curve(2.2, t => -1.6 * Win(t, .55, .7, .7, .85) - 1.2 * Win(t, 1.0, 1.1, 1.1, 1.25)));
                Go(TiltProperty, 2.2, t => tl + 5 * Win(t, .3, .7, 1.5, 2.0));
                break;
            case 8: // a little dance
                Go(TiltProperty, 3.0, t => tl + 10 * Math.Sin(2 * Math.PI * 1.7 * t) * Env(t, 3.0));
                Go(LiftProperty, 3.0, t => -3.5 * Math.Abs(Math.Sin(2 * Math.PI * 1.7 * t)) * Env(t, 3.0));
                Go(SquashProperty, 3.0, t => sq + .045 * Math.Cos(2 * Math.PI * 3.4 * t) * Env(t, 3.0));
                Go(WingLProperty, 3.0, t => wl + 52 * Math.Max(0, Math.Sin(2 * Math.PI * 1.7 * t)) * Env(t, 3.0));
                Go(WingRProperty, 3.0, t => wr + 52 * Math.Max(0, -Math.Sin(2 * Math.PI * 1.7 * t)) * Env(t, 3.0));
                Go(BlushProperty, 3.0, t => .45 + .45 * Env(t, 3.0));
                break;
            default: // drowsy, a yawn, then a quick shake awake
                Go(SquashProperty, 4.0, t => sq - .06 * Win(t, .2, 1.0, 2.6, 3.2));
                Go(TiltProperty, 4.0, t => tl + 11 * Win(t, .2, 1.1, 2.4, 3.0) + 7 * Decay(Math.Max(0, t - 3.0), 5, 4) * Sm((t - 3.0) / .05));
                Go(LidsProperty, 4.0, t => .85 * Win(t, .3, 1.0, 2.6, 3.2));
                Go(BeakProperty, 4.0, t => Win(t, 1.3, 1.6, 2.1, 2.4));
                break;
        }
    }

    /// <summary>One hop: a parabola starting at <paramref name="start"/> seconds and lasting <paramref name="length"/>.</summary>
    private static double Hop(double t, double start, double length)
    {
        var u = (t - start) / length;
        return u is < 0 or > 1 ? 0 : 4 * u * (1 - u);
    }

    /// <summary>Fades a repeating motion in and out over the act's length.</summary>
    private static double Env(double t, double total) => Sm(t / .35) * Sm((total - t) / .5);
}
