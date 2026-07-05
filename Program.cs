using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using VmTips.Web.Components;
using VmTips.Web.Data;
using VmTips.Web.Services;
using VmTips.Web.Services.Import;

var builder = WebApplication.CreateBuilder(args);

// Configure structured logging for the first version. In production, add Serilog or Application Insights.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
// Hide noisy MudBlazor debug logs during development.
builder.Logging.AddFilter("MudBlazor", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
builder.Services.AddScoped<ExcelImportService>();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUserService>();
builder.Services.AddScoped<PredictionService>();
builder.Services.AddScoped<LeaderboardService>();
builder.Services.AddScoped<AdminService>();

var databaseProvider = builder.Configuration["Database:Provider"] ?? "Postgres";

void ConfigureDatabase(DbContextOptionsBuilder options)
{
    if (databaseProvider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlServer(builder.Configuration.GetConnectionString("SqlServer"));
    }
    else if (databaseProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
    {
        options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres"));
    }
    else
    {
        var sqlitePath = builder.Environment.IsProduction()
     ? "/var/data/vmtips.db"
     : "vmtips.db";

        options.UseSqlite($"Data Source={sqlitePath}");
    }

    options.EnableSensitiveDataLogging(builder.Environment.IsDevelopment());
}

builder.Services.AddDbContext<AppDbContext>(ConfigureDatabase);
builder.Services.AddDbContextFactory<AppDbContext>(ConfigureDatabase, ServiceLifetime.Scoped);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

// Creates the database and inserts sample data for the first version.
// Replace EnsureCreated with EF migrations when the model stabilizes.
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    logger.LogInformation("Preparing database using provider {Provider}", databaseProvider);
    db.Database.Migrate();
    SeedData.EnsureSeeded(db, logger);
}

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
