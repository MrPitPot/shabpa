using System.Globalization;
using System.Runtime.InteropServices;

namespace Shabpa.Core;

/// <summary>Everything about eye comfort and breaks. All of it is optional and off unless the user turns it on.</summary>
public sealed class ComfortSettings
{
    // ---- screen dimming ---------------------------------------------------------------------
    /// <summary>The screen is dimmed right now because the user switched it on.</summary>
    public bool DimOn { get; set; }
    /// <summary>How dark the cover is, 0.05 (barely) to 0.92 (almost black).</summary>
    public double DimLevel { get; set; } = 0.55;
    /// <summary>Tint the cover warm (less blue light) instead of plain black.</summary>
    public bool DimWarm { get; set; } = true;
    /// <summary>Colour temperature of the warm tint in Kelvin, 1900 (very warm) to 6500 (no tint). Used when <see cref="DimWarm"/> is on.</summary>
    public int DimKelvin { get; set; } = 3600;
    /// <summary>Night-light presets the user edited; a preset missing here has its built-in values.</summary>
    public List<PresetValue> Presets { get; set; } = [];
    /// <summary>Dim by itself between <see cref="DimFrom"/> and <see cref="DimTo"/>.</summary>
    public bool DimAuto { get; set; }
    public string DimFrom { get; set; } = "22:00";
    public string DimTo { get; set; } = "07:00";
    public string DimKey { get; set; } = "Ctrl+Alt+D";
    public string DimDarkerKey { get; set; } = "Ctrl+Alt+Down";
    public string DimLighterKey { get; set; } = "Ctrl+Alt+Up";

    // ---- break reminders --------------------------------------------------------------------
    public bool BreakOn { get; set; }
    public int BreakEveryMinutes { get; set; } = 45;
    /// <summary>Also remind while a game is running (off: the reminder waits until the game is over).</summary>
    public bool BreakInGame { get; set; }

    // ---- sleep mode -------------------------------------------------------------------------
    public int SleepMinutes { get; set; } = 30;
    public PowerAction SleepAction { get; set; } = PowerAction.Shutdown;
    public bool SleepDim { get; set; } = true;
    public bool SleepVolume { get; set; } = true;
    /// <summary>The volume sleep mode started from, kept until it has been put back (also after a shutdown).</summary>
    public double? PendingVolume { get; set; }

    // ---- game mode --------------------------------------------------------------------------
    /// <summary>When a game starts: switch to the best power plan, free RAM and keep Shabpa's own notices quiet.</summary>
    public bool GameModeOn { get; set; }
    public bool GameModePower { get; set; } = true;
    public bool GameModeRam { get; set; } = true;
    public bool GameModeQuiet { get; set; } = true;
    /// <summary>The power plan that was active before game mode changed it; put back at the end (also after a restart).</summary>
    public string PendingPowerPlan { get; set; } = "";
}

/// <summary>One finished game, kept so the history page can show how sessions compare.</summary>
public sealed class GameLogEntry
{
    public string Game { get; set; } = "";
    public DateTime Start { get; set; }
    public double Minutes { get; set; }
    public double AvgFps { get; set; }
    public double Low1Fps { get; set; }
    public double AvgPing { get; set; }
    public double MaxGpuTemp { get; set; }
    public double AvgGpuLoad { get; set; }

    public static GameLogEntry From(GameReport r) => new()
    {
        Game = r.Game, Start = r.Start, Minutes = r.Duration.TotalMinutes, AvgFps = r.AvgFps, Low1Fps = r.Low1Fps,
        AvgPing = r.AvgPing, MaxGpuTemp = r.MaxGpuTemp, AvgGpuLoad = r.AvgGpuLoad,
    };
}

/// <summary>Where one group of overlay readings is drawn: with the main block, in a screen corner, or at a free spot.</summary>
public sealed class GroupPlace
{
    public string Group { get; set; } = "";
    /// <summary>"main" (together with the rest), tl, tc, tr, bl, bc, br, or "free" (X and Y are screen pixels).</summary>
    public string Anchor { get; set; } = "main";
    public double X { get; set; }
    public double Y { get; set; }
}

/// <summary>Overlay choices remembered for one game; applied whenever that game is the one running.</summary>
public sealed class OverlayProfile
{
    public string Game { get; set; } = "";
    public string Layout { get; set; } = "card";
    public string Look { get; set; } = "theme";
    public double Scale { get; set; } = 1;
    public string Anchor { get; set; } = "tl";
    public double X { get; set; } = 24;
    public double Y { get; set; } = 24;
    public bool ShowLabels { get; set; } = true;
    public List<string> Items { get; set; } = [];
}

public static class ComfortTime
{
    /// <summary>Reads "HH:mm" (Persian or Latin digits); null if it is not a time of day.</summary>
    public static TimeSpan? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var latin = new string([.. text.Trim().Select(c => c is >= '۰' and <= '۹' ? (char)('0' + (c - '۰')) : c is >= '٠' and <= '٩' ? (char)('0' + (c - '٠')) : c)]);
        return TimeSpan.TryParseExact(latin, ["h\\:mm", "hh\\:mm"], CultureInfo.InvariantCulture, out var t) && t < TimeSpan.FromDays(1) ? t : null;
    }

    /// <summary>True when <paramref name="now"/> is inside the window; windows that cross midnight (22:00–07:00) work too.</summary>
    public static bool InWindow(DateTime now, string from, string to)
    {
        if (Parse(from) is not { } a || Parse(to) is not { } b || a == b) return false;
        var t = now.TimeOfDay;
        return a < b ? t >= a && t < b : t >= a || t < b;
    }
}

/// <summary>Works out when a "stand up, drink water, look away" nudge is due. It only counts time the user is
/// really at the computer: five idle minutes count as a break and start the count again.</summary>
public sealed class BreakPlanner
{
    private DateTime? _last;
    private double _activeSeconds, _idleSeconds;
    private int _next;

    public double ActiveMinutes => _activeSeconds / 60;

    public static readonly string[] Fa =
    [
        "پاشو یه کش‌وقوس بیا! کمرت داره از دست مانیتور شکایت می‌کنه 🧘",
        "یه لیوان آب بخور، گیاه نیستی که خشک شی 💧",
        "۲۰ ثانیه به یه چیز دور نگاه کن، چشمات استراحت می‌خوان 👀",
        "ساعتی شده نشستی! پاشو یه دور بزن، پاهات هنوز کار می‌کنن 🚶",
        "شونه‌هات رو شل کن، دارن به گوشت می‌چسبن 😅",
        "چشمک بزن! آره، همین حالا. چشمات خشک شدن 😉",
        "یه نفس عمیق بکش، بازدم هم یادت نره 🌬️",
        "آب بخور، قول می‌دم بازی‌ت فرار نمی‌کنه 🥤",
        "وقت یه استراحت کوچیکه؛ سر و گردنت رو بچرخون 🔄",
        "پاشو یه لبخند به آشپزخونه بزن و آب بیار 🍵",
    ];

    public static readonly string[] En =
    [
        "Stand up and stretch! Your back is filing a complaint about the monitor 🧘",
        "Drink a glass of water, you're not a plant 💧",
        "Look at something far away for 20 seconds, your eyes need it 👀",
        "You've been sitting a while! Take a short walk 🚶",
        "Drop your shoulders, they're hugging your ears 😅",
        "Blink! Yes, right now. Your eyes are dry 😉",
        "Take a deep breath, and don't forget to breathe out 🌬️",
        "Have some water, your game won't run away 🥤",
        "Time for a tiny break; roll your neck 🔄",
        "Go say hi to the kitchen and bring some tea 🍵",
    ];

    /// <summary>Call about once a second. Returns the reminder text when one is due, otherwise null.</summary>
    public string? Tick(DateTime now, bool userActive, bool inGame, ComfortSettings settings, bool english)
    {
        var dt = _last is { } last ? Math.Clamp((now - last).TotalSeconds, 0, 5) : 0;
        _last = now;
        if (!settings.BreakOn)
        {
            _activeSeconds = _idleSeconds = 0;
            return null;
        }
        if (userActive)
        {
            _idleSeconds = 0;
            _activeSeconds += dt;
        }
        else
        {
            _idleSeconds += dt;
            if (_idleSeconds >= 300) _activeSeconds = 0;
        }
        var every = Math.Clamp(settings.BreakEveryMinutes, 5, 240) * 60;
        if (_activeSeconds < every || (inGame && !settings.BreakInGame)) return null;
        _activeSeconds = 0;
        var list = english ? En : Fa;
        return list[_next++ % list.Length];
    }
}

/// <summary>The gentle "going to sleep" ramp: over the chosen time the volume and the screen fade out, then the
/// chosen power action runs.</summary>
public static class SleepRamp
{
    public const double DimTarget = 0.88;

    public static double Progress(DateTime start, TimeSpan length, DateTime now) =>
        length <= TimeSpan.Zero ? 1 : Math.Clamp((now - start).TotalSeconds / length.TotalSeconds, 0, 1);

    /// <summary>Volume (0–1) at this point: falls evenly from where it started to silence.</summary>
    public static double Volume(double startVolume, double progress) => Math.Clamp(startVolume * (1 - progress), 0, 1);

    /// <summary>Dim level at this point: starts at the current dimming and ends at <see cref="DimTarget"/>.</summary>
    public static double Dim(double startDim, double progress) => Math.Clamp(startDim + (DimTarget - startDim) * progress, 0, DimTarget);
}

/// <summary>The master volume of the default speakers (Core Audio).</summary>
public static class MasterVolume
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorClass { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object endpoint);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr notify);
        int UnregisterControlChangeNotify(IntPtr notify);
        int GetChannelCount(out uint count);
        int SetMasterVolumeLevel(float level, ref Guid context);
        int SetMasterVolumeLevelScalar(float level, ref Guid context);
        int GetMasterVolumeLevel(out float level);
        int GetMasterVolumeLevelScalar(out float level);
    }

    private static IAudioEndpointVolume? Open()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorClass();
            if (enumerator.GetDefaultAudioEndpoint(0, 1, out var device) != 0) return null;
            var iid = typeof(IAudioEndpointVolume).GUID;
            device.Activate(ref iid, 23, IntPtr.Zero, out var endpoint);
            return endpoint as IAudioEndpointVolume;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>0–1, or null if there is no audio device.</summary>
    public static double? Get()
    {
        try
        {
            return Open() is { } volume && volume.GetMasterVolumeLevelScalar(out var level) == 0 ? level : null;
        }
        catch
        {
            return null;
        }
    }

    // The audio endpoint is a COM call into the sound driver. A driver that is busy or broken (virtual cables, some vendor audio
    // suites) can keep it for many seconds, so the window never makes this call itself: it asks a worker and waits only a
    // moment for the answer, or hands over the newest level and moves on.
    private static readonly object LaterGate = new();
    private static double? _later;
    private static bool _laterRunning;

    /// <summary><see cref="Get"/> without ever holding the caller for long: null when the driver did not answer in time.</summary>
    public static double? GetBounded(int milliseconds = 1500) => Offload.Bounded(Get, null, milliseconds);

    /// <summary><see cref="Set"/> that gives up waiting after a moment (false then).</summary>
    public static bool SetBounded(double level, int milliseconds = 1500) => Offload.Bounded(() => Set(level), false, milliseconds);

    /// <summary>Sets the level in the background; when called again before the driver answered, only the newest level is applied.</summary>
    public static void SetLater(double level)
    {
        lock (LaterGate)
        {
            _later = level;
            if (_laterRunning) return;
            _laterRunning = true;
        }
        _ = Task.Run(() =>
        {
            while (true)
            {
                double next;
                lock (LaterGate)
                {
                    if (_later is not { } value) { _laterRunning = false; return; }
                    next = value;
                    _later = null;
                }
                Set(next);
            }
        });
    }

    public static bool Set(double level)
    {
        try
        {
            var context = Guid.Empty;
            return Open() is { } volume && volume.SetMasterVolumeLevelScalar((float)Math.Clamp(level, 0, 1), ref context) == 0;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>A Windows waitable timer that wakes the PC from sleep at a given time (needs "allow wake timers", which is
/// the default on most PCs). One timer; setting it again moves it.</summary>
public sealed class WakeTimer : IDisposable
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWaitableTimerW(IntPtr attributes, bool manualReset, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetWaitableTimer(IntPtr timer, ref long dueTime, int period, IntPtr completion, IntPtr argument, bool resume);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CancelWaitableTimer(IntPtr timer);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    private IntPtr _timer;
    private DateTime? _at;

    public DateTime? At => _at;

    /// <summary>Wake at this local time. Returns false if Windows refused.</summary>
    public bool Set(DateTime at)
    {
        if (_at is { } current && Math.Abs((current - at).TotalSeconds) < 1) return true;
        if (_timer == IntPtr.Zero) _timer = CreateWaitableTimerW(IntPtr.Zero, true, null);
        if (_timer == IntPtr.Zero) return false;
        var due = at.ToUniversalTime().ToFileTimeUtc(); // positive = absolute time
        if (!SetWaitableTimer(_timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, true)) return false;
        _at = at;
        return true;
    }

    public void Clear()
    {
        if (_timer != IntPtr.Zero) CancelWaitableTimer(_timer);
        _at = null;
    }

    public void Dispose()
    {
        if (_timer == IntPtr.Zero) return;
        CancelWaitableTimer(_timer);
        CloseHandle(_timer);
        _timer = IntPtr.Zero;
    }
}

/// <summary>How long ago the keyboard or mouse was last touched.</summary>
public static class UserIdle
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInput
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInput info);

    public static TimeSpan Since()
    {
        var info = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
        return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Time));
    }
}
