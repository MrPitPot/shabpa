using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Shabpa;

/// <summary>WPF draws emoji from a colour font as a single flat colour. Segoe UI Emoji keeps its colours as
/// layers (COLR / CPAL tables): one glyph per layer, each with its own palette colour. This reads those tables
/// and draws the layers, so emoji show in their real colours. Anything that cannot be drawn this way falls back
/// to ordinary text.</summary>
public static class ColorEmoji
{
    internal sealed class Layer
    {
        public ushort Glyph;
        public Color? Colour; // null = use the text colour
    }

    private static readonly object Gate = new();
    private static bool _loaded;
    private static GlyphTypeface? _font;
    private static Dictionary<ushort, (int first, int count)>? _bases;
    private static List<Layer>? _layers;

    private static void Load()
    {
        lock (Gate)
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "seguiemj.ttf");
                if (!File.Exists(path)) return;
                var data = File.ReadAllBytes(path);
                var (bases, layers) = Parse(data);
                _bases = bases;
                _layers = layers;
                _font = new GlyphTypeface(new Uri(path));
            }
            catch
            {
                _bases = null;
                _font = null;
            }
        }
    }

    private static ushort U16(byte[] d, int o) => (ushort)((d[o] << 8) | d[o + 1]);
    private static uint U32(byte[] d, int o) => ((uint)d[o] << 24) | ((uint)d[o + 1] << 16) | ((uint)d[o + 2] << 8) | d[o + 3];

    /// <summary>Reads the COLR (layer lists) and CPAL (first palette) tables of a TrueType font file.</summary>
    internal static (Dictionary<ushort, (int first, int count)> bases, List<Layer> layers) Parse(byte[] data)
    {
        var tables = new Dictionary<string, int>();
        var count = U16(data, 4);
        for (var i = 0; i < count; i++)
        {
            var at = 12 + 16 * i;
            tables[System.Text.Encoding.ASCII.GetString(data, at, 4)] = (int)U32(data, at + 8);
        }
        var colr = tables["COLR"];
        var cpal = tables["CPAL"];

        var baseCount = U16(data, colr + 2);
        var baseOffset = colr + (int)U32(data, colr + 4);
        var layerOffset = colr + (int)U32(data, colr + 8);
        var layerCount = U16(data, colr + 12);

        var entries = U16(data, cpal + 2);
        var colourOffset = cpal + (int)U32(data, cpal + 8);
        var firstColour = U16(data, cpal + 12);
        Color Colour(int index)
        {
            var at = colourOffset + (firstColour + index) * 4;
            return Color.FromArgb(data[at + 3], data[at + 2], data[at + 1], data[at]);
        }

        var layers = new List<Layer>(layerCount);
        for (var i = 0; i < layerCount; i++)
        {
            var at = layerOffset + i * 4;
            var palette = U16(data, at + 2);
            layers.Add(new Layer { Glyph = U16(data, at), Colour = palette == 0xFFFF || palette >= entries ? null : Colour(palette) });
        }
        var bases = new Dictionary<ushort, (int first, int count)>(baseCount);
        for (var i = 0; i < baseCount; i++)
        {
            var at = baseOffset + i * 6;
            bases[U16(data, at)] = (U16(data, at + 2), U16(data, at + 4));
        }
        return (bases, layers);
    }

    /// <summary>True for the characters worth drawing as an emoji (pictographs and the common symbols).</summary>
    internal static bool LooksLikeEmoji(int cp) =>
        cp >= 0x1F000 || cp is >= 0x2600 and <= 0x27BF or >= 0x2B00 and <= 0x2BFF or >= 0x2300 and <= 0x23FF
        or 0x00A9 or 0x00AE or 0x203C or 0x2049 or 0x2122 or 0x2139 or >= 0x2190 and <= 0x21FF or >= 0x25A0 and <= 0x25FF;

    /// <summary>A coloured picture of one emoji (a single code point, with or without the VS16 selector), or null when
    /// the font has no colour layers for it.</summary>
    public static FrameworkElement? Element(string emoji, double size)
    {
        Load();
        if (_font is null || _bases is null || _layers is null) return null;
        var cp = char.ConvertToUtf32(emoji, 0);
        if (!_font.CharacterToGlyphMap.TryGetValue(cp, out var glyph)) return null;
        if (!_bases.TryGetValue(glyph, out var entry) || entry.count == 0) return null;
        var layers = _layers.GetRange(entry.first, entry.count);
        return new Picture(_font, layers, size);
    }

    private sealed class Picture : FrameworkElement
    {
        private readonly GlyphTypeface _font;
        private readonly List<Layer> _layers;
        private readonly double _size;

        public Picture(GlyphTypeface font, List<Layer> layers, double size)
        {
            _font = font;
            _layers = layers;
            _size = size;
            Width = size * 1.2;
            Height = size * 1.25;
            IsHitTestVisible = false;
            SnapsToDevicePixels = false;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var inherited = TextElement.GetForeground(this);
            foreach (var layer in _layers)
            {
                if (!_font.AdvanceWidths.TryGetValue(layer.Glyph, out var advance)) advance = 1;
                var run = new GlyphRun(_font, 0, false, _size, (float)dpi, [layer.Glyph], new Point(0, _size * _font.Baseline + _size * 0.06),
                    [advance * _size], null, null, null, null, null, null);
                dc.DrawGlyphRun(layer.Colour is { } c ? new SolidColorBrush(c) : inherited, run);
            }
        }
    }

    /// <summary>Fills <paramref name="box"/> with <paramref name="text"/>, drawing every emoji in colour.</summary>
    public static void Fill(TextBlock box, string text)
    {
        box.Inlines.Clear();
        var size = box.FontSize;
        var plain = new System.Text.StringBuilder();
        void FlushPlain()
        {
            if (plain.Length == 0) return;
            box.Inlines.Add(new Run(plain.ToString()));
            plain.Clear();
        }

        var iterator = StringInfo.GetTextElementEnumerator(text);
        while (iterator.MoveNext())
        {
            var piece = (string)iterator.Current;
            var cp = char.ConvertToUtf32(piece, 0);
            if (LooksLikeEmoji(cp) && Element(piece, size) is { } picture)
            {
                FlushPlain();
                box.Inlines.Add(new InlineUIContainer(picture) { BaselineAlignment = BaselineAlignment.Center });
            }
            else plain.Append(piece);
        }
        FlushPlain();
    }

    /// <summary>A TextBlock-free emoji for places that build elements directly (tiles, hourly cards).</summary>
    public static UIElement Icon(string emoji, double size, Brush? fallback = null)
    {
        if (Element(emoji, size) is { } picture) return picture;
        return new TextBlock { Text = emoji, FontSize = size, FontFamily = new FontFamily("Segoe UI Emoji"), Foreground = fallback ?? Brushes.White };
    }
}
