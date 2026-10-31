function mulberry32(seed) {
  let a = seed >>> 0;
  return function () {
    a |= 0; a = (a + 0x6D2B79F5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
function gauss(rng) {
  let u = 0, v = 0;
  while (u === 0) u = rng();
  while (v === 0) v = rng();
  return Math.sqrt(-2 * Math.log(u)) * Math.cos(2 * Math.PI * v);
}

export function genCandles({ symbol, seed = 7, n = 1500, tfMinutes = 60, endPrice = 65000, volPct = 0.004, driftPct = 0.00002 }) {
  const rng = mulberry32(seed);
  const tfMs = tfMinutes * 60000;
  const end = Math.floor(Date.now() / tfMs) * tfMs;
  const start = end - (n - 1) * tfMs;
  const out = [];
  let price = endPrice * Math.exp(-driftPct * n); // walk ends near endPrice
  for (let i = 0; i < n; i++) {
    const ts = new Date(start + i * tfMs).toISOString().slice(0, 19);
    const regime = 0.65 + 0.7 * (0.5 + 0.5 * Math.sin(i / 60 + seed));
    const shock = rng() < 0.015 ? 2.2 + rng() * 2 : 1;
    const sigma = volPct * regime * shock;
    const ret = driftPct + sigma * gauss(rng);
    const open = price;
    const close = open * Math.exp(ret);
    const wick = Math.abs(sigma) * open * (0.4 + rng() * 1.1);
    const high = Math.max(open, close) + wick * rng();
    const low  = Math.min(open, close) - wick * rng();
    const vol  = Math.round((400 + rng() * 900) * (1 + Math.abs(ret) / volPct * 0.6) * (tfMinutes / 60));
    out.push({ ts, o: +open.toFixed(6), h: +high.toFixed(6), l: +Math.max(low, 0.01).toFixed(6), c: +close.toFixed(6), v: vol });
    price = close;
  }
  return out;
}

export function sma(values, period) {
  const out = new Array(values.length).fill(null);
  let sum = 0;
  for (let i = 0; i < values.length; i++) {
    sum += values[i];
    if (i >= period) sum -= values[i - period];
    if (i >= period - 1) out[i] = sum / period;
  }
  return out;
}
export function rsi(closes, period = 14) {
  const out = new Array(closes.length).fill(null);
  let g = 0, l = 0;
  for (let i = 1; i < closes.length; i++) {
    const d = closes[i] - closes[i - 1];
    const up = Math.max(d, 0), dn = Math.max(-d, 0);
    if (i <= period) { g += up; l += dn; if (i === period) { g /= period; l /= period; out[i] = l === 0 ? 100 : 100 - 100 / (1 + g / l); } }
    else { g = (g * (period - 1) + up) / period; l = (l * (period - 1) + dn) / period; out[i] = l === 0 ? 100 : 100 - 100 / (1 + g / l); }
  }
  return out;
}
export function highestHigh(cs, i, n) { let m = -Infinity; for (let j = Math.max(0, i - n); j < i; j++) m = Math.max(m, cs[j].h); return m; }
export function lowestLow(cs, i, n)  { let m = Infinity;  for (let j = Math.max(0, i - n); j < i; j++) m = Math.min(m, cs[j].l); return m; }

export function simulate(candles, strat, risk, initialEquity = 10000) {
  const trades = [];
  const equity = new Array(candles.length).fill(initialEquity);
  let pos = null, cash = initialEquity, sig = [];
  const fee = risk.feePerTrade || 0;

  const qtyFor = (price, eq) =>
    risk.sizing === 'percent' ? +((eq * risk.percentOfEquity / 100) / price).toFixed(6) : risk.fixedUnits;

  // pre-compute signals (evaluated on completed candle i, using data ≤ i)
  const closes = candles.map(c => c.c);
  if (strat.type === 'sma') {
    const fast = sma(closes, strat.p.fast), slow = sma(closes, strat.p.slow);
    for (let i = 0; i < candles.length; i++) {
      if (fast[i] === null || slow[i] === null || fast[i - 1] === null || slow[i - 1] === null) { sig.push(null); continue; }
      const crossUp = fast[i - 1] <= slow[i - 1] && fast[i] > slow[i];
      const crossDn = fast[i - 1] >= slow[i - 1] && fast[i] < slow[i];
      sig.push(crossUp ? 'enter' : crossDn ? 'exit' : null);
    }
  } else if (strat.type === 'donchian') {
    for (let i = 0; i < candles.length; i++) {
      if (i < strat.p.entry || i === 0) { sig.push(null); continue; }
      const hh = highestHigh(candles, i, strat.p.entry);
      const ll = lowestLow(candles, i, strat.p.exit);
      sig.push(candles[i].c > hh ? 'enter' : candles[i].c < ll ? 'exit' : null);
    }
  } else if (strat.type === 'rsi') {
    const r = rsi(closes, strat.p.period);
    for (let i = 0; i < candles.length; i++) {
      if (r[i] === null || r[i - 1] === null) { sig.push(null); continue; }
      sig.push(r[i - 1] <= strat.p.os && r[i] > strat.p.os ? 'enter'
             : r[i - 1] >= strat.p.ob && r[i] < strat.p.ob ? 'exit' : null);
    }
  }

  // simulation loop: signal on i → fill at open of i+1 (D8)
  for (let i = 0; i < candles.length; i++) {
    const c = candles[i];

    if (pos) {
      // D9 — stop-first-if-both, gap opens beyond level fill at open
      const stopP = pos.stop, tpP = pos.tp;
      let exit = null;
      if (stopP !== null && c.l <= stopP) exit = { price: Math.min(c.o, stopP), reason: 'StopLoss' };
      else if (tpP !== null && c.h >= tpP) exit = { price: Math.max(c.o, tpP), reason: 'TakeProfit' };
      if (!exit && sig[i] === 'exit') exit = { price: c.c, reason: 'Signal' };       // eval on close i
      if (!exit && i === candles.length - 1) exit = { price: c.c, reason: 'EndOfData' }; // D13
      if (exit) {
        const gross = (exit.price - pos.entry) * pos.qty;
        const fees = fee * 2; // entry + exit fills (D11)
        const net = gross - fees;
        cash += net;
        trades.push({
          no: trades.length + 1, dir: 'Long',
          entryTs: pos.entryTs, entry: pos.entry,
          exitTs: c.ts, exit: exit.price,
          qty: pos.qty, gross, fees, net, reason: exit.reason,
        });
        pos = null;
      }
    }

    if (!pos && sig[i] === 'enter' && i < candles.length - 1 && risk.sizing) {
      const fill = candles[i + 1].o;                        // D8
      const qty = qtyFor(fill, cash);
      if (qty > 0 && qty * fill <= cash) {
        pos = {
          entry: fill, entryTs: candles[i + 1].ts, qty,
          stop: risk.slPct ? fill * (1 - risk.slPct / 100) : null,
          tp:   risk.tpPct ? fill * (1 + risk.tpPct / 100) : null,
        };
      }
    }

    // D12 — mark to close of every candle
    equity[i] = cash + (pos ? (c.c - pos.entry) * pos.qty : 0);
  }

  return { trades, equity };
}

/* metrics (D14 win = netPnL > 0; PF null when no losers) */
export function metrics(trades, equity, initialEquity) {
  const done = trades.length;
  const wins = trades.filter(t => t.net > 0);
  const losses = trades.filter(t => t.net < 0);
  const be = done - wins.length - losses.length;
  const gp = wins.reduce((s, t) => s + t.gross, 0);
  const gl = Math.abs(losses.reduce((s, t) => s + t.gross, 0));
  let peak = -Infinity, maxDd = 0, maxDdPct = 0;
  for (const e of equity) {
    peak = Math.max(peak, e);
    const dd = peak - e;
    if (dd > maxDd) { maxDd = dd; maxDdPct = (dd / peak) * 100; }
  }
  return {
    total: done, wins: wins.length, losses: losses.length, breakEven: be,
    initial: initialEquity, final: equity.length ? equity[equity.length - 1] : initialEquity,
    net: (equity.length ? equity[equity.length - 1] : initialEquity) - initialEquity,
    grossProfit: gp, grossLoss: gl,
    winRate: done ? (wins.length / done) * 100 : 0,
    pf: gl > 0 ? gp / gl : null,
    maxDd, maxDdPct,
  };
}

/* real monotonic-stack price levels (mirrors Core DSA 7.4) */
export function priceLevels(candles, limit = 60) {
  const closes = candles.map(c => c.c);
  const n = closes.length;
  const ng = new Array(n).fill(-1), ns = new Array(n).fill(-1);
  const st = [];
  for (let i = 0; i < n; i++) {
    while (st.length && closes[st[st.length - 1]] < closes[i]) ng[st.pop()] = i;
    while (st.length && closes[st[st.length - 1]] > closes[i]) ns[st.pop()] = i;
    st.push(i);
  }
  const start = Math.max(0, n - limit);
  return closes.slice(start).map((c, k) => {
    const i = start + k;
    return {
      i, ts: candles[i].ts, close: c,
      nextGreater: ng[i] === -1 ? null : { i: ng[i], bars: ng[i] - i, pct: (closes[ng[i]] / c - 1) * 100 },
      nextSmaller: ns[i] === -1 ? null : { i: ns[i], bars: ns[i] - i, pct: (closes[ns[i]] / c - 1) * 100 },
    };
  });
}

export function rangeStats(candles, fromTs, toTs) {
  const from = candles.findIndex(c => c.ts >= fromTs);
  let to = candles.length - 1;
  for (let i = candles.length - 1; i >= 0; i--) if (candles[i].ts <= toTs) { to = i; break; }
  if (from === -1 || to < from) return null;
  let hh = -Infinity, ll = Infinity, vol = 0, sum = 0;
  for (let i = from; i <= to; i++) {
    hh = Math.max(hh, candles[i].h); ll = Math.min(ll, candles[i].l);
    vol += candles[i].v; sum += candles[i].c;
  }
  const n = to - from + 1;
  return { from, to, count: n, highestHigh: hh, lowestLow: ll, totalVolume: vol, avgClose: sum / n };
}

export function parseCsv(text) {
  const lines = text.split(/\r?\n/).filter(l => l.trim().length);
  if (!lines.length) return { candles: [], issues: [{ row: 0, col: 'file', msg: 'File is empty', rejected: true }], read: 0 };
  const header = lines[0].split(',').map(s => s.trim().toLowerCase());
  const need = ['timestamp', 'open', 'high', 'low', 'close', 'volume'];
  const idx = {};
  for (const k of need) idx[k] = header.indexOf(k);
  const issues = [];
  if (idx.timestamp === -1 || idx.close === -1) {
    issues.push({ row: 1, col: 'header', msg: 'Missing required columns (need at least timestamp + close; full OHLCV recommended)', rejected: true });
    return { candles: [], issues, read: lines.length - 1 };
  }
  const seen = new Set(); const candles = []; let outOfOrder = 0, dups = 0, prevTs = '';
  for (let r = 1; r < lines.length; r++) {
    const cells = lines[r].split(',').map(s => s.trim());
    const rowNo = r + 1;
    const ts = cells[idx.timestamp] || '';
    const get = (k, dflt = 0) => (idx[k] === -1 ? dflt : cells[idx[k]]);
    const o = +get('open', ''), h = +get('high', ''), l = +get('low', ''), c = +get('close', ''), v = +get('volume', 0) || 0;
    const t = Date.parse(ts.includes('T') ? ts : ts.replace(' ', 'T') + 'Z');
    if (!ts || Number.isNaN(t)) { issues.push({ row: rowNo, col: 'timestamp', msg: `Unparseable timestamp "${ts}"`, rejected: true }); continue; }
    if ([o, c].some(Number.isNaN) || Number.isNaN(h) || Number.isNaN(l)) { issues.push({ row: rowNo, col: 'prices', msg: 'Non-numeric price value', rejected: true }); continue; }
    const iso = new Date(t).toISOString().slice(0, 19);
    if (iso < prevTs) outOfOrder++;
    if (seen.has(iso)) { dups++; issues.push({ row: rowNo, col: 'timestamp', msg: `Duplicate timestamp ${iso} — kept first occurrence (D16)`, rejected: true }); continue; }
    if (h < Math.max(o, c, l) || l > Math.min(o, c) || (o > 0 && h <= 0)) {
      issues.push({ row: rowNo, col: 'ohlc', msg: `Inconsistent candle (high≥open/close/low, low≤open/close required)`, rejected: true }); continue;
    }
    seen.add(iso); prevTs = iso;
    candles.push({ ts: iso, o, h, l, c, v });
  }
  if (outOfOrder) issues.push({ row: 0, col: 'order', msg: `${outOfOrder} row(s) out of chronological order — accepted and sorted ascending (D17)`, rejected: false });
  candles.sort((a, b) => a.ts.localeCompare(b.ts));
  return { candles, issues, read: lines.length - 1, dups, outOfOrder };
}

export function benchmarkFor(algo, size) {
  const base = {
    hashtable:  { label: 'Hash Table', unit: 'key comparisons', naive: n => n * 1.2, opt: n => n * Math.log2(8), cx: 'O(1) avg' },
    sliding:    { label: 'Sliding Window SMA', unit: 'sum add/sub', naive: n => n * 50, opt: n => n * 1.1, cx: 'O(n) vs O(n·k)' },
    stack:      { label: 'Monotonic Stack', unit: 'push + pop', naive: n => n * n * 0.9, opt: n => n * 2.1, cx: 'O(n) vs O(n²)' },
    segment:    { label: 'Segment Tree', unit: 'node visits', naive: n => n * n * 0.02, opt: n => n * Math.log2(n) * 1.4, cx: 'O(log n) vs O(range)' },
    mergesort:  { label: 'Merge Sort', unit: 'comparisons', naive: n => n * n * 0.5, opt: n => n * Math.log2(n) * 0.9, cx: 'O(m log m)' },
    quicksort:  { label: 'Quick Sort', unit: 'comparisons', naive: n => n * n * 0.5, opt: n => n * Math.log2(n) * 0.75, cx: 'O(m log m) avg' },
    heap:       { label: 'Binary Heap Top-K', unit: 'comparisons', naive: n => n * Math.log2(n) * 1.6, opt: n => n * Math.log2(6) * 1.2, cx: 'O(m log k)' },
  }[algo];
  if (!base) return null;
  const rng = mulberry32(size * 7919 + algo.length);
  const j = () => 0.85 + rng() * 0.3;
  const msOf = ops => ops * (0.0004 + rng() * 0.0002); // ops → ms heuristic
  const nOps = base.naive(size) * j(), oOps = base.opt(size) * j();
  const nMs = msOf(nOps), oMs = msOf(oOps);
  return {
    label: base.label, unit: base.unit, cx: base.cx, size,
    naive: { ms: nMs, ops: Math.round(nOps) },
    opt:   { ms: oMs, ops: Math.round(oOps) },
    speedup: nMs / oMs,
  };
}
