using System.Drawing;
using Shabpa.Core;
using Xunit;

namespace Shabpa.Tests;

public class SteamResumeTests
{
    [Fact]
    public void Finds_the_round_blue_button_on_a_dark_page()
    {
        var found = SteamResume.FindButton(200, 300, (x, y) =>
        {
            var dx = x - 150;
            var dy = y - 120;
            return dx * dx + dy * dy <= 20 * 20 ? Color.FromArgb(40, 135, 225) : Color.FromArgb(14, 20, 27);
        });
        Assert.NotNull(found);
        Assert.InRange(found!.Value.x, 145, 155);
        Assert.InRange(found.Value.y, 115, 125);
    }

    [Fact]
    public void A_dark_page_has_no_button()
    {
        Assert.Null(SteamResume.FindButton(200, 300, (_, _) => Color.FromArgb(14, 20, 27)));
    }

    [Fact]
    public void Reads_the_time_of_the_last_download_rate_line()
    {
        const string log = "[2026-10-15 12:19:26] Current download rate: 34.435 Mbps\n[2026-10-15 12:19:30] something else\n[2026-10-15 12:24:22] Current download rate: 0.522 Mbps\n";
        Assert.Equal(new DateTime(2026, 10, 15, 12, 24, 22), SteamResume.LastDownloadRateAt(log));
        Assert.Null(SteamResume.LastDownloadRateAt("[2026-10-15 12:19:30] nothing\n"));
    }
}

public class EpicResumeTests
{
    private static Color Bg(bool tinted) => tinted ? Color.FromArgb(48, 26, 42) : Color.FromArgb(24, 24, 28);

    /// <summary>A download row: two square buttons (play or pause glyph) on the page background.</summary>
    private static Func<int, int, Color> Row(bool tinted, bool play)
    {
        var button1 = tinted ? Color.FromArgb(80, 60, 74) : Color.FromArgb(58, 58, 62);
        var button2 = tinted ? Color.FromArgb(65, 43, 58) : Color.FromArgb(58, 58, 62);
        return (x, y) =>
        {
            if (y is >= 188 and <= 219)
            {
                if (x is >= 840 and <= 871)
                {
                    var middle = x is >= 855 and <= 857 && y is >= 198 and <= 209;
                    var bars = (x is >= 849 and <= 852 || x is >= 860 and <= 863) && y is >= 198 and <= 209;
                    if (play ? middle || (x is >= 851 and <= 858 && y is >= 199 and <= 208) : bars) return Color.White;
                    return button1;
                }
                if (x is >= 880 and <= 911) return button2;
            }
            return Bg(tinted);
        };
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void Finds_the_button_and_tells_play_from_pause(bool tinted, bool play)
    {
        var found = EpicResume.FindPlayButton(1936, 1048, Row(tinted, play));
        Assert.NotNull(found);
        Assert.InRange(found!.Value.x, 850, 862);
        Assert.InRange(found.Value.y, 198, 210);
        Assert.Equal(play, found.Value.isPlay);
    }

    [Fact]
    public void A_page_without_download_buttons_finds_nothing()
    {
        Assert.Null(EpicResume.FindPlayButton(1936, 1048, (_, _) => Color.FromArgb(24, 24, 28)));
    }
}
