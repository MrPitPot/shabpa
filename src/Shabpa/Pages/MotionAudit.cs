using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>Test aid for <c>--motion-audit &lt;report&gt;</c> (needs <c>--screenshots &lt;folder&gt;</c>, nothing is saved or shown on the desktop):
/// for every page it measures the entrance (staggered blocks), the exit of the old page, which clickable things have hover and press
/// feedback (simulated with real routed events), what is still animated once the page has settled, and the idle CPU. Then 200 page
/// switches are made and memory and running animations are compared. The result is a machine-readable text report.</summary>
internal static class MotionAudit
{
    // ---- is something animated right now? ---------------------------------------------------------

    /// <summary>True if the element has an animated property of its own (opacity, transform, brush, glow...).</summary>
    public static bool Animated(DependencyObject e, StringBuilder? why = null)
    {
        var any = false;
        foreach (var entry in e.GetLocalValueEnumerator() is var en ? Enumerate(en) : [])
        {
            if (DependencyPropertyHelper.GetValueSource(e, entry.Property).IsAnimated) { any = true; why?.Append(entry.Property.Name).Append(' '); }
            else if (entry.Value is Animatable a && AnimFreezable(a)) { any = true; why?.Append(entry.Property.Name).Append("* "); }
            if (any && why is null) return true;
        }
        return any;
    }

    private static IEnumerable<LocalValueEntry> Enumerate(LocalValueEnumerator en)
    {
        while (en.MoveNext()) yield return en.Current;
    }

    private static bool AnimFreezable(Animatable a)
    {
        if (a.HasAnimatedProperties) return true;
        switch (a)
        {
            case TransformGroup g: return g.Children.Any(AnimFreezable);
            case GradientBrush gb: return gb.GradientStops.Any(AnimFreezable) || (gb.Transform is { } t && AnimFreezable(t)) || (gb.RelativeTransform is { } r && AnimFreezable(r));
            case Brush b: return (b.Transform is { } t2 && AnimFreezable(t2)) || (b.RelativeTransform is { } r2 && AnimFreezable(r2));
        }
        return false;
    }

    public static List<string> AnimatedIn(DependencyObject root)
    {
        var list = new List<string>();
        foreach (var d in All(root))
        {
            var why = new StringBuilder();
            if (Animated(d, why)) list.Add($"{d.GetType().Name}[{why.ToString().Trim()}]");
        }
        return list;
    }

    public static IEnumerable<DependencyObject> All(DependencyObject root)
    {
        yield return root;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
            foreach (var child in All(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    // ---- the animation clocks of the whole app (reflection into WPF's time manager) ----------------------

    private static readonly Type? MediaContextType = typeof(Visual).Assembly.GetType("System.Windows.Media.MediaContext");

    /// <summary>Counts the clocks WPF is ticking: Active ones cost frames, Filling ones hold a value (and should be gone once a page settles).</summary>
    public static (int active, int filling, int total) Clocks()
    {
        try
        {
            var ctx = MediaContextType!.GetProperty("CurrentMediaContext", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(null)!;
            var tm = MediaContextType.GetProperty("TimeManager", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(ctx)!;
            var root = (ClockGroup)tm.GetType().GetProperty("TimeManagerClock", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(tm)!;
            int a = 0, f = 0, t = 0;
            void Walk(Clock c)
            {
                if (c is ClockGroup g) { foreach (var child in g.Children) Walk(child); return; }
                t++;
                if (c.CurrentState == ClockState.Active) a++;
                else if (c.CurrentState == ClockState.Filling) f++;
            }
            Walk(root);
            return (a, f, t);
        }
        catch { return (-1, -1, -1); }
    }

    // ---- clickables -----------------------------------------------------------------------------------

    private static readonly PropertyInfo? StoreProp = typeof(UIElement).GetProperty("EventHandlersStore", BindingFlags.Instance | BindingFlags.NonPublic);
    private static MethodInfo? _contains;
    public static bool StoreWorks;

    private static bool Has(UIElement e, RoutedEvent ev)
    {
        try
        {
            var store = StoreProp?.GetValue(e);
            if (store is null) return false;
            _contains ??= store.GetType().GetMethod("Contains", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, [typeof(RoutedEvent)]);
            if (_contains is null) return false;
            StoreWorks = true;
            return (bool)_contains.Invoke(store, [ev])!;
        }
        catch { return false; }
    }

    private static bool IsTemplatedInternal(DependencyObject e)
    {
        for (var p = VisualTreeHelper.GetParent(e); p is not null; p = VisualTreeHelper.GetParent(p))
            if (p is ButtonBase or Slider or ComboBox or ScrollBar or TextBoxBase or PasswordBox or TabItem or ListBoxItem or Segmented or Swatches) return true;
        return false;
    }

    private static bool IsClickable(FrameworkElement e, out string kind)
    {
        kind = "";
        if (e is Thumb or ScrollBar or ScrollViewer or Popup) return false;
        if ((e is ButtonBase or Thumb) && IsTemplatedInternal(e)) return false;
        if (e is GlassCard gc) { kind = "custom:GlassCard"; return gc.Clicked is not null; } // every card hooks mouse-up; only a card with an action is a button
        if (e is Slider) { kind = "slider"; return true; }
        if (e is ComboBox) { kind = "combo"; return true; }
        if (e is Kit.Switch) { kind = "switch"; return true; }
        if (e is ButtonBase) { kind = e.GetType().Name.ToLowerInvariant(); return true; }
        if (e is Segmented) { kind = "segmented"; return true; }
        if (e is Swatches) { kind = "swatches"; return true; }
        if (e is TextBox or PasswordBox) { kind = "textbox"; return true; }
        if (e is TabItem or ListBoxItem) { kind = "item"; return true; }
        if (e is Thumb or ScrollBar or ScrollViewer or Popup) return false;
        if (IsTemplatedInternal(e)) return false;
        var click = Has(e, UIElement.MouseLeftButtonUpEvent) || Has(e, UIElement.MouseLeftButtonDownEvent) || Has(e, UIElement.PreviewMouseLeftButtonUpEvent) ||
                    Has(e, UIElement.PreviewMouseLeftButtonDownEvent) || Has(e, UIElement.MouseUpEvent) || Has(e, UIElement.MouseDownEvent) ||
                    (e is GlassCard { Clicked: not null }) || e.Cursor == Cursors.Hand;
        if (click) { kind = "custom:" + e.GetType().Name; return true; }
        return false;
    }

    private static bool StyleTrigger(FrameworkElement e, DependencyProperty prop)
    {
        for (var s = e.Style; s is not null; s = s.BasedOn)
            if (HasTrigger(s.Triggers, prop)) return true;
        if (e is Control c && c.Template is { } t && HasTrigger(t.Triggers, prop)) return true;
        return false;
    }

    private static bool HasTrigger(IEnumerable<TriggerBase> triggers, DependencyProperty prop)
    {
        foreach (var t in triggers)
        {
            if (t is Trigger tr && tr.Property == prop) return true;
            if (t is MultiTrigger mt && mt.Conditions.Any(c => c.Property == prop)) return true;
        }
        return false;
    }

    private static string Describe(FrameworkElement e)
    {
        var text = e is ContentControl cc ? cc.Content as string : null;
        text ??= string.Concat(All(e).OfType<TextBlock>().Take(2).Select(t => t.Text));
        text = text?.Replace('\n', ' ').Trim() ?? "";
        if (text.Length > 24) text = text[..24];
        var par = VisualTreeHelper.GetParent(e) is { } pa ? pa.GetType().Name : "";
        return $"{e.GetType().Name}({text}) w{e.ActualWidth:0} in {par} fx[l{Fx.GetLift(e)} p{Fx.GetPress(e)} g{Fx.GetGrow(e)}]{(e.IsEnabled ? "" : " [disabled]")}";
    }

    private sealed record ClickResult(string Kind, string Name, string Hover, string Press);

    /// <summary>Simulates the pointer entering each clickable and pressing it, and reports what reacted.</summary>
    private static async Task<List<ClickResult>> Clickables(FrameworkElement root)
    {
        var list = new List<ClickResult>();
        var targets = All(root).OfType<FrameworkElement>().Where(e => e.IsVisible && e.ActualWidth > 0 && IsClickable(e, out _)).ToList();
        foreach (var e in targets)
        {
            IsClickable(e, out var kind);
            string hover;
            var fxHover = Fx.GetLift(e) != 0 || Fx.GetGrow(e) != 1 || Fx.GetNudge(e) != 0;
            var fxPress = Fx.GetPress(e) != 1;
            var before = Clocks().total; var sigBefore = Signature(e);
            var mouse = new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseEnterEvent, Source = e };
            try { e.RaiseEvent(mouse); } catch { }
            var react = Clocks().total > before || Differs(sigBefore, Signature(e));
            // a hover handler that fired but animated nothing might still change a brush instantly: report the handler
            var handler = Has(e, Mouse.MouseEnterEvent) || Has(e, Mouse.MouseMoveEvent) || Has(e, UIElement.MouseLeaveEvent);
            var trigger = StyleTrigger(e, UIElement.IsMouseOverProperty) || (e is Control && StyleTrigger(e, UIElement.IsKeyboardFocusWithinProperty));
            hover = react ? "anim" : fxHover ? "fx-no-run" : handler ? "handler" : trigger ? "tint" : kind is "textbox" ? "focus" : "NONE";
            var leave = new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseLeaveEvent, Source = e };
            try { e.RaiseEvent(leave); } catch { }
            var press = fxPress ? "fx" : StyleTrigger(e, ButtonBase.IsPressedProperty) ? "tint" : kind is "textbox" or "slider" or "segmented" or "swatches" or "combo" ? "n/a" : (kind.StartsWith("custom") && Has(e, UIElement.PreviewMouseLeftButtonDownEvent)) ? "handler" : "NONE";
            if (kind is "checkbox" or "switch" or "radiobutton" or "togglebutton") press = fxPress ? "fx" : press;
            // composite controls whose feedback lives in their parts: slider thumb, segmented labels, swatch dots
            if (e is Slider sl2 && All(sl2).OfType<Thumb>().FirstOrDefault() is { } th) { hover = Fx.GetGrow(th) != 1 ? "thumb-grow" : "NONE"; press = Fx.GetPress(th) != 1 ? "thumb-fx" : "NONE"; }
            else if (e is Segmented sg2) { var lab = All(sg2).OfType<TextBlock>().FirstOrDefault(); if (lab is not null) { hover = Fx.GetLift(lab) != 0 ? "label-lift" : "NONE"; press = Fx.GetPress(lab) != 1 ? "label-fx" : "NONE"; } }
            else if (e is Kit.Switch swi && swi.Template?.FindName("PART_Knob", swi) is FrameworkElement knob && knob.Parent is FrameworkElement body) { hover = Fx.GetGrow(body) != 1 ? "body-grow" : "NONE"; press = "knob-stretch"; }
            else if (e is Swatches sw2&& All(sw2).OfType<Grid>().FirstOrDefault() is { } dot) { hover = Fx.GetGrow(dot) != 1 ? "dot-grow" : "NONE"; press = Fx.GetPress(dot) != 1 ? "dot-fx" : "NONE"; }
            list.Add(new ClickResult(kind, Describe(e), hover, press));
        }
        await Task.Delay(20);
        return list;
    }

    /// <summary>Numbers that motion changes: opacity, size of animated things, transform parts and the card hover glow of every element.</summary>
    private static List<double> Signature(DependencyObject root)
    {
        var list = new List<double>();
        foreach (var d in All(root))
        {
            if (d is not FrameworkElement e) continue;
            list.Add(e.Opacity);
            if (e is GlassCard g) list.Add(g.HoverGlow);
            if (!double.IsNaN(e.Width)) list.Add(e.Width);
            if (e.RenderTransform is { } t) AddTransform(t, list);
        }
        return list;
    }

    private static void AddTransform(Transform t, List<double> list)
    {
        switch (t)
        {
            case TransformGroup g: foreach (var c in g.Children) AddTransform(c, list); break;
            case TranslateTransform tr: list.Add(tr.X); list.Add(tr.Y); break;
            case ScaleTransform sc: list.Add(sc.ScaleX); list.Add(sc.ScaleY); break;
            case RotateTransform ro: list.Add(ro.Angle); break;
        }
    }

    private static bool Differs(List<double> a, List<double> b)
    {
        if (a.Count != b.Count) return true;
        for (var i = 0; i < a.Count; i++) if (Math.Abs(a[i] - b[i]) > 0.002) return true;
        return false;
    }

    // ---- dialogs, toast, overlay -------------------------------------------------------------------------

    private static async Task<List<double>> Sample(Func<double> read, int count, int ms)
    {
        var list = new List<double>();
        for (var i = 0; i < count; i++) { list.Add(read()); await Task.Delay(ms); }
        return list;
    }

    private static string Series(IEnumerable<double> v) => string.Join(" ", v.Select(x => x.ToString("0.00")));

    private static Button? FindButton(DependencyObject root, string text) =>
        All(root).OfType<Button>().FirstOrDefault(b => string.Concat(All(b).OfType<TextBlock>().Select(t => t.Text)).StartsWith(text, StringComparison.Ordinal) || (b.Content as string)?.StartsWith(text, StringComparison.Ordinal) == true);

    private static async Task Dialogs(MainWindow window, Action<string> W, string folder, Action<FrameworkElement, string> save)
    {
        W("");
        W("== DIALOGS (sheet opacity every 25 ms; exit = time from click to Closed)");
        try
        {
            window.Navigate("home");
            await Task.Delay(1500);
            // toast
            var toast = window.ShellToast;
            toast.Visibility = Visibility.Collapsed;
            var shown = window.ShowToast("Title", "A notice that slides in and out");
            var inSeries = await Sample(() => toast.Opacity, 14, 25);
            W($"toast in  : shown={shown} opacity {Series(inSeries)}");
            toast.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
            var outSeries = await Sample(() => toast.Visibility == Visibility.Visible ? toast.Opacity : -1, 14, 25);
            W($"toast out : opacity {Series(outSeries)}  (-1 = hidden)");
            await Task.Delay(300);

            // confirm
            var confirm = ConfirmWindow.Preview("Confirm?", "Message", "Yes", "No", false, "detail");
            var sheet = (FrameworkElement)((Panel)confirm.Content).Children[2];
            var cin = await Sample(() => sheet.Opacity, 16, 25);
            W($"confirm in : opacity {Series(cin)}");
            await Task.Delay(700);
            var closedAt = 0.0;
            var sw = Stopwatch.StartNew();
            confirm.Closed += (_, _) => closedAt = sw.Elapsed.TotalMilliseconds;
            FindButton(confirm, "No")?.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            var cout = await Sample(() => sheet.Opacity, 12, 20);
            await Task.Delay(300);
            W($"confirm out: opacity {Series(cout)} closed after {closedAt:0} ms (0 = never closed)");

            // countdown
            var countdown = new CountdownWindow(PowerAction.Shutdown, "reason", 30);
            countdown.Show();
            var kin = await Sample(() => countdown.Sheet.Opacity, 16, 25);
            W($"countdown in : opacity {Series(kin)}");
            await Task.Delay(1200);
            closedAt = 0; sw.Restart();
            countdown.Closed += (_, _) => closedAt = sw.Elapsed.TotalMilliseconds;
            countdown.Finish(CountdownOutcome.Cancel);
            var kout = await Sample(() => countdown.Sheet.Opacity, 12, 20);
            await Task.Delay(300);
            W($"countdown out: opacity {Series(kout)} closed after {closedAt:0} ms");

            // macro banner
            var banner = new MacroBanner(true);
            banner.SetText("Teaching");
            banner.Show();
            var bar = (FrameworkElement)banner.Content;
            var bin = await Sample(() => bar.Opacity, 16, 25);
            W($"banner in : opacity {Series(bin)}");
            await Task.Delay(900);
            closedAt = 0; sw.Restart();
            banner.Closed += (_, _) => closedAt = sw.Elapsed.TotalMilliseconds;
            banner.Close();
            var bout = await Sample(() => bar.Opacity, 12, 20);
            await Task.Delay(300);
            W($"banner out: opacity {Series(bout)} closed after {closedAt:0} ms");

            // overlay HUD
            var hub = Hub.I;
            var previous = hub.Settings.Overlay.Mode;
            hub.Settings.Overlay.Mode = OverlayMode.Always;
            var hud = new OverlayWindow(hub);
            new System.Windows.Interop.WindowInteropHelper(hud).EnsureHandle();
            hub.Overlay = hud;
            hud.Evaluate();
            var hin = await Sample(() => hud.Opacity, 14, 25);
            W($"overlay HUD in : visible={hud.IsVisible} opacity {Series(hin)}");
            await Task.Delay(1500);
            hub.Settings.Overlay.Mode = OverlayMode.Off;
            hud.Evaluate();
            var hout = await Sample(() => hud.IsVisible ? hud.Opacity : -1, 14, 25);
            W($"overlay HUD out: opacity {Series(hout)}  (-1 = hidden)");
            hub.Settings.Overlay.Mode = previous;
            hud.Close();
            hub.Overlay = null;

            // tray menu (Windows Forms ContextMenuStrip, shown far off-screen): fade in on open, fade out on dismiss
            var item = new System.Windows.Forms.ToolStripMenuItem("next") { Enabled = false };
            var menu = Tray.BuildMenu(() => { }, () => { }, item);
            menu.Show(new System.Drawing.Point(-30000, -30000));
            var tin = await Sample(() => menu.Opacity, 14, 20);
            W($"tray menu in : opacity {Series(tin)}");
            await Task.Delay(400);
            var tsw = Stopwatch.StartNew();
            menu.Close(System.Windows.Forms.ToolStripDropDownCloseReason.AppFocusChange);
            var tout = await Sample(() => menu.Visible ? menu.Opacity : -1, 12, 20);
            W($"tray menu out: opacity {Series(tout)} (-1 = closed) visible-after={menu.Visible}");
            menu.Dispose();
        }
        catch (Exception ex)
        {
            W("dialog audit error: " + ex);
        }
    }

    private static int HalfFaded(DependencyObject root) =>
        All(root).OfType<FrameworkElement>().Count(d => d.IsVisible && d.Opacity is > 0.02 and < 0.98);

    /// <summary>Real data changes on the Home page: a command is added, then deleted. The new row must rise in (some elements
    /// half-faded for a moment), the stat numbers must glow on a new reading, and nothing may stay faded afterwards.</summary>
    private static async Task Dynamic(MainWindow window, Action<string> W)
    {
        W("");
        W("== DYNAMIC (real state changes on Home)");
        try
        {
            window.Navigate("home");
            await Task.Delay(2200);
            var hub = Hub.I;
            var peak = 0;
            var sw = Stopwatch.StartNew();
            hub.AddCommand(PowerAction.Sleep, DateTime.Now.AddHours(3));
            while (sw.ElapsedMilliseconds < 700) { peak = Math.Max(peak, HalfFaded(window.PageHost)); await Task.Delay(25); }
            await Task.Delay(500);
            W($"home: add a command -> peak half-faded elements while the new row rises = {peak}; settled half-faded = {HalfFaded(window.PageHost)}");
            var id = hub.Queue.Items.OrderBy(c => c.DueAt).Last().Id;
            hub.RemoveCommand(id);
            peak = 0; sw.Restart();
            while (sw.ElapsedMilliseconds < 500) { peak = Math.Max(peak, HalfFaded(window.PageHost)); await Task.Delay(25); }
            await Task.Delay(500);
            W($"home: command removed -> peak half-faded = {peak}; settled half-faded = {HalfFaded(window.PageHost)}; clocks {Clocks()}");
        }
        catch (Exception ex) { W("dynamic audit error: " + ex); }
    }

    /// <summary>Filmstrip of a page entrance (frames saved as PNG): proof that the page is not static on arrival.</summary>
    private static async Task Film(MainWindow window, Action<string> W, string folder, Action<FrameworkElement, string> save)
    {
        W("");
        W("== FILMSTRIP (entrance frames saved to the shots folder)");
        try
        {
            foreach (var page in new[] { "home", "settings", "system:power" })
            {
                window.Navigate(page == "home" ? "settings" : "home");
                await Task.Delay(1800);
                window.Navigate(page);
                var sw = Stopwatch.StartNew();
                foreach (var at in new[] { 120, 280, 480, 1000 })
                {
                    var wait = at - (int)sw.ElapsedMilliseconds;
                    if (wait > 0) await Task.Delay(wait);
                    var name = System.IO.Path.Combine(folder, $"film-{page.Replace(':', '-')}-{at}ms.png");
                    save(window.CaptureRoot, name);
                    W($"frame {name} at ~{sw.ElapsedMilliseconds} ms");
                }
            }
        }
        catch (Exception ex) { W("film error: " + ex); }
    }

    /// <summary>Flips real controls of the design-kit page and checks that each one starts an animation (clock count rises).</summary>
    private static async Task Toggles(MainWindow window, Action<string> W)
    {
        W("");
        W("== TOGGLE / SEGMENTED / SLIDER / CHIP FEEDBACK (design-kit page: a change must start an animation)");
        try
        {
            window.Navigate("kit");
            await Task.Delay(2200);
            var host = window.PageHost;
            var results = new List<string>();
            async Task Check(string name, Action change)
            {
                await Task.Delay(800); // let earlier motion end
                var before = Signature(host);
                change();
                await Task.Delay(40);
                var mid = Signature(host);
                await Task.Delay(900);
                var end = Signature(host);
                var moved = Differs(before, mid);
                var changed = Differs(before, end);
                results.Add($"{name}: {(moved ? "animates" : changed ? "INSTANT (no animation)" : "no visual change?")}");
            }
            foreach (var sw in All(host).OfType<Kit.Switch>().Take(2)) await Check("Switch", () => sw.IsChecked = sw.IsChecked != true);
            foreach (var seg in All(host).OfType<Segmented>().Take(2)) await Check("Segmented", () => seg.Select(seg.Index == 0 ? 1 : 0));
            foreach (var cb in All(host).OfType<CheckBox>().Where(c => c is not Kit.Switch).Take(2)) await Check("CheckBox", () => cb.IsChecked = cb.IsChecked != true);
            foreach (var rb in All(host).OfType<RadioButton>().Take(3)) await Check("RadioButton/Chip/Tile", () => rb.IsChecked = true);
            foreach (var sl in All(host).OfType<Slider>().Take(1)) await Check("Slider (value)", () => sl.Value = sl.Maximum * 0.7);
            foreach (var r in results) W(r);
        }
        catch (Exception ex)
        {
            W("toggle audit error: " + ex);
        }
    }

    // ---- main -----------------------------------------------------------------------------------------

    public static async Task Run(MainWindow window, string reportFile, string folder, Action<FrameworkElement, string> save)
    {
        var log = new StringBuilder();
        void W(string s) { log.AppendLine(s); }
        try
        {
            Directory.CreateDirectory(folder);
            var off = Environment.GetEnvironmentVariable("AUDIT_MOTION") == "off"; // run the same audit with animations switched off in Settings
            Hub.I.Settings.Animations = !off;
            await Task.Delay(3000);
            W($"Motion.Enabled={Motion.Enabled} StoreReflection={(Has(window, UIElement.MouseDownEvent) || true)}");
            using var self = Process.GetCurrentProcess();

            var only = Environment.GetEnvironmentVariable("AUDIT_ONLY") ?? "";
            bool Do(string part) => only.Length == 0 || only.Split(',').Contains(part);
            var pages = new[] { "home", "system", "comfort", "profiles", "gaming", "ping", "dns", "download", "files", "health", "tasks", "mic", "weather", "support", "update", "settings", "kit" };
            var keys = window.CaptureKeys.ToList();
            W("KEYS " + keys.Count);

            // 1. every key: entrance and exit
            if (Do("keys")) {
            W("");
            W("== PER KEY: key | exit(min stack opacity) | entrance(blocks, min opacity, stagger ms, done ms) | settled animated elements | clickables");
            string? previous = null;
            var allClicks = new Dictionary<string, List<ClickResult>>();
            foreach (var key in keys)
            {
                var topChanges = previous is null || previous.Split(':')[0] != key.Split(':')[0];
                window.Navigate(key);
                // sample the exit (page stack opacity) and the entrance (opacity of top-level blocks and of what is inside them)
                var sw = Stopwatch.StartNew();
                double minStack = 1;
                var firstSeen = new Dictionary<FrameworkElement, double>();
                double minBlock = 1, done = 0;
                var blocksSeen = 0;
                var lastChange = 0.0;
                while (sw.ElapsedMilliseconds < 1700)
                {
                    await Task.Delay(12);
                    var t = sw.Elapsed.TotalMilliseconds;
                    minStack = Math.Min(minStack, window.PageStackOpacity);
                    var blocks = window.PageBlocks();
                    blocksSeen = Math.Max(blocksSeen, blocks.Count);
                    foreach (var b in blocks)
                    {
                        var fading = b.Opacity < 0.999 || (b.RenderTransform is { } rt && rt != Transform.Identity && !rt.IsFrozen && (rt is TransformGroup || rt is ScaleTransform || rt is TranslateTransform));
                        if (b.Opacity < minBlock) minBlock = b.Opacity;
                        if (fading) lastChange = t;
                        if (b.Opacity > 0.03 && !firstSeen.ContainsKey(b)) firstSeen[b] = t;
                    }
                    if (t > 300 && lastChange > 0 && t - lastChange > 350) break;
                    if (t > 600 && lastChange == 0) break;
                }
                done = lastChange;
                var stagger = firstSeen.Count > 1 ? firstSeen.Values.Max() - firstSeen.Values.Min() : 0;
                await Task.Delay(900);
                var (ca, cf, ct) = Clocks();
                var clicks = await Clickables(key == keys[0] ? window.CaptureRoot : window.PageHost);
                allClicks[key] = clicks;
                var fault = $"clocks active={ca} filling={cf} total={ct}";
                W($"{key} | exit={(topChanges ? minStack.ToString("0.00") : "n/a(same page)")} | blocks={blocksSeen} minOpacity={minBlock:0.00} stagger={stagger:0}ms done={done:0}ms | {fault} | settledHalfFaded={HalfFaded(window.PageHost)} | clickables={clicks.Count} hoverNONE={clicks.Count(c => c.Hover == "NONE")} pressNONE={clicks.Count(c => c.Press == "NONE")}");
                previous = key;
            }

            // 2. clickable detail
            W("");
            W("== CLICKABLE DETAIL (only elements without hover or press feedback)");
            foreach (var (key, list) in allClicks)
            {
                foreach (var c in list.Where(c => c.Hover == "NONE" || c.Press == "NONE").GroupBy(c => (c.Kind, c.Name, c.Hover, c.Press)).Select(g => (g.Key, n: g.Count())))
                    W($"{key} :: {c.Key.Kind} {c.Key.Name} x{c.n}: hover={c.Key.Hover} press={c.Key.Press}");
            }
            W("");
            W("== CLICKABLE SUMMARY (hover kinds over all keys)");
            foreach (var g in allClicks.Values.SelectMany(l => l).GroupBy(c => c.Hover)) W($"hover {g.Key}: {g.Count()}");
            foreach (var g in allClicks.Values.SelectMany(l => l).GroupBy(c => c.Press)) W($"press {g.Key}: {g.Count()}");

            }
            if (Do("dialogs")) await Dialogs(window, W, folder, save);
            if (Do("toggles")) await Toggles(window, W);
            if (Do("dynamic")) await Dynamic(window, W);
            if (Do("film")) await Film(window, W, folder, save);
            // 3. idle CPU per top-level page
            if (Do("cpu")) {
            W("");
            W("== IDLE CPU (window open, nothing touched, 4 s) | page | % of all cores | animated elements");
            foreach (var page in pages)
            {
                window.Navigate(page);
                await Task.Delay(2600);
                self.Refresh();
                var cpu0 = self.TotalProcessorTime;
                var t0 = Stopwatch.StartNew();
                await Task.Delay(4000);
                self.Refresh();
                var cpu = (self.TotalProcessorTime - cpu0).TotalMilliseconds / (t0.Elapsed.TotalMilliseconds * Environment.ProcessorCount) * 100;
                W($"{page} | {cpu:0.00}% | clocks {Clocks()}");
            }

            }
            // 4. 200 page switches with motion on
            if (Do("switch")) {
            W("");
            W("== 200 PAGE SWITCHES");
            var cycle = new[] { "home", "settings", "ping", "tasks", "health", "gaming", "weather", "support" };
            window.Navigate("home");
            await Task.Delay(1500);
            long Mem() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); return GC.GetTotalMemory(true); }
            // warm-up so every page is built once
            foreach (var p in cycle) { window.Navigate(p); await Task.Delay(700); }
            var mem0 = Mem(); self.Refresh(); var priv0 = self.PrivateMemorySize64; var ws0 = self.WorkingSet64; var handles0 = self.HandleCount; var threads0 = self.Threads.Count;
            var cpuStart = self.TotalProcessorTime; var wall = Stopwatch.StartNew();
            var total = int.TryParse(Environment.GetEnvironmentVariable("AUDIT_SWITCHES"), out var tn) ? tn : 200;
            for (var i = 0; i < total; i++)
            {
                window.Navigate(cycle[i % cycle.Length]);
                await Task.Delay(i % 3 == 0 ? 60 : 320); // some switches interrupt the previous transition
                if ((i + 1) % 200 == 0 && i + 1 < total) { await Task.Delay(1200); var m = Mem(); self.Refresh(); W($"  after {i + 1}: managed {m / 1048576.0:0.0} MB, private {self.PrivateMemorySize64 >> 20} MB, working set {self.WorkingSet64 >> 20} MB, handles {self.HandleCount}, clocks {Clocks()}"); }
            }
            await Task.Delay(2500);
            self.Refresh();
            var cpuSwitch = (self.TotalProcessorTime - cpuStart).TotalMilliseconds / wall.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100;
            var mem1 = Mem(); self.Refresh();
            W($"managed heap {mem0 / 1024.0 / 1024:0.0} MB -> {mem1 / 1024.0 / 1024:0.0} MB; private {priv0 >> 20} -> {self.PrivateMemorySize64 >> 20} MB; working set {ws0 >> 20} -> {self.WorkingSet64 >> 20} MB; handles {handles0} -> {self.HandleCount}; threads {threads0} -> {self.Threads.Count}");
            W($"CPU while switching: {cpuSwitch:0.0}% of all cores over {wall.Elapsed.TotalSeconds:0} s");
            W($"clocks after 200 switches + 2.5 s (active, filling, total): {Clocks()}");
            var stuck = new List<string>();
            foreach (var d in All(window.CaptureRoot).OfType<FrameworkElement>())
                if (d.IsVisible && d.Opacity is > 0.01 and < 0.99 && !(d.GetType().Name is "Pill" or "Border" && d.Name == "Pill")) stuck.Add(d.GetType().Name + ":" + d.Opacity.ToString("0.00"));
            W($"visible elements stuck half-faded: {stuck.Count} {string.Join(", ", stuck.Take(8))}");
            self.Refresh();
            var cpu2 = self.TotalProcessorTime;
            var w2 = Stopwatch.StartNew();
            await Task.Delay(4000);
            self.Refresh();
            W($"idle CPU after the switching: {(self.TotalProcessorTime - cpu2).TotalMilliseconds / (w2.Elapsed.TotalMilliseconds * Environment.ProcessorCount) * 100:0.00}%");
            }
        }
        catch (Exception ex)
        {
            W("error: " + ex);
        }
        File.WriteAllText(reportFile, log.ToString());
        Application.Current.Shutdown();
    }
}
