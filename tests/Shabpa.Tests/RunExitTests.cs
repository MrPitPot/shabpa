using Shabpa.Setup;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Shabpa.Tests;

/// <summary>Which ends of a run delete running.flag (a clean exit), which keep it with a reason, and which leave it for a real crash report.</summary>
[Collection("log")]
public class RunExitTests
{
    private static string Folder()
    {
        var d = Path.Combine(Path.GetTempPath(), "shabpa-exit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    private static string FlagPath(string d) => Path.Combine(d, RunMarker.FileName);

    [Fact]
    public void A_normal_end_removes_the_flag_and_ending_twice_is_harmless()
    {
        var d = Folder();
        try
        {
            RunMarker.Begin(d, "9.9.9", () => "SettingsPage", startTimer: false);
            Assert.True(File.Exists(FlagPath(d)));
            RunMarker.End();
            Assert.False(File.Exists(FlagPath(d)));
            RunMarker.End();
        }
        finally { Directory.Delete(d, true); }
    }

    [Fact]
    public void A_run_that_is_closing_does_not_delete_the_flag_of_the_copy_that_took_over()
    {
        var d = Folder();
        try
        {
            RunMarker.Begin(d, "9.9.9", () => "", startTimer: false);
            // the restarted copy (another process id) has written its own flag while this one was still closing
            File.WriteAllText(FlagPath(d), JsonSerializer.Serialize(new RunMarker.Mark { Version = "9.9.9", Pid = Environment.ProcessId + 4242, Started = DateTime.Now, Heartbeat = DateTime.Now }));
            RunMarker.End();
            Assert.True(File.Exists(FlagPath(d)));
            Assert.Equal(Environment.ProcessId + 4242, JsonSerializer.Deserialize<RunMarker.Mark>(File.ReadAllText(FlagPath(d)))!.Pid);
        }
        finally { Directory.Delete(d, true); }
    }

    [Fact]
    public void Windows_ending_the_session_leaves_a_flag_that_the_next_start_reads_as_not_a_crash()
    {
        var d = Folder();
        var log = Path.Combine(d, "shabpa.log");
        var oldFile = Log.File;
        try
        {
            RunMarker.Begin(d, "3.11.0", () => "OverlayPage", startTimer: false);
            RunMarker.Beat();
            RunMarker.NoteExit(RunMarker.ExitSession);
            var mark = JsonSerializer.Deserialize<RunMarker.Mark>(File.ReadAllText(FlagPath(d)))!;
            Assert.Equal(RunMarker.ExitSession, mark.Exit);
            var line = RunMarker.Describe(mark, _ => false)!;
            Assert.Contains("ended by Windows shutting down", line);
            Assert.DoesNotContain("unexpectedly", line);
            Assert.Contains("OverlayPage", line);
            // next start: the same line goes to the log, still not a crash report
            mark.Pid = Environment.ProcessId + 1;
            File.WriteAllText(FlagPath(d), JsonSerializer.Serialize(mark));
            Log.File = log;
            RunMarker.End();
            RunMarker.Begin(d, "3.11.1", () => "", startTimer: false);
            Assert.DoesNotContain("unexpectedly", File.ReadAllText(log));
            Assert.Contains("ended by Windows", File.ReadAllText(log));
        }
        finally { Log.File = oldFile; RunMarker.End(); Directory.Delete(d, true); }
    }

    [Fact]
    public void A_flag_with_no_reason_is_still_reported_as_unexpected_and_a_vetoed_session_end_is_forgotten()
    {
        var plain = new RunMarker.Mark { Version = "3.10.0", Pid = Environment.ProcessId + 1, Started = DateTime.Now };
        Assert.Contains("ended unexpectedly", RunMarker.Describe(plain, _ => false)!);
        var d = Folder();
        var saved = RunMarker.VetoGrace;
        try
        {
            RunMarker.VetoGrace = TimeSpan.Zero;
            RunMarker.Begin(d, "9.9.9", () => "", startTimer: false);
            RunMarker.Beat();
            RunMarker.NoteExit(RunMarker.ExitSession);
            Thread.Sleep(20);
            RunMarker.Beat();   // still alive after the "ending" note: another program vetoed it
            Assert.Equal("", JsonSerializer.Deserialize<RunMarker.Mark>(File.ReadAllText(FlagPath(d)))!.Exit);
        }
        finally { RunMarker.VetoGrace = saved; RunMarker.End(); Directory.Delete(d, true); }
    }

    [Fact]
    public void The_installer_marks_the_flag_of_the_copy_it_kills_and_only_that_one()
    {
        var d = Folder();
        try
        {
            var mark = new RunMarker.Mark { Version = "3.10.0", Pid = 31337, Started = new DateTime(2026, 10, 1, 9, 0, 0), Heartbeat = new DateTime(2026, 10, 1, 9, 5, 0), Page = "SettingsPage" };
            File.WriteAllText(FlagPath(d), JsonSerializer.Serialize(mark));
            Assert.False(Installer.MarkRunFlag(d, 555));                       // another process: untouched
            Assert.Equal("", JsonSerializer.Deserialize<RunMarker.Mark>(File.ReadAllText(FlagPath(d)))!.Exit);
            Assert.True(Installer.MarkRunFlag(d, 31337));
            var after = JsonSerializer.Deserialize<RunMarker.Mark>(File.ReadAllText(FlagPath(d)))!;
            Assert.Equal(RunMarker.ExitInstaller, after.Exit);
            Assert.Equal("SettingsPage", after.Page);
            Assert.Contains("ended by the installer", RunMarker.Describe(after, _ => false)!);
            Assert.True(Installer.MarkRunFlag(d, 31337));                      // twice is fine
            Assert.False(Installer.MarkRunFlag(Path.Combine(d, "nothing-here"), 31337));
        }
        finally { Directory.Delete(d, true); }
    }
}
