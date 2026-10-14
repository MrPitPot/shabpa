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

public sealed class AmdGpu : IGpuProvider
{
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

    private const int InfoSize = 1572; // sizeof(AdapterInfo) on Windows
    private const int OffIndex = 4, OffBus = 264, OffVendor = 276, OffName = 280, OffPresent = 792;

    private readonly object _gate = new();
    private IntPtr _context;
    private readonly int _adapter;
    private readonly IntPtr _sensorBuffer = Marshal.AllocHGlobal(8192);
    private double? _vram;

    public string Name { get; }
    public string Vendor => "AMD";

    private AmdGpu(IntPtr context, int adapter, string name)
    {
        _context = context;
        _adapter = adapter;
        Name = name;
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
            var gpu = new AmdGpu(context, best.Value.index, best.Value.name);
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
