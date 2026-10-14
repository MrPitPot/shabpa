namespace Shabpa.Core;

/// <summary>What the user asked the card to do. Saved with the settings.</summary>
public sealed class GpuTuneProfile
{
    /// <summary>Highest core clock in MHz (null = leave the driver's value).</summary>
    public int? CoreMax { get; set; }
    /// <summary>Highest core voltage in mV: lowering it is the undervolt.</summary>
    public int? Voltage { get; set; }
    /// <summary>Power limit offset in percent.</summary>
    public int? PowerPercent { get; set; }
    /// <summary>Highest VRAM clock in MHz.</summary>
    public int? VramMax { get; set; }
    public bool? FastTiming { get; set; }
    public bool? ZeroRpm { get; set; }

    /// <summary>driver (untouched), preset, custom, smart.</summary>
    public string FanMode { get; set; } = "driver";
    public string FanPreset { get; set; } = "balanced";
    public List<int> CurveTemp { get; set; } = [.. FanCurves.Balanced.temps];
    public List<int> CurveSpeed { get; set; } = [.. FanCurves.Balanced.speeds];
    /// <summary>Smart mode aims to keep the hot spot at this temperature.</summary>
    public int TargetTemp { get; set; } = 85;

    /// <summary>Apply the profile again whenever Shabpa starts.</summary>
    public bool AutoApply { get; set; }
    /// <summary>Set while a startup apply has not yet proved stable; if it is still set at the next start, the card
    /// crashed or the PC hung after it, and the profile is not applied automatically.</summary>
    public bool Pending { get; set; }

    public bool TouchesClocks => CoreMax is not null || Voltage is not null || PowerPercent is not null || VramMax is not null || FastTiming is not null;
    public bool IsDefault => !TouchesClocks && ZeroRpm is null && FanMode == "driver";
}

public static class FanCurves
{
    public static readonly (int[] temps, int[] speeds) Silent = ([35, 55, 68, 78, 88], [15, 25, 38, 55, 80]);
    public static readonly (int[] temps, int[] speeds) Balanced = ([30, 50, 61, 72, 80], [15, 32, 49, 66, 100]);
    public static readonly (int[] temps, int[] speeds) Cool = ([30, 45, 58, 68, 76], [25, 40, 58, 78, 100]);
    public static readonly (int[] temps, int[] speeds) Turbo = ([30, 42, 52, 62, 70], [35, 50, 70, 90, 100]);

    public static (int[] temps, int[] speeds) Preset(string key) => key switch
    {
        "silent" => Silent,
        "cool" => Cool,
        "turbo" => Turbo,
        _ => Balanced,
    };

    public static readonly IReadOnlyList<(string key, string fa, string en)> Presets =
    [
        ("silent", "بی‌صدا", "Silent"),
        ("balanced", "متعادل", "Balanced"),
        ("cool", "خنک", "Cool"),
        ("turbo", "توربو", "Turbo"),
    ];

    /// <summary>Keeps temperatures rising and speeds from falling, and everything inside the driver's range.</summary>
    public static (int[] temps, int[] speeds) Sanitize(IReadOnlyList<int> temps, IReadOnlyList<int> speeds, int tempMin, int tempMax, int speedMin, int speedMax)
    {
        var t = new int[AmdGpu.FanCurvePoints];
        var s = new int[AmdGpu.FanCurvePoints];
        for (var i = 0; i < t.Length; i++)
        {
            var temp = i < temps.Count ? temps[i] : tempMax;
            var speed = i < speeds.Count ? speeds[i] : speedMax;
            t[i] = Math.Clamp(temp, tempMin, tempMax);
            s[i] = Math.Clamp(speed, speedMin, speedMax);
            if (i > 0)
            {
                t[i] = Math.Max(t[i], t[i - 1] + 1);
                s[i] = Math.Max(s[i], s[i - 1]);
            }
        }
        // Pushing temperatures up can run past the top of the range; fold the tail back in.
        for (var i = t.Length - 1; i >= 0; i--)
        {
            var ceiling = tempMax - (t.Length - 1 - i);
            if (t[i] > ceiling) t[i] = ceiling;
        }
        return (t, s);
    }
}

/// <summary>The "smart" fan: nudges the whole curve up or down so the hot spot settles near the target
/// instead of following a fixed table. Pure logic so it can be tested without a card.</summary>
public static class FanSmart
{
    public const int MinOffset = -10;
    public const int MaxOffset = 45;

    /// <summary>One adjustment step (called every few seconds). Returns the new offset in percentage points.</summary>
    public static int Next(int offset, double temp, int target)
    {
        var error = temp - target;
        var step = error switch
        {
            > 10 => 8,
            > 3 => 3,
            > 0.5 => 1,
            < -10 => -3,
            < -5 => -2,
            < -2 => -1,
            _ => 0,
        };
        return Math.Clamp(offset + step, MinOffset, MaxOffset);
    }

    public static int[] Apply(IReadOnlyList<int> baseSpeeds, int offset, int speedMin, int speedMax)
    {
        var result = new int[baseSpeeds.Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = Math.Clamp(baseSpeeds[i] + offset, speedMin, speedMax);
            if (i > 0) result[i] = Math.Max(result[i], result[i - 1]);
        }
        return result;
    }
}

/// <summary>Applies a <see cref="GpuTuneProfile"/> to an AMD card and runs the smart fan.</summary>
public sealed class GpuTuner
{
    private static readonly int[] AllIds =
    [
        AmdGpu.GfxClkFmax, AmdGpu.GfxClkFmin, AmdGpu.UclkFmax, AmdGpu.UclkFmin, AmdGpu.PowerPercentage, AmdGpu.OdVoltage, 14, AmdGpu.FanZeroRpm,
        .. Enumerable.Range(0, AmdGpu.FanCurvePoints * 2).Select(i => AmdGpu.FanCurveTemp1 + i),
    ];

    private readonly Func<AmdGpu?> _gpu;
    private readonly object _gate = new();
    private int _offset;
    private DateTime _lastWrite = DateTime.MinValue;
    private int[]? _lastSpeeds;
    private DateTime _lastStep = DateTime.MinValue;

    public GpuTuner(Func<AmdGpu?> gpu) => _gpu = gpu;

    public string? LastError { get; private set; }
    /// <summary>The smart fan's current offset, for display.</summary>
    public int SmartOffset => _offset;

    /// <summary>The settings the card would be given for this profile (id → value), without writing anything.</summary>
    public static Dictionary<int, int> Plan(GpuTuneProfile p, Od8State state)
    {
        var values = new Dictionary<int, int>();
        void Add(int id, int? value)
        {
            if (value is { } v && state.Get(id) is not null) values[id] = v;
        }
        Add(AmdGpu.GfxClkFmax, p.CoreMax);
        Add(AmdGpu.OdVoltage, p.Voltage);
        Add(AmdGpu.PowerPercentage, p.PowerPercent);
        Add(AmdGpu.UclkFmax, p.VramMax);
        Add(14, p.FastTiming is { } f ? (f ? 1 : 0) : null);
        Add(AmdGpu.FanZeroRpm, p.ZeroRpm is { } z ? (z ? 1 : 0) : null);

        if (p.FanMode is "preset" or "custom" or "smart" && state.Get(AmdGpu.FanCurveTemp1) is { } t0 && state.Get(AmdGpu.FanCurveSpeed1) is { } s0)
        {
            var (temps, speeds) = p.FanMode switch
            {
                "preset" => FanCurves.Preset(p.FanPreset),
                _ => (p.CurveTemp.ToArray(), p.CurveSpeed.ToArray()),
            };
            var (t, s) = FanCurves.Sanitize(temps, speeds, t0.Min, t0.Max, s0.Min, s0.Max);
            for (var i = 0; i < AmdGpu.FanCurvePoints; i++)
            {
                values[AmdGpu.FanCurveTemp1 + i * 2] = t[i];
                values[AmdGpu.FanCurveSpeed1 + i * 2] = s[i];
            }
        }
        return values;
    }

    public string? Apply(GpuTuneProfile profile)
    {
        lock (_gate)
        {
            var gpu = _gpu();
            if (gpu is null || !gpu.CanTune) return "not-supported";
            var state = gpu.ReadOd8();
            if (state is null) return "not-readable";
            var plan = Plan(profile, state);
            _offset = 0;
            _lastSpeeds = null;
            if (plan.Count == 0)
            {
                LastError = null;
                return null;
            }
            LastError = gpu.SetOd8(plan);
            _lastWrite = DateTime.Now;
            return LastError;
        }
    }

    /// <summary>Puts every setting this tool can change back to what the driver ships with.</summary>
    public string? ResetAll()
    {
        lock (_gate)
        {
            var gpu = _gpu();
            if (gpu is null || !gpu.CanTune) return "not-supported";
            _offset = 0;
            _lastSpeeds = null;
            LastError = gpu.ResetOd8(AllIds);
            return LastError;
        }
    }

    /// <summary>Gives the fan curve back to the driver.</summary>
    public string? ResetFan()
    {
        lock (_gate)
        {
            var gpu = _gpu();
            if (gpu is null || !gpu.CanTune) return "not-supported";
            _offset = 0;
            _lastSpeeds = null;
            LastError = gpu.ResetOd8(Enumerable.Range(0, AmdGpu.FanCurvePoints * 2).Select(i => AmdGpu.FanCurveTemp1 + i));
            return LastError;
        }
    }

    /// <summary>The current values of the settings this tool can change (to restore after a failed trial).</summary>
    public Dictionary<int, int>? Snapshot()
    {
        var gpu = _gpu();
        var state = gpu?.ReadOd8();
        if (state is null) return null;
        return AllIds.Where(id => state.Features.ContainsKey(id)).ToDictionary(id => id, id => state.Features[id].Current);
    }

    public string? Restore(IReadOnlyDictionary<int, int> values)
    {
        lock (_gate)
        {
            var gpu = _gpu();
            return gpu is null ? "not-supported" : gpu.SetOd8(values);
        }
    }

    /// <summary>Smart fan: called with every telemetry sample. Cheap when the mode is not smart.</summary>
    public void Tick(GpuTuneProfile profile, TelemetrySample sample)
    {
        if (profile.FanMode != "smart") return;
        var now = DateTime.Now;
        if (now - _lastStep < TimeSpan.FromSeconds(3)) return;
        _lastStep = now;
        var temp = sample.Gpu.HotspotC ?? sample.Gpu.TempC;
        if (temp is null) return;

        lock (_gate)
        {
            var gpu = _gpu();
            if (gpu is null || !gpu.CanTune) return;
            var state = gpu.ReadOd8();
            if (state is null || state.Get(AmdGpu.FanCurveSpeed1) is not { } s0 || state.Get(AmdGpu.FanCurveTemp1) is not { } t0) return;

            _offset = FanSmart.Next(_offset, temp.Value, profile.TargetTemp);
            var baseCurve = FanCurves.Balanced;
            var (temps, baseSpeeds) = FanCurves.Sanitize(baseCurve.temps, baseCurve.speeds, t0.Min, t0.Max, s0.Min, s0.Max);
            var speeds = FanSmart.Apply(baseSpeeds, _offset, s0.Min, s0.Max);
            if (_lastSpeeds is not null && speeds.SequenceEqual(_lastSpeeds)) return;
            // Rewriting for a 1 % change is noise; wait for a real move or a few seconds.
            if (_lastSpeeds is not null && now - _lastWrite < TimeSpan.FromSeconds(6)
                && Enumerable.Range(0, speeds.Length).All(i => Math.Abs(speeds[i] - _lastSpeeds[i]) < 3)) return;

            var values = new Dictionary<int, int>();
            for (var i = 0; i < AmdGpu.FanCurvePoints; i++)
            {
                values[AmdGpu.FanCurveTemp1 + i * 2] = temps[i];
                values[AmdGpu.FanCurveSpeed1 + i * 2] = speeds[i];
            }
            LastError = gpu.SetOd8(values);
            if (LastError is null)
            {
                _lastSpeeds = speeds;
                _lastWrite = now;
            }
        }
    }
}
