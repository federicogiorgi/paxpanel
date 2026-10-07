'use strict';
// Fake snapshots for previewing the page in a normal browser:
// index.html?mock[=nulls|usb3|usb5|long|max|idle|v1|sweep]  (sweep: CPU temp walks 25 -> 100 -> 25 °C)
(function () {
  const mode = new URLSearchParams(location.search).get('mock') || 'normal';
  // &still: no CSS transitions, so a screenshot shows final bar and gauge sizes
  if (new URLSearchParams(location.search).has('still')) {
    const st = document.createElement('style');
    st.textContent = '* { transition: none !important; }';
    document.head.appendChild(st);
  }
  let seed = 7;
  const rnd = () => (seed = (seed * 9301 + 49297) % 233280) / 233280;
  const wob = (base, amp) => Math.max(0, base + (rnd() - 0.5) * 2 * amp);
  const MB = 1048576;
  const ui = {
    cpu: { title: 'CPU', subtitle: 'i9-14900KS', logo: 'assets/INTELLOGO.png' },
    gpu: { title: 'GPU', subtitle: 'RTX 4090', logo: 'assets/PNYLOGO.png' },
    memory: { logo: 'assets/CORSAIRLOGO.png', ramType: 'DDR5', vramType: 'GDDR6X' },
    storageLogos: ['assets/SAMSUNGLOGO.png', 'assets/WDLOGO.png'],
    netLogo: 'assets/FASTWEBLOGO.png',
  };

  function localIso() {
    const now = new Date();
    return new Date(now.getTime() - now.getTimezoneOffset() * 60000).toISOString().slice(0, 19);
  }

  function cores() {
    const mixed = [92, 85, 100, 40, 12, 66, 8, 30];
    return Array.from({ length: 24 }, (_, i) => {
      const p = i < 8;
      const base = mode === 'max' ? 100 : mode === 'idle' ? 2 : p ? mixed[i] : (i % 5 === 0 ? 70 : 6 + (i % 4) * 7);
      const load = Math.min(100, wob(base, mode === 'max' ? 0 : 6));
      return { name: p ? `P${i + 1}` : `E${i - 7}`, performance: p, loadPct: mode === 'nulls' ? null : load,
               tempC: mode === 'nulls' ? null : Math.round((p ? 34 : 32) + load * (p ? 0.5 : 0.4) + rnd() * 3) };
    });
  }

  function drives() {
    const base = [['C', 'SYSTEM', 1862, 1503, 47, 97, 2.0, 1.0], ['D', 'DRIVE', 7452, 7165, 42, 100, 0, 0],
                  ['E', 'FAST', 1863, 1585, 46, 97, 0.1, 0], ['F', 'DATA', 3726, 2992, 48, null, 0, 0],
                  ['G', 'QBIT', 3726, 3041, 47, null, 0, 5.3], ['O', 'OLD', 3726, 3085, 51, null, 0, 0]]
      .map(([letter, label, total, free, temp, life, r, w]) => ({ letter, label, mounted: true, removable: false, extra: false,
        usedGB: total - free, totalGB: total, tempC: temp, readBps: r * MB * (0.5 + rnd()), writeBps: w * MB * (0.5 + rnd()), lifePct: life }));
    const usb = n => Array.from({ length: n }, (_, i) => ({
      letter: String.fromCharCode(72 + i), label: i % 2 ? 'BACKUP' : 'STICK', mounted: true, removable: i % 2 === 0,
      extra: true, usedGB: 20 + i * 100, totalGB: 64 + i * 400, tempC: i % 2 ? 31 : null, readBps: null, writeBps: null, lifePct: null }));
    if (mode === 'usb3') return { list: base.concat(usb(3)), more: 0 };
    if (mode === 'usb5') return { list: base.concat(usb(3)), more: 2 };
    if (mode === 'long') {
      base[1].label = 'DROPBOX AND EVERYTHING ELSE TOO';
      return { list: base.concat([{ ...usb(1)[0], label: 'A VERY LONG USB STICK NAME' }]), more: 0 };
    }
    if (mode === 'nulls') {
      base.forEach(d => { d.tempC = null; d.readBps = null; d.writeBps = null; d.lifePct = null; });
      base[5] = { ...base[5], mounted: false, usedGB: null, totalGB: null };
    }
    return { list: base, more: 0 };
  }

  let tick = 0;
  function snapshot() {
    tick++;
    const nulls = mode === 'nulls', max = mode === 'max', idle = mode === 'idle';
    const v = x => (nulls ? null : x);
    const d = drives();
    const c = cores();
    const avg = c.reduce((s, x) => s + (x.loadPct || 0) * (x.performance ? 2 : 1), 0) / 32;
    const gpuLoad = max ? 100 : idle ? 1 : wob(18, 4);
    const snap = {
      time: localIso(),
      ui,
      cpu: { tempC: v(max ? 99 : Math.round(36 + avg * 0.45)), loadPct: v(avg), clockMHz: v(5000),
             voltV: v(0.9 + avg / 100 * 0.47), powerW: v(max ? 320 : 25 + avg * 2.5),
             pClockMHz: v(idle ? 1400 : 5480), eClockMHz: v(idle ? 980 : 4280), cores: c },
      gpu: { tempC: v(Math.round(34 + gpuLoad * 0.4)), hotspotC: v(Math.round(44 + gpuLoad * 0.45)), loadPct: v(gpuLoad),
             clockMHz: v(gpuLoad < 5 ? 210 : 2520), powerW: v(30 + gpuLoad * 4.2), vramUsedMB: v(max ? 24564 : 3600), vramTotalMB: v(24564),
             memJunctionC: v(Math.round(44 + gpuLoad * 0.35)), powerPct: v((30 + gpuLoad * 4.2) / 4.5),
             pcieRxBps: v(wob(29, 10) * MB), pcieTxBps: v(wob(9, 4) * MB) },
      ram: { usedMB: max ? 131072 : wob(76700, 200), totalMB: 131072, speedMTs: v(4000) },
      fans: [{ label: 'CPU', rpm: v(idle ? 820 : Math.round(600 + avg / 100 * 1600)), maxRpm: v(2213), dutyPct: null },
             { label: 'GPU', rpm: v(gpuLoad < 5 ? 0 : Math.round(900 + gpuLoad / 100 * 1700)), maxRpm: v(3343), dutyPct: v(30) }],
      drives: d.list,
      moreDrives: d.more,
      net: { upBps: max ? 120 * MB : wob(12000, 9000), downBps: max ? 950 * MB : wob(148000, 120000), linkMbps: 1000 },
      warnings: nulls ? ['PawnIO driver not found: CPU and fan sensors unavailable'] : [],
      board: [['SYS', 34], ['PCH', 46], ['CPU', 78], ['PCIe', 39], ['VRM', 51], ['SYS2', 37]].map(([label, t]) => ({ label, tempC: v(t) })),
      sys: { uptimeSec: 3 * 86400 + 7 * 3600 + 1234,
             top: nulls ? [] : [{ name: 'vmmemWSL', cpuPct: wob(88, 3) }, { name: 'chrome', cpuPct: wob(4.4, 1) }, { name: 'explorer', cpuPct: 1.2 }],
             topGpu: nulls || idle ? [] : [{ name: 'blender', cpuPct: wob(64, 5) }, { name: 'dwm', cpuPct: wob(3, 1) }, { name: 'chrome', cpuPct: 1.4 }] },
    };
    const fixedTemp = new URLSearchParams(location.search).get('temp');
    if (fixedTemp !== null) snap.cpu.tempC = Number(fixedTemp);
    if (mode === 'sweep') {
      const phase = (tick % 40) / 40;   // 40 s round trip
      snap.cpu.tempC = Math.round(25 + 75 * (phase < 0.5 ? phase * 2 : 2 - phase * 2));
    }
    if (mode === 'v1') {
      // a v1 snapshot: none of the v2 fields exist
      delete snap.board; delete snap.sys;
      for (const k of ['pClockMHz', 'eClockMHz', 'cores']) delete snap.cpu[k];
      for (const k of ['memJunctionC', 'powerPct', 'pcieRxBps', 'pcieTxBps']) delete snap.gpu[k];
      snap.fans.forEach(f => { delete f.maxRpm; delete f.dutyPct; });
      snap.drives.forEach(x => { delete x.readBps; delete x.writeBps; delete x.lifePct; });
    }
    return snap;
  }

  window.paxRender(snapshot());
  setInterval(() => window.paxRender(snapshot()), 1000);
})();
