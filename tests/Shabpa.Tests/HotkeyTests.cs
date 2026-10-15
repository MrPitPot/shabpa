using System.Windows.Input;
using Shabpa.Core;

namespace Shabpa.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+O", "Ctrl+Alt+O")]
    [InlineData("ctrl+shift+f9", "Ctrl+Shift+F9")]
    [InlineData("O", "Alt+O")] // older settings stored just the letter
    [InlineData("Alt+5", "Alt+5")]
    public void Valid_shortcuts_round_trip(string text, string expected) => Assert.Equal(expected, Hotkey.Parse(text)?.ToString());

    [Theory]
    [InlineData("")]
    [InlineData("Shift+O")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+Nope")]
    public void A_shortcut_without_ctrl_or_alt_is_refused(string text) => Assert.Null(Hotkey.Parse(text));

    [Fact]
    public void Captured_keys_need_ctrl_or_alt_and_a_real_key()
    {
        Assert.Null(Hotkey.From(ModifierKeys.None, Key.F9));
        Assert.Null(Hotkey.From(ModifierKeys.Shift, Key.F9));
        Assert.Null(Hotkey.From(ModifierKeys.Control, Key.LeftCtrl));
        var hotkey = Hotkey.From(ModifierKeys.Control | ModifierKeys.Shift, Key.K)!;
        Assert.Equal(Hotkey.ModCtrl | Hotkey.ModShift, hotkey.Modifiers);
        Assert.Equal((uint)'K', hotkey.VirtualKey);
    }
}
