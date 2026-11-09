using System.IO;
using Shabpa.Core;
using Xunit;

namespace Shabpa.Tests;

[Collection("log")]
public class LogTests
{
    private static string TempLog() => Path.Combine(Path.GetTempPath(), $"shabpa-logtest-{Guid.NewGuid():N}", "shabpa.log");

    [Fact]
    public void Lines_written_off_the_window_thread_are_on_disk_at_once_and_in_order()
    {
        var file = TempLog();
        var old = Log.File;
        Log.File = file;
        try
        {
            for (var i = 0; i < 50; i++) Log.Write($"line {i}");
            var lines = File.ReadAllLines(file);
            Assert.Equal(50, lines.Length);
            Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} line 0$", lines[0]);
            Assert.EndsWith("line 49", lines[^1]);
        }
        finally { Log.File = old; try { Directory.Delete(Path.GetDirectoryName(file)!, true); } catch { /* temp */ } }
    }

    [Fact]
    public void A_full_log_becomes_the_old_one_and_a_new_one_starts()
    {
        var file = TempLog();
        var old = Log.File;
        Log.File = file;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, new string('x', (int)Log.MaxBytes + 10));
            Log.Write("after the turn");
            Assert.True(File.Exists(file + ".old"));
            Assert.True(new FileInfo(file + ".old").Length > Log.MaxBytes);
            Assert.EndsWith("after the turn", File.ReadAllText(file).TrimEnd());
            Assert.True(Log.MaxBytes >= 2 * 1024 * 1024, "the log keeps a few days of evidence, not one evening");
        }
        finally { Log.File = old; try { Directory.Delete(Path.GetDirectoryName(file)!, true); } catch { /* temp */ } }
    }

    [Fact]
    public void Writing_from_many_threads_at_once_loses_no_line()
    {
        var file = TempLog();
        var old = Log.File;
        Log.File = file;
        try
        {
            Parallel.For(0, 400, i => Log.Write($"p{i}"));
            Log.Flush();
            var lines = File.ReadAllLines(file);
            Assert.Equal(400, lines.Length);
            Assert.Equal(400, lines.Select(l => l[(l.LastIndexOf(' ') + 1)..]).Distinct().Count());
        }
        finally { Log.File = old; try { Directory.Delete(Path.GetDirectoryName(file)!, true); } catch { /* temp */ } }
    }

    [Fact]
    public void The_machine_line_names_windows_memory_screens_and_the_look_but_nothing_personal()
    {
        var settings = new Settings { Theme = "koi", Glass = "mica", Language = "fa" };
        var line = App.MachineLine(settings);
        Assert.StartsWith("machine: Windows ", line);
        Assert.Contains("build ", line);
        Assert.Contains("GB RAM", line);
        Assert.Contains("theme koi", line);
        Assert.Contains("glass mica", line);
        Assert.DoesNotContain(Environment.MachineName, line);
        Assert.DoesNotContain(Environment.UserName, line);
    }
}
