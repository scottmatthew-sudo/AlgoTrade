# AlgoTrade — Intelligent Trading Strategy Backtesting System

Educational backtesting platform: **C# / ASP.NET Core** backend with six hand-written DSA
structures, **Turso (libSQL)** persistence, **Binance** live polling with CSV fallback, and a
**zero-dependency HTML/CSS/JS** trading terminal (already delivered as Phase 9, embedded here
under `src/AlgoTrade.Api/wwwroot`).

```
AlgoTrade/
├── AlgoTrade.sln
├── src/
│   ├── AlgoTrade.Core/      domain, DSA, indicators, engine, CSV parser, benchmark (no deps)
│   ├── AlgoTrade.Data/      Turso HTTP client (/v2/pipeline), scaling, schema, repositories
│   └── AlgoTrade.Api/       REST host + static frontend + Binance poller  ← run this
├── tests/AlgoTrade.Tests/   xUnit oracle suite (engine rules, DSA vs brute force, RSI by hand)
└── docs/001_schema_turso.sql
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

The database schema self-creates on first start (idempotent DDL, retried 5×). If Turso is
unreachable the API still boots: `/api/health` reports `db: "unavailable"` and the frontend's
**demo mode** (in-browser engine) keeps the UI fully explorable. Flip demo mode OFF in the
⚙ Settings modal once the API is live — the UI auto-falls-back to demo if the API dies.

> **Security note:** the Turso token in `appsettings.json` is a secret. Do not commit it.
> Rotate the token after the project demo (Turso dashboard → database → Tokens).

## 2 · Architecture decisions that matter

| Decision | Where | Why |
|---|---|---|
| No EF / no native SQLite | `AlgoTrade.Data` | Turso is accessed over **HTTP `/v2/pipeline`** (Hrana v2) — pure `HttpClient`, zero native dependencies, deploy anywhere |
| SQLite has no DECIMAL | `Scaling.cs` | LOCKED **D5**: domain stays `double` (JS-Number semantics), DB stores prices/volumes **×10⁶** and percents **×10⁴** as INTEGER — one auditable conversion point |
| Same process serves UI + API | `Program.cs` | `UseStaticFiles` + `/api/*` — same origin, **no CORS**, one deployable |
| Live feed resilience | `Infrastructure/LiveFeed.cs` | `BackgroundService` polls Binance klines (15 s) → newest closed candle lands in a memory ring **and** an append-only CSV; on outage the CSV tail is served and the UI badge flips `LIVE → CACHED` |
| JSON contract is frozen | every DTO | The wire shapes mirror `wwwroot/js/api.js` **field-for-field** (`m.net`, `p.fast`, `risk.slPct`, `nextGreater.bars`, runLite rows …) — switching CONFIG.mock=false just works |

## 3 · The six DSA structures (Blueprint §7)

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

Benchmark honesty rule: naive O(n²) baselines only run up to a ~3·10⁸-op budget; beyond that
they're measured at n=20 000 and **extrapolated** — the persisted `OperationDefinition` says so.
Every batch (GUID `BatchId`, 5 reps, median/min/max, `MachineInfo`) is written to
`AlgorithmBenchmark`.

## 4 · Backtest engine — LOCKED rules (D7–D14)

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
- **D23** every run stores the full strategy snapshot + assumptions → history stays reproducible

The xUnit suite pins each of these with hand-computed oracles (`tests/AlgoTrade.Tests/EngineTests.cs`),
RSI by hand (`IndicatorTests.cs`), and every DSA structure against brute force (`DsaTests.cs`).

## 5 · REST contract (frozen in `js/config.js`)

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
```

Import formats accepted by `POST /api/datasets/import` (auto-detected, no conversion needed):

| Format | Example source | Shape |
|---|---|---|
| Canonical OHLCV | any export / this repo's samples | header `timestamp,open,high,low,close,volume`, ISO timestamps |
| Raw Binance kline dump | `data.binance.vision` monthly/daily zips | headerless 12 cols, unix-ms `open_time` (`open_time,…` headered variants too) |
| Third-party Binance exports | Kaggle / CryptoDataDownload | header aliases `unix`/`date`/`time` → `timestamp`, plain `volume` column optional |

All rows run the same validation pipeline (D16 duplicates, D17 ordering, OHLC sanity); every
rejection is reported row-by-row in the `ImportReport`. Uploads are capped at 8 MB by the UI and
the detail/backtest path loads up to 20 000 candles per dataset — prefer 15 m/1 h/4 h files for
long spans, or import 1 m data month by month.

Strategy wire format (exactly what the Strategy Editor shows as "StrategyConfig JSON"):

```json
{ "name": "…", "type": "sma", "desc": "…",
  "p": { "fast": 10, "slow": 30 },
  "risk": { "sizing": "percent", "percentOfEquity": 10, "fixedUnits": 1,
            "slPct": 4, "tpPct": null, "feePerTrade": 0 },
  "version": 3, "createdAt": "…", "updatedAt": "…" }
```

## 6 · Deployment (Render free tier — Docker)

Vercel/Netlify cannot host this stack: the backend is a long-running ASP.NET Core
process with a BackgroundService (Binance poller), which serverless platforms don't
run. Render's free web service runs the whole single-process app as-is
(`Dockerfile` + `render.yaml` at the repo root).

1. Push this folder to a **private** GitHub repo (web-UI drag & drop works)
2. render.com → New → Blueprint → pick the repo (`render.yaml` auto-detected)
3. First sync asks for two secrets — paste them:
   - `Turso__Url` → `libsql://algotrade-….turso.io`
   - `Turso__Token` → your JWT
   (double underscore = ASP.NET config override; no token baked into the image)
4. Deploy → `https://algotrade-<hash>.onrender.com` — `/api/health` should show `db=ok`

Notes:
- Region **Frankfurt**: Binance returns HTTP 451 to US datacenter IPs; even then
  LiveFeed degrades to the CSV cache (LIVE → CACHED badge) instead of failing.
- Free tier sleeps after ~15 min idle → first request wakes it in 30–60 s;
  open the tab a few minutes before a demo.
- Container filesystem is ephemeral: the `data/live` CSV cache resets on restart;
  durable state lives in Turso.
- Alternative: `docker build -t algotrade . && docker run -p 8080:10000 …` anywhere.
- Keep the repo private; **rotate the Turso token** after grading.

Financial disclaimer: historical simulation only — not investment advice.
