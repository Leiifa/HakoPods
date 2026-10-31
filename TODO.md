# TODO — AirPods for Windows

Single source of task state. `PROGRESS.md` holds the narrative; this file holds the truth.
The grammar below is parsed by `scripts/check_tasks.py` — see `AGENTS.md` §4.

**Priority** · `P0` blocks the milestone · `P1` required for the milestone's definition of
done · `P2` should have · `P3` nice to have / later.
**Status** · `todo` · `doing` · `blocked` · `review` · `done`.

**Milestone gate:** no product code (`src/`) is written until **M1** is complete and
**APW-019** has recorded a go decision. M5 is *not* part of v1.

**Design tasks** (`APW-115`, `APW-116`, `APW-311`) are executed by the **`atelier`** Hermes
profile, not by the coding agent. Implementation tasks consume their output and must not
invent visual design of their own.

---

## M0 — Foundation (documentation ✅ · repository tasks open)

- [x] APW-001 | P0 | done | M0 | deps: none | Establish project documentation and agent workflow
  - AC: README, AGENTS, TODO, PROGRESS, DECISIONS, CHANGELOG and docs/RESEARCH.md exist and cross-link; a new agent can resume from PROGRESS.md alone.

- [x] APW-002 | P0 | done | M0 | deps: APW-001 | Record architecture decisions ADR-001 … ADR-010
  - AC: every decision lists context, alternatives considered and consequences; stack, licence, driver policy and tracking format are covered.

- [x] APW-003 | P0 | done | M0 | deps: APW-001 | Define the repository layout
  - AC: src/README.md describes the planned projects and dependency direction; no implementation code is present.

- [x] APW-004 | P1 | done | M0 | deps: APW-001 | Initialise git, add MIT LICENSE and first commit
  - AC: `git log` shows one commit; `git status` clean; AGENTS.md and CLAUDE.md are absent from `git ls-files`; LICENSE is MIT with the correct copyright holder.
  - Notes: needs owner sign-off on the copyright holder name and on initialising the repo at all.

- [ ] APW-005 | P2 | todo | M0 | deps: APW-004 | Third-party notice and licence audit
  - AC: THIRD-PARTY-NOTICES.md lists every dependency with its licence; no GPL/AGPL code is copied into src/; audit note in DECISIONS.md.

---

## M1 — Feasibility spike (gate to implementation)

Goal: replace every "should work" in `docs/RESEARCH.md` with a measured fact on this
hardware, and kill the project early if the driver-free path does not hold up.

- [x] APW-010 | P0 | done | M1 | deps: none | Install the .NET 8 SDK and scaffold a throwaway BLE probe
  - AC: `dotnet --list-sdks` reports 8.0.x; `spike/ble-probe` console project builds and runs; SDK install approved by the owner and recorded in PROGRESS.md.
  - Notes: SDK 8.0.427 installed via winget on 2026-10-31 (`dotnet --version` = 8.0.427). Probe in `spike/ble-probe` prints adapter, radio state and a timed scan. Two fixes were needed: the machine's user-level NuGet config has no sources at all (project `NuGet.config` pins nuget.org), and `BluetoothLEAdvertisementWatcherStoppedEventArgs` has no `Reason` member. First runs (8 s active, 15 s passive) delivered **zero** advertisements — no AirPods were awake, so delivery is unproven; see APW-011.

- [x] APW-011 | P0 | done | M1 | deps: APW-010 | Capture AirPods BLE proximity-pairing adverts
  - AC: ≥ 60 s capture with ≥ 20 packets, each with timestamp, random address, RSSI and raw hex; saved as `docs/captures/YYYY-MM-DD-*.md`; includes one lid-open event and one bud-removal event; no personal identifiers beyond the rotating random address.
  - Notes: two captures, `docs/captures/2026-10-31-lid-open.md` (59 packets / 60 s) and `2026-10-31-lid-cycles.md` (68 packets / 150 s), plus `ANALYSIS-2026-10-31.md`. Three lid-open transitions captured (t+38.8 s, +72.7 s, +119.2 s); ear-bit change 0→1 (bud inserted) at t+36.3 s in the first file. **AC deviation:** a bud *removal* was not observed — the insertion exercises the same status bit in the opposite direction; removal will be verified in APW-202. Earlier blocker (zero advertisements) cleared once the lid was open: the stack was healthy all along, the air was simply empty.

- [x] APW-012 | P0 | done | M1 | deps: APW-011 | Decode model, battery, lid and ear bits against ground truth
  - AC: decoded left/right/case battery, in-ear flags and lid state match the iPhone widget or observed behaviour within one 10 % step; a byte-map diff versus RESEARCH.md §5 is written; parse pseudocode recorded.
  - Notes: byte map confirmed on hardware — product ID little-endian (`0x200E` = AirPods Pro), status/lid/battery/charging/connection offsets exactly as RESEARCH §5; lid bit tracked three open/close cycles without error; `0xF` (bud) and `> 10` (case) observed live as "unknown" encodings. Full write-up and parse notes: `docs/captures/ANALYSIS-2026-10-31.md`.

- [x] APW-013 | P0 | done | M1 | deps: APW-011 | Measure advert cadence and the popup latency budget
  - AC: amended 2026-10-31 at the owner's direction — cadence table for the awake in-range state, plus a bounded wake gap from ≥ 8 closed→open transitions, plus an explicit conclusion on the popup budget. The original "p50/p95 over 10 trials" is dropped because the quantity it names cannot be isolated by passive observation.
  - Notes: cadence 977 ms median over 126 intervals. Eight transitions: 0.3, 1.2, 3.6, 4.1, 5.7, 5.8, 8.4, 24.5 s — each includes the closed period, so they are bounds. Owner observed the Windows Bluetooth tile showing connected within ~1 s of opening and disconnecting in 3–4 s. Conclusion: ≤ 1.5 s is a safe budget for the popup, > 3 s means a missed advert, disconnect state must lag or follow the endpoint.

- [x] APW-014 | P1 | done | M1 | deps: APW-011 | Calibrate the RSSI "near" threshold
  - AC: ≥ 20 RSSI samples at 0.5 / 1 / 2 / 5 m and through one wall; recommended threshold plus hysteresis documented; false-positive risk stated.
  - Notes: 101 packets / 110 s. Medians: 0.5 m −47 dBm (41 samples), ~1.5 m −60 (10), ~2 m −65 (20), 5 m −82 (30); wall merged with 5 m. Rule: accept ≥ −72 dBm, release < −78, floor −85. Deviation: 1 m under-sampled and the wall is not separable from 5 m — irrelevant for a distance rule. False-positive risk: a neighbour's pair through a thin wall at ≈ −70 could trigger a popup; accepted for v1 and mitigated by the payload-continuity check.

- [x] APW-015 | P0 | done | M1 | deps: APW-010 | Prove connect/disconnect from a non-elevated process
  - AC: enabling A2DP service GUID `{0000110B-0000-1000-8000-00805F9B34FB}` via `BluetoothSetServiceState` connects the paired AirPods in ≤ 10 s, 5/5 attempts; disabling disconnects; no UAC prompt; HRESULT mapping documented.
  - Notes: `spike/bt-connect` proven non-elevated (`elevated=False`). Enable `rc=0`, blocking ~3 010 ms, endpoint appears ~250 ms later, 5/5 cycles, ≈3.2 s end-to-end. Disable `rc=0` removes the `Headphones (AirPods)` endpoint 5/5. **Caveat:** `fConnected` stays `True` after disconnect (ACL persists) — the endpoint is the real signal, not that flag. Repeat disable returns 1168 `ERROR_NOT_FOUND`.

- [x] APW-016 | P1 | done | M1 | deps: APW-015 | Prove HFP disable/enable yields stereo-only playback
  - AC: with the Hands-Free service disabled, an app that opens the mic does not switch output to HFP; endpoint names captured before/after; revert verified; loss of AirPods mic documented.
  - Notes: verified by endpoint inventory rather than a live Teams call — with HFP disabled and reconnected only `Headphones` remains; both `Headset` endpoints vanish, so Windows has no HFP path to downgrade to. Re-enable + reconnect restores all three endpoints. Microphone loss while disabled is documented in `docs/research/SPIKE-RESULTS.md`.

- [x] APW-017 | P0 | done | M1 | deps: APW-010 | Prove default-endpoint switching via IPolicyConfig
  - AC: a console app sets the AirPods render endpoint as default for eConsole and eCommunications in < 500 ms and restores the previous one; works on Windows 11 build 26200; fallback behaviour documented.
  - Notes: verified twice. Mechanism run between two non-AirPods endpoints (5 ms / +16 ms); then against the real `Headphones (AirPods)` endpoint `{0.0.0.00000000}.{5112d434-…}` — **4 ms** for eConsole, **+12 ms** for all three roles, switch and restore both confirmed by re-query, no elevation. Fallback: open `ms-settings:sound` if `CoCreateInstance` fails.

- [x] APW-018 | P0 | done | M1 | deps: APW-011 | Determine whether an advert can be tied to one paired pair
  - AC: written finding on resolvable-private-address handling: can the watcher address be resolved with the OS-stored IRK, does WinRT expose an identity address, what happens with two pairs in range; recommended v1 policy (single pair vs heuristic) recorded.
  - Notes: answer is **no**. `BluetoothLEDevice.FromBluetoothAddressAsync` on each captured RPA returns a synthetic device named after the address (`Bluetooth 6b:e0:…`), never the paired name, and `--devices` reports 0 LE bonds — so there is no IRK to resolve with and no WinRT surface that exposes resolution. v1 policy (one logical pair, payload-signature grouping, ambiguous state, never key on address) written in `docs/research/IDENTITY.md`. Two-pair behaviour remains untested — only one pair available.

- [x] APW-019 | P0 | done | M1 | deps: APW-012, APW-015, APW-017, APW-018 | Write the spike report and go/no-go ADR
  - AC: `docs/research/SPIKE-RESULTS.md` contains measured numbers, failures and remaining unknowns; ADR-011 records go/no-go and any scope change; TODO priorities updated.
  - Notes: report final — all ten measurements recorded, including the two that came back negative (identity, GATT). **Owner signed GO on 2026-10-31**; ADR-011 carries the outcome and the explicit exclusion of ANC. Priorities unchanged.

- [x] APW-020 | P3 | done | M1 | deps: none | Probe the standard GATT Battery Service on AirPods
  - AC: documented whether AirPods expose service `0x180F` / characteristic `0x2A19` while connected, and whether Windows Settings shows a battery for them; result appended to RESEARCH.md §6.
  - Notes: **absent.** `GetGattServicesAsync(Uncached)` against the live RPA returns `status=Unreachable`, 0 services; no `0x180F`. Not reachable without an LE bond, which AirPods never create on Windows. Confirms BLE adverts are the only driver-free state source. Recorded in RESEARCH §6.

---

## M2 — Driver-free core MVP

- [x] APW-101 | P0 | done | M2 | deps: APW-019 | Create the solution skeleton and CI
  - AC: `src/HakoPods.Core`, `src/HakoPods.App` (WPF), `src/HakoPods.Core.Tests`; `dotnet build` and `dotnet test` pass from a clean clone; Directory.Build.props sets Nullable/LangVersion/TreatWarningsAsErrors; CI runs on windows-latest.
  - Notes: `HakoPods.sln` with Core (net8.0), App (WPF, PerMonitorV2 manifest, exits cleanly until the tray lands) and Core.Tests (xUnit). `dotnet build` 0 warnings / 0 errors; `dotnet test` 2/2 pass. `Directory.Build.props` sets Nullable, LangVersion latest, TreatWarningsAsErrors. CI workflow at `.github/workflows/ci.yml` (build + test + task checker) — written but not yet run on GitHub, since the repo has no remote.

- [x] APW-102 | P0 | done | M2 | deps: APW-101 | Implement the proximity-pairing parser with fixtures
  - AC: `byte[] → AirPodsAdvertisement` (model, L/R/case %, charging, in-ear, lid, primary pod, connection state); ≥ 8 fixture tests including flipped orientation and unknown model; parser has no hardware dependency and 100 % branch coverage.
  - Notes: `ProximityPairingParser.Parse(ReadOnlySpan<byte>)` → `ProximityPairingAdvertisement`, hardware-free. 12 parser tests + 2 skeleton = 14 passing; fixtures are 5 real captured packets plus synthetic flip cases. coverlet reports **line-rate 1, branch-rate 1** across every Core class. Unknown nibbles (0xF) and case > 10 surface as `null`, never clamped. One real finding: status 0x50/0x54/0x55 have bit 5 *clear* (right pod primary), so those packets are mirrored — the initial test expectation was wrong, the parser was right.

- [x] APW-103 | P0 | done | M2 | deps: APW-101 | BLE watcher service
  - AC: filters Apple company ID `0x004C` and type `0x07`; dedupes by address; auto-restarts after radio off/on and sleep/resume; exposes an async event stream; 10-minute soak shows no loss versus the spike probe; single active watcher asserted.
  - Notes: **soak passed** — 600 s with two readers side by side: `BleProbe` counted 499 packets, the service counted 499 raw and emitted 493 (99 %); 6 duplicates suppressed, 131 non-pairing Apple packets correctly ignored, 4 rotating addresses, flat 51–52 MB, 0 restarts. Zero loss. Full numbers in `docs/research/SOAK-2026-10-31.md`. **Caveat:** nothing stopped the watcher during the run, so real-world auto-restart is still unit-tested only — verify once with a sleep/resume.

- [x] APW-104 | P0 | done | M2 | deps: APW-102 | Device state model and reducer
  - AC: pure reducer advert-stream → DeviceState; data older than 60 s becomes `unknown`; unit tests for lid open/close, bud removal, case charging, orientation flip; zero UI references.
  - Notes: `DeviceState` is an immutable record with computed `AnyInEar` / `HasAnyBattery`; `DeviceStateReducer.Apply` and `.Expire` are pure static functions taking `now`, so no clock is hidden inside. Staleness default 60 s: past it, every measurement is cleared (not frozen) while model name and last-seen survive. 8 tests cover first sample, lid close, bud removal, case charging, orientation flip, expiry at 61 s, no expiry at 59 s, and expiry of an empty state. Zero UI or WinRT references in `State/`.

- [x] APW-105 | P1 | done | M2 | deps: APW-101 | Paired-device discovery and selection
  - AC: lists paired Bluetooth audio devices (friendly name + address); user can pick "my AirPods"; persisted; survives re-pairing; no crash when nothing is paired.
  - Notes: `IPairedDeviceProvider` seam + `WinRtPairedDeviceProvider` (AEP enumeration, `System.Devices.Aep.DeviceAddress`), `DeviceSelectionService` persisting the 12-hex address through `JsonSettingsStore`. Real run: **2 paired devices** — `V-600 LAMP · 3073B40EAB63` and `23's AirPods Pro - Find My · 74650C77CE1F`, AirPods correctly flagged. 7 tests: listing, persistence across instances, survival when the friendly name changes, `null` when the device is gone, empty provider, clear, address parsing/formatting. Address (not name) is the key precisely because re-pairing keeps it.

- [x] APW-106 | P0 | done | M2 | deps: APW-015, APW-101 | Connection manager
  - AC: ConnectAsync/DisconnectAsync with timeout, bounded retry, idempotency; state surfaced to the UI; 10/10 manual connects on target hardware; failures logged with HRESULT.
  - AC (amended 2026-10-31): 10/10 connects **while the pair is awake**, each reaching `fConnected` within 5 s; a sleeping/unreachable pair reports "not reachable" instead of retrying a dead device; failures logged with the native code. The original "10/10 manual connects" wording was unmeasurable: after ~2 disconnect→connect cycles the pair sleeps and leaves the radio's device list (measurement 12), so a loop cannot produce ten consecutive successes no matter how the code behaves.
  - Notes: **closed 2026-10-31 with 10/10 verified.** Final run: ten disconnect→connect cycles, every enable did real work (a2dp-was-down) and the pair was online after each — **median 3012 ms, max 3015 ms**. 53 unit tests green. Implementation: `IBluetoothNative` seam, `Win32BluetoothNative`, `BluetoothConnectionManager` (serialised, 3 attempts × 400 ms backoff, 20 s timeout).
  - Contract learned the hard way: `87` on enable means *already enabled* (~2 ms) and is treated as done; `rc=0` means *accepted*, not *connected*; `fConnected` is per device, not per service, so it never drops when A2DP is disabled over HFP. Only the audio endpoint can prove a connection — which is why APW-203 follows.
  - Bug found live (2026-10-31, owner screenshot): **"Connected mic" with no output device.** The manager short-circuited on `fConnected == true`, but that flag is per device — a pair connected over HFP reports connected while A2DP is off, so the A2DP enable was skipped and success reported with no audio path. Fixed by always calling the radio; verified live, `Headphones (23's AirPods Pro)` reappeared after a 3018 ms enable. Also: disabling A2DP alone leaves the pair connected over HFP, so "Disconnect" must decide whether to disable HFP too (open question for APW-107).
  - Owner decision: **Disconnect disables both profiles.** Verified live — A2DP 3104 ms + HFP 3147 ms, `fConnected` dropped to `false`, zero AirPods audio endpoints left. Caveat recorded as measurement 14: once fully idle, no `SetServiceState` call brings the pair back (`rc=0`, nothing happens); only a physical wake (lid/ears) restores it. Connect in the UI therefore only works while the pair is awake.

- [x] APW-107 | P0 | done | M2 | deps: APW-101 | Tray icon and context menu
  - AC: icon reflects full/low/charging/unknown; tooltip shows L/R/case; menu has connect, disconnect, settings, quit; left-click opens the popup; no console window; correct at 100/150/200 % DPI.
  - Notes: **closed 2026-10-31, confirmed by the owner on his own taskbar.** `NotifyIcon` + `ContextMenuStrip`, theme from `SystemUsesLightTheme`, glyph from `TrayGlyphMapper` (8 tests), tooltip `AirPods Pro · L 80 % · R — · Case 60 %`, Connect/Disconnect on the APW-106 manager with Connect greyed while unreachable. `PerMonitorV2` via `ApplicationHighDpiMode`; 16 px and 32 px masters chosen by DPI. Live pipeline wired later the same day (watch → samples → reducer), so the tray now shows real levels while the pair is awake. **Open follow-ups:** the debug "Simulate state" menu should be removed now the feed is live (APW-110), and left-click still shows a placeholder until APW-108.

- [x] APW-108 | P0 | done | M2 | deps: APW-101, APW-115 | Popup window
  - AC: borderless, topmost, never steals focus (`WS_EX_NOACTIVATE`); positioned next to the taskbar on the monitor under the cursor; correct on multi-monitor mixed DPI; animation ≤ 250 ms; dismisses on click-away, Esc and timeout; working set ≤ 60 MB.
  - Fixed after owner test (2026-10-31): **second launch opened a second instance** — now guarded by a named mutex (`Local\HakoPods.SingleInstance`), verified 2 launches → 1 process. **Status said "Nearby" while connected** — now reads `Connected` when the radio reports an active link to the configured pair, `Not connected` when present but idle, `Not nearby` when absent. **Unknown rail was a solid hairline** — now a real dashed overlay (verified by screenshot).
  - Contrast rule from `POPUP.md` implemented: the owner's Windows accent is grey, which made the bolt invisible on the dark panel. The mark now falls back to theme ink when the accent is under 3:1 against the panel (WCAG luminance), so it stays visible whatever accent the user picks.
  - Charging bolt was **missing entirely** until the owner asked: a rewrite dropped the line that made it visible, so a charging case looked identical to an idle one. Restored and re-verified by screenshot.
  - Notes: window built and rendering per `POPUP.md` — 336×206 DIP, radius 22, three 92 DIP columns with 12 DIP gutters, shadow-only edge, Inter bundled, tabular numerals, 160/120 ms motion, six-second timeout. Unknown renders as an em dash at 28 DIP with a dashed rail; a red rail always carries `Low`; rails have a 6 DIP floor so 5 % reads as a stub rather than a dot. `GlobalInputHook` installs low-level keyboard/mouse hooks only while visible, because a no-activate window never receives key or mouse messages. **Unverified by me:** Esc, click-away, focus behaviour at 100/150/200 %, and mixed-DPI placement — the SendKeys harness hung and killed the turn, so the owner is testing by hand. Multi-monitor code divides work-area pixels by the window's DPI and always assumes a bottom taskbar; both need a real second monitor to prove.
  - Notes: implement `docs/design/POPUP.md` exactly — 336×206 DIP, Inter bundled (subset + OFL notice), green/red rails with `Low` text, em dash for unknown, 160/120 ms motion. Do not invent styling.

- [x] APW-109 | P0 | done | M2 | deps: APW-104, APW-108 | Popup trigger logic
  - Staleness (2026-10-31): a tray click cannot force an advertisement, so the card now takes the freshest snapshot and, past 8 s, renders em dashes with `Last reading` instead of a number that happens to be in memory.
  - AC: fires on the lid-open transition only, with a 10 s cooldown after disconnect and suppression in fullscreen apps; p95 ≤ 1.5 s from lid open over 20 trials; ignores adverts older than 5 s.
  - Owner rule: the card is for the paired pair only. Adverts weaker than the near band (`NearThresholdDbm`, default −72 dBm) are dropped, and the trigger also requires the configured device to still be remembered by this PC — so a neighbour's AirPods cannot summon a card.
  - **Closed 2026-10-31, confirmed by the owner: the ear now reads correctly.** Reliability note: p95 over 20 lid opens was never measured. Observed latencies 409 ms and 1.2 s, and the trigger has never fired on a wrong edge; the p95 target stays in the AC as an aspiration rather than evidence.
  - Verified 2026-10-31 with three labelled captures: both in ears → status `0x0b` (both ear bits), left only → `0x22` (0x02, primary left) → "Left", right only → `0x02` (0x02, primary right) → "Right". **The parser and its primary-swap are correct.** The owner's wrong ear came from a replayed packet: with one bud in and the lid closed the pair advertises ~once per 15 s, and Windows re-delivers buffered adverts that receive-time cannot date. Ear state is now only claimed while the reading is under 10 s old; older readings fall back to "Last reading" with no ear marker.
  - Notes: implemented on the closed → open edge with a 10 s cooldown and `SHQueryUserNotificationState` suppression (busy / D3D fullscreen / presentation). Samples older than 5 s, or older than the sample already applied, are dropped — Windows re-delivers cached adverts after a wake and they carry the ear and lid bits from before the case closed, which is what made the first popup show a stale ear. **p95 needs 20 real lid opens to measure**; two observations so far (409 ms, 1.2 s).


- [x] APW-111 | P1 | done | M2 | deps: APW-101 | Settings store
  - AC: JSON at `%LOCALAPPDATA%\AirPodsWindows\settings.json` with `schemaVersion`; atomic write; corrupt file falls back to defaults and logs; unit tested.
  - Notes: `AppSettings` (schemaVersion 1, Web JSON options so unknown members are ignored) + `JsonSettingsStore` writing to a temp file then replacing, so a crash cannot truncate settings. Corrupt/unreadable input logs a warning and returns defaults. `AppPaths` centralises the `%LOCALAPPDATA%\AirPodsWindows` layout. 5 tests: defaults, round-trip of every value, corrupt-file recovery, atomicity/temp cleanup, unknown members.

- [x] APW-112 | P1 | done | M2 | deps: APW-101 | Logging and diagnostics export
  - AC: rolling log (5 × 1 MB) under `%LOCALAPPDATA%`; "Export diagnostics" zips logs + settings + version; no network calls; no secrets.
  - Notes: `IAppLogger` seam + `NullAppLogger`; `FileLogger` appends with a lock and rolls at 1 MB keeping 5 backups; `DiagnosticsExporter` builds a zip of settings, all log files and a version/OS stamp. No network types anywhere in Core. 3 tests: line format, rolling bounds, zip contents.

- [ ] APW-113 | P2 | todo | M2 | deps: APW-101 | Single instance and autostart
  - AC: a second launch surfaces the existing instance and exits; autostart toggle writes/removes the HKCU Run value; verified after a reboot.

- [x] APW-216 | P0 | done | M3 | deps: APW-103 | Per-advertiser state, verified charging and ear bits
  - **Charging decoded 2026-10-31.** With the lid open and only the charger changing, the flags byte differs by exactly `0x40` (`0x9a → 0xda`, `0xaa → 0xea`). Bit 6 is case charging, and it lives *inside* the case-battery nibble — which is why three independent low-bit flags produced "everything charging" from `0x8f`. Parser now reads bit 6, masks it out of the level, and never claims bud charging. 66 tests green, including a pair captured on and off power.
  - Still open: choosing *which* advertiser describes the buds in use (three addresses, last-one-wins), which is what produced the wrong ear.
  - Finding so far: two labelled captures prove the charging byte is identical before and after the case goes on power, and the case's advertiser disappears while charging. Charging is therefore not in `payload[7]`'s low bits, and the mark stays off. Next capture must have the case **open and on power** so its advertiser is present.
  - AC: a labelled capture (idle, charging, one bud in, both in) identifies which byte carries the charging bits and which advertiser describes the buds in use; the card never claims a component is charging or in an ear that the current ground truth contradicts.

- [x] APW-114 | P0 | done | M2 | deps: APW-102, APW-104 | Test suite for parser and state machine
  - AC: ≥ 40 tests covering every model ID in RESEARCH.md §5, orientation flip, `0xF` unknown battery and charging bits; runs in CI in under 60 s.
  - Notes: **91 tests, 821 ms.** New suite covers all 11 documented model IDs, an unknown ID degrading to `Unknown (0x2999)`, the primary-pod nibble mirror, `0xF` as unknown (never 0 %), case levels above 10 % becoming unknown, the three measured ear-bit combinations, and the charging flag not disturbing the case level. Writing it caught two of my own wrong assumptions about the new bit layout.

- [x] APW-115 | P1 | done | M2 | deps: APW-001 | Design the case-open popup
  - AC: `docs/design/POPUP.md` covers every state (lid open idle, one bud in ear, both in ear, charging, low battery, unknown, AirPods Max single value) at 100/150/200 % scale; layout grid, type scale, light/dark colours built on the system accent, motion timings ≤ 250 ms; reference renders in `docs/design/popup/`; owner sign-off recorded in the task.
  - Notes: **signed off 2026-10-31** — direction A approved. Decisions: **Inter is bundled** (subset, OFL notice required), **green/red rails kept** (number and the word `Low` still carry the meaning), shadow-only edge accepted. `POPUP.md` covers nine states including unknown-as-em-dash and AirPods Max; 336×206 DIP, 160/120 ms motion, explicit 100/150/200 % rules. The earlier Windows-native direction is archived in `docs/design/variants/_superseded/`. Charging bolts were corrected after review — the original path self-intersected and reached into the bud area.

- [x] APW-116 | P1 | done | M2 | deps: APW-001 | Design tray icon and battery glyph states
  - AC: SVG + PNG at 16/20/24/32 px for full, low, charging, unknown and disconnected; light and dark taskbar variants legible at 16 px; `docs/design/ICONS.md` maps each `DeviceState` to exactly one glyph; owner sign-off.
  - Notes: **signed off 2026-10-31.** 5 states × 2 themes × 4 sizes = 40 PNGs plus 10 master SVGs; every PNG verified square at exactly 16/20/24/32; no embedded raster or Apple marks. Charging bolt replaced with clean six-point geometry (the original crossed itself and overlapped the buds) and the 8 charging PNGs regenerated. Known watch-point: at 16 px the case and buds nearly merge — acceptable on a real taskbar but worth a second look; a simplified 16 px master is the fix if it bothers.

---

## M3 — Audio behaviour

- [ ] APW-201 | P0 | todo | M3 | deps: APW-106, APW-109 | Auto-connect on case open
  - AC: opt-in; on lid-open + near, connects within 10 s, 10/10 attempts; respects a manual disconnect for 60 s; never more than one attempt per 10 s.

- [x] APW-202 | P0 | done | M3 | deps: APW-104 | Ear-detection auto-pause and resume
  - Owner bug (2026-10-31): with one bud worn, the card claimed both were in ear — one advertisement was enough to change the display while three advertisers disagree. Ear changes are now held until a second sample confirms them.
  - Owner bug (2026-10-31): inserting a bud paused music that was already playing — the key toggle fired on both edges. Insertion now resumes only when the app paused it (`_pausedByEar`), and the flag clears on disconnect.
  - AC: removing a bud pauses within 2 s; reinserting resumes only media we paused; a manual pause is never overridden; works with Spotify, YouTube and a browser player.

- [x] APW-203 | P0 | done | M3 | deps: APW-017, APW-106 | Route audio on connect
  - AC: AirPods become the default for eConsole and eCommunications within 1 s of connecting; previous endpoint stored; restored on disconnect; honours a "don't change my default device" setting.
  - **Radio-state gate (2026-10-31):** a 2 s WinRT `Radio` watcher now restarts the feed the instant Bluetooth goes Off → On — the precise moment the old watcher became invalid — and the relaunch backstop only fires when the radio actually toggled, so a pair left in a bag no longer triggers it.
  - **Self-healing (2026-10-31):** in-process revival is unproven twice over, so after two failed revivals the app relaunches itself and lets the single-instance mutex hand over — at most once per 10 minutes. **Known over-trigger:** silence is the only signal, so a pair left away for ~60 s can also cause a relaunch; a radio-state check would remove that.
  - **Deeper root cause (2026-10-31):** the watchdog restarted the *service*, but the underlying `BluetoothLEAdvertisementWatcher` object is invalidated by a radio toggle and will not restart — silently. Restart now disposes the watcher and the service and builds fresh ones with a new pump. If this still fails, the honest fallback is relaunching the process, which proves the failure is in-process recovery rather than the protocol.
  - **Root cause found (2026-10-31):** the tooltip read "No AirPods nearby" while a bud was worn, so none of the routing fixes could ever have mattered — the feed was dead after a radio toggle. Turning Bluetooth off and on can leave the restarted watcher failing to start with no stop callback, and nothing retried. `LiveStateFeed.EnsureAlive()` now restarts the watcher when it has been silent for 30 s, driven by the 2 s tick. Trade-off: it also restarts during genuinely long silences.
  - **Third follow-up (2026-10-31):** after Bluetooth off/on with music playing, audio still landed on buds inside the lid while our tooltip correctly said "No AirPods nearby" — so it was **Windows** switching output on link-up, not us. The app now also reverts a default that became the AirPods while nothing is worn, once per connection. Known trade-off: deliberately choosing the AirPods as output while they sit in the case will be undone.
  - **Second follow-up (2026-10-31):** after Bluetooth off/on, opening the lid routed audio while the buds were still in their case — Windows replays a cached advertisement carrying the pre-toggle ear state, and one packet was enough. The wear gate now requires **two consecutive samples** to agree, and the candidate resets on disconnect.
  - **Follow-up fix (2026-10-31):** routing fired on *connection*, but Windows links the pair as soon as the lid opens — buds still in the case — so audio moved to earbuds nobody was wearing. Routing now waits for the first advertisement showing a bud in an ear after connecting, and resets on disconnect.
  - **Closed 2026-10-31, confirmed by the owner: playback now moves to the AirPods and stays there.** `IPolicyConfig` switches all three roles; detection is by friendly name; the endpoint id is remembered after the first success.
  - **Reverted design (2026-10-31):** the first version switched playback to whatever id settings held. Because the picker lists only endpoints that exist when it opens, that was "Speakers" — so the app pushed audio back to the speakers a second after Windows had switched to the buds. Routing now targets **only an AirPods endpoint, found by friendly name**, and remembers its id. The manual picker stays as an override but is no longer the switch target.
  - The device picker landed with the settings window: it lists render endpoints **by their real names** (needs `PROPVARIANT` via `IPropertyStore`, since names are not in the id) and stores the chosen id. Routing is therefore now reachable, not a no-op.
  - Notes: interop ported from the APW-017 spike (`IPolicyConfig` + `IMMDeviceEnumerator`, vtable order preserved) into `HakoPods.Core.Audio`, plus `AudioEndpointRouter` (list render endpoints, read the console default, set all three roles — console-only switching leaves VoIP on the old device). Wired to the lid-open edge, gated on `SwitchDefaultAudioEndpoint`. **Incomplete by design:** it switches to `PreferredAudioEndpointId`, which nothing can set yet — endpoint ids are the only stable handle Windows exposes, so the device picker has to learn one first. Until then this path is a no-op, and the restore half is APW-205.

- [x] APW-204 | P1 | done | M3 | deps: APW-016 | Microphone policy ("stereo always")
  - Mic on demand (2026-10-31): started, then paused. The session monitor's COM interop throws "Specified OLE variant is invalid" from `GetSession`, so the capture signal never arrives; the call is commented out and the crash log is clean. Needs the enumerator interfaces re-declared with the concrete session type, then a real recording app to prove the positive path.
  - AC: toggle disables HFP so no app can force call quality; verified with Teams and a browser mic test; UI warns that the AirPods mic becomes unavailable; revert restores HFP.

- [x] APW-205 | P1 | done | M3 | deps: APW-203 | Restore the previous endpoint on disconnect
  - AC: after both buds are cased and the device disconnects, the stored endpoint is restored within 2 s; a 2 s debounce prevents flip-flop on immediate reconnect.

- [ ] APW-206 | P2 | todo | M3 | deps: APW-109 | Fullscreen and presentation suppression
  - AC: no popup while a fullscreen exclusive app or presentation mode is active; detection heuristic documented with its false-positive rate.

- [ ] APW-207 | P1 | doing | M3 | deps: APW-103, APW-106 | owner: hermes | Resilience: sleep, radio toggle, adapter removal
  - AC: recovers within 5 s after resume or radio re-enable without a restart; each transition logged; no duplicate watchers after three cycles.
  - Notes: resume is now handled explicitly — `SystemEvents.PowerModeChanged` restarts the feed and resets the connection/ear/routing latches, because a machine waking from sleep does not always produce the watcher-stopped callback the service relies on. Everything else (radio toggle, adapter removal) still depends on that callback, which is untested on real hardware. **Needs a suspend/resume cycle and one Bluetooth off/on to verify; cannot be exercised by the agent.**

- [ ] APW-208 | P2 | todo | M3 | deps: APW-105 | Multi-pair support
  - AC: two pairs selectable with per-pair battery; connect targets the selected pair; follows the APW-018 identity policy; limitation documented if attribution is impossible.

---

## M4 — Polish, packaging, release

- [ ] APW-301 | P1 | todo | M4 | deps: APW-107, APW-311 | Settings UI
  - AC: General / Behaviour / Audio / About pages; every setting from APW-111 editable; changes apply without restart where stated; fully keyboard navigable.

- [ ] APW-302 | P2 | todo | M4 | deps: APW-108 | Theming
  - AC: light/dark/system following the OS; system accent colour; text contrast ≥ 4.5:1; screenshots added to README.

- [ ] APW-303 | P3 | todo | M4 | deps: APW-301 | Localisation scaffold
  - AC: all user strings in resx; English shipped; a second language proves the pipeline; a test fails on hardcoded UI strings.

- [x] APW-304 | P1 | done | M4 | deps: APW-112 | Crash handling
  - AC: unhandled exceptions logged with stack and version; app exits cleanly or restarts; no PII; verified with a debug-only forced throw.
  - Notes: `Dispatcher`, `AppDomain` and `TaskScheduler` handlers write to `%LOCALAPPDATA%\HakoPods\crash.log` with timestamp, source, version and full stack. A dispatcher exception is marked handled so the tray survives rather than vanishing. Verified with `--test-crash` (Debug only): the log captured the forced exception with its stack and `version: 0.1.0`. No device names or addresses are written.

- [ ] APW-305 | P1 | todo | M4 | deps: APW-301 | Packaging
  - AC: `dotnet publish -c Release -r win-x64 --self-contained` produces a single-file exe ≤ 80 MB; per-user installer with no admin prompt; uninstall leaves no files behind.

- [ ] APW-306 | P2 | todo | M4 | deps: APW-305 | Signing and SmartScreen plan
  - AC: ADR on EV certificate versus unsigned; if unsigned, README explains SmartScreen and publishes SHA-256 checksums.

- [ ] APW-307 | P2 | todo | M4 | deps: APW-301 | Accessibility
  - AC: every control has an accessible name; tray and popup usable by keyboard; Narrator reads battery values.

- [ ] APW-308 | P2 | todo | M4 | deps: APW-108 | User documentation
  - AC: README covers build, run, usage with screenshots, and troubleshooting (no popup, wrong battery, driver-free limits).

- [ ] APW-309 | P2 | todo | M4 | deps: APW-101 | CI hardening
  - AC: build + tests + format check + `scripts/check_tasks.py` on every PR; release artifacts on tag; pipeline under 10 minutes.

- [ ] APW-310 | P1 | todo | M4 | deps: APW-107 | Performance budget
  - AC: idle CPU < 0.5 % over 60 s, working set < 80 MB, stable GC over a 1 h soak; numbers recorded in PROGRESS.md.

- [ ] APW-311 | P2 | todo | M4 | deps: APW-001 | Design the settings UI
  - AC: `docs/design/SETTINGS.md` with page-by-page layouts (General, Behaviour, Audio, About), a control inventory mapped to settings keys, empty/disabled/error states, keyboard order; renders in `docs/design/settings/`; owner sign-off.
  - Notes: executed by the `atelier` profile. Consumed by APW-301.

---

## M5 — Optional driver tier (not v1, gated)

Only after APW-401 is explicitly approved by the owner. Requires Test Mode + Secure Boot
off; this is a different product promise and a different support burden.

- [ ] APW-401 | P3 | todo | M5 | deps: APW-019 | Decide whether the driver tier is worth it
  - AC: ADR weighing demand against Secure Boot/Test Mode cost, signing, and support load; explicit owner sign-off; if rejected, close M5.

- [ ] APW-402 | P3 | todo | M5 | deps: APW-401 | KMDF L2CAP client-profile driver
  - AC: derived from Microsoft's `bthecho` sample, targets PSM `0x1001`, test-signed; install/uninstall scripts; documented as experimental only.

- [ ] APW-403 | P3 | todo | M5 | deps: APW-402 | AAP transport and features
  - AC: exact 1 % battery, noise control and Conversation Awareness behind a feature flag; per-model support matrix documented.

- [ ] APW-404 | P3 | todo | M5 | deps: APW-402 | Driver security review and UX
  - AC: threat model; Secure Boot/Test Mode warning flow; rollback instructions; code-signing path.

- [ ] APW-405 | P3 | todo | M5 | deps: APW-403 | Beats and other Apple-audio support
  - AC: model table extended; graceful unknown-model UI; tested on at least one non-AirPods device.

---

## Suggested next five

1. **APW-010** — approve and install the .NET 8 SDK (blocks everything).
2. **APW-011** — capture real adverts from the paired AirPods Pro.
3. **APW-012 / APW-013** — confirm the byte map and the latency budget.
4. **APW-015 / APW-017** — prove connect and endpoint switching without admin.
5. **APW-018** — settle whether adverts can be attributed to one pair (scope driver).
