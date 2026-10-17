using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Shabpa.Kit;

/// <summary>
/// A check mark that is drawn, never mirrored: the short stroke on the LEFT going down, the long stroke going up to the
/// RIGHT, in Persian and English alike. A tick is a picture, not text, so <see cref="FrameworkElement.FlowDirection"/> is
/// forced to LeftToRight (an RTL parent therefore cannot flip it) and the draw-in always starts at the left end of the short
/// stroke and finishes at the top right end of the long one.
/// <para>The geometry is the "chk" icon (24 x 24: M5 12.5 l4.5 4.5 L19 7). <see cref="Progress"/> (0..1) is the length of the
/// path that is visible, measured from the left start; the element draws only that part (two line segments, one pen, no
/// geometry allocation per frame beyond the open figure), so an animation is cheap and nothing runs once it has ended.</para>
/// <para><see cref="IsOn"/> turns the tick on or off: on = draw left to right (after <see cref="Delay"/> ms, so a box can spring
/// first), off = gone at once. With motion off (Settings, low-memory mode, Windows' animation switch) it simply shows the
/// finished tick. A tick that is already on when the control loads is shown finished, never replayed.</para>
/// </summary>
public sealed class TickMark : FrameworkElement
{
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(TickMark),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));
    public static readonly DependencyProperty WeightProperty = DependencyProperty.Register(nameof(Weight), typeof(double), typeof(TickMark),
        new FrameworkPropertyMetadata(2.4, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(nameof(Progress), typeof(double), typeof(TickMark),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty IsOnProperty = DependencyProperty.Register(nameof(IsOn), typeof(bool), typeof(TickMark),
        new FrameworkPropertyMetadata(false, (d, e) => ((TickMark)d).OnIsOnChanged((bool)e.NewValue)));

    public static readonly DependencyProperty PopTargetProperty = DependencyProperty.Register(nameof(PopTarget), typeof(FrameworkElement), typeof(TickMark), new PropertyMetadata(null));

    // the "chk" icon in its 24 x 24 box
    private static readonly Point A = new(5, 12.5), B = new(9.5, 17), C = new(19, 7);
    private static readonly double Len1 = (B - A).Length, Len2 = (C - B).Length, Total = Len1 + Len2;

    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    /// <summary>Stroke thickness in the 24-unit icon space (scaled with the element).</summary>
    public double Weight { get => (double)GetValue(WeightProperty); set => SetValue(WeightProperty, value); }
    /// <summary>0 = nothing, 1 = the whole tick; the part drawn runs from the left start towards the top right.</summary>
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    /// <summary>True shows the tick (drawn in when it changes while loaded), false hides it.</summary>
    public bool IsOn { get => (bool)GetValue(IsOnProperty); set => SetValue(IsOnProperty, value); }
    /// <summary>Optional element (the checkbox fill) that springs from 40% to full size when the tick turns on, before the tick is drawn.</summary>
    public FrameworkElement? PopTarget { get => (FrameworkElement?)GetValue(PopTargetProperty); set => SetValue(PopTargetProperty, value); }
    /// <summary>Milliseconds to wait before the draw-in starts (lets a box fill spring first).</summary>
    public int Delay { get; set; } = 110;
    /// <summary>Draw-in duration in milliseconds.</summary>
    public int DrawMs { get; set; } = 320;

    static TickMark()
    {
        WidthProperty.OverrideMetadata(typeof(TickMark), new FrameworkPropertyMetadata(20.0));
        HeightProperty.OverrideMetadata(typeof(TickMark), new FrameworkPropertyMetadata(20.0));
        FlowDirectionProperty.OverrideMetadata(typeof(TickMark), new FrameworkPropertyMetadata(FlowDirection.LeftToRight));
        IsHitTestVisibleProperty.OverrideMetadata(typeof(TickMark), new FrameworkPropertyMetadata(false));
    }

    public TickMark() { FlowDirection = FlowDirection.LeftToRight; }

    public TickMark(double size, Brush? foreground = null, double weight = 2.4, bool on = true) : this()
    {
        Width = Height = size; Weight = weight;
        if (foreground is not null) Foreground = foreground;
        if (on) { Progress = 1; SetCurrentValue(IsOnProperty, true); }
    }

    private void OnIsOnChanged(bool on)
    {
        BeginAnimation(ProgressProperty, null);
        if (!on) { Progress = 0; return; }
        if (!IsLoaded || !Fx.Enabled || !IsVisible) { Progress = 1; return; }
        Pop();
        Progress = 1; // the real value first; the animation only plays on top of it and leaves no clock behind
        Draw(Delay);
    }

    private static readonly IEasingFunction DrawEase = Freeze(new QuadraticEase { EasingMode = EasingMode.EaseOut });
    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    private void Pop()
    {
        if (PopTarget is not { } t) return;
        if (t.RenderTransform is not ScaleTransform st || st.IsFrozen) t.RenderTransform = st = new ScaleTransform(1, 1);
        st.ScaleX = st.ScaleY = 0.4;
        Fx.Anim(st, ScaleTransform.ScaleXProperty, 1);
        Fx.Anim(st, ScaleTransform.ScaleYProperty, 1);
    }

    // 0 is held through the delay (a plain BeginTime would show the finished tick until it starts), then 0 -> 1, left to right
    private void Draw(int delayMs)
    {
        var a = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        a.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delayMs + DrawMs)), DrawEase)); // eased between the previous key and this one
        if (delayMs > 0) a.KeyFrames.Insert(1, new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delayMs))));
        BeginAnimation(ProgressProperty, a);
    }

    /// <summary>Play the draw-in again (for success moments); with motion off it just shows the finished tick.</summary>
    public void Replay(int delayMs = 0)
    {
        BeginAnimation(ProgressProperty, null);
        Progress = 1;
        if (!Fx.Enabled || !IsLoaded) return;
        Draw(delayMs);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var p = Math.Clamp(Progress, 0, 1);
        if (p <= 0.001) return;
        var s = Math.Min(ActualWidth, ActualHeight) / 24.0;
        if (s <= 0) return;
        var shown = p * Total;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(A, false, false);
            if (shown <= Len1) c.LineTo(A + (B - A) * (shown / Len1), true, true);
            else
            {
                c.LineTo(B, true, true);
                c.LineTo(B + (C - B) * Math.Min(1, (shown - Len1) / Len2), true, true);
            }
        }
        g.Freeze();
        var pen = new Pen(Foreground, Weight) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        if (pen.CanFreeze) pen.Freeze();
        dc.PushTransform(new ScaleTransform(s, s));
        dc.DrawGeometry(null, pen, g);
        dc.Pop();
    }
}
