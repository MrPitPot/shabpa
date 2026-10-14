using System.IO;
using Shabpa.Core;
using Shabpa.Pages;

namespace Shabpa.Tests;

public class UsageHistoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "shabpa-usage-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private static AppTraffic App(string key, long down, long up) => new(key, key.ToUpperInvariant(), "", 0, 0, down, up, default);

    [Fact]
    public void Running_totals_are_turned_into_per_day_amounts()
    {
        var usage = new UsageHistory();
        var t = new DateTime(2026, 5, 10, 21, 0, 0);
        usage.Update(t, [App("steam", 1000, 100)]);
        usage.Update(t.AddSeconds(3), [App("steam", 4000, 150)]);
        usage.Update(t.AddSeconds(6), [App("steam", 4000, 150)]); // nothing new

        var day = DateOnly.FromDateTime(t);
        var row = Assert.Single(usage.Range(day, day));
        Assert.Equal((4000, 150), (row.Down, row.Up));
        Assert.Equal(4000, usage.Hourly(day).down[21]);
    }

    [Fact]
    public void A_meter_restart_or_midnight_counts_the_new_total_as_new_traffic()
    {
        var usage = new UsageHistory();
        var evening = new DateTime(2026, 5, 10, 23, 59, 50);
        usage.Update(evening, [App("chrome", 5000, 0)]);
        usage.Update(evening.AddSeconds(20), [App("chrome", 300, 0)]); // next day: meter started again

        var yesterday = DateOnly.FromDateTime(evening);
        Assert.Equal(5000, usage.Range(yesterday, yesterday).Single().Down);
        Assert.Equal(300, usage.Range(yesterday.AddDays(1), yesterday.AddDays(1)).Single().Down);
        Assert.Equal(5300, usage.Total(yesterday, yesterday.AddDays(1)).down);
    }

    [Fact]
    public void Ranges_rank_apps_and_daily_series_fill_empty_days()
    {
        var usage = new UsageHistory();
        var d1 = new DateTime(2026, 5, 10, 12, 0, 0);
        usage.Update(d1, [App("a", 100, 0), App("b", 900, 0)]);
        usage.Update(d1.AddDays(2), [App("a", 700, 0), App("b", 0, 0)]);

        var from = DateOnly.FromDateTime(d1);
        var rows = usage.Range(from, from.AddDays(2));
        Assert.Equal(["b", "a"], rows.Select(r => r.Key));

        var daily = usage.Daily(from, from.AddDays(2), "a");
        Assert.Equal([100L, 0L, 700L], daily.Select(d => d.down));
    }

    [Fact]
    public void History_survives_a_restart_and_tiny_apps_are_folded_into_other()
    {
        var file = Path.Combine(_dir, "usage.json");
        var usage = new UsageHistory();
        var past = DateTime.Now.AddDays(-3);
        usage.Update(past, [App("big", 10_000_000, 0), App("tiny", 100, 0)]);
        usage.Save(file);

        var loaded = new UsageHistory();
        loaded.Load(file);
        var day = DateOnly.FromDateTime(past);
        var rows = loaded.Range(day, day);
        Assert.Contains(rows, r => r.Key == "big" && r.Down == 10_000_000);
        Assert.DoesNotContain(rows, r => r.Key == "tiny");
        Assert.Contains(rows, r => r.Key == TrafficMeter.Other && r.Down == 100);
    }

    [Fact]
    public void A_damaged_file_is_ignored()
    {
        var file = Path.Combine(_dir, "usage.json");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(file, "{ not json");
        var usage = new UsageHistory();
        usage.Load(file);
        Assert.Equal(0, usage.Days);
    }

    [Fact]
    public void Range_buttons_cover_the_expected_dates()
    {
        var now = new DateTime(2026, 5, 18, 10, 0, 0);
        var today = new DateOnly(2026, 5, 18);
        Assert.Equal((today, today), UsagePage.Bounds(UsagePage.Span.Today, now, null));
        Assert.Equal((today.AddDays(-1), today.AddDays(-1)), UsagePage.Bounds(UsagePage.Span.Yesterday, now, null));
        Assert.Equal((today.AddDays(-6), today), UsagePage.Bounds(UsagePage.Span.Week, now, null));
        // Persian months by default: 1405/02/28 started on 1405/02/01 = 2026-04-21.
        Assert.Equal((new DateOnly(2026, 4, 21), today), UsagePage.Bounds(UsagePage.Span.ThisMonth, now, null));
        Assert.Equal((new DateOnly(2026, 2, 3), today), UsagePage.Bounds(UsagePage.Span.All, now, new DateOnly(2026, 2, 3)));
    }
}
