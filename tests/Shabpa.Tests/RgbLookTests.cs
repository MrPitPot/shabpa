using Shabpa.Core;

namespace Shabpa.Tests;

/// <summary>The look of the lights: a rainbow is two or three neighbouring colours on every light (not the whole circle, not one colour per device), blends stay rich, and
/// colours are corrected for LEDs so mixed colours do not look pale.</summary>
public class RgbLookTests
{
    private static double HueSpan(Rgb[] frame)
    {
        var hues = frame.Select(c => c.Hue).ToList();
        // the span of hues along the strip, measured the short way round (no jump at red)
        double total = 0;
        for (var i = 1; i < hues.Count; i++)
        {
            var d = hues[i] - hues[i - 1];
            if (d > 0.5) d -= 1; else if (d < -0.5) d += 1;
            total += Math.Abs(d);
        }
        return total;
    }

    [Fact]
    public void A_rainbow_shows_two_or_three_neighbouring_colours_on_every_light_and_every_length()
    {
        var look = new RgbLook(RgbEffects.Find("rainbow"), new Rgb(255, 45, 85), 1, 1);
        foreach (var leds in new[] { 8, 12, 36, 120 })
            Assert.InRange(HueSpan(RgbEngine.Frame(look, leds, 3.0)), 0.25, 0.45);
        Assert.InRange(HueSpan(RgbEngine.Frame(look with { Effect = RgbEffects.Find("rainbowwide") }, 60, 3.0)), 0.9, 1.0);
    }

    [Fact]
    public void The_rainbow_runs_slowly_and_without_jumps()
    {
        var look = new RgbLook(RgbEffects.Find("rainbow"), new Rgb(255, 45, 85), 1, 1);
        var a = RgbEngine.Frame(look, 20, 5.00)[10];
        var b = RgbEngine.Frame(look, 20, 5.05)[10];
        Assert.True(Math.Abs(a.Hue - b.Hue) < 0.02 || Math.Abs(a.Hue - b.Hue) > 0.98);
        Assert.True(RgbEngine.Frame(look, 20, 5)[0].Saturation > 0.99);                      // fully saturated, never whitish
    }

    [Fact]
    public void Blending_two_saturated_colours_stays_saturated_where_a_straight_mix_goes_grey()
    {
        var blue = new Rgb(0, 0, 255);
        var yellow = new Rgb(255, 255, 0);
        Assert.True(Rgb.Lerp(blue, yellow, 0.5).Saturation < 0.05);                         // the old mix: plain grey
        Assert.True(Rgb.Blend(blue, yellow, 0.5).Saturation > 0.95);
        Assert.Equal(new Rgb(0, 0, 255), Rgb.Blend(blue, yellow, 0));
        Assert.Equal(new Rgb(255, 255, 0), Rgb.Blend(blue, yellow, 1));
        Assert.Equal(new Rgb(100, 100, 100), Rgb.Blend(new Rgb(100, 100, 100), new Rgb(100, 100, 100), 0.5));
    }

    [Fact]
    public void Colour_correction_makes_pale_mixes_deeper_and_keeps_black_and_full_values()
    {
        Assert.Equal(Rgb.Black, Rgb.Black.ForLight());
        Assert.Equal(new Rgb(255, 255, 255), new Rgb(255, 255, 255).ForLight());
        var pink = new Rgb(255, 158, 207).ForLight();
        Assert.True(pink.Saturation > new Rgb(255, 158, 207).Saturation + 0.2);
        Assert.Equal(255, pink.R);
    }

    [Fact]
    public void Every_effect_has_a_unique_key_and_valid_colours_and_the_new_flowing_looks_have_several_hues()
    {
        var all = RgbEffects.All;
        Assert.Equal(all.Count, all.Select(e => e.Key).Distinct().Count());
        foreach (var e in all) Assert.All(e.Palette, hex => Assert.True(Rgb.TryParse(hex, out _), e.Key));
        foreach (var key in new[] { "spectrum", "sunsetdrive", "tropical", "northern", "neonduo", "firewave", "oceandeep" })
        {
            var e = RgbEffects.Find(key);
            Assert.Equal(key, e.Key);
            Assert.True(e.Span < 1 && e.Colors.Length >= 4);
            var frame = RgbEngine.Frame(new RgbLook(e, Rgb.Black, 1, 1), 24, 2);
            Assert.True(frame.Max(c => c.Value) > 0.9);
        }
    }

    [Fact]
    public void In_step_lights_every_device_shows_the_same_colours_at_the_same_moment()
    {
        // two devices of different length laid with the same look at the same time: same first, middle and last colour
        var look = new RgbLook(RgbEffects.Find("rainbow"), new Rgb(255, 45, 85), 1, 1);
        var a = RgbEngine.Frame(look, 12, 7.3);
        var b = RgbEngine.Frame(look, 120, 7.3);
        Assert.Equal(a[0], b[0]);
        Assert.Equal(a[^1], b[^1]);
        Assert.True(Math.Abs(a[6].Hue - b[Math.Min(119, (int)Math.Round(6 * 119 / 11.0))].Hue) < 0.01);
    }
}
