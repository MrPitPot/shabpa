using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using Shabpa.Core;

namespace Shabpa;

/// <summary>XAML side of <see cref="L"/>: <c>Text="{s:Tr 'فارسی', 'English'}"</c>.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class Tr(string fa, string en) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(fa, en);
}

/// <summary>Language direction and colour theme of the running app. Both are fixed at startup;
/// changing either restarts the app so every screen is built fresh.</summary>
public static class Ui
{
    public static Palette Theme { get; private set; } = Palette.Find(null);

    /// <summary>"off", "mica" or "acrylic" for this run (fixed at start, like the theme).</summary>
    public static string Glass { get; private set; } = "off";

    public static bool IsGlass => Glass is "mica" or "acrylic";

    /// <summary>0 to 1: how much of the theme's tint stays over the backdrop (0 = as clear as it gets).</summary>
    public static double GlassLevel { get; private set; } = 0.35;

    /// <summary>Glass: the theme's background and panel colours are replaced by neutral, see-through layers (white veils on
    /// a dark theme, milky ones on a light theme) so the Windows backdrop is what the window looks like; the theme only
    /// leaves a faint halo and its accent colour.</summary>
    private static System.Windows.Media.Color GlassLayer(Palette palette, string key, System.Windows.Media.Color original)
    {
        var light = palette.Light;
        var k = 0.4 + 1.2 * GlassLevel; // the person's clarity slider
        byte a(byte dark, byte bright) => (byte)Math.Clamp((light ? bright : dark) * k, 0, 255);
        System.Windows.Media.Color veil(byte alpha) => light ? System.Windows.Media.Color.FromArgb(alpha, 255, 255, 255) : System.Windows.Media.Color.FromArgb(alpha, 255, 255, 255);
        return key switch
        {
            "Panel" => veil(a(0x16, 0x99)),
            "Panel2" => veil(a(0x22, 0xB4)),
            "Panel3" => veil(a(0x30, 0xCC)),
            "Line" => System.Windows.Media.Color.FromArgb(a(0x38, 0x30), light ? (byte)0 : (byte)255, light ? (byte)0 : (byte)255, light ? (byte)0 : (byte)255),
            _ => original,
        };
    }

    public static string CleanGlass(string? value) => value is "mica" or "acrylic" ? value : "off";

    public static FlowDirection Flow => L.En ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;

    private static Color Color(Palette palette, string key)
    {
        var (r, g, b) = palette.Rgb(key);
        return System.Windows.Media.Color.FromRgb(r, g, b);
    }

    /// <summary>Builds the brush resources the styles in Theme.xaml refer to.</summary>
    public static ResourceDictionary Resources(Palette palette, string glass = "off", double glassLevel = 0.35)
    {
        Theme = palette;
        GlassLevel = Math.Clamp(glassLevel, 0, 1);
        Glass = CleanGlass(glass);
        var resources = new ResourceDictionary();
        foreach (var key in Palette.Keys)
        {
            var color = Color(palette, key);
            if (IsGlass) color = GlassLayer(palette, key, color);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            resources[key] = brush;
        }

        // glass: only a light tint of the theme, so the backdrop (Mica or Acrylic) carries the look
        // neutral: black on a dark theme, white on a light one, very faint, so the backdrop shows
        System.Windows.Media.Color Tint(string key, byte alpha)
        {
            if (!IsGlass) return Color(palette, key);
            var level = palette.Light ? (byte)255 : (byte)0;
            return System.Windows.Media.Color.FromArgb(alpha, level, level, level);
        }
        var background = new LinearGradientBrush(Tint("Bg", (byte)(6 + GlassLevel * 120)), Tint("Bg2", (byte)(6 + GlassLevel * 120)), new Point(0, 0), new Point(1, 1));
        background.Freeze();
        // The base gradient plus a soft glow of the theme's halo colour bleeding in from two corners.
        var halo = Color(palette, "Halo");
        var strength = IsGlass ? (byte)0x1E : palette.Light ? (byte)0x30 : (byte)0x46; // glass: only a faint halo of the theme
        RadialGradientBrush Glow(Point centre, double radiusX, double radiusY, byte alpha)
        {
            var brush = new RadialGradientBrush { Center = centre, GradientOrigin = centre, RadiusX = radiusX, RadiusY = radiusY };
            brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(alpha, halo.R, halo.G, halo.B), 0));
            brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0, halo.R, halo.G, halo.B), 1));
            return brush;
        }
        var scene = new DrawingGroup();
        var whole = new RectangleGeometry(new Rect(0, 0, 1, 1));
        scene.Children.Add(new GeometryDrawing(background, null, whole));
        scene.Children.Add(new GeometryDrawing(Glow(new Point(0.1, 0.0), 0.75, 0.6, strength), null, whole));
        scene.Children.Add(new GeometryDrawing(Glow(new Point(0.95, 1.0), 0.6, 0.5, (byte)(strength * 2 / 3)), null, whole));
        scene.Freeze();
        var withHalo = new DrawingBrush(scene) { Stretch = Stretch.Fill };
        withHalo.Freeze();
        resources["BgGradient"] = withHalo;
        var side = new LinearGradientBrush(Tint("Side", (byte)(10 + GlassLevel * 90)), Tint("Side2", (byte)(10 + GlassLevel * 90)), new Point(0, 0), new Point(0.3, 1));
        side.Freeze();
        resources["SideGradient"] = side;

        var accent = new LinearGradientBrush(Color(palette, "Accent1"), Color(palette, "Accent2"), new Point(0, 0), new Point(1, 1));
        accent.Freeze();
        resources["AccentGradient"] = accent;

        var hero = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        System.Windows.Media.Color Soft(System.Windows.Media.Color c) => IsGlass ? System.Windows.Media.Color.FromArgb((byte)(0x28 + GlassLevel * 0x50), c.R, c.G, c.B) : c;
        hero.GradientStops.Add(new GradientStop(Soft(Color(palette, "Hero")), 0));
        hero.GradientStops.Add(new GradientStop(Soft(Color(palette, "Hero2")), 0.45));
        hero.GradientStops.Add(new GradientStop(IsGlass ? System.Windows.Media.Color.FromArgb(0x10, 255, 255, 255) : Color(palette, "Panel"), 1));
        hero.Freeze();
        resources["HeroGradient"] = hero;
        return resources;
    }

    /// <summary>Colours the window's title bar to match the theme (caption colour needs Windows 11).</summary>
    public static void Chrome(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var dark = Theme.Light ? 0 : 1;
        Native.DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        if (IsGlass && window is MainWindow)
        {
            // Windows 11 backdrop: the whole window becomes the material, the panels above it stay translucent
            if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } target }) target.BackgroundColor = System.Windows.Media.Colors.Transparent;
            var margins = new Native.Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            Native.DwmExtendFrameIntoClientArea(hwnd, ref margins);
            var backdrop = Glass == "acrylic" ? 3 : 2; // DWMSBT_TRANSIENTWINDOW / DWMSBT_MAINWINDOW
            Native.DwmSetWindowAttribute(hwnd, 38, ref backdrop, sizeof(int));
            return;
        }
        var (r, g, b) = Theme.Rgb("Bg");
        var caption = r | (g << 8) | (b << 16); // COLORREF is 0x00BBGGRR
        Native.DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));
    }
}
