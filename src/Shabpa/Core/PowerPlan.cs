using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Shabpa.Core;

/// <summary>Reads and switches the Windows power plan through powercfg. Used by game mode to run a game on the
/// high-performance plan and put the previous plan back afterwards.</summary>
public static class PowerPlan
{
    public const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    public const string Ultimate = "e9a42b02-d5df-448d-aa00-03f14749eb61";

    private static readonly Regex GuidPattern = new(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b");

    public static bool IsGuid(string value) => value.Length == 36 && GuidPattern.IsMatch(value);

    /// <summary>The GUID in a "powercfg /getactivescheme" line.</summary>
    internal static string? ParseActive(string output)
    {
        var m = GuidPattern.Match(output);
        return m.Success ? m.Value.ToLowerInvariant() : null;
    }

    /// <summary>All plan GUIDs in a "powercfg /list" output.</summary>
    internal static List<string> ParseList(string output) =>
        [.. GuidPattern.Matches(output).Select(m => m.Value.ToLowerInvariant()).Distinct()];

    private static string? Run(string arguments)
    {
        try
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo("powercfg.exe", arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true },
            };
            p.Start();
            var text = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(8000)) return null;
            return p.ExitCode == 0 ? text : null;
        }
        catch (Exception ex)
        {
            Log.Write($"powercfg: {ex.GetType().Name}");
            return null;
        }
    }

    public static string? Active() => Run("/getactivescheme") is { } o ? ParseActive(o) : null;

    public static List<string> Installed() => Run("/list") is { } o ? ParseList(o) : [];

    public static bool Set(string guid) => IsGuid(guid) && Run("/setactive " + guid) is not null;

    /// <summary>Switches to the best performance plan that exists on this PC; returns the plan that was active before,
    /// or null if nothing was changed.</summary>
    public static string? SwitchToPerformance()
    {
        var before = Active();
        if (before is null) return null;
        var installed = Installed();
        var target = installed.Contains(Ultimate) ? Ultimate : installed.Contains(HighPerformance) ? HighPerformance : null;
        if (target is null || target == before) return null;
        return Set(target) ? before : null;
    }
}
