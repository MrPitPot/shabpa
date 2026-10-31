namespace Shabpa.Core;

// Boofi's wardrobe: 90 parts in six slots (hat, glasses, neck, watch, lantern, fur) and five rarities, bought with feathers.
// The art is a stack of transparent pictures in one frame (see Assets/boofi and brand/boofi-parts/source/export-app.js).

/// <param name="FurIndex">For fur parts: the number in the owl pictures (o_N_*, k_N); -1 for the others.</param>
public sealed record BoofiPart(string Id, string Slot, string Rarity, string Fa, string Note, int Price, bool HasFront, bool HasBehind, int FurIndex, bool IsLantern)
{
    public string Name => Fa;
    public bool Free => Price == 0;
}

public static partial class BoofiCatalog
{
    public static readonly string[] Slots = ["hat", "glasses", "neck", "watch", "lantern", "fur"];
    public static readonly string[] Rarities = ["common", "rare", "epic", "legend", "season"];
    public const string DefaultFur = "fu-night";

    private static readonly Lazy<Dictionary<string, BoofiPart>> ById = new(() => All.ToDictionary(p => p.Id));

    public static BoofiPart? Find(string id) => ById.Value.GetValueOrDefault(id);

    public static string SlotName(string slot) => slot switch
    {
        "hat" => L.T("کلاه", "Hat"), "glasses" => L.T("عینک", "Glasses"), "neck" => L.T("گردن", "Neck"),
        "watch" => L.T("ساعت", "Watch"), "lantern" => L.T("فانوس", "Lantern"), _ => L.T("رنگ پر", "Fur"),
    };

    public static string RarityName(string r) => r switch
    {
        "common" => L.T("معمولی", "Common"), "rare" => L.T("کمیاب", "Rare"), "epic" => L.T("حماسی", "Epic"),
        "legend" => L.T("افسانه‌ای", "Legendary"), _ => L.T("فصلی", "Seasonal"),
    };

    /// <summary>The colour of a rarity (#RRGGBB).</summary>
    public static string RarityColor(string r) => r switch
    {
        "common" => "#9FA3E8", "rare" => "#4CC9F0", "epic" => "#C084FC", "legend" => "#FFD27A", _ => "#FF6B81",
    };

    /// <summary>The picture files (inside Assets/boofi) to stack, back to front, for a set of worn parts (slot → id).</summary>
    public static List<string> Layers(IReadOnlyDictionary<string, string> worn)
    {
        BoofiPart? Get(string slot) => worn.TryGetValue(slot, out var id) ? Find(id) : null;
        var fur = Get("fur") is { FurIndex: >= 0 } f ? f.FurIndex : Find(DefaultFur)!.FurIndex;
        var list = new List<string>();
        if (FurWithBehind.Contains(fur)) list.Add($"k_{fur}.png");
        var order = new[] { "neck", "glasses", "hat", "watch", "lantern" };
        foreach (var slot in order) if (Get(slot) is { HasBehind: true } p) list.Add($"{p.Id}_b.png");
        list.Add($"o_{fur}_{(Get("lantern") is null ? 1 : 0)}_{(Get("hat") is null ? 1 : 0)}.png");
        foreach (var slot in order) if (Get(slot) is { HasFront: true } p) list.Add($"{p.Id}.png");
        return list;
    }

    /// <summary>The part of the 240x240 frame that shows a slot best (centre x, centre y, width), for the small store pictures.</summary>
    public static (double X, double Y, double W) Focus(string slot) => slot switch
    {
        "hat" => (100, 46, 172), "glasses" => (100, 100, 128), "neck" => (100, 140, 176),
        "watch" => (64, 118, 138), "lantern" => (128, 108, 190), _ => (100, 110, 208),
    };

    public static readonly (double X, double Y, double W) Whole = (100, 108, 236);
}
