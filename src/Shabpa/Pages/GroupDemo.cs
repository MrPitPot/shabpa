using System.IO;
using Shabpa.Core;

namespace Shabpa.Pages;

/// <summary>Plausible sample data for the pages that start empty (disk space, duplicates, clean-up, backups). Only used when the
/// app runs with <c>--demo</c> / <c>--screenshots</c> (<see cref="Hub.Demo"/>); nothing here is ever used in a normal run.</summary>
internal static class GroupDemo
{
    private static FileEntry F(string path, long mb, int daysAgo) => new(path, mb << 20, DateTime.Now.AddDays(-daysAgo));

    public static FolderReport Space()
    {
        const string root = @"C:\Users\Demo\Downloads";
        List<FolderEntry> folders =
        [
            new(root + @"\Games", 48L << 30, 5120), new(root + @"\Videos", 21L << 30, 140), new(root + @"\Installers", 9L << 30, 64),
            new(root + @"\Courses", 6L << 30, 380), new(root + @"\Music", 3L << 30, 910), new(root + @"\Documents", 900L << 20, 260),
        ];
        List<FileEntry> files =
        [
            F(root + @"\Games\open-world-pack.iso", 18432, 21), F(root + @"\Videos\conference-2025.mkv", 6144, 77), F(root + @"\Installers\dev-tools-setup.exe", 2310, 9),
            F(root + @"\Courses\algorithms-week-3.mp4", 1480, 40), F(root + @"\Games\texture-pack-hd.zip", 1210, 21), F(root + @"\Music\album-collection.zip", 640, 220),
            F(root + @"\Documents\scan-archive.pdf", 215, 120),
        ];
        return new FolderReport(root, folders.Sum(x => x.Bytes), 8341, 2, folders, files);
    }

    public static List<DuplicateGroup> Duplicates() =>
    [
        new(1480L << 20, [F(@"C:\Users\Demo\Downloads\algorithms-week-3.mp4", 1480, 300), F(@"C:\Users\Demo\Videos\Courses\algorithms-week-3.mp4", 1480, 40), F(@"D:\Backup\algorithms-week-3 (1).mp4", 1480, 12)]),
        new(212L << 20, [F(@"C:\Users\Demo\Documents\scan-archive.pdf", 212, 200), F(@"C:\Users\Demo\Desktop\scan-archive.pdf", 212, 10)]),
        new(64L << 20, [F(@"C:\Users\Demo\Pictures\trip-album.zip", 64, 400), F(@"C:\Users\Demo\Downloads\trip-album.zip", 64, 80)]),
    ];

    public static List<CleanScan> CleanupScans()
    {
        long[] bytes = [3200L << 20, 1450L << 20, 820L << 20, 410L << 20, 260L << 20, 96L << 20, 0, 12L << 20];
        int[] files = [4210, 1380, 960, 410, 220, 90, 0, 14];
        var elevated = Hub.Elevated;
        return [.. Core.Cleanup.Categories().Select((c, i) => new CleanScan(c, bytes[i % bytes.Length], files[i % files.Length], c.NeedsAdmin && !elevated))];
    }

    public static List<BackupJob> Backups()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var target = Path.Combine(Path.GetTempPath(), "ShabpaDemoBackup");
        return
        [
            new BackupJob
            {
                Name = L.T("اسناد", "Documents"), Sources = [docs], Destination = target, Frequency = BackupFrequency.Daily, Hour = 22, Minute = 30,
                History = [new BackupRunLog { Time = DateTime.Now.AddHours(-9), Ok = true, Files = 214, Bytes = 1_300_000_000 }],
            },
            new BackupJob
            {
                Name = L.T("عکس‌ها", "Photos"), Sources = [pictures], Destination = target, Frequency = BackupFrequency.Weekly, Day = DayOfWeek.Friday, Hour = 21,
                History = [new BackupRunLog { Time = DateTime.Now.AddDays(-3), Ok = false, Files = 12, Bytes = 90_000_000, Note = L.T("چند فایل در حال استفاده بود", "a few files were in use") }],
            },
        ];
    }
}
