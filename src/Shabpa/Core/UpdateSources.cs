using System.IO;
using System.Net;
using System.Net.Http;

namespace Shabpa.Core;

/// <summary>One place that can serve latest.json (+ latest.json.sig) and the installer.</summary>
/// <param name="Legacy">GitHub's release API (no signed manifest); only accepted for api.github.com over HTTPS.</param>
public sealed record UpdateSource(string Id, string Name, string ManifestUrl, bool Legacy = false);

public sealed record SourceFailure(string SourceId, string Reason);

/// <summary>The outcome of walking the source list: a newer manifest, "up to date", or nothing reachable.</summary>
public sealed record UpdateCheckResult(UpdateManifest? Manifest, UpdateSource? Source, bool UpToDate, IReadOnlyList<SourceFailure> Failures)
{
    public bool AllFailed => Manifest is null && !UpToDate;
}

/// <summary>Ordered mirror list + selection. Pure: all network goes through the HttpClient it is given
/// (tests pass a fake handler), nothing here touches the UI thread.</summary>
public sealed class UpdateSources(HttpClient http, string publicKey, bool allowLocal = false)
{
    public const string GitHubId = "github", SiteId = "shabpa.ir", DomesticId = "dl.shabpa.ir", CustomId = "custom", LegacyId = "github-api";
    public const string LegacyApi = "https://api.github.com/repos/" + Updater.DefaultFeed + "/releases/latest";

    /// <summary>Short timeout for the manifest of each source (so a dead one costs seconds, not minutes).</summary>
    public TimeSpan ManifestTimeout { get; init; } = TimeSpan.FromSeconds(6);

    /// <summary>The built-in sources in order, then the user's extra mirror; the last one that worked goes first.</summary>
    public static IReadOnlyList<UpdateSource> Build(string extraMirror, string lastWorkingId, bool allowLocal = false)
    {
        var list = new List<UpdateSource>
        {
            new(GitHubId, "GitHub", "https://github.com/" + Updater.DefaultFeed + "/releases/latest/download/latest.json"),
            new(SiteId, "shabpa.ir", "https://shabpa.ir/update/latest.json"),
            new(DomesticId, "dl.shabpa.ir", "https://dl.shabpa.ir/update/latest.json"),
        };
        if (NormalizeMirror(extraMirror, allowLocal) is { } custom) list.Add(new UpdateSource(CustomId, custom, custom));
        var last = list.FirstOrDefault(s => s.Id == lastWorkingId);
        if (last is not null) { list.Remove(last); list.Insert(0, last); }
        list.Add(new UpdateSource(LegacyId, "GitHub API", LegacyApi, Legacy: true));
        return list;
    }

    /// <summary>"https://mirror.ir/shabpa" or ".../latest.json" gives the manifest address; null if blank or not https.</summary>
    public static string? NormalizeMirror(string text, bool allowLocal = false)
    {
        text = text.Trim();
        if (text.Length == 0 || !Uri.TryCreate(text, UriKind.Absolute, out var uri)) return null;
        var ok = uri.Scheme == Uri.UriSchemeHttps || (allowLocal && uri.Scheme == Uri.UriSchemeFile);
        if (!ok) return null;
        var s = uri.AbsoluteUri;
        if (!uri.AbsolutePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            s = s.TrimEnd('/') + "/latest.json";
        return s;
    }

    public async Task<UpdateCheckResult> CheckAsync(IReadOnlyList<UpdateSource> sources, Version current, CancellationToken ct = default)
    {
        var failures = new List<SourceFailure>();
        foreach (var source in sources)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(ManifestTimeout);
                var manifest = source.Legacy ? await FetchLegacy(source, timeout.Token) : await FetchSigned(source, timeout.Token);
                if (manifest.Version < current) { failures.Add(new(source.Id, "stale")); continue; }
                if (manifest.Version == current) return new(null, source, true, failures);
                if (manifest.MinApp > current) { failures.Add(new(source.Id, "min-app")); continue; }
                return new(manifest, source, false, failures);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                failures.Add(new(source.Id, "timeout"));
            }
            catch (UpdateRejectedException ex)
            {
                failures.Add(new(source.Id, ex.Reason));
            }
            catch (HttpRequestException ex)
            {
                failures.Add(new(source.Id, ex.StatusCode is { } code ? ((int)code).ToString() : "network"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UriFormatException)
            {
                failures.Add(new(source.Id, "io"));
            }
        }
        return new(null, null, false, failures);
    }

    private async Task<UpdateManifest> FetchSigned(UpdateSource source, CancellationToken ct)
    {
        var url = UpdateManifestReader.Resolve(source.ManifestUrl, null, allowLocal)
            ?? throw new UpdateRejectedException("scheme", "The mirror address must be HTTPS.");
        var baseUri = new Uri(url);
        var body = GetBytes(baseUri, UpdateManifestReader.MaxManifestBytes, ct);
        var sig = GetBytes(new Uri(url + ".sig"), 4096, ct);
        await Task.WhenAll(body, sig);
        return UpdateManifestReader.ReadSigned(body.Result, System.Text.Encoding.ASCII.GetString(sig.Result), publicKey, baseUri, allowLocal);
    }

    private async Task<UpdateManifest> FetchLegacy(UpdateSource source, CancellationToken ct)
    {
        var uri = new Uri(source.ManifestUrl);
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase))
            throw new UpdateRejectedException("scheme", "Only GitHub's own API is accepted without a signature.");
        var bytes = await GetBytes(uri, 1 << 20, ct);
        UpdateInfo? info;
        try { info = Updater.ParseRelease(System.Text.Encoding.UTF8.GetString(bytes)); }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new UpdateRejectedException("parse", "The GitHub response is malformed.");
        }
        if (info is null || info.Sha256 is not { Length: 64 }) throw new UpdateRejectedException("parse", "GitHub has no installer with a checksum.");
        if (!Uri.TryCreate(info.DownloadUrl, UriKind.Absolute, out var dl) || dl.Scheme != Uri.UriSchemeHttps
            || !(dl.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || dl.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
            throw new UpdateRejectedException("scheme", "The GitHub installer address is not a GitHub address.");
        return new UpdateManifest(info.Version, new Version(0, 0, 0), info.Published, info.Sha256, info.Size, [info.DownloadUrl], info.Notes, info.Notes);
    }

    /// <summary>Reads at most <paramref name="max"/> bytes; HTTP errors throw, redirects must stay on HTTPS.</summary>
    private async Task<byte[]> GetBytes(Uri uri, int max, CancellationToken ct)
    {
        if (uri.Scheme == Uri.UriSchemeFile)
        {
            if (!allowLocal) throw new UpdateRejectedException("scheme", "Local sources are not allowed.");
            var info = new FileInfo(uri.LocalPath);
            if (!info.Exists) throw new HttpRequestException("missing", null, HttpStatusCode.NotFound);
            if (info.Length > max) throw new UpdateRejectedException("size", "The file is too large.");
            return await File.ReadAllBytesAsync(uri.LocalPath, ct);
        }
        if (uri.Scheme != Uri.UriSchemeHttps) throw new UpdateRejectedException("scheme", "The mirror address must be HTTPS.");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.ParseAdd(uri.Host == "api.github.com" ? "application/vnd.github+json" : "application/json, */*");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri is { } final && final.Scheme != Uri.UriSchemeHttps)
            throw new UpdateRejectedException("scheme", "The mirror redirected away from HTTPS.");
        if (response.Content.Headers.ContentLength > max) throw new UpdateRejectedException("size", "The response is too large.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > max) throw new UpdateRejectedException("size", "The response is too large.");
        }
        return buffer.ToArray();
    }

    /// <summary>Installer addresses to try: the ones on the same host as the source that served the manifest first.</summary>
    public static IReadOnlyList<string> OrderUrls(UpdateManifest manifest, UpdateSource? source)
    {
        var host = source is not null && Uri.TryCreate(source.ManifestUrl, UriKind.Absolute, out var m) ? m.Host : "";
        return manifest.Urls
            .OrderBy(u => Uri.TryCreate(u, UriKind.Absolute, out var x) && x.Host.Equals(host, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ToList();
    }
}
