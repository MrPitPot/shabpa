using Microsoft.Win32;

namespace Shabpa.Core;

/// <summary>Tells when a game is in true exclusive full screen, where no window (the overlay included) can be drawn over it.
/// Windows normally runs a "full screen" Direct3D game through fullscreen optimizations: the game thinks it owns the screen,
/// but the desktop compositor still draws other windows on top. A game whose optimizations were turned off (the
/// compatibility check box "Disable fullscreen optimizations", or the system-wide GameDVR switch that tweak tools set) really
/// takes the screen over, and a click-through overlay cannot be seen until the game is borderless / windowed or the
/// optimizations are on again. This is why the same game shows the overlay on one PC and not on another.</summary>
public static class ExclusiveFullscreen
{
    /// <summary>The compatibility layer behind "Disable fullscreen optimizations".</summary>
    public const string CompatFlag = "DISABLEDXMAXIMIZEDWINDOWEDMODE";

    private const string LayersKey = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    private const string GameConfigKey = @"System\GameConfigStore";

    /// <summary>A compatibility value such as "~ DISABLEDXMAXIMIZEDWINDOWEDMODE HIGHDPIAWARE" turns the optimizations off.</summary>
    public static bool FlagDisables(string? layers) =>
        layers is not null && layers.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(l => l.Equals(CompatFlag, StringComparison.OrdinalIgnoreCase));

    /// <summary>The same value with the flag taken out; null when nothing else is left (the value can be deleted).</summary>
    public static string? WithoutFlag(string layers)
    {
        var rest = layers.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(l => !l.Equals(CompatFlag, StringComparison.OrdinalIgnoreCase)).ToList();
        return rest.Count == 0 || (rest.Count == 1 && rest[0] is "~" or "$") ? null : string.Join(' ', rest);
    }

    /// <summary>The GameDVR switches that "optimizer" tools flip: GameDVR_FSEBehaviorMode = 2 (which Windows obeys together with
    /// GameDVR_HonorUserFSEBehaviorMode = 1), or GameDVR_FSEBehavior = 2. Either turns fullscreen optimizations off for every game.</summary>
    public static bool GlobalOff(object? behaviorMode, object? honorUser, object? behavior) => (behaviorMode is 2 && honorUser is 1) || behavior is 2;

    /// <param name="notificationState">From SHQueryUserNotificationState; 3 = a Direct3D program holds the screen.</param>
    /// <returns>True when the overlay certainly cannot be seen: the game is exclusive and nothing composes over it.</returns>
    public static bool Blocks(int notificationState, Why why) => notificationState == 3 && why != Why.None;

    public enum Why { None, ThisGame, ThisGameAllUsers, AllGames }

    /// <summary>Why fullscreen optimizations are off for this program, if they are. Reads the registry only.</summary>
    public static Why Reason(string? exePath)
    {
        try
        {
            if (!string.IsNullOrEmpty(exePath))
            {
                using (var user = Registry.CurrentUser.OpenSubKey(LayersKey))
                    if (FlagDisables(user?.GetValue(exePath) as string)) return Why.ThisGame;
                using (var machine = Registry.LocalMachine.OpenSubKey(LayersKey))
                    if (FlagDisables(machine?.GetValue(exePath) as string)) return Why.ThisGameAllUsers;
            }
            using var config = Registry.CurrentUser.OpenSubKey(GameConfigKey);
            if (GlobalOff(config?.GetValue("GameDVR_FSEBehaviorMode"), config?.GetValue("GameDVR_HonorUserFSEBehaviorMode"), config?.GetValue("GameDVR_FSEBehavior"))) return Why.AllGames;
        }
        catch
        {
            // a locked registry: nothing is known, so nothing is claimed
        }
        return Why.None;
    }

    /// <summary>Turns fullscreen optimizations back on for this program for the current user (the reverse of ticking the
    /// compatibility box). Takes effect the next time the game starts. Returns false when there was nothing to undo here.</summary>
    public static bool Reenable(string exePath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(LayersKey, writable: true);
            if (key?.GetValue(exePath) is not string layers || !FlagDisables(layers)) return false;
            if (WithoutFlag(layers) is { } rest) key.SetValue(exePath, rest);
            else key.DeleteValue(exePath, throwOnMissingValue: false);
            Log.Write($"fullscreen optimizations turned back on for {System.IO.Path.GetFileName(exePath)}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Write($"could not turn fullscreen optimizations back on: {ex.Message}");
            return false;
        }
    }
}
