using System.IO;
using System.Text.Json;
using Shabpa.Core;

namespace Shabpa.Tests;

public sealed class TempTree : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "shabpa-files-" + Guid.NewGuid().ToString("N"));

    public TempTree() => Directory.CreateDirectory(Root);

    public string Write(string relative, string content = "x", DateTime? modified = null)
    {
        var path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        if (modified is { } m) File.SetLastWriteTime(path, m);
        return path;
    }

    public string WriteBytes(string relative, int size, byte fill = 1)
    {
        var path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Enumerable.Repeat(fill, size).ToArray());
        return path;
    }

    public string Sub(string relative) => Path.Combine(Root, relative);

    public void Dispose()
    {
        try { Directory.Delete(Root, true); }
        catch (IOException) { /* a test left a handle; the temp folder is cleaned by Windows later */ }
    }
}

public class FileToolsTests
{
    [Fact]
    public void Analyze_measures_folders_and_finds_the_biggest_files()
    {
        using var t = new TempTree();
        t.WriteBytes("a/big.bin", 5000);
        t.WriteBytes("a/small.bin", 100);
        t.WriteBytes("b/medium.bin", 2000);
        t.WriteBytes("root.bin", 10);
        var report = FileTools.Analyze(t.Root);
        Assert.Equal(7110, report.TotalBytes);
        Assert.Equal(4, report.FileCount);
        Assert.Equal("a", Path.GetFileName(report.Folders[0].Path));
        Assert.Equal(5100, report.Folders[0].Bytes);
        Assert.Equal("big.bin", Path.GetFileName(report.Largest[0].Path));
        Assert.Equal(["big.bin", "medium.bin", "small.bin", "root.bin"], report.Largest.Select(f => Path.GetFileName(f.Path)));
    }

    [Fact]
    public void Analyze_can_be_cancelled()
    {
        using var t = new TempTree();
        t.Write("a/x.txt");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => FileTools.Analyze(t.Root, ct: cts.Token));
    }

    [Fact]
    public void Duplicates_are_found_by_content_not_by_name_or_size_alone()
    {
        using var t = new TempTree();
        var a = t.WriteBytes("one/photo.jpg", 3000, 7);
        var b = t.WriteBytes("two/copy of photo.jpg", 3000, 7);
        t.WriteBytes("three/same-size-different.bin", 3000, 9); // same size, other content
        t.WriteBytes("four/unique.bin", 1234, 1);
        File.SetLastWriteTime(a, new DateTime(2020, 1, 1));
        File.SetLastWriteTime(b, new DateTime(2024, 1, 1));

        var groups = FileTools.FindDuplicates([t.Root], minSize: 1000);
        var g = Assert.Single(groups);
        Assert.Equal(2, g.Files.Count);
        Assert.Equal(3000, g.Reclaimable);
        Assert.Equal(Path.GetFullPath(a), g.Files[0].Path, ignoreCase: true); // oldest first, so "keep the oldest" is simple
    }

    [Fact]
    public void Files_below_the_minimum_size_are_ignored()
    {
        using var t = new TempTree();
        t.WriteBytes("a.bin", 100);
        t.WriteBytes("b.bin", 100);
        Assert.Empty(FileTools.FindDuplicates([t.Root], minSize: 1000));
    }

    [Fact]
    public void Large_duplicates_that_differ_only_in_the_middle_are_not_grouped()
    {
        using var t = new TempTree();
        var a = t.WriteBytes("a.bin", 400_000, 5);
        var b = t.WriteBytes("b.bin", 400_000, 5);
        var bytes = File.ReadAllBytes(b);
        bytes[200_000] = 99; // the quick start/end hash is equal, the full hash is not
        File.WriteAllBytes(b, bytes);
        Assert.Empty(FileTools.FindDuplicates([t.Root], minSize: 1000));
        Assert.True(File.Exists(a));
    }
}

public class RecyclerSafetyTests
{
    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Windows\System32\kernel32.dll")]
    [InlineData(@"C:\Program Files\anything\x.exe")]
    [InlineData(@"C:\ProgramData\x.dat")]
    public void System_and_program_locations_are_protected(string path) => Assert.True(FileTools.IsProtectedPath(path));

    [Fact]
    public void Refuses_paths_outside_the_scanned_folders_wildcards_and_directories()
    {
        using var t = new TempTree();
        using var other = new TempTree();
        var inside = t.Write("keep/a.txt");
        var outside = other.Write("b.txt");
        Assert.Null(Recycler.Refusal(inside, [t.Root]));
        Assert.Equal("outside the scanned folders", Recycler.Refusal(outside, [t.Root]));
        Assert.NotNull(Recycler.Refusal(Path.Combine(t.Root, "*.txt"), [t.Root]));
        Assert.NotNull(Recycler.Refusal(Path.Combine(t.Root, "keep"), [t.Root])); // a directory
        Assert.Equal("no longer exists", Recycler.Refusal(Path.Combine(t.Root, "gone.txt"), [t.Root]));
        // a sibling folder whose name merely starts with the root's name is outside, too
        var sibling = t.Root + "-evil";
        Directory.CreateDirectory(sibling);
        try
        {
            var f = Path.Combine(sibling, "x.txt");
            File.WriteAllText(f, "x");
            Assert.Equal("outside the scanned folders", Recycler.Refusal(f, [t.Root]));
        }
        finally
        {
            Directory.Delete(sibling, true);
        }
    }

    [Fact]
    public void Send_moves_a_file_to_the_recycle_bin_and_reports_refusals()
    {
        using var t = new TempTree();
        var file = t.Write("gone.txt", "bye");
        var results = Recycler.Send([file, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe")], [t.Root]);
        Assert.True(results[0].ok, results[0].error);
        Assert.False(File.Exists(file));
        Assert.False(results[1].ok);
        Assert.True(File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe")));
    }
}

public class BackupTests
{
    private static BackupJob Job(TempTree src, TempTree dst, string name = "docs") => new()
    {
        Name = name, Sources = [src.Root], Destination = dst.Root, Frequency = BackupFrequency.Manual, VerifyAfter = true,
    };

    [Fact]
    public void A_first_backup_copies_everything_and_a_second_copies_nothing()
    {
        using var src = new TempTree();
        using var dst = new TempTree();
        src.Write("a.txt", "alpha");
        src.Write("sub/b.txt", "beta");
        var job = Job(src, dst);

        var plan = Backups.Plan(job);
        Assert.Equal(2, plan.ToCopy.Count);
        var result = Backups.Run(job, plan);
        Assert.True(result.Ok);
        Assert.Equal(2, result.Copied);
        Assert.Equal(2, result.Verified);

        var folder = Backups.JobFolder(job);
        var tag = new DirectoryInfo(src.Root).Name;
        Assert.Equal("beta", File.ReadAllText(Path.Combine(folder, tag, "sub", "b.txt")));
        Assert.Empty(Backups.Plan(job).ToCopy);
    }

    [Fact]
    public void Only_changed_files_are_copied_again_and_nothing_is_deleted_at_the_destination()
    {
        using var src = new TempTree();
        using var dst = new TempTree();
        var a = src.Write("a.txt", "one");
        src.Write("b.txt", "two");
        var job = Job(src, dst);
        Backups.Run(job, Backups.Plan(job));

        File.WriteAllText(a, "one changed");
        File.SetLastWriteTimeUtc(a, DateTime.UtcNow.AddMinutes(5));
        var plan = Backups.Plan(job);
        Assert.Equal("a.txt", Path.GetFileName(Assert.Single(plan.ToCopy).SourcePath));
        Backups.Run(job, plan);

        // the source file is deleted: the backup copy stays
        File.Delete(a);
        Backups.Run(job, Backups.Plan(job));
        var tag = new DirectoryInfo(src.Root).Name;
        Assert.Equal("one changed", File.ReadAllText(Path.Combine(Backups.JobFolder(job), tag, "a.txt")));
    }

    [Fact]
    public void Verify_detects_a_damaged_and_a_missing_copy()
    {
        using var src = new TempTree();
        using var dst = new TempTree();
        src.Write("a.txt", "alpha");
        src.Write("b.txt", "beta");
        var job = Job(src, dst);
        Backups.Run(job, Backups.Plan(job));
        Assert.True(Backups.Verify(job).Ok);

        var tag = new DirectoryInfo(src.Root).Name;
        File.WriteAllText(Path.Combine(Backups.JobFolder(job), tag, "a.txt"), "tampered");
        File.Delete(Path.Combine(Backups.JobFolder(job), tag, "b.txt"));
        var v = Backups.Verify(job);
        Assert.False(v.Ok);
        Assert.Single(v.Mismatched);
        Assert.Single(v.Missing);
    }

    [Fact]
    public void Restore_never_overwrites_and_checks_the_content()
    {
        using var src = new TempTree();
        using var dst = new TempTree();
        using var target = new TempTree();
        src.Write("a.txt", "original");
        var job = Job(src, dst);
        Backups.Run(job, Backups.Plan(job));
        var tag = new DirectoryInfo(src.Root).Name;
        var existing = target.Write(Path.Combine(tag, "a.txt"), "do not touch");

        var items = Backups.PlanRestore(job, target.Root);
        var item = Assert.Single(items);
        Assert.True(item.WouldCollide);
        var (restored, failed) = Backups.Restore(job, items);
        Assert.Equal(1, restored);
        Assert.Empty(failed);
        Assert.Equal("do not touch", File.ReadAllText(existing));
        Assert.Equal("original", File.ReadAllText(Path.Combine(target.Root, tag, "a (restored).txt")));
    }

    [Fact]
    public void A_corrupted_backup_file_is_not_restored()
    {
        using var src = new TempTree();
        using var dst = new TempTree();
        using var target = new TempTree();
        src.Write("a.txt", "original");
        var job = Job(src, dst);
        Backups.Run(job, Backups.Plan(job));
        var tag = new DirectoryInfo(src.Root).Name;
        File.WriteAllText(Path.Combine(Backups.JobFolder(job), tag, "a.txt"), "rotten");
        var (restored, failed) = Backups.Restore(job, Backups.PlanRestore(job, target.Root));
        Assert.Equal(0, restored);
        Assert.Single(failed);
        Assert.Empty(Directory.GetFiles(target.Root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void A_hostile_manifest_cannot_write_outside_the_restore_folder()
    {
        using var src = new TempTree();
        using var dst = new TempTree();
        using var target = new TempTree();
        src.Write("a.txt", "x");
        var job = Job(src, dst);
        Backups.Run(job, Backups.Plan(job));
        var manifestPath = Path.Combine(Backups.JobFolder(job), "shabpa-backup.json");
        var manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(manifestPath))!;
        manifest.Files["docs/../../evil.txt"] = new ManifestEntry { Size = 1, Sha256 = "00" };
        manifest.Files["C:/Windows/evil.txt"] = new ManifestEntry { Size = 1, Sha256 = "00" };
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));

        var items = Backups.PlanRestore(job, target.Root);
        Assert.All(items, i => Assert.StartsWith(Path.GetFullPath(target.Root), i.TargetPath, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(items, i => i.Key.Contains(".."));
        Assert.DoesNotContain(items, i => i.Key.StartsWith("C:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Unsafe_jobs_are_refused()
    {
        using var src = new TempTree();
        Directory.CreateDirectory(src.Sub("inside"));
        Assert.NotNull(Backups.Problem(new BackupJob { Name = "", Sources = [src.Root], Destination = src.Sub("inside") }));
        // destination inside the source would copy the backup into itself
        Assert.NotNull(Backups.Problem(new BackupJob { Name = "x", Sources = [src.Root], Destination = src.Sub("inside") }));
        Assert.NotNull(Backups.Problem(new BackupJob { Name = "x", Sources = [], Destination = "D:\\" }));
        Assert.NotNull(Backups.Problem(new BackupJob { Name = "x", Sources = [src.Sub("missing")], Destination = Path.GetTempPath() }));
    }

    [Fact]
    public void The_schedule_says_when_a_run_is_owed()
    {
        var job = new BackupJob { Frequency = BackupFrequency.Daily, Hour = 22, Minute = 0, Created = new DateTime(2026, 10, 10, 9, 0, 0) };
        Assert.False(job.IsDue(new DateTime(2026, 10, 10, 21, 0, 0)));  // not yet
        Assert.True(job.IsDue(new DateTime(2026, 10, 10, 22, 5, 0)));   // owed
        job.LastRun = new DateTime(2026, 10, 10, 22, 6, 0);
        Assert.False(job.IsDue(new DateTime(2026, 10, 11, 8, 0, 0)));   // done tonight, next one is tonight
        Assert.True(job.IsDue(new DateTime(2026, 10, 11, 22, 1, 0)));
        job.Enabled = false;
        Assert.False(job.IsDue(new DateTime(2026, 10, 11, 23, 0, 0)));
        Assert.False(new BackupJob { Frequency = BackupFrequency.Manual }.IsDue(DateTime.Now));
    }

    [Fact]
    public void A_weekly_job_waits_for_its_day()
    {
        var friday = new DateTime(2026, 10, 16, 23, 0, 0);
        var job = new BackupJob { Frequency = BackupFrequency.Weekly, Day = DayOfWeek.Friday, Hour = 22, Created = friday.AddDays(-3) };
        Assert.True(job.IsDue(friday));
        job.LastRun = friday;
        Assert.False(job.IsDue(friday.AddDays(3)));
        Assert.True(job.IsDue(friday.AddDays(7)));
    }

    [Fact]
    public void Source_folders_with_the_same_name_get_distinct_tags()
    {
        using var a = new TempTree();
        using var b = new TempTree();
        Directory.CreateDirectory(Path.Combine(a.Root, "Photos"));
        Directory.CreateDirectory(Path.Combine(b.Root, "Photos"));
        var tags = Backups.Tags([Path.Combine(a.Root, "Photos"), Path.Combine(b.Root, "Photos")]);
        Assert.Equal(2, tags.Count);
        Assert.Contains("Photos", tags.Keys);
        Assert.Contains("Photos-2", tags.Keys);
    }
}
