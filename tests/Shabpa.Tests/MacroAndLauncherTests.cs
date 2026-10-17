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
    public void Paths_with_forward_slashes_are_written_the_Windows_way()
    {
        // Steam's registry value and Riot's json use forward slashes; Explorer opens Documents for those
        Assert.Equal(@"C:\Program Files (x86)\Steam\steam.exe", LauncherDetector.Normalize("C:/Program Files (x86)/Steam/steam.exe"));
        var found = LauncherDetector.Detect(Pc([@"D:\Games\Steam\steam.exe"], registry: (key, name) => name == "SteamExe" ? "d:/games/steam/steam.exe" : null));
        Assert.DoesNotContain('/', found.Single().ExePath);
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

public sealed class UiSoundTests
{
    [Fact]
    public void Generated_sounds_are_valid_short_quiet_wave_files()
    {
        var wav = UiSound.Tone(2300, 26, 0.045, 2100);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal(wav.Length - 44, BitConverter.ToInt32(wav, 40));
        Assert.True(wav.Length < 4000);
        var peak = 0;
        for (var i = 44; i < wav.Length; i += 2) peak = Math.Max(peak, Math.Abs(BitConverter.ToInt16(wav, i)));
        Assert.InRange(peak, 100, short.MaxValue * 0.06);
    }
}

public sealed class TaughtClicksSurviveTests
{
    private static LauncherMacro Macro() => new()
    {
        LauncherKey = "steam", WindowProcess = "steam", WindowClass = "SDL_app", Width = 1000, Height = 700,
        Steps = [new MacroStep { Dx = 900, Dy = 60, Fx = 0.9, Fy = 0.08, WaitMs = 1200 }],
    };

    [Fact]
    public void Taught_clicks_are_still_there_after_the_app_restarts_and_after_an_old_file_is_read()
    {
        var folder = Path.Combine(Path.GetTempPath(), "shabpa-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new Store(folder);
            store.State.Macros.Add(Macro());
            store.Save();
            var again = new Store(folder);
            again.Load();
            Assert.Single(again.State.Macros);
            Assert.Equal(900, again.State.Macros[0].Steps[0].Dx);

            // a state file written by an older version (no "Macros" at all) must load without touching other data
            File.WriteAllText(Path.Combine(folder, "state.json"), "{\"Settings\":{\"Theme\":\"forest\"}}");
            var old = new Store(folder);
            old.Load();
            Assert.Equal("forest", old.State.Settings.Theme);
            Assert.Empty(old.State.Macros);
        }
        finally
        {
            try { Directory.Delete(folder, true); } catch { }
        }
    }

    [Fact]
    public void Taught_clicks_travel_in_a_settings_backup_and_are_range_checked()
    {
        var state = new AppState { Macros = [Macro()] };
        var json = SettingsBackup.Export(state, includeProfiles: true, includeTasks: false, DateTime.Now);
        var pack = SettingsBackup.Read(json)!;
        pack.Macros![0].Steps[0].Dx = 99999999;
        pack.Macros[0].Steps[0].Button = "evil";
        var target = new AppState();
        SettingsBackup.Apply(pack, target);
        Assert.Single(target.Macros);
        Assert.Equal(20000, target.Macros[0].Steps[0].Dx);
        Assert.Equal("left", target.Macros[0].Steps[0].Button);
    }

    [Fact]
    public void An_update_never_removes_the_data_folder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Shabpa.slnx"))) dir = dir.Parent;
        var installer = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Setup", "Installer.cs"));
        // the only place the data folder is deleted is the uninstall with the person's explicit "remove my data"
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(installer, @"Directory\.Delete\(data"));
        Assert.Contains("if (removeData)", installer);
    }
}

public sealed class RatingTests
{
    [Fact]
    public void Every_star_count_has_a_friendly_line_and_a_reply()
    {
        for (var stars = 1; stars <= 5; stars++)
        {
            Assert.True(Pages.RatingCard.Joke(stars).Length > 20);
            Assert.True(Pages.RatingCard.Reply(stars).Length > 10);
        }
        // nothing insulting, in either language
        foreach (var banned in new[] { "احمق", "بی‌خود", "stupid", "idiot", "garbage", "trash", "terrible" })
            for (var stars = 1; stars <= 5; stars++) Assert.DoesNotContain(banned, Pages.RatingCard.Joke(stars) + Pages.RatingCard.Reply(stars), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_rating_never_leaves_the_pc()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Shabpa.slnx"))) dir = dir.Parent;
        var text = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Shabpa", "Pages", "RatingCard.cs"));
        foreach (var banned in new[] { "HttpClient", "WebClient", "Socket" }) Assert.DoesNotContain(banned, text);
    }
}

public sealed class GlassTests
{
    [Fact]
    public void The_glass_slider_changes_the_veil_live_and_theme_colour_never_paints_the_window()
    {
        var dictionary = Ui.Resources(Palette.Find("midnight"), "mica", 0);
        var veil = (System.Windows.Media.SolidColorBrush)dictionary["MicaVeil"];
        var background = (System.Windows.Media.SolidColorBrush)dictionary["BgGradient"];
        Assert.False(veil.IsFrozen);
        Assert.Equal(0, background.Color.A); // the window plate itself is clear: the veil is the only thing over the backdrop
        var before = veil.Color.A;
        Ui.SetGlassLevel(1);
        Assert.True(veil.Color.A > before);
        Assert.True(veil.Color.A > 100);
        // neutral: only black or white, never a theme colour
        Assert.True(veil.Color.R == veil.Color.G && veil.Color.G == veil.Color.B);
        Ui.Resources(Palette.Find(null), "off", 0); // leave the shared state as other tests expect
    }

    [Fact]
    public void Glass_depth_tokens_exist_for_every_theme_and_keep_the_design_numbers_for_night_and_white()
    {
        foreach (var palette in Palette.All)
        {
            var t = GlassTokens.Of(palette);
            Assert.True((t.Card >> 24) is > 0 and < 255, palette.Key);   // translucent
            Assert.True((t.Line >> 24) is > 0 and < 64, palette.Key);    // a hairline
            var d = Ui.Resources(palette, "off", 0);
            foreach (var key in new[] { "CardFill", "Card2Fill", "CardRim", "MicaVeil", "Wallpaper", "AccentGradient", "HueA", "HueE", "HueASoft", "ShadowC", "ShadowSoftC", "Hi", "FlyoutFill" })
                Assert.True(d.Contains(key), $"{palette.Key} lacks {key}");
        }
        Assert.Equal(0xB82E386Au, GlassTokens.Of(Palette.Find("night")).Card2);
        Assert.Equal(0xB8FFFFFFu, GlassTokens.Of(Palette.Find("white")).Card);
        Ui.Resources(Palette.Find(null), "off", 0);
    }
}

public sealed class GameRuleTests
{
    private static bool Decide(int state = 0, double gpu = 0, bool covers = false, bool graphics = false, string? path = null, string process = "somegame", params string[] user) =>
        GameRules.Decide(state, gpu, covers, graphics, path, process, user);

    [Fact]
    public void The_old_signals_still_work()
    {
        Assert.True(Decide(state: 3));
        Assert.True(Decide(state: 2, gpu: 60));
        Assert.False(Decide(state: 2, gpu: 5)); // a full-screen video, not a game
        Assert.False(Decide());
    }

    [Fact]
    public void Borderless_and_windowed_games_are_found_by_their_folder_or_their_graphics()
    {
        Assert.True(Decide(path: @"D:\SteamLibrary\steamapps\common\Some Game\game.exe", graphics: true, gpu: 20));
        Assert.True(Decide(path: @"E:\Games\Foo\foo.exe", graphics: true, gpu: 8));
        Assert.True(Decide(covers: true, graphics: true, gpu: 40)); // borderless, unknown folder
        Assert.False(Decide(covers: true, graphics: false, gpu: 90)); // fills the screen but loads no game graphics
        Assert.False(Decide(path: @"D:\steamapps\common\x\x.exe", graphics: true, gpu: 0)); // folder alone, card idle
    }

    [Fact]
    public void Browsers_players_launchers_and_Shabpa_are_never_games()
    {
        foreach (var program in new[] { "chrome", "vlc", "explorer", "shabpa", "steam", "discord", "RiotClientServices" })
            Assert.False(Decide(state: 3, gpu: 90, covers: true, graphics: true, process: program), program);
    }

    [Fact]
    public void A_program_the_person_lists_is_a_game_whatever_it_looks_like()
    {
        Assert.True(Decide(process: "weirdgame", user: ["WeirdGame.exe"]));
        Assert.True(Decide(process: "cs2", user: ["cs2"]));
        Assert.False(Decide(process: "shabpa", user: ["shabpa"]));
    }

    [Fact]
    public void The_timer_reads_naturally()
    {
        Assert.Equal("05:09", GameRules.Elapsed(TimeSpan.FromSeconds(309)));
        Assert.Equal("1:02:03", GameRules.Elapsed(TimeSpan.FromSeconds(3723)));
        Assert.Equal("00:00", GameRules.Elapsed(TimeSpan.FromSeconds(-5)));
    }
}

public sealed class StoreSafetyTests
{
    [Fact]
    public void A_daily_backup_is_kept_and_used_when_the_main_file_is_broken()
    {
        var folder = Path.Combine(Path.GetTempPath(), "shabpa-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new Store(folder);
            store.State.Macros.Add(new LauncherMacro { LauncherKey = "steam", Steps = [new MacroStep()] });
            store.Save();
            new Store(folder).Load(); // a normal start makes today's copy
            Assert.Single(Directory.GetFiles(Path.Combine(folder, "backups"), "state-*.json"));

            File.WriteAllText(Path.Combine(folder, "state.json"), "{ not json");
            var broken = new Store(folder);
            broken.Load();
            Assert.Single(broken.State.Macros); // came back from the copy
            Assert.True(File.Exists(Path.Combine(folder, "state.json.broken")));
        }
        finally
        {
            try { Directory.Delete(folder, true); } catch { }
        }
    }

    [Fact]
    public void A_file_held_open_by_another_program_does_not_make_saving_throw_or_wait()
    {
        var folder = Path.Combine(Path.GetTempPath(), "shabpa-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var store = new Store(folder);
            store.Save();
            using var hold = new FileStream(Path.Combine(folder, "state.json"), FileMode.Open, FileAccess.Read, FileShare.None);
            store.State.Settings.Tariff = 777;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            store.Save(); // must return at once and try again in the background
            Assert.True(watch.ElapsedMilliseconds < 400, $"waited {watch.ElapsedMilliseconds} ms");
            hold.Dispose();
            var saved = false;
            for (var i = 0; i < 40 && !saved; i++)
            {
                Thread.Sleep(100);
                saved = File.ReadAllText(Path.Combine(folder, "state.json")).Contains("777");
            }
            Assert.True(saved, "the retry in the background never wrote the file");
        }
        finally
        {
            try { Directory.Delete(folder, true); } catch { }
        }
    }
}

public sealed class WingetManifestTests
{
    [Fact]
    public void The_winget_manifests_agree_with_each_other_and_point_at_a_published_release()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Shabpa.slnx"))) dir = dir.Parent;
        var root = Path.Combine(dir!.FullName, "packaging", "winget", "manifests", "m", "MrPitPot", "Shabpa");
        // The experimental Shabpa-next copy has no packaging folder (releases are never built from it); nothing to check there.
        if (!Directory.Exists(root)) return;
        var versions = Directory.GetDirectories(root);
        Assert.NotEmpty(versions);
        foreach (var folder in versions)
        {
            var version = Path.GetFileName(folder);
            var files = Directory.GetFiles(folder, "*.yaml");
            Assert.Equal(3, files.Length);
            foreach (var file in files)
            {
                var text = File.ReadAllText(file);
                Assert.Contains($"PackageVersion: {version}", text);
                Assert.Contains("PackageIdentifier: MrPitPot.Shabpa", text);
            }
            var installer = File.ReadAllText(files.Single(f => f.EndsWith(".installer.yaml")));
            Assert.Contains($"releases/download/v{version}/ShabpaSetup.exe", installer);
            var hash = System.Text.RegularExpressions.Regex.Match(installer, @"InstallerSha256: ([0-9A-Fa-f]{64})\b");
            Assert.True(hash.Success, "InstallerSha256 must be 64 hex digits");
            Assert.Contains($"## [{version}]", File.ReadAllText(Path.Combine(dir.FullName, "CHANGELOG.md")));
        }
    }
}
