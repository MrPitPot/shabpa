namespace Shabpa.Core;

/// <summary>What a stall leaves behind without any stack: how long the runtime spent paused in garbage collections, how many
/// collections ran, how much CPU the process used while the window was silent, how full the memory is. A window thread that is
/// stuck in its own code uses CPU or waits and the collector stays quiet; a process-wide pause shows as garbage-collection
/// time close to the stall, or as no CPU at all. Numbers only; nothing personal.</summary>
public static class StallEvidence
{
    public readonly record struct Point(long GcPauseMs, int Gen0, int Gen1, int Gen2, long CpuMs, long At, int MemoryLoadPercent, long HeapMB);

    public static Point Take()
    {
        try
        {
            var info = GC.GetGCMemoryInfo(GCKind.Any);
            var load = info.TotalAvailableMemoryBytes > 0 ? (int)(info.MemoryLoadBytes * 100 / info.TotalAvailableMemoryBytes) : 0;
            return new Point((long)GC.GetTotalPauseDuration().TotalMilliseconds, GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2),
                (long)Environment.CpuUsage.TotalTime.TotalMilliseconds, Environment.TickCount64, load, GC.GetTotalMemory(false) >> 20);
        }
        catch { return default; }
    }

    /// <summary>One line: the verdict first, then the figures.</summary>
    public static string Describe(Point before, Point after, long stallMs, long watchdogLateMs, string lastStep)
    {
        var wall = Math.Max(1, after.At - before.At);
        var gcPause = Math.Max(0, after.GcPauseMs - before.GcPauseMs);
        var cpu = Math.Max(0, after.CpuMs - before.CpuMs);
        string verdict;
        if (watchdogLateMs >= 700) verdict = "whole process paused (the watchdog thread was late too)";
        else if (gcPause * 2 >= stallMs) verdict = "garbage collection";
        else if (cpu * 100 / wall >= 60) verdict = "busy: the process was computing";
        else verdict = "waiting: little CPU and no collection (blocked on I/O, a lock or a call that does not return)";
        return $"{verdict}; stall {stallMs} ms, gc pause +{gcPause} ms (gen0 +{after.Gen0 - before.Gen0}, gen1 +{after.Gen1 - before.Gen1}, gen2 +{after.Gen2 - before.Gen2}), "
               + $"cpu +{cpu} ms in {wall} ms, memory load {after.MemoryLoadPercent}%, heap {after.HeapMB} MB, watchdog late {watchdogLateMs} ms"
               + (lastStep.Length > 0 ? $", last step: {lastStep}" : "");
    }
}
