using System.IO;
using Shabpa.Core;

namespace Shabpa.Tests;

/// <summary>A tab switched off in Settings leaves the list and its background work stops; only the listed tabs can be switched off (Home, Settings and Updates always stay).</summary>
public class TabSwitchTests
{
    private static Hub NewHub() => new(new Store(Path.Combine(Path.GetTempPath(), "shabpa-tab-" + Guid.NewGuid().ToString("N"))) { ReadOnly = true }, dryRun: true, demo: true);

    [Fact]
    public void Home_settings_and_updates_can_never_be_switched_off()
    {
        Assert.DoesNotContain(OptionalTabs.All, t => t.Key is "home" or "settings" or "update" or "kit");
        var hub = NewHub();
        hub.SetTab("home", false);
        hub.SetTab("settings", false);
        Assert.Empty(hub.Settings.HiddenTabs);
        Assert.Equal(["rgb", "tasks"], OptionalTabs.Clean(["rgb", "home", "rgb", "nonsense", "tasks"]));
    }

    [Fact]
    public void Switching_off_lighting_the_overlay_comfort_and_reminders_stops_them_and_switching_on_does_not_turn_them_back_on()
    {
        var hub = NewHub();
        hub.Settings.Rgb.Enabled = true;
        hub.Settings.Overlay.Mode = OverlayMode.Always;
        hub.Settings.Comfort.BreakOn = true;
        hub.Settings.Comfort.DimAuto = true;
        hub.Settings.TaskReminders = true;
        var changes = 0;
        hub.TabsChanged += () => changes++;

        foreach (var key in new[] { "rgb", "gaming", "comfort", "tasks" }) hub.SetTab(key, false);

        Assert.False(hub.Settings.Rgb.Enabled);
        Assert.Equal(OverlayMode.Off, hub.Settings.Overlay.Mode);
        Assert.False(hub.Settings.Comfort.BreakOn);
        Assert.False(hub.Settings.Comfort.DimAuto);
        Assert.False(hub.Settings.TaskReminders);
        Assert.True(hub.TabOff("rgb") && hub.TabOff("tasks"));
        Assert.Equal(4, changes);

        hub.SetTab("rgb", true);
        Assert.False(hub.TabOff("rgb"));
        Assert.False(hub.Settings.Rgb.Enabled);                       // it comes back as a tab; the lights stay off until the person turns them on
        hub.SetTab("rgb", false);
        hub.SetTab("rgb", false);                                     // twice is the same as once
        Assert.Equal(1, hub.Settings.HiddenTabs.Count(k => k == "rgb"));
    }

    [Fact]
    public void A_hand_edited_list_cannot_lock_home_or_settings_and_a_tab_comes_back_even_if_listed_twice()
    {
        var hub = NewHub();
        hub.Settings.HiddenTabs = ["home", "settings", "rgb", "rgb", "nonsense"];
        Assert.False(hub.TabOff("home"));
        Assert.False(hub.TabOff("settings"));
        Assert.True(hub.TabOff("rgb"));
        hub.SetTab("rgb", true);
        Assert.False(hub.TabOff("rgb"));                                   // both copies are gone
    }

    [Fact]
    public void A_sentence_does_not_start_what_was_switched_off()
    {
        var hub = NewHub();
        hub.SetTab("rgb", false);
        var reply = hub.RunAssistant(new AssistantCommand { Intent = CommandIntent.Rgb, On = true }, "box");
        Assert.False(reply.Ok);
        Assert.False(hub.Settings.Rgb.Enabled);                           // the lights stay off
        hub.SetTab("system", false);
        Assert.False(hub.RunAssistant(new AssistantCommand { Intent = CommandIntent.OpenPage, Page = "system:rules" }, "box").Ok);   // a tab inside a page counts too
    }
}
