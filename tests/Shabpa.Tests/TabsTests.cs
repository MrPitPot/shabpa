using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shabpa.Core;
using Shabpa.Pages;

namespace Shabpa.Tests;

/// <summary>Drives the real tab control inside a real WPF window on an STA thread.</summary>
[Collection("Wpf")]
public class TabsTests
{
    private sealed class Dummy(string title) : PageBase
    {
        public override string PageTitle => title;
        public override string PageSubtitle => "sub-" + title;
        public int Refreshed;
        public override void Refresh() => Refreshed++;
        public Dummy Init()
        {
            Content = new TextBlock { Text = title };
            return this;
        }
    }

    private sealed class Tabs : TabsPage
    {
        public readonly Dummy A = new Dummy("A").Init(), B = new Dummy("B").Init(), C = new Dummy("C").Init();
        public override string PageTitle => "T";

        public Tabs()
        {
            Add("a", "A", A);
            Add("b", "B", B);
            Add("c", "C", C);
        }
    }

    private static void OnSta(Action test)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { test(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Exception("STA test failed", error);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static Application EnsureApp()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        if (!app.Resources.Contains("Panel"))
        {
            app.Resources.MergedDictionaries.Add(Ui.Resources(Palette.Find("night")));
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Shabpa;component/Theme.xaml", UriKind.Absolute) });
        }
        return app;
    }

    [Fact]
    public void Clicking_a_tab_shows_that_tab_and_only_that_tab()
    {
        OnSta(() =>
        {
            EnsureApp();
            // Pages reach the hub for the sensor snapshot when a tab is shown.
            _ = new Hub(new Store(Path.Combine(Path.GetTempPath(), "shabpa-tabs-" + Guid.NewGuid().ToString("N"))), dryRun: true, demo: true);
            var tabs = new Tabs();
            var window = new Window { Content = tabs, Width = 600, Height = 400 };
            window.Show();
            window.UpdateLayout();
            try
            {
                var host = Descendants<ContentControl>(tabs).First(c => c.Content is PageBase);
                Assert.Same(tabs.A, host.Content);

                var radios = Descendants<RadioButton>(tabs).ToList();
                Assert.Equal(3, radios.Count);
                Assert.True(radios[0].IsChecked);

                radios[2].IsChecked = true; // what a click does
                window.UpdateLayout();
                Assert.Same(tabs.C, host.Content);
                Assert.False(radios[0].IsChecked);

                radios[0].IsChecked = true;
                window.UpdateLayout();
                Assert.Same(tabs.A, host.Content);
                Assert.Equal("sub-A", tabs.PageSubtitle);

                tabs.Select("b");
                Assert.Same(tabs.B, host.Content);
                Assert.True(radios[1].IsChecked);
                Assert.False(radios[0].IsChecked);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
