# paxpanel — design

Date: 2026-10-07
Status: approved in brainstorming, awaiting spec review

## 1. Purpose

paxpanel is a standalone Windows sensor panel for a 400×1280 mini-monitor. It
replaces the "Cronoghirian" AIDA64 SensorPanel, which runs on AIDA64 6.33
(March 2021). That version predates the Intel 14th-gen CPU in this PC, so it
cannot read CPU power or memory clock, and it can't be upgraded at no cost.
paxpanel must not depend on AIDA64 at all.

### Success criteria

- One program (`PaxPanel.exe` plus its `web/` folder) shows every reading
  listed in §4 on the mini-monitor and refreshes once per second.
- The panel covers the whole mini-monitor, Windows taskbar included, and
  leaves the other monitors untouched.
- It starts by itself at logon with the rights the sensor driver needs and no
  UAC prompt.
- Hardware names, logos, drives and fan labels can be changed by editing
  `config.json`, without recompiling.
- The code is backed up at https://github.com/federicogiorgi/paxpanel with a
  README that includes a real screenshot of the panel.

### Non-goals

- Supporting other people's hardware out of the box. The defaults target this
  PC; others can adapt `config.json`.
- A settings UI, themes, an alerts/OSD/logging system or remote access.
- Writing a kernel driver. paxpanel only uses an existing signed one (PawnIO).

## 2. Target machine

| Part | Detail |
|---|---|
| CPU | Intel Core i9-14900KS (24 cores / 32 threads) |
| GPU | PNY NVIDIA GeForce RTX 4090, 24 GB GDDR6X (PCI subsystem `196E` = PNY) |
| RAM | 4 × 32 GB Corsair Vengeance DDR5 5600 (CMK128GX5M4B5600C40), currently configured at 4000 MT/s |
| Motherboard | Gigabyte Z790 AORUS ELITE AX |
| NIC | Realtek Gaming 2.5GbE (link 1 Gbps), ISP Fastweb |
| Mini-monitor | `ZL400X1280`, 400×1280 @ 60 Hz, physical position (−4240, 0); Windows display scaling 150 % |
| OS | Windows 11 Pro |

Drives (letter → volume label → physical disk):

| Letter | Label | Disk | Size |
|---|---|---|---|
| C: | SYSTEM | Samsung SSD 990 PRO 2TB (NVMe) | 1862 GB |
| D: | DRIVE | Samsung SSD 9100 PRO 8TB (NVMe) | 7452 GB |
| E: | FAST | Samsung SSD 990 PRO 2TB (NVMe) | 1863 GB |
| F: | DATA | WDC WD4005FZBX (SATA HDD) | 3726 GB |
| G: | QBIT | WDC WD4005FZBX (SATA HDD) | 3726 GB |
| O: | OLD | WDC WD4005FZBX (SATA HDD) | 3726 GB |

## 3. Architecture

A single x64 .NET 8 process with two halves that communicate only through a
JSON snapshot:

```
┌──────────────────────── PaxPanel.exe (elevated) ────────────────────────┐
│  HardwareSource (LibreHardwareMonitorLib + PawnIO)                       │
│  SystemSource   (Win32/.NET: RAM, DriveInfo, NIC counters)   ──► Snapshot │
│                                                         1 Hz    │ JSON   │
│  PanelWindow (borderless WinForms + WebView2) ◄─────────────────┘        │
│        └─ renders web/index.html  (panel.css, panel.js, assets/)         │
└──────────────────────────────────────────────────────────────────────────┘
```

### Repository layout

```
paxpanel/
├─ src/PaxPanel/
│  ├─ PaxPanel.csproj          net8.0-windows, WinForms, x64
│  ├─ Program.cs               arguments, single-instance mutex, logging, main loop
│  ├─ Config.cs                config.json model + loader + validation
│  ├─ Sensors/
│  │  ├─ HardwareSource.cs     LibreHardwareMonitorLib wrapper
│  │  ├─ SystemSource.cs       RAM, drive space, extra-drive discovery, NIC rates
│  │  ├─ DiskMapper.cs         drive letter → physical disk → temperature sensor
│  │  └─ Snapshot.cs           snapshot records + JSON serialisation
│  └─ PanelWindow.cs           monitor selection, borderless window, WebView2 host, context menu
├─ tests/PaxPanel.Tests/       xUnit tests for the hardware-free logic
├─ web/
│  ├─ index.html · panel.css · panel.js
│  ├─ mock.js                  fake snapshot generator for browser preview
│  └─ assets/                  geforce_light.otf, geforce_bold.otf, logos (PNG)
├─ config.json
├─ scripts/install.ps1 · scripts/uninstall.ps1
├─ docs/screenshot.png
├─ README.md · LICENSE (MIT) · .gitignore
```

### Data flow

1. A timer fires every `refreshMs` (default 1000 ms).
2. `HardwareSource.Update()` refreshes the LibreHardwareMonitor `Computer`
   (CPU, GPU, Motherboard, Memory, Storage enabled). `SystemSource` reads RAM,
   drive space and NIC byte counters.
3. The values are combined into a `Snapshot` and serialised with
   System.Text.Json (camelCase).
4. The snapshot goes to the page with `CoreWebView2.PostWebMessageAsJson`.
   `panel.js` updates the DOM and appends to 60-sample history buffers for the
   sparklines.

Sensor reads run off the UI thread, and only the post happens on it. If an
update takes longer than the interval, the next tick is skipped rather than
queued.

### Snapshot shape (contract between C# and the page)

```json
{
  "time": "2026-10-07T18:55:03",
  "cpu":  { "name": "i9-14900KS", "tempC": 42, "loadPct": 4, "clockMHz": 3190, "voltV": 1.116, "powerW": 38.4 },
  "gpu":  { "name": "RTX 4090", "tempC": 36, "hotspotC": 45, "loadPct": 3, "clockMHz": 210, "powerW": 46.3,
            "vramUsedMB": 3300, "vramTotalMB": 24564 },
  "ram":  { "usedMB": 23300, "totalMB": 131072, "speedMTs": 4000 },
  "fans": [ { "label": "CPU", "rpm": 1259 } ],
  "drives": [ { "letter": "C", "label": "SYSTEM", "usedGB": 355, "totalGB": 1862, "tempC": 40, "extra": false } ],
  "moreDrives": 0,
  "net":  { "upBps": 12000, "downBps": 148000, "linkMbps": 1000 },
  "warnings": [ "PawnIO driver not found: CPU and fan sensors unavailable" ]
}
```

Every numeric field can be `null`, meaning "not available"; the page draws
"–" for it. `warnings` is shown as one small line at the bottom of the panel
when it isn't empty.

## 4. Panel layout ("Twin gauges")

Canvas 400×1280 CSS px (mapped 1:1 to device pixels, see §7), background `#1f1f1f`, GeForce
font (light/bold), red `#ff0000` titles, grey `#808080` subtitles, white
values. From top to bottom:

1. **Header**: `HH:mm` on the left, `ddd dd-MMM-yy` on the right, grey, with
   a thin red rule under them.
2. **CPU | GPU**, two equal columns. Each has: a red title with its logo, the
   model subtitle from config, and a 270° arc gauge. The arc fill is load %,
   and the centre shows temperature °C in large type with the "°" in red.
   Below the gauge are key/value rows:
   - CPU: Clock (GHz), Volt (V), Power (W)
   - GPU: Clock (MHz), Hot spot (°C), Power (W)
3. **Load sparkline** across the full width: 60 s of CPU load (red) and GPU
   load (dim red), scaled 0–100 %.
4. **MEMORY** (Corsair logo): a RAM bar ("used / 128 GB", subtitle
   "DDR5 · {speed}") and a VRAM bar ("used / 24 GB", subtitle "GDDR6X").
   Each bar has a red gradient fill with the % at its right end.
5. **FANS**: up to 4 cells in one row, each showing RPM with a small label.
   Which motherboard/GPU sensor feeds each cell is set in config (see §6).
6. **STORAGE** (Samsung + WD logos): one row per configured drive (label,
   thin usage bar, "N GB free", temperature in red). Below that, up to **3**
   extra rows for drives not in config (USB sticks, external disks), sorted
   by letter. Each extra row is tagged with a small red "USB" when removable,
   and its temperature cell is blank if there's no reading. If there are more
   than 3 extra drives, a "+N more" line is shown.
7. **NETWORK** (Fastweb logo): ↑ upload and ↓ download in auto units
   (B/s, KB/s, MB/s), plus a 60 s sparkline of download (red) and upload
   (dim red), auto-scaled.

The reference mockup is `.superpowers/brainstorm/*/content/layout-v2.html`
(local only). It fits all sections, six drives, two extra drives and the
network block inside 1280 px.

## 5. Sensor sources

| Value | Source |
|---|---|
| CPU temp | LHM CPU sensor "CPU Package" (Temperature) |
| CPU load | LHM CPU "CPU Total" (Load) |
| CPU clock | Mean of the LHM CPU "Core #n" clocks (P- and E-cores) |
| CPU volt | LHM CPU "Core (SVID)" or "Vcore" from SuperIO (first available) |
| CPU power | LHM CPU "CPU Package" (Power) |
| GPU temp / hot spot / load / clock / power | LHM NVIDIA GPU sensors ("GPU Core", "GPU Hot Spot", "GPU Core" load, "GPU Core" clock, "GPU Package" power) |
| VRAM used / total | LHM NVIDIA "GPU Memory Used" / "GPU Memory Total" (SmallData) |
| RAM used / total | `GlobalMemoryStatusEx` |
| RAM speed | `Win32_PhysicalMemory.ConfiguredClockSpeed`, read once at start-up |
| Fans | LHM SuperIO (ITE chip on the Gigabyte board) fan sensors and GPU fan, chosen per config |
| Drive space | `System.IO.DriveInfo` (Fixed and Removable drives that are ready) |
| Drive temp | LHM Storage temperature, matched to the letter by `DiskMapper` |
| Net rates / link | `NetworkInterface.GetIPv4Statistics()` byte deltas / `Speed` |

The exact LHM sensor names must be confirmed on this machine with
`--dump-sensors` (§7) before they're hard-coded as defaults. Lookups are by
hardware type + sensor type + name, with a fallback to the first sensor of
the right type.

**DiskMapper:** for each drive letter, WMI
(`MSFT_Partition` → `DiskNumber`, `MSFT_PhysicalDisk` → `FriendlyName`,
`SerialNumber`) gives the physical disk. That is matched to the LHM storage
hardware whose name or serial matches, so temperatures follow the disk even
if Windows renumbers disks. This replaces AIDA64's opaque `THDDn` numbering.

**Driver:** LibreHardwareMonitorLib uses the PawnIO driver for CPU MSR and
SuperIO access. It has to be installed and paxpanel has to run elevated. How
the library expects PawnIO to be present (bundled vs a separate PawnIO
install) is confirmed during planning. If the driver is missing or paxpanel
isn't elevated, the affected values become `null` and a warning is added.
Nothing crashes.

## 6. Configuration (`config.json`)

```json
{
  "refreshMs": 1000,
  "monitor": { "width": 400, "height": 1280, "deviceName": null },
  "cpu":    { "title": "CPU", "subtitle": "i9-14900KS", "logo": "assets/INTELLOGO.png" },
  "gpu":    { "title": "GPU", "subtitle": "RTX 4090",   "logo": "assets/PNYLOGO.png" },
  "memory": { "logo": "assets/CORSAIRLOGO.png", "ramType": "DDR5", "vramType": "GDDR6X" },
  "fans": [
    { "label": "CPU",  "match": "Fan #1" },
    { "label": "PUMP", "match": "Fan #2" },
    { "label": "SYS",  "match": "Fan #3" },
    { "label": "GPU",  "match": "GPU Fan" }
  ],
  "drives": [
    { "letter": "C", "label": "SYSTEM" }, { "letter": "D", "label": "DRIVE" },
    { "letter": "E", "label": "FAST" },   { "letter": "F", "label": "DATA" },
    { "letter": "G", "label": "QBIT" },   { "letter": "O", "label": "OLD" }
  ],
  "maxExtraDrives": 3,
  "storageLogos": [ "assets/SAMSUNGLOGO.png", "assets/WDLOGO.png" ],
  "network": { "adapter": "Realtek Gaming 2.5GbE", "logo": "assets/FASTWEBLOGO.png" }
}
```

- `monitor.deviceName` (for example `\\.\DISPLAY4`) overrides matching by
  size when set.
- Fan `match` values are placeholders until `--dump-sensors` shows the real
  header names. `match` is a case-insensitive substring of the LHM sensor
  name.
- `network.adapter` is a case-insensitive substring of the adapter
  description. If nothing matches, the first adapter that is up, not virtual
  and not loopback is used.
- An invalid or missing config file falls back to built-in defaults (the
  values above) and adds a warning.

## 7. Program behaviour

**Command line**
- (no args): run the panel.
- `--dump-sensors`: print every LHM hardware item and sensor (type, name,
  value), every drive → disk mapping and every NIC to stdout, then exit.
- `--screenshot <path>`: start, wait for the first full render, save a
  400×1280 PNG with `CoreWebView2.CapturePreviewAsync`, then exit.
- `--windowed`: a normal movable window on the primary monitor, for
  debugging.

**Window**
- Borderless, `TopMost`, `ShowInTaskbar = false`, and tool-window style so it
  has no Alt-Tab entry. Bounds equal the target monitor's full `Bounds` in
  physical pixels. The process is per-monitor-DPI aware (PerMonitorV2), and
  WebView2's zoom is set so 1 CSS px = 1 device px on that monitor.
- Monitor selection: `deviceName` if set, otherwise the first screen whose
  physical size is exactly `monitor.width` × `monitor.height` (400×1280,
  portrait). If none is found, the window stays hidden and is checked
  again on `SystemEvents.DisplaySettingsChanged` and every 10 s.
- Right-click menu: Reload page · Save screenshot… · Exit.
- Single instance via a named mutex `Global\paxpanel`. A second launch exits
  immediately.

**Autostart**
- `scripts/install.ps1` (run as admin) creates the scheduled task `paxpanel`:
  trigger *At log on* of the current user, *Run with highest privileges*,
  action `PaxPanel.exe` in the install folder, no time limit, restart on
  failure 3 times. `uninstall.ps1` removes the task and stops the process.

**Logging**
- Errors and warnings go to `%LOCALAPPDATA%\paxpanel\paxpanel.log`, rolled at
  1 MB with one previous file kept.

## 8. Error handling

| Situation | Behaviour |
|---|---|
| A sensor isn't found or throws | That field is `null` and shows "–"; logged once |
| PawnIO missing / not elevated | CPU/fan/SuperIO values `null`; warning line on the panel |
| A drive in config isn't mounted | Row shows label + "not mounted" in grey |
| Mini-monitor disconnected | Window hidden; restored when it reappears |
| WebView2 runtime missing | Message box with the download link, then exit |
| Bad `config.json` | Built-in defaults + warning line |
| Unhandled exception in the update loop | Logged; the loop continues on the next tick |

## 9. Testing

- **Unit tests (xUnit, no hardware):** config parsing/defaults/validation;
  monitor selection from a list of fake screens; DiskMapper matching (by
  serial, by name, and when nothing matches); extra-drive discovery and the
  `maxExtraDrives` / `moreDrives` cut-off; NIC rate calculation from two
  counter samples, including counter reset; snapshot JSON shape with `null`s.
- **Page (browser, mock data):** `web/index.html?mock` runs on `mock.js`.
  Check at 400×1280 in a browser: normal values, every value `null`, 3 and 5
  extra drives, very long labels, 0 % and 100 % loads, multi-MB/s network.
  Nothing may overflow the 1280 px height or the 400 px width.
- **On the machine:** `--dump-sensors` output reviewed to fix sensor names
  and fan mapping; the panel runs on the mini-monitor and values are compared
  with Task Manager, `nvidia-smi` and Explorer free space; plug and unplug a
  USB stick; disconnect and reconnect the monitor; reboot to confirm
  autostart.

## 10. GitHub and README

- Repo: https://github.com/federicogiorgi/paxpanel, branch `main`.
- Commits and pushes are authored only by the repository owner's git
  identity, with no third-party attribution in commits, descriptions or
  files.
- `README.md` contains:
  - a one-paragraph description (an AIDA64-free sensor panel for a 400×1280
    mini-monitor, in the spirit of the "Cronoghirian" AIDA64 SensorPanel);
  - `docs/screenshot.png` (made with `--screenshot` on real hardware);
  - requirements (Windows 10/11 x64, .NET 8 Desktop Runtime, WebView2
    Runtime, PawnIO);
  - build, install and uninstall steps, and a `config.json` overview;
  - a **Thanks** section: *Andrea "Pax" Paci, for the original idea of
    buying a mini-monitor, which then forced the writing of a sensor panel to
    use it.*
- License: MIT. The GeForce font and third-party logos are kept for personal
  use; the README notes that they belong to their respective owners.
- `.gitignore`: `bin/`, `obj/`, `*.user`, `.vs/`, `*.log`, `.superpowers/`.

## 11. One-time prerequisites (installed only with the owner's approval)

- .NET 8 SDK (via `winget install Microsoft.DotNet.SDK.8`) to build.
- PawnIO driver, from its official distribution.
- WebView2 Runtime (already part of Windows 11; verify).
