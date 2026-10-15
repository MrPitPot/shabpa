using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Shabpa.Core;

public enum BackupFrequency { Manual, Daily, Weekly }

public sealed class BackupRunLog
{
    public DateTime Time { get; set; }
    public bool Ok { get; set; }
    public int Files { get; set; }
    public long Bytes { get; set; }
    public string Note { get; set; } = "";
}

/// <summary>A folder backup: which folders, where to, and when. A job only ever copies files to the destination; it
/// never deletes anything there or at the source.</summary>
public sealed class BackupJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public List<string> Sources { get; set; } = [];
    public string Destination { get; set; } = "";
    public BackupFrequency Frequency { get; set; } = BackupFrequency.Daily;
    public int Hour { get; set; } = 22;
    public int Minute { get; set; }
    public DayOfWeek Day { get; set; } = DayOfWeek.Friday;
    public bool Enabled { get; set; } = true;
    public bool VerifyAfter { get; set; } = true;
    public DateTime Created { get; set; } = DateTime.Now;
    public DateTime? LastRun { get; set; }
    public List<BackupRunLog> History { get; set; } = [];

    /// <summary>The most recent time the schedule says a run should have happened, or null for manual jobs.</summary>
    public DateTime? LatestOccurrence(DateTime now)
    {
        if (Frequency == BackupFrequency.Manual) return null;
        var at = now.Date.AddHours(Math.Clamp(Hour, 0, 23)).AddMinutes(Math.Clamp(Minute, 0, 59));
        if (Frequency == BackupFrequency.Daily) return at <= now ? at : at.AddDays(-1);
        var back = ((int)now.DayOfWeek - (int)Day + 7) % 7;
        var weekly = at.AddDays(-back);
        return weekly <= now ? weekly : weekly.AddDays(-7);
    }

    /// <summary>True when a scheduled run is owed: the schedule has passed a time after the last run (or after the job
    /// was created, if it never ran).</summary>
    public bool IsDue(DateTime now) =>
        Enabled && LatestOccurrence(now) is { } occurrence && occurrence > (LastRun ?? Created);

    public DateTime? NextOccurrence(DateTime now)
    {
        if (Frequency == BackupFrequency.Manual || LatestOccurrence(now) is not { } last) return null;
        return Frequency == BackupFrequency.Daily ? last.AddDays(1) : last.AddDays(7);
    }
}

public sealed class ManifestEntry
{
    public long Size { get; set; }
    public long ModifiedUtcTicks { get; set; }
    public string Sha256 { get; set; } = "";
}

public sealed class BackupManifest
{
    public int Version { get; set; } = 1;
    /// <summary>Source folder tag to its original path.</summary>
    public Dictionary<string, string> Sources { get; set; } = [];
    /// <summary>"tag/relative/path" to what was copied.</summary>
    public Dictionary<string, ManifestEntry> Files { get; set; } = [];
}

public sealed record PlannedCopy(string SourcePath, string Key, long Size, long ModifiedUtcTicks);

public sealed record BackupPlan(IReadOnlyList<PlannedCopy> ToCopy, int Unchanged, long Bytes, IReadOnlyList<string> Problems, Dictionary<string, string> Tags);

public sealed record BackupResult(bool Ok, int Copied, long Bytes, IReadOnlyList<string> Failed, int Verified, IReadOnlyList<string> Corrupt, string Note);

public sealed record VerifyResult(int Checked, IReadOnlyList<string> Missing, IReadOnlyList<string> Mismatched)
{
    public bool Ok => Missing.Count == 0 && Mismatched.Count == 0;
}

public sealed record RestoreItem(string Key, string TargetPath, long Size, bool WouldCollide);

public static class Backups
{
    private const string ManifestName = "shabpa-backup.json";
    private const string TempSuffix = ".shabpa-part";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string SafeName(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
        return clean.Length == 0 ? "backup" : clean.Length > 60 ? clean[..60] : clean;
    }

    /// <summary>The folder inside the destination that belongs to this job.</summary>
    public static string JobFolder(BackupJob job) => Path.Combine(job.Destination, SafeName(job.Name));

    private static string ManifestPath(BackupJob job) => Path.Combine(JobFolder(job), ManifestName);

    public static BackupManifest LoadManifest(BackupJob job)
    {
        try
        {
            var path = ManifestPath(job);
            if (!File.Exists(path)) return new BackupManifest();
            return JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(path), Json) ?? new BackupManifest();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Write($"backup manifest: {ex.Message}");
            return new BackupManifest();
        }
    }

    private static void SaveManifest(BackupJob job, BackupManifest manifest)
    {
        var path = ManifestPath(job);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + TempSuffix;
        File.WriteAllText(tmp, JsonSerializer.Serialize(manifest, Json));
        File.Move(tmp, path, true);
    }

    /// <summary>Why this job cannot run, or null if it can.</summary>
    public static string? Problem(BackupJob job)
    {
        if (job.Name.Trim().Length == 0) return L.T("اسم نداره.", "It has no name.");
        if (job.Sources.Count == 0) return L.T("هیچ پوشه‌ای برای پشتیبان‌گیری انتخاب نشده.", "No folder is chosen to back up.");
        if (job.Destination.Trim().Length == 0) return L.T("مقصد انتخاب نشده.", "No destination is chosen.");
        string dest;
        try { dest = Path.GetFullPath(job.Destination).TrimEnd('\\') + "\\"; }
        catch { return L.T("آدرس مقصد درست نیست.", "The destination path is not valid."); }
        foreach (var source in job.Sources)
        {
            string full;
            try { full = Path.GetFullPath(source).TrimEnd('\\') + "\\"; }
            catch { return L.T("آدرس یکی از پوشه‌ها درست نیست.", "One of the folders has an invalid path."); }
            if (!Directory.Exists(source)) return L.T($"پوشه پیدا نشد: {source}", $"Folder not found: {source}");
            if (dest.StartsWith(full, StringComparison.OrdinalIgnoreCase)) return L.T("مقصد نباید داخل خود پوشه‌ی پشتیبان‌گیری باشه.", "The destination cannot be inside a folder being backed up.");
            if (full.StartsWith(dest, StringComparison.OrdinalIgnoreCase)) return L.T("پوشه‌ی منبع نباید داخل مقصد باشه.", "A source folder cannot be inside the destination.");
        }
        return null;
    }

    /// <summary>Unique, file-system safe tag per source folder.</summary>
    public static Dictionary<string, string> Tags(IEnumerable<string> sources)
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            var name = SafeName(new DirectoryInfo(source).Name is { Length: > 0 } n ? n : "root");
            var tag = name;
            for (var i = 2; tags.ContainsKey(tag); i++) tag = $"{name}-{i}";
            tags[tag] = source;
        }
        return tags;
    }

    private static readonly EnumerationOptions Walk = new()
    {
        IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
    };

    /// <summary>What a run would do. Changes nothing.</summary>
    public static BackupPlan Plan(BackupJob job, CancellationToken ct = default)
    {
        var tags = Tags(job.Sources);
        var manifest = LoadManifest(job);
        var copy = new List<PlannedCopy>();
        var problems = new List<string>();
        var unchanged = 0;
        long bytes = 0;
        var jobFolder = JobFolder(job).TrimEnd('\\') + "\\";
        foreach (var (tag, source) in tags)
        {
            var root = Path.GetFullPath(source).TrimEnd('\\') + "\\";
            foreach (var path in Directory.EnumerateFiles(source, "*", Walk))
            {
                ct.ThrowIfCancellationRequested();
                if (path.StartsWith(jobFolder, StringComparison.OrdinalIgnoreCase)) continue;
                FileInfo info;
                try { info = new FileInfo(path); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    problems.Add(path);
                    continue;
                }
                var key = tag + "/" + path[root.Length..].Replace('\\', '/');
                var ticks = info.LastWriteTimeUtc.Ticks;
                var target = Path.Combine(JobFolder(job), tag, path[root.Length..]);
                if (manifest.Files.TryGetValue(key, out var old) && old.Size == info.Length
                    && Math.Abs(old.ModifiedUtcTicks - ticks) <= TimeSpan.TicksPerSecond * 2 && File.Exists(target))
                {
                    unchanged++;
                    continue;
                }
                copy.Add(new PlannedCopy(path, key, info.Length, ticks));
                bytes += info.Length;
            }
        }
        return new BackupPlan(copy, unchanged, bytes, problems, tags);
    }

    private static string TargetOf(BackupJob job, string key)
    {
        var slash = key.IndexOf('/');
        var tag = key[..slash];
        var rel = key[(slash + 1)..].Replace('/', '\\');
        var full = Path.GetFullPath(Path.Combine(JobFolder(job), tag, rel));
        var root = Path.GetFullPath(JobFolder(job)).TrimEnd('\\') + "\\";
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("path escapes the backup folder");
        return full;
    }

    /// <summary>Copies what the plan lists. Each file is written under a temporary name and moved into place when it is
    /// complete, so a stop in the middle never leaves half a file that looks real.</summary>
    public static BackupResult Run(BackupJob job, BackupPlan plan, CancellationToken ct = default, Action<int, int>? progress = null)
    {
        var problem = Problem(job);
        if (problem is not null) return new BackupResult(false, 0, 0, [], 0, [], problem);
        Directory.CreateDirectory(JobFolder(job));

        // enough room? keep a little spare
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(job.Destination))!);
            if (drive.AvailableFreeSpace < plan.Bytes + (100L << 20))
                return new BackupResult(false, 0, 0, [], 0, [], L.T("جای خالی مقصد کافی نیست.", "Not enough free space at the destination."));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException) { /* a network path: let the copy decide */ }

        var manifest = LoadManifest(job);
        foreach (var (tag, source) in plan.Tags) manifest.Sources[tag] = source;
        var failed = new List<string>();
        long done = 0;
        var copied = 0;
        var block = new byte[1 << 20];
        foreach (var item in plan.ToCopy)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var target = TargetOf(job, item.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var tmp = target + TempSuffix;
                string hash;
                using (var input = new FileStream(item.SourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan))
                using (var output = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                using (var sha = SHA256.Create())
                {
                    int read;
                    while ((read = input.Read(block, 0, block.Length)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        output.Write(block, 0, read);
                        sha.TransformBlock(block, 0, read, null, 0);
                    }
                    sha.TransformFinalBlock([], 0, 0);
                    hash = Convert.ToHexString(sha.Hash!);
                }
                File.Move(tmp, target, true);
                try { File.SetLastWriteTimeUtc(target, new DateTime(item.ModifiedUtcTicks, DateTimeKind.Utc)); } catch (IOException) { /* keeps the copy time */ }
                manifest.Files[item.Key] = new ManifestEntry { Size = item.Size, ModifiedUtcTicks = item.ModifiedUtcTicks, Sha256 = hash };
                copied++;
                done += item.Size;
            }
            catch (OperationCanceledException)
            {
                SaveManifest(job, manifest);
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                failed.Add(item.SourcePath);
                Log.Write($"backup copy failed: {ex.GetType().Name}");
            }
            progress?.Invoke(copied + failed.Count, plan.ToCopy.Count);
        }
        SaveManifest(job, manifest);

        var corrupt = new List<string>();
        var verified = 0;
        if (job.VerifyAfter)
        {
            var v = Verify(job, ct, only: plan.ToCopy.Select(p => p.Key).ToHashSet());
            verified = v.Checked;
            corrupt.AddRange(v.Missing);
            corrupt.AddRange(v.Mismatched);
        }
        var ok = failed.Count == 0 && corrupt.Count == 0;
        return new BackupResult(ok, copied, done, failed, verified, corrupt, "");
    }

    /// <summary>Re-reads the backed-up files and compares them with what was recorded when they were copied.</summary>
    public static VerifyResult Verify(BackupJob job, CancellationToken ct = default, HashSet<string>? only = null)
    {
        var manifest = LoadManifest(job);
        var missing = new List<string>();
        var bad = new List<string>();
        var checkedCount = 0;
        foreach (var (key, entry) in manifest.Files)
        {
            if (only is not null && !only.Contains(key)) continue;
            ct.ThrowIfCancellationRequested();
            string target;
            try { target = TargetOf(job, key); }
            catch (InvalidDataException)
            {
                bad.Add(key);
                continue;
            }
            if (!File.Exists(target))
            {
                missing.Add(key);
                continue;
            }
            checkedCount++;
            try
            {
                using var stream = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, FileOptions.SequentialScan);
                using var sha = SHA256.Create();
                var actual = Convert.ToHexString(sha.ComputeHash(stream));
                if (!actual.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase)) bad.Add(key);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                bad.Add(key);
            }
        }
        return new VerifyResult(checkedCount, missing, bad);
    }

    // ---- restore ----------------------------------------------------------------------------------

    /// <summary>What restoring into <paramref name="targetFolder"/> would write. Nothing is overwritten: a file that
    /// already exists there is restored next to it with " (restored)" added.</summary>
    public static List<RestoreItem> PlanRestore(BackupJob job, string targetFolder, Func<string, bool>? include = null)
    {
        var manifest = LoadManifest(job);
        var items = new List<RestoreItem>();
        var root = Path.GetFullPath(targetFolder).TrimEnd('\\') + "\\";
        foreach (var (key, entry) in manifest.Files.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (include is not null && !include(key)) continue;
            if (key.Contains("..") || Path.IsPathRooted(key) || key.IndexOf('/') <= 0) continue; // a damaged or hostile manifest
            var target = Path.GetFullPath(Path.Combine(root, key.Replace('/', '\\')));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
            items.Add(new RestoreItem(key, target, entry.Size, File.Exists(target)));
        }
        return items;
    }

    private static string FreeName(string path)
    {
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        var candidate = Path.Combine(dir, $"{name} (restored){ext}");
        for (var i = 2; File.Exists(candidate); i++) candidate = Path.Combine(dir, $"{name} (restored {i}){ext}");
        return candidate;
    }

    /// <summary>Copies the planned files back. Each copy is checked against the recorded hash; one that does not match
    /// is removed again and reported, never left in place.</summary>
    public static (int restored, IReadOnlyList<string> failed) Restore(BackupJob job, IReadOnlyList<RestoreItem> items, CancellationToken ct = default)
    {
        var manifest = LoadManifest(job);
        var failed = new List<string>();
        var restored = 0;
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!manifest.Files.TryGetValue(item.Key, out var entry)) throw new InvalidDataException("not in manifest");
                var source = TargetOf(job, item.Key);
                var target = File.Exists(item.TargetPath) ? FreeName(item.TargetPath) : item.TargetPath;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                string hash;
                using (var input = File.OpenRead(source))
                using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var sha = SHA256.Create())
                {
                    var block = new byte[1 << 20];
                    int read;
                    while ((read = input.Read(block, 0, block.Length)) > 0)
                    {
                        output.Write(block, 0, read);
                        sha.TransformBlock(block, 0, read, null, 0);
                    }
                    sha.TransformFinalBlock([], 0, 0);
                    hash = Convert.ToHexString(sha.Hash!);
                }
                if (!hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(target);
                    failed.Add(item.Key);
                    continue;
                }
                try { File.SetLastWriteTimeUtc(target, new DateTime(entry.ModifiedUtcTicks, DateTimeKind.Utc)); } catch (IOException) { /* keeps the copy time */ }
                restored++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                failed.Add(item.Key);
                Log.Write($"restore failed: {ex.GetType().Name}");
            }
        }
        return (restored, failed);
    }
}
