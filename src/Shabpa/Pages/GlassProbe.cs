using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shabpa.Core;

namespace Shabpa.Pages;

/// <summary>Test aid for <c>--glass-test &lt;report&gt;</c> (with <c>--screenshots</c> and <c>--glass acrylic</c>): changes the glass level the way the Settings slider does,
/// before and after a live theme change, and reports whether the brush the window really paints with followed.</summary>
internal static class GlassProbe
{
    public static async Task Run(MainWindow window, string report)
    {
        try { await Body(window, report); }
        catch (Exception ex) { File.WriteAllText(report, ex.ToString()); }
        Application.Current.Shutdown(0);
    }

    private static async Task Body(MainWindow window, string report)
    {
        await Task.Delay(2000);
        var sb = new StringBuilder();
        string Line(string when)
        {
            var found = new List<(string where, byte a)>();
            void Walk(DependencyObject o)
            {
                if (o is Border { Background: SolidColorBrush b } border && border.Background == window.TryFindResource("MicaVeil")) found.Add((border.GetType().Name, b.Color.A));
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++) Walk(VisualTreeHelper.GetChild(o, i));
            }
            Walk(window);
            var res = window.TryFindResource("MicaVeil") as SolidColorBrush;
            return $"{when}: IsGlass={Ui.IsGlass} level={Ui.GlassLevel:0.00} resourceAlpha={res?.Color.A} frozen={res?.IsFrozen} painters={found.Count} painterAlpha={string.Join(",", found.Select(f => f.a))}";
        }
        sb.AppendLine(Line("start"));
        Ui.SetGlassLevel(1);
        await Task.Delay(300);
        sb.AppendLine(Line("level 1"));
        Ui.SetTheme(Palette.Find("white"));
        await Task.Delay(300);
        sb.AppendLine(Line("after theme change"));
        Ui.SetGlassLevel(0);
        await Task.Delay(300);
        sb.AppendLine(Line("level 0"));
        File.WriteAllText(report, sb.ToString());
    }
}
