using System.Runtime.InteropServices;

namespace Shabpa.Core;

/// <summary>The sound of a reminder: four quick high beeps and a pause, over and over like an alarm clock, until it is stopped. Made in memory (no file, no download) and
/// played by Windows' own PlaySound in the background. It does not depend on the little interface sounds, which are an option and often off: a reminder that makes no
/// sound is a reminder the person misses.</summary>
public static class AlarmTone
{
    private const int Rate = 44100;

    /// <summary>One round of the pattern as a WAV: <paramref name="beeps"/> beeps of 90 ms with 70 ms between them, then silence up to <paramref name="seconds"/>.</summary>
    public static byte[] Wav(int beeps = 4, double seconds = 1.8)
    {
        var count = (int)(Rate * seconds);
        var samples = new short[count];
        var beep = (int)(Rate * 0.09);
        var gap = (int)(Rate * 0.07);
        for (var b = 0; b < beeps; b++)
        {
            var start = b * (beep + gap);
            for (var i = 0; i < beep && start + i < count; i++)
            {
                var t = i / (double)Rate;
                var edge = Math.Min(1.0, Math.Min(i, beep - i) / (Rate * 0.008));        // 8 ms in and out: no clicks
                var tone = Math.Sin(2 * Math.PI * 1568 * t) + 0.35 * Math.Sin(2 * Math.PI * 3136 * t);
                samples[start + i] = (short)Math.Round(tone / 1.35 * edge * 0.6 * short.MaxValue);
            }
        }
        var data = new byte[44 + count * 2];
        void Text(int at, string s) { for (var k = 0; k < s.Length; k++) data[at + k] = (byte)s[k]; }
        void Int(int at, int v) => BitConverter.GetBytes(v).CopyTo(data, at);
        void Short(int at, short v) => BitConverter.GetBytes(v).CopyTo(data, at);
        Text(0, "RIFF"); Int(4, data.Length - 8); Text(8, "WAVE"); Text(12, "fmt "); Int(16, 16); Short(20, 1); Short(22, 1);
        Int(24, Rate); Int(28, Rate * 2); Short(32, 2); Short(34, 16); Text(36, "data"); Int(40, count * 2);
        for (var i = 0; i < count; i++) Short(44 + i * 2, samples[i]);
        return data;
    }

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW")]
    private static extern bool PlaySound(IntPtr data, IntPtr module, uint flags);

    private static GCHandle _pin;
    private static readonly object Gate = new();

    /// <summary>The alarm is sounding. Windows plays one PlaySound sound at a time, so every other sound (the little interface ticks, notice chimes, the focus bell, the Halloween sting) keeps
    /// quiet meanwhile: any of them would replace the looping alarm and silence it for good.</summary>
    public static volatile bool Active;

    /// <summary>Starts the alarm and keeps it going until <see cref="Stop"/>. False when Windows would not play it.</summary>
    public static bool Start()
    {
        try
        {
            lock (Gate)
            {
                if (!_pin.IsAllocated) _pin = GCHandle.Alloc(Wav(), GCHandleType.Pinned);
                Active = true;
                return PlaySound(_pin.AddrOfPinnedObject(), IntPtr.Zero, 0x4 | 0x1 | 0x8 | 0x2);   // memory, async, loop, no default beep
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }

    public static void Stop()
    {
        Active = false;
        try { lock (Gate) PlaySound(IntPtr.Zero, IntPtr.Zero, 0x40); }       // SND_PURGE
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }
}
