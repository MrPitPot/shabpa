using Shabpa.Core;
using Xunit;

namespace Shabpa.Tests;

/// <summary>Contrast audit of what is really on screen: the translucent glass layers (Mica veil, Card, Card2, Flyout) are
/// composited over the brightest/darkest wallpaper blooms of the theme, then the text colours are measured on the result.
/// Main text needs 4.5:1, secondary (Muted) 4.5:1, Faint (disabled/hints) 3.5:1.</summary>
public class GlassContrastTests
{
    public static TheoryData<string> Themes => [.. Palette.All.Select(p => p.Key)];

    public static double Lum(uint rgb)
    {
        static double C(uint v) { var s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * C((rgb >> 16) & 255) + 0.7152 * C((rgb >> 8) & 255) + 0.0722 * C(rgb & 255);
    }

    public static double Ratio(uint a, uint b)
    {
        var x = Lum(a); var y = Lum(b);
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    /// <summary>Source-over of a 0xAARRGGBB layer on an opaque colour (what WPF does in sRGB).</summary>
    public static uint Over(uint layer, uint below)
    {
        var a = (layer >> 24) / 255.0;
        return GlassTokens.Mix(below & 0xFFFFFF, layer & 0xFFFFFF, a);
    }

    public static IEnumerable<(string name, uint rgb)> Surfaces(Palette p)
    {
        var t = GlassTokens.Of(p);
        // wallpaper under the cards: base colour, or any single bloom at its strongest
        foreach (var (wallName, wall) in new[] { ("base", t.WallBase), ("w1", t.Wall1), ("w2", t.Wall2), ("w3", t.Wall3), ("w4", t.Wall4) })
        {
            var plate = Over(t.Mica, wall);
            yield return ($"Card on {wallName}", Over(t.Card, plate));
            yield return ($"Card2 on {wallName}", Over(t.Card2, plate));
            yield return ($"Flyout on {wallName}", Over(t.Flyout, plate));
            yield return ($"Plate {wallName}", plate);
        }
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Text_is_readable_on_the_real_glass_surfaces(string theme)
    {
        var p = Palette.Find(theme);
        var bad = new List<string>();
        foreach (var (name, surface) in Surfaces(p))
        {
            foreach (var (text, need) in new[] { ("Text", 4.5), ("Muted", 4.5), ("Faint", 3.5) })
            {
                var r = Ratio(GlassTokens.Rgb(p.Colors[text]), surface);
                if (r < need) bad.Add($"{text} on {name} = {r:0.00} (needs {need})");
            }
        }
        Assert.True(bad.Count == 0, $"{theme}: " + string.Join("; ", bad));
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Card_edge_is_visible_but_not_harsh(string theme)
    {
        var p = Palette.Find(theme);
        var t = GlassTokens.Of(p);
        foreach (var (name, surface) in Surfaces(p).Where(s => s.name.StartsWith("Card on")))
        {
            var edge = Over(t.Line, surface);
            var r = Ratio(edge, surface);
            Assert.True(r >= 1.12 && r <= 3.2, $"{theme}: edge on {name} contrast {r:0.00}");
        }
    }
}

/// <summary>Glass (Mica/Acrylic) needs Windows 11 22H2; the Windows version is faked here.</summary>
public class GlassSupportTests
{
    [Theory]
    [InlineData(19045, false)] // Windows 10 22H2
    [InlineData(22000, false)] // Windows 11 21H2: no system backdrop attribute
    [InlineData(22621, true)]  // Windows 11 22H2
    [InlineData(26100, true)]
    public void Support_depends_on_the_windows_build(int build, bool supported) => Assert.Equal(supported, Shabpa.Ui.GlassSupported(build));

    [Theory]
    [InlineData("mica", 19045, "off")]
    [InlineData("acrylic", 22000, "off")]
    [InlineData("mica", 22621, "mica")]
    [InlineData("acrylic", 26100, "acrylic")]
    [InlineData("junk", 26100, "off")]
    public void A_saved_choice_falls_back_to_the_normal_theme_when_unsupported(string saved, int build, string expected) =>
        Assert.Equal(expected, Shabpa.Ui.EffectiveGlass(saved, build));
}
