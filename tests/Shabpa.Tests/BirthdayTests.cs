using System.IO;
using System.Text.Json;
using Shabpa.Core;

namespace Shabpa.Tests;

public class BirthdayTests
{
    private static BirthdaySettings J(int month, int day, int year = 0) => new() { Jalali = true, Month = month, Day = day, Year = year };
    private static BirthdaySettings G(int month, int day, int year = 0) => new() { Jalali = false, Month = month, Day = day, Year = year };

    [Fact]
    public void Fifteenth_of_Aban_1405_is_6_November_2026()
    {
        var b = J(8, 15);
        Assert.True(Birthday.IsToday(b, new DateTime(2026, 11, 6, 9, 0, 0)));
        Assert.False(Birthday.IsToday(b, new DateTime(2026, 11, 5, 23, 59, 0)));
        Assert.False(Birthday.IsToday(b, new DateTime(2026, 11, 7, 0, 0, 1)));
    }

    [Fact]
    public void Next_birthday_is_today_or_later_and_rolls_to_next_year()
    {
        var b = J(8, 15);
        Assert.Equal(new DateTime(2026, 11, 6), Birthday.Next(b, new DateTime(2026, 11, 6, 22, 0, 0)));
        Assert.Equal(new DateTime(2026, 11, 6), Birthday.Next(b, new DateTime(2026, 10, 1)));
        var next = Birthday.Next(b, new DateTime(2026, 11, 7))!.Value;
        Assert.True(next > new DateTime(2027, 11, 1) && next < new DateTime(2027, 11, 30));
        Assert.Equal(15, Birthday.DayOf(true, next));
    }

    [Fact]
    public void Esfand_30_falls_on_the_last_day_in_a_common_year()
    {
        var b = J(12, 30);
        Assert.Equal(30, Birthday.DaysIn(true, 1403, 12));
        Assert.Equal(29, Birthday.DaysIn(true, 1404, 12));
        var common = Birthday.Occurrence(b, 1404)!.Value;
        Assert.Equal(29, Birthday.DayOf(true, common));
        Assert.Equal(12, Birthday.MonthOf(true, common));
        var leap = Birthday.Occurrence(b, 1403)!.Value;
        Assert.Equal(30, Birthday.DayOf(true, leap));
        Assert.True(Birthday.IsToday(b, common));
    }

    [Fact]
    public void Feb_29_is_celebrated_on_Feb_28_in_non_leap_years()
    {
        var b = G(2, 29);
        Assert.Equal(new DateTime(2027, 2, 28), Birthday.Occurrence(b, 2027));
        Assert.Equal(new DateTime(2028, 2, 29), Birthday.Occurrence(b, 2028));
        Assert.True(Birthday.IsToday(b, new DateTime(2027, 2, 28, 12, 0, 0)));
        Assert.False(Birthday.IsToday(b, new DateTime(2027, 3, 1)));
    }

    [Fact]
    public void Year_is_optional_and_validated()
    {
        Assert.True(Birthday.YearOk(G(5, 10)));
        Assert.True(Birthday.YearOk(G(5, 10, 1990)));
        Assert.False(Birthday.YearOk(G(2, 29, 2001)));
        Assert.True(Birthday.YearOk(G(2, 29, 2000)));
        Assert.False(Birthday.YearOk(J(12, 30, 1404)));
        Assert.True(Birthday.YearOk(J(12, 30, 1403)));
        Assert.False(Birthday.YearOk(G(5, 10, 3000)));
    }

    [Fact]
    public void Month_lengths_are_validated()
    {
        Assert.False(J(7, 31).HasDate);
        Assert.True(J(6, 31).HasDate);
        Assert.False(G(4, 31).HasDate);
        Assert.True(G(2, 29).HasDate);
        Assert.False(G(2, 30).HasDate);
        Assert.False(G(13, 1).HasDate);
        Assert.False(new BirthdaySettings().HasDate);
    }

    [Fact]
    public void Switching_calendar_keeps_the_same_day()
    {
        var b = J(8, 15);
        Birthday.Convert(b, false, new DateTime(2026, 10, 1));
        Assert.False(b.Jalali);
        Assert.Equal((11, 6), (b.Month, b.Day));
        Birthday.Convert(b, true, new DateTime(2026, 10, 1));
        Assert.Equal((8, 15), (b.Month, b.Day));
    }

    [Fact]
    public void Greeting_is_due_once_per_day_and_the_look_follows_the_day()
    {
        var b = J(8, 15);
        var day = new DateTime(2026, 11, 6, 8, 0, 0);
        Assert.True(Birthday.GreetingDue(b, day));
        b.LastGreeted = Birthday.Key(day);
        Assert.False(Birthday.GreetingDue(b, day.AddHours(10)));
        Assert.True(Birthday.LookFor(b, day));
        b.Theme = false;
        Assert.False(Birthday.LookFor(b, day));
        Assert.False(Birthday.IsToday(b, Birthday.NextMidnight(day)));
        Assert.Equal(new DateTime(2026, 11, 7), Birthday.NextMidnight(day));
    }

    [Fact]
    public void Names_are_trimmed_limited_and_any_script()
    {
        Assert.Equal("علی", Birthday.CleanName("  علی \t"));
        Assert.Equal("Ann Lee", Birthday.CleanName("Ann \n  Lee"));
        Assert.Equal(30, Birthday.CleanName(new string('x', 80)).Length);
        Assert.Equal("", Birthday.CleanName("   "));
        Assert.Equal("田中", Birthday.CleanName("田中"));
    }

    [Fact]
    public void Greeting_texts_are_gender_neutral_and_handle_no_name()
    {
        Assert.Equal("تولدت مبارک امیر 🎂", Birthday.Title("امیر", false));
        Assert.Equal("تولدت مبارک 🎂", Birthday.Title("", false));
        Assert.Equal("Happy birthday, Ali!", Birthday.Title("Ali", true));
        Assert.Equal("Happy birthday!", Birthday.Title(" ", true));
    }

    [Fact]
    public void Persian_digits_are_read()
    {
        Assert.Equal("26", Birthday.LatinDigits("۲۶"));
        Assert.Equal("1405", Birthday.LatinDigits("١٤٠٥"));
        Assert.Equal("7", Birthday.LatinDigits("a7"));
    }

    [Fact]
    public void The_festive_look_keeps_the_real_theme_key()
    {
        foreach (var p in Palette.All)
        {
            var t = Birthday.Tint(p);
            Assert.Equal(p.Key, t.BaseKey);
            Assert.Equal(p.Light, t.Light);
            Assert.NotEqual(p.Key, t.Key);
            foreach (var key in Palette.Keys) Assert.True(t.Colors.ContainsKey(key), key);
            Assert.Same(t, Birthday.Tint(t));
            _ = GlassTokens.Of(t);
        }
    }

    [Fact]
    public void Birthday_round_trips_through_the_state_file()
    {
        var folder = Path.Combine(Path.GetTempPath(), "shabpa-bday-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new Store(folder);
            store.State.Settings.Birthday = new BirthdaySettings { Name = "Ali", Jalali = true, Month = 8, Day = 15, Year = 1375, Theme = false, LastGreeted = "2026-11-06" };
            store.Save();
            var loaded = new Store(folder);
            loaded.Load();
            var b = loaded.State.Settings.Birthday;
            Assert.Equal(("Ali", true, 8, 15, 1375, false, "2026-11-06"), (b.Name, b.Jalali, b.Month, b.Day, b.Year, b.Theme, b.LastGreeted));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void Old_state_files_without_a_birthday_load_with_safe_defaults()
    {
        var state = JsonSerializer.Deserialize<AppState>("{\"Settings\":{\"Tariff\":321,\"Theme\":\"night\"}}")!;
        Assert.Equal(321, state.Settings.Tariff);
        var b = state.Settings.Birthday;
        Assert.True(b.IsEmpty);
        Assert.True(b.Theme);
        Assert.Equal("", b.LastGreeted);
    }

    [Fact]
    public void Corrupt_birthday_values_are_ignored_without_losing_the_rest()
    {
        const string json = "{\"Settings\":{\"Tariff\":555,\"Birthday\":{\"Name\":123,\"Month\":\"abc\",\"Day\":99,\"Year\":\"x\",\"Theme\":\"yes\",\"Jalali\":false,\"LastGreeted\":\"garbage\"}}}";
        var state = JsonSerializer.Deserialize<AppState>(json)!;
        Assert.Equal(555, state.Settings.Tariff);
        var b = state.Settings.Birthday;
        Assert.False(b.HasDate);
        Assert.Equal("", b.Name);
        Assert.True(b.Theme);
        Assert.Equal("", b.LastGreeted);
        Assert.Equal(0, b.Month);

        var notAnObject = JsonSerializer.Deserialize<AppState>("{\"Settings\":{\"Birthday\":[1,2]}}")!;
        Assert.True(notAnObject.Settings.Birthday.IsEmpty);
    }

    [Fact]
    public void An_impossible_year_is_dropped_but_the_date_is_kept()
    {
        var state = JsonSerializer.Deserialize<AppState>("{\"Settings\":{\"Birthday\":{\"Jalali\":true,\"Month\":12,\"Day\":30,\"Year\":1404}}}")!;
        var b = state.Settings.Birthday;
        Assert.True(b.HasDate);
        Assert.Equal(0, b.Year);
    }
}
