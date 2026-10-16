using System.Windows;
using Shabpa.Core;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Shabpa;

/// <summary>The motion of the app. Every tab opens with its own entrance (what moves, from where, how fast), built
/// from the same few easing curves so the whole app feels like one thing. Each element gets its start delay in turn,
/// so a page builds itself from the top down instead of appearing at once. Nothing stays attached afterwards, so the
/// page is a normal, sharp page again once it has settled.</summary>
public static class Motion
{
    /// <summary>The user's choice in Settings, and Windows' own "animation effects" switch.</summary>
    public static bool Enabled => Hub.I?.Settings is not ({ Animations: false } or { LowMemory: true }) && SystemParameters.ClientAreaAnimation;

    public enum Style { Rise, Side, Ripple, Focus, Drift, Rows, Lift, Breathe, Pop, Tilt, Alternate }

    /// <summary>Which entrance a tab uses.</summary>
    public static Style StyleOf(string page) => page switch
    {
        "home" => Style.Rise,
        "system" => Style.Side,
        "ping" => Style.Ripple,
        "gaming" => Style.Focus,
        "weather" => Style.Drift,
        "dns" => Style.Rows,
        "download" => Style.Lift,
        "comfort" => Style.Breathe,
        "support" => Style.Pop,
        "update" => Style.Tilt,
        "settings" => Style.Alternate,
        _ => Style.Rise,
    };

    private sealed record Spec(double Seconds, double Stagger, double Dx, double Dy, double Scale, double Degrees, bool Spring);

    private static Spec SpecOf(Style style) => style switch
    {
        Style.Rise => new(0.52, 0.055, 0, 24, 1, 0, false),
        Style.Side => new(0.46, 0.045, 38, 0, 1, 0, false),
        Style.Ripple => new(0.50, 0.060, 0, 10, 0.94, 0, false),
        Style.Focus => new(0.40, 0.035, 0, 0, 1.06, 0, false),
        Style.Drift => new(0.78, 0.090, 0, 34, 1, 0, false),
        Style.Rows => new(0.40, 0.030, 30, 0, 1, 0, false),
        Style.Lift => new(0.56, 0.060, 0, 46, 1, 0, true),
        Style.Breathe => new(0.85, 0.120, 0, 0, 0.985, 0, false),
        Style.Pop => new(0.46, 0.070, 0, 0, 0.90, 0, true),
        Style.Tilt => new(0.52, 0.060, 0, 12, 1, -3.5, false),
        _ => new(0.42, 0.040, 28, 0, 1, 0, false),
    };

    private static IEasingFunction Out(bool spring) =>
        spring ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.55 } : new QuarticEase { EasingMode = EasingMode.EaseOut };

    /// <summary>Plays the entrance of <paramref name="page"/>: its top-level blocks appear one after another.</summary>
    public static void Enter(string pageKey, FrameworkElement page)
    {
        if (!Enabled || page is null) return;
        var spec = SpecOf(StyleOf(pageKey));
        var blocks = page is ContentControl { Content: Panel panel } ? panel.Children.OfType<FrameworkElement>().Where(c => c.Visibility == Visibility.Visible).ToList() : [];
        if (blocks.Count == 0) blocks = [page];
        for (var i = 0; i < blocks.Count; i++)
            Play(blocks[i], spec, pageKey, i);
    }

    /// <summary>A short entrance for the content of a tab inside a tab page.</summary>
    public static void Switch(FrameworkElement content)
    {
        if (!Enabled || content is null) return;
        Play(content, new Spec(0.34, 0, 0, 14, 1, 0, false), "", 0);
    }

    private static void Play(FrameworkElement element, Spec spec, string pageKey, int index)
    {
        var fromSide = (L.En ? -1 : 1) * spec.Dx;
        var alternate = StyleOf(pageKey) == Style.Alternate && index % 2 == 1;
        var dx = alternate ? -fromSide : fromSide;
        var delay = TimeSpan.FromSeconds(Math.Min(index, 12) * spec.Stagger);
        var duration = TimeSpan.FromSeconds(spec.Seconds);
        var ease = Out(spec.Spring);

        var scale = new ScaleTransform(spec.Scale, spec.Scale);
        var move = new TranslateTransform(dx, spec.Dy);
        var rotate = new RotateTransform(spec.Degrees);
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(rotate);
        group.Children.Add(move);
        element.RenderTransformOrigin = new Point(0.5, spec.Dy > 0 || spec.Degrees != 0 ? 0.0 : 0.5);
        element.RenderTransform = group;
        element.Opacity = 0;

        DoubleAnimation Make(double from, double to, bool eased = true) =>
            new(from, to, duration) { BeginTime = delay, EasingFunction = eased ? ease : null, FillBehavior = FillBehavior.HoldEnd };

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(Math.Min(0.34, spec.Seconds))) { BeginTime = delay, FillBehavior = FillBehavior.HoldEnd };
        fade.Completed += (_, _) =>
        {
            // Settled: drop every animation and transform so the page is drawn plainly again.
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
        };
        element.BeginAnimation(UIElement.OpacityProperty, fade);

        if (spec.Scale != 1)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, Make(spec.Scale, 1));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, Make(spec.Scale, 1));
        }
        if (spec.Dx != 0) move.BeginAnimation(TranslateTransform.XProperty, Make(dx, 0));
        if (spec.Dy != 0) move.BeginAnimation(TranslateTransform.YProperty, Make(spec.Dy, 0));
        if (spec.Degrees != 0) rotate.BeginAnimation(RotateTransform.AngleProperty, Make(spec.Degrees, 0));

        var cleanup = new DoubleAnimation(0, 1, duration + TimeSpan.FromMilliseconds(60)) { BeginTime = delay };
        cleanup.Completed += (_, _) => element.RenderTransform = null;
        scale.BeginAnimation(ScaleTransform.CenterXProperty, cleanup); // only used as a timer to remove the transform
    }

    /// <summary>The page title and subtitle glide in when the tab changes.</summary>
    public static void Header(params FrameworkElement[] parts)
    {
        if (!Enabled) return;
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var move = new TranslateTransform((L.En ? -1 : 1) * 16, 0);
            part.RenderTransform = move;
            part.Opacity = 0;
            var begin = TimeSpan.FromMilliseconds(40 + i * 70);
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)) { BeginTime = begin };
            fade.Completed += (_, _) =>
            {
                part.BeginAnimation(UIElement.OpacityProperty, null);
                part.Opacity = 1;
                part.RenderTransform = null;
            };
            part.BeginAnimation(UIElement.OpacityProperty, fade);
            move.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(move.X, 0, TimeSpan.FromMilliseconds(420)) { BeginTime = begin, EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } });
        }
    }
}
