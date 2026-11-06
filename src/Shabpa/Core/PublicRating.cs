using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Shabpa.Core;

public enum ShareOutcome { Declined, Sent, Failed }

/// <summary>Sends a star rating to shabpa.ir, but only when the person said yes to that one question. What leaves the PC is the number of stars, the app version, the interface
/// language and a random install id (a GUID made once and kept in Shabpa's data folder: not made from the hardware, the Windows account or the PC name), so one install has one vote
/// that it can change. The site stores only a hash of that id and no address.</summary>
public static class PublicRating
{
    /// <summary>The site's own address first, then its second address that does not use the domain name: on some networks one of the two is cut while the other works.</summary>
    public static readonly string[] Endpoints = ["https://shabpa.ir", "https://shabpa.amber-garden-174c.workers.dev"];

    private const string IdFile = "rating-id.txt";

    /// <summary>The install id: 32 lower-case hex characters, made on first use and kept in <paramref name="folder"/>.</summary>
    public static string InstallId(string folder)
    {
        var path = Path.Combine(folder, IdFile);
        try
        {
            if (File.Exists(path) && File.ReadAllText(path).Trim() is { Length: 32 } saved && saved.All(Uri.IsHexDigit)) return saved.ToLowerInvariant();
        }
        catch (IOException) { /* made again below */ }
        var id = Guid.NewGuid().ToString("N");
        try { Directory.CreateDirectory(folder); File.WriteAllText(path, id); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Write($"rating id: {ex.GetType().Name}"); }
        return id;
    }

    /// <summary>Exactly what is sent, and nothing else.</summary>
    public static string Payload(int stars, string id, string version, string lang) => JsonSerializer.Serialize(new { stars, id, version, lang });

    /// <summary>Does the whole thing: nothing at all happens unless <paramref name="agreed"/>; a failure never throws and never touches the rating kept on this PC.</summary>
    public static async Task<ShareOutcome> ShareAsync(bool agreed, int stars, string folder, string version, string lang, IReadOnlyList<string>? endpoints = null, string proxyUrl = "", HttpMessageHandler? handler = null, CancellationToken ct = default)
    {
        if (!agreed || stars is < 1 or > 5) return ShareOutcome.Declined;
        try
        {
            var body = Payload(stars, InstallId(folder), version, lang);
            using var http = new HttpClient(handler ?? GiftRedeemer.Real(proxyUrl), disposeHandler: handler is null) { Timeout = TimeSpan.FromSeconds(10) };
            foreach (var endpoint in endpoints ?? Endpoints)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.TrimEnd('/') + "/api/rate") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
                    using var response = await http.SendAsync(request, ct);
                    if (response.StatusCode == HttpStatusCode.OK) return ShareOutcome.Sent;
                    if ((int)response.StatusCode is >= 400 and < 500 && response.StatusCode != HttpStatusCode.TooManyRequests) return ShareOutcome.Failed;   // the server refused this vote: another address will not change that
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested) { /* try the next address */ }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { Log.Write($"rating: {ex.GetType().Name}"); }
        return ShareOutcome.Failed;
    }
}
