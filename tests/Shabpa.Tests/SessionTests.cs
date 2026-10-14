using Shabpa.Core;

namespace Shabpa.Tests;

public class GameSessionTests
{
    private static readonly DateTime T0 = new(2026, 5, 15, 21, 0, 0);

    private static TelemetrySample Sample(int seconds, bool inGame = true, string game = "cs2", double fps = 144, double low = 100, double? ping = 30, double temp = 70)
        => new()
        {
            Time = T0.AddSeconds(seconds), InGame = inGame, Game = inGame ? game : "", Fps = inGame ? fps : 0, Low1Fps = inGame ? low : 0, PingMs = ping,
            Gpu = new GpuReading { TempC = temp, HotspotC = temp + 15, LoadPercent = 90 },
        };

    [Fact]
    public void A_long_session_produces_one_report_when_the_game_ends()
    {
        var tracker = new GameSessionTracker();
        GameReport? report = null;
        for (var s = 0; s <= 600; s += 5)
        {
            var result = tracker.Update(Sample(s, fps: 100 + s % 40, temp: 60 + s / 20.0, ping: s == 300 ? 140 : 30), 1000 + s * 1000, 500 + s * 100);
            Assert.Null(result);
        }
        report = tracker.Update(Sample(605, inGame: false), 0, 0);

        Assert.NotNull(report);
        Assert.Equal("cs2", report!.Game);
        Assert.Equal(TimeSpan.FromSeconds(605), report.Duration);
        Assert.InRange(report.AvgFps, 100, 140);
        Assert.Equal(140, report.MaxPing);
        Assert.Equal(90, report.MaxGpuTemp, 0);
        Assert.Equal(600 * 1000, report.DownBytes);
        Assert.False(tracker.InSession);
    }

    [Fact]
    public void Short_sessions_are_not_reported()
    {
        var tracker = new GameSessionTracker();
        for (var s = 0; s < 60; s += 5) tracker.Update(Sample(s), 0, 0);
        Assert.Null(tracker.Update(Sample(65, inGame: false), 0, 0));
    }

    [Fact]
    public void Switching_games_closes_the_first_session()
    {
        var tracker = new GameSessionTracker();
        for (var s = 0; s <= 400; s += 10) tracker.Update(Sample(s, game: "cs2"), 0, 0);
        var report = tracker.Update(Sample(410, game: "valorant"), 0, 0);
        Assert.Equal("cs2", report?.Game);
        Assert.True(tracker.InSession);
    }
}

public class DataBudgetTests
{
    [Theory]
    [InlineData(10, 100, 0)]
    [InlineData(79, 100, 0)]
    [InlineData(80, 100, 1)]
    [InlineData(99, 100, 1)]
    [InlineData(100, 100, 2)]
    [InlineData(150, 100, 2)]
    public void Warning_levels(double usedGb, double limitGb, int level)
        => Assert.Equal(level, DataBudget.Evaluate((long)(usedGb * 1073741824), limitGb).Level);

    [Fact]
    public void No_limit_means_no_warning()
    {
        var check = DataBudget.Evaluate(500L << 30, 0);
        Assert.Equal(0, check.Level);
        Assert.Equal(500, check.UsedGb, 3);
    }

    [Fact]
    public void Month_start_follows_the_calendar_the_user_sees()
    {
        var day = new DateTime(2026, 5, 18, 14, 30, 0);
        Assert.Equal(new DateTime(2026, 4, 21), Fa.MonthStart(day));
        L.En = true;
        try { Assert.Equal(new DateTime(2026, 5, 1), Fa.MonthStart(day)); }
        finally { L.En = false; }
    }
}
