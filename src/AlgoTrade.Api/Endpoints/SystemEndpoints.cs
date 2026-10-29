using AlgoTrade.Api.Infrastructure;
using AlgoTrade.Data.Repos;
using AlgoTrade.Data.Turso;
using Microsoft.AspNetCore.Mvc;

namespace AlgoTrade.Api.Endpoints;

public static class SystemEndpoints
{
    private static readonly object HealthLock = new();
    private static bool _dbOk;
    private static DateTime _dbCheckedAt = DateTime.MinValue;

    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", async (TursoClient db, LiveFeedState live) =>
        {
            bool dbOk;
            lock (HealthLock)
            {
                if ((DateTime.UtcNow - _dbCheckedAt).TotalSeconds > 15)
                {
                    try
                    {
                        db.OneAsync("SELECT 1").GetAwaiter().GetResult().EnsureOk();
                        _dbOk = true;
                    }
                    catch { _dbOk = false; }
                    _dbCheckedAt = DateTime.UtcNow;
                }
                dbOk = _dbOk;
            }
            var (status, lastError, lastUpdate) = live.SnapshotMeta();
            return Results.Json(new
            {
                mode = "live",
                engine = "AlgoTrade C# Engine 1.0 (.NET 10)",
                db = dbOk ? "connected" : "unavailable",
                live = status,
                liveError = lastError,
                lastUpdateUtc = lastUpdate?.ToString("yyyy-MM-ddTHH:mm:ss"),
                ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"),
                version = "1.0.0",
            });
        }).WithTags("System");

        return app;
    }
}
