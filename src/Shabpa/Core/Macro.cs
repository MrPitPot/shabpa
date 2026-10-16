using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Shabpa.Core;

/// <summary>One mouse action taught by the person: where it happened inside the launcher's window and how long to
/// wait before it.</summary>
public sealed class MacroStep
{
    /// <summary>Pixels from the window's top-left corner when it was recorded.</summary>
    public int Dx { get; set; }
    public int Dy { get; set; }
    /// <summary>The same place as a fraction of the window's width and height, used when the window is another size.</summary>
    public double Fx { get; set; }
    public double Fy { get; set; }
    /// <summary>Milliseconds since the step before.</summary>
    public int WaitMs { get; set; }
    /// <summary>"left", "right" or "wheel".</summary>
    public string Button { get; set; } = "left";
    /// <summary>Wheel notches (positive = up), only for "wheel".</summary>
    public int Wheel { get; set; }
}

/// <summary>The clicks taught for one launcher: "open the downloads page, press resume". Stored on this PC only.
/// Only the position and timing of mouse clicks inside that launcher's window are kept; no key, text or screen picture.</summary>
public sealed class LauncherMacro
{
    public string LauncherKey { get; set; } = "";
    /// <summary>The window the clicks happened in: its program and window class, so it is found again later.</summary>
    public string WindowProcess { get; set; } = "";
    public string WindowClass { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public int Dpi { get; set; } = 96;
    public bool Maximized { get; set; }
    public DateTime Recorded { get; set; }
    public List<MacroStep> Steps { get; set; } = [];
}

/// <summary>The arithmetic of taught clicks, kept apart from the Windows calls so it can be tested.</summary>
public static class MacroMath
{
    public const int MinWaitMs = 400, MaxWaitMs = 20000, MaxSteps = 60;

    public static int ClampWait(int ms) => Math.Clamp(ms, MinWaitMs, MaxWaitMs);

    public static (double fx, double fy) Fractions(int dx, int dy, int width, int height) =>
        (width > 0 ? Math.Clamp((double)dx / width, 0, 1) : 0, height > 0 ? Math.Clamp((double)dy / height, 0, 1) : 0);

    /// <summary>Where to click now. In a window of the recorded size the recorded pixel offset is exact; in another size
    /// the fraction keeps the click on the same part of the window. A different screen scaling scales the offset.</summary>
    public static (int x, int y) Resolve(MacroStep step, LauncherMacro macro, int left, int top, int width, int height, int dpi)
    {
        var same = Math.Abs(width - macro.Width) <= 3 && Math.Abs(height - macro.Height) <= 3;
        if (same)
        {
            var scale = macro.Dpi > 0 && dpi > 0 ? (double)dpi / macro.Dpi : 1;
            return (left + (int)Math.Round(step.Dx * scale), top + (int)Math.Round(step.Dy * scale));
        }
        return (left + (int)Math.Round(step.Fx * width), top + (int)Math.Round(step.Fy * height));
    }

    /// <summary>Whether a click belongs in the recording: it must land in the launcher's own window, never in Shabpa.</summary>
    public static bool Accept(int clickPid, int ownPid, string? clickProcess, string? referenceProcess) =>
        clickPid != ownPid && clickProcess is { Length: > 0 }
        && (referenceProcess is null || string.Equals(clickProcess, referenceProcess, StringComparison.OrdinalIgnoreCase));

    /// <summary>Adds a wheel turn, merging it into the step before when it is the same spot a moment later.</summary>
    public static void AddWheel(List<MacroStep> steps, MacroStep wheel)
    {
        if (steps.Count > 0 && steps[^1] is { Button: "wheel" } last && wheel.WaitMs < 450 && Math.Abs(last.Dx - wheel.Dx) < 6 && Math.Abs(last.Dy - wheel.Dy) < 6)
        {
            last.Wheel += wheel.Wheel;
            return;
        }
        steps.Add(wheel);
    }
}

internal static class MacroNative
{
    [StructLayout(LayoutKind.Sequential)] public struct Pt { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct HookData { public Pt Point; public uint MouseData, Flags, Time; public IntPtr Extra; }

    public delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Pt point);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder text, int max);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Pt point);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, int data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] public static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll", SetLastError = true)] public static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] public static extern bool GetTokenInformation(IntPtr token, int kind, out int value, int length, out int returned);

    public const int WhMouseLl = 14;
    public const int WmLButtonDown = 0x201, WmRButtonDown = 0x204, WmMouseWheel = 0x20A;
    public const uint MouseLeftDown = 0x2, MouseLeftUp = 0x4, MouseRightDown = 0x8, MouseRightUp = 0x10, MouseWheel = 0x800;

    public static string ClassOf(IntPtr hwnd)
    {
        var text = new System.Text.StringBuilder(256);
        return GetClassName(hwnd, text, text.Capacity) > 0 ? text.ToString() : "";
    }

    public static (int pid, string name) ProcessOf(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return ((int)pid, p.ProcessName);
        }
        catch
        {
            return ((int)pid, "");
        }
    }

    /// <summary>True when the program runs "as administrator". Such a window ignores input from a normal program.</summary>
    public static bool IsElevated(int pid)
    {
        var process = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (process == IntPtr.Zero) return false;
        try
        {
            if (!OpenProcessToken(process, 0x8, out var token)) return false;
            try
            {
                return GetTokenInformation(token, 20, out var elevated, 4, out _) && elevated != 0; // TokenElevation
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    /// <summary>The screen is locked (or the secure desktop is up): nothing typed or clicked reaches the programs.</summary>
    public static bool ScreenLocked()
    {
        var desktop = OpenInputDesktop(0, false, 0x100);
        if (desktop == IntPtr.Zero) return true;
        CloseDesktop(desktop);
        return false;
    }

    public static void Foreground(IntPtr hwnd)
    {
        if (IsIconic(hwnd)) ShowWindow(hwnd, 9);
        var current = GetForegroundWindow();
        var currentThread = current == IntPtr.Zero ? 0 : GetWindowThreadProcessId(current, out _);
        var me = GetCurrentThreadId();
        var attached = currentThread != 0 && currentThread != me && AttachThreadInput(me, currentThread, true);
        SetForegroundWindow(hwnd);
        BringWindowToTop(hwnd);
        if (attached) AttachThreadInput(me, currentThread, false);
    }
}

/// <summary>Watches the mouse while the person does, once, what Shabpa should later do by itself in a launcher. It only
/// keeps clicks and wheel turns that land in that launcher's window (never in Shabpa, never on another program), with
/// their position inside the window and the pause before them. It sees no key presses and takes no screen pictures.</summary>
public sealed class MacroRecorder : IDisposable
{
    private readonly MacroNative.HookProc _proc; // kept alive for as long as the hook is installed
    private IntPtr _hook;
    private readonly int _ownPid = Environment.ProcessId;
    private IntPtr _window;
    private string? _reference;
    private long _lastTick;
    private readonly string _key;
    private readonly LauncherMacro _macro;

    /// <summary>How many steps have been recorded so far.</summary>
    public event Action<int>? Changed;

    public int Count => _macro.Steps.Count;
    public bool Running => _hook != IntPtr.Zero;

    public MacroRecorder(string launcherKey)
    {
        _key = launcherKey;
        _macro = new LauncherMacro { LauncherKey = launcherKey };
        _proc = Hook;
    }

    public bool Start()
    {
        if (_hook != IntPtr.Zero) return true;
        _lastTick = Environment.TickCount64;
        _hook = MacroNative.SetWindowsHookEx(MacroNative.WhMouseLl, _proc, MacroNative.GetModuleHandle(null), 0);
        return _hook != IntPtr.Zero;
    }

    /// <summary>Stops listening and returns what was taught, or null when nothing was recorded.</summary>
    public LauncherMacro? Stop()
    {
        Dispose();
        if (_macro.Steps.Count == 0) return null;
        _macro.Recorded = DateTime.Now;
        return _macro;
    }

    private IntPtr Hook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var message = (int)wParam;
            if (message is MacroNative.WmLButtonDown or MacroNative.WmRButtonDown or MacroNative.WmMouseWheel)
            {
                try { Record(message, Marshal.PtrToStructure<MacroNative.HookData>(lParam)); }
                catch { /* a failed recording step must never get in the way of the mouse */ }
            }
        }
        return MacroNative.CallNextHookEx(_hook, code, wParam, lParam);
    }

    private void Record(int message, MacroNative.HookData data)
    {
        if (_macro.Steps.Count >= MacroMath.MaxSteps) return;
        var under = MacroNative.WindowFromPoint(data.Point);
        if (under == IntPtr.Zero) return;
        var root = MacroNative.GetAncestor(under, 2); // GA_ROOT
        if (root == IntPtr.Zero) root = under;
        var (pid, name) = MacroNative.ProcessOf(root);
        if (!MacroMath.Accept(pid, _ownPid, name, _reference)) return;

        if (_window == IntPtr.Zero || !MacroNative.IsWindow(_window))
        {
            // the first click names the launcher's window
            _window = root;
            _reference = name;
            _macro.WindowProcess = name;
            _macro.WindowClass = MacroNative.ClassOf(root);
        }
        if (!MacroNative.GetWindowRect(_window, out var rect)) return;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width < 100 || height < 100) return;
        if (_macro.Steps.Count == 0)
        {
            _macro.Width = width;
            _macro.Height = height;
            _macro.Maximized = MacroNative.IsZoomed(_window);
            _macro.Dpi = (int)Math.Max(96, MacroNative.GetDpiForWindow(_window));
        }

        var now = Environment.TickCount64;
        var dx = data.Point.X - rect.Left;
        var dy = data.Point.Y - rect.Top;
        var (fx, fy) = MacroMath.Fractions(dx, dy, width, height);
        var step = new MacroStep { Dx = dx, Dy = dy, Fx = fx, Fy = fy, WaitMs = (int)Math.Min(now - _lastTick, 600000) };
        _lastTick = now;
        if (message == MacroNative.WmMouseWheel)
        {
            step.Button = "wheel";
            step.Wheel = (short)(data.MouseData >> 16) / 120;
            if (step.Wheel == 0) return;
            MacroMath.AddWheel(_macro.Steps, step);
        }
        else
        {
            step.Button = message == MacroNative.WmRButtonDown ? "right" : "left";
            _macro.Steps.Add(step);
        }
        Changed?.Invoke(_macro.Steps.Count);
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero) return;
        MacroNative.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    public string Key => _key;
}

public enum MacroOutcome { Done, NoWindow, ScreenLocked, Interrupted, LostWindow, NeedsAdmin, Empty }

/// <summary>Repeats the taught clicks. It checks before each one that the launcher's window is where it should be and
/// that the person has not taken the mouse, and Esc stops it at any moment.</summary>
public static class MacroPlayer
{
    private static (IntPtr hwnd, int pid)? FindWindow(LauncherMacro macro)
    {
        (IntPtr hwnd, int pid, long area)? best = null;
        MacroNative.EnumWindows((h, _) =>
        {
            if (!MacroNative.IsWindowVisible(h) || !MacroNative.GetWindowRect(h, out var r)) return true;
            var area = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
            var minimized = MacroNative.IsIconic(h);
            if (!minimized && (r.Right - r.Left < 200 || r.Bottom - r.Top < 150)) return true;
            var (pid, name) = MacroNative.ProcessOf(h);
            if (!string.Equals(name, macro.WindowProcess, StringComparison.OrdinalIgnoreCase)) return true;
            if (macro.WindowClass.Length > 0 && MacroNative.ClassOf(h) != macro.WindowClass) return true;
            if (best is null || area > best.Value.area) best = (h, pid, area);
            return true;
        }, IntPtr.Zero);
        return best is { } b ? (b.hwnd, b.pid) : null;
    }

    private static bool EscPressed() => (MacroNative.GetAsyncKeyState(0x1B) & 0x8000) != 0;

    /// <summary>Sleeps in short slices; false when Esc was pressed or the run was cancelled.</summary>
    private static bool Wait(int ms, CancellationToken ct)
    {
        for (var left = ms; left > 0; left -= 50)
        {
            if (ct.IsCancellationRequested || EscPressed()) return false;
            Thread.Sleep(Math.Min(50, left));
        }
        return !ct.IsCancellationRequested && !EscPressed();
    }

    public static MacroOutcome Run(LauncherMacro macro, Action<int, int>? progress = null, int waitForWindowSeconds = 90, CancellationToken ct = default)
    {
        if (macro.Steps.Count == 0) return MacroOutcome.Empty;
        if (MacroNative.ScreenLocked()) return MacroOutcome.ScreenLocked;

        (IntPtr hwnd, int pid)? found = null;
        for (var i = 0; i < waitForWindowSeconds * 2 && !ct.IsCancellationRequested; i++)
        {
            found = FindWindow(macro);
            if (found is not null) break;
            Thread.Sleep(500);
        }
        if (found is not { } window) return MacroOutcome.NoWindow;
        if (!Hub.Elevated && MacroNative.IsElevated(window.pid)) return MacroOutcome.NeedsAdmin;

        // The window needs a moment to draw its pages before the first click.
        if (!Wait(Math.Clamp(macro.Steps[0].WaitMs, 2500, 10000), ct)) return MacroOutcome.Interrupted;
        if (MacroNative.ScreenLocked()) return MacroOutcome.ScreenLocked;
        MacroNative.Foreground(window.hwnd);
        FitWindow(window.hwnd, macro);

        MacroNative.Pt? placed = null;
        for (var i = 0; i < macro.Steps.Count; i++)
        {
            var step = macro.Steps[i];
            if (i > 0 && !Wait(MacroMath.ClampWait(step.WaitMs), ct)) return MacroOutcome.Interrupted;
            if (!MacroNative.IsWindow(window.hwnd)) return MacroOutcome.LostWindow;
            if (MacroNative.ScreenLocked()) return MacroOutcome.ScreenLocked;
            // somebody moved the mouse since the last click: it is theirs again
            if (placed is { } p && MacroNative.GetCursorPos(out var now) && Math.Abs(now.X - p.X) + Math.Abs(now.Y - p.Y) > 60) return MacroOutcome.Interrupted;

            MacroNative.Foreground(window.hwnd);
            if (!MacroNative.GetWindowRect(window.hwnd, out var rect)) return MacroOutcome.LostWindow;
            var dpi = (int)Math.Max(96, MacroNative.GetDpiForWindow(window.hwnd));
            var (x, y) = MacroMath.Resolve(step, macro, rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, dpi);
            progress?.Invoke(i + 1, macro.Steps.Count);

            // the point must belong to the launcher (a message box or another program on top would take the click)
            var ok = false;
            for (var tries = 0; tries < 6 && !ok; tries++)
            {
                var under = MacroNative.WindowFromPoint(new MacroNative.Pt { X = x, Y = y });
                var root = under == IntPtr.Zero ? IntPtr.Zero : MacroNative.GetAncestor(under, 2);
                ok = root != IntPtr.Zero && string.Equals(MacroNative.ProcessOf(root).name, macro.WindowProcess, StringComparison.OrdinalIgnoreCase);
                if (!ok)
                {
                    MacroNative.Foreground(window.hwnd);
                    if (!Wait(500, ct)) return MacroOutcome.Interrupted;
                }
            }
            if (!ok) return MacroOutcome.LostWindow;

            Glide(x, y);
            placed = new MacroNative.Pt { X = x, Y = y };
            Thread.Sleep(90);
            Press(step);
        }
        return MacroOutcome.Done;
    }

    /// <summary>The window as it was when taught: same size (when it can be resized) and maximised or not.</summary>
    private static void FitWindow(IntPtr hwnd, LauncherMacro macro)
    {
        try
        {
            var zoomed = MacroNative.IsZoomed(hwnd);
            if (macro.Maximized && !zoomed) { MacroNative.ShowWindow(hwnd, 3); Thread.Sleep(400); return; }
            if (!macro.Maximized && zoomed) { MacroNative.ShowWindow(hwnd, 9); Thread.Sleep(400); }
            if (macro.Maximized || !MacroNative.GetWindowRect(hwnd, out var r)) return;
            if (Math.Abs(r.Right - r.Left - macro.Width) > 3 || Math.Abs(r.Bottom - r.Top - macro.Height) > 3)
            {
                // SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE
                MacroNative.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, macro.Width, macro.Height, 0x2 | 0x4 | 0x10);
                Thread.Sleep(400);
            }
        }
        catch
        {
            // a window that cannot be resized is used as it is
        }
    }

    /// <summary>Moves the pointer there in a short smooth glide, the way a hand would.</summary>
    private static void Glide(int x, int y)
    {
        MacroNative.GetCursorPos(out var from);
        const int Steps = 14;
        for (var i = 1; i <= Steps; i++)
        {
            var t = i / (double)Steps;
            var eased = t * t * (3 - 2 * t);
            MacroNative.SetCursorPos(from.X + (int)((x - from.X) * eased), from.Y + (int)((y - from.Y) * eased));
            Thread.Sleep(12);
        }
        MacroNative.SetCursorPos(x, y);
    }

    private static void Press(MacroStep step)
    {
        switch (step.Button)
        {
            case "wheel":
                MacroNative.mouse_event(MacroNative.MouseWheel, 0, 0, step.Wheel * 120, UIntPtr.Zero);
                break;
            case "right":
                MacroNative.mouse_event(MacroNative.MouseRightDown, 0, 0, 0, UIntPtr.Zero);
                Thread.Sleep(55);
                MacroNative.mouse_event(MacroNative.MouseRightUp, 0, 0, 0, UIntPtr.Zero);
                break;
            default:
                MacroNative.mouse_event(MacroNative.MouseLeftDown, 0, 0, 0, UIntPtr.Zero);
                Thread.Sleep(55);
                MacroNative.mouse_event(MacroNative.MouseLeftUp, 0, 0, 0, UIntPtr.Zero);
                break;
        }
    }

    public static string Describe(MacroOutcome outcome) => outcome switch
    {
        MacroOutcome.Done => L.T("کلیک‌های یادگرفته‌شده انجام شد.", "The taught clicks were done."),
        MacroOutcome.NoWindow => L.T("پنجره‌ی لانچر پیدا نشد.", "The launcher's window was not found."),
        MacroOutcome.ScreenLocked => L.T("صفحه قفل بود؛ کلیکی زده نشد.", "The screen was locked; nothing was clicked."),
        MacroOutcome.Interrupted => L.T("با Esc یا حرکت ماوس متوقف شد.", "Stopped by Esc or by moving the mouse."),
        MacroOutcome.LostWindow => L.T("جای کلیک توی پنجره‌ی لانچر نبود (پنجره‌ی دیگه‌ای روش بود یا بسته شد).", "The click spot was not in the launcher's window (another window was over it, or it closed)."),
        MacroOutcome.NeedsAdmin => L.T("لانچر با دسترسی ادمین بازه؛ شب‌پا هم باید با ادمین اجرا بشه.", "The launcher runs as administrator; Shabpa must run as administrator too."),
        _ => L.T("چیزی برای اجرا ضبط نشده.", "Nothing was taught."),
    };
}
