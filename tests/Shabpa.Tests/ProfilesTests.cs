using Shabpa.Core;

namespace Shabpa.Tests;

internal sealed class FakeProfileHost : IProfileHost
{
    public readonly List<string> Calls = [];
    public bool PowerOk = true;
    public bool Explode;

    private void Note(string s)
    {
        if (Explode) throw new InvalidOperationException("boom");
        Calls.Add(s);
    }

    public void SetDim(bool on, int percent) => Note($"dim {on} {percent}");
    public void SetVolume(int percent) => Note($"volume {percent}");
    public void CleanRam() => Note("ram");
    public void StartFocus(int minutes) => Note($"focus {minutes}");
    public void SetBreaks(bool on) => Note($"breaks {on}");
    public void StartSleepMode(int minutes) => Note($"sleep {minutes}");
    public bool SchedulePower(PowerAction action, int minutes)
    {
        Note($"power {action} {minutes}");
        return PowerOk;
    }
    public void Notify(string text) => Note($"notify {text}");
}

public class ProfileEngineTests
{
    [Fact]
    public void A_profile_runs_its_actions_in_order()
    {
        var host = new FakeProfileHost();
        var profile = new WorkProfile
        {
            Name = "Night",
            Actions =
            [
                new ProfileAction { Kind = ActionKinds.Dim, Value = 60, Value2 = 1 },
                new ProfileAction { Kind = ActionKinds.Volume, Value = 25 },
                new ProfileAction { Kind = ActionKinds.Focus, Value = 30 },
                new ProfileAction { Kind = ActionKinds.Breaks, Value = 0 },
                new ProfileAction { Kind = ActionKinds.RamClean },
                new ProfileAction { Kind = ActionKinds.Notify, Text = "go" },
            ],
        };
        var outcome = Profiles.Run(profile, host);
        Assert.True(outcome.Ok);
        Assert.Equal(["dim True 60", "volume 25", "focus 30", "breaks False", "ram", "notify go"], host.Calls);
        Assert.Equal(6, outcome.Steps.Count);
        Assert.All(outcome.Steps, s => Assert.StartsWith("✓", s));
    }

    [Fact]
    public void One_failing_action_does_not_stop_the_rest()
    {
        var host = new FakeProfileHost { PowerOk = false };
        var profile = new WorkProfile
        {
            Name = "x",
            Actions =
            [
                new ProfileAction { Kind = ActionKinds.PowerLater, Value = 10, Value2 = (int)PowerAction.Sleep },
                new ProfileAction { Kind = ActionKinds.Volume, Value = 10 },
            ],
        };
        var outcome = Profiles.Run(profile, host);
        Assert.False(outcome.Ok);
        Assert.StartsWith("✗", outcome.Steps[0]);
        Assert.StartsWith("✓", outcome.Steps[1]);
    }

    [Fact]
    public void An_exception_in_an_action_is_caught_and_reported()
    {
        var host = new FakeProfileHost { Explode = true };
        var outcome = Profiles.Run(new WorkProfile { Name = "x", Actions = [new ProfileAction { Kind = ActionKinds.RamClean }] }, host);
        Assert.False(outcome.Ok);
        Assert.StartsWith("✗", Assert.Single(outcome.Steps));
    }

    [Fact]
    public void Values_are_clamped_and_unknown_kinds_are_dropped()
    {
        Assert.Equal(92, Profiles.Clean(new ProfileAction { Kind = ActionKinds.Dim, Value = 500, Value2 = 1 })!.Value);
        Assert.Equal(5, Profiles.Clean(new ProfileAction { Kind = ActionKinds.Dim, Value = -9, Value2 = 1 })!.Value);
        Assert.Equal(100, Profiles.Clean(new ProfileAction { Kind = ActionKinds.Volume, Value = 1000 })!.Value);
        Assert.Equal(600, Profiles.Clean(new ProfileAction { Kind = ActionKinds.PowerLater, Value = 99999, Value2 = (int)PowerAction.Shutdown })!.Value);
        Assert.Null(Profiles.Clean(new ProfileAction { Kind = "run-program", Text = "calc.exe" }));
        Assert.Null(Profiles.Clean(new ProfileAction { Kind = ActionKinds.PowerLater, Value = 5, Value2 = 99 }));
        Assert.Null(Profiles.Clean(new ProfileAction { Kind = ActionKinds.Notify, Text = "  " }));
    }

    [Fact]
    public void Power_actions_are_recognised_so_they_can_be_confirmed()
    {
        Assert.True(Profiles.HasPower(new WorkProfile { Actions = [new ProfileAction { Kind = ActionKinds.SleepMode, Value = 30 }] }));
        Assert.True(Profiles.HasPower(new WorkProfile { Actions = [new ProfileAction { Kind = ActionKinds.PowerLater, Value = 30, Value2 = 0 }] }));
        Assert.False(Profiles.HasPower(new WorkProfile { Actions = [new ProfileAction { Kind = ActionKinds.Volume, Value = 30 }] }));
    }

    [Fact]
    public void Default_profiles_are_valid_and_never_switch_the_pc_off()
    {
        var defaults = Profiles.Defaults();
        Assert.True(defaults.Count >= 3);
        foreach (var p in defaults)
        {
            Assert.NotNull(Profiles.Clean(p));
            Assert.False(Profiles.HasPower(p));
        }
    }
}

public class ProfileImportTests
{
    [Fact]
    public void Export_and_import_round_trip_with_fresh_ids()
    {
        var original = new List<WorkProfile>
        {
            new() { Name = "Study", Actions = [new ProfileAction { Kind = ActionKinds.Focus, Value = 40 }, new ProfileAction { Kind = ActionKinds.Volume, Value = 15 }] },
        };
        var json = Profiles.Export(original);
        var (profiles, dropped) = Profiles.Import(json)!.Value;
        Assert.Equal(0, dropped);
        var p = Assert.Single(profiles);
        Assert.Equal("Study", p.Name);
        Assert.Equal(2, p.Actions.Count);
        Assert.NotEqual(original[0].Id, p.Id);
    }

    [Fact]
    public void A_file_that_asks_to_run_a_program_loses_that_action()
    {
        const string json = """
        {"Version":1,"App":"shabpa","Profiles":[{"Name":"Evil","Actions":[
          {"Kind":"run-program","Text":"powershell -enc AAAA"},
          {"Kind":"exec","Text":"cmd /c del *"},
          {"Kind":"volume","Value":999}]}]}
        """;
        var (profiles, dropped) = Profiles.Import(json)!.Value;
        Assert.Equal(2, dropped);
        var actions = Assert.Single(profiles).Actions;
        var only = Assert.Single(actions);
        Assert.Equal(ActionKinds.Volume, only.Kind);
        Assert.Equal(100, only.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"Version\":2,\"App\":\"shabpa\",\"Profiles\":[]}")]
    [InlineData("{\"Version\":1,\"App\":\"other\",\"Profiles\":[]}")]
    [InlineData("[1,2,3]")]
    public void Foreign_or_broken_files_are_refused(string json) => Assert.Null(Profiles.Import(json));

    [Fact]
    public void An_oversized_file_is_refused()
    {
        var big = "{\"Version\":1,\"App\":\"shabpa\",\"Profiles\":[],\"Padding\":\"" + new string('x', Profiles.MaxPackBytes) + "\"}";
        Assert.Null(Profiles.Import(big));
    }

    [Fact]
    public void Too_many_profiles_and_actions_are_cut_to_the_limits()
    {
        var many = new List<WorkProfile>();
        for (var i = 0; i < 45; i++)
            many.Add(new WorkProfile { Name = "p" + i, Actions = [.. Enumerable.Range(0, 25).Select(_ => new ProfileAction { Kind = ActionKinds.RamClean })] });
        var (profiles, _) = Profiles.Import(Profiles.Export(many))!.Value;
        Assert.Equal(Profiles.MaxProfiles, profiles.Count);
        Assert.All(profiles, p => Assert.Equal(Profiles.MaxActions, p.Actions.Count));
    }

    [Fact]
    public void Names_are_made_unique()
    {
        Assert.Equal("Night", Profiles.UniqueName("Night", ["Work"]));
        Assert.Equal("Night (2)", Profiles.UniqueName("Night", ["night"]));
        Assert.Equal("Night (3)", Profiles.UniqueName("Night", ["Night", "Night (2)"]));
    }
}

public class AutomationTriggerTests
{
    [Fact]
    public void A_time_trigger_fires_once_per_day_after_its_time_and_not_for_a_time_before_it_existed()
    {
        var t = new AutomationTrigger { Kind = TriggerKind.Time, Hour = 22, Minute = 0, Created = new DateTime(2026, 10, 10, 23, 0, 0) };
        Assert.False(t.IsDue(new DateTime(2026, 10, 10, 23, 30, 0))); // 22:00 passed before it was created
        Assert.False(t.IsDue(new DateTime(2026, 10, 11, 21, 0, 0)));
        Assert.True(t.IsDue(new DateTime(2026, 10, 11, 22, 0, 30)));
        t.LastRun = new DateTime(2026, 10, 11, 22, 0, 30);
        Assert.False(t.IsDue(new DateTime(2026, 10, 11, 23, 0, 0)));
        Assert.True(t.IsDue(new DateTime(2026, 10, 12, 22, 1, 0)));
    }

    [Fact]
    public void Days_limit_a_time_trigger_and_paused_ones_never_fire()
    {
        var friday = new DateTime(2026, 10, 16, 22, 5, 0);
        var t = new AutomationTrigger { Kind = TriggerKind.Time, Hour = 22, Days = [DayOfWeek.Monday], Created = friday.AddDays(-5) };
        Assert.False(t.IsDue(friday));
        t.Days = [DayOfWeek.Friday];
        Assert.True(t.IsDue(friday));
        t.Enabled = false;
        Assert.False(t.IsDue(friday));
        Assert.False(new AutomationTrigger { Kind = TriggerKind.GameStart, Created = friday.AddDays(-1) }.IsDue(friday));
    }
}
