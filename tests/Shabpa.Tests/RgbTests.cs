using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Shabpa.Core;
using Xunit;

namespace Shabpa.Tests;

public class RgbEngineTests
{
    [Fact]
    public void Every_effect_makes_a_valid_frame_at_any_moment_in_both_languages()
    {
        foreach (var e in RgbEffects.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Fa) || string.IsNullOrWhiteSpace(e.En), e.Key);
            foreach (var leds in new[] { 1, 2, 8, 12, 36 })
            foreach (var t in new[] { 0.0, 0.31, 1.7, 9.99, 123.4 })
            foreach (var level in new[] { 0.0, 0.5, 1.0 })
            {
                var frame = RgbEngine.Frame(new RgbLook(e, new Rgb(255, 45, 85), 1, 1), leds, t, level);
                Assert.Equal(leds, frame.Length);
            }
        }
    }

    [Fact]
    public void Effect_keys_are_unique_and_every_special_effect_is_in_a_known_group()
    {
        var all = RgbEffects.All;
        Assert.Equal(all.Count, all.Select(e => e.Key).Distinct().Count());
        Assert.All(RgbEffects.Special, e => Assert.Contains(e.Group, RgbEffects.Groups));
        Assert.All(RgbEffects.Special, e => Assert.NotEmpty(e.Palette));
        Assert.True(RgbEffects.Basic.Count >= 14, "the basic list should cover the usual lighting-program modes");
        Assert.True(RgbEffects.Special.Count >= 24);
        Assert.All(RgbEffects.All.SelectMany(e => e.Palette), hex => Assert.True(Rgb.TryParse(hex, out _), hex));
    }

    [Fact]
    public void Zero_brightness_is_dark_and_full_brightness_is_not()
    {
        foreach (var e in RgbEffects.Basic.Where(e => e.Pattern is not (RgbPattern.Flash or RgbPattern.DoubleFlash)))
        {
            var dark = RgbEngine.Frame(new RgbLook(e, new Rgb(255, 0, 0), 0, 1), 12, 0.7, 0.8);
            Assert.All(dark, c => Assert.Equal(Rgb.Black, c));
        }
        var lit = RgbEngine.Frame(new RgbLook(RgbEffects.Find("static"), new Rgb(10, 200, 30), 1, 1), 5, 3);
        Assert.All(lit, c => Assert.Equal(new Rgb(10, 200, 30), c));
        var half = RgbEngine.Frame(new RgbLook(RgbEffects.Find("static"), new Rgb(200, 100, 50), 0.5, 1), 3, 0);
        Assert.Equal(new Rgb(100, 50, 25), half[0]);
    }

    [Fact]
    public void The_same_inputs_give_the_same_frame_and_time_moves_the_moving_effects()
    {
        var look = new RgbLook(RgbEffects.Find("rainbow"), Rgb.Black, 1, 1);
        Assert.Equal(RgbEngine.Frame(look, 20, 2.5), RgbEngine.Frame(look, 20, 2.5));
        Assert.NotEqual(RgbEngine.Frame(look, 20, 0), RgbEngine.Frame(look, 20, 4));
        var staticFrame = RgbEngine.Frame(new RgbLook(RgbEffects.Find("static"), new Rgb(1, 2, 3), 1, 1), 4, 0);
        Assert.Equal(staticFrame, RgbEngine.Frame(new RgbLook(RgbEffects.Find("static"), new Rgb(1, 2, 3), 1, 1), 4, 77));
    }

    [Fact]
    public void Rainbow_goes_through_different_hues_along_the_strip()
    {
        var frame = RgbEngine.Frame(new RgbLook(RgbEffects.Find("rainbow"), Rgb.Black, 1, 1), 24, 0);
        Assert.True(frame.Select(c => c.Hex).Distinct().Count() > 12);
    }

    [Fact]
    public void Temperature_turns_from_green_to_red_and_music_follows_the_level()
    {
        var temp = RgbEffects.Find("temp");
        var cool = RgbEngine.Frame(new RgbLook(temp, Rgb.Black, 1, 1), 10, 0, 0.0)[0];
        var hot = RgbEngine.Frame(new RgbLook(temp, Rgb.Black, 1, 1), 10, 0, 1.0)[0];
        Assert.True(cool.G > cool.R && hot.R > hot.G, $"{cool} -> {hot}");
        var music = RgbEffects.Find("music");
        int Lit(double level) => RgbEngine.Frame(new RgbLook(music, new Rgb(255, 0, 80), 1, 1), 20, 0, level).Count(c => c.R + c.G + c.B > 120);
        Assert.True(Lit(1.0) > Lit(0.2));
        Assert.True(Lit(0.2) >= Lit(0.0));
    }

    [Fact]
    public void A_frame_can_be_stretched_to_any_led_count()
    {
        var frame = new[] { new Rgb(0, 0, 0), new Rgb(100, 100, 100), new Rgb(200, 200, 200) };
        var more = RgbEngine.Resample(frame, 5);
        Assert.Equal(5, more.Length);
        Assert.Equal(frame[0], more[0]);
        Assert.Equal(frame[2], more[4]);
        Assert.Equal(new Rgb(50, 50, 50), more[1]);
        Assert.Single(RgbEngine.Resample(frame, 1));
        Assert.Equal(7, RgbEngine.Resample([], 7).Length);
    }

    [Fact]
    public void Colours_round_trip_through_text_and_hue()
    {
        Assert.True(Rgb.TryParse("#FF8A1F", out var c));
        Assert.Equal(new Rgb(255, 138, 31), c);
        Assert.Equal("FF8A1F", c.Hex);
        Assert.False(Rgb.TryParse("nope", out _));
        Assert.Equal(0.0, new Rgb(255, 0, 0).Hue, 3);
        Assert.Equal(1 / 3.0, new Rgb(0, 255, 0).Hue, 3);
        Assert.Equal(new Rgb(0, 0, 255), Rgb.FromHsv(2 / 3.0, 1, 1));
    }

    [Fact]
    public void The_look_of_a_zone_follows_sync_own_settings_and_custom_effects()
    {
        var rgb = new RgbSettings();
        rgb.All.Effect = "rainbow"; rgb.All.Brightness = 40;
        Assert.Equal("rainbow", RgbService.LookOf(rgb, "fans").Effect.Key);
        Assert.Equal(0.4, RgbService.LookOf(rgb, "ram").Brightness, 3);
        rgb.Sync = false;
        rgb.ZoneSetting("ram").Effect = "static";
        rgb.ZoneSetting("ram").Brightness = 100;
        Assert.Equal("static", RgbService.LookOf(rgb, "ram").Effect.Key);
        Assert.Equal("rainbow", RgbService.LookOf(rgb, "fans").Effect.Key);          // a zone never touched starts as a copy of "all"
        var mine = new RgbCustomEffect { Name = "x", Colors = ["FF0000", "0000FF"], Pattern = "Chase" };
        rgb.Custom.Add(mine);
        rgb.ZoneSetting("gpu").Effect = "custom:" + mine.Id;
        var look = RgbService.LookOf(rgb, "gpu");
        Assert.Equal(RgbPattern.Chase, look.CustomPattern);
        Assert.Equal(2, look.CustomPalette!.Length);
        Assert.Equal(12, RgbEngine.Frame(look, 12, 1).Length);
        rgb.ZoneSetting("keys").Effect = "custom:gone";                              // a deleted custom effect falls back, never crashes
        Assert.Equal("static", RgbService.LookOf(rgb, "keys").Effect.Key);
    }

    [Fact]
    public void Old_settings_files_load_with_lighting_off_and_synced()
    {
        var s = System.Text.Json.JsonSerializer.Deserialize<Settings>("{}")!;
        Assert.False(s.Rgb.Enabled);
        Assert.True(s.Rgb.Sync);
        Assert.Equal(6742, s.Rgb.Port);
    }
}

public class RgbRivalTests
{
    [Fact]
    public void Known_lighting_programs_are_found_by_process_or_service_whatever_the_spacing_or_case()
    {
        var found = RgbRivals.Find(["chrome", "MSI Center", "icue", "notepad", "Armoury-Crate"], ["Mystic_Light_Service", "wuauserv"]);
        var names = found.Select(f => f.rival.Name).ToList();
        Assert.Contains(names, n => n.Contains("MSI"));
        Assert.Contains(names, n => n.Contains("Corsair"));
        Assert.DoesNotContain(names, n => n.Contains("Razer"));
        var msi = found.First(f => f.rival.Name.Contains("MSI"));
        Assert.Contains("Mystic_Light_Service", msi.services);
        Assert.Contains("MSI Center", msi.processes);
    }

    [Fact]
    public void A_clean_system_shows_nothing()
    {
        Assert.Empty(RgbRivals.Find(["explorer", "chrome", "Shabpa", "OpenRGB"], ["Dnscache"]));      // OpenRGB is the helper, not a rival
    }
}

/// <summary>A tiny stand-in for OpenRGB's SDK server, speaking the packet format Shabpa uses, to check the client end to end without hardware.
/// It proves Shabpa agrees with itself about the format; the real program still has to be tried by hand.</summary>
public class OpenRgbClientTests
{
    private static byte[] Str(string s) { var b = Encoding.UTF8.GetBytes(s + "\0"); return [.. BitConverter.GetBytes((ushort)b.Length), .. b]; }

    private static byte[] Controller(int type, string name, int leds, bool withModesAndZones = true, int? max = null)
    {
        var body = new List<byte>();
        body.AddRange(BitConverter.GetBytes(type));
        body.AddRange(Str(name));
        body.AddRange(Str("Vendor"));
        body.AddRange(Str("description")); body.AddRange(Str("1.0")); body.AddRange(Str("serial")); body.AddRange(Str("location"));
        body.AddRange(BitConverter.GetBytes((ushort)(withModesAndZones ? 2 : 0)));       // modes
        body.AddRange(BitConverter.GetBytes(0));                                          // active mode
        if (withModesAndZones)
            foreach (var mode in new[] { "Direct", "Static" })
            {
                body.AddRange(Str(mode));
                body.AddRange(new byte[4 * 4]);                                           // value, flags, speed min/max
                body.AddRange(new byte[8]);                                               // brightness min/max (protocol 3)
                body.AddRange(new byte[12]);                                              // colours min/max, speed
                body.AddRange(new byte[4]);                                               // brightness
                body.AddRange(new byte[8]);                                               // direction, colour mode
                body.AddRange(BitConverter.GetBytes((ushort)1));
                body.AddRange(new byte[4]);                                               // one mode colour
            }
        body.AddRange(BitConverter.GetBytes((ushort)(withModesAndZones ? 1 : 0)));        // zones
        if (withModesAndZones)
        {
            body.AddRange(Str("Zone 1"));
            body.AddRange(BitConverter.GetBytes(1)); body.AddRange(BitConverter.GetBytes(0)); body.AddRange(BitConverter.GetBytes(max ?? leds)); body.AddRange(BitConverter.GetBytes(leds));
            body.AddRange(BitConverter.GetBytes((ushort)0));
        }
        body.AddRange(BitConverter.GetBytes((ushort)leds));
        for (var i = 0; i < leds; i++) { body.AddRange(Str("LED " + i)); body.AddRange(BitConverter.GetBytes(i)); }
        body.AddRange(BitConverter.GetBytes((ushort)leds));
        body.AddRange(new byte[leds * 4]);
        return [.. BitConverter.GetBytes(body.Count + 4), .. body];
    }

    [Fact]
    public void Controller_records_give_the_right_device_zone_and_led_count()
    {
        var ram = OpenRgbClient.ParseController(1, Controller(1, "Corsair Vengeance RGB", 10), 3)!;
        Assert.Equal(("ram", 10, "Corsair Vengeance RGB"), (ram.Zone, ram.Leds, ram.Name));
        var fan = OpenRgbClient.ParseController(2, Controller(3, "Lian Li Uni Fan", 48), 3)!;
        Assert.Equal(("fans", 48), (fan.Zone, fan.Leds));
        var pump = OpenRgbClient.ParseController(3, Controller(3, "NZXT Kraken X73 pump", 24), 3)!;
        Assert.Equal("cooler", pump.Zone);
        Assert.Equal("board", OpenRgbClient.ParseController(4, Controller(0, "MSI MPG Z790", 6), 3)!.Zone);
        Assert.Equal("gpu", OpenRgbClient.ParseController(5, Controller(2, "ASUS ROG GPU", 4), 3)!.Zone);
        Assert.Equal("keyboard", OpenRgbClient.ParseController(6, Controller(5, "Keyboard", 104), 3)!.Zone);
        Assert.Equal("mouse", OpenRgbClient.ParseController(8, Controller(6, "Mouse", 3), 3)!.Zone);
        Assert.Equal("strip", OpenRgbClient.ParseController(7, Controller(4, "LED strip", 60), 3)!.Zone);
    }

    [Fact]
    public void A_record_the_walk_cannot_follow_still_gives_its_led_count_from_the_closing_colour_list()
    {
        var data = Controller(1, "Some DIMM", 9, withModesAndZones: true).ToList();
        data.InsertRange(60, new byte[] { 0xAA, 0xBB, 0xCC });                                  // a field a newer server added somewhere in the middle
        var device = OpenRgbClient.ParseController(0, [.. data], 3);
        Assert.NotNull(device);
        Assert.Equal(9, device!.Leds);
    }

    [Fact]
    public void Nonsense_never_throws()
    {
        Assert.Null(OpenRgbClient.ParseController(0, [], 3));
        Assert.Null(OpenRgbClient.ParseController(0, [1, 2, 3, 4, 5], 3));
        Assert.Null(OpenRgbClient.ParseController(0, new byte[40], 3));
    }

    internal sealed class FakeServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public readonly List<(uint device, uint id, byte[] payload)> Seen = [];
        private readonly Task _loop;
        private readonly List<TcpClient> _clients = [];
        private readonly Dictionary<uint, int> _current = [];   // guarded by itself
        public int UpdateDelayMs;
        /// <summary>A delay of its own for one device (milliseconds), when set it wins over <see cref="UpdateDelayMs"/>.</summary>
        public Func<uint, int>? DelayOf;
        /// <summary>How many devices the server says it has now (a real one is still looking for hardware for a while after it opens its port).</summary>
        public volatile int Count;
        public FakeServer((int type, string name, int leds)[] devices, int[]? startCounts = null)
        {
            for (var i = 0; i < devices.Length; i++) _current[(uint)i] = startCounts?[i] ?? devices[i].leds;
            Count = devices.Length;
            _listener.Start();
            // every connection (the first one lists the devices, and each device gets one of its own for its frames) is served on its own thread, like the real server does
            _loop = Task.Run(() =>
            {
                try
                {
                    while (true)
                    {
                        var accepted = _listener.AcceptTcpClient();
                        lock (_clients) _clients.Add(accepted);
                        _ = Task.Run(() => Serve(accepted, devices));
                    }
                }
                catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or InvalidOperationException) { }
            });
        }

        private void Serve(TcpClient client, (int type, string name, int leds)[] devices)
        {
            using (client)
            {
                try
                {
                    using var stream = client.GetStream();
                    while (true)
                    {
                        var header = Read(stream, 16);
                        var device = BitConverter.ToUInt32(header, 4);
                        var id = BitConverter.ToUInt32(header, 8);
                        var payload = Read(stream, (int)BitConverter.ToUInt32(header, 12));
                        lock (Seen) Seen.Add((device, id, payload));
                        void Reply(uint rid, byte[] data) { var p = new List<byte>("ORGB"u8.ToArray()); p.AddRange(BitConverter.GetBytes(device)); p.AddRange(BitConverter.GetBytes(rid)); p.AddRange(BitConverter.GetBytes((uint)data.Length)); p.AddRange(data); stream.Write([.. p]); }
                        if (id == 40) Reply(40, BitConverter.GetBytes(3u));
                        else if (id == 0) Reply(0, BitConverter.GetBytes((uint)Count));
                        else if (id == 1050 && (DelayOf?.Invoke(device) ?? UpdateDelayMs) is var delay && delay > 0) Thread.Sleep(delay);
                        else if (id == 1000) lock (_current) _current[device] = BitConverter.ToInt32(payload, 4);
                        else if (id == 1) { int now; lock (_current) now = _current[device]; Reply(1, Controller(devices[device].type, devices[device].name, now, true, devices[device].leds)); }
                    }
                }
                catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or InvalidOperationException) { }
            }
        }
        private static byte[] Read(NetworkStream s, int n) { var b = new byte[n]; var r = 0; while (r < n) { var k = s.Read(b, r, n - r); if (k <= 0) throw new IOException(); r += k; } return b; }
        public void Dispose()
        {
            _listener.Stop();
            lock (_clients) foreach (var c in _clients) { try { c.Close(); } catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException) { } }
            try { _loop.Wait(500); } catch (AggregateException) { }
        }
    }

    [Fact]
    public void The_client_lists_the_devices_and_sends_each_one_its_zone_frame_stretched_to_its_led_count()
    {
        using var server = new FakeServer([(1, "RAM stick", 5), (3, "Case fan", 12)]);
        using var client = new OpenRgbClient();
        Assert.True(client.Connect("127.0.0.1", server.Port), client.LastError);
        Assert.Equal(2, client.Devices.Count);
        Assert.Equal(["ram", "fans"], client.Devices.Select(d => d.Zone));

        var ok = client.Update(d => d.Zone == "ram" ? [new Rgb(255, 0, 0), new Rgb(0, 0, 255)] : d.Zone == "fans" ? [new Rgb(1, 2, 3)] : null);
        Assert.True(ok);
        Thread.Sleep(150);
        List<(uint device, uint id, byte[] payload)> updates;
        lock (server.Seen) updates = [.. server.Seen.Where(s => s.id == 1050)];
        Assert.Equal(2, updates.Count);
        var ram = updates.First(u => u.device == 0).payload;
        Assert.Equal(4 + 2 + 5 * 4, ram.Length);
        Assert.Equal(5, BitConverter.ToUInt16(ram, 4));
        Assert.Equal((255, 0, 0), (ram[6], ram[7], ram[8]));                               // first LED red
        Assert.Equal((0, 0, 255), (ram[6 + 16], ram[7 + 16], ram[8 + 16]));                // last LED blue
        var fan = updates.First(u => u.device == 1).payload;
        Assert.Equal(12, BitConverter.ToUInt16(fan, 4));
        Assert.Equal((1, 2, 3), (fan[6], fan[7], fan[8]));
        lock (server.Seen) Assert.Equal(2, server.Seen.Count(s => s.id == 1100));          // both devices were put in direct control
    }

    [Fact]
    public void A_server_that_is_still_looking_for_hardware_gets_asked_again_and_the_new_devices_appear()
    {
        using var server = new FakeServer([(1, "RAM stick", 5), (3, "Case fan", 12)]);
        server.Count = 0;
        using var client = new OpenRgbClient();
        Assert.True(client.Connect("127.0.0.1", server.Port), client.LastError);
        Assert.Empty(client.Devices);                                  // it listened before it had found anything
        Assert.False(client.Reload());                                 // still nothing: no change

        server.Count = 1;
        Assert.True(client.Reload());
        Assert.Equal(["1|RAM stick"], client.Devices.Select(d => d.Key));
        server.Count = 2;
        Assert.True(client.Reload());
        Assert.Equal(2, client.Devices.Count);
        Assert.False(client.Reload());                                 // the same number: nothing is read again
        Assert.True(client.Connected);
        Assert.True(client.Update(_ => [new Rgb(1, 2, 3)]));           // and the new devices take frames
    }

    [Fact]
    public void Nothing_listening_means_not_connected_and_no_exception()
    {
        var port = new TcpListener(IPAddress.Loopback, 0);
        port.Start();
        var free = ((IPEndPoint)port.LocalEndpoint).Port;
        port.Stop();
        using var client = new OpenRgbClient();
        Assert.False(client.Connect("127.0.0.1", free, 400));
        Assert.False(client.Connected);
        Assert.False(client.Update(_ => [Rgb.Black]));
    }

    [Fact]
    public void Identical_devices_get_different_keys_that_stay_the_same_between_runs()
    {
        using var server = new FakeServer([(1, "RAM stick", 5), (1, "RAM stick", 5), (3, "Case fan", 12)]);
        using var client = new OpenRgbClient();
        Assert.True(client.Connect("127.0.0.1", server.Port), client.LastError);
        Assert.Equal(["1|RAM stick", "1|RAM stick#1", "3|Case fan"], client.Devices.Select(d => d.Key));
    }

    [Fact]
    public void A_connection_only_looks_at_the_devices_and_takes_control_when_the_first_frame_is_sent()
    {
        using var server = new FakeServer([(1, "RAM stick", 5)]);
        using var client = new OpenRgbClient();
        Assert.True(client.Connect("127.0.0.1", server.Port), client.LastError);
        Thread.Sleep(100);
        lock (server.Seen) Assert.DoesNotContain(server.Seen, s => s.id == 1100);          // listing changes nothing on the device
        Assert.True(client.Update(_ => [new Rgb(9, 9, 9)]));
        Thread.Sleep(150);
        lock (server.Seen) Assert.Single(server.Seen, s => s.id == 1100);
    }

    [Fact]
    public void A_frame_that_did_not_change_is_not_sent_again_and_a_changed_one_is()
    {
        using var server = new FakeServer([(3, "Case fan", 4)]);
        using var client = new OpenRgbClient();
        Assert.True(client.Connect("127.0.0.1", server.Port), client.LastError);
        Rgb[] red = [new Rgb(255, 0, 0)], blue = [new Rgb(0, 0, 255)];
        client.Update(_ => red);
        Thread.Sleep(60);
        client.Update(_ => red);                                                              // same colours: nothing to send
        Thread.Sleep(60);
        client.Update(_ => blue);
        Thread.Sleep(200);
        lock (server.Seen) Assert.Equal(2, server.Seen.Count(s => s.id == 1050));
    }

    [Fact]
    public void A_look_can_run_the_other_way_and_show_as_stars()
    {
        var look = new RgbLook(RgbEffects.Find("rainbow"), new Rgb(255, 0, 0), 1, 1);
        var forward = RgbEngine.Frame(look, 10, 3);
        var backward = RgbEngine.Frame(look with { Reverse = true }, 10, 3);
        Assert.Equal(forward.Reverse(), backward);
        var plain = RgbEngine.Frame(look, 30, 1.25);
        var starry = RgbEngine.Frame(look with { Starry = true }, 30, 1.25);
        Assert.Equal(30, starry.Length);
        Assert.True(starry.Sum(c => c.R + c.G + c.B) < plain.Sum(c => c.R + c.G + c.B));      // a dim base with a few bright stars is darker than the full flow
        Assert.Contains(starry.Select((c, i) => c.R + c.G + c.B > (plain[i].R + plain[i].G + plain[i].B) * 0.3), b => b);
    }

    [Fact]
    public void A_device_setting_is_made_once_and_remembers_its_own_look()
    {
        var rgb = new RgbSettings();
        rgb.All.Effect = "aurora"; rgb.All.Brightness = 70;
        var d = rgb.DeviceSetting("5|Keyboard");
        Assert.Same(d, rgb.DeviceSetting("5|Keyboard"));
        Assert.True(d.Enabled);
        Assert.False(d.Own);
        Assert.Equal(("aurora", 70), (d.Look.Effect, d.Look.Brightness));
        Assert.Null(rgb.FindDevice("1|Other"));
        d.Look.Effect = "lava"; d.Look.Reverse = true; d.Own = true;
        var look = RgbService.LookOf(rgb, d.Look);
        Assert.Equal(("lava", true), (look.Effect.Key, look.Reverse));
    }

    [Fact]
    public void The_zones_include_the_keyboard_and_the_mouse_apart()
    {
        var keys = RgbZones.All.Select(z => z.Key).ToList();
        Assert.Contains("keyboard", keys);
        Assert.Contains("mouse", keys);
        Assert.DoesNotContain("keys", keys);
    }

    [Fact]
    public void A_page_of_fixed_colours_is_still_and_any_moving_effect_is_not()
    {
        var rgb = new RgbSettings();
        Assert.True(RgbService.IsStill(rgb));                                            // the default look is a static colour
        rgb.All.Effect = "rainbow";
        Assert.False(RgbService.IsStill(rgb));
        rgb.All.Brightness = 0;
        Assert.True(RgbService.IsStill(rgb));                                            // dark is still too
        rgb.All.Brightness = 80; rgb.All.Effect = "static";
        rgb.DeviceSetting("5|Keyboard").Look.Effect = "aurora";
        Assert.True(RgbService.IsStill(rgb));                                            // a look that is not in use does not count
        rgb.DeviceSetting("5|Keyboard").Own = true;
        Assert.False(RgbService.IsStill(rgb));
    }

    [Fact]
    public void Headers_whose_size_can_be_set_are_raised_to_the_maximum_unless_the_person_chose_a_size()
    {
        using var server = new FakeServer([(3, "ARGB hub", 24), (3, "Other hub", 40)], [4, 8]);
        using var client = new OpenRgbClient();
        Assert.True(client.Connect("127.0.0.1", server.Port), client.LastError);
        Assert.Equal([4, 8], client.Devices.Select(d => d.Leds));
        Assert.All(client.Devices, d => Assert.True(d.Zones[0].Resizable));
        var typed = new Dictionary<string, int> { ["3|Other hub|Zone 1"] = 16 };              // the person typed 16 for the second one
        var changed = client.ApplySizes((d, z) => typed.TryGetValue(RgbService.SizeKey(d, z), out var n) ? n : z.Max);
        Assert.Equal(2, changed);
        Assert.Equal([24, 16], client.Devices.Select(d => d.Leds));
        Assert.Equal(0, client.ApplySizes((d, z) => typed.TryGetValue(RgbService.SizeKey(d, z), out var n) ? n : z.Max));   // nothing left to do
        Thread.Sleep(100);
        lock (server.Seen) Assert.Equal(2, server.Seen.Count(x => x.id == 1000));
    }

    [Fact]
    public void A_header_size_outside_what_the_device_allows_is_not_sent()
    {
        using var server = new FakeServer([(3, "ARGB hub", 24)], [4]);
        using var client = new OpenRgbClient();
        Assert.True(client.Connect("127.0.0.1", server.Port), client.LastError);
        Assert.Equal(0, client.ApplySizes((_, _) => 99));
        lock (server.Seen) Assert.DoesNotContain(server.Seen, x => x.id == 1000);
    }

    [Theory]
    [InlineData("blaze")]
    [InlineData("sunrise")]
    [InlineData("center")]
    [InlineData("darkwave")]
    [InlineData("soft")]
    public void Every_fade_goes_from_dark_to_the_frame_and_never_gets_dimmer_as_it_goes(string style)
    {
        var frame = Enumerable.Repeat(new Rgb(200, 120, 40), 24).ToArray();
        long Total(Rgb[] f) => f.Sum(c => (long)c.R + c.G + c.B);
        Assert.Equal(0, Total(RgbTransitions.Apply(frame, style, 0)));
        Assert.Equal(frame, RgbTransitions.Apply(frame, style, 1));
        long last = -1;
        for (var p = 0.0; p <= 1.0001; p += 0.05)
        {
            var now = Total(RgbTransitions.Apply(frame, style, p));
            Assert.True(now >= last - 40, $"{style} at {p:0.00}: {now} < {last}");          // blaze's orange front may wobble a little, nothing else
            last = now;
        }
    }

    [Fact]
    public void No_effect_means_the_frame_comes_through_at_once_and_every_style_has_a_name()
    {
        var frame = new[] { new Rgb(1, 2, 3) };
        Assert.Equal(frame, RgbTransitions.Apply(frame, "none", 0));
        Assert.All(RgbTransitions.Styles, st => Assert.True(RgbTransitions.Name(st.Key).Length > 0));
        Assert.Equal("blaze", new RgbSettings().Transition);
        var d = new RgbSettings();
        Assert.True(d.OffOnSleep && d.StartupEffect && d.Smooth);
        Assert.False(d.PauseInGame || d.KeepRivalsClosed);
    }

    [Fact]
    public void Synchronised_lights_take_their_colours_from_one_flow_laid_along_all_of_them()
    {
        var look = new RgbLook(RgbEffects.Find("rainbow"), new Rgb(255, 0, 0), 1, 1);
        var global = RgbEngine.Frame(look, 100, 2.0);
        var first = RgbEngine.Slice(global, 0, 0.5, 50);
        var second = RgbEngine.Slice(global, 0.5, 1, 50);
        Assert.Equal(global[0], first[0]);
        Assert.Equal(global[99], second[49]);
        Assert.NotEqual(first[49], second[0] == default ? first[0] : Rgb.Black);
        // the end of the first stretch and the start of the second are neighbours on the flow: nearly the same colour
        Assert.True(Math.Abs(first[49].R - second[0].R) + Math.Abs(first[49].G - second[0].G) + Math.Abs(first[49].B - second[0].B) < 60);
        Assert.Equal(7, RgbEngine.Slice(global, 0.2, 0.3, 7).Length);
    }

    [Fact]
    public void A_devices_one_colour_switch_survives_saving()
    {
        var rgb = new RgbSettings();
        var d = rgb.DeviceSetting("3|Pump");
        d.OneColour = true;
        var back = System.Text.Json.JsonSerializer.Deserialize<RgbSettings>(System.Text.Json.JsonSerializer.Serialize(rgb))!;
        Assert.True(back.FindDevice("3|Pump")!.OneColour);
        Assert.False(new RgbDeviceSetting().OneColour);
    }

    [Fact]
    public void A_rainbow_on_one_device_shows_several_colours_and_turns_with_time()
    {
        double HueSpan(Rgb[] f) { var d = Math.Abs(f[^1].Hue - f[0].Hue); return Math.Min(d, 1 - d); }
        var plain = new RgbLook(RgbEffects.Find("rainbow"), new Rgb(255, 0, 0), 1, 1);
        var spread = plain;
        var eighthPlain = RgbEngine.Frame(plain with { Spread = 0.3 }, 20, 1.0);
        var eighthSpread = RgbEngine.Frame(spread, 20, 1.0);
        Assert.True(HueSpan(eighthSpread) > 0.3, $"{HueSpan(eighthSpread):0.00}");           // a device shows a good part of the colour circle, not one colour
        Assert.True(HueSpan(eighthSpread) > HueSpan(eighthPlain) * 2);
        var later = RgbEngine.Frame(spread, 20, 3.0);
        Assert.NotEqual(eighthSpread[0], later[0]);                                           // and it keeps turning
        Assert.NotEqual(eighthSpread[0], eighthSpread[^1]);                                   // while two ends of one device differ at any moment
    }

    [Fact]
    public void Frames_never_pile_up_behind_a_slow_device_and_its_delay_is_measured()
    {
        using var server = new FakeServer([(3, "Slow hub", 4), (3, "Quick fan", 4)]) { UpdateDelayMs = 0 };
        using var client = new OpenRgbClient();
        Assert.True(client.Connect("127.0.0.1", server.Port), client.LastError);
        server.UpdateDelayMs = 80;                                                            // every frame now takes 80 ms to put on a device
        for (var i = 0; i < 40; i++)
        {
            var colour = new Rgb((byte)(10 + i * 5), 0, 0);
            Assert.True(client.Update(_ => [colour], wait: false));                           // the caller is never slowed down
            Thread.Sleep(25);                                                                 // one second of frames at 40 a second
        }
        client.Flush(2000);
        Assert.InRange(client.CostMs(0), 50, 400);                                            // the measured delay of a device
        lock (server.Seen) Assert.InRange(server.Seen.Count(x => x.id == 1050), 8, 28);       // about ten frames a second for each of the two devices, not forty
    }

    [Fact]
    public void A_quick_device_costs_next_to_nothing_and_is_not_held_back()
    {
        using var server = new FakeServer([(3, "Quick fan", 4)]);
        using var client = new OpenRgbClient();
        Assert.True(client.Connect("127.0.0.1", server.Port), client.LastError);
        client.Update(_ => [new Rgb(1, 1, 1)]);
        Assert.InRange(client.CostMs(0), 0, 30);
        Thread.Sleep(60);
        client.Update(_ => [new Rgb(2, 2, 2)]);
        Thread.Sleep(80);
        lock (server.Seen) Assert.Equal(2, server.Seen.Count(x => x.id == 1050));
    }

    [Fact]
    public void A_slow_device_does_not_hold_up_a_quick_one()
    {
        using var server = new FakeServer([(1, "Slow RAM", 4), (3, "Quick fan", 4)]) { DelayOf = d => d == 0 ? 150 : 0 };
        using var client = new OpenRgbClient { Smooth = true };
        Assert.True(client.Connect("127.0.0.1", server.Port), client.LastError);
        var started = Environment.TickCount64;
        for (var i = 0; i < 25; i++)
        {
            var colour = new Rgb((byte)(i * 9), 0, 0);
            Assert.True(client.Update(_ => [colour], wait: false));
            Thread.Sleep(20);
        }
        client.Flush(2000);
        var elapsed = Environment.TickCount64 - started;
        int slow, quick;
        lock (server.Seen) { slow = server.Seen.Count(x => x.id == 1050 && x.device == 0); quick = server.Seen.Count(x => x.id == 1050 && x.device == 1); }
        Assert.True(quick >= 10, $"the quick device got only {quick} frames in {elapsed} ms");              // it kept its own pace
        Assert.True(slow <= 6, $"the slow device got {slow} frames");                                       // and the slow one was never queued up
        Assert.True(quick > slow * 2);
    }

    [Fact]
    public void A_frame_that_had_to_wait_for_a_busy_device_is_drawn_for_the_moment_it_goes_out()
    {
        using var server = new FakeServer([(3, "Slow hub", 4), (3, "Quick fan", 4)]) { DelayOf = d => d == 0 ? 120 : 0 };
        using var client = new OpenRgbClient { Smooth = true };
        Assert.True(client.Connect("127.0.0.1", server.Port), client.LastError);
        var lates = new Dictionary<int, List<double>> { [0] = [], [1] = [] };
        void Offer(int i) => Assert.True(client.UpdateAt((device, late) => { lock (lates) lates[device.Index].Add(late); return [new Rgb((byte)(i * 20), 1, 1)]; }, wait: false));
        Offer(1);
        Thread.Sleep(10);
        Offer(2);                                                                             // the slow device is busy with the first frame: this one waits for it
        client.Flush(2000);
        lock (lates)
        {
            Assert.Contains(lates[0], l => l >= 0.07);                                        // drawn when the slow device was ready, a good while after it was offered
            Assert.All(lates[0], l => Assert.InRange(l, 0, 1));
            Assert.All(lates[1], l => Assert.InRange(l, 0, 0.05));                            // the quick device is never made to wait
        }
    }

    [Fact]
    public void A_server_that_goes_away_is_noticed_by_the_next_update()
    {
        var server = new FakeServer([(3, "Quick fan", 4)]);
        using var client = new OpenRgbClient();
        Assert.True(client.Connect("127.0.0.1", server.Port), client.LastError);
        Assert.True(client.Update(_ => [new Rgb(1, 1, 1)]));
        server.Dispose();
        var ok = true;
        for (var i = 0; i < 20 && ok; i++) { ok = client.Update(_ => [new Rgb((byte)(i + 2), 2, 2)], force: true); Thread.Sleep(50); }
        Assert.False(ok);
        Assert.False(client.Connected);
    }
}
