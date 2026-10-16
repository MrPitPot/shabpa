using System.IO;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Shabpa.Core;

/// <summary>One program that has a newer version available.</summary>
public sealed record AppUpdate(string Name, string Id, string Installed, string Available, string Source)
{
    /// <summary>winget shows "Unknown" or a "&lt; 1.2" range when it could not tell exactly what is installed.</summary>
    public bool InstalledUnknown => Installed.Length == 0 || Installed.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase) || Installed.StartsWith('<');
}

/// <summary>Reads and runs Windows' own package manager (winget): the list of programs that can be updated, and the
/// update itself. Nothing is downloaded by Shabpa; winget fetches from each program's publisher and checks it.</summary>
public static class Winget
{
    /// <summary>Terminal cell width of a character: East Asian wide characters take two columns in winget's table.</summary>
    private static int Cells(char c) => c switch
    {
        >= 'ᄀ' and <= 'ᅟ' or >= '⺀' and <= '꓏' or >= '가' and <= '힣' or >= '豈' and <= '﫿'
            or >= '︰' and <= '﹯' or >= '＀' and <= '｠' or >= '￠' and <= '￦' => 2,
        _ => 1,
    };

    /// <summary>The part of <paramref name="line"/> that sits between two column positions (in terminal cells).</summary>
    private static string Slice(string line, int fromCell, int toCell)
    {
        var sb = new StringBuilder();
        var cell = 0;
        foreach (var c in line)
        {
            var w = Cells(c);
            if (cell >= fromCell && (toCell < 0 || cell < toCell)) sb.Append(c);
            cell += w;
            if (toCell >= 0 && cell >= toCell && cell > fromCell) break;
        }
        return sb.ToString().Trim();
    }

    private static int CellOf(string line, string word)
    {
        var index = line.IndexOf(word, StringComparison.Ordinal);
        if (index < 0) return -1;
        var cell = 0;
        for (var i = 0; i < index; i++) cell += Cells(line[i]);
        return cell;
    }

    /// <summary>Parses the table printed by <c>winget upgrade</c>.</summary>
    public static List<AppUpdate> ParseUpgradeTable(string output)
    {
        var result = new List<AppUpdate>();
        // The progress spinner writes "-", "\", "|" and carriage returns before the table; only whole lines count.
        var lines = output.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => l.TrimEnd()).ToList();
        var header = lines.FindIndex(l => l.StartsWith("Name", StringComparison.Ordinal) && l.Contains(" Id ") && l.Contains("Version") && l.Contains("Available"));
        if (header < 0) return result;
        // the rows start after the line made only of dashes
        var rule = lines.FindIndex(header + 1, l => l.Length >= 5 && l.All(c => c == '-'));
        if (rule < 0) return result;

        var h = lines[header];
        var id = CellOf(h, "Id");
        var version = CellOf(h, "Version");
        var available = CellOf(h, "Available");
        var source = CellOf(h, "Source");
        if (id < 0 || version < 0 || available < 0) return result;

        for (var i = rule + 1; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Length == 0) continue;
            // "19 upgrades available." and the notes about pinned or unknown versions end the table
            if (Regex.IsMatch(line, @"^\d+\s+(upgrades?|package)", RegexOptions.IgnoreCase) || line.StartsWith("The following", StringComparison.Ordinal)) break;
            var name = Slice(line, 0, id);
            var pkgId = Slice(line, id, version);
            var installed = Slice(line, version, available);
            var latest = Slice(line, available, source > 0 ? source : -1);
            var from = source > 0 ? Slice(line, source, -1) : "";
            if (name.Length == 0 || pkgId.Length == 0 || latest.Length == 0 || pkgId.Contains(' ')) continue;
            result.Add(new AppUpdate(name, pkgId, installed, latest, from));
        }
        return result;
    }

    public static bool IsAvailable()
    {
        try
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                if (File.Exists(Path.Combine(dir.Trim(), "winget.exe"))) return true;
            return File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "winget.exe"));
        }
        catch
        {
            return false;
        }
    }

    private static async Task<(int code, string output)> RunAsync(string arguments, TimeSpan timeout, Action<string>? line, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("winget.exe", arguments)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            },
        };
        var sb = new StringBuilder();
        void Got(string? text)
        {
            if (text is null) return;
            lock (sb) sb.AppendLine(text);
            line?.Invoke(text);
        }
        process.OutputDataReceived += (_, e) => Got(e.Data);
        process.ErrorDataReceived += (_, e) => Got(e.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cancel.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(cancel.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw;
        }
        lock (sb) return (process.ExitCode, sb.ToString());
    }

    /// <summary>Programs with a newer version. Throws if winget is missing or fails.</summary>
    public static async Task<List<AppUpdate>> ListUpgradesAsync(CancellationToken ct = default)
    {
        var (code, output) = await RunAsync("upgrade --include-unknown --accept-source-agreements --disable-interactivity", TimeSpan.FromMinutes(3), null, ct);
        var list = ParseUpgradeTable(output);
        // exit code 0 with nothing listed = everything is current; a non-zero code with no table is a failure
        if (list.Count == 0 && code != 0 && !output.Contains("No installed package found matching input criteria", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(output.Trim().Split('\n').LastOrDefault() ?? "winget failed");
        return list;
    }

    /// <summary>Updates one program silently. Returns null on success, otherwise a short reason.</summary>
    public static async Task<string?> UpgradeAsync(string id, Action<string>? progress = null, CancellationToken ct = default)
    {
        if (!Regex.IsMatch(id, @"^[A-Za-z0-9][A-Za-z0-9._\-+]*$")) return "invalid id"; // never pass anything odd to a command line
        var (code, output) = await RunAsync(
            $"upgrade --id {id} --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity",
            TimeSpan.FromMinutes(30), progress, ct);
        if (code == 0) return null;
        // winget's own explanation is the last meaningful line
        var reason = output.Replace("\r", "\n").Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 3 && !l.All(c => c is '-' or '\\' or '|' or '/' or '█' or '▒'));
        return reason ?? $"winget exit code {code}";
    }
}
