namespace Shabpa.Core;

/// <summary>Everything Bidar can do on his own (or in answer to an app event). Pure names: the rig (Kit/BidarLife.cs) plays them.</summary>
public enum BidarAct
{
    Breathe, Blink, DoubleBlink, LookAround, LanternFlicker, Stretch, Yawn, WingWave, Hop, HatFidget, Hello, Think, Search, Shrug, Peek, Doze, Fly, Curious,
    // reactions (never picked at random)
    Startle, Celebrate, Worry, Alert, Dizzy, EasterEgg,
}

/// <summary>Things that happen in the app that Bidar reacts to.</summary>
public enum BidarCue { DownloadDone, BadPing, ShutdownSoon, Error }

/// <summary>Why nothing is happening right now (None = free to act).</summary>
public enum PauseReason { None, Disabled, NoMotion, LowMemory, GameRunning, Hidden, Inactive, UserBusy, PopupOpen, Busy, Asleep }

/// <summary>What the host knows at the moment Bidar is about to choose. A plain record so tests can build any situation.</summary>
public sealed record BrainContext
{
    public DateTime Now { get; init; }
    /// <summary>The "Idle life and flights" switch in Settings.</summary>
    public bool LifeEnabled { get; init; } = true;
    public bool MotionOn { get; init; } = true;
    public bool LowMemory { get; init; }
    public bool GameRunning { get; init; }
    /// <summary>False while the window is minimised, hidden in the tray or covered by nothing we can see.</summary>
    public bool WindowVisible { get; init; } = true;
    public bool WindowActive { get; init; } = true;
    /// <summary>The person typed, clicked or scrolled a moment ago.</summary>
    public bool UserBusy { get; init; }
    public bool PopupOpen { get; init; }
    /// <summary>An act or a flight is still playing.</summary>
    public bool Busy { get; init; }
    public bool Asleep { get; init; }
    /// <summary>At least one safe resting place other than the current one exists.</summary>
    public bool CanFly { get; init; }
    /// <summary>How long nobody has touched the owl or the app.</summary>
    public TimeSpan IdleFor { get; init; }
}

/// <summary>Chooses what Bidar does next. Weighted random with cooldowns, seeded so a run can be replayed; it never touches
/// the screen or a clock of its own (the caller passes <see cref="BrainContext.Now"/>), so every rule is unit-testable.</summary>
public sealed class BidarBrain
{
    public static readonly TimeSpan MinGap = TimeSpan.FromSeconds(4), MaxGap = TimeSpan.FromSeconds(15);

    private readonly record struct Entry(BidarAct Act, double Weight, double CooldownSeconds, double MinIdleSeconds = 0);

    private static readonly Entry[] Table =
    [
        new(BidarAct.Breathe, 10, 8),
        new(BidarAct.Blink, 14, 3),
        new(BidarAct.DoubleBlink, 4, 20),
        new(BidarAct.LookAround, 10, 12),
        new(BidarAct.LanternFlicker, 6, 15),
        new(BidarAct.Stretch, 3, 90, 30),
        new(BidarAct.Yawn, 3, 120, 45),
        new(BidarAct.WingWave, 3, 60),
        new(BidarAct.Hop, 3, 45),
        new(BidarAct.HatFidget, 3, 60),
        new(BidarAct.Hello, 2, 120),
        new(BidarAct.Think, 2, 90),
        new(BidarAct.Search, 2, 150),
        new(BidarAct.Shrug, 1.5, 150),
        new(BidarAct.Peek, 2, 100),
        new(BidarAct.Doze, 1.5, 180, 25),
        new(BidarAct.Fly, 3, 75),
    ];

    private static readonly Dictionary<BidarCue, (BidarAct Act, double CooldownSeconds)> Reactions = new()
    {
        [BidarCue.DownloadDone] = (BidarAct.Celebrate, 2),
        [BidarCue.BadPing] = (BidarAct.Worry, 180),
        [BidarCue.ShutdownSoon] = (BidarAct.Alert, 300),
        [BidarCue.Error] = (BidarAct.Dizzy, 4),
    };

    private readonly Random _rng;
    private readonly Dictionary<BidarAct, DateTime> _last = [];
    private readonly Dictionary<BidarCue, DateTime> _lastCue = [];
    private readonly List<DateTime> _chain = [];
    private DateTime _worriedUntil = DateTime.MinValue;
    private double _fondness;
    private DateTime _fondnessAt;

    public BidarBrain(int seed) => _rng = new Random(seed);

    /// <summary>How long to wait until the next decision: 4 to 15 seconds.</summary>
    public TimeSpan NextGap() => MinGap + (MaxGap - MinGap) * _rng.NextDouble();

    /// <summary>A number in [min, max) from the brain's own seeded stream (hold times, which edge to peek from).</summary>
    public double Between(double min, double max) => min + (max - min) * _rng.NextDouble();

    /// <summary>The person has been nice to him lately (0..1): clicks raise it, five quiet minutes fade it. More hops and waves when high.</summary>
    public double Fondness(DateTime now)
    {
        if (_fondnessAt == default) return 0;
        var minutes = Math.Max(0, (now - _fondnessAt).TotalMinutes);
        return _fondness * Math.Exp(-minutes / 5);
    }

    public static PauseReason Pause(BrainContext c)
    {
        if (!c.LifeEnabled) return PauseReason.Disabled;
        if (!c.MotionOn) return PauseReason.NoMotion;
        if (c.LowMemory) return PauseReason.LowMemory;
        if (c.GameRunning) return PauseReason.GameRunning;
        if (!c.WindowVisible) return PauseReason.Hidden;
        if (!c.WindowActive) return PauseReason.Inactive;
        if (c.PopupOpen) return PauseReason.PopupOpen;
        if (c.UserBusy) return PauseReason.UserBusy;
        if (c.Asleep) return PauseReason.Asleep;
        if (c.Busy) return PauseReason.Busy;
        return PauseReason.None;
    }

    public static bool IsNight(DateTime now) => now.Hour >= 22 || now.Hour < 6;

    /// <summary>What to do now, or null when something pauses him. Records the act so its cooldown starts.</summary>
    public BidarAct? Pick(BrainContext c)
    {
        if (Pause(c) != PauseReason.None) return null;
        var night = IsNight(c.Now);
        var fond = Fondness(c.Now);
        var worried = c.Now < _worriedUntil;
        var weights = new double[Table.Length];
        var total = 0.0;
        for (var i = 0; i < Table.Length; i++)
        {
            var e = Table[i];
            if (c.IdleFor.TotalSeconds < e.MinIdleSeconds) continue;
            if (_last.TryGetValue(e.Act, out var at) && (c.Now - at).TotalSeconds < e.CooldownSeconds) continue;
            if (e.Act == BidarAct.Fly && !c.CanFly) continue;
            var w = e.Weight;
            switch (e.Act)
            {
                case BidarAct.Doze:
                    w *= 1 + c.IdleFor.TotalSeconds / 60;                       // the longer nothing happens, the sleepier
                    if (c.IdleFor.TotalSeconds >= 120) w *= 6;
                    if (night) w *= 4;
                    break;
                case BidarAct.Yawn: if (night) w *= 3; break;
                case BidarAct.Stretch: if (night) w *= 2; break;
                case BidarAct.Hop:
                case BidarAct.Hello:
                case BidarAct.WingWave:
                    w *= 1 + fond;
                    if (night) w *= 0.4;
                    if (worried) w *= 0.2;
                    break;
                case BidarAct.Fly:
                    if (night) w *= 0.5;
                    if (worried) w *= 0.3;
                    break;
                case BidarAct.Shrug:
                case BidarAct.Think:
                    if (worried) w *= 3;
                    break;
            }
            weights[i] = w;
            total += w;
        }
        if (total <= 0) return null;
        var roll = _rng.NextDouble() * total;
        for (var i = 0; i < Table.Length; i++)
        {
            if (weights[i] <= 0) continue;
            roll -= weights[i];
            if (roll > 0) continue;
            _last[Table[i].Act] = c.Now;
            return Table[i].Act;
        }
        var lastIndex = Array.FindLastIndex(weights, w => w > 0); // rounding fallback
        _last[Table[lastIndex].Act] = c.Now;
        return Table[lastIndex].Act;
    }

    /// <summary>His reaction to something in the app, or null when he should stay quiet (hidden window, game, switched off, or he
    /// reacted to the same thing a moment ago). Unlike a random act it may interrupt what he is doing.</summary>
    public BidarAct? OnCue(BidarCue cue, BrainContext c)
    {
        if (!c.LifeEnabled || !c.MotionOn || c.LowMemory || c.GameRunning || !c.WindowVisible || c.PopupOpen) return null;
        var (act, cooldown) = Reactions[cue];
        if (_lastCue.TryGetValue(cue, out var at) && (c.Now - at).TotalSeconds < cooldown) return null;
        _lastCue[cue] = c.Now;
        if (cue == BidarCue.BadPing) _worriedUntil = c.Now.AddSeconds(90);
        return act;
    }

    /// <summary>The cursor stayed close to the owl: a curious look, at most every 25 seconds.</summary>
    public BidarAct? OnCursorNear(BrainContext c)
    {
        if (Pause(c) is not (PauseReason.None or PauseReason.Busy)) return null;
        if (c.Busy) return null;
        if (_last.TryGetValue(BidarAct.Curious, out var at) && (c.Now - at).TotalSeconds < 25) return null;
        _last[BidarAct.Curious] = c.Now;
        return BidarAct.Curious;
    }

    /// <summary>Records a click. Seven clicks in a row, each within 1.5 s of the last but not a frantic burst, is the easter egg.</summary>
    public bool RegisterClick(DateTime now)
    {
        _fondness = Math.Min(1, Fondness(now) + 0.15);
        _fondnessAt = now;
        if (_chain.Count > 0 && (now - _chain[^1]).TotalSeconds > 1.5) _chain.Clear();
        _chain.Add(now);
        if (_chain.Count < 7) return false;
        var span = _chain[^1] - _chain[^5]; // the last five clicks: faster than the burst window means he is just dizzy
        var egg = span > BidarMoodLogic.BurstWindow;
        _chain.Clear();
        return egg;
    }
}

/// <summary>Turns app facts into cues. Pure, so each threshold is tested.</summary>
public static class BidarCues
{
    public const int PingWindow = 6;
    public static readonly TimeSpan SoonWindow = TimeSpan.FromMinutes(10);

    /// <summary>The last few pings show a bad line: at least half lost, or the average above 250 ms. Needs a full window of samples.</summary>
    public static bool PingIsBad(IReadOnlyList<double?> samples)
    {
        if (samples.Count < PingWindow) return false;
        var recent = samples.Skip(samples.Count - PingWindow).ToList();
        var lost = recent.Count(s => s is null);
        if (lost * 2 >= PingWindow) return true;
        var answered = recent.Where(s => s is not null).Select(s => s!.Value).ToList();
        return answered.Count > 0 && answered.Average() > 250;
    }

    /// <summary>A power command comes due within ten minutes (but not yet).</summary>
    public static bool SomethingDueSoon(IEnumerable<DateTime> dueTimes, DateTime now) =>
        dueTimes.Any(d => d > now && d - now <= SoonWindow);

    /// <summary>The cue to give at this moment, ping first (it fades fastest).</summary>
    public static BidarCue? Next(IReadOnlyList<double?> pings, IEnumerable<DateTime> dueTimes, DateTime now) =>
        PingIsBad(pings) ? BidarCue.BadPing : SomethingDueSoon(dueTimes, now) ? BidarCue.ShutdownSoon : null;
}
