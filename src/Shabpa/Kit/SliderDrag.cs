using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Shabpa.Kit;

/// <summary>Press anywhere on a slider's track: the value jumps there and keeps following the pointer until release (the built-in
/// move-to-point only jumps, and hands over to the knob unreliably). A press on the knob itself is left to the knob.
/// <para>XAML: <c>kit:SliderDrag.Enabled="True"</c> (the Slide style does it).</para></summary>
public static class SliderDrag
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(SliderDrag), new PropertyMetadata(false, OnEnabled));
    public static bool GetEnabled(DependencyObject o) => (bool)o.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject o, bool v) => o.SetValue(EnabledProperty, v);

    private static void OnEnabled(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Slider s) return;
        s.PreviewMouseLeftButtonDown -= Down;
        s.PreviewMouseMove -= Move;
        s.PreviewMouseLeftButtonUp -= Up;
        if (!(bool)e.NewValue) return;
        s.PreviewMouseLeftButtonDown += Down;
        s.PreviewMouseMove += Move;
        s.PreviewMouseLeftButtonUp += Up;
    }

    private static bool OnThumb(Slider s, MouseEventArgs e)
        => e.OriginalSource is DependencyObject o && FindThumb(o) is not null && s.IsAncestorOf(o);

    private static Thumb? FindThumb(DependencyObject? o)
    {
        for (; o is not null; o = o is Visual ? VisualTreeHelper.GetParent(o) : LogicalTreeHelper.GetParent(o))
            if (o is Thumb t) return t;
        return null;
    }

    private static void Down(object sender, MouseButtonEventArgs e)
    {
        var s = (Slider)sender;
        if (!s.IsEnabled || OnThumb(s, e)) return;
        if (s.Template?.FindName("PART_Track", s) is not Track) return;
        s.Focus();
        s.CaptureMouse();
        SetFrom(s, e);
        e.Handled = true;
    }

    private static void Move(object sender, MouseEventArgs e)
    {
        var s = (Slider)sender;
        if (s.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) SetFrom(s, e);
    }

    private static void Up(object sender, MouseButtonEventArgs e)
    {
        var s = (Slider)sender;
        if (!s.IsMouseCaptured) return;
        s.ReleaseMouseCapture();
        e.Handled = true;
    }

    private static void SetFrom(Slider s, MouseEventArgs e)
    {
        if (s.Template?.FindName("PART_Track", s) is Track track && ValueAt(s, e.GetPosition(track)) is { } v) s.Value = v;
    }

    /// <summary>The value under a point given in track coordinates (null when the slider has no laid-out track).</summary>
    public static double? ValueAt(Slider s, Point trackPoint)
    {
        if (s.Template?.FindName("PART_Track", s) is not Track track) return null;
        var v = track.ValueFromPoint(trackPoint);
        return double.IsNaN(v) || double.IsInfinity(v) ? null : Math.Clamp(v, s.Minimum, s.Maximum);
    }
}
