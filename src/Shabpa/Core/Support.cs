using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shabpa.Core;

/// <summary>Support settings kept on this PC. The device id is random and only ties tickets to this installation.</summary>
public sealed class SupportSettings
{
    public string DeviceId { get; set; } = "";
    /// <summary>Overrides <see cref="SupportClient.DefaultEndpoint"/>; the owner can paste a server address here.</summary>
    public string Endpoint { get; set; } = "";
    public string Contact { get; set; } = "";
    /// <summary>The owner's admin key, protected for this Windows user. Empty for everyone else.</summary>
    public string AdminKeyProtected { get; set; } = "";
}

/// <summary>A ticket the user sent from this PC (kept so GitHub tickets, which have no server list, still show up).</summary>
public sealed class TicketRecord
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime Created { get; set; }
    /// <summary>"server" or "github".</summary>
    public string Via { get; set; } = "server";
}

public sealed class TicketReply
{
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("at")] public DateTime At { get; set; }
    [JsonPropertyName("admin")] public bool Admin { get; set; }
}

public sealed class TicketInfo
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("body")] public string Body { get; set; } = "";
    [JsonPropertyName("category")] public string Category { get; set; } = "other";
    [JsonPropertyName("status")] public string Status { get; set; } = "open";
    [JsonPropertyName("created")] public DateTime Created { get; set; }
    [JsonPropertyName("contact")] public string Contact { get; set; } = "";
    [JsonPropertyName("diag")] public string Diag { get; set; } = "";
    [JsonPropertyName("app")] public string App { get; set; } = "";
    [JsonPropertyName("replies")] public List<TicketReply> Replies { get; set; } = [];
}

public static class SupportInfo
{
    public static readonly string[] Categories = ["bug", "idea", "question", "other"];

    public static string CategoryName(string key) => key switch
    {
        "bug" => L.T("مشکل / باگ", "Problem / bug"),
        "idea" => L.T("پیشنهاد", "Idea"),
        "question" => L.T("سؤال", "Question"),
        _ => L.T("سایر", "Other"),
    };

    public static string StatusName(string key) => key switch
    {
        "reviewing" => L.T("در حال بررسی", "Being looked at"),
        "answered" => L.T("جواب داده شد", "Answered"),
        "closed" => L.T("بسته شد", "Closed"),
        _ => L.T("باز", "Open"),
    };

    /// <summary>Technical details that help to fix a problem. No name, no files, no network addresses, nothing from other programs.</summary>
    public static string Diagnostics(bool elevated) =>
        string.Join("\n",
        [
            $"Shabpa {Updater.Current.ToString(3)}",
            $"Windows {Environment.OSVersion.Version}",
            $"64-bit: {Environment.Is64BitOperatingSystem}",
            $"Administrator: {elevated}",
            $"Language: {(L.En ? "en" : "fa")}",
            $"CPU threads: {Environment.ProcessorCount}",
        ]);
}

/// <summary>Opens a pre-filled new-issue page on the project's GitHub. Nothing is sent by Shabpa itself; the person
/// reads the page in the browser and presses the button there.</summary>
public static class GitHubIssue
{
    public const string Repo = "MrPitPot/shabpa";
    public const string IssuesUrl = "https://github.com/" + Repo + "/issues";

    public static string NewIssueUrl(string title, string body, string category)
    {
        // Long text in a link is cut off by browsers and servers; the page keeps the rest for the person to paste.
        if (body.Length > 4500) body = body[..4500] + "\n…";
        var label = category switch { "bug" => "bug", "idea" => "enhancement", "question" => "question", _ => "" };
        var url = $"https://github.com/{Repo}/issues/new?title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}";
        return label.Length > 0 ? url + "&labels=" + label : url;
    }
}

/// <summary>Talks to the owner's support server (see server/support-worker). Disabled until an address is set.</summary>
public sealed class SupportClient
{
    /// <summary>The address of the owner's server; empty until it is deployed and this constant (or the setting) is filled in.</summary>
    public const string DefaultEndpoint = "";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly Func<SupportSettings> _settings;
    private readonly Func<string> _proxy;

    public SupportClient(Func<SupportSettings> settings, Func<string> proxy)
    {
        _settings = settings;
        _proxy = proxy;
    }

    public string Endpoint
    {
        get
        {
            var set = _settings().Endpoint.Trim();
            return (set.Length > 0 ? set : DefaultEndpoint).TrimEnd('/');
        }
    }

    /// <summary>Only an https address counts; plain http would send tickets in the clear.</summary>
    public static bool IsValidEndpoint(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host.Length > 0;

    public bool Available => IsValidEndpoint(Endpoint);

    public string AdminKey => TelegramBot.Unprotect(_settings().AdminKeyProtected);
    public bool IsAdmin => AdminKey.Length > 0;

    private HttpClient Http()
    {
        var handler = new HttpClientHandler();
        var proxy = _proxy().Trim();
        if (proxy.Length > 0)
        {
            try { handler.Proxy = new WebProxy(proxy); handler.UseProxy = true; }
            catch { /* invalid proxy: use the system one */ }
        }
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Shabpa");
        return http;
    }

    private sealed record Created([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("status")] string Status);
    private sealed record Listing([property: JsonPropertyName("tickets")] List<TicketInfo> Tickets);

    private static string Describe(HttpStatusCode code) => code switch
    {
        HttpStatusCode.TooManyRequests => L.T("تعداد تیکت‌های این ساعت به سقف رسید؛ بعداً دوباره امتحان کن.", "Too many tickets this hour; try again later."),
        HttpStatusCode.Forbidden => L.T("کلید مدیر درست نیست.", "The admin key is not right."),
        HttpStatusCode.BadRequest => L.T("سرور تیکت رو قبول نکرد (متن خیلی کوتاه یا نامعتبره).", "The server did not accept the ticket (too short or invalid)."),
        _ => L.T($"سرور جواب خطا داد ({(int)code}).", $"The server answered with an error ({(int)code})."),
    };

    /// <summary>Sends a ticket. Returns (id, null) or (null, error text).</summary>
    public async Task<(string? id, string? error)> SendAsync(string title, string body, string category, string contact, string diag, CancellationToken ct = default)
    {
        if (!Available) return (null, L.T("سرور پشتیبانی تنظیم نشده.", "The support server is not set up."));
        try
        {
            using var http = Http();
            var payload = JsonSerializer.Serialize(new
            {
                device = _settings().DeviceId, title, body, category, contact, diag,
                app = Updater.Current.ToString(3),
            });
            using var response = await http.PostAsync(Endpoint + "/tickets", new StringContent(payload, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return (null, Describe(response.StatusCode));
            var created = JsonSerializer.Deserialize<Created>(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false), Json);
            return created is null ? (null, L.T("جواب سرور خوانده نشد.", "The server's answer could not be read.")) : (created.Id, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (null, L.T("اتصال به سرور پشتیبانی برقرار نشد.", "Could not reach the support server."));
        }
    }

    /// <summary>The tickets sent from this PC, with the replies they got.</summary>
    public async Task<(List<TicketInfo>? tickets, string? error)> MineAsync(CancellationToken ct = default) =>
        await ListAsync($"{Endpoint}/tickets?device={_settings().DeviceId}", admin: false, ct).ConfigureAwait(false);

    /// <summary>Every ticket (owner only; the server checks the key).</summary>
    public async Task<(List<TicketInfo>? tickets, string? error)> AllAsync(CancellationToken ct = default) =>
        await ListAsync(Endpoint + "/admin/tickets", admin: true, ct).ConfigureAwait(false);

    internal static List<TicketInfo> ParseListing(string json) => JsonSerializer.Deserialize<Listing>(json, Json)?.Tickets ?? [];

    private async Task<(List<TicketInfo>?, string?)> ListAsync(string url, bool admin, CancellationToken ct)
    {
        if (!Available) return (null, L.T("سرور پشتیبانی تنظیم نشده.", "The support server is not set up."));
        try
        {
            using var http = Http();
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (admin) request.Headers.Add("X-Admin-Key", AdminKey);
            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return (null, Describe(response.StatusCode));
            return (ParseListing(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (null, L.T("اتصال به سرور پشتیبانی برقرار نشد.", "Could not reach the support server."));
        }
    }

    /// <summary>Owner only: answers a ticket and/or changes its status.</summary>
    public async Task<string?> ReplyAsync(string id, string text, string status, CancellationToken ct = default)
    {
        if (!Available || !IsAdmin) return L.T("کلید مدیر یا سرور تنظیم نشده.", "The admin key or the server is not set.");
        try
        {
            using var http = Http();
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{Endpoint}/admin/tickets/{Uri.EscapeDataString(id)}/reply")
            {
                Content = new StringContent(JsonSerializer.Serialize(new { text, status }), Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("X-Admin-Key", AdminKey);
            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? null : Describe(response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return L.T("اتصال به سرور پشتیبانی برقرار نشد.", "Could not reach the support server.");
        }
    }
}
