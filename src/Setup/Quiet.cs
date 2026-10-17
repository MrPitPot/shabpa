using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Shabpa.Setup;

/// <summary>The command line, read forgivingly: any case, "-", "--" or "/" prefixes, and several switches inside one
/// argument ("--quiet --update" passed as a single string). winget appends the manifest's Silent switch as one string.</summary>
public sealed class SetupArgs
{
    private static readonly HashSet<string> QuietNames = new(StringComparer.Ordinal)
        { "quiet", "silent", "s", "q", "qn", "qb", "verysilent", "passive", "unattended" };

    public bool Quiet { get; private set; }
    public bool Uninstall { get; private set; }
    public bool Update { get; private set; }
    public bool NoDesktop { get; private set; }
    public bool Autostart { get; private set; }
    public bool Launch { get; private set; }
    public bool RemoveData { get; private set; }
    public IReadOnlyList<string> Tokens { get; private set; } = [];

    public static SetupArgs Parse(IEnumerable<string> raw)
    {
        var tokens = new List<string>();
        foreach (var arg in raw)
            foreach (var part in (arg ?? "").ToLowerInvariant().Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var name = part.TrimStart('-', '/');
                if (name.Length > 0) tokens.Add(name);
            }
        var result = new SetupArgs { Tokens = tokens };
        foreach (var token in tokens)
        {
            if (QuietNames.Contains(token)) result.Quiet = true;
            else switch (token)
            {
                case "uninstall": result.Uninstall = true; break;
                case "update": result.Update = true; break;
                case "no-desktop": result.NoDesktop = true; break;
                case "autostart": result.Autostart = true; break;
                case "launch": result.Launch = true; break;
                case "remove-data": result.RemoveData = true; break;
            }
        }
        return result;
    }
}

/// <summary>Everything the silent run touches, so tests can swap the real machine for a sandbox.</summary>
public sealed class QuietEnvironment
{
    private InstallTarget? _target;
    public InstallTarget Target { get => _target ??= InstallTarget.Machine; init => _target = value; }
    public Func<byte[]> Payload { get; init; } = Installer.EmbeddedPayload;
    public Func<bool> RuntimeInstalled { get; init; } = () => Runtime.IsInstalled();
    /// <summary>Downloads and installs the runtime; returns an error text or null.</summary>
    public Func<string?> InstallRuntime { get; init; } = () => Runtime.InstallAsync(_ => { }, (_, en) => en).GetAwaiter().GetResult();
    public Action StopApp { get; init; } = Installer.StopRunningApp;
    public string SelfPath { get; init; } = "";
    public bool CleanSystem { get; init; } = true;
    public string? DataDir { get; init; }
    public Action<InstallTarget, bool>? Launch { get; init; }
    public Action<InstallTarget, bool>? Autostart { get; init; }
    /// <summary>The run is ended with <see cref="ExitCodes.Timeout"/> if it takes longer than this.</summary>
    public TimeSpan Watchdog { get; init; } = TimeSpan.FromMinutes(10);
    /// <summary>What the watchdog does after logging; the real one ends the process.</summary>
    public Action<int> Terminate { get; init; } = code => Environment.Exit(code);
}

/// <summary>The scripted install (winget, the app's own updater): no WPF, no window, no message box, ever. Every step is
/// logged, every failure has its own exit code, and a watchdog ends a run that hangs.</summary>
public static class Quiet
{
    public static int Run(SetupArgs args, QuietEnvironment env)
    {
        var started = DateTime.UtcNow;
        using var done = new ManualResetEvent(false);
        var watchdog = new Thread(() =>
        {
            if (done.WaitOne(env.Watchdog)) return;
            SetupLog.Error("the silent run did not finish in " + env.Watchdog + "; ending it");
            env.Terminate(ExitCodes.Timeout);
        }) { IsBackground = true, Name = "setup-watchdog" };
        watchdog.Start();

        var code = ExitCodes.Ok;
        try
        {
            SetupLog.Info("mode: " + (args.Uninstall ? "silent uninstall" : "silent install") +
                          (args.Update ? " (update)" : "") + "  target=" + env.Target.Dir);
            if (!SetupLog.IsAdministrator() && TestHooks.Root is null)
                SetupLog.Warn("not running as administrator: writing to Program Files and HKLM will probably fail");

            env.StopApp();
            if (args.Uninstall) Uninstall(args, env);
            else Install(args, env);
        }
        catch (SetupException ex)
        {
            code = ex.Code;
            SetupLog.Error("failed with exit code " + code + ": " + ex.Message, ex.InnerException ?? ex);
        }
        catch (Exception ex)
        {
            code = args.Uninstall ? ExitCodes.UninstallFailed : ExitCodes.Failed;
            SetupLog.Error("failed with exit code " + code + ": " + ex.Message, ex);
        }
        finally
        {
            done.Set();
        }
        SetupLog.Info("exit code " + code + " after " + (int)(DateTime.UtcNow - started).TotalMilliseconds + " ms");
        return code;
    }

    private static void Install(SetupArgs args, QuietEnvironment env)
    {
        var target = env.Target;
        // --update (from the app's updater) keeps the existing desktop shortcut and start-up setting.
        var desktop = args.Update ? System.IO.File.Exists(target.DesktopLink) : !args.NoDesktop;

        // Read the payload first: a damaged installer must fail before anything is downloaded or touched.
        byte[] payload;
        try
        {
            payload = env.Payload();
            Payload.Unpack(payload);
        }
        catch (InvalidDataException ex) { throw new SetupException(ExitCodes.PayloadDamaged, ex.Message, ex); }

        if (env.RuntimeInstalled())
        {
            SetupLog.Info(".NET " + Runtime.Major + " Desktop Runtime is present; no download needed");
        }
        else
        {
            SetupLog.Info(".NET " + Runtime.Major + " Desktop Runtime is missing; installing it");
            var error = env.InstallRuntime();
            if (error is not null) throw new SetupException(ExitCodes.RuntimeMissing, error);
        }

        Installer.Install(target, payload, new InstallOptions(desktop, args.Autostart), env.SelfPath);
        // Starting the app is a courtesy: if it fails the install is still complete.
        try { if (args.Autostart) env.Autostart?.Invoke(target, true); }
        catch (Exception ex) { SetupLog.Warn("autostart setting failed: " + ex.Message); }
        try { if (args.Launch) env.Launch?.Invoke(target, args.Update); }
        catch (Exception ex) { SetupLog.Warn("launching the app failed: " + ex.Message); }
    }

    private static void Uninstall(SetupArgs args, QuietEnvironment env)
    {
        try
        {
            Installer.Uninstall(env.Target, args.RemoveData, scheduleFolderRemoval: true, cleanSystem: env.CleanSystem, dataDir: env.DataDir);
        }
        catch (SetupException) { throw; }
        catch (Exception ex) { throw new SetupException(ExitCodes.UninstallFailed, "uninstall failed: " + ex.Message, ex); }
    }
}
