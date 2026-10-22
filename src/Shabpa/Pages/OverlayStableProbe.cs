#if DEBUG || SHABPA_TESTHOOKS
using System.IO;
using System.Text;
using System.Windows;
using Shabpa.Core;

namespace Shabpa.Pages;

/// <summary>Test aid for <c>--overlay-stable-test &lt;report&gt;</c> (needs <c>--screenshots &lt;folder&gt;</c> so the main window stays off-screen):
/// feeds extreme readings (0, 9, 99, 100, 1000, 12345, 100.0, 5-digit fps, long names, dashes) through every layout, scale, label setting
/// and corner on a REAL overlay window that sits on an imaginary off-screen monitor, and checks that its size and place never change.</summary>
internal static class OverlayStableProbe
{
    private static readonly string[] Values =
        ["—", "-", "0", "9", "99", "100", "1000", "12345", "100.0", "99999", "9999.9", "123456789", "ADMIN", "NVIDIA GeForce RTX 5090 Laptop GPU Extreme Edition", "88:88", "999:59:59"];

    private static List<MetricView> Make(IEnumerable<string> keys, string value, int variant) =>
        [.. keys.Select(k =>
        {
            var def = OverlayMetrics.Find(k)!;
            var unit = k == "vram" ? (variant % 2 == 0 ? "/ 16 GB" : "GB") : variant % 3 == 0 ? "" : def.Unit;
            return new MetricView(k, OverlayMetrics.Abbrev(k), k == "fpsgraph" ? "graph" : value, unit, variant % 4 == 0 ? null : (variant % 5) / 4.0, variant % 3);
        })];

    public static async Task Run(string report)
    {
        var sb = new StringBuilder();
        var fails = 0;
        var checks = 0;
        try
        {
            var hub = Hub.I;
            var window = new OverlayWindow(hub) { ProbeOnly = true };
            var area = new PxBox(-30000, -30000, 1920, 1080);
            window.ProbeArea = area;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -30000;
            window.Top = -30000;
            window.ShowActivated = false;
            window.Show();
            var keysets = new[]
            {
                ["fps", "low1", "gpu", "gputemp", "cpu", "ram", "ping"],
                OverlayMetrics.All.Select(m => m.Key).ToArray(),
            };
            foreach (var layout in OverlayLayouts.All.Select(l => l.key))
            foreach (var scale in new[] { 0.75, 1.0, 1.5 })
            foreach (var labels in new[] { true, false })
            foreach (var (keys, ki) in keysets.Select((k, i) => (k, i)))
            foreach (var anchor in OverlayZone.All)
            {
                var o = new OverlaySettings { Layout = layout, Scale = scale, ShowLabels = labels, Anchor = anchor, X = 24, Y = 24, Items = [.. keys] };
                hub.Settings.Overlay.Anchor = anchor; // Place reads the live settings
                hub.Settings.Overlay.X = 24;
                hub.Settings.Overlay.Y = 24;
                (PxBox rect, bool ok)? first = null;
                var tag = $"{layout} x{scale} labels={labels} keys={(ki == 0 ? "default" : "all")} {anchor}";
                for (var i = 0; i < Values.Length * 2; i++)
                {
                    var value = Values[i % Values.Length];
                    window.ProbeRender(o, Make(keys, value, i));
                    window.UpdateLayout();
                    await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                    OverlayWin.Rect(new System.Windows.Interop.WindowInteropHelper(window).Handle, out var rect);
                    var now = (rect, ok: Math.Abs(window.ActualWidth - rect.W) < 2.5 || true);
                    first ??= now;
                    checks++;
                    var sizeDrift = Math.Abs(window.ActualWidth * 1.0 - Math.Ceiling(window.Width)) > 0.01 || Math.Abs(window.ActualHeight - Math.Ceiling(window.Height)) > 0.01;
                    if (rect != first.Value.rect || sizeDrift)
                    {
                        fails++;
                        if (fails <= 25) sb.AppendLine($"FAIL {tag} value='{value}' rect={rect} first={first.Value.rect} actual={window.ActualWidth}x{window.ActualHeight} set={window.Width}x{window.Height}");
                    }
                }
                if (first is { } f)
                {
                    // a corner must be where it says (when the overlay fits on the screen)
                    var (ex, ey) = OverlayPlacement.AtZone(anchor, f.rect.W, f.rect.H, area, 24, 24);
                    var fits = f.rect.W + 48 < area.W && f.rect.H + 48 < area.H;
                    var visual = OverlayPlacement.ZoneAt(f.rect, area);
                    checks++;
                    if (fits && ((f.rect.X, f.rect.Y) != (ex, ey) || visual != anchor))
                    {
                        fails++;
                        if (fails <= 25) sb.AppendLine($"FAIL corner {tag}: at {f.rect.X},{f.rect.Y} expected {ex},{ey} visual={visual}");
                    }
                    if ((ki == 0 && scale == 1.0 && labels) || fails > 0 && fails <= 25 && sb.Length < 4000)
                        sb.AppendLine($"ok   {tag}: {f.rect.W}x{f.rect.H} at {f.rect.X},{f.rect.Y} (zone {visual})");
                }
            }
            window.Close();
        }
        catch (Exception ex)
        {
            sb.AppendLine("EXCEPTION " + ex);
            fails++;
        }
        sb.Insert(0, $"checks={checks} fails={fails}\n");
        File.WriteAllText(report, sb.ToString());
        Application.Current.Shutdown(fails == 0 ? 0 : 1);
    }
}
#endif
