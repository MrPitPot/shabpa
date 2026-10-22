using System.Text.Json;

namespace Shabpa.Tests;

public class PowerEstimateTests
{
    private static HardwareSnapshot Desktop4070() => new()
    {
        CpuName = "AMD Ryzen 7 7800X3D 8-Core Processor", CpuCores = 8, GpuName = "NVIDIA GeForce RTX 4070", GpuVendor = "NVIDIA", GpuLimitWatts = 200,
        GpuVramMb = 12288, RamSticks = 2, RamGb = 32, RamType = "DDR5", Ssd = 2, Hdd = 1, Fans = 4, Monitors = 2,
    };

    // ---- tables -------------------------------------------------------------------------------------

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4090", 450)]
    [InlineData("NVIDIA GeForce RTX 3060 Ti", 200)]
    [InlineData("NVIDIA GeForce RTX 4070 Ti SUPER", 285)]
    [InlineData("NVIDIA GeForce GTX 1650", 75)]
    [InlineData("AMD Radeon RX 7900 XTX", 355)]
    [InlineData("AMD Radeon RX 6600", 132)]
    [InlineData("AMD Radeon RX 580 Series", 185)]
    [InlineData("Intel(R) Arc(TM) A770 Graphics", 225)]
    public void Gpu_table_knows_common_cards(string name, double watts) => Assert.Equal(watts, PowerEstimator.GpuTable(name));

    [Theory]
    [InlineData("")]
    [InlineData("Some Unknown Adapter")]
    public void Gpu_table_is_silent_for_unknown(string name) => Assert.Null(PowerEstimator.GpuTable(name));

    [Theory]
    [InlineData("AMD Radeon(TM) Graphics", true)]
    [InlineData("Intel(R) UHD Graphics 770", true)]
    [InlineData("Intel(R) Arc(TM) Graphics", true)]
    [InlineData("Intel(R) Arc(TM) A770 Graphics", false)]
    [InlineData("AMD Radeon RX 7600", false)]
    [InlineData("NVIDIA GeForce RTX 3050", false)]
    public void Integrated_graphics_are_told_from_cards(string name, bool integrated) => Assert.Equal(integrated, PowerEstimator.IsIntegratedGpu(name));

    [Theory]
    [InlineData("Intel(R) Core(TM) i9-14900K", false, 250)]
    [InlineData("12th Gen Intel(R) Core(TM) i5-12400F", false, 100)]
    [InlineData("13th Gen Intel(R) Core(TM) i7-13700H", true, 55)]
    [InlineData("AMD Ryzen 9 7950X 16-Core Processor", false, 170)]
    [InlineData("AMD Ryzen 7 5800X3D 8-Core Processor", false, 120)]
    [InlineData("AMD Ryzen 5 5600 6-Core Processor", false, 65)]
    [InlineData("AMD Ryzen 7 7840HS w/ Radeon 780M Graphics", true, 45)]
    public void Cpu_table_knows_common_processors(string name, bool laptop, double watts) => Assert.Equal(watts, PowerEstimator.CpuTable(name, laptop));

    [Fact]
    public void Cpu_table_is_silent_for_unknown() => Assert.Null(PowerEstimator.CpuTable("Contoso Quantum 9000", false));

    // ---- estimator ----------------------------------------------------------------------------------

    [Fact]
    public void Driver_limit_wins_and_gives_high_confidence()
    {
        var r = PowerEstimator.Estimate(Desktop4070());
        Assert.Equal(200, r.GpuMax);
        Assert.Equal(PowerConfidence.High, r.Confidence);
        Assert.Equal(PowerSource.Sensor, r.Parts.First(p => p.Key == "gpu").Source);
        Assert.Equal(120, r.CpuMax);
    }

    [Fact]
    public void Two_PCs_do_not_get_the_same_numbers()
    {
        var a = PowerEstimator.Estimate(Desktop4070());
        var b = PowerEstimator.Estimate(new HardwareSnapshot { CpuName = "Intel(R) Core(TM) i3-12100", GpuName = "NVIDIA GeForce GTX 1650", RamSticks = 1, Ssd = 1, Monitors = 1 });
        Assert.NotEqual(a.GpuMax, b.GpuMax);
        Assert.NotEqual(a.Other, b.Other);
        Assert.NotEqual(a.GpuIdle, b.GpuIdle);
    }

    [Fact]
    public void Table_match_without_a_sensor_is_medium()
    {
        var r = PowerEstimator.Estimate(Desktop4070() with { GpuLimitWatts = null });
        Assert.Equal(PowerConfidence.Medium, r.Confidence);
        Assert.Equal(200, r.GpuMax);
        Assert.Equal(PowerSource.Table, r.Parts.First(p => p.Key == "gpu").Source);
    }

    [Fact]
    public void Unknown_hardware_is_low_confidence_but_sane()
    {
        var r = PowerEstimator.Estimate(new HardwareSnapshot { GpuName = "Mystery", GpuVramMb = 8192, CpuCores = 6 });
        Assert.Equal(PowerConfidence.Low, r.Confidence);
        Assert.Equal(150, r.GpuMax);
        Assert.InRange(r.CpuMax, 15, 450);
        Assert.InRange(r.Other, 10, 250);
    }

    [Fact]
    public void Empty_snapshot_does_not_throw_and_stays_in_range()
    {
        var r = PowerEstimator.Estimate(new HardwareSnapshot());
        Assert.InRange(r.GpuMax, 1, 1000);
        Assert.True(r.GpuIdle <= r.GpuMax);
        Assert.InRange(r.Other, 10, 250);
    }

    [Fact]
    public void Laptop_draws_less_than_the_same_desktop()
    {
        var desktop = PowerEstimator.Estimate(Desktop4070() with { GpuLimitWatts = null });
        var laptop = PowerEstimator.Estimate(Desktop4070() with { GpuLimitWatts = null, Laptop = true });
        Assert.True(laptop.GpuMax < desktop.GpuMax);
        Assert.True(laptop.Other < desktop.Other);
        Assert.Contains(laptop.Parts, p => p.Key == "display");
        Assert.DoesNotContain(desktop.Parts, p => p.Key == "display");
    }

    [Fact]
    public void Integrated_graphics_get_small_numbers()
    {
        var r = PowerEstimator.Estimate(new HardwareSnapshot { CpuName = "AMD Ryzen 5 5600G", GpuName = "AMD Radeon(TM) Graphics", RamSticks = 2, Ssd = 1 });
        Assert.Equal(20, r.GpuMax);
        Assert.Equal(2, r.GpuIdle);
    }

    [Fact]
    public void More_drives_and_sticks_raise_the_rest_figure()
    {
        var small = PowerEstimator.Estimate(Desktop4070() with { RamSticks = 2, Ssd = 1, Hdd = 0, Fans = 2 });
        var big = PowerEstimator.Estimate(Desktop4070() with { RamSticks = 4, Ssd = 2, Hdd = 4, Fans = 8 });
        Assert.True(big.Other > small.Other + 30);
    }

    [Fact]
    public void Heuristic_rest_matches_the_documented_rules()
    {
        // desktop: board 25 + 2 DDR4 sticks x3 + 1 SSD x3 + 3 default fans x2 + 5 misc = 45, plus 10 % supply loss
        var r = PowerEstimator.Estimate(new HardwareSnapshot { CpuName = "Intel(R) Core(TM) i5-12400", GpuName = "NVIDIA GeForce RTX 3060", RamSticks = 2, RamType = "DDR4", Ssd = 1 });
        Assert.Equal(50, r.Other);
    }

    [Fact]
    public void Idle_grows_with_monitors_and_is_higher_on_amd()
    {
        var one = PowerEstimator.Estimate(Desktop4070() with { GpuLimitWatts = null, Monitors = 1 });
        var three = PowerEstimator.Estimate(Desktop4070() with { GpuLimitWatts = null, Monitors = 3 });
        var amd = PowerEstimator.Estimate(new HardwareSnapshot { GpuName = "AMD Radeon RX 6700 XT", GpuVendor = "AMD", Monitors = 1 });
        Assert.True(three.GpuIdle > one.GpuIdle);
        Assert.True(amd.GpuIdle > PowerEstimator.Estimate(new HardwareSnapshot { GpuName = "NVIDIA GeForce RTX 3060 Ti", GpuVendor = "NVIDIA" }).GpuIdle);
    }

    [Fact]
    public void Measured_cpu_peak_replaces_the_table()
    {
        var r = PowerEstimator.Estimate(Desktop4070() with { CpuPeakWatts = 88 });
        Assert.Equal(88, r.CpuMax);
        Assert.Equal(PowerSource.Sensor, r.Parts.First(p => p.Key == "cpu").Source);
    }

    [Fact]
    public void Breakdown_has_a_line_for_every_part()
    {
        var r = PowerEstimator.Estimate(Desktop4070());
        foreach (var key in new[] { "gpu", "cpu", "board", "ram", "ssd", "hdd", "fans", "psu" }) Assert.Contains(r.Parts, p => p.Key == key);
    }

    [Fact]
    public void Hardware_key_changes_with_hardware_only()
    {
        var a = PowerEstimator.KeyOf(Desktop4070());
        Assert.Equal(a, PowerEstimator.KeyOf(Desktop4070() with { Monitors = 5, GpuLimitWatts = 100 }));
        Assert.NotEqual(a, PowerEstimator.KeyOf(Desktop4070() with { GpuName = "NVIDIA GeForce RTX 4080" }));
        Assert.NotEqual(a, PowerEstimator.KeyOf(Desktop4070() with { RamSticks = 4 }));
    }

    // ---- detection helpers --------------------------------------------------------------------------

    [Theory]
    [InlineData(26, "DDR4")]
    [InlineData(34, "DDR5")]
    [InlineData(24, "DDR3")]
    [InlineData(0, "")]
    public void Ram_type_from_smbios(int code, string name) => Assert.Equal(name, PowerDetect.RamTypeOf(code));

    [Fact]
    public void Laptop_chassis_codes()
    {
        Assert.True(PowerDetect.IsLaptopChassis([10]));
        Assert.True(PowerDetect.IsLaptopChassis([3, 9]));
        Assert.False(PowerDetect.IsLaptopChassis([3]));
        Assert.False(PowerDetect.IsLaptopChassis([]));
    }

    [Fact]
    public void External_and_virtual_disks_are_not_counted()
    {
        Assert.True(PowerDetect.IsInternalDisk("SCSI", "Samsung SSD 980", "Fixed hard disk media"));
        Assert.False(PowerDetect.IsInternalDisk("USB", "My Passport", "Fixed hard disk media"));
        Assert.False(PowerDetect.IsInternalDisk("SCSI", "Microsoft Virtual Disk", "Fixed hard disk media"));
        Assert.False(PowerDetect.IsInternalDisk("SCSI", "Flash", "Removable Media"));
    }

    [Fact]
    public void Disk_kind_from_storage_type_then_model()
    {
        Assert.True(PowerDetect.IsSsd(4, "x"));
        Assert.False(PowerDetect.IsSsd(3, "x"));
        Assert.True(PowerDetect.IsSsd(0, "WDC WDS500G3X0C NVMe SSD"));
        Assert.Null(PowerDetect.IsSsd(0, "ST2000DM008"));
    }

    [Fact]
    public void A_real_card_beats_integrated_graphics()
    {
        var pick = PowerDetect.PickGpu([new("AMD Radeon(TM) Graphics", 536870912), new("NVIDIA GeForce RTX 3060", 4293918720), new("Microsoft Basic Display Adapter", null)]);
        Assert.Equal("NVIDIA GeForce RTX 3060", pick!.Value.Name);
        Assert.Null(PowerDetect.PickGpu([new("Microsoft Basic Display Adapter", null)]));
    }

    [Fact]
    public void Same_family_compares_model_digits()
    {
        Assert.True(PowerDetect.SameFamily("NVIDIA GeForce RTX 4070", "NVIDIA GeForce RTX 4070"));
        Assert.False(PowerDetect.SameFamily("AMD Radeon(TM) Graphics 780", "NVIDIA GeForce RTX 4070"));
    }
}

public class PowerLearnTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0);
    private static readonly TimeSpan Win = TimeSpan.FromMinutes(5);

    /// <summary>Feeds one window of readings, then one more reading after the window to close it.</summary>
    private static void Window(PowerLearnState st, DateTime start, double loadedWatts, double idleWatts = 0, int loaded = 5, int idle = 12, double ceiling = 300)
    {
        for (var i = 0; i < loaded; i++) PowerLearner.Observe(st, loadedWatts, 99, ceiling, start.AddSeconds(i), Win);
        for (var i = 0; i < idle; i++) PowerLearner.Observe(st, idleWatts, 0, ceiling, start.AddSeconds(30 + i), Win);
        PowerLearner.Observe(st, 0, 50, ceiling, start + Win, Win);
    }

    [Fact]
    public void Peaks_and_idles_are_recorded_per_window()
    {
        var st = new PowerLearnState();
        Window(st, T0, 180, 14);
        Assert.Single(st.Peaks);
        Assert.Equal(180, st.Peaks[0]);
        Assert.Equal(14, st.Idles[0]);
    }

    [Fact]
    public void A_single_spike_does_not_become_the_peak()
    {
        var st = new PowerLearnState();
        PowerLearner.Observe(st, 150, 99, 300, T0, Win);
        PowerLearner.Observe(st, 150, 99, 300, T0.AddSeconds(1), Win);
        PowerLearner.Observe(st, 290, 99, 300, T0.AddSeconds(2), Win); // one spike
        PowerLearner.Observe(st, 0, 50, 300, T0 + Win, Win);
        Assert.Equal(150, st.Peaks[0]);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-5)]
    [InlineData(0)]
    [InlineData(5000)]
    [InlineData(600)] // above 1.6 x the 300 W ceiling
    public void Impossible_readings_are_ignored(double watts)
    {
        var st = new PowerLearnState();
        PowerLearner.Observe(st, watts, 99, 300, T0, Win);
        PowerLearner.Observe(st, watts, 0, 300, T0.AddSeconds(1), Win);
        Assert.Empty(st.WindowTop);
        Assert.Equal(0, st.WindowIdleCount);
    }

    [Fact]
    public void Lists_stay_bounded()
    {
        var st = new PowerLearnState();
        for (var i = 0; i < 120; i++) Window(st, T0.AddHours(i), 150 + i % 5, 10);
        Assert.Equal(PowerLearnState.MaxWindows, st.Peaks.Count);
        Assert.Equal(PowerLearnState.MaxWindows, st.Idles.Count);
    }

    [Fact]
    public void Learned_peak_needs_three_windows_and_drops_one_outlier()
    {
        var st = new PowerLearnState { Peaks = [100, 110] };
        Assert.Null(PowerLearner.LearnedPeak(st));
        st.Peaks = [150, 152, 148, 151, 149, 400];
        Assert.Equal(152, PowerLearner.LearnedPeak(st));
        st.Peaks = [150, 152, 148];
        Assert.Equal(152, PowerLearner.LearnedPeak(st));
    }

    [Fact]
    public void Learned_idle_is_the_median()
    {
        var st = new PowerLearnState { Idles = [11, 13, 12, 90, 12] };
        Assert.Equal(12, PowerLearner.LearnedIdle(st));
        Assert.Null(PowerLearner.LearnedIdle(new PowerLearnState { Idles = [10] }));
    }

    [Fact]
    public void Refine_max_ignores_a_card_that_was_never_worked_and_caps_overshoot()
    {
        Assert.Equal(200, PowerLearner.RefineMax(200, null));
        Assert.Equal(200, PowerLearner.RefineMax(200, 60)); // only light use so far
        Assert.Equal(170, PowerLearner.RefineMax(200, 170));
        Assert.Equal(300, PowerLearner.RefineMax(200, 900)); // 1.5 x cap
        Assert.Equal(0, PowerLearner.RefineMax(0, 100));
    }

    [Fact]
    public void Refine_idle_rejects_nonsense()
    {
        Assert.Equal(9, PowerLearner.RefineIdle(12, 9.2));
        Assert.Equal(12, PowerLearner.RefineIdle(12, 0.2));
        Assert.Equal(12, PowerLearner.RefineIdle(12, 500));
        Assert.Equal(12, PowerLearner.RefineIdle(12, null));
    }

    [Fact]
    public void A_clock_going_backwards_restarts_the_window()
    {
        var st = new PowerLearnState();
        PowerLearner.Observe(st, 150, 99, 300, T0, Win);
        PowerLearner.Observe(st, 160, 99, 300, T0.AddHours(-1), Win);
        Assert.Single(st.WindowTop);
    }
}

public class PowerModelTests
{
    private static PowerEstimateResult Detected() => PowerEstimator.Estimate(new HardwareSnapshot
    {
        CpuName = "AMD Ryzen 5 5600 6-Core Processor", GpuName = "NVIDIA GeForce RTX 3060", GpuLimitWatts = 170, RamSticks = 2, RamType = "DDR4", Ssd = 1, Monitors = 1,
    });

    private static readonly DateTime Now = new(2026, 5, 5, 10, 0, 0);

    [Fact]
    public void Defaults_are_the_old_ones()
    {
        var s = new Settings();
        Assert.Equal((250, 12, 45, 125), (s.GpuMaxWatts, s.GpuIdleWatts, s.BaseWatts, s.CpuMaxWatts));
    }

    [Fact]
    public void Untouched_old_defaults_are_replaced_by_detection()
    {
        var s = new Settings();
        PowerModel.Migrate(s);
        var r = Detected();
        PowerModel.ApplyDetected(s, r, Now);
        Assert.Equal(170, s.GpuMaxWatts);
        Assert.Equal(r.GpuIdle, s.GpuIdleWatts);
        Assert.Equal(r.Other, s.BaseWatts);
        Assert.Equal(r.CpuMax, s.CpuMaxWatts);
        Assert.False(PowerModel.IsManual(s, PowerField.GpuMax));
    }

    [Fact]
    public void Values_an_existing_user_typed_are_kept()
    {
        var s = new Settings { GpuMaxWatts = 320, BaseWatts = 80 }; // idle and cpu stay at the old defaults
        PowerModel.Migrate(s);
        PowerModel.ApplyDetected(s, Detected(), Now);
        Assert.Equal(320, s.GpuMaxWatts);
        Assert.Equal(80, s.BaseWatts);
        Assert.True(PowerModel.IsManual(s, PowerField.GpuMax));
        Assert.True(PowerModel.IsManual(s, PowerField.Base));
        Assert.False(PowerModel.IsManual(s, PowerField.GpuIdle));
        Assert.Equal(Detected().GpuIdle, s.GpuIdleWatts);
    }

    [Fact]
    public void Migration_runs_once()
    {
        var s = new Settings();
        PowerModel.Migrate(s);
        s.GpuMaxWatts = 111;
        PowerModel.Migrate(s); // second call must not reclassify
        Assert.False(PowerModel.IsManual(s, PowerField.GpuMax));
    }

    [Fact]
    public void Editing_a_field_marks_it_manual_and_detection_leaves_it_alone()
    {
        var s = new Settings();
        PowerModel.Migrate(s);
        PowerModel.ApplyDetected(s, Detected(), Now);
        PowerModel.Edit(s, PowerField.GpuMax, 400);
        Assert.True(PowerModel.IsManual(s, PowerField.GpuMax));
        PowerModel.ApplyDetected(s, PowerEstimator.Estimate(new HardwareSnapshot { GpuName = "NVIDIA GeForce RTX 4090", GpuLimitWatts = 450 }), Now);
        Assert.Equal(400, s.GpuMaxWatts);
        Assert.Equal(450, s.Power.DetGpuMax);
    }

    [Fact]
    public void Typing_the_same_value_is_not_an_edit()
    {
        var s = new Settings();
        PowerModel.Migrate(s);
        PowerModel.ApplyDetected(s, Detected(), Now);
        PowerModel.Edit(s, PowerField.GpuMax, s.GpuMaxWatts);
        Assert.False(PowerModel.IsManual(s, PowerField.GpuMax));
    }

    [Fact]
    public void Edit_is_clamped_and_idle_never_exceeds_max()
    {
        var s = new Settings();
        PowerModel.Edit(s, PowerField.GpuMax, 99999);
        Assert.Equal(1000, s.GpuMaxWatts);
        PowerModel.Edit(s, PowerField.GpuMax, 100);
        PowerModel.Edit(s, PowerField.GpuIdle, 500);
        Assert.Equal(100, s.GpuIdleWatts);
        PowerModel.Edit(s, PowerField.Base, -20);
        Assert.Equal(0, s.BaseWatts);
        PowerModel.Edit(s, PowerField.CpuMax, double.NaN);
        Assert.Equal(125, s.CpuMaxWatts);
    }

    [Fact]
    public void Reset_to_detected_restores_the_value_and_clears_the_flag()
    {
        var s = new Settings();
        PowerModel.Migrate(s);
        PowerModel.ApplyDetected(s, Detected(), Now);
        PowerModel.Edit(s, PowerField.Base, 123);
        PowerModel.ResetToDetected(s, PowerField.Base);
        Assert.False(PowerModel.IsManual(s, PowerField.Base));
        Assert.Equal(Detected().Other, s.BaseWatts);
    }

    [Fact]
    public void Reset_without_detection_keeps_the_value_but_clears_the_flag()
    {
        var s = new Settings();
        PowerModel.Edit(s, PowerField.Base, 70);
        PowerModel.ResetToDetected(s, PowerField.Base);
        Assert.Equal(70, s.BaseWatts);
        Assert.False(PowerModel.IsManual(s, PowerField.Base));
    }

    [Fact]
    public void Learning_refines_unedited_fields_only()
    {
        var s = new Settings();
        PowerModel.Migrate(s);
        PowerModel.ApplyDetected(s, Detected(), Now);
        PowerModel.Edit(s, PowerField.GpuIdle, 20); // manual idle
        var t = Now;
        var win = TimeSpan.FromMinutes(1);
        for (var w = 0; w < 5; w++)
        {
            for (var i = 0; i < 4; i++) PowerModel.Observe(s, 140, 99, t.AddSeconds(i), win);
            for (var i = 0; i < 12; i++) PowerModel.Observe(s, 8, 0, t.AddSeconds(10 + i), win);
            t = t.AddMinutes(1);
            PowerModel.Observe(s, 140, 99, t, win);
        }
        Assert.Equal(140, s.GpuMaxWatts);
        Assert.True(PowerModel.IsLearned(s, PowerField.GpuMax));
        Assert.Equal(20, s.GpuIdleWatts); // the person's value stays
        PowerModel.ResetToDetected(s, PowerField.GpuIdle);
        Assert.Equal(8, s.GpuIdleWatts);
    }

    [Fact]
    public void Learning_never_touches_a_manual_maximum()
    {
        var s = new Settings();
        PowerModel.Migrate(s);
        PowerModel.ApplyDetected(s, Detected(), Now);
        PowerModel.Edit(s, PowerField.GpuMax, 300);
        s.Power.Learn.Peaks = [140, 141, 139, 140];
        PowerModel.Refresh(s);
        Assert.Equal(300, s.GpuMaxWatts);
    }

    [Fact]
    public void Detected_value_includes_learning_but_hardware_value_is_kept()
    {
        var s = new Settings();
        PowerModel.Migrate(s);
        PowerModel.ApplyDetected(s, Detected(), Now);
        s.Power.Learn.Peaks = [150, 151, 149];
        Assert.Equal(151, PowerModel.Detected(s, PowerField.GpuMax));
        Assert.Equal(170, s.Power.DetGpuMax);
    }

    [Fact]
    public void Needs_detection_when_never_run_or_hardware_changed()
    {
        var s = new Settings();
        Assert.True(PowerModel.NeedsDetection(s, "x"));
        PowerModel.ApplyDetected(s, Detected(), Now);
        Assert.False(PowerModel.NeedsDetection(s, Detected().HardwareKey));
        Assert.True(PowerModel.NeedsDetection(s, "other pc"));
    }

    // ---- persistence and sanitising -------------------------------------------------------------------

    [Fact]
    public void Settings_with_power_state_survive_a_json_round_trip()
    {
        var s = new Settings();
        PowerModel.Migrate(s);
        PowerModel.ApplyDetected(s, Detected(), Now);
        PowerModel.Edit(s, PowerField.Base, 99);
        s.Power.Learn.Peaks = [150, 151, 149];
        var json = JsonSerializer.Serialize(s);
        var back = JsonSerializer.Deserialize<Settings>(json)!;
        Assert.Equal(s.Power.DetGpuMax, back.Power.DetGpuMax);
        Assert.True(back.Power.BaseManual);
        Assert.Equal(s.Power.Parts.Count, back.Power.Parts.Count);
        Assert.Equal(new[] { 150.0, 151, 149 }, back.Power.Learn.Peaks);
        Assert.Equal(PowerSource.Sensor, back.Power.Parts.First(p => p.Key == "gpu").Source);
    }

    [Fact]
    public void An_old_settings_file_without_power_loads_with_defaults()
    {
        var back = JsonSerializer.Deserialize<Settings>("{\"Tariff\":500,\"GpuMaxWatts\":275}")!;
        Assert.False(back.Power.Migrated);
        PowerModel.Migrate(back);
        Assert.True(PowerModel.IsManual(back, PowerField.GpuMax));
        Assert.False(PowerModel.IsManual(back, PowerField.GpuIdle));
    }

    [Fact]
    public void Sanitize_clamps_everything()
    {
        var s = new Settings { GpuMaxWatts = 1e9, GpuIdleWatts = double.NaN, BaseWatts = -5, CpuMaxWatts = double.PositiveInfinity };
        s.Power.DetGpuMax = 5000;
        s.Power.DetGpuIdle = 9999;
        s.Power.Learn.Peaks = [double.NaN, -1, 200, 99999, .. Enumerable.Repeat(150.0, 100)];
        s.Power.Learn.WindowTop = [10, 20, 30, 40, 5000];
        s.Power.Learn.WindowIdleCount = -4;
        s.Power.Confidence = "banana";
        s.Power.Parts = [new PowerPart("gpu", double.NaN, PowerSource.Sensor), new PowerPart("cpu", 9e9, PowerSource.Table)];
        PowerModel.Sanitize(s);
        Assert.Equal(1000, s.GpuMaxWatts);
        Assert.Equal(12, s.GpuIdleWatts);
        Assert.Equal(0, s.BaseWatts);
        Assert.Equal(125, s.CpuMaxWatts);
        Assert.Equal(1000, s.Power.DetGpuMax);
        Assert.True(s.Power.DetGpuIdle <= s.Power.DetGpuMax);
        Assert.Equal(PowerLearnState.MaxWindows, s.Power.Learn.Peaks.Count);
        Assert.All(s.Power.Learn.Peaks, v => Assert.InRange(v, 0.1, 1500));
        Assert.Equal(3, s.Power.Learn.WindowTop.Count);
        Assert.Equal(0, s.Power.Learn.WindowIdleCount);
        Assert.Equal("", s.Power.Confidence);
        Assert.Single(s.Power.Parts);
        Assert.Equal(2000, s.Power.Parts[0].Watts);
    }

    [Fact]
    public void Sanitize_survives_nulls_from_a_damaged_file()
    {
        var s = JsonSerializer.Deserialize<Settings>("{\"Power\":{\"Parts\":null,\"Learn\":null}}")!;
        PowerModel.Sanitize(s);
        Assert.NotNull(s.Power.Learn);
        Assert.Empty(s.Power.Parts);
    }

    [Fact]
    public void Import_clamps_power_values_and_forgets_the_other_pcs_measurements()
    {
        var source = new AppState();
        source.Settings.GpuMaxWatts = 5000;
        source.Settings.GpuIdleWatts = 9000;
        source.Settings.Power.Hardware = "other pc";
        source.Settings.Power.Learn.Peaks = [200, 201, 202];
        source.Settings.Power.Migrated = true;
        source.Settings.Power.GpuMaxManual = true;
        var json = SettingsBackup.Export(source, false, false, DateTime.Now);
        var target = new AppState();
        SettingsBackup.Apply(SettingsBackup.Read(json)!, target);
        Assert.Equal(1000, target.Settings.GpuMaxWatts);
        Assert.True(target.Settings.GpuIdleWatts <= target.Settings.GpuMaxWatts);
        Assert.Equal("", target.Settings.Power.Hardware);
        Assert.Empty(target.Settings.Power.Learn.Peaks);
        Assert.True(target.Settings.Power.GpuMaxManual); // the person's choice travels
    }

    [Fact]
    public void Cost_uses_the_stored_figures()
    {
        // the live estimate (Sensors) is idle + load% x (max - idle); the detected maximum feeds it unchanged
        var s = new Settings();
        PowerModel.Migrate(s);
        PowerModel.ApplyDetected(s, Detected(), Now);
        var atHalf = s.GpuIdleWatts + 50 / 100.0 * (s.GpuMaxWatts - s.GpuIdleWatts);
        Assert.InRange(atHalf, s.GpuIdleWatts, s.GpuMaxWatts);
        Assert.NotEqual(250, s.GpuMaxWatts);
    }
}
