import { api } from '../api.js';
import { CONFIG } from '../config.js';
import { el, icon, fmt, toast } from '../utils.js';
import { statCard, section, dataTable, statusBadge, badge, emptyState, mockNote, skeletonCards, skeletonRows } from '../ui.js';
import { mountCandleChart, sparkline } from '../charts.js';
import { openModal, confirmModal } from '../utils.js';

export async function renderDashboard(root) {
  root.append(
    el('div', { class: 'page-head' },
      el('div', {},
        el('h1', { class: 'page-title', text: 'Dashboard' }),
        el('div', { class: 'page-sub', text: 'Your backtesting desk at a glance — market data, strategies, and live engine status.' }),
      ),
      el('div', { class: 'page-actions' },
        el('button', { class: 'btn btn-primary', html: icon('upload', 15) + '<span>Import Data</span>', onclick: () => location.hash = '#/import' }),
        el('button', { class: 'btn', html: icon('play', 15) + '<span>Run Backtest</span>', onclick: () => location.hash = '#/backtest' }),
      ),
    ),
  );

  const kpi = el('div', { class: 'grid grid-4' }, skeletonCards(4));
  root.append(kpi);

  const [datasets, strategies, runs] = await Promise.all([
    api.listDatasets(), api.listStrategies(), api.listRuns(),
  ]);

  kpi.innerHTML = '';
  const completed = runs.filter(r => r.status === 'Completed');
  const avgWin = completed.length ? completed.reduce((s, r) => s + r.winRatePct, 0) / completed.length : 0;
  kpi.append(
    statCard({ label: 'Datasets', value: datasets.length, ico: 'db', sub: `${datasets.reduce((s, d) => s + d.rowsAccepted, 0).toLocaleString()} candles stored` }),
    statCard({ label: 'Strategies', value: strategies.length, ico: 'edit', tone: 'teal', sub: 'SMA · Donchian · RSI' }),
    statCard({ label: 'Backtest Runs', value: runs.length, ico: 'play', tone: 'amber', sub: `${completed.length} completed` }),
    statCard({ label: 'Avg Win Rate', value: avgWin, ico: 'chart', tone: '', sub: 'across completed runs', fmtFn: (v) => v.toFixed(1) + '%' }),
  );

  const main = el('div', { class: 'grid grid-main-side mt-16' });

  const legend = el('div', { class: 'chart-legend' });
  const chartBox = el('div', { class: 'chart-box' });

  /* empty database — guide instead of crashing (datasets[0].id threw here) */
  if (!datasets.length) {
    root.append(el('div', { class: 'card', style: 'grid-column:1/-1;padding:24px' },
      el('div', { class: 'empty' },
        el('div', { class: 'empty-ico', html: icon('db', 24) }),
        el('div', { class: 'empty-title', text: 'No market data yet' }),
        el('div', { class: 'empty-sub', text: 'Load the one-click sample dataset or import your own CSV — the chart, stats and backtests come alive right after.' }),
        el('div', { class: 'row gap-8', style: 'justify-content:center;margin-top:4px' },
          el('button', { class: 'btn btn-primary', html: icon('upload', 15) + '<span>Load sample dataset</span>', onclick: async () => {
            try {
              await api.demoImport();
              toast('Sample dataset saved to your database', { msg: 'DOGEUSDT · 5m · 1,200 candles' });
              location.hash = '#/import';
            } catch (e) { toast('Sample import failed', { type: 'err', msg: e.message }); }
          } }),
          el('button', { class: 'btn', text: 'Go to Data Import', onclick: () => location.hash = '#/import' }),
        ))));
    return;
  }

  const btc = datasets.find(d => d.symbol === 'BTCUSDT') || datasets[0];
  const full = await api.getDataset(btc.id);
  const chart = el('div', { class: 'card chart-panel' }, legend, chartBox);
  const upd = (c) => {
    if (!c) { legend.innerHTML = '<span class="lg dim">Hover the chart for OHLCV — drag to pan, wheel to zoom</span>'; return; }
    legend.innerHTML =
      `<span class="lg">${full.symbol} · ${full.timeframe}</span>` +
      `<span class="lg">O <b>${c.o}</b></span><span class="lg">H <b>${c.h}</b></span>` +
      `<span class="lg">L <b>${c.l}</b></span>` +
      `<span class="lg">C <b class="${c.c >= c.o ? 'up' : 'down'}">${c.c}</b></span>` +
      `<span class="lg">Vol <b>${c.v.toLocaleString()}</b></span>`;
  };
  upd(null);
  mountCandleChart(chartBox, full.candles, { onHover: upd });
  main.append(el('div', {}, chart));
  const rail = el('div', { class: 'col gap-14' });
  rail.style.gap = '14px';

  rail.append(el('div', { class: 'card' },
    el('div', { class: 'card-head' }, el('div', { class: 'card-title' }, 'Data Source', badge(DSB.mode, DSB.tone, { dot: true }))),
    el('div', { class: 'card-body' },
      el('div', { class: 'kv' },
        el('div', { class: 'kv-row' }, el('span', { class: 'kv-key', text: 'Provider' }), el('span', { class: 'kv-val mono', text: DSB.provider })),
        el('div', { class: 'kv-row' }, el('span', { class: 'kv-key', text: 'CSV fallback' }), el('span', { class: 'kv-val up', text: 'ARMED ✓' })),
        el('div', { class: 'kv-row' }, el('span', { class: 'kv-key', text: 'Poll interval' }), el('span', { class: 'kv-val mono', text: '15s' })),
        el('div', { class: 'kv-row' }, el('span', { class: 'kv-key', text: 'Last sync' }), el('span', { class: 'kv-val mono', id: 'dash-sync', text: fmt.date(new Date(), 'time') })),
      ),
    ),
  ));

  rail.append(el('div', { class: 'card' },
    el('div', { class: 'card-head' }, el('div', { class: 'card-title' }, 'DSA Engine', badge('6 modules', 'accent'))),
    el('div', { class: 'card-body', style: 'display:flex;flex-direction:column;gap:8px' },
      ['Hash Table — O(1) lookup', 'Sliding Window — O(1) update', 'Monotonic Stack — O(n)', 'Segment Tree — O(log n)', 'Merge / Quick Sort — O(m log m)', 'Binary Heap — O(log m)'].map(t =>
        el('div', { class: 'row', style: 'font-size:12px;color:var(--text-2)' },
          el('span', { class: 'ok', html: icon('check', 13) }), t),
      ),
    ),
  ));

  main.append(rail);
  root.append(main);

  root.append(section('Recent Backtest Runs', { actions: el('button', { class: 'btn btn-ghost btn-sm', text: 'View all →', onclick: () => location.hash = '#/history' }) }));
  const runsWrap = el('div');
  root.append(runsWrap);
  runsWrap.append(skeletonRows(4));
  setTimeout(() => {
    runsWrap.innerHTML = '';
    if (!runs.length) {
      runsWrap.append(el('div', { class: 'card' }, emptyState({ ico: 'play', title: 'No backtests yet', sub: 'Run your first backtest to see results here.' })));
      return;
    }
    runsWrap.append(dataTable({
      columns: [
        { key: 'runAt', label: 'Date', render: r => fmt.date(r.runAt), sortable: true, sort: 'str' },
        { key: 'datasetName', label: 'Dataset', render: r => el('span', { class: 'strong', text: r.datasetName }) },
        { key: 'strategyName', label: 'Strategy' },
        { key: 'totalTrades', label: 'Trades', r: true, mono: true, sortable: true },
        { key: 'netProfit', label: 'Net P/L', r: true, mono: true, sortable: true, render: r => el('span', { class: fmt.pnlClass(r.netProfit), text: fmt.money(r.netProfit, { sign: true }) }) },
        { key: 'winRatePct', label: 'Win rate', r: true, mono: true, sortable: true, render: r => fmt.pct(r.winRatePct) },
        { key: 'spark', label: 'Equity', c: true, render: r => { const s = el('div', { class: 'spark-cell' }); requestAnimationFrame(() => sparkline(s, r.spark, r.netProfit >= 0 ? '#22c55e' : '#f43f5e')); return s; } },
        { key: 'status', label: '', c: true, render: r => statusBadge(r.status) },
      ],
      rows: runs.slice(0, 6),
      onRowClick: (r) => { location.hash = `#/results/${r.id}`; },
      empty: emptyState(),
    }));
  }, 250);
}

/* Data Source card badge — computed at module load, never stale.
   (Was a hardcoded MOCK badge that lied in live mode.) */
const DSB = CONFIG.mock
  ? { mode: 'MOCK', tone: 'amber', provider: 'in-browser engine' }
  : { mode: 'LIVE', tone: 'teal', provider: 'ASP.NET Core API · Binance 15s' };

export async function renderHistory(root) {
  root.append(
    el('div', { class: 'page-head' },
      el('div', {},
        el('h1', { class: 'page-title', text: 'History' }),
        el('div', { class: 'page-sub', text: 'Everything persisted by the engine — datasets, strategy versions, and backtest runs.' }),
      ),
    ),
    ...(CONFIG.mock ? [mockNote()] : []),   /* demo banner only in demo mode */
  );

  const tabs = ['Datasets', 'Strategies', 'Runs'];
  const body = el('div', { class: 'mt-16' });
  let active = 'Datasets';

  const tabBar = el('div', { class: 'tabs' }, tabs.map(t =>
    el('button', {
      class: `tab ${t === active ? 'on' : ''}`, text: t,
      onclick: (e) => {
        active = t;
        tabBar.querySelectorAll('.tab').forEach(b => b.classList.remove('on'));
        e.currentTarget.classList.add('on');
        load();
      },
    }),
  ));
  root.append(tabBar, body);

  async function load() {
    body.innerHTML = '';
    body.append(skeletonRows(5));
    if (active === 'Datasets') await renderDatasetsTab(body);
    else if (active === 'Strategies') await renderStrategiesTab(body);
    else await renderRunsTab(body);
  }

  async function renderDatasetsTab(body) {
    const rows = await api.listDatasets();
    setTimeout(() => {
      body.innerHTML = '';
      body.append(dataTable({
        columns: [
          { key: 'name', label: 'Dataset', render: r => el('span', { class: 'strong', text: r.name }) },
          { key: 'timeframe', label: 'TF', c: true, mono: true },
          { key: 'rowsAccepted', label: 'Candles', r: true, mono: true, sortable: true },
          { key: 'source', label: 'Source', render: r => badge(r.source, r.source?.startsWith('live') ? 'teal' : 'gray') },
          { key: 'importedAt', label: 'Imported', render: r => fmt.date(r.importedAt) },
          { key: 'firstTs', label: 'Range', render: r => el('span', { class: 'mono fs-11 dim', text: `${fmt.date(r.firstTs)} → ${fmt.date(r.lastTs)}` }) },
          { key: 'x', label: '', r: true, render: r => el('button', {
              class: 'icon-btn', 'aria-label': 'Delete dataset', html: icon('trash', 15),
              onclick: async (e) => {
                e.stopPropagation();
                if (await confirmModal({ title: 'Delete dataset?', message: `"${r.name}" will be removed. Datasets used by runs are protected by the foreign key.`, confirmLabel: 'Delete' })) {
                  try { await api.deleteDataset(r.id); toast('Dataset deleted'); load(); }
                  catch (err) { toast('Cannot delete', { type: 'err', msg: err.message }); }
                }
              },
            }) },
        ],
        rows,
        onRowClick: (r) => { location.hash = `#/datasets/${r.id}`; },
      }));
    }, 200);
  }

  async function renderStrategiesTab(body) {
    const rows = await api.listStrategies();
    setTimeout(() => {
      body.innerHTML = '';
      body.append(dataTable({
        columns: [
          { key: 'name', label: 'Strategy', render: r => el('span', { class: 'strong', text: r.name }) },
          { key: 'type', label: 'Type', render: r => badge('teal', r.type?.toUpperCase()) },
          { key: 'p', label: 'Parameters', render: r => el('span', { class: 'mono fs-11', text: Object.entries(r.p || {}).map(([k, v]) => `${k}=${v}`).join(' · ') }) },
          { key: 'version', label: 'Ver', c: true, mono: true },
          { key: 'updatedAt', label: 'Updated', render: r => fmt.ago(r.updatedAt), sortable: true, sort: 'str' },
          { key: 'x', label: '', r: true, render: r => el('button', {
              class: 'icon-btn', 'aria-label': 'Delete strategy', html: icon('trash', 15),
              onclick: async (e) => {
                e.stopPropagation();
                if (await confirmModal({ title: 'Delete strategy?', message: `"${r.name}" will be removed. Saved runs keep their own snapshot (D23) — history stays reproducible.`, confirmLabel: 'Delete' })) {
                  await api.deleteStrategy(r.id); toast('Strategy deleted'); load();
                }
              },
            }) },
        ],
        rows,
        onRowClick: (r) => { location.hash = '#/strategies'; },
      }));
    }, 200);
  }

  async function renderRunsTab(body) {
    const rows = await api.listRuns();
    setTimeout(() => {
      body.innerHTML = '';
      body.append(dataTable({
        columns: [
          { key: 'id', label: '#', c: true, mono: true, sortable: true },
          { key: 'runAt', label: 'When', render: r => fmt.date(r.runAt) },
          { key: 'datasetName', label: 'Dataset' },
          { key: 'strategyName', label: 'Strategy' },
          { key: 'totalTrades', label: 'Trades', r: true, mono: true, sortable: true },
          { key: 'netProfit', label: 'Net P/L', r: true, mono: true, sortable: true, render: r => el('span', { class: fmt.pnlClass(r.netProfit), text: fmt.money(r.netProfit, { sign: true }) }) },
          { key: 'winRatePct', label: 'Win %', r: true, mono: true, render: r => fmt.pct(r.winRatePct, { digits: 1 }) },
          { key: 'status', label: 'Status', c: true, render: r => statusBadge(r.status) },
        ],
        rows,
        onRowClick: (r) => { location.hash = `#/results/${r.id}`; },
      }));
    }, 200);
  }

  await load();
}
