using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VmTips.Web.Data;
using VmTips.Web.Models;

namespace VmTips.Web.Services;

public sealed class ResultSyncService(
    HttpClient httpClient,
    AppDbContext db,
    IOptions<ResultsApiSettings> options,
    ILogger<ResultSyncService> logger)
{
    private readonly ResultsApiSettings _settings = options.Value;
    private static readonly object ThrottleLock = new();
    private static DateTimeOffset _nextAllowedRequestUtc = DateTimeOffset.MinValue;

    private static readonly Dictionary<string, string> TeamAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["algeriet"] = "algeria",
        ["bosnia herzegovina"] = "bosnia herzegovina",
        ["bosnia and herzegovina"] = "bosnia herzegovina",
        ["bosnien och hercegovina"] = "bosnia herzegovina",
        ["brasilien"] = "brazil",
        ["cape verde islands"] = "cape verde",
        ["cabo verde"] = "cape verde",
        ["kap verde"] = "cape verde",
        ["congo dr"] = "congo dr",
        ["dr congo"] = "congo dr",
        ["kongo dr"] = "congo dr",
        ["dr kongo"] = "congo dr",
        ["kongo kinshasa"] = "congo dr",
        ["curacao"] = "curacao",
        ["egypten"] = "egypt",
        ["elfenbenskusten"] = "ivory coast",
        ["kanada"] = "canada",
        ["kroatien"] = "croatia",
        ["irak"] = "iraq",
        ["irland"] = "ireland",
        ["ivory coast"] = "ivory coast",
        ["frankrike"] = "france",
        ["nya zeeland"] = "new zealand",
        ["osterrike"] = "austria",
        ["österrike"] = "austria",
        ["saudiarabien"] = "saudi arabia",
        ["saudi arabien"] = "saudi arabia",
        ["skottland"] = "scotland",
        ["sverige"] = "sweden",
        ["tyskland"] = "germany",
        ["marocko"] = "morocco",
        ["mexiko"] = "mexico",
        ["nederlanderna"] = "netherlands",
        ["norge"] = "norway",
        ["spanien"] = "spain",
        ["sydafrika"] = "south africa",
        ["south africa"] = "south africa",
        ["sydkorea"] = "south korea",
        ["schweiz"] = "switzerland",
        ["tjeckien"] = "czechia",
        ["usa"] = "united states"
    };

    private static readonly Dictionary<string, string> ApiCodesByTeamName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["argentina"] = "ARG",
        ["australia"] = "AUS",
        ["australien"] = "AUS",
        ["belgium"] = "BEL",
        ["belgien"] = "BEL",
        ["brazil"] = "BRA",
        ["brasilien"] = "BRA",
        ["canada"] = "CAN",
        ["kanada"] = "CAN",
        ["croatia"] = "CRO",
        ["kroatien"] = "CRO",
        ["czechia"] = "CZE",
        ["tjeckien"] = "CZE",
        ["england"] = "ENG",
        ["france"] = "FRA",
        ["frankrike"] = "FRA",
        ["germany"] = "GER",
        ["tyskland"] = "GER",
        ["japan"] = "JPN",
        ["mexico"] = "MEX",
        ["mexiko"] = "MEX",
        ["morocco"] = "MAR",
        ["marocko"] = "MAR",
        ["netherlands"] = "NED",
        ["nederlanderna"] = "NED",
        ["norway"] = "NOR",
        ["norge"] = "NOR",
        ["paraguay"] = "PAR",
        ["portugal"] = "POR",
        ["algeria"] = "ALG",
        ["algeriet"] = "ALG",
        ["austria"] = "AUT",
        ["osterrike"] = "AUT",
        ["österrike"] = "AUT",
        ["bosnia herzegovina"] = "BIH",
        ["bosnia and herzegovina"] = "BIH",
        ["bosnien och hercegovina"] = "BIH",
        ["cape verde"] = "CPV",
        ["cape verde islands"] = "CPV",
        ["cabo verde"] = "CPV",
        ["kap verde"] = "CPV",
        ["colombia"] = "COL",
        ["congo dr"] = "COD",
        ["dr congo"] = "COD",
        ["kongo dr"] = "COD",
        ["dr kongo"] = "COD",
        ["kongo kinshasa"] = "COD",
        ["curacao"] = "CUW",
        ["egypt"] = "EGY",
        ["egypten"] = "EGY",
        ["haiti"] = "HAI",
        ["iran"] = "IRN",
        ["iraq"] = "IRQ",
        ["irak"] = "IRQ",
        ["ivory coast"] = "CIV",
        ["elfenbenskusten"] = "CIV",
        ["jordan"] = "JOR",
        ["jordanien"] = "JOR",
        ["new zealand"] = "NZL",
        ["nya zeeland"] = "NZL",
        ["saudi arabia"] = "KSA",
        ["saudiarabien"] = "KSA",
        ["saudi arabien"] = "KSA",
        ["scotland"] = "SCO",
        ["skottland"] = "SCO",
        ["senegal"] = "SEN",
        ["south africa"] = "RSA",
        ["sydafrika"] = "RSA",
        ["south korea"] = "KOR",
        ["sydkorea"] = "KOR",
        ["spain"] = "ESP",
        ["spanien"] = "ESP",
        ["sweden"] = "SWE",
        ["sverige"] = "SWE",
        ["switzerland"] = "SUI",
        ["schweiz"] = "SUI",
        ["tunisia"] = "TUN",
        ["tunisien"] = "TUN",
        ["uruguay"] = "URU",
        ["united states"] = "USA",
        ["usa"] = "USA"
    };

    public async Task<ResultSyncPreview> PreviewLatestResultsAsync()
    {
        if (!_settings.Provider.Equals("FootballData", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported results API provider: {_settings.Provider}");

        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
            throw new InvalidOperationException("Missing ResultsApi:ApiKey. Add it locally or as a Render environment variable.");

        var apiMatches = await GetFootballDataMatchesAsync();
        var localMatches = await db.Matches
            .AsNoTracking()
            .Include(x => x.HomeTeam)
            .Include(x => x.AwayTeam)
            .ToListAsync();

        var finishedApiMatches = apiMatches
            .Where(x => x.IsFinished && x.HomeGoals is not null && x.AwayGoals is not null)
            .ToList();
        var localTeamKeys = BuildLocalTeamKeys(localMatches);
        var preview = new ResultSyncPreview
        {
            ApiMatchesFetched = apiMatches.Count,
            FinishedApiMatches = finishedApiMatches.Count
        };

        foreach (var apiMatch in finishedApiMatches)
        {
            if (!ApiTeamExistsLocally(apiMatch.HomeTeamName, apiMatch.HomeTeamCode, localTeamKeys)
                || !ApiTeamExistsLocally(apiMatch.AwayTeamName, apiMatch.AwayTeamCode, localTeamKeys))
            {
                preview.IgnoredApiMatches++;
                continue;
            }

            var apiHomeGoals = apiMatch.HomeGoals!.Value;
            var apiAwayGoals = apiMatch.AwayGoals!.Value;
            var localMatch = FindLocalMatch(apiMatch, localMatches);

            if (localMatch is null)
            {
                preview.Skipped.Add($"{apiMatch.HomeTeamName} vs {apiMatch.AwayTeamName} could not be matched by teams/date.");
                continue;
            }

            if (localMatch.HomeGoals == apiHomeGoals && localMatch.AwayGoals == apiAwayGoals)
                continue;

            preview.Updates.Add(new ResultSyncUpdate
            {
                MatchId = localMatch.Id,
                MatchNo = localMatch.MatchNo,
                HomeTeamName = localMatch.HomeTeam.Name,
                AwayTeamName = localMatch.AwayTeam.Name,
                KickoffUtc = localMatch.KickoffUtc,
                CurrentHomeGoals = localMatch.HomeGoals,
                CurrentAwayGoals = localMatch.AwayGoals,
                ApiHomeGoals = apiHomeGoals,
                ApiAwayGoals = apiAwayGoals,
                SourceMatch = $"{apiMatch.HomeTeamName} vs {apiMatch.AwayTeamName}"
            });
        }

        return preview;
    }

    public async Task<int> ApplyUpdatesAsync(IEnumerable<ResultSyncUpdate> updates)
    {
        var updateList = updates.ToList();

        if (updateList.Count == 0)
            return 0;

        var updatesByMatchId = updateList.ToDictionary(x => x.MatchId);
        var matches = await db.Matches
            .Where(x => updatesByMatchId.Keys.Contains(x.Id))
            .ToListAsync();

        foreach (var match in matches)
        {
            var update = updatesByMatchId[match.Id];
            match.HomeGoals = update.ApiHomeGoals;
            match.AwayGoals = update.ApiAwayGoals;
            match.Status = MatchStatus.Finished;
        }

        await db.SaveChangesAsync();

        logger.LogInformation("Applied {Count} result updates from API", matches.Count);

        return matches.Count;
    }

    private async Task<List<ApiMatchResult>> GetFootballDataMatchesAsync()
    {
        ThrowIfThrottled();

        var baseUrl = _settings.BaseUrl.TrimEnd('/');
        var dateFrom = DateTime.UtcNow.Date.AddDays(-_settings.DateWindowDaysBefore).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var dateTo = DateTime.UtcNow.Date.AddDays(_settings.DateWindowDaysAfter).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var url = $"{baseUrl}/competitions/{_settings.CompetitionCode}/matches?dateFrom={dateFrom}&dateTo={dateTo}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Auth-Token", _settings.ApiKey);

        using var response = await httpClient.SendAsync(request);
        UpdateThrottleState(response);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Results API returned {(int)response.StatusCode} {response.ReasonPhrase}.");

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);

        if (!document.RootElement.TryGetProperty("matches", out var matchesElement))
            return [];

        var matches = new List<ApiMatchResult>();

        foreach (var matchElement in matchesElement.EnumerateArray())
        {
            var status = ReadString(matchElement, "status");
            var utcDateText = ReadString(matchElement, "utcDate");
            var utcDate = DateTime.TryParse(utcDateText, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsedDate)
                ? parsedDate.ToUniversalTime()
                : DateTime.MinValue;

            var homeTeam = matchElement.GetProperty("homeTeam");
            var awayTeam = matchElement.GetProperty("awayTeam");
            var score = ReadPreferredScore(matchElement.GetProperty("score"));

            matches.Add(new ApiMatchResult(
                ReadString(homeTeam, "name"),
                ReadString(homeTeam, "tla"),
                ReadString(awayTeam, "name"),
                ReadString(awayTeam, "tla"),
                utcDate,
                status,
                score.HomeGoals,
                score.AwayGoals));
        }

        return matches;
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

    private static Match? FindLocalMatch(ApiMatchResult apiMatch, List<Match> localMatches)
    {
        var candidates = localMatches
            .Where(x => TeamsMatch(apiMatch.HomeTeamName, apiMatch.HomeTeamCode, x.HomeTeam)
                && TeamsMatch(apiMatch.AwayTeamName, apiMatch.AwayTeamCode, x.AwayTeam))
            .ToList();

        if (candidates.Count == 0)
            return null;

        return candidates
            .OrderBy(x => Math.Abs((x.KickoffUtc - apiMatch.KickoffUtc).TotalHours))
            .FirstOrDefault(x => Math.Abs((x.KickoffUtc - apiMatch.KickoffUtc).TotalHours) <= 48)
            ?? candidates.FirstOrDefault();
    }

    private static HashSet<string> BuildLocalTeamKeys(List<Match> localMatches)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var match in localMatches)
        {
            AddLocalTeamKeys(keys, match.HomeTeam);
            AddLocalTeamKeys(keys, match.AwayTeam);
        }

        return keys;
    }

    private static void AddLocalTeamKeys(HashSet<string> keys, Team team)
    {
        keys.Add(NormalizeTeamName(team.Name));

        if (!string.IsNullOrWhiteSpace(team.Code))
            keys.Add(team.Code);

        var expectedApiCode = GetExpectedApiCode(team);

        if (!string.IsNullOrWhiteSpace(expectedApiCode))
            keys.Add(expectedApiCode);
    }

    private static bool ApiTeamExistsLocally(string apiName, string apiCode, HashSet<string> localTeamKeys)
    {
        if (!string.IsNullOrWhiteSpace(apiCode) && localTeamKeys.Contains(apiCode))
            return true;

        return localTeamKeys.Contains(NormalizeTeamName(apiName));
    }

    private static bool TeamsMatch(string apiName, string apiCode, Team localTeam)
    {
        if (!string.IsNullOrWhiteSpace(apiCode) && apiCode.Equals(localTeam.Code, StringComparison.OrdinalIgnoreCase))
            return true;

        var expectedApiCode = GetExpectedApiCode(localTeam);

        if (!string.IsNullOrWhiteSpace(apiCode) && apiCode.Equals(expectedApiCode, StringComparison.OrdinalIgnoreCase))
            return true;

        return NormalizeTeamName(apiName) == NormalizeTeamName(localTeam.Name);
    }

    private static string GetExpectedApiCode(Team localTeam)
    {
        var normalizedName = NormalizeTeamName(localTeam.Name);

        if (ApiCodesByTeamName.TryGetValue(normalizedName, out var apiCode))
            return apiCode;

        return localTeam.Code;
    }

    private static string NormalizeTeamName(string teamName)
    {
        var normalized = RemoveDiacritics(teamName)
            .ToLowerInvariant()
            .Replace(".", string.Empty)
            .Replace("-", " ")
            .Trim();

        while (normalized.Contains("  ", StringComparison.Ordinal))
            normalized = normalized.Replace("  ", " ");

        return TeamAliases.TryGetValue(normalized, out var alias)
            ? alias
            : normalized;
    }

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(character);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static int? ReadNullableInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number
            ? property.GetInt32()
            : null;
    }

    private static ApiScore ReadPreferredScore(JsonElement scoreElement)
    {
        if (scoreElement.TryGetProperty("regularTime", out var regularTimeScore)
            && TryReadCompleteScore(regularTimeScore, out var regularTime))
        {
            return regularTime;
        }

        if (scoreElement.TryGetProperty("fullTime", out var fullTimeScore)
            && TryReadCompleteScore(fullTimeScore, out var fullTime))
        {
            return fullTime;
        }

        return new ApiScore(null, null);
    }

    private static bool TryReadCompleteScore(JsonElement scoreElement, out ApiScore score)
    {
        var homeGoals = ReadNullableInt(scoreElement, "home");
        var awayGoals = ReadNullableInt(scoreElement, "away");

        score = new ApiScore(homeGoals, awayGoals);

        return homeGoals is not null && awayGoals is not null;
    }

    private sealed record ApiScore(int? HomeGoals, int? AwayGoals);

    private sealed record ApiMatchResult(
        string HomeTeamName,
        string HomeTeamCode,
        string AwayTeamName,
        string AwayTeamCode,
        DateTime KickoffUtc,
        string Status,
        int? HomeGoals,
        int? AwayGoals)
    {
        public bool IsFinished => Status.Equals("FINISHED", StringComparison.OrdinalIgnoreCase);
    }
}
