using Shabpa.Core;

namespace Shabpa.Tour;

public enum TourEnd { Done, Skipped }

/// <summary>The running state of one tour: which step is showing, going back and forth, finishing or skipping. No UI in here, so
/// it is unit-tested; <see cref="TourOverlay"/> only draws it. Clicking the right menu item while a step asks for it moves on by itself.</summary>
public sealed class TourSession(IReadOnlyList<TourStep> steps)
{
    public IReadOnlyList<TourStep> Steps { get; } = steps.Count > 0 ? steps : throw new ArgumentException("a tour needs at least one step");
    public int Index { get; private set; }
    public TourStep Current => Steps[Index];
    public bool IsFirst => Index == 0;
    public bool IsLast => Index == Steps.Count - 1;
    public bool Ended { get; private set; }
    public TourEnd? Outcome { get; private set; }

    /// <summary>The step changed (the overlay draws the first step itself, so this is not raised when the tour starts).</summary>
    public event Action? Changed;
    public event Action<TourEnd>? Finished;

    /// <summary>"3 of 8", in the language of the app (Persian digits for Persian).</summary>
    public string Counter => L.En ? $"{Index + 1} of {Steps.Count}" : $"{Fa.Digits((Index + 1).ToString())} از {Fa.Digits(Steps.Count.ToString())}";

    /// <summary>0 to 1, for the thin progress line.</summary>
    public double Progress => Steps.Count <= 1 ? 1 : (double)Index / (Steps.Count - 1);

    /// <summary>Next step; on the last one it ends the tour as done.</summary>
    public void Next()
    {
        if (Ended) return;
        if (IsLast) { End(TourEnd.Done); return; }
        Index++;
        Changed?.Invoke();
    }

    public void Back()
    {
        if (Ended || IsFirst) return;
        Index--;
        Changed?.Invoke();
    }

    public void Skip() => End(TourEnd.Skipped);

    /// <summary>The window opened page <paramref name="key"/>. When the step is waiting for exactly that click, the tour moves on.</summary>
    public bool PageOpened(string key)
    {
        if (Ended || !Current.NeedsClick || !string.Equals(Current.ClickPage, key, StringComparison.Ordinal)) return false;
        Next();
        return true;
    }

    private void End(TourEnd how)
    {
        if (Ended) return;
        Ended = true;
        Outcome = how;
        Finished?.Invoke(how);
    }

    /// <summary>Records how the tour ended (or that it started) in the settings; callers save the store afterwards.</summary>
    public static void Record(Settings settings, TourEnd how) => settings.TourStatus = how == TourEnd.Done ? TourState.Done : TourState.Skipped;
}
