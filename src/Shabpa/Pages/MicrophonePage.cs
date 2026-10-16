using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;

namespace Shabpa.Pages;

/// <summary>«میکروفون»: level and mute, a short noise test with plain advice, the noise suppressors that exist on this
/// PC, and which programs used the microphone. Audio is only listened to while a test runs, only to measure loudness,
/// and is never saved or sent.</summary>
public sealed class MicrophonePage : PageBase
{
    private readonly TextBlock _device = new() { Style = St("Body"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock _deviceState = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private Slider _level = null!;
    private readonly Button _mute = new() { Style = St("Btn"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
    private readonly ProgressBar _meter = new() { Style = St("Bar"), Minimum = 0, Maximum = 1, Height = 10, Margin = new Thickness(0, 12, 0, 0) };
    private readonly TextBlock _meterText = new() { Style = St("Label"), Margin = new Thickness(0, 6, 0, 0), FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _result = new() { Style = St("Body"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly Button _noise = new() { Style = St("BtnPrimary"), Margin = new Thickness(0, 0, 8, 6) };
    private readonly Button _speech = new() { Style = St("Btn"), Margin = new Thickness(0, 0, 8, 6) };
    private readonly StackPanel _tools = new();
    private readonly StackPanel _apps = new();
    private readonly TextBlock _access = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private bool _loading;
    private bool _testing;
    private readonly WrapPanel _voiceIn = new();
    private readonly WrapPanel _voiceOut = new();
    private readonly WrapPanel _voiceStrength = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly Button _voiceToggle = new() { Style = St("BtnPrimary"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
    private readonly ProgressBar _voiceIn1 = new() { Style = St("Bar"), Minimum = 0, Maximum = 1, Height = 8, Margin = new Thickness(0, 4, 0, 0) };
    private readonly ProgressBar _voiceOut1 = new() { Style = St("Bar"), Minimum = 0, Maximum = 1, Height = 8, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBlock _voiceNote = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly CheckBox _voiceAuto = new() { Style = St("Check"), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly System.Windows.Threading.DispatcherTimer _voiceTimer = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private List<AudioDevice> _inputs = [], _outputs = [];

    public override string PageTitle => L.T("میکروفون", "Microphone");
    public override string PageSubtitle => L.T("سطح و قطع/وصل، یه تست نویز با راهنمای ساده، نویزگیرهای موجود و برنامه‌هایی که از میکروفون استفاده کردن.",
        "Level and mute, a short noise test with plain advice, the noise suppressors available, and which programs used the microphone.");

    public MicrophonePage()
    {
        var root = new StackPanel();

        // ---- live noise suppression ---------------------------------------------------------------
        var (voiceCard, voice) = Ui2.Card(L.T("نویزگیر زنده‌ی شب‌پا", "Shabpa live noise suppression"),
            L.T("صدای میکروفون رو همین الان تمیز می‌کنه: خش‌خش، وزوز و صدای فن کم می‌شه و صدای تو می‌مونه. همه‌چیز توی حافظه و روی همین کامپیوتر انجام می‌شه؛ صدا ذخیره یا فرستاده نمی‌شه.",
                "Cleans the microphone live: hiss, hum and fan noise go down and your voice stays. It all happens in memory on this PC; audio is never saved or sent."));
        voice.Children.Add(Ui2.Label(L.T("میکروفون", "Microphone"), 12, 4));
        voice.Children.Add(_voiceIn);
        voice.Children.Add(Ui2.Label(L.T("صدای تمیز کجا بره؟", "Where should the clean sound go?"), 8, 4));
        voice.Children.Add(_voiceOut);
        voice.Children.Add(Ui2.Label(L.T("شدت", "Strength"), 8, 0));
        voice.Children.Add(_voiceStrength);
        voice.Children.Add(_voiceToggle);
        voice.Children.Add(Ui2.Label(L.T("قبل (میکروفون)", "Before (microphone)"), 12, 0));
        voice.Children.Add(_voiceIn1);
        voice.Children.Add(Ui2.Label(L.T("بعد (تمیز‌شده)", "After (cleaned)"), 8, 0));
        voice.Children.Add(_voiceOut1);
        voice.Children.Add(_voiceNote);
        _voiceAuto.Content = L.T("با هر بار باز شدن شب‌پا خودکار روشن بشه", "Switch on by itself whenever Shabpa starts");
        voice.Children.Add(_voiceAuto);
        voice.Children.Add(Ui2.Label(L.T("برای اینکه برنامه‌های دیگه (دیسکورد، تیم‌اسپیک، بازی…) صدای تمیز رو بشنون، یه کابل صوتی مجازی (مثل VB-Cable که رایگانه) لازمه: «صدای تمیز کجا بره؟» رو روی CABLE Input بذار و توی اون برنامه میکروفون رو CABLE Output انتخاب کن. شب‌پا خودش هیچ درایوری نصب نمی‌کنه. اگه فقط می‌خوای صدای خودت رو بشنوی، یه هدفون انتخاب کن (با بلندگو صدا برمی‌گرده توی میکروفون و زوزه می‌کشه).",
            "For other programs (Discord, TeamSpeak, games…) to hear the clean sound you need a virtual audio cable (VB-Cable is free): set “Where should the clean sound go?” to CABLE Input and pick CABLE Output as the microphone in that program. Shabpa installs no driver itself. To just hear yourself, choose headphones (speakers feed back into the microphone and howl)."), 12, 0));
        root.Children.Insert(0, voiceCard);

        var (card, body) = Ui2.Card(L.T("میکروفون پیش‌فرض", "Default microphone"));
        body.Children.Add(_device);
        var level = Ui2.SliderRow(L.T("سطح ورودی", "Input level"), 0, 100, 1, v => Fa.Digits(((int)v).ToString()) + "%");
        _level = level.slider;
        level.row.Margin = new Thickness(0, 12, 0, 0);
        body.Children.Add(level.row);
        _level.ValueChanged += (_, _) =>
        {
            if (_loading) return;
            Microphone.SetLevel(_level.Value / 100.0);
        };
        _mute.Click += (_, _) =>
        {
            var now = Microphone.Read();
            if (now is null) return;
            Microphone.SetMuted(!now.Value.muted);
            Refresh();
        };
        body.Children.Add(_mute);
        body.Children.Add(_deviceState);
        root.Children.Add(card);

        var (testCard, test) = Ui2.Card(L.T("تست نویز", "Noise test"),
            L.T("فقط وقتی دکمه رو می‌زنی چند ثانیه گوش می‌ده تا بلندی صدا رو اندازه بگیره. صدا ذخیره یا فرستاده نمی‌شه.",
                "It only listens for a few seconds after you press a button, to measure loudness. Nothing is saved or sent."));
        var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        _noise.Content = L.T("نویز اتاق (۴ ثانیه سکوت)", "Room noise (4 s of silence)");
        _speech.Content = L.T("صدای من (۴ ثانیه حرف بزن)", "My voice (speak for 4 s)");
        _noise.Click += (_, _) => RunTest(speech: false);
        _speech.Click += (_, _) => RunTest(speech: true);
        buttons.Children.Add(_noise);
        buttons.Children.Add(_speech);
        test.Children.Add(buttons);
        test.Children.Add(_meter);
        test.Children.Add(_meterText);
        test.Children.Add(_result);
        root.Children.Add(testCard);

        var (toolCard, tool) = Ui2.Card(L.T("نویزگیر", "Noise suppression"),
            L.T("نویزگیرهای دیگه‌ای که روی همین سیستم وجود دارن (غیر از نویزگیر زنده‌ی شب‌پا).", "Other noise suppressors on this PC (besides Shabpa's own)."));
        tool.Children.Add(_tools);
        var open = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var sound = new Button { Style = St("Btn"), Content = L.T("تنظیمات صدای ویندوز", "Windows sound settings"), Margin = new Thickness(0, 0, 8, 6) };
        sound.Click += (_, _) => Launch("ms-settings:sound", "");
        var recording = new Button { Style = St("Btn"), Content = L.T("دستگاه‌های ضبط (ویژگی‌های میکروفون)", "Recording devices (microphone properties)"), Margin = new Thickness(0, 0, 8, 6) };
        recording.Click += (_, _) => Launch("control.exe", "mmsys.cpl,,1");
        open.Children.Add(sound);
        open.Children.Add(recording);
        tool.Children.Add(open);
        tool.Children.Add(Ui2.Label(L.T("توی ویندوز ۱۱: Settings ← System ← Sound ← میکروفونت ← Audio enhancements (یا Voice clarity) رو روشن کن.",
            "In Windows 11: Settings → System → Sound → your microphone → turn Audio enhancements (or Voice clarity) on."), 8, 0));
        root.Children.Add(toolCard);

        var (appCard, apps) = Ui2.Card(L.T("چه برنامه‌هایی از میکروفون استفاده کردن؟", "Which programs used the microphone?"),
            L.T("این لیست رو خود ویندوز نگه می‌داره؛ شب‌پا فقط می‌خونه.", "Windows keeps this list; Shabpa only reads it."));
        apps.Children.Add(_access);
        _apps.Margin = new Thickness(0, 8, 0, 0);
        apps.Children.Add(_apps);
        var privacy = new Button { Style = St("Btn"), Content = L.T("تنظیمات حریم خصوصی میکروفون", "Microphone privacy settings"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        privacy.Click += (_, _) => Launch("ms-settings:privacy-microphone", "");
        apps.Children.Add(privacy);
        root.Children.Add(appCard);
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
            _voiceIn1.Value = Hub.Voice.InLevel;
            _voiceOut1.Value = Hub.Voice.OutLevel;
            if (Hub.Voice.Running) _voiceNote.Text = L.T($"روشنه · الان {Fa.Digits(Hub.Voice.ReductionDb.ToString("0"))} دسی‌بل از صدای زمینه کم شده.", $"On · currently {Hub.Voice.ReductionDb:0} dB quieter in the background.");
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) _voiceTimer.Start();
            else _voiceTimer.Stop();
        };
    }

    private static double Closest() => Strengths.OrderBy(x => Math.Abs(x.value - Hub.Settings.Voice.Strength)).First().value;

    private void SaveVoice() => Hub.Store.Save();

    private void ShowVoice()
    {
        _loading = true;
        var settings = Hub.Settings.Voice;
        var inputs = VoiceFilter.Inputs();
        var outputs = VoiceFilter.Outputs();
        var same = _voiceIn.Children.Count > 0 && inputs.Select(d => d.Name).SequenceEqual(_inputs.Select(d => d.Name)) && outputs.Select(d => d.Name).SequenceEqual(_outputs.Select(d => d.Name));
        _inputs = inputs;
        _outputs = outputs;
        void Fill(WrapPanel panel, List<AudioDevice> devices, string chosen, Action<string> pick)
        {
            var items = new List<(string, string)> { ("", L.T("پیش‌فرض ویندوز", "Windows default")) };
            items.AddRange(devices.Select(d => (d.Name, d.Name)));
            Chips(panel, items, devices.Any(d => d.Name == chosen) ? chosen : "", pick);
        }
        Fill(_voiceIn, _inputs, settings.Input, name => { settings.Input = name; SaveVoice(); });
        Fill(_voiceOut, _outputs, settings.Output, name => { settings.Output = name; SaveVoice(); });
        _voiceAuto.IsChecked = settings.AutoStart;
        _voiceToggle.Content = Hub.Voice.Running ? L.T("توقف نویزگیر", "Stop noise suppression") : L.T("شروع نویزگیر", "Start noise suppression");
        _voiceToggle.Style = St(Hub.Voice.Running ? "Btn" : "BtnPrimary");
        if (!Hub.Voice.Running && Hub.Voice.Error is { } error) _voiceNote.Text = error;
        else if (!Hub.Voice.Running)
        {
            var cable = VoiceFilter.VirtualCable(_outputs);
            _voiceNote.Text = cable is null
                ? L.T("کابل صوتی مجازی پیدا نشد؛ فعلاً فقط می‌تونی صدای تمیزشده رو خودت بشنوی.", "No virtual audio cable found; for now you can only hear the cleaned sound yourself.")
                : L.T($"«{cable.Name}» پیدا شد؛ برای فرستادن صدای تمیز به برنامه‌های دیگه، اون رو به‌عنوان مقصد انتخاب کن.", $"Found “{cable.Name}”; choose it as the destination to send the clean sound to other programs.");
        }
        _loading = false;
    }

    private static void Launch(string file, string arguments)
    {
        try { Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Write($"open settings: {ex.GetType().Name}"); }
    }

    public override void Refresh()
    {
        ShowVoice();
        _loading = true;
        var name = Microphone.DeviceName();
        var state = Microphone.Read();
        _device.Text = name ?? L.T("میکروفونی پیدا نشد", "No microphone found");
        _level.IsEnabled = state is not null;
        _mute.IsEnabled = state is not null;
        if (state is { } s)
        {
            _level.Value = Math.Round(s.level * 100);
            _mute.Content = s.muted ? L.T("وصل کردن میکروفون", "Unmute") : L.T("قطع کردن میکروفون", "Mute");
            _deviceState.Text = s.muted ? L.T("الان قطعه؛ هیچ برنامه‌ای صدا نمی‌گیره.", "Muted right now; no program hears anything.") : L.T("وصله.", "On.");
        }
        else
        {
            _mute.Content = L.T("قطع کردن میکروفون", "Mute");
            _deviceState.Text = L.T("میکروفونی وصل نیست یا ویندوز دسترسی نمی‌ده.", "No microphone is connected, or Windows does not allow access.");
        }
        _loading = false;

        _tools.Children.Clear();
        foreach (var t in Microphone.NoiseTools())
        {
            var box = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
            box.Children.Add(new TextBlock { Text = (t.Installed ? "✓ " : "✗ ") + t.Name + (t.Installed ? L.T(" (نصبه)", " (installed)") : L.T(" (نصب نیست)", " (not installed)")), Style = St("Body"), FontWeight = FontWeights.SemiBold });
            if (t.Installed) box.Children.Add(new TextBlock { Text = L.T(t.HintFa, t.HintEn), Style = St("Label"), TextWrapping = TextWrapping.Wrap });
            _tools.Children.Add(box);
        }

        var allowed = Microphone.DesktopAppsAllowed();
        _access.Text = allowed switch
        {
            true => L.T("ویندوز اجازه می‌ده برنامه‌های دسکتاپ از میکروفون استفاده کنن.", "Windows lets desktop programs use the microphone."),
            false => L.T("ویندوز دسترسی میکروفون برای برنامه‌های دسکتاپ رو بسته؛ تست‌ها و بعضی برنامه‌ها کار نمی‌کنن.", "Windows blocks microphone access for desktop programs; tests and some programs will not work."),
            _ => "",
        };
        _apps.Children.Clear();
        var list = Microphone.RecentApps().Take(12).ToList();
        if (list.Count == 0) _apps.Children.Add(new TextBlock { Text = L.T("چیزی ثبت نشده.", "Nothing recorded."), Style = St("Body") });
        foreach (var a in list)
        {
            var when = a.InUseNow ? L.T("همین الان", "right now") : a.LastStart is { } t ? $"{Fa.DayOf(t, DateTime.Now)} {Fa.Clock(t)}" : L.T("تا حالا نه", "never");
            _apps.Children.Add(new TextBlock
            {
                Text = $"{(a.InUseNow ? "🔴 " : "")}{a.Name} · {when}" + (a.Allowed ? "" : L.T(" · مسدود", " · blocked")),
                Style = St("Body"), Margin = new Thickness(0, 3, 0, 3), FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left,
            });
        }
    }

    private void RunTest(bool speech)
    {
        if (_testing) return;
        _testing = true;
        _noise.IsEnabled = _speech.IsEnabled = false;
        _result.Text = speech ? L.T("حرف بزن…", "Speak now…") : L.T("ساکت باش…", "Stay quiet…");
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
                    _result.Text = error;
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
                if (speech) _result.Text = L.T($"بلندی صدا: {mean:0} dB (اوج {peakMax:0} dB). ", $"Voice level: {mean:0} dB (peak {peakMax:0} dB). ") + Microphone.SpeechAdvice(mean, peakMax);
                else
                {
                    var verdict = Microphone.Judge(mean);
                    _result.Text = L.T($"نویز اتاق: {mean:0} dB. ", $"Room noise: {mean:0} dB. ") + Microphone.Advice(verdict);
                }
            });
        });
    }
}
