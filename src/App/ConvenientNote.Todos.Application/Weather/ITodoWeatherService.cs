namespace ConvenientNote.Services;

/// <summary>Weather read capability required by the todo header.</summary>
public interface ITodoWeatherService
{
    Task<WeatherSnapshot> GetCurrentWeatherAsync(CancellationToken cancellationToken = default);
}

public sealed record WeatherLocation(
    string? Name,
    string? Admin1,
    string? Country,
    double Latitude,
    double Longitude);

public sealed record WeatherSnapshot(
    string LocationName,
    double TemperatureC,
    double ApparentTemperatureC,
    double WindSpeedKmh,
    int WeatherCode,
    bool IsDay,
    string Time);
