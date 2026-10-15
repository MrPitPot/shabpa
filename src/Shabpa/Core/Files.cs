using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Shabpa.Core;

public sealed record FileEntry(string Path, long Size, DateTime Modified);

public sealed record FolderEntry(string Path, long Bytes, int Files);

/// <summary>What a folder analysis found: the biggest sub-folders and files. Nothing is changed by looking.</summary>
public sealed record FolderReport(string Root, long TotalBytes, int FileCount, int SkippedFolders, IReadOnlyList<FolderEntry> Folders, IReadOnlyList<FileEntry> Largest);

public sealed record DuplicateGroup(long Size, IReadOnlyList<FileEntry> Files)
{
    /// <summary>What would be freed by keeping one copy.</summary>
    public long Reclaimable => Size * (Files.Count - 1);
}

public static class FileTools
{
    /// <summary>Folders that are never offered for scanning results to be deleted from: parts of Windows and programs.</summary>
    public static bool IsProtectedPath(string path)
    {
        string full;
        try { full = Path.GetFullPath(path).TrimEnd('\\', '/'); }
        catch { return true; }
        if (full.Length <= 3) return true; // a drive root
        var protectedRoots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
        }.Where(p => p.Length > 0).Select(p => p.TrimEnd('\\'));
        foreach (var root in protectedRoots)
            if (full.Equals(root, StringComparison.OrdinalIgnoreCase) || full.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)) return true;
        // the user's own AppData holds programs' settings; offering those for deletion is never a good idea.
        // The temp folder inside it is the one place where clearing big leftovers makes sense.
        var appData = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        if (appData is not null && (full.Equals(appData, StringComparison.OrdinalIgnoreCase) || full.StartsWith(appData.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)))
        {
            var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\') + "\\";
            return !(full + "\\").StartsWith(temp, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    private static bool IsReparse(FileSystemInfo info) => (info.Attributes & FileAttributes.ReparsePoint) != 0;

    private static readonly EnumerationOptions Safe = new()
    {
        IgnoreInaccessible = true, RecurseSubdirectories = false, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
        ReturnSpecialDirectories = false,
    };

    /// <summary>Walks a folder and measures it. Skips links (so nothing is counted twice or escapes the folder) and
    /// whatever cannot be read. Cancellable.</summary>
    public static FolderReport Analyze(string root, int topFiles = 40, int topFolders = 30, CancellationToken ct = default, Action<int>? progress = null)
    {
        var rootInfo = new DirectoryInfo(root);
        var largest = new List<FileEntry>();
        var folders = new List<FolderEntry>();
        long total = 0;
        var count = 0;
        var seen = 0; // for progress only
        var skipped = 0;

        void Consider(FileEntry file)
        {
            if (largest.Count < topFiles || file.Size > largest[^1].Size)
            {
                var i = largest.FindIndex(f => f.Size < file.Size);
                if (i < 0) largest.Add(file);
                else largest.Insert(i, file);
                if (largest.Count > topFiles) largest.RemoveAt(largest.Count - 1);
            }
        }

        // returns (bytes, files) of everything below dir
        (long, int) Walk(DirectoryInfo dir)
        {
            ct.ThrowIfCancellationRequested();
            long bytes = 0;
            var files = 0;
            try
            {
                foreach (var entry in dir.EnumerateFileSystemInfos("*", Safe))
                {
                    ct.ThrowIfCancellationRequested();
                    if (entry is FileInfo f)
                    {
                        bytes += f.Length;
                        files++;
                        Consider(new FileEntry(f.FullName, f.Length, f.LastWriteTime));
                        if (++seen % 2000 == 0) progress?.Invoke(seen);
                    }
                    else if (entry is DirectoryInfo d)
                    {
                        var (b, n) = Walk(d);
                        bytes += b;
                        files += n;
                    }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                skipped++;
            }
            return (bytes, files);
        }

        try
        {
            foreach (var entry in rootInfo.EnumerateFileSystemInfos("*", Safe))
            {
                ct.ThrowIfCancellationRequested();
                if (entry is FileInfo f)
                {
                    total += f.Length;
                    count++;
                    Consider(new FileEntry(f.FullName, f.Length, f.LastWriteTime));
                }
                else if (entry is DirectoryInfo d)
                {
                    var (b, n) = Walk(d);
                    total += b;
                    count += n;
                    folders.Add(new FolderEntry(d.FullName, b, n));
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            skipped++;
        }
        return new FolderReport(root, total, count, skipped, [.. folders.OrderByDescending(f => f.Bytes).Take(topFolders)], largest);
    }

    /// <summary>Files with identical content. Size first, then a hash of the start and end, then the whole file, so only
    /// real candidates are read completely. Links and unreadable files are ignored.</summary>
    public static List<DuplicateGroup> FindDuplicates(IEnumerable<string> roots, long minSize = 1 << 20, CancellationToken ct = default, Action<string>? progress = null)
    {
        var bySize = new Dictionary<long, List<FileInfo>>();
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
            };
            foreach (var path in Directory.EnumerateFiles(root, "*", options))
            {
                ct.ThrowIfCancellationRequested();
                FileInfo info;
                try { info = new FileInfo(path); }
                catch { continue; }
                if (info.Length < minSize) continue;
                if (!bySize.TryGetValue(info.Length, out var list)) bySize[info.Length] = list = [];
                if (!list.Any(f => string.Equals(f.FullName, info.FullName, StringComparison.OrdinalIgnoreCase))) list.Add(info);
            }
        }

        var groups = new List<DuplicateGroup>();
        foreach (var (size, files) in bySize)
        {
            if (files.Count < 2) continue;
            ct.ThrowIfCancellationRequested();
            var byQuick = files.GroupBy(f => Hash(f.FullName, size, quick: true, ct)).Where(g => g.Key is not null && g.Count() > 1);
            foreach (var quick in byQuick)
            {
                progress?.Invoke(quick.First().Name);
                foreach (var full in quick.GroupBy(f => Hash(f.FullName, size, quick: false, ct)).Where(g => g.Key is not null && g.Count() > 1))
                    groups.Add(new DuplicateGroup(size, [.. full.Select(f => new FileEntry(f.FullName, f.Length, f.LastWriteTime)).OrderBy(f => f.Modified)]));
            }
        }
        return [.. groups.OrderByDescending(g => g.Reclaimable)];
    }

    private static string? Hash(string path, long size, bool quick, CancellationToken ct)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
            using var sha = SHA256.Create();
            if (quick && size > 1 << 17)
            {
                var buffer = new byte[1 << 16];
                var n = stream.Read(buffer, 0, buffer.Length);
                sha.TransformBlock(buffer, 0, n, null, 0);
                stream.Seek(-(1 << 16), SeekOrigin.End);
                n = stream.Read(buffer, 0, buffer.Length);
                sha.TransformFinalBlock(buffer, 0, n);
                return Convert.ToHexString(sha.Hash!);
            }
            var block = new byte[1 << 20];
            int read;
            while ((read = stream.Read(block, 0, block.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                sha.TransformBlock(block, 0, read, null, 0);
            }
            sha.TransformFinalBlock([], 0, 0);
            return Convert.ToHexString(sha.Hash!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>The only way Shabpa removes a file the person picked: into the Recycle Bin, so it can be put back.</summary>
public static class Recycler
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOp
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
    private static extern int SHFileOperation(ref ShFileOp op);

    private const uint FoDelete = 3;
    private const ushort FofSilent = 0x4, FofNoConfirmation = 0x10, FofAllowUndo = 0x40, FofNoErrorUi = 0x400;

    /// <summary>Why this path must not be deleted, or null if it may be. Only existing regular files outside protected
    /// folders, and (when given) only inside the folders that were scanned.</summary>
    public static string? Refusal(string path, IEnumerable<string>? allowedRoots = null)
    {
        string full;
        try { full = Path.GetFullPath(path); }
        catch { return "invalid path"; }
        if (full.Contains('*') || full.Contains('?')) return "wildcards are not allowed"; // SHFileOperation would expand them
        if (FileTools.IsProtectedPath(full)) return "protected location";
        FileInfo info;
        try { info = new FileInfo(full); }
        catch { return "invalid path"; }
        if (!info.Exists) return "no longer exists";
        if ((info.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0) return "not a regular file";
        if (allowedRoots is not null)
        {
            var inside = allowedRoots.Any(r =>
            {
                var root = Path.GetFullPath(r).TrimEnd('\\') + "\\";
                return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
            });
            if (!inside) return "outside the scanned folders";
        }
        return null;
    }

    /// <summary>Moves each file to the Recycle Bin. Returns what was done per path; never throws for a single file.</summary>
    public static List<(string path, bool ok, string? error)> Send(IEnumerable<string> paths, IEnumerable<string>? allowedRoots = null)
    {
        var roots = allowedRoots?.ToList();
        var results = new List<(string, bool, string?)>();
        foreach (var path in paths)
        {
            var refusal = Refusal(path, roots);
            if (refusal is not null)
            {
                results.Add((path, false, refusal));
                continue;
            }
            var op = new ShFileOp { wFunc = FoDelete, pFrom = Path.GetFullPath(path) + "\0\0", fFlags = FofAllowUndo | FofNoConfirmation | FofSilent | FofNoErrorUi };
            var code = SHFileOperation(ref op);
            results.Add((path, code == 0 && !op.fAnyOperationsAborted, code == 0 && !op.fAnyOperationsAborted ? null : $"error {code}"));
        }
        return results;
    }
}
