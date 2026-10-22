using System.Text.RegularExpressions;

namespace Shabpa.Core;

/// <summary>Short, readable part names for the Home cards ("Ryzen 7 5800X", "RX 6900 XT").</summary>
public static class HardwareLabel
{
    private static readonly string[] Noise = ["AMD", "NVIDIA", "Intel", "Radeon", "GeForce", "Graphics", "Processor", "CPU", "with", "Corporation", "Series"];

    public static string Cpu(string raw)
    {
        var name = Regex.Replace(raw.Replace("(R)", "").Replace("(TM)", "").Replace("(tm)", ""), @"\s+", " ").Trim();
        var at = name.IndexOf(" @ ", StringComparison.Ordinal);
        if (at > 0) name = name[..at];
        // "12th Gen Intel Core i7-12700K" -> "Core i7-12700K"; "AMD Ryzen 7 5800X 8-Core Processor" -> "Ryzen 7 5800X"
        name = Regex.Replace(name, @"\b\d+(st|nd|rd|th) Gen\b", "", RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\b\d+-Core\b", "", RegexOptions.IgnoreCase);
        return Join(name);
    }

    public static string Gpu(string raw)
    {
        var name = Regex.Replace(raw.Replace("(R)", "").Replace("(TM)", ""), @"\s+", " ").Trim();
        return Join(name);
    }

    /// <summary>Card title with the part name: "Processor · Ryzen 7 5800X"; the title alone when there is no name.</summary>
    public static string Title(string title, string shortName) => shortName.Length == 0 ? title : title + " · " + shortName;

    private static string Join(string name) =>
        string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !Noise.Contains(w, StringComparer.OrdinalIgnoreCase)));
}
