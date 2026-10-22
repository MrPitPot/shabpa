using System.Globalization;
using System.Text.RegularExpressions;

namespace Shabpa.Core;

public enum PowerConfidence { Low, Medium, High }

/// <summary>Where a figure came from: a real sensor / driver value, the built-in model table, or a rule of thumb.</summary>
public enum PowerSource { Sensor, Table, Heuristic }

/// <summary>What was found out about this PC (all of it optional; the estimator copes with gaps).</summary>
public sealed record HardwareSnapshot
{
    public string CpuName { get; init; } = "";
    public int CpuCores { get; init; }
    /// <summary>Measured CPU package peak (RAPL / energy meter), if one was seen.</summary>
    public double? CpuPeakWatts { get; init; }
    public string GpuName { get; init; } = "";
    public string GpuVendor { get; init; } = "";
    /// <summary>The card's own power limit from the driver (NVML), in watts.</summary>
    public double? GpuLimitWatts { get; init; }
    public double? GpuVramMb { get; init; }
    public int RamSticks { get; init; }
    public double RamGb { get; init; }
    /// <summary>"DDR3", "DDR4", "DDR5" or empty.</summary>
    public string RamType { get; init; } = "";
    public int Ssd { get; init; }
    public int Hdd { get; init; }
    /// <summary>Fans seen by a sensor; 0 = unknown.</summary>
    public int Fans { get; init; }
    public int Monitors { get; init; } = 1;
    public bool Laptop { get; init; }
}

/// <summary>One line of the breakdown. <see cref="Key"/> is language-neutral (gpu, cpu, board, ram, ssd, hdd, fans, display, psu).</summary>
public sealed record PowerPart(string Key, double Watts, PowerSource Source, string Detail = "");

public sealed record PowerEstimateResult(double GpuMax, double GpuIdle, double CpuMax, double Other,
    PowerConfidence Confidence, IReadOnlyList<PowerPart> Parts, string HardwareKey);

/// <summary>Turns a hardware snapshot into the four power figures. Pure: no WMI, no sensors, no clock.</summary>
public static class PowerEstimator
{
    // NVIDIA by model number, with the usual suffix: board power in watts.
    private static readonly Dictionary<string, double> Nvidia = new(StringComparer.OrdinalIgnoreCase)
    {
        ["5090"] = 575, ["5080"] = 360, ["5070 TI"] = 300, ["5070"] = 250, ["5060 TI"] = 180, ["5060"] = 145, ["5050"] = 130,
        ["4090"] = 450, ["4080 SUPER"] = 320, ["4080"] = 320, ["4070 TI SUPER"] = 285, ["4070 TI"] = 285, ["4070 SUPER"] = 220, ["4070"] = 200,
        ["4060 TI"] = 160, ["4060"] = 115, ["3090 TI"] = 450, ["3090"] = 350, ["3080 TI"] = 350, ["3080"] = 320, ["3070 TI"] = 290, ["3070"] = 220,
        ["3060 TI"] = 200, ["3060"] = 170, ["3050"] = 130, ["2080 TI"] = 250, ["2080 SUPER"] = 250, ["2080"] = 215, ["2070 SUPER"] = 215, ["2070"] = 175,
        ["2060 SUPER"] = 175, ["2060"] = 160, ["1660 TI"] = 120, ["1660 SUPER"] = 125, ["1660"] = 120, ["1650 SUPER"] = 100, ["1650"] = 75,
        ["1080 TI"] = 250, ["1080"] = 180, ["1070 TI"] = 180, ["1070"] = 150, ["1060"] = 120, ["1050 TI"] = 75, ["1050"] = 75, ["1030"] = 30,
    };

    private static readonly Dictionary<string, double> Amd = new(StringComparer.OrdinalIgnoreCase)
    {
        ["9070 XT"] = 304, ["9070"] = 220, ["9060 XT"] = 160, ["7900 XTX"] = 355, ["7900 XT"] = 315, ["7900 GRE"] = 260, ["7800 XT"] = 263, ["7700 XT"] = 245,
        ["7700"] = 190, ["7600 XT"] = 190, ["7600"] = 165, ["6950 XT"] = 335, ["6900 XT"] = 300, ["6800 XT"] = 300, ["6800"] = 250, ["6750 XT"] = 250,
        ["6700 XT"] = 230, ["6700"] = 175, ["6650 XT"] = 180, ["6600 XT"] = 160, ["6600"] = 132, ["6500 XT"] = 107, ["6400"] = 53, ["5700 XT"] = 225,
        ["5700"] = 180, ["5600 XT"] = 150, ["5500 XT"] = 130, ["590"] = 225, ["580"] = 185, ["570"] = 150, ["560"] = 80, ["550"] = 50,
        ["480"] = 150, ["470"] = 120, ["460"] = 75,
    };

    private static readonly Regex NvidiaModel = new(@"\b(?:RTX|GTX)\s*(\d{4})\s*(Ti\s*SUPER|SUPER|Ti)?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AmdModel = new(@"\bRX\s*(\d{3,4})\s*(XTX|XT|GRE)?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ArcModel = new(@"\bArc(?:\(TM\))?\s*(?:Pro\s*)?([AB])\s*(\d{3})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex IntegratedName = new(
        @"(Radeon\(TM\)\s*Graphics|Radeon\s+Graphics|Radeon\s+\d{3}M|UHD Graphics|Iris|Intel\(R\)\s*HD Graphics|Intel.*Arc.*Graphics|Vega\s*\d+\s*Graphics|Microsoft Basic|Basic Render)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex IntelCore = new(@"\bi([3579])-(\d{4,5})([A-Z]{0,3})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Ultra = new(@"Core\s*\(?TM\)?\s*Ultra\s*([579])\s*(\d{3})([A-Z]{0,2})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Ryzen = new(@"Ryzen\s*(?:AI\s*)?([3579])\s*(?:PRO\s*)?(\d{4,5})([A-Z0-9]{0,4})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsLaptopGpu(string name) =>
        name.Contains("Laptop", StringComparison.OrdinalIgnoreCase) || name.Contains("Mobile", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Max-Q", StringComparison.OrdinalIgnoreCase);

    public static bool IsIntegratedGpu(string name) =>
        name.Length > 0 && IntegratedName.IsMatch(name) && !AmdModel.IsMatch(name) && !NvidiaModel.IsMatch(name) && !ArcModel.IsMatch(name);

    /// <summary>Board power of a known discrete card from its name, or null.</summary>
    public static double? GpuTable(string name)
    {
        if (name.Length == 0) return null;
        if (NvidiaModel.Match(name) is { Success: true } n)
        {
            var suffix = Regex.Replace(n.Groups[2].Value, @"\s+", " ").Trim().ToUpperInvariant();
            if (Nvidia.TryGetValue((n.Groups[1].Value + " " + suffix).Trim(), out var w) || Nvidia.TryGetValue(n.Groups[1].Value, out w)) return w;
        }
        if (AmdModel.Match(name) is { Success: true } a)
        {
            var key = (a.Groups[1].Value + " " + a.Groups[2].Value.ToUpperInvariant()).Trim();
            if (Amd.TryGetValue(key, out var w) || Amd.TryGetValue(a.Groups[1].Value, out w)) return w;
        }
        if (ArcModel.Match(name) is { Success: true } arc)
        {
            return ArcWatts(arc.Groups[1].Value.ToUpperInvariant(), arc.Groups[2].Value);
        }
        return null;
    }

    private static double ArcWatts(string gen, string number) => (gen, number) switch
    {
        ("A", "770") or ("A", "750") => 225,
        ("A", "580") => 185,
        ("A", "380") => 75,
        ("B", "580") => 190,
        ("B", "570") => 150,
        _ => 150,
    };

    /// <summary>CPU peak power from the model name (PL2-ish, what a loaded game or render can pull), or null.</summary>
    public static double? CpuTable(string name, bool laptop)
    {
        if (name.Length == 0) return null;
        if (Ultra.Match(name) is { Success: true } u)
        {
            var suffix = u.Groups[3].Value.ToUpperInvariant();
            if (suffix.StartsWith('K')) return 200;
            if (suffix.StartsWith("HX")) return 100;
            if (suffix.StartsWith('H')) return 55;
            if (suffix.StartsWith('U') || suffix.StartsWith('V')) return 28;
            return laptop ? 45 : 100;
        }
        if (IntelCore.Match(name) is { Success: true } i)
        {
            var tier = int.Parse(i.Groups[1].Value, CultureInfo.InvariantCulture);
            var suffix = i.Groups[3].Value.ToUpperInvariant();
            if (suffix.Contains("HX")) return 110;
            if (suffix.StartsWith('H')) return 55;
            if (suffix.StartsWith('U') || suffix.StartsWith('P') || suffix.StartsWith('G') && laptop) return 28;
            if (suffix.StartsWith('K')) return tier switch { 9 => 250, 7 => 190, 5 => 150, _ => 110 };
            if (laptop) return 45;
            return tier switch { 9 => 150, 7 => 125, 5 => 100, _ => 75 };
        }
        if (Ryzen.Match(name) is { Success: true } r)
        {
            var tier = int.Parse(r.Groups[1].Value, CultureInfo.InvariantCulture);
            var suffix = r.Groups[3].Value.ToUpperInvariant();
            if (suffix.Contains("HX")) return 100;
            if (suffix.Contains("X3D")) return tier == 9 ? 160 : 120;
            if (suffix.StartsWith('H') || laptop && suffix.StartsWith('G')) return 45;
            if (suffix.StartsWith('U')) return 25;
            if (laptop) return 45;
            if (suffix.StartsWith('X')) return tier switch { 9 => 170, 7 => 105, _ => 105 };
            return tier switch { 9 => 105, 7 => 88, _ => 65 };
        }
        return null;
    }

    /// <summary>A short, stable text for "has the hardware changed since last time?".</summary>
    public static string KeyOf(HardwareSnapshot h) =>
        string.Join('|', h.CpuName.Trim(), h.GpuName.Trim(), h.RamSticks, Math.Round(h.RamGb), h.Ssd, h.Hdd, h.Laptop ? "L" : "D");

    public static PowerEstimateResult Estimate(HardwareSnapshot h)
    {
        var parts = new List<PowerPart>();

        // ---- graphics card --------------------------------------------------------------------------
        double gpuMax;
        var gpuKnown = true;
        var gpuSensor = false;
        var integrated = IsIntegratedGpu(h.GpuName);
        if (h.GpuLimitWatts is > 5 and < 1200)
        {
            gpuMax = h.GpuLimitWatts.Value;
            gpuSensor = true;
            parts.Add(new PowerPart("gpu", gpuMax, PowerSource.Sensor, h.GpuName));
        }
        else if (integrated)
        {
            gpuMax = 20;
            parts.Add(new PowerPart("gpu", gpuMax, PowerSource.Heuristic, h.GpuName));
        }
        else if (GpuTable(h.GpuName) is { } table)
        {
            gpuMax = h.Laptop || IsLaptopGpu(h.GpuName) ? Math.Min(table * 0.6, 140) : table;
            parts.Add(new PowerPart("gpu", gpuMax, PowerSource.Table, h.GpuName));
        }
        else
        {
            gpuKnown = false;
            gpuMax = h.GpuVramMb switch
            {
                null or <= 0 => h.Laptop ? 60 : 120,
                <= 2048 => 50,
                <= 4096 => 75,
                <= 8192 => 150,
                <= 12288 => 220,
                _ => 300,
            };
            if (h.GpuName.Length == 0 && h.GpuVramMb is null) gpuMax = h.Laptop ? 40 : 100;
            parts.Add(new PowerPart("gpu", gpuMax, PowerSource.Heuristic, h.GpuName));
        }

        var monitors = Math.Clamp(h.Monitors, 1, 6);
        double gpuIdle;
        if (integrated) gpuIdle = 2;
        else
        {
            gpuIdle = 5 + 0.035 * gpuMax + 2 * (monitors - 1);
            if (h.GpuVendor.Contains("AMD", StringComparison.OrdinalIgnoreCase) || h.GpuName.Contains("Radeon", StringComparison.OrdinalIgnoreCase)) gpuIdle += 4;
            gpuIdle = Math.Clamp(gpuIdle, 5, 40);
        }
        gpuIdle = Math.Min(gpuIdle, gpuMax);

        // ---- processor ------------------------------------------------------------------------------
        double cpuMax;
        bool cpuKnown;
        if (h.CpuPeakWatts is >= 10 and < 600)
        {
            cpuMax = h.CpuPeakWatts.Value;
            cpuKnown = true;
            parts.Add(new PowerPart("cpu", cpuMax, PowerSource.Sensor, h.CpuName));
        }
        else if (CpuTable(h.CpuName, h.Laptop) is { } cpuTable)
        {
            cpuMax = cpuTable;
            cpuKnown = true;
            parts.Add(new PowerPart("cpu", cpuMax, PowerSource.Table, h.CpuName));
        }
        else
        {
            cpuKnown = false;
            cpuMax = h.CpuCores > 0 ? 25 + h.CpuCores * 7 : 90;
            parts.Add(new PowerPart("cpu", cpuMax, PowerSource.Heuristic, h.CpuName));
        }
        if (h.Laptop) cpuMax = Math.Min(cpuMax, 110);
        cpuMax = Math.Clamp(Math.Round(cpuMax), 15, 450);

        // ---- everything else ------------------------------------------------------------------------
        var perStick = h.Laptop ? 1.5 : h.RamType.Equals("DDR5", StringComparison.OrdinalIgnoreCase) ? 3.5 : 3;
        var sticks = h.RamSticks > 0 ? h.RamSticks : 2;
        var ssd = h.Ssd;
        var hdd = h.Hdd;
        if (ssd + hdd == 0) ssd = 1;
        var fans = h.Fans > 0 ? h.Fans : h.Laptop ? 1 : 3;
        var board = h.Laptop ? 8 : 25;
        var ssdW = ssd * (h.Laptop ? 2 : 3);
        var hddW = hdd * 7;
        var ramW = sticks * perStick;
        var fanW = fans * 2;
        var display = h.Laptop ? 7.0 : 0;
        var misc = h.Laptop ? 0 : 5; // USB devices, RGB, case lighting
        var subtotal = board + ramW + ssdW + hddW + fanW + display + misc;
        var loss = subtotal * (h.Laptop ? 0.05 : 0.10);
        var other = Math.Clamp(Math.Round(subtotal + loss), 10, 250);

        parts.Add(new PowerPart("board", board + misc, PowerSource.Heuristic));
        parts.Add(new PowerPart("ram", ramW, h.RamSticks > 0 ? PowerSource.Table : PowerSource.Heuristic, $"{sticks}x {h.RamType}".Trim()));
        if (ssd > 0) parts.Add(new PowerPart("ssd", ssdW, PowerSource.Table, ssd.ToString(CultureInfo.InvariantCulture)));
        if (hdd > 0) parts.Add(new PowerPart("hdd", hddW, PowerSource.Table, hdd.ToString(CultureInfo.InvariantCulture)));
        parts.Add(new PowerPart("fans", fanW, h.Fans > 0 ? PowerSource.Sensor : PowerSource.Heuristic, fans.ToString(CultureInfo.InvariantCulture)));
        if (display > 0) parts.Add(new PowerPart("display", display, PowerSource.Heuristic));
        parts.Add(new PowerPart("psu", Math.Round(loss, 1), PowerSource.Heuristic));

        var confidence = gpuSensor && cpuKnown && h.RamSticks > 0 ? PowerConfidence.High
            : gpuKnown && cpuKnown ? PowerConfidence.Medium
            : PowerConfidence.Low;

        return new PowerEstimateResult(Math.Round(gpuMax), Math.Round(gpuIdle), cpuMax, other, confidence, parts, KeyOf(h));
    }
}
