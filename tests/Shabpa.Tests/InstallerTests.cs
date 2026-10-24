using System.IO;
using System.Text;
using Microsoft.Win32;
using Shabpa.Setup;

namespace Shabpa.Tests;

public class PayloadTests
{
    private static PayloadFile F(string path, string text) => new(path, Encoding.UTF8.GetBytes(text));

    [Fact]
    public void Files_survive_packing()
    {
        var files = new[] { F("Shabpa.exe", "exe"), F("amd64/KernelTraceControl.dll", "native"), F("WinDivert.dll", new string('x', 100_000)) };
        var (version, unpacked) = Payload.Unpack(Payload.Pack("2.0.0", files));
        Assert.Equal("2.0.0", version);
        Assert.Equal(files.Select(f => f.Path), unpacked.Select(f => f.Path));
        Assert.All(files.Zip(unpacked), pair => Assert.Equal(pair.First.Data, pair.Second.Data));
    }

    [Fact]
    public void Repetitive_content_compresses_well()
    {
        var packed = Payload.Pack("1", [F("big.txt", string.Concat(Enumerable.Repeat("Shabpa night watch ", 50_000)))]);
        Assert.True(packed.Length < 10_000, $"{packed.Length} bytes");
    }

    [Fact]
    public void A_damaged_installer_is_refused_rather_than_installed()
    {
        var packed = Payload.Pack("1", [F("a.txt", new string('a', 5000) + "end")]);
        packed[^3] ^= 0x55;
        Assert.Throws<InvalidDataException>(() => Payload.Unpack(packed));
        Assert.Throws<InvalidDataException>(() => Payload.Unpack(Encoding.UTF8.GetBytes("not a payload at all")));
    }

    [Theory]
    [InlineData("../evil.exe")]
    [InlineData("sub/../../evil.exe")]
    [InlineData("C:/Windows/evil.exe")]
    public void Paths_cannot_escape_the_install_folder(string path)
    {
        Assert.Throws<InvalidDataException>(() => Payload.Unpack(Payload.Pack("1", [F(path, "x")])));
    }
}

public sealed class InstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "shabpa-install-" + Guid.NewGuid().ToString("N"));
    private readonly string _registryPath = @"Software\ShabpaTests\" + Guid.NewGuid().ToString("N");
    private readonly RegistryKey _registry;
    private readonly InstallTarget _target;

    public InstallerTests()
    {
        // Everything goes to a temp folder and a throwaway HKCU key: nothing touches the real install.
        _registry = Registry.CurrentUser.CreateSubKey(_registryPath);
        _target = new InstallTarget(Path.Combine(_root, "Program Files", "Shabpa"), _registry, Path.Combine(_root, "Start"), Path.Combine(_root, "Desktop"));
    }

    public void Dispose()
    {
        _registry.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_registryPath, false);
        using (var parent = Registry.CurrentUser.OpenSubKey(@"Software\ShabpaTests"))
            if (parent is { SubKeyCount: 0 }) Registry.CurrentUser.DeleteSubKey(@"Software\ShabpaTests", false);
        try { Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    private static byte[] Build(string version, params (string path, string text)[] files) =>
        Payload.Pack(version, files.Select(f => new PayloadFile(f.path, Encoding.UTF8.GetBytes(f.text))));

    [Fact]
    public void Install_writes_files_entry_and_shortcuts()
    {
        var version = Installer.Install(_target, Build("2.0.0", ("Shabpa.exe", "app"), ("amd64/KernelTraceControl.dll", "native")),
            new InstallOptions(DesktopShortcut: true, Autostart: false), uninstallerSource: null);

        Assert.Equal("2.0.0", version);
        Assert.Equal("app", File.ReadAllText(_target.Exe));
        Assert.True(File.Exists(Path.Combine(_target.Dir, "amd64", "KernelTraceControl.dll")));
        Assert.True(File.Exists(_target.StartMenuLink));
        Assert.True(File.Exists(_target.DesktopLink));
        Assert.Equal("2.0.0", Installer.InstalledVersion(_target));

        using var key = _registry.OpenSubKey(Installer.UninstallKey)!;
        Assert.Equal($"\"{_target.Uninstaller}\" --uninstall", key.GetValue("UninstallString"));
        Assert.Equal(_target.Exe, key.GetValue("DisplayIcon"));
    }

    [Fact]
    public void Update_replaces_files_and_drops_ones_the_new_version_no_longer_has()
    {
        Installer.Install(_target, Build("1.0.0", ("Shabpa.exe", "v1"), ("old.dll", "gone soon")), new InstallOptions(true, false), null);
        // A file the user put there is not ours and must survive.
        File.WriteAllText(Path.Combine(_target.Dir, "notes.txt"), "mine");

        Installer.Install(_target, Build("2.0.0", ("Shabpa.exe", "v2"), ("new.dll", "fresh")), new InstallOptions(false, false), null);
        Assert.Equal("v2", File.ReadAllText(_target.Exe));
        Assert.False(File.Exists(Path.Combine(_target.Dir, "old.dll")));
        Assert.True(File.Exists(Path.Combine(_target.Dir, "new.dll")));
        Assert.True(File.Exists(Path.Combine(_target.Dir, "notes.txt")));
        Assert.False(File.Exists(_target.DesktopLink), "unticking the desktop shortcut removes it");
        Assert.Equal("2.0.0", Installer.InstalledVersion(_target));
    }

    [Fact]
    public void Installer_copies_itself_as_the_uninstaller()
    {
        var source = Path.Combine(_root, "ShabpaSetup.exe");
        Directory.CreateDirectory(_root);
        File.WriteAllText(source, "setup");
        Installer.Install(_target, Build("2.0.0", ("Shabpa.exe", "app")), new InstallOptions(false, false), source);
        Assert.Equal("setup", File.ReadAllText(_target.Uninstaller));
    }

    [Fact]
    public void After_an_update_the_app_is_told_so_it_comes_to_the_front()
    {
        Assert.Equal("--updated", Installer.LaunchArguments(updated: true));
        Assert.Equal("", Installer.LaunchArguments(updated: false));
    }

    [Fact]
    public void Uninstall_removes_everything_it_created()
    {
        Installer.Install(_target, Build("2.0.0", ("Shabpa.exe", "app"), ("sub/x.dll", "x")), new InstallOptions(true, false), null);
        Installer.Uninstall(_target, removeData: false, scheduleFolderRemoval: false, cleanSystem: false);

        Assert.False(Directory.Exists(_target.Dir));
        Assert.False(File.Exists(_target.StartMenuLink));
        Assert.False(File.Exists(_target.DesktopLink));
        Assert.Null(Installer.InstalledVersion(_target));
    }

    [Fact]
    public void The_real_installer_payload_contains_the_whole_app()
    {
        // Only meaningful after build-installer.sh; a plain test build has no payload.
        byte[] payload;
        try { payload = Installer.EmbeddedPayload(); }
        catch (InvalidDataException) { return; }
        var (version, files) = Payload.Unpack(payload);
        Assert.Equal("3.11.5", version);
        var names = files.Select(f => f.Path).ToHashSet();
        foreach (var required in new[] { "Shabpa.exe", "Shabpa.dll", "WinDivert.dll", "WinDivert64.sys", "Microsoft.Diagnostics.Tracing.TraceEvent.dll", "Shabpa.runtimeconfig.json" })
            Assert.Contains(required, names);
    }
}

public class RuntimeTests
{
    [Fact]
    public void This_pc_has_the_desktop_runtime() => Assert.True(Runtime.IsInstalled());

    [Fact]
    public void A_pc_without_it_is_detected()
    {
        var empty = Path.Combine(Path.GetTempPath(), "shabpa-no-dotnet-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(empty, "shared", "Microsoft.WindowsDesktop.App", "8.0.31"));
        try { Assert.False(Runtime.IsInstalled(empty)); }
        finally { Directory.Delete(empty, true); }
    }

    [Fact]
    public void Microsoft_signed_files_pass_and_unsigned_ones_do_not()
    {
        Assert.True(Runtime.IsSignedByMicrosoft(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe")));
        Assert.False(Runtime.IsSignedByMicrosoft(typeof(RuntimeTests).Assembly.Location));
    }

    [Fact]
    public void The_built_installer_identifies_itself_for_the_updater()
    {
        // The updater refuses anything whose product name is not "Shabpa Setup".
        var setup = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "installer", "ShabpaSetup.exe"));
        if (!File.Exists(setup)) return;
        Assert.Equal("Shabpa Setup", System.Diagnostics.FileVersionInfo.GetVersionInfo(setup).ProductName);
    }
}
