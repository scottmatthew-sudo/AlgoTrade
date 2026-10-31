const NF2 = new Intl.NumberFormat('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const NF0 = new Intl.NumberFormat('en-US', { maximumFractionDigits: 0 });
const NF6 = new Intl.NumberFormat('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 6 });

export const fmt = {
  money(v, { sign = false, dash = '—' } = {}) {
    if (v === null || v === undefined || Number.isNaN(+v)) return dash;
    const n = +v;
    const s = NF2.format(Math.abs(n));
    if (sign) return (n > 0 ? '+$' : n < 0 ? '-$' : '$') + s;
    return (n < 0 ? '-$' : '$') + s;
  },
  num(v, dash = '—') {
    if (v === null || v === undefined || Number.isNaN(+v)) return dash;
    return NF2.format(+v);
  },
  int(v, dash = '—') {
    if (v === null || v === undefined || Number.isNaN(+v)) return dash;
    return NF0.format(+v);
  },
  price(v, dash = '—') {
    if (v === null || v === undefined || Number.isNaN(+v)) return dash;
    return NF6.format(+v);
  },
  pct(v, { digits = 2, sign = false } = {}) {
    if (v === null || v === undefined || Number.isNaN(+v)) return '—';
    const n = +v;
    return (sign && n > 0 ? '+' : '') + n.toFixed(digits) + '%';
  },
  x(v) {
    if (v === null || v === undefined || Number.isNaN(+v)) return 'N/A';
    return (+v).toFixed(2) + '×';
  },
  date(ts, style = 'short') {
    if (!ts) return '—';
    const d = new Date(ts);
    if (Number.isNaN(d.getTime())) return String(ts);
    if (style === 'date') return d.toLocaleDateString('en-CA'); // YYYY-MM-DD
    if (style === 'time') return d.toLocaleTimeString('en-GB', { hour12: false });
    if (style === 'full') return d.toLocaleDateString('en-CA') + ' ' + d.toLocaleTimeString('en-GB', { hour12: false });
    return d.toLocaleDateString('en-CA');
  },
  ago(ts) {
    if (!ts) return '—';
    const s = Math.max(0, (Date.now() - new Date(ts).getTime()) / 1000);
    if (s < 60) return 'just now';
    if (s < 3600) return Math.floor(s / 60) + 'm ago';
    if (s < 86400) return Math.floor(s / 3600) + 'h ago';
    return Math.floor(s / 86400) + 'd ago';
  },
  duration(ms) {
    if (ms === null || ms === undefined) return '—';
    if (ms < 1000) return Math.round(ms) + ' ms';
    if (ms < 60000) return (ms / 1000).toFixed(2) + ' s';
    const m = Math.floor(ms / 60000), s = Math.round((ms % 60000) / 1000);
    return `${m}m ${s}s`;
  },
  compact(v) {
    const n = +v;
    if (Math.abs(n) >= 1e9) return (n / 1e9).toFixed(1) + 'B';
    if (Math.abs(n) >= 1e6) return (n / 1e6).toFixed(1) + 'M';
    if (Math.abs(n) >= 1e3) return (n / 1e3).toFixed(1) + 'K';
    return NF0.format(n);
  },
  pnlClass(v) { return +v > 0 ? 'up' : +v < 0 ? 'down' : 'dim'; },
};

export const pnlClass = fmt.pnlClass;

export function el(tag, attrs = {}, ...children) {
  const node = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs || {})) {
    if (v === null || v === undefined || v === false) continue;
    if (k === 'class') node.className = v;
    else if (k === 'html') node.innerHTML = v;
    else if (k === 'text') node.textContent = v;
    else if (k.startsWith('on') && typeof v === 'function') node.addEventListener(k.slice(2).toLowerCase(), v);
    else if (k === 'dataset') Object.assign(node.dataset, v);
    else if (k === 'style' && typeof v === 'object') Object.assign(node.style, v);
    else if (v === true) node.setAttribute(k, '');
    else node.setAttribute(k, v);
  }
  for (const c of children.flat(Infinity)) {
    if (c === null || c === undefined || c === false) continue;
    node.append(c.nodeType ? c : document.createTextNode(String(c)));
  }
  return node;
}

const I = {
  check:  '<path d="m5 13 4 4L19 7"/>',
  x:      '<path d="M18 6 6 18M6 6l12 12"/>',
  alert:  '<path d="M12 9v4m0 4h.01"/><path d="M10.3 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.7 3.86a2 2 0 0 0-3.4 0z"/>',
  info:   '<circle cx="12" cy="12" r="9"/><path d="M12 8h.01M12 12v4"/>',
  upload: '<path d="M12 15V3m0 0 4 4m-4-4L8 7"/><path d="M4 14v4a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-4"/>',
  db:     '<ellipse cx="12" cy="5.5" rx="8" ry="3"/><path d="M4 5.5V12c0 1.66 3.58 3 8 3s8-1.34 8-3V5.5"/><path d="M4 12v6.5c0 1.66 3.58 3 8 3s8-1.34 8-3V12"/>',
  play:   '<circle cx="12" cy="12" r="9"/><path d="M10 8.5v7l6-3.5z"/>',
  chart:  '<path d="M3 20h18"/><path d="M6 20v-6m6 6V9m6 11V4"/>',
  trash:  '<path d="M3 6h18M8 6V4a1 1 0 0 1 1-1h6a1 1 0 0 1 1 1v2m3 0v13a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6"/>',
  save:   '<path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"/><path d="M17 21v-8H7v8M7 3v5h8"/>',
  edit:   '<path d="M17 3a2.8 2.8 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5z"/>',
  eye:    '<path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7-10-7-10-7z"/><circle cx="12" cy="12" r="3"/>',
  flash:  '<path d="M13 2 3 14h7l-1 8 10-12h-7l1-8z"/>',
  arrow:  '<path d="M5 12h14m-6-6 6 6-6 6"/>',
  search: '<circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/>',
  download:'<path d="M12 3v12m0 0 4-4m-4 4-4-4"/><path d="M4 17v2a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-2"/>',
  stop:   '<rect x="7" y="7" width="10" height="10" rx="1.5"/>',
  back:   '<path d="M19 12H5m6 6-6-6 6-6"/>',
  layers: '<path d="m12 2 9 5-9 5-9-5 9-5z"/><path d="m3 12 9 5 9-5"/><path d="m3 17 9 5 9-5"/>',
  timer:  '<circle cx="12" cy="13" r="8"/><path d="M12 9v4l2.5 2.5M9 2h6"/>',
};

export function icon(name, size = 16) {
  const body = I[name] || I.info;
  return `<svg viewBox="0 0 24 24" width="${size}" height="${size}" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${body}</svg>`;
}

export function toast(title, { type = 'ok', msg = '', timeout = 3800 } = {}) {
  const root = document.getElementById('toast-root');
  if (!root) return;
  const t = el('div', { class: `toast ${type}`, role: 'status' },
    el('div', { class: 'toast-ico', html: icon(type === 'ok' ? 'check' : type === 'err' ? 'alert' : type === 'warn' ? 'alert' : 'info', 15) }),
    el('div', {},
      el('div', { class: 'toast-title', text: title }),
      msg ? el('div', { class: 'toast-msg', text: msg }) : null,
    ),
  );
  root.append(t);
  setTimeout(() => {
    t.classList.add('out');
    setTimeout(() => t.remove(), 260);
  }, timeout);
}

export function openModal({ title, body, footer, wide = false }) {
  const root = document.getElementById('modal-root');
  root.innerHTML = '';
  const scrim = el('div', { class: 'modal-scrim', role: 'dialog', 'aria-modal': 'true', 'aria-label': title });
  const m = el('div', { class: 'modal', style: wide ? 'max-width:680px' : '' },
    el('div', { class: 'modal-head' },
      el('div', { class: 'modal-title', text: title }),
      el('button', { class: 'icon-btn', 'aria-label': 'Close', html: icon('x', 17), onclick: close }),
    ),
    el('div', { class: 'modal-body' }, body),
    footer ? el('div', { class: 'modal-foot' }, footer) : null,
  );
  scrim.append(m);
  scrim.addEventListener('mousedown', (e) => { if (e.target === scrim) close(); });
  root.append(scrim);
  function close() { scrim.style.opacity = '0'; setTimeout(() => { scrim.remove(); }, 180); }
  return { close, root: m };
}

export function confirmModal({ title = 'Are you sure?', message, confirmLabel = 'Confirm', danger = true }) {
  return new Promise((resolve) => {
    let done = false;
    const finish = (v) => { if (!done) { done = true; resolve(v); } };
    const m = openModal({
      title,
      body: el('div', {}, el('p', { style: 'font-size:13.5px;color:var(--text-2);line-height:1.6', text: message })),
      footer: [
        el('button', { class: 'btn', text: 'Cancel', onclick: () => { finish(false); modal.close(); } }),
        el('button', { class: `btn ${danger ? 'btn-danger' : 'btn-primary'}`, text: confirmLabel, onclick: () => { finish(true); modal.close(); } }),
      ],
    });
  });
}

export const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));

export function debounce(fn, ms = 200) {
  let t;
  return (...a) => { clearTimeout(t); t = setTimeout(() => fn(...a), ms); };
}

export const uid = () => Math.random().toString(36).slice(2, 9);

export function countUp(node, target, { ms = 750, fmtFn = NF0.format } = {}) {
  const start = performance.now();
  const from = 0;
  const tick = (now) => {
    const p = clamp((now - start) / ms, 0, 1);
    const eased = 1 - Math.pow(1 - p, 3);
    node.textContent = fmtFn(from + (target - from) * eased);
    if (p < 1) requestAnimationFrame(tick);
  };
  requestAnimationFrame(tick);
}

export function setRangeFill(input) {
  const pct = ((input.value - input.min) / (input.max - input.min)) * 100;
  input.style.setProperty('--fill', pct + '%');
}

export async function copyText(text) {
  try { await navigator.clipboard.writeText(text); toast('Copied to clipboard', { type: 'info', timeout: 1800 }); }
  catch { toast('Copy failed', { type: 'err', timeout: 1800 }); }
}
