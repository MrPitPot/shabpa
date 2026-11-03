using System.IO;
using System.IO.Compression;
using Shabpa.Core;

namespace Shabpa.Tests;

/// <summary>The problem report: a zip on the desktop with the log, the log before it, the lighting server's newest log and a few lines about the PC. Nothing else, nothing sent.</summary>
public class LogExportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "shabpa-report-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    [Fact]
    public void The_zip_holds_both_logs_the_newest_lighting_log_and_the_info()
    {
        Directory.CreateDirectory(Path.Combine(_root, "data"));
        Directory.CreateDirectory(Path.Combine(_root, "openrgb", "logs"));
        var log = Path.Combine(_root, "data", "shabpa.log");
        File.WriteAllText(log, "new log");
        File.WriteAllText(log + ".old", "old log");
        File.WriteAllText(Path.Combine(_root, "openrgb", "logs", "OpenRGB_20261101_100000.log"), "older server log");
        File.WriteAllText(Path.Combine(_root, "openrgb", "logs", "OpenRGB_20261103_100000.log"), "newest server log");
        File.WriteAllText(Path.Combine(_root, "openrgb", "state.json"), "must not be in the zip");

        var path = LogExport.Create(Path.Combine(_root, "desktop"), new DateTime(2026, 11, 3, 4, 5, 6), log, "info", Path.Combine(_root, "openrgb"));

        Assert.Equal("Shabpa-logs-20261103-040506.zip", Path.GetFileName(path));
        using var zip = ZipFile.OpenRead(path);
        string Text(string name) { using var r = new StreamReader(zip.GetEntry(name)!.Open()); return r.ReadToEnd().TrimEnd(); }
        Assert.Equal(["info.txt", "lighting-server.log", "shabpa.log", "shabpa.log.old"], zip.Entries.Select(e => e.FullName).OrderBy(n => n).ToArray());
        Assert.Equal("new log", Text("shabpa.log"));
        Assert.Equal("old log", Text("shabpa.log.old"));
        Assert.Equal("newest server log", Text("lighting-server.log"));
    }

    [Fact]
    public void A_log_that_is_still_open_for_writing_can_be_read_and_missing_parts_are_skipped()
    {
        Directory.CreateDirectory(_root);
        var log = Path.Combine(_root, "shabpa.log");
        using (var writer = new FileStream(log, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
            writer.Write("being written"u8);
            writer.Flush();
            var path = LogExport.Create(Path.Combine(_root, "desktop"), DateTime.Now, log, "info");      // no .old, no lighting folder
            using var zip = ZipFile.OpenRead(path);
            Assert.Equal(["info.txt", "shabpa.log"], zip.Entries.Select(e => e.FullName).OrderBy(n => n).ToArray());
        }
    }

    [Fact]
    public void The_info_names_the_version_and_windows_but_nothing_personal()
    {
        var info = LogExport.Info(true, new DateTime(2026, 11, 3, 4, 5, 6));
        Assert.Contains("Shabpa ", info);
        Assert.Contains("Windows ", info);
        Assert.DoesNotContain(Environment.UserName, info, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.MachineName, info, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"state save failed: Access to the path 'C:\Users\Ali Reza\AppData\Roaming\Shabpa\state.json.tmp' is denied.", @"state save failed: Access to the path 'C:\Users\<user>\AppData\Roaming\Shabpa\state.json.tmp' is denied.")]
    [InlineData(@"scan d:\users\bob\Documents ok", @"scan d:\users\<user>\Documents ok")]
    [InlineData("C:/Users/amir/x done", "C:/Users/<user>/x done")]
    [InlineData("nothing personal here: 12:00 ping 25 ms", "nothing personal here: 12:00 ping 25 ms")]
    public void The_windows_user_name_is_taken_out_of_the_paths_in_the_log(string line, string expected) => Assert.Equal(expected, LogExport.Scrub(line));

    [Fact]
    public void The_zip_never_holds_the_user_name_and_no_half_written_file_is_left()
    {
        Directory.CreateDirectory(Path.Combine(_root, "data"));
        var log = Path.Combine(_root, "data", "shabpa.log");
        File.WriteAllText(log, "2026-11-03 error: C:\\Users\\SomeoneReal\\AppData\\x" + Environment.NewLine);
        var path = LogExport.Create(Path.Combine(_root, "desktop"), DateTime.Now, log, "info");
        using (var zip = ZipFile.OpenRead(path))
        {
            using var r = new StreamReader(zip.GetEntry("shabpa.log")!.Open());
            var text = r.ReadToEnd();
            Assert.DoesNotContain("SomeoneReal", text);
            Assert.Contains("<user>", text);
        }
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "desktop"), "*.tmp"));
    }
}
