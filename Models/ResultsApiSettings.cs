namespace VmTips.Web.Models;

public sealed class ResultsApiSettings
{
    public string Provider { get; set; } = "FootballData";
    public string BaseUrl { get; set; } = "https://api.football-data.org/v4";
    public string ApiKey { get; set; } = string.Empty;
    public string CompetitionCode { get; set; } = "WC";
    public int DateWindowDaysBefore { get; set; } = 30;
    public int DateWindowDaysAfter { get; set; } = 30;
}

