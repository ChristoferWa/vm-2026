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

        var previousRanks = BuildRankLookup(
            participants,
            match => match.KickoffUtc.Date < TodayInSweden);

        return BuildLeaderboardRows(
            participants,
            currentParticipantId,
            match => true,
            previousRanks);
    }
    public static int CalculatePredictionPoints(Prediction prediction, Match match)
    {
        return CalculatePredictionPointsBreakdown(prediction, match).TotalPoints;
    }

    public static (int GoalPoints, int OutcomePoints, int TotalPoints) CalculatePredictionPointsBreakdown(Prediction prediction, Match match)
    {
        if (prediction.HomeGoals is null || prediction.AwayGoals is null)
            return (0, 0, 0);

        if (match.HomeGoals is null || match.AwayGoals is null)
            return (0, 0, 0);

        var goalPoints = 0;

        if (prediction.HomeGoals == match.HomeGoals)
            goalPoints += CorrectHomeGoalsPoints;

        if (prediction.AwayGoals == match.AwayGoals)
            goalPoints += CorrectAwayGoalsPoints;

        var outcomePoints = 0;

        if (GetOutcome(prediction.HomeGoals.Value, prediction.AwayGoals.Value) == GetOutcome(match.HomeGoals.Value, match.AwayGoals.Value))
            outcomePoints += CorrectOutcomePoints;

        return (goalPoints, outcomePoints, goalPoints + outcomePoints);
    }
    private static List<LeaderboardRow> BuildLeaderboardRows(
        List<Participant> participants,
        int? currentParticipantId,
        Func<Match, bool> includeMatch,
        IReadOnlyDictionary<int, int> previousRanks)
    {
        var sortedRows = participants
            .Select(p => new
            {
                Participant = p,
                Score = CalculateParticipantScore(p, includeMatch)
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

            previousRanks.TryGetValue(current.Participant.Id, out var previousRank);
            var rankChange = previousRank == 0 ? 0 : previousRank - rank;

            leaderboardRows.Add(new LeaderboardRow(
                rank,
                current.Participant.DisplayName,
                current.Score.Points,
                current.Score.CorrectScore,
                current.Score.CorrectOutcome,
                current.Score.PredictionsSubmitted,
                rankChange,
                current.Participant.Id == currentParticipantId));
        }

        return leaderboardRows;
    }

    private static Dictionary<int, int> BuildRankLookup(List<Participant> participants, Func<Match, bool> includeMatch)
    {
        return BuildLeaderboardRows(participants, null, includeMatch, new Dictionary<int, int>())
            .Zip(
                participants
                    .OrderByDescending(p => CalculateParticipantScore(p, includeMatch).Points)
                    .ThenByDescending(p => CalculateParticipantScore(p, includeMatch).CorrectScore)
                    .ThenByDescending(p => CalculateParticipantScore(p, includeMatch).CorrectOutcome)
                    .ThenBy(p => p.DisplayName),
                (row, participant) => new { participant.Id, row.Rank })
            .ToDictionary(x => x.Id, x => x.Rank);
    }

    private static (int Points, int CorrectScore, int CorrectOutcome, int PredictionsSubmitted) CalculateParticipantScore(
        Participant participant,
        Func<Match, bool> includeMatch)
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

            if (!includeMatch(match))
                continue;

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

    private static DateTime TodayInSweden => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, SwedishTimeZone).Date;

    private static TimeZoneInfo SwedishTimeZone => _swedishTimeZone ??= GetSwedishTimeZone();

    private static TimeZoneInfo? _swedishTimeZone;

    private static TimeZoneInfo GetSwedishTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
        }
    }

    private static int GetOutcome(int homeGoals, int awayGoals)
    {
        return homeGoals.CompareTo(awayGoals);
    }
}
