using System.Globalization;

namespace Shabpa.Core;

/// <summary>What part of the day a clock time was said with: «صبح»/"am", «شب»/"at night" and so on.</summary>
public enum DayPart { None, Morning, Noon, Afternoon, Evening, Night, Midnight }

/// <summary>Everything about "when" that was found in a command.</summary>
public sealed class TimeFound
{
    /// <summary>A length of time: «یه ساعت و ربع», "30 min".</summary>
    public TimeSpan? Duration { get; set; }
    /// <summary>The duration was said as "from now" («دیگه»، «بعد»، "in", "later"), so it is a delay rather than a length.</summary>
    public bool Relative { get; set; }
    /// <summary>The duration was said as a length («به مدت»، "for").</summary>
    public bool Length { get; set; }
    /// <summary>The moments a clock time can mean, soonest first: none (no clock time), one, or two when «ساعت ۲»
    /// could be 2 at night or 2 in the afternoon.</summary>
    public List<DateTime> Moments { get; } = [];
    /// <summary>The hour (or the whole time) was not said and was filled in: «فردا صبح» → 8:00.</summary>
    public bool Defaulted { get; set; }
    /// <summary>Weekly repeat days («هر شب»، "every friday"); all seven for every day.</summary>
    public List<DayOfWeek> Repeat { get; } = [];
    /// <summary>A day was said but it has already passed («امروز ساعت ۸» at 21:00).</summary>
    public bool InPast { get; set; }
    /// <summary>A clock time that cannot be («ساعت ۲۵»، "25:00").</summary>
    public bool Invalid { get; set; }
    /// <summary>A percentage («۴۰ درصد»، "40%").</summary>
    public int? Percent { get; set; }

    /// <summary>«هر شب» says the night as well as the repeat.</summary>
    internal DayPart RepeatPart { get; set; }

    public bool HasClock => Moments.Count > 0;
    public bool Any => Duration is not null || HasClock || Repeat.Count > 0 || InPast || Invalid;
}

/// <summary>Reads times out of a tokenised command: durations («یه ساعت و ربع دیگه»، "in 1.5 hours"), clock times
/// («ساعت ۲ و نیم شب»، "at 2am", "quarter to 3"), days («فردا صبح»، "on friday") and weekly repeats. Words it
/// uses are marked in <c>used</c> so the caller can see what is left (a reminder's title). Pure logic.</summary>
public static class TimePhrase
{
    private static readonly Dictionary<string, int> FaNumbers = new()
    {
        ["صفر"] = 0, ["یک"] = 1, ["یه"] = 1, ["یدونه"] = 1, ["دو"] = 2, ["سه"] = 3, ["چهار"] = 4, ["چار"] = 4, ["پنج"] = 5,
        ["شش"] = 6, ["شیش"] = 6, ["هفت"] = 7, ["هشت"] = 8, ["نه"] = 9, ["ده"] = 10, ["یازده"] = 11, ["دوازده"] = 12,
        ["سیزده"] = 13, ["چهارده"] = 14, ["پانزده"] = 15, ["پونزده"] = 15, ["شانزده"] = 16, ["شونزده"] = 16,
        ["هفده"] = 17, ["هیفده"] = 17, ["هجده"] = 18, ["هیجده"] = 18, ["نوزده"] = 19, ["بیست"] = 20, ["سی"] = 30,
        ["چهل"] = 40, ["پنجاه"] = 50, ["شصت"] = 60, ["هفتاد"] = 70, ["هشتاد"] = 80, ["نود"] = 90, ["صد"] = 100,
        ["صدوبیست"] = 120,
    };

    private static readonly Dictionary<string, int> EnNumbers = new()
    {
        ["zero"] = 0, ["one"] = 1, ["a"] = 1, ["an"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6,
        ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["thirteen"] = 13,
        ["fourteen"] = 14, ["fifteen"] = 15, ["sixteen"] = 16, ["seventeen"] = 17, ["eighteen"] = 18, ["nineteen"] = 19,
        ["twenty"] = 20, ["thirty"] = 30, ["forty"] = 40, ["fourty"] = 40, ["fifty"] = 50, ["sixty"] = 60, ["ninety"] = 90,
        ["hundred"] = 100,
    };

    private enum Unit { None, Second, Minute, Hour, Day }

    private static Unit UnitOf(string w)
    {
        if (w.StartsWith("دقیقه", StringComparison.Ordinal) || w.StartsWith("دیقه", StringComparison.Ordinal) || w.StartsWith("دقیقع", StringComparison.Ordinal)
            || w is "min" or "mins" or "minute" or "minutes" or "minuts" or "minuets" or "m" or "دق") return Unit.Minute;
        if (w.StartsWith("ساعت", StringComparison.Ordinal) || w is "h" or "hr" or "hrs" or "hour" or "hours" or "houre") return Unit.Hour;
        if (w.StartsWith("ثانیه", StringComparison.Ordinal) || w is "s" or "sec" or "secs" or "second" or "seconds") return Unit.Second;
        if (w is "روز" or "روزه" or "day" or "days") return Unit.Day;
        return Unit.None;
    }

    private static TimeSpan Of(double value, Unit unit) => unit switch
    {
        Unit.Second => TimeSpan.FromSeconds(value),
        Unit.Minute => TimeSpan.FromMinutes(value),
        Unit.Hour => TimeSpan.FromHours(value),
        _ => TimeSpan.FromDays(value),
    };

    private static bool IsAnd(string w) => w is "و" or "and" or "&";

    /// <summary>A number at <paramref name="i"/>: digits ("2", "1.5"), Persian words with «و» («بیست و پنج»)
    /// or English words ("twenty five"). <paramref name="loose"/> allows «یه»/"a" (only before a unit).</summary>
    internal static bool TryNumber(IReadOnlyList<CommandToken> t, int i, out double value, out int length, bool loose = true)
    {
        value = 0;
        length = 0;
        if (i >= t.Count) return false;
        var w = t[i].Norm;
        if (double.TryParse(w, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value) && w.All(c => char.IsAsciiDigit(c) || c == '.'))
        {
            length = 1;
            return true;
        }
        if (FaNumbers.TryGetValue(w, out var n) || EnNumbers.TryGetValue(w, out n))
        {
            if (!loose && w is "یه" or "a" or "an" or "یدونه") return false;
            value = n;
            length = 1;
            // «بیست و پنج», "twenty five", "twenty-five"
            if (n is >= 20 and < 100 && n % 10 == 0)
            {
                if (i + 2 < t.Count && IsAnd(t[i + 1].Norm) && FaNumbers.TryGetValue(t[i + 2].Norm, out var u) && u is > 0 and < 10)
                {
                    value += u;
                    length = 3;
                }
                else if (i + 1 < t.Count && EnNumbers.TryGetValue(t[i + 1].Norm, out var e) && e is > 0 and < 10 && t[i + 1].Norm is not "a" and not "an")
                {
                    value += e;
                    length = 2;
                }
            }
            return true;
        }
        return false;
    }

    private static readonly string[] RelativeAfter = ["دیگه", "دیگر", "دگه", "دیگع", "بعد", "later", "بعدش"];

    /// <summary>Finds every time phrase. <paramref name="used"/> must be as long as <paramref name="t"/>.</summary>
    public static TimeFound Parse(IReadOnlyList<CommandToken> t, bool[] used, DateTime now)
    {
        var found = new TimeFound();
        ParsePercent(t, used, found);
        ParseRepeat(t, used, found);
        var day = ParseDay(t, used, found, now, out var dayPartFromDay);
        var clock = ParseClock(t, used, out var part);
        if (part == DayPart.None) part = dayPartFromDay;
        if (part == DayPart.None) part = ParseLoosePart(t, used);
        if (part == DayPart.None) part = found.RepeatPart;
        ParseDuration(t, used, found);
        Resolve(found, now, day, clock, part);
        return found;
    }

    // ---- percentages ------------------------------------------------------------------------

    private static void ParsePercent(IReadOnlyList<CommandToken> t, bool[] used, TimeFound found)
    {
        for (var i = 0; i < t.Count; i++)
        {
            var w = t[i].Norm.Replace('٪', '%');
            if (w.Length > 1 && w[^1] == '%' && int.TryParse(w[..^1], out var p))
            {
                found.Percent = p;
                used[i] = true;
                return;
            }
            if (TryNumber(t, i, out var v, out var len, loose: false) && i + len < t.Count && t[i + len].Norm is "درصد" or "%" or "percent" or "٪")
            {
                found.Percent = (int)v;
                for (var k = i; k <= i + len; k++) used[k] = true;
                return;
            }
        }
    }

    // ---- weekly repeats ---------------------------------------------------------------------

    private static readonly DayOfWeek[] All = [DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    internal static DayOfWeek? Weekday(string w) => w switch
    {
        "شنبه" or "شمبه" or "saturday" or "sat" => DayOfWeek.Saturday,
        "یکشنبه" or "یکشمبه" or "sunday" or "sun" => DayOfWeek.Sunday,
        "دوشنبه" or "دوشمبه" or "monday" or "mon" => DayOfWeek.Monday,
        "سهشنبه" or "سشنبه" or "سهشمبه" or "tuesday" or "tue" or "tues" => DayOfWeek.Tuesday,
        "چهارشنبه" or "چارشنبه" or "چهارشمبه" or "wednesday" or "wed" => DayOfWeek.Wednesday,
        "پنجشنبه" or "پنجشمبه" or "thursday" or "thu" or "thurs" => DayOfWeek.Thursday,
        "جمعه" or "friday" or "fri" => DayOfWeek.Friday,
        _ => null,
    };

    /// <summary>A weekday at <paramref name="i"/>; «دو شنبه» and «چهار شنبه» may also arrive as two words.</summary>
    private static DayOfWeek? WeekdayAt(IReadOnlyList<CommandToken> t, int i, out int length)
    {
        length = 1;
        if (i + 1 < t.Count && t[i + 1].Norm is "شنبه" or "شمبه" && t[i].Norm is "دو" or "چهار" or "چار")
        {
            length = 2;
            return t[i].Norm == "دو" ? DayOfWeek.Monday : DayOfWeek.Wednesday;
        }
        var w = t[i].Norm;
        // «جمعه‌ها»، "fridays"
        if (w.EndsWith("ها", StringComparison.Ordinal) && Weekday(w[..^2]) is { } plural) return plural;
        if (w.EndsWith('s') && w.Length > 4 && Weekday(w[..^1]) is { } en) return en;
        return Weekday(w);
    }

    private static void ParseRepeat(IReadOnlyList<CommandToken> t, bool[] used, TimeFound found)
    {
        for (var i = 0; i < t.Count; i++)
        {
            var w = t[i].Norm;
            if (w is "daily" or "nightly" or "هرشب" or "هرروز" or "شبها" or "everyday")
            {
                found.Repeat.AddRange(All);
                if (w is "nightly" or "هرشب" or "شبها") found.RepeatPart = DayPart.Night;
                used[i] = true;
                continue;
            }
            if (w is not ("هر" or "every" or "each")) continue;
            if (i + 1 >= t.Count) continue;
            var next = t[i + 1].Norm;
            if (next is "روز" or "شب" or "day" or "night" or "evening" or "morning" or "صبح" or "عصر")
            {
                found.Repeat.AddRange(All);
                found.RepeatPart = next switch { "شب" or "night" => DayPart.Night, "evening" => DayPart.Evening, "صبح" or "morning" => DayPart.Morning, "عصر" => DayPart.Afternoon, _ => DayPart.None };
                used[i] = used[i + 1] = true;
                continue;
            }
            // «هر جمعه»، «هر شنبه و سه‌شنبه»، "every monday and friday"
            var j = i + 1;
            var any = false;
            while (j < t.Count)
            {
                if (WeekdayAt(t, j, out var len) is { } d)
                {
                    found.Repeat.Add(d);
                    for (var k = j; k < j + len; k++) used[k] = true;
                    j += len;
                    any = true;
                    if (j + 1 < t.Count && (IsAnd(t[j].Norm) || t[j].Norm == "or") && WeekdayAt(t, j + 1, out _) is not null)
                    {
                        used[j] = true;
                        j++;
                    }
                    continue;
                }
                break;
            }
            if (any) used[i] = true;
        }
        for (var i = 0; i < t.Count; i++)
        {
            if (t[i].Norm is "weekdays" && !used[i])
            {
                found.Repeat.AddRange([DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday]);
                used[i] = true;
            }
            else if (t[i].Norm is "weekends" && !used[i])
            {
                found.Repeat.AddRange([DayOfWeek.Saturday, DayOfWeek.Sunday]);
                used[i] = true;
            }
        }
        var distinct = found.Repeat.Distinct().ToList();
        found.Repeat.Clear();
        found.Repeat.AddRange(distinct);
    }

    // ---- days -------------------------------------------------------------------------------

    /// <summary>The date that was named (today, tomorrow, a weekday), or null.</summary>
    private static DateTime? ParseDay(IReadOnlyList<CommandToken> t, bool[] used, TimeFound found, DateTime now, out DayPart part)
    {
        part = DayPart.None;
        for (var i = 0; i < t.Count; i++)
        {
            if (used[i]) continue;
            var w = t[i].Norm;
            switch (w)
            {
                case "امروز" or "today":
                    used[i] = true;
                    return now.Date;
                case "امشب" or "tonight":
                    used[i] = true;
                    part = DayPart.Night;
                    return now.Date;
                case "فردا" or "tomorrow" or "tmrw" or "tomorow" or "tommorow" or "tommorrow":
                    used[i] = true;
                    if (i > 0 && t[i - 1].Norm is "پس" && !used[i - 1]) { used[i - 1] = true; return now.Date.AddDays(2); }
                    return now.Date.AddDays(1);
                case "فرداشب":
                    used[i] = true;
                    part = DayPart.Night;
                    return now.Date.AddDays(1);
                case "فرداصبح":
                    used[i] = true;
                    part = DayPart.Morning;
                    return now.Date.AddDays(1);
                case "پسفردا":
                    used[i] = true;
                    return now.Date.AddDays(2);
            }
            if (w == "day" && i + 2 < t.Count && t[i + 1].Norm == "after" && t[i + 2].Norm is "tomorrow")
            {
                used[i] = used[i + 1] = used[i + 2] = true;
                if (i > 0 && t[i - 1].Norm == "the") used[i - 1] = true;
                return now.Date.AddDays(2);
            }
            if (WeekdayAt(t, i, out var len) is { } d)
            {
                for (var k = i; k < i + len; k++) used[k] = true;
                if (i > 0 && t[i - 1].Norm is "on" or "next" or "this" or "این" && !used[i - 1]) used[i - 1] = true;
                if (i + len < t.Count && t[i + len].Norm is "آینده" or "بعد" or "دیگه" && !used[i + len]) used[i + len] = true;
                var ahead = ((int)d - (int)now.DayOfWeek + 7) % 7;
                return now.Date.AddDays(ahead);
            }
        }
        return null;
    }

    // ---- clock times ------------------------------------------------------------------------

    private static DayPart PartOf(IReadOnlyList<CommandToken> t, int i, bool[] used, out int length)
    {
        length = 1;
        if (i >= t.Count || used[i]) return DayPart.None;
        var w = t[i].Norm;
        var next = i + 1 < t.Count ? t[i + 1].Norm : "";
        if (w is "نصفه" or "نصف" or "نیمه" && next == "شب") { length = 2; return DayPart.Midnight; }
        if (w is "بعد" && next is "ازظهر") { length = 2; return DayPart.Afternoon; }
        if (w is "بعد" && next is "از" && i + 2 < t.Count && t[i + 2].Norm == "ظهر") { length = 3; return DayPart.Afternoon; }
        if (w is "in" or "at" or "this" && i + 1 < t.Count)
        {
            var skip = i + 1 < t.Count && t[i + 1].Norm == "the" ? 2 : 1;
            if (i + skip < t.Count)
            {
                var p = t[i + skip].Norm switch
                {
                    "morning" => DayPart.Morning,
                    "afternoon" => DayPart.Afternoon,
                    "evening" => DayPart.Evening,
                    "night" => DayPart.Night,
                    _ => DayPart.None,
                };
                if (p != DayPart.None) { length = skip + 1; return p; }
            }
        }
        return w switch
        {
            "صبح" or "صب" or "بامداد" or "am" or "a.m" or "morning" => DayPart.Morning,
            "ظهر" or "noon" => DayPart.Noon,
            "بعدازظهر" or "بعدظهر" or "عصر" or "pm" or "p.m" or "afternoon" => DayPart.Afternoon,
            "غروب" or "evening" => DayPart.Evening,
            "شب" or "night" or "tonight" => DayPart.Night,
            "نصفهشب" or "نصفشب" or "نیمهشب" or "midnight" => DayPart.Midnight,
            _ => DayPart.None,
        };
    }

    /// <summary>A day part said on its own, with no clock time («فردا صبح»، "tomorrow morning").</summary>
    private static DayPart ParseLoosePart(IReadOnlyList<CommandToken> t, bool[] used)
    {
        for (var i = 0; i < t.Count; i++)
        {
            var p = PartOf(t, i, used, out var len);
            if (p == DayPart.None) continue;
            for (var k = i; k < i + len; k++) used[k] = true;
            return p;
        }
        return DayPart.None;
    }

    private static bool IsClockWord(string w) => w is "ساعت" or "ساعته" or "at" or "@" or "around" or "حدود" or "حدودا" or "سر";

    /// <summary>(hour, minute, written with a leading zero) of the first clock time, or null.</summary>
    private static (int Hour, int Minute, bool Zero)? ParseClock(IReadOnlyList<CommandToken> t, bool[] used, out DayPart part)
    {
        part = DayPart.None;
        for (var i = 0; i < t.Count; i++)
        {
            if (used[i]) continue;
            var w = t[i].Norm;

            // "noon", "midnight", «نصف شب» on their own are a time
            if (w is "noon" or "midday" && !(i > 0 && IsHourLike(t, i - 1)))
            {
                used[i] = true;
                if (i > 0 && t[i - 1].Norm is "at" or "by") used[i - 1] = true;
                part = DayPart.Noon;
                return (12, 0, false);
            }
            var midnightWords = w is "نصفه" or "نصف" or "نیمه" && i + 1 < t.Count && t[i + 1].Norm == "شب" ? 2 : w is "midnight" or "نصفهشب" or "نصفشب" or "نیمهشب" ? 1 : 0;
            if (midnightWords > 0 && !(i > 0 && IsHourLike(t, i - 1)))
            {
                Mark(used, i, i + midnightWords);
                if (i > 0 && t[i - 1].Norm is "at" or "by" or "سر") used[i - 1] = true;
                part = DayPart.Midnight;
                return (0, 0, true);
            }

            // "half past 2", "quarter past 3", "quarter to 3", «یه ربع به ۳»، «ده دقیقه به ۳»
            if (TryRelativeClock(t, used, i, out var rel, out var relLen))
            {
                for (var k = i; k < i + relLen; k++) used[k] = true;
                part = PartOf(t, i + relLen, used, out var pl);
                if (part != DayPart.None) for (var k = i + relLen; k < i + relLen + pl; k++) used[k] = true;
                if (i > 0 && IsClockWord(t[i - 1].Norm) && !used[i - 1]) used[i - 1] = true;
                return rel;
            }

            var clockWord = IsClockWord(w);
            var start = clockWord ? i + 1 : i;
            if (start >= t.Count || used[start]) continue;

            // "2:30", "14:00"
            if (TryColon(t[start].Norm, out var h, out var m, out var zero))
            {
                var end = start + 1;
                end = Tail(t, used, end, ref h, ref m, out part);
                Mark(used, clockWord ? i : start, end);
                return (h, m, zero);
            }

            if (clockWord && !used[start] && (t[start].Norm.Contains(':') || (TryNumber(t, start, out var bad, out _, loose: false) && (bad > 24 || bad % 1 != 0))
                                              && !(start + 1 < t.Count && UnitOf(t[start + 1].Norm) != Unit.None)))
            {
                // «ساعت ۲۵»، "at 25:00": a time that does not exist
                used[start] = true;
                return (-1, 0, false);
            }
            if (!TryNumber(t, start, out var value, out var len, loose: false) || value % 1 != 0 || value is < 0 or > 24) continue;
            var after = start + len;
            // «دو ساعت» / "2 hours" is a length, not a clock time
            if (after < t.Count && UnitOf(t[after].Norm) is not Unit.None and var u && !(u == Unit.Hour && clockWord)) continue;
            if (!clockWord)
            {
                // a bare number is only a time when a day part or o'clock follows: «۲ بامداد»، "2am", "7 o'clock"
                var p0 = PartOf(t, after, used, out _);
                var oclock = after < t.Count && t[after].Norm is "oclock" or "o'clock" or "o’clock";
                var colonNext = false;
                if (p0 == DayPart.None && !oclock)
                {
                    // «۲ و نیم شب»
                    if (after + 2 < t.Count && IsAnd(t[after].Norm) && t[after + 1].Norm is "نیم" or "ربع" && PartOf(t, after + 2, used, out _) != DayPart.None) colonNext = true;
                    if (!colonNext) continue;
                }
                if (oclock) used[after] = true;
            }
            h = (int)value;
            m = 0;
            var tail = Tail(t, used, after, ref h, ref m, out part);
            Mark(used, i, tail);
            return (h, m, t[start].Norm.Length == 2 && t[start].Norm[0] == '0');
        }
        return null;
    }

    private static bool IsHourLike(IReadOnlyList<CommandToken> t, int i) =>
        TryNumber(t, i, out var v, out _, loose: false) && v is >= 0 and <= 24 || TryColon(t[i].Norm, out _, out _, out _);

    private static void Mark(bool[] used, int from, int to)
    {
        for (var k = from; k < to && k < used.Length; k++) used[k] = true;
    }

    private static bool TryColon(string w, out int hour, out int minute, out bool zero)
    {
        hour = minute = 0;
        zero = false;
        var parts = w.Split(':');
        if (parts.Length != 2 || parts[1].Length != 2 || !int.TryParse(parts[0], out hour) || !int.TryParse(parts[1], out minute)) return false;
        zero = parts[0].Length == 2 && parts[0][0] == '0';
        return hour is >= 0 and <= 24 && minute is >= 0 and < 60;
    }

    /// <summary>What may follow the hour: «و نیم»، «و ربع»، «و ده دقیقه»، «ده دقیقه» and then a day part.</summary>
    private static int Tail(IReadOnlyList<CommandToken> t, bool[] used, int i, ref int hour, ref int minute, out DayPart part)
    {
        if (i < t.Count && IsAnd(t[i].Norm) && i + 1 < t.Count)
        {
            var w = t[i + 1].Norm;
            if (w is "نیم" or "half") { minute = 30; i += 2; }
            else if (w is "ربع" or "quarter") { minute = 15; i += 2; }
            else if (TryNumber(t, i + 1, out var v, out var len, loose: false) && v is > 0 and < 60 && v % 1 == 0)
            {
                // «ساعت ۲ و ده دقیقه», but not «ساعت ۲ و ۱۰ دقیقه دیگه» (that tail belongs to a duration, which is rare enough to ignore)
                minute = (int)v;
                i += 1 + len;
                if (i < t.Count && UnitOf(t[i].Norm) == Unit.Minute) i++;
            }
        }
        else if (i < t.Count && TryNumber(t, i, out var v2, out var len2, loose: false) && v2 is > 0 and < 60 && v2 % 1 == 0
                 && i + len2 < t.Count && UnitOf(t[i + len2].Norm) == Unit.Minute && !(i + len2 + 1 < t.Count && RelativeAfter.Contains(t[i + len2 + 1].Norm)))
        {
            // «ساعت ۲ ده دقیقه»
            minute = (int)v2;
            i += len2 + 1;
        }
        if (i < t.Count && t[i].Norm is "oclock" or "o'clock" or "o’clock") i++;
        part = PartOf(t, i, used, out var pl);
        if (part != DayPart.None) i += pl;
        // "2 in the morning" is handled by PartOf; «ساعت ۲ شب فردا» needs nothing more
        return i;
    }

    private static bool TryRelativeClock(IReadOnlyList<CommandToken> t, bool[] used, int i, out (int, int, bool) clock, out int length)
    {
        clock = default;
        length = 0;
        var w = t[i].Norm;
        // English: "half past 2", "quarter past 3", "quarter to 3", "10 past 4", "20 to 5"
        {
            var j = i;
            int minutes;
            if (w is "half") minutes = 30;
            else if (w is "quarter") minutes = 15;
            else if (w == "a" && i + 1 < t.Count && t[i + 1].Norm == "quarter") { minutes = 15; j++; }
            else if (TryNumber(t, i, out var v, out var len, loose: false) && v is > 0 and < 60) { minutes = (int)v; j += len - 1; if (j + 1 < t.Count && UnitOf(t[j + 1].Norm) == Unit.Minute) j++; }
            else minutes = -1;
            if (minutes > 0 && j + 2 < t.Count && t[j + 1].Norm is "past" or "to" or "after" or "before" && TryNumber(t, j + 2, out var hv, out var hl, loose: false) && hv is >= 1 and <= 12 && hv % 1 == 0)
            {
                var h = (int)hv;
                var to = t[j + 1].Norm is "to" or "before";
                clock = to ? ((h + 11) % 12 == 0 ? 12 : (h + 11) % 12, 60 - minutes, false) : (h, minutes, false);
                if (to && h == 1) clock = (12, 60 - minutes, false);
                length = j + 2 + hl - i;
                return true;
            }
        }
        // Persian: «یه ربع به ۳»، «ربع به ۳»، «ده دقیقه به ۳»، «۱۰ دقیقه مونده به ۳»
        {
            var j = i;
            int minutes;
            if (w is "ربع") minutes = 15;
            else if (w is "یه" or "یک" && i + 1 < t.Count && t[i + 1].Norm == "ربع") { minutes = 15; j++; }
            else if (TryNumber(t, i, out var v, out var len, loose: false) && v is > 0 and < 60 && i + len < t.Count && UnitOf(t[i + len].Norm) == Unit.Minute) { minutes = (int)v; j = i + len; }
            else return false;
            if (j + 1 < t.Count && t[j + 1].Norm is "مونده" or "مانده") j++;
            if (j + 2 < t.Count && t[j + 1].Norm == "به" && TryNumber(t, j + 2, out var hv, out var hl, loose: false) && hv is >= 1 and <= 24 && hv % 1 == 0)
            {
                var h = (int)hv;
                var hour = h == 1 ? 12 : h - 1;
                if (h == 13) hour = 12;
                clock = (hour, 60 - minutes, false);
                length = j + 2 + hl - i;
                return true;
            }
            if (j + 3 < t.Count && t[j + 1].Norm == "به" && t[j + 2].Norm == "ساعت" && TryNumber(t, j + 3, out hv, out hl, loose: false) && hv is >= 1 and <= 24 && hv % 1 == 0)
            {
                var h = (int)hv;
                clock = (h == 1 ? 12 : h - 1, 60 - minutes, false);
                length = j + 3 + hl - i;
                return true;
            }
        }
        return false;
    }

    // ---- durations --------------------------------------------------------------------------

    private static void ParseDuration(IReadOnlyList<CommandToken> t, bool[] used, TimeFound found)
    {
        for (var i = 0; i < t.Count; i++)
        {
            if (used[i]) continue;
            if (!TryDuration(t, used, i, out var span, out var len)) continue;
            Mark(used, i, i + len);
            found.Duration = span;

            // after: «دیگه»، «بعد»، "later", "from now"
            var after = i + len;
            if (after < t.Count && !used[after] && RelativeAfter.Contains(t[after].Norm))
            {
                found.Relative = true;
                used[after] = true;
            }
            else if (after + 1 < t.Count && t[after].Norm == "from" && t[after + 1].Norm == "now")
            {
                found.Relative = true;
                used[after] = used[after + 1] = true;
            }
            // before: "in", "after", «بعد از»، «تا», «ظرف»; or a length: "for", «به مدت»
            if (i > 0 && !used[i - 1])
            {
                var b = t[i - 1].Norm;
                if (b is "in" or "after" or "within" or "ظرف" or "تا" or "پس")
                {
                    found.Relative = true;
                    used[i - 1] = true;
                }
                else if (b is "از" && i > 1 && t[i - 2].Norm is "بعد" or "پس" && !used[i - 2])
                {
                    found.Relative = true;
                    used[i - 1] = used[i - 2] = true;
                }
                else if (b is "for" or "مدت" or "طول")
                {
                    found.Length = true;
                    used[i - 1] = true;
                    if (b == "مدت" && i > 1 && t[i - 2].Norm == "به") used[i - 2] = true;
                }
            }
            return;
        }
    }

    /// <summary>A whole duration starting at <paramref name="i"/>: «یه ساعت و ربع»، «یک ساعت و نیم»، «نیم ساعت»،
    /// «سه ربع»، «دو ساعت و بیست دقیقه»، "an hour and a half", "half an hour", "1h30m", "1.5 hours".</summary>
    private static bool TryDuration(IReadOnlyList<CommandToken> t, bool[] used, int i, out TimeSpan span, out int length)
    {
        span = TimeSpan.Zero;
        length = 0;
        var j = i;
        if (!TryPart(t, used, ref j, out var first, out var unit)) return false;
        span = first;
        // «و نیم»، «و ربع»، «و ده دقیقه», "and a half", "and 30 minutes", or straight on "1 hour 30 minutes"
        while (j < t.Count)
        {
            var k = j;
            if (IsAnd(t[k].Norm)) k++;
            if (k >= t.Count || used[k]) break;
            var w = t[k].Norm;
            if (w is "نیم" or "half" || (w == "a" && k + 1 < t.Count && t[k + 1].Norm == "half"))
            {
                if (unit is not (Unit.Hour or Unit.Day or Unit.Minute)) break;
                span += Of(0.5, unit);
                j = w == "a" ? k + 2 : k + 1;
                continue;
            }
            if (w is "ربع" && unit == Unit.Hour)
            {
                span += TimeSpan.FromMinutes(15);
                j = k + 1;
                continue;
            }
            var probe = k;
            if (TryPart(t, used, ref probe, out var more, out var u2) && u2 < unit)
            {
                span += more;
                unit = u2;
                j = probe;
                continue;
            }
            break;
        }
        length = j - i;
        return span > TimeSpan.Zero;
    }

    private static bool TryPart(IReadOnlyList<CommandToken> t, bool[] used, ref int i, out TimeSpan span, out Unit unit)
    {
        span = TimeSpan.Zero;
        unit = Unit.None;
        if (i >= t.Count || used[i]) return false;
        var w = t[i].Norm;

        // "1h30m", "1h30", "90m", "2h"
        if (System.Text.RegularExpressions.Regex.Match(w, @"^(\d+)h(\d+)m?$") is { Success: true } hm)
        {
            span = TimeSpan.FromHours(int.Parse(hm.Groups[1].Value)) + TimeSpan.FromMinutes(int.Parse(hm.Groups[2].Value));
            unit = Unit.Minute;
            i++;
            return true;
        }

        // «نیم ساعت», "half an hour", "half hour"
        if (w is "نیم" or "half" && i + 1 < t.Count)
        {
            var k = i + 1;
            if (t[k].Norm is "an" or "a" && k + 1 < t.Count) k++;
            if (UnitOf(t[k].Norm) is not Unit.None and var hu && t[k].Norm is not "m" and not "s" and not "h")
            {
                span = Of(0.5, hu);
                unit = hu;
                i = k + 1;
                return true;
            }
        }
        // «ربع ساعت», «یه ربع», «سه ربع», "a quarter of an hour", "quarter hour"
        if (w is "ربع" or "quarter" || (w is "یه" or "یک" or "a" or "سه" or "three" && i + 1 < t.Count && t[i + 1].Norm is "ربع" or "quarter"))
        {
            var quarters = w is "سه" or "three" ? 3 : 1;
            var k = w is "ربع" or "quarter" ? i + 1 : i + 2;
            // "a quarter to 3" / «یه ربع به ۳» are clock times, handled elsewhere
            if (k < t.Count && t[k].Norm is "به" or "to" or "past") return false;
            if (k < t.Count && t[k].Norm is "of" && k + 1 < t.Count) { k++; if (t[k].Norm is "an" or "a") k++; }
            if (k < t.Count && UnitOf(t[k].Norm) == Unit.Hour) k++;
            else if (w is "quarter" or "a" && !(k < t.Count && t[k - 1].Norm is "hour")) { if (w == "quarter") return false; }
            span = TimeSpan.FromMinutes(15 * quarters);
            unit = Unit.Minute;
            i = k;
            return true;
        }

        if (!TryNumber(t, i, out var value, out var len)) return false;
        var j = i + len;
        if (j >= t.Count) return false;
        unit = UnitOf(t[j].Norm);
        // "1 hr", but "a h" is not English
        if (unit == Unit.None || (t[i].Norm is "a" or "an" or "یه" && t[j].Norm.Length <= 2)) return false;
        // «ساعت ۲» is a clock time, «۲ ساعت» a duration; «یه ساعت» too
        span = Of(value, unit);
        i = j + 1;
        return true;
    }

    // ---- putting it together ----------------------------------------------------------------

    private static int DefaultHour(DayPart part) => part switch
    {
        DayPart.Morning => 8,
        DayPart.Noon => 12,
        DayPart.Afternoon => 16,
        DayPart.Evening => 19,
        DayPart.Night => 22,
        DayPart.Midnight => 0,
        _ => 9,
    };

    /// <summary>The 24-hour readings of <paramref name="hour"/> said with <paramref name="part"/>; two when it could be either.</summary>
    internal static int[] Hours(int hour, DayPart part, bool zero)
    {
        if (hour == 24) return [0];
        if (hour > 12 || zero) return [hour];
        if (hour == 0) return [0];
        return part switch
        {
            DayPart.Morning => [hour == 12 ? 0 : hour],
            DayPart.Noon => [hour is >= 11 ? hour : hour + 12],
            DayPart.Afternoon or DayPart.Evening => [hour == 12 ? 12 : hour + 12],
            DayPart.Night => [hour == 12 ? 0 : hour <= 5 ? hour : hour + 12],
            DayPart.Midnight => [hour == 12 ? 0 : hour <= 6 ? hour : hour + 12],
            _ => hour == 12 ? [12, 0] : [hour, hour + 12],
        };
    }

    private static void Resolve(TimeFound found, DateTime now, DateTime? day, (int Hour, int Minute, bool Zero)? clock, DayPart part)
    {
        if (clock is { Hour: < 0 })
        {
            found.Invalid = true;
            return;
        }
        if (clock is null && day is null && part == DayPart.None) return;

        int[] hours;
        var minute = 0;
        if (clock is { } c)
        {
            hours = Hours(c.Hour, part, c.Zero);
            minute = c.Minute;
        }
        else
        {
            // a day or a day part without a time: «فردا صبح» → 8:00, «فردا» → 9:00
            if (found.Duration is not null && day is null) return; // «۲۰ دقیقه دیگه شب بخیر» is a delay
            hours = [DefaultHour(part)];
            found.Defaulted = true;
        }

        var moments = new List<DateTime>();
        foreach (var h in hours)
        {
            DateTime at;
            if (day is { } d)
            {
                at = d.AddHours(h).AddMinutes(minute);
                // «امشب ساعت ۲»، «فردا شب ساعت ۱» mean after that midnight
                if (part is DayPart.Night or DayPart.Midnight && h < 6) at = at.AddDays(1);
                if (at <= now)
                {
                    // «فردا» never ends up in the past; «امروز ساعت ۸» at 21:00 does
                    if (clock is null && part == DayPart.None) continue;
                    if (hours.Length > 1) continue;
                    found.InPast = true;
                    continue;
                }
            }
            else if (found.Repeat.Count > 0)
            {
                at = Schedule.Next(h, minute, found.Repeat, now);
            }
            else
            {
                at = Schedule.Next(h, minute, [], now);
            }
            moments.Add(at);
        }
        if (moments.Count == 0 && hours.Length > 1 && day is not null) found.InPast = true;
        found.Moments.AddRange(moments.Distinct().OrderBy(m => m));
        if (found.Moments.Count > 0) found.InPast = false;
    }
}
