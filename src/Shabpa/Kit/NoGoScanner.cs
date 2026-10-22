using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Shabpa.Core;

namespace Shabpa.Kit;

/// <summary>Finds what Bidar must not cover: buttons, fields, switches, sliders and every piece of text on the page behind him,
/// as rectangles in the owl layer's own pixels. Run only when he is about to choose a resting place (every few seconds at most).</summary>
public static class NoGoScanner
{
    public const int MaxNodes = 4000;

    public static List<Box> Collect(DependencyObject root, Visual relativeTo, Box area)
    {
        var found = new List<Box>();
        var visited = 0;
        void Walk(DependencyObject node, bool insideControl)
        {
            if (++visited > MaxNodes) return;
            if (node is UIElement ui && (!ui.IsVisible || ui.Opacity < 0.05)) return;
            if (node is BidarOwl) return;
            var hit = false;
            if (node is FrameworkElement fe && !insideControl)
            {
                if (fe is Control { IsHitTestVisible: true, Focusable: true } or ButtonBase) hit = true;
                else if (fe is TextBlock { Text.Length: > 0 }) hit = true;
                else if (fe is TextBlock { Inlines.Count: > 0 }) hit = true;
                if (hit && !Add(fe)) hit = false;
            }
            var n = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < n; i++) Walk(VisualTreeHelper.GetChild(node, i), insideControl || hit);
        }
        bool Add(FrameworkElement fe)
        {
            if (fe.ActualWidth <= 0 || fe.ActualHeight <= 0) return false;
            try
            {
                var r = fe.TransformToVisual(relativeTo).TransformBounds(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight));
                var box = new Box(r.X, r.Y, r.Width, r.Height);
                if (box.W > area.W * 0.6 && box.H > area.H * 0.5) return false; // a container, not a control: look inside it
                if (box.Intersects(area)) found.Add(box);
                return true;
            }
            catch (InvalidOperationException)
            {
                // not in the same visual tree any more: it is leaving
                return false;
            }
        }
        Walk(root, false);
        return found;
    }
}
