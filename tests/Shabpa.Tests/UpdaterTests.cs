using System.IO;

namespace Shabpa.Tests;

public class UpdaterTests
{
    [Theory]
    [InlineData("v2.1.0", "2.1.0")]
    [InlineData("2.1", "2.1.0")]
    [InlineData("Shabpa 3.0.4-beta", "3.0.4")]
    public void Versions_are_read_from_tags(string tag, string expected) => Assert.Equal(Version.Parse(expected), Updater.ParseVersion(tag));

    [Fact]
    public void A_tag_without_a_version_is_ignored() => Assert.Null(Updater.ParseVersion("latest"));

    [Fact]
    public void Manifest_feed_is_understood_and_must_use_https()
    {
        var info = Updater.ParseManifest("""{"version":"2.2.0","url":"https://example.org/ShabpaSetup.exe","sha256":"AA11","notes":"n"}""")!;
        Assert.Equal(new Version(2, 2, 0), info.Version);
        Assert.Equal("aa11", info.Sha256);
        Assert.Null(Updater.ParseManifest("""{"version":"2.2.0","url":"http://example.org/ShabpaSetup.exe"}"""));
    }

    [Fact]
    public void A_file_with_the_wrong_checksum_is_deleted_not_run()
    {
        var file = Path.Combine(Path.GetTempPath(), $"shabpa-update-{Guid.NewGuid():N}.exe");
        File.WriteAllText(file, "not the real installer");
        Assert.Throws<InvalidDataException>(() => Updater.Verify(file, new string('0', 64)));
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void A_file_that_is_not_the_shabpa_installer_is_deleted_not_run()
    {
        // Notepad has the right shape (a signed exe) but the wrong product name.
        var file = Path.Combine(Path.GetTempPath(), $"shabpa-update-{Guid.NewGuid():N}.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "notepad.exe"), file);
        Assert.Throws<InvalidDataException>(() => Updater.Verify(file, null));
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void A_file_without_a_published_checksum_is_never_accepted()
    {
        var file = Path.Combine(Path.GetTempPath(), $"shabpa-update-{Guid.NewGuid():N}.exe");
        File.WriteAllText(file, "anything");
        Assert.Throws<InvalidDataException>(() => Updater.Verify(file, ""));
        Assert.False(File.Exists(file));
    }
}
