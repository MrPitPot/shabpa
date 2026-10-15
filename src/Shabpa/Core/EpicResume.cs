using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Shabpa.Core;

/// <summary>Epic Games Launcher keeps a manually paused download paused when it is reopened. This opens its
/// Downloads page (by clicking the download bar at the bottom) and presses the ▶ button when the queue shows it.
/// Like <see cref="SteamResume"/> it only needs an unlocked screen.</summary>
public static class EpicResume
{
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out Rect r);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr l);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);

    private delegate bool EnumProc(IntPtr h, IntPtr l);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    private const uint LeftDown = 0x0002, LeftUp = 0x0004;

    private static bool Near(Color a, Color b, int tolerance) =>
        Math.Abs(a.R - b.R) <= tolerance && Math.Abs(a.G - b.G) <= tolerance && Math.Abs(a.B - b.B) <= tolerance;

    private static int Luma(Color c) => (c.R * 3 + c.G * 6 + c.B) / 10;

    /// <summary>Looks for the first pair of equal-sized square buttons side by side (the ▶/⏸ and ✕ buttons of a
    /// download row). They are lighter than the card behind them, in grey when paused and tinted while running.
    /// Returns the centre of the left one and whether its glyph is a ▶ (the middle of the square is white).</summary>
    internal static (int x, int y, bool isPlay)? FindPlayButton(int width, int height, Func<int, int, Color> pixel)
    {
        var from = Math.Max(10, width / 8);
        var to = (int)(width * 0.8);
        for (var y = 90; y < Math.Min(height, 700); y += 2)
        {
            var x = from;
            while (x < to)
            {
                var seed = pixel(x, y);
                var seedLuma = Luma(seed);
                // A button is a flat patch clearly lighter than whatever lies left of it.
                if (seedLuma < 40 || Luma(pixel(x - 3, y)) > seedLuma - 15) { x++; continue; }
                var start = x;
                var backgroundLuma = Luma(pixel(x - 3, y));
                while (x < to && Near(pixel(x, y), seed, 8)) x++;
                var length = x - start;
                if (length < 24 || length > 44) continue;

                // After a narrow, darker gap comes a second patch of about the same width.
                var gapEnd = x;
                while (gapEnd < to && Luma(pixel(gapEnd, y)) <= backgroundLuma + 8 && gapEnd - x < 16) gapEnd++;
                var gap = gapEnd - x;
                if (gap < 4 || gap > 14) continue;
                var second = pixel(gapEnd + 2, y);
                if (Luma(second) < 40) continue;
                var secondEnd = gapEnd;
                while (secondEnd < to && Near(pixel(secondEnd, y), second, 8)) secondEnd++;
                if (Math.Abs((secondEnd - gapEnd) - length) > 8) continue;

                // Square, not a wide bar: about as tall as it is wide.
                var edge = start + 4;
                var top = y;
                while (top > 0 && Near(pixel(edge, top - 1), seed, 8)) top--;
                var bottom = y;
                while (bottom < height - 1 && Near(pixel(edge, bottom + 1), seed, 8)) bottom++;
                var tall = bottom - top + 1;
                if (tall < 24 || tall > 52) continue;

                var cx = start + length / 2;
                var cy = (top + bottom) / 2;
                // ▶ is white along the middle column; ⏸ is two bars with a dark gap exactly there.
                var white = 0;
                for (var dy = -3; dy <= 3; dy++)
                    if (pixel(cx, cy + dy) is var p && p.R > 190 && p.G > 190 && p.B > 190) white++;
                return (cx, cy, white >= 6);
            }
        }
        return null;
    }

    public static SteamResume.Outcome Run(CancellationToken ct = default)
    {
        var window = IntPtr.Zero;
        for (var i = 0; i < 60 && window == IntPtr.Zero && !ct.IsCancellationRequested; i++)
        {
            window = LargestWindow("EpicGamesLauncher");
            if (window == IntPtr.Zero) Thread.Sleep(1000);
        }
        if (window == IntPtr.Zero) return SteamResume.Outcome.NoWindow;

        if (IsIconic(window)) ShowWindow(window, 9);
        // Windows only lets the foreground program hand focus over; a tap of Alt makes that allowed.
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        keybd_event(0x12, 0, 2, UIntPtr.Zero);
        SetForegroundWindow(window);
        Thread.Sleep(1500);
        if (!GetWindowRect(window, out var rect)) return SteamResume.Outcome.NoWindow;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width < 600 || height < 400) return SteamResume.Outcome.NoWindow;

        // The Downloads page may already be open (clicking the bar again would close it): look first.
        if (Capture(rect) is null)
        {
            // The download bar sits at the bottom centre; clicking it opens the Downloads page.
            Click(rect.Left + width / 2, rect.Bottom - 38);
        }

        for (var i = 0; i < 12 && !ct.IsCancellationRequested; i++)
        {
            Thread.Sleep(1000);
            if (!GetWindowRect(window, out rect)) continue;
            if (Capture(rect) is not { } found) continue;
            if (!found.isPlay) return SteamResume.Outcome.AlreadyRunning;
            // The first click can be spent on giving the window focus; look again and press once more only if
            // the button still shows ▶ (a second press on a running download would pause it).
            for (var attempt = 0; attempt < 3; attempt++)
            {
                Click(rect.Left + found.x, rect.Top + found.y);
                Thread.Sleep(2000);
                if (!GetWindowRect(window, out rect)) break;
                if (Capture(rect) is not { } after) break;
                if (!after.isPlay) return SteamResume.Outcome.Resumed;
                found = after;
            }
            return SteamResume.Outcome.NothingQueued;
        }
        return SteamResume.Outcome.NothingQueued;
    }

    private static (int x, int y, bool isPlay)? Capture(Rect r)
    {
        var width = r.Right - r.Left;
        var height = r.Bottom - r.Top;
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
            g.CopyFromScreen(r.Left, r.Top, 0, 0, bitmap.Size);
        return FindPlayButton(width, height, bitmap.GetPixel);
    }

    private static void Click(int x, int y)
    {
        GetCursorPos(out var before);
        SetCursorPos(x - 3, y - 3);
        Thread.Sleep(80);
        SetCursorPos(x, y);
        mouse_event(0x0001, 1, 1, 0, UIntPtr.Zero); // a real move event so the page registers the hover
        Thread.Sleep(250);
        mouse_event(LeftDown, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(90);
        mouse_event(LeftUp, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(200);
        SetCursorPos(before.X, before.Y);
    }

    private static IntPtr LargestWindow(string processName)
    {
        var pids = Process.GetProcessesByName(processName).Select(p => (uint)p.Id).ToHashSet();
        if (pids.Count == 0) return IntPtr.Zero;
        var best = IntPtr.Zero;
        long bestArea = 0;
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out var pid);
            if (!pids.Contains(pid) || !GetWindowRect(h, out var r)) return true;
            var area = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
            if (area > bestArea && (IsWindowVisible(h) || IsIconic(h)))
            {
                bestArea = area;
                best = h;
            }
            return true;
        }, IntPtr.Zero);
        return bestArea >= 600 * 400 ? best : IntPtr.Zero;
    }
}
