using Microsoft.EntityFrameworkCore;
using VmTips.Web.Data;
using VmTips.Web.Models;
using VmTips.Web.Services.Import;


namespace VmTips.Web.Services;

public sealed class AdminService
{
    private readonly AppDbContext _db;

    private readonly ExcelImportService _excelImportService;

    public AdminService(AppDbContext db, ExcelImportService excelImportService)
    {
        _db = db;
        _excelImportService = excelImportService;
    }

    public async Task<AppSettings> GetSettingsAsync()
    {
        var settings = await _db.AppSettings.FirstOrDefaultAsync();

        if (settings is not null)
            return settings;

        settings = new AppSettings
        {
            PredictionsLocked = false
        };

        _db.AppSettings.Add(settings);
        await _db.SaveChangesAsync();

        return settings;
    }

    public async Task<bool> SetPredictionsLockedAsync(bool locked)
    {
        var settings = await GetSettingsAsync();

        settings.PredictionsLocked = locked;

        await _db.SaveChangesAsync();

        return settings.PredictionsLocked;
    }
    public Task<ImportResult> ImportTipsWorkbookAsync()
    {
        var filePath = Path.Combine(
            AppContext.BaseDirectory,
            "Data",
            "Import",
            "vm-tips-2026.xlsx");

        if (!File.Exists(filePath))
        {
            filePath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "Data",
                "Import",
                "vm-tips-2026.xlsx");
        }

        var result = _excelImportService.ImportTipsWorkbook(filePath);

        return Task.FromResult(result);
    }
}