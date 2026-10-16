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

    /// <summary>0 to 1: how dark the glass is (0 = as clear as it gets).</summary>
    public static double GlassLevel { get; private set; }

    /// <summary>The brushes the glass look is made of. They stay unfrozen so the slider changes them while the app runs.</summary>
    private static readonly Dictionary<string, SolidColorBrush> GlassBrushes = [];

    /// <summary>Glass: the theme's background and panel colours are replaced by neutral, see-through layers (white veils on
    /// a dark theme, milky ones on a light theme); no theme colour is painted on the window, only buttons and bars keep
    /// their accent.</summary>
    private static System.Windows.Media.Color GlassColor(Palette palette, string key)
    {
        var light = palette.Light;
        var level = GlassLevel;
        static byte Alpha(double v) => (byte)Math.Clamp(v, 0, 255);
        System.Windows.Media.Color White(double a) => System.Windows.Media.Color.FromArgb(Alpha(a), 255, 255, 255);
        System.Windows.Media.Color Dark(double a) => System.Windows.Media.Color.FromArgb(Alpha(a), 0, 0, 0);
        return key switch
        {
            "Panel" => light ? White(0x66 + level * 0x50) : White(0x0E + level * 0x20),
            "Panel2" => light ? White(0x88 + level * 0x40) : White(0x18 + level * 0x28),
            "Panel3" => light ? White(0xA6 + level * 0x30) : White(0x26 + level * 0x30),
            "Line" => light ? Dark(0x30) : White(0x30),
            // the window itself: a veil that only grows when the person asks for a darker glass
            "BgGradient" => light ? White(level * 0xB0) : Dark(level * 0xB0),
            "SideGradient" => light ? White(0x10 + level * 0x90) : Dark(0x10 + level * 0xA0),
            "HeroGradient" => light ? White(0x50 + level * 0x50) : White(0x12 + level * 0x20),
            _ => System.Windows.Media.Colors.Transparent,
        };
    }

    /// <summary>Changes how dark the glass is, right now, without restarting.</summary>
    public static void SetGlassLevel(double level)
    {
        GlassLevel = Math.Clamp(level, 0, 1);
        if (!IsGlass) return;
        foreach (var (key, brush) in GlassBrushes) brush.Color = GlassColor(Theme, key);
    }

    /// <summary>Switches between the Mica and Acrylic materials right now (going to or from "off" needs a restart).</summary>
    public static bool SetBackdrop(string kind)
    {
        kind = CleanGlass(kind);
        if (!IsGlass || kind == "off" || Application.Current.MainWindow is not { } window) return false;
        Glass = kind;
        var hwnd = new WindowInteropHelper(window).Handle;
        var backdrop = kind == "acrylic" ? 3 : 2;
        Native.DwmSetWindowAttribute(hwnd, 38, ref backdrop, sizeof(int));
        return true;
    }

    public static string CleanGlass(string? value) => value is "mica" or "acrylic" ? value : "off";

    public static FlowDirection Flow => L.En ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;

    private static Color Color(Palette palette, string key)
    {
        var (r, g, b) = palette.Rgb(key);
        return System.Windows.Media.Color.FromRgb(r, g, b);
    }

    /// <summary>Builds the brush resources the styles in Theme.xaml refer to.</summary>
    public static ResourceDictionary Resources(Palette palette, string glass = "off", double glassLevel = 0)
    {
        Theme = palette;
        Glass = CleanGlass(glass);
        GlassLevel = Math.Clamp(glassLevel, 0, 1);
        GlassBrushes.Clear();
        var resources = new ResourceDictionary();
        foreach (var key in Palette.Keys)
        {
            if (IsGlass && key is "Panel" or "Panel2" or "Panel3" or "Line")
            {
                var live = new SolidColorBrush(GlassColor(palette, key));
                GlassBrushes[key] = live;
                resources[key] = live;
                continue;
            }
            var brush = new SolidColorBrush(Color(palette, key));
            brush.Freeze();
            resources[key] = brush;
        }

        if (IsGlass)
        {
            foreach (var key in new[] { "BgGradient", "SideGradient", "HeroGradient" })
            {
                var live = new SolidColorBrush(GlassColor(palette, key));
                GlassBrushes[key] = live;
                resources[key] = live;
            }
        }
        else
        {
            var background = new LinearGradientBrush(Color(palette, "Bg"), Color(palette, "Bg2"), new Point(0, 0), new Point(1, 1));
            background.Freeze();
            // The base gradient plus a soft glow of the theme's halo colour bleeding in from two corners.
            var halo = Color(palette, "Halo");
            var strength = palette.Light ? (byte)0x30 : (byte)0x46;
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
            var side = new LinearGradientBrush(Color(palette, "Side"), Color(palette, "Side2"), new Point(0, 0), new Point(0.3, 1));
            side.Freeze();
            resources["SideGradient"] = side;

            var hero = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            hero.GradientStops.Add(new GradientStop(Color(palette, "Hero"), 0));
            hero.GradientStops.Add(new GradientStop(Color(palette, "Hero2"), 0.45));
            hero.GradientStops.Add(new GradientStop(Color(palette, "Panel"), 1));
            hero.Freeze();
            resources["HeroGradient"] = hero;
        }

        var accent = new LinearGradientBrush(Color(palette, "Accent1"), Color(palette, "Accent2"), new Point(0, 0), new Point(1, 1));
        accent.Freeze();
        resources["AccentGradient"] = accent;
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
