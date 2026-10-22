using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Shabpa.Core;

public sealed record UpdateInfo(Version Version, string Notes, string DownloadUrl, string? Sha256, long Size, DateTime Published)
{
    /// <summary>The verified manifest this came from (all installer addresses) and the source that served it.</summary>
    public UpdateManifest? Manifest { get; init; }
    public UpdateSource? Source { get; init; }
}

public enum UpdateState { NotConfigured, Idle, Checking, UpToDate, Available, Downloading, Installing, Error }

/// <summary>Checks a GitHub repository's latest release (or a plain JSON manifest), downloads the
/// installer, verifies it and runs it silently. The installer closes this app, replaces it and starts it again.</summary>
public sealed class Updater
{
    /// <summary>Where releases are published: "owner/repo" on GitHub, or an https:// URL to a manifest
    /// like {"version":"2.1.0","url":"https://…/ShabpaSetup.exe","sha256":"…","notes":"…"}.</summary>
    public const string DefaultFeed = "MrPitPot/shabpa";

    /// <summary>The page people can open to see every release.</summary>
    public const string ReleasesPage = "https://github.com/" + DefaultFeed + "/releases";

    public const string SetupName = "ShabpaSetup.exe";

    /// <summary>The project site; reachable from inside Iran even when GitHub is not.</summary>
    public const string DownloadPage = "https://shabpa.ir/";

    private readonly Func<Settings> _settings;
    private readonly Func<HttpClient>? _http;
    private readonly string _key;
    private CancellationTokenSource? _cts;

    public UpdateState State { get; private set; }
    public UpdateInfo? Available { get; private set; }
    public string Error { get; private set; } = "";
    public double Progress { get; private set; }
    public DateTime? LastCheck { get; private set; }
    public event Action? Changed;

    public Updater(Func<Settings> settings, Func<HttpClient>? http = null, string? publicKey = null)
    {
        _settings = settings;
        _http = http;
        _key = publicKey ?? UpdateKeys.PublicKeyBase64;
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

    /// <summary>Built in, so nobody has to find and paste a link.</summary>
    public string Feed => DefaultFeed;

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
        if (_http is not null) return _http();
        var handler = new HttpClientHandler(); // follows the system proxy by default
        var proxy = _settings().ProxyUrl.Trim();
        if (proxy.Length > 0)
        {
            try { handler.Proxy = new WebProxy(proxy); handler.UseProxy = true; }
            catch { /* invalid proxy: use the system one */ }
        }
        // Per-step timeouts live in UpdateSources / UpdateDownloader; the client itself never cuts a download short.
        var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Shabpa/{Current}");
        return http;
    }

    /// <summary>True when the last check or download reached none of the sources (show the manual options).</summary>
    public bool AllSourcesFailed { get; private set; }

    /// <summary>Name of the source that answered last time.</summary>
    public string LastSourceName { get; private set; } = "";

    /// <summary>Called after the working source changes so the choice is saved.</summary>
    public Action? Persist { get; set; }

    /// <summary>Returns the newer release, or null when this version is current (or every source failed).</summary>
    public async Task<UpdateInfo?> CheckAsync()
    {
        if (State is UpdateState.Checking or UpdateState.Downloading or UpdateState.Installing) return Available;
        Set(UpdateState.Checking);
        AllSourcesFailed = false;
        try
        {
            var s = _settings();
            using var http = Http();
            var sources = UpdateSources.Build(s.ExtraMirrorUrl, s.LastUpdateSource);
            var result = await new UpdateSources(http, _key).CheckAsync(sources, Current);
            foreach (var f in result.Failures) Log.Write($"update source {f.SourceId}: {f.Reason}");
            if (result.AllFailed)
            {
                AllSourcesFailed = true;
                Available = null;
                Set(UpdateState.Error, L.T("به هیچ‌کدوم از سرورهای به‌روزرسانی دسترسی نیست. می‌تونی فایل نصب رو دستی دانلود یا از فایل نصب کنی.",
                    "None of the update servers can be reached. You can download the installer manually or install from a file."));
                return null;
            }
            LastCheck = DateTime.Now;
            if (result.Source is { Legacy: false } ok && s.LastUpdateSource != ok.Id)
            {
                s.LastUpdateSource = ok.Id;
                Persist?.Invoke();
            }
            LastSourceName = result.Source?.Name ?? "";
            Available = result.Manifest is { } m ? ToInfo(m, result.Source) : null;
            Set(Available is null ? UpdateState.UpToDate : UpdateState.Available);
            return Available;
        }
        catch (Exception ex)
        {
            Log.Write($"update check: {ex.Message}");
            AllSourcesFailed = true;
            Set(UpdateState.Error, L.T("به سرور به‌روزرسانی دسترسی نیست. اینترنت یا پروکسی رو چک کن.", "The update server is unreachable. Check the internet or proxy."));
            return null;
        }
    }

    private static UpdateInfo ToInfo(UpdateManifest m, UpdateSource? source) =>
        new(m.Version, m.Notes, m.Urls.FirstOrDefault() ?? "", m.Sha256, m.Size, m.Published) { Manifest = m, Source = source };

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
            AllSourcesFailed = true;
            Set(UpdateState.Error, message);
            return message;
        }
        return Launch(file, info.Sha256, elevated, UpdateState.Available);
    }

    /// <summary>Installs a file the person picked: it must be a Shabpa installer and match <paramref name="sha256"/>
    /// (from a signed manifest, or typed in). The file is never deleted on a mismatch.</summary>
    public string? InstallLocal(string file, string sha256, bool elevated)
    {
        try
        {
            if (InstallerVersion(file) is { } v && v <= Current)
                return L.T("این نصب‌کننده از نسخه‌ی نصب‌شده جدیدتر نیست و اجرا نشد.", "This installer is not newer than the installed version and was not run.");
            if (!IsShabpaInstaller(file) || !string.Equals(UpdateManifestReader.Sha256Hex(file), sha256, StringComparison.OrdinalIgnoreCase))
                return L.T("این فایل نصب‌کننده‌ی شب‌پا با چک‌سام داده‌شده نیست و اجرا نشد.", "This file is not the Shabpa installer with that checksum and was not run.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return L.T("فایل خونده نشد.", "The file could not be read.");
        }
        return Launch(file, sha256.ToLowerInvariant(), elevated, Available is null ? UpdateState.Idle : UpdateState.Available);
    }

    private string? Launch(string file, string? sha256, bool elevated, UpdateState onCancel)
    {
        Set(UpdateState.Installing);
        FileStream hold;
        try
        {
            // Keep the file open (nobody else may write or replace it) and check it once more right before it runs;
            // the temp folder is writable by any program of this user and the installer runs elevated.
            hold = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var check = SHA256.Create();
            var actual = Convert.ToHexString(check.ComputeHash(hold)).ToLowerInvariant();
            if (sha256 is not { Length: > 0 } expected || actual != expected)
            {
                hold.Dispose();
                Set(UpdateState.Error, L.T("فایل دانلودشده تغییر کرده بود و اجرا نشد.", "The downloaded file had changed and was not run."));
                return Error;
            }
        }
        catch (IOException)
        {
            Set(UpdateState.Error, L.T("فایل به‌روزرسانی در دسترس نیست.", "The update file is not available."));
            return Error;
        }
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
            hold.Dispose();
            var message = L.T("نصب به تأیید ادمین نیاز داشت و انجام نشد.", "Installing needed administrator approval and was cancelled.");
            Set(onCancel, message);
            return message;
        }
    }

    private async Task<string> Download(UpdateInfo info, CancellationToken ct)
    {
        var folder = Path.Combine(Path.GetTempPath(), "Shabpa-update");
        var manifest = info.Manifest ?? throw new InvalidDataException(L.T("اطلاعات به‌روزرسانی کامل نیست.", "The update details are incomplete."));
        using var http = Http();
        var urls = UpdateSources.OrderUrls(manifest, info.Source);
        string file;
        try
        {
            file = await new UpdateDownloader(http).DownloadAsync(manifest, urls, folder, p => { Progress = p; Changed?.Invoke(); }, ct);
        }
        catch (InvalidDataException)
        {
            throw new InvalidDataException(L.T("دانلود از هیچ‌کدوم از سرورها کامل نشد یا فایل با نسخه‌ی منتشرشده یکی نبود.",
                "The download did not complete from any server, or the file did not match the published one."));
        }
        Verify(file, manifest.Sha256);
        return file;
    }

    internal static Version? InstallerVersion(string file) => ParseVersion(FileVersionInfo.GetVersionInfo(file).FileVersion ?? "");

    internal static bool IsShabpaInstaller(string file) => FileVersionInfo.GetVersionInfo(file).ProductName == "Shabpa Setup";

    /// <summary>The file must match the published checksum and actually be a Shabpa installer.</summary>
    internal static void Verify(string file, string? sha256)
    {
        // No published checksum, no install: an unverifiable file is never run (it would run elevated).
        if (sha256 is not { Length: > 0 })
        {
            File.Delete(file);
            throw new InvalidDataException(L.T("برای این نسخه چک‌سام منتشر نشده و نصب نشد.", "No checksum was published for this version; it was not installed."));
        }
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
