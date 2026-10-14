using System.Windows;
using Shabpa.Core;
using WF = System.Windows.Forms;

namespace Shabpa;

/// <summary>Notification-area icon: the app lives here when its window is closed.</summary>
public sealed class Tray : IDisposable
{
    private readonly WF.NotifyIcon _icon;
    private readonly WF.ToolStripMenuItem _next;

    public Tray(Action show, Action quit)
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/shabpa.ico"))!.Stream;
        _next = new WF.ToolStripMenuItem("") { Enabled = false };

        var menu = new WF.ContextMenuStrip { RightToLeft = L.En ? WF.RightToLeft.No : WF.RightToLeft.Yes, ShowImageMargin = false };
        menu.Items.Add(_next);
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add(L.T("باز کردن شب‌پا", "Open Shabpa"), null, (_, _) => show());
        menu.Items.Add(L.T("خاموشی در ۳۰ دقیقه", "Shut down in 30 minutes"), null, (_, _) => Quick(30));
        menu.Items.Add(L.T("خاموشی در ۱ ساعت", "Shut down in 1 hour"), null, (_, _) => Quick(60));
        menu.Items.Add(L.T("خالی کردن رم", "Free up RAM"), null, (_, _) => Hub.I.CleanMemory(cache: false));
        menu.Items.Add(L.T("لغو همه‌ی دستورها", "Cancel all commands"), null, (_, _) =>
        {
            Hub.I.CancelCountdown();
            Hub.I.Queue.Clear();
            Hub.I.Store.Save();
            Hub.I.RaiseChanged();
        });
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add(L.T("خروج", "Exit"), null, (_, _) => quit());

        _icon = new WF.NotifyIcon
        {
            Icon = new System.Drawing.Icon(stream, WF.SystemInformation.SmallIconSize),
            Text = L.T("شب‌پا", "Shabpa"),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == WF.MouseButtons.Left) show(); };
        _icon.BalloonTipClicked += (_, _) => show();
    }

    private static void Quick(int minutes)
    {
        Hub.I.AddCommand(PowerAction.Shutdown, DateTime.Now.AddMinutes(minutes));
        var span = Fa.Duration(TimeSpan.FromMinutes(minutes));
        Hub.I.Notify(L.T("زمان‌بندی شد", "Scheduled"), L.T($"سیستم {span} دیگه خاموش می‌شه.", $"The PC shuts down in {span}."));
    }

    public void Refresh()
    {
        var text = Hub.I.NextText();
        _next.Text = text;
        // NotifyIcon.Text is limited to 127 characters.
        var tip = $"{L.T("شب‌پا", "Shabpa")} · {text}";
        _icon.Text = tip.Length > 120 ? tip[..120] : tip;
    }

    public void Balloon(string title, string text) => _icon.ShowBalloonTip(6000, title, text, WF.ToolTipIcon.None);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
