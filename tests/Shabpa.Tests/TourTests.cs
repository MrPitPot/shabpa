using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using Shabpa.Tour;

namespace Shabpa.Tests;

/// <summary>The guided tour: who gets it (new install, updated user, someone who already decided), the steps (every target must be a
/// real control of MainWindow, the order, the click steps), the running session and where the bubble goes.</summary>
public class TourTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Shabpa.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private static string Source(string relative) => File.ReadAllText(Path.Combine(RepoRoot(), "src", "Shabpa", relative.Replace('/', Path.DirectorySeparatorChar)));

    // ---- first run / updated user --------------------------------------------------------------------

    [Theory]
    [InlineData("", true, TourStart.Run)]           // brand-new install
    [InlineData("", false, TourStart.Offer)]        // updated from a version without the tour: asked, never forced
    [InlineData(null, false, TourStart.Offer)]
    [InlineData("pending", false, TourStart.Run)]   // a first-run tour that was interrupted starts again
    [InlineData("offered", false, TourStart.None)]  // the card is shown once
    [InlineData("done", true, TourStart.None)]
    [InlineData("skipped", true, TourStart.None)]
    [InlineData("declined", false, TourStart.None)]
    public void Decide_covers_new_updated_and_decided_users(string? status, bool fresh, TourStart expected) =>
        Assert.Equal(expected, TourState.Decide(status, fresh));

    [Fact]
    public void Final_statuses_never_start_again()
    {
        foreach (var s in new[] { TourState.Offered, TourState.Done, TourState.Skipped, TourState.Declined })
        {
            Assert.True(TourState.IsFinal(s));
            Assert.Equal(TourStart.None, TourState.Decide(s, freshInstall: true));
        }
        Assert.False(TourState.IsFinal(""));
        Assert.False(TourState.IsFinal(TourState.Pending));
    }

    [Fact]
    public void An_old_state_file_without_the_flag_loads_as_undecided_and_not_fresh()
    {
        var folder = Path.Combine(Path.GetTempPath(), "shabpa-tour-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var store = new Store(folder);
            store.Load();
            Assert.True(store.FreshInstall);                       // no file yet
            Assert.Equal("", store.State.Settings.TourStatus);
            File.WriteAllText(Path.Combine(folder, "state.json"), "{ \"Settings\": { \"Tariff\": 410 } }"); // as an older version wrote it
            var again = new Store(folder);
            again.Load();
            Assert.False(again.FreshInstall);
            Assert.Equal("", again.State.Settings.TourStatus);
            Assert.Equal(TourStart.Offer, TourState.Decide(again.State.Settings.TourStatus, again.FreshInstall));
            // the flag survives a save and load
            again.State.Settings.TourStatus = TourState.Done;
            again.Save();
            var third = new Store(folder);
            third.Load();
            Assert.Equal(TourState.Done, third.State.Settings.TourStatus);
        }
        finally { try { Directory.Delete(folder, true); } catch { /* temp */ } }
    }

    // ---- the steps ---------------------------------------------------------------------------------

    [Fact]
    public void Steps_have_unique_ids_texts_in_both_languages_and_a_sane_order()
    {
        var steps = TourSteps.All;
        Assert.InRange(steps.Count, 8, 20);
        Assert.Equal(steps.Count, steps.Select(s => s.Id).Distinct().Count());
        foreach (var s in steps)
        {
            Assert.False(string.IsNullOrWhiteSpace(s.TitleFa) || string.IsNullOrWhiteSpace(s.TitleEn), s.Id);
            Assert.False(string.IsNullOrWhiteSpace(s.TextFa) || string.IsNullOrWhiteSpace(s.TextEn), s.Id);
            Assert.True(TourTargets.IsValidShape(s.Target), $"{s.Id}: {s.Target}");
            if (s.NeedsClick) Assert.False(string.IsNullOrWhiteSpace(s.ClickFa) || string.IsNullOrWhiteSpace(s.ClickEn), s.Id);
            Assert.DoesNotContain("  ", s.TextEn);
        }
        Assert.Equal("welcome", steps[0].Id);
        Assert.Equal(TourTargets.None, steps[0].Target);   // the greeting is centred
        Assert.Equal("sidebar", steps[1].Id);              // the menu is shown before anything inside a page
        Assert.Equal("support", steps[^1].Id);
    }

    [Fact]
    public void Every_page_the_brief_names_is_covered_in_order_of_the_menu()
    {
        var pages = TourSteps.PagesUsed.ToList();
        foreach (var key in new[] { "home", "system", "comfort", "gaming", "playtime", "dns", "health", "mic", "weather", "settings", "support" })
            Assert.Contains(key, pages);
        var order = TourSteps.All.Select(s => s.Page).Where(p => p.Length > 0).Distinct().ToList();
        Assert.True(order.IndexOf("system") < order.IndexOf("gaming") && order.IndexOf("gaming") < order.IndexOf("settings") && order.IndexOf("settings") < order.IndexOf("support"));
    }

    [Fact]
    public void Every_target_is_a_real_control_of_the_main_window()
    {
        var xaml = Source("MainWindow.xaml");
        var code = Source("MainWindow.xaml.cs");
        // page key -> menu item name, exactly as the window registers them
        var pageNav = Regex.Matches(code, "\\[\"(?<key>\\w+)\"\\] = \\((?<nav>Nav\\w+),").ToDictionary(m => m.Groups["key"].Value, m => m.Groups["nav"].Value);
        Assert.Contains("system", pageNav.Keys);

        foreach (var s in TourSteps.All)
        {
            if (s.Page.Length > 0) Assert.True(pageNav.ContainsKey(s.Page), $"{s.Id}: no page '{s.Page}'");
            if (s.NeedsClick) Assert.True(pageNav.ContainsKey(s.ClickPage), $"{s.Id}: no page '{s.ClickPage}'");
            if (s.Target.StartsWith(TourTargets.NavPrefix))
            {
                var page = s.Target[TourTargets.NavPrefix.Length..];
                Assert.True(pageNav.TryGetValue(page, out var nav), $"{s.Id}: no menu item for '{page}'");
                Assert.Contains($"x:Name=\"{nav}\"", xaml);
                Assert.Equal(s.ClickPage.Length > 0 ? s.ClickPage : page, page); // a click step names the same page as its target
            }
            if (s.Target == TourTargets.Theme) Assert.Contains("x:Name=\"ThemeButton\"", xaml);
            if (s.Target == TourTargets.Language) Assert.Contains("x:Name=\"LangChip\"", xaml);
            if (s.Target == TourTargets.Sidebar) Assert.Contains("x:Name=\"NavHost\"", xaml);
        }
        // the menu item names the tour relies on are not hidden
        foreach (var nav in TourSteps.All.Where(s => s.Target.StartsWith(TourTargets.NavPrefix)).Select(s => pageNav[s.Target[TourTargets.NavPrefix.Length..]]))
            Assert.DoesNotMatch($"x:Name=\"{nav}\"[^>]*Visibility=\"Collapsed\"", xaml);
    }

    [Fact]
    public void The_replay_button_exists_in_settings_with_the_id_the_last_steps_point_at()
    {
        var settings = Source("Pages/SettingsPage.cs");
        Assert.Contains("TourSteps.ReplayId", settings);
        Assert.Contains("StartTour()", settings);
        Assert.Contains(TourSteps.All, s => s.Target == TourTargets.IdPrefix + TourSteps.ReplayId && s.Page == "settings");
    }

    [Fact]
    public void A_click_step_is_followed_by_a_step_inside_the_page_it_asked_for()
    {
        var steps = TourSteps.All.ToList();
        for (var i = 0; i < steps.Count; i++)
        {
            if (!steps[i].NeedsClick) continue;
            Assert.True(i + 1 < steps.Count);
            Assert.Equal(steps[i].ClickPage, steps[i + 1].Page);
        }
        Assert.Equal(3, steps.Count(s => s.NeedsClick)); // system, game overlay, settings
    }

    [Fact]
    public void Target_shapes_are_checked()
    {
        Assert.True(TourTargets.IsValidShape(""));
        Assert.True(TourTargets.IsValidShape("page:0"));
        Assert.True(TourTargets.IsValidShape("nav:dns"));
        Assert.False(TourTargets.IsValidShape("page:x"));
        Assert.False(TourTargets.IsValidShape("page:-1"));
        Assert.False(TourTargets.IsValidShape("nav:"));
        Assert.False(TourTargets.IsValidShape("banana"));
    }

    // ---- the running session -----------------------------------------------------------------------

    private static TourSession Make(int n = 3) =>
        new(Enumerable.Range(0, n).Select(i => new TourStep("s" + i, "", "home", "t" + i, "T" + i, "x" + i, "X" + i)).ToList());

    [Fact]
    public void Next_and_back_move_one_step_and_the_last_next_finishes_as_done()
    {
        var s = Make();
        var changes = 0;
        TourEnd? end = null;
        s.Changed += () => changes++;
        s.Finished += e => end = e;
        s.Back();                       // nothing before the first
        Assert.Equal(0, s.Index);
        s.Next(); s.Next();
        Assert.True(s.IsLast);
        Assert.Equal(2, changes);
        s.Back();
        Assert.Equal(1, s.Index);
        s.Next();
        s.Next();                       // past the last: done
        Assert.True(s.Ended);
        Assert.Equal(TourEnd.Done, end);
        s.Next(); s.Back(); s.Skip();   // nothing happens after the end
        Assert.Equal(TourEnd.Done, s.Outcome);
        Assert.Equal(2, s.Index);
    }

    [Fact]
    public void Skip_ends_at_any_step_once()
    {
        var s = Make();
        var count = 0;
        s.Finished += _ => count++;
        s.Next();
        s.Skip();
        s.Skip();
        Assert.Equal(1, count);
        Assert.Equal(TourEnd.Skipped, s.Outcome);
    }

    [Fact]
    public void Clicking_the_asked_menu_item_moves_on_but_another_page_does_not()
    {
        var steps = new List<TourStep>
        {
            new("a", "", "home", "a", "a", "a", "a"),
            new("click", "nav:system", "home", "c", "c", "c", "c", ClickPage: "system", ClickFa: "x", ClickEn: "x"),
            new("inside", "page:0", "system", "i", "i", "i", "i"),
        };
        var s = new TourSession(steps);
        Assert.False(s.PageOpened("system"));  // the current step is not waiting for a click
        s.Next();
        Assert.False(s.PageOpened("dns"));
        Assert.Equal("click", s.Current.Id);
        Assert.True(s.PageOpened("system"));
        Assert.Equal("inside", s.Current.Id);
        Assert.False(s.PageOpened("system"));
    }

    [Fact]
    public void The_counter_uses_the_language_and_the_progress_goes_from_zero_to_one()
    {
        var was = L.En;
        try
        {
            var s = Make(8);
            for (var i = 0; i < 2; i++) s.Next();
            L.En = true;
            Assert.Equal("3 of 8", s.Counter);
            L.En = false;
            Assert.Equal("۳ از ۸", s.Counter);
            Assert.Equal(2.0 / 7, s.Progress, 6);
            Assert.Equal(0, Make(8).Progress);
            for (var i = 0; i < 5; i++) s.Next();
            Assert.Equal(1, s.Progress);
        }
        finally { L.En = was; }
    }

    [Fact]
    public void A_tour_needs_steps_and_the_end_is_written_to_the_settings()
    {
        Assert.Throws<ArgumentException>(() => new TourSession([]));
        var settings = new Settings();
        TourSession.Record(settings, TourEnd.Skipped);
        Assert.Equal(TourState.Skipped, settings.TourStatus);
        TourSession.Record(settings, TourEnd.Done);
        Assert.Equal(TourState.Done, settings.TourStatus);
    }

    // ---- where the bubble goes ---------------------------------------------------------------------

    private static readonly Rect Window = new(0, 0, 1280, 800);
    private static readonly Size Bubble = new(380, 240);

    private static bool Inside(Point p) => Window.Contains(new Rect(p, Bubble));
    private static bool Overlaps(Point p, Rect target) => new Rect(p, Bubble).IntersectsWith(target);

    [Fact]
    public void Without_a_target_the_bubble_is_centred()
    {
        var p = TourOverlay.BubbleSpot(null, Bubble, Window);
        Assert.Equal((1280 - 380) / 2.0, p.X);
        Assert.Equal((800 - 240) / 2.0, p.Y);
    }

    [Fact]
    public void A_small_target_gets_the_bubble_under_it_or_above_it_never_on_top()
    {
        var top = new Rect(300, 100, 200, 50);
        var p = TourOverlay.BubbleSpot(top, Bubble, Window);
        Assert.True(Inside(p));
        Assert.False(Overlaps(p, top));
        Assert.True(p.Y >= top.Bottom);
        var bottom = new Rect(300, 700, 200, 50);
        var q = TourOverlay.BubbleSpot(bottom, Bubble, Window);
        Assert.False(Overlaps(q, bottom));
        Assert.True(q.Y + Bubble.Height <= bottom.Top);
    }

    [Fact]
    public void A_menu_item_gets_the_bubble_beside_it_on_the_side_of_the_middle()
    {
        var rightItem = new Rect(1067, 124, 204, 48); // right-to-left: the menu is on the right
        var p = TourOverlay.BubbleSpot(rightItem, Bubble, Window);
        Assert.False(Overlaps(p, rightItem));
        Assert.True(p.X + Bubble.Width <= rightItem.Left);
        var leftItem = new Rect(9, 124, 204, 48);     // left-to-right: on the left
        var q = TourOverlay.BubbleSpot(leftItem, Bubble, Window);
        Assert.False(Overlaps(q, leftItem));
        Assert.True(q.X >= leftItem.Right);
    }

    [Fact]
    public void The_bubble_stays_inside_the_window_for_any_target_and_window_size()
    {
        var rnd = new Random(7);
        for (var i = 0; i < 400; i++)
        {
            var area = new Rect(0, 0, 960 + rnd.Next(1200), 620 + rnd.Next(700));
            var t = new Rect(rnd.Next((int)area.Width - 40), rnd.Next((int)area.Height - 40), 20 + rnd.Next((int)area.Width / 2), 20 + rnd.Next((int)area.Height / 2));
            t.Intersect(area);
            var p = TourOverlay.BubbleSpot(t, Bubble, area);
            Assert.True(area.Contains(new Rect(p, Bubble)), $"{area} {t} -> {p}");
        }
    }

    [Fact]
    public void A_target_that_fills_the_window_still_gets_a_visible_bubble()
    {
        var p = TourOverlay.BubbleSpot(new Rect(4, 4, 1272, 792), Bubble, Window);
        Assert.True(Inside(p));
    }

    // ---- the modal keys ----------------------------------------------------------------------------

    private const System.Windows.Input.ModifierKeys None = System.Windows.Input.ModifierKeys.None, Ctrl = System.Windows.Input.ModifierKeys.Control;

    [Theory]
    [InlineData(System.Windows.Input.Key.Escape, None, TourKeyAction.Skip)]
    [InlineData(System.Windows.Input.Key.Enter, None, TourKeyAction.Next)]
    [InlineData(System.Windows.Input.Key.Tab, None, TourKeyAction.Swallow)]
    [InlineData(System.Windows.Input.Key.Space, None, TourKeyAction.Swallow)]
    [InlineData(System.Windows.Input.Key.D3, Ctrl, TourKeyAction.Swallow)]
    [InlineData(System.Windows.Input.Key.OemComma, Ctrl, TourKeyAction.Swallow)]
    [InlineData(System.Windows.Input.Key.Right, Ctrl, TourKeyAction.Swallow)]
    [InlineData(System.Windows.Input.Key.LeftCtrl, Ctrl, TourKeyAction.Pass)]
    [InlineData(System.Windows.Input.Key.F4, System.Windows.Input.ModifierKeys.Alt, TourKeyAction.Pass)]
    [InlineData(System.Windows.Input.Key.LWin, None, TourKeyAction.Pass)]
    public void Keys_are_swallowed_except_the_tour_keys_and_system_keys(System.Windows.Input.Key key, System.Windows.Input.ModifierKeys mods, TourKeyAction expected) =>
        Assert.Equal(expected, TourKeys.Classify(key, mods, FlowDirection.LeftToRight));

    [Fact]
    public void Ctrl_shift_K_the_hidden_kit_shortcut_is_swallowed_too() =>
        Assert.Equal(TourKeyAction.Swallow, TourKeys.Classify(System.Windows.Input.Key.K, Ctrl | System.Windows.Input.ModifierKeys.Shift, FlowDirection.LeftToRight));

    [Fact]
    public void Arrows_follow_the_reading_direction()
    {
        Assert.Equal(TourKeyAction.Next, TourKeys.Classify(System.Windows.Input.Key.Right, None, FlowDirection.LeftToRight));
        Assert.Equal(TourKeyAction.Back, TourKeys.Classify(System.Windows.Input.Key.Left, None, FlowDirection.LeftToRight));
        Assert.Equal(TourKeyAction.Next, TourKeys.Classify(System.Windows.Input.Key.Left, None, FlowDirection.RightToLeft));
        Assert.Equal(TourKeyAction.Back, TourKeys.Classify(System.Windows.Input.Key.Right, None, FlowDirection.RightToLeft));
    }

    [Fact]
    public void A_step_asks_for_its_page_again_when_another_one_is_open()
    {
        var step = new TourStep("x", "page:0", "dns", "t", "t", "x", "x");
        Assert.True(TourSteps.NeedsPage(step, "home"));
        Assert.False(TourSteps.NeedsPage(step, "dns"));
        Assert.False(TourSteps.NeedsPage(step with { Page = "" }, "home"));
    }

    [Fact]
    public void The_window_hooks_keep_the_tour_safe()
    {
        var code = Source("MainWindow.Tour.cs");
        var main = Source("MainWindow.xaml.cs");
        Assert.Contains("_tourScrolled.Clear()", code);          // a second tour scrolls again
        Assert.Contains("EnsureHome();", code);                  // skipping while a page opens still lands on Home
        // the key hook is registered before the window's own shortcuts (Ctrl+number...), so the tour sees every key first
        Assert.True(main.IndexOf("InitTour();") < main.IndexOf("PreviewKeyDown += OnShortcut"));
        // the card saves "offered" only after the last visibility check, so a window sent to the tray meanwhile saves nothing
        var look = code[code.IndexOf("Task TourFirstLookAsync")..];
        Assert.True(look.LastIndexOf("!IsVisible || WindowState") < look.IndexOf("TourState.Offered"));
    }
}
