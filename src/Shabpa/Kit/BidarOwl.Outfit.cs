using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Shabpa.Core;
using Shabpa.Pages;

namespace Shabpa.Kit;

// ---- Boofi as the store shows him: the little Boofi (dock, assistant) can draw the same layered pictures, wearing what the person wears,
// and keeps alive with a gentle breath plus ten small acts now and then. Everything is a render-thread animation that ends by itself. ----
public sealed partial class BidarOwl
{
    private readonly Canvas _sprite = new() { Width = 100, Height = 120, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    private readonly ScaleTransform _breath = new(1, 1, 50, 112), _aScale = new(1, 1, 50, 112), _watchPulse = new(1, 1, 22, 69);
    private readonly RotateTransform _bodyTilt = new(0, 50, 108), _aTilt = new(0, 50, 108), _lanSwing = new(0, 80, 70);
    private readonly TranslateTransform _aShift = new(), _glassNudge = new();
    private readonly DispatcherTimer _idleTimer = new();
    private bool _useOutfit, _spriteOn, _hasHat, _hasLantern, _hasWatch, _hasGlasses;
    private int _lastAct = -1;
    private static readonly Random ActRng = new();

    /// <summary>True for the Boofi that wears the person's outfit (set once, before it is shown).</summary>
    public bool UseOutfit
    {
        get => _useOutfit;
        set
        {
            if (_useOutfit == value) return;
            _useOutfit = value;
            _sprite.RenderTransform = new TransformGroup { Children = { _breath, _aScale, _aTilt, _bodyTilt, _aShift, _squash, _lift, _spin } };
            if (value)
            {
                BoofiOutfit.Changed += OnOutfit;
                Unloaded += (_, _) => StopOutfitLife();
                Loaded += (_, _) => { RefreshOutfit(); StartOutfitLife(); };
                RefreshOutfit();
            }
            else
            {
                BoofiOutfit.Changed -= OnOutfit;
                StopOutfitLife();
                ShowSprite(false);
            }
        }
    }

    private void OnOutfit() => Dispatcher.BeginInvoke(RefreshOutfit);

    private void ShowSprite(bool on)
    {
        _spriteOn = on;
        _sprite.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (_vector is not null) _vector.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Redraws the layers for what is worn now; falls back to the drawn rig when the pictures are not available.</summary>
    public void RefreshOutfit()
    {
        if (!_useOutfit) return;
        var worn = BoofiOutfit.Current;
        var files = BoofiCatalog.Layers(worn);
        _sprite.Children.Clear();
        _hasHat = worn.ContainsKey("hat"); _hasLantern = worn.ContainsKey("lantern"); _hasWatch = worn.ContainsKey("watch"); _hasGlasses = worn.ContainsKey("glasses");
        const double side = 132, left = -16, top = -8;
        var any = false;
        foreach (var file in files)
        {
            if (BoofiArt.Load(file, 224) is not { } picture) continue;
            var image = new Image { Source = picture, Width = side, Height = side, Stretch = Stretch.Fill, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            Canvas.SetLeft(image, left);
            Canvas.SetTop(image, top);
            var kind = file.Split('-', '_')[0];
            if (kind == "hat") image.RenderTransform = new TransformGroup { Children = { _hatTilt, _hatMove } };
            else if (kind == "ln") image.RenderTransform = _lanSwing;
            else if (kind == "wt") image.RenderTransform = _watchPulse;
            else if (kind == "gl") image.RenderTransform = _glassNudge;
            _sprite.Children.Add(image);
            any = true;
        }
        ShowSprite(any);
        if (any) StartOutfitLife(); else StopOutfitLife();
    }

    // ---- life --------------------------------------------------------------------------------------------------------------------

    private void StartOutfitLife()
    {
        if (!_useOutfit || !_spriteOn || !IsVisible || !Motion.Enabled) return;
        _breath.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 1.025, TimeSpan.FromMilliseconds(1700)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        _breath.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0.99, TimeSpan.FromMilliseconds(1700)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        ArmIdle();
    }

    private void StopOutfitLife()
    {
        _idleTimer.Stop();
        _breath.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _breath.BeginAnimation(ScaleTransform.ScaleXProperty, null);
    }

    private void ArmIdle()
    {
        if (!_useOutfit || !_spriteOn || !IsVisible || !Motion.Enabled) return;
        _idleTimer.Interval = TimeSpan.FromSeconds(3.5 + ActRng.NextDouble() * 5.5);
        _idleTimer.Start();
    }

    private static DoubleAnimationUsingKeyFrames K(double ms, params (double At, double V)[] frames) => Keys(ms, frames);

    private void Run(Transform t, DependencyProperty dp, DoubleAnimationUsingKeyFrames a) => t.BeginAnimation(dp, a);

    /// <summary>One of ten small acts (never the same twice in a row; skipped while flying or in a mood).</summary>
    public void IdleAct(int? which = null)
    {
        if (!_spriteOn || !Motion.Enabled || _airborne || _mood != BidarMood.Idle) return;
        int n;
        do { n = which ?? ActRng.Next(10); } while (which is null && n == _lastAct);
        _lastAct = n;
        switch (n)
        {
            case 0: // look around
                Run(_aTilt, RotateTransform.AngleProperty, K(2600, (.22, -7), (.5, -7), (.75, 7), (1, 0)));
                Run(_aShift, TranslateTransform.XProperty, K(2600, (.22, -3), (.5, -3), (.75, 3), (1, 0)));
                break;
            case 1: // two hops
                Run(_aShift, TranslateTransform.YProperty, K(1100, (.2, -9), (.4, 0), (.6, -9), (.8, 0), (1, 0)));
                Run(_aScale, ScaleTransform.ScaleYProperty, K(1100, (.08, .93), (.2, 1.04), (.4, .93), (.5, 1.04), (.7, 1), (1, 1)));
                break;
            case 2: // ruffle the feathers
                Run(_aTilt, RotateTransform.AngleProperty, K(750, (.15, 5), (.3, -5), (.45, 4), (.6, -4), (.8, 2), (1, 0)));
                Run(_aScale, ScaleTransform.ScaleXProperty, K(750, (.15, 1.05), (.6, 1.03), (1, 1)));
                break;
            case 3: // stretch up
                Run(_aScale, ScaleTransform.ScaleYProperty, K(1800, (.3, 1.08), (.7, 1.08), (1, 1)));
                Run(_aScale, ScaleTransform.ScaleXProperty, K(1800, (.3, .96), (.7, .96), (1, 1)));
                break;
            case 4: // tip the hat (or nod when there is none)
                if (_hasHat)
                {
                    BeginAnimation(HatLiftProperty, K(1500, (.25, -7), (.65, -7), (1, 0)));
                    BeginAnimation(HatTiltProperty, K(1500, (.25, -14), (.65, -14), (1, 0)));
                    Run(_aTilt, RotateTransform.AngleProperty, K(1500, (.25, 4), (.65, 4), (1, 0)));
                }
                else
                    Run(_aShift, TranslateTransform.YProperty, K(900, (.25, 3), (.5, 0), (.75, 3), (1, 0)));
                break;
            case 5: // swing the lantern
                if (_hasLantern) Run(_lanSwing, RotateTransform.AngleProperty, K(2600, (.15, 10), (.35, -9), (.55, 6), (.75, -3), (1, 0)));
                else Run(_aTilt, RotateTransform.AngleProperty, K(2000, (.3, 4), (.7, -4), (1, 0)));
                break;
            case 6: // look at the wrist
                Run(_aTilt, RotateTransform.AngleProperty, K(1900, (.3, -6), (.75, -6), (1, 0)));
                if (_hasWatch) Run(_watchPulse, ScaleTransform.ScaleXProperty, K(1900, (.35, 1.2), (.65, 1.2), (1, 1)));
                if (_hasWatch) Run(_watchPulse, ScaleTransform.ScaleYProperty, K(1900, (.35, 1.2), (.65, 1.2), (1, 1)));
                break;
            case 7: // push the glasses up
                if (_hasGlasses) Run(_glassNudge, TranslateTransform.YProperty, K(1100, (.2, -3), (.4, 0), (.6, -2), (1, 0)));
                Run(_aShift, TranslateTransform.YProperty, K(1100, (.2, 2), (.5, 0), (1, 0)));
                break;
            case 8: // a little dance
                Run(_aTilt, RotateTransform.AngleProperty, K(2000, (.125, 6), (.25, -6), (.375, 6), (.5, -6), (.625, 6), (.75, -6), (1, 0)));
                Run(_aScale, ScaleTransform.ScaleYProperty, K(2000, (.125, .95), (.25, 1.03), (.375, .95), (.5, 1.03), (.625, .95), (.75, 1.03), (1, 1)));
                break;
            default: // sleepy droop
                Run(_aScale, ScaleTransform.ScaleYProperty, K(3000, (.3, .94), (.7, .94), (1, 1)));
                Run(_aTilt, RotateTransform.AngleProperty, K(3000, (.3, 8), (.7, 8), (1, 0)));
                break;
        }
    }
}
