using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Shabpa.Core;

namespace Shabpa;

/// <summary>The small bar that is on top of every window while Shabpa learns clicks or repeats them. While repeating it
/// lets every click through and never takes focus, so it cannot get in the way of what it is describing.</summary>
public sealed class MacroBanner : Window
{
    private readonly TextBlock _text = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 };
    private readonly bool _clickThrough;

    public event Action? Finished;
    public event Action? Cancelled;

    /// <param name="recording">True: shows the two buttons that end the lesson; false: a quiet bar that clicks go through.</param>
    public MacroBanner(bool recording)
    {
        _clickThrough = !recording;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = recording;
        SizeToContent = SizeToContent.WidthAndHeight;
        FlowDirection = Ui.Flow;
        FontFamily = (FontFamily)Application.Current.Resources["Ui"];

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 10, Height = 10, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
            Fill = (Brush)Application.Current.Resources[recording ? "Red" : "Accent"],
        });
        _text.Foreground = (Brush)Application.Current.Resources["Text"];
        row.Children.Add(_text);
        if (recording)
        {
            var done = new Button { Style = (Style)Application.Current.Resources["BtnPrimary"], Content = L.T("پایان و ذخیره", "Finish and save"), Margin = new Thickness(14, 0, 0, 0) };
            var cancel = new Button { Style = (Style)Application.Current.Resources["Btn"], Content = L.T("لغو", "Cancel"), Margin = new Thickness(8, 0, 0, 0) };
            done.Click += (_, _) => Finished?.Invoke();
            cancel.Click += (_, _) => Cancelled?.Invoke();
            row.Children.Add(done);
            row.Children.Add(cancel);
        }
        Content = new Border
        {
            Child = row, Padding = new Thickness(16, 11, 16, 11), CornerRadius = new CornerRadius(14), Margin = new Thickness(18),
            Background = (Brush)Application.Current.Resources["Panel2"], BorderBrush = (Brush)Application.Current.Resources["Line"], BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 22, ShadowDepth = 5, Opacity = 0.4, Color = Colors.Black },
        };
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - ActualWidth) / 2;
            Top = area.Top + 6;
        };
        SizeChanged += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - ActualWidth) / 2;
        };
    }

    public void SetText(string text) => _text.Text = text;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (!_clickThrough) return;
        var handle = new WindowInteropHelper(this).Handle;
        // WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW: clicks pass through and focus never moves here
        var style = GetWindowLongPtr(handle, -20).ToInt64() | 0x20 | 0x08000000 | 0x80;
        SetWindowLongPtr(handle, -20, new IntPtr(style));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}
