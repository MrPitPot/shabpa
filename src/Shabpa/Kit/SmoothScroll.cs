using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Shabpa.Kit;

/// <summary>Smooth, inertial wheel scrolling for every ScrollViewer in the app, installed once with <see cref="Install"/> (a class
/// handler, so no page has to opt in). The physics is in <see cref="SmoothScrollPhysics"/>. While nothing scrolls there is no
/// subscription and no timer; the frame callback exists only during a glide and is released when it ends.
/// Left alone on purpose: item-based scrolling (CanContentScroll on a virtualised list), touch panning (WPF's own manipulation),
/// the scrollbar thumb, and everything when <see cref="Motion.Enabled"/> is false. Precision-touchpad deltas are applied directly.</summary>
public static class SmoothScroll
{
    /// <summary>Set to false on a ScrollViewer that must keep WPF's default wheel behaviour.</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SmoothScroll), new PropertyMetadata(true));
    public static bool GetIsEnabled(DependencyObject o) => (bool)o.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject o, bool v) => o.SetValue(IsEnabledProperty, v);

    /// <summary>For a strip that only scrolls sideways (the hourly weather): the plain wheel moves it horizontally.</summary>
    public static readonly DependencyProperty HorizontalWheelProperty = DependencyProperty.RegisterAttached(
        "HorizontalWheel", typeof(bool), typeof(SmoothScroll), new PropertyMetadata(false));
    public static bool GetHorizontalWheel(DependencyObject o) => (bool)o.GetValue(HorizontalWheelProperty);
    public static void SetHorizontalWheel(DependencyObject o, bool v) => o.SetValue(HorizontalWheelProperty, v);

    private sealed class Axis
    {
        public SmoothScrollPhysics.State State;
        public double Target, LastApplied, PrevApplied;
        public bool Active;
    }

    private sealed class Glide { public readonly Axis V = new(), H = new(); }

    private static readonly ConditionalWeakTable<ScrollViewer, Glide> Glides = new();
    // The frame hook belongs to one dispatcher (the UI thread), hence thread-static; tests run several STA threads.
    [ThreadStatic] private static List<WeakReference<ScrollViewer>>? _running;
    [ThreadStatic] private static bool _hooked;
    [ThreadStatic] private static TimeSpan _last;
    private static bool _installed;
    private static List<WeakReference<ScrollViewer>> Running => _running ??= [];

    /// <summary>Frames processed while a glide was running (a measurement aid for probes).</summary>
    [ThreadStatic] private static int _frames;
    public static int FramesRendered => _frames;

    /// <summary>True while the frame callback is subscribed; false when idle (the zero-cost state).</summary>
    public static bool IsRunning => _hooked;

    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.MouseWheelEvent, new MouseWheelEventHandler(OnWheel));
    }

    /// <summary>Wheel scrolling on a virtualised list scrolls in items, not pixels; the glide only knows pixels, so it leaves those alone.</summary>
    private static bool IsItemBased(ScrollViewer sv) => sv.CanContentScroll && sv.Content is ItemsPresenter or VirtualizingPanel;

    private static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not ScrollViewer sv || !GetIsEnabled(sv) || !Motion.Enabled || IsItemBased(sv)) return;

        var horizontal = GetHorizontalWheel(sv) || (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        var max = horizontal ? sv.ScrollableWidth : sv.ScrollableHeight;
        if (max <= 0) { HandOff(sv, e); return; }       // nothing to scroll here (WPF would still swallow the wheel): the parent gets it

        var glide = Glides.GetOrCreateValue(sv);
        var axis = horizontal ? glide.H : glide.V;
        var offset = horizontal ? sv.HorizontalOffset : sv.VerticalOffset;
        Sync(axis, offset);

        if (SmoothScrollPhysics.IsPrecise(e.Delta))
        {
            if (!SmoothScrollPhysics.ShouldHandle(offset, max, e.Delta, false)) { HandOff(sv, e); return; }
            var next = Math.Clamp(offset - e.Delta / 120.0 * SmoothScrollPhysics.PixelsPerNotch, 0, max);
            axis.Active = false;
            axis.Target = next;
            axis.State = new(next, 0);
            Apply(sv, horizontal, axis, next);
            e.Handled = true;
            return;
        }

        if (!SmoothScrollPhysics.ShouldHandle(axis.Target, max, e.Delta, axis.Active)) { HandOff(sv, e); return; }
        axis.Target = SmoothScrollPhysics.AddWheel(axis.Target, e.Delta, max);
        axis.Active = true;
        e.Handled = true;
        Start(sv);
    }

    /// <summary>WPF's own ScrollViewer swallows the wheel even at its end, so a list inside a page would trap it. At the end the
    /// event is re-raised on the parent instead, so the outer viewer carries on.</summary>
    private static void HandOff(ScrollViewer sv, MouseWheelEventArgs e)
    {
        e.Handled = true;
        if (VisualTreeHelper.GetParent(sv) is not UIElement parent) return;
        parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = sv });
    }

    /// <summary>If somebody else moved the offset (scrollbar drag, keyboard, caret) the glide forgets its target and follows.</summary>
    private static void Sync(Axis axis, double offset)
    {
        if (axis.Active && IsOwn(axis, offset)) return;
        axis.Active = false;
        axis.State = new(offset, 0);
        axis.Target = offset;
        axis.LastApplied = axis.PrevApplied = offset;
    }

    /// <summary>The viewer shows the offset one layout pass late, so the value read back is one we set a moment ago (this or the previous frame).</summary>
    private static bool IsOwn(Axis axis, double offset)
        => Math.Abs(offset - axis.LastApplied) <= 1.5 || Math.Abs(offset - axis.PrevApplied) <= 1.5;

    private static void Apply(ScrollViewer sv, bool horizontal, Axis axis, double value)
    {
        if (horizontal) sv.ScrollToHorizontalOffset(value); else sv.ScrollToVerticalOffset(value);
        axis.PrevApplied = axis.LastApplied;
        axis.LastApplied = value;
    }

    private static void Start(ScrollViewer sv)
    {
        if (!Running.Any(w => w.TryGetTarget(out var s) && ReferenceEquals(s, sv))) Running.Add(new(sv));
        if (_hooked) return;
        _hooked = true;
        _last = TimeSpan.MinValue;
        CompositionTarget.Rendering += OnFrame;
    }

    private static void OnFrame(object? sender, EventArgs e)
    {
        var now = e is RenderingEventArgs r ? r.RenderingTime : TimeSpan.Zero;
        if (now == _last) return;                      // WPF can raise Rendering twice for one frame
        var dt = _last == TimeSpan.MinValue ? 1.0 / 60 : (now - _last).TotalSeconds;
        _last = now;
        _frames++;
        for (var i = Running.Count - 1; i >= 0; i--)
        {
            if (!Running[i].TryGetTarget(out var sv) || !sv.IsLoaded || !Glides.TryGetValue(sv, out var g)) { Running.RemoveAt(i); continue; }
            var busy = Tick(sv, g.V, false, dt) | Tick(sv, g.H, true, dt);
            if (!busy) Running.RemoveAt(i);
        }
        if (Running.Count == 0) { CompositionTarget.Rendering -= OnFrame; _hooked = false; }
    }

    private static bool Tick(ScrollViewer sv, Axis axis, bool horizontal, double dt)
    {
        if (!axis.Active) return false;
        var offset = horizontal ? sv.HorizontalOffset : sv.VerticalOffset;
        if (!IsOwn(axis, offset)) { axis.Active = false; return false; }   // moved by someone else: let go
        if (!Motion.Enabled) { Apply(sv, horizontal, axis, axis.Target); axis.Active = false; return false; }
        axis.State = SmoothScrollPhysics.Step(axis.State, axis.Target, dt);
        Apply(sv, horizontal, axis, axis.State.Position);
        if (!SmoothScrollPhysics.AtRest(axis.State, axis.Target)) return true;
        axis.Active = false;
        return false;
    }
}
