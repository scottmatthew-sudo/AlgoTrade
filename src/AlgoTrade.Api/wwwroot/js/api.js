import { CONFIG } from './config.js';
import { db, seed } from './store.js';
import { parseCsv, priceLevels, rangeStats, benchmarkFor, genCandles } from './mock-data.js';

seed(); // idempotent

const sleep = (ms) => new Promise(r => setTimeout(r, ms));
const latency = async () => sleep(90 + Math.random() * 180);

async function live(method, path, body) {
  const res = await fetch(CONFIG.apiBase + path, {
    method,
    headers: body ? { 'Content-Type': 'application/json' } : undefined,
    body: body ? JSON.stringify(body) : undefined,
  });
  if (!res.ok) throw new Error(`${method} ${path} → HTTP ${res.status}`);
  return res.status === 204 ? null : res.json();
}

/* every call: if MOCK → in-browser, else → fetch with mock fallback.
   Failure policy: cooldown on warn toasts (no spam) + auto-heal after
   3 consecutive live failures → flip back to demo mode and persist. */
let liveFailCount = 0;
let lastWarnAt = 0;

function shim(mockFn, liveFn) {
  return async (...args) => {
    if (CONFIG.mock) { await latency(); return mockFn(...args); }
    try {
      const res = await liveFn(...args);
      liveFailCount = 0;
      return res;
    } catch (e) {
      console.warn('[AlgoTrade] live API failed:', e.message);
      liveFailCount++;
      const now = Date.now();
      const { toast } = await import('./utils.js');
      if (liveFailCount >= 3) {
        liveFailCount = 0;
        CONFIG.mock = true;                       // self-heal + persist
        window.dispatchEvent(new CustomEvent('at:mode-changed', { detail: { mock: true } }));
        toast('API unreachable — switched to demo mode', { type: 'warn', msg: 'Start your C# API, then reconnect via ⚙ Settings.' });
      } else if (now - lastWarnAt > 45000) {      // max 1 warn / 45 s
        lastWarnAt = now;
        toast('API unreachable — showing demo data', { type: 'warn', msg: e.message });
      }
      await latency();
      return mockFn(...args);
    }
  };
}

/* flatten run → row shape used by tables (metrics live in r.m) */
function runLite({ equity, trades, m, ...meta }) {
  const step = Math.max(1, Math.floor((equity?.length || 1) / 40));
  return {
    ...meta,
    tradeCount: trades?.length ?? 0,
    netProfit: m?.net ?? 0,
    winRatePct: m?.winRate ?? 0,
    totalTrades: m?.total ?? 0,
    profitFactor: m?.pf ?? null,
    maxDrawdownPct: m?.maxDdPct ?? 0,
    finalEquity: m?.final ?? null,
    spark: equity ? equity.filter((_, i) => i % step === 0) : [],
  };
}

export const api = {

  health: shim(
    async () => ({ mode: 'mock', engine: 'READY', ts: new Date().toISOString() }),
    () => live('GET', '/health'),
  ),

  listDatasets: shim(
    async () => db.store.datasets.map(({ candles, issues, ...meta }) => ({ ...meta, issues: issues?.length ?? 0, spark: candles.filter((_, i) => i % Math.max(1, Math.floor(candles.length / 40)) === 0).map(c => c.c) })),
    () => live('GET', '/datasets'),
  ),

  getDataset: shim(
    async (id) => db.getDataset(id),
    (id) => live('GET', `/datasets/${id}`),
  ),

  importCsv: shim(
    async ({ name, csvText }) => {
      const { candles, issues, read, dups = 0, outOfOrder = 0 } = parseCsv(csvText);
      const rejected = issues.filter(i => i.rejected).length;
      if (!candles.length) return { ok: false, issues, read, accepted: 0, rejected, dups, outOfOrder };
      const tfGuess = guessTimeframe(candles);
      const d = db.addDataset({ name, symbol: name.split(/[\s·-]/)[0]?.toUpperCase() || 'CSV', timeframe: tfGuess, source: 'csv-import', candles, rowsRead: read, rowsRejected: rejected, issues });
      return { ok: true, datasetId: d.id, issues, read, accepted: candles.length, rejected, dups, outOfOrder };
    },
    ({ name, csvText }) => live('POST', '/datasets/import', { name, csvText }),
  ),

  demoImport: shim(
    async () => {
      const candles = genCandles({ symbol: 'DOGEUSDT', seed: 88, n: 1200, tfMinutes: 5, endPrice: 0.32, volPct: 0.008 });
      const d = db.addDataset({ name: 'DOGEUSDT · 5m (demo fetch)', symbol: 'DOGEUSDT', timeframe: '5m', source: 'live-api (Binance)', candles, rowsRead: 1203, rowsRejected: 3 });
      return { ok: true, datasetId: d.id, accepted: candles.length };
    },
    () => live('POST', '/datasets/demo'),
  ),

  deleteDataset: shim(
    async (id) => {
      const res = db.deleteDataset(id);
      if (res?.blocked) throw new Error('Dataset has backtest runs — delete its runs first (FK rule).');
      return res;
    },
    (id) => live('DELETE', `/datasets/${id}`),
  ),

  candlePage: shim(
    async (id, page = 1, size = 25) => {
      const d = db.getDataset(id);
      if (!d) throw new Error('Dataset not found');
      const total = d.candles.length;
      const start = (page - 1) * size;
      return { rows: d.candles.slice(start, start + size), total, page, pages: Math.ceil(total / size) };
    },
    (id, page, size) => live('GET', `/datasets/${id}/candles?page=${page}&size=${size}`),
  ),

  rangeStats: shim(
    async (id, from, to) => {
      const d = db.getDataset(id);
      return rangeStats(d.candles, from, to);
    },
    (id, from, to) => live('GET', `/datasets/${id}/rangestats?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`),
  ),

  priceLevels: shim(
    async (id, limit = 60) => {
      const d = db.getDataset(id);
      return priceLevels(d.candles, limit);
    },
    (id, limit) => live('GET', `/datasets/${id}/pricelevels?limit=${limit}`),
  ),

  listStrategies: shim(
    async () => db.store.strategies,
    () => live('GET', '/strategies'),
  ),

  saveStrategy: shim(
    async (s) => (s.id ? db.updateStrategy(s.id, s) : db.addStrategy(s)),
    (s) => (s.id ? live('PUT', `/strategies/${s.id}`, s) : live('POST', '/strategies', s)),
  ),

  deleteStrategy: shim(
    async (id) => db.deleteStrategy(id),
    (id) => live('DELETE', `/strategies/${id}`),
  ),

  listRuns: shim(
    async () => db.store.runs.map(runLite),
    () => live('GET', '/runs'),
  ),

  getRun: shim(
    async (id) => db.getRun(id),
    (id) => live('GET', `/runs/${id}`),
  ),

  deleteRun: shim(
    async (id) => db.deleteRun(id),
    (id) => live('DELETE', `/runs/${id}`),
  ),

  runBacktest: shim(
    async ({ datasetId, strategy, initialEquity = 10000, onPhase = () => {} }) => {
      onPhase({ phase: 'load', msg: `Loading ${db.getDataset(datasetId)?.candles.length ?? 0} candles in chronological order …` });
      await sleep(500);
      onPhase({ phase: 'init', msg: 'Initializing rolling indicators (sliding window) + segment trees …' });
      await sleep(450);
      onPhase({ phase: 'run', msg: 'Simulating — no look-ahead: signal on close, fill at next open …' });
      await sleep(700);
      const run = db.addRun({ datasetId, strategy, initialEquity });
      onPhase({ phase: 'metrics', msg: `Calculating metrics — ${run.m.total} completed trades …` });
      await sleep(350);
      onPhase({ phase: 'save', msg: 'Persisting run + trades (one transaction) …' });
      await sleep(300);
      return run;
    },
    async ({ datasetId, strategy, initialEquity, onPhase = () => {} }) => {
      const run = await live('POST', '/backtest', { datasetId, strategyId: strategy.id, initialEquity });
      onPhase({ phase: 'done', msg: 'Run completed.' });
      return run;
    },
  ),

  benchmarkRun: shim(
    async ({ algos, size, onProgress = () => {} }) => {
      const out = [];
      for (let i = 0; i < algos.length; i++) {
        await sleep(260 + Math.random() * 300);
        const r = benchmarkFor(algos[i], size);
        if (r) out.push({ algo: algos[i], ...r });
        onProgress(i + 1, algos.length, algos[i]);
      }
      return { batchId: crypto.randomUUID?.() || String(Date.now()), machine: navigator.userAgent.slice(0, 80), results: out };
    },
    ({ algos, size }) => live('GET', `/benchmark?algos=${algos.join(',')}&size=${size}`),
  ),

  ticker: shim(
    async () => {
      const syms = [
        { symbol: 'BTCUSDT', price: 64723.85 }, { symbol: 'ETHUSDT', price: 3151.42 },
        { symbol: 'SOLUSDT', price: 148.77 }, { symbol: 'BNBUSDT', price: 592.31 },
        { symbol: 'XRPUSDT', price: 0.5241 },
      ];
      return syms.map(s => {
        const drift = (Math.random() - 0.485) * 0.0012;
        const prev = s.price / (1 + drift);
        return { ...s, prev, delta: drift * 100 };
      });
    },
    () => live('GET', '/live/ticker'),
  ),
};

function guessTimeframe(candles) {
  if (candles.length < 3) return 'unknown';
  const gaps = [];
  for (let i = 1; i < Math.min(30, candles.length); i++) {
    const d = (Date.parse(candles[i].ts) - Date.parse(candles[i - 1].ts)) / 60000;
    if (d > 0) gaps.push(d);
  }
  if (!gaps.length) return 'unknown';
  const med = gaps.sort((a, b) => a - b)[Math.floor(gaps.length / 2)];
  return med >= 1440 ? '1d' : med >= 60 ? `${Math.round(med / 60)}h` : `${Math.round(med)}m`;
}
