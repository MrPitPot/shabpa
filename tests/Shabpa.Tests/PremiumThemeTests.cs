using System.IO;
using Shabpa.Core;

namespace Shabpa.Tests;

public class PremiumThemeTests
{
    public static IEnumerable<object[]> Keys() => PremiumTheme.All.Select(t => new object[] { t.Key });

    [Fact]
    public void There_are_ten_with_their_own_keys_scenes_and_prices_and_none_repeats_a_free_theme()
    {
        Assert.True(PremiumTheme.All.Count >= 10);
        Assert.Equal(PremiumTheme.All.Count, PremiumTheme.All.Select(t => t.Key).Distinct().Count());
        Assert.Equal(PremiumTheme.All.Count, PremiumTheme.All.Select(t => t.Scene).Distinct().Count()); // every one has its own backdrop
        Assert.DoesNotContain(PremiumTheme.All, t => Palette.All.Any(p => p.Key == t.Key));
        Assert.All(PremiumTheme.All, t =>
        {
            Assert.NotEqual(Scene.None, t.Scene);
            Assert.True(t.Price is > 0 and < 10000);
            Assert.False(string.IsNullOrWhiteSpace(t.Fa) || string.IsNullOrWhiteSpace(t.En) || string.IsNullOrWhiteSpace(t.NoteFa) || string.IsNullOrWhiteSpace(t.NoteEn));
        });
        Assert.All(PremiumTheme.All, t => Assert.Same(t.Palette, Palette.Find(t.Key)));
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public void Each_defines_every_colour_and_stays_readable(string key)
    {
        var p = Palette.Find(key);
        Assert.Equal(Palette.Keys.Order(), p.Colors.Keys.Order());
        Assert.All(p.Colors.Values, hex => Assert.Matches("^[0-9A-F]{6}$", hex));
        Assert.Equal(p.Light, p.Rgb("Bg").g > p.Rgb("Text").g);
        void AtLeast(double ratio, string fg, string bg) => Assert.True(p.Contrast(fg, bg) >= ratio, $"{key}: {fg} on {bg} is {p.Contrast(fg, bg):0.00}, needs {ratio}");
        foreach (var surface in new[] { "Bg", "Bg2", "Side", "Side2", "Panel", "Panel2", "Panel3", "Hero", "Hero2", "AccentSoft" }) AtLeast(7, "Text", surface);
        foreach (var surface in new[] { "Bg", "Bg2", "Side", "Side2", "Panel", "Panel2" }) AtLeast(4.5, "Muted", surface);
        foreach (var surface in new[] { "Side", "Panel", "Panel2" }) AtLeast(3, "Faint", surface);
        foreach (var colour in new[] { "Accent", "Gold", "Green", "Red" }) { AtLeast(4.5, colour, "Panel"); AtLeast(4.5, colour, "Panel2"); AtLeast(4.5, colour, colour + "Soft"); }
        AtLeast(4.5, "Gold", "Hero"); AtLeast(4.5, "Gold", "Hero2");
        AtLeast(3.5, "OnAccent", "Accent1"); AtLeast(3.5, "OnAccent", "Accent2");
    }

    [Fact]
    public void Buying_a_theme_spends_feathers_once_and_keeps_the_level()
    {
        var hub = new Hub(new Store(Path.Combine(Path.GetTempPath(), "shabpa-pt-" + Guid.NewGuid().ToString("N"))) { ReadOnly = true }, dryRun: true, demo: true);
        hub.State.Rewards = new RewardsState { Feathers = 1000, TotalFeathers = 1000 };
        var e = new RewardsEngine(hub, () => new DateTime(2026, 11, 2, 12, 0, 0));
        var cheap = PremiumTheme.All.OrderBy(t => t.Price).First();
        var dear = PremiumTheme.All.First(t => t.Price > 1000);
        Assert.False(e.OwnsTheme(cheap.Key));
        Assert.Equal(RewardsEngine.BuyResult.NotEnough, e.BuyTheme(dear.Key));
        Assert.Equal(RewardsEngine.BuyResult.Ok, e.BuyTheme(cheap.Key));
        Assert.True(e.OwnsTheme(cheap.Key));
        Assert.Equal(RewardsEngine.BuyResult.AlreadyOwned, e.BuyTheme(cheap.Key));
        Assert.Equal(RewardsEngine.BuyResult.Unknown, e.BuyTheme("nope"));
        Assert.Equal(cheap.Price, e.State.TotalFeathers - e.State.Feathers);
    }

    [Fact]
    public void Every_backdrop_runs_at_a_low_frame_rate_so_a_weak_graphics_card_is_not_taxed()
    {
        foreach (var scene in Enum.GetValues<Scene>().Where(s => s != Scene.None))
        {
            var fps = Shabpa.Kit.ThemeScene.FpsFor(scene);
            Assert.InRange(fps, 8, 18);                    // a 60 Hz (or 144 Hz) redraw of the whole window for a slow drift was the cause of 8-14 % GPU
        }
        Assert.Equal(30, PremiumTheme.All.Count);
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public void Each_has_its_painting_and_every_sprite_its_layers_use(string key)
    {
        var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Shabpa", "Assets", "themes"));
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "scenes.json")));
        Assert.True(doc.RootElement.TryGetProperty(key, out var spec), $"{key} has no entry in scenes.json");
        Assert.Equal(PremiumTheme.All.Single(t => t.Key == key).Scene.ToString(), spec.GetProperty("scene").GetString());
        Assert.True(File.Exists(Path.Combine(dir, key + ".jpg")), $"{key}.jpg is missing");
        Assert.True(File.Exists(Path.Combine(dir, key + ".thumb.jpg")), $"{key}.thumb.jpg is missing");
        var sprites = spec.GetProperty("sprites");
        foreach (var s in sprites.EnumerateObject()) Assert.True(File.Exists(Path.Combine(dir, $"{key}.{s.Name}.png")), $"{key}.{s.Name}.png is missing");
        foreach (var layer in spec.GetProperty("layers").EnumerateArray())
        {
            var sp = layer.GetProperty("sprite");
            var names = sp.ValueKind == System.Text.Json.JsonValueKind.Array ? sp.EnumerateArray().Select(n => n.GetString()!).ToArray() : [sp.GetString()!];
            foreach (var n in names.Where(n => !n.StartsWith('#'))) Assert.True(sprites.TryGetProperty(n, out _), $"{key}: a layer uses the unknown sprite {n}");
            if (layer.TryGetProperty("fps", out var fps)) Assert.InRange(fps.GetInt32(), 4, 24);   // only quick streaks (meteors, tracers) go above the scene rate
        }
    }
}
