namespace Shabpa.Core;

/// <summary>The tabs the person may switch off in Settings. Off means the tab is gone from the list and what it ran in the background is stopped. Home, Settings and Updates
/// always stay (without them there would be no way back).</summary>
public static class OptionalTabs
{
    public static readonly IReadOnlyList<(string Key, string Fa, string En)> All =
    [
        ("system", "کنترل سیستم", "System control"), ("comfort", "نور شب و استراحت", "Night light & rest"), ("profiles", "پروفایل‌ها", "Profiles"),
        ("gaming", "اورلی بازی", "Game overlay"), ("playtime", "زمان بازی", "Playtime"), ("ping", "پایداری پینگ", "Ping stability"), ("dns", "دی‌ان‌اس", "DNS"),
        ("download", "دانلود زمان‌بندی‌شده", "Scheduled download"), ("rgb", "نورپردازی", "Lighting"), ("files", "فایل و پشتیبان", "Files & backup"),
        ("health", "سلامت و امنیت", "Health & security"), ("rewards", "مدال‌ها و مأموریت‌ها", "Medals & quests"), ("tasks", "کارها و تمرکز", "Tasks & focus"),
        ("mic", "میکروفون", "Microphone"), ("weather", "آب و هوا", "Weather"), ("support", "پشتیبانی", "Support"),
    ];

    public static bool Contains(string key) => All.Any(t => t.Key == key);

    /// <summary>Keeps only real, switchable keys, each once (a hand-edited or old settings file cannot hide Home or Settings).</summary>
    public static List<string> Clean(IEnumerable<string>? keys) => [.. (keys ?? []).Where(Contains).Distinct()];
}
