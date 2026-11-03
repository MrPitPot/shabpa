using System.IO;
using Shabpa.Core;

namespace Shabpa.Tests;

/// <summary>Switching the lights on and off with their fade effects, against a fake lighting server (never the real OpenRGB: the folder it would be fetched into is an empty one).</summary>
[Collection("OpenRgbHost")]
public class RgbSwitchTests : IDisposable
{
    private readonly string _saved = OpenRgbHost.Folder;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "shabpa-sw-" + Guid.NewGuid().ToString("N"));

    public RgbSwitchTests() => OpenRgbHost.Folder = Path.Combine(_root, "none");

    public void Dispose()
    {
        OpenRgbHost.Folder = _saved;
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private static bool LastFrameIsBlack(OpenRgbClientTests.FakeServer server, uint device)
    {
        lock (server.Seen)
        {
            var last = server.Seen.LastOrDefault(s => s.id == 1050 && s.device == device);
            if (last.payload is null) return false;
            var leds = BitConverter.ToUInt16(last.payload, 4);
            for (var i = 0; i < leds * 4; i++) if (i % 4 != 3 && last.payload[6 + i] != 0) return false;
            return true;
        }
    }

    private static bool WaitFor(Func<bool> condition, int ms = 8000)
    {
        var end = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < end) { if (condition()) return true; Thread.Sleep(40); }
        return condition();
    }

    [Fact]
    public void Lighting_that_was_left_on_starts_with_the_app_without_a_visit_to_the_lighting_page()
    {
        var on = new Settings();
        on.Rgb.Enabled = true;
        Assert.True(Hub.WantsRgbAtStart(on, dryRun: false, demo: false, tabOff: false));      // a restart or a boot: the lights come back by themselves
        Assert.False(Hub.WantsRgbAtStart(new Settings(), false, false, false));                // lighting off: nothing starts, nothing is downloaded or run
        Assert.False(Hub.WantsRgbAtStart(on, false, false, tabOff: true));                     // the tab is switched off
        Assert.False(Hub.WantsRgbAtStart(on, dryRun: true, demo: false, tabOff: false));       // test runs and demos never touch hardware
        Assert.False(Hub.WantsRgbAtStart(on, false, demo: true, false));
    }

    [Theory]
    [InlineData("blaze")]
    [InlineData("sunrise")]
    [InlineData("none")]
    public void Switching_the_lights_off_always_ends_with_every_device_dark_even_with_nothing_open(string fade)
    {
        using var server = new OpenRgbClientTests.FakeServer([(3, "Case fan", 12), (1, "RAM stick", 5), (0, "Board", 40)]);
        var settings = new Settings();
        settings.Rgb.Host = "127.0.0.1";
        settings.Rgb.Port = server.Port;
        settings.Rgb.Transition = fade;
        settings.Rgb.StartupEffect = false;
        settings.Rgb.All.Effect = "rainbow";
        settings.Rgb.All.Brightness = 100;
        using var service = new RgbService(() => settings, () => null, () => 0);
        settings.Rgb.Enabled = true;
        service.Refresh();
        Assert.True(WaitFor(() => service.Link == RgbLink.Sending), "the lights should be sent");
        Assert.True(WaitFor(() => { lock (server.Seen) return server.Seen.Count(s => s.id == 1050) >= 6; }));
        Assert.False(LastFrameIsBlack(server, 0));                                               // the rainbow is on

        settings.Rgb.Enabled = false;                                                            // switched off with no page open: only the loop can put the lights out
        service.BlackOut();
        Assert.True(WaitFor(() => LastFrameIsBlack(server, 0) && LastFrameIsBlack(server, 1) && LastFrameIsBlack(server, 2), 9000), "every device is dark");
        Assert.True(WaitFor(() => service.Link == RgbLink.Off, 3000));
    }

    [Fact]
    public void Switching_the_lights_on_and_off_again_and_again_always_ends_the_way_the_last_click_said()
    {
        using var server = new OpenRgbClientTests.FakeServer([(3, "Case fan", 12), (1, "RAM stick", 5)]);
        var settings = new Settings();
        settings.Rgb.Host = "127.0.0.1";
        settings.Rgb.Port = server.Port;
        settings.Rgb.StartupEffect = false;
        settings.Rgb.All.Effect = "rainbow";
        settings.Rgb.All.Brightness = 100;
        using var service = new RgbService(() => settings, () => null, () => 0);
        settings.Rgb.Enabled = true;
        service.Refresh();
        Assert.True(WaitFor(() => service.Link == RgbLink.Sending));
        for (var i = 0; i < 8; i++)
        {
            settings.Rgb.Enabled = i % 2 == 1;
            service.Refresh();
            if (!settings.Rgb.Enabled) service.BlackOut();
            Thread.Sleep(90 + i * 40);
        }
        // the last click was «on» (i = 7): the lights are lit and moving again
        Assert.True(settings.Rgb.Enabled);
        Assert.True(WaitFor(() => { lock (server.Seen) return !LastFrameIsBlack(server, 0) && server.Seen.Count(s => s.id == 1050) > 10; }), "on means lit");
        settings.Rgb.Enabled = false;
        service.BlackOut();
        Assert.True(WaitFor(() => LastFrameIsBlack(server, 0) && LastFrameIsBlack(server, 1), 9000), "the last click was off: every device is dark");
    }
}
