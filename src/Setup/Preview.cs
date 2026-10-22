#if DEBUG || SHABPA_PREVIEW
// Test-only: shows the installer window in any state WITHOUT installing anything and renders it to PNG. Compiled into
// Debug builds only (or with -p:DefineConstants=SHABPA_PREVIEW); the release installer does not contain it.
//
//   ShabpaSetup.exe --preview <state> [--lang fa|en] [--out dir] [--progress 0.4] [--fail] [--script "wait 900;shot a;main;wait 500;shot b;exit"]
//
// states: welcome update runtime uninstall | working download removing | done removed | failed
// script: wait <ms> | shot <name> | main (the Install/Update/Uninstall button, runs a SIMULATED run) | lang | toggle desktop|autostart|launch |
//         hover main|close | unhover main|close | exit
// OnMain never touches the system while a preview spec is present: the real work is replaced by Simulate().
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Shabpa.Setup;

internal sealed class PreviewSpec
{
    public string State = "welcome";
    public string? Lang;
    public double Progress = 0.4;
    public string Out = ".";
    public string Script = "";
    public bool Fail;

    public bool Uninstall => State is "uninstall" or "removing" or "removed";

    public static PreviewSpec? Parse(string[] args)
    {
        var list = args.ToList();
        var at = list.FindIndex(a => a.Equals("--preview", StringComparison.OrdinalIgnoreCase));
        if (at < 0) return null;
        string? Value(string name)
        {
            var i = list.FindIndex(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < list.Count ? list[i + 1] : null;
        }
        var spec = new PreviewSpec { State = (at + 1 < list.Count ? list[at + 1] : "welcome").ToLowerInvariant() };
        spec.Lang = Value("--lang");
        spec.Out = Value("--out") ?? ".";
        spec.Script = Value("--script") ?? "";
        spec.Fail = list.Any(a => a.Equals("--fail", StringComparison.OrdinalIgnoreCase));
        if (double.TryParse(Value("--progress"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p)) spec.Progress = p;
        return spec;
    }
}

public partial class SetupWindow
{
    private PreviewSpec? _preview;

    internal SetupWindow(bool uninstall, PreviewSpec preview) : this(uninstall)
    {
        _preview = preview;
        _installed = null;
        _runtimeReady = true;
        _version = "3.10.0";
        _stage = Stage.Ready;
        _error = "";
        if (preview.Lang is "en" or "fa") _en = preview.Lang == "en";
        switch (preview.State)
        {
            case "update": _installed = "3.3.0"; break;
            case "runtime": _runtimeReady = false; break;
            case "working":
            case "removing":
                SetWorking(preview.Progress, _uninstall ? T("در حال حذف فایل‌ها…", "Removing files…") : T("کپی فایل‌ها…", "Copying files…"));
                break;
            case "download":
                _runtimeReady = false;
                SetWorking(preview.Progress, T("دانلود ‎.NET 10 از سایت مایکروسافت (حدود ۶۰ مگابایت)…", "Downloading .NET 10 from Microsoft (about 60 MB)…"));
                break;
            case "done":
            case "removed":
                _stage = Stage.Done;
                break;
            case "failed":
                _stage = Stage.Failed;
                _error = T("دانلود ‎.NET از سایت مایکروسافت انجام نشد. اینترنت یا فیلترشکن رو چک کن و دوباره امتحان کن.\n‎The remote name could not be resolved: 'aka.ms'‎",
                    "Could not download .NET from Microsoft (The remote name could not be resolved: 'aka.ms'). Check the internet connection and try again.");
                break;
        }
        Render();
    }

    private void SetWorking(double fraction, string status)
    {
        _stage = Stage.Working;
        _progress = fraction;
        RingCtl.Fraction = fraction;
        Progress.Value = fraction;
        StatusText.Text = status;
    }

    internal void ShowPreview()
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -12000;
        Top = 40;
        ShowActivated = false;
        ShowInTaskbar = false;
        Show();
        RunScript();
    }

    private async Task Simulate(IProgress<double> progress)
    {
        void Report(double v) => progress.Report(v);
        Report(0.02);
        await Task.Delay(500);
        if (_uninstall)
        {
            StatusText.Text = T("حذف فایل‌ها و میان‌برها…", "Removing files and shortcuts…");
            for (var i = 1; i <= 8; i++) { Report(0.1 + i * 0.11); await Task.Delay(110); }
        }
        else
        {
            if (!_runtimeReady)
            {
                StatusText.Text = T("دانلود ‎.NET 10 از سایت مایکروسافت (حدود ۶۰ مگابایت)…", "Downloading .NET 10 from Microsoft (about 60 MB)…");
                for (var i = 1; i <= 20; i++) { Report(0.05 + 0.6 * i / 20.0); await Task.Delay(90); }
            }
            StatusText.Text = T("کپی فایل‌ها…", "Copying files…");
            for (var i = 1; i <= 14; i++) { Report(0.65 + 0.3 * i / 14.0); await Task.Delay(80); }
            StatusText.Text = T("تنظیم اجرا همراه ویندوز…", "Setting up start with Windows…");
            await Task.Delay(350);
        }
        Report(1);
        if (_preview!.Fail) throw new InvalidOperationException("simulated failure for the preview");
    }

    private async void RunScript()
    {
        var p = _preview!;
        Directory.CreateDirectory(p.Out);
        try
        {
            var script = p.Script.Length > 0 ? p.Script : $"wait 1500;shot {p.State};exit";
            foreach (var raw in script.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = raw.Trim().Split(' ');
                switch (parts[0])
                {
                    case "wait": await Task.Delay(int.Parse(parts[1])); break;
                    case "shot": Shot(Path.Combine(p.Out, parts[1] + ".png")); break;
                    case "main": if (_stage == Stage.Ready) OnMain(this, new RoutedEventArgs()); break;
                    case "lang": _language.Select(_en ? 0 : 1); break;
                    case "toggle":
                        var sw = parts[1] switch { "desktop" => DesktopCheck, "autostart" => AutostartCheck, "launch" => LaunchCheck, _ => RemoveDataCheck };
                        ((IToggleProvider)new ToggleButtonAutomationPeer(sw).GetPattern(PatternInterface.Toggle)).Toggle();
                        break;
                    case "hover": Hover(parts[1], true); break;
                    case "unhover": Hover(parts[1], false); break;
                    case "dump": File.AppendAllText(System.IO.Path.Combine(p.Out, "dump.txt"), $"grid {MainGrid.ActualWidth}x{MainGrid.ActualHeight} head {HeadText.ActualWidth}x{HeadText.ActualHeight} note {NoteText.ActualWidth}x{NoteText.ActualHeight} mark {Mark.ActualWidth}x{Mark.ActualHeight} window {ActualWidth}x{ActualHeight} stage {StageHost.ActualHeight} rows {MainGrid.RowDefinitions[0].ActualHeight}/{MainGrid.RowDefinitions[1].ActualHeight} main {MainCard.ActualHeight} opts {OptionsCard.ActualHeight} btn {ButtonsRow.ActualHeight} hdr {Header.ActualHeight}\r\n"); break;
                    case "exit": goto done;
                }
            }
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(p.Out, "preview-error.txt"), ex.ToString());
        }
    done:
        Application.Current.Shutdown();
    }

    private void Hover(string which, bool on)
    {
        var b = which == "close" ? CloseButton : MainButton;
        b.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = on ? Mouse.MouseEnterEvent : Mouse.MouseLeaveEvent });
    }

    private void Shot(string file)
    {
        Root.UpdateLayout();
        const double scale = 2;
        var w = (int)Math.Ceiling(Root.ActualWidth);
        var h = (int)Math.Ceiling(Root.ActualHeight);
        var bmp = new RenderTargetBitmap((int)(w * scale), (int)(h * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // FlowDirection is part of the element's own rendering only when it differs from its parent, so paint through a brush
            if (Root.FlowDirection == FlowDirection.RightToLeft) dc.PushTransform(new ScaleTransform(-1, 1, w / 2.0, 0));
            dc.DrawRectangle(new VisualBrush(Root) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, w, h));
        }
        bmp.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var stream = File.Create(file);
        encoder.Save(stream);
    }
}
#endif
