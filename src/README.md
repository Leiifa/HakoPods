# src — planned solution layout

The skeleton exists (APW-101) and builds; behaviour is added task by task. This file fixes the
shape so agents do not invent competing structures. See ADR-002 and ADR-011.

```
src/
├─ AirPods.Core/         # net8.0 — no UI, no WPF references
│  ├─ Advertisements/    # proximity-pairing parser, model table, fixtures
│  ├─ Devices/           # paired-device discovery, identity policy
│  ├─ State/             # DeviceState model + pure reducer
│  ├─ Bluetooth/         # IBluetoothConnector, watcher service
│  ├─ Audio/             # IAudioEndpointSwitcher, media control (SMTC)
│  ├─ Settings/          # settings store, schema, migration
│  └─ Interop/           # P/Invoke + COM only (bthprops, IPolicyConfig, MMDevice)
├─ AirPods.App/          # net8.0-windows — WPF tray app
│  ├─ Tray/              # NotifyIcon, menus, tooltip
│  ├─ Popup/             # borderless no-activate window, animation, placement
│  ├─ SettingsUi/        # settings pages
│  └─ App.xaml           # composition root, single instance, DI
└─ AirPods.Core.Tests/   # xUnit — parser fixtures, reducer, settings, fake interop
```

## Dependency direction

```
AirPods.App ──▶ AirPods.Core
AirPods.Core.Tests ──▶ AirPods.Core
```

`Core` never references `App`. `Interop` is never referenced directly by UI code — only
through the interfaces in `Bluetooth/` and `Audio/`, which are fakeable in tests.

## Rules

- The parser is a pure function over bytes. No `Windows.*` types in `Advertisements/`.
- State transitions live in one reducer; no ad-hoc flags in view models.
- Hardware-free tests must cover every branch that can be driven by data.
- Hardware-dependent behaviour gets a written manual test script under `docs/manual-tests/`.

## Disposable

`spike/` (created in APW-010) sits outside `src/`, is git-ignored, and must never be
referenced by product code.
