namespace Shabpa.Core;

/// <summary>Fixed room for every overlay reading. A reading is drawn inside a slot whose width is that of its widest plausible text, so the
/// numbers change only inside their slots and the overlay never grows, shrinks or moves because of a value. Pure, so it is tested.</summary>
public static class OverlaySlots
{
    /// <summary>'#' stands for the widest digit of the font.</summary>
    public const char Digit = '#';

    /// <summary>The widest text a reading may show, as a pattern ('#' = a digit).</summary>
    public static string NumberPattern(string key) => key switch
    {
        "fps" or "low1" => "#####",
        "frame" => "####.#",
        "hz" or "ping" or "gpuclock" or "memclock" or "cpufreq" or "gpupower" or "cpuwatts" or "gpuvolt" => "####",
        "gpu" or "cpu" or "ram" or "gpufan" or "cpufan" => "###.#",
        "gputemp" or "hotspot" or "memtemp" or "cputemp" or "systemp" or "sockettemp" or "vrmtemp" or "pchtemp" => "###",
        "gpufanrpm" or "cpufanrpm" or "pumprpm" or "sysfanrpm" or "down" or "up" => "#####",
        "vram" => "##.#",
        "ramused" => "###.#",
        "vcore" or "v12" => "##.##",
        "clock" => "##:##",
        "gametime" => "###:##:##",
        _ => "#####",
    };

    /// <summary>The widest unit that can follow the number (the VRAM reading carries its total: "/ 16 GB").</summary>
    public static string UnitPattern(string key, string unit) => key == "vram" ? "/ ## GB" : unit;

    /// <summary>How many characters the slot of a reading holds; a longer text is clipped to the slot, never allowed to widen it.</summary>
    public static int Capacity(string key) => NumberPattern(key).Length;

    /// <summary>Everything that decides the outer size of the overlay: layout, scale, labels, language and the list of readings.
    /// While it stays the same the window keeps its size, whatever the values do. Look, colours and opacity do not change the size.</summary>
    public static string Signature(OverlaySettings o, IEnumerable<string> keys, bool english) =>
        string.Join('|', o.Layout, o.Scale.ToString("R", System.Globalization.CultureInfo.InvariantCulture), o.ShowLabels, o.Rounded, english, string.Join(',', keys));
}
