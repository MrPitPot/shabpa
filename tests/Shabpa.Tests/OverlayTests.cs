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
