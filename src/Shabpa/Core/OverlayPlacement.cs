namespace Shabpa.Core;

/// <summary>A rectangle in physical screen pixels (the overlay works in pixels so several monitors with different scaling agree).</summary>
public readonly record struct PxBox(int X, int Y, int W, int H)
{
    public int Right => X + W;
    public int Bottom => Y + H;
}

/// <summary>Where the overlay may sit: kept inside the monitor, and pulled to its corners and edges when it is let go near them.
/// Pure arithmetic, so it is covered by tests.</summary>
public static class OverlayPlacement
{
    /// <summary>Moves <paramref name="box"/> back inside <paramref name="area"/> (a box bigger than the area is pinned to its top-left).</summary>
    public static PxBox Clamp(PxBox box, PxBox area)
    {
        var x = box.W >= area.W ? area.X : Math.Clamp(box.X, area.X, area.Right - box.W);
        var y = box.H >= area.H ? area.Y : Math.Clamp(box.Y, area.Y, area.Bottom - box.H);
        return box with { X = x, Y = y };
    }

    /// <summary>The top-left of a box of the given size at a screen zone ("tl", "tc", "tr", "bl", "bc", "br"), <paramref name="offX"/> from the side
    /// and <paramref name="offY"/> from the top or bottom.</summary>
    public static (int x, int y) AtZone(string zone, int w, int h, PxBox area, int offX, int offY)
    {
        var x = zone switch
        {
            "tr" or "br" => area.Right - w - offX,
            "tc" or "bc" => area.X + (area.W - w) / 2,
            _ => area.X + offX,
        };
        var y = zone is "bl" or "br" or "bc" ? area.Bottom - h - offY : area.Y + offY;
        return (x, y);
    }

    /// <summary>The result of letting the overlay go: where it ends up, and the zone it snapped into if it landed in a corner or on the
    /// top/bottom middle (otherwise <c>null</c>: it stays where it is, only nudged against the edges it is close to).</summary>
    public readonly record struct Snapped(PxBox Box, string? Zone);

    /// <summary>Snaps to a monitor edge when within <paramref name="threshold"/> pixels of it (leaving <paramref name="margin"/> free), to the
    /// horizontal middle when close to it, and keeps the whole box on the monitor.</summary>
    public static Snapped Snap(PxBox box, PxBox area, int threshold, int margin)
    {
        char? h = null, v = null;
        var x = box.X;
        var y = box.Y;
        if (box.X - area.X <= threshold) { x = area.X + margin; h = 'l'; }
        else if (area.Right - box.Right <= threshold) { x = area.Right - box.W - margin; h = 'r'; }
        else if (Math.Abs(box.X + box.W / 2 - (area.X + area.W / 2)) <= threshold) { x = area.X + (area.W - box.W) / 2; h = 'c'; }
        if (box.Y - area.Y <= threshold) { y = area.Y + margin; v = 't'; }
        else if (area.Bottom - box.Bottom <= threshold) { y = area.Bottom - box.H - margin; v = 'b'; }
        var snapped = Clamp(box with { X = x, Y = y }, area);
        var zone = h is not null && v is not null ? $"{v}{h}" : null;
        return new Snapped(snapped, zone);
    }

    /// <summary>The monitor that holds most of <paramref name="box"/>, or the nearest one when it is off every monitor (an unplugged screen).</summary>
    public static PxBox Monitor(PxBox box, IReadOnlyList<PxBox> monitors)
    {
        if (monitors.Count == 0) return box;
        long best = -1;
        var pick = monitors[0];
        foreach (var m in monitors)
        {
            var w = Math.Max(0, Math.Min(box.Right, m.Right) - Math.Max(box.X, m.X));
            var h = Math.Max(0, Math.Min(box.Bottom, m.Bottom) - Math.Max(box.Y, m.Y));
            long overlap = (long)w * h;
            if (overlap > best) { best = overlap; pick = m; }
        }
        if (best > 0) return pick;
        // off every monitor: the one whose centre is nearest
        var (cx, cy) = (box.X + box.W / 2.0, box.Y + box.H / 2.0);
        return monitors.OrderBy(m => Math.Pow(m.X + m.W / 2.0 - cx, 2) + Math.Pow(m.Y + m.H / 2.0 - cy, 2)).First();
    }
}
