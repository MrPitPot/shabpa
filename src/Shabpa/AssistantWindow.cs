using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa;

/// <summary>The stand-alone command box (tray item and global shortcut): Bidar and the typing panel in one small window.
/// The same panel lives in the speech bubble inside the main window (see MainWindow.Assistant.cs).</summary>
public sealed class AssistantWindow : Window
{
    private static AssistantWindow? _open;

    private readonly BidarOwl _owl = new() { Width = 76, Height = 91, IdleLife = true, Margin = new Thickness(0, 0, 14, 0) };
    private readonly AssistantPanel _panel = new();

    /// <summary>Opens the box, or brings the open one to the front.</summary>
    public static void Open()
    {
        if (_open is { } w)
        {
            w.Activate();
            w._panel.FocusInput();
            return;
        }
        _open = new AssistantWindow();
        _open.Closed += (_, _) => _open = null;
        _open.Show();
        _open.Activate();
    }

    private AssistantWindow()
    {
        FlowDirection = Ui.Flow;
        Title = L.T("فرمان متنی شب‌پا", "Shabpa command box");
        Width = 540;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        this.SetResourceReference(BackgroundProperty, "Bg");
        FontFamily = (FontFamily)Application.Current.Resources["Ui"];
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        UseLayoutRounding = true;
        SourceInitialized += (_, _) => Ui.Chrome(this);

        var title = new TextBlock { Text = L.T("چی کار کنم؟", "What should I do?"), FontSize = 19, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(_owl, Dock.Left);
        header.Children.Add(_owl);
        header.Children.Add(title);

        var panel = new StackPanel();
        panel.Children.Add(header);
        panel.Children.Add(_panel);
        _panel.Mood += m => _owl.SetMood(m);
        _panel.Spoke += () => _owl.Talk();
        _panel.Activity += _owl.Touch;

        var sheet = new GlassCard { CornerRadius = new CornerRadius(22), Padding = new Thickness(24, 22, 24, 22), Margin = new Thickness(18), Child = panel };
        sheet.SetResourceReference(Control.BackgroundProperty, "FlyoutFill");
        var wall = new Border();
        wall.SetResourceReference(Border.BackgroundProperty, "Wallpaper");
        var veil = new Border { Opacity = 0.45 };
        veil.SetResourceReference(Border.BackgroundProperty, "Bg");
        var root = new Grid();
        root.Children.Add(wall);
        root.Children.Add(veil);
        root.Children.Add(sheet);
        Content = root;

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        PreviewMouseMove += (_, e) =>
        {
            var p = e.GetPosition(_owl);
            _owl.LookAt(p.X - _owl.ActualWidth / 2, p.Y - _owl.ActualHeight / 2);
        };
        Loaded += (_, _) => _panel.FocusInput();
    }
}
