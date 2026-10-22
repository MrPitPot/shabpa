using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shabpa.Core;

/// <summary>The optional name and birthday. Day and month are kept in the calendar the person chose (a Jalali birthday is
/// celebrated on the same Jalali day every year), the year is optional. Loaded tolerantly: a value of the wrong type or out
/// of range is ignored instead of making the whole state file unreadable.</summary>
[JsonConverter(typeof(BirthdaySettingsConverter))]
public sealed class BirthdaySettings
{
    public const int MaxName = 30;

    public string Name { get; set; } = "";
    /// <summary>true = Jalali (Persian) calendar, false = Gregorian.</summary>
    public bool Jalali { get; set; } = true;
    /// <summary>1-12, 0 = not set.</summary>
    public int Month { get; set; }
    public int Day { get; set; }
    /// <summary>Birth year in the chosen calendar, 0 = not given. Not needed for the greeting.</summary>
    public int Year { get; set; }
    /// <summary>Use the festive look on the birthday.</summary>
    public bool Theme { get; set; } = true;
    /// <summary>"yyyy-MM-dd" of the day the greeting window was shown, so it appears once a day.</summary>
    public string LastGreeted { get; set; } = "";
    /// <summary>"yyyy-MM-dd" of the day the tray notice was shown (window hidden).</summary>
    public string LastBalloon { get; set; } = "";

    public bool HasDate => Month is >= 1 and <= 12 && Day >= 1 && Day <= Birthday.MaxDay(Jalali, Month);

    public bool IsEmpty => Name.Length == 0 && !HasDate;
}

public sealed class BirthdaySettingsConverter : JsonConverter<BirthdaySettings>
{
    public override BirthdaySettings Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var b = new BirthdaySettings();
        using var doc = JsonDocument.ParseValue(ref reader);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return b;
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            var v = p.Value;
            switch (p.Name.ToLowerInvariant())
            {
                case "name" when v.ValueKind == JsonValueKind.String: b.Name = Birthday.CleanName(v.GetString()); break;
                case "jalali" when v.ValueKind is JsonValueKind.True or JsonValueKind.False: b.Jalali = v.GetBoolean(); break;
                case "theme" when v.ValueKind is JsonValueKind.True or JsonValueKind.False: b.Theme = v.GetBoolean(); break;
                case "month" when v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var m): b.Month = m; break;
                case "day" when v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var d): b.Day = d; break;
                case "year" when v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var y): b.Year = y; break;
                case "lastgreeted" when v.ValueKind == JsonValueKind.String: b.LastGreeted = Birthday.CleanKey(v.GetString()); break;
                case "lastballoon" when v.ValueKind == JsonValueKind.String: b.LastBalloon = Birthday.CleanKey(v.GetString()); break;
            }
        }
        if (!b.HasDate) { b.Month = 0; b.Day = 0; b.Year = 0; }
        else if (b.Year != 0 && !Birthday.YearOk(b)) b.Year = 0;
        return b;
    }

    public override void Write(Utf8JsonWriter w, BirthdaySettings b, JsonSerializerOptions options)
    {
        w.WriteStartObject();
        w.WriteString("Name", b.Name);
        w.WriteBoolean("Jalali", b.Jalali);
        w.WriteNumber("Month", b.Month);
        w.WriteNumber("Day", b.Day);
        w.WriteNumber("Year", b.Year);
        w.WriteBoolean("Theme", b.Theme);
        w.WriteString("LastGreeted", b.LastGreeted);
        w.WriteString("LastBalloon", b.LastBalloon);
        w.WriteEndObject();
    }
}

/// <summary>Birthday logic (pure, no WPF): which day counts, the greeting texts and the festive palette.</summary>
public static class Birthday
{
    private static readonly PersianCalendar Persian = new();
    private static readonly GregorianCalendar Gregorian = new();

    public static readonly string[] JalaliMonths =
    [
        "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند",
    ];

    private static readonly string[] GregorianFa =
    [
        "ژانویه", "فوریه", "مارس", "آوریل", "مه", "ژوئن", "ژوئیه", "اوت", "سپتامبر", "اکتبر", "نوامبر", "دسامبر",
    ];

    public static string MonthName(bool jalali, int month) =>
        jalali ? (L.En ? JalaliEn[month - 1] : JalaliMonths[month - 1])
               : (L.En ? CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month) : GregorianFa[month - 1]);

    private static readonly string[] JalaliEn =
    [
        "Farvardin", "Ordibehesht", "Khordad", "Tir", "Mordad", "Shahrivar", "Mehr", "Aban", "Azar", "Dey", "Bahman", "Esfand",
    ];

    // ---- the clock (with a test aid) ------------------------------------------------------------------------
    /// <summary>The current local time. Debug builds and builds with SHABPA_TESTHOOKS honour SHABPA_FAKE_TODAY
    /// ("yyyy-MM-dd" keeps the real time of day, "yyyy-MM-ddTHH:mm:ss" sets the clock); everywhere else this is DateTime.Now.</summary>
    public static DateTime Now()
    {
#if DEBUG || SHABPA_TESTHOOKS
        if (_offset is null)
        {
            var text = Environment.GetEnvironmentVariable("SHABPA_FAKE_TODAY");
            var real = DateTime.Now;
            if (!string.IsNullOrWhiteSpace(text) && DateTime.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var fake))
                _offset = text.Trim().Length <= 10 ? fake.Date + real.TimeOfDay - real : fake - real;
            else _offset = TimeSpan.Zero;
        }
        return DateTime.Now + _offset.Value;
#else
        return DateTime.Now;
#endif
    }

#if DEBUG || SHABPA_TESTHOOKS
    /// <summary>Test aid: makes the clock read <paramref name="now"/> from here on (it keeps running).</summary>
    public static void FakeClock(DateTime now) => _offset = now - DateTime.Now;
#endif

    // ---- calendar arithmetic ---------------------------------------------------------------------------------

    /// <summary>The longest this month can be (Esfand 30 and Feb 29 exist in leap years).</summary>
    public static int MaxDay(bool jalali, int month) =>
        jalali ? (month <= 6 ? 31 : 30) : month == 2 ? 29 : month is 4 or 6 or 9 or 11 ? 30 : 31;

    public static int DaysIn(bool jalali, int year, int month) =>
        jalali ? Persian.GetDaysInMonth(year, month) : Gregorian.GetDaysInMonth(year, month);

    public static int YearOf(bool jalali, DateTime date) => jalali ? Persian.GetYear(date) : date.Year;

    public static int MonthOf(bool jalali, DateTime date) => jalali ? Persian.GetMonth(date) : date.Month;

    public static int DayOf(bool jalali, DateTime date) => jalali ? Persian.GetDayOfMonth(date) : date.Day;

    private static DateTime Make(bool jalali, int year, int month, int day) =>
        jalali ? Persian.ToDateTime(year, month, day, 0, 0, 0, 0) : new DateTime(year, month, day);

    /// <summary>Whether the optional birth year is plausible and the date existed in it (no Esfand 30 in a non-leap year).</summary>
    public static bool YearOk(BirthdaySettings b)
    {
        if (b.Year == 0) return true;
        var (lo, hi) = b.Jalali ? (1200, 1500) : (1800, 2200);
        return b.Year >= lo && b.Year <= hi && b.HasDate && b.Day <= DaysIn(b.Jalali, b.Year, b.Month);
    }

    /// <summary>The birthday in the given year of the chosen calendar. In a year where the month is too short (Esfand 30,
    /// Feb 29) it is the last day of that month. Null when no date is set.</summary>
    public static DateTime? Occurrence(BirthdaySettings b, int year)
    {
        if (!b.HasDate) return null;
        try { return Make(b.Jalali, year, b.Month, Math.Min(b.Day, DaysIn(b.Jalali, year, b.Month))); }
        catch (ArgumentException) { return null; }
    }

    public static bool IsToday(BirthdaySettings b, DateTime now) =>
        b.HasDate && Occurrence(b, YearOf(b.Jalali, now)) is { } d && d.Date == now.Date;

    /// <summary>The next day on which the birthday falls, today included.</summary>
    public static DateTime? Next(BirthdaySettings b, DateTime now)
    {
        if (!b.HasDate) return null;
        var year = YearOf(b.Jalali, now);
        if (Occurrence(b, year) is { } d && d.Date >= now.Date) return d.Date;
        return Occurrence(b, year + 1)?.Date;
    }

    /// <summary>The festive look is on for this local day.</summary>
    public static bool LookFor(BirthdaySettings b, DateTime now) => b.Theme && IsToday(b, now);

    public static string Key(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>True while the greeting window is still owed for this local day.</summary>
    public static bool GreetingDue(BirthdaySettings b, DateTime now) => IsToday(b, now) && b.LastGreeted != Key(now);

    /// <summary>Moves the date to the other calendar (the day it falls on this year), so switching calendars keeps the same
    /// birthday. The year, if given, is converted by the same day of that year.</summary>
    public static void Convert(BirthdaySettings b, bool toJalali, DateTime now)
    {
        if (b.Jalali == toJalali) return;
        if (b.HasDate)
        {
            var day = Occurrence(b, YearOf(b.Jalali, now));
            if (day is { } d)
            {
                (var m, var dd) = (MonthOf(toJalali, d), DayOf(toJalali, d));
                if (b.Year != 0 && YearOk(b)) b.Year = YearOf(toJalali, Make(b.Jalali, b.Year, b.Month, b.Day));
                (b.Month, b.Day) = (m, dd);
            }
        }
        b.Jalali = toJalali;
        if (!YearOk(b)) b.Year = 0;
    }

    public static DateTime NextMidnight(DateTime now) => now.Date.AddDays(1);

    // ---- text ------------------------------------------------------------------------------------------------

    /// <summary>Trims, collapses spaces, drops control characters, keeps at most 30 characters (never cutting a character in half).</summary>
    public static string CleanName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var sb = new StringBuilder();
        var space = false;
        foreach (var ch in raw)
        {
            if (char.IsControl(ch) && !char.IsWhiteSpace(ch)) continue;
            if (char.IsWhiteSpace(ch)) { space = sb.Length > 0; continue; }
            if (space) { sb.Append(' '); space = false; }
            sb.Append(ch);
        }
        var text = sb.ToString().Trim();
        var info = new StringInfo(text);
        return info.LengthInTextElements <= BirthdaySettings.MaxName ? text : info.SubstringByTextElements(0, BirthdaySettings.MaxName).TrimEnd();
    }

    public static string CleanKey(string? raw) =>
        DateTime.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? Key(d) : "";

    /// <summary>Persian, Arabic-Indic and Latin digits to Latin; anything else is dropped. For the day and year boxes.</summary>
    public static string LatinDigits(string? raw)
    {
        var sb = new StringBuilder();
        foreach (var ch in raw ?? "")
        {
            if (ch is >= '0' and <= '9') sb.Append(ch);
            else if (ch is >= '۰' and <= '۹') sb.Append((char)('0' + (ch - '۰')));
            else if (ch is >= '٠' and <= '٩') sb.Append((char)('0' + (ch - '٠')));
        }
        return sb.ToString();
    }

    /// <summary>"تولدت مبارک {name} 🎂" / "Happy birthday, {name}!" (gender-neutral; without a name the name part is left out).</summary>
    public static string Title(string name, bool? english = null)
    {
        var en = english ?? L.En;
        name = CleanName(name);
        if (en) return name.Length > 0 ? $"Happy birthday, {name}!" : "Happy birthday!";
        return name.Length > 0 ? $"تولدت مبارک {name} 🎂" : "تولدت مبارک 🎂";
    }

    /// <summary>The line at the top of the home page on the day.</summary>
    public static string HomeLine(string name, bool? english = null)
    {
        var en = english ?? L.En;
        name = CleanName(name);
        if (en) return name.Length > 0 ? $"🎂 Happy birthday, {name}!" : "🎂 Happy birthday!";
        return name.Length > 0 ? $"🎂 تولدت مبارک {name}" : "🎂 تولدت مبارک";
    }

    public static string Wish(bool? english = null) => (english ?? L.En)
        ? "Wishing you a year full of good news, good health and good company. Shabpa is happy to be on your PC today."
        : "امیدوارم سال تازه‌ات پر از خبرهای خوب، سلامتی و لحظه‌های شاد باشه. شب‌پا هم خوشحاله که امروز کنارته 🎈";

    // ---- the festive palette ---------------------------------------------------------------------------------

    private static readonly Dictionary<string, Palette> Tints = [];

    /// <summary>The same theme with a warm pink, gold and violet accent set, for the birthday only. It has its own key
    /// (so the glass tokens are derived from its colours) and remembers the real theme in <see cref="Palette.BaseKey"/>.
    /// The saved theme choice is never touched.</summary>
    public static Palette Tint(Palette basePalette)
    {
        if (basePalette.BaseKey is { } real) basePalette = Palette.Find(real);
        lock (Tints)
        {
            if (Tints.TryGetValue(basePalette.Key, out var cached)) return cached;
            var c = new Dictionary<string, string>(basePalette.Colors);
            void Warm(string key, uint towards, double t) => c[key] = GlassTokens.Mix(GlassTokens.Rgb(c[key]), towards, t).ToString("X6", CultureInfo.InvariantCulture);
            if (!basePalette.Light)
            {
                foreach (var key in new[] { "Bg", "Bg2", "Side", "Side2" }) Warm(key, 0x2A1034, 0.30);
                foreach (var key in new[] { "Panel", "Panel2", "Panel3", "Thumb", "Line" }) Warm(key, 0x4A1E56, 0.26);
                Set(c, "Accent=FF8CC6 Accent1=FF6FB5 Accent2=B86BFF AccentSoft=441A47 OnAccent=2B0E2E Halo=FF5FA8 Glow=FFFFFF " +
                       "Gold=FFD36B GoldSoft=45321A Hero=51195A Hero2=2F1B66");
            }
            else
            {
                foreach (var key in new[] { "Bg", "Bg2", "Side", "Side2", "Panel2", "Panel3", "Line" }) Warm(key, 0xFFD6EA, 0.22);
                Set(c, "Accent=C2257A Accent1=E0488F Accent2=8E24AA AccentSoft=FFE0F0 OnAccent=FFFFFF Halo=FF8FC8 Glow=000000 " +
                       "Gold=8A5600 GoldSoft=FFEFC2 Hero=FFE0F0 Hero2=EBD7FF");
            }
            var tinted = new Palette(basePalette.Key + "-birthday", basePalette.Fa, basePalette.En, basePalette.Light, c) { BaseKey = basePalette.Key };
            return Tints[basePalette.Key] = tinted;
        }

        static void Set(Dictionary<string, string> colors, string spec)
        {
            foreach (var pair in spec.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=');
                colors[kv[0]] = kv[1];
            }
        }
    }
}
