# Changelog

All notable changes to this project are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versioning follows
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- `HakoPods.Core.Bluetooth.IBluetoothNative` / `Win32BluetoothNative` /
  `BluetoothConnectionManager` — connect and disconnect with bounded retry, timeout,
  serialisation and idempotency; native error codes mapped and logged. 7 tests.
- Measured: enabling an already-enabled service returns `87` in ~2 ms (treated as done);
  `rc=0` means "service enabled", not "audio live".
- `docs/design/POPUP.md` + four SVG renders — popup spec covering all nine states, DPI rules,
  tokens, motion. `docs/design/ICONS.md` + 10 SVG masters and 40 PNGs (5 states × 2 themes ×
  4 sizes), produced by the `atelier` profile, verified by hermes, signed off by the owner.
- `HakoPods.Core.Devices` — paired-device discovery behind a seam, plus a selection service
  that persists the address (stable across re-pairing). 7 tests.
- `spike/soak` — harness that runs the real watcher and service side by side with `BleProbe`
  and reports raw vs emitted counts, restarts and memory.
- `docs/research/SOAK-2026-10-31.md` — 10-minute soak result: 499/499 packets, zero loss.
  APW-103 closed.
- `HakoPods.Core.Settings` — versioned `AppSettings` and an atomic JSON store that recovers
  from corruption instead of throwing. 5 tests.
- `HakoPods.Core.Logging` + `Diagnostics` — rolling `FileLogger` (1 MB × 5) and a local
  diagnostics zip (settings + logs + version). No network. 3 tests.
- `HakoPods.Core.State` — immutable `DeviceState` and a pure reducer with 60 s staleness
  (stale measurements are cleared, never frozen). 8 tests.
- `HakoPods.Core.Bluetooth` — `IAdvertisementWatcher` seam, WinRT adapter, and
  `ProximityPairingService` (dedupe, auto-restart, `IAsyncEnumerable` stream). 9 tests.
- `HakoPods.Core.Advertisements` — proximity-pairing model table, decoded advertisement record
  and a pure parser. 12 tests over real captured packets plus synthetic orientation cases.
- `HakoPods.sln` — `HakoPods.Core` (net8.0), `HakoPods.App` (WPF, PerMonitorV2), `HakoPods.Core.Tests`
  (xUnit), `Directory.Build.props`, and a CI workflow that also runs the task checker.
- `spike/ble-probe` — disposable .NET 8 console probe: Bluetooth adapter capabilities,
  radio state, and a timed BLE advertisement scan with per-device RSSI/model/hex output.
- `--capture <seconds> --out <file>` mode: writes a timestamped, per-packet markdown capture.
- `docs/captures/2026-10-31-lid-open.md` (59 packets / 60 s) and
  `2026-10-31-lid-cycles.md` (68 packets / 150 s) — real AirPods Pro traffic.
- `docs/captures/ANALYSIS-2026-10-31.md` — byte map confirmed on hardware, plus the
  rotating-address finding and cadence measurements.
- `docs/research/IDENTITY.md` — address resolution tested and refuted; v1 identity policy.
- `docs/research/SPIKE-RESULTS.md` — draft M1 report (measurements 1–4 complete).
- `--resolve <addr…>` probe mode: asks the OS to resolve a captured random address.
- `spike/bt-connect` — Classic probe: device status, A2DP connect/disconnect, HFP toggle,
  repeated cycles with timings. Non-elevated.
- `spike/audio-endpoint` — `IPolicyConfig` spike: enumerate render endpoints, switch the
  default for all three roles, verify, restore. 5 ms per role on build 26200.
- `NuGet.config` — pins nuget.org; the machine's user-level NuGet config has no sources.
- `docs/design/README.md` — artefact contract for design work owned by the `atelier` profile.
- Design tasks `APW-115` (popup), `APW-116` (tray glyphs), `APW-311` (settings);
  `APW-108` and `APW-301` now depend on them.

### Changed
- Design signed off (direction A): Inter bundled with the app, green/red battery rails kept,
  shadow-only edge. ADR-012 amended — the popup deliberately imitates the AirPods experience
  while window behaviour stays native. Charging bolts redrawn (the original path
  self-intersected and overlapped the buds); 8 charging PNGs regenerated.
- **M1 closed with a GO** (ADR-011): implementation authorised on measured evidence; ANC
  explicitly excluded from scope.
- ADR-002 accepted: the owner approved C# / .NET 8 + WPF and the SDK install.
- ADR-012 records that GUI design is owned by the `atelier` Hermes profile.
- `.NET SDK 8.0.427` is now installed on the dev machine (was runtime-only). APW-010 closed.
- APW-011 and APW-012 closed; APW-013 in review (3 of 10 lid-open trials).
- APW-015 and APW-016 closed from measured evidence; SPIKE-RESULTS measurements 5–6 filled.
- APW-017 closed: endpoint switch verified against the AirPods endpoint (4 ms / +12 ms).
- APW-020 closed: GATT enumeration returns `Unreachable` — no battery service.
- APW-014 closed: RSSI calibrated (−47 dBm at 0.5 m, −82 at 5 m; threshold −72/+6 dB hysteresis).
- APW-013 closed with an amended acceptance criterion: cadence + bounded wake gap, plus an
  owner observation that Windows shows connected within ~1 s of lid-open and disconnects in
  3–4 s.
- RESEARCH §5.6/§5.7 corrected: address rotation confirmed, and an awake pair advertises
  about once per second, not every 20–30 s.

### Notes
- The earlier "zero advertisements" result was environmental, not a stack failure: the
  watcher reported `Started` throughout and delivered 127 packets once the lid was open.
- Hardware observed unknown-value encodings live (`0xF` bud battery, case > 10), and three
  clean lid open/close cycles.

---

## [0.1.0-setup] — 2026-10-31

Project scaffolding and research. No application code.

### Added
- `README.md` — goals, non-goals, feasibility summary, prerequisites, documentation map.
- `AGENTS.md` — shared working agreement for every coding agent: read-first order, task
  claim/finish protocol, handover rules, no-overwrite rules, scope discipline, conventions.
- `TODO.md` — 53 tasks across six milestones (M0–M5), each with a unique ID, priority,
  status, milestone, dependencies and acceptance criteria.
- `PROGRESS.md` — current state, blockers, next steps, handoff notes.
- `DECISIONS.md` — ADR-001 … ADR-011 covering build-vs-fork, stack, driver policy, licence,
  state source, undocumented APIs, popup strategy, battery fidelity, tracking format,
  privacy and the M1 gate.
- `docs/RESEARCH.md` — prior art (14 projects), AirPods BLE proximity-pairing layout, Windows
  API inventory, feature feasibility matrix, licensing analysis, build-vs-extend verdict.
- `src/README.md` — planned solution layout and dependency direction.
- `scripts/check_tasks.py` — stdlib-only validator for the `TODO.md` grammar.
- `.gitignore` — build output, local tooling, and agent-instruction files kept out of git.
- `CLAUDE.md` — one-line pointer to `AGENTS.md` (git-ignored).

### Decisions
- Build from scratch under MIT rather than fork AirPodsDesktop (GPL-3.0) — ADR-001, ADR-004.
- C# / .NET 8 + WPF as the client stack — ADR-002.
- Driver-free v1; AAP-based features deferred to a gated M5 — ADR-003.
- 10 % battery granularity accepted; no Apple key extraction — ADR-008.

### Known gaps
- No hardware verification yet; every protocol claim is from published research or prior art.
- No `git init`, no LICENSE file, no CI.
- .NET SDK not installed on the dev machine (runtime only), blocking the spike.
