using System.IO;
using System.Text.Json;

namespace Shabpa.Tests;

public class GameLogTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 20, 0, 0);

    private static RecorderInput In(int seconds, string key = "elden", double gpuT = 70, double? cpuT = null, long down = 0, long up = 0, bool known = false, double fps = 0)
        => new(T0.AddSeconds(seconds), key, key.Length > 0 ? "Elden Ring" : "", 40 + seconds % 20, 80, cpuT, gpuT, 55, 2, 0.5, fps, down, up, known);

    private static string Scratch()
    {
        var dir = Path.Combine(Path.GetTempPath(), "shabpa-gamelog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static FinishedSession Make(string key, DateTime start, int seconds = 600)
        => new(key, key, new GameSessionRecord { Id = start.ToString("yyyyMMddHHmmss") + "-" + key, Start = start, Seconds = seconds, Stats = { [GameMetric.GpuTemp] = new MetricStat { Min = 50, Avg = 60, Max = 70 } } },
            new SessionSeries { Series = { [GameMetric.GpuTemp] = [50, 60, 70] } });

    [Fact]
    public void A_session_starts_with_the_game_and_ends_when_it_closes()
    {
        var rec = new GameSessionRecorder();
        for (var s = 0; s <= 600; s++) Assert.Null(rec.Feed(In(s, gpuT: 60 + s / 60.0)));
        Assert.True(rec.Active);
        var done = rec.Feed(In(601, ""));
        Assert.NotNull(done);
        Assert.False(rec.Active);
        Assert.Equal("elden", done!.Key);
        Assert.InRange(done.Record.Seconds, 600, 610);
        var gpu = done.Record.Stat(GameMetric.GpuTemp)!;
        Assert.Equal(60, gpu.Min, 0);
        Assert.Equal(70, gpu.Max, 0);
        Assert.InRange(gpu.Avg, 64, 66);
        Assert.Null(done.Record.Stat(GameMetric.CpuTemp)); // no reading, no entry
        Assert.Null(done.Record.Stat(GameMetric.Fps));
        Assert.True(done.Record.NetApprox);
    }

    [Fact]
    public void Sessions_under_a_minute_are_dropped()
    {
        var rec = new GameSessionRecorder();
        for (var s = 0; s <= 50; s++) rec.Feed(In(s));
        Assert.Null(rec.Feed(In(51, "")));
    }

    [Fact]
    public void Another_game_closes_the_first_session()
    {
        var rec = new GameSessionRecorder();
        for (var s = 0; s <= 300; s++) rec.Feed(In(s));
        var done = rec.Feed(In(301, "valorant"));
        Assert.Equal("elden", done!.Key);
        Assert.Equal("valorant", rec.Key);
    }

    [Fact]
    public void Series_are_downsampled_to_at_most_120_points_and_memory_stays_bounded()
    {
        var rec = new GameSessionRecorder();
        for (var s = 0; s <= 10 * 3600; s += 1) rec.Feed(In(s, gpuT: 50 + s % 3600 / 120.0, cpuT: 60, fps: 90));
        var done = rec.Flush(T0.AddHours(10))!;
        foreach (var (_, values) in done.Series.Series) Assert.InRange(values.Length, 2, GameSessionRecorder.MaxPoints);
        Assert.Equal(GameSessionRecorder.MaxPoints, done.Series.Series[GameMetric.GpuTemp].Length);
        Assert.NotNull(done.Record.Stat(GameMetric.Fps));
        Assert.NotNull(done.Record.Stat(GameMetric.CpuTemp));
    }

    [Fact]
    public void Downsample_averages_slices_and_keeps_short_series()
    {
        Assert.Equal([1.0, 2.0], GameSessionRecorder.Downsample([1, double.NaN, 2]));
        var big = GameSessionRecorder.Downsample(Enumerable.Range(0, 1000).Select(i => (double)i).ToList());
        Assert.Equal(120, big.Length);
        Assert.True(big[0] < big[^1]);
    }

    [Fact]
    public void Network_comes_from_the_app_counters_and_survives_a_midnight_reset()
    {
        var rec = new GameSessionRecorder();
        for (var s = 0; s <= 120; s += 1) rec.Feed(In(s, down: 1000 + s * 100, up: 10 * s, known: true));
        // the day's counters start again from zero
        for (var s = 121; s <= 240; s += 1) rec.Feed(In(s, down: (s - 121) * 100, up: (s - 121) * 10, known: true));
        var done = rec.Feed(In(241, ""))!;
        Assert.False(done.Record.NetApprox);
        Assert.InRange(done.Record.DownBytes, 23000, 24500);
    }

    [Fact]
    public void Other_programs_are_aggregated_by_name_and_capped_at_eight()
    {
        var rec = new GameSessionRecorder();
        rec.Feed(In(0));
        Assert.False(rec.TryBeginProcessLook(T0.AddSeconds(10)));
        for (var s = 1; s <= 130; s++) rec.Feed(In(s));
        Assert.True(rec.TryBeginProcessLook(T0.AddSeconds(41)));
        Assert.False(rec.TryBeginProcessLook(T0.AddSeconds(42)));
        var look = Enumerable.Range(0, 12).Select(i => new ProcUse("app" + i, 10 - i, 100 * (i + 1))).ToList();
        rec.AddProcessLook("elden", look);
        rec.AddProcessLook("elden", look.Take(2).ToList());
        rec.AddProcessLook("other", look); // belongs to another game: ignored
        var done = rec.Feed(In(131, ""))!;
        Assert.Equal(2, done.Record.ProcLooks);
        Assert.Equal(8, done.Record.Programs.Count);
    }

    [Fact]
    public void Probe_picks_the_busiest_and_skips_system_and_the_game()
    {
        var a = new Dictionary<int, (string, double, double)> { [1] = ("chrome", 0, 900), [2] = ("svchost", 0, 300), [3] = ("elden", 0, 4000), [4] = ("tiny", 0, 20) };
        var b = new Dictionary<int, (string, double, double)> { [1] = ("chrome", 8, 950), [2] = ("svchost", 30, 300), [3] = ("elden", 100, 4000), [4] = ("tiny", 0.01, 20) };
        var picked = ProcessProbe.Pick(a, b, 10, 4, "elden");
        Assert.Single(picked);
        Assert.Equal("chrome", picked[0].Name);
        Assert.InRange(picked[0].CpuPercent, 19, 21);
    }

    [Fact]
    public void The_store_keeps_sessions_and_trims_old_ones_per_game_and_in_total()
    {
        var dir = Scratch();
        try
        {
            var store = new GameLogStore(dir);
            for (var i = 0; i < GameLogStore.PerGameCap + 20; i++) store.Add(Make("a", T0.AddMinutes(i * 15)));
            Assert.Equal(GameLogStore.PerGameCap, store.Game("a")!.Sessions.Count);
            Assert.Equal(GameLogStore.PerGameCap, Directory.GetFiles(Path.Combine(dir, "games-series")).Length); // evicted series files are gone too
            for (var g = 0; g < 7; g++)
                for (var i = 0; i < GameLogStore.PerGameCap; i++) store.Add(Make("g" + g, T0.AddDays(1).AddMinutes(i * 15 + g)));
            Assert.True(store.Games.Sum(x => x.Sessions.Count) <= GameLogStore.TotalCap);

            var again = new GameLogStore(dir);
            again.Load();
            Assert.Equal(store.Games.Sum(x => x.Sessions.Count), again.Games.Sum(x => x.Sessions.Count));
            var first = again.Games[0].Sessions[0];
            Assert.NotNull(again.SeriesOf(first.Id));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Deleting_a_session_or_a_game_removes_its_files()
    {
        var dir = Scratch();
        try
        {
            var store = new GameLogStore(dir);
            store.Add(Make("a", T0));
            store.Add(Make("a", T0.AddHours(1)));
            store.Add(Make("b", T0));
            var id = store.Game("a")!.Sessions[0].Id;
            Assert.True(store.DeleteSession("a", id));
            Assert.Null(store.ReadSeries(id));
            Assert.Single(store.Game("a")!.Sessions);
            Assert.True(store.DeleteGame("a"));
            Assert.Null(store.Game("a"));
            Assert.Single(Directory.GetFiles(Path.Combine(dir, "games-series")));
            Assert.False(store.DeleteGame("nope"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_corrupt_file_is_kept_aside_and_the_last_good_copy_is_used()
    {
        var dir = Scratch();
        try
        {
            var store = new GameLogStore(dir);
            store.Add(Make("a", T0));
            store.Add(Make("a", T0.AddHours(1))); // the second save leaves the first version as games.json.bak
            File.WriteAllText(store.File, "{ this is not json");
            var again = new GameLogStore(dir);
            again.Load();
            Assert.Single(again.Games);
            Assert.True(File.Exists(store.File + ".broken"));

            File.WriteAllText(store.File, "garbage");
            File.WriteAllText(store.File + ".bak", "garbage too");
            var empty = new GameLogStore(dir);
            empty.Load(); // must not throw
            Assert.Empty(empty.Games);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_missing_or_odd_file_loads_as_empty_and_old_state_without_the_setting_keeps_the_default()
    {
        var dir = Scratch();
        try
        {
            var store = new GameLogStore(dir);
            store.Load();
            Assert.Empty(store.Games);
            File.WriteAllText(store.File, "{\"Games\":[{\"Key\":\"\",\"Sessions\":[]},{\"Key\":\"x\",\"Sessions\":null}]}");
            store.Load();
            Assert.Empty(store.Games);

            // a state.json written before this feature has no GameLog setting: it stays on
            var old = JsonSerializer.Deserialize<Settings>("{\"GameReports\":true,\"Theme\":\"night\"}")!;
            Assert.True(old.GameLog);
            Assert.True(old.GameReports);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Read_only_stores_never_touch_the_disk()
    {
        var dir = Scratch();
        try
        {
            var store = new GameLogStore(dir, readOnly: true);
            store.Add(Make("a", T0));
            Assert.Single(store.Games);
            Assert.False(File.Exists(store.File));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Totals_follow_the_chosen_period_and_sort_by_time()
    {
        var now = new DateTime(2026, 10, 17, 22, 0, 0);
        var (games, _) = GameLogDemo.Make(now);
        var week = PlayStats.Totals(games, PlaySpan.Week, now);
        var all = PlayStats.Totals(games, PlaySpan.All, now);
        Assert.True(all.Count >= week.Count);
        Assert.True(all.Sum(t => t.Time.TotalSeconds) >= week.Sum(t => t.Time.TotalSeconds));
        Assert.Equal(all.OrderByDescending(t => t.Time).Select(t => t.Key), all.Select(t => t.Key));
        Assert.Empty(PlayStats.Totals(games, PlaySpan.Today, now.AddDays(30)));
    }

    [Fact]
    public void The_end_of_game_summary_names_the_game_time_internet_and_temperatures()
    {
        var rec = new GameSessionRecorder();
        for (var s = 0; s <= 700; s++) rec.Feed(In(s, gpuT: 71, cpuT: 64, down: s * 50_000, up: s * 5_000, known: true));
        var text = Shabpa.Hub.DescribeSession(rec.Feed(In(701, ""))!);
        Assert.Contains("Elden Ring", text);
        Assert.Contains("71", text);
        Assert.Contains("64", text);
        Assert.Contains('↓', text);
    }
}

public class GameMessageTests
{
    private static FinishedSession Make(string name = "Elden Ring")
    {
        var rec = new GameSessionRecorder();
        for (var s = 0; s <= 700; s++)
            rec.Feed(new RecorderInput(new DateTime(2026, 10, 1, 20, 0, 0).AddSeconds(s), "k", name, 40, 80, 64, 71, 55, 1, 0.1, 90, s * 50_000, s * 5_000, true));
        return rec.Feed(new RecorderInput(new DateTime(2026, 10, 1, 20, 0, 0).AddSeconds(701), "", "", 0, 0, null, null, 0, 0, 0, 0))!;
    }

    [Fact]
    public void Html_has_bold_game_name_sections_and_bullets()
    {
        var html = GameMessage.Html(Make());
        Assert.StartsWith("🎮 <b>Elden Ring</b>", html);
        Assert.Contains("🌡", html);
        Assert.Contains("📶", html);
        Assert.Contains("  • ", html);
        Assert.Equal(html.Split("<b>").Length, html.Split("</b>").Length);
    }

    [Fact]
    public void Html_escapes_the_game_name()
    {
        var html = GameMessage.Html(Make("A<b>&Co"));
        Assert.Contains("A&lt;b&gt;&amp;Co", html);
        Assert.DoesNotContain("A<b>", html);
    }

    [Fact]
    public void Plain_has_no_tags_and_html_is_cut_under_telegram_limit()
    {
        Assert.DoesNotContain("<b>", GameMessage.Plain(Make()));
        var big = string.Join('\n', Enumerable.Repeat("line of text here", 600));
        var cut = GameMessage.Fit(big);
        Assert.True(cut.Length <= GameMessage.MaxLength);
        Assert.EndsWith("…", cut);
    }
}

public class GameImpactTests
{
    [Fact]
    public void Level_follows_load_and_temperature()
    {
        Assert.Equal(0, Shabpa.Core.GameImpact.Level(30, 60, 50, 60, 65));
        Assert.Equal(1, Shabpa.Core.GameImpact.Level(30, 95, 50, 60, 65));
        Assert.Equal(2, Shabpa.Core.GameImpact.Level(30, 95, 50, 60, 88));
        Assert.Equal(2, Shabpa.Core.GameImpact.Level(30, null, 95, null, null));
        Assert.False(string.IsNullOrWhiteSpace(Shabpa.Core.GameImpact.Describe(30, 60, 50, 60, 65)));
    }
}
