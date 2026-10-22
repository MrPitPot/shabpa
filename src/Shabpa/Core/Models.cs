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
    /// <summary>The city shown on the weather page.</summary>
    public WeatherCity? WeatherCity { get; set; }
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
    public string PingHost { get; set; } = "8.8.8.8";
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
    /// <summary>The newest version the user was already told about, so each release is announced once.</summary>
    public string AnnouncedVersion { get; set; } = "";
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
    /// <summary>Work profiles, their triggers and the run history (phase D).</summary>
    public List<WorkProfile> Profiles { get; set; } = [];
    public List<AutomationTrigger> Triggers { get; set; } = [];
    public List<RunLogEntry> RunLog { get; set; } = [];
    /// <summary>Tickets sent from this PC.</summary>
    public List<TicketRecord> Tickets { get; set; } = [];
}
