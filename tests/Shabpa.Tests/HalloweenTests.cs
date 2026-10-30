using Shabpa.Core;
using Xunit;

namespace Shabpa.Tests;

public class HalloweenTests
{
    [Theory]
    [InlineData(2026, 10, 27, false)]
    [InlineData(2026, 10, 28, true)]
    [InlineData(2026, 10, 31, true)]
    [InlineData(2026, 11, 1, true)]
    [InlineData(2026, 11, 2, false)]
    [InlineData(2026, 5, 31, false)]
    public void The_season_is_28_October_to_1_November(int y, int m, int d, bool expected) => Assert.Equal(expected, Halloween.InSeason(new DateTime(y, m, d, 12, 0, 0)));

    [Fact]
    public void The_scare_comes_once_a_year_and_only_when_it_is_on()
    {
        var day = new DateTime(2026, 10, 31, 20, 0, 0);
        Assert.True(Halloween.Due(true, 2025, day));
        Assert.True(Halloween.Due(true, 0, day));
        Assert.False(Halloween.Due(true, 2026, day));        // already shown this year
        Assert.False(Halloween.Due(false, 0, day));          // switched off
        Assert.False(Halloween.Due(true, 0, new DateTime(2026, 7, 4)));
    }

    [Fact]
    public void It_is_on_by_default_and_old_settings_files_turn_it_on()
    {
        Assert.True(new Settings().Halloween);
        var old = System.Text.Json.JsonSerializer.Deserialize<Settings>("{}")!;
        Assert.True(old.Halloween);
        Assert.Equal(0, old.HalloweenSeenYear);
    }

    [Theory]
    [InlineData("night")]
    [InlineData("white")]
    public void The_festive_theme_keeps_the_real_theme_and_is_cached(string key)
    {
        var real = Palette.Find(key);
        var tinted = Halloween.Tint(real);
        Assert.Equal(real.Key, tinted.BaseKey);
        Assert.EndsWith("-halloween", tinted.Key);
        Assert.Same(tinted, Halloween.Tint(real));
        Assert.Same(tinted, Halloween.Tint(tinted));              // tinting a tinted theme starts from the real one
        Assert.NotEqual(real.Colors["Accent"], tinted.Colors["Accent"]);
        Assert.Equal(real.Light, tinted.Light);
    }

    [Fact]
    public void Every_day_of_the_season_has_a_greeting_in_both_languages()
    {
        for (var day = 28; day <= 31; day++)
        {
            var d = new DateTime(2026, 10, day);
            Assert.Contains("🎃", Halloween.Title(true)); Assert.False(string.IsNullOrWhiteSpace(Halloween.Wish(d, true)));
            Assert.False(string.IsNullOrWhiteSpace(Halloween.Wish(d, false)));
        }
        Assert.Contains("Halloween", Halloween.Title(true));
        Assert.Contains("هالووین", Halloween.Title(false));
    }

    [Fact]
    public void The_scream_is_a_valid_short_wav_that_is_loud_enough_but_never_full_scale()
    {
        var wav = Halloween.ScreamWav();
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal(44100, BitConverter.ToInt32(wav, 24));
        var samples = (wav.Length - 44) / 2;
        Assert.InRange(samples / 44100.0, 1.0, 1.6);
        Assert.Equal(samples * 2, BitConverter.ToInt32(wav, 40));
        var peak = 0;
        var loudSamples = 0;
        for (var i = 0; i < samples; i++)
        {
            var v = Math.Abs((int)BitConverter.ToInt16(wav, 44 + i * 2));
            peak = Math.Max(peak, v);
            if (v > 4000) loudSamples++;
        }
        Assert.InRange(peak / 32767.0, 0.55, 0.66);
        Assert.True(loudSamples > 5000, "the sting should be audible for a good part of its length");
        Assert.Equal(wav, Halloween.ScreamWav());             // the same sound every time
    }
}