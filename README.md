# AlgoTrade — Intelligent Trading Strategy Backtesting System

Educational backtesting platform: **C# / ASP.NET Core (.NET 10)** backend with six hand-written
DSA structures, **Turso (libSQL)** persistence over HTTP, a **Binance** live poller with CSV
fallback, and a **zero-dependency HTML/CSS/JS** trading terminal served from the same process.

```
AlgoTrade/
├── AlgoTrade.sln
├── src/
│   ├── AlgoTrade.Core/      domain, six DSA structures, indicators, engine, CSV parser, benchmark
│   ├── AlgoTrade.Data/      Turso HTTP client (/v2/pipeline), scaling, schema, repositories
│   │   └── Sql/001_schema_turso.sql
│   └── AlgoTrade.Api/       REST host + static frontend + Binance poller   ← run this
│       ├── Endpoints/       system, datasets, strategies, runs, benchmark, live
│       ├── Infrastructure/  LiveFeed (poller + CSV cache), SeedStrategies
│       ├── data/live/       append-only CSV cache (BTCUSDT_1m.csv)
│       └── wwwroot/         index.html, css/, js/ (views + store), assets/
└── tests/AlgoTrade.Tests/   xUnit suite (40 tests): engine rules, DSA vs brute force, RSI by hand
```

---

## 1 · Run it

**Prereqs:** .NET 10 SDK.

```bash
# 1 · paste your Turso credentials (once)
#    src/AlgoTrade.Api/appsettings.json → "Turso": { "Url": "...", "Token": "..." }

# 2 · restore + run
dotnet run --project src/AlgoTrade.Api
#    → http://localhost:5090        (frontend)
#    → http://localhost:5090/api/health

# 3 · tests
dotnet test
```

On first start the API:

- creates the schema in the background (idempotent DDL, 5 retries with backoff),
- seeds three built-in strategies **only when the `Strategy` table is empty**:
  `SMA Golden Cross 10/30` (`sma`), `Donchian Breakout 20/10` (`donchian`),
  `RSI Reversion 14 (28/72)` (`rsi`).

If Turso is unreachable the API still boots: `/api/health` reports `db: "unavailable"` and the
frontend's **demo mode** (in-browser engine) keeps the UI fully explorable. Flip demo mode OFF in
⚙ Settings once the API is live — the UI auto-falls back to demo after 3 consecutive API failures.

> **Security note:** the Turso token in `appsettings.json` is a secret. Do not commit it.
> Rotate the token after the project demo (Turso dashboard → database → Tokens).

## 2 · Frontend screens

The terminal is plain ES modules — no build step, no npm. Routes:

| Route | Screen | Purpose |
|---|---|---|
| `#/dashboard` | Dashboard | KPIs, live ticker rail, recent runs |
| `#/import` | Data Import | CSV validation preview before persisting (D18) |
| `#/datasets` | Datasets | Chronological OHLCV storage + sparklines |
| `#/datasets/:id` | Dataset Details | Candle table, O(log n) range stats, price levels |
| `#/strategies` | Strategy Editor | Versioned `StrategyConfig` JSON (sma / donchian / rsi) |
| `#/backtest` | Run Backtest | Dataset + strategy + equity → engine (D7–D9) |
| `#/results` | Results | Run picker (points to History) |
| `#/results/:id` | Backtest Results | Metrics, equity curve, trade list (D14–D15) |
| `#/compare` | Compare | Merge sort ranking + heap top-K |
| `#/history` | History | Saved runs, reproducibility via snapshots (D23) |
| `#/benchmark` | DSA Benchmark | Measured naive-vs-optimized timings |

## 3 · Architecture decisions that matter

| Decision | Where | Why |
|---|---|---|
| No EF / no native SQLite | `AlgoTrade.Data` | Turso is accessed over **HTTP `/v2/pipeline`** (Hrana v2) — pure `HttpClient`, zero native dependencies, deploy anywhere |
| SQLite has no DECIMAL | `Scaling.cs` | LOCKED **D5**: domain stays `double` (JS-Number semantics), DB stores prices/volumes **×10⁶** and percents **×10⁴** as INTEGER — one auditable conversion point |
| Same process serves UI + API | `Program.cs` | `UseStaticFiles` + `/api/*` — same origin, **no CORS**, one deployable |
| Live feed resilience | `Infrastructure/LiveFeed.cs` | `BackgroundService` polls Binance klines (default 15 s, `Binance:PollSeconds`) → newest closed candle lands in a memory ring **and** an append-only CSV; on outage the CSV tail is served and the UI badge flips `LIVE → CACHED` |
| JSON contract is frozen | every DTO | The wire shapes mirror `wwwroot/js/api.js` **field-for-field** (`m.net`, `p.fast`, `risk.slPct`, `nextGreater.bars`, runLite rows …) — switching demo mode off just works |

## 4 · The six DSA structures (Blueprint §7)

All structures live in `AlgoTrade.Core/Dsa` and carry an `OpCounter`
(comparisons / moves / probes) so every Big-O claim on the Benchmark screen is **measured
evidence**, not theory:

| Structure | File | Production use | Benchmark vs |
|---|---|---|---|
| Fixed open-addressing hash table (`long→int`, FNV-mix, ≤0.75 load) | `FixedHashTable.cs` | timestamp→index lookups in `CandleSeries` | linear-scan lookup |
| Sliding window (circular buffer + running sum; monotonic deque min/max) | `SlidingWindow.cs` | SMA indicators O(1)/candle | recompute-window-per-step O(n·k) |
| Monotonic stack | `MonotonicStack.cs` | next-greater / next-smaller price levels | O(n²) nested scan |
| Segment tree (recursive, 4n arrays, max/min/sum) | `SegmentTree.cs` | Range Statistics screen, Donchian | per-query full rescan |
| Merge sort (stable) + quick sort (median-of-three, insertion <16) | `OptimizedSorts.cs` | Compare-screen run ranking | bubble sort |
| Bounded binary min-heap | `BinaryHeap.cs` | Top-K best runs without a full sort | sort-then-slice |

Benchmark keys (`GET /api/benchmark?algos=…`): `hashtable`, `sliding`, `stack`, `segment`,
`mergesort`, `quicksort`, `heap`.

Benchmark honesty rule: naive O(n²) baselines only run up to a ~3·10⁸-op budget; beyond that
they're measured at n=20 000 and **extrapolated** — the persisted `OperationDefinition` says so.
Every batch (GUID `BatchId`, 5 reps, median/min/max, `MachineInfo`) is written to
`AlgorithmBenchmark`. Default size is 10 000; values below 100 fall back to 10 000 and the
maximum accepted is 1 000 000.

## 5 · Backtest engine — LOCKED rules (D7–D23)

`AlgoTrade.Core/Engine/BacktestEngine.cs` mirrors the browser mini-engine line-for-line:

- **D7** signal computed from the *completed* candle *i* (indicators use data ≤ i only)
- **D8** fills at the **open of candle i+1** — never the same bar
- **D9** per-candle SL/TP via high/low, **stop-first** when both are touched; gap opens fill at the open
- **D10** no leverage — entry rejected when `qty × fill > cash`
- **D11** flat fee per fill, charged `2 × fee` on exit
- **D12** equity marked to the **close of every candle**
- **D13** open position force-closed at the last close (`EndOfData`)
- **D14** win = `netPnL > 0` (strict); break-even is its own bucket
- **D15** profit factor is `NULL` (rendered N/A) when there is no losing trade
- **D16** duplicate timestamps rejected, first kept · **D17** out-of-order rows accepted + sorted
- **D18** import validation is reported row-by-row (`ImportReport`) before persistence
- **D23** every run stores the full strategy snapshot + assumptions → history stays reproducible

The xUnit suite pins each of these with hand-computed oracles (`tests/AlgoTrade.Tests/EngineTests.cs`),
RSI by hand (`IndicatorTests.cs`), and every DSA structure against brute force (`DsaTests.cs`).

## 6 · REST contract (frozen in `js/config.js` / `js/api.js`)

```
GET    /api/health
GET    /api/datasets                                   POST /api/datasets/import
GET    /api/datasets/{id}                              POST /api/datasets/demo
GET    /api/datasets/{id}/candles?page=&size=          DELETE /api/datasets/{id}
GET    /api/datasets/{id}/rangestats?from=&to=         (segment tree, O(log n))
GET    /api/datasets/{id}/pricelevels?limit=           (monotonic stack, O(n))
GET    /api/strategies        POST /api/strategies     PUT /api/strategies/{id}
GET    /api/runs              GET /api/runs/{id}       DELETE /api/strategies/{id}
POST   /api/backtest          { datasetId, strategyId, initialEquity }
GET    /api/benchmark?algos=a,b,c&size=10000
GET    /api/live/ticker       GET /api/live/candles?symbol=&tf=&limit=
DELETE /api/runs/{id}
```

Import formats accepted by `POST /api/datasets/import` (auto-detected, no conversion needed):

| Format | Example source | Shape |
|---|---|---|
| Canonical OHLCV | any export / this repo's samples | header `timestamp,open,high,low,close,volume`, ISO timestamps |
| Raw Binance kline dump | `data.binance.vision` monthly/daily zips | headerless 12 cols, unix-ms `open_time` (`open_time,…` headered variants too) |
| Third-party Binance exports | Kaggle / CryptoDataDownload | header aliases `unix`/`date`/`time` → `timestamp`, plain `volume` column optional |

All rows run the same validation pipeline (D16 duplicates, D17 ordering, OHLC sanity); every
rejection is reported row-by-row in the `ImportReport`. Uploads are capped at 8 MB by the UI and
the detail/backtest path loads up to 20 000 candles per dataset (`CandleRepo.DetailCap`) — prefer
15 m/1 h/4 h files for long spans, or import 1 m data month by month.

Strategy wire format (exactly what the Strategy Editor shows as "StrategyConfig JSON"):

```json
{ "name": "…", "type": "sma", "desc": "…",
  "p": { "fast": 10, "slow": 30 },
  "risk": { "sizing": "percent", "percentOfEquity": 10, "fixedUnits": 1,
            "slPct": 4, "tpPct": null, "feePerTrade": 0 },
  "version": 3, "createdAt": "…", "updatedAt": "…" }
```

## 7 · Deployment

The app is a single long-running ASP.NET Core process that also hosts a `BackgroundService`
(Binance poller), so serverless platforms (Vercel/Netlify) cannot run it. Use any host that
runs .NET 10 or a container:

```bash
dotnet publish src/AlgoTrade.Api -c Release -o out
dotnet out/AlgoTrade.Api.dll
```

Configuration comes from `appsettings.json` or environment variables (double underscore =
ASP.NET config override, so no secrets are baked into the build):

| Variable | Example |
|---|---|
| `Turso__Url` | `libsql://algotrade-….turso.io` |
| `Turso__Token` | your JWT |
| `Binance__Symbol` | `BTCUSDT` (default) |
| `Binance__Interval` | `1m` (default) |
| `Binance__PollSeconds` | `15` (default, minimum 5) |
| `ASPNETCORE_URLS` | `http://0.0.0.0:8080` |

Notes:

- The repo contains **no Dockerfile or deployment manifest** — add one if your host needs it
  (the published `out/` folder is self-contained for a `mcr.microsoft.com/dotnet/aspnet:10.0` image).
- Some datacenter regions (notably US) receive HTTP 451 from Binance; LiveFeed then degrades to
  the CSV cache (`LIVE → CACHED` badge) instead of failing.
- The container/host filesystem is ephemeral: the `data/live` CSV cache resets on restart;
  durable state lives in Turso.
- Keep the repo private; **rotate the Turso token** after grading.

Financial disclaimer: historical simulation only — not investment advice.
