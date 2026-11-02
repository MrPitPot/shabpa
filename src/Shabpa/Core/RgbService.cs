namespace Shabpa.Core;

/// <summary>Why the lights do not follow Shabpa, in plain terms (shown on the page and used by the owl).</summary>
public enum RgbLink
{
    /// <summary>Lighting is switched off in Shabpa.</summary>
    Off,
    /// <summary>Trying to reach the lighting server.</summary>
    Connecting,
    /// <summary>Connected and sending.</summary>
    Sending,
    /// <summary>Nothing answers on the lighting server's port (OpenRGB is not running or its server is off).</summary>
    NoServer,
    /// <summary>The server answers but knows no light.</summary>
    NoDevices,
}

/// <summary>Runs the lighting: works out the frame of every zone about 30 times a second from the page's settings and sends it to the lighting server.
/// The loop only runs while the preview is open or while the lights are switched on, so a PC that never uses lighting pays nothing.</summary>
public sealed class RgbService : IDisposable
{
    private readonly Func<Settings> _settings;
    private readonly Func<double?> _cpuTemp;
    private readonly Func<double> _cpuLoad;
    private readonly object _gate = new();
    private readonly OpenRgbClient _client = new();
    private CancellationTokenSource? _cts;
    private int _previewUsers;
    private double _smoothedMusic;
    private long _nextConnect;
    private long _linkSince = Environment.TickCount64;
    private readonly Dictionary<string, Rgb[]> _frames = [];
    private RgbLink _link = RgbLink.Off;
    private string _lastError = "";

    public RgbService(Func<Settings> settings, Func<double?> cpuTemp, Func<double> cpuLoad)
    {
        _settings = settings;
        _cpuTemp = cpuTemp;
        _cpuLoad = cpuLoad;
    }

    /// <summary>Raised on a worker thread after each frame (the page copies what it needs and marshals itself).</summary>
    public event Action? FrameReady;

    /// <summary>Raised when <see cref="Link"/> changes.</summary>
    public event Action? LinkChanged;

    public RgbLink Link => _link;
    public string LastError => _lastError;
    public IReadOnlyList<RgbDevice> Devices => _client.Devices;

    /// <summary>How long (ms) the link has been in its current state.</summary>
    public long LinkAgeMs => Environment.TickCount64 - _linkSince;

    /// <summary>The newest frame of a zone (a copy), or an empty array.</summary>
    public Rgb[] FrameOf(string zone)
    {
        lock (_gate) return _frames.TryGetValue(zone, out var f) ? [.. f] : [];
    }

    /// <summary>The page is open and wants the preview moving. Pair every call with <see cref="PreviewStop"/>.</summary>
    public void PreviewStart()
    {
        Interlocked.Increment(ref _previewUsers);
        EnsureRunning();
    }

    public void PreviewStop() => Interlocked.Decrement(ref _previewUsers);

    /// <summary>Settings changed (switch, effect, address): start or stop the loop as needed.</summary>
    public void Refresh() => EnsureRunning();

    private bool WantsHardware => _settings().Rgb.Enabled;

    private void EnsureRunning()
    {
        lock (_gate)
        {
            if (_cts is not null || (!WantsHardware && _previewUsers <= 0)) return;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _ = Task.Run(() => Loop(token), token);
        }
    }

    private void SetLink(RgbLink link, string error = "")
    {
        _lastError = error;
        if (link == _link) return;
        _link = link;
        _linkSince = Environment.TickCount64;
        LinkChanged?.Invoke();
    }

    /// <summary>How the page's settings become the look of one zone.</summary>
    public static RgbLook LookOf(RgbSettings rgb, string zone)
    {
        var s = rgb.For(zone);
        var speed = Math.Pow(2, (Math.Clamp(s.Speed, 1, 10) - 5) / 2.5);
        var colour = Rgb.Parse(s.Color, new Rgb(255, 45, 85));
        var brightness = Math.Clamp(s.Brightness, 0, 100) / 100.0;
        if (s.Effect.StartsWith("custom:", StringComparison.Ordinal) && rgb.Custom.FirstOrDefault(c => c.Id == s.Effect[7..]) is { } custom)
        {
            var palette = custom.Colors.Select(h => Rgb.Parse(h, colour)).ToArray();
            var pattern = Enum.TryParse<RgbPattern>(custom.Pattern, out var p) ? p : RgbPattern.Wave;
            return new RgbLook(RgbEffects.Basic[0], colour, brightness, speed, palette.Length > 0 ? palette : [colour], pattern);
        }
        return new RgbLook(RgbEffects.Find(s.Effect), colour, brightness, speed);
    }

    private static bool Needs(RgbSettings rgb, RgbPattern pattern) =>
        RgbZones.All.Any(z => LookOf(rgb, z.Key) is { } look && (look.CustomPattern ?? look.Effect.Pattern) == pattern);

    private async Task Loop(CancellationToken token)
    {
        var started = Environment.TickCount64;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(33));
            var blackSent = false;
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                var settings = _settings();
                var rgb = settings.Rgb;
                if (!rgb.Enabled && _previewUsers <= 0) break;
                var t = (Environment.TickCount64 - started) / 1000.0;

                double music = 0, temp = 0;
                if (Needs(rgb, RgbPattern.Music))
                {
                    var peak = AudioPeak.Read() ?? 0;
                    _smoothedMusic = Math.Max(peak, _smoothedMusic * 0.88);        // quick up, gentle down
                    music = _smoothedMusic;
                }
                if (Needs(rgb, RgbPattern.Temperature))
                    temp = _cpuTemp() is { } c ? Math.Clamp((c - 35) / 55.0, 0, 1) : Math.Clamp(_cpuLoad() / 100.0, 0, 1);

                var frames = new Dictionary<string, Rgb[]>();
                foreach (var zone in RgbZones.All)
                {
                    var look = LookOf(rgb, zone.Key);
                    var pattern = look.CustomPattern ?? look.Effect.Pattern;
                    var level = pattern == RgbPattern.Music ? music : temp;
                    // lights switched off in Shabpa: the preview keeps showing the effect, the hardware gets nothing new
                    frames[zone.Key] = RgbEngine.Frame(look, zone.Leds, t, level);
                }
                lock (_gate)
                {
                    _frames.Clear();
                    foreach (var (k, v) in frames) _frames[k] = v;
                }
                FrameReady?.Invoke();

                if (!rgb.Enabled)
                {
                    SetLink(RgbLink.Off);
                    if (_client.Connected && !blackSent) { blackSent = true; _client.Update(_ => [Rgb.Black]); }
                    continue;
                }
                blackSent = false;
                if (!_client.Connected)
                {
                    if (Environment.TickCount64 >= _nextConnect)
                    {
                        SetLink(RgbLink.Connecting);
                        // the lighting server is Shabpa's own hidden copy of OpenRGB (when it was fetched): start it, it needs a few seconds to find the hardware
                        var local = rgb.Host.Length == 0 || rgb.Host is "127.0.0.1" or "localhost";
                        var port = Math.Clamp(rgb.Port, 1, 65535);
                        if (local && OpenRgbHost.Installed && OpenRgbHost.Start(port)) { _nextConnect = Environment.TickCount64 + 6000; continue; }
                        var ok = _client.Connect(rgb.Host.Length > 0 ? rgb.Host : "127.0.0.1", port);
                        _nextConnect = Environment.TickCount64 + (ok ? 0 : 4000);
                        if (!ok) SetLink(RgbLink.NoServer, _client.LastError);
                        else SetLink(_client.Devices.Count == 0 ? RgbLink.NoDevices : RgbLink.Sending);
                    }
                    continue;
                }
                if (_client.Devices.Count == 0) { SetLink(RgbLink.NoDevices); continue; }
                if (!_client.Update(zone => frames.TryGetValue(zone, out var f) ? f : null))
                {
                    SetLink(RgbLink.NoServer, _client.LastError);
                    _nextConnect = Environment.TickCount64 + 2000;
                }
                else SetLink(RgbLink.Sending);
            }
        }
        catch (OperationCanceledException) { /* stopped */ }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Write($"rgb loop: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            lock (_gate)
            {
                _cts?.Dispose();
                _cts = null;
            }
            if (!_settings().Rgb.Enabled) { SetLink(RgbLink.Off); if (_previewUsers <= 0) OpenRgbHost.Stop(); }   // lights off: the hidden server stops too and costs nothing
        }
    }

    /// <summary>Puts every light out (used when the person turns lighting off, and when Shabpa quits with lighting on... it leaves them as they were).</summary>
    public void BlackOut()
    {
        if (_client.Connected) _client.Update(_ => [Rgb.Black]);
    }

    public void Dispose()
    {
        lock (_gate) { _cts?.Cancel(); }
        _client.Dispose();
        OpenRgbHost.Stop();
    }
}
