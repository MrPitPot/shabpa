using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>«فایل و پشتیبان»: where the disk space went, duplicate files, and scheduled folder backups.
/// Nothing is deleted without a preview and an explicit confirmation, and deleted files go to the Recycle Bin.</summary>
public sealed class FilesPage : TabsPage
{
    public static readonly string[] TabKeys = ["space", "duplicates", "cleanup", "backup"];

    public override string PageTitle => L.T("فایل و پشتیبان", "Files & backup");

    public FilesPage()
    {
        Add("space", L.T("فضای دیسک", "Disk space"), new SpacePage());
        Add("duplicates", L.T("فایل‌های تکراری", "Duplicates"), new DuplicatesPage());
        Add("cleanup", L.T("پاک‌سازی", "Clean-up"), new CleanupPage());
        Add("backup", L.T("پشتیبان‌گیری", "Backup"), new BackupPage());
    }
}

internal static class FolderPick
{
    public static string? Choose(string? start = null, string? description = null)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = description ?? L.T("یه پوشه انتخاب کن", "Choose a folder"), UseDescriptionForTitle = true, ShowNewFolderButton = true,
            InitialDirectory = start is not null && Directory.Exists(start) ? start : "",
        };
        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    public static List<(string name, string path)> Quick()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var list = new List<(string, string)>
        {
            (L.T("دانلودها", "Downloads"), Path.Combine(home, "Downloads")),
            (L.T("اسناد", "Documents"), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            (L.T("دسکتاپ", "Desktop"), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            (L.T("تصاویر", "Pictures"), Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
            (L.T("ویدیوها", "Videos"), Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
            (L.T("موسیقی", "Music"), Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)),
        };
        return [.. list.Where(x => x.Item2.Length > 0 && Directory.Exists(x.Item2))];
    }

    public static void ShowInExplorer(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false }); }
        catch (Exception ex) { Log.Write($"explorer: {ex.Message}"); }
    }

    /// <summary>The button that opens a path in Explorer (icon only, with a tooltip).</summary>
    public static Button ExplorerButton(string path) =>
        ListUi.IconBtn("folder", L.T("نمایش توی اکسپلورر", "Show in Explorer"), () => ShowInExplorer(path));
}

/// <summary>Where the space went in a folder: the biggest sub-folders and files.</summary>
public sealed class SpacePage : PageBase
{
    private readonly WrapPanel _quick = new();
    private readonly TextBlock _status = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly StackPanel _folders = new();
    private readonly StackPanel _files = new();
    private readonly Button _cancel = new() { Style = St("Btn"), Content = L.T("توقف", "Stop"), Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 8, 8) };
    private readonly Button _delete = new() { Style = St("BtnDanger"), Visibility = Visibility.Collapsed, Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Dictionary<string, CheckBox> _checks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _sizes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Grid _grid = ListUi.PageGrid(3);
    private Border _pickCard = null!, _foldersCard = null!, _filesCard = null!;
    private bool? _wide;
    private bool _seeded;
    private CancellationTokenSource? _cts;
    private string _root = "";

    public override string PageTitle => L.T("فایل و پشتیبان", "Files & backup");
    public override string PageSubtitle => L.T("ببین فضای دیسک کجا رفته. فقط نگاه می‌کنه؛ هیچ فایلی بدون پیش‌نمایش و تأیید تو حذف نمی‌شه.",
        "See where the disk space went. It only looks; nothing is deleted without a preview and your confirmation.");

    public SpacePage()
    {
        var (card, body) = Ui2.Card(L.T("یه پوشه انتخاب کن", "Pick a folder"));
        _quick.Margin = new Thickness(0, 14, 0, 0);
        foreach (var (name, path) in FolderPick.Quick())
        {
            var p = path;
            var b = ListUi.Btn("folder", name, "Btn", () => Scan(p));
            b.Margin = new Thickness(0, 0, 8, 8);
            _quick.Children.Add(b);
        }
        var other = ListUi.Btn("plus", L.T("پوشه‌ی دیگه…", "Other folder…"), "Btn", () =>
        {
            if (FolderPick.Choose() is { } p) Scan(p);
        });
        other.Margin = new Thickness(0, 0, 8, 8);
        _quick.Children.Add(other);
        // IsReady asks the drive itself: a disk that has gone to sleep takes seconds to answer, so the list is read on a worker and the
        // buttons are added when it is known (the folder shortcuts above are there at once)
        _ = Task.Run(() =>
        {
            try
            {
                var drives = (DriveReader ?? ReadDrives)();
                Dispatcher.BeginInvoke(() => AddDrives(drives));
            }
            catch (Exception ex) { Log.Write($"drive list: {ex.GetType().Name}: {ex.Message}"); }
        });
        body.Children.Add(_quick);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        _cancel.Click += (_, _) => _cts?.Cancel();
        row.Children.Add(_cancel);
        body.Children.Add(row);
        body.Children.Add(_status);
        _pickCard = card;

        var (fCard, f) = Ui2.Card(L.T("بزرگ‌ترین پوشه‌ها", "Biggest folders"));
        _folders.Margin = new Thickness(0, 8, 0, 0);
        f.Children.Add(_folders);
        _foldersCard = fCard;
        var (bCard, b2) = Ui2.Card(L.T("بزرگ‌ترین فایل‌ها", "Biggest files"), L.T("تیک بزن و به سطل بازیافت بفرست؛ از اونجا برمی‌گردن.", "Tick files and send them to the Recycle Bin; they can be restored from there."));
        b2.Children.Add(_files);
        _files.Margin = new Thickness(0, 6, 0, 0);
        _delete.Click += (_, _) => DeleteSelected();
        b2.Children.Add(_delete);
        _filesCard = bCard;

        foreach (var c in new[] { _pickCard, _foldersCard, _filesCard })
        {
            c.Margin = new Thickness(8);
            _grid.Children.Add(c);
        }
        _foldersCard.Visibility = _filesCard.Visibility = Visibility.Collapsed; // appear after the first scan
        Content = _grid;
        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width > 900);
        ApplyLayout(true);
    }

    private void ApplyLayout(bool wide)
    {
        if (_wide == wide) return;
        _wide = wide;
        while (_grid.RowDefinitions.Count < 3) _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        ListUi.Place(_pickCard, 0, 0, 12);
        if (wide)
        {
            ListUi.Place(_foldersCard, 1, 0, 5);
            ListUi.Place(_filesCard, 1, 5, 7);
        }
        else
        {
            ListUi.Place(_foldersCard, 1, 0, 12);
            ListUi.Place(_filesCard, 2, 0, 12);
        }
    }

    public override void Refresh()
    {
        if (Hub.Demo && !_seeded)
        {
            _seeded = true;
            Show(GroupDemo.Space());
        }
    }

    /// <summary>Test aid: replaces the drive list read.</summary>
    internal static Func<List<(string root, string name)>>? DriveReader { get; set; }

    private static List<(string root, string name)> ReadDrives() =>
        DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => (root: d.RootDirectory.FullName, name: d.Name)).ToList();

    internal int QuickButtons => _quick.Children.Count;

    private void AddDrives(List<(string root, string name)> drives)
    {
        foreach (var (root, name) in drives)
        {
            var p = root;
            var b = ListUi.Btn("mon", L.T("درایو", "Drive"), "Btn", () => Scan(p));
            // the letter and its colon are their own left-to-right run, so the colon cannot jump to the wrong side in Persian
            if (b.Content is StackPanel driveRow)
                driveRow.Children.Add(new TextBlock { Text = name.TrimEnd((char)92), FlowDirection = FlowDirection.LeftToRight, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
            b.Margin = new Thickness(0, 0, 8, 8);
            _quick.Children.Add(b);
        }
    }

    private void Scan(string path)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _root = path;
        _folders.Children.Clear();
        _files.Children.Clear();
        _checks.Clear();
        _sizes.Clear();
        _delete.Visibility = Visibility.Collapsed;
        _cancel.Visibility = Visibility.Visible;
        _status.Foreground = Res("Muted");
        Mo.Say(_status, L.T("در حال بررسی…", "Scanning…"));
        _ = Task.Run(() =>
        {
            try
            {
                var report = FileTools.Analyze(path, ct: token, progress: n => Dispatcher.BeginInvoke(() => _status.Text = L.T($"در حال بررسی… {Fa.Digits(n.ToString())} فایل", $"Scanning… {n} files")));
                Dispatcher.BeginInvoke(() => Show(report));
            }
            catch (OperationCanceledException)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    Mo.Say(_status, L.T("متوقف شد.", "Stopped."));
                    _cancel.Visibility = Visibility.Collapsed;
                });
            }
            catch (Exception ex)
            {
                Log.Write($"space scan: {ex.GetType().Name}: {ex.Message}");
                Dispatcher.BeginInvoke(() =>
                {
                    Mo.Say(_status, L.T("بررسی انجام نشد.", "The scan failed."));
                    _cancel.Visibility = Visibility.Collapsed;
                });
            }
        });
    }

    private void Show(FolderReport report)
    {
        _root = report.Root;
        Mo.Show(_foldersCard, true, 20, 0.7);
        Mo.Show(_filesCard, true, 20, 0.7);
        _cancel.Visibility = Visibility.Collapsed;
        var folderMemo = new Mo.Memo(_folders);
        var fileMemo = new Mo.Memo(_files);
        Mo.Say(_status, L.T($"{report.Root}: {Fa.Bytes(report.TotalBytes)} در {Fa.Digits(report.FileCount.ToString())} فایل", $"{report.Root}: {Fa.Bytes(report.TotalBytes)} in {report.FileCount} files")
            + (report.SkippedFolders > 0 ? L.T($" ({Fa.Digits(report.SkippedFolders.ToString())} پوشه خونده نشد)", $" ({report.SkippedFolders} folder(s) could not be read)") : ""));
        var max = Math.Max(1, report.Folders.Count == 0 ? 1 : report.Folders[0].Bytes);
        _folders.Children.Clear();
        var folderIndex = 0;
        foreach (var folder in report.Folders)
        {
            var grid = new Grid { Margin = new Thickness(0, 7, 0, 7) };
            // the size column has one width for every row so the bars start and end at the same place
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
            var left = new StackPanel();
            left.Children.Add(new TextBlock { Text = Path.GetFileName(folder.Path), FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Res("Text"), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = folder.Path });
            var bar = new ProgressBar { Style = St("Bar"), Minimum = 0, Maximum = 1, Value = (double)folder.Bytes / max, Height = 6, Margin = new Thickness(0, 6, 0, 0) };
            left.Children.Add(bar);
            Mo.Fill(bar, 0, 650 + folderIndex++ * 90);
            grid.Children.Add(left);
            var size = new TextBlock { Text = Fa.Bytes(folder.Bytes), Style = St("Label"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(14, 0, 0, 0), FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left };
            Grid.SetColumn(size, 1);
            grid.Children.Add(size);
            _folders.Children.Add(folderMemo.Item(folder.Path, grid));
        }
        if (report.Folders.Count == 0) _folders.Children.Add(new TextBlock { Text = L.T("زیرپوشه‌ای نیست.", "No sub-folders."), Style = St("Body") });

        _files.Children.Clear();
        _checks.Clear();
        var firstFile = true;
        foreach (var file in report.Largest)
        {
            _sizes[file.Path] = file.Size;
            var check = new CheckBox { Style = St("Check") };
            check.Click += (_, _) => UpdateDelete();
            _checks[file.Path] = check;
            var item = ListUi.CheckItem(check, Path.GetFileName(file.Path),
                $"{Fa.Bytes(file.Size)}{ListUi.Sep}{Fa.Date(file.Modified)}{ListUi.Sep}\u200E{Path.GetDirectoryName(file.Path)}\u200E", FolderPick.ExplorerButton(file.Path), firstFile);
            item.ToolTip = file.Path;
            _files.Children.Add(fileMemo.Item(file.Path, item));
            firstFile = false;
        }
        UpdateDelete();
    }

    private List<string> Selected() => [.. _checks.Where(c => c.Value.IsChecked == true).Select(c => c.Key)];

    private void UpdateDelete()
    {
        var chosen = Selected();
        Mo.Show(_delete, chosen.Count > 0, 8, 0.4);
        ListUi.SetContent(_delete, "trash", L.T($"فرستادن {Fa.Digits(chosen.Count.ToString())} فایل ({Fa.Bytes(chosen.Sum(p => _sizes[p]))}) به سطل بازیافت",
            $"Send {chosen.Count} file(s) ({Fa.Bytes(chosen.Sum(p => _sizes[p]))}) to the Recycle Bin"));
    }

    private void DeleteSelected()
    {
        var chosen = Selected();
        if (chosen.Count == 0) return;
        var preview = string.Join("\n", chosen.Take(12).Select(p => "• " + p)) + (chosen.Count > 12 ? L.T($"\n… و {Fa.Digits((chosen.Count - 12).ToString())} فایل دیگه", $"\n… and {chosen.Count - 12} more") : "");
        if (!ConfirmWindow.AskAlways(L.T("فرستادن به سطل بازیافت", "Send to the Recycle Bin"),
                L.T($"{Fa.Digits(chosen.Count.ToString())} فایل با حجم {Fa.Bytes(chosen.Sum(p => _sizes[p]))} به سطل بازیافت می‌ره و از اونجا قابل برگردوندنه.",
                    $"{chosen.Count} file(s), {Fa.Bytes(chosen.Sum(p => _sizes[p]))}, go to the Recycle Bin and can be restored from there."),
                L.T("بفرست", "Send"), danger: true, detail: preview)) return;
        var results = Recycler.Send(chosen, [_root]);
        var ok = results.Count(r => r.ok);
        var failed = results.Where(r => !r.ok).ToList();
        Hub.Notify(L.T("سطل بازیافت", "Recycle Bin"),
            L.T($"{Fa.Digits(ok.ToString())} فایل فرستاده شد." + (failed.Count > 0 ? $" {Fa.Digits(failed.Count.ToString())} فایل نشد." : ""),
                $"{ok} file(s) sent." + (failed.Count > 0 ? $" {failed.Count} could not be moved." : "")));
        Scan(_root);
    }
}

/// <summary>Files with identical content in the chosen folder.</summary>
public sealed class DuplicatesPage : PageBase
{
    private readonly WrapPanel _quick = new();
    private readonly TextBlock _status = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly StackPanel _groups = new();
    private readonly Button _delete = new() { Style = St("BtnDanger"), Visibility = Visibility.Collapsed, Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Button _pickOld = new() { Style = St("Btn"), Content = L.T("همه رو جز قدیمی‌ترین نسخه‌ی هر گروه علامت بزن", "Tick all but the oldest copy of each group"), Visibility = Visibility.Collapsed, Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Button _cancel = new() { Style = St("Btn"), Content = L.T("توقف", "Stop"), Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
    private readonly List<(DuplicateGroup group, List<(FileEntry file, CheckBox box)> items)> _shown = [];
    private readonly Grid _grid = ListUi.PageGrid(2);
    private Border _groupsCard = null!;
    private bool _seeded;
    private CancellationTokenSource? _cts;
    private long _minSize = 1 << 20;
    private string _root = "";

    public override string PageTitle => L.T("فایل و پشتیبان", "Files & backup");
    public override string PageSubtitle => L.T("فایل‌هایی که دقیقاً یه محتوا دارن. از هر گروه همیشه یه نسخه می‌مونه و حذف فقط با تأیید خودت و به سطل بازیافت انجام می‌شه.",
        "Files with exactly the same content. One copy of each group always stays, and deleting only happens with your confirmation, into the Recycle Bin.");

    public DuplicatesPage()
    {
        var (card, body) = Ui2.Card(L.T("کجا بگردم؟", "Where to look?"));
        _quick.Margin = new Thickness(0, 14, 0, 0);
        foreach (var (name, path) in FolderPick.Quick())
        {
            var p = path;
            var b = ListUi.Btn("folder", name, "Btn", () => Scan(p));
            b.Margin = new Thickness(0, 0, 8, 8);
            _quick.Children.Add(b);
        }
        var other = ListUi.Btn("plus", L.T("پوشه‌ی دیگه…", "Other folder…"), "Btn", () =>
        {
            if (FolderPick.Choose() is { } p) Scan(p);
        });
        other.Margin = new Thickness(0, 0, 8, 8);
        _quick.Children.Add(other);
        body.Children.Add(_quick);
        body.Children.Add(Ui2.Label(L.T("فقط فایل‌های بزرگ‌تر از", "Only files bigger than"), 4, 6));
        var sizes = new[] { ("1 MB", 1L << 20), ("10 MB", 10L << 20), ("100 MB", 100L << 20) };
        var sizeSeg = new Segmented([.. sizes.Select(s => "\u200E" + s.Item1 + "\u200E")], 0);
        sizeSeg.Changed += i => _minSize = sizes[i].Item2;
        body.Children.Add(sizeSeg);
        _cancel.Click += (_, _) => _cts?.Cancel();
        body.Children.Add(_cancel);
        body.Children.Add(_status);
        card.Margin = new Thickness(8);
        _grid.Children.Add(card);
        ListUi.Place(card, 0, 0, 12);

        var (gCard, g) = Ui2.Card(L.T("گروه‌های تکراری", "Duplicate groups"));
        _groups.Margin = new Thickness(0, 4, 0, 0);
        g.Children.Add(_groups);
        _pickOld.Click += (_, _) =>
        {
            foreach (var (_, items) in _shown)
                for (var i = 0; i < items.Count; i++) items[i].box.IsChecked = i != 0; // sorted oldest first
            UpdateDelete();
        };
        g.Children.Add(_pickOld);
        _delete.Click += (_, _) => DeleteSelected();
        g.Children.Add(_delete);
        gCard.Margin = new Thickness(8);
        gCard.Visibility = Visibility.Collapsed; // appears once a search has finished
        _groupsCard = gCard;
        _grid.Children.Add(gCard);
        ListUi.Place(gCard, 1, 0, 12);
        Content = _grid;
    }

    public override void Refresh()
    {
        if (Hub.Demo && !_seeded)
        {
            _seeded = true;
            Show(GroupDemo.Duplicates());
        }
    }

    private void Scan(string path)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _root = path;
        _groups.Children.Clear();
        _shown.Clear();
        _delete.Visibility = _pickOld.Visibility = Visibility.Collapsed;
        _cancel.Visibility = Visibility.Visible;
        Mo.Say(_status, L.T("در حال جستجو… (برای پوشه‌های بزرگ ممکنه چند دقیقه طول بکشه)", "Searching… (big folders can take a few minutes)"));
        var min = _minSize;
        _ = Task.Run(() =>
        {
            try
            {
                var found = FileTools.FindDuplicates([path], min, token);
                Dispatcher.BeginInvoke(() => Show(found));
            }
            catch (OperationCanceledException)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    Mo.Say(_status, L.T("متوقف شد.", "Stopped."));
                    _cancel.Visibility = Visibility.Collapsed;
                });
            }
            catch (Exception ex)
            {
                Log.Write($"duplicates: {ex.GetType().Name}: {ex.Message}");
                Dispatcher.BeginInvoke(() =>
                {
                    Mo.Say(_status, L.T("جستجو انجام نشد.", "The search failed."));
                    _cancel.Visibility = Visibility.Collapsed;
                });
            }
        });
    }

    private void Show(List<DuplicateGroup> found)
    {
        _cancel.Visibility = Visibility.Collapsed;
        _groups.Children.Clear();
        _shown.Clear();
        if (found.Count == 0)
        {
            Mo.Say(_status, L.T("فایل تکراری پیدا نشد.", "No duplicate files found."));
            _groupsCard.Visibility = Visibility.Collapsed;
            return;
        }
        Mo.Show(_groupsCard, true, 20, 0.7);
        var groupMemo = new Mo.Memo(_groups);
        var shown = found.Take(100).ToList();
        Mo.Say(_status, L.T($"{Fa.Digits(found.Count.ToString())} گروه تکراری پیدا شد؛ با نگه داشتن یه نسخه از هر گروه تا {Fa.Bytes(found.Sum(f => f.Reclaimable))} آزاد می‌شه.",
            $"{found.Count} duplicate group(s); keeping one of each frees up to {Fa.Bytes(found.Sum(f => f.Reclaimable))}.")
            + (found.Count > shown.Count ? L.T($" (۱۰۰ گروه اول نشون داده می‌شه)", " (first 100 shown)") : ""));
        foreach (var group in shown)
        {
            var box = ListUi.Nested();
            var content = new StackPanel();
            content.Children.Add(new TextBlock
            {
                Text = ListUi.Ltr(L.T($"{Fa.Digits(group.Files.Count.ToString())} نسخه · هرکدوم {Fa.Bytes(group.Size)}", $"{group.Files.Count} copies · {Fa.Bytes(group.Size)} each")),
                Style = St("H2"), FontSize = 13.5, Margin = new Thickness(0, 0, 0, 2),
            });
            var items = new List<(FileEntry, CheckBox)>();
            var first = true;
            foreach (var file in group.Files)
            {
                var check = new CheckBox { Style = St("Check") };
                check.Click += (_, _) => UpdateDelete();
                var row = ListUi.CheckItem(check, Path.GetFileName(file.Path), $"\u200E{Path.GetDirectoryName(file.Path)}\u200E{ListUi.Sep}{Fa.Date(file.Modified)}", FolderPick.ExplorerButton(file.Path), first);
                row.ToolTip = file.Path;
                content.Children.Add(row);
                items.Add((file, check));
                first = false;
            }
            box.Child = content;
            _groups.Children.Add(groupMemo.Item(string.Join("|", group.Files.Select(f => f.Path)), box));
            _shown.Add((group, items));
        }
        Mo.Show(_pickOld, true, 8, 0.4);
        UpdateDelete();
    }

    private List<FileEntry> Selected() => [.. _shown.SelectMany(s => s.items).Where(i => i.box.IsChecked == true).Select(i => i.file)];

    /// <summary>A group whose every copy is ticked would lose the file entirely; that is never allowed.</summary>
    private bool AllTicked() => _shown.Any(s => s.items.Count > 0 && s.items.All(i => i.box.IsChecked == true));

    private void UpdateDelete()
    {
        var chosen = Selected();
        var all = AllTicked();
        Mo.Show(_delete, chosen.Count > 0, 8, 0.4);
        _delete.IsEnabled = !all;
        ListUi.SetContent(_delete, all ? "warn" : "trash", all
            ? L.T("از هر گروه باید یه نسخه بدون علامت بمونه", "Leave at least one copy unticked in every group")
            : L.T($"فرستادن {Fa.Digits(chosen.Count.ToString())} نسخه ({Fa.Bytes(chosen.Sum(f => f.Size))}) به سطل بازیافت", $"Send {chosen.Count} copies ({Fa.Bytes(chosen.Sum(f => f.Size))}) to the Recycle Bin"));
    }

    private void DeleteSelected()
    {
        if (AllTicked()) return;
        var chosen = Selected();
        if (chosen.Count == 0) return;
        var preview = string.Join("\n", chosen.Take(12).Select(f => "• " + f.Path)) + (chosen.Count > 12 ? L.T($"\n… و {Fa.Digits((chosen.Count - 12).ToString())} فایل دیگه", $"\n… and {chosen.Count - 12} more") : "");
        if (!ConfirmWindow.AskAlways(L.T("فرستادن به سطل بازیافت", "Send to the Recycle Bin"),
                L.T($"{Fa.Digits(chosen.Count.ToString())} نسخه‌ی تکراری ({Fa.Bytes(chosen.Sum(f => f.Size))}) به سطل بازیافت می‌ره. از هر گروه یه نسخه می‌مونه.",
                    $"{chosen.Count} duplicate copies ({Fa.Bytes(chosen.Sum(f => f.Size))}) go to the Recycle Bin. One copy of each group stays."),
                L.T("بفرست", "Send"), danger: true, detail: preview)) return;
        var results = Recycler.Send(chosen.Select(f => f.Path), [_root]);
        Hub.Notify(L.T("سطل بازیافت", "Recycle Bin"),
            L.T($"{Fa.Digits(results.Count(r => r.ok).ToString())} فایل فرستاده شد.", $"{results.Count(r => r.ok)} file(s) sent."));
        Scan(_root);
    }
}

/// <summary>Scheduled folder backups with a preview, verification and a restore that never overwrites.</summary>
public sealed class BackupPage : PageBase
{
    private readonly StackPanel _jobs = new();
    private readonly Mo.Memo _jobMemo;
    private readonly TextBox _name = new() { Style = St("Input"), MaxLength = 60 };
    private readonly StackPanel _sources = new() { MinHeight = 20 };
    private readonly TextBlock _destination = new() { Style = St("Body"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBox _hour = new() { Style = St("NumberInput"), Text = "22", FlowDirection = FlowDirection.LeftToRight };
    private readonly TextBox _minute = new() { Style = St("NumberInput"), Text = "00", FlowDirection = FlowDirection.LeftToRight };
    private readonly WrapPanel _days = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly CheckBox _verify = new Kit.Switch { IsChecked = true };
    private readonly TextBlock _hint = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly List<string> _chosenSources = [];
    private readonly Grid _root = ListUi.PageGrid(2);
    private GlassCard _listCard = null!;
    private bool _seeded;
    private string _dest = "";
    private BackupFrequency _frequency = BackupFrequency.Daily;
    private DayOfWeek _day = DayOfWeek.Friday;

    public override string PageTitle => L.T("فایل و پشتیبان", "Files & backup");
    public override string PageSubtitle => L.T("از پوشه‌هات به‌صورت خودکار و زمان‌بندی‌شده نسخه‌ی پشتیبان می‌گیره، درستیش رو چک می‌کنه و بدون جایگزین‌کردن فایل‌های فعلی بازیابی می‌کنه.",
        "Backs up your folders on a schedule, checks the copies and restores without replacing current files.");

    public BackupPage()
    {
        _jobMemo = new Mo.Memo(_jobs);
        var (listCard, list) = Ui2.Card(L.T("پشتیبان‌گیری‌های تو", "Your backups"));
        list.Children.Add(_jobs);
        listCard.Margin = new Thickness(8);
        _listCard = (GlassCard)listCard;
        _root.Children.Add(listCard);
        ListUi.Place(listCard, 0, 0, 12);

        var (card, body) = Ui2.Card(L.T("پشتیبان‌گیری جدید", "New backup"));
        card.Margin = new Thickness(8);
        var cols = new ListUi.TwoColumns { Margin = new Thickness(0, 4, 0, 0) };
        var left = cols.Left;
        var right = cols.Right;

        // left: what, from where, to where
        left.Children.Add(Ui2.Label(L.T("اسم", "Name"), 10, 6));
        left.Children.Add(_name);
        left.Children.Add(Ui2.Label(L.T("چه پوشه‌هایی؟", "Which folders?"), 14, 6));
        var sourceBox = new Border { Background = Res("FieldFill"), BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 14, 10), MinHeight = 44, Child = _sources };
        left.Children.Add(sourceBox);
        var srcButtons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        srcButtons.Children.Add(Small(ListUi.Btn("folder", L.T("افزودن پوشه…", "Add folder…"), "Btn", () =>
        {
            if (FolderPick.Choose() is not { } p || _chosenSources.Contains(p, StringComparer.OrdinalIgnoreCase)) return;
            _chosenSources.Add(p);
            ShowSources();
        })));
        foreach (var (name, path) in FolderPick.Quick().Take(4))
        {
            var p = path;
            srcButtons.Children.Add(Small(ListUi.Btn(null, "+ " + name, "Btn", () =>
            {
                if (_chosenSources.Contains(p, StringComparer.OrdinalIgnoreCase)) return;
                _chosenSources.Add(p);
                ShowSources();
            })));
        }
        left.Children.Add(srcButtons);
        var clear = ListUi.Btn("x", L.T("خالی کردن لیست پوشه‌ها", "Clear the folder list"), "Btn", () =>
        {
            _chosenSources.Clear();
            ShowSources();
        });
        clear.HorizontalAlignment = HorizontalAlignment.Left;
        left.Children.Add(clear);

        left.Children.Add(Ui2.Label(L.T("کجا ذخیره بشه؟ (ترجیحاً یه دیسک یا فلش دیگه)", "Where to store it? (ideally another disk or drive)"), 16, 4));
        left.Children.Add(_destination);
        var pickDest = ListUi.Btn("folder", L.T("انتخاب مقصد…", "Choose destination…"), "Btn", () =>
        {
            if (FolderPick.Choose(_dest) is not { } p) return;
            _dest = p;
            _destination.Text = p;
        });
        pickDest.HorizontalAlignment = HorizontalAlignment.Left;
        pickDest.Margin = new Thickness(0, 8, 0, 0);
        left.Children.Add(pickDest);

        // right: when, how often, checking
        right.Children.Add(Ui2.Label(L.T("هر چند وقت", "How often"), 10, 6));
        var freq = new Segmented([L.T("هر روز", "Daily"), L.T("هر هفته", "Weekly"), L.T("فقط دستی", "Manual only")], 0);
        BackupFrequency[] freqs = [BackupFrequency.Daily, BackupFrequency.Weekly, BackupFrequency.Manual];
        freq.Changed += i =>
        {
            _frequency = freqs[i];
            _days.Visibility = _frequency == BackupFrequency.Weekly ? Visibility.Visible : Visibility.Collapsed;
        };
        right.Children.Add(freq);
        right.Children.Add(_days);
        Chips(_days, new[] { DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }
            .Select(d => (d, Fa.DayName(d))), _day, d => _day = d);
        _days.Visibility = Visibility.Collapsed;
        right.Children.Add(Ui2.Label(L.T("ساعت اجرا", "Time of day"), 14, 6));
        var time = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left };
        time.Children.Add(_hour);
        time.Children.Add(new TextBlock { Text = ":", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Res("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) });
        time.Children.Add(_minute);
        TimeBoxes.Wire(_hour, _minute);
        right.Children.Add(time);
        var verifyRow = K.SettingRow(L.T("بعد از کپی، فایل‌ها رو دوباره بخون و درستیشون رو چک کن", "After copying, read the files back and check them"), _verify);
        verifyRow.Margin = new Thickness(0, 14, 0, 0);
        right.Children.Add(verifyRow);
        body.Children.Add(cols);

        var save = ListUi.Btn("plus", L.T("ساختن پشتیبان‌گیری", "Create backup"), "BtnPrimary", Create);
        save.HorizontalAlignment = HorizontalAlignment.Left;
        save.Margin = new Thickness(0, 18, 0, 0);
        body.Children.Add(save);
        body.Children.Add(_hint);
        body.Children.Add(Ui2.Label(L.T("چیزی از مقصد یا پوشه‌های اصلی حذف نمی‌شه؛ فقط فایل‌های جدید یا تغییرکرده کپی می‌شن. این یه «همگام‌سازی» نیست.",
            "Nothing is deleted at the destination or the source; only new or changed files are copied. This is not a sync."), 10, 0));
        _root.Children.Add(card);
        ListUi.Place(card, 1, 0, 12);
        Content = _root;

        While<Action>(h => Hub.BackupsChanged += h, h => Hub.BackupsChanged -= h, () => Dispatcher.BeginInvoke(RefreshJobs));
        ShowSources();
    }

    private static Button Small(Button b)
    {
        b.Margin = new Thickness(0, 0, 8, 8);
        return b;
    }

    private void ShowSources()
    {
        _sources.Children.Clear();
        foreach (var s in _chosenSources)
            _sources.Children.Add(new TextBlock { Text = s, Style = St("Body"), FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = s, Foreground = Res("Text") });
        if (_chosenSources.Count == 0) _sources.Children.Add(new TextBlock { Text = L.T("هنوز پوشه‌ای انتخاب نشده.", "No folder chosen yet."), Style = St("Label") });
    }

    private void Create()
    {
        TimeBoxes.TryRead(_hour, _minute, out var h, out var m);
        var job = new BackupJob
        {
            Name = _name.Text.Trim(), Sources = [.. _chosenSources], Destination = _dest, Frequency = _frequency, Day = _day,
            Hour = h, Minute = m, VerifyAfter = _verify.IsChecked == true,
        };
        if (Backups.Problem(job) is { } problem)
        {
            Mo.Say(_hint, problem);
            _hint.Foreground = Res("Red");
            return;
        }
        if (Hub.State.Backups.Any(j => Backups.SafeName(j.Name).Equals(Backups.SafeName(job.Name), StringComparison.OrdinalIgnoreCase)))
        {
            Mo.Say(_hint, L.T("یه پشتیبان‌گیری با همین اسم هست.", "A backup with this name already exists."));
            _hint.Foreground = Res("Red");
            return;
        }
        Hub.State.Backups.Add(job);
        Hub.Store.Save();
        _name.Text = "";
        _chosenSources.Clear();
        ShowSources();
        _hint.Foreground = Res("Muted");
        Mo.Say(_hint, L.T("ساخته شد. می‌تونی همین الان از لیست بالا اجراش کنی.", "Created. You can run it now from the list above."));
        RefreshJobs();
    }

    public override void Refresh()
    {
        if (Hub.Demo && !_seeded)
        {
            _seeded = true;
            if (Hub.State.Backups.Count == 0) Hub.State.Backups.AddRange(GroupDemo.Backups());
        }
        RefreshJobs();
    }

    public override void OnSecond()
    {
        if (Hub.BackupRunningId is not null) RefreshJobs();
    }

    private static string ScheduleText(BackupJob j) => j.Frequency switch
    {
        BackupFrequency.Manual => L.T("فقط دستی", "Manual only"),
        BackupFrequency.Daily => L.T($"هر روز ساعت {Fa.Clock(j.Hour, j.Minute)}", $"Every day at {j.Hour:00}:{j.Minute:00}"),
        _ => L.T($"هر {Fa.DayName(j.Day)} ساعت {Fa.Clock(j.Hour, j.Minute)}", $"Every {j.Day} at {j.Hour:00}:{j.Minute:00}"),
    };

    private void RefreshJobs()
    {
        _jobs.Children.Clear();
        _jobMemo.Begin();
        if (Hub.State.Backups.Count == 0)
        {
            _jobMemo.End();
            _jobs.Children.Add(new TextBlock { Text = L.T("هنوز پشتیبان‌گیری‌ای نساختی. پایین یکی بساز.", "No backups yet. Create one below."), Style = St("Body"), Margin = new Thickness(0, 8, 0, 0) });
            return;
        }
        foreach (var job in Hub.State.Backups.ToList()) _jobs.Children.Add(_jobMemo.Item(job.Id.ToString(), JobCard(job)));
        _jobMemo.End();
    }

    private FrameworkElement JobCard(BackupJob job)
    {
        var running = Hub.BackupRunningId == job.Id;
        var box = ListUi.Nested();
        var s = new StackPanel();

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.Children.Add(new IconBadge("cloud", job.Enabled ? "HueA" : "HueC", 38, 19, 12) { VerticalAlignment = VerticalAlignment.Top });
        var titles = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
        titles.Children.Add(new TextBlock { Text = job.Name, Style = St("H2"), FontSize = 15 });
        var sources = string.Join("، ", job.Sources.Select(p => new DirectoryInfo(p).Name));
        titles.Children.Add(new TextBlock { Text = ScheduleText(job) + "  ·  " + sources, Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        titles.Children.Add(new TextBlock { Text = "→  " + job.Destination, Style = St("Label"), FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = job.Destination, Margin = new Thickness(0, 2, 0, 0) });
        Grid.SetColumn(titles, 1);
        head.Children.Add(titles);
        s.Children.Add(head);

        var last = job.History.LastOrDefault();
        if (last is null)
            s.Children.Add(new TextBlock { Text = L.T("هنوز اجرا نشده.", "Not run yet."), Style = St("Body"), Margin = new Thickness(0, 10, 0, 0) });
        else
            s.Children.Add(ListUi.IconLine(last.Ok ? "chk" : "warn", last.Ok ? "Green" : "Gold",
                L.T($"آخرین اجرا {Fa.DayOf(last.Time, DateTime.Now)} ساعت {Fa.Clock(last.Time)}: {Fa.Digits(last.Files.ToString())} فایل ({Fa.Bytes(last.Bytes)})", $"Last run {Fa.DayOf(last.Time, DateTime.Now)} {last.Time:HH:mm}: {last.Files} file(s) ({Fa.Bytes(last.Bytes)})") + (last.Note.Length > 0 ? " · " + last.Note : ""), 10));
        if (job.Enabled && job.NextOccurrence(DateTime.Now) is { } next && !running)
            s.Children.Add(new TextBlock { Text = L.T($"بعدی: {Fa.DayOf(next, DateTime.Now)} ساعت {Fa.Clock(next)}", $"Next: {Fa.DayOf(next, DateTime.Now)} {next:HH:mm}"), Style = St("Label"), Margin = new Thickness(0, 4, 0, 0) });
        if (Backups.Problem(job) is { } problem)
            s.Children.Add(ListUi.IconLine("warn", "Red", problem, 6));

        if (running)
        {
            s.Children.Add(new ProgressBar { Style = St("Bar"), Minimum = 0, Maximum = Math.Max(1, Hub.BackupTotal), Value = Hub.BackupDone, Height = 8, Margin = new Thickness(0, 12, 0, 0) });
            var stop = ListUi.Btn("x", L.T("توقف", "Stop"), "Btn", Hub.CancelBackup);
            stop.HorizontalAlignment = HorizontalAlignment.Left;
            stop.Margin = new Thickness(0, 10, 0, 0);
            s.Children.Add(stop);
        }
        else
        {
            var buttons = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            Button B(string icon, string text, Action click, string style = "Btn")
            {
                var b = ListUi.Btn(icon, text, style, click);
                b.Margin = new Thickness(0, 0, 8, 6);
                return b;
            }
            buttons.Children.Add(B("play", L.T("اجرا با پیش‌نمایش", "Run (with preview)"), () => RunWithPreview(job), "BtnPrimary"));
            buttons.Children.Add(B("shield", L.T("بررسی درستی نسخه‌ها", "Verify copies"), () => VerifyJob(job)));
            buttons.Children.Add(B("refresh", L.T("بازیابی…", "Restore…"), () => RestoreJob(job)));
            buttons.Children.Add(B(job.Enabled ? "min" : "play", job.Enabled ? L.T("غیرفعال کردن زمان‌بندی", "Pause schedule") : L.T("فعال کردن زمان‌بندی", "Resume schedule"), () =>
            {
                job.Enabled = !job.Enabled;
                Hub.Store.Save();
                RefreshJobs();
            }));
            buttons.Children.Add(B("trash", L.T("حذف این پشتیبان‌گیری", "Delete this backup job"), () =>
            {
                if (!ConfirmWindow.AskAlways(L.T("حذف پشتیبان‌گیری", "Delete backup job"),
                        L.T($"«{job.Name}» از لیست برداشته می‌شه. فایل‌های کپی‌شده توی مقصد دست‌نخورده می‌مونن.", $"“{job.Name}” is removed from the list. The copied files at the destination are left untouched."),
                        L.T("حذف کن", "Delete"), danger: true)) return;
                Mo.Leave(box, () =>
                {
                    Hub.State.Backups.Remove(job);
                    Hub.Store.Save();
                    RefreshJobs();
                });
            }, "BtnDanger"));
            s.Children.Add(buttons);
        }
        box.Child = s;
        return box;
    }

    private void RunWithPreview(BackupJob job)
    {
        if (Backups.Problem(job) is { } problem)
        {
            Hub.Notify(L.T("پشتیبان‌گیری", "Backup"), problem);
            return;
        }
        Hub.Notify(L.T("پشتیبان‌گیری", "Backup"), L.T("در حال بررسی تغییرات…", "Checking what changed…"));
        _ = Task.Run(() =>
        {
            BackupPlan plan;
            try { plan = Backups.Plan(job); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Dispatcher.BeginInvoke(() => Hub.Notify(L.T("پشتیبان‌گیری", "Backup"), L.T("خوندن پوشه‌ها ممکن نبود.", "The folders could not be read.")));
                return;
            }
            Dispatcher.BeginInvoke(() =>
            {
                if (plan.ToCopy.Count == 0)
                {
                    Hub.Notify(L.T("پشتیبان‌گیری", "Backup"), L.T("همه‌چیز از قبل پشتیبان‌گیری شده؛ چیزی برای کپی نیست.", "Everything is already backed up; nothing to copy."));
                    return;
                }
                var detail = string.Join("\n", plan.ToCopy.Take(10).Select(p => $"• {p.Key}")) + (plan.ToCopy.Count > 10 ? L.T($"\n… و {Fa.Digits((plan.ToCopy.Count - 10).ToString())} فایل دیگه", $"\n… and {plan.ToCopy.Count - 10} more") : "");
                if (!ConfirmWindow.AskAlways(L.T("اجرای پشتیبان‌گیری", "Run backup"),
                        L.T($"{Fa.Digits(plan.ToCopy.Count.ToString())} فایل ({Fa.Bytes(plan.Bytes)}) کپی می‌شه؛ {Fa.Digits(plan.Unchanged.ToString())} فایل بدون تغییره و رد می‌شه.",
                            $"{plan.ToCopy.Count} file(s) ({Fa.Bytes(plan.Bytes)}) will be copied; {plan.Unchanged} unchanged file(s) are skipped."),
                        L.T("شروع کن", "Start"), detail: detail)) return;
                if (!Hub.StartBackup(job)) Hub.Notify(L.T("پشتیبان‌گیری", "Backup"), L.T("یه پشتیبان‌گیری دیگه در حال اجراست.", "Another backup is running."));
            });
        });
    }

    private void VerifyJob(BackupJob job)
    {
        Hub.Notify(L.T("بررسی نسخه‌ها", "Verifying copies"), L.T("در حال خوندن فایل‌های پشتیبان…", "Reading the backed-up files…"));
        _ = Task.Run(() =>
        {
            var r = Backups.Verify(job);
            Dispatcher.BeginInvoke(() => Hub.Notify(L.T("بررسی نسخه‌ها", "Verifying copies"),
                r.Ok
                    ? L.T($"{Fa.Digits(r.Checked.ToString())} فایل بررسی شد و همه سالمه ✅", $"{r.Checked} file(s) checked, all intact ✅")
                    : L.T($"{Fa.Digits(r.Mismatched.Count.ToString())} فایل خراب و {Fa.Digits(r.Missing.Count.ToString())} فایل گم‌شده پیدا شد. دوباره پشتیبان‌گیری کن.",
                        $"{r.Mismatched.Count} damaged and {r.Missing.Count} missing file(s). Run the backup again.")));
        });
    }

    private void RestoreJob(BackupJob job)
    {
        var start = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        if (FolderPick.Choose(start, L.T("فایل‌ها کجا بازیابی بشن؟ (فایل‌های فعلی جایگزین نمی‌شن)", "Where should files be restored? (current files are not replaced)")) is not { } target) return;
        var items = Backups.PlanRestore(job, target);
        if (items.Count == 0)
        {
            Hub.Notify(L.T("بازیابی", "Restore"), L.T("چیزی برای بازیابی نیست.", "There is nothing to restore."));
            return;
        }
        var collide = items.Count(i => i.WouldCollide);
        var detail = string.Join("\n", items.Take(10).Select(i => "• " + i.TargetPath)) + (items.Count > 10 ? L.T($"\n… و {Fa.Digits((items.Count - 10).ToString())} فایل دیگه", $"\n… and {items.Count - 10} more") : "");
        if (!ConfirmWindow.AskAlways(L.T("بازیابی فایل‌ها", "Restore files"),
                L.T($"{Fa.Digits(items.Count.ToString())} فایل ({Fa.Bytes(items.Sum(i => i.Size))}) توی «{target}» بازیابی می‌شه." +
                    (collide > 0 ? $" {Fa.Digits(collide.ToString())} فایل از قبل اونجا هست و کنارش با «(restored)» ذخیره می‌شه؛ هیچ فایلی جایگزین نمی‌شه." : ""),
                    $"{items.Count} file(s) ({Fa.Bytes(items.Sum(i => i.Size))}) will be restored into “{target}”." +
                    (collide > 0 ? $" {collide} already exist there and are saved next to them as “(restored)”; nothing is replaced." : "")),
                L.T("بازیابی کن", "Restore"), detail: detail)) return;
        _ = Task.Run(() =>
        {
            var (restored, failed) = Backups.Restore(job, items);
            Dispatcher.BeginInvoke(() => Hub.Notify(L.T("بازیابی", "Restore"),
                L.T($"{Fa.Digits(restored.ToString())} فایل بازیابی شد." + (failed.Count > 0 ? $" {Fa.Digits(failed.Count.ToString())} فایل نشد." : " ✅"),
                    $"{restored} file(s) restored." + (failed.Count > 0 ? $" {failed.Count} failed." : " ✅"))));
        });
    }
}


/// <summary>Temporary files and caches that programs rebuild by themselves, shown with their size before anything is
/// removed.</summary>
public sealed class CleanupPage : PageBase
{
    private readonly StackPanel _list = new();
    private readonly TextBlock _status = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly Button _clean = new() { Style = St("BtnDanger"), Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed };
    private readonly Dictionary<string, CheckBox> _checks = [];
    private List<CleanScan> _scans = [];
    private bool _busy;
    private bool _seeded;

    public override string PageTitle => L.T("فایل و پشتیبان", "Files & backup");
    public override string PageSubtitle => L.T("فایل‌های موقت و کش‌هایی که برنامه‌ها خودشون دوباره می‌سازن. اسناد، رمزها و تاریخچه هیچ‌وقت دست نمی‌خورن.",
        "Temporary files and caches that programs rebuild themselves. Documents, passwords and history are never touched.");

    public CleanupPage()
    {
        var root = ListUi.PageGrid(1);
        var (card, body) = Ui2.Card(L.T("چی جمع شده؟", "What has piled up?"));
        card.Margin = new Thickness(8);
        ListUi.Place(card, 0, 0, 12);
        var scan = ListUi.Btn("brush", L.T("بررسی", "Scan"), "BtnPrimary", Scan);
        scan.HorizontalAlignment = HorizontalAlignment.Left;
        scan.Margin = new Thickness(0, 14, 0, 0);
        body.Children.Add(scan);
        body.Children.Add(_status);
        _list.Margin = new Thickness(0, 8, 0, 0);
        body.Children.Add(_list);
        _clean.Click += (_, _) => Clean();
        body.Children.Add(_clean);
        root.Children.Add(card);
        Content = root;
    }

    public override void Refresh()
    {
        if (Hub.Demo && !_seeded)
        {
            _seeded = true;
            Show(GroupDemo.CleanupScans());
        }
    }

    private void Scan()
    {
        if (_busy) return;
        _busy = true;
        Mo.Say(_status, L.T("در حال بررسی…", "Scanning…"));
        var elevated = Hub.Elevated;
        _ = Task.Run(() =>
        {
            var scans = new List<CleanScan>();
            foreach (var c in Cleanup.Categories())
            {
                try { scans.Add(Cleanup.Scan(c, elevated)); }
                catch (Exception ex) { Log.Write($"cleanup scan {c.Id}: {ex.GetType().Name}"); }
            }
            Dispatcher.BeginInvoke(() =>
            {
                _busy = false;
                Show(scans);
            });
        });
    }

    private void Show(List<CleanScan> scans)
    {
        _scans = scans;
        var rowMemo = new Mo.Memo(_list);
        _list.Children.Clear();
        _checks.Clear();
        var total = scans.Where(s => !s.Blocked).Sum(s => s.Bytes);
        Mo.Say(_status, L.T($"در مجموع {Fa.Bytes(total)} قابل پاک‌سازیه.", $"{Fa.Bytes(total)} can be cleaned in total."));
        var first = true;
        foreach (var s in scans)
        {
            var check = new CheckBox { Style = St("Check"), IsEnabled = !s.Blocked && s.Files > 0 };
            check.Click += (_, _) => Update();
            _checks[s.Category.Id] = check;
            var size = new TextBlock
            {
                Text = s.Files == 0 ? L.T("چیزی نیست", "nothing") : $"{Fa.Bytes(s.Bytes)} · {Fa.Digits(s.Files.ToString())}", Style = St("Label"), VerticalAlignment = VerticalAlignment.Center,
                FlowDirection = FlowDirection.LeftToRight,
            };
            _list.Children.Add(rowMemo.Item(s.Category.Id, ListUi.CheckItem(check, s.Category.Name, s.Category.Note + (s.Blocked ? L.T(" (نیاز به اجرای ادمین)", " (needs administrator)") : ""), size, first, trimSub: false)));
            first = false;
        }
        Update();
    }

    private List<CleanScan> Chosen() => [.. _scans.Where(s => _checks.TryGetValue(s.Category.Id, out var c) && c.IsChecked == true)];

    private void Update()
    {
        var chosen = Chosen();
        Mo.Show(_clean, chosen.Count > 0, 8, 0.4);
        ListUi.SetContent(_clean, "trash", L.T($"پاک کردن {Fa.Bytes(chosen.Sum(s => s.Bytes))}", $"Clean {Fa.Bytes(chosen.Sum(s => s.Bytes))}"));
    }

    private void Clean()
    {
        var chosen = Chosen();
        if (chosen.Count == 0 || _busy) return;
        var detail = string.Join("\n", chosen.Select(s => $"• {s.Category.Name}: {Fa.Bytes(s.Bytes)}"));
        if (!ConfirmWindow.AskAlways(L.T("پاک‌سازی", "Clean up"),
                L.T($"{Fa.Bytes(chosen.Sum(s => s.Bytes))} فایل موقت و کش برای همیشه پاک می‌شه. اسناد و تنظیمات شخصی‌ت جزوش نیست.", $"{Fa.Bytes(chosen.Sum(s => s.Bytes))} of temporary files and caches is deleted for good. Your documents and personal settings are not part of it."),
                L.T("پاک کن", "Clean"), danger: true, detail: detail)) return;
        _busy = true;
        Mo.Say(_status, L.T("در حال پاک‌سازی…", "Cleaning…"));
        var categories = chosen.Select(s => s.Category).ToList();
        _ = Task.Run(() =>
        {
            long freed = 0;
            var skipped = 0;
            foreach (var c in categories)
            {
                try
                {
                    var r = Cleanup.Clean(c);
                    freed += r.FreedBytes;
                    skipped += r.Skipped;
                }
                catch (Exception ex) { Log.Write($"cleanup {c.Id}: {ex.GetType().Name}"); }
            }
            Dispatcher.BeginInvoke(() =>
            {
                _busy = false;
                Hub.Notify(L.T("پاک‌سازی", "Clean-up"),
                    L.T($"{Fa.Bytes(freed)} آزاد شد." + (skipped > 0 ? $" {Fa.Digits(skipped.ToString())} فایل در حال استفاده بود و رد شد." : ""),
                        $"{Fa.Bytes(freed)} freed." + (skipped > 0 ? $" {skipped} file(s) were in use and skipped." : "")));
                Scan();
            });
        });
    }
}
