# MYU STATS

A macOS system monitor that lives on the edge of the screen. A glass pill on any edge shows one ring
per metric — CPU, memory, network, disk, thermals, battery. Hover a ring for a detail card; open
**Details** for a full page in Settings. Inspired by Codenotch's edge layout.

Personal project — not affiliated with Codenotch or Apple.

## 1. Overview

| Area | What you get |
|------|--------------|
| Edge pill | One ring + value per metric, welded to the screen edge with flared ends. Left, right, top (under the menu bar) or bottom, on any display. |
| Auto-hide | Only a thin tab stays on the edge; touch the edge next to it and the pill unfolds, rings staggering in. |
| Hover cards | Glass card with a tail pointing at its ring; glides between rings; **Details** button at the foot. |
| Arc buttons | In the pockets of the pill's two flares: **move handle** (drag along the edge or onto another edge) and **settings orb** (gear, spins on click). |
| Settings | Codenotch-style dark window: Metrics, Appearance, General, plus a **Details** page per metric. |
| Disk analysis | "What's using space": home folder + Applications largest first, drill into any folder, developer caches with the command that clears them. |
| Fan control | Auto / Manual rpm / Max per fan. The password (or Touch ID) is asked once, to install the helper. |
| Languages | English and Uzbek (follows the macOS language order). |

Details pages show what does not fit in a card: per-core load, 40-sample history charts, the top 10
processes, every mounted volume, every sensor group and fan, battery health and cycles.

## 2. Tech stack

- Swift (5 language mode, clean under `-strict-concurrency=complete`), SwiftUI for views, AppKit for
  panels and windows. Observation (`@Observable`) for state.
- Swift Package Manager — no Xcode project. Swift Testing for tests. `os.Logger` for logging.
- String Catalog (`Resources/Localizable.xcstrings`) for localization.
- macOS 14 Sonoma or later; Liquid Glass (`glassEffect`) on macOS 26+, `NSVisualEffectView` before that.
- No third-party dependencies.

## 3. Architecture

```
Sources/
├── MYUStats/            the app
│   ├── App/             main.swift, AppDelegate (reopen, quit guard), Settings (keys, enums,
│   │                    migration, reset), Log (unified-log categories)
│   ├── Metrics/         samplers, SamplingEngine (actor, runs them off the main thread),
│   │                    StatsStore (timer + observable state), StorageAnalyzer, FanController
│   ├── Edge/            EdgePanel, EdgeController (placement, hover, drag), EdgeLayout (geometry),
│   │                    DisplayChoice, EdgeView, DetailCards, Shapes, GlassBackground, DropZoneOverlay,
│   │                    Components
│   └── Settings/        SettingsView (sidebar), SettingsStyle, SettingsWindowController
│       ├── Panes/       Metrics, Appearance, General
│       └── Details/     MetricDetailPane (shared rows) + one file per metric
├── SMCKit/              SMC client (read + write, `SMCAccess` protocol) and FanControl, shared by the
│                        app and the helper
└── myustats-fan/        privileged fan helper (bundled as Contents/MacOS/myustats-fan)
Resources/               Info.plist, AppIcon.icns, Localizable.xcstrings
Tests/MYUStatsTests/     geometry, placement, formatting, settings, store, samplers, storage, fans, catalog
.github/workflows/       CI: strict build, tests and bundle on macOS 26
```

- `StatsStore` ticks on a timer (1 / 2 / 5 s). Each tick asks a `StatsSource` for readings: in the app
  the `SamplingEngine` actor, which runs every sampler off the main thread; in tests a fake. Only the
  results are published on the main actor, and only values that changed, so each view re-renders when
  something it reads changes. Disk, battery and interface are read every 5 ticks, thermals every 2;
  SMC sensor discovery happens on the first thermal read. Process scanning runs only while a card or
  Details page needs it (`setProcessSampling(_:for:)`).
- `EdgeLayout` is the single source of geometry. It works in edge-relative terms (*along* / *across*
  the edge) and maps to panel coordinates in one place, so all four edges share one set of rules.
  Shapes are drawn for the right edge and mapped with `EdgeTransform`. Screen placement, clamping and
  drop-target selection are pure functions here, so they are unit-tested without a screen.
- `EdgeController` follows mouse moves (a global event monitor, which needs no accessibility
  permission, plus a local one) and hit-tests against `EdgeLayout`. It polls every 50 ms only while
  something can change without the mouse moving: a card or pill waiting to close, a drag, the pointer
  on a panel button, or Settings in front. The panel ignores the mouse except over the orb, the move
  handle and the Details button, so it never steals clicks.
- Only placement settings (edge, display, offset, rings, auto-hide) re-lay the panel out; other
  settings writes are ignored there.
- Settings navigation is a `SettingsNavigation` object, so a card's Details button can open Settings
  on that metric's page.

## 4. Setup and run

Requirements: Xcode command-line tools (`xcode-select --install`).

```sh
make run       # build "build/MYU STATS.app" and launch it
make install   # build, copy to /Applications and launch
make test      # swift test
make clean     # remove build artefacts
make icon      # regenerate Resources/AppIcon.icns from Scripts/make-icon.swift
make strings   # refresh Resources/Localizable.xcstrings from the source
```

Logs go to the unified log under `com.muhammadyunusxon.myustats` (categories `app`, `sampling`, `fans`,
`storage`, `settings`, `smc`):

```sh
log stream --level info --predicate 'subsystem == "com.muhammadyunusxon.myustats"'
```

### Localization

User-facing text is written in English in the code (`Text("…")`, `String(localized: "…")`) and
translated in `Resources/Localizable.xcstrings`. After adding or changing text, run `make strings`,
then translate the new entries (Xcode's catalog editor or the JSON). `bundle.sh` compiles the catalog
into `<language>.lproj` folders. A test fails while any string lacks an Uzbek translation or a
translation drops a format specifier. To try another language without changing the system:

```sh
open "build/MYU STATS.app" --args -AppleLanguages '(uz)'
```

For development in Xcode: `open Package.swift`, then run the `MYUStats` scheme.

There is no menu bar icon. Settings open from the gear orb under the pill, a card's **Details**
button, or by launching the app again from Finder/Spotlight. **Quit MYU STATS** sits at the foot of
the Settings sidebar.

| Pane | Options |
|------|---------|
| Metrics | Show/hide and reorder rings, update interval (1 / 2 / 5 s) |
| Appearance | Edge (left / right / top / bottom), display (with more than one), re-centre, surface (Liquid Glass / Dark glass / Solid black), darkness, auto-hide, hide delay |
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
- **Administrator** — once, the first time fans are changed, to install the fan helper (and again after
  an app update, since the helper must match the app). Removing the helper asks once more.

### Fan control

- `myustats-fan` accepts only `auto|max <fan|all>` or `manual <fan|all> <rpm>`, refuses to run unless
  its effective user is root, drops the caller's environment, and clamps speeds to each fan's
  min/max. It does one change and exits; no privileged process stays behind.
- The first change runs `do shell script … with administrator privileges` once. The helper file
  inside the bundle is writable by the user, so it is never run as root in place: the app checks its
  own signature (the bundle on disk must be the running app, with its seal intact) and reads the
  helper's code hash from it; the root shell copies the helper into a fresh root-only folder, checks
  the copy against that hash with `codesign --verify -R '=cdhash …'`, and installs it as
  `/Library/PrivilegedHelperTools/com.muhammadyunusxon.myustats.fan` (root:wheel, setuid, 4755).
- Later changes run the installed copy directly, with no prompt, but only while it is root-owned,
  writable by nobody else, validly signed and has the same code hash as the helper in this app. A new
  build installs its own helper once; anything else is refused and reinstalled.
- Any process of the logged-in user can run the installed helper, so it can change fan speeds (within
  each fan's range) without a password. That is the trade-off for asking once. **Thermals → Fan
  control → Remove** hands the fans back to macOS and deletes it.
- Changes are all or nothing: if one fan refuses, the fans taken over by that change go back to Auto
  and `Ftst` is lowered again.
- Manual raises `Ftst` (required on Apple silicon), sets `F<n>Md = 1` and `F<n>Tg`. Auto lowers the
  mode and `Ftst` so thermalmonitord takes over. Automatic reads as `0` or `3`; only `1` is manual.
- Quitting while a fan is manual offers to restore Auto first. If that is cancelled or fails, the app
  stays open (and says why), so fans are never left pinned by accident.

## 6. Build and distribution

`Scripts/bundle.sh` builds release binaries, assembles `build/MYU STATS.app` with
`Resources/Info.plist` and `Resources/AppIcon.icns` (`LSUIElement` — no Dock icon; bundle ID `com.muhammadyunusxon.myustats`),
compiles the string catalog into `Contents/Resources/<language>.lproj`, copies the fan helper into
`Contents/MacOS`, and ad-hoc signs the helper, then the bundle.

CI (`.github/workflows/test.yml`) builds with strict concurrency checking and warnings as errors, runs
the tests and assembles the bundle on every push to `main` and every pull request.

An ad-hoc build runs on the machine that built it. To share it, sign with a Developer ID certificate
(helper first, then the app, with the hardened runtime) and notarize.

**Rename:** the app was called NotchStats (`com.muhammadyunusxon.notchstats`). On first launch MYU STATS
copies the old saved settings once (`migrateLegacySettings`). Launch at login must be switched on again.

## Tests

```sh
make test
```

Swift Testing suites in `Tests/MYUStatsTests` (76 tests):

- `EdgeLayoutTests` — all four edges: mapping, pill and cells, reveal trigger, card and tail, arc
  buttons (position, hit areas, direction), Details button placement.
- `PlacementTests` — screen placement and clamping, offset round-trip, drop-target selection.
- `ModelAndFormatTests` — formatters, derived values, settings defaults and reset, ring order, rename migration.
- `StatsStoreTests` — sampling cadence, slow values kept between reads, history cap, process demand,
  sensor-change callback (against a fake `StatsSource`).
- `SamplerParsingTests` — network route-message parsing (physical interfaces only, truncated buffers),
  display choice and fallback.
- `StorageAnalyzerTests`, `StorageNavigationTests` — sizes on real temporary files; overview order,
  open / back with cached sizes, rescan.
- `FanControlTests` — helper command parsing (including rejected input), SMC value encoding, the admin
  script (verified root-owned copy), error cleanup, and `FanControl` rollback against a fake SMC.
- `LocalizationTests` — every string translated, format specifiers preserved.

## Performance notes

- Idle cost is about 0.5–3 % of one core and 0 idle wakeups per second with the pill folded and
  Settings closed (the previous always-on 50 ms hover timer caused 20 a second). Settings with a live
  page open costs more, since it redraws every tick.
- Per-second value changes are not animated: animating the rings kept SwiftUI re-laying out the panel
  for most of every second (~8 % CPU).
- Process scanning adds a few percent while a CPU or Memory card or page is open.
- Disk analysis walks every file, so large folders take seconds to a minute; it runs off the main
  thread, four folders at a time, and stops when you leave the page.

## Known limitations

- Thermals and fan control use the undocumented SMC interface; key names can change between chips and
  macOS releases. The Thermals ring hides itself when no sensors are found.
- Root-owned processes (WindowServer, kernel_task) are missing from process lists.
- Folder sizes count APFS clones and hard links once per path, so totals are approximate.
- The pill does not fold away for full-screen apps. Dragging it moves it between edges of its display,
  not to another display (pick the display in Appearance).
- Errors reported by the root fan helper itself (for example "The SMC refused to change F0Md.") are in
  English only.
- Another edge app on the same edge (e.g. Codenotch) will overlap — use a different edge.
