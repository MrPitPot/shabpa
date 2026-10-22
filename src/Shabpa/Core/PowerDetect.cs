using System.Management;

namespace Shabpa.Core;

/// <summary>Reads this PC's hardware (WMI and the graphics driver) into a <see cref="HardwareSnapshot"/>. Slow and blocking: it
/// only runs on a worker thread, never on the window's thread.</summary>
public static class PowerDetect
{
    public readonly record struct Video(string Name, double? RamBytes);

    /// <summary>SMBIOS memory type code to a name.</summary>
    public static string RamTypeOf(int smbios) => smbios switch
    {
        20 => "DDR", 21 => "DDR2", 24 => "DDR3", 26 => "DDR4", 34 => "DDR5", _ => "",
    };

    /// <summary>The chassis types Windows reports for portable machines (portable, laptop, notebook, handheld, sub-notebook, tablet, convertible, detachable).</summary>
    public static bool IsLaptopChassis(IEnumerable<int> chassisTypes) => chassisTypes.Any(t => t is 8 or 9 or 10 or 11 or 14 or 30 or 31 or 32);

    /// <summary>True for a drive that counts toward the PC's own power: not a USB stick or an external box.</summary>
    public static bool IsInternalDisk(string interfaceType, string model, string mediaType) =>
        !interfaceType.Contains("USB", StringComparison.OrdinalIgnoreCase) && !interfaceType.Contains("1394", StringComparison.OrdinalIgnoreCase)
        && !mediaType.Contains("Removable", StringComparison.OrdinalIgnoreCase) && !mediaType.Contains("External", StringComparison.OrdinalIgnoreCase)
        && !model.Contains("Virtual", StringComparison.OrdinalIgnoreCase);

    /// <summary>SSD, HDD or unknown (null) from the Storage-subsystem media type (3 = HDD, 4 = SSD, 5 = SCM) or, failing that, from the model text.</summary>
    public static bool? IsSsd(int storageMediaType, string model)
    {
        if (storageMediaType == 4 || storageMediaType == 5) return true;
        if (storageMediaType == 3) return false;
        if (model.Contains("SSD", StringComparison.OrdinalIgnoreCase) || model.Contains("NVMe", StringComparison.OrdinalIgnoreCase)
            || model.Contains("Samsung SSD", StringComparison.OrdinalIgnoreCase)) return true;
        return null;
    }

    /// <summary>The card to describe: a real graphics card over a processor's built-in graphics.</summary>
    public static Video? PickGpu(IEnumerable<Video> cards)
    {
        var list = cards.Where(c => c.Name.Length > 0 && !c.Name.Contains("Basic", StringComparison.OrdinalIgnoreCase)
                                    && !c.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase) && !c.Name.Contains("Remote", StringComparison.OrdinalIgnoreCase)).ToList();
        if (list.Count == 0) return null;
        return list.OrderBy(c => PowerEstimator.IsIntegratedGpu(c.Name) ? 1 : 0).ThenByDescending(c => PowerEstimator.GpuTable(c.Name) ?? 0)
            .ThenByDescending(c => c.RamBytes ?? 0).First();
    }

    public static HardwareSnapshot Read()
    {
        string cpuName = "";
        var cores = 0;
        var cards = new List<Video>();
        var ram = new List<(int type, double bytes)>();
        var chassis = new List<int>();
        var battery = false;
        int ssd = 0, hdd = 0, unknownDisks = 0;

        Each("SELECT Name, NumberOfCores FROM Win32_Processor", o =>
        {
            if (cpuName.Length == 0) cpuName = (o["Name"] as string ?? "").Trim();
            cores += Convert.ToInt32(o["NumberOfCores"] ?? 0);
        });
        Each("SELECT Name, AdapterRAM FROM Win32_VideoController", o => cards.Add(new Video((o["Name"] as string ?? "").Trim(), o["AdapterRAM"] is { } r ? Convert.ToDouble(r) : null)));
        Each("SELECT SMBIOSMemoryType, Capacity FROM Win32_PhysicalMemory", o => ram.Add((Convert.ToInt32(o["SMBIOSMemoryType"] ?? 0), Convert.ToDouble(o["Capacity"] ?? 0))));
        Each("SELECT ChassisTypes FROM Win32_SystemEnclosure", o =>
        {
            if (o["ChassisTypes"] is ushort[] types) chassis.AddRange(types.Select(t => (int)t));
        });
        Each("SELECT Name FROM Win32_Battery", _ => battery = true);

        // media type per physical disk from the Storage subsystem (names match Win32_DiskDrive by model)
        var media = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        Each("SELECT FriendlyName, MediaType FROM MSFT_PhysicalDisk", o =>
        {
            if (o["FriendlyName"] is string n) media[n.Trim()] = Convert.ToInt32(o["MediaType"] ?? 0);
        }, @"root\Microsoft\Windows\Storage");
        Each("SELECT Model, InterfaceType, MediaType FROM Win32_DiskDrive", o =>
        {
            var model = (o["Model"] as string ?? "").Trim();
            if (!IsInternalDisk(o["InterfaceType"] as string ?? "", model, o["MediaType"] as string ?? "")) return;
            switch (IsSsd(media.GetValueOrDefault(model), model))
            {
                case true: ssd++; break;
                case false: hdd++; break;
                default: unknownDisks++; break;
            }
        });
        ssd += unknownDisks; // an unlabelled modern drive is far more likely an SSD

        var pick = PickGpu(cards);
        GpuReading? driver = null;
        try { driver = GpuProbe.CardInfo(3000); }
        catch (Exception ex) { Log.Write($"power detect: gpu driver read failed: {ex.Message}"); }

        double? vram = driver?.VramTotalMb;
        if (vram is null && pick is { } p) vram = GpuProbe.VramTotalMb(p.Name) ?? (p.RamBytes is > 0 ? p.RamBytes / 1048576.0 : null);
        // the driver names the card more exactly than WMI, but only when it is the card we picked
        var gpuName = pick?.Name ?? "";
        var limit = driver?.PowerLimitW;
        if (driver is { Name.Length: > 0 } && (gpuName.Length == 0 || SameFamily(gpuName, driver.Name))) gpuName = driver.Name; else limit = null;

        var ramTypes = ram.Select(r => RamTypeOf(r.type)).Where(t => t.Length > 0).GroupBy(t => t).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? "";
        var monitors = 1;
        try { monitors = Math.Max(1, System.Windows.Forms.Screen.AllScreens.Length); }
        catch { /* no display session */ }

        return new HardwareSnapshot
        {
            CpuName = cpuName, CpuCores = cores, GpuName = gpuName, GpuVendor = driver?.Vendor ?? "", GpuLimitWatts = limit, GpuVramMb = vram,
            RamSticks = ram.Count, RamGb = ram.Sum(r => r.bytes) / 1073741824.0, RamType = ramTypes,
            Ssd = ssd, Hdd = hdd, Monitors = monitors, Laptop = battery || IsLaptopChassis(chassis),
        };
    }

    /// <summary>Same card by the model digits (WMI says "NVIDIA GeForce RTX 4070", NVML says "NVIDIA GeForce RTX 4070").</summary>
    public static bool SameFamily(string a, string b)
    {
        static string Digits(string s) => new(s.Where(char.IsDigit).ToArray());
        var da = Digits(a);
        var db = Digits(b);
        return da.Length == 0 || db.Length == 0 || da == db;
    }

    private static void Each(string query, Action<ManagementBaseObject> row, string scope = @"root\cimv2")
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, query) { Options = { Timeout = TimeSpan.FromSeconds(8) } };
            foreach (var o in searcher.Get())
            {
                using (o)
                {
                    try { row(o); }
                    catch (Exception ex) { Log.Write($"power detect row: {ex.Message}"); }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Write($"power detect '{query[..Math.Min(40, query.Length)]}': {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Detects and estimates on the thread pool.</summary>
    public static Task<PowerEstimateResult> EstimateAsync() => Task.Run(() => PowerEstimator.Estimate(Read()));
}
