# PROGRESS

> The single place that answers "where is this project right now?". Update it when you
> start and when you stop. Keep it short, factual and dated. History lives in
> `CHANGELOG.md`; task state lives in `TODO.md`.

**Last updated:** 2026-10-31 15:05 (UTC+7) · **By:** Hermes
**Phase:** M2 at 11 of 16 — tray, popup and lid trigger shipped and confirmed; settings and diagnostics remain
**Current milestone:** M2
**Current task:** APW-206 is in review — needs a fullscreen check (a video or game full-screen, then open the lid; no card should appear)

---

## Snapshot

| | |
|---|---|
| Tasks done | 31 of 55 |
| In review | 0 |
| Blocked | 0 |
| Product code | parser, watcher, reducer, advertiser table, settings, logging, selection, connection, audio, tray — 91 tests green |
| Toolchain | .NET SDK 8.0.427 installed |
| Repo | git initialised 2026-10-31 — commit `de6f6d6`, no remote yet |

## What exists

- Documentation set: `README.md`, `AGENTS.md`, `TODO.md`, `PROGRESS.md`, `DECISIONS.md`
  (ADR-001…012), `CHANGELOG.md`, `docs/RESEARCH.md`, `docs/design/README.md`.
- `spike/ble-probe` — throwaway .NET console probe (git-ignored). Builds and runs; prints
  adapter capabilities, radio state and a timed advertisement scan.
- `NuGet.config` — pins nuget.org, because the machine's user-level NuGet config has **no
  package sources** and restore fails with `NU1100` without it.
- Task system: 56 tasks, M0–M5, validated by `scripts/check_tasks.py`.
- `docs/research/IDENTITY.md` — why an advert cannot be tied to a paired pair, and the v1
  policy that follows.
- `docs/research/SPIKE-RESULTS.md` — draft spike report, measurements 1–4 done, 5–10 open.
- `spike/bt-connect` — Classic connect/disconnect probe: `--status`, `--a2dp on|off`,
  `--hfp on|off`, `--cycles N`.
- `spike/audio-endpoint` — `IPolicyConfig` probe: enumerate, switch, verify, restore.

## What the captures proved (2026-10-31)

- **The driver-free data path works.** 127 packets from the paired AirPods Pro across two
  captures, all Apple `0x004C` / type `0x07` — no pairing, no connection, no admin.
- **Byte map confirmed** on hardware: product ID little-endian (`0x200E`), status, lid,
  battery, charging and connection offsets exactly as documented; three clean lid
  open/close cycles tracked without error.
- **Unknown encodings are real:** `0xF` (bud battery) and case `> 10` appeared live, so the
  parser must model "unknown" rather than clamp.
- **Addresses rotate:** one pair, four addresses inside 150 s, overlapping windows, identical
  product ID and battery. An address is not an identity — direct input to APW-018.
- **Cadence:** median 977 ms while awake, silent within ~1–2 s of the lid closing, worst
  observed silence 24.5 s. The ≤ 1.5 s popup is achievable but not guaranteed; the trigger
  needs a post-silence burst rule.
- The earlier "zero advertisements" result was environmental (AirPods asleep in a closed
  case), not a stack failure — the watcher reported `Started` throughout.
- **Identity is settled, negatively.** Asking Windows to resolve each captured random address
  returns a synthetic device named after the address, never the paired name; the machine has
  **0 LE bonds**, so there is no IRK to resolve with. v1 is therefore one logical pair,
  grouped by payload signature and continuity, with an explicit ambiguous state.
- **Connect works without admin.** A2DP enable via `BluetoothSetServiceState` connects in
  ≈3.2 s, 5/5, from a non-elevated process. Disable removes the audio endpoint but leaves
  `fConnected=True` — the endpoint is the only trustworthy signal.
- **Mic policy works.** Disabling HFP and reconnecting leaves only the A2DP endpoint, so
  Windows cannot downgrade playback to call quality; the AirPods mic is unavailable while off.
- **Endpoint switching works.** `IPolicyConfig` sets the default render device in **5 ms** per
  role (+16 ms for all three) and restores it, verified by re-query, without elevation.
- **No GATT fallback exists.** `GetGattServicesAsync` on the live AirPods address returns
  `Unreachable` with zero services — BLE adverts are the only driver-free state source.
- **Signal strength separates cleanly.** 101 packets across five positions: −47 dBm at arm's
  length, −65 at ~2 m, −82 at 5 m. Rule: accept ≥ −72, release < −78, floor −85.
- **The popup-latency metric was wrong.** Measuring "time since the previous packet" includes
  the entire closed period, so it cannot yield a wake latency. Eight trials gave 0.3–8.4 s,
  but that is a bound, not the number the AC asked for.

## Blockers

| ID | Blocker | Needs | Impact |
|---|---|---|---|
| B3 | Git not initialised | owner decision on repo + copyright holder | APW-004 / APW-005 pending |
| B7 | `atelier` provider rate-limited (HTTP 429; resets in ~25 days) | switch that profile's model, or wait | APW-311 (settings design) blocked; 115/116 artefacts already landed |
| B8 | ~~APW-115/116 await owner sign-off~~ | resolved 2026-10-31 — direction A approved | — |

## Next steps (in order)

1. **APW-107** — tray icon and menu against the signed-off icon set (Connect/Disconnect now have a real backend).
2. **APW-108/109** — popup window and trigger.
3. **APW-203** — route audio on connect (needs the endpoint layer).
4. **Manual check** — one suspend/resume to exercise the watcher's real-world restart.

## Handoff notes

**Core is complete and tested.** Parse → stream → state → settings → logging → device
selection, 46 tests, plus a ten-minute live soak with zero packet loss. Safe to build on.

**Two things wait on the owner:** sign off the design artefacts (B8), and decide on git (B3).

**One thing was never exercised for real:** the watcher's auto-restart after sleep or a radio
toggle. Unit-tested only — suspend the machine once and confirm it recovers.

**`atelier` is unavailable** until its provider limit resets (B7). Do not plan design work
through that profile for the next few weeks; either switch its model or have the coding agent
follow the existing specs exactly. Never invent styling to fill the gap — ADR-012.

Run the probe with:

```
./spike/ble-probe/bin/Release/net8.0-windows10.0.19041.0/BleProbe.exe --scan 30 --raw
```

If `total=0` persists while the AirPods are awake, stop and investigate the adapter before
building any UI on top of this data source.

## Recently completed

- 2026-10-31 — **APW-106 (code)**: `Win32BluetoothNative` + `BluetoothConnectionManager` —
  serialised, retried, time-boxed, idempotent. Live enable 3014 ms, disable 3135 ms, both rc=0.
  Found that "already enabled" returns `87` in 2 ms and that `rc=0` is not "audio live".
- 2026-10-31 — **APW-115 / APW-116 signed off**: direction A approved (Inter bundled,
  green/red rails kept, shadow edge). Charging bolts redrawn after review; 8 charging PNGs
  regenerated. Tray and popup unblocked.
- 2026-10-31 — **APW-115 / APW-116 (artefacts)**: popup spec with four renders, icon set with
  40 PNGs and 10 SVGs, all verified; awaiting owner sign-off.
- 2026-10-31 — **APW-105**: paired-device discovery verified against the real system (2
  devices), selection persisted by address; 7 tests, 46 total green.
- 2026-10-31 — **APW-103**: 10-minute soak, 499/499 packets, zero loss vs the reference
  reader, 99 % emit ratio, flat memory. See `docs/research/SOAK-2026-10-31.md`.
- 2026-10-31 — **APW-111 / APW-112**: atomic JSON settings, rolling file log and a local
  diagnostics zip. 8 new tests, 39 total green.
- 2026-10-31 — **APW-104**: immutable `DeviceState`, pure `Apply`/`Expire`, 60 s staleness,
  8 tests. The core pipeline (parse → stream → state) is now complete.
- 2026-10-31 — **APW-103 (implementation)**: watcher service with dedupe, auto-restart and an
  async stream; 9 new tests, 23 total green. Soak deferred.
- 2026-10-31 — **APW-102**: parser with 12 fixture tests, 100 % line and branch coverage.
- 2026-10-31 — **APW-019**: owner signed **GO**; ADR-011 records the outcome and the ANC
  exclusion. M1 closed 11/11.
- 2026-10-31 — **APW-101**: solution skeleton builds (0 warnings) and tests pass (2/2).
- 2026-10-31 — **APW-013**: AC amended (the original metric was unmeasurable); popup budget
  set at ≤ 1.5 s with the owner's Bluetooth-tile observation as corroboration.
- 2026-10-31 — **APW-014**: RSSI calibrated; −72 dBm accept / −78 release.

- 2026-10-31 — **APW-017 / APW-020**: endpoint switch verified against the real AirPods
  endpoint (4 ms / +12 ms); GATT probe returns `Unreachable`, no battery service.
- 2026-10-31 — **APW-017 (mechanism)**: `IPolicyConfig` switches the default endpoint in
  5 ms; AirPods-specific re-run pending reconnection.
- 2026-10-31 — **APW-015 / APW-016**: connect 5/5 in ≈3.2 s non-elevated; HFP suppression
  leaves A2DP only. `spike/bt-connect` written and verified.
- 2026-10-31 — **APW-018**: identity answered negatively, with evidence; v1 policy written.
- 2026-10-31 — **APW-019 draft**: `docs/research/SPIKE-RESULTS.md` assembled from measurements.
- 2026-10-31 — **APW-011 / APW-012**: two captures, 127 packets, byte map validated on
  hardware, analysis in `docs/captures/ANALYSIS-2026-10-31.md`.
- 2026-10-31 — **APW-010**: .NET SDK 8.0.427 installed via winget; `spike/ble-probe` builds
  and runs; project `NuGet.config` added.
- 2026-10-31 — **ADR-012**: GUI design delegated to the `atelier` profile; design tasks
  APW-115 / APW-116 / APW-311 added, implementation tasks re-pointed at them.
- 2026-10-31 — Research phase: 14 prior-art projects, Windows API inventory, BLE packet
  analysis, licensing review, build-vs-fork verdict (`docs/RESEARCH.md`).
- 2026-10-31 — Documentation set, task tracker and agent working agreement (`AGENTS.md`).
