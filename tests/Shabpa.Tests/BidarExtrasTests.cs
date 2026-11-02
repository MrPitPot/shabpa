using System.IO;
using System.Text.RegularExpressions;
using Shabpa.Core;

namespace Shabpa.Tests;

public class BidarExtrasTests
{
    private static readonly DateTime Now = new(2026, 11, 2, 14, 0, 0);

    private static Hub NewHub() =>
        new(new Store(Path.Combine(Path.GetTempPath(), "shabpa-extras-" + Guid.NewGuid().ToString("N"))) { ReadOnly = true }, dryRun: true, demo: true);

    private static string Ask(string text, string origin = "box", DateTime? now = null)
    {
        var result = CommandParser.Parse(text, now ?? Now);
        Assert.True(result is { Status: CommandStatus.Ready, Command: { Intent: CommandIntent.Info } }, $"'{text}' should be an answer, got {result.Status} {result.Command?.Intent}");
        return NewHub().RunAssistant(result.Command!, origin).Fa;
    }

    private static bool HasPersian(string s) => s.Any(c => c is >= '؀' and <= 'ۿ');

    [Theory]
    [InlineData("convert 5 km to miles", "5 km = 3.1069 mi")]
    [InlineData("100 c to f", "100 °C = 212 °F")]
    [InlineData("32 f to c", "32 °F = 0 °C")]
    [InlineData("1 gb to mb", "1 GB = 1024 MB")]
    [InlineData("10 lb to kg", "10 lb = 4.5359 kg")]
    [InlineData("what is 2 inches in cm", "2 in = 5.08 cm")]
    public void English_conversions_answer_in_English(string text, string expected)
    {
        var reply = Ask(text);
        Assert.Equal(expected, reply);
        Assert.False(HasPersian(reply));
    }

    [Theory]
    [InlineData("۵ کیلومتر چند مایله؟", "۵ کیلومتر = ۳.۱۰۶۹ مایل")]
    [InlineData("تبدیل ۱۰۰ سانتیگراد به فارنهایت", "۱۰۰ درجه‌ی سانتی‌گراد = ۲۱۲ درجه‌ی فارنهایت")]
    [InlineData("۲ گیگ به مگابایت", "۲ گیگابایت = ۲۰۴۸ مگابایت")]
    public void Persian_conversions_answer_in_Persian(string text, string expected) => Assert.Equal(expected, Ask(text));

    [Theory]
    [InlineData("5 km to kg")]
    [InlineData("5 km to km")]
    [InlineData("shutdown in 5 minutes")]
    [InlineData("restart in 10 minutes")]
    [InlineData("۱۰ دقیقه دیگه خاموش کن")]
    [InlineData("sleep at 2:30")]
    public void Power_commands_and_nonsense_are_never_taken_for_conversions(string text) =>
        Assert.Null(BidarExtras.Match(text, Now));

    [Fact]
    public void A_password_is_strong_has_the_asked_length_and_is_new_every_time()
    {
        var reply = Ask("make me a strong password 20 characters");
        var pwd = reply.Split('\n')[0].Split(": ")[1];
        Assert.Equal(20, pwd.Length);
        Assert.Contains(pwd, c => char.IsUpper(c));
        Assert.Contains(pwd, c => char.IsLower(c));
        Assert.Contains(pwd, c => char.IsDigit(c));
        Assert.Contains(pwd, c => !char.IsLetterOrDigit(c));
        Assert.NotEqual(BidarExtras.MakePassword(20), BidarExtras.MakePassword(20));
        Assert.Equal(8, BidarExtras.MakePassword(3).Length);
        Assert.Equal(64, BidarExtras.MakePassword(500).Length);
    }

    [Fact]
    public void A_persian_password_ask_is_answered_in_Persian_and_never_sent_through_the_bot()
    {
        Assert.Contains("پسورد قوی", Ask("یه پسورد قوی بساز"));
        var viaBot = Ask("یه پسورد قوی بساز", origin: "bot");
        Assert.Contains("تلگرام", viaBot);
        Assert.DoesNotContain("\n", viaBot);
        Assert.Contains("Telegram", Ask("generate a password", origin: "bot"));
    }

    [Theory]
    [InlineData("time in london")]
    [InlineData("what time is it in tokyo")]
    [InlineData("ساعت لندن چنده")]
    [InlineData("ساعت نیویورک چنده؟")]
    public void The_time_in_another_city_is_answered(string text) =>
        Assert.Matches(@"\d{1,2}:\d{2}|[۰-۹]{2}:[۰-۹]{2}", Ask(text));

    [Fact]
    public void The_time_in_a_city_is_computed_from_its_time_zone()
    {
        var utc = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("12:00", BidarExtras.WorldTimeAt("London", utc));
        Assert.Equal("15:30", BidarExtras.WorldTimeAt("Tehran", utc));
        Assert.Equal("07:00", BidarExtras.WorldTimeAt("New York", utc));
        Assert.Equal("21:00", BidarExtras.WorldTimeAt("Tokyo", utc));
    }

    [Theory]
    [InlineData("شب بخیر")]
    [InlineData("ساعت چنده")]
    [InlineData("what time is it")]
    [InlineData("shut down at london time 5")]
    public void Plain_time_questions_and_commands_keep_their_old_meaning(string text) => Assert.Null(BidarExtras.Match(text, Now));

    [Fact]
    public void Days_until_an_occasion_are_counted_from_the_calendar()
    {
        Assert.Equal(new DateTime(2026, 12, 25), BidarExtras.NextOccasion("christmas", Now));
        Assert.Equal(new DateTime(2027, 1, 1), BidarExtras.NextOccasion("newyear", Now));
        Assert.Equal(new DateTime(2027, 3, 21), BidarExtras.NextOccasion("nowruz", Now));        // 1 Farvardin 1406
        Assert.Equal(new DateTime(2026, 12, 21), BidarExtras.NextOccasion("yalda", Now));        // 30 Azar 1405
        Assert.Equal(new DateTime(2026, 11, 6), BidarExtras.NextOccasion("friday", Now));        // 2 Nov 2026 is a Monday
        Assert.Equal(new DateTime(2026, 11, 6), BidarExtras.NextOccasion("friday", new DateTime(2026, 11, 6)));
        Assert.Equal(new DateTime(2026, 10, 31), BidarExtras.NextOccasion("halloween", new DateTime(2026, 10, 31)));
        Assert.Equal(new DateTime(2027, 10, 31), BidarExtras.NextOccasion("halloween", Now));
    }

    [Fact]
    public void Days_until_questions_answer_in_the_language_of_the_question()
    {
        Assert.Equal(("until", "christmas"), BidarExtras.Match("how many days until christmas", Now));
        Assert.Equal(("until", "nowruz"), BidarExtras.Match("تا نوروز چند روز مونده؟", Now));
        Assert.Equal(("until", "friday"), BidarExtras.Match("تا جمعه چند روز مونده", Now));
        using (L.Scope(true)) Assert.StartsWith("53 days until Christmas", BidarExtras.Answer("until", "christmas", Now));
        using (L.Scope(false))
        {
            var fa = BidarExtras.Answer("until", "nowruz", Now);
            Assert.Contains("نوروز", fa);
            Assert.Contains("۱۳۹", fa);                                                          // 2 Nov 2026 to 21 Mar 2027
            Assert.Contains("امروزه", BidarExtras.Answer("until", "friday", new DateTime(2026, 11, 6)));
        }
        Assert.Contains("until Christmas", Ask("how many days until christmas"));
        Assert.Contains("نوروز", Ask("تا نوروز چند روز مونده؟"));
    }

    [Fact]
    public void Sleep_times_follow_90_minute_cycles_plus_15_minutes_to_fall_asleep()
    {
        Assert.Equal(("bed", "07:00"), BidarExtras.Match("when should I go to bed if I wake up at 7:00", Now));
        Assert.Equal(("bed", ""), BidarExtras.Match("when should I sleep", Now));
        Assert.Equal(("bed", "07:30"), BidarExtras.Match("اگه ساعت ۷:۳۰ بیدار شم کی بخوابم؟", Now));
        using (L.Scope(true))
        {
            Assert.StartsWith("To wake up at 07:00, go to sleep at 21:45 or 23:15 or 00:45.", BidarExtras.Answer("bed", "07:00", new DateTime(2026, 11, 2, 22, 0, 0)));
            Assert.Contains("05:15, 06:45, 08:15", BidarExtras.Answer("bed", "", new DateTime(2026, 11, 2, 23, 0, 0)));   // 23:00 + 15 min + 4, 5, 6 cycles
        }
        Assert.Contains("خواب", Ask("کی بخوابم؟"));
        Assert.Contains("sleep", Ask("when should I sleep"));
    }

    [Fact]
    public void A_random_number_stays_inside_the_range_and_a_choice_picks_one_of_the_options()
    {
        for (var i = 0; i < 40; i++)
            Assert.InRange(int.Parse(Regex.Match(Ask("random number between 5 and 8"), @": (\d+)").Groups[1].Value), 5, 8);
        Assert.Contains("1000", Ask("random number between 1000 and 1000"));
        var picks = Enumerable.Range(0, 60).Select(_ => Ask("choose between tea or coffee")).ToList();
        Assert.All(picks, p => Assert.True(p.Contains("tea") || p.Contains("coffee")));
        Assert.Contains(picks, p => p.Contains("tea"));
        Assert.Contains(picks, p => p.Contains("coffee"));
        var fa = Ask("بین چای یا قهوه انتخاب کن");
        Assert.True(fa.Contains("چای") || fa.Contains("قهوه"));
        Assert.Contains("عدد تصادفی", Ask("یه عدد تصادفی بین ۱ تا ۱۰ بگو"));
    }

    [Theory]
    [InlineData("choose")]
    [InlineData("pick a theme")]
    [InlineData("choose between tea")]
    public void A_choice_needs_at_least_two_options(string text) => Assert.Null(BidarExtras.Match(text, Now));

    [Theory]
    [InlineData("battery level")]
    [InlineData("باتری چنده؟")]
    public void The_battery_question_is_answered_with_something_readable(string text)
    {
        var reply = Ask(text);
        Assert.True(reply.Contains("battery", StringComparison.OrdinalIgnoreCase) || reply.Contains("باتری") || reply.Contains("Battery") || reply.Contains("battery"));
    }

    [Theory]
    [InlineData("shut down when the battery is low")]
    [InlineData("وقتی باتری تموم شد خاموش کن")]
    public void A_battery_condition_is_still_a_power_command(string text) => Assert.Null(BidarExtras.Match(text, Now));

    [Fact]
    public void Help_lists_the_new_abilities_in_both_languages()
    {
        foreach (var english in new[] { true, false })
        {
            var help = CommandFeatures.HelpText(english);
            Assert.Contains(english ? "password" : "پسورد", help);
            Assert.Contains(english ? "convert" : "مایله", help);
            Assert.Contains(english ? "battery" : "باتری", help);
        }
    }

    [Fact]
    public void Jokes_and_facts_are_plentiful_unique_and_in_their_own_language()
    {
        Assert.True(BidarJokes.JokesFa.Length >= 50 && BidarJokes.JokesEn.Length >= 50);
        Assert.True(BidarJokes.FactsFa.Length >= 50 && BidarJokes.FactsEn.Length >= 50);
        foreach (var set in new[] { BidarJokes.JokesFa, BidarJokes.JokesEn, BidarJokes.FactsFa, BidarJokes.FactsEn })
            Assert.Equal(set.Length, set.Distinct().Count());
        Assert.All(BidarJokes.JokesFa.Concat(BidarJokes.FactsFa), s => Assert.True(HasPersian(s), s));
        Assert.All(BidarJokes.JokesEn.Concat(BidarJokes.FactsEn), s => Assert.False(HasPersian(s), s));
    }

    [Fact]
    public void Asking_for_a_joke_or_fact_never_repeats_in_a_row_and_tells_the_whole_set_before_starting_over()
    {
        // the bag may be half used by other tests, so the window is two sets minus one: it always holds a whole set
        foreach (var (get, size) in new (Func<bool, string>, int)[] { (BidarJokes.Joke, BidarJokes.JokesEn.Length), (BidarJokes.Fact, BidarJokes.FactsEn.Length) })
        {
            var told = Enumerable.Range(0, 2 * size - 1).Select(_ => get(true)).ToList();
            Assert.Equal(size, told.Distinct().Count());
            for (var i = 1; i < told.Count; i++) Assert.NotEqual(told[i - 1], told[i]);
        }
    }
}
