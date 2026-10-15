using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace Shabpa.Core;

/// <summary>A place the user can pick (from the search suggestions).</summary>
public sealed class WeatherCity
{
    public string Name { get; set; } = "";
    public string Region { get; set; } = "";
    public string Country { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }

    public string Title => Region.Length > 0 && Region != Name ? $"{Name}، {Region}" : Name;
    public string Subtitle => string.Join(L.En ? ", " : "، ", new[] { Region.Length > 0 && Region != Name ? Region : "", Country }.Where(s => s.Length > 0));
}

/// <summary>What the sky is doing, which drives the icon and the animated scene.</summary>
public enum Sky { Clear, PartlyCloudy, Cloudy, Fog, Drizzle, Rain, Showers, Snow, Thunder }

public sealed record WeatherNow(double Temp, double Feels, int Humidity, double WindKmh, double WindDir, double PressureHpa, double CloudPercent,
    double PrecipMm, bool IsDay, int Code, double Uv);

public sealed record WeatherHour(DateTime Time, double Temp, int Code, int RainChance, bool IsDay);

public sealed record WeatherDay(DateTime Date, double Max, double Min, int Code, int RainChance, double RainMm, double WindMaxKmh, double UvMax,
    DateTime? Sunrise, DateTime? Sunset);

public sealed record WeatherReport(WeatherCity City, DateTime Fetched, WeatherNow Now, IReadOnlyList<WeatherHour> Hours, IReadOnlyList<WeatherDay> Days,
    string Source);

public static class WeatherCodes
{
    /// <summary>WMO weather interpretation codes (the ones Open-Meteo returns).</summary>
    public static Sky SkyOf(int code) => code switch
    {
        0 => Sky.Clear,
        1 or 2 => Sky.PartlyCloudy,
        3 => Sky.Cloudy,
        45 or 48 => Sky.Fog,
        51 or 53 or 55 or 56 or 57 => Sky.Drizzle,
        61 or 63 or 65 or 66 or 67 => Sky.Rain,
        80 or 81 or 82 => Sky.Showers,
        71 or 73 or 75 or 77 or 85 or 86 => Sky.Snow,
        95 or 96 or 99 => Sky.Thunder,
        _ => Sky.Cloudy,
    };

    public static string Text(int code) => code switch
    {
        0 => L.T("آسمان صاف", "Clear sky"),
        1 => L.T("بیشتر صاف", "Mainly clear"),
        2 => L.T("کمی ابری", "Partly cloudy"),
        3 => L.T("ابری", "Overcast"),
        45 => L.T("مه", "Fog"),
        48 => L.T("مه یخ‌زده", "Rime fog"),
        51 => L.T("نم‌نم باران ملایم", "Light drizzle"),
        53 => L.T("نم‌نم باران", "Drizzle"),
        55 => L.T("نم‌نم باران شدید", "Dense drizzle"),
        56 or 57 => L.T("نم‌نم باران یخ‌زده", "Freezing drizzle"),
        61 => L.T("باران ملایم", "Light rain"),
        63 => L.T("باران", "Rain"),
        65 => L.T("باران شدید", "Heavy rain"),
        66 or 67 => L.T("باران یخ‌زده", "Freezing rain"),
        71 => L.T("برف ملایم", "Light snow"),
        73 => L.T("برف", "Snow"),
        75 => L.T("برف شدید", "Heavy snow"),
        77 => L.T("دانه‌های برف", "Snow grains"),
        80 => L.T("رگبار ملایم", "Light showers"),
        81 => L.T("رگبار", "Showers"),
        82 => L.T("رگبار شدید", "Violent showers"),
        85 or 86 => L.T("بارش برف", "Snow showers"),
        95 => L.T("رعد و برق", "Thunderstorm"),
        96 or 99 => L.T("رعد و برق با تگرگ", "Thunderstorm with hail"),
        _ => L.T("ابری", "Cloudy"),
    };

    public static string Emoji(int code, bool day = true) => SkyOf(code) switch
    {
        Sky.Clear => day ? "☀️" : "🌙",
        Sky.PartlyCloudy => day ? "🌤️" : "☁️",
        Sky.Cloudy => "☁️",
        Sky.Fog => "🌫️",
        Sky.Drizzle => "🌦️",
        Sky.Rain => "🌧️",
        Sky.Showers => "🌧️",
        Sky.Snow => "❄️",
        Sky.Thunder => "⛈️",
        _ => "☁️",
    };

    public static string Compass(double degrees)
    {
        var names = L.En
            ? new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" }
            : new[] { "شمال", "شمال‌شرق", "شرق", "جنوب‌شرق", "جنوب", "جنوب‌غرب", "غرب", "شمال‌غرب" };
        return names[(int)Math.Round(((degrees % 360) + 360) % 360 / 45.0) % 8];
    }
}

/// <summary>Open-Meteo: a free, key-less, open-source weather service (HTTPS) that merges national weather agencies' models.
/// Nothing but the place name or coordinates is sent; no account or identifier.</summary>
public sealed class WeatherClient
{
    private const string GeoUrl = "https://geocoding-api.open-meteo.com/v1/search";
    private const string ForecastUrl = "https://api.open-meteo.com/v1/forecast";
    public const string SourceName = "Open-Meteo.com";

    private readonly Func<string> _proxy;

    public WeatherClient(Func<string> proxy) => _proxy = proxy;

    private HttpClient Http()
    {
        var handler = new HttpClientHandler();
        var proxy = _proxy().Trim();
        if (proxy.Length > 0)
        {
            try { handler.Proxy = new WebProxy(proxy); handler.UseProxy = true; }
            catch { /* invalid proxy: use the system one */ }
        }
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Shabpa");
        return http;
    }

    public async Task<List<WeatherCity>> SearchAsync(string query, bool persian, CancellationToken ct = default)
    {
        query = query.Trim();
        if (query.Length < 2) return [];
        using var http = Http();
        var url = $"{GeoUrl}?name={Uri.EscapeDataString(query)}&count=8&language={(persian ? "fa" : "en")}&format=json";
        var json = await http.GetStringAsync(url, ct).ConfigureAwait(false);
        return ParseCities(json);
    }

    public async Task<WeatherReport> ForecastAsync(WeatherCity city, CancellationToken ct = default)
    {
        using var http = Http();
        var lat = city.Latitude.ToString("0.####", CultureInfo.InvariantCulture);
        var lon = city.Longitude.ToString("0.####", CultureInfo.InvariantCulture);
        var url = $"{ForecastUrl}?latitude={lat}&longitude={lon}"
                  + "&current=temperature_2m,apparent_temperature,relative_humidity_2m,is_day,precipitation,weather_code,cloud_cover,pressure_msl,wind_speed_10m,wind_direction_10m"
                  + "&hourly=temperature_2m,weather_code,precipitation_probability,is_day"
                  + "&daily=weather_code,temperature_2m_max,temperature_2m_min,sunrise,sunset,uv_index_max,precipitation_sum,precipitation_probability_max,wind_speed_10m_max"
                  + "&wind_speed_unit=kmh&timezone=auto&forecast_days=8";
        var json = await http.GetStringAsync(url, ct).ConfigureAwait(false);
        return ParseForecast(city, json, DateTime.Now);
    }

    public static List<WeatherCity> ParseCities(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<WeatherCity>();
        if (!doc.RootElement.TryGetProperty("results", out var results)) return list;
        foreach (var r in results.EnumerateArray())
        {
            if (!r.TryGetProperty("latitude", out var lat) || !r.TryGetProperty("longitude", out var lon)) continue;
            list.Add(new WeatherCity
            {
                Name = Str(r, "name"), Region = Str(r, "admin1"), Country = Str(r, "country"),
                Latitude = lat.GetDouble(), Longitude = lon.GetDouble(),
            });
        }
        // Drop duplicates the service sometimes returns for the same spot.
        return [.. list.GroupBy(c => (c.Name, c.Region, Math.Round(c.Latitude, 1), Math.Round(c.Longitude, 1))).Select(g => g.First())];
    }

    public static WeatherReport ParseForecast(WeatherCity city, string json, DateTime fetched)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var c = root.GetProperty("current");
        var now = new WeatherNow(
            Num(c, "temperature_2m"), Num(c, "apparent_temperature"), (int)Math.Round(Num(c, "relative_humidity_2m")), Num(c, "wind_speed_10m"),
            Num(c, "wind_direction_10m"), Num(c, "pressure_msl"), Num(c, "cloud_cover"), Num(c, "precipitation"),
            Num(c, "is_day") >= 1, (int)Num(c, "weather_code"), 0);

        var currentTime = ParseTime(c.TryGetProperty("time", out var ct) ? ct.GetString() : null) ?? DateTime.Now;
        var hours = new List<WeatherHour>();
        if (root.TryGetProperty("hourly", out var h))
        {
            var times = h.GetProperty("time").EnumerateArray().Select(t => ParseTime(t.GetString())).ToList();
            var temps = Series(h, "temperature_2m");
            var codes = Series(h, "weather_code");
            var rain = Series(h, "precipitation_probability");
            var day = Series(h, "is_day");
            for (var i = 0; i < times.Count; i++)
            {
                if (times[i] is not { } t || t < currentTime.AddMinutes(-59) || hours.Count >= 24) continue;
                hours.Add(new WeatherHour(t, temps.At(i), (int)codes.At(i), (int)rain.At(i), day.At(i) >= 1));
            }
        }

        var days = new List<WeatherDay>();
        double uvToday = 0;
        if (root.TryGetProperty("daily", out var d))
        {
            var dates = d.GetProperty("time").EnumerateArray().Select(t => ParseTime(t.GetString())).ToList();
            var max = Series(d, "temperature_2m_max");
            var min = Series(d, "temperature_2m_min");
            var codes = Series(d, "weather_code");
            var rainChance = Series(d, "precipitation_probability_max");
            var rainMm = Series(d, "precipitation_sum");
            var wind = Series(d, "wind_speed_10m_max");
            var uv = Series(d, "uv_index_max");
            var sunrise = d.TryGetProperty("sunrise", out var sr) ? sr.EnumerateArray().Select(t => ParseTime(t.GetString())).ToList() : [];
            var sunset = d.TryGetProperty("sunset", out var ss) ? ss.EnumerateArray().Select(t => ParseTime(t.GetString())).ToList() : [];
            for (var i = 0; i < dates.Count; i++)
            {
                if (dates[i] is not { } date) continue;
                days.Add(new WeatherDay(date, max.At(i), min.At(i), (int)codes.At(i), (int)rainChance.At(i), rainMm.At(i), wind.At(i), uv.At(i),
                    i < sunrise.Count ? sunrise[i] : null, i < sunset.Count ? sunset[i] : null));
            }
            if (uv.Count > 0) uvToday = uv[0];
        }
        return new WeatherReport(city, fetched, now with { Uv = uvToday }, hours, days, WeatherClient.SourceName);
    }

    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static double Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    private static List<double> Series(JsonElement e, string name) =>
        e.TryGetProperty(name, out var arr)
            ? [.. arr.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Number ? x.GetDouble() : 0)]
            : [];

    private static DateTime? ParseTime(string? text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;
}

internal static class WeatherListExtensions
{
    public static double At(this List<double> list, int i) => i >= 0 && i < list.Count ? list[i] : 0;
}
