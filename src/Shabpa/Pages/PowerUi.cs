using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>Small builders shared by the four power pages (Power, Rules, Energy, Wake-up): option tiles, callouts, button
/// content, cards. Everything comes from the kit and the shared tokens.</summary>
internal static class PowerUi
{
    /// <summary>Test aid: <c>SHABPA_SHOT_STATE</c> (for example <c>power:after</c>) lets a screenshot run show a state the page
    /// is not in by default. Not set in normal use, so it does nothing.</summary>
    public static bool Shot(string key) =>
        string.Equals(Environment.GetEnvironmentVariable("SHABPA_SHOT_STATE"), key, StringComparison.OrdinalIgnoreCase);

    public static string ActionIcon(PowerAction a) => a switch
    {
        PowerAction.Sleep => "moon",
        PowerAction.Hibernate => "hib",
        PowerAction.Restart => "refresh",
        PowerAction.Lock => "lock",
        _ => "pw",
    };

    public static string ActionHue(PowerAction a) => a switch
    {
        PowerAction.Sleep => "HueA",
        PowerAction.Hibernate => "HueB",
        PowerAction.Restart => "HueC",
        PowerAction.Lock => "HueD",
        _ => "HueE",
    };

    /// <summary>A card with a section title (H2) and an optional one-line explanation; blocks stack with a 16 px gap.</summary>
    public static (GlassCard card, StackPanel body) Card(string? title, string? subtitle = null)
    {
        var body = new StackPanel();
        if (title is not null) body.Children.Add(new TextBlock { Text = title, Style = K.Style("H2") });
        if (subtitle is not null) body.Children.Add(new TextBlock { Text = subtitle, Style = K.Style("Body"), Margin = new Thickness(0, 3, 0, 0) });
        var card = new GlassCard { Margin = new Thickness(0, 0, 0, 16), Child = body };
        return (card, body);
    }

    /// <summary>Option tile (a radio button in the shared Tile style): tinted icon badge, bold title, small muted line.
    /// The badge springs on hover like the quick actions of Home.</summary>
    public static RadioButton Tile(string icon, string hue, string title, string sub, string group, string tag)
    {
        var badge = new IconBadge(icon, hue, 40, 20, 12) { HorizontalAlignment = HorizontalAlignment.Left };
        var texts = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        texts.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = K.Brush("Text"), TextWrapping = TextWrapping.Wrap, LineHeight = 22, LineStackingStrategy = LineStackingStrategy.BlockLineHeight });
        texts.Children.Add(new TextBlock { Text = sub, FontSize = 11.5, Foreground = K.Brush("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0), LineHeight = 18, LineStackingStrategy = LineStackingStrategy.BlockLineHeight });
        var content = new StackPanel();
        content.Children.Add(badge);
        content.Children.Add(texts);
        var tile = new RadioButton { Style = K.Style("Tile"), GroupName = group, Tag = tag, Content = content, Margin = new Thickness(8), VerticalContentAlignment = VerticalAlignment.Top };
        tile.MouseEnter += (_, _) => badge.Pose(true);
        tile.MouseLeave += (_, _) => badge.Pose(false);
        return tile;
    }

    /// <summary>Puts <paramref name="e"/> in grid cell (row, column) with the given span.</summary>
    public static void Place(UIElement e, int row, int column, int span = 1)
    {
        Grid.SetRow(e, row);
        Grid.SetColumn(e, column);
        Grid.SetColumnSpan(e, span);
    }

    /// <summary>Icon + text for a button; the icon follows the button's foreground.</summary>
    public static StackPanel ButtonContent(string icon, TextBlock text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Icon(icon, 18, null) { Margin = new Thickness(0, 0, 8, 0) });
        text.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(text);
        return row;
    }

    /// <summary>Soft tinted strip with an icon and a line of text (live summary, errors).</summary>
    public sealed class Callout : Border
    {
        private readonly Icon _icon = new("clk", 18, K.Brush("Accent")) { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 12, 0) };
        public TextBlock Text { get; } = new() { FontSize = 13.5, FontWeight = FontWeights.Medium, Foreground = K.Brush("Text"), TextWrapping = TextWrapping.Wrap, LineHeight = 24, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, VerticalAlignment = VerticalAlignment.Center };

        public Callout(string tint = "Accent", string icon = "clk")
        {
            CornerRadius = new CornerRadius(12);
            Padding = new Thickness(16, 11, 16, 11);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(_icon);
            Grid.SetColumn(Text, 1);
            grid.Children.Add(Text);
            Child = grid;
            Set(tint, icon);
        }

        private string _iconName = "";

        public void Set(string tint, string icon)
        {
            Background = K.Brush(tint + "Soft");
            _icon.Foreground = K.Brush(tint);
            _icon.IconName = icon;
            if (_iconName.Length > 0 && _iconName != icon && IsLoaded) Motion.Pop(_icon);
            _iconName = icon;
        }

        /// <summary>Changes the line of text; when it really changed the new text slides in from 5 px below.</summary>
        public void Say(string text)
        {
            if (Text.Text == text) return;
            var had = Text.Text.Length > 0;
            Text.Text = text;
            if (!had || !Motion.Enabled || !IsLoaded) return;
            var move = new TranslateTransform(0, 5);
            Text.RenderTransform = move;
            Text.Opacity = 0.3;
            Fx.Anim(Text, OpacityProperty, 1, Fx.Out, 420);
            Fx.Anim(move, TranslateTransform.YProperty, 0, Fx.Out, 420);
        }
    }

    /// <summary>Label above a control, the way the fine-tune cards of the old pages read.</summary>
    public static TextBlock Caption(string text, double top = 0) =>
        new() { Text = text, Style = K.Style("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 8) };

    public static Border Hairline(double top = 14, double bottom = 14) =>
        new() { Height = 1, Background = K.Brush("Line"), Margin = new Thickness(0, top, 0, bottom) };

    public static TextBlock Empty(string text) =>
        new() { Text = text, Style = K.Style("Body"), Margin = new Thickness(0, 10, 0, 4) };

    // ---- motion helpers (all cheap: transform/opacity, one-shot, nothing left running) --------------------------

    /// <summary>A list row or block that was just added: a short rise (no-op while the page itself is entering).</summary>
    public static void Appear(FrameworkElement e, int index = 0)
    {
        if (Motion.Enabled) Motion.Rise(e, index, 12, 0.5);
    }

    /// <summary>The row leaves (fade + 14 px slide toward the end), then <paramref name="done"/> runs (the real delete).</summary>
    public static void Leave(FrameworkElement e, Action done)
    {
        if (!Motion.Enabled || !e.IsLoaded) { done(); return; }
        var move = new TranslateTransform();
        e.RenderTransform = move;
        var ms = TimeSpan.FromMilliseconds(220);
        var fade = new DoubleAnimation(1, 0, ms) { FillBehavior = FillBehavior.HoldEnd };
        fade.Completed += (_, _) => done();
        e.BeginAnimation(UIElement.OpacityProperty, fade);
        move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, Ui.Flow == FlowDirection.RightToLeft ? -18 : 18, ms) { EasingFunction = Fx.Out });
    }

    /// <summary>Shows or hides a panel; a panel that just appeared rises in (<see cref="Motion.Switch"/>).</summary>
    public static void Toggle(FrameworkElement panel, bool visible)
    {
        var was = panel.Visibility == Visibility.Visible;
        panel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (visible && !was && panel.IsLoaded) Motion.Switch(panel);
    }

    /// <summary>The icon badge of a tile springs when the tile is chosen.</summary>
    public static void PopTile(RadioButton tile)
    {
        if (tile.IsLoaded && tile.Content is Panel p && p.Children.Count > 0 && p.Children[0] is FrameworkElement badge) Motion.Pop(badge);
    }

    /// <summary>The badge of a card or row springs while the pointer is over it (like the stat cards of Home).</summary>
    public static void HoverBadge(FrameworkElement host, IconBadge badge)
    {
        if (host is Border { Background: null } b) b.Background = Brushes.Transparent;
        else if (host is Panel { Background: null } pn) pn.Background = Brushes.Transparent;
        host.MouseEnter += (_, _) => badge.Pose(true);
        host.MouseLeave += (_, _) => badge.Pose(false);
    }

    /// <summary>Row built by <c>PageBase.Row</c>: its badge reacts to the pointer.</summary>
    public static Grid Live(Grid row)
    {
        if (row.Children.OfType<IconBadge>().FirstOrDefault() is { } badge) HoverBadge(row, badge);
        return row;
    }

    /// <summary>Carries a number for <see cref="Count"/>; each change calls back (the clock only runs while it counts).</summary>
    private sealed class Tween : Animatable
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(Tween),
            new PropertyMetadata(0.0, (d, e) => ((Tween)d).On?.Invoke((double)e.NewValue)));
        public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public Action<double>? On;
        protected override Freezable CreateInstanceCore() => new Tween();
    }

    /// <summary>Shows <paramref name="to"/> in <paramref name="label"/> and, when motion is on, counts there from
    /// <paramref name="from"/> over about 0.8 s with the ease-out of the design (the final text is already in place).</summary>
    public static void Count(TextBlock label, double from, double to, Func<double, string> format, int ms = 800)
    {
        var animate = Motion.Enabled && Math.Abs(from - to) >= 1e-9;
        label.Text = format(animate ? from : to);
        if (!animate) return;
        var tween = new Tween { Value = to };
        tween.On = v => label.Text = format(v);
        var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Fx.Out, FillBehavior = FillBehavior.Stop };
        anim.Completed += (_, _) => { tween.On = null; label.Text = format(to); };
        tween.BeginAnimation(Tween.ValueProperty, anim);
    }

    /// <summary>Bar value that fills from its current value (one spring-free ease-out, nothing held).</summary>
    public static void Fill(ProgressBar bar, double to, int ms = 800)
    {
        var from = bar.Value;
        bar.Value = to;
        if (!Motion.Enabled || Math.Abs(from - to) < 1e-6) return;
        bar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty,
            new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Fx.Out, FillBehavior = FillBehavior.Stop });
    }
}
