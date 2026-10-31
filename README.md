# HakoPods

*The AirPods experience, on Windows. 箱 — the case.*

HakoPods is an independent, open-source tray app. It is not affiliated with, endorsed by, or
sponsored by Apple. "AirPods" is a trademark of Apple Inc. and is used here only to describe
which devices the app works with.

Bring the *native* AirPods experience to Windows: a popup when you open the case, live
left / right / case battery, automatic connect, ear-detection auto-pause, and audio that
follows you between your headphones and your speakers.

> **Status: M2 in progress.** The feasibility spike closed with a **go** (ADR-011); the
> solution skeleton builds and its smoke tests pass. No product behaviour yet — the parser
> is next. See [`PROGRESS.md`](PROGRESS.md) for the live state.

---

## Goals

| # | Goal | Target |
|---|------|--------|
| G1 | Case-open popup with left / right / case battery and charging state | Driver-free, ≤ 1.5 s after lid opens |
| G2 | System-tray presence: battery at a glance, connect / disconnect | Driver-free |
| G3 | Automatic connection when the case opens near the PC (opt-in) | Driver-free |
| G4 | Ear detection → pause / resume media | Driver-free (10–30 s worst case) |
| G5 | Audio routing: AirPods become the default output on connect, previous device restored on disconnect | Driver-free |
| G6 | "Stereo always": stop Windows from dropping to HFP call quality when an app grabs the mic | Driver-free |
| G7 | Low-battery warnings | Driver-free |
| G8 | Noise control / exact 1 % battery / Conversation Awareness | **Deferred** — needs a kernel driver (M5, opt-in) |

## Non-goals (v1)

- Apple-ID-bound features: automatic switching between Apple devices, Find My, firmware updates.
- Spatial audio / head tracking, hearing-aid mode, heart-rate.
- Any kernel driver, service, or admin-required installation.
- Telemetry, accounts, or network traffic.

## Feasibility at a glance

Full analysis, sources and caveats: [`docs/RESEARCH.md`](docs/RESEARCH.md).

| Feature | Verdict | Evidence |
|---|---|---|
| Battery (10 % steps) | ✅ **verified** | 127 packets captured; cleartext in Apple's BLE proximity-pairing advert |
| Battery (exact 1 %) | ⚠️ deferred | Encrypted advert; key only obtainable over AAP (needs a driver) |
| Case-open popup | ✅ **verified** | Lid bit tracked across three open/close cycles; ~1 s advert cadence while awake |
| Ear detection | ✅ **verified** | In-ear bit flipped live during a capture |
| Auto-connect | ✅ **verified** | `BluetoothSetServiceState` connects 5/5 in ≈3.2 s, no admin |
| Audio switching | ⚠️ partial, **verified** | Default endpoint switches in 5 ms (`IPolicyConfig`); macOS-style hand-off is impossible |
| Noise control / ANC | ❌ not without a driver | AAP over L2CAP, which Windows does not expose to user mode |
| Automatic device switching | ❌ not possible | Requires Apple's cloud/continuity layer |

Measured on this machine on 2026-10-31 — details in
[`docs/research/SPIKE-RESULTS.md`](docs/research/SPIKE-RESULTS.md).

## Prerequisites

**To run (once implemented)**

- Windows 10 2004 (19041) or newer; Windows 11 recommended. x64.
- A Bluetooth adapter with Bluetooth LE (4.0+; 5.0+ recommended).
- AirPods paired once through Windows Settings → Bluetooth & devices.
- No admin rights, no driver, no installer required (portable `.exe`).

**To build**

- .NET 8 SDK — 8.0.427 installed on the dev machine on 2026-10-31.
- Git 2.53.
- Optional: Inno Setup or MSIX tooling for the installer milestone.

## Build & run

The application skeleton (no behaviour yet):

```
dotnet build HakoPods.sln -c Release
dotnet test  HakoPods.sln -c Release
```

The feasibility probes that produced the measurements are separate, disposable projects:

```
dotnet build spike/ble-probe/BleProbe.csproj -c Release
./spike/ble-probe/bin/Release/net8.0-windows10.0.19041.0/BleProbe.exe --scan 30 --raw
```

It prints the Bluetooth adapter's capabilities, the radio state and a timed scan of Apple
proximity-pairing advertisements. `spike/` is disposable and git-ignored; it is not the
product.

Planned product layout (task `APW-101`):

```
src/
├─ HakoPods.Core/        # domain, BLE parser, Win32 interop, no UI
├─ HakoPods.App/         # WPF tray app + popup
└─ HakoPods.Core.Tests/  # xUnit, parser fixtures
```

## Documentation map

| File | What it is |
|---|---|
| [`AGENTS.md`](AGENTS.md) | **Read first if you are an agent.** Working agreement, task protocol, handover rules |
| [`TODO.md`](TODO.md) | Single source of task state, grouped by milestone |
| [`PROGRESS.md`](PROGRESS.md) | Where the project is *right now*: current task, blockers, next steps |
| [`DECISIONS.md`](DECISIONS.md) | Architecture decision records (ADR-001 …) |
| [`docs/RESEARCH.md`](docs/RESEARCH.md) | Prior art, Windows APIs, AirPods protocol, feasibility, licensing |
| [`CHANGELOG.md`](CHANGELOG.md) | Meaningful changes, newest first |

## Working on this project

This project is designed to be handed between coding agents (Hermes, OpenCode, Claude Code,
Codex, …) and humans without losing state. Before any change:

1. Read `PROGRESS.md`, then `TODO.md`, then `DECISIONS.md`.
2. Claim exactly one task by setting its status to `doing` and naming an owner.
3. Update `TODO.md` **and** `PROGRESS.md` when you start, pause, or finish.
4. Run `python scripts/check_tasks.py` before you stop.

Task tracking is deliberately plain Markdown: no agent-specific files, no proprietary
formats, no tool lock-in.

## License

MIT — to be added with the repository initialisation (`APW-004`). Third-party research is
cited, not copied; GPL-licensed prior art is used as documentation only. See
`docs/RESEARCH.md` §10.

AirPods, Apple and Beats are trademarks of Apple Inc. This project is independent and not
affiliated with, sponsored or endorsed by Apple.
