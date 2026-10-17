using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;

namespace Shabpa.Setup;

/// <summary>The installer window. Silent runs (--quiet) never get here: <see cref="Program"/> handles them without WPF.</summary>
public partial class App : Application
{
    public static string SelfPath => Program.SelfPath;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = SetupArgs.Parse(e.Args);

#if DEBUG || SHABPA_PREVIEW
        // Test-only (Debug builds): --preview <state> shows the window without installing anything, see Preview.cs.
        var preview = PreviewSpec.Parse(e.Args);
        if (preview is not null)
        {
            new SetupWindow(preview.Uninstall, preview).ShowPreview();
            return;
        }
#endif
        new SetupWindow(args.Uninstall).Show();
    }
}
