using System.Globalization;

namespace Shabpa.Core;

/// <summary>One colour of a light. 8 bits per channel.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static readonly Rgb Black = new(0, 0, 0);

    public static Rgb FromHsv(double hue, double saturation, double value)
    {
        hue = (hue % 1 + 1) % 1 * 6;
        var c = value * saturation;
        var x = c * (1 - Math.Abs(hue % 2 - 1));
        var m = value - c;
        var (r, g, b) = (int)hue switch { 0 => (c, x, 0.0), 1 => (x, c, 0.0), 2 => (0.0, c, x), 3 => (0.0, x, c), 4 => (x, 0.0, c), _ => (c, 0.0, x) };
        return new Rgb(To(r + m), To(g + m), To(b + m));
    }

    private static byte To(double v) => (byte)Math.Clamp(Math.Round(v * 255), 0, 255);

    public Rgb Scale(double k) => new(To(R / 255.0 * k), To(G / 255.0 * k), To(B / 255.0 * k));

    public static Rgb Lerp(Rgb a, Rgb b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new Rgb((byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
    }

    /// <summary>A blend that keeps colours rich: two saturated colours are mixed through the hues between them (blue to red goes through violet, not through a grey-purple),
    /// greys and near-greys are mixed plainly. A straight mix of two RGB colours loses saturation in the middle, which is what made gradients look washed out.</summary>
    public static Rgb Blend(Rgb a, Rgb b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        var (sa, va) = (a.Saturation, a.Value);
        var (sb, vb) = (b.Saturation, b.Value);
        if (sa < 0.3 || sb < 0.3 || va < 0.05 || vb < 0.05) return Lerp(a, b, t);
        var ha = a.Hue;
        var d = b.Hue - ha;
        if (d > 0.5) d -= 1; else if (d < -0.5) d += 1;               // the short way round the colour circle
        return FromHsv(ha + d * t, sa + (sb - sa) * t, va + (vb - va) * t);
    }

    public double Saturation { get { var max = Math.Max(R, Math.Max(G, B)); return max == 0 ? 0 : (max - Math.Min(R, Math.Min(G, B))) / (double)max; } }
    public double Value => Math.Max(R, Math.Max(G, B)) / 255.0;

    private static readonly byte[] GammaTable = [.. Enumerable.Range(0, 256).Select(i => (byte)Math.Round(Math.Pow(i / 255.0, 2.2) * 255))];

    /// <summary>What to send to the light so it looks the way the colour looks on a screen. LEDs answer a number in a straight line, a screen answers it in a curve; without this
    /// every mixed colour (yellow, pink, cyan, anything pastel) looks pale and whitish on the lights.</summary>
    public Rgb ForLight() => new(GammaTable[R], GammaTable[G], GammaTable[B]);

    public static Rgb Max(Rgb a, Rgb b) => new(Math.Max(a.R, b.R), Math.Max(a.G, b.G), Math.Max(a.B, b.B));

    /// <summary>"RRGGBB" (no #).</summary>
    public string Hex => $"{R:X2}{G:X2}{B:X2}";

    public static bool TryParse(string? hex, out Rgb color)
    {
        color = Black;
        if (hex is null) return false;
        hex = hex.Trim().TrimStart('#');
        if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
        color = new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public static Rgb Parse(string? hex, Rgb fallback) => TryParse(hex, out var c) ? c : fallback;

    /// <summary>The hue (0 to 1) of this colour; 0 for greys.</summary>
    public double Hue
    {
        get
        {
            double r = R / 255.0, g = G / 255.0, b = B / 255.0;
            var max = Math.Max(r, Math.Max(g, b));
            var d = max - Math.Min(r, Math.Min(g, b));
            if (d < 1e-9) return 0;
            var h = max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
            return (h / 6 + 1) % 1;
        }
    }
}

/// <summary>The movements an effect is built from. Every named effect is one of these with a palette and a speed.</summary>
public enum RgbPattern { Solid, Breathe, Flash, DoubleFlash, Random, Rainbow, Cycle, Wave, Chase, Meteor, Scan, Stack, Sparkle, Heartbeat, Siren, Lightning, Ripple, Fire, Temperature, Music }

/// <summary>A named lighting effect. <see cref="UsesColor"/> effects follow the colour the person picks; the others carry their own palette.</summary>
public sealed record RgbEffect(string Key, string Fa, string En, string Group, RgbPattern Pattern, string[] Palette, double Speed = 1, bool UsesColor = false, double Span = 1)
{
    public string Name => L.T(Fa, En);
    public Rgb[] Colors => [.. Palette.Select(h => Rgb.Parse(h, Rgb.Black))];
}

public static class RgbEffects
{
    public const string GroupBase = "base", GroupCalm = "calm", GroupNature = "nature", GroupSpace = "space", GroupEnergy = "energy", GroupPersian = "persian", GroupSeason = "season";

    private static RgbEffect B(string key, string fa, string en, RgbPattern p, double speed = 1, params string[] palette) => new(key, fa, en, GroupBase, p, palette.Length > 0 ? palette : ["FF2D55"], speed, UsesColor: palette.Length == 0);
    /// <summary>A flowing look whose lights each show only a part of the palette at a time (<paramref name="span"/> of it, so two or three neighbouring colours) while the whole palette slowly rolls through.</summary>
    private static RgbEffect F(string key, string fa, string en, string group, double span, double speed, params string[] palette) => new(key, fa, en, group, RgbPattern.Wave, palette, speed, Span: span);
    private static RgbEffect S(string key, string fa, string en, string group, RgbPattern p, double speed, params string[] palette) => new(key, fa, en, group, p, palette, speed);

    /// <summary>The basic effects of a lighting program: they follow the colour and brightness chosen on the page (MSI Center's static, breathing,
    /// flashing, double flashing, random, rainbow, colour shift, meteor, music, CPU temperature and lightning families live here too).</summary>
    public static readonly IReadOnlyList<RgbEffect> Basic =
    [
        B("static", "ثابت", "Static", RgbPattern.Solid),
        B("breathe", "تنفسی", "Breathing", RgbPattern.Breathe, 0.8),
        B("flash", "چشمک", "Flashing", RgbPattern.Flash, 1),
        B("double", "دو چشمک", "Double flash", RgbPattern.DoubleFlash, 1),
        new("rainbow", "رنگین‌کمان", "Rainbow", GroupBase, RgbPattern.Rainbow, ["FF2D55"], 0.8, UsesColor: true, Span: 0.34),        // two or three neighbouring hues on every light, all lights in step
        new("rainbowwide", "رنگین‌کمان کامل", "Full rainbow", GroupBase, RgbPattern.Rainbow, ["FF2D55"], 0.8, UsesColor: true, Span: 1),
        B("cycle", "چرخش رنگ", "Colour cycle", RgbPattern.Cycle, 1),
        B("random", "تصادفی", "Random", RgbPattern.Random, 1),
        B("chase", "تعقیب", "Chase", RgbPattern.Chase, 1),
        B("meteor", "شهاب", "Meteor", RgbPattern.Meteor, 1),
        B("scan", "اسکنر", "Scanner", RgbPattern.Scan, 1),
        B("stack", "پُر و خالی", "Fill up", RgbPattern.Stack, 1),
        B("ripple", "موج", "Ripple", RgbPattern.Ripple, 1),
        B("sparkle", "جرقه", "Sparkle", RgbPattern.Sparkle, 1),
        B("lightning", "رعدوبرق", "Lightning", RgbPattern.Lightning, 1),
        B("music", "موسیقی", "Music", RgbPattern.Music, 1),
        B("temp", "دمای پردازنده", "CPU temperature", RgbPattern.Temperature, 1),
    ];

    /// <summary>Ready-made looks with their own colours, in groups.</summary>
    public static readonly IReadOnlyList<RgbEffect> Special =
    [
        S("moonlake", "دریاچه‌ی مهتاب", "Moonlit lake", GroupCalm, RgbPattern.Wave, 0.5, "1B3A8F", "5FA8FF", "E6F1FF", "5FA8FF"),
        S("cottoncloud", "ابر پنبه‌ای", "Cotton cloud", GroupCalm, RgbPattern.Wave, 0.6, "FF9ECF", "A9D8FF", "FFD6EC", "A9D8FF"),
        S("lavender", "مه‌ی اسطوخودوس", "Lavender haze", GroupCalm, RgbPattern.Wave, 0.5, "7A4DFF", "C5A6FF", "F0E4FF", "9B72FF"),
        S("frost", "یخبندان", "Frost", GroupCalm, RgbPattern.Sparkle, 0.7, "BFE9FF", "FFFFFF", "6EC6FF"),
        S("deepsea", "اعماق دریا", "Deep sea", GroupNature, RgbPattern.Wave, 0.6, "001F5C", "0077B6", "00B4D8", "0077B6"),
        S("aurora", "شفق قطبی", "Polar aurora", GroupNature, RgbPattern.Wave, 0.6, "00E08A", "00B7C2", "7B4DFF", "00E08A"),
        S("forest", "جنگل بارانی", "Rain forest", GroupNature, RgbPattern.Wave, 0.5, "0B6B2E", "3DDC63", "B5F26B", "1E9E46"),
        S("lava", "گدازه", "Lava flow", GroupNature, RgbPattern.Fire, 1.1, "FF2A00", "FF7A00", "FFD000", "7A0A00"),
        S("blossom", "شکوفه‌ی بهاری", "Spring blossom", GroupNature, RgbPattern.Sparkle, 0.8, "FF9EC7", "FFFFFF", "FFC2DD"),
        S("sunrise", "طلوع", "Sunrise", GroupNature, RgbPattern.Wave, 0.4, "FF4D6D", "FF9E40", "FFE08A", "FF7A59"),
        S("dusk", "غروب", "Dusk", GroupNature, RgbPattern.Wave, 0.4, "FF5E3A", "C2185B", "512DA8", "FF5E3A"),
        S("nebula", "سحابی", "Nebula drift", GroupSpace, RgbPattern.Wave, 0.7, "5B2BFF", "FF3DCB", "28C7FF", "5B2BFF"),
        S("galaxy", "کهکشان", "Galaxy bloom", GroupSpace, RgbPattern.Sparkle, 0.6, "2B1055", "7B4DFF", "FFFFFF", "FF6EC7"),
        S("blackhole", "سیاه‌چاله", "Event horizon", GroupSpace, RgbPattern.Ripple, 0.5, "000000", "6A2BFF", "FFB347"),
        S("comet", "ستاره‌ی دنباله‌دار", "Comet", GroupSpace, RgbPattern.Meteor, 1.2, "BFE9FF", "6EC6FF", "0A2A66"),
        S("neonstorm", "طوفان نئون", "Neon storm", GroupEnergy, RgbPattern.Lightning, 1.2, "00F0FF", "FF00E6", "7A00FF"),
        S("cybernight", "شب سایبری", "Cyber night", GroupEnergy, RgbPattern.Chase, 1.3, "FFE600", "00F0FF", "FF00E6"),
        S("patrol", "گشت", "Patrol", GroupEnergy, RgbPattern.Siren, 1.2, "FF1A1A", "1A4DFF"),
        S("pulse", "ضربان", "Heartbeat", GroupEnergy, RgbPattern.Heartbeat, 1, "FF1744"),
        S("toxic", "سمی", "Toxic", GroupEnergy, RgbPattern.Wave, 1.1, "A6FF00", "7A00FF", "39FF14", "7A00FF"),
        S("phoenix", "ققنوس", "Phoenix", GroupEnergy, RgbPattern.Fire, 1.3, "FF0000", "FF6A00", "FFD000"),
        S("dome", "گنبد فیروزه‌ای", "Turquoise dome", GroupPersian, RgbPattern.Wave, 0.5, "0AA6A6", "1FD1C4", "E8C26A", "0A6F9E"),
        S("yalda", "شب یلدا", "Yalda night", GroupPersian, RgbPattern.Sparkle, 0.7, "B3123C", "5A0F2E", "FFFFFF"),
        S("nowruz", "بهار نوروز", "Nowruz spring", GroupPersian, RgbPattern.Wave, 0.6, "2ECC40", "FFDC00", "FF6B81", "FF4136"),
        S("carpet", "فرش ایرانی", "Persian carpet", GroupPersian, RgbPattern.Wave, 0.4, "9B1B30", "1B2A6B", "D9A441", "9B1B30"),
        S("saffron", "زعفران", "Saffron", GroupPersian, RgbPattern.Breathe, 0.6, "FF9A00", "FFC93C"),
        S("halloween", "هالووین", "Halloween", GroupSeason, RgbPattern.Fire, 1, "FF6A00", "8A2BE2", "1A0033"),
        S("candy", "آب‌نبات", "Candy shop", GroupSeason, RgbPattern.Wave, 0.9, "FF3D81", "FFFFFF", "3DCBFF", "FF3D81"),
        // smooth, flowing looks
        S("mint", "نسیم نعنا", "Mint breeze", GroupCalm, RgbPattern.Wave, 0.5, "7CFFCB", "B8F7E0", "3DDCC2", "7CFFCB"),
        S("arctic", "درخشش قطبی", "Arctic glow", GroupCalm, RgbPattern.Wave, 0.45, "9BE7FF", "E8FBFF", "5AB8FF", "9BE7FF"),
        S("rosegold", "رزگلد", "Rose gold", GroupCalm, RgbPattern.Breathe, 0.5, "FFB5A7", "F7CAD0", "E8A0A0"),
        S("ocean", "امواج اقیانوس", "Ocean waves", GroupNature, RgbPattern.Wave, 0.8, "003B73", "0074D9", "39CCCC", "7FDBFF"),
        S("emerald", "جنگل زمرد", "Emerald forest", GroupNature, RgbPattern.Wave, 0.55, "004D2C", "00C46A", "9DFFC9", "00A35A"),
        S("fireflies", "کرم شب‌تاب", "Fireflies", GroupNature, RgbPattern.Sparkle, 0.9, "D8FF3D", "FFF59D", "7CFC00"),
        S("autumn", "برگ پاییزی", "Autumn leaves", GroupNature, RgbPattern.Fire, 0.7, "B7410E", "E67E22", "F1C40F", "4A1C07"),
        S("supernova", "ابرنواختر", "Supernova", GroupSpace, RgbPattern.Ripple, 0.7, "FFFFFF", "FFB347", "7B2FFF"),
        S("saturn", "حلقه‌های زحل", "Saturn rings", GroupSpace, RgbPattern.Wave, 0.5, "D9A441", "F3E2B3", "7B5E2E", "D9A441"),
        S("starfield", "میدان ستاره", "Starfield", GroupSpace, RgbPattern.Sparkle, 0.8, "FFFFFF", "9BB8FF", "FFE9A6"),
        S("vaporwave", "ویپروِیو", "Vaporwave", GroupEnergy, RgbPattern.Wave, 0.8, "FF71CE", "B967FF", "01CDFE", "05FFA1"),
        S("synthwave", "سینث‌ویو", "Synthwave", GroupEnergy, RgbPattern.Wave, 1.0, "FF2E97", "7B2FFF", "00E5FF", "FF2E97"),
        S("firefrost", "آتش و یخ", "Fire and ice", GroupEnergy, RgbPattern.Wave, 0.9, "FF4500", "FFFFFF", "00BFFF", "FF4500"),
        S("volt", "ولت", "Volt", GroupEnergy, RgbPattern.Lightning, 1.4, "FFE600", "FFFFFF", "00E5FF"),
        S("razor", "تیغ", "Razor", GroupEnergy, RgbPattern.Scan, 1.3, "FF1744"),
        S("kashan", "کاشی کاشان", "Kashan tile", GroupPersian, RgbPattern.Wave, 0.45, "0B3D91", "1FA8C9", "F2E8CF", "0B3D91"),
        S("pomegranate", "انار", "Pomegranate", GroupPersian, RgbPattern.Wave, 0.5, "7A0019", "C1121F", "3A7D44", "7A0019"),
        S("christmas", "کریسمس", "Christmas", GroupSeason, RgbPattern.Sparkle, 0.9, "E63946", "2A9D8F", "FFFFFF"),
        S("valentine", "ولنتاین", "Valentine", GroupSeason, RgbPattern.Heartbeat, 0.9, "FF4D8D"),
        // rich, saturated flowing looks: every light shows two or three of these colours at once, all lights in step, the palette rolling slowly
        F("spectrum", "طیف رنگی", "Spectrum", GroupEnergy, 0.4, 0.7, "FF1744", "FF9100", "FFD600", "00E676", "00B0FF", "651FFF", "F50057"),
        F("sunsetdrive", "جاده‌ی غروب", "Sunset drive", GroupNature, 0.5, 0.6, "FF1E56", "FF6B00", "FFC400", "FF1E56", "9C27B0", "3F1DCB"),
        F("tropical", "گرمسیری", "Tropical", GroupNature, 0.45, 0.7, "00E5A0", "00B8FF", "FF2E93", "FFB800", "00E5A0"),
        F("northern", "شمالگان", "Northern lights", GroupNature, 0.5, 0.55, "00FF87", "00C2FF", "7B2CFF", "FF2EA6", "00FF87"),
        F("neonduo", "نئون دوقلو", "Neon duo", GroupEnergy, 0.5, 0.8, "FF0080", "7000FF", "00D4FF", "7000FF"),
        F("firewave", "موج آتش", "Fire wave", GroupEnergy, 0.5, 0.8, "FF0000", "FF5A00", "FFB000", "FF0050"),
        F("oceandeep", "عمق اقیانوس", "Ocean depth", GroupCalm, 0.5, 0.5, "0033FF", "00A2FF", "00E0D0", "0033FF", "5A00FF"),
    ];

    public static IReadOnlyList<RgbEffect> All => [.. Basic, .. Special];

    public static RgbEffect Find(string? key) => All.FirstOrDefault(e => e.Key == key) ?? Basic[0];

    public static readonly string[] Groups = [GroupCalm, GroupNature, GroupSpace, GroupEnergy, GroupPersian, GroupSeason];

    public static string GroupName(string group) => group switch
    {
        GroupCalm => L.T("آرام", "Calm"), GroupNature => L.T("طبیعت", "Nature"), GroupSpace => L.T("فضا", "Space"),
        GroupEnergy => L.T("پرانرژی", "Energetic"), GroupPersian => L.T("ایرانی", "Persian"), GroupSeason => L.T("مناسبتی", "Seasonal"), _ => L.T("پایه", "Basic"),
    };

    /// <summary>The colours of the swatches on the page.</summary>
    public static readonly string[] Swatches = ["8B5CF6", "FF2D55", "FF8A00", "FFD60A", "10B981", "06B6D4", "3B82F6", "EC4899", "FFFFFF"];
}

/// <summary>The places a PC has lights, in the order of the preview. Each is one group of LEDs the effect is laid along.</summary>
public sealed record RgbZone(string Key, string Fa, string En, int Leds)
{
    public string Name => L.T(Fa, En);
}

public static class RgbZones
{
    public static readonly IReadOnlyList<RgbZone> All =
    [
        new("fans", "فن‌ها", "Fans", 36),
        new("ram", "رم", "Memory", 16),
        new("cooler", "کولر پردازنده", "CPU cooler", 12),
        new("strip", "نوار و هدرها", "Strips & headers", 24),
        new("board", "مادربورد", "Motherboard", 8),
        new("gpu", "کارت گرافیک", "Graphics card", 10),
        new("keyboard", "کیبورد", "Keyboard", 22),
        new("mouse", "موس و لوازم", "Mouse & accessories", 6),
    ];

    public static RgbZone Find(string key) => All.FirstOrDefault(z => z.Key == key) ?? All[0];
}

/// <summary>How the lights come on and go out: a mask that lets a frame through from nothing (or to nothing) over about a second, in one of a few ways.</summary>
public static class RgbTransitions
{
    public const int DurationMs = 1200;
    public static readonly IReadOnlyList<(string Key, string Fa, string En)> Styles =
    [
        ("blaze", "شعله‌ور", "Blaze"), ("sunrise", "طلوع", "Sunrise"), ("center", "از مرکز", "From the centre"),
        ("darkwave", "موج تاریکی", "Dark wave"), ("soft", "نرم", "Soft"), ("none", "بدون افکت", "No effect"),
    ];

    public static string Name(string key) => Styles.FirstOrDefault(s => s.Key == key) is { Key: not null } s ? L.T(s.Fa, s.En) : Name("blaze");

    private static double Smooth(double v) { v = Math.Clamp(v, 0, 1); return v * v * (3 - 2 * v); }

    /// <summary>The frame as it looks when <paramref name="visible"/> (0 to 1) of the transition has happened. 1 is the frame itself, 0 is dark.</summary>
    public static Rgb[] Apply(Rgb[] frame, string style, double visible)
    {
        visible = Math.Clamp(visible, 0, 1);
        if (style == "none" || visible >= 1) return frame;
        var n = frame.Length;
        var result = new Rgb[n];
        for (var i = 0; i < n; i++)
        {
            var x = n == 1 ? 0 : i / (double)(n - 1);
            double k;
            var tint = 0.0;
            switch (style)
            {
                case "sunrise": k = Smooth((visible * 1.4 - x) / 0.4); break;
                case "center": k = Smooth((visible * 1.3 - Math.Abs(x - 0.5) * 2) / 0.35); break;
                case "darkwave": k = Smooth((visible * 1.4 - (1 - x)) / 0.35); break;       // a dark wave runs from the far end
                case "soft": k = visible; break;
                default: k = Smooth((visible * 1.5 - x) / 0.35); tint = (1 - k) * 0.9 * (k > 0 ? 1 : 0); break;   // blaze: a front of fire runs along the lights
            }
            var c = frame[i].Scale(k);
            if (tint > 0) c = Rgb.Max(c, new Rgb(255, 110, 20).Scale(Math.Min(1, tint) * Math.Min(1, k * 2.2) * 0.8));
            result[i] = c;
        }
        return result;
    }
}

/// <summary>The inputs that change from moment to moment, and the look chosen on the page.</summary>
public sealed record RgbLook(RgbEffect Effect, Rgb Color, double Brightness, double Speed, Rgb[]? CustomPalette = null, RgbPattern? CustomPattern = null, bool Reverse = false, bool Starry = false, double Spread = 1);

/// <summary>Turns an effect, the time and a few live numbers into the colour of every LED. Pure maths, no clock and no device: the same inputs always give the
/// same frame, so every effect can be tested, drawn in the preview and sent to hardware by the same code.</summary>
public static class RgbEngine
{
    /// <summary>One frame: <paramref name="leds"/> colours at time <paramref name="t"/> seconds. <paramref name="level"/> is the live number the music and
    /// temperature effects follow (0 to 1).</summary>
    public static Rgb[] Frame(RgbLook look, int leds, double t, double level = 0)
    {
        leds = Math.Clamp(leds, 1, 1024);
        var effect = look.Effect;
        var pattern = look.CustomPattern ?? effect.Pattern;
        var palette = look.CustomPalette is { Length: > 0 } custom ? custom : effect.UsesColor ? Basic(look.Color) : effect.Colors;
        var speed = Math.Clamp(look.Speed, 0.1, 5) * effect.Speed;
        var frame = new Rgb[leds];
        for (var i = 0; i < leds; i++)
        {
            var x = leds == 1 ? 0 : i / (double)(leds - 1);
            frame[i] = Pixel(pattern, palette, i, leds, x, t, speed, level, look.Color, Math.Max(0.1, look.Spread * effect.Span));
        }
        if (look.Starry && pattern is not (RgbPattern.Solid or RgbPattern.Flash or RgbPattern.DoubleFlash or RgbPattern.Temperature or RgbPattern.Music or RgbPattern.Sparkle))
        {
            // «starry»: the flow stays as a dim base and a few of its own colours twinkle bright on top of it
            var slot = (int)Math.Floor(t * 2.2 * speed);
            var age = Frac(t * 2.2 * speed);
            for (var i = 0; i < leds; i++)
            {
                var star = Hash(i + 11, slot) < 0.2 ? Math.Pow(1 - age, 1.4) : 0;
                frame[i] = Rgb.Max(frame[i].Scale(0.28), frame[i].Scale(star));
            }
        }
        if (look.Reverse) Array.Reverse(frame);
        var b = Math.Clamp(look.Brightness, 0, 1);
        if (b < 0.9999)
            for (var i = 0; i < leds; i++) frame[i] = frame[i].Scale(b);
        return frame;
    }

    /// <summary>The colour and a softer second tone of it, so a basic effect has a little depth.</summary>
    private static Rgb[] Basic(Rgb colour)
    {
        var second = Rgb.FromHsv(colour.Hue + 0.08, 0.85, 1);
        return [colour, second];
    }

    private static Rgb Gradient(Rgb[] palette, double pos)
    {
        if (palette.Length == 1) return palette[0];
        pos = (pos % 1 + 1) % 1 * palette.Length;
        var a = (int)pos % palette.Length;
        var b = (a + 1) % palette.Length;
        var f = pos - Math.Floor(pos);
        f = f * f * (3 - 2 * f);                          // smooth step: no visible bands between colours
        return Rgb.Blend(palette[a], palette[b], f);
    }

    private static double Hash(int a, int b)
    {
        unchecked
        {
            var h = (uint)(a * 374761393 + b * 668265263);
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (double)0x1000000;
        }
    }

    private static double Noise(double x, double y)
    {
        int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
        double fx = x - xi, fy = y - yi;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        var top = Hash(xi, yi) + (Hash(xi + 1, yi) - Hash(xi, yi)) * fx;
        var bottom = Hash(xi, yi + 1) + (Hash(xi + 1, yi + 1) - Hash(xi, yi + 1)) * fx;
        return top + (bottom - top) * fy;
    }

    private static double Frac(double v) => v - Math.Floor(v);

    private static Rgb Pixel(RgbPattern pattern, Rgb[] p, int i, int n, double x, double t, double speed, double level, Rgb user, double spread = 1)
    {
        switch (pattern)
        {
            case RgbPattern.Solid:
                return p[0];
            case RgbPattern.Breathe:
            {
                var v = (Math.Sin(2 * Math.PI * t * 0.45 * speed - Math.PI / 2) + 1) / 2;
                return Gradient(p, 0).Scale(0.04 + 0.96 * v * v);
            }
            case RgbPattern.Flash:
                return Frac(t * 0.9 * speed) < 0.5 ? p[0] : Rgb.Black;
            case RgbPattern.DoubleFlash:
            {
                var f = Frac(t * 0.8 * speed);
                return f < 0.1 || f is >= 0.2 and < 0.3 ? p[0] : Rgb.Black;
            }
            case RgbPattern.Random:
            {
                var slot = (int)Math.Floor(t * 1.5 * speed);
                return Rgb.FromHsv(Hash(i + 1, slot), 0.9, 1);
            }
            case RgbPattern.Rainbow:
                return Rgb.FromHsv(x * spread - t * 0.12 * speed, 1, 1);          // spread is the part of the colour circle laid along one light
            case RgbPattern.Cycle:
                return Rgb.FromHsv(t * 0.08 * speed, 1, 1);
            case RgbPattern.Wave:
                return Gradient(p, x * spread - t * 0.18 * speed);
            case RgbPattern.Chase:
            {
                var head = Frac(t * 0.45 * speed) * n;
                var behind = (head - i + n) % n;
                var tail = Math.Max(2.5, n * 0.28);
                var v = Math.Max(0, 1 - behind / tail);
                return Gradient(p, x * 0.5 + t * 0.05 * speed).Scale(0.06 + 0.94 * v * v);
            }
            case RgbPattern.Meteor:
            {
                var head = Frac(t * 0.4 * speed) * (n + n * 0.4) ;
                var behind = head - i;
                var tail = Math.Max(3, n * 0.4);
                if (behind < 0 || behind > tail) return Gradient(p, 1).Scale(0.04);
                var v = 1 - behind / tail;
                var flicker = 0.75 + 0.25 * Noise(i * 1.7, t * 8 * speed);
                return Gradient(p, behind / tail).Scale(v * v * flicker);
            }
            case RgbPattern.Scan:
            {
                var ping = Math.Abs(Frac(t * 0.3 * speed) * 2 - 1) * (n - 1);
                var v = Math.Max(0, 1 - Math.Abs(ping - i) / Math.Max(2, n * 0.14));
                return Gradient(p, 0).Scale(0.04 + 0.96 * v);
            }
            case RgbPattern.Stack:
            {
                var phase = Frac(t * 0.25 * speed) * 2;
                var filling = phase < 1;
                var level2 = (filling ? phase : phase - 1) * n;
                var on = filling ? i < level2 : i >= level2;
                return on ? Gradient(p, x) : Rgb.Black;
            }
            case RgbPattern.Sparkle:
            {
                var rate = 3.0 * speed;
                var slot = (int)Math.Floor(t * rate);
                var age = Frac(t * rate);
                var lit = Hash(i + 7, slot) < 0.16;
                var v = lit ? Math.Pow(1 - age, 1.6) : 0;
                var baseColour = Gradient(p, x * 0.5 + t * 0.03 * speed).Scale(0.12);
                return Rgb.Max(baseColour, Gradient(p, Hash(i, slot)).Scale(v));
            }
            case RgbPattern.Heartbeat:
            {
                var f = Frac(t * 0.85 * speed);
                var v = Math.Exp(-Math.Pow((f - 0.08) / 0.05, 2)) + 0.7 * Math.Exp(-Math.Pow((f - 0.28) / 0.06, 2));
                return p[0].Scale(0.05 + 0.95 * Math.Min(1, v));
            }
            case RgbPattern.Siren:
            {
                var phase = (int)Math.Floor(t * 2.2 * speed) % 2;
                var left = i < n / 2;
                var a = p[0];
                var b = p.Length > 1 ? p[1] : p[0];
                var pick = (left ? phase == 0 : phase == 1) ? a : b;
                var inPhase = Frac(t * 2.2 * speed);
                return pick.Scale(0.15 + 0.85 * (1 - inPhase * inPhase));
            }
            case RgbPattern.Lightning:
            {
                var slot = (int)Math.Floor(t * 2.5 * speed);
                var age = Frac(t * 2.5 * speed);
                var strike = Hash(slot, 91) < 0.45;
                var zone = Hash(slot, 17);
                var near = 1 - Math.Min(1, Math.Abs(x - zone) * 2.2);
                var v = strike ? Math.Pow(1 - age, 3) * (0.35 + 0.65 * near) : 0;
                var bg = Gradient(p, x + t * 0.04 * speed).Scale(0.10);
                return Rgb.Max(bg, Gradient(p, Hash(slot, 3)).Scale(v));
            }
            case RgbPattern.Ripple:
            {
                var d = Math.Abs(x - 0.5) * 2;
                var front = Frac(t * 0.3 * speed);
                var v = Math.Max(0, 1 - Math.Abs(d - front) * 5);
                return Gradient(p, d + t * 0.05).Scale(0.05 + 0.95 * v);
            }
            case RgbPattern.Fire:
            {
                var heat = Math.Clamp(0.25 + 0.75 * Noise(x * 4, t * 1.6 * speed) * (1 - Math.Abs(x - 0.5) * 0.4) + 0.2 * Noise(x * 9, t * 3.1 * speed) - 0.15, 0, 1);
                return FireColour(p, heat);
            }
            case RgbPattern.Temperature:
            {
                // green when cool, through yellow to red when hot: hue 0.33 (green) down to 0 (red)
                var l = Math.Clamp(level, 0, 1);
                var bar = x <= l + 0.0001 || i == 0;
                var colour = Rgb.FromHsv(0.33 * (1 - l), 1, 1);
                return bar ? colour : colour.Scale(0.10);
            }
            case RgbPattern.Music:
            {
                var l = Math.Clamp(level, 0, 1);
                var bar = (1 - Math.Abs(x - 0.5) * 2) <= l * 1.05 + 0.02;
                var colour = Gradient(p, x + t * 0.05);
                return bar ? colour.Scale(0.35 + 0.65 * l) : colour.Scale(0.06);
            }
            default:
                return p[0];
        }
    }

    /// <summary>Embers to flame tips: the palette's fourth colour (or black) is the glow of the coals, then its first, second and third colours get hotter.</summary>
    private static Rgb FireColour(Rgb[] p, double heat)
    {
        Rgb[] stops = [p.Length > 3 ? p[3] : Rgb.Black, p[0], p.Length > 1 ? p[1] : p[0], p.Length > 2 ? p[2] : p[^1]];
        var at = Math.Clamp(heat, 0, 1) * (stops.Length - 1);
        var lo = Math.Min(stops.Length - 2, (int)Math.Floor(at));
        return Rgb.Lerp(stops[lo], stops[lo + 1], at - lo).Scale(0.25 + 0.75 * Math.Clamp(heat, 0, 1));
    }
    /// <summary>The part of a long frame between <paramref name="from"/> and <paramref name="to"/> (0 to 1 of its length), stretched to <paramref name="count"/> LEDs:
    /// how every light of the PC takes its colour from one single flowing effect when the lights are synchronised.</summary>
    public static Rgb[] Slice(IReadOnlyList<Rgb> frame, double from, double to, int count)
    {
        if (frame.Count == 0) return new Rgb[Math.Max(1, count)];
        var a = Math.Clamp((int)Math.Floor(from * frame.Count), 0, frame.Count - 1);
        var b = Math.Clamp((int)Math.Ceiling(to * frame.Count), a + 1, frame.Count);
        return Resample([.. frame.Skip(a).Take(b - a)], count);
    }

    /// <summary>The frame stretched or squeezed to <paramref name="count"/> LEDs, so one zone's frame can drive a device with any LED count.</summary>
    public static Rgb[] Resample(IReadOnlyList<Rgb> frame, int count)
    {
        count = Math.Max(1, count);
        if (frame.Count == 0) return new Rgb[count];
        if (frame.Count == count) return [.. frame];
        var result = new Rgb[count];
        for (var i = 0; i < count; i++)
        {
            var at = count == 1 ? 0 : i * (frame.Count - 1) / (double)(count - 1);
            var lo = (int)Math.Floor(at);
            var hi = Math.Min(frame.Count - 1, lo + 1);
            result[i] = Rgb.Lerp(frame[lo], frame[hi], at - lo);
        }
        return result;
    }
}
