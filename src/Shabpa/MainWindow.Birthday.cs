using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa;

/// <summary>The birthday greeting (a glass card over the window with a short confetti shower) and the festive decoration of the
/// title area. Everything here is created when needed and removed afterwards: when it is not the birthday nothing exists.</summary>
public partial class MainWindow
{
    private Grid? _birthday;
    private ConfettiLayer? _confetti;
    private Bunting? _bunting;

    internal Pages.PageBase PageFor(string key) => Page(key);

    /// <summary>Test aid: the greeting card is on screen.</summary>
    public bool BirthdayOpen => _birthday is not null;

    /// <summary>Test aid: the shower (null when the greeting is not open).</summary>
    public ConfettiLayer? BirthdayConfetti => _confetti;

    /// <summary>Test aid: the greeting card itself (for screenshots).</summary>
    public FrameworkElement? BirthdayCard => _birthday?.Children.OfType<GlassCard>().FirstOrDefault();

    /// <summary>Test aid: the decoration along the title bar is present.</summary>
    public bool BirthdayDecor => _bunting is not null;

    private void OnBirthdayChanged()
    {
        ApplyBirthdayLook();
        if (Shown) Pages.PageBase.SafeRefresh(_current); // the home page changes its greeting line
    }

    private void UpdateBirthdayDecor()
    {
        var on = Hub.I.BirthdayLook;
        if (on && _bunting is null)
        {
            _bunting = new Bunting { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(170, 0, 170, 0), Opacity = 0.9 };
            Panel.SetZIndex(_bunting, 5);
            Root.Children.Add(_bunting);
        }
        else if (!on && _bunting is not null)
        {
            Root.Children.Remove(_bunting);
            _bunting = null;
        }
    }

    /// <summary>Shows the greeting card and the shower. Used on the birthday and for the preview in Settings (with the name typed there).</summary>
    public void ShowBirthdayGreeting(string? name = null)
    {
        if (_birthday is not null) return;
        name = Birthday.CleanName(name ?? Hub.I.Settings.Birthday.Name);

        var scrim = new SolidColorBrush(Color.FromArgb(0xB4, 0x16, 0x06, 0x1E));
        scrim.Freeze();
        var host = new Grid { Background = scrim, Focusable = false };
        Panel.SetZIndex(host, 900);

        var shower = new ConfettiLayer();
        host.Children.Add(shower);

        var (card, body) = K.Card();
        card.Margin = new Thickness(28);
        card.Padding = new Thickness(34, 28, 34, 26);
        card.MaxWidth = 470;
        card.HorizontalAlignment = HorizontalAlignment.Center;
        card.VerticalAlignment = VerticalAlignment.Center;
        var cake = ColorEmoji.Icon("🎂", 58);
        ((FrameworkElement)cake).HorizontalAlignment = HorizontalAlignment.Center;
        ((FrameworkElement)cake).Margin = new Thickness(0, 0, 0, 8);
        body.Children.Add(cake);
        var title = new TextBlock { Style = K.Style("H1"), FontSize = 26, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Foreground = K.Brush("Accent") };
        ColorEmoji.Fill(title, Birthday.Title(name, null).Replace(" 🎂", ""));
        body.Children.Add(title);
        var wish = new TextBlock { Style = K.Style("Body"), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 20), Foreground = K.Brush("Muted") };
        ColorEmoji.Fill(wish, BirthdayWishes.Text(name, Birthday.Now()));
        body.Children.Add(wish);
        var thanks = new Button
        {
            Style = K.Style("BtnPrimary"), Content = L.T("ممنونم", "Thank you"), HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(30, 9, 30, 9),
        };
        body.Children.Add(thanks);
        host.Children.Add(card);

        void Close()
        {
            PreviewKeyDown -= OnKey;
            shower.Stop();
            Root.Children.Remove(host);
            if (ReferenceEquals(_birthday, host)) { _birthday = null; _confetti = null; }
        }
        void OnKey(object? s, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close();
        }
        thanks.Click += (_, _) => Close();
        host.MouseLeftButtonDown += (_, e) => { if (!card.IsMouseOver) Close(); };
        PreviewKeyDown += OnKey;

        _birthday = host;
        _confetti = shower;
        Root.Children.Add(host);
        // the window has its size once laid out; no focus is taken (the greeting never steals the keyboard)
        host.Dispatcher.BeginInvoke(() => { if (ReferenceEquals(_birthday, host)) shower.Start(); }, System.Windows.Threading.DispatcherPriority.Loaded);
        if (Motion.Enabled)
        {
            Motion.Rise(card, 0, 26, 0.7);
            host.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop });
        }
    }

    /// <summary>Test aid: closes the greeting if it is open.</summary>
    public void CloseBirthdayGreeting()
    {
        if (_birthday is null) return;
        _confetti?.Stop();
        Root.Children.Remove(_birthday);
        _birthday = null;
        _confetti = null;
    }
}
