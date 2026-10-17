using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa;

/// <summary>The birthday party: a card with a cake over the whole window, and (with motion on) one shower of confetti in
/// the theme's own colours. The confetti falls once and is removed, so nothing keeps running; with animations off, in
/// "Use less RAM" mode or with Windows' animation effects off there is only the card. A click outside the card, Esc or
/// the button closes it.</summary>
public sealed class BirthdayParty : Grid
{
    private static readonly Random Dice = new();
    private readonly Border _card;
    private bool _closing;

    private BirthdayParty(Birthday.Kind kind)
    {
        Background = new SolidColorBrush(Color.FromArgb(0x73, 0, 0, 0));
        Focusable = true;
        var (title, text) = Birthday.Message(kind);

        var body = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var cake = ColorEmoji.Icon("🎂", 64, K.Brush("Text"));
        if (cake is FrameworkElement fe) fe.HorizontalAlignment = HorizontalAlignment.Center;
        body.Children.Add(cake);
        var heading = new TextBlock { Style = K.Style("H1"), FontSize = 24, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
        ColorEmoji.Fill(heading, title);
        body.Children.Add(heading);
        var line = new TextBlock { Style = K.Style("Body"), FontSize = 14, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), LineHeight = 24 };
        ColorEmoji.Fill(line, text);
        body.Children.Add(line);
        var thanks = new Button
        {
            Style = K.Style("BtnPrimary"), Content = L.T("مرسی شب‌پا!", "Thanks, Shabpa!"), HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 22, 0, 0), Padding = new Thickness(30, 10, 30, 10),
        };
        thanks.Click += (_, _) => Close();
        body.Children.Add(thanks);

        // the flyout surface of the design: near-opaque sheet, rim light, layered shadow
        var sheet = new Grid { MaxWidth = 460, Margin = new Thickness(24), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        sheet.Children.Add(new Shadow { Radius = 22, Blur = 40, Spread = -12, OffsetY = 18, Layers = 8, Color = K.ColorOf("ShadowHoverC"), IsHitTestVisible = false });
        _card = new Border
        {
            Child = body, Padding = new Thickness(36, 30, 36, 28), CornerRadius = new CornerRadius(22),
            Background = K.Brush("FlyoutFill"), BorderBrush = K.Brush("CardRim"), BorderThickness = new Thickness(1),
        };
        sheet.Children.Add(_card);
        sheet.MouseLeftButtonDown += (_, e) => e.Handled = true; // clicks on the card do not close it
        Children.Add(sheet);

        MouseLeftButtonDown += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        Loaded += (_, _) =>
        {
            Keyboard.Focus(thanks);
            if (!Motion.Enabled) return;
            Motion.Pop(sheet);
            Confetti();
        };
    }

    /// <summary>Puts the party over everything in <paramref name="host"/> (the window's root grid).</summary>
    public static void Show(Panel host, Birthday.Kind kind)
    {
        if (kind == Birthday.Kind.None || host.Children.OfType<BirthdayParty>().Any()) return;
        host.Children.Add(new BirthdayParty(kind));
        UiSound.Play(UiSound.Kind.Notice);
    }

    private void Close()
    {
        if (_closing) return;
        _closing = true;
        Motion.Dismiss(this, () => (Parent as Panel)?.Children.Remove(this), 0.22);
    }

    /// <summary>One shower: 80 small paper pieces fall from above the window, drifting and turning, then the layer goes.</summary>
    private void Confetti()
    {
        var width = Math.Max(ActualWidth, 400);
        var height = Math.Max(ActualHeight, 300);
        var colours = new[] { "Accent", "Gold", "Green", "Red", "HueA", "HueB", "HueC", "HueD", "HueE" }
            .Select(k => Application.Current.Resources[k] is Brush b ? b : Brushes.Gold).ToArray();
        var layer = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
        Children.Insert(0, layer);
        var left = 0;
        for (var i = 0; i < 80; i++)
        {
            var round = Dice.Next(4) == 0;
            Shape piece = round
                ? new Ellipse { Width = 8, Height = 8 }
                : new Rectangle { Width = 6 + Dice.Next(5), Height = 10 + Dice.Next(7), RadiusX = 1.5, RadiusY = 1.5 };
            piece.Fill = colours[Dice.Next(colours.Length)];
            var spin = new RotateTransform(Dice.Next(360));
            var fall = new TranslateTransform(Dice.NextDouble() * width, -30);
            piece.RenderTransformOrigin = new Point(0.5, 0.5);
            piece.RenderTransform = new TransformGroup { Children = { spin, fall } };
            layer.Children.Add(piece);

            var delay = TimeSpan.FromMilliseconds(Dice.Next(1400));
            var duration = TimeSpan.FromMilliseconds(2600 + Dice.Next(1800));
            var down = new DoubleAnimation(-30 - Dice.Next(120), height + 30, duration) { BeginTime = delay, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
            var drift = new DoubleAnimation(fall.X, fall.X + (Dice.NextDouble() - 0.5) * 220, duration) { BeginTime = delay, EasingFunction = new SineEase() };
            var turn = new DoubleAnimation(spin.Angle, spin.Angle + (Dice.Next(2) == 0 ? -1 : 1) * (360 + Dice.Next(540)), duration) { BeginTime = delay };
            down.Completed += (_, _) =>
            {
                layer.Children.Remove(piece);
                if (++left == 80) Children.Remove(layer);
            };
            fall.BeginAnimation(TranslateTransform.YProperty, down);
            fall.BeginAnimation(TranslateTransform.XProperty, drift);
            spin.BeginAnimation(RotateTransform.AngleProperty, turn);
        }
    }
}
