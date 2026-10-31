using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa;

/// <summary>«مدال جدید!»: a small card that springs up above the taskbar when a medal is earned, even when the main window is in
/// the tray. It never takes focus, stays about six seconds, and a click dismisses it. Several medals queue one after another.</summary>
public sealed class MedalToast : Window
{
    private static readonly Queue<MedalDef> Queue = new();
    private static MedalToast? _open;

    public static void Show(MedalDef medal)
    {
        Queue.Enqueue(medal);
        if (_open is null) Next();
    }

    private static void Next()
    {
        if (Queue.Count == 0) return;
        var toast = new MedalToast(Queue.Dequeue());
        _open = toast;
        toast.Closed += (_, _) => { _open = null; Next(); };
        toast.Show();
    }

    /// <summary>Test aid (--toast-test): draws the card as it looks once it has settled, over a dark desktop-like backdrop, into a PNG.</summary>
    public static void RenderPreview(MedalDef medal, string path)
    {
        var toast = new MedalToast(medal);
        var root = (Grid)toast.Content;
        foreach (var child in root.Children.OfType<Border>()) { child.Opacity = 1; child.RenderTransform = Transform.Identity; }
        var holder = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x14, 0x1E, 0x3C)), FlowDirection = Ui.Flow };
        toast.Content = null;
        holder.Children.Add(root);
        holder.Measure(new Size(480, 200));
        holder.Arrange(new Rect(0, 0, holder.DesiredSize.Width, holder.DesiredSize.Height));
        holder.UpdateLayout();
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)holder.DesiredSize.Width * 2, (int)holder.DesiredSize.Height * 2, 192, 192, PixelFormats.Pbgra32);
        bmp.Render(holder);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using var fs = System.IO.File.Create(path);
        enc.Save(fs);
    }

    private readonly Border _card;
    private readonly TranslateTransform _move = new(0, 28);
    private readonly ScaleTransform _scale = new(0.94, 0.94);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(6) };
    private bool _leaving;

    public MedalToast(MedalDef medal)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        FlowDirection = Ui.Flow;
        UseLayoutRounding = true;
        FontFamily = (FontFamily)Application.Current.Resources["Ui"];
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

        var grid = new Grid { Margin = new Thickness(12, 9, 14, 9) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 150, MaxWidth = 200 });

        var glyph = new MedalGlyph(medal, true, 50) { Margin = new Thickness(0, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(glyph);

        var text = new StackPanel { Margin = new Thickness(11, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = L.T("مدال جدید!", "New medal!"), FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = K.Brush("Gold") });
        text.Children.Add(new TextBlock { Text = medal.Name, FontSize = 14.5, FontWeight = FontWeights.Bold, Foreground = K.Brush("Text"), Margin = new Thickness(0, 0, 0, 1), TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = medal.Desc, FontSize = 11, Foreground = K.Brush("Muted"), TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock
        {
            Text = $"+{Fa.N(medal.Reward)} {L.T("پَر", "feathers")}", FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = K.Brush("Accent"), Margin = new Thickness(0, 3, 0, 0),
        });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        _card = new Border
        {
            Background = K.Brush("FlyoutFill"),
            BorderBrush = K.Brush("Gold"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(15),
            Child = grid,
            Margin = new Thickness(10, 10, 10, 4),
            Opacity = 0,
            RenderTransformOrigin = new Point(0.5, 1),
            RenderTransform = new TransformGroup { Children = { _scale, _move } },
            Cursor = Cursors.Hand,
        };
        // the glass fill is translucent; under it a solid plate keeps the text readable on any wallpaper
        var plate = new Border { Background = K.Brush("Panel3"), CornerRadius = new CornerRadius(15), Margin = new Thickness(10, 10, 10, 4), Opacity = 0 };
        var root = new Grid();
        root.Children.Add(plate);
        root.Children.Add(_card);
        Content = root;
        MouseLeftButtonUp += (_, _) => Leave();
        _timer.Tick += (_, _) => Leave();
        Loaded += (_, _) => { Place(); Enter(plate); };
        SizeChanged += (_, _) => Place();
    }

    private void Place()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 2;
        Top = area.Bottom - ActualHeight + 2;
    }

    private void Enter(Border plate)
    {
        _timer.Start();
        if (!Motion.Enabled)
        {
            _move.Y = 0; _scale.ScaleX = _scale.ScaleY = 1; _card.Opacity = 1; plate.Opacity = 1;
            return;
        }
        var ease = SpringEase.I;
        var d = TimeSpan.FromMilliseconds(650);
        _card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        plate.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        _move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(28, 0, d) { EasingFunction = ease });
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.94, 1, d) { EasingFunction = ease });
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.94, 1, d) { EasingFunction = ease });
        UiSound.Play(UiSound.Kind.Notice);
    }

    private void Leave()
    {
        if (_leaving) return;
        _leaving = true;
        _timer.Stop();
        if (!Motion.Enabled) { Close(); return; }
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
        _move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 18, TimeSpan.FromMilliseconds(220)));
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW: the card never takes focus and stays out of Alt+Tab
        var style = GetWindowLongPtr(handle, -20).ToInt64() | 0x08000000 | 0x80;
        SetWindowLongPtr(handle, -20, new IntPtr(style));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}
