using System.Windows;
using Shabpa.Core;
using D = System.Drawing;
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

        var menu = BuildMenu(show, quit, _next);

        _icon = new WF.NotifyIcon
        {
            Icon = new System.Drawing.Icon(stream, WF.SystemInformation.SmallIconSize),
            Text = L.T("شب‌پا", "Shabpa"),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == WF.MouseButtons.Left) show(); };
        _icon.BalloonTipClicked += (_, _) => { show(); Hub.I.OpenNoticeTarget(); };
    }

    /// <summary>The themed menu (also built on its own by the screenshot aid, without a notification-area icon).</summary>
    internal static WF.ContextMenuStrip BuildMenu(Action show, Action quit, WF.ToolStripMenuItem next)
    {
        var menu = new WF.ContextMenuStrip
        {
            RightToLeft = L.En ? WF.RightToLeft.No : WF.RightToLeft.Yes, ShowImageMargin = false,
            Renderer = new ThemedRenderer(), Font = new D.Font("Segoe UI", 9.75f), Padding = new WF.Padding(5),
            BackColor = ThemedRenderer.Pick("Bg"), ForeColor = ThemedRenderer.Pick("Text"),
        };
        // Windows 11: rounded corners like the app's own flyouts
        menu.HandleCreated += (_, _) => { var round = 2; Core.Native.DwmSetWindowAttribute(menu.Handle, 33, ref round, sizeof(int)); };
        // it fades in (160 ms, ease-out) instead of snapping open; the timer exists only while it fades
        menu.Opening += (_, _) => { menu.Opacity = Motion.Enabled ? 0 : 1; };
        menu.Opened += (_, _) => FadeIn(menu);
        // ...and fades out (110 ms) when it is dismissed, instead of vanishing; Close(CloseCalled) at the end is the real close
        var fadingOut = false;
        menu.Closing += (_, e) =>
        {
            if (fadingOut) { e.Cancel = true; return; }
            if (!Motion.Enabled || e.Cancel || !menu.Visible || e.CloseReason == WF.ToolStripDropDownCloseReason.CloseCalled) return;
            e.Cancel = true;
            fadingOut = true;
            FadeOut(menu, () => { fadingOut = false; menu.Close(WF.ToolStripDropDownCloseReason.CloseCalled); });
        };
        menu.Items.Add(next);
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add(L.T("باز کردن شب‌پا", "Open Shabpa"), null, (_, _) => show());
        menu.Items.Add(L.T("فرمان متنی…", "Type a command…"), null, (_, _) => AssistantWindow.Open());
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

        foreach (WF.ToolStripItem item in menu.Items)
            if (item is WF.ToolStripMenuItem) item.Padding = new WF.Padding(10, 7, 10, 7);
        return menu;
    }

    private static void FadeOut(WF.ToolStripDropDown menu, Action done)
    {
        var started = Environment.TickCount64;
        var from = menu.Opacity;
        var timer = new WF.Timer { Interval = 15 };
        timer.Tick += (_, _) =>
        {
            var p = Math.Min(1.0, (Environment.TickCount64 - started) / 110.0);
            if (!menu.Visible || menu.IsDisposed) p = 1;
            if (!menu.IsDisposed) menu.Opacity = from * (1 - p * p);
            if (p < 1) return;
            timer.Stop();
            timer.Dispose();
            if (!menu.IsDisposed) done();
        };
        timer.Start();
    }

    private static void FadeIn(WF.ToolStripDropDown menu)
    {
        if (!Motion.Enabled) { menu.Opacity = 1; return; }
        var started = Environment.TickCount64;
        var timer = new WF.Timer { Interval = 15 };
        timer.Tick += (_, _) =>
        {
            var p = Math.Min(1.0, (Environment.TickCount64 - started) / 160.0);
            if (!menu.Visible || menu.IsDisposed) p = 1;
            menu.Opacity = 1 - Math.Pow(1 - p, 3);
            if (p < 1) return;
            timer.Stop();
            timer.Dispose();
        };
        timer.Start();
    }

    /// <summary>Paints the menu with the theme's own colours (a flat sheet, a rounded hover pill, hairline separators) so the
    /// notification-area menu looks like the rest of the app instead of the plain Windows Forms one.</summary>
    private sealed class ThemedRenderer : WF.ToolStripProfessionalRenderer
    {
        public static D.Color Pick(string key)
        {
            var (r, g, b) = Ui.BaseTheme.Rgb(key);
            return D.Color.FromArgb(r, g, b);
        }

        private static D.Color Mix(D.Color a, D.Color b, double t) =>
            D.Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

        private readonly D.Color _bg = Pick("Bg"), _text = Pick("Text"), _muted = Pick("Muted"), _line = Mix(Pick("Bg"), Pick("Text"), 0.14), _hover = Mix(Pick("Bg"), Pick("Accent"), 0.2);

        public ThemedRenderer() : base(new WF.ProfessionalColorTable { UseSystemColors = false }) { RoundedEdges = false; }

        protected override void OnRenderToolStripBackground(WF.ToolStripRenderEventArgs e)
        {
            using var brush = new D.SolidBrush(_bg);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(WF.ToolStripRenderEventArgs e)
        {
            using var pen = new D.Pen(_line);
            var r = new D.Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            e.Graphics.DrawRectangle(pen, r);
        }

        protected override void OnRenderMenuItemBackground(WF.ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            e.Graphics.SmoothingMode = D.Drawing2D.SmoothingMode.AntiAlias;
            var r = new D.Rectangle(2, 1, e.Item.Width - 4, e.Item.Height - 2);
            using var path = new D.Drawing2D.GraphicsPath();
            const int d = 14;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            using var brush = new D.SolidBrush(_hover);
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnRenderItemText(WF.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? _text : _muted;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(WF.ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new D.Pen(_line);
            var y = e.Item.Height / 2;
            e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
        }
    }

    private static void Quick(int minutes)
    {
        var now = DateTime.Now;
        var due = now.AddMinutes(minutes);
        var span = Fa.Duration(TimeSpan.FromMinutes(minutes));
        if (!ConfirmWindow.Ask(L.T("خاموشی ثبت بشه؟", "Schedule a shutdown?"),
                L.T("سیستم خاموش می‌شه؛ قبلش شمارش معکوس قابل لغو می‌بینی. از لیست «بعدی چیه؟» می‌تونی حذفش کنی.", "The PC shuts down; you see a cancellable countdown first. Delete it any time from the “What's next?” list."),
                L.T("تایید و ثبت", "Confirm and schedule"),
                detail: L.T($"شروع: {Pages.HomePage.At(due, now)} ({span} دیگه)", $"Runs: {Pages.HomePage.At(due, now)} (in {span})")))
            return;
        if (Hub.I.AddCommand(PowerAction.Shutdown, due))
            Hub.I.Notify(L.T("زمان‌بندی شد", "Scheduled"), L.T($"سیستم {span} دیگه خاموش می‌شه.", $"The PC shuts down in {span}."));
        else
            Hub.I.Notify(L.T("از قبل ثبت شده بود", "Already scheduled"), L.T("همین دستور قبلاً توی لیسته.", "The same command is already in the list."));
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
