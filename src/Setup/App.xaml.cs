using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;

namespace Shabpa.Setup;

public partial class App : Application
{
    public static string SelfPath => Process.GetCurrentProcess().MainModule?.FileName ?? "";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        // Windows 10 1607+ negotiates TLS 1.2/1.3 by itself; older defaults could still offer TLS 1.0.
        System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
        var args = e.Args.Select(a => a.ToLowerInvariant()).ToList();
        var uninstall = args.Contains("--uninstall");

        // Silent modes for scripted installs: --quiet installs (or uninstalls) without any window.
        if (args.Contains("--quiet"))
        {
            var code = 0;
            try
            {
                var target = InstallTarget.Machine;
                Installer.StopRunningApp();
                if (uninstall)
                {
                    Installer.Uninstall(target, removeData: args.Contains("--remove-data"), scheduleFolderRemoval: true);
                }
                else
                {
                    // --update (from the app's updater) keeps the existing desktop shortcut and start-up setting.
                    var update = args.Contains("--update");
                    if (!Runtime.IsInstalled())
                    {
                        var error = Runtime.InstallAsync(_ => { }, (fa, _) => fa).GetAwaiter().GetResult();
                        if (error is not null) throw new InvalidOperationException(error);
                    }
                    var desktop = update ? File.Exists(target.DesktopLink) : !args.Contains("--no-desktop");
                    Installer.Install(target, Installer.EmbeddedPayload(), new InstallOptions(desktop, args.Contains("--autostart")), SelfPath);
                    if (args.Contains("--autostart")) Installer.SetAutostart(target, true);
                    if (args.Contains("--launch")) Installer.Launch(target, updated: update);
                }
            }
            catch
            {
                code = 1;
            }
            Shutdown(code);
            return;
        }

        new SetupWindow(uninstall).Show();
    }
}
