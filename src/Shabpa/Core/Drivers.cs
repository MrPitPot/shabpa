using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Text;
using Microsoft.Win32;

namespace Shabpa.Core;

public enum GpuVendor { Nvidia, Amd, Intel, Other }

/// <summary>A graphics card and the driver installed for it.</summary>
public sealed record GpuDriver(string Name, GpuVendor Vendor, string Version, string FriendlyVersion, DateTime? Date, string Provider)
{
    /// <summary>The vendor's own driver download page for this card.</summary>
    public string DownloadPage => Vendor switch
    {
        GpuVendor.Nvidia => "https://www.nvidia.com/Download/index.aspx",
        GpuVendor.Amd => "https://www.amd.com/en/support/download/drivers.html",
        GpuVendor.Intel => "https://www.intel.com/content/www/us/en/support/detect.html",
        _ => "",
    };

    public string VendorName => Vendor switch { GpuVendor.Nvidia => "NVIDIA", GpuVendor.Amd => "AMD", GpuVendor.Intel => "Intel", _ => "" };
}

/// <summary>A driver update Windows Update offers for a device on this PC.</summary>
public sealed record DriverOffer(string Title, string Manufacturer, string Class, string Version, DateTime? Date);

public static class Drivers
{
    private const string DisplayClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    internal static GpuVendor VendorOf(string text)
    {
        var t = text.ToLowerInvariant();
        if (t.Contains("nvidia")) return GpuVendor.Nvidia;
        if (t.Contains("advanced micro devices") || t.Contains("amd") || t.Contains("radeon")) return GpuVendor.Amd;
        if (t.Contains("intel")) return GpuVendor.Intel;
        return GpuVendor.Other;
    }

    /// <summary>NVIDIA's package number is the last digits of Windows' 4-part version: 32.0.15.6094 is 560.94.</summary>
    internal static string NvidiaVersion(string windowsVersion)
    {
        var parts = windowsVersion.Split('.');
        if (parts.Length != 4) return windowsVersion;
        var tail = (parts[2] + parts[3].PadLeft(4, '0'));
        if (tail.Length < 5) return windowsVersion;
        tail = tail[^5..];
        return $"{int.Parse(tail[..3], CultureInfo.InvariantCulture)}.{tail[3..]}";
    }

    /// <summary>The graphics cards with the driver version and date, read from the display-adapter registry class.</summary>
    public static List<GpuDriver> Gpus()
    {
        var list = new List<GpuDriver>();
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(DisplayClass);
            if (root is null) return list;
            foreach (var name in root.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsAsciiDigit)))
            {
                using var key = root.OpenSubKey(name);
                if (key?.GetValue("DriverDesc") is not string desc || desc.Length == 0) continue;
                if (desc.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) || desc.Contains("Remote Display", StringComparison.OrdinalIgnoreCase)) continue;
                var provider = key.GetValue("ProviderName") as string ?? "";
                var version = key.GetValue("DriverVersion") as string ?? "";
                var vendor = VendorOf(provider + " " + desc);
                DateTime? date = null;
                if (key.GetValue("DriverDate") is string d && DateTime.TryParse(d, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.None, out var parsed)) date = parsed;
                var friendly = vendor == GpuVendor.Nvidia ? NvidiaVersion(version) : version;
                list.Add(new GpuDriver(desc, vendor, version, friendly, date, provider));
            }
        }
        catch
        {
            // no registry access: the page just shows nothing for graphics
        }
        return list;
    }

    internal static List<DriverOffer> ParseOffers(string output)
    {
        var list = new List<DriverOffer>();
        foreach (var raw in output.Replace("\r", "").Split('\n'))
        {
            var p = raw.Split('|');
            if (p.Length < 6 || p[0] != "DRV") continue;
            DateTime? date = DateTime.TryParse(p[5], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
            list.Add(new DriverOffer(p[1].Trim(), p[2].Trim(), p[3].Trim(), p[4].Trim(), date));
        }
        return list;
    }

    /// <summary>Driver updates that Windows Update has found for this PC (it only offers ones that match the hardware).
    /// Empty when there are none. The search can take a minute.</summary>
    public static async Task<List<DriverOffer>> WindowsUpdateOffersAsync(CancellationToken ct = default)
    {
        const string script = "$ProgressPreference='SilentlyContinue';$ErrorActionPreference='Stop';" +
            "$s=New-Object -ComObject Microsoft.Update.Session;" +
            "$r=$s.CreateUpdateSearcher().Search(\"IsInstalled=0 and Type='Driver'\");" +
            "foreach($u in $r.Updates){ 'DRV|{0}|{1}|{2}|{3}|{4}' -f $u.Title,$u.DriverManufacturer,$u.DriverClass,$u.DriverModel,$u.DriverVerDate.ToString('yyyy-MM-dd') }";
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {Convert.ToBase64String(Encoding.Unicode.GetBytes(script))}")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8,
            },
        };
        process.Start();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cancel.CancelAfter(TimeSpan.FromMinutes(3));
        var text = await process.StandardOutput.ReadToEndAsync(cancel.Token);
        await process.WaitForExitAsync(cancel.Token);
        return ParseOffers(text);
    }
}
