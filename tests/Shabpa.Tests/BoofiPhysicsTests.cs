using Shabpa.Kit;

namespace Shabpa.Tests;

/// <summary>The springs that make Boofi and what he wears move: whatever frame rate the screen has and however floppy or heavy the part is,
/// a spring must stay inside its starting swing, never blow up, and come to rest.</summary>
public class BoofiPhysicsTests
{
    // every (omega, zeta) pair the outfit code hands out, and the head, body, wing and watch springs
    private static readonly (double W, double Z)[] Feels =
        [(12, .35), (18, .3), (6, .25), (7.5, .14), (14, .5), (10, .3), (7, .22), (15, .5), (11, .3), (13, .35), (14, .4), (7.8, .25)];

    [Theory]
    [InlineData(1 / 240.0)]
    [InlineData(1 / 144.0)]
    [InlineData(1 / 60.0)]
    [InlineData(1 / 30.0)]
    [InlineData(1 / 40.0)]            // the longest frame is cut to 50 ms and split in two, so 25 ms is the worst step
    public void A_released_spring_swings_back_inside_its_start_and_comes_to_rest(double dt)
    {
        foreach (var (w, z) in Feels)
        {
            var s = new BidarOwl.Spring { X = 30 };
            var peak = 0.0;
            var steps = (int)(30 / dt);
            for (var i = 0; i < steps; i++)
            {
                s.Step(dt, 0, w, z);
                Assert.True(double.IsFinite(s.X) && double.IsFinite(s.V), $"w={w} z={z} dt={dt:F4}");
                peak = Math.Max(peak, Math.Abs(s.X));
            }
            Assert.True(peak <= 30 * 1.02, $"w={w} z={z} dt={dt:F4} peak={peak}");     // it never swings wider than it started
            Assert.True(s.Calm(0), $"w={w} z={z} dt={dt:F4} rests (x={s.X}, v={s.V})");
        }
    }

    [Fact]
    public void A_spring_that_follows_a_moving_target_ends_on_it_without_running_away()
    {
        var s = new BidarOwl.Spring();
        var dt = 1 / 60.0;
        for (var i = 0; i < 600; i++) s.Step(dt, 20 * Math.Sin(i * dt * 3), 12, .35);
        for (var i = 0; i < 1200; i++) s.Step(dt, 5, 12, .35);
        Assert.True(s.Calm(5), $"x={s.X} v={s.V}");
    }

    [Fact]
    public void A_light_kick_on_a_hanging_part_settles_instead_of_spinning_forever()
    {
        // what the physics loop does to a hat: a velocity impulse from the body's acceleration, then the spring pulls it back
        var s = new BidarOwl.Spring();
        s.V += 900 * (1 / 60.0) * 0.35;
        for (var i = 0; i < 60 * 20; i++) s.Step(1 / 60.0, 0, 12, .35);
        Assert.True(s.Calm(0), $"x={s.X} v={s.V}");
    }
}
