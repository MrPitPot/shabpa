using System.Windows;
using Shabpa.Core;

namespace Shabpa;

// ---- typed commands («یه ساعت دیگه خاموش کن»): the command box and free text from @ShabPa_Bot ----------------------
public sealed partial class Hub
{
    /// <summary>The shortcut that opens the command box (null in demo runs).</summary>
    public AssistantHotkey? AssistantKey { get; private set; }

    /// <summary>Raised when the Bidar corner owl is switched on or off in Settings.</summary>
    public event Action? AssistantDockChanged;

    /// <summary>Things Bidar reacts to (a finished download, an error, a countdown). The window decides whether he is free to.</summary>
    public event Action<BidarCue>? BidarCued;

    public void CueBidar(BidarCue cue)
    {
        if (Application.Current?.Dispatcher is { } d && !d.CheckAccess()) { d.BeginInvoke(() => BidarCued?.Invoke(cue)); return; }
        BidarCued?.Invoke(cue);
    }

    public void SetBidarIdleLife(bool on)
    {
        Settings.BidarIdleLife = on;
        Store.Save();
        AssistantDockChanged?.Invoke();
    }

    public void SetAssistantDock(bool on)
    {
        Settings.ShowAssistantDock = on;
        Store.Save();
        AssistantDockChanged?.Invoke();
    }

    /// <summary>Runs a command the person has already confirmed on the card. Power actions go through the same paths as
    /// the Power page: "now" shows the cancellable countdown, a time goes into the queue (which shows its own countdown),
    /// a condition becomes the active rule. Returns what happened, in the current language.</summary>
    public RemoteReply RunAssistant(AssistantCommand command, string origin)
    {
        var now = DateTime.Now;
        MedalCount(Medals.AssistantCounter);
        Log.Write($"assistant {origin}: {command.Intent} {command.Action} at={command.At:yyyy-MM-dd HH:mm} rule={command.Condition?.Kind}");
        // a card that waited on screen past its own time must not run late
        if (command.At is { } late && late <= now && command.Intent is CommandIntent.Power or CommandIntent.Reminder && command.Repeat.Count == 0)
            return Reply(false, L.T("زمانش گذشت. دوباره بنویس.", "That time has passed. Type it again."));
        switch (command.Intent)
        {
            case CommandIntent.Power when command.Condition is { } rule:
            {
                var replaced = Engine.Rule is not null;
                ActivateRule(rule);
                return Reply(true, L.T("قانون شرطی فعال شد", "The conditional rule is on") + (replaced ? L.T(" (جای قانون قبلی).", " (it replaces the previous one).") : "."));
            }
            case CommandIntent.Power when command.At is { } at:
            {
                var action = command.Action ?? PowerAction.Sleep;
                return AddCommand(action, at, command.Repeat.Count > 0 ? command.Repeat : null)
                    ? Reply(true, L.T($"ثبت شد: {Fa.DayOf(at, now)} ساعت {Fa.Clock(at)} {Fa.ActionSentence(action)}.", $"Scheduled: {Fa.DayOf(at, now)} at {Fa.Clock(at)} {Fa.ActionSentence(action)}."))
                    : Reply(true, L.T("همین دستور از قبل توی صف بود.", "The same command was already in the queue."));
            }
            case CommandIntent.Power:
            {
                var action = command.Action ?? PowerAction.Sleep;
                if (action == PowerAction.Lock)
                {
                    Run(action);
                    return Reply(true, L.T("صفحه قفل شد.", "The screen is locked."));
                }
                var seconds = Math.Max(5, Settings.CountdownSeconds);
                return RequestAction(action, L.T("فرمان متنی", "Typed command"), seconds)
                    ? Reply(true, L.T($"{Fa.Digits(seconds.ToString())} ثانیه‌ی دیگه {Fa.ActionSentence(action)}؛ شمارش معکوس روی صفحه‌ست و می‌تونی لغوش کنی.",
                        $"In {seconds} seconds {Fa.ActionSentence(action)}; the countdown is on screen and can be cancelled."))
                    : Reply(false, L.T("الان یه شمارش معکوس دیگه روی صفحه‌ست. اول لغوش کن.", "Another countdown is already on screen. Cancel it first."));
            }
            case CommandIntent.Cancel:
            {
                var sleep = SleepActive;
                CancelSleepMode();
                var text = CancelEverything();
                if (sleep) text += "\n" + L.T("حالت شب‌خواب هم لغو شد.", "Sleep mode was cancelled too.");
                return Reply(true, text);
            }
            case CommandIntent.Wake when command.At is { } wake:
            {
                var error = AddWake(new WakeSchedule { Hour = wake.Hour, Minute = wake.Minute, Days = [.. command.Repeat] });
                return error is null
                    ? Reply(true, L.T($"بیدارباش ثبت شد: {Fa.Days(command.Repeat)} ساعت {Fa.Clock(wake)}.", $"Wake timer set: {Fa.Days(command.Repeat)} at {Fa.Clock(wake)}."))
                    : Reply(false, error);
            }
            case CommandIntent.Reminder:
            {
                var item = Tasks.Add(command.Title, command.At, command.At is null ? -1 : 0, now: now);
                if (item is null) return Reply(false, L.T("کار اضافه نشد (لیست پره یا عنوان خالیه).", "The task was not added (the list is full or the title is empty)."));
                return Reply(true, command.At is { } due
                    ? L.T($"یادآوری ثبت شد: {Fa.DayOf(due, now)} ساعت {Fa.Clock(due)}.", $"Reminder set: {Fa.DayOf(due, now)} at {Fa.Clock(due)}.")
                    : L.T("به کارها اضافه شد.", "Added to your tasks."));
            }
            case CommandIntent.Focus when !command.On:
                Focus.Stop(now);
                Store.Save();
                return Reply(true, L.T("تایمر تمرکز متوقف شد.", "The focus timer is stopped."));
            case CommandIntent.Focus:
                if (command.Minutes > 0) Settings.Focus.FocusMinutes = Math.Clamp(command.Minutes, 5, 90);
                Store.Save();
                Focus.Start(now);
                return Reply(true, L.T($"تمرکز {Fa.Digits(Settings.Focus.FocusMinutes.ToString())} دقیقه‌ای شروع شد.", $"A {Settings.Focus.FocusMinutes}-minute focus started."));
            case CommandIntent.NightLight:
            {
                var c = Settings.Comfort;
                c.DimOn = command.On;
                if (command.Level > 0) c.DimLevel = Math.Clamp(command.Level / 100.0, 0.05, 0.92);
                Store.Save();
                Dim?.Apply();
                ComfortChanged();
                return Reply(true, command.On ? L.T("نور شب روشن شد.", "Night light is on.") : L.T("نور شب خاموش شد.", "Night light is off."));
            }
            case CommandIntent.SleepMode when !command.On:
                if (!SleepActive) return Reply(true, L.T("حالت شب‌خواب روشن نبود.", "Sleep mode was not on."));
                CancelSleepMode();
                return Reply(true, L.T("حالت شب‌خواب لغو شد.", "Sleep mode is cancelled."));
            case CommandIntent.SleepMode:
            {
                if (SleepActive) return Reply(false, L.T("حالت شب‌خواب از قبل روشنه.", "Sleep mode is already on."));
                var c = Settings.Comfort;
                if (command.Minutes > 0) c.SleepMinutes = Math.Clamp(command.Minutes, 1, 600);
                if (command.Action is { } end) c.SleepAction = end;
                Store.Save();
                StartSleepMode();
                return Reply(true, L.T($"حالت شب‌خواب شروع شد؛ {Fa.Digits(c.SleepMinutes.ToString())} دقیقه‌ی دیگه {Fa.ActionSentence(c.SleepAction)}.",
                    $"Sleep mode started; in {c.SleepMinutes} minutes {Fa.ActionSentence(c.SleepAction)}."));
            }
            case CommandIntent.ListSchedules:
                return Reply(true, ScheduleText(now));
            case CommandIntent.OpenPage:
            {
                if (origin == "bot") return Reply(false, L.T("باز کردن صفحه فقط از خود برنامه ممکنه.", "Opening a page works only inside the app."));
                RequestPage(command.Page);
                Application.Current?.Dispatcher.BeginInvoke(() => (Application.Current as App)?.ShowWindow());
                return Reply(true, L.T($"صفحه‌ی «{CommandFeatures.PageName(command.Page)}» باز شد.", $"“{CommandFeatures.PageName(command.Page)}” is open."));
            }
            case CommandIntent.Overlay:
                return RunOverlay(command);
            case CommandIntent.Theme:
            {
                if (origin == "bot") return Reply(false, L.T("تم رو فقط از خود برنامه می‌شه عوض کرد.", "The theme can only be changed inside the app."));
                var key = Palette.Find(command.Value).Key;
                Application.Current?.Dispatcher.BeginInvoke(() => (Application.Current.MainWindow as MainWindow)?.SwitchTheme(key));
                return Reply(true, L.T($"تم شد «{Palette.Find(key).Fa}».", $"Theme is now {Palette.Find(key).En}."));
            }
            case CommandIntent.Rgb:
                return RunRgb(command);
            case CommandIntent.MemoryClean:
                CleanMemory(cache: false);
                return Reply(true, L.T("رم خالی شد (برنامه‌هایی که الان لازم نیستن کوچیک شدن).", "Memory was freed (programs that are not in use were trimmed)."));
            case CommandIntent.GameMode:
                Settings.Comfort.GameModeOn = command.On;
                Store.Save();
                ComfortChanged();
                return Reply(true, command.On ? L.T("حالت بازی روشن شد.", "Game mode is on.") : L.T("حالت بازی خاموش شد.", "Game mode is off."));
            case CommandIntent.Info:
                return Reply(true, InfoText(command.Value, now));            default:
                return Reply(false, L.T("این دستور رو نمی‌شناسم.", "Unknown command."));
        }
    }

    private RemoteReply RunOverlay(AssistantCommand command)
    {
        var o = Settings.Overlay;
        var (kind, arg) = command.Value.Split(':', 2) is [var k, var a] ? (k, a) : (command.Value, "");
        string said;
        switch (kind)
        {
            case "mode":
                o.Mode = Enum.TryParse<OverlayMode>(arg, out var mode) ? mode : OverlayMode.InGame;
                said = o.Mode switch
                {
                    OverlayMode.Off => L.T("اورلی خاموش شد.", "The overlay is off."),
                    OverlayMode.Always => L.T("اورلی همیشه نشون داده می‌شه.", "The overlay shows all the time."),
                    _ => L.T("اورلی فقط موقع بازی نشون داده می‌شه.", "The overlay shows only while gaming."),
                };
                break;
            case "layout":
                o.Layout = OverlayLayouts.All.Any(l => l.key == arg) ? arg : o.Layout;
                said = L.T($"طرح اورلی شد «{OverlayLayouts.All.First(l => l.key == o.Layout).fa}».", $"Overlay layout is now {OverlayLayouts.All.First(l => l.key == o.Layout).en}.");
                break;
            case "size":
                o.Scale = Math.Round(Math.Clamp((int.TryParse(arg, out var pct) ? pct : 100) / 100.0, OverlayRenderer.MinScale, OverlayRenderer.MaxScale), 2);
                said = L.T($"اندازه‌ی اورلی {Fa.Digits(Math.Round(o.Scale * 100).ToString())}٪ شد.", $"Overlay size is now {Math.Round(o.Scale * 100)}%.");
                break;
            default:
                return Reply(false, L.T("این دستور اورلی رو نفهمیدم.", "I did not understand that overlay command."));
        }
        Store.Save();
        OverlayChanged();
        return Reply(true, said);
    }

    private RemoteReply RunRgb(AssistantCommand command)
    {
        var rgb = Settings.Rgb;
        if (command.Value == "off")
        {
            rgb.Enabled = false;
            Store.Save();
            Rgb.Refresh();
            Rgb.BlackOut();
            return Reply(true, L.T("نورپردازی خاموش شد.", "The lights are off."));
        }
        if (command.Value == "on") rgb.Enabled = true;
        else
        {
            rgb.Enabled = true;
            var all = new[] { rgb.All }.Concat(rgb.Zones).ToList();
            foreach (var s in all)
            {
                if (command.Value.Length > 0) s.Effect = command.Value;
                if (command.Color.Length > 0) s.Color = command.Color;
                if (command.Level >= 0) s.Brightness = command.Level;
            }
            if (command.Value.Length == 0 && command.Color.Length > 0 && !RgbEffects.Find(rgb.All.Effect).UsesColor)
                foreach (var s in all) s.Effect = "static";               // a colour asked for on a fixed-palette look means "just this colour"
        }
        Store.Save();
        Rgb.Refresh();
        var link = Rgb.Link;
        var note = link is RgbLink.NoServer or RgbLink.NoDevices
            ? L.T(" (ولی الان به نورها نمی‌رسه؛ صفحه‌ی نورپردازی رو ببین.)", " (but it cannot reach the lights right now; see the Lighting page.)")
            : "";
        return Reply(true, L.T("نورپردازی تنظیم شد.", "The lighting is set.") + note);
    }

    /// <summary>Answers to quick questions and small talk.</summary>
    internal string InfoText(string kind, DateTime now)
    {
        var s = Snapshot;
        var t = Telemetry.Latest;
        switch (kind)
        {
            case "help": return CommandFeatures.HelpText(L.En);
            case "time": return L.T($"ساعت {Fa.Clock(now)} ـه.", $"It is {now:HH:mm}.");
            case "date": return L.T($"امروز {Fa.DayOf(now, now)} ـه.", $"Today is {now:dddd, d MMMM yyyy}.");
            case "cpu":
                return t.CpuTempC is { } c
                    ? L.T($"دمای پردازنده {Fa.Digits(Math.Round(c).ToString())} درجه‌ست.", $"The processor is at {Math.Round(c)} °C.")
                    : L.T($"دمای پردازنده رو نمی‌تونم بخونم؛ مصرفش {Fa.Digits(Math.Round(s.CpuLoad).ToString())}٪ه.", $"I cannot read the processor temperature; its load is {Math.Round(s.CpuLoad)}%.");
            case "gpu":
                return t.Gpu.TempC is { } g
                    ? L.T($"دمای کارت گرافیک {Fa.Digits(Math.Round(g).ToString())} درجه‌ست.", $"The graphics card is at {Math.Round(g)} °C.")
                    : L.T("دمای کارت گرافیک رو نمی‌تونم بخونم.", "I cannot read the graphics card temperature.");
            case "ram":
            {
                var m = MemoryCleaner.Read();
                return m.Total > 0 ? L.T($"رم {Fa.Digits(Math.Round(m.UsedPercent).ToString())}٪ پره.", $"Memory is {Math.Round(m.UsedPercent)}% full.") : L.T("مصرف رم رو نمی‌تونم بخونم.", "I cannot read the memory use.");
            }
            case "ping":
                return Ping.Samples.LastOrDefault() is { } ms
                    ? L.T($"پینگ {Fa.Digits(Math.Round(ms).ToString())} میلی‌ثانیه‌ست.", $"The ping is {Math.Round(ms)} ms.")
                    : L.T("هنوز پینگی نسنجیدم؛ صفحه‌ی «پایداری پینگ» رو باز کن.", "I have not measured the ping yet; open the Ping stability page.");
            case "status":
            {
                var parts = new List<string> { L.T($"پردازنده {Fa.Digits(Math.Round(s.CpuLoad).ToString())}٪", $"CPU {Math.Round(s.CpuLoad)}%") };
                if (t.CpuTempC is { } ct) parts.Add(L.T($"{Fa.Digits(Math.Round(ct).ToString())} درجه", $"{Math.Round(ct)} °C"));
                parts.Add(L.T($"کارت گرافیک {Fa.Digits(Math.Round(s.GpuLoad).ToString())}٪", $"GPU {Math.Round(s.GpuLoad)}%"));
                if (MemoryCleaner.Read() is { Total: > 0 } m) parts.Add(L.T($"رم {Fa.Digits(Math.Round(m.UsedPercent).ToString())}٪", $"RAM {Math.Round(m.UsedPercent)}%"));
                parts.Add(L.T($"دانلود {Fa.Rate(s.DownMbps)}", $"down {Fa.Rate(s.DownMbps)}"));
                return string.Join(L.T("، ", ", "), parts) + (s.GameRunning ? L.T(" — الان داری بازی می‌کنی.", " — you are gaming right now.") : ".");
            }
            case "hello": return Greeting(now);
            case "goodnight": return L.T("شب بخیر! 🌙 اگه خواستی، بگو سیستم رو بعد از چند دقیقه خاموش کنم.", "Good night! 🌙 If you like, tell me to shut the PC down in a few minutes.");
            case "thanks": return L.T("قابل نداشت! 🦉", "You are welcome! 🦉");
            case "howareyou": return L.T("من بیدارم و سرحالم 🦉 تو چطوری؟", "Wide awake and happy 🦉 and you?");
            case "who": return L.T("من بیدارم، جغد شب‌پا 🦉 کارم اینه که سیستمت رو سر وقت خاموش کنم و هر کاری از شب‌پا رو برات انجام بدم. «چه کارایی بلدی» رو بنویس.", "I am Bidar, Shabpa's owl 🦉 I switch your PC off on time and do anything Shabpa can do for you. Type “what can you do”.");
            case "joke": return Jokes[(now.Minute + now.Second) % Jokes.Length];
            case "version": return L.T($"نسخه‌ی شب‌پا {Updater.Current.ToString(3)} ـه.", $"Shabpa version {Updater.Current.ToString(3)}.");
            default: return L.T("نمی‌دونم چی بگم.", "I do not know what to say.");
        }
    }

    private static readonly string[] Jokes =
    [
        "چرا کامپیوتر سرما نمی‌خوره؟ چون همیشه ویندوزش بازه! 🪟",
        "Why do programmers prefer dark mode? Because light attracts bugs. 🐛",
        "به کامپیوتر گفتم خسته شدی؟ گفت نه، فقط یه کم هنگ کردم. 😅",
        "There are 10 kinds of people: those who understand binary and those who do not. 🦉",
        "پینگم ۹۹۹ بود؛ بازی گفت: «بیا یه استراحت کوتاه کنیم.» 🎮",
        "Why did the PC go to sleep? It had too many tabs open. 😴",
    ];

    private string Greeting(DateTime now)
    {
        var h = now.Hour;
        return h switch
        {
            < 5 => L.T("سلام شب‌زنده‌دار! 🦉 دیروقته؛ بگم سیستم رو بخوابونم؟", "Hello, night owl! 🦉 It is late; shall I put the PC to sleep?"),
            < 12 => L.T("سلام، صبح بخیر! ☀️", "Hello, good morning! ☀️"),
            < 18 => L.T("سلام! 🦉 در خدمتم.", "Hello! 🦉 At your service."),
            _ => L.T("سلام، عصر بخیر! 🌙", "Hello, good evening! 🌙"),
        };
    }
    private static RemoteReply Reply(bool ok, string text) => new(ok, text, text);

    /// <summary>Everything that is waiting: queued commands, the conditional rule, wake timers and sleep mode.</summary>
    public string ScheduleText(DateTime now)
    {
        var lines = new List<string>();
        foreach (var c in Queue.Items.OrderBy(c => c.DueAt))
            lines.Add(c.Repeats
                ? L.T($"⏳ {Fa.ActionName(c.Action)}: {Fa.Days(c.RepeatDays)} ساعت {Fa.Clock(c.DueAt)}", $"⏳ {Fa.ActionName(c.Action)}: {Fa.Days(c.RepeatDays)} at {Fa.Clock(c.DueAt)}")
                : L.T($"⏳ {Fa.ActionName(c.Action)}: {Fa.DayOf(c.DueAt, now)} ساعت {Fa.Clock(c.DueAt)}", $"⏳ {Fa.ActionName(c.Action)}: {Fa.DayOf(c.DueAt, now)} at {Fa.Clock(c.DueAt)}"));
        if (Engine.Rule is { } rule)
            lines.Add(L.T($"🎯 قانون شرطی ({Fa.ActionName(rule.Action)}): {Engine.Status.Text}", $"🎯 Conditional rule ({Fa.ActionName(rule.Action)}): {Engine.Status.Text}"));
        foreach (var w in State.WakeSchedules)
            lines.Add(L.T($"⏰ بیدارباش: {Fa.Days(w.Days)} ساعت {Fa.Clock(w.Hour, w.Minute)}", $"⏰ Wake: {Fa.Days(w.Days)} at {Fa.Clock(w.Hour, w.Minute)}"));
        if (SleepActive) lines.Add(L.T("🌙 حالت شب‌خواب روشنه.", "🌙 Sleep mode is on."));
        return lines.Count == 0 ? L.T("هیچ دستوری در راه نیست.", "Nothing is scheduled.") : string.Join("\n", lines);
    }

    /// <summary>Free text sent to @ShabPa_Bot. Only a clear reading runs, through the same commands the bot already has
    /// (power still shows its on-screen countdown); anything unclear is answered with the choices, and nothing runs.</summary>
    internal RemoteReply ExecuteText(string text)
    {
        var now = DateTime.Now;
        var result = CommandParser.Parse(text, now);
        // a guessed time (the person gave no clock) never runs unseen: there is no card to confirm it on here
        if (result is { Status: CommandStatus.Ready, Command: { Defaulted: true } guessed })
            return Reply(false, L.T("ساعت دقیق رو ننوشتی. دقیق بنویس، مثلاً «ساعت ۹ صبح»؛ چیزی اجرا نشد.", "You did not give an exact time. Write it exactly, e.g. “at 9am”; nothing was run.")
                + Environment.NewLine + guessed.Summary(now));
        if (result is { Status: CommandStatus.Ready, Command: { } command })
        {
            if (command.ToBotRequest(now) is { } request)
            {
                using var doc = System.Text.Json.JsonDocument.Parse(request.Args);
                var reply = ExecuteRemote(request.Type, doc.RootElement.Clone());
                // say the exact time that was understood, so a misreading is seen at once
                return reply.Ok && command.Intent == CommandIntent.Power && command.At is not null
                    ? new RemoteReply(true, reply.Fa + Environment.NewLine + command.Summary(now), reply.En + Environment.NewLine + command.Summary(now))
                    : reply;
            }
            return RunAssistant(command, "bot");
        }
        var lines = new List<string> { result.Question };
        // the bot has no buttons for these yet: the person answers by writing one of them out in full
        foreach (var choice in result.Choices) lines.Add("• " + choice.Summary(now));
        return RemoteReply.Fail(string.Join("\n", lines), string.Join("\n", lines));
    }
}
