using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>A 12-column block grid in the Home reference style: children are the page's top-level blocks (so the entrance
/// staggers them) with an 8 px margin, the grid itself has -8 so the blocks line up with the header. Below
/// <c>threshold</c> px of width every block takes the narrow placement.</summary>
internal sealed class NetBlocks : Grid
{
    private readonly double _threshold;
    private readonly List<(FrameworkElement e, int row, int col, int span, int nRow)> _items = [];
    private bool? _wide;

    public NetBlocks(double threshold = 900, int rows = 10)
    {
        _threshold = threshold;
        Margin = new Thickness(-8, 0, -8, 0);
        for (var i = 0; i < rows; i++) RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < 12; i++) ColumnDefinitions.Add(new ColumnDefinition());
        SizeChanged += (_, e) => Apply(e.NewSize.Width > _threshold);
    }

    /// <summary>Adds a block. <paramref name="row"/>/<paramref name="col"/>/<paramref name="span"/> are the wide placement,
    /// <paramref name="narrowRow"/> the row when stacked (full width).</summary>
    public void Add(FrameworkElement element, int row, int col, int span, int narrowRow, int rowSpan = 1)
    {
        element.Margin = new Thickness(8);
        _items.Add((element, row, col, span, narrowRow));
        Children.Add(element);
        if (rowSpan > 1) SetRowSpan(element, rowSpan);
        Place(element, row, col, span, narrowRow, _wide ?? true);
    }

    private void Apply(bool wide)
    {
        if (_wide == wide) return;
        _wide = wide;
        foreach (var (e, row, col, span, nRow) in _items) Place(e, row, col, span, nRow, wide);
    }

    private static void Place(UIElement e, int row, int col, int span, int nRow, bool wide)
    {
        SetRow(e, wide ? row : nRow);
        SetColumn(e, wide ? col : 0);
        SetColumnSpan(e, wide ? span : 12);
        if (!wide) SetRowSpan(e, 1);
    }
}

internal static class NetParts
{
    /// <summary>Icon + text for a button (the icon follows the button's foreground).</summary>
    public static StackPanel IconText(string icon, string text, out TextBlock label)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Icon(icon, 17, null) { Margin = new Thickness(0, 0, 8, 0) });
        label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(label);
        return row;
    }

    /// <summary>A section title inside a card body, same look as <c>Ui2.Card</c> titles.</summary>
    public static TextBlock Title(string text) => new() { Text = text, Style = K.Style("H2") };

    /// <summary>Card title row with a tinted icon badge at the start (design: card header).</summary>
    public static StackPanel Header(string icon, string tint, string title, string? sub = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new IconBadge(icon, tint, 34, 18, 11) { Margin = new Thickness(0, 0, 12, 0) });
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = title, Style = K.Style("H2") });
        if (sub is not null) texts.Children.Add(new TextBlock { Text = sub, Style = K.Style("Label"), Margin = new Thickness(0, 2, 0, 0) });
        row.Children.Add(texts);
        return row;
    }

    /// <summary>GlassCard with a header (icon badge + title + optional subtitle line under the body header) and a body panel.</summary>
    public static (GlassCard card, StackPanel body) Card(string icon, string tint, string title, string? subtitle = null)
    {
        var body = new StackPanel();
        body.Children.Add(Header(icon, tint, title));
        if (subtitle is not null)
            body.Children.Add(new TextBlock { Text = subtitle, Style = K.Style("Body"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) });
        return (new GlassCard { Child = body }, body);
    }

    /// <summary>Numbered step badge (accent gradient square, white digit).</summary>
    public static Border StepBadge(int number) => new()
    {
        Width = 28, Height = 28, CornerRadius = new CornerRadius(9), Background = K.Brush("AccentGradient"), VerticalAlignment = VerticalAlignment.Top,
        Child = new TextBlock { Text = Fa.Digits(number.ToString()), Foreground = K.Brush("OnAccent"), FontWeight = FontWeights.Bold, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
    };

    /// <summary>Text that is a Latin run (device names, addresses): left-to-right but placed at the reading start.</summary>
    public static TextBlock LatinText(string text, string style = "Body", double? size = null)
    {
        var t = K.Text(text, style, size);
        t.FlowDirection = FlowDirection.LeftToRight;
        t.TextAlignment = Ui.Flow == FlowDirection.RightToLeft ? TextAlignment.Right : TextAlignment.Left;
        t.HorizontalAlignment = HorizontalAlignment.Stretch;
        return t;
    }
}

/// <summary>Vertical pick-one list (device picker): a radio dot, the label, the chosen row tinted with the accent. Values are
/// strings; <c>""</c> is the Windows default. Long names trim with an ellipsis and show in a tooltip.</summary>
internal sealed class NetChoiceList : StackPanel
{
    private readonly List<(string value, Border row, Border dot, TextBlock text)> _rows = [];
    private string _selected;
    private readonly Action<string> _onPick;

    public NetChoiceList(IEnumerable<(string value, string label, bool latin)> items, string selected, Action<string> onPick)
    {
        _selected = selected;
        _onPick = onPick;
        foreach (var (value, label, latin) in items)
        {
            var dot = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(2), VerticalAlignment = VerticalAlignment.Center };
            var text = latin ? NetParts.LatinText(label, "Body", 13) : K.Text(label, "Body", 13);
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Margin = new Thickness(12, 0, 0, 0);
            text.VerticalAlignment = VerticalAlignment.Center;
            if (latin) text.ToolTip = label;
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(dot);
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            var row = new Border
            {
                Child = grid, CornerRadius = new CornerRadius(11), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 4),
                BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Background = Brushes.Transparent, Focusable = true, FocusVisualStyle = null,
            };
            var v = value;
            row.MouseLeftButtonUp += (_, _) => Pick(v);
            row.KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { Pick(v); e.Handled = true; } };
            Fx.Attach(row, 0, 3, 1, 0.98);
            _rows.Add((value, row, dot, text));
            Children.Add(row);
        }
        Paint();
    }

    private void Pick(string value)
    {
        if (value == _selected) return;
        _selected = value;
        Paint();
        var chosen = _rows.First(r => r.value == value);
        Motion.Pop(chosen.dot);
        NetFx.Flash(chosen.row, 0.5, 380);
        _onPick(value);
    }

    private void Paint()
    {
        foreach (var (value, row, dot, text) in _rows)
        {
            var on = value == _selected;
            row.Background = on ? K.Brush("AccentSoft") : Brushes.Transparent;
            row.BorderBrush = on ? K.Brush("Accent") : K.Brush("Line");
            dot.BorderBrush = on ? K.Brush("Accent") : K.Brush("Faint");
            dot.Background = on ? K.Brush("Accent") : Brushes.Transparent;
            text.Foreground = K.Brush(on ? "Text" : "Muted");
            text.FontWeight = on ? FontWeights.SemiBold : FontWeights.Medium;
        }
    }
}
