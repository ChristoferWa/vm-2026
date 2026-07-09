namespace VmTips.Web.Models;

public class AppSettings
{
    public int Id { get; set; }

    // Controls whether participants are allowed to submit or edit predictions.
    public bool PredictionsLocked { get; set; }

    public string RulesText { get; set; } = string.Empty;
}
