using System.IO;
using System.Text.Json;
using Shabpa.Core;

namespace Shabpa.Tests;

public class SettingsBackupTests
{
    private static AppState Sample()
    {
        var state = new AppState();
        state.Settings.Tariff = 777;
        state.Settings.Theme = "neon";
        state.Settings.BotEnabled = true;
        state.Settings.BotDeviceId = "device-123";
        state.Settings.BotSecretProtected = "SECRET-PROTECTED-VALUE";
        state.Settings.Support.DeviceId = new string('a', 32);
        state.Settings.Support.AdminKeyProtected = "ADMIN-KEY-PROTECTED";
        state.Settings.WindowPlacement = "10,10,900,700,0";
        state.Settings.Comfort.DimLevel = 0.6;
        var night = new WorkProfile { Name = "Night", Actions = [new ProfileAction { Kind = ActionKinds.Volume, Value = 20 }] };
        var sleepy = new WorkProfile { Name = "Sleepy", Actions = [new ProfileAction { Kind = ActionKinds.PowerLater, Value = 30, Value2 = 0 }] };
        state.Profiles.AddRange([night, sleepy]);
        state.Triggers.Add(new AutomationTrigger { ProfileId = night.Id, Hour = 23, Kind = TriggerKind.Time });
        state.Triggers.Add(new AutomationTrigger { ProfileId = sleepy.Id, Hour = 23, Kind = TriggerKind.Time });
        state.Tasks.Add(new TodoItem { Title = "private task about my doctor" });
        return state;
    }

    [Fact]
    public void The_exported_file_contains_no_secrets_or_identity()
    {
        var json = SettingsBackup.Export(Sample(), includeProfiles: true, includeTasks: false, DateTime.Now);
        Assert.DoesNotContain("SECRET-PROTECTED-VALUE", json);
        Assert.DoesNotContain("ADMIN-KEY-PROTECTED", json);
        Assert.DoesNotContain("device-123", json);
        Assert.DoesNotContain(new string('a', 32), json);
        Assert.DoesNotContain("10,10,900,700", json);
        Assert.Contains("777", json);
    }

    [Fact]
    public void Tasks_are_only_in_the_file_when_asked_for()
    {
        var without = SettingsBackup.Export(Sample(), includeProfiles: false, includeTasks: false, DateTime.Now);
        Assert.DoesNotContain("doctor", without);
        Assert.DoesNotContain("Night", without);
        var with = SettingsBackup.Export(Sample(), includeProfiles: false, includeTasks: true, DateTime.Now);
        Assert.Contains("doctor", with);
    }

    [Fact]
    public void Import_applies_settings_but_keeps_this_pcs_own_secrets()
    {
        var json = SettingsBackup.Export(Sample(), includeProfiles: true, includeTasks: true, DateTime.Now);
        var target = new AppState();
        target.Settings.BotEnabled = true;
        target.Settings.BotDeviceId = "mine";
        target.Settings.BotSecretProtected = "MY-SECRET";
        target.Settings.Support.AdminKeyProtected = "MY-ADMIN";
        target.Settings.WindowPlacement = "1,2,3,4,0";

        var pack = SettingsBackup.Read(json)!;
        var summary = SettingsBackup.Apply(pack, target);
        Assert.Equal(777, target.Settings.Tariff);
        Assert.Equal("neon", target.Settings.Theme);
        Assert.Equal("mine", target.Settings.BotDeviceId);
        Assert.Equal("MY-SECRET", target.Settings.BotSecretProtected);
        Assert.Equal("MY-ADMIN", target.Settings.Support.AdminKeyProtected);
        Assert.Equal("1,2,3,4,0", target.Settings.WindowPlacement);
        Assert.Equal(2, summary.Profiles);
        Assert.Equal(1, summary.Tasks);
        // the automation that pointed at a profile able to switch the PC off is not brought back
        Assert.Equal(1, summary.Triggers);
        Assert.Equal("Night", target.Profiles.First(p => p.Id == target.Triggers[0].ProfileId).Name);
    }

    [Fact]
    public void Imported_values_are_brought_back_into_range()
    {
        var pack = new SettingsPack();
        pack.Settings.CountdownSeconds = 0;
        pack.Settings.Comfort.DimLevel = 5;
        var state = new AppState();
        SettingsBackup.Apply(pack, state);
        Assert.Equal(5, state.Settings.CountdownSeconds);
        Assert.Equal(0.92, state.Settings.Comfort.DimLevel);
    }

    [Fact]
    public void Name_and_birthday_never_travel_in_a_settings_file()
    {
        var state = new AppState();
        state.Settings.Birthday = new BirthdaySettings { Name = "Ali", Month = 7, Day = 26 };
        var json = SettingsBackup.Export(state, false, false, new DateTime(2026, 10, 22));
        Assert.DoesNotContain("Ali", json);
        var pack = SettingsBackup.Read(json)!;
        pack.Settings.Birthday = new BirthdaySettings { Name = "Other", Month = 1, Day = 1 };
        SettingsBackup.Apply(pack, state);
        Assert.Equal("Ali", state.Settings.Birthday.Name);
    }

    [Fact]
    public void Damaged_dns_notes_in_a_backup_are_cleaned()
    {
        var pack = new SettingsPack();
        pack.Settings.DnsNotes = new Dictionary<string, string> { ["a/b"] = new string('x', 500), ["c/d"] = null!, [""] = "no key" };
        var state = new AppState();
        SettingsBackup.Apply(pack, state);
        Assert.Equal(["a/b"], state.Settings.DnsNotes.Keys);
        Assert.Equal(140, state.Settings.DnsNotes["a/b"].Length);
    }

    [Fact]
    public void A_profile_with_an_unknown_action_is_cleaned_on_import()
    {
        var pack = new SettingsPack
        {
            Profiles = [new WorkProfile { Name = "Bad", Actions = [new ProfileAction { Kind = "run-program", Text = "calc" }, new ProfileAction { Kind = ActionKinds.RamClean }] }],
        };
        var state = new AppState();
        SettingsBackup.Apply(pack, state);
        var kept = Assert.Single(Assert.Single(state.Profiles).Actions);
        Assert.Equal(ActionKinds.RamClean, kept.Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("{\"Version\":9,\"App\":\"shabpa\",\"Settings\":{}}")]
    [InlineData("{\"Version\":1,\"App\":\"someone-else\",\"Settings\":{}}")]
    public void Foreign_files_are_refused(string json) => Assert.Null(SettingsBackup.Read(json));

    [Fact]
    public void A_huge_file_is_refused()
    {
        var big = "{\"Version\":1,\"App\":\"shabpa\",\"Settings\":{},\"X\":\"" + new string('x', SettingsBackup.MaxBytes) + "\"}";
        Assert.Null(SettingsBackup.Read(big));
    }

    [Fact]
    public void An_old_state_file_without_the_new_sections_still_loads()
    {
        // state.json written by 3.0.x has none of the new lists; loading it must give empty lists, not nulls or errors
        var folder = Path.Combine(Path.GetTempPath(), "shabpa-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "state.json"), "{\"Settings\":{\"Tariff\":450,\"Theme\":\"neon\"},\"Queue\":[],\"Energy\":[]}");
            var store = new Store(folder);
            store.Load();
            Assert.Equal(450, store.State.Settings.Tariff);
            Assert.NotNull(store.State.Tasks);
            Assert.NotNull(store.State.Backups);
            Assert.NotNull(store.State.Profiles);
            Assert.Null(store.State.StartupBaseline);
            Assert.False(store.State.Settings.BotEnabled);
            Assert.Equal(25, store.State.Settings.Focus.FocusMinutes);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void The_old_telegram_token_field_in_a_state_file_is_ignored()
    {
        var json = "{\"Settings\":{\"TelegramTokenProtected\":\"OLD\",\"TelegramChatId\":5,\"Tariff\":300}}";
        var state = JsonSerializer.Deserialize<AppState>(json)!;
        Assert.Equal(300, state.Settings.Tariff);
        var written = JsonSerializer.Serialize(state);
        Assert.DoesNotContain("TelegramToken", written);
        Assert.DoesNotContain("OLD", written);
    }
}
