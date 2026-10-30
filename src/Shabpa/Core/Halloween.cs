using System.Globalization;
using System.Runtime.InteropServices;

namespace Shabpa.Core;

/// <summary>The Halloween season: a few days around 31 October the app wears an orange-and-violet look and, the first time the window
/// opens that year, shows a short jump scare (one flash, one sound) and a greeting. All of it is drawn and generated in the app: no
/// picture or sound file, nothing from the network. It is on by default and one switch in Settings turns it off.</summary>
public static class Halloween
{
    /// <summary>28 October to 1 November.</summary>
    public static bool InSeason(DateTime now) => now.Month == 10 && now.Day >= 28 || now.Month == 11 && now.Day == 1;

    /// <summary>The scare and greeting are due: the season, the option on, and not shown yet this year.</summary>
    public static bool Due(bool enabled, int seenYear, DateTime now) => enabled && InSeason(now) && seenYear != now.Year;

    // ---- texts -------------------------------------------------------------------------------------------------

    private static readonly (string fa, string en)[] Wishes =
    [
        ("هالووین مبارک! 🎃 امشب هر صدایی از پشت سرت اومد، فقط شب‌پاست که داره حواسش به سیستمت هست.", "Happy Halloween! 🎃 Whatever creaks behind you tonight, it is only Shabpa keeping an eye on your PC."),
        ("هالووین مبارک! 👻 قول می‌دم تنها چیزی که امشب از سیستمت می‌ترسه، دمای بالای فنه.", "Happy Halloween! 👻 The only thing your PC should fear tonight is a hot fan curve."),
        ("هالووین مبارک! 🦇 شب‌پا امشب بیداره؛ هر وقت خواستی خاموشش می‌کنه، حتی اگه شبح باشی.", "Happy Halloween! 🦇 Shabpa is awake tonight; it will switch the PC off whenever you say, ghost or not."),
    ];

    /// <summary>One greeting for the day: the same text all day, another one the next day.</summary>
    public static string Wish(DateTime now, bool? english = null)
    {
        var w = Wishes[(now.Day + now.Month) % Wishes.Length];
        return (english ?? L.En) ? w.en : w.fa;
    }

    public static string Title(bool? english = null) => (english ?? L.En) ? "Happy Halloween! 🎃" : "هالووین مبارک! 🎃";

    // ---- the festive palette ----------------------------------------------------------------------------------------

    private static readonly Dictionary<string, Palette> Tints = [];

    /// <summary>The same theme with deep violet backgrounds and pumpkin-orange accents. Like the birthday look it has its own key,
    /// remembers the real theme in <see cref="Palette.BaseKey"/> and never changes the saved theme choice.</summary>
    public static Palette Tint(Palette basePalette)
    {
        if (basePalette.BaseKey is { } real) basePalette = Palette.Find(real);
        lock (Tints)
        {
            if (Tints.TryGetValue(basePalette.Key, out var cached)) return cached;
            var c = new Dictionary<string, string>(basePalette.Colors);
            void Mix(string key, uint towards, double t) => c[key] = GlassTokens.Mix(GlassTokens.Rgb(c[key]), towards, t).ToString("X6", CultureInfo.InvariantCulture);
            if (!basePalette.Light)
            {
                foreach (var key in new[] { "Bg", "Bg2", "Side", "Side2" }) Mix(key, 0x1A0B24, 0.45);
                foreach (var key in new[] { "Panel", "Panel2", "Panel3", "Thumb", "Line" }) Mix(key, 0x2E1245, 0.30);
                Set(c, "Accent=FF8A1F Accent1=FF7A00 Accent2=A855F7 AccentSoft=3D1F0A OnAccent=1A0B00 Halo=FF7A00 Glow=FFFFFF " +
                       "Gold=FFB347 GoldSoft=3A260E Hero=4A1D05 Hero2=2A1054");
            }
            else
            {
                foreach (var key in new[] { "Bg", "Bg2", "Side", "Side2", "Panel2", "Panel3", "Line" }) Mix(key, 0xFFE3C2, 0.25);
                Set(c, "Accent=C2410C Accent1=EA580C Accent2=7E22CE AccentSoft=FFE8D0 OnAccent=FFFFFF Halo=FB923C Glow=000000 " +
                       "Gold=8A5600 GoldSoft=FFEFC2 Hero=FFE3C2 Hero2=EBD7FF");
            }
            var tinted = new Palette(basePalette.Key + "-halloween", basePalette.Fa, basePalette.En, basePalette.Light, c) { BaseKey = basePalette.Key };
            return Tints[basePalette.Key] = tinted;
        }

        static void Set(Dictionary<string, string> colors, string spec)
        {
            foreach (var pair in spec.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=');
                colors[kv[0]] = kv[1];
            }
        }
    }

    // ---- the scream -----------------------------------------------------------------------------------------------

    /// <summary>A mono 16-bit WAV of a short jump-scare sting made from maths alone: a sudden low thud, a rising shriek made of a few
    /// detuned, wobbling voices with rough noise, and a fast fade. About 1.3 seconds, peak level capped well below full scale.</summary>
    public static byte[] ScreamWav(int rate = 44100)
    {
        const double seconds = 1.3, peak = 0.62;
        var count = (int)(rate * seconds);
        var samples = new double[count];
        var rng = new Random(31);                                  // fixed seed: the same sound every time
        double p1 = 0, p2 = 0, p3 = 0, lowpass = 0;
        for (var i = 0; i < count; i++)
        {
            var t = i / (double)rate;
            // thud: a 70 Hz sine falling to 40 Hz, gone in a quarter of a second
            var thud = Math.Sin(2 * Math.PI * (40 * t + 30 * (1 - Math.Exp(-t * 14)) / 14 * 2)) * Math.Exp(-t * 11) * 0.9;
            // shriek: rises from 700 Hz to 2100 Hz with a fast wobble, three voices a little apart
            var f = 700 + 1400 * Math.Min(1, t / 0.55) + 90 * Math.Sin(2 * Math.PI * 9 * t);
            p1 += 2 * Math.PI * f / rate;
            p2 += 2 * Math.PI * f * 1.013 / rate;
            p3 += 2 * Math.PI * f * 0.987 * 1.5 / rate;
            var voice = (Math.Sin(p1) + 0.7 * Math.Sin(p2) + 0.35 * Math.Sign(Math.Sin(p3))) / 2.05;
            var noise = rng.NextDouble() * 2 - 1;
            lowpass += 0.35 * (noise - lowpass);                    // rough breath, not hiss
            var shriekEnv = Math.Min(1, Math.Max(0, (t - 0.03) / 0.04)) * Math.Pow(Math.Max(0, 1 - t / seconds), 1.6);
            samples[i] = thud + (voice * 0.85 + lowpass * 0.5) * shriekEnv;
        }
        var top = samples.Max(Math.Abs);
        var data = new byte[44 + count * 2];
        void Text(int at, string s) { for (var k = 0; k < s.Length; k++) data[at + k] = (byte)s[k]; }
        void Int(int at, int v) => BitConverter.GetBytes(v).CopyTo(data, at);
        void Short(int at, short v) => BitConverter.GetBytes(v).CopyTo(data, at);
        Text(0, "RIFF"); Int(4, data.Length - 8); Text(8, "WAVE"); Text(12, "fmt "); Int(16, 16); Short(20, 1); Short(22, 1);
        Int(24, rate); Int(28, rate * 2); Short(32, 2); Short(34, 16); Text(36, "data"); Int(40, count * 2);
        for (var i = 0; i < count; i++) Short(44 + i * 2, (short)Math.Round(samples[i] / top * peak * short.MaxValue));
        return data;
    }

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW")]
    private static extern bool PlaySound(IntPtr data, IntPtr module, uint flags);

    private static GCHandle _pin;

    private static long _playingUntil;

    /// <summary>True while the sting plays: the small interface sounds keep quiet meanwhile, because Windows plays one sound at a time and a hover tick would cut the scream short.</summary>
    public static bool Playing => Environment.TickCount64 < _playingUntil;

    /// <summary>Plays the sting once, in the background. It does not depend on the little interface sounds (those are experimental and often off);
    /// the Halloween switch in Settings is its own consent. Silent (returns false) while a game runs or in "use less RAM" mode.</summary>
    public static bool PlayScream()
    {
        try
        {
            if (Hub.I is { } hub && (hub.Settings.LowMemory || hub.Snapshot.GameProcess.Length > 0)) return false;
            _playingUntil = Environment.TickCount64 + 1600;
            if (!_pin.IsAllocated) _pin = GCHandle.Alloc(ScreamWav(), GCHandleType.Pinned);
            return PlaySound(_pin.AddrOfPinnedObject(), IntPtr.Zero, 0x4 | 0x1 | 0x2); // memory, async, no default beep
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }
}
