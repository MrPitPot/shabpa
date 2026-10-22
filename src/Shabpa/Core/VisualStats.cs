using System.Windows;
using System.Windows.Media;

namespace Shabpa.Core;

/// <summary>How big a visual tree is: the longest chain from the window down and the number of visuals. Walks iteratively (an
/// explicit stack), so measuring a very deep tree can never overflow the call stack itself. A few hundred visuals take
/// microseconds; it runs once per page switch, never per frame.</summary>
public static class VisualStats
{
    public readonly record struct Result(int Depth, int Count);

    /// <summary>Depth is counted from the top of the whole tree (the ancestors of <paramref name="root"/> are included).</summary>
    public static Result Measure(DependencyObject root, int limit = 200_000)
    {
        var above = 0;
        for (var p = Parent(root); p is not null && above < 10_000; p = Parent(p)) above++;
        int max = 1, count = 0;
        var stack = new Stack<(DependencyObject node, int depth)>();
        stack.Push((root, 1));
        while (stack.Count > 0 && count < limit)
        {
            var (node, depth) = stack.Pop();
            count++;
            if (depth > max) max = depth;
            if (node is not (Visual or System.Windows.Media.Media3D.Visual3D)) continue;
            var n = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < n; i++) stack.Push((VisualTreeHelper.GetChild(node, i), depth + 1));
        }
        return new Result(above + max, count);
    }

    private static DependencyObject? Parent(DependencyObject o) => o is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(o) : null;
}
