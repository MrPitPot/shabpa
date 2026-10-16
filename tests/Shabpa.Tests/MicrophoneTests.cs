using Shabpa.Core;

namespace Shabpa.Tests;

public class MicrophoneTests
{
    [Fact]
    public void Silence_measures_as_the_floor_and_a_full_scale_square_wave_as_zero_db()
    {
        var silence = new short[1600];
        Assert.Equal(Microphone.SilenceFloorDb, Microphone.Measure(silence).rmsDb);
        var loud = Enumerable.Range(0, 1600).Select(i => i % 2 == 0 ? short.MaxValue : short.MinValue).ToArray();
        var (rms, peak) = Microphone.Measure(loud);
        Assert.InRange(rms, -0.1, 0.01);
        Assert.InRange(peak, -0.1, 0.01);
    }

    [Fact]
    public void Half_amplitude_is_about_minus_six_db()
    {
        var half = Enumerable.Repeat((short)16384, 800).ToArray();
        var (rms, peak) = Microphone.Measure(half);
        Assert.InRange(rms, -6.1, -5.9);
        Assert.InRange(peak, -6.1, -5.9);
    }

    [Fact]
    public void Quiet_noise_is_measured_in_the_expected_range()
    {
        var random = new Random(1);
        var noise = Enumerable.Range(0, 16000).Select(_ => (short)random.Next(-100, 101)).ToArray(); // roughly -50 dB
        var (rms, _) = Microphone.Measure(noise);
        Assert.InRange(rms, -56, -54);
    }

    [Theory]
    [InlineData(-90, MicVerdict.Silent)]
    [InlineData(-70, MicVerdict.Excellent)]
    [InlineData(-55, MicVerdict.Good)]
    [InlineData(-45, MicVerdict.Noisy)]
    [InlineData(-30, MicVerdict.VeryNoisy)]
    public void The_verdict_follows_the_noise_floor(double db, MicVerdict expected) => Assert.Equal(expected, Microphone.Judge(db));

    [Fact]
    public void Every_verdict_has_advice_and_speech_advice_catches_clipping_and_quiet_voices()
    {
        foreach (var v in Enum.GetValues<MicVerdict>()) Assert.False(string.IsNullOrWhiteSpace(Microphone.Advice(v)));
        var clipping = Microphone.SpeechAdvice(-12, -0.2);
        var quiet = Microphone.SpeechAdvice(-45, -20);
        var good = Microphone.SpeechAdvice(-20, -6);
        Assert.NotEqual(clipping, good);
        Assert.NotEqual(quiet, good);
        Assert.NotEqual(clipping, quiet);
    }

    [Fact]
    public void Endpoint_names_are_read_from_the_registry_by_guid()
    {
        var reg = new FakeRegistry();
        var key = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture\{1111-2222}\Properties";
        reg.Values[(key, "{a45c254e-df1c-4efd-8020-67d146a850e0},2")] = "Microphone (USB)";
        Assert.Equal("Microphone (USB)", Microphone.NameFromEndpointId("{0.0.1.00000000}.{1111-2222}", reg));
        Assert.Null(Microphone.NameFromEndpointId("garbage", reg));
    }

    [Fact]
    public void App_names_are_made_readable()
    {
        Assert.Equal("chrome.exe", Microphone.Pretty("C:#Program Files#Google#Chrome#Application#chrome.exe"));
        Assert.Equal("Microsoft.WindowsCamera", Microphone.Pretty("Microsoft.WindowsCamera_8wekyb3d8bbwe"));
    }

    [Fact]
    public void File_times_are_converted_and_bad_values_are_ignored()
    {
        Assert.Null(Microphone.FromFileTime(0L));
        Assert.Null(Microphone.FromFileTime("text"));
        Assert.Null(Microphone.FromFileTime(long.MaxValue));
        var now = DateTime.Now;
        var back = Microphone.FromFileTime(now.ToFileTime());
        Assert.NotNull(back);
        Assert.InRange((back!.Value - now).TotalSeconds, -1, 1);
    }

    [Fact]
    public void Reading_the_real_devices_never_throws_even_without_a_microphone()
    {
        _ = Microphone.Read();
        _ = Microphone.DeviceName();
        _ = Microphone.RecentApps();
        _ = Microphone.DesktopAppsAllowed();
        Assert.Equal(2, Microphone.NoiseTools().Count);
    }
}
