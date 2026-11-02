using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>The premium themes: a live preview of the chosen one (its palette and its moving backdrop) above a grid of small
/// mock-ups. Buying spends feathers; applying uses the same circle reveal as the colour chips in Settings.
/// Only the big preview animates; the tiles are still pictures built from the palette.</summary>
internal sealed class ThemeStoreView : StackPanel
{
    private readonly Hub _hub;
    private string _selected = PremiumTheme.All[0].Key;
    private string _sig = "";
    private readonly StackPanel _stage = new(), _grid = new();
    private ThemeScene? _live;

    public ThemeStoreView(Hub hub)
    {
        _hub = hub;
        Children.Add(_stage);
        Children.Add(_grid);
        Refresh(true);
    }

    private static Color Col(Palette p, string key) { var (r, g, b) = p.Rgb(key); return Color.FromRgb(r, g, b); }
    private static Brush Solid(Palette p, string key) { var b = new SolidColorBrush(Col(p, key)); b.Freeze(); return b; }

    public void Refresh(bool force = false)
    {
        var e = _hub.Rewards;
        var sig = $"{e.State.Feathers}|{string.Join(',', e.State.Owned.Where(o => o.StartsWith(PremiumTheme.Prefix)))}|{Ui.BaseTheme.Key}|{_selected}";
        if (!force && sig == _sig) return;
        _sig = sig;
        Draw(e);
    }

    /// <summary>A small picture of a window in the theme's colours: sidebar, two cards, an accent button and a few lines of text.</summary>
    private static Border Mock(Palette p, double width, double height, bool detailed)
    {
        var bg = new LinearGradientBrush(Col(p, "Bg"), Col(p, "Bg2"), 90);
        bg.Freeze();
        var root = new Grid { ClipToBounds = true };
        var side = new Border { Width = width * .2, HorizontalAlignment = HorizontalAlignment.Right, Background = Solid(p, "Side") };
        var lines = new StackPanel { Margin = new Thickness(width * .03, height * .1, width * .03, 0), VerticalAlignment = VerticalAlignment.Top };
        for (var i = 0; i < 4; i++) lines.Children.Add(new Rectangle { Height = height * .045, RadiusX = 3, RadiusY = 3, Margin = new Thickness(0, 0, 0, height * .06), Fill = Solid(p, i == 0 ? "Accent" : "Faint"), Opacity = i == 0 ? 1 : .6 });
        side.Child = lines;
        var body = new Grid { Margin = new Thickness(width * .04, height * .1, width * .24, height * .08) };
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(.9, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var hero = new Border { CornerRadius = new CornerRadius(8), Background = new LinearGradientBrush(Col(p, "Hero"), Col(p, "Hero2"), 20) };
        var heroLines = new StackPanel { Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        heroLines.Children.Add(new Rectangle { Height = height * .06, Width = width * .22, RadiusX = 3, RadiusY = 3, HorizontalAlignment = HorizontalAlignment.Right, Fill = Solid(p, "Text") });
        heroLines.Children.Add(new Rectangle { Height = height * .04, Width = width * .32, RadiusX = 2, RadiusY = 2, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0), Fill = Solid(p, "Muted") });
        hero.Child = heroLines;
        Grid.SetRow(hero, 0);
        var card = new Border { CornerRadius = new CornerRadius(8), Background = Solid(p, "Panel"), BorderBrush = Solid(p, "Line"), BorderThickness = new Thickness(1) };
        var cardGrid = new Grid { Margin = new Thickness(10) };
        cardGrid.Children.Add(new Border { Width = width * .16, Height = height * .09, CornerRadius = new CornerRadius(height * .045), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Background = new LinearGradientBrush(Col(p, "Accent1"), Col(p, "Accent2"), 0) });
        cardGrid.Children.Add(new Rectangle { Height = height * .04, Width = width * .3, RadiusX = 2, RadiusY = 2, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Fill = Solid(p, "Muted") });
        card.Child = cardGrid;
        Grid.SetRow(card, 2);
        body.Children.Add(hero);
        body.Children.Add(card);
        root.Children.Add(body);
        root.Children.Add(side);
        _ = detailed;
        return new Border { Width = width, Height = height, CornerRadius = new CornerRadius(12), Background = bg, ClipToBounds = true, Child = root, BorderBrush = Solid(p, "Line"), BorderThickness = new Thickness(1.2) };
    }

    private void Draw(RewardsEngine e)
    {
        _stage.Children.Clear();
        _grid.Children.Clear();
        var sel = PremiumTheme.Find(_selected) ?? PremiumTheme.All[0];
        var owned = e.OwnsTheme(sel.Key);
        var applied = Ui.BaseTheme.Key == sel.Key;

        // the big live preview: the mock-up with the backdrop moving behind it
        var frame = Mock(sel.Palette, 620, 250, true);
        var host = (Grid)((Border)frame).Child;
        
        _live = new ThemeScene(sel.Scene) { IsHitTestVisible = false, Width = 620, Height = 250 };
        host.Children.Insert(0, _live);
        frame.HorizontalAlignment = HorizontalAlignment.Center;
        frame.Margin = new Thickness(0, 4, 0, 10);
        _stage.Children.Add(frame);

        var info = new DockPanel { LastChildFill = true, Margin = new Thickness(4, 0, 4, 14) };
        var action = owned
            ? applied
                ? BoofiArt.Button(L.T("روشنه", "In use"), BoofiArt.Kind.Ok, null, 34)
                : BoofiArt.Button(L.T("اعمال کن", "Apply"), BoofiArt.Kind.Primary, () => Apply(sel), 34)
            : e.State.Feathers >= sel.Price
                ? BoofiArt.Button(L.T($"بخر · {Fa.N(sel.Price)} پَر", $"Buy · {sel.Price}"), BoofiArt.Kind.Primary, () => Buy(sel, e), 34)
                : BoofiArt.Button(L.T($"{Fa.N(sel.Price - e.State.Feathers)} پَر کم داری", $"{sel.Price - e.State.Feathers} short"), BoofiArt.Kind.Off, null, 34);
        action.Padding = new Thickness(18, 0, 18, 0);
        action.MinWidth = 150;
        DockPanel.SetDock(action, Dock.Left);
        info.Children.Add(action);
        var text = new StackPanel();
        text.Children.Add(BoofiArt.Text(sel.Name, 17, "Text", FontWeights.Bold));
        var note = BoofiArt.Text(sel.Note, 12.5, "Muted");
        note.Margin = new Thickness(0, 3, 0, 0);
        text.Children.Add(note);
        info.Children.Add(text);
        _stage.Children.Add(info);

        var wrap = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var t in PremiumTheme.All)
        {
            var theme = t;
            var has = e.OwnsTheme(theme.Key);
            var cell = new StackPanel { Width = 176, Margin = new Thickness(6, 0, 6, 12), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            var mock = Mock(theme.Palette, 176, 108, false);
            mock.BorderBrush = theme.Key == _selected ? Ui.Live("Accent") : mock.BorderBrush;
            mock.BorderThickness = new Thickness(theme.Key == _selected ? 2.4 : 1.2);
            cell.Children.Add(mock);
            var line = new DockPanel { Margin = new Thickness(2, 6, 2, 0) };
            var tag = BoofiArt.Text(Ui.BaseTheme.Key == theme.Key ? L.T("روشن", "In use") : has ? L.T("داری", "Owned") : Fa.N(theme.Price), 11.5, has ? "Green" : "Gold", FontWeights.SemiBold);
            DockPanel.SetDock(tag, Dock.Left);
            line.Children.Add(tag);
            line.Children.Add(BoofiArt.Text(theme.Name, 12.5, "Text", FontWeights.SemiBold));
            cell.Children.Add(line);
            cell.MouseLeftButtonUp += (_, ev) => { ev.Handled = true; _selected = theme.Key; Refresh(); };
            BoofiArt.Cache(cell);
            wrap.Children.Add(cell);
        }
        _grid.Children.Add(wrap);
    }

    private void Buy(PremiumTheme t, RewardsEngine e)
    {
        if (!ConfirmWindow.Ask(L.T($"تم «{t.Name}» رو بخری؟", $"Buy the “{t.Name}” theme?"),
                L.T($"{Fa.N(t.Price)} پَر از موجودیت کم می‌شه. سطحت پایین نمی‌آد.", $"{t.Price} feathers will be taken from your balance. Your level does not go down."), L.T("بخر", "Buy"), null, false, null)) return;
        if (e.BuyTheme(t.Key) == RewardsEngine.BuyResult.Ok)
        {
            _hub.Notify(L.T("خریدی!", "Bought!"), L.T($"تم «{t.Name}» مال توئه.", $"The “{t.Name}” theme is yours."), "rewards");
            Refresh(true);
            Apply(t);
        }
    }

    private void Apply(PremiumTheme t)
    {
        if (Application.Current.MainWindow is MainWindow main) main.SwitchTheme(t.Key, this);
        else { _hub.Settings.Theme = t.Key; _hub.Store.Save(); }
        Refresh(true);
    }
}
