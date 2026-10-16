using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Shabpa.Core;

namespace Shabpa.Pages;

/// <summary>One place to arrange the overlay: a small picture of the screen with six zones, and the groups of readings
/// (graphics card, processor, clock...) as tiles you drag between zones or into a new order. Click a tile to choose which
/// readings of that group are shown. Everything is saved as the person changes it.</summary>
internal sealed class OverlayEditor : StackPanel
{
    private static readonly string[] Zones = ["tl", "tc", "tr", "bl", "bc", "br"];

    private readonly Func<OverlaySettings> _settings;
    private readonly IReadOnlyList<(string key, string fa, string en)> _groups;
    private readonly Action _changed;
    private readonly Func<string, Panel> _readings;
    private readonly Dictionary<string, StackPanel> _zonePanels = [];
    private readonly Dictionary<string, Border> _zoneBorders = [];
    private readonly Border _readingsHost = new() { Margin = new Thickness(0, 12, 0, 0) };
    private readonly StackPanel _free = new();
    private string _selected = "";
    private Point _down;
    private string? _pressed;

    public OverlayEditor(Func<OverlaySettings> settings, IReadOnlyList<(string key, string fa, string en)> groups, Action changed, Func<string, Panel> readings)
    {
        _settings = settings;
        _groups = groups;
        _changed = changed;
        _readings = readings;
        _selected = groups[0].key;

        // the main block: groups here share one frame, wherever that frame sits on the screen
        var mainPanel = new StackPanel { MinHeight = 56 };
        var mainBorder = new Border
        {
            Child = mainPanel, Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(8), CornerRadius = new CornerRadius(10), AllowDrop = true, Background = Brushes.Transparent,
            BorderThickness = new Thickness(1.5), BorderBrush = Brush("Line"), Tag = "main",
        };
        mainBorder.DragEnter += (_, e) => { Highlight(mainBorder, true); e.Effects = DragDropEffects.Move; e.Handled = true; };
        mainBorder.DragOver += (_, e) => { e.Effects = DragDropEffects.Move; e.Handled = true; };
        mainBorder.DragLeave += (_, _) => Highlight(mainBorder, false);
        mainBorder.Drop += (_, e) => { Highlight(mainBorder, false); DropOn(e, "main", null); };
        _zonePanels["main"] = mainPanel;
        _zoneBorders["main"] = mainBorder;
        Children.Add(new TextBlock
        {
            Text = L.T("کادر اصلی اورلی: بخش‌هایی که این‌جان توی یه کادر کنار هم می‌شینن (جای کادر رو با «گوشه‌ی صفحه» یا «جابجایی اورلی با ماوس» مشخص کن).",
                "Main overlay frame: groups here share one frame (set where it sits with “Corner of the screen” or “Move the overlay with the mouse”)."),
            Style = (Style)Application.Current.Resources["Label"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0),
        });
        Children.Add(mainBorder);
        Children.Add(new TextBlock
        {
            Text = L.T("یا یه بخش رو بکش به یه ناحیه‌ی صفحه تا کادر جدا و مستقل داشته باشه:", "Or drag a group into a screen zone to give it a frame of its own:"),
            Style = (Style)Application.Current.Resources["Label"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0),
        });

        // the screen: a top band, the game in the middle, a bottom band
        var screen = new Grid { FlowDirection = FlowDirection.LeftToRight, MinHeight = 250 };
        for (var c = 0; c < 3; c++) screen.ColumnDefinitions.Add(new ColumnDefinition());
        screen.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        screen.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 36 });
        screen.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < Zones.Length; i++)
        {
            var key = Zones[i];
            var panel = new StackPanel { MinHeight = 74 };
            var border = new Border
            {
                Child = panel, Margin = new Thickness(4), Padding = new Thickness(6), CornerRadius = new CornerRadius(10), AllowDrop = true, Background = Brushes.Transparent,
                BorderThickness = new Thickness(1.5), BorderBrush = Brush("Line"), Tag = key,
            };
            border.DragEnter += (_, e) => { Highlight(border, true); e.Effects = DragDropEffects.Move; e.Handled = true; };
            border.DragOver += (_, e) => { e.Effects = DragDropEffects.Move; e.Handled = true; };
            border.DragLeave += (_, _) => Highlight(border, false);
            border.Drop += (_, e) => { Highlight(border, false); DropOn(e, key, null); };
            Grid.SetColumn(border, i % 3);
            Grid.SetRow(border, i < 3 ? 0 : 2);
            screen.Children.Add(border);
            _zonePanels[key] = panel;
            _zoneBorders[key] = border;
        }
        var game = new TextBlock
        {
            Text = L.T("بازی", "Game"), Foreground = Brush("Faint"), FontSize = 20, FontWeight = FontWeights.SemiBold, Opacity = 0.35,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
        };
        Grid.SetRow(game, 1);
        Grid.SetColumnSpan(game, 3);
        screen.Children.Add(game);
        Children.Add(new Border
        {
            Child = screen, CornerRadius = new CornerRadius(12), Padding = new Thickness(6), Margin = new Thickness(0, 10, 0, 0),
            Background = new LinearGradientBrush([new GradientStop(Color.FromRgb(18, 24, 44), 0), new GradientStop(Color.FromRgb(36, 30, 62), 1)], new Point(0, 0), new Point(1, 1)),
        });

        _free.Margin = new Thickness(0, 8, 0, 0);
        Children.Add(_free);
        Children.Add(_readingsHost);
    }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    private static void Highlight(Border zone, bool on)
    {
        var brush = on ? Brush("Accent") : Brush("Line");
        zone.BorderBrush = brush;
        zone.Background = on ? Brush("AccentSoft") : Brushes.Transparent;
    }

    private static string ZoneOf(GroupPlace place, OverlaySettings o) => place.Anchor == "main" ? "main" : place.Anchor;

    /// <summary>Rebuilds the tiles from the saved settings.</summary>
    public void Rebuild()
    {
        var o = _settings();
        foreach (var panel in _zonePanels.Values) panel.Children.Clear();
        _free.Children.Clear();
        var ordered = _groups.OrderBy(g => o.OrderOf(g.key)).ToList();
        foreach (var (key, fa, en) in ordered)
        {
            var place = o.PlaceOf(key);
            var tile = Tile(key, L.T(fa, en), o);
            if (place.Anchor == "free") _free.Children.Add(FreeRow(key, tile));
            else _zonePanels[ZoneOf(place, o)].Children.Add(tile);
        }
        ShowReadings();
    }

    private UIElement FreeRow(string group, Border tile)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        tile.Width = 220;
        row.Children.Add(tile);
        row.Children.Add(new TextBlock
        {
            Text = L.T("جای دلخواه (با «جابجایی اورلی با ماوس» تعیین شده)؛ برای برگردوندن به یه ناحیه، تیکش رو بکش اون‌جا.",
                "Custom spot (set with “Move the overlay with the mouse”); drag it into a zone to bring it back."),
            Style = (Style)Application.Current.Resources["Label"], TextWrapping = TextWrapping.Wrap, MaxWidth = 340, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        });
        return row;
    }

    private Border Tile(string group, string title, OverlaySettings o)
    {
        var count = OverlayMetrics.All.Count(m => m.Group == group && !m.Advanced && o.Items.Contains(m.Key));
        var selected = group == _selected;
        var text = new StackPanel { FlowDirection = Ui.Flow };
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 12.5, Foreground = Brush(count > 0 ? "Text" : "Muted"), TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock
        {
            Text = count > 0 ? L.T($"{Fa.Digits(count.ToString())} مورد", $"{count} shown") : L.T("چیزی نشون داده نمی‌شه", "nothing shown"),
            FontSize = 11, Foreground = Brush("Faint"),
        });
        var grip = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), Foreground = Brush("Faint"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var row = new DockPanel { FlowDirection = FlowDirection.LeftToRight };
        DockPanel.SetDock(grip, Dock.Left);
        row.Children.Add(grip);
        row.Children.Add(text);
        var tile = new Border
        {
            Child = row, Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 0, 4), CornerRadius = new CornerRadius(9), Cursor = Cursors.SizeAll, AllowDrop = true,
            Background = Brush(selected ? "AccentSoft" : "Panel2"), BorderThickness = new Thickness(1), BorderBrush = Brush(selected ? "Accent" : "Line"), Tag = group,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1),
        };
        tile.MouseEnter += (_, _) => Pulse(tile, 1.03);
        tile.MouseLeave += (_, _) => Pulse(tile, 1);
        tile.PreviewMouseLeftButtonDown += (_, e) => { _pressed = group; _down = e.GetPosition(this); };
        tile.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (_pressed != group) return;
            _pressed = null;
            _selected = group;
            Rebuild();
        };
        tile.PreviewMouseMove += (_, e) =>
        {
            if (_pressed != group || e.LeftButton != MouseButtonState.Pressed) return;
            var at = e.GetPosition(this);
            if (Math.Abs(at.X - _down.X) + Math.Abs(at.Y - _down.Y) < 8) return;
            _pressed = null;
            tile.Opacity = 0.5;
            DragDrop.DoDragDrop(tile, new DataObject("shabpa.group", group), DragDropEffects.Move);
            tile.Opacity = 1;
        };
        tile.DragOver += (_, e) => { e.Effects = DragDropEffects.Move; e.Handled = true; };
        tile.Drop += (_, e) =>
        {
            var target = (string)tile.Tag;
            DropOn(e, ZoneOf(_settings().PlaceOf(target), _settings()), target);
        };
        return tile;
    }

    private static void Pulse(Border tile, double to)
    {
        if (!Motion.Enabled || tile.RenderTransform is not ScaleTransform scale) return;
        var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(140)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    private void DropOn(DragEventArgs e, string zone, string? before)
    {
        e.Handled = true;
        if (e.Data.GetData("shabpa.group") is not string group || group == before) return;
        var o = _settings();
        // the place: the zone of the main block means "with the main block"
        var place = o.Places.FirstOrDefault(p => p.Group == group);
        if (place is null) o.Places.Add(place = new GroupPlace { Group = group });
        place.Anchor = zone;
        // the order: in front of the tile it was dropped on, or at the end of the zone
        var order = _groups.OrderBy(g => o.OrderOf(g.key)).Select(g => g.key).Where(k => k != group).ToList();
        var index = before is not null ? order.IndexOf(before) : order.FindLastIndex(k => ZoneOf(o.PlaceOf(k), o) == zone && o.PlaceOf(k).Anchor != "free") + 1;
        order.Insert(Math.Clamp(index < 0 ? order.Count : index, 0, order.Count), group);
        o.GroupOrder = order;
        _selected = group;
        _changed();
        Rebuild();
        // the dropped tile pops in
        foreach (var panel in _zonePanels.Values)
            foreach (var tile in panel.Children.OfType<Border>().Where(b => (string)b.Tag == group))
                if (Motion.Enabled && tile.RenderTransform is ScaleTransform scale)
                {
                    var pop = new DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 } };
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
                }
    }

    private bool _readingsBuilt;

    /// <summary>Every group with all its readings as tick boxes, under the screen: anything can be switched on or off.
    /// The page owns the checkboxes and keeps them in step with the settings; they are placed here once.</summary>
    private void ShowReadings()
    {
        if (_readingsBuilt) return;
        _readingsBuilt = true;
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = L.T("چی نشون داده بشه؟", "What to show"), Style = (Style)Application.Current.Resources["H2"], Margin = new Thickness(0, 6, 0, 0),
        });
        foreach (var (key, fa, en) in _groups)
        {
            panel.Children.Add(new TextBlock { Text = L.T(fa, en), Style = (Style)Application.Current.Resources["Label"], Margin = new Thickness(0, 12, 0, 4) });
            var wrap = _readings(key);
            if (wrap.Parent is Border old) old.Child = null;
            else if (wrap.Parent is Panel oldPanel) oldPanel.Children.Remove(wrap);
            panel.Children.Add(wrap);
        }
        _readingsHost.Child = panel;
    }
}
