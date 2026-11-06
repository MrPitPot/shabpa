using System.IO;
using Shabpa.Core;
using Xunit;

namespace Shabpa.Tests;

public class NativeStackTests
{
    [Fact]
    public void A_thread_waiting_in_windows_shows_the_libraries_it_is_stuck_in()
    {
        var release = new ManualResetEventSlim();
        var started = new ManualResetEventSlim();
        uint id = 0;
        var thread = new Thread(() => { id = NativeStack.CurrentThreadId(); started.Set(); release.Wait(); }) { IsBackground = true };
        thread.Start();
        Assert.True(started.Wait(5000));
        Thread.Sleep(200);                                  // it is inside the wait by now
        try
        {
            var lines = NativeStack.Capture(id);
            Assert.NotEmpty(lines);
            Assert.DoesNotContain(lines, l => l.StartsWith('('));
            Assert.Contains(lines, l => l.Contains("ntdll", StringComparison.OrdinalIgnoreCase) || l.Contains("KERNELBASE", StringComparison.OrdinalIgnoreCase));
            Assert.All(lines, l => Assert.Matches(@"^(\w[\w.\-]*\+0x[0-9A-F]+|0x[0-9A-F]+)$", l));
        }
        finally { release.Set(); thread.Join(); }
    }

    [Fact]
    public void The_paused_thread_runs_on_afterwards()
    {
        var stop = false;
        var ticks = 0L;
        uint id = 0;
        var started = new ManualResetEventSlim();
        var thread = new Thread(() => { id = NativeStack.CurrentThreadId(); started.Set(); while (!Volatile.Read(ref stop)) { Interlocked.Increment(ref ticks); Thread.Sleep(1); } }) { IsBackground = true };
        thread.Start();
        Assert.True(started.Wait(5000));
        NativeStack.Capture(id);
        var before = Interlocked.Read(ref ticks);
        Thread.Sleep(300);
        Assert.True(Interlocked.Read(ref ticks) > before, "the thread was left paused");
        Volatile.Write(ref stop, true);
        thread.Join();
    }

    [Fact]
    public void An_unknown_thread_is_explained_in_one_line()
    {
        var lines = NativeStack.Capture(0xFFFFFF0);
        Assert.Single(lines);
        Assert.StartsWith("(", lines[0]);
    }

    [Fact]
    public void The_stall_log_carries_the_native_stack_too()
    {
        var folder = Path.Combine(Path.GetTempPath(), "shabpa-native-" + Guid.NewGuid().ToString("N"));
        var oldFile = Log.File;
        Log.File = Path.Combine(folder, "shabpa.log");
        var oldReader = UiWatchdog.StackReader;
        var oldNative = UiWatchdog.NativeReader;
        var oldId = UiWatchdog.OsThreadId;
        UiWatchdog.StackReader = _ => ["Fake.Frame"];
        UiWatchdog.NativeReader = id => [$"fake_driver.dll+0x10 (thread {id})"];
        UiWatchdog.OsThreadId = 4242;
        try
        {
            UiWatchdog.WriteStackForTest(1, 3000);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            var text = "";
            while (DateTime.UtcNow < deadline && !text.Contains("fake_driver"))
            {
                Thread.Sleep(50);
                try { if (File.Exists(Log.File)) text = File.ReadAllText(Log.File); } catch (IOException) { }
            }
            Assert.Contains("native stack", text);
            Assert.Contains("fake_driver.dll+0x10 (thread 4242)", text);
        }
        finally
        {
            UiWatchdog.StackReader = oldReader;
            UiWatchdog.NativeReader = oldNative;
            UiWatchdog.OsThreadId = oldId;
            Log.File = oldFile;
            try { Directory.Delete(folder, true); } catch { }
        }
    }
}
