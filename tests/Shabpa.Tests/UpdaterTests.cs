using System.IO;

namespace Shabpa.Tests;

public class UpdaterTests
{
    private const string Release = """
        {
          "tag_name": "v2.1.0",
          "draft": false,
          "prerelease": false,
          "published_at": "2026-11-02T10:00:00Z",
          "body": "- faster ping chart\n- updates from Telegram",
          "assets": [
            { "name": "checksums.txt", "browser_download_url": "https://github.com/o/r/releases/download/v2.1.0/checksums.txt", "size": 100 },
            { "name": "ShabpaSetup.exe", "browser_download_url": "https://github.com/o/r/releases/download/v2.1.0/ShabpaSetup.exe",
              "size": 1963193, "digest": "sha256:ABCDEF0123" }
          ]
        }
        """;

    [Theory]
    [InlineData("v2.1.0", "2.1.0")]
    [InlineData("2.1", "2.1.0")]
    [InlineData("Shabpa 3.0.4-beta", "3.0.4")]
    public void Versions_are_read_from_tags(string tag, string expected) => Assert.Equal(Version.Parse(expected), Updater.ParseVersion(tag));

    [Fact]
    public void A_tag_without_a_version_is_ignored() => Assert.Null(Updater.ParseVersion("latest"));

    [Fact]
    public void Github_release_gives_version_installer_and_checksum()
    {
        var info = Updater.ParseRelease(Release)!;
        Assert.Equal(new Version(2, 1, 0), info.Version);
        Assert.EndsWith("/ShabpaSetup.exe", info.DownloadUrl);
        Assert.Equal("abcdef0123", info.Sha256);
        Assert.Equal(1963193, info.Size);
        Assert.Contains("Telegram", info.Notes);
    }

    [Fact]
    public void Releases_without_the_installer_or_marked_prerelease_are_skipped()
    {
        Assert.Null(Updater.ParseRelease(Release.Replace("ShabpaSetup.exe\"", "Other.exe\"")));
        Assert.Null(Updater.ParseRelease(Release.Replace("\"prerelease\": false", "\"prerelease\": true")));
    }

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
    public void The_bot_understands_update()
    {
        Assert.Equal(BotCommand.Update, TelegramBot.Parse("به‌روزرسانی"));
        Assert.Equal(BotCommand.Update, TelegramBot.Parse("/update"));
        Assert.Equal(BotCommand.Update, TelegramBot.Parse("Update"));
    }
}
