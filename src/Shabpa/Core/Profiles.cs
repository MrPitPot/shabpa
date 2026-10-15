using System.Text.Json;

namespace Shabpa.Core;

/// <summary>The only things a profile can do. Anything else in an imported file is dropped, so a file can never make
/// the app run a program or a command.</summary>
public static class ActionKinds
{
    public const string Dim = "dim";
    public const string Volume = "volume";
    public const string RamClean = "ram-clean";
    public const string Focus = "focus";
    public const string Breaks = "breaks";
    public const string SleepMode = "sleep-mode";
    public const string PowerLater = "power-later";
    public const string Notify = "notify";

    public static readonly string[] All = [Dim, Volume, RamClean, Focus, Breaks, SleepMode, PowerLater, Notify];

    /// <summary>Ones that switch the PC off or put it to sleep: confirmed when a profile is imported and when it runs.</summary>
    public static bool IsPower(string kind) => kind is PowerLater or SleepMode;
}

public sealed class ProfileAction
{
    public string Kind { get; set; } = "";
    /// <summary>Dim and volume: percent. Focus, sleep mode and power-later: minutes. Breaks: 1 on, 0 off.</summary>
    public int Value { get; set; }
    /// <summary>Dim: 1 = switch on, 0 = off. Power-later: the <see cref="PowerAction"/> number.</summary>
    public int Value2 { get; set; }
    public string Text { get; set; } = "";
}

public sealed class WorkProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public List<ProfileAction> Actions { get; set; } = [];
}

public enum TriggerKind { Time, GameStart, GameEnd, AppStart }

public sealed class AutomationTrigger
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public TriggerKind Kind { get; set; } = TriggerKind.Time;
    public Guid ProfileId { get; set; }
    public bool Enabled { get; set; } = true;
    public int Hour { get; set; } = 22;
    public int Minute { get; set; }
    public List<DayOfWeek> Days { get; set; } = [];
    public DateTime Created { get; set; } = DateTime.Now;
    public DateTime? LastRun { get; set; }

    /// <summary>Time triggers: true when the time of day has passed since the last run (on one of the chosen days).</summary>
    public bool IsDue(DateTime now)
    {
        if (!Enabled || Kind != TriggerKind.Time) return false;
        var at = now.Date.AddHours(Math.Clamp(Hour, 0, 23)).AddMinutes(Math.Clamp(Minute, 0, 59));
        if (at > now) return false;
        if (Days.Count > 0 && !Days.Contains(now.DayOfWeek)) return false;
        // not for a time that passed before the trigger was made, and never twice for the same time
        if (at <= Created) return false;
        return LastRun is not { } last || last < at;
    }
}

public sealed class RunLogEntry
{
    public DateTime Time { get; set; }
    public string Profile { get; set; } = "";
    public string Source { get; set; } = "";
    public bool Ok { get; set; } = true;
    public List<string> Steps { get; set; } = [];
}

/// <summary>What the profile engine needs from the app. The hub implements it; tests use a fake.</summary>
public interface IProfileHost
{
    void SetDim(bool on, int percent);
    void SetVolume(int percent);
    void CleanRam();
    void StartFocus(int minutes);
    void SetBreaks(bool on);
    void StartSleepMode(int minutes);
    bool SchedulePower(PowerAction action, int minutes);
    void Notify(string text);
}

public sealed record ProfileOutcome(bool Ok, IReadOnlyList<string> Steps);

public static class Profiles
{
    public const int MaxProfiles = 40;
    public const int MaxActions = 20;
    public const int MaxPackBytes = 200_000;

    public static string Describe(ProfileAction a) => a.Kind switch
    {
        ActionKinds.Dim => a.Value2 == 0 ? L.T("نور صفحه به حالت عادی", "Screen back to normal") : L.T($"کم‌نور کردن صفحه تا {Fa.Digits(a.Value.ToString())}٪", $"Dim the screen to {a.Value}%"),
        ActionKinds.Volume => a.Value <= 0 ? L.T("بی‌صدا", "Mute") : L.T($"صدا روی {Fa.Digits(a.Value.ToString())}٪", $"Volume {a.Value}%"),
        ActionKinds.RamClean => L.T("خالی کردن رم", "Free up RAM"),
        ActionKinds.Focus => L.T($"شروع تایمر تمرکز ({Fa.Digits(a.Value.ToString())} دقیقه)", $"Start the focus timer ({a.Value} min)"),
        ActionKinds.Breaks => a.Value == 0 ? L.T("خاموش کردن یادآور استراحت", "Turn break reminders off") : L.T("روشن کردن یادآور استراحت", "Turn break reminders on"),
        ActionKinds.SleepMode => L.T($"حالت شب‌خواب ({Fa.Digits(a.Value.ToString())} دقیقه)", $"Sleep mode ({a.Value} min)"),
        ActionKinds.PowerLater => L.T($"{Fa.ActionSentence((PowerAction)a.Value2)} بعد از {Fa.Digits(a.Value.ToString())} دقیقه", $"{Fa.ActionSentence((PowerAction)a.Value2)} in {a.Value} min"),
        ActionKinds.Notify => L.T("نمایش پیام: ", "Show a message: ") + a.Text,
        _ => a.Kind,
    };

    /// <summary>Clamps values and drops what is not allowed. Returns null if the action must be rejected outright.</summary>
    public static ProfileAction? Clean(ProfileAction a)
    {
        if (!ActionKinds.All.Contains(a.Kind)) return null;
        var c = new ProfileAction { Kind = a.Kind, Text = TaskBook.Clean(a.Text, 120).Replace('\n', ' ') };
        switch (a.Kind)
        {
            case ActionKinds.Dim:
                c.Value = Math.Clamp(a.Value, 5, 92);
                c.Value2 = a.Value2 == 0 ? 0 : 1;
                break;
            case ActionKinds.Volume:
                c.Value = Math.Clamp(a.Value, 0, 100);
                break;
            case ActionKinds.Focus:
                c.Value = Math.Clamp(a.Value, 5, 90);
                break;
            case ActionKinds.Breaks:
                c.Value = a.Value == 0 ? 0 : 1;
                break;
            case ActionKinds.SleepMode:
                c.Value = Math.Clamp(a.Value, 5, 180);
                break;
            case ActionKinds.PowerLater:
                if (!Enum.IsDefined((PowerAction)a.Value2)) return null;
                c.Value = Math.Clamp(a.Value, 1, 600);
                c.Value2 = a.Value2;
                break;
            case ActionKinds.Notify:
                if (c.Text.Length == 0) return null;
                break;
        }
        return c;
    }

    public static WorkProfile? Clean(WorkProfile p)
    {
        var name = TaskBook.Clean(p.Name, 40).Replace('\n', ' ');
        if (name.Length == 0) return null;
        var actions = p.Actions.Select(Clean).Where(a => a is not null).Cast<ProfileAction>().Take(MaxActions).ToList();
        return new WorkProfile { Id = p.Id == Guid.Empty ? Guid.NewGuid() : p.Id, Name = name, Actions = actions };
    }

    public static bool HasPower(WorkProfile p) => p.Actions.Any(a => ActionKinds.IsPower(a.Kind));

    /// <summary>Runs every action; one failing does not stop the rest.</summary>
    public static ProfileOutcome Run(WorkProfile profile, IProfileHost host)
    {
        var steps = new List<string>();
        var ok = true;
        foreach (var a in profile.Actions)
        {
            try
            {
                switch (a.Kind)
                {
                    case ActionKinds.Dim: host.SetDim(a.Value2 != 0, a.Value); break;
                    case ActionKinds.Volume: host.SetVolume(a.Value); break;
                    case ActionKinds.RamClean: host.CleanRam(); break;
                    case ActionKinds.Focus: host.StartFocus(a.Value); break;
                    case ActionKinds.Breaks: host.SetBreaks(a.Value != 0); break;
                    case ActionKinds.SleepMode: host.StartSleepMode(a.Value); break;
                    case ActionKinds.PowerLater:
                        if (!host.SchedulePower((PowerAction)a.Value2, a.Value))
                        {
                            steps.Add("✗ " + Describe(a));
                            ok = false;
                            continue;
                        }
                        break;
                    case ActionKinds.Notify: host.Notify(a.Text); break;
                    default: continue;
                }
                steps.Add("✓ " + Describe(a));
            }
            catch (Exception ex)
            {
                Log.Write($"profile action {a.Kind}: {ex.GetType().Name}: {ex.Message}");
                steps.Add("✗ " + Describe(a));
                ok = false;
            }
        }
        return new ProfileOutcome(ok, steps);
    }

    /// <summary>The profiles a first-time user starts with. They are ordinary profiles and can be edited or deleted.</summary>
    public static List<WorkProfile> Defaults() =>
    [
        new WorkProfile
        {
            Name = L.T("مطالعه", "Study"),
            Actions =
            [
                new ProfileAction { Kind = ActionKinds.Focus, Value = 25 },
                new ProfileAction { Kind = ActionKinds.Breaks, Value = 1 },
                new ProfileAction { Kind = ActionKinds.Volume, Value = 20 },
            ],
        },
        new WorkProfile
        {
            Name = L.T("کار", "Work"),
            Actions =
            [
                new ProfileAction { Kind = ActionKinds.Focus, Value = 50 },
                new ProfileAction { Kind = ActionKinds.Breaks, Value = 1 },
                new ProfileAction { Kind = ActionKinds.Dim, Value2 = 0, Value = 20 },
            ],
        },
        new WorkProfile
        {
            Name = L.T("استراحت", "Rest"),
            Actions =
            [
                new ProfileAction { Kind = ActionKinds.Breaks, Value = 0 },
                new ProfileAction { Kind = ActionKinds.Dim, Value2 = 0, Value = 20 },
                new ProfileAction { Kind = ActionKinds.Volume, Value = 50 },
            ],
        },
        new WorkProfile
        {
            Name = L.T("شب", "Night"),
            Actions =
            [
                new ProfileAction { Kind = ActionKinds.Dim, Value2 = 1, Value = 55 },
                new ProfileAction { Kind = ActionKinds.Volume, Value = 25 },
                new ProfileAction { Kind = ActionKinds.Breaks, Value = 0 },
            ],
        },
    ];

    // ---- export and import ----------------------------------------------------------------------------

    private sealed class Pack
    {
        public int Version { get; set; } = 1;
        public string App { get; set; } = "shabpa";
        public List<WorkProfile> Profiles { get; set; } = [];
    }

    public static string Export(IEnumerable<WorkProfile> profiles) =>
        JsonSerializer.Serialize(new Pack { Profiles = [.. profiles] }, new JsonSerializerOptions { WriteIndented = true });

    /// <summary>Reads a profile file. Everything is validated and clamped; unknown actions are dropped. Returns the
    /// profiles (with fresh ids) and a count of dropped items, or null if the file is not a profile pack.</summary>
    public static (List<WorkProfile> profiles, int dropped)? Import(string json)
    {
        if (json.Length == 0 || json.Length > MaxPackBytes) return null;
        Pack? pack;
        try { pack = JsonSerializer.Deserialize<Pack>(json); }
        catch (JsonException) { return null; }
        if (pack is null || pack.Version != 1 || !string.Equals(pack.App, "shabpa", StringComparison.Ordinal)) return null;
        var result = new List<WorkProfile>();
        var dropped = 0;
        foreach (var p in pack.Profiles.Take(MaxProfiles))
        {
            var before = p.Actions.Count;
            var clean = Clean(p);
            if (clean is null)
            {
                dropped++;
                continue;
            }
            dropped += before - clean.Actions.Count;
            clean.Id = Guid.NewGuid();
            result.Add(clean);
        }
        return (result, dropped);
    }

    /// <summary>A name not used yet: "Night", then "Night (2)".</summary>
    public static string UniqueName(string name, IEnumerable<string> taken)
    {
        var set = new HashSet<string>(taken, StringComparer.CurrentCultureIgnoreCase);
        if (!set.Contains(name)) return name;
        for (var i = 2; ; i++)
        {
            var candidate = $"{name} ({i})";
            if (!set.Contains(candidate)) return candidate;
        }
    }
}
