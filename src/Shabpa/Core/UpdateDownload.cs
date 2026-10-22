using System.IO;
using System.Net;
using System.Net.Http;

namespace Shabpa.Core;

/// <summary>Resumable installer download with per-address fallback. The SHA-256 from the signed manifest decides
/// whether the bytes are accepted, so it does not matter which mirror delivered them.</summary>
public sealed class UpdateDownloader(HttpClient http, bool allowLocal = false)
{
    public TimeSpan HeaderTimeout { get; init; } = TimeSpan.FromSeconds(15);
    /// <summary>No bytes for this long = this address is stalled; the next one is tried (partial data is kept for resuming).</summary>
    public TimeSpan StallTimeout { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>Downloads into <paramref name="folder"/> and returns the verified file. Throws if every address fails.</summary>
    public async Task<string> DownloadAsync(UpdateManifest manifest, IReadOnlyList<string> urls, string folder, Action<double>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(folder);
        var final = Path.Combine(folder, $"ShabpaSetup-{manifest.Version.ToString(3)}.exe");
        var part = final + ".part";
        var errors = new List<string>();
        foreach (var url in urls)
        {
            ct.ThrowIfCancellationRequested();
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    await Fetch(new Uri(url), manifest, part, progress, ct);
                    var actual = UpdateManifestReader.Sha256Hex(part);
                    if (actual != manifest.Sha256)
                    {
                        File.Delete(part);
                        throw new UpdateRejectedException("sha", "The downloaded file does not match the signed checksum.");
                    }
                    File.Move(part, final, overwrite: true);
                    return final;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (RangeRestart) { continue; }
                catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or IOException or UpdateRejectedException or UriFormatException)
                {
                    errors.Add($"{url}: {(ex is UpdateRejectedException r ? r.Reason : ex.GetType().Name)}");
                    break;
                }
            }
        }
        throw new InvalidDataException("all download addresses failed: " + string.Join("; ", errors));
    }

    private sealed class RangeRestart : Exception;

    private async Task Fetch(Uri uri, UpdateManifest manifest, string part, Action<double>? progress, CancellationToken ct)
    {
        var limit = manifest.Size > 0 ? manifest.Size : UpdateManifestReader.MaxSetupBytes;
        long have = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (have > limit) { File.Delete(part); have = 0; }

        Stream input;
        HttpResponseMessage? response = null;
        long total = manifest.Size;
        if (uri.Scheme == Uri.UriSchemeFile)
        {
            if (!allowLocal) throw new UpdateRejectedException("scheme", "Local sources are not allowed.");
            var fs = File.OpenRead(uri.LocalPath);
            if (fs.Length > limit) { fs.Dispose(); throw new UpdateRejectedException("size", "The installer is larger than announced."); }
            fs.Seek(Math.Min(have, fs.Length), SeekOrigin.Begin);
            input = fs;
            total = fs.Length;
        }
        else
        {
            if (uri.Scheme != Uri.UriSchemeHttps) throw new UpdateRejectedException("scheme", "Downloads must use HTTPS.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (have > 0) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(have, null);
            using var headerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            headerCts.CancelAfter(HeaderTimeout);
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headerCts.Token);
            try
            {
                if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    File.Delete(part);
                    throw new RangeRestart();
                }
                response.EnsureSuccessStatusCode();
                if (response.RequestMessage?.RequestUri is { } fin && fin.Scheme != Uri.UriSchemeHttps)
                    throw new UpdateRejectedException("scheme", "The download redirected away from HTTPS.");
                if (response.StatusCode == HttpStatusCode.PartialContent)
                {
                    if (response.Content.Headers.ContentRange?.From != have) { File.Delete(part); throw new RangeRestart(); }
                }
                else if (have > 0)
                {
                    have = 0; // the server ignored Range: start over
                }
                var length = response.Content.Headers.ContentLength ?? 0;
                var expected = have + length;
                if ((length > 0 && expected > limit) || (manifest.Size > 0 && length > 0 && expected != manifest.Size))
                    throw new UpdateRejectedException("size", "The installer is not the announced size.");
                if (total == 0) total = expected;
                input = await response.Content.ReadAsStreamAsync(ct);
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }

        try
        {
            await using var output = new FileStream(part, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read);
            var buffer = new byte[81920];
            long done = have;
            while (true)
            {
                using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
                stall.CancelAfter(StallTimeout);
                var read = await input.ReadAsync(buffer, stall.Token);
                if (read == 0) break;
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (done > limit) throw new UpdateRejectedException("size", "The installer is larger than announced.");
                if (total > 0) progress?.Invoke(Math.Min(1, (double)done / total));
            }
            if (manifest.Size > 0 && done != manifest.Size)
                throw new IOException("incomplete download"); // the .part file stays so another address can resume it
        }
        finally
        {
            await input.DisposeAsync();
            response?.Dispose();
        }
    }
}
