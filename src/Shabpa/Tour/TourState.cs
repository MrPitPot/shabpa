using Shabpa.Core;

namespace Shabpa.Tour;

/// <summary>What the app should do about the guided tour when its window first opens.</summary>
public enum TourStart
{
    /// <summary>Nothing: the person already saw, skipped or declined it.</summary>
    None,
    /// <summary>A brand-new install: start the tour.</summary>
    Run,
    /// <summary>Somebody who updated from an older version: only offer it, with a small card (never forced).</summary>
    Offer,
}

/// <summary>The tour flag kept in <see cref="Settings.TourStatus"/>. It is additive: older files simply lack it ("").
/// <para>"" = nothing decided yet. "pending" = a first-run tour was started but not finished (it starts again next time).
/// "offered" = the card was shown to an existing user (once, never again). "done", "skipped", "declined" = final.</para></summary>
public static class TourState
{
    public const string Pending = "pending", Offered = "offered", Done = "done", Skipped = "skipped", Declined = "declined";

    /// <summary>Decides what happens at the first window opening. <paramref name="freshInstall"/> is true when the data file
    /// did not exist before this run (see <see cref="Store.FreshInstall"/>).</summary>
    public static TourStart Decide(string? status, bool freshInstall) => status switch
    {
        Pending => TourStart.Run,
        null or "" => freshInstall ? TourStart.Run : TourStart.Offer,
        _ => TourStart.None,
    };

    /// <summary>True when the status is a final one (the tour never starts by itself again).</summary>
    public static bool IsFinal(string? status) => status is Offered or Done or Skipped or Declined;
}
