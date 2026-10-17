using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;

namespace Shabpa.Setup;

/// <summary>The entry point. A silent run never creates WPF's Application, a Dispatcher or a window: it is plain code that
/// returns an exit code, so nothing about the desktop, fonts, the session or the shell can get in its way.</summary>
internal static class Program
{
    private static bool _quiet;

    [STAThread]
    public static int Main(string[] argv)
    {
        var args = SetupArgs.Parse(argv);
        _quiet = args.Quiet;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) => SetupLog.Warn("unobserved task exception: " + e.Exception);
        try
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            // Windows 10 1607+ negotiates TLS 1.2/1.3 by itself; older defaults could still offer TLS 1.0.
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
        }
        catch (Exception ex) { SetupLog.Warn("startup settings failed: " + ex.Message); }

        if (args.Quiet) return RunQuiet(argv, args);
        SetupLog.Begin("window", argv);
        return RunWindow();
    }

    public static string SelfPath
    {
        get
        {
            try { return Process.GetCurrentProcess().MainModule?.FileName ?? ""; } catch { return ""; }
        }
    }

    private static int RunQuiet(string[] argv, SetupArgs args)
    {
        var code = ExitCodes.Failed;
        try
        {
            SetupLog.Begin("quiet", argv);
            var target = InstallTarget.Machine;
            var test = TestHooks.Root;
            var seconds = int.TryParse(TestHooks.Env("SHABPA_SETUP_WATCHDOG_SEC"), out var s) && s > 0 ? s : 600;
            code = Quiet.Run(args, new QuietEnvironment
            {
                Target = target,
                SelfPath = SelfPath,
                CleanSystem = test is null,
                DataDir = test is null ? null : Path.Combine(test, "AppData", "Shabpa"),
                Launch = (t, updated) => Installer.Launch(t, updated),
                Autostart = (t, on) => Installer.SetAutostart(t, on),
                Watchdog = TimeSpan.FromSeconds(seconds),
            });
        }
        catch (Exception ex)
        {
            SetupLog.Error("silent run crashed", ex);
            code = ExitCodes.Unhandled;
        }
        // Whatever thread is still alive, the process ends here with the code.
        Environment.Exit(code);
        return code;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunWindow()
    {
        try
        {
            var app = new App();
            app.DispatcherUnhandledException += (_, e) => SetupLog.Error("unhandled exception in the window", e.Exception);
            app.InitializeComponent();
            return app.Run();
        }
        catch (Exception ex)
        {
            SetupLog.Error("the installer window could not start", ex);
            return ExitCodes.Unhandled;
        }
    }

    private static void OnUnhandled(object sender, UnhandledExceptionEventArgs e)
    {
        SetupLog.Error("unhandled exception (terminating=" + e.IsTerminating + ")", e.ExceptionObject as Exception);
        if (_quiet) Environment.Exit(ExitCodes.Unhandled);
    }
}
