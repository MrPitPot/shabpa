using System.Text.Json;
using Shabpa.Core;

namespace Shabpa.Tests;

public class MedalsTests
{
    private static readonly DateTime Noon = new(2026, 11, 3, 12, 0, 0);
    private static MedalFacts F(Func<MedalFacts, MedalFacts>? with = null)
    {
        var f = new MedalFacts { Now = Noon, PagesKnown = 13 };
        return with is null ? f : with(f);
    }

    [Fact]
    public void Ids_are_unique_and_every_medal_has_a_rule_and_an_icon()
    {
        Assert.Equal(Medals.All.Count, Medals.All.Select(m => m.Id).Distinct().Count());
        var everything = new MedalFacts
        {
            Now = new DateTime(2026, 10, 31, 0, 0, 0), Games = 99, GamesPlayed = 9, LongestGameMinutes = 999, GameHours = 99, GameAtNight = true,
            TasksDone = 99, FocusSessions = 99, FocusHours = 99, PagesSeen = 13, PagesKnown = 13, DownloadPlans = 1, Scheduled = 1, Macros = 1,
            Backups = 1, Days = 999, TourDone = true, Birthday = true, Halloween = true,
            Counters = new Dictionary<string, int> { [Medals.CleanCounter] = 99, [Medals.SettingsCounter] = 1, [Medals.OverlayCounter] = 1, [Medals.AssistantCounter] = 1 },
        };
        var all = Medals.Due(everything, new Dictionary<string, DateTime>());
        Assert.Equal(Medals.All.Select(m => m.Id), all); // each rule can be met, in catalogue order
        Assert.All(Medals.All, m => Assert.False(string.IsNullOrWhiteSpace(m.Icon)));
    }

    [Fact]
    public void A_fresh_install_earns_only_the_hello_medal()
    {
        Assert.Equal(["hello"], Medals.Due(F(), new Dictionary<string, DateTime>()));
    }

    [Fact]
    public void Earned_medals_are_never_given_twice()
    {
        var earned = new Dictionary<string, DateTime> { ["hello"] = Noon, ["game1"] = Noon };
        Assert.Equal(["game10"], Medals.Due(F(f => f with { Games = 10 }), earned));
    }

    [Fact]
    public void Thresholds_are_exact()
    {
        var none = new Dictionary<string, DateTime> { ["hello"] = Noon };
        Assert.DoesNotContain("marathon", Medals.Due(F(f => f with { Games = 1, LongestGameMinutes = 179.9 }), none));
        Assert.Contains("marathon", Medals.Due(F(f => f with { Games = 1, LongestGameMinutes = 180 }), none));
        Assert.DoesNotContain("curious", Medals.Due(F(f => f with { PagesSeen = 4 }), none));
        Assert.Contains("curious", Medals.Due(F(f => f with { PagesSeen = 5 }), none));
        Assert.DoesNotContain("explorer", Medals.Due(F(f => f with { PagesSeen = 12 }), none));
        Assert.Contains("explorer", Medals.Due(F(f => f with { PagesSeen = 13 }), none));
    }

    [Fact]
    public void Midnight_only_at_minute_zero()
    {
        var none = new Dictionary<string, DateTime> { ["hello"] = Noon };
        Assert.Contains("midnight", Medals.Due(F(f => f with { Now = new DateTime(2026, 11, 3, 0, 0, 40) }), none));
        Assert.DoesNotContain("midnight", Medals.Due(F(f => f with { Now = new DateTime(2026, 11, 3, 0, 1, 0) }), none));
        Assert.DoesNotContain("midnight", Medals.Due(F(f => f with { Now = new DateTime(2026, 11, 3, 12, 0, 0) }), none));
    }

    [Fact]
    public void Collector_counts_medals_found_in_the_same_pass()
    {
        var earned = Medals.All.Take(13).ToDictionary(m => m.Id, _ => Noon);
        Assert.DoesNotContain("collector", Medals.Due(F(), earned));
        var due = Medals.Due(F(f => f with { Days = 7, Counters = new Dictionary<string, int> { [Medals.CleanCounter] = 1 } }), earned);
        Assert.Contains("collector", due);
        Assert.Equal("collector", due[^1]);
    }

    [Fact]
    public void Days_count_once_per_calendar_day()
    {
        var s = new MedalState();
        Assert.True(Medals.TouchDay(s, new DateTime(2026, 11, 3, 8, 0, 0)));
        Assert.False(Medals.TouchDay(s, new DateTime(2026, 11, 3, 23, 59, 0)));
        Assert.True(Medals.TouchDay(s, new DateTime(2026, 11, 4, 0, 0, 1)));
        Assert.Equal(2, s.Days);
    }

    [Fact]
    public void Old_state_files_without_medals_still_load()
    {
        var state = JsonSerializer.Deserialize<AppState>("{\"Settings\":{}}")!;
        Assert.NotNull(state.Medals);
        Assert.Empty(state.Medals.Earned);
        Assert.False(state.Medals.Primed);
        Assert.True(state.Settings.MedalToasts);
        Assert.True(state.Settings.MedalSound);
    }

    [Fact]
    public void The_jingle_is_short_and_never_clips()
    {
        var samples = UiSound.Jingle();
        Assert.InRange(samples.Length / 44100.0, 0.5, 1.2);
        Assert.All(samples, v => Assert.InRange(v, -1, 1));
    }
}
