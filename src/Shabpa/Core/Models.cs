namespace Shabpa.Core;

public enum PowerAction { Sleep, Hibernate, Shutdown, Restart, Lock }

public sealed class ScheduledCommand
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public PowerAction Action { get; set; }
    public DateTime DueAt { get; set; }
    /// <summary>Empty = one-shot. Otherwise the command repeats weekly on these days at DueAt's time of day.</summary>
    public List<DayOfWeek> RepeatDays { get; set; } = [];
    /// <summary>Empty = scheduled by hand in the app.</summary>
    public string Source { get; set; } = "";

    public bool Repeats => RepeatDays.Count > 0;
}

public enum RuleKind { Network, Load, Idle, ProcessExit }

public sealed class Rule
{
    public RuleKind Kind { get; set; } = RuleKind.Network;
    public bool Upload { get; set; }
    public double ThresholdMbps { get; set; } = 1;
    /// <summary>Only start counting after the transfer has actually been seen running.</summary>
    public bool WaitForStart { get; set; } = true;
    public int LoadPercent { get; set; } = 15;
    public int HoldMinutes { get; set; } = 5;
    public string ProcessName { get; set; } = "";
    public bool ExceptRecording { get; set; } = true;
    public bool ExceptGame { get; set; }
    public PowerAction Action { get; set; } = PowerAction.Sleep;

    public Rule Clone() => (Rule)MemberwiseClone();
}

public sealed class WakeSchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Hour { get; set; } = 7;
    public int Minute { get; set; } = 30;
    public List<DayOfWeek> Days { get; set; } = [];
}

public sealed class EnergyDay
{
    public DateOnly Date { get; set; }
    public double GamingWh { get; set; }
    public double ActiveWh { get; set; }
    public double IdleWh { get; set; }
    public double MeasuredSeconds { get; set; }

    public double TotalWh => GamingWh + ActiveWh + IdleWh;
}

public sealed class Settings
{
    public double Tariff { get; set; } = 300;
    public int CountdownSeconds { get; set; } = 30;
    /// <summary>Everything that is not CPU or GPU: board, RAM, drives, fans, PSU loss.</summary>
    public double BaseWatts { get; set; } = 45;
    public double GpuIdleWatts { get; set; } = 12;
    public double GpuMaxWatts { get; set; } = 250;
    /// <summary>Used only when Windows exposes no CPU energy meter.</summary>
    public double CpuMaxWatts { get; set; } = 125;
    /// <summary>Detected hardware figures, which fields were typed by hand, and what the sensors have learned.</summary>
    public PowerSettings Power { get; set; } = new();
    public bool StartMinimized { get; set; }
    /// <summary>Ask "are you sure" (with the exact time it will happen) before anything is scheduled, run or deleted.</summary>
    public bool ConfirmActions { get; set; } = true;
    public OverlaySettings Overlay { get; set; } = new();
    /// <summary>After a game of at least a few minutes, show how it went (and send it to Telegram).</summary>
    public bool GameReports { get; set; } = true;
    /// <summary>Record every game session (play time, temperatures, usage, other programs running) on this PC for the Playtime page.</summary>
    public bool GameLog { get; set; } = true;
    /// <summary>Page and selection animations (also off when Windows has animation effects off).</summary>
    public bool Animations { get; set; } = true;
    /// <summary>Tiny interface sounds on hover, click and notices (experimental).</summary>
    public bool UiSounds { get; set; } = true;
    /// <summary>Windows 11 backdrop behind the window: "off", "mica" (soft, follows the wallpaper) or "acrylic" (frosted glass).</summary>
    public string Glass { get; set; } = "off";
    /// <summary>How dark the glass is, 0 (clearest) to 1.</summary>
    public double GlassLevel { get; set; }
    /// <summary>The stars the person gave the app (0 = none). Kept on this PC only.</summary>
    public int Rating { get; set; }
    public DateTime? RatedAt { get; set; }
    /// <summary>Draw the window in software: less RAM while it is open, a little more CPU. Applied at start.</summary>
    public bool LowMemory { get; set; }
    /// <summary>Draw the window with the processor instead of the graphics card (lighter on the GPU, a little more CPU). Applied at start.</summary>
    public bool SoftwareRender { get; set; }
    /// <summary>The live noise suppressor for the microphone.</summary>
    public VoiceSettings Voice { get; set; } = new();
    /// <summary>The starter profiles were added once; deleting them keeps them gone.</summary>
    public bool ProfilesSeeded { get; set; }
    public FocusSettings Focus { get; set; } = new();
    /// <summary>Show a notice when a task's reminder time comes.</summary>
    public bool TaskReminders { get; set; } = true;
    /// <summary>Let the Telegram bot read today's task titles. Off by default: titles are personal.</summary>
    public bool TasksToBot { get; set; }
    /// <summary>Where the window was last: "left,top,width,height,maximized" in device-independent pixels.</summary>
    public string WindowPlacement { get; set; } = "";
    /// <summary>Eye comfort (dimming), break reminders and sleep mode.</summary>
    public ComfortSettings Comfort { get; set; } = new();
    /// <summary>Support tickets: device id, server address, admin key (owner only).</summary>
    public SupportSettings Support { get; set; } = new();
    /// <summary>Monthly internet allowance in GB; 0 = no limit.</summary>
    public double MonthlyLimitGb { get; set; }
    /// <summary>"yyyy-MM:level" of the last budget warning, so each level is announced once a month.</summary>
    public string BudgetWarned { get; set; } = "";
    public bool TrayNoticeShown { get; set; }
    /// <summary>The launchers found the last time the user asked Shabpa to look.</summary>
    public List<LauncherInfo> Launchers { get; set; } = [];
    public bool LaunchersScanned { get; set; }
    /// <summary>DNS servers the user added to the list.</summary>
    public List<DnsEntry> CustomDns { get; set; } = [];
    /// <summary>The person's short note per DNS (works for this game / got around that block), keyed by "primary/secondary". Older files simply lack it.</summary>
    public Dictionary<string, string> DnsNotes { get; set; } = [];
    /// <summary>The default city of the weather page (shown at startup). Always one of <see cref="WeatherFavorites"/> once those are sanitised.</summary>
    public WeatherCity? WeatherCity { get; set; }
    /// <summary>The cities the person keeps on the weather page (at most <see cref="WeatherCities.MaxCities"/>). Older files simply lack it.</summary>
    public List<WeatherCity> WeatherFavorites { get; set; } = [];
    /// <summary>Programs the person says are games (names, separated by commas), for games Shabpa does not recognise by itself.</summary>
    public string GameList { get; set; } = "";
    public string RecorderProcesses { get; set; } = "obs64, obs32, obs, Streamlabs OBS, bdcam, CamtasiaStudio, CamRecorder, XSplit.Core, Action, ShareX, ffmpeg";
    /// <summary>The person chose to link this PC to @ShabPa_Bot; nothing is sent to the server before that.</summary>
    public bool BotEnabled { get; set; }
    public string BotDeviceId { get; set; } = "";
    /// <summary>DPAPI-protected device secret; the server only keeps its hash.</summary>
    public string BotSecretProtected { get; set; } = "";
    /// <summary>Overrides the project's server address (empty = built-in).</summary>
    public string BotEndpoint { get; set; } = "";
    public string ProxyUrl { get; set; } = "";
    /// <summary>Optional name and birthday for the birthday greeting. Stored on this PC only, never sent anywhere.</summary>
    public BirthdaySettings Birthday { get; set; } = new();
    /// <summary>The Halloween look and the first-open jump scare with a greeting (28 October to 1 November). On by default.</summary>
    public bool Halloween { get; set; } = true;
    /// <summary>Lighting (RGB) of the PC's fans, memory, strips and so on. Off until the person turns it on.</summary>
    public RgbSettings Rgb { get; set; } = new();
    /// <summary>The year the scare was last shown, so it comes once a year.</summary>
    public int HalloweenSeenYear { get; set; }
    public string PingHost { get; set; } = "8.8.8.8";
    /// <summary>"mbit", "mbyte" or "kbyte": the unit internet speeds are shown in.</summary>
    public string SpeedUnit { get; set; } = "mbit";
    /// <summary>While a game runs, cap the upload of everything else.</summary>
    public bool GuardEnabled { get; set; }
    public double GuardCapMbps { get; set; } = 1;
    /// <summary>While a game runs, cap the download of everything else; 0 = leave downloads alone.</summary>
    public double GuardDownCapMbps { get; set; }
    /// <summary>"fa" or "en".</summary>
    public string Language { get; set; } = "fa";
    public string Theme { get; set; } = Palette.Default;
    /// <summary>Trim working sets automatically when RAM use passes this percentage; 0 = off.</summary>
    public int AutoTrimPercent { get; set; }
    public bool AutoUpdateCheck { get; set; } = true;
    /// <summary>A newer signed version is installed by Shabpa itself (a short countdown, then the same checked download as the Updates page). Never while a game runs.</summary>
    public bool ForceUpdates { get; set; } = true;
    /// <summary>The newest version the user was already told about, so each release is announced once.</summary>
    public string AnnouncedVersion { get; set; } = "";
    /// <summary>Night internet window and power-cut standby (phase 1 of the growth plan).</summary>
    public NightSettings Overnight { get; set; } = new();
    /// <summary>Advanced: an extra update mirror (https address of a folder holding latest.json), tried after the built-in ones.</summary>
    public string ExtraMirrorUrl { get; set; } = "";
    /// <summary>Id of the update source that worked last; it is tried first next time.</summary>
    public string LastUpdateSource { get; set; } = "";
    /// <summary>The guided tour: "" (undecided), "pending", "offered", "done", "skipped" or "declined" (see Tour/TourState.cs).</summary>
    public string TourStatus { get; set; } = "";
    /// <summary>Opens the typed-command box from anywhere; empty = no shortcut (the tray item still works).</summary>
    public string AssistantKey { get; set; } = "Ctrl+Alt+Space";
    /// <summary>The small Bidar owl docked in the corner of the main window (a click opens the command box).</summary>
    public bool ShowAssistantDock { get; set; } = true;
    /// <summary>Bidar lives on his own: breathes, blinks, looks about, dozes, reacts to the app and sometimes flies to another free corner.</summary>
    public bool BidarIdleLife { get; set; } = true;
}

public sealed class AppState
{
    public Settings Settings { get; set; } = new();
    public List<ScheduledCommand> Queue { get; set; } = [];
    public Rule RuleDraft { get; set; } = new();
    public Rule? ActiveRule { get; set; }
    public List<WakeSchedule> WakeSchedules { get; set; } = [];
    public List<EnergyDay> Energy { get; set; } = [];
    public List<AppLimit> NetLimits { get; set; } = [];
    public NetUsage? NetUsage { get; set; }
    public List<DownloadPlan> DownloadPlans { get; set; } = [];
    /// <summary>Clicks the person taught for a launcher (one per launcher).</summary>
    public List<LauncherMacro> Macros { get; set; } = [];
    /// <summary>How often each page was opened (local only, never sent): the most used ones appear under the quick actions.</summary>
    public Dictionary<string, int> PageVisits { get; set; } = [];
    /// <summary>The last games played, newest last (at most 60).</summary>
    public List<GameLogEntry> GameHistory { get; set; } = [];
    /// <summary>The to-do list (phase A). Local only.</summary>
    public List<TodoItem> Tasks { get; set; } = [];
    /// <summary>Finished and stopped focus sessions, newest last (at most 2000).</summary>
    public List<FocusSession> FocusLog { get; set; } = [];
    /// <summary>Folder backup jobs (phase B).</summary>
    public List<BackupJob> Backups { get; set; } = [];
    /// <summary>Startup items as last accepted by the person (key to command); null until the first look.</summary>
    public Dictionary<string, string>? StartupBaseline { get; set; }
    /// <summary>Startup changes already announced, so each is announced once.</summary>
    public List<string> StartupAnnounced { get; set; } = [];
    /// <summary>Ids of the health or security warnings the person chose to ignore (they can bring each back).</summary>
    public List<string> HealthIgnored { get; set; } = [];
    /// <summary>Error groups of the health page the person marked as handled: key to the time of the newest event then (a newer one shows again).</summary>
    public Dictionary<string, DateTime> EventsHandled { get; set; } = [];
    /// <summary>The person's own values of the game tweaks, kept before the first change so turning a tweak off puts exactly them back.</summary>
    public Dictionary<string, string> TuneBackup { get; set; } = [];
    /// <summary>Work profiles, their triggers and the run history (phase D).</summary>
    public List<WorkProfile> Profiles { get; set; } = [];
    public List<AutomationTrigger> Triggers { get; set; } = [];
    public List<RunLogEntry> RunLog { get; set; } = [];
    /// <summary>Tickets sent from this PC.</summary>
    public List<TicketRecord> Tickets { get; set; } = [];
    /// <summary>Feathers, quests and medals (phase 1 of the growth plan). Local only.</summary>
    public RewardsState Rewards { get; set; } = new();
    /// <summary>The optional cloud account (login with Telegram). Empty unless the person chose it.</summary>
    public CloudState Cloud { get; set; } = new();
}

/// <summary>What one group of lights shows.</summary>
public sealed class RgbZoneSetting
{
    public string Key { get; set; } = "";
    public string Effect { get; set; } = "static";
    public string Color { get; set; } = "FF2D55";
    /// <summary>1 to 100 percent.</summary>
    public int Brightness { get; set; } = 85;
    /// <summary>1 (slow) to 10 (fast), 5 is the effect's own pace.</summary>
    public int Speed { get; set; } = 5;
    /// <summary>The effect runs the other way along the lights.</summary>
    public bool Reverse { get; set; }
    /// <summary>The effect is shown as a few bright stars on a dimmed base instead of the smooth flow.</summary>
    public bool Starry { get; set; }
}

/// <summary>What the person decided for one found device: leave it alone, drive it as another zone, or give it a look of its own.</summary>
public sealed class RgbDeviceSetting
{
    /// <summary>The device's stable key (see <see cref="RgbDevice.Key"/>).</summary>
    public string Key { get; set; } = "";
    /// <summary>False: Shabpa never sends this device anything.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Empty: the zone Shabpa worked out from the device type. Otherwise the zone it is driven as.</summary>
    public string Zone { get; set; } = "";
    /// <summary>True: this device uses <see cref="Look"/> instead of its zone's look.</summary>
    public bool Own { get; set; }
    public RgbZoneSetting Look { get; set; } = new();
    /// <summary>Milliseconds this device is shown ahead of the rest (negative: behind). For a device that lags (a slow USB cooler) so it falls back in step with the others.</summary>
    public int ShiftMs { get; set; }
    /// <summary>All the LEDs of this device show one colour (the colour of the flow at its place), for parts such as a pump head whose few LEDs look out of step as a gradient.</summary>
    public bool OneColour { get; set; }
}

/// <summary>An effect the person made: two to five colours moved in one of a few ways.</summary>
public sealed class RgbCustomEffect
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public List<string> Colors { get; set; } = ["FF2D55", "8B5CF6"];
    public string Pattern { get; set; } = "Wave";
}

public sealed class RgbSettings
{
    /// <summary>The lights are driven by Shabpa while this is on (and it is running).</summary>
    public bool Enabled { get; set; }
    /// <summary>One look for every group; off lets each group have its own.</summary>
    public bool Sync { get; set; } = true;
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 6742;
    public RgbZoneSetting All { get; set; } = new() { Key = "all" };
    public List<RgbZoneSetting> Zones { get; set; } = [];
    public List<RgbCustomEffect> Custom { get; set; } = [];
    /// <summary>What the person decided for each found device (only devices they changed are here).</summary>
    public List<RgbDeviceSetting> Devices { get; set; } = [];
    /// <summary>Header sizes the person typed ("device key|output name" to LEDs). Every other resizable header is set to its maximum, so the whole strip or fan chain lights.</summary>
    public Dictionary<string, int> ZoneSizes { get; set; } = [];
    /// <summary>The effects stand still while a game runs.</summary>
    public bool PauseInGame { get; set; }
    /// <summary>The lights go out when Windows goes to sleep and come back when it wakes.</summary>
    public bool OffOnSleep { get; set; } = true;
    /// <summary>A short effect when Shabpa starts its lights and when Windows shuts down.</summary>
    public bool StartupEffect { get; set; } = true;
    /// <summary>Another lighting program that starts is closed again, so it cannot fight Shabpa.</summary>
    public bool KeepRivalsClosed { get; set; }
    /// <summary>More frames a second, for smoother movement (and a little more work for the PC).</summary>
    public bool Smooth { get; set; } = true;
    /// <summary>How the lights fade in and out ("blaze", "sunrise", "center", "darkwave", "soft", "none").</summary>
    public string Transition { get; set; } = "blaze";

    public const int MaxCustom = 12;

    public RgbDeviceSetting? FindDevice(string key) => Devices.FirstOrDefault(d => d.Key == key);

    /// <summary>The setting of a device, made on first use (enabled, no look of its own).</summary>
    public RgbDeviceSetting DeviceSetting(string key)
    {
        var d = FindDevice(key);
        if (d is not null) return d;
        d = new RgbDeviceSetting { Key = key, Look = new RgbZoneSetting { Key = key, Effect = All.Effect, Color = All.Color, Brightness = All.Brightness, Speed = All.Speed } };
        Devices.Add(d);
        return d;
    }

    /// <summary>The setting of one zone, made on first use as a copy of the "all" look.</summary>
    public RgbZoneSetting ZoneSetting(string key)
    {
        var z = Zones.FirstOrDefault(x => x.Key == key);
        if (z is not null) return z;
        z = new RgbZoneSetting { Key = key, Effect = All.Effect, Color = All.Color, Brightness = All.Brightness, Speed = All.Speed };
        Zones.Add(z);
        return z;
    }

    /// <summary>The look a zone uses right now: the shared one when synced, its own otherwise.</summary>
    public RgbZoneSetting For(string key) => Sync ? All : ZoneSetting(key);
}
