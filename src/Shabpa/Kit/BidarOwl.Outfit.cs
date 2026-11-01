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
    private Ellipse? _bodyEl, _bellyEl;
    private Path? _wlPath, _wrPath;
    private readonly Canvas _gBack = new() { Width = 100, Height = 120, IsHitTestVisible = false }, _gNeck = new() { IsHitTestVisible = false },
        _gWatch = new() { IsHitTestVisible = false }, _gLantern = new() { IsHitTestVisible = false },
        _gGlasses = new() { Width = 100, Height = 120, IsHitTestVisible = false }, _gHat = new() { Width = 100, Height = 120, IsHitTestVisible = false };
    private readonly RotateTransform _lanSwing = new(0, RestHandX, RestHandY);
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

    /// <summary>How far (rig pixels) a hat is moved down so it sits on the drawn head (found by laying every hat over the rig).</summary>
    private static double HatDrop(string id) => id switch
    {
        "hat-headset" => 2.5, "hat-aviator" => 5, "hat-astronaut" => 4, "hat-halo" => 4, "hat-earflap" => 6.5, _ => 7.5,
    };

    private static Color Hx(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    /// <summary>Called while the rig is built: lays the (empty) part layers into it at the right depth.</summary>
    private void AttachOutfit(Canvas root, Canvas head, Canvas hat)
    {
        root.Children.Insert(0, _gBack);
        // the left wing's group follows the wing's own turn (pivot = shoulder), so a watch rides on the wrist
        _gWatch.RenderTransform = new TransformGroup { Children = { _watchDangle, _watchFollow } }; // the watch turns with the left wing and dangles a little
        _gLantern.RenderTransform = new TransformGroup { Children = { _lanSwing, _lanMove } };      // the lantern hangs from the right hand and swings
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
    public void Showcase(string slot)
    {
        var act = slot switch { "hat" => 4, "glasses" => 7, "watch" => 6, "lantern" => 5, "neck" => 1, _ => 3 };
        if (DateTime.UtcNow < _actUntil) { _pendingAct = act; ArmPending(); return; } // one act at a time: the latest request waits its turn
        IdleAct(act);
    }

    private void OnOutfit() => Dispatcher.BeginInvoke(RefreshOutfit);

    public void RefreshOutfit()
    {
        if (!_useOutfit) return;
        Dress(BoofiOutfit.Current);
    }

    private static Brush? SkinBrush(string file, int width, Rect? crop)
    {
        if (BoofiArt.Load(file, width) is not { } picture) return null;
        var brush = new ImageBrush(picture) { Stretch = Stretch.Fill };
        if (crop is { } c) brush.Viewbox = c;
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
        brush.Freeze();
        return brush;
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

        Characterize(Get("hat")?.Id, Get("glasses")?.Id, Get("neck")?.Id, Get("watch")?.Id, Get("lantern")?.Id);

        // fur: repaint the drawn body; the glow behind (epic and above) is a picture
        var fur = Get("fur");
        var c = fur is not null && Fur.TryGetValue(fur.Id, out var col) ? col : ("#3d3b94", "#5652b8", "#2a2766", "#7d7ae0");
        _cBody.Color = Hx(c.Item1); _cBelly.Color = Hx(c.Item2); _cWing.Color = Hx(c.Item3); _cEdge.Color = Hx(c.Item4);
        // the fur's own gradient and pattern (the same art as the store tile) is painted into the body and the wings
        var skinBody = fur is null ? null : SkinBrush($"fb_{fur.Id}.png", 384, null);
        var skinWing = fur is null ? null : SkinBrush($"fw_{fur.Id}.png", 224, new Rect(0.184, 0.048, 0.663, 0.903));
        var skinBelly = fur is null ? null : SkinBrush($"fv_{fur.Id}.png", 288, null);
        if (_bodyEl is not null) _bodyEl.Fill = skinBody ?? _cBody;
        if (_bellyEl is not null) _bellyEl.Fill = skinBelly ?? _cBelly;
        if (_wlPath is not null) _wlPath.Fill = skinWing ?? _cWing;
        if (_wrPath is not null) _wrPath.Fill = skinWing ?? _cWing;

        // the drawn cap and candle give way to a worn hat or lantern
        if (_vHat is not null) _vHat.Visibility = _hasHat ? Visibility.Collapsed : Visibility.Visible;
        _lantern.Visibility = _hasLantern ? Visibility.Collapsed : Visibility.Visible;
        _glow.Visibility = _hasLantern ? Visibility.Collapsed : Visibility.Visible;

        foreach (var slot in new[] { "neck", "glasses", "hat", "watch", "lantern" })
        {
            if (Get(slot) is not { } part) continue;
            if (part.HasBehind && Pic($"{part.Id}_b.png") is { } back)
            {
                if (slot == "hat") Canvas.SetTop(back, Top + HatDrop(part.Id));
                _gBack.Children.Add(back);
            }
            if (!part.HasFront || Pic($"{part.Id}.png") is not { } front) continue;
            switch (slot)
            {
                case "neck": _gNeck.Children.Add(front); break;
                case "glasses": _gGlasses.Children.Add(front); break;
                case "hat":
                    Canvas.SetTop(front, Top + HatDrop(part.Id)); // the drawn head is lower than the store's, so hats move down onto it
                    _gHat.Children.Add(front);
                    break;
                case "watch":
                    // the rig's wing is the model-sheet wing at 0.63, hung 1.5 px right and 7.9 px lower than in the store frame, so the watch moves by the same
                    Canvas.SetLeft(front, Left + 1.5); Canvas.SetTop(front, Top + 7.9);
                    _gWatch.Children.Add(front);
                    break;
                default:
                    Canvas.SetLeft(front, Left - 1.5); Canvas.SetTop(front, Top + 7.9); // same shift for the right wing (mirrored)
                    _gLantern.Children.Add(front);
                    break;
            }
        }
    }

    // ---- life --------------------------------------------------------------------------------------------------------------------

    private void StartOutfitLife()
    {
        if (!(_useOutfit || Standalone) || !IsVisible || !Motion.Enabled) return;
        StartBreathing();
        StartGlances();
        ArmIdle();
    }

    private void StopOutfitLife() { _idleTimer.Stop(); StopBreathing(); StopGlances(); }

    private void ArmIdle()
    {
        if (!(_useOutfit || Standalone) || !IsVisible || !Motion.Enabled) return;
        _idleTimer.Interval = TimeSpan.FromSeconds(2.5 + ActRng.NextDouble() * 3.5);
        _idleTimer.Start();
    }

}
