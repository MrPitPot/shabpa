using System.Globalization;

namespace Shabpa.Core;

/// <summary>The "Glass Depth" surface tokens of one theme, as 0xAARRGGBB values (pure data, no WPF). The design's own
/// numbers are used verbatim for "night" and "white"; every other theme gets the same recipe derived from its colours.
/// Palette.Colors stays the 6-digit source of truth for text/accent colours; these are the translucent layers on top.</summary>
public sealed record GlassTokens(
    uint Mica, uint Card, uint Card2, uint Hi, uint Line, uint Flyout, uint Field, uint ScrollThumb,
    uint Shadow, uint ShadowHover, uint ShadowTight, uint OnInk,
    uint WallBase, uint Wall1, uint Wall2, uint Wall3, uint Wall4,
    uint HueA, uint HueB, uint HueC, uint HueD, uint HueE)
{
    public static uint Argb(byte a, uint rgb) => ((uint)a << 24) | (rgb & 0xFFFFFF);

    public static uint Rgb(string hex) => uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture) & 0xFFFFFF;

    /// <summary>Scales brightness (f below 1 darkens, above 1 brightens) keeping the hue.</summary>
    public static uint Scale(uint rgb, double f)
    {
        static uint C(uint v, double f) => (uint)Math.Clamp(Math.Round(v * f), 0, 255);
        return (C((rgb >> 16) & 255, f) << 16) | (C((rgb >> 8) & 255, f) << 8) | C(rgb & 255, f);
    }

    public static uint Mix(uint a, uint b, double t)
    {
        static uint C(uint x, uint y, double t) => (uint)Math.Round(x + (y - x) * t);
        return (C((a >> 16) & 255, (b >> 16) & 255, t) << 16) | (C((a >> 8) & 255, (b >> 8) & 255, t) << 8) | C(a & 255, b & 255, t);
    }

    public static GlassTokens Of(Palette p)
    {
        uint Col(string key) => Rgb(p.Colors[key]);
        if (p.BaseKey is not null && p.Light)
        {
            // the temporary birthday look on a light theme: the design's own "white" surfaces with a pink, gold and violet wallpaper
            return White(p) with
            {
                Mica = Argb(0xBD, Col("Bg")), Shadow = 0x47A0408C, ShadowHover = 0x61A0408C, OnInk = Argb(0xFF, Col("Bg")),
                WallBase = Scale(Col("Bg"), 0.97), Wall1 = 0xFFC4E3, Wall2 = 0xFFE2A8, Wall3 = 0xD9C2FF, Wall4 = 0xFFC0D0,
                HueA = Argb(0xFF, Col("Accent")), HueD = 0xFFC2185B,
            };
        }
        if (p.Key == "night") return Night(p);
        if (p.Key == "white") return White(p);
        var accent = Col("Accent");
        if (!p.Light)
        {
            var halo = Col("Halo");
            return new GlassTokens(
                Mica: Argb(0xCC, Col("Bg")), Card: Argb(0x8C, Col("Panel3")), Card2: Argb(0xB8, Col("Thumb")), Hi: 0x1AFFFFFF, Line: 0x17FFFFFF,
                Flyout: Argb(0xEB, Mix(Col("Panel3"), Col("Thumb"), 0.4)), Field: 0x0FFFFFFF, ScrollThumb: Argb(0x50, Col("Faint")),
                Shadow: 0x99000000, ShadowHover: 0xBF000000, ShadowTight: 0x66000000, OnInk: Argb(0xFF, Scale(Col("Bg"), 0.8)),
                WallBase: Scale(Col("Bg"), 0.8), Wall1: Scale(halo, 0.62), Wall2: Mix(Scale(Col("Accent2"), 0.45), 0x6A2F6B, 0.5),
                Wall3: Mix(Scale(Col("Accent2"), 0.4), 0x14566B, 0.5), Wall4: Mix(Scale(halo, 0.4), Col("Hero"), 0.5),
                HueA: Argb(0xFF, accent), HueB: Argb(0xFF, Col("Green")), HueC: 0xFFFFC764, HueD: 0xFFFF6B9E, HueE: Argb(0xFF, Col("Red")));
        }
        var shadow = Scale(Col("Accent2"), 0.7);
        return new GlassTokens(
            Mica: Argb(0xBD, Col("Bg")), Card: Argb(0xB8, Col("Panel")), Card2: Argb(0xE6, Col("Panel")), Hi: 0xF2FFFFFF, Line: Argb(0x14, Col("Text")),
            Flyout: Argb(0xE0, Col("Panel")), Field: Argb(0x0D, Col("Text")), ScrollThumb: Argb(0x50, Col("Faint")),
            Shadow: Argb(0x47, shadow), ShadowHover: Argb(0x61, shadow), ShadowTight: Argb(0x0F, Col("Text")), OnInk: Argb(0xFF, Col("Bg")),
            WallBase: Scale(Col("Bg"), 0.86), Wall1: Mix(Col("Accent1"), 0xFFFFFF, 0.45), Wall2: 0xFFD0B0, Wall3: 0x9FE3E0, Wall4: 0xF6B8E0,
            HueA: Argb(0xFF, accent), HueB: Argb(0xFF, Col("Green")), HueC: 0xFFB25E00, HueD: 0xFFC2185B, HueE: Argb(0xFF, Col("Red")));
    }

    private static GlassTokens Night(Palette p) => new(
        Mica: 0xCC0C1026, Card: 0x8C1E254C, Card2: 0xB82E386A, Hi: 0x1AFFFFFF, Line: 0x17FFFFFF, Flyout: 0xE61E244E, Field: 0x0FFFFFFF, ScrollThumb: 0x508089AD,
        Shadow: 0x99000000, ShadowHover: 0xBF000000, ShadowTight: 0x66000000, OnInk: 0xFF0B0F24,
        WallBase: 0x070A18, Wall1: 0x3B3A9A, Wall2: 0x6A2F6B, Wall3: 0x14566B, Wall4: 0x4A2A7A,
        HueA: 0xFF8087FF, HueB: 0xFF3DDC97, HueC: 0xFFFFC764, HueD: 0xFFFF6B9E, HueE: 0xFFFF6B7F);

    private static GlassTokens White(Palette p) => new(
        Mica: 0xBDF6F8FD, Card: 0xB8FFFFFF, Card2: 0xE6FFFFFF, Hi: 0xF2FFFFFF, Line: 0x14141A2E, Flyout: 0xE0FFFFFF, Field: 0x0D141A2E, ScrollThumb: 0x50737D99,
        Shadow: 0x473C46AA, ShadowHover: 0x613C46AA, ShadowTight: 0x0F141A2E, OnInk: 0xFFF6F8FD,
        WallBase: 0xCFD6EE, Wall1: 0xB9B6FF, Wall2: 0xFFD0B0, Wall3: 0x9FE3E0, Wall4: 0xF6B8E0,
        HueA: 0xFF4A51E0, HueB: 0xFF0A7349, HueC: 0xFFB25E00, HueD: 0xFFC2185B, HueE: 0xFFC42B1C);
}
