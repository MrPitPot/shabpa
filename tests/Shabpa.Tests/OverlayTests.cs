using Shabpa.Core;

namespace Shabpa.Tests;

public class OverlayModelTests
{
    [Fact]
    public void Ticked_readings_stay_on_the_list_even_when_empty()
    {
        var sample = new TelemetrySample { Gpu = new GpuReading { LoadPercent = 50 }, Fps = 144 };
        var metrics = OverlayMetrics.Build(sample, ["fps", "gpu", "hotspot", "gpupower", "ping"]);
        Assert.Equal(["fps", "gpu", "hotspot", "gpupower", "ping"], metrics.Select(m => m.Key)); // nothing jumps off the list
        Assert.Equal("—", metrics.Single(m => m.Key == "hotspot").Value);
        Assert.Equal("—", metrics.Single(m => m.Key == "ping").Value);
    }

    [Fact]
    public void Values_are_coloured_by_how_worrying_they_are()
    {
        Assert.Equal(0, OverlayMetrics.Level("gputemp", 60));
        Assert.Equal(1, OverlayMetrics.Level("gputemp", 78));
        Assert.Equal(2, OverlayMetrics.Level("gputemp", 90));
        Assert.Equal(2, OverlayMetrics.Level("fps", 20));
        Assert.Equal(0, OverlayMetrics.Level("fps", 144));
        Assert.Equal(2, OverlayMetrics.Level("ping", 180));
    }

    [Fact]
    public void Demo_sample_fills_the_default_layout()
    {
        var settings = new OverlaySettings();
        var metrics = OverlayMetrics.Build(OverlayMetrics.Demo(DateTime.Now), settings.Items);
        Assert.Equal(settings.Items.Count, metrics.Count);
        Assert.All(metrics, m => Assert.True(m.HasValue));
    }

    [Fact]
    public void Custom_colours_must_be_six_hex_digits()
    {
        Assert.True(OverlayLook.IsHex("3EE6A0"));
        Assert.False(OverlayLook.IsHex("#3EE6A0"));
        Assert.False(OverlayLook.IsHex("XYZ123"));
        Assert.False(OverlayLook.IsHex("3EE6A"));
    }
}

public class GameTimeAltTabTests
{
    [Fact]
    public void AltTabDoesNotResetPlayTime()
    {
        var start = new DateTime(2026, 1, 1, 20, 0, 0);
        DateTime? s = null;
        s = Shabpa.Core.Telemetry.NextGameStart(s, true, start, start.AddMinutes(10));
        // another window in front: the game is not the foreground program but is still alive
        s = Shabpa.Core.Telemetry.NextGameStart(s, false, start, start.AddMinutes(11));
        Assert.Equal(start, s);
        // the game really ended
        Assert.Null(Shabpa.Core.Telemetry.NextGameStart(s, false, null, start.AddMinutes(12)));
    }

    [Fact]
    public void WithoutAProcessStartTheFirstSeenMomentIsKept()
    {
        var t0 = new DateTime(2026, 1, 1, 20, 0, 0);
        var s = Shabpa.Core.Telemetry.NextGameStart(null, true, null, t0);
        s = Shabpa.Core.Telemetry.NextGameStart(s, true, null, t0.AddMinutes(5));
        Assert.Equal(t0, s);
    }
}

public class OverlayColourPickerMathTests
{
    [Theory]
    [InlineData("3EE6A0")]
    [InlineData("FF0000")]
    [InlineData("000000")]
    [InlineData("FFFFFF")]
    [InlineData("808080")]
    [InlineData("1A2B3C")]
    public void Hsv_round_trips_every_colour(string hex)
    {
        Assert.True(ColorMath.TryParse(hex, out var c));
        var (h, s, v) = ColorMath.ToHsv(c.r, c.g, c.b);
        var back = ColorMath.FromHsv(h, s, v);
        Assert.Equal(hex, ColorMath.ToHex(back.r, back.g, back.b));
    }

    [Fact]
    public void Primary_hues_land_on_the_primary_colours()
    {
        Assert.Equal("FF0000", Hex(ColorMath.FromHsv(0, 1, 1)));
        Assert.Equal("00FF00", Hex(ColorMath.FromHsv(120, 1, 1)));
        Assert.Equal("0000FF", Hex(ColorMath.FromHsv(240, 1, 1)));
        Assert.Equal("FFFFFF", Hex(ColorMath.FromHsv(200, 0, 1)));
        Assert.Equal("000000", Hex(ColorMath.FromHsv(200, 1, 0)));
        Assert.Equal("FF0000", Hex(ColorMath.FromHsv(360, 1, 1)));
    }

    [Fact]
    public void Typed_codes_accept_a_hash_and_reject_junk()
    {
        Assert.True(ColorMath.TryParse("#3ee6a0", out var c));
        Assert.Equal((0x3E, 0xE6, 0xA0), (c.r, c.g, c.b));
        Assert.False(ColorMath.TryParse("3EE6", out _));
        Assert.False(ColorMath.TryParse(null, out _));
    }

    private static string Hex((byte r, byte g, byte b) c) => ColorMath.ToHex(c.r, c.g, c.b);
}

public class OverlayPlacementTests
{
    private static readonly PxBox Screen = new(0, 0, 1920, 1080);

    [Fact]
    public void A_block_that_grew_past_the_edge_is_pulled_back_inside()
    {
        var grown = OverlayPlacement.Clamp(new PxBox(1800, 1000, 300, 200), Screen);
        Assert.Equal(new PxBox(1620, 880, 300, 200), grown);
        Assert.Equal(new PxBox(0, 0, 300, 200), OverlayPlacement.Clamp(new PxBox(-50, -9, 300, 200), Screen));
    }

    [Fact]
    public void A_block_bigger_than_the_monitor_is_pinned_to_its_corner()
    {
        Assert.Equal(new PxBox(0, 0, 2500, 1500), OverlayPlacement.Clamp(new PxBox(300, 300, 2500, 1500), Screen));
    }

    [Fact]
    public void Letting_go_near_a_corner_snaps_to_it()
    {
        var r = OverlayPlacement.Snap(new PxBox(20, 10, 200, 100), Screen, 36, 16);
        Assert.Equal("tl", r.Zone);
        Assert.Equal(new PxBox(16, 16, 200, 100), r.Box);

        r = OverlayPlacement.Snap(new PxBox(1920 - 200 - 5, 1080 - 100 - 30, 200, 100), Screen, 36, 16);
        Assert.Equal("br", r.Zone);
        Assert.Equal(new PxBox(1920 - 200 - 16, 1080 - 100 - 16, 200, 100), r.Box);
    }

    [Fact]
    public void Letting_go_in_the_middle_leaves_it_alone_and_near_one_edge_only_snaps_that_edge()
    {
        var r = OverlayPlacement.Snap(new PxBox(500, 400, 200, 100), Screen, 36, 16);
        Assert.Null(r.Zone);
        Assert.Equal(new PxBox(500, 400, 200, 100), r.Box);

        r = OverlayPlacement.Snap(new PxBox(10, 400, 200, 100), Screen, 36, 16);
        Assert.Null(r.Zone);
        Assert.Equal(new PxBox(16, 400, 200, 100), r.Box);
    }

    [Fact]
    public void The_horizontal_middle_snaps_on_the_top_and_bottom_bands()
    {
        var r = OverlayPlacement.Snap(new PxBox(1920 / 2 - 100 + 12, 8, 200, 100), Screen, 36, 16);
        Assert.Equal("tc", r.Zone);
        Assert.Equal(new PxBox(860, 16, 200, 100), r.Box);
    }

    [Fact]
    public void Snapping_never_pushes_the_block_off_the_monitor()
    {
        var r = OverlayPlacement.Snap(new PxBox(-80, 1060, 200, 100), Screen, 36, 16);
        Assert.InRange(r.Box.X, 0, 1920 - 200);
        Assert.InRange(r.Box.Y, 0, 1080 - 100);
    }

    [Fact]
    public void Zones_are_placed_from_their_own_edges()
    {
        Assert.Equal((1920 - 200 - 20, 20), OverlayPlacement.AtZone("tr", 200, 100, Screen, 20, 20));
        Assert.Equal((20, 1080 - 100 - 20), OverlayPlacement.AtZone("bl", 200, 100, Screen, 20, 20));
        Assert.Equal((860, 1080 - 100 - 20), OverlayPlacement.AtZone("bc", 200, 100, Screen, 20, 20));
    }

    [Fact]
    public void A_spot_on_an_unplugged_screen_falls_back_to_the_nearest_monitor()
    {
        var left = new PxBox(-1920, 0, 1920, 1080);
        var main = new PxBox(0, 0, 2560, 1440);
        Assert.Equal(main, OverlayPlacement.Monitor(new PxBox(2400, 100, 300, 200), [left, main]));
        Assert.Equal(left, OverlayPlacement.Monitor(new PxBox(-500, 100, 300, 200), [left, main]));
        // nothing under it: 6000 px to the right is nearest to the main monitor
        Assert.Equal(main, OverlayPlacement.Monitor(new PxBox(6000, 100, 300, 200), [left, main]));
        // a monitor with a negative origin still clamps correctly
        Assert.Equal(new PxBox(-1920, 880, 300, 200), OverlayPlacement.Clamp(new PxBox(-2100, 1000, 300, 200), left));
    }
}
