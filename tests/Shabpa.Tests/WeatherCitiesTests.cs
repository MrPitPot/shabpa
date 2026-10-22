using System.Text.Json;
using Shabpa.Core;
using Xunit;

namespace Shabpa.Tests;

public class WeatherCitiesTests
{
    private static WeatherCity City(string name, double lat, double lon) => new() { Name = name, Region = "R", Country = "C", Latitude = lat, Longitude = lon };

    [Fact]
    public void An_old_file_with_only_a_default_gets_it_as_the_first_favourite()
    {
        var s = new Settings { WeatherCity = City("Tehran", 35.69, 51.42) };
        WeatherCities.Sanitize(s);
        Assert.Single(s.WeatherFavorites);
        Assert.Equal("Tehran", s.WeatherCity!.Name);
    }

    [Fact]
    public void Add_keeps_the_first_city_as_default_and_refuses_duplicates()
    {
        var s = new Settings();
        Assert.True(WeatherCities.Add(s, City("A", 10, 10)));
        Assert.True(WeatherCities.Add(s, City("B", 20, 20)));
        Assert.False(WeatherCities.Add(s, City("A again", 10.001, 10.002)));
        Assert.Equal(2, s.WeatherFavorites.Count);
        Assert.Equal("A", s.WeatherCity!.Name);
    }

    [Fact]
    public void The_list_is_capped()
    {
        var s = new Settings();
        for (var i = 0; i < 20; i++) WeatherCities.Add(s, City("C" + i, i * 4, i * 5));
        Assert.Equal(WeatherCities.MaxCities, s.WeatherFavorites.Count);
        Assert.False(WeatherCities.Add(s, City("Extra", -50, -60)));
    }

    [Fact]
    public void Removing_the_default_hands_it_to_the_next_city_and_the_last_one_clears_it()
    {
        var s = new Settings();
        WeatherCities.Add(s, City("A", 10, 10));
        WeatherCities.Add(s, City("B", 20, 20));
        Assert.True(WeatherCities.Remove(s, City("A", 10, 10)));
        Assert.Equal("B", s.WeatherCity!.Name);
        Assert.True(WeatherCities.Remove(s, City("B", 20, 20)));
        Assert.Null(s.WeatherCity);
        Assert.Empty(s.WeatherFavorites);
        Assert.False(WeatherCities.Remove(s, City("B", 20, 20)));
    }

    [Fact]
    public void Set_default_only_works_for_a_saved_city()
    {
        var s = new Settings();
        WeatherCities.Add(s, City("A", 10, 10));
        WeatherCities.Add(s, City("B", 20, 20));
        Assert.True(WeatherCities.SetDefault(s, City("B", 20, 20)));
        Assert.Equal("B", s.WeatherCity!.Name);
        Assert.False(WeatherCities.SetDefault(s, City("Z", 1, 1)));
        Assert.Equal("B", s.WeatherCity!.Name);
    }

    [Fact]
    public void Damaged_entries_are_dropped_and_text_is_trimmed()
    {
        var s = new Settings
        {
            WeatherCity = City("  ", 10, 10), // no name
            WeatherFavorites =
            [
                City("Nan", double.NaN, 5), City("TooFar", 91, 0), City("Wide", 0, 181), City(new string('x', 500), 12, 12),
                null!, City("Ok", 30, 30), City("Ok copy", 30, 30),
            ],
        };
        WeatherCities.Sanitize(s);
        Assert.Equal(2, s.WeatherFavorites.Count);
        Assert.Equal(80, s.WeatherFavorites[0].Name.Length);
        Assert.Equal("x", s.WeatherFavorites[0].Name[..1]);
        Assert.Equal("Ok", s.WeatherFavorites[1].Name);
        Assert.Equal(s.WeatherFavorites[0].Name, s.WeatherCity!.Name);
    }

    [Fact]
    public void Null_list_from_a_hand_edited_file_is_survivable()
    {
        var s = JsonSerializer.Deserialize<Settings>("{\"WeatherFavorites\":null}")!;
        WeatherCities.Sanitize(s);
        Assert.Empty(s.WeatherFavorites);
        Assert.Null(s.WeatherCity);
    }

    [Fact]
    public void Cities_survive_a_save_and_load_round_trip()
    {
        var s = new Settings();
        WeatherCities.Add(s, City("تهران", 35.69, 51.42));
        WeatherCities.Add(s, City("Isfahan", 32.66, 51.68));
        var back = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(s))!;
        Assert.Equal(2, back.WeatherFavorites.Count);
        Assert.Equal("تهران", back.WeatherCity!.Name);
        Assert.True(WeatherCities.Same(back.WeatherFavorites[1], s.WeatherFavorites[1]));
    }

    [Fact]
    public void An_imported_settings_file_is_sanitised()
    {
        var incoming = new Settings { WeatherFavorites = Enumerable.Range(0, 50).Select(i => City("C" + i, i, i)).ToList() };
        incoming.WeatherFavorites.Add(City("Bad", 500, 0));
        var state = new AppState();
        var pack = new SettingsPack { Settings = incoming };
        SettingsBackup.Apply(pack, state);
        Assert.Equal(WeatherCities.MaxCities, state.Settings.WeatherFavorites.Count);
        Assert.NotNull(state.Settings.WeatherCity);
    }
}
