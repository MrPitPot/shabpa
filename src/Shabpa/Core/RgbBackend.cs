using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace Shabpa.Core;

/// <summary>One thing with lights that a lighting server knows about, and the Shabpa zone its lights are driven as.</summary>
public sealed record RgbDevice(int Index, string Name, string Vendor, int Type, int Leds, string Zone)
{
    /// <summary>What the maker says it is (often the model), where it is plugged in, and its serial: shown in the devices window.</summary>
    public string Description { get; init; } = "";
    public string Location { get; init; } = "";
    public string Serial { get; init; } = "";
    /// <summary>The groups of LEDs inside the device (an addressable header, a fan ring, the memory strip...).</summary>
    public IReadOnlyList<RgbZoneInfo> Zones { get; init; } = [];
    /// <summary>A name that stays the same between runs (type, name and, for identical devices such as two memory sticks, their order), used to remember the person's settings for it.</summary>
    public string Key { get; init; } = "";
}

/// <summary>One group of LEDs of a device. A header whose <see cref="Min"/> is below its <see cref="Max"/> can be given a size (how many LEDs are really plugged in).</summary>
public sealed record RgbZoneInfo(int Index, string Name, int Type, int Min, int Max, int Count)
{
    public bool Resizable => Max > Min;
}

/// <summary>Talks to a running OpenRGB program (its "SDK Server", TCP port 6742 on this PC) so Shabpa can light the fans, memory, strips,
/// motherboard and graphics card that program supports. Shabpa itself ships no vendor code and no driver: if OpenRGB is not running, nothing
/// is sent and the page says so. Only localhost is ever used unless the person typed another address.</summary>
public sealed class OpenRgbClient : IDisposable
{
    internal const uint MagicRequestControllerCount = 0, RequestControllerData = 1, RequestProtocolVersion = 40, SetClientName = 50, ResizeZoneId = 1000, UpdateLeds = 1050, SetCustomMode = 1100;
    private const uint ClientProtocol = 3;

    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private uint _protocol = 1;
    private string _host = "127.0.0.1";
    private int _port = 6742;

    public IReadOnlyList<RgbDevice> Devices { get; private set; } = [];
    public bool Connected => _tcp is { Connected: true } && _stream is not null;
    public string LastError { get; private set; } = "";

    /// <summary>Opens the connection and reads the device list. False (with <see cref="LastError"/>) when nothing answers.</summary>
    public bool Connect(string host, int port, int timeoutMs = 700)
    {
        Close();
        _host = host;
        _port = port;
        TcpClient? tcp = null;
        try
        {
            tcp = new TcpClient { NoDelay = true, ReceiveTimeout = 2000, SendTimeout = 2000 };
            var connect = tcp.ConnectAsync(host, port);
            if (!connect.Wait(timeoutMs) || !tcp.Connected)
            {
                OpenRgbHost.Observe(connect);              // disposing a connect that is still going makes it fail later; nobody should be left holding that failure
                tcp.Dispose();
                LastError = "no answer";
                return false;
            }
            _tcp = tcp;
            _stream = tcp.GetStream();
            Send(0, SetClientName, Encoding.ASCII.GetBytes("Shabpa\0"));
            Send(0, RequestProtocolVersion, BitConverter.GetBytes(ClientProtocol));
            var version = Receive(RequestProtocolVersion);
            _protocol = Math.Min(ClientProtocol, version.Length >= 4 ? BitConverter.ToUInt32(version, 0) : 0);
            ReadDevices();
            LastError = "";
            return true;
        }
        catch (Exception ex) when (ex is SocketException or IOException or AggregateException or ObjectDisposedException or InvalidOperationException or InvalidDataException)
        {
            LastError = ex is AggregateException a ? a.InnerException?.Message ?? ex.Message : ex.Message;
            Close();
            if (tcp is not null && !ReferenceEquals(_tcp, tcp)) tcp.Dispose();           // a refused connect left its socket behind (every 4 s while no server runs)
            return false;
        }
    }

    private int _serverCount = -1;

    /// <summary>Asks how many devices the server has and reads every one of them (they are only looked at: nothing is changed on them yet).</summary>
    private void ReadDevices()
    {
        Send(0, MagicRequestControllerCount, []);
        var count = BitConverter.ToUInt32(Receive(MagicRequestControllerCount), 0);
        var list = new List<RgbDevice>();
        for (var i = 0; i < Math.Min(count, 64u); i++)
        {
            Send((uint)i, RequestControllerData, _protocol >= 1 ? BitConverter.GetBytes(_protocol) : []);
            if (ParseController(i, Receive(RequestControllerData), _protocol) is { } device) list.Add(device);
        }
        Devices = WithKeys(list);
        _serverCount = (int)count;
        StopLanes();                                             // device numbers may have moved: every lane starts again
        _controlledSet.Clear();
    }

    /// <summary>OpenRGB opens its port before it has finished looking for hardware, so a connection made early sees no device, or only some (memory and the motherboard come last).
    /// This asks again and reads the list anew when the number of devices changed. True when the list changed.</summary>
    public bool Reload()
    {
        if (!Connected) return false;
        try
        {
            Send(0, MagicRequestControllerCount, []);
            var count = (int)BitConverter.ToUInt32(Receive(MagicRequestControllerCount), 0);
            if (count == _serverCount) return false;
            ReadDevices();
            return true;
        }
        catch (Exception ex) when (ex is SocketException or IOException or AggregateException or ObjectDisposedException or InvalidOperationException or InvalidDataException)
        {
            LastError = ex is AggregateException a ? a.InnerException?.Message ?? ex.Message : ex.Message;
            Close();
            return false;
        }
    }

    private static List<RgbDevice> WithKeys(List<RgbDevice> list)
    {
        var seen = new Dictionary<string, int>();
        for (var i = 0; i < list.Count; i++)
        {
            var baseKey = $"{list[i].Type}|{list[i].Name}";
            var n = seen.GetValueOrDefault(baseKey);
            seen[baseKey] = n + 1;
            list[i] = list[i] with { Key = n == 0 ? baseKey : $"{baseKey}#{n}" };
        }
        return list;
    }

    private readonly Dictionary<int, RgbLane> _lanes = [];

    /// <summary>How long (ms) the server takes to put one frame on this device, measured on every frame sent. A big motherboard header or memory over a slow bus takes tens of
    /// milliseconds; it is also how far this device runs behind, which the caller makes up for by showing it the effect that far ahead.</summary>
    public double CostMs(int deviceIndex) => _lanes.TryGetValue(deviceIndex, out var lane) ? lane.CostMs : 0;
    private readonly HashSet<int> _controlledSet = [];

    /// <summary>Every device has a lane of its own (its own connection and thread), made when the first frame for it is offered: a device the person left out (no frame for it) is never
    /// touched and keeps its own effect.</summary>
    private RgbLane LaneFor(RgbDevice device)
    {
        if (!_lanes.TryGetValue(device.Index, out var lane)) _lanes[device.Index] = lane = new RgbLane(_host, _port, device.Index);
        return lane;
    }

    private void StopLanes()
    {
        var lanes = _lanes.Values.ToList();
        _lanes.Clear();
        foreach (var lane in lanes) lane.Dispose();
    }

    /// <summary>Devices that talk over a slow bus (memory and many motherboards, over SMBus) get fewer frames; everything else gets up to 40 a second.</summary>
    private static int MinIntervalMs(RgbDevice device, bool smooth) => device.Type is 0 or 1 ? (smooth ? 100 : 130) : (smooth ? 20 : 40);

    /// <summary>More frames a second for devices that can take them (the page's «smoother» switch).</summary>
    public bool Smooth { get; set; } = true;
    /// <summary>Colours are corrected for LEDs (see <see cref="Rgb.ForLight"/>) before they are sent.</summary>
    public bool RichColours { get; set; }

    /// <summary>Sets every LED of the devices. <paramref name="frameFor"/> gives a device's frame (or null to leave it alone); it is stretched to the device's LED count and handed to
    /// that device's lane, which sends it as soon as the device can take it (all devices at once, not one after the other). A device only gets a frame when its colours changed
    /// (or every two seconds, so none falls back to its own program), and a slow one only every 100 ms. <paramref name="wait"/> holds the call until every lane has
    /// finished with what it was given (the light loop does not wait: it must never be slowed by a slow device); false when the server is gone.</summary>
    public bool Update(Func<RgbDevice, Rgb[]?> frameFor, bool force = false, bool wait = true) => UpdateAt((device, _) => frameFor(device), force, wait);

    /// <summary>Like <see cref="Update"/>, but the frame is drawn by the lane at the moment it really goes out: <paramref name="frameAt"/> gets the device and how many seconds late
    /// that is compared with now, and shows the effect that much further on. Every device is then shown the same moment of the effect, however long it had to wait its turn.</summary>
    public bool UpdateAt(Func<RgbDevice, double, Rgb[]?> frameAt, bool force = false, bool wait = true)
    {
        if (!Connected) return false;
        try
        {
            var offered = new List<RgbLane>();
            foreach (var device in Devices)
            {
                if (frameAt(device, 0) is not { Length: > 0 }) continue;                       // left out: never touched
                var lane = LaneFor(device);
                _controlledSet.Add(device.Index);
                var d = device;
                var rich = RichColours;
                lane.Offer(late =>
                {
                    if (frameAt(d, late) is not { Length: > 0 } frame) return null;
                    var leds = RgbEngine.Resample(frame, d.Leds);
                    if (rich) leds = [.. leds.Select(c => c.ForLight())];
                    var colours = new byte[leds.Length * 3];
                    for (var i = 0; i < leds.Length; i++) { colours[i * 3] = leds[i].R; colours[i * 3 + 1] = leds[i].G; colours[i * 3 + 2] = leds[i].B; }
                    return colours;
                }, force, MinIntervalMs(device, Smooth));
                offered.Add(lane);
            }
            if (wait)
            {
                var until = Environment.TickCount64 + 4000;
                while (offered.Any(l => !l.Idle && !l.Broken) && Environment.TickCount64 < until) Thread.Sleep(1);
            }
            if (_lanes.Values.Any(l => l.Broken))
            {
                LastError = "the lighting server stopped answering";
                Close();
                return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            LastError = ex.Message;
            Close();
            return false;
        }
    }

    /// <summary>Waits (at most <paramref name="timeoutMs"/>) until every lane has put what it was given on its device.</summary>
    public void Flush(int timeoutMs = 1000)
    {
        var until = Environment.TickCount64 + timeoutMs;
        while (_lanes.Values.Any(l => !l.Idle && !l.Broken) && Environment.TickCount64 < until) Thread.Sleep(1);
    }

    /// <summary>Sets every header whose size can be set to the size <paramref name="target"/> gives for it (null leaves it). Returns how many were changed.</summary>
    public int ApplySizes(Func<RgbDevice, RgbZoneInfo, int?> target)
    {
        var changed = 0;
        for (var guard = 0; guard < 64; guard++)
        {
            var next = Devices.SelectMany(d => d.Zones.Where(z => z.Resizable).Select(z => (d, z, want: target(d, z)))).FirstOrDefault(x => x.want is { } w && w != x.z.Count && w >= x.z.Min && w <= x.z.Max);
            if (next.d is null || next.want is not { } size) break;
            if (!ResizeZone(next.d.Index, next.z.Index, size)) break;
            changed++;
        }
        return changed;
    }

    /// <summary>Gives a header zone a size (OpenRGB "resize zone") and reads the device again. False when the device refuses or the link is down.</summary>
    public bool ResizeZone(int deviceIndex, int zoneIndex, int count)
    {
        if (!Connected) return false;
        try
        {
            var payload = new byte[8];
            BitConverter.GetBytes(zoneIndex).CopyTo(payload, 0);
            BitConverter.GetBytes(count).CopyTo(payload, 4);
            Send((uint)deviceIndex, ResizeZoneId, payload);
            Send((uint)deviceIndex, RequestControllerData, BitConverter.GetBytes(_protocol));
            if (ParseController(deviceIndex, Receive(RequestControllerData), _protocol) is not { } fresh) return false;
            var list = Devices.ToList();
            var at = list.FindIndex(d => d.Index == deviceIndex);
            if (at < 0) return false;
            list[at] = fresh;
            Devices = WithKeys(list);
            if (_controlledSet.Contains(deviceIndex)) Send((uint)deviceIndex, SetCustomMode, []);
            return true;
        }
        catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException or InvalidOperationException or InvalidDataException)
        {
            LastError = ex.Message;
            Close();
            return false;
        }
    }

    private void Send(uint device, uint id, byte[] payload) => WritePacket(_stream!, device, id, payload);

    internal static void WritePacket(Stream stream, uint device, uint id, byte[] payload)
    {
        var packet = new byte[16 + payload.Length];
        "ORGB"u8.CopyTo(packet);
        BitConverter.GetBytes(device).CopyTo(packet, 4);
        BitConverter.GetBytes(id).CopyTo(packet, 8);
        BitConverter.GetBytes((uint)payload.Length).CopyTo(packet, 12);
        payload.CopyTo(packet, 16);
        stream.Write(packet, 0, packet.Length);
    }

    private byte[] Receive(uint expectedId) => ReadAnswer(_stream!, expectedId);

    internal static byte[] ReadAnswer(Stream stream, uint expectedId)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var header = ReadExactly(stream, 16);
            if (header[0] != 'O' || header[1] != 'R' || header[2] != 'G' || header[3] != 'B') throw new InvalidDataException("not an OpenRGB answer");
            var id = BitConverter.ToUInt32(header, 8);
            var size = BitConverter.ToUInt32(header, 12);
            if (size > 4 * 1024 * 1024) throw new InvalidDataException("answer too big");
            var body = ReadExactly(stream, (int)size);
            if (id == expectedId) return body;                   // anything else (a "device list changed" note) is skipped
        }
        throw new InvalidDataException("no answer to the request");
    }

    private static byte[] ReadExactly(Stream stream, int count)
    {
        var buffer = new byte[count];
        var read = 0;
        while (read < count)
        {
            var n = stream.Read(buffer, read, count - read);
            if (n <= 0) throw new IOException("connection closed");
            read += n;
        }
        return buffer;
    }

    // ---- controller data ------------------------------------------------------------------------------------------

    private sealed class Reader(byte[] data)
    {
        public int Pos;
        public uint U32() { var v = BitConverter.ToUInt32(data, Pos); Pos += 4; return v; }
        public ushort U16() { var v = BitConverter.ToUInt16(data, Pos); Pos += 2; return v; }
        public string Str()
        {
            var n = U16();
            var s = n <= 1 ? "" : Encoding.UTF8.GetString(data, Pos, n - 1);
            Pos += n;
            return s;
        }
        public void Skip(int n) => Pos += n;
        public bool AtEnd => Pos == data.Length;
    }

    /// <summary>Reads one controller description: its type, name, vendor and the number of LEDs. The LED count comes from walking the whole record
    /// (modes, zones, LEDs); when that walk does not end exactly at the end of the record (a newer server) the count is taken from the colour
    /// list that always closes the record, which is the same number.</summary>
    internal static RgbDevice? ParseController(int index, byte[] data, uint protocol)
    {
        try
        {
            var r = new Reader(data);
            r.U32();                                              // size of the record
            var type = (int)r.U32();
            var name = r.Str();
            var vendor = protocol >= 1 ? r.Str() : "";
            var afterIdentity = r.Pos;
            var walk = Walk(data, afterIdentity, protocol);
            var leds = walk?.Leds ?? TailLeds(data);
            if (leds <= 0) return null;
            return new RgbDevice(index, name, vendor, type, leds, ZoneFor(type, name))
            {
                Description = walk?.Description ?? "", Serial = walk?.Serial ?? "", Location = walk?.Location ?? "", Zones = walk?.Zones ?? [],
            };
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException)
        {
            return null;
        }
    }

    private sealed record Walked(int Leds, string Description, string Serial, string Location, List<RgbZoneInfo> Zones);

    private static Walked? Walk(byte[] data, int start, uint protocol)
    {
        try
        {
            var r = new Reader(data) { Pos = start };
            var description = r.Str(); r.Str(); var serial = r.Str(); var location = r.Str();   // description, version, serial, location
            var modes = r.U16();
            r.U32();                                              // the active mode
            for (var m = 0; m < modes; m++)
            {
                r.Str();
                r.Skip(4 + 4 + 4 + 4);                            // value, flags, speed min, speed max
                if (protocol >= 3) r.Skip(8);                     // brightness min, max
                r.Skip(4 + 4 + 4);                                // colours min, max, speed
                if (protocol >= 3) r.Skip(4);                     // brightness
                r.Skip(4 + 4);                                    // direction, colour mode
                var colours = r.U16();
                r.Skip(colours * 4);
            }
            var zones = r.U16();
            var zoneList = new List<RgbZoneInfo>();
            for (var z = 0; z < zones; z++)
            {
                var zoneName = r.Str();
                var zoneType = (int)r.U32();
                var min = (int)r.U32();
                var max = (int)r.U32();
                var count = (int)r.U32();
                zoneList.Add(new RgbZoneInfo(z, zoneName, zoneType, min, max, count));
                var matrix = r.U16();
                r.Skip(matrix);
            }
            var leds = r.U16();
            for (var l = 0; l < leds; l++) { r.Str(); r.Skip(4); }
            var colourCount = r.U16();
            r.Skip(colourCount * 4);
            return r.AtEnd && colourCount == leds ? new Walked(leds, description, serial, location, zoneList) : null;
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException)
        {
            return null;
        }
    }

    private static int TailLeds(byte[] data)
    {
        for (var n = 1; n <= 2048; n++)
        {
            var at = data.Length - 2 - n * 4;
            if (at < 0) break;
            if (BitConverter.ToUInt16(data, at) == n) return n;
        }
        return 0;
    }

    /// <summary>OpenRGB device types: 0 motherboard, 1 memory, 2 graphics card, 3 cooler, 4 LED strip, 5 keyboard, 6 mouse, 7 mouse mat, 8 headset,
    /// 9 headset stand, 10 gamepad, 11 light, 12 speaker, 13 virtual, 14 storage, 15 case, 16 microphone, 17 accessory, 18 keypad, 19 laptop.</summary>
    internal static string ZoneFor(int type, string name)
    {
        var n = name.ToLowerInvariant();
        return type switch
        {
            0 => "board",
            1 => "ram",
            2 => "gpu",
            3 => n.Contains("pump") || n.Contains("aio") || n.Contains("kraken") || n.Contains("liquid") || n.Contains("cpu") ? "cooler" : "fans",
            4 or 11 => "strip",
            5 or 18 or 19 => "keyboard",
            6 or 7 or 8 or 9 or 10 or 12 or 16 or 17 => "mouse",
            14 => "board",
            15 => "fans",
            _ => "strip",
        };
    }

    public void Close()
    {
        StopLanes();
        try { _stream?.Dispose(); } catch (IOException) { }
        try { _tcp?.Dispose(); } catch (SocketException) { }
        _stream = null;
        _tcp = null;
        _controlledSet.Clear();
    }

    public void Dispose() => Close();
}

/// <summary>A program (or Windows service) that also drives the lights of this PC and will fight Shabpa for them.</summary>
public sealed record RgbRival(string Name, string[] Processes, string[] Services);

public static class RgbRivals
{
    public static readonly IReadOnlyList<RgbRival> Known =
    [
        new("MSI Center / Mystic Light", ["MSI Center", "MSI.CentralServer", "MysticLight", "Mystic_Light", "MSI_Center", "DragonCenter", "Dragon Center", "MSI Dragon Center"], ["Mystic_Light_Service", "MSI_Central_Service", "MSI_Case_Service", "LightKeeperService"]),
        new("ASUS Armoury Crate / Aura", ["ArmouryCrate.UserSessionHelper", "ArmouryCrate", "LightingService", "AuraService", "AuraWallpaperService", "ArmourySocketServer"], ["LightingService", "AsusAppService", "ArmouryCrateService", "ArmouryCrateControlInterface"]),
        new("Corsair iCUE", ["iCUE", "iCUEDevicePluginHost", "Corsair.Service"], ["CorsairService", "CorsairLLAService", "CorsairGamingAudioConfig"]),
        new("Razer Synapse / Chroma", ["RazerCentral", "Razer Synapse 3", "RazerCortex", "Razer Synapse Service"], ["Razer Chroma SDK Service", "RzActionSvc", "Razer Synapse Service"]),
        new("Gigabyte RGB Fusion / Control Center", ["RGBFusion", "GCC", "GigabyteControlCenter"], ["GigabyteUpdateService", "GCC_Service"]),
        new("NZXT CAM", ["NZXT CAM", "NZXTCAM"], ["NZXT CAM Service"]),
        new("Lian Li L-Connect", ["L-Connect 3", "LConnect3", "L-Connect"], []),
        new("SignalRGB", ["SignalRgb", "SignalRgbLauncher"], []),
        new("Logitech G HUB", ["lghub", "lghub_agent", "lghub_system_tray"], ["LGHUB Updater Service"]),
        new("HyperX NGENUITY", ["NGENUITY", "HyperX NGENUITY"], []),
        new("ASRock Polychrome", ["ASRRGBLED", "Polychrome RGB"], []),
        new("Thermaltake TT RGB Plus", ["TT RGB Plus", "TTRGBPlus"], []),
        new("Cooler Master MasterPlus", ["MasterPlus", "MasterPlus+"], []),
        new("Govee / Wooting / other lighting tools", ["GoveeDesktop", "Wootility"], []),
    ];

    private static string Norm(string s) => s.ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace("_", "").Replace(".", "");

    /// <summary>Which known lighting programs are running among <paramref name="runningProcesses"/> (names without .exe) and which of their services
    /// are running. Matching ignores case, spaces and dashes.</summary>
    public static List<(RgbRival rival, List<string> processes, List<string> services)> Find(IEnumerable<string> runningProcesses, IEnumerable<string> runningServices)
    {
        var procs = runningProcesses.Select(p => (name: p, key: Norm(p))).ToList();
        var svcs = runningServices.Select(s => (name: s, key: Norm(s))).ToList();
        var found = new List<(RgbRival, List<string>, List<string>)>();
        foreach (var rival in Known)
        {
            var wantedProcs = rival.Processes.Select(Norm).ToHashSet();
            var wantedSvcs = rival.Services.Select(Norm).ToHashSet();
            var p = procs.Where(x => wantedProcs.Contains(x.key)).Select(x => x.name).Distinct().ToList();
            var s = svcs.Where(x => wantedSvcs.Contains(x.key)).Select(x => x.name).Distinct().ToList();
            if (p.Count > 0 || s.Count > 0) found.Add((rival, p, s));
        }
        return found;
    }

    /// <summary>Reads what is running now (process names and running service names).</summary>
    public static List<(RgbRival rival, List<string> processes, List<string> services)> Scan()
    {
        var procs = new List<string>();
        try { foreach (var p in Process.GetProcesses()) { using (p) procs.Add(p.ProcessName); } }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { /* a partial list is fine */ }
        return Find(procs, RunningServices());
    }

    private static List<string> RunningServices()
    {
        var names = new List<string>();
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (key is null) return names;
            var wanted = Known.SelectMany(k => k.Services).Select(Norm).ToHashSet();
            foreach (var name in key.GetSubKeyNames())
                if (wanted.Contains(Norm(name)) && ServiceRunning(name)) names.Add(name);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { /* no list: processes only */ }
        return names;
    }

    private static bool ServiceRunning(string name)
    {
        try
        {
            using var info = Process.Start(new ProcessStartInfo("sc.exe", $"query \"{name}\"") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true, StandardOutputEncoding = Encoding.ASCII });
            var text = info?.StandardOutput.ReadToEnd() ?? "";
            info?.WaitForExit(1500);
            return text.Contains("RUNNING", StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return false; }
    }

    /// <summary>Asks the program's windows to close, and ends what is left after a moment; then stops its running services (administrator needed for those).
    /// Returns how many things were stopped. The person pressed a button naming exactly this program, nothing else is touched.</summary>
    public static int Close(RgbRival rival, IEnumerable<string> processNames, IEnumerable<string> serviceNames)
    {
        var stopped = 0;
        var wanted = processNames.Select(Norm).ToHashSet();
        var victims = new List<Process>();
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                if (wanted.Contains(Norm(p.ProcessName))) victims.Add(p);
                else p.Dispose();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        foreach (var p in victims)
        {
            using (p)
            {
                try
                {
                    if (p.MainWindowHandle != IntPtr.Zero) p.CloseMainWindow();
                    if (!p.WaitForExit(1500)) p.Kill(entireProcessTree: true);
                    stopped++;
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { /* protected process: left running, the page tells */ }
            }
        }
        foreach (var service in serviceNames)
        {
            try
            {
                using var stop = Process.Start(new ProcessStartInfo("sc.exe", $"stop \"{service}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
                stop?.WaitForExit(4000);
                if (stop is { ExitCode: 0 }) stopped++;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        }
        Log.Write($"rgb: closed {rival.Name}: {stopped} item(s)");
        return stopped;
    }
}

/// <summary>The loudness of what the PC is playing (0 to 1), for the music effect.</summary>
public static class AudioPeak
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorClass { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object endpoint);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        int GetPeakValue(out float peak);
    }

    private static IAudioMeterInformation? _meter;
    private static long _triedAt;

    /// <summary>The loudest recent sound on the default speakers, or null when there is no audio device. Call it from a worker thread.</summary>
    public static double? Read()
    {
        try
        {
            if (_meter is null)
            {
                if (Environment.TickCount64 - _triedAt < 5000) return null;       // no device: look again every few seconds, not every frame
                _triedAt = Environment.TickCount64;
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorClass();
                if (enumerator.GetDefaultAudioEndpoint(0, 1, out var device) != 0) return null;
                var iid = typeof(IAudioMeterInformation).GUID;
                device.Activate(ref iid, 23, IntPtr.Zero, out var endpoint);
                _meter = endpoint as IAudioMeterInformation;
            }
            return _meter is not null && _meter.GetPeakValue(out var peak) == 0 ? Math.Clamp(peak, 0, 1) : null;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException)
        {
            _meter = null;
            return null;
        }
    }
}
