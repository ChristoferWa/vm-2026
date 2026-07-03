using Microsoft.EntityFrameworkCore;
using VmTips.Web.Data;
using VmTips.Web.Models;

namespace VmTips.Web.Services;

public sealed class LeaderboardService(AppDbContext db)
{
    private const int CorrectHomeGoalsPoints = 2;
    private const int CorrectAwayGoalsPoints = 2;
    private const int CorrectOutcomePoints = 2;

    public async Task<List<LeaderboardRow>> GetLeaderboardAsync(int? currentParticipantId = null)
    {
        var participants = await db.Participants
            .Include(x => x.Predictions)
            .ThenInclude(x => x.Match)
            .OrderBy(x => x.DisplayName)
            .ToListAsync();

        var sortedRows = participants
     .Select(p => new
     {
         Participant = p,
         Score = CalculateParticipantScore(p)
     })
     .OrderByDescending(x => x.Score.Points)
     .ThenByDescending(x => x.Score.CorrectScore)
     .ThenByDescending(x => x.Score.CorrectOutcome)
     .ThenBy(x => x.Participant.DisplayName)
     .ToList();

        var leaderboardRows = new List<LeaderboardRow>();

        for (var i = 0; i < sortedRows.Count; i++)
        {
            var current = sortedRows[i];

            var rank = i + 1;

            if (i > 0)
            {
                var previous = sortedRows[i - 1];

                var sameRank =
                    current.Score.Points == previous.Score.Points &&
                    current.Score.CorrectScore == previous.Score.CorrectScore &&
                    current.Score.CorrectOutcome == previous.Score.CorrectOutcome;

                if (sameRank)
                    rank = leaderboardRows[i - 1].Rank;
            }

            leaderboardRows.Add(new LeaderboardRow(
                rank,
                current.Participant.DisplayName,
                current.Score.Points,
                current.Score.CorrectScore,
                current.Score.CorrectOutcome,
                current.Score.PredictionsSubmitted,
                current.Participant.Id == currentParticipantId));
        }

        return leaderboardRows;
    }
    public static int CalculatePredictionPoints(Prediction prediction, Match match)
    {
        if (prediction.HomeGoals is null || prediction.AwayGoals is null)
            return 0;

        if (match.HomeGoals is null || match.AwayGoals is null)
            return 0;

        var points = 0;

        if (prediction.HomeGoals == match.HomeGoals)
            points += CorrectHomeGoalsPoints;

        if (prediction.AwayGoals == match.AwayGoals)
            points += CorrectAwayGoalsPoints;

        if (GetOutcome(prediction.HomeGoals.Value, prediction.AwayGoals.Value) == GetOutcome(match.HomeGoals.Value, match.AwayGoals.Value))
            points += CorrectOutcomePoints;

        return points;
    }
    private static (int Points, int CorrectScore, int CorrectOutcome, int PredictionsSubmitted) CalculateParticipantScore(Participant participant)
    {
        var points = 0;
        var correctScore = 0;
        var correctOutcome = 0;
        var predictionsSubmitted = 0;

        foreach (var prediction in participant.Predictions)
        {
            if (prediction.HomeGoals is null || prediction.AwayGoals is null)
                continue;

            predictionsSubmitted++;

            var match = prediction.Match;

            if (match.HomeGoals is null || match.AwayGoals is null)
                continue;

            var predictedHomeGoals = prediction.HomeGoals.Value;
            var predictedAwayGoals = prediction.AwayGoals.Value;
            var actualHomeGoals = match.HomeGoals.Value;
            var actualAwayGoals = match.AwayGoals.Value;

            points += CalculatePredictionPoints(prediction, match);

            if (GetOutcome(predictedHomeGoals, predictedAwayGoals) == GetOutcome(actualHomeGoals, actualAwayGoals))
            {
                correctOutcome++;
            }

            if (predictedHomeGoals == actualHomeGoals && predictedAwayGoals == actualAwayGoals)
                correctScore++;
        }

        return (points, correctScore, correctOutcome, predictionsSubmitted);
    }

    private static int GetOutcome(int homeGoals, int awayGoals)
    {
        return homeGoals.CompareTo(awayGoals);
    }
}