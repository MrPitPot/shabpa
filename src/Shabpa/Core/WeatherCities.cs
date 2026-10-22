namespace Shabpa.Core;

/// <summary>The weather page's saved cities: a short list with one default. Pure functions over <see cref="Settings"/>,
/// so adding, removing and cleaning a hand-edited or damaged file are all testable without the UI.</summary>
public static class WeatherCities
{
    public const int MaxCities = 8;
    private const int MaxName = 80;

    /// <summary>Two places are the same city when their coordinates agree to about a kilometre.</summary>
    public static string Key(WeatherCity c) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Math.Round(c.Latitude, 2):0.00},{Math.Round(c.Longitude, 2):0.00}");

    public static bool Same(WeatherCity? a, WeatherCity? b) => a is not null && b is not null && Key(a) == Key(b);

    /// <summary>A trimmed, range-checked copy, or null when the entry is unusable (no name, NaN or out-of-range coordinates).</summary>
    public static WeatherCity? Clean(WeatherCity? c)
    {
        if (c is null) return null;
        var name = Cut(c.Name, MaxName);
        if (name.Length == 0) return null;
        if (!double.IsFinite(c.Latitude) || !double.IsFinite(c.Longitude) || Math.Abs(c.Latitude) > 90 || Math.Abs(c.Longitude) > 180) return null;
        return new WeatherCity { Name = name, Region = Cut(c.Region, MaxName), Country = Cut(c.Country, MaxName), Latitude = c.Latitude, Longitude = c.Longitude };
    }

    private static string Cut(string? s, int max)
    {
        s = (s ?? "").Trim();
        return s.Length > max ? s[..max].TrimEnd() : s;
    }

    /// <summary>Brings the list into shape: valid entries only, no duplicates, at most <see cref="MaxCities"/>, and a default that is in the list.
    /// A default from an older file (before favourites existed) becomes the first favourite.</summary>
    public static void Sanitize(Settings s)
    {
        var list = new List<WeatherCity>();
        var seen = new HashSet<string>();
        var def = Clean(s.WeatherCity);
        if (def is not null) { list.Add(def); seen.Add(Key(def)); }
        foreach (var raw in s.WeatherFavorites ?? [])
        {
            if (list.Count >= MaxCities) break;
            if (Clean(raw) is { } c && seen.Add(Key(c))) list.Add(c);
        }
        s.WeatherFavorites = list;
        s.WeatherCity = def ?? list.FirstOrDefault();
    }

    /// <summary>Adds a city (it becomes the default when there is none). False when it is invalid, already there, or the list is full.</summary>
    public static bool Add(Settings s, WeatherCity city)
    {
        Sanitize(s);
        if (Clean(city) is not { } c || s.WeatherFavorites.Count >= MaxCities || s.WeatherFavorites.Any(f => Same(f, c))) return false;
        s.WeatherFavorites.Add(c);
        s.WeatherCity ??= c;
        return true;
    }

    /// <summary>Removes a city; when it was the default, the first remaining one takes over.</summary>
    public static bool Remove(Settings s, WeatherCity city)
    {
        Sanitize(s);
        var n = s.WeatherFavorites.RemoveAll(f => Same(f, city));
        if (n == 0) return false;
        if (Same(s.WeatherCity, city)) s.WeatherCity = s.WeatherFavorites.FirstOrDefault();
        return true;
    }

    public static bool SetDefault(Settings s, WeatherCity city)
    {
        Sanitize(s);
        if (s.WeatherFavorites.FirstOrDefault(f => Same(f, city)) is not { } found) return false;
        s.WeatherCity = found;
        return true;
    }
}
