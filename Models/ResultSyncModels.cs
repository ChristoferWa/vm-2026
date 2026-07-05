namespace VmTips.Web.Models;

public sealed class ResultSyncPreview
{
    public int ApiMatchesFetched { get; set; }
    public int FinishedApiMatches { get; set; }
    public int IgnoredApiMatches { get; set; }
    public List<ResultSyncUpdate> Updates { get; set; } = [];
    public List<string> Skipped { get; set; } = [];
    public string Summary => $"{Updates.Count} result updates found, {Skipped.Count} relevant skipped, {IgnoredApiMatches} ignored.";
}

public sealed class ResultSyncUpdate
{
    public int MatchId { get; set; }
    public int MatchNo { get; set; }
    public string HomeTeamName { get; set; } = string.Empty;
    public string AwayTeamName { get; set; } = string.Empty;
    public DateTime KickoffUtc { get; set; }
    public int? CurrentHomeGoals { get; set; }
    public int? CurrentAwayGoals { get; set; }
    public int ApiHomeGoals { get; set; }
    public int ApiAwayGoals { get; set; }
    public string SourceMatch { get; set; } = string.Empty;
}
