using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using Microsoft.Win32;

namespace Shabpa.Core;

internal static class Proc
{
    /// <summary>Runs a console tool hidden and returns (exit code, stdout + stderr).</summary>
    public static (int code, string output) Run(string file, string args, int timeoutMs = 15000)
    {
        try
        {
            using var p = new Process();
            p.StartInfo = new ProcessStartInfo(file, args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            p.Start();
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(); } catch { /* already gone */ }
                return (-1, "timeout");
            }
            return (p.ExitCode, stdout.Result + stderr.Result);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}

public sealed record SleepBlocker(string Category, string Kind, string Name, string Reason)
{
    public string CategoryText => Category switch
    {
        "DISPLAY" => L.T("صفحه رو روشن نگه می‌داره", "Keeps the screen on"),
        "SYSTEM" => L.T("نمی‌ذاره سیستم بخوابه", "Keeps the PC awake"),
        "AWAYMODE" => L.T("حالت Away", "Away mode"),
        "EXECUTION" => L.T("اجرای پس‌زمینه", "Background execution"),
        "PERFBOOST" => L.T("افزایش کارایی", "Performance boost"),
        "ACTIVELOCKSCREEN" => L.T("صفحه‌ی قفل", "Lock screen"),
        _ => Category,
    };

    public string KindText => Kind switch
    {
        "DRIVER" => L.T("درایور", "Driver"),
        "PROCESS" => L.T("برنامه", "App"),
        "SERVICE" => L.T("سرویس", "Service"),
        _ => Kind,
    };
}

/// <summary>What is keeping the PC awake, from <c>powercfg /requests</c> (needs admin).</summary>
public static class SleepBlockers
{
    public sealed record Result(bool NeedsAdmin, IReadOnlyList<SleepBlocker> Items, string? Error = null);

    public static Result Read()
    {
        var (code, output) = Proc.Run("powercfg.exe", "/requests");
        if (code != 0) return new Result(true, []);
        return new Result(false, Parse(output));
    }

    /// <summary>Asks for elevation (UAC) and reads the list through a temp file.</summary>
    public static Result ReadElevated()
    {
        var file = Path.Combine(Path.GetTempPath(), $"shabpa-requests-{Guid.NewGuid():N}.txt");
        try
        {
            using var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c powercfg /requests > \"{file}\"")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (p is null) return new Result(true, [], L.T("اجرا نشد.", "Could not start."));
            p.WaitForExit(15000);
            return File.Exists(file) ? new Result(false, Parse(File.ReadAllText(file))) : new Result(true, [], L.T("خروجی‌ای دریافت نشد.", "No output was returned."));
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return new Result(true, [], L.T("دسترسی ادمین داده نشد.", "Admin access was not granted."));
        }
        finally
        {
            try { File.Delete(file); } catch { /* best effort */ }
        }
    }

    internal static List<SleepBlocker> Parse(string output)
    {
        var items = new List<SleepBlocker>();
        var category = "";
        SleepBlocker? pending = null;

        void Flush()
        {
            if (pending is not null) items.Add(pending);
            pending = null;
        }

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            if (line.EndsWith(':') && !line.StartsWith('[') && line.All(c => char.IsLetter(c) || c == ':'))
            {
                Flush();
                category = line.TrimEnd(':').ToUpperInvariant();
                continue;
            }

            if (line.StartsWith('['))
            {
                Flush();
                var close = line.IndexOf(']');
                if (close < 0) continue;
                pending = new SleepBlocker(category, line[1..close].ToUpperInvariant(), FriendlyName(line[(close + 1)..].Trim()), "");
            }
            else if (pending is not null)
            {
                // Anything after an entry is its reason; "None." under a header never has a pending entry.
                pending = pending with { Reason = pending.Reason.Length == 0 ? line : $"{pending.Reason} {line}" };
            }
        }
        Flush();
        return items;
    }

    private static string FriendlyName(string name)
    {
        // "\Device\HarddiskVolume3\Program Files\App\app.exe" -> "app.exe"
        if (name.StartsWith(@"\Device\", StringComparison.OrdinalIgnoreCase)) return name.Split('\\')[^1];
        // "Realtek Audio (HDAUDIO\FUNC_01&VEN...)" -> "Realtek Audio"
        var paren = name.IndexOf(" (", StringComparison.Ordinal);
        return paren > 0 && name.EndsWith(')') ? name[..paren] : name;
    }
}

/// <summary>Wake-from-sleep schedules, implemented as Task Scheduler tasks with "wake to run".</summary>
public static class WakeScheduler
{
    public const string Folder = "Shabpa";

    public static string TaskName(Guid id) => $@"{Folder}\Wake-{id:N}";

    public static string BuildXml(WakeSchedule s, string exePath, DateTime now)
    {
        var start = now.Date.AddHours(s.Hour).AddMinutes(s.Minute);
        string schedule;
        if (s.Days.Count is 0 or 7)
            schedule = "<ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay>";
        else
            schedule = "<ScheduleByWeek><DaysOfWeek>" + string.Concat(s.Days.Distinct().OrderBy(d => d).Select(d => $"<{d} />")) +
                       "</DaysOfWeek><WeeksInterval>1</WeeksInterval></ScheduleByWeek>";

        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Shabpa: wake the PC from sleep at {s.Hour:00}:{s.Minute:00}</Description>
              </RegistrationInfo>
              <Triggers>
                <CalendarTrigger>
                  <StartBoundary>{start.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)}</StartBoundary>
                  <Enabled>true</Enabled>
                  {schedule}
                </CalendarTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>LeastPrivilege</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <StartWhenAvailable>false</StartWhenAvailable>
                <WakeToRun>true</WakeToRun>
                <ExecutionTimeLimit>PT10M</ExecutionTimeLimit>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{SecurityElement.Escape(exePath)}</Command>
                  <Arguments>--wake</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    /// <summary>Returns an error message, or null on success.</summary>
    public static string? Register(WakeSchedule s, string exePath)
    {
        var file = Path.Combine(Path.GetTempPath(), $"shabpa-task-{s.Id:N}.xml");
        try
        {
            File.WriteAllText(file, BuildXml(s, exePath, DateTime.Now), Encoding.Unicode);
            var (code, output) = Proc.Run("schtasks.exe", $"/Create /TN \"{TaskName(s.Id)}\" /XML \"{file}\" /F");
            if (code == 0) return null;
            Log.Write($"schtasks create failed: {output}");
            return output.Trim();
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
        finally
        {
            try { File.Delete(file); } catch { /* best effort */ }
        }
    }

    public static bool Unregister(Guid id) =>
        Proc.Run("schtasks.exe", $"/Delete /TN \"{TaskName(id)}\" /F").code == 0;

    public static bool Exists(Guid id) =>
        Proc.Run("schtasks.exe", $"/Query /TN \"{TaskName(id)}\"").code == 0;

    /// <summary>Whether "Allow wake timers" is on for the current power plan while plugged in.
    /// Null when it cannot be determined.</summary>
    public static bool? WakeTimersEnabled()
    {
        var (code, output) = Proc.Run("powercfg.exe", "/q SCHEME_CURRENT SUB_SLEEP RTCWAKE");
        return code == 0 ? ParseWakeTimers(output) : null;
    }

    internal static bool? ParseWakeTimers(string output)
    {
        // The line of interest is the first "... Index: 0x0000000N" after the possible values, i.e. the
        // AC setting; its label is localised, so match on shape rather than text.
        var values = output.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Contains(": 0x", StringComparison.Ordinal) && !l.Contains("GUID", StringComparison.OrdinalIgnoreCase))
            .Select(l => l[(l.LastIndexOf("0x", StringComparison.Ordinal) + 2)..])
            .Select(hex => int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : -1)
            .ToList();
        if (values.Count < 2) return null;
        // Last two entries are the current AC and DC values. 0 = off, 1 = on, 2 = important timers only.
        return values[^2] == 1;
    }

    /// <summary>Turns wake timers on for the current plan. Returns false if Windows refused.</summary>
    public static bool EnableWakeTimers()
    {
        var ac = Proc.Run("powercfg.exe", "/setacvalueindex SCHEME_CURRENT SUB_SLEEP RTCWAKE 1").code == 0;
        Proc.Run("powercfg.exe", "/setdcvalueindex SCHEME_CURRENT SUB_SLEEP RTCWAKE 1");
        return ac && Proc.Run("powercfg.exe", "/setactive SCHEME_CURRENT").code == 0;
    }

    /// <summary>Runs in the short-lived process the wake task starts: turn the screen on and tell
    /// Windows somebody is here, otherwise it goes back to sleep after two unattended minutes.</summary>
    public static void HandleWake()
    {
        Log.Write("wake task fired");
        Native.SetThreadExecutionState(Native.ES_SYSTEM_REQUIRED | Native.ES_DISPLAY_REQUIRED);
        Native.NudgeInput();
        Thread.Sleep(TimeSpan.FromSeconds(20));
        Native.NudgeInput();
    }
}

public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "Shabpa";
    private const string TaskName = @"Shabpa\Autostart";

    public static bool IsEnabled() => RunKeyEnabled() || TaskEnabled();

    // "Is the logon task there" is a console tool (schtasks) that can take seconds on a busy or antivirus-scanned PC, so the
    // window never waits for it: it gets the last answer at once and the real one arrives from the background.
    private static readonly Cached<bool> TaskState = new(TaskEnabled, TimeSpan.FromMinutes(1));

    /// <summary>The same answer as <see cref="IsEnabled"/> without ever running a program on the caller's thread.
    /// <paramref name="changed"/> is called (on a pool thread) when the background check finds a different answer.</summary>
    public static bool IsEnabledCached(Action<bool>? changed = null) =>
        RunKeyEnabled() || TaskState.Get(task => changed?.Invoke(RunKeyEnabled() || task));

    public static bool RunKeyEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(Name) is string;
    }

    public static bool TaskEnabled() => Proc.Run("schtasks.exe", $"/Query /TN \"{TaskName}\"").code == 0;

    public static string BuildTaskXml(string exePath, string user) => $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <Description>Shabpa: start with Windows, with admin rights for per-app network limits</Description>
          </RegistrationInfo>
          <Triggers>
            <LogonTrigger>
              <Enabled>true</Enabled>
              <UserId>{SecurityElement.Escape(user)}</UserId>
            </LogonTrigger>
          </Triggers>
          <Principals>
            <Principal id="Author">
              <UserId>{SecurityElement.Escape(user)}</UserId>
              <LogonType>InteractiveToken</LogonType>
              <RunLevel>HighestAvailable</RunLevel>
            </Principal>
          </Principals>
          <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
            <Enabled>true</Enabled>
          </Settings>
          <Actions Context="Author">
            <Exec>
              <Command>{SecurityElement.Escape(exePath)}</Command>
              <Arguments>--minimized</Arguments>
            </Exec>
          </Actions>
        </Task>
        """;

    /// <summary>With <paramref name="elevated"/> the app is started by a logon task that keeps its admin
    /// rights (a Run-key entry would start it without them). Returns an error message or null.</summary>
    public static string? Set(bool enabled, string exePath, bool elevated = false)
    {
        try { return SetCore(enabled, exePath, elevated); }
        finally { TaskState.Invalidate(); }
    }

    private static string? SetCore(bool enabled, string exePath, bool elevated)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (enabled && !elevated) key.SetValue(Name, $"\"{exePath}\" --minimized");
            else key.DeleteValue(Name, false);
        }

        if (!elevated)
        {
            // Removing a highest-privilege task needs admin; if that fails the task simply stays.
            if (TaskEnabled()) Proc.Run("schtasks.exe", $"/Delete /TN \"{TaskName}\" /F");
            return null;
        }

        if (!enabled)
        {
            Proc.Run("schtasks.exe", $"/Delete /TN \"{TaskName}\" /F");
            return null;
        }

        var file = Path.Combine(Path.GetTempPath(), "shabpa-autostart.xml");
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            File.WriteAllText(file, BuildTaskXml(exePath, identity.Name), Encoding.Unicode);
            var (code, output) = Proc.Run("schtasks.exe", $"/Create /TN \"{TaskName}\" /XML \"{file}\" /F");
            return code == 0 ? null : output.Trim();
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
        finally
        {
            try { File.Delete(file); } catch { /* best effort */ }
        }
    }
}
