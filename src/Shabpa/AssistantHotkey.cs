using System.Runtime.InteropServices;
using System.Windows.Interop;
using Shabpa.Core;

namespace Shabpa;

/// <summary>The global shortcut that opens the command box from anywhere (Ctrl+Alt+Space unless changed in Settings).</summary>
public sealed class AssistantHotkey : IDisposable
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    private const int Id = 0xC101;
    private const uint ModNoRepeat = 0x4000;
    private readonly HwndSource? _sink;
    private string _key = "";
    private bool _registered;

    public string? Failed { get; private set; }

    public AssistantHotkey()
    {
        try
        {
            _sink = new HwndSource(new HwndSourceParameters("ShabpaAssistant") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0 });
            _sink.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (msg == 0x0312 && wParam.ToInt32() == Id)
                {
                    handled = true;
                    AssistantWindow.Open();
                }
                return IntPtr.Zero;
            });
        }
        catch { /* no shortcut; the tray item still opens the box */ }
    }

    /// <summary>Registers <paramref name="text"/> (empty = no shortcut); a no-op when it did not change.</summary>
    public void Apply(string text)
    {
        if (_sink is null || (_registered && text == _key)) return;
        if (_registered) UnregisterHotKey(_sink.Handle, Id);
        _key = text;
        _registered = true;
        Failed = null;
        if (Hotkey.Parse(text) is { } key && !RegisterHotKey(_sink.Handle, Id, key.Modifiers | ModNoRepeat, key.VirtualKey))
            Failed = key.ToString();
    }

    public void Dispose()
    {
        if (_sink is null) return;
        if (_registered) UnregisterHotKey(_sink.Handle, Id);
        _sink.Dispose();
    }
}
