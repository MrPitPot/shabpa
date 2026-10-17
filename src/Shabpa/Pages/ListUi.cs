using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>Builders shared by the Downloads, Files, Memory, System and Health pages (Glass Depth): the design's list item
/// (<c>.it</c>: tinted icon badge, bold title, muted second line, optional end element, hairline between items),
/// buttons with a stroke icon, and the two-column helper for forms. Everything uses kit tokens only.</summary>
internal static class ListUi
{
    private static Brush B(string key) => K.Brush(key);

    /// <summary>Hairline that separates list items (Line token).</summary>
    public static Border Hairline() => new() { Height = 1, VerticalAlignment = VerticalAlignment.Top, Background = B("Line") };

    /// <summary>One list item: icon badge, title, optional second line, optional element at the end. The hairline sits above
    /// every item except the first. <paramref name="neutral"/> paints the badge grey (state unknown).</summary>
    public static Grid Item(string icon, string tint, string title, string? sub = null, UIElement? trailing = null, bool first = false,
        bool trimSub = false, bool subLtr = false, string? titleTint = null, bool neutral = false, string? note = null, bool titleLtr = false)
    {
        var grid = new Grid { MinHeight = 60 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (!first)
        {
            var line = Hairline();
            Grid.SetColumnSpan(line, 3);
            grid.Children.Add(line);
        }
        var badge = new IconBadge(icon, tint, 36, 18, 11) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 10, 0, 10) };
        if (neutral)
        {
            badge.Background = B("Line");
            if (badge.Child is Icon i) i.Foreground = B("Muted");
        }
        grid.Children.Add(badge);
        grid.Children.Add(Texts(title, sub, trimSub, subLtr, titleTint, 1, note, titleLtr));
        if (trailing is not null) Place(grid, trailing, 2);
        return grid;
    }

    /// <summary>Same as <see cref="Item"/> but with a check box in front instead of the badge (selectable lists).</summary>
    public static Grid CheckItem(CheckBox check, string title, string? sub, UIElement? trailing, bool first, bool trimSub = true)
    {
        var grid = new Grid { MinHeight = 56 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (!first)
        {
            var line = Hairline();
            Grid.SetColumnSpan(line, 3);
            grid.Children.Add(line);
        }
        check.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(check);
        grid.Children.Add(Texts(title, sub, trimSub, false, null, 1));
        if (trailing is not null) Place(grid, trailing, 2);
        return grid;
    }

    private static readonly System.Text.RegularExpressions.Regex Sizes = new(@"\d+(?:\.\d+)? (?:[KMGT]?B)\b", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Separator between the parts of a second line. In Persian the dot is pinned to the right-to-left context so a
    /// path or a number next to it cannot pull a date apart.</summary>
    public static string Sep => Ui.Flow == FlowDirection.RightToLeft ? " \u200F\u00B7\u200F " : "  \u00B7  ";

    /// <summary>Keeps "22.2 GB" one left-to-right run inside a Persian line (an LRM on both sides; WPF ignores isolates).</summary>
    public static string Ltr(string text) => Ui.Flow == FlowDirection.RightToLeft ? Sizes.Replace(text.Replace(" \u00B7 ", Sep), "\u200E$0\u200E") : text;

    private static StackPanel Texts(string title, string? sub, bool trimSub, bool subLtr, string? titleTint, int column, string? note = null, bool titleLtr = false)
    {
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 9, 14, 9) };
        var t = new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = B(titleTint ?? "Text"), TextWrapping = trimSub ? TextWrapping.NoWrap : TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis };
        if (titleLtr) { t.FlowDirection = FlowDirection.LeftToRight; t.HorizontalAlignment = HorizontalAlignment.Left; }
        texts.Children.Add(t);
        if (!string.IsNullOrEmpty(sub))
        {
            var s = new TextBlock { Text = subLtr ? sub : Ltr(sub), Style = K.Style("Label"), Margin = new Thickness(0, 2, 0, 0) };
            if (trimSub) { s.TextTrimming = TextTrimming.CharacterEllipsis; s.ToolTip = sub; }
            else s.TextWrapping = TextWrapping.Wrap;
            if (subLtr) { s.FlowDirection = FlowDirection.LeftToRight; s.HorizontalAlignment = HorizontalAlignment.Left; }
            texts.Children.Add(s);
        }
        if (!string.IsNullOrEmpty(note))
            texts.Children.Add(new TextBlock { Text = note, Style = K.Style("Tip"), Margin = new Thickness(0, 3, 0, 0) });
        Grid.SetColumn(texts, column);
        return texts;
    }

    /// <summary>Appends another line (for example a warning) under the texts of an <see cref="Item"/>.</summary>
    public static void AddText(Grid item, UIElement line)
    {
        if (item.Children.OfType<StackPanel>().FirstOrDefault() is { } texts) texts.Children.Add(line);
    }

    private static void Place(Grid grid, UIElement e, int column)
    {
        if (e is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(e, column);
        grid.Children.Add(e);
    }

    /// <summary>Button with a stroke icon in front of the text (the icon follows the button's foreground).</summary>
    public static Button Btn(string? icon, string text, string style = "Btn", Action? click = null)
    {
        var button = new Button { Style = K.Style(style) };
        SetContent(button, icon, text);
        if (click is not null) button.Click += (_, _) => click();
        return button;
    }

    /// <summary>(Re)builds the content of a button: optional icon, then the text.</summary>
    public static void SetContent(Button button, string? icon, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (icon is not null) row.Children.Add(new Icon(icon, 16, null) { Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = Ltr(text), VerticalAlignment = VerticalAlignment.Center });
        button.Content = row;
    }

    /// <summary>Icon-only ghost button with a tooltip.</summary>
    public static Button IconBtn(string icon, string tooltip, Action click, string? tint = null)
    {
        var button = new Button { Style = K.Style("BtnIcon"), ToolTip = tooltip, Content = new Icon(icon, 17, tint is null ? null : B(tint)) };
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>Small pill with text (design: soft tinted chip).</summary>
    public static Border Pill(string text, string tint)
    {
        var pill = new Border { Style = K.Style("Pill"), Background = B(tint + "Soft"), Child = new TextBlock { Text = text, Foreground = B(tint), FontSize = 12, FontWeight = FontWeights.SemiBold } };
        return pill;
    }

    /// <summary>Neutral pill (hairline fill, muted text).</summary>
    public static Border PillMuted(string text) =>
        new() { Style = K.Style("Pill"), Child = new TextBlock { Text = text, Foreground = B("Muted"), FontSize = 12, FontWeight = FontWeights.SemiBold } };

    /// <summary>A flat surface nested inside a card (job card, duplicate group).</summary>
    public static Border Nested(Thickness? margin = null) => new()
    {
        Style = K.Style("Card"), CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 12, 16, 12), Margin = margin ?? new Thickness(0, 10, 0, 0),
        Background = B("Card2Fill"),
    };

    /// <summary>Muted line with an icon in front (status line).</summary>
    public static StackPanel IconLine(string icon, string tint, string text, double top = 6)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, top, 0, 0) };
        row.Children.Add(new Icon(icon, 16, B(tint)) { Margin = new Thickness(0, 2, 8, 0), VerticalAlignment = VerticalAlignment.Top });
        row.Children.Add(new TextBlock { Text = Ltr(text), Style = K.Style("Body"), TextWrapping = TextWrapping.Wrap });
        return row;
    }

    /// <summary>Two columns that stack under <paramref name="breakpoint"/> px. Add children to <see cref="TwoColumns.Left"/>/<see cref="TwoColumns.Right"/>.</summary>
    public sealed class TwoColumns : Grid
    {
        public StackPanel Left { get; } = new();
        public StackPanel Right { get; } = new();
        private readonly double _breakpoint;
        private bool? _wide;

        public TwoColumns(double breakpoint = 760, double gap = 44)
        {
            _breakpoint = breakpoint;
            ColumnDefinitions.Add(new ColumnDefinition());
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gap) });
            ColumnDefinitions.Add(new ColumnDefinition());
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Children.Add(Left);
            Children.Add(Right);
            SizeChanged += (_, e) => Apply(e.NewSize.Width >= _breakpoint);
            Apply(true);
        }

        private void Apply(bool wide)
        {
            if (_wide == wide) return;
            _wide = wide;
            SetRow(Left, 0); SetColumn(Left, 0); SetColumnSpan(Left, wide ? 1 : 3);
            SetRow(Right, wide ? 0 : 1); SetColumn(Right, wide ? 2 : 0); SetColumnSpan(Right, wide ? 1 : 3);
            Right.Margin = wide ? new Thickness(0) : new Thickness(0, 6, 0, 0);
        }
    }

    /// <summary>12-column page grid like Home's: children are the page's top-level blocks (cards with margin 8).</summary>
    public static Grid PageGrid(int rows)
    {
        var grid = new Grid { Margin = new Thickness(-8, 0, -8, 0) };
        for (var i = 0; i < rows; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < 12; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        return grid;
    }

    public static void Place(UIElement e, int row, int column, int span)
    {
        Grid.SetRow(e, row);
        Grid.SetColumn(e, column);
        Grid.SetColumnSpan(e, span);
    }
}
