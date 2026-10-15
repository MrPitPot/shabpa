using System.IO;
using System.Text.RegularExpressions;
using Shabpa.Core;

namespace Shabpa.Tests;

/// <summary>Guards for the "nothing leaves this PC, nothing is deleted behind your back" promises of the daily-use
/// features (tasks, files, backup, health, profiles, settings backup). They read the source so a later change that
/// breaks a promise fails a test instead of shipping.</summary>
public class PrivacyAndSafetyTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Shabpa.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private static string Source(string relative) => File.ReadAllText(Path.Combine(RepoRoot(), "src", "Shabpa", relative.Replace('/', Path.DirectorySeparatorChar)));

    private static readonly string[] LocalOnlyFiles =
    [
        "Core/Tasks.cs", "Core/Files.cs", "Core/Backup.cs", "Core/Health.cs", "Core/Profiles.cs", "Core/SettingsBackup.cs",
        "Pages/TasksPage.cs", "Pages/FilesPage.cs", "Pages/HealthPage.cs", "Pages/ProfilesPage.cs",
    ];

    [Fact]
    public void The_daily_use_features_contain_no_network_code()
    {
        var forbidden = new[] { "HttpClient", "WebClient", "WebRequest", "HttpWebRequest", "Socket", "TcpClient", "UdpClient", "WebSocket", "api.telegram", "https://", "http://" };
        foreach (var file in LocalOnlyFiles)
        {
            var text = Source(file);
            foreach (var word in forbidden)
                Assert.False(text.Contains(word, StringComparison.Ordinal), $"{file} mentions {word}");
        }
    }

    [Fact]
    public void Notices_and_task_reminders_are_never_forwarded_to_the_bot()
    {
        var hub = Source("Hub.cs");
        var start = hub.IndexOf("private void RunTasks(", StringComparison.Ordinal);
        var end = hub.IndexOf("private void RunComfort(", start, StringComparison.Ordinal);
        Assert.InRange(start, 1, int.MaxValue);
        var body = hub[start..end];
        Assert.DoesNotContain("Bot.", body);
        // the generic notice function must not hand text to the bot either
        var notify = hub[hub.IndexOf("public void Notify(string title, string text)", StringComparison.Ordinal)..];
        notify = notify[..notify.IndexOf('}')];
        Assert.DoesNotContain("Bot.", notify);
    }

    [Fact]
    public void Only_a_fixed_set_of_events_goes_to_the_bot()
    {
        var hub = Source("Hub.cs");
        var kinds = Regex.Matches(hub, @"Bot\.Notify\(""(\w+)""").Select(m => m.Groups[1].Value).ToHashSet();
        Assert.Subset(new HashSet<string> { "game", "budget", "update", "warning", "linked" }, kinds);
    }

    [Fact]
    public void Every_place_that_sends_files_to_the_recycle_bin_asks_first()
    {
        var text = Source("Pages/FilesPage.cs");
        var lines = text.Split('\n');
        var sends = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].Contains("Recycler.Send(", StringComparison.Ordinal)) continue;
            sends++;
            var before = string.Join("\n", lines[Math.Max(0, i - 25)..i]);
            Assert.Contains("ConfirmWindow.AskAlways", before);
            Assert.Contains(", [_root]", lines[i]); // always limited to the folder that was scanned
        }
        Assert.True(sends >= 2);
    }

    [Fact]
    public void Nothing_in_the_new_features_deletes_files_directly()
    {
        foreach (var file in new[] { "Core/Files.cs", "Core/Tasks.cs", "Core/Health.cs", "Core/Profiles.cs", "Core/SettingsBackup.cs", "Pages/FilesPage.cs", "Pages/TasksPage.cs", "Pages/HealthPage.cs", "Pages/ProfilesPage.cs" })
        {
            var text = Source(file);
            Assert.DoesNotContain("File.Delete(", text);
            Assert.DoesNotContain("Directory.Delete(", text);
        }
        // the backup engine deletes only a file it has just written and found to be wrong (restore), never user data
        var backup = Source("Core/Backup.cs");
        Assert.Equal(1, Regex.Matches(backup, @"File\.Delete\(").Count);
        Assert.DoesNotContain("Directory.Delete(", backup);
    }

    [Fact]
    public void Starting_other_programs_is_limited_to_known_ones()
    {
        // new files may start only explorer.exe (show a file) and sc.exe (service state)
        var allowed = new Dictionary<string, string[]>
        {
            ["Core/Health.cs"] = ["sc.exe"],
            ["Pages/FilesPage.cs"] = ["explorer.exe"],
        };
        foreach (var file in LocalOnlyFiles)
        {
            var text = Source(file);
            var starts = Regex.Matches(text, @"ProcessStartInfo\(""([^""]+)""").Select(m => m.Groups[1].Value).ToList();
            var ok = allowed.TryGetValue(file, out var list) ? list : [];
            Assert.All(starts, s => Assert.Contains(s, ok));
            Assert.DoesNotContain("Process.Start(\"", text);
        }
    }

    [Theory]
    [InlineData("wuauserv & calc")]
    [InlineData("x\" /c calc")]
    [InlineData("")]
    [InlineData("..\\..\\evil")]
    public void Odd_service_names_never_reach_a_command_line(string name) => Assert.Null(Health.ServiceStateNow(name));

    [Fact]
    public void No_secrets_or_keys_are_tracked_in_the_repository_text()
    {
        var root = RepoRoot();
        var patterns = new[]
        {
            new Regex(@"\b\d{8,10}:[A-Za-z0-9_\-]{35}\b"),        // a Telegram bot token
            new Regex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----"),
            new Regex(@"ghp_[A-Za-z0-9]{30,}"),                   // a GitHub token
        };
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".cs") || f.EndsWith(".xaml") || f.EndsWith(".json") || f.EndsWith(".toml")) && !f.Contains("\\obj\\") && !f.Contains("\\bin\\")))
        {
            var text = File.ReadAllText(file);
            foreach (var pattern in patterns) Assert.False(pattern.IsMatch(text), $"{file} looks like it holds a secret");
        }
    }

    [Fact]
    public void The_log_does_not_record_task_titles_or_file_names_in_the_new_code()
    {
        foreach (var file in new[] { "Core/Tasks.cs", "Core/Files.cs", "Core/Profiles.cs" })
        {
            var text = Source(file);
            Assert.DoesNotMatch(@"Log\.Write\([^)]*(Title|\.Path|FullName)", text);
        }
        // backup logs the kind of failure, not the file that failed
        var backup = Source("Core/Backup.cs");
        foreach (Match m in Regex.Matches(backup, @"Log\.Write\(([^;]*)\);"))
            Assert.DoesNotContain("SourcePath", m.Groups[1].Value);
    }

    [Fact]
    public void Nothing_is_sent_to_the_server_before_the_person_presses_connect()
    {
        var settings = new Settings();
        Assert.False(settings.BotEnabled);
        Assert.Equal("", settings.BotDeviceId);
        Assert.False(settings.TasksToBot);
    }
}
