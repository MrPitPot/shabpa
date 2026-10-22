using Shabpa.Core;
using Xunit;

namespace Shabpa.Tests;

/// <summary>While limits exist every packet of the PC waits for the shaper's lock, so the slow work of rebuilding the port map
/// (reading the table, naming processes) must never run under it. No driver is opened here.</summary>
public class ShaperLockTests
{
    private static EffectiveLimit Rule(string app) => new(app, 5, 5, false, false);

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
}
