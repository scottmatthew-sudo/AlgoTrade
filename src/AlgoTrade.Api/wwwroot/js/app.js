/* ═══════════════════════════════════════════════════════════════
   AlgoTrade · app.js — hash router, topbar (ticker/clock/badges),
   settings modal, sidebar wiring. Entry point.
   ═══════════════════════════════════════════════════════════════ */

import { CONFIG } from './config.js';
import { api } from './api.js';
import { el, icon, fmt, toast, openModal, confirmModal } from './utils.js';
import { renderDashboard, renderHistory } from './views/overview.js';
import { renderImport, renderDatasets, renderDatasetDetails } from './views/data.js';
import { renderStrategies, renderBacktest } from './views/strategy.js';
import { renderResults, renderCompare } from './views/results.js';
import { renderBenchmark } from './views/benchmark.js';

/* ── route table ───────────────────────────────────────────── */
const ROUTES = [
  { path: '#/dashboard', title: 'Dashboard', crumb: 'Overview', hint: 'Historical simulation — not financial advice', render: renderDashboard },
  { path: '#/import', title: 'Data Import', crumb: 'Market Data', hint: 'Validate before you persist — D18', render: renderImport },
  { path: '#/datasets', title: 'Datasets', crumb: 'Market Data', hint: 'Chronological OHLCV storage', render: renderDatasets },
  { path: '#/datasets/:id', title: 'Dataset Details', crumb: 'Market Data', hint: 'O(log n) range answers via segment tree', render: renderDatasetDetails },
  { path: '#/strategies', title: 'Strategy Editor', crumb: 'Strategy', hint: 'Rules in, JSON out — versioned', render: renderStrategies },
  { path: '#/backtest', title: 'Run Backtest', crumb: 'Strategy', hint: 'No look-ahead. Ever. (D7–D9)', render: renderBacktest },
  { path: '#/results', title: 'Results', crumb: 'Analysis', hint: 'Pick a run from History', render: renderResultsHome },
  { path: '#/results/:id', title: 'Backtest Results', crumb: 'Analysis', hint: 'Metrics with defined formulas (D14–D15)', render: renderResults },
  { path: '#/compare', title: 'Compare', crumb: 'Analysis', hint: 'Merge sort O(m log m) · heap top-K O(m log k)', render: renderCompare },
  { path: '#/history', title: 'History', crumb: 'Analysis', hint: 'Reproducibility via snapshots (D23)', render: renderHistory },
  { path: '#/benchmark', title: 'DSA Benchmark', crumb: 'System', hint: 'Measured, not just theoretical', render: renderBenchmark },
];

async function renderResultsHome(root) {
  const runs = await api.listRuns();
  if (runs.length) location.hash = `#/results/${runs[0].id}`;
  else {
    root.append(el('div', { class: 'card', style: 'padding:10px' },
      el('div', { class: 'empty' },
        el('div', { class: 'empty-ico', html: icon('chart', 24) }),
        el('div', { class: 'empty-title', text: 'No runs yet' }),
        el('div', { class: 'empty-sub', text: 'Start a backtest and its results will open here.' }),
        el('button', { class: 'btn btn-primary', text: 'Run a backtest', onclick: () => location.hash = '#/backtest' }))));
  }
}

/* ── router ────────────────────────────────────────────────── */
const viewRoot = document.getElementById('view-root');
let currentPath = null;

function parseHash() {
  const raw = location.hash || '#/dashboard';
  const [pathPart, queryPart] = raw.split('?');
  const query = Object.fromEntries(new URLSearchParams(queryPart || ''));
  return { path: pathPart.replace(/\/+$/, '') || '#/dashboard', query };
}

async function route() {
  const { path, query } = parseHash();
  if (path === currentPath) { /* re-render anyway on explicit hashchange */ }
  currentPath = path;

  const match = matchRoute(path);
  const r = match.route;
  const params = match.params;

  // topbar
  document.getElementById('view-title').textContent = r.title;
  document.getElementById('view-crumb').textContent = r.crumb;
  document.getElementById('sb-hint').textContent = r.hint;

  // nav active state
  document.querySelectorAll('.nav-item').forEach(n => {
    const active = n.dataset.route === r.base;
    n.classList.toggle('active', active);
    if (active) n.setAttribute('aria-current', 'page'); else n.removeAttribute('aria-current');
  });

  // close mobile drawer
  document.getElementById('sidebar').classList.remove('open');

  // render
  viewRoot.innerHTML = '';
  const inner = el('div', { class: 'view-inner view-enter' });
  viewRoot.append(inner);
  viewRoot.scrollTop = 0;
  try {
    await r.render(inner, { ...params, ...query });
  } catch (e) {
    console.error('[AlgoTrade] view error:', e);
    inner.innerHTML = '';
    inner.append(el('div', { class: 'card', style: 'padding:32px' },
      el('div', { class: 'empty' },
        el('div', { class: 'empty-ico', html: icon('alert', 24) }),
        el('div', { class: 'empty-title', text: 'Something went wrong' }),
        el('div', { class: 'empty-sub', text: e.message }),
        el('button', { class: 'btn', text: 'Back to dashboard', onclick: () => location.hash = '#/dashboard' }))));
  }
}

function matchRoute(path) {
  for (const r of ROUTES) {
    const rp = r.path.split('/');
    const pp = path.split('/');
    if (r.path === path) return { route: { ...r, base: r.path }, params: {} };
    if (rp.length === pp.length && rp.every((seg, i) => seg.startsWith(':') || seg === pp[i])) {
      const params = {};
      rp.forEach((seg, i) => { if (seg.startsWith(':')) params[seg.slice(1)] = decodeURIComponent(pp[i]); });
      return { route: { ...r, base: r.path.split('/:')[0] }, params };
    }
  }
  return { route: { ...ROUTES[0], base: '#/dashboard' }, params: {} };
}

window.addEventListener('hashchange', route);

/* ── topbar: clock (UTC + Pakistan) ────────────────────────── */
/* Chart candles aur DB sab UTC me hain (exchange convention) —
   UTC wala clock unse aligned rehta hai. PKT wala local ke liye:
   Asia/Karachi = fixed UTC+5, no DST, browser se lete hain.   */
function tickClock() {
  const c = document.getElementById('clock');
  if (!c) return;
  const now = new Date();
  const utc = now.toISOString().slice(11, 19);
  const pkt = now.toLocaleTimeString('en-GB', { timeZone: 'Asia/Karachi', hour12: false });
  c.textContent = `${utc} UTC · ${pkt} PKT`;
}
setInterval(tickClock, 1000); tickClock();

/* ── topbar: live ticker ───────────────────────────────────── */
const tickerEl = document.getElementById('ticker');
let prevPrices = {};
async function tickTicker() {
  const data = await api.ticker();
  if (!tickerEl) return;
  tickerEl.innerHTML = '';
  for (const t of data) {
    const cls = t.delta >= 0 ? 'up' : 'down';
    const flash = prevPrices[t.symbol] !== undefined && prevPrices[t.symbol] !== t.price ? `animation:at-flash .8s ease` : '';
    prevPrices[t.symbol] = t.price;
    tickerEl.append(el('div', { class: 'tick-item' },
      el('span', { class: 'tick-sym', text: t.symbol.replace('USDT', '') }),
      el('span', { class: `tick-px mono ${cls}`, style: flash, text: fmt.price(t.price) }),
      el('span', { class: `tick-delta mono ${cls}`, text: (t.delta >= 0 ? '▲' : '▼') + Math.abs(t.delta).toFixed(2) + '%' }),
    ));
  }
  const sync = document.getElementById('sb-sync');
  if (sync) sync.textContent = fmt.date(new Date(), 'time');
  const dash = document.getElementById('dash-sync');
  if (dash) dash.textContent = fmt.date(new Date(), 'time');
}
tickTicker();
setInterval(tickTicker, 5000);

/* ── mode badge ────────────────────────────────────────────── */
function renderModeBadge() {
  const badgeEl = document.getElementById('mode-badge');
  const label = document.getElementById('mode-label');
  const src = document.getElementById('sb-source');
  if (CONFIG.mock) {
    badgeEl.className = 'mode-badge mock';
    label.textContent = 'MOCK DATA';
    if (src) { src.textContent = 'MOCK'; src.className = 'mono warn'; }
  } else {
    badgeEl.className = 'mode-badge live';
    label.textContent = 'LIVE API';
    if (src) { src.textContent = 'API'; src.className = 'mono ok'; }
  }
}
renderModeBadge();
window.addEventListener('at:mode-changed', renderModeBadge);

/* ── sidebar engine chip: reflect the REAL backend ─────────── */
/* index.html ships the static text "READY" (demo engine). In live
   mode this pings /health once per mode change and shows the C#
   engine + db state instead of a label that never updates. */
async function refreshEngineChip() {
  const chip = document.getElementById('sb-engine');
  if (!chip) return;
  if (CONFIG.mock) { chip.textContent = 'READY'; chip.title = 'In-browser demo engine'; return; }
  try {
    const res = await fetch(CONFIG.apiBase + '/health');
    if (!res.ok) throw new Error('HTTP ' + res.status);
    const h = await res.json();
    chip.textContent = h.db === 'connected' ? 'C# LIVE' : 'C# (db offline)';
    chip.title = `${h.engine || 'AlgoTrade API'} · db: ${h.db || 'unknown'}`;
  } catch {
    chip.textContent = 'API DOWN';
    chip.title = 'Health check failed — check the C# server console';
  }
}
refreshEngineChip();
window.addEventListener('at:mode-changed', refreshEngineChip);

/* ── settings modal ────────────────────────────────────────── */
document.getElementById('settings-btn').addEventListener('click', () => {
  const mockSwitch = el('input', { type: 'checkbox' });
  if (CONFIG.mock) mockSwitch.checked = true;
  const apiIn = el('input', { class: 'input mono', value: CONFIG.apiBase, placeholder: '/api or https://host/api' });
  const m = openModal({
    title: 'Connection Settings',
    body: el('div', { class: 'col', style: 'gap:14px' },
      el('div', { class: 'switch-row' },
        el('div', {},
          el('div', { class: 'fs-13 fw-6', text: 'Demo mode (in-browser engine)' }),
          el('div', { class: 'fs-11 dim', text: 'Off = call the ASP.NET Core REST API at the base URL below.' })),
        el('label', { class: 'switch' }, mockSwitch, el('span', { class: 'track' }))),
      el('div', { class: 'field' },
        el('label', { class: 'field-label', text: 'API base URL' }), apiIn,
        el('span', { class: 'field-hint', text: 'Endpoints: /health · /datasets · /strategies · /runs · /backtest · /benchmark' })),
      el('div', { class: 'hint-banner' },
        el('span', { html: icon('info', 16) }),
        el('span', { text: 'On startup the app auto-detects a running AlgoTrade API and uses it. Saving settings here stores your explicit choice and disables auto-detection.' })),
    ),
    footer: [
      el('button', { class: 'btn', text: 'Test connection', onclick: async () => {
        const prevMock = CONFIG.mock;                 // never persist a probe
        const base = apiIn.value.trim() || '/api';
        try {
          const res = await fetch(base + '/health');
          if (!res.ok) throw new Error('HTTP ' + res.status);
          const h = await res.json();
          if (!h || typeof h !== 'object' || (!('engine' in h) && !('mode' in h)))
            throw new Error('Reachable, but not an AlgoTrade API (no engine field).');
          toast('Connected to AlgoTrade API', { msg: `engine: ${h.engine || 'ok'}` });
        } catch (e) {
          toast('API test failed', { type: 'warn', msg: e.message + ' — staying in demo mode.' });
        } finally {
          CONFIG.mock = prevMock; renderModeBadge();  // restore original state
        }
      } }),
      el('button', { class: 'btn btn-primary', text: 'Save', onclick: () => {
        CONFIG.mock = mockSwitch.checked;
        CONFIG.apiBase = apiIn.value.trim() || '/api';
        CONFIG.mockSet = true;                      // explicit choice → boot auto-detect off
        renderModeBadge();
        m.close();
        toast('Settings saved', { msg: CONFIG.mock ? 'Demo mode active.' : `Live API: ${CONFIG.apiBase}` });
        refreshEngineChip();
        route();
      } }),
    ],
  });
});

/* ── sidebar mobile wiring ─────────────────────────────────── */
document.getElementById('sidebar-open').addEventListener('click', () => document.getElementById('sidebar').classList.add('open'));
document.getElementById('sidebar-close').addEventListener('click', () => document.getElementById('sidebar').classList.remove('open'));
document.getElementById('sidebar-scrim').addEventListener('click', () => document.getElementById('sidebar').classList.remove('open'));

/* ── boot ──────────────────────────────────────────────────── */
if (!location.hash) location.hash = '#/dashboard';
route();   // immediate first paint in the stored mode

/* live-API auto-detection: while the user has never saved an explicit
   source choice (⚙ Settings), probe /api/health once on boot — a real
   AlgoTrade C# API flips the app out of demo mode automatically.
   api.js keeps its own self-heal (3 live failures → back to demo). */
(async () => {
  if (CONFIG.mockSet) return;                    // respect the manual choice
  try {
    const ctl = new AbortController();
    const timer = setTimeout(() => ctl.abort(), 2500);
    const res = await fetch(CONFIG.apiBase + '/health', { signal: ctl.signal });
    clearTimeout(timer);
    if (!res.ok) throw new Error('HTTP ' + res.status);
    const h = await res.json();
    if (!h || typeof h !== 'object' || !('engine' in h)) throw new Error('not an AlgoTrade API');
    if (CONFIG.mock) {
      CONFIG.mock = false;                       // persists at.mock = '0'
      toast('Connected to AlgoTrade C# API', { msg: `engine: ${h.engine}` });
      window.dispatchEvent(new Event('at:mode-changed'));
      route();                                   // re-render everything from the live API
    }
  } catch { /* API not up yet — demo mode stays, zero noise */ }
})();
