namespace Shabpa.Core;

/// <summary>The unit internet speeds are shown in: megabit (what providers sell), megabyte (what download managers show) or kilobyte.
/// The speeds themselves are always measured in megabit per second; this only converts them for display.</summary>
public static class SpeedUnit
{
    public const string Mbit = "mbit", MByte = "mbyte", KByte = "kbyte";

    public static readonly string[] All = [Mbit, MByte];

    /// <summary>The choice to use for display, read from the settings (megabit when nothing is loaded yet, e.g. in tests).</summary>
    public static string Key => Clean(Hub.I?.Settings.SpeedUnit);

    public static string Clean(string? key) => key is Mbit or MByte or KByte ? (key == KByte ? Mbit : key) : Mbit;

    /// <summary>"Mb/s", "MB/s" or "KB/s": the widest unit the current choice can show (used to size fixed slots).</summary>
    public static string WidestUnit => Key switch { MByte => "MB/s", KByte => "KB/s", _ => "Mb/s" };

    /// <summary>Converts megabit per second into the chosen unit and its name. Megabit and megabyte switch to kilo below 1.</summary>
    public static (double value, string unit) Of(double mbps)
    {
        switch (Key)
        {
            case KByte: return (mbps * 125, "KB/s");
            case MByte:
                var mb = mbps / 8;
                return mb >= 1 ? (mb, "MB/s") : (mb * 1000, "KB/s");
            default:
                return mbps >= 1 ? (mbps, "Mb/s") : (mbps * 1000, "Kb/s");
        }
    }

    /// <summary>The names shown in the settings dropdown.</summary>
    public static string Title(string key) => key switch
    {
        MByte => L.T("مگابایت بر ثانیه (MB/s)", "Megabytes per second (MB/s)"),
        KByte => L.T("کیلوبایت بر ثانیه (KB/s)", "Kilobytes per second (KB/s)"),
        _ => L.T("مگابیت بر ثانیه (Mb/s)", "Megabits per second (Mb/s)"),
    };
}
