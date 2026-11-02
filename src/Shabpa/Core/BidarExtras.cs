using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Shabpa.Core;

/// <summary>Small things Bidar can work out from what is typed, in Persian or English: a strong password, unit conversion, the time in another
/// city, days until an occasion, when to sleep, a random number, a choice between options, and the battery. Each is recognised only by its own
/// clear words, so the power and time commands keep reading exactly as before. <see cref="Match"/> only reads the text (pure); <see cref="Answer"/>
/// builds the reply in the current language (<see cref="L.Scope"/>).</summary>
public static class BidarExtras
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static (string Kind, string Arg)? Match(string? text, DateTime now)
    {
        var t = Clean(text);
        if (t.Length == 0 || t.Length > 160) return null;
        return Password(t) ?? Convert(t) ?? WorldTime(t) ?? Countdown(t) ?? Bedtime(t) ?? RandomNumber(t) ?? Choose(t, text!) ?? Battery(t);
    }

    public static string Answer(string kind, string arg, DateTime now, bool remote = false) => kind switch
    {
        "password" => PasswordText(arg, remote),
        "convert" => ConvertText(arg),
        "world" => WorldText(arg),
        "until" => UntilText(arg, now),
        "bed" => BedText(arg, now),
        "random" => RandomText(arg),
        "choose" => ChooseText(arg),
        "battery" => BatteryText(),
        _ => L.T("نمی‌دونم چی بگم.", "I do not know what to say."),
    };

    // ---- reading ------------------------------------------------------------------------------------------------------

    /// <summary>Lower case, Persian and Arabic digits and letters made plain, joiners removed, the question mark and the end dots dropped.</summary>
    internal static string Clean(string? text)
    {
        var t = CommandText.Normalize(text).Trim().TrimEnd('?', '!', '.', ' ');
        return Regex.Replace(t, @"\s+", " ");
    }

    private static bool Has(string t, params string[] words) => words.Any(w => Regex.IsMatch(t, @"(?<![\p{L}\d])" + Regex.Escape(CommandText.Normalize(w).Trim()) + @"(?![\p{L}\d])"));

    private static bool HasPart(string t, params string[] parts) => parts.Any(p => t.Contains(CommandText.Normalize(p).Trim(), StringComparison.Ordinal));

    // ---- password -----------------------------------------------------------------------------------------------------

    private static (string, string)? Password(string t)
    {
        var noun = Has(t, "password", "passwords", "passcode", "پسورد", "رمز", "رمزعبور", "رمز عبور", "پسوورد");
        var verb = HasPart(t, "بساز", "بده", "تولید", "پیشنهاد", "بسازی", "بسازید", "generate", "make", "create", "give me", "suggest", "random", "strong", "new", "تصادفی", "قوی", "جدید");
        if (!noun || !verb || t.Split(' ').Length > 9) return null;
        var m = Regex.Match(t, @"\b(\d{1,2})\b");
        var length = m.Success && int.TryParse(m.Groups[1].Value, out var n) ? Math.Clamp(n, 8, 64) : 16;
        return ("password", length.ToString(Inv));
    }

    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ", Lower = "abcdefghijkmnopqrstuvwxyz", Digit = "23456789", Symbol = "!@#$%&*?-_+=";

    /// <summary>A random password with at least one of each kind of character (look-alikes such as 0/O and l/1 are left out). Uses the system's
    /// secure random source.</summary>
    public static string MakePassword(int length)
    {
        length = Math.Clamp(length, 8, 64);
        var all = Upper + Lower + Digit + Symbol;
        var chars = new List<char> { Pick(Upper), Pick(Lower), Pick(Digit), Pick(Symbol) };
        while (chars.Count < length) chars.Add(Pick(all));
        for (var i = chars.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string([.. chars]);
    }

    private static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];

    private static string PasswordText(string arg, bool remote)
    {
        if (remote) return L.T("پسورد رو توی تلگرام نمی‌فرستم، چون پیام‌ها از جاهای دیگه رد می‌شن. همین رو توی خود شب‌پا بخواه.", "I will not send a password through Telegram because messages pass through other servers. Ask me inside Shabpa instead.");
        var length = int.TryParse(arg, out var n) ? n : 16;
        return L.T($"یه پسورد قوی ({Fa.Digits(length.ToString())} حرفی): {MakePassword(length)}\nجایی امن ذخیره‌ش کن (مثلاً توی یه مدیر رمز)؛ من نگهش نمی‌دارم.",
            $"A strong password ({length} characters): {MakePassword(length)}\nSave it somewhere safe (a password manager, for example); I do not keep it.");
    }

    // ---- units --------------------------------------------------------------------------------------------------------

    internal sealed record Unit(string Dim, string Key, double Factor, string Fa, string En, string[] Names);

    private static readonly Unit[] Units =
    [
        new("len", "m", 1, "متر", "m", ["m", "meter", "meters", "metre", "metres", "متر"]),
        new("len", "km", 1000, "کیلومتر", "km", ["km", "kilometer", "kilometers", "kilometre", "kilometres", "کیلومتر"]),
        new("len", "cm", 0.01, "سانتی‌متر", "cm", ["cm", "centimeter", "centimeters", "centimetre", "centimetres", "سانتیمتر", "سانت"]),
        new("len", "mm", 0.001, "میلی‌متر", "mm", ["mm", "millimeter", "millimeters", "millimetre", "millimetres", "میلیمتر"]),
        new("len", "mi", 1609.344, "مایل", "mi", ["mi", "mile", "miles", "مایل"]),
        new("len", "ft", 0.3048, "فوت", "ft", ["ft", "foot", "feet", "فوت"]),
        new("len", "inch", 0.0254, "اینچ", "in", ["inch", "inches", "اینچ"]),
        new("len", "yd", 0.9144, "یارد", "yd", ["yd", "yard", "yards", "یارد"]),
        new("mass", "kg", 1, "کیلوگرم", "kg", ["kg", "kilogram", "kilograms", "kilo", "kilos", "کیلوگرم", "کیلو"]),
        new("mass", "g", 0.001, "گرم", "g", ["g", "gram", "grams", "گرم"]),
        new("mass", "lb", 0.45359237, "پوند", "lb", ["lb", "lbs", "pound", "pounds", "پوند"]),
        new("mass", "oz", 0.028349523125, "اونس", "oz", ["oz", "ounce", "ounces", "اونس"]),
        new("mass", "ton", 1000, "تن", "t", ["ton", "tons", "tonne", "tonnes", "تن"]),
        new("vol", "l", 1, "لیتر", "L", ["l", "liter", "liters", "litre", "litres", "لیتر"]),
        new("vol", "ml", 0.001, "میلی‌لیتر", "mL", ["ml", "milliliter", "milliliters", "millilitre", "millilitres", "میلیلیتر"]),
        new("vol", "gal", 3.785411784, "گالن", "gal", ["gal", "gallon", "gallons", "گالن"]),
        new("data", "byte", 1, "بایت", "B", ["byte", "bytes", "بایت"]),
        new("data", "kb", 1024, "کیلوبایت", "KB", ["kb", "kilobyte", "kilobytes", "کیلوبایت"]),
        new("data", "mb", 1048576, "مگابایت", "MB", ["mb", "megabyte", "megabytes", "مگابایت", "مگ"]),
        new("data", "gb", 1073741824, "گیگابایت", "GB", ["gb", "gigabyte", "gigabytes", "گیگابایت", "گیگ"]),
        new("data", "tb", 1099511627776, "ترابایت", "TB", ["tb", "terabyte", "terabytes", "ترابایت"]),
        new("speed", "ms", 1, "متر بر ثانیه", "m/s", ["m/s", "mps", "متر بر ثانیه"]),
        new("speed", "kmh", 1 / 3.6, "کیلومتر بر ساعت", "km/h", ["km/h", "kmh", "kph", "کیلومتر بر ساعت"]),
        new("speed", "mph", 0.44704, "مایل بر ساعت", "mph", ["mph", "مایل بر ساعت"]),
        new("temp", "c", 1, "درجه‌ی سانتی‌گراد", "°C", ["c", "°c", "celsius", "centigrade", "سانتیگراد", "درجه سانتیگراد", "سلسیوس"]),
        new("temp", "f", 1, "درجه‌ی فارنهایت", "°F", ["f", "°f", "fahrenheit", "فارنهایت", "درجه فارنهایت"]),
        new("temp", "k", 1, "کلوین", "K", ["k", "kelvin", "کلوین"]),
    ];

    private static readonly Dictionary<string, Unit> UnitByName = Units.SelectMany(u => u.Names.Select(n => (n: CommandText.Normalize(n).Trim(), u))).ToDictionary(x => x.n, x => x.u);

    private static readonly Regex ConvertRx = new(
        @"^(?:convert\s+|تبدیل(?:\s+کن)?\s+|what\s+is\s+|what's\s+|how\s+much\s+is\s+)?(?<n>-?\d+(?:\.\d+)?)\s*(?<from>" + string.Join("|", UnitByName.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape))
        + @")\s+(?:(?:to|in|into|as|به|تو|توی|=)\s*(?:how\s+many\s+)?|چند\s*|how\s+many\s+)(?<to>" + string.Join("|", UnitByName.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape)) + @")(?:ه|ست)?$",
        RegexOptions.Compiled);

    private static (string, string)? Convert(string t)
    {
        var m = ConvertRx.Match(t);
        if (!m.Success) return null;
        var from = UnitByName[m.Groups["from"].Value];
        var to = UnitByName[m.Groups["to"].Value];
        if (from.Dim != to.Dim || from.Key == to.Key) return null;
        return ("convert", $"{m.Groups["n"].Value}|{from.Key}|{to.Key}");
    }

    internal static double? ConvertValue(double value, Unit from, Unit to)
    {
        if (from.Dim == "temp")
        {
            var c = from.Key switch { "f" => (value - 32) * 5 / 9, "k" => value - 273.15, _ => value };
            return to.Key switch { "f" => c * 9 / 5 + 32, "k" => c + 273.15, _ => c };
        }
        return value * from.Factor / to.Factor;
    }

    private static string Number(double v) => Math.Round(v, 4).ToString("0.####", Inv);

    private static string ConvertText(string arg)
    {
        var p = arg.Split('|');
        var from = Units.First(u => u.Key == p[1]);
        var to = Units.First(u => u.Key == p[2]);
        var value = double.Parse(p[0], Inv);
        var result = ConvertValue(value, from, to)!.Value;
        if (from.Dim == "temp" && to.Key == "k" && result < 0 || from.Dim == "temp" && from.Key == "k" && value < 0)
            return L.T("این عدد زیر صفر مطلقه و وجود نداره.", "That is below absolute zero and cannot exist.");
        return L.T($"{Fa.Digits(Number(value))} {from.Fa} = {Fa.Digits(Number(result))} {to.Fa}", $"{Number(value)} {from.En} = {Number(result)} {to.En}");
    }

    // ---- time in another city -----------------------------------------------------------------------------------------

    private static readonly (string[] Names, string Zone, string Fa, string En)[] Cities =
    [
        (["tehran", "تهران", "ایران", "iran"], "Iran Standard Time", "تهران", "Tehran"),
        (["london", "لندن"], "GMT Standard Time", "لندن", "London"),
        (["paris", "پاریس"], "Romance Standard Time", "پاریس", "Paris"),
        (["berlin", "برلین", "آلمان", "المان"], "W. Europe Standard Time", "برلین", "Berlin"),
        (["rome", "ایتالیا", "italy"], "W. Europe Standard Time", "رم", "Rome"),
        (["madrid", "مادرید", "اسپانیا"], "Romance Standard Time", "مادرید", "Madrid"),
        (["istanbul", "استانبول", "ترکیه"], "Turkey Standard Time", "استانبول", "Istanbul"),
        (["moscow", "مسکو", "روسیه"], "Russian Standard Time", "مسکو", "Moscow"),
        (["dubai", "دبی", "امارات"], "Arabian Standard Time", "دبی", "Dubai"),
        (["delhi", "دهلی", "هند", "india"], "India Standard Time", "دهلی", "Delhi"),
        (["beijing", "پکن", "چین", "china"], "China Standard Time", "پکن", "Beijing"),
        (["tokyo", "توکیو", "ژاپن", "japan"], "Tokyo Standard Time", "توکیو", "Tokyo"),
        (["sydney", "سیدنی", "استرالیا", "australia"], "AUS Eastern Standard Time", "سیدنی", "Sydney"),
        (["new york", "نیویورک", "نیویورک"], "Eastern Standard Time", "نیویورک", "New York"),
        (["toronto", "تورنتو", "کانادا", "canada"], "Eastern Standard Time", "تورنتو", "Toronto"),
        (["los angeles", "لس آنجلس", "لس‌آنجلس", "لس آنجلس", "کالیفرنیا", "california"], "Pacific Standard Time", "لس‌آنجلس", "Los Angeles"),
    ];

    private static (string, string)? WorldTime(string t)
    {
        if (!Has(t, "time", "ساعت", "زمان")) return null;
        if (HasPart(t, "خاموش", "بخواب", "ریستارت", "ری استارت", "یادم", "shut", "sleep", "restart", "remind", "hibernate", "alarm", "wake") || Regex.IsMatch(t, @"\d")) return null;
        var city = Cities.FirstOrDefault(c => c.Names.Any(n => Has(t, n)));
        return city.Names is null ? null : ("world", city.En);
    }

    private static string WorldText(string arg)
    {
        var city = Cities.First(c => c.En == arg);
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(city.Zone);
            var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime;
            return L.T($"ساعت {city.Fa} الان {Fa.Clock(local)} ـه.", $"The time in {city.En} is {local:HH:mm}.");
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return L.T("ساعت اون شهر رو نتونستم بخونم.", "I could not read the time for that city.");
        }
    }

    internal static string WorldTimeAt(string cityEn, DateTimeOffset utc)
    {
        var city = Cities.First(c => c.En == cityEn);
        var local = TimeZoneInfo.ConvertTime(utc, TimeZoneInfo.FindSystemTimeZoneById(city.Zone)).DateTime;
        return local.ToString("HH:mm", Inv);
    }

    // ---- days until -----------------------------------------------------------------------------------------------------

    private static (string, string)? Countdown(string t)
    {
        var asks = HasPart(t, "چند روز", "چقدر مونده", "چقدر مانده", "چند وقت دیگه", "days until", "days to", "days left", "days till", "how long until", "how long till", "how many days");
        if (!asks) return null;
        if (HasPart(t, "نوروز", "سال نو ایرانی", "nowruz", "norooz", "persian new year")) return ("until", "nowruz");
        if (HasPart(t, "یلدا", "yalda")) return ("until", "yalda");
        if (HasPart(t, "کریسمس", "christmas")) return ("until", "christmas");
        if (HasPart(t, "هالووین", "هالوین", "halloween")) return ("until", "halloween");
        if (HasPart(t, "سال نو میلادی", "سال میلادی", "new year", "newyear")) return ("until", "newyear");
        if (HasPart(t, "جمعه", "friday")) return ("until", "friday");
        return null;
    }

    /// <summary>The next date of an occasion on or after <paramref name="today"/> (its date only).</summary>
    internal static DateTime NextOccasion(string key, DateTime today)
    {
        today = today.Date;
        var pc = new PersianCalendar();
        DateTime Jalali(int month, int day)
        {
            var year = pc.GetYear(today);
            var d = pc.ToDateTime(year, month, day, 0, 0, 0, 0);
            return d < today ? pc.ToDateTime(year + 1, month, day, 0, 0, 0, 0) : d;
        }
        DateTime Gregorian(int month, int day)
        {
            var d = new DateTime(today.Year, month, day);
            return d < today ? d.AddYears(1) : d;
        }
        return key switch
        {
            "nowruz" => Jalali(1, 1),
            "yalda" => Jalali(9, 30),
            "christmas" => Gregorian(12, 25),
            "halloween" => Gregorian(10, 31),
            "newyear" => Gregorian(1, 1),
            "friday" => today.AddDays(((int)DayOfWeek.Friday - (int)today.DayOfWeek + 7) % 7),
            _ => today,
        };
    }

    private static string UntilText(string key, DateTime now)
    {
        var date = NextOccasion(key, now);
        var days = (int)(date - now.Date).TotalDays;
        var name = key switch
        {
            "nowruz" => L.T("نوروز", "Nowruz"), "yalda" => L.T("شب یلدا", "Yalda night"), "christmas" => L.T("کریسمس", "Christmas"),
            "halloween" => L.T("هالووین", "Halloween"), "newyear" => L.T("سال نو میلادی", "the new year"), _ => L.T("جمعه", "Friday"),
        };
        if (days == 0) return L.T($"{name} امروزه! 🎉", $"{name} is today! 🎉");
        return L.T($"تا {name} {Fa.N(days)} روز مونده ({Fa.DayOf(date, now.Date)}).", $"{days} day{(days == 1 ? "" : "s")} until {name} ({date:dddd, d MMMM}).");
    }

    // ---- sleep ----------------------------------------------------------------------------------------------------------

    private static readonly Regex TimeRx = new(@"(?<h>\d{1,2})(?:[:.](?<m>\d{2}))?", RegexOptions.Compiled);

    private static (string, string)? Bedtime(string t)
    {
        var asks = HasPart(t, "کی بخوابم", "چه ساعتی بخوابم", "چه ساعتی بخوابم", "چه موقع بخوابم", "ساعت خواب", "محاسبه خواب", "چرخه خواب", "when should i sleep", "when should i go to bed", "when to sleep", "sleep calculator", "sleep cycle", "when do i go to bed", "what time should i sleep");
        if (!asks && !(HasPart(t, "بیدار شم", "بیدار بشم", "بیدار شوم", "wake up at", "wake at", "wake up by") && HasPart(t, "بخوابم", "sleep", "bed", "چه ساعتی", "کی"))) return null;
        var m = TimeRx.Match(t);
        if (m.Success && int.TryParse(m.Groups["h"].Value, out var h) && h is >= 0 and <= 23)
        {
            var min = m.Groups["m"].Success ? int.Parse(m.Groups["m"].Value, Inv) : 0;
            if (min is >= 0 and <= 59) return ("bed", $"{h:00}:{min:00}");
        }
        return ("bed", "");
    }

    private static string BedText(string arg, DateTime now)
    {
        // a sleep cycle is about 90 minutes and falling asleep takes about 15
        if (arg.Length == 0)
        {
            var wakes = new[] { 4, 5, 6 }.Select(k => now.AddMinutes(15 + 90 * k)).ToList();
            return L.T($"اگه همین الان بخوابی، بهترین ساعت‌های بیدار شدن: {string.Join(L.T("، ", ", "), wakes.Select(Fa.Clock))}. (هر چرخه‌ی خواب حدود ۹۰ دقیقه‌ست.) 🌙",
                $"If you fall asleep now, good times to wake up: {string.Join(", ", wakes.Select(w => w.ToString("HH:mm", Inv)))}. (A sleep cycle is about 90 minutes.) 🌙");
        }
        var wake = DateTime.ParseExact(arg, "HH:mm", Inv);
        var wakeAt = now.Date.AddHours(wake.Hour).AddMinutes(wake.Minute);
        if (wakeAt <= now) wakeAt = wakeAt.AddDays(1);
        var beds = new[] { 6, 5, 4 }.Select(k => wakeAt.AddMinutes(-(15 + 90 * k))).ToList();
        return L.T($"برای بیدار شدن ساعت {Fa.Clock(wakeAt)}، بهتره {string.Join(L.T(" یا ", " or "), beds.Select(Fa.Clock))} بخوابی. (هر چرخه‌ی خواب حدود ۹۰ دقیقه‌ست.) 🌙",
            $"To wake up at {wakeAt:HH:mm}, go to sleep at {string.Join(" or ", beds.Select(b => b.ToString("HH:mm", Inv)))}. (A sleep cycle is about 90 minutes.) 🌙");
    }

    // ---- random number and choosing -------------------------------------------------------------------------------------

    private static (string, string)? RandomNumber(string t)
    {
        if (!HasPart(t, "عدد تصادفی", "عدد رندوم", "عدد شانسی", "random number", "pick a number", "a number between", "یه عدد", "یک عدد") || t.Split(' ').Length > 10) return null;
        var numbers = Regex.Matches(t, @"-?\d+").Select(x => long.TryParse(x.Value, out var v) ? v : (long?)null).Where(v => v is not null).Select(v => v!.Value).ToList();
        long lo = 1, hi = 100;
        if (numbers.Count >= 2) (lo, hi) = (Math.Min(numbers[0], numbers[1]), Math.Max(numbers[0], numbers[1]));
        else if (numbers.Count == 1) hi = Math.Max(1, numbers[0]);
        lo = Math.Clamp(lo, -1_000_000_000, 1_000_000_000);
        hi = Math.Clamp(hi, lo, 1_000_000_000);
        return ("random", $"{lo}|{hi}");
    }

    private static string RandomText(string arg)
    {
        var p = arg.Split('|');
        var lo = long.Parse(p[0], Inv);
        var hi = long.Parse(p[1], Inv);
        var value = Random.Shared.NextInt64(lo, hi + 1);
        return L.T($"عدد تصادفی بین {Fa.Digits(lo.ToString())} و {Fa.Digits(hi.ToString())}: {Fa.Digits(value.ToString())} 🎲", $"A random number between {lo} and {hi}: {value} 🎲");
    }

    private static (string, string)? Choose(string t, string raw)
    {
        var asks = HasPart(t, "انتخاب کن", "یکی رو انتخاب", "تصمیم بگیر", "کدوم رو", "choose", "pick", "decide", "which one", "help me choose");
        if (!asks || HasPart(t, "تم", "theme", "صفحه", "page", "ساعت", "دقیقه", "minute", "hour")) return null;
        var body = Regex.Replace(t, @"^(?:please\s+|لطفا\s+)?(?:choose|pick|decide|help me choose)(?:\s+between)?\s+|\s*(?:بین)\s+|\s+(?:انتخاب کن|یکی رو انتخاب کن|تصمیم بگیر|کدوم رو انتخاب کنم|کدوم رو)\s*$", " ").Trim();
        var options = Regex.Split(body, @"\s+(?:یا|or)\s+|,|،|\s+و\s+|\s+and\s+").Select(o => o.Trim()).Where(o => o.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (options.Count < 2 || options.Count > 8 || options.Any(o => o.Length > 40)) return null;
        return ("choose", string.Join("|", options));
    }

    private static string ChooseText(string arg)
    {
        var options = arg.Split('|');
        var pick = options[RandomNumberGenerator.GetInt32(options.Length)];
        return L.T($"من «{pick}» رو انتخاب می‌کنم 🦉", $"I pick “{pick}” 🦉");
    }

    // ---- battery --------------------------------------------------------------------------------------------------------

    private static (string, string)? Battery(string t)
    {
        if (!HasPart(t, "باتری", "شارژ لپ", "battery") || t.Split(' ').Length > 7) return null;
        if (HasPart(t, "خاموش", "بخواب", "یادم", "shut", "sleep", "remind") || Has(t, "when", "وقتی")) return null;
        return ("battery", "");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerStatus { public byte AcLine, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }

    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out PowerStatus status);

    private static string BatteryText()
    {
        try
        {
            if (!GetSystemPowerStatus(out var s) || s.BatteryFlag == 128 || s.BatteryLifePercent == 255)
                return L.T("این کامپیوتر باتری نداره (یا نمی‌تونم بخونمش).", "This PC has no battery (or I cannot read it).");
            var plugged = s.AcLine == 1;
            var left = s.BatteryLifeTime >= 0 && !plugged ? TimeSpan.FromSeconds(s.BatteryLifeTime) : (TimeSpan?)null;
            return L.T($"باتری {Fa.Digits(s.BatteryLifePercent.ToString())}٪ه" + (plugged ? " و به شارژر وصله. 🔌" : left is { } l ? $"؛ حدود {Fa.Duration(l)} دیگه جواب می‌ده. 🔋" : ". 🔋"),
                $"The battery is at {s.BatteryLifePercent}%" + (plugged ? " and plugged in. 🔌" : left is { } l2 ? $"; about {Fa.Duration(l2)} left. 🔋" : ". 🔋"));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return L.T("باتری رو نتونستم بخونم.", "I could not read the battery.");
        }
    }
}
