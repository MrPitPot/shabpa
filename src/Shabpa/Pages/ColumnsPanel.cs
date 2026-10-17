using System.Windows;
using System.Windows.Controls;

namespace Shabpa.Pages;

/// <summary>Cards in two columns (one when the window is narrow) that are all direct children of one panel, so the page entrance
/// can stagger every card. Each child names its column with <see cref="ColumnProperty"/>; inside a column the cards keep their
/// order and are <see cref="Gap"/> apart. Column 0 is at the start side (right in a right-to-left page).</summary>
internal sealed class ColumnsPanel : Panel
{
    public static readonly DependencyProperty ColumnProperty = DependencyProperty.RegisterAttached("Column", typeof(int), typeof(ColumnsPanel),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsParentMeasure));
    public static int GetColumn(DependencyObject o) => (int)o.GetValue(ColumnProperty);
    public static void SetColumn(DependencyObject o, int v) => o.SetValue(ColumnProperty, v);

    /// <summary>Space between cards and between the columns.</summary>
    public double Gap { get; set; } = 16;
    /// <summary>Width (px) above which two columns are used.</summary>
    public double Breakpoint { get; set; } = 900;

    private bool Wide(double width) => width > Breakpoint;

    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? Breakpoint : available.Width;
        var wide = Wide(width);
        var colWidth = wide ? (width - Gap) / 2 : width;
        var heights = new double[2];
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            child.Measure(new Size(colWidth, double.PositiveInfinity));
            var c = wide ? Math.Clamp(GetColumn(child), 0, 1) : 0;
            heights[c] += (heights[c] > 0 ? Gap : 0) + child.DesiredSize.Height;
        }
        return new Size(width, Math.Max(heights[0], heights[1]));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var wide = Wide(finalSize.Width);
        var colWidth = wide ? (finalSize.Width - Gap) / 2 : finalSize.Width;
        var y = new double[2];
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            var c = wide ? Math.Clamp(GetColumn(child), 0, 1) : 0;
            if (y[c] > 0) y[c] += Gap;
            child.Arrange(new Rect(c * (colWidth + Gap), y[c], colWidth, child.DesiredSize.Height));
            y[c] += child.DesiredSize.Height;
        }
        return finalSize;
    }
}
