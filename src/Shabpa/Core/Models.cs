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
    /// <summary>Monthly internet allowance in GB; 0 = no limit.</summary>
    public double MonthlyLimitGb { get; set; }
    /// <summary>"yyyy-MM:level" of the last budget warning, so each level is announced once a month.</summary>
    public string BudgetWarned { get; set; } = "";
    public bool TrayNoticeShown { get; set; }
    /// <summary>The launchers found the last time the user asked Shabpa to look.</summary>
    public List<LauncherInfo> Launchers { get; set; } = [];
    public bool LaunchersScanned { get; set; }
    public string RecorderProcesses { get; set; } = "obs64, obs32, obs, Streamlabs OBS, bdcam, CamtasiaStudio, CamRecorder, XSplit.Core, Action, ShareX, ffmpeg";
    public string TelegramTokenProtected { get; set; } = "";
    public long TelegramChatId { get; set; }
    public string TelegramUser { get; set; } = "";
    public string ProxyUrl { get; set; } = "";
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
}
