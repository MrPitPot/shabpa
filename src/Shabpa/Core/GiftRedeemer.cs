using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Shabpa.Core;

public enum GiftAnswer { Ok, Used, Unknown, Offline }

/// <summary>Asks Shabpa's server (the same Cloudflare worker as the Telegram link; no account or pairing needed) to use up a one-time gift code. The server keeps the list of codes that
/// were already used, so a code works once in the whole world, not once per copy. With no answer from the server nothing is paid: the gift is never given on trust.</summary>
public static class GiftRedeemer
{
    /// <summary>Only the SHA-256 of the code is sent, never the code itself.</summary>
    public static string HashOf(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim()))).ToLowerInvariant();

    public static async Task<GiftAnswer> RedeemAsync(string code, IReadOnlyList<string> endpoints, string proxyUrl = "", HttpMessageHandler? handler = null, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new { hash = HashOf(code) });
        HttpMessageHandler use = handler ?? Real(proxyUrl);
        try
        {
            using var http = new HttpClient(use, disposeHandler: handler is null) { Timeout = TimeSpan.FromSeconds(15) };
            foreach (var endpoint in endpoints)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.TrimEnd('/') + "/api/gift/redeem") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
                    using var response = await http.SendAsync(request, ct);
                    switch (response.StatusCode)
                    {
                        case HttpStatusCode.OK: return GiftAnswer.Ok;
                        case HttpStatusCode.Conflict: return GiftAnswer.Used;
                        case HttpStatusCode.NotFound: return GiftAnswer.Unknown;
                        default: continue;                          // rate limit or a server fault: the other address may answer
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested) { /* try the next address */ }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { Log.Write($"gift code: {ex.GetType().Name}"); }
        return GiftAnswer.Offline;
    }

    private static HttpMessageHandler Real(string proxyUrl)
    {
        var real = new HttpClientHandler();
        if (!string.IsNullOrWhiteSpace(proxyUrl))
        {
            try { real.Proxy = new WebProxy(proxyUrl.Trim()); real.UseProxy = true; }
            catch (Exception) { /* a bad proxy address: go direct */ }
        }
        return real;
    }
}
