using System.Text.Json;
using AlgoTrade.Core.Domain;
using AlgoTrade.Data.Turso;

namespace AlgoTrade.Data.Repos;

/* ═══════════════════════════════════════════════════════════════
   RunRepo — persists a completed backtest (run row + trades in
   one transaction-ish pipeline), then lists / loads / deletes.

   Column snapshots (scaled) keep SQL-side queries fast; the JSON
   snapshots (equity curve, exact metrics incl. pf NULL, D23
   strategy snapshot, assumptions) rebuild the full run object.
   ═══════════════════════════════════════════════════════════════ */
public sealed class RunRepo
{
    private readonly TursoClient _db;
    public RunRepo(TursoClient db) { _db = db; }

    public async Task<long> SaveAsync(BacktestRun run, long datasetId, long? strategyId,
        string rangeStartTs, string rangeEndTs)
    {
        var m = run.M;
        var insertRun = new TursoStmt(
            """
            INSERT INTO BacktestRun
              (DatasetId, StrategyId, StrategySnapshotJson, AssumptionsJson,
               RangeStartTs, RangeEndTs, Status, ErrorMessage, ExecutionMs,
               InitialEquityI, FinalEquityI, TotalTrades, WinningTrades, LosingTrades,
               BreakEvenTrades, NetProfitI, GrossProfitI, GrossLossI, WinRatePctI,
               ProfitFactorI, MaxDrawdownAmountI, MaxDrawdownPctI, EquityCurveJson, MetricsJson)
            VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)
            """,
            datasetId, strategyId,
            JsonSerializer.Serialize(run.Snapshot),
            JsonSerializer.Serialize(run.Assumptions),
            rangeStartTs, rangeEndTs,
            run.Status, run.Error, (long)run.ExecutionMs,
            Scaling.ToI(m.Initial), Scaling.ToI(m.Final), (long)m.Total, (long)m.Wins, (long)m.Losses,
            (long)m.BreakEven, Scaling.ToI(m.Net), Scaling.ToI(m.GrossProfit), Scaling.ToI(m.GrossLoss),
            Scaling.ToPctI(m.WinRate), Scaling.ToPctINull(m.Pf), Scaling.ToI(m.MaxDd), Scaling.ToPctI(m.MaxDdPct),
            JsonSerializer.Serialize(run.Equity), JsonSerializer.Serialize(m));

        var r = await _db.OneAsync(insertRun.Sql, insertRun.Args ?? Array.Empty<object?>());
        r.EnsureOk();
        long runId = r.LastInsertRowId ?? 0;

        if (run.Trades.Count > 0)
        {
            const string tradeSql =
                "INSERT INTO Trade (RunId, TradeNo, Direction, EntryTs, EntryPriceI, ExitTs, ExitPriceI, " +
                "QuantityI, GrossPnLI, FeesI, NetPnLI, ExitReason) VALUES (?,?,?,?,?,?,?,?,?,?,?,?)";
            const int chunk = 400;
            for (int offset = 0; offset < run.Trades.Count; offset += chunk)
            {
                int take = System.Math.Min(chunk, run.Trades.Count - offset);
                var stmts = new List<TursoStmt>(take);
                for (int i = offset; i < offset + take; i++)
                {
                    var t = run.Trades[i];
                    stmts.Add(new TursoStmt(tradeSql,
                        runId, (long)t.No, 1L, t.EntryTs, Scaling.ToI(t.Entry), t.ExitTs, Scaling.ToI(t.Exit),
                        Scaling.ToI(t.Qty), Scaling.ToI(t.Gross), Scaling.ToI(t.Fees), Scaling.ToI(t.Net), t.Reason));
                }
                var results = await _db.ExecuteAsync(stmts);
                foreach (var x in results) x.EnsureOk();
            }
        }
        return runId;
    }

    public async Task<List<RunListRow>> ListAsync(int limit = 100)
    {
        var r = await _db.OneAsync(
            """
            SELECT b.RunId, b.DatasetId, b.StrategyId,
                   COALESCE(d.Name, '— dataset deleted —') AS DatasetName,
                   COALESCE(s.Name, json_extract(b.StrategySnapshotJson, '$.name'), '— strategy deleted —') AS StrategyName,
                   b.RunAtUtc, b.Status, b.ErrorMessage, b.ExecutionMs,
                   b.AssumptionsJson, b.StrategySnapshotJson,
                   b.TotalTrades, b.MetricsJson, b.EquityCurveJson
            FROM BacktestRun b
            LEFT JOIN MarketDataset d ON d.DatasetId = b.DatasetId
            LEFT JOIN Strategy s      ON s.StrategyId = b.StrategyId
            ORDER BY b.RunId DESC LIMIT ?
            """, (long)limit);
        r.EnsureOk();

        var list = new List<RunListRow>(r.RowCount);
        for (int i = 0; i < r.RowCount; i++)
        {
            // column order: 0 RunId · 1 DatasetId · 2 StrategyId · 3 DatasetName · 4 StrategyName
            //                5 RunAtUtc · 6 Status · 7 Error · 8 ExecutionMs · 9 Assumptions · 10 Snapshot
            //               11 TotalTrades · 12 MetricsJson · 13 EquityCurveJson
            var m = TryDeserialize<PerformanceMetrics>(r.SN(i, 12)) ?? new PerformanceMetrics();
            var equity = TryDeserialize<List<double>>(r.SN(i, 13)) ?? new List<double>();

            // spark — same downsampling as runLite(): every len/40-th point
            int step = System.Math.Max(1, (int)System.Math.Floor(equity.Count / 40.0));
            var spark = equity.Where((_, idx) => idx % step == 0).ToList();

            list.Add(new RunListRow
            {
                Id = r.L(i, 0),
                DatasetId = r.L(i, 1),
                StrategyId = r.IsNull(i, 2) ? null : r.L(i, 2),
                DatasetName = r.S(i, 3),
                StrategyName = r.S(i, 4),
                RunAt = r.S(i, 5),
                Status = r.S(i, 6),
                Error = r.SN(i, 7),
                ExecutionMs = (int)r.L(i, 8),
                Assumptions = TryDeserialize<Dictionary<string, object?>>(r.SN(i, 9)) ?? new(),
                Snapshot = TryDeserialize<StrategyConfig>(r.SN(i, 10)),
                TradeCount = (int)r.L(i, 11),
                NetProfit = m.Net,
                WinRatePct = m.WinRate,
                TotalTrades = m.Total,
                ProfitFactor = m.Pf,
                MaxDrawdownPct = m.MaxDdPct,
                FinalEquity = m.Final,
                Spark = spark,
            });
        }
        return list;
    }

    public async Task<BacktestRun?> GetAsync(long id)
    {
        var results = await _db.ExecuteAsync(new[]
        {
            new TursoStmt(
                """
                SELECT b.RunId, b.DatasetId, b.StrategyId,
                       COALESCE(d.Name, '— dataset deleted —') AS DatasetName,
                       COALESCE(s.Name, json_extract(b.StrategySnapshotJson, '$.name'), '— strategy deleted —') AS StrategyName,
                       b.RunAtUtc, b.Status, b.ErrorMessage, b.ExecutionMs,
                       b.AssumptionsJson, b.StrategySnapshotJson, b.EquityCurveJson, b.MetricsJson
                FROM BacktestRun b
                LEFT JOIN MarketDataset d ON d.DatasetId = b.DatasetId
                LEFT JOIN Strategy s      ON s.StrategyId = b.StrategyId
                WHERE b.RunId = ?
                """, id),
            new TursoStmt(
                "SELECT TradeNo, Direction, EntryTs, EntryPriceI, ExitTs, ExitPriceI, QuantityI, " +
                "GrossPnLI, FeesI, NetPnLI, ExitReason FROM Trade WHERE RunId = ? ORDER BY TradeNo", id),
        });
        foreach (var x in results) x.EnsureOk();
        var run = results[0];
        var tradesRes = results[1];
        if (run.RowCount == 0) return null;

        var trades = new List<Trade>(tradesRes.RowCount);
        for (int i = 0; i < tradesRes.RowCount; i++)
        {
            trades.Add(new Trade
            {
                No = (int)tradesRes.L(i, 0),
                Dir = tradesRes.L(i, 1) == 1 ? "Long" : "Short",
                EntryTs = tradesRes.S(i, 2),
                Entry = Scaling.FromI(tradesRes.L(i, 3)),
                ExitTs = tradesRes.S(i, 4),
                Exit = Scaling.FromI(tradesRes.L(i, 5)),
                Qty = Scaling.FromI(tradesRes.L(i, 6)),
                Gross = Scaling.FromI(tradesRes.L(i, 7)),
                Fees = Scaling.FromI(tradesRes.L(i, 8)),
                Net = Scaling.FromI(tradesRes.L(i, 9)),
                Reason = tradesRes.S(i, 10),
            });
        }

        return new BacktestRun
        {
            Id = run.L(0, 0),
            DatasetId = run.L(0, 1),
            StrategyId = run.IsNull(0, 2) ? null : run.L(0, 2),
            DatasetName = run.S(0, 3),
            StrategyName = run.S(0, 4),
            RunAt = run.S(0, 5),
            Status = run.S(0, 6),
            Error = run.SN(0, 7),
            ExecutionMs = (int)run.L(0, 8),
            Assumptions = TryDeserialize<Dictionary<string, object?>>(run.SN(0, 9)) ?? new(),
            Snapshot = TryDeserialize<StrategyConfig>(run.SN(0, 10)),
            Equity = TryDeserialize<List<double>>(run.SN(0, 11)) ?? new List<double>(),
            M = TryDeserialize<PerformanceMetrics>(run.SN(0, 12)) ?? new PerformanceMetrics(),
            Trades = trades,
        };
    }

    public async Task<bool> DeleteAsync(long id)
    {
        var r = await _db.OneAsync("DELETE FROM BacktestRun WHERE RunId = ?", id);
        r.EnsureOk();
        return r.AffectedRowCount > 0;   // trades cascade via FK
    }

    private static T? TryDeserialize<T>(string? json) where T : class
        => string.IsNullOrEmpty(json) ? null : TryCore<T>(json);

    private static T? TryCore<T>(string json) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(json); }
        catch (JsonException) { return null; }
    }
}
