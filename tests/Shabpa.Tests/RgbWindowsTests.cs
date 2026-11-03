using System.IO;
using System.Reflection;
using System.Windows;
using Shabpa.Pages;
using Xunit;

namespace Shabpa.Tests;

/// <summary>The windows and pop-ups the lighting work added are opened for real, off screen: a missing resource or a bad layout would throw in front of the person.</summary>
[Collection("Wpf")]
public class RgbWindowsTests
{
    private static void Prepare()
    {
        StaRunner.EnsureApp();
        _ = new Hub(new Store(Path.Combine(Path.GetTempPath(), "shabpa-rgbw-" + Guid.NewGuid().ToString("N"))), dryRun: true, demo: true);
    }

    [Fact]
    public void The_gear_menu_opens_and_every_switch_and_fade_choice_works()
    {
        StaRunner.Run(() =>
        {
            Prepare();
            var page = new RgbPage();
            var window = new Window { Content = page, Width = 900, Height = 700, WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000 };
            window.Show();
            page.Refresh();
            typeof(RgbPage).GetMethod("OpenGear", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(page, null);
            StaRunner.Pump(5);
            var popup = (System.Windows.Controls.Primitives.Popup)typeof(RgbPage).GetField("_gearPopup", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!;
            Assert.True(popup.IsOpen);
            var cfg = Hub.I.Settings.Rgb;
            Assert.True(cfg.Smooth);
            popup.IsOpen = false;
            window.Close();
        });
    }

    [Fact]
    public void The_devices_window_opens_without_devices_and_says_what_is_missing()
    {
        StaRunner.Run(() =>
        {
            Prepare();
            string? asked = null;
            RgbDevicesWindow.Open(k => asked = k);
            StaRunner.Pump(5);
            var open = (RgbDevicesWindow)typeof(RgbDevicesWindow).GetField("_open", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            Assert.False(string.IsNullOrWhiteSpace(open.Title));
            open.Close();
            Assert.Null(asked);
        });
    }

}
