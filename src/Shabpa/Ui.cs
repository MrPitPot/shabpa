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
/// changing either restarts the app so every screen is built fresh.
/// This is where the "Glass Depth" tokens become WPF brushes (see PORTING-GUIDE.md for the key list).</summary>
public static class Ui
{
    public static Palette Theme { get; private set; } = Palette.Find(null);

    /// <summary>"off", "mica" or "acrylic" for this run (fixed at start, like the theme).</summary>
    public static string Glass { get; private set; } = "off";

    public static bool IsGlass => Glass is "mica" or "acrylic";

    /// <summary>0 to 1: how dark the glass is (0 = as clear as it gets).</summary>
    public static double GlassLevel { get; private set; }

    /// <summary>The veil over the window material. Unfrozen so the glass slider changes it while the app runs.</summary>
    private static SolidColorBrush? _veil;

    /// <summary>Glass: the veil is neutral (black on a dark theme, white on a light one), never a theme colour; the slider
    /// only decides how much of the Mica / Acrylic material shows through.</summary>
    private static System.Windows.Media.Color VeilColor(Palette palette)
    {
        var a = (byte)Math.Clamp(0x30 + GlassLevel * 0x90, 0, 255);
        return palette.Light ? System.Windows.Media.Color.FromArgb(a, 255, 255, 255) : System.Windows.Media.Color.FromArgb(a, 0, 0, 0);
    }

    /// <summary>Changes how dark the glass is, right now, without restarting.</summary>
    public static void SetGlassLevel(double level)
    {
        GlassLevel = Math.Clamp(level, 0, 1);
        if (IsGlass && _veil is not null) _veil.Color = VeilColor(Theme);
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

    private static System.Windows.Media.Color Color(Palette palette, string key)
    {
        var (r, g, b) = palette.Rgb(key);
        return System.Windows.Media.Color.FromRgb(r, g, b);
    }

    private static System.Windows.Media.Color FromArgb(uint argb) =>
        System.Windows.Media.Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    private static System.Windows.Media.Color WithAlpha(System.Windows.Media.Color c, byte a) => System.Windows.Media.Color.FromArgb(a, c.R, c.G, c.B);

    private static SolidColorBrush Solid(System.Windows.Media.Color c)
    {
        // left unfrozen on purpose: a theme change rewrites these very brushes in place (see SetTheme)
        return new SolidColorBrush(c);
    }

    private static LinearGradientBrush Linear(System.Windows.Media.Color a, System.Windows.Media.Color b, Point from, Point to,
        double mid = -1, System.Windows.Media.Color? midColor = null)
    {
        var g = new LinearGradientBrush { StartPoint = from, EndPoint = to };
        g.GradientStops.Add(new GradientStop(a, 0));
        if (mid >= 0 && midColor is { } m) g.GradientStops.Add(new GradientStop(m, mid));
        g.GradientStops.Add(new GradientStop(b, 1));
        return g;
    }

    /// <summary>Layer 0 of the design: four static radial blooms on a dark/light base (never repainted).</summary>
    private static Brush Wallpaper(GlassTokens t, bool light)
    {
        var scene = new DrawingGroup();
        var whole = new RectangleGeometry(new Rect(0, 0, 1, 1));
        scene.Children.Add(new GeometryDrawing(Solid(FromArgb(0xFF000000 | t.WallBase)), null, whole));
        void Bloom(uint rgb, double cx, double cy, double rx, double ry, double stop)
        {
            var c = FromArgb(0xFF000000 | rgb);
            var brush = new RadialGradientBrush { Center = new Point(cx, cy), GradientOrigin = new Point(cx, cy), RadiusX = rx, RadiusY = ry };
            brush.GradientStops.Add(new GradientStop(c, 0));
            brush.GradientStops.Add(new GradientStop(WithAlpha(c, 0), stop));
            brush.GradientStops.Add(new GradientStop(WithAlpha(c, 0), 1));
            brush.Freeze();
            scene.Children.Add(new GeometryDrawing(brush, null, whole));
        }
        if (light)
        {
            Bloom(t.Wall1, 0.12, 0.08, 0.60, 0.70, 0.6);
            Bloom(t.Wall2, 0.92, 0.14, 0.55, 0.60, 0.62);
            Bloom(t.Wall3, 0.78, 1.00, 0.60, 0.70, 0.6);
            Bloom(t.Wall4, 0.00, 1.00, 0.50, 0.60, 0.6);
        }
        else
        {
            Bloom(t.Wall1, 0.10, 0.06, 0.60, 0.70, 0.6);
            Bloom(t.Wall2, 0.94, 0.12, 0.55, 0.60, 0.62);
            Bloom(t.Wall3, 0.78, 1.00, 0.60, 0.70, 0.6);
            Bloom(t.Wall4, 0.00, 1.00, 0.50, 0.50, 0.6);
        }
        scene.Freeze();
        return new DrawingBrush(scene) { Stretch = Stretch.Fill };
    }

    /// <summary>Builds the brush resources the styles in Theme.xaml refer to. Legacy keys (Bg, Panel, Panel2, Text, Accent...)
    /// keep their names; the Glass Depth layers are added next to them (see PORTING-GUIDE.md for the full list).</summary>
    public static ResourceDictionary Resources(Palette palette, string glass = "off", double glassLevel = 0)
    {
        Theme = palette;
        Glass = CleanGlass(glass);
        GlassLevel = Math.Clamp(glassLevel, 0, 1);
        _veil = null;
        var t = GlassTokens.Of(palette);
        var light = palette.Light;
        var resources = new ResourceDictionary();

        foreach (var key in Palette.Keys) resources[key] = Solid(Color(palette, key));

        // translucent layers replace the opaque panel colours
        var card = FromArgb(t.Card);
        var card2 = FromArgb(t.Card2);
        var line = FromArgb(t.Line);
        resources["Panel"] = Solid(card);
        resources["Panel2"] = Solid(card2);
        resources["Panel3"] = Solid(FromArgb(t.Flyout));
        resources["Line"] = Solid(line);
        resources["CardFill"] = resources["Panel"];
        resources["Card2Fill"] = resources["Panel2"];
        resources["FlyoutFill"] = resources["Panel3"];
        resources["Hi"] = Solid(FromArgb(t.Hi));
        resources["FieldFill"] = Solid(FromArgb(t.Field));
        resources["ScrollBrush"] = Solid(FromArgb(t.ScrollThumb));
        resources["Ink"] = resources["Text"];
        resources["OnInk"] = Solid(FromArgb(t.OnInk));
        resources["White"] = Solid(System.Windows.Media.Colors.White);
        resources["Transparent"] = Solid(System.Windows.Media.Colors.Transparent);
        resources["ShadowC"] = FromArgb(t.Shadow);
        resources["ShadowHoverC"] = FromArgb(t.ShadowHover);
        resources["ShadowTightC"] = FromArgb(t.ShadowTight);
        var shadowBase = FromArgb(t.Shadow);
        resources["ShadowSoftC"] = WithAlpha(shadowBase, (byte)(shadowBase.A * 0.45));
        resources["AccentGlowC"] = WithAlpha(Color(palette, "Accent2"), 0xB3);

        // dark themes: the "soft" tints are the colour at low alpha, so they sit on any card like in the design
        if (!light)
        {
            resources["AccentSoft"] = Solid(WithAlpha(Color(palette, "Accent"), 0x2E));
            resources["GreenSoft"] = Solid(WithAlpha(Color(palette, "Green"), 0x24));
            resources["RedSoft"] = Solid(WithAlpha(Color(palette, "Red"), 0x24));
        }

        // stat hues: use as the tint name in Badge/StatCard ("HueA".."HueE" plus "...Soft")
        var hues = new[] { t.HueA, t.HueB, t.HueC, t.HueD, t.HueE };
        for (var i = 0; i < hues.Length; i++)
        {
            var c = FromArgb(hues[i]);
            var name = "Hue" + (char)('A' + i);
            resources[name] = Solid(c);
            resources[name + "Soft"] = Solid(WithAlpha(c, 0x24));
        }

        // layer 1: the window plate. Glass shows the real Mica/Acrylic through a neutral veil; otherwise the static wallpaper under a tinted veil
        if (IsGlass)
        {
            _veil = new SolidColorBrush(VeilColor(palette));
            resources["MicaVeil"] = _veil;
            resources["BgGradient"] = Solid(System.Windows.Media.Colors.Transparent);
            resources["Wallpaper"] = resources["BgGradient"];
        }
        else
        {
            resources["MicaVeil"] = Solid(FromArgb(t.Mica));
            var wall = Wallpaper(t, light);
            resources["Wallpaper"] = wall;
            resources["BgGradient"] = wall;
        }
        resources["SideGradient"] = Solid(System.Windows.Media.Colors.Transparent);
        resources["MainBg"] = light
            ? Linear(System.Windows.Media.Color.FromArgb(0x57, 255, 255, 255), System.Windows.Media.Color.FromArgb(0x1A, 255, 255, 255), new Point(0, 0), new Point(0, 1))
            : Linear(System.Windows.Media.Color.FromArgb(0x0D, 255, 255, 255), System.Windows.Media.Color.FromArgb(0, 255, 255, 255), new Point(0, 0), new Point(0, 1));

        // card rim light: bright hairline on the top edge fading into the normal hairline
        var rim = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        rim.GradientStops.Add(new GradientStop(FromArgb(t.Hi), 0));
        rim.GradientStops.Add(new GradientStop(line, 0.22));
        rim.GradientStops.Add(new GradientStop(line, 1));
        resources["CardRim"] = rim;

        var a1 = Color(palette, "Accent1");
        var a2 = Color(palette, "Accent2");
        resources["AccentGradient"] = Linear(a1, a2, new Point(0, 0), new Point(1, 1));
        resources["AccentGradientH"] = Linear(a1, a2, new Point(0, 0.5), new Point(1, 0.5));
        resources["AccentBar"] = Linear(a1, a2, new Point(0, 0), new Point(0, 1));
        resources["WarnGradientH"] = Linear(System.Windows.Media.Color.FromRgb(0xFF, 0xC7, 0x64), System.Windows.Media.Color.FromRgb(0xFF, 0x9F, 0x5C), new Point(0, 0.5), new Point(1, 0.5));
        // hero card: card2 fading into card at 135 degrees, plus the soft accent bloom
        resources["HeroGradient"] = Linear(card2, card, new Point(0, 0), new Point(1, 1));
        var glow = new RadialGradientBrush { Center = new Point(0.163, 0.244), GradientOrigin = new Point(0.163, 0.244), RadiusX = 0.285, RadiusY = 0.64 };
        glow.GradientStops.Add(new GradientStop(WithAlpha(a1, 0x38), 0));
        glow.GradientStops.Add(new GradientStop(WithAlpha(a1, 0), 1));
        resources["HeroGlow"] = glow;
        RegisterLive(resources);
        _active ??= resources;
        return resources;
    }

    /// <summary>The dictionary the app was started with; a live theme change rewrites its brushes in place.</summary>
    private static ResourceDictionary? _active;

    /// <summary>Switches the colour theme right now, no restart. Styles and templates (which WPF freezes) follow because the
    /// dictionary entries are replaced and the XAML refers to them dynamically; brushes that code-built screens already hold
    /// (<see cref="Live"/>) are rewritten in place.</summary>
    public static void SetTheme(Palette palette)
    {
        var old = _active;
        var fresh = Resources(palette, Glass, GlassLevel); // also sets Theme, the veil and rewrites the live brushes
        if (old is null || ReferenceEquals(old, fresh)) return;
        foreach (var key in fresh.Keys.Cast<object>().ToList()) old[key] = fresh[key];
        foreach (Window window in Application.Current.Windows)
            if (window.IsLoaded) Chrome(window);
    }

    /// <summary>The brushes code-built screens hold (<c>K.Brush</c>): one long-lived instance per key that a theme change
    /// rewrites in place, so a page that is already on screen changes colour without being rebuilt.</summary>
    private static readonly Dictionary<string, Brush> _live = [];

    public static Brush Live(string key) =>
        _live.TryGetValue(key, out var brush) ? brush : (Brush)Application.Current.Resources[key];

    private static void RegisterLive(ResourceDictionary resources)
    {
        foreach (var key in resources.Keys.Cast<object>().OfType<string>())
        {
            if (resources[key] is not Brush fresh) continue;
            if (_live.TryGetValue(key, out var live) && live.Dispatcher == fresh.Dispatcher && !live.IsFrozen && Sync(live, fresh)) continue; // another thread's brush (tests) is replaced
            _live[key] = fresh.Clone();
        }
    }

    private static bool Sync(object? old, object? fresh)
    {
        switch (old)
        {
            case SolidColorBrush o when fresh is SolidColorBrush n && !o.IsFrozen:
                o.Color = n.Color;
                return true;
            case LinearGradientBrush o when fresh is LinearGradientBrush n && !o.IsFrozen:
                o.StartPoint = n.StartPoint;
                o.EndPoint = n.EndPoint;
                o.GradientStops = n.GradientStops.Clone();
                return true;
            case RadialGradientBrush o when fresh is RadialGradientBrush n && !o.IsFrozen:
                o.Center = n.Center;
                o.GradientOrigin = n.GradientOrigin;
                o.RadiusX = n.RadiusX;
                o.RadiusY = n.RadiusY;
                o.GradientStops = n.GradientStops.Clone();
                return true;
            case DrawingBrush o when fresh is DrawingBrush n && !o.IsFrozen:
                o.Drawing = n.Drawing;
                return true;
        }
        return false;
    }

    /// <summary>Colours the window's title bar to match the theme (caption colour needs Windows 11). The main window draws
    /// its own title bar (WindowChrome); in glass mode it also becomes the Windows 11 backdrop material.</summary>
    public static void Chrome(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var dark = Theme.Light ? 0 : 1;
        Native.DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        if (window is MainWindow)
        {
            var round = 2; // DWMWCP_ROUND
            Native.DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
            if (IsGlass)
            {
                if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } target }) target.BackgroundColor = System.Windows.Media.Colors.Transparent;
                var margins = new Native.Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                Native.DwmExtendFrameIntoClientArea(hwnd, ref margins);
                var backdrop = Glass == "acrylic" ? 3 : 2; // DWMSBT_TRANSIENTWINDOW / DWMSBT_MAINWINDOW
                Native.DwmSetWindowAttribute(hwnd, 38, ref backdrop, sizeof(int));
            }
            return;
        }
        var (r, g, b) = Theme.Rgb("Bg");
        var caption = r | (g << 8) | (b << 16); // COLORREF is 0x00BBGGRR
        Native.DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));
    }
}
