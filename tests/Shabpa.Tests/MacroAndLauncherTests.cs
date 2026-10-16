using System.IO;
using Shabpa.Core;
using Xunit;

namespace Shabpa.Tests;

public sealed class MacroAndLauncherTests
{
    private static LauncherProbe Pc(IEnumerable<string> files, Func<IEnumerable<(string, string?, string?)>>? installed = null,
        Func<IEnumerable<(string, string)>>? shortcuts = null, Func<string[], IEnumerable<string>>? running = null,
        Func<string, string, string?>? registry = null)
    {
        var present = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
        return new LauncherProbe
        {
            Exists = present.Contains, Roots = [@"C:\Program Files", @"C:\Program Files (x86)"], Drives = [@"C:\", @"D:\"], ProgramData = @"C:\ProgramData",
            Registry = registry ?? ((_, _) => null), Installed = installed ?? (() => []), Shortcuts = shortcuts ?? (() => []), Running = running ?? (_ => []),
        };
    }

    [Fact]
    public void Finds_a_launcher_installed_on_another_drive_without_any_registry_help()
    {
        var found = LauncherDetector.Detect(Pc([@"D:\Games\Steam\steam.exe"]));
        Assert.Equal(@"D:\Games\Steam\steam.exe", found.Single().ExePath);
    }

    [Fact]
    public void Finds_a_launcher_from_its_start_menu_shortcut()
    {
        var found = LauncherDetector.Detect(Pc([@"E:\Stuff\Battle.net\Battle.net Launcher.exe"],
            shortcuts: () => [("Battle.net", @"E:\Stuff\Battle.net\Battle.net Launcher.exe")]));
        Assert.Equal("battlenet", found.Single().Key);
    }

    [Fact]
    public void Finds_a_launcher_from_the_installed_programs_list()
    {
        var found = LauncherDetector.Detect(Pc([@"F:\Epic\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe"],
            installed: () => [("Epic Games Launcher", @"F:\Epic\", null)]));
        Assert.Equal("epic", found.Single().Key);
    }

    [Fact]
    public void Finds_a_launcher_from_its_icon_file_or_while_running()
    {
        var byIcon = LauncherDetector.Detect(Pc([@"G:\x\EADesktop.exe"], installed: () => [("EA app", null, "\"G:\\x\\EADesktop.exe\",0")]));
        Assert.Equal("ea", byIcon.Single().Key);
        var running = LauncherDetector.Detect(Pc([@"H:\GOG\GalaxyClient.exe"], running: names => names.Contains("GalaxyClient.exe") ? [@"H:\GOG\GalaxyClient.exe"] : []));
        Assert.Equal("gog", running.Single().Key);
    }

    [Fact]
    public void A_generic_Launcher_exe_is_only_taken_as_Rockstar_when_it_really_is()
    {
        var other = LauncherDetector.Detect(Pc([@"D:\Tools\Launcher.exe"], running: _ => [@"D:\Tools\Launcher.exe"]));
        Assert.Empty(other);
        var real = LauncherDetector.Detect(Pc([@"D:\Rockstar Games\Launcher\Launcher.exe"]));
        Assert.Equal("rockstar", real.Single().Key);
    }

    [Fact]
    public void A_probe_that_throws_does_not_stop_the_search()
    {
        var probe = Pc([@"C:\Program Files (x86)\Steam\steam.exe"], installed: () => throw new InvalidOperationException("registry"),
            shortcuts: () => throw new InvalidOperationException("com"));
        Assert.Equal("steam", LauncherDetector.Detect(probe).Single().Key);
    }

    [Fact]
    public void Hand_added_launchers_are_checked_and_survive_a_new_search()
    {
        Assert.NotNull(LauncherDetector.Reject(@"C:\no\such.exe"));
        Assert.NotNull(LauncherDetector.Reject(@"C:\Windows\System32\notepad.exe"));
        Assert.NotNull(LauncherDetector.Reject(@"\\server\share\x.exe"));
        Assert.NotNull(LauncherDetector.Reject(@"C:\file.txt"));
        var manual = new LauncherInfo { Key = "manual:foo", Name = "Foo", ExePath = @"D:\foo.exe", Manual = true };
        var merged = LauncherDetector.Merge([new LauncherInfo { Key = "steam", ExePath = @"C:\s.exe" }], [manual], _ => true);
        Assert.Equal(["steam", "manual:foo"], merged.Select(l => l.Key));
        Assert.Empty(LauncherDetector.Merge([], [manual], p => false));
    }

    [Fact]
    public void A_known_launcher_file_added_by_hand_keeps_its_key()
    {
        var info = LauncherDetector.FromFile(@"D:\Weird\Folder\steam.exe");
        Assert.Equal("steam", info.Key);
        Assert.True(info.Manual);
    }

    // ---- taught clicks ------------------------------------------------------------------------

    private static LauncherMacro Taught() => new() { Width = 1000, Height = 600, Dpi = 96, Steps = [new MacroStep { Dx = 900, Dy = 100, Fx = 0.9, Fy = 100 / 600.0 }] };

    [Fact]
    public void A_click_lands_on_the_same_pixel_in_a_window_of_the_recorded_size()
    {
        var m = Taught();
        Assert.Equal((1400, 300), MacroMath.Resolve(m.Steps[0], m, 500, 200, 1000, 600, 96));
    }

    [Fact]
    public void In_another_size_the_click_keeps_its_place_by_fraction_and_scaling_scales_the_offset()
    {
        var m = Taught();
        Assert.Equal((500 + 1080, 200 + 120), MacroMath.Resolve(m.Steps[0], m, 500, 200, 1200, 720, 96));
        Assert.Equal((1350 + 500, 150 + 200), MacroMath.Resolve(m.Steps[0], m, 500, 200, 1000, 600, 144));
    }

    [Fact]
    public void Waits_and_fractions_stay_inside_sane_bounds()
    {
        Assert.Equal(MacroMath.MinWaitMs, MacroMath.ClampWait(5));
        Assert.Equal(MacroMath.MaxWaitMs, MacroMath.ClampWait(9_999_999));
        Assert.Equal((1.0, 0.0), MacroMath.Fractions(5000, -20, 1000, 600));
    }

    [Fact]
    public void Clicks_in_Shabpa_or_in_another_program_are_not_recorded()
    {
        Assert.False(MacroMath.Accept(10, 10, "Shabpa", null));
        Assert.True(MacroMath.Accept(20, 10, "steam", null));
        Assert.True(MacroMath.Accept(21, 10, "Steam", "steam"));
        Assert.False(MacroMath.Accept(22, 10, "chrome", "steam"));
        Assert.False(MacroMath.Accept(22, 10, "", null));
    }

    [Fact]
    public void Wheel_turns_at_one_spot_merge_into_one_step()
    {
        var steps = new List<MacroStep>();
        MacroMath.AddWheel(steps, new MacroStep { Button = "wheel", Dx = 100, Dy = 100, Wheel = -1, WaitMs = 800 });
        MacroMath.AddWheel(steps, new MacroStep { Button = "wheel", Dx = 101, Dy = 100, Wheel = -1, WaitMs = 120 });
        MacroMath.AddWheel(steps, new MacroStep { Button = "wheel", Dx = 400, Dy = 100, Wheel = -1, WaitMs = 100 });
        Assert.Equal(2, steps.Count);
        Assert.Equal(-2, steps[0].Wheel);
    }

    [Fact]
    public void A_taught_lesson_survives_saving_and_holds_no_text_or_keys()
    {
        var state = new AppState { Macros = [Taught()] };
        var json = System.Text.Json.JsonSerializer.Serialize(state);
        var back = System.Text.Json.JsonSerializer.Deserialize<AppState>(json)!;
        Assert.Equal(900, back.Macros[0].Steps[0].Dx);
        var names = typeof(MacroStep).GetProperties().Select(p => p.Name).Concat(typeof(LauncherMacro).GetProperties().Select(p => p.Name)).ToList();
        Assert.DoesNotContain(names, n => n.Contains("Key", StringComparison.OrdinalIgnoreCase) && n != "LauncherKey");
        Assert.DoesNotContain(names, n => n.Contains("Text", StringComparison.OrdinalIgnoreCase) || n.Contains("Image", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void An_empty_lesson_does_nothing()
    {
        Assert.Equal(MacroOutcome.Empty, MacroPlayer.Run(new LauncherMacro()));
    }
}

public sealed class InputSafetyTests
{
    private static string Source(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Shabpa.slnx"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "src", "Shabpa", relative));
    }

    [Fact]
    public void Taught_clicks_never_see_keys_screens_files_or_the_network()
    {
        var text = Source(Path.Combine("Core", "Macro.cs"));
        foreach (var banned in new[] { "WH_KEYBOARD", "SetWindowsHookEx(2", "CopyFromScreen", "Bitmap", "HttpClient", "Socket", "File.", "keybd_event", "SendKeys", "GetKeyboardState" })
            Assert.DoesNotContain(banned, text);
        // the only key it ever looks at is Esc, to stop
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, @"GetAsyncKeyState\(0x1B\)"));
        Assert.Contains("WhMouseLl = 14", text);
    }

    [Fact]
    public void Launchers_added_by_hand_are_opened_with_normal_rights()
    {
        var text = Source(Path.Combine("Core", "Launchers.cs"));
        Assert.Contains("explorer.exe", text);
        var hub = Source("Hub.cs");
        Assert.DoesNotContain("Process.Start(new System.Diagnostics.ProcessStartInfo(plan.ExePath)", hub);
    }
}
