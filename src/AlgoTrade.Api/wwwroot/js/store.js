import { genCandles, simulate, metrics } from './mock-data.js';

const now = Date.now();
const daysAgo = (d, h = 0) => new Date(now - d * 86400000 - h * 3600000).toISOString().slice(0, 19);

const store = {
  datasets: [],
  strategies: [],
  runs: [],
  benchmarks: [],
  seq: { dataset: 1, strategy: 1, run: 1 },
};

/* run factory (module scope — used by seed AND db.addRun) */
const mkRun = (dataset, strat, when, execMs, initialEquity = 10000) => {
  const t0 = performance.now();
  const { trades, equity } = simulate(dataset.candles, strat, strat.risk, initialEquity);
  const m = metrics(trades, equity, initialEquity);
  return {
    id: store.seq.run++, datasetId: dataset.id, strategyId: strat.id,
    datasetName: dataset.name, strategyName: strat.name,
    runAt: when, status: 'Completed', error: null,
    executionMs: execMs ?? Math.round(60 + (performance.now() - t0) * 8),
    equity, trades,
    assumptions: { initialEquity, fillModel: 'next open (D8)', costs: strat.risk.feePerTrade ? `$${strat.risk.feePerTrade}/fill` : 'before transaction costs (D11)', endOfData: 'force-close at last close (D13)' },
    snapshot: JSON.parse(JSON.stringify(strat)),
    m,
  };
};

function mkDataset(name, sym, tf, n, seed, endPrice, volPct, source, days) {
  const candles = genCandles({ symbol: sym, seed, n, tfMinutes: tf, endPrice, volPct });
  const d = {
    id: store.seq.dataset++, name, symbol: sym,
    timeframe: tf === 1440 ? '1d' : tf === 60 ? '1h' : tf === 15 ? '15m' : tf === 5 ? '5m' : tf + 'm',
    source, importedAt: daysAgo(days, 3),
    rowsRead: n + 4, rowsAccepted: n, rowsRejected: 4,
    firstTs: candles[0].ts, lastTs: candles[candles.length - 1].ts,
    candles,
  };
  store.datasets.push(d);
  return d;
}

export function seed() {
  if (store.datasets.length) return;
  const btc  = mkDataset('BTCUSDT · hourly',      'BTCUSDT', 60,   1800, 11, 64500, 0.0045, 'live-api (Binance)', 12);
  const eth  = mkDataset('ETHUSDT · 15-minute',   'ETHUSDT', 15,   2400, 23, 3150,  0.005,  'csv-import', 9);
  const sol  = mkDataset('SOLUSDT · 5-minute',    'SOLUSDT', 5,    2000, 37, 148,   0.006,  'live-api (Binance)', 6);
  const aapl = mkDataset('AAPL · daily (demo)',   'AAPL',    1440, 520,  51, 228,   0.011,  'csv-import', 15);
  void btc; void eth; void sol; void aapl;

  const s1 = {
    id: store.seq.strategy++, name: 'SMA Golden Cross 10/30', type: 'sma',
    desc: 'Classic trend following — long when fast SMA crosses above slow SMA.',
    p: { fast: 10, slow: 30 },
    risk: { sizing: 'percent', percentOfEquity: 10, fixedUnits: 1, slPct: 4, tpPct: null, feePerTrade: 0 },
    version: 3, createdAt: daysAgo(20), updatedAt: daysAgo(2),
  };
  const s2 = {
    id: store.seq.strategy++, name: 'Donchian Breakout 20/10', type: 'donchian',
    desc: 'Turtle-style breakout — enter on N-bar high, exit on M-bar low.',
    p: { entry: 20, exit: 10 },
    risk: { sizing: 'percent', percentOfEquity: 15, fixedUnits: 1, slPct: 3, tpPct: 9, feePerTrade: 0 },
    version: 1, createdAt: daysAgo(14), updatedAt: daysAgo(5),
  };
  const s3 = {
    id: store.seq.strategy++, name: 'RSI Reversion 14 (28/72)', type: 'rsi',
    desc: 'Mean reversion — buy oversold relief, exit into overbought.',
    p: { period: 14, os: 28, ob: 72 },
    risk: { sizing: 'fixed', percentOfEquity: 10, fixedUnits: 0.5, slPct: 2.5, tpPct: 5, feePerTrade: 0 },
    version: 2, createdAt: daysAgo(10), updatedAt: daysAgo(7),
  };
  store.strategies.push(s1, s2, s3);

  store.runs.push(
    mkRun(btc, s1, daysAgo(8, 2), 184),
    mkRun(eth, s1, daysAgo(6, 5), 201),
    mkRun(btc, s2, daysAgo(4, 1), 176),
    mkRun(sol, s2, daysAgo(3, 4), 158),
    mkRun(eth, s3, daysAgo(1, 6), 243),
  );
}

export const db = {
  store,
  addDataset({ name, symbol, timeframe, source, candles, rowsRead, rowsRejected, issues }) {
    const d = {
      id: store.seq.dataset++, name, symbol: symbol || name.split(' ')[0], timeframe,
      source, importedAt: new Date().toISOString().slice(0, 19),
      rowsRead: rowsRead ?? candles.length, rowsAccepted: candles.length,
      rowsRejected: rowsRejected ?? 0, firstTs: candles[0]?.ts ?? null,
      lastTs: candles[candles.length - 1]?.ts ?? null, candles, issues: issues || [],
    };
    store.datasets.unshift(d);
    return d;
  },
  addStrategy(s) {
    const strat = { ...s, id: store.seq.strategy++, version: 1, createdAt: new Date().toISOString().slice(0, 19), updatedAt: new Date().toISOString().slice(0, 19) };
    store.strategies.unshift(strat);
    return strat;
  },
  updateStrategy(id, patch) {
    const s = store.strategies.find(x => x.id === id);
    if (s) Object.assign(s, patch, { version: (s.version || 1) + 1, updatedAt: new Date().toISOString().slice(0, 19) });
    return s;
  },
  deleteStrategy(id) {
    const i = store.strategies.findIndex(x => x.id === id);
    return i >= 0 ? store.strategies.splice(i, 1)[0] : null;
  },
  addRun({ datasetId, strategy, riskOverride, initialEquity }) {
    const dataset = store.datasets.find(d => d.id === datasetId);
    const strat = { ...strategy, risk: riskOverride || strategy.risk };
    const run = mkRun(dataset, strat, new Date().toISOString().slice(0, 19), null);
    run.assumptions.initialEquity = initialEquity;
    // resimulate with custom equity if needed
    if (initialEquity !== 10000) {
      const { trades, equity } = simulate(dataset.candles, strat, strat.risk, initialEquity);
      run.trades = trades; run.equity = equity;
      run.m = metrics(trades, equity, initialEquity);
      run.m.initial = initialEquity;
    }
    store.runs.unshift(run);
    return run;
  },
  getDataset(id) { return store.datasets.find(d => d.id === +id); },
  getStrategy(id) { return store.strategies.find(s => s.id === +id); },
  getRun(id) { return store.runs.find(r => r.id === +id); },
  deleteRun(id) {
    const i = store.runs.findIndex(r => r.id === +id);
    return i >= 0 ? store.runs.splice(i, 1)[0] : null;
  },
  deleteDataset(id) {
    const i = store.datasets.findIndex(d => d.id === +id);
    if (i < 0) return null;
    const had = store.runs.some(r => r.datasetId === +id);
    if (had) return { blocked: true };
    return store.datasets.splice(i, 1)[0];
  },
};
