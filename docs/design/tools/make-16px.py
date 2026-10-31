"""Generates the simplified 16 px tray masters.

The 32 px masters carry a lid line and bud stems that merge into a wedge when Windows scales
them down to 16. These keep three shapes only — case, two buds, one state mark — with a heavier
stroke, per `docs/design/ICONS.md` ("internal detail is removed before outer silhouette detail").

Run from the repo root: python docs/design/tools/make-16px.py
"""

import pathlib

OUT = pathlib.Path(__file__).resolve().parents[1] / "icons" / "16"

INK = {"light": "#20201E", "dark": "#F3F2EF"}
BOLT = {"light": "#0071E3", "dark": "#0A84FF"}

CASE = "M3 9h10l1.5 2.5L13 15H3L1.5 11.5z"
BUD_LEFT = "M6 3.5v3.5"
BUD_RIGHT = "M10 3.5v3.5"
BOLT_PATH = "M8.7 9.7 7 12.2h1.4l-.8 1.9 2.4-2.6H8.7z"
SLASH = "M2.6 14.6 13.4 8.4"
DOT = '<circle cx="11.6" cy="12.1" r="1.7"'


def build(state: str, theme: str) -> str:
    ink = INK[theme]
    parts = [
        f'<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 16 16">',
        f'<g fill="none" stroke="{ink}" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">',
    ]

    if state == "disconnected":
        # Empty case, no buds: the one state where the pods are visibly absent. A strike-through
        # was tried first and turned into a blob at 16 px.
        parts.append(f'<path d="{CASE}"/>')
    else:
        dash = ' stroke-dasharray="2.4 1.6"' if state == "unknown" else ""
        parts.append(f'<path d="{CASE}"{dash}/>')
        parts.append(f'<g stroke-width="2.4"><path d="{BUD_LEFT}"/><path d="{BUD_RIGHT}"/></g>')

        if state == "charging":
            parts.append(f'<path d="{BOLT_PATH}" fill="{BOLT[theme]}" stroke="{BOLT[theme]}" stroke-width=".6"/>')
        elif state == "low":
            parts.append(f'{DOT} fill="{ink}" stroke="none"/>')

    parts.append("</g></svg>")
    return "".join(parts)


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    for state in ("full", "low", "charging", "unknown", "disconnected"):
        for theme in ("light", "dark"):
            path = OUT / f"{state}-{theme}.svg"
            path.write_text(build(state, theme), encoding="utf-8")
    print(f"wrote {len(list(OUT.glob('*.svg')))} files to {OUT}")


if __name__ == "__main__":
    main()
