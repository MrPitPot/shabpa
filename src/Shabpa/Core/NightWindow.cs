namespace Shabpa.Core;

/// <summary>The hours of cheap night internet the person's package gives (set by hand: packages differ, nothing is assumed).</summary>
public sealed class NightSettings
{
    /// <summary>A night plan made by the night-internet card exists.</summary>
    public bool Enabled { get; set; }
    /// <summary>Minutes after midnight. 23:00 to 06:00 is start 1380, end 360 (the window crosses midnight).</summary>
    public int StartMinute { get; set; } = 0;
    public int EndMinute { get; set; } = 6 * 60;
    /// <summary>Show the "last night" line on Home and tell the person in the morning how much came in during the window.</summary>
    public bool MorningReport { get; set; } = true;
    /// <summary>The morning (yyyy-MM-dd) the report was already shown for.</summary>
    public string ReportedMorning { get; set; } = "";
}

/// <summary>Splits the hourly usage Shabpa already records into "inside the night window" and "outside it". Usage is kept per hour,
/// so an hour counts as night when its middle falls inside the window.</summary>
public static class NightWindow
{
    public delegate (long[] down, long[] up) Hourly(DateOnly day);

    public static bool Wraps(int startMinute, int endMinute) => startMinute >= endMinute;

    /// <summary>The hour (0 to 23) belongs to the window.</summary>
    public static bool IsNightHour(int hour, int startMinute, int endMinute)
    {
        var mid = hour * 60 + 30;
        return Wraps(startMinute, endMinute) ? mid >= startMinute || mid < endMinute : mid >= startMinute && mid < endMinute;
    }

    /// <summary>Bytes inside the window that ended on the morning of <paramref name="morning"/>.</summary>
    public static (long down, long up) Night(Hourly hourly, DateOnly morning, int startMinute, int endMinute)
    {
        long down = 0, up = 0;
        if (Wraps(startMinute, endMinute))
        {
            var (ed, eu) = hourly(morning.AddDays(-1));
            var (md, mu) = hourly(morning);
            for (var h = 0; h < 24; h++)
            {
                var mid = h * 60 + 30;
                if (mid >= startMinute) { down += At(ed, h); up += At(eu, h); }
                if (mid < endMinute) { down += At(md, h); up += At(mu, h); }
            }
        }
        else
        {
            var (md, mu) = hourly(morning);
            for (var h = 0; h < 24; h++)
                if (IsNightHour(h, startMinute, endMinute)) { down += At(md, h); up += At(mu, h); }
        }
        return (down, up);
    }

    /// <summary>One calendar day cut into the bytes that fell in night hours and the rest.</summary>
    public static (long night, long day) Split(Hourly hourly, DateOnly date, int startMinute, int endMinute)
    {
        var (d, u) = hourly(date);
        long night = 0, rest = 0;
        for (var h = 0; h < 24; h++)
        {
            var bytes = At(d, h) + At(u, h);
            if (IsNightHour(h, startMinute, endMinute)) night += bytes; else rest += bytes;
        }
        return (night, rest);
    }

    private static long At(long[] a, int i) => i < a.Length ? a[i] : 0;
}
