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

    public static FlowDirection Flow => L.En ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;

    private static Color Color(Palette palette, string key)
    {
        var (r, g, b) = palette.Rgb(key);
        return System.Windows.Media.Color.FromRgb(r, g, b);
    }

    /// <summary>Builds the brush resources the styles in Theme.xaml refer to.</summary>
    public static ResourceDictionary Resources(Palette palette)
    {
        Theme = palette;
        var resources = new ResourceDictionary();
        foreach (var key in Palette.Keys)
        {
            var brush = new SolidColorBrush(Color(palette, key));
            brush.Freeze();
            resources[key] = brush;
        }

        var accent = new LinearGradientBrush(Color(palette, "Accent1"), Color(palette, "Accent2"), new Point(0, 0), new Point(1, 1));
        accent.Freeze();
        resources["AccentGradient"] = accent;

        var hero = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        hero.GradientStops.Add(new GradientStop(Color(palette, "Hero"), 0));
        hero.GradientStops.Add(new GradientStop(Color(palette, "Hero2"), 0.45));
        hero.GradientStops.Add(new GradientStop(Color(palette, "Panel"), 1));
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
        var (r, g, b) = Theme.Rgb("Bg");
        var caption = r | (g << 8) | (b << 16); // COLORREF is 0x00BBGGRR
        Native.DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));
    }
}
