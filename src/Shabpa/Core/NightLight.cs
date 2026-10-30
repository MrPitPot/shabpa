using System.Runtime.InteropServices;
using WF = System.Windows.Forms;

namespace Shabpa.Core;

/// <summary>The warm colour of the night light, done the way Windows' own Night Light and f.lux do it: the display's colour
/// curves (gamma ramp) are scaled so white turns warm and the picture keeps its depth. A tinted plate laid over the screen
/// (the old way) only greys the picture yellow-brown. Windows refuses ramps that cut a channel below about half, so the blue
/// cut is limited to that; when a screen refuses even that, the caller falls back to the plate.</summary>
public static class NightLight
{
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateDC(string? driver, string device, string? port, IntPtr data);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool SetDeviceGammaRamp(IntPtr dc, [In] ushort[] ramp);

    /// <summary>The smallest share of a channel Windows lets through without a registry change (tested: 0.52 is accepted, 0.45 is not).</summary>
    public const double Floor = 0.52;

    private static bool _applied;

    /// <summary>The share (0..1) of red, green and blue that stays at a colour temperature; 6500 K and above change nothing.</summary>
    public static (double r, double g, double b) WhitePoint(int kelvin)
    {
        if (kelvin >= 6500) return (1, 1, 1);
        var (r, g, b) = Raw(Math.Max(kelvin, 1000));
        var (r0, g0, b0) = Raw(6500);
        return (Math.Clamp(r / r0, Floor, 1), Math.Clamp(g / g0, Floor, 1), Math.Clamp(b / b0, Floor, 1));
    }

    // Blackbody approximation of Tanner Helland (valid from 1000 K to 40000 K), channel values 0..255
    private static (double r, double g, double b) Raw(int kelvin)
    {
        var t = kelvin / 100.0;
        var r = t <= 66 ? 255 : 329.698727446 * Math.Pow(t - 60, -0.1332047592);
        var g = t <= 66 ? 99.4708025861 * Math.Log(t) - 161.1195681661 : 288.1221695283 * Math.Pow(t - 60, -0.0755148492);
        var b = t >= 66 ? 255 : t <= 19 ? 0 : 138.5177312231 * Math.Log(t - 10) - 305.0447927307;
        return (Math.Clamp(r, 1, 255), Math.Clamp(g, 1, 255), Math.Clamp(b, 1, 255));
    }

    /// <summary>The 3 x 256 table Windows wants: red, then green, then blue.</summary>
    public static ushort[] Ramp((double r, double g, double b) white)
    {
        var ramp = new ushort[768];
        for (var i = 0; i < 256; i++)
        {
            ramp[i] = (ushort)Math.Round(i * 257 * white.r);
            ramp[256 + i] = (ushort)Math.Round(i * 257 * white.g);
            ramp[512 + i] = (ushort)Math.Round(i * 257 * white.b);
        }
        return ramp;
    }

    /// <summary>Warms every screen. False when no screen took it (the caller then uses the plate instead).</summary>
    public static bool Apply(int kelvin)
    {
        var ramp = Ramp(WhitePoint(kelvin));
        var any = false;
        foreach (var screen in WF.Screen.AllScreens)
        {
            if (Set(screen.DeviceName, ramp)) any = true;
        }
        _applied = any || _applied;
        return any;
    }

    /// <summary>Puts every screen's colours back to normal. Safe to call at any time.</summary>
    public static void Reset()
    {
        if (!_applied) return;
        _applied = false;
        var plain = Ramp((1, 1, 1));
        try
        {
            foreach (var screen in WF.Screen.AllScreens) Set(screen.DeviceName, plain);
        }
        catch (Exception ex)
        {
            Log.Write($"night light reset: {ex.Message}");
        }
    }

    public static bool Active => _applied;

    private static bool Set(string device, ushort[] ramp)
    {
        var dc = IntPtr.Zero;
        try
        {
            dc = CreateDC(null, device, null, IntPtr.Zero);
            return dc != IntPtr.Zero && SetDeviceGammaRamp(dc, ramp);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or ExternalException)
        {
            return false;
        }
        finally
        {
            if (dc != IntPtr.Zero) DeleteDC(dc);
        }
    }
}
