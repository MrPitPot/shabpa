using System.Media;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa;

public enum CountdownOutcome { Cancel, Run, Later }

/// <summary>The last chance to stop a power action: always on top, cancels by default.</summary>
public partial class CountdownWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DateTime _deadline;
    private readonly double _total;
    private bool _finished;
    private int _shown = -1;
    private bool _first = true;
    private readonly ScaleTransform _tick = new(1, 1);

    public CountdownOutcome Outcome { get; private set; } = CountdownOutcome.Cancel;

    public CountdownWindow(PowerAction action, string reason, int seconds)
    {
        InitializeComponent();
        FlowDirection = Ui.Flow;
        BadgeHost.Content = new IconBadge("alarm", "HueC", 52, 26, 16);
        _total = Math.Max(1, seconds);
        _deadline = DateTime.UtcNow.AddSeconds(_total);
        ReasonText.Text = reason;
        var sentence = Fa.ActionSentence(action);
        TitleText.Text = L.En ? char.ToUpperInvariant(sentence[0]) + sentence[1..] : sentence;
        _timer.Tick += (_, _) => Update();
        Closed += (_, _) => _timer.Stop();
        SecondsText.RenderTransform = _tick;
        Loaded += (_, _) =>
        {
            Enter();
            Update();
            _timer.Start();
            if (!Hub.I.DryRun) SystemSounds.Exclamation.Play();
        };
    }

    private void Update()
    {
        var left = (_deadline - DateTime.UtcNow).TotalSeconds;
        if (left <= 0)
        {
            Finish(CountdownOutcome.Run);
            return;
        }
        var whole = (int)Math.Ceiling(left);
        SecondsText.Text = Fa.Digits(whole.ToString());
        var fraction = Math.Clamp(left / _total, 0.0001, 0.9999);
        if (_first)
        {
            _first = false;
            Ring.AnimateTo(fraction, 900); // the ring sweeps in once, then follows the clock
        }
        else Ring.Fraction = fraction;
        if (whole != _shown)
        {
            _shown = whole;
            // every second the number pops and the last five seconds turn the ring red
            if (whole <= 5) Ring.ArcBrush = (System.Windows.Media.Brush)Application.Current.Resources["Red"];
            if (Motion.Enabled && whole < _total)
            {
                _tick.ScaleX = 1.14;
                _tick.ScaleY = 1.14;
                Fx.Anim(_tick, ScaleTransform.ScaleXProperty, 1.0);
                Fx.Anim(_tick, ScaleTransform.ScaleYProperty, 1.0);
            }
        }
    }

    /// <summary>The flyout springs up from 40 px below at 94% size while it fades in.</summary>
    private void Enter()
    {
        if (!Motion.Enabled) return;
        var scale = new ScaleTransform(0.94, 0.94);
        var move = new TranslateTransform(0, 40);
        Sheet.RenderTransform = new TransformGroup { Children = { scale, move } };
        Fx.Anim(scale, ScaleTransform.ScaleXProperty, 1);
        Fx.Anim(scale, ScaleTransform.ScaleYProperty, 1);
        Fx.Anim(move, TranslateTransform.YProperty, 0, null, 700);
        Sheet.Opacity = 0;
        Sheet.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)) { FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop });
        Sheet.Opacity = 1;
    }

    public void Finish(CountdownOutcome outcome)
    {
        if (_finished) return;
        _finished = true;
        _timer.Stop();
        Outcome = outcome;
        Motion.Dismiss(Sheet, Close); // sinks away in 150 ms, then the window closes (Hub reacts to Closed)
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Finish(CountdownOutcome.Cancel);
    private void OnLater(object sender, RoutedEventArgs e) => Finish(CountdownOutcome.Later);
    private void OnNow(object sender, RoutedEventArgs e) => Finish(CountdownOutcome.Run);

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
