using System.IO;
using System.Text;
using Shabpa.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Shabpa.Pages;

/// <summary>Test aid for <c>--motion-test</c> (Downloads, Files, Memory, Health pages): opens each page with motion on, waits for
/// the entrance, presses real buttons, takes a picture in the middle of the motion and checks that, once everything has settled,
/// nothing is left half-visible or transformed. Any exception ends up in the report.</summary>
internal static class GroupDProbe
{
    public static async Task Run(MainWindow window, StringBuilder log, string folder, Action<FrameworkElement, string> save)
    {
        try
        {
            Hub.I.Settings.Animations = true;
            window.Navigate("download");
            await Task.Delay(1800);
            Press(window.CaptureRoot, L.T("پیدا کردن لانچرها", "Find launchers"));
            await Task.Delay(260);
            save(window.CaptureRoot, Path.Combine(folder, "motion-download-found.png"));
            await Task.Delay(1500);
            log.AppendLine("download: " + Settled(window.CaptureRoot));

            window.Navigate("files:space");
            await Task.Delay(1800);
            Press(window.CaptureRoot, L.T("دانلودها", "Downloads"));
            await Task.Delay(350);
            save(window.CaptureRoot, Path.Combine(folder, "motion-files-space.png"));
            await Task.Delay(1800);
            log.AppendLine("files:space: " + Settled(window.CaptureRoot));

            foreach (var key in new[] { "files:duplicates", "files:cleanup", "files:backup", "health:overview", "health:security", "health:report", "system:memory" })
            {
                window.Navigate(key);
                await Task.Delay(key.StartsWith("health") ? 3500 : 1800);
                log.AppendLine($"{key}: " + Settled(window.CaptureRoot));
            }
            save(window.CaptureRoot, Path.Combine(folder, "motion-system-memory.png"));
            window.Navigate("health:overview");
            await Task.Delay(250);
            save(window.CaptureRoot, Path.Combine(folder, "motion-health-enter.png"));
            await Task.Delay(1800);
            Press(window.CaptureRoot, L.T("بررسی دوباره", "Check again"));
            await Task.Delay(3500);
            log.AppendLine("health:overview (check again): " + Settled(window.CaptureRoot));
        }
        catch (Exception ex)
        {
            log.AppendLine("group D motion error: " + ex);
        }
    }

    /// <summary>Clicks the first enabled button whose text starts with <paramref name="text"/>.</summary>
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
    }

    private static string TextOf(DependencyObject button) => string.Concat(All(button).OfType<TextBlock>().Select(t => t.Text));

    /// <summary>Counts visible elements that are still faded or carry a leftover animated transform.</summary>
    private static string Settled(DependencyObject root)
    {
        int faded = 0, leftover = 0, total = 0;
        var who = new List<string>();
        foreach (var e in All(root).OfType<FrameworkElement>())
        {
            if (!e.IsVisible) continue;
            total++;
            if (e.Opacity is > 0.01 and < 0.99) faded++;
            if (e.RenderTransform is TransformGroup g && g.Children.OfType<TranslateTransform>().Any(t => Math.Abs(t.Y) > 0.5 && !t.IsFrozen)) { leftover++; who.Add(e.GetType().Name + "@" + (e.RenderTransform is TransformGroup gg ? string.Join(",", gg.Children.OfType<TranslateTransform>().Select(t => t.Y.ToString("0.0"))) : "")); }
        }
        return $"{total} elements, {faded} faded, {leftover} with a leftover lift {string.Join(" ", who)}";
    }

    private static IEnumerable<DependencyObject> All(DependencyObject root)
    {
        yield return root;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
            foreach (var child in All(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
