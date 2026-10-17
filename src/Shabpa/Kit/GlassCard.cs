using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Shabpa.Kit;

/// <summary>Layer 2 of the design: an acrylic card. A <see cref="Border"/> (so every place that expects a Border, such as
/// <c>Ui2.Card</c>, keeps working) that paints its own soft shadow outside its box with cheap layered rectangles, plus the
/// translucent fill and the rim light of the "Card" style. <see cref="Lift"/> adds the springy hover rise and the stronger
/// hover shadow that fades in; <see cref="Clicked"/> makes it a button.
/// <para>Rules: use it for top-level cards; inner surfaces use a plain Border with Style Card (flat) or Panel2/Line.</para></summary>
public class GlassCard : Border
{
    public static readonly DependencyProperty HoverGlowProperty = DependencyProperty.Register(nameof(HoverGlow), typeof(double), typeof(GlassCard),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty LiftProperty = DependencyProperty.Register(nameof(Lift), typeof(bool), typeof(GlassCard),
        new PropertyMetadata(false, (d, e) => ((GlassCard)d).LiftChanged((bool)e.NewValue)));
    public static readonly DependencyProperty ElevatedProperty = DependencyProperty.Register(nameof(Elevated), typeof(bool), typeof(GlassCard),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>0 to 1: how much of the hover shadow shows (animated by the card itself).</summary>
    public double HoverGlow { get => (double)GetValue(HoverGlowProperty); set => SetValue(HoverGlowProperty, value); }
    /// <summary>The card rises 3 px on hover (spring) and its shadow deepens.</summary>
    public bool Lift { get => (bool)GetValue(LiftProperty); set => SetValue(LiftProperty, value); }
    /// <summary>False: no shadow at all (cards nested in cards, dense lists).</summary>
    public bool Elevated { get => (bool)GetValue(ElevatedProperty); set => SetValue(ElevatedProperty, value); }
    /// <summary>When set the whole card is a button (hand cursor, press scale .96).</summary>
    public Action? Clicked { get; set; }

    public GlassCard()
    {
        if (Application.Current?.TryFindResource("Card") is Style style) Style = style;
        else CornerRadius = new CornerRadius(18);
        MouseLeftButtonUp += (_, e) => { if (Clicked is not null && IsMouseOver) { Clicked(); e.Handled = true; } };
    }

    private void LiftChanged(bool on)
    {
        if (!on) return;
        Fx.Attach(this, lift: 3, press: Clicked is null ? 1 : 0.96, onHover: Hover);
        if (Clicked is not null) Cursor = Cursors.Hand;
    }

    /// <summary>Call after setting <see cref="Clicked"/> if Lift was already on.</summary>
    public GlassCard OnClick(Action action)
    {
        Clicked = action;
        Cursor = Cursors.Hand;
        Fx.SetPress(this, 0.96);
        if (!Lift) Fx.Attach(this, 0, 0, 1, 0.96, Hover);
        return this;
    }

    private void Hover(bool on)
    {
        var from = HoverGlow;
        var to = on ? 1.0 : 0.0;
        HoverGlow = to;
        if (Fx.Enabled && Math.Abs(from - to) > 0.001)
            BeginAnimation(HoverGlowProperty, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(300)) { FillBehavior = FillBehavior.Stop });
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (Elevated && ActualWidth > 0 && ActualHeight > 0 && Application.Current is { } app)
        {
            var radius = CornerRadius.TopLeft;
            var glow = HoverGlow;
            var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            if (glow > 0.001 && app.TryFindResource("ShadowHoverC") is Color hover)
            {
                dc.PushOpacity(glow);
                Shadow.Draw(dc, ActualWidth, ActualHeight, hover, radius, 40, -16, 26, 9, true, dpi);
                dc.Pop();
            }
            if (app.TryFindResource("ShadowC") is Color baseColor)
                Shadow.Draw(dc, ActualWidth, ActualHeight, baseColor, radius, 30, -14, 14, 9, true, dpi);
            if (app.TryFindResource("ShadowTightC") is Color tight)
                Shadow.Draw(dc, ActualWidth, ActualHeight, tight, radius, 3, 0, 1, 3, true, dpi);
        }
        base.OnRender(dc);
    }
}
