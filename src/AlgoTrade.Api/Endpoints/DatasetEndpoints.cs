using AlgoTrade.Core.Dsa;
using AlgoTrade.Core.Domain;
using AlgoTrade.Core.Engine;
using AlgoTrade.Core.Import;
using AlgoTrade.Data.Repos;
using Microsoft.AspNetCore.Mvc;

namespace AlgoTrade.Api.Endpoints;


public static class DatasetEndpoints
{
    public sealed record ImportBody(string? Name, string? CsvText);

    public static IEndpointRouteBuilder MapDatasetEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/datasets").WithTags("Datasets");

        g.MapGet("", async (DatasetRepo datasets, CandleRepo candles) =>
        {
            var metas = await datasets.ListAsync();
            var rows = new List<object>(metas.Count);
            foreach (var d in metas)
            {
                var spark = await candles.SampleClosesAsync(d.Id, 40);
                rows.Add(new
                {
                    id = d.Id,
                    name = d.Name,
                    symbol = d.InstrumentLabel ?? "",
                    timeframe = d.Timeframe ?? "",
                    source = d.SourceFileName ?? "",
                    importedAt = d.ImportedAtUtc,
                    rowsRead = d.RowsRead,
                    rowsAccepted = d.RowsAccepted,
                    rowsRejected = d.RowsRejected,
                    firstTs = d.FirstTimestamp,
                    lastTs = d.LastTimestamp,
                    issues = d.ValidationIssues.Count,
                    spark,
                });
            }
            return Results.Json(rows);
        });

        g.MapGet("/{id:long}", async (long id, DatasetRepo datasets, CandleRepo candles) =>
        {
            var meta = await datasets.GetAsync(id);
            if (meta is null) return Results.NotFound(new { error = 404 });
            var list = await candles.LoadAllAsync(id);
            return Results.Json(new
            {
                id = meta.Id,
                name = meta.Name,
                symbol = meta.InstrumentLabel ?? "",
                timeframe = meta.Timeframe ?? "",
                source = meta.SourceFileName ?? "",
                importedAt = meta.ImportedAtUtc,
                rowsRead = meta.RowsRead,
                rowsAccepted = meta.RowsAccepted,
                rowsRejected = meta.RowsRejected,
                firstTs = meta.FirstTimestamp,
                lastTs = meta.LastTimestamp,
                issues = meta.ValidationIssues,
                candles = list,
            });
        });

        g.MapPost("/import", async (ImportBody body, DatasetRepo datasets, CandleRepo candles) =>
        {
            var name = string.IsNullOrWhiteSpace(body.Name) ? "unnamed-import" : body.Name.Trim();
            var parsed = CsvCandleParser.Parse(body.CsvText ?? "");
            int rejected = parsed.Issues.Count(i => i.Rejected);

            if (parsed.Candles.Count == 0)
                return Results.Json(new
                {
                    ok = false, issues = parsed.Issues, read = parsed.Read,
                    accepted = 0, rejected, dups = parsed.Dups, outOfOrder = parsed.OutOfOrder,
                });

            var meta = new DatasetMeta
            {
                Name = name,
                InstrumentLabel = GuessSymbol(name),
                Timeframe = GuessTimeframe(parsed.Candles),
                SourceFileName = "csv-import",
                RowsRead = parsed.Read,
                RowsAccepted = parsed.Candles.Count,
                RowsRejected = rejected,
                FirstTimestamp = parsed.Candles[0].Ts,
                LastTimestamp = parsed.Candles[^1].Ts,
            };
            long id = await datasets.InsertAsync(meta, parsed.Issues);
            await candles.BulkUpsertAsync(id, parsed.Candles);

            return Results.Json(new
            {
                ok = true, datasetId = id, issues = parsed.Issues, read = parsed.Read,
                accepted = parsed.Candles.Count, rejected, dups = parsed.Dups, outOfOrder = parsed.OutOfOrder,
            });
        });

        g.MapPost("/demo", async (DatasetRepo datasets, CandleRepo candles) =>
        {
            var spec = DemoGenerator.Default;
            var list = DemoGenerator.GenCandles(spec);
            var meta = new DatasetMeta
            {
                Name = "DOGEUSDT · 5m (demo fetch)",
                InstrumentLabel = spec.Symbol,
                Timeframe = "5m",
                SourceFileName = "live-api (Binance)",
                RowsRead = spec.N + 3,
                RowsAccepted = list.Count,
                RowsRejected = 3,
                FirstTimestamp = list[0].Ts,
                LastTimestamp = list[^1].Ts,
            };
            long id = await datasets.InsertAsync(meta, null);
            await candles.BulkUpsertAsync(id, list);
            return Results.Json(new { ok = true, datasetId = id, accepted = list.Count });
        });

        g.MapDelete("/{id:long}", async (long id, DatasetRepo datasets) =>
        {
            var ok = await datasets.DeleteAsync(id);
            if (!ok) return Results.Json(new { error = "Dataset has backtest runs — delete its runs first (FK rule)." }, statusCode: 409);
            return Results.Json(new { ok = true });
        });

        g.MapGet("/{id:long}/candles", async (long id, int? page, int? size, CandleRepo candles) =>
        {
            var (rows, total, pages) = await candles.PageAsync(id, page ?? 1, size ?? 25);
            return Results.Json(new { rows, total, page = page ?? 1, pages });
        });

        g.MapGet("/{id:long}/rangestats", async (long id, string? from, string? to, CandleRepo candles) =>
        {
            var list = await candles.LoadAllAsync(id);
            if (list.Count == 0 || string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
                return Results.Json<object?>(null);

            int fromIdx = list.FindIndex(c => string.CompareOrdinal(c.Ts, from) >= 0);
            int toIdx = -1;
            for (int i = list.Count - 1; i >= 0; i--)
                if (string.CompareOrdinal(list[i].Ts, to) <= 0) { toIdx = i; break; }
            if (fromIdx < 0 || toIdx < fromIdx) return Results.Json<object?>(null);

            var ops = new OpCounter();
            var highs = new SegmentTree(list.Select(c => c.H).ToArray(), ops);
            var lows = new SegmentTree(list.Select(c => c.L).ToArray(), ops);
            var closes = new SegmentTree(list.Select(c => c.C).ToArray(), ops);
            var vols = new SegmentTree(list.Select(c => c.V).ToArray(), ops);

            var (hh, _, _) = highs.Query(fromIdx, toIdx);
            var (_, ll, _) = lows.Query(fromIdx, toIdx);
            var (_, _, sumClose) = closes.Query(fromIdx, toIdx);
            var (_, _, sumVol) = vols.Query(fromIdx, toIdx);

            int count = toIdx - fromIdx + 1;
            return Results.Json(new
            {
                from = fromIdx,
                to = toIdx,
                count,
                highestHigh = hh,
                lowestLow = ll,
                totalVolume = sumVol,
                avgClose = sumClose / count,
                dsa = new { algorithm = "segment-tree", complexity = "O(log n) per query", ops = ops.Total },
            });
        });

        g.MapGet("/{id:long}/pricelevels", async (long id, int? limit, CandleRepo candles) =>
        {
            var list = await candles.LoadAllAsync(id);
            if (list.Count == 0) return Results.Json(Array.Empty<object>());

            var closes = list.Select(c => c.C).ToArray();
            var ng = MonotonicStack.NextGreater(closes);
            var ns = MonotonicStack.NextSmaller(closes);

            int lim = limit is > 0 ? System.Math.Min(limit.Value, closes.Length) : System.Math.Min(60, closes.Length);
            int start = closes.Length - lim;

            var rows = new List<object>(lim);
            for (int i = start; i < closes.Length; i++)
            {
                rows.Add(new
                {
                    i,
                    ts = list[i].Ts,
                    close = closes[i],
                    nextGreater = ng[i] < 0 ? null : new { i = ng[i], bars = ng[i] - i, pct = (closes[ng[i]] / closes[i] - 1) * 100 },
                    nextSmaller = ns[i] < 0 ? null : new { i = ns[i], bars = ns[i] - i, pct = (closes[ns[i]] / closes[i] - 1) * 100 },
                });
            }
            return Results.Json(rows);
        });

        return app;
    }

    /* mirrors js guessTimeframe / symbol guess */
    internal static string GuessSymbol(string name)
    {
        var token = name.Split(new[] { ' ', '·', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault();
        return string.IsNullOrEmpty(token) ? "CSV" : token.ToUpperInvariant();
    }

    internal static string GuessTimeframe(List<Candle> candles)
    {
        if (candles.Count < 3) return "unknown";
        var gaps = new List<double>();
        for (int i = 1; i < System.Math.Min(30, candles.Count); i++)
        {
            double d = (Candle.ParseTs(candles[i].Ts) - Candle.ParseTs(candles[i - 1].Ts)).TotalMinutes;
            if (d > 0) gaps.Add(d);
        }
        if (gaps.Count == 0) return "unknown";
        gaps.Sort();
        double med = gaps[gaps.Count / 2];
        return med >= 1440 ? "1d" : med >= 60 ? $"{(int)System.Math.Round(med / 60)}h" : $"{(int)System.Math.Round(med)}m";
    }
}
