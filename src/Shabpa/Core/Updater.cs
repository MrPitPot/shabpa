using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Shabpa.Core;

public sealed record UpdateInfo(Version Version, string Notes, string DownloadUrl, string? Sha256, long Size, DateTime Published);

public enum UpdateState { NotConfigured, Idle, Checking, UpToDate, Available, Downloading, Installing, Error }

/// <summary>Checks a GitHub repository's latest release (or a plain JSON manifest), downloads the
/// installer, verifies it and runs it silently. The installer closes this app, replaces it and starts it again.</summary>
public sealed class Updater
{
    /// <summary>Where releases are published: "owner/repo" on GitHub, or an https:// URL to a manifest
    /// like {"version":"2.1.0","url":"https://…/ShabpaSetup.exe","sha256":"…","notes":"…"}.</summary>
    public const string DefaultFeed = "MrPitPot/shabpa";

    public const string SetupName = "ShabpaSetup.exe";
    private const long MaxSetupBytes = 200L << 20;

    private readonly Func<Settings> _settings;
    private CancellationTokenSource? _cts;

    public UpdateState State { get; private set; }
    public UpdateInfo? Available { get; private set; }
    public string Error { get; private set; } = "";
    public double Progress { get; private set; }
    public DateTime? LastCheck { get; private set; }
    public event Action? Changed;

    public Updater(Func<Settings> settings)
    {
        _settings = settings;
        State = Feed.Length == 0 ? UpdateState.NotConfigured : UpdateState.Idle;
    }

    public static Version Current
    {
        get
        {
            var v = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    public string Feed
    {
        get
        {
            var feed = _settings().UpdateFeed.Trim();
            return feed.Length > 0 ? feed : DefaultFeed;
        }
    }

    private void Set(UpdateState state, string error = "")
    {
        State = state;
        Error = error;
        Changed?.Invoke();
    }

    /// <summary>"v2.1.0", "2.1", "Shabpa 2.1.0-beta" → 2.1.0; null if there is no version in it.</summary>
    public static Version? ParseVersion(string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, @"(\d+)\.(\d+)(?:\.(\d+))?");
        if (!match.Success) return null;
        return new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value),
            match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0);
    }

    /// <summary>Reads a GitHub "latest release" response. Null if it has no installer attached.</summary>
    public static UpdateInfo? ParseRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) return null;
        if (root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()) return null;
        var version = ParseVersion(root.GetProperty("tag_name").GetString() ?? "");
        if (version is null || !root.TryGetProperty("assets", out var assets)) return null;

        foreach (var asset in assets.EnumerateArray())
        {
            if (!string.Equals(asset.GetProperty("name").GetString(), SetupName, StringComparison.OrdinalIgnoreCase)) continue;
            string? sha = null;
            // GitHub publishes a SHA-256 for every release asset.
            if (asset.TryGetProperty("digest", out var digest) && digest.GetString() is { } d && d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                sha = d[7..].ToLowerInvariant();
            return new UpdateInfo(version,
                root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
                asset.GetProperty("browser_download_url").GetString() ?? "",
                sha,
                asset.TryGetProperty("size", out var size) ? size.GetInt64() : 0,
                root.TryGetProperty("published_at", out var published) && published.TryGetDateTime(out var at) ? at.ToLocalTime() : DateTime.Now);
        }
        return null;
    }

    public static UpdateInfo? ParseManifest(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var version = ParseVersion(root.GetProperty("version").GetString() ?? "");
        var url = root.GetProperty("url").GetString() ?? "";
        if (version is null || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return null;
        return new UpdateInfo(version,
            root.TryGetProperty("notes", out var notes) ? notes.GetString() ?? "" : "",
            url,
            root.TryGetProperty("sha256", out var sha) ? sha.GetString()?.ToLowerInvariant() : null,
            root.TryGetProperty("size", out var size) ? size.GetInt64() : 0,
            DateTime.Now);
    }

    private HttpClient Http()
    {
        var handler = new HttpClientHandler();
        var proxy = _settings().ProxyUrl.Trim();
        if (proxy.Length > 0)
        {
            try { handler.Proxy = new WebProxy(proxy); handler.UseProxy = true; }
            catch { /* invalid proxy: use the system one */ }
        }
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Shabpa/{Current}");
        return http;
    }

    /// <summary>Returns the newer release, or null when this version is current (or the check failed).</summary>
    public async Task<UpdateInfo?> CheckAsync()
    {
        var feed = Feed;
        if (feed.Length == 0)
        {
            Set(UpdateState.NotConfigured);
            return null;
        }
        if (State is UpdateState.Checking or UpdateState.Downloading or UpdateState.Installing) return Available;
        Set(UpdateState.Checking);
        try
        {
            using var http = Http();
            UpdateInfo? info;
            if (feed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                info = ParseManifest(await http.GetStringAsync(feed));
            }
            else
            {
                http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                using var response = await http.GetAsync($"https://api.github.com/repos/{feed}/releases/latest");
                // 404 = the repository has no published release yet.
                info = response.StatusCode == HttpStatusCode.NotFound ? null : ParseRelease(await response.EnsureSuccessStatusCode().Content.ReadAsStringAsync());
            }
            LastCheck = DateTime.Now;
            Available = info is not null && info.Version > Current ? info : null;
            Set(Available is null ? UpdateState.UpToDate : UpdateState.Available);
            return Available;
        }
        catch (Exception ex)
        {
            Log.Write($"update check: {ex.Message}");
            Set(UpdateState.Error, L.T("به سرور به‌روزرسانی دسترسی نیست. اینترنت یا پروکسی رو چک کن.", "The update server is unreachable. Check the internet or proxy."));
            return null;
        }
    }

    /// <summary>Downloads and verifies the installer, then starts it. Returns an error message, or null when
    /// the installer is running (it will close this app).</summary>
    public async Task<string?> DownloadAndInstallAsync(bool elevated)
    {
        if (Available is not { } info) return L.T("نسخه‌ی جدیدی پیدا نشده.", "No newer version was found.");
        _cts = new CancellationTokenSource();
        Progress = 0;
        Set(UpdateState.Downloading);
        string file;
        try
        {
            file = await Download(info, _cts.Token);
        }
        catch (Exception ex)
        {
            var message = ex is InvalidDataException ? ex.Message : L.T("دانلود انجام نشد.", "The download failed.");
            Log.Write($"update download: {ex.Message}");
            Set(UpdateState.Error, message);
            return message;
        }

        Set(UpdateState.Installing);
        try
        {
            // --update keeps the user's shortcut and start-up choices; --launch starts the new version.
            using var process = Process.Start(new ProcessStartInfo(file, "--quiet --update --launch")
            {
                UseShellExecute = true,
                Verb = elevated ? "" : "runas",
            });
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            var message = L.T("نصب به تأیید ادمین نیاز داشت و انجام نشد.", "Installing needed administrator approval and was cancelled.");
            Set(UpdateState.Available, message);
            return message;
        }
    }

    private async Task<string> Download(UpdateInfo info, CancellationToken ct)
    {
        var folder = Path.Combine(Path.GetTempPath(), "Shabpa-update");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, $"ShabpaSetup-{info.Version}.exe");

        using var http = Http();
        using var response = await http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? info.Size;
        if (total > MaxSetupBytes) throw new InvalidDataException(L.T("فایل به‌روزرسانی بیش از حد بزرگه.", "The update file is unexpectedly large."));

        await using (var input = await response.Content.ReadAsStreamAsync(ct))
        await using (var output = File.Create(file))
        {
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (done > MaxSetupBytes) throw new InvalidDataException(L.T("فایل به‌روزرسانی بیش از حد بزرگه.", "The update file is unexpectedly large."));
                if (total > 0)
                {
                    Progress = Math.Min(1, (double)done / total);
                    Changed?.Invoke();
                }
            }
        }

        Verify(file, info.Sha256);
        return file;
    }

    /// <summary>The file must match the published checksum and actually be a Shabpa installer.</summary>
    internal static void Verify(string file, string? sha256)
    {
        if (sha256 is { Length: > 0 })
        {
            string actual;
            using (var stream = File.OpenRead(file))
                actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (actual != sha256)
            {
                File.Delete(file);
                throw new InvalidDataException(L.T("فایل دانلودشده با نسخه‌ی منتشرشده یکی نیست و پاک شد.", "The downloaded file does not match the published one and was deleted."));
            }
        }
        var product = FileVersionInfo.GetVersionInfo(file).ProductName;
        if (product != "Shabpa Setup")
        {
            File.Delete(file);
            throw new InvalidDataException(L.T("فایل دانلودشده نصب‌کننده‌ی شب‌پا نیست و پاک شد.", "The downloaded file is not a Shabpa installer and was deleted."));
        }
    }
}
