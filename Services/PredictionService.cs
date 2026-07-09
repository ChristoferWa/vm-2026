using Microsoft.EntityFrameworkCore;
using VmTips.Web.Data;
using VmTips.Web.Models;

namespace VmTips.Web.Services;

public sealed class PredictionService(AppDbContext db, ILogger<PredictionService> logger)
{
    public async Task<List<Match>> GetMatchesWithPredictionsAsync(int participantId)
    {
        return await db.Matches
       .AsNoTracking()
       .Include(x => x.HomeTeam)
       .Include(x => x.AwayTeam)
       .Include(x => x.Predictions.Where(p => p.ParticipantId == participantId))
       .OrderBy(x => x.KickoffUtc)
       .ToListAsync();
    }
    public async Task<List<Match>> GetTodaysMatchesWithAllPredictionsAsync()
    {
        var todayUtc = DateTime.UtcNow.Date;
        var tomorrowUtc = todayUtc.AddDays(1);

        return await db.Matches
            .AsNoTracking()
            .Include(x => x.HomeTeam)
            .Include(x => x.AwayTeam)
            .Include(x => x.Predictions)
                .ThenInclude(x => x.Participant)
            .Where(x => x.KickoffUtc >= todayUtc && x.KickoffUtc < tomorrowUtc)
            .OrderBy(x => x.KickoffUtc)
            .ToListAsync();
    }
    public async Task<Match?> GetMatchWithAllPredictionsAsync(int matchId)
    {
        return await db.Matches
            .AsNoTracking()
            .Include(x => x.HomeTeam)
            .Include(x => x.AwayTeam)
            .Include(x => x.Predictions)
                .ThenInclude(x => x.Participant)
            .FirstOrDefaultAsync(x => x.Id == matchId);
    }
    public async Task<List<Match>> GetMatchesForResultAdminAsync()
    {
        return await db.Matches
            .AsNoTracking()
            .Include(x => x.HomeTeam)
            .Include(x => x.AwayTeam)
            .OrderBy(x => x.KickoffUtc)
            .ToListAsync();
    }
    public async Task<List<Participant>> GetParticipantsAsync()
    {
        return await db.Participants
            .AsNoTracking()
            .OrderBy(x => x.DisplayName)
            .ToListAsync();
    }
    public async Task SavePredictionAsync(int participantId, int matchId, int? homeGoals, int? awayGoals)
    {
        var match = await db.Matches.FirstAsync(x => x.Id == matchId);

        if (match.HomeGoals is not null && match.AwayGoals is not null)
            throw new InvalidOperationException("This match already has a result and can no longer be predicted.");

        var predictionsLocked = await db.AppSettings
            .Select(x => (bool?)x.PredictionsLocked)
            .FirstOrDefaultAsync()
            ?? false;

        if (predictionsLocked)
            throw new InvalidOperationException("Predictions are locked.");

        var prediction = await db.Predictions.FirstOrDefaultAsync(x => x.ParticipantId == participantId && x.MatchId == matchId);
        if (prediction is null)
        {
            prediction = new Prediction { ParticipantId = participantId, MatchId = matchId };
            db.Predictions.Add(prediction);
        }

        prediction.HomeGoals = homeGoals;
        prediction.AwayGoals = awayGoals;
        prediction.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        logger.LogInformation("Saved prediction for participant {ParticipantId} and match {MatchId}", participantId, matchId);
    }

    public async Task SaveResultAsync(int matchId, int? homeGoals, int? awayGoals)
    {
        var match = await db.Matches.FirstAsync(x => x.Id == matchId);
        match.HomeGoals = homeGoals;
        match.AwayGoals = awayGoals;
        match.Status = homeGoals is null || awayGoals is null ? MatchStatus.Scheduled : MatchStatus.Finished;

        await db.SaveChangesAsync();
        logger.LogInformation("Saved result for match {MatchId}", matchId);
    }

    public async Task<int> FillNextKnockoutRoundAsync(string sourceStage, string targetStage, IReadOnlyDictionary<int, int?> advancingTeamIds)
    {
        var matches = await db.Matches
            .Include(x => x.HomeTeam)
            .Include(x => x.AwayTeam)
            .ToListAsync();

        var sourceMatches = matches
            .Where(x => StageKey(x.GroupName) == StageKey(sourceStage))
            .OrderBy(x => x.KickoffUtc)
            .ThenBy(x => x.MatchNo)
            .ToList();

        var targetMatches = matches
            .Where(x => StageKey(x.GroupName) == StageKey(targetStage))
            .OrderBy(x => x.KickoffUtc)
            .ThenBy(x => x.MatchNo)
            .ToList();

        if (sourceMatches.Count == 0 || targetMatches.Count == 0)
            throw new InvalidOperationException("Could not find matches for both rounds.");

        var winners = sourceMatches
            .Select(match => GetAdvancingTeamId(match, advancingTeamIds))
            .ToList();

        if (winners.Any(x => x is null))
            throw new InvalidOperationException("All source matches must have a result. Tied matches need an advancing team.");

        var updates = 0;

        for (var targetIndex = 0; targetIndex < targetMatches.Count; targetIndex++)
        {
            var winnerIndex = targetIndex * 2;

            if (winnerIndex + 1 >= winners.Count)
                break;

            var targetMatch = targetMatches[targetIndex];
            var homeTeamId = winners[winnerIndex]!.Value;
            var awayTeamId = winners[winnerIndex + 1]!.Value;

            if (targetMatch.HomeTeamId != homeTeamId)
            {
                targetMatch.HomeTeamId = homeTeamId;
                updates++;
            }

            if (targetMatch.AwayTeamId != awayTeamId)
            {
                targetMatch.AwayTeamId = awayTeamId;
                updates++;
            }
        }

        await db.SaveChangesAsync();

        logger.LogInformation("Filled {TargetStage} from {SourceStage} with {UpdateCount} team slot updates", targetStage, sourceStage, updates);

        return updates;
    }

    private static int? GetAdvancingTeamId(Match match, IReadOnlyDictionary<int, int?> advancingTeamIds)
    {
        if (match.HomeGoals is null || match.AwayGoals is null)
            return null;

        if (match.HomeGoals > match.AwayGoals)
            return match.HomeTeamId;

        if (match.AwayGoals > match.HomeGoals)
            return match.AwayTeamId;

        if (!advancingTeamIds.TryGetValue(match.Id, out var advancingTeamId))
            return null;

        return advancingTeamId == match.HomeTeamId || advancingTeamId == match.AwayTeamId
            ? advancingTeamId
            : null;
    }

    private static string StageKey(string groupName)
    {
        var value = groupName.Trim().ToLowerInvariant();

        if (value.Contains("sextondel") || value.Contains("round of 32") || value.Contains("last 32"))
            return "roundof32";

        if (value.Contains("åtton") || value.Contains("atton") || value.Contains("round of 16") || value.Contains("last 16"))
            return "roundof16";

        if (value.Contains("kvart") || value.Contains("quarter"))
            return "quarterfinal";

        if (value.Contains("semi"))
            return "semifinal";

        if (value == "final" || value.Contains(" final"))
            return "final";

        return value;
    }
    public async Task SaveKickoffAsync(int matchId, DateTime kickoffUtc)
    {
        var match = await db.Matches.FirstAsync(x => x.Id == matchId);

        match.KickoffUtc = kickoffUtc;

        await db.SaveChangesAsync();

        logger.LogInformation("Saved kickoff for match {MatchId}", matchId);
    }

    public async Task SaveGroupNameAsync(int matchId, string groupName)
    {
        var normalizedGroupName = groupName.Trim();

        if (string.IsNullOrWhiteSpace(normalizedGroupName))
            throw new InvalidOperationException("Stage cannot be empty.");

        var match = await db.Matches.FirstAsync(x => x.Id == matchId);

        match.GroupName = normalizedGroupName;

        await db.SaveChangesAsync();

        logger.LogInformation("Saved stage/group for match {MatchId}", matchId);
    }
}
