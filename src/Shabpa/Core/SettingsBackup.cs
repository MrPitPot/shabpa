using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shabpa.Core;

/// <summary>A file with the app's settings (and, if the person chooses, profiles and tasks), for moving to another PC
/// or restoring after a reinstall. It never contains anything that identifies this PC to a server or unlocks anything:
/// the device secret, the support admin key and the Telegram link are left out.</summary>
public sealed class SettingsPack
{
    public int Version { get; set; } = 1;
    public string App { get; set; } = "shabpa";
    public string Created { get; set; } = "";
    public Settings Settings { get; set; } = new();
    public List<WorkProfile>? Profiles { get; set; }
    public List<AutomationTrigger>? Triggers { get; set; }
    public List<TodoItem>? Tasks { get; set; }
    /// <summary>Clicks taught for launchers (travel with the profiles).</summary>
    public List<LauncherMacro>? Macros { get; set; }
}

public sealed record ImportSummary(int Settings, int Profiles, int Triggers, int Tasks);

public static class SettingsBackup
{
    public const int MaxBytes = 2_000_000;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Removes everything that is secret or tied to this PC's identity.</summary>
    internal static void StripSecrets(Settings s)
    {
        s.BotEnabled = false;
        s.BotDeviceId = "";
        s.BotSecretProtected = "";
        s.Support.DeviceId = "";
        s.Support.AdminKeyProtected = "";
        s.WindowPlacement = "";
        s.Birthday = new(); // a name and a birthday are personal: a shared settings file does not carry them
    }

    private static Settings Copy(Settings s) =>
        JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(s, Json), Json) ?? new Settings();

    public static string Export(AppState state, bool includeProfiles, bool includeTasks, DateTime now)
    {
        var pack = new SettingsPack { Created = now.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture), Settings = Copy(state.Settings) };
        StripSecrets(pack.Settings);
        if (includeProfiles)
        {
            pack.Macros = [.. state.Macros];
            pack.Profiles = [.. state.Profiles];
            pack.Triggers = [.. state.Triggers.Select(t => new AutomationTrigger
            {
                Id = t.Id, Kind = t.Kind, ProfileId = t.ProfileId, Enabled = t.Enabled, Hour = t.Hour, Minute = t.Minute, Days = [.. t.Days], Created = t.Created,
            })];
        }
        if (includeTasks) pack.Tasks = [.. state.Tasks];
        return JsonSerializer.Serialize(pack, Json);
    }

    /// <summary>Reads and checks a settings file; null if it is not one of ours.</summary>
    public static SettingsPack? Read(string json)
    {
        if (json.Length == 0 || json.Length > MaxBytes) return null;
        try
        {
            var pack = JsonSerializer.Deserialize<SettingsPack>(json, Json);
            if (pack is null || pack.Version != 1 || !string.Equals(pack.App, "shabpa", StringComparison.Ordinal) || pack.Settings is null) return null;
            return pack;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Applies a pack to the current state. This PC's own secrets and window position are kept; imported
    /// profiles go through the same validation as a profile file.</summary>
    public static ImportSummary Apply(SettingsPack pack, AppState state)
    {
        var keep = state.Settings;
        var incoming = Copy(pack.Settings);
        incoming.BotEnabled = keep.BotEnabled;
        incoming.BotDeviceId = keep.BotDeviceId;
        incoming.BotSecretProtected = keep.BotSecretProtected;
        incoming.BotEndpoint = keep.BotEndpoint;
        incoming.Support.DeviceId = keep.Support.DeviceId;
        incoming.Support.AdminKeyProtected = keep.Support.AdminKeyProtected;
        incoming.WindowPlacement = keep.WindowPlacement;
        incoming.Birthday = keep.Birthday;
        // values that would be unsafe or senseless are brought back into range
        incoming.CountdownSeconds = Math.Clamp(incoming.CountdownSeconds, 5, 600);
        incoming.Comfort.DimLevel = Math.Clamp(incoming.Comfort.DimLevel, 0.05, 0.92);
        incoming.Comfort.DimKelvin = Math.Clamp(incoming.Comfort.DimKelvin, ComfortPresets.MinKelvin, ComfortPresets.MaxKelvin);
        incoming.Comfort.Presets ??= [];
        // notes from a hand-edited or damaged file: no nulls, a sane count and length
        incoming.DnsNotes = (incoming.DnsNotes ?? []).Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && kv.Value is not null).Take(200)
            .ToDictionary(kv => kv.Key, kv => kv.Value.Length > 140 ? kv.Value[..140] : kv.Value);
        state.Settings = incoming;

        var profiles = 0;
        var triggers = 0;
        if (pack.Profiles is not null)
        {
            var map = new Dictionary<Guid, Guid>();
            state.Profiles.Clear();
            foreach (var p in pack.Profiles.Take(Profiles.MaxProfiles))
            {
                if (Profiles.Clean(p) is not { } c) continue;
                c.Id = Guid.NewGuid();
                map[p.Id] = c.Id;
                state.Profiles.Add(c);
            }
            profiles = state.Profiles.Count;
            state.Triggers.Clear();
            foreach (var t in (pack.Triggers ?? []).Take(100))
            {
                if (!map.TryGetValue(t.ProfileId, out var id)) continue;
                var target = state.Profiles.First(p => p.Id == id);
                if (Profiles.HasPower(target)) continue; // a profile that can switch the PC off is never automatic
                state.Triggers.Add(new AutomationTrigger
                {
                    Kind = t.Kind, ProfileId = id, Enabled = t.Enabled, Hour = Math.Clamp(t.Hour, 0, 23), Minute = Math.Clamp(t.Minute, 0, 59),
                    Days = [.. t.Days.Distinct()], Created = DateTime.Now,
                });
                triggers++;
            }
            state.Settings.ProfilesSeeded = true;
        }

        if (pack.Macros is not null)
        {
            // taught clicks are only positions and waits; they are brought back into sane ranges and capped
            foreach (var m in pack.Macros.Where(m => m.LauncherKey.Length is > 0 and <= 80 && m.WindowProcess.Length <= 120 && m.Steps.Count > 0).Take(20))
            {
                m.Width = Math.Clamp(m.Width, 100, 20000);
                m.Height = Math.Clamp(m.Height, 100, 20000);
                m.Dpi = Math.Clamp(m.Dpi, 96, 600);
                m.Steps = [.. m.Steps.Take(MacroMath.MaxSteps).Select(st => new MacroStep
                {
                    Dx = Math.Clamp(st.Dx, -100, 20000), Dy = Math.Clamp(st.Dy, -100, 20000), Fx = Math.Clamp(st.Fx, 0, 1), Fy = Math.Clamp(st.Fy, 0, 1),
                    WaitMs = Math.Clamp(st.WaitMs, 0, 600000), Button = st.Button is "right" or "wheel" ? st.Button : "left", Wheel = Math.Clamp(st.Wheel, -50, 50),
                })];
                state.Macros.RemoveAll(x => x.LauncherKey == m.LauncherKey);
                state.Macros.Add(m);
            }
        }

        var tasks = 0;
        if (pack.Tasks is not null)
        {
            foreach (var t in pack.Tasks.Take(TaskBook.MaxTasks))
            {
                var title = TaskBook.Clean(t.Title, TaskBook.MaxTitle).Replace('\n', ' ');
                if (title.Length == 0 || state.Tasks.Any(x => x.Id == t.Id)) continue;
                state.Tasks.Add(new TodoItem
                {
                    Id = t.Id == Guid.Empty ? Guid.NewGuid() : t.Id, Title = title, Notes = TaskBook.Clean(t.Notes, TaskBook.MaxNotes), Category = TaskBook.Clean(t.Category, 40),
                    Due = t.Due, RemindBefore = Math.Clamp(t.RemindBefore, -1, 7 * 24 * 60), Important = t.Important, Created = t.Created, Done = t.Done, DoneAt = t.DoneAt,
                });
                tasks++;
            }
        }
        return new ImportSummary(1, profiles, triggers, tasks);
    }
}
