using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>One place to arrange the overlay: a small picture of the screen with six zones, and the groups of readings
/// (graphics card, processor, clock...) as tiles you drag between zones or into a new order. Click a tile to choose which
/// readings of that group are shown. Everything is saved as the person changes it.</summary>
internal sealed class OverlayEditor : StackPanel
{
    private static readonly string[] Zones = ["tl", "tc", "tr", "bl", "bc", "br"];

    private static readonly Dictionary<string, (string fa, string en)> ZoneNames = new()
    {
        ["tl"] = ("بالا چپ", "Top left"), ["tc"] = ("بالا وسط", "Top centre"), ["tr"] = ("بالا راست", "Top right"),
        ["bl"] = ("پایین چپ", "Bottom left"), ["bc"] = ("پایین وسط", "Bottom centre"), ["br"] = ("پایین راست", "Bottom right"),
        ["main"] = ("کادر اصلی", "Main frame"),
    };

    private readonly Func<OverlaySettings> _settings;
    private readonly IReadOnlyList<(string key, string fa, string en)> _groups;
    private readonly Action _changed;
    private readonly Func<string, Panel> _readings;
    private readonly Dictionary<string, StackPanel> _zonePanels = [];
    private readonly Dictionary<string, Border> _zoneBorders = [];
    private readonly Border _readingsHost = new() { Margin = new Thickness(0, 14, 0, 0) };
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
            Child = mainPanel, Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(8), CornerRadius = new CornerRadius(14), AllowDrop = true, Background = Brushes.Transparent,
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
            Style = (Style)Application.Current.Resources["Label"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0),
        });
        Children.Add(mainBorder);
        Children.Add(new TextBlock
        {
            Text = L.T("یا یه بخش رو بکش به یه ناحیه‌ی صفحه تا کادر جدا و مستقل داشته باشه:", "Or drag a group into a screen zone to give it a frame of its own:"),
            Style = (Style)Application.Current.Resources["Label"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0),
        });

        // the screen: a top band, the game in the middle, a bottom band
        var screen = new Grid { FlowDirection = FlowDirection.LeftToRight, MinHeight = 250, Margin = new Thickness(6) };
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
                Child = panel, Margin = new Thickness(4), Padding = new Thickness(6), CornerRadius = new CornerRadius(12), AllowDrop = true, Background = SceneArt.ZoneShade,
                BorderThickness = new Thickness(1.5), BorderBrush = SceneArt.ZoneLine, Tag = key,
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
            Text = L.T("بازی", "Game"), Foreground = SceneArt.ZoneText, FontSize = 20, FontWeight = FontWeights.SemiBold, Opacity = 0.6,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
        };
        Grid.SetRow(game, 1);
        Grid.SetColumnSpan(game, 3);
        screen.Children.Add(game);
        var frame = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        frame.Children.Add(SceneArt.Build(14));
        frame.Children.Add(screen);
        Children.Add(frame);

        _free.Margin = new Thickness(0, 10, 0, 0);
        Children.Add(_free);
        Children.Add(_readingsHost);
    }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    private static void Highlight(Border zone, bool on)
    {
        // the zone under the dragged tile swells a little and relaxes again when the tile leaves
        if (zone.RenderTransform is not ScaleTransform)
        {
            zone.RenderTransformOrigin = new Point(0.5, 0.5);
            zone.RenderTransform = new ScaleTransform(1, 1);
        }
        var swell = (ScaleTransform)zone.RenderTransform;
        Fx.Anim(swell, ScaleTransform.ScaleXProperty, on ? 1.015 : 1);
        Fx.Anim(swell, ScaleTransform.ScaleYProperty, on ? 1.015 : 1);
        if ((string)zone.Tag == "main")
        {
            zone.BorderBrush = on ? Brush("Accent") : Brush("Line");
            zone.Background = on ? Brush("AccentSoft") : Brushes.Transparent;
            return;
        }
        zone.BorderBrush = on ? SceneArt.ZoneHot : SceneArt.ZoneLine;
        zone.Background = on ? SceneArt.ZoneHotFill : SceneArt.ZoneShade;
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
        // an empty zone names itself, so it is clear where a tile would land
        foreach (var (key, panel) in _zonePanels)
        {
            if (panel.Children.Count > 0 || !ZoneNames.TryGetValue(key, out var name)) continue;
            panel.Children.Add(new TextBlock
            {
                Text = L.T(name.fa, name.en), FontSize = 11.5, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, IsHitTestVisible = false,
                Foreground = key == "main" ? Brush("Faint") : SceneArt.ZoneText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, key == "main" ? 14 : 24, 0, 0),
                FlowDirection = Ui.Flow,
            });
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
            Style = (Style)Application.Current.Resources["Label"], TextWrapping = TextWrapping.Wrap, MaxWidth = 340, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        });
        return row;
    }

    /// <summary>Six small dots: the drag handle.</summary>
    private static FrameworkElement Grip()
    {
        var grid = new Grid { Width = 10, Height = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), IsHitTestVisible = false };
        for (var c = 0; c < 2; c++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var r = 0; r < 3; r++) grid.RowDefinitions.Add(new RowDefinition());
        for (var i = 0; i < 6; i++)
        {
            var dot = new System.Windows.Shapes.Ellipse { Width = 3, Height = 3, Fill = Brush("Faint"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(dot, i % 2);
            Grid.SetRow(dot, i / 2);
            grid.Children.Add(dot);
        }
        return grid;
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
            FontSize = 11, Foreground = Brush("Muted"),
        });
        var row = new DockPanel { FlowDirection = FlowDirection.LeftToRight, Margin = new Thickness(11, 8, 11, 8) };
        var grip = Grip();
        DockPanel.SetDock(grip, Dock.Left);
        row.Children.Add(grip);
        row.Children.Add(text);
        var face = new Grid();
        if (selected) face.Children.Add(new Border { Background = Brush("AccentSoft"), CornerRadius = new CornerRadius(9) });
        face.Children.Add(row);
        var tile = new Border
        {
            Child = face, Margin = new Thickness(0, 0, 0, 4), CornerRadius = new CornerRadius(10), Cursor = Cursors.SizeAll, AllowDrop = true,
            Background = Brush("Panel3"), BorderThickness = new Thickness(1.5), BorderBrush = Brush(selected ? "Accent" : "Line"), Tag = group,
        };
        Fx.Attach(tile, grow: 1.025, press: 1);
        tile.PreviewMouseLeftButtonDown += (_, e) => { _pressed = group; _down = e.GetPosition(this); };
        tile.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (_pressed != group) return;
            _pressed = null;
            _selected = group;
            Rebuild();
            PopTile(group);
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
        // the dropped tile pops in (a short spring, nothing left running afterwards)
        PopTile(group);
    }

    private void PopTile(string group)
    {
        foreach (var panel in _zonePanels.Values)
            foreach (var tile in panel.Children.OfType<Border>().Where(b => (string)b.Tag == group))
                Motion.Pop(tile);
        foreach (var row in _free.Children.OfType<StackPanel>())
            foreach (var tile in row.Children.OfType<Border>().Where(b => (string?)b.Tag == group))
                Motion.Pop(tile);
    }

    private bool _readingsBuilt;

    /// <summary>Every group with all its readings as tick boxes, under the screen: anything can be switched on or off.
    /// The page owns the checkboxes and keeps them in step with the settings; they are placed here once.</summary>
    private void ShowReadings()
    {
        if (_readingsBuilt) return;
        _readingsBuilt = true;
        var panel = new StackPanel();
        panel.Children.Add(new Border { Height = 1, Background = Brush("Line"), Margin = new Thickness(0, 0, 0, 14) });
        panel.Children.Add(new TextBlock
        {
            Text = L.T("چی نشون داده بشه؟", "What to show"), Style = (Style)Application.Current.Resources["H2"], Margin = new Thickness(0, 0, 0, 2),
        });
        foreach (var (key, fa, en) in _groups)
        {
            panel.Children.Add(new TextBlock { Text = L.T(fa, en), Style = (Style)Application.Current.Resources["Label"], FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 6) });
            var wrap = _readings(key);
            if (wrap.Parent is Border old) old.Child = null;
            else if (wrap.Parent is Panel oldPanel) oldPanel.Children.Remove(wrap);
            panel.Children.Add(wrap);
        }
        _readingsHost.Child = panel;
    }
}
