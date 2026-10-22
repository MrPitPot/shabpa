using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        Width = 468;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Res("Bg");
        FontFamily = (FontFamily)Application.Current.Resources["Ui"];
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        UseLayoutRounding = true;
        SourceInitialized += (_, _) => Ui.Chrome(this);

        // The flyout of the design: a near-opaque sheet with a big tinted icon, centred text and one full-width confirm button,
        // over a darker scrim (no blur in WPF; the scrim stands in for it).
        Kit.GlassCard sheet = null!; // built below; the button handlers only run later
        var hue = danger ? "HueE" : "HueA";
        var panel = new StackPanel();
        panel.Children.Add(new Kit.IconBadge("bolt", hue, 52, 26, 16) { HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(new TextBlock
        {
            Text = title, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Res("Text"), TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 14, 0, 0), LineHeight = 32, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        });
        panel.Children.Add(new TextBlock
        {
            Text = message, Style = (Style)Application.Current.Resources["Body"], TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 8, 0, 0), LineHeight = 23,
        });
        if (!string.IsNullOrWhiteSpace(detail))
        {
            panel.Children.Add(new Border
            {
                Background = Res(hue + "Soft"),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 16, 0, 0),
                Child = new TextBlock
                {
                    Text = detail, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, FontSize = 14, TextAlignment = TextAlignment.Center,
                    Foreground = Res(danger ? "Red" : "Accent"), LineHeight = 23,
                },
            });
        }

        var yesButton = new Button
        {
            Content = yes, Style = (Style)Application.Current.Resources[danger ? "BtnDanger" : "BtnPrimary"],
            Padding = new Thickness(20, 13, 20, 13), FontSize = 14.5, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 22, 0, 0),
        };
        Kit.Skin.SetCorner(yesButton, new CornerRadius(24));
        var noButton = new Button
        {
            Content = no, Style = (Style)Application.Current.Resources["Btn"], Padding = new Thickness(20, 10, 20, 10), IsCancel = true, IsDefault = true,
            HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 10, 0, 0),
        };
        // the sheet sinks away first (150 ms), then the dialog ends; a second click meanwhile is ignored
        var leaving = false;
        void Choose(bool result)
        {
            if (leaving) return;
            leaving = true;
            Motion.Dismiss(sheet, () => { try { DialogResult = result; } catch (InvalidOperationException) { Close(); } });
        }
        yesButton.Click += (_, _) => Choose(true);
        noButton.Click += (_, _) => Choose(false);
        panel.Children.Add(yesButton);
        panel.Children.Add(noButton);

        sheet = new Kit.GlassCard
        {
            Background = Res("FlyoutFill"), CornerRadius = new CornerRadius(22), Padding = new Thickness(28, 28, 28, 24),
            Margin = new Thickness(22, 18, 22, 38), Child = panel, RenderTransformOrigin = new Point(0.5, 0.5),
        };
        var root = new Grid();
        root.Children.Add(new Border { Background = Res("Wallpaper") });
        root.Children.Add(new Border { Background = Res("Bg"), Opacity = 0.45 });
        root.Children.Add(sheet);
        Content = root;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Choose(false); };
        Loaded += (_, _) =>
        {
            // Enter never confirms by accident: the safe button holds the default.
            noButton.Focus();
            if (!Motion.Enabled) return;
            // the sheet springs up from 40 px below and 94% size (the design's flyout)
            var scale = new ScaleTransform(0.94, 0.94);
            var move = new TranslateTransform(0, 40);
            sheet.RenderTransform = new TransformGroup { Children = { scale, move } };
            sheet.Opacity = 0;
            Kit.Fx.Anim(scale, ScaleTransform.ScaleXProperty, 1);
            Kit.Fx.Anim(scale, ScaleTransform.ScaleYProperty, 1);
            Kit.Fx.Anim(move, TranslateTransform.YProperty, 0, null, 700);
            sheet.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop });
            sheet.Opacity = 1;
        };
    }

    private static System.Windows.Media.Brush Res(string key) => (System.Windows.Media.Brush)Application.Current.Resources[key];

    /// <summary>A non-modal copy for screenshots.</summary>
    internal static Window Preview(string title, string message, string yes, string no, bool danger, string? detail)
    {
        var window = new ConfirmWindow(title, message, yes, no, danger, detail);
        window.Show();
        return window;
    }

    /// <summary>Shows the dialog and returns true only when the user chose the confirm button.</summary>
    /// <summary>Always asks, even when the "ask before every action" setting is off. For things that cannot be taken
    /// back, such as deleting data.</summary>
    public static bool AskAlways(string title, string message, string? yes = null, string? no = null, bool danger = false, string? detail = null)
    {
        var hub = Hub.I;
        if (hub is { Demo: true }) return true;
        var window = new ConfirmWindow(title, message, yes ?? L.T("تایید", "Confirm"), no ?? L.T("انصراف", "Cancel"), danger, detail);
        OffScreenForTests(window);
        return window.ShowDialog() == true;
    }

    // Debug builds only: probes that answer the dialog themselves keep it out of the user's sight.
    [System.Diagnostics.Conditional("DEBUG")]
    private static void OffScreenForTests(Window w)
    {
        if (Environment.GetEnvironmentVariable("SHABPA_TEST_OFFSCREEN") != "1") return;
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        w.Left = -30000;
        w.Top = -30000;
        w.ShowActivated = false;
    }

    public static bool Ask(string title, string message, string? yes = null, string? no = null, bool danger = false, string? detail = null)
    {
        var hub = Hub.I;
        if (hub is null || hub.Demo || !hub.Settings.ConfirmActions) return true;
        var window = new ConfirmWindow(title, message, yes ?? L.T("تایید", "Confirm"), no ?? L.T("انصراف", "Cancel"), danger, detail);
        return window.ShowDialog() == true;
    }
}
