using System.Windows;

namespace Shabpa.Kit;

/// <summary>WPF makes the FIRST window ever created the application's main window. Several places look the main window up
/// through <c>Application.MainWindow</c> (the birthday preview, the quick cards, the theme switch), and each does nothing at all
/// when that is some other window, for example a countdown or a notice that opened before the real window did.</summary>
public static class MainWindowClaim
{
    /// <summary>Makes <paramref name="window"/> the application's main window unless it already is.</summary>
    public static void Adopt(Application app, Window window)
    {
        if (!ReferenceEquals(app.MainWindow, window)) app.MainWindow = window;
    }
}
