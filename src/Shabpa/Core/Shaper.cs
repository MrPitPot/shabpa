using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Shabpa.Core;

public enum Verdict { Send, Queued, Drop }

/// <summary>A token bucket with a short queue in front of it: packets that do not fit the rate wait
/// their turn, and only when the wait gets too long are they dropped (which is what tells TCP to slow
/// down). Pure logic; the caller supplies the clock in seconds.</summary>
public sealed class RateQueue<T>
{
    private readonly Queue<(T item, int size)> _queue = new();
    private double _tokens;
    private double _last = double.NaN;

    public double BytesPerSecond { get; }
    /// <summary>How much may go out in one go after a pause.</summary>
    public double Burst { get; }
    public double MaxQueuedBytes { get; }
    public int QueuedBytes { get; private set; }
    public int Count => _queue.Count;

    public RateQueue(double bytesPerSecond)
    {
        BytesPerSecond = Math.Max(1, bytesPerSecond);
        Burst = Math.Max(3000, BytesPerSecond * 0.05);
        MaxQueuedBytes = Math.Max(96_000, BytesPerSecond * 0.3);
        _tokens = Burst;
    }

    private void Refill(double now)
    {
        if (!double.IsNaN(_last) && now > _last)
            _tokens = Math.Min(Burst, _tokens + (now - _last) * BytesPerSecond);
        _last = now;
    }

    public Verdict Offer(T item, int size, double now)
    {
        Refill(now);
        // Tokens may go negative: a packet larger than the burst (loopback packets reach 64 KB) still
        // passes, and the debt is paid off before the next one.
        if (_queue.Count == 0 && _tokens > 0)
        {
            _tokens -= size;
            return Verdict.Send;
        }
        if (QueuedBytes + size > MaxQueuedBytes) return Verdict.Drop;
        _queue.Enqueue((item, size));
        QueuedBytes += size;
        return Verdict.Queued;
    }

    /// <summary>Hands over the queued packets the rate allows by now, oldest first.</summary>
    public void Release(double now, Action<T> send)
    {
        Refill(now);
        while (_queue.Count > 0 && _tokens > 0)
        {
            var (item, size) = _queue.Dequeue();
            QueuedBytes -= size;
            _tokens -= size;
            send(item);
        }
    }

    public void Flush(Action<T> send)
    {
        while (_queue.Count > 0) send(_queue.Dequeue().item);
        QueuedBytes = 0;
    }
}

/// <summary>Which process owns which local TCP/UDP port right now (IP Helper tables; no admin needed).</summary>
public static class PortTable
{
    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int family, int tableClass, uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(IntPtr table, ref int size, bool sort, int family, int tableClass, uint reserved);

    private const int AF_INET = 2, AF_INET6 = 23;
    private const int TCP_TABLE_OWNER_PID_ALL = 5, UDP_TABLE_OWNER_PID = 1;

    public static int Key(bool tcp, int port) => (tcp ? 0x10000 : 0x20000) | (port & 0xFFFF);

    /// <summary>Port key -> owning process id for every TCP connection/listener and UDP socket.</summary>
    public static Dictionary<int, int> Read()
    {
        var map = new Dictionary<int, int>(512);
        // (tcp?, family, row size, port offset, pid offset)
        Fill(map, true, AF_INET, 24, 8, 20);
        Fill(map, true, AF_INET6, 56, 20, 52);
        Fill(map, false, AF_INET, 12, 4, 8);
        Fill(map, false, AF_INET6, 28, 20, 24);
        return map;
    }

    private static void Fill(Dictionary<int, int> map, bool tcp, int family, int rowSize, int portOffset, int pidOffset)
    {
        var size = 0;
        uint Call(IntPtr buffer) => tcp
            ? GetExtendedTcpTable(buffer, ref size, false, family, TCP_TABLE_OWNER_PID_ALL, 0)
            : GetExtendedUdpTable(buffer, ref size, false, family, UDP_TABLE_OWNER_PID, 0);

        Call(IntPtr.Zero);
        if (size <= 0) return;
        size += 4096; // the table can grow between the two calls
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (Call(buffer) != 0) return;
            var count = Marshal.ReadInt32(buffer);
            for (var i = 0; i < count; i++)
            {
                var row = buffer + 4 + i * rowSize;
                // The port sits in the first two bytes in network byte order.
                var port = (Marshal.ReadByte(row, portOffset) << 8) | Marshal.ReadByte(row, portOffset + 1);
                var pid = Marshal.ReadInt32(row, pidOffset);
                if (port != 0) map[Key(tcp, port)] = pid;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}

/// <summary>Finds protocol and ports in a raw IPv4/IPv6 packet.</summary>
public static class PacketParser
{
    public static bool TryParse(ReadOnlySpan<byte> packet, out bool tcp, out int sourcePort, out int destinationPort)
    {
        tcp = false;
        sourcePort = destinationPort = 0;
        if (packet.Length < 20) return false;

        int protocol, offset;
        var version = packet[0] >> 4;
        if (version == 4)
        {
            offset = (packet[0] & 0x0F) * 4;
            protocol = packet[9];
            // Later fragments carry no TCP/UDP header.
            if ((((packet[6] & 0x1F) << 8) | packet[7]) != 0) return false;
        }
        else if (version == 6)
        {
            if (packet.Length < 40) return false;
            protocol = packet[6];
            offset = 40;
            // Skip hop-by-hop (0), routing (43) and destination-options (60) headers.
            for (var i = 0; i < 4 && protocol is 0 or 43 or 60; i++)
            {
                if (packet.Length < offset + 8) return false;
                protocol = packet[offset];
                offset += (packet[offset + 1] + 1) * 8;
            }
        }
        else
        {
            return false;
        }

        if (protocol is not (6 or 17) || offset < 20 || packet.Length < offset + 4) return false;
        tcp = protocol == 6;
        sourcePort = (packet[offset] << 8) | packet[offset + 1];
        destinationPort = (packet[offset + 2] << 8) | packet[offset + 3];
        return true;
    }
}

/// <summary>Per-application download/upload shaping. Packets are taken from the network stack with the
/// WinDivert driver, held back to the configured rate and put back. The driver is only attached while
/// at least one limit exists; closing the handle (or the app dying) restores normal traffic at once.</summary>
public sealed class Shaper : IDisposable
{
    [DllImport("WinDivert.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr WinDivertOpen(string filter, int layer, short priority, ulong flags);

    [DllImport("WinDivert.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinDivertRecv(IntPtr handle, byte[] packet, uint length, out uint received, byte[] address);

    [DllImport("WinDivert.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinDivertSend(IntPtr handle, byte[] packet, uint length, out uint sent, byte[] address);

    [DllImport("WinDivert.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinDivertShutdown(IntPtr handle, int how);

    [DllImport("WinDivert.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinDivertClose(IntPtr handle);

    [DllImport("WinDivert.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinDivertSetParam(IntPtr handle, int parameter, ulong value);

    private const int AddressSize = 80;      // sizeof(WINDIVERT_ADDRESS)
    private const int MaxPacket = 40 + 0xFFFF;
    private const uint OutboundBit = 1u << 17, LoopbackBit = 1u << 18;
    private static readonly IntPtr Invalid = new(-1);

    private sealed class Held(byte[] packet, byte[] address)
    {
        public readonly byte[] Packet = packet;
        public readonly byte[] Address = address;
    }

    private sealed class AppRule
    {
        public bool Blocked;
        public double DownMbps, UpMbps;
        public RateQueue<Held>? Down, Up;
    }

    private readonly object _gate = new();
    private Dictionary<int, string> _names = [];
    private Dictionary<string, AppRule> _rules = [];
    private Dictionary<int, AppRule> _ports = [];
    private HashSet<int> _knownPorts = [];
    private long _lastRefresh;
    private IntPtr _handle = Invalid;
    private Thread? _receiver, _pump;
    private volatile bool _running;

    public bool Running => _running;
    /// <summary>Why shaping is not active although limits exist, or null.</summary>
    public string? Error { get; private set; }
    public long Dropped { get; private set; }

    /// <summary>True when the driver library sits next to the app.</summary>
    public static bool DriverPresent =>
        System.IO.File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, "WinDivert.dll")) &&
        System.IO.File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, "WinDivert64.sys"));

    private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    /// <summary>Makes the running state match <paramref name="plan"/>: starts, updates or stops shaping.</summary>
    public void Apply(IReadOnlyList<EffectiveLimit> plan)
    {
        var count = SetRules(plan);
        if (count == 0) Stop();
        else if (!_running) Start();
    }

    /// <summary>Installs the rules and rebuilds the port map (no driver involved); returns how many rules there are.</summary>
    internal int SetRules(IReadOnlyList<EffectiveLimit> plan)
    {
        var rules = new Dictionary<string, AppRule>();
        lock (_gate)
        {
            foreach (var limit in plan.Where(l => l.Blocked || l.DownMbps > 0 || l.UpMbps > 0))
            {
                _rules.TryGetValue(limit.Key, out var old);
                var rule = new AppRule { Blocked = limit.Blocked, DownMbps = limit.DownMbps, UpMbps = limit.UpMbps };
                // Keep a queue whose rate did not change so in-flight packets are not reordered.
                rule.Down = limit.DownMbps <= 0 ? null : old?.Down is { } d && old.DownMbps == limit.DownMbps ? d : new RateQueue<Held>(limit.DownMbps * 125_000);
                rule.Up = limit.UpMbps <= 0 ? null : old?.Up is { } u && old.UpMbps == limit.UpMbps ? u : new RateQueue<Held>(limit.UpMbps * 125_000);
                rules[limit.Key] = rule;
            }

            // Queues that are going away must not swallow their packets.
            if (_handle != Invalid)
                foreach (var old in _rules.Values)
                foreach (var queue in new[] { old.Down, old.Up })
                    if (queue is not null && !rules.Values.Any(r => r.Down == queue || r.Up == queue))
                        queue.Flush(Inject);

            _rules = rules;
            _lastRefresh = 0;
        }

        RefreshPorts(); // not under the lock: naming every process can take seconds, and the whole PC's traffic waits for the lock
        return rules.Count;
    }

    private void Start()
    {
        Error = null;
        try
        {
            // Layer 0 = network. Everything TCP/UDP is diverted while limits exist, loopback included,
            // because apps behind a local proxy only ever talk to 127.0.0.1. Packets another packet driver
            // (NetLimiter, a VPN) re-injected are left alone: diverting those too can make two drivers
            // bounce the same packet between them.
            _handle = WinDivertOpen("(tcp or udp) and !impostor", 0, 0, 0);
        }
        catch (DllNotFoundException)
        {
            Error = "driver-missing";
            return;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return;
        }

        if (_handle == Invalid)
        {
            var code = Marshal.GetLastWin32Error();
            Error = code switch
            {
                2 => "driver-missing",
                5 => "needs-admin",
                577 => L.T("ویندوز امضای درایور WinDivert رو قبول نکرد.", "Windows rejected the WinDivert driver signature."),
                1275 => L.T("ویندوز یا آنتی‌ویروس جلوی درایور WinDivert رو گرفته.", "Windows or the antivirus blocked the WinDivert driver."),
                _ => L.T($"درایور WinDivert باز نشد (کد {code}).", $"Could not open the WinDivert driver (code {code})."),
            };
            Log.Write($"WinDivertOpen failed: {code}");
            return;
        }

        WinDivertSetParam(_handle, 0, 16384);     // queue length (packets)
        WinDivertSetParam(_handle, 2, 33554432);  // queue size (bytes)

        _running = true;
        _receiver = new Thread(Receive) { IsBackground = true, Name = "shaper-recv", Priority = ThreadPriority.AboveNormal };
        _pump = new Thread(Pump) { IsBackground = true, Name = "shaper-pump", Priority = ThreadPriority.AboveNormal };
        _receiver.Start();
        _pump.Start();
        Log.Write("shaper started");
    }

    private void Stop()
    {
        if (!_running && _handle == Invalid) return;
        _running = false;
        var handle = _handle;
        if (handle != Invalid)
        {
            WinDivertShutdown(handle, 1); // stop receiving; the receive loop drains and exits
            _receiver?.Join(1500);
            _pump?.Join(500);
            lock (_gate)
            {
                foreach (var rule in _rules.Values)
                {
                    rule.Down?.Flush(Inject);
                    rule.Up?.Flush(Inject);
                }
                _handle = Invalid;
            }
            WinDivertClose(handle);
            Log.Write($"shaper stopped (dropped {Dropped} packets)");
        }
    }

    private void Inject(Held held)
    {
        if (_handle != Invalid) WinDivertSend(_handle, held.Packet, (uint)held.Packet.Length, out _, held.Address);
    }

    private void Receive()
    {
        var packet = new byte[MaxPacket];
        var address = new byte[AddressSize];
        var handle = _handle;
        while (true)
        {
            if (!WinDivertRecv(handle, packet, (uint)packet.Length, out var length, address))
            {
                var code = Marshal.GetLastWin32Error();
                // 232 = queue drained after shutdown, 6/995 = handle closed.
                if (code is 232 or 6 or 995 || !_running) break;
                continue;
            }

            Queue(packet, (int)length, address, handle);
        }
    }

    private void Queue(byte[] packet, int length, byte[] address, IntPtr handle)
    {
        AppRule? receiver = null, sender = null;
        if (PacketParser.TryParse(packet.AsSpan(0, length), out var tcp, out var sourcePort, out var destinationPort))
        {
            var flags = BitConverter.ToUInt32(address, 8);
            var loopback = (flags & LoopbackBit) != 0;
            var outbound = (flags & OutboundBit) != 0;
            lock (_gate)
            {
                // A loopback packet is somebody's upload and somebody else's download at once.
                if (loopback || !outbound) receiver = Owner(tcp, destinationPort);
                if (loopback || outbound) sender = Owner(tcp, sourcePort);

                if (receiver?.Blocked == true || sender?.Blocked == true)
                {
                    Dropped++;
                    return;
                }

                var queue = receiver?.Down ?? sender?.Up;
                if (queue is not null)
                {
                    var held = new Held(packet.AsSpan(0, length).ToArray(), (byte[])address.Clone());
                    switch (queue.Offer(held, length, Now))
                    {
                        case Verdict.Queued: return;
                        case Verdict.Drop: Dropped++; return;
                    }
                }
            }
        }
        WinDivertSend(handle, packet, (uint)length, out _, address);
    }

    /// <summary>Must be called under the lock.</summary>
    private AppRule? Owner(bool tcp, int port)
    {
        var key = PortTable.Key(tcp, port);
        if (_ports.TryGetValue(key, out var rule)) return rule;
        if (_knownPorts.Contains(key)) return null;
        // A port we have never seen: almost always a brand-new connection, and its very first packets
        // must already be limited. Re-reading the table takes a millisecond or two; a short floor keeps a
        // burst of new connections from doing it on every packet.
        if (Stopwatch.GetElapsedTime(_lastRefresh).TotalMilliseconds < 20) return null;
        RefreshPortsQuick();
        if (_ports.TryGetValue(key, out rule)) return rule;
        // Still unknown (a socket that closed already): remember it until the next full rebuild.
        _knownPorts.Add(key);
        return null;
    }

    /// <summary>Test seams: the system's port table and the name of a process id.</summary>
    internal Func<Dictionary<int, int>> ReadTable = PortTable.Read;
    internal Func<int, string> NameOf = ProcessName;
    private int _wantNames;

    /// <summary>The port map from the table and the cached names only (cheap; called under the lock from the packet loop).
    /// A process that is not named yet is picked up by the next full rebuild, which runs within a few milliseconds.</summary>
    private void RefreshPortsQuick()
    {
        _lastRefresh = Stopwatch.GetTimestamp();
        if (_rules.Count == 0) { _ports = []; _knownPorts = []; return; }
        try
        {
            var table = ReadTable();
            var ports = new Dictionary<int, AppRule>();
            var known = new HashSet<int>();
            var unnamed = false;
            foreach (var (key, pid) in table)
            {
                if (!_names.TryGetValue(pid, out var name)) { unnamed = true; continue; }
                known.Add(key);
                if (name.Length > 0 && _rules.TryGetValue(name, out var rule)) ports[key] = rule;
            }
            _ports = ports;
            _knownPorts = known;
            if (unnamed) Interlocked.Exchange(ref _wantNames, 1);
        }
        catch (Exception ex)
        {
            Log.Write($"port table: {ex.Message}");
        }
    }

    /// <summary>Full rebuild. The slow part (reading the table, naming processes: each lookup can take milliseconds and a busy PC
    /// has hundreds) runs WITHOUT the lock, because every packet of the PC waits while the lock is held; the result is swapped
    /// in under the lock afterwards, and dropped if the rules changed in the meantime.</summary>
    private void RefreshPorts()
    {
        Dictionary<string, AppRule> rules;
        Dictionary<int, string> known;
        lock (_gate)
        {
            _lastRefresh = Stopwatch.GetTimestamp();
            if (_rules.Count == 0) { _ports = []; _knownPorts = []; return; }
            rules = _rules;
            known = _names;
        }
        try
        {
            var table = ReadTable();
            var ports = new Dictionary<int, AppRule>();
            // Names are remembered only for processes still holding a port, so a reused id is re-read.
            var names = new Dictionary<int, string>();
            foreach (var (key, pid) in table)
            {
                if (!names.TryGetValue(pid, out var name))
                    names[pid] = name = known.TryGetValue(pid, out var cached) ? cached : NameOf(pid);
                if (name.Length > 0 && rules.TryGetValue(name, out var rule)) ports[key] = rule;
            }
            lock (_gate)
            {
                if (!ReferenceEquals(rules, _rules)) return; // newer rules bring their own rebuild
                _names = names;
                _ports = ports;
                _knownPorts = [.. table.Keys];
            }
        }
        catch (Exception ex)
        {
            Log.Write($"port table: {ex.Message}");
        }
    }

    /// <summary>Test aid: true while the packet lock is free (a slow rebuild must not hold it).</summary>
    internal bool GateFree()
    {
        if (!Monitor.TryEnter(_gate)) return false;
        Monitor.Exit(_gate);
        return true;
    }

    /// <summary>Test aid: the port map as it stands.</summary>
    internal int MappedPorts { get { lock (_gate) return _ports.Count; } }

    private static string ProcessName(int pid)
    {
        if (pid is 0 or 4) return "system";
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName.ToLowerInvariant();
        }
        catch
        {
            return "";
        }
    }

    private void Pump()
    {
        var ticks = 0;
        while (_running)
        {
            Thread.Sleep(4);
            lock (_gate)
            {
                var now = Now;
                foreach (var rule in _rules.Values)
                {
                    rule.Down?.Release(now, Inject);
                    rule.Up?.Release(now, Inject);
                }
            }
            // Ports are reused by other programs over time; rebuild the map about once a second (outside the lock).
            if (++ticks % 60 == 0 || Interlocked.Exchange(ref _wantNames, 0) == 1) RefreshPorts();
        }
    }

    public void Dispose() => Stop();
}
