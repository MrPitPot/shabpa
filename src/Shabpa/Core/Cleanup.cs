using System.IO;

namespace Shabpa.Core;

/// <summary>One place Windows or a program keeps files it can rebuild: temporary files, caches, thumbnails, crash dumps.
/// Only these exact folders (and for browsers only their cache folders) are ever touched: no documents, no cookies,
/// no passwords, no history.</summary>
public sealed record CleanTarget(string Root, string Pattern = "*", bool Recurse = true);

public sealed record CleanCategory(string Id, string Fa, string En, string NoteFa, string NoteEn, IReadOnlyList<CleanTarget> Targets, bool NeedsAdmin, TimeSpan MinAge)
{
    public string Name => L.T(Fa, En);
    public string Note => L.T(NoteFa, NoteEn);
}

public sealed record CleanScan(CleanCategory Category, long Bytes, int Files, bool Blocked);

public sealed record CleanResult(long FreedBytes, int Deleted, int Skipped);

public static class Cleanup
{
    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string Windows => Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    /// <summary>Cache folders of the browsers found for the current user. Profile folders are enumerated; only the cache
    /// sub-folders inside them are returned.</summary>
    internal static IEnumerable<CleanTarget> BrowserTargets(string localAppData)
    {
        var chromium = new[]
        {
            Path.Combine(localAppData, "Google", "Chrome", "User Data"),
            Path.Combine(localAppData, "Microsoft", "Edge", "User Data"),
            Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data"),
            Path.Combine(localAppData, "Vivaldi", "User Data"),
        };
        foreach (var userData in chromium)
        {
            if (!Directory.Exists(userData)) continue;
            IEnumerable<string> profiles;
            try { profiles = Directory.EnumerateDirectories(userData).Where(d => Path.GetFileName(d) is "Default" || Path.GetFileName(d).StartsWith("Profile ", StringComparison.Ordinal)).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var profile in profiles)
                foreach (var cache in new[] { "Cache", "Code Cache", "GPUCache", Path.Combine("Service Worker", "CacheStorage") })
                    yield return new CleanTarget(Path.Combine(profile, cache));
        }
        var firefox = Path.Combine(localAppData, "Mozilla", "Firefox", "Profiles");
        if (Directory.Exists(firefox))
        {
            IEnumerable<string> profiles;
            try { profiles = Directory.EnumerateDirectories(firefox).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { yield break; }
            foreach (var profile in profiles) yield return new CleanTarget(Path.Combine(profile, "cache2"));
        }
    }

    public static List<CleanCategory> Categories(string? localAppData = null, string? windows = null, string? temp = null)
    {
        var local = localAppData ?? Local;
        var win = windows ?? Windows;
        var tmp = temp ?? Path.GetTempPath().TrimEnd('\\');
        return
        [
            new("usertemp", "فایل‌های موقت من", "My temporary files", "فایل‌هایی که برنامه‌ها موقتاً ساخته‌ان. فایل‌های امروز دست نمی‌خورن.", "Files programs made temporarily. Today's files are left alone.",
                [new CleanTarget(tmp)], false, TimeSpan.FromHours(24)),
            new("wintemp", "فایل‌های موقت ویندوز", "Windows temporary files", "پوشه‌ی Temp خود ویندوز.", "Windows' own Temp folder.",
                [new CleanTarget(Path.Combine(win, "Temp"))], true, TimeSpan.FromHours(24)),
            new("wucache", "فایل‌های دانلود‌شده‌ی Windows Update", "Windows Update downloads", "بعد از نصب لازم نیستن؛ اگه لازم شد دوباره دانلود می‌شن.", "Not needed after installing; downloaded again if required.",
                [new CleanTarget(Path.Combine(win, "SoftwareDistribution", "Download"))], true, TimeSpan.FromDays(1)),
            new("shader", "کش شیدر بازی‌ها و گرافیک", "Graphics and game shader cache", "خودکار دوباره ساخته می‌شه؛ ممکنه بار اول بازی یه کم لگ بزنه.", "Rebuilt automatically; a game may stutter slightly the first time.",
                [
                    new CleanTarget(Path.Combine(local, "D3DSCache")), new CleanTarget(Path.Combine(local, "AMD", "DxCache")), new CleanTarget(Path.Combine(local, "AMD", "GLCache")),
                    new CleanTarget(Path.Combine(local, "NVIDIA", "DXCache")), new CleanTarget(Path.Combine(local, "NVIDIA", "GLCache")),
                ], false, TimeSpan.FromHours(1)),
            new("browser", "کش مرورگرها", "Browser caches", "فقط کش؛ رمزها، کوکی‌ها و تاریخچه دست نمی‌خورن. مرورگر بسته باشه بهتره.", "Cache only; passwords, cookies and history are untouched. Best with the browser closed.",
                [.. BrowserTargets(local)], false, TimeSpan.FromHours(1)),
            new("thumbs", "کش بندانگشتی و آیکون", "Thumbnail and icon cache", "ویندوز خودش دوباره می‌سازه.", "Windows rebuilds it.",
                [new CleanTarget(Path.Combine(local, "Microsoft", "Windows", "Explorer"), "thumbcache_*.db", false), new CleanTarget(Path.Combine(local, "Microsoft", "Windows", "Explorer"), "iconcache_*.db", false)],
                false, TimeSpan.FromHours(1)),
            new("dumps", "گزارش کرش‌ها", "Crash dumps", "فایل‌های گزارش خرابی برنامه‌ها و ویندوز.", "Crash report files from programs and Windows.",
                [new CleanTarget(Path.Combine(local, "CrashDumps")), new CleanTarget(Path.Combine(win, "Minidump"))], false, TimeSpan.FromDays(1)),
        ];
    }

    private static bool IsReparse(FileSystemInfo info) => (info.Attributes & FileAttributes.ReparsePoint) != 0;

    private static IEnumerable<FileInfo> Files(CleanTarget target, TimeSpan minAge, DateTime now)
    {
        if (!Directory.Exists(target.Root)) yield break;
        var root = new DirectoryInfo(target.Root);
        if (IsReparse(root)) yield break; // never follow a link out of the folder
        var stack = new Stack<DirectoryInfo>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            IEnumerable<FileSystemInfo> entries;
            try { entries = dir.EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var entry in entries)
            {
                if (IsReparse(entry)) continue;
                if (entry is DirectoryInfo d)
                {
                    if (target.Recurse) stack.Push(d);
                }
                else if (entry is FileInfo f)
                {
                    if (target.Pattern != "*" && !Matches(f.Name, target.Pattern)) continue;
                    if (now - f.LastWriteTime < minAge) continue;
                    yield return f;
                }
            }
        }
    }

    private static bool Matches(string name, string pattern)
    {
        // only "prefix*suffix" patterns are used
        var star = pattern.IndexOf('*');
        if (star < 0) return name.Equals(pattern, StringComparison.OrdinalIgnoreCase);
        return name.StartsWith(pattern[..star], StringComparison.OrdinalIgnoreCase) && name.EndsWith(pattern[(star + 1)..], StringComparison.OrdinalIgnoreCase);
    }

    public static CleanScan Scan(CleanCategory category, bool elevated, DateTime? now = null, CancellationToken ct = default)
    {
        var t = now ?? DateTime.Now;
        long bytes = 0;
        var files = 0;
        foreach (var target in category.Targets)
            foreach (var f in Files(target, category.MinAge, t))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    bytes += f.Length;
                    files++;
                }
                catch (IOException) { /* gone meanwhile */ }
            }
        return new CleanScan(category, bytes, files, category.NeedsAdmin && !elevated);
    }

    /// <summary>Deletes the files of one category (those old enough and not in use). Empty folders left behind inside the
    /// category folder are removed; the category folder itself stays.</summary>
    public static CleanResult Clean(CleanCategory category, DateTime? now = null, CancellationToken ct = default)
    {
        var t = now ?? DateTime.Now;
        long freed = 0;
        int deleted = 0, skipped = 0;
        foreach (var target in category.Targets)
        {
            foreach (var f in Files(target, category.MinAge, t).ToList())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var size = f.Length;
                    f.Delete();
                    freed += size;
                    deleted++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    skipped++; // in use or not allowed: left alone
                }
            }
            if (target.Pattern == "*" && target.Recurse) RemoveEmptyFolders(target.Root);
        }
        return new CleanResult(freed, deleted, skipped);
    }

    private static void RemoveEmptyFolders(string root)
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).ToList())
            {
                if ((new DirectoryInfo(dir).Attributes & FileAttributes.ReparsePoint) != 0) continue;
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* in use */ }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* cannot list: leave it */ }
    }
}
