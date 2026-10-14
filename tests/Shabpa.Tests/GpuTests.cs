using Shabpa.Core;

namespace Shabpa.Tests;

public class FrameStatsTests
{
    private static List<double> Steady(double ms, int frames) => Enumerable.Range(0, frames).Select(i => i * ms).ToList();

    [Fact]
    public void A_steady_stream_gives_its_frame_rate()
    {
        var stats = FrameStats.From(Steady(1000.0 / 144, 300));
        Assert.Equal(144, stats.Fps, 1);
        Assert.Equal(1000.0 / 144, stats.AverageFrameMs, 2);
        Assert.Equal(144, stats.OnePercentLowFps, 1);
    }

    [Fact]
    public void A_few_long_frames_pull_the_one_percent_low_down_but_not_the_average_much()
    {
        var times = new List<double>();
        double t = 0;
        for (var i = 0; i < 300; i++)
        {
            times.Add(t);
            t += i % 50 == 0 ? 50 : 10; // 6 hitches of 50 ms among 10 ms frames
        }
        var stats = FrameStats.From(times);
        Assert.InRange(stats.Fps, 80, 100);
        Assert.InRange(stats.OnePercentLowFps, 19, 21);
        Assert.Equal(50, stats.WorstFrameMs, 3);
    }

    [Fact]
    public void Too_little_data_gives_nothing()
    {
        Assert.Equal(FrameStats.None, FrameStats.From([]));
        Assert.Equal(FrameStats.None, FrameStats.From([5.0]));
        Assert.Equal(FrameStats.None, FrameStats.From([5.0, 5.0]));
    }
}

public class GpuProbeTests
{
    [Fact]
    public void Probing_never_throws_even_on_a_pc_without_any_supported_card()
    {
        using var gpu = GpuProbe.Open();
        if (gpu is null) return;
        var reading = gpu.Read();
        Assert.False(string.IsNullOrWhiteSpace(reading.Name));
        if (reading.TempC is { } temp) Assert.InRange(temp, 0, 150);
    }
}
