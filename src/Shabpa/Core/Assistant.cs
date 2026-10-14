using System.Text;
using System.Text.RegularExpressions;

namespace Shabpa.Core;

public enum AssistantKind { Unknown, Schedule, Rule, Cancel, Status, FreeRam }

/// <summary>What a typed sentence was understood as. Nothing is executed from here; the caller confirms first.</summary>
public sealed record AssistantResult(AssistantKind Kind, PowerAction Action = PowerAction.Shutdown, DateTime? Due = null,
    IReadOnlyList<DayOfWeek>? Repeat = null, RuleKind? Rule = null, int Minutes = 0)
{
    public static readonly AssistantResult None = new(AssistantKind.Unknown);

    public bool Understood => Kind != AssistantKind.Unknown;
}

/// <summary>Understands short Persian or English sentences such as «یک ساعت و نیم دیگه خاموش کن» or
/// "sleep at 1:30 am every night". Pure rules, no network and no model: it only recognises what it can
/// explain back to the user.</summary>
public static class Assistant
{
    private static readonly Dictionary<string, int> Numbers = new()
    {
        ["صفر"] = 0, ["یک"] = 1, ["یه"] = 1, ["دو"] = 2, ["سه"] = 3, ["چهار"] = 4, ["پنج"] = 5, ["شش"] = 6, ["شیش"] = 6, ["هفت"] = 7, ["هشت"] = 8,
        ["نه"] = 9, ["ده"] = 10, ["یازده"] = 11, ["دوازده"] = 12, ["سیزده"] = 13, ["چهارده"] = 14, ["پانزده"] = 15, ["شانزده"] = 16, ["هفده"] = 17,
        ["هجده"] = 18, ["نوزده"] = 19, ["بیست"] = 20, ["سی"] = 30, ["چهل"] = 40, ["پنجاه"] = 50, ["شصت"] = 60,
        ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10,
        ["fifteen"] = 15, ["twenty"] = 20, ["thirty"] = 30, ["forty"] = 40, ["fifty"] = 50, ["sixty"] = 60,
    };

    private static readonly (string[] words, DayOfWeek day)[] Days =
    [
        (["شنبه", "saturday", "sat"], DayOfWeek.Saturday),
        (["یکشنبه", "یک‌شنبه", "sunday", "sun"], DayOfWeek.Sunday),
        (["دوشنبه", "دو‌شنبه", "monday", "mon"], DayOfWeek.Monday),
        (["سهشنبه", "سه‌شنبه", "tuesday", "tue"], DayOfWeek.Tuesday),
        (["چهارشنبه", "چهار‌شنبه", "wednesday", "wed"], DayOfWeek.Wednesday),
        (["پنجشنبه", "پنج‌شنبه", "thursday", "thu"], DayOfWeek.Thursday),
        (["جمعه", "friday", "fri"], DayOfWeek.Friday),
    ];

    /// <summary>Persian and Arabic-Indic digits to Latin, ZWNJ and punctuation to spaces, everything lower case.</summary>
    internal static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text.Trim().ToLowerInvariant())
        {
            sb.Append(c switch
            {
                >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
                >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
                'ي' => 'ی',
                'ك' => 'ک',
                '‌' or '‏' or '‎' => ' ',
                '،' or ',' or '.' or '!' or '?' or '؟' or '؛' or ';' or '"' or '«' or '»' => ' ',
                _ => c,
            });
        }
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    private static bool Has(string text, params string[] words) => words.Any(w => Regex.IsMatch(text, $@"(?<![\p{{L}}\d]){Regex.Escape(w)}(?![\p{{L}}\d])"));

    private static PowerAction? ActionOf(string t)
    {
        // "restart" before "shutdown": "shut down and restart" is rare, but "restart" must win over a stray "off".
        if (Has(t, "ریستارت", "ری استارت", "ریاستارت", "restart", "reboot", "راه اندازی مجدد", "راهاندازی مجدد", "ریبوت")) return PowerAction.Restart;
        if (Has(t, "هایبرنیت", "hibernate", "هایبرنت")) return PowerAction.Hibernate;
        if (Has(t, "بخواب", "بخوابون", "بخوابان", "خواب", "sleep", "standby", "بخوابه")) return PowerAction.Sleep;
        if (Has(t, "قفل", "lock")) return PowerAction.Lock;
        if (Has(t, "خاموش", "خاموشش", "شات داون", "شاتداون", "shutdown", "shut down", "power off", "poweroff", "turn off", "off")) return PowerAction.Shutdown;
        return null;
    }

    /// <summary>How many minutes a phrase like "یک ساعت و نیم" or "45 minutes" stands for; null if none.</summary>
    private static int? Duration(string t)
    {
        static int? Num(string token) =>
            int.TryParse(token, out var n) ? n : Numbers.TryGetValue(token, out var w) ? w : null;

        var total = 0;
        var found = false;

        // "نیم ساعت", "half an hour"
        if (Regex.IsMatch(t, @"(?<![\p{L}\d])(نیم|half)( an)? (ساعت|hour)")) { total += 30; found = true; }
        // "ساعت و نیم" / "N ساعت و نیم": an hour count followed by half
        var hours = Regex.Match(t, @"(?<![\p{L}\d])(\d+|[\p{L}]+) ?(ساعت|hours?|hrs?|h)(?![\p{L}])( و (نیم)(?![\p{L}])| و (ربع)(?![\p{L}])| and a half)?");
        if (hours.Success && Num(hours.Groups[1].Value) is { } h && !(hours.Groups[1].Value is "نیم" or "half"))
        {
            total += h * 60;
            if (hours.Groups[4].Success || hours.Groups[0].Value.Contains("and a half")) total += 30;
            if (hours.Groups[5].Success) total += 15;
            found = true;
        }
        var minutes = Regex.Match(t, @"(?<![\p{L}\d])(\d+|[\p{L}]+) ?(دقیقه|minutes?|mins?|m)(?![\p{L}])");
        if (minutes.Success && Num(minutes.Groups[1].Value) is { } m)
        {
            total += m;
            found = true;
        }
        if (!found && Regex.IsMatch(t, @"(?<![\p{L}\d])(ربع ساعت|quarter of an hour)")) { total = 15; found = true; }
        return found ? total : null;
    }

    /// <summary>"ساعت 1 شب", "at 1:30 pm", "23:45", "ساعت ۷ صبح"; null if there is no clock time.</summary>
    private static (int hour, int minute)? ClockTime(string t)
    {
        // "ساعت دو" -> "ساعت 2": spoken numbers right after the word for "o'clock".
        t = Regex.Replace(t, @"ساعت (یک|یه|دو|سه|چهار|پنج|شش|شیش|هفت|هشت|نه|ده|یازده|دوازده)(?![\p{L}])",
            m => "ساعت " + Numbers[m.Groups[1].Value]);

        // "ساعت 2 دیگه" / "2 ساعت" are durations, not clock times.
        var m = Regex.Match(t, @"(?<![\p{L}\d])(?:ساعت|at|@) ?(?<h>\d{1,2})(?:[:٫](?<m>\d{2}))?(?: و (?<half>نیم|ربع))?(?! ?\d)(?! ?(?:دیگه|دیگر|بعد|ساعت|دقیقه|hours?|minutes?|mins?|later))(?: ?(?<ap>am|pm|a\.m|p\.m))?");
        if (!m.Success) m = Regex.Match(t, @"(?<![\d:])(?<h>\d{1,2}):(?<m>\d{2})(?!\d)(?: ?(?<ap>am|pm))?");
        if (!m.Success) m = Regex.Match(t, @"(?<![\d:])(?<h>\d{1,2}) ?(?<ap>am|pm)(?![\p{L}])");
        if (!m.Success) return null;

        if (!int.TryParse(m.Groups["h"].Value, out var hour)) return null;
        var minute = m.Groups["m"].Success && int.TryParse(m.Groups["m"].Value, out var mm) ? mm : 0;
        if (m.Groups["half"].Success) minute = m.Groups["half"].Value == "نیم" ? 30 : 15;
        if (hour > 24 || minute > 59) return null;

        var suffix = m.Groups["ap"].Success ? m.Groups["ap"].Value.Replace(".", "") : "";
        if (suffix == "pm") { if (hour < 12) hour += 12; }
        else if (suffix == "am") { if (hour == 12) hour = 0; }
        else if (Has(t, "شب", "night"))
        {
            // "۱۲ شب" is midnight, "۱ شب" is 01:00, "۱۰ شب" is 22:00.
            if (hour == 12) hour = 0;
            else if (hour >= 6 && hour <= 11) hour += 12;
        }
        else if (Has(t, "ظهر", "noon")) { if (hour is >= 1 and <= 4) hour += 12; }
        else if (Has(t, "عصر", "بعدازظهر", "بعد از ظهر", "afternoon", "evening")) { if (hour < 12) hour += 12; }
        else if (Has(t, "صبح", "morning")) { if (hour == 12) hour = 0; }
        if (hour == 24) hour = 0;
        return hour <= 23 ? (hour, minute) : null;
    }

    private static List<DayOfWeek> WeekdaysIn(string t)
    {
        var found = new List<DayOfWeek>();
        foreach (var (words, day) in Days)
        {
            var wordHit = words.Any(w => Has(t, w));
            if (wordHit && !found.Contains(day)) found.Add(day);
        }
        // "یکشنبه" contains "شنبه": prefer the longer names when they matched.
        if (found.Contains(DayOfWeek.Saturday) && (Has(t, "یکشنبه", "یک‌شنبه", "دوشنبه", "سه‌شنبه", "سهشنبه", "چهارشنبه", "پنجشنبه", "پنج‌شنبه")) && !Has(t, "شنبه"))
            found.Remove(DayOfWeek.Saturday);
        return found;
    }

    public static AssistantResult Parse(string text, DateTime now)
    {
        var t = Normalize(text);
        if (t.Length == 0) return AssistantResult.None;

        if (Has(t, "لغو", "کنسل", "cancel", "stop", "بیخیال", "منصرف")) return new AssistantResult(AssistantKind.Cancel);
        if (Has(t, "وضعیت", "status", "چی داریم", "what s next", "whats next")) return new AssistantResult(AssistantKind.Status);
        if (Has(t, "رم", "ram", "حافظه") && Has(t, "خالی", "پاک", "آزاد", "free", "clear", "clean", "empty")) return new AssistantResult(AssistantKind.FreeRam);

        var action = ActionOf(t);
        if (action is null) return AssistantResult.None;

        // Conditions first: "when the download finishes" and "when I am away".
        if (Has(t, "دانلود", "download", "دانلودها") && Has(t, "تموم", "تمام", "تموم شد", "بعد", "finish", "finishes", "done", "ends", "complete", "after", "when", "once", "وقتی"))
            return new AssistantResult(AssistantKind.Rule, action.Value, Rule: RuleKind.Network);
        if (Has(t, "بیکار", "بی استفاده", "بی‌استفاده", "idle", "inactive", "away", "دور") || Regex.IsMatch(t, @"(دست نخور|کار نکن|استفاده نکن|not used|untouched)"))
        {
            var idle = Duration(t) ?? 30;
            return new AssistantResult(AssistantKind.Rule, action.Value, Rule: RuleKind.Idle, Minutes: Math.Clamp(idle, 1, 600));
        }

        var days = WeekdaysIn(t);
        var everyDay = Has(t, "هر شب", "هر روز", "همه روز", "همه روزها", "every night", "every day", "daily", "nightly", "هرشب", "هرروز");
        var repeating = everyDay || Has(t, "هر", "every", "each") && days.Count > 0;

        var clock = ClockTime(t);
        var span = clock is null ? Duration(t) : null;

        DateTime due;
        if (span is { } minutes && minutes > 0 && !repeating)
        {
            due = now.AddMinutes(minutes);
            return new AssistantResult(AssistantKind.Schedule, action.Value, due, null);
        }
        if (clock is { } c)
        {
            if (Has(t, "پس فردا", "پسفردا", "day after tomorrow"))
                due = now.Date.AddDays(2).AddHours(c.hour).AddMinutes(c.minute);
            else if (Has(t, "فردا", "tomorrow"))
                due = now.Date.AddDays(1).AddHours(c.hour).AddMinutes(c.minute);
            else if (days.Count > 0 && !everyDay)
                due = Schedule.Next(c.hour, c.minute, days, now);
            else
                due = Schedule.Next(c.hour, c.minute, [], now);

            IReadOnlyList<DayOfWeek>? repeat = null;
            if (everyDay) repeat = [.. Fa.Week];
            else if (repeating && days.Count > 0) repeat = days;
            return new AssistantResult(AssistantKind.Schedule, action.Value, due, repeat);
        }
        if (Has(t, "همین الان", "الان", "now", "right now", "immediately", "فوری")) return new AssistantResult(AssistantKind.Schedule, action.Value, now);
        return AssistantResult.None;
    }

    /// <summary>The sentence read back to the user, with exact times.</summary>
    public static string Describe(AssistantResult r, DateTime now)
    {
        switch (r.Kind)
        {
            case AssistantKind.Cancel:
                return L.T("همه‌ی دستورهای زمان‌بندی‌شده و قانون فعال لغو بشن.", "Cancel every scheduled command and the active rule.");
            case AssistantKind.FreeRam:
                return L.T("رم خالی بشه.", "Free up RAM.");
            case AssistantKind.Status:
                return L.T("وضعیت نشون داده بشه.", "Show the status.");
            case AssistantKind.Rule when r.Rule == RuleKind.Network:
                return L.T($"از همین الان نظارت روی دانلود شروع بشه؛ وقتی دانلود تموم شد: {Fa.ActionSentence(r.Action)}.",
                    $"Start watching downloads now; when they finish: {Fa.ActionSentence(r.Action)}.");
            case AssistantKind.Rule:
                return L.T($"اگه {Fa.Digits(r.Minutes.ToString())} دقیقه موس و کیبورد دست نخورد: {Fa.ActionSentence(r.Action)}.",
                    $"If the mouse and keyboard stay untouched for {r.Minutes} minutes: {Fa.ActionSentence(r.Action)}.");
            case AssistantKind.Schedule when r.Due is { } due:
                var when = $"{Fa.RelativeDay(due, now)} {L.T("ساعت", "at")} {Fa.Clock(due)}";
                var repeat = r.Repeat is { Count: > 0 } days
                    ? (days.Count == 7 ? L.T(" · هر روز تکرار می‌شه", " · repeats every day") : L.T($" · تکرار: {Fa.Days([.. days])}", $" · repeats: {Fa.Days([.. days])}"))
                    : "";
                return L.T($"{Fa.ActionSentence(r.Action)} — {when} ({Fa.Duration(due - now)} دیگه){repeat}.",
                    $"{Fa.ActionSentence(r.Action)} — {when} (in {Fa.Duration(due - now)}){repeat}.");
            default:
                return L.T("متوجه نشدم.", "I did not understand that.");
        }
    }

    public static string Examples() => L.T(
        "مثلاً: «یک ساعت و نیم دیگه خاموش کن»، «ساعت ۱ شب بخواب»، «هر شب ساعت ۲ خاموش کن»، «وقتی دانلود تموم شد خاموش کن»",
        "For example: “shut down in 90 minutes”, “sleep at 1 am”, “shut down every night at 2:00”, “shut down when the download finishes”");
}
