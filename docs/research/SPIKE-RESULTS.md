# M1 spike results

> **Status: 10 of 11 M1 tasks closed, 0 blocked.** Every measurement is done; APW-019 (the
> go/no-go itself) is the only open task. That block is deliberately unsigned — it is the
> owner's decision, not the agent's.

Hardware: Windows 11 build 26200 (25H2), x64, MediaTek RZ616, AirPods Pro (`0x200E`),
paired over Classic, not connected during capture.

---

## 1. Results so far

| # | Question | Measurement | Verdict |
|---|---|---|---|
| 1 | Do Apple adverts reach a non-elevated .NET process? | 127 packets over two captures (59/60 s, 68/150 s), all `0x004C`/`0x07` | ✅ yes |
| 2 | Is the documented byte map right? | Product ID LE `0x200E`; status, lid, battery, charging, connection at documented offsets; 3 lid cycles tracked | ✅ yes |
| 3 | Are unknown values real? | `0xF` bud nibble and case `> 10` observed live | ✅ yes — parser must model Unknown |
| 4 | Can an advert be tied to one pair? | 4 addresses/150 s; `FromBluetoothAddressAsync` invents a placeholder per address; 0 LE bonds | ❌ no — see `IDENTITY.md` |
| 5 | Does connect/disconnect work without admin? | 5/5 enable, ~3.2 s end-to-end; disable removes the A2DP endpoint 5/5; `elevated=False` | ✅ yes |
| 6 | Does HFP suppression keep stereo? | HFP off + reconnect leaves only `Headphones`; both `Headset` endpoints gone | ✅ yes |
| 7 | Does `IPolicyConfig` switch the endpoint? | 4 ms eConsole / +12 ms all roles, against the real AirPods endpoint; switch + restore verified | ✅ yes |
| 8 | Is there a GATT battery service? | `GetGattServicesAsync` → `Unreachable`, 0 services, no `0x180F` | ❌ absent — adverts are the only source |
| 9 | What RSSI means "near"? | 0.5 m −47 dBm, ~2 m −65, 5 m −82; clean 18 dB separation | ✅ yes — threshold −72 dBm |
| 10 | Is the ≤ 1.5 s popup reachable? | cadence ~1 s; closed→open gaps 0.3–8.4 s, but each gap contains the closed period | ⚠️ **not measurable** by passive observation |

## 2. The numbers

| Metric | Value |
|---|---|
| Packets captured | 127 (59 + 68) |
| Distinct addresses for one pair | 4 in 150 s |
| Inter-packet median | 977 ms |
| Inter-packet p95 | 4 126 ms |
| Longest silence | 24 517 ms |
| RSSI at close range | −39 dBm |
| Stale/phantom RSSI | −127 dBm (must be floored) |
| Lid transitions captured | 3 open, 3 close |
| Build/runtime | .NET SDK 8.0.427, runtime 8.0.33 |

### Connect and disconnect (APW-015)

| Step | Result |
|---|---|
| Process elevation | `elevated=False` — no UAC, no admin manifest |
| `BluetoothSetServiceState(A2DP, ENABLE)` | `rc=0`, blocking call ~3 010 ms, endpoint present ~250 ms later |
| End-to-end connect | ≈ 3.2 s, 5/5 cycles |
| `BluetoothSetServiceState(A2DP, DISABLE)` | `rc=0`; `Headphones (AirPods)` endpoint disappears, 5/5 |
| Repeat disable | `rc=1168` (`ERROR_NOT_FOUND`) — already disabled |
| `BLUETOOTH_DEVICE_INFO.fConnected` | **stays `True` after disconnect** — the ACL persists |

**The trap:** `fConnected` is not a disconnect signal. It tracks the ACL link, which Windows
keeps up. The user-visible truth is the audio endpoint: present = connected, absent =
disconnected. The product must watch endpoints (MMDevice), not this flag.

### Microphone policy (APW-016)

With HFP **enabled** and a reconnect, three endpoints exist:

```
Headphones (… AirPods)          ← A2DP, stereo
Headset    (… AirPods)          ← HFP
Headset    (… Hands-Free AG)    ← HFP
```

With HFP **disabled** and a reconnect, only `Headphones` remains. No HFP endpoint means no
path for Windows to downgrade playback when an app opens the mic — the "stereo always"
mechanism works at the profile level. Re-enabling and reconnecting restores both Handset
endpoints.

Cost, and it must be stated in the UI: while HFP is disabled the AirPods microphone is
unavailable and calls fall back to the PC microphone.

### Endpoint switching (APW-017)

`spike/audio-endpoint` activates the undocumented `IPolicyConfig` COM interface (CLSID
`870AF99C-171D-4F9E-AF0D-E63DF40C2BC9`) and switches the default render device through the
MMDevice API, then asks the OS again to confirm:

| Call | Latency | Result |
|---|---|---|
| `SetDefaultEndpoint(id, eConsole)` | **5 ms** | `hr=0x0`, default changed |
| `eConsole` + `eMultimedia` + `eCommunications` | **+16 ms** | all three roles follow |
| Restore original endpoint | ~5 ms | verified by re-query |

The interface is undocumented but behaves exactly as prior art reports on build 26200, and
needs no elevation. The run switched between the machine's two active render endpoints because
the AirPods were disconnected; asserting it against the `Headphones (AirPods)` endpoint is a
10-second re-run once they are connected.

### Signal strength (APW-014)

101 packets over 110 s while the owner walked the open case through five positions:

| Position | Median RSSI | Samples |
|---|---|---|
| Arm's length (0.5 m) | **−47 dBm** | 41 (out and back) |
| ~1.5 m | −60 dBm | 10 |
| ~2 m | −65 dBm | 20 |
| 5 m | **−82 dBm** | 30 |
| Behind a wall | merged with 5 m (−82) | ~10 |

Recommendation: **accept as "near" at ≥ −72 dBm, release at < −78 dBm** (6 dB hysteresis), and
floor anything below −85 dBm, which also drops the −127 dBm phantom reports. The 18 dB gap
between arm's length and across-the-room is wide enough that tuning is not delicate.

Caveats: 1 m was under-sampled (10), and a wall could not be distinguished from ~5 m — both
land near −82. That is acceptable, because the rule is distance-based, not wall-aware.

### Popup latency (APW-013) — the metric does not work as written

Eight closed→open transitions across two captures, measured as "time since the previous
packet": 0.3, 1.2, 3.6, 4.1, 5.7, 5.8, 8.4, 24.5 s (p50 4.9 s).

**That number is not the latency.** The previous packet is the one sent just after the lid
*closed*, so every gap contains the whole closed period plus the wake-up. Passive observation
cannot separate them: the first packet carrying `lid = open` is the only timestamp available,
and it is also the event being measured.

What can be stated:

- While awake, adverts arrive about once per second (median 977 ms), so delivery is fast once
  the device is transmitting.
- Best observed wake-to-packet gaps: 0.3 s and 1.2 s; worst 8.4 s after a longer silence.
- A true lid-open → advert latency needs a second clock: a screen recording of the lid action,
  or a hardware trigger. Until then, no honest p95 can be quoted.

**Owner observation, 2026-10-31 (not instrumented):** watching the Windows Bluetooth tile in
Quick Settings, opening the lid shows *connected* in about **0–1 s**, while closing the lid
takes **3–4 s** to show *not connected*. That is the Classic link, not the advert stream, but
it establishes the thing the captures could not: the AirPods wake and connect fast on lid
open. It also bounds the *disconnect* side, which the UI must not treat as instant — a
"disconnected" state should lag reality by a few seconds, or be driven by the endpoint rather
than the tile.

**Working conclusion:** a popup appearing within ~1 s of the lid opening is realistic, 1.5 s
is a safe budget, and anything beyond ~3 s should be treated as "the advert was missed" rather
than as normal latency. Design accordingly: optimistic timing, graceful absence, never a stale
popup.

## 3. What this settles

### Measurement 11 — `BluetoothSetServiceState` return codes (2026-10-31, APW-106)

| Call | Result | Duration |
|---|---|---|
| `ENABLE` while the service is already enabled | `87` (`ERROR_INVALID_PARAMETER`) | **~2 ms** |
| `DISABLE` | `0` | 3135 ms |
| `ENABLE` immediately after a disable | `0` | 3014 ms |

Three things follow, and they contradict the "just return the HRESULT" assumption:

1. `87` on enable means **already enabled**, not "bad argument". It returns instantly, before any
   radio I/O. A manager that treats it as failure reports an error on every connect attempt
   against a sleeping pair.
2. A genuine enable blocks for ~3 s. Duration is the only cheap discriminator between the two.
3. `rc=0` means "service enabled", **not** "audio is live": `fConnected` stayed `false` after a
   successful enable, and only rose once the devices woke. The UI must therefore treat connect
   as *requested* and confirm via the audio endpoint.

### Measurement 12 — repeated disconnect→connect cycles (2026-10-31, APW-106)

Run A, 10 cycles, 20 s wait: **2/10** verified (wake 14 602 ms, 22 528 ms); 8 cycles never
came back inside 20 s.

Run B, 5 cycles, 45 s wait: **2/5** verified (3 826 ms, 3 008 ms). Cycles 3–5 failed
differently — `BluetoothGetDeviceInfo` stopped finding the device at all, so every disconnect
returned `0x490` (`ERROR_NOT_FOUND`) three times, and the "connect" that followed resolved to
the already-enabled path in 0 ms.

Run C, 10 cycles, pair awake, after the `fConnected` fix: **10/10**. Every enable did real work
(3.0 s, so A2DP had genuinely gone down) and the pair was online after each cycle —
median **3012 ms**, max **3015 ms**. This is the run that closes APW-106.

Note the metric change: Run C does not wait for `fConnected` to drop on disconnect, because it
never does while HFP holds the link. Proof that A2DP went down is that the *next* enable takes
~3 s instead of returning `87` in ~2 ms.

Three conclusions, all of which matter for the tray and trigger design:

1. **`rc=0` is "accepted", never "connected".** All fifteen connect calls returned `0` in
   ~3.0 s, including the ones after which nothing happened. Duration is not a success signal
   either — the 0 ms cases were the already-enabled path.
2. **The pair sleeps after roughly two cycles of activity**, and once asleep it drops out of
   the radio's device list. `BluetoothGetDeviceInfo` then returns `ERROR_NOT_FOUND`, which is
   indistinguishable from "not paired" through that API — hence the manager's wording is now
   "not reachable (asleep, out of range, or not paired)".
3. **Reconnect loops are not a valid way to verify this feature.** The real reconnect is
   lid-driven and takes 0–1 s (owner observation, APW-013). Forcing disconnect→connect makes
   the pair sleep and then fight the verification. Confirmation belongs on the audio endpoint,
   not on a retry loop.

### Measurement 13 — `fConnected` is per device, not per service (2026-10-31)

Observed live: Bluetooth showed **"23's AirPods Pro · Connected mic"**, the sound-output flyout
listed only HDMI and speakers, and `Get-PnpDevice -Class AudioEndpoint` returned
`Headset (23's AirPods Pro)` but **no `Headphones (…)`** entry.

That is the HFP-only state: the pair is connected over hands-free, A2DP is off, so there is no
stereo render endpoint. Windows still calls the device "Connected", and `fConnected` is `true`.

The first implementation short-circuited on that flag and returned success without calling the
radio — leaving the user with a device that claims to be connected and produces no sound.
Removing the short-circuit fixed it live: the enable blocked 3018 ms and
`Headphones (23's AirPods Pro)` reappeared.

Two rules follow.

1. **Never infer service state from `fConnected`.** It answers "is this device on the link",
   not "is A2DP up". Only the audio endpoint answers the second question.
2. **Disabling A2DP is not "disconnect".** The pair stays connected over HFP and Windows keeps
   showing it as connected. If the product's Disconnect should leave the device fully idle, it
   must disable HFP as well — a decision for the tray menu, not for this layer.

### Measurement 14 — disconnecting both profiles really does idle the pair (2026-10-31)

With HFP also disabled:

| Step | Result |
|---|---|
| Disable A2DP | `0`, 3104 ms |
| Disable HFP | `0`, 3147 ms |
| `fConnected` afterwards | **`false`** (the only state in which it ever dropped) |
| Audio endpoints afterwards | **none** — no Headphones, no Headset |
| Re-enable A2DP | `0`, 3016 ms — **nothing comes back** |
| Re-enable HFP | `0` — still nothing after 100 s |

So "disconnect" works properly when it disables both profiles: the pair leaves the radio's
active set instead of lingering as "Connected mic".

It also proves the reverse is not symmetric. Once the pair is fully idle, no `SetServiceState`
call revives it — `rc=0` just means the request was accepted. Bring them back by opening the
case or putting them in; Windows then connects on its own within a second. This is the strongest
argument yet that the product's trigger must be the lid, and that our Connect action is only
useful while the pair is already awake.

- The **driver-free data path is real**, not theoretical: battery, lid, ear and connection
  state arrive without pairing, connection or elevation.
- **Address is not identity.** Any design keyed on it is wrong. The v1 policy is a single
  logical pair grouped by payload signature and continuity.
- **Silence is normal.** A closed lid produces no packets; the popup needs a post-silence
  burst trigger, and state must go `Unknown` on timeout instead of freezing.
- The earlier "zero advertisements" scare was environmental — the stack reported `Started`
  throughout and delivered instantly once the case opened.

## 4. What is still open, and what it could change

| Task | If it fails | Impact on the product |
|---|---|---|
| APW-015 connect | No auto-connect without a driver/admin | G3 cut; popup becomes informational only |
| APW-016 HFP | Users keep call quality on mic use | G6 cut; no "stereo always" promise |
| APW-017 endpoint | Manual device switching only | G5 cut; settings deep-link instead |
| APW-020 GATT | Nothing — BLE adverts already cover battery | None for v1 |
| APW-014 RSSI | Popup may fire for a neighbour's pair | Needs a stronger heuristic or user confirmation |
| APW-013 trials | Latency budget under-sampled | Confidence, not feasibility |

A failure in APW-015 or APW-017 weakens the product but does not invalidate it: the popup,
battery, ear detection and tray all stand on their own.

## 5. Go / no-go

- [ ] **Go** — continue to M2 as specified (driver-free, one pair, no ANC).
- [ ] **No-go** — stop, or re-scope to the driver tier (M5).

Signed: _pending owner review_ · Date: _pending_

## 5a. What has to happen before this block can be signed

| Gate | Task | State |
|---|---|---|
| AirPods reconnected once | APW-017 final assertion, APW-020 GATT probe | 10 s + 2 min |
| 7 lid-open cycles | APW-013 tenth-trial requirement | 30 s, or amend the AC to 3 |
| Walk 0.5 / 1 / 2 / 5 m with the case | APW-014 RSSI threshold | 3 min |

Everything else on the M1 board is closed. When those three are in, this report is final and
the decision is a single line in ADR-011.

---

## 6. Draft recommendation (not a decision)

Proceed to M2 **if** APW-015 and APW-017 pass. If either fails, proceed anyway with the
affected goal removed from the README rather than pretending the feature exists — the core
experience (popup, battery, ear detection) does not depend on them.

### Measurement 15 — the charging byte does not move when charging starts (2026-10-31)

Two labelled captures, same pair, right bud worn throughout.

| Ground truth | Advertisers seen | status | batt | chg byte |
|---|---|---|---|---|
| Case **not** charging | `0x6604…76FD` (−42) | `0x73` | `0xaa` | `0x99` |
| | `0x6E95…2B57` (−58) | `0x02` | `0xfa` | `0x8f` |
| Case **charging** | `0x6E95…2B57` (−54) | `0x02` | `0xfa` | **`0x8f`** |

The charging byte is **byte-identical** before and after the case went on power, and the case's
own advertiser stopped appearing at all while charging.

So:

1. Charging is **not** in the low bits of `payload[7]`. Our parser read three independent flags
   there and produced "everything charging" from `0x8f`, which is wrong — `0x0f`-style values
   look like a "nothing applies" marker, not a bitfield of three flags.
2. Charging cannot be inferred from the bud advertiser at all. If it is reported, it comes from
   the case advertiser — which was absent during the charging capture.
3. The charging mark therefore stays disabled (`ChargingMarksVerified = false`). A future capture
   needs the case both open and on power, so its advertiser is present.


