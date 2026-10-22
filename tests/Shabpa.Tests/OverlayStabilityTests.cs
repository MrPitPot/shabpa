using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;
using Shabpa.Kit;
using Shabpa.Pages;

namespace Shabpa.Tests;

/// <summary>The overlay corner picker and placement (a corner is where it says, in any language / flow direction / monitor / scale) and
/// the fixed size of the overlay (values change only inside their slots).</summary>
public class OverlayZoneTests
{
    // monitors as the overlay sees them: physical pixels, origin anywhere (a screen left of / above the primary one gives negatives)
    public static TheoryData<int, int, int, int, double> Monitors => new()
    {
        { 0, 0, 1920, 1080, 1.0 },
        { 0, 0, 2560, 1440, 1.25 },
        { 0, 0, 3840, 2160, 1.5 },
        { -1920, 0, 1920, 1080, 1.0 },
        { 1920, -1080, 2560, 1440, 1.75 },
        { -3840, -2160, 3840, 2160, 2.0 },
        { 0, 0, 1366, 768, 1.0 },
    };

    [Theory]
    [MemberData(nameof(Monitors))]
    public void Every_zone_lands_where_its_name_says_and_reads_back_as_the_same_zone(int x, int y, int w, int h, double scale)
    {
        var area = new PxBox(x, y, w, h);
        var (bw, bh) = ((int)(330 * scale), (int)(190 * scale));
        var margin = (int)Math.Round(24 * scale);
        foreach (var zone in OverlayZone.All)
        {
            var (bx, by) = OverlayPlacement.AtZone(zone, bw, bh, area, margin, margin);
            if (zone[1] == 'l') Assert.Equal(area.X + margin, bx);
            if (zone[1] == 'r') Assert.Equal(area.Right - bw - margin, bx);
            if (zone[1] == 'c') Assert.Equal(area.X + (area.W - bw) / 2, bx);
            Assert.Equal(zone[0] == 't' ? area.Y + margin : area.Bottom - bh - margin, by);
            Assert.Equal(zone, OverlayPlacement.ZoneAt(new PxBox(bx, by, bw, bh), area));
        }
        // top right is to the right of top left, bottom is below top: in pixels, always
        var tl = OverlayPlacement.AtZone("tl", bw, bh, area, margin, margin);
        var tr = OverlayPlacement.AtZone("tr", bw, bh, area, margin, margin);
        var bl = OverlayPlacement.AtZone("bl", bw, bh, area, margin, margin);
        Assert.True(tr.x > tl.x);
        Assert.True(bl.y > tl.y);
    }

    [Fact]
    public void A_stale_free_position_can_never_swap_a_left_corner_with_a_right_one()
    {
        // the regression: a free drag stored screen coordinates in X/Y; picking "top right" afterwards used them as margins,
        // pushed the box off the left edge and the clamp put it in the top-left corner (bottom the same)
        var area = new PxBox(0, 0, 1920, 1080);
        foreach (var stale in new[] { 0, 24, 300, 900, 1700, 5000 })
        {
            var tr = OverlayPlacement.AtZone("tr", 330, 190, area, stale, stale);
            Assert.True(tr.x >= area.W / 2 - 330 / 2, $"stale {stale}: right zone ended at x={tr.x}");
            var tl = OverlayPlacement.AtZone("tl", 330, 190, area, stale, stale);
            Assert.True(tl.x <= area.W / 2, $"stale {stale}: left zone ended at x={tl.x}");
            Assert.True(tr.x >= tl.x);
        }
        Assert.True(OverlayPlacement.AtZone("tr", 330, 190, area, 1700, 1700).x >= OverlayPlacement.AtZone("tl", 330, 190, area, 1700, 1700).x);
        Assert.Equal(1920 - 330 - 24, OverlayPlacement.AtZone("tr", 330, 190, area, 24, 24).x);
    }

    [Fact]
    public void Picking_a_corner_after_a_free_spot_resets_the_margins()
    {
        Assert.Equal((24d, 24d), OverlayPlacement.OffsetsForNewAnchor("free", 1700, 40));
        Assert.Equal((24d, 24d), OverlayPlacement.OffsetsForNewAnchor("tl", 1700, 40));
        Assert.Equal((24d, 24d), OverlayPlacement.OffsetsForNewAnchor("tl", -5, 10));
        Assert.Equal((40d, 60d), OverlayPlacement.OffsetsForNewAnchor("tl", 40, 60)); // a margin the user set stays
        Assert.Equal(0, OverlayPlacement.SafeOffset(-3, 1000));
        Assert.Equal(0, OverlayPlacement.SafeOffset(double.NaN, 1000));
        Assert.Equal(500, OverlayPlacement.SafeOffset(9999, 1000));
        Assert.Equal(0, OverlayPlacement.SafeOffset(50, -20));
    }

    [Theory]
    [MemberData(nameof(Monitors))]
    public void Letting_go_near_a_corner_snaps_to_that_corner_flush_and_reports_its_zone(int x, int y, int w, int h, double scale)
    {
        var area = new PxBox(x, y, w, h);
        var (bw, bh) = ((int)(300 * scale), (int)(160 * scale));
        var threshold = (int)Math.Round(36 * scale);
        var near = threshold / 2;
        var cases = new (string zone, int bx, int by)[]
        {
            ("tl", area.X + near, area.Y + near),
            ("tr", area.Right - bw - near, area.Y + near),
            ("bl", area.X + near, area.Bottom - bh - near),
            ("br", area.Right - bw - near, area.Bottom - bh - near),
            ("tc", area.X + (area.W - bw) / 2 + near / 2, area.Y + near),
            ("bc", area.X + (area.W - bw) / 2 - near / 2, area.Bottom - bh - near),
        };
        foreach (var (zone, bx, by) in cases)
        {
            var snapped = OverlayPlacement.Snap(new PxBox(bx, by, bw, bh), area, threshold, 0);
            Assert.Equal(zone, snapped.Zone);
            var (ex, ey) = OverlayPlacement.AtZone(zone, bw, bh, area, 0, 0);
            Assert.Equal((ex, ey), (snapped.Box.X, snapped.Box.Y));
        }
        // far from every edge: no zone, the box stays where it was let go
        var free = new PxBox(area.X + area.W / 5, area.Y + area.H / 3, bw, bh);
        var middle = OverlayPlacement.Snap(free, area, threshold, 0);
        Assert.Null(middle.Zone);
        Assert.Equal(free, middle.Box);
        // dropped half off the monitor: pulled back inside, into the corner
        var outside = OverlayPlacement.Snap(new PxBox(area.Right - bw / 2, area.Bottom - bh / 2, bw, bh), area, threshold, 0);
        Assert.True(outside.Box.Right <= area.Right && outside.Box.Bottom <= area.Bottom && outside.Box.X >= area.X && outside.Box.Y >= area.Y);
        Assert.Equal("br", outside.Zone);
    }

    [Fact]
    public void Zones_are_screen_directions_and_know_nothing_about_flow_direction()
    {
        Assert.Equal([0, 1, 2, 0, 1, 2], OverlayZone.All.Select(OverlayZone.Column));
        Assert.Equal([0, 0, 0, 2, 2, 2], OverlayZone.All.Select(OverlayZone.Row));
        Assert.Equal(ZoneSide.Right, OverlayZone.SideOf("br"));
        Assert.Equal(ZoneSide.Left, OverlayZone.SideOf("free"));
        Assert.Equal(ZoneSide.Left, OverlayZone.SideOf(null));
        Assert.False(OverlayZone.IsBottom("free"));
        foreach (var zone in OverlayZone.All) Assert.Equal(zone, OverlayZone.Key(OverlayZone.SideOf(zone), OverlayZone.IsBottom(zone)));
    }
}

[Collection("Wpf")]
public class AnchorPickerFlowTests
{
    internal static void OnSta(Action work)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { work(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Exception("STA test failed", error);
    }

    internal static void Prepare()
    {
        _ = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Ui.Resources(Palette.Find("night"), "off", 0);
        if (!Application.Current!.Resources.Contains("Num"))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Shabpa;component/Theme.xaml", UriKind.Absolute) });
    }

    [Theory]
    [InlineData(FlowDirection.LeftToRight)]
    [InlineData(FlowDirection.RightToLeft)]
    public void The_picker_puts_every_zone_where_it_is_on_the_screen_in_either_flow_direction(FlowDirection parentFlow)
    {
        var cells = new Dictionary<string, Point>();
        OnSta(() =>
        {
            Prepare();
            var picker = new AnchorPicker(AnchorPicker.Keys.Select(k => (k, k)).ToList(), "tl");
            var host = new Grid { FlowDirection = parentFlow, Width = 400, Height = 300 }; // the settings page of a Persian user is right-to-left
            host.Children.Add(picker);
            host.Measure(new Size(400, 300));
            host.Arrange(new Rect(0, 0, 400, 300));
            host.UpdateLayout();
            var grid = (Grid)picker.Child;
            foreach (var cell in grid.Children.OfType<Border>())
                {
                    var p = cell.TranslatePoint(new Point(cell.ActualWidth / 2, cell.ActualHeight / 2), host);
                    // points in a right-to-left host are measured from its right edge: convert to where they really are on the screen
                    cells[(string)cell.ToolTip] = parentFlow == FlowDirection.RightToLeft ? new Point(host.ActualWidth - p.X, p.Y) : p;
                }
        });
        Assert.Equal(6, cells.Count);
        Assert.True(cells["tl"].X < cells["tc"].X && cells["tc"].X < cells["tr"].X, $"top row runs left to right: {cells["tl"]} {cells["tc"]} {cells["tr"]}");
        Assert.True(cells["bl"].X < cells["bc"].X && cells["bc"].X < cells["br"].X);
        Assert.Equal(cells["tl"].X, cells["bl"].X, 1);
        Assert.Equal(cells["tr"].X, cells["br"].X, 1);
        Assert.True(cells["tl"].Y < cells["bl"].Y && cells["tr"].Y < cells["br"].Y);
    }
}

public class OverlaySlotsTests
{
    [Fact]
    public void Every_reading_has_a_slot_pattern_wide_enough_for_its_extreme_value()
    {
        // (key, widest text the reading can really produce)
        var widest = new (string key, string text)[]
        {
            ("fps", "12345"), ("low1", "99999"), ("frame", "1000.5"), ("hz", "1000"), ("gpu", "100"), ("cpu", "100.0"), ("ram", "100"),
            ("gputemp", "110"), ("hotspot", "105"), ("memtemp", "99"), ("cputemp", "100"), ("gpuclock", "3500"), ("memclock", "3000"),
            ("gpupower", "1000"), ("cpuwatts", "999"), ("gpuvolt", "1300"), ("gpufan", "100"), ("gpufanrpm", "12345"), ("cpufanrpm", "12345"),
            ("vram", "16.0"), ("ramused", "128.5"), ("vcore", "1.31"), ("v12", "12.02"), ("ping", "9999"), ("down", "12345"), ("up", "9999"),
            ("clock", "23:59"), ("gametime", "100:59:59"), ("systemp", "99"), ("pumprpm", "2560"),
        };
        foreach (var (key, text) in widest)
            Assert.True(OverlaySlots.Capacity(key) >= text.Length, $"{key}: slot holds {OverlaySlots.Capacity(key)} characters, '{text}' needs {text.Length}");
    }

    [Fact]
    public void The_size_depends_on_layout_scale_labels_language_and_readings_only()
    {
        var a = new OverlaySettings { Layout = "bar", Scale = 1, ShowLabels = true };
        string[] keys = ["fps", "gpu"];
        var sig = OverlaySlots.Signature(a, keys, true);
        Assert.Equal(sig, OverlaySlots.Signature(a, keys, true));
        // things that must not resize the window
        Assert.Equal(sig, OverlaySlots.Signature(new OverlaySettings { Layout = "bar", Scale = 1, ShowLabels = true, Look = "matrix", BackOpacity = 0.3, Anchor = "br", X = 99, Shadow = false }, keys, true));
        // things that must
        Assert.NotEqual(sig, OverlaySlots.Signature(new OverlaySettings { Layout = "card", Scale = 1, ShowLabels = true }, keys, true));
        Assert.NotEqual(sig, OverlaySlots.Signature(new OverlaySettings { Layout = "bar", Scale = 1.25, ShowLabels = true }, keys, true));
        Assert.NotEqual(sig, OverlaySlots.Signature(new OverlaySettings { Layout = "bar", Scale = 1, ShowLabels = false }, keys, true));
        Assert.NotEqual(sig, OverlaySlots.Signature(a, keys, false));
        Assert.NotEqual(sig, OverlaySlots.Signature(a, ["fps", "gpu", "cpu"], true));
    }

    [Fact]
    public void Missing_and_extreme_samples_keep_the_same_list_of_readings()
    {
        // the list (and with it the structure of every layout) must not depend on the values
        string[] keys = ["fps", "low1", "gpu", "vram", "ping", "clock", "gametime", "hz"];
        var empty = OverlayMetrics.Build(new TelemetrySample(), keys);
        var huge = OverlayMetrics.Build(new TelemetrySample
        {
            Fps = 99999, Low1Fps = 12345, RefreshHz = 1000, PingMs = 100000, Gpu = new GpuReading { LoadPercent = 100, VramUsedMb = 99999, VramTotalMb = 99999 },
            GameTime = TimeSpan.FromHours(120), InGame = true,
        }, keys);
        Assert.Equal(empty.Select(m => m.Key), huge.Select(m => m.Key));
        Assert.Equal(empty.Select(m => m.Label), huge.Select(m => m.Label));
    }
}

[Collection("Wpf")]
public class OverlaySizeTests
{
    private static readonly string[] Values =
        ["—", "-", "0", "9", "99", "100", "1000", "12345", "100.0", "99999", "123456789", "ADMIN", "NVIDIA GeForce RTX 5090 Laptop GPU Extreme Edition", "88:88", "999:59:59"];

    [Fact]
    public void No_layout_changes_size_when_the_numbers_change()
    {
        var report = new List<string>();
        AnchorPickerFlowTests.OnSta(() =>
        {
            AnchorPickerFlowTests.Prepare();
            var keysets = new[] { new[] { "fps", "low1", "gpu", "gputemp", "cpu", "ram", "ping" }, OverlayMetrics.All.Select(m => m.Key).ToArray() };
            foreach (var layout in OverlayLayouts.All.Select(l => l.key))
            foreach (var scale in new[] { 0.75, 1.0, 1.5 })
            foreach (var labels in new[] { true, false })
            foreach (var keys in keysets)
            {
                var o = new OverlaySettings { Layout = layout, Scale = scale, ShowLabels = labels, Look = "matrix", Items = [.. keys] };
                Size? first = null;
                for (var i = 0; i < Values.Length * 2; i++)
                {
                    var metrics = keys.Select(k =>
                    {
                        var def = OverlayMetrics.Find(k)!;
                        var unit = k == "vram" ? (i % 2 == 0 ? "/ 16 GB" : "GB") : def.Unit;
                        return new MetricView(k, OverlayMetrics.Abbrev(k), k == "fpsgraph" ? "graph" : Values[i % Values.Length], unit, i % 4 == 0 ? null : (i % 5) / 4.0, i % 3);
                    }).ToList();
                    var element = OverlayRenderer.Build(o, metrics, []);
                    element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    var size = element.DesiredSize;
                    first ??= size;
                    if (Math.Abs(size.Width - first.Value.Width) > 0.01 || Math.Abs(size.Height - first.Value.Height) > 0.01)
                        report.Add($"{layout} x{scale} labels={labels} keys={keys.Length} value='{Values[i % Values.Length]}': {size} vs {first}");
                }
            }
        });
        Assert.True(report.Count == 0, string.Join("\n", report.Take(10)));
    }
}
