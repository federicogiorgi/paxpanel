'use strict';

const HISTORY = 60;
const hist = { up: [], down: [] };
const DAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const MB = 1024 * 1024;
let frames = 0;
let storageLogoKey = null;
let coreKey = null;
let fanState = [];

const $ = (sel, root = document) => root.querySelector(sel);
const pad2 = n => String(n).padStart(2, '0');
const isNum = v => typeof v === 'number' && Number.isFinite(v);
const num = (v, d = 0) => (isNum(v) ? v.toFixed(d) : '–');
const withUnit = (v, d, unit) => (isNum(v) ? v.toFixed(d) + unit : '–');
const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));

/* ---- accent colour from CPU temperature ----
   35 °C and below: blue (hue 210) · 60 °C: green (135) · 75 °C: yellow-orange · 90 °C and above: red (0).
   The temperature is smoothed (~10 s) so the colour drifts instead of flickering with every load spike. */
const HUE_STOPS = [[35, 210], [60, 135], [90, 0]];
function hueFor(tempC) {
  if (!isNum(tempC)) return null;
  if (tempC <= HUE_STOPS[0][0]) return HUE_STOPS[0][1];
  for (let i = 1; i < HUE_STOPS.length; i++) {
    const [t0, h0] = HUE_STOPS[i - 1], [t1, h1] = HUE_STOPS[i];
    if (tempC <= t1) return h0 + (h1 - h0) * (tempC - t0) / (t1 - t0);
  }
  return HUE_STOPS[HUE_STOPS.length - 1][1];
}
let smoothTemp = null;
let hue = 0;
function updateAccent(tempC) {
  if (!isNum(tempC)) return;
  smoothTemp = smoothTemp === null ? tempC : smoothTemp + (tempC - smoothTemp) * 0.1;
  hue = hueFor(smoothTemp);
  document.documentElement.style.setProperty('--hue', hue.toFixed(1));
}
// core bar fill: same hue as the accent, brighter with more load
const heat = load => `hsl(${hue.toFixed(1)} 90% ${(20 + clamp(load, 0, 100) * 0.38).toFixed(1)}%)`;

function fit() {
  const z = Math.min(innerWidth / 400, innerHeight / 1280);
  document.body.style.zoom = z > 0 ? String(z) : '1';
}
addEventListener('resize', fit);
fit();

function setText(el, text) { if (el.textContent !== text) el.textContent = text; }

function setLogo(img, src) {
  if (!src) { img.hidden = true; img.removeAttribute('src'); return; }
  if (img.getAttribute('src') === src) return;
  img.onerror = () => { img.hidden = true; };
  img.onload = () => { img.hidden = false; };
  img.src = src;
}

function rate(bps) {
  if (!isNum(bps)) return '–';
  if (bps < 1024) return bps.toFixed(0) + ' B/s';
  if (bps < MB) return (bps / 1024).toFixed(1) + ' KB/s';
  return (bps / MB).toFixed(1) + ' MB/s';
}

const mbs = bps => (isNum(bps) ? (bps / MB < 0.05 ? '0' : (bps / MB).toFixed(1)) : '–');

function uptime(sec) {
  if (!isNum(sec)) return '–';
  const d = Math.floor(sec / 86400), h = Math.floor(sec % 86400 / 3600), m = Math.floor(sec % 3600 / 60);
  return d > 0 ? `${d}d ${pad2(h)}h` : `${h}h ${pad2(m)}m`;
}

function push(arr, v) { arr.push(isNum(v) ? v : null); if (arr.length > HISTORY) arr.shift(); }

function renderClock(iso) {
  const t = iso ? new Date(iso) : new Date();
  if (Number.isNaN(t.getTime())) return;
  setText($('#clock'), `${pad2(t.getHours())}:${pad2(t.getMinutes())}`);
  setText($('#date'), `${DAYS[t.getDay()]} ${pad2(t.getDate())}-${MONTHS[t.getMonth()]}-${String(t.getFullYear()).slice(2)}`);
}

function renderBrand(root, brand, fallbackTitle) {
  setText($('.t', root), (brand && brand.title) || fallbackTitle);
  const sub = $('.sub', root);
  if (sub) setText(sub, (brand && brand.subtitle) || '');
  setLogo($('.logo', root), brand && brand.logo);
}

function renderGauge(root, temp, load) {
  setText($('.temp', root), num(temp));
  setText($('.load', root), isNum(load) ? load.toFixed(0) + ' %' : '–');
  $('.fill', root).style.strokeDasharray = `${isNum(load) ? clamp(load, 0, 100) : 0} 100`;
}

function spark(svg, series, max) {
  const w = 372, h = 40, step = w / (HISTORY - 1);
  svg.setAttribute('viewBox', `0 0 ${w} ${h}`);
  svg.setAttribute('preserveAspectRatio', 'none');
  let out = `<rect class="bg" width="${w}" height="${h}"/>`;
  for (const { data, cls } of series) {
    if (data.length < 2) continue;
    const x0 = w - (data.length - 1) * step;
    const pts = data.map((v, i) => {
      const y = h - 1 - (clamp(v ?? 0, 0, max) / max) * (h - 2);
      return `${(x0 + i * step).toFixed(1)},${y.toFixed(1)}`;
    }).join(' ');
    out += `<polygon class="${cls}-area" points="${x0.toFixed(1)},${h} ${pts} ${w},${h}"/>`;
    out += `<polyline class="${cls}" points="${pts}"/>`;
  }
  svg.innerHTML = out;
}

function meter(sel, used, total, label) {
  const root = $(sel);
  setText($('.k', root), label);
  const ok = isNum(used) && isNum(total) && total > 0;
  setText($('.amt', root), ok ? `${(used / 1024).toFixed(1)} / ${(total / 1024).toFixed(0)} GB` : '–');
  const pct = ok ? clamp(used / total * 100, 0, 100) : 0;
  $('.bar i', root).style.width = pct + '%';
  setText($('.bar em', root), ok ? pct.toFixed(0) + ' %' : '–');
}

/* ---- per-core load bars: P-cores wide, a small gap, then E-cores ---- */
function renderCores(cores) {
  const box = $('#cbars');
  const key = cores.map(c => c.name).join(',');
  if (key !== coreKey) {
    coreKey = key;
    const firstE = cores.findIndex(c => !c.performance);
    box.replaceChildren(...cores.flatMap((c, i) => {
      const bar = document.createElement('div');
      bar.className = 'c' + (c.performance ? ' p' : '');
      bar.append(document.createElement('i'));
      if (i === firstE && i > 0) { const gap = document.createElement('div'); gap.className = 'gap'; return [gap, bar]; }
      return [bar];
    }));
    const p = cores.filter(c => c.performance).length, e = cores.length - p;
    setText($('#coreLabel'), cores.length === 0 ? 'CORES' : e > 0 && p > 0 ? `CORES · ${p} P | ${e} E` : `CORES · ${cores.length}`);
  }
  $('#cores').hidden = cores.length === 0;
  box.querySelectorAll('.c').forEach((bar, i) => {
    const load = cores[i] && isNum(cores[i].loadPct) ? cores[i].loadPct : 0;
    const fill = bar.firstChild;
    fill.style.height = load + '%';
    fill.style.background = heat(load);
  });
}

function hottestCore(cores) {
  const withTemp = cores.filter(c => isNum(c.tempC));
  if (withTemp.length === 0) return '–';
  const hot = withTemp.reduce((a, b) => (b.tempC > a.tempC ? b : a));
  return `${hot.name} ${hot.tempC.toFixed(0)} °C`;
}

/* ---- fans: spinning icons whose speed follows rpm / max rpm ---- */
const BLADES = Array.from({ length: 7 }, (_, i) =>
  `<path class="blade" transform="rotate(${(i * 360 / 7).toFixed(2)})" d="M0,-6 C10,-11 16,-24 6,-30 C-1,-25 -3,-14 0,-6 Z"/>`).join('');

function renderFans(fans) {
  const row = $('#fanRow');
  if (row.children.length !== fans.length) {
    fanState = fans.map(() => ({ angle: 0, rps: 0 }));
    row.replaceChildren(...fans.map((_, i) => {
      const unit = document.createElement('div');
      unit.className = 'fanu';
      unit.innerHTML = `<svg viewBox="-36 -36 72 72"><circle class="ring" r="33"/><g>${BLADES}</g><circle class="hub" r="7"/></svg><div class="txt"><b></b><span></span></div>`;
      fanState[i].g = unit.querySelector('g');
      return unit;
    }));
  }
  fans.forEach((f, i) => {
    const unit = row.children[i];
    const frac = isNum(f.rpm) && isNum(f.maxRpm) && f.maxRpm > 0 ? clamp(f.rpm / f.maxRpm, 0, 1) : null;
    setText($('b', unit), num(f.rpm));
    setText($('span', unit), frac === null ? f.label : `${f.label} · ${Math.round(frac * 100)}%`);
    unit.classList.toggle('stopped', !(f.rpm > 0));
    // visual speed only: 0.15 rev/s at the slowest, ~2.75 rev/s at max (real rpm would just blur)
    fanState[i].rps = f.rpm > 0 ? 0.15 + 2.6 * (frac ?? clamp(f.rpm / 3000, 0, 1)) : 0;
  });
  $('#fans').hidden = fans.length === 0;
}

let lastFrame = performance.now();
function spin(now) {
  const dt = Math.min(0.1, (now - lastFrame) / 1000);
  lastFrame = now;
  for (const s of fanState) {
    if (!s.g || s.rps === 0) continue;
    s.angle = (s.angle + s.rps * 360 * dt) % 360;
    s.g.setAttribute('transform', `rotate(${s.angle.toFixed(1)})`);
  }
  requestAnimationFrame(spin);
}
requestAnimationFrame(spin);

function renderBoard(board) {
  const box = $('#board');
  if (box.children.length !== board.length) {
    box.replaceChildren(...board.map(() => {
      const cell = document.createElement('div');
      cell.append(document.createElement('span'), document.createElement('b'));
      return cell;
    }));
  }
  board.forEach((b, i) => {
    const cell = box.children[i];
    setText(cell.children[0], b.label);
    setText(cell.children[1], isNum(b.tempC) ? `${b.tempC.toFixed(0)}°` : '–');
    cell.children[1].classList.toggle('hot', isNum(b.tempC) && b.tempC >= 70);
  });
}

function driveRow(d) {
  const row = document.createElement('div');
  row.className = 'drv' + (d.extra ? ' extra' : '') + (d.mounted ? '' : ' off');
  const name = document.createElement('span');
  name.className = 'n';
  name.textContent = `${d.letter}: ${d.label}`;
  if (d.extra && d.removable) {
    const tag = document.createElement('i');
    tag.className = 'usb';
    tag.textContent = 'USB';
    name.append(' ', tag);
  }
  const mid = document.createElement('div');
  const bar = document.createElement('div');
  bar.className = 'thin';
  const fill = document.createElement('i');
  const ok = d.mounted && isNum(d.usedGB) && isNum(d.totalGB) && d.totalGB > 0;
  fill.style.width = (ok ? clamp(d.usedGB / d.totalGB * 100, 0, 100) : 0) + '%';
  bar.append(fill);
  const small = document.createElement('small');
  const free = document.createElement('span');
  free.textContent = ok ? `${(d.totalGB - d.usedGB).toFixed(0)} GB free` : 'not mounted';
  small.append(free);
  if (isNum(d.readBps) || isNum(d.writeBps)) {
    const io = document.createElement('span');
    const busy = (d.readBps || 0) + (d.writeBps || 0) > 0.05 * MB;
    io.className = 'io' + (busy ? ' busy' : '');
    io.textContent = `R ${mbs(d.readBps)} · W ${mbs(d.writeBps)} MB/s`;
    small.append(io);
  }
  mid.append(bar, small);
  const tc = document.createElement('span');
  tc.className = 'tc';
  const temp = document.createElement('span');
  temp.className = 't';
  temp.textContent = isNum(d.tempC) ? `${d.tempC.toFixed(0)}°C` : '';
  tc.append(temp);
  if (isNum(d.lifePct)) {
    const life = document.createElement('span');
    life.className = 'life';
    life.textContent = `life ${d.lifePct.toFixed(0)}%`;
    tc.append(life);
  }
  row.append(name, mid, tc);
  return row;
}

/* one line per process: label on the first line only, name, %, and a faint bar behind showing the % */
function renderTop(el, list, label, rows, empty) {
  if (el.children.length !== rows) {
    el.replaceChildren(...Array.from({ length: rows }, (_, i) => {
      const row = document.createElement('div');
      row.className = 'pr';
      row.innerHTML = '<i></i><span class="k"></span><span class="n"></span><span class="p"></span>';
      row.querySelector('.k').textContent = i === 0 ? label : '';
      return row;
    }));
  }
  [...el.children].forEach((row, i) => {
    const p = list && list[i];
    row.classList.toggle('empty', !p);
    setText(row.querySelector('.n'), p ? p.name : (i === 0 ? empty : ''));
    setText(row.querySelector('.p'), p ? p.cpuPct.toFixed(0) + '%' : '');
    row.querySelector('i').style.width = p ? clamp(p.cpuPct, 0, 100) + '%' : '0';
  });
}

function renderStorageLogos(logos) {
  const key = JSON.stringify(logos || []);
  if (key === storageLogoKey) return;
  storageLogoKey = key;
  $('#storageLogos').replaceChildren(...(logos || []).map(src => {
    const img = document.createElement('img');
    img.alt = '';
    img.hidden = true;
    setLogo(img, src);
    return img;
  }));
}

function render(s) {
  frames++;
  const ui = s.ui || {};
  updateAccent(s.cpu && s.cpu.tempC);
  renderClock(s.time);

  const cpu = $('#cpu'), gpu = $('#gpu');
  const cores = s.cpu.cores || [];
  renderBrand(cpu, ui.cpu, 'CPU');
  renderGauge(cpu, s.cpu.tempC, s.cpu.loadPct);
  const ghz = v => (isNum(v) ? (v / 1000).toFixed(2) + ' GHz' : '–');
  setText($('.pclk', cpu), ghz(isNum(s.cpu.pClockMHz) ? s.cpu.pClockMHz : s.cpu.clockMHz));
  setText($('.eclk', cpu), ghz(s.cpu.eClockMHz));
  setText($('.volt', cpu), withUnit(s.cpu.voltV, 3, ' V'));
  setText($('.power', cpu), withUnit(s.cpu.powerW, 1, ' W'));
  setText($('.hottest', cpu), hottestCore(cores));

  renderBrand(gpu, ui.gpu, 'GPU');
  renderGauge(gpu, s.gpu.tempC, s.gpu.loadPct);
  setText($('.clock', gpu), withUnit(s.gpu.clockMHz, 0, ' MHz'));
  setText($('.hot', gpu), withUnit(s.gpu.hotspotC, 0, ' °C'));
  setText($('.memj', gpu), withUnit(s.gpu.memJunctionC, 0, ' °C'));
  setText($('.power', gpu), isNum(s.gpu.powerW)
    ? s.gpu.powerW.toFixed(0) + ' W' + (isNum(s.gpu.powerPct) ? ` · ${s.gpu.powerPct.toFixed(0)}%` : '') : '–');
  setText($('.pcie', gpu), isNum(s.gpu.pcieRxBps) || isNum(s.gpu.pcieTxBps)
    ? `↓${mbs(s.gpu.pcieRxBps)} ↑${mbs(s.gpu.pcieTxBps)} MB/s` : '–');

  renderCores(cores);
  const loads = cores.filter(c => isNum(c.loadPct));
  setText($('#coreAvg'), isNum(s.cpu.loadPct) ? s.cpu.loadPct.toFixed(0) + ' %'
    : loads.length ? (loads.reduce((a, c) => a + c.loadPct, 0) / loads.length).toFixed(0) + ' %' : '–');

  const mem = ui.memory || {};
  setLogo($('#mem .logo'), mem.logo);
  meter('#ram', s.ram.usedMB, s.ram.totalMB, `RAM · ${mem.ramType || ''}${isNum(s.ram.speedMTs) ? ' ' + s.ram.speedMTs.toFixed(0) : ''}`);
  meter('#vram', s.gpu.vramUsedMB, s.gpu.vramTotalMB, `VRAM · ${mem.vramType || ''}`);

  renderFans(s.fans || []);
  renderBoard(s.board || []);

  renderStorageLogos(ui.storageLogos);
  $('#drives').replaceChildren(...(s.drives || []).map(driveRow));
  setText($('#more'), s.moreDrives > 0 ? `+${s.moreDrives} more` : '');

  setLogo($('#net .logo'), ui.netLogo);
  setText($('#up'), rate(s.net.upBps));
  setText($('#down'), rate(s.net.downBps));
  push(hist.up, s.net.upBps);
  push(hist.down, s.net.downBps);
  const peak = Math.max(10 * 1024, ...hist.up.map(v => v ?? 0), ...hist.down.map(v => v ?? 0));
  spark($('#netSpark'), [{ data: hist.up, cls: 's2' }, { data: hist.down, cls: 's1' }], peak);

  const sys = s.sys;
  renderTop($('#topCpu'), sys && sys.top, 'CPU', 3, '–');
  renderTop($('#topGpu'), sys && sys.topGpu, 'GPU', 1, 'idle');
  setText($('#uptime'), uptime(sys && sys.uptimeSec));
  $('#sys').hidden = !sys;

  setText($('#warn'), (s.warnings || []).join(' · '));

  if (frames === 5) post('rendered');
}

function post(message) {
  if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage(message);
}

addEventListener('contextmenu', e => { e.preventDefault(); post('contextmenu'); });
if (window.chrome && window.chrome.webview) {
  window.chrome.webview.addEventListener('message', e => {
    try { render(e.data); } catch (err) { console.error(err); }
  });
}
window.paxRender = render;
window.paxHueFor = hueFor;
renderClock(null);
