using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;

namespace Shabpa.Pages;

public partial class RulesPage : PageBase
{
    private readonly RuleEngine _preview = new();
    private bool _loading = true;

    public override string PageTitle => L.T("خاموشی شرطی", "Conditional rules");
    public override string PageSubtitle => L.T("یه شرط تعیین کن؛ شب‌پا حواسش هست و وقتی برقرار شد کار رو انجام می‌ده.", "Set a condition; Shabpa keeps watch and acts when it is met.");

    private Rule Draft => Hub.State.RuleDraft;

    public RulesPage()
    {
        InitializeComponent();
        LoadDraft();
        _loading = false;
    }

    private static string Mb(double value) => L.T($"{Fa.Mbit(value)} مگابیت", $"{Fa.Mbit(value)} Mbit");

    private void LoadDraft()
    {
        var d = Draft;
        (d.Kind switch
        {
            RuleKind.Load => KindLoad,
            RuleKind.Idle => KindIdle,
            RuleKind.ProcessExit => KindProcess,
            _ => KindNetwork,
        }).IsChecked = true;

        Chips(DirectionChips, [(false, L.T("دانلود", "Download")), (true, L.T("آپلود", "Upload"))], d.Upload, v => { d.Upload = v; Changed(); });
        Chips(SpeedChips, new[] { 0.5, 1.0, 2.0, 5.0, 10.0 }.Select(v => (v, Mb(v))), d.ThresholdMbps, v => { d.ThresholdMbps = v; Changed(); });
        Chips(LoadChips, new[] { 10, 15, 25, 40 }.Select(v => (v, Fa.Digits(v.ToString()) + L.T("٪", "%"))), d.LoadPercent, v => { d.LoadPercent = v; Changed(); });
        Chips(ActionChips,
            [
                (PowerAction.Sleep, L.T("بخوابه", "Sleep")), (PowerAction.Hibernate, L.T("هایبرنیت بشه", "Hibernate")),
                (PowerAction.Shutdown, L.T("خاموش بشه", "Shut down")), (PowerAction.Restart, L.T("ری‌استارت بشه", "Restart")),
            ],
            d.Action, v => { d.Action = v; Changed(); });
        ProcessBox.Text = d.ProcessName;
        WaitCheck.IsChecked = d.WaitForStart;
        RecordCheck.IsChecked = d.ExceptRecording;
        GameCheck.IsChecked = d.ExceptGame;
        ShowKind();
    }

    private void ShowKind()
    {
        var d = Draft;
        NetworkPanel.Visibility = d.Kind == RuleKind.Network ? Visibility.Visible : Visibility.Collapsed;
        LoadPanel.Visibility = d.Kind == RuleKind.Load ? Visibility.Visible : Visibility.Collapsed;
        ProcessPanel.Visibility = d.Kind == RuleKind.ProcessExit ? Visibility.Visible : Visibility.Collapsed;
        WaitCheck.Visibility = d.Kind == RuleKind.Network ? Visibility.Visible : Visibility.Collapsed;

        HoldLabel.Text = d.Kind switch
        {
            RuleKind.Idle => L.T("وقتی این مدت کسی به سیستم دست نزد:", "When nobody touched the PC for:"),
            RuleKind.ProcessExit => L.T("و این مدت بعد از بسته‌شدن صبر کن:", "And wait this long after it closes:"),
            _ => L.T("و این مدت پیوسته همین‌طور موند:", "And it stayed that way for:"),
        };
        int[] options = d.Kind switch
        {
            RuleKind.Idle => [5, 10, 15, 30, 60, 120],
            RuleKind.ProcessExit => [0, 1, 2, 5, 10],
            _ => [1, 2, 5, 10, 30],
        };
        if (!options.Contains(d.HoldMinutes)) d.HoldMinutes = d.Kind == RuleKind.Idle ? 30 : d.Kind == RuleKind.ProcessExit ? 1 : 5;
        Chips(HoldChips, options.Select(m => (m, m == 0 ? L.T("بلافاصله", "Right away") : Fa.Duration(TimeSpan.FromMinutes(m)))), d.HoldMinutes,
            v => { d.HoldMinutes = v; Changed(); });

        if (d.Kind == RuleKind.ProcessExit) LoadProcesses();
    }

    /// <summary>Offers the apps that currently have a window, since those are what people mean by "a program".</summary>
    private void LoadProcesses()
    {
        ProcessChips.Children.Clear();
        Task.Run(() =>
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.MainWindowHandle != IntPtr.Zero && p.MainWindowTitle.Length > 0 && p.Id != Environment.ProcessId)
                        names.Add(p.ProcessName);
                }
                catch { /* access denied or exited */ }
                finally { p.Dispose(); }
            }
            names.ExceptWith(["explorer", "TextInputHost", "ApplicationFrameHost", "SystemSettings"]);
            Dispatcher.BeginInvoke(() =>
            {
                ProcessChips.Children.Clear();
                foreach (var name in names.Take(14))
                {
                    var button = new Button { Style = St("Btn"), Content = name, Margin = new Thickness(0, 0, 8, 8), FlowDirection = FlowDirection.LeftToRight, FontWeight = FontWeights.Medium };
                    button.Click += (_, _) => ProcessBox.Text = name;
                    ProcessChips.Children.Add(button);
                }
            });
        });
    }

    private void OnKind(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Draft.Kind = Enum.Parse<RuleKind>((string)((RadioButton)sender).Tag);
        ShowKind();
        Changed();
    }

    private void OnProcessText(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        Draft.ProcessName = ProcessBox.Text.Trim();
        Changed();
    }

    private void OnOption(object sender, RoutedEventArgs e)
    {
        Draft.WaitForStart = WaitCheck.IsChecked == true;
        Draft.ExceptRecording = RecordCheck.IsChecked == true;
        Draft.ExceptGame = GameCheck.IsChecked == true;
        Changed();
    }

    private void Changed()
    {
        if (_loading) return;
        Hub.Store.Save();
        UpdateStatus(Hub.Snapshot);
    }

    private void OnToggle(object sender, RoutedEventArgs e)
    {
        if (Hub.Engine.Rule is not null)
        {
            if (!Ask(L.T("این قانون غیرفعال بشه؟", "Turn this rule off?"),
                    L.T("دیگه چیزی زیر نظر گرفته نمی‌شه و سیستم به خاطر این قانون چیزی اجرا نمی‌کنه.", "Nothing is watched any more and the PC will not act because of this rule."),
                    L.T("غیرفعال کن", "Turn off"), danger: true, detail: Fa.ActionName(Hub.Engine.Rule.Action)))
                return;
            Hub.DeactivateRule();
            return;
        }
        if (!Ask(L.T("این قانون فعال بشه؟", "Turn this rule on?"),
                L.T($"از همین لحظه شب‌پا شرط رو زیر نظر می‌گیره و وقتی برقرار شد، {Fa.ActionSentence(Draft.Action)} (با شمارش معکوس قابل لغو).",
                    $"From this moment Shabpa watches the condition; once it is met, {Fa.ActionSentence(Draft.Action)} (with a cancellable countdown)."),
                L.T("تایید و فعال کن", "Confirm and turn on"),
                detail: L.T("شروع نظارت: همین الان", "Watching starts: right now")))
            return;
        Hub.ActivateRule(Draft);
    }

    public override void Refresh() => UpdateStatus(Hub.Snapshot);

    public override void OnTick(SensorSnapshot s) => UpdateStatus(s);

    private void UpdateStatus(SensorSnapshot s)
    {
        var active = Hub.Engine.Rule;
        Editor.IsEnabled = active is null;

        if (active is not null)
        {
            var status = Hub.Engine.Status;
            Toggle.Content = L.T("غیرفعال کردن قانون", "Turn the rule off");
            Toggle.Style = St("BtnDanger");
            Toggle.Padding = new Thickness(20, 14, 20, 14);
            Toggle.FontSize = 15;
            Toggle.HorizontalAlignment = HorizontalAlignment.Stretch;
            Toggle.IsEnabled = true;

            StatusTitle.Text = L.T($"قانون فعاله؛ وقتی شرط برقرار شد {Fa.ActionSentence(active.Action)}", $"The rule is active; once the condition is met {Fa.ActionSentence(active.Action)}");
            StatusText.Text = status.Text;
            StatusBar.Visibility = Visibility.Visible;
            StatusBar.Value = status.Progress;
            StatusClock.Text = status.State == RuleState.Counting ? Fa.Countdown(status.Remaining) : "";
            SetTint(status.State switch
            {
                RuleState.Blocked => "Red",
                RuleState.Counting => "Gold",
                _ => "Green",
            });
            return;
        }

        Toggle.Content = L.T("فعال کردن این قانون", "Turn this rule on");
        Toggle.Style = St("BtnBig");
        StatusBar.Visibility = Visibility.Collapsed;
        StatusClock.Text = "";
        SetTint("Accent");
        StatusTitle.Text = L.T("وضعیت همین لحظه", "Right now");

        var draft = Draft;
        if (draft.Kind == RuleKind.ProcessExit && RuleEngine.NormalizeProcess(draft.ProcessName).Length == 0)
        {
            StatusText.Text = L.T("اسم برنامه رو وارد کن.", "Enter the program name.");
            Toggle.IsEnabled = false;
            return;
        }
        Toggle.IsEnabled = true;

        // Run the draft through a scratch engine so the preview uses exactly the real wording and logic.
        _preview.Activate(draft);
        var preview = _preview.Evaluate(s, s.Time);
        StatusText.Text = preview.State switch
        {
            RuleState.Waiting or RuleState.Blocked => preview.Text,
            RuleState.NotMet => preview.Text + " " + L.T("اگه الان فعالش کنی، منتظر می‌مونه.", "If you turn it on now, it will wait."),
            _ => preview.Text + " " + L.T("اگه الان فعالش کنی، شمارش شروع می‌شه.", "If you turn it on now, the count starts."),
        };
    }

    private void SetTint(string tint)
    {
        StatusBadge.Background = Res(tint + "Soft");
        StatusIcon.Foreground = Res(tint);
        StatusBar.Foreground = Res(tint);
    }
}
