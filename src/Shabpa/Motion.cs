using System.Windows;
using Shabpa.Core;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Shabpa.Kit;

namespace Shabpa;

/// <summary>The motion of the app ("Glass Depth"): one page entrance for every tab (each top-level block rises 22 px and
/// scales .97 to 1 over 800 ms with an ease-out, 55 ms apart), a 170 ms exit, and small touches. Springy hover/press lives in
/// <see cref="Fx"/>. Nothing stays attached afterwards: when an animation ends the clock and the transform are removed, so a
/// settled page costs nothing. Everything honours <see cref="Enabled"/>.</summary>
public static class Motion
{
    /// <summary>Tests only: a headless runner reports animations as off; this pins the answer.</summary>
    public static bool? TestOverride;

    /// <summary>The user's choice in Settings, low-memory mode, and Windows' own "animation effects" switch.</summary>
    public static bool Enabled => TestOverride ?? (Hub.I?.Settings is not ({ Animations: false } or { LowMemory: true }) && SystemParameters.ClientAreaAnimation);

    /// <summary>Kept for callers that still ask which entrance a tab uses: every tab uses the same one now.</summary>
    public enum Style { Rise }

    public static Style StyleOf(string page) => Style.Rise;

    /// <summary>Plays the entrance of <paramref name="page"/>: its top-level blocks rise one after another.
    /// <paramref name="firstIndex"/> lets the shell's own header take index 0.</summary>
    public static void Enter(string pageKey, FrameworkElement page, int firstIndex = 0)
    {
        if (!Enabled || page is null) return;
        var blocks = page is ContentControl { Content: Panel panel } ? panel.Children.OfType<FrameworkElement>().Where(c => c.Visibility == Visibility.Visible).ToList() : [];
        if (blocks.Count == 0) blocks = [page];
        for (var i = 0; i < blocks.Count; i++) Rise(blocks[i], i + firstIndex);
    }

    /// <summary>A short entrance for the content of a tab inside a tab page.</summary>
    public static void Switch(FrameworkElement content)
    {
        if (!Enabled || content is null) return;
        Rise(content, 0, 12, 0.34);
    }

    /// <summary>The design's "rise": opacity 0 to 1, translateY 22 to 0, scale .97 to 1; index * 55 ms stagger.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Transform, object> RiseGroups = new();

    public static void Rise(FrameworkElement element, int index, double dy = 22, double seconds = 0.8)
    {
        if (!Enabled) return;
        // A transform left behind by an earlier (interrupted or repeated) Rise is ours: never nest it, or its half-way values stick.
        var original = element.RenderTransform;
        if (original is { } prev && RiseGroups.TryGetValue(prev, out _)) original = Transform.Identity;
        var scale = new ScaleTransform(0.97, 0.97);
        var move = new TranslateTransform(0, dy);
        var group = new TransformGroup();
        RiseGroups.Add(group, new object());
        if (original is { } o && o != Transform.Identity && !o.IsFrozen) group.Children.Add(o);
        group.Children.Add(scale);
        group.Children.Add(move);
        var savedOrigin = element.RenderTransformOrigin;
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = group;
        element.Opacity = 0;
        // While it moves and fades the block is drawn from one cached bitmap: the graphics card then only moves a picture instead of
        // re-tessellating every rounded shape of the block on every frame (that is what made a page's entrance cost ~20 MB of RAM).
        var cached = CacheWhileMoving(element);

        var delay = TimeSpan.FromMilliseconds(Math.Min(index, 12) * 55);
        var duration = TimeSpan.FromSeconds(seconds);
        DoubleAnimation Make(double from, double to) =>
            new(from, to, duration) { BeginTime = delay, EasingFunction = Fx.Out, FillBehavior = FillBehavior.HoldEnd };

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(Math.Min(0.5, seconds))) { BeginTime = delay, FillBehavior = FillBehavior.HoldEnd };
        element.BeginAnimation(UIElement.OpacityProperty, fade);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, Make(0.97, 1));
        var lift = Make(dy, 0);
        lift.Completed += (_, _) =>
        {
            // Settled: drop every animation and transform so the page is drawn plainly again.
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
            if (cached) element.CacheMode = null;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            move.BeginAnimation(TranslateTransform.YProperty, null);
            // Clearing an animation leaves the start value behind (.97 / dy): make a stale group harmless.
            scale.ScaleX = 1; scale.ScaleY = 1; move.Y = 0;
            if (ReferenceEquals(element.RenderTransform, group))
            {
                element.RenderTransform = original is { } o2 && !o2.IsFrozen ? o2 : Transform.Identity;
                element.RenderTransformOrigin = savedOrigin;
            }
        };
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, Make(0.97, 1));
        move.BeginAnimation(TranslateTransform.YProperty, lift);
    }

    /// <summary>Gives <paramref name="element"/> a bitmap cache for the length of an animation (the caller removes it when it ends).
    /// (The block has not been measured yet when its entrance starts, so its size cannot be checked here; WPF itself skips the cache for anything bigger than the graphics card can hold.)</summary>
    internal static bool CacheWhileMoving(FrameworkElement element)
    {
        if (element.CacheMode is not null) return false;
        // RenderAtScale is relative to device-independent units: use the screen's scale so the text stays sharp on 125% / 150% screens
        element.CacheMode = new BitmapCache(Math.Max(1, VisualTreeHelper.GetDpi(element).DpiScaleX)) { SnapsToDevicePixels = true };
        return true;
    }

    /// <summary>The 170 ms exit of the old page: fades and lifts 8 px, then <paramref name="done"/> runs exactly once. If the clock
    /// never reports back (a render thread that is stuck, a window that is not drawn) a one-shot timer runs it anyway: without that
    /// the page switch would wait for the exit forever and the old page would stay faded out on screen.</summary>
    public static void Leave(FrameworkElement host, Action done)
    {
        if (!Enabled) { done(); return; }
        var ran = false;
        var mine = ++_leaveId;
        void Finish()
        {
            if (ran) return;
            ran = true;
            // a newer exit on the same host owns the fade now: this one only reports back
            if (mine == _leaveId)
            {
                host.BeginAnimation(UIElement.OpacityProperty, null);
                host.Opacity = 1;
                host.RenderTransform = Transform.Identity;
            }
            done();
        }
        var move = new TranslateTransform();
        var scale = new ScaleTransform(1, 1);
        host.RenderTransformOrigin = new Point(0.5, 0.3);
        host.RenderTransform = new TransformGroup { Children = { scale, move } };
        var d = TimeSpan.FromMilliseconds(170);
        var fade = new DoubleAnimation(1, 0, d) { FillBehavior = FillBehavior.HoldEnd };
        fade.Completed += (_, _) => Finish();
        host.BeginAnimation(UIElement.OpacityProperty, fade);
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -8, d));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0.99, d));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.99, d));
        var net = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Normal, host.Dispatcher) { Interval = d + LeaveSafety };
        net.Tick += (_, _) => { net.Stop(); Finish(); };
        net.Start();
    }

    [ThreadStatic] private static int _leaveId;

    /// <summary>How long past the exit's own length the safety timer of <see cref="Leave"/> waits.</summary>
    internal static TimeSpan LeaveSafety = TimeSpan.FromMilliseconds(300);

    /// <summary>The page title and subtitle rise in when the tab changes (the shell header is block 0 of the page).</summary>
    public static void Header(params FrameworkElement[] parts)
    {
        if (!Enabled) return;
        foreach (var part in parts) Rise(part, 0, 14, 0.7);
    }

    // ---- small touches ---------------------------------------------------------------------------

    /// <summary>Kept for compatibility: cards no longer change their edge on hover (the design lifts them instead,
    /// see <see cref="GlassCard.Lift"/>).</summary>
    public static void Install() { }

    /// <summary>A short spring pop for something that was just chosen.</summary>
    public static void Pop(FrameworkElement element)
    {
        if (!Enabled) return;
        var scale = new ScaleTransform(0.92, 0.92);
        var saved = element.RenderTransform;
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = scale;
        var grow = new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(500)) { EasingFunction = SpringEase.I };
        grow.Completed += (_, _) => element.RenderTransform = saved;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }

    private static readonly IEasingFunction In = Freeze(new CubicEase { EasingMode = EasingMode.EaseIn });
    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    /// <summary>The exit of a dialog / flyout: the sheet fades, sinks 14 px and shrinks to 96% in 150 ms, then
    /// <paramref name="done"/> runs exactly once (also when motion is off, when the sheet is not shown, or if the clock never
    /// reports back: a one-shot timer is the safety net). Nothing is left running afterwards.</summary>
    public static void Dismiss(FrameworkElement sheet, Action done, double seconds = 0.15, double dy = 14)
    {
        if (!Enabled || !sheet.IsLoaded || !sheet.IsVisible) { done(); return; }
        var ran = false;
        void Finish() { if (ran) return; ran = true; done(); }
        var scale = new ScaleTransform(1, 1);
        var move = new TranslateTransform();
        sheet.RenderTransformOrigin = new Point(0.5, 0.5);
        sheet.RenderTransform = new TransformGroup { Children = { scale, move } };
        var d = TimeSpan.FromSeconds(seconds);
        var fade = new DoubleAnimation(1, 0, d) { EasingFunction = In, FillBehavior = FillBehavior.HoldEnd };
        fade.Completed += (_, _) => Finish();
        sheet.BeginAnimation(UIElement.OpacityProperty, fade);
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, dy, d) { EasingFunction = In });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0.96, d) { EasingFunction = In });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.96, d) { EasingFunction = In });
        var net = new System.Windows.Threading.DispatcherTimer { Interval = d + TimeSpan.FromMilliseconds(300) };
        net.Tick += (_, _) => { net.Stop(); Finish(); };
        net.Start();
    }

    /// <summary>The side menu's entries slide in one after another when the window opens.</summary>
    public static void Stagger(IEnumerable<UIElement> items)
    {
        if (!Enabled) return;
        var index = 0;
        foreach (var item in items)
        {
            var move = new TranslateTransform(22, 0);
            item.RenderTransform = move;
            item.Opacity = 0;
            var begin = TimeSpan.FromMilliseconds(60 + index++ * 38);
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { BeginTime = begin };
            fade.Completed += (_, _) => { item.BeginAnimation(UIElement.OpacityProperty, null); item.Opacity = 1; item.RenderTransform = Transform.Identity; };
            item.BeginAnimation(UIElement.OpacityProperty, fade);
            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(22, 0, TimeSpan.FromMilliseconds(420)) { BeginTime = begin, EasingFunction = Fx.Out });
        }
    }
}
