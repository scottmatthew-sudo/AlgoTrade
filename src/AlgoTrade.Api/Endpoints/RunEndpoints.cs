using System.Diagnostics;
using AlgoTrade.Core.Domain;
using AlgoTrade.Core.Engine;
using AlgoTrade.Data.Repos;
using Microsoft.AspNetCore.Mvc;

namespace AlgoTrade.Api.Endpoints;

/* ═══════════════════════════════════════════════════════════════
   Run endpoints (frozen contract):

     GET    /api/runs                  (list rows shaped like runLite())
     GET    /api/runs/{id}             (full run: m, equity, trades…)
     DELETE /api/runs/{id}
     POST   /api/backtest              { datasetId, strategyId, initialEquity }

   The backtest pipeline mirrors the browser phases:
     load candles → run engine (D7–D14) → metrics → persist → return run
   ═══════════════════════════════════════════════════════════════ */
public static class RunEndpoints
{
    public sealed record BacktestBody(long DatasetId, long StrategyId, double? InitialEquity);

    public static IEndpointRouteBuilder MapRunEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api").WithTags("Runs & Backtest");

        g.MapGet("/runs", async (RunRepo repo, int? limit) =>
            Results.Json(await repo.ListAsync(limit is > 0 ? limit.Value : 100)));

        g.MapGet("/runs/{id:long}", async (long id, RunRepo repo) =>
        {
            var run = await repo.GetAsync(id);
            if (run is null) return Results.NotFound(new { error = 404 });
            return Results.Json(run);
        });

        g.MapDelete("/runs/{id:long}", async (long id, RunRepo repo) =>
        {
            var ok = await repo.DeleteAsync(id);
            return ok ? Results.Json(new { ok = true }) : Results.NotFound(new { error = 404 });
        });

        g.MapPost("/backtest", async (BacktestBody body, DatasetRepo datasets, CandleRepo candles,
            StrategyRepo strategies, RunRepo runs) =>
        {
            var meta = await datasets.GetAsync(body.DatasetId);
            if (meta is null) return Results.Json(new { error = "Dataset not found" }, statusCode: 404);

            var strategy = await strategies.GetAsync(body.StrategyId);
            if (strategy is null) return Results.Json(new { error = "Strategy not found" }, statusCode: 404);

            double initialEquity = body.InitialEquity is > 0 ? body.InitialEquity.Value : 10000;

            var sw = Stopwatch.StartNew();

            /* load → simulate → metrics (all deterministic, D7–D14) */
            var candleList = await candles.LoadAllAsync(body.DatasetId);
            if (candleList.Count == 0)
                return Results.Json(new { error = "Dataset has no candles" }, statusCode: 400);

            var output = BacktestEngine.Run(candleList, strategy, initialEquity);
            var metrics = MetricsCalculator.Compute(output.Trades, output.Equity, initialEquity);
            sw.Stop();

            var run = new BacktestRun
            {
                Id = 0,
                DatasetId = body.DatasetId,
                StrategyId = strategy.Id,
                DatasetName = meta.Name,
                StrategyName = strategy.Name,
                RunAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"),
                Status = "Completed",
                Error = null,
                ExecutionMs = System.Math.Max(1, (int)System.Math.Round(sw.Elapsed.TotalMilliseconds)),
                Equity = output.Equity.ToList(),
                Trades = output.Trades,
                Assumptions = BacktestRun.BuildAssumptions(initialEquity, strategy.Risk.FeePerTrade),
                Snapshot = strategy,
                M = metrics,
            };

            long runId = await runs.SaveAsync(run, body.DatasetId, strategy.Id,
                candleList[0].Ts, candleList[^1].Ts);
            run.Id = runId;

            return Results.Json(run);
        });

        return app;
    }
}
