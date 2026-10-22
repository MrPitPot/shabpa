using Shabpa.Core;

namespace Shabpa.Tests;

public class BidarMoodTests
{
    private static readonly DateTime T0 = new(2026, 10, 22, 21, 0, 0);

    [Fact]
    public void CalmClicksCycleThroughCuteMoods()
    {
        var logic = new BidarMoodLogic();
        var seen = new List<BidarMood>();
        for (var i = 0; i < 8; i++) seen.Add(logic.Click(T0.AddSeconds(i * 5)));
        Assert.Equal([BidarMood.Surprised, BidarMood.Happy, BidarMood.Wink, BidarMood.Love, BidarMood.Surprised, BidarMood.Happy, BidarMood.Wink, BidarMood.Love], seen);
    }

    [Fact]
    public void QuickBurstMakesHimDizzyThenCalmsDown()
    {
        var logic = new BidarMoodLogic();
        BidarMood last = BidarMood.Idle;
        for (var i = 0; i < BidarMoodLogic.BurstClicks; i++) last = logic.Click(T0.AddMilliseconds(i * 200));
        Assert.Equal(BidarMood.Dizzy, last);
        Assert.NotEqual(BidarMood.Dizzy, logic.Click(T0.AddSeconds(10)));
    }

    [Fact]
    public void FallsAsleepOnlyAfterAQuietMinute()
    {
        var logic = new BidarMoodLogic();
        Assert.False(logic.ShouldSleep(T0)); // never touched: nothing to doze from
        logic.Touch(T0);
        Assert.False(logic.ShouldSleep(T0.AddSeconds(59)));
        Assert.True(logic.ShouldSleep(T0.AddSeconds(60)));
        logic.Touch(T0.AddSeconds(61));
        Assert.False(logic.ShouldSleep(T0.AddSeconds(100)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1000, 0)]
    [InlineData(-30, 400)]
    [InlineData(5, 5)]
    public void PupilsNeverLeaveTheEye(double dx, double dy)
    {
        var (x, y) = BidarMoodLogic.Look(dx, dy, 3.2);
        Assert.True(Math.Sqrt(x * x + y * y) <= 3.2 + 1e-9);
        if (dx == 0 && dy == 0) Assert.Equal((0.0, 0.0), (x, y));
    }

    [Fact]
    public void EveryMoodHasASanePose()
    {
        foreach (var mood in Enum.GetValues<BidarMood>())
        {
            var p = BidarPoses.For(mood);
            Assert.InRange(p.WingL, -45, 160);
            Assert.InRange(p.WingR, -45, 160);
            Assert.InRange(p.Squash, 0.88, 1.1);
            Assert.InRange(p.Beak, 0, 1);
            Assert.InRange(p.Blush, 0, 1);
            Assert.InRange(p.Lids, 0, 1);
        }
        Assert.Equal(BidarEyes.Hearts, BidarPoses.For(BidarMood.Love).Eyes);
        Assert.True(BidarPoses.For(BidarMood.Wink).WinkRight);
        Assert.Equal(BidarEyes.Closed, BidarPoses.For(BidarMood.Sleepy).Eyes);
    }
}
