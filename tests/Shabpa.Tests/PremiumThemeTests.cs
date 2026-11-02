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
}
