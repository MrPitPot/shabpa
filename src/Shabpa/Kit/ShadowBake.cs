using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Shabpa.Kit;

/// <summary>Draws the layered card shadow from a few small, shared, frozen bitmaps instead of ~20 rounded shapes plus a
/// knock-out clip per card. The vector version asks the graphics card to tessellate (and, while a card is moving, to
/// keep "realisations" of) every one of those shapes for every card: about 12 MB of RAM on the Home page and a lot of
/// per-frame work. Here the shadow of one reference card is rendered once per (colour, radius, blur...) and cut into the
/// usual 9-slice: four corners and four edges, with the middle (the card itself, where the shadow is knocked out anyway)
/// left empty. A card then costs eight cheap image draws that all share the same textures.
/// <para>Boxes too small for the slices (buttons) keep the vector version, which is cheap for small shapes.</para></summary>
internal static class ShadowBake
{
    private sealed class Slices
    {
        /// <summary>Row-major 3x3, the centre (index 4) is null.</summary>
        public readonly BitmapSource?[] Piece = new BitmapSource?[9];
        /// <summary>Pixels: margin outside the card (left/right/top/bottom) and slice inside the card.</summary>
        public int Ml, Mr, Mt, Mb, Cl, Cr, Ct, Cb;
        /// <summary>DIPs: smaller boxes than this keep the vector shadow (some of the inset layers would vanish in them).</summary>
        public double MinW, MinH;
    }

    private const int MidPx = 2;
    private static readonly Dictionary<(Color, double, double, double, double, int, int), Slices?> Cache = [];

    /// <summary>True when the shadow was drawn from the bitmaps; false when the caller must draw the vector version.</summary>
    public static bool TryDraw(DrawingContext dc, double w, double h, Color color, double radius, double blur, double spread,
        double offsetY, int layers, double scale)
    {
        var s = Math.Clamp(Math.Round(scale * 100) / 100, 1, 4);
        var key = (color, radius, blur, spread, offsetY, layers, (int)Math.Round(s * 100));
        if (!Cache.TryGetValue(key, out var sl))
        {
            try { sl = Bake(color, radius, blur, spread, offsetY, layers, s); }
            catch (Exception ex) { Shabpa.Core.Log.Write("shadow bake failed: " + ex.Message); sl = null; }
            Cache[key] = sl;
        }
        if (sl is null) return false;

        double x0 = -sl.Ml / s, x1 = sl.Cl / s, x2 = w - sl.Cr / s, x3 = w + sl.Mr / s;
        double y0 = -sl.Mt / s, y1 = sl.Ct / s, y2 = h - sl.Cb / s, y3 = h + sl.Mb / s;
        if (w < sl.MinW || h < sl.MinH || x2 - x1 < 0.5 || y2 - y1 < 0.5) return false; // too small for the slices

        double[] xs = [x0, x1, x2, x3], ys = [y0, y1, y2, y3];
        for (var row = 0; row < 3; row++)
            for (var col = 0; col < 3; col++)
            {
                var piece = sl.Piece[row * 3 + col];
                if (piece is null) continue;
                dc.DrawImage(piece, new Rect(xs[col], ys[row], xs[col + 1] - xs[col], ys[row + 1] - ys[row]));
            }
        return true;
    }

    private static Slices? Bake(Color color, double radius, double blur, double spread, double offsetY, int layers, double s)
    {
        if (layers < 1 || color.A == 0) return null;

        // how far the layers reach outside the card, and how far into it their rounded corners curve (all in DIPs)
        double outL = 0, outT = 0, outB = 0, inX = radius, inTop = radius, inBottom = radius, maxAbsG = 0;
        for (var k = 0; k < layers; k++)
        {
            var t = layers == 1 ? 0.5 : k / (double)(layers - 1);
            var g = spread + blur * (0.5 - t);
            var rad = Math.Max(0, radius + g);
            maxAbsG = Math.Max(maxAbsG, Math.Abs(g));
            outL = Math.Max(outL, g);
            outT = Math.Max(outT, g - offsetY);
            outB = Math.Max(outB, offsetY + g);
            inX = Math.Max(inX, rad - g);
            inTop = Math.Max(inTop, offsetY - g + rad);
            inBottom = Math.Max(inBottom, rad - offsetY - g);
        }
        int Px(double dip, int extra) => (int)Math.Ceiling(dip * s) + extra;
        var ml = Px(outL, 2); var mr = ml; var mt = Px(outT, 2); var mb = Px(outB, 2);
        var cl = Px(inX, 3); var cr = cl; var ct = Px(inTop, 3); var cb = Px(inBottom, 3);

        // The reference card is a bit larger than the slices need, so that every layer (also the inset ones) exists in it like in a real card.
        var extra = (int)Math.Ceiling(2 * maxAbsG * s) + 12;
        int cardW = cl + MidPx + cr + extra, cardH = ct + MidPx + cb + extra;
        int totalW = ml + cardW + mr, totalH = mt + cardH + mb;
        if (totalW > 1024 || totalH > 1024) return null;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(s, s));
            dc.PushTransform(new TranslateTransform(ml / s, mt / s));
            Shadow.DrawVector(dc, cardW / s, cardH / s, color, radius, blur, spread, offsetY, layers, true);
        }
        var bmp = new RenderTargetBitmap(totalW, totalH, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();

        var sl = new Slices { Ml = ml, Mr = mr, Mt = mt, Mb = mb, Cl = cl, Cr = cr, Ct = ct, Cb = cb, MinW = 2 * maxAbsG + 4, MinH = 2 * maxAbsG + 4 };
        // columns / rows to cut: the left corner, a thin strip from the middle of the reference card, the right corner
        int midX = ml + (cardW - MidPx) / 2, midY = mt + (cardH - MidPx) / 2;
        int[] colFrom = [0, midX, ml + cardW - cr], colLen = [ml + cl, MidPx, cr + mr];
        int[] rowFrom = [0, midY, mt + cardH - cb], rowLen = [mt + ct, MidPx, cb + mb];
        for (var row = 0; row < 3; row++)
            for (var col = 0; col < 3; col++)
            {
                if (row == 1 && col == 1) continue;
                var piece = new CroppedBitmap(bmp, new Int32Rect(colFrom[col], rowFrom[row], colLen[col], rowLen[row]));
                piece.Freeze();
                sl.Piece[row * 3 + col] = piece;
            }
        return sl;
    }
}
