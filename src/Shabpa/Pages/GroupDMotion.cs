using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>Cheap motion shared by the Downloads, Files, Memory, System and Health pages (Glass Depth): list items that spring
/// in and out, a number that counts up, a bar that fills, a note that fades in. Everything is transform/opacity (or one
/// attached number), runs once and leaves nothing behind, and is skipped when <see cref="Motion.Enabled"/> is off.</summary>
internal static class Mo
{
    // ---- list items ----------------------------------------------------------------------------------

    /// <summary>Remembers which list items were already shown, so a rebuilt list only animates the ones that are new.</summary>
    public sealed class Memo(Panel panel)
    {
        private HashSet<string> _seen = [];
        private HashSet<string> _next = [];
        private int _index;

        /// <summary>Call before the list is rebuilt.</summary>
        public void Begin()
        {
            _next = [];
            _index = 0;
        }

        /// <summary>Registers an item; a new one rises in (staggered) when the list is already on screen.</summary>
        public T Item<T>(string key, T element) where T : FrameworkElement
        {
            _next.Add(key);
            if (!_seen.Contains(key) && panel.IsLoaded) Appear(element, _index++);
            return element;
        }

        /// <summary>Call after the list is rebuilt.</summary>
        public void End() => _seen = _next;
    }

    /// <summary>A short rise for one item: opacity, 12 px up, scale .97 to 1 (index staggers by 55 ms, capped).</summary>
    public static void Appear(FrameworkElement element, int index, double dy = 12, double seconds = 0.5)
    {
        if (!Motion.Enabled) return;
        Motion.Rise(element, Math.Min(index, 8), dy, seconds);
    }

    /// <summary>Plays the exit of an item (fade, small shrink and drift), then runs <paramref name="done"/> once.</summary>
    public static void Leave(FrameworkElement? element, Action done)
    {
        if (element is null || !Motion.Enabled || !element.IsLoaded)
        {
            done();
            return;
        }
        element.IsHitTestVisible = false;
        var scale = new ScaleTransform(1, 1);
        var move = new TranslateTransform();
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = new TransformGroup { Children = { scale, move } };
        var span = TimeSpan.FromMilliseconds(190);
        var finished = false;
        void Finish()
        {
            if (finished) return;
            finished = true;
            done();
        }
        var fade = new DoubleAnimation(1, 0, span) { FillBehavior = FillBehavior.HoldEnd };
        fade.Completed += (_, _) => Finish();
        element.BeginAnimation(UIElement.OpacityProperty, fade);
        move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, Ui.Flow == FlowDirection.RightToLeft ? -26 : 26, span) { EasingFunction = Fx.Out });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0.97, span));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.97, span));
    }

    /// <summary>Shows or hides a block; a block that becomes visible rises in.</summary>
    public static void Show(FrameworkElement element, bool visible, double dy = 10, double seconds = 0.45)
    {
        var was = element.Visibility == Visibility.Visible;
        element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        // not while the page is still being put on screen: the page entrance rises its blocks itself
        if (visible && !was && (element.Parent as FrameworkElement ?? element).IsLoaded) Appear(element, 0, dy, seconds);
    }

    // ---- notes and results ---------------------------------------------------------------------------

    /// <summary>A note, status line or result that just appeared or changed: fades in and drifts up 6 px. It can be called again
    /// while it still plays (it restarts); it owns one translate transform per element and leaves nothing behind.</summary>
    public static void Flash(FrameworkElement element)
    {
        if (!Motion.Enabled || !element.IsLoaded) return;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(380)) { FillBehavior = FillBehavior.Stop });
        var move = element.GetValue(FlashMoveProperty) as TranslateTransform;
        if (move is null && (element.RenderTransform is null || element.RenderTransform == Transform.Identity))
        {
            move = new TranslateTransform();
            element.SetValue(FlashMoveProperty, move);
            element.RenderTransform = move;
        }
        if (move is not null && ReferenceEquals(element.RenderTransform, move))
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(520)) { EasingFunction = Fx.Out, FillBehavior = FillBehavior.Stop });
    }

    private static readonly DependencyProperty FlashMoveProperty = DependencyProperty.RegisterAttached("FlashMove", typeof(TranslateTransform), typeof(Mo));

    /// <summary>Sets the text of <paramref name="block"/> and flashes it when it actually changed.</summary>
    public static void Say(TextBlock block, string text)
    {
        text = ListUi.Ltr(text);
        var changed = block.Text != text;
        block.Text = text;
        if (changed && text.Length > 0) Flash(block);
    }

    // ---- numbers and bars ----------------------------------------------------------------------------

    private static readonly DependencyProperty CountProperty = DependencyProperty.RegisterAttached("Count", typeof(double), typeof(Mo),
        new PropertyMetadata(0.0, (d, e) =>
        {
            if (d is TextBlock block && block.GetValue(FormatProperty) is Func<double, string> format) block.Text = format((double)e.NewValue);
        }));

    private static readonly DependencyProperty FormatProperty = DependencyProperty.RegisterAttached("Format", typeof(Func<double, string>), typeof(Mo));

    /// <summary>Counts the number in <paramref name="block"/> from its last value (zero the first time) to <paramref name="to"/>.
    /// The final text is always written first, so a page that is not visible still shows the right number.</summary>
    public static void Count(TextBlock block, double to, Func<double, string> format, int ms = 800)
    {
        block.SetValue(FormatProperty, format);
        var from = (double)block.GetValue(CountProperty);
        block.BeginAnimation(CountProperty, null);
        block.SetValue(CountProperty, to);
        block.Text = format(to);
        if (!Motion.Enabled || Math.Abs(from - to) < 0.5) return;
        block.BeginAnimation(CountProperty, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Fx.Out, FillBehavior = FillBehavior.Stop });
    }

    /// <summary>Counts a whole number (Persian digits in Persian mode).</summary>
    public static void CountInt(TextBlock block, int to, int ms = 800) =>
        Count(block, to, v => Core.Fa.Digits(((int)Math.Round(v)).ToString()), ms);

    /// <summary>Sweeps a bar from <paramref name="from"/> to its (already set) value.</summary>
    public static void Fill(ProgressBar bar, double from, int ms = 700)
    {
        if (!Motion.Enabled || Math.Abs(from - bar.Value) < 0.0005) return;
        bar.BeginAnimation(RangeBase.ValueProperty, new DoubleAnimation(from, bar.Value, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = Fx.Out, FillBehavior = FillBehavior.Stop,
        });
    }
}
