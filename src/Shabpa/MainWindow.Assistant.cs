using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa;

// ---- Bidar in the main window: a small owl docked in the corner of every page; a click opens the speech bubble with the command box ----
public partial class MainWindow
{
    private BidarOwl? _dock, _big;
    private Grid? _popup;
    private AssistantPanel? _assistant;
    private ScaleTransform? _popupScale;
    private bool _popupOpen;

    private void InitAssistantDock()
    {
        _dock = new BidarOwl { Width = 50, Height = 60, ToolTip = L.T("فرمان متنی (Bidar)", "Type a command (Bidar)"), Focusable = true, FocusVisualStyle = null };
        AutomationProperties.SetName(_dock, L.T("باز کردن فرمان متنی", "Open the command box"));
        _dock.Clicked += () => Dispatcher.BeginInvoke(OpenAssistant);
        _dock.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Space)) return;
            e.Handled = true;
            OpenAssistant();
        };
        _dock.MouseMove += (_, e) =>
        {
            var p = e.GetPosition(_dock);
            _dock.LookAt(p.X - _dock.ActualWidth / 2, p.Y - _dock.ActualHeight / 2);
        };
        _dock.MouseLeave += (_, _) => _dock.LookAt(0, 0);
        // the owl's own corner: the end side of the page (the left in Persian), clear of the scroll bar
        _dock.HorizontalAlignment = HorizontalAlignment.Right;
        _dock.VerticalAlignment = VerticalAlignment.Bottom;
        _dock.Margin = new Thickness(0, 0, 26, 12);
        BidarLayer.Children.Add(_dock);
        Hub.I.AssistantDockChanged += OnDockSetting;
        Closed += (_, _) => Hub.I.AssistantDockChanged -= OnDockSetting;
        OnDockSetting();
    }

    private void OnDockSetting()
    {
        var show = Hub.I.Settings.ShowAssistantDock;
        if (_dock is not null) _dock.Visibility = show && !_popupOpen ? Visibility.Visible : Visibility.Collapsed;
        if (!show && _popupOpen) CloseAssistant();
    }

    private void BuildPopup()
    {
        _big = new BidarOwl { Width = 92, Height = 110, IdleLife = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, -2, 24, 0) };
        _assistant = new AssistantPanel();
        _assistant.Mood += m => _big.SetMood(m);
        _assistant.Spoke += () => _big.Talk();
        _assistant.Activity += _big.Touch;

        var title = new TextBlock { Text = L.T("چی کار کنم؟", "What should I do?"), FontSize = 16, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        var closeIcon = new Kit.Icon { Glyph = "x", Width = 14, Height = 14, Weight = 1.6 };
        closeIcon.SetResourceReference(Kit.Icon.ForegroundProperty, "Muted");
        var close = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = closeIcon, ToolTip = L.T("بستن (Esc)", "Close (Esc)") };
        close.MouseLeftButtonUp += (_, _) => CloseAssistant();
        Fx.SetPress(close, 0.88);
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        DockPanel.SetDock(close, Dock.Right);
        top.Children.Add(close);
        top.Children.Add(title);
        var content = new StackPanel();
        content.Children.Add(top);
        content.Children.Add(_assistant);

        var bubble = new GlassCard { CornerRadius = new CornerRadius(20), Padding = new Thickness(18, 14, 18, 16), Child = content };
        bubble.SetResourceReference(Control.BackgroundProperty, "FlyoutFill");
        // the little tail that points down at the owl
        var tail = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M0,0 L16,0 L8,10 Z"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, -1, 54, 0), IsHitTestVisible = false,
        };
        tail.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Bg");
        var stack = new StackPanel { Width = 360 };
        // an opaque plate under the glass, so the page behind never shows through the text
        var plate = new Border { CornerRadius = new CornerRadius(20), Margin = new Thickness(1) };
        plate.SetResourceReference(Border.BackgroundProperty, "Bg");
        var bubbleHost = new Grid();
        bubbleHost.Children.Add(plate);
        bubbleHost.Children.Add(bubble);
        stack.Children.Add(bubbleHost);
        stack.Children.Add(tail);
        stack.Children.Add(_big);

        _popupScale = new ScaleTransform(1, 1);
        _popup = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 18, 6),
            RenderTransformOrigin = new Point(0.85, 1), RenderTransform = _popupScale, Visibility = Visibility.Collapsed,
        };
        _popup.Children.Add(stack);
        _popup.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            CloseAssistant();
        };
        PreviewMouseMove += (_, e) =>
        {
            if (!_popupOpen || _big is null) return;
            var p = e.GetPosition(_big);
            _big.LookAt(p.X - _big.ActualWidth / 2, p.Y - _big.ActualHeight / 2);
        };
        BidarLayer.Children.Add(_popup);
    }

    /// <summary>Shows Bidar and his speech bubble; the box is ready to type in.</summary>
    public void OpenAssistant()
    {
        if (_popupOpen) { _assistant?.FocusInput(); return; }
        if (_popup is null) BuildPopup();
        _popupOpen = true;
        _dock!.Visibility = Visibility.Collapsed;
        _popup!.Visibility = Visibility.Visible;
        _big!.SetMood(BidarMood.Happy);
        if (Motion.Enabled)
        {
            _popup.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)) { FillBehavior = FillBehavior.Stop });
            var spring = new DoubleAnimation(0.55, 1, TimeSpan.FromMilliseconds(620)) { EasingFunction = SpringEase.I, FillBehavior = FillBehavior.Stop };
            _popupScale!.BeginAnimation(ScaleTransform.ScaleXProperty, spring);
            _popupScale.BeginAnimation(ScaleTransform.ScaleYProperty, spring);
            _big.Bounce();
            _big.Talk(2);
        }
        _big.Touch();
        Dispatcher.BeginInvoke(() => _assistant?.FocusInput(), System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>Hides the bubble and brings the small owl back.</summary>
    public void CloseAssistant()
    {
        if (!_popupOpen || _popup is null) return;
        _popupOpen = false;
        void Done()
        {
            _popup.Visibility = Visibility.Collapsed;
            OnDockSetting();
        }
        if (!Motion.Enabled)
        {
            Done();
            return;
        }
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(140)) { FillBehavior = FillBehavior.Stop };
        fade.Completed += (_, _) => { if (!_popupOpen) Done(); };
        _popup.BeginAnimation(OpacityProperty, fade);
    }

    // ---- screenshot probe helpers ----
    internal AssistantPanel? AssistantPanelForProbe => _assistant;
    internal BidarOwl? BigOwlForProbe => _big;
}
