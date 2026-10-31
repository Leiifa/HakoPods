# APW-115 — Case-open popup

## Direction

**Apple accessory card, in a Windows window.** This is the iPhone/iPad battery-widget idiom applied to a 336 × 206 DIP popup: one opaque card, large tabular numerals, sentence-case labels, capsule battery rails whose colour follows the level, and a shadow as the only edge. No device illustration at all — no Apple artwork, no product photography, no simplified device glyph.

The change from the first direction is deliberate: that version borrowed Windows 11 Settings' page chrome and read as a status instrument. This one borrows the *widget's* information hierarchy — number first, label second, rail as confirmation — while keeping the Windows constraints that matter (opaque, no-activate, DIPs, no motion loops).

**Intent:** opening the case should tell you which buds are in use and roughly how much charge is left, without moving focus away from what you are doing. The popup is informational, never a CTA or a connection prompt.

**Guardrails:** no Apple logo, product art or photography; no fabricated precision; no gradients or glass *inside* the panel; no nested cards; no focus steal; no looping motion. All battery values are 10 % steps. Missing or stale values are unknown, never a frozen reading.

## Geometry and grid

- Reference window: **336 × 206 DIPs**. Corner radius **22 DIP**. Padding **16 DIP** vertical, **18 DIP** horizontal; content width 300 DIP.
- Surface is opaque, edge defined by shadow only: `0 0 0 0.5px` at 5 % black, `0 2px 4px -1px` at 6 %, `0 14px 34px -12px` at 30 %. There is **no 1-DIP outline**; if the window is ever drawn without a shadow, restore a hairline before shipping.
- Header: product family name at 15 DIP semibold, starting at the padding edge. No device glyph — the card carries the name, the state and the values, nothing else. Status right-aligned at the padding edge, 12 DIP, with a 6-DIP dot 8 DIP to its left — accent-coloured only when the state is `Connected`, otherwise secondary at 32 %.
- Main region: **three equal 92-DIP columns, 12-DIP gutters**. Label 12 DIP secondary, sentence case; value 32 DIP semibold tabular with a 17-DIP secondary `%`; then a 6-DIP capsule rail with 3-DIP radius. Vertical rhythm inside a column: 8 DIP label-to-value, 12 DIP value-to-rail.
- AirPods Max is a dedicated single-column layout: one centred `Headphones` label, one 44-DIP value, one 190-DIP rail. No left/right/case slots, no empty columns.
- Footer: 12 DIP secondary, pinned to the bottom padding edge. No buttons. Omit when it would only repeat the header.
- 4-DIP alignment grid throughout. The 0.5-DIP shadow ring replaces a border as the edge definition.

## Typography

**Inter** (SIL OFL, bundled) as the primary face, with `SF Pro Text`, `Segoe UI Variable Text` and `Segoe UI` as fallbacks. Inter is used because it is the closest licence-safe match to SF's metrics and shapes; without it the panel reads as a generic Windows card.

Numerals use tabular lining figures (`Typography.NumeralAlignment="Tabular"` in WPF). Product name 15/20 semibold, −0.3 DIP tracking; battery value 32/32 semibold, −1.3 DIP tracking; per cent 17/17 regular secondary; labels and footer 12/18 regular; status 12/18 regular. **Minimum functional text is 12 DIP** — nothing in the panel goes below it, including the `Low` cue.

Unknown values render the em dash at **28 DIP regular** in the secondary colour, not at full value weight: a dash at 32 DIP semibold reads as a rule rather than as missing data.

## Tokens and themes

Accent is resolved from the Windows system accent at runtime; the values below are the stand-in used in the reference renders. Keep accent for the charging bolt and the connected dot. Battery level uses the green/red tokens, but **colour is never the only signal** — the number, and where relevant the word `Low`, carry the same information.

| Token | Light | Dark | Use |
|---|---|---|---|
| Surface | `#FFFFFF` | `#1C1C1E` | Opaque panel |
| Track | `#EAEAEE` | `#3A3A3C` | Empty part of the battery rail |
| Primary text | `#1D1D1F` | `#F5F5F7` | Model name and values |
| Secondary text | `#6E6E73` | `#A1A1A6` | Labels, status, footer, per cent, unknown |
| Level normal | `#248A3D` | `#30D158` | Rail fill above 10 % |
| Level low | `#D70015` | `#FF453A` | Rail fill at 10 % or below, and the `Low` word |
| Accent | System accent | System accent | Charging bolt, connected dot |
| Unknown rail | 1-DIP dashed, 30 % black | 1-DIP dashed, 34 % white | No fill, no percentage |

Text contrast measured against the panel: primary 12.2–15.5:1, secondary 4.66–7.7:1, level green 4.4:1 (graphic, 3:1 required), low red 4.99:1. Recompute accent and red against the actual system surface at runtime; if a chosen accent fails contrast, keep it for the 6-DIP dot only and leave all text neutral.

## States and content

| State | Display |
|---|---|
| Lid open, idle | Show every known L/R/case value; footer “Lid open”. Do not imply connected. |
| One bud in ear | Keep both bud columns; add a small ear-worn outline marker beside the in-ear bud label; footer “Left in ear” or “Right in ear”. |
| Both in ear | Mark both labels; footer “Both in ear”. |
| Charging | Add a 9 × 13 inline bolt after the charging component's value, in the accent. Keep the percentage visible; no animated fill. Case charging can coexist with buds charging. |
| Low battery (≤ 10 %) | Value stays visible; rail turns red; add `Low` under the rail in 12 DIP semibold. Apply independently per component. No alarm styling, no colour-only cue. |
| Unknown value | Em dash at 28 DIP regular secondary; rail empty and dashed; no per cent sign. Apply per component. |
| Disconnected | Retain values only while fresh (≤ 60 s); status reads “Not connected” only when the endpoint signal supports it. After 60 s, clear every measurement to unknown. Never infer disconnect from BLE silence alone. |
| AirPods Max | Single `Headphones` value and one 190-DIP rail, centred; no L/R or case slots. Unknown and low treatments identical. |
| Unknown model | Header falls back to “AirPods”; keep the standard bud/case presentation only for fields that are present. Omit unavailable components rather than inventing them. |

Battery rail length is proportional to the reported 0–100 % in 10 % increments. A full rail means the last advert reported 100 %, not a prediction. No interpolation, no continuous fill animation.

## Scale and placement

All dimensions above are WPF DIPs. At 100 % the reference is 336 × 206 physical px; at 150 % it is 504 × 309; at 200 % it is 672 × 412. Keep type and the 0.5-DIP ring in DIPs so Windows scales them together. Snap rails and the shadow ring to device pixels at the active monitor DPI; never scale font sizes or compress padding independently. At 200 % the composition is unchanged — same three columns, no dashboard.

Position adjacent to the taskbar edge on the monitor under the cursor; inset 12 DIP from the taskbar and 8 DIP from the work-area edge. Clamp to the selected monitor's work area. On mixed-DPI displays, measure and position in that monitor's coordinate space. Do not cross a monitor boundary to fit. Window is topmost and no-activate; Esc, click-away or timeout dismisses it without taking keyboard focus.

## Motion and timing

- Show: 160 ms, cubic ease-out; opacity 0 → 1 and translate Y 4 → 0 DIP. No scale, no bounce.
- Dismiss: 120 ms, cubic ease-in; opacity 1 → 0 and translate Y 0 → 2 DIP.
- Charging mark is static. No looping motion, no battery-fill tween.
- All motion is below 250 ms. Under reduced-motion preference, use a one-frame opacity switch and no translation.

## Reference renders

- `popup/01-light-idle.svg` — light idle, lid open.
- `popup/02-dark-in-ear.svg` — dark, both in ear with one low value.
- `popup/03-light-charging.svg` — light, charging and unknown-value example.
- `popup/04-max-dark.svg` — dark, AirPods Max single-value layout.

Vector layout references at 100 % scale, not pixel-captured WPF windows. They need Inter available to render as intended; the dimensions and rules above are authoritative, not the glyph positions inside the SVGs.

## Implementation handoff

The tokens, sizes and content rules above are the source of truth. Do not substitute Apple imagery or invent states. `DeviceState` unknown/staleness maps to the em dash and the dashed rail; disconnect text must follow the endpoint signal. Keep popup trigger logic separate from rendering. The popup remains informational and no-activate.

## Open decisions before implementation

1. **Typeface.** Inter must be bundled (two static weights or one variable font, roughly 400 KB) for the panel to look like this. If bundling is rejected, every size and spacing above still holds, but the card reverts to Segoe and the visual argument weakens to “a clean Windows card”. Owner call, not an implementation detail.
2. **Level colour.** The previous direction reserved colour for the accent only. This one gives every rail a green fill and turns it red at 10 %. That is a contract change and should be confirmed with whoever owns the accessibility review.
3. **Shadow-only edge.** Accepted on the understanding that the window is drawn with a shadow. If that ever stops being true, a 1-DIP hairline returns.

## Sign-off

Owner: pending review — prepared by atelier, 2026-10-31. This revision replaces the first direction (10-DIP radius, 3-DIP rails, Segoe, accent-only colour). APW-115 remains in review until the owner approves.
