namespace Shabpa.Core;

public enum ZoneSide { Left, Centre, Right }

/// <summary>The six screen zones as keys ("tl" = top left ... "br" = bottom right). Left and right always mean the left and right of the
/// SCREEN, in every language: the picker, the preview and the placement all read the zone through here, never through the flow direction.</summary>
public static class OverlayZone
{
    public static readonly string[] All = ["tl", "tc", "tr", "bl", "bc", "br"];

    public static ZoneSide SideOf(string? zone) => zone is { Length: 2 } ? zone[1] switch { 'r' => ZoneSide.Right, 'c' => ZoneSide.Centre, _ => ZoneSide.Left } : ZoneSide.Left;

    public static bool IsBottom(string? zone) => zone is { Length: 2 } && zone[0] == 'b';

    public static string Key(ZoneSide side, bool bottom) => (bottom ? "b" : "t") + (side switch { ZoneSide.Right => 'r', ZoneSide.Centre => 'c', _ => 'l' });

    /// <summary>Column in a left-to-right picture of the screen: 0 = left edge, 2 = right edge.</summary>
    public static int Column(string? zone) => (int)SideOf(zone);

    /// <summary>Row in the picker (0 = top, 2 = bottom; row 1 holds the "Game" caption).</summary>
    public static int Row(string? zone) => IsBottom(zone) ? 2 : 0;
}
