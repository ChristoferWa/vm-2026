using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using VmTips.Web.Data;
using VmTips.Web.Models;

namespace VmTips.Web.Services.Import;

public sealed class ExcelImportService
{
    private readonly AppDbContext _db;
    private readonly ILogger<ExcelImportService> _logger;

    public ExcelImportService(AppDbContext db, ILogger<ExcelImportService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public void Import(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Import file was not found: {filePath}");
        }

        using var workbook = new XLWorkbook(filePath);

        ImportTeams(workbook);
        ImportParticipants(workbook);
        ImportMatches(workbook);
        ImportPredictions(workbook);

        _db.SaveChanges();

        _logger.LogInformation("Excel import completed from {FilePath}", filePath);
    }
    public ImportResult ImportTipsWorkbook(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Tips file was not found: {filePath}");
        }

        using var workbook = new XLWorkbook(filePath);

        return ImportTipsWorkbook(workbook, filePath);
    }

    public ImportResult ImportTipsWorkbook(Stream fileStream)
    {
        using var workbook = new XLWorkbook(fileStream);

        return ImportTipsWorkbook(workbook, "uploaded workbook");
    }

    private ImportResult ImportTipsWorkbook(XLWorkbook workbook, string sourceName)
    {
        var result = new ImportResult();

        ImportTipsWorkbookResults(workbook, result);
        ImportTipsWorkbookPredictions(workbook, result);

        _db.SaveChanges();

        _logger.LogInformation("Tips workbook import completed from {SourceName}", sourceName);

        return result;
    }

    private void ImportTipsWorkbookResults(XLWorkbook workbook, ImportResult result)
    {
        var sheet = workbook.Worksheet("📊 Resultat & Tabell");

        RemoveSampleMatchesWithoutMatchNumbers();

        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var matchNo = TryReadInt(row.Cell(3));
            var kickoffLocal = TryReadKickoffLocal(row.Cell(4), row.Cell(5));
            var homeTeamName = row.Cell(9).GetString().Trim();
            var awayTeamName = row.Cell(10).GetString().Trim();

            var homeGoals = TryReadInt(row.Cell(11));
            var awayGoals = TryReadInt(row.Cell(12));

            if (matchNo is null || string.IsNullOrWhiteSpace(homeTeamName) || string.IsNullOrWhiteSpace(awayTeamName))
                continue;

            var homeTeam = GetOrCreateTeam(homeTeamName);
            var awayTeam = GetOrCreateTeam(awayTeamName);

            var match = _db.Matches.FirstOrDefault(x => x.MatchNo == matchNo.Value);

            if (match is null)
            {
                match = new Match
                {
                    MatchNo = matchNo.Value,
                    GroupName = ReadGroupName(row),
                    KickoffUtc = kickoffLocal?.ToUniversalTime() ?? DateTime.UtcNow,
                    HomeTeamId = homeTeam.Id,
                    AwayTeamId = awayTeam.Id,
                    Status = MatchStatus.Scheduled
                };

                _db.Matches.Add(match);
            }
            else
            {
                match.HomeTeamId = homeTeam.Id;
                match.AwayTeamId = awayTeam.Id;
                match.GroupName = ReadGroupName(row);
            }

            if (kickoffLocal is not null)
            {
                var kickoffUtc = kickoffLocal.Value.ToUniversalTime();

                if (match.KickoffUtc != kickoffUtc)
                {
                    match.KickoffUtc = kickoffUtc;
                    result.KickoffTimesUpdated++;
                }
            }

            if (match.HomeGoals != homeGoals || match.AwayGoals != awayGoals)
            {
                match.HomeGoals = homeGoals;
                match.AwayGoals = awayGoals;
                result.ResultsUpdated++;
            }

            match.Status = homeGoals is null || awayGoals is null
                ? MatchStatus.Scheduled
                : MatchStatus.Finished;

            _db.SaveChanges();
        }
    }
    private void ImportTipsWorkbookPredictions(XLWorkbook workbook, ImportResult result)
    {
        var ignoredSheets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "🏆 Leaderboard",
        "⚽ Match Center",
        "📊 Resultat & Tabell",
        "Poäng & instruktioner",
        "Favoriter & Odds",
        "tippare mall",
        "Teams",
        "Participants",
        "Matches"
    };

        var participantsByName = _db.Participants
            .ToDictionary(x => x.DisplayName.Trim(), StringComparer.OrdinalIgnoreCase);

        var matches = _db.Matches
     .Include(x => x.HomeTeam)
     .Include(x => x.AwayTeam)
     .ToList();

        var existingPredictions = _db.Predictions
            .ToList()
            .ToDictionary(x => (x.ParticipantId, x.MatchId));

        foreach (var sheet in workbook.Worksheets)
        {
            if (ignoredSheets.Contains(sheet.Name))
                continue;

            if (!participantsByName.TryGetValue(sheet.Name.Trim(), out var participant))
                continue;

            foreach (var row in sheet.RowsUsed().Skip(2))
            {
                var matchNo = TryReadInt(row.Cell(1));
                var homeGoals = TryReadInt(row.Cell(6));
                var awayGoals = TryReadInt(row.Cell(7));

                if (matchNo is null || homeGoals is null || awayGoals is null)
                    continue;

                var match = matches.FirstOrDefault(x => x.MatchNo == matchNo.Value);

                if (match is null)
                    continue;

                existingPredictions.TryGetValue((participant.Id, match.Id), out var prediction);

                if (prediction is null)
                {
                    prediction = new Prediction
                    {
                        ParticipantId = participant.Id,
                        MatchId = match.Id
                    };

                    _db.Predictions.Add(prediction);
                    existingPredictions[(participant.Id, match.Id)] = prediction;
                    result.PredictionsCreated++;
                }
                if (prediction.HomeGoals != homeGoals || prediction.AwayGoals != awayGoals)
                {
                    result.PredictionsUpdated++;
                }
                else
                {
                    result.PredictionsUnchanged++;
                }
                prediction.HomeGoals = homeGoals;
                prediction.AwayGoals = awayGoals;
                prediction.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        _db.SaveChanges();
    }
    private Match? GetMatchByMatchNo(XLWorkbook workbook, List<Match> matches, int matchNo)
    {
        var sheet = workbook.Worksheet("📊 Resultat & Tabell");

        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var rowMatchNo = TryReadInt(row.Cell(3));

            if (rowMatchNo != matchNo)
                continue;

            var homeTeamName = row.Cell(9).GetString().Trim();
            var awayTeamName = row.Cell(10).GetString().Trim();

            if (string.IsNullOrWhiteSpace(homeTeamName) || string.IsNullOrWhiteSpace(awayTeamName))
                return null;

            return matches.FirstOrDefault(x =>
                string.Equals(x.HomeTeam.Name, homeTeamName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.AwayTeam.Name, awayTeamName, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private void ImportTeams(XLWorkbook workbook)
    {
        var sheet = workbook.Worksheet("Teams");

        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var code = row.Cell(1).GetString().Trim().ToUpperInvariant();
            var name = row.Cell(2).GetString().Trim();
            var flagEmoji = row.Cell(3).GetString().Trim();

            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
                continue;

            if (_db.Teams.Any(x => x.Code == code))
                continue;

            _db.Teams.Add(new Team
            {
                Code = code,
                Name = name,
                FlagEmoji = flagEmoji
            });
        }

        _db.SaveChanges();
    }

    private void ImportParticipants(XLWorkbook workbook)
    {
        var sheet = workbook.Worksheet("Participants");

        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var displayName = row.Cell(1).GetString().Trim();
            var accessCode = row.Cell(2).GetString().Trim().ToUpperInvariant();
            var isAdmin = ReadBool(row.Cell(3).GetString());

            if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(accessCode))
                continue;

            var existingParticipant = _db.Participants.FirstOrDefault(x => x.AccessCode == accessCode);

            if (existingParticipant is not null)
            {
                existingParticipant.DisplayName = displayName;
                existingParticipant.IsAdmin = isAdmin || IsKnownAdmin(displayName, accessCode);
                continue;
            }

            _db.Participants.Add(new Participant
            {
                DisplayName = displayName,
                AccessCode = accessCode,
                IsAdmin = isAdmin || IsKnownAdmin(displayName, accessCode)
            });
        }

        _db.SaveChanges();
    }

    private void ImportMatches(XLWorkbook workbook)
    {
        var sheet = workbook.Worksheet("Matches");
        var teamsByCode = _db.Teams.ToDictionary(x => x.Code);

        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var matchNo = row.Cell(1).GetValue<int>();
            var kickoffUtc = ReadKickoffDate(row.Cell(2));
            var stage = row.Cell(3).GetString().Trim();
            var groupName = ReadGroupName(stage, row.Cell(4).GetString().Trim());
            var venue = row.Cell(5).GetString().Trim();
            var homeCode = row.Cell(6).GetString().Trim().ToUpperInvariant();
            var awayCode = row.Cell(7).GetString().Trim().ToUpperInvariant();
            var statusText = row.Cell(8).GetString().Trim();

            int? homeGoals = TryReadInt(row.Cell(9));
            int? awayGoals = TryReadInt(row.Cell(10));

            if (!teamsByCode.TryGetValue(homeCode, out var homeTeam))
                throw new InvalidOperationException($"Unknown HomeCode '{homeCode}' on match row {row.RowNumber()}.");

            if (!teamsByCode.TryGetValue(awayCode, out var awayTeam))
                throw new InvalidOperationException($"Unknown AwayCode '{awayCode}' on match row {row.RowNumber()}.");

            var match = _db.Matches.FirstOrDefault(x =>
    x.HomeTeamId == homeTeam.Id &&
    x.AwayTeamId == awayTeam.Id);

            if (match is null)
            {
                _db.Matches.Add(new Match
                {
                    MatchNo = matchNo,
                    KickoffUtc = kickoffUtc,
                    GroupName = groupName,
                    HomeTeamId = homeTeam.Id,
                    AwayTeamId = awayTeam.Id,
                    Status = ParseStatus(statusText),
                    HomeGoals = homeGoals,
                    AwayGoals = awayGoals
                });

                continue;
            }

            match.KickoffUtc = kickoffUtc;
            match.GroupName = groupName;
            match.Status = ParseStatus(statusText);
            match.HomeGoals = homeGoals;
            match.AwayGoals = awayGoals;
        }

        _db.SaveChanges();
    }
    private void ImportPredictions(XLWorkbook workbook)
    {
        var ignoredSheets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "🏆 Leaderboard",
        "⚽ Match Center",
        "📊 Resultat & Tabell",
        "Poäng & instruktioner",
        "Favoriter & Odds",
        "tippare mall",
        "Teams",
        "Participants",
        "Matches"
    };

        var participantsByName = _db.Participants
            .ToDictionary(x => x.DisplayName.Trim(), StringComparer.OrdinalIgnoreCase);

        var matchesByMatchNo = GetMatchesByMatchNo(workbook);

        foreach (var sheet in workbook.Worksheets)
        {
            if (ignoredSheets.Contains(sheet.Name))
                continue;

            if (!participantsByName.TryGetValue(sheet.Name.Trim(), out var participant))
                continue;

            foreach (var row in sheet.RowsUsed().Skip(3))
            {
                var matchNo = TryReadInt(row.Cell(1));
                var homeGoals = TryReadInt(row.Cell(6));
                var awayGoals = TryReadInt(row.Cell(7));

                if (matchNo is null || homeGoals is null || awayGoals is null)
                    continue;

                if (!matchesByMatchNo.TryGetValue(matchNo.Value, out var match))
                    continue;

                var prediction = _db.Predictions.FirstOrDefault(x =>
                    x.ParticipantId == participant.Id &&
                    x.MatchId == match.Id);

                if (prediction is null)
                {
                    prediction = new Prediction
                    {
                        ParticipantId = participant.Id,
                        MatchId = match.Id
                    };

                    _db.Predictions.Add(prediction);
                }

                prediction.HomeGoals = homeGoals;
                prediction.AwayGoals = awayGoals;
                prediction.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        _db.SaveChanges();
    }
    private Dictionary<int, Match> GetMatchesByMatchNo(XLWorkbook workbook)
    {
        var sheet = workbook.Worksheet("Matches");
        var teamsByCode = _db.Teams.ToDictionary(x => x.Code);
        var result = new Dictionary<int, Match>();

        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var matchNo = row.Cell(1).GetValue<int>();
            var kickoffUtc = ReadKickoffDate(row.Cell(2));
            var homeCode = row.Cell(6).GetString().Trim().ToUpperInvariant();
            var awayCode = row.Cell(7).GetString().Trim().ToUpperInvariant();

            if (!teamsByCode.TryGetValue(homeCode, out var homeTeam))
                continue;

            if (!teamsByCode.TryGetValue(awayCode, out var awayTeam))
                continue;

            var match = _db.Matches.FirstOrDefault(x =>
                x.HomeTeamId == homeTeam.Id &&
                x.AwayTeamId == awayTeam.Id &&
                x.KickoffUtc == kickoffUtc);

            match ??= _db.Matches.FirstOrDefault(x =>
                x.HomeTeamId == homeTeam.Id &&
                x.AwayTeamId == awayTeam.Id);

            if (match is not null)
                result[matchNo] = match;
        }

        return result;
    }
    private static DateTime ReadKickoffDate(IXLCell cell)
    {
        if (cell.TryGetValue<DateTime>(out var parsedDate))
        {
            return DateTime.SpecifyKind(parsedDate.Date.AddHours(12), DateTimeKind.Utc);
        }

        var rawValue = cell.GetString().Trim();

        if (DateTime.TryParse(rawValue, out var parsedTextDate))
        {
            return DateTime.SpecifyKind(parsedTextDate.Date.AddHours(12), DateTimeKind.Utc);
        }

        throw new InvalidOperationException($"Could not parse kickoff date value '{rawValue}'.");
    }

    private static MatchStatus ParseStatus(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "scheduled" => MatchStatus.Scheduled,
            "finished" => MatchStatus.Finished,
            "live" => MatchStatus.Live,
            _ => MatchStatus.Scheduled
        };
    }

    private static int? TryReadInt(IXLCell cell)
    {
        if (cell.IsEmpty())
            return null;

        if (int.TryParse(cell.GetString(), out var value))
            return value;

        if (cell.TryGetValue<int>(out var number))
            return number;

        return null;
    }
    private static DateTime? TryReadKickoffLocal(IXLCell dateCell, IXLCell timeCell)
    {
        DateTime? date = null;
        TimeSpan? time = null;

        if (dateCell.TryGetValue<DateTime>(out var parsedDate))
            date = parsedDate.Date;
        else if (DateTime.TryParse(dateCell.GetString().Trim(), out var parsedDateText))
            date = parsedDateText.Date;

        if (timeCell.TryGetValue<DateTime>(out var parsedTimeDate))
            time = parsedTimeDate.TimeOfDay;
        else if (timeCell.TryGetValue<TimeSpan>(out var parsedTimeSpan))
            time = parsedTimeSpan;
        else if (TimeSpan.TryParse(timeCell.GetString().Trim(), out var parsedTimeText))
            time = parsedTimeText;

        if (date is null)
            return null;

        return date.Value.Add(time ?? TimeSpan.Zero);
    }
    private void RemoveSampleMatchesWithoutMatchNumbers()
    {
        var sampleMatches = _db.Matches
            .Where(x => x.MatchNo <= 0)
            .ToList();

        if (sampleMatches.Count == 0)
            return;

        var sampleMatchIds = sampleMatches.Select(x => x.Id).ToList();
        var samplePredictions = _db.Predictions
            .Where(x => sampleMatchIds.Contains(x.MatchId))
            .ToList();

        _db.Predictions.RemoveRange(samplePredictions);
        _db.Matches.RemoveRange(sampleMatches);
        _db.SaveChanges();
    }

    private Team GetOrCreateTeam(string teamName)
    {
        var normalizedName = NormalizeTeamName(teamName);

        var team = _db.Teams
            .AsEnumerable()
            .FirstOrDefault(x => NormalizeTeamName(x.Name) == normalizedName);

        if (team is not null)
        {
            var flag = TeamFlags.GetFlag(teamName);

            if (!string.IsNullOrWhiteSpace(flag) && string.IsNullOrWhiteSpace(team.FlagEmoji))
            {
                team.FlagEmoji = flag;
                _db.SaveChanges();
            }

            return team;
        }

        team = new Team
        {
            Code = CreateTeamCode(teamName),
            Name = teamName.Trim(),
            FlagEmoji = TeamFlags.GetFlag(teamName)
        };

        _db.Teams.Add(team);
        _db.SaveChanges();

        return team;
    }

    private static string ReadGroupName(IXLRow row)
    {
        var groupName = row.Cell(8).GetString().Trim();
        var stageName = row.Cell(7).GetString().Trim();

        return ReadGroupName(stageName, groupName);
    }

    private static string ReadGroupName(string stageName, string groupName)
    {
        return IsKnockoutStageName(stageName) || string.IsNullOrWhiteSpace(groupName)
            ? stageName
            : groupName;
    }

    private static bool IsKnockoutStageName(string stageName)
    {
        var value = stageName.Trim().ToLowerInvariant();

        return value.Contains("sextondel")
            || value.Contains("round of 32")
            || value.Contains("last 32")
            || value.Contains("åtton")
            || value.Contains("round of 16")
            || value.Contains("last 16")
            || value.Contains("kvart")
            || value.Contains("quarter")
            || value.Contains("semi")
            || value == "final"
            || value.Contains(" final");
    }

    private string CreateTeamCode(string teamName)
    {
        var normalized = NormalizeTeamName(teamName);
        var baseCode = new string(normalized
            .Where(char.IsLetterOrDigit)
            .Take(3)
            .ToArray())
            .ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(baseCode))
            baseCode = "TEAM";

        var code = baseCode;
        var suffix = 2;

        while (_db.Teams.Any(x => x.Code == code))
        {
            code = $"{baseCode}{suffix}";
            suffix++;
        }

        return code;
    }

    private static string NormalizeTeamName(string value)
    {
        return value.Trim().ToUpperInvariant();
    }

    private static bool ReadBool(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "true" => true,
            "yes" => true,
            "1" => true,
            "ja" => true,
            _ => false
        };
    }

    private static bool IsKnownAdmin(string displayName, string accessCode)
    {
        return accessCode.Trim().Equals("CHRIS", StringComparison.OrdinalIgnoreCase) ||
            displayName.Trim().Equals("Christofer", StringComparison.OrdinalIgnoreCase);
    }
}
