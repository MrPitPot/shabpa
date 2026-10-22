using System.Windows.Media;

namespace Shabpa.Core;

/// <summary>One night-light preset's values: colour temperature in Kelvin (lower = warmer, 6500 = no tint) and darkness 0.05 to 0.92.</summary>
public sealed class PresetValue
{
    public string Id { get; set; } = "";
    public int Kelvin { get; set; } = 6500;
    public double Darkness { get; set; } = 0.2;
}

public sealed record PresetInfo(string Id, string NameFa, string NameEn, string Icon, int Kelvin, double Darkness);

/// <summary>The ready-made night-light presets. The user can edit any of them (kept in <see cref="ComfortSettings.Presets"/>)
/// and put one back to the values below.</summary>
public static class ComfortPresets
{
    public const int MinKelvin = 1900, MaxKelvin = 6500;

    public static readonly IReadOnlyList<PresetInfo> Defaults =
    [
        new("work", "کار", "Work", "💼", 6500, 0.10),
        new("reading", "مطالعه", "Reading", "📖", 5000, 0.20),
        new("movie", "فیلم", "Movie", "🎬", 3400, 0.35),
        new("night", "شب", "Night", "🌙", 2700, 0.55),
        new("warm", "گرم", "Warm", "🔥", 2200, 0.40),
        new("cool", "خنک", "Cool", "❄", 5600, 0.15),
    ];

    public static PresetInfo Default(string id) => Defaults.First(p => p.Id == id);

    /// <summary>The values in use: the user's edited copy when there is one, else the built-in.</summary>
    public static (int Kelvin, double Darkness) Current(ComfortSettings s, string id)
    {
        var d = Default(id);
        var o = s.Presets.FirstOrDefault(p => p.Id == id);
        return o is null ? (d.Kelvin, d.Darkness) : (Math.Clamp(o.Kelvin, MinKelvin, MaxKelvin), Math.Clamp(o.Darkness, 0.05, 0.92));
    }

    public static bool IsModified(ComfortSettings s, string id)
    {
        var d = Default(id);
        var (k, dk) = Current(s, id);
        return k != d.Kelvin || Math.Abs(dk - d.Darkness) > 0.0005;
    }

    public static void Edit(ComfortSettings s, string id, int kelvin, double darkness)
    {
        var d = Default(id);
        kelvin = Math.Clamp(kelvin, MinKelvin, MaxKelvin);
        darkness = Math.Clamp(darkness, 0.05, 0.92);
        s.Presets.RemoveAll(p => p.Id == id);
        if (kelvin != d.Kelvin || Math.Abs(darkness - d.Darkness) > 0.0005) s.Presets.Add(new PresetValue { Id = id, Kelvin = kelvin, Darkness = darkness });
    }

    public static void Reset(ComfortSettings s, string id) => s.Presets.RemoveAll(p => p.Id == id);

    /// <summary>Puts the preset on the screen: darkness, tint, and dimming switched on.</summary>
    public static void Apply(ComfortSettings s, string id)
    {
        var (k, dk) = Current(s, id);
        s.DimLevel = dk;
        s.DimKelvin = k;
        s.DimWarm = k < MaxKelvin;
        s.DimOn = true;
    }

    /// <summary>The colour of the dimming layer for a colour temperature. 6500 K is plain black; lower gets amber, which
    /// keeps more red than blue light through the layer. 3600 K gives the app's original warm cover (38, 14, 0).</summary>
    public static Color Tint(int kelvin)
    {
        var t = Math.Clamp((MaxKelvin - kelvin) / (double)(MaxKelvin - MinKelvin), 0, 1);
        return Color.FromRgb((byte)Math.Round(60 * t), (byte)Math.Round(22 * t), 0);
    }
}
