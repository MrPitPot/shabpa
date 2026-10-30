using Shabpa.Core;
using Xunit;

namespace Shabpa.Tests;

public class GreetingVarietyTests
{
    private static readonly DateTime Evening = new(2026, 11, 14, 19, 30, 0);   // a Saturday in autumn

    [Fact]
    public void Every_part_of_the_day_has_many_lines_in_both_languages_with_the_same_count()
    {
        foreach (var part in Enum.GetValues<Greetings.Part>())
        {
            Assert.True(Greetings.Persian[part].Length >= 20, $"{part}: {Greetings.Persian[part].Length}");
            Assert.Equal(Greetings.Persian[part].Length, Greetings.English[part].Length);
            Assert.Equal(Greetings.Persian[part].Length, Greetings.Persian[part].Distinct().Count());
            Assert.Equal(Greetings.English[part].Length, Greetings.English[part].Distinct().Count());
        }
    }

    [Theory]
    [InlineData(3, 20, Season.Winter)]
    [InlineData(3, 21, Season.Spring)]
    [InlineData(6, 20, Season.Spring)]
    [InlineData(6, 21, Season.Summer)]
    [InlineData(9, 22, Season.Summer)]
    [InlineData(9, 23, Season.Autumn)]
    [InlineData(12, 20, Season.Autumn)]
    [InlineData(12, 21, Season.Winter)]
    [InlineData(1, 15, Season.Winter)]
    [InlineData(3, 19, Season.Winter)]
    public void Seasons_follow_the_iranian_calendar_dates(int month, int day, Season expected) =>
        Assert.Equal(expected, GreetingLines.SeasonOf(new DateTime(2026, month, day)));

    [Fact]
    public void Each_season_and_each_weekday_has_lines_in_both_languages_with_the_same_count()
    {
        foreach (var s in Enum.GetValues<Season>())
        {
            var (fa, en) = GreetingLines.Seasons[s];
            Assert.True(fa.Length >= 10);
            Assert.Equal(fa.Length, en.Length);
            Assert.Equal(fa.Length, fa.Distinct().Count());
        }
        foreach (var d in Enum.GetValues<DayOfWeek>())
        {
            var (fa, en) = GreetingLines.Weekdays[d];
            Assert.True(fa.Length >= 4);
            Assert.Equal(fa.Length, en.Length);
        }
        foreach (var (key, (fa, en)) in GreetingLines.Special)
        {
            Assert.True(fa.Length >= 5, key);
            Assert.Equal(fa.Length, en.Length);
        }
    }

    [Theory]
    [InlineData(3, 21, "nowruz")]
    [InlineData(12, 20, "yalda")]
    [InlineData(12, 31, "newyear")]
    [InlineData(1, 1, "newyear")]
    [InlineData(10, 31, "halloween")]
    [InlineData(5, 5, null)]
    public void Special_days_are_found(int month, int day, string? expected) => Assert.Equal(expected, GreetingLines.SpecialDay(new DateTime(2026, month, day)));

    [Fact]
    public void Without_any_game_the_pool_is_the_hour_the_season_and_the_weekday()
    {
        var kinds = Greetings.Pools(Evening, GreetingContext.None, english: false).Select(p => p.kind).ToList();
        Assert.Equal(["time", "season", "weekday"], kinds);
    }

    [Fact]
    public void A_running_game_brings_lines_about_it_with_its_name_filled_in()
    {
        var ctx = new GreetingContext("Elden Ring", "", null, 45, 0);
        foreach (var english in new[] { false, true })
        {
            var pools = Greetings.Pools(Evening, ctx, english);
            Assert.Contains(pools, p => p.kind == "playing");
            Assert.Contains(pools, p => p.kind == "game");
            var playing = pools.First(p => p.kind == "playing").lines;
            Assert.All(playing, line => { Assert.Contains("Elden Ring", line); Assert.DoesNotContain("{0}", line); });
        }
    }

    [Fact]
    public void A_game_just_logged_brings_lines_about_it_and_a_hot_run_brings_a_cleaning_hint()
    {
        var ctx = new GreetingContext("", "Valorant", TimeSpan.FromMinutes(20), 150, 2, LastMaxGpuTemp: 88, LastMinutes: 70);
        var kinds = Greetings.Pools(Evening, ctx, false).Select(p => p.kind).ToList();
        Assert.Contains("after", kinds);
        Assert.Contains("game", kinds);
        Assert.Contains("longday", kinds);
        Assert.Contains("hot", kinds);
        Assert.All(Greetings.Pools(Evening, ctx, true).First(p => p.kind == "after").lines, l => Assert.Contains("Valorant", l));
        Assert.Contains("3", Greetings.Pools(Evening, ctx, true).First(p => p.kind == "longday").lines[0]);     // 150 minutes round to three hours
    }

    [Fact]
    public void A_game_played_long_ago_is_not_mentioned()
    {
        var ctx = new GreetingContext("", "Valorant", TimeSpan.FromDays(2), 0, 0);
        Assert.DoesNotContain(Greetings.Pools(Evening, ctx, false), p => p.kind is "after" or "game");
    }

    [Fact]
    public void A_special_day_is_in_the_pool_and_heavily_weighted()
    {
        var pools = Greetings.Pools(new DateTime(2026, 12, 20, 21, 0, 0), GreetingContext.None, false);
        var special = pools.Single(p => p.kind == "special");
        Assert.True(special.weight > pools.Where(p => p.kind != "special").Max(p => p.weight));
    }

    [Theory]
    [InlineData("Valorant", true)]
    [InlineData("Counter-Strike 2", true)]
    [InlineData("ELDEN RING", true)]
    [InlineData("Some Indie Game", false)]
    public void Well_known_games_get_their_own_lines(string game, bool known)
    {
        Assert.Equal(known, Greetings.PerGameLines(game, false).Length > 0);
        Assert.Equal(Greetings.PerGameLines(game, false).Length, Greetings.PerGameLines(game, true).Length);
    }

    [Fact]
    public void Over_many_draws_every_kind_of_line_shows_up_and_nothing_is_empty()
    {
        var ctx = new GreetingContext("Minecraft", "Valorant", TimeSpan.FromMinutes(30), 200, 3, LastMaxGpuTemp: 90);
        var seen = new HashSet<string>();
        var lines = new HashSet<string>();
        for (var seed = 0; seed < 600; seed++)
        {
            var line = Greetings.Pick(Evening.AddMinutes(seed), ctx, new Random(seed));      // another minute never changes the key; the key is fixed so reset below
            Greetings.Reset();
            Assert.False(string.IsNullOrWhiteSpace(line));
            Assert.DoesNotContain("{0}", line);
            lines.Add(line);
        }
        foreach (var pool in Greetings.Pools(Evening, ctx, false)) seen.Add(pool.kind);
        Assert.True(lines.Count > 40, $"only {lines.Count} different lines in 600 draws");
        Assert.True(seen.Count >= 6);
    }

    [Fact]
    public void The_line_stays_while_nothing_it_depends_on_changes_and_changes_when_the_game_does()
    {
        Greetings.Reset();
        var ctx = new GreetingContext("", "", null, 0, 0);
        var a = Greetings.Pick(Evening, ctx, new Random(1));
        Assert.Equal(a, Greetings.Pick(Evening.AddMinutes(10), ctx, new Random(2)));
        var playing = new GreetingContext("Valorant", "", null, 10, 0);
        var found = Enumerable.Range(0, 40).Select(i => { Greetings.Reset(); return Greetings.Pick(Evening, playing, new Random(i)); }).ToList();
        Assert.Contains(found, l => l.Contains("Valorant", StringComparison.OrdinalIgnoreCase) || l.Contains("والورنت"));
        Greetings.Reset();
    }

    [Fact]
    public void The_context_is_made_from_the_game_log()
    {
        var now = new DateTime(2026, 11, 14, 22, 0, 0);
        var history = new List<GameLogEntry>
        {
            new() { Game = "Old Game", Start = now.AddDays(-3), Minutes = 60, MaxGpuTemp = 70 },
            new() { Game = "Dota 2", Start = now.AddHours(-3), Minutes = 90, MaxGpuTemp = 85 },
        };
        var c = GreetingContext.From(history, "", null, now);
        Assert.Equal(("Dota 2", 90, 1), (c.Last, c.TodayMinutes, c.GamesToday));
        Assert.Equal(TimeSpan.FromMinutes(90), c.SinceLast);
        Assert.True(c.PlayedRecently);
        var playing = GreetingContext.From(history, "CS2", now.AddMinutes(-30), now);
        Assert.Equal(120, playing.TodayMinutes);
        Assert.True(playing.Playing);
        Assert.False(playing.PlayedRecently);
        Assert.Equal(GreetingContext.None.Last, GreetingContext.From([], "", null, now).Last);
    }
}
