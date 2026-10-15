using System.Globalization;

namespace Shabpa.Core;

public enum OverlayMode { Off, InGame, Always }

/// <summary>Everything about how the in-game overlay looks and where it sits; all of it is user-editable.</summary>
public sealed class OverlaySettings
{
    public OverlayMode Mode { get; set; } = OverlayMode.InGame;
    /// <summary>card, bar, compact, minimal, rings, graph.</summary>
    public string Layout { get; set; } = "card";
    /// <summary>theme (follow the app), matrix, cyber, sunset, ice, mono, rose, custom.</summary>
    public string Look { get; set; } = "theme";
    public string CustomAccent { get; set; } = "3EE6A0";
    public string CustomText { get; set; } = "F2F2F2";
    public string CustomBack { get; set; } = "0B0F1A";
    public double BackOpacity { get; set; } = 0.78;
    public double Scale { get; set; } = 1.0;
    /// <summary>tl, tr, bl, br, tc, bc, or "free" (X and Y are screen coordinates).</summary>
    public string Anchor { get; set; } = "tl";
    public double X { get; set; } = 24;
    public double Y { get; set; } = 24;
    public bool ColorByValue { get; set; } = true;
    public bool ShowLabels { get; set; } = true;
    /// <summary>Optional module: CPU temperature and CPU fan from the motherboard chip (needs admin and the PawnIO driver).</summary>
    public bool BoardSensors { get; set; } = true;
    public bool Shadow { get; set; } = true;
    public bool Rounded { get; set; } = true;
    /// <summary>Shortcut that shows or hides the overlay, like "Ctrl+Alt+O" (must contain Ctrl or Alt; empty = off).
    /// A single letter from older versions means Alt plus that letter.</summary>
    public string ToggleKey { get; set; } = "Ctrl+Alt+O";
    /// <summary>Shortcut that lets the overlay be dragged to a new place.</summary>
    public string MoveKey { get; set; } = "Ctrl+Alt+P";
    /// <summary>Per-game choices; when one of these games runs, its layout and items replace the general ones.</summary>
    public List<OverlayProfile> Profiles { get; set; } = [];
    public List<string> Items { get; set; } = ["fps", "low1", "gpu", "gputemp", "cpu", "ram", "ping"];
    /// <summary>Where each group of readings (GPU, CPU, RAM, network, time...) sits. A group that is not listed stays with
    /// the main block, except the clock group which defaults to a small block in the top-right corner.</summary>
    public List<GroupPlace> Places { get; set; } = [];

    public GroupPlace PlaceOf(string group) =>
        Places.FirstOrDefault(p => p.Group == group)
        ?? (group == "time" ? new GroupPlace { Group = "time", Anchor = "tr" } : new GroupPlace { Group = group, Anchor = "main" });

    /// <summary>Settings for a block that sits apart from the main one: a single compact strip, so a corner stays small.</summary>
    public OverlaySettings ForBlock()
    {
        var copy = (OverlaySettings)MemberwiseClone();
        copy.Layout = "bar";
        return copy;
    }

    public OverlayProfile? ProfileFor(string game) =>
        game.Length == 0 ? null : Profiles.FirstOrDefault(p => string.Equals(p.Game, game, StringComparison.OrdinalIgnoreCase));

    /// <summary>The settings to draw with while <paramref name="game"/> runs: a copy with that game's profile laid over them,
    /// or these settings themselves when the game has none.</summary>
    public OverlaySettings WithProfile(string game)
    {
        if (ProfileFor(game) is not { } p) return this;
        var copy = (OverlaySettings)MemberwiseClone();
        copy.Layout = p.Layout;
        copy.Look = p.Look;
        copy.Scale = p.Scale;
        copy.Anchor = p.Anchor;
        copy.X = p.X;
        copy.Y = p.Y;
        copy.ShowLabels = p.ShowLabels;
        if (p.Items.Count > 0) copy.Items = [.. p.Items];
        return copy;
    }

    public OverlayProfile Snapshot(string game) => new()
    {
        Game = game, Layout = Layout, Look = Look, Scale = Scale, Anchor = Anchor, X = X, Y = Y, ShowLabels = ShowLabels, Items = [.. Items],
    };
}

/// <param name="Advanced">Needs a sensor Windows does not expose by itself (fan speeds, memory temperature).</param>
public sealed record MetricDef(string Key, string Fa, string En, string Group, string Unit, bool AlwaysShown = false, bool Advanced = false)
{
    public string Label => L.T(Fa, En);
}

/// <summary>One line of the overlay, ready to draw.</summary>
public sealed record MetricView(string Key, string Label, string Value, string Unit, double? Fraction, int Level)
{
    public bool HasValue => Value != "—";
}

public static class OverlayMetrics
{
    public static readonly IReadOnlyList<MetricDef> All =
    [
        // general
        new("fps", "نمایش FPS", "FPS", "general", "", true),
        new("frame", "زمان فریم", "Frame time", "general", "ms"),
        new("low1", "Low ۱٪", "1% low", "general", "", true),
        new("fpsgraph", "نمودار FPS", "FPS graph", "general", ""),
        new("clock", "ساعت", "Clock", "time", ""),
        new("gametime", "مدت بازی", "Game time", "time", ""),
        // graphics card
        new("gputemp", "دمای GPU", "GPU temp", "gpu", "°C"),
        new("hotspot", "هات‌اسپات GPU", "GPU hotspot", "gpu", "°C"),
        new("gpupower", "توان GPU", "GPU power", "gpu", "W"),
        new("gpuvolt", "ولتاژ هسته", "Core voltage", "gpu", "mV"),
        new("gpu", "بار GPU", "GPU load", "gpu", "%", true),
        new("gpufan", "سرعت فن", "Fan speed", "gpu", "%"),
        new("gpufanrpm", "دور فن (RPM)", "Fan (RPM)", "gpu", "rpm"),
        new("gpuclock", "کلاک هسته", "Core clock", "gpu", "MHz"),
        new("memclock", "فرکانس VRAM", "VRAM clock", "gpu", "MHz"),
        new("memtemp", "دمای VRAM", "VRAM temp", "gpu", "°C"),
        new("vram", "مصرف VRAM", "VRAM use", "gpu", "GB"),
        // processor
        new("cputemp", "دمای CPU", "CPU temp", "cpu", "°C"),
        new("cpu", "بار CPU", "CPU load", "cpu", "%", true),
        new("cpuwatts", "توان CPU", "CPU power", "cpu", "W"),
        new("cpufreq", "فرکانس CPU", "CPU clock", "cpu", "MHz"),
        new("cpufan", "سرعت فن CPU", "CPU fan speed", "cpu", "%"),
        new("cpufanrpm", "دور فن CPU (RPM)", "CPU fan (RPM)", "cpu", "rpm"),
        // memory
        new("ram", "مصرف RAM", "RAM use", "ram", "%", true),
        new("ramused", "RAM مصرفی (گیگ)", "RAM used (GB)", "ram", "GB"),
        // network
        new("systemp", "دمای سیستم", "System temp", "board", "°C"),
        new("sockettemp", "دمای سوکت CPU", "CPU socket temp", "board", "°C"),
        new("vrmtemp", "دمای VRM", "VRM temp", "board", "°C"),
        new("pchtemp", "دمای چیپست", "Chipset temp", "board", "°C"),
        new("pumprpm", "دور پمپ (RPM)", "Pump (RPM)", "board", "rpm"),
        new("sysfanrpm", "دور فن کیس (RPM)", "Case fan (RPM)", "board", "rpm"),
        new("vcore", "ولتاژ Vcore", "Vcore", "board", "V"),
        new("v12", "ولتاژ ۱۲ ولت", "12 V rail", "board", "V"),

        new("ping", "پینگ", "Ping", "net", "ms", true),
        new("down", "سرعت دانلود", "Download speed", "net", "Mb/s"),
        new("up", "سرعت آپلود", "Upload speed", "net", "Mb/s"),
    ];

    public static MetricDef? Find(string key) => All.FirstOrDefault(m => m.Key == key);

    /// <summary>0 = fine, 1 = getting high, 2 = a problem. The limits are the usual rules of thumb for each reading.</summary>
    public static int Level(string key, double value) => key switch
    {
        "cpu" or "gpu" or "ram" or "vram" or "gpufan" or "cpufan" => key is "gpufan" or "cpufan" ? 0 : value >= 92 ? 2 : value >= 75 ? 1 : 0,
        "gputemp" => value >= 85 ? 2 : value >= 75 ? 1 : 0,
        "hotspot" => value >= 105 ? 2 : value >= 92 ? 1 : 0,
        "memtemp" => value >= 95 ? 2 : value >= 85 ? 1 : 0,
        "cputemp" => value >= 90 ? 2 : value >= 75 ? 1 : 0,
        "fps" or "low1" => value <= 0 ? 0 : value < (key == "fps" ? 30 : 24) ? 2 : value < (key == "fps" ? 60 : 45) ? 1 : 0,
        "frame" => value > 33 ? 2 : value > 20 ? 1 : 0,
        "systemp" or "sockettemp" or "pchtemp" => value >= 80 ? 2 : value >= 65 ? 1 : 0,
        "vrmtemp" => value >= 100 ? 2 : value >= 85 ? 1 : 0,
        "ping" => value > 100 ? 2 : value > 50 ? 1 : 0,
        _ => 0,
    };

    private static string N(double value, int decimals = 0) => value.ToString("F" + decimals, CultureInfo.InvariantCulture);

    /// <summary>Turns a sample into the lines the user chose. Every ticked item stays on the list for as long as it is
    /// ticked; a reading that is missing or zero for a moment shows as "—" instead of making the line jump away.</summary>
    public static List<MetricView> Build(TelemetrySample s, IEnumerable<string> keys)
    {
        var result = new List<MetricView>();
        foreach (var key in keys)
        {
            var def = Find(key);
            if (def is null) continue;
            var view = One(def, s);
            if (view is null) continue;
            result.Add(view);
        }
        return result;
    }

    /// <summary>True for readings that only exist when Shabpa runs as administrator (frame rate trace, motherboard chip).</summary>
    public static bool NeedsAdmin(string key) =>
        key is "fps" or "low1" or "frame" or "fpsgraph" or "cputemp" or "cpufan" or "cpufanrpm"
        || Find(key)?.Group == "board";

    /// <summary>The short English caption drawn on the overlay itself, like other overlay tools do.</summary>
    public static string Abbrev(string key) => key switch
    {
        "fps" => "FPS", "low1" => "1% LOW", "frame" => "FRAME", "fpsgraph" => "FPS", "clock" => "TIME", "gametime" => "PLAYED",
        "gpu" => "GPU LOAD", "gputemp" => "GPU TEMP", "hotspot" => "GPU HOT", "memtemp" => "VRAM TEMP", "gpupower" => "GPU PWR",
        "gpuvolt" => "GPU VOLT", "gpufan" => "GPU FAN", "gpufanrpm" => "GPU RPM", "gpuclock" => "GPU CLK", "memclock" => "VRAM CLK",
        "vram" => "VRAM", "cpu" => "CPU LOAD", "cputemp" => "CPU TEMP", "cpuwatts" => "CPU PWR", "cpufreq" => "CPU CLK",
        "cpufan" => "CPU FAN", "cpufanrpm" => "CPU RPM", "systemp" => "SYS TEMP", "sockettemp" => "SOCKET", "vrmtemp" => "VRM", "pchtemp" => "PCH",
        "pumprpm" => "PUMP", "sysfanrpm" => "CASE FAN", "vcore" => "VCORE", "v12" => "12V", "ram" => "RAM", "ramused" => "RAM GB", "ping" => "PING", "down" => "DL", "up" => "UL",
        _ => key.ToUpperInvariant(),
    };

    private static MetricView? One(MetricDef def, TelemetrySample s)
    {
        MetricView Make(double? value, int decimals = 0, double? max = null, string? text = null)
        {
            if (value is null) return new MetricView(def.Key, Abbrev(def.Key), "—", def.Unit, null, 0);
            var shown = text ?? N(value.Value, decimals);
            return new MetricView(def.Key, Abbrev(def.Key), shown, def.Unit, max is { } m && m > 0 ? Math.Clamp(value.Value / m, 0, 1) : null, Level(def.Key, value.Value));
        }

        var g = s.Gpu;
        return def.Key switch
        {
            "fps" => Make(s.Fps > 0 ? s.Fps : null, 0, 240),
            "low1" => Make(s.Low1Fps > 0 ? s.Low1Fps : null, 0, 240),
            "frame" => Make(s.FrameMs > 0 ? s.FrameMs : null, 1, 50),
            "gpu" => Make(g.LoadPercent, 0, 100),
            "gputemp" => Make(g.TempC, 0, 110),
            "hotspot" => Make(g.HotspotC, 0, 110),
            "memtemp" => Make(g.MemTempC, 0, 110),
            "gpuclock" => Make(g.CoreMHz, 0, 3500),
            "memclock" => Make(g.MemMHz, 0, 3000),
            "gpupower" => Make(g.PowerW, 0, 400),
            "gpuvolt" => Make(g.VoltageMv, 0, 1300),
            "gpufanrpm" => Make(g.FanRpm, 0, 4000),
            "gpufan" => Make(g.FanPercent, 0, 100),
            "vram" => g.VramUsedMb is { } used
                ? new MetricView("vram", Abbrev(def.Key), N(used / 1024, 1), g.VramTotalMb is { } t ? $"/ {N(t / 1024, 0)} GB" : "GB",
                    s.VramPercent / 100, Level("vram", s.VramPercent ?? 0))
                : new MetricView("vram", Abbrev(def.Key), "—", def.Unit, null, 0),
            "cpu" => Make(s.CpuLoad, 0, 100),
            "cputemp" => Make(s.CpuTempC, 0, 110),
            "cpuwatts" => Make(s.CpuWatts, 0, 250),
            "cpufreq" => Make(s.CpuMHz, 0, 6000),
            "cpufan" => Make(s.CpuFanPercent, 0, 100),
            "cpufanrpm" => Make(s.CpuFanRpm, 0, 4000),
                "ramused" => Make(s.RamUsedGb > 0 ? s.RamUsedGb : null, 1, s.RamTotalGb),
            "fpsgraph" => new MetricView("fpsgraph", Abbrev(def.Key), "graph", "", null, 0),
            "ram" => Make(s.RamPercent, 0, 100),
            "systemp" or "sockettemp" or "vrmtemp" or "pchtemp" or "pumprpm" or "sysfanrpm" or "vcore" or "v12"
                => Make(s.Board is { } b && b.TryGetValue(def.Key, out var bv) ? bv : null, def.Key is "vcore" or "v12" ? 2 : 0,
                    def.Key switch { "pumprpm" or "sysfanrpm" => 4000, "vcore" => 1.8, "v12" => 13, _ => 110 }),
            "ping" => Make(s.PingMs, 0, 200),
            "down" => Make(s.DownMbps, s.DownMbps >= 10 ? 0 : 1, 100),
            "up" => Make(s.UpMbps, s.UpMbps >= 10 ? 0 : 1, 100),
            "clock" => new MetricView("clock", Abbrev(def.Key), s.Time.ToString("HH:mm", CultureInfo.InvariantCulture), "", null, 0),
            "gametime" => s.InGame
                ? new MetricView("gametime", Abbrev(def.Key), $"{(int)s.GameTime.TotalHours}:{s.GameTime.Minutes:00}:{s.GameTime.Seconds:00}", "", null, 0)
                : new MetricView("gametime", Abbrev(def.Key), "—", "", null, 0),
            _ => null,
        };
    }

    /// <summary>Plausible figures for the preview and the screenshots.</summary>
    public static TelemetrySample Demo(DateTime now)
    {
        var wobble = Math.Sin(now.Second / 60.0 * Math.PI * 2);
        return new TelemetrySample
        {
            Time = now,
            CpuLoad = 38 + 6 * wobble,
            CpuTempC = 58,
            CpuWatts = 74,
            CpuMHz = 4900,
            CpuFanPercent = 52,
            CpuFanRpm = 1180,
            Board = new Dictionary<string, double> { ["systemp"] = 42, ["sockettemp"] = 39, ["vrmtemp"] = 38, ["pchtemp"] = 58, ["pumprpm"] = 2560, ["sysfanrpm"] = 1150, ["vcore"] = 1.31, ["v12"] = 12.02 },
            RamTempC = 43,
            RamPercent = 56,
            RamUsedGb = 17.9,
            RamTotalGb = 32,
            Gpu = new GpuReading
            {
                Name = "AMD Radeon RX 6900 XT", Vendor = "AMD", TempC = 67 + 3 * wobble, HotspotC = 84, MemTempC = 72, CoreMHz = 2310, MemMHz = 2000,
                FanPercent = 46, FanRpm = 1650, VoltageMv = 1010, PowerW = 212, LoadPercent = 96, VramUsedMb = 9400, VramTotalMb = 16368,
            },
            Fps = 164 + 8 * wobble,
            Low1Fps = 121,
            FrameMs = 6.1,
            FpsProcess = "cs2",
            PingMs = 24 + 3 * wobble,
            DownMbps = 1.4,
            UpMbps = 0.3,
            InGame = true,
            Game = "cs2",
            GameTime = TimeSpan.FromMinutes(47) + TimeSpan.FromSeconds(now.Second),
        };
    }
}

/// <summary>Colour presets for the overlay. Colours are "RRGGBB".</summary>
public sealed record OverlayLook(string Key, string Fa, string En, string Accent, string Text, string Back, string Accent2)
{
    public string Name => L.T(Fa, En);

    public static readonly IReadOnlyList<OverlayLook> All =
    [
        new("theme", "هم‌رنگ برنامه", "Match the app", "", "", "", ""),
        new("matrix", "ماتریکس", "Matrix", "39FF88", "D6FFE5", "021208", "00B85C"),
        new("cyber", "سایبر", "Cyber", "00E5FF", "FFFFFF", "0A0614", "FF2BD6"),
        new("sunset", "غروب", "Sunset", "FF9A5C", "FFF1E8", "1E0B12", "FF4D8D"),
        new("ice", "یخی", "Ice", "7AD7FF", "F0FAFF", "06121C", "A5B4FF"),
        new("rose", "رز", "Rose", "FF8AD0", "FFF0FA", "1B0A22", "B28CFF"),
        new("mono", "تک‌رنگ", "Mono", "FFFFFF", "E8E8E8", "000000", "BDBDBD"),
        new("custom", "دلخواه", "Custom", "", "", "", ""),
    ];

    public static OverlayLook Find(string key) => All.FirstOrDefault(l => l.Key == key) ?? All[0];

    public static bool IsHex(string? value) =>
        value is { Length: 6 } && value.All(Uri.IsHexDigit);
}

public static class OverlayLayouts
{
    public static readonly IReadOnlyList<(string key, string fa, string en)> All =
    [
        ("card", "کارت", "Card"),
        ("bar", "نوار افقی", "Bar"),
        ("compact", "فشرده", "Compact"),
        ("minimal", "مینیمال", "Minimal"),
        ("rings", "حلقه‌ای", "Rings"),
        ("graph", "نمودار", "Graph"),
    ];
}
