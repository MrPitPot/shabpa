using System.Diagnostics;

namespace Shabpa.Core;

public interface IPowerExecutor
{
    /// <summary>Runs the action. Returns an error message, or null on success.</summary>
    string? Execute(PowerAction action);
}

public sealed class WindowsPowerExecutor : IPowerExecutor
{
    public string? Execute(PowerAction action)
    {
        Log.Write($"execute {action}");
        try
        {
            switch (action)
            {
                case PowerAction.Sleep:
                    // Off the caller's thread: the call only returns after the machine resumes.
                    Task.Run(() => Native.SetSuspendState(false, false, false));
                    return null;
                case PowerAction.Hibernate:
                    Task.Run(() => Native.SetSuspendState(true, false, false));
                    return null;
                case PowerAction.Shutdown:
                    return Shutdown("/s /t 0");
                case PowerAction.Restart:
                    return Shutdown("/r /t 0");
                default:
                    return Native.LockWorkStation() ? null : L.T("قفل کردن صفحه انجام نشد.", "Could not lock the screen.");
            }
        }
        catch (Exception ex)
        {
            Log.Write($"execute {action} failed: {ex}");
            return ex.Message;
        }
    }

    private static string? Shutdown(string args)
    {
        using var p = Process.Start(new ProcessStartInfo("shutdown.exe", args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        });
        return p is null ? L.T("دستور shutdown اجرا نشد.", "Could not run the shutdown command.") : null;
    }
}

/// <summary>Records actions instead of running them; used by --dry-run and by the tests.</summary>
public sealed class DryRunPowerExecutor : IPowerExecutor
{
    public List<PowerAction> Executed { get; } = [];

    public string? Execute(PowerAction action)
    {
        Executed.Add(action);
        Log.Write($"DRY-RUN {action}");
        return null;
    }
}
