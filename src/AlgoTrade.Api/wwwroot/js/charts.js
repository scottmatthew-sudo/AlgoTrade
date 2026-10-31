const C = {
  up: '#22c55e', dn: '#f43f5e', upFill: 'rgba(34,197,94,.9)', dnFill: 'rgba(244,63,94,.9)',
  grid: 'rgba(148,163,184,.07)', axis: 'rgba(148,163,184,.55)', text: '#64748b',
  cross: 'rgba(232,238,248,.45)', accent: '#34d399', warn: '#f59e0b',
  dd: 'rgba(244,63,94,.14)', ddLine: 'rgba(244,63,94,.5)',
};

const css = getComputedStyle(document.documentElement);
const FONT = (css.getPropertyValue('--font-mono') || 'monospace').trim();

function setupCanvas(box) {
  const dpr = Math.min(window.devicePixelRatio || 1, 2.5);
  const w = box.clientWidth || 300, h = box.clientHeight || 200;
  let cv = box.querySelector('canvas');
  if (!cv) { cv = document.createElement('canvas'); box.append(cv); }
  cv.width = Math.round(w * dpr); cv.height = Math.round(h * dpr);
  const ctx = cv.getContext('2d');
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  return { cv, ctx, w, h };
}

const niceTicks = (min, max, count = 5) => {
  const span = max - min || 1;
  const step0 = span / count;
  const mag = Math.pow(10, Math.floor(Math.log10(step0)));
  const norm = step0 / mag;
  const step = (norm < 1.5 ? 1 : norm < 3.5 ? 2 : norm < 7.5 ? 5 : 10) * mag;
  const first = Math.ceil(min / step) * step;
  const out = [];
  for (let v = first; v <= max + step * 0.01; v += step) out.push(v);
  return out;
};

const fmtAxisPrice = (v) => {
  const a = Math.abs(v);
  if (a >= 10000) return (v / 1000).toFixed(1) + 'k';
  if (a >= 100) return v.toFixed(0);
  if (a >= 1) return v.toFixed(2);
  return v.toPrecision(3);
};
const fmtAxisTime = (ts) => {
  const d = new Date(ts);
  return `${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')} ${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`;
};

export function mountCandleChart(box, candles, opts = {}) {
  box.classList.add('chart-panel');
  const tip = document.createElement('div');
  tip.className = 'chart-tip';
  box.append(tip);
  let view = { start: Math.max(0, candles.length - (opts.visible ?? 180)), end: candles.length };
  let hover = -1;
  let raf = 0;

  const draw = () => {
    const { ctx, w, h } = setupCanvas(box);
    const data = candles.slice(view.start, view.end);
    if (!data.length) return;
    const padR = 58, padB = 20, padT = 8;
    const plotW = w - padR, volH = (h - padB - padT) * 0.2, priceH = h - padB - padT - volH - 6;

    let hi = -Infinity, lo = Infinity, vMax = 0;
    for (const c of data) { hi = Math.max(hi, c.h); lo = Math.min(lo, c.l); vMax = Math.max(vMax, c.v); }
    const pad = (hi - lo) * 0.06;
    hi += pad; lo -= pad;
    const X = (i) => (i + 0.5) * (plotW / data.length);
    const Y = (p) => padT + (hi - p) / (hi - lo) * priceH;
    ctx.font = `10px ${FONT}`;
    for (const t of niceTicks(lo, hi, 5)) {
      const y = Y(t);
      ctx.strokeStyle = C.grid; ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(plotW, y); ctx.stroke();
      ctx.fillStyle = C.text; ctx.textAlign = 'left'; ctx.fillText(fmtAxisPrice(t), plotW + 6, y + 3);
    }
    const stepX = Math.max(1, Math.floor(data.length / 6));
    ctx.textAlign = 'center';
    for (let i = 0; i < data.length; i += stepX) {
      ctx.fillText(fmtAxisTime(data[i].ts), X(i), h - 6);
    }
    const bw = Math.max(1.5, (plotW / data.length) * 0.62);
    data.forEach((c, i) => {
      const x = X(i), up = c.c >= c.o;
      ctx.strokeStyle = ctx.fillStyle = up ? C.up : C.dn;
      ctx.lineWidth = Math.min(1.4, bw * 0.22);
      ctx.beginPath(); ctx.moveTo(x, Y(c.h)); ctx.lineTo(x, Y(c.l)); ctx.stroke();
      const yo = Y(c.o), yc = Y(c.c);
      const top = Math.min(yo, yc);
      ctx.fillRect(x - bw / 2, top, bw, Math.max(1, Math.abs(yc - yo)));
      const vh = vMax ? (c.v / vMax) * (volH - 4) : 0;
      ctx.fillStyle = up ? 'rgba(34,197,94,.35)' : 'rgba(244,63,94,.35)';
      ctx.fillRect(x - bw / 2, padT + priceH + 6 + (volH - vh), bw, vh);
    });
    const last = data[data.length - 1];
    const ly = Y(last.c);
    ctx.setLineDash([4, 3]);
    ctx.strokeStyle = last.c >= last.o ? C.up : C.dn;
    ctx.beginPath(); ctx.moveTo(0, ly); ctx.lineTo(plotW, ly); ctx.stroke();
    ctx.setLineDash([]);
    ctx.fillStyle = last.c >= last.o ? C.up : C.dn;
    ctx.fillRect(plotW + 2, ly - 8, padR - 6, 16);
    ctx.fillStyle = '#04120b'; ctx.textAlign = 'left';
    ctx.fillText(fmtAxisPrice(last.c), plotW + 6, ly + 3.5);
    if (hover >= 0 && hover >= view.start && hover < view.end) {
      const x = X(hover - view.start);
      ctx.strokeStyle = C.cross; ctx.setLineDash([3, 3]);
      ctx.beginPath(); ctx.moveTo(x, padT); ctx.lineTo(x, h - padB); ctx.stroke();
      ctx.beginPath(); ctx.moveTo(0, Y(candles[hover].c)); ctx.lineTo(plotW, Y(candles[hover].c)); ctx.stroke();
      ctx.setLineDash([]);
      const c = candles[hover];
      tip.style.opacity = '1';
      tip.style.left = Math.min(Math.max(x - 60, 4), box.clientWidth - 150) + 'px';
      tip.style.top = '8px';
      tip.innerHTML = `<b>${c.ts}</b><br>O ${c.o} · H ${c.h}<br>L ${c.l} · C <b style="color:${c.c >= c.o ? C.up : C.dn}">${c.c}</b><br>Vol ${c.v.toLocaleString()}`;
      if (opts.onHover) opts.onHover(c);
    } else {
      tip.style.opacity = '0';
      if (opts.onHover) opts.onHover(null);
    }
  };

  const schedule = () => { cancelAnimationFrame(raf); raf = requestAnimationFrame(draw); };
  let dragging = false, dragX = 0;
  const onWheel = (e) => {
    e.preventDefault();
    const span = view.end - view.start;
    const factor = e.deltaY > 0 ? 1.15 : 0.87;
    const ns = Math.round(span * factor);
    const cx = view.start + span / 2;
    let s = Math.round(cx - ns / 2), en = Math.round(cx + ns / 2);
    s = Math.max(0, Math.min(s, candles.length - 20));
    en = Math.min(candles.length, Math.max(s + 20, en));
    view = { start: s, end: en };
    schedule();
  };
  const onDown = (e) => { dragging = true; dragX = e.clientX; };
  const onMove = (e) => {
    const rect = box.getBoundingClientRect();
    if (dragging) {
      const dx = e.clientX - dragX; dragX = e.clientX;
      const shift = Math.round(dx / (rect.width / (view.end - view.start)));
      let s = view.start - shift, en = view.end - shift;
      if (s < 0) { en -= s; s = 0; }
      if (en > candles.length) { s -= en - candles.length; en = candles.length; }
      view = { start: Math.max(0, s), end: Math.min(candles.length, en) };
      schedule();
      return;
    }
    const x = e.clientX - rect.left;
    const plotW = rect.width - 58;
    const idx = view.start + Math.floor((x / plotW) * (view.end - view.start));
    hover = (idx >= view.start && idx < view.end) ? idx : -1;
    schedule();
  };
  const onUp = () => { dragging = false; };
  const onLeave = () => { dragging = false; hover = -1; schedule(); };
  box.addEventListener('wheel', onWheel, { passive: false });
  box.addEventListener('mousedown', onDown);
  box.addEventListener('mousemove', onMove);
  window.addEventListener('mouseup', onUp);
  box.addEventListener('mouseleave', onLeave);
  const ro = new ResizeObserver(schedule);
  ro.observe(box);
  schedule();

  return {
    update(newCandles) {
      candles = newCandles;
      const span = view.end - view.start;
      view = { start: Math.max(0, candles.length - span), end: candles.length };
      schedule();
    },
    destroy() {
      ro.disconnect();
      box.removeEventListener('wheel', onWheel);
      box.removeEventListener('mousedown', onDown);
      box.removeEventListener('mousemove', onMove);
      window.removeEventListener('mouseup', onUp);
      box.removeEventListener('mouseleave', onLeave);
      tip.remove();
    },
  };
}

export function mountEquityChart(box, values, opts = {}) {
  box.classList.add('chart-panel');
  const tip = document.createElement('div');
  tip.className = 'chart-tip';
  box.append(tip);
  let hover = -1, raf = 0;

  const draw = () => {
    const { ctx, w, h } = setupCanvas(box);
    if (!values.length) return;
    const padR = 58, padB = 20, padT = 10;
    const plotW = w - padR, plotH = h - padB - padT;
    let hi = -Infinity, lo = Infinity, peak = -Infinity;
    const peaks = values.map(v => (peak = Math.max(peak, v)));
    for (const v of values) { hi = Math.max(hi, v); lo = Math.min(lo, v); }
    const pad = (hi - lo) * 0.08;
    hi += pad; lo -= pad;
    const X = (i) => (i / Math.max(1, values.length - 1)) * plotW;
    const Y = (v) => padT + (hi - v) / (hi - lo) * plotH;

    ctx.font = `10px ${FONT}`;
    const narrow = hi - lo < 200;
    for (const t of niceTicks(lo, hi, 5)) {
      const y = Y(t);
      ctx.strokeStyle = C.grid; ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(plotW, y); ctx.stroke();
      ctx.fillStyle = C.text; ctx.textAlign = 'left';
      ctx.fillText('$' + (narrow ? t.toFixed(2) : fmtAxisPrice(t)), plotW + 6, y + 3);
    }
    ctx.beginPath();
    ctx.moveTo(X(0), Y(values[0]));
    for (let i = 1; i < values.length; i++) ctx.lineTo(X(i), Y(values[i]));
    for (let i = values.length - 1; i >= 0; i--) ctx.lineTo(X(i), Y(peaks[i]));
    ctx.closePath();
    ctx.fillStyle = C.dd; ctx.fill();
    const grad = ctx.createLinearGradient(0, padT, 0, padT + plotH);
    grad.addColorStop(0, 'rgba(52,211,153,.22)');
    grad.addColorStop(1, 'rgba(52,211,153,0)');
    ctx.beginPath();
    ctx.moveTo(X(0), Y(values[0]));
    for (let i = 1; i < values.length; i++) ctx.lineTo(X(i), Y(values[i]));
    ctx.lineTo(X(values.length - 1), padT + plotH);
    ctx.lineTo(X(0), padT + plotH);
    ctx.closePath();
    ctx.fillStyle = grad; ctx.fill();
    ctx.lineWidth = 1.8; ctx.strokeStyle = C.accent;
    ctx.beginPath();
    ctx.moveTo(X(0), Y(values[0]));
    for (let i = 1; i < values.length; i++) ctx.lineTo(X(i), Y(values[i]));
    ctx.stroke();
    ctx.lineWidth = 1; ctx.strokeStyle = C.ddLine; ctx.setLineDash([3, 4]);
    ctx.beginPath();
    ctx.moveTo(X(0), Y(peaks[0]));
    for (let i = 1; i < values.length; i++) ctx.lineTo(X(i), Y(peaks[i]));
    ctx.stroke();
    ctx.setLineDash([]);
    if (opts.initial != null) {
      const y = Y(opts.initial);
      ctx.strokeStyle = 'rgba(148,163,184,.35)'; ctx.setLineDash([5, 4]);
      ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(plotW, y); ctx.stroke();
      ctx.setLineDash([]);
      ctx.fillStyle = C.text; ctx.textAlign = 'left';
      ctx.fillText('initial $' + fmtAxisPrice(opts.initial), 6, y - 4);
    }
    if (hover >= 0 && hover < values.length) {
      const x = X(hover), y = Y(values[hover]);
      ctx.strokeStyle = C.cross; ctx.setLineDash([3, 3]);
      ctx.beginPath(); ctx.moveTo(x, padT); ctx.lineTo(x, h - padB); ctx.stroke();
      ctx.setLineDash([]);
      ctx.fillStyle = C.accent;
      ctx.beginPath(); ctx.arc(x, y, 3.5, 0, Math.PI * 2); ctx.fill();
      tip.style.opacity = '1';
      tip.style.left = Math.min(Math.max(x - 60, 4), box.clientWidth - 140) + 'px';
      tip.style.top = '8px';
      tip.innerHTML = `Equity <b style="color:${C.accent}">$${values[hover].toLocaleString('en-US', { maximumFractionDigits: 2 })}</b><br>Peak $${peaks[hover].toLocaleString('en-US', { maximumFractionDigits: 2 })} · DD ${peaks[hover] ? (((peaks[hover] - values[hover]) / peaks[hover]) * 100).toFixed(2) : '0'}%`;
      if (opts.onHover) opts.onHover(hover);
    } else { tip.style.opacity = '0'; if (opts.onHover) opts.onHover(null); }
  };

  const schedule = () => { cancelAnimationFrame(raf); raf = requestAnimationFrame(draw); };
  const onMove = (e) => {
    const rect = box.getBoundingClientRect();
    const x = e.clientX - rect.left;
    hover = Math.round((x / (rect.width - 58)) * (values.length - 1));
    if (hover < 0 || hover >= values.length) hover = -1;
    schedule();
  };
  const onLeave = () => { hover = -1; schedule(); };
  box.addEventListener('mousemove', onMove);
  box.addEventListener('mouseleave', onLeave);
  const ro = new ResizeObserver(schedule);
  ro.observe(box);
  schedule();

  return {
    update(v) { values = v; hover = -1; schedule(); },
    destroy() { ro.disconnect(); box.removeEventListener('mousemove', onMove); box.removeEventListener('mouseleave', onLeave); tip.remove(); },
  };
}

export function sparkline(container, values, color = C.accent, { h = 30, fill = true } = {}) {
  container.style.position = 'relative';
  const { cv, ctx, w } = setupCanvas2(container, h);
  if (!values || values.length < 2) return;
  const hi = Math.max(...values), lo = Math.min(...values);
  const X = (i) => (i / (values.length - 1)) * (w - 2) + 1;
  const Y = (v) => h - 3 - ((v - lo) / (hi - lo || 1)) * (h - 6);
  if (fill) {
    const g = ctx.createLinearGradient(0, 0, 0, h);
    g.addColorStop(0, color + '44'); g.addColorStop(1, color + '00');
    ctx.beginPath();
    ctx.moveTo(X(0), h);
    values.forEach((v, i) => ctx.lineTo(X(i), Y(v)));
    ctx.lineTo(X(values.length - 1), h);
    ctx.closePath();
    ctx.fillStyle = g; ctx.fill();
  }
  ctx.beginPath();
  ctx.moveTo(X(0), Y(values[0]));
  values.forEach((v, i) => ctx.lineTo(X(i), Y(v)));
  ctx.strokeStyle = color; ctx.lineWidth = 1.4; ctx.stroke();
}
function setupCanvas2(container, h) {
  const dpr = Math.min(window.devicePixelRatio || 1, 2);
  const w = container.clientWidth || 100;
  const cv = document.createElement('canvas');
  cv.style.position = 'absolute'; cv.style.inset = '0';
  cv.width = Math.round(w * dpr); cv.height = Math.round(h * dpr);
  cv.style.width = w + 'px'; cv.style.height = h + 'px';
  const ctx = cv.getContext('2d');
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  container.append(cv);
  return { cv, ctx, w };
}
