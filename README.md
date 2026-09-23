# MYU STATS

A macOS system monitor that lives on the edge of the screen. A glass pill on any edge shows one ring
per metric — CPU, memory, network, disk, thermals, battery. Hover a ring for a detail card; open
**Details** for a full page in Settings. Inspired by Codenotch's edge layout.

Personal project — not affiliated with Codenotch or Apple.

## 1. Overview

| Area | What you get |
|------|--------------|
| Edge pill | One ring + value per metric, welded to the screen edge with flared ends. Left, right, top (under the menu bar) or bottom. |
| Auto-hide | Only a thin tab stays on the edge; touch the edge next to it and the pill unfolds, rings staggering in. |
| Hover cards | Glass card with a tail pointing at its ring; glides between rings; **Details** button at the foot. |
| Arc buttons | In the pockets of the pill's two flares: **move handle** (drag along the edge or onto another edge) and **settings orb** (gear, spins on click). |
| Settings | Codenotch-style dark window: Metrics, Appearance, General, plus a **Details** page per metric. |
| Disk analysis | "What's using space": home folder + Applications largest first, drill into any folder, developer caches with the command that clears them. |
| Fan control | Auto / Manual rpm / Max per fan, through the macOS administrator prompt. |

Details pages show what does not fit in a card: per-core load, 40-sample history charts, the top 10
processes, every mounted volume, every sensor group and fan, battery health and cycles.

## 2. Tech stack

- Swift (5 language mode), SwiftUI for views, AppKit for panels, windows and the status item.
- Swift Package Manager — no Xcode project. Swift Testing for tests.
- macOS 14 Sonoma or later; Liquid Glass (`glassEffect`) on macOS 26+, `NSVisualEffectView` before that.
- No third-party dependencies.

## 3. Architecture

```
Sources/
├── MYUStats/            the app
│   ├── App/             main.swift, AppDelegate (status item, quit guard), Settings (keys, enums, migration)
│   ├── Metrics/         samplers, StatsStore (timer + @Published state), StorageAnalyzer, FanController
│   ├── Edge/            EdgePanel, EdgeController (placement, hover, drag), EdgeLayout (geometry),
│   │                    EdgeView, DetailCards, Shapes, GlassBackground, DropZoneOverlay, Components
│   └── Settings/        SettingsView (sidebar + panes), MetricDetailPane, SettingsStyle,
│                        SettingsWindowController
├── SMCKit/              SMC client (read + write) and FanControl — shared by the app and the helper
└── myustats-fan/        privileged fan helper (bundled as Contents/MacOS/myustats-fan)
Tests/MYUStatsTests/     geometry, placement, formatting, settings, storage, fan command tests
```

- `StatsStore` samples on a timer (1 / 2 / 5 s) and publishes values; views observe it. Disk, battery
  and interface are read every 5 samples, thermals every 2. Process scanning runs only while a card or
  Details page needs it (`setProcessSampling(_:for:)`).
- `EdgeLayout` is the single source of geometry. It works in edge-relative terms (*along* / *across*
  the edge) and maps to panel coordinates in one place, so all four edges share one set of rules.
  Shapes are drawn for the right edge and mapped with `EdgeTransform`. Screen placement, clamping and
  drop-target selection are pure functions here, so they are unit-tested without a screen.
- `EdgeController` polls the cursor every 50 ms and hit-tests against `EdgeLayout`. The panel ignores
  the mouse except over the orb, the move handle and the Details button, so it never steals clicks.
- Settings navigation is a `SettingsNavigation` object, so a card's Details button can open Settings
  on that metric's page.

## 4. Setup and run

Requirements: Xcode command-line tools (`xcode-select --install`).

```sh
make run       # build "build/MYU STATS.app" and launch it
make install   # build, copy to /Applications and launch
make test      # swift test
make clean     # remove build artefacts
```

For development in Xcode: `open Package.swift`, then run the `MYUStats` scheme.

Settings open from the gear orb under the pill, the status-bar gauge icon → **Settings…** (⌘,), a
card's **Details** button, or by launching the app again from Finder/Spotlight.

| Pane | Options |
|------|---------|
| Metrics | Show/hide and reorder rings, update interval (1 / 2 / 5 s) |
| Appearance | Edge (left / right / top / bottom), re-centre, surface (Liquid Glass / Dark glass / Solid black), darkness, auto-hide, hide delay |
| General | Launch at login, top-process scanning, reset to defaults |
| Details → CPU … Battery | Full page per metric |

All options are `UserDefaults` keys (`SettingsKey`); the pill re-lays itself out as soon as one changes.

## 5. Data sources and permissions

| Metric | Source |
|--------|--------|
| CPU | `host_statistics(HOST_CPU_LOAD_INFO)`, per core `host_processor_info`, `getloadavg`, `hw.perflevel*` sysctls |
| Memory | `host_statistics64(HOST_VM_INFO64)`; swap `vm.swapusage`; pressure `kern.memorystatus_vm_pressure_level` |
| Processes | `proc_listallpids` + `proc_pid_rusage` — current user's processes only |
| Network | `sysctl(NET_RT_IFLIST2)` 64-bit counters on `en*` / `pdp_ip*` (VPN tunnels excluded); address via `getifaddrs` |
| Disk | `volumeAvailableCapacityForImportantUsage`; other volumes via `mountedVolumeURLs`; folder sizes by walking files (`totalFileAllocatedSize`) |
| Battery | IOKit power sources + `AppleSmartBattery` registry (`BatteryData`) for health and cycles |
| Thermals and fans | SMC (`SMCKit`): sensor keys discovered by prefix — `Tp*`/`Te*` CPU, `Tg*` GPU, `TB*T` battery; fans `FNum`, `F<n>Ac/Mn/Mx/Md/Tg` |

Permissions macOS may ask for:

- **Files** — the first disk analysis touches Documents, Downloads, Desktop and other apps' data.
  Declining just leaves those folders unmeasured.
- **Administrator** — every fan-control change (password or Touch ID).

### Fan control

- `myustats-fan` accepts only `auto|max <fan|all>` or `manual <fan|all> <rpm>`, refuses to run unless
  root, and clamps speeds to each fan's min/max. It does one change and exits.
- The app runs it with `do shell script … with administrator privileges`; no privileged process stays behind.
- Manual raises `Ftst` (required on Apple silicon), sets `F<n>Md = 1` and `F<n>Tg`. Auto lowers the
  mode and `Ftst` so thermalmonitord takes over. Automatic reads as `0` or `3`; only `1` is manual.
- Quitting while a fan is manual offers to restore Auto first.

## 6. Build and distribution

`Scripts/bundle.sh` builds release binaries, assembles `build/MYU STATS.app` with
`Resources/Info.plist` (`LSUIElement` — no Dock icon; bundle ID `com.muhammadyunusxon.myustats`),
copies the fan helper into `Contents/MacOS`, and ad-hoc signs the helper, then the bundle.

An ad-hoc build runs on the machine that built it. To share it, sign with a Developer ID certificate
(helper first, then the app, with the hardened runtime) and notarize.

**Rename:** the app was called NotchStats (`com.muhammadyunusxon.notchstats`). On first launch MYU STATS
copies the old saved settings once (`migrateLegacySettings`). Launch at login must be switched on again.

## Tests

```sh
make test
```

Swift Testing suites in `Tests/MYUStatsTests` (49 tests):

- `EdgeLayoutTests` — all four edges: mapping, pill and cells, reveal trigger, card and tail, arc
  buttons (position, hit areas, direction), Details button placement.
- `PlacementTests` — screen placement and clamping, offset round-trip, drop-target selection.
- `ModelAndFormatTests` — formatters, derived values, settings defaults, ring-order and rename migration.
- `StorageAnalyzerTests` — folder and file sizes on real temporary files.
- `FanControlTests` — helper command parsing (including rejected input), SMC value encoding, admin-prompt quoting.

## Performance notes

- Idle cost is about 2–4 % of one core. Per-second value changes are not animated: animating the
  rings kept SwiftUI re-laying out the panel for most of every second (~8 % CPU).
- Process scanning adds a few percent while a CPU or Memory card or page is open.
- Disk analysis walks every file, so large folders take seconds to a minute; it runs off the main
  thread, four folders at a time, and stops when you leave the page.

## Known limitations

- Thermals and fan control use the undocumented SMC interface; key names can change between chips and
  macOS releases. The Thermals ring hides itself when no sensors are found.
- Root-owned processes (WindowServer, kernel_task) are missing from process lists.
- Folder sizes count APFS clones and hard links once per path, so totals are approximate.
- The pill always uses the main display; there is no display picker yet, and it does not fold away
  for full-screen apps.
- Another edge app on the same edge (e.g. Codenotch) will overlap — use a different edge.
