using Shabpa.Core;
using Xunit;

namespace Shabpa.Tests;

/// <summary>While limits exist every packet of the PC waits for the shaper's lock, so the slow work of rebuilding the port map
/// (reading the table, naming processes) must never run under it. No driver is opened here.</summary>
public class ShaperLockTests
{
    private static EffectiveLimit Rule(string app) => new(app, 5, 5, false, false);

    private const string Config = "[SC] QueryServiceConfig SUCCESS" + Nl + Nl + "SERVICE_NAME: WinDivert" + Nl
        + "        TYPE               : 1  KERNEL_DRIVER " + Nl + "        START_TYPE         : 4   DISABLED" + Nl
        + "        BINARY_PATH_NAME   : " + @"\??\{0}" + Nl + "        DISPLAY_NAME       : WinDivert" + Nl;
    private const string Nl = "\r\n";

    [Fact]
    public void The_program_whose_driver_is_loaded_is_named_and_our_own_or_an_unknown_one_is_not()
    {
        var other = string.Format(Config, @"C:\Program Files\PacketRaft\WinDivert64.sys");
        Assert.Equal("PacketRaft", Shaper.ForeignDriverOwner(other, @"C:\Program Files\Shabpa\"));
        var ours = string.Format(Config, @"C:\Program Files\Shabpa\WinDivert64.sys");
        Assert.Null(Shaper.ForeignDriverOwner(ours, @"C:\Program Files\Shabpa\"));
        Assert.Null(Shaper.ForeignDriverOwner(ours, @"C:\Program Files\Shabpa"));                       // with or without the closing backslash
        Assert.Equal("Shabpa Tools", Shaper.ForeignDriverOwner(string.Format(Config, @"C:\Program Files\Shabpa Tools\WinDivert64.sys"), @"C:\Program Files\Shabpa\"));
        Assert.Null(Shaper.ForeignDriverOwner(null, @"C:\Program Files\Shabpa\"));
        Assert.Null(Shaper.ForeignDriverOwner("[SC] OpenService FAILED 1060", @"C:\Program Files\Shabpa\"));
    }

    [Theory]
    [InlineData(-2144206838, true)]     // 0x8032000A, what the owner's PC reported
    [InlineData(-2144206847, true)]     // 0x80320001
    [InlineData(5, false)]
    [InlineData(577, false)]
    [InlineData(1275, false)]
    [InlineData(0, false)]
    public void OnlyFilteringPlatformErrorsCountAsAStaleDriver(int code, bool stale) =>
        Assert.Equal(stale, Shaper.IsStaleDriverError(code));

    [Fact]
    public void SlowNamingDoesNotHoldThePacketLock()
    {
        using var shaper = new Shaper();
        var inside = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        shaper.ReadTable = () => new Dictionary<int, int> { [PortTable.Key(true, 5000)] = 42 };
        shaper.NameOf = _ => { inside.Set(); release.Wait(5000); return "game"; };

        var apply = Task.Run(() => shaper.SetRules([Rule("game")]));
        Assert.True(inside.Wait(5000), "the rebuild never asked for a name");
        Assert.True(shaper.GateFree(), "the lock was held while a process name was being looked up");
        release.Set();
        Assert.Equal(1, apply.Result);
        Assert.Equal(1, shaper.MappedPorts);
    }

    [Fact]
    public void SlowTableReadDoesNotHoldThePacketLock()
    {
        using var shaper = new Shaper();
        var inside = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        shaper.ReadTable = () => { inside.Set(); release.Wait(5000); return []; };
        var apply = Task.Run(() => shaper.SetRules([Rule("game")]));
        Assert.True(inside.Wait(5000));
        Assert.True(shaper.GateFree());
        release.Set();
        apply.Wait();
    }

    [Fact]
    public void NoRulesMeansNoPorts()
    {
        using var shaper = new Shaper();
        shaper.ReadTable = () => new Dictionary<int, int> { [PortTable.Key(true, 80)] = 1 };
        shaper.NameOf = _ => "x";
        Assert.Equal(0, shaper.SetRules([]));
        Assert.Equal(0, shaper.MappedPorts);
    }

    [Fact]
    public void OnlyNamedRulesAreMapped()
    {
        using var shaper = new Shaper();
        shaper.ReadTable = () => new Dictionary<int, int> { [PortTable.Key(true, 1)] = 1, [PortTable.Key(true, 2)] = 2 };
        shaper.NameOf = pid => pid == 1 ? "game" : "other";
        shaper.SetRules([Rule("game")]);
        Assert.Equal(1, shaper.MappedPorts);
    }

    [Fact]
    public void The_log_line_names_the_service_state_and_the_other_network_drivers_running()
    {
        var own = "SERVICE_NAME: WinDivert\n        TYPE               : 1  KERNEL_DRIVER\n        STATE              : 4  RUNNING\n";
        var all = "SERVICE_NAME: wintun\n        STATE              : 4  RUNNING\nSERVICE_NAME: ndiswan\n        STATE              : 1  STOPPED\nSERVICE_NAME: ovpn-dco\n        STATE              : 4  RUNNING\nSERVICE_NAME: dxgkrnl\n        STATE              : 4  RUNNING\n";
        var line = Shaper.SituationFrom(own, all);
        Assert.Contains("WinDivert service RUNNING", line);
        Assert.Contains("wintun, ovpn-dco", line);
        Assert.DoesNotContain("dxgkrnl", line);
        Assert.Contains("no WinDivert service", Shaper.SituationFrom(null, null));
        Assert.Contains("none seen", Shaper.SituationFrom(null, null));
    }
}
