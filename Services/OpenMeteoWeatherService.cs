using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace ConvenientNote.Services
{
    public sealed class OpenMeteoWeatherService
    {
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);
        private static readonly WeatherLocation DefaultLocation = new(
            "无锡",
            "江苏",
            "中国",
            31.56887,
            120.28857);

        private readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        private WeatherSnapshot? _cachedWeather;
        private DateTimeOffset _cachedAt;

        public async Task<WeatherSnapshot> GetCurrentWeatherAsync(
            CancellationToken cancellationToken = default)
        {
            if (_cachedWeather is not null &&
                DateTimeOffset.Now - _cachedAt < CacheDuration)
            {
                return _cachedWeather;
            }

            var weather = await GetWeatherAsync(DefaultLocation, cancellationToken);

            _cachedWeather = weather;
            _cachedAt = DateTimeOffset.Now;
            return weather;
        }

        private async Task<WeatherSnapshot> GetWeatherAsync(
            WeatherLocation location,
            CancellationToken cancellationToken)
        {
            var latitude = location.Latitude.ToString(CultureInfo.InvariantCulture);
            var longitude = location.Longitude.ToString(CultureInfo.InvariantCulture);
            var url = "https://api.open-meteo.com/v1/forecast" +
                $"?latitude={latitude}&longitude={longitude}" +
                "&current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m,is_day" +
                "&timezone=auto&forecast_days=1";

            var response = await _httpClient.GetFromJsonAsync<ForecastResponse>(
                url,
                cancellationToken);
            var current = response?.Current;

            if (current is null)
            {
                throw new InvalidOperationException("天气接口没有返回当前天气。");
            }

            return new WeatherSnapshot(
                BuildLocationName(location),
                current.Temperature,
                current.ApparentTemperature,
                current.WindSpeed,
                current.WeatherCode,
                current.IsDay == 1,
                current.Time ?? string.Empty);
        }

        private static string BuildLocationName(WeatherLocation location)
        {
            var parts = new[]
            {
                location.Name,
                location.Admin1,
                location.Country
            };

            return string.Join(" · ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        }

        private sealed record WeatherLocation(
            string? Name,
            string? Admin1,
            string? Country,
            double Latitude,
            double Longitude);

        private sealed class ForecastResponse
        {
            [JsonPropertyName("current")]
            public CurrentWeather? Current { get; init; }
        }

        private sealed class CurrentWeather
        {
            [JsonPropertyName("time")]
            public string? Time { get; init; }

            [JsonPropertyName("temperature_2m")]
            public double Temperature { get; init; }

            [JsonPropertyName("apparent_temperature")]
            public double ApparentTemperature { get; init; }

            [JsonPropertyName("weather_code")]
            public int WeatherCode { get; init; }

            [JsonPropertyName("wind_speed_10m")]
            public double WindSpeed { get; init; }

            [JsonPropertyName("is_day")]
            public int IsDay { get; init; }
        }
    }

    public sealed record WeatherSnapshot(
        string LocationName,
        double TemperatureC,
        double ApparentTemperatureC,
        double WindSpeedKmh,
        int WeatherCode,
        bool IsDay,
        string Time);
}
