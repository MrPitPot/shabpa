using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Shabpa.Core;

/// <summary>One lit device found through OpenRGB (a keyboard, a fan hub, RAM, a motherboard...).</summary>
public sealed record RgbDevice(int Index, string Name, string Vendor, int Type, int Leds, int Zones);

/// <summary>What the person chose for the lighting. Everything stays on this PC.</summary>
public sealed class RgbSettings
{
    public bool Enabled { get; set; }
    /// <summary>static, breathing, rainbow, wave, cycle, fire, aurora, twinkle, load, game or off.</summary>
    public string Effect { get; set; } = "wave";
    /// <summary>Name of a ready-made palette ("custom" uses <see cref="Colors"/>).</summary>
    public string Palette { get; set; } = "shabpa";
    /// <summary>The person's own colours as 6-digit hex values (at most 6).</summary>
    public List<string> Colors { get; set; } = ["3EE6A0", "5B8CFF"];
    public double Speed { get; set; } = 0.5;
    public double Brightness { get; set; } = 0.8;
    /// <summary>Devices left alone (by name).</summary>
    public List<string> Ignored { get; set; } = [];
    /// <summary>Per-device choices that replace the general effect: "off" or a fixed colour.</summary>
    public List<RgbDeviceChoice> Devices { get; set; } = [];
}

public sealed class RgbDeviceChoice
{
    public string Name { get; set; } = "";
    /// <summary>"follow" (the general effect), "off" or "color".</summary>
    public string Mode { get; set; } = "follow";
    public string Color { get; set; } = "FFFFFF";
}

/// <summary>Talks to a running OpenRGB program (openrgb.org, open source) through its SDK server on this PC. OpenRGB knows
/// the lights of most keyboards, mice, fans, RAM and motherboards; Shabpa only tells it which colours to show. The
/// connection is to this computer only (127.0.0.1); nothing is sent over the internet.</summary>
public sealed class OpenRgbClient : IDisposable
{
    public const int Port = 6742;
    private const int MaxVersion = 3;

    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private readonly object _gate = new();

    public int Version { get; private set; }
    public List<RgbDevice> Devices { get; } = [];
    public bool Connected => _tcp is { Connected: true };

    private static byte[] Header(int device, int id, int size)
    {
        var h = new byte[16];
        "ORGB"u8.CopyTo(h);
        BitConverter.GetBytes((uint)device).CopyTo(h, 4);
        BitConverter.GetBytes((uint)id).CopyTo(h, 8);
        BitConverter.GetBytes((uint)size).CopyTo(h, 12);
        return h;
    }

    private void Write(int device, int id, byte[] data)
    {
        lock (_gate)
        {
            _stream!.Write(Header(device, id, data.Length));
            if (data.Length > 0) _stream.Write(data);
        }
    }

    private byte[] ReadExact(int count)
    {
        var buffer = new byte[count];
        var got = 0;
        while (got < count)
        {
            var n = _stream!.Read(buffer, got, count - got);
            if (n <= 0) throw new IOException("connection closed");
            got += n;
        }
        return buffer;
    }

    private byte[] ReadPacket(int expectedId)
    {
        // other packets (for instance "device list updated") may arrive in between; skip them
        for (var guard = 0; guard < 8; guard++)
        {
            var h = ReadExact(16);
            if (h[0] != (byte)'O' || h[1] != (byte)'R' || h[2] != (byte)'G' || h[3] != (byte)'B') throw new IOException("not an OpenRGB server");
            var id = BitConverter.ToInt32(h, 8);
            var size = BitConverter.ToInt32(h, 12);
            if (size is < 0 or > 4_000_000) throw new IOException("packet too large");
            var data = ReadExact(size);
            if (id == expectedId) return data;
        }
        throw new IOException("no answer");
    }

    /// <summary>Connects to the OpenRGB SDK server on this PC. Returns an error text, or null on success.</summary>
    public string? Connect(int port = Port, int timeoutMs = 1500)
    {
        Dispose();
        try
        {
            _tcp = new TcpClient { NoDelay = true, ReceiveTimeout = timeoutMs, SendTimeout = timeoutMs };
            if (!_tcp.ConnectAsync(IPAddress.Loopback, port).Wait(timeoutMs)) throw new TimeoutException();
            _stream = _tcp.GetStream();

            Write(0, 50, "Shabpa\0"u8.ToArray());
            Write(0, 40, BitConverter.GetBytes((uint)MaxVersion));
            Version = 0;
            try
            {
                var reply = ReadPacket(40);
                Version = (int)Math.Min(BitConverter.ToUInt32(reply, 0), MaxVersion);
            }
            catch (IOException) { Version = 0; }

            Write(0, 0, []);
            var count = (int)BitConverter.ToUInt32(ReadPacket(0), 0);
            Devices.Clear();
            for (var i = 0; i < Math.Min(count, 64); i++)
            {
                Write(i, 1, Version >= 1 ? BitConverter.GetBytes((uint)Version) : []);
                var device = ParseDevice(i, ReadPacket(1), Version);
                if (device is not null) Devices.Add(device);
            }
            return null;
        }
        catch (Exception ex) when (ex is SocketException or IOException or TimeoutException or AggregateException or ObjectDisposedException or InvalidDataException)
        {
            Dispose();
            return L.T("OpenRGB پیدا نشد. برنامه باز و سرور SDK روشن باشه.", "OpenRGB was not found. Open it and start its SDK server.");
        }
    }

    /// <summary>Reads one controller's description (protocol 0 to 3).</summary>
    internal static RgbDevice? ParseDevice(int index, byte[] data, int version)
    {
        try
        {
            var r = new Reader(data);
            r.U32(); // data size
            var type = r.I32();
            var name = r.Text();
            var vendor = version >= 1 ? r.Text() : "";
            r.Text(); // description
            r.Text(); // version
            r.Text(); // serial
            r.Text(); // location
            var modes = r.U16();
            r.I32(); // active mode
            for (var m = 0; m < modes; m++)
            {
                r.Text(); r.I32(); r.U32(); r.U32(); r.U32();
                if (version >= 3) { r.U32(); r.U32(); }
                r.U32(); r.U32(); r.U32();
                if (version >= 3) r.U32();
                r.U32(); r.U32();
                var colors = r.U16();
                r.Skip(4 * colors);
            }
            var zones = r.U16();
            for (var z = 0; z < zones; z++)
            {
                r.Text(); r.I32(); r.U32(); r.U32(); r.U32();
                var matrix = r.U16();
                r.Skip(matrix);
            }
            var leds = r.U16();
            return name.Length == 0 ? null : new RgbDevice(index, name, vendor, type, leds, zones);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>Puts a device into the mode that accepts colours from a program (OpenRGB's "custom"/direct mode).</summary>
    public void UseDirectMode(int device) => Write(device, 1100, []);

    /// <summary>Shows one colour per light on a device.</summary>
    public void SetColors(int device, IReadOnlyList<(byte r, byte g, byte b)> colors)
    {
        var data = new byte[4 + 2 + 4 * colors.Count];
        BitConverter.GetBytes((uint)data.Length).CopyTo(data, 0);
        BitConverter.GetBytes((ushort)colors.Count).CopyTo(data, 4);
        for (var i = 0; i < colors.Count; i++)
        {
            data[6 + i * 4] = colors[i].r;
            data[7 + i * 4] = colors[i].g;
            data[8 + i * 4] = colors[i].b;
        }
        Write(device, 1050, data);
    }

    public void Dispose()
    {
        try { _stream?.Dispose(); } catch { /* closing */ }
        try { _tcp?.Dispose(); } catch { /* closing */ }
        _stream = null;
        _tcp = null;
    }

    private sealed class Reader(byte[] data)
    {
        private int _at;

        private void Need(int n)
        {
            if (n < 0 || _at + n > data.Length) throw new InvalidDataException("short packet");
        }

        public void Skip(int n) { Need(n); _at += n; }
        public uint U32() { Need(4); var v = BitConverter.ToUInt32(data, _at); _at += 4; return v; }
        public int I32() { Need(4); var v = BitConverter.ToInt32(data, _at); _at += 4; return v; }
        public ushort U16() { Need(2); var v = BitConverter.ToUInt16(data, _at); _at += 2; return v; }

        public string Text()
        {
            var n = U16();
            Need(n);
            var s = Encoding.UTF8.GetString(data, _at, n).TrimEnd('\0');
            _at += n;
            return s;
        }
    }
}

/// <summary>The looks: pure functions from time and position to a colour, so they can be tested without any hardware.</summary>
public static class RgbEffects
{
    public static readonly string[] EffectKeys = ["static", "breathing", "rainbow", "wave", "cycle", "fire", "aurora", "twinkle", "load", "game", "off"];

    public static (string key, string fa, string en)[] EffectNames =>
    [
        ("static", "رنگ ثابت", "Static colour"), ("breathing", "تنفس", "Breathing"), ("rainbow", "رنگین‌کمان", "Rainbow"), ("wave", "موج رنگی", "Colour wave"),
        ("cycle", "چرخش رنگ", "Colour cycle"), ("fire", "آتش", "Fire"), ("aurora", "شفق", "Aurora"), ("twinkle", "چشمک ستاره", "Twinkle"),
        ("load", "بار سیستم (سبز تا قرمز)", "System load (green to red)"), ("game", "حالت بازی", "Game mode"), ("off", "خاموش", "Off"),
    ];

    /// <summary>Ready-made colour sets. "shabpa" follows the app's own look.</summary>
    public static readonly (string key, string fa, string en, string[] colors)[] Palettes =
    [
        ("shabpa", "شب‌پا", "Shabpa", ["3EE6A0", "5B8CFF"]),
        ("ocean", "اقیانوس", "Ocean", ["00C6FF", "0072FF", "00E0C6"]),
        ("sunset", "غروب", "Sunset", ["FF512F", "F09819", "DD2476"]),
        ("cyber", "سایبر", "Cyberpunk", ["FF00E5", "00F0FF", "7B2FFF"]),
        ("forest", "جنگل", "Forest", ["1B8A3A", "7BD35A", "0F5A2E"]),
        ("ice", "یخ", "Ice", ["BFEFFF", "6AB7FF", "FFFFFF"]),
        ("lava", "گدازه", "Lava", ["FF2A00", "FF8A00", "FFD000"]),
        ("matrix", "ماتریکس", "Matrix", ["00FF41", "008F11", "003B00"]),
        ("candy", "آبنبات", "Candy", ["FF7EB6", "FFD1E8", "A890FE"]),
        ("police", "پلیس", "Police", ["FF0000", "0000FF"]),
        ("mono", "سفید", "White", ["FFFFFF", "BBBBBB"]),
        ("custom", "رنگ‌های من", "My colours", []),
    ];

    public static (byte r, byte g, byte b) Hex(string text)
    {
        var t = text.Trim().TrimStart('#');
        if (t.Length == 6 && int.TryParse(t, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var v))
            return ((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return (255, 255, 255);
    }

    public static bool IsHex(string text) => text.Trim().TrimStart('#') is { Length: 6 } t && t.All(Uri.IsHexDigit);

    public static List<(byte r, byte g, byte b)> PaletteOf(RgbSettings s)
    {
        IEnumerable<string> colors = s.Palette == "custom" ? s.Colors : Palettes.FirstOrDefault(p => p.key == s.Palette).colors ?? [];
        var list = colors.Where(IsHex).Take(6).Select(Hex).ToList();
        return list.Count > 0 ? list : [(255, 255, 255)];
    }

    /// <summary>A colour along a ring of colours, 0..1.</summary>
    public static (double r, double g, double b) Along(IReadOnlyList<(byte r, byte g, byte b)> ring, double position)
    {
        if (ring.Count == 1) return (ring[0].r, ring[0].g, ring[0].b);
        position -= Math.Floor(position);
        var scaled = position * ring.Count;
        var i = (int)scaled % ring.Count;
        var f = scaled - Math.Floor(scaled);
        var a = ring[i];
        var b = ring[(i + 1) % ring.Count];
        return (a.r + (b.r - a.r) * f, a.g + (b.g - a.g) * f, a.b + (b.b - a.b) * f);
    }

    public static (double r, double g, double b) Hsv(double hue, double saturation = 1, double value = 1)
    {
        hue -= Math.Floor(hue);
        var h = hue * 6;
        var i = (int)h;
        var f = h - i;
        var p = value * (1 - saturation);
        var q = value * (1 - saturation * f);
        var t = value * (1 - saturation * (1 - f));
        return (i % 6) switch
        {
            0 => (value * 255, t * 255, p * 255), 1 => (q * 255, value * 255, p * 255), 2 => (p * 255, value * 255, t * 255),
            3 => (p * 255, q * 255, value * 255), 4 => (t * 255, p * 255, value * 255), _ => (value * 255, p * 255, q * 255),
        };
    }

    /// <summary>The colour of one light.</summary>
    /// <param name="seconds">Time since the effect started.</param>
    /// <param name="index">This light's number and how many lights the device has.</param>
    /// <param name="load">System load 0..1 and whether a game is running, for the effects that follow the PC.</param>
    public static (byte r, byte g, byte b) Pixel(RgbSettings s, IReadOnlyList<(byte r, byte g, byte b)> ring, double seconds, int index, int count, double load, bool game)
    {
        var speed = 0.15 + 1.6 * Math.Clamp(s.Speed, 0, 1);
        var spatial = count > 1 ? (double)index / count : 0;
        (double r, double g, double b) c;
        switch (s.Effect)
        {
            case "off": return (0, 0, 0);
            case "static":
                c = Along(ring, 0);
                break;
            case "breathing":
            {
                var pulse = 0.5 - 0.5 * Math.Cos(seconds * speed * 2);
                var b = Along(ring, 0);
                c = (b.r * pulse, b.g * pulse, b.b * pulse);
                break;
            }
            case "rainbow":
                c = Hsv(spatial + seconds * speed * 0.15);
                break;
            case "wave":
                c = Along(ring, spatial - seconds * speed * 0.15);
                break;
            case "cycle":
                c = Along(ring, seconds * speed * 0.1);
                break;
            case "fire":
            {
                // flicker: a slow wave and a fast one, mapped from deep red to yellow
                var heat = Math.Clamp(0.55 + 0.25 * Math.Sin(seconds * speed * 3 + index * 1.7) + 0.2 * Math.Sin(seconds * speed * 7.3 + index * 0.9), 0, 1);
                c = (255 * Math.Min(1, heat * 1.6), 255 * Math.Max(0, heat * 1.3 - 0.35) * 0.7, 255 * Math.Max(0, heat - 0.85) * 0.5);
                break;
            }
            case "aurora":
            {
                var sway = 0.5 + 0.5 * Math.Sin(seconds * speed * 0.8 + spatial * 5);
                var b = Along(ring, 0.15 * Math.Sin(seconds * speed * 0.4 + spatial * 3) + sway * 0.4);
                var glow = 0.35 + 0.65 * sway;
                c = (b.r * glow, b.g * glow, b.b * glow);
                break;
            }
            case "twinkle":
            {
                var seed = Math.Sin(index * 12.9898 + Math.Floor(seconds * speed * 2) * 78.233) * 43758.5453;
                var dice = seed - Math.Floor(seed);
                var blink = dice > 0.82 ? 1.0 : 0.12;
                var b = Along(ring, spatial);
                c = (b.r * blink, b.g * blink, b.b * blink);
                break;
            }
            case "load":
                // green when idle, through yellow, to red under full load
                c = Hsv((1 - Math.Clamp(load, 0, 1)) * 0.33);
                break;
            case "game":
                // calm palette on the desktop, a strong pulse while a game runs
                if (game)
                {
                    var pulse = 0.6 + 0.4 * Math.Sin(seconds * speed * 4);
                    var b = Along(ring, 0.5);
                    c = (b.r * pulse, b.g * pulse, b.b * pulse);
                }
                else c = Along(ring, spatial * 0.5 + seconds * speed * 0.03);
                break;
            default:
                c = Along(ring, 0);
                break;
        }
        var level = Math.Clamp(s.Brightness, 0, 1);
        return ((byte)Math.Clamp(c.r * level, 0, 255), (byte)Math.Clamp(c.g * level, 0, 255), (byte)Math.Clamp(c.b * level, 0, 255));
    }
}

/// <summary>Keeps the lights in step with the chosen look: connects to OpenRGB, repaints about 25 times a second and
/// reconnects by itself when OpenRGB is started later.</summary>
public sealed class RgbEngine : IDisposable
{
    private readonly Func<RgbSettings> _settings;
    private readonly Func<(double load, bool game)> _pc;
    private Thread? _thread;
    private volatile bool _stop;
    private OpenRgbClient? _client;

    public string Status { get; private set; } = "";
    public IReadOnlyList<RgbDevice> Devices => _client?.Devices.ToList() ?? [];
    public bool Connected => _client is { Connected: true };

    public RgbEngine(Func<RgbSettings> settings, Func<(double load, bool game)> pc)
    {
        _settings = settings;
        _pc = pc;
    }

    public bool Running => _thread is { IsAlive: true };

    public void Start()
    {
        if (Running) return;
        _stop = false;
        _thread = new Thread(Loop) { IsBackground = true, Name = "rgb", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    public void Stop(bool lightsOff = false)
    {
        _stop = true;
        _thread?.Join(2000);
        _thread = null;
        if (lightsOff && _client is { Connected: true } c)
        {
            try { foreach (var d in c.Devices) c.SetColors(d.Index, Enumerable.Repeat(((byte)0, (byte)0, (byte)0), d.Leds).ToList()); }
            catch { /* the connection is gone */ }
        }
        _client?.Dispose();
        _client = null;
        Status = "";
    }

    public void Dispose() => Stop();

    /// <summary>The colours of every light of a device right now; the single place that decides what a device shows.</summary>
    internal static List<(byte r, byte g, byte b)> Frame(RgbSettings s, RgbDevice device, double seconds, double load, bool game)
    {
        var choice = s.Devices.FirstOrDefault(d => d.Name == device.Name);
        var count = Math.Max(1, device.Leds);
        if (s.Ignored.Contains(device.Name)) return [];
        if (choice is { Mode: "off" }) return [.. Enumerable.Repeat(((byte)0, (byte)0, (byte)0), count)];
        if (choice is { Mode: "color" })
        {
            var c = RgbEffects.Hex(choice.Color);
            var k = Math.Clamp(s.Brightness, 0, 1);
            var fixedColor = ((byte)(c.r * k), (byte)(c.g * k), (byte)(c.b * k));
            return [.. Enumerable.Repeat(fixedColor, count)];
        }
        var ring = RgbEffects.PaletteOf(s);
        var frame = new List<(byte, byte, byte)>(count);
        for (var i = 0; i < count; i++) frame.Add(RgbEffects.Pixel(s, ring, seconds, i, count, load, game));
        return frame;
    }

    private void Loop()
    {
        var started = DateTime.UtcNow;
        var lastTry = DateTime.MinValue;
        var prepared = new HashSet<int>();
        while (!_stop)
        {
            var s = _settings();
            try
            {
                if (_client is not { Connected: true })
                {
                    if (DateTime.UtcNow - lastTry < TimeSpan.FromSeconds(8)) { Thread.Sleep(250); continue; }
                    lastTry = DateTime.UtcNow;
                    _client?.Dispose();
                    _client = new OpenRgbClient();
                    var error = _client.Connect();
                    prepared.Clear();
                    if (error is not null) { Status = error; _client.Dispose(); _client = null; continue; }
                    Status = L.T($"وصل شد؛ {Fa.Digits(_client.Devices.Count.ToString())} دستگاه", $"Connected; {_client.Devices.Count} devices");
                }
                var seconds = (DateTime.UtcNow - started).TotalSeconds;
                var (load, game) = _pc();
                foreach (var device in _client.Devices)
                {
                    var frame = Frame(s, device, seconds, load, game);
                    if (frame.Count == 0) continue;
                    if (prepared.Add(device.Index)) _client.UseDirectMode(device.Index);
                    _client.SetColors(device.Index, frame);
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or InvalidOperationException)
            {
                Status = L.T("ارتباط با OpenRGB قطع شد؛ دوباره تلاش می‌کنم.", "Lost OpenRGB; trying again.");
                _client?.Dispose();
                _client = null;
            }
            Thread.Sleep(40);
        }
    }
}
