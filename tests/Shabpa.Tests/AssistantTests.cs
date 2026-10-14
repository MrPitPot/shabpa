using Shabpa.Core;

namespace Shabpa.Tests;

public class AssistantTests
{
    // Friday 2026-05-15 20:00
    private static readonly DateTime Now = new(2026, 5, 15, 20, 0, 0);

    private static AssistantResult P(string text) => Assistant.Parse(text, Now);

    [Theory]
    [InlineData("یک ساعت و نیم دیگه خاموش کن", PowerAction.Shutdown, 90)]
    [InlineData("۳۰ دقیقه دیگه بخواب", PowerAction.Sleep, 30)]
    [InlineData("نیم ساعت دیگه خاموش شو", PowerAction.Shutdown, 30)]
    [InlineData("2 ساعت دیگه ریستارت کن", PowerAction.Restart, 120)]
    [InlineData("۲ ساعت دیگه هایبرنیت کن", PowerAction.Hibernate, 120)]
    [InlineData("shut down in 2 hours", PowerAction.Shutdown, 120)]
    [InlineData("sleep in 45 minutes", PowerAction.Sleep, 45)]
    [InlineData("restart in 10 min", PowerAction.Restart, 10)]
    [InlineData("ربع ساعت دیگه خاموش کن", PowerAction.Shutdown, 15)]
    public void Relative_times_are_understood(string text, PowerAction action, int minutes)
    {
        var result = P(text);
        Assert.Equal(AssistantKind.Schedule, result.Kind);
        Assert.Equal(action, result.Action);
        Assert.Equal(Now.AddMinutes(minutes), result.Due);
        Assert.Null(result.Repeat);
    }

    [Theory]
    [InlineData("ساعت ۱ شب خاموش کن", 1, 0)]
    [InlineData("ساعت ۱۰ شب خاموش کن", 22, 0)]
    [InlineData("ساعت ۱۲ شب خاموش کن", 0, 0)]
    [InlineData("ساعت ۲ و نیم شب خاموش کن", 2, 30)]
    [InlineData("ساعت 23:45 خاموش کن", 23, 45)]
    [InlineData("ساعت دو شب بخواب", 2, 0)]
    [InlineData("shut down at 1:30 am", 1, 30)]
    [InlineData("shut down at 9 pm", 21, 0)]
    [InlineData("shutdown at 22:15", 22, 15)]
    public void Clock_times_pick_the_next_occurrence(string text, int hour, int minute)
    {
        var result = P(text);
        Assert.Equal(AssistantKind.Schedule, result.Kind);
        Assert.NotNull(result.Due);
        Assert.Equal((hour, minute), (result.Due!.Value.Hour, result.Due.Value.Minute));
        Assert.True(result.Due > Now);
        Assert.True(result.Due <= Now.AddHours(24));
    }

    [Fact]
    public void Tomorrow_weekdays_and_nightly_repeats()
    {
        var tomorrow = P("فردا ساعت ۷ صبح خاموش کن");
        Assert.Equal(new DateTime(2026, 5, 16, 7, 0, 0), tomorrow.Due);

        var nightly = P("هر شب ساعت ۲ خاموش کن");
        Assert.Equal(AssistantKind.Schedule, nightly.Kind);
        Assert.Equal(7, nightly.Repeat!.Count);
        Assert.Equal(2, nightly.Due!.Value.Hour);

        var monday = P("دوشنبه ساعت ۸ شب خاموش کن");
        Assert.Equal(DayOfWeek.Monday, monday.Due!.Value.DayOfWeek);
        Assert.Equal(20, monday.Due.Value.Hour);
        Assert.Null(monday.Repeat);

        var weekly = P("هر دوشنبه ساعت ۸ شب خاموش کن");
        Assert.Equal([DayOfWeek.Monday], weekly.Repeat);
    }

    [Fact]
    public void Conditions_become_rules()
    {
        var download = P("وقتی دانلود تموم شد خاموش کن");
        Assert.Equal(AssistantKind.Rule, download.Kind);
        Assert.Equal(RuleKind.Network, download.Rule);
        Assert.Equal(PowerAction.Shutdown, download.Action);

        var idle = P("اگه ۴۵ دقیقه بی‌استفاده موند بخواب");
        Assert.Equal(AssistantKind.Rule, idle.Kind);
        Assert.Equal(RuleKind.Idle, idle.Rule);
        Assert.Equal(45, idle.Minutes);
        Assert.Equal(PowerAction.Sleep, idle.Action);

        var english = P("shut down when the download finishes");
        Assert.Equal(RuleKind.Network, english.Rule);
    }

    [Theory]
    [InlineData("لغو", AssistantKind.Cancel)]
    [InlineData("cancel everything", AssistantKind.Cancel)]
    [InlineData("وضعیت", AssistantKind.Status)]
    [InlineData("رم رو خالی کن", AssistantKind.FreeRam)]
    [InlineData("free the ram", AssistantKind.FreeRam)]
    public void Housekeeping_phrases(string text, AssistantKind kind) => Assert.Equal(kind, P(text).Kind);

    [Theory]
    [InlineData("")]
    [InlineData("سلام حالت چطوره")]
    [InlineData("خاموش")] // an action with no time and no condition is not guessed at
    [InlineData("what is the weather")]
    public void Anything_else_is_not_guessed(string text) => Assert.False(P(text).Understood);

    [Fact]
    public void Persian_digits_and_extra_marks_do_not_matter()
    {
        var result = P("  ۱۵ دقیقه دیگه، خاموش کن! ");
        Assert.Equal(Now.AddMinutes(15), result.Due);
    }

    [Fact]
    public void The_read_back_names_the_exact_time()
    {
        var text = Assistant.Describe(P("ساعت ۱ شب خاموش کن"), Now);
        Assert.Contains("۰۱:۰۰", text);
    }
}

public class AssistantBotTests
{
    [Fact]
    public void Free_text_reaches_the_assistant_only_when_no_fixed_command_matches()
    {
        var seen = new List<string>();
        var settings = new Settings { TelegramChatId = 111 };
        var bot = new TelegramBot(() => settings, () => { }, new BotHost
        {
            Status = () => "STATUS",
            Request = _ => 15,
            Cancel = () => "CANCELLED",
            Assistant = text => { seen.Add(text); return "ASSISTANT"; },
        });

        Assert.Equal("STATUS", bot.Handle(111, "وضعیت", "Amir", false));
        Assert.Empty(seen);
        Assert.Equal("ASSISTANT", bot.Handle(111, "یک ساعت دیگه خاموش کن", "Amir", false));
        Assert.Single(seen);
    }

    [Fact]
    public void Old_messages_never_reach_the_assistant()
    {
        var called = false;
        var settings = new Settings { TelegramChatId = 111 };
        var bot = new TelegramBot(() => settings, () => { }, new BotHost
        {
            Status = () => "",
            Request = _ => 15,
            Cancel = () => "",
            Assistant = _ => { called = true; return "x"; },
        });
        var reply = bot.Handle(111, "یک ساعت دیگه خاموش کن", "Amir", stale: true);
        Assert.False(called);
        Assert.Contains("قدیمیه", reply);
    }

    [Fact]
    public void Unknown_text_still_gets_the_help_reply_when_the_assistant_declines()
    {
        var settings = new Settings { TelegramChatId = 111 };
        var bot = new TelegramBot(() => settings, () => { }, new BotHost
        {
            Status = () => "", Request = _ => 15, Cancel = () => "", Assistant = _ => null,
        });
        Assert.Contains("متوجه نشدم", bot.Handle(111, "سلام", "Amir", false));
    }
}
