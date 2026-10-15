using System.Diagnostics;

namespace Shabpa.Core;

/// <summary>Everything the overlay and the graphics page show, sampled about once a second.</summary>
public sealed record TelemetrySample
{
    public DateTime Time { get; init; } = DateTime.Now;
    public double CpuLoad { get; init; }
    public double? CpuTempC { get; init; }
    public double? CpuWatts { get; init; }
    public double? CpuMHz { get; init; }
    /// <summary>Only available with an extra hardware sensor library; null when Windows does not expose it.</summary>
    public double? CpuFanPercent { get; init; }
    /// <summary>Motherboard readings by key (system / VRM / chipset / socket temperature, pump and system fan, voltages).</summary>
    public IReadOnlyDictionary<string, double>? Board { get; init; }
    public double? CpuFanRpm { get; init; }
    public double? RamTempC { get; init; }
    public double RamPercent { get; init; }
    public double RamUsedGb { get; init; }
    public double RamTotalGb { get; init; }
    public GpuReading Gpu { get; init; } = new();
    public double Fps { get; init; }
    public double Low1Fps { get; init; }
    public double FrameMs { get; init; }
    public string FpsProcess { get; init; } = "";
    public double? PingMs { get; init; }
    public double DownMbps { get; init; }
    public double UpMbps { get; init; }
    public bool InGame { get; init; }
    public string Game { get; init; } = "";
    public TimeSpan GameTime { get; init; }

    public double? GpuLoad => Gpu.LoadPercent;
    public double? VramPercent => Gpu.VramUsedMb is { } used && Gpu.VramTotalMb is { } total && total > 0 ? Math.Clamp(100 * used / total, 0, 100) : null;
}

/// <summary>Samples CPU, memory, graphics card, frame rate, ping and network on its own thread. It only
/// does real work while something is looking at it (the overlay, the graphics page) or a game is running.</summary>
public sealed class Telemetry : IDisposable
{
    private const int HistoryLength = 150;

    private readonly Func<SensorSnapshot> _sensors;
    private readonly Func<IReadOnlyList<double?>> _ping;
    private readonly ManualResetEventSlim _stop = new(false);
    private readonly ManualResetEventSlim _wake = new(false);
    private readonly object _gate = new();
    private readonly Queue<TelemetrySample> _history = new();
    private Thread? _thread;
    private int _demand;

    private IGpuProvider? _gpu;
    private BoardSensors? _board;

    /// <summary>Switches the optional motherboard sensor module (CPU temperature and fan) on or off.</summary>
    public volatile bool UseBoardSensors;

    /// <summary>"" when fine or off; "admin", "nodata" or "error" when the module is on but cannot read.</summary>
    public string BoardProblem => _board?.Problem ?? "";
    private FpsMeter? _fps;
    private DateTime _fpsRetry = DateTime.MinValue;
    private (long idle, long kernel, long user) _cpu;
    private bool _cpuPrimed;
    private PerformanceCounter? _thermal;
    private PerformanceCounter? _cpuFreq, _cpuPerf;
    private bool _cpuFreqTried;
    private bool _thermalTried;
    private DateTime _vramAt = DateTime.MinValue;
    private double? _vramUsed;
    private DateTime? _gameStarted;
    private string _fpsError = "";
    private bool _wasActive;

    public TelemetrySample Latest { get; private set; } = new();
    public event Action<TelemetrySample>? Updated;

    public Telemetry(Func<SensorSnapshot> sensors, Func<IReadOnlyList<double?>> ping)
    {
        _sensors = sensors;
        _ping = ping;
    }

    private static string? _cpuName;

    /// <summary>"12th Gen Intel Core i7-12700K", from the registry (no (R) / (TM) marks).</summary>
    public static string CpuName()
    {
        if (_cpuName is not null) return _cpuName;
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            var raw = key?.GetValue("ProcessorNameString") as string ?? "";
            _cpuName = System.Text.RegularExpressions.Regex.Replace(raw.Replace("(R)", "").Replace("(TM)", ""), @"\s+", " ").Trim();
            // "Intel Core i7-12700K CPU @ 3.60GHz" -> drop the clock, it is shown separately.
            var at = _cpuName.IndexOf(" @ ", StringComparison.Ordinal);
            if (at > 0) _cpuName = _cpuName[..at].Replace(" CPU", "");
        }
        catch
        {
            _cpuName = "";
        }
        return _cpuName;
    }

    /// <summary>The graphics card provider, once it has been opened (AMD cards can also be tuned through it).</summary>
    public IGpuProvider? Gpu => _gpu;
    public bool FpsAvailable => _fps is not null;
    /// <summary>Why frame rates are unavailable ("needs-admin" or an error), or "".</summary>
    public string FpsError => _fpsError;

    public void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(Loop) { IsBackground = true, Name = "telemetry", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    /// <summary>Asks for live sampling (overlay on screen, graphics page open). Pair with <see cref="Release"/>.</summary>
    public void Acquire()
    {
        Interlocked.Increment(ref _demand);
        _wake.Set(); // sample straight away instead of waiting out the idle pause
    }

    public void Release() => Interlocked.Decrement(ref _demand);

    public IReadOnlyList<TelemetrySample> History()
    {
        lock (_gate) return [.. _history];
    }

    private void Loop()
    {
        try { _gpu = GpuProbe.Open(); }
        catch (Exception ex) { Log.Write($"gpu open failed: {ex.Message}"); }

        while (true)
        {
            var sensors = _sensors();
            var active = Volatile.Read(ref _demand) > 0 || sensors.GameRunning;
            var signalled = WaitHandle.WaitAny([_stop.WaitHandle, _wake.WaitHandle], active ? 1000 : 3000);
            if (signalled == 0) break;
            if (signalled == 1)
            {
                _wake.Reset();
                active = true;
            }
            try
            {
                if (!active)
                {
                    StopFps();
                    // One last sample says "the game is over" to whoever is keeping a session report.
                    if (_wasActive)
                    {
                        _wasActive = false;
                        var last = Sample(_sensors());
                        lock (_gate) Latest = last;
                        Updated?.Invoke(last);
                    }
                    continue;
                }
                _wasActive = true;
                var sample = Sample(_sensors());
                lock (_gate)
                {
                    _history.Enqueue(sample);
                    while (_history.Count > HistoryLength) _history.Dequeue();
                    Latest = sample;
                }
                Updated?.Invoke(sample);
            }
            catch (Exception ex)
            {
                Log.Write($"telemetry tick failed: {ex.Message}");
            }
        }
        StopFps();
        _gpu?.Dispose();
        _board?.Dispose();
    }

    private TelemetrySample Sample(SensorSnapshot sensors)
    {
        var now = DateTime.Now;
        var memory = MemoryCleaner.Read();

        var gpu = _gpu?.Read() ?? new GpuReading();
        if (UseBoardSensors && _board?.LastGpu is { } extra)
        {
            // The vendor library wins; the hardware-sensor module only fills what it left empty (any brand of card).
            gpu = gpu with
            {
                TempC = gpu.TempC ?? extra.TempC, HotspotC = gpu.HotspotC ?? extra.HotspotC, MemTempC = gpu.MemTempC ?? extra.MemTempC,
                CoreMHz = gpu.CoreMHz ?? extra.CoreMHz, MemMHz = gpu.MemMHz ?? extra.MemMHz, FanRpm = gpu.FanRpm ?? extra.FanRpm,
                FanPercent = gpu.FanPercent ?? extra.FanPercent, PowerW = gpu.PowerW ?? extra.PowerW,
                VoltageMv = gpu.VoltageMv ?? extra.VoltageMv, LoadPercent = gpu.LoadPercent ?? extra.LoadPercent,
            };
        }
        if (gpu.LoadPercent is null) gpu = gpu with { LoadPercent = sensors.GpuLoad };
        if (gpu.VramUsedMb is null)
        {
            // The adapter counters are comparatively heavy, so they are read every other second.
            if (now - _vramAt > TimeSpan.FromSeconds(2))
            {
                _vramAt = now;
                _vramUsed = ReadVramUsedMb();
            }
            gpu = gpu with { VramUsedMb = _vramUsed };
        }
        if (gpu.VramTotalMb is null && gpu.Name.Length > 0) gpu = gpu with { VramTotalMb = GpuProbe.VramTotalMb(gpu.Name) };

        var ping = _ping();
        double? lastPing = null;
        for (var i = ping.Count - 1; i >= 0; i--)
        {
            if (ping[i] is { } value)
            {
                lastPing = value;
                break;
            }
        }

        if (sensors.GameRunning) _gameStarted ??= now;
        else _gameStarted = null;

        var board = default(BoardSensors.Reading);
        if (UseBoardSensors)
        {
            board = (_board ??= new BoardSensors()).Read();
            _board.LastGpu = board.Gpu;
        }
        else if (_board is not null)
        {
            _board.Dispose();
            _board = null;
        }

        var (fps, low, frameMs, fpsProcess) = ReadFrames();
        return new TelemetrySample
        {
            Time = now,
            CpuLoad = ReadCpu(),
            CpuTempC = board.CpuTempC,
            CpuFanRpm = board.FanRpm,
            CpuFanPercent = board.FanPercent,
            Board = board.Board,
            CpuMHz = ReadCpuFrequency(),
            CpuWatts = sensors.CpuMeasured ? sensors.CpuWatts : null,
            RamPercent = memory.UsedPercent,
            RamUsedGb = memory.Used / 1073741824.0,
            RamTotalGb = memory.Total / 1073741824.0,
            Gpu = gpu,
            Fps = fps,
            Low1Fps = low,
            FrameMs = frameMs,
            FpsProcess = fpsProcess,
            PingMs = lastPing,
            DownMbps = sensors.DownMbps,
            UpMbps = sensors.UpMbps,
            InGame = sensors.GameRunning,
            Game = sensors.GameProcess,
            GameTime = _gameStarted is { } started ? now - started : TimeSpan.Zero,
        };
    }

    private double ReadCpu()
    {
        if (!Native.GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        var prev = _cpu;
        _cpu = (idle, kernel, user);
        if (!_cpuPrimed)
        {
            _cpuPrimed = true;
            return 0;
        }
        var total = (kernel - prev.kernel) + (user - prev.user);
        if (total <= 0) return 0;
        return Math.Clamp(100.0 * (total - (idle - prev.idle)) / total, 0, 100);
    }

    /// <summary>Current CPU clock: the base frequency counter times "% Processor Performance" (which goes above 100 when boosting).</summary>
    private double? ReadCpuFrequency()
    {
        try
        {
            if (!_cpuFreqTried)
            {
                _cpuFreqTried = true;
                _cpuFreq = new PerformanceCounter("Processor Information", "Processor Frequency", "_Total", true);
                _cpuPerf = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total", true);
                _cpuFreq.NextValue();
                _cpuPerf.NextValue();
                return null;
            }
            if (_cpuFreq is null || _cpuPerf is null) return null;
            var mhz = _cpuFreq.NextValue() * _cpuPerf.NextValue() / 100.0;
            return mhz is > 100 and < 8000 ? mhz : null;
        }
        catch
        {
            _cpuFreq = _cpuPerf = null;
            return null;
        }
    }

    private static double? ReadVramUsedMb()
    {
        try
        {
            var data = new PerformanceCounterCategory("GPU Adapter Memory").ReadCategory();
            if (!data.Contains("Dedicated Usage")) return null;
            long best = 0;
            foreach (System.Diagnostics.InstanceData inst in data["Dedicated Usage"].Values)
                best = Math.Max(best, inst.RawValue);
            return best > 0 ? best / 1048576.0 : null;
        }
        catch
        {
            return null;
        }
    }

    // ---- frame rate ---------------------------------------------------------------------------

    private (double fps, double low, double frameMs, string process) ReadFrames()
    {
        if (_fps is null && DateTime.Now >= _fpsRetry)
        {
            _fps = FpsMeter.TryStart(out var error);
            _fpsError = _fps is null ? error ?? "" : "";
            if (_fps is null) _fpsRetry = DateTime.Now.AddSeconds(30);
        }
        if (_fps is null) return (0, 0, 0, "");

        try
        {
            Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out var foreground);
            var pid = (int)foreground;
            var stats = pid == 0 ? FrameStats.None : _fps.Stats(pid);
            if (stats.Frames < 5)
            {
                // The window in front is not what is drawing (a launcher in front of the game, a second monitor, the
                // desktop itself): take the busiest program that is not part of Windows' own shell.
                if (_fps.Busiest(p => !IsShell(p)) is not { } busy) return (0, 0, 0, "");
                (pid, stats) = busy;
            }
            string name;
            try
            {
                using var process = Process.GetProcessById(pid);
                name = process.ProcessName;
            }
            catch
            {
                name = "";
            }
            return (stats.Fps, stats.OnePercentLowFps, stats.AverageFrameMs, name);
        }
        catch
        {
            return (0, 0, 0, "");
        }
    }

    private static readonly HashSet<string> ShellNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "dwm", "explorer", "csrss", "winlogon", "ShellExperienceHost", "SearchHost", "StartMenuExperienceHost", "TextInputHost",
        "ApplicationFrameHost", "LockApp", "SystemSettings", "Shabpa", "WidgetService", "Widgets",
    };

    private static bool IsShell(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return ShellNames.Contains(process.ProcessName);
        }
        catch
        {
            return true;
        }
    }

    private void StopFps()
    {
        if (_fps is null) return;
        _fps.Dispose();
        _fps = null;
    }

    public void Dispose()
    {
        _stop.Set();
        _thread?.Join(1500);
        _thermal?.Dispose();
    }
}
