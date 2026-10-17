using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>«خاموشی شرطی» in the Glass Depth design: a live status card, three steps (condition tiles, fine-tuning, action) and
/// the on/off button. Behaviour is unchanged: the draft is saved on every change, the preview runs through a scratch engine,
/// turning a rule on or off asks first.</summary>
public sealed class RulesPage : PageBase
{
    private readonly RuleEngine _preview = new();
    private bool _loading = true;

    // status
    private readonly IconBadge _statusBadge = new("bolt", "Accent", 48, 22, 14);
    private readonly TextBlock _statusTitle = new() { Style = St("H2"), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _statusText = new() { Style = St("Body"), Margin = new Thickness(0, 3, 0, 0) };
    private readonly ProgressBar _statusBar = new() { Style = St("Bar"), Margin = new Thickness(0, 12, 0, 2), Visibility = Visibility.Collapsed };
    private readonly TextBlock _statusClock = new() { FontSize = 26, FontWeight = FontWeights.Bold, Foreground = Res("Gold"), VerticalAlignment = VerticalAlignment.Center, FlowDirection = FlowDirection.LeftToRight };

    // step 1: condition tiles
    private readonly Grid _tiles = new() { Margin = new Thickness(-8, -8, -8, 0) };
    private readonly RadioButton _kindNetwork, _kindLoad, _kindIdle, _kindProcess;
    private readonly List<RadioButton> _tileList = [];
    private bool? _wide;

    // step 2: fine-tune
    private readonly StackPanel _networkPanel = new();
    private readonly StackPanel _loadPanel = new();
    private readonly StackPanel _processPanel = new();
    private readonly WrapPanel _directionChips = new();
    private readonly WrapPanel _speedChips = new();
    private readonly WrapPanel _loadChips = new();
    private readonly WrapPanel _processChips = new();
    private readonly WrapPanel _holdChips = new();
    private readonly WrapPanel _actionChips = new();
    private readonly TextBlock _holdLabel = PowerUi.Caption("", 4);
    private readonly TextBox _processBox = new() { Style = St("Input"), FlowDirection = FlowDirection.LeftToRight, MaxWidth = 340, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly CheckBox _waitCheck = new() { Style = St("Check"), Margin = new Thickness(0, 0, 8, 8), Content = L.T("اول صبر کن شروع بشه", "Wait for it to start first"), ToolTip = L.T("تا وقتی سرعت یک بار از آستانه بالاتر نرفته، شمارش شروع نمی‌شه.", "Counting only starts after the speed has been above the threshold once.") };
    private readonly CheckBox _recordCheck = new() { Style = St("Check"), Margin = new Thickness(0, 0, 8, 8), Content = L.T("موقع ضبط صفحه دست نگه دار", "Hold while screen recording") };
    private readonly CheckBox _gameCheck = new() { Style = St("Check"), Margin = new Thickness(0, 0, 8, 8), Content = L.T("موقع بازی دست نگه دار", "Hold while gaming") };

    private readonly StackPanel _editor = new();
    private readonly Button _toggle = new() { HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 280, Height = 48, Padding = new Thickness(26, 0, 26, 0), Margin = new Thickness(0, 4, 0, 0) };
    private readonly Icon _toggleIcon = new("bolt", 18, null) { Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBlock _toggleText = new() { FontWeight = FontWeights.Bold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };

    public override string PageTitle => L.T("خاموشی شرطی", "Conditional rules");
    public override string PageSubtitle => L.T("یه شرط تعیین کن؛ شب‌پا حواسش هست و وقتی برقرار شد کار رو انجام می‌ده.", "Set a condition; Shabpa keeps watch and acts when it is met.");

    private Rule Draft => Hub.State.RuleDraft;

    public RulesPage()
    {
        // ---- status card (always visible; becomes the live countdown once a rule is active) ---------------------
        var status = new Grid();
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status.ColumnDefinitions.Add(new ColumnDefinition());
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status.Children.Add(_statusBadge);
        var statusTexts = new StackPanel { Margin = new Thickness(18, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
        statusTexts.Children.Add(_statusTitle);
        statusTexts.Children.Add(_statusText);
        statusTexts.Children.Add(_statusBar);
        Grid.SetColumn(statusTexts, 1);
        status.Children.Add(statusTexts);
        Grid.SetColumn(_statusClock, 2);
        status.Children.Add(_statusClock);
        var statusCard = new GlassCard { Margin = new Thickness(0, 0, 0, 8), Child = status };
        PowerUi.HoverBadge(statusCard, _statusBadge);

        // ---- step 1: pick a condition --------------------------------------------------------------------------
        for (var i = 0; i < 4; i++) _tiles.ColumnDefinitions.Add(new ColumnDefinition());
        _kindNetwork = AddTile("dl", "HueB", L.T("دانلود یا آپلود تموم شد", "A download or upload finished"), L.T("سرعت شبکه زیر آستانه بمونه", "Network speed stays low"), "Network");
        _kindLoad = AddTile("cpu", "HueA", L.T("کار سنگین تموم شد", "Heavy work finished"), L.T("پردازنده و گرافیک آروم بگیرن", "CPU and GPU calm down"), "Load");
        _kindIdle = AddTile("clk", "HueC", L.T("سیستم بی‌استفاده موند", "The PC sat idle"), L.T("بدون موس و کیبورد", "No mouse or keyboard"), "Idle");
        _kindProcess = AddTile("mon", "HueD", L.T("یه برنامه بسته شد", "A program closed"), L.T("رندر، نصب بازی، بکاپ…", "Render, game install, backup…"), "ProcessExit");
        ApplyLayout(true);

        // ---- step 2: fine-tune ---------------------------------------------------------------------------------
        var (tuneCard, tune) = PowerUi.Card(null);
        tuneCard.Margin = new Thickness(0, 0, 0, 0);

        _networkPanel.Children.Add(PowerUi.Caption(L.T("وقتی کدوم تموم شد؟", "When which one finishes?")));
        _networkPanel.Children.Add(_directionChips);
        _networkPanel.Children.Add(PowerUi.Caption(L.T("وقتی سرعتش از این کمتر شد:", "When its speed drops below:"), 8));
        _networkPanel.Children.Add(_speedChips);
        tune.Children.Add(_networkPanel);

        _loadPanel.Children.Add(PowerUi.Caption(L.T("وقتی بار پردازنده و گرافیک هر دو از این کمتر شد:", "When CPU and GPU load are both below:")));
        _loadPanel.Children.Add(_loadChips);
        tune.Children.Add(_loadPanel);

        _processPanel.Children.Add(PowerUi.Caption(L.T("اسم برنامه (مثلاً steam یا blender.exe):", "Program name (for example steam or blender.exe):")));
        _processPanel.Children.Add(_processBox);
        _processPanel.Children.Add(PowerUi.Caption(L.T("یا از برنامه‌های باز انتخاب کن:", "Or pick one that is open:"), 14));
        _processPanel.Children.Add(_processChips);
        tune.Children.Add(_processPanel);

        tune.Children.Add(_holdLabel);
        tune.Children.Add(_holdChips);
        tune.Children.Add(PowerUi.Hairline(8, 16));
        var options = new WrapPanel();
        options.Children.Add(_waitCheck);
        options.Children.Add(_recordCheck);
        options.Children.Add(_gameCheck);
        tune.Children.Add(options);

        // ---- step 3: what happens then ---------------------------------------------------------------------------
        var (actionCard, action) = PowerUi.Card(null);
        actionCard.Margin = new Thickness(0, 0, 0, 20);
        action.Children.Add(_actionChips);

        _editor.Children.Add(Section(L.T("قدم ۱: شرط رو انتخاب کن", "Step 1: pick a condition")));
        _editor.Children.Add(_tiles);
        _editor.Children.Add(Section(L.T("قدم ۲: تنظیم دقیق", "Step 2: fine-tune")));
        _editor.Children.Add(tuneCard);
        _editor.Children.Add(Section(L.T("قدم ۳: بعدش چی کار کنه؟", "Step 3: what should happen then?")));
        _editor.Children.Add(actionCard);

        _toggle.Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { _toggleIcon, _toggleText } };
        _toggle.Click += OnToggle;

        var tip = new TextBlock
        {
            Text = L.T("هر قانون فقط یک بار اجرا می‌شه و بعدش خودش خاموش می‌شه. شرط باید پیوسته برقرار بمونه؛ اگه یه لحظه نقض بشه شمارش از صفر شروع می‌شه و قبل از اجرا هم فرصت لغو داری.",
                "A rule runs once and then turns itself off. The condition has to hold without a break; one interruption restarts the count, and you can still cancel before it runs."),
            Style = St("Tip"), Margin = new Thickness(4, 14, 4, 8),
        };

        var root = new StackPanel();
        root.Children.Add(statusCard);
        root.Children.Add(_editor);
        root.Children.Add(_toggle);
        root.Children.Add(tip);
        Content = root;

        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width > 800);

        if (PowerUi.Shot("rules:load")) Draft.Kind = RuleKind.Load;
        else if (PowerUi.Shot("rules:idle")) Draft.Kind = RuleKind.Idle;
        else if (PowerUi.Shot("rules:process")) Draft.Kind = RuleKind.ProcessExit;
        LoadDraft();
        _loading = false;
        if (PowerUi.Shot("rules:active"))
            Dispatcher.BeginInvoke(() => Hub.ActivateRule(new Rule { Kind = RuleKind.Network, WaitForStart = false, HoldMinutes = 5, Action = PowerAction.Sleep }));
    }

    private static TextBlock Section(string text) => new() { Text = text, Style = St("Section") };

    private RadioButton AddTile(string icon, string hue, string title, string sub, string tag)
    {
        var tile = PowerUi.Tile(icon, hue, title, sub, "ruleKind", tag);
        tile.Checked += OnKind;
        _tileList.Add(tile);
        _tiles.Children.Add(tile);
        return tile;
    }

    private void ApplyLayout(bool wide)
    {
        if (_wide == wide) return;
        _wide = wide;
        _tiles.RowDefinitions.Clear();
        if (!wide) for (var r = 0; r < 2; r++) _tiles.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < _tileList.Count; i++)
        {
            if (wide) PowerUi.Place(_tileList[i], 0, i);
            else PowerUi.Place(_tileList[i], i / 2, (i % 2) * 2, 2);
        }
    }

    private static string Mb(double value) => L.T($"{Fa.Mbit(value)} مگابیت", $"{Fa.Mbit(value)} Mbit");

    private void LoadDraft()
    {
        var d = Draft;
        (d.Kind switch
        {
            RuleKind.Load => _kindLoad,
            RuleKind.Idle => _kindIdle,
            RuleKind.ProcessExit => _kindProcess,
            _ => _kindNetwork,
        }).IsChecked = true;

        Chips(_directionChips, [(false, L.T("دانلود", "Download")), (true, L.T("آپلود", "Upload"))], d.Upload, v => { d.Upload = v; Changed(); });
        Chips(_speedChips, new[] { 0.5, 1.0, 2.0, 5.0, 10.0 }.Select(v => (v, Mb(v))), d.ThresholdMbps, v => { d.ThresholdMbps = v; Changed(); });
        Chips(_loadChips, new[] { 10, 15, 25, 40 }.Select(v => (v, Fa.Digits(v.ToString()) + L.T("٪", "%"))), d.LoadPercent, v => { d.LoadPercent = v; Changed(); });
        Chips(_actionChips,
            [
                (PowerAction.Sleep, L.T("بخوابه", "Sleep")), (PowerAction.Hibernate, L.T("هایبرنیت بشه", "Hibernate")),
                (PowerAction.Shutdown, L.T("خاموش بشه", "Shut down")), (PowerAction.Restart, L.T("ری‌استارت بشه", "Restart")),
            ],
            d.Action, v => { d.Action = v; Changed(); });
        _processBox.Text = d.ProcessName;
        _waitCheck.IsChecked = d.WaitForStart;
        _recordCheck.IsChecked = d.ExceptRecording;
        _gameCheck.IsChecked = d.ExceptGame;
        // wired after the initial values are in, so loading the draft does not save it back
        _processBox.TextChanged += OnProcessText;
        _waitCheck.Click += OnOption;
        _recordCheck.Click += OnOption;
        _gameCheck.Click += OnOption;
        ShowKind();
    }

    private void ShowKind()
    {
        var d = Draft;
        PowerUi.Toggle(_networkPanel, d.Kind == RuleKind.Network);
        PowerUi.Toggle(_loadPanel, d.Kind == RuleKind.Load);
        PowerUi.Toggle(_processPanel, d.Kind == RuleKind.ProcessExit);
        PowerUi.Toggle(_waitCheck, d.Kind == RuleKind.Network);

        _holdLabel.Text = d.Kind switch
        {
            RuleKind.Idle => L.T("وقتی این مدت کسی به سیستم دست نزد:", "When nobody touched the PC for:"),
            RuleKind.ProcessExit => L.T("و این مدت بعد از بسته‌شدن صبر کن:", "And wait this long after it closes:"),
            _ => L.T("و این مدت پیوسته همین‌طور موند:", "And it stayed that way for:"),
        };
        // the hold row follows the panel above it; with no panel above (idle) it starts the card
        _holdLabel.Margin = new Thickness(0, d.Kind == RuleKind.Idle ? 0 : 16, 0, 8);
        int[] options = d.Kind switch
        {
            RuleKind.Idle => [5, 10, 15, 30, 60, 120],
            RuleKind.ProcessExit => [0, 1, 2, 5, 10],
            _ => [1, 2, 5, 10, 30],
        };
        if (!options.Contains(d.HoldMinutes)) d.HoldMinutes = d.Kind == RuleKind.Idle ? 30 : d.Kind == RuleKind.ProcessExit ? 1 : 5;
        Chips(_holdChips, options.Select(m => (m, m == 0 ? L.T("بلافاصله", "Right away") : Fa.Duration(TimeSpan.FromMinutes(m)))), d.HoldMinutes,
            v => { d.HoldMinutes = v; Changed(); });

        if (d.Kind == RuleKind.ProcessExit) LoadProcesses();
    }

    /// <summary>Offers the apps that currently have a window, since those are what people mean by "a program".</summary>
    private void LoadProcesses()
    {
        _processChips.Children.Clear();
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
                _processChips.Children.Clear();
                var n = 0;
                foreach (var name in names.Take(14))
                {
                    var button = new Button { Style = St("Btn"), Content = name, Margin = new Thickness(0, 0, 8, 8), FlowDirection = FlowDirection.LeftToRight, FontWeight = FontWeights.Medium, Padding = new Thickness(16, 7, 16, 7) };
                    button.Click += (_, _) => { _processBox.Text = name; Motion.Pop(_processBox); };
                    _processChips.Children.Add(button);
                    PowerUi.Appear(button, n++);
                }
            });
        });
    }

    private void OnKind(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Draft.Kind = Enum.Parse<RuleKind>((string)((RadioButton)sender).Tag);
        PowerUi.PopTile((RadioButton)sender);
        ShowKind();
        Changed();
    }

    private void OnProcessText(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        Draft.ProcessName = _processBox.Text.Trim();
        Changed();
    }

    private void OnOption(object sender, RoutedEventArgs e)
    {
        Draft.WaitForStart = _waitCheck.IsChecked == true;
        Draft.ExceptRecording = _recordCheck.IsChecked == true;
        Draft.ExceptGame = _gameCheck.IsChecked == true;
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
            Motion.Pop(_toggle);
            Hub.DeactivateRule();
            return;
        }
        if (!Ask(L.T("این قانون فعال بشه؟", "Turn this rule on?"),
                L.T($"از همین لحظه شب‌پا شرط رو زیر نظر می‌گیره و وقتی برقرار شد، {Fa.ActionSentence(Draft.Action)} (با شمارش معکوس قابل لغو).",
                    $"From this moment Shabpa watches the condition; once it is met, {Fa.ActionSentence(Draft.Action)} (with a cancellable countdown)."),
                L.T("تایید و فعال کن", "Confirm and turn on"),
                detail: L.T("شروع نظارت: همین الان", "Watching starts: right now")))
            return;
        Motion.Pop(_toggle);
        Hub.ActivateRule(Draft);
    }

    public override void Refresh() => UpdateStatus(Hub.Snapshot);

    public override void OnTick(SensorSnapshot s) => UpdateStatus(s);

    private void SetToggle(string text, string icon, bool danger)
    {
        _toggleText.Text = text;
        _toggleIcon.IconName = icon;
        var style = St(danger ? "BtnDanger" : "BtnPrimary");
        if (!ReferenceEquals(_toggle.Style, style)) _toggle.Style = style;
    }

    private void UpdateStatus(SensorSnapshot s)
    {
        var active = Hub.Engine.Rule;
        if (_editor.IsEnabled != (active is null))
        {
            _editor.IsEnabled = active is null;
            if (IsLoaded) Motion.Pop(_statusBadge);
        }

        if (active is not null)
        {
            var status = Hub.Engine.Status;
            SetToggle(L.T("غیرفعال کردن قانون", "Turn the rule off"), "x", true);
            _toggle.IsEnabled = true;

            _statusTitle.Text = L.T($"قانون فعاله؛ وقتی شرط برقرار شد {Fa.ActionSentence(active.Action)}", $"The rule is active; once the condition is met {Fa.ActionSentence(active.Action)}");
            _statusText.Text = status.Text;
            _statusBar.Visibility = Visibility.Visible;
            _statusBar.Value = status.Progress;
            _statusClock.Text = status.State == RuleState.Counting ? Fa.Countdown(status.Remaining) : "";
            SetTint(status.State switch
            {
                RuleState.Blocked => "Red",
                RuleState.Counting => "Gold",
                _ => "Green",
            });
            return;
        }

        SetToggle(L.T("فعال کردن این قانون", "Turn this rule on"), "bolt", false);
        _statusBar.Visibility = Visibility.Collapsed;
        _statusClock.Text = "";
        SetTint("Accent");
        _statusTitle.Text = L.T("وضعیت همین لحظه", "Right now");

        var draft = Draft;
        if (draft.Kind == RuleKind.ProcessExit && RuleEngine.NormalizeProcess(draft.ProcessName).Length == 0)
        {
            _statusText.Text = L.T("اسم برنامه رو وارد کن.", "Enter the program name.");
            _toggle.IsEnabled = false;
            return;
        }
        _toggle.IsEnabled = true;

        // Run the draft through a scratch engine so the preview uses exactly the real wording and logic.
        _preview.Activate(draft);
        var preview = _preview.Evaluate(s, s.Time);
        _statusText.Text = preview.State switch
        {
            RuleState.Waiting or RuleState.Blocked => preview.Text,
            RuleState.NotMet => preview.Text + " " + L.T("اگه الان فعالش کنی، منتظر می‌مونه.", "If you turn it on now, it will wait."),
            _ => preview.Text + " " + L.T("اگه الان فعالش کنی، شمارش شروع می‌شه.", "If you turn it on now, the count starts."),
        };
    }

    private void SetTint(string tint)
    {
        _statusBadge.Tint(tint);
        _statusBar.Foreground = Res(tint);
    }
}
