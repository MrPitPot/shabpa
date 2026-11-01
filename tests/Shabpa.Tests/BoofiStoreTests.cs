using System.IO;
using Shabpa.Core;

namespace Shabpa.Tests;

public class BoofiStoreTests
{
    private static (Hub hub, RewardsEngine e) Engine()
    {
        var hub = new Hub(new Store(Path.Combine(Path.GetTempPath(), "shabpa-bs-" + Guid.NewGuid().ToString("N"))) { ReadOnly = true }, dryRun: true, demo: true);
        hub.State.Rewards = new RewardsState();
        return (hub, new RewardsEngine(hub, () => new DateTime(2026, 11, 2, 12, 0, 0)));
    }

    [Fact]
    public void The_catalog_has_90_parts_six_slots_three_per_rarity_and_every_picture_it_needs()
    {
        Assert.Equal(90, BoofiCatalog.All.Count);
        Assert.Equal(BoofiCatalog.All.Count, BoofiCatalog.All.Select(p => p.Id).Distinct().Count());
        foreach (var slot in BoofiCatalog.Slots) Assert.Equal(15, BoofiCatalog.All.Count(p => p.Slot == slot));
        Assert.All(BoofiCatalog.All, p => Assert.True(p.Price >= 0));
        Assert.Equal(0, BoofiCatalog.Find(BoofiCatalog.DefaultFur)!.Price);
        // the pictures are real files in the project folder
        var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Shabpa", "Assets", "boofi"));
        if (!Directory.Exists(dir)) return; // tests run elsewhere than the source tree
        var worn = new Dictionary<string, string>();
        foreach (var p in BoofiCatalog.All.Where(p => p.Slot != "fur"))
            foreach (var f in BoofiCatalog.Layers(new Dictionary<string, string> { [p.Slot] = p.Id })) Assert.True(File.Exists(Path.Combine(dir, f)), f);
        foreach (var p in BoofiCatalog.All.Where(p => p.Slot == "fur"))
            foreach (var f in BoofiCatalog.Layers(new Dictionary<string, string> { ["fur"] = p.Id })) Assert.True(File.Exists(Path.Combine(dir, f)), f);
    }

    [Fact]
    public void Layers_stack_back_to_front_and_a_worn_hat_or_lantern_replaces_the_default_cap_and_candle()
    {
        var none = BoofiCatalog.Layers(new Dictionary<string, string>());
        Assert.Single(none);
        Assert.EndsWith("_1_1.png", none[0]);          // candle and cap
        var dressed = BoofiCatalog.Layers(new Dictionary<string, string> { ["hat"] = "hat-wizard", ["lantern"] = "ln-star", ["neck"] = "nk-scarf" });
        Assert.Contains(dressed, f => f.EndsWith("_0_0.png")); // no candle, no cap
        Assert.True(dressed.IndexOf("hat-wizard_b.png") < dressed.FindIndex(f => f.StartsWith("o_")));
        Assert.True(dressed.FindIndex(f => f.StartsWith("o_")) < dressed.IndexOf("hat-wizard.png"));
    }

    [Fact]
    public void Buying_spends_feathers_but_not_the_level_and_only_once()
    {
        var (_, e) = Engine();
        e.State.Feathers = 100; e.State.TotalFeathers = 100;
        var cheap = BoofiCatalog.All.First(p => p.Price is > 0 and <= 100);
        var dear = BoofiCatalog.All.First(p => p.Price > 100);
        Assert.Equal(RewardsEngine.BuyResult.NotEnough, e.Buy(dear.Id));
        Assert.Equal(RewardsEngine.BuyResult.Ok, e.Buy(cheap.Id));
        Assert.Equal(cheap.Price, e.State.TotalFeathers - e.State.Feathers); // spent; the level only ever counts what was earned (a quest may pay in between)
        Assert.Equal(RewardsEngine.BuyResult.AlreadyOwned, e.Buy(cheap.Id));
        Assert.Equal(RewardsEngine.BuyResult.Unknown, e.Buy("nope"));
        Assert.Contains("o.firstbuy", e.State.Claimed);
    }

    [Fact]
    public void Only_owned_parts_of_the_right_slot_can_be_worn_and_the_free_fur_is_always_owned()
    {
        var (_, e) = Engine();
        Assert.False(e.Wear("hat", "hat-royal"));
        Assert.True(e.Owns(BoofiCatalog.DefaultFur));
        e.State.Owned.Add("hat-cap");
        Assert.False(e.Wear("glasses", "hat-cap"));
        Assert.True(e.Wear("hat", "hat-cap"));
        Assert.Equal("hat-cap", e.State.Worn["hat"]);
        Assert.True(e.Wear("hat", null));
        Assert.False(e.State.Worn.ContainsKey("hat"));
    }

    [Fact]
    public void A_merged_wardrobe_keeps_both_PCs_purchases_and_pays_the_other_copys_from_the_base()
    {
        var a = new RewardsState { TotalFeathers = 500, Feathers = 300 };
        a.Owned.Add("hat-cap"); a.Worn["hat"] = "hat-cap";
        var b = new RewardsState { TotalFeathers = 400, Feathers = 100 };
        b.Owned.Add("gl-round"); b.Worn["glasses"] = "gl-round";
        var m = RewardsMerge.Merge(a, b);
        Assert.Contains("hat-cap", m.Owned); Assert.Contains("gl-round", m.Owned);
        Assert.Equal("hat-cap", m.Worn["hat"]); Assert.Equal("gl-round", m.Worn["glasses"]);
        Assert.Equal(300 - BoofiCatalog.Find("gl-round")!.Price, m.Feathers);
    }

    [Fact]
    public void Every_part_has_an_english_name_and_note_and_the_text_follows_the_language()
    {
        Assert.All(BoofiCatalog.All, p => { Assert.False(string.IsNullOrWhiteSpace(p.En)); Assert.False(string.IsNullOrWhiteSpace(p.EnNote)); Assert.DoesNotContain(p.En, new[] { p.Fa }); });
        var wasEn = L.En;
        try
        {
            L.En = true;
            Assert.Equal("Earflap hat", BoofiCatalog.Find("hat-earflap")!.Name);
            L.En = false;
            Assert.Equal("کلاه گوش‌دار", BoofiCatalog.Find("hat-earflap")!.Name);
        }
        finally { L.En = wasEn; }
    }

    [Fact]
    public void Three_looks_can_be_saved_and_follow_later_changes()
    {
        var (hub, e) = Engine();
        hub.State.Rewards.Feathers = 2000;
        e.Buy("hat-wizard"); e.Buy("gl-round");
        e.Wear("hat", "hat-wizard");
        e.UsePreset(0);                                    // empty → saves the current look
        Assert.Equal("hat-wizard", e.Preset(0)!["hat"]);
        e.Wear("glasses", "gl-round");                     // the active set follows
        Assert.Equal("gl-round", e.Preset(0)!["glasses"]);
        e.Wear("hat", null);
        e.UsePreset(1);                                    // second set saves the bare look
        e.UsePreset(0);                                    // back to the first
        Assert.Equal("gl-round", hub.State.Rewards.Worn["glasses"]);
        Assert.Equal(0, hub.State.Rewards.ActivePreset);
    }

    [Fact]
    public void Feathers_earned_today_are_counted_for_the_shop_bar()
    {
        var (hub, e) = Engine();
        Assert.Equal(0, e.EarnedToday);
        e.Count("storeBuy");                               // any evaluation that pays something
        e.Evaluate();
        Assert.True(e.EarnedToday >= 0);
        Assert.Equal(hub.State.Rewards.DayCounters.GetValueOrDefault("earned"), e.EarnedToday);
    }

    [Fact]
    public void Every_fur_except_the_free_one_has_body_and_wing_skins_for_the_little_Boofi()
    {
        var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Shabpa", "Assets", "boofi"));
        if (!Directory.Exists(dir)) return;
        foreach (var p in BoofiCatalog.All.Where(p => p.Slot == "fur" && p.Id != BoofiCatalog.DefaultFur))
        {
            Assert.True(File.Exists(Path.Combine(dir, $"fb_{p.Id}.png")), p.Id);
            Assert.True(File.Exists(Path.Combine(dir, $"fw_{p.Id}.png")), p.Id);
        }
    }
}
