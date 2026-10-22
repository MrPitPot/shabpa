namespace Shabpa.Core;

/// <summary>Counts, on this PC only and never sent anywhere, how often each page is opened, so the home page can put the
/// most used ones under «اقدام سریع».</summary>
public static class PageUse
{
    public sealed record PageInfo(string Key, string Icon, string Hue, string Fa, string En);

    /// <summary>Pages that can appear as a quick action. Home, settings, updates and the pages that already have a fixed card are left out.</summary>
    public static readonly IReadOnlyList<PageInfo> Known =
    [
        new("system", "cpu", "HueA", "کنترل سیستم", "System control"),
        new("comfort", "sun", "HueC", "نور شب و استراحت", "Night light & rest"),
        new("profiles", "user", "HueB", "پروفایل‌ها", "Profiles"),
        new("playtime", "game", "HueD", "زمان بازی", "Playtime"),
        new("ping", "net", "HueE", "پایداری پینگ", "Ping stability"),
        new("dns", "globe", "HueA", "دی‌ان‌اس", "DNS"),
        new("download", "dl", "HueB", "دانلود زمان‌بندی‌شده", "Scheduled download"),
        new("files", "folder", "HueC", "فایل و پشتیبان", "Files & backup"),
        new("health", "ecg", "HueD", "سلامت و امنیت", "Health & security"),
        new("tasks", "tasks", "HueE", "کارها و تمرکز", "Tasks & focus"),
        new("mic", "mic", "HueA", "میکروفون", "Microphone"),
        new("weather", "cloud", "HueB", "آب و هوا", "Weather"),
        new("support", "life", "HueC", "پشتیبانی", "Support"),
    ];

    /// <summary>Adds one visit and returns true when the count is worth writing to disk (the first visit, then every third).</summary>
    public static bool Record(Dictionary<string, int> visits, string key)
    {
        if (!Known.Any(k => k.Key == key)) return false;
        visits[key] = visits.GetValueOrDefault(key) + 1;
        return visits[key] % 3 == 1;
    }

    /// <summary>The most visited pages, most first, only those visited at least <paramref name="minVisits"/> times.</summary>
    public static List<PageInfo> Top(Dictionary<string, int> visits, int take = 3, int minVisits = 3)
        => Known.Where(k => visits.GetValueOrDefault(k.Key) >= minVisits)
            .OrderByDescending(k => visits.GetValueOrDefault(k.Key)).ThenBy(k => k.Key, StringComparer.Ordinal).Take(take).ToList();
}
