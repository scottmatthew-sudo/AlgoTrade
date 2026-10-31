import { el, icon, countUp } from './utils.js';

export function section(title, { tag = '', actions = null } = {}) {
  return el('div', { class: 'sec-head' },
    el('span', { class: 'sec-title', text: title }),
    tag ? el('span', { class: 'sec-tag', text: tag, title: 'Implemented with a hand-built C# data structure' }) : null,
    el('span', { class: 'sec-line' }),
    actions,
  );
}

export function statCard({ label, value, ico = 'chart', tone = '', sub = null, fmtFn = null, animate = true }) {
  const valueNode = el('div', { class: 'stat-value' });
  const c = el('div', { class: `stat ${tone}` },
    el('div', { class: 'stat-top' },
      el('span', { class: 'stat-label', text: label }),
      el('span', { class: `stat-ico ${tone === 'red' || tone === 'amber' || tone === 'teal' ? tone : ''}`, html: icon(ico, 17) }),
    ),
    valueNode,
    sub ? el('div', { class: 'stat-foot', text: sub }) : null,
  );
  if (animate && typeof value === 'number') {
    countUp(valueNode, value, { fmtFn: fmtFn || ((v) => Math.round(v).toLocaleString('en-US')) });
  } else if (value !== null && value !== undefined) {
    valueNode.innerHTML = value;
  }
  return c;
}

export function dataTable({ columns, rows, onRowClick, empty = null, footer = null }) {
  // columns: [{key, label, r?, c?, mono?, sortable?, sort?: 'num'|'str', render?(row)}]
  const state = { sortKey: null, dir: 1 };

  const thead = el('thead');
  const trh = el('tr');
  for (const col of columns) {
    const th = el('th', {
      class: [
        col.r ? 'r' : '', col.c ? 'c' : '',
        col.sortable ? 'sortable' : '',
        state.sortKey === col.key ? 'sorted' : '',
      ].filter(Boolean).join(' '),
      text: col.label,
    });
    if (col.sortable) {
      th.append(el('span', { class: 'sort-ico', text: state.sortKey === col.key ? (state.dir > 0 ? '▲' : '▼') : '⇅' }));
      th.addEventListener('click', () => {
        if (state.sortKey === col.key) state.dir *= -1;
        else { state.sortKey = col.key; state.dir = col.sort === 'str' ? 1 : -1; }
        Array.from(thead.querySelectorAll('th')).forEach((n) => n.classList.remove('sorted'));
        th.classList.add('sorted');
        th.querySelector('.sort-ico').textContent = state.dir > 0 ? '▲' : '▼';
        renderBody();
      });
    }
    trh.append(th);
  }
  thead.append(trh);

  const tbody = el('tbody');

  function sortedRows() {
    if (!state.sortKey) return rows;
    const col = columns.find((c) => c.key === state.sortKey);
    const val = (r) => {
      const raw = col.sort === 'render' && col.raw ? col.raw(r) : r[state.sortKey];
      return col.sort === 'str' ? String(raw ?? '') : +raw;
    };
    return [...rows].sort((a, b) => {
      const va = val(a), vb = val(b);
      if (va < vb) return -state.dir;
      if (va > vb) return state.dir;
      return 0;
    });
  }

  function renderBody() {
    tbody.innerHTML = '';
    if (!rows.length) {
      tbody.append(el('tr', {}, el('td', { colspan: String(columns.length) }, empty || emptyState())));
      return;
    }
    for (const r of sortedRows()) {
      const tds = columns.map((col) => {
        const content = col.render ? col.render(r) : (r[col.key] ?? '—');
        return el('td', {
          class: [col.r ? 'r' : '', col.c ? 'c' : '', col.mono ? 'mono' : ''].filter(Boolean).join(' '),
        }, content);
      });
      const tr = el('tr', { class: onRowClick ? 'clickable' : '' }, tds);
      if (onRowClick) {
        tr.addEventListener('click', () => onRowClick(r));
        tr.setAttribute('tabindex', '0');
        tr.addEventListener('keydown', (e) => { if (e.key === 'Enter') onRowClick(r); });
      }
      tbody.append(tr);
    }
  }

  renderBody();
  const wrap = el('div', { class: 'tbl-wrap' }, el('table', { class: 'tbl' }, thead, tbody));
  return el('div', { class: 'card' }, wrap, footer ? footer : null);
}

export function emptyState({ ico = 'db', title = 'Nothing here yet', sub = '' } = {}) {
  return el('div', { class: 'empty' },
    el('div', { class: 'empty-ico', html: icon(ico, 24) }),
    el('div', { class: 'empty-title', text: title }),
    sub ? el('div', { class: 'empty-sub', text: sub }) : null,
  );
}

export function badge(text, tone = 'gray', { dot = false } = {}) {
  return el('span', { class: `badge badge-${tone}` }, dot ? el('span', { class: 'dot' }) : null, text);
}

export const statusBadge = (status) => {
  const map = { Completed: 'green', Failed: 'red', Running: 'amber', Draft: 'gray', Ready: 'teal' };
  return badge(status, map[status] || 'gray', { dot: true });
};

export const reasonBadge = (reason) => {
  const map = {
    Signal: ['teal', 'SIG'], StopLoss: ['red', 'SL'],
    TakeProfit: ['green', 'TP'], EndOfData: ['amber', 'EOD'],
  };
  const [tone, label] = map[reason] || ['gray', reason];
  return el('span', { class: `badge badge-${tone} trade-reason`, title: reason }, label);
};

export function kv(pairs) {
  return el('div', { class: 'kv' },
    pairs.filter(Boolean).map(([k, v]) =>
      el('div', { class: 'kv-row' }, el('span', { class: 'kv-key', text: k }), el('span', { class: 'kv-val', html: v })),
    ),
  );
}

export function skeletonCards(n = 4) {
  return el('div', { class: 'grid grid-4' },
    Array.from({ length: n }, () => el('div', { class: 'skel card-skel' })),
  );
}

export function skeletonRows(n = 6) {
  return el('div', { style: 'padding:16px' }, Array.from({ length: n }, () => el('div', { class: 'skel row-skel' })));
}

export function field(label, control, { hint = null, required = false } = {}) {
  return el('div', { class: 'field' },
    el('label', { class: 'field-label' }, label, required ? el('span', { class: 'req', text: '*' }) : null),
    control,
    hint ? el('span', { class: 'field-hint', text: hint }) : null,
  );
}

export function cx(complexity, title) {
  return el('span', { class: 'cx', text: complexity, 'data-tip': title || `Hand-implemented in C# — ${complexity}` });
}

export function mockNote(text = 'Demo mode — data is generated in your browser. Connect the ASP.NET Core API to go live.') {
  return el('div', { class: 'hint-banner', style: 'margin-bottom:16px' },
    el('span', { html: icon('info', 16) }),
    el('span', { text: text }),
  );
}
