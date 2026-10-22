using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Shabpa.Core;

namespace Shabpa.Kit;

/// <summary>Gives the corner owl a life of his own. The brain (Core/BidarBrain.cs) decides, the path maker (Core/BidarFlight.cs) plans,
/// the rig (BidarOwl) draws; this class only joins them.
/// Cost model: ONE <see cref="DispatcherTimer"/> that fires every 4 to 15 seconds (and once more for each pose that is held), and
/// <see cref="CompositionTarget.Rendering"/> hooked only while a flight is in the air. A settled owl costs nothing.
/// The timer stops when the owl is hidden or unloaded and everything is unsubscribed then.</summary>
public sealed class BidarLife : IDisposable
{
    private readonly BidarOwl _owl;
    private readonly Canvas _stage;
    private readonly Func<BrainContext> _host;
    private readonly Func<IReadOnlyList<Box>> _scan;
    private readonly Func<BidarCue?>? _poll;
    private readonly Func<bool> _rtl;
    private readonly Func<DateTime> _clock;
    private readonly BidarBrain _brain;
    private readonly Random _rng;
    private readonly DispatcherTimer _timer = new();
    private readonly TranslateTransform _move = new();
    private readonly Stopwatch _sw = new();

    private bool _running, _hooked, _maintenance, _atHome = true, _looking, _asleep;
    private DateTime _lastActivity, _busyUntil, _restoreAt = DateTime.MinValue;
    private long _lastCursorTick;
    private IReadOnlyList<Box> _noGo = [];

    public BidarLife(BidarOwl owl, Canvas stage, Func<BrainContext> host, Func<IReadOnlyList<Box>> scan, Func<bool> rtl,
        Func<BidarCue?>? poll = null, int? seed = null, Func<DateTime>? clock = null)
    {
        _owl = owl;
        _stage = stage;
        _host = host;
        _scan = scan;
        _rtl = rtl;
        _poll = poll;
        _clock = clock ?? (() => DateTime.Now);
        var s = seed ?? Environment.TickCount;
        _brain = new BidarBrain(s);
        _rng = new Random(s ^ 0x5bd1e995);
        _timer.Tick += (_, _) => Tick();
        _owl.RenderTransform = _move;
        _lastActivity = _clock();
    }

    // ---- state the tests and the host can read ----------------------------------------------------------------------------
    public bool Running => _running;
    public bool Flying { get; private set; }
    public bool Asleep => _asleep;
    public BidarAct? CurrentAct { get; private set; }
    public bool RenderingHooked => _hooked;
    public bool TimerEnabled => _timer.IsEnabled;
    public FlightPlan? Plan { get; private set; }
    public Pt Rest { get; private set; }
    /// <summary>Where the owl is drawn right now (resting place plus the flight offset).</summary>
    public Pt Position => new(Rest.X + _move.X, Rest.Y + _move.Y);
    public BidarBrain Brain => _brain;
    public event Action? Landed;

    private double W => _owl.Width;
    private double H => _owl.Height;
    private Box Area => new(0, 0, _stage.ActualWidth, _stage.ActualHeight);

    // ---- lifecycle --------------------------------------------------------------------------------------------------------------

    public void Start()
    {
        if (_running) return;
        _running = true;
        _stage.SizeChanged += OnSize;
        _owl.PreClick += OnPreClick;
        _owl.MouseEnter += OnEnter;
        _owl.IsVisibleChanged += OnVisible;
        _lastActivity = _clock();
        PlaceHome();
        if (_owl.IsVisible) Arm(_brain.NextGap());
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _timer.Stop();
        Unhook();
        _stage.SizeChanged -= OnSize;
        _owl.PreClick -= OnPreClick;
        _owl.MouseEnter -= OnEnter;
        _owl.IsVisibleChanged -= OnVisible;
        if (Flying) { Flying = false; Plan = null; SetRest(Rest); }
        _asleep = false;
        _restoreAt = DateTime.MinValue;
    }

    public void Dispose() => Stop();

    private void OnVisible(object? s, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue) { if (_running) Arm(_brain.NextGap()); }
        else
        {
            _timer.Stop();
            if (Flying) LandNow();
        }
    }

    private void Arm(TimeSpan span)
    {
        if (!_running) return;
        _timer.Stop();
        _timer.Interval = span < TimeSpan.FromMilliseconds(50) ? TimeSpan.FromMilliseconds(50) : span;
        _timer.Start();
    }

    // ---- placing -----------------------------------------------------------------------------------------------------------------

    private void SetRest(Pt p)
    {
        Rest = p;
        _move.X = _move.Y = 0;
        Canvas.SetLeft(_owl, p.X);
        Canvas.SetTop(_owl, p.Y);
    }

    private void PlaceHome()
    {
        var area = Area;
        if (area.W < W + 2 * Perches.SideMargin || area.H < H + 2 * Perches.BottomMargin) { SetRest(Perches.Clamp(new Pt(0, 0), area, W, H)); return; }
        SetRest(Perches.Home(area, W, H, _rtl()));
        _atHome = true;
    }

    private void OnSize(object? s, SizeChangedEventArgs e) => Resized();

    /// <summary>The stage changed size: a flight lands at once, then the owl re-homes (if he lives at home) or is pulled back inside.</summary>
    public void Resized()
    {
        if (Flying) LandNow();
        var area = Area;
        if (_atHome) { PlaceHome(); PageChanged(); return; }
        SetRest(Perches.Clamp(Rest, area, W, H));
        PageChanged(); // and check at a quiet moment that the new spot is still free
    }

    /// <summary>The page behind changed (a tab switch, a scroll): check at the next quiet moment that the owl still sits on free space.</summary>
    public void PageChanged()
    {
        if (_running && _owl.IsVisible && !Flying && _restoreAt == DateTime.MinValue && _clock() >= _busyUntil) { _maintenance = true; Arm(TimeSpan.FromMilliseconds(700)); }
    }

    // ---- the scheduler -----------------------------------------------------------------------------------------------------

    private BrainContext Context(DateTime now) => _host() with
    {
        Now = now,
        Busy = Flying || now < _busyUntil,
        Asleep = _asleep,
        IdleFor = now - _lastActivity,
    };

    /// <summary>One scheduler step. Public so a test can drive it without waiting for the timer.</summary>
    public void Tick()
    {
        _timer.Stop();
        if (!_running) return;
        var now = _clock();
        if (Flying) return; // the render clock ends the flight and re-arms
        if (_restoreAt != DateTime.MinValue)
        {
            if (now < _restoreAt) { Arm(_restoreAt - now); return; }
            Restore();
            Arm(_brain.NextGap());
            return;
        }
        if (_poll?.Invoke() is { } cue)
        {
            Cue(cue);
            if (!_timer.IsEnabled) Arm(_brain.NextGap());
            return;
        }
        var ctx = Context(now);
        var maintenance = _maintenance;
        _maintenance = false;
        var pause = BidarBrain.Pause(ctx);
        // a page change is checked even while the person is busy (their own click caused it); everything else waits
        if (pause != PauseReason.None && !(maintenance && pause is PauseReason.UserBusy or PauseReason.Inactive or PauseReason.Busy)) { Arm(_brain.NextGap()); return; }
        _noGo = _scan();
        var moved = Revalidate();
        if (moved || maintenance || pause != PauseReason.None) { if (!_timer.IsEnabled && !Flying) Arm(_brain.NextGap()); return; }
        var act = _brain.Pick(ctx with { CanFly = CanFly() });
        if (act is { } a) Play(a);
        if (!_timer.IsEnabled && !Flying) Arm(_brain.NextGap());
    }

    // ---- acts --------------------------------------------------------------------------------------------------------------------

    private void Hold(BidarMood mood, double ms)
    {
        _owl.SetMood(mood);
        HoldFor(ms);
    }

    private void HoldFor(double ms)
    {
        var now = _clock();
        _busyUntil = now.AddMilliseconds(ms);
        _restoreAt = _busyUntil;
        Arm(TimeSpan.FromMilliseconds(ms));
    }

    private void Busy(double ms) => _busyUntil = _clock().AddMilliseconds(ms);

    /// <summary>Plays one act now (the scheduler, the cues and the screenshot probe all come through here).</summary>
    public void Play(BidarAct act, double dx = 0, double dy = 0)
    {
        CurrentAct = act;
        switch (act)
        {
            case BidarAct.Breathe: _owl.Breathe(); Busy(3600); break;
            case BidarAct.Blink: _owl.DoBlink(); Busy(200); break;
            case BidarAct.DoubleBlink: _owl.DoubleBlink(); Busy(500); break;
            case BidarAct.LookAround: _owl.LookAround(); Busy(2600); break;
            case BidarAct.LanternFlicker: _owl.FlickerLantern(); Busy(1500); break;
            case BidarAct.Stretch: Hold(BidarMood.Stretch, 1500); break;
            case BidarAct.Yawn: Hold(BidarMood.Yawn, 1900); break;
            case BidarAct.WingWave: _owl.WaveWing(); Busy(1800); break;
            case BidarAct.Hop: _owl.Bounce(); Busy(650); break;
            case BidarAct.HatFidget: _owl.FidgetHat(); Busy(2200); break;
            case BidarAct.Hello: Hold(BidarMood.Hello, 2000); _owl.WaveWing(); break;
            case BidarAct.Think: Hold(BidarMood.Think, 1900); break;
            case BidarAct.Search: Hold(BidarMood.Search, 2400); _owl.LookAround(); break;
            case BidarAct.Shrug: Hold(BidarMood.Shrug, 1600); _owl.Bounce(); break;
            case BidarAct.Peek:
                _owl.PeekOut(Rest.X + W / 2 < Area.W / 2 ? -1 : 1);
                Busy(2600);
                break;
            case BidarAct.Doze:
                _asleep = true;
                _owl.SetMood(BidarMood.Sleepy);
                HoldFor(_brain.Between(20_000, 45_000));
                break;
            case BidarAct.Fly:
                if (!StartFlight()) { _owl.Bounce(); Busy(650); CurrentAct = BidarAct.Hop; }
                break;
            case BidarAct.Curious: _owl.Curious(dx, dy); Busy(1800); break;
            case BidarAct.Startle: _owl.SetMood(BidarMood.Surprised); _owl.Bounce(true); HoldFor(1400); break;
            case BidarAct.Celebrate: _owl.SetMood(BidarMood.Celebrate); _owl.Bounce(true); HoldFor(3400); break;
            case BidarAct.Worry: _owl.SetMood(BidarMood.Ping); _owl.Shiver(); HoldFor(4200); break;
            case BidarAct.Alert: _owl.SetMood(BidarMood.Alert); _owl.Bounce(); HoldFor(4800); break;
            case BidarAct.Dizzy: _owl.SetMood(BidarMood.Dizzy); _owl.Bounce(true); HoldFor(2800); break;
            case BidarAct.EasterEgg: _owl.SetMood(BidarMood.Love); _owl.SpinAround(); HoldFor(2600); break;
        }
    }

    /// <summary>Back to the calm face after a held pose or a nap.</summary>
    private void Restore()
    {
        _restoreAt = DateTime.MinValue;
        _busyUntil = DateTime.MinValue;
        var wasAsleep = _asleep;
        _asleep = false;
        CurrentAct = null;
        _owl.SetMood(BidarMood.Idle);
        if (wasAsleep && Motion.Enabled) _owl.Bounce();
    }

    /// <summary>Stops whatever he is doing (a flight lands first). Used before a click reaction or a cue.</summary>
    public void Interrupt()
    {
        if (Flying) LandNow();
        _restoreAt = DateTime.MinValue;
        _busyUntil = DateTime.MinValue;
        _asleep = false;
        CurrentAct = null;
        _owl.Settle();
    }

    /// <summary>Lands, calms down and returns to the home corner (the life switch was turned off).</summary>
    public void GoHome()
    {
        if (!_running) return;
        Interrupt();
        PlaceHome();
    }

    /// <summary>The person did something in the app (typed, clicked): he stays awake.</summary>
    public void NoteActivity() => _lastActivity = _clock();

    /// <summary>An app event. He reacts only if the brain lets him (not hidden, not in a game, not switched off, not twice in a row).</summary>
    public void Cue(BidarCue cue)
    {
        if (!_running) return;
        var now = _clock();
        if (_brain.OnCue(cue, Context(now)) is not { } act) return;
        Interrupt();
        Play(act);
    }

    private void OnPreClick()
    {
        var now = _clock();
        NoteActivity();
        _brain.RegisterClick(now);
        Interrupt();
    }

    /// <summary>Counts a click on another owl (the one in the speech bubble); true when it was the seventh gentle click in a row.</summary>
    public bool RegisterClick()
    {
        NoteActivity();
        return _brain.RegisterClick(_clock());
    }

    private void OnEnter(object? s, System.Windows.Input.MouseEventArgs e)
    {
        if (!_asleep || !_running || !Motion.Enabled) return;
        NoteActivity();
        Interrupt();
        Play(BidarAct.Startle); // woken by the pointer: a startled hop
    }

    /// <summary>The pointer moved (stage coordinates). He follows it with his eyes when near, and may lean over for a closer look.</summary>
    public void OnCursor(Point p)
    {
        if (!_running || Flying || _asleep) return;
        var tick = Environment.TickCount64;
        if (tick - _lastCursorTick < 40) return;
        _lastCursorTick = tick;
        var now = _clock();
        var ctx = Context(now);
        var pause = BidarBrain.Pause(ctx);
        if (pause is not (PauseReason.None or PauseReason.Busy)) return;
        var dx = p.X - (Rest.X + W / 2);
        var dy = p.Y - (Rest.Y + H / 2);
        var dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist > 280)
        {
            if (_looking) { _owl.LookAt(0, 0); _looking = false; }
            return;
        }
        _owl.LookAt(dx, dy);
        _looking = true;
        if (dist < 110 && _brain.OnCursorNear(ctx) is { } act) Play(act, dx, dy);
    }

    // ---- flying ------------------------------------------------------------------------------------------------------------------

    private bool CanFly()
    {
        _ready = FindFlight();
        return _ready is not null;
    }

    /// <summary>If the current resting place is no longer free (new page, scrolled text, other size) he flies to a free one.
    /// Returns true when he moved or started to.</summary>
    private bool Revalidate()
    {
        if (Flying) return false;
        var area = Area;
        if (area.W < 3 * W || area.H < 2 * H) return false;
        var rtl = _rtl();
        var home = Perches.Home(area, W, H, rtl);
        var target = Perches.Revalidate(Rest, area, W, H, rtl, _noGo);
        if (_atHome && target != Rest && Perches.IsClear(home, W, H, area, _noGo)) target = home;
        if (target == Rest) return false;
        if (Motion.Enabled && _owl.IsVisible && FlightPlanner.Plan(Rest, target, W, H, area, _noGo, _rng) is { } plan)
        {
            BeginFlight(plan);
            return true;
        }
        SetRest(target);
        _atHome = target == home;
        return true;
    }

    /// <summary>Takes off now toward a random free resting place; false when there is none (or the path is blocked).</summary>
    public bool FlyNow()
    {
        if (!_running || Flying) return false;
        _noGo = _scan();
        _ready = null;
        return StartFlight();
    }

    private FlightPlan? _ready;

    /// <summary>A flight to a random free place whose path is clear, or null (tries the free places in random order).</summary>
    private FlightPlan? FindFlight()
    {
        var area = Area;
        if (area.W < 3 * W || area.H < 2 * H) return null;
        var valid = Perches.Valid(area, W, H, _rtl(), _noGo);
        var far = valid.Where(p => Pt.Distance(p, Rest) >= 120).OrderBy(_ => _rng.Next()).ToList();
        // first a path that never crosses anything; on a crowded page, a high arc over the content (he only passes over it for a
        // second or two and lands on a free place, so nothing stays covered)
        foreach (var target in far)
            if (FlightPlanner.Plan(Rest, target, W, H, area, _noGo, _rng) is { } plan) return plan;
        foreach (var target in far)
            if (FlightPlanner.Plan(Rest, target, W, H, area, [], _rng) is { } plan) return plan;
        return null;
    }

    private bool StartFlight()
    {
        var plan = _ready ?? FindFlight();
        _ready = null;
        if (plan is null) return false;
        BeginFlight(plan);
        return true;
    }

    private void BeginFlight(FlightPlan plan)
    {
        _owl.Settle();
        _owl.SetMood(BidarMood.Fly);
        Plan = plan;
        Flying = true;
        CurrentAct = BidarAct.Fly;
        _seek = 0;
        _sw.Restart();
        Hook();
    }

    private void Hook()
    {
        if (_hooked) return;
        CompositionTarget.Rendering += OnRender;
        _hooked = true;
    }

    private void Unhook()
    {
        if (!_hooked) return;
        CompositionTarget.Rendering -= OnRender;
        _hooked = false;
    }

    private double _seek;

    private void OnRender(object? sender, EventArgs e) => StepFlight(_seek + _sw.Elapsed.TotalSeconds);

    /// <summary>Jumps the running flight clock to <paramref name="elapsed"/> seconds (the screenshot probe holds a mid-air frame this way).</summary>
    public void Seek(double elapsed)
    {
        _seek = elapsed;
        _sw.Restart();
        StepFlight(elapsed);
    }

    /// <summary>Moves the owl to where the flight is <paramref name="elapsed"/> seconds in. Public so a test can step it by hand.</summary>
    public void StepFlight(double elapsed)
    {
        if (!Flying || Plan is not { } plan) return;
        var frame = FlightMotion.Frame(plan, elapsed);
        _move.X = frame.Pos.X - Rest.X;
        _move.Y = frame.Pos.Y - Rest.Y;
        _owl.ApplyFlight(frame);
        if (frame.Done) EndFlight();
    }

    /// <summary>Jumps to the end of the flight at once (a click, a resize, hiding): he lands first, then the rest happens.</summary>
    public void LandNow()
    {
        if (!Flying || Plan is not { } plan) return;
        StepFlight(FlightMotion.Total(plan));
    }

    private void EndFlight()
    {
        Unhook();
        var plan = Plan!;
        Flying = false;
        Plan = null;
        SetRest(plan.To);
        _atHome = plan.To == Perches.Home(Area, W, H, _rtl());
        CurrentAct = null;
        _owl.Settle();
        Landed?.Invoke();
        if (_running && _owl.IsVisible) Arm(_brain.NextGap());
    }
}
