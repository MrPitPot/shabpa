using System.Windows;
using System.Windows.Media;
using Shabpa.Core;

namespace Shabpa.Kit;

/// <summary>A medal drawn in code: the eight-pointed Persian star (shamseh) of the supporter badge in the tier's metal,
/// a night-blue disc and the medal's icon in the middle. Locked medals are grey with a lock (or a "?" for secret ones).
/// Fixed brand colours on purpose: a medal looks the same in every theme, like a real one.</summary>
public sealed class MedalArt : FrameworkElement
{
    private readonly Medal? _medal;
    private readonly bool _earned;

    private static readonly Dictionary<MedalTier, (Color light, Color dark)> Metal = new()
    {
        [MedalTier.Bronze] = (Color.FromRgb(0xF2, 0xB0, 0x7A), Color.FromRgb(0xB0, 0x63, 0x34)),
        [MedalTier.Silver] = (Color.FromRgb(0xE4, 0xE6, 0xFA), Color.FromRgb(0x8F, 0x93, 0xD8)),
        [MedalTier.Gold] = (Color.FromRgb(0xFF, 0xE2, 0xA0), Color.FromRgb(0xF7, 0xA9, 0x3B)),
        [MedalTier.Special] = (Color.FromRgb(0xC9, 0xA8, 0xFF), Color.FromRgb(0x7D, 0x5A, 0xE0)),
    };
    private static readonly (Color light, Color dark) Locked = (Color.FromRgb(0x6B, 0x6B, 0x86), Color.FromRgb(0x3A, 0x3A, 0x52));
    private static readonly Color Night1 = Color.FromRgb(0x3D, 0x3B, 0x94), Night2 = Color.FromRgb(0x0D, 0x0E, 0x24);

    public MedalArt(Medal? medal, bool earned, double size)
    {
        _medal = medal;
        _earned = earned;
        Width = Height = size;
        FlowDirection = FlowDirection.LeftToRight;
        IsHitTestVisible = false;
    }

    public static Color TierColor(MedalTier tier) => Metal[tier].dark;

    private static StreamGeometry Star(Point c, double ro, double ri)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            for (var i = 0; i < 16; i++)
            {
                var r = i % 2 == 0 ? ro : ri;
                var a = (-90 + i * 22.5) * Math.PI / 180;
                var p = new Point(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
                if (i == 0) ctx.BeginFigure(p, true, true); else ctx.LineTo(p, true, false);
            }
        }
        g.Freeze();
        return g;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        var c = new Point(size / 2, size / 2);
        var (light, dark) = _earned && _medal is not null ? Metal[_medal.Tier] : Locked;
        var metal = new LinearGradientBrush(light, dark, new Point(0, 0), new Point(1, 1));
        var night = new RadialGradientBrush(Night1, Night2) { GradientOrigin = new Point(0.5, 0.35), Center = new Point(0.5, 0.42), RadiusX = 0.6, RadiusY = 0.6 };
        if (!_earned) night = new RadialGradientBrush(Color.FromRgb(0x34, 0x34, 0x4A), Color.FromRgb(0x16, 0x16, 0x24));
        var ro = size * 0.48;
        const double k = 0.7654; // two overlapped squares
        dc.DrawGeometry(metal, new Pen(metal, size * 0.02) { LineJoin = PenLineJoin.Round }, Star(c, ro, ro * k));
        var inner = ro - size * 0.045;
        dc.DrawGeometry(night, null, Star(c, inner, inner * k));
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(0x70, light.R, light.G, light.B)), Math.Max(0.6, size * 0.006)), Star(c, inner - size * 0.05, (inner - size * 0.05) * k));
        var disc = size * 0.27;
        dc.DrawEllipse(night, new Pen(metal, Math.Max(1, size * 0.022)), c, disc, disc);

        var glyph = !_earned ? (_medal is { Secret: true } || _medal is null ? null : "lock") : _medal!.Icon;
        var ink = new SolidColorBrush(_earned ? light : Color.FromRgb(0x9A, 0x9A, 0xB4));
        if (glyph is not null && Application.Current?.TryFindResource("I." + glyph) is Geometry geometry)
        {
            var box = disc * 1.15;
            var s = box / 24.0;
            var pen = new Pen(ink, 2.0) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            dc.PushTransform(new TranslateTransform(c.X - box / 2, c.Y - box / 2));
            dc.PushTransform(new ScaleTransform(s, s));
            dc.DrawGeometry(null, pen, geometry);
            dc.Pop();
            dc.Pop();
        }
        else
        {
            var text = new FormattedText("?", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), disc * 1.1, ink, 1.0);
            dc.DrawText(text, new Point(c.X - text.Width / 2, c.Y - text.Height / 2));
        }
        if (_earned && size >= 48)
        {
            // one small sparkle on the upper right point, the brand's star
            var sp = new Point(c.X + ro * 0.52, c.Y - ro * 0.6);
            var r = size * 0.05;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(sp.X, sp.Y - r), true, true);
                ctx.QuadraticBezierTo(new Point(sp.X + r * 0.15, sp.Y - r * 0.15), new Point(sp.X + r, sp.Y), true, false);
                ctx.QuadraticBezierTo(new Point(sp.X + r * 0.15, sp.Y + r * 0.15), new Point(sp.X, sp.Y + r), true, false);
                ctx.QuadraticBezierTo(new Point(sp.X - r * 0.15, sp.Y + r * 0.15), new Point(sp.X - r, sp.Y), true, false);
                ctx.QuadraticBezierTo(new Point(sp.X - r * 0.15, sp.Y - r * 0.15), new Point(sp.X, sp.Y - r), true, false);
            }
            dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0xFF, 0xF6, 0xDC)), null, g);
        }
    }
}
