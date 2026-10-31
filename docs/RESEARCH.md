# RESEARCH — AirPods on Windows

Phase-2 research for **AirPods for Windows**. Written 2026-10-31. No application code has
been written and **no hardware verification has been performed yet** — that is M1's job.

Every claim below is tagged:

- **[doc]** — stated in a primary source (Apple/Microsoft documentation or a protocol write-up).
- **[prior art]** — observed/reported by a project that ran on real hardware (theirs, not ours).
- **[inferred]** — our reasoning from the above; needs the spike to confirm.
- **[unverified]** — plausible but not confirmed anywhere we could check.

The one thing this document does **not** do is assume that because a feature is advertised
by some app, it is achievable — or achievable *without a driver*. That distinction drives
the whole architecture.

---

## 1. Executive summary

**Verdict: the driver-free path is viable.** It covers every goal in `README.md` except
exact-1 % battery and noise control, and meets audio switching only partially.

Apple's BLE *proximity pairing* advertisement is broadcast continuously, in cleartext, and
carries the model, both bud batteries, case battery, charging bits, in-ear flags, lid state
and connection state — **without pairing and without a connection** `[doc]`. That single
packet is enough for the case-open popup, tray battery, ear detection and low-battery
warnings.

Connecting and disconnecting is a documented Win32 call (`BluetoothSetServiceState` on the
A2DP service GUID) `[doc]`. Switching the default audio endpoint needs the undocumented
`IPolicyConfig` COM interface, which is stable in practice but not contractual `[prior art]`.

What is **not** achievable without a kernel driver: exact 1 % battery (encrypted advert),
ANC / Transparency / Adaptive control, Conversation Awareness, head gestures, and macOS-style
automatic device hand-off `[doc]` `[prior art]`. Every Windows project that ships those
features bundles a KMDF driver and asks the user to disable Secure Boot `[prior art]`.

Cost of the driver-free constraint: battery granularity 10 %, popup latency bounded by the
advert cadence, and no noise-control UI. That is a good trade for a portable, admin-free
utility — and it is reversible later (M5).

---

## 2. Method and limits

- Reviewed 2026-10-31: 14 prior-art projects, Apple/Microsoft documentation, two protocol
  write-ups, three reverse-engineering references (see §14).
- Target machine: Windows 11 Pro build 26200 (25H2), x64, MediaTek RZ616 (Bluetooth 5.2),
  AirPods Pro already paired.
- **No capture has been taken yet.** Byte layouts below come from published write-ups, and
  the model IDs are partly cross-checked between two projects, partly single-source.
- Anything marked **[unverified]** must be resolved by APW-011…APW-019 before it influences
  the UI.

---

## 3. Prior art

### 3.1 Landscape

| Project | Stack | Licence | What it proves | Caveats |
|---|---|---|---|---|
| **AirPodsDesktop** (SpriteOvO, 3.6k★) | C++ / Qt 6 | GPL-3.0 | Battery, ear detection + media control, low-latency mode, animated popups; Win10 1809+/Win11 x64 | Most mature, but GPL, heavy Qt build, 105 open issues, x64 only |
| **librepods** (librepods-org, 30.5k★) | Kotlin / C++ / Rust | GPL-3.0 | Full AAP implementation on Linux/Android: ANC, ear detection, exact battery, gestures, auto-connect, hearing aid | No upstream Windows support; the Windows work lives in forks/PRs behind a driver |
| **WinPods** (sinanovicanes, 42★) | Rust / Tauri / Vue | MIT | Battery, tray, widget, connection status, low-battery alerts | No popup, no ear detection; Tauri shell |
| **WinPods** (JamesCJ60) | C# / WPF + WPF-UI | GPL-3.0 | Claims case-open popup, auto-pause, auto-switch, EQ, mic toggle, "driverless" | 9 commits, very new; claims unverified |
| **WinPods** (changcheng967) | C# / WinUI 3 + KMDF | MIT | Case popup, auto-connect, AAP layer, L2CAP bridge driver | Driver needs test signing; MIT claim worth verifying before reuse |
| **WinPods** (tzyuan15) | — | — | Battery viewer | Abandoned |
| **AirPodsTray** (ahmadashwah) | Python | **none declared** | Compact reference for *exactly* our v1 scope: BLE decode, tray, popup, auto-connect, auto-pause, self-healing scan | 3 commits; no licence file → all rights reserved, code cannot be reused |
| **Earlo** (kylianxdev, ex-PodPilot) | C# | MIT | Case-open popup, ear detection, one-click connect, stem-swipe volume overlay, ANC via *experimental driver* | Created 2026-10-21, 1★; quality unknown |
| **Near** (jlbjulio) | Rust | MIT | Battery, case popup, reconnect on bud removal, ear pause, endpoint restore, fullscreen suppression | Created 2026-10-11, 0★; states "AirPods report battery in 10 % steps" |
| **PodBridge** (bhemsen) | C# | Apache-2.0 | Driver-free by default: battery, auto play/pause, mic-profile policy, codec guidance; ANC opt-in via driver | Requires Win11 21H2+; explicit about A2DP↔HFP trade-off |
| **WPods** (FPNDev) | C# / WebView2 | GPL-3.0 | Full feature set incl. own open drivers, stereo-while-mic, ANC, Adaptive, heart rate | **Needs Test Mode + Secure Boot off** |
| **NTPods** (arctumn) | — | GPL-3.0 | Windows port of LibrePods; battery, ANC, ear detection, Conversation Awareness, hi-res mic, heart rate | Own drivers, same security posture |
| **MyPods** (HanreichC) | Qt 6 / C++ | GPL-3.0 | Case popup, 1 % battery via advert decryption + AAP, handoff, noise modes; Linux + Windows | Says it is "vibe-coded"; based on MagicPods |
| **MagicPods** (commercial, $1.99) | C# / UWP + kernel driver | Closed | Proof of demand: popup, ANC, gestures, accurate battery | Closed source; MagicAAP driver |

Also useful: **MagicPodsCore** (GPL-3.0, Linux backend with a WebSocket API — good protocol
reference), **OpenPods** (Android battery-only), **aap-head-tracker** (shows that even head
tracking on Windows needs the third-party MagicAAP driver), and **awesome-airpods**
(curated index; it lists most of the above in one place).

### 3.2 Naming warning

"WinPods" refers to **four unrelated projects** (sinanovicanes, JamesCJ60, changcheng967,
tzyuan15) and a website (`winpods.app`) that belongs to the first. Our project must not use
that name — it would collide in search, in package registries and in users' heads. The
folder is `AirPods-Windows`; the product name is still open (see §13, Q9).

### 3.3 What the prior art agrees on

1. The BLE advert alone is enough for a case-open popup `[prior art]` (AirPodsTray, Earlo,
   Near, MyPods).
2. Battery from the cleartext advert is quantised to 10 % `[doc]` `[prior art]`.
3. Exact 1 % values come from the encrypted block, and the key arrives over AAP `[doc]`.
4. AAP on Windows requires a kernel driver, because Windows exposes no user-mode L2CAP
   `[doc]` `[prior art]`.
5. Once HFP is active, Windows drops playback to mono call quality; the fix is to prevent
   HFP from being used `[prior art]` (PodBridge, Earlo).
6. Windows has no public API to set the default audio endpoint; everyone uses `IPolicyConfig`
   `[prior art]`.

---

## 4. How AirPods talk to a host

AirPods are a dual-radio device:

| Channel | Carries | Windows access |
|---|---|---|
| **Bluetooth Classic — A2DP** | Stereo audio (AAC or SBC) | Fully supported; endpoint appears as *Headphones* |
| **Bluetooth Classic — HFP** | Mono call audio + microphone | Fully supported; endpoint appears as *Headset* (AG Audio) |
| **Bluetooth Classic — AVRCP** | Play/pause/skip, volume | Handled by Windows; no per-device API |
| **Bluetooth LE — Continuity adverts** | Battery, ear state, lid, connection state, model | `BluetoothLEAdvertisementWatcher` — **passive, no pairing** |
| **Bluetooth Classic — L2CAP PSM 0x1001 (AAP)** | Exact battery, ANC, gestures, Conversation Awareness, motion | **Not exposed to user mode** — driver required |

`[doc]` for the channel split and PSM; `[prior art]` for what each channel exposes.

The practical consequence: **BLE tells you what the AirPods are doing; Classic is how you
hear them.** Our app therefore runs two independent subsystems — a passive BLE observer and
a Classic connection/audio controller — merged in one state model.

---

## 5. The BLE proximity-pairing packet

This is the core data source. Layout below follows the LinuxPods protocol write-up, which
was verified on AirPods Pro 2 (firmware 7A305) and AirPods Pro 3 (8A353) `[doc]`.

### 5.1 Envelope

```
AD structure:  Length | 0xFF (manufacturer data) | Company ID 0x004C (Apple) | Type 0x07 | payload…
```

`0x004C` = Apple Inc.; `0x07` = "proximity pairing". Payload ≈ 25 bytes `[doc]`.

### 5.2 Payload

| Offset | Field | Notes |
|---|---|---|
| 0 | Prefix | always `0x01` |
| 1–2 | Product ID | **little-endian** u16 (`27 20` → `0x2027` = AirPods Pro 3) |
| 3 | Status byte | ear flags, primary pod, in-case flags (see 5.4) |
| 4 | Battery | two nibbles: left / right, 10 % steps (see 5.5) |
| 5 | Charging + case battery | low bits charging, high nibble case battery |
| 6 | Lid state | **bit 3: 0 = lid open** (only meaningful when pods are in the case) |
| 7 | Device colour | 0 = white, … |
| 8 | Connection state | `0x00` disconnected, `0x04` idle, `0x05` media playing … |
| 9–24 | Encrypted block | AES-128-ECB: exact battery + MAC suffix |

`[doc]` — with one caveat: the public write-ups disagree on whether offsets 1–2 are read
big- or little-endian for *some* model tables. The little-endian reading is the one that
matches BlueZ's `Modalias` for the same pair; APW-012 must confirm on our hardware.

### 5.3 Product IDs

```
0x2002  AirPods (1st gen)        0x200E  AirPods Pro
0x200F  AirPods (2nd gen)        0x2014  AirPods Pro (2nd gen)
0x2013  AirPods (3rd gen)        0x2024  AirPods Pro (2nd gen, USB-C)
0x2019  AirPods 4                0x2027  AirPods Pro 3
0x201B  AirPods 4 (ANC)          0x200A  AirPods Max
                                 0x201F  AirPods Max (USB-C)
```

Only `0x2024` and `0x2027` were verified on hardware by the source project; the rest are
cross-referenced between two public tables `[doc]`. We must degrade gracefully on an unknown
ID (show "AirPods", no model art).

### 5.4 Status byte

| Bit | Meaning |
|---|---|
| 0 | unknown |
| 1 | in ear (primary pod) |
| 2 | both pods in case |
| 3 | in ear (secondary pod) |
| 4 | one pod in case (partner worn) |
| 5 | primary pod: 0 = right, 1 = left |
| 6 | this pod in case |
| 7 | unknown |

`[doc]`. Bit 5 matters everywhere else: when the right pod is primary, the battery nibbles,
charging bits and ear flags are **swapped** and the ear bits use XOR logic. A parser that
ignores this produces mirrored left/right values on roughly half of all advertisements —
the single most likely source of a "my left bud shows 0 %" bug `[inferred]`.

### 5.5 Battery encoding

- Bud levels: one nibble each, value 0–10 (= 0–100 % in 10 % steps); `0xF` means unknown.
- Case: high nibble of byte 5; a value > 10 means "unknown" (often while the case is closed).
- Charging: bits 1/2/3 of byte 5 = case / right / left charging.
- These are **approximate**: the write-up warns of a 5–10 % deviation from the real level and
  a slow refresh `[doc]`.

### 5.6 Encryption, identity and why it matters

- Bytes 9–24 are AES-128-ECB ciphertext containing the exact battery percentages and the
  last three bytes of the pair's real MAC. The key is delivered over AAP `[doc]`.
- Advertisements come from a **resolvable private address** that rotates; only the pair's IRK
  resolves it. While connected, the MAC suffix inside the payload is zeroed `[doc]`.
- **Confirmed on hardware (2026-10-31):** one pair appeared under **four different
  addresses inside 150 s**, with overlapping windows and identical product ID and battery
  values. An address is not an identity. See `docs/captures/ANALYSIS-2026-10-31.md`.
- Therefore, driver-free and key-free, we can read *a* nearby AirPods' state but cannot
  cryptographically prove *which* pair it is `[doc]` `[inferred]`.

This is the project's biggest UX risk: with one pair in range, a product-ID + RSSI heuristic
is fine; with two, the popup may show the wrong pair's battery. APW-018 decides the policy
(single-pair only in v1 is an acceptable answer).

### 5.7 Cadence

The payload changes when something happens (lid, ear, charging) and otherwise refreshes
every **20–30 s** `[doc]`. That gives an excellent popup latency budget on events and a poor
one for continuous battery animation — the UI must not imply real-time precision.

**Corrected by measurement (2026-10-31):** an out-of-case, in-range pair advertises about
**once per second** — median interval 977 ms, p95 4.1 s over 126 intervals — and falls silent
within a second or two of the lid closing. The 20–30 s figure applies to a sleeping device,
not an awake one. Worst observed silence before a lid-open packet: **24.5 s**, which is why
the popup trigger must also react to a burst after silence rather than assume a steady feed.

---

## 6. Windows API inventory

| Capability | API | Status | Caveats |
|---|---|---|---|
| Scan BLE adverts | `Windows.Devices.Bluetooth.Advertisement.BluetoothLEAdvertisementWatcher` | **Works** `[doc]` | Needs Bluetooth on; `AllowExtendedAdvertisements` since Win10 2004; `bluetooth` capability only enforced for packaged apps; watcher dies silently on sleep/adapter reset → must self-heal |
| Enumerate paired devices | `DeviceInformation.FindAllAsync` / `DeviceWatcher`, AEP properties | **Works** `[doc]` | Gives Classic address + friendly name; does **not** resolve BLE RPA |
| Connect / disconnect A2DP | `BluetoothSetServiceState(hRadio, &deviceInfo, &A2DP_SINK_GUID, ENABLE/DISABLE)` (`bthprops.cpl`) | **Works** `[doc]` `[prior art]` | Enabling installs the profile driver; `E_INVALIDARG` if already in that state → treat as success; radio handle via `BluetoothFindFirstRadio` |
| Toggle HFP (mic policy) | same call with the Hands-Free GUID `{0000111E-…}` | **Expected to work** `[prior art]` | Disables the AirPods mic entirely; Windows may re-enable on reconnect → re-apply |
| Set default audio endpoint | `IPolicyConfig::SetDefaultEndpoint` (undocumented COM) | **Works in practice** `[prior art]` | No public API exists; CLSID `870AF99C-171D-4F9E-AF0D-E63DF40C2BC9`; needs a fallback to Settings |
| Enumerate render endpoints | `IMMDeviceEnumerator` (MMDevice API) | **Works** `[doc]` | Endpoint IDs are stable per device; HFP vs A2DP are distinct endpoints |
| Pause / resume media | `Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager`; fallback `SendInput` VK_MEDIA_PLAY_PAUSE | **Works** `[doc]` | SMTC only covers SMTC-aware apps; media key is the universal fallback but is a blind toggle |
| Observe volume changes | `IAudioEndpointVolume` callback | **Works** `[doc]` | Enables a stem-swipe volume overlay (a nice-to-have) |
| Receive audio *from* a phone | `Windows.Media.Audio.AudioPlaybackConnection` | Works, **wrong direction** | Often mistaken for "connect to headphones"; it makes the PC an A2DP **sink** |
| Raw L2CAP / AAP | — | **Not available** `[doc]` `[prior art]` | Winsock exposes RFCOMM only; every project uses a KMDF `bthecho`-derived driver |
| GATT battery service | `GattDeviceService` `0x180F` / `0x2A19` | **Absent** — measured 2026-10-31 | `GetGattServicesAsync` on the live RPA returns `Unreachable`, 0 services. No LE bond is created by AirPods, so there is nothing to connect to. Windows Settings therefore shows no battery |
| Toast notifications | `AppNotificationManager` / Community Toolkit | Works but awkward unpackaged | We use our own popup window instead (ADR-007) |

**Two APIs carry the design**: `BluetoothLEAdvertisementWatcher` (state in) and
`BluetoothSetServiceState` (connectivity out). Everything else is polish.

---

## 7. Feature feasibility

| # | Feature | Mechanism | Effort | Fidelity | Risk |
|---|---|---|---|---|---|
| F1 | Case-open popup | lid bit + advert burst + RSSI gate | M | High | Lid bit only broadcast when pods are cased; missed first packets |
| F2 | Tray battery | advert nibbles | S | 10 % steps | Stale when out of range — must show "unknown", not last value |
| F3 | Auto-connect | `BluetoothSetServiceState` A2DP | M | High | 2–10 s connect time; may steal audio from the phone mid-call |
| F4 | Ear detection → pause/resume | status bits + SMTC/media key | M | High | Must not resume media the user paused manually |
| F5 | Default endpoint switch | `IPolicyConfig` | S | High | Undocumented; may break on a future build |
| F6 | Mic policy ("stereo always") | disable HFP service | S | High | AirPods mic becomes unavailable; re-apply after reconnect |
| F7 | Low-battery warning | advert nibbles | S | 10 % steps | Fires late by up to one step |
| F8 | Exact 1 % battery | AAP or encrypted advert | **XL** | Exact | Driver + keys (ADR-003, ADR-008) |
| F9 | ANC / Transparency | AAP | **XL** | Exact | Driver |
| F10 | Automatic device hand-off | Apple continuity | **—** | — | Not reproducible without Apple's cloud |
| F11 | Spatial audio / head tracking | AAP motion | **XL** | — | Driver + undocumented sensor service |
| F12 | Low-latency mode | likely HFP suppression + enhancements off | S | Medium | Marketing term; no true low-latency codec path on Windows |

### 7.1 Case-open and proximity — is it realistic?

**Yes, with caveats.** The lid bit exists and prior-art apps trigger popups from it
`[doc]` `[prior art]`. Three constraints shape the design:

1. The lid bit is only trustworthy when the status byte says the pods are in the case
   (bits 2/6). One pod out → no reliable lid signal `[doc]`.
2. Adverts are event-driven: opening the lid produces a burst within a few hundred ms
   `[prior art]`, but Windows may drop the first packets while the watcher is sampling.
   A 1.5 s p95 popup is a realistic target, not a guarantee.
3. "Near" is RSSI, and RSSI is noisy. A threshold plus hysteresis plus a 10 s cooldown is
   the honest implementation; a popup for the neighbour's AirPods through a wall is
   possible and must be mitigated by requiring a strong signal.

What is **not** realistic: knowing the case was opened *while the AirPods are connected to
another device and asleep*. If they are not advertising, there is no event to see.

### 7.2 Battery reliability across models

| Model | Left/Right | Case | Notes |
|---|---|---|---|
| AirPods 1/2 | Yes (10 %) | Usually, lid open only | Older firmware is the least documented `[unverified]` |
| AirPods 3/4 | Yes | Yes | 4/4-ANC use the newer product IDs |
| AirPods Pro 1/2/3 | Yes | Yes | Pro 2 USB-C and Pro 3 verified by the source `[doc]` |
| AirPods Max | **Single value** | **No case** | UI must not render two buds + case for Max |

Failure modes to design for: nibble `0xF` (unknown), case nibble > 10 (unknown), a bud in the
case showing its last level, and a device that has not advertised for > 60 s.

### 7.3 Audio switching — what "switching" can mean here

- ✅ Switch the PC's default output to the AirPods when they connect.
- ✅ Restore the previous output when they disconnect.
- ✅ Optionally pause other media while the AirPods are active.
- ❌ Move audio *from the phone to the PC* when you put the buds in — that is Apple's
  continuity hand-off, requiring Apple-account pairing material.
- ⚠️ Simultaneous multi-host (AirPods connected to phone and PC at once) is a firmware
  behaviour, not something an app can request reliably.

---

## 8. Windows 11 compatibility

- **Minimum:** Windows 10 2004 (19041) for `AllowExtendedAdvertisements`; we target Windows
  11 primarily (test machine: build 26200).
- **Architecture:** x64 first. ARM64 is plausible but untested; do not promise it.
- **Adapter matters.** Some stacks merge or throttle Apple adverts, and Intel/MediaTek
  drivers differ in how they report RSSI `[inferred]`. The RZ616 on the dev machine is a
  common laptop part, so results there are representative of a large user base.
- **Sleep/resume** stops the watcher without an exception; every mature project has a
  self-healing restart `[prior art]`. Treat it as a first-class requirement (APW-207).
- **Windows 11 "Bluetooth LE Audio"** is unrelated: AirPods are Classic A2DP devices and do
  not use LE Audio `[doc]`.
- **Codec:** Windows negotiates AAC where the adapter/driver supports it, otherwise SBC
  `[prior art]`. We cannot force a codec through a public API; do not promise "better sound".
- **SmartScreen / Smart App Control** will warn on an unsigned exe. Signing is a product
  decision (APW-306), not a technical blocker.

## 9. Security, privacy, legal

- **No admin, no driver** in v1 (ADR-003). The driver tier requires Test Mode and Secure
  Boot off — a machine-wide security downgrade that is unacceptable as a default and often
  blocked by corporate policy `[prior art]`.
- **Passive scanning** sees every BLE device nearby. The app must filter to Apple `0x004C`
  type `0x07`, must not log unrelated addresses, and must not persist raw captures by
  default. Diagnostic exports are user-initiated (ADR-010).
- **No Apple credentials.** Do not read keychains, do not ask for an Apple ID, do not
  extract `MagicAccIRK` / `MagicAccEncKey`. Several projects do; we will not (ADR-008).
- **Reverse engineering** of an interoperability protocol is widely practised and published,
  but patents and Apple's terms are a real (if low) risk. We ship no Apple code, assets or
  keys, and we do not redistribute firmware. This is engineering judgement, not legal advice.
- **Trademarks:** "AirPods" may be used descriptively; do not use Apple logos, device
  renders, or the AirPods product photography in the UI or store listing. Draw our own
  simplified device shapes.

## 10. Licensing

| Licence | Projects | Consequence for us |
|---|---|---|
| GPL-3.0 | AirPodsDesktop, librepods, WPods, NTPods, MyPods, MagicPodsCore | Read for facts; **do not copy code**; linking would force GPL on us |
| MIT | sinanovicanes/winpods, Earlo, Near, changcheng967/WinPods, OpenPods | Reusable with attribution, but the two closest matches are weeks old |
| Apache-2.0 | PodBridge, podctl | Reusable with notice + patent grant |
| None declared | AirPodsTray | Treat as all rights reserved — reference only |
| Closed | MagicPods | Behaviour reference only |

Protocol facts (company ID, PSM number, service UUIDs, byte offsets) are not copyrightable
expression; particular code, comments, tables and assets are. Our rule: **facts yes, files
no** (ADR-004).

---

## 11. Build vs. extend — verdict

| Option | Best when | Why not (for us) |
|---|---|---|
| Fork **AirPodsDesktop** | You want maximum features today and accept GPL + Qt + C++ | Viral licence, heavy toolchain, large inherited surface, slow iteration |
| Fork **WPods / NTPods** | You want ANC and exact battery and accept a driver + Secure Boot off | Security posture contradicts the product promise; GPL |
| Fork **Earlo / Near** | You want an MIT base with the right feature set | Weeks old, 0–1 stars, no tests; we would inherit unknown protocol code |
| **Build from scratch** | You want a small, tested, permissively licensed app with a specific UX | Slower to first release; we own protocol correctness |

**Recommendation: build from scratch** (ADR-001), with a 1–2 day spike to de-risk the two
unknowns that could invalidate the plan: advert reliability on this adapter, and device
identity.

If the spike fails on identity *and* the owner needs multi-pair support, the fallback is to
fork Earlo (MIT) rather than to write a driver.

---

## 12. Where the effort will actually go

1. **Popup polish** (multi-monitor DPI, no-focus activation, animation, theming) — easily a
   third of the UI work.
2. **State correctness** (orientation flip, staleness, unknown values, sleep/resume) — the
   part users notice as "it's wrong sometimes".
3. **Audio behaviour** (connect timing, endpoint restore, HFP toggling, not fighting the
   user's manual choices) — the part that generates support tickets.
4. Protocol parsing is comparatively small (~300 lines) but must be tested exhaustively.

---

## 13. Open questions (must be answered by M1)

| # | Question | Task |
|---|---|---|
| Q1 | Does this adapter deliver Apple adverts reliably **while the AirPods are connected to this PC**? | APW-011 |
| Q2 | Is the payload little-endian for the product ID on our firmware? | APW-012 |
| Q3 | Is the lid bit present and stable on the paired AirPods Pro? | APW-012/013 |
| Q4 | What is the real p95 lid-open → first-advert latency on Windows? | APW-013 |
| Q5 | Can a BLE advert be attributed to a specific paired pair (RPA/IRK)? | APW-018 |
| Q6 | Does `BluetoothSetServiceState` connect without elevation on build 26200? | APW-015 |
| Q7 | Does disabling HFP persist across reconnects and Windows updates? | APW-016 |
| Q8 | Does `IPolicyConfig` still work on build 26200, and how fast? | APW-017 |
| Q9 | What is the product called? ("WinPods" is taken four times over) | owner |
| Q10 | Do we support Windows 10 at all, or Windows 11 only? | owner |

---

## 14. References

**Protocol / reverse engineering**
- LinuxPods — *Apple Continuity BLE Proximity Pairing Protocol* (byte-level, verified on Pro 2 / Pro 3): https://github.com/mstroecker/LinuxPods/blob/main/docs/ble-proximity-pairing.md
- LinuxPods — *AAP noise control* (L2CAP PSM 4097 / `0x1001`): https://github.com/mstroecker/LinuxPods/blob/main/docs/aap-noise-control.md
- LibrePods — AAP implementation and AACP opcode reference: https://github.com/librepods-org/librepods
- `librepods-windowsbridge` — *Proximity Pairing Message* notes: https://github.com/will-ch-h/librepods-windowsbridge
- furiousMAC/continuity — Apple Continuity protocol documentation: https://github.com/furiousMAC/continuity
- `l2cap-windowsdriver` — bthecho-derived L2CAP bridge, states the user-mode limitation: https://github.com/will-ch-h/l2cap-windowsdriver

**Microsoft documentation**
- `BluetoothLEAdvertisementWatcher`: https://learn.microsoft.com/uwp/api/windows.devices.bluetooth.advertisement.bluetoothleadvertisementwatcher
- `BluetoothSetServiceState`: https://learn.microsoft.com/windows/win32/api/bluetoothapis/nf-bluetoothapis-bluetoothsetservicestate
- Bluetooth Classic audio (A2DP/HFP, codecs): https://learn.microsoft.com/windows-hardware/drivers/bluetooth/bluetooth-classic-audio
- Bluetooth driver stack (why L2CAP is kernel-side): https://learn.microsoft.com/windows-hardware/drivers/bluetooth/bluetooth-driver-stack
- `AudioPlaybackConnection` (the *other* direction): https://learn.microsoft.com/windows/apps/develop/media-playback/enable-remote-audio-playback

**Prior art**
- AirPodsDesktop: https://github.com/SpriteOvO/AirPodsDesktop
- WinPods (Rust/Tauri, MIT): https://github.com/sinanovicanes/winpods
- WinPods (C#/WPF, GPL): https://github.com/JamesCJ60/WinPods
- WinPods (C#/WinUI + driver): https://github.com/changcheng967/WinPods
- AirPodsTray (Python, no licence): https://github.com/ahmadashwah/AirPodsTray
- Earlo (C#, MIT): https://github.com/kylianxdev/Earlo
- Near (Rust, MIT): https://github.com/jlbjulio/Near
- PodBridge (Apache-2.0): https://github.com/bhemsen/PodBridge
- WPods (GPL, drivers): https://github.com/FPNDev/wpods
- NTPods: https://github.com/arctumn/ntpods
- MyPods: https://github.com/HanreichC/MyPods
- MagicPods (commercial) and MagicAAP: https://magicpods.app/magicaap/
- awesome-airpods index: https://github.com/hypery11/awesome-airpods

**Audio endpoint switching**
- `IPolicyConfig` declaration used by Sunshine: https://docs.lizardbyte.dev/projects/sunshine/latest/PolicyConfig_8h.html
