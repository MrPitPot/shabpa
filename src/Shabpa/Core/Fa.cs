using System.Globalization;
using System.Text;

namespace Shabpa.Core;

/// <summary>Formatting for people: digits, dates and durations in the current language
/// (Persian digits and Jalali dates, or Latin digits and Gregorian dates).</summary>
public static class Fa
{
    private static readonly PersianCalendar Calendar = new();
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static readonly string[] Months =
    [
        "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
        "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند",
    ];

    private static readonly DayOfWeek[] SaturdayFirst =
    [
        DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday,
        DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday,
    ];

    private static readonly DayOfWeek[] MondayFirst =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    ];

    /// <summary>Week order used across the UI: Saturday first in Persian, Monday first in English.</summary>
    public static DayOfWeek[] Week => L.En ? MondayFirst : SaturdayFirst;

    public static string Digits(string text)
    {
        if (L.En) return text;
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
            sb.Append(c is >= '0' and <= '9' ? (char)('۰' + (c - '0')) : c);
        return sb.ToString();
    }

    public static string N(long value)
    {
        var text = value.ToString("N0", Inv);
        return L.En ? text : Digits(text).Replace(',', '٬');
    }

    public static string N(double value, int decimals)
    {
        var text = value.ToString("F" + decimals, Inv);
        return L.En ? text : Digits(text).Replace('.', '٫');
    }

    public static string Clock(DateTime t) => Digits(t.ToString("HH:mm", Inv));

    public static string Clock(int hour, int minute) => Digits($"{hour:00}:{minute:00}");

    public static string MonthName(DateTime d) => L.En ? d.ToString("MMMM", Inv) : Months[Calendar.GetMonth(d) - 1];

    public static int DayOfMonth(DateTime d) => L.En ? d.Day : Calendar.GetDayOfMonth(d);

    /// <summary>Midnight of the first day of the month <paramref name="d"/> falls in, in the calendar the user sees
    /// (Persian months in Persian, calendar months in English).</summary>
    public static DateTime MonthStart(DateTime d) => L.En ? new DateTime(d.Year, d.Month, 1) : d.Date.AddDays(1 - Calendar.GetDayOfMonth(d));

    /// <summary>e.g. «۲۲ مهر» or "Oct 14".</summary>
    public static string Date(DateTime d) =>
        L.En ? d.ToString("MMM d", Inv) : $"{Digits(Calendar.GetDayOfMonth(d).ToString())} {Months[Calendar.GetMonth(d) - 1]}";

    public static string DayName(DayOfWeek d) => L.En
        ? d.ToString()
        : d switch
        {
            DayOfWeek.Saturday => "شنبه",
            DayOfWeek.Sunday => "یکشنبه",
            DayOfWeek.Monday => "دوشنبه",
            DayOfWeek.Tuesday => "سه‌شنبه",
            DayOfWeek.Wednesday => "چهارشنبه",
            DayOfWeek.Thursday => "پنجشنبه",
            _ => "جمعه",
        };

    public static string DayLetter(DayOfWeek d) => L.En
        ? d.ToString()[..2]
        : d switch
        {
            DayOfWeek.Saturday => "ش",
            DayOfWeek.Sunday => "ی",
            DayOfWeek.Monday => "د",
            DayOfWeek.Tuesday => "س",
            DayOfWeek.Wednesday => "چ",
            DayOfWeek.Thursday => "پ",
            _ => "ج",
        };

    /// <summary>«امروز»، «دیروز»، «فردا» or the weekday name.</summary>
    public static string RelativeDay(DateTime day, DateTime now)
    {
        var diff = (day.Date - now.Date).Days;
        return diff switch
        {
            0 => L.T("امروز", "Today"),
            -1 => L.T("دیروز", "Yesterday"),
            1 => L.T("فردا", "Tomorrow"),
            _ => DayName(day.DayOfWeek),
        };
    }

    public static string Days(IReadOnlyCollection<DayOfWeek> days)
    {
        if (days.Count is 0 or 7) return L.T("هر روز", "Every day");
        return string.Join(L.Comma, Week.Where(days.Contains).Select(d => L.En ? DayLetter(d) : DayName(d)));
    }

    /// <summary>e.g. «۲ ساعت و ۵ دقیقه»، «۴۰ ثانیه» or "2 h 5 min", "40 s".</summary>
    public static string Duration(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        if (t.TotalSeconds < 60)
        {
            var seconds = ((int)Math.Ceiling(t.TotalSeconds)).ToString();
            return L.En ? $"{seconds} s" : $"{Digits(seconds)} ثانیه";
        }
        var parts = new List<string>(3);
        if (t.Days > 0) parts.Add(L.En ? $"{t.Days} d" : $"{Digits(t.Days.ToString())} روز");
        if (t.Hours > 0) parts.Add(L.En ? $"{t.Hours} h" : $"{Digits(t.Hours.ToString())} ساعت");
        if (t.Minutes > 0 && t.Days == 0) parts.Add(L.En ? $"{t.Minutes} min" : $"{Digits(t.Minutes.ToString())} دقیقه");
        return string.Join(L.En ? " " : " و ", parts);
    }

    /// <summary>mm:ss or h:mm:ss countdown text.</summary>
    public static string Countdown(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        var text = t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes:00}:{t.Seconds:00}";
        return Digits(text);
    }

    public static string ActionName(PowerAction a) => a switch
    {
        PowerAction.Sleep => L.T("خواب", "Sleep"),
        PowerAction.Hibernate => L.T("هایبرنیت", "Hibernate"),
        PowerAction.Shutdown => L.T("خاموش کردن", "Shut down"),
        PowerAction.Restart => L.T("راه‌اندازی مجدد", "Restart"),
        _ => L.T("قفل صفحه", "Lock screen"),
    };

    /// <summary>Sentence form: «سیستم خاموش می‌شه» / "the PC shuts down".</summary>
    public static string ActionSentence(PowerAction a) => a switch
    {
        PowerAction.Sleep => L.T("سیستم می‌خوابه", "the PC goes to sleep"),
        PowerAction.Hibernate => L.T("سیستم هایبرنیت می‌شه", "the PC hibernates"),
        PowerAction.Shutdown => L.T("سیستم خاموش می‌شه", "the PC shuts down"),
        PowerAction.Restart => L.T("سیستم ری‌استارت می‌شه", "the PC restarts"),
        _ => L.T("صفحه قفل می‌شه", "the screen locks"),
    };

    /// <summary>Latin, for left-to-right figures: "1.3 GB".</summary>
    public static string Bytes(long bytes)
    {
        double value = bytes;
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var unit = 0;
        while (value >= 1000 && unit < units.Length - 1) { value /= 1024; unit++; }
        return value.ToString(unit == 0 || value >= 100 ? "0" : "0.0", Inv) + " " + units[unit];
    }

    /// <summary>Latin, for left-to-right figures: "8.5 Mb/s", "410 Kb/s".</summary>
    public static string Rate(double mbps)
    {
        if (mbps < 0.001) return "0";
        return mbps >= 1
            ? mbps.ToString(mbps >= 100 ? "0" : "0.0", Inv) + " Mb/s"
            : (mbps * 1000).ToString("0", Inv) + " Kb/s";
    }

    public static string Speed(double mbps) =>
        mbps >= 10 ? N(mbps, 0) : mbps >= 1 ? N(mbps, 1) : N(mbps, 2);

    /// <summary>A megabit figure without trailing zeros: «۰٫۵», "2".</summary>
    public static string Mbit(double mbps) => N(mbps, mbps % 1 == 0 ? 0 : mbps * 10 % 1 == 0 ? 1 : 2);
}
