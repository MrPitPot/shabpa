using System.Runtime.InteropServices;
using System.IO;
using System.Text;

namespace Shabpa.Core;

/// <summary>Where a frozen window thread is stuck once it has left managed code: the chain of libraries and offsets (a graphics driver, a hook of
/// another program, the window system), read live from the thread with the Windows unwinder. The managed stack of a stall stops at the last .NET
/// frame; when that frame is only "DispatchMessage", this is what says who is holding the thread. Nothing is written to the thread and it is
/// resumed at once, so it is safe on a thread that is already stuck.</summary>
public static class NativeStack
{
    private const uint ThreadSuspendResume = 0x0002, ThreadGetContext = 0x0008, ThreadQueryInformation = 0x0040;
    private const uint ContextFull = 0x10000B;                 // CONTEXT_AMD64 | control | integer | floating point
    private const int ContextSize = 1232, RspOffset = 0x98, RipOffset = 0xF8, FlagsOffset = 0x30;
    public const int MaxFrames = 40;

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenThread(uint access, bool inherit, uint threadId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint SuspendThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetThreadContext(IntPtr thread, IntPtr context);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetModuleHandleExW(uint flags, IntPtr address, out IntPtr module);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetModuleFileNameW(IntPtr module, StringBuilder name, uint size);
    [DllImport("ntdll.dll")] private static extern IntPtr RtlLookupFunctionEntry(ulong controlPc, out ulong imageBase, IntPtr history);
    [DllImport("ntdll.dll")] private static extern IntPtr RtlVirtualUnwind(uint handlerType, ulong imageBase, ulong controlPc, IntPtr functionEntry, IntPtr context, out IntPtr handlerData, out ulong establisherFrame, IntPtr contextPointers);

    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    /// <summary>The Windows id of the calling thread (what <see cref="Capture"/> asks for).</summary>
    public static uint CurrentThreadId() => GetCurrentThreadId();

    /// <summary>One line per frame, top first: <c>library+0xoffset</c>. A single line in parentheses says why nothing could be read.</summary>
    public static IReadOnlyList<string> Capture(uint osThreadId)
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64) return ["(native stacks are only read on 64-bit Intel/AMD processors)"];
        var addresses = new List<ulong>();
        var handle = OpenThread(ThreadSuspendResume | ThreadGetContext | ThreadQueryInformation, false, osThreadId);
        if (handle == IntPtr.Zero) return [$"(thread {osThreadId} could not be opened: error {Marshal.GetLastWin32Error()})"];
        var block = Marshal.AllocHGlobal(ContextSize + 16);
        try
        {
            var context = (IntPtr)((block.ToInt64() + 15) & ~15L);                // the context record has to sit on a 16-byte boundary
            Marshal.Copy(new byte[ContextSize], 0, context, ContextSize);
            Marshal.WriteInt32(context, FlagsOffset, unchecked((int)ContextFull));
            if (SuspendThread(handle) == uint.MaxValue) return [$"(thread {osThreadId} could not be paused: error {Marshal.GetLastWin32Error()})"];
            try
            {
                if (!GetThreadContext(handle, context)) return [$"(thread {osThreadId} has no readable state: error {Marshal.GetLastWin32Error()})"];
                // only plain reads and the unwinder in here: the thread may hold any lock, so nothing that could wait for one (no logging, no module lookups)
                for (var i = 0; i < MaxFrames; i++)
                {
                    var rip = (ulong)Marshal.ReadInt64(context, RipOffset);
                    if (rip == 0) break;
                    addresses.Add(rip);
                    var entry = RtlLookupFunctionEntry(rip, out var imageBase, IntPtr.Zero);
                    if (entry == IntPtr.Zero)
                    {
                        var rsp = (ulong)Marshal.ReadInt64(context, RspOffset);          // a leaf: the return address is on top of the stack
                        if (rsp == 0) break;
                        Marshal.WriteInt64(context, RipOffset, Marshal.ReadInt64((IntPtr)rsp));
                        Marshal.WriteInt64(context, RspOffset, (long)(rsp + 8));
                    }
                    else RtlVirtualUnwind(0, imageBase, rip, entry, context, out _, out _, IntPtr.Zero);
                }
            }
            finally { ResumeThread(handle); }
        }
        catch (Exception ex) when (ex is AccessViolationException or InvalidOperationException or ArgumentException)
        {
            if (addresses.Count == 0) return [$"(native stack not available: {ex.GetType().Name})"];
        }
        finally
        {
            Marshal.FreeHGlobal(block);
            CloseHandle(handle);
        }
        return addresses.Select(Describe).ToList();
    }

    private static string Describe(ulong address)
    {
        const uint FromAddress = 0x4, UnchangedRefCount = 0x2;
        if (!GetModuleHandleExW(FromAddress | UnchangedRefCount, (IntPtr)address, out var module) || module == IntPtr.Zero) return $"0x{address:X}";
        var name = new StringBuilder(260);
        var length = GetModuleFileNameW(module, name, (uint)name.Capacity);
        var file = length > 0 ? Path.GetFileName(name.ToString()) : "?";
        return $"{file}+0x{address - (ulong)module:X}";
    }
}
