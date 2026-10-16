using System.IO;
using Shabpa.Core;

namespace Shabpa.Tests;

public class CleanupTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0);

    private static void Old(string path, int hoursOld = 100) => File.SetLastWriteTime(path, Now.AddHours(-hoursOld));

    private static (CleanCategory[] all, string temp, string local, string win) Layout(TempTree t)
    {
        var temp = t.Sub("temp");
        var local = t.Sub("local");
        var win = t.Sub("win");
        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(local);
        Directory.CreateDirectory(win);
        return ([.. Cleanup.Categories(local, win, temp)], temp, local, win);
    }

    [Fact]
    public void Old_temp_files_are_cleaned_and_fresh_ones_are_kept()
    {
        using var t = new TempTree();
        var (all, temp, _, _) = Layout(t);
        var oldFile = t.WriteBytes("temp/old.tmp", 1000);
        var freshFile = t.WriteBytes("temp/fresh.tmp", 500);
        var nested = t.WriteBytes("temp/sub/deep/old2.tmp", 300);
        Old(oldFile);
        Old(nested);
        File.SetLastWriteTime(freshFile, Now.AddMinutes(-5));
        var category = all.First(c => c.Id == "usertemp");

        var scan = Cleanup.Scan(category, elevated: false, Now);
        Assert.Equal(1300, scan.Bytes);
        Assert.Equal(2, scan.Files);

        var result = Cleanup.Clean(category, Now);
        Assert.Equal(1300, result.FreedBytes);
        Assert.False(File.Exists(oldFile));
        Assert.False(File.Exists(nested));
        Assert.True(File.Exists(freshFile));
        Assert.True(Directory.Exists(temp));           // the folder itself stays
        Assert.False(Directory.Exists(t.Sub("temp/sub"))); // emptied sub-folders go
    }

    [Fact]
    public void Admin_only_categories_are_reported_as_blocked_without_admin()
    {
        using var t = new TempTree();
        var (all, _, _, win) = Layout(t);
        Directory.CreateDirectory(Path.Combine(win, "Temp"));
        File.WriteAllText(Path.Combine(win, "Temp", "x.tmp"), "x");
        Old(Path.Combine(win, "Temp", "x.tmp"));
        var wintemp = all.First(c => c.Id == "wintemp");
        Assert.True(Cleanup.Scan(wintemp, elevated: false, Now).Blocked);
        Assert.False(Cleanup.Scan(wintemp, elevated: true, Now).Blocked);
    }

    [Fact]
    public void Browser_cleaning_touches_only_cache_folders_never_cookies_or_history()
    {
        using var t = new TempTree();
        var (_, _, local, _) = Layout(t);
        var profile = Path.Combine(local, "Google", "Chrome", "User Data", "Default");
        var cache = t.WriteBytes(Path.Combine("local", "Google", "Chrome", "User Data", "Default", "Cache", "Cache_Data", "f_000001"), 2000);
        var code = t.WriteBytes(Path.Combine("local", "Google", "Chrome", "User Data", "Default", "Code Cache", "js", "a"), 500);
        var cookies = t.Write(Path.Combine("local", "Google", "Chrome", "User Data", "Default", "Cookies"), "cookies");
        var history = t.Write(Path.Combine("local", "Google", "Chrome", "User Data", "Default", "History"), "history");
        var logins = t.Write(Path.Combine("local", "Google", "Chrome", "User Data", "Default", "Login Data"), "passwords");
        foreach (var f in new[] { cache, code, cookies, history, logins }) Old(f);
        Assert.True(Directory.Exists(profile));

        // browser profiles are looked up when the categories are built, so build them after the files exist
        var category = Cleanup.Categories(local, t.Sub("win"), t.Sub("temp")).First(c => c.Id == "browser");
        Assert.NotEmpty(category.Targets);
        var result = Cleanup.Clean(category, Now);
        Assert.Equal(2500, result.FreedBytes);
        Assert.True(File.Exists(cookies));
        Assert.True(File.Exists(history));
        Assert.True(File.Exists(logins));
    }

    [Fact]
    public void Thumbnail_cleaning_only_matches_the_cache_file_names()
    {
        using var t = new TempTree();
        var (all, _, _, _) = Layout(t);
        var thumb = t.Write(Path.Combine("local", "Microsoft", "Windows", "Explorer", "thumbcache_256.db"), "t");
        var other = t.Write(Path.Combine("local", "Microsoft", "Windows", "Explorer", "important.docx"), "doc");
        Old(thumb);
        Old(other);
        var result = Cleanup.Clean(all.First(c => c.Id == "thumbs"), Now);
        Assert.Equal(1, result.Deleted);
        Assert.False(File.Exists(thumb));
        Assert.True(File.Exists(other));
    }

    [Fact]
    public void A_link_inside_a_cache_folder_is_not_followed()
    {
        using var t = new TempTree();
        var (all, _, _, _) = Layout(t);
        var outside = t.Write("outside/precious.txt", "keep");
        Old(outside);
        var linkPath = t.Sub("temp/link");
        try
        {
            Directory.CreateSymbolicLink(linkPath, t.Sub("outside"));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return; // creating links needs a privilege on this machine; nothing to check
        }
        Cleanup.Clean(all.First(c => c.Id == "usertemp"), Now);
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void Files_in_use_are_skipped_not_forced()
    {
        using var t = new TempTree();
        var (all, _, _, _) = Layout(t);
        var locked = t.WriteBytes("temp/locked.tmp", 100);
        Old(locked);
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = Cleanup.Clean(all.First(c => c.Id == "usertemp"), Now);
            Assert.Equal(1, result.Skipped);
            Assert.Equal(0, result.Deleted);
        }
        Assert.True(File.Exists(locked));
    }

    [Fact]
    public void Every_cleaning_root_is_a_cache_or_temp_location_never_a_user_folder()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var forbidden = new[] { "Documents", "Desktop", "Pictures", "Videos", "Music", "Downloads" }
            .Select(n => Path.Combine(home, n)).ToList();
        foreach (var c in Cleanup.Categories())
            foreach (var target in c.Targets)
                foreach (var f in forbidden)
                    Assert.False(target.Root.StartsWith(f, StringComparison.OrdinalIgnoreCase), $"{c.Id}: {target.Root}");
        // drive roots are never targets either
        Assert.All(Cleanup.Categories().SelectMany(c => c.Targets), x => Assert.True(x.Root.Length > 3));
    }
}
