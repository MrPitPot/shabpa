using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Shabpa.Kit;
using Xunit;

namespace Shabpa.Tests;

/// <summary>A wheel turn over a single-line TextBox: the box's own ScrollViewer has nothing to scroll and hands the wheel on.
/// That once re-entered itself without end (a stack overflow that killed the app the moment the wheel turned over a text box
/// on Settings, Support or Update). The wheel must reach the page behind and the app must stay alive.</summary>
[Collection("Wpf")]
public class SmoothScrollTextBoxTests
{
    private static void Sta(Action test)
    {
        Exception? error = null;
        Motion.TestOverride = true;
        var thread = new Thread(() => { try { test(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Motion.TestOverride = null;
        if (error is not null) throw new Exception("STA test failed", error);
    }

    private static void Pump(double seconds)
    {
        var end = DateTime.UtcNow.AddSeconds(seconds);
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(5) };
        timer.Tick += (_, _) => { if (DateTime.UtcNow >= end) frame.Continue = false; };
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T hit) return hit;
            if (Find<T>(child) is { } deeper) return deeper;
        }
        return null;
    }

    [Fact]
    public void Wheel_over_a_text_box_scrolls_the_page_behind_and_does_not_recurse()
    {
        Sta(() =>
        {
            SmoothScroll.Install();
            var box = new TextBox { Width = 200, Margin = new Thickness(0, 400, 0, 0) };
            var stack = new StackPanel { Children = { box, new Border { Height = 3000, Width = 100 } } };
            var page = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stack };
            var win = new Window { Left = -32000, Top = -32000, Width = 400, Height = 300, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Content = page };
            win.Show();
            try
            {
                Pump(0.3);
                var inner = Find<ScrollViewer>(box);
                Assert.NotNull(inner);   // the box's own viewer (PART_ContentHost)
                for (var i = 0; i < 3; i++)
                {
                    var e = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.MouseWheelEvent, Source = inner };
                    inner!.RaiseEvent(e);
                }
                Pump(0.8);
                Assert.True(page.VerticalOffset > 0, $"the page did not move (offset {page.VerticalOffset})");
            }
            finally { win.Close(); }
        });
    }

    [Fact]
    public void Inner_viewer_at_its_end_hands_the_wheel_to_the_outer_viewer()
    {
        Sta(() =>
        {
            SmoothScroll.Install();
            var inner = new ScrollViewer { Height = 80, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new Border { Height = 40, Width = 50 } }; // nothing to scroll
            var outer = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new StackPanel { Children = { inner, new Border { Height = 3000, Width = 50 } } } };
            var win = new Window { Left = -32000, Top = -32000, Width = 300, Height = 300, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Content = outer };
            win.Show();
            try
            {
                Pump(0.3);
                var e = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.MouseWheelEvent, Source = inner };
                inner.RaiseEvent(e);
                Pump(0.8);
                Assert.True(outer.VerticalOffset > 0);
            }
            finally { win.Close(); }
        });
    }
}
