using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Shabpa.Core;

namespace Shabpa;

/// <summary>Owns the long-lived services and is the one place the UI talks to.</summary>
public sealed class Hub
{
    public static Hub I { get; private set; } = null!;

    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private CountdownWindow? _countdown;
    private DateTime _lastSave = DateTime.Now;

    public Store Store { get; }
    public AppState State => Store.State;
    public Settings Settings => State.Settings;
    public SensorHub Sensors { get; }
    public RuleEngine Engine { get; } = new();
    public CommandQueue Queue { get; }
    public EnergyTracker Energy { get; }
    public TelegramBot Bot { get; }
    public IPowerExecutor Executor { get; }
    public TrafficMeter Traffic { get; }
    public PingMeter Ping { get; }
    public Shaper Shaper { get; } = new();
    public UsageHistory Usage { get; } = new();
    public Updater Updater { get; }
    public bool DryRun { get; }
    public bool Demo { get; }
    public SensorSnapshot Snapshot => Sensors.Latest;
    public static string ExePath => Environment.ProcessPath ?? "Shabpa.exe";

    /// <summary>Queue, rule or schedules changed.</summary>
    public event Action? Changed;
    /// <summary>A fresh sensor snapshot, raised on the UI thread.</summary>
    public event Action<SensorSnapshot>? Tick;
    /// <summary>Once a second on the UI thread, for countdown labels.</summary>
    public event Action? Second;
    public event Action<string, string>? Notified;

    public Hub(Store store, bool dryRun, bool demo)
    {
        I = this;
        Store = store;
        DryRun = dryRun;
        Demo = demo;
        Executor = dryRun ? new DryRunPowerExecutor() : new WindowsPowerExecutor();
        Queue = new CommandQueue(State.Queue);
        Energy = new EnergyTracker(State.Energy);
        Sensors = new SensorHub(() => Settings);
        Traffic = new TrafficMeter(_resolver.Resolve);
        Ping = new PingMeter(() => Settings.PingHost);
        Updater = new Updater(() => Settings);
        Bot = new TelegramBot(() => Settings, Store.Save, new BotHost
        {
            Status = () => OnUi(StatusText),
            Request = action => OnUi(() => RequestFromBot(action)),
            Cancel = () => OnUi(CancelEverything),
            Update = () => OnUi(UpdateFromBot),
        });
    }

    private static T OnUi<T>(Func<T> f) => Application.Current.Dispatcher.Invoke(f);

    public void Start()
    {
        if (State.ActiveRule is { } rule) Engine.Activate(rule);

        Traffic.Load(State.NetUsage, DateTime.Now);
        if (Demo) Usage.FillDemo(DateTime.Now);
        else Usage.Load(UsageFile);
        if (!Demo)
        {
            _net = NetMonitor.TryStart(Traffic, out var netError);
            NetError = netError;
        }

        Sensors.Updated += s => Application.Current?.Dispatcher.BeginInvoke(() => OnSensors(s));
        Sensors.Start();
        _clock.Tick += (_, _) => OnSecond();
        _clock.Start();
        Bot.Restart();

        // The wake tasks point at this exe; re-registering keeps them valid if the app was moved.
        if (!Demo && State.WakeSchedules.Count > 0)
            Task.Run(() =>
            {
                foreach (var schedule in State.WakeSchedules.ToList())
                    WakeScheduler.Register(schedule, ExePath);
            });
    }

    public void Shutdown()
    {
        _clock.Stop();
        Sensors.Dispose();
        Bot.Dispose();
        Ping.Dispose();
        _net?.Dispose();
        // Limits only exist inside this process: stopping the shaper restores normal traffic.
        Shaper.Dispose();
        Store.Save();
        if (!Store.ReadOnly && !Demo) Usage.Save(UsageFile);
    }

    private string UsageFile => System.IO.Path.Combine(Store.Folder, "usage.json");

    private void OnSecond()
    {
        var now = DateTime.Now;
        var missed = new List<ScheduledCommand>();
        var due = Queue.TakeDue(now, missed);
        if (due.Count > 0 || missed.Count > 0)
        {
            Store.Save();
            Changed?.Invoke();
        }
        // Anything that came due while the app was not running is dropped, not fired late.
        if (missed.Count > 0)
            Notify(L.T("دستور جامانده", "Missed command"),
                L.T($"«{Fa.ActionName(missed[0].Action)}» اجرا نشد چون موقعش سیستم خواب بود یا شب‌پا باز نبود.",
                    $"“{Fa.ActionName(missed[0].Action)}” did not run because the PC was asleep or Shabpa was closed at the time."));
        foreach (var cmd in due)
        {
            // Another countdown is already on screen: try this one again in a minute rather than lose it.
            if (!RequestAction(cmd.Action, L.T("دستور زمان‌بندی‌شده", "Scheduled command"), Settings.CountdownSeconds))
                Queue.Postpone(cmd.Action, now, TimeSpan.FromMinutes(1), cmd.Source);
        }

        CheckForUpdates(now);

        if (now - _lastSave > TimeSpan.FromMinutes(1))
        {
            _lastSave = now;
            // On the UI thread on purpose: everything that mutates the state lives here too.
            Store.Save();
            if (!Store.ReadOnly && !Demo) Usage.Save(UsageFile);
        }
        Second?.Invoke();
    }

    private void OnSensors(SensorSnapshot s)
    {
        if (!Demo) Energy.Add(s);
        UpdateNet(s);
        UpdateMemory();
        var rule = Engine.Rule;
        var status = Engine.Evaluate(s, s.Time);
        if (status.State == RuleState.Fire && rule is not null)
        {
            Engine.Deactivate();
            State.ActiveRule = null;
            Store.Save();
            Changed?.Invoke();
            Bot.Notify(L.T($"شرط برقرار شد: {status.Text}\n{Fa.ActionSentence(rule.Action)}.", $"Condition met: {status.Text}\nNow {Fa.ActionSentence(rule.Action)}."));
            RequestAction(rule.Action, L.T("خاموشی شرطی", "Conditional rule"), Settings.CountdownSeconds);
        }
        Tick?.Invoke(s);
    }

    // ---- memory ----------------------------------------------------------------------------------

    private DateTime _lastAutoTrim = DateTime.MinValue;
    private bool _cleaning;

    public MemoryInfo Memory { get; private set; } = new(0, 0, 0);
    public bool Cleaning => _cleaning;
    public event Action<MemoryResult, bool>? MemoryCleaned;

    private void UpdateMemory()
    {
        Memory = Demo ? new MemoryInfo(32L << 30, 14L << 30, 9L << 30) : MemoryCleaner.Read();
        var threshold = Settings.AutoTrimPercent;
        if (threshold > 0 && !Demo && Memory.UsedPercent >= threshold && DateTime.Now - _lastAutoTrim > TimeSpan.FromMinutes(10))
        {
            _lastAutoTrim = DateTime.Now;
            CleanMemory(cache: false);
        }
    }

    /// <summary>Trims working sets, or with <paramref name="cache"/> purges the standby cache. Runs in
    /// the background; <see cref="MemoryCleaned"/> reports the result.</summary>
    public void CleanMemory(bool cache)
    {
        if (_cleaning) return;
        _cleaning = true;
        Task.Run(() =>
        {
            var result = Demo
                ? new MemoryResult(cache ? 6L << 30 : 3L << 30, 180, null)
                : cache ? MemoryCleaner.PurgeCache() : MemoryCleaner.TrimWorkingSets();
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                _cleaning = false;
                if (!Demo) Memory = MemoryCleaner.Read();
                MemoryCleaned?.Invoke(result, cache);
            });
        });
    }

    // ---- network: per-app traffic and limits --------------------------------------------------

    private readonly ProcessResolver _resolver = new();
    private NetMonitor? _net;
    private long _netStamp;
    private string _appliedSignature = "";
    private bool _applying;

    public static bool Elevated => Elevation.IsElevated;
    /// <summary>Per-app monitoring and limits need admin; demo runs pretend so the UI can be reviewed.</summary>
    public bool CanLimit => Elevated || Demo;
    /// <summary>Why per-app monitoring is not running, or null when it is.</summary>
    public string? NetError { get; private set; }
    public string? LimitError { get; private set; }
    public IReadOnlyList<AppTraffic> Apps { get; private set; } = [];
    /// <summary>The limits that should be in force right now (manual + automatic).</summary>
    public IReadOnlyList<EffectiveLimit> Planned { get; private set; } = [];
    public IReadOnlyList<EffectiveLimit> Applied { get; private set; } = [];
    public string GameProcess { get; private set; } = "";
    public event Action? NetTick;

    private void UpdateNet(SensorSnapshot s)
    {
        var stamp = Stopwatch.GetTimestamp();
        var seconds = _netStamp == 0 ? SensorHub.Interval.TotalSeconds : Stopwatch.GetElapsedTime(_netStamp, stamp).TotalSeconds;
        _netStamp = stamp;

        if (Demo)
        {
            Apps = DemoApps(s.Time);
        }
        else if (_net is not null)
        {
            Apps = Traffic.Snapshot(seconds, s.Time);
            State.NetUsage = Traffic.Export();
            Usage.Update(s.Time, Apps);
        }

        GameProcess = s.GameProcess;
        Replan();
        NetTick?.Invoke();
    }

    private void Replan()
    {
        var plan = LimitPlanner.Plan(State.NetLimits, Settings.GuardEnabled, Settings.GuardCapMbps, GameProcess, Apps, DateTime.Now, Settings.GuardDownCapMbps);
        Planned = plan;
        var signature = LimitPlanner.Signature(plan);
        if (signature == _appliedSignature || _applying) return;

        if (Demo)
        {
            _appliedSignature = signature;
            Applied = plan;
            return;
        }
        if (!Elevated) return;

        // Off the UI thread: the first start loads the driver, which can take a moment.
        _applying = true;
        Task.Run(() =>
        {
            Shaper.Apply(plan);
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                _applying = false;
                _appliedSignature = signature;
                LimitError = Shaper.Error switch
                {
                    null => null,
                    "driver-missing" => L.T("فایل‌های درایور WinDivert (WinDivert.dll و WinDivert64.sys) کنار برنامه نیستن؛ بدون اون‌ها محدودیت سرعت کار نمی‌کنه.",
                        "The WinDivert driver files (WinDivert.dll and WinDivert64.sys) are not next to the app; speed limits cannot work without them."),
                    "needs-admin" => L.T("برای محدودیت سرعت، دسترسی ادمین لازمه.", "Speed limits need admin rights."),
                    var other => other,
                };
                Applied = Shaper.Error is null ? plan : [];
                Replan(); // something may have changed while this was running
                NetTick?.Invoke();
            });
        });
    }

    /// <summary>Adds, changes or (when empty) removes one app's limit and applies it straight away.</summary>
    public void SetLimit(AppLimit limit)
    {
        State.NetLimits.RemoveAll(l => l.Key == limit.Key);
        if (!limit.IsEmpty) State.NetLimits.Add(limit);
        LimitsChanged();
    }

    public void ClearLimits()
    {
        State.NetLimits.Clear();
        LimitsChanged();
    }

    public void LimitsChanged()
    {
        Store.Save();
        Replan();
        NetTick?.Invoke();
    }

    private static IReadOnlyList<AppTraffic> DemoApps(DateTime now)
    {
        var wobble = 0.85 + 0.3 * Math.Abs(Math.Sin(now.Second / 9.0));
        const long mb = 1024 * 1024;
        return
        [
            new("steam", "Steam Client", "", 38.5 * wobble, 0.4, 5200 * mb, 61 * mb, now),
            new("chrome", "Google Chrome", "", 6.2 * wobble, 0.9 * wobble, 1480 * mb, 212 * mb, now),
            new("onedrive", "Microsoft OneDrive", "", 0.1, 4.6 * wobble, 96 * mb, 1900 * mb, now),
            new("discord", "Discord", "", 0.3, 0.2 * wobble, 240 * mb, 88 * mb, now),
            new("telegram", "Telegram Desktop", "", 0.02, 0.01, 130 * mb, 24 * mb, now.AddMinutes(-1)),
            new("svchost", "Host Process for Windows Services", "", 0, 0, 310 * mb, 45 * mb, now.AddMinutes(-20)),
        ];
    }

    // ---- updates ----------------------------------------------------------------------------

    private readonly DateTime _started = DateTime.Now;
    private DateTime _lastUpdateCheck = DateTime.MinValue;

    /// <summary>Half a minute after start, then every six hours; each new version is announced once.</summary>
    private void CheckForUpdates(DateTime now)
    {
        if (Demo || !Settings.AutoUpdateCheck || Updater.Feed.Length == 0) return;
        if (now - _started < TimeSpan.FromSeconds(30) || now - _lastUpdateCheck < TimeSpan.FromHours(6)) return;
        _lastUpdateCheck = now;
        _ = AnnounceUpdate();
    }

    private async Task AnnounceUpdate()
    {
        if (await Updater.CheckAsync() is not { } info) return;
        var version = info.Version.ToString(3);
        if (Settings.AnnouncedVersion == version) return;
        Settings.AnnouncedVersion = version;
        Store.Save();
        Notify(L.T("نسخه‌ی جدید شب‌پا", "New Shabpa version"), L.T($"نسخه‌ی {Fa.Digits(version)} اومده. از «به‌روزرسانی» نصبش کن.", $"Version {version} is out. Install it from Updates."));
        Bot.Notify(L.T($"🆕 نسخه‌ی {Fa.Digits(version)} شب‌پا اومده. برای نصب «به‌روزرسانی» رو بفرست.", $"🆕 Shabpa {version} is out. Send “Update” to install it."));
    }

    private string UpdateFromBot()
    {
        if (Updater.Feed.Length == 0) return L.T("منبع به‌روزرسانی تنظیم نشده.", "No update source is set.");
        if (!Elevated)
            return L.T("نصب از راه دور فقط وقتی ممکنه که شب‌پا با دسترسی ادمین اجرا شده باشه (وگرنه ویندوز روی خود سیستم تأیید می‌خواد). از توی برنامه، بخش «به‌روزرسانی»، نصبش کن.",
                "Remote install only works while Shabpa runs as administrator (otherwise Windows asks for approval on the PC). Install it from the Updates page in the app.");
        _ = Task.Run(async () =>
        {
            var info = await Updater.CheckAsync();
            if (info is null)
            {
                Bot.Notify(Updater.State == UpdateState.Error ? Updater.Error : L.T("✅ آخرین نسخه نصبه.", "✅ You already have the latest version."));
                return;
            }
            Bot.Notify(L.T($"⬇️ نسخه‌ی {Fa.Digits(info.Version.ToString(3))} پیدا شد؛ دانلود و نصب…", $"⬇️ Found {info.Version.ToString(3)}; downloading and installing…"));
            var error = await Updater.DownloadAndInstallAsync(elevated: true);
            Bot.Notify(error ?? L.T("🔄 نصب شروع شد. شب‌پا چند ثانیه بسته می‌شه و با نسخه‌ی جدید برمی‌گرده.", "🔄 Installing. Shabpa closes for a few seconds and comes back on the new version."));
        });
        return L.T("🔎 در حال بررسی به‌روزرسانی…", "🔎 Checking for updates…");
    }

    // ---- power actions ----------------------------------------------------------------------

    /// <summary>Shows the cancellable countdown, then runs the action. False if another countdown is up.</summary>
    public bool RequestAction(PowerAction action, string reason, int seconds)
    {
        if (seconds <= 0 || action == PowerAction.Lock)
        {
            Run(action);
            return true;
        }
        if (_countdown is not null) return false;

        var window = new CountdownWindow(action, reason, seconds);
        _countdown = window;
        window.Closed += (_, _) =>
        {
            _countdown = null;
            switch (window.Outcome)
            {
                case CountdownOutcome.Run:
                    Run(action);
                    break;
                case CountdownOutcome.Later:
                    Queue.Postpone(action, DateTime.Now, TimeSpan.FromMinutes(10), "postponed");
                    Store.Save();
                    Changed?.Invoke();
                    break;
            }
        };
        window.Show();
        return true;
    }

    private int RequestFromBot(PowerAction action)
    {
        if (action == PowerAction.Lock)
        {
            Run(action);
            return 0;
        }
        const int seconds = 15;
        return RequestAction(action, L.T("ربات تلگرام", "Telegram bot"), seconds) ? seconds : -1;
    }

    private void Run(PowerAction action)
    {
        Store.Save();
        var error = Executor.Execute(action);
        if (error is not null) Notify(L.T("انجام نشد", "Failed"), error);
        else if (DryRun) Notify(L.T("حالت آزمایشی", "Test mode"), L.T($"«{Fa.ActionName(action)}» فقط شبیه‌سازی شد.", $"“{Fa.ActionName(action)}” was only simulated."));
    }

    public bool CountdownActive => _countdown is not null;

    public void CancelCountdown() => _countdown?.Finish(CountdownOutcome.Cancel);

    private string CancelEverything()
    {
        var had = _countdown is not null;
        CancelCountdown();
        var n = Queue.Clear();
        var rule = Engine.Rule is not null;
        if (rule) DeactivateRule();
        Store.Save();
        Changed?.Invoke();
        if (!had && n == 0 && !rule) return L.T("چیزی برای لغو نبود.", "There was nothing to cancel.");
        return L.T("لغو شد ✅", "Cancelled ✅")
               + (n > 0 ? "\n" + L.T($"{Fa.Digits(n.ToString())} دستور از صف پاک شد.", $"{n} queued command(s) removed.") : "")
               + (rule ? "\n" + L.T("قانون خاموشی شرطی هم غیرفعال شد.", "The conditional rule was turned off too.") : "");
    }

    /// <summary>How a queued command's origin is shown; empty for ones scheduled by hand.</summary>
    public static string SourceText(string source) => source switch
    {
        "postponed" or "عقب افتاده" => L.T("عقب افتاده", "postponed"),
        _ => "",
    };

    // ---- queue / rule / wake ----------------------------------------------------------------

    public void AddCommand(PowerAction action, DateTime dueAt, IEnumerable<DayOfWeek>? repeat = null)
    {
        Queue.Add(action, dueAt, repeat);
        Store.Save();
        Changed?.Invoke();
    }

    public void RemoveCommand(Guid id)
    {
        Queue.Remove(id);
        Store.Save();
        Changed?.Invoke();
    }

    public void ActivateRule(Rule rule)
    {
        State.ActiveRule = rule.Clone();
        Engine.Activate(rule);
        Engine.Evaluate(Snapshot, DateTime.Now);
        Store.Save();
        Changed?.Invoke();
    }

    public void DeactivateRule()
    {
        State.ActiveRule = null;
        Engine.Deactivate();
        Store.Save();
        Changed?.Invoke();
    }

    /// <summary>Returns an error message or null.</summary>
    public string? AddWake(WakeSchedule schedule)
    {
        if (!Demo && WakeScheduler.Register(schedule, ExePath) is { } error) return error;
        State.WakeSchedules.Add(schedule);
        Store.Save();
        Changed?.Invoke();
        return null;
    }

    public void RemoveWake(Guid id)
    {
        if (!Demo) WakeScheduler.Unregister(id);
        State.WakeSchedules.RemoveAll(w => w.Id == id);
        Store.Save();
        Changed?.Invoke();
    }

    public DateTime? NextWake(DateTime now) =>
        State.WakeSchedules.Count == 0 ? null : State.WakeSchedules.Min(w => Schedule.Next(w.Hour, w.Minute, w.Days, now));

    // ---- misc -------------------------------------------------------------------------------

    public void Notify(string title, string text) => Notified?.Invoke(title, text);

    public void RaiseChanged() => Changed?.Invoke();

    /// <summary>One line describing what is coming up, for the sidebar and the tray tooltip.</summary>
    public string NextText()
    {
        if (Queue.Next is { } next)
            return $"{Fa.ActionName(next.Action)}{L.Comma}{Fa.RelativeDay(next.DueAt, DateTime.Now)} {Fa.Clock(next.DueAt)}";
        if (Engine.Rule is { } rule)
            return L.T($"خاموشی شرطی فعاله: {Fa.ActionName(rule.Action)}", $"Conditional rule active: {Fa.ActionName(rule.Action)}");
        return L.T("برنامه‌ای در راه نیست", "Nothing scheduled");
    }

    public string StatusText()
    {
        var s = Snapshot;
        var lines = new List<string>
        {
            L.T("🖥 سیستم روشنه", "🖥 The PC is on"),
            L.T($"⚡ مصرف: {Fa.N(s.TotalWatts, 0)} وات", $"⚡ Power: {Fa.N(s.TotalWatts, 0)} W"),
            L.T($"⬇️ {Fa.Speed(s.DownMbps)}  ⬆️ {Fa.Speed(s.UpMbps)} مگابیت", $"⬇️ {Fa.Speed(s.DownMbps)}  ⬆️ {Fa.Speed(s.UpMbps)} Mbit"),
            L.T($"🧠 پردازنده {Fa.N(s.CpuLoad, 0)}٪ · گرافیک {Fa.N(s.GpuLoad, 0)}٪", $"🧠 CPU {Fa.N(s.CpuLoad, 0)}% · GPU {Fa.N(s.GpuLoad, 0)}%"),
        };
        if (Queue.Next is { } next)
            lines.Add(L.T($"⏳ بعدی: {Fa.ActionName(next.Action)}، {Fa.RelativeDay(next.DueAt, DateTime.Now)} ساعت {Fa.Clock(next.DueAt)}",
                $"⏳ Next: {Fa.ActionName(next.Action)}, {Fa.RelativeDay(next.DueAt, DateTime.Now)} at {Fa.Clock(next.DueAt)}"));
        if (Engine.Rule is { } rule)
            lines.Add(L.T($"🎯 قانون فعال ({Fa.ActionName(rule.Action)}): {Engine.Status.Text}", $"🎯 Active rule ({Fa.ActionName(rule.Action)}): {Engine.Status.Text}"));
        if (Queue.Next is null && Engine.Rule is null) lines.Add(L.T("برنامه‌ای در راه نیست.", "Nothing scheduled."));
        return string.Join("\n", lines);
    }

    /// <summary>Sample data for --demo runs; never written to disk.</summary>
    public void FillDemo()
    {
        var now = DateTime.Now;
        var random = new Random(7);
        State.Energy.Clear();
        for (var i = 0; i < 30; i++)
        {
            var heavy = random.NextDouble() < 0.25;
            State.Energy.Add(new EnergyDay
            {
                Date = DateOnly.FromDateTime(now).AddDays(-i),
                GamingWh = (heavy ? 1800 : 500) * random.NextDouble() + (i == 0 ? 0 : 150),
                ActiveWh = 250 + 500 * random.NextDouble(),
                IdleWh = 60 + 220 * random.NextDouble(),
                MeasuredSeconds = 3600 * (4 + 9 * random.NextDouble()),
            });
        }
        DayOfWeek[] weekdays = [DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday];
        State.Queue.Clear();
        Queue.Add(PowerAction.Shutdown, Schedule.Next(1, 30, [], now), weekdays);
        Queue.Add(PowerAction.Sleep, now.AddMinutes(47));
        State.WakeSchedules.Clear();
        State.WakeSchedules.Add(new WakeSchedule { Hour = 7, Minute = 30, Days = [.. weekdays] });
        State.WakeSchedules.Add(new WakeSchedule { Hour = 10, Minute = 0, Days = [DayOfWeek.Friday] });
        State.NetLimits.Add(new AppLimit { Key = "onedrive", Title = "Microsoft OneDrive", UpMbps = 1 });
        State.NetLimits.Add(new AppLimit { Key = "steam", Title = "Steam Client", DownMbps = 20 });
    }
}
