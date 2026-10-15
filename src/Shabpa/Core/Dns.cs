using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Win32;

namespace Shabpa.Core;

/// <summary>One DNS server pair the user can switch to.</summary>
public sealed class DnsEntry
{
    public string Name { get; set; } = "";
    public string Primary { get; set; } = "";
    public string Secondary { get; set; } = "";
    /// <summary>"iran" (bypass / local), "global" (public resolvers), "secure" (filtering), "custom".</summary>
    public string Group { get; set; } = "custom";

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsCustom => Group == "custom";
}

/// <summary>The ready-made list: resolvers known for working from Iran, sanction-bypass resolvers, and the big public ones.</summary>
public static class DnsCatalog
{
    private static DnsEntry E(string name, string a, string b, string group) => new() { Name = name, Primary = a, Secondary = b, Group = group };

    public static readonly IReadOnlyList<DnsEntry> All =
    [
        E("Shecan", "178.22.122.100", "185.51.200.2", "iran"),
        E("Electro", "78.157.42.100", "78.157.42.101", "iran"),
        E("Radar Game", "10.202.10.10", "10.202.10.11", "iran"),
        E("403.online", "10.202.10.202", "10.202.10.102", "iran"),
        E("Begzar", "185.55.226.26", "185.55.225.25", "iran"),
        E("Pishgaman", "5.202.100.100", "5.202.100.101", "iran"),
        E("Shatel", "85.15.1.14", "85.15.1.15", "iran"),
        E("TCI (Mokhaberat)", "217.218.127.127", "217.218.155.155", "iran"),

        E("Google Public DNS", "8.8.8.8", "8.8.4.4", "global"),
        E("Cloudflare", "1.1.1.1", "1.0.0.1", "global"),
        E("Quad9", "9.9.9.9", "149.112.112.112", "global"),
        E("Quad9 (no filtering)", "9.9.9.10", "149.112.112.10", "global"),
        E("OpenDNS", "208.67.222.222", "208.67.220.220", "global"),
        E("Control D", "76.76.2.0", "76.76.10.0", "global"),
        E("Yandex", "77.88.8.8", "77.88.8.1", "global"),
        E("Level 3 (A)", "4.2.2.1", "4.2.2.2", "global"),
        E("Level 3 (B)", "4.2.2.3", "4.2.2.4", "global"),
        E("Verisign", "64.6.64.6", "64.6.65.6", "global"),
        E("Neustar", "156.154.70.1", "156.154.71.1", "global"),
        E("Comodo Secure", "8.26.56.26", "8.20.247.20", "global"),
        E("DNS.WATCH", "84.200.69.80", "84.200.70.40", "global"),

        E("Cloudflare (malware blocked)", "1.1.1.2", "1.0.0.2", "secure"),
        E("Cloudflare (family)", "1.1.1.3", "1.0.0.3", "secure"),
        E("AdGuard (ads blocked)", "94.140.14.14", "94.140.15.15", "secure"),
        E("AdGuard (no filtering)", "94.140.14.140", "94.140.14.141", "secure"),
        E("CleanBrowsing (security)", "185.228.168.9", "185.228.169.9", "secure"),
        E("OpenDNS FamilyShield", "208.67.222.123", "208.67.220.123", "secure"),
        E("Norton ConnectSafe", "199.85.126.10", "199.85.127.10", "secure"),
    ];

    public static readonly (string key, string fa, string en)[] Groups =
    [
        ("all", "همه", "All"), ("iran", "ایران و دور زدن تحریم", "Iran / bypass"), ("global", "عمومی", "Public"),
        ("secure", "امنیتی و فیلتر", "Secure / filtered"), ("custom", "من اضافه کردم", "Mine"),
    ];

    public static bool TryParseIPv4(string text, out IPAddress address) =>
        IPAddress.TryParse(text.Trim(), out address!) && address.AddressFamily == AddressFamily.InterNetwork
        && text.Trim().Count(c => c == '.') == 3;
}

/// <summary>Measures how fast a DNS server answers a real query.</summary>
public static class DnsProbe
{
    /// <summary>A minimal "A record for example.com" question.</summary>
    internal static byte[] BuildQuery(ushort id, string host)
    {
        var bytes = new List<byte> { (byte)(id >> 8), (byte)id, 0x01, 0x00, 0x00, 0x01, 0, 0, 0, 0, 0, 0 };
        foreach (var label in host.Split('.'))
        {
            bytes.Add((byte)label.Length);
            bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(label));
        }
        bytes.AddRange([0, 0x00, 0x01, 0x00, 0x01]);
        return [.. bytes];
    }

    /// <summary>True when <paramref name="reply"/> answers query <paramref name="id"/> with a normal response.</summary>
    internal static bool IsAnswer(byte[] reply, int length, ushort id) =>
        length >= 12 && reply[0] == (byte)(id >> 8) && reply[1] == (byte)id && (reply[2] & 0x80) != 0 && (reply[3] & 0x0F) is 0 or 3;

    /// <summary>Round-trip time in ms, or null when the server did not answer in time.</summary>
    public static async Task<double?> PingAsync(string server, int timeoutMs = 1500, CancellationToken ct = default)
    {
        if (!IPAddress.TryParse(server, out var ip)) return null;
        try
        {
            using var udp = new UdpClient(ip.AddressFamily);
            udp.Connect(ip, 53);
            double? best = null;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var id = (ushort)Random.Shared.Next(1, 65535);
                var query = BuildQuery(id, "www.google.com");
                var clock = Stopwatch.StartNew();
                await udp.SendAsync(query, ct).ConfigureAwait(false);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeoutMs);
                try
                {
                    var result = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
                    if (IsAnswer(result.Buffer, result.Buffer.Length, id))
                    {
                        var ms = clock.Elapsed.TotalMilliseconds;
                        best = best is null ? ms : Math.Min(best.Value, ms);
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // timed out; the second attempt may still answer
                }
            }
            return best;
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            return null;
        }
    }
}

/// <summary>A network adapter whose DNS can be changed.</summary>
public sealed record DnsAdapter(string Name, int Index, string Id, string[] Servers, bool IsAutomatic, bool IsLikelyMain);

/// <summary>Reads and sets the DNS servers of a network adapter (IPv4). Needs administrator rights to change.</summary>
public static class DnsConfig
{
    public static List<DnsAdapter> Adapters()
    {
        var list = new List<DnsAdapter>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            IPInterfaceProperties props;
            IPv4InterfaceProperties? v4;
            try
            {
                props = nic.GetIPProperties();
                v4 = props.GetIPv4Properties();
            }
            catch (NetworkInformationException)
            {
                continue; // an adapter without IPv4 (some tunnels, IPv6-only)
            }
            if (v4 is null) continue;
            var servers = props.DnsAddresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(a => a.ToString()).ToArray();
            var hasGateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
            var virt = nic.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) || nic.Description.Contains("TAP", StringComparison.OrdinalIgnoreCase)
                       || nic.Description.Contains("Wintun", StringComparison.OrdinalIgnoreCase) || nic.Description.Contains("VPN", StringComparison.OrdinalIgnoreCase)
                       || nic.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) || nic.Description.Contains("VMware", StringComparison.OrdinalIgnoreCase);
            list.Add(new DnsAdapter(nic.Name, v4.Index, nic.Id, servers, !IsStatic(nic.Id), hasGateway && !virt));
        }
        return [.. list.OrderByDescending(a => a.IsLikelyMain).ThenBy(a => a.Name)];
    }

    /// <summary>A manually set DNS lives in the adapter's "NameServer" registry value; empty means DHCP supplies it.</summary>
    private static bool IsStatic(string guid)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{guid}");
            return key?.GetValue("NameServer") is string s && s.Trim().Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Sets the adapter's DNS to the given servers, or back to automatic when none are given.
    /// Returns an error text, or null on success (the result is read back from Windows).</summary>
    public static string? Apply(DnsAdapter adapter, params string[] servers)
    {
        if (!Elevation.IsElevated) return "admin";
        var wanted = servers.Where(s => s.Length > 0).ToArray();
        foreach (var s in wanted)
            if (!DnsCatalog.TryParseIPv4(s, out _)) return "invalid";

        string? error;
        if (wanted.Length == 0)
            error = Netsh($"interface ipv4 set dnsservers name={adapter.Index} source=dhcp");
        else
        {
            error = Netsh($"interface ipv4 set dnsservers name={adapter.Index} source=static address={wanted[0]} register=primary validate=no");
            for (var i = 1; error is null && i < wanted.Length; i++)
                error = Netsh($"interface ipv4 add dnsservers name={adapter.Index} address={wanted[i]} index={i + 1} validate=no");
        }
        if (error is not null) return error;

        Netsh("interface ip delete arpcache", ignore: true);
        Run("ipconfig.exe", "/flushdns");

        // Trust what Windows now says, not what was asked for.
        var now = Adapters().FirstOrDefault(a => a.Index == adapter.Index);
        if (now is null) return "readback";
        if (wanted.Length == 0) return now.IsAutomatic ? null : "readback";
        return wanted.All(w => now.Servers.Contains(w)) ? null : "readback";
    }

    private static string? Netsh(string args, bool ignore = false)
    {
        var (code, output) = Run("netsh.exe", args);
        return code == 0 || ignore ? null : (output.Length > 0 ? output : $"netsh exit {code}");
    }

    private static (int code, string output) Run(string file, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(file, args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            })!;
            var text = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            if (!p.WaitForExit(15000))
            {
                try { p.Kill(); } catch { /* already gone */ }
                return (-1, "timeout");
            }
            return (p.ExitCode, text.Trim());
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
