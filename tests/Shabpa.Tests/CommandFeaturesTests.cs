using Shabpa.Core;
using Xunit;

namespace Shabpa.Tests;

public class CommandFeaturesTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 21, 15, 0);

    private static AssistantCommand Read(string text)
    {
        var r = CommandParser.Parse(text, Now);
        Assert.True(r.Status == CommandStatus.Ready && r.Command is not null, $"“{text}” → {r.Status}");
        return r.Command!;
    }

    [Theory]
    [InlineData("برو تنظیمات", "settings")]
    [InlineData("تنظیمات", "settings")]
    [InlineData("open settings", "settings")]
    [InlineData("go to dns", "dns")]
    [InlineData("دی ان اس رو باز کن", "dns")]
    [InlineData("بهینه سازی بازی رو نشون بده", "system:tune")]
    [InlineData("show game tuning", "system:tune")]
    [InlineData("میکروفون", "mic")]
    [InlineData("آب و هوا", "weather")]
    [InlineData("open the health page", "health")]
    [InlineData("برو به پشتیبانی", "support")]
    [InlineData("اورلی رو نشون بده", "gaming")]
    [InlineData("show me the ping page", "ping")]
    [InlineData("go to playtime", "playtime")]
    [InlineData("backup", "files")]
    [InlineData("check for updates", "update")]
    public void A_page_can_be_opened_by_many_names_in_both_languages(string text, string page)
    {
        var c = Read(text);
        Assert.Equal(CommandIntent.OpenPage, c.Intent);
        Assert.Equal(page, c.Page);
        Assert.True(c.Immediate);
    }

    [Theory]
    [InlineData("restart after update")]
    [InlineData("بعد از آپدیت ریستارت کن")]
    [InlineData("shut down when the download ends")]
    [InlineData("ساعت ۵ خاموش کن")]
    [InlineData("sleep in 20 minutes")]
    [InlineData("remind me at 5pm to open settings")]
    public void Power_and_time_commands_that_only_mention_a_page_are_not_taken_for_opening_it(string text)
    {
        var r = CommandParser.Parse(text, Now);
        Assert.NotEqual(CommandIntent.OpenPage, r.Command?.Intent);
        Assert.DoesNotContain(r.Choices, c => c.Intent == CommandIntent.OpenPage);
    }

    [Theory]
    [InlineData("rgb off", false, "off")]
    [InlineData("آر جی بی رو خاموش کن", false, "off")]
    [InlineData("نورپردازی خاموش", false, "off")]
    [InlineData("rgb on", true, "on")]
    [InlineData("نورپردازی رو روشن کن", true, "on")]
    public void The_lights_switch_on_and_off(string text, bool on, string value)
    {
        if (!CommandFeatures.Lights) return;                       // the lighting page waits for a later version
        var c = Read(text);
        Assert.Equal(CommandIntent.Rgb, c.Intent);
        Assert.Equal((on, value), (c.On, c.Value));
    }

    [Theory]
    [InlineData("rgb red", "FF2D2D")]
    [InlineData("نورپردازی قرمز", "FF2D2D")]
    [InlineData("rgb color blue", "2D7DFF")]
    [InlineData("آر جی بی رو بنفش کن", "A855F7")]
    [InlineData("rgb pink", "FF4FA3")]
    [InlineData("led lights green", "22E06B")]
    public void The_lights_take_a_colour_by_name(string text, string hex)
    {
        if (!CommandFeatures.Lights) return;                       // the lighting page waits for a later version
        var c = Read(text);
        Assert.Equal((CommandIntent.Rgb, hex), (c.Intent, c.Color));
    }

    [Theory]
    [InlineData("rgb rainbow", "rainbow")]
    [InlineData("نورپردازی رنگین کمان", "rainbow")]
    [InlineData("rgb breathing", "breathe")]
    [InlineData("rgb music", "music")]
    [InlineData("آر جی بی موسیقی", "music")]
    [InlineData("rgb aurora", "aurora")]
    [InlineData("rgb halloween", "halloween")]
    [InlineData("نورپردازی شب یلدا", "yalda")]
    [InlineData("rgb lava flow", "lava")]
    public void The_lights_take_an_effect_by_name_in_either_language(string text, string key)
    {
        if (!CommandFeatures.Lights) return;                       // the lighting page waits for a later version
        var c = Read(text);
        Assert.Equal((CommandIntent.Rgb, key), (c.Intent, c.Value));
    }

    [Fact]
    public void Brightness_is_read_from_the_text()
    {
        if (!CommandFeatures.Lights) return;                       // the lighting page waits for a later version
        Assert.Equal(40, Read("rgb brightness 40%").Level);
        Assert.Equal(40, Read("نورپردازی روشنایی ۴۰ درصد").Level);
        Assert.Equal(100, Read("rgb 250 percent").Level);          // clamped
    }

    [Theory]
    [InlineData("overlay off", "mode:Off")]
    [InlineData("اورلی رو خاموش کن", "mode:Off")]
    [InlineData("overlay on", "mode:Always")]
    [InlineData("اورلی روشن", "mode:Always")]
    [InlineData("overlay only in game", "mode:InGame")]
    [InlineData("اورلی فقط موقع بازی", "mode:InGame")]
    [InlineData("overlay bar layout", "layout:bar")]
    [InlineData("اورلی مدل کارت", "layout:card")]
    [InlineData("overlay size 150%", "size:150")]
    [InlineData("اورلی اندازه ۱۲۰ درصد", "size:120")]
    [InlineData("overlay size 900", "size:200")]
    public void The_overlay_can_be_switched_and_styled(string text, string value)
    {
        var c = Read(text);
        Assert.Equal((CommandIntent.Overlay, value), (c.Intent, c.Value));
    }

    [Theory]
    [InlineData("theme halloween", "halloween")]
    [InlineData("تم هالووین", "halloween")]
    [InlineData("dark mode", "night")]
    [InlineData("حالت روشن", "white")]
    [InlineData("theme black", "black")]
    [InlineData("تم صورتی", "pink")]
    public void The_theme_can_be_changed(string text, string key)
    {
        var c = Read(text);
        Assert.Equal((CommandIntent.Theme, key), (c.Intent, c.Value));
    }

    [Theory]
    [InlineData("free ram")]
    [InlineData("رم رو خالی کن")]
    [InlineData("clean memory please")]
    [InlineData("پاکسازی رم")]
    public void The_memory_can_be_freed(string text) => Assert.Equal(CommandIntent.MemoryClean, Read(text).Intent);

    [Theory]
    [InlineData("game mode on", true)]
    [InlineData("حالت بازی رو روشن کن", true)]
    [InlineData("game mode off", false)]
    [InlineData("حالت بازی خاموش", false)]
    public void Game_mode_can_be_switched(string text, bool on)
    {
        var c = Read(text);
        Assert.Equal((CommandIntent.GameMode, on), (c.Intent, c.On));
    }

    [Theory]
    [InlineData("what time is it", "time")]
    [InlineData("ساعت چنده", "time")]
    [InlineData("what's the date", "date")]
    [InlineData("امروز چندمه", "date")]
    [InlineData("cpu temp?", "cpu")]
    [InlineData("دمای پردازنده چنده", "cpu")]
    [InlineData("gpu temperature", "gpu")]
    [InlineData("ram usage", "ram")]
    [InlineData("مصرف رم", "ram")]
    [InlineData("what's my ping", "ping")]
    [InlineData("system status", "status")]
    [InlineData("وضعیت سیستم", "status")]
    [InlineData("help", "help")]
    [InlineData("چه کارایی بلدی", "help")]
    [InlineData("what can you do", "help")]
    [InlineData("hello", "hello")]
    [InlineData("سلام", "hello")]
    [InlineData("thanks", "thanks")]
    [InlineData("مرسی", "thanks")]
    [InlineData("how are you", "howareyou")]
    [InlineData("who are you", "who")]
    [InlineData("tell me a joke", "joke")]
    [InlineData("یه جوک بگو", "joke")]
    [InlineData("good night", "goodnight")]
    public void Questions_and_small_talk_get_an_answer(string text, string kind)
    {
        var c = Read(text);
        Assert.Equal((CommandIntent.Info, kind), (c.Intent, c.Value));
        Assert.True(c.Immediate);
    }

    [Fact]
    public void The_cpu_temperature_effect_of_the_lights_is_not_a_question()
    {
        if (!CommandFeatures.Lights) return;                       // the lighting page waits for a later version
        var c = Read("rgb cpu temperature");
        Assert.Equal((CommandIntent.Rgb, "temp"), (c.Intent, c.Value));
    }

    [Fact]
    public void Everything_the_old_reader_understood_is_still_understood()
    {
        Assert.Equal(CommandIntent.Power, Read("shut down in an hour").Intent);
        Assert.Equal(CommandIntent.Power, Read("یه ساعت دیگه خاموش کن").Intent);
        Assert.Equal(CommandIntent.NightLight, Read("نور شب رو روشن کن").Intent);
        Assert.Equal(CommandIntent.NightLight, Read("turn off night light").Intent);
        Assert.Equal(CommandIntent.Cancel, Read("cancel").Intent);
        Assert.Equal(CommandIntent.Focus, Read("focus 25 minutes").Intent);
        Assert.Equal(CommandIntent.ListSchedules, Read("what's scheduled").Intent);
    }

    [Theory]
    [InlineData("power it down in an hour", CommandIntent.Power)]
    [InlineData("oops", CommandIntent.Cancel)]
    [InlineData("scratch that", CommandIntent.Cancel)]
    [InlineData("ولش کن", CommandIntent.Cancel)]
    [InlineData("dim the screen", CommandIntent.NightLight)]
    [InlineData("چشمام درد می‌کنه", CommandIntent.NightLight)]
    [InlineData("study 25 minutes", CommandIntent.Focus)]
    [InlineData("lock the screen", CommandIntent.Power)]
    [InlineData("go to bed in 10 minutes", CommandIntent.Power)]
    [InlineData("ping me at 6pm to call mom", CommandIntent.Reminder)]
    public void More_ways_to_say_the_old_commands_are_understood(string text, CommandIntent intent) => Assert.Equal(intent, Read(text).Intent);

    [Fact]
    public void The_lighting_words_do_nothing_while_the_page_is_not_in_the_app()
    {
        Assert.False(CommandFeatures.Lights);
        foreach (var text in new[] { "rgb red", "نورپردازی رو خاموش کن", "open lighting" })
        {
            var r = CommandParser.Parse(text, Now);
            Assert.DoesNotContain(r.Command?.Intent, new CommandIntent?[] { CommandIntent.Rgb, CommandIntent.OpenPage });
        }
        Assert.DoesNotContain("rgb", CommandFeatures.PageKeys.Where(_ => CommandFeatures.Lights));
    }

    [Fact]
    public void Help_lists_every_kind_of_command_in_both_languages()
    {
        foreach (var english in new[] { true, false })
        {
            var help = CommandFeatures.HelpText(english);
            Assert.True(help.Split('\n').Length >= 9);
        }
    }

    [Fact]
    public void Every_page_the_reader_can_open_is_a_real_page_of_the_window()
    {
        var keys = CommandFeatures.PageKeys.Select(k => k.Split(':')[0]).Distinct().ToList();
        var known = new[] { "home", "system", "comfort", "profiles", "gaming", "playtime", "ping", "dns", "download", "files", "health", "tasks", "mic", "weather", "support", "update", "settings" };
        Assert.All(keys, k => Assert.Contains(k, known));
    }
}
