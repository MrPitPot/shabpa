using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Shabpa.Core;

/// <summary>Very small interface sounds (experimental): a soft tick when the mouse moves over a button or option, a
/// click when it is pressed and a short chime for notices. The sounds are generated in memory, so there are no audio
/// files and nothing is loaded from disk. They stay silent in "Use less RAM" mode, while a game runs and when the
/// person turned them off in Settings.</summary>
public static class UiSound
{
    public enum Kind { Hover, Click, On, Off, Notice, Medal }

    private static readonly Dictionary<Kind, (byte[] data, GCHandle pin)> Cache = [];
    private static long _lastHover;
    private static bool _installed;

    public static bool Enabled => Hub.I is { } hub && hub.Settings.UiSounds && !Halloween.Playing && !hub.Settings.LowMemory && hub.Snapshot.GameProcess.Length == 0;

    /// <summary>A mono 16-bit WAV of a short sine blip with a quick fade in and a smooth fade out.</summary>
    internal static byte[] Tone(double hz, double milliseconds, double volume, double hz2 = 0, int rate = 44100)
    {
        var count = (int)(rate * milliseconds / 1000);
        var data = new byte[44 + count * 2];
        void Text(int at, string s) { for (var i = 0; i < s.Length; i++) data[at + i] = (byte)s[i]; }
        void Int(int at, int v) => BitConverter.GetBytes(v).CopyTo(data, at);
        void Short(int at, short v) => BitConverter.GetBytes(v).CopyTo(data, at);
        Text(0, "RIFF"); Int(4, data.Length - 8); Text(8, "WAVE"); Text(12, "fmt "); Int(16, 16); Short(20, 1); Short(22, 1);
        Int(24, rate); Int(28, rate * 2); Short(32, 2); Short(34, 16); Text(36, "data"); Int(40, count * 2);
        var phase = 0.0;
        for (var i = 0; i < count; i++)
        {
            var t = i / (double)count;
            var envelope = Math.Min(1, i / (rate * 0.004)) * Math.Pow(1 - t, 2.2);
            var f = hz2 > 0 ? hz + (hz2 - hz) * t : hz;
            phase += 2 * Math.PI * f / rate;
            var sample = (Math.Sin(phase) + 0.25 * Math.Sin(phase * 2)) / 1.25 * envelope * volume;
            Short(44 + i * 2, (short)Math.Round(sample * short.MaxValue));
        }
        return data;
    }

    /// <summary>A mono 16-bit WAV from samples in -1..1, with a 2 ms fade at the end so nothing clicks where it stops.</summary>
    internal static byte[] Wav(double[] samples, int rate = 44100)
    {
        var count = samples.Length;
        var data = new byte[44 + count * 2];
        void Text(int at, string s) { for (var i = 0; i < s.Length; i++) data[at + i] = (byte)s[i]; }
        void Int(int at, int v) => BitConverter.GetBytes(v).CopyTo(data, at);
        void Short(int at, short v) => BitConverter.GetBytes(v).CopyTo(data, at);
        Text(0, "RIFF"); Int(4, data.Length - 8); Text(8, "WAVE"); Text(12, "fmt "); Int(16, 16); Short(20, 1); Short(22, 1);
        Int(24, rate); Int(28, rate * 2); Short(32, 2); Short(34, 16); Text(36, "data"); Int(40, count * 2);
        var fade = Math.Max(1, rate * 2 / 1000);
        for (var i = 0; i < count; i++)
        {
            var v = Math.Clamp(samples[i], -1, 1) * Math.Min(1, (count - i) / (double)fade);
            Short(44 + i * 2, (short)Math.Round(v * short.MaxValue));
        }
        return data;
    }

    /// <summary>One physical-sounding "tick": a sharp, very short bright transient (the click of a switch) on top of a small tuned body whose pitch drops
    /// quickly and fades (the little "tock" under it). Generated from maths; <paramref name="bodyHz"/> to <paramref name="bodyEndHz"/> is the pitch fall,
    /// <paramref name="click"/> the loudness of the transient against the body.</summary>
    internal static double[] Tick(double bodyHz, double bodyEndHz, double milliseconds, double click, double volume, int rate = 44100, int seed = 7)
    {
        var count = (int)(rate * milliseconds / 1000);
        var samples = new double[count];
        var rng = new Random(seed);
        double phase = 0, previous = 0;
        var decay = 5.0 / (milliseconds / 1000);
        for (var i = 0; i < count; i++)
        {
            var t = i / (double)rate;
            var f = bodyEndHz + (bodyHz - bodyEndHz) * Math.Exp(-t * 90);
            phase += 2 * Math.PI * f / rate;
            var body = Math.Sin(phase) * Math.Exp(-t * decay) * Math.Min(1, i / (rate * 0.0008));
            var noise = rng.NextDouble() * 2 - 1;
            var bright = noise - previous;                              // a differentiator keeps only the high, crisp part of the noise
            previous = noise;
            samples[i] = (body * 0.8 + bright * 0.5 * click * Math.Exp(-t * 1400)) * volume;
        }
        return samples;
    }

    private static double[] Then(double[] first, double[] second, double gapMs, int rate = 44100)
    {
        var offset = (int)(rate * gapMs / 1000);
        var result = new double[Math.Max(first.Length, offset + second.Length)];
        first.CopyTo(result, 0);
        for (var i = 0; i < second.Length; i++) result[offset + i] += second[i];
        return result;
    }
    /// <summary>One small bell note: a sine with a soft, inharmonic overtone (what makes it sound like a bell, not a beep), a 3 ms
    /// attack and an exponential fade.</summary>
    internal static double[] Bell(double hz, double milliseconds, double volume, int rate = 44100)
    {
        var count = (int)(rate * milliseconds / 1000);
        var samples = new double[count];
        var decay = 4.5 / (milliseconds / 1000);
        for (var i = 0; i < count; i++)
        {
            var t = i / (double)rate;
            var envelope = Math.Min(1, i / (rate * 0.003)) * Math.Exp(-t * decay);
            var tone = Math.Sin(2 * Math.PI * hz * t) + 0.35 * Math.Sin(2 * Math.PI * hz * 2.76 * t) * Math.Exp(-t * 18) + 0.12 * Math.Sin(2 * Math.PI * hz * 4 * t);
            samples[i] = tone / 1.47 * envelope * volume;
        }
        return samples;
    }

    /// <summary>The medal jingle: a quick rising "ta-da" of four bell notes (C E G C), the last one left to ring, with a
    /// tiny high sparkle on top. About 0.8 s.</summary>
    internal static double[] Jingle()
    {
        double[] notes = [1046.5, 1318.5, 1568.0, 2093.0];
        var mix = Bell(notes[0], 260, 0.15);
        for (var i = 1; i < notes.Length; i++)
            mix = Then(mix, Bell(notes[i], i == notes.Length - 1 ? 620 : 260, i == notes.Length - 1 ? 0.17 : 0.14), 80 * i);
        return Then(mix, Bell(4186, 300, 0.035), 300);
    }

    private static (byte[], GCHandle) Make(Kind kind)
    {
        // satisfying, not beepy: tiny switch-like ticks. Hover is the lightest, a click is a soft "thock", on rises and off falls as a pair of ticks.
        var data = kind switch
        {
            Kind.Hover => Wav(Tick(3300, 2300, 18, 1.0, 0.075)),
            Kind.Click => Wav(Tick(1100, 420, 52, 1.0, 0.16)),
            Kind.On => Wav(Then(Tick(1500, 900, 40, 0.9, 0.13), Tick(2300, 1500, 46, 0.9, 0.14), 46)),
            Kind.Off => Wav(Then(Tick(2300, 1500, 40, 0.9, 0.13), Tick(1300, 760, 52, 0.9, 0.14), 46)),
            Kind.Medal => Wav(Jingle()),
            _ => Tone(880, 220, 0.14, 1320),
        };
        return (data, GCHandle.Alloc(data, GCHandleType.Pinned));
    }

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW")]
    private static extern bool PlaySound(IntPtr data, IntPtr module, uint flags);

    private const uint Memory = 0x4, Async = 0x1, NoDefault = 0x2;

    /// <summary>The medal jingle has its own switch (on by default), so turning off the hover ticks does not silence it; it
    /// still stays quiet during games and in "Use less RAM" mode.</summary>
    public static bool MedalEnabled => Hub.I is { } hub && hub.Settings.MedalSound && !Halloween.Playing && !hub.Settings.LowMemory && hub.Snapshot.GameProcess.Length == 0;

    public static void Play(Kind kind)
    {
        try
        {
            if (!(kind == Kind.Medal ? MedalEnabled : Enabled)) return;
            if (!Cache.TryGetValue(kind, out var entry)) Cache[kind] = entry = Make(kind);
            PlaySound(entry.pin.AddrOfPinnedObject(), IntPtr.Zero, Memory | Async | NoDefault);
        }
        catch
        {
            // a missing sound device must never bother anything else
        }
    }

    /// <summary>Listens to the whole app once, so no page has to ask for a sound.</summary>
    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        EventManager.RegisterClassHandler(typeof(ButtonBase), UIElement.MouseEnterEvent, new System.Windows.Input.MouseEventHandler(OnEnter));
        EventManager.RegisterClassHandler(typeof(ListBoxItem), UIElement.MouseEnterEvent, new System.Windows.Input.MouseEventHandler(OnEnter));
        EventManager.RegisterClassHandler(typeof(ButtonBase), ButtonBase.ClickEvent, new RoutedEventHandler(OnClick));
    }

    private static void OnEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!Enabled || sender is not UIElement { IsEnabled: true }) return;
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) return; // dragging
        var now = Environment.TickCount64;
        if (now - _lastHover < 55) return; // sweeping across a row is a flutter, not a machine gun
        _lastHover = now;
        Play(Kind.Hover);
    }

    private static void OnClick(object sender, RoutedEventArgs e)
    {
        if (!Enabled) return;
        Play(sender switch
        {
            ToggleButton { IsChecked: true } and not RadioButton => Kind.On,
            ToggleButton { IsChecked: false } => Kind.Off,
            _ => Kind.Click,
        });
    }
}
