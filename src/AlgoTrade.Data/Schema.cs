namespace AlgoTrade.Data;

/* ═══════════════════════════════════════════════════════════════
   Schema — DDL translated from Blueprint Section 5 into SQLite
   (Turso). Mirrors docs/001_schema_turso.sql plus three nullable
   extension columns needed by the frozen web contract:

     MarketDataset.ValidationIssuesJson  — full import report (D18 UI)
     BacktestRun.EquityCurveJson         — D12 curve for the chart
     BacktestRun.MetricsJson             — exact metrics incl. pf NULL

   Scaled columns remain the queryable source of truth; the JSON
   columns are fidelity snapshots for the UI.

   ⚠ Databases created from the blueprint SQL (or an earlier build)
   predate those columns, and CREATE TABLE IF NOT EXISTS never
   upgrades an existing table — so EnsureCreatedAsync also runs
   Migrations (ALTER TABLE ADD COLUMN) on every start. All three
   are nullable TEXT → instant, data-safe, idempotent; the
   "duplicate column" failure on already-upgraded DBs is expected
   and swallowed.
   ═══════════════════════════════════════════════════════════════ */
public static class Schema
{
    public static readonly string[] Ddl =
    {
        """
        CREATE TABLE IF NOT EXISTS MarketDataset (
          DatasetId      INTEGER PRIMARY KEY AUTOINCREMENT,
          Name           TEXT    NOT NULL UNIQUE,
          InstrumentLabel TEXT   NULL,
          Timeframe      TEXT    NULL,
          SourceFileName TEXT    NULL,
          ImportedAtUtc  TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%S','now')),
          RowsRead       INTEGER NOT NULL,
          RowsAccepted   INTEGER NOT NULL,
          RowsRejected   INTEGER NOT NULL,
          FirstTimestamp TEXT    NULL,
          LastTimestamp  TEXT    NULL,
          ValidationIssuesJson TEXT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS Candle (
          CandleId INTEGER PRIMARY KEY AUTOINCREMENT,
          DatasetId INTEGER NOT NULL REFERENCES MarketDataset(DatasetId) ON DELETE CASCADE,
          Ts       TEXT    NOT NULL,
          OpenI    INTEGER NOT NULL,
          HighI    INTEGER NOT NULL,
          LowI     INTEGER NOT NULL,
          CloseI   INTEGER NOT NULL,
          VolumeI  INTEGER NOT NULL DEFAULT 0,
          CONSTRAINT UQ_Candle_Dataset_Ts UNIQUE (DatasetId, Ts),
          CONSTRAINT CK_Candle_Ohlc CHECK (
            OpenI > 0 AND HighI > 0 AND LowI > 0 AND CloseI > 0 AND VolumeI >= 0
            AND HighI >= LowI AND HighI >= OpenI AND HighI >= CloseI
            AND LowI <= OpenI AND LowI <= CloseI)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS Strategy (
          StrategyId     INTEGER PRIMARY KEY AUTOINCREMENT,
          Name           TEXT    NOT NULL UNIQUE,
          Description    TEXT    NULL,
          StrategyType   TEXT    NOT NULL,
          ParametersJson TEXT    NOT NULL CHECK (json_valid(ParametersJson)),
          RiskJson       TEXT    NOT NULL CHECK (json_valid(RiskJson)),
          Version        INTEGER NOT NULL DEFAULT 1,
          CreatedAtUtc   TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%S','now')),
          UpdatedAtUtc   TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%S','now'))
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS BacktestRun (
          RunId       INTEGER PRIMARY KEY AUTOINCREMENT,
          DatasetId   INTEGER  NOT NULL REFERENCES MarketDataset(DatasetId),
          StrategyId  INTEGER  NULL REFERENCES Strategy(StrategyId) ON DELETE SET NULL,
          StrategySnapshotJson TEXT NOT NULL,
          AssumptionsJson      TEXT NOT NULL,
          RangeStartTs TEXT NOT NULL,
          RangeEndTs   TEXT NOT NULL,
          RunAtUtc     TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%S','now')),
          Status       TEXT NOT NULL CHECK (Status IN ('Completed','Failed')),
          ErrorMessage TEXT NULL,
          ExecutionMs  INTEGER NULL,
          InitialEquityI INTEGER NULL,
          FinalEquityI   INTEGER NULL,
          TotalTrades     INTEGER NULL,
          WinningTrades   INTEGER NULL,
          LosingTrades    INTEGER NULL,
          BreakEvenTrades INTEGER NULL,
          NetProfitI      INTEGER NULL,
          GrossProfitI    INTEGER NULL,
          GrossLossI      INTEGER NULL,
          WinRatePctI     INTEGER NULL,
          ProfitFactorI   INTEGER NULL,
          MaxDrawdownAmountI INTEGER NULL,
          MaxDrawdownPctI    INTEGER NULL,
          EquityCurveJson TEXT NULL,
          MetricsJson     TEXT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS Trade (
          TradeId  INTEGER PRIMARY KEY AUTOINCREMENT,
          RunId    INTEGER NOT NULL REFERENCES BacktestRun(RunId) ON DELETE CASCADE,
          TradeNo  INTEGER NOT NULL,
          Direction INTEGER NOT NULL CHECK (Direction IN (1,2)),
          EntryTs  TEXT    NOT NULL,
          EntryPriceI INTEGER NOT NULL,
          ExitTs   TEXT    NOT NULL,
          ExitPriceI  INTEGER NOT NULL,
          QuantityI   INTEGER NOT NULL,
          GrossPnLI   INTEGER NOT NULL,
          FeesI       INTEGER NOT NULL,
          NetPnLI     INTEGER NOT NULL,
          ExitReason TEXT NOT NULL CHECK (ExitReason IN ('Signal','StopLoss','TakeProfit','EndOfData')),
          CONSTRAINT UQ_Trade_Run_No UNIQUE (RunId, TradeNo)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS AlgorithmBenchmark (
          BenchmarkId INTEGER PRIMARY KEY AUTOINCREMENT,
          BatchId     TEXT NOT NULL,
          AlgorithmName TEXT NOT NULL,
          Variant     TEXT NOT NULL,
          InputSize   INTEGER NOT NULL,
          Parameter   TEXT NULL,
          Repetitions INTEGER NOT NULL,
          MedianMs    REAL NOT NULL,
          MinMs       REAL NOT NULL,
          MaxMs       REAL NOT NULL,
          OperationCount INTEGER NULL,
          OperationDefinition TEXT NULL,
          MachineInfo TEXT NULL,
          RunAtUtc    TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%S','now'))
        )
        """,
        "CREATE INDEX IF NOT EXISTS IX_BacktestRun_Dataset   ON BacktestRun(DatasetId)",
        "CREATE INDEX IF NOT EXISTS IX_BacktestRun_RunAt     ON BacktestRun(RunAtUtc DESC)",
        "CREATE INDEX IF NOT EXISTS IX_BacktestRun_Strategy  ON BacktestRun(StrategyId)",
        "CREATE INDEX IF NOT EXISTS IX_Benchmark_Batch       ON AlgorithmBenchmark(BatchId)",
        "CREATE INDEX IF NOT EXISTS IX_Trade_Run             ON Trade(RunId)",
    };

    /* ── column migrations for databases that predate the extension
       columns (created from the blueprint SQL or an earlier build).
       Nullable TEXT ADD COLUMN → instant and data-safe in SQLite. */
    public static readonly string[] Migrations =
    {
        "ALTER TABLE MarketDataset ADD COLUMN ValidationIssuesJson TEXT",
        "ALTER TABLE BacktestRun  ADD COLUMN EquityCurveJson TEXT",
        "ALTER TABLE BacktestRun  ADD COLUMN MetricsJson TEXT",
    };

    /// <summary>Creates every table/index (idempotent) then applies column
    /// migrations. Duplicate-column errors on upgraded DBs are expected
    /// and ignored; anything else rethrows. Throws TursoException on failure.</summary>
    public static async Task EnsureCreatedAsync(Turso.TursoClient client, CancellationToken ct = default)
    {
        var stmts = Ddl.Select(sql => new Turso.TursoStmt(sql)).ToList();
        var results = await client.ExecuteAsync(stmts, ct).ConfigureAwait(false);
        foreach (var r in results) r.EnsureOk();
        await MigrateAsync(client).ConfigureAwait(false);
    }

    private static async Task MigrateAsync(Turso.TursoClient client)
    {
        foreach (var sql in Migrations)
        {
            try
            {
                var r = await client.OneAsync(sql).ConfigureAwait(false);
                r.EnsureOk();
            }
            catch (Turso.TursoException ex)
                when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase)
                   || ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                // column already present → migration already applied, expected on every start
            }
        }
    }
}
