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
    private readonly ScaleTransform _watchPulse = new(1, 1, 2, 11);
    private readonly Canvas _wHost = new() { IsHitTestVisible = false };
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
        _gWatch.RenderTransform = new TransformGroup { Children = { _wingL, new TranslateTransform(20, 58) } };
        _wHost.RenderTransform = _watchPulse;
        _gWatch.Children.Add(_wHost);
        _gLantern.RenderTransform = _lanSwing;
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
        if (BoofiArt.Load(file, 224) is not { } picture) return null;
        var image = new Image { Source = picture, Width = Side, Height = Side, Stretch = Stretch.Fill, IsHitTestVisible = false };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        Canvas.SetLeft(image, Left);
        Canvas.SetTop(image, Top);
        return image;
    }

    private void Dress(IReadOnlyDictionary<string, string> worn)
    {
        foreach (var g in new[] { _gBack, _gNeck, _wHost, _gLantern, _gGlasses, _gHat }) g.Children.Clear();
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
                    // the group sits on the shoulder (20,58), so the picture is shifted back by that
                    Canvas.SetLeft(front, Left - 20); Canvas.SetTop(front, Top - 58);
                    _wHost.Children.Add(front);
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

    private static DoubleAnimationUsingKeyFrames K(double ms, params (double At, double V)[] frames) => Keys(ms, frames);

    /// <summary>One of ten small acts (never the same twice in a row; skipped while flying, asleep or in the middle of a reaction).</summary>
    public void IdleAct(int? which = null)
    {
        if (!Motion.Enabled || _airborne || _mood is not BidarMood.Idle) return;
        int n;
        do { n = which ?? ActRng.Next(10); } while (which is null && n == _lastAct);
        _lastAct = n;
        switch (n)
        {
            case 0: // look around, head turning each way
                BeginAnimation(LookXProperty, K(2800, (.2, -3.2), (.45, -3.2), (.7, 3.2), (.9, 3.2), (1, 0)));
                BeginAnimation(TiltProperty, K(2800, (.2, -9), (.45, -9), (.7, 9), (.9, 9), (1, 0)));
                break;
            case 1: // two hops with a flap
                BeginAnimation(LiftProperty, K(1200, (.18, -10), (.36, 0), (.54, -10), (.72, 0), (1, 0)));
                BeginAnimation(SquashProperty, K(1200, (.08, .9), (.18, 1.06), (.36, .9), (.46, 1.06), (.7, 1), (1, 1)));
                BeginAnimation(WingLProperty, K(1200, (.18, 60), (.36, 14), (.54, 60), (.72, 14), (1, 14)));
                BeginAnimation(WingRProperty, K(1200, (.18, 60), (.36, 14), (.54, 60), (.72, 14), (1, 14)));
                break;
            case 2: // ruffle the feathers
                BeginAnimation(TiltProperty, K(800, (.15, 7), (.3, -7), (.45, 6), (.6, -6), (.8, 3), (1, 0)));
                BeginAnimation(WingLProperty, K(800, (.15, 40), (.3, 14), (.45, 40), (.6, 14), (1, 14)));
                BeginAnimation(WingRProperty, K(800, (.15, 40), (.3, 14), (.45, 40), (.6, 14), (1, 14)));
                break;
            case 3: // stretch both wings up
                BeginAnimation(WingLProperty, K(2200, (.3, 120), (.7, 120), (1, 14)));
                BeginAnimation(WingRProperty, K(2200, (.3, 120), (.7, 120), (1, 14)));
                BeginAnimation(SquashProperty, K(2200, (.3, 1.07), (.7, 1.07), (1, 1)));
                BeginAnimation(BeakProperty, K(2200, (.3, 0), (.45, .7), (.7, .7), (.85, 0), (1, 0)));
                break;
            case 4: // tip the hat
                BeginAnimation(WingLProperty, K(1700, (.25, 150), (.7, 150), (1, 14)));
                BeginAnimation(HatLiftProperty, K(1700, (.3, -8), (.7, -8), (1, 0)));
                BeginAnimation(HatTiltProperty, K(1700, (.3, -16), (.7, -16), (1, 0)));
                BeginAnimation(TiltProperty, K(1700, (.3, 6), (.7, 6), (1, 0)));
                break;
            case 5: // swing the lantern
                _lanSwing.BeginAnimation(RotateTransform.AngleProperty, K(2800, (.12, 16), (.3, -14), (.5, 10), (.7, -6), (.85, 3), (1, 0)));
                BeginAnimation(WingRProperty, K(2800, (.12, 30), (.3, 8), (.5, 24), (.7, 12), (1, 14)));
                BeginAnimation(GlowProperty, K(2800, (.3, 1), (.6, .5), (1, .8)));
                break;
            case 6: // look at the wrist
                BeginAnimation(WingLProperty, K(2300, (.25, 85), (.8, 85), (1, 14)));
                BeginAnimation(TiltProperty, K(2300, (.25, -8), (.8, -8), (1, 0)));
                BeginAnimation(LookXProperty, K(2300, (.25, -3), (.8, -3), (1, 0)));
                BeginAnimation(LookYProperty, K(2300, (.25, 2.5), (.8, 2.5), (1, 0)));
                _watchPulse.BeginAnimation(ScaleTransform.ScaleXProperty, K(2300, (.35, 1.25), (.7, 1.25), (1, 1)));
                _watchPulse.BeginAnimation(ScaleTransform.ScaleYProperty, K(2300, (.35, 1.25), (.7, 1.25), (1, 1)));
                break;
            case 7: // push the glasses up with a wing
                BeginAnimation(WingRProperty, K(1500, (.25, 95), (.6, 95), (1, 14)));
                _glassNudge.BeginAnimation(TranslateTransform.YProperty, K(1500, (.35, -2.5), (.5, 0), (.65, -2), (1, 0)));
                BeginAnimation(TiltProperty, K(1500, (.3, 5), (.7, 5), (1, 0)));
                break;
            case 8: // a little dance
                BeginAnimation(TiltProperty, K(2400, (.125, 10), (.25, -10), (.375, 10), (.5, -10), (.625, 10), (.75, -10), (1, 0)));
                BeginAnimation(SquashProperty, K(2400, (.125, .93), (.25, 1.05), (.375, .93), (.5, 1.05), (.625, .93), (.75, 1.05), (1, 1)));
                BeginAnimation(WingLProperty, K(2400, (.125, 70), (.25, 14), (.375, 70), (.5, 14), (.625, 70), (.75, 14), (1, 14)));
                BeginAnimation(WingRProperty, K(2400, (.125, 14), (.25, 70), (.375, 14), (.5, 70), (.625, 14), (.75, 70), (1, 14)));
                BeginAnimation(BlushProperty, K(2400, (.2, .9), (.8, .9), (1, .45)));
                break;
            default: // sleepy droop with a yawn
                BeginAnimation(SquashProperty, K(3200, (.3, .93), (.75, .93), (1, 1)));
                BeginAnimation(TiltProperty, K(3200, (.3, 10), (.75, 10), (1, 0)));
                BeginAnimation(LidsProperty, K(3200, (.3, .85), (.75, .85), (1, 0)));
                BeginAnimation(BeakProperty, K(3200, (.2, 0), (.4, 1), (.6, 1), (.75, 0), (1, 0)));
                break;
        }
    }
}
