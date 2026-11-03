using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Shabpa.Core;

// The optional account («ذخیره‌ی ابری»). Everything works without it. When a person chooses to log in with Telegram, the progress
// (feathers, medals, counters; nothing personal) is kept in the cloud so a Windows reinstall or a new PC does not wipe it.
// Local first: the app always uses its own copy; the cloud copy is merged in and written back now and then.

/// <summary>What this PC remembers about the account (state.json → Cloud). The token is encrypted for this Windows user.</summary>
public sealed class CloudState
{
    public string Token { get; set; } = "";
    public int Rev { get; set; }
    public DateTime? LastSync { get; set; }
    /// <summary>The gentle «keep your progress» suggestion: when it was last shown, how often, and whether the person said no.</summary>
    public DateTime? LastSuggested { get; set; }
    public int Suggested { get; set; }
    public bool NoThanks { get; set; }
}

/// <summary>Joins two copies of the progress (this PC's and the cloud's) without losing anything either one earned.</summary>
public static class RewardsMerge
{
    /// <summary>The result is a new state; neither input is changed.</summary>
    public static RewardsState Merge(RewardsState local, RewardsState remote)
    {
        // the copy with more earned feathers is the base (a reinstall has almost nothing; the cloud wins), the other adds what it has extra
        var (a, b) = remote.TotalFeathers > local.TotalFeathers ? (remote, local) : (local, remote);
        var m = new RewardsState
        {
            FirstRun = a.FirstRun < b.FirstRun ? a.FirstRun : b.FirstRun,
            Feathers = a.Balance,                          // the real balance, never the large number the owner's key shows
            TotalFeathers = a.TotalFeathers,
            TourDone = a.TourDone || b.TourDone,
            TourOffered = a.TourOffered || b.TourOffered,
            NightOwl = a.NightOwl || b.NightOwl,
            LongestGameMinutes = Math.Max(a.LongestGameMinutes, b.LongestGameMinutes),
            LastLevel = Math.Max(a.LastLevel, b.LastLevel),
            BestStreak = Math.Max(a.BestStreak, b.BestStreak),
        };
        foreach (var src in new[] { b, a })
            foreach (var (id, at) in src.Medals)
                m.Medals[id] = m.Medals.TryGetValue(id, out var have) && have < at ? have : at;
        m.Days = [.. a.Days.Union(b.Days).Order()];
        m.Pages = [.. a.Pages.Union(b.Pages)];
        m.Games = [.. a.Games.Union(b.Games).Take(60)];
        m.Claimed = [.. a.Claimed.Union(b.Claimed)];   // never forget a payment
        // the wardrobe: parts bought on either copy are kept; the other copy's purchases are paid from the base copy's feathers
        m.Owned = [.. a.Owned.Union(b.Owned)];
        m.Worn = new(a.Worn);
        foreach (var (slot, id) in b.Worn) m.Worn.TryAdd(slot, id);
        m.Presets = a.Presets.Any(x => x.Count > 0) ? [.. a.Presets] : [.. b.Presets];
        m.ActivePreset = a.Presets.Any(x => x.Count > 0) ? a.ActivePreset : b.ActivePreset;
        var extra = b.Owned.Except(a.Owned).Sum(id => BoofiCatalog.Find(id)?.Price ?? (id.StartsWith(PremiumTheme.Prefix) ? PremiumTheme.Find(id[PremiumTheme.Prefix.Length..])?.Price ?? 0 : 0));
        m.Feathers = Math.Max(0, m.Feathers - extra);
        m.DoneTasks = [.. a.DoneTasks.Union(b.DoneTasks).TakeLast(400)];
        m.Active = Max(a.Active, b.Active);
        m.Counters = Max(a.Counters, b.Counters);
        // today's and this week's counters belong to whichever copy is from the later day/week
        var today = string.CompareOrdinal(local.DayKey, remote.DayKey) >= 0 ? local : remote;
        m.DayKey = today.DayKey; m.DayCounters = new(today.DayCounters); m.DayPages = [.. today.DayPages];
        var week = string.CompareOrdinal(local.WeekKey, remote.WeekKey) >= 0 ? local : remote;
        m.WeekKey = week.WeekKey; m.WeekCounters = new(week.WeekCounters);
        return m;
    }

    private static Dictionary<string, int> Max(Dictionary<string, int> x, Dictionary<string, int> y)
    {
        var r = new Dictionary<string, int>(x);
        foreach (var (k, v) in y) r[k] = r.TryGetValue(k, out var have) ? Math.Max(have, v) : v;
        return r;
    }

    /// <summary>True when two states hold the same progress (so there is nothing to write back). Order does not matter.</summary>
    public static bool Same(RewardsState a, RewardsState b) =>
        a.Feathers == b.Feathers && a.TotalFeathers == b.TotalFeathers && a.FirstRun == b.FirstRun && a.TourDone == b.TourDone && a.TourOffered == b.TourOffered
        && a.NightOwl == b.NightOwl && a.LongestGameMinutes == b.LongestGameMinutes && a.LastLevel == b.LastLevel && a.BestStreak == b.BestStreak
        && a.DayKey == b.DayKey && a.WeekKey == b.WeekKey
        && SameMap(a.Medals, b.Medals) && SameMap(a.Active, b.Active) && SameMap(a.Counters, b.Counters) && SameMap(a.DayCounters, b.DayCounters) && SameMap(a.WeekCounters, b.WeekCounters)
        && SameSet(a.Owned, b.Owned) && SameMap(a.Worn, b.Worn) && SameSet(a.Days, b.Days) && SameSet(a.Pages, b.Pages) && SameSet(a.Games, b.Games) && SameSet(a.Claimed, b.Claimed) && SameSet(a.DoneTasks, b.DoneTasks) && SameSet(a.DayPages, b.DayPages);

    private static bool SameSet(List<string> x, List<string> y) => x.Count == y.Count && !x.Except(y).Any();
    private static bool SameMap<T>(Dictionary<string, T> x, Dictionary<string, T> y) where T : IEquatable<T> =>
        x.Count == y.Count && x.All(kv => y.TryGetValue(kv.Key, out var v) && v.Equals(kv.Value));
}

public sealed class AccountService
{
    /// <summary>The address of the deployed account worker (server/account-worker). Empty means the feature is hidden; set it when the server is live.</summary>
    public const string DefaultServer = "";

    private readonly Hub _hub;
    private readonly Func<string> _server;
    private readonly HttpMessageHandler? _handler;
    private HttpClient? _http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _dirty;
    private DateTime _lastPush = DateTime.MinValue;
    private int _tick;

    public AccountService(Hub hub, Func<string>? server = null, HttpMessageHandler? handler = null)
    {
        _hub = hub;
        _server = server ?? (() => DefaultServer);
        _handler = handler;
    }

    private CloudState State => _hub.State.Cloud;
    public bool Available => _server().Length > 0;
    public bool SignedIn => Available && State.Token.Length > 0;
    public DateTime? LastSync => State.LastSync;
    /// <summary>The last problem, in words the person can read (empty when all is well).</summary>
    public string Problem { get; private set; } = "";
    public event Action? Changed;

    public void Start()
    {
        if (!Available) return;
        _hub.Rewards.Changed += () => _dirty = true;
        _hub.Second += OnSecond;
        if (SignedIn) _ = Task.Run(() => SyncAsync());
    }

    private void OnSecond()
    {
        _tick++;
        if (SignedIn && _dirty && DateTime.UtcNow - _lastPush > TimeSpan.FromMinutes(5)) _ = Task.Run(() => SyncAsync());
        if (_tick % 60 == 0) MaybeSuggest();
    }

    /// <summary>Once in a while, never pushy: after real use, a notice that progress can be kept. «نه ممنون» stops it for good.</summary>
    public void MaybeSuggest()
    {
        var r = _hub.State.Rewards;
        if (!Available || SignedIn || State.NoThanks || State.Suggested >= 3) return;
        if (r.TotalFeathers < 60 || r.Days.Count < 2) return;                       // only after the person has something worth keeping
        if (State.LastSuggested is { } at && DateTime.Now - at < TimeSpan.FromDays(14)) return;
        if (_hub.GameModeActive) return;
        State.LastSuggested = DateTime.Now;
        State.Suggested++;
        _hub.Store.Save();
        _hub.Notify(L.T("پیشرفتت رو نگه دار", "Keep your progress"),
            L.T("اگه ویندوز رو دوباره نصب کنی پَرها و مدال‌هات می‌پرن. اگه خواستی (اختیاری) با تلگرام وارد شو تا توی ابر ذخیره بشن.",
                "If you reinstall Windows your feathers and medals are gone. If you like (it is optional), log in with Telegram to keep them in the cloud."),
            "rewards");
    }

    public void SaidNoThanks() { State.NoThanks = true; _hub.Store.Save(); Changed?.Invoke(); }

    private HttpClient Http()
    {
        if (_http is not null) return _http;
        HttpMessageHandler handler;
        if (_handler is not null) handler = _handler;
        else
        {
            var real = new HttpClientHandler();
            var proxy = _hub.Settings.ProxyUrl.Trim();
            if (proxy.Length > 0) { try { real.Proxy = new WebProxy(proxy); real.UseProxy = true; } catch { /* a bad proxy address is reported on the bot page */ } }
            handler = real;
        }
        _http = new HttpClient(handler, disposeHandler: _handler is null) { Timeout = TimeSpan.FromSeconds(20) };
        return _http;
    }

    private Uri Url(string path) => new(_server().TrimEnd('/') + path);

    private async Task<(HttpStatusCode status, JsonElement body)> Call(HttpMethod method, string path, object? body, bool authorized, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, Url(path));
        if (authorized) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Secret.Unprotect(State.Token));
        if (body is not null) req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var res = await Http().SendAsync(req, ct).ConfigureAwait(false);
        var text = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        JsonElement el = default;
        if (text.Length > 0) { try { el = JsonDocument.Parse(text).RootElement.Clone(); } catch { /* not JSON */ } }
        return (res.StatusCode, el);
    }

    // ---- login with Telegram -------------------------------------------------------------------------------

    /// <summary>Starts a login and returns the Telegram link to open, and the code to wait for.</summary>
    public async Task<(string Code, string Url)?> BeginLogin(CancellationToken ct = default)
    {
        if (!Available) return null;
        try
        {
            var (status, body) = await Call(HttpMethod.Post, "/v1/login/start", null, false, ct);
            if (status != HttpStatusCode.OK || !body.TryGetProperty("code", out var c) || !body.TryGetProperty("url", out var u)) { Fail(status); return null; }
            Problem = "";
            return (c.GetString() ?? "", u.GetString() ?? "");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { Problem = Offline(); return null; }
    }

    /// <summary>Waits (up to about five minutes) until the person pressed Start in Telegram, then keeps the token and syncs.</summary>
    public async Task<bool> FinishLogin(string code, CancellationToken ct = default, TimeSpan? every = null)
    {
        var delay = every ?? TimeSpan.FromSeconds(2);
        var until = DateTime.UtcNow.AddMinutes(5);
        while (DateTime.UtcNow < until && !ct.IsCancellationRequested)
        {
            try
            {
                var (status, body) = await Call(HttpMethod.Get, "/v1/login/poll?code=" + Uri.EscapeDataString(code), null, false, ct);
                if (status == HttpStatusCode.OK && body.TryGetProperty("token", out var t) && (t.GetString() ?? "").Length > 0)
                {
                    State.Token = Secret.Protect(t.GetString()!);
                    State.Rev = 0;
                    _hub.Store.Save();
                    Problem = "";
                    Changed?.Invoke();
                    await SyncAsync();
                    return true;
                }
                if (status == HttpStatusCode.Gone) { Problem = L.T("لینک ورود منقضی شد. دوباره امتحان کن.", "The login link expired. Try again."); return false; }
            }
            catch (Exception e) when (e is HttpRequestException) { Problem = Offline(); }
            try { await Task.Delay(delay, ct); } catch (TaskCanceledException) { return false; }
        }
        return false;
    }

    // ---- sync ----------------------------------------------------------------------------------------------

    /// <summary>Reads the cloud copy, merges it into this PC's progress and writes back if anything is new. Safe to call any time.</summary>
    public async Task<bool> SyncAsync(CancellationToken ct = default)
    {
        if (!SignedIn || _hub.State.Rewards.Unlimited || !await _gate.WaitAsync(0, ct)) return false;      // the owner's unlimited-feathers key is a local test mode: nothing of it goes to the cloud
        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var (status, body) = await Call(HttpMethod.Get, "/v1/save", null, true, ct);
                RewardsState? remote = null;
                var rev = 0;
                if (status == HttpStatusCode.OK && body.TryGetProperty("blob", out var blob) && body.TryGetProperty("rev", out var r))
                {
                    rev = r.GetInt32();
                    try { remote = JsonSerializer.Deserialize<RewardsState>(blob.GetString() ?? ""); } catch { remote = null; }
                }
                else if (status == HttpStatusCode.Unauthorized) { SignedOutByServer(); return false; }
                else if (status != HttpStatusCode.NotFound) { Fail(status); return false; }

                var local = _hub.State.Rewards;
                var merged = remote is null ? local : RewardsMerge.Merge(local, remote);
                if (remote is not null && !RewardsMerge.Same(local, merged)) { _hub.Rewards.Adopt(merged); local = _hub.State.Rewards; }

                if (remote is not null && RewardsMerge.Same(merged, remote)) { Done(rev); return true; }  // the cloud already has everything
                var put = await Call(HttpMethod.Put, "/v1/save", new { rev, blob = JsonSerializer.Serialize(local) }, true, ct);
                if (put.status == HttpStatusCode.OK && put.body.TryGetProperty("rev", out var nr)) { Done(nr.GetInt32()); return true; }
                if (put.status == HttpStatusCode.Conflict) continue;                                   // another PC saved first: read, merge, try again
                if (put.status == HttpStatusCode.Unauthorized) { SignedOutByServer(); return false; }
                Fail(put.status);
                return false;
            }
            return false;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { Problem = Offline(); Changed?.Invoke(); return false; }
        finally { _gate.Release(); }
    }

    private void Done(int rev)
    {
        State.Rev = rev; State.LastSync = DateTime.Now; _lastPush = DateTime.UtcNow; _dirty = false; Problem = "";
        _hub.Store.Save();
        Changed?.Invoke();
    }

    private void SignedOutByServer()
    {
        State.Token = ""; Problem = L.T("ورودت منقضی شده؛ دوباره وارد شو.", "Your login expired; log in again.");
        _hub.Store.Save(); Changed?.Invoke();
    }

    private void Fail(HttpStatusCode status)
    {
        Problem = status == HttpStatusCode.TooManyRequests ? L.T("کمی بعد دوباره امتحان کن.", "Try again in a little while.") : L.T("سرور جواب درست نداد.", "The server did not answer properly.");
        Changed?.Invoke();
    }

    private static string Offline() => L.T("به سرور نرسیدم. اینترنت یا پروکسی رو ببین.", "Could not reach the server. Check your internet or proxy.");

    // ---- leaving -------------------------------------------------------------------------------------------

    /// <summary>Logs this PC out. The progress stays here and in the cloud.</summary>
    public async Task SignOut()
    {
        try { if (SignedIn) await Call(HttpMethod.Post, "/v1/logout", null, true, CancellationToken.None); } catch { /* the token is dropped here either way */ }
        State.Token = ""; State.LastSync = null; State.Rev = 0;
        _hub.Store.Save(); Changed?.Invoke();
    }

    /// <summary>Removes everything the server holds about this person. The progress on this PC stays.</summary>
    public async Task<bool> DeleteAccount()
    {
        if (!SignedIn) return false;
        try
        {
            var (status, _) = await Call(HttpMethod.Delete, "/v1/account", null, true, CancellationToken.None);
            if (status != HttpStatusCode.OK && status != HttpStatusCode.Unauthorized) { Fail(status); return false; }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { Problem = Offline(); Changed?.Invoke(); return false; }
        State.Token = ""; State.LastSync = null; State.Rev = 0; State.NoThanks = false;
        _hub.Store.Save(); Changed?.Invoke();
        return true;
    }
}
