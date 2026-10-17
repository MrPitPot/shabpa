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
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 10, Height = 10, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
            Fill = (Brush)Application.Current.Resources[recording ? "Red" : "Accent"],
        };
        row.Children.Add(dot);
        _text.Foreground = Ui.Live("Text");
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
        // flyout surface of the design: near-opaque sheet, rim light, layered shadow (kit:Shadow, no DropShadowEffect)
        var bar = new Grid { Margin = new Thickness(26, 14, 26, 32) };
        bar.Children.Add(new Kit.Shadow { Radius = 14, Blur = 22, Spread = -8, OffsetY = 9, Layers = 7, Color = Kit.K.ColorOf("ShadowC"), IsHitTestVisible = false });
        bar.Children.Add(new Border
        {
            Child = row, Padding = new Thickness(16, 11, 16, 11), CornerRadius = new CornerRadius(14),
            Background = Ui.Live("FlyoutFill"), BorderBrush = Ui.Live("CardRim"), BorderThickness = new Thickness(1),
        });
        Content = bar;
        _bar = bar;
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - ActualWidth) / 2;
            Top = area.Top + 6;
            if (!Motion.Enabled) return;
            // the bar drops in from above with the spring; the dot breathes while something is being learned or repeated
            var move = new TranslateTransform(0, -36);
            var scale = new ScaleTransform(0.94, 0.94);
            bar.RenderTransformOrigin = new Point(0.5, 0.5);
            bar.RenderTransform = new TransformGroup { Children = { scale, move } };
            Kit.Fx.Anim(move, TranslateTransform.YProperty, 0, null, 700);
            Kit.Fx.Anim(scale, ScaleTransform.ScaleXProperty, 1);
            Kit.Fx.Anim(scale, ScaleTransform.ScaleYProperty, 1);
            bar.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop });
            dot.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, 0.3, TimeSpan.FromMilliseconds(800))
            {
                AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                EasingFunction = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut },
            });
        };
        Closed += (_, _) => dot.BeginAnimation(OpacityProperty, null);
        SizeChanged += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - ActualWidth) / 2;
        };
    }

    public void SetText(string text) => _text.Text = text;

    private FrameworkElement? _bar;
    private bool _leaving;

    /// <summary>Closes the bar the way the app's other flyouts leave: it lifts away and fades first (instant when motion is off).</summary>
    public new void Close()
    {
        if (_leaving || _bar is null || !IsVisible) { base.Close(); return; }
        _leaving = true;
        Motion.Dismiss(_bar, base.Close, 0.15, -24);
    }

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
