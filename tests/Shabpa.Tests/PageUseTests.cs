using Shabpa.Core;
using Xunit;

namespace Shabpa.Tests;

public class PageUseTests
{
    [Fact]
    public void Top_lists_the_most_visited_known_pages_after_enough_visits()
    {
        var v = new Dictionary<string, int>();
        for (var i = 0; i < 5; i++) PageUse.Record(v, "dns");
        for (var i = 0; i < 9; i++) PageUse.Record(v, "mic");
        for (var i = 0; i < 2; i++) PageUse.Record(v, "ping");
        PageUse.Record(v, "home");
        PageUse.Record(v, "settings");
        Assert.Equal(["mic", "dns"], PageUse.Top(v).Select(t => t.Key));
        Assert.DoesNotContain("home", v.Keys);
    }

    [Fact]
    public void Top_is_capped_and_empty_without_visits()
    {
        Assert.Empty(PageUse.Top([]));
        var v = PageUse.Known.ToDictionary(k => k.Key, _ => 10);
        Assert.Equal(3, PageUse.Top(v).Count);
    }

    [Fact]
    public void Record_asks_to_save_on_first_and_every_third_visit()
    {
        var v = new Dictionary<string, int>();
        var saves = Enumerable.Range(0, 7).Select(_ => PageUse.Record(v, "dns")).ToArray();
        Assert.Equal([true, false, false, true, false, false, true], saves);
    }
}

public class VbCableTests
{
    [Fact]
    public void Only_official_vb_audio_https_links_are_opened()
    {
        Assert.True(VbCable.IsOfficial(VbCable.OfficialPage));
        Assert.True(VbCable.IsOfficial(VbCable.DirectPackage));
        Assert.False(VbCable.IsOfficial("http://vb-audio.com/Cable/"));
        Assert.False(VbCable.IsOfficial("https://vb-audio.com.evil.example/"));
    }
}
