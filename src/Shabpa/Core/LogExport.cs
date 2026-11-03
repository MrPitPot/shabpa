using System.IO;
using System.IO.Compression;

namespace Shabpa.Core;

/// <summary>Gathers what helps to find a problem into one zip the person can send: Shabpa's own log (and the one before it), a few lines about the PC and the newest
/// log of the lighting server. Nothing is sent by Shabpa; the file is only written where the person can see it (their desktop).</summary>
public static class LogExport
{
    /// <summary>Writes the zip into <paramref name="folder"/> and returns its path. <paramref name="logFile"/> is Shabpa's log, <paramref name="lightingFolder"/> the folder of the lighting server.</summary>
    public static string Create(string folder, DateTime now, string logFile, string info, string? lightingFolder = null)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "Shabpa-logs-" + now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".zip");   // not the Persian calendar
        var building = path + ".tmp";                                           // a half-written zip is never left on the desktop under its real name
        try
        {
        using (var zip = new ZipArchive(File.Create(building), ZipArchiveMode.Create))
        {
            Add(zip, "shabpa.log", logFile);
            Add(zip, "shabpa.log.old", logFile + ".old");
            if (lightingFolder is not null && Directory.Exists(Path.Combine(lightingFolder, "logs"))
                && Directory.GetFiles(Path.Combine(lightingFolder, "logs"), "*.log").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).LastOrDefault() is { } newest)
                Add(zip, "lighting-server.log", newest);
            var entry = zip.CreateEntry("info.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(info);
        }
        File.Move(building, path, true);
        }
        catch
        {
            try { File.Delete(building); } catch (IOException) { }
            throw;
        }
        return path;
    }

    private static readonly System.Text.RegularExpressions.Regex UserFolder = new(@"(?i)([A-Z]:[\\/]+Users[\\/]+)[^\\/\r\n""':<>|*?]+", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>The Windows user name sits in every path of a log line («C:\Users\name\AppData…»): it is replaced, so the file can be sent without it.</summary>
    internal static string Scrub(string text) => UserFolder.Replace(text, "$1<user>");

    private static void Add(ZipArchive zip, string name, string file)
    {
        if (!File.Exists(file)) return;
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        // the log is being written while it is read; the user name in its paths is taken out on the way
        using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(input);
        using var output = new StreamWriter(entry.Open(), new System.Text.UTF8Encoding(false));
        string? line;
        while ((line = reader.ReadLine()) is not null) output.WriteLine(Scrub(line));
    }

    /// <summary>The lines of info.txt: what <see cref="SupportInfo.Diagnostics"/> gives, plus the time and the Windows and .NET versions.</summary>
    public static string Info(bool elevated, DateTime now) =>
        SupportInfo.Diagnostics(elevated) + "\n" + "Created: " + now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + "\n" + $".NET: {Environment.Version}\n";
}
