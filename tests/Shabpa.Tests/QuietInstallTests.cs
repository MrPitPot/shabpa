using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Win32;
using Shabpa.Setup;

namespace Shabpa.Tests;

/// <summary>Keeps the setup log of the test run out of the real %ProgramData%.</summary>
internal static class SetupLogRedirect
{
    public static readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "shabpa-tests-setup-log-" + Guid.NewGuid().ToString("N") + ".txt");

    [ModuleInitializer]
    internal static void Init() => Environment.SetEnvironmentVariable("SHABPA_SETUP_LOG", Path);
}

public class SetupArgsTests
{
    [Theory]
    [InlineData("--quiet")]
    [InlineData("--QUIET")]
    [InlineData("/quiet")]
    [InlineData("-quiet")]
    [InlineData("--silent")]
    [InlineData("/S")]
    [InlineData("-q")]
    [InlineData("/verysilent")]
    [InlineData("--passive")]
    public void Every_common_spelling_of_quiet_is_silent(string arg) => Assert.True(SetupArgs.Parse([arg]).Quiet);

    [Fact]
    public void Several_switches_in_one_argument_are_read_one_by_one()
    {
        var a = SetupArgs.Parse(["--Quiet --update  --no-desktop"]);
        Assert.True(a.Quiet);
        Assert.True(a.Update);
        Assert.True(a.NoDesktop);
        Assert.False(a.Uninstall);
    }

    [Fact]
    public void Without_a_silent_switch_the_window_is_shown()
    {
        Assert.False(SetupArgs.Parse([]).Quiet);
        Assert.False(SetupArgs.Parse(["--uninstall"]).Quiet);
        Assert.True(SetupArgs.Parse(["--uninstall"]).Uninstall);
        Assert.False(SetupArgs.Parse(["--update"]).Quiet);
    }

    [Fact]
    public void The_updaters_command_line_is_understood()
    {
        var a = SetupArgs.Parse(["--quiet", "--update", "--launch"]);
        Assert.True(a.Quiet && a.Update && a.Launch);
    }

    [Fact]
    public void Exit_codes_are_distinct()
    {
        int[] codes = [ExitCodes.Ok, ExitCodes.Failed, ExitCodes.RuntimeMissing, ExitCodes.WriteFailed, ExitCodes.RegistryFailed,
                       ExitCodes.PayloadDamaged, ExitCodes.UninstallFailed, ExitCodes.Timeout, ExitCodes.Unhandled];
        Assert.Equal(codes.Length, codes.Distinct().Count());
    }
}

public sealed class QuietInstallTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "shabpa-quiet-" + Guid.NewGuid().ToString("N"));
    private readonly string _registryPath = @"Software\ShabpaTests\" + Guid.NewGuid().ToString("N");
    private readonly RegistryKey _registry;
    private readonly InstallTarget _target;

    public QuietInstallTests()
    {
        _registry = Registry.CurrentUser.CreateSubKey(_registryPath);
        // Neither the Start Menu nor the Desktop folder exists yet, like on a freshly made image.
        _target = new InstallTarget(Path.Combine(_root, "Program Files", "Shabpa"), _registry, Path.Combine(_root, "Start"), Path.Combine(_root, "Desktop"));
    }

    public void Dispose()
    {
        _registry.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_registryPath, false);
        try { Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    private static byte[] Payload3() =>
        Shabpa.Setup.Payload.Pack("3.10.0", [new PayloadFile("Shabpa.exe", Encoding.UTF8.GetBytes("app")), new PayloadFile("sub/x.dll", [1, 2, 3])]);

    private QuietEnvironment Env(Func<bool>? runtime = null, Func<string?>? download = null, Func<byte[]>? payload = null,
                                 InstallTarget? target = null, TimeSpan? watchdog = null, Action? stop = null, Action<int>? terminate = null) => new()
    {
        Target = target ?? _target,
        Payload = payload ?? Payload3,
        RuntimeInstalled = runtime ?? (() => true),
        InstallRuntime = download ?? (() => throw new InvalidOperationException("the runtime must not be downloaded here")),
        StopApp = stop ?? (() => { }),
        SelfPath = "",
        CleanSystem = false,
        DataDir = Path.Combine(_root, "AppData"),
        Watchdog = watchdog ?? TimeSpan.FromMinutes(5),
        Terminate = terminate ?? (_ => { }),
    };

    [Fact]
    public void A_silent_install_writes_files_every_uninstall_value_and_shortcuts_even_when_their_folders_do_not_exist()
    {
        Assert.Equal(0, Quiet.Run(SetupArgs.Parse(["--quiet"]), Env()));

        Assert.Equal("app", File.ReadAllText(_target.Exe));
        Assert.True(File.Exists(_target.StartMenuLink));
        Assert.True(File.Exists(_target.DesktopLink));
        using var key = _registry.OpenSubKey(Installer.UninstallKey)!;
        foreach (var name in new[] { "DisplayName", "DisplayVersion", "Publisher", "InstallLocation", "UninstallString", "QuietUninstallString", "DisplayIcon" })
            Assert.False(string.IsNullOrEmpty(key.GetValue(name) as string), name);
        Assert.Equal("3.10.0", key.GetValue("DisplayVersion"));
        Assert.Equal("Mr.PitPot", key.GetValue("Publisher"));
        Assert.Equal(_target.Dir, key.GetValue("InstallLocation"));
        Assert.Equal($"\"{_target.Uninstaller}\" --uninstall --quiet", key.GetValue("QuietUninstallString"));
        Assert.True((int)key.GetValue("EstimatedSize")! >= 1);
    }

    [Fact]
    public void The_runtime_is_not_downloaded_when_it_is_already_there()
    {
        // Env() makes InstallRuntime throw: reaching it would turn the result into a failure.
        Assert.Equal(0, Quiet.Run(SetupArgs.Parse(["--quiet"]), Env(runtime: () => true)));
    }

    [Fact]
    public void A_missing_runtime_that_cannot_be_downloaded_fails_with_its_own_code_and_installs_nothing()
    {
        var code = Quiet.Run(SetupArgs.Parse(["--quiet"]), Env(runtime: () => false, download: () => "offline"));
        Assert.Equal(ExitCodes.RuntimeMissing, code);
        Assert.False(Directory.Exists(_target.Dir));
        Assert.Null(Installer.InstalledVersion(_target));
    }

    [Fact]
    public void A_downloaded_runtime_lets_the_install_go_on()
    {
        var downloaded = false;
        var code = Quiet.Run(SetupArgs.Parse(["--quiet"]), Env(runtime: () => downloaded, download: () => { downloaded = true; return null; }));
        Assert.Equal(0, code);
        Assert.True(File.Exists(_target.Exe));
    }

    [Fact]
    public void A_damaged_installer_fails_before_anything_is_downloaded_or_written()
    {
        var code = Quiet.Run(SetupArgs.Parse(["--quiet"]), Env(runtime: () => false, payload: () => Encoding.UTF8.GetBytes("garbage garbage garbage")));
        Assert.Equal(ExitCodes.PayloadDamaged, code);
        Assert.False(Directory.Exists(_target.Dir));
    }

    [Fact]
    public void A_missing_payload_has_its_own_code()
    {
        var code = Quiet.Run(SetupArgs.Parse(["--quiet"]), Env(payload: () => throw new SetupException(ExitCodes.PayloadDamaged, "no payload")));
        Assert.Equal(ExitCodes.PayloadDamaged, code);
    }

    [Fact]
    public void Files_that_cannot_be_written_give_the_write_code()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Program Files"));
        File.WriteAllText(Path.Combine(_root, "Program Files", "Shabpa"), "a file where the folder should be");
        Assert.Equal(ExitCodes.WriteFailed, Quiet.Run(SetupArgs.Parse(["--quiet"]), Env()));
    }

    [Fact]
    public void A_read_only_registry_gives_the_registry_code()
    {
        using var readOnly = Registry.CurrentUser.OpenSubKey(_registryPath, writable: false)!;
        var target = _target with { RegistryRoot = readOnly };
        Assert.Equal(ExitCodes.RegistryFailed, Quiet.Run(SetupArgs.Parse(["--quiet"]), Env(target: target)));
    }

    [Fact]
    public void A_shortcut_that_cannot_be_made_does_not_fail_the_install()
    {
        File.Create(Path.Combine(Directory.CreateDirectory(_root).FullName, "Start")).Dispose();   // a file where the Start Menu folder should be
        Assert.Equal(0, Quiet.Run(SetupArgs.Parse(["--quiet"]), Env()));
        Assert.True(File.Exists(_target.Exe));
        Assert.Equal("3.10.0", Installer.InstalledVersion(_target));
    }

    [Fact]
    public void An_unexpected_exception_becomes_a_plain_failure_code()
    {
        Assert.Equal(ExitCodes.Failed, Quiet.Run(SetupArgs.Parse(["--quiet"]), Env(payload: () => throw new InvalidOperationException("boom"))));
    }

    [Fact]
    public async Task A_run_that_hangs_is_ended_by_the_watchdog_with_its_code()
    {
        var terminated = new ManualResetEventSlim();
        var gate = new ManualResetEventSlim();
        var code = -1;
        var worker = Task.Run(() => Quiet.Run(SetupArgs.Parse(["--quiet"]), Env(
            watchdog: TimeSpan.FromMilliseconds(200), stop: () => gate.Wait(10_000),
            terminate: c => { code = c; terminated.Set(); })));
        Assert.True(terminated.Wait(5000), "the watchdog never fired");
        Assert.Equal(ExitCodes.Timeout, code);
        gate.Set();
        await worker;
    }

    [Fact]
    public void A_quick_run_does_not_trip_the_watchdog()
    {
        var fired = false;
        Quiet.Run(SetupArgs.Parse(["--quiet"]), Env(watchdog: TimeSpan.FromMilliseconds(300), terminate: _ => fired = true));
        Thread.Sleep(600);
        Assert.False(fired);
    }

    [Fact]
    public void A_silent_uninstall_removes_the_entry_files_and_shortcuts()
    {
        Quiet.Run(SetupArgs.Parse(["--quiet"]), Env());
        Assert.Equal(0, Quiet.Run(SetupArgs.Parse(["--uninstall", "--quiet"]), Env()));
        Assert.Null(Installer.InstalledVersion(_target));
        Assert.False(File.Exists(_target.StartMenuLink));
        Assert.False(File.Exists(_target.DesktopLink));
        Assert.False(File.Exists(_target.Exe));
    }

    [Fact]
    public void Update_keeps_the_desktop_shortcut_only_if_it_was_there()
    {
        Quiet.Run(SetupArgs.Parse(["--quiet", "--no-desktop"]), Env());
        Assert.False(File.Exists(_target.DesktopLink));
        Quiet.Run(SetupArgs.Parse(["--quiet", "--update"]), Env());
        Assert.False(File.Exists(_target.DesktopLink), "an update must not add a desktop shortcut the user did not want");
    }

    [Fact]
    public void The_log_records_the_run()
    {
        Quiet.Run(SetupArgs.Parse(["--quiet"]), Env());
        Assert.NotNull(SetupLog.Path);
        var text = File.ReadAllText(SetupLog.Path!);
        Assert.Contains("silent install", text);
        Assert.Contains("install finished", text);
        Assert.Contains("exit code 0", text);
    }
}

public class RuntimeDetectionTests
{
    private static string Dotnet(params string[] versions)
    {
        var root = Path.Combine(Path.GetTempPath(), "shabpa-dn-" + Guid.NewGuid().ToString("N"));
        foreach (var v in versions) Directory.CreateDirectory(Path.Combine(root, "shared", "Microsoft.WindowsDesktop.App", v));
        Directory.CreateDirectory(root);
        return root;
    }

    [Theory]
    [InlineData("10.0.1", true)]
    [InlineData("10.0.0-rc.2.25502.107", true)]
    [InlineData("9.0.8", false)]
    [InlineData("100.0.0", false)]
    public void Only_a_ten_dot_x_desktop_runtime_counts(string version, bool expected)
    {
        var root = Dotnet(version);
        try { Assert.Equal(expected, Runtime.IsInstalled(root)); }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void A_folder_without_the_desktop_runtime_is_not_enough()
    {
        var root = Dotnet();
        try { Assert.False(Runtime.IsInstalled(root)); }
        finally { Directory.Delete(root, true); }
    }
}

public class MachineTargetTests
{
    [Fact]
    public void The_real_target_is_always_made_of_absolute_paths_in_the_64_bit_program_files()
    {
        var t = InstallTarget.Machine;
        Assert.True(Path.IsPathRooted(t.Dir) && Path.IsPathRooted(t.StartMenuDir) && Path.IsPathRooted(t.DesktopDir));
        Assert.EndsWith(@"\Shabpa", t.Dir);
        Assert.Equal(Path.Combine(Environment.GetEnvironmentVariable("ProgramW6432") ?? InstallTarget.ProgramFilesDir(), "Shabpa"), t.Dir);
        Assert.Contains("Programs", t.StartMenuDir);
        Assert.Contains("HKEY_LOCAL_MACHINE", t.RegistryRoot.Name);
    }
}
