#if DEBUG || SHABPA_TESTHOOKS
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Shabpa.Core;

namespace Shabpa.Pages;

/// <summary>Test aid for <c>--delete-test &lt;report&gt;</c> (needs <c>--screenshots &lt;folder&gt; --live --demo-games</c>): opens the first game
/// on the Playtime page and clicks the trash icon of the first session with a REAL mouse click (the window is made 1% visible so it can
/// be hit, and the click is only sent if the window under the cursor is this one), answers the confirmation, and reports whether the
/// window thread came back and how many sessions are left. If the window thread is stuck for 12 s the report holds its stack.</summary>
internal static class DeleteProbe
{
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, int dx, int dy, uint data, nint extra);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(System.Drawing.Point p);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLong(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLong(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);

    public static async Task Run(MainWindow window, string report)
    {
        var sb = new StringBuilder();
        var ui = Application.Current.Dispatcher.Thread.ManagedThreadId;
        var done = false;
        _ = Task.Run(async () =>
        {
            await Task.Delay(30000);
            if (done) return;
            File.WriteAllText(report, sb + "STUCK\n" + string.Join("\n", StallStack.Capture(ui)));
            Environment.Exit(3);
        });
        var game = Hub.I.GameLog.Games.OrderByDescending(g => g.Sessions.Count).First();
        window.Left = 40; window.Top = 40; window.ShowInTaskbar = false;
        var hwnd = new WindowInteropHelper(window).Handle;
        SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x80000 | 0x20 * 0); // WS_EX_LAYERED
        SetLayeredWindowAttributes(hwnd, 0, 3, 2);
        window.Navigate("playtime:" + game.Key);
        await Task.Delay(1500);
        sb.AppendLine($"before: {Hub.I.GameLog.Game(game.Key)!.Sessions.Count} sessions");
        var trash = MotionAudit.All(window).OfType<Button>().FirstOrDefault(b => b.ToolTip as string is "Delete this session" or "پاک کردن این نوبت");
        if (trash is null) { File.WriteAllText(report, sb + "no trash button"); Application.Current.Shutdown(1); return; }
        var p = trash.PointToScreen(new Point(trash.ActualWidth / 2, trash.ActualHeight / 2));
        SetCursorPos((int)p.X, (int)p.Y);
        await Task.Delay(300);
        var under = GetAncestor(WindowFromPoint(new System.Drawing.Point((int)p.X, (int)p.Y)), 2);
        sb.AppendLine($"window under cursor is ours: {under == hwnd}");
        if (under != hwnd) { done = true; File.WriteAllText(report, sb.ToString()); Application.Current.Shutdown(2); return; }
        var clicks = 0;
        trash.PreviewMouseLeftButtonDown += (_, _) => sb.AppendLine("down");
        trash.PreviewMouseLeftButtonUp += (_, _) => sb.AppendLine("up");
        trash.MouseLeftButtonUp += (_, e) => sb.AppendLine("up bubble handled=" + e.Handled);
        trash.Click += (_, _) => clicks++;
        mouse_event(0x2, 0, 0, 0, 0); // left down
        await Task.Delay(80);
        sb.AppendLine($"held: pressed={trash.IsPressed} captured={trash.IsMouseCaptured} over={trash.IsMouseOver} size={trash.ActualWidth}x{trash.ActualHeight} enabled={trash.IsEnabled} clickMode={trash.ClickMode}");
        mouse_event(0x4, 0, 0, 0, 0); // left up
        await Task.Delay(1200);
        sb.AppendLine($"windows: {string.Join(",", Application.Current.Windows.Cast<Window>().Select(w => w.GetType().Name))} clicks: {clicks}, mouse captured by: {System.Windows.Input.Mouse.Captured?.GetType().Name ?? "nobody"}");
        if (Application.Current.Windows.OfType<ConfirmWindow>().FirstOrDefault() is { } dialog)
        {
            sb.AppendLine("dialog open");
            var yes = MotionAudit.All(dialog).OfType<Button>().First(b => !b.IsCancel);
            yes.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        await Task.Delay(1500);
        sb.AppendLine($"after: {Hub.I.GameLog.Game(game.Key)?.Sessions.Count.ToString() ?? "game gone"} sessions");
        done = true;
        File.WriteAllText(report, sb.ToString());
        Application.Current.Shutdown(0);
    }
}
#endif
