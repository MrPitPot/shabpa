using System.IO;
using System.Windows;
using System.Windows.Controls;
using Shabpa.Kit;
using Shabpa.Pages;
using Xunit;

namespace Shabpa.Tests;

/// <summary>"Specified element is already the logical child of another element" killed a rebuild in the real log (the permission list of
/// the security page, only when running as administrator). Every page is built and refreshed over and over, in both elevation modes,
/// without catching anything: a page that throws here would throw in front of the user.</summary>
[Collection("Wpf")]
public class PageRebuildTests
{
    private static readonly (string name, Func<PageBase> make)[] Pages =
    [
        ("home", () => new HomePage()), ("system", () => new SystemPage()), ("comfort", () => new ComfortPage()), ("profiles", () => new ProfilesPage()),
        ("overlay", () => new OverlayPage()), ("playtime", () => new GameLogPage()), ("network", () => new NetworkPage()), ("dns", () => new DnsPage()),
        ("downloads", () => new DownloadsPage()), ("files", () => new FilesPage()), ("health", () => new HealthPage()), ("tasks", () => new TasksPage()),
        ("mic", () => new MicrophonePage()), ("weather", () => new WeatherPage()), ("support", () => new SupportPage()), ("update", () => new UpdatePage()),
        ("settings", () => new SettingsPage()), ("kit", () => new KitPage()),
        // leaves that sit inside the tab pages above, built on their own as well
        ("security", () => new SecurityPage()), ("health-overview", () => new HealthOverviewPage()), ("health-report", () => new HealthReportPage()),
        ("energy", () => new EnergyPage()), ("history", () => new HistoryPage()), ("memory", () => new MemoryPage()), ("power", () => new PowerPage()),
        ("remote", () => new RemotePage()), ("rules", () => new RulesPage()), ("wake", () => new WakePage()), ("usage", () => new UsagePage()),
        ("ping", () => new PingPage()), ("space", () => new SpacePage()), ("duplicates", () => new DuplicatesPage()), ("backup", () => new BackupPage()),
        ("cleanup", () => new CleanupPage()), ("todo", () => new TodoPage()), ("focus", () => new FocusPage()), ("weekly", () => new WeeklyReportPage()),
        ("profile-list", () => new ProfileListPage()), ("automation", () => new AutomationPage()), ("run-history", () => new RunHistoryPage()),
        ("app-updates", () => new AppUpdatesPage()), ("drivers", () => new DriversPage()),
    ];

    private static Window OffScreen(UIElement content) => new()
    {
        Content = content, Width = 900, Height = 700, ShowActivated = false, ShowInTaskbar = false,
        WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000,
    };

    private static void Prepare()
    {
        StaRunner.EnsureApp();
        _ = new Hub(new Store(Path.Combine(Path.GetTempPath(), "shabpa-rebuild-" + Guid.NewGuid().ToString("N"))), dryRun: true, demo: true);
    }

    [Fact]
    public void Every_page_class_is_in_the_list_above()
    {
        StaRunner.Run(() =>
        {
            Prepare();
            var types = Pages.Select(p => p.make().GetType()).ToHashSet();
            var all = typeof(PageBase).Assembly.GetTypes().Where(t => typeof(PageBase).IsAssignableFrom(t) && t != typeof(PageBase) && !t.IsAbstract && t.IsPublic && t.GetConstructor(Type.EmptyTypes) is not null);
            var missing = all.Where(t => !types.Contains(t)).Select(t => t.Name).ToList();
            Assert.True(missing.Count == 0, "pages not covered by the rebuild test: " + string.Join(", ", missing));
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Every_page_survives_being_shown_and_refreshed_again_and_again(bool admin)
    {
        var failures = new List<string>();
        StaRunner.Run(() =>
        {
            Prepare();
            Elevation.TestOverride = admin;
            Motion.TestOverride = false;
            try
            {
                foreach (var (name, make) in Pages)
                {
                    Window? window = null;
                    try
                    {
                        var page = make();
                        window = OffScreen(page);
                        window.Show();
                        for (var i = 0; i < 4; i++)
                        {
                            page.Refresh();
                            page.OnTick(Hub.I.Snapshot);
                            page.OnSecond();
                            window.UpdateLayout();
                            StaRunner.Pump(5);
                        }
                        // the tab pages: every tab is shown, and again
                        if (page is TabsPage tabs)
                            for (var round = 0; round < 2; round++)
                                foreach (var key in tabs.Keys)
                                {
                                    tabs.Select(key);
                                    page.Refresh();
                                    window.UpdateLayout();
                                    StaRunner.Pump(5);
                                }
                    }
                    catch (Exception ex) { failures.Add($"{name} (admin={admin}): {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}"); }
                    finally { try { window?.Close(); } catch { /* gone */ } }
                }
            }
            finally
            {
                Elevation.TestOverride = null;
                Motion.TestOverride = null;
            }
        });
        Assert.True(failures.Count == 0, string.Join("\n---\n", failures));
    }

    [Fact]
    public void Detach_lets_go_of_the_old_parent_so_the_element_can_move()
    {
        StaRunner.Run(() =>
        {
            var text = new TextBlock { Text = "x" };
            var border = new Border { Child = text };
            var panel = new StackPanel();
            Assert.ThrowsAny<InvalidOperationException>(() => panel.Children.Add(text)); // the failure the log showed
            Reparent.AddTo(panel, text);
            Assert.Null(border.Child);
            Assert.Same(panel, text.Parent);
            var grid = new Grid();
            Reparent.AddTo(grid, text);                    // panel to panel
            Assert.Empty(panel.Children);
            Assert.Single(grid.Children);
            var holder = new ContentControl();
            holder.Content = Reparent.Detach(text);        // panel to content control
            Assert.Same(text, holder.Content);
            Assert.Empty(grid.Children);
            Reparent.AddTo(panel, text);                   // content control to panel
            Assert.Null(holder.Content);
        });
    }

    [Fact]
    public void A_page_step_that_throws_is_logged_once_and_swallowed()
    {
        StaRunner.Run(() =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "shabpa-safe-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var saved = Log.File;
            Log.File = Path.Combine(folder, "shabpa.log");
            try
            {
                var page = new Throwing();
                Assert.False(PageBase.SafeRefresh(page));
                Assert.False(PageBase.SafeRefresh(page));   // the same failure again within a minute is not logged again
                Assert.False(PageBase.SafeSecond(page));
                Assert.True(PageBase.SafeTick(page, new SensorSnapshot()));
                var log = File.ReadAllText(Log.File);
                Assert.Contains("Throwing.Refresh failed", log);
                Assert.Contains("Throwing.OnSecond failed", log);
                Assert.Equal(1, log.Split("Throwing.Refresh failed").Length - 1);
            }
            finally { Log.File = saved; try { Directory.Delete(folder, true); } catch { /* temp */ } }
        });
    }

    private sealed class Throwing : PageBase
    {
        public override void Refresh() => throw new InvalidOperationException("Specified element is already the logical child of another element");
        public override void OnSecond() => throw new InvalidOperationException("boom");
    }
}
