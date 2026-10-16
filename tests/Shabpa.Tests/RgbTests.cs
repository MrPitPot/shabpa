using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Shabpa.Core;
using Xunit;

namespace Shabpa.Tests;

public sealed class RgbTests
{
    // ---- a stand-in OpenRGB server, written from the published SDK description ------------------------------

    private static byte[] Str(string s) => [.. BitConverter.GetBytes((ushort)(s.Length + 1)), .. Encoding.UTF8.GetBytes(s), 0];

    private static byte[] DeviceData(string name, int leds, int version)
    {
        var body = new List<byte>();
        body.AddRange(BitConverter.GetBytes(6)); // type: keyboard
        body.AddRange(Str(name));
        if (version >= 1) body.AddRange(Str("TestVendor"));
        body.AddRange(Str("description")); body.AddRange(Str("1.0")); body.AddRange(Str("serial")); body.AddRange(Str("loc"));
        body.AddRange(BitConverter.GetBytes((ushort)1)); // one mode
        body.AddRange(BitConverter.GetBytes(0)); // active mode
        body.AddRange(Str("Direct")); body.AddRange(BitConverter.GetBytes(0)); body.AddRange(BitConverter.GetBytes(0u)); // name, value, flags
        body.AddRange(BitConverter.GetBytes(0u)); body.AddRange(BitConverter.GetBytes(0u)); // speed min/max
        if (version >= 3) { body.AddRange(BitConverter.GetBytes(0u)); body.AddRange(BitConverter.GetBytes(100u)); } // brightness min/max
        body.AddRange(BitConverter.GetBytes(0u)); body.AddRange(BitConverter.GetBytes(0u)); // colours min/max
        body.AddRange(BitConverter.GetBytes(0u)); // speed
        if (version >= 3) body.AddRange(BitConverter.GetBytes(100u)); // brightness
        body.AddRange(BitConverter.GetBytes(0u)); body.AddRange(BitConverter.GetBytes(1u)); // direction, colour mode
        body.AddRange(BitConverter.GetBytes((ushort)1)); body.AddRange(new byte[] { 255, 0, 0, 0 }); // one mode colour
        body.AddRange(BitConverter.GetBytes((ushort)1)); // one zone
        body.AddRange(Str("Zone")); body.AddRange(BitConverter.GetBytes(1)); body.AddRange(BitConverter.GetBytes((uint)leds)); body.AddRange(BitConverter.GetBytes((uint)leds)); body.AddRange(BitConverter.GetBytes((uint)leds));
        body.AddRange(BitConverter.GetBytes((ushort)0)); // no matrix
        body.AddRange(BitConverter.GetBytes((ushort)leds));
        for (var i = 0; i < leds; i++) { body.AddRange(Str($"LED {i}")); body.AddRange(BitConverter.GetBytes(0u)); }
        body.AddRange(BitConverter.GetBytes((ushort)leds));
        for (var i = 0; i < leds; i++) body.AddRange(new byte[] { 0, 0, 0, 0 });
        return [.. BitConverter.GetBytes((uint)(body.Count + 4)), .. body];
    }

    private sealed class Mock : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public readonly List<(int device, int id, byte[] data)> Received = [];
        private readonly Task _task;
        private readonly int _serverVersion;

        public Mock(int serverVersion = 3)
        {
            _serverVersion = serverVersion;
            _listener.Start();
            _task = Task.Run(Serve);
        }

        private async Task Serve()
        {
            using var client = await _listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var negotiated = 0;
            try
            {
                while (true)
                {
                    var h = new byte[16];
                    var got = 0;
                    while (got < 16) { var n = await stream.ReadAsync(h.AsMemory(got, 16 - got)); if (n == 0) return; got += n; }
                    var device = BitConverter.ToInt32(h, 4);
                    var id = BitConverter.ToInt32(h, 8);
                    var size = BitConverter.ToInt32(h, 12);
                    var data = new byte[size];
                    got = 0;
                    while (got < size) { var n = await stream.ReadAsync(data.AsMemory(got, size - got)); if (n == 0) return; got += n; }
                    lock (Received) Received.Add((device, id, data));
                    async Task Reply(int rid, byte[] payload)
                    {
                        var head = new byte[16];
                        "ORGB"u8.CopyTo(head);
                        BitConverter.GetBytes(device).CopyTo(head, 4);
                        BitConverter.GetBytes(rid).CopyTo(head, 8);
                        BitConverter.GetBytes(payload.Length).CopyTo(head, 12);
                        await stream.WriteAsync(head);
                        await stream.WriteAsync(payload);
                    }
                    switch (id)
                    {
                        case 40:
                            negotiated = (int)Math.Min(BitConverter.ToUInt32(data, 0), (uint)_serverVersion);
                            await Reply(40, BitConverter.GetBytes((uint)_serverVersion));
                            break;
                        case 0: await Reply(0, BitConverter.GetBytes(2u)); break;
                        case 1: await Reply(1, DeviceData(device == 0 ? "Test Keyboard" : "Test Fan", device == 0 ? 5 : 3, negotiated)); break;
                    }
                }
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }

        public void Dispose()
        {
            _listener.Stop();
            try { _task.Wait(500); } catch { }
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(2)]
    [InlineData(1)]
    public void The_client_reads_the_devices_a_server_describes(int serverVersion)
    {
        using var server = new Mock(serverVersion);
        using var client = new OpenRgbClient();
        Assert.Null(client.Connect(server.Port));
        Assert.Equal(serverVersion, client.Version);
        Assert.Equal(["Test Keyboard", "Test Fan"], client.Devices.Select(d => d.Name));
        Assert.Equal([5, 3], client.Devices.Select(d => d.Leds));
        Assert.Equal(serverVersion >= 1 ? "TestVendor" : "", client.Devices[0].Vendor);
    }

    [Fact]
    public void Colours_are_sent_as_one_update_packet_with_the_documented_layout()
    {
        using var server = new Mock();
        using var client = new OpenRgbClient();
        Assert.Null(client.Connect(server.Port));
        client.UseDirectMode(0);
        client.SetColors(0, [(1, 2, 3), (4, 5, 6)]);
        Thread.Sleep(300);
        (int device, int id, byte[] data) update, custom;
        lock (server.Received)
        {
            update = server.Received.Last(r => r.id == 1050);
            custom = server.Received.Last(r => r.id == 1100);
        }
        Assert.Equal(0, update.device);
        Assert.Empty(custom.data);
        Assert.Equal(4 + 2 + 8, update.data.Length);
        Assert.Equal((uint)update.data.Length, BitConverter.ToUInt32(update.data, 0));
        Assert.Equal(2, BitConverter.ToUInt16(update.data, 4));
        Assert.Equal(new byte[] { 1, 2, 3, 0, 4, 5, 6, 0 }, update.data[6..]);
    }

    [Fact]
    public void Nothing_listening_gives_a_plain_message_and_garbage_never_crashes()
    {
        using var client = new OpenRgbClient();
        Assert.NotNull(client.Connect(1, 300));
        Assert.Null(OpenRgbClientProbe.Parse(new byte[] { 1, 2, 3 }));
        Assert.Null(OpenRgbClientProbe.Parse(new byte[200]));
    }

    [Fact]
    public void The_client_only_ever_connects_to_this_pc()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Shabpa.slnx"))) dir = dir.Parent;
        var text = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Shabpa", "Core", "Rgb.cs"));
        Assert.Contains("IPAddress.Loopback", text);
        foreach (var banned in new[] { "HttpClient", "WebClient", "File.", "Process.Start", "Dns." }) Assert.DoesNotContain(banned, text);
    }

    // ---- the looks --------------------------------------------------------------------------------------

    private static RgbSettings Look(string effect, string palette = "shabpa") => new() { Effect = effect, Palette = palette, Speed = 0.5, Brightness = 1 };

    [Fact]
    public void Every_effect_gives_valid_colours_for_every_light_at_any_moment()
    {
        foreach (var effect in RgbEffects.EffectKeys)
        {
            var s = Look(effect);
            var ring = RgbEffects.PaletteOf(s);
            foreach (var t in new[] { 0.0, 0.3, 7.7, 1234.5 })
                for (var i = 0; i < 40; i++)
                    foreach (var load in new[] { 0.0, 0.5, 1.0, 5.0, -1.0 })
                        RgbEffects.Pixel(s, ring, t, i, 40, load, game: i % 2 == 0); // must not throw
        }
    }

    [Fact]
    public void Off_is_black_static_is_the_first_colour_and_brightness_scales()
    {
        var ring = RgbEffects.PaletteOf(Look("static", "police"));
        Assert.Equal(((byte)0, (byte)0, (byte)0), RgbEffects.Pixel(Look("off"), ring, 3, 0, 10, 0, false));
        Assert.Equal(((byte)255, (byte)0, (byte)0), RgbEffects.Pixel(Look("static", "police"), ring, 3, 0, 10, 0, false));
        var dim = Look("static", "police");
        dim.Brightness = 0.5;
        Assert.Equal((byte)127, RgbEffects.Pixel(dim, ring, 3, 0, 10, 0, false).r);
    }

    [Fact]
    public void The_load_effect_goes_from_green_to_red_and_a_wave_moves_with_time()
    {
        var s = Look("load");
        var ring = RgbEffects.PaletteOf(s);
        var idle = RgbEffects.Pixel(s, ring, 0, 0, 10, 0.0, false);
        var busy = RgbEffects.Pixel(s, ring, 0, 0, 10, 1.0, false);
        Assert.True(idle.g > idle.r);
        Assert.True(busy.r > busy.g);
        var wave = Look("wave", "cyber");
        var ring2 = RgbEffects.PaletteOf(wave);
        Assert.NotEqual(RgbEffects.Pixel(wave, ring2, 0, 3, 20, 0, false), RgbEffects.Pixel(wave, ring2, 5, 3, 20, 0, false));
    }

    [Fact]
    public void A_device_choice_overrides_the_general_look_and_ignored_devices_are_left_alone()
    {
        var device = new RgbDevice(0, "Fan", "V", 1, 4, 1);
        var s = Look("rainbow");
        Assert.Equal(4, RgbEngine.Frame(s, device, 1, 0, false).Count);
        s.Devices.Add(new RgbDeviceChoice { Name = "Fan", Mode = "off" });
        Assert.All(RgbEngine.Frame(s, device, 1, 0, false), c => Assert.Equal(((byte)0, (byte)0, (byte)0), c));
        s.Devices[0] = new RgbDeviceChoice { Name = "Fan", Mode = "color", Color = "FF8000" };
        Assert.All(RgbEngine.Frame(s, device, 1, 0, false), c => Assert.Equal(255, c.r));
        s.Ignored.Add("Fan");
        Assert.Empty(RgbEngine.Frame(s, device, 1, 0, false));
    }

    [Fact]
    public void Custom_colours_are_validated_and_capped()
    {
        var s = new RgbSettings { Palette = "custom", Colors = ["zzz", "FF0000", "00FF00", "0000FF", "111111", "222222", "333333", "444444"] };
        var ring = RgbEffects.PaletteOf(s);
        Assert.Equal(6, ring.Count); // the bad one is dropped, at most six are read
        Assert.Equal(((byte)255, (byte)255, (byte)255), RgbEffects.PaletteOf(new RgbSettings { Palette = "custom", Colors = [] })[0]); // nothing usable: white
    }
}

/// <summary>Reaches the parser the way the client does, for the garbage tests.</summary>
internal static class OpenRgbClientProbe
{
    public static RgbDevice? Parse(byte[] data) =>
        (RgbDevice?)typeof(OpenRgbClient).GetMethod("ParseDevice", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, [0, data, 3]);
}
