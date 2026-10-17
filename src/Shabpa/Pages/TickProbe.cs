using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>Test aid for <c>--tick-test &lt;report&gt;</c> (needs <c>--screenshots &lt;folder&gt;</c>): opens the design kit, ticks the empty
/// checkbox and the toast, and saves frames of the draw-in (the checkbox row) plus the TickMark.Progress samples, so the direction
/// (left to right) can be checked in both languages.</summary>
internal static class TickProbe
{
    // the checkbox sits somewhere in a scrolled page: paint it through a brush so only the element itself is captured
    private static void Snap(FrameworkElement e, string file)
    {
        e.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(e);
        int w = (int)Math.Ceiling(e.ActualWidth * dpi.DpiScaleX), h = (int)Math.Ceiling(e.ActualHeight * dpi.DpiScaleY);
        var dv = new System.Windows.Media.DrawingVisual();
        using (var dc = dv.RenderOpen())
            dc.DrawRectangle(new System.Windows.Media.VisualBrush(e) { Stretch = System.Windows.Media.Stretch.None, AlignmentX = System.Windows.Media.AlignmentX.Left, AlignmentY = System.Windows.Media.AlignmentY.Top }, null, new Rect(0, 0, e.ActualWidth, e.ActualHeight));
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, dpi.PixelsPerInchX, dpi.PixelsPerInchY, System.Windows.Media.PixelFormats.Pbgra32);
        bmp.Render(dv);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        // an off-screen RTL element is not mirrored by its window, so undo it like the screenshot aid does
        System.Windows.Media.Imaging.BitmapSource img = e.FlowDirection == FlowDirection.RightToLeft ? new System.Windows.Media.Imaging.TransformedBitmap(bmp, new System.Windows.Media.ScaleTransform(-1, 1)) : bmp;
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(img));
        using var fs = File.Create(file);
        enc.Save(fs);
    }

    public static async Task Run(MainWindow window, string report, string folder, Action<FrameworkElement, string> save)
    {
        Directory.CreateDirectory(folder);
        var sb = new StringBuilder();
        window.Navigate("kit");
        await Task.Delay(1800);
        var box = MotionAudit.All(window).OfType<CheckBox>().FirstOrDefault(c => c is not Switch && c.IsChecked != true);
        if (box is null) { File.WriteAllText(report, "no unchecked checkbox found"); Application.Current.Shutdown(1); return; }
        var tick = MotionAudit.All(box).OfType<TickMark>().First();
        var target = (FrameworkElement)box;
        sb.AppendLine($"motion enabled={Fx.Enabled} lang={(L.En ? "en" : "fa")} before: progress={tick.Progress:0.00} isOn={tick.IsOn}");
        box.IsChecked = true;
        var started = DateTime.UtcNow;
        var frames = new[] { 40, 130, 190, 240, 290, 340, 440, 700 };
        foreach (var at in frames)
        {
            var wait = at - (int)(DateTime.UtcNow - started).TotalMilliseconds;
            if (wait > 0) await Task.Delay(wait);
            window.UpdateLayout();
            var t = (int)(DateTime.UtcNow - started).TotalMilliseconds;
            sb.AppendLine($"t={t}ms progress={tick.Progress:0.00}");
            Snap(target, Path.Combine(folder, $"tick-{at:000}.png"));
        }
        sb.AppendLine($"after: progress={tick.Progress:0.00} animated={DependencyPropertyHelper.GetValueSource(tick, TickMark.ProgressProperty).IsAnimated}");
        box.IsChecked = false;
        sb.AppendLine($"unchecked: progress={tick.Progress:0.00}");
        File.WriteAllText(report, sb.ToString());
        Application.Current.Shutdown(0);
    }
}
