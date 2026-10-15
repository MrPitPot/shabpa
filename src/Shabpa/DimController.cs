using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Shabpa.Core;
using WF = System.Windows.Forms;

namespace Shabpa;

/// <summary>Covers every monitor with a dark, click-through layer so a screen can be made darker than its own
/// brightness buttons allow. The layer never takes focus or input, so games and windows work normally under it.</summary>
public sealed class DimController : IDisposable
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    private const int ToggleId = 0xC001, DarkerId = 0xC002, LighterId = 0xC003;
    private const uint ModNoRepeat = 0x4000, SwpNoActivate = 0x10, SwpShowWindow = 0x40;
    private static readonly IntPtr HwndTopmost = new(-1);

    private readonly Hub _hub;
    private readonly HwndSource? _sink;
    private readonly List<Window> _windows = [];
    private string _keys = "";
    private string _shape = "";
    private bool _suppressAuto;

    /// <summary>When set (sleep mode), this level is used and the screen is dimmed whatever the settings say.</summary>
    public double? Override { get; set; }
    public bool Active { get; private set; }
    public IReadOnlyList<string> FailedHotkeys { get; private set; } = [];

    public DimController(Hub hub)
    {
        _hub = hub;
        try
        {
            _sink = new HwndSource(new HwndSourceParameters("ShabpaComfort") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0 });
            _sink.AddHook(WndProc);
        }
        catch { /* no hotkeys; dimming from the page still works */ }
    }

    private ComfortSettings S => _hub.Settings.Comfort;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != 0x0312) return IntPtr.Zero;
        switch (wParam.ToInt32())
        {
            case ToggleId: Toggle(); handled = true; break;
            case DarkerId: Nudge(+0.05); handled = true; break;
            case LighterId: Nudge(-0.05); handled = true; break;
        }
        return IntPtr.Zero;
    }

    public void Toggle()
    {
        var auto = S.DimAuto && ComfortTime.InWindow(DateTime.Now, S.DimFrom, S.DimTo);
        if (Active)
        {
            S.DimOn = false;
            if (auto) _suppressAuto = true; // the user turned it off inside the night window: respect that until it ends
        }
        else
        {
            S.DimOn = true;
            _suppressAuto = false;
        }
        Changed();
    }

    public void Nudge(double delta)
    {
        S.DimLevel = Math.Round(Math.Clamp(S.DimLevel + delta, 0.05, 0.92), 2);
        if (!Active) S.DimOn = true;
        Changed();
    }

    private void Changed()
    {
        _hub.Store.Save();
        Apply();
        _hub.ComfortChanged();
    }

    /// <summary>Works out whether the screen should be dimmed right now and draws or removes the layer.</summary>
    public void Apply()
    {
        RegisterKeys();
        var now = DateTime.Now;
        var inNight = S.DimAuto && ComfortTime.InWindow(now, S.DimFrom, S.DimTo);
        if (!inNight) _suppressAuto = false;
        var on = Override is not null || S.DimOn || (inNight && !_suppressAuto);
        var level = Math.Clamp(Override ?? S.DimLevel, 0.05, 0.92);
        Active = on;
        if (!on)
        {
            Close();
            return;
        }
        var shape = $"{level:0.000}|{S.DimWarm}|{string.Join(';', WF.Screen.AllScreens.Select(s => s.Bounds.ToString()))}";
        if (shape == _shape && _windows.Count > 0) return;
        _shape = shape;
        Close();
        // Warm cover: a hint of amber keeps blue light down on top of simply being darker.
        var colour = S.DimWarm ? Color.FromRgb(38, 14, 0) : Colors.Black;
        foreach (var screen in WF.Screen.AllScreens)
        {
            var window = new Window
            {
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, AllowsTransparency = true, ShowInTaskbar = false,
                ShowActivated = false, Topmost = true, Focusable = false, Title = "Shabpa dim",
                Background = new SolidColorBrush(Color.FromArgb((byte)Math.Round(level * 255), colour.R, colour.G, colour.B)),
                Left = screen.Bounds.X, Top = screen.Bounds.Y, Width = 10, Height = 10,
            };
            window.SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                // Click-through, hidden from Alt+Tab, never activated.
                var style = GetWindowLongPtr(hwnd, -20).ToInt64() | 0x20 | 0x80 | 0x08000000 | 0x80000;
                SetWindowLongPtr(hwnd, -20, (IntPtr)style);
                SetWindowPos(hwnd, HwndTopmost, screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height, SwpNoActivate | SwpShowWindow);
            };
            window.Show();
            _windows.Add(window);
        }
    }

    private void Close()
    {
        foreach (var window in _windows) window.Close();
        _windows.Clear();
        _shape = "";
    }

    private bool _registered;

    private void RegisterKeys()
    {
        if (_sink is null) return;
        var wanted = $"{S.DimKey}|{S.DimDarkerKey}|{S.DimLighterKey}";
        if (_registered && wanted == _keys) return;
        if (_registered)
            foreach (var id in new[] { ToggleId, DarkerId, LighterId }) UnregisterHotKey(_sink.Handle, id);
        _keys = wanted;
        _registered = true;
        var failed = new List<string>();
        void One(int id, string text)
        {
            if (Hotkey.Parse(text) is not { } key) return;
            if (!RegisterHotKey(_sink.Handle, id, key.Modifiers | ModNoRepeat, key.VirtualKey)) failed.Add(key.ToString());
        }
        One(ToggleId, S.DimKey);
        One(DarkerId, S.DimDarkerKey);
        One(LighterId, S.DimLighterKey);
        FailedHotkeys = [.. failed];
    }

    public void Dispose()
    {
        Close();
        if (_sink is null) return;
        if (_registered)
            foreach (var id in new[] { ToggleId, DarkerId, LighterId }) UnregisterHotKey(_sink.Handle, id);
        _sink.Dispose();
    }
}
