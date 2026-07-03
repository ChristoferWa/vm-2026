namespace VmTips.Web.Models;

public enum MatchStatus
{
    Scheduled = 0,
    Live = 1,
    Finished = 2
}

public sealed class Participant
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string AccessCode { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public ICollection<Prediction> Predictions { get; set; } = new List<Prediction>();
}

public sealed class Team
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string FlagEmoji { get; set; } = string.Empty;
}

public sealed class Match
{
    public int Id { get; set; }
    public int MatchNo { get; set; }
    public DateTime KickoffUtc { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public int HomeTeamId { get; set; }
    public Team HomeTeam { get; set; } = default!;
    public int AwayTeamId { get; set; }
    public Team AwayTeam { get; set; } = default!;
    public MatchStatus Status { get; set; } = MatchStatus.Scheduled;
    public int? HomeGoals { get; set; }
    public int? AwayGoals { get; set; }
    public ICollection<Prediction> Predictions { get; set; } = new List<Prediction>();
}

public sealed class Prediction
{
    public int Id { get; set; }
    public int ParticipantId { get; set; }
    public Participant Participant { get; set; } = default!;
    public int MatchId { get; set; }
    public Match Match { get; set; } = default!;
    public int? HomeGoals { get; set; }
    public int? AwayGoals { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed record LeaderboardRow(
    int Rank,
    string ParticipantName,
    int Points,
    int CorrectScore,
    int CorrectOutcome,
    int PredictionsSubmitted,
    bool IsCurrentUser);
