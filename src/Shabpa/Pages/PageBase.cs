using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>Common base for the pages hosted by <see cref="MainWindow"/>, plus small UI builders.</summary>
public class PageBase : UserControl
{
    public virtual string PageTitle => "";
    public virtual string PageSubtitle => "";
    /// <summary>Optional element shown at the far end of the page header (status badge, primary action). See PORTING-GUIDE.md.</summary>
    public virtual UIElement? HeaderAccessory => null;
    /// <summary>Raised when title or subtitle change while the page is showing.</summary>
    public event Action? HeaderChanged;
    protected void RaiseHeaderChanged() => HeaderChanged?.Invoke();

    /// <summary>State changed (queue, rule, schedules) or the page was just shown.</summary>
    public virtual void Refresh() { }
    public virtual void OnTick(SensorSnapshot s) { }
    public virtual void OnSecond() { }

    protected static Hub Hub => Hub.I;
    protected static FlowDirection Flow => Ui.Flow;

    protected static Brush Res(string key) => Ui.Live(key); // long-lived brush: follows a live theme change
    protected static Style St(string key) => (Style)Application.Current.Resources[key];

    protected static TextBlock Txt(string text, string style = "Body") => new() { Text = text, Style = St(style) };

    /// <summary>Tinted rounded square with an icon. <paramref name="glyph"/> is an icon name from Icons.xaml ("cpu") or, for
    /// older pages, a Segoe Fluent glyph character. <paramref name="tint"/>: Accent, Green, Gold, Red, HueA..HueE.</summary>
    protected static Border Badge(string glyph, string tint, double size = 40)
    {
        if (Icon.Has(glyph)) return new IconBadge(glyph, tint, size, size * 0.46, size * 0.3);
        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size * 0.3),
            Background = Res(tint + "Soft"),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = glyph,
                Style = St("Icon"),
                Foreground = Res(tint),
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = size * 0.42,
            },
        };
    }

    /// <summary>Icon badge, title + subtitle, and an optional trailing element.</summary>
    protected static Grid Row(string glyph, string tint, string title, string sub, UIElement? trailing = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 7, 0, 7) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(Badge(glyph, tint));

        var texts = new StackPanel { Margin = new Thickness(14, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = title, Style = St("H2"), FontSize = 14, TextWrapping = TextWrapping.Wrap });
        if (sub.Length > 0)
            texts.Children.Add(new TextBlock { Text = sub, Style = St("Label"), Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);

        if (trailing is not null)
        {
            Grid.SetColumn(trailing, 2);
            grid.Children.Add(trailing);
        }
        return grid;
    }

    /// <summary>"Are you sure" with the exact consequence spelled out; false if the user said no.</summary>
    protected static bool Ask(string title, string message, string? yes = null, bool danger = false, string? detail = null) =>
        ConfirmWindow.Ask(title, message, yes, null, danger, detail);

    protected static Border Divider() => new() { Height = 1, Background = Res("Line"), Margin = new Thickness(0, 5, 0, 5) };

    protected static Border PillText(string text, string tint)
    {
        return new Border
        {
            Style = St("Pill"),
            Background = Res(tint + "Soft"),
            Child = new TextBlock { Text = text, Foreground = Res(tint), FontSize = 12, FontWeight = FontWeights.SemiBold },
        };
    }

    protected static Button IconButton(string glyph, string tooltip, Action onClick)
    {
        var button = new Button
        {
            Style = St("BtnIcon"),
            ToolTip = tooltip,
            Content = Icon.Has(glyph) ? new Icon(glyph, 17) : new TextBlock { Text = glyph, Style = St("Icon"), FontSize = 14 },
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>Fills a panel with a mutually exclusive choice. Two to four short options become the design's segmented
    /// control (one thumb that springs to the choice); longer or more numerous options become wrapping chips.</summary>
    protected static void Chips<T>(Panel panel, IEnumerable<(T value, string label)> options, T selected, Action<T> onPick)
    {
        panel.Children.Clear();
        var list = options.ToList();
        if (list.Count is >= 2 and <= 4 && list.Sum(o => o.label.Length) <= 36)
        {
            var seg = new Segmented(list.Select(o => o.label).ToList(), Math.Max(0, list.FindIndex(o => EqualityComparer<T>.Default.Equals(o.value, selected))))
            {
                Margin = new Thickness(0, 0, 8, 6),
            };
            seg.Changed += i => onPick(list[i].value);
            panel.Children.Add(seg);
            return;
        }
        var group = Guid.NewGuid().ToString("N");
        foreach (var (value, label) in list)
        {
            var chip = new RadioButton
            {
                Style = St("Chip"),
                GroupName = group,
                Content = label,
                IsChecked = EqualityComparer<T>.Default.Equals(value, selected),
                Margin = new Thickness(0, 0, 8, 6),
            };
            chip.Checked += (_, _) => { onPick(value); if (chip.IsLoaded) Motion.Pop(chip); };
            panel.Children.Add(chip);
        }
    }

    /// <summary>Seven day toggles, Saturday first. Returns a getter for the selected days.</summary>
    protected static Func<List<DayOfWeek>> DayToggles(Panel panel, Action? onChange = null)
    {
        panel.Children.Clear();
        var toggles = new List<(DayOfWeek day, ToggleButton button)>();
        foreach (var day in Fa.Week)
        {
            var toggle = new ToggleButton { Style = St("Day"), Content = Fa.DayLetter(day), ToolTip = Fa.DayName(day) };
            toggle.Checked += (_, _) => onChange?.Invoke();
            toggle.Unchecked += (_, _) => onChange?.Invoke();
            toggles.Add((day, toggle));
            panel.Children.Add(toggle);
        }
        return () => toggles.Where(t => t.button.IsChecked == true).Select(t => t.day).ToList();
    }

    protected internal static string Latin(string text)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is >= '۰' and <= '۹') chars[i] = (char)('0' + (chars[i] - '۰'));
            else if (chars[i] is >= '٠' and <= '٩') chars[i] = (char)('0' + (chars[i] - '٠'));
            else if (chars[i] is '٫' or '/') chars[i] = '.';
        }
        return new string(chars).Trim();
    }

    protected static int Int(TextBox box, int min, int max, int fallback) =>
        int.TryParse(Latin(box.Text), out var v) ? Math.Clamp(v, min, max) : fallback;

    protected static double Number(TextBox box, double min, double max, double fallback) =>
        double.TryParse(Latin(box.Text), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? Math.Clamp(v, min, max)
            : fallback;

    protected static string ActionGlyph(PowerAction a) => a switch
    {
        PowerAction.Sleep => "",
        PowerAction.Hibernate => "",
        PowerAction.Shutdown => "",
        PowerAction.Restart => "",
        _ => "",
    };
}
