using System.Diagnostics;

namespace Shabpa.Core;

/// <summary>One running program (all its processes together), as the person sees it in Task Manager.</summary>
public sealed record ProgramRow(string Name, string Title, int Count, long Bytes, IReadOnlyList<int> Pids, bool Protected);

public enum CloseOutcome { Closed, StillRunning, Protected, NotRunning }

/// <summary>The programs that use the most memory, with a polite «close» and a «trim» for each. Windows' own parts, the security software and Shabpa
/// itself are listed (so the picture is honest) but cannot be closed from here. A program is asked to close like clicking its X first; only when the
/// person confirms a second time is it ended by force, which can lose unsaved work.</summary>
public static class ProcessList
{
    /// <summary>Programs that must never be closed from here: the parts Windows needs to run, the security software and the shell.</summary>
    internal static readonly HashSet<string> Critical = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "registry", "memcompression", "memory compression", "secure system", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso",
        "svchost", "dwm", "fontdrvhost", "explorer", "sihost", "ctfmon", "audiodg", "spoolsv", "wudfhost", "dllhost", "conhost", "taskhostw", "runtimebroker",
        "startmenuexperiencehost", "searchhost", "searchindexer", "textinputhost", "shellexperiencehost", "securityhealthservice", "securityhealthsystray",
        "msmpeng", "nissrv", "smartscreen", "sgrmbroker", "wmiprvse", "lockapp", "logonui", "applicationframehost", "userinit", "dashost",
    };

    public static bool IsProtected(string name, int? ownPid = null, string? ownName = null)
    {
        var n = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        return Critical.Contains(n) || (ownName is not null && n.Equals(ownName, StringComparison.OrdinalIgnoreCase)) || n.StartsWith("Shabpa", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Groups raw processes by program name, biggest memory first. Pure: <paramref name="processes"/> is (pid, name, window title, bytes).</summary>
    public static List<ProgramRow> Group(IEnumerable<(int Pid, string Name, string Title, long Bytes)> processes, int count, string? ownName = null) =>
        [.. processes.Where(p => p.Pid > 4 && p.Name.Length > 0)
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ProgramRow(g.Key, g.Select(p => p.Title).FirstOrDefault(t => t.Length > 0) ?? "", g.Count(), g.Sum(p => p.Bytes), [.. g.Select(p => p.Pid)], IsProtected(g.Key, null, ownName)))
            .OrderByDescending(r => r.Bytes).Take(count)];

    public static List<ProgramRow> Top(int count)
    {
        var raw = new List<(int, string, string, long)>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try { raw.Add((p.Id, p.ProcessName, p.MainWindowTitle ?? "", p.WorkingSet64)); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { /* it ended meanwhile or is not readable */ }
            }
        }
        return Group(raw, count, Process.GetCurrentProcess().ProcessName);
    }

    /// <summary>Asks every process of the program to close its window and waits a little. Nothing is ended by force here.</summary>
    public static CloseOutcome Close(string name, int waitMs = 3000)
    {
        if (IsProtected(name, null, Process.GetCurrentProcess().ProcessName)) return CloseOutcome.Protected;
        var procs = Process.GetProcessesByName(name);
        if (procs.Length == 0) return CloseOutcome.NotRunning;
        try
        {
            foreach (var p in procs) { try { p.CloseMainWindow(); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { } }
            var deadline = Environment.TickCount64 + waitMs;
            while (Environment.TickCount64 < deadline && procs.Any(p => !Exited(p))) Thread.Sleep(100);
            return procs.All(Exited) ? CloseOutcome.Closed : CloseOutcome.StillRunning;
        }
        finally { foreach (var p in procs) p.Dispose(); }
    }

    /// <summary>Ends the program by force (the person confirmed it knowing unsaved work is lost). Returns how many processes were ended.</summary>
    public static int ForceClose(string name)
    {
        if (IsProtected(name, null, Process.GetCurrentProcess().ProcessName)) return 0;
        var ended = 0;
        foreach (var p in Process.GetProcessesByName(name))
        {
            using (p)
            {
                try { p.Kill(); ended++; }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { /* protected or already gone */ }
            }
        }
        return ended;
    }

    private static bool Exited(Process p)
    {
        try { return p.HasExited; }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return true; }
    }
}
