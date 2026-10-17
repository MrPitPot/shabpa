using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;
using Shabpa.Kit;

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
    private readonly AutoColumns _list = new() { MinColumnWidth = 440, Margin = new Thickness(-8, 8, -8, 0) };
    private readonly StackPanel _editor = new();
    private readonly Button _undo = new() { Style = St("Btn"), Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 8, 0) };
    private readonly TextBlock _hint = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private WorkProfile? _draft;
    private Guid? _editingId;
    private readonly HashSet<Guid> _seen = [];
    private bool _listShown, _closing;
    private int _newRow = -1;

    public override string PageTitle => L.T("پروفایل‌ها", "Profiles");
    public override string PageSubtitle => L.T("با یه کلیک چند کار رو با هم انجام بده: نور صفحه، صدا، تمرکز، یادآور استراحت و بیشتر.",
        "Do several things with one click: screen, volume, focus, break reminders and more.");

    public ProfileListPage()
    {
        var root = new StackPanel();
        var (card, body) = Ui2.Card(L.T("پروفایل‌های تو", "Your profiles"));
        card.Margin = new Thickness(0);
        var tools = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var add = new Button { Style = St("BtnPrimary"), Content = GroupB.IconText("plus", L.T("پروفایل جدید", "New profile")), Margin = new Thickness(0, 0, 8, 6) };
        add.Click += (_, _) => Edit(new WorkProfile { Name = "", Actions = [] }, null);
        tools.Children.Add(add);
        _undo.Click += (_, _) =>
        {
            if (Hub.UndoProfile()) Refresh();
        };
        _undo.Margin = new Thickness(0, 0, 8, 6);
        tools.Children.Add(_undo);
        var export = new Button { Style = St("Btn"), Content = L.T("خروجی گرفتن…", "Export…"), Margin = new Thickness(0, 0, 8, 6) };
        export.Click += (_, _) => Export();
        var import = new Button { Style = St("Btn"), Content = L.T("وارد کردن…", "Import…"), Margin = new Thickness(0, 0, 0, 6) };
        import.Click += (_, _) => Import();
        tools.Children.Add(export);
        tools.Children.Add(import);
        body.Children.Add(tools);
        body.Children.Add(_hint);
        root.Children.Add(card);
        root.Children.Add(_editor);
        root.Children.Add(_list);
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

    private static readonly string[] Hues = ["HueA", "HueB", "HueC", "HueD", "HueE"];

    private static string KindIcon(string kind) => kind switch
    {
        ActionKinds.Dim => "sun",
        ActionKinds.Volume => "bell",
        ActionKinds.RamClean => "brush",
        ActionKinds.Focus => "clk",
        ActionKinds.Breaks => "alarm",
        ActionKinds.SleepMode => "moon",
        ActionKinds.PowerLater => "pw",
        _ => "bell",
    };

    private void RefreshList()
    {
        _undo.Visibility = Hub.CanUndoProfile ? Visibility.Visible : Visibility.Collapsed;
        _undo.Content = L.T("برگردوندن به حالت قبل از پروفایل", "Undo the last profile");
        _list.Children.Clear();
        if (Hub.State.Profiles.Count == 0)
            _list.Children.Add(new TextBlock { Text = L.T("پروفایلی نداری. یکی بساز.", "No profiles. Create one."), Style = St("Body"), Margin = new Thickness(8, 10, 8, 0) });
        var index = 0;
        var fresh = 0;
        foreach (var profile in Hub.State.Profiles.ToList())
        {
            var card = Card(profile, Hues[index++ % Hues.Length]);
            _list.Children.Add(card);
            // a profile that was just created or copied rises in; the first fill is covered by the page entrance
            if (_listShown && !_seen.Contains(profile.Id)) GroupB.Appear(card, fresh++);
            _seen.Add(profile.Id);
        }
        _listShown = true;
    }

    private FrameworkElement Card(WorkProfile profile, string hue)
    {
        var box = new GlassCard { Padding = new Thickness(22, 20, 22, 14), Margin = new Thickness(8), Lift = true };
        var s = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.Children.Add(new IconBadge("user", hue, 42, 20, 12) { VerticalAlignment = VerticalAlignment.Center });
        var titles = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = profile.Name, Style = St("H2"), FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis });
        titles.Children.Add(new TextBlock
        {
            Text = profile.Actions.Count == 0 ? L.T("هنوز کاری نداره.", "No actions yet.") : L.T($"{Fa.Digits(profile.Actions.Count.ToString())} کار", profile.Actions.Count == 1 ? "1 action" : $"{profile.Actions.Count} actions"),
            Style = St("Label"), Margin = new Thickness(0, 2, 0, 0),
        });
        Grid.SetColumn(titles, 1);
        head.Children.Add(titles);
        s.Children.Add(head);

        var actions = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        foreach (var a in profile.Actions)
        {
            var line = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition());
            line.Children.Add(new Icon(KindIcon(a.Kind), 16, Res(ActionKinds.IsPower(a.Kind) ? "Gold" : "Accent")) { Margin = new Thickness(0, 4, 10, 0), VerticalAlignment = VerticalAlignment.Top });
            var text = new TextBlock { Style = St("Body"), TextWrapping = TextWrapping.Wrap };
            text.Inlines.Add(new System.Windows.Documents.Run(Core.Profiles.Describe(a)));
            if (ActionKinds.IsPower(a.Kind)) text.Inlines.Add(new System.Windows.Documents.Run("  ⚠") { Foreground = Res("Gold") });
            Grid.SetColumn(text, 1);
            line.Children.Add(text);
            actions.Children.Add(line);
        }
        if (profile.Actions.Count > 0) s.Children.Add(actions);

        s.Children.Add(new Border { Height = 1, Background = Res("Line"), Margin = new Thickness(0, 12, 0, 12) });
        var buttons = new WrapPanel();
        Button B(string icon, string text, Action click, string style = "Btn")
        {
            var b = new Button { Style = St(style), Content = GroupB.IconText(icon, text, 15), Margin = new Thickness(0, 0, 8, 6) };
            b.Click += (_, _) => click();
            return b;
        }
        buttons.Children.Add(B("play", L.T("اجرا", "Run"), () => RunNow(profile), "BtnPrimary"));
        buttons.Children.Add(B("edit", L.T("ویرایش", "Edit"), () => Edit(Clone(profile), profile.Id)));
        buttons.Children.Add(B("plus", L.T("کپی", "Duplicate"), () =>
        {
            var copy = Clone(profile);
            copy.Id = Guid.NewGuid();
            copy.Name = Core.Profiles.UniqueName(profile.Name, Hub.State.Profiles.Select(p => p.Name));
            Hub.State.Profiles.Add(copy);
            Hub.Store.Save();
            RefreshList();
        }));
        buttons.Children.Add(B("trash", L.T("حذف", "Delete"), () =>
        {
            if (!ConfirmWindow.Ask(L.T("حذف پروفایل", "Delete profile"), L.T($"«{profile.Name}» و خودکارسازی‌های وابسته‌اش حذف می‌شن.", $"“{profile.Name}” and the automations that use it are deleted."), L.T("حذف کن", "Delete"), danger: true)) return;
            GroupB.Vanish(box, () =>
            {
                Hub.State.Profiles.Remove(profile);
                Hub.State.Triggers.RemoveAll(t => t.ProfileId == profile.Id);
                Hub.Store.Save();
                RefreshList();
            });
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
        var opening = _draft is null;
        _draft = draft;
        _editingId = editing;
        BuildEditor();
        if (opening && _editor.Children.Count > 0 && _editor.Children[0] is FrameworkElement box) GroupB.Appear(box);
    }

    private void CloseEditor()
    {
        if (_closing) return;
        _closing = true;
        void Finish()
        {
            _draft = null;
            _editingId = null;
            _editor.Children.Clear();
            _closing = false;
            RefreshList();
        }
        if (_editor.Children.Count > 0 && _editor.Children[0] is FrameworkElement box) GroupB.Vanish(box, Finish);
        else Finish();
    }

    private void BuildEditor()
    {
        _editor.Children.Clear();
        if (_draft is not { } draft) return;
        var box = new GlassCard { BorderBrush = Res("Accent"), BorderThickness = new Thickness(1.5), Margin = new Thickness(0, 16, 0, 0) };
        var s = new StackPanel();
        s.Children.Add(new TextBlock { Text = _editingId is null ? L.T("پروفایل جدید", "New profile") : L.T("ویرایش پروفایل", "Edit profile"), Style = St("H2") });
        s.Children.Add(Ui2.Label(L.T("اسم", "Name"), 8, 4));
        var name = new TextBox { Style = St("Input"), Text = draft.Name, MaxLength = 40 };
        name.TextChanged += (_, _) => draft.Name = name.Text;
        s.Children.Add(name);
        s.Children.Add(Ui2.Label(L.T("کارها (به ترتیب اجرا می‌شن)", "Actions (run in this order)"), 12, 4));
        for (var i = 0; i < draft.Actions.Count; i++)
        {
            var row = ActionRow(draft, draft.Actions[i]);
            s.Children.Add(row);
            if (i == _newRow) GroupB.Appear(row);
        }
        _newRow = -1;

        s.Children.Add(Ui2.Label(L.T("افزودن کار", "Add an action"), 14, 6));
        var chips = new WrapPanel();
        void Add(string kind, string text, ProfileAction template)
        {
            var b = new Button { Style = St("Btn"), Content = GroupB.IconText("plus", text, 14), Margin = new Thickness(0, 0, 8, 6) };
            b.Click += (_, _) =>
            {
                if (draft.Actions.Count >= Core.Profiles.MaxActions) return;
                _newRow = draft.Actions.Count;
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
        var save = new Button { Style = St("BtnPrimary"), Content = GroupB.IconText("chk", L.T("ذخیره", "Save")), Margin = new Thickness(0, 0, 8, 0) };
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
        // a flat surface inside the editor card
        var surface = new Border
        {
            Background = Res("Panel2"), BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 10, 10, 12), Margin = new Thickness(0, 6, 0, 6),
        };
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new IconBadge(KindIcon(a.Kind), ActionKinds.IsPower(a.Kind) ? "HueC" : "HueA", 34, 17, 10) { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 12, 0) });
        var left = new StackPanel();
        Grid.SetColumn(left, 1);
        var title = new TextBlock { Style = St("Body"), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
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
                var on = new Switch { Content = L.T("روشن بشه (وگرنه به حالت عادی برمی‌گرده)", "Switch on (otherwise back to normal)"), IsChecked = a.Value2 != 0, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
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
                var on = new Switch { Content = L.T("روشن باشه", "On"), IsChecked = a.Value != 0, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
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
                var text = new TextBox { Style = St("Input"), Text = a.Text, MaxLength = 120, Margin = new Thickness(0, 6, 0, 0) };
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
                var chips = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
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
        var remove = IconButton("trash", L.T("حذف این کار", "Remove this action"), () => GroupB.Vanish(surface, () =>
        {
            draft.Actions.Remove(a);
            BuildEditor();
        }));
        remove.VerticalAlignment = VerticalAlignment.Top;
        remove.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(remove, 2);
        row.Children.Add(remove);
        surface.Child = row;
        return surface;
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
    private readonly StackPanel _timeRow;
    private readonly StackPanel _daysBlock = new();
    private readonly TextBox _hour = GroupB.TimeBox("22");
    private readonly TextBox _minute = GroupB.TimeBox("00");
    private readonly TextBlock _hint = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button _save = new() { Style = St("BtnPrimary"), Content = GroupB.IconText("plus", L.T("افزودن", "Add")), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0) };
    private TriggerKind _kind = TriggerKind.Time;
    private AutomationTrigger? _touched, _added;
    private Guid? _profile;
    private readonly Func<List<DayOfWeek>> _days;

    public override string PageTitle => L.T("پروفایل‌ها", "Profiles");
    public override string PageSubtitle => L.T("بگو یه پروفایل کی خودش اجرا بشه: ساعت مشخص، شروع بازی، پایان بازی یا بالا اومدن شب‌پا.",
        "Choose when a profile runs by itself: a time of day, a game starting or ending, or Shabpa starting.");

    public AutomationPage()
    {
        var root = new StackPanel();
        var (listCard, list) = Ui2.Card(L.T("خودکارسازی‌ها", "Automations"));
        listCard.Margin = new Thickness(0);
        list.Children.Add(_list);
        root.Children.Add(listCard);

        var (card, body) = Ui2.Card(L.T("خودکارسازی جدید", "New automation"),
            L.T("پروفایلی که خاموشی یا خواب داره هیچ‌وقت خودکار اجرا نمی‌شه.", "A profile that can switch the PC off never runs by itself."));
        card.Margin = new Thickness(0, 16, 0, 0);
        body.Children.Add(Ui2.Label(L.T("وقتی…", "When…"), 12, 6));
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
                _daysBlock.Visibility = captured == TriggerKind.Time ? Visibility.Visible : Visibility.Collapsed;
            };
            _kindChips.Children.Add(chip);
        }
        body.Children.Add(_kindChips);

        // time of day and weekdays only matter for the time trigger
        _timeRow = GroupB.TimePair(_hour, _minute);
        _timeRow.Margin = new Thickness(0, 6, 0, 0);
        TimeBoxes.Wire(_hour, _minute);
        _daysBlock.Children.Add(Ui2.Label(L.T("ساعت", "Time of day"), 10, 0));
        _daysBlock.Children.Add(_timeRow);
        _daysBlock.Children.Add(Ui2.Label(L.T("روزها", "Days"), 14, 6));
        _days = DayToggles(_dayChips);
        _daysBlock.Children.Add(_dayChips);
        _daysBlock.Children.Add(Ui2.Label(L.T("روزها رو خالی بذاری هر روز اجرا می‌شه.", "Leave the days empty to run every day."), 6, 0));
        body.Children.Add(_daysBlock);
        body.Children.Add(Ui2.Label(L.T("این پروفایل اجرا بشه", "Run this profile"), 16, 6));
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
        _daysBlock.Visibility = _kind == TriggerKind.Time ? Visibility.Visible : Visibility.Collapsed;
        RefreshList();
    }

    private static string Describe(AutomationTrigger t) => t.Kind switch
    {
        TriggerKind.Time => L.T($"ساعت {Fa.Clock(t.Hour, t.Minute)} · {Fa.Days(t.Days)}", $"At {t.Hour:00}:{t.Minute:00} · {Fa.Days(t.Days)}"),
        TriggerKind.GameStart => L.T("وقتی بازی شروع می‌شه", "When a game starts"),
        TriggerKind.GameEnd => L.T("وقتی بازی تموم می‌شه", "When a game ends"),
        _ => L.T("وقتی شب‌پا بالا میاد", "When Shabpa starts"),
    };

    private static string TriggerIcon(TriggerKind kind) => kind switch
    {
        TriggerKind.Time => "clk",
        TriggerKind.GameStart => "play",
        TriggerKind.GameEnd => "chk",
        _ => "bolt",
    };

    private void RefreshList()
    {
        _list.Children.Clear();
        if (Hub.State.Triggers.Count == 0)
            _list.Children.Add(new TextBlock { Text = L.T("هنوز خودکارسازی‌ای نداری.", "No automations yet."), Style = St("Body"), Margin = new Thickness(0, 10, 0, 0) });
        _list.Margin = new Thickness(0, Hub.State.Triggers.Count > 0 ? 8 : 0, 0, 0);
        var first = true;
        foreach (var t in Hub.State.Triggers.ToList())
        {
            var profile = Hub.State.Profiles.FirstOrDefault(p => p.Id == t.ProfileId);
            var rowHolder = new StrongBox<FrameworkElement?>();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var toggle = new Button { Style = St("Btn"), Content = t.Enabled ? L.T("مکث", "Pause") : L.T("ادامه", "Resume"), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            toggle.Click += (_, _) =>
            {
                t.Enabled = !t.Enabled;
                _touched = t;
                Hub.Store.Save();
                RefreshList();
            };
            var delete = new Button { Style = St("BtnDanger"), Content = L.T("حذف", "Delete"), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            delete.Click += (_, _) => GroupB.Vanish(rowHolder.Value!, () =>
            {
                Hub.State.Triggers.Remove(t);
                Hub.Store.Save();
                RefreshList();
            });
            buttons.Children.Add(toggle);
            buttons.Children.Add(delete);
            var sub = "→ " + (profile?.Name ?? L.T("پروفایل حذف شده", "profile deleted")) + (t.LastRun is { } last ? L.T($" · آخرین اجرا {Fa.DayOf(last, DateTime.Now)} {Fa.Clock(last)}", $" · last ran {Fa.DayOf(last, DateTime.Now)} {last:HH:mm}") : "");
            if (!first) _list.Children.Add(GroupB.Line());
            first = false;
            var row = Row(TriggerIcon(t.Kind), t.Enabled ? "HueA" : "HueC", Describe(t), sub, buttons);
            rowHolder.Value = row;
            _list.Children.Add(row);
            if (ReferenceEquals(t, _added)) GroupB.Appear(row);
            if (!t.Enabled)
            {
                // a row that was just paused fades to its dimmed look
                if (ReferenceEquals(t, _touched)) Fx.Anim(row, UIElement.OpacityProperty, 0.65, null, 280);
                else row.Opacity = 0.65;
            }
            else if (ReferenceEquals(t, _touched)) { row.Opacity = 0.65; Fx.Anim(row, UIElement.OpacityProperty, 1, null, 280); }
        }
        _touched = null;
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
        var trigger = new AutomationTrigger { Kind = _kind, ProfileId = id, Days = [.. _days()] };
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
        GroupB.Appear(_hint);
        _added = trigger;
        RefreshList();
        _added = null;
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
        card.Margin = new Thickness(0);
        var clear = new Button { Style = St("Btn"), Content = GroupB.IconText("trash", L.T("پاک کردن تاریخچه", "Clear history"), 15), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
        clear.Click += (_, _) =>
        {
            GroupB.Vanish(_list, () =>
            {
                Hub.State.RunLog.Clear();
                Hub.Store.Save();
                Refresh();
                _list.Opacity = 1;
                _list.BeginAnimation(UIElement.OpacityProperty, null);
                _list.RenderTransform = System.Windows.Media.Transform.Identity;
            });
        };
        body.Children.Add(clear);
        _list.Margin = new Thickness(0, 8, 0, 0);
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
        var first = true;
        foreach (var entry in Hub.State.RunLog.AsEnumerable().Reverse().Take(40))
        {
            var source = entry.Source switch
            {
                "manual" => L.T("دستی", "by hand"), "time" => L.T("ساعت", "time"), "game" => L.T("بازی", "game"), "start" => L.T("شروع برنامه", "app start"), "undo" => L.T("برگردوندن", "undo"), _ => entry.Source,
            };
            var row = new Grid { Margin = new Thickness(0, 8, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new IconBadge(entry.Ok ? "chk" : "bolt", entry.Ok ? "HueB" : "HueC", 34, 17, 10) { VerticalAlignment = VerticalAlignment.Top });
            var box = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
            Grid.SetColumn(box, 1);
            box.Children.Add(new TextBlock
            {
                Text = $"{entry.Profile} · {source} · {Fa.DayOf(entry.Time, DateTime.Now)} {Fa.Clock(entry.Time)}",
                Style = St("H2"), FontSize = 13.5, TextWrapping = TextWrapping.Wrap,
            });
            foreach (var step in entry.Steps)
            {
                // "✓ text" / "✗ text" is stored data: show the marker as a drawn icon (a text tick is bidi-reordered in Persian), never as a glyph
                var failed = step.StartsWith('✗');
                var text = step.StartsWith('✗') || step.StartsWith('✓') ? step[1..].TrimStart() : step;
                var line = new Grid { Margin = new Thickness(0, 3, 0, 0) };
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                line.ColumnDefinitions.Add(new ColumnDefinition());
                if (step != text) line.Children.Add(new Icon(failed ? "x" : "chk", 13, Res(failed ? "Red" : "Green"), 2.2) { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 7, 0) });
                var label = new TextBlock { Text = text, Style = St("Label"), TextWrapping = TextWrapping.Wrap, Foreground = failed ? Res("Red") : Res("Muted") };
                Grid.SetColumn(label, 1);
                line.Children.Add(label);
                box.Children.Add(line);
            }
            row.Children.Add(box);
            if (!first) _list.Children.Add(GroupB.Line());
            first = false;
            _list.Children.Add(row);
        }
    }
}
