using Shabpa.Core;

namespace Shabpa.Tests;

public class FanSmartTests
{
    [Fact]
    public void Too_hot_raises_the_curve_and_cool_lowers_it_within_bounds()
    {
        Assert.Equal(8, FanSmart.Next(0, 98, 85));   // far over target: big step
        Assert.Equal(3, FanSmart.Next(0, 90, 85));
        Assert.Equal(1, FanSmart.Next(0, 86, 85));
        Assert.Equal(0, FanSmart.Next(0, 84, 85));   // inside the dead band
        Assert.Equal(-1, FanSmart.Next(0, 82, 85));
        Assert.Equal(-3, FanSmart.Next(0, 60, 85));
        Assert.Equal(FanSmart.MaxOffset, FanSmart.Next(FanSmart.MaxOffset, 110, 85));
        Assert.Equal(FanSmart.MinOffset, FanSmart.Next(FanSmart.MinOffset, 30, 85));
    }

    [Fact]
    public void Applied_speeds_stay_in_range_and_never_fall_along_the_curve()
    {
        var speeds = FanSmart.Apply([15, 32, 49, 66, 100], 30, 15, 100);
        Assert.Equal([45, 62, 79, 96, 100], speeds);
        var lowered = FanSmart.Apply([15, 32, 49, 66, 100], -10, 15, 100);
        Assert.Equal([15, 22, 39, 56, 90], lowered);
    }

    [Fact]
    public void A_steady_hot_load_settles_instead_of_running_away()
    {
        // Simple model: the hot spot falls 0.4 °C for every curve point added, from 95 °C at offset 0.
        var offset = 0;
        double temp = 95;
        for (var i = 0; i < 80; i++)
        {
            offset = FanSmart.Next(offset, temp, 85);
            temp = 95 - 0.4 * offset;
        }
        Assert.InRange(temp, 83, 87);
    }
}

public class FanCurveTests
{
    [Fact]
    public void Sanitize_orders_points_and_clamps_to_the_driver_range()
    {
        var (t, s) = FanCurves.Sanitize([60, 40, 200, 70, 10], [90, 20, 300, 5, 50], 25, 90, 15, 100);
        Assert.Equal(5, t.Length);
        for (var i = 1; i < t.Length; i++)
        {
            Assert.True(t[i] > t[i - 1]);
            Assert.True(s[i] >= s[i - 1]);
        }
        Assert.All(t, v => Assert.InRange(v, 25, 90));
        Assert.All(s, v => Assert.InRange(v, 15, 100));
    }

    [Fact]
    public void Every_preset_is_valid_for_the_range_this_card_reports()
    {
        foreach (var (key, _, _) in FanCurves.Presets)
        {
            var (temps, speeds) = FanCurves.Preset(key);
            var (t, s) = FanCurves.Sanitize(temps, speeds, 25, 90, 15, 100);
            Assert.Equal(temps, t);
            Assert.Equal(speeds, s);
        }
    }

    [Fact]
    public void The_plan_only_contains_settings_the_card_actually_offers()
    {
        var features = new Dictionary<int, Od8Range>
        {
            [AmdGpu.PowerPercentage] = new(AmdGpu.PowerPercentage, -10, 15, 0, 0),
            [AmdGpu.OdVoltage] = new(AmdGpu.OdVoltage, 900, 1200, 1200, 1200),
            // no VRAM clock range: the card does not allow it
        };
        var state = new Od8State(0, 76, features);
        var plan = GpuTuner.Plan(new GpuTuneProfile { PowerPercent = -5, Voltage = 1100, VramMax = 2100, FanMode = "driver" }, state);
        Assert.Equal(new Dictionary<int, int> { [AmdGpu.PowerPercentage] = -5, [AmdGpu.OdVoltage] = 1100 }, plan);
    }

    [Fact]
    public void A_fan_preset_writes_all_ten_curve_values()
    {
        var features = new Dictionary<int, Od8Range>();
        for (var i = 0; i < AmdGpu.FanCurvePoints; i++)
        {
            features[AmdGpu.FanCurveTemp1 + i * 2] = new(AmdGpu.FanCurveTemp1 + i * 2, 25, 90, 50, 50);
            features[AmdGpu.FanCurveSpeed1 + i * 2] = new(AmdGpu.FanCurveSpeed1 + i * 2, 15, 100, 50, 50);
        }
        var plan = GpuTuner.Plan(new GpuTuneProfile { FanMode = "preset", FanPreset = "silent" }, new Od8State(0, 76, features));
        Assert.Equal(10, plan.Count);
        Assert.Equal(35, plan[AmdGpu.FanCurveTemp1]);
        Assert.Equal(80, plan[AmdGpu.FanCurveSpeed1 + 8]);
    }
}

public class OverlayModelTests
{
    [Fact]
    public void Readings_this_pc_cannot_provide_are_left_out()
    {
        var sample = new TelemetrySample { Gpu = new GpuReading { LoadPercent = 50 }, Fps = 144 };
        var metrics = OverlayMetrics.Build(sample, ["fps", "gpu", "hotspot", "gpupower", "ping"]);
        Assert.Equal(["fps", "gpu", "ping"], metrics.Select(m => m.Key)); // hotspot/power missing, ping always listed
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
