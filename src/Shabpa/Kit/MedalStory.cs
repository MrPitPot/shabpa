using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Shabpa.Core;

namespace Shabpa.Kit;

/// <summary>What a story card says. Everything comes from this PC's own rewards; the texts follow the app language (Persian cards
/// are all Persian, English cards all English).</summary>
public sealed record StoryData(MedalDef Medal, DateTime When, int Earned, int Total, int Rank, int Level, string LevelName, int Streak, int Feathers,
    int MonthMedals, int MonthDays, string Name);

public sealed record StoryDesign(string Key, string FaName, string EnName, string FaHint, string EnHint)
{
    public string Name => L.T(FaName, EnName);
    public string Hint => L.T(FaHint, EnHint);
}

/// <summary>Ten story designs (1080×1920) for a medal. Each is drawn in code from the medal art, so it is sharp at any size, uses
/// the colour of the medal's tier, and needs no image files. <see cref="Build"/> makes a 540×960 element; <see cref="Save"/> renders it at 2×.</summary>
public static class MedalStory
{
    public const double W = 540, H = 960;

    public static readonly StoryDesign[] Designs =
    [
        new("spotlight", "نورافکن", "Spotlight", "نور از بالا روی مدالت", "A beam of light on your medal"),
        new("card", "کارت کلکسیونی", "Collector card", "کارت شماره‌دار با درخشش رنگین‌کمانی", "A numbered card with a holographic shine"),
        new("bidar", "تبریک بوفی", "Boofi's cheer", "بوفی برات دست می‌زنه", "Boofi cheers for you"),
        new("poster", "پوستر مینیمال", "Minimal poster", "تایپوگرافی درشت، ساده و شیک", "Big type, simple and sharp"),
        new("stars", "صورت فلکی", "Constellation", "هر مدال یه ستاره تو آسمونته", "Every medal is a star in your sky"),
        new("month", "خلاصه‌ی ماه", "Month in review", "آمار واقعی این ماهت", "Your real numbers this month"),
        new("tile", "کاشی ایرانی", "Persian tile", "محراب و کاشی‌کاری هشت‌پر", "An arch and eight-point tiles"),
        new("arcade", "آرکید گیمی", "Arcade", "مثل دستاوردهای بازی", "Like a game achievement"),
        new("screen", "عکس از صفحه", "Screen photo", "همون نوتیف روی مانیتور", "The notice, right on a monitor"),
        new("certificate", "گواهی افتخار", "Certificate", "گواهی امضاشده‌ی بوفی", "A certificate signed by Boofi"),
    ];

    public static StoryData DataFor(MedalDef medal, DateTime when)
    {
        var hub = Hub.I;
        var r = hub.Rewards.State;
        var level = hub.Rewards.Level;
        var rank = r.Medals.Count(m => m.Value <= when);
        var monthStart = Fa.MonthStart(DateTime.Now);
        return new StoryData(medal, when, r.Medals.Count, RewardsCatalog.Medals.Count, Math.Max(1, rank), level.Level, level.Name, hub.Rewards.Streak, r.Balance,
            r.Medals.Count(m => m.Value >= monthStart),
            r.Days.Count(d => DateTime.TryParseExact(d, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var t) && t >= monthStart),
            hub.Settings.Birthday.Name);
    }

    // ---- small drawing helpers ---------------------------------------------------------------------------------------

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static SolidColorBrush Br(string hex, double opacity = 1) => Br(C(hex), opacity);

    private static SolidColorBrush Br(Color c, double opacity = 1)
    {
        var b = new SolidColorBrush(c) { Opacity = opacity };
        b.Freeze();
        return b;
    }

    private static LinearGradientBrush Lg(Color a, Color b, double x1 = 0, double y1 = 0, double x2 = 1, double y2 = 1) => new(a, b, new Point(x1, y1), new Point(x2, y2));

    private static (Color A, Color B) Tier(MedalTier t) => t switch
    {
        MedalTier.Bronze => (C("#F0A35E"), C("#9B5B33")),
        MedalTier.Silver => (C("#D5D8F5"), C("#8D91BE")),
        MedalTier.Gold => (C("#FFD56E"), C("#E3A02B")),
        MedalTier.Platinum => (C("#A6F5FF"), C("#2FA3C4")),
        MedalTier.Diamond => (C("#B7D2FF"), C("#4A74F2")),
        MedalTier.Legendary => (C("#FF8AE0"), C("#7B4DFF")),
        _ => (C("#BBA3FF"), C("#7A5BE0")),
    };

    private static TextBlock T(string text, double size, Brush fg, bool bold = false, double width = W, TextAlignment align = TextAlignment.Center, double lineHeight = 0)
    {
        var tb = new TextBlock
        {
            Text = text, FontSize = size, Foreground = fg, FontWeight = bold ? FontWeights.Bold : FontWeights.Medium, Width = width, TextAlignment = align,
            TextWrapping = TextWrapping.Wrap, FlowDirection = Ui.Flow,
        };
        if (lineHeight > 0) { tb.LineHeight = lineHeight; tb.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; }
        return tb;
    }

    private static void At(UIElement e, double x, double y)
    {
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
    }

    private static Canvas Root(Brush background)
    {
        var c = new Canvas { Width = W, Height = H, Background = background, ClipToBounds = true, FlowDirection = FlowDirection.LeftToRight, SnapsToDevicePixels = true };
        TextElement.SetFontFamily(c, (FontFamily)Application.Current.Resources["Ui"]);
        TextOptions.SetTextFormattingMode(c, TextFormattingMode.Ideal);
        return c;
    }

    private static void Glow(Canvas c, double cx, double cy, double r, Color color, double opacity)
    {
        var e = new Ellipse
        {
            Width = r * 2, Height = r * 2, IsHitTestVisible = false,
            Fill = new RadialGradientBrush(Color.FromArgb((byte)(255 * opacity), color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B)),
        };
        At(e, cx - r, cy - r);
        c.Children.Add(e);
    }

    private static void Sparkles(Canvas c, int seed, int count, Rect area, Color color, double maxSize = 7)
    {
        var rnd = new Random(seed);
        for (var i = 0; i < count; i++)
        {
            var s = 1.5 + rnd.NextDouble() * maxSize;
            var p = new Path { Data = Spark(s), Fill = Br(color, 0.35 + rnd.NextDouble() * 0.6), IsHitTestVisible = false };
            At(p, area.Left + rnd.NextDouble() * area.Width, area.Top + rnd.NextDouble() * area.Height);
            c.Children.Add(p);
        }
    }

    private static Geometry Spark(double s)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(0, -s), true, true);
            foreach (var p in new[] { new Point(s * 0.25, -s * 0.25), new Point(s, 0), new Point(s * 0.25, s * 0.25), new Point(0, s), new Point(-s * 0.25, s * 0.25), new Point(-s, 0), new Point(-s * 0.25, -s * 0.25) })
                ctx.LineTo(p, true, false);
        }
        g.Freeze();
        return g;
    }

    private static Geometry Star8(double cx, double cy, double outer, double inner)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            for (var i = 0; i < 16; i++)
            {
                var a = (-90 + i * 22.5) * Math.PI / 180;
                var r = i % 2 == 0 ? outer : inner;
                var p = new Point(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
                if (i == 0) ctx.BeginFigure(p, false, true); else ctx.LineTo(p, true, false);
            }
        }
        g.Freeze();
        return g;
    }

    /// <summary>The shared footer: the site, the name, a line about the app. On the right in Persian, on the left in English.</summary>
    private static void Footer(Canvas c, bool dark = true, double y = 852)
    {
        var ink = dark ? Br("#FFFFFF") : Br("#1B1946");
        var soft = dark ? Br("#A9ADDB") : Br("#5B5E86");
        var plate = new Border
        {
            Width = 460, Height = 72, CornerRadius = new CornerRadius(20), Background = dark ? Br("#FFFFFF", 0.07) : Br("#1B1946", 0.07),
            BorderBrush = dark ? Br("#FFFFFF", 0.14) : Br("#1B1946", 0.14), BorderThickness = new Thickness(1),
        };
        At(plate, 40, y);
        c.Children.Add(plate);
        var rtl = !L.En;
        double Px(double offsetFromStart, double width) => rtl ? 40 + 460 - offsetFromStart - width : 40 + offsetFromStart;
        var moon = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(22), Background = dark ? Br("#FFFFFF", 0.1) : Br("#1B1946"), Child = new Icon("moon", 24, Br("#FFD56E"), 1.8) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        At(moon, Px(14, 44), y + 14);
        c.Children.Add(moon);
        var align = rtl ? TextAlignment.Right : TextAlignment.Left;
        var title = T(L.T("شبپا", "Shabpa"), 19, ink, true, 250, align);
        At(title, Px(70, 250), y + 10);
        c.Children.Add(title);
        var sub = T(L.T("دستیار رایگان ویندوز برای شب‌بیدارها", "Free Windows companion for night owls"), 11, soft, false, 250, align);
        At(sub, Px(70, 250), y + 40);
        c.Children.Add(sub);
        var chip = new Border { CornerRadius = new CornerRadius(10), Background = Br("#FFD56E"), Padding = new Thickness(11, 5, 11, 5), Child = new TextBlock { Text = "shabpa.ir", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Br("#1B1946"), FlowDirection = FlowDirection.LeftToRight } };
        At(chip, Px(460 - 14 - 92, 92), y + 22);
        c.Children.Add(chip);
    }

    private static string Num(int n) => Fa.N(n);

    /// <summary>Soft light rays fanning out of a point (behind a medal).</summary>
    private static void Rays(Canvas c, double cx, double cy, int count, double length, Color color, double opacity = 0.08, double phase = 0)
    {
        for (var i = 0; i < count; i++)
        {
            var a = phase + i * 2 * Math.PI / count;
            var w = Math.PI / count * 0.5;
            c.Children.Add(new Polygon
            {
                Points = [new Point(cx, cy), new Point(cx + length * Math.Cos(a - w), cy + length * Math.Sin(a - w)), new Point(cx + length * Math.Cos(a + w), cy + length * Math.Sin(a + w))],
                Fill = Br(color, opacity), IsHitTestVisible = false,
            });
        }
    }

    /// <summary>Little paper strips and dots thrown over an area.</summary>
    private static void Confetti(Canvas c, int seed, int count, Rect area, params Color[] colors)
    {
        var rnd = new Random(seed);
        for (var i = 0; i < count; i++)
        {
            var col = colors[rnd.Next(colors.Length)];
            Shape piece = rnd.Next(4) == 0
                ? new Ellipse { Width = 4 + rnd.NextDouble() * 5, Height = 4 + rnd.NextDouble() * 5 }
                : new Rectangle { Width = 7 + rnd.NextDouble() * 9, Height = 3 + rnd.NextDouble() * 3, RadiusX = 1, RadiusY = 1 };
            piece.Fill = Br(col, 0.55 + rnd.NextDouble() * 0.45);
            piece.RenderTransformOrigin = new Point(0.5, 0.5);
            piece.RenderTransform = new RotateTransform(rnd.NextDouble() * 180);
            piece.IsHitTestVisible = false;
            At(piece, area.Left + rnd.NextDouble() * area.Width, area.Top + rnd.NextDouble() * area.Height);
            c.Children.Add(piece);
        }
    }

    /// <summary>Two sprigs of laurel leaves around a point, open at the top.</summary>
    private static void Laurel(Canvas c, double cx, double cy, double r, Color color, int leaves = 9)
    {
        foreach (var side in new[] { -1, 1 })
            for (var i = 0; i < leaves; i++)
            {
                var theta = (18 + i * (118.0 / (leaves - 1))) * Math.PI / 180;
                var x = cx + side * r * Math.Sin(theta);
                var y = cy + r * Math.Cos(theta);
                var tangent = Math.Atan2(-Math.Sin(theta), side * Math.Cos(theta)) * 180 / Math.PI;
                var size = 1.0 - i * 0.045;
                var leaf = new Ellipse { Width = 26 * size, Height = 10 * size, Fill = Br(color, 0.85 - i * 0.04), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(tangent + side * 28), IsHitTestVisible = false };
                At(leaf, x - 13 * size, y - 5 * size);
                c.Children.Add(leaf);
            }
    }

    /// <summary>A folded banner behind a title, drawn in the colours of the medal's tier.</summary>
    private static void Ribbon(Canvas c, double cx, double y, double w, double h, Color a, Color b)
    {
        var tail = new Func<double, Polygon>(dir => new Polygon
        {
            Points = [new Point(0, 12), new Point(34, 12), new Point(34, h + 12), new Point(0, h + 12), new Point(dir < 0 ? 14 : 20, 12 + h / 2)],
            Fill = Lg(b, a, 0, 0, 1, 1),
        });
        var left = tail(-1);
        At(left, cx - w / 2 - 28, y);
        c.Children.Add(left);
        var right = tail(1);
        right.RenderTransformOrigin = new Point(0.5, 0.5);
        right.RenderTransform = new ScaleTransform(-1, 1);
        At(right, cx + w / 2 - 6, y);
        c.Children.Add(right);
        foreach (var fold in new[] { cx - w / 2 + 6, cx + w / 2 - 6 })
        {
            var tri = new Polygon { Points = [new Point(0, 0), new Point(0, 12), new Point(fold < cx ? -22 : 22, 12)], Fill = Br(b, 0.9) };
            At(tri, fold, y + h);
            c.Children.Add(tri);
        }
        var main = new Border { Width = w, Height = h, Background = Lg(a, b, 0, 0, 1, 1), CornerRadius = new CornerRadius(4) };
        At(main, cx - w / 2, y);
        c.Children.Add(main);
        var gloss = new Rectangle { Width = w, Height = h / 2, Fill = Br("#FFFFFF", 0.18), RadiusX = 4, RadiusY = 4 };
        At(gloss, cx - w / 2, y);
        c.Children.Add(gloss);
    }

    /// <summary>A round sticker with the feathers the medal paid.</summary>
    private static void FeatherSticker(Canvas c, double x, double y, StoryData d, Color a, Color b, double tilt = 12)
    {
        var sticker = new Canvas { Width = 92, Height = 92, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(tilt) };
        sticker.Children.Add(new Ellipse { Width = 92, Height = 92, Fill = Lg(a, b), Stroke = Br("#FFFFFF", 0.85), StrokeThickness = 3 });
        sticker.Children.Add(new Ellipse { Width = 78, Height = 78, Stroke = Br("#1B1946", 0.35), StrokeThickness = 1.2, StrokeDashArray = [2, 2] });
        var plus = T($"+{Num(d.Medal.Reward)}", 25, Br("#1B1946"), true, 92);
        plus.FlowDirection = FlowDirection.LeftToRight;
        At(plus, 0, 20);
        sticker.Children.Add(plus);
        var unit = T(L.T("پَر", "feathers"), L.En ? 10.5 : 13, Br("#1B1946"), true, 92);
        At(unit, 0, 55);
        sticker.Children.Add(unit);
        At(sticker, x, y);
        c.Children.Add(sticker);
    }

    private static void Rings(Canvas c, double cx, double cy, Color color, params double[] radii)
    {
        var first = true;
        foreach (var r in radii)
        {
            var ring = new Ellipse { Width = r * 2, Height = r * 2, Stroke = Br(color, first ? 0.5 : 0.25), StrokeThickness = 1.2, StrokeDashArray = first ? [3, 5] : [1, 7], IsHitTestVisible = false };
            At(ring, cx - r, cy - r);
            c.Children.Add(ring);
            first = false;
        }
    }

    /// <summary>The largest font size (not above <paramref name="start"/>) at which the longest word of the text still fits the width,
    /// so a big name is never broken in the middle of a word.</summary>
    private static double Fit(string text, double maxWidth, double start, double min, FontWeight weight)
    {
        var face = new Typeface((FontFamily)Application.Current.Resources["Ui"], FontStyles.Normal, weight, FontStretches.Normal);
        var longest = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).OrderByDescending(w => w.Length).FirstOrDefault() ?? text;
        for (var size = start; size > min; size -= 2)
        {
            var ft = new FormattedText(longest + ".", System.Globalization.CultureInfo.CurrentUICulture, Ui.Flow, face, size, Brushes.Black, 1.0);
            if (ft.Width <= maxWidth) return size;
        }
        return min;
    }

    private static string DateText(StoryData d) => Fa.Date(d.When);

    // ---- the ten designs --------------------------------------------------------------------------------------------

    public static FrameworkElement Build(string key, StoryData d) => key switch
    {
        "card" => Card(d),
        "bidar" => BidarCheer(d),
        "poster" => Poster(d),
        "stars" => Constellation(d),
        "month" => Month(d),
        "tile" => TileArch(d),
        "arcade" => Arcade(d),
        "screen" => ScreenPhoto(d),
        "certificate" => Certificate(d),
        _ => Spotlight(d),
    };

    private static Canvas NightBackground(Color accent)
    {
        var c = Root(Lg(C("#161A4E"), C("#080A24"), 0.3, 0, 0.7, 1));
        Glow(c, 270, 330, 330, accent, 0.18);
        Sparkles(c, 11, 26, new Rect(10, 10, 520, 780), C("#D9DCFF"), 3.5);
        return c;
    }

    private static Canvas Spotlight(StoryData d)
    {
        var (a, b) = Tier(d.Medal.Tier);
        var c = NightBackground(b);
        // the beam: a soft wedge from the top, brighter near the medal
        var beam = new Polygon { Points = [new Point(230, 0), new Point(310, 0), new Point(450, 560), new Point(90, 560)], Fill = Lg(Color.FromArgb(120, a.R, a.G, a.B), Color.FromArgb(0, a.R, a.G, a.B), 0.5, 0, 0.5, 1) };
        c.Children.Add(beam);
        var floor = new Ellipse { Width = 320, Height = 50, Fill = new RadialGradientBrush(Color.FromArgb(150, a.R, a.G, a.B), Color.FromArgb(0, a.R, a.G, a.B)) };
        At(floor, 110, 598);
        c.Children.Add(floor);
        Glow(c, 270, 400, 210, a, 0.30);
        Rays(c, 270, 400, 20, 560, a, 0.06);
        Rings(c, 270, 400, a, 196, 214, 236);
        Laurel(c, 270, 405, 178, a);
        Confetti(c, 3, 46, new Rect(24, 24, 492, 640), a, b, C("#FFFFFF"), C("#FF8AE0"), C("#7CF0C8"));
        var frame = new Border { Width = 508, Height = 928, CornerRadius = new CornerRadius(34), BorderThickness = new Thickness(3), BorderBrush = Lg(a, b), Opacity = 0.9 };
        At(frame, 16, 16);
        c.Children.Add(frame);
        var label = T(L.T("مدال جدید گرفتم", "I just earned a medal"), 17, Br(a), true);
        At(label, 0, 78);
        c.Children.Add(label);
        var glyph = new MedalGlyph(d.Medal, true, 330);
        At(glyph, 105, 235);
        c.Children.Add(glyph);
        Sparkles(c, 5, 16, new Rect(90, 220, 360, 360), Colors.White, 9);
        Ribbon(c, 270, 612, 400, 70, a, b);
        var name = T(d.Medal.Name, d.Medal.Name.Length > 12 ? 34 : 46, Br("#1B1946"), true, 400);
        At(name, 70, 620);
        c.Children.Add(name);
        var desc = T(d.Medal.Desc, 17, Br("#B6BAE6"), false, 420, TextAlignment.Center, 27);
        At(desc, 60, 712);
        c.Children.Add(desc);
        FeatherSticker(c, L.En ? 38 : 410, 150, d, a, b);
        var meta = T($"{d.Medal.TierName} · {DateText(d)} · {L.T($"مدال {Num(d.Rank)} از {Num(d.Total)}", $"medal {d.Rank} of {d.Total}")}", 12.5, Br(a, 0.85));
        At(meta, 0, 782);
        c.Children.Add(meta);
        Footer(c);
        return c;
    }

    private static Canvas Card(StoryData d)
    {
        var (a, b) = Tier(d.Medal.Tier);
        var c = NightBackground(b);
        var cap = T(L.T($"کارت شماره‌ی {Num(d.Rank)} کلکسیونم", $"Card number {d.Rank} of my collection"), 15, Br("#B6BAE6"), true);
        At(cap, 0, 70);
        c.Children.Add(cap);

        var rainbow = new LinearGradientBrush(
            [new GradientStop(C("#FF8AE0"), 0), new GradientStop(C("#FFD56E"), 0.35), new GradientStop(C("#7CF0C8"), 0.65), new GradientStop(C("#7CB2FF"), 1)], new Point(0, 0), new Point(1, 1));
        var card = new Canvas { Width = 390, Height = 600 };
        var rim = new Border { Width = 390, Height = 600, CornerRadius = new CornerRadius(30), Background = d.Medal.Tier is MedalTier.Legendary or MedalTier.Diamond ? rainbow : Lg(a, b) };
        card.Children.Add(rim);
        var inner = new Border { Width = 372, Height = 582, CornerRadius = new CornerRadius(24), Background = Lg(C("#1D2163"), C("#0E1030"), 0.2, 0, 0.8, 1) };
        At(inner, 9, 9);
        card.Children.Add(inner);
        var number = T($"#{Num(d.Rank)}/{Num(d.Total)}", 15, Br(a), true, 150, L.En ? TextAlignment.Left : TextAlignment.Right);
        number.FlowDirection = FlowDirection.LeftToRight;
        At(number, 30, 30);
        card.Children.Add(number);
        var tier = T($"★ {d.Medal.TierName}", 15, Br(a), true, 150, L.En ? TextAlignment.Right : TextAlignment.Left);
        At(tier, 210, 30);
        card.Children.Add(tier);
        Glow(card, 195, 230, 150, a, 0.28);
        Rays(card, 195, 230, 16, 300, a, 0.07);
        Laurel(card, 195, 232, 128, a, 8);
        var foil = new DrawingBrush
        {
            Viewport = new Rect(0, 0, 14, 14), ViewportUnits = BrushMappingMode.Absolute, TileMode = TileMode.Tile,
            Drawing = new GeometryDrawing(null, new Pen(Br("#FFFFFF", 0.05), 1.2), Geometry.Parse("M0 14 L14 0")),
        };
        card.Children.Insert(2, new Border { Width = 372, Height = 582, CornerRadius = new CornerRadius(24), Background = foil, Margin = new Thickness(9), IsHitTestVisible = false });
        var glyph = new MedalGlyph(d.Medal, true, 230);
        At(glyph, 80, 108);
        card.Children.Add(glyph);
        var name = T(d.Medal.Name, d.Medal.Name.Length > 14 ? 28 : 36, Br("#FFFFFF"), true, 340);
        At(name, 25, 362);
        card.Children.Add(name);
        var desc = T(d.Medal.Desc, 14.5, Br("#AEB2DE"), false, 320, TextAlignment.Center, 23);
        At(desc, 35, 420);
        card.Children.Add(desc);
        var line = new Rectangle { Width = 300, Height = 1, Fill = Br("#FFFFFF", 0.14) };
        At(line, 45, 520);
        card.Children.Add(line);
        var foot = T($"{DateText(d)}  ·  {L.T("نسخه‌ی شب‌بیدار", "Night-owl edition")}", 12, Br("#8E93C9"), false, 340);
        At(foot, 25, 536);
        card.Children.Add(foot);
        // the holographic shine: a diagonal band that crosses the card
        var shine = new Border
        {
            Width = 390, Height = 600, CornerRadius = new CornerRadius(30), IsHitTestVisible = false,
            Background = new LinearGradientBrush([new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.25), new GradientStop(Color.FromArgb(70, 255, 255, 255), 0.45), new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.62)], new Point(0, 0), new Point(1, 1)),
        };
        card.Children.Add(shine);
        card.RenderTransformOrigin = new Point(0.5, 0.5);
        card.RenderTransform = new RotateTransform(-4);
        At(card, 75, 140);
        c.Children.Add(card);
        FeatherSticker(c, L.En ? 380 : 52, 640, d, a, b, -10);
        Sparkles(c, 21, 12, new Rect(40, 110, 460, 700), Colors.White, 8);
        Footer(c);
        return c;
    }

    private static Canvas BidarCheer(StoryData d)
    {
        var (a, b) = Tier(d.Medal.Tier);
        var c = NightBackground(b);
        // a big moon in the corner, with a halo
        Glow(c, 440, 140, 120, C("#FFD56E"), 0.35);
        var moon = new Ellipse { Width = 92, Height = 92, Fill = Lg(C("#FFE9A8"), C("#F4B94E")) };
        At(moon, 394, 94);
        c.Children.Add(moon);
        // the bubble
        var bubble = new Border { Width = 390, CornerRadius = new CornerRadius(24), Background = Br("#FFFFFF"), Padding = new Thickness(20, 16, 20, 18) };
        bubble.Child = new TextBlock
        {
            Text = L.T($"آفرین! مدال «{d.Medal.Name}» مال توئه. من هم تا اینجا بیدار موندم!", $"Well done! The “{d.Medal.Name}” medal is yours. I stayed up for this!"),
            FontSize = 19, FontWeight = FontWeights.Bold, Foreground = Br("#1B1946"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, FlowDirection = Ui.Flow, LineHeight = 30,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        };
        At(bubble, 40, 150);
        c.Children.Add(bubble);
        var tail = new Polygon { Points = [new Point(0, 0), new Point(44, 0), new Point(22, 26)], Fill = Br("#FFFFFF") };
        At(tail, 248, 296);
        c.Children.Add(tail);
        Glow(c, 270, 520, 190, b, 0.35);
        Rays(c, 270, 520, 16, 420, a, 0.05);
        Confetti(c, 9, 60, new Rect(10, 330, 520, 380), a, b, C("#FFFFFF"), C("#FF8AE0"), C("#7CF0C8"), C("#FFD56E"));
        var owl = new BidarOwl { Width = 320, Height = 320, UseOutfit = true, Still = true };   // Boofi exactly as the person dressed him
        At(owl, 110, 352);   // a little lower, so a tall hat does not touch the bubble's tail
        c.Children.Add(owl);
        var glyph = new MedalGlyph(d.Medal, true, 150);
        At(glyph, 28, 400);
        glyph.RenderTransformOrigin = new Point(0.5, 0.5);
        glyph.RenderTransform = new RotateTransform(-12);
        c.Children.Add(glyph);
        Sparkles(c, 33, 16, new Rect(20, 330, 500, 380), C("#FFD56E"), 9);
        FeatherSticker(c, L.En ? 40 : 408, 520, d, a, b, 10);
        var name = T(d.Medal.Name, d.Medal.Name.Length > 14 ? 34 : 46, Br("#FFFFFF"), true, 480);
        At(name, 30, 700);
        c.Children.Add(name);
        var meta = T($"{d.Medal.TierName} · {DateText(d)}", 14, Br(a, 0.9));
        At(meta, 0, 770);
        c.Children.Add(meta);
        Footer(c);
        return c;
    }

    private static Canvas Poster(StoryData d)
    {
        var (a, b) = Tier(d.Medal.Tier);
        var ink = Br("#1B1946");
        var c = Root(Br("#F3EEDF"));
        var rule1 = new Rectangle { Width = 460, Height = 3, Fill = ink };
        At(rule1, 40, 64);
        c.Children.Add(rule1);
        var top1 = T(DateText(d), 13, ink, true, 220, L.En ? TextAlignment.Left : TextAlignment.Right);
        At(top1, L.En ? 40 : 320, 78);
        var top2 = T(L.T($"مدال {Num(d.Rank)}/{Num(d.Total)}", $"Medal {d.Rank}/{d.Total}"), 13, ink, true, 220, L.En ? TextAlignment.Right : TextAlignment.Left);
        At(top2, L.En ? 280 : 40, 78);
        c.Children.Add(top1);
        c.Children.Add(top2);
        // the name, as big as it can be; the last word gets the colour of the tier and a full stop
        var words = d.Medal.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var size = Fit(d.Medal.Name, 440, d.Medal.Name.Length switch { <= 6 => 150, <= 10 => 118, <= 16 => 92, _ => 72 }, 40, FontWeights.ExtraBold);
        var big = new TextBlock { FontSize = size, FontWeight = FontWeights.ExtraBold, Foreground = ink, Width = 460, TextWrapping = TextWrapping.Wrap, FlowDirection = Ui.Flow, TextAlignment = L.En ? TextAlignment.Left : TextAlignment.Right, LineHeight = size * 1.05, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
        for (var i = 0; i < words.Length; i++)
        {
            var last = i == words.Length - 1;
            big.Inlines.Add(new Run(words[i] + (last ? "." : " ")) { Foreground = last ? Br(C("#E8892B")) : ink });
        }
        At(big, 40, 130);
        c.Children.Add(big);
        var dots = new DrawingBrush
        {
            Viewport = new Rect(0, 0, 13, 13), ViewportUnits = BrushMappingMode.Absolute, TileMode = TileMode.Tile,
            Drawing = new GeometryDrawing(Br("#1B1946", 0.14), null, new EllipseGeometry(new Point(6.5, 6.5), 2.1, 2.1)),
        };
        var halftone = new Rectangle { Width = 460, Height = 300, Fill = dots, OpacityMask = Lg(Colors.Transparent, Colors.White, 0, 0, 0, 1) };
        At(halftone, 40, 500);
        c.Children.Add(halftone);
        var mark = T(Num(d.Rank), 330, Br("#1B1946", 0.05), true, 460, L.En ? TextAlignment.Right : TextAlignment.Left);
        mark.FlowDirection = Ui.Flow;
        At(mark, 40, 330);
        c.Children.Add(mark);
        big.Measure(new Size(460, double.PositiveInfinity));
        var under = new Rectangle { Width = 300, Height = 13, RadiusX = 6, RadiusY = 6, Fill = Br(C("#FFD27A")), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(-1.6) };
        At(under, L.En ? 40 : 200, 130 + big.DesiredSize.Height + 4);
        c.Children.Add(under);
        var stamp = new Border { Width = 150, Height = 150, CornerRadius = new CornerRadius(75), BorderThickness = new Thickness(3), BorderBrush = Br(C("#E8892B")), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(-14), Opacity = 0.9 };
        stamp.Child = new TextBlock { Text = d.Medal.TierName, FontSize = 22, FontWeight = FontWeights.ExtraBold, Foreground = Br(C("#E8892B")), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FlowDirection = Ui.Flow };
        At(stamp, L.En ? 330 : 40, 600);
        c.Children.Add(stamp);
        var glyph = new MedalGlyph(d.Medal, true, 250);
        At(glyph, L.En ? 40 : 250, 450);
        c.Children.Add(glyph);
        var desc = T(d.Medal.Desc, 20, ink, true, 300, L.En ? TextAlignment.Left : TextAlignment.Right, 31);
        At(desc, L.En ? 40 : 200, 730);
        c.Children.Add(desc);
        var rule2 = new Rectangle { Width = 460, Height = 1.5, Fill = ink };
        At(rule2, 40, 822);
        c.Children.Add(rule2);
        Footer(c, dark: false, y: 842);
        return c;
    }

    private static Canvas Constellation(StoryData d)
    {
        var (a, b) = Tier(d.Medal.Tier);
        var c = NightBackground(b);
        var rnd = new Random(7);
        for (var i = 0; i < 90; i++)
        {
            var dot = new Ellipse { Width = 1.5 + rnd.NextDouble() * 2, Height = 2, Fill = Br("#FFFFFF", 0.15 + rnd.NextDouble() * 0.5) };
            At(dot, rnd.NextDouble() * W, rnd.NextDouble() * 780);
            c.Children.Add(dot);
        }
        Glow(c, 80, 700, 200, C("#7B4DFF"), 0.22);
        Glow(c, 470, 250, 190, C("#FF8AE0"), 0.14);
        var tail = new Line { X1 = 470, Y1 = 150, X2 = 330, Y2 = 230, StrokeThickness = 2.4, Stroke = new LinearGradientBrush(Color.FromArgb(0, 255, 255, 255), Colors.White, 0) };
        c.Children.Add(tail);
        var head = new Path { Data = Spark(7), Fill = Br("#FFFFFF") };
        At(head, 330, 230);
        c.Children.Add(head);
        var top = T(L.T("آسمون من داره پرستاره می‌شه", "My sky is filling with stars"), 15, Br("#B6BAE6"), true);
        At(top, 0, 62);
        c.Children.Add(top);
        var count = T($"{Num(d.Earned)} / {Num(d.Total)}", 44, Br(a), true);
        At(count, 0, 92);
        c.Children.Add(count);
        // one star per medal, laid along a looping path; the lit ones are the medals earned
        var points = new List<Point>();
        var stars = Math.Min(d.Total, 36);
        for (var i = 0; i < stars; i++)
        {
            var t = i / (double)(stars - 1);
            var ang = Math.PI * (-0.5 + 2 * t);
            var rx = 205 + 20 * Math.Sin(t * 9);
            points.Add(new Point(270 + rx * Math.Cos(ang), 440 + (170 + 25 * Math.Cos(t * 7)) * Math.Sin(ang) * 1.18));
        }
        var lit = Math.Min(stars, (int)Math.Round(stars * d.Earned / (double)d.Total));
        for (var i = 1; i < points.Count; i++)
            c.Children.Add(new Line { X1 = points[i - 1].X, Y1 = points[i - 1].Y, X2 = points[i].X, Y2 = points[i].Y, Stroke = i < lit ? Br(a, 0.7) : Br("#FFFFFF", 0.10), StrokeThickness = i < lit ? 1.6 : 1 });
        for (var i = 0; i < points.Count; i++)
        {
            var on = i < lit;
            var s = on ? 6.5 : 3.6;
            if (on) Glow(c, points[i].X, points[i].Y, 14, a, 0.35);
            var star = new Path { Data = Spark(s), Fill = on ? Br("#FFFFFF") : Br("#FFFFFF", 0.42) };
            At(star, points[i].X, points[i].Y);
            c.Children.Add(star);
        }
        Glow(c, 270, 440, 150, a, 0.25);
        Rays(c, 270, 440, 14, 330, a, 0.05);
        var glyph = new MedalGlyph(d.Medal, true, 210);
        At(glyph, 165, 335);
        c.Children.Add(glyph);
        var name = T(d.Medal.Name, d.Medal.Name.Length > 14 ? 34 : 46, Br("#FFFFFF"), true, 480);
        At(name, 30, 700);
        c.Children.Add(name);
        var desc = T(d.Medal.Desc, 15.5, Br("#B6BAE6"), false, 440, TextAlignment.Center, 24);
        At(desc, 50, 765);
        c.Children.Add(desc);
        Footer(c);
        return c;
    }

    private static Canvas Month(StoryData d)
    {
        var ink = Br("#2A1B4D");
        var c = Root(Br("#FFD27A"));
        var sun = new Ellipse { Width = 520, Height = 520, Fill = Br("#F5A33A") };
        At(sun, L.En ? -190 : 210, -230);
        c.Children.Add(sun);
        var blob = new Ellipse { Width = 560, Height = 440, Fill = Br("#FF9AAE") };
        At(blob, L.En ? 250 : -270, 560);
        c.Children.Add(blob);
        Confetti(c, 61, 40, new Rect(10, 380, 520, 520), C("#2A1B4D"), C("#FFFFFF"), C("#FF9AAE"), C("#F5A33A"));
        var month = Fa.MonthName(DateTime.Now);
        var head = T(L.T($"{month} من با شبپا", $"My {month} with Shabpa"), 20, ink, true, 460, L.En ? TextAlignment.Left : TextAlignment.Right);
        At(head, 40, 70);
        c.Children.Add(head);
        var big = T(Num(Math.Max(d.MonthMedals, 1)), 190, ink, true, 460, L.En ? TextAlignment.Left : TextAlignment.Right);
        At(big, 40, 96);
        c.Children.Add(big);
        var unit = T(d.MonthMedals <= 1 ? L.T("مدال تازه", "new medal") : L.T("مدال تازه", "new medals"), 30, ink, true, 460, L.En ? TextAlignment.Left : TextAlignment.Right);
        At(unit, 40, 322);
        c.Children.Add(unit);
        var sub = T(L.T("توی این ماه گرفتم", "earned this month"), 17, Br("#2A1B4D", 0.75), false, 460, L.En ? TextAlignment.Left : TextAlignment.Right);
        At(sub, 40, 368);
        c.Children.Add(sub);
        // three real numbers
        var stats = new[]
        {
            (Num(d.MonthDays), L.T("روز با شبپا", "days with Shabpa")),
            (Num(d.Streak), L.T("شب پیاپی", "night streak")),
            (Num(d.Level), L.T($"سطح · {d.LevelName}", $"level · {d.LevelName}")),
        };
        for (var i = 0; i < 3; i++)
        {
            var x = 40 + (L.En ? i : 2 - i) * 156;
            var card = new Border { Width = 148, Height = 104, CornerRadius = new CornerRadius(20), Background = Br("#FFFFFF", 0.55) };
            At(card, x, 430);
            c.Children.Add(card);
            var n = T(stats[i].Item1, 38, ink, true, 148);
            At(n, x, 442);
            c.Children.Add(n);
            var l = T(stats[i].Item2, 12.5, Br("#2A1B4D", 0.8), false, 140);
            At(l, x + 4, 494);
            c.Children.Add(l);
        }
        var glyph = new MedalGlyph(d.Medal, true, 190);
        At(glyph, L.En ? 40 : 310, 580);
        c.Children.Add(glyph);
        FeatherSticker(c, L.En ? 150 : 296, 550, d, C("#FFFFFF"), C("#FFD56E"), -14);
        var cap = T(L.T("مدال آخرم", "my latest medal"), 14, ink, false, 250, L.En ? TextAlignment.Left : TextAlignment.Right);
        At(cap, L.En ? 250 : 40, 626);
        c.Children.Add(cap);
        var name = T(d.Medal.Name, d.Medal.Name.Length > 12 ? 28 : 36, ink, true, 250, L.En ? TextAlignment.Left : TextAlignment.Right);
        At(name, L.En ? 250 : 40, 652);
        c.Children.Add(name);
        var meta = T($"{DateText(d)} · {d.Medal.TierName}", 13, Br("#2A1B4D", 0.75), false, 250, L.En ? TextAlignment.Left : TextAlignment.Right);
        At(meta, L.En ? 250 : 40, 704);
        c.Children.Add(meta);
        Footer(c, dark: false);
        return c;
    }

    private static Canvas TileArch(StoryData d)
    {
        var (a, b) = Tier(d.Medal.Tier);
        var c = Root(Lg(C("#1A1E5A"), C("#0B0D2C"), 0.2, 0, 0.8, 1));
        // the tile pattern: eight-point stars in a grid, very faint
        var pattern = new DrawingBrush
        {
            Viewport = new Rect(0, 0, 66, 66), ViewportUnits = BrushMappingMode.Absolute, TileMode = TileMode.Tile,
            Drawing = new GeometryDrawing(null, new Pen(Br(a, 0.16), 1.2), Star8(33, 33, 28, 19)),
        };
        c.Children.Add(new Rectangle { Width = W, Height = H, Fill = pattern });
        Glow(c, 270, 440, 260, b, 0.22);
        // the arch (mihrab): two nested outlines
        Geometry Arch(double x, double y, double w, double h)
        {
            var g = new StreamGeometry();
            using var ctx = g.Open();
            ctx.BeginFigure(new Point(x, y + h), true, true);
            ctx.LineTo(new Point(x, y + w / 2), true, false);
            ctx.ArcTo(new Point(x + w, y + w / 2), new Size(w / 2, w / 2), 0, false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(x + w, y + h), true, false);
            g.Freeze();
            return g;
        }
        var fill = new Path { Data = Arch(60, 70, 420, 740), Fill = Lg(C("#222672"), C("#12154A"), 0.5, 0, 0.5, 1), Stroke = Lg(a, b), StrokeThickness = 4 };
        c.Children.Add(fill);
        c.Children.Add(new Path { Data = Arch(76, 86, 388, 708), Stroke = Br(a, 0.55), StrokeThickness = 1.5 });
        var inside = new Canvas { Width = W, Height = H, Clip = Arch(60, 70, 420, 740), IsHitTestVisible = false };
        Rays(inside, 270, 380, 22, 520, a, 0.07);
        Sparkles(inside, 71, 22, new Rect(70, 80, 400, 720), C("#FFD56E"), 6);
        c.Children.Add(inside);
        foreach (var (ox, oy) in new[] { (60.0, 330.0), (480.0, 330.0), (60.0, 810.0), (480.0, 810.0) })
        {
            var gem = new Path { Data = Spark(11), Fill = Br(a) };
            At(gem, ox, oy);
            c.Children.Add(gem);
        }
        var badge = T($"✦  {L.T("نشان افتخار", "Badge of honour")}  ✦", 16, Br(a), true, 360);
        At(badge, 90, 175);
        c.Children.Add(badge);
        Glow(c, 270, 380, 150, a, 0.3);
        var glyph = new MedalGlyph(d.Medal, true, 250);
        At(glyph, 145, 255);
        c.Children.Add(glyph);
        var name = T(d.Medal.Name, d.Medal.Name.Length > 12 ? 36 : 50, Br("#FFFFFF"), true, 340);
        At(name, 100, 535);
        c.Children.Add(name);
        var orn = new Path { Data = Geometry.Parse("M0 0 L60 0 M80 0 L86 0 M106 0 L166 0"), Stroke = Br(a, 0.8), StrokeThickness = 1.5 };
        At(orn, 187, 612);
        c.Children.Add(orn);
        var diamond = new Path { Data = Spark(7), Fill = Br(a) };
        At(diamond, 270, 612);
        c.Children.Add(diamond);
        var desc = T(d.Medal.Desc, 16, Br("#B6BAE6"), false, 320, TextAlignment.Center, 26);
        At(desc, 110, 636);
        c.Children.Add(desc);
        var meta = T($"{d.Medal.TierName} · {DateText(d)}", 13, Br(a, 0.9), false, 340);
        At(meta, 100, 726);
        c.Children.Add(meta);
        FeatherSticker(c, L.En ? 380 : 66, 176, d, a, b, -12);
        Footer(c);
        return c;
    }

    private static Canvas Arcade(StoryData d)
    {
        var (a, b) = Tier(d.Medal.Tier);
        var c = Root(Br("#05060F"));
        // the floor grid running to the horizon
        var horizon = 560.0;
        for (var i = -8; i <= 8; i++)
            c.Children.Add(new Line { X1 = 270 + i * 12, Y1 = horizon, X2 = 270 + i * 120, Y2 = H, Stroke = Br(b, 0.55), StrokeThickness = 1.4 });
        for (var i = 1; i <= 9; i++)
        {
            var y = horizon + Math.Pow(i / 9.0, 2.2) * (H - horizon);
            c.Children.Add(new Line { X1 = 0, Y1 = y, X2 = W, Y2 = y, Stroke = Br(b, 0.45), StrokeThickness = 1.2 });
        }
        Glow(c, 270, 560, 300, b, 0.28);
        var en = L.En;
        var rnd = new Random(88);
        for (var i = 0; i < 40; i++)
        {
            var px = new Rectangle { Width = 3, Height = 3, Fill = Br(i % 3 == 0 ? "#FFD56E" : "#FFFFFF", 0.4 + rnd.NextDouble() * 0.5) };
            At(px, rnd.NextDouble() * W, rnd.NextDouble() * 520);
            c.Children.Add(px);
        }
        foreach (var (dx, color) in new[] { (-3.0, "#00E5FF"), (3.0, "#FF3DAA") })
        {
            var ghost = new TextBlock
            {
                Text = en ? "ACHIEVEMENT\nUNLOCKED" : "دستاورد باز شد!", FontFamily = en ? new FontFamily("Consolas") : (FontFamily)Application.Current.Resources["Ui"], FontSize = en ? 40 : 52,
                FontWeight = FontWeights.Bold, Foreground = Br(color, 0.65), TextAlignment = TextAlignment.Center, Width = W, FlowDirection = Ui.Flow, LineHeight = 46, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            };
            At(ghost, dx, 78);
            c.Children.Add(ghost);
        }
        var title = new TextBlock
        {
            Text = en ? "ACHIEVEMENT\nUNLOCKED" : "دستاورد باز شد!", FontFamily = en ? new FontFamily("Consolas") : (FontFamily)Application.Current.Resources["Ui"], FontSize = en ? 40 : 52,
            FontWeight = FontWeights.Bold, Foreground = Br("#FFD56E"), TextAlignment = TextAlignment.Center, Width = W, FlowDirection = Ui.Flow, LineHeight = 46, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        };
        At(title, 0, 78);
        c.Children.Add(title);
        var sub = T(en ? "- medal unlocked -" : "— مدال جدید —", 15, Br("#8E93C9"));
        At(sub, 0, en ? 190 : 160);
        c.Children.Add(sub);
        Glow(c, 270, 380, 200, a, 0.35);
        var glyph = new MedalGlyph(d.Medal, true, 270);
        At(glyph, 135, 250);
        c.Children.Add(glyph);
        var plus = new TextBlock { Text = $"+{Num(d.Medal.Reward)} {L.T("پَر", "FEATHERS")}", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Br("#7CF0C8"), FlowDirection = Ui.Flow, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(-8) };
        At(plus, en ? 360 : 70, 290);
        c.Children.Add(plus);
        // the achievement box
        var box = new Border { Width = 440, Height = 190, Background = Br("#05060F"), BorderBrush = Br("#FFFFFF"), BorderThickness = new Thickness(3) };
        At(box, 50, 560);
        c.Children.Add(box);
        var name = T(d.Medal.Name, d.Medal.Name.Length > 14 ? 26 : 34, Br("#FFFFFF"), true, 410, en ? TextAlignment.Left : TextAlignment.Right);
        At(name, 65, 572);
        c.Children.Add(name);
        var desc = T(d.Medal.Desc, 14, Br("#B6BAE6"), false, 410, en ? TextAlignment.Left : TextAlignment.Right, 22);
        At(desc, 65, 626);
        c.Children.Add(desc);
        // a segmented bar: one cell per ten medals of the collection
        var cells = 12;
        var filled = (int)Math.Round(cells * d.Earned / (double)d.Total);
        for (var i = 0; i < cells; i++)
        {
            var cell = new Rectangle { Width = 26, Height = 14, Fill = i < filled ? Br("#FFD56E") : Br("#FFFFFF", 0.14) };
            At(cell, 68 + (en ? i : cells - 1 - i) * 32, 690);
            c.Children.Add(cell);
        }
        var counts = T(en ? $"{d.Earned}/{d.Total} MEDALS  ·  +{d.Medal.Reward} FEATHERS" : $"{Num(d.Earned)}/{Num(d.Total)} مدال  ·  +{Num(d.Medal.Reward)} پَر", 12.5, Br("#8E93C9"), true, 410, en ? TextAlignment.Left : TextAlignment.Right);
        At(counts, 65, 714);
        c.Children.Add(counts);
        // scan lines
        var scan = new DrawingBrush { Viewport = new Rect(0, 0, 4, 4), ViewportUnits = BrushMappingMode.Absolute, TileMode = TileMode.Tile, Drawing = new GeometryDrawing(Br("#000000", 0.22), null, new RectangleGeometry(new Rect(0, 0, 4, 1.5))) };
        c.Children.Add(new Rectangle { Width = W, Height = H, Fill = scan, IsHitTestVisible = false });
        Footer(c);
        return c;
    }

    private static Canvas ScreenPhoto(StoryData d)
    {
        var (a, b) = Tier(d.Medal.Tier);
        var c = NightBackground(b);
        var cap = T(L.T("وسط بازی بودم که این اومد پایین صفحه ↓", "I was mid-game when this popped up ↓"), 17, Br("#FFFFFF"), true, 460);
        At(cap, 40, 78);
        c.Children.Add(cap);
        var monitor = new Canvas { Width = 460, Height = 340 };
        monitor.Children.Add(new Border { Width = 460, Height = 340, CornerRadius = new CornerRadius(20), Background = Br("#0D0F2A"), BorderBrush = Br("#FFFFFF", 0.16), BorderThickness = new Thickness(2) });
        var screen = new Border
        {
            Width = 432, Height = 312, CornerRadius = new CornerRadius(12), ClipToBounds = true,
            Background = new RadialGradientBrush(C("#3E5C9A"), C("#14214A")) { Center = new Point(0.3, 0.2), GradientOrigin = new Point(0.3, 0.2), RadiusX = 0.9, RadiusY = 0.9 },
        };
        At(screen, 14, 14);
        monitor.Children.Add(screen);
        var bar = new Rectangle { Width = 432, Height = 22, Fill = Br("#0B0C22", 0.92) };
        At(bar, 14, 304);
        monitor.Children.Add(bar);
        for (var i = 0; i < 6; i++)
        {
            var app = new Rectangle { Width = 12, Height = 12, RadiusX = 3, RadiusY = 3, Fill = Br("#5B57D6") };
            At(app, 150 + i * 20, 309);
            monitor.Children.Add(app);
        }
        // the notice, drawn like the real one (small card, medal, text), bottom right
        var toast = new Canvas { Width = 210, Height = 70 };
        toast.Children.Add(new Border { Width = 210, Height = 70, CornerRadius = new CornerRadius(13), Background = Br("#1D2060"), BorderBrush = Br("#FFD56E"), BorderThickness = new Thickness(1) });
        var tg = new MedalGlyph(d.Medal, true, 46);
        At(tg, L.En ? 10 : 154, 12);
        toast.Children.Add(tg);
        var t1 = T(L.T("مدال جدید!", "New medal!"), 9.5, Br("#FFD56E"), true, 130, L.En ? TextAlignment.Left : TextAlignment.Right);
        At(t1, L.En ? 64 : 16, 10);
        toast.Children.Add(t1);
        var t2 = T(d.Medal.Name, 13, Br("#FFFFFF"), true, 130, L.En ? TextAlignment.Left : TextAlignment.Right);
        At(t2, L.En ? 64 : 16, 24);
        toast.Children.Add(t2);
        var t3 = T(d.Medal.Desc, 9.5, Br("#AEB2DE"), false, 130, L.En ? TextAlignment.Left : TextAlignment.Right);
        t3.TextWrapping = TextWrapping.NoWrap;
        t3.TextTrimming = TextTrimming.CharacterEllipsis;
        At(t3, L.En ? 64 : 16, 44);
        toast.Children.Add(t3);
        At(toast, 226, 222);
        monitor.Children.Add(toast);
        var ring = new Ellipse { Width = 250, Height = 108, Stroke = Br("#FFD56E"), StrokeThickness = 3.5, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(-4) };
        At(ring, 206, 203);
        monitor.Children.Add(ring);
        monitor.RenderTransformOrigin = new Point(0.5, 0.5);
        monitor.RenderTransform = new RotateTransform(-5);
        At(monitor, 40, 140);
        c.Children.Add(monitor);
        Glow(c, 60, 560, 200, C("#FFD56E"), 0.12);
        var keyboard = new Border { Width = 400, Height = 46, CornerRadius = new CornerRadius(10), Background = Br("#FFFFFF", 0.06), BorderBrush = Br("#FFFFFF", 0.1), BorderThickness = new Thickness(1) };
        At(keyboard, 70, 548);
        c.Children.Add(keyboard);
        for (var row = 0; row < 3; row++)
            for (var k = 0; k < 20; k++)
            {
                var key = new Rectangle { Width = 14, Height = 8, RadiusX = 2, RadiusY = 2, Fill = Br("#FFFFFF", 0.1) };
                At(key, 80 + k * 19.5, 555 + row * 12);
                c.Children.Add(key);
            }
        var stand = new Border { Width = 120, Height = 14, CornerRadius = new CornerRadius(7), Background = Br("#FFFFFF", 0.12) };
        At(stand, 210, 520);
        c.Children.Add(stand);
        Glow(c, 270, 650, 140, a, 0.2);
        FeatherSticker(c, L.En ? 400 : 48, 610, d, a, b, 9);
        var name = T(d.Medal.Name, d.Medal.Name.Length > 14 ? 34 : 46, Br("#FFFFFF"), true, 480);
        At(name, 30, 610);
        c.Children.Add(name);
        var desc = T(d.Medal.Desc, 16, Br("#B6BAE6"), false, 430, TextAlignment.Center, 25);
        At(desc, 55, 676);
        c.Children.Add(desc);
        var meta = T($"{d.Medal.TierName} · {DateText(d)} · {L.T($"مدال {Num(d.Rank)} از {Num(d.Total)}", $"medal {d.Rank} of {d.Total}")}", 12.5, Br(a, 0.9));
        At(meta, 0, 742);
        c.Children.Add(meta);
        Footer(c);
        return c;
    }

    private static Canvas Certificate(StoryData d)
    {
        var (a, b) = Tier(d.Medal.Tier);
        var c = Root(Br("#0F1236"));
        Glow(c, 270, 480, 380, b, 0.18);
        var paper = new Canvas { Width = 440, Height = 700 };
        paper.Children.Add(new Border { Width = 440, Height = 700, CornerRadius = new CornerRadius(6), Background = Lg(C("#FAF0D0"), C("#EAD6A0"), 0.2, 0, 0.8, 1) });
        var ink = Br("#4B3210");
        paper.Children.Add(new Border { Width = 408, Height = 668, Margin = new Thickness(16), BorderBrush = Br("#B98A2E"), BorderThickness = new Thickness(3) });
        paper.Children.Add(new Border { Width = 392, Height = 652, Margin = new Thickness(24), BorderBrush = Br("#B98A2E", 0.6), BorderThickness = new Thickness(1) });
        foreach (var (x, y) in new[] { (34.0, 34.0), (406.0, 34.0), (34.0, 666.0), (406.0, 666.0) })
        {
            var orn = new Path { Data = Spark(9), Fill = Br("#B98A2E") };
            At(orn, x, y);
            paper.Children.Add(orn);
        }
        var title = T(L.T("گواهی افتخار", "Certificate of Honour"), 34, ink, true, 380);
        At(title, 30, 70);
        paper.Children.Add(title);
        var to1 = T(L.T("این گواهی تقدیم می‌شود به", "This certificate is presented to"), 14, Br("#6B4E1C"), false, 380);
        At(to1, 30, 138);
        paper.Children.Add(to1);
        var who = T(string.IsNullOrWhiteSpace(d.Name) ? L.T("یک شب‌بیدار واقعی", "a true night owl") : d.Name, 28, Br("#8A5A12"), true, 380);
        At(who, 30, 166);
        paper.Children.Add(who);
        var forText = T(L.T("برای گرفتن مدال", "for earning the medal"), 14, Br("#6B4E1C"), false, 380);
        At(forText, 30, 222);
        paper.Children.Add(forText);
        Laurel(paper, 220, 362, 128, Br("#B98A2E").Color, 9);
        var glyph = new MedalGlyph(d.Medal, true, 200);
        At(glyph, 120, 262);
        paper.Children.Add(glyph);
        var name = T(d.Medal.Name, d.Medal.Name.Length > 14 ? 30 : 40, ink, true, 380);
        At(name, 30, 482);
        paper.Children.Add(name);
        var desc = T(d.Medal.Desc, 15, Br("#6B4E1C"), false, 340, TextAlignment.Center, 24);
        At(desc, 50, 540);
        paper.Children.Add(desc);
        var line1 = new Rectangle { Width = 120, Height = 1, Fill = ink };
        At(line1, L.En ? 50 : 270, 622);
        paper.Children.Add(line1);
        var date = T(DateText(d), 13, ink, true, 120);
        At(date, L.En ? 50 : 270, 628);
        paper.Children.Add(date);
        var dl = T(L.T("تاریخ", "date"), 11, Br("#6B4E1C"), false, 120);
        At(dl, L.En ? 50 : 270, 648);
        paper.Children.Add(dl);
        var line2 = new Rectangle { Width = 120, Height = 1, Fill = ink };
        At(line2, L.En ? 270 : 50, 622);
        paper.Children.Add(line2);
        var owl = new BidarOwl { Width = 74, Height = 74, UseOutfit = true, Still = true };
        At(owl, L.En ? 293 : 73, 560);
        paper.Children.Add(owl);
        var sign = T(L.T("امضای بوفی", "signed by Boofi"), 11, Br("#6B4E1C"), false, 120);
        At(sign, L.En ? 270 : 50, 648);
        paper.Children.Add(sign);
        var serial = T($"No. SP-{d.When:yyyyMMdd}-{d.Rank:00}", 10, Br("#8A6A2E"), false, 380);
        serial.FlowDirection = FlowDirection.LeftToRight;
        At(serial, 30, 672);
        paper.Children.Add(serial);
        // the wax seal, with two ribbon tails
        foreach (var rot in new[] { -18, 18 })
        {
            var tailR = new Polygon { Points = [new Point(0, 0), new Point(22, 0), new Point(22, 46), new Point(11, 36), new Point(0, 46)], Fill = Br("#9A1F2B"), RenderTransformOrigin = new Point(0.5, 0), RenderTransform = new RotateTransform(rot) };
            At(tailR, 209, 610);
            paper.Children.Add(tailR);
        }
        var seal = new Ellipse { Width = 76, Height = 76, Fill = new RadialGradientBrush(C("#E04A5A"), C("#8E1624")), Stroke = Br("#6E0F1B"), StrokeThickness = 3 };
        At(seal, 182, 566);
        paper.Children.Add(seal);
        paper.Children.Add(new Ellipse { Width = 56, Height = 56, Stroke = Br("#FFFFFF", 0.35), StrokeThickness = 1.4, Margin = new Thickness(192, 576, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top });
        var seed = new Path { Data = Star8(220, 604, 20, 13), Stroke = Br("#FFE3E6", 0.9), StrokeThickness = 2, Fill = Br("#FFFFFF", 0.12) };
        paper.Children.Add(seed);
        paper.RenderTransformOrigin = new Point(0.5, 0.5);
        paper.RenderTransform = new RotateTransform(1.2);
        At(paper, 50, 80);
        c.Children.Add(paper);
        Sparkles(c, 41, 10, new Rect(20, 40, 500, 780), C("#FFD56E"), 7);
        Footer(c, y: 846);
        return c;
    }

    // ---- saving ------------------------------------------------------------------------------------------------------

    /// <summary>Draws the card at 2× (1080×1920).</summary>
    public static BitmapSource Render(FrameworkElement card)
    {
        card.Measure(new Size(W, H));
        card.Arrange(new Rect(0, 0, W, H));
        card.UpdateLayout();
        var bmp = new RenderTargetBitmap(1080, 1920, 192, 192, PixelFormats.Pbgra32);
        bmp.Render(card);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>Test aid: all ten designs side by side in one picture (two rows of five), to judge them at a glance.</summary>
    public static void SaveSheet(StoryData data, string path)
    {
        const double scale = 0.36, gap = 12;
        var cw = W * scale;
        var ch = H * scale;
        var sheet = new Canvas { Width = 5 * (cw + gap) + gap, Height = 2 * (ch + gap) + gap, Background = Br("#0A0A19") };
        for (var i = 0; i < Designs.Length; i++)
        {
            var host = new Viewbox { Width = cw, Height = ch, Child = Build(Designs[i].Key, data) };
            At(host, gap + (i % 5) * (cw + gap), gap + (i / 5) * (ch + gap));
            sheet.Children.Add(host);
        }
        sheet.Measure(new Size(sheet.Width, sheet.Height));
        sheet.Arrange(new Rect(0, 0, sheet.Width, sheet.Height));
        sheet.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)sheet.Width * 2, (int)sheet.Height * 2, 192, 192, PixelFormats.Pbgra32);
        bmp.Render(sheet);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = System.IO.File.Create(path);
        enc.Save(fs);
    }

    /// <summary>Writes the card as a PNG (test aid; the picker writes through the same <see cref="Render"/>).</summary>
    public static void Save(FrameworkElement card, string path)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(Render(card)));
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        using var fs = System.IO.File.Create(path);
        enc.Save(fs);
    }
}
