using System.Diagnostics;
using System.Net.NetworkInformation;

namespace Shabpa.Core;

public sealed record SensorSnapshot
{
    private static readonly IReadOnlySet<string> NoProcesses = new HashSet<string>();

    public DateTime Time { get; init; }
    public double DownMbps { get; init; }
    public double UpMbps { get; init; }
    public double CpuLoad { get; init; }
    public double GpuLoad { get; init; }
    public double IdleSeconds { get; init; }
    public bool GameRunning { get; init; }
    /// <summary>Lower-case process name of the full-screen game, or "" when none is running.</summary>
    public string GameProcess { get; init; } = "";
    /// <summary>A readable name of the game that is running (also while another window is in front), or "".</summary>
    public string GameName { get; init; } = "";
    /// <summary>When that game's program started, so a timer can show how long it has been running.</summary>
    public DateTime? GameSince { get; init; }
    public bool Recording { get; init; }
    public double CpuWatts { get; init; }
    public double GpuWatts { get; init; }
    public double TotalWatts { get; init; }
    /// <summary>True when CPU power comes from the hardware energy meter rather than an estimate.</summary>
    public bool CpuMeasured { get; init; }
    /// <summary>Lower-case process names without ".exe".</summary>
    public IReadOnlySet<string> Processes { get; init; } = NoProcesses;
}

/// <summary>Polls the machine every few seconds on a background thread.</summary>
public sealed class SensorHub : IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    private readonly Func<Settings> _settings;
    private readonly ManualResetEventSlim _stop = new(false);
    private Thread? _thread;

    private Dictionary<string, (long rx, long tx)> _net = new();
    private long _netTicks;
    private (long idle, long kernel, long user) _cpu;
    private bool _cpuPrimed;
    private PerformanceCounter? _cpuPower;
    private bool _cpuPowerTried;
    private Dictionary<string, CounterSample> _gpu = new();
    private string? _mainGpuLuid;
    private int _tick;

    public SensorSnapshot Latest { get; private set; } = new() { Time = DateTime.Now };
    public event Action<SensorSnapshot>? Updated;

    public SensorHub(Func<Settings> settings) => _settings = settings;

    public void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(Loop) { IsBackground = true, Name = "sensors", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    private void Loop()
    {
        Sample(); // primes the deltas
        while (!_stop.Wait(Interval))
        {
            try
            {
                Latest = Sample();
                Updated?.Invoke(Latest);
            }
            catch (Exception ex)
            {
                Log.Write($"sensor tick failed: {ex.Message}");
            }
        }
    }

    public SensorSnapshot Sample()
    {
        var settings = _settings();
        var (down, up) = ReadNetwork();
        var cpu = ReadCpu();
        var (gpuAny, gpuMain) = ReadGpu();
        var processes = ReadProcesses();
        var state = Native.SHQueryUserNotificationState(out var notification) == 0 ? notification : 0;
        var game = _watcher.Update(state, gpuAny, UserGames(settings));
        var recording = RecorderNames(settings.RecorderProcesses).Any(processes.Contains);

        var cpuWatts = ReadCpuWatts();
        var measured = cpuWatts is not null;
        cpuWatts ??= 8 + cpu / 100 * Math.Max(0, settings.CpuMaxWatts - 8);
        var gpuWatts = settings.GpuIdleWatts + gpuMain / 100 * Math.Max(0, settings.GpuMaxWatts - settings.GpuIdleWatts);

        _tick++;
        return new SensorSnapshot
        {
            Time = DateTime.Now,
            DownMbps = down,
            UpMbps = up,
            CpuLoad = cpu,
            GpuLoad = gpuAny,
            IdleSeconds = Native.IdleTime().TotalSeconds,
            GameRunning = game,
            GameProcess = game ? (_watcher.Current?.Process ?? ForegroundProcess()) : "",
            GameName = _watcher.Current?.Name ?? "",
            GameSince = _watcher.Current?.Start,
            Recording = recording,
            CpuWatts = cpuWatts.Value,
            GpuWatts = gpuWatts,
            TotalWatts = cpuWatts.Value + gpuWatts + settings.BaseWatts,
            CpuMeasured = measured,
            Processes = processes,
        };
    }

    public static IEnumerable<string> RecorderNames(string list) =>
        list.Split([',', '،', ';', '؛', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(RuleEngine.NormalizeProcess)
            .Where(n => n.Length > 0);

    private (double down, double up) ReadNetwork()
    {
        var now = Stopwatch.GetTimestamp();
        var seconds = _netTicks == 0 ? 0 : Stopwatch.GetElapsedTime(_netTicks, now).TotalSeconds;
        _netTicks = now;

        var current = new Dictionary<string, (long rx, long tx)>();
        double down = 0, up = 0;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback) continue;
            try
            {
                var stats = nic.GetIPStatistics();
                current[nic.Id] = (stats.BytesReceived, stats.BytesSent);
                if (seconds > 0 && _net.TryGetValue(nic.Id, out var prev))
                {
                    // A VPN shows the same bytes on the tunnel and on the physical adapter, so the busiest
                    // adapter is a better figure for "the" transfer speed than the sum of all adapters.
                    down = Math.Max(down, Math.Max(0, stats.BytesReceived - prev.rx) * 8 / 1e6 / seconds);
                    up = Math.Max(up, Math.Max(0, stats.BytesSent - prev.tx) * 8 / 1e6 / seconds);
                }
            }
            catch (NetworkInformationException) { /* adapter went away mid-read */ }
        }
        _net = current;
        return (down, up);
    }

    private double ReadCpu()
    {
        if (!Native.GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        var prev = _cpu;
        _cpu = (idle, kernel, user);
        if (!_cpuPrimed) { _cpuPrimed = true; return 0; }
        // Kernel time includes idle time.
        var total = (kernel - prev.kernel) + (user - prev.user);
        if (total <= 0) return 0;
        return Math.Clamp(100.0 * (total - (idle - prev.idle)) / total, 0, 100);
    }

    /// <summary>CPU package power in watts from the Windows energy meter (Intel/AMD RAPL), if exposed.</summary>
    private double? ReadCpuWatts()
    {
        try
        {
            if (!_cpuPowerTried)
            {
                _cpuPowerTried = true;
                if (PerformanceCounterCategory.Exists("Energy Meter"))
                {
                    var instance = new PerformanceCounterCategory("Energy Meter").GetInstanceNames()
                        .FirstOrDefault(n => n.Contains("PKG", StringComparison.OrdinalIgnoreCase));
                    if (instance is not null)
                    {
                        _cpuPower = new PerformanceCounter("Energy Meter", "Power", instance, true);
                        _cpuPower.NextValue();
                        return null;
                    }
                }
            }
            if (_cpuPower is null) return null;
            var watts = _cpuPower.NextValue() / 1000.0; // counter is in milliwatts
            return watts is > 0.5 and < 1000 ? watts : null;
        }
        catch (Exception ex)
        {
            Log.Write($"energy meter unavailable: {ex.Message}");
            _cpuPower = null;
            return null;
        }
    }

    /// <summary>Returns the busiest engine of any adapter and of the main (discrete) adapter, in percent.</summary>
    private (double any, double main) ReadGpu()
    {
        try
        {
            if (_tick % 20 == 0) _mainGpuLuid = FindMainGpu() ?? _mainGpuLuid;

            var data = new PerformanceCounterCategory("GPU Engine").ReadCategory();
            if (!data.Contains("Utilization Percentage")) return (0, 0);
            var current = new Dictionary<string, CounterSample>();
            var perEngine = new Dictionary<string, double>();
            foreach (InstanceData inst in data["Utilization Percentage"].Values)
            {
                current[inst.InstanceName] = inst.Sample;
                if (!_gpu.TryGetValue(inst.InstanceName, out var prev)) continue;
                var value = CounterSampleCalculator.ComputeCounterValue(prev, inst.Sample);
                if (float.IsNaN(value) || value <= 0) continue;
                // "pid_123_luid_0x.._0x.._phys_0_eng_0_engtype_3D": sum over processes per engine.
                var at = inst.InstanceName.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
                if (at < 0) continue;
                var engine = inst.InstanceName[at..].ToLowerInvariant();
                perEngine[engine] = perEngine.GetValueOrDefault(engine) + value;
            }
            _gpu = current;
            if (perEngine.Count == 0) return (0, 0);

            var any = Math.Clamp(perEngine.Values.Max(), 0, 100);
            var main = _mainGpuLuid is null
                ? any
                : Math.Clamp(perEngine.Where(e => e.Key.StartsWith(_mainGpuLuid, StringComparison.Ordinal))
                    .Select(e => e.Value).DefaultIfEmpty(0).Max(), 0, 100);
            return (any, main);
        }
        catch (Exception ex)
        {
            if (_tick == 0) Log.Write($"gpu counters unavailable: {ex.Message}");
            return (0, 0);
        }
    }

    /// <summary>The adapter using the most dedicated video memory; integrated GPUs use shared memory.</summary>
    private static string? FindMainGpu()
    {
        try
        {
            var data = new PerformanceCounterCategory("GPU Adapter Memory").ReadCategory();
            if (!data.Contains("Dedicated Usage")) return null;
            string? best = null;
            long bestValue = 0;
            foreach (InstanceData inst in data["Dedicated Usage"].Values)
            {
                if (inst.RawValue <= bestValue) continue;
                bestValue = inst.RawValue;
                best = inst.InstanceName.ToLowerInvariant();
            }
            return best;
        }
        catch
        {
            return null;
        }
    }

    private static HashSet<string> ReadProcesses()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in Process.GetProcesses())
        {
            try { names.Add(p.ProcessName.ToLowerInvariant()); }
            catch { /* process exited */ }
            finally { p.Dispose(); }
        }
        return names;
    }

    private readonly GameWatcher _watcher = new();

    /// <summary>Programs the person told Shabpa are games, and every game they made an overlay profile for.</summary>
    private static IEnumerable<string> UserGames(Settings settings) =>
        RecorderNames(settings.GameList).Concat(settings.Overlay.Profiles.Select(p => p.Game).Where(g => g.Length > 0));

    private static string ForegroundProcess()
    {
        try
        {
            Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out var pid);
            if (pid == 0) return "";
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName.ToLowerInvariant();
        }
        catch
        {
            return "";
        }
    }


    public void Dispose()
    {
        _stop.Set();
        _thread?.Join(1000);
        _cpuPower?.Dispose();
    }
}
