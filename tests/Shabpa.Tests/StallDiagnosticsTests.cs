using System.IO;
using System.Windows.Threading;
using Xunit;

namespace Shabpa.Tests;

public class StallEvidenceTests
{
    private static StallEvidence.Point P(long gc = 0, long cpu = 0, long at = 0, int gen2 = 0) => new(gc, 0, 0, gen2, cpu, at, 40, 120);

    [Fact]
    public void A_stall_made_of_garbage_collection_says_so()
    {
        var text = StallEvidence.Describe(P(gc: 100, cpu: 0, at: 1000), P(gc: 3300, cpu: 200, at: 4000, gen2: 2), 3000, 0, "page overlay 2 s ago");
        Assert.StartsWith("garbage collection", text);
        Assert.Contains("gc pause +3200 ms", text);
        Assert.Contains("gen2 +2", text);
        Assert.Contains("last step: page overlay 2 s ago", text);
    }

    [Fact]
    public void A_busy_window_a_waiting_window_and_a_paused_process_are_told_apart()
    {
        Assert.StartsWith("busy", StallEvidence.Describe(P(at: 0), P(cpu: 2700, at: 3000), 3000, 0, ""));
        Assert.StartsWith("waiting", StallEvidence.Describe(P(at: 0), P(cpu: 50, at: 3000), 3000, 0, ""));
        // the watchdog woke up late itself: nothing could have looked at the window thread, whatever the other figures say
        Assert.StartsWith("whole process paused", StallEvidence.Describe(P(at: 0), P(cpu: 50, at: 3000), 3000, 2500, ""));
    }

    [Fact]
    public void Real_figures_can_be_taken_and_the_clock_moves_forward()
    {
        var a = StallEvidence.Take();
        GC.Collect();
        Thread.Sleep(20);
        var b = StallEvidence.Take();
        Assert.True(b.At > a.At);
        Assert.True(b.Gen0 > a.Gen0);
        Assert.True(b.GcPauseMs >= a.GcPauseMs);
        Assert.InRange(b.MemoryLoadPercent, 1, 100);
    }
}

[Collection("log")]
public class StallStackRealTests
{
    [Fact]
    public void The_real_snapshot_reader_finds_the_method_a_thread_is_stuck_in_without_administrator_rights()
    {
        var inside = new ManualResetEventSlim();
        var id = 0;
        var stuck = new Thread(() =>
        {
            id = Environment.CurrentManagedThreadId;
            inside.Set();
            StallTestSpot.Block(TimeSpan.FromSeconds(6));
        }) { IsBackground = true };
        stuck.Start();
        Assert.True(inside.Wait(3000));
        Thread.Sleep(200);
        var lines = StallStack.Capture(id);
        Assert.True(lines.Any(l => l.Contains("StallTestSpot.Block")), "stack was: " + string.Join(" | ", lines));
    }

    [Fact]
    public void A_stack_that_cannot_be_read_leaves_one_line_with_the_reason()
    {
        var file = Path.Combine(Path.GetTempPath(), $"shabpa-nostack-{Guid.NewGuid():N}.log");
        var oldFile = Log.File;
        var oldReader = UiWatchdog.StackReader;
        Log.File = file;
        UiWatchdog.StackReader = _ => ["(stack not available: UnauthorizedAccessException: Access is denied)"];
        try
        {
            UiWatchdog.WriteStackForTest(1, 3100);
            var text = "";
            for (var i = 0; i < 100 && !text.Contains("unavailable"); i++)
            {
                Thread.Sleep(50);
                try { text = File.ReadAllText(file); } catch (IOException) { }
            }
            Assert.Contains("ui stall stack unavailable: stack not available: UnauthorizedAccessException", text);
            Assert.DoesNotContain("\n    at ", text);
        }
        finally { UiWatchdog.StackReader = oldReader; Log.File = oldFile; try { File.Delete(file); } catch { /* temp */ } }
    }

    [Fact]
    public void A_stall_leaves_an_evidence_line_with_the_cause_class_and_the_last_step()
    {
        var file = Path.Combine(Path.GetTempPath(), $"shabpa-evidence-{Guid.NewGuid():N}.log");
        var oldFile = Log.File;
        Log.File = file;
        Dispatcher? dispatcher = null;
        var ready = new ManualResetEventSlim();
        var ui = new Thread(() => { dispatcher = Dispatcher.CurrentDispatcher; ready.Set(); Dispatcher.Run(); }) { IsBackground = true };
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
        Assert.True(ready.Wait(5000));
        var uiId = 0;
        dispatcher!.Invoke(() => uiId = Environment.CurrentManagedThreadId);
        var oldReader = UiWatchdog.StackReader;
        UiWatchdog.StackReader = _ => ["Fake.Frame"];
        try
        {
            var folder = Path.Combine(Path.GetTempPath(), "shabpa-ev-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            RunMarker.Begin(folder, "9.9.9", () => "OverlayPage", startTimer: false);
            RunMarker.Note("page gaming");
            UiWatchdog.Rearm();
            var watchdog = new Thread(() => UiWatchdog.Run(dispatcher!, () => "OverlayPage", uiId, 20)) { IsBackground = true };
            watchdog.Start();
            Thread.Sleep(300);
            dispatcher!.BeginInvoke(() => Thread.Sleep(1500));
            Thread.Sleep(2500);
            UiWatchdog.Stop();
            watchdog.Join(3000);
            RunMarker.End();
            var text = File.ReadAllText(file);
            var line = text.Split('\n').First(l => l.Contains("ui stall evidence:"));
            Assert.Contains("waiting", line);          // a sleeping thread: no CPU, no collection
            Assert.Contains("last step: page gaming", line);
            try { Directory.Delete(folder, true); } catch { /* temp */ }
        }
        finally
        {
            UiWatchdog.StackReader = oldReader;
            Log.File = oldFile;
            dispatcher!.InvokeShutdown();
            try { File.Delete(file); } catch { /* temp */ }
        }
    }
}
