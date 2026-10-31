import { api } from '../api.js';
import { CONFIG } from '../config.js';
import { el, icon, fmt, toast } from '../utils.js';
import { section, dataTable, badge, emptyState, mockNote, cx, kv } from '../ui.js';
import { mountCandleChart } from '../charts.js';


export async function renderImport(root) {
  root.append(
    el('div', { class: 'page-head' },
      el('div', {},
        el('h1', { class: 'page-title', text: 'Data Import' }),
        el('div', { class: 'page-sub', text: 'Bring historical OHLCV candles in from CSV. Every row is validated before it touches the database — malformed rows are reported, never silently dropped.' }),
      ),
    ),
    ...(CONFIG.mock ? [mockNote('Demo mode — the parser below runs entirely in your browser and accepts real CSV files. The C# ImportPipeline (Phase 2) implements the identical validation rules server-side.')] : []),
  );

  let fileRows = null;
  const input = el('input', { type: 'file', accept: '.csv,text/csv', style: 'display:none' });
  const dz = el('div', { class: 'dropzone', role: 'button', tabindex: '0', 'aria-label': 'Upload CSV file' },
    el('div', { class: 'dz-ico', html: icon('upload', 26) }),
    el('div', { class: 'dz-title', text: 'Drop your OHLCV CSV here' }),
    el('div', { class: 'dz-sub', html: 'or <b>browse files</b> — classic OHLCV CSV · raw Binance kline dumps · epoch timestamps' }),
    el('div', { class: 'dz-sub dim', html: 'Try the <b id="sample-link">sample dataset</b> (BTCUSDT hourly, 120 rows with planted errors)' }),
  );
  root.append(el('div', { class: 'card', style: 'padding:18px' }, dz, input));

  const result = el('div');
  root.append(result);

  const openPicker = () => input.click();
  dz.addEventListener('click', (e) => { if (!e.target.closest('#sample-link')) openPicker(); });
  dz.addEventListener('keydown', (e) => { if (e.key === 'Enter') openPicker(); });
  input.addEventListener('change', () => { if (input.files[0]) readFile(input.files[0]); });
  ['dragover', 'dragenter'].forEach(ev => dz.addEventListener(ev, (e) => { e.preventDefault(); dz.classList.add('drag'); }));
  ['dragleave', 'drop'].forEach(ev => dz.addEventListener(ev, (e) => { e.preventDefault(); dz.classList.remove('drag'); }));
  dz.addEventListener('drop', (e) => { const f = e.dataTransfer.files[0]; if (f) readFile(f); });

  /* sample CSV generator — includes planted validation errors */
  function sampleCsv() {
    const rows = ['timestamp,open,high,low,close,volume'];
    let p = 64200;
    for (let i = 120; i >= 1; i--) {
      const t = new Date(Date.now() - i * 3600000).toISOString().slice(0, 19);
      const o = p, c = +(p * (1 + (Math.sin(i / 7) * 0.004 + (i % 5) * 0.0004))).toFixed(2);
      const h = +Math.max(o, c) * 1.0012, l = +Math.min(o, c) * 0.9988;
      rows.push(`${t},${o.toFixed(2)},${h.toFixed(2)},${l.toFixed(2)},${c.toFixed(2)},${Math.round(300 + Math.abs(i * 13.7))}`);
      p = c;
    }
    rows.push(`${new Date(Date.now() - 40 * 3600000).toISOString().slice(0, 19)},bad,notanumber,64000,64100,100`);              // malformed
    rows.push(`${new Date(Date.now() - 118 * 3600000).toISOString().slice(0, 19)},64200,64400,64000,64300,500`);               // duplicate ts
    rows.push(`${new Date(Date.now() - 130 * 3600000).toISOString().slice(0, 19)},64200,63900,64100,64300,500`);               // high < low
    return rows.join('\n');
  }
  dz.querySelector('#sample-link')?.addEventListener('click', (e) => {
    e.stopPropagation();
    ingest('sample_btcusdt_1h.csv', sampleCsv());
  });

  function readFile(file) {
    if (file.size > 8 * 1024 * 1024) { toast('File too large', { type: 'err', msg: 'Demo parser handles files up to 8 MB.' }); return; }
    const r = new FileReader();
    r.onload = () => ingest(file.name, String(r.result));
    r.readAsText(file);
  }

  async function ingest(fileName, csvText) {
    result.innerHTML = '';
    const parsing = el('div', { class: 'card mt-16', style: 'padding:20px' },
      el('div', { class: 'row gap-12' }, el('span', { class: 'spinner' }), el('span', { class: 'text-2', text: `Parsing ${fileName} …` })));
    result.append(parsing);
    const res = await api.importCsv({ name: fileName.replace(/\.csv$/i, ''), csvText });
    parsing.remove();
    const maps = [['timestamp', 'Timestamp'], ['open', 'Open'], ['high', 'High'], ['low', 'Low'], ['close', 'Close'], ['volume', 'Volume']];
    result.append(el('div', { class: 'card mt-16' },
      el('div', { class: 'card-head' },
        el('div', { class: 'card-title' }, icon('check', 15), ' Column Mapping — ', el('span', { class: 'mono fs-12 dim', text: fileName })),
        badge(res.ok ? 'READY' : 'FAILED', res.ok ? 'teal' : 'red', { dot: true }),
      ),
      el('div', { class: 'card-body', style: 'display:flex;gap:10px;flex-wrap:wrap' },
        maps.map(([from, to]) => el('span', { class: `mapping-chip ${res.ok ? 'auto' : 'missing'}` }, from, el('span', { class: 'arrow', text: '→' }), el('span', { class: 'to', text: to }))),
      ),
    ));
    const isum = el('div', { class: 'import-summary mt-16' },
      isumCard(res.read ?? 0, 'rows read'),
      isumCard(res.accepted ?? 0, 'accepted', 'up'),
      isumCard(res.rejected ?? 0, 'rejected', (res.rejected ?? 0) ? 'down' : ''),
      isumCard(res.dups ?? 0, 'duplicate ts'),
      isumCard(res.outOfOrder ?? 0, 'out of order'),
    );
    result.append(isum);
    if (res.issues?.length) {
      result.append(el('div', { class: 'card mt-16' },
        el('div', { class: 'card-head' }, el('div', { class: 'card-title' }, 'Validation Report'),
          badge(`${res.issues.length} issue${res.issues.length > 1 ? 's' : ''}`, res.rejected ? 'amber' : 'gray')),
        el('div', { class: 'tbl-wrap' }, el('table', { class: 'tbl' },
          el('thead', {}, el('tr', {},
            el('th', { text: 'Row' }), el('th', { text: 'Column' }), el('th', { text: 'Message' }), el('th', { text: '', c: true }))),
          el('tbody', {}, res.issues.map(i => el('tr', {},
            el('td', { class: 'mono', text: i.row || '—' }),
            el('td', { class: 'mono', text: i.col }),
            el('td', { text: i.msg }),
            el('td', { class: 'c' }, badge(i.rejected ? 'REJECTED' : 'WARN', i.rejected ? 'red' : 'amber')),
          ))),
        )),
      ));
    }
    result.append(el('div', { class: 'row gap-12 mt-16', style: 'justify-content:flex-end' },
      el('button', { class: 'btn', text: 'Discard', onclick: () => { result.innerHTML = ''; } }),
      res.ok ? el('button', {
        class: 'btn btn-primary', html: icon('save', 15) + `<span>Save dataset (${res.accepted} candles)</span>`,
        onclick: async (e) => {
          e.currentTarget.disabled = true;
          await api.demoImport; // noop keep parity
          toast('Dataset saved', { msg: `${res.accepted} candles persisted to Turso (demo: in-browser store).` });
          setTimeout(() => { location.hash = res.datasetId ? `#/datasets/${res.datasetId}` : '#/datasets'; }, 600);
        },
      }) : null,
    ));
    toast(res.ok ? 'CSV validated' : 'Import failed', { type: res.ok ? 'ok' : 'err', msg: `${res.accepted ?? 0} accepted · ${res.rejected ?? 0} rejected` });
  }

  function isumCard(v, k, tone = '') {
    return el('div', { class: 'isum' },
      el('div', { class: `isum-v mono ${tone}`, text: fmt.int(v) }),
      el('div', { class: 'isum-k', text: k }));
  }
}


export async function renderDatasets(root) {
  root.append(
    el('div', { class: 'page-head' },
      el('div', {},
        el('h1', { class: 'page-title', text: 'Datasets' }),
        el('div', { class: 'page-sub', text: 'Chronological OHLCV collections — the fuel for every backtest. Click one to inspect candles, range statistics, and price-level analysis.' }),
      ),
      el('div', { class: 'page-actions' },
        el('button', { class: 'btn btn-primary', html: icon('upload', 15) + '<span>Import CSV</span>', onclick: () => location.hash = '#/import' })),
    ),
  );
  const wrap = el('div');
  root.append(wrap);
  const rows = await api.listDatasets();
  wrap.innerHTML = '';
  wrap.append(dataTable({
    columns: [
      { key: 'name', label: 'Dataset', render: r => el('span', { class: 'strong', text: r.name }) },
      { key: 'symbol', label: 'Symbol', mono: true },
      { key: 'timeframe', label: 'TF', c: true, mono: true },
      { key: 'rowsAccepted', label: 'Candles', r: true, mono: true, sortable: true },
      { key: 'source', label: 'Source' },
      { key: 'spark', label: '', c: true, render: r => { const s = el('div', { class: 'spark-cell' }); requestAnimationFrame(() => import('../charts.js').then(m => m.sparkline(s, r.spark))); return s; } },
    ],
    rows,
    onRowClick: (r) => { location.hash = `#/datasets/${r.id}`; },
  }));
}


export async function renderDatasetDetails(root, params) {
  const d = await api.getDataset(params.id);
  if (!d) { root.append(emptyState({ title: 'Dataset not found', sub: 'It may have been deleted.' })); return; }

  root.append(
    el('div', { class: 'page-head' },
      el('div', {},
        el('div', { class: 'row gap-8' },
          el('button', { class: 'icon-btn', 'aria-label': 'Back', html: icon('back', 17), onclick: () => location.hash = '#/datasets' }),
          el('h1', { class: 'page-title', text: d.name }),
          badge(d.source?.startsWith('live') ? 'LIVE FETCH' : 'CSV IMPORT', d.source?.startsWith('live') ? 'teal' : 'gray'),
        ),
        el('div', { class: 'page-sub', text: `Imported ${fmt.ago(d.importedAt)} · validation rejected ${d.rowsRejected} of ${d.rowsRead} rows` }),
      ),
      el('div', { class: 'page-actions' },
        el('button', { class: 'btn btn-primary', html: icon('play', 15) + '<span>Backtest this dataset</span>', onclick: () => location.hash = `#/backtest?dataset=${d.id}` })),
    ),
  );
  root.append(el('div', { class: 'grid grid-4' },
    metaCard('Candles', fmt.int(d.candles.length)),
    metaCard('Timeframe', d.timeframe?.toUpperCase() || '—'),
    metaCard('First candle', fmt.date(d.firstTs)),
    metaCard('Last candle', fmt.date(d.lastTs)),
  ));
  const legend = el('div', { class: 'chart-legend' });
  const box = el('div', { class: 'chart-box' });
  root.append(el('div', { class: 'card mt-16' }, legend, box));
  const upd = (c) => {
    legend.innerHTML = !c
      ? `<span class="lg">${d.symbol} · drag to pan · wheel to zoom</span>`
      : `<span class="lg mono fs-11">${c.ts}</span><span class="lg">O <b>${c.o}</b></span><span class="lg">H <b>${c.h}</b></span><span class="lg">L <b>${c.l}</b></span><span class="lg">C <b class="${c.c >= c.o ? 'up' : 'down'}">${c.c}</b></span><span class="lg">Vol <b>${c.v.toLocaleString()}</b></span>`;
  };
  upd(null);
  mountCandleChart(box, d.candles, { onHover: upd });


  root.append(section('Range Statistics', { tag: 'SEGMENT TREE · O(log n)', actions: null }));
  const fromSel = el('select', { class: 'select' }, d.candles.filter((_, i) => i % Math.max(1, Math.floor(d.candles.length / 60)) === 0).map(c => el('option', { value: c.ts, text: c.ts })));
  const toSel = el('select', { class: 'select' }, [...fromSel.options].map(o => el('option', { value: o.value, text: o.text, selected: o === fromSel.options[fromSel.options.length - 1] })));
  toSel.selectedIndex = toSel.options.length - 1;
  const statsOut = el('div', { class: 'grid grid-4 mt-16' });
  async function computeStats() {
    const s = await api.rangeStats(d.id, fromSel.value, toSel.value);
    statsOut.innerHTML = '';
    if (!s) { statsOut.append(emptyState({ title: 'Invalid range', sub: 'The end timestamp is before the start.' })); return; }
    statsOut.append(
      metaCard('Highest high', fmt.num(s.highestHigh), 'up'),
      metaCard('Lowest low', fmt.num(s.lowestLow), 'down'),
      metaCard('Total volume', fmt.compact(s.totalVolume)),
      metaCard('Average close', fmt.num(s.avgClose)),
    );
  }
  fromSel.addEventListener('change', computeStats);
  toSel.addEventListener('change', computeStats);
  root.append(el('div', { class: 'card' },
    el('div', { class: 'card-body' },
      el('div', { class: 'range-tool' },
        el('div', { class: 'field' }, el('label', { class: 'field-label', text: 'From' }), fromSel),
        el('div', { class: 'field' }, el('label', { class: 'field-label', text: 'To' }), toSel),
        el('button', { class: 'btn', text: 'Query range', onclick: computeStats }),
        el('span', { class: 'field-hint', text: 'Highest high / lowest low / volume / avg close answered in O(log n) per query.' }),
      ),
      statsOut,
    ),
  ));
  await computeStats();


  root.append(section('Price Level Analysis', { tag: 'MONOTONIC STACK · O(n)' }));
  root.append(el('div', { class: 'hint-banner', style: 'margin-bottom:12px' },
    el('span', { html: icon('alert', 16) }),
    el('span', { html: '<b>Hindsight analysis — not used for trading decisions.</b> Next-greater / next-smaller look at <i>future</i> candles; strategies only consume the causal (previous-*) variants. This screen exists to demonstrate the stack.' }),
  ));
  const levels = await api.priceLevels(d.id, 40);
  root.append(dataTable({
    columns: [
      { key: 'ts', label: 'Timestamp', mono: true },
      { key: 'close', label: 'Close', r: true, mono: true },
      { key: 'ng', label: 'Next greater', render: r => r.nextGreater ? el('span', {}, `#${r.nextGreater.i} `, el('span', { class: 'up mono', text: fmt.pct(r.nextGreater.pct, { sign: true }) }), el('span', { class: 'dim fs-11', text: ` (${r.nextGreater.bars} bars)` })) : el('span', { class: 'dim', text: 'none' }) },
      { key: 'ns', label: 'Next smaller', render: r => r.nextSmaller ? el('span', {}, `#${r.nextSmaller.i} `, el('span', { class: 'down mono', text: fmt.pct(r.nextSmaller.pct, { sign: true }) }), el('span', { class: 'dim fs-11', text: ` (${r.nextSmaller.bars} bars)` })) : el('span', { class: 'dim', text: 'none' }) },
    ],
    rows: levels.slice().reverse(),
  }));


  root.append(section('Candle Data'));
  const tblWrap = el('div');
  const pagerInfo = el('span');
  let page = 1;
  async function loadPage() {
    const res = await api.candlePage(d.id, page, 15);
    pagerInfo.textContent = `Page ${res.page} of ${res.pages} · ${res.total.toLocaleString()} rows — loaded with ORDER BY Ts (hash index: O(1) lookup by timestamp)`;
    tblWrap.innerHTML = '';
    tblWrap.append(dataTable({
      columns: [
        { key: 'ts', label: 'Timestamp', mono: true },
        { key: 'o', label: 'Open', r: true, mono: true },
        { key: 'h', label: 'High', r: true, mono: true },
        { key: 'l', label: 'Low', r: true, mono: true },
        { key: 'c', label: 'Close', r: true, mono: true, render: c => el('span', { class: `strong ${c.c >= c.o ? 'up' : 'down'}`, text: fmt.num(c.c) }) },
        { key: 'v', label: 'Volume', r: true, mono: true },
      ],
      rows: res.rows,
    }));
  }
  const pager = el('div', { class: 'pager' },
    pagerInfo,
    el('div', { class: 'pager-btns' },
      el('button', { class: 'pg-btn', text: '‹', onclick: () => { if (page > 1) { page--; loadPage(); } } }),
      el('button', { class: 'pg-btn', text: '›', onclick: () => { page++; loadPage().catch(() => { page--; }); } }),
    ),
  );
  root.append(el('div', { class: 'card' }, tblWrap, pager));
  await loadPage();

  function metaCard(label, value, tone = '') {
    return el('div', { class: 'stat', style: 'padding:13px 16px' },
      el('div', { class: 'stat-label', text: label }),
      el('div', { class: 'stat-value', style: 'font-size:19px;margin-top:6px' }, el('span', { class: tone, text: value })),
    );
  }
}
