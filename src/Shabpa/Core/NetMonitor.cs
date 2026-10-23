using System.Net;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace Shabpa.Core;

/// <summary>Per-process network traffic from the Windows kernel's own trace events (ETW). This is the
/// only driverless way to attribute bytes to a process, and Windows only allows it for admins.</summary>
public sealed class NetMonitor : IDisposable
{
    private const string SessionName = "Shabpa-Net";

    private readonly TraceEventSession _session;

    private NetMonitor(TraceEventSession session) => _session = session;

    /// <summary>Returns null and an error when the session cannot be started (typically: not elevated).</summary>
    public static NetMonitor? TryStart(TrafficMeter meter, out string? error)
    {
        error = null;
        if (!Elevation.IsElevated)
        {
            error = "needs-admin";
            return null;
        }

        try
        {
            // A session left behind by a crashed run is replaced rather than joined.
            var session = new TraceEventSession(SessionName) { StopOnDispose = true };
            session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);

            var kernel = session.Source.Kernel;
            kernel.TcpIpRecv += d => Add(meter, d.ProcessID, d.size, false, d.saddr, d.daddr);
            kernel.TcpIpSend += d => Add(meter, d.ProcessID, d.size, true, d.saddr, d.daddr);
            kernel.TcpIpRecvIPV6 += d => Add(meter, d.ProcessID, d.size, false, d.saddr, d.daddr);
            kernel.TcpIpSendIPV6 += d => Add(meter, d.ProcessID, d.size, true, d.saddr, d.daddr);
            kernel.UdpIpRecv += d => Add(meter, d.ProcessID, d.size, false, d.saddr, d.daddr);
            kernel.UdpIpSend += d => Add(meter, d.ProcessID, d.size, true, d.saddr, d.daddr);
            kernel.UdpIpRecvIPV6 += d => Add(meter, d.ProcessID, d.size, false, d.saddr, d.daddr);
            kernel.UdpIpSendIPV6 += d => Add(meter, d.ProcessID, d.size, true, d.saddr, d.daddr);

            var monitor = new NetMonitor(session);
            new Thread(() =>
            {
                try { session.Source.Process(); }
                catch (Exception ex) { Log.Write($"net trace stopped: {ex.Message}"); }
            }) { IsBackground = true, Name = "net-trace", Priority = ThreadPriority.BelowNormal }.Start();
            return monitor;
        }
        catch (Exception ex)
        {
            Log.Write($"net trace failed: {ex}");
            error = ex.Message;
            return null;
        }
    }

    private static void Add(TrafficMeter meter, int pid, int size, bool upload, IPAddress source, IPAddress destination) =>
        meter.Record(pid, size, upload, NeverLeavesThisPc(upload ? destination : source));

    /// <summary>True for the far end of a connection that stays inside this PC: loopback, link-local, and 198.18.0.0/15, the
    /// range VPN/proxy clients (Clash, Mihomo, sing-box tunnels) use. Behind such a client an app talks to the client, and the
    /// client's own connection then carries the same bytes to the internet; that second leg is the real use of the line.</summary>
    internal static bool NeverLeavesThisPc(IPAddress remote)
    {
        if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();
        if (IPAddress.IsLoopback(remote) || remote.IsIPv6LinkLocal) return true;
        if (remote.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        var b = remote.GetAddressBytes();
        return (b[0] == 169 && b[1] == 254) || (b[0] == 198 && (b[1] == 18 || b[1] == 19));
    }

    public void Dispose()
    {
        try { _session.Dispose(); }
        catch { /* already stopped */ }
    }
}
