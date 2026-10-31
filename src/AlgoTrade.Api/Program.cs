using AlgoTrade.Api.Endpoints;
using AlgoTrade.Api.Infrastructure;
using AlgoTrade.Data;
using AlgoTrade.Data.Repos;
using AlgoTrade.Data.Turso;

var builder = WebApplication.CreateBuilder(args);

var tursoUrlRaw = builder.Configuration["Turso:Url"] ?? "";
var tursoToken = builder.Configuration["Turso:Token"] ?? "";

if (string.IsNullOrWhiteSpace(tursoUrlRaw) || tursoToken.Contains("PASTE_YOUR", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine(
        """
        ┌──────────────────────────────────────────────────────────────────────┐
        │  AlgoTrade: Turso credentials missing.                               │
        │  Paste your database URL + token into                                │
        │  src/AlgoTrade.Api/appsettings.json → Turso:Url / Turso:Token        │
        │  Until then /api/health reports db=unavailable; the frontend's       │
        │  demo mode (browser engine) keeps working.                           │
        └──────────────────────────────────────────────────────────────────────┘
        """);
}

/* libsql:// is the platform URL scheme — the HTTP API lives on https:// same host */
var tursoUrl = tursoUrlRaw.StartsWith("libsql://", StringComparison.OrdinalIgnoreCase)
    ? "https://" + tursoUrlRaw["libsql://".Length..]
    : tursoUrlRaw;

builder.Services.AddSingleton(_ => new TursoClient(tursoUrl, tursoToken));
builder.Services.AddSingleton<DatasetRepo>();
builder.Services.AddSingleton<CandleRepo>();
builder.Services.AddSingleton<StrategyRepo>();
builder.Services.AddSingleton<RunRepo>();
builder.Services.AddSingleton<BenchmarkRepo>();
builder.Services.AddSingleton<LiveFeedState>();
builder.Services.AddSingleton<CsvStore>();
builder.Services.AddHostedService<BinancePoller>();

var app = builder.Build();

/* static frontend first, API under /api (no conflicts) */
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapSystemEndpoints();
app.MapDatasetEndpoints();
app.MapStrategyEndpoints();
app.MapRunEndpoints();
app.MapBenchmarkEndpoints();
app.MapLiveEndpoints();

/* schema bootstrap in the background — a Turso outage must not kill the UI */
var turso = app.Services.GetRequiredService<TursoClient>();
var logger = app.Services.GetRequiredService<ILogger<Program>>();
_ = Task.Run(async () =>
{
    for (int attempt = 1; attempt <= 5; attempt++)
    {
        try
        {
            await Schema.EnsureCreatedAsync(turso);
            logger.LogInformation("[AlgoTrade] Turso schema ready ({Attempt})", attempt);

            /* first-run content: seed the three built-in strategies ONLY
               when the Strategy table is empty, so a fresh database can
               run a backtest immediately. Failures are non-fatal. */
            try
            {
                var strategies = app.Services.GetRequiredService<StrategyRepo>();
                if ((await strategies.ListAsync()).Count == 0)
                {
                    foreach (var s in SeedStrategies.Defaults()) await strategies.InsertAsync(s);
                    logger.LogInformation("[AlgoTrade] seeded 3 default strategies (empty Strategy table)");
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning("[AlgoTrade] strategy seed skipped: {Message}", ex.Message);
            }
            return;
        }
        catch (Exception ex)
        {
            logger.LogWarning("[AlgoTrade] schema bootstrap attempt {Attempt} failed: {Message}",
                attempt, ex.Message);
            await Task.Delay(TimeSpan.FromSeconds(8 * attempt));
        }
    }
    logger.LogError(
        "[AlgoTrade] schema bootstrap failed after 5 attempts — /api/health will show db=unavailable; restart after fixing credentials");
});

app.Run();
