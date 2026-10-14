using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Shabpa.Core;

/// <summary>What the graphics card reports right now. Anything the card or driver does not expose is null.</summary>
public sealed record GpuReading
{
    public string Name { get; init; } = "";
    public string Vendor { get; init; } = "";
    public double? TempC { get; init; }
    public double? HotspotC { get; init; }
    public double? MemTempC { get; init; }
    public double? CoreMHz { get; init; }
    public double? MemMHz { get; init; }
    public double? FanPercent { get; init; }
    public double? FanRpm { get; init; }
    public double? PowerW { get; init; }
    public double? VoltageMv { get; init; }
    public double? LoadPercent { get; init; }
    public double? VramUsedMb { get; init; }
    public double? VramTotalMb { get; init; }
}

/// <summary>A vendor's way of reading (and for AMD also tuning) the main graphics card.</summary>
public interface IGpuProvider : IDisposable
{
    string Name { get; }
    string Vendor { get; }
    GpuReading Read();
}

public static class GpuProbe
{
    /// <summary>Opens the best provider for this PC: the discrete card's vendor library when present.</summary>
    public static IGpuProvider? Open()
    {
        try
        {
            if (AmdGpu.TryOpen() is { } amd) return amd;
        }
        catch (Exception ex)
        {
            Log.Write($"amd gpu probe failed: {ex.Message}");
        }
        try
        {
            if (NvidiaGpu.TryOpen() is { } nvidia) return nvidia;
        }
        catch (Exception ex)
        {
            Log.Write($"nvidia gpu probe failed: {ex.Message}");
        }
        return null;
    }

    /// <summary>Dedicated video memory in MB for the adapter whose driver description matches, from the registry
    /// (the WMI figure is capped at 4 GB).</summary>
    public static double? VramTotalMb(string adapterName)
    {
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (classKey is null) return null;
            foreach (var name in classKey.GetSubKeyNames())
            {
                using var key = classKey.OpenSubKey(name);
                if (key?.GetValue("DriverDesc") is not string desc) continue;
                if (!desc.Equals(adapterName, StringComparison.OrdinalIgnoreCase) && !adapterName.Contains(desc, StringComparison.OrdinalIgnoreCase)) continue;
                var raw = key.GetValue("HardwareInformation.qwMemorySize");
                var bytes = raw switch
                {
                    long l => l,
                    byte[] b when b.Length >= 8 => BitConverter.ToInt64(b, 0),
                    int i => i,
                    _ => 0L,
                };
                if (bytes > 0) return bytes / 1048576.0;
            }
        }
        catch { /* registry layout differs: no figure */ }
        return null;
    }
}

// ---- AMD: ADL -------------------------------------------------------------------------------------

/// <summary>One Overdrive 8 feature as the driver describes it.</summary>
public sealed record Od8Range(int Id, int Min, int Max, int Default, int Current)
{
    public bool Adjustable => Max > Min;
}

/// <summary>Everything Overdrive 8 reports: capability bits, how many settings the driver knows, each range.</summary>
public sealed record Od8State(int Caps, int Count, IReadOnlyDictionary<int, Od8Range> Features)
{
    public bool Has(int capability) => (Caps & capability) != 0;

    public Od8Range? Get(int id) => Features.TryGetValue(id, out var range) && range.Adjustable ? range : null;
}

public sealed class AmdGpu : IGpuProvider
{
    // Overdrive 8 setting ids (adl_defines.h, enum ADLOD8SettingId).
    public const int GfxClkFmax = 0, GfxClkFmin = 1, UclkFmax = 8, PowerPercentage = 9, FanMinSpeed = 10, FanAcousticLimit = 11, FanTargetTemp = 12,
        OperatingTempMax = 13, FanZeroRpm = 15, FanCurveTemp1 = 19, FanCurveSpeed1 = 20, UclkFmin = 34, FanZeroRpmStopTemp = 35, OdVoltage = 37,
        GfxVoltageLimit = 45, GfxClkVoltage1 = 3, GfxClkVoltage2 = 5, GfxClkVoltage3 = 7;
    public const int FanCurvePoints = 5;
    /// <summary>OD8_COUNT in the SDK header this was written against.</summary>
    private const int KnownSettings = 77;

    // Overdrive 8 capability bits.
    public const int CapGfxClkLimits = 1 << 0, CapUclkMax = 1 << 2, CapPowerLimit = 1 << 3, CapFanCurve = 1 << 13, CapOdVoltage = 1 << 17,
        CapGfxVoltageLimit = 1 << 21, CapZeroRpm = 1 << 9;

    // Performance-log sensor ids (adl_defines.h, ADLSensorType).
    private const int SensorGfxClk = 1, SensorMemClk = 2, SensorTempEdge = 8, SensorTempMem = 9, SensorFanRpm = 14, SensorFanPercent = 15,
        SensorActivityGfx = 19, SensorGfxVoltage = 21, SensorAsicPower = 23, SensorTempHotspot = 27;
    private const int SensorSlots = 256;

    private delegate IntPtr AdlMalloc(int size);
    private static readonly AdlMalloc Alloc = size => Marshal.AllocHGlobal(Math.Max(size, 1));

    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL2_Main_Control_Create(AdlMalloc callback, int enumConnectedAdapters, out IntPtr context);
    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL2_Main_Control_Destroy(IntPtr context);
    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL2_Adapter_NumberOfAdapters_Get(IntPtr context, out int count);
    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL2_Adapter_AdapterInfo_Get(IntPtr context, IntPtr info, int size);
    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL2_Overdrive_Caps(IntPtr context, int adapter, out int supported, out int enabled, out int version);
    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL2_New_QueryPMLogData_Get(IntPtr context, int adapter, IntPtr output);
    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL2_Overdrive8_Init_Setting_Get(IntPtr context, int adapter, IntPtr init);
    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL2_Overdrive8_Current_Setting_Get(IntPtr context, int adapter, IntPtr current);
    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL2_Overdrive8_Setting_Set(IntPtr context, int adapter, IntPtr set, IntPtr current);

    private const int InfoSize = 1572; // sizeof(AdapterInfo) on Windows
    private const int OffIndex = 4, OffBus = 264, OffVendor = 276, OffName = 280, OffPresent = 792;

    private readonly object _gate = new();
    private IntPtr _context;
    private readonly int _adapter;
    private readonly IntPtr _sensorBuffer = Marshal.AllocHGlobal(8192);
    private double? _vram;

    public string Name { get; }
    public string Vendor => "AMD";
    public int Overdrive8Version { get; }
    /// <summary>The driver exposes Overdrive 8 (power, clocks, fan curve).</summary>
    public bool CanTune => Overdrive8Version >= 8;

    private AmdGpu(IntPtr context, int adapter, string name, int odVersion)
    {
        _context = context;
        _adapter = adapter;
        Name = name;
        Overdrive8Version = odVersion;
        _vram = GpuProbe.VramTotalMb(name);
    }

    /// <summary>Why the last <see cref="TryOpen"/> found nothing; for diagnostics.</summary>
    public static string ProbeNote { get; private set; } = "";

    public static AmdGpu? TryOpen()
    {
        ProbeNote = "";
        if (!NativeLibrary.TryLoad("atiadlxx.dll", out var lib))
        {
            ProbeNote = "atiadlxx.dll not found";
            return null;
        }
        NativeLibrary.Free(lib);
        var create = ADL2_Main_Control_Create(Alloc, 1, out var context);
        if (create != 0 || context == IntPtr.Zero)
        {
            ProbeNote = $"ADL2_Main_Control_Create returned {create}";
            return null;
        }

        var buffer = IntPtr.Zero;
        try
        {
            if (ADL2_Adapter_NumberOfAdapters_Get(context, out var count) != 0 || count <= 0)
            {
                ProbeNote = $"no adapters ({count})";
                return Fail(context);
            }
            buffer = Marshal.AllocHGlobal(InfoSize * count);
            for (var i = 0; i < InfoSize * count; i += 8) Marshal.WriteInt64(buffer, i, 0);
            var infoResult = ADL2_Adapter_AdapterInfo_Get(context, buffer, InfoSize * count);
            if (infoResult != 0)
            {
                ProbeNote = $"AdapterInfo_Get returned {infoResult}";
                return Fail(context);
            }

            // Several indexes can point at one physical card: keep the first per bus, prefer one that has Overdrive.
            var seenBus = new HashSet<int>();
            (int index, string name, int version)? best = null;
            for (var i = 0; i < count; i++)
            {
                var at = IntPtr.Add(buffer, i * InfoSize);
                var vendor = Marshal.ReadInt32(at, OffVendor);
                if (vendor is not (1002 or 0x1002) || Marshal.ReadInt32(at, OffPresent) == 0) continue; // ADL reports the vendor id in decimal
                var bus = Marshal.ReadInt32(at, OffBus);
                if (!seenBus.Add(bus)) continue;
                var index = Marshal.ReadInt32(at, OffIndex);
                var name = Marshal.PtrToStringAnsi(IntPtr.Add(at, OffName), 256)?.Split('\0')[0].Trim() ?? "AMD GPU";
                ADL2_Overdrive_Caps(context, index, out var supported, out _, out var version);
                var candidate = (index, name, supported != 0 ? version : 0);
                if (best is null || candidate.Item3 > best.Value.version) best = candidate;
            }
            if (best is null)
            {
                ProbeNote = $"{count} adapter entries, none AMD and present";
                return Fail(context);
            }
            var gpu = new AmdGpu(context, best.Value.index, best.Value.name, best.Value.version);
            context = IntPtr.Zero; // owned by the instance now
            return gpu;
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            if (context != IntPtr.Zero) ADL2_Main_Control_Destroy(context);
        }

        static AmdGpu? Fail(IntPtr ctx)
        {
            ADL2_Main_Control_Destroy(ctx);
            return null;
        }
    }

    public GpuReading Read()
    {
        lock (_gate)
        {
            if (_context == IntPtr.Zero) return new GpuReading { Name = Name, Vendor = Vendor };
            double? Sensor(int id)
            {
                var supported = Marshal.ReadInt32(_sensorBuffer, 4 + id * 8);
                return supported != 0 ? Marshal.ReadInt32(_sensorBuffer, 4 + id * 8 + 4) : null;
            }

            Marshal.WriteInt32(_sensorBuffer, 0, 4 + SensorSlots * 8);
            if (ADL2_New_QueryPMLogData_Get(_context, _adapter, _sensorBuffer) != 0)
                return new GpuReading { Name = Name, Vendor = Vendor, VramTotalMb = _vram };

            double? Valid(double? v, double min, double max) => v is { } x && x >= min && x <= max ? x : null;
            return new GpuReading
            {
                Name = Name,
                Vendor = Vendor,
                TempC = Valid(Sensor(SensorTempEdge), 1, 150),
                HotspotC = Valid(Sensor(SensorTempHotspot), 1, 150),
                MemTempC = Valid(Sensor(SensorTempMem), 1, 150),
                CoreMHz = Valid(Sensor(SensorGfxClk), 0, 6000),
                MemMHz = Valid(Sensor(SensorMemClk), 0, 6000),
                FanPercent = Valid(Sensor(SensorFanPercent), 0, 100),
                FanRpm = Valid(Sensor(SensorFanRpm), 0, 20000),
                PowerW = Valid(Sensor(SensorAsicPower), 0, 1500),
                VoltageMv = Valid(Sensor(SensorGfxVoltage), 0, 3000),
                LoadPercent = Valid(Sensor(SensorActivityGfx), 0, 100),
                VramTotalMb = _vram,
            };
        }
    }

    // ---- Overdrive 8 ----------------------------------------------------------------------------

    /// <summary>The ranges and current values of every feature the driver offers, plus the capability bits.</summary>
    public Od8State? ReadOd8()
    {
        lock (_gate)
        {
            if (_context == IntPtr.Zero || !CanTune) return null;
            var init = Marshal.AllocHGlobal(16384);
            var current = Marshal.AllocHGlobal(16384);
            try
            {
                Zero(init, 16384);
                Zero(current, 16384);
                // The driver fills as many table entries as "count" says the caller has room for.
                Marshal.WriteInt32(init, 0, KnownSettings);
                Marshal.WriteInt32(current, 0, KnownSettings);
                if (ADL2_Overdrive8_Init_Setting_Get(_context, _adapter, init) != 0) return null;
                if (ADL2_Overdrive8_Current_Setting_Get(_context, _adapter, current) != 0) return null;
                // The driver reports how many table entries it filled; fall back to the header's size if it does not.
                var reported = Marshal.ReadInt32(init, 0);
                var count = reported is > 0 and <= 256 ? reported : KnownSettings;
                var currentReported = Marshal.ReadInt32(current, 0);
                var currentCount = currentReported is > 0 and <= 256 ? currentReported : KnownSettings;
                var caps = Marshal.ReadInt32(init, 4);
                var features = new Dictionary<int, Od8Range>();
                for (var id = 0; id < count; id++)
                {
                    // ADLOD8InitSetting: count, capabilities, then {featureID, min, max, default} per entry.
                    var at = 8 + id * 16;
                    var feature = Marshal.ReadInt32(init, at);
                    var min = Marshal.ReadInt32(init, at + 4);
                    var max = Marshal.ReadInt32(init, at + 8);
                    var def = Marshal.ReadInt32(init, at + 12);
                    var value = id < currentCount ? Marshal.ReadInt32(current, 4 + id * 4) : def;
                    if (max == 0 && min == 0 && def == 0 && feature == 0 && id != 0) continue;
                    features[id] = new Od8Range(id, min, max, def, value);
                }
                return new Od8State(caps, count, features);
            }
            finally
            {
                Marshal.FreeHGlobal(init);
                Marshal.FreeHGlobal(current);
            }
        }
    }

    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int ADL2_Adapter_RegValueInt_Set(IntPtr context, int adapter, int driverPathOption, string? subKey, string keyName, int value);

    private const int OptimizedPowerMode = 36, ManualPowerMode = 3;
    private const int CapOptimizedPowerMode = 1 << 16;

    /// <summary>Writes the given feature values (each clamped to the driver's own range). Returns an error or null.
    /// Follows the vendor sample for this card family: manual mode first, and settings that belong together
    /// (min/max clock, the fan curve, min/max VRAM clock) are always written together.</summary>
    public string? SetOd8(IReadOnlyDictionary<int, int> values, bool reset = false)
    {
        lock (_gate)
        {
            if (_context == IntPtr.Zero || !CanTune) return "not-supported";
            var info = ReadOd8();
            if (info is null) return "not-readable";

            // Manual power mode, so the driver does not override what is written.
            if (info.Has(CapOptimizedPowerMode) && info.Features.TryGetValue(OptimizedPowerMode, out var mode) && mode.Current != ManualPowerMode)
            {
                var modeError = WriteTable(new Dictionary<int, (int value, bool reset)> { [OptimizedPowerMode] = (ManualPowerMode, false) });
                if (modeError is not null) return modeError;
            }

            var table = new Dictionary<int, (int value, bool reset)>();
            foreach (var (id, wanted) in values)
            {
                if (!info.Features.TryGetValue(id, out var range) || !range.Adjustable) continue;
                table[id] = (reset ? range.Default : Math.Clamp(wanted, range.Min, range.Max), reset);
            }
            if (table.Count == 0) return null;

            // Companions keep their current value but must travel with the setting that changed.
            void Companions(int first, int last)
            {
                if (!table.Keys.Any(id => id >= first && id <= last)) return;
                for (var id = first; id <= last; id++)
                    if (!table.ContainsKey(id) && info.Features.TryGetValue(id, out var r)) table[id] = (r.Current, false);
            }
            Companions(GfxClkFmax, GfxClkFmin);
            Companions(FanCurveTemp1, FanCurveSpeed1 + (FanCurvePoints - 1) * 2);
            if (table.ContainsKey(UclkFmax) && !table.ContainsKey(UclkFmin) && info.Features.TryGetValue(UclkFmin, out var vramMin)) table[UclkFmin] = (vramMin.Current, false);
            if (table.ContainsKey(UclkFmin) && !table.ContainsKey(UclkFmax) && info.Features.TryGetValue(UclkFmax, out var vramMax)) table[UclkFmax] = (vramMax.Current, false);

            var error = WriteTable(table);
            if (error is null) ADL2_Adapter_RegValueInt_Set(_context, _adapter, 1, null, "IsAutoDefault", 0);
            return error;
        }
    }

    private string? WriteTable(Dictionary<int, (int value, bool reset)> table)
    {
        var set = Marshal.AllocHGlobal(16384);
        var current = Marshal.AllocHGlobal(16384);
        try
        {
            Zero(set, 16384);
            Zero(current, 16384);
            Marshal.WriteInt32(current, 0, KnownSettings);
            // ADLOD8SetSetting: count, then {value, requested, reset} per setting id.
            Marshal.WriteInt32(set, 0, KnownSettings);
            foreach (var (id, (value, reset)) in table)
            {
                if (id < 0 || id >= KnownSettings) continue;
                var at = 4 + id * 12;
                Marshal.WriteInt32(set, at, value);
                Marshal.WriteInt32(set, at + 4, 1);
                Marshal.WriteInt32(set, at + 8, reset ? 1 : 0);
            }
            var result = ADL2_Overdrive8_Setting_Set(_context, _adapter, set, current);
            return result == 0 ? null : $"adl-{result}";
        }
        finally
        {
            Marshal.FreeHGlobal(set);
            Marshal.FreeHGlobal(current);
        }
    }

    /// <summary>Puts every given feature back to the driver's default.</summary>
    public string? ResetOd8(IEnumerable<int> ids)
    {
        var info = ReadOd8();
        if (info is null) return "not-readable";
        var defaults = ids.Where(info.Features.ContainsKey).ToDictionary(id => id, id => info.Features[id].Default);
        return defaults.Count == 0 ? null : SetOd8(defaults, reset: true);
    }

    private static void Zero(IntPtr buffer, int bytes)
    {
        for (var i = 0; i < bytes; i += 8) Marshal.WriteInt64(buffer, i, 0);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_context != IntPtr.Zero)
            {
                try { ADL2_Main_Control_Destroy(_context); }
                catch { /* library already unloaded */ }
                _context = IntPtr.Zero;
            }
            Marshal.FreeHGlobal(_sensorBuffer);
        }
    }
}

// ---- NVIDIA: NVML (read-only) ------------------------------------------------------------------

/// <summary>NVIDIA cards, read through the driver's own nvml.dll. Monitoring only; nothing is written to the card.</summary>
public sealed class NvidiaGpu : IGpuProvider
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NvmlInit();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NvmlShutdown();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NvmlGetHandle(uint index, out IntPtr device);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NvmlGetName(IntPtr device, IntPtr buffer, uint length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NvmlGetTemperature(IntPtr device, int sensor, out uint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NvmlGetUInt(IntPtr device, out uint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NvmlGetClock(IntPtr device, int type, out uint mhz);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NvmlGetUtilization(IntPtr device, out Utilization value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NvmlGetMemory(IntPtr device, out MemoryInfo value);

    [StructLayout(LayoutKind.Sequential)] private struct Utilization { public uint Gpu, Memory; }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryInfo { public ulong Total, Free, Used; }

    private readonly IntPtr _library;
    private readonly IntPtr _device;
    private readonly NvmlShutdown _shutdown;
    private readonly NvmlGetTemperature _temperature;
    private readonly NvmlGetUInt? _fan, _power;
    private readonly NvmlGetClock _clock;
    private readonly NvmlGetUtilization _utilization;
    private readonly NvmlGetMemory _memory;

    public string Name { get; }
    public string Vendor => "NVIDIA";

    private NvidiaGpu(IntPtr library, IntPtr device, string name, NvmlShutdown shutdown, NvmlGetTemperature temperature, NvmlGetUInt? fan,
        NvmlGetUInt? power, NvmlGetClock clock, NvmlGetUtilization utilization, NvmlGetMemory memory)
    {
        _library = library;
        _device = device;
        Name = name;
        _shutdown = shutdown;
        _temperature = temperature;
        _fan = fan;
        _power = power;
        _clock = clock;
        _utilization = utilization;
        _memory = memory;
    }

    private static T? Export<T>(IntPtr library, string name) where T : Delegate =>
        NativeLibrary.TryGetExport(library, name, out var address) ? Marshal.GetDelegateForFunctionPointer<T>(address) : null;

    public static NvidiaGpu? TryOpen()
    {
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var program = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        foreach (var path in new[] { Path.Combine(system, "nvml.dll"), Path.Combine(program, "NVIDIA Corporation", "NVSMI", "nvml.dll") })
        {
            if (!File.Exists(path) || !NativeLibrary.TryLoad(path, out var library)) continue;
            try
            {
                var init = Export<NvmlInit>(library, "nvmlInit_v2");
                var shutdown = Export<NvmlShutdown>(library, "nvmlShutdown");
                var handle = Export<NvmlGetHandle>(library, "nvmlDeviceGetHandleByIndex_v2");
                var name = Export<NvmlGetName>(library, "nvmlDeviceGetName");
                var temperature = Export<NvmlGetTemperature>(library, "nvmlDeviceGetTemperature");
                var clock = Export<NvmlGetClock>(library, "nvmlDeviceGetClockInfo");
                var utilization = Export<NvmlGetUtilization>(library, "nvmlDeviceGetUtilizationRates");
                var memory = Export<NvmlGetMemory>(library, "nvmlDeviceGetMemoryInfo");
                if (init is null || shutdown is null || handle is null || name is null || temperature is null || clock is null || utilization is null || memory is null
                    || init() != 0 || handle(0, out var device) != 0)
                {
                    NativeLibrary.Free(library);
                    continue;
                }
                var buffer = Marshal.AllocHGlobal(128);
                try
                {
                    Marshal.WriteInt64(buffer, 0, 0);
                    var label = name(device, buffer, 128) == 0 ? Marshal.PtrToStringAnsi(buffer) ?? "NVIDIA GPU" : "NVIDIA GPU";
                    return new NvidiaGpu(library, device, label, shutdown, temperature, Export<NvmlGetUInt>(library, "nvmlDeviceGetFanSpeed"), null,
                        clock, utilization, memory);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch
            {
                NativeLibrary.Free(library);
            }
        }
        return null;
    }

    public GpuReading Read()
    {
        double? temp = _temperature(_device, 0, out var t) == 0 ? t : null;
        double? fan = _fan is not null && _fan(_device, out var f) == 0 ? f : null;
        double? core = _clock(_device, 0, out var c) == 0 ? c : null;
        double? mem = _clock(_device, 2, out var m) == 0 ? m : null;
        double? load = _utilization(_device, out var u) == 0 ? u.Gpu : null;
        double? used = null, total = null;
        if (_memory(_device, out var info) == 0)
        {
            used = info.Used / 1048576.0;
            total = info.Total / 1048576.0;
        }
        return new GpuReading
        {
            Name = Name, Vendor = Vendor, TempC = temp, CoreMHz = core, MemMHz = mem, FanPercent = fan, LoadPercent = load,
            VramUsedMb = used, VramTotalMb = total,
        };
    }

    public void Dispose()
    {
        try { _shutdown(); }
        catch { /* driver gone */ }
    }
}
