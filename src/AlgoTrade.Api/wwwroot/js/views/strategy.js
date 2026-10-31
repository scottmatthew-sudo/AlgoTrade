import { api } from '../api.js';
import { el, icon, fmt, toast, setRangeFill } from '../utils.js';
import { section, field, badge, emptyState, cx, statusBadge } from '../ui.js';

/* parameter schemas per strategy type (must match C# StrategyFactory) */
const SCHEMAS = {
  sma: {
    label: 'SMA Crossover', icon: 'chart',
    desc: 'Long entry when the fast simple moving average crosses above the slow one; exit on the opposite cross.',
    params: [
      { key: 'fast', label: 'Fast period', min: 2, max: 50, step: 1, def: 10 },
      { key: 'slow', label: 'Slow period', min: 5, max: 200, step: 1, def: 30 },
    ],
    warmup: p => p.slow + 1,
  },
  donchian: {
    label: 'Donchian Breakout', icon: 'layers',
    desc: 'Enter when close breaks above the highest high of the last N candles; exit below the M-candle lowest low. Range queries answered by segment trees in O(log n).',
    params: [
      { key: 'entry', label: 'Entry lookback (bars)', min: 5, max: 100, step: 1, def: 20 },
      { key: 'exit', label: 'Exit lookback (bars)', min: 5, max: 50, step: 1, def: 10 },
    ],
    warmup: p => Math.max(p.entry, p.exit) + 1,
  },
  rsi: {
    label: 'RSI Mean Reversion', icon: 'timer',
    desc: 'Buy when RSI crosses up out of oversold; exit when it crosses down out of overbought. Wilder smoothing via sliding window.',
    params: [
      { key: 'period', label: 'RSI period', min: 2, max: 30, step: 1, def: 14 },
      { key: 'os', label: 'Oversold level', min: 10, max: 45, step: 1, def: 28 },
      { key: 'ob', label: 'Overbought level', min: 55, max: 90, step: 1, def: 72 },
    ],
    warmup: p => p.period * 3,
  },
};

const defaultRisk = () => ({ sizing: 'percent', percentOfEquity: 10, fixedUnits: 1, slPct: null, tpPct: null, feePerTrade: 0 });

export async function renderStrategies(root) {
  root.append(
    el('div', { class: 'page-head' },
      el('div', {},
        el('h1', { class: 'page-title', text: 'Strategy Editor' }),
        el('div', { class: 'page-sub', text: 'Define entry rules, exit rules, and risk settings. Configurations persist as JSON — every backtest run stores its own snapshot, so later edits never rewrite history.' }),
      ),
      el('div', { class: 'page-actions' },
        el('button', { class: 'btn btn-primary', html: icon('play', 15) + '<span>Run a backtest →</span>', onclick: () => location.hash = '#/backtest' })),
    ),
  );

  const grid = el('div', { class: 'editor-grid mt-16' });
  root.append(grid);

  const listCol = el('div', { class: 'card' });
  const editCol = el('div', { class: 'card' });
  const jsonCol = el('div', { class: 'card editor-json' });
  grid.append(listCol, editCol, jsonCol);

  let strategies = await api.listStrategies();
  let current = strategies[0] || null;

  function renderList() {
    listCol.innerHTML = '';
    listCol.append(
      el('div', { class: 'card-head' },
        el('div', { class: 'card-title', text: `Saved strategies (${strategies.length})` }),
        el('button', { class: 'btn btn-sm', text: '+ New', onclick: () => { current = null; renderEditor(); markList(); } }),
      ),
      el('div', { class: 'card-body flush', style: 'padding:8px' },
        strategies.length ? strategies.map(s => el('div', {
          class: `strat-item ${current && current.id === s.id ? 'on' : ''}`,
          onclick: () => { current = s; renderEditor(); markList(); },
        },
          el('span', { class: 'si-name', text: s.name }),
          el('span', { class: 'si-meta' }, badge(SCHEMAS[s.type]?.label || s.type, 'teal'), `v${s.version}`, fmt.ago(s.updatedAt)),
        )) : el('div', { style: 'padding:20px' }, emptyState({ ico: 'edit', title: 'No strategies yet', sub: 'Create your first rule set on the right.' })),
      ),
    );
  }
  function markList() { listCol.querySelectorAll('.strat-item').forEach((n, i) => n.classList.toggle('on', current && strategies[i] && strategies[i].id === current.id)); }

  let form = null;
  function renderEditor() {
    const isNew = !current;
    const strat = current ? JSON.parse(JSON.stringify(current)) : { name: '', desc: '', type: 'sma', p: { fast: 10, slow: 30 }, risk: defaultRisk() };
    editCol.innerHTML = '';

    const typeSeg = el('div', { class: 'seg' }, Object.entries(SCHEMAS).map(([k, v]) =>
      el('button', { class: k === strat.type ? 'on' : '', text: v.label, onclick: (e) => {
        strat.type = k;
        strat.p = Object.fromEntries(SCHEMAS[k].params.map(p => [p.key, p.def]));
        typeSeg.querySelectorAll('button').forEach(b => b.classList.remove('on'));
        e.currentTarget.classList.add('on');
        renderParams(); refreshJson();
      } })));

    const nameIn = el('input', { class: 'input', value: strat.name, placeholder: 'e.g. SMA Golden Cross 10/30' });
    const descIn = el('textarea', { class: 'textarea', rows: '2', placeholder: 'What does this strategy try to capture?' });
    descIn.value = strat.desc || '';

    const paramsWrap = el('div', { class: 'col', style: 'gap:12px' });
    function renderParams() {
      paramsWrap.innerHTML = '';
      for (const pd of SCHEMAS[strat.type].params) {
        const valEl = el('span', { class: 'param-val mono', text: strat.p[pd.key] });
        const range = el('input', { class: 'range', type: 'range', min: pd.min, max: pd.max, step: pd.step, value: strat.p[pd.key] });
        setRangeFill(range);
        range.addEventListener('input', () => { strat.p[pd.key] = +range.value; valEl.textContent = range.value; setRangeFill(range); refreshJson(); validate(); });
        paramsWrap.append(el('div', { class: 'param-card' },
          el('div', { class: 'param-head' }, el('span', { class: 'param-name', text: pd.label }), valEl),
          el('div', { class: 'param-range' }, range),
        ));
      }
    }
    renderParams();
    const risk = strat.risk;
    const sizingSeg = el('div', { class: 'seg' },
      el('button', { class: risk.sizing === 'percent' ? 'on' : '', text: '% of equity', onclick: (e) => { risk.sizing = 'percent'; syncSeg(sizingSeg, e); refreshJson(); } }),
      el('button', { class: risk.sizing === 'fixed' ? 'on' : '', text: 'Fixed units', onclick: (e) => { risk.sizing = 'fixed'; syncSeg(sizingSeg, e); refreshJson(); } }));
    const pctIn = el('input', { class: 'input mono', type: 'number', min: '0.1', max: '100', step: '0.5', value: risk.percentOfEquity });
    const fixIn = el('input', { class: 'input mono', type: 'number', min: '0.0001', step: '0.1', value: risk.fixedUnits });
    const slOn = switchEl(!!risk.slPct), tpOn = switchEl(!!risk.tpPct);
    const slIn = el('input', { class: 'input mono', type: 'number', min: '0.1', max: '90', step: '0.5', value: risk.slPct ?? 2, disabled: !risk.slPct });
    const tpIn = el('input', { class: 'input mono', type: 'number', min: '0.1', max: '500', step: '0.5', value: risk.tpPct ?? 5, disabled: !risk.tpPct });
    const feeIn = el('input', { class: 'input mono', type: 'number', min: '0', step: '0.01', value: risk.feePerTrade ?? 0 });
    slOn.input.addEventListener('change', () => { slIn.disabled = !slOn.input.checked; if (slOn.input.checked && !risk.slPct) risk.slPct = +slIn.value; refreshJson(); });
    tpOn.input.addEventListener('change', () => { tpIn.disabled = !tpOn.input.checked; if (tpOn.input.checked && !risk.tpPct) risk.tpPct = +tpIn.value; refreshJson(); });
    [pctIn, fixIn, slIn, tpIn, feeIn].forEach((inp, k) => inp.addEventListener('change', () => {
      if (k === 0) risk.percentOfEquity = +pctIn.value;
      if (k === 1) risk.fixedUnits = +fixIn.value;
      if (k === 2) risk.slPct = slOn.input.checked ? +slIn.value : null;
      if (k === 3) risk.tpPct = tpOn.input.checked ? +tpIn.value : null;
      if (k === 4) risk.feePerTrade = +feeIn.value;
      refreshJson();
    }));

    const validOut = el('div');
    editCol.append(
      el('div', { class: 'card-head' },
        el('div', { class: 'card-title' }, isNew ? 'New strategy' : `Editing · v${strat.version}`),
        current ? el('button', { class: 'btn btn-danger btn-sm', html: icon('trash', 13) + 'Delete', onclick: async () => {
          await api.deleteStrategy(current.id); toast('Strategy deleted'); strategies = await api.listStrategies(); current = strategies[0] || null; renderList(); renderEditor();
        } }) : null,
      ),
      el('div', { class: 'card-body col', style: 'gap:14px' },
        el('div', { class: 'grid grid-2', style: 'gap:12px' },
          field('Strategy name', nameIn, { required: true }),
          field('Description', descIn),
        ),
        field('Strategy type', typeSeg, { hint: SCHEMAS[strat.type].desc }),
        el('div', {}, el('div', { class: 'field-label', style: 'margin-bottom:8px' }, 'Parameters'), paramsWrap),
        el('div', {},
          el('div', { class: 'field-label', style: 'margin-bottom:8px' }, 'Risk management'),
          el('div', { class: 'col', style: 'gap:8px' },
            el('div', { class: 'switch-row' }, el('span', { class: 'fs-13', text: 'Position sizing' }), sizingSeg),
            el('div', { class: 'grid grid-2', style: 'gap:8px' },
              el('div', { class: 'switch-row' }, el('span', { class: 'fs-13' }, '% of equity'), pctIn),
              el('div', { class: 'switch-row' }, el('span', { class: 'fs-13' }, 'Fixed units'), fixIn),
            ),
            el('div', { class: 'grid grid-2', style: 'gap:8px' },
              el('div', { class: 'switch-row' }, el('span', { class: 'fs-13' }, 'Stop-loss %'), slIn, slOn.node),
              el('div', { class: 'switch-row' }, el('span', { class: 'fs-13' }, 'Take-profit %'), tpIn, tpOn.node),
            ),
            el('div', { class: 'switch-row' }, el('span', { class: 'fs-13' }, 'Fee per trade ($, flat — D11)'), feeIn),
          ),
        ),
        validOut,
        el('div', { class: 'row', style: 'justify-content:flex-end' },
          el('button', { class: 'btn btn-primary', html: icon('save', 15) + '<span>Save strategy</span>', onclick: async () => {
            strat.name = nameIn.value.trim(); strat.desc = descIn.value.trim();
            const errs = validate(true);
            if (errs.length) { toast('Fix validation errors first', { type: 'err', msg: errs[0] }); return; }
            const saved = await api.saveStrategy(strat);
            current = saved;
            strategies = await api.listStrategies();
            renderList(); renderEditor();
            toast('Strategy saved', { msg: `"${saved.name}" v${saved.version} persisted.` });
          } }),
        ),
      ),
    );

    function validate(showAll = false) {
      const errs = [];
      if (!nameIn.value.trim()) errs.push('Strategy name is required.');
      if (strat.type === 'sma' && strat.p.fast >= strat.p.slow) errs.push('Fast period must be smaller than slow period.');
      if (strat.type === 'donchian' && strat.p.entry <= strat.p.exit) errs.push('Entry lookback should exceed exit lookback.');
      if (strat.type === 'rsi' && strat.p.os >= strat.p.ob) errs.push('Oversold level must be below overbought level.');
      if (risk.sizing === 'percent' && (+pctIn.value <= 0 || +pctIn.value > 100)) errs.push('% of equity must be within (0, 100].');
      if (risk.sizing === 'fixed' && +fixIn.value <= 0) errs.push('Fixed units must be positive.');
      if (showAll || validOut.childNodes.length) {
        validOut.innerHTML = '';
        if (errs.length) validOut.append(...errs.map(m => el('div', { class: 'hint-banner', style: 'margin-bottom:6px;padding:8px 12px' }, el('span', { html: icon('alert', 14) }), el('span', { text: m }))));
      }
      return errs;
    }
    nameIn.addEventListener('input', () => { validate(); refreshJson(); });
    validate();

    function refreshJson() {
      strat.name = nameIn.value.trim() || undefined;
      strat.desc = descIn.value.trim() || undefined;
      jsonCol.innerHTML = '';
      jsonCol.append(
        el('div', { class: 'card-head' }, el('div', { class: 'card-title' }, 'StrategyConfig JSON'), badge('wire-format', 'gray')),
        el('div', { class: 'card-body' },
          el('div', { class: 'json-view', html: syntaxJson({
            name: strat.name || '—', description: strat.desc || '—',
            strategyType: strat.type === 'sma' ? 'SmaCrossover' : strat.type === 'donchian' ? 'DonchianBreakout' : 'RsiMeanReversion',
            parameters: strat.p,
            risk: {
              sizing: risk.sizing === 'percent' ? 'PercentOfEquity' : 'FixedUnits',
              percentOfEquity: risk.percentOfEquity, fixedUnits: risk.fixedUnits,
              stopLossPercent: risk.slPct, takeProfitPercent: risk.tpPct, feePerTrade: risk.feePerTrade,
            },
            warmupCandles: SCHEMAS[strat.type].warmup(strat.p),
          }) }),
          el('div', { class: 'mt-8 fs-11 dim', text: 'This exact JSON is stored per run (D23) — reproducibility guaranteed.' }),
        ),
      );
    }
    refreshJson();
  }

  renderList();
  renderEditor();
}

function switchEl(checked) {
  const input = el('input', { type: 'checkbox' });
  if (checked) input.checked = true;
  const node = el('label', { class: 'switch' }, input, el('span', { class: 'track' }));
  return { node, input };
}
function syncSeg(seg, e) { seg.querySelectorAll('button').forEach(b => b.classList.remove('on')); e.currentTarget.classList.add('on'); }
function syntaxJson(obj) {
  const json = JSON.stringify(obj, (k, v) => v === undefined ? null : v, 2);
  return json
    .replace(/&/g, '&amp;').replace(/</g, '&lt;')
    .replace(/"([^"]+)":/g, '<span class="jk">"$1"</span>:')
    .replace(/: "([^"]*)"/g, ': <span class="js">"$1"</span>')
    .replace(/: (-?\d+\.?\d*)/g, ': <span class="jn">$1</span>')
    .replace(/: null/g, ': <span class="jb">null</span>');
}

export async function renderBacktest(root, params = {}) {
  root.append(
    el('div', { class: 'page-head' },
      el('div', {},
        el('h1', { class: 'page-title', text: 'Run Backtest' }),
        el('div', { class: 'page-sub', text: 'Simulate a strategy over historical candles — strictly chronological, strictly no look-ahead.' }),
      ),
    ),
  );

  const [datasets, strategies] = await Promise.all([api.listDatasets(), api.listStrategies()]);

  /* empty database guard — a backtest needs candles AND a strategy.
     (Used to fall through with empty <select>s and crash on .name.) */
  if (!datasets.length || !strategies.length) {
    root.append(el('div', { class: 'card', style: 'padding:24px' },
      el('div', { class: 'empty' },
        el('div', { class: 'empty-ico', html: icon('db', 24) }),
        el('div', { class: 'empty-title', text: 'Not ready to run yet' }),
        el('div', { class: 'empty-sub', html:
          (!datasets.length ? '<b>No datasets</b> — a backtest needs historical candles.&nbsp; ' : '') +
          (!strategies.length ? '<b>No strategies</b> — create one in the Strategy Editor first.' : '') }),
        el('div', { class: 'row gap-8', style: 'justify-content:center;margin-top:4px' },
          ...(!datasets.length ? [
            el('button', { class: 'btn btn-primary', html: icon('upload', 15) + '<span>Load sample dataset</span>', onclick: async () => {
              try {
                await api.demoImport();
                toast('Sample dataset saved to your database', { msg: 'DOGEUSDT · 5m · 1,200 candles' });
                location.hash = '#/import';
              } catch (e) { toast('Sample import failed', { type: 'err', msg: e.message }); }
            } }),
            el('button', { class: 'btn', text: 'Go to Data Import', onclick: () => location.hash = '#/import' }),
          ] : []),
          ...(!strategies.length ? [
            el('button', { class: 'btn btn-primary', text: 'Open Strategy Editor', onclick: () => location.hash = '#/strategies' }),
          ] : []),
        ))));
    return;
  }

  const dsSel = el('select', { class: 'select' }, datasets.map(d => el('option', { value: d.id, text: `${d.name} — ${d.rowsAccepted.toLocaleString()} candles`, selected: String(d.id) === String(params.dataset) })));
  const stSel = el('select', { class: 'select' }, strategies.map(s => el('option', { value: s.id, text: `${s.name} (v${s.version})` })));
  const eqIn = el('input', { class: 'input mono', type: 'number', min: '100', step: '500', value: '10000' });
  const runBtn = el('button', { class: 'btn btn-primary btn-lg btn-block', html: icon('play', 16) + '<span>Start backtest</span>' });

  const selCard = el('div', { class: 'card' },
    el('div', { class: 'card-head' }, el('div', { class: 'card-title' }, 'Configuration')),
    el('div', { class: 'card-body col', style: 'gap:14px' },
      field('Dataset', dsSel, { required: true }),
      field('Strategy', stSel, { required: true }),
      field('Initial equity ($)', eqIn, { hint: 'Starting cash — position sizing uses this (D10, no leverage).' }),
      runBtn,
    ),
  );

  const assumptions = el('div', { class: 'card' },
    el('div', { class: 'card-head' }, el('div', { class: 'card-title' }, 'Execution assumptions'), cx('DETERMINISTIC', 'Same inputs → same trades, every time.')),
    el('div', { class: 'card-body' },
      el('div', { class: 'assumption-note', style: 'margin-bottom:10px' },
        el('span', { html: icon('info', 16) }),
        el('span', { html: '<b>No look-ahead:</b> a signal computed from candle <i>i</i>’s close can only fill at candle <i>i+1</i>’s open (D7–D8). Indicators use data up to <i>i</i> only.' }),
      ),
      el('div', { class: 'kv' },
        el('div', { class: 'kv-row' }, el('span', { class: 'kv-key', text: 'Fill model' }), el('span', { class: 'kv-val', text: 'Next candle open (D8)' })),
        el('div', { class: 'kv-row' }, el('span', { class: 'kv-key', text: 'Stop / target' }), el('span', { class: 'kv-val', text: 'Per-candle high/low, stop-first (D9)' })),
        el('div', { class: 'kv-row' }, el('span', { class: 'kv-key', text: 'Equity curve' }), el('span', { class: 'kv-val', text: 'Marked to close, every candle (D12)' })),
        el('div', { class: 'kv-row' }, el('span', { class: 'kv-key', text: 'End of data' }), el('span', { class: 'kv-val', text: 'Force-close at last close (D13)' })),
        el('div', { class: 'kv-row' }, el('span', { class: 'kv-key', text: 'Costs' }), el('span', { class: 'kv-val', text: 'Fee per fill; shown “before costs” when 0 (D11)' })),
      ),
    ),
  );

  const consoleCard = el('div', { class: 'card', style: 'grid-column:1/-1' },
    el('div', { class: 'card-head' },
      el('div', { class: 'card-title' }, 'Engine console'),
      el('span', { class: 'row gap-8' }, cx('O(n) TRAVERSAL'), el('span', { id: 'bt-status' })),
    ),
    el('div', { class: 'card-body' },
      el('div', { class: 'prog', style: 'margin-bottom:12px' }, el('i', { style: 'width:0%' })),
      el('div', { class: 'prog-phases', style: 'margin-bottom:12px' },
        progPhase('load', 'Load candles (ORDER BY Ts, hash-indexed lookup)'),
        progPhase('init', 'Build indicators — sliding window + segment trees'),
        progPhase('run', 'Simulate chronologically — evaluate, fill, track equity'),
        progPhase('metrics', 'Compute metrics — win rate, profit factor, drawdown'),
        progPhase('save', 'Persist run + trades (single transaction)'),
      ),
      el('div', { class: 'run-console', id: 'bt-console' },
        el('span', { class: 'ln dim', text: '// console idle — start a backtest to stream engine output' })),
    ),
  );

  const resultSlot = el('div', { style: 'grid-column:1/-1' });

  const grid = el('div', { class: 'grid mt-16' });
  grid.style.gridTemplateColumns = 'minmax(0,1fr) minmax(0,1fr)';
  grid.append(selCard, assumptions);
  root.append(grid, consoleCard, resultSlot);

  let running = false;
  runBtn.addEventListener('click', async () => {
    if (running) return;
    running = true; runBtn.disabled = true;
    runBtn.innerHTML = '<span class="spinner"></span><span>Running …</span>';
    const consoleEl = consoleCard.querySelector('#bt-console');
    const barEl = consoleCard.querySelector('.prog > i');
    const statusEl = consoleCard.querySelector('#bt-status');
    consoleEl.innerHTML = ''; resultSlot.innerHTML = '';
    const log = (msg, cls = '') => {
      consoleEl.append(el('span', { class: `ln ${cls}` },
        el('span', { class: 'ts', text: new Date().toISOString().slice(11, 19) }), msg));
      consoleEl.scrollTop = consoleEl.scrollHeight;
    };
    const phases = ['load', 'init', 'run', 'metrics', 'save'];
    try {
      const dataset = datasets.find(d => String(d.id) === String(dsSel.value));
      const strategy = strategies.find(s => String(s.id) === String(stSel.value));
      if (!dataset || !strategy) throw new Error('Pick a dataset and a strategy first — both lists must be non-empty.');
      log(`dataset="${dataset.name}" strategy="${strategy.name}" equity=$${eqIn.value}`, 'ok');
      statusEl.innerHTML = '<span class="spinner" style="width:12px;height:12px"></span>';

      const run = await api.runBacktest({
        datasetId: +dsSel.value, strategy,
        initialEquity: +eqIn.value || 10000,
        onPhase: ({ phase, msg }) => {
          const idx = phases.indexOf(phase);
          barEl.style.width = `${Math.round(((idx + 1) / phases.length) * 100)}%`;
          document.querySelectorAll('.prog-phase').forEach((n, i) => {
            n.classList.toggle('done', i < idx);
            n.classList.toggle('active', i === idx);
          });
          log(msg, phase === 'run' ? 'warn' : '');
        },
      });

      document.querySelectorAll('.prog-phase').forEach(n => { n.classList.remove('active'); n.classList.add('done'); });
      barEl.style.width = '100%';
      statusEl.innerHTML = '<span class="ok">✓ COMPLETED</span>';
      log(`run #${run.id} saved — ${run.m.total} trades, net ${fmt.money(run.m.net, { sign: true })}, ${run.executionMs} ms`, 'ok');
      log('engine done — reproducible via strategy snapshot (D23)', 'ok');
      toast('Backtest completed', { msg: `Run #${run.id} — ${run.m.total} trades · net ${fmt.money(run.m.net, { sign: true })}` });
      resultSlot.append(el('div', { class: 'card', style: 'border-color:var(--border-accent)' },
        el('div', { class: 'card-body row wrap gap-16' },
          el('div', {}, el('div', { class: 'stat-label', text: 'Net P/L' }), el('div', { class: `stat-value mono ${fmt.pnlClass(run.m.net)}`, style: 'font-size:22px', text: fmt.money(run.m.net, { sign: true }) })),
          el('div', {}, el('div', { class: 'stat-label', text: 'Win rate' }), el('div', { class: 'stat-value mono', style: 'font-size:22px', text: fmt.pct(run.m.winRate, { digits: 1 }) })),
          el('div', {}, el('div', { class: 'stat-label', text: 'Trades' }), el('div', { class: 'stat-value mono', style: 'font-size:22px', text: String(run.m.total) })),
          el('div', { class: 'right row gap-8' },
            el('button', { class: 'btn', text: 'Run another', onclick: () => { resultSlot.innerHTML = ''; barEl.style.width = '0%'; } }),
            el('button', { class: 'btn btn-primary', html: icon('eye', 15) + '<span>Open full results</span>', onclick: () => location.hash = `#/results/${run.id}` }),
          ),
        ),
      ));
    } catch (e) {
      log(`ERROR — ${e.message}`, 'err');
      statusEl.innerHTML = '<span class="down">✗ FAILED</span>';
      toast('Backtest failed', { type: 'err', msg: e.message });
    } finally {
      running = false; runBtn.disabled = false;
      runBtn.innerHTML = icon('play', 16) + '<span>Start backtest</span>';
    }
  });
}

function progPhase(id, label) {
  return el('div', { class: 'prog-phase', 'data-phase': id }, el('span', { class: 'pd' }), el('span', { text: label }));
}
