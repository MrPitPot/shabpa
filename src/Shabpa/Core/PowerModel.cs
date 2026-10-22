using System.Globalization;

namespace Shabpa.Core;

public enum PowerField { GpuMax, GpuIdle, Base, CpuMax }

/// <summary>What the power estimate knows about this PC: the detected figures, which fields the person typed by hand, and what the
/// live sensors have taught so far. Lives inside <see cref="Settings"/>.</summary>
public sealed class PowerSettings
{
    /// <summary>The one-time check of values saved by older versions has run.</summary>
    public bool Migrated { get; set; }
    public bool GpuMaxManual { get; set; }
    public bool GpuIdleManual { get; set; }
    public bool BaseManual { get; set; }
    public bool CpuMaxManual { get; set; }
    /// <summary>Detected figures from hardware (0 = not detected yet).</summary>
    public double DetGpuMax { get; set; }
    public double DetGpuIdle { get; set; }
    public double DetBase { get; set; }
    public double DetCpuMax { get; set; }
    public string Confidence { get; set; } = "";
    public string DetectedAt { get; set; } = "";
    public string Hardware { get; set; } = "";
    public List<PowerPart> Parts { get; set; } = [];
    public PowerLearnState Learn { get; set; } = new();
}

/// <summary>Bounded, persisted record of what the card drew while the app watched it.</summary>
public sealed class PowerLearnState
{
    public const int MaxWindows = 48;
    /// <summary>Per finished window: the typical top draw under load.</summary>
    public List<double> Peaks { get; set; } = [];
    /// <summary>Per finished window: the average draw while the card was idle.</summary>
    public List<double> Idles { get; set; } = [];
    public string WindowStart { get; set; } = "";
    /// <summary>The highest few loaded readings of the open window.</summary>
    public List<double> WindowTop { get; set; } = [];
    public double WindowIdleSum { get; set; }
    public int WindowIdleCount { get; set; }
}

public static class PowerLearner
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
    public const double LoadedAbove = 70;
    public const double IdleBelow = 3;

    /// <summary>Feeds one reading. Returns true when a window closed (the learned figures may have changed and are worth saving).
    /// Readings that are impossible or far above <paramref name="ceiling"/> are ignored, so a driver glitch cannot skew the model.</summary>
    public static bool Observe(PowerLearnState st, double watts, double load, double ceiling, DateTime now, TimeSpan? window = null)
    {
        var length = window ?? Window;
        var closed = false;
        if (DateTime.TryParse(st.WindowStart, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var start))
        {
            if (now < start) { Reset(st, now); }
            else if (now - start >= length) { Close(st); Reset(st, now); closed = true; }
        }
        else Reset(st, now);

        if (!double.IsFinite(watts) || !double.IsFinite(load) || watts <= 0 || watts > 1500) return closed;
        if (ceiling > 0 && watts > ceiling * 1.6) return closed;
        if (load >= LoadedAbove)
        {
            st.WindowTop.Add(watts);
            st.WindowTop.Sort((a, b) => b.CompareTo(a));
            if (st.WindowTop.Count > 3) st.WindowTop.RemoveRange(3, st.WindowTop.Count - 3);
        }
        else if (load <= IdleBelow)
        {
            st.WindowIdleSum += watts;
            st.WindowIdleCount++;
        }
        return closed;
    }

    private static void Reset(PowerLearnState st, DateTime now)
    {
        st.WindowStart = now.ToString("O", CultureInfo.InvariantCulture);
        st.WindowTop.Clear();
        st.WindowIdleSum = 0;
        st.WindowIdleCount = 0;
    }

    private static void Close(PowerLearnState st)
    {
        // the second-highest of the top three: one spike cannot become the card's "maximum"
        if (st.WindowTop.Count >= 3) Add(st.Peaks, st.WindowTop[1]);
        if (st.WindowIdleCount >= 10) Add(st.Idles, st.WindowIdleSum / st.WindowIdleCount);
    }

    private static void Add(List<double> list, double value)
    {
        list.Add(Math.Round(value, 1));
        if (list.Count > PowerLearnState.MaxWindows) list.RemoveRange(0, list.Count - PowerLearnState.MaxWindows);
    }

    /// <summary>The card's typical peak: with enough windows the single highest one is dropped as a possible outlier.</summary>
    public static double? LearnedPeak(PowerLearnState st)
    {
        var v = st.Peaks.Where(double.IsFinite).OrderBy(x => x).ToList();
        if (v.Count < 3) return null;
        if (v.Count >= 6) v.RemoveAt(v.Count - 1);
        return v[^1];
    }

    public static double? LearnedIdle(PowerLearnState st)
    {
        var v = st.Idles.Where(double.IsFinite).OrderBy(x => x).ToList();
        if (v.Count < 3) return null;
        return v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2;
    }

    /// <summary>A peak is only trusted once the card has really been worked (at least 60 % of the detected maximum); it may lift
    /// the figure at most 50 % above it.</summary>
    public static double RefineMax(double detected, double? learned)
    {
        if (detected <= 0 || learned is not { } l || l < detected * 0.6) return detected;
        return Math.Round(Math.Min(l, detected * 1.5));
    }

    public static double RefineIdle(double detected, double? learned)
    {
        if (detected <= 0 || learned is not { } l) return detected;
        return l >= 1 && l <= Math.Max(detected * 3, 40) ? Math.Round(l) : detected;
    }
}

/// <summary>The rules that tie the detected figures, the learned ones and the person's own edits to <see cref="Settings"/>.</summary>
public static class PowerModel
{
    public const double OldGpuMax = 250, OldGpuIdle = 12, OldBase = 45, OldCpuMax = 125;
    public const double MaxWatts = 1000;

    public static bool IsManual(Settings s, PowerField f) => f switch
    {
        PowerField.GpuMax => s.Power.GpuMaxManual,
        PowerField.GpuIdle => s.Power.GpuIdleManual,
        PowerField.Base => s.Power.BaseManual,
        _ => s.Power.CpuMaxManual,
    };

    private static void SetManual(Settings s, PowerField f, bool value)
    {
        switch (f)
        {
            case PowerField.GpuMax: s.Power.GpuMaxManual = value; break;
            case PowerField.GpuIdle: s.Power.GpuIdleManual = value; break;
            case PowerField.Base: s.Power.BaseManual = value; break;
            default: s.Power.CpuMaxManual = value; break;
        }
    }

    public static double Current(Settings s, PowerField f) => f switch
    {
        PowerField.GpuMax => s.GpuMaxWatts,
        PowerField.GpuIdle => s.GpuIdleWatts,
        PowerField.Base => s.BaseWatts,
        _ => s.CpuMaxWatts,
    };

    private static void Put(Settings s, PowerField f, double value)
    {
        switch (f)
        {
            case PowerField.GpuMax: s.GpuMaxWatts = value; break;
            case PowerField.GpuIdle: s.GpuIdleWatts = value; break;
            case PowerField.Base: s.BaseWatts = value; break;
            default: s.CpuMaxWatts = value; break;
        }
    }

    private static double Det(PowerSettings p, PowerField f) => f switch
    {
        PowerField.GpuMax => p.DetGpuMax,
        PowerField.GpuIdle => p.DetGpuIdle,
        PowerField.Base => p.DetBase,
        _ => p.DetCpuMax,
    };

    /// <summary>What the model says for a field now: the hardware estimate, refined by what the sensors saw. 0 = nothing detected.</summary>
    public static double Detected(Settings s, PowerField f)
    {
        var p = s.Power;
        return f switch
        {
            PowerField.GpuMax => PowerLearner.RefineMax(p.DetGpuMax, PowerLearner.LearnedPeak(p.Learn)),
            PowerField.GpuIdle => PowerLearner.RefineIdle(p.DetGpuIdle, PowerLearner.LearnedIdle(p.Learn)),
            _ => Det(p, f),
        };
    }

    /// <summary>The figure is not the pure hardware estimate: the live sensors moved it.</summary>
    public static bool IsLearned(Settings s, PowerField f) => Det(s.Power, f) > 0 && Math.Abs(Detected(s, f) - Det(s.Power, f)) >= 1;

    public static bool HasDetected(Settings s) => s.Power.DetGpuMax > 0;

    /// <summary>One-time, for values saved by older versions: a value equal to the old built-in default was never edited, so the
    /// detected figure may replace it; anything else was typed by the person and stays.</summary>
    public static void Migrate(Settings s)
    {
        var p = s.Power;
        if (p.Migrated) return;
        p.Migrated = true;
        p.GpuMaxManual = !Same(s.GpuMaxWatts, OldGpuMax);
        p.GpuIdleManual = !Same(s.GpuIdleWatts, OldGpuIdle);
        p.BaseManual = !Same(s.BaseWatts, OldBase);
        p.CpuMaxManual = !Same(s.CpuMaxWatts, OldCpuMax);
    }

    private static bool Same(double a, double b) => Math.Abs(a - b) < 0.5;

    /// <summary>Stores a fresh detection and moves every field the person has not edited to it.</summary>
    public static void ApplyDetected(Settings s, PowerEstimateResult r, DateTime now)
    {
        var p = s.Power;
        p.DetGpuMax = r.GpuMax;
        p.DetGpuIdle = r.GpuIdle;
        p.DetBase = r.Other;
        p.DetCpuMax = r.CpuMax;
        p.Confidence = r.Confidence.ToString();
        p.Hardware = r.HardwareKey;
        p.Parts = [.. r.Parts];
        p.DetectedAt = now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        Refresh(s);
    }

    /// <summary>Re-applies the model to the fields that are not manual.</summary>
    public static void Refresh(Settings s)
    {
        foreach (var f in Enum.GetValues<PowerField>())
        {
            if (IsManual(s, f)) continue;
            var d = Detected(s, f);
            if (d > 0) Put(s, f, d);
        }
        s.GpuIdleWatts = Math.Min(s.GpuIdleWatts, s.GpuMaxWatts);
    }

    /// <summary>The person typed a value. It becomes manual when it differs from what is stored.</summary>
    public static void Edit(Settings s, PowerField f, double value)
    {
        value = Clamp(f, value, s);
        if (Same(value, Current(s, f))) return;
        Put(s, f, value);
        SetManual(s, f, true);
        s.GpuIdleWatts = Math.Min(s.GpuIdleWatts, s.GpuMaxWatts);
    }

    /// <summary>Back to the detected figure; the field follows detection and learning again.</summary>
    public static void ResetToDetected(Settings s, PowerField f)
    {
        SetManual(s, f, false);
        var d = Detected(s, f);
        if (d > 0) Put(s, f, d);
        s.GpuIdleWatts = Math.Min(s.GpuIdleWatts, s.GpuMaxWatts);
    }

    private static double Clamp(PowerField f, double v, Settings s)
    {
        if (!double.IsFinite(v)) return Current(s, f);
        var max = f == PowerField.GpuIdle ? Math.Min(MaxWatts, s.GpuMaxWatts) : MaxWatts;
        return Math.Clamp(Math.Round(v), 0, max);
    }

    /// <summary>Brings every power figure of a loaded or imported settings object into range (a hand-edited or damaged file).</summary>
    public static void Sanitize(Settings s)
    {
        s.Power ??= new();
        var p = s.Power;
        p.Parts ??= [];
        p.Learn ??= new();
        p.Learn.Peaks = Cleaned(p.Learn.Peaks);
        p.Learn.Idles = Cleaned(p.Learn.Idles);
        p.Learn.WindowTop = [.. (p.Learn.WindowTop ?? []).Where(v => double.IsFinite(v) && v is > 0 and <= 1500).OrderByDescending(v => v).Take(3)];
        p.Learn.WindowStart ??= "";
        if (!double.IsFinite(p.Learn.WindowIdleSum) || p.Learn.WindowIdleSum < 0) p.Learn.WindowIdleSum = 0;
        if (p.Learn.WindowIdleCount < 0 || p.Learn.WindowIdleCount > 100000) { p.Learn.WindowIdleCount = 0; p.Learn.WindowIdleSum = 0; }
        p.Parts = [.. p.Parts.Where(x => x is not null && double.IsFinite(x.Watts)).Take(20)
            .Select(x => x with { Watts = Math.Clamp(x.Watts, 0, 2000), Key = x.Key ?? "", Detail = x.Detail ?? "" })];
        p.Confidence = Enum.TryParse<PowerConfidence>(p.Confidence, out _) ? p.Confidence : "";
        p.DetectedAt ??= "";
        p.Hardware ??= "";
        p.DetGpuMax = Fix(p.DetGpuMax, 0);
        p.DetGpuIdle = Math.Min(Fix(p.DetGpuIdle, 0), p.DetGpuMax > 0 ? p.DetGpuMax : MaxWatts);
        p.DetBase = Fix(p.DetBase, 0);
        p.DetCpuMax = Fix(p.DetCpuMax, 0);
        s.GpuMaxWatts = Fix(s.GpuMaxWatts, OldGpuMax);
        s.GpuIdleWatts = Math.Min(Fix(s.GpuIdleWatts, OldGpuIdle), s.GpuMaxWatts);
        s.BaseWatts = Fix(s.BaseWatts, OldBase);
        s.CpuMaxWatts = Fix(s.CpuMaxWatts, OldCpuMax);
    }

    private static double Fix(double v, double fallback) => double.IsFinite(v) ? Math.Clamp(v, 0, MaxWatts) : fallback;

    private static List<double> Cleaned(List<double>? list) =>
        [.. (list ?? []).Where(v => double.IsFinite(v) && v is > 0 and <= 1500).TakeLast(PowerLearnState.MaxWindows)];

    /// <summary>A live reading of the card. Returns true when something worth saving changed.</summary>
    public static bool Observe(Settings s, double gpuWatts, double gpuLoad, DateTime now, TimeSpan? window = null)
    {
        var ceiling = Math.Max(Math.Max(s.Power.DetGpuMax, s.GpuMaxWatts), 50);
        if (!PowerLearner.Observe(s.Power.Learn, gpuWatts, gpuLoad, ceiling, now, window)) return false;
        Refresh(s);
        return true;
    }

    /// <summary>The hardware changed (or was never detected) since the last detection.</summary>
    public static bool NeedsDetection(Settings s, string hardwareKey) => !HasDetected(s) || s.Power.Hardware != hardwareKey;
}
