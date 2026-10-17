using System.Globalization;

namespace Shabpa.Core;

/// <summary>Birthdays: the person's own (if they told Shabpa in Settings) and the maker's, 26 Mehr. On either day the
/// window greets with a small party once. Birthdays in Iran follow the Jalali calendar, so a Persian birthday is kept as a
/// Jalali month and day and lands on the right Gregorian day every year.</summary>
public static class Birthday
{
    public enum Kind { None, Mine, Maker }

    private static readonly PersianCalendar Jalali = new();

    /// <summary>Mr.PitPot, who made Shabpa: 26 Mehr.</summary>
    public const int MakerMonth = 7, MakerDay = 26;

    /// <summary>"fa:7-26" (Jalali month-day) or "en:10-18" (Gregorian month-day); empty = not set.</summary>
    public static string Format(bool jalali, int month, int day) => $"{(jalali ? "fa" : "en")}:{month}-{day}";

    public static bool TryParse(string value, out bool jalali, out int month, out int day)
    {
        jalali = false;
        month = day = 0;
        if (value.Split(':') is not [var cal, var rest] || rest.Split('-') is not [var m, var d]) return false;
        if (cal is not ("fa" or "en") || !int.TryParse(m, out month) || !int.TryParse(d, out day)) return false;
        jalali = cal == "fa";
        return IsValid(jalali, month, day);
    }

    /// <summary>Jalali: the first six months have 31 days, the next five 30, Esfand 30 (29 outside leap years).
    /// Gregorian: 29 February is allowed and celebrated on 28 February in other years.</summary>
    public static bool IsValid(bool jalali, int month, int day) =>
        month is >= 1 and <= 12 && day >= 1 && day <= (jalali ? (month <= 6 ? 31 : 30) : DateTime.DaysInMonth(2024, month));

    public static bool Matches(DateTime date, bool jalali, int month, int day)
    {
        int m, d, lastDay;
        if (jalali) { m = Jalali.GetMonth(date); d = Jalali.GetDayOfMonth(date); lastDay = Jalali.GetDaysInMonth(Jalali.GetYear(date), m); }
        else { m = date.Month; d = date.Day; lastDay = DateTime.DaysInMonth(date.Year, m); }
        // born on a day this year does not have (30 Esfand, 29 February): the last day of that month
        return m == month && (d == day || (day > lastDay && d == lastDay));
    }

    /// <summary>Whose birthday <paramref name="date"/> is. The person's own wins when both fall on the same day.</summary>
    public static Kind On(DateTime date, string mine)
    {
        if (TryParse(mine, out var jalali, out var month, out var day) && Matches(date, jalali, month, day)) return Kind.Mine;
        return Matches(date, true, MakerMonth, MakerDay) ? Kind.Maker : Kind.None;
    }

    /// <summary>The party to show now, or None when there is none today or it was already shown today.</summary>
    public static Kind Due(DateTime now, Settings s) =>
        s.BirthdayShown == now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ? Kind.None : On(now, s.Birthday);

    public static void MarkShown(DateTime now, Settings s) => s.BirthdayShown = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static (string title, string text) Message(Kind kind) => kind switch
    {
        Kind.Mine => (L.T("تولدت مبارک! 🎂", "Happy birthday! 🎂"),
            L.T("امروز روز توئه و شب‌پا هم براش جشن گرفته. یه سال پر از خنده، بازی‌های بی‌لگ و شب‌های آروم برات آرزو می‌کنیم 🎉",
                "Today is your day, and Shabpa is celebrating too. Here's to a year full of laughs, lag-free games and calm nights 🎉")),
        Kind.Maker => (L.T("تولدت مبارک Mr.PitPot! 🎂", "Happy birthday, Mr.PitPot! 🎂"),
            L.T("امروز تولد سازنده‌ی شب‌پاست. اگه شب‌پا یه شب کارت رو راه انداخته، یه تبریک توی کانال @ShabPa_App براش بفرست 🌙",
                "Today is the birthday of the person who made Shabpa. If it ever saved your night, send a happy birthday on the @ShabPa_App channel 🌙")),
        _ => ("", ""),
    };
}
