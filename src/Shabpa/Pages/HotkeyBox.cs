using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Shabpa.Core;

namespace Shabpa.Pages;

/// <summary>A button that records a keyboard shortcut: click it, then press the new combination. The shortcut must
/// include Ctrl or Alt (Shift is optional). Esc cancels, Backspace or Delete turns the shortcut off.</summary>
public sealed class HotkeyBox : Button
{
    private bool _listening;
    private string _hint = "";

    public string Value { get; private set; } = "";
    public event Action<string>? Changed;

    public HotkeyBox()
    {
        Style = (Style)Application.Current.Resources["Btn"];
        MinWidth = 150;
        FlowDirection = FlowDirection.LeftToRight;
        Focusable = true;
        Click += (_, _) => Listen();
        LostKeyboardFocus += (_, _) => Stop();
        PreviewKeyDown += OnKey;
        Show();
    }

    public void Set(string value)
    {
        Value = Hotkey.Parse(value)?.ToString() ?? "";
        Stop();
    }

    private void Listen()
    {
        _listening = true;
        _hint = "";
        Focus();
        Show();
    }

    private void Stop()
    {
        _listening = false;
        _hint = "";
        Show();
    }

    private void Show()
    {
        if (_listening) Content = _hint.Length > 0 ? _hint : L.T("دکمه‌ی جدید رو بزن…", "Press the new keys…");
        else Content = Value.Length > 0 ? Value : L.T("خاموش (کلیک کن)", "Off (click to set)");
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (!_listening) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        var mods = Keyboard.Modifiers;
        if (key == Key.Escape && mods == ModifierKeys.None)
        {
            Stop();
            return;
        }
        if (key is Key.Back or Key.Delete && mods == ModifierKeys.None)
        {
            Value = "";
            Stop();
            Changed?.Invoke("");
            return;
        }
        if (Hotkey.From(mods, key) is not { } hotkey)
        {
            _hint = L.T("باید Ctrl یا Alt هم باشه", "It needs Ctrl or Alt too");
            Show();
            return;
        }
        Value = hotkey.ToString();
        Stop();
        Changed?.Invoke(Value);
    }
}
