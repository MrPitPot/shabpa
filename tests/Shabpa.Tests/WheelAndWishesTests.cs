using System.Text.RegularExpressions;
using Shabpa.Kit;

namespace Shabpa.Tests;

public class WheelAndWishesTests
{
    // ---- wheel maths ----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(-2.4)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(0.5)]
    [InlineData(1)]
    [InlineData(3)]
    public void A_click_maps_back_to_the_row_drawn_there(double rows)
    {
        var (y, _, _) = WheelMath.Pose(rows, 36);
        Assert.Equal(rows, WheelMath.RowAtOffset(y, 36), 6);
    }

    [Fact]
    public void A_row_three_places_out_is_not_picked_as_the_second()
    {
        var (y, _, _) = WheelMath.Pose(3, 36);
        Assert.Equal(3, WheelMath.Nearest(WheelMath.RowAtOffset(y, 36), 50));
        Assert.NotEqual(3, WheelMath.Nearest(y / 36, 50)); // the old linear maths missed it
    }

    [Fact]
    public void Clamp_and_step_stay_inside_the_list()
    {
        Assert.Equal(0, WheelMath.Clamp(-3, 10));
        Assert.Equal(9, WheelMath.Clamp(40, 10));
        Assert.Equal(0, WheelMath.Clamp(5, 0));
        Assert.Equal(4, WheelMath.Step(3, 1, 10));
        Assert.Equal(0, WheelMath.Step(0, -1, 10));
        Assert.Equal(9, WheelMath.Step(8, 5, 10));
    }

    [Fact]
    public void Nearest_snaps_to_the_closest_row()
    {
        Assert.Equal(3, WheelMath.Nearest(2.6, 12));
        Assert.Equal(2, WheelMath.Nearest(2.4, 12));
        Assert.Equal(0, WheelMath.Nearest(-0.7, 12));
        Assert.Equal(11, WheelMath.Nearest(14.2, 12));
    }

    [Fact]
    public void Fling_goes_further_with_speed_and_stops_at_the_ends()
    {
        Assert.Equal(5, WheelMath.Fling(5.1, 0, 31));
        Assert.True(WheelMath.Fling(5, 20, 31) > WheelMath.Fling(5, 5, 31));
        Assert.True(WheelMath.Fling(15, -20, 31) < 15);
        Assert.Equal(30, WheelMath.Fling(28, 500, 31));
        Assert.Equal(0, WheelMath.Fling(2, -500, 31));
    }

    [Fact]
    public void Rubber_band_gives_way_only_a_little_past_the_ends()
    {
        Assert.Equal(4.0, WheelMath.Rubber(4.0, 10));
        Assert.InRange(WheelMath.Rubber(-3, 10), -0.9, 0);
        Assert.InRange(WheelMath.Rubber(14, 10), 9, 9.9);
        Assert.Equal(0, WheelMath.Rubber(-0.0, 10));
    }

    [Fact]
    public void Drum_pose_is_big_in_the_middle_and_fades_towards_the_edges()
    {
        var (y0, s0, o0) = WheelMath.Pose(0, 38);
        Assert.Equal(0, y0);
        Assert.Equal(1, s0);
        Assert.Equal(1, o0);
        var (y1, s1, o1) = WheelMath.Pose(1, 38);
        var (y2, s2, o2) = WheelMath.Pose(2, 38);
        Assert.True(y1 > 30 && y1 < 38.5);
        Assert.True(y2 > y1 && s2 < s1 && s1 < 1 && o2 < o1 && o1 < 1);
        var (yUp, _, _) = WheelMath.Pose(-1, 38);
        Assert.Equal(-y1, yUp, 6);
        Assert.Equal(0, WheelMath.Pose(3.6, 38).opacity); // round the back of the drum
    }

    [Fact]
    public void Emphasis_is_full_in_the_middle_and_gone_half_a_row_away()
    {
        Assert.Equal(1, WheelMath.Emphasis(0));
        Assert.Equal(0, WheelMath.Emphasis(0.5));
        Assert.Equal(0, WheelMath.Emphasis(-2));
        Assert.Equal(0.5, WheelMath.Emphasis(0.25), 6);
    }

    // ---- days of the month ----------------------------------------------------------------------------------

    [Theory]
    [InlineData(true, 1, 31)]
    [InlineData(true, 6, 31)]
    [InlineData(true, 7, 30)]
    [InlineData(true, 12, 30)]
    [InlineData(false, 1, 31)]
    [InlineData(false, 2, 29)]
    [InlineData(false, 4, 30)]
    [InlineData(false, 12, 31)]
    public void Without_a_year_the_longest_month_length_is_offered(bool jalali, int month, int expected) =>
        Assert.Equal(expected, WheelMath.DayCount(jalali, month, 0));

    [Fact]
    public void With_a_year_Esfand_and_February_follow_the_leap_rules()
    {
        Assert.Equal(30, WheelMath.DayCount(true, 12, 1403));
        Assert.Equal(29, WheelMath.DayCount(true, 12, 1404));
        Assert.Equal(29, WheelMath.DayCount(false, 2, 2028));
        Assert.Equal(28, WheelMath.DayCount(false, 2, 2027));
        Assert.Equal(31, WheelMath.DayCount(true, 1, 1404));
    }

    [Fact]
    public void Odd_input_does_not_throw()
    {
        Assert.Equal(31, WheelMath.DayCount(true, 0, 1400));
        Assert.Equal(31, WheelMath.DayCount(false, 13, 2000));
        Assert.Equal(Birthday.MaxDay(true, 5), WheelMath.DayCount(true, 5, 99999)); // year out of the calendar's range
    }

    [Fact]
    public void Day_is_clamped_when_the_month_gets_shorter()
    {
        Assert.Equal(29, WheelMath.ClampDay(31, 29));
        Assert.Equal(1, WheelMath.ClampDay(0, 30));
        Assert.Equal(15, WheelMath.ClampDay(15, 31));
    }

    // ---- years -----------------------------------------------------------------------------------------------

    [Fact]
    public void Year_wheel_starts_with_no_year_then_newest_first()
    {
        var now = new DateTime(2026, 10, 18);
        var (lo, hi) = WheelMath.YearRange(true, now);
        Assert.Equal(1405, hi);
        Assert.Equal(1280, lo);
        Assert.Equal(hi - lo + 2, WheelMath.YearCount(lo, hi));
        Assert.Equal(0, WheelMath.YearAt(0, hi));
        Assert.Equal(1405, WheelMath.YearAt(1, hi));
        Assert.Equal(1404, WheelMath.YearAt(2, hi));
        Assert.Equal(lo, WheelMath.YearAt(WheelMath.YearCount(lo, hi) - 1, hi));
    }

    [Fact]
    public void Year_and_row_convert_both_ways()
    {
        var (lo, hi) = WheelMath.YearRange(false, new DateTime(2026, 10, 18));
        Assert.Equal(2026, hi);
        for (var year = lo; year <= hi; year += 7) Assert.Equal(year, WheelMath.YearAt(WheelMath.YearIndex(year, lo, hi), hi));
        Assert.Equal(0, WheelMath.YearIndex(0, lo, hi));
        Assert.Equal(0, WheelMath.YearIndex(1700, lo, hi));
    }

    [Fact]
    public void A_saved_year_outside_the_usual_range_widens_the_wheel()
    {
        var (lo, hi) = WheelMath.YearRange(false, new DateTime(2026, 10, 18), 1850);
        Assert.Equal(1850, lo);
        Assert.Equal(1850, WheelMath.YearAt(WheelMath.YearIndex(1850, lo, hi), hi));
    }

    // ---- wishes ----------------------------------------------------------------------------------------------

    [Fact]
    public void There_are_enough_wishes_in_both_languages()
    {
        Assert.InRange(BirthdayWishes.All.Count, 12, 20);
        Assert.True(BirthdayWishes.All.Count(w => w.UsesName) >= 5);
        Assert.True(BirthdayWishes.All.Count(w => !w.UsesName) >= 5);
        Assert.Equal(BirthdayWishes.All.Count, BirthdayWishes.All.Select(w => w.Fa).Distinct().Count());
        Assert.Equal(BirthdayWishes.All.Count, BirthdayWishes.All.Select(w => w.En).Distinct().Count());
        foreach (var w in BirthdayWishes.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(w.Fa));
            Assert.False(string.IsNullOrWhiteSpace(w.En));
            Assert.Equal(w.Fa.Contains("{name}"), w.En.Contains("{name}")); // both languages agree on whether the name is used
            Assert.True(w.Fa.Length <= 110 && w.En.Length <= 110, w.En);
        }
    }

    [Fact]
    public void No_wish_says_anything_about_age()
    {
        foreach (var w in BirthdayWishes.All)
        {
            Assert.DoesNotMatch(@"[0-9۰-۹٠-٩]", w.Fa + w.En);
            Assert.DoesNotContain("سالگی", w.Fa);
            Assert.DoesNotMatch(@"(?i)years? old|\bage[ds]?\b|turn(s|ing)?\b|birthday number", w.En);
        }
    }

    [Fact]
    public void The_same_day_always_gives_the_same_wish()
    {
        var day = new DateTime(2026, 10, 18, 7, 0, 0);
        var first = BirthdayWishes.Text("Ali", day, true);
        for (var i = 0; i < 22; i++) Assert.Equal(first, BirthdayWishes.Text("Ali", day.Date.AddHours(i + 1), true));
        Assert.Equal(BirthdayWishes.Text("علی", day, false), BirthdayWishes.Text("علی", day.AddMinutes(500), false));
    }

    [Fact]
    public void Different_days_give_a_variety_of_wishes()
    {
        var seen = new HashSet<string>();
        for (var i = 0; i < 120; i++) seen.Add(BirthdayWishes.For("Ali", new DateTime(2026, 1, 1).AddDays(i)).Fa);
        Assert.True(seen.Count >= 12, $"only {seen.Count} different wishes in 120 days");
    }

    [Fact]
    public void Without_a_name_no_placeholder_is_ever_shown()
    {
        for (var i = 0; i < 400; i++)
        {
            var day = new DateTime(2026, 1, 1).AddDays(i);
            Assert.DoesNotContain("{name}", BirthdayWishes.Text("", day, true));
            Assert.DoesNotContain("{name}", BirthdayWishes.Text(null, day, false));
            Assert.DoesNotContain("{name}", BirthdayWishes.Text("   ", day, false));
            Assert.False(BirthdayWishes.For("", day).UsesName);
        }
    }

    [Fact]
    public void The_name_is_filled_in_and_cleaned()
    {
        var withName = Enumerable.Range(0, 400).Select(i => new DateTime(2026, 1, 1).AddDays(i))
            .Where(d => BirthdayWishes.For("Ali", d).UsesName).ToList();
        Assert.NotEmpty(withName);
        foreach (var d in withName)
        {
            Assert.Contains("Ali", BirthdayWishes.Text("  Ali  ", d, true));
            Assert.Contains("علی", BirthdayWishes.Text("علی", d, false));
            Assert.DoesNotContain("{name}", BirthdayWishes.Text("Ali", d, false));
        }
    }

    [Fact]
    public void Wish_seed_is_stable_across_runs()
    {
        // FNV-1a of a fixed text: guards against switching to a randomised hash
        Assert.Equal(BirthdayWishes.Seed("2026-11-06"), BirthdayWishes.Seed("2026-11-06"));
        Assert.NotEqual(BirthdayWishes.Seed("2026-11-06"), BirthdayWishes.Seed("2026-10-19"));
        Assert.Equal(2166136261u, BirthdayWishes.Seed(""));
    }

    // ---- friendly lines --------------------------------------------------------------------------------------

    [Fact]
    public void Greetings_use_the_name_only_when_there_is_one()
    {
        var noon = new DateTime(2026, 5, 5, 12, 30, 0);
        Assert.Null(Friendly.Greeting(noon, ""));
        Assert.Null(Friendly.Greeting(noon, null));
        Assert.Null(Friendly.Calm("  "));
        foreach (var part in Enum.GetValues<Greetings.Part>())
            for (var v = 0; v < Friendly.VariantCount; v++)
            {
                Assert.Contains("Sara", Friendly.Greeting(part, "Sara", true, v));
                Assert.Contains("سارا", Friendly.Greeting(part, "سارا", false, v));
                Assert.DoesNotContain("{0}", Friendly.Greeting(part, "Sara", true, v));
            }
    }

    [Fact]
    public void Calm_and_notice_lines_are_friendly_and_fall_back_without_a_name()
    {
        Assert.Equal("علی، همه‌چیز آرومه", Friendly.Calm("علی", false));
        Assert.Equal("All calm, Ali", Friendly.Calm("Ali", true));
        Assert.Equal("وقت یه استراحته", Friendly.Notice("", "وقت یه استراحته", "Break time", "{name}، وقت یه استراحته", "{name}, time for a break", false));
        Assert.Equal("Ali, time for a break", Friendly.Notice(" Ali ", "وقت یه استراحته", "Break time", "{name}، وقت یه استراحته", "{name}, time for a break", true));
    }

    [Fact]
    public void Long_names_are_cut_like_everywhere_else()
    {
        var text = Friendly.Calm(new string('ا', 80), false)!;
        Assert.True(Regex.Matches(text, "ا").Count <= BirthdaySettings.MaxName + 1);
    }
}
