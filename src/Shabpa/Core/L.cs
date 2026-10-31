namespace Shabpa.Core;

/// <summary>Two-language text: every user-facing string is written once as (Persian, English).</summary>
public static class L
{
    private static bool _en;

    [ThreadStatic] private static bool? _scope;

    /// <summary>True for English, false for Persian. Set once at startup; changing it restarts the app. Inside a
    /// <see cref="Scope"/> (a chat with Bidar) it is the language the person is writing in.</summary>
    public static bool En
    {
        get => _scope ?? _en;
        set => _en = value;
    }

    /// <summary>Everything written inside the scope uses the given language (null = no change), then the app language is back.
    /// Used when Bidar answers in the language the person typed.</summary>
    public static IDisposable Scope(bool? english) => new ScopeToken(english);

    private sealed class ScopeToken : IDisposable
    {
        private readonly bool? _before = _scope;

        public ScopeToken(bool? english)
        {
            if (english is not null) _scope = english;
        }

        public void Dispose() => _scope = _before;
    }

    public static string T(string fa, string en) => En ? en : fa;

    /// <summary>List separator: Persian comma or ASCII comma.</summary>
    public static string Comma => En ? ", " : "، ";
}
