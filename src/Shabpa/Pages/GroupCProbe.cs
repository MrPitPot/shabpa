using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Controls;
using System.Windows.Media;
using Shabpa.Core;

namespace Shabpa.Pages;

/// <summary>Test aid for <c>--motion-test</c> (Tasks, History, Usage, Updates pages): opens each page with motion on, takes a picture in
/// the middle of the entrance, adds and removes a task, presses real buttons, and reports whether, once everything has settled,
/// anything is left half-visible or transformed. Any exception ends up in the report.</summary>
internal static class GroupCProbe
{
    public static async Task Run(MainWindow window, StringBuilder log, string folder, Action<FrameworkElement, string> save)
    {
        try
        {
            Hub.I.Settings.Animations = true;
            foreach (var key in new[] { "system:history", "tasks:todo", "tasks:focus", "tasks:report", "ping:usage", "health:apps", "health:drivers", "update" })
            {
                window.Navigate(key);
                await Task.Delay(330);
                save(window.CaptureRoot, Path.Combine(folder, $"motion-c-{key.Replace(':', '-')}.png"));
                await Task.Delay(2200);
                log.AppendLine($"{key}: " + Settled(window.CaptureRoot));
            }

            // the task list: a new task springs in, finishing it springs the row out, deleting too
            window.Navigate("tasks:todo");
            await Task.Delay(1800);
            var item = Hub.I.Tasks.Add("Probe task", DateTime.Now.Date.AddHours(23), 0, "", "", true);
            await Task.Delay(260);
            save(window.CaptureRoot, Path.Combine(folder, "motion-c-task-added.png"));
            await Task.Delay(900);
            log.AppendLine("tasks:todo (task added): " + Settled(window.CaptureRoot));
            if (item is not null)
            {
                Press(window.CaptureRoot, L.T("افزودن کار", "Add task"));
                await Task.Delay(300);
                save(window.CaptureRoot, Path.Combine(folder, "motion-c-task-empty-title.png"));
                await Task.Delay(700);
                Hub.I.Tasks.Remove(item.Id);
                await Task.Delay(900);
                log.AppendLine("tasks:todo (task removed): " + Settled(window.CaptureRoot));
            }

            // history: pick another day, refresh
            window.Navigate("system:history");
            await Task.Delay(2000);
            Press(window.CaptureRoot, L.T("بازبینی مجدد", "Refresh"));
            await Task.Delay(300);
            save(window.CaptureRoot, Path.Combine(folder, "motion-c-history-refresh.png"));
            await Task.Delay(2200);
            log.AppendLine("system:history (refresh): " + Settled(window.CaptureRoot));

            // usage: another period
            window.Navigate("ping:usage");
            await Task.Delay(2000);
            Press(window.CaptureRoot, L.T("امروز", "Today"));
            await Task.Delay(300);
            save(window.CaptureRoot, Path.Combine(folder, "motion-c-usage-today.png"));
            await Task.Delay(2200);
            log.AppendLine("ping:usage (today): " + Settled(window.CaptureRoot));

            await UpdateStates(window, log, folder, save);
        }
        catch (Exception ex)
        {
            log.AppendLine("group C motion error: " + ex);
        }
    }

    /// <summary>Puts the updater through "update ready" and "downloading" (by reflection, nothing is downloaded) and photographs the page.</summary>
    private static async Task UpdateStates(MainWindow window, StringBuilder log, string folder, Action<FrameworkElement, string> save)
    {
        var updater = Hub.I.Updater;
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
        void Set(string name, object? value) => typeof(Updater).GetProperty(name, flags)!.SetValue(updater, value);
        void Raise() => ((Action?)typeof(Updater).GetField("Changed", flags)!.GetValue(updater))?.Invoke();
        window.Navigate("update");
        await Task.Delay(1800);
        Set("Available", new UpdateInfo(new Version(3, 5, 0), "• New Glass Depth look\n• Faster page changes\n• Small fixes", "https://example.invalid/setup.exe", null, 1000, DateTime.Now));
        Set("State", UpdateState.Available);
        Raise();
        await Task.Delay(300);
        save(window.CaptureRoot, Path.Combine(folder, "motion-c-update-ready.png"));
        await Task.Delay(1200);
        Set("State", UpdateState.Downloading);
        Set("Progress", 0.42);
        Raise();
        await Task.Delay(700);
        save(window.CaptureRoot, Path.Combine(folder, "motion-c-update-downloading.png"));
        log.AppendLine("update (ready, downloading): " + Settled(window.CaptureRoot));
        Set("Progress", 0.0);
        Set("Available", null);
        Set("State", UpdateState.UpToDate);
        Raise();
        await Task.Delay(500);
    }

    /// <summary>Clicks the first enabled button (or segmented option) whose text starts with <paramref name="text"/>.</summary>
    private static void Press(DependencyObject root, string text)
    {
        foreach (var button in All(root).OfType<ButtonBase>())
        {
            if (button.IsEnabled && button.IsVisible && TextOf(button).StartsWith(text, StringComparison.Ordinal))
            {
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                return;
            }
        }
        foreach (var block in All(root).OfType<TextBlock>())
        {
            if (block.IsVisible && block.Text == text && block.Parent is Panel)
            {
                block.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left)
                {
                    RoutedEvent = UIElement.MouseLeftButtonDownEvent,
                });
                return;
            }
        }
    }

    private static string TextOf(DependencyObject button) => string.Concat(All(button).OfType<TextBlock>().Select(t => t.Text));

    /// <summary>Counts visible elements that are still faded or carry a leftover animated transform.</summary>
    private static string Settled(DependencyObject root)
    {
        int faded = 0, leftover = 0, total = 0;
        foreach (var e in All(root).OfType<FrameworkElement>())
        {
            if (!e.IsVisible) continue;
            total++;
            if (e.Opacity is > 0.01 and < 0.99) faded++;
            if (e.RenderTransform is TransformGroup g && g.Children.OfType<TranslateTransform>().Any(t => Math.Abs(t.Y) > 0.5 && !t.IsFrozen)) leftover++;
        }
        return $"{total} elements, {faded} faded, {leftover} with a leftover lift";
    }

    private static IEnumerable<DependencyObject> All(DependencyObject root)
    {
        yield return root;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
            foreach (var child in All(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
