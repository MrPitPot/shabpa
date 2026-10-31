using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>«مأموریت‌ها و پَر»: Bidar's level, the three quests of the day, the quest of the week, the one-time ones that teach a
/// feature, and the badges. Everything is counted on this PC from things the app already notices; nothing is sent anywhere and
/// feathers only open looks, never features.</summary>
public sealed class QuestsPage : PageBase
{
    private readonly StackPanel _root = new();
    private readonly StackPanel _top = new(), _today = new(), _week = new(), _once = new(), _badges = new();
    private readonly Switch _on = new();
    private bool _building;

    public override string PageTitle => L.T("مأموریت‌ها و پَر", "Quests & feathers");
    public override string PageSubtitle => L.T("با استفاده از شب‌پا پَر جمع کن و بیدار رو بالا ببر. همه‌چی فقط روی همین سیستمه.",
        "Collect feathers by using Shabpa and level Bidar up. Everything stays on this PC.");

    public QuestsPage()
    {
        var (topCard, top) = PowerUi.Card(null);
        top.Children.Add(_top);
        var (todayCard, today) = PowerUi.Card(L.T("مأموریت‌های امروز", "Today's quests"), L.T("هر روز سه تا؛ فردا عوض می‌شن.", "Three a day; they change tomorrow."));
        today.Children.Add(_today);
        var (weekCard, week) = PowerUi.Card(L.T("مأموریت این هفته", "This week's quest"), L.T("هفته از شنبه شروع می‌شه.", "The week starts on Saturday."));
        week.Children.Add(_week);
        var (onceCard, once) = PowerUi.Card(L.T("یاد بگیر و پَر بگیر", "Learn and earn"), L.T("هر کدوم یه قابلیت شب‌پا رو معرفی می‌کنه و فقط یک بار پَر می‌ده.", "Each introduces a Shabpa feature and pays once."));
        once.Children.Add(_once);
        var (badgeCard, badge) = PowerUi.Card(L.T("نشان‌ها", "Badges"));
        badge.Children.Add(_badges);

        var (setCard, set) = PowerUi.Card(L.T("تنظیمات", "Settings"));
        _on.Checked += (_, _) => OnToggle(false);
        _on.Unchecked += (_, _) => OnToggle(true);
        set.Children.Add(K.SettingRow(L.T("شمارش پَر و مأموریت‌ها روشن باشه", "Count feathers and quests"), _on, first: true,
            hint: L.T("خاموش کنی، چیزی شمرده نمی‌شه و این صفحه خالی می‌مونه. پَرها فقط ظاهر بیدار رو باز می‌کنن و هیچ قابلیتی پشتشون قفل نیست.",
                "Turn it off and nothing is counted. Feathers only unlock looks; no feature is ever locked behind them.")));
        var reset = ListUi.Btn("trash", L.T("صفر کردن پَرها", "Reset feathers"), "Btn", () =>
        {
            if (Ask(L.T("همه‌ی پَرها صفر بشه؟", "Reset all feathers?"), L.T("سطح، زنجیره و نشان‌ها هم پاک می‌شن. برگشتی نداره.", "Level, streak and badges go too. This cannot be undone."),
                    L.T("صفر کن", "Reset"), danger: true))
                Hub.ResetQuests();
        });
        reset.HorizontalAlignment = HorizontalAlignment.Left;
        reset.Margin = new Thickness(0, 10, 0, 0);
        set.Children.Add(reset);

        foreach (var c in new UIElement[] { topCard, todayCard, weekCard, onceCard, badgeCard, setCard }) _root.Children.Add(c);
        Content = _root;
        While<Action>(h => Hub.QuestsChanged += h, h => Hub.QuestsChanged -= h, () => Dispatcher.BeginInvoke(Refresh));
    }

    private void OnToggle(bool off)
    {
        if (_building) return;
        Hub.SetQuestsOff(off);
    }

    public override void Refresh()
    {
        _building = true;
        try
        {
            var q = Hub.Quest;
            _on.IsChecked = !Hub.Settings.QuestsOff;
            var now = DateTime.Now;
            var day = DateOnly.FromDateTime(now);
            Quests.Roll(q, now); // a page left open past midnight shows the new day

            // ---- level -----------------------------------------------------------------------------------
            _top.Children.Clear();
            var level = Quests.Level(q.Feathers);
            var (fa, en) = Quests.LevelNames[level - 1];
            var next = Quests.ToNext(q.Feathers);
            _top.Children.Add(Row("game", "Accent",
                L.T($"سطح {Fa.Digits(level.ToString())} · {fa}", $"Level {level} · {en}"),
                L.T($"{Fa.Digits(q.Feathers.ToString())} {Quests.FeatherFa}" + (next is { } n ? $" · {Fa.Digits(n.ToString())} {Quests.FeatherFa} تا سطح بعد" : " · بالاترین سطح"),
                    $"{q.Feathers} feathers" + (next is { } n2 ? $" · {n2} to the next level" : " · top level"))));
            _top.Children.Add(new ProgressBar { Style = St("Bar"), Maximum = 100, Value = Quests.LevelProgress(q.Feathers) * 100, Margin = new Thickness(0, 4, 0, 10) });
            _top.Children.Add(Txt(q.Streak > 0
                ? L.T($"🔥 {Fa.Digits(q.Streak.ToString())} روز پیاپی (بهترین: {Fa.Digits(q.BestStreak.ToString())}). هر هفت روز پیاپی {Fa.Digits(Quests.StreakBonusFeathers.ToString())} {Quests.FeatherFa} جایزه داره.",
                    $"🔥 {q.Streak} days in a row (best: {q.BestStreak}). Every seven days in a row pays {Quests.StreakBonusFeathers} feathers.")
                : L.T("هر روز که با شب‌پا یه کار انجام بدی، زنجیره‌ات یه روز بلندتر می‌شه.", "Each day you do something with Shabpa adds a day to your streak."), "Label"));

            // ---- quests ----------------------------------------------------------------------------------
            Fill(_today, Quests.Today(day), q);
            Fill(_week, [Quests.Weekly(day)], q);
            Fill(_once, Quests.OnceList, q);

            // ---- badges ----------------------------------------------------------------------------------
            _badges.Children.Clear();
            foreach (var b in Quests.Badges)
            {
                var have = q.Badges.Contains(b.Id);
                var progress = Math.Min(q.Lifetime.GetValueOrDefault(b.Event.ToString()), b.Target);
                if (_badges.Children.Count > 0) _badges.Children.Add(Divider());
                _badges.Children.Add(Row(have ? "chk" : "clk", have ? "Green" : "HueB", L.T(b.Fa, b.En),
                    have ? L.T(b.DescFa, b.DescEn) : L.T($"{b.DescFa} ({Fa.Digits(((int)progress).ToString())} از {Fa.Digits(((int)b.Target).ToString())})", $"{b.DescEn} ({(int)progress} of {(int)b.Target})")));
            }
            if (Hub.Settings.Supporter)
            {
                _badges.Children.Add(Divider());
                _badges.Children.Add(Row("chk", "Gold", L.T("حامی شب‌پا", "Shabpa supporter"), L.T("ممنون که پشتیبانی.", "Thank you for the support.")));
            }

            _root.Opacity = Hub.Settings.QuestsOff ? 0.45 : 1;
        }
        finally { _building = false; }
    }

    private void Fill(StackPanel host, IReadOnlyList<QuestDef> quests, QuestState state)
    {
        host.Children.Clear();
        foreach (var quest in quests)
        {
            if (host.Children.Count > 0) host.Children.Add(Divider());
            var done = state.Done.Contains(quest.Id);
            var progress = Math.Min(Quests.Progress(state, quest), quest.Target);
            var sub = done
                ? L.T($"انجام شد · {Fa.Digits("+" + quest.Feathers)} {Quests.FeatherFa}", $"Done · +{quest.Feathers} feathers")
                : L.T($"{Fa.Digits(((int)progress).ToString())} از {Fa.Digits(((int)quest.Target).ToString())} · {Fa.Digits("+" + quest.Feathers)} {Quests.FeatherFa}",
                    $"{(int)progress} of {(int)quest.Target} · +{quest.Feathers} feathers");
            host.Children.Add(Row(done ? "chk" : "game", done ? "Green" : "HueD", L.T(quest.Fa, quest.En), sub));
            if (!done)
                host.Children.Add(new ProgressBar { Style = St("Bar"), Maximum = 100, Value = 100 * progress / quest.Target, Margin = new Thickness(0, 0, 0, 6) });
        }
    }
}
