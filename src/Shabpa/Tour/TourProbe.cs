using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Tour;

/// <summary>Test aid for <c>--tour-test &lt;report&gt;</c> (needs <c>--screenshots &lt;folder&gt;</c>, optionally <c>--lang en</c> and <c>--theme white</c>):
/// starts the tour in the off-screen window, walks every step (asking for the click by opening the page the step names, which is what the
/// menu item does; the mouse itself is not used), saves one picture per step and writes where the lit hole and the bubble were.
/// Then it checks the keys, plays two steps with animations on and shows the "want a tour?" card.</summary>
internal static class TourProbe
{
    public static async Task Run(MainWindow window, string report, string folder, Action<FrameworkElement, string> save)
    {
        var sb = new StringBuilder();
        if (Environment.GetCommandLineArgs().Contains("--tour-cpu")) { await Cpu(window, report); return; }
        try
        {
            Directory.CreateDirectory(folder);
            await Task.Delay(3500);
            Hub.I.Settings.Animations = false; // settled pictures
            var tag = L.En ? "en" : "fa";
            sb.AppendLine($"lang={tag} theme={Ui.Theme.Key} window={window.ActualWidth:0}x{window.ActualHeight:0}");

            window.StartTour();
            await Task.Delay(700);
            var tour = window.ActiveTour ?? throw new InvalidOperationException("the tour did not start");
            var session = tour.Session;
            sb.AppendLine($"steps={session.Steps.Count} first={session.Current.Id}");
            var problems = 0;
            for (var i = 0; i < session.Steps.Count; i++)
            {
                var step = session.Current;
                await Task.Delay(1300);
                window.UpdateLayout();
                var hole = tour.Hole;
                var bubble = tour.Bubble;
                var area = new Rect(0, 0, tour.ActualWidth, tour.ActualHeight);
                var inside = area.Contains(bubble);
                var overlap = hole is { } h && h.Width > 2 && Rect.Intersect(h, bubble) is { IsEmpty: false } o ? o.Width * o.Height : 0;
                var shown = step.Target.Length == 0 || hole is { Width: > 2 };
                if (!inside || !shown) problems++;
                sb.AppendLine($"{i + 1:00} {step.Id,-12} target={step.Target,-16} page={window.ActiveTourPage,-9} hole={Fmt(hole)} bubble={Fmt(bubble)} insideWindow={inside} overlapPx2={overlap:0}{(shown ? "" : " TARGET-NOT-LIT")}");
                save(window.CaptureRoot, Path.Combine(folder, $"tour-{tag}-{i + 1:00}-{step.Id}.png"));
                if (step.NeedsClick)
                {
                    window.Navigate(step.ClickPage); // what clicking the menu item does
                    await Task.Delay(100);
                    sb.AppendLine($"   click on {step.ClickPage}: moved on by itself = {session.Current.Id != step.Id}");
                    if (session.Current.Id == step.Id) { problems++; session.Next(); }
                }
                else if (i < session.Steps.Count - 1) session.Next();
            }

            // keys: Back, Next and Esc through the window's key tunnel
            var s2 = session;
            if (!s2.Ended)
            {
                var before = s2.Index;
                Press(window, Key.Escape);
                await Task.Delay(100);
                sb.AppendLine($"Esc skips: ended={s2.Ended} outcome={s2.Outcome}");
            }
            await Task.Delay(500);
            sb.AppendLine($"after the tour: active={(window.ActiveTour is null ? "none" : "STILL THERE")} status={Hub.I.Settings.TourStatus} children-left-on-root={window.CaptureRootOverlays}");

            // keyboard on a fresh tour, in the language's own direction
            window.StartTour();
            await Task.Delay(600);
            var forward = Ui.Flow == FlowDirection.LeftToRight ? Key.Right : Key.Left;
            var backward = Ui.Flow == FlowDirection.LeftToRight ? Key.Left : Key.Right;
            var t2 = window.ActiveTour!.Session;
            Press(window, forward); sb.AppendLine($"forward arrow -> step {t2.Index + 1}");
            Press(window, Key.Enter); sb.AppendLine($"Enter -> step {t2.Index + 1}");
            Press(window, backward); sb.AppendLine($"backward arrow -> step {t2.Index + 1}");
            Press(window, Key.Escape);
            await Task.Delay(300);

            // with animations on: the hole should be between two targets half-way through
            Hub.I.Settings.Animations = true;
            window.StartTour();
            await Task.Delay(1500);
            var live = window.ActiveTour!;
            live.Session.Next(); // sidebar
            await Task.Delay(2000);
            Rect? a = live.LiveHole;
            live.Session.Next(); // home block
            await Task.Delay(120);
            Rect? mid = live.LiveHole;
            await Task.Delay(1800);
            Rect? b = live.LiveHole;
            sb.AppendLine($"motion: sidebar={Fmt(a)} 120ms-later={Fmt(mid)} settled={Fmt(b)} (middle value must differ from both)");
            save(window.CaptureRoot, Path.Combine(folder, $"tour-{tag}-motion.png"));
            Press(window, Key.Escape);
            await Task.Delay(800);
            sb.AppendLine($"motion on, after Esc: overlay removed = {window.ActiveTour is null && window.CaptureRootOverlays == 0}");
            Hub.I.Settings.Animations = false;

            // Skip at the very moment a step opens its page, with animations on: the person must still land on Home
            Hub.I.Settings.Animations = true;
            window.StartTour();
            await Task.Delay(1200);
            var racer = window.ActiveTour!.Session;
            while (racer.Current.Id != "dns") { racer.Next(); await Task.Delay(30); }
            Press(window, Key.Escape);                 // the page "dns" is only just opening
            await Task.Delay(2500);
            sb.AppendLine($"skip while dns is opening: page={window.ActiveTourPage} (home expected) showing home={window.HomeIsShowing}");
            if (window.ActiveTourPage != "home" || !window.HomeIsShowing) problems++;

            // modal keys: Tab, Space, Ctrl+number must be swallowed (not reach the window); a page must not change under the curtain
            Hub.I.Settings.Animations = false;
            window.StartTour();
            await Task.Delay(700);
            var pageBefore = window.ActiveTourPage;
            var swallowed = new[] { Press(window, Key.Tab), Press(window, Key.Space), Press(window, Key.D3, ModifierKeys.Control), Press(window, Key.OemComma, ModifierKeys.Control) };
            await Task.Delay(300);
            sb.AppendLine($"modal keys swallowed={string.Join(",", swallowed)} page unchanged={window.ActiveTourPage == pageBefore} tour still open={window.ActiveTour is not null}");
            if (swallowed.Contains(false) || window.ActiveTourPage != pageBefore) problems++;
            Press(window, Key.Escape);
            await Task.Delay(300);

            // the card for people who updated
            window.ShowTourOffer();
            await Task.Delay(500);
            save(window.CaptureRoot, Path.Combine(folder, $"tour-{tag}-offer.png"));
            sb.AppendLine($"offer card shown: {window.TourOfferCard is not null}");
            sb.AppendLine(problems == 0 ? "RESULT: every step lit its target and kept the bubble inside the window" : $"RESULT: {problems} problem(s)");
        }
        catch (Exception ex)
        {
            sb.AppendLine("ERROR: " + ex);
        }
        File.WriteAllText(report, sb.ToString());
        Application.Current.Shutdown();
    }

    /// <summary>Test aid (<c>--tour-test r --tour-cpu</c>): idle CPU of the whole process (one core = 100 %) with nothing, with the tour
    /// settled on a step, with the "want a tour?" card, and with the tour while the window is minimised; 20 s warm-up, 10 s measured each.</summary>
    private static async Task Cpu(MainWindow window, string report)
    {
        var sb = new StringBuilder();
        Hub.I.Settings.Animations = true;
        sb.AppendLine($"motion enabled={Motion.Enabled}");
        await Task.Delay(6000);
        async Task Measure(string name)
        {
            await Task.Delay(20000);
            var proc = System.Diagnostics.Process.GetCurrentProcess();
            var t0 = proc.TotalProcessorTime;
            var w0 = DateTime.UtcNow;
            await Task.Delay(10000);
            proc.Refresh();
            var pct = (proc.TotalProcessorTime - t0).TotalSeconds / (DateTime.UtcNow - w0).TotalSeconds * 100;
            sb.AppendLine($"{name}: {pct:0.0} % of one core");
            File.WriteAllText(report, sb.ToString());
        }
        await Measure("no tour");
        window.StartTour();
        window.ActiveTour!.Session.Next(); // sidebar step
        await Measure("tour open and still");
        window.ActiveTour!.Session.Skip();
        await Task.Delay(1000);
        window.ShowTourOffer();
        await Measure("offer card");
        window.HideTourOfferNow();
        window.StartTour();
        window.ActiveTour!.Session.Next();
        window.WindowState = WindowState.Minimized;
        await Measure("tour open, window minimised");
        window.WindowState = WindowState.Normal;
        File.WriteAllText(report, sb.ToString());
        Application.Current.Shutdown();
    }

    private static string Fmt(Rect? r) => r is { } x ? $"({x.X:0},{x.Y:0},{x.Width:0},{x.Height:0})" : "-";

    /// <summary>Raises the key through the window's key tunnel; returns whether somebody handled it.</summary>
    private static bool Press(Window w, Key key, ModifierKeys modifiers = ModifierKeys.None)
    {
        var source = PresentationSource.FromVisual(w);
        if (source is null) return false;
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        w.RaiseEvent(args);
        return args.Handled;
    }
}
