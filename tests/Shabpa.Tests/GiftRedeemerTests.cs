using System.IO;
using System.Net;
using System.Net.Http;
using Shabpa.Core;

namespace Shabpa.Tests;

/// <summary>A gift code is used up on the server so it works once in the whole world; with no answer from the server nothing is paid.</summary>
public class GiftRedeemerTests
{
    private sealed class Fake(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];
        public string Body { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!.ToString());
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            return answer(request);
        }
    }

    private static readonly string[] Two = ["https://one.test", "https://two.test/"];

    [Theory]
    [InlineData(HttpStatusCode.OK, GiftAnswer.Ok)]
    [InlineData(HttpStatusCode.Conflict, GiftAnswer.Used)]
    [InlineData(HttpStatusCode.NotFound, GiftAnswer.Unknown)]
    public async Task The_server_answer_decides(HttpStatusCode status, GiftAnswer expected)
    {
        var fake = new Fake(_ => new HttpResponseMessage(status));
        Assert.Equal(expected, await GiftRedeemer.RedeemAsync("abc-123", Two, handler: fake));
        Assert.Equal(["https://one.test/api/gift/redeem"], fake.Urls);
    }

    [Fact]
    public async Task Only_the_hash_is_sent_never_the_code()
    {
        var fake = new Fake(_ => new HttpResponseMessage(HttpStatusCode.OK));
        await GiftRedeemer.RedeemAsync("  secret-code ", Two, handler: fake);
        Assert.DoesNotContain("secret-code", fake.Body);
        Assert.Contains(GiftRedeemer.HashOf("secret-code"), fake.Body);
        Assert.Equal(64, GiftRedeemer.HashOf("x").Length);
    }

    [Fact]
    public async Task A_dead_first_address_falls_back_to_the_second()
    {
        var fake = new Fake(r => r.RequestUri!.Host == "one.test" ? throw new HttpRequestException("cut") : new HttpResponseMessage(HttpStatusCode.Conflict));
        Assert.Equal(GiftAnswer.Used, await GiftRedeemer.RedeemAsync("abc", Two, handler: fake));
        Assert.Equal(2, fake.Urls.Count);
    }

    [Fact]
    public async Task No_answer_at_all_pays_nothing()
    {
        Assert.Equal(GiftAnswer.Offline, await GiftRedeemer.RedeemAsync("abc", Two, handler: new Fake(_ => throw new HttpRequestException("offline"))));
        Assert.Equal(GiftAnswer.Offline, await GiftRedeemer.RedeemAsync("abc", Two, handler: new Fake(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError))));
        Assert.Equal(GiftAnswer.Offline, await GiftRedeemer.RedeemAsync("abc", Two, handler: new Fake(_ => new HttpResponseMessage((HttpStatusCode)429))));
    }

    [Fact]
    public async Task A_wrong_code_never_reaches_the_network_and_looks_like_any_unknown_sentence()
    {
        var hub = new Hub(new Store(Path.Combine(Path.GetTempPath(), "shabpa-gr-" + Guid.NewGuid().ToString("N"))) { ReadOnly = true }, dryRun: true, demo: true);
        hub.State.Rewards = new RewardsState { Feathers = 3, TotalFeathers = 3 };
        var asked = false;
        var text = await hub.RedeemGiftAsync("BOOFI-NOPE0-NOPE0", _ => { asked = true; return Task.FromResult(GiftAnswer.Ok); });
        Assert.False(asked);
        Assert.Equal(3, hub.State.Rewards.Feathers);
        Assert.Contains("نمی‌دونم", text);
    }
}
