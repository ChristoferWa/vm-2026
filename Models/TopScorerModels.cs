namespace VmTips.Web.Models;

public sealed class TopScorersResult
{
    public DateTime FetchedAtUtc { get; set; } = DateTime.UtcNow;
    public List<TopScorerRow> Scorers { get; set; } = [];
}

public sealed class TopScorerRow
{
    public int Rank { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    public string TeamCode { get; set; } = string.Empty;
    public string? TeamCrestUrl { get; set; }
    public int Goals { get; set; }
    public int? Assists { get; set; }
    public int? Penalties { get; set; }
}

