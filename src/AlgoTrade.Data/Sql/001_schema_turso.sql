-- RULES OF THIS TRANSLATION (locked):
--   • All money/prices/volumes: INTEGER scaled by 1e6 (suffix I).
--     C# domain stays decimal (LOCKED D5) — repo layer converts.
--   • Percent metrics (WinRatePct, ProfitFactor, MaxDrawdownPct):
--     INTEGER scaled by 1e4.
--   • Timestamps: TEXT ISO-8601 "YYYY-MM-DDTHH:MM:SS" (sortable).
--   • Timings (MedianMs/MinMs/MaxMs): REAL — benchmarks only, not money.

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
  -- v1.1 extension: full import report kept for the dataset detail screen (D18)
  ValidationIssuesJson TEXT NULL
);

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
);

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
);

CREATE TABLE IF NOT EXISTS BacktestRun (
  RunId       INTEGER PRIMARY KEY AUTOINCREMENT,
  DatasetId   INTEGER  NOT NULL REFERENCES MarketDataset(DatasetId),
  StrategyId  INTEGER  NULL REFERENCES Strategy(StrategyId) ON DELETE SET NULL,
  StrategySnapshotJson TEXT NOT NULL,   -- D23 reproducibility snapshot
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
  WinRatePctI     INTEGER NULL,        -- ×10^4
  ProfitFactorI   INTEGER NULL,        -- ×10^4, NULL = N/A
  MaxDrawdownAmountI INTEGER NULL,
  MaxDrawdownPctI    INTEGER NULL,     -- ×10^4

  -- v1.1 extensions: fidelity snapshots for the frozen web contract
  -- (scaled columns above remain the queryable source of truth)
  EquityCurveJson TEXT NULL,           -- D12 mark-to-market curve per candle
  MetricsJson     TEXT NULL            -- exact metrics incl. pf NULL (D15)
);

CREATE TABLE IF NOT EXISTS Trade (
  TradeId  INTEGER PRIMARY KEY AUTOINCREMENT,
  RunId    INTEGER NOT NULL REFERENCES BacktestRun(RunId) ON DELETE CASCADE,
  TradeNo  INTEGER NOT NULL,
  Direction INTEGER NOT NULL CHECK (Direction IN (1,2)),  -- 1 Long, 2 Short
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
);

CREATE TABLE IF NOT EXISTS AlgorithmBenchmark (
  BenchmarkId INTEGER PRIMARY KEY AUTOINCREMENT,
  BatchId     TEXT NOT NULL,
  AlgorithmName TEXT NOT NULL,
  Variant     TEXT NOT NULL,           -- 'Naive' | 'Optimized' | 'BuiltIn'
  InputSize   INTEGER NOT NULL,
  Parameter   TEXT NULL,               -- e.g. 'k=50', 'q=10000'
  Repetitions INTEGER NOT NULL,
  MedianMs    REAL NOT NULL,
  MinMs       REAL NOT NULL,
  MaxMs       REAL NOT NULL,
  OperationCount INTEGER NULL,
  OperationDefinition TEXT NULL,
  MachineInfo TEXT NULL,
  RunAtUtc    TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%S','now'))
);

CREATE INDEX IF NOT EXISTS IX_BacktestRun_Dataset   ON BacktestRun(DatasetId);
CREATE INDEX IF NOT EXISTS IX_BacktestRun_RunAt     ON BacktestRun(RunAtUtc DESC);
CREATE INDEX IF NOT EXISTS IX_BacktestRun_Strategy  ON BacktestRun(StrategyId);
CREATE INDEX IF NOT EXISTS IX_Benchmark_Batch       ON AlgorithmBenchmark(BatchId);
CREATE INDEX IF NOT EXISTS IX_Trade_Run             ON Trade(RunId);
