using AlgoTrade.Api.Infrastructure;
using AlgoTrade.Core.Benchmark;
using AlgoTrade.Data.Repos;
using Microsoft.AspNetCore.Mvc;

namespace AlgoTrade.Api.Endpoints;

/* ═══════════════════════════════════════════════════════════════
   Benchmark + live endpoints (frozen contract):

     GET /api/benchmark?algos=a,b,c&size=10000
         → { batchId, machine, results: [ { algo, label, unit, cx, size,
               naive: {ms, ops}, opt: {ms, ops}, speedup } ] }
         REAL measured timings + operation counters, persisted to
         AlgorithmBenchmark (BatchId groups the batch, MachineInfo kept).

     GET /api/live/ticker  → [ { symbol, price, prev, delta } ]
     GET /api/live/candles?symbol=&tf=&limit=
         → { status: LIVE|CACHED|OFFLINE, symbol, tf, lastUpdateUtc, candles }
   ═══════════════════════════════════════════════════════════════ */
public static class BenchmarkEndpoints
{
    private static readonly string[] KnownAlgos =
        { "hashtable", "sliding", "stack", "segment", "mergesort", "quicksort", "heap" };

    public static IEndpointRouteBuilder MapBenchmarkEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/benchmark", async (string? algos, int? size,
            BenchmarkRepo repo, ILogger<Program> log) =>
        {
            int n = size is > 99 ? Math.Min(size.Value, 1_000_000) : 10_000;

            var keys = (algos ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(a => KnownAlgos.Contains(a))
                .Distinct()
                .ToArray();
            if (keys.Length == 0) keys = KnownAlgos;

            var rows = DsaBench.Run(keys, n);
            string batchId = Guid.NewGuid().ToString("N");
            string machine = DsaBench.MachineInfo();

            /* persist — a DB outage must never cost the user their measurement */
            try { await repo.SaveBatchAsync(batchId, machine, rows); }
            catch (Exception ex) { log.LogWarning("Benchmark persist failed: {Message}", ex.Message); }

            return Results.Json(new
            {
                batchId,
                machine,
                results = rows.Select(r => new
                {
                    algo = r.Algo,
                    label = r.Label,
                    unit = r.Unit,
                    cx = r.Cx,
                    size = r.Size,
                    naive = new { ms = System.Math.Round(r.NaiveMs, 3), ops = r.NaiveOps },
                    opt = new { ms = System.Math.Round(r.OptMs, 3), ops = r.OptOps },
                    speedup = r.OptMs > 0 ? System.Math.Round(r.NaiveMs / r.OptMs, 2) : 0,
                }),
            });
        }).WithTags("Benchmark");

        return app;
    }
}

public static class LiveEndpoints
{
    public static IEndpointRouteBuilder MapLiveEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/live/ticker", (LiveFeedState state) =>
            Results.Json(state.SnapshotTicker().Select(t => new
            {
                symbol = t.Symbol,
                price = t.Price,
                prev = t.Prev,
                delta = t.Delta,
            }))).WithTags("Live");

        app.MapGet("/api/live/candles", (LiveFeedState state, int? limit, string? symbol, string? tf) =>
        {
            var (status, lastError, lastUpdate) = state.SnapshotMeta();
            return Results.Json(new
            {
                status,
                symbol = string.IsNullOrEmpty(symbol) ? state.Symbol : symbol,
                tf = string.IsNullOrEmpty(tf) ? state.Interval : tf,
                lastUpdateUtc = lastUpdate?.ToString("yyyy-MM-ddTHH:mm:ss"),
                error = lastError,
                candles = state.SnapshotCandles(limit is > 0 ? limit.Value : 240),
            });
        }).WithTags("Live");

        return app;
    }
}
