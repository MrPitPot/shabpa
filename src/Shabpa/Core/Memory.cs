using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Shabpa.Core;

public sealed record MemoryInfo(long Total, long Available, long Standby)
{
    public long Used => Total - Available;
    public double UsedPercent => Total <= 0 ? 0 : 100.0 * Used / Total;
}

public sealed record MemoryResult(long Freed, int Processes, string? Error);

/// <summary>Frees RAM the way RAMMap does: trim every process's working set, and purge the standby
/// (cache) list. Trimming all processes and purging the cache need admin.</summary>
public static class MemoryCleaner
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID Luid;
        public uint Attributes;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr process);

    /// <summary>Shabpa's own memory: frees unused managed memory and hands idle pages back to Windows. Used while the
    /// window is hidden, so the app sitting next to the clock costs as little as possible. Pages come back on their own
    /// when something needs them.</summary>
    public static void ReleaseOwn()
    {
        try
        {
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            using var self = Process.GetCurrentProcess();
            EmptyWorkingSet(self.Handle);
        }
        catch
        {
            // best effort
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSystemFileCacheSize(IntPtr minimum, IntPtr maximum, int flags);

    [DllImport("ntdll.dll")]
    private static extern int NtSetSystemInformation(int infoClass, ref int info, int length);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out LUID luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, [MarshalAs(UnmanagedType.Bool)] bool disableAll,
        ref TOKEN_PRIVILEGES state, int length, IntPtr previous, IntPtr returnLength);

    private const int SystemMemoryListInformation = 80;
    private const int MemoryEmptyWorkingSets = 2;
    private const int MemoryFlushModifiedList = 3;
    private const int MemoryPurgeStandbyList = 4;

    private static PerformanceCounter[]? _standby;

    public static MemoryInfo Read()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status)) return new MemoryInfo(0, 0, 0);
        long standby = 0;
        try
        {
            _standby ??=
            [
                new PerformanceCounter("Memory", "Standby Cache Normal Priority Bytes", true),
                new PerformanceCounter("Memory", "Standby Cache Reserve Bytes", true),
                new PerformanceCounter("Memory", "Standby Cache Core Bytes", true),
            ];
            standby = _standby.Sum(c => c.RawValue);
        }
        catch { /* counters missing: the cache size is simply not shown */ }
        return new MemoryInfo((long)status.ullTotalPhys, (long)status.ullAvailPhys, standby);
    }

    private static bool EnablePrivilege(string name)
    {
        const uint TOKEN_ADJUST_PRIVILEGES = 0x20, TOKEN_QUERY = 0x8, SE_PRIVILEGE_ENABLED = 0x2;
        if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var token)) return false;
        try
        {
            if (!LookupPrivilegeValue(null, name, out var luid)) return false;
            var state = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
            // Returns true even when the privilege is not held; the last error tells them apart.
            return AdjustTokenPrivileges(token, false, ref state, 0, IntPtr.Zero, IntPtr.Zero) && Marshal.GetLastWin32Error() == 0;
        }
        finally
        {
            CloseHandle(token);
        }
    }

    private static bool MemoryCommand(int command) =>
        NtSetSystemInformation(SystemMemoryListInformation, ref command, sizeof(int)) >= 0;

    private static MemoryResult Measure(Func<(int processes, string? error)> work)
    {
        var before = Read().Available;
        var (processes, error) = work();
        Thread.Sleep(400); // the counters lag a little behind
        return new MemoryResult(Math.Max(0, Read().Available - before), processes, error);
    }

    /// <summary>Pushes the idle memory of running programs out of RAM. With admin rights every process
    /// is trimmed in one system call; otherwise only the ones this user may touch.</summary>
    public static MemoryResult TrimWorkingSets() => Measure(() =>
    {
        if (Elevation.IsElevated && EnablePrivilege("SeProfileSingleProcessPrivilege") && MemoryCommand(MemoryEmptyWorkingSets))
            return (Process.GetProcesses().Length, null);
        return (TrimEach(), null);
    });

    /// <summary>Trims processes one by one; returns how many were trimmed.</summary>
    public static int TrimEach(int? onlyPid = null)
    {
        const uint PROCESS_SET_QUOTA = 0x0100, PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        var count = 0;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (onlyPid is { } pid && process.Id != pid) continue;
                var handle = OpenProcess(PROCESS_SET_QUOTA | PROCESS_QUERY_LIMITED_INFORMATION, false, process.Id);
                if (handle == IntPtr.Zero) continue;
                if (EmptyWorkingSet(handle)) count++;
                CloseHandle(handle);
            }
        }
        return count;
    }

    /// <summary>Empties the file cache and the standby list (memory Windows keeps "just in case").</summary>
    public static MemoryResult PurgeCache() => Measure(() =>
    {
        if (!Elevation.IsElevated) return (0, "needs-admin");

        if (EnablePrivilege("SeIncreaseQuotaPrivilege"))
            SetSystemFileCacheSize(new IntPtr(-1), new IntPtr(-1), 0);

        if (!EnablePrivilege("SeProfileSingleProcessPrivilege"))
            return (0, new Win32Exception(Marshal.GetLastWin32Error()).Message);
        MemoryCommand(MemoryFlushModifiedList);
        return MemoryCommand(MemoryPurgeStandbyList) ? (0, null) : (0, "NtSetSystemInformation");
    });
}
