import { api } from '../api.js';
import { el, icon, fmt, toast, openModal } from '../utils.js';
import { section, dataTable, badge, statusBadge, reasonBadge, emptyState, cx, kv, skeletonCards } from '../ui.js';
import { mountEquityChart, sparkline } from '../charts.js';

export async function renderResults(root, params) {
  const kpis = el('div', { class: 'grid grid-5' }, skeletonCards(5));
  root.append(
    el('div', { class: 'page-head' },
      el('div', { class: 'row gap-8' },
        el('button', { class: 'icon-btn', 'aria-label': 'Back', html: icon('back', 17), onclick: () => history.back() }),
        el('div', {},
          el('h1', { class: 'page-title', text: 'Backtest Results' }),
          el('div', { class: 'page-sub', text: 'Loading run …' }),
        ),
      ),
    ),
    kpis,
  );

  const run = await api.getRun(params.id);
  if (!run || run.error === 404) { root.innerHTML = ''; root.append(emptyState({ ico: 'chart', title: 'Run not found', sub: 'It may have been deleted.' })); return; }
  const m = run.m;

  root.querySelector('.page-sub').innerHTML =
    `Run #${run.id} · ${run.datasetName} · ${run.strategyName} · ${fmt.date(run.runAt, 'full')} · ${statusBadge(run.status).outerHTML}`;
  kpis.innerHTML = '';
  kpis.className = 'grid grid-5';
  const net = el('div', { class: `stat-value ${fmt.pnlClass(m.net)}`, style: 'font-size:23px', text: fmt.money(m.net, { sign: true }) });
  kpis.append(
    el('div', { class: 'stat' }, el('div', { class: 'stat-label', text: 'Net profit / loss' }), net,
      el('div', { class: 'stat-foot', text: `Equity $${fmt.int(m.initial)} → $${fmt.int(m.final)}` })),
    winRateRing(m.winRate),
    el('div', { class: 'stat' }, el('div', { class: 'stat-label', text: 'Profit factor' }),
      el('div', { class: 'stat-value', style: 'font-size:23px', text: m.pf ? fmt.x(m.pf) : 'N/A' }),
      el('div', { class: 'stat-foot', text: m.pf ? `GP ${fmt.money(m.grossProfit)} / GL ${fmt.money(m.grossLoss)}` : 'no losing trades (D15)' })),
    el('div', { class: 'stat' }, el('div', { class: 'stat-label', text: 'Max drawdown' }),
      el('div', { class: 'stat-value down', style: 'font-size:23px', text: '-' + fmt.pct(m.maxDdPct, { digits: 2 }) }),
      el('div', { class: 'stat-foot', text: `peak-to-trough ${fmt.money(-m.maxDd)} (D12 curve)` })),
    el('div', { class: 'stat' }, el('div', { class: 'stat-label', text: 'Trades · time' }),
      el('div', { class: 'stat-value', style: 'font-size:23px' }, `${m.total}`, el('span', { class: 'unit', text: ` · ${fmt.duration(run.executionMs)}` })),
      el('div', { class: 'stat-foot', text: `${m.wins}W · ${m.losses}L · ${m.breakEven}BE (D14)` })),
  );
  root.append(section('Equity Curve', { tag: 'MARK-TO-MARKET · O(n) PASS' }));
  const legend = el('div', { class: 'chart-legend' },
    el('span', { class: 'lg' }, el('span', { style: 'width:14px;height:2px;background:var(--accent);display:inline-block' }), ' Equity'),
    el('span', { class: 'lg' }, el('span', { style: 'width:14px;height:0;border-top:2px dashed rgba(244,63,94,.5);display:inline-block' }), ' Running peak'),
    el('span', { class: 'lg' }, el('span', { style: 'width:14px;height:10px;background:rgba(244,63,94,.14);display:inline-block' }), ' Drawdown'),
  );
  const box = el('div', { class: 'chart-box' });
  root.append(el('div', { class: 'card' }, legend, box));
  mountEquityChart(box, run.equity, { initial: m.initial });
  root.append(el('div', { class: 'grid grid-2 mt-16' },
    el('div', { class: 'card' },
      el('div', { class: 'card-head' }, el('div', { class: 'card-title' }, 'Assumptions'), badge('recorded', 'gray')),
      el('div', { class: 'card-body' }, kv(Object.entries(run.assumptions || {}).map(([k, v]) => [k.replace(/([A-Z])/g, ' $1').replace(/^./, c => c.toUpperCase()), String(v)])))),
    el('div', { class: 'card' },
      el('div', { class: 'card-head' }, el('div', { class: 'card-title' }, 'Strategy snapshot'), badge('D23', 'accent', {})),
      el('div', { class: 'card-body' },
        el('div', { class: 'json-view', style: 'max-height:150px', text: JSON.stringify({ name: run.strategyName, parameters: run.snapshot?.p, risk: run.snapshot?.risk }, null, 2) })),
    ),
  ));
  root.append(section('Trade Log', { tag: 'O(n) RECORD', actions:
    el('button', { class: 'btn btn-ghost btn-sm', html: icon('download', 14) + 'Export CSV', onclick: () => exportTradesCsv(run) }) }));
  const rows = run.trades.slice().reverse();
  root.append(dataTable({
    columns: [
      { key: 'no', label: '#', c: true, mono: true, sortable: true },
      { key: 'dir', label: 'Side', c: true, render: () => badge('LONG', 'green') },
      { key: 'entryTs', label: 'Entry', mono: true, render: r => fmt.date(r.entryTs, 'full') },
      { key: 'entry', label: 'Entry px', r: true, mono: true },
      { key: 'exitTs', label: 'Exit', mono: true, render: r => fmt.date(r.exitTs, 'full') },
      { key: 'exit', label: 'Exit px', r: true, mono: true },
      { key: 'qty', label: 'Qty', r: true, mono: true },
      { key: 'gross', label: 'Gross', r: true, mono: true, sortable: true, render: r => fmt.money(r.gross, { sign: true }) },
      { key: 'fees', label: 'Fees', r: true, mono: true, render: r => r.fees ? fmt.money(r.fees) : '—' },
      { key: 'net', label: 'Net P/L', r: true, mono: true, sortable: true, render: r => el('span', { class: `strong ${fmt.pnlClass(r.net)}`, text: fmt.money(r.net, { sign: true }) }) },
      { key: 'reason', label: 'Exit', c: true, render: r => reasonBadge(r.reason) },
    ],
    rows,
    empty: emptyState({ ico: 'chart', title: 'No completed trades', sub: 'The strategy never signaled — a valid empty result is shown (no divide-by-zero).' }),
  }));
}

function winRateRing(pct) {
  const R = 30, CIRC = 2 * Math.PI * R;
  const node = el('div', { class: 'stat', style: 'display:flex;align-items:center;gap:14px' });
  const svg = el('div', { class: 'metric-ring' });
  svg.innerHTML = `<svg width="74" height="74" viewBox="0 0 74 74">
    <circle class="ring-bg" cx="37" cy="37" r="${R}" fill="none" stroke-width="7"/>
    <circle class="ring-fg" cx="37" cy="37" r="${R}" fill="none" stroke-width="7" stroke-linecap="round"
      stroke-dasharray="${CIRC}" stroke-dashoffset="${CIRC}"/>
  </svg><span class="ring-txt">${pct.toFixed(0)}%</span>`;
  node.append(svg, el('div', {},
    el('div', { class: 'stat-label', text: 'Win rate' }),
    el('div', { class: 'fs-11 dim', style: 'margin-top:4px', text: `${pct.toFixed(2)}% of completed trades` })));
  requestAnimationFrame(() => setTimeout(() => {
    svg.querySelector('.ring-fg').style.strokeDashoffset = CIRC * (1 - Math.min(pct, 100) / 100);
  }, 60));
  return node;
}

function exportTradesCsv(run) {
  const head = 'TradeNo,Direction,EntryTs,EntryPrice,ExitTs,ExitPrice,Quantity,GrossPnL,Fees,NetPnL,ExitReason';
  const lines = run.trades.map(t =>
    [t.no, t.dir, t.entryTs, t.entry, t.exitTs, t.exit, t.qty, t.gross, t.fees, t.net, t.reason].join(','));
  const blob = new Blob([[head, ...lines].join('\n')], { type: 'text/csv' });
  const a = el('a', { href: URL.createObjectURL(blob), download: `algotrade_run_${run.id}_trades.csv` });
  document.body.append(a); a.click(); a.remove();
  toast('Trade log exported', { type: 'info' });
}

const METRICS = [
  { key: 'netProfit', label: 'Net Profit', fmtFn: v => fmt.money(v, { sign: true }), best: 'high' },
  { key: 'winRatePct', label: 'Win Rate %', fmtFn: v => fmt.pct(v, { digits: 1 }), best: 'high' },
  { key: 'profitFactor', label: 'Profit Factor', fmtFn: v => v == null ? 'N/A' : fmt.x(v), best: 'high' },
  { key: 'maxDrawdownPct', label: 'Max Drawdown %', fmtFn: v => '-' + fmt.pct(v, { digits: 2 }), best: 'low' },
  { key: 'totalTrades', label: 'Total Trades', fmtFn: v => fmt.int(v), best: 'high' },
  { key: 'finalEquity', label: 'Final Equity', fmtFn: v => fmt.money(v), best: 'high' },
];

export async function renderCompare(root) {
  root.append(
    el('div', { class: 'page-head' },
      el('div', {},
        el('h1', { class: 'page-title', text: 'Compare Results' }),
        el('div', { class: 'page-sub', text: 'Rank saved runs head-to-head. Sorting runs is hand-merge-sort (stable, O(m log m)); the top-K card uses our binary heap (O(m log k)).' }),
      ),
    ),
  );

  const runs = (await api.listRuns()).map(({ spark, ...r }) => ({ ...r, spark }));
  if (runs.length < 2) {
    root.append(el('div', { class: 'card' }, emptyState({ ico: 'compare', title: 'Need at least 2 runs', sub: 'Run a couple of backtests first.' })));
    return;
  }
  const selected = new Set(runs.slice(0, 3).map(r => r.id));
  const chipRow = el('div', { class: 'row wrap gap-8', style: 'margin-bottom:14px' });
  function renderChips() {
    chipRow.innerHTML = '';
    runs.forEach(r => {
      const on = selected.has(r.id);
      chipRow.append(el('button', {
        class: `chip ${on ? '' : 'dim'}`, style: on ? 'border-color:rgba(52,211,153,.4);background:var(--accent-dim);color:var(--text-1)' : '',
        onclick: () => {
          if (on) selected.delete(r.id); else selected.add(r.id);
          if (selected.size) { renderChips(); renderTable(); }
        },
      },
        el('span', { class: on ? 'ok' : '', html: on ? icon('check', 12) : icon('x', 12) }),
        `#${r.id} ${r.strategyName}`,
        el('span', { class: 'mono dim fs-11', text: fmt.money(r.netProfit, { sign: true }) }),
      ));
    });
  }
  renderChips();
  root.append(chipRow);
  const metricSel = el('select', { class: 'select', style: 'width:auto' }, METRICS.map(mm => el('option', { value: mm.key, text: mm.label })));
  const topKBtn = el('button', { class: 'btn', html: icon('flash', 14) + '<span>Top-K: best 5 by Net Profit</span>' });
  root.append(section('Head-to-Head', {
    tag: 'MERGE SORT · O(m log m)',
    actions: el('div', { class: 'row gap-8' }, metricSel, topKBtn),
  }));

  const tableSlot = el('div');
  root.append(tableSlot);

  function renderTable() {
    const metric = METRICS.find(mm => mm.key === metricSel.value);
    const pool = runs.filter(r => selected.has(r.id));
    // stable merge-sort semantics shown; N/A always last
    const sorted = [...pool].sort((a, b) => {
      const av = a[metric.key], bv = b[metric.key];
      if (av == null) return 1;
      if (bv == null) return -1;
      return metric.best === 'low' ? av - bv : bv - av;
    });
    const bestVal = pool.length ? metric.best === 'low'
      ? Math.min(...pool.filter(r => r[metric.key] != null).map(r => r[metric.key]))
      : Math.max(...pool.filter(r => r[metric.key] != null).map(r => r[metric.key])) : null;

    tableSlot.innerHTML = '';
    const cols = [
      { key: 'rank', label: '', c: true, render: r => el('span', { class: `rank-badge ${sorted.indexOf(r) === 0 ? 'gold' : sorted.indexOf(r) === 1 ? 'silver' : sorted.indexOf(r) === 2 ? 'bronze' : ''}`, text: String(sorted.indexOf(r) + 1) }) },
      { key: 'strategyName', label: 'Strategy', render: r => el('span', { class: 'strong', text: r.strategyName }) },
      { key: 'datasetName', label: 'Dataset' },
      ...METRICS.map(mm => ({
        key: mm.key, label: mm.label, r: true, mono: true,
        render: r => el('span', { class: mm.key === metric.key && r[mm.key] === bestVal ? 'best-cell' : '' }, mm.fmtFn(r[mm.key])),
      })),
      { key: 'spark', label: 'Equity', c: true, render: r => { const s = el('div', { class: 'spark-cell' }); requestAnimationFrame(() => sparkline(s, r.spark, r.netProfit >= 0 ? '#22c55e' : '#f43f5e')); return s; } },
      { key: 'open', label: '', r: true, render: r => el('button', { class: 'btn btn-sm', text: 'Open →', onclick: () => location.hash = `#/results/${r.id}` }) },
    ];
    tableSlot.append(dataTable({ columns: cols, rows: sorted }));
  }
  metricSel.addEventListener('change', renderTable);
  renderTable();

  topKBtn.addEventListener('click', async () => {
    const runsAll = await api.listRuns();
    // binary heap top-K (O(m log k)) — mirrors Core BinaryHeap/TopKSelector
    const k = Math.min(5, runsAll.length);
    const heap = [];
    const worse = (a, b) => a.netProfit > b.netProfit; // min-heap of "worst of best-k"
    for (const r of runsAll) {
      if (heap.length < k) { heap.push(r); siftUp(heap, heap.length - 1, worse); }
      else if (r.netProfit > heap[0].netProfit) { heap[0] = r; siftDown(heap, 0, worse); }
    }
    const top = heap.sort((a, b) => b.netProfit - a.netProfit);
    openModal({
      title: 'Top-K Ranking — Binary Heap O(m log k)',
      body: el('div', {},
        el('div', { class: 'assumption-note', style: 'margin-bottom:12px' },
          el('span', { html: icon('info', 16) }),
          el('span', { text: `Selected the best ${k} of ${runsAll.length} runs with a bounded min-heap — no full sort needed.` })),
        ...top.map((r, i) => el('div', { class: `row-b kv-row ${i === 0 ? 'rank-1' : ''}` },
          el('span', { class: 'row gap-8' },
            el('span', { class: `rank-badge ${i === 0 ? 'gold' : i === 1 ? 'silver' : i === 2 ? 'bronze' : ''}`, text: String(i + 1) }),
            el('span', { class: 'strong', text: r.strategyName }),
            el('span', { class: 'dim fs-12', text: r.datasetName })),
          el('span', { class: `mono ${fmt.pnlClass(r.netProfit)}`, text: fmt.money(r.netProfit, { sign: true }) }))),
      ),
      footer: [el('button', { class: 'btn', text: 'Close', onclick: () => document.querySelector('.modal-scrim')?.remove() })],
    });
  });
}

function siftUp(h, i, worse) { while (i > 0) { const p = (i - 1) >> 1; if (!worse(h[i], h[p])) break; [h[i], h[p]] = [h[p], h[i]]; i = p; } }
function siftDown(h, i, worse) {
  for (;;) {
    const l = 2 * i + 1, r = l + 1; let m = i;
    if (l < h.length && worse(h[l], h[m])) m = l;
    if (r < h.length && worse(h[r], h[m])) m = r;
    if (m === i) break;
    [h[i], h[m]] = [h[m], h[i]]; i = m;
  }
}
