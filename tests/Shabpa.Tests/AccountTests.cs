using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Shabpa.Core;

namespace Shabpa.Tests;

public class AccountTests
{
    private static Hub NewHub() =>
        new(new Store(Path.Combine(Path.GetTempPath(), "shabpa-acc-" + Guid.NewGuid().ToString("N"))) { ReadOnly = true }, dryRun: true, demo: true);

    /// <summary>A tiny stand-in for the account worker: one save file with a revision, and the same 409 rule.</summary>
    private sealed class FakeServer : HttpMessageHandler
    {
        public string Blob = ""; public int Rev; public int Puts; public bool ConflictOnce; public string? SeenToken;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            SeenToken = req.Headers.Authorization?.Parameter;
            var path = req.RequestUri!.AbsolutePath;
            HttpResponseMessage Json(HttpStatusCode code, object o) => new(code) { Content = new StringContent(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json") };
            if (path == "/v1/save" && req.Method == HttpMethod.Get)
                return Task.FromResult(Rev == 0 ? Json(HttpStatusCode.NotFound, new { error = "none" }) : Json(HttpStatusCode.OK, new { rev = Rev, blob = Blob }));
            if (path == "/v1/save" && req.Method == HttpMethod.Put)
            {
                var body = JsonDocument.Parse(req.Content!.ReadAsStringAsync(ct).Result).RootElement;
                if (ConflictOnce) { ConflictOnce = false; Rev++; Blob = JsonSerializer.Serialize(new RewardsState { TotalFeathers = 500, Feathers = 500, Medals = { ["tour"] = DateTime.Now } }); return Task.FromResult(Json(HttpStatusCode.Conflict, new { error = "conflict" })); }
                if (body.GetProperty("rev").GetInt32() != Rev) return Task.FromResult(Json(HttpStatusCode.Conflict, new { error = "conflict" }));
                Rev++; Puts++; Blob = body.GetProperty("blob").GetString()!;
                return Task.FromResult(Json(HttpStatusCode.OK, new { rev = Rev }));
            }
            return Task.FromResult(Json(HttpStatusCode.NotFound, new { }));
        }
    }

    private static (Hub hub, AccountService svc, FakeServer srv) Signed()
    {
        var hub = NewHub();
        var srv = new FakeServer();
        var svc = new AccountService(hub, () => "https://cloud.test", srv);
        hub.State.Cloud.Token = Secret.Protect("tok-123456789012345678901234567890123");
        return (hub, svc, srv);
    }

    [Fact]
    public void After_a_reinstall_the_cloud_copy_brings_everything_back()
    {
        var cloud = new RewardsState { Feathers = 420, TotalFeathers = 900, BestStreak = 12, FirstRun = new DateTime(2026, 1, 1) };
        cloud.Medals["hello"] = new DateTime(2026, 1, 2); cloud.Counters["tasksDone"] = 40; cloud.Claimed.Add("o.tour");
        var fresh = new RewardsState { Feathers = 5, TotalFeathers = 5, FirstRun = DateTime.Now };
        fresh.Medals["hello"] = DateTime.Now;
        var m = RewardsMerge.Merge(fresh, cloud);
        Assert.Equal(420, m.Feathers);
        Assert.Equal(900, m.TotalFeathers);
        Assert.Equal(new DateTime(2026, 1, 1), m.FirstRun);
        Assert.Equal(new DateTime(2026, 1, 2), m.Medals["hello"]);
        Assert.Equal(40, m.Counters["tasksDone"]);
        Assert.Contains("o.tour", m.Claimed);
    }

    [Fact]
    public void Two_PCs_with_their_own_progress_lose_nothing_and_never_pay_a_quest_twice()
    {
        var a = new RewardsState { TotalFeathers = 300, Feathers = 300 };
        a.Medals["x"] = DateTime.Now; a.Claimed.Add("o.backup"); a.Counters["ramFreed"] = 3; a.Days.Add("2026-11-01");
        var b = new RewardsState { TotalFeathers = 250, Feathers = 250 };
        b.Medals["y"] = DateTime.Now; b.Claimed.Add("o.theme"); b.Counters["ramFreed"] = 9; b.Days.Add("2026-11-02");
        var m = RewardsMerge.Merge(a, b);
        Assert.Equal(300, m.TotalFeathers);
        Assert.Contains("x", m.Medals.Keys); Assert.Contains("y", m.Medals.Keys);
        Assert.Contains("o.backup", m.Claimed); Assert.Contains("o.theme", m.Claimed);
        Assert.Equal(9, m.Counters["ramFreed"]);
        Assert.Equal(2, m.Days.Count);
        Assert.Equal(300, a.TotalFeathers); // the inputs are untouched
        Assert.True(RewardsMerge.Same(RewardsMerge.Merge(m, m), m));
    }

    [Fact]
    public async Task First_sync_with_an_empty_cloud_uploads_this_PCs_progress()
    {
        var (hub, svc, srv) = Signed();
        hub.State.Rewards = new RewardsState { Feathers = 77, TotalFeathers = 77 };
        Assert.True(await svc.SyncAsync());
        Assert.Equal(1, srv.Rev);
        Assert.Equal(77, JsonSerializer.Deserialize<RewardsState>(srv.Blob)!.Feathers);
        Assert.Equal("tok-123456789012345678901234567890123", srv.SeenToken);
        Assert.Equal(1, hub.State.Cloud.Rev);
        Assert.NotNull(hub.State.Cloud.LastSync);
    }

    [Fact]
    public async Task A_second_PC_that_saved_first_is_merged_in_instead_of_overwritten()
    {
        var (hub, svc, srv) = Signed();
        hub.State.Rewards = new RewardsState { Feathers = 10, TotalFeathers = 10 };
        srv.ConflictOnce = true;
        Assert.True(await svc.SyncAsync());
        Assert.Equal(500, hub.State.Rewards.TotalFeathers);       // the other PC's progress came in
        Assert.Contains("tour", hub.State.Rewards.Medals.Keys);
    }

    [Fact]
    public async Task Nothing_is_sent_when_the_cloud_already_has_the_same_progress()
    {
        var (hub, svc, srv) = Signed();
        hub.State.Rewards = new RewardsState { Feathers = 10, TotalFeathers = 10 };
        await svc.SyncAsync();
        var puts = srv.Puts;
        await svc.SyncAsync();
        Assert.Equal(puts, srv.Puts);
    }

    [Fact]
    public void The_suggestion_waits_for_something_worth_keeping_and_stops_after_three_or_a_no()
    {
        var hub = NewHub();
        var svc = new AccountService(hub, () => "https://cloud.test", new FakeServer());
        hub.State.Rewards = new RewardsState { TotalFeathers = 20 };
        svc.MaybeSuggest();
        Assert.Equal(0, hub.State.Cloud.Suggested);                  // too early
        hub.State.Rewards = new RewardsState { TotalFeathers = 200, Days = ["2026-11-01", "2026-11-02"] };
        svc.MaybeSuggest();
        Assert.Equal(1, hub.State.Cloud.Suggested);
        svc.MaybeSuggest();
        Assert.Equal(1, hub.State.Cloud.Suggested);                  // not again for two weeks
        hub.State.Cloud.LastSuggested = DateTime.Now.AddDays(-15);
        svc.SaidNoThanks();
        svc.MaybeSuggest();
        Assert.Equal(1, hub.State.Cloud.Suggested);                  // «no thanks» is final
        Assert.False(new AccountService(hub, () => "").Available);   // no server address: the feature is off and silent
    }
}
