using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Shabpa.Core;

/// <summary>The one release of the free, open-source OpenRGB that Shabpa will fetch: the official file and its SHA-256, both fixed in the program.</summary>
public sealed record OpenRgbRelease(string Version, string Url, string Sha256)
{
    /// <summary>Other places holding the very same file, tried in turn when the official one cannot be reached (the official host is blocked on some networks).
    /// Whatever comes from them is still refused unless its SHA-256 is the fixed one, so a mirror can never hand over a different file.</summary>
    public IReadOnlyList<string> Mirrors { get; init; } = [];
}

/// <summary>Makes lighting work with one click: Shabpa fetches the official OpenRGB once (22 MB, checked against a fixed SHA-256, nothing else is accepted),
/// keeps it in its own folder, starts it hidden as a local-only lighting server (127.0.0.1, its own config) when the lights are on and stops it with them.
/// The person never installs, opens or configures OpenRGB. Nothing is sent anywhere: the only network call is the download of that one file.</summary>
public static class OpenRgbHost
{
    /// <summary>OpenRGB 1.1 for Windows 64-bit, from the project's official release page. The hash was computed from that file; a different file is refused.</summary>
    public static readonly OpenRgbRelease Release = new("1.1",
        "https://codeberg.org/OpenRGB/OpenRGB/releases/download/release_1.1/OpenRGB_1.1_Windows_64_8e16de2.zip",
        "63291726c6fc84eb479ef2ee752e97e1187f6ce9c49fd27eee40ded588a8172a")
    {
        Mirrors =
        [
            // the project's own GitHub mirror of the same release (codeberg.org is blocked on some networks); the same file, same SHA-256
            "https://github.com/CalcProgrammer1/OpenRGB/releases/download/release_1.1/OpenRGB_1.1_Windows_64_8e16de2.zip",
        ],
    };

    /// <summary>Where it lives (a folder of Shabpa's own; tests point it elsewhere).</summary>
    public static string Folder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Shabpa", "openrgb");

    public static string ExePath => Path.Combine(Folder, "OpenRGB.exe");

    private static string Marker => Path.Combine(Folder, "shabpa-openrgb.txt");

    /// <summary>True when the checked release is unpacked and complete.</summary>
    public static bool Installed
    {
        get
        {
            try { return File.Exists(ExePath) && File.Exists(Marker) && File.ReadAllText(Marker).Split('\n')[0].Trim() == Release.Sha256; }
            catch (IOException) { return false; }
        }
    }

    /// <summary>Everything OpenRGB is started with: a server on this PC only, its own config folder, no window, no hunt for other servers.</summary>
    public static string Arguments(int port) => $"--server --server-host 127.0.0.1 --server-port {port} --localconfig --noautoconnect";

    public readonly record struct InstallResult(bool Ok, string Error);

    /// <summary>Downloads the release, refuses it unless its SHA-256 is the fixed one, unpacks it into <see cref="Folder"/> (every path kept inside the folder) and
    /// writes the marker last, so a half-done install is never taken for a finished one. <paramref name="percent"/> gets 0-100.</summary>
    public static async Task<InstallResult> InstallAsync(IProgress<int>? percent = null, CancellationToken ct = default, HttpMessageHandler? handler = null, OpenRgbRelease? release = null)
    {
        release ??= Release;
        var temp = Folder + ".download";
        var staging = Folder + ".new";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Folder)!);
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            if (File.Exists(temp)) File.Delete(temp);

            // the official address first, then the mirrors; a file only counts when its SHA-256 is the fixed one
            var hash = "";
            var wrongHash = false;
            foreach (var url in new[] { release.Url }.Concat(release.Mirrors))
            {
                try
                {
                    using var own = handler is null ? new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(15) } : null;
                    using var http = handler is null ? new HttpClient(own!, disposeHandler: false) : new HttpClient(handler, disposeHandler: false);
                    http.Timeout = TimeSpan.FromMinutes(10);
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("Shabpa");
                    using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode) { Log.Write($"openrgb download from {new Uri(url).Host}: HTTP {(int)response.StatusCode}"); continue; }
                    var total = response.Content.Headers.ContentLength ?? 0;
                    using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    await using (var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                    await using (var output = File.Create(temp))
                    {
                        var buffer = new byte[81920];
                        long done = 0;
                        int read;
                        var last = -1;
                        while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                        {
                            sha.AppendData(buffer, 0, read);
                            await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                            done += read;
                            var p = total > 0 ? (int)Math.Min(99, done * 100 / total) : 0;
                            if (p != last) { last = p; percent?.Report(p); }
                        }
                    }
                    var got = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
                    if (got.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase)) { hash = got; wrongHash = false; break; }
                    Log.Write($"openrgb download from {new Uri(url).Host} refused: wrong SHA-256");
                    wrongHash = true;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
                {
                    if (ct.IsCancellationRequested) throw;
                    Log.Write($"openrgb download from {new Uri(url).Host}: {ex.GetType().Name}: {ex.InnerException?.Message ?? ex.Message}");
                }
            }

            if (hash.Length == 0) return new(false, wrongHash ? "hash" : "download");

            Unpack(temp, staging);
            if (!File.Exists(Path.Combine(staging, "OpenRGB.exe"))) return new(false, "unpack");
            // line 1: the SHA-256 of the file that was downloaded; line 2: the SHA-256 of the OpenRGB.exe that came out of it, checked again before every start
            File.WriteAllText(Path.Combine(staging, "shabpa-openrgb.txt"), release.Sha256 + "\n" + HashOf(Path.Combine(staging, "OpenRGB.exe")));
            Stop();
            if (Directory.Exists(Folder)) Directory.Delete(Folder, true);   // only ever Shabpa's own copy
            Directory.Move(staging, Folder);
            percent?.Report(100);
            return new(true, "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Log.Write($"openrgb install: {ex.GetType().Name}: {ex.Message}");
            return new(false, "unpack");
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { }
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch (IOException) { }
        }
    }

    /// <summary>Unpacks the zip into <paramref name="target"/>, dropping the one top folder it comes in and refusing any entry that would land outside the target.</summary>
    internal static void Unpack(string zipFile, string target)
    {
        using var zip = ZipFile.OpenRead(zipFile);
        var root = Path.GetFullPath(target) + Path.DirectorySeparatorChar;
        var names = zip.Entries.Where(e => e.Name.Length > 0).Select(e => e.FullName.Replace('\\', '/')).ToList();
        var top = names.Count > 0 && names.All(n => n.Contains('/')) && names.Select(n => n[..n.IndexOf('/')]).Distinct().Count() == 1 ? names[0][..(names[0].IndexOf('/') + 1)] : "";
        Directory.CreateDirectory(target);
        foreach (var entry in zip.Entries)
        {
            if (entry.Name.Length == 0) continue;                              // a folder entry
            var relative = entry.FullName.Replace('\\', '/');
            if (top.Length > 0 && relative.StartsWith(top, StringComparison.Ordinal)) relative = relative[top.Length..];
            var destination = Path.GetFullPath(Path.Combine(target, relative));
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("an entry leaves the folder");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }
    }

    // ---- running -----------------------------------------------------------------------------------------------------

    private static Process? _process;
    private static readonly object Gate = new();

    /// <summary>The runtime files OpenRGB 1.1 (built with Visual C++ and Qt 6) cannot start without, and which a clean Windows 10 often lacks.</summary>
    internal static readonly string[] RuntimeFiles = ["vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll", "msvcp140_1.dll"];

    /// <summary>Oldest Windows 10 build Qt 6 runs on (1809).</summary>
    internal const int OldestBuild = 17763;

    /// <summary>Why the server cannot start on this PC before even trying: "windows" (too old a Windows 10), "runtime" (the Microsoft Visual C++ runtime is missing), or "".</summary>
    public static string Blocked(string? systemDirectory = null, int? build = null)
    {
        if ((build ?? Environment.OSVersion.Version.Build) < OldestBuild) return "windows";
        var dir = systemDirectory ?? Environment.SystemDirectory;
        return RuntimeFiles.Any(f => !File.Exists(Path.Combine(dir, f)) && !File.Exists(Path.Combine(Folder, f))) ? "runtime" : "";
    }

    /// <summary>The exit code of the server when it ended by itself (a crash or a missing file), so the page can say so.</summary>
    public static int? LastExit { get; private set; }

    /// <summary>Marks a task's failure as seen. A connect that timed out is abandoned and then fails when its socket is closed; left alone, every one of those
    /// became an "unobserved task exception" (hundreds in a day on a PC whose lighting server was not running).</summary>
    internal static void Observe(Task task) => task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);

    /// <summary>True when something already answers on the lighting port of this PC (ours, or one the person runs themselves).</summary>
    public static bool IsListening(int port, int timeoutMs = 200)
    {
        try
        {
            using var tcp = new TcpClient();
            var connect = tcp.ConnectAsync("127.0.0.1", port);
            if (connect.Wait(timeoutMs)) return tcp.Connected;
            Observe(connect);
            return false;
        }
        catch (Exception ex) when (ex is SocketException or AggregateException) { return false; }
    }

    private static string HashOf(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>The folder is writable by anything the person runs, and Shabpa is often elevated: an OpenRGB.exe that is not the one that was unpacked is never started. An install from before this
    /// check has no second line; its exe is taken as it is now, and recorded.</summary>
    internal static bool ExeIntact()
    {
        try
        {
            var lines = File.ReadAllText(Marker).Split('\n');
            var now = HashOf(ExePath);
            if (lines.Length < 2 || lines[1].Trim().Length == 0)
            {
                File.WriteAllText(Marker, lines[0].Trim() + "\n" + now);
                return true;
            }
            return string.Equals(lines[1].Trim(), now, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static long _noRetryUntil;
    private static int _quickEnds;

    /// <summary>A server that ends within this time of its start did not really start (a missing file, a blocked driver).</summary>
    internal const int QuickEndMs = 20_000;

    /// <summary>Called when the server we started has ended by itself after <paramref name="aliveMs"/>: three such quick ends in a row stop the restarts for five minutes (a server that
    /// cannot start was started again every few seconds, for ever).</summary>
    internal static void NoteEnded(long aliveMs, long now)
    {
        if (aliveMs >= QuickEndMs) { _quickEnds = 0; return; }
        if (++_quickEnds < 3) return;
        _quickEnds = 0;
        _noRetryUntil = now + 5 * 60_000;
        Log.Write("openrgb server keeps ending right after it starts: not tried again for five minutes");
    }

    /// <summary>True while restarts are paused because the server keeps ending right after it starts.</summary>
    public static bool Paused => Environment.TickCount64 < _noRetryUntil;

    /// <summary>Starts the hidden server unless one already answers. Returns true when Shabpa started it just now. <paramref name="force"/> (the person pressed «start again») ends a pause.</summary>
    public static bool Start(int port = 6742, bool force = false)
    {
        lock (Gate)
        {
            if (force) { _noRetryUntil = 0; _quickEnds = 0; }
            if (!Installed || IsListening(port) || Paused) return false;
            if (!ExeIntact()) { Log.Write("openrgb: the program file is not the one that was unpacked; it is not started"); return false; }
            if (_process is { HasExited: false }) return false;                 // started a moment ago, still detecting hardware
            try
            {
                var info = new ProcessStartInfo(ExePath, Arguments(port))
                {
                    UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Folder,
                };
                _process = Process.Start(info);
                if (_process is { } started)
                {
                    started.EnableRaisingEvents = true;
                    var since = Environment.TickCount64;
                    started.Exited += (_, _) =>
                    {
                        try
                        {
                            if (Environment.TickCount64 - _lastStop < 4000) return;      // Shabpa ended it (the lights were switched off), not a failure
                            LastExit = started.ExitCode;
                            Log.Write($"openrgb server ended by itself, exit code {started.ExitCode}");
                            NoteEnded(Environment.TickCount64 - since, Environment.TickCount64);
                        }
                        catch (InvalidOperationException) { /* disposed by Stop */ }
                    };
                }
                Log.Write("openrgb server started (hidden, local only)");
                return _process is not null;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
            {
                Log.Write($"openrgb start: {ex.GetType().Name}");
                return false;
            }
        }
    }

    /// <summary>Ends the server Shabpa started (and any left over from a crash of Shabpa), never one the person runs from somewhere else.</summary>
    private static long _lastStop;

    public static void Stop()
    {
        lock (Gate)
        {
            _lastStop = Environment.TickCount64;
            try { if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            _process?.Dispose();
            _process = null;
            foreach (var p in Process.GetProcessesByName("OpenRGB"))
            {
                using (p)
                {
                    try { if (string.Equals(p.MainModule?.FileName, ExePath, StringComparison.OrdinalIgnoreCase)) p.Kill(); }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
                }
            }
        }
    }
}
