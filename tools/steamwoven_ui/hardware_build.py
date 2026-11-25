"""ARC-UI-2481: the hardware of the kit strips (batch B-HARDWARE, slice 1: the two HUD strips, arts 185 and 184).

    python3 -B tools/steamwoven_ui/hardware_build.py refs  [--sheet hud-bottom|hud-top|hud-top-parts] [--local-root DIR]
    python3 -B tools/steamwoven_ui/hardware_build.py cut [--sheet NAME]   # the details out of the generated sheets, placements.json
    python3 -B tools/steamwoven_ui/hardware_build.py strips            # the strip arts without the original's metal, the preview with the parts laid
    python3 -B tools/steamwoven_ui/hardware_build.py fit [--only ID]   # fit gates of the parts
    python3 -B tools/steamwoven_ui/hardware_build.py art               # fit gates of the two strip arts
    python3 -B tools/steamwoven_ui/hardware_build.py self-test

The strip art of the kit (`board.*`, ARC-UI-2927) is wood and the original's openings; its metal, rings, sockets and housings were the original's own pixels as stand-ins. This
batch makes them kit parts (TRD SWI-LAY-12: hardware is a separate layer over the strip). Each detail is drawn ONCE (a repeated one, such as the ten hotkey slot frames, the four
socket rings or the rail unit, is one part placed by coordinates; a right-hand detail that mirrors the left one is its mirror) and the parts of one strip are one Codex item, a sheet of
the details cut from the ORIGINAL art on a transparent ground, so one generation keeps one look. Everything stays under the ignored `local/steamwoven-ui/`; nothing here holds art.

The shapes below are the cut-out outlines of the details in the strip master's pixels (3.6 px per original pixel), drawn by hand from the original: rectangles, ellipses and polygons,
the holes (the inside of a ring or a frame) are cut out again. The generator redraws the shape, it does not trace it, so an outline needs only to separate the detail from the wood.
"""
from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
try:
    from . import pngio, prompt_library, tone_check
except ImportError:  # run as a script
    sys.path.insert(0, str(HERE))
    import pngio  # type: ignore
    import prompt_library  # type: ignore
    import tone_check  # type: ignore

MASTER_W = 2880
GRID = 6
SHEET_SCALE = 1.35        # the sheet reference is laid at this multiple of the master scale (the generator keeps about 1.57 MP per image)
CELL_GAP = 60             # master px between two cells of a sheet
SHEET_WIDTH = 1180        # master px
STRIPS = {                # strip part id -> (work folder pair, original upscaled file, strip height)
    "board.inttop": ("hud", "original-top.png", 150),
    "board.intbotom": ("hud", "original-bottom.png", 570),
    "board.charcreatetopbar": ("charcreate", "original-top.png", 150),         # ARC-UI-3260: the two-strip menu screens' strips (arts 336 and 335)
    "board.charcreatebottombar": ("charcreate", "original-bottom.png", 570),
}

# Shape primitives (strip master px): ("rect", x0, y0, x1, y1) | ("ellipse", cx, cy, rx, ry) | ("poly", x, y, x, y, ...); a leading "-" makes a hole (cut out of what came before).
# A part: strip, shapes, instances (where the part stands in the strip: its top-left corner in the master; the first is the one the sheet is cut from; "mirror" flips it).
PARTS: dict[str, dict] = {
    "hardware.hud_vial": {
        "sheet": "hud-bottom", "strip": "board.intbotom", "note": "the health vial's housing: cap, collars, side rail with valves, bottom plate",
        "shapes": [("rect", 85, 8, 116, 34), ("rect", 50, 24, 142, 64), ("rect", 34, 60, 174, 114), ("rect", 38, 100, 174, 444), ("rect", 148, 124, 204, 154), ("rect", 148, 236, 204, 272),
                   ("rect", 6, 368, 52, 404), ("rect", 32, 424, 176, 474), ("rect", 16, 468, 196, 558)],
        "instances": [('board.intbotom', None, None, False)],
    },
    "hardware.hud_vial_r": {
        "sheet": None, "shapes": [], "strip": "board.intbotom", "mirror_of": "hardware.hud_vial", "note": "the mana vial's housing: the left one mirrored",
        "instances": [("board.intbotom", 2769, 283, True)],
    },
    "hardware.hud_cylinder_r": {
        "sheet": None, "shapes": [], "strip": "board.intbotom", "mirror_of": "hardware.hud_cylinder", "note": "the glass cylinder housing with clamps beside the message window (right)",
        "instances": [("board.intbotom", 2245, 363, True)],
    },
    "hardware.hud_ring": {
        "sheet": "hud-bottom", "strip": "board.intbotom", "note": "the brass setting of a round button socket (the button plate fills its inside)",
        "shapes": [("ellipse", 382, 128, 94, 92), ("-ellipse", 382, 128, 74, 72)],
        "instances": [('board.intbotom', 380.5, 127.7, False), ('board.intbotom', 2565.7, 124.3, False), ('board.intbotom', 2407.3, 260.5, False), ('board.intbotom', 2565.6, 421.7, False)],
    },
    "hardware.hud_dial": {
        "sheet": "hud-bottom", "strip": "board.intbotom", "note": "the gear / dial housing at the left end of the slot panel",
        "shapes": [("ellipse", 645, 72, 68, 68)],
        "instances": [('board.intbotom', None, None, False)],
    },
    "hardware.hud_slot": {
        "sheet": "hud-bottom", "strip": "board.intbotom", "note": "the brass frame of one hotkey slot (ten of them)",
        "squeeze": (18, 14), "gain": 0.8,   # the redraw's frame is 18 px thick and 26 % brighter than the original's 14 px (owner look check): its border is squeezed to 14 px, its tone brought down
        "shapes": [("rect", 704, 4, 838, 140), ("-rect", 716, 16, 826, 128)],
        "instances": [('board.intbotom', 771, 72, False), ('board.intbotom', 912, 72, False), ('board.intbotom', 1053, 72, False), ('board.intbotom', 1194, 72, False), ('board.intbotom', 1335, 72, False), ('board.intbotom', 1567, 72, False), ('board.intbotom', 1706, 72, False), ('board.intbotom', 1845, 72, False), ('board.intbotom', 1985, 72, False), ('board.intbotom', 2124, 72, False)],
    },
    "hardware.hud_rail": {
        "sheet": "hud-bottom", "strip": "board.intbotom", "note": "one unit of the rail with indicator dashes under the slots (ten of them)",
        "shapes": [("rect", 780, 138, 920, 180)],
        "instances": [('board.intbotom', 850, 159, False), ('board.intbotom', 990, 159, False), ('board.intbotom', 1130, 159, False), ('board.intbotom', 1270, 159, False), ('board.intbotom', 1410, 159, False), ('board.intbotom', 1535, 159, False), ('board.intbotom', 1675, 159, False), ('board.intbotom', 1815, 159, False), ('board.intbotom', 1955, 159, False), ('board.intbotom', 2095, 159, False)],
    },
    "hardware.hud_cylinder": {
        "sheet": "hud-bottom", "strip": "board.intbotom", "note": "the glass cylinder housing with clamps beside the message window (left)",
        "shapes": [("rect", 608, 204, 632, 280), ("rect", 630, 204, 692, 272), ("rect", 592, 284, 668, 450), ("rect", 660, 270, 692, 522), ("rect", 630, 470, 692, 522), ("rect", 604, 450, 636, 522)],
        "instances": [('board.intbotom', None, None, False)],
    },
    "hardware.hud_gem": {
        "sheet": "hud-bottom", "strip": "board.intbotom", "note": "the red lamp housing at the right end of the slot panel",
        "shapes": [("ellipse", 2250, 78, 66, 68), ("rect", 2290, 54, 2314, 106)],
        "instances": [('board.intbotom', None, None, False)],
    },
    "hardware.hud_wires": {
        "sheet": "hud-bottom", "strip": "board.intbotom", "note": "the wired gap between the fifth and the sixth slot",
        "shapes": [("rect", 1394, 2, 1500, 142)],
        "instances": [('board.intbotom', None, None, False)],
    },
}
# The top strip's two iron end sections are plates (iron only, the original's openings kept); the brass frames, the rings, the clock frame, the infinity box, the hourglass and the rivets standing on
# them are parts of their own, placed by the openings' coordinates (a detail drawn inside a plate would never sit exactly on the opening the game draws through).
# `nonwood`: what the strip's wood mask leaves of the box; `cut_holes`: the original's openings stay empty; `paint`: the details painted out of the plate's reference with the iron colour.
IRON = "rgb(50,50,50)"
TOP_SLOT_CENTRES = [75.5, 209, 342, 475]    # the four slot openings of the left plate (x of their centres; y 69.5)
_PLATE_PAINT_LEFT = [("rect", 5, 0, 146, 139), ("rect", 139, 0, 279, 139), ("rect", 272, 0, 412, 139), ("rect", 405, 0, 545, 139), ("ellipse", 606, 71, 50, 50), ("rect", 672, 10, 786, 50),
                     ("rect", 676, 54, 780, 120), ("ellipse", 863, 15, 14, 14), ("ellipse", 798, 122, 14, 14)]
_PLATE_PAINT_RIGHT = [("ellipse", 2215, 71, 50, 50), ("rect", 2320, 6, 2806, 136), ("rect", 2078, 2, 2157, 136), ("ellipse", 2005, 15, 14, 14), ("ellipse", 2067, 127, 14, 14)]
PARTS.update({
    "hardware.hud_plate_left": {
        "sheet": "hud-top", "strip": "board.inttop", "note": "the left iron end section of the top strip, iron only, with the slot, socket and window openings",
        "shapes": [("nonwood", 0, 0, 980, 150)], "cut_holes": True, "fit": "height", "paint": _PLATE_PAINT_LEFT,
        "instances": [("board.inttop", None, None, False)],
    },
    "hardware.hud_plate_right": {
        "sheet": "hud-top", "strip": "board.inttop", "note": "the right iron end section of the top strip, iron only, with the socket and window openings",
        "shapes": [("nonwood", 1940, 0, 2880, 150)], "cut_holes": True, "fit": "height", "paint": _PLATE_PAINT_RIGHT,
        "instances": [("board.inttop", None, None, False)],
    },
    "hardware.hud_top_slot": {
        "sheet": "hud-top-parts", "strip": "board.inttop", "note": "the thin brass frame of one slot opening of the top strip (four of them)",
        "brass_only": True, "squeeze": (12, 10), "gain": 0.93, "shapes": [("rect", 5, 0, 146, 139), ("-rect", 14, 7, 137, 132)],
        "instances": [("board.inttop", x, 69.5, False) for x in TOP_SLOT_CENTRES],
    },
    "hardware.hud_top_ring": {
        "sheet": "hud-top-parts", "strip": "board.inttop", "note": "the thin brass setting of a round socket of the top strip (two of them)",
        "brass_only": True, "shapes": [("ellipse", 606, 71, 49, 49), ("-ellipse", 606, 71, 38, 38)],
        "instances": [("board.inttop", 606, 70.9, False), ("board.inttop", 2215.4, 70.9, False)],
    },
    "hardware.hud_top_clock": {
        "sheet": "hud-top-parts", "strip": "board.inttop", "note": "the thin brass frame of the clock window, with a small diamond on its top and bottom edge",
        "brass_only": True, "shapes": [("rect", 2324, 9, 2803, 133), ("-rect", 2333, 18, 2794, 124)],
        "instances": [("board.inttop", 2563.5, 71, False)],
    },
    "hardware.hud_top_infinity": {
        "sheet": "hud-top-parts", "strip": "board.inttop", "note": "the infinity sign and the dark inset in a thin brass frame below it",
        "shapes": [("rect", 676, 14, 780, 48), ("rect", 679, 57, 777, 116)],
        "instances": [("board.inttop", None, None, False)],
    },
    "hardware.hud_top_hourglass": {
        "sheet": "hud-top-parts", "strip": "board.inttop", "note": "the hourglass of the top strip: brass caps, clouded glass, a little brown sand", "solid": "rgb(34,36,40)", "darken_neutral": 0.6,   # the glass is opaque like the original's (a dark body under the generated glass)
        
        "shapes": [("rect", 2082, 6, 2153, 132)],
        "instances": [("board.inttop", None, None, False)],
    },
    "hardware.hud_top_rivet": {
        "sheet": "hud-top-parts", "strip": "board.inttop", "note": "a round brass rivet of the top strips' iron sections (four on each of the HUD's and the menu screens' top strips)",
        "shapes": [("ellipse", 863, 15, 12, 12)],
        "instances": [("board.inttop", 863, 15, False), ("board.inttop", 798, 122, False), ("board.inttop", 2005, 15, False), ("board.inttop", 2067, 127, False)],
    },
})
# ARC-UI-3260, slice 2 of B-HARDWARE: the metal of the two-strip menu screens' strips (art 336 top, 335 bottom). The top strip's iron end sections have the HUD top strip's silhouette and rivets (the
# four rivets are the HUD's own part laid again at the same places), but no openings; the right section is the left one mirrored (its light recomputed to the top left). The bottom strip's metal is the two
# round socket settings (one part, two places; the buttons the screens draw stand inside them) and the frame of the message window (its inside stays the strip's dark field).
_MENU_PLATE_PAINT = [("ellipse", 863, 15, 14, 14), ("ellipse", 798, 122, 14, 14)]
PARTS.update({
    "hardware.menu_plate_left": {
        "sheet": "menu-top", "strip": "board.charcreatetopbar", "note": "the left iron end section of the menu screens' top strip, iron only, with its concave end and no openings",
        "shapes": [("nonwood", 0, 0, 980, 150)], "fit": "height", "paint": _MENU_PLATE_PAINT,
        "instances": [("board.charcreatetopbar", None, None, False)],
    },
    "hardware.menu_plate_right": {
        "sheet": None, "shapes": [], "strip": "board.charcreatetopbar", "mirror_of": "hardware.menu_plate_left", "note": "the right iron end section: the left one mirrored",
        "footprint": [("nonwood", 1900, 0, 2880, 150)],   # what the original's right section covers: erased from the strip art
        "instances": [("board.charcreatetopbar", None, None, True)],
    },
    "hardware.menu_ring": {
        "sheet": "menu-bottom", "strip": "board.charcreatebottombar", "note": "the brass setting of a round button socket of the menu screens' bottom strip (the screen's button stands inside it)",
        "keep_hole": True, "shapes": [("ellipse", 353, 301, 126, 125), ("-ellipse", 353, 301, 102, 101)],
        "instances": [("board.charcreatebottombar", 352.5, 301, False), ("board.charcreatebottombar", 2534, 301, False)],
    },
    "hardware.menu_window": {
        "sheet": "menu-bottom", "strip": "board.charcreatebottombar", "note": "the thin iron and brass frame of the message window in the middle of the menu screens' bottom strip (its inside is the strip's dark field)",
        "keep_hole": True, "erase_grow": 3, "shapes": [("rect", 704, 184, 2176, 564), ("-rect", 736, 212, 2142, 538)],
        "instances": [("board.charcreatebottombar", None, None, False)],
    },
})
for _x, _y in ((863, 15), (798, 122), (2005, 15), (2067, 127)):   # the HUD's rivet is the menu strip's rivet too (the same four places)
    PARTS["hardware.hud_top_rivet"]["instances"].append(("board.charcreatetopbar", _x, _y, False))
SHEETS = {
    "hud-top": {"prompt": "hardware-hud-top-plates", "context_crop": "1448x74+0+0", "width": 1060, "scale": 1.5, "order": ["hardware.hud_plate_left", "hardware.hud_plate_right"]},
    "hud-top-parts": {"prompt": "hardware-hud-top-parts", "context_crop": "1448x74+0+0", "width": 1000, "scale": 1.6,
                      "order": ["hardware.hud_top_clock", "hardware.hud_top_slot", "hardware.hud_top_ring", "hardware.hud_top_infinity", "hardware.hud_top_hourglass", "hardware.hud_top_rivet"]},
    "hud-bottom": {"prompt": "hardware-hud-bottom", "context_crop": "1448x290+0+796", "order": ["hardware.hud_vial", "hardware.hud_cylinder", "hardware.hud_wires", "hardware.hud_ring", "hardware.hud_dial", "hardware.hud_slot",
                                                                "hardware.hud_gem", "hardware.hud_rail"]},
    "menu-top": {"prompt": "hardware-menu-top-plates", "batch": "B-HARDWARE-MENU", "context_crop": "1448x74+0+0", "width": 1110, "min_height": 640, "scale": 1.5, "order": ["hardware.menu_plate_left"]},
    "menu-bottom": {"prompt": "hardware-menu-bottom", "batch": "B-HARDWARE-MENU", "context_crop": "1448x290+0+796", "width": 1600, "scale": 1.1, "order": ["hardware.menu_window", "hardware.menu_ring"]},
}


def layers_of(strip: str) -> list[str]:
    """The part ids laid over a strip's art, in draw order (the manifest's `layers` of the strip, after its end caps)."""
    return [ident for ident in HARDWARE_ORDER if any(instance[0] == strip for instance in PARTS[ident]["instances"])]


HARDWARE_ORDER = ["hardware.menu_plate_left", "hardware.menu_plate_right", "hardware.menu_window", "hardware.menu_ring", "hardware.hud_plate_left", "hardware.hud_plate_right", "hardware.hud_top_slot", "hardware.hud_top_ring", "hardware.hud_top_clock", "hardware.hud_top_infinity",
                  "hardware.hud_top_hourglass", "hardware.hud_top_rivet", "hardware.hud_vial", "hardware.hud_vial_r", "hardware.hud_cylinder", "hardware.hud_cylinder_r", "hardware.hud_wires",
                  "hardware.hud_ring", "hardware.hud_dial", "hardware.hud_gem", "hardware.hud_slot", "hardware.hud_rail"]


class HardwareError(Exception):
    pass


def ceil_grid(value: float) -> int:
    return -(-round(value) // GRID) * GRID


def run(command: list[str]) -> str:
    result = subprocess.run(command, capture_output=True, text=True)
    if result.returncode != 0:
        raise HardwareError(f"{command[0]} failed: {result.stderr.strip()[:300]}")
    return result.stdout


def local_root() -> Path:
    return prompt_library.default_local_root()


def shape_args(shapes: list[tuple], grow: int = 0) -> list[str]:
    """magick draw arguments that paint the shapes white and the holes black (in order), on a black ground."""
    args: list[str] = []
    for shape in shapes:
        kind, hole = shape[0].lstrip("-"), shape[0].startswith("-")
        v = list(shape[1:])
        args += ["-fill", "black" if hole else "white", "-stroke", "none"]
        sign = -1 if hole else 1
        if kind == "rect":
            args += ["-draw", f"rectangle {v[0] - sign * grow},{v[1] - sign * grow} {v[2] + sign * grow - 1},{v[3] + sign * grow - 1}"]
        elif kind == "ellipse":
            args += ["-draw", f"ellipse {v[0]},{v[1]} {v[2] + sign * grow},{v[3] + sign * grow} 0,360"]
        elif kind == "poly":
            args += ["-draw", "polygon " + " ".join(f"{v[i]},{v[i + 1]}" for i in range(0, len(v), 2))]
        else:
            raise HardwareError(f"unknown shape {shape[0]}")
    return args


def shift(shape: tuple, dx: int, dy: int) -> tuple:
    kind, v = shape[0], list(shape[1:])
    if kind.lstrip("-") == "ellipse":
        return (kind, v[0] - dx, v[1] - dy, v[2], v[3])
    return (kind,) + tuple(c - (dx if i % 2 == 0 else dy) for i, c in enumerate(v))


def bbox_of(shapes: list[tuple]) -> tuple[int, int, int, int]:
    x0 = y0 = 10 ** 9
    x1 = y1 = -10 ** 9
    for shape in shapes:
        if shape[0].startswith("-"):
            continue
        v = shape[1:]
        if shape[0] in ("rect", "nonwood"):
            a, b, c, d = v
        elif shape[0] == "ellipse":
            a, b, c, d = v[0] - v[2], v[1] - v[3], v[0] + v[2], v[1] + v[3]
        else:
            xs, ys = v[0::2], v[1::2]
            a, b, c, d = min(xs), min(ys), max(xs), max(ys)
        x0, y0, x1, y1 = min(x0, a), min(y0, b), max(x1, c), max(y1, d)
    return x0, y0, x1, y1


def cell_image(part: dict, original: Path, out: Path, root: Path, pad: int = 4) -> tuple[int, int, int, int]:
    """The cut-out of one part from the original strip (RGBA, its alpha the shape grown by a pixel): written to `out`; the part's box in the strip is returned."""
    x0, y0, x1, y1 = bbox_of(part["shapes"])
    x0, y0, x1, y1 = max(0, x0 - pad), max(0, y0 - pad), x1 + pad, y1 + pad
    w, h = x1 - x0, y1 - y0
    mask = out.with_suffix(".mask.png")
    drawn = [shift(s, x0, y0) for s in part["shapes"] if s[0] != "nonwood"]
    run(["magick", "-size", f"{w}x{h}", "xc:black", "-fill", "white", *shape_args(drawn, 1), "-blur", "0x0.7", "-alpha", "off", "-colorspace", "gray", str(mask)])
    strip = part["strip"]
    folder, _, strip_h = STRIPS[strip]
    for shape in part["shapes"]:
        if shape[0] == "nonwood":   # what the strip's wood mask leaves of the box, added to the mask
            ws = root / "work" / "B-BOARD" / folder / "w" / "wood-solid.png"
            row = 0 if strip_h == 150 else 1590
            run(["magick", str(ws), "-crop", f"{w}x{h}+{x0}+{row + y0}", "+repage", "-negate", "-alpha", "off", "-colorspace", "gray", str(mask), "-compose", "Lighten", "-composite", str(mask)])
    if part.get("cut_holes"):   # the original's openings stay empty
        alpha = root / "work" / "B-BOARD" / folder / f"alpha-{strip.replace('.', '-').replace('_', '-')}.png"
        run(["magick", str(mask), "(", str(alpha), "-crop", f"{w}x{h}+{x0}+{y0}", "+repage", "-alpha", "off", "-colorspace", "gray", "-level", "20%,60%", ")", "-compose", "Multiply", "-composite", str(mask)])
    command = ["magick", str(original), "-crop", f"{w}x{h}+{x0}+{y0}", "+repage", "-alpha", "off"]
    for shape in part.get("paint", []):   # details painted out with the iron colour (the plate's reference carries none of them)
        command += ["-fill", IRON, "-stroke", "none", *shape_args([shift(shape, x0, y0)], 0)[4:]]
    run(command + [str(mask), "-compose", "CopyOpacity", "-composite", str(out)])
    return x0, y0, x1, y1


def cmd_refs(args) -> int:
    root = args.local_root
    work = root / "work" / "B-HARDWARE"
    work.mkdir(parents=True, exist_ok=True)
    refs = root / "refs"
    refs.mkdir(parents=True, exist_ok=True)
    sheet = SHEETS[args.sheet]
    sheet_width, sheet_scale = sheet.get("width", SHEET_WIDTH), sheet.get("scale", SHEET_SCALE)
    layout = {"sheet": args.sheet, "scale": sheet_scale, "cells": {}}
    x = y = CELL_GAP
    row_h = 0
    cells = []
    for part_id in sheet["order"]:
        part = PARTS[part_id]
        folder, original, _ = STRIPS[part["strip"]]
        src = root / "work" / "B-BOARD" / folder / original
        if not src.is_file():
            raise HardwareError(f"no {src} (run board_build.py strips first)")
        cell = work / f"cell-{part_id}.png"
        box = cell_image(part, src, cell, root)
        w, h = box[2] - box[0], box[3] - box[1]
        if x + w + CELL_GAP > sheet_width:
            x, y, row_h = CELL_GAP, y + row_h + CELL_GAP, 0
        cells.append((part_id, cell, x, y, w, h, box))
        layout["cells"][part_id] = {"x": x, "y": y, "w": w, "h": h, "strip_box": list(box)}
        x += w + CELL_GAP
        row_h = max(row_h, h)
    height = max(y + row_h + CELL_GAP, sheet.get("min_height", 0))   # a sheet of one long thin detail is made taller: the generator keeps a long thin aspect poorly
    layout["size"] = [sheet_width, height]
    canvas = work / f"sheet-{args.sheet}-1x.png"
    command = ["magick", "-size", f"{sheet_width}x{height}", "xc:none"]
    for _, cell, cx, cy, _, _, _ in cells:
        command += [str(cell), "-geometry", f"+{cx}+{cy}", "-compose", "over", "-composite"]
    run(command + [str(canvas)])
    out = refs / f"{sheet['prompt']}-original.png"
    scaled = [ceil_grid(sheet_width * sheet_scale), ceil_grid(height * sheet_scale)]
    run(["magick", str(canvas), "-filter", "Lanczos", "-resize", f"{scaled[0]}x{scaled[1]}!", str(out)])
    layout["reference_size"] = scaled
    # the SHEET references: the approved material swatches, and the approved redraw of this strip (the look the details must match)
    cut = root / "material-sheet" / "cutouts"
    swatches = [cut / name for name in ("brass-edge.png", "iron.png", "rivet.png", "brass-ring-lens.png")]
    run(["magick", "montage", *map(str, swatches), "-tile", "4x1", "-geometry", "+10+10", "-background", "#808080", str(refs / f"{sheet['prompt']}-sheet-1-materials.png")])
    ctx = root / "outbox" / "style-anchors-contexts" / "ctx-hud.png"
    run(["magick", str(ctx), "-crop", sheet["context_crop"], "+repage", str(refs / f"{sheet['prompt']}-sheet-2-context.png")])
    (work / f"sheet-{args.sheet}.layout.json").write_text(json.dumps(layout, indent=2) + "\n", encoding="utf-8")
    print(f"REF {out.name} {scaled[0]}x{scaled[1]} ({len(cells)} parts)")
    return 0


TONE_COLOURED = (12.0, 24.0)   # chroma C* from which a pixel counts as coloured (brass, red glass) and in full from


def class_stats(image: "pngio.Image", weight_of) -> dict:
    """Mean L* and mean chroma of the opaque pixels, split into the coloured class (brass, red) and the neutral one (steel, dark glass): by the weight `weight_of(C*)`."""
    sums = {"c": [0.0, 0.0, 0.0], "n": [0.0, 0.0, 0.0]}   # weight, L* x weight, C* x weight
    d = image.data
    for i in range(0, len(d), 4):
        if d[i + 3] < 200:
            continue
        l, a, b = tone_check._pixel_lab(d[i], d[i + 1], d[i + 2])
        c = (a * a + b * b) ** 0.5
        w = weight_of(c)
        for key, wk in (("c", w), ("n", 1 - w)):
            sums[key][0] += wk
            sums[key][1] += l * wk
            sums[key][2] += c * wk
    return {k: {"w": v[0], "l": v[1] / v[0] if v[0] > 1 else None, "c": v[2] / v[0] if v[0] > 1 else None} for k, v in sums.items()}


def tone_fit(content: Path, reference: Path, out: Path) -> dict:
    """The redraw takes the ORIGINAL detail's tone (SWI-GEN-9, the common rule 4): inside each class (coloured, neutral) the mean L* is shifted to the original's and the chroma of the
    coloured class is scaled to it (hue and texture stay). Returns the before / after numbers."""
    weight = lambda c: min(1.0, max(0.0, (c - TONE_COLOURED[0]) / (TONE_COLOURED[1] - TONE_COLOURED[0])))
    new, ref = pngio.read(content), pngio.read(reference)
    s_new, s_ref = class_stats(new, weight), class_stats(ref, weight)
    shift = {}
    for key in ("c", "n"):
        if s_new[key]["l"] is None or s_ref[key]["l"] is None or s_new[key]["w"] < 40 or s_ref[key]["w"] < 40:
            shift[key] = (0.0, 1.0)
        else:
            k = s_ref[key]["c"] / s_new[key]["c"] if key == "c" and s_new[key]["c"] > 1 else 1.0
            limit = 16.0 if key == "c" else 8.0   # a class's mean is rough where the cut-out carries old backing: a larger shift is not taken
            shift[key] = (max(-limit, min(limit, s_ref[key]["l"] - s_new[key]["l"])), max(0.8, min(1.25, k)))
    d = new.data
    for i in range(0, len(d), 4):
        if d[i + 3] == 0:
            continue
        l, a, b = tone_check._pixel_lab(d[i], d[i + 1], d[i + 2])
        c = (a * a + b * b) ** 0.5
        w = weight(c)
        dl = w * shift["c"][0] + (1 - w) * shift["n"][0]
        k = w * shift["c"][1] + (1 - w) * 1.0
        d[i], d[i + 1], d[i + 2] = tone_check._from_lab(max(0.0, min(100.0, l + dl)), a * k, b * k)
    pngio.write(out, new)
    after = class_stats(new, weight)
    return {"before": s_new, "original": s_ref, "after": after, "shift": {k: [round(v[0], 2), round(v[1], 3)] for k, v in shift.items()}}


def trim_box(image: Path, threshold: str = "10%") -> tuple[int, int, int, int]:
    """x, y, w, h of what the alpha of the image covers (alpha above the threshold)."""
    text = run(["magick", str(image), "-alpha", "extract", "-threshold", threshold, "-bordercolor", "black", "-border", "1", "-format", "%@", "info:"]).strip()
    size, _, rest = text.partition("+")
    w, h = (int(v) for v in size.split("x"))
    x, y = (int(v) for v in rest.split("+"))
    return x - 1, y - 1, w, h


def round_grid(value: float) -> int:
    return int(round(value / GRID)) * GRID


def png_size(path: Path) -> tuple[int, int]:
    w, h = run(["magick", "identify", "-format", "%w %h", str(path)]).split()
    return int(w), int(h)


def squeeze_frame(image: Path, border_from: int, border_to: int) -> None:
    """Nine-slice: the frame's border of `border_from` px is resized to `border_to` px (corners, edges), the inside stays empty, the size stays: a thinner frame on the same opening."""
    w, h = png_size(image)
    f, t = border_from, border_to
    pieces = [(f, f, 0, 0, t, t, 0, 0), (f, f, w - f, 0, t, t, w - t, 0), (f, f, 0, h - f, t, t, 0, h - t), (f, f, w - f, h - f, t, t, w - t, h - t),
              (w - 2 * f, f, f, 0, w - 2 * t, t, t, 0), (w - 2 * f, f, f, h - f, w - 2 * t, t, t, h - t),
              (f, h - 2 * f, 0, f, t, h - 2 * t, 0, t), (f, h - 2 * f, w - f, f, t, h - 2 * t, w - t, t)]
    command = ["magick", "-size", f"{w}x{h}", "xc:none"]
    for cw, ch, sx, sy, tw, th, dx, dy in pieces:
        command += ["(", str(image), "-crop", f"{cw}x{ch}+{sx}+{sy}", "+repage", "-filter", "Lanczos", "-resize", f"{tw}x{th}!", ")", "-geometry", f"+{dx}+{dy}", "-compose", "over", "-composite"]
    run(command + ["-depth", "8", str(image)])


def darken_neutral(image: Path, factor: float) -> None:
    """Grey pixels (a glass the redraw made pale) are multiplied by `factor`; brass and sand (saturated pixels) keep their tone, the change fades in between."""
    mask = image.with_suffix(".neutral.png")
    run(["magick", str(image), "-alpha", "off", "-colorspace", "HSB", "-channel", "G", "-separate", "+channel", "-negate", "-level", "45%,85%", "-depth", "8", str(mask)])
    alpha = image.with_suffix(".alpha.png")
    run(["magick", str(image), "-alpha", "extract", "-depth", "8", str(alpha)])
    run(["magick", str(image), "-alpha", "off", "(", "+clone", "-channel", "RGB", "-evaluate", "Multiply", str(factor), "+channel", ")", "(", str(mask), ")", "-compose", "Over", "-composite", str(alpha),
         "-compose", "CopyOpacity", "-composite", "-depth", "8", str(image)])


def solid_body(image: Path, colour: str) -> None:
    """The part's silhouette (alpha over 8 %, closed) is made opaque: the generated pixels lie over a body of `colour`, so a clouded glass is as solid as the original's."""
    w, h = png_size(image)
    mask = image.with_suffix(".solid.png")
    run(["magick", str(image), "-alpha", "extract", "-threshold", "8%", "-morphology", "Close", "Disk:2", "-depth", "8", str(mask)])
    run(["magick", "-size", f"{w}x{h}", f"xc:{colour}", str(image), "-compose", "over", "-composite", "-alpha", "off", str(mask), "-compose", "CopyOpacity", "-composite", "-depth", "8", str(image)])
    # a dark rim of the silhouette's last pixel: the glass reads as a body of its own against the plate (the original's glass has a dark edge), not as a paler patch of it
    edge = image.with_suffix(".edge.png")
    run(["magick", str(mask), "(", "+clone", "-morphology", "Erode", "Disk:1.5", ")", "-compose", "Minus", "-composite", "-depth", "8", str(edge)])
    run(["magick", str(image), "(", "-size", f"{w}x{h}", "xc:rgb(12,12,14)", str(edge), "-alpha", "off", "-compose", "CopyOpacity", "-composite", ")", "-compose", "over", "-composite",
         str(mask), "-alpha", "off", "-compose", "CopyOpacity", "-composite", "-depth", "8", str(image)])


def blurred_light(image: "pngio.Image", radius: int) -> list[float]:
    """The slow light of a part: the luminance averaged over a box of `radius` px, weighted by alpha (three passes, near a Gaussian), per pixel; 0 where nothing is near."""
    w, h = image.w, image.h
    lum = [(0.3 * image.data[i * 4] + 0.59 * image.data[i * 4 + 1] + 0.11 * image.data[i * 4 + 2]) * image.data[i * 4 + 3] / 255 for i in range(w * h)]
    wgt = [image.data[i * 4 + 3] / 255 for i in range(w * h)]

    def box(values: list[float]) -> list[float]:
        for axis in (0, 1):
            out = [0.0] * (w * h)
            for line in range(h if axis == 0 else w):
                n = w if axis == 0 else h
                get = (lambda k: values[line * w + k]) if axis == 0 else (lambda k: values[k * w + line])
                acc, window = 0.0, [0.0] * (n + 1)
                for k in range(n):
                    acc += get(k)
                    window[k + 1] = acc
                for k in range(n):
                    a, b = max(0, k - radius), min(n, k + radius + 1)
                    value = window[b] - window[a]
                    if axis == 0:
                        out[line * w + k] = value
                    else:
                        out[k * w + line] = value
            values = out
        return values

    for _ in range(3):
        lum, wgt = box(lum), box(wgt)
        scale = max(wgt) or 1.0
        lum = [value / scale for value in lum]
        wgt = [value / scale for value in wgt]
    return [lum[i] / wgt[i] if wgt[i] > 1e-6 else 0.0 for i in range(w * h)]


def relight_mirror(base: Path, mirrored: Path, radius: int = 10, limits: tuple[float, float] = (0.65, 1.5)) -> None:
    """The right-hand copy of a part is the left one mirrored, so its light would fall from the wrong side (the strip is lit from the top left). The form is the mirrored one, the slow light is
    the original's: each pixel is multiplied by (slow light of the left part) / (slow light of its mirror), limited to `limits`; the sharp detail (screws, mesh, edges) stays the mirrored one."""
    left, flopped = pngio.read(base), pngio.read(mirrored)
    want, have = blurred_light(left, radius), blurred_light(flopped, radius)
    out = bytearray(flopped.data)
    for i in range(flopped.w * flopped.h):
        if flopped.data[i * 4 + 3] == 0 or have[i] <= 1.0:
            continue
        gain = min(limits[1], max(limits[0], want[i] / have[i]))
        for c in range(3):
            out[i * 4 + c] = min(255, round(flopped.data[i * 4 + c] * gain))
    pngio.write(mirrored, pngio.Image(flopped.w, flopped.h, out))


def cmd_cut(args) -> int:
    """Cuts the details out of the generated sheets, fits each to the size of the original's and writes the part canvases (a multiple of 6 px) and the placements."""
    root = args.local_root
    work = root / "work" / "B-HARDWARE"
    parts_dir = work / "parts"
    parts_dir.mkdir(parents=True, exist_ok=True)
    placements: dict[str, dict] = {}
    contents: dict[str, tuple[Path, int, int]] = {}
    tones: dict[str, dict] = {}
    for name in args.sheet or sorted(SHEETS):
        sheet = SHEETS[name]
        try:
            layout = json.loads((work / f"sheet-{name}.layout.json").read_text(encoding="utf-8"))
            result, _ = sheet_file(root, sheet)
        except (HardwareError, FileNotFoundError) as err:
            print(f"SKIP sheet {name}: {err}")
            continue
        out_w, out_h = png_size(result)
        ref_w, ref_h = layout["reference_size"]
        if abs((out_w / out_h) / (ref_w / ref_h) - 1) > 0.01:
            raise HardwareError(f"{result.name} is {out_w} x {out_h}: not the reference's aspect {ref_w}:{ref_h} within 1 %")
        scale = out_w / layout["size"][0]   # generated px per master px
        for part_id in sheet["order"]:
            cell, ref = layout["cells"][part_id], work / f"cell-{part_id}.png"
            ex, ey, ew, eh = trim_box(ref)
            ref_w_trim = ew
            slack = CELL_GAP * 0.45
            cx0, cy0 = max(0, round((cell["x"] - slack) * scale)), max(0, round((cell["y"] - slack) * scale))
            cw, ch = min(out_w - cx0, round((cell["w"] + 2 * slack) * scale)), min(out_h - cy0, round((cell["h"] + 2 * slack) * scale))
            crop = parts_dir / f"{part_id}-crop.png"
            run(["magick", str(result), "-crop", f"{cw}x{ch}+{cx0}+{cy0}", "+repage", str(crop)])
            tx, ty, tw, th = trim_box(crop)
            by_height = PARTS[part_id].get("fit") == "height"   # a plate: its thin shadow tail makes the original's width unreliable, so the redraw keeps its own proportion, scaled to the original's height
            drift = (tw / th) / (ew / eh)
            if by_height:
                ew = round(tw * eh / th)
            print(f"CUT {part_id}: generated {tw} x {th} px, original {ew} x {eh}, aspect drift {drift - 1:+.1%}" + (" (fitted by height)" if by_height else ""))
            if not by_height and abs(drift - 1) > 0.08:
                raise HardwareError(f"{part_id}: the redrawn detail's aspect differs by {drift - 1:+.1%} from the original's (limit 8 %): check the sheet")
            content = parts_dir / f"{part_id}-content.png"
            run(["magick", str(crop), "-crop", f"{tw}x{th}+{tx}+{ty}", "+repage", "-filter", "Lanczos", "-resize", f"{ew}x{eh}!", str(content)])
            fitted = parts_dir / f"{part_id}-tone.png"
            refcrop = parts_dir / f"{part_id}-ref.png"
            run(["magick", str(ref), "-crop", f"{ref_w_trim}x{eh}+{ex}+{ey}", "+repage", str(refcrop)])
            report = tone_fit(content, refcrop, fitted)
            print(f"TONE {part_id}: shift coloured {report['shift']['c']} neutral {report['shift']['n']} (L*, chroma x); coloured L* {report['original']['c']['l'] and round(report['original']['c']['l'], 1)} original, "
                  f"{report['before']['c']['l'] and round(report['before']['c']['l'], 1)} redrawn")
            if PARTS[part_id].get("brass_only"):   # a thin brass frame or ring: the grey iron rim the redraw kept of the old backing is cut away by its colour
                run(["magick", str(fitted), "(", "+clone", "-alpha", "off", "-colorspace", "HSB", "-channel", "G", "-separate", "+channel", "-level", "14%,22%", "-morphology", "Close", "Disk:1", ")",
                     "(", "-clone", "0", "-alpha", "extract", ")", "-delete", "0", "-compose", "Multiply", "-composite", str(fitted.with_suffix(".mask.png"))])
                run(["magick", str(fitted), "-alpha", "off", str(fitted.with_suffix(".mask.png")), "-compose", "CopyOpacity", "-composite", str(fitted)])
            run(["magick", str(fitted), "-channel", "A", "-level", "12%,88%", "+channel", str(fitted)])   # the generator's faint haze at the edge: alpha under 12 % is none, over 88 % is full
            if PARTS[part_id].get("gain"):   # the redraw's tone is brought down to the original's (mean luminance, highlights)
                run(["magick", str(fitted), "-channel", "RGB", "-evaluate", "Multiply", str(PARTS[part_id]["gain"]), "+channel", str(fitted)])
            if PARTS[part_id].get("squeeze"):
                squeeze_frame(fitted, *PARTS[part_id]["squeeze"])
            if PARTS[part_id].get("darken_neutral"):
                darken_neutral(fitted, PARTS[part_id]["darken_neutral"])
            if PARTS[part_id].get("solid"):
                solid_body(fitted, PARTS[part_id]["solid"])
            tones[part_id] = report["shift"]
            contents[part_id] = (fitted, ew, eh)
            placements[part_id] = {"content": [ew, eh], "origin": [cell["strip_box"][0] + ex, cell["strip_box"][1] + ey]}
    for part_id, part in PARTS.items():
        if part.get("mirror_of"):
            base = contents.get(part["mirror_of"])
            if base is None:
                continue
            content = parts_dir / f"{part_id}-content.png"
            run(["magick", str(base[0]), "-flop", str(content)])
            relight_mirror(base[0], content)
            contents[part_id] = (content, base[1], base[2])
            base_origin = placements[part["mirror_of"]]["origin"]   # a mirror laid over the strip's middle (no centre given) stands where the strip mirrors its base
            placements[part_id] = {"content": [base[1], base[2]], "origin": [MASTER_W - base_origin[0] - base[1], base_origin[1]] if base_origin else None}
    canvases = {}
    for part_id, (content, ew, eh) in contents.items():
        part = PARTS[part_id]
        w, h = ceil_grid(ew), ceil_grid(eh)
        px, py = (w - ew) // 2, (h - eh) // 2
        canvas = parts_dir / f"{part_id}.png"
        run(["magick", "-size", f"{w}x{h}", "xc:none", str(content), "-geometry", f"+{px}+{py}", "-compose", "over", "-composite", str(canvas)])
        base_origin = placements[part_id]["origin"]
        layers = []
        for strip, cx, cy, _ in part["instances"]:
            if cx is None:
                ox, oy = base_origin
            else:
                ox, oy = round(cx - ew / 2), round(cy - eh / 2)
            layers.append([strip, round_grid(ox - px), round_grid(oy - py)])
        canvases[part_id] = {"size": [w, h], "content": [ew, eh], "pad": [px, py], "layers": layers, "strip": part["strip"], "tone_shift": tones.get(part_id)}
        print(f"PART {part_id}: canvas {w} x {h}, {len(layers)} place(s)")
    placed_file = work / "placements.json"
    kept = json.loads(placed_file.read_text(encoding="utf-8")) if placed_file.exists() else {}   # a cut of one sheet keeps the other sheets' places
    placed_file.write_text(json.dumps({**kept, **canvases}, indent=2) + "\n", encoding="utf-8")
    return 0


# Leftovers of the original's metal that no part stands for. Bottom: the lone square before the rail's first unit. Top: two scraps (1900, 0 and 2026, 96) found in the strip preview after the first erase.
ERASE_BOXES = {"board.intbotom": [(752, 146, 778, 178)], "board.inttop": [(1900, 0, 1944, 26), (2026, 96, 2048, 150)],
               "board.charcreatetopbar": [(1900, 0, 1944, 26), (2026, 96, 2048, 150)]}   # ARC-UI-3260: the menu top strip has the HUD one's end sections, so the same scraps   # leftovers of the original's metal that no part stands for: the lone square before the rail's first unit
ERASE_GROW = 8            # px: the original's metal is erased this far beyond a part's alpha (its fringe of old shadow and wood), the wood beside it fills the gap


def pid(strip: str) -> str:
    return strip.replace(".", "-").replace("_", "-")


def mirror_fill(image: "pngio.Image", erase: "pngio.Image", wood: "pngio.Image") -> int:
    """Fills the erased pixels (erase alpha > 127) with wood: each erased run of a row is mirrored from the wood just beside it (left, else right), so the grain stays
    horizontal and continuous; a pixel nothing reaches takes the mean of the nearest wood left and right. Returns the number of pixels filled."""
    w, h = image.w, image.h
    d, e, wd = image.data, erase.data, wood.data

    def valid(x: int, y: int) -> bool:
        i = (y * w + x) * 4
        return e[i] <= 127 and wd[i] > 127 and d[i + 3] > 0

    filled = 0
    for y in range(h):
        x = 0
        while x < w:
            if e[(y * w + x) * 4] <= 127 or d[(y * w + x) * 4 + 3] == 0:
                x += 1
                continue
            a = x
            while x < w and e[(y * w + x) * 4] > 127:
                x += 1
            b = x   # the run [a, b)
            length = b - a
            left_ok = a - length - 1 >= 0 and all(valid(a - 1 - k, y) for k in range(length))
            right_ok = b + length < w and all(valid(b + k, y) for k in range(length))
            for k in range(length):
                i = (y * w + a + k) * 4
                if left_ok:
                    j = (y * w + a - 1 - k) * 4
                elif right_ok:
                    j = (y * w + b + (length - 1 - k)) * 4
                else:
                    xl = next((a - 1 - t for t in range(80) if a - 1 - t >= 0 and valid(a - 1 - t, y)), None)
                    xr = next((b + t for t in range(80) if b + t < w and valid(b + t, y)), None)
                    near = [(y * w + c) * 4 for c in (xl, xr) if c is not None]
                    if not near:
                        continue
                    for ch in range(3):
                        d[i + ch] = sum(d[n + ch] for n in near) // len(near)
                    filled += 1
                    continue
                d[i:i + 3] = d[j:j + 3]
                filled += 1
    return filled


def footprint_mask(part_id: str, part: dict, root: Path, work: Path) -> tuple[Path, tuple[int, int, int, int]]:
    """ARC-UI-3260: the mask (white = inside) and box of what the ORIGINAL's detail covers where the part itself carries no shapes (a mirrored plate): its `footprint`."""
    folder, original, _ = STRIPS[part["strip"]]
    out = work / f"footprint-{part_id}.png"
    box = cell_image({"strip": part["strip"], "shapes": part["footprint"]}, root / "work" / "B-BOARD" / folder / original, out, root)
    return out.with_suffix(".mask.png"), box


def hole_of(alpha: Path, out: Path) -> None:
    """The inside of a ring or a frame (what its alpha encloses and does not cover) as a white-on-black mask the size of the part."""
    run(["magick", str(alpha), "-alpha", "extract", "-threshold", "50%", "-bordercolor", "black", "-border", "1", "-fill", "red", "-draw", "color 0,0 floodfill", "-shave", "1x1",
         "-channel", "R", "-separate", "+channel", "-negate", "-depth", "8", str(out)])


def cmd_strips(args) -> int:
    """The strip art without the original's metal (its erased places filled with the wood beside them) and a preview of the strip with its layers laid at their places."""
    root = args.local_root
    work = root / "work" / "B-HARDWARE"
    placements = json.loads((work / "placements.json").read_text(encoding="utf-8"))
    out_dir = work / "strips"
    out_dir.mkdir(exist_ok=True)
    for strip, (folder, _, height) in STRIPS.items():
        base = root / "work" / "B-BOARD" / folder / f"master-{pid(strip)}.png"
        if not base.is_file():
            raise HardwareError(f"no {base} (run board_build.py strips first)")
        here = {part: {**info, "layers": [layer for layer in info["layers"] if layer[0] == strip]} for part, info in placements.items()}
        here = {part: info for part, info in here.items() if info["layers"]}
        if not here:
            continue
        erase = out_dir / f"{pid(strip)}-erase.png"
        command = ["magick", "-size", f"{MASTER_W}x{height}", "xc:black"]
        for part, info in here.items():
            grow = PARTS[part].get("erase_grow", ERASE_GROW)
            for _, x, y in info["layers"]:
                command += ["(", str(work / "parts" / f"{part}.png"), "-alpha", "extract", "-bordercolor", "black", "-border", str(grow), "-morphology", "Dilate", f"Disk:{grow}"]
                if PARTS[part].get("keep_hole"):   # a ring or a frame: its inside (the button's socket, the window's field) is not the original's metal and stays the strip's
                    hole = work / "strips" / f"{part}-hole.png"
                    hole_of(work / "parts" / f"{part}.png", hole)
                    command += ["(", str(hole), "-bordercolor", "black", "-border", str(grow), ")", "-compose", "MinusSrc", "-composite"]   # ImageMagick's MinusSrc is destination minus source
                command += [")", "-geometry", f"+{x - grow}+{y - grow}", "-compose", "Lighten", "-composite"]
        layouts = {name: json.loads((work / f"sheet-{name}.layout.json").read_text(encoding="utf-8")) for name in SHEETS if (work / f"sheet-{name}.layout.json").is_file()}
        for part, info in here.items():   # the footprint of the original's plate (what the strip's wood mask leaves), so no scrap of the old iron or its shadow stands where the new plate is smaller
            if any(sh[0] == "nonwood" for sh in PARTS[part]["shapes"]):
                lay = layouts[PARTS[part]["sheet"]]["cells"][part]["strip_box"]
                command += ["(", str(work / f"cell-{part}.mask.png"), "-bordercolor", "black", "-border", "4", "-morphology", "Dilate", "Disk:4", ")", "-geometry", f"+{lay[0] - 4}+{lay[1] - 4}",
                            "-compose", "Lighten", "-composite"]
        for part, info in here.items():   # a mirrored plate has no sheet cell: the footprint of the original's section is its own shapes
            if PARTS[part].get("footprint"):
                mask, box = footprint_mask(part, PARTS[part], root, work)
                command += ["(", str(mask), "-bordercolor", "black", "-border", "4", "-morphology", "Dilate", "Disk:4", ")", "-geometry", f"+{box[0] - 4}+{box[1] - 4}", "-compose", "Lighten", "-composite"]
        for x0, y0, x1, y1 in ERASE_BOXES.get(strip, []):
            command += ["-fill", "white", "-draw", f"rectangle {x0},{y0} {x1 - 1},{y1 - 1}"]
        run(command + [str(erase)])
        row = 0 if height == 150 else 1590
        wood = out_dir / f"{pid(strip)}-wood.png"
        run(["magick", str(root / "work" / "B-BOARD" / folder / "w" / "wood-solid.png"), "-crop", f"{MASTER_W}x{height}+0+{row}", "+repage", "-alpha", "off", "-colorspace", "gray", "-depth", "8", str(wood)])
        image, mask, wood_img = pngio.read(base), pngio.read(erase), pngio.read(wood)
        filled = mirror_fill(image, mask, wood_img)
        art = out_dir / f"{pid(strip)}-art.png"
        pngio.write(art, image)
        preview = out_dir / f"{pid(strip)}-preview.png"
        command = ["magick", str(art)]
        for part in (p for p in layers_of(strip) if p in here):   # the draw order of the runtime table, so a rivet stands over its plate
            for _, x, y in here[part]["layers"]:
                command += [str(work / "parts" / f"{part}.png"), "-geometry", f"+{x}+{y}", "-compose", "over", "-composite"]
        run(command + [str(preview)])
        print(f"STRIP {strip}: {filled} erased pixels filled with wood; {art.name}, {preview.name}")
    return 0


def sha256_file(path: Path) -> str:
    return prompt_library.sha256_file(path)


def sheet_file(root: Path, sheet: dict) -> tuple[Path, dict]:
    """The generated sheet and its record. Codex sometimes remarks that the aspect it drew "differs slightly" and the runner then records `refused` without copying the image to the outbox
    (#3187, the same as for the strips and the caps of B-BOARD): the canvas is in the work folder of the run the record names; its aspect is checked against the reference's by `cut`."""
    batch = sheet.get("batch", "B-HARDWARE-HUD")
    result = root / "outbox" / batch / f"{sheet['prompt']}.png"
    record = json.loads(result.with_suffix(".provenance.json").read_text(encoding="utf-8"))
    if result.is_file():
        return result, record
    run_name = record.get("run")
    canvas = root / "codex-work" / batch / str(run_name) / f"{sheet['prompt']}.png"
    if not run_name or not canvas.is_file():
        raise HardwareError(f"no result for {sheet['prompt']}: neither {result} nor the run's canvas {canvas} (run the batch first)")
    return canvas, record


def derived_record(root: Path, part_id: str, sheet: dict, canvas: Path) -> Path:
    """The generator record of a part's source: the Codex record of its sheet plus what this tool did to it (`image_sha256` is what the fit tool checks)."""
    result, gen = sheet_file(root, sheet)
    sheet_hash = sha256_file(result)
    if gen.get("image_sha256") not in (None, sheet_hash):
        raise HardwareError(f"{result.name} is not the image its generator record describes")
    gen["runner_outcome"] = gen.get("outcome")
    gen["generator_image_sha256"] = sheet_hash
    gen["image_sha256"] = sha256_file(canvas)
    gen["derived"] = {"tool": "hardware_build.py cut: the detail trimmed from the generated sheet, scaled to the original detail's size, laid on a canvas of a multiple of 6 px"
                      + ("; mirrored" if PARTS[part_id].get("mirror_of") else ""), "sheet_file": result.name, "sheet_sha256": sheet_hash, "part": part_id}
    out = canvas.with_suffix(".provenance.json")
    out.write_text(json.dumps(gen, indent=2) + "\n", encoding="utf-8")
    return out


def cmd_fit(args) -> int:
    """Fits each part canvas to a kit master (fit.py: alpha clean-up, colour lock to the approved material sheet, gates)."""
    root = args.local_root
    work = root / "work" / "B-HARDWARE"
    placements = json.loads((work / "placements.json").read_text(encoding="utf-8"))
    codes = []
    for part_id, info in placements.items():
        if args.only and part_id not in args.only:
            continue
        sheet = SHEETS[PARTS[PARTS[part_id].get("mirror_of", part_id)]["sheet"]]
        canvas = work / "parts" / f"{part_id}.png"
        record = derived_record(root, part_id, sheet, canvas)
        command = [sys.executable, "-B", str(HERE / "fit.py"), "fit", part_id, "--source", str(canvas), "--size", f"{info['size'][0]}x{info['size'][1]}", "--key", "none", "--replace",
                   "--provenance", str(record)]
        if args.material:   # the colour lock to the approved sheet is off by default: the tone is fitted to the ORIGINAL detail by `cut` (tone_fit)
            command += ["--material", args.material, "--lock-strength", str(args.lock_strength)]
        if args.force_lock:
            command.append("--force-lock")
        if args.dry_run:
            command.append("--dry-run")
        proc = subprocess.run(command, capture_output=True, text=True)
        text = (proc.stdout.strip() or proc.stderr.strip())
        print(f"FIT {part_id}: exit {proc.returncode} {text.splitlines()[-1] if text else ''}")
        if proc.returncode != 0:
            print(text)
        codes.append(proc.returncode)
    return 0 if all(c == 0 for c in codes) else 1


def cmd_art(args) -> int:
    """Fits the two strip arts (the wood and the original's openings, no metal) to the kit masters `board.inttop` and `board.intbotom`."""
    root = args.local_root
    work = root / "work" / "B-HARDWARE"
    codes = []
    for strip, (folder, _, height) in STRIPS.items():
        art = work / "strips" / f"{pid(strip)}-art.png"
        base_record = root / "work" / "B-BOARD" / folder / f"master-{pid(strip)}.provenance.json"
        gen = json.loads(base_record.read_text(encoding="utf-8"))
        gen["board_image_sha256"] = gen.get("image_sha256")
        gen["image_sha256"] = sha256_file(art)
        gen["derived_hardware"] = {"tool": "hardware_build.py strips: the strip art of B-BOARD with the places of its original metal filled with the wood beside them (the metal is the hardware layers)",
                                   "strip_art_sha256": gen["board_image_sha256"], "hardware_parts": sorted(layers_of(strip))}
        record = art.with_suffix(".provenance.json")
        record.write_text(json.dumps(gen, indent=2) + "\n", encoding="utf-8")
        command = [sys.executable, "-B", str(HERE / "fit.py"), "fit", strip, "--source", str(art), "--size", f"{MASTER_W}x{height}", "--key", "none", "--replace", "--provenance", str(record)]
        if args.dry_run:
            command.append("--dry-run")
        proc = subprocess.run(command, capture_output=True, text=True)
        text = proc.stdout.strip() or proc.stderr.strip()
        print(f"FIT {strip}: exit {proc.returncode} {text.splitlines()[-1] if text else ''}")
        codes.append(proc.returncode)
    return 0 if all(c == 0 for c in codes) else 1


def self_test() -> int:
    for part_id, part in PARTS.items():
        if part.get("mirror_of"):
            assert part["mirror_of"] in PARTS and part["sheet"] is None, part_id
            continue
        x0, y0, x1, y1 = bbox_of(part["shapes"])
        assert x1 > x0 and y1 > y0, part_id
        assert part["sheet"] in SHEETS and part_id in SHEETS[part["sheet"]]["order"], part_id
    for sheet in SHEETS.values():
        assert all(p in PARTS for p in sheet["order"])
    print("hardware_build self-test: OK")
    return 0


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = ap.add_subparsers(dest="cmd", required=True)
    r = sub.add_parser("refs")
    r.add_argument("--sheet", default="hud-bottom", choices=sorted(SHEETS))
    r.add_argument("--local-root", type=Path, default=None)
    t = sub.add_parser("strips")
    t.add_argument("--local-root", type=Path, default=None)
    f = sub.add_parser("fit")
    f.add_argument("--local-root", type=Path, default=None)
    f.add_argument("--only", action="append")
    f.add_argument("--material", default="")
    f.add_argument("--lock-strength", type=float, default=0.5)
    f.add_argument("--force-lock", action="store_true")
    f.add_argument("--dry-run", action="store_true")
    ar = sub.add_parser("art")
    ar.add_argument("--local-root", type=Path, default=None)
    ar.add_argument("--dry-run", action="store_true")
    c = sub.add_parser("cut")
    c.add_argument("--sheet", action="append", choices=sorted(SHEETS))
    c.add_argument("--local-root", type=Path, default=None)
    sub.add_parser("self-test")
    args = ap.parse_args(argv)
    if args.cmd == "self-test":
        return self_test()
    args.local_root = args.local_root or local_root()
    try:
        return {"refs": cmd_refs, "cut": cmd_cut, "strips": cmd_strips, "fit": cmd_fit, "art": cmd_art}[args.cmd](args)
    except HardwareError as err:
        print(f"ERROR {err}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
