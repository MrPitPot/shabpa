using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using Shabpa.Core;

namespace Shabpa;

/// <summary>Motion probe of the Settings / Support / tab strip / dialog group (called from --motion-test, animations switched on):
/// everything is exercised while the clocks run and screenshots are taken in the middle of the motion. Any exception ends
/// up in the report.</summary>
public partial class App
{
    private static T? FindVisual<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T hit) return hit;
            if (FindVisual<T>(child) is { } deeper) return deeper;
        }
        return null;
    }

    private static void Call(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(target, args);

    /// <summary>Screenshot aid: the notification-area menu drawn to a PNG (no icon is added to the real tray).</summary>
    private static void TrayMenuShot(string folder)
    {
        try
        {
            var next = new System.Windows.Forms.ToolStripMenuItem(Hub.I.NextText()) { Enabled = false };
            using var menu = Tray.BuildMenu(() => { }, () => { }, next);
            menu.CreateControl();
            _ = menu.Handle;
            menu.Size = menu.PreferredSize;
            menu.PerformLayout();
            using var bitmap = new System.Drawing.Bitmap(menu.Width, menu.Height);
            menu.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, menu.Width, menu.Height));
            bitmap.Save(Path.Combine(folder, "tray-menu.png"));
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(folder, "tray-menu-error.txt"), ex.ToString());
        }
    }

    private async Task SettingsGroupMotion(string folder, System.Text.StringBuilder log)
    {
        try
        {
            // Settings: page entrance, a notice that arrives while it is open, saving the power boxes, rating stars
            _window!.Navigate("settings");
            await Task.Delay(350);
            Save(_window.CaptureRoot, Path.Combine(folder, "motion-f-settings-enter.png"));
            await Task.Delay(1200);
            Hub.I.Notify(L.T("آزمایش", "Probe"), L.T("یک اعلان تازه", "A fresh notice"));
            await Task.Delay(200);
            var settings = FindVisual<Pages.SettingsPage>(_window.CaptureRoot);
            if (settings is not null) { settings.Refresh(); Call(settings, "OnSavePower", null!, null!); }
            await Task.Delay(150);
            var rating = settings is null ? null : FindVisual<Pages.RatingCard>(settings);
            if (rating is not null) Call(rating, "Choose", 4);
            await Task.Delay(260);
            Save(_window.CaptureRoot, Path.Combine(folder, "motion-f-settings-rating.png"));
            await Task.Delay(1000);
            log.AppendLine($"settings: notice, save pulse and rating cascade ran (rating card found: {rating is not null}, rating now {Hub.I.Settings.Rating})");
            Hub.I.Settings.Rating = 0;

            // Support: page entrance, then a ticket that is added while the page is open
            _window.Navigate("support");
            await Task.Delay(350);
            Save(_window.CaptureRoot, Path.Combine(folder, "motion-f-support-enter.png"));
            await Task.Delay(1200);
            var support = FindVisual<Pages.SupportPage>(_window.CaptureRoot);
            if (support is not null) Call(support, "Remember", "probe-1", L.T("تیکت آزمایشی", "Probe ticket"), "github");
            await Task.Delay(220);
            Save(_window.CaptureRoot, Path.Combine(folder, "motion-f-support-ticket.png"));
            await Task.Delay(900);
            Hub.I.State.Tickets.Clear();
            log.AppendLine($"support: ticket row rose in (page found: {support is not null})");

            // tab strip (ping stability): the thumb springs to the second tab, the content switches
            _window.Navigate("ping");
            await Task.Delay(1400);
            _window.Navigate("ping:usage");
            await Task.Delay(260);
            Save(_window.CaptureRoot, Path.Combine(folder, "motion-f-tabs.png"));
            await Task.Delay(900);
            log.AppendLine("tabs: segmented strip switched to usage");

            // dialogs: the confirm flyout and the countdown at the start of their entrance
            var confirm = ConfirmWindow.Preview(L.T("این زمان‌بندی ثبت بشه؟", "Schedule this?"), L.T("سیستم خاموش می‌شه.", "The PC shuts down."),
                L.T("تایید و ثبت", "Confirm and schedule"), L.T("انصراف", "Cancel"), false, L.T("شروع: ۳۰ دقیقه دیگه", "Runs: in 30 minutes"));
            await Task.Delay(130);
            Save((FrameworkElement)confirm.Content, Path.Combine(folder, "motion-f-confirm-mid.png"));
            await Task.Delay(900);
            confirm.Close();
            var countdown = new CountdownWindow(PowerAction.Shutdown, L.T("دستور زمان‌بندی‌شده", "Scheduled command"), 8);
            countdown.Show();
            await Task.Delay(130);
            Save((FrameworkElement)countdown.Content, Path.Combine(folder, "motion-f-countdown-mid.png"));
            await Task.Delay(4200);
            Save((FrameworkElement)countdown.Content, Path.Combine(folder, "motion-f-countdown-late.png"));
            countdown.Finish(CountdownOutcome.Cancel);
            var banner = new MacroBanner(true);
            banner.SetText(L.T("در حال یادگیری…", "Learning…"));
            banner.Show();
            await Task.Delay(300);
            Save((FrameworkElement)banner.Content, Path.Combine(folder, "motion-f-banner.png"));
            banner.Close();
            log.AppendLine("dialogs: confirm, countdown and macro banner entrances ran");
        }
        catch (Exception ex)
        {
            log.AppendLine("group F error: " + ex);
        }
    }
}
