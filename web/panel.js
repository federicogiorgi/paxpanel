'use strict';

const HISTORY = 60;
const hist = { cpu: [], gpu: [], up: [], down: [] };
const DAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
let frames = 0;
let storageLogoKey = null;

const $ = (sel, root = document) => root.querySelector(sel);
const pad2 = n => String(n).padStart(2, '0');
const isNum = v => typeof v === 'number' && Number.isFinite(v);
const num = (v, d = 0) => (isNum(v) ? v.toFixed(d) : '–');
const withUnit = (v, d, unit) => (isNum(v) ? v.toFixed(d) + unit : '–');
const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));

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
  if (bps < 1024 * 1024) return (bps / 1024).toFixed(1) + ' KB/s';
  return (bps / 1024 / 1024).toFixed(1) + ' MB/s';
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
  const w = 372, h = 52, step = w / (HISTORY - 1);
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

function renderFans(fans) {
  const row = $('#fanRow');
  if (row.children.length !== fans.length) {
    row.replaceChildren(...fans.map(() => {
      const cell = document.createElement('div');
      cell.append(document.createElement('b'), document.createElement('span'));
      return cell;
    }));
  }
  fans.forEach((f, i) => {
    const cell = row.children[i];
    setText(cell.children[0], num(f.rpm));
    setText(cell.children[1], f.label);
  });
  $('#fans').hidden = fans.length === 0;
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
  small.textContent = ok ? `${(d.totalGB - d.usedGB).toFixed(0)} GB free` : 'not mounted';
  mid.append(bar, small);
  const temp = document.createElement('span');
  temp.className = 't';
  temp.textContent = isNum(d.tempC) ? `${d.tempC.toFixed(0)}°C` : '';
  row.append(name, mid, temp);
  return row;
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
  renderClock(s.time);

  const cpu = $('#cpu'), gpu = $('#gpu');
  renderBrand(cpu, ui.cpu, 'CPU');
  renderGauge(cpu, s.cpu.tempC, s.cpu.loadPct);
  setText($('.clock', cpu), isNum(s.cpu.clockMHz) ? (s.cpu.clockMHz / 1000).toFixed(2) + ' GHz' : '–');
  setText($('.volt', cpu), withUnit(s.cpu.voltV, 3, ' V'));
  setText($('.power', cpu), withUnit(s.cpu.powerW, 1, ' W'));

  renderBrand(gpu, ui.gpu, 'GPU');
  renderGauge(gpu, s.gpu.tempC, s.gpu.loadPct);
  setText($('.clock', gpu), withUnit(s.gpu.clockMHz, 0, ' MHz'));
  setText($('.hot', gpu), withUnit(s.gpu.hotspotC, 0, ' °C'));
  setText($('.power', gpu), withUnit(s.gpu.powerW, 1, ' W'));

  push(hist.cpu, s.cpu.loadPct);
  push(hist.gpu, s.gpu.loadPct);
  spark($('#loadSpark'), [{ data: hist.gpu, cls: 's2' }, { data: hist.cpu, cls: 's1' }], 100);

  const mem = ui.memory || {};
  setLogo($('#mem .logo'), mem.logo);
  meter('#ram', s.ram.usedMB, s.ram.totalMB, `RAM · ${mem.ramType || ''}${isNum(s.ram.speedMTs) ? ' ' + s.ram.speedMTs.toFixed(0) : ''}`);
  meter('#vram', s.gpu.vramUsedMB, s.gpu.vramTotalMB, `VRAM · ${mem.vramType || ''}`);

  renderFans(s.fans || []);

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
renderClock(null);
