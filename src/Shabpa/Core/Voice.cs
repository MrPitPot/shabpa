using System.Runtime.InteropServices;

namespace Shabpa.Core;

/// <summary>What the person chose for the live noise suppressor. Device names, not numbers: the numbers change when
/// something is plugged in.</summary>
public sealed class VoiceSettings
{
    /// <summary>Empty = the Windows default microphone.</summary>
    public string Input { get; set; } = "";
    /// <summary>Empty = the Windows default output.</summary>
    public string Output { get; set; } = "";
    /// <summary>0 to 1: how hard the noise is pushed down.</summary>
    public double Strength { get; set; } = 0.65;
    /// <summary>Switch the suppressor on again whenever Shabpa starts.</summary>
    public bool AutoStart { get; set; }
}

/// <summary>A classic spectral noise suppressor that runs entirely in memory: it follows the steady background level
/// of each frequency (fans, hiss, hum) and turns down what is only noise, leaving speech alone. It works on 10 ms
/// steps, adds about 5 ms of delay and keeps nothing: every block is processed and dropped.</summary>
public sealed class NoiseSuppressor
{
    private readonly int _n, _hop, _bins;
    private readonly float[] _window;
    private readonly float[] _cos, _sin;
    private readonly int[] _reverse;
    private readonly float[] _in, _outAcc;
    private int _filled;
    private readonly Queue<float> _ready = new();
    private readonly float[] _re, _im;
    private readonly float[] _smooth, _noise, _prevGain, _prevClean;
    private readonly float[][] _minRing;
    private readonly float[] _minNow;
    private int _frame, _ringAt;
    private const int SubWindows = 6, SubFrames = 30;
    private const float NoiseBias = 1.6f;

    /// <summary>0 = hardly any reduction, 1 = as strong as it goes.</summary>
    public double Strength { get; set; } = 0.65;
    public int SampleRate { get; }

    public NoiseSuppressor(int sampleRate)
    {
        SampleRate = sampleRate;
        _n = sampleRate >= 32000 ? 512 : 256;
        _hop = _n / 2;
        _bins = _n / 2 + 1;
        _window = new float[_n];
        for (var i = 0; i < _n; i++) _window[i] = (float)Math.Sqrt(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / _n)); // sqrt-Hann: analysis x synthesis = Hann
        _cos = new float[_n / 2];
        _sin = new float[_n / 2];
        for (var i = 0; i < _n / 2; i++)
        {
            _cos[i] = (float)Math.Cos(2 * Math.PI * i / _n);
            _sin[i] = (float)-Math.Sin(2 * Math.PI * i / _n);
        }
        _reverse = new int[_n];
        var bits = (int)Math.Log2(_n);
        for (var i = 0; i < _n; i++)
        {
            var r = 0;
            for (var b = 0; b < bits; b++) if ((i & (1 << b)) != 0) r |= 1 << (bits - 1 - b);
            _reverse[i] = r;
        }
        _in = new float[_n];
        _outAcc = new float[_n];
        _re = new float[_n];
        _im = new float[_n];
        _smooth = new float[_bins];
        _noise = new float[_bins];
        _prevGain = new float[_bins];
        _prevClean = new float[_bins];
        _minNow = new float[_bins];
        _minRing = new float[SubWindows][];
        for (var i = 0; i < SubWindows; i++) _minRing[i] = new float[_bins];
        Array.Fill(_prevGain, 1f);
        // one hop of silence in front, so a block of any size can always be answered with the same number of samples
        for (var i = 0; i < _hop; i++) _ready.Enqueue(0);
    }

    /// <summary>Cleans one block; the result has the same length.</summary>
    public void Process(ReadOnlySpan<float> input, Span<float> output)
    {
        for (var i = 0; i < input.Length; i++)
        {
            _in[_filled++] = float.IsFinite(input[i]) ? input[i] : 0;
            if (_filled == _n)
            {
                Frame();
                // keep the second half as the first half of the next frame
                Array.Copy(_in, _hop, _in, 0, _hop);
                _filled = _hop;
            }
            output[i] = _ready.Count > 0 ? _ready.Dequeue() : 0;
        }
    }

    private void Fft(bool inverse)
    {
        for (var i = 0; i < _n; i++)
        {
            var j = _reverse[i];
            if (j > i)
            {
                (_re[i], _re[j]) = (_re[j], _re[i]);
                (_im[i], _im[j]) = (_im[j], _im[i]);
            }
        }
        for (var size = 2; size <= _n; size <<= 1)
        {
            var half = size / 2;
            var step = _n / size;
            for (var start = 0; start < _n; start += size)
                for (var k = 0; k < half; k++)
                {
                    var c = _cos[k * step];
                    var s = inverse ? -_sin[k * step] : _sin[k * step];
                    var a = start + k;
                    var b = a + half;
                    var tr = _re[b] * c - _im[b] * s;
                    var ti = _re[b] * s + _im[b] * c;
                    _re[b] = _re[a] - tr;
                    _im[b] = _im[a] - ti;
                    _re[a] += tr;
                    _im[a] += ti;
                }
        }
        if (inverse)
            for (var i = 0; i < _n; i++) { _re[i] /= _n; _im[i] /= _n; }
    }

    private void Frame()
    {
        for (var i = 0; i < _n; i++) { _re[i] = _in[i] * _window[i]; _im[i] = 0; }
        Fft(false);

        var strength = (float)Math.Clamp(Strength, 0, 1);
        // the quietest the noise may be pushed: -8 dB (gentle) down to -34 dB (strong)
        var floor = (float)Math.Pow(10, -(8 + 26 * strength) / 20);
        var over = 0.3f + 1.7f * strength;
        var gains = new float[_bins];

        for (var k = 0; k < _bins; k++)
        {
            var power = _re[k] * _re[k] + _im[k] * _im[k] + 1e-12f;
            _smooth[k] = _frame == 0 ? power : 0.75f * _smooth[k] + 0.25f * power;
            if (_frame == 0) { _minNow[k] = _smooth[k]; for (var r = 0; r < SubWindows; r++) _minRing[r][k] = _smooth[k]; }
            _minNow[k] = Math.Min(_minNow[k], _smooth[k]);

            // the steady level: the smallest the smoothed power got in the last second or so
            var low = _minRing[0][k];
            for (var r = 1; r < SubWindows; r++) low = Math.Min(low, _minRing[r][k]);
            low = Math.Min(low, _minNow[k]);
            // while this frame is plainly background (not louder than twice the estimate) the estimate follows it
            var estimate = low * NoiseBias;
            _noise[k] = _frame < 4 ? Math.Max(_smooth[k], 1e-12f) : estimate;

            var noise = Math.Max(_noise[k], 1e-12f);
            var post = power / noise;
            // decision-directed a-priori signal-to-noise ratio
            var prior = 0.93f * (_prevGain[k] * _prevGain[k] * _prevClean[k] / noise) + 0.07f * Math.Max(post - 1, 0);
            var gain = prior / (prior + over);
            gains[k] = Math.Clamp(gain, floor, 1f);
        }

        // sub-window bookkeeping for the minimum tracking
        _frame++;
        if (_frame % SubFrames == 0)
        {
            Array.Copy(_minNow, _minRing[_ringAt], _bins);
            _ringAt = (_ringAt + 1) % SubWindows;
            Array.Copy(_smooth, _minNow, _bins);
        }

        // smooth the gain across neighbouring frequencies, and let it fall back slower than it opens (no "musical" chirps)
        for (var k = 0; k < _bins; k++)
        {
            var left = gains[Math.Max(0, k - 1)];
            var right = gains[Math.Min(_bins - 1, k + 1)];
            var g = 0.25f * left + 0.5f * gains[k] + 0.25f * right;
            if (g < _prevGain[k]) g = 0.55f * _prevGain[k] + 0.45f * g;
            g = Math.Clamp(g, floor, 1f);
            var power = _re[k] * _re[k] + _im[k] * _im[k];
            _prevClean[k] = power * g * g;
            _prevGain[k] = g;
            _re[k] *= g;
            _im[k] *= g;
            if (k > 0 && k < _n / 2)
            {
                _re[_n - k] = _re[k];
                _im[_n - k] = -_im[k];
            }
        }
        Fft(true);

        for (var i = 0; i < _n; i++) _outAcc[i] += _re[i] * _window[i];
        for (var i = 0; i < _hop; i++) _ready.Enqueue(_outAcc[i]);
        Array.Copy(_outAcc, _hop, _outAcc, 0, _hop);
        Array.Clear(_outAcc, _hop, _hop);
    }
}

/// <summary>One sound device Windows lists (the names it gives to this older interface are cut at 31 characters).</summary>
public sealed record AudioDevice(int Index, string Name);

/// <summary>Takes the microphone, cleans it with <see cref="NoiseSuppressor"/> and plays the result to an output device.
/// Pointing that output at a virtual audio cable makes the cleaned voice available to other programs as a microphone.
/// Audio is only held in small buffers for a few milliseconds; nothing is stored, recorded or sent.</summary>
public sealed class VoiceFilter : IDisposable
{
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct InCaps
    {
        public ushort Mid, Pid;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        public uint Formats;
        public ushort Channels, Reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OutCaps
    {
        public ushort Mid, Pid;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        public uint Formats;
        public ushort Channels, Reserved;
        public uint Support;
    }

    [DllImport("winmm.dll")] private static extern uint waveInGetNumDevs();
    [DllImport("winmm.dll")] private static extern uint waveOutGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] private static extern int waveInGetDevCapsW(UIntPtr device, out InCaps caps, int size);
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] private static extern int waveOutGetDevCapsW(UIntPtr device, out OutCaps caps, int size);
    [DllImport("winmm.dll")] private static extern int waveInOpen(out IntPtr handle, uint device, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] private static extern int waveInPrepareHeader(IntPtr handle, IntPtr header, int size);
    [DllImport("winmm.dll")] private static extern int waveInUnprepareHeader(IntPtr handle, IntPtr header, int size);
    [DllImport("winmm.dll")] private static extern int waveInAddBuffer(IntPtr handle, IntPtr header, int size);
    [DllImport("winmm.dll")] private static extern int waveInStart(IntPtr handle);
    [DllImport("winmm.dll")] private static extern int waveInReset(IntPtr handle);
    [DllImport("winmm.dll")] private static extern int waveInClose(IntPtr handle);
    [DllImport("winmm.dll")] private static extern int waveOutOpen(out IntPtr handle, uint device, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] private static extern int waveOutPrepareHeader(IntPtr handle, IntPtr header, int size);
    [DllImport("winmm.dll")] private static extern int waveOutUnprepareHeader(IntPtr handle, IntPtr header, int size);
    [DllImport("winmm.dll")] private static extern int waveOutWrite(IntPtr handle, IntPtr header, int size);
    [DllImport("winmm.dll")] private static extern int waveOutReset(IntPtr handle);
    [DllImport("winmm.dll")] private static extern int waveOutClose(IntPtr handle);

    private const uint Mapper = 0xFFFFFFFF, CallbackEvent = 0x50000, Done = 1;
    private const int Buffers = 8;

    public static List<AudioDevice> Inputs()
    {
        var list = new List<AudioDevice>();
        for (var i = 0; i < waveInGetNumDevs(); i++)
            if (waveInGetDevCapsW((UIntPtr)i, out var caps, Marshal.SizeOf<InCaps>()) == 0) list.Add(new AudioDevice(i, caps.Name));
        return list;
    }

    public static List<AudioDevice> Outputs()
    {
        var list = new List<AudioDevice>();
        for (var i = 0; i < waveOutGetNumDevs(); i++)
            if (waveOutGetDevCapsW((UIntPtr)i, out var caps, Marshal.SizeOf<OutCaps>()) == 0) list.Add(new AudioDevice(i, caps.Name));
        return list;
    }

    /// <summary>A virtual audio cable among the outputs (VB-Audio "CABLE Input" and similar), if one is installed.</summary>
    public static AudioDevice? VirtualCable(IEnumerable<AudioDevice> outputs) =>
        outputs.FirstOrDefault(d => d.Name.StartsWith("CABLE Input", StringComparison.OrdinalIgnoreCase) || d.Name.Contains("Virtual Cable", StringComparison.OrdinalIgnoreCase)
            || d.Name.StartsWith("VoiceMeeter Input", StringComparison.OrdinalIgnoreCase) || d.Name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase));

    private Thread? _thread;
    private volatile bool _stop;
    private readonly NoiseSuppressor?[] _suppressor = new NoiseSuppressor?[1];

    public bool Running => _thread is { IsAlive: true };
    /// <summary>Loudness of the last block before and after cleaning, 0 to 1 on a square-root scale (for the meters).</summary>
    public double InLevel { get; private set; }
    public double OutLevel { get; private set; }
    /// <summary>Average reduction in dB over the last moments (positive = quieter).</summary>
    public double ReductionDb { get; private set; }
    public string? Error { get; private set; }
    public double Strength
    {
        get => _suppressor[0]?.Strength ?? _strength;
        set { _strength = Math.Clamp(value, 0, 1); if (_suppressor[0] is { } s) s.Strength = _strength; }
    }
    private double _strength = 0.65;

    /// <summary>Starts cleaning. <paramref name="input"/> and <paramref name="output"/> are device indexes (null = the Windows default).
    /// Returns an error text, or null when it started.</summary>
    public string? Start(int? input, int? output, double strength)
    {
        Stop();
        Error = null;
        _strength = Math.Clamp(strength, 0, 1);
        var started = new ManualResetEventSlim();
        _failure = null;
        _stop = false;
        _thread = new Thread(() => Run(input, output, started)) { IsBackground = true, Name = "voice-filter", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
        started.Wait(3000);
        var failure = _failure;
        if (failure is not null)
        {
            Error = failure;
            _thread = null;
        }
        return failure;
    }

    public void Stop()
    {
        _stop = true;
        _thread?.Join(1500);
        _thread = null;
        InLevel = OutLevel = ReductionDb = 0;
    }

    public void Dispose() => Stop();

    private static double Level(float[] block, int count)
    {
        double sum = 0;
        for (var i = 0; i < count; i++) sum += block[i] * block[i];
        return Math.Sqrt(sum / Math.Max(1, count));
    }

    private string? _failure;

    private void Run(int? input, int? output, ManualResetEventSlim started)
    {
        IntPtr waveIn = IntPtr.Zero, waveOut = IntPtr.Zero;
        var inHeaders = new IntPtr[Buffers];
        var outHeaders = new IntPtr[Buffers];
        var inData = new IntPtr[Buffers];
        var outData = new IntPtr[Buffers];
        var outBusy = new bool[Buffers];
        var headerSize = Marshal.SizeOf<WaveHeader>();
        var flagsOffset = (int)Marshal.OffsetOf<WaveHeader>(nameof(WaveHeader.Flags));
        var recordedOffset = (int)Marshal.OffsetOf<WaveHeader>(nameof(WaveHeader.BytesRecorded));
        using var inEvent = new AutoResetEvent(false);
        using var outEvent = new AutoResetEvent(false);
        try
        {
            // 48 kHz is what most microphones run at natively; fall back to the rates every device accepts
            WaveFormat Format(int rate) => new() { Tag = 1, Channels = 1, SamplesPerSec = (uint)rate, AvgBytesPerSec = (uint)(rate * 2), BlockAlign = 2, BitsPerSample = 16 };
            var rate = 0;
            foreach (var candidate in new[] { 48000, 44100, 16000 })
            {
                var format = Format(candidate);
                if (waveInOpen(out waveIn, input is { } i ? (uint)i : Mapper, ref format, inEvent.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CallbackEvent) != 0) continue;
                var outFormat = Format(candidate);
                if (waveOutOpen(out waveOut, output is { } o ? (uint)o : Mapper, ref outFormat, outEvent.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CallbackEvent) != 0)
                {
                    waveInClose(waveIn);
                    waveIn = IntPtr.Zero;
                    continue;
                }
                rate = candidate;
                break;
            }
            if (rate == 0)
            {
                _failure = L.T("میکروفون یا خروجی باز نشد (ممکنه قطع باشه، دست برنامه‌ی دیگه‌ای باشه یا دسترسی میکروفون بسته باشه).",
                    "The microphone or the output could not be opened (it may be unplugged, in exclusive use, or blocked in Windows privacy settings).");
                return;
            }

            var block = rate / 100; // 10 ms
            var suppressor = new NoiseSuppressor(rate) { Strength = _strength };
            _suppressor[0] = suppressor;
            for (var i = 0; i < Buffers; i++)
            {
                inData[i] = Marshal.AllocHGlobal(block * 2);
                outData[i] = Marshal.AllocHGlobal(block * 2);
                inHeaders[i] = Marshal.AllocHGlobal(headerSize);
                outHeaders[i] = Marshal.AllocHGlobal(headerSize);
                Marshal.StructureToPtr(new WaveHeader { Data = inData[i], BufferLength = (uint)(block * 2) }, inHeaders[i], false);
                Marshal.StructureToPtr(new WaveHeader { Data = outData[i], BufferLength = (uint)(block * 2) }, outHeaders[i], false);
                waveInPrepareHeader(waveIn, inHeaders[i], headerSize);
                waveInAddBuffer(waveIn, inHeaders[i], headerSize);
                waveOutPrepareHeader(waveOut, outHeaders[i], headerSize);
            }
            waveInStart(waveIn);
            started.Set();

            var samples = new short[block];
            var floats = new float[block];
            var cleaned = new float[block];
            var next = 0;
            var smoothIn = 0.0;
            var smoothOut = 0.0;
            // a short cushion of silence so playback never runs dry between blocks
            for (var pre = 0; pre < 2; pre++) Play(Array.Empty<short>(), block);

            while (!_stop)
            {
                inEvent.WaitOne(40);
                while ((Marshal.ReadInt32(inHeaders[next], flagsOffset) & Done) != 0)
                {
                    var recorded = (int)(uint)Marshal.ReadInt32(inHeaders[next], recordedOffset) / 2;
                    var count = Math.Min(recorded, block);
                    Marshal.Copy(inData[next], samples, 0, count);
                    for (var i = 0; i < count; i++) floats[i] = samples[i] / 32768f;
                    suppressor.Process(floats.AsSpan(0, count), cleaned.AsSpan(0, count));
                    var before = Level(floats, count);
                    var after = Level(cleaned, count);
                    smoothIn = smoothIn * 0.6 + before * 0.4;
                    smoothOut = smoothOut * 0.6 + after * 0.4;
                    InLevel = Math.Sqrt(Math.Min(1, smoothIn * 4));
                    OutLevel = Math.Sqrt(Math.Min(1, smoothOut * 4));
                    ReductionDb = smoothIn > 1e-5 ? Math.Max(0, 20 * Math.Log10(smoothIn / Math.Max(smoothOut, 1e-6))) : 0;

                    var shorts = new short[count];
                    for (var i = 0; i < count; i++) shorts[i] = (short)Math.Clamp(cleaned[i] * 32767f, short.MinValue, short.MaxValue);
                    Play(shorts, block);

                    Marshal.WriteInt32(inHeaders[next], flagsOffset, Marshal.ReadInt32(inHeaders[next], flagsOffset) & ~(int)Done);
                    Marshal.WriteInt32(inHeaders[next], recordedOffset, 0);
                    waveInAddBuffer(waveIn, inHeaders[next], headerSize);
                    next = (next + 1) % Buffers;
                }
            }

            void Play(short[] data, int length)
            {
                for (var i = 0; i < Buffers; i++)
                {
                    if (outBusy[i] && (Marshal.ReadInt32(outHeaders[i], flagsOffset) & Done) == 0) continue;
                    var padded = new short[length];
                    Array.Copy(data, padded, Math.Min(data.Length, length));
                    Marshal.Copy(padded, 0, outData[i], length);
                    Marshal.WriteInt32(outHeaders[i], flagsOffset, Marshal.ReadInt32(outHeaders[i], flagsOffset) & ~(int)Done);
                    waveOutWrite(waveOut, outHeaders[i], headerSize);
                    outBusy[i] = true;
                    return;
                }
                // every buffer is still playing: this block is dropped rather than delayed
            }
        }
        catch (Exception ex)
        {
            _failure ??= ex.Message;
            Log.Write($"voice filter: {ex.GetType().Name}");
        }
        finally
        {
            _suppressor[0] = null;
            if (waveIn != IntPtr.Zero) { waveInReset(waveIn); }
            if (waveOut != IntPtr.Zero) { waveOutReset(waveOut); }
            for (var i = 0; i < Buffers; i++)
            {
                if (inHeaders[i] != IntPtr.Zero)
                {
                    if (waveIn != IntPtr.Zero) waveInUnprepareHeader(waveIn, inHeaders[i], headerSize);
                    Marshal.FreeHGlobal(inHeaders[i]);
                }
                if (outHeaders[i] != IntPtr.Zero)
                {
                    if (waveOut != IntPtr.Zero) waveOutUnprepareHeader(waveOut, outHeaders[i], headerSize);
                    Marshal.FreeHGlobal(outHeaders[i]);
                }
                if (inData[i] != IntPtr.Zero) Marshal.FreeHGlobal(inData[i]);
                if (outData[i] != IntPtr.Zero) Marshal.FreeHGlobal(outData[i]);
            }
            if (waveIn != IntPtr.Zero) waveInClose(waveIn);
            if (waveOut != IntPtr.Zero) waveOutClose(waveOut);
            started.Set();
        }
    }
}
