using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Shabpa.Core;

public enum CheckState { Good, Attention, Problem, Unknown }

/// <summary>One line of the health or security overview: what was looked at, what it is, and what to do.</summary>
public sealed record HealthCheck(string Id, string Title, CheckState State, string Detail, string Advice = "");

/// <summary>Reads registry values; replaceable in tests.</summary>
public interface IRegistryProbe
{
    object? Get(string key, string name);
    IReadOnlyList<string> ValueNames(string key);
    bool KeyExists(string key);
}

/// <summary>The real registry. Keys are written as "HKLM\..." or "HKCU\..."; the 64-bit view is read.</summary>
public sealed class WindowsRegistryProbe : IRegistryProbe
{
    private static RegistryKey? Open(string key)
    {
        var slash = key.IndexOf('\\');
        if (slash < 0) return null;
        var hive = key[..slash] switch { "HKLM" => RegistryHive.LocalMachine, "HKCU" => RegistryHive.CurrentUser, _ => (RegistryHive?)null };
        if (hive is null) return null;
        try { return RegistryKey.OpenBaseKey(hive.Value, RegistryView.Registry64).OpenSubKey(key[(slash + 1)..]); }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return null; }
    }

    public object? Get(string key, string name)
    {
        using var k = Open(key);
        return k?.GetValue(name);
    }

    public IReadOnlyList<string> ValueNames(string key)
    {
        using var k = Open(key);
        return k is null ? [] : k.GetValueNames();
    }

    public bool KeyExists(string key)
    {
        using var k = Open(key);
        return k is not null;
    }
}

public sealed record EventSummary(string Provider, int Id, string Log, bool Critical, int Count, DateTime Last, string Meaning, bool UsuallyHarmless);

public sealed record StartupItem(string Source, string Name, string Command, bool Enabled, bool CanToggle)
{
    public string Key => Source + "|" + Name;
}

public sealed record StartupChange(StartupItem Item, string Kind, string? Was);

public static class Health
{
    // ---- disk, updates, reboot ----------------------------------------------------------------------

    public static List<HealthCheck> Disks(IEnumerable<(string name, long free, long total)> drives)
    {
        var list = new List<HealthCheck>();
        foreach (var (name, free, total) in drives)
        {
            if (total <= 0) continue;
            var percent = free * 100.0 / total;
            var state = percent < 5 || free < 5L << 30 ? CheckState.Problem : percent < 12 ? CheckState.Attention : CheckState.Good;
            list.Add(new HealthCheck("disk:" + name, L.T($"فضای درایو {name}", $"Space on drive {name}"), state,
                L.T($"آزاد: {Fa.Bytes(free)} · کل: {Fa.Bytes(total)} · {Fa.N(percent, 0)}٪ خالی", $"{Fa.Bytes(free)} free of {Fa.Bytes(total)} · {percent:0}% free"),
                state == CheckState.Good ? "" : L.T("جای خالی کم شده؛ ویندوز و بازی‌ها کند می‌شن. از «فایل و پشتیبان» ببین فضا کجا رفته.", "Free space is low and Windows and games slow down. See where it went under Files & backup.")));
        }
        return list;
    }

    public static List<HealthCheck> DisksNow() =>
        Disks(DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => (d.Name.TrimEnd('\\'), d.AvailableFreeSpace, d.TotalSize)));

    /// <summary>The rename/delete jobs Windows will really run at the next start. Entries whose source file is already gone are dead
    /// flags (installers and antivirus tools leave them behind, and Windows never clears them), so they do not count.</summary>
    public static int LiveFileRenames(IRegistryProbe reg, Func<string, bool>? exists = null)
    {
        exists ??= p => File.Exists(p) || Directory.Exists(p);
        if (reg.Get(@"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager", "PendingFileRenameOperations") is not string[] raw) return 0;
        var live = 0;
        for (var i = 0; i < raw.Length; i += 2)
        {
            var source = raw[i].Trim();
            if (source.Length == 0) continue;
            source = source.TrimStart('!');
            if (source.StartsWith(@"\??\", StringComparison.Ordinal)) source = source[4..];
            if (exists(source)) live++;
        }
        return live;
    }

    public static HealthCheck PendingReboot(IRegistryProbe reg, Func<string, bool>? exists = null)
    {
        // the two Windows Update / servicing keys are volatile (Windows recreates them empty at every start), so a restart clears them
        var service = reg.KeyExists(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired")
                      || reg.KeyExists(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
        var files = LiveFileRenames(reg, exists);
        return service || files > 0
            ? new HealthCheck("reboot", L.T("راه‌اندازی مجدد", "Restart pending"), CheckState.Attention,
                service ? L.T("ویندوز منتظر یه ری‌استارت برای تموم کردن نصب‌هاست.", "Windows is waiting for a restart to finish installing things.")
                        : L.T($"{Fa.Digits(files.ToString())} فایل منتظر جابه‌جایی یا حذف موقع ری‌استارت هستن.", $"{files} file(s) are waiting to be moved or deleted at the next restart."),
                L.T("وقتی کارت تموم شد یه بار ری‌استارت کن. (اگه بعد از ری‌استارت هم موند، با «نادیده گرفتن» کنارش بذار.)", "Restart once when you are done with your work. (If it stays after a restart, use Ignore.)"))
            : new HealthCheck("reboot", L.T("راه‌اندازی مجدد", "Restart pending"), CheckState.Good, L.T("لازم نیست.", "Not needed."));
    }

    public static HealthCheck WindowsUpdate(IRegistryProbe reg, DateTime nowUtc)
    {
        var raw = reg.Get(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\Results\Install", "LastSuccessTime") as string;
        if (!DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var last))
            return new HealthCheck("update", L.T("به‌روزرسانی ویندوز", "Windows Update"), CheckState.Unknown,
                L.T("زمان آخرین نصب موفق خونده نشد.", "The time of the last successful install could not be read."),
                L.T("از تنظیمات ویندوز، بخش Windows Update رو ببین.", "Check Windows Update in Windows Settings."));
        var days = (nowUtc - last).TotalDays;
        var state = days > 90 ? CheckState.Problem : days > 35 ? CheckState.Attention : CheckState.Good;
        return new HealthCheck("update", L.T("به‌روزرسانی ویندوز", "Windows Update"), state,
            L.T($"آخرین نصب موفق: {Fa.Digits(((int)days).ToString())} روز پیش", $"Last successful install: {(int)days} day(s) ago"),
            state == CheckState.Good ? "" : L.T("خیلی وقته به‌روزرسانی ویندوز نصب نشده؛ از تنظیمات ویندوز چک کن.", "Windows has not installed updates for a long time; check in Windows Settings."));
    }

    // ---- services -------------------------------------------------------------------------------------

    public static readonly (string name, string fa, string en)[] KeyServices =
    [
        ("wuauserv", "Windows Update", "Windows Update"), ("WinDefend", "Microsoft Defender", "Microsoft Defender"), ("mpssvc", "فایروال ویندوز", "Windows Firewall"),
        ("EventLog", "ثبت رویدادها", "Event Log"), ("Audiosrv", "صدا", "Windows Audio"), ("Dnscache", "کش DNS", "DNS Client"),
        ("BITS", "انتقال هوشمند (BITS)", "Background Intelligent Transfer"), ("W32Time", "ساعت ویندوز", "Windows Time"), ("Winmgmt", "WMI", "WMI"),
    ];

    public static List<HealthCheck> Services(IRegistryProbe reg, Func<string, string?> state)
    {
        var list = new List<HealthCheck>();
        foreach (var (name, fa, en) in KeyServices)
        {
            var start = reg.Get(@"HKLM\SYSTEM\CurrentControlSet\Services\" + name, "Start") is int s ? s : -1;
            if (start < 0) continue; // not installed on this edition
            var running = state(name);
            var title = L.T(fa, en);
            if (start == 4)
            {
                list.Add(new HealthCheck("svc:" + name, title, CheckState.Attention, L.T("غیرفعال شده.", "Turned off."),
                    L.T("اگه خودت خاموشش نکردی، چک کن چرا.", "If you did not turn it off yourself, find out why.")));
            }
            else if (running is null)
            {
                list.Add(new HealthCheck("svc:" + name, title, CheckState.Unknown, L.T("وضعیتش خونده نشد.", "Its state could not be read.")));
            }
            else if (running == "RUNNING")
            {
                list.Add(new HealthCheck("svc:" + name, title, CheckState.Good, L.T("در حال اجراست.", "Running.")));
            }
            else if (start == 3 || name is "wuauserv" or "BITS" or "W32Time")
            {
                // demand-start services are normally stopped until needed
                list.Add(new HealthCheck("svc:" + name, title, CheckState.Good, L.T("متوقف (فقط وقتی لازم باشه اجرا می‌شه).", "Stopped (starts only when needed).")));
            }
            else
            {
                list.Add(new HealthCheck("svc:" + name, title, CheckState.Problem, L.T("باید اجرا باشه ولی نیست.", "Should be running but is not."),
                    L.T("ری‌استارت معمولاً درستش می‌کنه.", "A restart usually fixes it.")));
            }
        }
        return list;
    }

    /// <summary>"RUNNING", "STOPPED", … from the service control manager, or null if it cannot be read.</summary>
    public static string? ServiceStateNow(string name)
    {
        // only plain service names reach the command line
        if (!Regex.IsMatch(name, @"^[A-Za-z0-9_.\-]{1,64}$")) return null;
        try
        {
            using var p = new System.Diagnostics.Process();
            p.StartInfo = new System.Diagnostics.ProcessStartInfo("sc.exe", $"query {name}") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            p.Start();
            var text = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            var m = Regex.Match(text, @"STATE\s*:\s*\d+\s+(\w+)");
            return m.Success ? m.Groups[1].Value : null;
        }
        catch (Exception ex)
        {
            Log.Write($"service state: {ex.Message}");
            return null;
        }
    }

    // ---- event log ------------------------------------------------------------------------------------

    private static readonly (string provider, int id, string fa, string en, bool harmless)[] KnownEvents =
    [
        ("Microsoft-Windows-Kernel-Power", 41, "کامپیوتر بدون خاموش شدن درست، ری‌استارت شد (قطع برق، هنگ یا خاموش کردن اجباری).", "The PC restarted without a clean shutdown (power loss, a freeze or a forced power-off).", false),
        ("EventLog", 6008, "خاموش شدن غیرمنتظره‌ی قبلی ثبت شده.", "An earlier unexpected shutdown was recorded.", false),
        ("BugCheck", 1001, "صفحه‌ی آبی (BSOD) رخ داده.", "A blue screen (BSOD) happened.", false),
        ("Microsoft-Windows-WHEA-Logger", 17, "خطای سخت‌افزاری گزارش شده (حافظه، پردازنده یا PCIe).", "A hardware error was reported (memory, CPU or PCIe).", false),
        ("Microsoft-Windows-WHEA-Logger", 18, "خطای سخت‌افزاری جدی گزارش شده.", "A serious hardware error was reported.", false),
        ("Microsoft-Windows-WHEA-Logger", 19, "خطای سخت‌افزاری قابل اصلاح گزارش شده.", "A correctable hardware error was reported.", false),
        ("disk", 7, "دیسک بلاک خراب گزارش کرده.", "The disk reported a bad block.", false),
        ("disk", 11, "کنترلر دیسک خطا داده؛ کابل یا خود دیسک رو چک کن.", "The disk controller reported an error; check the cable or the disk.", false),
        ("disk", 51, "موقع نوشتن یا خوندن از دیسک خطا رخ داده.", "An error happened while reading from or writing to a disk.", false),
        ("Ntfs", 55, "ساختار فایل‌سیستم آسیب دیده؛ اجرای chkdsk کمک می‌کنه.", "The file system structure is damaged; running chkdsk helps.", false),
        ("Display", 4101, "درایور کارت گرافیک هنگ کرد و دوباره راه افتاد.", "The graphics driver stopped responding and recovered.", false),
        ("Application Error", 1000, "یه برنامه کرش کرده.", "An application crashed.", true),
        ("Application Hang", 1002, "یه برنامه هنگ کرده.", "An application stopped responding.", true),
        ("Microsoft-Windows-DistributedCOM", 10016, "مجوز یه جزء ویندوز؛ تقریباً همیشه بی‌خطره و می‌شه نادیده گرفت.", "A Windows component permission message; almost always harmless.", true),
        ("Service Control Manager", 7000, "یه سرویس راه نیفتاد.", "A service failed to start.", false),
        ("Service Control Manager", 7001, "یه سرویس به‌خاطر وابستگی راه نیفتاد.", "A service failed because of a dependency.", false),
        ("Service Control Manager", 7009, "یه سرویس دیر جواب داد.", "A service took too long to respond.", true),
        ("Service Control Manager", 7023, "یه سرویس با خطا متوقف شد.", "A service stopped with an error.", false),
        ("Service Control Manager", 7031, "یه سرویس غیرمنتظره متوقف شد.", "A service stopped unexpectedly.", false),
        ("Service Control Manager", 7034, "یه سرویس غیرمنتظره متوقف شد.", "A service terminated unexpectedly.", false),
        ("Microsoft-Windows-WindowsUpdateClient", 20, "نصب یه به‌روزرسانی ناموفق بود.", "Installing an update failed.", false),
        ("volmgr", 161, "ساخت فایل dump انجام نشد.", "A crash dump could not be created.", true),
    ];

    public static (string meaning, bool harmless) Explain(string provider, int id)
    {
        foreach (var k in KnownEvents)
            if (k.id == id && provider.Equals(k.provider, StringComparison.OrdinalIgnoreCase)) return (L.T(k.fa, k.en), k.harmless);
        return (L.T("رویداد خطا؛ توضیح ساده‌ای برای این کد ندارم.", "An error event; I have no plain-language note for this code."), false);
    }

    /// <summary>Errors and critical events of the last days, grouped by source and code, most frequent first.</summary>
    public static List<EventSummary> RecentErrors(int days = 7, int top = 25)
    {
        var groups = new Dictionary<(string, int, string), (int count, DateTime last, bool critical)>();
        var from = DateTime.UtcNow.AddDays(-days);
        foreach (var log in new[] { "System", "Application" })
        {
            try
            {
                var query = new EventLogQuery(log, PathType.LogName,
                    $"*[System[(Level=1 or Level=2) and TimeCreated[@SystemTime >= '{from:yyyy-MM-ddTHH:mm:ss.000Z}']]]");
                using var reader = new EventLogReader(query);
                while (reader.ReadEvent() is { } record)
                {
                    using (record)
                    {
                        var key = (record.ProviderName ?? "?", record.Id, log);
                        var time = record.TimeCreated ?? DateTime.MinValue;
                        var critical = record.Level == 1;
                        groups[key] = groups.TryGetValue(key, out var g)
                            ? (g.count + 1, time > g.last ? time : g.last, g.critical || critical)
                            : (1, time, critical);
                    }
                }
            }
            catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException or InvalidOperationException)
            {
                Log.Write($"event log {log}: {ex.GetType().Name}");
            }
        }
        return [.. groups
            .OrderByDescending(g => g.Value.critical).ThenByDescending(g => g.Value.count)
            .Take(top)
            .Select(g =>
            {
                var (meaning, harmless) = Explain(g.Key.Item1, g.Key.Item2);
                return new EventSummary(g.Key.Item1, g.Key.Item2, g.Key.Item3, g.Value.critical, g.Value.count, g.Value.last, meaning, harmless);
            })];
    }

    // ---- security ---------------------------------------------------------------------------------------

    /// <summary>One antivirus product Windows Security Center knows about (Defender or a third-party one).</summary>
    public sealed record AvProduct(string Name, bool Enabled, bool UpToDate);

    /// <summary>Decodes the packed <c>productState</c> of root\SecurityCenter2: bits 12-15 tell whether the engine is on, bits 4-7 whether signatures are current.</summary>
    public static AvProduct DecodeAv(string name, uint productState) => new(name, ((productState >> 12) & 0xF) == 1, (productState & 0xF0) == 0);

    // Antivirus programs that do not (or not always) register with Security Center, found by the name of their running programs.
    private static readonly (string process, string name)[] KnownAv =
    [
        ("sheed", "Sheed"), ("padvish", "Padvish"), ("kaspersky", "Kaspersky"), ("avp", "Kaspersky"), ("ekrn", "ESET"), ("egui", "ESET"),
        ("avastsvc", "Avast"), ("avgsvc", "AVG"), ("bdagent", "Bitdefender"), ("vsserv", "Bitdefender"), ("nortonsecurity", "Norton"), ("nsc", "Norton"),
        ("mcshield", "McAfee"), ("mbamservice", "Malwarebytes"), ("avguard", "Avira"), ("avira", "Avira"), ("coreserviceshell", "Trend Micro"),
        ("sophosfs", "Sophos"), ("fsorsp", "F-Secure"), ("psanhost", "Panda"), ("quickheal", "Quick Heal"), ("360tray", "360 Total Security"),
    ];

    /// <summary>Third-party antivirus products among the running program names (without .exe).</summary>
    public static List<AvProduct> DetectByProcess(IEnumerable<string> processNames)
    {
        var found = new List<AvProduct>();
        foreach (var name in processNames)
            // short names must match the whole program name ("avp" must not match "navpane")
            foreach (var (needle, product) in KnownAv)
                if ((needle.Length <= 4 ? name.Equals(needle, StringComparison.OrdinalIgnoreCase) : name.Contains(needle, StringComparison.OrdinalIgnoreCase)) && found.All(f => f.Name != product))
                    found.Add(new AvProduct(product, true, true));
        return found;
    }

    /// <summary>Every antivirus registered with Windows Security Center plus well-known ones that are running without registering
    /// (null if nothing could be read).</summary>
    public static List<AvProduct>? AntivirusNow()
    {
        // one look at a time and at most every 20 seconds: the page asks again on every switch to its tab
        lock (AvGate)
        {
            if (_avAt != default && DateTime.UtcNow - _avAt < TimeSpan.FromSeconds(20)) return _avCache?.ToList();
            _avCache = AntivirusUncached();
            _avAt = DateTime.UtcNow;
            return _avCache?.ToList();
        }
    }

    private static readonly object AvGate = new();
    private static List<AvProduct>? _avCache;
    private static DateTime _avAt;

    private static List<AvProduct>? AntivirusUncached()
    {
        var list = AntivirusFromSecurityCenter();
        try
        {
            var extra = DetectByProcess(System.Diagnostics.Process.GetProcesses().Select(p => { using (p) return p.ProcessName; }));
            if (extra.Count > 0)
            {
                list ??= [];
                list.AddRange(extra.Where(e => !list.Any(l => l.Enabled && l.Name.Contains(e.Name, StringComparison.OrdinalIgnoreCase))));
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Write($"antivirus processes: {ex.GetType().Name}");
        }
        return list;
    }

    private static List<AvProduct>? AntivirusFromSecurityCenter()
    {
        try
        {
            var type = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
            if (type is null) return null;
            dynamic locator = Activator.CreateInstance(type)!;
            dynamic svc = locator.ConnectServer(".", @"root\SecurityCenter2");
            var list = new List<AvProduct>();
            foreach (dynamic o in svc.ExecQuery("SELECT displayName, productState FROM AntiVirusProduct"))
                list.Add(DecodeAv((string)o.displayName, unchecked((uint)(int)o.productState)));
            return list;
        }
        catch (Exception ex)
        {
            Log.Write($"security center: {ex.GetType().Name} {ex.Message}");
            return null;
        }
    }

    private static bool IsDefenderName(string name) => name.Contains("Defender", StringComparison.OrdinalIgnoreCase);

    public static List<HealthCheck> Security(IRegistryProbe reg, Func<string, string?> service, bool elevated, IReadOnlyList<AvProduct>? antivirus = null)
    {
        var list = new List<HealthCheck>();
        HealthCheck Item(string id, string fa, string en, CheckState s, string dFa, string dEn, string aFa = "", string aEn = "") =>
            new(id, L.T(fa, en), s, L.T(dFa, dEn), L.T(aFa, aEn));

        // firewall: all three profiles
        var profiles = new[] { "DomainProfile", "StandardProfile", "PublicProfile" };
        var values = profiles.Select(p => reg.Get(@"HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\" + p, "EnableFirewall")).ToList();
        if (values.All(v => v is null))
            list.Add(Item("firewall", "فایروال ویندوز", "Windows Firewall", CheckState.Unknown, "وضعیتش خونده نشد.", "Its state could not be read."));
        else if (values.Any(v => v is int i && i == 0))
            list.Add(Item("firewall", "فایروال ویندوز", "Windows Firewall", CheckState.Problem, "برای یکی از شبکه‌ها خاموشه.", "It is off for at least one network type.",
                "اگه فایروال دیگه‌ای نصب نکردی، از «Windows Security» روشنش کن.", "Unless you use another firewall, turn it on in Windows Security."));
        else
            list.Add(Item("firewall", "فایروال ویندوز", "Windows Firewall", CheckState.Good, "برای همه‌ی شبکه‌ها روشنه.", "On for all network types."));

        // antivirus: whatever Security Center lists, so a third-party product (not only Defender) counts
        var other = antivirus?.Where(a => !IsDefenderName(a.Name) && a.Enabled).ToList() ?? [];
        if (antivirus is not null)
        {
            var active = antivirus.Where(a => a.Enabled).ToList();
            if (active.Count > 0)
            {
                var names = string.Join("، ", active.Select(a => a.Name));
                var namesEn = string.Join(", ", active.Select(a => a.Name));
                list.Add(active.All(a => a.UpToDate)
                    ? Item("antivirus", "آنتی‌ویروس", "Antivirus", CheckState.Good, $"{names} روشنه و به‌روزه.", $"{namesEn} is on and up to date.")
                    : Item("antivirus", "آنتی‌ویروس", "Antivirus", CheckState.Attention, $"{names} روشنه ولی تعریف‌های ویروسش قدیمیه.", $"{namesEn} is on but its signatures are out of date.",
                        "از خود آنتی‌ویروس به‌روزرسانی رو بزن.", "Update it from the antivirus itself."));
            }
            else if (antivirus.Count > 0)
                list.Add(Item("antivirus", "آنتی‌ویروس", "Antivirus", CheckState.Problem, "آنتی‌ویروس نصبه ولی خاموشه.", "An antivirus is installed but turned off.",
                    "از خود آنتی‌ویروس یا Windows Security روشنش کن.", "Turn it on in the antivirus or in Windows Security."));
            else
                list.Add(Item("antivirus", "آنتی‌ویروس", "Antivirus", CheckState.Problem, "هیچ آنتی‌ویروسی ثبت نشده.", "No antivirus is registered.",
                    "Microsoft Defender رو از Windows Security روشن کن یا یه آنتی‌ویروس نصب کن.", "Turn on Microsoft Defender in Windows Security or install an antivirus."));
        }

        // Defender real-time protection
        var rtp = reg.Get(@"HKLM\SOFTWARE\Microsoft\Windows Defender\Real-Time Protection", "DisableRealtimeMonitoring");
        var defender = service("WinDefend");
        if (rtp is int r && r == 1 && other.Count > 0)
            list.Add(Item("defender", "محافظت لحظه‌ای Defender", "Defender real-time protection", CheckState.Good,
                $"خاموشه چون {other[0].Name} کار محافظت رو انجام می‌ده.", $"It is off because {other[0].Name} does the protecting."));
        else if (rtp is int r1 && r1 == 1)
            list.Add(Item("defender", "محافظت لحظه‌ای Defender", "Defender real-time protection", CheckState.Attention,
                "خاموشه. اگه آنتی‌ویروس دیگه‌ای داری طبیعیه.", "It is off. That is normal if you use another antivirus.",
                "اگه آنتی‌ویروس دیگه‌ای نداری، از Windows Security روشنش کن.", "If you have no other antivirus, turn it on in Windows Security."));
        else if (defender == "RUNNING" || rtp is int r2 && r2 == 0)
            list.Add(Item("defender", "محافظت لحظه‌ای Defender", "Defender real-time protection", CheckState.Good, "روشنه.", "On."));
        else if (other.Count > 0)
            list.Add(Item("defender", "محافظت لحظه‌ای Defender", "Defender real-time protection", CheckState.Good,
                $"استفاده نمی‌شه؛ {other[0].Name} جاش رو گرفته.", $"Not in use; {other[0].Name} has taken over."));
        else
            list.Add(Item("defender", "محافظت لحظه‌ای Defender", "Defender real-time protection", CheckState.Unknown, "وضعیتش مشخص نشد.", "Its state is unclear."));

        // User Account Control
        list.Add(reg.Get(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA") is int uac
            ? uac == 0
                ? Item("uac", "کنترل حساب کاربری (UAC)", "User Account Control (UAC)", CheckState.Problem, "خاموشه.", "It is off.", "برنامه‌ها بدون پرسیدن به دسترسی ادمین می‌رسن؛ روشنش کن.", "Programs get admin rights without asking; turn it on.")
                : Item("uac", "کنترل حساب کاربری (UAC)", "User Account Control (UAC)", CheckState.Good, "روشنه.", "On.")
            : Item("uac", "کنترل حساب کاربری (UAC)", "User Account Control (UAC)", CheckState.Unknown, "خونده نشد.", "Could not be read."));

        // SmartScreen
        var smart = reg.Get(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "SmartScreenEnabled") as string;
        list.Add(smart is null
            ? Item("smartscreen", "SmartScreen", "SmartScreen", CheckState.Unknown, "خونده نشد.", "Could not be read.")
            : smart.Equals("Off", StringComparison.OrdinalIgnoreCase)
                ? Item("smartscreen", "SmartScreen", "SmartScreen", CheckState.Attention, "خاموشه.", "It is off.", "هشدار برنامه‌های ناشناخته رو نمی‌گیری.", "You will not get warnings about unknown programs.")
                : Item("smartscreen", "SmartScreen", "SmartScreen", CheckState.Good, "روشنه.", "On."));

        // Remote Desktop
        var rdp = reg.Get(@"HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server", "fDenyTSConnections");
        list.Add(rdp is int d
            ? d == 0
                ? Item("rdp", "دسترسی از راه دور (Remote Desktop)", "Remote Desktop", CheckState.Attention, "روشنه؛ از شبکه می‌شه به این سیستم وصل شد.", "It is on; this PC accepts connections from the network.",
                    "اگه لازمش نداری خاموشش کن.", "Turn it off if you do not need it.")
                : Item("rdp", "دسترسی از راه دور (Remote Desktop)", "Remote Desktop", CheckState.Good, "خاموشه.", "Off.")
            : Item("rdp", "دسترسی از راه دور (Remote Desktop)", "Remote Desktop", CheckState.Unknown, "خونده نشد.", "Could not be read."));

        // Secure Boot
        var sb = reg.Get(@"HKLM\SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled");
        list.Add(sb is int b
            ? b == 1
                ? Item("secureboot", "بوت امن (Secure Boot)", "Secure Boot", CheckState.Good, "روشنه.", "On.")
                : Item("secureboot", "بوت امن (Secure Boot)", "Secure Boot", CheckState.Attention, "خاموشه.", "Off.", "ویندوز ۱۱ و بعضی بازی‌ها به اون نیاز دارن؛ از BIOS روشنش کن.", "Windows 11 and some games need it; turn it on in the BIOS.")
            : Item("secureboot", "بوت امن (Secure Boot)", "Secure Boot", CheckState.Unknown, "این سیستم UEFI نیست یا خونده نشد.", "Not a UEFI system, or it could not be read."));

        _ = elevated;
        return list;
    }

    // ---- startup items ----------------------------------------------------------------------------------

    private static readonly (string source, string key, string approved)[] RunKeys =
    [
        ("HKCU Run", @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run", @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"),
        ("HKLM Run", @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run", @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"),
        ("HKLM Run (32-bit)", @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32"),
    ];

    /// <summary>The programs that start with Windows: the Run keys and the Startup folders.</summary>
    public static List<StartupItem> Startup(IRegistryProbe reg, IEnumerable<string>? startupFolders = null)
    {
        var list = new List<StartupItem>();
        foreach (var (source, key, approved) in RunKeys)
        {
            foreach (var name in reg.ValueNames(key))
            {
                if (name.Length == 0) continue;
                var command = reg.Get(key, name)?.ToString() ?? "";
                var enabled = true;
                if (reg.Get(approved, name) is byte[] flags && flags.Length > 0) enabled = (flags[0] & 1) == 0; // 02/06 enabled; 03/07 disabled
                list.Add(new StartupItem(source, name, command, enabled, CanToggle: source == "HKCU Run"));
            }
        }
        foreach (var folder in startupFolders ?? [])
        {
            try
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var file in Directory.EnumerateFiles(folder).Where(f => !f.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase)))
                    list.Add(new StartupItem("Startup folder", Path.GetFileName(file), file, true, false));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* skip an unreadable folder */ }
        }
        return list;
    }

    public static List<string> StartupFolders() =>
    [
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
    ];

    /// <summary>What is new, changed or gone since <paramref name="baseline"/> (key to command).</summary>
    public static List<StartupChange> Compare(IReadOnlyList<StartupItem> current, IReadOnlyDictionary<string, string> baseline)
    {
        var changes = new List<StartupChange>();
        foreach (var item in current)
        {
            if (!baseline.TryGetValue(item.Key, out var was)) changes.Add(new StartupChange(item, "new", null));
            else if (!string.Equals(was, item.Command, StringComparison.OrdinalIgnoreCase)) changes.Add(new StartupChange(item, "changed", was));
        }
        var now = current.Select(i => i.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var (key, command) in baseline.Where(b => !now.Contains(b.Key)))
        {
            var bar = key.IndexOf('|');
            if (bar < 0) continue; // a hand-edited or damaged baseline entry must not stop the whole check
            changes.Add(new StartupChange(new StartupItem(key[..bar], key[(bar + 1)..], command, false, false), "removed", command));
        }
        return changes;
    }

    private static readonly string[] ScriptHosts = ["powershell", "pwsh", "cmd.exe", "wscript", "cscript", "mshta", "rundll32", "regsvr32"];

    /// <summary>Plain-language reasons a startup entry may be worth a look. Not a malware verdict.</summary>
    public static List<string> Notes(StartupItem item)
    {
        var notes = new List<string>();
        var lower = item.Command.ToLowerInvariant();
        if (lower.Contains(@"\temp\") || lower.Contains(@"\appdata\local\temp") || lower.Contains(@"\downloads\") || lower.Contains(@"\users\public\"))
            notes.Add(L.T("از یه پوشه‌ی موقت یا دانلود اجرا می‌شه؛ برنامه‌های معمولی از اونجا شروع نمی‌شن.", "It runs from a temporary or downloads folder; normal programs do not start from there."));
        if (ScriptHosts.Any(h => lower.Contains(h)))
            notes.Add(L.T("با یه مفسر اسکریپت اجرا می‌شه.", "It runs through a script interpreter."));
        var exe = ExtractPath(item.Command);
        if (exe is not null && Path.IsPathRooted(exe) && !File.Exists(exe) && !Directory.Exists(exe))
            notes.Add(L.T("فایلش دیگه وجود نداره (مدخل بیهوده).", "Its file no longer exists (a leftover entry)."));
        return notes;
    }

    internal static string? ExtractPath(string command)
    {
        var t = command.Trim();
        if (t.Length == 0) return null;
        if (t[0] == '"')
        {
            var end = t.IndexOf('"', 1);
            return end > 1 ? t[1..end] : t[1..];
        }
        var m = Regex.Match(t, @"^[A-Za-z]:\\.*?\.(exe|bat|cmd|com|lnk|ps1|vbs)", RegexOptions.IgnoreCase);
        return m.Success ? m.Value : t.Split(' ')[0];
    }

    /// <summary>Turns a current-user startup entry off or on the same way Task Manager does (the entry is kept, so it can
    /// be turned back on). Returns an error message or null.</summary>
    public static string? SetStartupEnabled(string name, bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
            var data = new byte[12];
            data[0] = enabled ? (byte)0x02 : (byte)0x03;
            if (!enabled) BitConverter.GetBytes(DateTime.Now.ToFileTime()).CopyTo(data, 4);
            key.SetValue(name, data, RegistryValueKind.Binary);
            return null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return ex.Message;
        }
    }
}

/// <summary>The plain-text report the person can read, redact, copy or save. Built from what the Health and Security
/// pages show; nothing is sent anywhere.</summary>
public static class HealthReport
{
    public static string Build(IReadOnlyList<HealthCheck> health, IReadOnlyList<EventSummary> events, IReadOnlyList<HealthCheck> security,
        IReadOnlyList<StartupItem> startup, string appVersion, string windowsVersion, DateTime now)
    {
        var sb = new System.Text.StringBuilder();
        string Mark(CheckState s) => s switch { CheckState.Good => "[ok]       ", CheckState.Attention => "[attention]", CheckState.Problem => "[problem]  ", _ => "[unknown]  " };
        sb.AppendLine($"Shabpa health report - {now:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"Shabpa {appVersion} | {windowsVersion} | {(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")}");
        sb.AppendLine("This is a status overview, not a security scan or a guarantee that anything is fixed.");
        sb.AppendLine();
        sb.AppendLine("== System health ==");
        foreach (var c in health) sb.AppendLine($"{Mark(c.State)} {c.Title}: {c.Detail}" + (c.Advice.Length > 0 ? $" -> {c.Advice}" : ""));
        sb.AppendLine();
        sb.AppendLine("== Errors in the last 7 days (grouped) ==");
        if (events.Count == 0) sb.AppendLine("none found");
        foreach (var e in events)
            sb.AppendLine($"{(e.Critical ? "critical" : "error   ")} x{e.Count,-4} {e.Provider} #{e.Id} ({e.Log}) last {e.Last:yyyy-MM-dd HH:mm}{(e.UsuallyHarmless ? " [usually harmless]" : "")} - {e.Meaning}");
        sb.AppendLine();
        sb.AppendLine("== Security settings ==");
        foreach (var c in security) sb.AppendLine($"{Mark(c.State)} {c.Title}: {c.Detail}" + (c.Advice.Length > 0 ? $" -> {c.Advice}" : ""));
        sb.AppendLine();
        sb.AppendLine("== Programs that start with Windows ==");
        foreach (var i in startup) sb.AppendLine($"{(i.Enabled ? "on " : "off")} [{i.Source}] {i.Name} = {i.Command}");
        return sb.ToString();
    }
}

/// <summary>Removes personal details from text before it is shown for review or shared. It cannot be perfect, which
/// is why the person always reads the result first.</summary>
public static class Redactor
{
    public static string Redact(string text, IEnumerable<string>? extra = null)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var result = text;
        // names that identify this PC and person, longest first so a long name is not half-replaced
        var names = new List<string> { Environment.UserName, Environment.MachineName };
        if (extra is not null) names.AddRange(extra);
        foreach (var n in names.Where(n => n.Length >= 3).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(n => n.Length))
            result = Regex.Replace(result, Regex.Escape(n), "<redacted>", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"[\w.+\-]+@[\w\-]+(\.[\w\-]+)+", "<email>");
        result = Regex.Replace(result, @"\bS-1-\d+(-\d+){2,}\b", "<sid>");
        result = Regex.Replace(result, @"\b([0-9A-Fa-f]{2}[:\-]){5}[0-9A-Fa-f]{2}\b", "<mac>");
        result = Regex.Replace(result, @"\b(?:\d{1,3}\.){3}\d{1,3}\b", "<ip>");
        result = Regex.Replace(result, @"\b(?:[0-9A-Fa-f]{1,4}:){2,7}[0-9A-Fa-f]{1,4}\b", "<ip6>");
        result = Regex.Replace(result, @"(?i)(C:\\Users\\)[^\\\s""']+", "$1<user>");
        return result;
    }
}
