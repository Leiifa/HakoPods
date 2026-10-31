# DECISIONS

Architecture decision records. Newest at the bottom. Append, never rewrite: if a decision
changes, add a new ADR that supersedes the old one and mark the old one `Superseded by ADR-xxx`.

Format: **Status · Date · Context · Decision · Alternatives · Consequences**.

---

## ADR-001 — Build from scratch instead of forking an existing project

**Status:** Accepted · **Date:** 2026-10-31

**Context.** Several mature projects already do parts of this (see `docs/RESEARCH.md` §3).
The closest full-featured one, AirPodsDesktop, is GPL-3.0, C++/Qt 6, ~3.6k stars, 105 open
issues, and carries a large UI/protocol surface we would inherit. The closest feature match
under a permissive licence, Earlo, was created three weeks ago with a handful of stars.
WinPods (Rust/Tauri, MIT) stops at battery and tray; Near (Rust, MIT) is a week old.

**Decision.** Build a new application, reusing **documented protocol findings** rather than
source code. Keep the surface small: the eight features in `README.md`, one platform, one
UI.

**Alternatives considered.**
- *Fork AirPodsDesktop* — fastest to a feature-complete app, but GPL-3.0 is viral, the Qt/C++
  build is heavy, and every future change fights an unfamiliar 3.6k-star codebase.
- *Fork Earlo or Near* — MIT, right feature set, right stack (C#/Rust), but both are weeks
  old, near-zero community, and have no test suite; we would inherit unknown correctness in
  exactly the area (protocol parsing) where correctness matters.
- *Contribute upstream instead of building* — sensible if the goal were "improve the
  ecosystem"; it is not: the goal is a specific UX on this machine, under our control.

**Consequences.** We own protocol parsing and must verify it on hardware (M1). We can choose
our licence, UI stack and release cadence. Prior art is used as evidence, not as a dependency.

---

## ADR-002 — C# / .NET 8 with WPF for the client

**Status:** Accepted — owner approved the stack and the SDK install on 2026-10-31 · **Date:** 2026-10-31

**Context.** The app needs: WinRT Bluetooth LE access, P/Invoke into `bthprops.cpl`,
COM interop for audio endpoints, a tray icon, and a transparent, animated, never-focused
popup window. The dev machine has .NET **runtime** 8.0.19, Node 24, git, Python 3.14 — and
no Rust, no Visual Studio Build Tools, no .NET SDK.

**Decision.** .NET 8 + WPF, self-contained single-file publish, x64.

**Alternatives considered.**
- *WinUI 3 / Windows App SDK* — best native look (Mica, acrylic), but heavier deployment
  (runtime dependency or self-contained payload), more XAML friction for a no-activate
  layered popup, and slower agent iteration.
- *C++ / Win32* — smallest binary, no runtime, but high cost per feature and the most
  error-prone interop; not worth it for a utility of this size.
- *Rust / Tauri* — good binary size and a solid BLE crate ecosystem, but requires installing
  Rust + MSVC build tools, and WebView2 popups are awkward for always-on-top, no-focus,
  per-pixel-transparent windows.
- *Node / Electron* — 100 MB+ runtime for a tray app; rejected on footprint alone.
- *Python (as AirPodsTray does)* — fastest prototype, weakest distribution story and no
  first-class WinRT; would need PyWinRT plus a packaging toolchain.

**Consequences.** Requires installing the .NET 8 SDK (owner approval pending). WPF gives
direct WinRT access through CsWinRT, trivial P/Invoke, and a single-file exe. The trade-off
is that a truly native Windows 11 look needs manual theming work (ADR-007).

---

## ADR-003 — v1 is driver-free; AAP is a separate, gated tier

**Status:** Accepted · **Date:** 2026-10-31

**Context.** Apple's Accessory Protocol (AAP) runs over Bluetooth Classic L2CAP, PSM
`0x1001`. Windows exposes no user-mode L2CAP socket API; the driver stack only offers RFCOMM
through Winsock. Every Windows project that reaches AAP ships a KMDF client-profile driver
(usually derived from Microsoft's `bthecho` sample), which requires test-signing and
therefore **Test Mode with Secure Boot disabled** (WPods, MagicPods, `l2cap-windowsdriver`).

**Decision.** v1 uses only mechanisms available to a normal, non-elevated user process:
passive BLE advertisement scanning, `BluetoothSetServiceState`, WinRT device enumeration and
`IPolicyConfig`. No driver, no service, no admin. AAP-dependent features (exact 1 % battery,
noise control, Conversation Awareness, head gestures) are M5 and require a new ADR plus
explicit owner sign-off.

**Alternatives considered.**
- *Ship the driver in v1* — unlocks the "wow" features, but asks every user to weaken boot
  security, breaks on Managed/Corporate devices, and turns a utility into a security
  liability.
- *Ask users to install a third-party signed driver (MagicAAP)* — external dependency,
  closed source, same security posture, no control over compatibility.

**Consequences.** We must be honest in the UI and README about the 10 % battery granularity
and the absent ANC controls. In exchange: portable exe, no UAC, works on locked-down
machines, and no kernel attack surface.

---

## ADR-004 — MIT licence, clean-room implementation

**Status:** Accepted · **Date:** 2026-10-31

**Context.** The strongest prior art is GPL-3.0 (AirPodsDesktop, librepods, WPods, MyPods,
MagicPodsCore). Reading GPL source and re-implementing it is legally murky; copying it is
plainly viral. Protocol facts (byte layouts, PSM numbers, service UUIDs) are not
copyrightable, but expression is.

**Decision.** MIT for this project. Implement from published protocol documentation and our
own captures. Do not copy code, comments, identifiers or file structure from GPL projects.
Cite sources in `docs/RESEARCH.md`. Keep a `THIRD-PARTY-NOTICES.md` (APW-005).

**Alternatives considered.** GPL-3.0 (matches the ecosystem, but blocks reuse in closed
tools and discourages contribution from some users); Apache-2.0 (fine, but MIT is simpler
for a small utility).

**Consequences.** Slower start than copy-paste; cleaner provenance. Any future code
contribution from a GPL project requires an explicit relicensing decision.

---

## ADR-005 — Passive BLE advertisements are the primary state source

**Status:** Accepted · **Date:** 2026-10-31

**Context.** Three ways to learn AirPods state on Windows: (a) passive BLE advertisement
scanning, (b) GATT connection to a battery service, (c) AAP over L2CAP. (b) is unreliable —
AirPods generally do not expose the standard `0x180F` battery service (APW-020 will confirm);
(c) needs a driver (ADR-003).

**Decision.** Subscribe to `BluetoothLEAdvertisementWatcher`, filter Apple company ID
`0x004C` and message type `0x07`, parse the proximity-pairing payload. Treat connection
status from the WinRT/`bthprops` device APIs as a separate, slower signal and merge both in
one reducer.

**Alternatives considered.** Polling `BluetoothLEDevice`/`DeviceInformation` for battery
(no such property); relying on Windows Settings battery (not shown for AirPods); GATT
(APW-020).

**Consequences.** State is event-driven and cheap (no connection), but updates are only as
fresh as the advert cadence (20–30 s idle, immediate on events) and battery is quantised to
10 %. The reducer must model staleness explicitly.

---

## ADR-006 — Undocumented APIs are allowed only behind a narrow, replaceable adapter

**Status:** Accepted · **Date:** 2026-10-31

**Context.** Windows has no public API to set the default audio endpoint. The de-facto
solution is the undocumented `IPolicyConfig` COM interface (CLSID
`870AF99C-171D-4F9E-AF0D-E63DF40C2BC9`), used by Sunshine, SoundSwitch and others. It has
survived for a decade but is not contractual. Conversely, `BluetoothSetServiceState` is
documented (bthprops) but has sharp edges (returns `E_INVALIDARG` when the state already
matches).

**Decision.** `IPolicyConfig` is permitted, isolated in `Interop/AudioPolicyConfig.cs`
behind `IAudioEndpointSwitcher`, with a documented fallback (open the Windows sound settings)
if activation fails. `BluetoothSetServiceState` is the supported way to connect/disconnect;
wrap it in `IBluetoothConnector` with HRESULT-aware error handling.

**Alternatives considered.** UI automation of the Settings app (fragile, slow, visible);
`AudioDeviceCmdlets` PowerShell (extra runtime, same underlying API); asking the user to
switch manually (fails goal G5).

**Consequences.** One small, well-tested interop surface can break on a future Windows
build; failures degrade to a clear message instead of a crash. Tests must fake the
interface, not the COM object.

---

## ADR-007 — Custom popup window instead of Windows toast notifications

**Status:** Accepted · **Date:** 2026-10-31

**Context.** The macOS experience is a bespoke, animated card with device art and per-bud
battery. Windows toast notifications are OS-chrome, cannot be positioned, require an AUMID
and packaging for reliable behaviour, and disappear into the Action Center.

**Decision.** A borderless WPF window: `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`, topmost,
per-pixel transparency, positioned against the taskbar on the monitor under the cursor,
DPI-aware, dismissed on click-away/Esc/timeout.

**Alternatives considered.** Toast notifications (wrong shape, wrong placement); a WinUI 3
flyout (heavier stack); a tray balloon (legacy look).

**Consequences.** We own accessibility, focus behaviour and multi-monitor math — all
explicit tasks (APW-108, APW-307). In exchange the popup looks and behaves like the product
it imitates.

---

## ADR-008 — 10 % battery is the v1 truth; Apple keys are out of scope

**Status:** Accepted · **Date:** 2026-10-31

**Context.** The cleartext advert gives battery in 10 % steps. Exact 1 % values live in an
AES-128-ECB encrypted block whose key is delivered over AAP; some projects also extract the
`MagicAccIRK`/`MagicAccEncKey` from an Apple device's keychain. Both routes require either
a driver or harvesting credentials from an Apple account.

**Decision.** Ship 10 % steps, clearly labelled. Do not extract, request or store Apple
keys. Do not implement Apple-ID-based pairing material. Revisit only if the owner
explicitly reopens it with a new ADR.

**Alternatives considered.** Driver-based AAP for exact values (ADR-003 defers); asking the
user for keychain material (unacceptable security and ToS posture); interpolating between
adverts to fake 1 % (dishonest, rejected).

**Consequences.** Battery display may differ from an iPhone by up to one step. Low-battery
warnings trigger at 10 % with a cooldown, not at an exact percentage.

---

## ADR-009 — Plain-Markdown task tracking with a strict grammar and a local checker

**Status:** Accepted · **Date:** 2026-10-31

**Context.** Work moves between Hermes, OpenCode, Claude Code, Codex, GUI and CLI. Every
agent-specific tracker (OpenCode sessions, Cursor rules, Claude todos) is invisible to the
others. The owner asked explicitly for tool-independent tracking.

**Decision.** `TODO.md` is the only task store, using a documented one-line grammar
(`AGENTS.md` §4) with IDs, priority, status, milestone, dependencies, optional owner, and
acceptance criteria. `PROGRESS.md` carries narrative state. `scripts/check_tasks.py`
(stdlib-only Python) validates uniqueness, references, cycles, checkbox/status agreement
and the presence of acceptance criteria.

**Alternatives considered.** GitHub Issues (requires a remote and network); a JSON/YAML
task file (unreadable in a diff, awkward to edit by hand); TodoWrite-style agent tools
(session-scoped, invisible to the next agent); a project-management SaaS (overkill,
offline-hostile).

**Consequences.** Humans and agents edit the same file; merge conflicts are possible but
rare with one active task per agent. The checker is the guardrail — run it before stopping.

---

## ADR-010 — Local-first: no telemetry, no network, no accounts

**Status:** Accepted · **Date:** 2026-10-31

**Context.** A utility that scans Bluetooth traffic and manages audio devices must be
trustworthy by default. Users of AirPods on Windows are already suspicious of telemetry.

**Decision.** The app performs no network I/O. Settings and logs live under
`%LOCALAPPDATA%\AirPodsWindows`. Diagnostics export is user-initiated and local. No
analytics, no crash upload, no update check in v1 (manual update via release page).

**Alternatives considered.** Opt-in analytics (still requires a network stack and a privacy
policy); auto-update (Squirrel/Velopack) — deferred to M4+ if ever.

**Consequences.** No usage data to prioritise features; support relies on user-provided
diagnostic zips. Smaller attack surface and a clean privacy story.

---

## ADR-011 — Implementation is gated on the M1 spike

**Status:** Accepted · **Date:** 2026-10-31

**Context.** The entire design rests on unverified hardware behaviour: advert cadence,
whether the lid bit is reliable on this firmware, whether the paired AirPods can be
identified from a rotating address, and whether `BluetoothSetServiceState` really connects
without elevation.

**Decision.** No product code in `src/` until APW-012, APW-015, APW-017 and APW-018 pass and
APW-019 records a go/no-go. The spike lives in `spike/` and is disposable.

**Alternatives considered.** Prototype-first and fix later (burns weeks on a UI whose data
source may be ambiguous); trust the prior-art reports (they run on different adapters,
firmware and Windows builds).

**Consequences.** A short delay before visible progress, and a real possibility that a
feature is cut early. That is the point: the spike is cheaper than the rework.

**Outcome (2026-10-31): GO.** The owner reviewed `docs/research/SPIKE-RESULTS.md` and approved
implementation on the measured evidence: 127 advertisement packets decoded, connect 5/5 in
≈3.2 s without elevation, HFP suppression verified, endpoint switching 4 ms, RSSI calibrated
at −72 dBm, and identity definitively unavailable. Scope is held exactly as specified —
driver-free, one pair, 10 % battery steps, popup budget ≤ 1.5 s, **ANC excluded** by explicit
decision (APW-401 stays closed unless the owner reopens it). APW-101 started the same day.

---

## ADR-012 — Visual design is owned by the `atelier` profile

**Status:** Accepted · **Date:** 2026-10-31

**Context.** The product's value is largely visual: a popup that has to feel native, tray
glyphs that read at 16 px, a settings surface that matches Windows 11. Coding agents optimise
for "it compiles and looks plausible", which produces inconsistent spacing, invented colours
and drift between screens. The owner already runs a separate Hermes profile, `atelier`, for
design work.

**Decision.** GUI design is a separate, owned deliverable: the `atelier` profile produces
specs and renders in `docs/design/` (APW-115 popup, APW-116 tray icons, APW-311 settings).
Coding agents implement those specs and must not invent styling; implementation tasks depend
on their design task (`APW-108` ← `APW-115`, `APW-301` ← `APW-311`).

**Alternatives considered.** Let the implementing agent design as it codes (fast, but the
result is inconsistent and unreviewable); adopt a third-party design system wholesale
(loses the native Windows feel); copy Apple's UI assets (trademark exposure, and the point is
to feel at home on Windows, not to clone macOS pixel-for-pixel).

**Consequences.** Implementation waits on a design artefact; the spec is reviewable before code
exists, and portable across agents. Handoff command and artefact contract live in
`AGENTS.md` §11 and `docs/design/README.md`.

**Outcome (2026-10-31).** The owner reviewed two directions. Direction A was approved — the
one that looks like the AirPods experience itself rather than Windows chrome: Inter bundled
with the app, full-width green battery rails that turn red at ≤ 10 %, white/near-black
surfaces, shadow-defined edge. This supersedes the "Windows-native, not a macOS clone" wording
above: the popup deliberately imitates the source experience, while window behaviour (no
focus steal, taskbar-relative placement, DPI handling) stays native. Charging bolts were
redrawn by the owner's request — the original path self-intersected and spilled over the buds.
