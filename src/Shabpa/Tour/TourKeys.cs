using System.Windows;
using System.Windows.Input;

namespace Shabpa.Tour;

public enum TourKeyAction { Pass, Next, Back, Skip, Swallow }

/// <summary>What a key does while the tour is on screen. The dim layer is modal: besides Esc, Enter and the arrows every key is swallowed,
/// so Tab, Space or Ctrl+number cannot press a button or change the page under the curtain. Only keys that are not for the app
/// (modifiers alone, the Windows key, Print Screen, Alt+F4) pass.</summary>
public static class TourKeys
{
    public static TourKeyAction Classify(Key key, ModifierKeys modifiers, FlowDirection flow)
    {
        if (key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin
            or Key.PrintScreen or Key.Snapshot or Key.Apps) return TourKeyAction.Pass;
        if (key == Key.F4 && modifiers.HasFlag(ModifierKeys.Alt)) return TourKeyAction.Pass;
        if (modifiers != ModifierKeys.None) return TourKeyAction.Swallow;
        var forward = flow == FlowDirection.LeftToRight ? Key.Right : Key.Left;
        var backward = flow == FlowDirection.LeftToRight ? Key.Left : Key.Right;
        if (key == Key.Escape) return TourKeyAction.Skip;
        if (key == forward || key == Key.Enter || key == Key.PageDown) return TourKeyAction.Next;
        if (key == backward || key == Key.PageUp) return TourKeyAction.Back;
        return TourKeyAction.Swallow;
    }
}
