using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>Small motion helpers for the network pages (microphone, ping, DNS, weather, Telegram). Everything is transform,
/// opacity or a brush's opacity, ends with <c>FillBehavior.Stop</c> or removes its own clock, and does nothing when
/// <see cref="Motion.Enabled"/> is false (Settings, low-memory mode, Windows' animation switch).</summary>
internal static class NetFx
{
    // ---- number counter ----------------------------------------------------------------------------

    private static readonly DependencyProperty NumProperty = DependencyProperty.RegisterAttached("Num", typeof(double), typeof(NetFx),
        new PropertyMetadata(0.0, (d, e) => { if (d is TextBlock t && t.GetValue(FormatProperty) is Func<double, string> f) t.Text = f((double)e.NewValue); }));
    private static readonly DependencyProperty LastCountProperty = DependencyProperty.RegisterAttached("LastCount", typeof(long), typeof(NetFx), new PropertyMetadata(0L));
    private static readonly DependencyProperty FormatProperty = DependencyProperty.RegisterAttached("Format", typeof(Func<double, string>), typeof(NetFx));
    private static readonly DependencyProperty HasValueProperty = DependencyProperty.RegisterAttached("HasValue", typeof(bool), typeof(NetFx));

    /// <summary>Shows <paramref name="to"/> in <paramref name="text"/>, counting up or down from the last shown number.
    /// The first value (nothing shown yet) appears without counting from zero.</summary>
    public static void Count(TextBlock text, double to, Func<double, string> format, int ms = 520)
    {
        var had = (bool)text.GetValue(HasValueProperty);
        text.SetValue(FormatProperty, format);
        text.SetValue(HasValueProperty, true);
        var from = (double)text.GetValue(NumProperty);
        // A live reading (ping, speed...) changes every second. Counting on every change keeps the render thread and text layout busy
        // all the time, so only a clearly different number counts, and at most once every 3 s; small wobbles just show the new value.
        var now = Environment.TickCount64;
        var big = Math.Abs(to - from) >= Math.Max(2, 0.2 * Math.Max(Math.Abs(from), Math.Abs(to)));
        if (!had || !Motion.Enabled || Math.Abs(from - to) < 0.0001 || !big || now - (long)text.GetValue(LastCountProperty) < 3000)
        {
            text.BeginAnimation(NumProperty, null);
            text.SetValue(NumProperty, to);
            text.Text = format(to);
            return;
        }
        text.SetValue(NumProperty, to);
        text.SetValue(LastCountProperty, now);
        text.BeginAnimation(NumProperty, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Fx.Out, FillBehavior = FillBehavior.Stop }.Slow());
    }

    /// <summary>Plain text that replaces a counted number (such as a dash): forgets the counter so the next number does not count from a stale value.</summary>
    public static void Plain(TextBlock text, string value)
    {
        text.BeginAnimation(NumProperty, null);
        text.SetValue(HasValueProperty, false);
        text.Text = value;
    }

    // ---- text and small pops -----------------------------------------------------------------------

    /// <summary>Sets a status line; when the words change it rises in softly (and stays hidden when empty).</summary>
    public static void Say(TextBlock text, string value, bool fromEmpty = true)
    {
        if (text.Text == value) return;
        var had = text.Text.Length > 0;
        text.Text = value;
        if (value.Length > 0 && (had || fromEmpty) && Motion.Enabled) Motion.Rise(text, 0, 6, 0.4);
    }

    /// <summary>One-shot soft pulse of the opacity (a value that just changed).</summary>
    public static void Flash(UIElement element, double from = 0.35, int ms = 420)
    {
        if (!Motion.Enabled) return;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(from, element.Opacity, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Fx.Out, FillBehavior = FillBehavior.Stop });
    }

    /// <summary>A newly built list: its first <paramref name="max"/> children rise in one after another (55 ms apart, the page's own rhythm).</summary>
    public static void RiseChildren(Panel panel, int max = 14, double dy = 10, double seconds = 0.5)
    {
        if (!Motion.Enabled) return;
        var i = 0;
        foreach (var child in panel.Children.OfType<FrameworkElement>())
        {
            if (i >= max) break;
            Motion.Rise(child, i++, dy, seconds);
        }
    }

    /// <summary>Slides the element away and fades it out, then runs <paramref name="done"/> (list removal).</summary>
    public static void Out(FrameworkElement element, Action done)
    {
        if (!Motion.Enabled) { done(); return; }
        var move = new TranslateTransform();
        element.RenderTransform = move;
        var span = TimeSpan.FromMilliseconds(200);
        var fade = new DoubleAnimation(element.Opacity, 0, span) { FillBehavior = FillBehavior.HoldEnd };
        fade.Completed += (_, _) =>
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.RenderTransform = Transform.Identity;
            done();
        };
        element.BeginAnimation(UIElement.OpacityProperty, fade);
        move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, Ui.Flow == FlowDirection.RightToLeft ? -26 : 26, span) { EasingFunction = Fx.Out });
    }

    // ---- hover -------------------------------------------------------------------------------------

    /// <summary>Row highlight that fades in under the pointer (a private brush clone whose opacity animates; no layout, no effect).</summary>
    public static void HoverTint(Border row, string key = "Line")
    {
        var brush = K.Brush(key).CloneCurrentValue();
        brush.Opacity = 0;
        row.Background = brush;
        void To(double value)
        {
            if (!Motion.Enabled) { brush.BeginAnimation(Brush.OpacityProperty, null); brush.Opacity = value; return; }
            var from = brush.Opacity;
            brush.Opacity = value;
            brush.BeginAnimation(Brush.OpacityProperty, new DoubleAnimation(from, value, TimeSpan.FromMilliseconds(220)) { EasingFunction = Fx.Out, FillBehavior = FillBehavior.Stop });
        }
        row.MouseEnter += (_, _) => To(1);
        row.MouseLeave += (_, _) => To(0);
    }

    // ---- continuous (only while something is running) -----------------------------------------------

    /// <summary>Spins the first <see cref="Icon"/> found under <paramref name="host"/> while <paramref name="on"/> is true.</summary>
    public static void Spin(DependencyObject host, bool on)
    {
        var icon = Find<Icon>(host);
        if (icon is null) return;
        if (!on || !Motion.Enabled)
        {
            if (icon.RenderTransform is RotateTransform stop) stop.BeginAnimation(RotateTransform.AngleProperty, null);
            icon.RenderTransform = Transform.Identity;
            return;
        }
        var rotate = new RotateTransform();
        icon.RenderTransformOrigin = new Point(0.5, 0.5);
        icon.RenderTransform = rotate;
        rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever });
    }

    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T hit) return hit;
        if (root is ContentControl { Content: DependencyObject inner }) return Find<T>(inner);
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            if (Find<T>(child) is { } found) return found;
        return null;
    }

    /// <summary>Turns a chevron icon between "closed" and "open" (90 degrees) with the spring; <paramref name="rtl"/> mirrors it.</summary>
    public static void Chevron(Icon icon, bool open, bool rtl)
    {
        if (icon.RenderTransform is not TransformGroup group || group.Children.Count != 2)
        {
            group = new TransformGroup { Children = { new ScaleTransform(rtl ? -1 : 1, 1), new RotateTransform() } };
            icon.RenderTransformOrigin = new Point(0.5, 0.5);
            icon.RenderTransform = group;
        }
        Fx.Anim((RotateTransform)group.Children[1], RotateTransform.AngleProperty, open ? (rtl ? -90 : 90) : 0);
    }
}
