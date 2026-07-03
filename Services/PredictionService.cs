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
    public async Task SaveKickoffAsync(int matchId, DateTime kickoffUtc)
    {
        var match = await db.Matches.FirstAsync(x => x.Id == matchId);

        match.KickoffUtc = kickoffUtc;

        await db.SaveChangesAsync();

        logger.LogInformation("Saved kickoff for match {MatchId}", matchId);
    }
}
