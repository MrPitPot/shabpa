using LibreHardwareMonitor.Hardware;

namespace Shabpa.Core;

/// <summary>CPU temperature and CPU fan readings from the motherboard's sensor chip. This is the optional
/// "advanced sensors" module: it needs administrator rights and a hardware access driver (PawnIO) that
/// the user installed themselves; without both it simply reports nothing.</summary>
public sealed class BoardSensors : IDisposable
{
    public readonly record struct Reading(double? CpuTempC, double? FanRpm, double? FanPercent, GpuReading? Gpu = null);

    private Computer? _computer;
    private bool _failed;

    /// <summary>The graphics card figures from the last read.</summary>
    public GpuReading? LastGpu { get; set; }

    /// <summary>Why nothing is available: empty while it works or has not been tried.</summary>
    public string Problem { get; private set; } = "";

    public Reading Read()
    {
        if (_failed) return default;
        try
        {
            if (!Elevation.IsElevated)
            {
                _failed = true;
                Problem = "admin";
                return default;
            }
            if (_computer is null)
            {
                _computer = new Computer { IsCpuEnabled = true, IsMotherboardEnabled = true, IsGpuEnabled = true };
                _computer.Open();
            }

            double? temp = null, rpm = null, percent = null;
            double? coreMax = null, package = null;
            foreach (var hardware in _computer.Hardware)
                Visit(hardware, ref coreMax, ref package, ref rpm, ref percent);
            temp = package ?? coreMax;
            var gpu = ReadGpu(_computer);
            if (temp is null && rpm is null && percent is null && gpu is null)
            {
                Problem = "nodata";
                return default;
            }
            Problem = "";
            return new Reading(temp, rpm, percent, gpu);
        }
        catch (Exception ex)
        {
            _failed = true;
            Problem = "error";
            Log.Write($"board sensors: {ex.Message}");
            return default;
        }
    }

    /// <summary>Whatever the main graphics card (any vendor) reports; the discrete card wins over integrated graphics.</summary>
    private static GpuReading? ReadGpu(Computer computer)
    {
        var card = computer.Hardware
            .Where(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
            .OrderBy(h => h.HardwareType == HardwareType.GpuIntel ? 1 : 0).FirstOrDefault();
        if (card is null) return null;
        card.Update();

        double? Pick(SensorType type, params string[] names)
        {
            foreach (var name in names)
                foreach (var s in card.Sensors)
                    if (s.SensorType == type && s.Value is { } v && s.Name.Contains(name, StringComparison.OrdinalIgnoreCase)) return v;
            return null;
        }

        var memory = Pick(SensorType.Temperature, "Memory Junction", "Memory");
        return new GpuReading
        {
            TempC = Pick(SensorType.Temperature, "GPU Core", "GPU"),
            HotspotC = Pick(SensorType.Temperature, "Hot Spot", "Hotspot"),
            MemTempC = memory,
            CoreMHz = Pick(SensorType.Clock, "GPU Core", "Core"),
            MemMHz = Pick(SensorType.Clock, "GPU Memory", "Memory"),
            FanRpm = Pick(SensorType.Fan, "GPU Fan", "Fan"),
            FanPercent = Pick(SensorType.Control, "GPU Fan", "Fan"),
            PowerW = Pick(SensorType.Power, "GPU Package", "GPU Core", "Package", "Total"),
            VoltageMv = Pick(SensorType.Voltage, "GPU Core", "Core") is { } volt ? volt * 1000 : null,
            LoadPercent = Pick(SensorType.Load, "GPU Core", "D3D 3D"),
        };
    }

    private static void Visit(IHardware hardware, ref double? coreMax, ref double? package, ref double? rpm, ref double? percent)
    {
        hardware.Update();
        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.Value is not { } value) continue;
            var name = sensor.Name;
            switch (sensor.SensorType)
            {
                case SensorType.Temperature when hardware.HardwareType == HardwareType.Cpu:
                    if (name.Contains("Package", StringComparison.OrdinalIgnoreCase) || name == "CPU Total") package = value;
                    else if (name.Contains("Core Max", StringComparison.OrdinalIgnoreCase) || name == "Core (Tctl/Tdie)" || name.StartsWith("CPU Core", StringComparison.Ordinal))
                        coreMax ??= value;
                    break;
                case SensorType.Fan when name.Contains("CPU", StringComparison.OrdinalIgnoreCase):
                    rpm ??= value;
                    break;
                case SensorType.Control when name.Contains("CPU", StringComparison.OrdinalIgnoreCase):
                    percent ??= value;
                    break;
            }
        }
        foreach (var sub in hardware.SubHardware) Visit(sub, ref coreMax, ref package, ref rpm, ref percent);
    }

    public void Dispose()
    {
        try { _computer?.Close(); } catch { /* driver already gone */ }
        _computer = null;
    }
}
