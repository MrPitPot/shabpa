namespace Shabpa.Core;

/// <summary>Thrown instead of starting yet another nested wait; callers treat it like a cancelled call (keep the old value, try later).</summary>
public sealed class OffloadBusyException : OperationCanceledException
{
    public OffloadBusyException() : base("another slow call is still running on the window thread") { }
}

public static class Offload
{
    /// <summary>Runs a slow call (a console program, a driver) on the thread pool while the calling window thread keeps
    /// handling messages, so the window keeps painting and answering instead of showing "not responding". Returns when the
    /// work is done. Off the window's thread it just runs the work in place.</summary>
    public static T KeepResponsive<T>(Func<T> work) => KeepResponsive(work, System.Windows.Application.Current?.Dispatcher);

    internal static T KeepResponsive<T>(Func<T> work, System.Windows.Threading.Dispatcher? dispatcher)
    {
        if (dispatcher is null || !dispatcher.CheckAccess()) return work();
        // Each call pumps messages on top of the caller's stack. Something that asks again while the first call is still running
        // (a refresh fired by the one-second clock, a second click) must not pile frames up until the stack is gone.
        if (_depth >= MaxDepth) throw new OffloadBusyException();
        _depth++;
        try
        {
            return Pump(work, dispatcher);
        }
        finally
        {
            _depth--;
        }
    }

    public const int MaxDepth = 3;
    [ThreadStatic] private static int _depth;

    private static T Pump<T>(Func<T> work, System.Windows.Threading.Dispatcher dispatcher)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        T result = default!;
        Exception? error = null;
        var done = new ManualResetEventSlim(false);
        _ = Task.Run(() =>
        {
            try { result = work(); }
            catch (Exception ex) { error = ex; }
            finally { done.Set(); frame.Continue = false; }
        });
        if (frame.Continue) System.Windows.Threading.Dispatcher.PushFrame(frame);
        // PushFrame also returns when the dispatcher shuts down mid-work: never hand back the default value as if it were a
        // finished result. Wait a bounded time for the work, else say so with an exception.
        if (!done.IsSet && !done.Wait(ShutdownWaitMs)) throw new OperationCanceledException("the work was still running when the window closed");
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        return result;
    }

    internal static int ShutdownWaitMs = 10000;

    /// <summary>Runs <paramref name="work"/> on the thread pool and waits at most <paramref name="milliseconds"/> for it.
    /// When it is late the caller gets <paramref name="fallback"/> at once and the work keeps running (and is ignored): a hung
    /// driver call then costs a pool thread, not the window.</summary>
    public static T Bounded<T>(Func<T> work, T fallback, int milliseconds)
    {
        try
        {
            var task = Task.Run(work);
            return task.Wait(milliseconds) ? task.Result : fallback;
        }
        catch
        {
            return fallback;
        }
    }
}

/// <summary>A value that is expensive or can block (a console tool, a driver call, COM audio): callers always get the last
/// known value at once, and a refresh runs on the thread pool, one at a time, never on the caller's thread. A refresh that
/// hangs can only hold up its own pool thread; it never reaches the window.</summary>
public sealed class Cached<T>
{
    private readonly Func<T> _read;
    private readonly TimeSpan _ttl;
    private readonly object _gate = new();
    private T _value;
    private long _at = long.MinValue;
    private int _running;

    public Cached(Func<T> read, TimeSpan ttl, T initial = default!)
    {
        _read = read;
        _ttl = ttl;
        _value = initial;
    }

    /// <summary>True once a read has finished at least once.</summary>
    public bool HasValue => _has;
    private volatile bool _has;

    /// <summary>The last value read (the initial one before the first read finishes). Starts a refresh when it is older than
    /// the time-to-live; <paramref name="changed"/> runs on the pool thread when a refresh brought a different value.</summary>
    public T Get(Action<T>? changed = null)
    {
        var at = Volatile.Read(ref _at);
        if (at == long.MinValue || Environment.TickCount64 - at >= _ttl.TotalMilliseconds) Refresh(changed);
        lock (_gate) return _value;
    }

    /// <summary>Forget the age so the next <see cref="Get"/> reads again (after a change that makes the value stale).</summary>
    public void Invalidate() => Volatile.Write(ref _at, long.MinValue);

    /// <summary>Replace the value by hand (the caller just changed the thing it describes).</summary>
    public void Set(T value)
    {
        lock (_gate) _value = value;
        _has = true;
        Volatile.Write(ref _at, Environment.TickCount64);
    }

    private void Refresh(Action<T>? changed)
    {
        if (Interlocked.Exchange(ref _running, 1) == 1) return;
        _ = Task.Run(() =>
        {
            try
            {
                var value = _read();
                bool different;
                lock (_gate)
                {
                    different = !HasValue || !EqualityComparer<T>.Default.Equals(_value, value);
                    _value = value;
                }
                _has = true;
                Volatile.Write(ref _at, Environment.TickCount64);
                if (different) changed?.Invoke(value);
            }
            catch (Exception ex)
            {
                Log.Write($"background read failed: {ex.GetType().Name}: {ex.Message}");
                Volatile.Write(ref _at, Environment.TickCount64); // do not hammer a failing read
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }
        });
    }
}
