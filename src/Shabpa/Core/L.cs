namespace Shabpa.Core;

/// <summary>Two-language text: every user-facing string is written once as (Persian, English).</summary>
public static class L
{
    /// <summary>True for English, false for Persian. Set once at startup; changing it restarts the app.</summary>
    public static bool En { get; set; }

    public static string T(string fa, string en) => En ? en : fa;

    /// <summary>List separator: Persian comma or ASCII comma.</summary>
    public static string Comma => En ? ", " : "، ";
}
