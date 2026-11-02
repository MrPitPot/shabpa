using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Shabpa.Core;

namespace Shabpa.Pages;

/// <summary>The picture of the PC and the desk: the case with its cooler, memory, graphics card, fans, strip and board accents, and beside it a mouse pad with a keyboard and a mouse.
/// Every light is drawn in the colour the effect gives it right now, and every part can be clicked to set it on its own (<see cref="ZonePicked"/>). Plain shapes only, a few hundred a frame,
/// and nothing is drawn unless the page is in front.</summary>
internal sealed class RgbPreview : FrameworkElement
{
    private const double W = 1000, H = 520;
    private readonly Dictionary<string, Rgb[]> _frames = [];
    private readonly Dictionary<string, Rect> _hits = [];
    private static readonly Dictionary<uint, SolidColorBrush> Brushes = [];
    private string _hover = "";
    private string _selected = "";

    /// <summary>The key of the part that was clicked.</summary>
    public event Action<string>? ZonePicked;

    public RgbPreview()
    {
        SnapsToDevicePixels = false;
        Cursor = Cursors.Arrow;
        MouseMove += (_, e) =>
        {
            var z = At(e.GetPosition(this));
            if (z == _hover) return;
            _hover = z;
            Cursor = z.Length > 0 ? Cursors.Hand : Cursors.Arrow;
            InvalidateVisual();
        };
        MouseLeave += (_, _) => { if (_hover.Length == 0) return; _hover = ""; Cursor = Cursors.Arrow; InvalidateVisual(); };
        MouseLeftButtonUp += (_, e) => { var z = At(e.GetPosition(this)); if (z.Length > 0) ZonePicked?.Invoke(z); };
    }

    /// <summary>The part being edited gets a ring around it.</summary>
    public void Select(string zone)
    {
        if (_selected == zone) return;
        _selected = zone;
        InvalidateVisual();
    }

    public void Show(Func<string, Rgb[]> frameOf)
    {
        foreach (var z in RgbZones.All) _frames[z.Key] = frameOf(z.Key);
        InvalidateVisual();
    }

    private string At(Point p) => _hits.Where(h => h.Value.Contains(p)).Select(h => h.Key).FirstOrDefault() ?? "";

    private static SolidColorBrush Brush(Rgb c, double alpha = 1)
    {
        var key = ((uint)(alpha * 255) << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
        if (Brushes.TryGetValue(key, out var cached)) return cached;
        if (Brushes.Count > 3000) Brushes.Clear();
        var brush = new SolidColorBrush(Color.FromArgb((byte)(alpha * 255), c.R, c.G, c.B));
        brush.Freeze();
        return Brushes[key] = brush;
    }

    private Rgb[] Frame(string zone) => _frames.TryGetValue(zone, out var f) && f.Length > 0 ? f : new Rgb[RgbZones.Find(zone).Leds];

    private static Rgb Mix(IReadOnlyList<Rgb> frame, double t) => frame.Count == 0 ? Rgb.Black : frame[Math.Clamp((int)Math.Round(t * (frame.Count - 1)), 0, frame.Count - 1)];

    private static bool Dark(Rgb c) => c.R + c.G + c.B < 14;

    private void Dot(DrawingContext dc, Rgb c, double x, double y, double r)
    {
        if (Dark(c)) { dc.DrawEllipse(Brush(new Rgb(70, 70, 84), 0.45), null, new Point(x, y), r * 0.5, r * 0.5); return; }
        dc.DrawEllipse(Brush(c, 0.22), null, new Point(x, y), r * 2.1, r * 2.1);
        dc.DrawEllipse(Brush(c), null, new Point(x, y), r, r);
    }

    private void Ring(DrawingContext dc, Rgb[] leds, int from, int count, double cx, double cy, double radius, double dot)
    {
        for (var i = 0; i < count; i++)
        {
            var a = i / (double)count * Math.PI * 2 - Math.PI / 2;
            Dot(dc, leds[Math.Min(leds.Length - 1, from + i)], cx + Math.Cos(a) * radius, cy + Math.Sin(a) * radius, dot);
        }
    }

    private void Part(DrawingContext dc, string zone, Rect box)
    {
        _hits[zone] = box;
        if (zone != _hover && zone != _selected) return;
        var ring = zone == _selected ? Brush(new Rgb(139, 92, 246), 0.95) : Brush(new Rgb(200, 200, 220), 0.45);
        dc.DrawRoundedRectangle(null, new Pen(ring, zone == _selected ? 2.2 : 1.4) { DashStyle = zone == _selected ? DashStyles.Solid : DashStyles.Dash }, Rect.Inflate(box, 6, 6), 12, 12);
    }

    private FormattedText Text(string text, double size, Rgb colour, double alpha = 1) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(Application.Current?.Resources["Ui"] as FontFamily ?? new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            size, Brush(colour, alpha), VisualTreeHelper.GetDpi(this).PixelsPerDip);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 80 || h < 60) return;
        _hits.Clear();
        var s = Math.Min(w / W, h / H);
        var ox = (w - W * s) / 2;
        var oy = (h - H * s) / 2;
        double X(double v) => v * s + ox;
        double Y(double v) => v * s + oy;
        Rect R(double x, double y, double wd, double ht) => new(X(x), Y(y), wd * s, ht * s);

        dc.DrawRoundedRectangle(Brush(new Rgb(11, 11, 17)), null, new Rect(0, 0, w, h), 18, 18);

        // ---- the case -----------------------------------------------------------------------------------
        dc.DrawRoundedRectangle(Brush(new Rgb(17, 17, 25)), new Pen(Brush(new Rgb(58, 58, 74)), 2), R(30, 24, 450, 472), 26 * s, 26 * s);
        dc.DrawRoundedRectangle(Brush(new Rgb(13, 13, 20)), new Pen(Brush(new Rgb(40, 40, 54)), 1), R(48, 42, 414, 436), 16 * s, 16 * s);

        // motherboard accents along the top
        var board = Frame("board");
        for (var i = 0; i < 8; i++) Dot(dc, Mix(board, i / 7.0), X(100 + i * 24), Y(74), 4.2 * s);
        Part(dc, "board", R(88, 62, 190, 24));

        // CPU cooler (a pump ring) with the Shabpa mark
        var cooler = Frame("cooler");
        dc.DrawEllipse(Brush(new Rgb(24, 24, 34)), new Pen(Brush(new Rgb(44, 44, 58)), 1.5), new Point(X(150), Y(170)), 60 * s, 60 * s);
        Ring(dc, cooler, 0, 12, X(150), Y(170), 46 * s, 4.6 * s);
        dc.DrawEllipse(Brush(new Rgb(10, 10, 16)), null, new Point(X(150), Y(170)), 28 * s, 28 * s);
        var mark = Text("شبپا", 15 * s, Mix(cooler, 0.5).R + Mix(cooler, 0.5).G + Mix(cooler, 0.5).B > 40 ? Mix(cooler, 0.5) : new Rgb(120, 120, 140));
        dc.DrawText(mark, new Point(X(150) - mark.Width / 2, Y(170) - mark.Height / 2));
        Part(dc, "cooler", R(86, 106, 128, 128));

        // memory: two sticks
        var ram = Frame("ram");
        for (var m = 0; m < 2; m++)
        {
            dc.DrawRoundedRectangle(Brush(new Rgb(26, 26, 36)), null, R(244 + m * 34, 96, 20, 170), 5 * s, 5 * s);
            for (var i = 0; i < 8; i++) Dot(dc, Mix(ram, (m * 8 + i) / 15.0), X(254 + m * 34), Y(112 + i * 20), 4.6 * s);
        }
        Part(dc, "ram", R(240, 92, 62, 178));

        // graphics card
        var gpu = Frame("gpu");
        dc.DrawRoundedRectangle(Brush(new Rgb(23, 23, 32)), new Pen(Brush(new Rgb(44, 44, 58)), 1), R(76, 300, 270, 56), 10 * s, 10 * s);
        for (var i = 0; i < 10; i++) Dot(dc, Mix(gpu, i / 9.0), X(100 + i * 24), Y(334), 4.6 * s);
        var gpuName = Text("GPU", 12 * s, new Rgb(130, 130, 150), 0.6);
        dc.DrawText(gpuName, new Point(X(98), Y(310)));
        Part(dc, "gpu", R(76, 300, 270, 56));

        // fans: three rings
        var fans = Frame("fans");
        for (var f = 0; f < 3; f++)
        {
            var cy = 120 + f * 130.0;
            dc.DrawRoundedRectangle(Brush(new Rgb(20, 20, 29)), new Pen(Brush(new Rgb(44, 44, 58)), 1), R(344, cy - 58, 116, 116), 18 * s, 18 * s);
            dc.DrawEllipse(Brush(new Rgb(12, 12, 18)), null, new Point(X(402), Y(cy)), 50 * s, 50 * s);
            Ring(dc, fans, f * 12, 12, X(402), Y(cy), 44 * s, 4.6 * s);
            dc.DrawEllipse(Brush(new Rgb(34, 34, 46)), null, new Point(X(402), Y(cy)), 12 * s, 12 * s);
        }
        Part(dc, "fans", R(344, 62, 116, 368));

        // strip along the bottom
        var strip = Frame("strip");
        dc.DrawRoundedRectangle(Brush(new Rgb(20, 20, 29)), null, R(70, 424, 340, 22), 8 * s, 8 * s);
        for (var i = 0; i < 24; i++) Dot(dc, Mix(strip, i / 23.0), X(82 + i * 14.2), Y(435), 3.6 * s);
        Part(dc, "strip", R(70, 424, 340, 22));

        // ---- the desk ------------------------------------------------------------------------------------
        var kb = Frame("keyboard");
        var mouse = Frame("mouse");
        var pad = R(520, 96, 450, 390);
        var glow = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        for (var i = 0; i < 5; i++) { var c = Mix(kb, i / 4.0); glow.GradientStops.Add(new GradientStop(Color.FromArgb(Dark(c) ? (byte)20 : (byte)95, c.R, c.G, c.B), i / 4.0)); }
        glow.Freeze();
        dc.DrawRoundedRectangle(Brush(new Rgb(15, 15, 22)), new Pen(glow, 3), pad, 26 * s, 26 * s);
        dc.DrawRoundedRectangle(glow, null, Rect.Inflate(pad, -10, -10), 20 * s, 20 * s);

        // keyboard: five rows of keys, the effect runs left to right across the columns
        var top = 150.0;
        var kbBox = R(548, top - 14, 322, 190);
        dc.DrawRoundedRectangle(Brush(new Rgb(12, 12, 18), 0.92), new Pen(Brush(new Rgb(44, 44, 58)), 1.2), kbBox, 14 * s, 14 * s);
        const double pitch = 296.0 / 14;
        // rows of (start, width) in key units; the last row has the space bar
        (double at, double span)[][] rows =
        [
            [.. Enumerable.Range(0, 14).Select(k => ((double)k, 1.0))],
            [(0, 1.5), .. Enumerable.Range(0, 12).Select(k => (1.5 + k, 1.0)), (13.5, 0.5)],
            [(0, 1.8), .. Enumerable.Range(0, 11).Select(k => (1.8 + k, 1.0)), (12.8, 1.2)],
            [(0, 2.2), .. Enumerable.Range(0, 10).Select(k => (2.2 + k, 1.0)), (12.2, 1.8)],
            [(0, 1.4), (1.4, 1.2), (2.6, 6.2), (8.8, 1.2), (10, 1.2), (11.2, 1.2), (12.4, 1.6)],
        ];
        for (var row = 0; row < rows.Length; row++)
            foreach (var (at, span) in rows[row])
            {
                var c = Mix(kb, (at + span / 2) / 14.0);
                var cell = R(560 + at * pitch, top + row * 34, span * pitch - 4, 28);
                dc.DrawRoundedRectangle(Dark(c) ? Brush(new Rgb(22, 22, 30)) : Brush(c, 0.18), new Pen(Dark(c) ? Brush(new Rgb(48, 48, 62)) : Brush(c, 0.95), 1.4), cell, 4 * s, 4 * s);
            }
        Part(dc, "keyboard", kbBox);

        // mouse
        var mBox = R(896, 168, 56, 100);
        dc.DrawRoundedRectangle(Brush(new Rgb(17, 17, 25)), new Pen(Brush(Dark(Mix(mouse, 0.2)) ? new Rgb(58, 58, 74) : Mix(mouse, 0.2), 0.9), 2), mBox, 26 * s, 26 * s);
        Dot(dc, Mix(mouse, 0.5), X(924), Y(200), 5.5 * s);
        Dot(dc, Mix(mouse, 1.0), X(924), Y(236), 7 * s);
        Part(dc, "mouse", mBox);

        var hint = Text(L.T("روی هر قطعه کلیک کن تا جدا تنظیمش کنی", "Click a part to set it on its own"), 14 * s, new Rgb(140, 140, 160), 0.7);
        dc.DrawText(hint, new Point(X(745) - hint.Width / 2, Y(430)));
    }
}
