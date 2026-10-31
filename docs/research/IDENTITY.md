# Identity: can an advertisement be tied to one paired pair?

APW-018 · measured 2026-10-31 · Windows 11 build 26200 · MediaTek RZ616 · AirPods Pro (`0x200E`)

**Answer: no. Not with public APIs, and not without an Apple-supplied IRK.** The v1 product
must therefore assume one logical pair, group samples by payload rather than address, and say
so in the UI.

---

## 1. What we observed

One physical pair, four random addresses inside 150 s (`ANALYSIS-2026-10-31.md` §2). Asking
Windows to resolve each captured address returns a **synthetic placeholder per address**:

```
0x6BE0048EE8FD → name='Bluetooth 6b:e0:04:8e:e8:fd'  status=Disconnected
0x66F821F6B909 → name='Bluetooth 66:f8:21:f6:b9:09'  status=Disconnected
0x5CEB8743B678 → name='Bluetooth 5c:eb:87:43:b6:78'  status=Disconnected
0x6D6BCA3BE7E3 → name='Bluetooth 6d:6b:ca:3b:e7:e3'  status=Disconnected
```

Reproduce: `BleProbe.exe --resolve 0x6BE0048EE8FD 0x66F821F6B909 …`

The OS does not say "23's AirPods Pro". It manufactures a device object named after the
address, one per address. That is the whole finding: **there is no resolution step exposed to
applications.**

## 2. Why the OS cannot do better

- The AirPods are paired over **Classic** (A2DP/HFP/AVRCP). Windows has a link key for that.
- BLE privacy resolution needs an **IRK** established during LE pairing. AirPods do not LE-pair
  with Windows, and there is no API to create that bond on their behalf.
- Evidence: `--devices` reports **0 known BLE association endpoints** for this machine, while
  `BluetoothDevice.GetDeviceSelectorFromPairingState(true)` returns 2 Classic entries
  (the AirPods and a lamp). No LE bond ⇒ no IRK ⇒ nothing to resolve with.
- WinRT exposes `BluetoothLEAdvertisementReceivedEventArgs.BluetoothAddress` — the raw,
  rotating address — and nothing else. There is no "resolved address" property, and the
  association-endpoint layer has no entry for an unpaired advertiser.

## 3. Options considered

| Option | Viable? | Why |
|---|---|---|
| Read the true address from the advert | ❌ | Payload's MAC suffix is inside the AES block; key arrives over AAP |
| Resolve with the OS-stored IRK | ❌ | No LE bond exists; no API exposes resolution |
| Extract `MagicAccIRK` / `MagicAccEncKey` from an Apple device | ❌ | Rejected in ADR-008 — credential harvesting, ToS, and a driver |
| Drive AAP over L2CAP to ask the device | ❌ for v1 | Needs a KMDF driver (ADR-003) |
| Group by payload signature + continuity | ✅ | Works with the data we have; imperfect with two identical pairs |
| Single-pair lock, chosen once by the user | ✅ | Honest, simple, matches the actual use case |

## 4. Recommended v1 policy

**One logical device, addresses are ephemeral.**

1. Keep a single `DeviceRecord` per configured pair, keyed by an internal id — never by
   `BluetoothAddress`.
2. Maintain a small set of recently seen addresses mapped to that record, with a ~10-minute
   TTL. A new address with a matching payload is the *same* pair rotating, not a new device.
3. Merge rule: same product ID, battery values within one 10 % step, gap under ~90 s, and
   RSSI above a −85 dBm floor. Anything outside that is a separate candidate.
4. If two distinct payload signatures coexist, enter an **ambiguous** state: show the model
   and a neutral "AirPods nearby" card, no per-bud numbers, until the user picks one. Never
   silently show the wrong pair's battery.
5. While audio is connected, trust the Classic side (the connected device is unambiguous) and
   treat BLE as decoration.
6. Document "one pair at a time" in the README. Multi-pair is a later, explicitly-scoped
   feature (APW-208).

## 5. What remains untested

- Two different AirPods pairs in range at once — we only have one pair. The ambiguity path is
  therefore designed but unverified; it needs hardware we do not have.
- Whether a *LE-bonded* peripheral (a normal BLE device, not AirPods) gets a resolved identity
  from the OS. Irrelevant for AirPods, but it would confirm the "no bond, no IRK" explanation.

## 6. Consequences

- The popup can say "AirPods Pro" reliably, and per-bud numbers reliably **when only one pair
  is in range** (the normal case).
- Any UI that lists devices by address would be visibly broken; there must be none.
- APW-208 (multi-pair) inherits this as a hard constraint, not a bug to fix later.
