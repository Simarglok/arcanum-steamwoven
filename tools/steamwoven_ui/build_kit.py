"""ARC-DEV-2454: make the kit manifest `tools/steamwoven_ui/kit.json` from the census of docs/ui (identifiers, sizes and verdicts only).

    python3 -B tools/steamwoven_ui/build_kit.py            write kit.json
    python3 -B tools/steamwoven_ui/build_kit.py --check    fail when kit.json differs from what the census gives

Rules (TRD SWI-KIT-1..3, SWI-BDR-8, SWI-LAY-7):
- One part per original art file; arts that are one file (same name and size, such as `cncl_big` 32 and 202) are one part whose
  `source_arts` lists every number (SWI-KIT-3). The 25 files without a message number are named `file:NAME`.
- Master size = the original unit size x 3.6 (the 2880 x 2160 composition is 800 x 600 units), snapped to the 6 px grid with halves up, so
  800 x 400 is 2880 x 1440, 358 x 400 is 1290 x 1440 (SWI-BDR-5) and the strips 41 and 159 units are 150 and 570 px: the strips' heights are the
  open question of the TRD (`size_basis` "provisional", fixed by #2927).
- Parts no original art feeds are rows of their own with an open size (`size_basis` "open", fixed by their batch issue): the four end caps, the
  materials of the material sheet, the grid layers painted into arts 221 and 365 today, and the corner and rivet decor.
- No pixels, no full-width board, no outward backdrop and no gap wood (SWI-BDR-8): none of them is a part.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[2]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from tools.steamwoven_ui import census as census_mod  # noqa: E402
from tools.steamwoven_ui import kit_check  # noqa: E402

MANIFEST = Path(__file__).resolve().parent / "kit.json"
PLATE_STATES = ["idle", "hover", "pressed", "disabled"]  # SWI-KIT-5: one button size's states are generated together
FAMILY_OF_BATCH = {"B-BOARD": "BOARD", **{f"BD-{c}": f"BD-{c}" for c in "ABCDEFGH"}}


snap = kit_check.snap  # original units -> master px on the 6 px grid (units x 3.6, halves up)
ART_NOTES = {
    "2": "census: a solid black fill, no kit part needed; kept as the row of the not-built credits and View Intro screens (M13) until a batch drops it",
    "330": "census: the network lobby backdrop is out of scope (M14, not built); kept as a row until a batch or the owner drops it",
}


def window_anchor(text: str) -> str:
    """The census' anchor text of a window -> the kit's anchor class."""
    lowered = text.lower()
    if lowered.startswith("top strip"):
        return "top-strip"
    if lowered.startswith("bottom strip"):
        return "bottom-strip"
    if "left edge" in lowered:
        return "window-edge"
    if lowered.startswith("full screen"):
        return "composition"
    if "panel" in lowered or "two strips" in lowered:
        return "zone"
    if "pop-up" in lowered:
        return "overlay"
    if "lens" in lowered:
        return "window"
    return "world"  # follows an object or the cursor, the play view's corners, the hidden-interface mode


def slug(name: str) -> str:
    return re.sub(r"[^a-z0-9]+", "_", name.lower()).strip("_")


def part_anchor(kind: str, size: tuple[int, int], windows: tuple[str, ...], classes: dict[str, str]) -> str:
    if kind == "cursor":
        return "cursor"
    if kind == "board":
        return "top-strip" if size[1] < 300 else "bottom-strip"
    if kind == "backdrop":
        return "composition" if size == kit_check.COMPOSITION else "zone"
    found = {classes[w] for w in windows}
    return found.pop() if len(found) == 1 else "window"  # shared by windows of different places, or by none: placed by its window


def new_part(ident: str, kind: str, anchor: str, used_by: list[str], family: str | None, batch: str | None, note: str,
             layers: list[str] | None = None, states: list[str] | None = None, master: bool = False) -> dict:
    return {"id": ident, "kind": kind, "size": None, "size_basis": "open", "anchor": anchor, "layers": layers or [], "states": states or [],
            "used_by": used_by, "source_arts": [], "family": family, "batch": batch, "provenance": f"fit:{ident}" if master else None,
            "status": "master" if master else "planned", "frames": 1,
            "note": note}


def new_parts() -> list[dict]:
    parts = [
        new_part(f"cap.{strip}_strip_{side}", "cap", f"{strip}-strip-{side}-end", [window], "BOARD", "B-BOARD",
                 f"end cap of the {strip} HUD strip's {side} end: a brass edge and a soft shadow on the world (SWI-LAY-12); no original art; "
                 "size fixed by #2927")
        for strip, window in (("top", "H01"), ("bottom", "H02")) for side in ("left", "right")]
    for material in ("wood", "dark_wood", "brass", "iron", "paper", "parchment"):
        parts.append(new_part(f"material.{material}", "material", "none", [], None, None,
                              "surface of the material sheet (#2455, #2456); no original art; size fixed with the sheet"))
    parts.append(new_part("grid.inventory_cells", "grid", "zone", ["G01", "G02", "G03", "G04", "G17", "M10"], "BD-A", "BD-A",
                          "inventory cell grid as its own layer over the BD-A window background (painted into the pack arts today); no original art"))
    parts.append(new_part("grid.schematic", "grid", "zone", ["G15"], "BD-F", "BD-F",
                          "schematic page grid as its own layer over art 365 (painted in today); no original art"))
    for decor in ("corner_piece", "rivet"):
        parts.append(new_part(f"decor.{decor}", "decor", "none", [], None, "B-HARDWARE",
                              "ornament that is not hardware; no original art; size fixed by #2481"))
    # the round button of the bottom strip (pilot ARC-UI-3058, #3058): one plate for the round buttons, idle (also the hover plate: the original's hover frame
    # shares the idle ring) and pressed, and a glyph in two looks, idle (dull) and lit (hover and pressed frames), each its own picture
    for ident, states, what in (("plate.round_button", ["idle", "hover"], "the shared round plate of the bottom strip's buttons (arts 470 to 473), idle and hover look"),
                                ("plate.round_button_pressed", ["pressed"], "the same plate in its pressed look (the unlit rim of the original's pressed frame)")):
        parts.append(new_part(ident, "plate", "bottom-strip", ["H02"], None, "B-BUTTONS", f"{what}; no symbol on it (the glyph is its own part); size 138 x 138 fixed by the pilot #3058",
                              states=states, master=True))
    for glyph, art in (("key", "472"), ("gear", "471"), ("arrows", "473"), ("shield", "470")):
        for ident, states, what in ((f"glyph.{glyph}", ["idle"], "dull look of the idle frame"), (f"glyph.{glyph}_lit", ["hover", "pressed"], "lit look of the hover and pressed frames")):
            parts.append(new_part(ident, "glyph", "bottom-strip", ["H02"], None, "B-BUTTONS",
                                  f"the {glyph} symbol of the bottom strip's round button (art {art}), {what}, drawn clean on a transparent background; size 108 x 108 fixed by the pilot #3058",
                                  states=states, master=True))
    return parts


def build(arts: list[census_mod.CensusArt], windows: list[census_mod.CensusWindow]) -> dict:
    classes = {w.ident: window_anchor(w.anchor) for w in windows}
    excluded: dict[str, list[str]] = {reason: [] for reason in kit_check.EXCLUDED_REASONS}
    groups: dict[tuple[str, tuple[int, int]], list[census_mod.CensusArt]] = {}
    for art in arts:
        reason = census_mod.excluded_reason(art)
        if reason:
            excluded[reason].append(art.ident)
        else:
            groups.setdefault((art.name.lower(), art.size), []).append(art)
    parts: list[dict] = []
    taken: set[str] = set()
    for group in groups.values():
        lead = next((a for a in group if a.windows), group[0])  # one file, so one kind: the row a window draws wins
        size = (snap(lead.size[0]), snap(lead.size[1]))
        used_by = sorted({w for a in group for w in a.windows})
        ident = f"{lead.kind}.{slug(lead.name)}"
        if ident in taken:
            ident += f"-{slug(group[0].ident.removeprefix('file:'))}"
        taken.add(ident)
        batch = lead.batch
        part = {"id": ident, "kind": lead.kind, "size": list(size), "size_basis": "units",
                "anchor": part_anchor(lead.kind, size, tuple(used_by), classes), "layers": [],
                "states": list(PLATE_STATES) if lead.kind == "plate" else [], "used_by": used_by,
                "source_arts": [a.ident for a in group], "family": FAMILY_OF_BATCH.get(batch), "batch": batch, "provenance": None,
                "status": "planned", "frames": max(a.frames for a in group)}
        notes = [ART_NOTES[a.ident] for a in group if a.ident in ART_NOTES]
        if notes:
            part["note"] = notes[0]
        if lead.kind == "board":
            part["size_basis"] = "provisional"
            part["note"] = "strip height is the grid choice 150 / 570 (units 147.6 / 572.4): open question of the TRD, fixed by #2927"
        parts.append(part)
    parts.extend(new_parts())
    by_art = {s: p for p in parts for s in p["source_arts"]}
    # Layers over a part (draw order, bottom first): the end caps over a HUD strip, the grid layer over a backdrop that paints it in today.
    for art, layers in (("184", ["cap.bottom_strip_left", "cap.bottom_strip_right"]), ("185", ["cap.top_strip_left", "cap.top_strip_right"]),
                        ("221", ["grid.inventory_cells"]), ("365", ["grid.schematic"])):
        if art in by_art:  # the real census has all four (a test pins it)
            by_art[art]["layers"] = layers
    parts.sort(key=lambda p: (kit_check.KINDS.index(p["kind"]), p["id"]))
    return {"schema": kit_check.SCHEMA,
            "windows": {w.ident: {"name": w.name, "anchor": classes[w.ident]} for w in windows},
            "parts": parts, "excluded": excluded}


def render(manifest: dict) -> str:
    """Stable text: the windows and the parts one per line, so a change is a small diff."""
    lines = ["{", f'  "schema": {manifest["schema"]},', '  "windows": {']
    items = [f'    {json.dumps(k)}: {json.dumps(v, ensure_ascii=False)}' for k, v in manifest["windows"].items()]
    lines.append(",\n".join(items))
    lines += ["  },", '  "parts": [']
    lines.append(",\n".join("    " + json.dumps(p, ensure_ascii=False) for p in manifest["parts"]))
    lines += ["  ],", '  "excluded": {']
    lines.append(",\n".join(f"    {json.dumps(k)}: {json.dumps(v)}" for k, v in manifest["excluded"].items()))
    lines += ["  }", "}", ""]
    return "\n".join(lines)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--check", action="store_true", help="fail when kit.json differs from the census")
    parser.add_argument("--out", type=Path, default=MANIFEST)
    args = parser.parse_args(argv)
    text = render(build(census_mod.load_arts(), census_mod.load_windows()))
    if args.check:
        current = args.out.read_text(encoding="utf-8") if args.out.exists() else ""
        if current != text:
            print(f"KIT BUILD FAIL: {args.out} differs from the census; run build_kit.py")
            return 1
        print("KIT BUILD PASS: kit.json is what the census gives")
        return 0
    args.out.write_text(text, encoding="utf-8")
    print(f"wrote {args.out} ({len(text)} bytes)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
