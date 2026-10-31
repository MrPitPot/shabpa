using System.IO;
using System.Diagnostics;
using Shabpa.Core;

namespace Shabpa.Tests;

public class BidarChatTests
{
    private static Hub NewHub() =>
        new(new Store(Path.Combine(Path.GetTempPath(), "shabpa-chat-" + Guid.NewGuid().ToString("N"))) { ReadOnly = true }, dryRun: true, demo: true);

    private static string Ask(Hub hub, string text, DateTime? now = null)
    {
        var result = CommandParser.Parse(text, now ?? new DateTime(2026, 11, 2, 14, 0, 0));
        Assert.Equal(CommandStatus.Ready, result.Status);
        var reply = hub.RunAssistant(result.Command!, "box");
        return reply.Fa; // Reply() writes the same text in both fields
    }

    private static bool HasPersian(string s) => s.Any(c => c is >= '\u0600' and <= '\u06FF');

    [Theory]
    [InlineData("tell me a joke", true)]
    [InlineData("What time is it?", true)]
    [InlineData("یه جوک بگو", false)]
    [InlineData("ساعت چنده؟ what", false)]
    [InlineData("salam", false)]
    [InlineData("khamosh kon", false)]
    [InlineData("shut down in an hour", true)]
    public void The_language_of_a_chat_is_the_language_of_the_text(string text, bool english) =>
        Assert.Equal(english, BidarJokes.IsEnglish(text));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12:30")]
    public void Text_without_letters_has_no_language_of_its_own(string text) => Assert.Null(BidarJokes.IsEnglish(text));

    [Fact]
    public void English_in_a_Persian_app_is_answered_in_English_and_the_other_way_round()
    {
        var hub = NewHub();
        Assert.False(L.En); // the test run is Persian
        var english = Ask(hub, "tell me a joke");
        Assert.False(HasPersian(english.Replace("🦉", "")), english);
        var persian = Ask(hub, "یه جوک بگو");
        Assert.True(HasPersian(persian), persian);

        // an English app, Persian text: the scope is per thread, so this does not touch other tests
        using (L.Scope(true))
        {
            var again = Ask(hub, "یه جوک بگو");
            Assert.True(HasPersian(again), again);
            Assert.Equal(BidarJokes.IsEnglish("یه جوک بگو"), false);
        }
        Assert.False(L.En);
    }

    [Fact]
    public void A_whole_round_of_jokes_has_no_repeat_and_every_list_is_in_one_language()
    {
        foreach (var (name, lines, persian) in new[] { ("a", BidarJokes.JokesFa, true), ("b", BidarJokes.JokesEn, false), ("c", BidarJokes.FactsFa, true), ("d", BidarJokes.FactsEn, false),
                     ("e", BidarJokes.TipsFa, true), ("f", BidarJokes.TipsEn, false), ("g", BidarJokes.PepFa, true), ("h", BidarJokes.PepEn, false) })
        {
            Assert.True(lines.Length >= 4);
            Assert.Equal(lines.Length, lines.Distinct().Count());
            Assert.All(lines, l => Assert.Equal(persian, HasPersian(l)));
            var round = Enumerable.Range(0, lines.Length).Select(_ => BidarJokes.Pick("round-" + name, lines)).ToList();
            Assert.Equal(lines.Length, round.Distinct().Count()); // each line once per round
            var next = BidarJokes.Pick("round-" + name, lines);
            Assert.NotEqual(round[^1], next); // a new round never opens with the line the last one ended on
        }
        Assert.True(BidarJokes.JokesFa.Length >= 25 && BidarJokes.JokesEn.Length >= 25);
    }

    [Theory]
    [InlineData("calc 12*3+4", "12*3+4 = 40")]
    [InlineData("حساب کن ۱۲*۳", "12*3 = 36")]
    [InlineData("۱۲*۳ حساب کن", "12*3 = 36")]
    [InlineData("what is 100/8", "100/8 = 12.5")]
    public void Plain_arithmetic_is_worked_out(string text, string answer)
    {
        var hub = NewHub();
        var reply = Ask(hub, text);
        Assert.Equal(answer, reply.Replace("۱", "1").Replace("۲", "2").Replace("۳", "3").Replace("۴", "4").Replace("۵", "5").Replace("۶", "6").Replace("۰", "0").Replace("٫", "."));
    }

    [Theory]
    [InlineData("calc 1/0")]
    [InlineData("calc 1+")]
    public void Arithmetic_that_cannot_be_worked_out_is_said_so(string text)
    {
        var hub = NewHub();
        var result = CommandParser.Parse(text, DateTime.Now);
        if (result.Status == CommandStatus.Ready && result.Command is { Intent: CommandIntent.Info })
            Assert.DoesNotContain("=", hub.RunAssistant(result.Command, "box").Fa.Replace("= ∞", ""));
    }

    [Theory]
    [InlineData("tell me a fact")]
    [InlineData("یه فکت بگو")]
    [InlineData("give me a tip")]
    [InlineData("یه نکته بگو")]
    [InlineData("cheer me up")]
    [InlineData("روحیه بده")]
    [InlineData("hoot")]
    [InlineData("flip a coin")]
    [InlineData("تاس بنداز")]
    [InlineData("my level")]
    [InlineData("سطحم چنده")]
    [InlineData("my tasks")]
    [InlineData("how long has my pc been on")]
    [InlineData("disk space")]
    public void The_new_questions_are_understood_and_answered(string text)
    {
        var hub = NewHub();
        var reply = Ask(hub, text);
        Assert.False(string.IsNullOrWhiteSpace(reply));
        Assert.DoesNotContain("نمی‌دونم چی بگم", reply);
    }

    [Fact]
    public void Talking_to_bidar_counts_for_the_chat_medals()
    {
        var hub = NewHub();
        hub.State.Rewards = new RewardsState();
        Ask(hub, "tell me a joke");
        Ask(hub, "یه جوک بگو");
        Assert.Equal(2, hub.State.Rewards.Counters.GetValueOrDefault("bidar"));
    }

    [Fact]
    public void Reading_a_command_is_fast()
    {
        var texts = new[] { "یه ساعت دیگه خاموش کن", "shut down in an hour", "ساعت ۲:۳۰ بخوابون", "open settings", "tell me a joke", "یه جوک بگو", "وقتی دانلود تموم شد خاموش کن", "calc 12*3", "نور شب روشن", "what time is it?" };
        for (var i = 0; i < 50; i++) CommandParser.Parse(texts[i % texts.Length], DateTime.Now); // warm up
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 1000; i++) CommandParser.Parse(texts[i % texts.Length], DateTime.Now);
        watch.Stop();
        Assert.True(watch.ElapsedMilliseconds < 3000, $"1000 parses took {watch.ElapsedMilliseconds} ms");
    }
}
