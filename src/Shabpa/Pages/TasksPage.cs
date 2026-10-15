using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shabpa.Core;

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

    private readonly TextBox _title = new() { Style = St("Input"), MaxLength = TaskBook.MaxTitle };
    private readonly TextBox _category = new() { Style = St("Input"), MaxLength = 40, Width = 150 };
    private readonly TextBox _hour = new() { Style = St("Input"), Width = 56, Padding = new Thickness(8, 6, 8, 6), FlowDirection = FlowDirection.LeftToRight, TextAlignment = TextAlignment.Center, Text = "09" };
    private readonly TextBox _minute = new() { Style = St("Input"), Width = 56, Padding = new Thickness(8, 6, 8, 6), FlowDirection = FlowDirection.LeftToRight, TextAlignment = TextAlignment.Center, Text = "00" };
    private readonly CheckBox _important = new() { Style = St("Check"), Content = L.T("مهمه", "Important") };
    private readonly WrapPanel _dayChips = new();
    private readonly WrapPanel _remindChips = new();
    private readonly WrapPanel _viewChips = new();
    private readonly WrapPanel _categoryChips = new();
    private readonly StackPanel _list = new();
    private readonly TextBlock _hint = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button _save = new() { Style = St("BtnPrimary"), Padding = new Thickness(18, 9, 18, 9) };
    private readonly Button _cancelEdit = new() { Style = St("Btn"), Content = L.T("انصراف از ویرایش", "Cancel editing"), Visibility = Visibility.Collapsed, Margin = new Thickness(8, 0, 0, 0) };
    private readonly CheckBox _remindersOn = new() { Style = St("Check"), Content = L.T("وقتی زمان یادآور رسید اعلان بده", "Show a notice when a reminder is due"), Margin = new Thickness(0, 12, 0, 0) };

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
        var (card, body) = Ui2.Card(L.T("کار جدید", "New task"));
        _title.Margin = new Thickness(0, 10, 0, 0);
        _title.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Save();
        };
        body.Children.Add(_title);

        body.Children.Add(Ui2.Label(L.T("موعد", "Due"), 12, 4));
        BuildChips(_dayChips, Days.Select(d => (L.T(d.fa, d.en), d.days)).ToList(), () => _dayOffset, v => _dayOffset = v);
        body.Children.Add(_dayChips);

        var timeRow = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        timeRow.Children.Add(_hour);
        timeRow.Children.Add(new TextBlock { Text = ":", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Res("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) });
        timeRow.Children.Add(_minute);
        TimeBoxes.Wire(_hour, _minute);
        var timeLabel = new TextBlock { Text = L.T("ساعت", "Time"), Style = St("Label"), Margin = new Thickness(0, 10, 0, 0) };
        body.Children.Add(timeLabel);
        body.Children.Add(timeRow);

        body.Children.Add(Ui2.Label(L.T("یادآور", "Reminder"), 12, 4));
        BuildChips(_remindChips, Reminders.Select(r => (L.T(r.fa, r.en), r.minutes)).ToList(), () => _remind, v => _remind = v);
        body.Children.Add(_remindChips);

        var more = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var catCell = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        catCell.Children.Add(new TextBlock { Text = L.T("دسته (اختیاری)", "Category (optional)"), Style = St("Label") });
        _category.Margin = new Thickness(0, 4, 0, 0);
        catCell.Children.Add(_category);
        more.Children.Add(catCell);
        _important.VerticalAlignment = VerticalAlignment.Bottom;
        _important.Margin = new Thickness(0, 0, 0, 8);
        more.Children.Add(_important);
        body.Children.Add(more);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        _save.Click += (_, _) => Save();
        _cancelEdit.Click += (_, _) => ResetForm();
        buttons.Children.Add(_save);
        buttons.Children.Add(_cancelEdit);
        body.Children.Add(buttons);
        body.Children.Add(_hint);
        root.Children.Add(card);

        var (listCard, list) = Ui2.Card(L.T("کارهات", "Your tasks"));
        _viewChips.Margin = new Thickness(0, 10, 0, 0);
        list.Children.Add(_viewChips);
        _categoryChips.Margin = new Thickness(0, 4, 0, 0);
        list.Children.Add(_categoryChips);
        _list.Margin = new Thickness(0, 6, 0, 0);
        list.Children.Add(_list);
        _remindersOn.Click += (_, _) =>
        {
            if (_loading) return;
            Hub.Settings.TaskReminders = _remindersOn.IsChecked == true;
            Hub.Store.Save();
        };
        list.Children.Add(_remindersOn);
        root.Children.Add(listCard);
        Content = root;

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
            chip.Checked += (_, _) => set(value);
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
        _save.Content = L.T("افزودن کار", "Add task");
        _cancelEdit.Visibility = Visibility.Collapsed;
        _hint.Text = "";
    }

    private void Save()
    {
        var title = _title.Text.Trim();
        if (title.Length == 0)
        {
            _hint.Text = L.T("اول بنویس چه کاری باید انجام بشه.", "Write what has to be done first.");
            _hint.Foreground = Res("Red");
            _title.Focus();
            return;
        }
        DateTime? due = null;
        if (_dayOffset >= 0)
        {
            if (!TimeBoxes.TryRead(_hour, _minute, out var h, out var m))
            {
                _hint.Text = L.T("ساعت درست نیست.", "The time is not valid.");
                _hint.Foreground = Res("Red");
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
            _hint.Text = L.T("ذخیره نشد؛ احتمالاً لیست پر شده.", "Could not save; the list may be full.");
            _hint.Foreground = Res("Red");
            return;
        }
        var wasEditing = _editing is not null;
        ResetForm();
        _hint.Foreground = Res("Muted");
        _hint.Text = wasEditing ? L.T("ذخیره شد.", "Saved.") : L.T("اضافه شد.", "Added.");
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
        _save.Content = L.T("ذخیره‌ی تغییرات", "Save changes");
        _cancelEdit.Visibility = Visibility.Visible;
        _hint.Text = "";
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

        _viewChips.Children.Clear();
        var group = "view" + GetHashCode();
        foreach (var (view, fa, en) in new[]
        {
            (TaskView.Today, "امروز", "Today"), (TaskView.Upcoming, "پیش‌رو", "Upcoming"), (TaskView.NoDate, "بدون موعد", "No date"), (TaskView.Done, "انجام‌شده", "Done"),
        })
        {
            var count = book.View(view, now, _filter).Count();
            var chip = new RadioButton
            {
                Style = St("Chip"), GroupName = group, Margin = new Thickness(0, 0, 8, 6), IsChecked = _view == view,
                Content = $"{L.T(fa, en)} ({Fa.Digits(count.ToString())})",
            };
            var captured = view;
            chip.Checked += (_, _) =>
            {
                _view = captured;
                RefreshList();
            };
            _viewChips.Children.Add(chip);
        }

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
                    RefreshList();
                };
                _categoryChips.Children.Add(chip);
            }
            Add(L.T("همه‌ی دسته‌ها", "All categories"), "");
            foreach (var c in categories) Add(c, c);
        }

        _list.Children.Clear();
        var items = book.View(_view, now, _filter).Take(200).ToList();
        if (items.Count == 0)
        {
            _list.Children.Add(new TextBlock
            {
                Style = St("Body"), Margin = new Thickness(0, 14, 0, 6), TextWrapping = TextWrapping.Wrap,
                Text = _view switch
                {
                    TaskView.Today => L.T("برای امروز کاری نداری. می‌تونی از بالا یه کار اضافه کنی.", "Nothing for today. Add a task above."),
                    TaskView.Upcoming => L.T("کار پیش‌رویی نیست.", "Nothing coming up."),
                    TaskView.NoDate => L.T("کاری بدون موعد نداری.", "No tasks without a date."),
                    _ => L.T("هنوز کاری انجام نشده.", "Nothing finished yet."),
                },
            });
            return;
        }
        foreach (var item in items) _list.Children.Add(Row(item, now));
        if (_view == TaskView.Done)
        {
            var clear = new Button { Style = St("Btn"), Content = L.T("پاک کردن انجام‌شده‌های قدیمی‌تر از یک ماه", "Clear finished tasks older than a month"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
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

    private FrameworkElement Row(TodoItem item, DateTime now)
    {
        var overdue = TaskBook.IsOverdue(item, now);
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var done = new CheckBox
        {
            Style = St("Check"), IsChecked = item.Done, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0),
            ToolTip = item.Done ? L.T("برگردوندن به کارهای باز", "Mark as not done") : L.T("انجام شد", "Mark as done"),
        };
        System.Windows.Automation.AutomationProperties.SetName(done, item.Title);
        done.Click += (_, _) =>
        {
            if (done.IsChecked == true) Hub.Tasks.Complete(item.Id);
            else Hub.Tasks.Reopen(item.Id);
        };
        grid.Children.Add(done);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock
        {
            Text = (item.Important ? "★ " : "") + item.Title, Style = St("H2"), FontSize = 14, TextWrapping = TextWrapping.Wrap,
            TextDecorations = item.Done ? TextDecorations.Strikethrough : null, Opacity = item.Done ? 0.55 : 1,
        };
        text.Children.Add(title);
        var parts = new List<string>();
        if (item.Due is { } due) parts.Add($"{Fa.DayOf(due, now)} · {Fa.Clock(due)}");
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
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Button Small(string glyph, string tip, Action click)
        {
            var b = new Button { Style = St("Btn"), Content = new TextBlock { Text = glyph, Style = St("Icon") }, ToolTip = tip, Padding = new Thickness(9, 7, 9, 7), Margin = new Thickness(4, 0, 0, 0) };
            System.Windows.Automation.AutomationProperties.SetName(b, tip);
            b.Click += (_, _) => click();
            return b;
        }
        if (!item.Done)
        {
            if (overdue || item.RemindedAt is not null)
                actions.Children.Add(Small("", L.T("یک ساعت دیگه یادم بنداز", "Remind me in an hour"), () => Hub.Tasks.Snooze(item.Id, 60, DateTime.Now)));
            actions.Children.Add(Small("", L.T("ویرایش", "Edit"), () => Edit(item)));
        }
        actions.Children.Add(Small("", L.T("حذف", "Delete"), () =>
        {
            if (!ConfirmWindow.Ask(L.T("حذف کار", "Delete task"), L.T($"«{item.Title}» حذف می‌شه.", $"“{item.Title}” will be deleted."), L.T("حذف کن", "Delete"), danger: true)) return;
            Hub.Tasks.Remove(item.Id);
        }));
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);
        return grid;
    }
}

/// <summary>A focus and break timer with optional task name, and today's focus time.</summary>
public sealed class FocusPage : PageBase
{
    private readonly TextBlock _phase = new() { Style = St("H2"), HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _clock = new() { FontSize = 64, FontWeight = FontWeights.Bold, FontFamily = (FontFamily)Application.Current.Resources["Num"], HorizontalAlignment = HorizontalAlignment.Center, FlowDirection = FlowDirection.LeftToRight, Margin = new Thickness(0, 6, 0, 6) };
    private readonly ProgressBar _progress = new() { Height = 8, Minimum = 0, Maximum = 1, Margin = new Thickness(40, 4, 40, 12) };
    private readonly TextBlock _round = new() { Style = St("Label"), HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBox _task = new() { Style = St("Input"), MaxLength = TaskBook.MaxTitle, Margin = new Thickness(0, 14, 0, 0) };
    private readonly Button _start = new() { Style = St("BtnPrimary"), Padding = new Thickness(22, 10, 22, 10) };
    private readonly Button _pause = new() { Style = St("Btn"), Margin = new Thickness(8, 0, 0, 0) };
    private readonly Button _skip = new() { Style = St("Btn"), Margin = new Thickness(8, 0, 0, 0), Content = L.T("رد کردن", "Skip") };
    private readonly Button _stop = new() { Style = St("BtnDanger"), Margin = new Thickness(8, 0, 0, 0), Content = L.T("توقف", "Stop") };
    private readonly TextBlock _today = new() { Style = St("Body"), TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _recent = new();
    private bool _loading;
    private Slider _focus = null!, _short = null!, _long = null!, _rounds = null!;
    private readonly CheckBox _auto = new() { Style = St("Check"), Content = L.T("بعد از هر مرحله خودکار ادامه بده", "Continue to the next step by itself"), Margin = new Thickness(0, 10, 0, 0) };

    public override string PageTitle => L.T("کارها و تمرکز", "Tasks & focus");
    public override string PageSubtitle => L.T("تایمر تمرکز: مدتی کامل روی یه کار کار کن، بعد یه استراحت کوتاه.", "A focus timer: work on one thing for a while, then take a short break.");

    public FocusPage()
    {
        var root = new StackPanel();
        var (card, body) = Ui2.Card(L.T("تایمر تمرکز", "Focus timer"));
        body.Children.Add(new Border { Height = 8 });
        body.Children.Add(_phase);
        body.Children.Add(_clock);
        body.Children.Add(_progress);
        body.Children.Add(_round);
        body.Children.Add(new TextBlock { Text = L.T("روی چی تمرکز می‌کنی؟ (اختیاری)", "What are you focusing on? (optional)"), Style = St("Label"), Margin = new Thickness(0, 14, 0, 0) });
        body.Children.Add(_task);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0) };
        row.Children.Add(_start);
        row.Children.Add(_pause);
        row.Children.Add(_skip);
        row.Children.Add(_stop);
        body.Children.Add(row);
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
        root.Children.Add(card);

        var (setCard, set) = Ui2.Card(L.T("مدت‌ها", "Durations"));
        Slider Row(string label, double min, double max, Func<double, string> format, out TextBlock value)
        {
            var r = Ui2.SliderRow(label, min, max, 1, format);
            set.Children.Add(r.row);
            value = r.value;
            return r.slider;
        }
        string Min(double v) => L.T($"{Fa.Digits(((int)v).ToString())} دقیقه", $"{(int)v} min");
        _focus = Row(L.T("تمرکز", "Focus"), 5, 90, Min, out _);
        _short = Row(L.T("استراحت کوتاه", "Short break"), 1, 30, Min, out _);
        _long = Row(L.T("استراحت بلند", "Long break"), 5, 60, Min, out _);
        _rounds = Row(L.T("دور تا استراحت بلند", "Rounds before long break"), 2, 8, v => Fa.Digits(((int)v).ToString()), out _);
        set.Children.Add(_auto);
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
        root.Children.Add(setCard);

        var (todayCard, today) = Ui2.Card(L.T("امروز", "Today"));
        _today.Margin = new Thickness(0, 8, 0, 0);
        today.Children.Add(_today);
        _recent.Margin = new Thickness(0, 8, 0, 0);
        today.Children.Add(_recent);
        root.Children.Add(todayCard);
        Content = root;
        Hub.TasksChanged += () => Dispatcher.BeginInvoke(Update);
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
        _phase.Text = focus.Phase switch
        {
            FocusPhase.Focus => L.T("تمرکز", "Focus") + (focus.Paused ? L.T(" (مکث)", " (paused)") : ""),
            FocusPhase.ShortBreak => L.T("استراحت کوتاه", "Short break") + (focus.Paused ? L.T(" (مکث)", " (paused)") : ""),
            FocusPhase.LongBreak => L.T("استراحت بلند", "Long break") + (focus.Paused ? L.T(" (مکث)", " (paused)") : ""),
            _ => L.T("آماده‌ای؟", "Ready?"),
        };
        var left = running ? focus.Remaining(now) : TimeSpan.FromMinutes(Math.Clamp(Hub.Settings.Focus.FocusMinutes, 1, 240));
        _clock.Text = $"{(int)left.TotalMinutes:00}:{left.Seconds:00}";
        _progress.Value = running ? focus.Progress(now) : 0;
        _round.Text = running ? L.T($"دور {Fa.Digits(focus.Round.ToString())}", $"Round {focus.Round}") + (focus.Task.Length > 0 ? " · " + focus.Task : "") : "";
        _task.IsEnabled = !running;
        _start.Content = L.T("شروع تمرکز", "Start focus");
        _start.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        _pause.Visibility = _skip.Visibility = _stop.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        _pause.Content = focus.Paused ? L.T("ادامه", "Resume") : L.T("مکث", "Pause");

        var sessions = Hub.State.FocusLog.Where(s => s.Start.Date == now.Date).ToList();
        var minutes = sessions.Sum(s => s.Minutes);
        _today.Text = sessions.Count == 0
            ? L.T("امروز هنوز جلسه‌ی تمرکزی نداشتی.", "No focus session yet today.")
            : L.T($"امروز {Fa.Digits(sessions.Count(s => s.Completed).ToString())} جلسه‌ی کامل و {Fa.Duration(TimeSpan.FromMinutes(minutes))} تمرکز داشتی.",
                $"Today: {sessions.Count(s => s.Completed)} complete session(s), {Fa.Duration(TimeSpan.FromMinutes(minutes))} of focus.");
        _recent.Children.Clear();
        foreach (var s in sessions.AsEnumerable().Reverse().Take(6))
        {
            _recent.Children.Add(new TextBlock
            {
                Style = St("Label"), Margin = new Thickness(0, 2, 0, 0),
                Text = $"{Fa.Clock(s.Start)} · {Fa.Duration(TimeSpan.FromMinutes(s.Minutes))}" + (s.Completed ? "" : L.T(" · ناتمام", " · stopped early")) + (s.Task.Length > 0 ? " · " + s.Task : ""),
            });
        }
    }
}

/// <summary>This week (or the one before) in numbers: tasks, focus time, and how the PC was used.</summary>
public sealed class WeeklyReportPage : PageBase
{
    private readonly StackPanel _body = new();
    private bool _previous;

    public override string PageTitle => L.T("کارها و تمرکز", "Tasks & focus");
    public override string PageSubtitle => L.T("خلاصه‌ی هفته‌ات: کارهای انجام‌شده، تمرکز و استفاده از سیستم. فقط روی همین کامپیوتر ساخته می‌شه.",
        "Your week: finished tasks, focus time and how the PC was used. Built only on this PC.");

    public WeeklyReportPage()
    {
        Content = _body;
        Hub.TasksChanged += () =>
        {
            if (IsVisible) Dispatcher.BeginInvoke(Refresh);
        };
    }

    private static FrameworkElement Tile(string value, string label)
    {
        var tile = new Border
        {
            Background = Res("Panel2"), BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 10, 10), MinWidth = 120,
        };
        var s = new StackPanel();
        s.Children.Add(new TextBlock { Text = value, FontSize = 24, FontWeight = FontWeights.Bold, FontFamily = (FontFamily)Application.Current.Resources["Num"], Foreground = Res("Text") });
        s.Children.Add(new TextBlock { Text = label, Style = St("Label"), Margin = new Thickness(0, 2, 0, 0) });
        tile.Child = s;
        return tile;
    }

    private static FrameworkElement Bars(string title, double[] values, Func<double, string> format, DateOnly start)
    {
        var box = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        box.Children.Add(new TextBlock { Text = title, Style = St("Label") });
        var max = Math.Max(1, values.Max());
        var grid = new Grid { Margin = new Thickness(0, 8, 0, 0), FlowDirection = FlowDirection.LeftToRight };
        for (var i = 0; i < 7; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var day = start.AddDays(i);
            var col = new StackPanel { Margin = new Thickness(4, 0, 4, 0) };
            col.Children.Add(new TextBlock { Text = values[i] > 0 ? format(values[i]) : "", Style = St("Label"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center });
            var area = new Grid { Height = 70 };
            area.Children.Add(new Border
            {
                Background = Res("Accent"), CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Bottom,
                Height = values[i] <= 0 ? 3 : Math.Max(5, 70 * values[i] / max), Opacity = values[i] <= 0 ? 0.25 : 1,
            });
            col.Children.Add(area);
            col.Children.Add(new TextBlock { Text = L.En ? day.DayOfWeek.ToString()[..3] : Fa.DayLetter(day.DayOfWeek), Style = St("Label"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
            Grid.SetColumn(col, i);
            grid.Children.Add(col);
        }
        box.Children.Add(grid);
        return box;
    }

    public override void Refresh()
    {
        _body.Children.Clear();
        var now = DateTime.Now;
        var thisWeek = WeekMath.StartOf(now, persianWeek: !L.En);
        var start = _previous ? thisWeek.AddDays(-7) : thisWeek;
        var report = WeekReport.Build(Hub.Tasks.All, Hub.State.FocusLog, start, now);

        var (card, body) = Ui2.Card(L.T("هفته‌ی ", "Week of ") + Fa.Date(start.ToDateTime(TimeOnly.MinValue)));
        var chips = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var group = "week" + GetHashCode();
        foreach (var (prev, text) in new[] { (false, L.T("این هفته", "This week")), (true, L.T("هفته‌ی قبل", "Last week")) })
        {
            var chip = new RadioButton { Style = St("Chip"), GroupName = group, Content = text, Margin = new Thickness(0, 0, 8, 6), IsChecked = _previous == prev };
            var captured = prev;
            chip.Checked += (_, _) =>
            {
                _previous = captured;
                Refresh();
            };
            chips.Children.Add(chip);
        }
        body.Children.Add(chips);

        var tiles = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        tiles.Children.Add(Tile(Fa.Digits(report.Completed.ToString()), L.T("کار انجام‌شده", "tasks finished")));
        tiles.Children.Add(Tile(Fa.Digits(report.CompletedOnTime.ToString()), L.T("سر موعد یا بی‌موعد", "on time")));
        tiles.Children.Add(Tile(Fa.Duration(TimeSpan.FromMinutes(report.FocusMinutes)).Replace(" و ", " "), L.T("تمرکز", "focus")));
        tiles.Children.Add(Tile(Fa.Digits(report.FocusSessions.ToString()), L.T("جلسه‌ی کامل", "focus sessions")));
        tiles.Children.Add(Tile(Fa.Digits(report.Streak.ToString()), L.T("روز پشت‌سرهم", "day streak")));
        tiles.Children.Add(Tile(Fa.Digits(report.OverdueNow.ToString()), L.T("کار گذشته‌ی باز", "overdue now")));
        body.Children.Add(tiles);
        body.Children.Add(Bars(L.T("کار انجام‌شده در روز", "Tasks finished per day"), report.CompletedPerDay.Select(x => (double)x).ToArray(), v => Fa.Digits(((int)v).ToString()), start));
        body.Children.Add(Bars(L.T("دقیقه‌ی تمرکز در روز", "Focus minutes per day"), report.FocusPerDay, v => Fa.Digits(((int)Math.Round(v)).ToString()), start));
        if (report.BestDay is { } best)
            body.Children.Add(new TextBlock
            {
                Style = St("Body"), Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap,
                Text = L.T($"پرکارترین روزت {Fa.DayName(best.DayOfWeek)} بود.", $"Your busiest day was {best.DayOfWeek}."),
            });
        _body.Children.Add(card);

        // how the PC was used that week (existing data, nothing new is collected)
        var (pcCard, pc) = Ui2.Card(L.T("استفاده از سیستم در این هفته", "PC use this week"));
        var from = start.ToDateTime(TimeOnly.MinValue);
        var to = from.AddDays(7);
        var games = Hub.State.GameHistory.Where(g => g.Start >= from && g.Start < to).ToList();
        var energy = Hub.State.Energy.Where(e => e.Date >= start && e.Date < start.AddDays(7)).ToList();
        var (down, up) = Hub.Usage.Total(start, start.AddDays(6));
        var lines = new List<string>
        {
            games.Count == 0
                ? L.T("بازی‌ای ثبت نشده.", "No games recorded.")
                : L.T($"بازی: {Fa.Digits(games.Count.ToString())} نوبت، جمعاً {Fa.Duration(TimeSpan.FromMinutes(games.Sum(g => g.Minutes)))}",
                    $"Games: {games.Count} session(s), {Fa.Duration(TimeSpan.FromMinutes(games.Sum(g => g.Minutes)))} in total"),
            L.T($"اینترنت مصرف‌شده: ↓{Fa.Bytes(down)} ↑{Fa.Bytes(up)}", $"Internet used: ↓{Fa.Bytes(down)} ↑{Fa.Bytes(up)}"),
            energy.Count == 0
                ? L.T("مصرف برق هنوز اندازه‌گیری نشده.", "No power use measured yet.")
                : L.T($"مصرف برق: حدود {Fa.N(energy.Sum(e => e.TotalWh) / 1000, 1)} کیلووات‌ساعت", $"Power: about {Fa.N(energy.Sum(e => e.TotalWh) / 1000, 1)} kWh"),
        };
        foreach (var line in lines) pc.Children.Add(new TextBlock { Text = line, Style = St("Body"), Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap });
        _body.Children.Add(pcCard);
    }
}
