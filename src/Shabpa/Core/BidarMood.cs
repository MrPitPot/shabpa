namespace Shabpa.Core;

/// <summary>What Bidar's face and body show. The rig (Kit/BidarOwl.cs) draws any of these; this file is pure logic.</summary>
public enum BidarMood
{
    Idle, Think, Happy, Confused, Worried, Surprised, Wink, Love, Dizzy, Sleepy,
    // poses from the poses sheet (a prop beside the head and/or a wing gesture)
    Search, Point, HatTip, Hello, Telegram, Schedule, Power, Download, Ping, Comfort, Guard, Peek, Shrug, Error, Celebrate, Alert, Stretch, Yawn, Fly,
}

/// <summary>The shape of the eyes: round, a closed line, a happy arc, hearts, spirals.</summary>
public enum BidarEyes { Open, Closed, Arc, Hearts, Spiral, Wide }

/// <summary>Numbers that describe one pose (ranges follow the animation guide: wings -45..160 degrees, squash 0.88..1.1).</summary>
public sealed record BidarPose(BidarEyes Eyes, bool WinkRight, double Beak, double WingL, double WingR, double Tilt, double Squash, double Blush, double Lids, string? Fx);

public static class BidarPoses
{
    public static BidarPose For(BidarMood mood) => mood switch
    {
        BidarMood.Think => new(BidarEyes.Open, false, 0, 14, 14, -6, 1, 0.25, 0, null),
        BidarMood.Happy => new(BidarEyes.Arc, false, 0.5, 60, 60, 4, 1.02, 0.9, 0, "star"),
        BidarMood.Confused => new(BidarEyes.Open, false, 0.2, 14, 60, 10, 1, 0.2, 0, "?"),
        BidarMood.Worried => new(BidarEyes.Open, false, 0.3, 100, 100, -4, 0.96, 0.1, 0.25, null),
        BidarMood.Surprised => new(BidarEyes.Wide, false, 1, 130, 130, 0, 1.08, 0.4, 0, "!"),
        BidarMood.Wink => new(BidarEyes.Open, true, 0.4, 14, 100, 6, 1, 0.8, 0, null),
        BidarMood.Love => new(BidarEyes.Hearts, false, 0.3, 60, 60, 0, 1.04, 1, 0, "heart"),
        BidarMood.Dizzy => new(BidarEyes.Spiral, false, 0.5, 100, 14, 12, 0.96, 0.3, 0, "stars"),
        // asleep: wings folded flat against the sides (the old -45 turned them inward through the belly), head dropped to one side
        BidarMood.Sleepy => new(BidarEyes.Closed, false, 0, 3, 3, 9, 0.95, 0.3, 1, "z"),
        BidarMood.Search => new(BidarEyes.Open, false, 0.1, 14, 92, -8, 1, 0.3, 0, "magnifier"),
        BidarMood.Point => new(BidarEyes.Open, false, 0.2, 14, 112, 6, 1, 0.4, 0, null),
        BidarMood.HatTip => new(BidarEyes.Arc, false, 0.2, 14, 138, -10, 1, 0.7, 0, null),
        BidarMood.Hello => new(BidarEyes.Arc, false, 0.5, 14, 128, 4, 1.02, 0.8, 0, null),
        BidarMood.Telegram => new(BidarEyes.Open, false, 0.3, 70, 70, 0, 1, 0.5, 0, "plane"),
        BidarMood.Schedule => new(BidarEyes.Open, false, 0.1, 14, 40, -4, 1, 0.4, 0, "clock"),
        BidarMood.Power => new(BidarEyes.Open, false, 0.2, 60, 60, 0, 1, 0.3, 0, "power"),
        BidarMood.Download => new(BidarEyes.Arc, false, 0.4, 100, 100, 0, 1.03, 0.7, 0, "down"),
        BidarMood.Ping => new(BidarEyes.Open, false, 0.2, 14, 14, -4, 1, 0.3, 0, "signal"),
        BidarMood.Comfort => new(BidarEyes.Arc, false, 0.3, 62, 62, 4, 1.02, 1, 0.2, "heart"),
        BidarMood.Guard => new(BidarEyes.Open, false, 0.1, 24, 24, 0, 1.04, 0.2, 0, "shield"),
        BidarMood.Peek => new(BidarEyes.Open, false, 0.1, 6, 6, 14, 1, 0.5, 0.15, null),
        BidarMood.Shrug => new(BidarEyes.Open, false, 0.3, 96, 96, -5, 1, 0.3, 0, null),
        BidarMood.Error => new(BidarEyes.Wide, false, 0.7, 120, 120, 0, 1.06, 0.2, 0, "sweat"),
        BidarMood.Celebrate => new(BidarEyes.Arc, false, 0.8, 150, 150, 0, 1.06, 1, 0, "confetti"),
        BidarMood.Alert => new(BidarEyes.Wide, false, 0.6, 112, 112, 0, 1.07, 0.3, 0, "clock"),
        BidarMood.Stretch => new(BidarEyes.Arc, false, 0.2, 140, 140, 0, 1.08, 0.5, 0, null),
        BidarMood.Yawn => new(BidarEyes.Closed, false, 1, 50, 50, -3, 1.04, 0.4, 1, null),
        BidarMood.Fly => new(BidarEyes.Open, false, 0.1, 90, 90, 0, 1, 0.45, 0, null),
        _ => new(BidarEyes.Open, false, 0, 14, 14, 0, 1, 0.45, 0, null),
    };
}

/// <summary>Picks Bidar's reaction to clicks and when he dozes off. A calm click cycles through surprised, happy, wink
/// and love; a quick burst makes him dizzy; he falls asleep after a quiet minute. No timers here: the caller passes the clock.</summary>
public sealed class BidarMoodLogic
{
    public static readonly TimeSpan BurstWindow = TimeSpan.FromSeconds(2.2);
    public const int BurstClicks = 5;
    public static readonly TimeSpan SleepAfter = TimeSpan.FromSeconds(60);
    private static readonly BidarMood[] Cycle = [BidarMood.Surprised, BidarMood.Happy, BidarMood.Wink, BidarMood.Love];

    private readonly Queue<DateTime> _clicks = new();
    private int _next;

    public DateTime LastTouch { get; private set; } = DateTime.MinValue;

    public BidarMood Click(DateTime now)
    {
        LastTouch = now;
        _clicks.Enqueue(now);
        while (_clicks.Count > 0 && now - _clicks.Peek() > BurstWindow) _clicks.Dequeue();
        if (_clicks.Count >= BurstClicks)
        {
            _clicks.Clear();
            return BidarMood.Dizzy;
        }
        var mood = Cycle[_next];
        _next = (_next + 1) % Cycle.Length;
        return mood;
    }

    public void Touch(DateTime now) => LastTouch = now;

    public bool ShouldSleep(DateTime now) => LastTouch != DateTime.MinValue && now - LastTouch >= SleepAfter;

    /// <summary>How far the pupils move toward the cursor: the direction scaled to at most <paramref name="max"/>.</summary>
    public static (double X, double Y) Look(double dx, double dy, double max)
    {
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-6) return (0, 0);
        var k = Math.Min(1, len / 160) * max / len; // eases in over the first 160 px, never beyond max
        return (dx * k, dy * k);
    }
}
