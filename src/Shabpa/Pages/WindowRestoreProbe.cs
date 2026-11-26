#if DEBUG || SHABPA_TESTHOOKS
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Shabpa.Kit;

namespace Shabpa.Pages;

internal static class WindowRestoreProbe
{
    public static async Task Run(App app, MainWindow window, string report, int rounds, int hiddenMs, int inspectMs)
    {
        int frames = 0;
        EventHandler onFrame = (_, _) => frames++;
        var start = Stopwatch.GetTimestamp();
        var heartbeat = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(50) };
        double maxGap = 0;
        heartbeat.Tick += (_, _) =>
        {
            var now = Stopwatch.GetTimestamp();
            maxGap = Math.Max(maxGap, Stopwatch.GetElapsedTime(start, now).TotalMilliseconds);
            start = now;
        };
        void Write(string line) => File.AppendAllText(report, line + Environment.NewLine);
        int exitCode = 1;
        try
        {
            await Task.Delay(1000);
            window.HideTourOfferNow();
            var root = (Grid)window.CaptureRoot;
            var scene = root.Children.OfType<ThemeScene>().SingleOrDefault();
            var tier = RenderCapability.Tier >> 16;
            Write($"START theme={Hub.I.Settings.Theme} tier={tier} active={window.IsActive} motion={Motion.Enabled} children={scene?.Children.Count ?? 0}");
            if (!window.IsActive || (scene is not null && scene.Children.Count == 0))
                throw new InvalidOperationException("The window or its theme did not become active.");

            heartbeat.Start();
            start = Stopwatch.GetTimestamp();
            CompositionTarget.Rendering += onFrame;
            var nav = (Panel)window.FindName("NavPanel");
            var title = (TextBlock)window.FindName("PageTitle");
            var targets = nav.Children.OfType<RadioButton>().Where(button => button.IsVisible).Take(2).ToArray();
            if (targets.Length < 2) throw new InvalidOperationException("Navigation controls are missing.");
            for (var i = 0; i < rounds; i++)
            {
                var plate = scene is { Children.Count: > 0 } ? scene.Children[0] : null;
                var minimize = i % 2 == 0;
                if (minimize) SystemCommands.MinimizeWindow(window);
                else window.Close(); // closing the window runs the normal hide-to-tray handler
                await Task.Delay(hiddenMs);
                if (minimize ? window.WindowState != WindowState.Minimized : window.IsVisible)
                    throw new InvalidOperationException("The requested hide operation did not take effect.");

                var before = frames;
                var restored = Stopwatch.StartNew();
                if (minimize) SystemCommands.RestoreWindow(window);
                else app.ShowWindow();
                await Task.Delay(400);
                if (!window.IsVisible || !window.IsEnabled || window.WindowState == WindowState.Minimized ||
                    !window.IsActive || frames <= before || (scene is not null && scene.Children.Count == 0))
                    throw new InvalidOperationException($"Restore failed at cycle {i + 1}: visible={window.IsVisible}, enabled={window.IsEnabled}, active={window.IsActive}, state={window.WindowState}, frames={frames - before}, children={scene?.Children.Count ?? 0}.");

                var previous = window.CurrentPageObject;
                var priorTitle = title.Text;
                var target = targets.First(button => button.IsChecked != true);
                target.IsChecked = true;
                await Task.Delay(1100);
                if (ReferenceEquals(previous, window.CurrentPageObject) ||
                    window.CurrentPageObject is not { IsVisible: true, ActualHeight: > 0 } ||
                    window.PageStackOpacity < 0.99 || title.Text == priorTitle)
                    throw new InvalidOperationException("The restored window did not finish navigation.");

                if (scene is not null && !ReferenceEquals(plate, scene.Children[0]))
                    throw new InvalidOperationException("The backdrop was rebuilt on restore.");
                Write($"PASS {i + 1} {(minimize ? "minimize" : "tray")} frames={frames - before} page={window.CurrentPage} reused={scene is not null} navigation={restored.ElapsedMilliseconds}ms gap={maxGap:F0}ms");
            }
            Write("PASS all restore cycles");
            exitCode = 0;
            if (inspectMs > 0) await Task.Delay(inspectMs);
        }
        catch (Exception ex) { Write($"FAIL {ex}"); }
        finally
        {
            heartbeat.Stop();
            CompositionTarget.Rendering -= onFrame;
            app.Shutdown(exitCode);
        }
    }

}
#endif
