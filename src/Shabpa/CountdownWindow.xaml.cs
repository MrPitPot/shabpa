using System.Media;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Shabpa.Core;

namespace Shabpa;

public enum CountdownOutcome { Cancel, Run, Later }

/// <summary>The last chance to stop a power action: always on top, cancels by default.</summary>
public partial class CountdownWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DateTime _deadline;
    private readonly double _total;
    private bool _finished;

    public CountdownOutcome Outcome { get; private set; } = CountdownOutcome.Cancel;

    public CountdownWindow(PowerAction action, string reason, int seconds)
    {
        InitializeComponent();
        FlowDirection = Ui.Flow;
        _total = Math.Max(1, seconds);
        _deadline = DateTime.UtcNow.AddSeconds(_total);
        ReasonText.Text = reason;
        var sentence = Fa.ActionSentence(action);
        TitleText.Text = L.En ? char.ToUpperInvariant(sentence[0]) + sentence[1..] : sentence;
        _timer.Tick += (_, _) => Update();
        Closed += (_, _) => _timer.Stop();
        Loaded += (_, _) =>
        {
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
        SecondsText.Text = Fa.Digits(((int)Math.Ceiling(left)).ToString());
        Arc.Data = ArcGeometry(left / _total);
    }

    /// <summary>A clockwise arc from 12 o'clock covering <paramref name="fraction"/> of the ring.</summary>
    private static Geometry ArcGeometry(double fraction)
    {
        const double size = 150, r = size / 2 - 4, c = size / 2;
        fraction = Math.Clamp(fraction, 0.0001, 0.9999);
        var angle = fraction * 2 * Math.PI;
        var end = new Point(c + r * Math.Sin(angle), c - r * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = new Point(c, c - r) };
        figure.Segments.Add(new ArcSegment(end, new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        return new PathGeometry([figure]);
    }

    public void Finish(CountdownOutcome outcome)
    {
        if (_finished) return;
        _finished = true;
        _timer.Stop();
        Outcome = outcome;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Finish(CountdownOutcome.Cancel);
    private void OnLater(object sender, RoutedEventArgs e) => Finish(CountdownOutcome.Later);
    private void OnNow(object sender, RoutedEventArgs e) => Finish(CountdownOutcome.Run);

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
