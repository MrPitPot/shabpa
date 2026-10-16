using Shabpa.Core;

namespace Shabpa.Tests;

public class PowerPlanTests
{
    [Fact]
    public void The_active_plan_is_read_from_powercfg_output_in_any_language()
    {
        Assert.Equal("381b4222-f694-41f0-9685-ff5bb260df2e", PowerPlan.ParseActive("Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)"));
        Assert.Equal("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", PowerPlan.ParseActive("شناسه‌ی طرح: 8C5E7FDA-E8BF-4A96-9A85-A6E23A8C635C  (عملکرد بالا)"));
        Assert.Null(PowerPlan.ParseActive("nothing here"));
    }

    [Fact]
    public void The_plan_list_is_parsed()
    {
        const string text = """
            Existing Power Schemes (* Active)
            -----------------------------------
            Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced) *
            Power Scheme GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (High performance)
            """;
        var list = PowerPlan.ParseList(text);
        Assert.Equal(2, list.Count);
        Assert.Contains(PowerPlan.HighPerformance, list);
    }

    [Theory]
    [InlineData("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", true)]
    [InlineData("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c & calc", false)]
    [InlineData("SCHEME_MIN", false)]
    [InlineData("", false)]
    public void Only_real_guids_reach_the_command_line(string value, bool expected) => Assert.Equal(expected, PowerPlan.IsGuid(value));

    [Fact]
    public void Game_mode_is_off_by_default_and_remembers_nothing()
    {
        var c = new ComfortSettings();
        Assert.False(c.GameModeOn);
        Assert.Equal("", c.PendingPowerPlan);
    }
}
