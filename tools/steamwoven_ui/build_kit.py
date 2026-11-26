"""ARC-DEV-2454: make the kit manifest `tools/steamwoven_ui/kit.json` from the census of docs/ui (identifiers, sizes and verdicts only).

    python3 -B tools/steamwoven_ui/build_kit.py            write kit.json
    python3 -B tools/steamwoven_ui/build_kit.py --check    fail when kit.json differs from what the census gives

Rules (TRD SWI-KIT-1..3, SWI-BDR-8, SWI-LAY-7):
- One part per original art file; arts that are one file (same name and size, such as `cncl_big` 32 and 202) are one part whose
  `source_arts` lists every number (SWI-KIT-3). The 25 files without a message number are named `file:NAME`.
- Master size = the original unit size x 3.6 (the 2880 x 2160 composition is 800 x 600 units), snapped to the 6 px grid with halves up, so
  800 x 400 is 2880 x 1440, 358 x 400 is 1290 x 1440 (SWI-BDR-5) and the strips 41 and 159 units are 150 and 570 px: the strips' heights are the
  grid choice 150 / 570, fixed by #2927 (ARC-UI-2927; the units 147.6 / 572.4 are off the grid, so only the grid heights bake to the four steps exactly).
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

from tools.steamwoven_ui import button_kit  # noqa: E402
from tools.steamwoven_ui import census as census_mod  # noqa: E402
from tools.steamwoven_ui import fixtures, sheet_parts  # noqa: E402
from tools.steamwoven_ui import gauge_kit  # noqa: E402
from tools.steamwoven_ui import hardware_build  # noqa: E402
from tools.steamwoven_ui import hardware_kit  # noqa: E402
from tools.steamwoven_ui import marker_kit  # noqa: E402
from tools.steamwoven_ui import popup_kit  # noqa: E402
from tools.steamwoven_ui import wide_button_kit  # noqa: E402
from tools.steamwoven_ui import disc_kit  # noqa: E402
from tools.steamwoven_ui import parchment_kit  # noqa: E402
from tools.steamwoven_ui import round_kit  # noqa: E402
from tools.steamwoven_ui import school_kit  # noqa: E402
from tools.steamwoven_ui import small_kit  # noqa: E402
from tools.steamwoven_ui import kit_check  # noqa: E402

MANIFEST = Path(__file__).resolve().parent / "kit.json"

# ARC-UI-2485 (B-ICONS, owner decision 2026-11-24): the pictures the pack cannot serve are made BY RULE from the original's own frames (`icon_rule.py upscale`, no Codex),
# so these parts are masters: the eight map indicators (jump, turn-based, waypoint, scroll lines in the iso and top-down looks) and the nine arts the pack has no frame for
# (the eight scroll cursors and `scroll_not`). `hoverover` (468) and `BlockedShot` (845) are NOT here: the pack serves them through the identical arts `hoverunder` and
# `magic-tech-penalty` (the audit #3144 looked them up by name and listed them as frameless), and its redraws are better than a rule's. Art numbers of the census.
ICON_RULE_ARTS = frozenset({"350", "351", "475", "476", "614", "615", "672", "673", "678", "679", "680", "681", "682", "683", "684", "685", "686"})
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
             layers: list[str] | None = None, states: list[str] | None = None, master: bool = False, provenance: str | None = None) -> dict:
    return {"id": ident, "kind": kind, "size": None, "size_basis": "open", "anchor": anchor, "layers": layers or [], "states": states or [],
            "used_by": used_by, "source_arts": [], "family": family, "batch": batch, "provenance": provenance or (f"fit:{ident}" if master else None),
            "status": "master" if master else "planned", "frames": 1,
            "note": note}


CAP_WIDTH, BAR_WIDTH = 72, 24  # ARC-UI-2927: an end cap's master width and its brass bar (the rest is the soft shadow)
STRIP_HEIGHT = {"top": 150, "bottom": 570}
FIXTURE_BATCH = {"G05": "BD-B", "G15": "BD-F"}  # the window family of a fixture's first window; every other fixture is the inventory family's (BD-A)


def fixture_part(fixture: fixtures.Fixture) -> dict:
    """ARC-UI-3098: a required detail the old window arts painted into themselves, a part of its own at the size of its place in the original (`fixtures.py` draws it by rule)."""
    batch = FIXTURE_BATCH.get(fixture.windows[0], "BD-A")
    kind = "grid" if fixture.ident.startswith("grid.") else "fixture"
    if not fixture.cut:
        how = "no original art, drawn by fixtures.py (ARC-UI-3098)"
    elif fixture.ident == sheet_parts.NEEDLE:
        how = "cut from the player's own HD pack by sheet_parts.py (ARC-UI-3321)"
    elif fixture.ident in sheet_parts.BY_ID:
        how = "cut from the player's own HD frame by sheet_parts.py (ARC-UI-3321)"
    else:
        how = "cut from the player's own HD frame by label_parts.py (ARC-UI-3299)"
    part = new_part(fixture.ident, kind, "zone" if kind == "grid" else "window", list(fixture.windows), batch, batch, f"{fixture.note}; {how}")
    part["size"], part["size_basis"] = list(fixture.master), "placed"
    return part


def new_parts() -> list[dict]:
    parts = [
        new_part(f"cap.{strip}_strip_{side}", "cap", f"{strip}-strip-{side}-end", [window], "BOARD", "B-BOARD",
                 f"end cap of the {strip} HUD strip's {side} end: a brass edge and a soft shadow on the world (SWI-LAY-12); no original art; "
                 f"size {CAP_WIDTH} x {STRIP_HEIGHT[strip]} fixed by #2927 (a {BAR_WIDTH} px brass bar at the inner edge, the shadow outward)", master=True)
        for strip, window in (("top", "H01"), ("bottom", "H02")) for side in ("left", "right")]
    for material in ("wood", "dark_wood", "brass", "iron", "paper", "parchment"):
        parts.append(new_part(f"material.{material}", "material", "none", [], None, None,
                              "surface of the material sheet (#2455, #2456); no original art; size fixed with the sheet"))
    parts.extend(fixture_part(fixture) for fixture in fixtures.FIXTURES)
    for decor in ("corner_piece", "rivet"):
        parts.append(new_part(f"decor.{decor}", "decor", "none", [], None, "B-HARDWARE",
                              "ornament that is not hardware; no original art; size fixed by #2481"))
    # the round button of the bottom strip (pilot ARC-UI-3058, #3058): one plate for the round buttons, idle (also the hover plate: the original's hover frame
    # shares the idle ring) and pressed, and a glyph in two looks, idle (dull) and lit (hover and pressed frames), each its own picture
    for ident, states, what in (("plate.round_button", ["idle", "hover"], "the shared round plate of the bottom strip's buttons (arts 470 to 473), idle and hover look"),
                                ("plate.round_button_pressed", ["pressed"], "the same plate in its pressed look (the unlit rim of the original's pressed frame)")):
        parts.append(new_part(ident, "plate", "bottom-strip", ["H02"], None, "B-BUTTONS", f"{what}; no symbol on it (the glyph is its own part); size 138 x 138 fixed by the pilot #3058",
                              states=states, master=True))
    # the glyphs are drawn by a rule from the original's own frame (no Codex, round 3 of the pilot): `glyph_cells.py cells`, a canvas of the whole 39-unit frame, so the glyph
    # lies where the original's does (the checker is one original pixel wide, the silhouette smooth); `flat` and `bevel` are two looks of the same rule
    for glyph, art in (("key", "472"), ("gear", "471"), ("arrows", "473"), ("shield", "470")):
        for ident, states, what in ((f"glyph.{glyph}", ["idle"], "the idle frame's tone"), (f"glyph.{glyph}_lit", ["hover", "pressed"], "the tone of the hover and pressed frames")):
            parts.append(new_part(ident, "glyph", "bottom-strip", ["H02"], None, "B-BUTTONS",
                                  f"the {glyph} symbol of round button art {art}, {what}: glyph_cells.py draws it from the original frame (no Codex), "
                                  "a 156 x 156 canvas of the whole frame, so the size is open",
                                  states=states, master=True, provenance=f"rule:glyph_cells:{ident}"))
    parts.extend(square_hud_parts())
    parts.extend(round_hud_parts())
    parts.extend(hud_hardware_parts())
    parts.extend(lens_ring_parts())
    parts.append(wide_button_part())
    return parts


def wide_button_part() -> dict:
    """ARC-UI-3271, B-BUTTONS: the schematic window's wide round button (bar, ring, two wells; baked into art 365) as one part, PACK FIRST: the old HD pack's frame, an analytic alpha and the soft shadow, by `wide_button_kit.py`."""
    return new_part(wide_button_kit.PART, "plate", "window", ["G15"], "BD-F", "B-BUTTONS",
                    "the schematic window's wide round button (brass bar, ring round the dome, two wells open); pack first: the old HD pack's frame of art 365 + analytic alpha + soft shadow, "
                    "wide_button_kit.py, 600 x 354; size open",
                    master=True, provenance=f"rule:wide_button_kit:{wide_button_kit.PART}")


def hud_hardware_parts() -> list[dict]:
    """ARC-UI-2481, slice 1 of B-HARDWARE: the metal of the two HUD strips, and ARC-UI-3260, slice 2: the metal of the menu screens' two strips (kit layers over the strip art, SWI-LAY-12), Codex sheets (hardware_build.py);
    a part placed several times (the ten slot frames, the four socket rings, the rail unit) is one master, a right-hand detail that mirrors the left one is its mirror."""
    parts = []
    menu_windows = ["M05", "M06", "M07", "M08", "M09", "M10"]   # ARC-UI-3260, slice 2: the metal of the menu screens' strips (census M05 to M10)
    for ident in hardware_build.HARDWARE_ORDER:
        info = hardware_build.PARTS[ident]
        strips = {instance[0] for instance in info["instances"]}
        used_by = (["H01"] if "board.inttop" in strips else []) + (["H02"] if "board.intbotom" in strips else []) + (menu_windows if strips & {"board.charcreatetopbar", "board.charcreatebottombar"} else [])
        top = info["strip"] in ("board.inttop", "board.charcreatetopbar")
        what = "mirror of " + info["mirror_of"].split(".")[1] if info.get("mirror_of") else "drawn once"
        parts.append(new_part(ident, "hardware", "top-strip" if top else "bottom-strip", used_by, None, "B-HARDWARE",
                              f"{info['note']}; a layer over the strip art ({what}); hardware_build.py, {'ARC-UI-3260' if info['strip'].startswith('board.charcreate') else 'ARC-UI-2481'}; open size", master=True))
    return parts


def lens_ring_parts() -> list[dict]:
    """ARC-UI-3261 (B-HARDWARE slice 3): the whole lens ring, a new part of an open size (the art's square plus 18 px on every side, centred on the lens) in two looks: open circle and with the dark fill of art 367. The
    eight lens arts' own rows hold the same ring on the art's own square (a drop-in for the old art; the old art clipped the ring where the circle touches its edge, so those four places are cut there)."""
    return [new_part(ident, "hardware", "window", used, None, "B-HARDWARE", hardware_kit.note_of(ident), master=True, provenance=hardware_kit.provenance_of(ident))
            for ident, used in (("hardware.lens_ring", ["G01", "G02", "G03", "G04", "G05", "G10", "G11", "G12", "G13", "G14", "G15", "G16", "G17", "H22", "M04", "M05", "M06", "M10"]),
                                ("hardware.lens_ring_filled", []))]


def square_hud_parts() -> list[dict]:
    """ARC-UI-2482, slice 1 of B-BUTTONS: the five square buttons of the top strip. Besides the plates and the lit faces (rows from the census, set to master below) each button has a glyph in two looks
    and the hover and pressed overlays (and, for the four lit arts with a second frame, the lit face's pressed overlay), made BY RULE from the original's frames by `button_kit.py` (no Codex)."""
    parts = []
    for b in button_kit.BUTTONS:
        for ident, states, what in ((f"glyph.{b.name}", ["idle"], f"the symbol of art {b.art} as the idle frame shows it over the plate"),
                                    (f"glyph.{b.name}_lit", ["hover", "pressed"], f"the symbol of art {b.art} lit (the lit art {b.on_art}'s frame 0: red, with its glow)"),
                                    (f"plate.{b.name}_hover", ["hover"], f"overlay of art {b.art}'s hover frame over its idle frame (the symbol turns red and glows)"),
                                    (f"plate.{b.name}_pressed", ["pressed"], f"overlay of art {b.art}'s pressed frame over its idle frame (the symbol turns red, the inset darkens)")):
            parts.append(new_part(ident, "plate" if ident.startswith("plate.") else "glyph", "top-strip", ["H01"], None, "B-BUTTONS",
                                  f"{what}; button_kit.py draws it from the original frames (no Codex), a 120 x 120 canvas of the whole 34-unit frame, so the size is open",
                                  states=states, master=True, provenance=f"rule:button_kit:{ident}"))
        if b.on_art != 561:  # Character's lit art has one frame
            parts.append(new_part(f"state.{b.on_name}_pressed", "state", "top-strip", ["H01"], None, "B-BUTTONS",
                                  f"overlay of the lit art {b.on_art}'s pressed frame over its frame 0; button_kit.py draws it from the original frames (no Codex), a 120 x 120 canvas, so the size is open",
                                  states=["pressed"], master=True, provenance=f"rule:button_kit:state.{b.on_name}_pressed"))
    return parts


def round_hud_parts() -> list[dict]:
    """ARC-UI-3202, slice 2 of B-BUTTONS: the four round buttons of the bottom strip (arts 470 to 473) and the ball of the Fate Points and Sleep buttons (art 137). Besides the plates (rows from the census, set to
    master below) each round button has its glyph and the hover and pressed overlays, the ball its two empty glyphs and the pressed overlay, made BY RULE from the original's frames by `round_kit.py` (no Codex)."""
    parts = []
    for b in round_kit.ROUND_BUTTONS:
        for ident, kind, states, what in ((f"glyph.{b.name}", "glyph", ["idle"], f"the symbol of art {b.art} as the idle frame shows it over the plate"),
                                          (f"plate.{b.name}_hover", "plate", ["hover"], f"overlay of art {b.art}'s hover frame over its idle frame (the symbol turns lit and glows)"),
                                          (f"plate.{b.name}_pressed", "plate", ["pressed"], f"overlay of art {b.art}'s pressed frame over its idle frame (the lit symbol, and the plate darkened where the original darkens it)")):
            parts.append(new_part(ident, kind, "bottom-strip", ["H02"], None, "B-BUTTONS",
                                  f"{what}; round_kit.py draws it from the original frames (no Codex), a 138 x 138 canvas, so the size is open",
                                  states=states, master=True, provenance=f"rule:round_kit:{ident}"))
    for glyph in round_kit.BALL_GLYPHS:
        parts.append(new_part(f"glyph.{glyph}", "glyph", "top-strip", ["H01"], None, "B-BUTTONS",
                              f"the {glyph.split('_')[0]} button's glyph on art 137: EMPTY (the ball has no symbol; the strip art 185 holds the infinity sign and the hourglass); an 84 x 84 layer so the runtime composes the face; size open",
                              states=["idle"], master=True, provenance=f"rule:round_kit:glyph.{glyph}"))
    parts.append(new_part("plate.lilgrnbut_pressed", "plate", "top-strip", ["H01"], None, "B-BUTTONS",
                          "overlay of art 137's pressed frame over its idle frame (the dome darkens, the rim brightens); round_kit.py draws it from the original frames (no Codex), 84 x 84, size open",
                          states=["pressed"], master=True, provenance="rule:round_kit:plate.lilgrnbut_pressed"))
    return parts


def dome_hud_parts(plates: dict[str, dict]) -> list[dict]:
    """ARC-UI-3203, slice 3 of B-BUTTONS: the glyph and the hover and pressed overlay of every dark-dome button whose plate row the census gave (the skills trio of the character sheet on the round plate: `round_kit.py`;
    the crescent family, Cancel / Done / Clear and the two Big Green arrows: `disc_kit.py`), BY RULE from the original's frames (no Codex). Each part sits where its plate sits (anchor and windows)."""
    parts = []
    rows = [(b.name, b.art, "round_kit.py", f"a {round_kit.ROUND_MASTER} x {round_kit.ROUND_MASTER} canvas") for b in round_kit.SKILL_BUTTONS]
    rows += [(m.name, m.art, "disc_kit.py", f"a {f.master[0]} x {f.master[1]} canvas") for f in disc_kit.FAMILIES for m in f.members]
    rows += [(t.name, t.art, "school_kit.py", None) for t in school_kit.TABS]  # ARC-UI-3204: the college tabs and the skill-school buttons (coloured domes), the canvas is the plate's size
    for name, art, tool, canvas in rows:
        plate = plates.get(f"plate.{name}")
        if canvas is None and plate is not None:
            canvas = f"{'an' if str(plate['size'][0]).startswith('8') else 'a'} {plate['size'][0]} x {plate['size'][1]} canvas"
        pack = tool == "school_kit.py"
        if plate is None:  # a census without the button (a test's synthetic one): nothing to hang the parts on (the real census has all eight, `build_kit.py --check` pins it)
            continue
        rule = tool.removesuffix(".py")
        for ident, kind, states, what in ((f"glyph.{name}", "glyph", ["idle"], f"the symbol of art {art} as the idle frame shows it over the plate" if not pack else f"an EMPTY layer: the symbol of art {art} is in the plate (the pack's frame as it is)"),
                                          (f"plate.{name}_hover", "plate", ["hover"], f"overlay of art {art}'s hover frame over its idle frame (" + ("the pack's hover frame in full, over the idle face" if pack else "the symbol lights up and glows") + ")"),
                                          (f"plate.{name}_pressed", "plate", ["pressed"], f"overlay of art {art}'s pressed frame over its idle frame (" + ("the pack's pressed frame in full, over the idle face" if pack else "the lit symbol, and the plate darkened where the original darkens it") + ")")):
            parts.append(new_part(ident, kind, plate["anchor"], list(plate["used_by"]), None, "B-BUTTONS",
                                  f"{what}; {tool} " + (("writes it, " if ident.startswith("glyph.") else "cuts it from the HD pack's frame, ") + "no Codex" if pack else "draws it from the original frames (no Codex)") + f", {canvas}, so the size is open", states=states, master=True, provenance=f"rule:{rule}:{ident}"))
    return parts


SMALL_FAMILY_TEXT = {  # family -> (provenance prefix, the idle plate's note, the glyph's note, the overlay's note)
    "rect": ("rule", "the old HD pack's idle frame at the master size, opaque (the owner kept the stone Add / Remove and the follower arrows from the pack, ARC-UI-3206); the symbol is in the frame",
             "EMPTY: the symbol of art {art} is in the pack's frames", "the pack's {state} frame of art {art}, a full frame laid over the idle plate"),
    "dome": ("rule", "the round face of the bottom strip's buttons (#3202) with the brass ring of the sockets (#2481), 102 x 108 px, no shadow (a runtime layer, #3249); symbol and states by rule E from art {art}'s own frames (small_kit.py, ARC-UI-3323)",
             "the red checkered symbol of art {art} by rule E, with a soft relief of its slot", "what art {art}'s {state} frame adds over its idle frame (rule E, as round_kit.py), clipped to the plate"),
    "codex": ("codex", "generated by Codex as a sheet of cells (original and the neighbouring pack frames as input), cut and reduced to the master size (small_kit.py, ARC-UI-3323); alpha: opaque, or the triangle button cut from its ground",
              "EMPTY: the symbol of art {art} is in the generated plate", "the generated {state} look of art {art}, a full frame laid over the idle plate"),
}


def small_parts(plates: dict[str, dict]) -> list[dict]:
    """ARC-UI-3206 and ARC-UI-3323, slice 6 of B-BUTTONS: the glyph and the hover and pressed plate of every small stone and dark button, by family (`small_kit.py`): the pack's frames (the stone Add / Remove and the
    follower arrows), by rule from the round plate of #3202 (the skill and slider buttons), or generated by Codex (the olive plus and minus, the scroll arrows, the follow scrolls, the skill-point plus and minus)."""
    parts = []
    for b in (*small_kit.SMALL, *small_kit.GENERATED):
        plate = plates.get(f"plate.{b.name}")
        if plate is None:  # a census without the button (a test's synthetic one)
            continue
        prefix, _, glyph_note, overlay_note = SMALL_FAMILY_TEXT[b.family]
        canvas = f"a {b.master[0]} x {b.master[1]} canvas"
        rows = [("glyph", "", "glyph", ["idle"], glyph_note.format(art=b.art))]
        for state in ("hover", "pressed"):
            if state in b.frames:
                rows.append(("plate", f"_{state}", "plate", [state], overlay_note.format(art=b.art, state=state)))
        for pre, suffix, kind, st, what in rows:
            ident = f"{pre}.{b.name}{suffix}"
            parts.append(new_part(ident, kind, plate["anchor"], list(plate["used_by"]), None, "B-BUTTONS",
                                  f"{what}; small_kit.py, {canvas}, so the size is open", states=st, master=True, provenance=f"{prefix}:small_kit:{ident}"))
    return parts


def marker_parts(bases: dict[str, dict]) -> list[dict]:
    """ARC-UI-3207, slice 7 of B-BUTTONS: the second frame (the highlighted look) of a map marker whose art has one, a part of its own (`marker_kit.py`) at the size of that frame."""
    parts = []
    for ident, spec in marker_kit.PARTS.items():
        base = bases.get(f"glyph.{spec.art.name}")
        if spec.frame == 0 or base is None:  # a census without the marker (a test's synthetic one)
            continue
        note = f"{marker_kit.note_of(ident)}; a {spec.size[0]} x {spec.size[1]} canvas centred on the marker's place, so the size is open"
        parts.append(new_part(ident, "glyph", base["anchor"], list(base["used_by"]), None, "B-BUTTONS", note, states=[], master=True, provenance=marker_kit.provenance_of(ident)))
    return parts


SMALL_PLATES = {f"plate.{b.name}": b for b in (*small_kit.SMALL, *small_kit.GENERATED)}
ROUND_HUD_PLATES = {f"plate.{b.name}": b for b in (*round_kit.ROUND_BUTTONS, round_kit.BALL)}
SKILL_PLATES = {f"plate.{b.name}": b for b in round_kit.SKILL_BUTTONS}
DISC_PLATES = {f"plate.{m.name}": (f, m) for f in disc_kit.FAMILIES for m in f.members}
SCHOOL_PLATES = {f"plate.{t.name}": t for t in school_kit.TABS}
BOX_PLATES = {f"plate.{b.name}": b for b in parchment_kit.BOXES}
BOX_STATES = {f"state.{n}": b for b in parchment_kit.BOXES for n in (b.on_name, b.ill_name)}
SQUARE_HUD_PLATES = {f"plate.{b.name}": b for b in button_kit.BUTTONS}
SQUARE_HUD_STATES = {f"state.{b.on_name}": b for b in button_kit.BUTTONS}
GAUGE_PARTS = frozenset(gauge_kit.coverage())
POPUP_PARTS = frozenset(popup_kit.coverage())    # ARC-UI-3278 (B-HARDWARE slice 4): the pop-up panel bases and frames
HARDWARE_RULE_PARTS = frozenset(p.ident for p in hardware_kit.PARTS if p.art)  # the parts made from census arts (the whole lens rings are new parts)


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
        if ICON_RULE_ARTS.issuperset(part["source_arts"]) and part["source_arts"]:
            part["provenance"], part["status"] = f"rule:icon_rule:{ident}", "master"
            part["note"] = ("by rule from the original's own frames (icon_rule.py upscale, no Codex), square or smooth colour, exact alpha; "
                            "HD frames at x4 of each original frame (ARC-UI-2485)")
        if ident in SQUARE_HUD_PLATES:
            part["status"], part["provenance"] = "master", f"fit:{ident}"
            part["note"] = "the empty square plate: one Codex item (plate-square-hud), fitted to this part; the symbol, its states and the lit faces are made by rule (button_kit.py, ARC-UI-2482)"
        if ident in ROUND_HUD_PLATES:
            part["status"], part["provenance"] = "master", f"fit:{ident}"
            part["note"] = ("the empty ball: one Codex item (plate-ball-hud), fitted to this part; the pressed overlay is made by rule (round_kit.py, ARC-UI-3202)"
                            if ident == "plate.lilgrnbut" else
                            "the empty round face (no ring: the strip draws the socket): one Codex item (plate-round-hud), fitted to this part; symbol and states by rule (round_kit.py, ARC-UI-3202)")
        if ident in SKILL_PLATES:
            part["status"], part["provenance"] = "master", f"fit:{ident}"
            part["note"] = ("the empty round face of the character sheet's skill-group button: the very same plate as the bottom strip's round buttons (item plate-round-hud, fitted to this part); "
                            "symbol and states by rule (round_kit.py, ARC-UI-3203)")
        if ident in DISC_PLATES:
            part["status"], part["provenance"] = "master", f"fit:{ident}"
            part["note"] = (f"the dark dome with its brass crescent: the dome is one Codex item ({DISC_PLATES[ident][0].prompt}), fitted to this part; the crescent, the gloss arc, the symbol and the states are made by rule (disc_kit.py, ARC-UI-3203)"
                            + (f"; art {DISC_PLATES[ident][1].twin.split('-')[0]} is the same picture" if DISC_PLATES[ident][1].twin else ""))
        if ident in SCHOOL_PLATES:
            part["status"], part["provenance"] = "master", f"rule:school_kit:{ident}"
            part["note"] = ("the pack's own idle frame of the button as it is, cut to the ring's circle by an analytic round alpha (school_kit.py, ARC-UI-3204, owner 2026-11-25); the states are the pack's frames"
                            if ident.removeprefix("plate.") not in school_kit.CODEX_SYMBOLS else
                            "the pack's frame with its symbol redrawn by Codex (the pack read it wrongly), cut to the ring's circle by an analytic round alpha (school_kit.py, ARC-UI-3204); the states are redrawn the same way")
        if ident in BOX_PLATES or ident in BOX_STATES:  # ARC-UI-3205: pack-first (owner rule 2026-11-25): the old HD pack's frame with an analytic alpha, no Codex
            part["status"], part["provenance"] = "master", f"rule:parchment_kit:{ident}"
            part["note"] = (f"the idle face of art {part['source_arts'][-1]}: the old HD pack's frame (pack-first rule) resampled to the master, the alpha the ring's ellipse united with the four corner triangles (parchment_kit.py, ARC-UI-3205); the symbol is in the frame" if ident in BOX_PLATES else
                            f"the {'lit' if ident.endswith('_on') else 'refused'} face of art {part['source_arts'][-1]}: the old HD pack's frame of that state (green or red corners kept), the same alpha (parchment_kit.py, ARC-UI-3205)")
        if ident in SMALL_PLATES:
            family = SMALL_PLATES[ident].family
            part["status"], part["provenance"] = "master", f"{SMALL_FAMILY_TEXT[family][0]}:small_kit:{ident}"
            part["note"] = SMALL_FAMILY_TEXT[family][1].format(art=SMALL_PLATES[ident].art)
        if ident in SQUARE_HUD_STATES:
            part["status"], part["provenance"] = "master", f"rule:button_kit:{ident}"
            part["note"] = f"the lit face of art {part['source_arts'][-1]}: the plate again; its symbol is glyph.{SQUARE_HUD_STATES[ident].name}_lit (button_kit.py, ARC-UI-2482)"
        if ident in GAUGE_PARTS:  # ARC-UI-2483 (B-GAUGES): every part of the batch is a master once gauge_kit.py has made it (a rule record or a fit record)
            part["status"], part["provenance"] = "master", gauge_kit.provenance_of(ident)
            part["note"] = gauge_kit.note_of(ident, part["frames"])
        if ident in POPUP_PARTS:  # ARC-UI-3278: every pop-up panel base is a master once popup_kit.py has made it (a rule record)
            part["status"], part["provenance"] = "master", popup_kit.provenance_of(ident)
            part["note"] = popup_kit.note_of(ident)
        if ident in marker_kit.PARTS:  # ARC-UI-3207: the map markers and slot covers, the old HD pack's frame or by rule (the picked mix of marker_kit.py)
            part["status"], part["provenance"] = "master", marker_kit.provenance_of(ident)
            part["note"] = marker_kit.note_of(ident)
        if ident in HARDWARE_RULE_PARTS:  # ARC-UI-3261 (B-HARDWARE slice 3): lens rings, slot insets, slot wood, plugs and small frames, by rule from the original's frames
            part["status"], part["provenance"] = "master", hardware_kit.provenance_of(ident)
            part["note"] = hardware_kit.note_of(ident)
        if lead.kind == "board":
            part["status"], part["provenance"] = "master", f"fit:{part['id']}"
            part["note"] = "strip height is the grid choice 150 / 570 (units 147.6 / 572.4), fixed by #2927: the only heights that bake to the four steps exactly"
        parts.append(part)
    parts.extend(new_parts())
    parts.extend(dome_hud_parts({p["id"]: p for p in parts if p["id"] in SKILL_PLATES or p["id"] in DISC_PLATES or p["id"] in SCHOOL_PLATES}))
    parts.extend(small_parts({p["id"]: p for p in parts if p["id"] in SMALL_PLATES}))
    parts.extend(marker_parts({p["id"]: p for p in parts if p["id"] in marker_kit.PARTS}))
    by_art = {s: p for p in parts for s in p["source_arts"]}
    # Layers over a part (draw order, bottom first): the end caps over a HUD strip, the grid layer over a backdrop that paints it in today.
    for art, layers in (("335", hardware_build.layers_of("board.charcreatebottombar")), ("336", hardware_build.layers_of("board.charcreatetopbar")),
                        ("184", ["cap.bottom_strip_left", "cap.bottom_strip_right"] + hardware_build.layers_of("board.intbotom")),
                        ("185", ["cap.top_strip_left", "cap.top_strip_right"] + hardware_build.layers_of("board.inttop")),
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
