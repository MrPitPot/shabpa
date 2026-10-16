using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Shabpa.Core;

public enum MicVerdict { Excellent, Good, Noisy, VeryNoisy, Silent }

public sealed record MicApp(string Name, DateTime? LastStart, DateTime? LastStop, bool InUseNow, bool Allowed);

public sealed record NoiseTool(string Name, bool Installed, string HintFa, string HintEn);

/// <summary>The default microphone: its level and mute switch, a short measurement of how noisy it is, and which
/// programs used it. Audio is only listened to while the person presses a test button, only to compute loudness, and is
/// never stored or sent.</summary>
public static class Microphone
{
    // ---- loudness maths ---------------------------------------------------------------------------

    public const double SilenceFloorDb = -90;

    /// <summary>Loudness of 16-bit samples as dB relative to full scale (0 dB is the loudest possible).</summary>
    public static (double rmsDb, double peakDb) Measure(ReadOnlySpan<short> samples)
    {
        if (samples.Length == 0) return (SilenceFloorDb, SilenceFloorDb);
        double sum = 0;
        double peak = 0;
        foreach (var s in samples)
        {
            var v = s / 32768.0;
            sum += v * v;
            if (Math.Abs(v) > peak) peak = Math.Abs(v);
        }
        var rms = Math.Sqrt(sum / samples.Length);
        return (Db(rms), Db(peak));
    }

    private static double Db(double amplitude) => amplitude <= 1e-5 ? SilenceFloorDb : Math.Max(SilenceFloorDb, 20 * Math.Log10(amplitude));

    /// <summary>How quiet the room is, judged from the loudness while nobody speaks.</summary>
    public static MicVerdict Judge(double noiseFloorDb) => noiseFloorDb switch
    {
        <= -85 => MicVerdict.Silent, // exactly nothing: muted, or no signal at all
        <= -62 => MicVerdict.Excellent,
        <= -50 => MicVerdict.Good,
        <= -40 => MicVerdict.Noisy,
        _ => MicVerdict.VeryNoisy,
    };

    public static string Advice(MicVerdict v) => v switch
    {
        MicVerdict.Silent => L.T("هیچ صدایی نیومد. میکروفون قطعه، بی‌صداست یا شب‌پا اجازه‌ی دسترسی نداره (Settings ← Privacy ← Microphone).",
            "No sound at all. The microphone may be unplugged or muted, or Windows is not letting desktop apps use it (Settings → Privacy → Microphone)."),
        MicVerdict.Excellent => L.T("نویز خیلی کمه؛ نیازی به کاری نیست.", "Very little noise; nothing to do."),
        MicVerdict.Good => L.T("نویز کمه و قابل قبوله. اگه تو تماس‌ها ناراحت‌کننده بود، نویزگیر رو روشن کن.", "Low noise and fine. If it bothers people on calls, turn a noise suppressor on."),
        MicVerdict.Noisy => L.T("نویز قابل توجهه. اول «سطح ورودی» رو کم کن (بیشتر نویز از تقویت زیاده)، بعد نویزگیر رو روشن کن و میکروفون رو از فن و بلندگو دور کن.",
            "Noticeable noise. First lower the input level (most noise comes from too much gain), then turn on a noise suppressor and move the microphone away from fans and speakers."),
        _ => L.T("نویز خیلی زیاده. سطح ورودی رو خیلی کم کن، پورت USB دیگه‌ای امتحان کن یا میکروفون رو از کیس و کابل برق دور کن؛ اگه تغییری نکرد ممکنه خود میکروفون یا کابل مشکل داشته باشه.",
            "Very noisy. Turn the input level well down, try another USB port or move the microphone away from the case and power cables; if nothing changes the microphone or cable may be at fault."),
    };

    /// <summary>Feedback on a speaking test: whether the voice is too quiet or clipping.</summary>
    public static string SpeechAdvice(double rmsDb, double peakDb)
    {
        if (peakDb >= -1.0) return L.T("صدا به سقف می‌رسه و خرد می‌شه (clipping)؛ سطح ورودی رو کم کن.", "The sound reaches the ceiling and distorts (clipping); lower the input level.");
        if (rmsDb < -40) return L.T("صدات خیلی کمه؛ سطح ورودی رو بالا ببر یا میکروفون رو نزدیک‌تر بگیر.", "Your voice is very quiet; raise the input level or move the microphone closer.");
        if (rmsDb < -30) return L.T("صدات کمی کمه؛ کمی سطح ورودی رو بالا ببر.", "Your voice is a little quiet; raise the input level slightly.");
        return L.T("سطح صدات خوبه.", "Your level is good.");
    }

    // ---- recording a few seconds for the meter ---------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormat
    {
        public ushort Tag, Channels;
        public uint SamplesPerSec, AvgBytesPerSec;
        public ushort BlockAlign, BitsPerSample, Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public IntPtr Data;
        public uint BufferLength, BytesRecorded;
        public IntPtr User;
        public uint Flags, Loops;
        public IntPtr Next, Reserved;
    }

    [DllImport("winmm.dll")] private static extern int waveInOpen(out IntPtr handle, uint device, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] private static extern int waveInPrepareHeader(IntPtr handle, ref WaveHeader header, int size);
    [DllImport("winmm.dll")] private static extern int waveInUnprepareHeader(IntPtr handle, ref WaveHeader header, int size);
    [DllImport("winmm.dll")] private static extern int waveInAddBuffer(IntPtr handle, ref WaveHeader header, int size);
    [DllImport("winmm.dll")] private static extern int waveInStart(IntPtr handle);
    [DllImport("winmm.dll")] private static extern int waveInStop(IntPtr handle);
    [DllImport("winmm.dll")] private static extern int waveInReset(IntPtr handle);
    [DllImport("winmm.dll")] private static extern int waveInClose(IntPtr handle);

    private const uint WaveMapper = 0xFFFFFFFF;
    private const uint HeaderDone = 1;

    /// <summary>Listens to the default microphone for <paramref name="milliseconds"/> and reports loudness every ~100 ms
    /// through <paramref name="block"/> (rms and peak in dB). Returns an error text, or null when it worked. Nothing is
    /// kept: each block is measured and dropped.</summary>
    public static string? Listen(int milliseconds, Action<double, double> block, CancellationToken ct = default)
    {
        const int sampleRate = 16000;
        const int blockSamples = sampleRate / 10;
        var format = new WaveFormat { Tag = 1, Channels = 1, SamplesPerSec = sampleRate, AvgBytesPerSec = sampleRate * 2, BlockAlign = 2, BitsPerSample = 16 };
        var opened = waveInOpen(out var handle, WaveMapper, ref format, IntPtr.Zero, IntPtr.Zero, 0);
        if (opened != 0) return L.T("میکروفون باز نشد (ممکنه قطع باشه یا دسترسی نداشته باشه).", "The microphone could not be opened (it may be unplugged or blocked).");
        const int buffers = 3;
        var headers = new WaveHeader[buffers];
        var memory = new IntPtr[buffers];
        var size = Marshal.SizeOf<WaveHeader>();
        try
        {
            for (var i = 0; i < buffers; i++)
            {
                memory[i] = Marshal.AllocHGlobal(blockSamples * 2);
                headers[i] = new WaveHeader { Data = memory[i], BufferLength = blockSamples * 2 };
                waveInPrepareHeader(handle, ref headers[i], size);
                waveInAddBuffer(handle, ref headers[i], size);
            }
            waveInStart(handle);
            var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
            var samples = new short[blockSamples];
            while (DateTime.UtcNow < until && !ct.IsCancellationRequested)
            {
                var any = false;
                for (var i = 0; i < buffers; i++)
                {
                    if ((headers[i].Flags & HeaderDone) == 0) continue;
                    any = true;
                    var count = (int)Math.Min(headers[i].BytesRecorded / 2, (uint)blockSamples);
                    Marshal.Copy(memory[i], samples, 0, count);
                    var (rms, peak) = Measure(samples.AsSpan(0, count));
                    block(rms, peak);
                    headers[i].Flags &= ~HeaderDone;
                    headers[i].BytesRecorded = 0;
                    waveInAddBuffer(handle, ref headers[i], size);
                }
                if (!any) Thread.Sleep(15);
            }
            return null;
        }
        finally
        {
            waveInStop(handle);
            waveInReset(handle);
            for (var i = 0; i < buffers; i++)
            {
                if (memory[i] == IntPtr.Zero) continue;
                waveInUnprepareHeader(handle, ref headers[i], size);
                Marshal.FreeHGlobal(memory[i]);
            }
            waveInClose(handle);
        }
    }

    // ---- level and mute through Core Audio ---------------------------------------------------------------

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
        int OpenPropertyStore(int access, out IntPtr properties);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetState(out int state);
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
        int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
        int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        int GetChannelVolumeLevel(uint channel, out float level);
        int GetChannelVolumeLevelScalar(uint channel, out float level);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    private static (IMMDevice device, IAudioEndpointVolume volume)? Open()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorClass();
            // flow 1 = capture, role 0 = console
            if (enumerator.GetDefaultAudioEndpoint(1, 0, out var device) != 0) return null;
            var iid = typeof(IAudioEndpointVolume).GUID;
            device.Activate(ref iid, 23, IntPtr.Zero, out var endpoint);
            return endpoint is IAudioEndpointVolume v ? (device, v) : null;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            return null;
        }
    }

    /// <summary>0–1 and whether it is muted, or null if there is no microphone.</summary>
    public static (double level, bool muted)? Read()
    {
        if (Open() is not { } open) return null;
        try
        {
            if (open.volume.GetMasterVolumeLevelScalar(out var level) != 0) return null;
            open.volume.GetMute(out var muted);
            return (level, muted);
        }
        catch (COMException)
        {
            return null;
        }
    }

    public static bool SetLevel(double level)
    {
        if (Open() is not { } open) return false;
        try
        {
            var context = Guid.Empty;
            return open.volume.SetMasterVolumeLevelScalar((float)Math.Clamp(level, 0, 1), ref context) == 0;
        }
        catch (COMException)
        {
            return false;
        }
    }

    public static bool SetMuted(bool muted)
    {
        if (Open() is not { } open) return false;
        try
        {
            var context = Guid.Empty;
            return open.volume.SetMute(muted, ref context) == 0;
        }
        catch (COMException)
        {
            return false;
        }
    }

    /// <summary>The friendly name of the default microphone, read from the registry by its endpoint id.</summary>
    public static string? DeviceName()
    {
        if (Open() is not { } open) return null;
        try
        {
            if (open.device.GetId(out var id) != 0) return null;
            return NameFromEndpointId(id, new WindowsRegistryProbe());
        }
        catch (COMException)
        {
            return null;
        }
    }

    internal static string? NameFromEndpointId(string id, IRegistryProbe reg)
    {
        // "{0.0.1.00000000}.{3b8a...}" -> the guid after the last dot
        var dot = id.LastIndexOf('.');
        if (dot < 0 || dot + 1 >= id.Length) return null;
        var guid = id[(dot + 1)..];
        var key = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture\" + guid + @"\Properties";
        // {a45c254e-...},2 is the device description, {b3f8fa53-...},6 the full friendly name
        return reg.Get(key, "{b3f8fa53-0004-438e-9003-51a46e139bfc},6") as string
               ?? reg.Get(key, "{a45c254e-df1c-4efd-8020-67d146a850e0},2") as string;
    }

    // ---- who used the microphone ------------------------------------------------------------------------

    private const string Consent = @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";

    internal static DateTime? FromFileTime(object? value)
    {
        if (value is not long ticks || ticks <= 0) return null;
        try { return DateTime.FromFileTime(ticks); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    /// <summary>Programs that used (or may use) the microphone, newest first. The names come from Windows' own privacy
    /// list; nothing is recorded by Shabpa.</summary>
    public static List<MicApp> RecentApps()
    {
        var list = new List<MicApp>();
        try
        {
            foreach (var (path, nonPackaged) in new[] { (Consent, false), (Consent + @"\NonPackaged", true) })
            {
                using var key = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64)
                    .OpenSubKey(path[(path.IndexOf('\\') + 1)..]);
                if (key is null) continue;
                foreach (var name in key.GetSubKeyNames())
                {
                    if (nonPackaged == false && name == "NonPackaged") continue;
                    using var app = key.OpenSubKey(name);
                    if (app is null) continue;
                    var start = FromFileTime(app.GetValue("LastUsedTimeStart"));
                    var stop = FromFileTime(app.GetValue("LastUsedTimeStop"));
                    var allowed = !string.Equals(app.GetValue("Value") as string, "Deny", StringComparison.OrdinalIgnoreCase);
                    var inUse = start is not null && app.GetValue("LastUsedTimeStop") is long s && s == 0;
                    list.Add(new MicApp(Pretty(name), start, stop, inUse, allowed));
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Write($"mic apps: {ex.GetType().Name}");
        }
        return [.. list.OrderByDescending(a => a.LastStart ?? DateTime.MinValue)];
    }

    internal static string Pretty(string registryName)
    {
        // non-packaged apps are stored as their path with "\" written as "#"
        if (registryName.Contains('#'))
        {
            var file = Path.GetFileName(registryName.Replace('#', (char)92));
            if (file.Length > 0) return file;
        }
        // packaged apps: "Microsoft.WindowsCamera_8wekyb3d8bbwe" -> "Microsoft.WindowsCamera"
        var us = registryName.IndexOf('_');
        return us > 0 ? registryName[..us] : registryName;
    }

    /// <summary>Whether Windows lets desktop programs use the microphone at all (Settings → Privacy).</summary>
    public static bool? DesktopAppsAllowed(IRegistryProbe? reg = null)
    {
        var r = reg ?? new WindowsRegistryProbe();
        var value = r.Get(Consent + @"\NonPackaged", "Value") as string ?? r.Get(Consent, "Value") as string;
        return value is null ? null : !string.Equals(value, "Deny", StringComparison.OrdinalIgnoreCase);
    }

    // ---- noise suppression tools ------------------------------------------------------------------------

    public static List<NoiseTool> NoiseTools()
    {
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        return
        [
            new NoiseTool("AMD Noise Suppression", File.Exists(Path.Combine(system, "AMD", "ANR", "AMDNoiseSuppression.exe")),
                "داخل AMD Software: Adrenalin Edition، بخش Settings ← Audio (یا Noise Suppression) روشنش کن.", "Turn it on in AMD Software: Adrenalin Edition under Settings → Audio (Noise Suppression)."),
            new NoiseTool("NVIDIA Broadcast", Directory.Exists(Path.Combine(programFiles, "NVIDIA Corporation", "NVIDIA Broadcast")),
                "داخل NVIDIA Broadcast، میکروفون رو انتخاب کن و Noise Removal رو روشن کن؛ بعد توی برنامه‌ی تماس، «NVIDIA Broadcast» رو به‌عنوان میکروفون بذار.",
                "In NVIDIA Broadcast choose the microphone and switch Noise Removal on; then pick “NVIDIA Broadcast” as the microphone in your call app."),
        ];
    }
}
