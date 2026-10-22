using System.Text.Json;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Shabpa.Core;

namespace Shabpa;

/// <summary>Owns the long-lived services and is the one place the UI talks to.</summary>
public sealed partial class Hub
{
    public static Hub I { get; private set; } = null!;

    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private CountdownWindow? _countdown;
    private DateTime _lastSave = DateTime.Now;
    private DateTime _lastSecond = DateTime.Now;
    private List<ScheduledCommand>? _pendingMissed;
    /// <summary>What the countdown on screen belongs to: a command id, "rule" or "" (direct request).</summary>
    private string _countdownOrigin = "";

    public Store Store { get; }
    public AppState State => Store.State;
    public Settings Settings => State.Settings;
    public SensorHub Sensors { get; }
    public RuleEngine Engine { get; } = new();
    public CommandQueue Queue { get; }
    public EnergyTracker Energy { get; }
    /// <summary>The to-do list and the focus timer (phase A). Local only.</summary>
    public TaskBook Tasks { get; }
    public FocusTimer Focus { get; }
    /// <summary>Tasks or the focus timer changed.</summary>
    public event Action? TasksChanged;
    public BotLink Bot { get; }
    public IPowerExecutor Executor { get; }
    public TrafficMeter Traffic { get; }
    public PingMeter Ping { get; }
    public Shaper Shaper { get; } = new();
    public UsageHistory Usage { get; } = new();
    /// <summary>Support tickets (GitHub link or the project server).</summary>
    public SupportClient Support { get; }
    public Telemetry Telemetry { get; }
    private readonly GameSessionRecorder _gameRecorder = new();
    /// <summary>Every recorded game session (local file games.json).</summary>
    public GameLogStore GameLog { get; }
    private readonly BreakPlanner _breaks = new();
    private readonly WakeTimer _wake = new();
    private bool _holdingAwake;
    private DateTime _lastWakeCheck = DateTime.MinValue;
    private DateTime? _volumeRestoreAt;
    /// <summary>Dims the screen (null in screenshot runs).</summary>
    public DimController? Dim { get; private set; }
    /// <summary>Eye comfort, break reminders, sleep mode or the game history changed.</summary>
    public event Action? ComfortUpdated;
    public void ComfortChanged() => ComfortUpdated?.Invoke();
    /// <summary>The window drawn over games; null in screenshot runs.</summary>
    public OverlayWindow? Overlay { get; set; }
    /// <summary>Overlay settings changed (layout, colours, position, mode).</summary>
    public event Action? OverlaySettingsChanged;
    /// <summary>The overlay entered or left the "drag me" mode.</summary>
    public event Action<bool>? OverlayMoving;
    public Updater Updater { get; }
    public bool DryRun { get; }
    public bool Demo { get; }
    /// <summary>The main window is on screen (set by the window); nothing asks for live numbers while it is hidden in the tray.</summary>
    public volatile bool WindowShown;
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
        Tasks = new TaskBook(State.Tasks);
        Focus = new FocusTimer(() => Settings.Focus, State.FocusLog);
        Tasks.Changed += () =>
        {
            Store.Save();
            TasksChanged?.Invoke();
        };
        Focus.PhaseChanged += (_, _) => TasksChanged?.Invoke();
        Sensors = new SensorHub(() => Settings);
        Traffic = new TrafficMeter(_resolver.Resolve);
        Ping = new PingMeter(() => Settings.PingHost);
        Telemetry = new Telemetry(() => Sensors.Latest, () => Ping.Samples);
        Telemetry.UseBoardSensors = Settings.Overlay.BoardSensors && !demo;
        if (!demo) GpuProbe.PrefetchCard();
        Telemetry.LogWhileGameAlive = Settings.GameLog && !demo;
        Sensors.InteractiveCheck = () => WindowShown || Telemetry.HasDemand;
        GameLog = new GameLogStore(Store.Folder, readOnly: store.ReadOnly || demo);
        Updater = new Updater(() => Settings);
        if (Settings.Support.DeviceId.Length != 32) Settings.Support.DeviceId = Guid.NewGuid().ToString("N");
        Support = new SupportClient(() => Settings.Support, () => Settings.ProxyUrl);
        Bot = new BotLink(() => Settings, Store.Save, new BotHost
        {
            Execute = (type, args) => OnUi(() => ExecuteRemote(type, args)),
        }, outboxFile: demo ? null : System.IO.Path.Combine(Store.Folder, "bot-outbox.json"));
    }

    private static T OnUi<T>(Func<T> f) => Application.Current.Dispatcher.Invoke(f);

    /// <summary>The live noise suppressor (off until the person starts it).</summary>
    public VoiceFilter Voice { get; } = new();

    /// <summary>Starts the noise suppressor with the saved choices. Returns an error text, or null.</summary>
    public string? StartVoice()
    {
        var v = Settings.Voice;
        int? Find(List<AudioDevice> list, string name) => name.Length == 0 ? null : list.FirstOrDefault(d => d.Name == name)?.Index;
        var inputs = VoiceFilter.Inputs();
        var outputs = VoiceFilter.Outputs();
        return Voice.Start(Find(inputs, v.Input), Find(outputs, v.Output), v.Strength);
    }

    public void StartVoiceIfWanted()
    {
        if (!DryRun && !Demo && Settings.Voice.AutoStart) StartVoice();
    }

    public void Start()
    {
        PrimeBirthday();
        StartVoiceIfWanted();
        if (State.ActiveRule is { } rule) Engine.Activate(rule);

        // Whatever came due while Shabpa was not running (PC off or asleep) is gone, not "late": a PC that
        // was switched off at 01:00 must not be shut down again at 01:30 because someone turned it back on.
        var startupMissed = Queue.DropOverdue(DateTime.Now);
        if (startupMissed.Count > 0)
        {
            Store.Save();
            _pendingMissed = startupMissed;
        }

        if (!Demo) GameLog.Load();
        Traffic.Load(State.NetUsage, DateTime.Now);
        if (Demo) Usage.FillDemo(DateTime.Now);
        else Usage.Load(UsageFile);
        if (!Demo)
        {
            // Starting the Windows trace session takes a moment (longer when a crashed run left one behind): off the window's thread.
            // Until it answers, "per-app measurement is not running" is simply the state, as it is without admin rights.
            if (!Elevated) NetError = "needs-admin";
            else _ = Task.Run(() =>
            {
                var monitor = NetMonitor.TryStart(Traffic, out var netError);
                if (Volatile.Read(ref _down)) { monitor?.Dispose(); return; } // the app closed while the trace session was starting: nobody else would stop it
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    if (Volatile.Read(ref _down)) { monitor?.Dispose(); return; }
                    _net = monitor;
                    NetError = netError;
                    NetTick?.Invoke();
                });
            });
        }

        // Only the newest reading is handed to the window: when it is busy (a long layout, a slow disk) the readings must not
        // pile up in its queue and then all run back to back, which is how one short stall turns into a long one.
        Sensors.Updated += s =>
        {
            Volatile.Write(ref _pendingSensors, s);
            if (Interlocked.Exchange(ref _sensorsQueued, 1) == 1) return;
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                Interlocked.Exchange(ref _sensorsQueued, 0);
                if (Volatile.Read(ref _pendingSensors) is { } latest) OnSensors(latest);
            });
        };
        Sensors.Start();
        if (!Demo)
        {
            Telemetry.Updated += OnTelemetryForReport;
            Telemetry.Start();
        }
        if (!Demo)
        {
            Dim = new DimController(this);
            Dim.Apply();
            AssistantKey = new AssistantHotkey();
            AssistantKey.Apply(Settings.AssistantKey);
            // Sleep mode lowered the volume and the PC was switched off before it could be put back.
            if (Settings.Comfort.PendingVolume is { } pending)
            {
                MasterVolume.SetLater(pending);
                Settings.Comfort.PendingVolume = null;
                Store.Save();
            }
        }
        if (!Demo && Settings.Comfort.PendingPowerPlan.Length > 0) RestorePowerPlan();
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

    private bool _down;

    public void Shutdown()
    {
        Volatile.Write(ref _down, true);
        Voice.Stop();
        _clock.Stop();
        if (_sleepStart is not null && _sleepVolume is { } startVolume)
        {
            MasterVolume.CancelLater(); // a queued ramp step must not land after the restore and leave the sound low
            MasterVolume.SetBounded(startVolume, 800);
            Settings.Comfort.PendingVolume = null;
        }
        Dim?.Dispose();
        AssistantKey?.Dispose();
        _wake.Dispose();
        Sensors.Dispose();
        Telemetry.Dispose();
        if (_gameRecorder.Flush(DateTime.Now) is { } last) GameLog.Add(last);
        Bot.Dispose();
        Ping.Dispose();
        _net?.Dispose();
        WaitForPowerPlan(3000); // the powercfg worker records the previous plan; the file must have it before the save below
        // Limits only exist inside this process: stopping the shaper restores normal traffic.
        Shaper.Dispose();
        Store.Save();
        if (!Store.ReadOnly && !Demo) Usage.Save(UsageFile);
    }

    private string UsageFile => System.IO.Path.Combine(Store.Folder, "usage.json");

    private void OnSecond()
    {
        var now = DateTime.Now;
        if (!Demo)
        {
            RunTasks(now);
            RunBackups(now);
            RunTriggers(now);
            if (!_appStartRan && now - _startedAt > TimeSpan.FromSeconds(20))
            {
                _appStartRan = true;
                foreach (var trigger in State.Triggers.Where(t => t.Enabled && t.Kind == TriggerKind.AppStart).ToList())
                    if (State.Profiles.FirstOrDefault(p => p.Id == trigger.ProfileId) is { } profile && !Core.Profiles.HasPower(profile))
                    {
                        trigger.LastRun = now;
                        RunProfile(profile, "start");
                    }
            }
            if (!_startupChecked && now - _startedAt > TimeSpan.FromSeconds(45))
            {
                _startupChecked = true;
                // the registry and the startup folders are read on a worker; the comparison and the notices run back here
                _ = Task.Run(() => Health.Startup(new WindowsRegistryProbe(), Health.StartupFolders())).ContinueWith(t =>
                {
                    if (t.IsFaulted) { Log.Write($"startup watch: {t.Exception?.GetBaseException().GetType().Name}"); return; }
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        try { CheckStartup(announce: true, t.Result); }
                        catch (Exception ex) { Log.Write($"startup watch: {ex.GetType().Name}: {ex.Message}"); }
                    });
                }, TaskScheduler.Default);
            }
        }
        var missed = new List<ScheduledCommand>();
        if (_pendingMissed is not null)
        {
            missed.AddRange(_pendingMissed);
            _pendingMissed = null;
        }
        // The one-second timer stood still for a while: the PC was asleep or hibernating. What came due
        // meanwhile is skipped; waking the PC up is not a request to shut it down.
        if (now - _lastSecond > TimeSpan.FromSeconds(15)) missed.AddRange(Queue.DropMissed(_lastSecond, now));
        _lastSecond = now;
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
            if (!RequestAction(cmd.Action, L.T("دستور زمان‌بندی‌شده", "Scheduled command"), Settings.CountdownSeconds, cmd.Id.ToString()))
                Queue.Postpone(cmd.Action, now, TimeSpan.FromMinutes(1), cmd.Source);
        }

        CheckBirthday(Core.Birthday.Now());
        CheckForUpdates(now);
        CheckBudget(now);
        RunDownloadPlans(now);
        RunComfort(now);

        if (now - _lastSave > TimeSpan.FromMinutes(1))
        {
            _lastSave = now;
            // On the UI thread on purpose: everything that mutates the state lives here too.
            Store.Save();
            if (!Store.ReadOnly && !Demo) Usage.Save(UsageFile);
        }
        Second?.Invoke();
    }

    private SensorSnapshot? _pendingSensors;
    private int _sensorsQueued;

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
            Bot.Notify("warning", L.T($"شرط برقرار شد: {status.Text}\n{Fa.ActionSentence(rule.Action)}.", $"Condition met: {status.Text}\nNow {Fa.ActionSentence(rule.Action)}."));
            RequestAction(rule.Action, L.T("خاموشی شرطی", "Conditional rule"), Settings.CountdownSeconds, "rule");
        }
        Tick?.Invoke(s);
    }

    // ---- overlay ----------------------------------------------------------------------------

    /// <summary>Call after changing <see cref="Settings.Overlay"/>: saves, redraws the overlay and tells the settings page.</summary>
    public void OverlayChanged()
    {
        Store.Save();
        Overlay?.Evaluate();
        OverlaySettingsChanged?.Invoke();
    }

    public void OverlayMoveChanged(bool moving) => OverlayMoving?.Invoke(moving);

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

    // ---- game reports and data budget -----------------------------------------------------------

    private void OnTelemetryForReport(TelemetrySample sample)
    {
        var key = Settings.GameLog ? sample.GameKey : "";
        Telemetry.LogWhileGameAlive = Settings.GameLog;
        if (key.Length == 0 && !_gameRecorder.Active) return;
        var now = sample.Time;
        FinishedSession? done;
        if (key.Length > 0 && !_gameRecorder.Wants(now) && string.Equals(key, _gameRecorder.Key, StringComparison.OrdinalIgnoreCase)) done = null;
        else
        {
            long down = 0, up = 0;
            var known = false;
            if (key.Length > 0)
            {
                var apps = Apps;
                for (var i = 0; i < apps.Count; i++)
                    if (apps[i].Key == key) { (down, up, known) = (apps[i].DownBytes, apps[i].UpBytes, true); break; }
            }
            done = _gameRecorder.Feed(new RecorderInput(now, key, sample.GameName, sample.CpuLoad, sample.GpuLoad ?? 0, sample.CpuTempC, sample.Gpu.TempC,
                sample.RamPercent, sample.DownMbps, sample.UpMbps, sample.Fps, down, up, known));
        }
        if (key.Length > 0 && _gameRecorder.TryBeginProcessLook(now))
            _ = Task.Run(async () => _gameRecorder.AddProcessLook(key, await ProcessProbe.Measure(key).ConfigureAwait(false)));
        if (done is not null) FinishSession(done);
    }

    private void FinishSession(FinishedSession done)
    {
        GameLog.Add(done);
        Application.Current?.Dispatcher.BeginInvoke(() => AnnounceSession(done));
    }

    /// <summary>The summary shown when a game ends: time, internet, temperatures and peak load.</summary>
    public static string DescribeSession(FinishedSession f) => GameMessage.Plain(f);

    private void AnnounceSession(FinishedSession f)
    {
        var r = f.Record;
        var fps = r.Stat(GameMetric.Fps);
        State.GameHistory.Add(new GameLogEntry
        {
            Game = f.Name, Start = r.Start, Minutes = r.Seconds / 60.0, AvgFps = fps?.Avg ?? 0, MaxGpuTemp = r.Stat(GameMetric.GpuTemp)?.Max ?? 0,
            AvgGpuLoad = r.Stat(GameMetric.GpuLoad)?.Avg ?? 0,
        });
        if (State.GameHistory.Count > 60) State.GameHistory.RemoveRange(0, State.GameHistory.Count - 60);
        Store.Save();
        ComfortUpdated?.Invoke();
        if (!Settings.GameReports) { Log.Write($"game report {f.Name}: skipped (game reports are off in Settings)"); return; }
        var text = GameMessage.Plain(f);
        Notify(L.T("گزارش بازی", "Game report"), text, "playtime:" + f.Key);
        Bot.Notify("game", text, html: GameMessage.Html(f));
    }

    /// <summary>The page the last notice leads to when clicked ("playtime:csgo2"), or null.</summary>
    public string? NoticeTarget { get; private set; }
    public event Action<string>? OpenPage;
    public void OpenNoticeTarget()
    {
        if (NoticeTarget is { } t) OpenPage?.Invoke(t);
    }
    public void RequestPage(string key) => OpenPage?.Invoke(key);

    public static string DescribeReport(GameReport r)
    {
        var lines = new List<string>
        {
            L.T($"🎮 {r.Game} · {Fa.Duration(r.Duration)}", $"🎮 {r.Game} · {Fa.Duration(r.Duration)}"),
        };
        if (r.HasFrames)
            lines.Add(L.T($"فریم بر ثانیه: میانگین {Fa.N(r.AvgFps, 0)}، ۱٪ پایین {Fa.N(r.Low1Fps, 0)}", $"FPS: average {Fa.N(r.AvgFps, 0)}, 1% low {Fa.N(r.Low1Fps, 0)}"));
        if (r.AvgPing > 0)
            lines.Add(L.T($"پینگ: میانگین {Fa.N(r.AvgPing, 0)}، بیشترین {Fa.N(r.MaxPing, 0)} میلی‌ثانیه", $"Ping: average {Fa.N(r.AvgPing, 0)}, worst {Fa.N(r.MaxPing, 0)} ms"));
        if (r.MaxGpuTemp > 0)
            lines.Add(L.T($"دمای کارت گرافیک: تا {Fa.N(r.MaxGpuTemp, 0)}°" + (r.MaxHotspot > 0 ? $" (هات‌اسپات {Fa.N(r.MaxHotspot, 0)}°)" : ""),
                $"GPU temperature: up to {Fa.N(r.MaxGpuTemp, 0)}°" + (r.MaxHotspot > 0 ? $" (hotspot {Fa.N(r.MaxHotspot, 0)}°)" : "")));
        if (r.DownBytes + r.UpBytes > 1_000_000)
            lines.Add(L.T($"اینترنت بازی: ↓{Fa.Bytes(r.DownBytes)} ↑{Fa.Bytes(r.UpBytes)}", $"Game traffic: ↓{Fa.Bytes(r.DownBytes)} ↑{Fa.Bytes(r.UpBytes)}"));
        return string.Join("\n", lines);
    }

    private DateTime _lastBudgetCheck = DateTime.MinValue;

    /// <summary>Once a minute: compare this month's use with the limit and warn at 80 % and 100 %, once each.</summary>
    private void CheckBudget(DateTime now)
    {
        if (Settings.MonthlyLimitGb <= 0 || Demo || now - _lastBudgetCheck < TimeSpan.FromMinutes(1)) return;
        _lastBudgetCheck = now;
        var from = DateOnly.FromDateTime(Fa.MonthStart(now));
        var (down, up) = Usage.Total(from, DateOnly.FromDateTime(now));
        var check = DataBudget.Evaluate(down + up, Settings.MonthlyLimitGb);
        if (check.Level == 0) return;
        var month = from.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var lastLevel = Settings.BudgetWarned.StartsWith(month + ":", StringComparison.Ordinal) && int.TryParse(Settings.BudgetWarned[(month.Length + 1)..], out var parsed) ? parsed : 0;
        if (check.Level <= lastLevel) return;
        Settings.BudgetWarned = $"{month}:{check.Level}";
        Store.Save();
        var text = check.Level == 2
            ? L.T($"سقف اینترنت ماهانه‌ات ({Fa.N(Settings.MonthlyLimitGb, 0)} گیگ) تموم شد.", $"You reached your monthly internet allowance ({Fa.N(Settings.MonthlyLimitGb, 0)} GB).")
            : L.T($"{Fa.N(check.Fraction * 100, 0)}٪ از سقف اینترنت ماهانه‌ات مصرف شده ({Fa.N(check.UsedGb, 1)} از {Fa.N(Settings.MonthlyLimitGb, 0)} گیگ).",
                $"{Fa.N(check.Fraction * 100, 0)}% of your monthly allowance is used ({Fa.N(check.UsedGb, 1)} of {Fa.N(Settings.MonthlyLimitGb, 0)} GB).");
        Notify(L.T("حجم اینترنت", "Data allowance"), text);
        Bot.Notify("budget", "📶 " + text);
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
        Bot.Notify("update", L.T($"🆕 نسخه‌ی {Fa.Digits(version)} شب‌پا اومده.", $"🆕 Shabpa {version} is out."));
    }

    // ---- comfort: dimming, break reminders, sleep mode, waking for downloads ----------------------

    private DateTime? _sleepStart;
    private TimeSpan _sleepLength;
    private double? _sleepVolume;
    private double _sleepDim;

    public bool SleepActive => _sleepStart is not null;
    public DateTime? SleepEndsAt => _sleepStart is { } s ? s + _sleepLength : null;

    /// <summary>Begins fading the volume and the screen out; the chosen power action runs at the end.</summary>
    public void StartSleepMode()
    {
        if (SleepActive) return;
        var c = Settings.Comfort;
        _sleepLength = TimeSpan.FromMinutes(Math.Clamp(c.SleepMinutes, 1, 600));
        _sleepStart = DateTime.Now;
        _sleepVolume = c.SleepVolume ? MasterVolume.GetBounded() : null;
        _sleepDim = Dim is { Active: true } ? c.DimLevel : 0.05;
        if (_sleepVolume is { } v)
        {
            c.PendingVolume = v;
            Store.Save();
        }
        ComfortUpdated?.Invoke();
    }

    public void CancelSleepMode()
    {
        if (!SleepActive) return;
        _sleepStart = null;
        if (_sleepVolume is { } v)
        {
            MasterVolume.SetLater(v);
            Settings.Comfort.PendingVolume = null;
            Store.Save();
        }
        _sleepVolume = null;
        if (Dim is not null)
        {
            Dim.Override = null;
            Dim.Apply();
        }
        ComfortUpdated?.Invoke();
    }

    // ---- backups (phase B) -------------------------------------------------------------------------

    private CancellationTokenSource? _backupCts;
    private readonly DateTime _startedAt = DateTime.Now;
    private DateTime _lastBackupCheck = DateTime.MinValue;
    public Guid? BackupRunningId { get; private set; }
    public int BackupDone { get; private set; }
    public int BackupTotal { get; private set; }
    public event Action? BackupsChanged;
    public void RaiseBackupsChanged() => BackupsChanged?.Invoke();

    public void CancelBackup() => _backupCts?.Cancel();

    /// <summary>Runs one job in the background and records the result. False if a backup is already running.</summary>
    public bool StartBackup(BackupJob job)
    {
        if (BackupRunningId is not null) return false;
        BackupRunningId = job.Id;
        BackupDone = BackupTotal = 0;
        _backupCts = new CancellationTokenSource();
        var token = _backupCts.Token;
        BackupsChanged?.Invoke();
        _ = Task.Run(() =>
        {
            BackupResult result;
            try
            {
                var plan = Backups.Plan(job, token);
                result = Backups.Run(job, plan, token, (done, total) =>
                {
                    BackupDone = done;
                    BackupTotal = total;
                    Application.Current?.Dispatcher.BeginInvoke(() => BackupsChanged?.Invoke());
                });
            }
            catch (OperationCanceledException)
            {
                result = new BackupResult(false, 0, 0, [], 0, [], L.T("متوقف شد.", "Stopped."));
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                Log.Write($"backup: {ex.GetType().Name}: {ex.Message}");
                result = new BackupResult(false, 0, 0, [], 0, [], L.T("مقصد در دسترس نیست.", "The destination is not available."));
            }
            Application.Current?.Dispatcher.BeginInvoke(() => FinishBackup(job, result));
        });
        return true;
    }

    private void FinishBackup(BackupJob job, BackupResult result)
    {
        BackupRunningId = null;
        _backupCts?.Dispose();
        _backupCts = null;
        job.LastRun = DateTime.Now;
        var note = result.Note.Length > 0 ? result.Note
            : result.Ok ? ""
            : L.T($"{Fa.Digits(result.Failed.Count.ToString())} فایل کپی نشد، {Fa.Digits(result.Corrupt.Count.ToString())} فایل تأیید نشد.",
                $"{result.Failed.Count} file(s) not copied, {result.Corrupt.Count} not verified.");
        job.History.Add(new BackupRunLog { Time = DateTime.Now, Ok = result.Ok, Files = result.Copied, Bytes = result.Bytes, Note = note });
        if (job.History.Count > 10) job.History.RemoveRange(0, job.History.Count - 10);
        Store.Save();
        BackupsChanged?.Invoke();
        Notify(L.T("پشتیبان‌گیری", "Backup"),
            result.Ok
                ? L.T($"«{job.Name}»: {Fa.Digits(result.Copied.ToString())} فایل ({Fa.Bytes(result.Bytes)}) کپی و تأیید شد.", $"“{job.Name}”: {result.Copied} file(s) ({Fa.Bytes(result.Bytes)}) copied and verified.")
                : L.T($"«{job.Name}» کامل نشد. {note}", $"“{job.Name}” did not finish. {note}"));
    }

    private void RunBackups(DateTime now)
    {
        if (now - _lastBackupCheck < TimeSpan.FromSeconds(30)) return;
        _lastBackupCheck = now;
        // not right after start (the PC is busy booting), not while a game runs, one at a time
        if (BackupRunningId is not null || now - _startedAt < TimeSpan.FromMinutes(2) || Snapshot.GameRunning) return;
        var job = State.Backups.FirstOrDefault(j => j.IsDue(now) && Backups.Problem(j) is null);
        if (job is not null) StartBackup(job);
    }

    // ---- profiles and automation (phase D) ------------------------------------------------------------

    public event Action? ProfilesChanged;
    public void RaiseProfilesChanged() => ProfilesChanged?.Invoke();

    private sealed class HubProfileHost(Hub hub) : IProfileHost
    {
        public void SetDim(bool on, int percent)
        {
            var c = hub.Settings.Comfort;
            c.DimOn = on;
            if (on) c.DimLevel = Math.Clamp(percent / 100.0, 0.05, 0.92);
            hub.Store.Save();
            hub.Dim?.Apply();
            hub.ComfortChanged();
        }

        public void SetVolume(int percent) => MasterVolume.SetLater(percent / 100.0);

        public void CleanRam() => hub.CleanMemory(cache: false);

        public void StartFocus(int minutes)
        {
            hub.Settings.Focus.FocusMinutes = minutes;
            hub.Store.Save();
            hub.Focus.Start(DateTime.Now);
        }

        public void SetBreaks(bool on)
        {
            hub.Settings.Comfort.BreakOn = on;
            hub.Store.Save();
            hub.ComfortChanged();
        }

        public void StartSleepMode(int minutes)
        {
            hub.Settings.Comfort.SleepMinutes = minutes;
            hub.Store.Save();
            hub.StartSleepMode();
        }

        public bool SchedulePower(PowerAction action, int minutes) => hub.AddCommand(action, DateTime.Now.AddMinutes(minutes));

        public void Notify(string text) => hub.Notify(L.T("پروفایل", "Profile"), text);
    }

    private (bool dimOn, double dimLevel, double? volume, bool breaks, string profile)? _profileUndo;

    /// <summary>The starter profiles, added the first time the profiles page is opened.</summary>
    public void EnsureProfiles()
    {
        if (Settings.ProfilesSeeded) return;
        Settings.ProfilesSeeded = true;
        if (State.Profiles.Count == 0) State.Profiles.AddRange(Core.Profiles.Defaults());
        Store.Save();
    }

    public bool CanUndoProfile => _profileUndo is not null;

    /// <summary>Runs a profile. <paramref name="source"/> says why: "manual", "time", "game", "start".</summary>
    public ProfileOutcome RunProfile(WorkProfile profile, string source)
    {
        _profileUndo = (Settings.Comfort.DimOn, Settings.Comfort.DimLevel, MasterVolume.GetBounded(), Settings.Comfort.BreakOn, profile.Name);
        var outcome = Core.Profiles.Run(profile, new HubProfileHost(this));
        State.RunLog.Add(new RunLogEntry { Time = DateTime.Now, Profile = profile.Name, Source = source, Ok = outcome.Ok, Steps = [.. outcome.Steps] });
        if (State.RunLog.Count > 100) State.RunLog.RemoveRange(0, State.RunLog.Count - 100);
        Store.Save();
        ProfilesChanged?.Invoke();
        return outcome;
    }

    /// <summary>Puts the screen, volume and break reminders back as they were before the last profile.</summary>
    public bool UndoProfile()
    {
        if (_profileUndo is not { } u) return false;
        _profileUndo = null;
        var c = Settings.Comfort;
        c.DimOn = u.dimOn;
        c.DimLevel = u.dimLevel;
        c.BreakOn = u.breaks;
        if (u.volume is { } v) MasterVolume.SetLater(v);
        Store.Save();
        Dim?.Apply();
        ComfortChanged();
        State.RunLog.Add(new RunLogEntry { Time = DateTime.Now, Profile = u.profile, Source = "undo", Steps = [L.T("برگردوندن به حالت قبل", "Restored the previous state")] });
        ProfilesChanged?.Invoke();
        return true;
    }

    private bool _gameWas;
    private DateTime _lastTriggerCheck = DateTime.MinValue;

    /// <summary>Game mode is on and a game is running: Shabpa's own notices stay quiet.</summary>
    public bool GameModeActive { get; private set; }

    /// <summary>Called when the "a game is running" flag flips (and once at start to undo a leftover from a crash).</summary>
    internal void GameModeStep(bool game)
    {
        var c = Settings.Comfort;
        if (game && c.GameModeOn && !GameModeActive)
        {
            GameModeActive = true;
            if (c.GameModePower && c.PendingPowerPlan.Length == 0) SwitchPowerPlanInBackground();
            if (c.GameModeRam) CleanMemory(cache: false);
            Notices.Add((DateTime.Now, L.T("حالت بازی", "Game mode"), L.T("روشن شد.", "On.")));
        }
        else if (!game && GameModeActive || !c.GameModeOn && GameModeActive)
        {
            GameModeActive = false;
            RestorePowerPlan();
            Notices.Add((DateTime.Now, L.T("حالت بازی", "Game mode"), L.T("تموم شد؛ همه‌چی برگشت.", "Over; everything is back.")));
        }
    }

    // powercfg is a console program (several launches, up to 8 s each on a slow PC), so the power plan is changed on a
    // worker, one step after the other: a "restore" queued behind a "switch" always sees what the switch remembered.
    private readonly object _planGate = new();
    private Task _planChain = Task.CompletedTask;
    private int _planSteps;
    private bool PlanBusy => Volatile.Read(ref _planSteps) > 0;

    private void QueuePlanStep(Action step)
    {
        Interlocked.Increment(ref _planSteps);
        lock (_planGate)
            _planChain = _planChain.ContinueWith(_ =>
            {
                try { step(); }
                catch (Exception ex) { Log.Write($"power plan: {ex.GetType().Name}: {ex.Message}"); }
                finally { Interlocked.Decrement(ref _planSteps); }
            }, TaskScheduler.Default);
    }

    /// <summary>Test aid: waits until the queued power plan steps are done.</summary>
    internal void WaitForPowerPlan(int milliseconds = 30000)
    {
        Task last;
        lock (_planGate) last = _planChain;
        last.Wait(milliseconds);
    }

    private void SwitchPowerPlanInBackground()
    {
        var c = Settings.Comfort;
        QueuePlanStep(() =>
        {
            if (c.PendingPowerPlan.Length != 0) return;
            var before = PowerPlan.SwitchToPerformance();
            if (before is null) return;
            c.PendingPowerPlan = before; // kept on disk so it is put back even if the PC restarts mid-game
            Application.Current?.Dispatcher.BeginInvoke(() => Store.Save());
        });
    }

    private void RestorePowerPlan()
    {
        var c = Settings.Comfort;
        if (c.PendingPowerPlan.Length == 0 && !PlanBusy) return;
        QueuePlanStep(() =>
        {
            var plan = c.PendingPowerPlan;
            if (plan.Length == 0) return;
            PowerPlan.Set(plan);
            c.PendingPowerPlan = "";
            Application.Current?.Dispatcher.BeginInvoke(() => Store.Save());
        });
    }

    private void RunTriggers(DateTime now)
    {
        // game start / end: edges of the "a game is running" flag
        var game = Snapshot.GameRunning;
        var edge = game != _gameWas;
        _gameWas = game;
        if (edge || GameModeActive) GameModeStep(game);
        var timeCheck = now - _lastTriggerCheck >= TimeSpan.FromSeconds(20);
        if (!edge && !timeCheck) return;
        if (timeCheck) _lastTriggerCheck = now;
        var fired = false;
        foreach (var trigger in State.Triggers.ToList())
        {
            var fire = trigger.Kind switch
            {
                TriggerKind.Time => timeCheck && trigger.IsDue(now),
                TriggerKind.GameStart => edge && game && trigger.Enabled,
                TriggerKind.GameEnd => edge && !game && trigger.Enabled,
                _ => false,
            };
            if (!fire) continue;
            fired = true;
            var profile = State.Profiles.FirstOrDefault(p => p.Id == trigger.ProfileId);
            trigger.LastRun = now;
            if (profile is null) continue;
            // a profile that can switch the PC off never starts by itself
            if (Core.Profiles.HasPower(profile))
            {
                Notify(L.T("اتوماسیون", "Automation"), L.T($"«{profile.Name}» شامل خاموشی یا خواب‌ه و خودکار اجرا نمی‌شه؛ دستی اجراش کن.", $"“{profile.Name}” can switch the PC off, so it does not run by itself; run it by hand."));
                continue;
            }
            var outcome = RunProfile(profile, trigger.Kind switch { TriggerKind.Time => "time", TriggerKind.AppStart => "start", _ => "game" });
            Notify(L.T("اتوماسیون", "Automation"), L.T($"پروفایل «{profile.Name}» اجرا شد.", $"Profile “{profile.Name}” ran.") + (outcome.Ok ? "" : L.T(" بعضی کارها انجام نشد.", " Some steps did not work.")));
        }
        if (fired) Store.Save(); // not every 20 s for nothing: that was a file write on the window's thread
    }

    private bool _appStartRan;

    // ---- startup watch (phase E) -------------------------------------------------------------------

    private bool _startupChecked;

    /// <summary>The first time ever, the current startup list becomes the baseline silently. After that, every new or
    /// changed entry is announced once.</summary>
    public List<StartupChange> CheckStartup(bool announce, List<StartupItem>? read = null)
    {
        var current = read ?? Health.Startup(new WindowsRegistryProbe(), Health.StartupFolders());
        if (State.StartupBaseline is null)
        {
            State.StartupBaseline = current.ToDictionary(i => i.Key, i => i.Command);
            Store.Save();
            return [];
        }
        var changes = Health.Compare(current, State.StartupBaseline).Where(c => c.Kind != "removed").ToList();
        if (announce)
        {
            foreach (var change in changes.Where(c => !State.StartupAnnounced.Contains(c.Item.Key + "|" + c.Item.Command)))
            {
                State.StartupAnnounced.Add(change.Item.Key + "|" + change.Item.Command);
                var notes = Health.Notes(change.Item);
                Notify(L.T("برنامه‌ی جدید توی استارتاپ", "New startup program"),
                    L.T($"«{change.Item.Name}» " + (change.Kind == "new" ? "به برنامه‌های شروع ویندوز اضافه شد." : "تغییر کرده."), $"“{change.Item.Name}” " + (change.Kind == "new" ? "was added to the programs that start with Windows." : "was changed."))
                    + (notes.Count > 0 ? Environment.NewLine + notes[0] : ""));
            }
            if (State.StartupAnnounced.Count > 200) State.StartupAnnounced.RemoveRange(0, State.StartupAnnounced.Count - 200);
            Store.Save();
        }
        return changes;
    }

    public void AcceptStartup()
    {
        State.StartupBaseline = Health.Startup(new WindowsRegistryProbe(), Health.StartupFolders()).ToDictionary(i => i.Key, i => i.Command);
        Store.Save();
    }

    private DateTime _lastTaskCheck = DateTime.MinValue;

    /// <summary>Reminders for tasks, and the end of focus and break phases.</summary>
    private void RunTasks(DateTime now)
    {
        if (Focus.Tick(now) is { } ended)
        {
            Store.Save();
            Notify(
                ended == FocusPhase.Focus
                    ? Friendly.Notice(Settings.Birthday.Name, "تمرکزت تموم شد 🎉", "Focus session done 🎉", "آفرین {name}! تمرکزت تموم شد 🎉", "Nice one, {name}! Focus session done 🎉")
                    : L.T("استراحت تموم شد", "Break is over"),
                ended == FocusPhase.Focus
                    ? (Focus.Phase == FocusPhase.LongBreak ? L.T("آفرین! یه استراحت بلند بکن.", "Well done! Take a long break.") : L.T("آفرین! چند دقیقه استراحت کن.", "Well done! Take a short break."))
                    : (Focus.Phase == FocusPhase.Focus ? L.T("وقت دور بعدی تمرکزه.", "Time for the next focus round.") : L.T("هر وقت آماده بودی دوباره شروع کن.", "Start again whenever you are ready.")));
            try { System.Media.SystemSounds.Asterisk.Play(); } catch (Exception ex) { Log.Write($"focus sound: {ex.Message}"); }
        }
        if (now - _lastTaskCheck < TimeSpan.FromSeconds(10)) return;
        _lastTaskCheck = now;
        if (!Settings.TaskReminders) return;
        foreach (var task in Tasks.TakeDueReminders(now))
        {
            var when = task.Due is { } due ? L.T($"موعد: {Fa.Clock(due)}", $"Due: {due:HH:mm}") : "";
            Notify(L.T("یادآور کار", "Task reminder"), task.Title + (when.Length > 0 ? Environment.NewLine + when : ""));
        }
    }

    private void RunComfort(DateTime now)
    {
        if (Demo) return;
        var c = Settings.Comfort;
        Dim?.Apply();

        // break reminders: only time the person is really at the PC counts
        var active = UserIdle.Since() < TimeSpan.FromMinutes(2);
        if (_breaks.Tick(now, active, Snapshot.GameRunning, c, L.En) is { } nudge)
            Notify(Friendly.Notice(Settings.Birthday.Name, "وقت یه استراحته", "Break time", "{name}، وقت یه استراحته", "{name}, time for a break"), nudge);

        // sleep mode: volume and screen fade out, then the power action
        if (_sleepStart is { } start)
        {
            var progress = SleepRamp.Progress(start, _sleepLength, now);
            if (_sleepVolume is { } volume) MasterVolume.SetLater(SleepRamp.Volume(volume, progress));
            if (c.SleepDim && Dim is not null)
            {
                Dim.Override = SleepRamp.Dim(_sleepDim, progress);
                Dim.Apply();
            }
            if (progress >= 1)
            {
                var action = c.SleepAction;
                if (RequestAction(action, L.T("حالت شب‌خواب", "Sleep mode"), Settings.CountdownSeconds, ""))
                {
                    _sleepStart = null;
                    if (_sleepVolume is not null) _volumeRestoreAt = now.AddSeconds(Settings.CountdownSeconds + 60);
                    _sleepVolume = null;
                    // After a lock or sleep the app keeps running; the dim cover is lifted then too.
                    if (Dim is not null && action is PowerAction.Lock or PowerAction.Sleep or PowerAction.Hibernate)
                    {
                        Dim.Override = null;
                        Dim.Apply();
                    }
                    ComfortUpdated?.Invoke();
                }
            }
        }
        if (_volumeRestoreAt is { } at && now >= at && c.PendingVolume is { } pending)
        {
            MasterVolume.SetLater(pending);
            c.PendingVolume = null;
            _volumeRestoreAt = null;
            Store.Save();
        }

        // keep the PC awake around a scheduled download, and wake it up for the next one
        var soon = State.DownloadPlans.Any(p => p.State == PlanState.Running || (p.State == PlanState.Waiting && Math.Abs((p.StartAt - now).TotalMinutes) < 3));
        if (soon != _holdingAwake)
        {
            _holdingAwake = soon;
            Native.SetThreadExecutionState(Native.ES_CONTINUOUS | (soon ? Native.ES_SYSTEM_REQUIRED : 0));
        }
        if (now - _lastWakeCheck > TimeSpan.FromSeconds(30))
        {
            _lastWakeCheck = now;
            if (NextWakeForDownloads(now) is { } wake) _wake.Set(wake);
            else _wake.Clear();
        }
    }

    /// <summary>When the PC should wake up for the next scheduled download: one minute before it is due.</summary>
    public DateTime? NextWakeForDownloads(DateTime now)
    {
        DateTime? best = null;
        foreach (var plan in State.DownloadPlans)
        {
            if (plan.State != PlanState.Waiting || !plan.WakeUp) continue;
            var at = plan.StartAt.AddMinutes(-1);
            if (at <= now.AddSeconds(5)) continue;
            if (best is null || at < best) best = at;
        }
        return best;
    }

    // ---- scheduled launcher downloads ----------------------------------------------------------

    /// <summary>False when the same launcher is already planned for that minute.</summary>
    public bool AddDownloadPlan(LauncherInfo launcher, DateTime at, PowerAction action, int quietMinutes)
    {
        if (State.DownloadPlans.Any(p => p.LauncherKey == launcher.Key && Math.Abs((p.StartAt - at).TotalMinutes) < 1)) return false;
        State.DownloadPlans.Add(new DownloadPlan
        {
            LauncherKey = launcher.Key, LauncherName = launcher.Name, ExePath = launcher.ExePath,
            StartAt = at, Action = action, QuietMinutes = Math.Clamp(quietMinutes, 1, 120),
        });
        Store.Save();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Adds a fully customised plan (repeat days, thresholds, open-only…). False when an identical one exists.</summary>
    public bool AddDownloadPlan(DownloadPlan plan)
    {
        if (State.DownloadPlans.Any(p => p.LauncherKey == plan.LauncherKey && Math.Abs((p.StartAt - plan.StartAt).TotalMinutes) < 1)) return false;
        State.DownloadPlans.Add(plan);
        Store.Save();
        Changed?.Invoke();
        return true;
    }

    /// <summary>A finished, missed or abandoned plan: repeating plans wait for their next day, one-off plans go away.</summary>
    private void EndPlan(DownloadPlan plan, DateTime now)
    {
        if (plan.Repeats)
        {
            plan.StartAt = Schedule.Next(plan.StartAt.Hour, plan.StartAt.Minute, plan.RepeatDays, now);
            plan.State = PlanState.Waiting;
            plan.StartedAt = null;
            plan.EndAt = null;
        }
        else State.DownloadPlans.Remove(plan);
        Store.Save();
        Changed?.Invoke();
    }

    // ---- taught clicks ------------------------------------------------------------------------------

    private MacroBanner? _macroBanner;
    private MacroRecorder? _recorder;
    private CancellationTokenSource? _macroRun;

    public bool MacroBusy => _recorder is not null || _macroRun is not null;

    public LauncherMacro? MacroFor(string key) => State.Macros.FirstOrDefault(m => m.LauncherKey == key);

    public void RemoveMacro(string key)
    {
        State.Macros.RemoveAll(m => m.LauncherKey == key);
        Store.Save();
        Changed?.Invoke();
    }

    /// <summary>Starts a lesson: from now on every click in the launcher's window is remembered until the person finishes.
    /// <paramref name="done"/> gets the number of steps (0 when cancelled or nothing was clicked).</summary>
    public bool StartMacroLesson(LauncherInfo launcher, Action<int> done)
    {
        if (MacroBusy) return false;
        var recorder = new MacroRecorder(launcher.Key);
        if (!recorder.Start()) return false;
        _recorder = recorder;
        var banner = new MacroBanner(recording: true);
        _macroBanner = banner;
        void Update(int n) => banner.SetText(n == 0
            ? L.T($"یادگیری «{launcher.Name}»: توی پنجره‌ی لانچر همون کارهایی رو بکن که برای شروع دانلود می‌کنی.",
                $"Teaching “{launcher.Name}”: do what you do in the launcher to start the download.")
            : L.T($"یادگیری «{launcher.Name}»: {Fa.Digits(n.ToString())} کلیک ضبط شد.", $"Teaching “{launcher.Name}”: {n} clicks recorded."));
        Update(0);
        recorder.Changed += n => banner.Dispatcher.BeginInvoke(() => Update(n));
        void End(bool save)
        {
            var macro = recorder.Stop();
            _recorder = null;
            _macroBanner = null;
            banner.Close();
            var count = 0;
            if (save && macro is not null)
            {
                State.Macros.RemoveAll(m => m.LauncherKey == launcher.Key);
                State.Macros.Add(macro);
                Store.Save();
                count = macro.Steps.Count;
            }
            Changed?.Invoke();
            done(count);
        }
        banner.Finished += () => End(true);
        banner.Cancelled += () => End(false);
        banner.Show();
        return true;
    }

    /// <summary>Repeats a taught lesson on another thread, with the quiet bar on top and Esc to stop.
    /// <paramref name="auto"/>: started by a plan (the launcher was just opened, so its window may take a while).</summary>
    public void PlayMacro(LauncherMacro macro, string name, bool auto, Action<MacroOutcome>? done = null)
    {
        if (_macroRun is not null) return;
        var cts = new CancellationTokenSource();
        _macroRun = cts;
        var banner = new MacroBanner(recording: false);
        _macroBanner = banner;
        banner.SetText(L.T($"شب‌پا داره کلیک‌های یادگرفته‌شده‌ی «{name}» رو انجام می‌ده. ماوس رو دست نزن؛ برای توقف Esc بزن.",
            $"Shabpa is repeating the clicks taught for “{name}”. Leave the mouse alone; press Esc to stop."));
        banner.Show();
        Task.Run(() => MacroPlayer.Run(macro, (i, n) => banner.Dispatcher.BeginInvoke(() => banner.SetText(
            L.T($"«{name}»: کلیک {Fa.Digits(i.ToString())} از {Fa.Digits(n.ToString())}. ماوس رو دست نزن؛ برای توقف Esc بزن.",
                $"“{name}”: click {i} of {n}. Leave the mouse alone; press Esc to stop."))), auto ? 90 : 20, cts.Token))
            .ContinueWith(t =>
            {
                var outcome = t.IsCompletedSuccessfully ? t.Result : MacroOutcome.Interrupted;
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    banner.Close();
                    _macroBanner = null;
                    _macroRun = null;
                    Notify(L.T("دانلود زمان‌بندی‌شده", "Scheduled download"), $"{name}: {MacroPlayer.Describe(outcome)}");
                    done?.Invoke(outcome);
                });
            });
    }

    /// <summary>Removes a plan; if it is running, the "finished" watch stops with it.</summary>
    public void RemoveDownloadPlan(Guid id)
    {
        var plan = State.DownloadPlans.FirstOrDefault(p => p.Id == id);
        if (plan is null) return;
        State.DownloadPlans.Remove(plan);
        if (plan.State == PlanState.Running && State.ActiveRule is not null) DeactivateRule();
        Store.Save();
        Changed?.Invoke();
    }

    private void RunDownloadPlans(DateTime now)
    {
        foreach (var plan in State.DownloadPlans.ToList())
        {
            if (plan.State == PlanState.Waiting)
            {
                if (now < plan.StartAt) continue;
                // Missed (PC off or asleep, or something else kept it busy for minutes): do not start late.
                if (now - plan.StartAt > TimeSpan.FromMinutes(3))
                {
                    EndPlan(plan, now);
                    Notify(L.T("دانلود زمان‌بندی‌شده شروع نشد", "Scheduled download did not start"),
                        L.T($"موقع شروع «{plan.LauncherName}» سیستم خاموش یا خواب بود، یا شب‌پا بسته بود.",
                            $"The PC was off or asleep, or Shabpa was closed, when “{plan.LauncherName}” was due."));
                    continue;
                }
                if (State.ActiveRule is not null || CountdownActive) continue; // try again next second

                // The path comes from a file other programs can write, so it is checked again here, and the launcher is
                // opened with normal rights (see LauncherDetector.Open) even when Shabpa runs as administrator.
                if (!DryRun && (!System.IO.File.Exists(plan.ExePath) || LauncherDetector.Reject(plan.ExePath) is not null))
                {
                    EndPlan(plan, now);
                    Notify(L.T("لانچر باز نشد", "Could not open the launcher"),
                        L.T($"«{plan.LauncherName}» دیگه جای شناخته‌شده‌ش نیست؛ دوباره لانچرها رو پیدا کن یا دستی اضافه‌ش کن.",
                            $"“{plan.LauncherName}” is no longer where it was found; search for launchers again or add it by hand."));
                    continue;
                }
                if (!DryRun)
                {
                    try
                    {
                        LauncherDetector.Open(plan.ExePath);
                    }
                    catch (Exception ex)
                    {
                        EndPlan(plan, now);
                        Notify(L.T("لانچر باز نشد", "Could not open the launcher"), $"{plan.LauncherName}: {ex.Message}");
                        continue;
                    }
                }
                var taught = plan.UseMacro && !plan.OpenOnly ? State.Macros.FirstOrDefault(m => m.LauncherKey == plan.LauncherKey && m.Steps.Count > 0) : null;
                if (!DryRun && taught is not null)
                {
                    // The person showed what to click: that wins over the built-in help.
                    PlayMacro(taught, plan.LauncherName, auto: true);
                }
                else if (!DryRun && plan.LauncherKey is "steam" or "epic" or "riot" && !plan.OpenOnly)
                {
                    // A paused queue ignores the launcher being opened; press its resume button like a person would.
                    var steamFolder = System.IO.Path.GetDirectoryName(plan.ExePath) ?? "";
                    var name = plan.LauncherName;
                    var key = plan.LauncherKey;
                    Task.Run(() =>
                    {
                        if (MacroNative.ScreenLocked())
                        {
                            Application.Current?.Dispatcher.BeginInvoke(() => Notify(L.T("دانلود زمان‌بندی‌شده", "Scheduled download"),
                                $"{name}: " + L.T("صفحه قفل بود و دکمه‌ی ادامه زده نشد.", "The screen was locked, so the resume button was not pressed.")));
                            return;
                        }
                        var outcome = key switch
                        {
                            "steam" => SteamResume.Run(() => SteamResume.LogShowsDownload(steamFolder, DateTime.Now)),
                            "epic" => EpicResume.Run(),
                            _ => RiotResume.Run(),
                        };
                        var text = outcome switch
                        {
                            SteamResume.Outcome.Resumed => L.T("دانلود ادامه داده شد.", "The download was resumed."),
                            SteamResume.Outcome.AlreadyRunning => L.T("دانلود از قبل در جریان بود.", "A download was already running."),
                            SteamResume.Outcome.NothingQueued => L.T("چیزی برای ادامه دادن توی صف پیدا نشد. اگه دانلود شروع نشد، از «دانلود زمان‌بندی‌شده ← یادگیری دستی» بهم یاد بده کجا کلیک کنم.",
                                "Nothing was waiting in the queue to resume. If the download did not start, teach me where to click in “Scheduled download → Teach by hand”."),
                            SteamResume.Outcome.ScreenLocked => L.T("صفحه قفل بود و دکمه‌ی ادامه زده نشد.", "The screen was locked, so the resume button was not pressed."),
                            _ => L.T("پنجره‌ی لانچر پیدا نشد.", "The launcher's window was not found."),
                        };
                        Application.Current?.Dispatcher.BeginInvoke(() => Notify(L.T("دانلود زمان‌بندی‌شده", "Scheduled download"), $"{name}: {text}"));
                    });
                }
                else if (!DryRun && !plan.OpenOnly)
                {
                    // No built-in help for this launcher and nothing taught: the plan only opens it.
                    var name = plan.LauncherName;
                    Notify(L.T("دانلود زمان‌بندی‌شده", "Scheduled download"),
                        L.T($"«{name}» باز شد. اگه دانلودش خودکار شروع نمی‌شه، از «دانلود زمان‌بندی‌شده ← یادگیری دستی» یادش بده کجا کلیک کنه.",
                            $"“{name}” was opened. If its download does not start by itself, teach it where to click in “Scheduled download → Teach by hand”."));
                }
                if (plan.OpenOnly)
                {
                    EndPlan(plan, now);
                    Notify(L.T("دانلود زمان‌بندی‌شده", "Scheduled download"), L.T($"«{plan.LauncherName}» باز شد.", $"“{plan.LauncherName}” was opened."));
                    continue;
                }
                plan.State = PlanState.Running;
                plan.StartedAt = now;
                plan.EndAt = DownloadPlan.EndAfter(plan.StartAt, plan.EndMinute);
                ActivateRule(new Rule
                {
                    Kind = RuleKind.Network, Upload = false, WaitForStart = true,
                    // Steam is often limited to a few hundred KB/s by its own setting; a 1 Mbit line would call that "quiet".
                    ThresholdMbps = plan.LauncherKey == "steam" ? Math.Min(plan.ThresholdMbps, 0.2) : plan.ThresholdMbps,
                    HoldMinutes = plan.QuietMinutes, ExceptRecording = false, ExceptGame = false, Action = plan.Action,
                });
                Notify(L.T("دانلود زمان‌بندی‌شده", "Scheduled download"),
                    L.T($"«{plan.LauncherName}» باز شد. وقتی دانلود تموم بشه، «{Fa.ActionName(plan.Action)}» انجام می‌شه.",
                        $"“{plan.LauncherName}” was opened. “{Fa.ActionName(plan.Action)}” runs when the download ends."));
            }
            else
            {
                var finishedOrCancelled = State.ActiveRule is null;
                var neverStarted = !finishedOrCancelled && Engine.Status.State == RuleState.Waiting
                                   && plan.StartedAt is { } started && now - started > TimeSpan.FromMinutes(plan.GiveUpMinutes);
                if (!finishedOrCancelled && plan.EndAt is { } windowEnd && now >= windowEnd)
                {
                    // the download window is over and the download is still going
                    DeactivateRule();
                    var act = plan.ActAtEnd && !plan.OpenOnly;
                    Notify(L.T("وقت دانلود تموم شد", "Download time is over"),
                        act ? L.T($"ساعت پایان «{plan.LauncherName}» رسید و دانلود هنوز تمومه نشده؛ «{Fa.ActionName(plan.Action)}» انجام می‌شه.",
                                  $"The end time for “{plan.LauncherName}” came and the download is not done; “{Fa.ActionName(plan.Action)}” runs now.")
                            : L.T($"ساعت پایان «{plan.LauncherName}» رسید و دانلود هنوز تمومه نشده؛ دیگه منتظرش نمی‌مونم و سیستم خاموش نمی‌شه.",
                                  $"The end time for “{plan.LauncherName}” came and the download is not done; I stop waiting and the PC will not be switched off."));
                    EndPlan(plan, now);
                    if (act) RequestAction(plan.Action, L.T("پایان زمان دانلود", "Download window ended"), Settings.CountdownSeconds, "plan");
                    continue;
                }
                if (neverStarted)
                {
                    DeactivateRule();
                    Notify(L.T("دانلودی دیده نشد", "No download seen"),
                        L.T($"{Fa.Digits(plan.GiveUpMinutes.ToString())} دقیقه بعد از باز شدن «{plan.LauncherName}» هیچ دانلودی شروع نشد؛ سیستم خاموش نمی‌شه.",
                            $"No download started within {plan.GiveUpMinutes} minutes of opening “{plan.LauncherName}”; the PC will not be shut down."));
                }
                if (finishedOrCancelled || neverStarted) EndPlan(plan, now);
            }
        }
    }

    // ---- power actions ----------------------------------------------------------------------

    /// <summary>Shows the cancellable countdown, then runs the action. False if another countdown is up.</summary>
    public bool RequestAction(PowerAction action, string reason, int seconds, string origin = "")
    {
        if (seconds <= 0 || action == PowerAction.Lock)
        {
            Run(action);
            return true;
        }
        if (_countdown is not null) return false;

        var window = new CountdownWindow(action, reason, seconds);
        _countdown = window;
        _countdownOrigin = origin;
        window.Closed += (_, _) =>
        {
            _countdown = null;
            _countdownOrigin = "";
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

    // ---- commands from @ShabPa_Bot (the fixed list in the bot's docs/PROTOCOL.md; nothing else can run) ----------

    private static bool TryAction(string? name, out PowerAction action)
    {
        action = name switch
        {
            "shutdown" => PowerAction.Shutdown,
            "sleep" => PowerAction.Sleep,
            "hibernate" => PowerAction.Hibernate,
            "restart" => PowerAction.Restart,
            "lock" => PowerAction.Lock,
            _ => (PowerAction)(-1),
        };
        return Enum.IsDefined(action);
    }

    private static int IntArg(JsonElement args, string name, int fallback) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : fallback;

    private static bool BoolArg(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static string? StringArg(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    internal RemoteReply ExecuteRemote(string type, JsonElement args)
    {
        switch (type)
        {
            case "status":
            {
                var text = StatusText();
                return RemoteReply.Done(text, text);
            }
            case "power":
            {
                if (!TryAction(StringArg(args, "action"), out var action))
                    return RemoteReply.Fail("این دستور رو نمی‌شناسم.", "Unknown command.");
                var delay = Math.Clamp(IntArg(args, "delaySeconds", 0), 0, 7 * 24 * 3600);
                if (delay > 60)
                {
                    // Later: it goes into the normal queue, which shows its own countdown on screen when the time comes.
                    var at = DateTime.Now.AddSeconds(delay);
                    AddCommand(action, at);
                    return RemoteReply.Done($"ساعت {Fa.Clock(at)} {Fa.ActionSentence(action)}.", $"At {at:HH:mm} {Fa.ActionSentence(action)}.");
                }
                if (action == PowerAction.Lock)
                {
                    Run(action);
                    return RemoteReply.Done("صفحه قفل شد.", "The screen is locked.");
                }
                const int seconds = 15;
                return RequestAction(action, L.T("ربات تلگرام", "Telegram bot"), seconds)
                    ? RemoteReply.Done($"{Fa.Digits(seconds.ToString())} ثانیه‌ی دیگه {Fa.ActionSentence(action)}؛ روی صفحه شمارش معکوس داره.", $"In {seconds} seconds {Fa.ActionSentence(action)}; a countdown is on screen.")
                    : RemoteReply.Fail("الان یه شمارش معکوس دیگه روی صفحه‌ست. اول لغوش کن.", "Another countdown is already on screen. Cancel it first.");
            }
            case "cancel":
            {
                var sleep = SleepActive;
                CancelSleepMode();
                var text = CancelEverything();
                if (sleep) text += "\n" + L.T("حالت شب‌خواب هم لغو شد.", "Sleep mode was cancelled too.");
                return RemoteReply.Done(text, text);
            }
            case "ram-clean":
                CleanMemory(cache: false);
                return RemoteReply.Done("رم داره خالی می‌شه.", "Freeing up RAM.");
            case "dim":
            {
                var c = Settings.Comfort;
                c.DimOn = BoolArg(args, "on");
                var level = IntArg(args, "level", 0);
                if (level > 0) c.DimLevel = Math.Clamp(level / 100.0, 0.05, 0.92);
                Store.Save();
                Dim?.Apply();
                ComfortChanged();
                return c.DimOn
                    ? RemoteReply.Done($"صفحه کم‌نور شد ({Fa.Digits(Math.Round(c.DimLevel * 100).ToString())}٪).", $"The screen is dimmed ({Math.Round(c.DimLevel * 100)}%).")
                    : RemoteReply.Done("نور صفحه برگشت.", "The screen is back to normal.");
            }
            case "volume":
            {
                var level = BoolArg(args, "mute") ? 0 : Math.Clamp(IntArg(args, "level", -1), -1, 100);
                if (level < 0) return RemoteReply.Fail("مقدار صدا مشخص نیست.", "No volume level was given.");
                return MasterVolume.SetBounded(level / 100.0)
                    ? RemoteReply.Done($"صدا روی {Fa.Digits(level.ToString())}٪ رفت.", $"Volume set to {level}%.")
                    : RemoteReply.Fail("دستگاه صدا پیدا نشد.", "No audio device was found.");
            }
            case "sleep-mode":
            {
                var c = Settings.Comfort;
                var minutes = IntArg(args, "minutes", 0);
                if (minutes > 0) c.SleepMinutes = Math.Clamp(minutes, 1, 600);
                if (TryAction(StringArg(args, "action"), out var action)) c.SleepAction = action;
                Store.Save();
                if (SleepActive) return RemoteReply.Fail("حالت شب‌خواب از قبل روشنه.", "Sleep mode is already on.");
                StartSleepMode();
                return RemoteReply.Done($"حالت شب‌خواب شروع شد؛ {Fa.Digits(c.SleepMinutes.ToString())} دقیقه‌ی دیگه {Fa.ActionSentence(c.SleepAction)}.",
                    $"Sleep mode started; in {c.SleepMinutes} minutes {Fa.ActionSentence(c.SleepAction)}.");
            }
            case "sleep-cancel":
                if (!SleepActive) return RemoteReply.Done("حالت شب‌خواب روشن نبود.", "Sleep mode was not on.");
                CancelSleepMode();
                return RemoteReply.Done("حالت شب‌خواب لغو شد.", "Sleep mode is cancelled.");
            case "profile":
            {
                EnsureProfiles();
                var wanted = (StringArg(args, "name") ?? "").Trim();
                var profile = State.Profiles.FirstOrDefault(p => p.Name.Equals(wanted, StringComparison.CurrentCultureIgnoreCase));
                if (profile is null)
                    return RemoteReply.Fail("پروفایلی با این اسم پیدا نشد.", "No profile with that name.");
                // switching the PC off is never started from afar through a profile: the power commands have their own
                // confirmation and countdown
                if (Core.Profiles.HasPower(profile))
                    return RemoteReply.Fail("این پروفایل شامل خاموشی یا خواب‌ه؛ از اینجا اجرا نمی‌شه.", "This profile can switch the PC off, so it cannot be run from here.");
                var outcome = RunProfile(profile, "bot");
                var text = string.Join("\n", outcome.Steps);
                return outcome.Ok ? RemoteReply.Done(text, text) : RemoteReply.Fail(text, text);
            }
            case "focus":
            {
                if (string.Equals(StringArg(args, "action"), "stop", StringComparison.OrdinalIgnoreCase))
                {
                    Focus.Stop(DateTime.Now);
                    Store.Save();
                    return RemoteReply.Done("تایمر تمرکز متوقف شد.", "The focus timer is stopped.");
                }
                var minutes = IntArg(args, "minutes", 0);
                if (minutes > 0) Settings.Focus.FocusMinutes = Math.Clamp(minutes, 5, 90);
                Store.Save();
                Focus.Start(DateTime.Now);
                return RemoteReply.Done($"تمرکز {Fa.Digits(Settings.Focus.FocusMinutes.ToString())} دقیقه‌ای شروع شد.", $"A {Settings.Focus.FocusMinutes}-minute focus started.");
            }
            case "tasks":
            {
                var today = Tasks.View(TaskView.Today, DateTime.Now).ToList();
                // task titles are personal: they only leave this PC when the person switched that on
                if (!Settings.TasksToBot)
                    return RemoteReply.Done($"{Fa.Digits(today.Count.ToString())} کار برای امروز داری. (نمایش عنوان‌ها توی تنظیمات ربات خاموشه.)",
                        $"You have {today.Count} task(s) for today. (Showing titles is off in the bot settings.)");
                var list = string.Join("\n", today.Take(10).Select(t => "• " + t.Title + (t.Due is { } d ? $" ({Fa.Clock(d)})" : "")));
                return RemoteReply.Done(today.Count == 0 ? "برای امروز کاری نداری." : list, today.Count == 0 ? "Nothing for today." : list);
            }
            case "text":
                return ExecuteText(StringArg(args, "text") ?? "");
            default:
                // "after-download" and anything newer than this version
                return RemoteReply.Fail("این نسخه‌ی شب‌پا هنوز این دستور رو نداره.", "This version of Shabpa does not support that yet.");
        }
    }

    private void Run(PowerAction action)
    {
        // Once the PC goes off or to sleep, everything else that was waiting for it is void.
        List<ScheduledCommand>? leftovers = null;
        var ruleWas = State.ActiveRule;
        if (action != PowerAction.Lock)
        {
            leftovers = Queue.ClearOneShots();
            if (ruleWas is not null)
            {
                State.ActiveRule = null;
                Engine.Deactivate();
            }
        }
        Store.Save();
        var error = Executor.Execute(action);
        if (error is not null && leftovers is not null)
        {
            // It did not happen after all: put things back as they were.
            State.Queue.AddRange(leftovers);
            if (ruleWas is not null)
            {
                State.ActiveRule = ruleWas;
                Engine.Activate(ruleWas);
            }
            Store.Save();
            Changed?.Invoke();
        }
        else if (leftovers is not null)
        {
            Changed?.Invoke();
        }
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

    /// <summary>False when an identical command was already waiting (nothing new was added).</summary>
    public bool AddCommand(PowerAction action, DateTime dueAt, IEnumerable<DayOfWeek>? repeat = null)
    {
        var added = Queue.TryAdd(action, dueAt, repeat, "", out _);
        Store.Save();
        Changed?.Invoke();
        return added;
    }

    /// <summary>Deletes a waiting command for good. If its countdown is already on screen, that stops too.</summary>
    public void RemoveCommand(Guid id)
    {
        Queue.Remove(id);
        if (_countdownOrigin == id.ToString()) CancelCountdown();
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
        if (_countdownOrigin == "rule") CancelCountdown();
        Store.Save();
        Changed?.Invoke();
    }

    private bool _addingWake;

    /// <summary>Returns an error message or null.</summary>
    public string? AddWake(WakeSchedule schedule)
    {
        if (_addingWake) return L.T("ثبت قبلی هنوز تموم نشده.", "The previous request is still running."); // the window keeps answering during the task call: no second click may start another
        if (!Demo)
        {
            _addingWake = true;
            string? error;
            try { error = Offload.KeepResponsive(() => WakeScheduler.Register(schedule, ExePath)); }
            catch (OperationCanceledException) { return L.T("برنامه در حال بسته شدن بود.", "The app was closing."); }
            finally { _addingWake = false; }
            if (error is not null) return error;
        }
        State.WakeSchedules.Add(schedule);
        Store.Save();
        Changed?.Invoke();
        return null;
    }

    public void RemoveWake(Guid id)
    {
        if (!Demo) _ = Task.Run(() => WakeScheduler.Unregister(id)); // a console program: never on the window's thread
        State.WakeSchedules.RemoveAll(w => w.Id == id);
        Store.Save();
        Changed?.Invoke();
    }

    public DateTime? NextWake(DateTime now) =>
        State.WakeSchedules.Count == 0 ? null : State.WakeSchedules.Min(w => Schedule.Next(w.Hour, w.Minute, w.Days, now));

    // ---- misc -------------------------------------------------------------------------------

    /// <summary>The last notices shown, newest last; memory only (they may mention task titles).</summary>
    public List<(DateTime Time, string Title, string Text)> Notices { get; } = [];

    public void Notify(string title, string text, string? target = null)
    {
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => Notify(title, text, target));
            return;
        }
        NoticeTarget = target;
        Notices.Add((DateTime.Now, title, text));
        if (Notices.Count > 30) Notices.RemoveAt(0);
        // game mode: nothing pops up over the game; it is all in the notice list afterwards
        if (GameModeActive && Settings.Comfort.GameModeQuiet) return;
        Notified?.Invoke(title, text);
    }

    public void RaiseChanged() => Changed?.Invoke();

    // ---- birthday ----------------------------------------------------------------------------------------
    // Checked on the existing one-second tick (a date comparison, nothing else) so there is no timer of its own: the first
    // tick after midnight flips the look, and the greeting appears once per day when no game is running.

    private DateTime _bdayDay;
    private bool _bdayToday, _bdayLook, _bdayDirty = true;

    /// <summary>Today is the person's birthday.</summary>
    public bool BirthdayToday => _bdayToday;

    /// <summary>The festive look is on (birthday today and the option is on).</summary>
    public bool BirthdayLook => _bdayLook;

    /// <summary>The birthday state changed (new day, settings edited): re-evaluate the look and the home greeting.</summary>
    public event Action? BirthdayChanged;

    /// <summary>The greeting window is due now.</summary>
    public event Action? BirthdayGreet;

    /// <summary>Settings were edited: look again at once.</summary>
    public void RecheckBirthday()
    {
        _bdayDirty = true;
        CheckBirthday(Core.Birthday.Now());
    }

    /// <summary>The state at start (no events: the window does not exist yet and builds itself from it).</summary>
    private void PrimeBirthday()
    {
        var now = Core.Birthday.Now();
        _bdayDay = now.Date;
        _bdayDirty = false;
        _bdayToday = Core.Birthday.IsToday(Settings.Birthday, now);
        _bdayLook = _bdayToday && Settings.Birthday.Theme;
    }

    private void CheckBirthday(DateTime now)
    {
        var b = Settings.Birthday;
        if (now.Date != _bdayDay || _bdayDirty)
        {
            _bdayDay = now.Date;
            _bdayDirty = false;
            var today = Core.Birthday.IsToday(b, now);
            var look = today && b.Theme;
            var changed = today != _bdayToday || look != _bdayLook;
            _bdayToday = today;
            _bdayLook = look;
            if (changed) BirthdayChanged?.Invoke();
        }
        if (!_bdayToday) return;
        var key = Core.Birthday.Key(now);
        if (b.LastGreeted == key) return;
        if (Snapshot.GameRunning || GameModeActive) return; // never over a game: it is offered again on a later tick
        if (WindowShown)
        {
            b.LastGreeted = key;
            Store.Save();
            BirthdayGreet?.Invoke();
        }
        else if (b.LastBalloon != key)
        {
            b.LastBalloon = key;
            Store.Save();
            Notify(Core.Birthday.Title(b.Name), L.T("امیدوارم روز خوبی داشته باشی 🎈", "Have a lovely day 🎈"), "home");
        }
    }

    /// <summary>One line describing what is coming up, for the sidebar and the tray tooltip.</summary>
    public string NextText()
    {
        if (Queue.Next is { } next)
            return $"{Fa.ActionName(next.Action)}{L.Comma}{Fa.DayOf(next.DueAt, DateTime.Now)} {Fa.Clock(next.DueAt)}";
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
            lines.Add(L.T($"⏳ بعدی: {Fa.ActionName(next.Action)}، {Fa.DayOf(next.DueAt, DateTime.Now)} ساعت {Fa.Clock(next.DueAt)}",
                $"⏳ Next: {Fa.ActionName(next.Action)}, {Fa.DayOf(next.DueAt, DateTime.Now)} at {Fa.Clock(next.DueAt)}"));
        if (Engine.Rule is { } rule)
            lines.Add(L.T($"🎯 قانون فعال ({Fa.ActionName(rule.Action)}): {Engine.Status.Text}", $"🎯 Active rule ({Fa.ActionName(rule.Action)}): {Engine.Status.Text}"));
        if (Queue.Next is null && Engine.Rule is null) lines.Add(L.T("برنامه‌ای در راه نیست.", "Nothing scheduled."));
        return string.Join("\n", lines);
    }

    /// <summary>Sample data for --demo runs; never written to disk.</summary>
    /// <summary>Fake sessions in memory (demo runs); with <paramref name="save"/> they are written to the data folder instead (--demo-games, scratch folders only).</summary>
    public void SeedGameLog(DateTime now, bool save = false)
    {
        var (games, series) = GameLogDemo.Make(now);
        GameLog.Seed(games, id => series.GetValueOrDefault(id) ?? new SessionSeries());
        if (save) GameLog.SaveWithSeries(series);
    }

    public void FillDemo()
    {
        var now = DateTime.Now;
        var random = new Random(7);
        SeedGameLog(now);
        Settings.Launchers =
        [
            new LauncherInfo { Key = "steam", Name = "Steam", ExePath = @"C:\Program Files (x86)\Steam\steam.exe", Process = "steam" },
            new LauncherInfo { Key = "epic", Name = "Epic Games", ExePath = @"C:\Program Files (x86)\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe", Process = "epicgameslauncher" },
        ];
        Settings.LaunchersScanned = true;
        State.Tasks.AddRange(
        [
            new TodoItem { Title = L.T("تحویل گزارش پروژه", "Hand in the project report"), Due = now.Date.AddHours(17), RemindBefore = 60, Category = L.T("کار", "Work"), Important = true },
            new TodoItem { Title = L.T("خرید نان و شیر", "Buy bread and milk"), Due = now.Date.AddHours(19), RemindBefore = 0, Category = L.T("شخصی", "Personal") },
            new TodoItem { Title = L.T("مرور فصل ۳", "Review chapter 3"), Due = now.Date.AddDays(1).AddHours(10), RemindBefore = 10, Category = L.T("مطالعه", "Study") },
            new TodoItem { Title = L.T("پاسخ به ایمیل‌ها", "Answer emails"), Due = now.Date.AddDays(-1).AddHours(16), Category = L.T("کار", "Work") },
            new TodoItem { Title = L.T("ایده‌هایی برای تعطیلات", "Holiday ideas") },
            new TodoItem { Title = L.T("نوبت دندان‌پزشکی گرفتم", "Booked the dentist"), Done = true, DoneAt = now.AddHours(-2), Category = L.T("شخصی", "Personal") },
            new TodoItem { Title = L.T("کتاب رو تموم کردم", "Finished the book"), Done = true, DoneAt = now.AddDays(-1), Category = L.T("مطالعه", "Study") },
        ]);
        State.FocusLog.AddRange(
        [
            new FocusSession { Start = now.Date.AddDays(-1).AddHours(10), Minutes = 25, Completed = true, Task = L.T("نوشتن گزارش", "Writing the report") },
            new FocusSession { Start = now.Date.AddHours(9), Minutes = 25, Completed = true, Task = L.T("مرور درس", "Studying") },
            new FocusSession { Start = now.Date.AddHours(10), Minutes = 18, Completed = false },
        ]);
        Settings.WeatherCity = new WeatherCity { Name = "تهران", Region = "استان تهران", Country = "ایران", Latitude = 35.69, Longitude = 51.42 };
        Settings.WeatherFavorites =
        [
            new WeatherCity { Name = L.T("اصفهان", "Isfahan"), Region = L.T("استان اصفهان", "Isfahan"), Country = L.T("ایران", "Iran"), Latitude = 32.66, Longitude = 51.68 },
            new WeatherCity { Name = L.T("شیراز", "Shiraz"), Region = L.T("استان فارس", "Fars"), Country = L.T("ایران", "Iran"), Latitude = 29.59, Longitude = 52.58 },
            new WeatherCity { Name = L.T("تبریز", "Tabriz"), Region = L.T("آذربایجان شرقی", "East Azerbaijan"), Country = L.T("ایران", "Iran"), Latitude = 38.08, Longitude = 46.29 },
        ];
        State.DownloadPlans =
        [
            new DownloadPlan
            {
                LauncherKey = "steam", LauncherName = "Steam", StartAt = now.Date.AddDays(1).AddHours(3), Action = PowerAction.Shutdown,
                RepeatDays = [DayOfWeek.Friday, DayOfWeek.Saturday],
            },
        ];
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
