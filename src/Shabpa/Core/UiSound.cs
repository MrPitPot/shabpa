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
    public enum Kind { Hover, Click, On, Off, Notice }

    private static readonly Dictionary<Kind, (byte[] data, GCHandle pin)> Cache = [];
    private static long _lastHover;
    private static bool _installed;

    public static bool Enabled => Hub.I is { } hub && hub.Settings.UiSounds && !hub.Settings.LowMemory && hub.Snapshot.GameProcess.Length == 0;

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

    private static (byte[], GCHandle) Make(Kind kind)
    {
        var data = kind switch
        {
            Kind.Hover => Tone(2300, 26, 0.045, 2100),
            Kind.Click => Tone(1500, 38, 0.10, 1100),
            Kind.On => Tone(1250, 70, 0.11, 1900),
            Kind.Off => Tone(1500, 70, 0.10, 950),
            _ => Tone(880, 220, 0.14, 1320),
        };
        return (data, GCHandle.Alloc(data, GCHandleType.Pinned));
    }

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW")]
    private static extern bool PlaySound(IntPtr data, IntPtr module, uint flags);

    private const uint Memory = 0x4, Async = 0x1, NoDefault = 0x2;

    public static void Play(Kind kind)
    {
        try
        {
            if (!Enabled) return;
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
