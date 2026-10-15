using System.IO;
using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;

namespace Shabpa.Pages;

/// <summary>«پروفایل‌ها»: one click runs several existing actions (screen, volume, focus, reminders…). Triggers run a
/// profile by themselves; anything that can switch the PC off is always started by hand.</summary>
public sealed class ProfilesPage : TabsPage
{
    public static readonly string[] TabKeys = ["profiles", "automation", "history"];

    public override string PageTitle => L.T("پروفایل‌ها", "Profiles");

    public ProfilesPage()
    {
        Add("profiles", L.T("پروفایل‌ها", "Profiles"), new ProfileListPage());
        Add("automation", L.T("خودکارسازی", "Automation"), new AutomationPage());
        Add("history", L.T("تاریخچه‌ی اجرا", "Run history"), new RunHistoryPage());
    }
}

public sealed class ProfileListPage : PageBase
{
    private readonly StackPanel _list = new();
    private readonly StackPanel _editor = new();
    private readonly Button _undo = new() { Style = St("Btn"), Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 8, 0) };
    private readonly TextBlock _hint = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private WorkProfile? _draft;
    private Guid? _editingId;

    public override string PageTitle => L.T("پروفایل‌ها", "Profiles");
    public override string PageSubtitle => L.T("با یه کلیک چند کار رو با هم انجام بده: نور صفحه، صدا، تمرکز، یادآور استراحت و بیشتر.",
        "Do several things with one click: screen, volume, focus, break reminders and more.");

    public ProfileListPage()
    {
        var root = new StackPanel();
        var (card, body) = Ui2.Card(L.T("پروفایل‌های تو", "Your profiles"));
        var tools = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var add = new Button { Style = St("BtnPrimary"), Content = L.T("پروفایل جدید", "New profile"), Margin = new Thickness(0, 0, 8, 0) };
        add.Click += (_, _) => Edit(new WorkProfile { Name = "", Actions = [] }, null);
        tools.Children.Add(add);
        _undo.Click += (_, _) =>
        {
            if (Hub.UndoProfile()) Refresh();
        };
        _undo.Margin = new Thickness(0, 0, 8, 0);
        tools.Children.Add(_undo);
        var export = new Button { Style = St("Btn"), Content = L.T("خروجی گرفتن…", "Export…"), Margin = new Thickness(0, 0, 8, 0) };
        export.Click += (_, _) => Export();
        var import = new Button { Style = St("Btn"), Content = L.T("وارد کردن…", "Import…") };
        import.Click += (_, _) => Import();
        tools.Children.Add(export);
        tools.Children.Add(import);
        body.Children.Add(tools);
        body.Children.Add(_hint);
        _editor.Margin = new Thickness(0, 4, 0, 0);
        body.Children.Add(_editor);
        _list.Margin = new Thickness(0, 6, 0, 0);
        body.Children.Add(_list);
        root.Children.Add(card);
        Content = root;
        Hub.ProfilesChanged += () => Dispatcher.BeginInvoke(() =>
        {
            if (_draft is null) RefreshList();
        });
    }

    public override void Refresh()
    {
        Hub.EnsureProfiles();
        RefreshList();
    }

    private void RefreshList()
    {
        _undo.Visibility = Hub.CanUndoProfile ? Visibility.Visible : Visibility.Collapsed;
        _undo.Content = L.T("برگردوندن به حالت قبل از پروفایل", "Undo the last profile");
        _list.Children.Clear();
        if (Hub.State.Profiles.Count == 0)
            _list.Children.Add(new TextBlock { Text = L.T("پروفایلی نداری. یکی بساز.", "No profiles. Create one."), Style = St("Body"), Margin = new Thickness(0, 10, 0, 0) });
        foreach (var profile in Hub.State.Profiles.ToList()) _list.Children.Add(Card(profile));
    }

    private FrameworkElement Card(WorkProfile profile)
    {
        var box = new Border
        {
            Background = Res("Panel2"), BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 10, 0, 0),
        };
        var s = new StackPanel();
        s.Children.Add(new TextBlock { Text = profile.Name, Style = St("H2"), FontSize = 15 });
        if (profile.Actions.Count == 0) s.Children.Add(new TextBlock { Text = L.T("هنوز کاری نداره.", "No actions yet."), Style = St("Label"), Margin = new Thickness(0, 4, 0, 0) });
        foreach (var a in profile.Actions)
            s.Children.Add(new TextBlock { Text = "• " + Core.Profiles.Describe(a) + (ActionKinds.IsPower(a.Kind) ? "  ⚠" : ""), Style = St("Body"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        Button B(string text, Action click, string style = "Btn")
        {
            var b = new Button { Style = St(style), Content = text, Margin = new Thickness(0, 0, 8, 6) };
            b.Click += (_, _) => click();
            return b;
        }
        buttons.Children.Add(B(L.T("اجرا", "Run"), () => RunNow(profile), "BtnPrimary"));
        buttons.Children.Add(B(L.T("ویرایش", "Edit"), () => Edit(Clone(profile), profile.Id)));
        buttons.Children.Add(B(L.T("کپی", "Duplicate"), () =>
        {
            var copy = Clone(profile);
            copy.Id = Guid.NewGuid();
            copy.Name = Core.Profiles.UniqueName(profile.Name, Hub.State.Profiles.Select(p => p.Name));
            Hub.State.Profiles.Add(copy);
            Hub.Store.Save();
            RefreshList();
        }));
        buttons.Children.Add(B(L.T("حذف", "Delete"), () =>
        {
            if (!ConfirmWindow.Ask(L.T("حذف پروفایل", "Delete profile"), L.T($"«{profile.Name}» و خودکارسازی‌های وابسته‌اش حذف می‌شن.", $"“{profile.Name}” and the automations that use it are deleted."), L.T("حذف کن", "Delete"), danger: true)) return;
            Hub.State.Profiles.Remove(profile);
            Hub.State.Triggers.RemoveAll(t => t.ProfileId == profile.Id);
            Hub.Store.Save();
            RefreshList();
        }, "BtnDanger"));
        s.Children.Add(buttons);
        box.Child = s;
        return box;
    }

    private static WorkProfile Clone(WorkProfile p) => new()
    {
        Id = p.Id, Name = p.Name,
        Actions = [.. p.Actions.Select(a => new ProfileAction { Kind = a.Kind, Value = a.Value, Value2 = a.Value2, Text = a.Text })],
    };

    private void RunNow(WorkProfile profile)
    {
        var detail = string.Join("\n", profile.Actions.Select(a => "• " + Core.Profiles.Describe(a)));
        if (Core.Profiles.HasPower(profile))
        {
            if (!ConfirmWindow.AskAlways(L.T("اجرای پروفایل", "Run profile"),
                    L.T($"«{profile.Name}» شامل کاری‌ه که ممکنه سیستم رو خاموش یا خواب کنه. اجرا بشه؟", $"“{profile.Name}” includes an action that can switch the PC off or to sleep. Run it?"),
                    L.T("اجرا کن", "Run"), danger: true, detail: detail)) return;
        }
        else if (!ConfirmWindow.Ask(L.T("اجرای پروفایل", "Run profile"), L.T($"«{profile.Name}» اجرا بشه؟", $"Run “{profile.Name}”?"), L.T("اجرا کن", "Run"), detail: detail)) return;
        var outcome = Hub.RunProfile(profile, "manual");
        _hint.Foreground = outcome.Ok ? Res("Muted") : Res("Red");
        _hint.Text = string.Join("  ", outcome.Steps);
        RefreshList();
    }

    // ---- editor ----------------------------------------------------------------------------------------

    private void Edit(WorkProfile draft, Guid? editing)
    {
        _draft = draft;
        _editingId = editing;
        BuildEditor();
    }

    private void CloseEditor()
    {
        _draft = null;
        _editingId = null;
        _editor.Children.Clear();
        RefreshList();
    }

    private void BuildEditor()
    {
        _editor.Children.Clear();
        if (_draft is not { } draft) return;
        var box = new Border
        {
            Background = Res("Panel"), BorderBrush = Res("Accent"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 10, 0, 0),
        };
        var s = new StackPanel();
        s.Children.Add(new TextBlock { Text = _editingId is null ? L.T("پروفایل جدید", "New profile") : L.T("ویرایش پروفایل", "Edit profile"), Style = St("H2") });
        s.Children.Add(Ui2.Label(L.T("اسم", "Name"), 8, 4));
        var name = new TextBox { Style = St("Input"), Text = draft.Name, MaxLength = 40 };
        name.TextChanged += (_, _) => draft.Name = name.Text;
        s.Children.Add(name);
        s.Children.Add(Ui2.Label(L.T("کارها (به ترتیب اجرا می‌شن)", "Actions (run in this order)"), 12, 4));
        for (var i = 0; i < draft.Actions.Count; i++) s.Children.Add(ActionRow(draft, draft.Actions[i]));

        s.Children.Add(Ui2.Label(L.T("افزودن کار", "Add an action"), 12, 4));
        var chips = new WrapPanel();
        void Add(string kind, string text, ProfileAction template)
        {
            var b = new Button { Style = St("Btn"), Content = "+ " + text, Margin = new Thickness(0, 0, 8, 6) };
            b.Click += (_, _) =>
            {
                if (draft.Actions.Count >= Core.Profiles.MaxActions) return;
                draft.Actions.Add(template);
                BuildEditor();
            };
            chips.Children.Add(b);
        }
        Add(ActionKinds.Dim, L.T("کم‌نور کردن صفحه", "Dim screen"), new ProfileAction { Kind = ActionKinds.Dim, Value = 50, Value2 = 1 });
        Add(ActionKinds.Volume, L.T("صدا", "Volume"), new ProfileAction { Kind = ActionKinds.Volume, Value = 30 });
        Add(ActionKinds.Focus, L.T("تمرکز", "Focus timer"), new ProfileAction { Kind = ActionKinds.Focus, Value = 25 });
        Add(ActionKinds.Breaks, L.T("یادآور استراحت", "Break reminders"), new ProfileAction { Kind = ActionKinds.Breaks, Value = 1 });
        Add(ActionKinds.RamClean, L.T("خالی کردن رم", "Free RAM"), new ProfileAction { Kind = ActionKinds.RamClean });
        Add(ActionKinds.Notify, L.T("پیام", "Message"), new ProfileAction { Kind = ActionKinds.Notify, Text = L.T("وقتشه!", "It's time!") });
        Add(ActionKinds.SleepMode, L.T("حالت شب‌خواب ⚠", "Sleep mode ⚠"), new ProfileAction { Kind = ActionKinds.SleepMode, Value = 30 });
        Add(ActionKinds.PowerLater, L.T("خاموشی بعد از مدتی ⚠", "Power action later ⚠"), new ProfileAction { Kind = ActionKinds.PowerLater, Value = 60, Value2 = (int)PowerAction.Sleep });
        s.Children.Add(chips);

        var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var save = new Button { Style = St("BtnPrimary"), Content = L.T("ذخیره", "Save"), Margin = new Thickness(0, 0, 8, 0) };
        save.Click += (_, _) =>
        {
            if (Core.Profiles.Clean(draft) is not { } clean)
            {
                _hint.Foreground = Res("Red");
                _hint.Text = L.T("اسم پروفایل رو بنویس.", "Give the profile a name.");
                return;
            }
            clean.Id = _editingId ?? clean.Id;
            if (Hub.State.Profiles.Any(p => p.Id != clean.Id && p.Name.Equals(clean.Name, StringComparison.CurrentCultureIgnoreCase)))
                clean.Name = Core.Profiles.UniqueName(clean.Name, Hub.State.Profiles.Where(p => p.Id != clean.Id).Select(p => p.Name));
            var index = Hub.State.Profiles.FindIndex(p => p.Id == clean.Id);
            if (index >= 0) Hub.State.Profiles[index] = clean;
            else Hub.State.Profiles.Add(clean);
            Hub.Store.Save();
            _hint.Text = "";
            CloseEditor();
        };
        var cancel = new Button { Style = St("Btn"), Content = L.T("انصراف", "Cancel") };
        cancel.Click += (_, _) => CloseEditor();
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        s.Children.Add(buttons);
        box.Child = s;
        _editor.Children.Add(box);
    }

    private FrameworkElement ActionRow(WorkProfile draft, ProfileAction a)
    {
        var row = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel();
        var title = new TextBlock { Style = St("Body"), FontWeight = FontWeights.SemiBold };
        void Retitle() => title.Text = Core.Profiles.Describe(a);
        Retitle();
        left.Children.Add(title);

        void SliderFor(double min, double max, Action<int> set)
        {
            var slider = new Slider { Style = St("Slide"), Minimum = min, Maximum = max, SmallChange = 1, LargeChange = 5, Value = Math.Clamp(a.Value, min, max), IsSnapToTickEnabled = true, TickFrequency = 1, Margin = new Thickness(0, 4, 0, 0) };
            slider.ValueChanged += (_, _) =>
            {
                set((int)slider.Value);
                Retitle();
            };
            left.Children.Add(slider);
        }
        switch (a.Kind)
        {
            case ActionKinds.Dim:
            {
                var on = new CheckBox { Style = St("Check"), Content = L.T("روشن بشه (وگرنه به حالت عادی برمی‌گرده)", "Switch on (otherwise back to normal)"), IsChecked = a.Value2 != 0, Margin = new Thickness(0, 4, 0, 0) };
                on.Click += (_, _) =>
                {
                    a.Value2 = on.IsChecked == true ? 1 : 0;
                    Retitle();
                };
                left.Children.Add(on);
                SliderFor(5, 92, v => a.Value = v);
                break;
            }
            case ActionKinds.Volume: SliderFor(0, 100, v => a.Value = v); break;
            case ActionKinds.Focus: SliderFor(5, 90, v => a.Value = v); break;
            case ActionKinds.SleepMode: SliderFor(5, 180, v => a.Value = v); break;
            case ActionKinds.Breaks:
            {
                var on = new CheckBox { Style = St("Check"), Content = L.T("روشن باشه", "On"), IsChecked = a.Value != 0, Margin = new Thickness(0, 4, 0, 0) };
                on.Click += (_, _) =>
                {
                    a.Value = on.IsChecked == true ? 1 : 0;
                    Retitle();
                };
                left.Children.Add(on);
                break;
            }
            case ActionKinds.Notify:
            {
                var text = new TextBox { Style = St("Input"), Text = a.Text, MaxLength = 120, Margin = new Thickness(0, 4, 0, 0) };
                text.TextChanged += (_, _) =>
                {
                    a.Text = text.Text;
                    Retitle();
                };
                left.Children.Add(text);
                break;
            }
            case ActionKinds.PowerLater:
            {
                var chips = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
                var group = "pw" + a.GetHashCode();
                foreach (var act in new[] { PowerAction.Sleep, PowerAction.Hibernate, PowerAction.Shutdown, PowerAction.Restart, PowerAction.Lock })
                {
                    var chip = new RadioButton { Style = St("Chip"), GroupName = group, Content = Fa.ActionName(act), Margin = new Thickness(0, 0, 8, 6), IsChecked = a.Value2 == (int)act };
                    var captured = act;
                    chip.Checked += (_, _) =>
                    {
                        a.Value2 = (int)captured;
                        Retitle();
                    };
                    chips.Children.Add(chip);
                }
                left.Children.Add(chips);
                SliderFor(1, 240, v => a.Value = v);
                break;
            }
        }
        row.Children.Add(left);
        var remove = new Button { Style = St("Btn"), Content = new TextBlock { Text = "", Style = St("Icon") }, Padding = new Thickness(9, 7, 9, 7), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10, 0, 0, 0), ToolTip = L.T("حذف این کار", "Remove this action") };
        remove.Click += (_, _) =>
        {
            draft.Actions.Remove(a);
            BuildEditor();
        };
        Grid.SetColumn(remove, 1);
        row.Children.Add(remove);
        return row;
    }

    // ---- export / import -------------------------------------------------------------------------------

    private void Export()
    {
        using var dialog = new System.Windows.Forms.SaveFileDialog
        {
            FileName = "shabpa-profiles.json", Filter = "Shabpa profiles|*.json", InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        try
        {
            File.WriteAllText(dialog.FileName, Core.Profiles.Export(Hub.State.Profiles));
            _hint.Foreground = Res("Muted");
            _hint.Text = L.T("ذخیره شد.", "Saved.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _hint.Foreground = Res("Red");
            _hint.Text = L.T("ذخیره نشد.", "Could not save.");
        }
    }

    private void Import()
    {
        using var dialog = new System.Windows.Forms.OpenFileDialog { Filter = "Shabpa profiles|*.json", CheckFileExists = true };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        string text;
        try
        {
            var info = new FileInfo(dialog.FileName);
            if (info.Length > Core.Profiles.MaxPackBytes)
            {
                _hint.Foreground = Res("Red");
                _hint.Text = L.T("فایل خیلی بزرگه و پروفایل نیست.", "The file is too large to be a profile file.");
                return;
            }
            text = File.ReadAllText(dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _hint.Foreground = Res("Red");
            _hint.Text = L.T("فایل خونده نشد.", "The file could not be read.");
            return;
        }
        var imported = Core.Profiles.Import(text);
        if (imported is not { } result || result.profiles.Count == 0)
        {
            _hint.Foreground = Res("Red");
            _hint.Text = L.T("این فایل پروفایل شب‌پا نیست.", "This is not a Shabpa profile file.");
            return;
        }
        var power = result.profiles.Count(Core.Profiles.HasPower);
        var detail = string.Join("\n", result.profiles.Take(8).Select(p => $"• {p.Name} ({Fa.Digits(p.Actions.Count.ToString())})"));
        if (!ConfirmWindow.AskAlways(L.T("وارد کردن پروفایل", "Import profiles"),
                L.T($"{Fa.Digits(result.profiles.Count.ToString())} پروفایل وارد می‌شه." + (power > 0 ? $" {Fa.Digits(power.ToString())} تاش شامل خاموشی یا خواب‌ه (فقط دستی اجرا می‌شه)." : "") + (result.dropped > 0 ? $" {Fa.Digits(result.dropped.ToString())} مورد ناشناخته نادیده گرفته شد." : ""),
                    $"{result.profiles.Count} profile(s) will be imported." + (power > 0 ? $" {power} include a power action (they only run by hand)." : "") + (result.dropped > 0 ? $" {result.dropped} unknown item(s) were ignored." : "")),
                L.T("وارد کن", "Import"), detail: detail)) return;
        foreach (var p in result.profiles.Take(Core.Profiles.MaxProfiles - Hub.State.Profiles.Count))
        {
            p.Name = Core.Profiles.UniqueName(p.Name, Hub.State.Profiles.Select(x => x.Name));
            Hub.State.Profiles.Add(p);
        }
        Hub.Store.Save();
        RefreshList();
    }
}

public sealed class AutomationPage : PageBase
{
    private readonly StackPanel _list = new();
    private readonly WrapPanel _kindChips = new();
    private readonly WrapPanel _profileChips = new();
    private readonly WrapPanel _dayChips = new();
    private readonly StackPanel _timeRow = new() { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _hour = new() { Style = St("Input"), Width = 56, Padding = new Thickness(8, 6, 8, 6), FlowDirection = FlowDirection.LeftToRight, TextAlignment = TextAlignment.Center, Text = "22" };
    private readonly TextBox _minute = new() { Style = St("Input"), Width = 56, Padding = new Thickness(8, 6, 8, 6), FlowDirection = FlowDirection.LeftToRight, TextAlignment = TextAlignment.Center, Text = "00" };
    private readonly TextBlock _hint = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button _save = new() { Style = St("BtnPrimary"), Content = L.T("افزودن", "Add"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
    private TriggerKind _kind = TriggerKind.Time;
    private Guid? _profile;
    private readonly HashSet<DayOfWeek> _days = [];

    public override string PageTitle => L.T("پروفایل‌ها", "Profiles");
    public override string PageSubtitle => L.T("بگو یه پروفایل کی خودش اجرا بشه: ساعت مشخص، شروع بازی، پایان بازی یا بالا اومدن شب‌پا.",
        "Choose when a profile runs by itself: a time of day, a game starting or ending, or Shabpa starting.");

    public AutomationPage()
    {
        var root = new StackPanel();
        var (listCard, list) = Ui2.Card(L.T("خودکارسازی‌ها", "Automations"));
        list.Children.Add(_list);
        root.Children.Add(listCard);

        var (card, body) = Ui2.Card(L.T("خودکارسازی جدید", "New automation"),
            L.T("پروفایلی که خاموشی یا خواب داره هیچ‌وقت خودکار اجرا نمی‌شه.", "A profile that can switch the PC off never runs by itself."));
        body.Children.Add(Ui2.Label(L.T("وقتی…", "When…"), 10, 4));
        var kgroup = "trig" + GetHashCode();
        foreach (var (kind, text) in new[]
        {
            (TriggerKind.Time, L.T("ساعت مشخصی شد", "It is a time of day")), (TriggerKind.GameStart, L.T("بازی شروع شد", "A game starts")),
            (TriggerKind.GameEnd, L.T("بازی تموم شد", "A game ends")), (TriggerKind.AppStart, L.T("شب‌پا بالا اومد", "Shabpa starts")),
        })
        {
            var chip = new RadioButton { Style = St("Chip"), GroupName = kgroup, Content = text, Margin = new Thickness(0, 0, 8, 6), IsChecked = kind == _kind };
            var captured = kind;
            chip.Checked += (_, _) =>
            {
                _kind = captured;
                var time = captured == TriggerKind.Time;
                _timeRow.Visibility = _dayChips.Visibility = time ? Visibility.Visible : Visibility.Collapsed;
            };
            _kindChips.Children.Add(chip);
        }
        body.Children.Add(_kindChips);
        _timeRow.Children.Add(_hour);
        _timeRow.Children.Add(new TextBlock { Text = ":", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Res("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) });
        _timeRow.Children.Add(_minute);
        TimeBoxes.Wire(_hour, _minute);
        body.Children.Add(_timeRow);
        foreach (var d in new[] { DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
        {
            var chip = new CheckBox { Style = St("Check"), Content = Fa.DayName(d), Margin = new Thickness(0, 8, 8, 0) };
            var captured = d;
            chip.Click += (_, _) =>
            {
                if (chip.IsChecked == true) _days.Add(captured);
                else _days.Remove(captured);
            };
            _dayChips.Children.Add(chip);
        }
        body.Children.Add(_dayChips);
        body.Children.Add(Ui2.Label(L.T("روزها رو خالی بذاری هر روز اجرا می‌شه.", "Leave the days empty to run every day."), 4, 0));
        body.Children.Add(Ui2.Label(L.T("این پروفایل اجرا بشه", "Run this profile"), 12, 4));
        body.Children.Add(_profileChips);
        _save.Click += (_, _) => Add();
        body.Children.Add(_save);
        body.Children.Add(_hint);
        root.Children.Add(card);
        Content = root;
    }

    public override void Refresh()
    {
        Hub.EnsureProfiles();
        _profileChips.Children.Clear();
        var group = "prof" + GetHashCode();
        foreach (var p in Hub.State.Profiles.Where(p => !Core.Profiles.HasPower(p)))
        {
            var chip = new RadioButton { Style = St("Chip"), GroupName = group, Content = p.Name, Margin = new Thickness(0, 0, 8, 6), IsChecked = _profile == p.Id };
            var id = p.Id;
            chip.Checked += (_, _) => _profile = id;
            _profileChips.Children.Add(chip);
        }
        if (_profileChips.Children.Count == 0)
            _profileChips.Children.Add(new TextBlock { Text = L.T("پروفایل مناسبی نیست (پروفایل‌های دارای خاموشی اینجا نمیان).", "No suitable profile (profiles with a power action are not offered here)."), Style = St("Label") });
        _timeRow.Visibility = _dayChips.Visibility = _kind == TriggerKind.Time ? Visibility.Visible : Visibility.Collapsed;
        RefreshList();
    }

    private static string Describe(AutomationTrigger t) => t.Kind switch
    {
        TriggerKind.Time => L.T($"ساعت {Fa.Clock(t.Hour, t.Minute)} · {Fa.Days(t.Days)}", $"At {t.Hour:00}:{t.Minute:00} · {Fa.Days(t.Days)}"),
        TriggerKind.GameStart => L.T("وقتی بازی شروع می‌شه", "When a game starts"),
        TriggerKind.GameEnd => L.T("وقتی بازی تموم می‌شه", "When a game ends"),
        _ => L.T("وقتی شب‌پا بالا میاد", "When Shabpa starts"),
    };

    private void RefreshList()
    {
        _list.Children.Clear();
        if (Hub.State.Triggers.Count == 0)
            _list.Children.Add(new TextBlock { Text = L.T("هنوز خودکارسازی‌ای نداری.", "No automations yet."), Style = St("Body"), Margin = new Thickness(0, 10, 0, 0) });
        foreach (var t in Hub.State.Triggers.ToList())
        {
            var profile = Hub.State.Profiles.FirstOrDefault(p => p.Id == t.ProfileId);
            var row = new Grid { Margin = new Thickness(0, 8, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = Describe(t), Style = St("Body"), FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock
            {
                Text = "→ " + (profile?.Name ?? L.T("پروفایل حذف شده", "profile deleted")) + (t.LastRun is { } last ? L.T($" · آخرین اجرا {Fa.DayOf(last, DateTime.Now)} {Fa.Clock(last)}", $" · last ran {Fa.DayOf(last, DateTime.Now)} {last:HH:mm}") : ""),
                Style = St("Label"), TextWrapping = TextWrapping.Wrap,
            });
            row.Children.Add(text);
            var toggle = new Button { Style = St("Btn"), Content = t.Enabled ? L.T("مکث", "Pause") : L.T("ادامه", "Resume"), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            toggle.Click += (_, _) =>
            {
                t.Enabled = !t.Enabled;
                Hub.Store.Save();
                RefreshList();
            };
            Grid.SetColumn(toggle, 1);
            row.Children.Add(toggle);
            var delete = new Button { Style = St("BtnDanger"), Content = L.T("حذف", "Delete"), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            delete.Click += (_, _) =>
            {
                Hub.State.Triggers.Remove(t);
                Hub.Store.Save();
                RefreshList();
            };
            Grid.SetColumn(delete, 2);
            row.Children.Add(delete);
            _list.Children.Add(row);
        }
    }

    private void Add()
    {
        if (_profile is not { } id || Hub.State.Profiles.FirstOrDefault(p => p.Id == id) is not { } profile)
        {
            _hint.Foreground = Res("Red");
            _hint.Text = L.T("یه پروفایل انتخاب کن.", "Choose a profile.");
            return;
        }
        if (Core.Profiles.HasPower(profile))
        {
            _hint.Foreground = Res("Red");
            _hint.Text = L.T("این پروفایل خاموشی داره و خودکار اجرا نمی‌شه.", "This profile has a power action and does not run by itself.");
            return;
        }
        var trigger = new AutomationTrigger { Kind = _kind, ProfileId = id, Days = [.. _days] };
        if (_kind == TriggerKind.Time)
        {
            if (!TimeBoxes.TryRead(_hour, _minute, out var h, out var m))
            {
                _hint.Foreground = Res("Red");
                _hint.Text = L.T("ساعت درست نیست.", "The time is not valid.");
                return;
            }
            trigger.Hour = h;
            trigger.Minute = m;
        }
        Hub.State.Triggers.Add(trigger);
        Hub.Store.Save();
        _hint.Foreground = Res("Muted");
        _hint.Text = L.T("اضافه شد.", "Added.");
        RefreshList();
    }
}

public sealed class RunHistoryPage : PageBase
{
    private readonly StackPanel _list = new();

    public override string PageTitle => L.T("پروفایل‌ها", "Profiles");
    public override string PageSubtitle => L.T("پروفایل‌هایی که اجرا شدن و هر مرحله چی شد.", "Profiles that ran, and what each step did.");

    public RunHistoryPage()
    {
        var (card, body) = Ui2.Card(L.T("تاریخچه", "History"));
        var clear = new Button { Style = St("Btn"), Content = L.T("پاک کردن تاریخچه", "Clear history"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        clear.Click += (_, _) =>
        {
            Hub.State.RunLog.Clear();
            Hub.Store.Save();
            Refresh();
        };
        body.Children.Add(clear);
        _list.Margin = new Thickness(0, 6, 0, 0);
        body.Children.Add(_list);
        Content = card;
        Hub.ProfilesChanged += () => Dispatcher.BeginInvoke(() =>
        {
            if (IsVisible) Refresh();
        });
    }

    public override void Refresh()
    {
        _list.Children.Clear();
        if (Hub.State.RunLog.Count == 0)
            _list.Children.Add(new TextBlock { Text = L.T("هنوز چیزی اجرا نشده.", "Nothing has run yet."), Style = St("Body"), Margin = new Thickness(0, 10, 0, 0) });
        foreach (var entry in Hub.State.RunLog.AsEnumerable().Reverse().Take(40))
        {
            var source = entry.Source switch
            {
                "manual" => L.T("دستی", "by hand"), "time" => L.T("ساعت", "time"), "game" => L.T("بازی", "game"), "start" => L.T("شروع برنامه", "app start"), "undo" => L.T("برگردوندن", "undo"), _ => entry.Source,
            };
            var box = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
            box.Children.Add(new TextBlock
            {
                Text = $"{(entry.Ok ? "✓" : "⚠")} {entry.Profile} · {source} · {Fa.DayOf(entry.Time, DateTime.Now)} {Fa.Clock(entry.Time)}",
                Style = St("H2"), FontSize = 13,
            });
            foreach (var step in entry.Steps)
                box.Children.Add(new TextBlock { Text = step, Style = St("Label"), Margin = new Thickness(8, 2, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = step.StartsWith('✗') ? Res("Red") : Res("Muted") });
            _list.Children.Add(box);
        }
    }
}
