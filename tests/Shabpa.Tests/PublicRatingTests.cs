using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Shabpa.Core;

namespace Shabpa.Tests;

/// <summary>A rating leaves the PC only after the person said yes to the separate question, carries only the stars, the app version, the language and a random install id, and a failure
/// never throws.</summary>
public class PublicRatingTests
{
    private sealed class Fake(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<(string Url, string Body, string? Type)> Seen { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen.Add((request.RequestUri!.ToString(), request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct), request.Content?.Headers.ContentType?.MediaType));
            return answer(request);
        }
    }

    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "shabpa-pr-" + Guid.NewGuid().ToString("N"));
    private static readonly string[] Two = ["https://one.test", "https://two.test/"];

    [Fact]
    public async Task Saying_no_sends_nothing_at_all_and_makes_no_id()
    {
        var folder = TempFolder();
        var fake = new Fake(_ => new HttpResponseMessage(HttpStatusCode.OK));
        Assert.Equal(ShareOutcome.Declined, await PublicRating.ShareAsync(false, 5, folder, "3.16.0", "fa", Two, handler: fake));
        Assert.Empty(fake.Seen);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task Yes_sends_exactly_four_fields_as_json_to_the_rate_endpoint()
    {
        var folder = TempFolder();
        var fake = new Fake(_ => new HttpResponseMessage(HttpStatusCode.OK));
        Assert.Equal(ShareOutcome.Sent, await PublicRating.ShareAsync(true, 4, folder, "3.16.0", "en", Two, handler: fake));
        var sent = Assert.Single(fake.Seen);
        Assert.Equal("https://one.test/api/rate", sent.Url);
        Assert.Equal("application/json", sent.Type);
        using var doc = JsonDocument.Parse(sent.Body);
        Assert.Equal(["stars", "id", "version", "lang"], doc.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal(4, doc.RootElement.GetProperty("stars").GetInt32());
        Assert.Equal("3.16.0", doc.RootElement.GetProperty("version").GetString());
        Assert.Equal("en", doc.RootElement.GetProperty("lang").GetString());
        Assert.Matches("^[0-9a-f]{32}$", doc.RootElement.GetProperty("id").GetString()!);
        Assert.DoesNotContain(Environment.MachineName, sent.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.UserName, sent.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_install_id_is_random_made_once_kept_and_repaired_when_damaged()
    {
        var a = TempFolder();
        var first = PublicRating.InstallId(a);
        Assert.Equal(first, PublicRating.InstallId(a));                                   // the same install keeps the same id, so it keeps one vote
        Assert.NotEqual(first, PublicRating.InstallId(TempFolder()));                     // another install gets another one
        File.WriteAllText(Path.Combine(a, "rating-id.txt"), "not an id");
        Assert.Matches("^[0-9a-f]{32}$", PublicRating.InstallId(a));
        Assert.DoesNotContain(Environment.MachineName.ToLowerInvariant(), first);
    }

    [Fact]
    public async Task A_dead_first_address_falls_back_to_the_second()
    {
        var fake = new Fake(r => r.RequestUri!.Host == "one.test" ? throw new HttpRequestException("cut") : new HttpResponseMessage(HttpStatusCode.OK));
        Assert.Equal(ShareOutcome.Sent, await PublicRating.ShareAsync(true, 5, TempFolder(), "3.16.0", "fa", Two, handler: fake));
        Assert.Equal(2, fake.Seen.Count);
        Assert.Equal(fake.Seen[0].Body, fake.Seen[1].Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.UnsupportedMediaType)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task A_refusal_or_a_failure_is_reported_never_thrown(HttpStatusCode status)
    {
        var fake = new Fake(_ => new HttpResponseMessage(status));
        Assert.Equal(ShareOutcome.Failed, await PublicRating.ShareAsync(true, 5, TempFolder(), "3.16.0", "fa", Two, handler: fake));
    }

    [Fact]
    public async Task No_connection_at_all_is_a_quiet_failure()
    {
        var fake = new Fake(_ => throw new HttpRequestException("offline"));
        Assert.Equal(ShareOutcome.Failed, await PublicRating.ShareAsync(true, 3, TempFolder(), "3.16.0", "fa", Two, handler: fake));
        Assert.Equal(ShareOutcome.Declined, await PublicRating.ShareAsync(true, 0, TempFolder(), "3.16.0", "fa", Two, handler: fake));   // an impossible star count is never sent
        Assert.Equal(ShareOutcome.Declined, await PublicRating.ShareAsync(true, 6, TempFolder(), "3.16.0", "fa", Two, handler: fake));
    }

    [Fact]
    public void The_privacy_wording_lists_ratings_among_the_things_that_use_the_internet()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Shabpa.slnx"))) dir = dir.Parent;
        var readme = File.ReadAllText(Path.Combine(dir!.FullName, "README.md"));
        var security = File.ReadAllText(Path.Combine(dir.FullName, "SECURITY.md"));
        Assert.Contains("rating", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rating", security, StringComparison.OrdinalIgnoreCase);
    }
}
