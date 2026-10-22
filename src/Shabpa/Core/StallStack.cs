using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.Diagnostics.Runtime;

namespace Shabpa.Core;

/// <summary>Finds out where the window thread is stuck while it is stuck: a short list of method names (and the name of the
/// native library it sits in), nothing else. It looks at a copy-on-write snapshot of this very process through Microsoft's
/// ClrMD, reads only the stack of the one thread, and never reads or keeps memory contents, text, paths or settings.
/// Slow and heavy compared to a log line, so it is meant for one stall now and then, and it can be switched off by the caller.</summary>
public static class StallStack
{
    public const int MaxLines = 25;

    /// <summary>Method names of the given managed thread, top first, or an explanation line when it could not be read.</summary>
    public static IReadOnlyList<string> Capture(int managedThreadId)
    {
        try { return Read(managedThreadId); }
        catch (Exception ex) { return [$"(stack not available: {ex.GetType().Name}: {Clean(ex.Message)})"]; }
    }

    // a separate method so the ClrMD assembly is only loaded when a stall really happens
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<string> Read(int managedThreadId)
    {
        var lines = new List<string>();
        using var target = DataTarget.CreateSnapshotAndAttach(Environment.ProcessId);
        var info = target.ClrVersions.FirstOrDefault();
        if (info is null) return ["(no .NET runtime found in the snapshot)"];
        using var runtime = info.CreateRuntime();
        var thread = runtime.Threads.FirstOrDefault(t => t.ManagedThreadId == managedThreadId);
        if (thread is null) return [$"(thread {managedThreadId} not found)"];

        // where the thread is right now, as a library name (a graphics or audio driver shows up here)
        var modules = target.EnumerateModules().ToList();
        string Module(ulong address)
        {
            foreach (var m in modules)
                if (address >= m.ImageBase && address < m.ImageBase + (ulong)Math.Max(0, m.IndexFileSize))
                    return Path.GetFileName(m.FileName ?? "?");
            return "?";
        }

        foreach (var frame in thread.EnumerateStackTrace(includeContext: false, maxFrames: 80))
        {
            string text;
            if (frame.Method is { } method)
                text = $"{method.Type?.Name ?? "?"}.{method.Name}";
            else if (!string.IsNullOrEmpty(frame.FrameName))
            {
                if (frame.FrameName is "InlinedCallFrame" or "PrestubMethodFrame") continue; // runtime bookkeeping, no information
                text = frame.FrameName;
            }
            else
                text = $"[native] {Module(frame.InstructionPointer)}";
            lines.Add(text);
            if (lines.Count >= MaxLines) break;
        }
        return lines;
    }

    private static string Clean(string text) => text.Length > 120 ? text[..120] : text;
}

/// <summary>Test aid for <c>--stall-test</c>: a recognisable place to be stuck in.</summary>
public static class StallTestSpot
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Block(TimeSpan time) => Thread.Sleep(time);
}
