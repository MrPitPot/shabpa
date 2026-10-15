using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Shabpa.Core;

/// <summary>Riot Client keeps a game that was paused (or is waiting for an update) behind a gold "Update" button on the
/// game's page; the games in that state wear a small gold badge in the sidebar. This opens such a game and presses
/// the button. Like the other launcher helpers it needs the screen unlocked.</summary>
public static class RiotResume
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
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);

    private delegate bool EnumProc(IntPtr h, IntPtr l);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    /// <summary>The little gold badge on a sidebar game icon (about 240,170,30).</summary>
    internal static bool IsBadge(Color c) => c.R >= 190 && c.G is >= 120 and <= 200 && c.B <= 70 && c.R - c.B >= 130;

    /// <summary>The big gold "Update" / "Resume" button (about 213,174,99).</summary>
    internal static bool IsGoldButton(Color c) => c.R is >= 190 and <= 240 && c.G is >= 145 and <= 195 && c.B is >= 55 and <= 140 && c.R - c.B >= 70;

    /// <summary>Centres of the gold badges in the sidebar strip, top to bottom.</summary>
    internal static List<(int x, int y)> FindBadges(int width, int height, Func<int, int, Color> pixel)
    {
        var strip = Math.Min(width, 90);
        var hits = new List<(int x, int y)>();
        for (var y = 120; y < height; y += 2)
            for (var x = 0; x < strip; x += 2)
                if (IsBadge(pixel(x, y))) hits.Add((x, y));
        // A badge hangs off the top-right of an icon (x about 49); gold glyphs of the selected icon sit further left.
        return Cluster(hits, 8, 4).Where(b => b.x is >= 44 and <= 62).OrderBy(b => b.y).ToList();
    }

    /// <summary>The centre of the biggest flat gold area in the page's upper left (the main action button), or null.</summary>
    internal static (int x, int y)? FindUpdateButton(int width, int height, Func<int, int, Color> pixel)
    {
        var right = Math.Min(width, 760);
        var bottom = Math.Min(height, 520);
        var hits = new List<(int x, int y)>();
        for (var y = 130; y < bottom; y += 3)
            for (var x = 100; x < right; x += 3)
                if (IsGoldButton(pixel(x, y))) hits.Add((x, y));
        var groups = Cluster(hits, 12, 60);
        return groups.Count == 0 ? null : groups[0];
    }

    /// <summary>Groups nearby hits and returns the centre of each group that has at least <paramref name="min"/> hits,
    /// biggest group first for buttons and top to bottom for badges (callers sort as needed).</summary>
    private static List<(int x, int y)> Cluster(List<(int x, int y)> hits, int reach, int min)
    {
        var groups = new List<List<(int x, int y)>>();
        foreach (var hit in hits)
        {
            List<(int x, int y)>? home = null;
            foreach (var group in groups.ToList())
            {
                if (!group.Any(p => Math.Abs(p.x - hit.x) <= reach && Math.Abs(p.y - hit.y) <= reach)) continue;
                if (home is null) { group.Add(hit); home = group; }
                else { home.AddRange(group); groups.Remove(group); }
            }
            if (home is null) groups.Add([hit]);
        }
        return [.. groups.Where(g => g.Count >= min).OrderByDescending(g => g.Count)
            .Select(g => ((int)g.Average(p => p.x), (int)g.Average(p => p.y)))];
    }

    public static SteamResume.Outcome Run(CancellationToken ct = default)
    {
        var window = IntPtr.Zero;
        for (var i = 0; i < 60 && window == IntPtr.Zero && !ct.IsCancellationRequested; i++)
        {
            window = LargestWindow("Riot Client");
            if (window == IntPtr.Zero) Thread.Sleep(1000);
        }
        if (window == IntPtr.Zero) return SteamResume.Outcome.NoWindow;

        if (IsIconic(window)) ShowWindow(window, 9);
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        keybd_event(0x12, 0, 2, UIntPtr.Zero);
        SetForegroundWindow(window);
        Thread.Sleep(2500); // the client may still be loading its pages

        var resumedAny = false;
        var tried = new HashSet<int>();
        for (var round = 0; round < 4 && !ct.IsCancellationRequested; round++)
        {
            if (!GetWindowRect(window, out var rect)) break;
            var badges = Capture(rect, (w, h, px) => FindBadges(w, h, px));
            var next = badges.FirstOrDefault(b => !tried.Contains(b.y / 24));
            if (badges.Count == 0 || next == default) break;
            tried.Add(next.y / 24);

            // The icon sits just below-left of its badge.
            Click(rect.Left + next.x - 9, rect.Top + next.y + 12);
            Thread.Sleep(2500);
            if (!GetWindowRect(window, out rect)) break;
            var button = Capture(rect, (w, h, px) => FindUpdateButton(w, h, px) is { } b ? [b] : []);
            if (button.Count == 0) continue;
            Click(rect.Left + button[0].x, rect.Top + button[0].y);
            Thread.Sleep(2500);
            resumedAny = true;
        }
        return resumedAny ? SteamResume.Outcome.Resumed : SteamResume.Outcome.NothingQueued;
    }

    private static List<(int x, int y)> Capture(Rect r, Func<int, int, Func<int, int, Color>, List<(int x, int y)>> find)
    {
        var width = r.Right - r.Left;
        var height = r.Bottom - r.Top;
        if (width < 500 || height < 300) return [];
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
            g.CopyFromScreen(r.Left, r.Top, 0, 0, bitmap.Size);
        return find(width, height, bitmap.GetPixel);
    }

    private static void Click(int x, int y)
    {
        GetCursorPos(out var before);
        SetCursorPos(x - 4, y - 4);
        Thread.Sleep(100);
        SetCursorPos(x, y);
        mouse_event(0x0001, 1, 1, 0, UIntPtr.Zero);
        Thread.Sleep(300);
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(90);
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
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
        return bestArea >= 500 * 300 ? best : IntPtr.Zero;
    }
}
