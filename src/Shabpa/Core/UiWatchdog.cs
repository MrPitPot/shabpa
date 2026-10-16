using System.Windows.Threading;

namespace Shabpa.Core;

/// <summary>Notices when the window stops answering for a moment ("hang") and writes when and on which page to the log, so a
/// freeze that happens once a day can be traced afterwards. It only measures; it changes nothing.</summary>
public static class UiWatchdog
{
    private static Thread? _thread;
    private static volatile bool _stop;

    /// <param name="page">Returns the name of the page that is open (for the log line).</param>
    public static void Start(Dispatcher dispatcher, Func<string> page)
    {
        if (_thread is not null) return;
        _stop = false;
        _thread = new Thread(() =>
        {
            var lastLogged = DateTime.MinValue;
            while (!_stop)
            {
                Thread.Sleep(500);
                var sent = Environment.TickCount64;
                var answered = new ManualResetEventSlim();
                long at = 0;
                try
                {
                    dispatcher.BeginInvoke(DispatcherPriority.Normal, () => { at = Environment.TickCount64; answered.Set(); });
                }
                catch
                {
                    return; // the app is closing
                }
                // wait up to a minute; a stall is the time until the window answers
                if (!answered.Wait(TimeSpan.FromSeconds(60))) { Log.Write("ui stall: no answer for 60 s"); continue; }
                var delay = at - sent;
                if (delay >= 700 && DateTime.Now - lastLogged > TimeSpan.FromSeconds(3))
                {
                    lastLogged = DateTime.Now;
                    string where;
                    try { where = page(); } catch { where = "?"; }
                    Log.Write($"ui stall: {delay} ms (page: {where})");
                }
            }
        }) { IsBackground = true, Name = "ui-watchdog", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    public static void Stop() => _stop = true;
}
