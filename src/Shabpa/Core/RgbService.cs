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

    private readonly Func<bool>? _gameRunning;
    private volatile bool _suspended;
    private int _blackLeft;
    private long _transStart;
    private bool _wasEnabled, _startupPlayed;
    private long _nextRivalCheck;
    private Dictionary<int, Rgb[]>? _slices;

    public RgbService(Func<Settings> settings, Func<double?> cpuTemp, Func<double> cpuLoad, Func<bool>? gameRunning = null)
    {
        _settings = settings;
        _cpuTemp = cpuTemp;
        _cpuLoad = cpuLoad;
        _gameRunning = gameRunning;
        try
        {
            Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerMode;
            Microsoft.Win32.SystemEvents.SessionEnding += OnSessionEnding;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { /* no Windows events here (tests) */ }
    }

    private void OnPowerMode(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (!_settings().Rgb.OffOnSleep) return;
        if (e.Mode == Microsoft.Win32.PowerModes.Suspend)
        {
            _suspended = true;
            using var done = new ManualResetEventSlim();
            _jobs.Enqueue(c => { c.Update(_ => [Rgb.Black]); done.Set(); });
            done.Wait(600);                                     // the lights go out before the PC does
        }
        else if (e.Mode == Microsoft.Win32.PowerModes.Resume)
        {
            _jobs.Enqueue(c => c.Close());                      // the devices may have reset: connect again and send everything anew
            _nextConnect = 0;
            _suspended = false;
        }
    }

    private void OnSessionEnding(object? sender, Microsoft.Win32.SessionEndingEventArgs e)
    {
        var rgb = _settings().Rgb;
        if (!rgb.Enabled || !rgb.StartupEffect || !_driving || rgb.Transition == "none") return;
        PlayTransition(false);                                  // the shutdown effect: about a second, then dark
        Thread.Sleep(RgbTransitions.DurationMs + 150);
        _jobs.Enqueue(c => c.Update(_ => [Rgb.Black]));
        Thread.Sleep(150);
    }

    private void PlayTransition(bool on)
    {
        _transOn = on;
        _transStart = Environment.TickCount64;
    }

    private bool _transOn;

    /// <summary>0 to 1: how much of the lights shows while a fade in or out is running; 1 when none is.</summary>
    private double TransitionVisible()
    {
        if (_transStart == 0) return 1;
        var p = (Environment.TickCount64 - _transStart) / (double)RgbTransitions.DurationMs;
        if (p >= 1) { if (_transOn) _transStart = 0; return _transOn ? 1 : 0; }
        return _transOn ? p : 1 - p;
    }

    private bool TransitionRunning => _transStart != 0 && Environment.TickCount64 - _transStart < RgbTransitions.DurationMs;

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

    /// <summary>True when nothing on any light moves (every zone and every device with its own look shows a fixed colour).</summary>
    internal static bool IsStill(RgbSettings rgb)
    {
        bool Fixed(RgbZoneSetting s) { var look = LookOf(rgb, s); return (look.CustomPattern ?? look.Effect.Pattern) == RgbPattern.Solid || look.Brightness <= 0; }
        var zones = rgb.Sync ? [rgb.All] : RgbZones.All.Select(z => rgb.ZoneSetting(z.Key)).ToList();
        return zones.All(Fixed) && rgb.Devices.Where(d => d.Own && d.Enabled).All(d => Fixed(d.Look));
    }

    private readonly System.Collections.Concurrent.ConcurrentQueue<Action<OpenRgbClient>> _jobs = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, long> _identify = new();
    private bool _driving;

    /// <summary>Blinks one device white for two seconds so the person can see which one it is (works with the lights off in Shabpa too).</summary>
    public void Identify(int deviceIndex)
    {
        _identify[deviceIndex] = Environment.TickCount64 + 2000;
        EnsureRunning();
    }

    /// <summary>Gives a header zone of a device a size (how many LEDs are plugged in) and re-reads the device. <paramref name="done"/> gets the result on the loop's thread.</summary>
    public void ResizeZone(int deviceIndex, int zoneIndex, int count, Action<bool>? done = null)
    {
        _jobs.Enqueue(client => done?.Invoke(client.ResizeZone(deviceIndex, zoneIndex, count)));
        EnsureRunning();
    }

    private Rgb[]? IdentifyOnly(RgbDevice device)
    {
        if (!_identify.TryGetValue(device.Index, out var until)) return null;
        if (Environment.TickCount64 >= until) { _identify.TryRemove(device.Index, out _); return [Rgb.Black]; }
        return Blink(device);
    }

    private static Rgb[] Blink(RgbDevice device) => [.. Enumerable.Repeat(Environment.TickCount64 / 250 % 2 == 0 ? new Rgb(255, 255, 255) : Rgb.Black, Math.Max(1, device.Leds))];

    /// <summary>What one device shows now: a white blink while being identified, nothing when the person left it out, its own look when it has one, otherwise its zone's frame.</summary>
    private Rgb[]? FrameFor(RgbDevice device, RgbSettings rgb, Dictionary<string, Rgb[]> frames, double t, double music, double temp)
    {
        if (_identify.ContainsKey(device.Index)) return IdentifyOnly(device);
        var setting = rgb.FindDevice(device.Key);
        if (setting is { Enabled: false }) return null;
        Rgb[]? frame;
        if (setting is { Own: true })
        {
            var look = LookOf(rgb, setting.Look);
            var pattern = look.CustomPattern ?? look.Effect.Pattern;
            frame = RgbEngine.Frame(look, Math.Max(1, device.Leds), t + Ahead(device), pattern == RgbPattern.Music ? music : temp);
        }
        else if (_slices is not null && _slices.TryGetValue(device.Index, out var slice)) frame = slice;
        else
        {
            var zone = setting is { Zone.Length: > 0 } ? setting.Zone : device.Zone;
            frame = frames.TryGetValue(zone, out var f) ? f : null;
            var ahead = Ahead(device);
            if (frame is not null && ahead >= 0.012)
            {
                // a device that takes time to show a frame gets the effect as it will be when the frame arrives
                var look = LookOf(rgb, zone);
                var pattern = look.CustomPattern ?? look.Effect.Pattern;
                frame = RgbEngine.Frame(look, RgbZones.Find(zone).Leds, t + ahead, pattern == RgbPattern.Music ? music : temp);
            }
        }
        return frame is null || _transStart == 0 ? frame : RgbTransitions.Apply(RgbEngine.Resample(frame, Math.Max(1, device.Leds)), rgb.Transition, TransitionVisible());
    }

    /// <summary>Synchronised lights: ONE flowing effect laid along all the lights of the PC in a fixed order (fans, memory, cooler, strips, board, graphics card, keyboard, mouse), so a
    /// rainbow runs through the whole case instead of every device showing its own copy. Each device takes the stretch that belongs to it. The preview frames of the zones are
    /// filled the same way, so the picture matches the hardware.</summary>
    private Dictionary<int, Rgb[]> SyncFrames(RgbSettings rgb, double t, double music, double temp, Dictionary<string, Rgb[]> zoneFrames)
    {
        var look = LookOf(rgb, rgb.All);
        var pattern = look.CustomPattern ?? look.Effect.Pattern;
        var level = pattern == RgbPattern.Music ? music : temp;
        var devices = _client.Devices
            .Where(d => rgb.FindDevice(d.Key) is not { Enabled: false } and not { Own: true })
            .OrderBy(d => RgbZones.All.ToList().FindIndex(z => z.Key == (rgb.FindDevice(d.Key) is { Zone.Length: > 0 } o ? o.Zone : d.Zone))).ThenBy(d => d.Index).ToList();
        // every device takes a stretch of the flow that is about as long as a fan or a strip (a board with 400 LEDs must not swallow the rainbow), and that stretch is a part of the
        // colour circle, so each device shows two or three neighbouring colours that run and turn, not one colour that cycles
        double Weight(int leds) => Math.Clamp(leds, 8, 40);
        var count = devices.Count > 0 ? devices.Count : RgbZones.All.Count;
        var spread = Math.Max(1.0, count * 0.4);
        var nominal = RgbZones.All.Sum(z => z.Leds);
        var weightTotal = Math.Max(1.0, devices.Sum(d => Weight(d.Leds)));
        var length = Math.Clamp((int)Math.Round(Math.Max(devices.Count > 0 ? weightTotal : nominal, 8) * 4), 32, 1024);
        look = look with { Spread = spread };
        var global = RgbEngine.Frame(look, length, t, level);

        double at = 0;
        foreach (var zone in RgbZones.All)
        {
            zoneFrames[zone.Key] = RgbEngine.Slice(global, at / nominal, (at + zone.Leds) / nominal, zone.Leds);
            at += zone.Leds;
        }
        var slices = new Dictionary<int, Rgb[]>();
        var shifted = new Dictionary<int, Rgb[]> { [0] = global };       // the flow at other moments, for devices shown ahead of or behind the rest
        double pos = 0;
        foreach (var d in devices)
        {
            var n = Math.Max(1, d.Leds);
            var w = Weight(d.Leds);
            var setting = rgb.FindDevice(d.Key);
            var shift = (int)Math.Round(Ahead(d) * 1000 / 20.0) * 20;      // in steps of 20 ms, so devices that run alike share one frame
            if (!shifted.TryGetValue(shift, out var flow)) shifted[shift] = flow = RgbEngine.Frame(look, length, t + shift / 1000.0, level);
            var slice = RgbEngine.Slice(flow, pos / weightTotal, (pos + w) / weightTotal, n);
            if (setting is { OneColour: true }) slice = [.. Enumerable.Repeat(slice[slice.Length / 2], n)];
            slices[d.Index] = slice;
            pos += w;
        }
        return slices;
    }

    /// <summary>Seconds this device runs behind (the measured time to put a frame on it, at most 0.8): the effect is shown that far ahead, so every light changes at the same moment.</summary>
    private double Ahead(RgbDevice device) => Math.Clamp(_client.CostMs(device.Index), 0, 800) / 1000.0;

    private void CloseRivals()
    {
        try
        {
            foreach (var (rival, processes, services) in RgbRivals.Scan()) RgbRivals.Close(rival, processes, services);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException) { Log.Write($"rgb rivals: {ex.GetType().Name}"); }
    }

    /// <summary>After connecting: every header whose size can be set is set to the size the person typed, or to its maximum, so the whole fan chain or strip lights.</summary>
    private void QueueHeaderSizes(RgbSettings rgb)
    {
        _jobs.Enqueue(client => client.ApplySizes((device, zone) => rgb.ZoneSizes.TryGetValue(SizeKey(device, zone), out var typed) ? typed : zone.Max));
    }

    /// <summary>The key a header size is remembered under.</summary>
    public static string SizeKey(RgbDevice device, RgbZoneInfo zone) => device.Key + "|" + zone.Name;

    /// <summary>Starts the hidden lighting server when it has been fetched, then connects. <paramref name="silent"/> leaves the link state alone (the lights are off in Shabpa).</summary>
    private void Connect(RgbSettings rgb, bool silent)
    {
        if (Environment.TickCount64 < _nextConnect) return;
        if (!silent) SetLink(RgbLink.Connecting);
        var local = rgb.Host.Length == 0 || rgb.Host is "127.0.0.1" or "localhost";
        var port = Math.Clamp(rgb.Port, 1, 65535);
        if (local && OpenRgbHost.Installed && OpenRgbHost.Start(port)) { _nextConnect = Environment.TickCount64 + 6000; return; }
        var ok = _client.Connect(rgb.Host.Length > 0 ? rgb.Host : "127.0.0.1", port);
        _nextConnect = Environment.TickCount64 + (ok ? 0 : 4000);
        if (ok) QueueHeaderSizes(rgb);
        if (silent) return;
        if (!ok) SetLink(RgbLink.NoServer, _client.LastError);
        else SetLink(_client.Devices.Count == 0 ? RgbLink.NoDevices : RgbLink.Sending);
    }

    /// <summary>How the page's settings become the look of one zone.</summary>
    public static RgbLook LookOf(RgbSettings rgb, string zone) => LookOf(rgb, rgb.For(zone));

    /// <summary>How one setting (a zone's, or a device's own) becomes a look.</summary>
    public static RgbLook LookOf(RgbSettings rgb, RgbZoneSetting s)
    {
        var speed = Math.Pow(2, (Math.Clamp(s.Speed, 1, 10) - 5) / 2.5);
        var colour = Rgb.Parse(s.Color, new Rgb(255, 45, 85));
        var brightness = Math.Clamp(s.Brightness, 0, 100) / 100.0;
        if (s.Effect.StartsWith("custom:", StringComparison.Ordinal) && rgb.Custom.FirstOrDefault(c => c.Id == s.Effect[7..]) is { } custom)
        {
            var palette = custom.Colors.Select(h => Rgb.Parse(h, colour)).ToArray();
            var pattern = Enum.TryParse<RgbPattern>(custom.Pattern, out var p) ? p : RgbPattern.Wave;
            return new RgbLook(RgbEffects.Basic[0], colour, brightness, speed, palette.Length > 0 ? palette : [colour], pattern, s.Reverse, s.Starry);
        }
        return new RgbLook(RgbEffects.Find(s.Effect), colour, brightness, speed, null, null, s.Reverse, s.Starry);
    }

    private static bool Needs(RgbSettings rgb, RgbPattern pattern) =>
        RgbZones.All.Any(z => LookOf(rgb, z.Key) is { } look && (look.CustomPattern ?? look.Effect.Pattern) == pattern);

    private async Task Loop(CancellationToken token)
    {
        var started = Environment.TickCount64;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(33));
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                var settings = _settings();
                var rgb = settings.Rgb;
                if (!rgb.Enabled && _previewUsers <= 0 && !_driving && _blackLeft <= 0 && !TransitionRunning) break;     // not before the lights are really out
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

                // a page of lights that only show one fixed colour changes nothing between frames: look four times a second instead of thirty
                _client.Smooth = rgb.Smooth;
                timer.Period = IsStill(rgb) && _identify.IsEmpty && !TransitionRunning ? TimeSpan.FromMilliseconds(250) : TimeSpan.FromMilliseconds(rgb.Smooth ? 20 : 40);
                var frames = new Dictionary<string, Rgb[]>();
                foreach (var zone in RgbZones.All)
                {
                    var look = LookOf(rgb, zone.Key);
                    var pattern = look.CustomPattern ?? look.Effect.Pattern;
                    var level = pattern == RgbPattern.Music ? music : temp;
                    // lights switched off in Shabpa: the preview keeps showing the effect, the hardware gets nothing new
                    frames[zone.Key] = RgbEngine.Frame(look, zone.Leds, t, level);
                }
                _slices = rgb.Sync ? SyncFrames(rgb, t, music, temp, frames) : null;
                lock (_gate)
                {
                    _frames.Clear();
                    foreach (var (k, v) in frames) _frames[k] = v;
                }
                FrameReady?.Invoke();

                if (rgb.Enabled != _wasEnabled)
                {
                    if (rgb.Transition != "none" && (rgb.Enabled ? _client.Connected : _driving)) PlayTransition(rgb.Enabled);
                    _wasEnabled = rgb.Enabled;
                }
                if (rgb.Enabled && rgb.KeepRivalsClosed && Environment.TickCount64 >= _nextRivalCheck)
                {
                    _nextRivalCheck = Environment.TickCount64 + 30000;
                    _ = Task.Run(CloseRivals);
                }

                // things the pages asked for (resize a header, ...) run here, on the one thread that talks to the server
                for (var j = 0; j < 4 && _jobs.TryDequeue(out var job); j++)
                    if (_client.Connected) { try { job(_client); } catch (Exception ex) when (ex is System.IO.IOException or InvalidOperationException) { Log.Write($"rgb job: {ex.GetType().Name}"); } }

                if (!rgb.Enabled)
                {
                    SetLink(RgbLink.Off);
                    if (_client.Connected && _driving && TransitionRunning) _client.Update(device => FrameFor(device, rgb, frames, t, music, temp), force: true);     // fading out
                    else if (_client.Connected && _driving) { _driving = false; _transStart = 0; _blackLeft = 3; }
                    if (_client.Connected && _blackLeft > 0)
                    {
                        // «off» is sent three times and never held back by the usual rate limits: a device that had just been given a frame used to skip it and stay lit
                        _client.Update(_ => [Rgb.Black], force: true);
                        _blackLeft--;
                    }
                    else if (!_client.Connected) { _blackLeft = 0; _driving = false; }
                    if (_previewUsers > 0)
                    {
                        // the page is open with the lights off: still look at what is there (the devices window needs it) and let «test» blink a device
                        if (!_client.Connected) Connect(rgb, silent: true);
                        else if (!_identify.IsEmpty) _client.Update(IdentifyOnly);
                    }
                    continue;
                }
                if (!_client.Connected)
                {
                    if (Environment.TickCount64 >= _nextConnect) Connect(rgb, silent: false);
                    continue;
                }
                if (_client.Devices.Count == 0) { SetLink(RgbLink.NoDevices); continue; }
                if (_suspended) continue;                                                       // Windows is going to sleep (or just woke): nothing is sent
                if (rgb.PauseInGame && _gameRunning?.Invoke() == true) { SetLink(RgbLink.Sending); continue; }   // a game is running: the lights keep what they show
                if (!_startupPlayed)
                {
                    _startupPlayed = true;
                    if (rgb.StartupEffect && rgb.Transition != "none") PlayTransition(true);   // the lights come on with the chosen effect
                }
                _driving = true;
                if (!_client.Update(device => FrameFor(device, rgb, frames, t, music, temp)))
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
        _driving = true;                                        // the loop (the only thread that talks to the server) fades out if it is set to, then sends «off» three times
        EnsureRunning();
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerMode;
            Microsoft.Win32.SystemEvents.SessionEnding -= OnSessionEnding;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        lock (_gate) { _cts?.Cancel(); }
        _client.Dispose();
        OpenRgbHost.Stop();
    }
}
