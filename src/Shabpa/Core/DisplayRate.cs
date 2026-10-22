using System.Runtime.InteropServices;

namespace Shabpa.Core;

/// <summary>What the desktop is really doing when no game is drawing. Two honest numbers:
/// the monitor's refresh rate (what the screen can show, e.g. 165 Hz) and the rate at which Windows' compositor (DWM)
/// actually produced new frames in the last moments. The DWM figure is low on a still desktop because DWM only
/// composes when something on screen changes; it reaches the refresh rate while something animates smoothly.</summary>
public sealed class DisplayRate
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Ratio { public uint Num, Den; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DwmTiming
    {
        public uint cbSize;
        public Ratio rateRefresh;
        public ulong qpcRefreshPeriod;
        public Ratio rateCompose;
        public ulong qpcVBlank, cRefresh;
        public uint cDXRefresh;
        public ulong qpcCompose, cFrame;
        public uint cDXPresent;
        public ulong cRefreshFrame, cFrameSubmitted;
        public uint cDXPresentSubmitted;
        public ulong cFrameConfirmed;
        public uint cDXPresentConfirmed;
        public ulong cRefreshConfirmed;
        public uint cDXRefreshConfirmed;
        public ulong cFramesLate;
        public uint cFramesOutstanding;
        public ulong cFrameDisplayed, qpcFrameDisplayed, cRefreshFrameDisplayed, cFrameComplete, qpcFrameComplete, cFramePending, qpcFramePending,
            cFramesDisplayed, cFramesComplete, cFramesPending, cFramesAvailable, cFramesDropped, cFramesMissed, cRefreshNextDisplayed,
            cRefreshNextPresented, cRefreshesDisplayed, cRefreshesPresented, cRefreshStarted, cPixelsReceived, cPixelsDrawn, cBuffersEmpty;
    }

    [DllImport("dwmapi.dll")] private static extern int DwmGetCompositionTimingInfo(IntPtr hwnd, ref DwmTiming info);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettings(string? device, int mode, ref DevMode dev);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int cbSize;
        public int l, t, r, b, wl, wt, wr, wb;
        public uint flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string device;
    }

    [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode, Size = 220)]
    private struct DevMode
    {
        [FieldOffset(68)] public ushort dmSize;
        [FieldOffset(184)] public uint dmDisplayFrequency;
    }

    private ulong _lastFrames;
    private long _lastTicks;

    /// <summary>The refresh rate of the monitor that holds <paramref name="window"/> (the primary monitor when 0), or 0 if unknown.</summary>
    public static int MonitorHz(IntPtr window)
    {
        try
        {
            var monitor = MonitorFromWindow(window, 1 /* MONITOR_DEFAULTTOPRIMARY */);
            var info = new MonitorInfoEx { cbSize = Marshal.SizeOf<MonitorInfoEx>(), device = "" };
            string? device = null;
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info)) device = info.device;
            var mode = new DevMode { dmSize = 220 };
            return EnumDisplaySettings(device, -1 /* ENUM_CURRENT_SETTINGS */, ref mode) && mode.dmDisplayFrequency > 1 ? (int)mode.dmDisplayFrequency : 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            return 0;
        }
    }

    /// <summary>Frames DWM composed per second since the previous call (about once a second); 0 until two readings exist.</summary>
    public double ComposeFps()
    {
        try
        {
            var info = new DwmTiming { cbSize = (uint)Marshal.SizeOf<DwmTiming>() };
            if (DwmGetCompositionTimingInfo(IntPtr.Zero, ref info) != 0) return 0;
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            var rate = 0.0;
            if (_lastTicks != 0 && info.cFrame >= _lastFrames)
            {
                var seconds = (now - _lastTicks) / (double)System.Diagnostics.Stopwatch.Frequency;
                if (seconds > 0.2) rate = (info.cFrame - _lastFrames) / seconds;
            }
            _lastFrames = info.cFrame;
            _lastTicks = now;
            return rate;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            return 0;
        }
    }
}
