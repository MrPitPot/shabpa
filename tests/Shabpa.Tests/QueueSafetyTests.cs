using Shabpa.Core;

namespace Shabpa.Tests;

/// <summary>The "shut down again at 01:30 after the PC was already off at 01:00" class of bug.</summary>
public class QueueSafetyTests
{
    private static readonly DateTime Night = new(2026, 5, 10, 1, 0, 0);

    private static CommandQueue NewQueue(out List<ScheduledCommand> items)
    {
        items = [];
        return new CommandQueue(items);
    }

    [Fact]
    public void The_same_one_shot_pressed_twice_is_added_once()
    {
        var queue = NewQueue(out var items);
        Assert.True(queue.TryAdd(PowerAction.Shutdown, Night.AddMinutes(60), null, "", out _));
        Assert.False(queue.TryAdd(PowerAction.Shutdown, Night.AddMinutes(60).AddSeconds(20), null, "", out var existing));
        Assert.Single(items);
        Assert.Same(items[0], existing);
    }

    [Fact]
    public void A_different_action_or_a_clearly_different_time_is_not_a_duplicate()
    {
        var queue = NewQueue(out var items);
        queue.TryAdd(PowerAction.Shutdown, Night.AddMinutes(60), null, "", out _);
        Assert.True(queue.TryAdd(PowerAction.Sleep, Night.AddMinutes(60), null, "", out _));
        Assert.True(queue.TryAdd(PowerAction.Shutdown, Night.AddMinutes(90), null, "", out _));
        Assert.Equal(3, items.Count);
    }

    [Fact]
    public void Weekly_schedules_with_the_same_time_and_days_are_duplicates()
    {
        var queue = NewQueue(out var items);
        DayOfWeek[] days = [DayOfWeek.Monday, DayOfWeek.Friday];
        Assert.True(queue.TryAdd(PowerAction.Shutdown, Night, days, "", out _));
        Assert.False(queue.TryAdd(PowerAction.Shutdown, Night.AddDays(7), days.Reverse(), "", out _));
        Assert.True(queue.TryAdd(PowerAction.Shutdown, Night, [DayOfWeek.Monday], "", out _));
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public void After_a_power_action_the_remaining_one_shots_are_cleared_but_weekly_schedules_stay()
    {
        var queue = NewQueue(out var items);
        queue.TryAdd(PowerAction.Shutdown, Night.AddMinutes(30), null, "", out _);
        queue.TryAdd(PowerAction.Sleep, Night.AddMinutes(90), null, "", out _);
        queue.TryAdd(PowerAction.Shutdown, Night.AddHours(23), [DayOfWeek.Sunday], "", out _);

        var gone = queue.ClearOneShots();

        Assert.Equal(2, gone.Count);
        var left = Assert.Single(items);
        Assert.True(left.Repeats);
    }

    [Fact]
    public void Commands_that_came_due_while_the_app_was_not_running_are_dropped_not_fired_late()
    {
        var queue = NewQueue(out var items);
        queue.TryAdd(PowerAction.Shutdown, Night.AddMinutes(30), null, "", out _);          // 01:30, PC was off
        queue.TryAdd(PowerAction.Shutdown, Night.AddMinutes(30), [DayOfWeek.Sunday], "", out _); // weekly
        queue.TryAdd(PowerAction.Sleep, Night.AddHours(5), null, "", out _);                 // still in the future

        var boot = Night.AddMinutes(31); // switched back on one minute later
        var missed = queue.DropOverdue(boot);

        Assert.Equal(2, missed.Count);
        Assert.Empty(queue.TakeDue(boot));
        Assert.Equal(2, items.Count); // weekly one rescheduled + the future one
        Assert.All(items, c => Assert.True(c.DueAt > boot));
    }

    [Fact]
    public void A_gap_in_the_clock_skips_only_what_fell_inside_it()
    {
        var queue = NewQueue(out var items);
        queue.TryAdd(PowerAction.Shutdown, Night.AddMinutes(20), null, "", out _); // inside the sleep
        queue.TryAdd(PowerAction.Shutdown, Night.AddMinutes(45), null, "", out _); // after waking
        var asleepFrom = Night.AddMinutes(10);
        var wokeAt = Night.AddMinutes(30);

        var missed = queue.DropMissed(asleepFrom, wokeAt);

        Assert.Single(missed);
        Assert.Single(items);
        Assert.Equal(Night.AddMinutes(45), items[0].DueAt);
    }

    [Fact]
    public void A_removed_command_never_comes_due()
    {
        var queue = NewQueue(out _);
        queue.TryAdd(PowerAction.Shutdown, Night.AddMinutes(1), null, "", out var cmd);
        Assert.True(queue.Remove(cmd.Id));
        Assert.Empty(queue.TakeDue(Night.AddMinutes(1)));
        Assert.Empty(queue.Items);
    }
}
