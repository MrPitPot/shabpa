using System.Windows;
using System.Windows.Media;
using Shabpa.Core;

namespace Shabpa.Kit;

/// <summary>The medal art: an eight-pointed star, a coin inside it and the medal's icon. It is artwork, not UI chrome, so its
/// colours are fixed (bronze, silver, gold and the purple of the hidden medals) and look the same in every theme. One
/// <c>OnRender</c> with frozen brushes and pens; no children, no effects.</summary>
public sealed class MedalGlyph : FrameworkElement
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(IconName), typeof(string), typeof(MedalGlyph),
        new FrameworkPropertyMetadata("star", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TierProperty = DependencyProperty.Register(nameof(Tier), typeof(MedalTier), typeof(MedalGlyph),
        new FrameworkPropertyMetadata(MedalTier.Bronze, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty UnlockedProperty = DependencyProperty.Register(nameof(Unlocked), typeof(bool), typeof(MedalGlyph),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty LockedIconProperty = DependencyProperty.Register(nameof(ShowLock), typeof(bool), typeof(MedalGlyph),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public string IconName { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public MedalTier Tier { get => (MedalTier)GetValue(TierProperty); set => SetValue(TierProperty, value); }
    public bool Unlocked { get => (bool)GetValue(UnlockedProperty); set => SetValue(UnlockedProperty, value); }
    /// <summary>Hidden medals that are still locked show a padlock instead of their icon.</summary>
    public bool ShowLock { get => (bool)GetValue(LockedIconProperty); set => SetValue(LockedIconProperty, value); }

    public MedalGlyph() { FlowDirection = FlowDirection.LeftToRight; IsHitTestVisible = false; }

    public MedalGlyph(MedalDef medal, bool unlocked, double size) : this()
    {
        IconName = medal.Icon;
        Tier = medal.Tier;
        Unlocked = unlocked;
        ShowLock = !unlocked && medal.Hidden;
        Width = Height = size;
    }

    static MedalGlyph()
    {
        WidthProperty.OverrideMetadata(typeof(MedalGlyph), new FrameworkPropertyMetadata(72.0));
        HeightProperty.OverrideMetadata(typeof(MedalGlyph), new FrameworkPropertyMetadata(72.0));
    }

    private sealed record Palette(Brush RimA, Brush RimB, Brush Icon, Brush Spark);

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static Brush Frozen(Brush b) { b.Freeze(); return b; }

    private static LinearGradientBrush Grad(string a, string b) =>
        (LinearGradientBrush)Frozen(new LinearGradientBrush(C(a), C(b), new Point(0, 0), new Point(1, 1)));

    private static Palette Make(string rimA, string rimB, string icon, string spark) =>
        new(Grad(rimA, rimB), Grad(rimB, rimA), Frozen(new SolidColorBrush(C(icon))), Frozen(new SolidColorBrush(C(spark))));

    private static readonly Palette Bronze = Make("#F0A35E", "#9B5B33", "#F4A862", "#FFE3B8");
    private static readonly Palette Silver = Make("#D5D8F5", "#8D91BE", "#E9EBFF", "#FFFFFF");
    private static readonly Palette Gold = Make("#FFD56E", "#E3A02B", "#FFD56E", "#FFF1C2");
    private static readonly Palette Platinum = Make("#A6F5FF", "#2FA3C4", "#9FF3FF", "#E8FDFF");
    private static readonly Palette Diamond = Make("#B7D2FF", "#4A74F2", "#B5D0FF", "#FFFFFF");
    private static readonly Palette Legendary = Make("#FF8AE0", "#7B4DFF", "#FFB3EE", "#FFF0FB");
    private static readonly Palette Special = Make("#BBA3FF", "#7A5BE0", "#D2C1FF", "#F0E8FF");
    private static readonly Palette Locked = Make("#555A86", "#3B3F66", "#6E739E", "#6E739E");

    private static readonly Brush CoinFill = Frozen(new LinearGradientBrush(C("#2A2F78"), C("#161947"), new Point(0.2, 0), new Point(0.8, 1)));
    private static readonly Brush StarFill = Frozen(new LinearGradientBrush(C("#1F2360"), C("#12153C"), new Point(0, 0), new Point(1, 1)));
    private static readonly Brush CoinFillLocked = Frozen(new SolidColorBrush(C("#1B1E4A")));

    private static readonly Geometry Star = BuildStar();
    private static readonly Geometry Sparkle = BuildSparkle();

    private static Geometry BuildStar()
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            for (var i = 0; i < 16; i++)
            {
                var a = (-90 + i * 22.5) * Math.PI / 180;
                var r = i % 2 == 0 ? 47 : 37.5;
                var p = new Point(50 + r * Math.Cos(a), 50 + r * Math.Sin(a));
                if (i == 0) c.BeginFigure(p, true, true); else c.LineTo(p, true, true);
            }
        }
        g.Freeze();
        return g;
    }

    private static Geometry BuildSparkle()
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(0, -4.5), true, true);
            c.LineTo(new Point(1.2, -1.2), true, false);
            c.LineTo(new Point(4.5, 0), true, false);
            c.LineTo(new Point(1.2, 1.2), true, false);
            c.LineTo(new Point(0, 4.5), true, false);
            c.LineTo(new Point(-1.2, 1.2), true, false);
            c.LineTo(new Point(-4.5, 0), true, false);
            c.LineTo(new Point(-1.2, -1.2), true, false);
        }
        g.Freeze();
        return g;
    }

    private static readonly Dictionary<(Brush, double), Pen> Pens = [];

    private static Pen PenOf(Brush b, double w)
    {
        if (Pens.TryGetValue((b, w), out var p)) return p;
        p = new Pen(b, w) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        p.Freeze();
        return Pens[(b, w)] = p;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        var pal = !Unlocked ? (ShowLock ? Special : Locked) : Tier switch
        {
            MedalTier.Silver => Silver,
            MedalTier.Gold => Gold,
            MedalTier.Platinum => Platinum,
            MedalTier.Diamond => Diamond,
            MedalTier.Legendary => Legendary,
            MedalTier.Special => Special,
            _ => Bronze,
        };
        var s = size / 100.0;
        dc.PushTransform(new TranslateTransform((ActualWidth - size) / 2, (ActualHeight - size) / 2));
        dc.PushTransform(new ScaleTransform(s, s));

        dc.DrawGeometry(StarFill, PenOf(pal.RimA, 3.2), Star);
        dc.DrawEllipse(Unlocked ? CoinFill : CoinFillLocked, PenOf(pal.RimB, 2.4), new Point(50, 50), 28, 28);

        var iconName = ShowLock ? "lock" : IconName;
        if (Application.Current?.TryFindResource("I." + iconName) is Geometry geo)
        {
            const double box = 30;
            dc.PushTransform(new TranslateTransform(50 - box / 2, 50 - box / 2));
            dc.PushTransform(new ScaleTransform(box / 24, box / 24));
            dc.DrawGeometry(null, PenOf(pal.Icon, 1.7), geo);
            dc.Pop();
            dc.Pop();
        }

        if (Unlocked)
        {
            dc.PushTransform(new TranslateTransform(80, 17));
            dc.DrawGeometry(pal.Spark, null, Sparkle);
            dc.Pop();
        }
        dc.Pop();
        dc.Pop();
    }
}
