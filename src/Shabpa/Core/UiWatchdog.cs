using System.Windows.Threading;

namespace Shabpa.Core;

/// <summary>What the watchdog should do about one window answer that is overdue.</summary>
public enum StallAction { None, Began, Still, TakeStack }

/// <summary>The rules of the watchdog without any clock, thread or window, so they can be tested: feed it how long the
/// window has been silent, it says what to write down. A stall is logged as soon as it passes two seconds (not only when it is
/// over, because a window that never recovers is killed by the person long before it would ever "end"), the stack of the window
/// thread is taken once it passes three seconds, and that is allowed once per ten minutes at most.</summary>
public sealed class StallTracker
{
    public const long LogAfterMs = 2000;
    public const long StackAfterMs = 3000;
    public const long StillEveryMs = 30000;
    public static readonly TimeSpan StackEvery = TimeSpan.FromMinutes(10);

    private bool _began, _stackAsked;
    private long _lastStill;
    private DateTime _lastStack = DateTime.MinValue;

    /// <summary>True once the current stall has been announced (so its end is worth a line too).</summary>
    public bool Announced => _began;

    public IReadOnlyList<StallAction> Observe(long silentMs, DateTime now)
    {
        var actions = new List<StallAction>();
        if (silentMs >= LogAfterMs && !_began)
        {
            _began = true;
            _lastStill = silentMs;
            actions.Add(StallAction.Began);
        }
        else if (_began && silentMs - _lastStill >= StillEveryMs)
        {
            _lastStill = silentMs;
            actions.Add(StallAction.Still);
        }
        if (silentMs >= StackAfterMs && !_stackAsked)
        {
            _stackAsked = true;
            if (now - _lastStack >= StackEvery)
            {
                _lastStack = now;
                actions.Add(StallAction.TakeStack);
            }
        }
        return actions;
    }

    /// <summary>The window answered: the next stall starts from nothing (the ten-minute rule for stacks stays).</summary>
    public void Reset()
    {
        _began = false;
        _stackAsked = false;
        _lastStill = 0;
    }
}

/// <summary>Notices when the window stops answering and writes when and on which page to the log, so a freeze that happens once
/// a day can be traced afterwards. It measures from its own thread and changes nothing in the app. Past three seconds it also
/// writes where the window thread is stuck (method names only, see <see cref="StallStack"/>).</summary>
public static class UiWatchdog
{
    private static Thread? _thread;
    private static volatile bool _stop;

    /// <summary>Replaceable for tests: how the window thread's stack is read.</summary>
    internal static Func<int, IReadOnlyList<string>> StackReader { get; set; } = StallStack.Capture;
    /// <summary>Set to false to skip reading stacks (the stall lines are still written).</summary>
    public static bool CaptureStacks { get; set; } = true;

    /// <param name="page">Returns the name of the page that is open (for the log line).</param>
    public static void Start(Dispatcher dispatcher, Func<string> page)
    {
        if (_thread is not null) return;
        _stop = false;
        var uiThread = dispatcher.Thread.ManagedThreadId;
        _thread = new Thread(() => Run(dispatcher, page, uiThread, 500)) { IsBackground = true, Name = "ui-watchdog", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    internal static void Run(Dispatcher dispatcher, Func<string> page, int uiThread, int pauseMs)
    {
        var tracker = new StallTracker();
        var lastLogged = DateTime.MinValue;
        while (!_stop)
        {
            Thread.Sleep(pauseMs);
            var sent = Environment.TickCount64;
            using var answered = new ManualResetEventSlim();
            long at = 0;
            try
            {
                dispatcher.BeginInvoke(DispatcherPriority.Normal, () => { at = Environment.TickCount64; answered.Set(); });
            }
            catch
            {
                return; // the app is closing
            }
            // not "wait a minute and see": look every quarter second, so a stall is written down while it is still going on
            while (!answered.Wait(250))
            {
                if (_stop) return;
                var silent = Environment.TickCount64 - sent;
                foreach (var action in tracker.Observe(silent, DateTime.Now))
                {
                    switch (action)
                    {
                        case StallAction.Began:
                            Log.Write($"ui stall: window not answering for {silent} ms and counting (page: {Where(page)})");
                            break;
                        case StallAction.Still:
                            Log.Write($"ui stall: still not answering after {silent / 1000} s (page: {Where(page)})");
                            break;
                        case StallAction.TakeStack:
                            if (CaptureStacks) WriteStack(uiThread, silent);
                            break;
                    }
                }
            }
            var delay = at - sent;
            if (tracker.Announced)
                Log.Write($"ui stall: over after {delay} ms (page: {Where(page)})");
            else if (delay >= 700 && DateTime.Now - lastLogged > TimeSpan.FromSeconds(3))
            {
                lastLogged = DateTime.Now;
                Log.Write($"ui stall: {delay} ms (page: {Where(page)})");
            }
            tracker.Reset();
        }
    }

    private static string Where(Func<string> page)
    {
        try { return page(); }
        catch { return "?"; }
    }

    // On its own thread: reading a snapshot takes a moment, and the watchdog has to keep measuring meanwhile.
    private static void WriteStack(int uiThread, long silentMs)
    {
        var reader = StackReader;
        Log.Write($"ui stall: reading the window thread's stack ({silentMs} ms into the stall)");
        // A thread of its own, not Task.Run: when the thread pool is starved (the usual companion of a frozen window: sync-over-async,
        // blocked workers) a queued job would never start and no stack would ever be written.
        var thread = new Thread(() =>
        {
            try
            {
                var lines = reader(uiThread);
                Log.Write($"ui stall stack (window thread, {silentMs} ms into the stall, top {lines.Count}):" + Environment.NewLine
                          + string.Join(Environment.NewLine, lines.Select(l => "    at " + l)));
            }
            catch (Exception ex)
            {
                Log.Write($"ui stall stack failed: {ex.GetType().Name}");
            }
        }) { IsBackground = true, Name = "ui-stall-stack" };
        thread.Start();
    }

    internal static void WriteStackForTest(int uiThread, long silentMs) => WriteStack(uiThread, silentMs);

    public static void Stop() => _stop = true;
}
