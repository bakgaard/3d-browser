using Microsoft.EntityFrameworkCore;
using PatreonBrowser.Api.Data;
using PatreonBrowser.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.Configure<LibraryOptions>(builder.Configuration.GetSection(LibraryOptions.SectionName));

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Host=db;Port=5432;Database=patreon;Username=patreon;Password=patreon";
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));

builder.Services.AddSingleton<PathService>();
builder.Services.AddSingleton<ThumbnailService>();
builder.Services.AddSingleton<ScanState>();
builder.Services.AddSingleton<ScannerService>();

// The Angular app is served same-origin through nginx in prod; this permissive policy
// eases local `ng serve` development. The Explorer helper handles its own CORS.
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

await EnsureDatabaseAsync(app);

app.UseCors();
app.MapControllers();
app.MapGet("/api/health", () => Results.Ok(new { ok = true }));

app.Run();

static async Task EnsureDatabaseAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    for (var attempt = 1; attempt <= 15; attempt++)
    {
        try
        {
            await db.Database.EnsureCreatedAsync();
            await PatchSchemaAsync(db);
            logger.LogInformation("Database ready.");
            return;
        }
        catch (Exception ex) when (attempt < 15)
        {
            logger.LogWarning("Database not ready (attempt {Attempt}): {Message}", attempt, ex.Message);
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
}

// Idempotently add columns introduced after the initial EnsureCreated() so existing
// databases upgrade in place without dropping data. Postgres supports IF NOT EXISTS.
static async Task PatchSchemaAsync(AppDbContext db)
{
    await db.Database.ExecuteSqlRawAsync("""
        ALTER TABLE "Objects" ADD COLUMN IF NOT EXISTS "CustomName" character varying(512) NULL;
        ALTER TABLE "Objects" ADD COLUMN IF NOT EXISTS "PrimaryRenderId" integer NULL;
        ALTER TABLE "Objects" ADD COLUMN IF NOT EXISTS "CanonicalObjectId" integer NULL;
        """);
}
