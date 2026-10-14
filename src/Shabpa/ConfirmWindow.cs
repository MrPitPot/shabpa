using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Shabpa.Core;

namespace Shabpa;

/// <summary>A small themed "are you sure" dialog. Nothing in the app that changes the PC or the schedule
/// happens without one (unless the user turned confirmations off in Settings).</summary>
public sealed class ConfirmWindow : Window
{
    private ConfirmWindow(string title, string message, string yes, string no, bool danger, string? detail)
    {
        FlowDirection = Ui.Flow;
        Title = L.T("شب‌پا", "Shabpa");
        SizeToContent = SizeToContent.WidthAndHeight;
        MinWidth = 380;
        MaxWidth = 500;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = (System.Windows.Media.Brush)Application.Current.Resources["Bg"];
        FontFamily = (System.Windows.Media.FontFamily)Application.Current.Resources["Ui"];
        SourceInitialized += (_, _) => Ui.Chrome(this);

        var panel = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
        panel.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.Resources["H2"], TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock
        {
            Text = message, Style = (Style)Application.Current.Resources["Body"], TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0), LineHeight = 22,
        });
        if (!string.IsNullOrWhiteSpace(detail))
        {
            panel.Children.Add(new Border
            {
                Background = (System.Windows.Media.Brush)Application.Current.Resources[danger ? "RedSoft" : "AccentSoft"],
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 14, 0, 0),
                Child = new TextBlock
                {
                    Text = detail, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold,
                    Foreground = (System.Windows.Media.Brush)Application.Current.Resources[danger ? "Red" : "Accent"],
                },
            });
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 22, 0, 0) };
        var yesButton = new Button
        {
            Content = yes, Style = (Style)Application.Current.Resources[danger ? "BtnDanger" : "BtnPrimary"],
            Padding = new Thickness(20, 10, 20, 10), Margin = new Thickness(0, 0, 10, 0),
        };
        var noButton = new Button
        {
            Content = no, Style = (Style)Application.Current.Resources["Btn"], Padding = new Thickness(20, 10, 20, 10), IsCancel = true, IsDefault = true,
        };
        yesButton.Click += (_, _) => { DialogResult = true; };
        noButton.Click += (_, _) => { DialogResult = false; };
        buttons.Children.Add(yesButton);
        buttons.Children.Add(noButton);
        panel.Children.Add(buttons);
        Content = panel;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) DialogResult = false; };
        // Enter never confirms by accident: the safe button holds the default.
        Loaded += (_, _) => noButton.Focus();
    }

    /// <summary>A non-modal copy for screenshots.</summary>
    internal static Window Preview(string title, string message, string yes, string no, bool danger, string? detail)
    {
        var window = new ConfirmWindow(title, message, yes, no, danger, detail);
        window.Show();
        return window;
    }

    /// <summary>Shows the dialog and returns true only when the user chose the confirm button.</summary>
    public static bool Ask(string title, string message, string? yes = null, string? no = null, bool danger = false, string? detail = null)
    {
        var hub = Hub.I;
        if (hub is null || hub.Demo || !hub.Settings.ConfirmActions) return true;
        var window = new ConfirmWindow(title, message, yes ?? L.T("تایید", "Confirm"), no ?? L.T("انصراف", "Cancel"), danger, detail);
        return window.ShowDialog() == true;
    }
}
