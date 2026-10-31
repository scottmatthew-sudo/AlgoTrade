import { api } from '../api.js';
import { el, icon, fmt, toast } from '../utils.js';
import { section, dataTable, badge, cx, emptyState } from '../ui.js';

const ALGOS = [
  { key: 'hashtable', name: 'Hash Table', desc: 'Timestamp → candle index. Separate chaining, power-of-two buckets, 0.75 load factor resize.', cx: ['O(1) avg insert/lookup', 'O(n) worst'] },
  { key: 'sliding', name: 'Sliding Window SMA', desc: 'k-period moving average via circular buffer + running sum — O(1) per candle.', cx: ['O(n) total', 'O(k) memory'] },
  { key: 'stack', name: 'Monotonic Stack', desc: 'Next-greater / next-smaller price levels; each index pushed and popped at most once.', cx: ['O(n) vs O(n²) naive'] },
  { key: 'segment', name: 'Segment Tree', desc: 'Range max / min / sum over candles; recursive array-backed tree of size 4n.', cx: ['build O(n)', 'query O(log n)'] },
  { key: 'mergesort', name: 'Merge Sort', desc: 'Stable production ranking of backtest runs — equal metrics keep run-date order.', cx: ['O(m log m) always', 'stable'] },
  { key: 'quicksort', name: 'Quick Sort', desc: 'Median-of-three pivot, insertion-sort fallback under 16 elements; benchmark-only vs merge.', cx: ['O(m log m) avg', 'O(m²) worst'] },
  { key: 'heap', name: 'Binary Heap · Top-K', desc: 'Bounded min-heap ranking the best K runs without a full sort.', cx: ['push/pop O(log m)', 'top-k O(m log k)'] },
];

const SIZES = [1000, 10000, 100000, 500000];

export async function renderBenchmark(root) {
  root.append(
    el('div', { class: 'page-head' },
      el('div', {},
        el('h1', { class: 'page-title', text: 'DSA Benchmark' }),
        el('div', { class: 'page-sub', text: 'Evidence for the Big-O report: measured execution time and operation counts — hand-built structures against naive baselines. Every result row is persisted with MachineInfo.' }),
      ),
    ),
  );

  const selected = new Set(ALGOS.map(a => a.key));
  const cards = el('div', { class: 'grid grid-3' }, ALGOS.map(a => {
    const card = el('div', { class: 'card algo-card card-hover on', role: 'checkbox', 'aria-checked': 'true', tabindex: '0' },
      el('div', { class: 'algo-head' },
        el('div', {},
          el('div', { class: 'algo-name', text: a.name }),
          el('div', { class: 'algo-desc', text: a.desc })),
        el('span', { class: 'algo-check' })),
      el('div', { class: 'algo-cx' }, a.cx.map(c => cx(c))),
    );
    const toggle = () => {
      if (selected.has(a.key)) { selected.delete(a.key); card.classList.remove('on'); card.setAttribute('aria-checked', 'false'); }
      else { selected.add(a.key); card.classList.add('on'); card.setAttribute('aria-checked', 'true'); }
    };
    card.addEventListener('click', toggle);
    card.addEventListener('keydown', (e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); toggle(); } });
    return card;
  }));
  root.append(section('1 · Choose algorithms', { actions: el('span', { class: 'fs-12 dim', id: 'algo-count' }) }));
  root.append(cards);
  const countEl = () => root.querySelector('#algo-count');
  const updateCount = () => { if (countEl()) countEl.textContent = `${selected.size} of ${ALGOS.length} selected`; };
  updateCount();

  root.append(section('2 · Input size (n = candles / m = results)'));
  const sizeSeg = el('div', { class: 'seg' }, SIZES.map((s, i) =>
    el('button', { class: i === 1 ? 'on' : '', text: s.toLocaleString(), 'data-size': s })));
  let size = 10000;
  sizeSeg.addEventListener('click', (e) => {
    const b = e.target.closest('button[data-size]');
    if (!b) return;
    size = +b.dataset.size;
    sizeSeg.querySelectorAll('button').forEach(x => x.classList.remove('on'));
    b.classList.add('on');
  });

  const runBtn = el('button', { class: 'btn btn-primary btn-lg', html: icon('play', 16) + '<span>Run benchmark</span>' });
  const prog = el('div', { class: 'prog', style: 'flex:1;min-width:160px' }, el('i', { style: 'width:0%' }));
  const status = el('span', { class: 'fs-12 dim' }, 'idle');
  root.append(el('div', { class: 'card', style: 'padding:16px;margin-top:2px' },
    el('div', { class: 'row wrap gap-16' }, el('div', { class: 'row gap-8' }, el('span', { class: 'fs-12 dim', text: 'n =' }), sizeSeg), prog, status, runBtn)));

  const resultsSlot = el('div');
  root.append(resultsSlot);
  let lastBatch = null;

  runBtn.addEventListener('click', async () => {
    if (!selected.size) { toast('Select at least one algorithm', { type: 'warn' }); return; }
    runBtn.disabled = true;
    runBtn.innerHTML = '<span class="spinner"></span><span>Benchmarking …</span>';
    resultsSlot.innerHTML = '';
    lastBatch = null;
    try {
      const batch = await api.benchmarkRun({
        algos: ALGOS.filter(a => selected.has(a.key)).map(a => a.key),
        size,
        onProgress: (done, total, key) => {
          prog.querySelector('i').style.width = `${Math.round((done / total) * 100)}%`;
          status.textContent = `${done}/${total} · ${key}`;
        },
      });
      lastBatch = batch;
      status.textContent = 'done ✓';
      renderBatch(batch);
      toast('Benchmark complete', { msg: `${batch.results.length} algorithms · n=${size.toLocaleString()} · saved as batch ${batch.batchId.slice(0, 8)}…` });
    } catch (e) {
      status.textContent = 'failed ✗';
      toast('Benchmark failed', { type: 'err', msg: e.message });
    } finally {
      runBtn.disabled = false;
      runBtn.innerHTML = icon('play', 16) + '<span>Run benchmark</span>';
    }
  });

  function renderBatch(batch) {
    resultsSlot.append(section('3 · Results', {
      tag: 'OPERATION-COUNTED',
      actions: el('button', { class: 'btn btn-ghost btn-sm', html: icon('download', 14) + 'Export CSV', onclick: () => exportBenchCsv(batch) }),
    }));

    for (const r of batch.results) {
      const maxMs = Math.max(r.naive.ms, r.opt.ms);
      const bar = (variant, ms, cls) => el('div', { class: 'bench-row' },
        el('span', { class: 'b-label', text: variant }),
        el('div', { class: 'bench-track' }, el('i', { class: `bench-fill ${cls}`, style: 'width:0%', 'data-w': String(Math.max(2, (ms / maxMs) * 100)) })),
        el('span', { class: `b-val ${cls === 'opt' ? 'ok' : 'down'}`, text: fmt.duration(ms) }));

      resultsSlot.append(el('div', { class: 'card', style: 'margin-bottom:12px' },
        el('div', { class: 'card-body' },
          el('div', { class: 'row-b wrap' },
            el('div', {},
              el('span', { class: 'strong fs-13', text: r.label }),
              el('span', { class: 'dim fs-12', text: ` · n=${r.size.toLocaleString()} · counted: ${r.unit}` })),
            el('span', { class: 'speedup' }, icon('flash', 14), `${r.speedup.toFixed(1)}× faster`, cx(r.cx)),
          ),
          el('div', { class: 'bench-bars mt-16' }, bar('Naive baseline', r.naive.ms, 'naive'), bar('Optimized (hand-built)', r.opt.ms, 'opt')),
          el('div', { class: 'row wrap gap-16 mt-8 fs-11 dim mono' },
            el('span', {}, `naive ops ≈ ${fmt.int(r.naive.ops)}`),
            el('span', {}, `optimized ops ≈ ${fmt.int(r.opt.ops)}`),
            el('span', {}, `median of 5 reps · ${batch.machine.slice(0, 40)}…`)),
        ),
      ));
    }
    requestAnimationFrame(() => setTimeout(() => {
      resultsSlot.querySelectorAll('.bench-fill').forEach(f => { f.style.width = f.dataset.w + '%'; });
    }, 40));
  }

  function exportBenchCsv(batch) {
    const head = 'Algorithm,Variant,InputSize,MedianMs,OperationCount,OperationDefinition';
    const lines = batch.results.flatMap(r => [
      `${r.label},Naive,${r.size},${r.naive.ms.toFixed(3)},${r.naive.ops},${r.unit}`,
      `${r.label},Optimized,${r.size},${r.opt.ms.toFixed(3)},${r.opt.ops},${r.unit}`,
    ]);
    const blob = new Blob([[head, ...lines].join('\n')], { type: 'text/csv' });
    const a = el('a', { href: URL.createObjectURL(blob), download: `algotrade_benchmark_${batch.batchId.slice(0, 8)}.csv` });
    document.body.append(a); a.click(); a.remove();
    toast('Benchmark CSV exported', { type: 'info' });
  }

  root.append(section('Complexity Reference (theory — prove it above)'));
  root.append(dataTable({
    columns: [
      { key: 'op', label: 'Operation', render: r => el('span', { class: 'strong', text: r.op }) },
      { key: 'cx', label: 'Complexity', render: r => cx(r.cx) },
      { key: 'why', label: 'Reason / assumption' },
      { key: 'where', label: 'Used in', render: r => badge('accent', r.where) },
    ],
    rows: [
      { op: 'Array/list chronological traversal', cx: 'O(n)', why: 'Visit each candle once during simulation.', where: 'BacktestEngine' },
      { op: 'Hash table exact lookup', cx: 'O(1) avg', why: 'Suitable hash + load factor ≤ 0.75; O(n) worst case.', where: 'CandleSeries' },
      { op: 'Sliding window update', cx: 'O(1)', why: 'Running sum: subtract oldest, add newest.', where: 'SMA indicators' },
      { op: 'Monotonic stack next-greater', cx: 'O(n) total', why: 'Each index pushed/popped at most once.', where: 'Price Level Analysis' },
      { op: 'Segment tree query', cx: 'O(log n)', why: 'Recursive top-down over 4n array-backed tree.', where: 'Donchian · Range stats' },
      { op: 'Comparison sort', cx: 'O(m log m)', why: 'Merge sort — stable, always.', where: 'Compare screen' },
      { op: 'Binary heap push/pop', cx: 'O(log m)', why: 'Sift-up / sift-down on bounded heap.', where: 'Top-K ranking' },
    ],
  }));
}
