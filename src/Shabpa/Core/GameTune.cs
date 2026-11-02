using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Shabpa.Core;

public enum TuneHive { CurrentUser, LocalMachine }

/// <summary>One registry value a tweak owns. <see cref="On"/> and <see cref="Off"/> are written as "d:&lt;number&gt;" (DWORD) or
/// "s:&lt;text&gt;" (string). An <see cref="On"/> of "m:Key=Value;" is a setting inside a shared list of "Key=Value;" pairs (the
/// way Windows stores the graphics preferences): only that pair is added or removed and the person's other pairs stay. <see cref="Off"/> is what Windows has by default and is used only when no backup of the person's own
/// value exists. A null <see cref="Off"/> means the value is simply absent by default.</summary>
public sealed record TuneEntry(TuneHive Hive, string Path, string Name, string On, string? Off);

public enum TuneKind { Registry, PcieLink, CpuPriority, X3dLock }

/// <summary>A gaming tweak: what it is called, what it does (the text of its info icon), what it touches and what it needs.</summary>
public sealed record Tweak(string Id, string Tab, TuneKind Kind, string NameFa, string NameEn, string TipFa, string TipEn, bool NeedsAdmin, bool NeedsRestart, IReadOnlyList<TuneEntry> Entries)
{
    public string Name => L.T(NameFa, NameEn);
    public string Tip => L.T(TipFa, TipEn);
}

/// <summary>Reads and writes the registry values of the tweaks; faked in tests so nothing on the real PC is touched.</summary>
public interface ITuneRegistry
{
    /// <summary>The value as "d:5" / "s:High", or null when it is not there.</summary>
    string? Read(TuneHive hive, string path, string name);
    /// <summary>Writes (creating the key); throws <see cref="UnauthorizedAccessException"/> without the rights.</summary>
    void Write(TuneHive hive, string path, string name, string encoded);
    void Delete(TuneHive hive, string path, string name);
}

public sealed class WindowsTuneRegistry : ITuneRegistry
{
    private static RegistryKey Root(TuneHive hive) => hive == TuneHive.CurrentUser ? Registry.CurrentUser : Registry.LocalMachine;

    public string? Read(TuneHive hive, string path, string name)
    {
        try
        {
            using var key = Root(hive).OpenSubKey(path);
            var value = key?.GetValue(name);
            return value switch
            {
                null => null,
                int number => "d:" + unchecked((uint)number),
                string text => "s:" + text,
                _ => null,
            };
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    public void Write(TuneHive hive, string path, string name, string encoded)
    {
        using var key = Root(hive).CreateSubKey(path, writable: true) ?? throw new IOException("registry key could not be opened");
        if (encoded.StartsWith("d:", StringComparison.Ordinal))
            key.SetValue(name, unchecked((int)long.Parse(encoded[2..], System.Globalization.CultureInfo.InvariantCulture)), RegistryValueKind.DWord);
        else
            key.SetValue(name, encoded[2..], RegistryValueKind.String);
    }

    public void Delete(TuneHive hive, string path, string name)
    {
        using var key = Root(hive).OpenSubKey(path, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}

/// <summary>The "PCIe link state power management" of the active power plan (how eagerly the PCIe lane to the graphics card goes to sleep).</summary>
public interface IPcieLink
{
    /// <summary>The current plugged-in setting (0 off, 1 moderate, 2 maximum savings), or null when it cannot be read.</summary>
    uint? Read();
    bool Write(uint value);
}

public sealed class WindowsPcieLink : IPcieLink
{
    private static Guid _subPcie = new("501a4d13-42af-4429-9fd1-a8218c268e20"), _aspm = new("ee12f906-d277-404b-b6da-e5fa1a576df5");

    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);

    private static bool Active(out Guid scheme)
    {
        scheme = default;
        if (PowerGetActiveScheme(IntPtr.Zero, out var pointer) != 0 || pointer == IntPtr.Zero) return false;
        try { scheme = Marshal.PtrToStructure<Guid>(pointer); return true; }
        finally { LocalFree(pointer); }
    }

    public uint? Read()
    {
        try
        {
            if (!Active(out var scheme)) return null;
            return PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref _subPcie, ref _aspm, out var value) == 0 ? value : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return null; }
    }

    public bool Write(uint value)
    {
        try
        {
            if (!Active(out var scheme)) return false;
            if (PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref _subPcie, ref _aspm, value) != 0) return false;
            return PowerSetActiveScheme(IntPtr.Zero, ref scheme) == 0; // makes the plan take the new value now
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }
}

public enum TuneOutcome { Done, NeedsAdmin, Failed }

/// <summary>Turns tweaks on and off. Before a value is changed the person's own value is kept (in <paramref name="backup"/>, which the
/// app saves with its data), and turning a tweak off puts exactly that back. Nothing here deletes files or touches anything that is
/// not listed in the tweak.</summary>
public sealed class GameTuner(ITuneRegistry registry, IPcieLink pcie, IDictionary<string, string> backup, Action save)
{
    private const string Absent = "<none>";
    private const string PcieKey = "pcie|ac";

    public bool IsOn(Tweak tweak) => tweak.Kind switch
    {
        TuneKind.Registry => tweak.Entries.All(e => Matches(e, registry.Read(e.Hive, e.Path, e.Name))),
        TuneKind.PcieLink => pcie.Read() == 0,
        _ => false,
    };

    /// <summary>True when turning this tweak off has something of the person's own to put back.</summary>
    public bool HasBackup(Tweak tweak) => tweak.Kind == TuneKind.PcieLink ? backup.ContainsKey(PcieKey) : tweak.Entries.Any(e => backup.ContainsKey(KeyOf(e)));

    public TuneOutcome Apply(Tweak tweak)
    {
        try
        {
            if (tweak.Kind == TuneKind.PcieLink)
            {
                if (pcie.Read() is not { } now) return TuneOutcome.Failed;
                if (!backup.ContainsKey(PcieKey)) backup[PcieKey] = now.ToString(System.Globalization.CultureInfo.InvariantCulture);
                save();
                return pcie.Write(0) ? TuneOutcome.Done : TuneOutcome.Failed;
            }
            if (tweak.Kind != TuneKind.Registry) return TuneOutcome.Failed;
            foreach (var e in tweak.Entries)
            {
                var key = KeyOf(e);
                if (!backup.ContainsKey(key)) backup[key] = registry.Read(e.Hive, e.Path, e.Name) ?? Absent;
                save();                                     // the original is on disk before the first change
                registry.Write(e.Hive, e.Path, e.Name, e.On.StartsWith("m:", StringComparison.Ordinal) ? "s:" + SetPair(PairsOf(registry.Read(e.Hive, e.Path, e.Name)), e.On[2..]) : e.On);
            }
            return TuneOutcome.Done;
        }
        catch (UnauthorizedAccessException) { return TuneOutcome.NeedsAdmin; }
        catch (Exception ex) when (ex is IOException or System.Security.SecurityException or InvalidOperationException)
        {
            Log.Write($"tune {tweak.Id}: {ex.GetType().Name} {ex.Message}");
            return TuneOutcome.Failed;
        }
    }

    public TuneOutcome Revert(Tweak tweak)
    {
        try
        {
            if (tweak.Kind == TuneKind.PcieLink)
            {
                var was = backup.TryGetValue(PcieKey, out var text) && uint.TryParse(text, out var number) ? number : 1u; // 1 = Windows' own default (moderate)
                if (!pcie.Write(was)) return TuneOutcome.Failed;
                backup.Remove(PcieKey);
                save();
                return TuneOutcome.Done;
            }
            if (tweak.Kind != TuneKind.Registry) return TuneOutcome.Failed;
            foreach (var e in tweak.Entries)
            {
                var key = KeyOf(e);
                if (e.On.StartsWith("m:", StringComparison.Ordinal))
                {
                    // a pair the person already had before Shabpa touched the list stays; otherwise only our pair goes and
                    // whatever else is in the list stays, even if it changed since
                    if (backup.TryGetValue(key, out var first) && first != Absent && Matches(e, first)) { backup.Remove(key); continue; }
                    var rest = DropPair(PairsOf(registry.Read(e.Hive, e.Path, e.Name)), e.On[2..]);
                    if (rest.Length == 0) registry.Delete(e.Hive, e.Path, e.Name); else registry.Write(e.Hive, e.Path, e.Name, "s:" + rest);
                    backup.Remove(key);
                    continue;
                }
                var original = backup.TryGetValue(key, out var saved) ? saved : e.Off ?? Absent;
                if (original == Absent) registry.Delete(e.Hive, e.Path, e.Name);
                else registry.Write(e.Hive, e.Path, e.Name, original);
                backup.Remove(key);
            }
            save();
            return TuneOutcome.Done;
        }
        catch (UnauthorizedAccessException) { return TuneOutcome.NeedsAdmin; }
        catch (Exception ex) when (ex is IOException or System.Security.SecurityException or InvalidOperationException)
        {
            Log.Write($"tune revert {tweak.Id}: {ex.GetType().Name} {ex.Message}");
            return TuneOutcome.Failed;
        }
    }

    private static string KeyOf(TuneEntry e) => $"{e.Hive}|{e.Path}|{e.Name}";

    private static bool Matches(TuneEntry e, string? current)
    {
        if (!e.On.StartsWith("m:", StringComparison.Ordinal)) return current == e.On;
        var want = e.On[2..].TrimEnd(';');
        return PairsOf(current).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(want, StringComparer.OrdinalIgnoreCase);
    }

    private static string PairsOf(string? encoded) => encoded is { Length: > 2 } && encoded.StartsWith("s:", StringComparison.Ordinal) ? encoded[2..] : "";

    private static IEnumerable<string> Split(string list) => list.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>The list with this pair set (an old value of the same key is replaced), every pair ending in ";".</summary>
    internal static string SetPair(string list, string pair)
    {
        var key = pair.Split('=')[0].Trim().TrimEnd(';');
        return string.Concat(Split(list).Where(p => !p.Split('=')[0].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)).Append(pair.TrimEnd(';')).Select(p => p + ";"));
    }

    internal static string DropPair(string list, string pair)
    {
        var key = pair.Split('=')[0].Trim().TrimEnd(';');
        return string.Concat(Split(list).Where(p => !p.Split('=')[0].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)).Select(p => p + ";"));
    }
}

/// <summary>The list of tweaks. Every one is a documented Windows setting that can be put back exactly; none removes or disables a program,
/// a service or a file.</summary>
public static class GameTweaks
{
    public const string TabCpu = "cpu", TabSystem = "system", TabGraphics = "graphics";

    private const string Games = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";
    private const string Profile = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    private const string Store = @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications";

    private static TuneEntry U(string path, string name, string on, string? off) => new(TuneHive.CurrentUser, path, name, on, off);
    private static TuneEntry M(string path, string name, string on, string? off) => new(TuneHive.LocalMachine, path, name, on, off);

    /// <summary>Total memory in KB: the value that makes Windows keep its background services together in one host process.</summary>
    internal static long TotalKb() => (long)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024);

    public static IReadOnlyList<Tweak> All(long? totalKb = null) =>
    [
        // ---- processor and memory ----
        new("prio", TabCpu, TuneKind.CpuPriority, "اولویت پردازنده برای بازی", "Game gets the processor first",
            "وقتی بازی اجرا می‌شه، پردازش بازی اولویت «بالا» می‌گیره تا برنامه‌های پس‌زمینه جلوش رو نگیرن. با بسته‌شدن بازی خودکار تموم می‌شه و چیزی روی سیستم نمی‌مونه.",
            "While a game runs, its process gets “High” priority so background programs cannot hold it up. It ends with the game; nothing stays on the system.",
            false, false, []),
        new("x3d", TabCpu, TuneKind.X3dLock, "چسبوندن بازی به هسته‌های کش بزرگ", "Pin the game to the big-cache cores",
            "فقط برای پردازنده‌های دو‌تکه‌ی X3D آمده؛ بازی رو روی تکه‌ای که کش بزرگ داره نگه می‌داره. روی این کامپیوتر چنین پردازنده‌ای پیدا نشد.",
            "Only for dual-chiplet X3D processors: it keeps the game on the chiplet with the big cache. No such processor was found on this PC.",
            false, false, []),
        new("games-profile", TabCpu, TuneKind.Registry, "اولویت زمان‌بندی بازی‌ها", "Game scheduling priority",
            "به برنامه‌ریز چندرسانه‌ای ویندوز می‌گه کارهای دسته‌ی «بازی» رو زودتر و با اولویت بالاتر بچینه. چهار مقدار ویندوز رو عوض می‌کنه و مقدار قبلی‌ات ذخیره می‌شه.",
            "Tells Windows' multimedia scheduler to line up “Games” work earlier and with higher priority. Four Windows values change and your old ones are kept.",
            true, false,
            [M(Games, "GPU Priority", "d:8", "d:8"), M(Games, "Priority", "d:6", "d:2"), M(Games, "Scheduling Category", "s:High", "s:Medium"), M(Games, "SFIO Priority", "s:High", "s:Normal")]),
        new("responsiveness", TabCpu, TuneKind.Registry, "سهم پس‌زمینه از پردازنده", "Background share of the processor",
            "ویندوز پیش‌فرض ۲۰٪ پردازنده رو برای کارهای چندرسانه‌ای و پس‌زمینه کنار می‌ذاره. این تنظیم رو روی کمترین حد ممکن می‌ذاره تا بیشتر نصیب بازی بشه.",
            "Windows keeps 20 % of the processor for multimedia and background work by default. This sets it to the lowest allowed value so more goes to the game.",
            true, false, [M(Profile, "SystemResponsiveness", "d:10", "d:20")]),

        // ---- windows and system ----
        new("gamemode", TabSystem, TuneKind.Registry, "حالت بازی خود ویندوز", "Windows' own Game Mode",
            "حالت بازی ویندوز آپدیت‌ها و کارهای سنگین پس‌زمینه رو موقع بازی عقب می‌اندازه. این گزینه مطمئن می‌شه روشنه.",
            "Windows' Game Mode holds back updates and heavy background work while you play. This makes sure it is on.",
            false, false, [U(@"Software\Microsoft\GameBar", "AutoGameModeEnabled", "d:1", null), U(@"Software\Microsoft\GameBar", "AllowAutoGameMode", "d:1", null)]),
        new("capture", TabSystem, TuneKind.Registry, "خاموش‌کردن ضبط پنهان گیم‌بار", "Stop Game Bar's hidden recording",
            "ضبط پس‌زمینه‌ی گیم‌بار (کلیپ‌های خودکار) همیشه کمی از کارت گرافیک و دیسک مصرف می‌کنه. با خاموش‌کردنش دیگه چیزی ضبط نمی‌شه؛ اگه ازش استفاده می‌کنی روشنش نکن.",
            "Game Bar's background capture (automatic clips) always uses a little graphics power and disk. Turned off, nothing is recorded; leave it alone if you use it.",
            false, false, [U(@"System\GameConfigStore", "GameDVR_Enabled", "d:0", "d:1"), U(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", "d:0", "d:1")]),
        new("store-apps", TabSystem, TuneKind.Registry, "بستن اپ‌های فروشگاه در پس‌زمینه", "Keep Store apps asleep",
            "اپ‌های فروشگاه ویندوز (مثل Phone Link) دیگه توی پس‌زمینه بالا نمی‌مونن. در عوض اعلان و آلارم‌هاشون هم قطع می‌شه.",
            "Windows Store apps (such as Phone Link) no longer stay running in the background. Their notifications and alarms stop too.",
            false, false, [U(Store, "GlobalUserDisabled", "d:1", "d:0")]),
        new("update-share", TabSystem, TuneKind.Registry, "قطع آپلود آپدیت برای دیگران", "Stop sharing updates with others",
            "ویندوز می‌تونه فایل‌های آپدیتش رو برای کامپیوترهای دیگه آپلود کنه و موقع بازی پینگت رو بالا ببره. این تنظیم فقط دانلود مستقیم از مایکروسافت رو نگه می‌داره.",
            "Windows can upload its update files to other PCs, which raises your ping while you play. This keeps only direct downloads from Microsoft.",
            true, false, [M(@"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", "d:0", null)]),
        new("net-throttle", TabSystem, TuneKind.Registry, "برداشتن ترمز شبکه‌ی چندرسانه‌ای", "Lift the multimedia network brake",
            "ویندوز سرعت بسته‌های شبکه رو موقع پخش چندرسانه‌ای محدود می‌کنه. این تنظیم محدودیت رو برمی‌داره؛ اثرش روی هر اینترنتی کوچیکه و بعضی سیستم‌ها هیچ تفاوتی حس نمی‌کنن.",
            "Windows limits the rate of network packets while multimedia plays. This lifts the limit; the effect is small and some systems feel no difference.",
            true, false, [M(Profile, "NetworkThrottlingIndex", "d:4294967295", "d:10")]),
        new("svchost", TabSystem, TuneKind.Registry, "یکی‌کردن پردازه‌های سرویس‌ها", "Merge service processes",
            "ویندوز هر سرویس رو توی یه پردازه‌ی جدا بالا میاره. با این تنظیم تا جا هست سرویس‌ها کنار هم می‌مونن و تعداد پردازه‌ها کم می‌شه. اثرش بعد از ری‌استارت دیده می‌شه؛ اگه یه سرویس کرش کنه چندتای دیگه هم باهاش می‌افتن.",
            "Windows starts every service in its own process. With this, services share one process while memory allows, so there are fewer processes. It shows after a restart; if one service crashes, a few others go with it.",
            true, true, [M(@"SYSTEM\CurrentControlSet\Control", "SvcHostSplitThresholdInKB", "d:" + (totalKb ?? TotalKb()).ToString(System.Globalization.CultureInfo.InvariantCulture), "d:3670016")]),

        // ---- graphics ----
        new("hags", TabGraphics, TuneKind.Registry, "زمان‌بندی سخت‌افزاری گرافیک", "Hardware-scheduled graphics (HAGS)",
            "کارت گرافیک خودش صف کارهای تصویر رو مدیریت می‌کنه و پردازنده سبک‌تر می‌شه؛ روی کارت‌های جدید گاهی تأخیر رو کم می‌کنه. بعد از ری‌استارت اعمال می‌شه و درایور کارت باید پشتیبانی کنه.",
            "The graphics card manages its own queue of work, lightening the processor; on newer cards it can lower delay. It applies after a restart and needs driver support.",
            true, true, [M(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", "d:2", "d:1")]),
        new("pcie", TabGraphics, TuneKind.PcieLink, "بیدار نگه‌داشتن مسیر PCIe", "Keep the PCIe lane awake",
            "ویندوز می‌تونه مسیر ارتباطی کارت گرافیک رو برای صرفه‌جویی به خواب ببره و بعد دوباره بیدارش کنه که گاهی پرش کوتاه ایجاد می‌کنه. این تنظیم صرفه‌جویی رو روی طرح برق فعلی خاموش می‌کنه (مصرف لپ‌تاپ روی برق کمی بالاتر می‌ره).",
            "Windows can put the lane to the graphics card to sleep to save power and wake it again, which can cause tiny hitches. This turns that saving off for the current power plan (a laptop on mains uses slightly more).",
            true, false, []),
        new("windowed", TabGraphics, TuneKind.Registry, "بهینه‌سازی بازی در پنجره و بدون‌حاشیه", "Windowed and borderless game boost",
            "بازی‌هایی که در حالت پنجره یا «بدون‌حاشیه» اجرا می‌شن با روش نمایش جدیدتر ویندوز رسم می‌شن و تأخیرشون کمتر می‌شه. روی بازی‌های تمام‌صفحه‌ی واقعی اثری نداره.",
            "Games running windowed or borderless are drawn with Windows' newer presentation method, which lowers delay. It does nothing for true full-screen games.",
            false, false, [U(@"Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings", "m:SwapEffectUpgradeEnable=1;", null)]),
    ];

    public static Tweak Find(string id) => All().First(t => t.Id == id);
}

/// <summary>Raises the processor priority of a running game for as long as it lives.</summary>
public static class GamePriority
{
    /// <summary>Sets every process called <paramref name="processName"/> to High (or back to Normal). Returns how many were changed.</summary>
    public static int Set(string processName, bool high)
    {
        if (processName.Length == 0 || GameRules.IsNotAGame(processName)) return 0;
        var changed = 0;
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                try
                {
                    process.PriorityClass = high ? ProcessPriorityClass.High : ProcessPriorityClass.Normal;
                    changed++;
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // protected by an anti-cheat or already gone: the game just keeps its priority
                }
            }
        }
        return changed;
    }

    /// <summary>True for a processor that has two chiplets and a big cache on one of them (Ryzen 7900X3D, 7950X3D, 9900X3D, 9950X3D).</summary>
    public static bool IsDualX3d(string cpuName)
    {
        var name = cpuName.ToUpperInvariant();
        return name.Contains("X3D") && (name.Contains("7900") || name.Contains("7950") || name.Contains("9900") || name.Contains("9950"));
    }
}
