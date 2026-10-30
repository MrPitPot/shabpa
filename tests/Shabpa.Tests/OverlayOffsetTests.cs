using Shabpa.Core;
using Xunit;

namespace Shabpa.Tests;

[Collection("Wpf")]
public class OverlayOffsetTests
{
    private static readonly PxBox Screen = new(0, 0, 1920, 1080);

    [Theory]
    [InlineData("tl", 100, 100)]
    [InlineData("tr", 100, 1920 - 400 - 100)]
    public void On_a_side_the_first_margin_is_the_distance_from_that_side(string zone, int offset, int expectedX) =>
        Assert.Equal(expectedX, OverlayPlacement.AtZone(zone, 400, 60, Screen, offset, 20).x);

    [Fact]
    public void In_the_middle_the_first_margin_shifts_the_overlay_away_from_the_centre()
    {
        var centre = (1920 - 400) / 2;
        Assert.Equal(centre, OverlayPlacement.AtZone("tc", 400, 60, Screen, 0, 20, shiftCentre: true).x);
        Assert.Equal(centre + 196, OverlayPlacement.AtZone("tc", 400, 60, Screen, 196, 20, shiftCentre: true).x);
        Assert.Equal(centre - 150, OverlayPlacement.AtZone("bc", 400, 60, Screen, -150, 20, shiftCentre: true).x);
    }

    [Fact]
    public void A_shift_never_carries_the_overlay_off_the_screen()
    {
        Assert.Equal(1920 - 400, OverlayPlacement.AtZone("tc", 400, 60, Screen, 900, 20, shiftCentre: true).x);
        Assert.Equal(0, OverlayPlacement.AtZone("tc", 400, 60, Screen, -900, 20, shiftCentre: true).x);
        Assert.Equal(0, OverlayPlacement.AtZone("tc", 2000, 60, Screen, 300, 20, shiftCentre: true).x);          // wider than the screen: pinned to the left edge
    }

    [Fact]
    public void The_top_and_bottom_margins_follow_the_zone()
    {
        Assert.Equal(80, OverlayPlacement.AtZone("tc", 400, 60, Screen, 0, 80).y);
        Assert.Equal(1080 - 60 - 80, OverlayPlacement.AtZone("bc", 400, 60, Screen, 0, 80).y);
    }

    [Fact]
    public void The_bar_rooms_are_tighter_than_the_general_ones_but_still_hold_a_full_value()
    {
        foreach (var key in new[] { "fps", "gpu", "cpu", "gputemp", "cputemp", "hotspot", "gpuclock", "cpufreq", "gpufan", "down", "up", "ramused", "vram" })
            Assert.True(OverlaySlots.CompactPattern(key).Length <= OverlaySlots.NumberPattern(key).Length, key);
        Assert.Equal("###", OverlaySlots.CompactPattern("gpu"));           // 100 %
        Assert.Equal("####", OverlaySlots.CompactPattern("cpufreq"));      // 4275 MHz
        Assert.Equal("##.#", OverlaySlots.CompactPattern("ramused"));      // 19.1 GB
    }

    [Fact]
    public void A_compact_slot_is_narrower_and_the_size_still_does_not_change_with_the_values()
    {
        double normal = 0, compact = 0;
        AnchorPickerFlowTests.OnSta(() =>
        {
            AnchorPickerFlowTests.Prepare();
            var font = (System.Windows.Media.FontFamily)System.Windows.Application.Current.Resources["Num"];
            var m = new MetricView("gpu", "GPU", "50", "%", 0.5, 0);
            normal = OverlayRenderer.SlotWidth(m, 23, font);
            compact = OverlayRenderer.SlotWidth(m, 23, font, compact: true);
        });
        Assert.True(compact < normal, $"{compact} vs {normal}");
    }
}
