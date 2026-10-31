# docs/design — visual design artefacts

Owned by the **`atelier`** Hermes profile (ADR-012). A coding agent implements what is here;
it does not redesign it.

## Contract

| File | Produced by | Consumed by |
|---|---|---|
| `POPUP.md` + `popup/` renders | APW-115 | APW-108 (popup window) |
| `ICONS.md` + `icons/` SVGs and PNGs | APW-116 | APW-107 (tray), APW-110 (notifications) |
| `SETTINGS.md` + `settings/` renders | APW-311 | APW-301 (settings UI) |

## Rules

- Every state must be specified, including the ugly ones: unknown battery, one bud in the
  case, AirPods Max (single value, no case), charging, disconnected.
- Specify at 100 / 150 / 200 % scale. Windows users run all three.
- Colours come from the system accent plus a neutral ramp; no hardcoded brand palette.
- Motion is specified as duration + easing + what moves. Budget: ≤ 250 ms.
- No Apple product imagery, logos or product photography. Simplified device shapes only.
- Each artefact ends with a `## Sign-off` line naming the owner and the date.

## Handoff

```
C:\Users\Leifa\.local\bin\atelier.bat chat -q "APW-115: design the case-open popup per docs/RESEARCH.md and AGENTS.md" --in D:\Leifa\Airpod-windows
```

The design task is `done` only when the artefact exists, covers every state above, and has
the owner's sign-off.
