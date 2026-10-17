using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>«کارها و تمرکز»: the to-do list, a focus timer and the weekly report. Everything stays on this PC.</summary>
public sealed class TasksPage : TabsPage
{
    public static readonly string[] TabKeys = ["todo", "focus", "report"];

    public override string PageTitle => L.T("کارها و تمرکز", "Tasks & focus");

    public TasksPage()
    {
        Add("todo", L.T("کارها", "Tasks"), new TodoPage());
        Add("focus", L.T("تمرکز", "Focus"), new FocusPage());
        Add("report", L.T("گزارش هفتگی", "Weekly report"), new WeeklyReportPage());
    }
}

/// <summary>The task list: add, edit, finish and delete, with deadlines, reminders and categories.</summary>
public sealed class TodoPage : PageBase
{
    private static readonly (string fa, string en, int days)[] Days =
    [
        ("بدون موعد", "No date", -1), ("امروز", "Today", 0), ("فردا", "Tomorrow", 1), ("پس‌فردا", "In 2 days", 2), ("هفته‌ی بعد", "Next week", 7),
    ];

    private static readonly (string fa, string en, int minutes)[] Reminders =
    [
        ("بدون یادآور", "No reminder", -1), ("سر موعد", "At the time", 0), ("۱۰ دقیقه قبل", "10 min before", 10), ("یک ساعت قبل", "1 hour before", 60), ("یک روز قبل", "1 day before", 1440),
    ];

    private static readonly (TaskView view, string fa, string en)[] ViewDefs =
    [
        (TaskView.Today, "امروز", "Today"), (TaskView.Upcoming, "پیش‌رو", "Upcoming"), (TaskView.NoDate, "بدون موعد", "No date"), (TaskView.Done, "انجام‌شده", "Done"),
    ];

    private readonly TextBox _title = new() { Style = St("Input"), MaxLength = TaskBook.MaxTitle };
    private readonly TextBox _category = new() { Style = St("Input"), MaxLength = 40, Width = 170 };
    private readonly TextBox _hour = new() { Style = St("Input"), Width = 56, Padding = new Thickness(8, 6, 8, 6), FlowDirection = FlowDirection.LeftToRight, TextAlignment = TextAlignment.Center, Text = "09" };
    private readonly TextBox _minute = new() { Style = St("Input"), Width = 56, Padding = new Thickness(8, 6, 8, 6), FlowDirection = FlowDirection.LeftToRight, TextAlignment = TextAlignment.Center, Text = "00" };
    private readonly CheckBox _important = new() { Style = St("Check"), Content = L.T("مهمه", "Important") };
    private readonly WrapPanel _dayChips = new();
    private readonly WrapPanel _remindChips = new();
    private readonly WrapPanel _categoryChips = new();
    private readonly Segmented _views = new(ViewDefs.Select(v => L.T(v.fa, v.en)).ToList(), 0, compact: true) { Margin = new Thickness(0, 12, 0, 4) };
    private readonly StackPanel _list = new();
    private readonly TextBlock _hint = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly Button _save = new() { Style = St("BtnPrimary"), Padding = new Thickness(20, 9, 20, 9) };
    private readonly TextBlock _saveText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Icon _saveIcon = new("plus", 18) { Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _cancelEdit = new() { Style = St("Btn"), Padding = new Thickness(18, 9, 18, 9), Content = L.T("انصراف از ویرایش", "Cancel editing"), Visibility = Visibility.Collapsed, Margin = new Thickness(8, 0, 0, 0) };
    private readonly Switch _remindersOn = new();
    private readonly CMo.Memo _rows;

    private int _dayOffset = 1; // chosen day chip (days from today), -1 none
    private int _remind = 0;
    private TaskView _view = TaskView.Today;
    private string _filter = "";
    private Guid? _editing;
    private bool _loading;

    public override string PageTitle => L.T("کارها و تمرکز", "Tasks & focus");
    public override string PageSubtitle => L.T("کارهات رو بنویس، موعد و یادآور بذار و هر روز ببین چی مونده. همه‌چی فقط روی همین کامپیوتر می‌مونه.",
        "Write your tasks, set deadlines and reminders, and see what is left each day. Everything stays on this PC.");

    public TodoPage()
    {
        var root = new StackPanel();
        _rows = new CMo.Memo(_list);

        // ---- new task ---------------------------------------------------------------------------
        var (card, body) = Ui2.Card(L.T("کار جدید", "New task"));
        card.Padding = new Thickness(24, 20, 24, 20);
        _title.Margin = new Thickness(0, 12, 0, 0);
        _title.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Save();
        };
        body.Children.Add(_title);

        var form = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        form.ColumnDefinitions.Add(new ColumnDefinition());
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        form.ColumnDefinitions.Add(new ColumnDefinition());
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var left = new StackPanel();
        left.Children.Add(Ui2.Label(L.T("موعد", "Due"), 12, 4));
        BuildChips(_dayChips, Days.Select(d => (L.T(d.fa, d.en), d.days)).ToList(), () => _dayOffset, v => _dayOffset = v);
        left.Children.Add(_dayChips);
        var timeRow = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        timeRow.Children.Add(_hour);
        timeRow.Children.Add(new TextBlock { Text = ":", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Res("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) });
        timeRow.Children.Add(_minute);
        TimeBoxes.Wire(_hour, _minute);
        left.Children.Add(Ui2.Label(L.T("ساعت", "Time"), 8, 4));
        left.Children.Add(timeRow);

        var right = new StackPanel();
        right.Children.Add(Ui2.Label(L.T("یادآور", "Reminder"), 12, 4));
        BuildChips(_remindChips, Reminders.Select(r => (L.T(r.fa, r.en), r.minutes)).ToList(), () => _remind, v => _remind = v);
        right.Children.Add(_remindChips);
        var more = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        var catCell = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        catCell.Children.Add(new TextBlock { Text = L.T("دسته (اختیاری)", "Category (optional)"), Style = St("Label"), Margin = new Thickness(0, 4, 0, 0) });
        _category.Margin = new Thickness(0, 4, 0, 0);
        catCell.Children.Add(_category);
        more.Children.Add(catCell);
        _important.VerticalAlignment = VerticalAlignment.Bottom;
        _important.Margin = new Thickness(0, 0, 0, 3);
        more.Children.Add(_important);
        right.Children.Add(more);

        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 2);
        form.Children.Add(left);
        form.Children.Add(right);
        bool? wide = null;
        void Arrange(bool w)
        {
            if (wide == w) return;
            wide = w;
            Grid.SetRow(right, w ? 0 : 1);
            Grid.SetColumn(right, w ? 2 : 0);
            Grid.SetColumnSpan(left, w ? 1 : 3);
            Grid.SetColumnSpan(right, w ? 1 : 3);
            if (w) Grid.SetColumn(left, 0);
        }
        card.SizeChanged += (_, e) => Arrange(e.NewSize.Width > 760);
        Arrange(true);
        body.Children.Add(form);

        var saveRow = new StackPanel { Orientation = Orientation.Horizontal };
        saveRow.Children.Add(_saveIcon);
        saveRow.Children.Add(_saveText);
        _save.Content = saveRow;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        _save.Click += (_, _) => Save();
        _cancelEdit.Click += (_, _) => ResetForm();
        buttons.Children.Add(_save);
        buttons.Children.Add(_cancelEdit);
        body.Children.Add(buttons);
        body.Children.Add(_hint);
        root.Children.Add(card);

        // ---- filters + reminder switch -----------------------------------------------------------
        var (listCard, list) = Ui2.Card(L.T("کارهات", "Your tasks"));
        listCard.Padding = new Thickness(24, 20, 24, 12);
        _views.Changed += i =>
        {
            _view = ViewDefs[i].view;
            _rows.Reset();
            RefreshList();
        };
        list.Children.Add(_views);
        _categoryChips.Margin = new Thickness(0, 4, 0, 0);
        list.Children.Add(_categoryChips);
        _remindersOn.Click += (_, _) =>
        {
            if (_loading) return;
            Hub.Settings.TaskReminders = _remindersOn.IsChecked == true;
            Hub.Store.Save();
        };
        list.Children.Add(K.SettingRow(L.T("وقتی زمان یادآور رسید اعلان بده", "Show a notice when a reminder is due"), _remindersOn));
        root.Children.Add(listCard);

        // ---- the task rows (one lifting card each) ------------------------------------------------
        _list.Margin = new Thickness(0, 12, 0, 0);
        root.Children.Add(_list);
        Content = root;
        CMo.CascadeOnLoad(this, root);

        Hub.TasksChanged += () => Dispatcher.BeginInvoke(() =>
        {
            if (IsVisible) RefreshList();
        });
        ResetForm();
    }

    private static void BuildChips(WrapPanel panel, List<(string text, int value)> items, Func<int> get, Action<int> set)
    {
        var group = "chips" + Guid.NewGuid().ToString("N");
        foreach (var (text, value) in items)
        {
            var chip = new RadioButton { Style = St("Chip"), GroupName = group, Content = text, Margin = new Thickness(0, 0, 8, 6), IsChecked = get() == value };
            chip.Checked += (_, _) =>
            {
                set(value);
                if (chip.IsLoaded) Motion.Pop(chip);
            };
            panel.Children.Add(chip);
        }
    }

    private void SelectChip(WrapPanel panel, int index)
    {
        if (index >= 0 && index < panel.Children.Count) ((RadioButton)panel.Children[index]).IsChecked = true;
    }

    private void ResetForm()
    {
        _editing = null;
        _title.Text = "";
        _category.Text = "";
        _important.IsChecked = false;
        _dayOffset = 1;
        _remind = 0;
        SelectChip(_dayChips, 2);
        SelectChip(_remindChips, 1);
        var soon = DateTime.Now.AddHours(1);
        _hour.Text = soon.Hour.ToString("00");
        _minute.Text = "00";
        _saveText.Text = L.T("افزودن کار", "Add task");
        if (_saveIcon.Glyph != "plus" && _save.IsLoaded) Motion.Pop(_saveIcon);
        _saveIcon.Glyph = "plus";
        _cancelEdit.Visibility = Visibility.Collapsed;
        _hint.Text = "";
    }

    /// <summary>The line under the form: new text fades in; an error also nudges the save button.</summary>
    private void Hint(string text, string brush)
    {
        _hint.Foreground = Res(brush);
        _hint.Text = text;
        CMo.Flash(_hint);
        if (brush == "Red" && _save.IsLoaded) Motion.Pop(_save);
    }

    private void Save()
    {
        var title = _title.Text.Trim();
        if (title.Length == 0)
        {
            Hint(L.T("اول بنویس چه کاری باید انجام بشه.", "Write what has to be done first."), "Red");
            _title.Focus();
            return;
        }
        DateTime? due = null;
        if (_dayOffset >= 0)
        {
            if (!TimeBoxes.TryRead(_hour, _minute, out var h, out var m))
            {
                Hint(L.T("ساعت درست نیست.", "The time is not valid."), "Red");
                return;
            }
            due = DateTime.Now.Date.AddDays(_dayOffset).AddHours(h).AddMinutes(m);
        }
        var book = Hub.Tasks;
        var ok = _editing is { } id
            ? book.Update(id, title, due, _remind, _category.Text, "", _important.IsChecked == true)
            : book.Add(title, due, _remind, _category.Text, "", _important.IsChecked == true) is not null;
        if (!ok)
        {
            Hint(L.T("ذخیره نشد؛ احتمالاً لیست پر شده.", "Could not save; the list may be full."), "Red");
            return;
        }
        var wasEditing = _editing is not null;
        ResetForm();
        Hint(wasEditing ? L.T("ذخیره شد.", "Saved.") : L.T("اضافه شد.", "Added."), "Muted");
        _title.Focus();
    }

    private void Edit(TodoItem item)
    {
        _editing = item.Id;
        _title.Text = item.Title;
        _category.Text = item.Category;
        _important.IsChecked = item.Important;
        if (item.Due is { } due)
        {
            var days = (due.Date - DateTime.Now.Date).Days;
            _dayOffset = Days.Any(d => d.days == days) ? days : 0;
            SelectChip(_dayChips, Array.FindIndex(Days, d => d.days == _dayOffset));
            _hour.Text = due.Hour.ToString("00");
            _minute.Text = due.Minute.ToString("00");
        }
        else
        {
            _dayOffset = -1;
            SelectChip(_dayChips, 0);
        }
        var r = Array.FindIndex(Reminders, x => x.minutes == item.RemindBefore);
        _remind = r >= 0 ? item.RemindBefore : 0;
        SelectChip(_remindChips, r >= 0 ? r : 1);
        _saveText.Text = L.T("ذخیره‌ی تغییرات", "Save changes");
        _saveIcon.Glyph = "chk";
        _cancelEdit.Visibility = Visibility.Visible;
        _hint.Text = "";
        if (_save.IsLoaded)
        {
            Motion.Pop(_saveIcon);
            CMo.Appear(_cancelEdit, 0, 8, 0.45);
            Motion.Pop(_title);
        }
        _title.Focus();
        _title.CaretIndex = _title.Text.Length;
    }

    public override void Refresh()
    {
        _loading = true;
        _remindersOn.IsChecked = Hub.Settings.TaskReminders;
        _loading = false;
        RefreshList();
    }

    private void RefreshList()
    {
        var now = DateTime.Now;
        var book = Hub.Tasks;

        // the segmented control keeps its sliding thumb; only the counts in its labels change
        for (var i = 0; i < ViewDefs.Length; i++)
        {
            var count = book.View(ViewDefs[i].view, now, _filter).Count();
            _views.SetLabel(i, $"{L.T(ViewDefs[i].fa, ViewDefs[i].en)} ({Fa.Digits(count.ToString())})");
        }
        _views.Select(Array.FindIndex(ViewDefs, v => v.view == _view), raise: false);

        _categoryChips.Children.Clear();
        var categories = book.Categories();
        if (_filter.Length > 0 && !categories.Contains(_filter)) _filter = "";
        if (categories.Count > 0)
        {
            var catGroup = "cat" + GetHashCode();
            void Add(string text, string value)
            {
                var chip = new RadioButton { Style = St("Chip"), GroupName = catGroup, Content = text, Margin = new Thickness(0, 0, 8, 6), IsChecked = _filter == value };
                chip.Checked += (_, _) =>
                {
                    _filter = value;
                    _rows.Reset();
                    RefreshList();
                };
                _categoryChips.Children.Add(chip);
            }
            Add(L.T("همه‌ی دسته‌ها", "All categories"), "");
            foreach (var c in categories) Add(c, c);
        }

        _list.Children.Clear();
        _rows.Begin();
        var items = book.View(_view, now, _filter).Take(200).ToList();
        if (items.Count == 0)
        {
            var text = _view switch
            {
                TaskView.Today => L.T("برای امروز کاری نداری. می‌تونی از بالا یه کار اضافه کنی.", "Nothing for today. Add a task above."),
                TaskView.Upcoming => L.T("کار پیش‌رویی نیست.", "Nothing coming up."),
                TaskView.NoDate => L.T("کاری بدون موعد نداری.", "No tasks without a date."),
                _ => L.T("هنوز کاری انجام نشده.", "Nothing finished yet."),
            };
            var empty = new StackPanel { Orientation = Orientation.Horizontal };
            empty.Children.Add(new IconBadge("tasks", "HueA", 40, 20, 12) { Margin = new Thickness(0, 0, 14, 0) });
            empty.Children.Add(new TextBlock { Text = text, Style = St("Body"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
            var emptyCard = new GlassCard { Padding = new Thickness(24, 18, 24, 18), Child = empty };
            _list.Children.Add(_rows.Item("empty:" + _view, emptyCard));
            _rows.End();
            return;
        }
        var index = 0;
        foreach (var item in items) _list.Children.Add(_rows.Item(item.Id.ToString(), Row(item, now, index++ < 30)));
        _rows.End();
        if (_view == TaskView.Done)
        {
            var clear = new Button { Style = St("Btn"), Padding = new Thickness(16, 9, 16, 9), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
            var clearRow = new StackPanel { Orientation = Orientation.Horizontal };
            clearRow.Children.Add(new Icon("trash", 16) { Margin = new Thickness(0, 0, 8, 0) });
            clearRow.Children.Add(new TextBlock { Text = L.T("پاک کردن انجام‌شده‌های قدیمی‌تر از یک ماه", "Clear finished tasks older than a month"), VerticalAlignment = VerticalAlignment.Center });
            clear.Content = clearRow;
            clear.Click += (_, _) =>
            {
                var cutoff = DateTime.Now.AddMonths(-1);
                var n = Hub.Tasks.All.Count(t => t.Done && (t.DoneAt ?? t.Created) < cutoff);
                if (n == 0) return;
                if (!ConfirmWindow.AskAlways(L.T("پاک کردن کارهای انجام‌شده", "Clear finished tasks"),
                        L.T($"{Fa.Digits(n.ToString())} کار انجام‌شده‌ی قدیمی‌تر از یک ماه برای همیشه پاک می‌شن.", $"{n} finished task(s) older than a month will be deleted for good."),
                        L.T("پاک کن", "Delete"), danger: true)) return;
                Hub.Tasks.ClearDone(cutoff);
            };
            _list.Children.Add(clear);
        }
    }

    private FrameworkElement Row(TodoItem item, DateTime now, bool elevated)
    {
        var overdue = TaskBook.IsOverdue(item, now);
        var card = new GlassCard { Padding = new Thickness(16, 12, 12, 12), Margin = new Thickness(0, 0, 0, 10), Lift = true, Elevated = elevated };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var done = new CheckBox
        {
            Style = St("Check"), IsChecked = item.Done, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0),
            ToolTip = item.Done ? L.T("برگردوندن به کارهای باز", "Mark as not done") : L.T("انجام شد", "Mark as done"),
        };
        System.Windows.Automation.AutomationProperties.SetName(done, item.Title);
        done.Click += (_, _) =>
        {
            var finished = done.IsChecked == true;
            // the row springs out first (it leaves this view either way), then the list changes
            CMo.Leave(card, () =>
            {
                if (finished) Hub.Tasks.Complete(item.Id);
                else Hub.Tasks.Reopen(item.Id);
            });
        };
        grid.Children.Add(done);

        var (icon, tint) = item.Done ? ("chk", "HueB") : overdue ? ("alarm", "HueE") : item.Important ? ("star", "HueC") : ("tasks", "HueA");
        var badge = new IconBadge(icon, tint, 38, 19, 12);
        Grid.SetColumn(badge, 1);
        grid.Children.Add(badge);
        card.MouseEnter += (_, _) => badge.Pose(true);
        card.MouseLeave += (_, _) => badge.Pose(false);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 10, 0) };
        var title = new TextBlock
        {
            Text = (item.Important ? "★ " : "") + item.Title, FontSize = 14.5, FontWeight = FontWeights.Bold, Foreground = Res("Text"), TextWrapping = TextWrapping.Wrap,
            TextDecorations = item.Done ? TextDecorations.Strikethrough : null, Opacity = item.Done ? 0.55 : 1,
        };
        text.Children.Add(title);
        var parts = new List<string>();
        if (item.Due is { } due) parts.Add(Fa.DayOf(due, now));
        else if (!item.Done) parts.Add(L.T("بدون موعد", "No date"));
        if (item.Done && item.DoneAt is { } at) parts.Add(L.T($"انجام شد: {Fa.DayOf(at, now)}", $"Done: {Fa.DayOf(at, now)}"));
        if (item.Category.Length > 0) parts.Add(item.Category);
        if (!item.Done && item.RemindBefore >= 0 && item.RemindedAt is null) parts.Add(L.T("⏰ یادآور", "⏰ reminder"));
        if (overdue) parts.Insert(0, L.T("گذشته", "Overdue"));
        text.Children.Add(new TextBlock
        {
            Text = string.Join("  ·  ", parts), Style = St("Label"), Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap,
            Foreground = overdue ? Res("Red") : Res("Muted"),
        });
        Grid.SetColumn(text, 2);
        grid.Children.Add(text);

        if (item.Due is { } when)
        {
            var time = new TextBlock
            {
                Text = Fa.Clock(when), FontSize = 17, FontWeight = FontWeights.Bold, FontFamily = (FontFamily)Application.Current.Resources["Num"], FlowDirection = FlowDirection.LeftToRight,
                Foreground = overdue ? Res("Red") : item.Done ? Res("Muted") : Res("Text"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0),
            };
            Grid.SetColumn(time, 3);
            grid.Children.Add(time);
        }

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Button Small(string glyph, string tip, Action click, string? brush = null)
        {
            var b = IconButton(glyph, tip, click);
            System.Windows.Automation.AutomationProperties.SetName(b, tip);
            if (brush is not null) b.Foreground = Res(brush);
            return b;
        }
        if (!item.Done)
        {
            if (overdue || item.RemindedAt is not null)
                actions.Children.Add(Small("bell", L.T("یک ساعت دیگه یادم بنداز", "Remind me in an hour"), () => Hub.Tasks.Snooze(item.Id, 60, DateTime.Now)));
            actions.Children.Add(Small("edit", L.T("ویرایش", "Edit"), () => Edit(item)));
        }
        actions.Children.Add(Small("trash", L.T("حذف", "Delete"), () =>
        {
            if (!ConfirmWindow.Ask(L.T("حذف کار", "Delete task"), L.T($"«{item.Title}» حذف می‌شه.", $"“{item.Title}” will be deleted."), L.T("حذف کن", "Delete"), danger: true)) return;
            CMo.Leave(card, () => Hub.Tasks.Remove(item.Id));
        }, "Red"));
        Grid.SetColumn(actions, 4);
        grid.Children.Add(actions);
        card.Child = grid;
        return card;
    }
}

/// <summary>A focus and break timer with optional task name, and today's focus time.</summary>
public sealed class FocusPage : PageBase
{
    private readonly RingGauge _ring = new() { Width = 252, Height = 252 };
    private readonly TextBlock _phase = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _clock = new() { FontSize = 46, FontWeight = FontWeights.Bold, FontFamily = (FontFamily)Application.Current.Resources["Num"], HorizontalAlignment = HorizontalAlignment.Center, FlowDirection = FlowDirection.LeftToRight, LineHeight = 54, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
    private readonly TextBlock _round = new() { Style = St("Label"), HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 150 };
    private readonly TextBox _task = new() { Style = St("Input"), MaxLength = TaskBook.MaxTitle, Margin = new Thickness(0, 4, 0, 0) };
    private readonly Button _start = new() { Style = St("BtnPrimary"), Padding = new Thickness(22, 10, 22, 10), Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _pause = new() { Style = St("Btn"), Padding = new Thickness(18, 10, 18, 10), Margin = new Thickness(0, 0, 8, 0) };
    private readonly Icon _pauseIcon = new("pause", 17) { Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBlock _pauseText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _skip = new() { Style = St("Btn"), Padding = new Thickness(18, 10, 18, 10), Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _stop = new() { Style = St("BtnDanger"), Padding = new Thickness(18, 10, 18, 10) };
    private readonly TextBlock _today = new() { Style = St("Body"), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _sessionsValue = new() { FontSize = 26, FontWeight = FontWeights.Bold, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _minutesValue = new() { FontSize = 26, FontWeight = FontWeights.Bold, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel _recent = new();
    private bool _loading;
    private Slider _focus = null!, _short = null!, _long = null!, _rounds = null!;
    private readonly Switch _auto = new();
    private readonly Blocks _root = new();
    private readonly CMo.Memo _recentRows;
    private bool? _wasRunning;
    private FocusPhase? _lastPhase;
    private bool _lastPaused;

    public override string PageTitle => L.T("کارها و تمرکز", "Tasks & focus");
    public override string PageSubtitle => L.T("تایمر تمرکز: مدتی کامل روی یه کار کار کن، بعد یه استراحت کوتاه.", "A focus timer: work on one thing for a while, then take a short break.");

    public FocusPage()
    {
        GroupC.EnsureIcons();

        // ---- hero: ring with the clock, the task box and the buttons ---------------------------------
        var hero = new GlassCard { Padding = new Thickness(0), Background = Res("HeroGradient") };
        var decor = new Border { Background = Res("HeroGlow"), IsHitTestVisible = false };
        var inner = new Grid { Margin = new Thickness(30, 26, 30, 26) };
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inner.ColumnDefinitions.Add(new ColumnDefinition());

        var ringBox = new Grid { Width = 252, Height = 252, Margin = new Thickness(0, 0, 34, 0), VerticalAlignment = VerticalAlignment.Center };
        ringBox.Children.Add(_ring);
        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        _phase.Foreground = Res("Accent");
        _clock.Foreground = Res("Text");
        mid.Children.Add(_phase);
        mid.Children.Add(_clock);
        mid.Children.Add(_round);
        ringBox.Children.Add(mid);
        inner.Children.Add(ringBox);

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(info, 1);
        var eyebrow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        eyebrow.Children.Add(new IconBadge("clk", "HueA", 36, 19, 11) { Margin = new Thickness(0, 0, 12, 0) });
        eyebrow.Children.Add(new TextBlock { Text = L.T("تایمر تمرکز", "Focus timer"), FontSize = 19, FontWeight = FontWeights.Bold, Foreground = Res("Text"), VerticalAlignment = VerticalAlignment.Center });
        info.Children.Add(eyebrow);
        info.Children.Add(new TextBlock { Text = L.T("روی چی تمرکز می‌کنی؟ (اختیاری)", "What are you focusing on? (optional)"), Style = St("Label") });
        info.Children.Add(_task);
        var row = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) };
        var startRow = new StackPanel { Orientation = Orientation.Horizontal };
        startRow.Children.Add(new Icon("play", 17) { Margin = new Thickness(0, 0, 8, 0) });
        startRow.Children.Add(new TextBlock { Text = L.T("شروع تمرکز", "Start focus"), VerticalAlignment = VerticalAlignment.Center });
        _start.Content = startRow;
        var pauseRow = new StackPanel { Orientation = Orientation.Horizontal };
        pauseRow.Children.Add(_pauseIcon);
        pauseRow.Children.Add(_pauseText);
        _pause.Content = pauseRow;
        var skipRow = new StackPanel { Orientation = Orientation.Horizontal };
        skipRow.Children.Add(new Icon("skip", 17) { Margin = new Thickness(0, 0, 8, 0) });
        skipRow.Children.Add(new TextBlock { Text = L.T("رد کردن", "Skip"), VerticalAlignment = VerticalAlignment.Center });
        _skip.Content = skipRow;
        var stopRow = new StackPanel { Orientation = Orientation.Horizontal };
        stopRow.Children.Add(new Icon("stop", 17) { Margin = new Thickness(0, 0, 8, 0) });
        stopRow.Children.Add(new TextBlock { Text = L.T("توقف", "Stop"), VerticalAlignment = VerticalAlignment.Center });
        _stop.Content = stopRow;
        row.Children.Add(_start);
        row.Children.Add(_pause);
        row.Children.Add(_skip);
        row.Children.Add(_stop);
        info.Children.Add(row);
        inner.Children.Add(info);
        _start.Click += (_, _) => Hub.Focus.Start(DateTime.Now, _task.Text);
        _pause.Click += (_, _) =>
        {
            if (Hub.Focus.Paused) Hub.Focus.Resume(DateTime.Now);
            else Hub.Focus.Pause(DateTime.Now);
            Hub.Store.Save();
            Update();
        };
        _skip.Click += (_, _) =>
        {
            Hub.Focus.Skip(DateTime.Now);
            Hub.Store.Save();
        };
        _stop.Click += (_, _) =>
        {
            Hub.Focus.Stop(DateTime.Now);
            Hub.Store.Save();
        };
        var outer = new Grid();
        outer.Children.Add(decor);
        outer.Children.Add(inner);
        outer.SizeChanged += (_, e) => outer.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 17, 17);
        hero.Child = outer;
        _root.Add(hero, Blocks.At(0, 0, 12));

        // ---- durations ---------------------------------------------------------------------------
        var (setCard, set) = Ui2.Card(L.T("مدت‌ها", "Durations"));
        setCard.Padding = new Thickness(24, 20, 24, 18);
        Slider SliderRow(string label, double min, double max, Func<double, string> format, out TextBlock value)
        {
            var r = Ui2.SliderRow(label, min, max, 1, format);
            r.row.Margin = new Thickness(0, 10, 0, 0);
            set.Children.Add(r.row);
            value = r.value;
            return r.slider;
        }
        string Min(double v) => L.T($"{Fa.Digits(((int)v).ToString())} دقیقه", $"{(int)v} min");
        _focus = SliderRow(L.T("تمرکز", "Focus"), 5, 90, Min, out _);
        _short = SliderRow(L.T("استراحت کوتاه", "Short break"), 1, 30, Min, out _);
        _long = SliderRow(L.T("استراحت بلند", "Long break"), 5, 60, Min, out _);
        _rounds = SliderRow(L.T("دور تا استراحت بلند", "Rounds before long break"), 2, 8, v => Fa.Digits(((int)v).ToString()), out _);
        set.Children.Add(K.SettingRow(L.T("بعد از هر مرحله خودکار ادامه بده", "Continue to the next step by itself"), _auto, hint: null));
        ((FrameworkElement)set.Children[^1]).Margin = new Thickness(0, 12, 0, 0);
        void Save()
        {
            if (_loading) return;
            var f = Hub.Settings.Focus;
            f.FocusMinutes = (int)_focus.Value;
            f.ShortBreakMinutes = (int)_short.Value;
            f.LongBreakMinutes = (int)_long.Value;
            f.RoundsBeforeLongBreak = (int)_rounds.Value;
            f.AutoContinue = _auto.IsChecked == true;
            Hub.Store.Save();
        }
        foreach (var s in new[] { _focus, _short, _long, _rounds }) s.ValueChanged += (_, _) => Save();
        _auto.Click += (_, _) => Save();
        _root.Add(setCard, Blocks.At(1, 0, 7));

        // ---- today -------------------------------------------------------------------------------
        var (todayCard, today) = Ui2.Card(L.T("امروز", "Today"));
        todayCard.Padding = new Thickness(24, 20, 24, 18);
        var stats = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        stats.ColumnDefinitions.Add(new ColumnDefinition());
        stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        stats.ColumnDefinitions.Add(new ColumnDefinition());
        var sessionsTile = MiniStat(_sessionsValue, L.T("جلسه‌ی کامل", "complete sessions"), "HueB");
        var minutesTile = MiniStat(_minutesValue, L.T("دقیقه تمرکز", "focus minutes"), "HueA");
        Grid.SetColumn(minutesTile, 2);
        stats.Children.Add(sessionsTile);
        stats.Children.Add(minutesTile);
        today.Children.Add(stats);
        _today.Margin = new Thickness(0, 12, 0, 0);
        today.Children.Add(_today);
        _recent.Margin = new Thickness(0, 6, 0, 0);
        today.Children.Add(_recent);
        _root.Add(todayCard, Blocks.At(1, 7, 5));

        Content = _root;
        _recentRows = new CMo.Memo(_recent);
        CMo.CascadeOnLoad(this, _root);
        Hub.TasksChanged += () => Dispatcher.BeginInvoke(Update);
    }

    private static Border MiniStat(TextBlock value, string caption, string tint)
    {
        value.Foreground = Res("Text");
        var box = new StackPanel();
        box.Children.Add(value);
        box.Children.Add(new TextBlock { Text = caption, Style = St("Label"), Margin = new Thickness(0, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        var tile = new Border { Style = St("Card"), Padding = new Thickness(16, 12, 16, 12), Child = box, Background = Res(tint + "Soft") };
        return tile;
    }

    public override void Refresh()
    {
        _loading = true;
        var f = Hub.Settings.Focus;
        _focus.Value = Math.Clamp(f.FocusMinutes, 5, 90);
        _short.Value = Math.Clamp(f.ShortBreakMinutes, 1, 30);
        _long.Value = Math.Clamp(f.LongBreakMinutes, 5, 60);
        _rounds.Value = Math.Clamp(f.RoundsBeforeLongBreak, 2, 8);
        _auto.IsChecked = f.AutoContinue;
        _loading = false;
        Update();
    }

    public override void OnSecond() => Update();

    private void Update()
    {
        var focus = Hub.Focus;
        var now = DateTime.Now;
        var running = focus.Phase != FocusPhase.Idle;
        var phaseText = focus.Phase switch
        {
            FocusPhase.Focus => L.T("تمرکز", "Focus") + (focus.Paused ? L.T(" (مکث)", " (paused)") : ""),
            FocusPhase.ShortBreak => L.T("استراحت کوتاه", "Short break") + (focus.Paused ? L.T(" (مکث)", " (paused)") : ""),
            FocusPhase.LongBreak => L.T("استراحت بلند", "Long break") + (focus.Paused ? L.T(" (مکث)", " (paused)") : ""),
            _ => L.T("آماده‌ای؟", "Ready?"),
        };
        CMo.Say(_phase, phaseText);
        var left = running ? focus.Remaining(now) : TimeSpan.FromMinutes(Math.Clamp(Hub.Settings.Focus.FocusMinutes, 1, 240));
        _clock.Text = Fa.Digits($"{(int)left.TotalMinutes:00}:{left.Seconds:00}");
        // the ring is what is left of the step; green for a break, gold while paused. A new step (start, stop, next phase,
        // pause) sweeps the arc to its place; within a step it simply follows the clock.
        var fraction = running ? Math.Clamp(1 - focus.Progress(now), 0, 1) : 1;
        var stepChanged = _wasRunning != running || _lastPhase != focus.Phase || _lastPaused != focus.Paused;
        _ring.ArcBrush = !running ? null : focus.Paused ? Res("HueC") : focus.Phase == FocusPhase.Focus ? null : Res("HueB");
        if (stepChanged && IsLoaded && Motion.Enabled) _ring.AnimateTo(fraction, _wasRunning is null ? 1400 : 900);
        else _ring.Fraction = fraction;
        _round.Text = running ? L.T($"دور {Fa.Digits(focus.Round.ToString())}", $"Round {focus.Round}") + (focus.Task.Length > 0 ? " · " + focus.Task : "") : "";
        _task.IsEnabled = !running;
        _start.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        _pause.Visibility = _skip.Visibility = _stop.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        _pauseText.Text = focus.Paused ? L.T("ادامه", "Resume") : L.T("مکث", "Pause");
        if (_pauseIcon.Glyph != (focus.Paused ? "play" : "pause") && _pause.IsLoaded) Motion.Pop(_pauseIcon);
        _pauseIcon.Glyph = focus.Paused ? "play" : "pause";
        if (_wasRunning is { } before && before != running && IsLoaded)
        {
            // the buttons that just appeared spring in one after another
            var shown = running ? new FrameworkElement[] { _pause, _skip, _stop } : [_start];
            for (var i = 0; i < shown.Length; i++) CMo.Appear(shown[i], i, 8, 0.45);
        }
        _wasRunning = running;
        _lastPhase = focus.Phase;
        _lastPaused = focus.Paused;

        var sessions = Hub.State.FocusLog.Where(s => s.Start.Date == now.Date).ToList();
        var minutes = sessions.Sum(s => s.Minutes);
        CMo.CountInt(_sessionsValue, sessions.Count(s => s.Completed));
        CMo.CountInt(_minutesValue, (int)Math.Round((double)minutes));
        _today.Text = sessions.Count == 0
            ? L.T("امروز هنوز جلسه‌ی تمرکزی نداشتی.", "No focus session yet today.")
            : L.T($"امروز {Fa.Digits(sessions.Count(s => s.Completed).ToString())} جلسه‌ی کامل و {Fa.Duration(TimeSpan.FromMinutes(minutes))} تمرکز داشتی.",
                $"Today: {sessions.Count(s => s.Completed)} complete session(s), {Fa.Duration(TimeSpan.FromMinutes(minutes))} of focus.");
        _recent.Children.Clear();
        _recentRows.Begin();
        foreach (var s in sessions.AsEnumerable().Reverse().Take(6))
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            line.Children.Add(new IconBadge(s.Completed ? "chk" : "clk", s.Completed ? "HueB" : "HueC", 26, 14, 8) { Margin = new Thickness(0, 0, 10, 0) });
            line.Children.Add(new TextBlock
            {
                Style = St("Label"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                Text = $"{Fa.Clock(s.Start)} · {Fa.Duration(TimeSpan.FromMinutes(s.Minutes))}" + (s.Completed ? "" : L.T(" · ناتمام", " · stopped early")) + (s.Task.Length > 0 ? " · " + s.Task : ""),
            });
            _recent.Children.Add(_recentRows.Item(s.Start.Ticks + ":" + s.Completed, line));
        }
        _recentRows.End();
    }
}

/// <summary>This week (or the one before) in numbers: tasks, focus time, and how the PC was used.</summary>
public sealed class WeeklyReportPage : PageBase
{
    private const double BarArea = 96;

    private readonly Blocks _root = new();
    private readonly TextBlock _weekTitle = new() { Style = St("H2") };
    private readonly Segmented _weekPick = new([L.T("این هفته", "This week"), L.T("هفته‌ی قبل", "Last week")], 0, compact: true) { Margin = new Thickness(0, 12, 0, 0) };
    private readonly TextBlock _best = new() { Style = St("Body"), Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly StatCard _completed = new(L.T("کار انجام‌شده", "tasks finished"), "chk", "HueB", meter: false);
    private readonly StatCard _onTime = new(L.T("سر موعد یا بی‌موعد", "on time"), "clk", "HueA");
    private readonly StatCard _focus = new(L.T("تمرکز", "focus"), "bolt", "HueD");
    private readonly StatCard _sessions = new(L.T("جلسه‌ی کامل", "focus sessions"), "play", "HueA");
    private readonly StatCard _streak = new(L.T("روز پشت‌سرهم", "day streak"), "star", "HueC");
    private readonly StatCard _overdue = new(L.T("کار گذشته‌ی باز", "overdue now"), "alarm", "HueE");
    private readonly StackPanel _taskBars = new();
    private readonly StackPanel _focusBars = new();
    private readonly StackPanel _pcBody = new();
    private bool _previous;

    public override string PageTitle => L.T("کارها و تمرکز", "Tasks & focus");
    public override string PageSubtitle => L.T("خلاصه‌ی هفته‌ات: کارهای انجام‌شده، تمرکز و استفاده از سیستم. فقط روی همین کامپیوتر ساخته می‌شه.",
        "Your week: finished tasks, focus time and how the PC was used. Built only on this PC.");

    public WeeklyReportPage()
    {
        // week title, picker, busiest day
        var head = new StackPanel();
        head.Children.Add(_weekTitle);
        head.Children.Add(_weekPick);
        head.Children.Add(_best);
        _weekPick.Changed += i =>
        {
            _previous = i == 1;
            Refresh();
        };
        _root.Add(new GlassCard { Padding = new Thickness(24, 20, 24, 20), Child = head }, Blocks.At(0, 0, 12));

        // six numbers, no sparkline (there is no series to draw)
        var cards = new[] { _completed, _onTime, _focus, _sessions, _streak, _overdue };
        for (var i = 0; i < cards.Length; i++)
        {
            cards[i].Spark!.Visibility = Visibility.Collapsed;
            cards[i].Padding = new Thickness(20, 18, 20, 18);
            _root.Add(cards[i], Blocks.At(1 + i / 3, (i % 3) * 4, 4), Blocks.At(1 + i / 2, (i % 2) * 6, 6));
        }

        _root.Add(new GlassCard { Padding = new Thickness(24, 20, 24, 18), Child = _taskBars }, Blocks.At(3, 0, 6), Blocks.At(4, 0, 12));
        _root.Add(new GlassCard { Padding = new Thickness(24, 20, 24, 18), Child = _focusBars }, Blocks.At(3, 6, 6), Blocks.At(5, 0, 12));

        var pc = new StackPanel();
        pc.Children.Add(new TextBlock { Text = L.T("استفاده از سیستم در این هفته", "PC use this week"), Style = St("H2") });
        _pcBody.Margin = new Thickness(0, 6, 0, 0);
        pc.Children.Add(_pcBody);
        _root.Add(new GlassCard { Padding = new Thickness(24, 20, 24, 14), Child = pc }, Blocks.At(4, 0, 12), Blocks.At(6, 0, 12));

        Content = _root;
        Loaded += (_, _) =>
        {
            // entrance: cards rise, the numbers count up from zero and the bars grow once the cards are in
            CMo.Cascade(_root);
            foreach (var card in cards) CMo.ResetCount(card.Value);
            _entering = true;
            Refresh();
            _entering = false;
        };
        Hub.TasksChanged += () =>
        {
            if (IsVisible) Dispatcher.BeginInvoke(Refresh);
        };
    }

    private static void Put(StatCard card, int value, string unit) => CMo.CountCard(card, value, unit);

    private readonly List<FrameworkElement> _barShapes = [];
    private bool _entering;

    /// <summary>One chart: a title and seven bars (Saturday-first in Persian, Monday-first in English), the value above each.
    /// The days run in the reading direction (Saturday at the start side).</summary>
    private void Bars(StackPanel host, string title, double[] values, Func<double, string> format, DateOnly start)
    {
        host.Children.Clear();
        host.Children.Add(new TextBlock { Text = title, Style = St("H2") });
        var max = Math.Max(1, values.Max());
        var grid = new Grid { Margin = new Thickness(0, 12, 0, 0), FlowDirection = Ui.Flow };
        for (var i = 0; i < 7; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var day = start.AddDays(i);
            var col = new StackPanel { Margin = new Thickness(5, 0, 5, 0) };
            var value = new TextBlock { Text = values[i] > 0 ? format(values[i]) : " ", Style = St("Label"), FontSize = 11.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4), FlowDirection = FlowDirection.LeftToRight };
            col.Children.Add(value);
            var area = new Grid { Height = BarArea };
            area.Children.Add(new Border { Background = Res("Line"), CornerRadius = new CornerRadius(7) });
            var bar = new Border
            {
                Background = values[i] <= 0 ? Res("Line") : Res("AccentBar"), CornerRadius = new CornerRadius(7), VerticalAlignment = VerticalAlignment.Bottom,
                Height = values[i] <= 0 ? 3 : Math.Max(6, BarArea * values[i] / max),
            };
            area.Children.Add(bar);
            _barShapes.Add(bar);
            col.Children.Add(area);
            col.Children.Add(new TextBlock { Text = L.En ? day.DayOfWeek.ToString()[..3] : Fa.DayLetter(day.DayOfWeek), Style = St("Label"), FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) });
            Grid.SetColumn(col, i);
            grid.Children.Add(col);
        }
        host.Children.Add(grid);
    }

    /// <summary>The bars of both charts grow up from their base, left to right, one after another.</summary>
    private void GrowBars(int delayMs)
    {
        for (var i = 0; i < _barShapes.Count; i++) CMo.Grow(_barShapes[i], i % 7 + i / 7, delayMs: delayMs);
    }

    public override void Refresh()
    {
        var now = DateTime.Now;
        var thisWeek = WeekMath.StartOf(now, persianWeek: !L.En);
        var start = _previous ? thisWeek.AddDays(-7) : thisWeek;
        var report = WeekReport.Build(Hub.Tasks.All, Hub.State.FocusLog, start, now);

        _weekTitle.Text = L.T("هفته‌ی ", "Week of ") + Fa.Date(start.ToDateTime(TimeOnly.MinValue));
        _weekPick.Select(_previous ? 1 : 0, raise: false);
        if (IsLoaded) { CMo.Flash(_weekTitle); CMo.Flash(_best); }
        _best.Visibility = report.BestDay is null ? Visibility.Collapsed : Visibility.Visible;
        if (report.BestDay is { } best) _best.Text = L.T($"پرکارترین روزت {Fa.DayName(best.DayOfWeek)} بود.", $"Your busiest day was {best.DayOfWeek}.");

        Put(_completed, report.Completed, L.T("کار", "tasks"));
        Put(_onTime, report.CompletedOnTime, L.T("کار", "tasks"));
        var focusText = Fa.Duration(TimeSpan.FromMinutes(report.FocusMinutes)).Replace(" و ", " ");
        Put(_focus, (int)Math.Round((double)report.FocusMinutes), L.T("دقیقه", "min"));
        _focus.ToolTip = focusText;
        Put(_sessions, report.FocusSessions, L.T("جلسه", "sessions"));
        Put(_streak, report.Streak, L.T("روز", "days"));
        Put(_overdue, report.OverdueNow, L.T("کار", "tasks"));

        _barShapes.Clear();
        Bars(_taskBars, L.T("کار انجام‌شده در روز", "Tasks finished per day"), report.CompletedPerDay.Select(x => (double)x).ToArray(), v => Fa.Digits(((int)v).ToString()), start);
        Bars(_focusBars, L.T("دقیقه‌ی تمرکز در روز", "Focus minutes per day"), report.FocusPerDay, v => Fa.Digits(((int)Math.Round(v)).ToString()), start);
        if (IsLoaded) GrowBars(_entering ? 420 : 0);

        // how the PC was used that week (existing data, nothing new is collected)
        var from = start.ToDateTime(TimeOnly.MinValue);
        var to = from.AddDays(7);
        var games = Hub.State.GameHistory.Where(g => g.Start >= from && g.Start < to).ToList();
        var energy = Hub.State.Energy.Where(e => e.Date >= start && e.Date < start.AddDays(7)).ToList();
        var (down, up) = Hub.Usage.Total(start, start.AddDays(6));
        var lines = new (string icon, string tint, string text)[]
        {
            ("mon", "HueD", games.Count == 0
                ? L.T("بازی‌ای ثبت نشده.", "No games recorded.")
                : L.T($"بازی: {Fa.Digits(games.Count.ToString())} نوبت، جمعاً {Fa.Duration(TimeSpan.FromMinutes(games.Sum(g => g.Minutes)))}",
                    $"Games: {games.Count} session(s), {Fa.Duration(TimeSpan.FromMinutes(games.Sum(g => g.Minutes)))} in total")),
            ("net", "HueB", L.T($"اینترنت مصرف‌شده: ↓{Fa.Bytes(down)} ↑{Fa.Bytes(up)}", $"Internet used: ↓{Fa.Bytes(down)} ↑{Fa.Bytes(up)}")),
            ("bolt", "HueC", energy.Count == 0
                ? L.T("مصرف برق هنوز اندازه‌گیری نشده.", "No power use measured yet.")
                : L.T($"مصرف برق: حدود {Fa.N(energy.Sum(e => e.TotalWh) / 1000, 1)} کیلووات‌ساعت", $"Power: about {Fa.N(energy.Sum(e => e.TotalWh) / 1000, 1)} kWh")),
        };
        _pcBody.Children.Clear();
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0) _pcBody.Children.Add(Divider());
            _pcBody.Children.Add(Row(lines[i].icon, lines[i].tint, lines[i].text, ""));
        }
    }
}
