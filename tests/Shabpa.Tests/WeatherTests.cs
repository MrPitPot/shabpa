using Shabpa.Core;
using Xunit;

namespace Shabpa.Tests;

public class WeatherTests
{
    private const string Cities = """
        {"results":[
          {"name":"تهران","latitude":35.69,"longitude":51.42,"country":"ایران","admin1":"استان تهران"},
          {"name":"تهران","latitude":35.69,"longitude":51.43,"country":"ایران","admin1":"استان تهران"},
          {"name":"Tehran","latitude":35.7,"longitude":51.4,"country":"Iran"}
        ]}
        """;

    private const string Forecast = """
        {"current":{"time":"2026-10-15T13:30","temperature_2m":21.4,"apparent_temperature":19.2,"relative_humidity_2m":41,"is_day":1,
          "precipitation":0,"weather_code":2,"cloud_cover":35,"pressure_msl":1015.2,"wind_speed_10m":13.7,"wind_direction_10m":292},
         "hourly":{"time":["2026-10-15T12:00","2026-10-15T13:00","2026-10-15T14:00","2026-10-15T15:00"],
          "temperature_2m":[20,21,22,21],"weather_code":[0,2,3,61],"precipitation_probability":[0,0,10,60],"is_day":[1,1,1,1]},
         "daily":{"time":["2026-10-15","2026-10-16"],"weather_code":[2,63],"temperature_2m_max":[24,19],"temperature_2m_min":[11,9],
          "sunrise":["2026-10-15T06:25","2026-10-16T06:26"],"sunset":["2026-10-15T17:40","2026-10-16T17:39"],
          "uv_index_max":[5.5,3],"precipitation_sum":[0,6.2],"precipitation_probability_max":[5,80],"wind_speed_10m_max":[20,28]}}
        """;

    [Fact]
    public void Suggestions_are_read_and_near_duplicates_dropped()
    {
        var cities = WeatherClient.ParseCities(Cities);
        Assert.Equal(2, cities.Count);
        Assert.Equal("تهران", cities[0].Name);
        Assert.Equal(35.69, cities[0].Latitude, 2);
        Assert.Contains("ایران", cities[0].Subtitle);
    }

    [Fact]
    public void No_results_is_an_empty_list()
    {
        Assert.Empty(WeatherClient.ParseCities("""{"generationtime_ms":0.3}"""));
    }

    [Fact]
    public void Forecast_is_parsed_into_now_hours_and_days()
    {
        var city = new WeatherCity { Name = "تهران", Latitude = 35.69, Longitude = 51.42 };
        var r = WeatherClient.ParseForecast(city, Forecast, DateTime.Now);
        Assert.Equal(21.4, r.Now.Temp, 1);
        Assert.Equal(41, r.Now.Humidity);
        Assert.True(r.Now.IsDay);
        Assert.Equal(5.5, r.Now.Uv, 1);
        Assert.Equal(2, r.Days.Count);
        Assert.Equal(24, r.Days[0].Max);
        Assert.Equal(new DateTime(2026, 10, 15, 6, 25, 0), r.Days[0].Sunrise);
        // hours before the current one are left out, the current hour stays
        Assert.Equal(3, r.Hours.Count);
        Assert.Equal(60, r.Hours[^1].RainChance);
    }

    [Theory]
    [InlineData(0, Sky.Clear)]
    [InlineData(2, Sky.PartlyCloudy)]
    [InlineData(3, Sky.Cloudy)]
    [InlineData(45, Sky.Fog)]
    [InlineData(53, Sky.Drizzle)]
    [InlineData(63, Sky.Rain)]
    [InlineData(81, Sky.Showers)]
    [InlineData(73, Sky.Snow)]
    [InlineData(95, Sky.Thunder)]
    public void Weather_codes_map_to_the_scene(int code, Sky sky) => Assert.Equal(sky, WeatherCodes.SkyOf(code));

    [Fact]
    public void Every_sky_has_text_and_colours_by_day_and_night()
    {
        foreach (var sky in Enum.GetValues<Sky>())
        foreach (var day in new[] { true, false })
        {
            var (top, bottom) = Pages.WeatherScene.Colours(sky, day);
            Assert.NotEqual(top, bottom);
        }
        foreach (var code in new[] { 0, 1, 2, 3, 45, 48, 51, 53, 55, 56, 61, 63, 65, 66, 71, 73, 75, 77, 80, 81, 82, 85, 95, 96, 99 })
            Assert.False(string.IsNullOrWhiteSpace(WeatherCodes.Text(code)));
    }

    [Fact]
    public void Compass_names_wrap_around()
    {
        Assert.Equal(WeatherCodes.Compass(0), WeatherCodes.Compass(360));
        Assert.NotEqual(WeatherCodes.Compass(0), WeatherCodes.Compass(180));
    }
}

public class GreetingTests
{
    [Fact]
    public void Every_part_of_the_day_has_many_lines_in_both_languages()
    {
        foreach (var part in Enum.GetValues<Greetings.Part>())
        {
            Assert.True(Greetings.Persian[part].Length >= 7, part.ToString());
            Assert.Equal(Greetings.Persian[part].Length, Greetings.English[part].Length);
            Assert.Equal(Greetings.Persian[part].Length, Greetings.Persian[part].Distinct().Count());
        }
    }

    [Theory]
    [InlineData(5, Greetings.Part.Dawn)]
    [InlineData(9, Greetings.Part.Morning)]
    [InlineData(12, Greetings.Part.Noon)]
    [InlineData(15, Greetings.Part.Afternoon)]
    [InlineData(19, Greetings.Part.Evening)]
    [InlineData(22, Greetings.Part.Night)]
    [InlineData(2, Greetings.Part.Late)]
    public void Hours_map_to_parts(int hour, Greetings.Part part) =>
        Assert.Equal(part, Greetings.PartOf(new DateTime(2026, 1, 1, hour, 0, 0)));

    [Fact]
    public void The_line_is_stable_within_a_part_of_the_day()
    {
        Greetings.Reset();
        var morning = new DateTime(2026, 1, 1, 9, 0, 0);
        Assert.Equal(Greetings.Pick(morning), Greetings.Pick(morning.AddMinutes(30)));
    }
}
