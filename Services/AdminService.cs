using Microsoft.EntityFrameworkCore;
using VmTips.Web.Data;
using VmTips.Web.Models;
using VmTips.Web.Services.Import;


namespace VmTips.Web.Services;

public sealed class AdminService
{
    private readonly AppDbContext _db;

    private readonly ExcelImportService _excelImportService;
    private readonly ILogger<AdminService> _logger;

    public AdminService(AppDbContext db, ExcelImportService excelImportService, ILogger<AdminService> logger)
    {
        _db = db;
        _excelImportService = excelImportService;
        _logger = logger;
    }

    public async Task<AppSettings> GetSettingsAsync()
    {
        var settings = await _db.AppSettings.FirstOrDefaultAsync();

        if (settings is not null)
        {
            if (string.IsNullOrWhiteSpace(settings.RulesText))
            {
                settings.RulesText = DefaultRulesText;
                await _db.SaveChangesAsync();
            }

            return settings;
        }

        settings = new AppSettings
        {
            PredictionsLocked = false,
            RulesText = DefaultRulesText
        };

        _db.AppSettings.Add(settings);
        await _db.SaveChangesAsync();

        return settings;
    }

    public async Task<bool> SetPredictionsLockedAsync(bool locked)
    {
        var updatedRows = await _db.AppSettings
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.PredictionsLocked, locked));

        if (updatedRows == 0)
        {
            _db.AppSettings.Add(new AppSettings
            {
                PredictionsLocked = locked,
                RulesText = DefaultRulesText
            });

            await _db.SaveChangesAsync();
        }

        return locked;
    }

    public async Task<bool> GetPredictionsLockedAsync()
    {
        return await _db.AppSettings
            .Select(x => (bool?)x.PredictionsLocked)
            .FirstOrDefaultAsync()
            ?? false;
    }

    public async Task<string> GetRulesTextAsync()
    {
        try
        {
            var rulesText = await _db.AppSettings
                .Select(x => x.RulesText)
                .FirstOrDefaultAsync();

            return string.IsNullOrWhiteSpace(rulesText)
                ? DefaultRulesText
                : rulesText;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load rules text from settings. Falling back to default rules.");
            return DefaultRulesText;
        }
    }

    public async Task<string> SetRulesTextAsync(string rulesText)
    {
        var settings = await GetSettingsAsync();

        settings.RulesText = string.IsNullOrWhiteSpace(rulesText)
            ? DefaultRulesText
            : rulesText.Trim();

        await _db.SaveChangesAsync();

        return settings.RulesText;
    }

    public Task<ImportResult> ImportTipsWorkbookAsync(Stream fileStream)
    {
        var result = _excelImportService.ImportTipsWorkbook(fileStream);

        return Task.FromResult(result);
    }

    public const string DefaultRulesText = """
Scoring system
- Correct score: 5 points
- Correct outcome: 2 points
- Wrong prediction: 0 points

Other rules
- You must submit your prediction before kick-off time.
- Changes are allowed until predictions are locked or the match has a result.
- In case of a tie, correct score predictions are prioritized.
""";
}
