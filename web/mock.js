'use strict';
// Fake snapshots for previewing the page in a normal browser: index.html?mock[=nulls|usb3|usb5|long|max]
(function () {
  const mode = new URLSearchParams(location.search).get('mock') || 'normal';
  let seed = 7;
  const rnd = () => (seed = (seed * 9301 + 49297) % 233280) / 233280;
  const wob = (base, amp) => Math.max(0, base + (rnd() - 0.5) * 2 * amp);
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

  function drives() {
    const base = [['C', 'SYSTEM', 1862, 1507, 40], ['D', 'DRIVE', 7452, 7181, 43], ['E', 'FAST', 1863, 1585, 44],
                  ['F', 'DATA', 3726, 2992, 39], ['G', 'QBIT', 3726, 3041, 38], ['O', 'OLD', 3726, 3085, 37]]
      .map(([letter, label, total, free, temp]) => ({ letter, label, mounted: true, removable: false, extra: false,
                                                      usedGB: total - free, totalGB: total, tempC: temp }));
    const usb = n => Array.from({ length: n }, (_, i) => ({
      letter: String.fromCharCode(72 + i), label: i % 2 ? 'BACKUP' : 'STICK', mounted: true, removable: i % 2 === 0,
      extra: true, usedGB: 20 + i * 100, totalGB: 64 + i * 400, tempC: i % 2 ? 31 : null }));
    if (mode === 'usb3') return { list: base.concat(usb(3)), more: 0 };
    if (mode === 'usb5') return { list: base.concat(usb(3)), more: 2 };
    if (mode === 'long') {
      base[1].label = 'DROPBOX AND EVERYTHING ELSE TOO';
      return { list: base.concat([{ ...usb(1)[0], label: 'A VERY LONG USB STICK NAME' }]), more: 0 };
    }
    if (mode === 'nulls') {
      base.forEach(d => { d.tempC = null; });
      base[5] = { ...base[5], mounted: false, usedGB: null, totalGB: null };
    }
    return { list: base, more: 0 };
  }

  function snapshot() {
    const nulls = mode === 'nulls', max = mode === 'max';
    const v = x => (nulls ? null : x);
    const d = drives();
    return {
      time: localIso(),
      ui,
      cpu: { tempC: v(max ? 99 : wob(42, 3)), loadPct: v(max ? 100 : wob(8, 7)), clockMHz: v(wob(3200, 400)),
             voltV: v(wob(1.12, 0.03)), powerW: v(max ? 320 : wob(40, 10)) },
      gpu: { tempC: v(max ? 88 : wob(36, 2)), hotspotC: v(wob(45, 2)), loadPct: v(max ? 100 : wob(4, 4)),
             clockMHz: v(wob(400, 200)), powerW: v(max ? 450 : wob(46, 5)), vramUsedMB: v(max ? 24564 : 3300), vramTotalMB: v(24564) },
      ram: { usedMB: max ? 131072 : wob(23300, 200), totalMB: 131072, speedMTs: v(4000) },
      fans: [{ label: 'CPU', rpm: v(wob(1259, 20)) }, { label: 'PUMP', rpm: v(wob(1840, 20)) },
             { label: 'SYS', rpm: v(wob(890, 10)) }, { label: 'GPU', rpm: v(max ? 3000 : 0) }],
      drives: d.list,
      moreDrives: d.more,
      net: { upBps: max ? 120 * 1048576 : wob(12000, 9000), downBps: max ? 950 * 1048576 : wob(148000, 120000), linkMbps: 1000 },
      warnings: nulls ? ['PawnIO driver not found: CPU and fan sensors unavailable'] : [],
    };
  }

  window.paxRender(snapshot());
  setInterval(() => window.paxRender(snapshot()), 1000);
})();
