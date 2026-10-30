using System.IO;
using Shabpa.Core;
using Xunit;

namespace Shabpa.Tests;

/// <summary>The game tweaks: nothing here touches the real registry or power plan, a fake stands in for both.</summary>
public class GameTuneTests
{
    private sealed class FakeRegistry : ITuneRegistry
    {
        public readonly Dictionary<string, string> Values = [];
        public bool Deny;
        public bool Throw;
        private static string K(TuneHive h, string p, string n) => $"{h}|{p}|{n}";
        public string? Read(TuneHive hive, string path, string name) => Values.GetValueOrDefault(K(hive, path, name));
        public void Write(TuneHive hive, string path, string name, string encoded)
        {
            if (Deny && hive == TuneHive.LocalMachine) throw new UnauthorizedAccessException();
            if (Throw) throw new IOException("disk");
            Values[K(hive, path, name)] = encoded;
        }
        public void Delete(TuneHive hive, string path, string name) => Values.Remove(K(hive, path, name));
    }

    private sealed class FakePcie(uint? value) : IPcieLink
    {
        public uint? Current = value;
        public uint? Read() => Current;
        public bool Write(uint v) { Current = v; return true; }
    }

    private static (GameTuner tuner, FakeRegistry reg, Dictionary<string, string> backup, int[] saves) Make(FakePcie? pcie = null)
    {
        var reg = new FakeRegistry();
        var backup = new Dictionary<string, string>();
        var saves = new int[1];
        return (new GameTuner(reg, pcie ?? new FakePcie(1), backup, () => saves[0]++), reg, backup, saves);
    }

    [Fact]
    public void Every_tweak_has_a_name_a_tip_and_a_known_tab_in_both_languages()
    {
        var all = GameTweaks.All(8_000_000);
        Assert.Equal(all.Count, all.Select(t => t.Id).Distinct().Count());
        foreach (var t in all)
        {
            Assert.False(string.IsNullOrWhiteSpace(t.NameFa)); Assert.False(string.IsNullOrWhiteSpace(t.NameEn));
            Assert.True(t.TipFa.Length > 30 && t.TipEn.Length > 30, t.Id);
            Assert.Contains(t.Tab, new[] { GameTweaks.TabCpu, GameTweaks.TabSystem, GameTweaks.TabGraphics });
            if (t.Kind == TuneKind.Registry) Assert.NotEmpty(t.Entries);
        }
    }

    [Fact]
    public void Turning_on_keeps_the_persons_value_and_turning_off_puts_it_back()
    {
        var (tuner, reg, backup, _) = Make();
        var tweak = GameTweaks.All().First(t => t.Id == "responsiveness");
        var entry = tweak.Entries[0];
        reg.Values[$"{entry.Hive}|{entry.Path}|{entry.Name}"] = "d:33";     // the person's own odd value, not Windows' default
        Assert.False(tuner.IsOn(tweak));
        Assert.Equal(TuneOutcome.Done, tuner.Apply(tweak));
        Assert.True(tuner.IsOn(tweak));
        Assert.True(tuner.HasBackup(tweak));
        Assert.Equal(TuneOutcome.Done, tuner.Revert(tweak));
        Assert.False(tuner.IsOn(tweak));
        Assert.Equal("d:33", reg.Values[$"{entry.Hive}|{entry.Path}|{entry.Name}"]);   // theirs, not the default 20
        Assert.Empty(backup);
    }

    [Fact]
    public void A_value_that_was_not_there_is_removed_again()
    {
        var (tuner, reg, _, _) = Make();
        var tweak = GameTweaks.All().First(t => t.Id == "gamemode");
        Assert.Equal(TuneOutcome.Done, tuner.Apply(tweak));
        Assert.True(tuner.IsOn(tweak));
        Assert.Equal(TuneOutcome.Done, tuner.Revert(tweak));
        Assert.Empty(reg.Values);
    }

    [Fact]
    public void Turning_on_twice_never_overwrites_the_first_backup()
    {
        var (tuner, reg, backup, _) = Make();
        var tweak = GameTweaks.All().First(t => t.Id == "capture");
        var e = tweak.Entries[0];
        reg.Values[$"{e.Hive}|{e.Path}|{e.Name}"] = "d:1";
        tuner.Apply(tweak);
        tuner.Apply(tweak);                                                  // its own "on" value must not become the backup
        tuner.Revert(tweak);
        Assert.Equal("d:1", reg.Values[$"{e.Hive}|{e.Path}|{e.Name}"]);
        Assert.Empty(backup);
    }

    [Fact]
    public void The_backup_is_saved_before_the_first_change()
    {
        var (tuner, reg, backup, saves) = Make();
        var tweak = GameTweaks.All().First(t => t.Id == "capture");
        reg.Throw = true;                                                    // the very first write fails
        Assert.Equal(TuneOutcome.Failed, tuner.Apply(tweak));
        Assert.True(saves[0] >= 1);                                          // the original was already on its way to disk
        Assert.NotEmpty(backup);
        Assert.Empty(reg.Values);                                            // and nothing was changed
    }

    [Fact]
    public void Without_administrator_rights_a_machine_wide_tweak_says_so_and_changes_nothing()
    {
        var (tuner, reg, _, _) = Make();
        reg.Deny = true;
        var tweak = GameTweaks.All().First(t => t.NeedsAdmin && t.Kind == TuneKind.Registry);
        Assert.Equal(TuneOutcome.NeedsAdmin, tuner.Apply(tweak));
        Assert.Empty(reg.Values);
    }

    [Fact]
    public void Network_brake_value_is_the_full_32_bit_number()
    {
        var tweak = GameTweaks.All().First(t => t.Id == "net-throttle");
        Assert.Equal("d:4294967295", tweak.Entries[0].On);
    }

    [Fact]
    public void Service_merge_uses_the_memory_size_it_was_given_and_needs_a_restart()
    {
        var tweak = GameTweaks.All(16_777_216).First(t => t.Id == "svchost");
        Assert.True(tweak.NeedsRestart);
        Assert.Equal("d:16777216", tweak.Entries[0].On);
    }

    [Fact]
    public void Pcie_saving_goes_off_and_the_old_setting_returns()
    {
        var pcie = new FakePcie(2);
        var (tuner, _, backup, _) = Make(pcie);
        var tweak = GameTweaks.All().First(t => t.Kind == TuneKind.PcieLink);
        Assert.False(tuner.IsOn(tweak));
        Assert.Equal(TuneOutcome.Done, tuner.Apply(tweak));
        Assert.Equal(0u, pcie.Current);
        Assert.True(tuner.IsOn(tweak));
        Assert.Equal(TuneOutcome.Done, tuner.Revert(tweak));
        Assert.Equal(2u, pcie.Current);
        Assert.Empty(backup);
    }

    [Fact]
    public void Pcie_that_cannot_be_read_fails_without_writing()
    {
        var pcie = new FakePcie(null);
        var (tuner, _, _, _) = Make(pcie);
        Assert.Equal(TuneOutcome.Failed, tuner.Apply(GameTweaks.All().First(t => t.Kind == TuneKind.PcieLink)));
        Assert.Null(pcie.Current);
    }

    [Theory]
    [InlineData("AMD Ryzen 9 7950X3D 16-Core Processor", true)]
    [InlineData("AMD Ryzen 9 9900X3D 12-Core Processor", true)]
    [InlineData("AMD Ryzen 7 7800X3D 8-Core Processor", false)]     // one chiplet: nothing to choose between
    [InlineData("12th Gen Intel(R) Core(TM) i7-12700K", false)]
    [InlineData("", false)]
    public void Only_two_chiplet_x3d_processors_count(string name, bool expected) => Assert.Equal(expected, GamePriority.IsDualX3d(name));

    [Fact]
    public void Priority_never_touches_programs_that_are_not_games()
    {
        Assert.Equal(0, GamePriority.Set("explorer", high: true));
        Assert.Equal(0, GamePriority.Set("", high: true));
    }

    [Fact]
    public void Old_state_files_without_a_backup_list_load_with_an_empty_one()
    {
        var state = System.Text.Json.JsonSerializer.Deserialize<AppState>("{}")!;
        Assert.NotNull(state.TuneBackup);
        Assert.Empty(state.TuneBackup);
        Assert.NotNull(state.EventsHandled);
    }
}

public class NightLightTests
{
    [Fact]
    public void Daylight_changes_nothing()
    {
        Assert.Equal((1.0, 1.0, 1.0), NightLight.WhitePoint(6500));
        Assert.Equal((1.0, 1.0, 1.0), NightLight.WhitePoint(9000));
    }

    [Theory]
    [InlineData(5000)]
    [InlineData(3400)]
    [InlineData(2700)]
    [InlineData(1900)]
    public void Warmer_means_less_blue_than_green_than_red_and_stays_in_what_windows_accepts(int kelvin)
    {
        var (r, g, b) = NightLight.WhitePoint(kelvin);
        Assert.True(r >= g && g >= b, $"{kelvin}: {r} {g} {b}");
        Assert.True(r <= 1 && b >= NightLight.Floor);
    }

    [Fact]
    public void Lower_temperature_never_means_a_cooler_blue()
    {
        var last = 2.0;
        foreach (var k in new[] { 6400, 5600, 5000, 4000, 3400, 2700, 2200, 1900 })
        {
            var b = NightLight.WhitePoint(k).b;
            Assert.True(b <= last + 1e-9);
            last = b;
        }
    }

    [Fact]
    public void The_curve_has_768_entries_rising_from_zero_to_the_scaled_top()
    {
        var ramp = NightLight.Ramp((1, 0.8, 0.6));
        Assert.Equal(768, ramp.Length);
        Assert.Equal(0, ramp[0]); Assert.Equal(0, ramp[256]); Assert.Equal(0, ramp[512]);
        Assert.Equal(65535, ramp[255]);
        Assert.Equal((ushort)Math.Round(65535 * 0.8), ramp[511]);
        Assert.Equal((ushort)Math.Round(65535 * 0.6), ramp[767]);
        for (var i = 1; i < 256; i++) Assert.True(ramp[512 + i] >= ramp[512 + i - 1]);
    }
}

public class HealthErrorsResolvedTests
{
    private static EventSummary Ev(string provider, int id, DateTime last) => new(provider, id, "System", false, 3, last, "m", false);

    [Fact]
    public void An_error_that_stopped_before_the_last_start_of_windows_is_set_aside()
    {
        var boot = new DateTime(2026, 10, 30, 9, 0, 0);
        var events = new[] { Ev("a", 1, boot.AddHours(-5)), Ev("b", 2, boot.AddMinutes(10)) };
        var (active, over) = Health.SplitResolved(events, boot, new Dictionary<string, DateTime>());
        Assert.Equal("b", Assert.Single(active).Provider);
        Assert.Equal("a", Assert.Single(over).Provider);
    }

    [Fact]
    public void Marked_as_fixed_hides_it_until_something_newer_happens()
    {
        var boot = new DateTime(2026, 10, 30, 9, 0, 0);
        var e = Ev("svc", 7034, boot.AddHours(1));
        var handled = new Dictionary<string, DateTime> { [e.Key] = e.Last };
        Assert.Single(Health.SplitResolved([e], boot, handled).over);
        var again = e with { Last = e.Last.AddMinutes(5), Count = 4 };            // it happened once more
        Assert.Single(Health.SplitResolved([again], boot, handled).active);
    }

    [Fact]
    public void First_lines_are_cut_short_and_blank_lines_dropped()
    {
        var text = "line one\r\n\r\n  line two  \r\nline three\r\nline four\r\nline five";
        Assert.Equal("line one\nline two\nline three\nline four", Health.FirstLines(text));
        Assert.EndsWith("…", Health.FirstLines(new string('x', 900)));
        Assert.Equal("", Health.FirstLines(""));
    }
}
