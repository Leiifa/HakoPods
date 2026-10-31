# APW-115 — Case-open popup

## Direction

**Quiet status instrument** — a small, precise piece of Windows desktop UI, not a miniature Apple product page. Use Segoe UI Variable for native legibility; a sparse two-column instrument-panel layout; warm-neutral surfaces; and the Windows system accent only for the active/charging cue. The device glyph is original geometry, never product photography. References: Windows 11 Settings' disciplined type hierarchy and native system-accent behavior; borrow restraint and predictable alignment, not its page chrome.

**Intent:** let someone opening the case read which buds are in use and the coarse battery levels without moving focus away from what they are doing. The popup is informational, never a CTA or a connection prompt.

**Guardrails:** no Apple logo/art, no product-rendered earbuds, no fabricated precision, no glass blur, no gradients, no nested cards, no focus steal. All battery values are 10% steps. Treat stale/missing values as unknown, not as a frozen reading.

## Geometry and grid

- Reference window: **336 × 206 DIPs** (content width 304 DIP); 12-DIP outer inset; 20-DIP base spacing. Surface is an opaque system window-background neutral with a 1-DIP outline and 10-DIP corner radius. No shadow-dependent edge definition.
- Header (32 DIP): small original two-bud/case line glyph at left; product family name (e.g. “AirPods Pro”) at 13 DIP semibold; a 12-DIP status line aligned to the trailing edge (“Nearby”, “Connected”, or “Not connected”). Unknown model: “AirPods”.
- Main region: three equal 88-DIP columns, 12-DIP gutters, labels “LEFT”, “RIGHT”, “CASE” in 11-DIP medium tracked caps; percentage at 24 DIP semibold with tabular numerals; slim 3-DIP battery rail below. Charging mark is a small inline bolt; do not color-fill the whole cell.
- Footer (28 DIP): concise state sentence, e.g. “Both in case · lid open” or “One in ear”. No buttons. Footer may be omitted when it would merely repeat the header.
- AirPods Max is a dedicated single-column layout: one “HEADPHONES” value, one rail, no buds, no case, no empty columns.
- Use a 4-DIP alignment grid. Vertical rhythm: 12-DIP header-to-data gap, 8-DIP label-to-value, 10-DIP value-to-rail, 12-DIP rail-to-footer. Never shrink type to fit a long model name; ellipsize it.

## Typography

Segoe UI Variable, with Segoe UI fallback. Numerals use tabular lining figures. Product name 13/18 DIP semibold; battery value 24/28 DIP semibold; status/body 12/16 DIP regular; labels 11/14 DIP medium, tracking +0.6 DIP. Do not use all caps for sentences. Minimum functional text is 11 DIP.

## Tokens and themes

Resolve the accent from the Windows system accent at runtime; the diagrams use a restrained blue stand-in. Keep accent to charging marks, the near/active status dot and keyboard/highlight affordances. All other semantics use neutral foreground/surface tokens; low battery uses a readable text/icon warning plus value, never color alone.

| Token | Light | Dark | Use |
|---|---|---|---|
| Window | `#F7F6F3` | `#242422` | Opaque surface |
| Raised/rail track | `#E9E7E2` | `#393936` | Battery rail track |
| Primary text | `#20201E` | `#F3F2EF` | Headings and values |
| Secondary text | `#62615C` | `#B9B7B0` | Labels/status |
| Outline | `#D3D1CA` | `#4A4944` | 1-DIP perimeter |
| Accent | System accent | System accent | Charging/active only |
| Low battery | `#8C3B2D` | `#FFB4A8` | Icon + “Low” cue; test contrast against surface |

Text contrast target: WCAG 4.5:1 for body and labels; large battery values at least 3:1. Recompute accent and warning foreground against actual system surfaces at runtime; if a chosen system accent fails contrast, use it only as a small decorative mark and retain neutral text.

## States and content

| State | Display |
|---|---|
| Lid open, idle | Show every known L/R/case value; footer “Lid open”. Do not imply connected. |
| One bud in ear | Keep both bud columns; add a small ear-worn outline marker beside the in-ear bud label; footer “Left in ear” or “Right in ear”. |
| Both in ear | Mark both labels; footer “Both in ear”. |
| Charging | Add inline bolt beside the charging component(s). Keep the battery percentage visible; no animated charging fill. Case charging can coexist with buds charging. |
| Low battery (≤10%) | Value remains visible; add “Low” with warning glyph and warning token. Apply independently to each bud and case; no alarm styling. |
| Unknown value | Render em dash `—`, not 0%; rail is empty with dashed/quiet track; secondary caption “Unknown”. Apply per component. |
| Disconnected | Retain values only while fresh (≤60 s); status says “Not connected” only when supported by the endpoint signal. After 60 s, clear all measurements to unknown. Never infer disconnect from BLE silence alone. |
| AirPods Max | Single “HEADPHONES” percentage, single rail; no L/R or case slots. Unknown and low-battery treatments remain identical. |
| Unknown model | Header “AirPods”; use the standard bud/case presentation only when fields are present. Omit unavailable components rather than showing invented data. |

Battery rail length is proportional to the reported 0–100% in 10% increments. A full rail means the latest advert reported 100%, not a prediction. No percentage interpolation or continuous fill animation.

## Scale and placement

All dimensions above are WPF DIPs. At 100% the reference is 336×206 physical px; at 150% it is 504×309; at 200% it is 672×412. Keep typography and 1-DIP strokes in DIPs so Windows scales them together. Snap the outer outline and 3-DIP rails to device pixels at the active monitor DPI; never independently scale font sizes or compress spacing. At 200%, preserve the same composition—do not turn the popup into a dashboard.

Position adjacent to the taskbar edge on the monitor under the cursor; inset 12 DIP from the taskbar and 8 DIP from the work-area edge. Clamp to the selected monitor's work area. On mixed-DPI displays, measure and position in that monitor's coordinate space. Do not cross a monitor boundary to fit. Window is topmost and no-activate; Esc/click-away/timeout dismiss it, without taking keyboard focus.

## Motion and timing

- Show: 160 ms, cubic ease-out; opacity 0→1 and translate Y 4→0 DIP. No scale/bounce.
- Dismiss: 120 ms, cubic ease-in; opacity 1→0 and translate Y 0→2 DIP.
- Charging mark is static. No looping motion or battery-fill tween.
- All motion is below 250 ms. Under reduced-motion preference, use a 1-frame opacity switch and no translation.

## Reference renders

- `popup/01-light-idle.svg` — light idle, lid open.
- `popup/02-dark-in-ear.svg` — dark, both in ear with one low value.
- `popup/03-light-charging.svg` — light, charging and unknown-value example.
- `popup/04-max-dark.svg` — AirPods Max single-value layout.

These are vector layout references at 100% scale, not pixel-captured WPF windows. Dimensions and scaling rules above are authoritative.

## Implementation handoff

Use these tokens and content rules as the source of truth. Do not substitute Apple imagery or invent extra states. `DeviceState` unknown/staleness maps to em dash, and disconnect text must follow the endpoint signal; keep popup trigger logic separate from rendering. The popup remains informational and no-activate.

## Sign-off

Owner: pending review — not yet signed off. Prepared by atelier, 2026-10-31. APW-115 remains in review until owner approval.