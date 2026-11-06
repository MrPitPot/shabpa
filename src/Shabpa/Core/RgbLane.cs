using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace Shabpa.Core;

/// <summary>One device's own road to the lighting server: its own connection and its own thread. The server handles each connection on a thread of its own, so a device that
/// takes tens of milliseconds to show a frame (memory and motherboards over a slow bus) no longer holds up the fans, strips and keyboards that take one: every device is given the
/// newest frame the moment it can take it, so they change together. A lane keeps only the newest frame offered (an old one is never worth sending late), measures how long the
/// device takes (<see cref="CostMs"/>, which the caller makes up for by showing the effect that far ahead) and never speaks faster than the device can listen.</summary>
internal sealed class RgbLane : IDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly uint _device;
    private readonly Thread _thread;
    private readonly object _gate = new();
    private readonly ManualResetEventSlim _wake = new(false);
    private Func<double, byte[]?>? _pending;                   // draws the colours (R, G, B per LED) for the moment of sending; the argument is how many seconds old the offer is
    private long _offeredAt;
    private bool _force, _busy;
    private int _minIntervalMs;
    private volatile bool _stop, _broken;
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private bool _controlled;
    private byte[]? _lastColours;
    private long _lastAt;
    private volatile int _cost10;                              // the measured delay in tenths of a millisecond (read from other threads)

    public RgbLane(string host, int port, int device)
    {
        _host = host;
        _port = port;
        _device = (uint)device;
        _thread = new Thread(Run) { IsBackground = true, Name = "rgb-lane-" + device };
        _thread.Start();
    }

    /// <summary>How long the server takes to put one frame on this device (ms, smoothed).</summary>
    public double CostMs => _cost10 / 10.0;

    /// <summary>True after the last attempt to talk to the server failed (it is gone or refused); the caller then connects again from the start.</summary>
    public bool Broken => _broken;

    /// <summary>Nothing waiting and nothing being sent.</summary>
    public bool Idle { get { lock (_gate) return _pending is null && !_busy; } }

    /// <summary>Gives the lane the newest frame: a function that draws it (R, G, B per LED, or null for nothing) given how old the offer is when the lane gets to it, so a frame that
    /// had to wait for the device is drawn for the moment it really goes out. It replaces any older offer that has not gone out yet.</summary>
    public void Offer(Func<double, byte[]?> render, bool force, int minIntervalMs)
    {
        lock (_gate)
        {
            _pending = render;
            _offeredAt = Environment.TickCount64;
            _force |= force;
            _minIntervalMs = minIntervalMs;
            _busy = true;                                      // from now until the lane has looked at it, the lane is not idle
        }
        _wake.Set();
    }

    private void Run()
    {
        while (!_stop)
        {
            _wake.Wait();
            if (_stop) return;
            Func<double, byte[]?>? frame;
            bool force;
            int minInterval;
            long offeredAt;
            lock (_gate)
            {
                frame = _pending;
                offeredAt = _offeredAt;
                force = _force;
                minInterval = _minIntervalMs;
                _pending = null;
                _force = false;
                _wake.Reset();
            }
            if (frame is null) { lock (_gate) _busy = _pending is not null; continue; }
            try
            {
                Send(frame(Math.Max(0, (Environment.TickCount64 - offeredAt) / 1000.0)), force, minInterval);
                _broken = false;
            }
            catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException or InvalidOperationException or InvalidDataException or AggregateException)
            {
                _broken = true;
                Drop();
                if (!_stop) Thread.Sleep(250);                  // the caller sees Broken, closes everything and connects anew; until then do not spin
            }
            finally
            {
                lock (_gate) _busy = _pending is not null;
            }
        }
    }

    private void Send(byte[]? colours, bool force, int minIntervalMs)
    {
        if (colours is not { Length: > 0 }) return;
        var now = Environment.TickCount64;
        if (!force && _lastColours is not null)
        {
            var age = now - _lastAt;
            if (age < Math.Max(minIntervalMs, Math.Min(400, CostMs * 1.2))) return;                 // never faster than the device can take them
            if (age < 2000 && _lastColours.AsSpan().SequenceEqual(colours)) return;                // unchanged (but every two seconds anyway, so none falls back to its own program)
        }
        if (_stream is null) Open();
        var leds = colours.Length / 3;
        if (!_controlled) { OpenRgbClient.WritePacket(_stream!, _device, OpenRgbClient.SetCustomMode, []); _controlled = true; }
        var payload = new byte[4 + 2 + leds * 4];
        BitConverter.GetBytes(payload.Length).CopyTo(payload, 0);
        BitConverter.GetBytes((ushort)leds).CopyTo(payload, 4);
        for (var i = 0; i < leds; i++)
        {
            payload[6 + i * 4] = colours[i * 3];
            payload[7 + i * 4] = colours[i * 3 + 1];
            payload[8 + i * 4] = colours[i * 3 + 2];
        }
        // A frame is sent, then a question the server only answers once it has finished with that frame (it handles one connection's requests in order). Waiting for the answer means frames
        // cannot pile up behind a slow device, and the time it takes is how slow the device is.
        var timer = Stopwatch.StartNew();
        OpenRgbClient.WritePacket(_stream!, _device, OpenRgbClient.UpdateLeds, payload);
        OpenRgbClient.WritePacket(_stream!, 0, OpenRgbClient.MagicRequestControllerCount, []);
        OpenRgbClient.ReadAnswer(_stream!, OpenRgbClient.MagicRequestControllerCount);
        var took = timer.Elapsed.TotalMilliseconds;
        _cost10 = (int)Math.Round((_cost10 == 0 ? took : _cost10 / 10.0 * 0.7 + took * 0.3) * 10);
        _lastColours = colours;
        _lastAt = Environment.TickCount64;
    }

    private void Open()
    {
        var tcp = new TcpClient { NoDelay = true, ReceiveTimeout = 2000, SendTimeout = 2000 };
        try
        {
            var connect = tcp.ConnectAsync(_host, _port);
            if (!connect.Wait(1000) || !tcp.Connected)
            {
                OpenRgbHost.Observe(connect);
                throw new IOException("no answer");
            }
            var stream = tcp.GetStream();
            OpenRgbClient.WritePacket(stream, 0, OpenRgbClient.SetClientName, Encoding.ASCII.GetBytes("Shabpa\0"));
            OpenRgbClient.WritePacket(stream, 0, OpenRgbClient.RequestProtocolVersion, BitConverter.GetBytes(3u));
            OpenRgbClient.ReadAnswer(stream, OpenRgbClient.RequestProtocolVersion);
            _tcp = tcp;
            _stream = stream;
            _controlled = false;
            _lastColours = null;
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    private void Drop()
    {
        try { _stream?.Dispose(); } catch (IOException) { }
        try { _tcp?.Dispose(); } catch (SocketException) { }
        _stream = null;
        _tcp = null;
        _controlled = false;
        _lastColours = null;
    }

    public void Dispose()
    {
        _stop = true;
        _wake.Set();
        Drop();
        _thread.Join(800);
    }
}
