using System.IO;
using System.Reflection;
using Shabpa.Core;
using Xunit;

namespace Shabpa.Tests;

/// <summary>The scheduled-download flow through the real hub (dry run: no launcher is really opened).</summary>
public class DownloadPlanTests
{
    private static void OnSta(Action test)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { test(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Exception("STA test failed", error);
    }

    private static (Hub hub, Action<DateTime> run) NewHub()
    {
        var hub = new Hub(new Store(Path.Combine(Path.GetTempPath(), "shabpa-dl-" + Guid.NewGuid().ToString("N"))), dryRun: true, demo: false);
        var method = typeof(Hub).GetMethod("RunDownloadPlans", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (hub, now => method.Invoke(hub, [now]));
    }

    private static readonly LauncherInfo Steam = new() { Key = "steam", Name = "Steam", ExePath = @"C:\steam.exe", Process = "steam" };

    [Fact]
    public void Due_plan_starts_the_download_rule_and_clears_when_the_rule_ends()
    {
        OnSta(() =>
        {
            var (hub, run) = NewHub();
            var due = DateTime.Now.AddSeconds(-5);
            Assert.True(hub.AddDownloadPlan(Steam, due, PowerAction.Shutdown, 12));
            Assert.False(hub.AddDownloadPlan(Steam, due, PowerAction.Shutdown, 12)); // same plan twice

            run(due.AddSeconds(-1)); // not due yet: nothing
            Assert.Null(hub.State.ActiveRule);

            run(DateTime.Now);
            var plan = Assert.Single(hub.State.DownloadPlans);
            Assert.Equal(PlanState.Running, plan.State);
            var rule = hub.State.ActiveRule;
            Assert.NotNull(rule);
            Assert.Equal(RuleKind.Network, rule!.Kind);
            Assert.Equal(PowerAction.Shutdown, rule.Action);
            Assert.Equal(12, rule.HoldMinutes);
            Assert.True(rule.WaitForStart);

            // The user cancels the rule (or it fired): the plan is cleaned up.
            hub.DeactivateRule();
            run(DateTime.Now);
            Assert.Empty(hub.State.DownloadPlans);
        });
    }

    [Fact]
    public void Plan_missed_by_minutes_is_dropped_not_started_late()
    {
        OnSta(() =>
        {
            var (hub, run) = NewHub();
            hub.AddDownloadPlan(Steam, DateTime.Now.AddMinutes(-20), PowerAction.Shutdown, 10);
            run(DateTime.Now);
            Assert.Empty(hub.State.DownloadPlans);
            Assert.Null(hub.State.ActiveRule);
        });
    }

    [Fact]
    public void Existing_rule_is_never_replaced_by_a_plan()
    {
        OnSta(() =>
        {
            var (hub, run) = NewHub();
            hub.ActivateRule(new Rule { Kind = RuleKind.Load, Action = PowerAction.Sleep });
            hub.AddDownloadPlan(Steam, DateTime.Now.AddSeconds(-1), PowerAction.Shutdown, 10);
            run(DateTime.Now);
            Assert.Equal(RuleKind.Load, hub.State.ActiveRule!.Kind);
            Assert.Equal(PlanState.Waiting, Assert.Single(hub.State.DownloadPlans).State);
        });
    }

    [Fact]
    public void Removing_a_running_plan_stops_its_watch()
    {
        OnSta(() =>
        {
            var (hub, run) = NewHub();
            hub.AddDownloadPlan(Steam, DateTime.Now.AddSeconds(-1), PowerAction.Shutdown, 10);
            run(DateTime.Now);
            Assert.NotNull(hub.State.ActiveRule);
            hub.RemoveDownloadPlan(hub.State.DownloadPlans[0].Id);
            Assert.Null(hub.State.ActiveRule);
            Assert.Empty(hub.State.DownloadPlans);
        });
    }
}
