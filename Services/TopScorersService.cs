using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VmTips.Web.Models;

namespace VmTips.Web.Services;

public sealed class TopScorersService(
    HttpClient httpClient,
    IOptions<ResultsApiSettings> options,
    ILogger<TopScorersService> logger)
{
    private readonly ResultsApiSettings _settings = options.Value;
    private static readonly object ThrottleLock = new();
    private static DateTimeOffset _nextAllowedRequestUtc = DateTimeOffset.MinValue;

    public async Task<TopScorersResult> GetTopScorersAsync(int limit = 10)
    {
        if (!_settings.Provider.Equals("FootballData", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported results API provider: {_settings.Provider}");

        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
            throw new InvalidOperationException("Missing ResultsApi:ApiKey. Add it locally or as a Render environment variable.");

        ThrowIfThrottled();

        var cappedLimit = Math.Clamp(limit, 1, 20);
        var baseUrl = _settings.BaseUrl.TrimEnd('/');
        var url = $"{baseUrl}/competitions/{_settings.CompetitionCode}/scorers?limit={cappedLimit}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Auth-Token", _settings.ApiKey);

        using var response = await httpClient.SendAsync(request);
        UpdateThrottleState(response);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Scorers API returned {(int)response.StatusCode} {response.ReasonPhrase}.");

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);

        var result = new TopScorersResult();

        if (!document.RootElement.TryGetProperty("scorers", out var scorersElement))
            return result;

        var rank = 1;

        foreach (var scorerElement in scorersElement.EnumerateArray())
        {
            var playerElement = scorerElement.GetProperty("player");
            var teamElement = scorerElement.GetProperty("team");

            result.Scorers.Add(new TopScorerRow
            {
                Rank = rank++,
                PlayerName = ReadString(playerElement, "name"),
                TeamName = ReadString(teamElement, "name"),
                TeamCode = ReadString(teamElement, "tla"),
                TeamCrestUrl = ReadNullableString(teamElement, "crest"),
                Goals = ReadInt(scorerElement, "goals"),
                Assists = ReadNullableInt(scorerElement, "assists"),
                Penalties = ReadNullableInt(scorerElement, "penalties")
            });
        }

        logger.LogInformation("Fetched {Count} top scorers from API", result.Scorers.Count);

        return result;
    }

    private static void ThrowIfThrottled()
    {
        DateTimeOffset nextAllowedRequestUtc;

        lock (ThrottleLock)
        {
            nextAllowedRequestUtc = _nextAllowedRequestUtc;
        }

        var now = DateTimeOffset.UtcNow;

        if (now < nextAllowedRequestUtc)
        {
            var waitSeconds = Math.Max(1, (int)Math.Ceiling((nextAllowedRequestUtc - now).TotalSeconds));
            throw new InvalidOperationException($"Results API rate limit is cooling down. Try again in {waitSeconds} seconds.");
        }
    }

    private static void UpdateThrottleState(HttpResponseMessage response)
    {
        var resetAfterSeconds = ReadHeaderInt(response, "X-RequestCounter-Reset");
        var requestsAvailable = ReadHeaderInt(response, "X-RequestsAvailable");
        var retryAfterSeconds = response.Headers.RetryAfter?.Delta is { } retryAfter
            ? (int)Math.Ceiling(retryAfter.TotalSeconds)
            : ReadHeaderInt(response, "Retry-After");

        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests && retryAfterSeconds is not null)
        {
            SetNextAllowedRequest(retryAfterSeconds.Value);
            return;
        }

        if (requestsAvailable is <= 1 && resetAfterSeconds is not null)
        {
            SetNextAllowedRequest(resetAfterSeconds.Value);
        }
    }

    private static int? ReadHeaderInt(HttpResponseMessage response, string headerName)
    {
        if (!response.Headers.TryGetValues(headerName, out var values))
            return null;

        var value = values.FirstOrDefault();

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static void SetNextAllowedRequest(int waitSeconds)
    {
        if (waitSeconds <= 0)
            return;

        lock (ThrottleLock)
        {
            _nextAllowedRequestUtc = DateTimeOffset.UtcNow.AddSeconds(waitSeconds);
        }
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return ReadNullableString(element, propertyName) ?? string.Empty;
    }

    private static string? ReadNullableString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static int ReadInt(JsonElement element, string propertyName)
    {
        return ReadNullableInt(element, propertyName) ?? 0;
    }

    private static int? ReadNullableInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number
            ? property.GetInt32()
            : null;
    }
}

