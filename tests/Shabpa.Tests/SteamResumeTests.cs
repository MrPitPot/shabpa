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
