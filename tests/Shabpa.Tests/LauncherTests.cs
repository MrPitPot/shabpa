using Shabpa.Core;
using Xunit;

namespace Shabpa.Tests;

public sealed class LauncherTests
{
    private static readonly string[] Roots = [@"C:\Program Files", @"C:\Program Files (x86)"];

    [Fact]
    public void Finds_launchers_from_folders_and_registry()
    {
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"D:\Games\Steam\steam.exe",
            @"C:\Program Files (x86)\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe",
        };
        var found = LauncherDetector.Detect(
            (key, name) => key == @"HKCU\Software\Valve\Steam" && name == "SteamExe" ? @"D:\Games\Steam\steam.exe" : null,
            present.Contains, Roots, @"C:\ProgramData");

        Assert.Equal(["steam", "epic"], found.Select(l => l.Key));
        Assert.Equal(@"D:\Games\Steam\steam.exe", found[0].ExePath);
        Assert.Equal("epicgameslauncher", found[1].Process);
    }

    [Fact]
    public void Nothing_installed_finds_nothing()
    {
        Assert.Empty(LauncherDetector.Detect((_, _) => null, _ => false, Roots, @"C:\ProgramData"));
    }

    [Fact]
    public void Registry_path_that_does_not_exist_is_ignored()
    {
        var found = LauncherDetector.Detect((_, _) => @"Z:\gone\steam.exe", _ => false, Roots, @"C:\ProgramData");
        Assert.Empty(found);
    }

    [Fact]
    public void Overlay_captions_are_short_english()
    {
        foreach (var def in OverlayMetrics.All)
        {
            var caption = OverlayMetrics.Abbrev(def.Key);
            Assert.Matches("^[A-Z0-9% ]+$", caption);
            Assert.True(caption.Length <= 10, caption);
        }
    }
}
