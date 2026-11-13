using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;

namespace Shabpa.Pages;

/// <summary>Two (or more) existing pages shown one under the other in a single tab, each under its own small heading. The parts keep
/// all their own behaviour; this page only passes on what the window tells a page (Refresh, the tick, the second) and the header text.
/// It lets related tabs become one without rewriting them.</summary>
public sealed class StackedPage : PageBase
{
    private readonly List<(string title, PageBase page)> _parts;
    private readonly string _title;

    public StackedPage(string title, string subtitle, params (string title, PageBase page)[] parts)
    {
        _title = title;
        _subtitle = subtitle;
        _parts = [.. parts];
        var stack = new StackPanel();
        for (var i = 0; i < _parts.Count; i++)
        {
            var (heading, page) = _parts[i];
            stack.Children.Add(new TextBlock { Text = heading, Style = St("H2"), Margin = new Thickness(8, i == 0 ? 0 : 22, 8, 6) });
            stack.Children.Add(page);
            page.HeaderChanged += RaiseHeaderChanged;
        }
        Content = stack;
    }

    private readonly string _subtitle;

    public override string PageTitle => _title;
    public override string PageSubtitle => _subtitle;
    public override UIElement? HeaderAccessory => _parts.Select(p => p.page.HeaderAccessory).FirstOrDefault(a => a is not null);

    /// <summary>The part of a given page type (for code that needs to reach into one of them).</summary>
    public T? Part<T>() where T : PageBase => _parts.Select(p => p.page).OfType<T>().FirstOrDefault();

    public override void Refresh() { foreach (var (_, page) in _parts) SafeRefresh(page); }
    public override void OnTick(SensorSnapshot s) { foreach (var (_, page) in _parts) SafeTick(page, s); }
    public override void OnSecond() { foreach (var (_, page) in _parts) SafeSecond(page); }
}
