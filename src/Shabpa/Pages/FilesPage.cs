using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;

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
}

/// <summary>Where the space went in a folder: the biggest sub-folders and files.</summary>
public sealed class SpacePage : PageBase
{
    private readonly WrapPanel _quick = new();
    private readonly TextBlock _status = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly StackPanel _folders = new();
    private readonly StackPanel _files = new();
    private readonly Button _cancel = new() { Style = St("Btn"), Content = L.T("توقف", "Stop"), Visibility = Visibility.Collapsed, Margin = new Thickness(8, 0, 0, 0) };
    private readonly Button _delete = new() { Style = St("BtnDanger"), Visibility = Visibility.Collapsed, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Dictionary<string, CheckBox> _checks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _sizes = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _cts;
    private string _root = "";

    public override string PageTitle => L.T("فایل و پشتیبان", "Files & backup");
    public override string PageSubtitle => L.T("ببین فضای دیسک کجا رفته. فقط نگاه می‌کنه؛ هیچ فایلی بدون پیش‌نمایش و تأیید تو حذف نمی‌شه.",
        "See where the disk space went. It only looks; nothing is deleted without a preview and your confirmation.");

    public SpacePage()
    {
        var root = new StackPanel();
        var (card, body) = Ui2.Card(L.T("یه پوشه انتخاب کن", "Pick a folder"));
        _quick.Margin = new Thickness(0, 10, 0, 0);
        foreach (var (name, path) in FolderPick.Quick())
        {
            var b = new Button { Style = St("Btn"), Content = name, Margin = new Thickness(0, 0, 8, 8) };
            var p = path;
            b.Click += (_, _) => Scan(p);
            _quick.Children.Add(b);
        }
        var other = new Button { Style = St("Btn"), Content = L.T("پوشه‌ی دیگه…", "Other folder…"), Margin = new Thickness(0, 0, 8, 8) };
        other.Click += (_, _) =>
        {
            if (FolderPick.Choose() is { } p) Scan(p);
        };
        _quick.Children.Add(other);
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        {
            var b = new Button { Style = St("Btn"), Content = L.T("درایو ", "Drive ") + "‎" + drive.Name.TrimEnd((char)92), Margin = new Thickness(0, 0, 8, 8) };
            var p = drive.RootDirectory.FullName;
            b.Click += (_, _) => Scan(p);
            _quick.Children.Add(b);
        }
        body.Children.Add(_quick);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        _cancel.Click += (_, _) => _cts?.Cancel();
        row.Children.Add(_cancel);
        body.Children.Add(row);
        body.Children.Add(_status);
        root.Children.Add(card);

        var (fCard, f) = Ui2.Card(L.T("بزرگ‌ترین پوشه‌ها", "Biggest folders"));
        f.Children.Add(_folders);
        root.Children.Add(fCard);
        var (bCard, b2) = Ui2.Card(L.T("بزرگ‌ترین فایل‌ها", "Biggest files"), L.T("تیک بزن و به سطل بازیافت بفرست؛ از اونجا برمی‌گردن.", "Tick files and send them to the Recycle Bin; they can be restored from there."));
        b2.Children.Add(_files);
        _delete.Click += (_, _) => DeleteSelected();
        b2.Children.Add(_delete);
        root.Children.Add(bCard);
        Content = root;
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
        _status.Text = L.T("در حال بررسی…", "Scanning…");
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
                    _status.Text = L.T("متوقف شد.", "Stopped.");
                    _cancel.Visibility = Visibility.Collapsed;
                });
            }
            catch (Exception ex)
            {
                Log.Write($"space scan: {ex.GetType().Name}: {ex.Message}");
                Dispatcher.BeginInvoke(() =>
                {
                    _status.Text = L.T("بررسی انجام نشد.", "The scan failed.");
                    _cancel.Visibility = Visibility.Collapsed;
                });
            }
        });
    }

    private void Show(FolderReport report)
    {
        _cancel.Visibility = Visibility.Collapsed;
        _status.Text = L.T($"{report.Root}: {Fa.Bytes(report.TotalBytes)} در {Fa.Digits(report.FileCount.ToString())} فایل", $"{report.Root}: {Fa.Bytes(report.TotalBytes)} in {report.FileCount} files")
            + (report.SkippedFolders > 0 ? L.T($" ({Fa.Digits(report.SkippedFolders.ToString())} پوشه خونده نشد)", $" ({report.SkippedFolders} folder(s) could not be read)") : "");
        var max = Math.Max(1, report.Folders.Count == 0 ? 1 : report.Folders[0].Bytes);
        _folders.Children.Clear();
        foreach (var folder in report.Folders)
        {
            var grid = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel();
            left.Children.Add(new TextBlock { Text = Path.GetFileName(folder.Path), Style = St("Body"), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = folder.Path });
            left.Children.Add(new ProgressBar { Style = St("Bar"), Minimum = 0, Maximum = 1, Value = (double)folder.Bytes / max, Height = 6, Margin = new Thickness(0, 4, 0, 0) });
            grid.Children.Add(left);
            var size = new TextBlock { Text = Fa.Bytes(folder.Bytes), Style = St("Label"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            Grid.SetColumn(size, 1);
            grid.Children.Add(size);
            _folders.Children.Add(grid);
        }
        if (report.Folders.Count == 0) _folders.Children.Add(new TextBlock { Text = L.T("زیرپوشه‌ای نیست.", "No sub-folders."), Style = St("Body") });

        _files.Children.Clear();
        _checks.Clear();
        foreach (var file in report.Largest)
        {
            _sizes[file.Path] = file.Size;
            var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var check = new CheckBox { Style = St("Check"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            check.Click += (_, _) => UpdateDelete();
            _checks[file.Path] = check;
            grid.Children.Add(check);
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = Path.GetFileName(file.Path), Style = St("Body"), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = file.Path });
            text.Children.Add(new TextBlock { Text = $"{Fa.Bytes(file.Size)}  ·  {Fa.Date(file.Modified)}  ·  {Path.GetDirectoryName(file.Path)}", Style = St("Label"), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = file.Path });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            var open = new Button { Style = St("Btn"), Content = new TextBlock { Text = "", Style = St("Icon") }, ToolTip = L.T("نمایش توی اکسپلورر", "Show in Explorer"), Padding = new Thickness(9, 7, 9, 7) };
            var p = file.Path;
            open.Click += (_, _) => FolderPick.ShowInExplorer(p);
            Grid.SetColumn(open, 2);
            grid.Children.Add(open);
            _files.Children.Add(grid);
        }
        UpdateDelete();
    }

    private List<string> Selected() => [.. _checks.Where(c => c.Value.IsChecked == true).Select(c => c.Key)];

    private void UpdateDelete()
    {
        var chosen = Selected();
        _delete.Visibility = chosen.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _delete.Content = L.T($"فرستادن {Fa.Digits(chosen.Count.ToString())} فایل ({Fa.Bytes(chosen.Sum(p => _sizes[p]))}) به سطل بازیافت",
            $"Send {chosen.Count} file(s) ({Fa.Bytes(chosen.Sum(p => _sizes[p]))}) to the Recycle Bin");
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
    private readonly WrapPanel _sizeChips = new();
    private readonly TextBlock _status = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly StackPanel _groups = new();
    private readonly Button _delete = new() { Style = St("BtnDanger"), Visibility = Visibility.Collapsed, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Button _pickOld = new() { Style = St("Btn"), Content = L.T("همه رو جز قدیمی‌ترین نسخه‌ی هر گروه علامت بزن", "Tick all but the oldest copy of each group"), Visibility = Visibility.Collapsed, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Button _cancel = new() { Style = St("Btn"), Content = L.T("توقف", "Stop"), Visibility = Visibility.Collapsed };
    private readonly List<(DuplicateGroup group, List<(FileEntry file, CheckBox box)> items)> _shown = [];
    private CancellationTokenSource? _cts;
    private long _minSize = 1 << 20;
    private string _root = "";

    public override string PageTitle => L.T("فایل و پشتیبان", "Files & backup");
    public override string PageSubtitle => L.T("فایل‌هایی که دقیقاً یه محتوا دارن. از هر گروه همیشه یه نسخه می‌مونه و حذف فقط با تأیید خودت و به سطل بازیافت انجام می‌شه.",
        "Files with exactly the same content. One copy of each group always stays, and deleting only happens with your confirmation, into the Recycle Bin.");

    public DuplicatesPage()
    {
        var root = new StackPanel();
        var (card, body) = Ui2.Card(L.T("کجا بگردم؟", "Where to look?"));
        _quick.Margin = new Thickness(0, 10, 0, 0);
        foreach (var (name, path) in FolderPick.Quick())
        {
            var b = new Button { Style = St("Btn"), Content = name, Margin = new Thickness(0, 0, 8, 8) };
            var p = path;
            b.Click += (_, _) => Scan(p);
            _quick.Children.Add(b);
        }
        var other = new Button { Style = St("Btn"), Content = L.T("پوشه‌ی دیگه…", "Other folder…"), Margin = new Thickness(0, 0, 8, 8) };
        other.Click += (_, _) =>
        {
            if (FolderPick.Choose() is { } p) Scan(p);
        };
        _quick.Children.Add(other);
        body.Children.Add(_quick);
        body.Children.Add(Ui2.Label(L.T("فقط فایل‌های بزرگ‌تر از", "Only files bigger than"), 4, 4));
        var group = "dup" + GetHashCode();
        foreach (var (text, size) in new[] { ("۱ MB", 1L << 20), ("۱۰ MB", 10L << 20), ("۱۰۰ MB", 100L << 20) })
        {
            var chip = new RadioButton { Style = St("Chip"), GroupName = group, Content = L.En ? text.Replace('۱', '1').Replace('۰', '0') : text, Margin = new Thickness(0, 0, 8, 6), IsChecked = size == _minSize };
            var s = size;
            chip.Checked += (_, _) => _minSize = s;
            _sizeChips.Children.Add(chip);
        }
        body.Children.Add(_sizeChips);
        _cancel.Click += (_, _) => _cts?.Cancel();
        body.Children.Add(_cancel);
        body.Children.Add(_status);
        root.Children.Add(card);

        var (gCard, g) = Ui2.Card(L.T("گروه‌های تکراری", "Duplicate groups"));
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
        root.Children.Add(gCard);
        Content = root;
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
        _status.Text = L.T("در حال جستجو… (برای پوشه‌های بزرگ ممکنه چند دقیقه طول بکشه)", "Searching… (big folders can take a few minutes)");
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
                    _status.Text = L.T("متوقف شد.", "Stopped.");
                    _cancel.Visibility = Visibility.Collapsed;
                });
            }
            catch (Exception ex)
            {
                Log.Write($"duplicates: {ex.GetType().Name}: {ex.Message}");
                Dispatcher.BeginInvoke(() =>
                {
                    _status.Text = L.T("جستجو انجام نشد.", "The search failed.");
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
            _status.Text = L.T("فایل تکراری پیدا نشد.", "No duplicate files found.");
            return;
        }
        var shown = found.Take(100).ToList();
        _status.Text = L.T($"{Fa.Digits(found.Count.ToString())} گروه تکراری پیدا شد؛ با نگه داشتن یه نسخه از هر گروه تا {Fa.Bytes(found.Sum(f => f.Reclaimable))} آزاد می‌شه.",
            $"{found.Count} duplicate group(s); keeping one of each frees up to {Fa.Bytes(found.Sum(f => f.Reclaimable))}.")
            + (found.Count > shown.Count ? L.T($" (۱۰۰ گروه اول نشون داده می‌شه)", " (first 100 shown)") : "");
        foreach (var group in shown)
        {
            var box = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
            box.Children.Add(new TextBlock
            {
                Text = L.T($"{Fa.Digits(group.Files.Count.ToString())} نسخه · هرکدوم {Fa.Bytes(group.Size)}", $"{group.Files.Count} copies · {Fa.Bytes(group.Size)} each"),
                Style = St("H2"), FontSize = 13,
            });
            var items = new List<(FileEntry, CheckBox)>();
            foreach (var file in group.Files)
            {
                var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var check = new CheckBox { Style = St("Check"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
                check.Click += (_, _) => UpdateDelete();
                row.Children.Add(check);
                var text = new StackPanel();
                text.Children.Add(new TextBlock { Text = Path.GetFileName(file.Path), Style = St("Body"), TextTrimming = TextTrimming.CharacterEllipsis });
                text.Children.Add(new TextBlock { Text = $"{Path.GetDirectoryName(file.Path)}  ·  {Fa.Date(file.Modified)}", Style = St("Label"), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = file.Path });
                Grid.SetColumn(text, 1);
                row.Children.Add(text);
                var open = new Button { Style = St("Btn"), Content = new TextBlock { Text = "", Style = St("Icon") }, ToolTip = L.T("نمایش توی اکسپلورر", "Show in Explorer"), Padding = new Thickness(9, 7, 9, 7) };
                var p = file.Path;
                open.Click += (_, _) => FolderPick.ShowInExplorer(p);
                Grid.SetColumn(open, 2);
                row.Children.Add(open);
                box.Children.Add(row);
                items.Add((file, check));
            }
            _groups.Children.Add(box);
            _shown.Add((group, items));
        }
        _pickOld.Visibility = Visibility.Visible;
        UpdateDelete();
    }

    private List<FileEntry> Selected() => [.. _shown.SelectMany(s => s.items).Where(i => i.box.IsChecked == true).Select(i => i.file)];

    /// <summary>A group whose every copy is ticked would lose the file entirely; that is never allowed.</summary>
    private bool AllTicked() => _shown.Any(s => s.items.Count > 0 && s.items.All(i => i.box.IsChecked == true));

    private void UpdateDelete()
    {
        var chosen = Selected();
        var all = AllTicked();
        _delete.Visibility = chosen.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _delete.IsEnabled = !all;
        _delete.Content = all
            ? L.T("از هر گروه باید یه نسخه بدون علامت بمونه", "Leave at least one copy unticked in every group")
            : L.T($"فرستادن {Fa.Digits(chosen.Count.ToString())} نسخه ({Fa.Bytes(chosen.Sum(f => f.Size))}) به سطل بازیافت", $"Send {chosen.Count} copies ({Fa.Bytes(chosen.Sum(f => f.Size))}) to the Recycle Bin");
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
    private readonly TextBox _name = new() { Style = St("Input"), MaxLength = 60 };
    private readonly ListBox _sources = new() { MinHeight = 20, Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0) };
    private readonly TextBlock _destination = new() { Style = St("Body"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBox _hour = new() { Style = St("Input"), Width = 56, Padding = new Thickness(8, 6, 8, 6), FlowDirection = FlowDirection.LeftToRight, TextAlignment = TextAlignment.Center, Text = "22" };
    private readonly TextBox _minute = new() { Style = St("Input"), Width = 56, Padding = new Thickness(8, 6, 8, 6), FlowDirection = FlowDirection.LeftToRight, TextAlignment = TextAlignment.Center, Text = "00" };
    private readonly WrapPanel _freq = new();
    private readonly WrapPanel _days = new();
    private readonly CheckBox _verify = new() { Style = St("Check"), Content = L.T("بعد از کپی، فایل‌ها رو دوباره بخون و درستیشون رو چک کن", "After copying, read the files back and check them"), IsChecked = true, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock _hint = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly List<string> _chosenSources = [];
    private string _dest = "";
    private BackupFrequency _frequency = BackupFrequency.Daily;
    private DayOfWeek _day = DayOfWeek.Friday;

    public override string PageTitle => L.T("فایل و پشتیبان", "Files & backup");
    public override string PageSubtitle => L.T("از پوشه‌هات به‌صورت خودکار و زمان‌بندی‌شده نسخه‌ی پشتیبان می‌گیره، درستیش رو چک می‌کنه و بدون جایگزین‌کردن فایل‌های فعلی بازیابی می‌کنه.",
        "Backs up your folders on a schedule, checks the copies and restores without replacing current files.");

    public BackupPage()
    {
        var root = new StackPanel();
        var (listCard, list) = Ui2.Card(L.T("پشتیبان‌گیری‌های تو", "Your backups"));
        list.Children.Add(_jobs);
        root.Children.Add(listCard);

        var (card, body) = Ui2.Card(L.T("پشتیبان‌گیری جدید", "New backup"));
        body.Children.Add(Ui2.Label(L.T("اسم", "Name"), 10, 4));
        body.Children.Add(_name);
        body.Children.Add(Ui2.Label(L.T("چه پوشه‌هایی؟", "Which folders?"), 12, 4));
        body.Children.Add(_sources);
        var srcButtons = new WrapPanel();
        var addSrc = new Button { Style = St("Btn"), Content = L.T("افزودن پوشه…", "Add folder…"), Margin = new Thickness(0, 4, 8, 6) };
        addSrc.Click += (_, _) =>
        {
            if (FolderPick.Choose() is not { } p || _chosenSources.Contains(p, StringComparer.OrdinalIgnoreCase)) return;
            _chosenSources.Add(p);
            ShowSources();
        };
        srcButtons.Children.Add(addSrc);
        foreach (var (name, path) in FolderPick.Quick().Take(4))
        {
            var b = new Button { Style = St("Btn"), Content = "+ " + name, Margin = new Thickness(0, 4, 8, 6) };
            var p = path;
            b.Click += (_, _) =>
            {
                if (_chosenSources.Contains(p, StringComparer.OrdinalIgnoreCase)) return;
                _chosenSources.Add(p);
                ShowSources();
            };
            srcButtons.Children.Add(b);
        }
        body.Children.Add(srcButtons);
        var clear = new Button { Style = St("Btn"), Content = L.T("خالی کردن لیست پوشه‌ها", "Clear the folder list"), HorizontalAlignment = HorizontalAlignment.Left };
        clear.Click += (_, _) =>
        {
            _chosenSources.Clear();
            ShowSources();
        };
        body.Children.Add(clear);

        body.Children.Add(Ui2.Label(L.T("کجا ذخیره بشه؟ (ترجیحاً یه دیسک یا فلش دیگه)", "Where to store it? (ideally another disk or drive)"), 12, 4));
        body.Children.Add(_destination);
        var pickDest = new Button { Style = St("Btn"), Content = L.T("انتخاب مقصد…", "Choose destination…"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        pickDest.Click += (_, _) =>
        {
            if (FolderPick.Choose(_dest) is not { } p) return;
            _dest = p;
            _destination.Text = p;
        };
        body.Children.Add(pickDest);

        body.Children.Add(Ui2.Label(L.T("هر چند وقت", "How often"), 12, 4));
        var fgroup = "freq" + GetHashCode();
        foreach (var (f, text) in new[]
        {
            (BackupFrequency.Daily, L.T("هر روز", "Daily")), (BackupFrequency.Weekly, L.T("هر هفته", "Weekly")), (BackupFrequency.Manual, L.T("فقط دستی", "Manual only")),
        })
        {
            var chip = new RadioButton { Style = St("Chip"), GroupName = fgroup, Content = text, Margin = new Thickness(0, 0, 8, 6), IsChecked = f == _frequency };
            var captured = f;
            chip.Checked += (_, _) =>
            {
                _frequency = captured;
                _days.Visibility = captured == BackupFrequency.Weekly ? Visibility.Visible : Visibility.Collapsed;
            };
            _freq.Children.Add(chip);
        }
        body.Children.Add(_freq);
        var dgroup = "day" + GetHashCode();
        foreach (var d in new[] { DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
        {
            var chip = new RadioButton { Style = St("Chip"), GroupName = dgroup, Content = Fa.DayName(d), Margin = new Thickness(0, 0, 8, 6), IsChecked = d == _day };
            var captured = d;
            chip.Checked += (_, _) => _day = captured;
            _days.Children.Add(chip);
        }
        _days.Visibility = Visibility.Collapsed;
        body.Children.Add(_days);
        var time = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        time.Children.Add(_hour);
        time.Children.Add(new TextBlock { Text = ":", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Res("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) });
        time.Children.Add(_minute);
        TimeBoxes.Wire(_hour, _minute);
        body.Children.Add(Ui2.Label(L.T("ساعت اجرا", "Time of day"), 8, 2));
        body.Children.Add(time);
        body.Children.Add(_verify);
        var save = new Button { Style = St("BtnPrimary"), Content = L.T("ساختن پشتیبان‌گیری", "Create backup"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(18, 9, 18, 9), Margin = new Thickness(0, 14, 0, 0) };
        save.Click += (_, _) => Create();
        body.Children.Add(save);
        body.Children.Add(_hint);
        body.Children.Add(Ui2.Label(L.T("چیزی از مقصد یا پوشه‌های اصلی حذف نمی‌شه؛ فقط فایل‌های جدید یا تغییرکرده کپی می‌شن. این یه «همگام‌سازی» نیست.",
            "Nothing is deleted at the destination or the source; only new or changed files are copied. This is not a sync."), 10, 0));
        root.Children.Add(card);
        Content = root;

        Hub.BackupsChanged += () => Dispatcher.BeginInvoke(RefreshJobs);
        ShowSources();
    }

    private void ShowSources()
    {
        _sources.Items.Clear();
        foreach (var s in _chosenSources) _sources.Items.Add(new TextBlock { Text = s, Style = St("Body") });
        if (_chosenSources.Count == 0) _sources.Items.Add(new TextBlock { Text = L.T("هنوز پوشه‌ای انتخاب نشده.", "No folder chosen yet."), Style = St("Label") });
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
            _hint.Text = problem;
            _hint.Foreground = Res("Red");
            return;
        }
        if (Hub.State.Backups.Any(j => Backups.SafeName(j.Name).Equals(Backups.SafeName(job.Name), StringComparison.OrdinalIgnoreCase)))
        {
            _hint.Text = L.T("یه پشتیبان‌گیری با همین اسم هست.", "A backup with this name already exists.");
            _hint.Foreground = Res("Red");
            return;
        }
        Hub.State.Backups.Add(job);
        Hub.Store.Save();
        _name.Text = "";
        _chosenSources.Clear();
        ShowSources();
        _hint.Foreground = Res("Muted");
        _hint.Text = L.T("ساخته شد. می‌تونی همین الان از لیست بالا اجراش کنی.", "Created. You can run it now from the list above.");
        RefreshJobs();
    }

    public override void Refresh() => RefreshJobs();

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
        if (Hub.State.Backups.Count == 0)
        {
            _jobs.Children.Add(new TextBlock { Text = L.T("هنوز پشتیبان‌گیری‌ای نساختی. پایین یکی بساز.", "No backups yet. Create one below."), Style = St("Body"), Margin = new Thickness(0, 10, 0, 0) });
            return;
        }
        foreach (var job in Hub.State.Backups.ToList()) _jobs.Children.Add(JobCard(job));
    }

    private FrameworkElement JobCard(BackupJob job)
    {
        var running = Hub.BackupRunningId == job.Id;
        var box = new Border
        {
            Background = Res("Panel2"), BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 10, 0, 0),
        };
        var s = new StackPanel();
        s.Children.Add(new TextBlock { Text = job.Name, Style = St("H2"), FontSize = 15 });
        s.Children.Add(new TextBlock { Text = ScheduleText(job) + "  ·  " + string.Join("، ", job.Sources.Select(p => new DirectoryInfo(p).Name)) + "  →  " + job.Destination, Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        var last = job.History.LastOrDefault();
        var lastText = last is null
            ? L.T("هنوز اجرا نشده.", "Not run yet.")
            : (last.Ok ? "✅ " : "⚠️ ") + L.T($"آخرین اجرا {Fa.DayOf(last.Time, DateTime.Now)} ساعت {Fa.Clock(last.Time)}: {Fa.Digits(last.Files.ToString())} فایل ({Fa.Bytes(last.Bytes)})", $"Last run {Fa.DayOf(last.Time, DateTime.Now)} {last.Time:HH:mm}: {last.Files} file(s) ({Fa.Bytes(last.Bytes)})") + (last.Note.Length > 0 ? " · " + last.Note : "");
        s.Children.Add(new TextBlock { Text = lastText, Style = St("Body"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        if (job.Enabled && job.NextOccurrence(DateTime.Now) is { } next && !running)
            s.Children.Add(new TextBlock { Text = L.T($"بعدی: {Fa.DayOf(next, DateTime.Now)} ساعت {Fa.Clock(next)}", $"Next: {Fa.DayOf(next, DateTime.Now)} {next:HH:mm}"), Style = St("Label"), Margin = new Thickness(0, 2, 0, 0) });
        if (Backups.Problem(job) is { } problem)
            s.Children.Add(new TextBlock { Text = "⚠️ " + problem, Style = St("Label"), Foreground = Res("Red"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });

        if (running)
        {
            s.Children.Add(new ProgressBar { Style = St("Bar"), Minimum = 0, Maximum = Math.Max(1, Hub.BackupTotal), Value = Hub.BackupDone, Height = 8, Margin = new Thickness(0, 10, 0, 0) });
            var stop = new Button { Style = St("Btn"), Content = L.T("توقف", "Stop"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            stop.Click += (_, _) => Hub.CancelBackup();
            s.Children.Add(stop);
        }
        else
        {
            var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            Button B(string text, Action click, string style = "Btn")
            {
                var b = new Button { Style = St(style), Content = text, Margin = new Thickness(0, 0, 8, 6) };
                b.Click += (_, _) => click();
                return b;
            }
            buttons.Children.Add(B(L.T("اجرا با پیش‌نمایش", "Run (with preview)"), () => RunWithPreview(job), "BtnPrimary"));
            buttons.Children.Add(B(L.T("بررسی درستی نسخه‌ها", "Verify copies"), () => VerifyJob(job)));
            buttons.Children.Add(B(L.T("بازیابی…", "Restore…"), () => RestoreJob(job)));
            buttons.Children.Add(B(job.Enabled ? L.T("غیرفعال کردن زمان‌بندی", "Pause schedule") : L.T("فعال کردن زمان‌بندی", "Resume schedule"), () =>
            {
                job.Enabled = !job.Enabled;
                Hub.Store.Save();
                RefreshJobs();
            }));
            buttons.Children.Add(B(L.T("حذف این پشتیبان‌گیری", "Delete this backup job"), () =>
            {
                if (!ConfirmWindow.AskAlways(L.T("حذف پشتیبان‌گیری", "Delete backup job"),
                        L.T($"«{job.Name}» از لیست برداشته می‌شه. فایل‌های کپی‌شده توی مقصد دست‌نخورده می‌مونن.", $"“{job.Name}” is removed from the list. The copied files at the destination are left untouched."),
                        L.T("حذف کن", "Delete"), danger: true)) return;
                Hub.State.Backups.Remove(job);
                Hub.Store.Save();
                RefreshJobs();
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
    private readonly Button _clean = new() { Style = St("BtnDanger"), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed };
    private readonly Dictionary<string, CheckBox> _checks = [];
    private List<CleanScan> _scans = [];
    private bool _busy;

    public override string PageTitle => L.T("فایل و پشتیبان", "Files & backup");
    public override string PageSubtitle => L.T("فایل‌های موقت و کش‌هایی که برنامه‌ها خودشون دوباره می‌سازن. اسناد، رمزها و تاریخچه هیچ‌وقت دست نمی‌خورن.",
        "Temporary files and caches that programs rebuild themselves. Documents, passwords and history are never touched.");

    public CleanupPage()
    {
        var root = new StackPanel();
        var (card, body) = Ui2.Card(L.T("چی جمع شده؟", "What has piled up?"));
        var scan = new Button { Style = St("BtnPrimary"), Content = L.T("بررسی", "Scan"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        scan.Click += (_, _) => Scan();
        body.Children.Add(scan);
        body.Children.Add(_status);
        _list.Margin = new Thickness(0, 8, 0, 0);
        body.Children.Add(_list);
        _clean.Click += (_, _) => Clean();
        body.Children.Add(_clean);
        root.Children.Add(card);
        Content = root;
    }

    private void Scan()
    {
        if (_busy) return;
        _busy = true;
        _status.Text = L.T("در حال بررسی…", "Scanning…");
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
        _list.Children.Clear();
        _checks.Clear();
        var total = scans.Where(s => !s.Blocked).Sum(s => s.Bytes);
        _status.Text = L.T($"در مجموع {Fa.Bytes(total)} قابل پاک‌سازیه.", $"{Fa.Bytes(total)} can be cleaned in total.");
        foreach (var s in scans)
        {
            var row = new Grid { Margin = new Thickness(0, 6, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var check = new CheckBox { Style = St("Check"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), IsEnabled = !s.Blocked && s.Files > 0 };
            check.Click += (_, _) => Update();
            _checks[s.Category.Id] = check;
            row.Children.Add(check);
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = s.Category.Name, Style = St("Body"), FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock { Text = s.Category.Note + (s.Blocked ? L.T(" (نیاز به اجرای ادمین)", " (needs administrator)") : ""), Style = St("Label"), TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            var size = new TextBlock
            {
                Text = s.Files == 0 ? L.T("چیزی نیست", "nothing") : $"{Fa.Bytes(s.Bytes)} · {Fa.Digits(s.Files.ToString())}", Style = St("Label"), VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0), FlowDirection = FlowDirection.LeftToRight,
            };
            Grid.SetColumn(size, 2);
            row.Children.Add(size);
            _list.Children.Add(row);
        }
        Update();
    }

    private List<CleanScan> Chosen() => [.. _scans.Where(s => _checks.TryGetValue(s.Category.Id, out var c) && c.IsChecked == true)];

    private void Update()
    {
        var chosen = Chosen();
        _clean.Visibility = chosen.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _clean.Content = L.T($"پاک کردن {Fa.Bytes(chosen.Sum(s => s.Bytes))}", $"Clean {Fa.Bytes(chosen.Sum(s => s.Bytes))}");
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
        _status.Text = L.T("در حال پاک‌سازی…", "Cleaning…");
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
