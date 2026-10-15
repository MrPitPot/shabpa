using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Shabpa.Core;

/// <summary>Steam has no command that resumes a paused download queue, so this does what a person would: opens the
/// Downloads page and presses the big round button when nothing is being downloaded. It looks for the button by
/// its colour, so it keeps working at any window size or scale. The screen must be unlocked at that moment.</summary>
public static class SteamResume
{
    public enum Outcome { Resumed, AlreadyRunning, NothingQueued, NoWindow, ScreenLocked }

    [DllImport("user32.dll")] private static extern IntPtr FindWindow(string? cls, string? title);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out Rect r);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    private const uint LeftDown = 0x0002, LeftUp = 0x0004;

    /// <summary>The round button's colour: a clear blue (about 40,135,225) and not the dark page around it.</summary>
    internal static bool IsButtonBlue(Color c) => c.B >= 190 && c.G is >= 95 and <= 175 && c.R <= 90 && c.B - c.R >= 110;

    /// <summary>Centre of the largest blue blob in <paramref name="pixels"/> (scanned on a coarse grid), or null.
    /// The button is a filled circle roughly 40 px across, so a blob needs a handful of hits to count.</summary>
    internal static (int x, int y)? FindButton(int width, int height, Func<int, int, Color> pixel)
    {
        const int Step = 3;
        var hits = new List<(int x, int y)>();
        for (var y = 0; y < height; y += Step)
            for (var x = 0; x < width; x += Step)
                if (IsButtonBlue(pixel(x, y))) hits.Add((x, y));
        if (hits.Count < 12) return null;

        // Group hits that touch (within two grid steps) and keep the biggest group.
        var groups = new List<List<(int x, int y)>>();
        foreach (var hit in hits)
        {
            List<(int x, int y)>? home = null;
            foreach (var group in groups)
            {
                if (!group.Any(p => Math.Abs(p.x - hit.x) <= Step * 2 && Math.Abs(p.y - hit.y) <= Step * 2)) continue;
                if (home is null) { group.Add(hit); home = group; }
                else { home.AddRange(group); group.Clear(); break; }
            }
            if (home is null) groups.Add([hit]);
            groups.RemoveAll(g => g.Count == 0);
        }
        var best = groups.OrderByDescending(g => g.Count).First();
        return best.Count < 12 ? null : ((int)best.Average(p => p.x), (int)best.Average(p => p.y));
    }

    /// <param name="alreadyDownloading">Asked right before clicking; true when data is flowing, so a click would pause it.</param>
    public static Outcome Run(Func<bool> alreadyDownloading, CancellationToken ct = default)
    {
        try
        {
            Process.Start(new ProcessStartInfo("steam://open/downloads") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Write($"steam resume: cannot open downloads: {ex.Message}");
            return Outcome.NoWindow;
        }

        // Steam may still be starting: wait for its main window.
        var window = IntPtr.Zero;
        for (var i = 0; i < 90 && window == IntPtr.Zero && !ct.IsCancellationRequested; i++)
        {
            window = FindWindow("SDL_app", "Steam");
            if (window == IntPtr.Zero) Thread.Sleep(1000);
        }
        if (window == IntPtr.Zero) return Outcome.NoWindow;

        if (IsIconic(window)) ShowWindow(window, 9);
        SetForegroundWindow(window);

        // The page needs a moment to draw; look for the button for up to 25 seconds.
        (int x, int y)? button = null;
        Rect rect = default;
        for (var i = 0; i < 25 && button is null && !ct.IsCancellationRequested; i++)
        {
            Thread.Sleep(1000);
            if (!GetWindowRect(window, out rect)) continue;
            button = Locate(rect);
        }
        if (button is null) return Outcome.NothingQueued;

        // Data already flowing (somebody pressed resume, or the queue was never paused): leave it alone.
        if (alreadyDownloading()) return Outcome.AlreadyRunning;

        GetCursorPos(out var before);
        SetCursorPos(rect.Left + button.Value.x, rect.Top + button.Value.y);
        Thread.Sleep(120);
        mouse_event(LeftDown, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(60);
        mouse_event(LeftUp, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(150);
        SetCursorPos(before.X, before.Y);
        return Outcome.Resumed;
    }

    /// <summary>True when Steam's own content log shows a download rate within the last 20 seconds.</summary>
    public static bool LogShowsDownload(string steamFolder, DateTime now)
    {
        try
        {
            var file = System.IO.Path.Combine(steamFolder, "logs", "content_log.txt");
            using var stream = new System.IO.FileStream(file, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete);
            var tail = Math.Min(stream.Length, 16384);
            stream.Seek(-tail, System.IO.SeekOrigin.End);
            using var reader = new System.IO.StreamReader(stream);
            return LastDownloadRateAt(reader.ReadToEnd()) is { } at && now - at < TimeSpan.FromSeconds(20) && now - at > TimeSpan.FromSeconds(-5);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The time of the last "Current download rate" line in a piece of content log, or null.</summary>
    internal static DateTime? LastDownloadRateAt(string log)
    {
        DateTime? last = null;
        foreach (var line in log.Split((char)10))
        {
            if (!line.Contains("Current download rate", StringComparison.Ordinal) || line.Length < 21 || line[0] != '[') continue;
            if (DateTime.TryParseExact(line.AsSpan(1, 19), "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var at))
                last = at;
        }
        return last;
    }

    private static (int x, int y)? Locate(Rect r)
    {
        var width = r.Right - r.Left;
        var height = r.Bottom - r.Top;
        if (width < 300 || height < 300) return null;
        // The button sits in the header, at the far right, within the top fifth of the page.
        var stripX = Math.Max(0, width - 200);
        var stripY = 60;
        var stripH = Math.Min(height - stripY, 360);
        using var bitmap = new Bitmap(200, stripH, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
            g.CopyFromScreen(r.Left + stripX, r.Top + stripY, 0, 0, bitmap.Size);
        var found = FindButton(bitmap.Width, bitmap.Height, bitmap.GetPixel);
        return found is { } f ? (f.x + stripX, f.y + stripY) : null;
    }
}
