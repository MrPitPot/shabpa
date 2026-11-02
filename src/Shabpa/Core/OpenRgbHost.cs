using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Shabpa.Core;

/// <summary>The one release of the free, open-source OpenRGB that Shabpa will fetch: the official file and its SHA-256, both fixed in the program.</summary>
public sealed record OpenRgbRelease(string Version, string Url, string Sha256);

/// <summary>Makes lighting work with one click: Shabpa fetches the official OpenRGB once (22 MB, checked against a fixed SHA-256, nothing else is accepted),
/// keeps it in its own folder, starts it hidden as a local-only lighting server (127.0.0.1, its own config) when the lights are on and stops it with them.
/// The person never installs, opens or configures OpenRGB. Nothing is sent anywhere: the only network call is the download of that one file.</summary>
public static class OpenRgbHost
{
    /// <summary>OpenRGB 1.1 for Windows 64-bit, from the project's official release page. The hash was computed from that file; a different file is refused.</summary>
    public static readonly OpenRgbRelease Release = new("1.1",
        "https://codeberg.org/OpenRGB/OpenRGB/releases/download/release_1.1/OpenRGB_1.1_Windows_64_8e16de2.zip",
        "63291726c6fc84eb479ef2ee752e97e1187f6ce9c49fd27eee40ded588a8172a");

    /// <summary>Where it lives (a folder of Shabpa's own; tests point it elsewhere).</summary>
    public static string Folder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Shabpa", "openrgb");

    public static string ExePath => Path.Combine(Folder, "OpenRGB.exe");

    private static string Marker => Path.Combine(Folder, "shabpa-openrgb.txt");

    /// <summary>True when the checked release is unpacked and complete.</summary>
    public static bool Installed
    {
        get
        {
            try { return File.Exists(ExePath) && File.Exists(Marker) && File.ReadAllText(Marker).Trim() == Release.Sha256; }
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

            string hash;
            try
            {
                using var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
                http.Timeout = TimeSpan.FromMinutes(10);
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Shabpa");
                using var response = await http.GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return new(false, "download");
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
                hash = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                if (ct.IsCancellationRequested) throw;
                Log.Write($"openrgb download: {ex.GetType().Name}");
                return new(false, "download");
            }

            if (!hash.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                Log.Write("openrgb download refused: wrong SHA-256");
                return new(false, "hash");
            }

            Unpack(temp, staging);
            if (!File.Exists(Path.Combine(staging, "OpenRGB.exe"))) return new(false, "unpack");
            File.WriteAllText(Path.Combine(staging, "shabpa-openrgb.txt"), release.Sha256);
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

    /// <summary>True when something already answers on the lighting port of this PC (ours, or one the person runs themselves).</summary>
    public static bool IsListening(int port, int timeoutMs = 200)
    {
        try
        {
            using var tcp = new TcpClient();
            return tcp.ConnectAsync("127.0.0.1", port).Wait(timeoutMs) && tcp.Connected;
        }
        catch (Exception ex) when (ex is SocketException or AggregateException) { return false; }
    }

    /// <summary>Starts the hidden server unless one already answers. Returns true when Shabpa started it just now.</summary>
    public static bool Start(int port = 6742)
    {
        lock (Gate)
        {
            if (!Installed || IsListening(port)) return false;
            if (_process is { HasExited: false }) return false;                 // started a moment ago, still detecting hardware
            try
            {
                var info = new ProcessStartInfo(ExePath, Arguments(port))
                {
                    UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Folder,
                };
                _process = Process.Start(info);
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
    public static void Stop()
    {
        lock (Gate)
        {
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
