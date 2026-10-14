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

    private static void Add(TrafficMeter meter, int pid, int size, bool upload, IPAddress source, IPAddress destination)
    {
        // Loopback is counted on purpose: behind a local proxy or VPN client, an app's only "network"
        // is 127.0.0.1, and that is the traffic the user wants to see and limit.
        _ = source;
        _ = destination;
        meter.Record(pid, size, upload);
    }

    public void Dispose()
    {
        try { _session.Dispose(); }
        catch { /* already stopped */ }
    }
}
