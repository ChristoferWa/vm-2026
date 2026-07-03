namespace VmTips.Web.Models;

public sealed class ImportResult
{
    public int ResultsUpdated { get; set; }
    public int KickoffTimesUpdated { get; set; }
    public int PredictionsUpdated { get; set; }
    public int PredictionsCreated { get; set; }
    public int PredictionsUnchanged { get; set; }
    public int Errors { get; set; }

    public string Summary =>
        $"Results: {ResultsUpdated}, Kickoff times: {KickoffTimesUpdated}, Predictions updated: {PredictionsUpdated}, Predictions created: {PredictionsCreated}, Predictions unchanged: {PredictionsUnchanged}, Errors: {Errors}";
}