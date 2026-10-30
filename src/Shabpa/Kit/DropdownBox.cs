using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Shabpa.Kit;

/// <summary>A small drop-down in the app's own look: a box showing the current choice that opens a list under itself.</summary>
public sealed class DropdownBox : Border
{
    private readonly TextBlock _text = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 13, FontWeight = FontWeights.Medium };
    private readonly Popup _popup = new() { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Bottom, PopupAnimation = PopupAnimation.Fade };
    private readonly StackPanel _list = new();
    private readonly List<(string key, string title)> _options;
    private string _value;

    /// <summary>Raised with the key of the option the user picked.</summary>
    public event Action<string>? Picked;

    public DropdownBox(IEnumerable<(string key, string title)> options, string value, double width = 260)
    {
        _options = [.. options];
        _value = value;
        Width = width;
        Height = 38;
        Cursor = Cursors.Hand;
        Padding = new Thickness(12, 0, 12, 0);
        CornerRadius = new CornerRadius(10);
        BorderThickness = new Thickness(1.5);
        BorderBrush = Ui.Live("Line");
        Background = Ui.Live("Panel3");
        _text.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(_text);
        var chevron = new TextBlock { Text = "▾", FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        chevron.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        Grid.SetColumn(chevron, 1);
        row.Children.Add(chevron);
        Child = row;

        _popup.PlacementTarget = this;
        _popup.Child = new Border
        {
            Child = _list, Padding = new Thickness(6), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1.5), Margin = new Thickness(8), MinWidth = width,
            BorderBrush = Ui.Live("Line"), Background = Ui.Live("Panel3"),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.4, Color = Colors.Black },
        };
        MouseLeftButtonUp += (_, _) => Open();
        Show();
    }

    public string Value
    {
        get => _value;
        set { _value = value; Show(); }
    }

    private void Show() => _text.Text = _options.FirstOrDefault(o => o.key == _value).title ?? "";

    private void Open()
    {
        if (_popup.IsOpen) return;
        _list.Children.Clear();
        foreach (var (key, title) in _options)
        {
            var chosen = key == _value;
            var item = new Border { Padding = new Thickness(10, 8, 10, 8), CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            var label = new TextBlock { Text = (chosen ? "✓  " : "     ") + title, FontSize = 13, FontWeight = chosen ? FontWeights.Bold : FontWeights.Medium };
            label.SetResourceReference(TextBlock.ForegroundProperty, chosen ? "Accent" : "Text");
            item.Child = label;
            item.MouseEnter += (_, _) => item.SetResourceReference(BackgroundProperty, "Panel2");
            item.MouseLeave += (_, _) => item.Background = Brushes.Transparent;
            var captured = key;
            item.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                _popup.IsOpen = false;
                if (captured == _value) return;
                _value = captured;
                Show();
                Picked?.Invoke(captured);
            };
            _list.Children.Add(item);
        }
        _popup.IsOpen = true;
    }
}
