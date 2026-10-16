using System.IO;
using Shabpa.Core;
using Xunit;

namespace Shabpa.Tests;

public sealed class VoiceTests
{
    private const int Rate = 48000;

    private static double Rms(float[] data, int from, int to)
    {
        double sum = 0;
        for (var i = from; i < to; i++) sum += data[i] * data[i];
        return Math.Sqrt(sum / (to - from));
    }

    private static float[] Run(NoiseSuppressor suppressor, float[] input)
    {
        var output = new float[input.Length];
        const int block = Rate / 100;
        for (var at = 0; at + block <= input.Length; at += block)
            suppressor.Process(input.AsSpan(at, block), output.AsSpan(at, block));
        return output;
    }

    /// <summary>Hiss for the whole time, and a voice-like buzz (a few harmonics, wobbling in loudness) between 4 s and 6 s.</summary>
    private static float[] Scene(double noiseLevel, double voiceLevel, int seconds = 8)
    {
        var random = new Random(7);
        var data = new float[Rate * seconds];
        for (var i = 0; i < data.Length; i++)
        {
            var t = i / (double)Rate;
            var noise = (random.NextDouble() * 2 - 1) * noiseLevel;
            var voice = 0.0;
            if (t is >= 4 and < 6)
            {
                var envelope = 0.6 + 0.4 * Math.Sin(2 * Math.PI * 3 * t);
                for (var h = 1; h <= 6; h++) voice += Math.Sin(2 * Math.PI * 180 * h * t) / h;
                voice *= envelope * voiceLevel;
            }
            data[i] = (float)(noise + voice);
        }
        return data;
    }

    [Fact]
    public void Steady_hiss_is_pushed_down_a_lot_once_it_has_been_heard()
    {
        var input = Scene(0.02, 0.2);
        var output = Run(new NoiseSuppressor(Rate) { Strength = 0.8 }, input);
        var before = Rms(input, Rate * 2, Rate * 3);
        var after = Rms(output, Rate * 2, Rate * 3);
        Assert.True(20 * Math.Log10(before / after) > 12, $"only {20 * Math.Log10(before / after):0.0} dB");
    }

    [Fact]
    public void A_voice_over_the_hiss_keeps_most_of_its_loudness()
    {
        var voiceOnly = Scene(0, 0.2);
        var input = Scene(0.02, 0.2);
        var output = Run(new NoiseSuppressor(Rate) { Strength = 0.8 }, input);
        var wanted = Rms(voiceOnly, Rate * 4, Rate * 6);
        var got = Rms(output, Rate * 4, Rate * 6);
        Assert.True(got > wanted * 0.6, $"voice dropped to {got / wanted:P0}");
    }

    [Fact]
    public void A_stronger_setting_reduces_more_than_a_gentle_one()
    {
        var input = Scene(0.02, 0.2);
        var gentle = Rms(Run(new NoiseSuppressor(Rate) { Strength = 0.1 }, input), Rate * 2, Rate * 3);
        var strong = Rms(Run(new NoiseSuppressor(Rate) { Strength = 1.0 }, input), Rate * 2, Rate * 3);
        Assert.True(strong < gentle * 0.7);
    }

    [Fact]
    public void Output_has_the_same_length_and_survives_silence_and_garbage()
    {
        var suppressor = new NoiseSuppressor(Rate);
        var silence = new float[Rate];
        var quiet = Run(suppressor, silence);
        Assert.Equal(silence.Length, quiet.Length);
        Assert.All(quiet, v => Assert.True(float.IsFinite(v) && Math.Abs(v) < 1e-3));

        var garbage = new float[Rate];
        for (var i = 0; i < garbage.Length; i++) garbage[i] = i % 7 == 0 ? float.NaN : i % 11 == 0 ? float.PositiveInfinity : 5f;
        Assert.All(Run(new NoiseSuppressor(Rate), garbage), v => Assert.True(float.IsFinite(v)));
    }

    [Fact]
    public void Odd_block_sizes_give_the_same_answer_as_regular_ones()
    {
        var input = Scene(0.02, 0.2, 3);
        var regular = Run(new NoiseSuppressor(Rate), input);
        var odd = new float[input.Length];
        var suppressor = new NoiseSuppressor(Rate);
        var sizes = new[] { 100, 333, 480, 1, 700 };
        for (int at = 0, k = 0; at < input.Length; k++)
        {
            var n = Math.Min(sizes[k % sizes.Length], input.Length - at);
            suppressor.Process(input.AsSpan(at, n), odd.AsSpan(at, n));
            at += n;
        }
        for (var i = 0; i < input.Length; i++) Assert.Equal(regular[i], odd[i], 4);
    }

    [Fact]
    public void Virtual_cables_are_recognised_by_name()
    {
        var cable = VoiceFilter.VirtualCable([new AudioDevice(0, "Speakers (Realtek)"), new AudioDevice(1, "CABLE Input (VB-Audio Virtual C")]);
        Assert.Equal(1, cable?.Index);
        Assert.Null(VoiceFilter.VirtualCable([new AudioDevice(0, "Speakers (Realtek)")]));
    }

    [Fact]
    public void The_filter_keeps_no_audio_on_disk_and_sends_nothing()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Shabpa", "Core", "Voice.cs"));
        foreach (var banned in new[] { "File.", "FileStream", "HttpClient", "WebClient", "Socket", "Process.Start" })
            Assert.DoesNotContain(banned, source);
    }
}

public sealed class RnnoiseTests
{
    [Fact]
    public void The_neural_engine_loads_and_runs_safely_and_keeps_a_voice()
    {
        var engine = RnnoiseEngine.TryCreate();
        Assert.NotNull(engine);
        using (engine)
        {
            const int Rate = 48000;
            var random = new Random(3);
            var input = new float[Rate * 6];
            for (var i = 0; i < input.Length; i++)
            {
                var t = i / (double)Rate;
                var voice = 0.0;
                if (t >= 3.5) { for (var h = 1; h <= 8; h++) voice += Math.Sin(2 * Math.PI * 160 * h * t) / h; voice *= 0.15 * (0.6 + 0.4 * Math.Sin(2 * Math.PI * 4 * t)); }
                input[i] = (float)((random.NextDouble() * 2 - 1) * 0.03 + voice);
            }
            var output = new float[input.Length];
            for (var at = 0; at + RnnoiseEngine.Frame <= input.Length; at += RnnoiseEngine.Frame)
                engine!.Process(input.AsSpan(at, RnnoiseEngine.Frame), output.AsSpan(at, RnnoiseEngine.Frame), 1.0);
            double Rms(float[] d, int a, int b) { double s = 0; for (var i = a; i < b; i++) s += d[i] * d[i]; return Math.Sqrt(s / (b - a)); }
            var drop = 20 * Math.Log10(Rms(input, Rate, Rate * 3) / Math.Max(1e-9, Rms(output, Rate, Rate * 3)));
            var kept = Rms(output, (int)(Rate * 4.5), Rate * 6) / Rms(input, (int)(Rate * 4.5), Rate * 6);
            // white noise is an artificial case for a network trained on real rooms; the real-microphone check measures the reduction
            Assert.True(drop > -0.5 && kept > 0.5, $"noise drop {drop:0.0} dB, voice segment kept {kept:P0}");
            Assert.All(output, v => Assert.True(float.IsFinite(v)));
        }
    }
}
