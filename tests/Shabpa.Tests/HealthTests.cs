using Shabpa.Core;

namespace Shabpa.Tests;

internal sealed class FakeRegistry : IRegistryProbe
{
    public readonly Dictionary<(string key, string name), object?> Values = new();
    public readonly HashSet<string> Keys = new(StringComparer.OrdinalIgnoreCase);

    public object? Get(string key, string name) => Values.TryGetValue((key, name), out var v) ? v : null;

    public IReadOnlyList<string> ValueNames(string key) => [.. Values.Keys.Where(k => k.key.Equals(key, StringComparison.OrdinalIgnoreCase)).Select(k => k.name)];

    public bool KeyExists(string key) => Keys.Contains(key) || Values.Keys.Any(k => k.key.Equals(key, StringComparison.OrdinalIgnoreCase));
}

public class HealthChecksTests
{
    [Fact]
    public void Disk_space_levels()
    {
        var checks = Health.Disks([("C:", 300L << 30, 1000L << 30), ("D:", 90L << 30, 1000L << 30), ("E:", 10L << 30, 1000L << 30), ("F:", 2L << 30, 8L << 30)]);
        Assert.Equal([CheckState.Good, CheckState.Attention, CheckState.Problem, CheckState.Problem], checks.Select(c => c.State));
    }

    [Fact]
    public void A_pending_reboot_is_detected_from_any_of_the_usual_markers()
    {
        var none = new FakeRegistry();
        Assert.Equal(CheckState.Good, Health.PendingReboot(none).State);
        var a = new FakeRegistry();
        a.Keys.Add(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
        Assert.Equal(CheckState.Attention, Health.PendingReboot(a).State);
        var b = new FakeRegistry();
        b.Values[(@"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager", "PendingFileRenameOperations")] = new[] { "x" };
        Assert.Equal(CheckState.Attention, Health.PendingReboot(b, _ => true).State);
    }

    [Fact]
    public void Dead_pending_file_renames_do_not_keep_the_restart_warning()
    {
        var reg = new FakeRegistry();
        reg.Values[(@"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager", "PendingFileRenameOperations")] = new[] { @"\??\C:\gone\old.dll", "", "", "" };
        Assert.Equal(CheckState.Good, Health.PendingReboot(reg, _ => false).State);
        Assert.Equal(1, Health.LiveFileRenames(reg, p => p == @"C:\gone\old.dll"));
        Assert.Equal(CheckState.Attention, Health.PendingReboot(reg, p => p == @"C:\gone\old.dll").State);
    }

    [Fact]
    public void Sheed_is_found_by_its_running_programs_even_when_it_is_not_registered()
    {
        var found = Health.DetectByProcess(["explorer", "SheedAntivirusService", "SheedScannerService", "chrome"]);
        Assert.Equal(["Sheed"], found.Select(f => f.Name));
        Assert.Empty(Health.DetectByProcess(["explorer", "chrome"]));
        Assert.Empty(Health.DetectByProcess(["navpane", "wsctrl", "legui"]));
        Assert.Equal(["Kaspersky"], Health.DetectByProcess(["avp"]).Select(f => f.Name));
        // Defender in "passive" state (0x62100 while another antivirus runs) is not "on"
        Assert.False(Health.DecodeAv("Windows Defender", 0x62100).Enabled);
        var checks = Health.Security(new FakeRegistry(), _ => null, false, [Health.DecodeAv("Windows Defender", 0x62100), .. found]);
        Assert.Equal(CheckState.Good, checks.Single(c => c.Id == "antivirus").State);
    }

    [Fact]
    public void Monitor_refresh_rate_and_dwm_rate_are_readable()
    {
        Assert.True(DisplayRate.MonitorHz(IntPtr.Zero) is >= 24 and <= 600);
        var rate = new DisplayRate();
        rate.ComposeFps();
        Thread.Sleep(300);
        Assert.True(rate.ComposeFps() >= 0);
        Assert.NotNull(Health.AntivirusNow());
    }

    [Fact]
    public void A_third_party_antivirus_is_recognised_and_defender_off_is_then_fine()
    {
        var reg = new FakeRegistry();
        reg.Values[(@"HKLM\SOFTWARE\Microsoft\Windows Defender\Real-Time Protection", "DisableRealtimeMonitoring")] = 1;
        var sheed = Health.DecodeAv("Sheed Antivirus", 0x1000);
        Assert.True(sheed.Enabled);
        Assert.True(sheed.UpToDate);
        Assert.False(Health.DecodeAv("X", 0x0000).Enabled);
        Assert.False(Health.DecodeAv("X", 0x1010).UpToDate);
        var checks = Health.Security(reg, _ => null, false, [sheed]);
        Assert.Equal(CheckState.Good, checks.Single(c => c.Id == "antivirus").State);
        Assert.Equal(CheckState.Good, checks.Single(c => c.Id == "defender").State);
        var none = Health.Security(reg, _ => null, false, []);
        Assert.Equal(CheckState.Problem, none.Single(c => c.Id == "antivirus").State);
    }

    [Fact]
    public void Windows_update_age_is_judged()
    {
        var now = new DateTime(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc);
        FakeRegistry With(DateTime when)
        {
            var r = new FakeRegistry();
            r.Values[(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\Results\Install", "LastSuccessTime")] = when.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            return r;
        }
        Assert.Equal(CheckState.Good, Health.WindowsUpdate(With(now.AddDays(-10)), now).State);
        Assert.Equal(CheckState.Attention, Health.WindowsUpdate(With(now.AddDays(-50)), now).State);
        Assert.Equal(CheckState.Problem, Health.WindowsUpdate(With(now.AddDays(-200)), now).State);
        Assert.Equal(CheckState.Unknown, Health.WindowsUpdate(new FakeRegistry(), now).State);
    }

    [Fact]
    public void Services_are_judged_by_start_type_and_state()
    {
        var reg = new FakeRegistry();
        reg.Values[(@"HKLM\SYSTEM\CurrentControlSet\Services\EventLog", "Start")] = 2;
        reg.Values[(@"HKLM\SYSTEM\CurrentControlSet\Services\Audiosrv", "Start")] = 2;
        reg.Values[(@"HKLM\SYSTEM\CurrentControlSet\Services\Dnscache", "Start")] = 4;
        reg.Values[(@"HKLM\SYSTEM\CurrentControlSet\Services\wuauserv", "Start")] = 3;
        var checks = Health.Services(reg, name => name switch { "EventLog" => "RUNNING", "Audiosrv" => "STOPPED", "wuauserv" => "STOPPED", _ => null })
            .ToDictionary(c => c.Id);
        Assert.Equal(CheckState.Good, checks["svc:EventLog"].State);
        Assert.Equal(CheckState.Problem, checks["svc:Audiosrv"].State);   // automatic but stopped
        Assert.Equal(CheckState.Attention, checks["svc:Dnscache"].State); // disabled
        Assert.Equal(CheckState.Good, checks["svc:wuauserv"].State);      // demand start, stopped is normal
        Assert.DoesNotContain("svc:BITS", checks.Keys);                   // not installed here
    }

    [Fact]
    public void Known_event_codes_get_plain_explanations_and_unknown_ones_say_so()
    {
        var (power, harmless) = Health.Explain("Microsoft-Windows-Kernel-Power", 41);
        Assert.False(harmless);
        Assert.NotEmpty(power);
        Assert.True(Health.Explain("Microsoft-Windows-DistributedCOM", 10016).harmless);
        Assert.False(Health.Explain("Some-Unknown-Source", 9999).harmless);
    }
}

public class SecurityChecksTests
{
    private static FakeRegistry Secure()
    {
        var r = new FakeRegistry();
        foreach (var p in new[] { "DomainProfile", "StandardProfile", "PublicProfile" })
            r.Values[(@"HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\" + p, "EnableFirewall")] = 1;
        r.Values[(@"HKLM\SOFTWARE\Microsoft\Windows Defender\Real-Time Protection", "DisableRealtimeMonitoring")] = 0;
        r.Values[(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA")] = 1;
        r.Values[(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "SmartScreenEnabled")] = "Warn";
        r.Values[(@"HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server", "fDenyTSConnections")] = 1;
        r.Values[(@"HKLM\SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled")] = 1;
        return r;
    }

    [Fact]
    public void A_secure_setup_reports_all_good()
    {
        var checks = Health.Security(Secure(), _ => "RUNNING", elevated: false);
        Assert.Equal(6, checks.Count);
        Assert.All(checks, c => Assert.Equal(CheckState.Good, c.State));
    }

    [Fact]
    public void Weak_settings_are_flagged_without_overclaiming()
    {
        var r = Secure();
        r.Values[(@"HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\PublicProfile", "EnableFirewall")] = 0;
        r.Values[(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA")] = 0;
        r.Values[(@"HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server", "fDenyTSConnections")] = 0;
        r.Values[(@"HKLM\SOFTWARE\Microsoft\Windows Defender\Real-Time Protection", "DisableRealtimeMonitoring")] = 1;
        var checks = Health.Security(r, _ => "STOPPED", elevated: false).ToDictionary(c => c.Id);
        Assert.Equal(CheckState.Problem, checks["firewall"].State);
        Assert.Equal(CheckState.Problem, checks["uac"].State);
        Assert.Equal(CheckState.Attention, checks["rdp"].State);
        Assert.Equal(CheckState.Attention, checks["defender"].State); // may be another antivirus: attention, not alarm
    }

    [Fact]
    public void Unreadable_settings_are_unknown_not_good()
    {
        var checks = Health.Security(new FakeRegistry(), _ => null, elevated: false);
        Assert.All(checks, c => Assert.Equal(CheckState.Unknown, c.State));
    }
}

public class StartupTests
{
    private const string Run = @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string Approved = @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    [Fact]
    public void Entries_are_read_with_their_enabled_state()
    {
        var r = new FakeRegistry();
        r.Values[(Run, "Chat")] = @"""C:\Program Files\Chat\chat.exe"" --minimized";
        r.Values[(Run, "Updater")] = @"C:\Tools\up.exe";
        r.Values[(Approved, "Updater")] = new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        r.Values[(Approved, "Chat")] = new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        var items = Health.Startup(r).ToDictionary(i => i.Name);
        Assert.True(items["Chat"].Enabled);
        Assert.False(items["Updater"].Enabled);
        Assert.True(items["Chat"].CanToggle);
    }

    [Fact]
    public void Comparing_with_the_baseline_finds_new_changed_and_removed_entries()
    {
        var current = new List<StartupItem>
        {
            new("HKCU Run", "Old", @"C:\a.exe", true, true),
            new("HKCU Run", "Moved", @"C:\Temp\evil.exe", true, true),
            new("HKCU Run", "Fresh", @"C:\new.exe", true, true),
        };
        var baseline = new Dictionary<string, string>
        {
            ["HKCU Run|Old"] = @"C:\a.exe", ["HKCU Run|Moved"] = @"C:\Good\ok.exe", ["HKCU Run|Gone"] = @"C:\gone.exe",
        };
        var changes = Health.Compare(current, baseline).ToDictionary(c => c.Item.Name);
        Assert.Equal("new", changes["Fresh"].Kind);
        Assert.Equal("changed", changes["Moved"].Kind);
        Assert.Equal(@"C:\Good\ok.exe", changes["Moved"].Was);
        Assert.Equal("removed", changes["Gone"].Kind);
        Assert.DoesNotContain("Old", changes.Keys);
    }

    [Fact]
    public void A_damaged_baseline_key_without_a_separator_is_skipped_instead_of_throwing()
    {
        var current = new List<StartupItem> { new("HKCU Run", "Fresh", @"C:
ew.exe", true, true) };
        var baseline = new Dictionary<string, string> { ["no-separator"] = "x.exe", ["HKCU Run|Gone"] = @"C:\gone.exe" };
        var changes = Health.Compare(current, baseline);
        Assert.Contains(changes, c => c.Kind == "new" && c.Item.Name == "Fresh");
        Assert.Contains(changes, c => c.Kind == "removed" && c.Item.Name == "Gone");
        Assert.Equal(2, changes.Count);
    }

    [Fact]
    public void Suspicious_looking_entries_get_plain_notes_but_normal_ones_do_not()
    {
        Assert.Empty(Health.Notes(new StartupItem("HKCU Run", "X", @"C:\Windows\System32\notepad.exe", true, true)));
        var temp = Health.Notes(new StartupItem("HKCU Run", "X", @"C:\Users\me\AppData\Local\Temp\a.exe", true, true));
        Assert.NotEmpty(temp);
        var script = Health.Notes(new StartupItem("HKCU Run", "X", @"powershell.exe -enc AAAA", true, true));
        Assert.NotEmpty(script);
    }

    [Fact]
    public void The_program_path_is_taken_out_of_quoted_and_unquoted_commands()
    {
        Assert.Equal(@"C:\Program Files\A\a.exe", Health.ExtractPath(@"""C:\Program Files\A\a.exe"" --x"));
        Assert.Equal(@"C:\Tools\b.exe", Health.ExtractPath(@"C:\Tools\b.exe /silent"));
    }
}

public class RedactionTests
{
    [Fact]
    public void Personal_details_are_removed_from_a_report()
    {
        var user = Environment.UserName;
        var pc = Environment.MachineName;
        var text = $"User {user} on {pc} at C:\\Users\\{user}\\Documents\\x.txt, mail me@example.com, ip 192.168.1.20 and 10.0.0.7, " +
                   "mac AA:BB:CC:DD:EE:FF, sid S-1-5-21-1111111111-2222222222-3333333333-1001, v6 fe80:0:0:0:1:2:3:4";
        var clean = Redactor.Redact(text);
        if (user.Length >= 3) Assert.DoesNotContain(user, clean, StringComparison.OrdinalIgnoreCase);
        if (pc.Length >= 3) Assert.DoesNotContain(pc, clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("example.com", clean);
        Assert.DoesNotContain("192.168.1.20", clean);
        Assert.DoesNotContain("10.0.0.7", clean);
        Assert.DoesNotContain("AA:BB:CC", clean);
        Assert.DoesNotContain("S-1-5-21", clean);
        Assert.DoesNotContain("fe80:", clean);
    }

    [Fact]
    public void Extra_names_can_be_redacted_and_ordinary_text_is_kept()
    {
        var clean = Redactor.Redact("Disk C: has 40 GB free. Owner Mahsa.", ["Mahsa"]);
        Assert.Contains("40 GB free", clean);
        Assert.DoesNotContain("Mahsa", clean);
    }

    [Fact]
    public void The_report_has_every_section_and_a_disclaimer()
    {
        var text = HealthReport.Build(
            [new HealthCheck("d", "Disk C:", CheckState.Good, "ok")],
            [new EventSummary("disk", 51, "System", false, 3, DateTime.Now, "meaning", false)],
            [new HealthCheck("f", "Firewall", CheckState.Good, "on")],
            [new StartupItem("HKCU Run", "App", @"C:\a.exe", true, true)], "3.1.0", "Windows", DateTime.Now);
        Assert.Contains("not a security scan", text);
        Assert.Contains("System health", text);
        Assert.Contains("disk #51", text);
        Assert.Contains("Security settings", text);
        Assert.Contains("start with Windows", text);
    }
}

public class HealthLiveTests
{
    [Fact]
    public void The_real_probes_run_without_throwing_on_this_pc()
    {
        var reg = new WindowsRegistryProbe();
        Assert.NotEmpty(Health.DisksNow());
        Assert.NotNull(Health.PendingReboot(reg));
        Assert.NotNull(Health.WindowsUpdate(reg, DateTime.UtcNow));
        Assert.NotEmpty(Health.Security(reg, Health.ServiceStateNow, false));
        Assert.NotNull(Health.Startup(reg, Health.StartupFolders()));
        Assert.NotNull(Health.Services(reg, Health.ServiceStateNow));
        _ = Health.RecentErrors(1, 5);
    }
}
