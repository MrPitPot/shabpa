using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa;

/// <summary>A reminder that cannot be missed: a small window on top of everything, with the alarm sound, until the person presses a button (the sound stops by itself after a
/// minute). It appears even when Shabpa's main window is closed to the tray, which a Windows notification cannot promise (Focus assist and the notification settings can
/// silence it). If more reminders arrive while it is open they are added to its list.</summary>
public sealed class AlarmWindow : Window
{
    private static AlarmWindow? _open;

    /// <summary>Shows the reminder. <paramref name="snooze"/> (when given) is run by the «ten minutes later» button.</summary>
    public static void Show(string title, string text, Action? snooze)
    {
        if (_open is { } open) { open.Add(title, text, snooze); return; }
        _open = new AlarmWindow(title, text, snooze);
        ((Window)_open).Show();
        _open.Activate();
        _open.Topmost = true;
    }

    private readonly StackPanel _items = new();
    private readonly List<Action> _snoozes = [];
    private readonly DispatcherTimer _silence = new() { Interval = TimeSpan.FromSeconds(60) };
    private readonly Button _later = new() { Style = (Style)Application.Current.Resources["Btn"], Padding = new Thickness(20, 10, 20, 10), Margin = new Thickness(0, 0, 10, 0) };

    private AlarmWindow(string title, string text, Action? snooze)
    {
        FlowDirection = Ui.Flow;
        Title = L.T("یادآور شب‌پا", "Shabpa reminder");
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = true;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = (Brush)Application.Current.Resources["Bg"];
        Foreground = (Brush)Application.Current.Resources["Text"];
        FontFamily = (FontFamily)Application.Current.Resources["Ui"];
        SourceInitialized += (_, _) => Ui.Chrome(this);

        var root = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
        root.Children.Add(new TextBlock { Text = "⏰  " + L.T("یادآور", "Reminder"), FontSize = 14, Foreground = (Brush)Application.Current.Resources["Gold"], FontWeight = FontWeights.SemiBold });
        root.Children.Add(_items);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 20, 0, 0) };
        var ok = new Button { Style = (Style)Application.Current.Resources["BtnPrimary"], Content = L.T("باشه", "OK"), Padding = new Thickness(26, 10, 26, 10), Margin = new Thickness(0, 0, 10, 0), IsDefault = true };
        ok.Click += (_, _) => Close();
        _later.Content = L.T("۱۰ دقیقه‌ی دیگه", "In 10 minutes");
        _later.Click += (_, _) => { foreach (var s in _snoozes) s(); Close(); };
        buttons.Children.Add(ok);
        buttons.Children.Add(_later);
        root.Children.Add(buttons);
        Content = root;

        Add(title, text, snooze);
        _silence.Tick += (_, _) => { _silence.Stop(); AlarmTone.Stop(); };
        _silence.Start();
        Closed += (_, _) => { _silence.Stop(); if (ReferenceEquals(_open, this)) { AlarmTone.Stop(); _open = null; } };
        Loaded += (_, _) => { if (!AlarmTone.Start()) Core.Log.Write("alarm sound could not start"); };
    }

    private void Add(string title, string text, Action? snooze)
    {
        _items.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, _items.Children.Count == 0 ? 10 : 14, 0, 0) });
        if (text.Length > 0)
            _items.Children.Add(new TextBlock { Text = text, FontSize = 14, Opacity = .85, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        if (snooze is not null) _snoozes.Add(snooze);
        if (IsLoaded) { _silence.Stop(); _silence.Start(); AlarmTone.Start(); }          // a reminder that comes in while the window is open sounds for a full minute too
        _later.Visibility = _snoozes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
