using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;
using Shabpa.Kit;
using Switch = Shabpa.Kit.Switch;

namespace Shabpa.Pages;

/// <summary>«میکروفون»: level and mute, a short noise test with plain advice, the noise suppressors that exist on this
/// PC, and which programs used the microphone. Audio is only listened to while a test runs, only to measure loudness,
/// and is never saved or sent.</summary>
public sealed class MicrophonePage : PageBase
{
    private readonly TextBlock _device = new() { Style = St("H2"), FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _deviceState = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private Slider _level = null!;
    private readonly Switch _mute = new();
    private readonly ProgressBar _meter = new() { Style = St("Bar"), Minimum = 0, Maximum = 1, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock _meterText = new() { FontSize = 30, FontWeight = FontWeights.Bold, FlowDirection = FlowDirection.LeftToRight, Text = "— dB", LineHeight = 36, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
    private readonly TextBlock _result = new() { Style = St("Body"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    private readonly Button _noise = new() { Style = St("BtnPrimary"), Height = 40, Padding = new Thickness(18, 0, 18, 0), Margin = new Thickness(0, 0, 0, 8) };
    private readonly Button _speech = new() { Style = St("Btn"), Height = 40, Padding = new Thickness(18, 0, 18, 0), Margin = new Thickness(0, 0, 0, 8) };
    private readonly StackPanel _tools = new();
    private readonly StackPanel _apps = new();
    private readonly TextBlock _access = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private bool _loading;
    private bool _testing;
    private readonly StackPanel _voiceIn = new();
    private readonly StackPanel _voiceOut = new();
    private readonly WrapPanel _voiceStrength = new();
    private readonly Button _voiceToggle = new() { Style = St("BtnPrimary"), Height = 42, Padding = new Thickness(22, 0, 22, 0), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 16, 0, 0) };
    private readonly ProgressBar _voiceIn1 = new() { Style = St("Bar"), Minimum = 0, Maximum = 1, Foreground = Res("HueC"), Margin = new Thickness(0, 6, 0, 0) };
    private readonly ProgressBar _voiceOut1 = new() { Style = St("Bar"), Minimum = 0, Maximum = 1, Foreground = Res("HueB"), Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _voiceNote = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    private readonly Switch _voiceAuto = new();
    private readonly System.Windows.Threading.DispatcherTimer _voiceTimer = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private readonly StatusBadge _badge = new(L.T("نویزگیر خاموشه", "Noise suppression off"), "Gold", "mic");
    private bool _shownRunning;
    private bool? _badgeShown;
    private string _toolsSig = "", _appsSig = "";
    private List<AudioDevice> _inputs = [], _outputs = [];

    public override string PageTitle => L.T("میکروفون", "Microphone");
    public override string PageSubtitle => L.T("سطح و قطع/وصل، یه تست نویز با راهنمای ساده، نویزگیرهای موجود و برنامه‌هایی که از میکروفون استفاده کردن.",
        "Level and mute, a short noise test with plain advice, the noise suppressors available, and which programs used the microphone.");
    public override UIElement? HeaderAccessory => _badge;

    public MicrophonePage()
    {
        var root = new NetBlocks(900, 4);

        // ---- live noise suppression ---------------------------------------------------------------
        var (voiceCard, voice) = NetParts.Card("ecg", "HueA", L.T("نویزگیر زنده‌ی شب‌پا", "Shabpa live noise suppression"),
            L.T("صدای میکروفون رو همین الان با شبکه‌ی عصبی RNNoise تمیز می‌کنه: خش‌خش، وزوز، فن و صدای کیبورد کم می‌شه و صدای تو می‌مونه. همه‌چیز توی حافظه و روی همین کامپیوتر انجام می‌شه؛ صدا ذخیره یا فرستاده نمی‌شه.",
                "Cleans the microphone live with the RNNoise neural network: hiss, hum, fans and keyboard clatter go down and your voice stays. It all happens in memory on this PC; audio is never saved or sent."));
        voice.Children.Add(_voiceToggle);
        voice.Children.Add(Ui2.Label(L.T("قبل (میکروفون)", "Before (microphone)"), 16, 0));
        voice.Children.Add(_voiceIn1);
        voice.Children.Add(Ui2.Label(L.T("بعد (تمیز‌شده)", "After (cleaned)"), 10, 0));
        voice.Children.Add(_voiceOut1);
        voice.Children.Add(_voiceNote);
        voice.Children.Add(new System.Windows.Controls.Border { Height = 1, Background = Res("Line"), Margin = new Thickness(0, 16, 0, 4) });
        voice.Children.Add(Ui2.Label(L.T("میکروفون", "Microphone"), 10, 6));
        voice.Children.Add(_voiceIn);
        voice.Children.Add(Ui2.Label(L.T("صدای تمیز کجا بره؟", "Where should the clean sound go?"), 10, 6));
        voice.Children.Add(_voiceOut);
        _voiceStrength.VerticalAlignment = VerticalAlignment.Center;
        voice.Children.Add(K.SettingRow(L.T("شدت", "Strength"), _voiceStrength));
        voice.Children.Add(K.SettingRow(L.T("با هر بار باز شدن شب‌پا خودکار روشن بشه", "Switch on by itself whenever Shabpa starts"), _voiceAuto));
        var cableTip = K.Text(L.T("برای اینکه برنامه‌های دیگه (دیسکورد، تیم‌اسپیک، بازی…) صدای تمیز رو بشنون، یه کابل صوتی مجازی (مثل VB-Cable که رایگانه) لازمه: «صدای تمیز کجا بره؟» رو روی CABLE Input بذار و توی اون برنامه میکروفون رو CABLE Output انتخاب کن. شب‌پا خودش هیچ درایوری نصب نمی‌کنه. اگه فقط می‌خوای صدای خودت رو بشنوی، یه هدفون انتخاب کن (با بلندگو صدا برمی‌گرده توی میکروفون و زوزه می‌کشه).",
            "For other programs (Discord, TeamSpeak, games…) to hear the clean sound you need a virtual audio cable (VB-Cable is free): set “Where should the clean sound go?” to CABLE Input and pick CABLE Output as the microphone in that program. Shabpa installs no driver itself. To just hear yourself, choose headphones (speakers feed back into the microphone and howl)."), "Tip");
        cableTip.TextWrapping = TextWrapping.Wrap;
        cableTip.Margin = new Thickness(0, 12, 0, 0);
        voice.Children.Add(cableTip);
        voice.Children.Add(_cableHelp);
        _cableHelp.Margin = new Thickness(0, 10, 0, 0);
        _cableHelp.Children.Add(new TextBlock
        {
            Style = St("Label"), TextWrapping = TextWrapping.Wrap,
            Text = L.T("VB-Cable برنامهٔ رایگان (donationware) شرکت VB-Audio است و لایسنسش بازتوزیع داخل برنامه‌های دیگه رو فقط با شرط‌های مشخص اجازه می‌ده؛ برای همین شب‌پا خودش دانلود یا نصبش نمی‌کنه. خودت از سایت رسمی بگیر:",
                "VB-Cable is VB-Audio's donationware and its licence allows bundling only under set conditions, so Shabpa never downloads or installs it for you. Get it yourself from the official site:"),
        });
        var cableButtons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var official = new Button { Style = St("Btn"), Content = L.T("صفحهٔ رسمی VB-Cable", "VB-Cable official page"), Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 8, 6) };
        official.Click += (_, _) => VbCable.Open(VbCable.OfficialPage);
        var direct = new Button { Style = St("Btn"), Content = L.T("دانلود مستقیم (سایت رسمی، ۱٫۳ مگ)", "Direct download (official site, 1.3 MB)"), Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 8, 6) };
        direct.Click += (_, _) => VbCable.Open(VbCable.DirectPackage);
        cableButtons.Children.Add(official);
        cableButtons.Children.Add(direct);
        _cableHelp.Children.Add(cableButtons);
        _cableHelp.Children.Add(new TextBlock
        {
            Style = St("Label"), TextWrapping = TextWrapping.Wrap,
            Text = L.T("نصب: فایل زیپ رو کامل Extract کن، VBCABLE_Setup_x64.exe رو با «Run as administrator» اجرا کن و Install Driver رو بزن، بعد کامپیوتر رو ری‌استارت کن. بعد از اون «CABLE Input» تو لیست بالا پیدا می‌شه. اگه به سایت رسمی دسترسی نداری، نسخه‌ای که از سایت‌های معتبر مثل سافت۹۸ می‌گیری هم همونه، ولی حتماً بررسی کن نسخهٔ سالم باشه.",
                "Install: extract the whole zip, run VBCABLE_Setup_x64.exe as administrator, press Install Driver, then restart the PC. “CABLE Input” will then show up in the list above. If the official site is unreachable, a copy from a trusted mirror such as Soft98 is the same package, but check it is the genuine one."),
        });

        // ---- default microphone -------------------------------------------------------------------
        var micBody = new StackPanel();
        var micHead = new Grid();
        micHead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        micHead.ColumnDefinitions.Add(new ColumnDefinition());
        micHead.Children.Add(new IconBadge("mic", "HueB", 40, 20, 12) { Margin = new Thickness(0, 0, 14, 0) });
        var micTexts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        micTexts.Children.Add(new TextBlock { Text = L.T("میکروفون پیش‌فرض", "Default microphone"), Style = St("Label") });
        micTexts.Children.Add(_device);
        Grid.SetColumn(micTexts, 1);
        micHead.Children.Add(micTexts);
        micBody.Children.Add(micHead);
        var level = Ui2.SliderRow(L.T("سطح ورودی", "Input level"), 0, 100, 1, v => Fa.Digits(((int)v).ToString()) + "%");
        _level = level.slider;
        level.row.Margin = new Thickness(0, 14, 0, 6);
        micBody.Children.Add(level.row);
        _level.ValueChanged += (_, _) =>
        {
            if (_loading) return;
            SetLevelLater(_level.Value / 100.0);
        };
        _mute.Click += (_, _) => SetMutedLater(_mute.IsChecked != true); // the switch says what is wanted; a double click cannot flip it back
        micBody.Children.Add(K.SettingRow(L.T("میکروفون وصل باشه", "Microphone on"), _mute));
        micBody.Children.Add(_deviceState);
        var micCard = new GlassCard { Child = micBody };

        // ---- noise test ---------------------------------------------------------------------------
        var (testCard, test) = NetParts.Card("clk", "HueC", L.T("تست نویز", "Noise test"),
            L.T("فقط وقتی دکمه رو می‌زنی چند ثانیه گوش می‌ده تا بلندی صدا رو اندازه بگیره. صدا ذخیره یا فرستاده نمی‌شه.",
                "It only listens for a few seconds after you press a button, to measure loudness. Nothing is saved or sent."));
        var buttons = new StackPanel { Margin = new Thickness(0, 14, 0, 4) };
        _noise.Content = NetParts.IconText("ecg", L.T("نویز اتاق (۴ ثانیه سکوت)", "Room noise (4 s of silence)"), out _);
        _speech.Content = NetParts.IconText("mic", L.T("صدای من (۴ ثانیه حرف بزن)", "My voice (speak for 4 s)"), out _);
        _noise.Click += (_, _) => RunTest(speech: false);
        _speech.Click += (_, _) => RunTest(speech: true);
        buttons.Children.Add(_noise);
        buttons.Children.Add(_speech);
        test.Children.Add(buttons);
        var reading = new System.Windows.Controls.Border { Style = St("Card"), Padding = new Thickness(16, 12, 16, 14) };
        var readingBody = new StackPanel();
        _meterText.HorizontalAlignment = HorizontalAlignment.Left;
        _meterText.Foreground = Res("Text");
        readingBody.Children.Add(K.Text(L.T("بلندی صدا", "Loudness"), "Label", 12));
        readingBody.Children.Add(_meterText);
        readingBody.Children.Add(_meter);
        reading.Child = readingBody;
        test.Children.Add(reading);
        test.Children.Add(_result);

        // ---- other suppressors --------------------------------------------------------------------
        var (toolCard, tool) = NetParts.Card("shield", "HueD", L.T("نویزگیر", "Noise suppression"),
            L.T("نویزگیرهای دیگه‌ای که روی همین سیستم وجود دارن (غیر از نویزگیر زنده‌ی شب‌پا).", "Other noise suppressors on this PC (besides Shabpa's own)."));
        _tools.Margin = new Thickness(0, 8, 0, 0);
        tool.Children.Add(_tools);
        var open = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var sound = new Button { Style = St("Btn"), Content = L.T("تنظیمات صدای ویندوز", "Windows sound settings"), Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 8, 6) };
        sound.Click += (_, _) => Launch("ms-settings:sound", "");
        var recording = new Button { Style = St("Btn"), Content = L.T("دستگاه‌های ضبط (ویژگی‌های میکروفون)", "Recording devices (microphone properties)"), Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 8, 6) };
        recording.Click += (_, _) => Launch("control.exe", "mmsys.cpl,,1");
        open.Children.Add(sound);
        open.Children.Add(recording);
        tool.Children.Add(open);
        var windowsTip = Ui2.Label(L.T("توی ویندوز ۱۱: Settings ← System ← Sound ← میکروفونت ← Audio enhancements (یا Voice clarity) رو روشن کن.",
            "In Windows 11: Settings → System → Sound → your microphone → turn Audio enhancements (or Voice clarity) on."), 6, 0);
        windowsTip.Style = St("Tip");
        tool.Children.Add(windowsTip);

        // ---- programs that used it ----------------------------------------------------------------
        var (appCard, apps) = NetParts.Card("user", "HueE", L.T("چه برنامه‌هایی از میکروفون استفاده کردن؟", "Which programs used the microphone?"),
            L.T("این لیست رو خود ویندوز نگه می‌داره؛ شب‌پا فقط می‌خونه.", "Windows keeps this list; Shabpa only reads it."));
        apps.Children.Add(_access);
        _apps.Margin = new Thickness(0, 6, 0, 0);
        apps.Children.Add(_apps);
        var privacy = new Button { Style = St("Btn"), Content = L.T("تنظیمات حریم خصوصی میکروفون", "Microphone privacy settings"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 12, 0, 0) };
        privacy.Click += (_, _) => Launch("ms-settings:privacy-microphone", "");
        apps.Children.Add(privacy);

        // the two small cards share one column next to the tall noise card, so neither stretches to fill the row
        micCard.Margin = new Thickness(0, 0, 0, 16);
        testCard.Margin = new Thickness(0);
        var side = new StackPanel();
        side.Children.Add(micCard);
        side.Children.Add(testCard);
        root.Add(voiceCard, 0, 0, 7, 0);
        root.Add(side, 0, 7, 5, 1);
        root.Add(toolCard, 1, 0, 6, 2);
        root.Add(appCard, 1, 6, 6, 3);
        Content = root;
        SetupVoice();
    }

    // ---- live noise suppression ---------------------------------------------------------------

    private static readonly (double value, string fa, string en)[] Strengths = [(0.35, "ملایم", "Gentle"), (0.65, "متعادل", "Balanced"), (0.95, "قوی", "Strong")];

    private void SetupVoice()
    {
        var settings = Hub.Settings.Voice;
        Chips(_voiceStrength, Strengths.Select(x => (x.value.ToString("0.00"), L.T(x.fa, x.en))), Closest().ToString("0.00"), key =>
        {
            settings.Strength = double.Parse(key, System.Globalization.CultureInfo.InvariantCulture);
            Hub.Voice.Strength = settings.Strength;
            SaveVoice();
        });
        // the switch sits in a settings row, so the segmented control needs no bottom gap
        foreach (var child in _voiceStrength.Children.OfType<FrameworkElement>()) child.Margin = new Thickness(0);
        _voiceAuto.Click += (_, _) => { settings.AutoStart = _voiceAuto.IsChecked == true; SaveVoice(); };
        _voiceToggle.Click += (_, _) =>
        {
            if (Hub.Voice.Running) Hub.Voice.Stop();
            else
            {
                var error = Hub.StartVoice();
                _voiceNote.Text = error ?? "";
            }
            ShowVoice();
        };
        _voiceTimer.Tick += (_, _) =>
        {
            var running = Hub.Voice.Running;
            if (running != _shownRunning) ShowRunning();
            if (!running && _voiceIn1.Value == 0 && _voiceOut1.Value == 0) return;
            _voiceIn1.Value = Hub.Voice.InLevel;
            _voiceOut1.Value = Hub.Voice.OutLevel;
            if (running) _voiceNote.Text = L.T($"روشنه ({(Hub.Voice.Engine == "ai" ? "هوش مصنوعی RNNoise" : "کلاسیک")}) · الان {Fa.Digits(Hub.Voice.ReductionDb.ToString("0"))} دسی‌بل از صدای زمینه کم شده.",
                $"On ({(Hub.Voice.Engine == "ai" ? "RNNoise neural network" : "classic")}) · currently {Hub.Voice.ReductionDb:0} dB quieter in the background.");
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) _voiceTimer.Start();
            else _voiceTimer.Stop();
        };
    }

    private static double Closest() => Strengths.OrderBy(x => Math.Abs(x.value - Hub.Settings.Voice.Strength)).First().value;

    private void SaveVoice() => Hub.Store.Save();

    /// <summary>Toggle button and header badge follow the engine (it can also be started or stopped from elsewhere).</summary>
    private void ShowRunning()
    {
        var running = Hub.Voice.Running;
        _shownRunning = running;
        var text = running ? L.T("توقف نویزگیر", "Stop noise suppression") : L.T("شروع نویزگیر", "Start noise suppression");
        _voiceToggle.Content = NetParts.IconText(running ? "x" : "play", text, out _);
        _voiceToggle.Style = St(running ? "Btn" : "BtnPrimary");
        _badge.Set(running ? L.T("نویزگیر روشنه", "Noise suppression on") : L.T("نویزگیر خاموشه", "Noise suppression off"), running ? "Green" : "Gold", "mic");
        if (_badgeShown is not null && _badgeShown != running)
        {
            Motion.Pop(_badge);
            Motion.Pop(_voiceToggle);
        }
        _badgeShown = running;
    }

    private void ShowVoice()
    {
        _loading = true;
        var settings = Hub.Settings.Voice;
        _inputs = VoiceFilter.Inputs();
        _outputs = VoiceFilter.Outputs();
        void Fill(StackPanel panel, List<AudioDevice> devices, string chosen, Action<string> pick)
        {
            var items = new List<(string value, string label, bool latin)> { ("", L.T("پیش‌فرض ویندوز", "Windows default"), false) };
            items.AddRange(devices.Select(d => (d.Name, d.Name, true)));
            panel.Children.Clear();
            panel.Children.Add(new NetChoiceList(items, devices.Any(d => d.Name == chosen) ? chosen : "", pick));
        }
        Fill(_voiceIn, _inputs, settings.Input, name => { settings.Input = name; SaveVoice(); });
        Fill(_voiceOut, _outputs, settings.Output, name => { settings.Output = name; SaveVoice(); });
        _voiceAuto.IsChecked = settings.AutoStart;
        ShowRunning();
        if (!Hub.Voice.Running && Hub.Voice.Error is { } error) _voiceNote.Text = error;
        else if (!Hub.Voice.Running)
        {
            var cable = VoiceFilter.VirtualCable(_outputs);
            _cableHelp.Visibility = cable is null ? Visibility.Visible : Visibility.Collapsed;
            _voiceNote.Text = cable is null
                ? L.T("کابل صوتی مجازی پیدا نشد؛ فعلاً فقط می‌تونی صدای تمیزشده رو خودت بشنوی.", "No virtual audio cable found; for now you can only hear the cleaned sound yourself.")
                : L.T($"«{cable.Name}» پیدا شد؛ برای فرستادن صدای تمیز به برنامه‌های دیگه، اون رو به‌عنوان مقصد انتخاب کن.", $"Found “{cable.Name}”; choose it as the destination to send the clean sound to other programs.");
        }
        _loading = false;
    }

    private readonly StackPanel _cableHelp = new();

    private static void Launch(string file, string arguments)
    {
        try { Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Write($"open settings: {ex.GetType().Name}"); }
    }

    private bool _gathering, _gatherAgain;
    private readonly object _levelGate = new();
    private double? _levelNext;
    private bool _levelRunning;

    /// <summary>The slider moves many times a second; the driver gets only the newest value, one call at a time, off the window's thread.</summary>
    private void SetLevelLater(double level)
    {
        lock (_levelGate)
        {
            _levelNext = level;
            if (_levelRunning) return;
            _levelRunning = true;
        }
        _ = Task.Run(() =>
        {
            while (true)
            {
                double next;
                lock (_levelGate)
                {
                    if (_levelNext is not { } value) { _levelRunning = false; return; }
                    next = value;
                    _levelNext = null;
                }
                try { Microphone.SetLevel(next); }
                catch (Exception ex) { Log.Write($"microphone level: {ex.GetType().Name}"); }
            }
        });
    }

    /// <summary>The audio driver and the registry are asked off the window's thread (a misbehaving audio driver can hold the
    /// call for many seconds); the page is filled in when the answers are there. Changes that arrive meanwhile run once more.</summary>
    private readonly object _muteGate = new();
    private bool? _muteWanted;
    private bool _muteRunning;

    /// <summary>Sets the mute state off the window's thread; quick clicks only leave the newest wish, applied in order.</summary>
    private void SetMutedLater(bool muted)
    {
        lock (_muteGate)
        {
            _muteWanted = muted;
            if (_muteRunning) return;
            _muteRunning = true;
        }
        _ = Task.Run(() =>
        {
            while (true)
            {
                bool next;
                lock (_muteGate)
                {
                    if (_muteWanted is not { } value) { _muteRunning = false; break; }
                    next = value;
                    _muteWanted = null;
                }
                try { Microphone.SetMuted(next); }
                catch (Exception ex) { Log.Write($"microphone mute: {ex.GetType().Name}: {ex.Message}"); }
            }
            Dispatcher.BeginInvoke(Refresh);
        });
    }

    public override void Refresh()
    {
        ShowVoice();
        if (_gathering) { _gatherAgain = true; return; }
        _gathering = true;
        _ = Task.Run(() =>
        {
            string? name = null;
            (double level, bool muted)? state = null;
            List<NoiseTool> toolList = [];
            bool? allowed = null;
            List<MicApp> list = [];
            try
            {
                name = Microphone.DeviceName();
                state = Microphone.Read();
                toolList = Microphone.NoiseTools().ToList();
                allowed = Microphone.DesktopAppsAllowed();
                list = Microphone.RecentApps().Take(12).ToList();
            }
            catch (Exception ex) { Log.Write($"microphone page: {ex.GetType().Name}: {ex.Message}"); }
            Dispatcher.BeginInvoke(() =>
            {
                _gathering = false;
                ApplyRefresh(name, state, toolList, allowed, list);
                if (_gatherAgain) { _gatherAgain = false; Refresh(); }
            });
        });
    }

    private void ApplyRefresh(string? name, (double level, bool muted)? state, List<NoiseTool> toolList, bool? allowed, List<MicApp> list)
    {
        _loading = true;
        _device.Text = name ?? L.T("میکروفونی پیدا نشد", "No microphone found");
        _device.FlowDirection = name is null ? Flow : FlowDirection.LeftToRight;
        _device.HorizontalAlignment = HorizontalAlignment.Stretch;
        _device.TextAlignment = name is null ? TextAlignment.Left : Flow == FlowDirection.RightToLeft ? TextAlignment.Right : TextAlignment.Left;
        _level.IsEnabled = state is not null;
        _mute.IsEnabled = state is not null;
        if (state is { } s)
        {
            _level.Value = Math.Round(s.level * 100);
            _mute.IsChecked = !s.muted;
            _mute.ToolTip = s.muted ? L.T("وصل کردن میکروفون", "Unmute") : L.T("قطع کردن میکروفون", "Mute");
            NetFx.Say(_deviceState, s.muted ? L.T("الان قطعه؛ هیچ برنامه‌ای صدا نمی‌گیره.", "Muted right now; no program hears anything.") : L.T("وصله.", "On."));
        }
        else
        {
            _mute.IsChecked = false;
            _mute.ToolTip = L.T("قطع کردن میکروفون", "Mute");
            NetFx.Say(_deviceState, L.T("میکروفونی وصل نیست یا ویندوز دسترسی نمی‌ده.", "No microphone is connected, or Windows does not allow access."));
        }
        _loading = false;

        _tools.Children.Clear();
        var toolSig = string.Join("|", toolList.Select(t => t.Name + t.Installed));
        foreach (var t in toolList)
        {
            var row = new Grid { Margin = new Thickness(0, 6, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var mark = new System.Windows.Controls.Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(9), VerticalAlignment = VerticalAlignment.Top,
                Background = Res(t.Installed ? "GreenSoft" : "Line"),
                Child = new Icon(t.Installed ? "chk" : "x", 15, Res(t.Installed ? "Green" : "Muted"), 2) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            row.Children.Add(mark);
            var box = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(box, 1);
            box.Children.Add(new TextBlock { Text = t.Name + (t.Installed ? L.T(" (نصبه)", " (installed)") : L.T(" (نصب نیست)", " (not installed)")), Style = St("Body"), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            if (t.Installed) box.Children.Add(new TextBlock { Text = L.T(t.HintFa, t.HintEn), Style = St("Label"), TextWrapping = TextWrapping.Wrap });
            row.Children.Add(box);
            _tools.Children.Add(row);
        }
        if (toolSig != _toolsSig) NetFx.RiseChildren(_tools, 8, 10, 0.5);
        _toolsSig = toolSig;

        _access.Text = allowed switch
        {
            true => L.T("ویندوز اجازه می‌ده برنامه‌های دسکتاپ از میکروفون استفاده کنن.", "Windows lets desktop programs use the microphone."),
            false => L.T("ویندوز دسترسی میکروفون برای برنامه‌های دسکتاپ رو بسته؛ تست‌ها و بعضی برنامه‌ها کار نمی‌کنن.", "Windows blocks microphone access for desktop programs; tests and some programs will not work."),
            _ => "",
        };
        _access.Visibility = _access.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _apps.Children.Clear();
        if (list.Count == 0) _apps.Children.Add(new TextBlock { Text = L.T("چیزی ثبت نشده.", "Nothing recorded."), Style = St("Body") });
        for (var i = 0; i < list.Count; i++)
        {
            var a = list[i];
            var when = a.InUseNow ? L.T("همین الان", "right now") : a.LastStart is { } t ? $"{Fa.DayOf(t, DateTime.Now)} {Fa.Clock(t)}" : L.T("تا حالا نه", "never");
            var row = new Grid { Margin = new Thickness(0, 7, 0, 7) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = NetParts.LatinText(a.Name, "Body");
            title.FontWeight = FontWeights.SemiBold;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            title.ToolTip = a.Name;
            title.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(title);
            var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0) };
            Grid.SetColumn(chips, 1);
            if (!a.Allowed) chips.Children.Add(PillText(L.T("مسدود", "blocked"), "Gold"));
            chips.Children.Add(a.InUseNow ? PillText(when, "Red") : new System.Windows.Controls.Border
            {
                Style = St("Pill"), Margin = new Thickness(a.Allowed ? 0 : 6, 0, 0, 0),
                Child = new TextBlock { Text = when, Foreground = Res("Muted"), FontSize = 12, FontWeight = FontWeights.Medium },
            });
            row.Children.Add(chips);
            if (i > 0) _apps.Children.Add(Divider());
            var line = new System.Windows.Controls.Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(-8, 0, -8, 0), Child = row };
            NetFx.HoverTint(line);
            _apps.Children.Add(line);
        }
        var appsSig = string.Join("|", list.Select(a => a.Name + a.InUseNow + a.Allowed));
        if (appsSig != _appsSig) NetFx.RiseChildren(_apps, 12, 10, 0.5);
        _appsSig = appsSig;
    }

    private void RunTest(bool speech)
    {
        if (_testing) return;
        _testing = true;
        _noise.IsEnabled = _speech.IsEnabled = false;
        NetFx.Say(_result, speech ? L.T("حرف بزن…", "Speak now…") : L.T("ساکت باش…", "Stay quiet…"));
        _meter.Value = 0;
        var rmsList = new List<double>();
        var peakMax = Microphone.SilenceFloorDb;
        _ = Task.Run(() =>
        {
            var error = Microphone.Listen(4000, (rms, peak) =>
            {
                lock (rmsList)
                {
                    rmsList.Add(rms);
                    if (peak > peakMax) peakMax = peak;
                }
                Dispatcher.BeginInvoke(() =>
                {
                    _meter.Value = Math.Clamp((rms + 70) / 70, 0, 1);
                    _meterText.Text = $"{rms:0} dB";
                });
            });
            Dispatcher.BeginInvoke(() =>
            {
                _testing = false;
                _noise.IsEnabled = _speech.IsEnabled = true;
                _meter.Value = 0;
                if (error is not null)
                {
                    NetFx.Say(_result, error);
                    return;
                }
                double mean;
                lock (rmsList)
                {
                    // the average of the loudest half is stable against a cough or a door in the silence test
                    var sorted = rmsList.OrderBy(x => x).ToList();
                    var part = speech ? sorted.Skip(sorted.Count / 2).ToList() : sorted.Take(Math.Max(1, sorted.Count * 3 / 4)).ToList();
                    mean = part.Count == 0 ? Microphone.SilenceFloorDb : part.Average();
                }
                if (speech) NetFx.Say(_result, L.T($"بلندی صدا: {mean:0} dB (اوج {peakMax:0} dB). ", $"Voice level: {mean:0} dB (peak {peakMax:0} dB). ") + Microphone.SpeechAdvice(mean, peakMax));
                else
                {
                    var verdict = Microphone.Judge(mean);
                    NetFx.Say(_result, L.T($"نویز اتاق: {mean:0} dB. ", $"Room noise: {mean:0} dB. ") + Microphone.Advice(verdict));
                }
            });
        });
    }
}
