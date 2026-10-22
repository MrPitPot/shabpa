using System.Diagnostics;
using System.IO;
using System.Windows;
using Shabpa.Pages;
using Xunit;

namespace Shabpa.Tests;

/// <summary>Slow reads (registry, services, startup folders, a sleeping disk) must not run on the window thread: the page returns at
/// once and shows the result when the worker is done.</summary>
[Collection("Wpf")]
public class UiThreadWorkTests
{
    private static Window OffScreen(UIElement content) => new()
    {
        Content = content, Width = 900, Height = 700, ShowActivated = false, ShowInTaskbar = false,
        WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000,
    };

    private static void Prepare()
    {
        StaRunner.EnsureApp();
        _ = new Hub(new Store(Path.Combine(Path.GetTempPath(), "shabpa-uiwork-" + Guid.NewGuid().ToString("N"))), dryRun: true, demo: true);
    }

    [Fact]
    public void The_security_page_reads_the_registry_and_startup_folders_on_a_worker()
    {
        StaRunner.Run(() =>
        {
            Prepare();
            var uiThread = Environment.CurrentManagedThreadId;
            var readOn = -1;
            var done = new ManualResetEventSlim();
            SecurityPage.Reader = () =>
            {
                readOn = Environment.CurrentManagedThreadId;
                Thread.Sleep(1200);   // a registry under a busy antivirus
                done.Set();
                return ([], []);
            };
            try
            {
                var page = new SecurityPage();
                var window = OffScreen(page);
                window.Show();
                var sw = Stopwatch.StartNew();
                page.Refresh();
                var returned = sw.ElapsedMilliseconds;
                Assert.True(returned < 400, $"Refresh held the window thread for {returned} ms");
                Assert.True(done.Wait(4000));
                Assert.NotEqual(uiThread, readOn);
                StaRunner.Pump(100);
                window.Close();
            }
            finally { SecurityPage.Reader = null; }
        });
    }

    [Fact]
    public void A_failing_read_does_not_leave_the_security_page_stuck_busy()
    {
        StaRunner.Run(() =>
        {
            Prepare();
            var calls = 0;
            SecurityPage.Reader = () => { calls++; if (calls == 1) throw new IOException("registry unavailable"); return ([], []); };
            try
            {
                var page = new SecurityPage();
                var window = OffScreen(page);
                window.Show();
                page.Refresh();
                StaRunner.Pump(300);
                page.Refresh();                 // the first read failed: the second must still run
                StaRunner.Pump(300);
                Assert.Equal(2, calls);
                window.Close();
            }
            finally { SecurityPage.Reader = null; }
        });
    }

    [Fact]
    public void The_space_page_is_built_without_waiting_for_the_drives()
    {
        StaRunner.Run(() =>
        {
            Prepare();
            var done = new ManualResetEventSlim();
            SpacePage.DriveReader = () => { Thread.Sleep(1200); done.Set(); return [("C:\\", "C:\\"), ("D:\\", "D:\\")]; };
            try
            {
                var sw = Stopwatch.StartNew();
                var page = new SpacePage();
                var built = sw.ElapsedMilliseconds;
                Assert.True(built < 600, $"building the page took {built} ms with a slow drive");
                var window = OffScreen(page);
                window.Show();
                var before = page.QuickButtons;
                Assert.True(done.Wait(4000));
                StaRunner.Pump(300);
                Assert.Equal(before + 2, page.QuickButtons);   // the two drive buttons arrived afterwards
                window.Close();
            }
            finally { SpacePage.DriveReader = null; }
        });
    }
}
