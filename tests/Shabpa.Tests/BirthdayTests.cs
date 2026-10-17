using Shabpa.Core;
using Xunit;

namespace Shabpa.Tests;

public class BirthdayTests
{
    [Fact]
    public void Maker_birthday_is_26_Mehr()
    {
        Assert.Equal(Birthday.Kind.Maker, Birthday.On(new DateTime(2026, 10, 18, 9, 0, 0), ""));
        Assert.Equal(Birthday.Kind.Maker, Birthday.On(new DateTime(2027, 10, 18), "")); // 26 Mehr 1406
        Assert.Equal(Birthday.Kind.None, Birthday.On(new DateTime(2026, 10, 17), ""));
    }

    [Fact]
    public void Own_birthday_wins_and_round_trips()
    {
        var mine = Birthday.Format(true, 7, 26);
        Assert.True(Birthday.TryParse(mine, out var jalali, out var month, out var day));
        Assert.True(jalali);
        Assert.Equal((7, 26), (month, day));
        Assert.Equal(Birthday.Kind.Mine, Birthday.On(new DateTime(2026, 10, 18), mine));
        Assert.Equal(Birthday.Kind.Mine, Birthday.On(new DateTime(2026, 3, 5), Birthday.Format(false, 3, 5)));
    }

    [Fact]
    public void Bad_values_are_ignored()
    {
        Assert.False(Birthday.TryParse("", out _, out _, out _));
        Assert.False(Birthday.TryParse("fa:7-31", out _, out _, out _)); // Mehr has 30 days
        Assert.False(Birthday.TryParse("en:2-30", out _, out _, out _));
        Assert.False(Birthday.TryParse("xx:1-1", out _, out _, out _));
    }

    [Fact]
    public void Missing_days_fall_on_the_month_end()
    {
        Assert.True(Birthday.Matches(new DateTime(2027, 2, 28), false, 2, 29));
        Assert.False(Birthday.Matches(new DateTime(2028, 2, 28), false, 2, 29)); // a leap year has the 29th
    }

    [Fact]
    public void Party_plays_once_a_day()
    {
        var s = new Settings();
        var day = new DateTime(2026, 10, 18, 20, 0, 0);
        Assert.Equal(Birthday.Kind.Maker, Birthday.Due(day, s));
        Birthday.MarkShown(day, s);
        Assert.Equal(Birthday.Kind.None, Birthday.Due(day.AddHours(2), s));
    }
}
