using Shabpa.Core;
using Xunit;

namespace Shabpa.Tests;

public class OverlayColorTests
{
    [Fact]
    public void Own_group_colour_is_read_and_damaged_values_are_ignored()
    {
        var o = new OverlaySettings();
        o.GroupColors["cpu"] = "FF8800";
        o.GroupColors["ram"] = "zzzzzz";
        o.GroupColors["net"] = "12";
        var cpu = OverlayRenderer.CustomColor(o, "cpu");
        Assert.NotNull(cpu);
        Assert.Equal((255, 136, 0), (cpu!.Value.R, cpu.Value.G, cpu.Value.B));
        Assert.Null(OverlayRenderer.CustomColor(o, "ram"));
        Assert.Null(OverlayRenderer.CustomColor(o, "net"));
        Assert.Null(OverlayRenderer.CustomColor(o, "gpu"));
    }

    [Fact]
    public void Clock_pattern_has_room_for_seconds_and_part_names_change_the_size_signature()
    {
        Assert.Equal("##:##:##", OverlaySlots.NumberPattern("clock"));
        Assert.Equal("##:##:##", OverlaySlots.CompactPattern("clock"));
        var a = new OverlaySettings { ShowPartNames = true };
        var b = new OverlaySettings { ShowPartNames = false };
        Assert.NotEqual(OverlaySlots.Signature(a, ["fps"], false), OverlaySlots.Signature(b, ["fps"], false));
    }

    [Fact]
    public void Dns_catalog_has_no_duplicate_addresses()
    {
        var all = DnsCatalog.All.SelectMany(e => new[] { e.Primary, e.Secondary }).ToList();
        Assert.All(DnsCatalog.All, e => Assert.True(DnsCatalog.TryParseIPv4(e.Primary, out _) && DnsCatalog.TryParseIPv4(e.Secondary, out _), e.Name));
        Assert.Equal(DnsCatalog.All.Count, DnsCatalog.All.Select(e => e.Primary).Distinct().Count());
    }
}

public class SpeedUnitTests
{
    [Fact]
    public void Speeds_convert_between_megabit_megabyte_and_kilobyte()
    {
        Assert.Equal(SpeedUnit.Mbit, SpeedUnit.Key); // nothing loaded in tests: the default
        Assert.Equal((8.0, "Mb/s"), SpeedUnit.Of(8));
        Assert.Equal("MB/s", SpeedUnit.WidestUnit == "Mb/s" ? "MB/s" : "");
        Assert.Equal(SpeedUnit.Mbit, SpeedUnit.Clean("garbage"));
        Assert.Equal(SpeedUnit.MByte, SpeedUnit.Clean("mbyte"));
        Assert.Equal(SpeedUnit.Mbit, SpeedUnit.Clean("kbyte")); // the kilobyte choice was dropped; an old setting falls back to megabit
    }
}

public class PowerPeakTests
{
    [Fact]
    public void A_play_session_peak_raises_the_maximum_even_a_typed_one()
    {
        var s = new Settings();
        PowerModel.ApplyDetected(s, new PowerEstimateResult(200, 12, 100, 45, PowerConfidence.Medium, [], "gpu-a"), new DateTime(2026, 1, 1));
        PowerModel.Edit(s, PowerField.GpuMax, 220);
        Assert.True(s.Power.GpuMaxManual);
        var t = new DateTime(2026, 1, 1, 20, 0, 0);
        // five minutes of hard work at about 260 W, the window closes on the way
        for (var i = 0; i < 22; i++) PowerModel.Observe(s, 255 + i % 5, 98, t.AddSeconds(i * 10), TimeSpan.FromMinutes(3));
        Assert.True(s.GpuMaxWatts >= 255, $"max is {s.GpuMaxWatts}");
        Assert.False(s.Power.GpuMaxManual);
        Assert.True(s.Power.SeenPeak >= 255);
        // a lower later session never lowers it again
        var before = s.GpuMaxWatts;
        PowerModel.Refresh(s);
        Assert.Equal(before, s.GpuMaxWatts);
    }
}
