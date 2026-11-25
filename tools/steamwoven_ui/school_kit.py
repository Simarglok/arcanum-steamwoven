#!/usr/bin/env python3
"""ARC-UI-3204 (batch B-BUTTONS, slice 4): the college tabs of the spell window (16 arts, 24 x 24 units) and the skill-school buttons (12 arts, 31 to 34 x 31 to 32 units), round buttons with a COLOURED dome inside a thin brass ring,
three frames each (idle 0, pressed 1, hover 2). Same rule as the earlier round buttons (`round_kit.py`, `disc_kit.py`, rule E of slice 1): a generated plate, and the glyph and the state overlays BY RULE from the original's own frames.

    python3 -B tools/steamwoven_ui/school_kit.py masks  --out FILE [--group college|school] [--zoom N]          (the symbol masks over the idle frames, for a look; needs ImageMagick)
    python3 -B tools/steamwoven_ui/school_kit.py refs   [--group college|school] [--frames DIR] [--local-root DIR]  (the references of the two collage prompts: the original domes without their symbols)
    python3 -B tools/steamwoven_ui/school_kit.py place  [--group ...] --source FILE needs --group [--out DIR]       (the generated collage cut into one dome per button, on the master's canvas)
    python3 -B tools/steamwoven_ui/school_kit.py fit    [--group ...] [--only NAME,...] [--fitted-dir DIR]                (fit.py fit for every placed dome -> work/B-BUTTONS/fitted)
    python3 -B tools/steamwoven_ui/school_kit.py gloss  [--group ...] [--fitted-dir DIR] [--out DIR] [--dry-run]  (the original's highlight over the fitted domes -> masters/)
    python3 -B tools/steamwoven_ui/school_kit.py build  [--group ...] [--only NAME,...] [--out DIR] [--dry-run]    (glyphs and overlays -> masters/ and provenance/rule/)
    python3 -B tools/steamwoven_ui/school_kit.py sheet  --out FILE [--group ...] [--only NAME,...] [--zoom N]     (the before / after composite, English labels)
    python3 -B tools/steamwoven_ui/school_kit.py self-test

The parts of a button (TRD SWI-KIT-5; the runtime's convention `SteamwovenKitComposer.ButtonLayers`: the face, over it the glyph, over both the overlay `<face>_hover` or `<face>_pressed`; all parts of a button are at the face's size):

* `plate.<name>` the empty plate: the coloured dome in its brass ring, WITHOUT the symbol and without the highlight, ONE Codex item per group (`plate-college-hud`, `plate-school-hud`: the sixteen or the twelve domes of the group on one canvas, each in
  the colour of its original so the ring, the bevel and the surface agree), cut apart (`place`) and fitted by `fit.py` to the face of every button; the plate's outline is the original's frame mask.
* `glyph.<name>` the symbol as the idle frame shows it over the plate, a layer on the whole frame: the cells of the symbol mask (below) in the original's own flat cell colours, with a smooth sharp outline.
* `plate.<name>_hover` what the hover frame shows over the IDLE frame: the symbol's cells in the lit colours, and the dome's own lighting as a soft layer under it (the original brightens the whole dome: a layer of the lit colour at a limited
  opacity, so the plate's own texture shows through it, never a replacement of the plate by the original's low-resolution cells).
* `plate.<name>_pressed` the same for the pressed frame (the dome and the ring brighten more).

The symbol mask is read from the PRESSED frame (the dome is brightest there and the near-black symbol stands out most; `read: idle` in an override reads the idle frame, where the `MAX_GAIN` filter then drops cells that brighten in the pressed frame): a cell in the dome's reach whose lightness is under half of what its neighbourhood shows (the high percentile of a window), small and large
(two window sizes), the pieces that lie wholly in the rim's shade dropped, the cells inside it that stand out in colour from the dome added (an accent such as the yellow gear core), small holes closed. `OVERRIDES` holds a per-button correction
(a threshold, cells, simple shapes straightened or a region with its own darkness limit) where the rule needs one; every mask is looked at (`masks`). The symbol's coloured body (the fire's orange) joins the dark outline. `gloss` also draws the brass ring by rule (`paint_ring`, the width of the original, the tone of the hardware menu ring).
Nothing here holds art: every picture is read from and written to the ignored `local/steamwoven-ui/`. Standard library only (`pngio.py`); ImageMagick for `masks`, `place` and `sheet`.
Exit 0 = done, 1 = a check failed, 2 = an input could not be used.
"""
from __future__ import annotations

import argparse
import datetime
import hashlib
import json
import math
import shutil
import subprocess
import sys
import tempfile
from dataclasses import dataclass
from pathlib import Path

HERE = Path(__file__).resolve().parent
try:
    from . import button_kit, pngio, prompt_library, round_kit
except ImportError:  # run as a script
    sys.path.insert(0, str(HERE))
    import button_kit  # type: ignore
    import pngio  # type: ignore
    import prompt_library  # type: ignore
    import round_kit  # type: ignore

TOOL_VERSION = 1
MANIFEST = HERE / "kit.json"
DOME_SHARE = 0.89           # the dome's radius as a share of the frame's (the brass ring and its dark outline lie outside it)
REACH_SHARE = 0.82          # a symbol lies within this share of the dome's radius
WINDOWS = ((0.28, 0.50), (0.55, 0.42))   # (window radius as a share of the dome's radius, lightness ratio): the symbol mask's two readings (thin lines, then blobs)
PERCENTILE = 0.85           # the dome's lightness around a cell: this percentile of the window
MIN_DROP = 8.0              # CIE L a cell must lie below the dome's lightness to be symbol
MAX_L = 9.0                 # CIE L: a symbol cell is as dark as this at most (the original draws its symbols near-black; the dome's own shade at the rim is lighter, with a few exceptions in `OVERRIDES`)
MAX_L_READ = 45.0           # CIE L: the same ceiling when the symbols are read from the pressed frame (the near-black symbol is a little lighter there)
MAX_GAIN = 30.0             # CIE L: a symbol cell gains this much lightness at most from the idle to the pressed frame (the original lights the dome, the symbol stays dark; the dome's shade at the rim gains 35 to 50)
GROW_PASSES = 0             # hysteresis passes (the cells next to the mask that are dark enough, though not as dark as its core); 0 = off, a button turns it on in `OVERRIDES` (`grow`)
GROW_RATIO = 0.58           # hysteresis: a cell next to the mask joins it when it is under this share of the dome's lightness around it ...
GROW_MAX_L = 15.0           # ... and no lighter than this, ...
GROW_GAIN = 34.0            # ... and gains no more than this from the idle to the pressed frame; one pass
DEFAULT_MEDIAN = (0.45, 6.0)  # the median reading every button without a `median` entry gets (window share, drop in CIE L)
MEDIAN_PERCENTILE = 0.5     # the median reading (`median` in `OVERRIDES`: [window share, drop in CIE L]): a cell this much darker than the median of the window's cells is symbol (for symbols only a little darker than a dome that itself darkens towards the rim)
RIM_DROP = 0.72             # the dome's shade at the lower right (beyond this share of the dome's radius, past the diagonal) is no symbol: such cells are dropped (`rim_drop` in `OVERRIDES` moves the limit)
RIM_SHARE = 0.74            # a piece of the mask whose nearest cell to the middle lies beyond this share of the dome's radius is the rim's shade, not a symbol
SPECK = 4                   # a piece of this many cells or fewer is a speck, dropped
FILL_DE = 24.0              # the symbol's coloured body: a cell within FILL_NEAR cells of the dark outline that is this far in hue (CIE a, b) from the median of the plain dome around it (in the pressed frame) joins the mask; `fill` in `OVERRIDES` changes it, 0 = off
FILL_NEAR = 1               # cells
FILL_WINDOW = 4             # the window of that median, in cells
ACCENT_DE = 34.0            # a cell this far (CIE76) from the median of the plain dome cells around it is an accent of the symbol (the gear's yellow core), joined to the mask
ACCENT_REACH = 0.50         # an accent lies this share of the dome's radius from the middle at most (the highlight on the upper left is no accent)
ACCENT_WINDOW = 3           # the window of the median, in cells
HOLE_MAX = 3                # a hole of the mask of this many cells or fewer is closed
GROUND = (128, 128, 128)    # the flat ground of a collage
COLLAGE_SCALE = {"college": 9.5, "school": 9.0}   # px per original unit of the cells of a collage
COLLAGE_PITCH = {"college": (255, 255), "school": (382, 340)}
COLLAGE_GRID = {"college": (4, 4), "school": (4, 3)}
COLLAGE_CANVAS = {"college": (1020, 1020), "school": (1530, 1020)}   # the sizes of the two prompts (the cells fill the top left of the canvas)
LABEL_FONT = button_kit.LABEL_FONT
Image = pngio.Image


class SchoolError(Exception):
    pass


@dataclass(frozen=True)
class Tab:
    art: int
    folder: str
    name: str          # `plate.<name>`, `glyph.<name>`
    group: str         # `college` or `school`


def _college(art: int, name: str) -> Tab:
    return Tab(art, f"{art}-{name}", name, "college")


def _school(art: int, folder: str) -> Tab:
    return Tab(art, f"{art}-{folder}", folder.lower(), "school")


TABS = (
    _college(11, "s_conveyance"), _college(38, "s_air"), _college(44, "s_divination"), _college(50, "s_fire"), _college(56, "s_forc"), _college(62, "s_meta"), _college(68, "s_morph"), _college(74, "s_nature"),
    _college(80, "s_phantasm"), _college(86, "s_water"), _college(92, "s_earth"), _college(98, "s_mental"), _college(104, "s_evilnecro"), _college(110, "s_goodnecro"), _college(116, "s_summoning"), _college(122, "s_temporal"),
    _school(302, "Combat_But"), _school(303, "Thieving_But"), _school(304, "Social_But"), _school(305, "Technological_But"), _school(306, "Anatomical_But"), _school(307, "Chemistry_But"),
    _school(308, "Electrical_But"), _school(309, "Explosives_But"), _school(310, "GunSmithy_But"), _school(311, "Mechanical_But"), _school(312, "Smithy_But"), _school(313, "Therapeutics_But"),
)
GROUPS = {"college": "plate-college-hud", "school": "plate-school-hud"}   # the prompt of each group
OVERRIDES: dict[str, dict] = {
    "electrical_but": {"reach": 0.72},
    "s_nature": {"fill": 0},   # dark strokes only: the pale cells between them are the dome's light, not a body
    "social_but": {"shapes_only": True, "shapes": (("rect", 14.0, 7.0, 22.0, 17.0), ("ellipse", 17.0, 20.5, 7.4, 3.1))},      # the top hat: crown and brim
    "technological_but": {"shapes_only": True, "shapes": (("gear", 16.0, 16.0, 5.4, 7.0, 8, 0.069),), "shapes_drop": (("ellipse", 16.0, 15.5, 1.8, 1.8),)},   # the gear: eight teeth, a round hole
    "anatomical_but": {"shapes_only": True, "shapes": (("poly", [(10.0, 11.5), (22.0, 11.5), (21.2, 19.5), (22.0, 21.3), (22.0, 24.0), (10.0, 24.0), (10.0, 21.3), (10.8, 19.5)]), ("line", 19.6, 10.4, 23.6, 6.6, 2.0))},   # the mortar's bowl and foot, the pestle
    "mechanical_but": {"reach": 0.78, "rim_share": 1.0, "shapes_only": True,
                       "shapes": (("gear", 13.3, 10.9, 3.4, 4.3, 8, 0.0), ("gear", 19.5, 18.5, 4.3, 5.2, 8, 0.06)),
                       "shapes_drop": (("ellipse", 13.0, 11.5, 1.2, 1.0), ("ellipse", 19.0, 18.0, 1.2, 1.2))},   # two gears with round holes
    "gunsmithy_but": {"reach": 0.76, "rim_share": 1.0, "close": 1,
                      "roi": ([(7.0, 9.0), (26.5, 6.5), (26.5, 11.5), (19.0, 14.5), (18.0, 24.0), (7.0, 24.0)], 33.0)},   # the revolver: dark only against the dome's own shade at the lower right, so the cells of its region
    "therapeutics_but": {"reach": 0.76, "rim_share": 1.0, "shapes_only": True,
                         "shapes": (("poly", [(15.0, 10.5), (12.0, 8.4), (8.0, 8.8), (7.0, 11.0), (10.0, 12.2), (15.0, 12.6)]), ("poly", [(16.0, 10.5), (19.0, 8.4), (23.0, 8.8), (24.0, 11.0), (21.0, 12.2), (16.0, 12.6)]),
                                    ("line", 15.5, 8.6, 15.5, 23.0, 1.7), ("line", 13.4, 14.0, 17.6, 15.7, 1.2), ("line", 17.6, 15.7, 13.4, 17.5, 1.2), ("line", 13.4, 17.5, 17.6, 19.3, 1.2))},   # the caduceus: wings, staff, the snakes' coils
}   # per button: {"windows": ((share, ratio), ...), "median": (share, drop), "read": "idle", "max_l", "max_gain", "reach", "rim_share", "rim_drop", "grow", "accent", "fill", "close", "add": ((x, y), ...), "drop": ((x, y), ...), "shapes" / "shapes_drop": simple shapes (`shape_cells`)}; read by eye from `masks`


def tabs_of(group: str | None) -> list[Tab]:
    if group is not None and group not in GROUPS:
        raise SchoolError(f"no group {group!r}; the groups are {list(GROUPS)}")
    return [t for t in TABS if group is None or t.group == group]


def tab_named(name: str) -> Tab:
    for t in TABS:
        if t.name == name:
            return t
    raise SchoolError(f"{name} is no button of this slice")


# --- frames and geometry ----------------------------------------------------------------------------------------------------

def read_frame(frames: Path, tab: Tab, number: int) -> Image:
    path = frames / tab.folder / f"f{number}.png"
    if not path.is_file():
        raise SchoolError(f"no frame {path}")
    return pngio.read(path)


def alpha_at(image: Image, x: int, y: int) -> int:
    return image.data[(y * image.w + x) * 4 + 3]


def colour_at(image: Image, x: int, y: int) -> tuple[int, int, int]:
    o = (y * image.w + x) * 4
    return image.data[o], image.data[o + 1], image.data[o + 2]


def disc_of(frame: Image) -> tuple[float, float, float]:
    """The frame's disc: the middle of its opaque cells (x, y, a cell's middle at +0.5) and the radius of the circle of the same area."""
    cells = [(x, y) for y in range(frame.h) for x in range(frame.w) if alpha_at(frame, x, y)]
    if not cells:
        raise SchoolError("the frame is empty")
    return sum(x + 0.5 for x, _ in cells) / len(cells), sum(y + 0.5 for _, y in cells) / len(cells), math.sqrt(len(cells) / math.pi)


def dome_radius(disc: tuple[float, float, float]) -> float:
    return disc[2] * DOME_SHARE


def neighbours8(i: int, w: int, h: int) -> list[int]:
    x, y = i % w, i // w
    return [yy * w + xx for yy in range(max(0, y - 1), min(h, y + 2)) for xx in range(max(0, x - 1), min(w, x + 2)) if (xx, yy) != (x, y)]


def components(mask: list[bool], w: int, h: int) -> list[list[int]]:
    seen = [False] * len(mask)
    out = []
    for start, on in enumerate(mask):
        if not on or seen[start]:
            continue
        stack, piece = [start], []
        seen[start] = True
        while stack:
            i = stack.pop()
            piece.append(i)
            for j in neighbours8(i, w, h):
                if mask[j] and not seen[j]:
                    seen[j] = True
                    stack.append(j)
        out.append(piece)
    return out


# --- the symbol mask --------------------------------------------------------------------------------------------------------

def lightness_plane(frame: Image) -> list[float]:
    return [button_kit.lab(*colour_at(frame, i % frame.w, i // frame.w))[0] if alpha_at(frame, i % frame.w, i // frame.w) else 0.0 for i in range(frame.w * frame.h)]


def dome_light(frame: Image, disc: tuple[float, float, float], light: list[float], window: float, reach: float) -> list[float | None]:
    """Per cell in the reach the dome's lightness around it: the `PERCENTILE` of the lightness of the window's cells inside the dome (None outside the reach)."""
    w, h = frame.w, frame.h
    cx, cy, _ = disc
    rd = dome_radius(disc)
    win = max(1, int(round(window * rd)))
    out: list[float | None] = [None] * (w * h)
    for y in range(h):
        for x in range(w):
            if not alpha_at(frame, x, y) or math.hypot(x + 0.5 - cx, y + 0.5 - cy) > reach * rd:
                continue
            around = sorted(light[yy * w + xx] for yy in range(y - win, y + win + 1) for xx in range(x - win, x + win + 1)
                            if 0 <= xx < w and 0 <= yy < h and alpha_at(frame, xx, yy) and (xx - x) ** 2 + (yy - y) ** 2 <= win * win + 1 and math.hypot(xx + 0.5 - cx, yy + 0.5 - cy) <= 0.98 * rd)
            if around:
                out[y * w + x] = around[min(len(around) - 1, int(len(around) * PERCENTILE))]
    return out


def dark_cells(frame: Image, disc: tuple[float, float, float], light: list[float], window: float, ratio: float, reach: float, max_l: float = MAX_L) -> list[bool]:
    """The cells in the reach whose lightness is under `ratio` of the dome's around them (the `PERCENTILE` of the window cells inside the dome) and at least `MIN_DROP` below it."""
    w, h = frame.w, frame.h
    cx, cy, _ = disc
    rd = dome_radius(disc)
    win = max(1, int(round(window * rd)))
    out = [False] * (w * h)
    for y in range(h):
        for x in range(w):
            if not alpha_at(frame, x, y) or math.hypot(x + 0.5 - cx, y + 0.5 - cy) > reach * rd:
                continue
            around = sorted(light[yy * w + xx] for yy in range(y - win, y + win + 1) for xx in range(x - win, x + win + 1)
                            if 0 <= xx < w and 0 <= yy < h and alpha_at(frame, xx, yy) and (xx - x) ** 2 + (yy - y) ** 2 <= win * win + 1 and math.hypot(xx + 0.5 - cx, yy + 0.5 - cy) <= 0.98 * rd)
            if not around:
                continue
            top = around[min(len(around) - 1, int(len(around) * PERCENTILE))]
            here = light[y * w + x]
            if here <= ratio * top and top - here >= MIN_DROP and here <= max_l:
                out[y * w + x] = True
    return out


def symbol_mask(frame: Image, pressed: Image | None = None, override: dict | None = None) -> list[bool]:
    """The symbol's cells of an idle frame (see the module text); `pressed` (the pressed frame) tells the symbol from the dome's own shade by how much lighter the cell becomes."""
    override = override or {}
    w, h = frame.w, frame.h
    disc = disc_of(frame)
    cx, cy, _ = disc
    rd = dome_radius(disc)
    reach = override.get("reach", REACH_SHARE)
    light = lightness_plane(frame)
    lit = lightness_plane(pressed) if pressed is not None else None
    reading = lit if (lit is not None and override.get("read", "pressed") == "pressed") else light   # the symbols are read from the pressed frame: the dome is brightest there and the near-black symbol stands out most
    mask = [False] * (w * h)
    for window, ratio in override.get("windows", WINDOWS):
        for i, on in enumerate(dark_cells(frame, disc, reading, window, ratio, reach, override.get("max_l", MAX_L_READ if reading is lit else MAX_L))):
            mask[i] = mask[i] or on
    median = override.get("median", DEFAULT_MEDIAN)
    if median is not None:
        share, drop = median
        win = max(1, int(round(share * rd)))
        for y in range(h):
            for x in range(w):
                if not alpha_at(frame, x, y) or math.hypot(x + 0.5 - cx, y + 0.5 - cy) > reach * rd:
                    continue
                around = sorted(reading[yy * w + xx] for yy in range(y - win, y + win + 1) for xx in range(x - win, x + win + 1)
                                if 0 <= xx < w and 0 <= yy < h and alpha_at(frame, xx, yy) and math.hypot(xx + 0.5 - cx, yy + 0.5 - cy) <= 0.98 * rd)
                if around and reading[y * w + x] <= around[int(len(around) * MEDIAN_PERCENTILE)] - drop:
                    mask[y * w + x] = True
    if lit is not None and reading is light:
        mask = [on and lit[i] - light[i] <= override.get("max_gain", MAX_GAIN) for i, on in enumerate(mask)]
    tops = dome_light(frame, disc, light, WINDOWS[0][0], reach)
    for _ in range(override.get("grow", GROW_PASSES)):  # hysteresis: the cells next to the mask that are dark enough, though not as dark as its core
        grown = list(mask)
        for i, top in enumerate(tops):
            if mask[i] or top is None or light[i] > GROW_MAX_L or light[i] > GROW_RATIO * top or (lit is not None and lit[i] - light[i] > GROW_GAIN):
                continue
            if any(mask[j] for j in neighbours8(i, w, h)):
                grown[i] = True
        mask = grown
    fill_de = override.get("fill", FILL_DE)
    if fill_de:  # the symbol's coloured body: cells next to its dark outline that stand out in colour from the plain dome around them (the fire's orange, the water's teal), read from the pressed frame
        shown = pressed if pressed is not None else frame
        near_mask = [False] * (w * h)
        for i, on in enumerate(mask):
            if on:
                for y in range(i // w - FILL_NEAR, i // w + FILL_NEAR + 1):
                    for x in range(i % w - FILL_NEAR, i % w + FILL_NEAR + 1):
                        if 0 <= x < w and 0 <= y < h:
                            near_mask[y * w + x] = True
        dome = [(not near_mask[i]) and alpha_at(shown, i % w, i // w) > 0 and math.hypot(i % w + 0.5 - cx, i // w + 0.5 - cy) <= 0.85 * rd for i in range(w * h)]
        extra = []
        for i in range(w * h):
            x, y = i % w, i // w
            if mask[i] or not near_mask[i] or alpha_at(shown, x, y) == 0 or math.hypot(x + 0.5 - cx, y + 0.5 - cy) > reach * rd:
                continue
            around = [button_kit.lab(*colour_at(shown, xx, yy)) for yy in range(y - FILL_WINDOW, y + FILL_WINDOW + 1) for xx in range(x - FILL_WINDOW, x + FILL_WINDOW + 1)
                      if 0 <= xx < w and 0 <= yy < h and dome[yy * w + xx]]
            if len(around) < 8:
                continue
            median = tuple(sorted(c[k] for c in around)[len(around) // 2] for k in range(3))
            if math.dist(button_kit.lab(*colour_at(shown, x, y))[1:], median[1:]) > fill_de:
                extra.append(i)
        for i in extra:
            mask[i] = True
    limit = override.get("rim_drop", RIM_DROP) * rd
    for i, on in enumerate(mask):
        dx, dy = i % w + 0.5 - cx, i // w + 0.5 - cy
        if on and (dx + dy > 0.15 * rd or dy > 0.35 * rd) and math.hypot(dx, dy) > limit:
            mask[i] = False
    for piece in components(mask, w, h):  # the rim's shade and specks
        nearest = min(math.hypot(i % w + 0.5 - cx, i // w + 0.5 - cy) for i in piece)
        if len(piece) <= SPECK or nearest > override.get("rim_share", RIM_SHARE) * rd:
            for i in piece:
                mask[i] = False
    plain = [(not mask[i]) and alpha_at(frame, i % w, i // w) > 0 and math.hypot(i % w + 0.5 - cx, i // w + 0.5 - cy) <= reach * rd for i in range(w * h)]
    for y in range(h):  # an accent of the symbol stands out in colour from the plain dome cells around it
        for x in range(w):
            i = y * w + x
            if not override.get("accent", False) or mask[i] or not plain[i] or math.hypot(x + 0.5 - cx, y + 0.5 - cy) > ACCENT_REACH * rd:
                continue
            near = [button_kit.lab(*colour_at(frame, xx, yy)) for yy in range(y - ACCENT_WINDOW, y + ACCENT_WINDOW + 1) for xx in range(x - ACCENT_WINDOW, x + ACCENT_WINDOW + 1)
                    if 0 <= xx < w and 0 <= yy < h and plain[yy * w + xx] and (xx, yy) != (x, y)]
            if len(near) < 8:
                continue
            median = tuple(sorted(c[k] for c in near)[len(near) // 2] for k in range(3))
            here = button_kit.lab(*colour_at(frame, x, y))
            if math.dist(here, median) > ACCENT_DE:
                mask[i] = True
    for piece in components([not m and alpha_at(frame, i % w, i // w) > 0 for i, m in enumerate(mask)], w, h):  # small holes
        if len(piece) <= HOLE_MAX and not any(i % w in (0, w - 1) or i // w in (0, h - 1) for i in piece):
            for i in piece:
                mask[i] = True
    if "roi" in override:  # a symbol that stands out only in its own region (dark-on-dark elsewhere): the cells of the region read as dark as `max_l` in the pressed frame
        region, max_l = override["roi"]
        inside = set(shape_cells(("poly", region), w, h))
        for i in inside:
            if reading[i] <= max_l and alpha_at(frame, i % w, i // w) > 0:
                mask[i] = True
    for _ in range(override.get("close", 0)):  # morphological closing: the gaps between the strokes of a symbol the pressed frame shows only partly
        grown = dilate(mask, w, h)
        eroded = [grown[i] and all(grown[j] for j in neighbours8(i, w, h)) for i in range(w * h)]
        mask = [mask[i] or (eroded[i] and alpha_at(frame, i % w, i // w) > 0) for i in range(w * h)]
    for key, value in ("shapes", True), ("shapes_drop", False):  # simple forms the original draws as a straight or round shape, straightened
        for shape in override.get(key, ()):
            for i in shape_cells(shape, w, h):
                if alpha_at(frame, i % w, i // w) > 0:
                    mask[i] = value
    for key, value in ("add", True), ("drop", False):
        for x, y in override.get(key, ()):
            if not (0 <= x < w and 0 <= y < h):
                raise SchoolError(f"override {key} cell {x},{y} lies outside the {w} x {h} frame")
            mask[y * w + x] = value
    return mask


# --- the plate template and the collage of the references ---------------------------------------------------------------------

def shape_contains(shape: tuple, px: float, py: float) -> bool:
    """Is the point (px, py), in the frame's cell coordinates (cell i spans i to i + 1), inside a simple shape: ("rect", x0, y0, x1, y1), ("ellipse", cx, cy, rx, ry), ("poly", [(x, y), ...]), ("line", x0, y0, x1, y1, width),
    ("gear", cx, cy, body radius, tooth radius, teeth, phase in turns)."""
    kind = shape[0]
    if kind == "rect":
        return shape[1] <= px <= shape[3] and shape[2] <= py <= shape[4]
    if kind == "ellipse":
        return ((px - shape[1]) / shape[3]) ** 2 + ((py - shape[2]) / shape[4]) ** 2 <= 1.0
    if kind == "poly":
        pts, inside = shape[1], False
        for k in range(len(pts)):
            (x0, y0), (x1, y1) = pts[k], pts[(k + 1) % len(pts)]
            if (y0 > py) != (y1 > py) and px < x0 + (py - y0) * (x1 - x0) / (y1 - y0):
                inside = not inside
        return inside
    if kind == "line":
        x0, y0, x1, y1, width = shape[1:]
        length2 = (x1 - x0) ** 2 + (y1 - y0) ** 2
        t = max(0.0, min(1.0, ((px - x0) * (x1 - x0) + (py - y0) * (y1 - y0)) / length2)) if length2 else 0.0
        return math.hypot(px - (x0 + t * (x1 - x0)), py - (y0 + t * (y1 - y0))) <= width / 2.0
    if kind == "gear":
        _, cx, cy, body, tooth, teeth, phase = shape
        angle = math.atan2(py - cy, px - cx) / (2 * math.pi) * teeth - phase * teeth
        return math.hypot(px - cx, py - cy) <= (tooth if math.cos(2 * math.pi * angle) > 0.15 else body)
    raise SchoolError(f"unknown override shape {kind}")


def shape_cells(shape: tuple, w: int, h: int) -> list[int]:
    """The cells (by their centres) inside a simple shape (`shape_contains`)."""
    return [y * w + x for y in range(h) for x in range(w) if shape_contains(shape, x + 0.5, y + 0.5)]


def dilate(mask: list[bool], w: int, h: int) -> list[bool]:
    return [mask[i] or any(mask[j] for j in neighbours8(i, w, h)) for i in range(w * h)]


def plate_template(frame: Image, mask: list[bool]) -> Image:
    """The idle frame without its symbol: the symbol's cells and their anti-aliased rim (the mask grown by a cell) take the mean of their known neighbours, ring by ring from the cells that are known, then a light 3 x 3 smoothing of those cells only."""
    w, h = frame.w, frame.h
    grown = dilate(mask, w, h)
    known = {(x, y): colour_at(frame, x, y) for y in range(h) for x in range(w) if alpha_at(frame, x, y) and not grown[y * w + x]}
    pending = [(x, y) for y in range(h) for x in range(w) if alpha_at(frame, x, y) and grown[y * w + x]]
    filled_cells = set(pending)
    while pending:
        filled = {}
        for x, y in pending:
            near = [known[(xx, yy)] for yy in range(y - 1, y + 2) for xx in range(x - 1, x + 2) if (xx, yy) in known]
            if near:
                filled[(x, y)] = tuple(int(round(sum(c[k] for c in near) / len(near))) for k in range(3))
        if not filled:
            raise SchoolError("a cell of the template has no known neighbour")
        known.update(filled)
        pending = [p for p in pending if p not in filled]
    for _ in range(3):
        smooth = {}
        for (x, y) in filled_cells:
            near = [known[(xx, yy)] for yy in range(y - 1, y + 2) for xx in range(x - 1, x + 2) if (xx, yy) in known]
            smooth[(x, y)] = tuple(int(round(sum(c[k] for c in near) / len(near))) for k in range(3))
        known.update(smooth)
    out = Image(w, h, bytearray(w * h * 4))
    for y in range(h):
        for x in range(w):
            o = (y * w + x) * 4
            if alpha_at(frame, x, y):
                out.data[o:o + 4] = bytes((*known[(x, y)], 255))
            else:
                out.data[o:o + 3] = frame.data[o:o + 3]
    return out


def collage_size(group: str) -> tuple[int, int]:
    return COLLAGE_CANVAS[group]


def collage_cells(group: str) -> list[dict]:
    """Per button of the group, in order: the cell's rectangle (x, y, w, h) on the collage and the middle of its dome."""
    cols, _ = COLLAGE_GRID[group]
    pw, ph = COLLAGE_PITCH[group]
    return [{"name": t.name, "art": t.art, "rect": [(i % cols) * pw, (i // cols) * ph, pw, ph], "centre": [(i % cols) * pw + pw / 2.0, (i // cols) * ph + ph / 2.0]} for i, t in enumerate(tabs_of(group))]


def collage_reference(group: str, frames: Path, overrides: dict[str, dict] | None = None) -> Image:
    """The original domes without their symbols, nearest cells at `COLLAGE_SCALE` px per unit, each dome's middle on the middle of its cell, on the flat `GROUND`."""
    overrides = OVERRIDES if overrides is None else overrides
    width, height = collage_size(group)
    scale = COLLAGE_SCALE[group]
    out = Image(width, height, bytearray(bytes((*GROUND, 255)) * (width * height)))
    for tab, cell in zip(tabs_of(group), collage_cells(group)):
        idle, pressed = read_frame(frames, tab, 0), read_frame(frames, tab, 1)
        template = plate_template(idle, symbol_mask(idle, pressed, overrides.get(tab.name)))
        cx, cy, _ = disc_of(idle)
        mx, my = cell["centre"]
        x0, y0 = int(mx - cx * scale), int(my - cy * scale)
        for py in range(max(0, y0), min(height, y0 + int(idle.h * scale) + 1)):
            fy = int((py - y0) / scale)
            if not 0 <= fy < idle.h:
                continue
            for px in range(max(0, x0), min(width, x0 + int(idle.w * scale) + 1)):
                fx = int((px - x0) / scale)
                if 0 <= fx < idle.w and alpha_at(template, fx, fy):
                    o = (py * width + px) * 4
                    out.data[o:o + 3] = bytes(colour_at(template, fx, fy))
    return out


# --- the parts by rule ------------------------------------------------------------------------------------------------------

LIFT_MIN_ALPHA = 0.62       # a lit overlay lays its colour at this opacity at least, so the plate's own texture shows through it (the colour is pushed further from the plate's, within the byte range, to give the frame's colour back)
LIFT_SIGMA = 0.90           # cells: the Gaussian of the soft layer
GLOSS_WINDOW = 3            # cells: the window of the median that stands for the dome without its highlight
GLOSS_DL = 14.0             # CIE L: a cell this much lighter than that median, on the upper left, belongs to the highlight
GLOSS_REACH = 0.97          # the highlight lies within this share of the dome's radius
GLOSS_MIN_ALPHA = 0.6       # the least opacity of the highlight layer (lift_of)
GLOSS_SIGMA = 0.55          # cells: the Gaussian of the highlight layer
SIL_SIGMA = 0.60            # cells: the Gaussian of the outline's mask (the original's staircase outline, smoothed so the brass ring shows no bumps at the master scale)
_MANIFEST: dict | None = None


def master_of(name: str) -> tuple[int, int]:
    """The plate master's size of a button (the manifest row `plate.<name>`)."""
    global _MANIFEST
    if _MANIFEST is None:
        _MANIFEST = json.loads(MANIFEST.read_text(encoding="utf-8"))
    for part in _MANIFEST["parts"]:
        if part["id"] == f"plate.{name}" and part.get("size"):
            return part["size"][0], part["size"][1]
    raise SchoolError(f"the manifest has no sized row plate.{name}")


def silhouette_clip(frame: Image, pixels: tuple[int, int], sigma: float = SIL_SIGMA) -> Image:
    """The frame's outline at the master size, a pixel of soft edge: what clips an overlay when there is no plate master yet."""
    alpha = button_kit.resample_plane([1.0 if frame.data[i + 3] else 0.0 for i in range(0, len(frame.data), 4)], (frame.w, frame.h), pixels, sigma)
    out = Image(pixels[0], pixels[1], bytearray(pixels[0] * pixels[1] * 4))
    for i, a in enumerate(alpha):
        out.data[i * 4 + 3] = int(round(max(0.0, min(1.0, (a - 0.5) * 2.0 + 0.5)) * 255))
    return out


def lift_of(base: tuple[int, int, int], lit: tuple[int, int, int], minimum: float = LIFT_MIN_ALPHA) -> tuple[float, tuple[float, float, float]]:
    """The layer (opacity, colour) that, laid over `base`, gives `lit`: the least opacity that keeps the colour inside the byte range, but at least `minimum`; (0, base) when the two are the same colour."""
    if button_kit.distance(base, lit) <= button_kit.DEAD_DE:
        return 0.0, tuple(float(v) for v in base)
    need = 0.0
    for b, p in zip(base, lit):
        if p > b:
            need = max(need, (p - b) / (255.0 - b))
        elif p < b:
            need = max(need, (b - p) / float(b))
    alpha = max(minimum, min(1.0, need))
    return alpha, tuple(max(0.0, min(255.0, b + (p - b) / alpha)) for b, p in zip(base, lit))


def soft_layer(cells: list[tuple[float, tuple[float, float, float]]], units: tuple[int, int], pixels: tuple[int, int], sigma: float) -> Image:
    """A layer from per-cell (opacity, colour): both resampled as planes (the colour premultiplied), the colour of a pixel is the opacity-weighted mean."""
    alphas = [a for a, _ in cells]
    ga = button_kit.resample_plane(alphas, units, pixels, sigma)
    gc = [button_kit.resample_plane([a * c[k] for a, c in cells], units, pixels, sigma) for k in range(3)]
    out = Image(pixels[0], pixels[1], bytearray(pixels[0] * pixels[1] * 4))
    for i in range(pixels[0] * pixels[1]):
        a = min(1.0, max(0.0, ga[i]))
        if a <= 0.002 or ga[i] <= 1e-9:
            continue
        out.data[i * 4:i * 4 + 4] = bytes((*(max(0, min(255, int(round(gc[k][i] / ga[i])))) for k in range(3)), int(round(a * 255))))
    return out


def fill_from_neighbours(values: list, w: int, h: int, unknown: list[bool]) -> list:
    """Per-cell (opacity, colour) values where `unknown` cells take the mean of their known neighbours, ring by ring (what a lit layer is under the symbol)."""
    out = list(values)
    known = [not u for u in unknown]
    pending = [i for i, u in enumerate(unknown) if u]
    while pending:
        nxt = {}
        for i in pending:
            near = [out[j] for j in neighbours8(i, w, h) if known[j]]
            if near:
                a = sum(v[0] for v in near) / len(near)
                nxt[i] = (a, tuple(sum(v[0] * v[1][k] for v in near) / max(1e-9, sum(v[0] for v in near)) if sum(v[0] for v in near) > 1e-9 else near[0][1][k] for k in range(3)))
        if not nxt:
            break
        for i, v in nxt.items():
            out[i] = v
            known[i] = True
        pending = [i for i in pending if i not in nxt]
    return out


def over(under: Image, top: Image) -> Image:
    return button_kit.composite(under, top)


def multiply_alpha(layer: Image, clip: Image) -> Image:
    out = Image(layer.w, layer.h, bytearray(layer.data))
    for i in range(3, len(out.data), 4):
        out.data[i] = out.data[i] * clip.data[i] // 255
    return out


GLYPH_SS = 3                # supersampling of the symbol's outline (samples per master pixel, per axis)
GLYPH_SIGMA = 0.62          # cells: the Gaussian that turns the mask's staircase into a smooth curve
GLYPH_LEVEL = 0.42          # the field's level of the outline (below 0.5: a thin line keeps its width)
STROKE_W = 1.15             # cells: the width every thin line of a symbol is drawn at (a line the original draws one or two cells wide, in its centreline)
SOLID_R = 1.2               # cells: a part of the symbol thicker than twice this stays a solid area (the hat's crown, a gear's body), only the thinner parts become strokes
ENGRAVE_PX = 1.1            # master pixels: the width of the light rim (upper left) and the shade (lower right) of the engraved symbol
ENGRAVE_LIGHT = 0.5         # the rim: this share of the way from the symbol's colour to white
ENGRAVE_SHADE = 0.5         # the shade: the symbol's colour times this


@dataclass(frozen=True)
class GlyphShape:
    """The symbol as a smooth curve at the master's size: per master pixel the coverage and the shares of it that lie in the light rim (upper left) and the shade (lower right)."""
    w: int
    h: int
    cover: tuple
    rim: tuple
    shade: tuple


def _phi(x: float) -> float:
    return 0.5 * (1.0 + math.erf(x / math.sqrt(2.0)))


def _axis_weights(cells: int, samples: int) -> list[list[tuple[int, float]]]:
    """For each sample along an axis, the weight of the cells near it: the share of a Gaussian (GLYPH_SIGMA cells) centred there that lies in the cell."""
    out = []
    for u in range(samples):
        c = (u + 0.5) / samples * cells
        row = []
        for i in range(max(0, int(c - 4 * GLYPH_SIGMA) - 1), min(cells, int(c + 4 * GLYPH_SIGMA) + 2)):
            weight = _phi((i + 1 - c) / GLYPH_SIGMA) - _phi((i - c) / GLYPH_SIGMA)
            if weight > 1e-4:
                row.append((i, weight))
        out.append(row)
    return out


def _thin(grid: list[bytearray]) -> list[bytearray]:
    """Zhang-Suen thinning: the one-sample centreline of every part of the shape."""
    h, w = len(grid), len(grid[0])
    img = [bytearray(row) for row in grid]
    changed = True
    while changed:
        changed = False
        for step in (0, 1):
            drop = []
            for y in range(1, h - 1):
                up, mid, down = img[y - 1], img[y], img[y + 1]
                for x in range(1, w - 1):
                    if not mid[x]:
                        continue
                    p2, p3, p4, p5, p6, p7, p8, p9 = up[x], up[x + 1], mid[x + 1], down[x + 1], down[x], down[x - 1], mid[x - 1], up[x - 1]
                    n = p2 + p3 + p4 + p5 + p6 + p7 + p8 + p9
                    if n < 2 or n > 6:
                        continue
                    transitions = (p2 == 0 and p3) + (p3 == 0 and p4) + (p4 == 0 and p5) + (p5 == 0 and p6) + (p6 == 0 and p7) + (p7 == 0 and p8) + (p8 == 0 and p9) + (p9 == 0 and p2)
                    if transitions != 1:
                        continue
                    if step == 0:
                        if p2 * p4 * p6 or p4 * p6 * p8:
                            continue
                    elif p2 * p4 * p8 or p2 * p6 * p8:
                        continue
                    drop.append((y, x))
            for y, x in drop:
                img[y][x] = 0
            changed = changed or bool(drop)
    return img


def _distance(grid: list[bytearray]) -> list[list[float]]:
    """Distance of every sample of the shape to the nearest sample outside it (two-pass chamfer, in samples)."""
    h, w = len(grid), len(grid[0])
    big = 1e9
    d = [[0.0 if not grid[y][x] else big for x in range(w)] for y in range(h)]
    r2 = math.sqrt(2.0)
    for y in range(h):
        for x in range(w):
            if d[y][x]:
                best = d[y][x]
                if x: best = min(best, d[y][x - 1] + 1.0)
                if y:
                    best = min(best, d[y - 1][x] + 1.0)
                    if x: best = min(best, d[y - 1][x - 1] + r2)
                    if x < w - 1: best = min(best, d[y - 1][x + 1] + r2)
                d[y][x] = best
    for y in range(h - 1, -1, -1):
        for x in range(w - 1, -1, -1):
            if d[y][x]:
                best = d[y][x]
                if x < w - 1: best = min(best, d[y][x + 1] + 1.0)
                if y < h - 1:
                    best = min(best, d[y + 1][x] + 1.0)
                    if x < w - 1: best = min(best, d[y + 1][x + 1] + r2)
                    if x: best = min(best, d[y + 1][x - 1] + r2)
                d[y][x] = best
    return d


def stroked(inside: list[bytearray], sample: float) -> list[bytearray]:
    """The shape with its thin parts redrawn as lines of one width (`STROKE_W` cells) along their centrelines, round at the ends, and its thick parts (thicker than twice `SOLID_R`) kept as they are: `sample` is the number of samples per cell."""
    h, w = len(inside), len(inside[0])
    dist = _distance(inside)
    core_r, line_r = SOLID_R * sample, STROKE_W * sample / 2.0
    skeleton = _thin(inside)
    out = [bytearray(w) for _ in range(h)]

    def stamp(cx: int, cy: int, radius: float, only_inside: bool) -> None:
        r = int(math.ceil(radius))
        for y in range(max(0, cy - r), min(h, cy + r + 1)):
            row, src = out[y], inside[y]
            for x in range(max(0, cx - r), min(w, cx + r + 1)):
                if (x - cx) ** 2 + (y - cy) ** 2 <= radius * radius and (src[x] or not only_inside):
                    row[x] = 1
    for y in range(h):
        for x in range(w):
            if dist[y][x] >= core_r:
                stamp(x, y, core_r, True)   # the thick parts: the opening of the shape by a disc of core_r
            elif skeleton[y][x]:
                stamp(x, y, line_r, False)
    return out


def glyph_shape(mask: list[bool], w: int, h: int, pixels: tuple[int, int], add: tuple = (), drop: tuple = (), lines: bool = False) -> GlyphShape:
    """The symbol's outline as a smooth curve: the mask's cells blurred by `GLYPH_SIGMA` and cut at `GLYPH_LEVEL` (a thin line keeps its width, a staircase becomes a curve), the exact simple shapes of an override laid in (`add`) and cut out (`drop`)
    at the sub-pixel level, so a circle is round and a straight edge straight. Then the light rim at the upper left and the shade at the lower right of the outline."""
    pw, ph = pixels
    sw, sh = pw * GLYPH_SS, ph * GLYPH_SS
    wx, wy = _axis_weights(w, sw), _axis_weights(h, sh)
    rows = [[1.0 if mask[j * w + i] else 0.0 for i in range(w)] for j in range(h)]
    temp = {}   # T[j][u]: the mask's row j blurred along x at every sample column
    for j in range(h):
        if any(rows[j]):
            temp[j] = [sum(rows[j][i] * weight for i, weight in wx[u]) for u in range(sw)]
    inside = []
    for v in range(sh):
        field = [0.0] * sw
        for j, weight in wy[v]:
            if j in temp:
                t = temp[j]
                for u in range(sw):
                    field[u] += weight * t[u]
        line = bytearray(sw)
        cy = (v + 0.5) / sh * h
        for u in range(sw):
            line[u] = 1 if field[u] >= GLYPH_LEVEL else 0
        inside.append(line)
    if lines and any(any(row) for row in inside):
        inside = stroked(inside, GLYPH_SS * (pw / w + ph / h) / 2.0)
    if add or drop:   # the exact shapes of an override, laid in and cut out after the lines are drawn
        for v in range(sh):
            cy = (v + 0.5) / sh * h
            for u in range(sw):
                cx = (u + 0.5) / sw * w
                if any(shape_contains(shape, cx, cy) for shape in add):
                    inside[v][u] = 1
                if inside[v][u] and any(shape_contains(shape, cx, cy) for shape in drop):
                    inside[v][u] = 0
    d = max(1, int(round(ENGRAVE_PX * GLYPH_SS)))
    cover, rim, shade = [], [], []
    count = float(GLYPH_SS * GLYPH_SS)
    for py in range(ph):
        for px in range(pw):
            c = r = sd = 0
            for v in range(py * GLYPH_SS, (py + 1) * GLYPH_SS):
                for u in range(px * GLYPH_SS, (px + 1) * GLYPH_SS):
                    if not inside[v][u]:
                        continue
                    c += 1
                    if v - d < 0 or u - d < 0 or not inside[v - d][u - d]:
                        r += 1
                    if v + d >= sh or u + d >= sw or not inside[v + d][u + d]:
                        sd += 1
            cover.append(c / count)
            rim.append(r / c if c else 0.0)
            shade.append(sd / c if c else 0.0)
    return GlyphShape(pw, ph, tuple(cover), tuple(rim), tuple(shade))


def symbol_colour(frame: Image, mask: list[bool]) -> tuple[int, int, int]:
    """The symbol's own colour in a frame (dark, or light and coloured where the original draws it so): the median of the cells inside the mask (those with no neighbour outside it, or all of them for a thin line)."""
    w, h = frame.w, frame.h
    cells = [i for i, on in enumerate(mask) if on]
    if not cells:
        return (0, 0, 0)
    inner = [i for i in cells if all(mask[j] for j in neighbours8(i, w, h)) and 0 < i % w < w - 1 and 0 < i // w < h - 1]
    pick = inner if len(inner) >= 4 else cells
    colours = [colour_at(frame, i % w, i // w) for i in pick]
    return tuple(sorted(c[k] for c in colours)[len(colours) // 2] for k in range(3))


def paint_symbol(shape: GlyphShape, colour: tuple[int, int, int]) -> Image:
    """The symbol: one solid colour, no cells, a light rim at the upper left and a shade at the lower right (an engraving), the outline a smooth curve."""
    light = tuple(c + (255 - c) * ENGRAVE_LIGHT for c in colour)
    dark = tuple(c * ENGRAVE_SHADE for c in colour)
    out = Image(shape.w, shape.h, bytearray(shape.w * shape.h * 4))
    for i, cover in enumerate(shape.cover):
        if cover <= 0.0:
            continue
        lit, shaded = shape.rim[i], shape.shade[i]
        for k in range(3):
            value = colour[k] * (1 - lit) + light[k] * lit
            out.data[i * 4 + k] = int(round(max(0.0, min(255.0, value * (1 - shaded) + dark[k] * shaded))))
        out.data[i * 4 + 3] = int(round(cover * 255))
    return out


def lit_overlay(frame: Image, idle: Image, mask: list[bool], pixels: tuple[int, int], clip: Image, symbol: Image) -> Image:
    """What `frame` (the hover or the pressed one) shows over the IDLE frame: the dome's lighting as a soft layer (`lift_of`: the plate's texture shows through), under the lit symbol (`symbol`: the same outline in the lit colour)."""
    w, h = idle.w, idle.h
    cells = []
    for y in range(h):
        for x in range(w):
            cells.append(lift_of(colour_at(idle, x, y), colour_at(frame, x, y)) if alpha_at(idle, x, y) else (0.0, (0.0, 0.0, 0.0)))
    cells = fill_from_neighbours(cells, w, h, dilate(mask, w, h))
    soft = soft_layer(cells, (w, h), pixels, LIFT_SIGMA)
    return multiply_alpha(over(soft, symbol), clip)


def gloss_layer(template: Image, pixels: tuple[int, int]) -> Image:
    """The original's highlight (the lighter cells on the dome's upper left) as a layer over the plate, in its shape and brightness: per cell the lift from the median colour of its window to the cell's own."""
    w, h = template.w, template.h
    cx, cy, _ = disc = disc_of(template)
    rd = dome_radius(disc)
    light = lightness_plane(template)
    cells = []
    for y in range(h):
        for x in range(w):
            if not alpha_at(template, x, y) or (x + 0.5 - cx) + (y + 0.5 - cy) > 0 or math.hypot(x + 0.5 - cx, y + 0.5 - cy) > GLOSS_REACH * rd:
                cells.append((0.0, (0.0, 0.0, 0.0)))
                continue
            near = [(light[yy * w + xx], colour_at(template, xx, yy)) for yy in range(y - GLOSS_WINDOW, y + GLOSS_WINDOW + 1) for xx in range(x - GLOSS_WINDOW, x + GLOSS_WINDOW + 1)
                    if 0 <= xx < w and 0 <= yy < h and alpha_at(template, xx, yy) and math.hypot(xx + 0.5 - cx, yy + 0.5 - cy) <= 0.98 * rd]
            near.sort(key=lambda t: t[0])
            median_l, median_c = near[len(near) // 2]
            if light[y * w + x] - median_l < GLOSS_DL:
                cells.append((0.0, (0.0, 0.0, 0.0)))
                continue
            cells.append(lift_of(median_c, colour_at(template, x, y), GLOSS_MIN_ALPHA))
    return soft_layer(cells, (w, h), pixels, GLOSS_SIGMA)


RING_UNITS = 2.6            # the brass ring's width in the original's cells (its outer line, the lit band and the shaded inner band), the same for both groups
RING_SOURCE = "hardware.menu_ring"   # the master whose brass the ring takes: the ring of the menu strips' hardware (#3259), cut and fitted there; the tone of the kit's brass rings
RING_BINS, RING_ANGLES = 12, 16
RING_GAIN = (0.78, 1.28)    # the light's share over the angles (upper left lit, lower right shaded) stays within this


def ring_profile(local: Path) -> tuple[list[tuple[float, float, float]], list[float]]:
    """The cross-section of the hardware ring (the mean colour from its outer edge to its hole, `RING_BINS` bins) and the light over its angles (`RING_ANGLES` gains around 1)."""
    path = local / "masters" / f"{RING_SOURCE}.png"
    if not path.is_file():
        raise SchoolError(f"no ring master {path}: the ring takes its brass from {RING_SOURCE}")
    ring = pngio.read(path)
    cells = [(i % ring.w, i // ring.w) for i in range(ring.w * ring.h) if ring.data[i * 4 + 3] > 128]
    if not cells:
        raise SchoolError(f"{path} is empty")
    cx = (min(x for x, _ in cells) + max(x for x, _ in cells) + 1) / 2.0
    cy = (min(y for _, y in cells) + max(y for _, y in cells) + 1) / 2.0
    radii = [math.hypot(x + 0.5 - cx, y + 0.5 - cy) for x, y in cells]
    r_out, r_in = max(radii), min(radii)
    if r_out - r_in < 2.0:
        raise SchoolError(f"{path}: the ring is thinner than two cells ({r_out - r_in:.1f}), no cross-section to read")
    sums = [[0.0, 0.0, 0.0, 0] for _ in range(RING_BINS)]
    light = [[0.0, 0] for _ in range(RING_ANGLES)]
    for (x, y), r in zip(cells, radii):
        b = min(RING_BINS - 1, int((r_out - r) / (r_out - r_in) * RING_BINS))
        colour = colour_at(ring, x, y)
        for k in range(3):
            sums[b][k] += colour[k]
        sums[b][3] += 1
        if 0.2 < (r_out - r) / (r_out - r_in) < 0.8:   # the lit band and the shaded band, not the dark edges
            a = min(RING_ANGLES - 1, int(((math.atan2(y + 0.5 - cy, x + 0.5 - cx) + math.pi) / (2 * math.pi)) * RING_ANGLES))
            light[a][0] += sum(colour) / 3.0
            light[a][1] += 1
    profile = [tuple(c[k] / c[3] for k in range(3)) if c[3] else None for c in sums]
    if not any(profile):
        raise SchoolError(f"{path}: no ring cells to read")
    for b in range(RING_BINS):   # an empty bin takes its nearest filled neighbour
        if profile[b] is None:
            profile[b] = next(profile[b + d * sign] for d in range(1, RING_BINS) for sign in (-1, 1) if 0 <= b + d * sign < RING_BINS and profile[b + d * sign] is not None)
    if not any(n for _, n in light):
        raise SchoolError(f"{path}: no cells in the ring's lit and shaded bands")
    filled = [v / n for v, n in light if n]
    means = [v / n if n else sum(filled) / len(filled) for v, n in light]
    overall = sum(means) / len(means)
    gains = [max(RING_GAIN[0], min(RING_GAIN[1], m / overall)) for m in means]
    return profile, [(gains[k - 1] + 2 * gains[k] + gains[(k + 1) % len(gains)]) / 4.0 for k in range(len(gains))]   # smoothed


def paint_ring(plate: Image, centre: tuple[float, float], r_out: float, width: float, ring: tuple[list[tuple[float, float, float]], list[float]]) -> Image:
    """The plate inside the circle `r_out`, with the brass ring laid over its outer `width` (smooth edges), the tone from the hardware ring's cross-section and its light over the angles."""
    profile, gains = ring
    cx, cy = centre
    out = Image(plate.w, plate.h, bytearray(plate.w * plate.h * 4))
    for y in range(plate.h):
        for x in range(plate.w):
            dx, dy = x + 0.5 - cx, y + 0.5 - cy
            r = math.hypot(dx, dy)
            a_out = max(0.0, min(1.0, r_out - r + 0.5))
            if a_out <= 0.0:
                continue
            w = max(0.0, min(1.0, r - (r_out - width) + 0.5))
            t = max(0.0, min(1.0, (r_out - r) / width)) * RING_BINS - 0.5
            lo = max(0, min(RING_BINS - 1, int(math.floor(t))))
            hi = min(RING_BINS - 1, lo + 1)
            f = max(0.0, min(1.0, t - lo))
            ang = (math.atan2(dy, dx) + math.pi) / (2 * math.pi) * RING_ANGLES - 0.5
            g0 = int(math.floor(ang)) % RING_ANGLES
            g1 = (g0 + 1) % RING_ANGLES
            gain = gains[g0] + (gains[g1] - gains[g0]) * (ang - math.floor(ang))
            ring_colour = [max(0.0, min(255.0, (profile[lo][k] * (1 - f) + profile[hi][k] * f) * gain)) for k in range(3)]
            under = colour_at(plate, x, y) if alpha_at(plate, x, y) else ring_colour
            i = (y * plate.w + x) * 4
            for k in range(3):
                out.data[i + k] = int(round(under[k] * (1 - w) + ring_colour[k] * w))
            out.data[i + 3] = int(round(a_out * 255))
    return out


def override_of(tab: Tab) -> dict:
    """The button's override; the school buttons draw dark silhouettes, so their symbols get no coloured body unless an override asks for one."""
    override = dict(OVERRIDES.get(tab.name) or {})
    if tab.group == "school":
        override.setdefault("fill", 0)
    return override


def tab_inputs(frames: Path, tab: Tab) -> tuple[Image, Image, Image, list[bool], Image]:
    idle, pressed, hover = (read_frame(frames, tab, k) for k in (0, 1, 2))
    for frame in (pressed, hover):
        if (frame.w, frame.h) != (idle.w, idle.h) or any(alpha_at(frame, i % idle.w, i // idle.w) != alpha_at(idle, i % idle.w, i // idle.w) for i in range(idle.w * idle.h)):
            raise SchoolError(f"{tab.folder}: the three frames do not share one outline")
    mask = symbol_mask(idle, pressed, override_of(tab))
    return idle, pressed, hover, mask, plate_template(idle, mask)


def read_plate(plate_dir: Path | None, name: str) -> Image | None:
    path = plate_dir / f"plate.{name}.png" if plate_dir else None
    if path is None or not path.is_file():
        return None
    master = pngio.read(path)
    if (master.w, master.h) != master_of(name):
        raise SchoolError(f"{path}: {master.w} x {master.h}, the plate master is {master_of(name)}")
    return master


@dataclass(frozen=True)
class TabSymbol:
    """The symbol of a button as smooth outlines: the dark outline (`dark`) and, where the original draws a coloured body inside it (the fire's orange), the body's silhouette under it (`body`), with the cells each colour is read from."""
    dark: GlyphShape
    body: GlyphShape | None
    dark_cells: list
    body_cells: list


def tab_symbol(frames: Path, tab: Tab, pixels: tuple[int, int]) -> TabSymbol:
    idle, pressed = read_frame(frames, tab, 0), read_frame(frames, tab, 1)
    override = override_of(tab)
    plain = {k: v for k, v in override.items() if k not in ("shapes", "shapes_drop")}
    add, drop = tuple(override.get("shapes", ())), tuple(override.get("shapes_drop", ()))
    n = idle.w * idle.h
    if override.get("shapes_only"):   # the shapes are the whole symbol (a top hat, two gears)
        cells = symbol_mask(idle, pressed, override)
        return TabSymbol(glyph_shape([False] * n, idle.w, idle.h, pixels, add, drop, lines=True), None, cells, [False] * n)
    dark = symbol_mask(idle, pressed, dict(plain, fill=0))
    full = symbol_mask(idle, pressed, plain)
    body = [f and not d for f, d in zip(full, dark)]
    shape_body = glyph_shape(full, idle.w, idle.h, pixels, add, drop) if sum(body) >= max(6, 0.3 * sum(dark)) else None
    if shape_body is None:
        body = [False] * n
    return TabSymbol(glyph_shape(dark, idle.w, idle.h, pixels, add, drop, lines=not override.get("solid")), shape_body, dark, body)


def symbol_image(symbol: TabSymbol, frame: Image) -> Image:
    """The symbol in a frame's colours: the body (if any) in its own colour, the dark outline over it."""
    out = Image(symbol.dark.w, symbol.dark.h, bytearray(symbol.dark.w * symbol.dark.h * 4))
    if symbol.body is not None:
        out = over(out, paint_symbol(symbol.body, symbol_colour(frame, symbol.body_cells)))
    return over(out, paint_symbol(symbol.dark, symbol_colour(frame, symbol.dark_cells)))


def tab_parts(frames: Path, tab: Tab, plate_dir: Path | None) -> dict[str, Image]:
    idle, pressed, hover, mask, template = tab_inputs(frames, tab)
    pixels = master_of(tab.name)
    plate = read_plate(plate_dir, tab.name)
    clip = plate if plate is not None else silhouette_clip(idle, pixels)
    symbol = tab_symbol(frames, tab, pixels)
    return {f"glyph.{tab.name}": symbol_image(symbol, idle),
            f"plate.{tab.name}_hover": lit_overlay(hover, idle, mask, pixels, clip, symbol_image(symbol, hover)),
            f"plate.{tab.name}_pressed": lit_overlay(pressed, idle, mask, pixels, clip, symbol_image(symbol, pressed))}


def build_parts(frames: Path, plate_dir: Path | None, tabs: list[Tab]) -> dict[str, Image]:
    parts: dict[str, Image] = {}
    for tab in tabs:
        parts.update(tab_parts(frames, tab, plate_dir))
    return parts


# --- commands -----------------------------------------------------------------------------------------------------------------

def need_magick() -> None:
    if shutil.which("magick") is None:
        raise SchoolError("ImageMagick `magick` is not installed")


def work_dir(local: Path) -> Path:
    return local / "work" / "B-BUTTONS"


# --- cutting the collage ----------------------------------------------------------------------------------------------------

FG_DE = 36.0               # RGB distance from the ground at which a pixel of the generated collage is a button
ROUND_TOLERANCE = 0.10     # the width and height of a cut dome differ by this share at most
SIZE_TOLERANCE = 0.14      # a cut dome's diameter differs from the layout's by this share at most (after the canvas scale)
ENLARGE = 1.02             # the generated dome's outer edge is set this far beyond the original's outline (the ground's fringe then lies outside the contour)
PLACE_SCALE = 6            # the placed source is this many times the master
EDGE_PULL = {"college": 8.0, "school": 14.0}   # placed px: the outline circle lies this far inside the generated dome's outer edge (about the dome's own darkest outer line: the fit tool's halo gate then holds)


def ground_of(image: Image) -> tuple[int, int, int]:
    border = [colour_at(image, x, y) for x in range(0, image.w, 7) for y in (0, image.h - 1)] + [colour_at(image, x, y) for y in range(0, image.h, 7) for x in (0, image.w - 1)]
    return tuple(sorted(c[k] for c in border)[len(border) // 2] for k in range(3))


def locate(image: Image, layout: dict) -> dict[str, tuple[float, float, float]]:
    """Per button the middle and radius (source pixels) of the dome the generator drew in its cell: the largest connected piece that differs from the ground inside the cell's rectangle (laid on the result by the canvas ratio)."""
    cw, ch = layout["canvas"]
    sx, sy = image.w / cw, image.h / ch
    if abs(sx - sy) / max(sx, sy) > 0.03:
        raise SchoolError(f"the result is {image.w} x {image.h}, not the aspect of the collage {cw} x {ch}")
    ground = ground_of(image)
    step = 2
    out = {}
    for cell in layout["cells"]:
        x0, y0, rw, rh = (v * s for v, s in zip(cell["rect"], (sx, sy, sx, sy)))
        gx0, gy0 = int(x0 // step), int(y0 // step)
        gw, gh = int(rw // step), int(rh // step)
        fg = [False] * (gw * gh)
        for gy in range(gh):
            for gx in range(gw):
                px, py = int((gx0 + gx) * step), int((gy0 + gy) * step)
                if px < image.w and py < image.h:
                    c = colour_at(image, px, py)
                    fg[gy * gw + gx] = sum(abs(a - b) for a, b in zip(c, ground)) > FG_DE
        pieces = components(fg, gw, gh)
        if not pieces:
            raise SchoolError(f"{cell['name']}: no dome in its cell")
        piece = max(pieces, key=len)
        xs, ys = [i % gw for i in piece], [i // gw for i in piece]
        bw, bh = (max(xs) - min(xs) + 1) * step, (max(ys) - min(ys) + 1) * step
        if abs(bw - bh) / max(bw, bh) > ROUND_TOLERANCE:
            raise SchoolError(f"{cell['name']}: the cut piece is {bw} x {bh} px, not round")
        out[cell["name"]] = ((gx0 + (min(xs) + max(xs) + 1) / 2.0) * step, (gy0 + (min(ys) + max(ys) + 1) / 2.0) * step, (bw + bh) / 4.0)
    return out


def placed_dir(local: Path) -> Path:
    return work_dir(local) / "placed"


def cmd_place(args: argparse.Namespace) -> int:
    """One dome of the generated collage per button, on a canvas `PLACE_SCALE` times the master where the original's disc stands, the alpha the original's outline (smooth): the `--source` of `fit.py fit plate.<name>`."""
    local = args.local_root
    frames = args.frames or local / "census" / "frames"
    need_magick()
    if args.source and not args.group:
        raise SchoolError("--source needs --group: one image cannot be cut against both layouts")
    out_dir = args.out or placed_dir(local)
    out_dir.mkdir(parents=True, exist_ok=True)
    for group in ([args.group] if args.group else list(GROUPS)):
        prompt = GROUPS[group]
        source = args.source or local / "outbox" / "B-BUTTONS" / f"{prompt}.png"
        record_path = source.with_name(source.stem + ".provenance.json")
        if not record_path.is_file():
            raise SchoolError(f"no generator record {record_path} (SWI-GEN-6)")
        record = json.loads(record_path.read_text(encoding="utf-8"))
        if record.get("image_sha256") != button_kit.sha256_file(source):
            raise SchoolError(f"{source.name} differs from the image its record describes")
        layout = json.loads((local / "refs" / f"{prompt}-layout.json").read_text(encoding="utf-8"))
        image = pngio.read(source)
        found = locate(image, layout)
        scale = image.w / layout["canvas"][0]
        for tab in tabs_of(group):
            idle = read_frame(frames, tab, 0)
            cx, cy, radius = disc_of(idle)
            pw, ph = master_of(tab.name)
            kx, ky = PLACE_SCALE * pw / idle.w, PLACE_SCALE * ph / idle.h
            fx, fy, fr = found[tab.name]
            expected = next(c for c in layout["cells"] if c["name"] == tab.name)
            if abs(fr - COLLAGE_SCALE[group] * radius * scale) / (COLLAGE_SCALE[group] * radius * scale) > SIZE_TOLERANCE:
                raise SchoolError(f"{tab.name}: the dome's radius is {fr:.0f} px, the layout's {COLLAGE_SCALE[group] * radius * scale:.0f} px")
            box = 2.0 * fr * 1.04
            if fx - box / 2 < 0 or fy - box / 2 < 0 or fx + box / 2 > image.w or fy + box / 2 > image.h:
                raise SchoolError(f"{tab.name}: the dome's crop box leaves the generated canvas ({image.w} x {image.h})")
            target_w, target_h = 2.0 * radius * ENLARGE * 1.04 * kx, 2.0 * radius * ENLARGE * 1.04 * ky
            left, top = cx * kx - target_w / 2.0, cy * ky - target_h / 2.0
            path = out_dir / f"{tab.name}-placed.png"
            subprocess.run(["magick", "-size", f"{PLACE_SCALE * pw}x{PLACE_SCALE * ph}", "xc:none", "(", str(source), "-crop", f"{int(round(box))}x{int(round(box))}+{int(round(fx - box / 2))}+{int(round(fy - box / 2))}", "+repage",
                            "-filter", "Lanczos", "-resize", f"{int(round(target_w))}x{int(round(target_h))}!", ")", "-geometry", f"{int(round(left)):+d}{int(round(top)):+d}", "-composite", f"PNG32:{path}"], check=True)
            placed = pngio.read(path)
            edge = radius * ENLARGE * (kx + ky) / 2.0 - EDGE_PULL[group]  # the outline: the circle of the original frame's area (its staircase outline smoothed to a circle), a little inside the generated dome's own outermost line
            for py in range(placed.h):
                for px in range(placed.w):
                    r = math.hypot((px + 0.5) / kx * ((kx + ky) / 2.0) - cx * (kx + ky) / 2.0, (py + 0.5) / ky * ((kx + ky) / 2.0) - cy * (kx + ky) / 2.0)
                    placed.data[(py * placed.w + px) * 4 + 3] = int(round(max(0.0, min(1.0, edge - r + 0.5)) * 255))
            pngio.write(path, placed)
            derived = dict(record)
            derived["derived_from"] = {"image": record.get("image"), "image_sha256": record["image_sha256"], "pixel_size": record.get("pixel_size"),
                                       "step": f"school_kit.py place (cell {expected['name']}: dome of radius {fr:.0f} px at {fx:.0f},{fy:.0f} -> {int(round(target_w))} x {int(round(target_h))} at {int(round(left)):+d}{int(round(top)):+d} on a {placed.w} x {placed.h} canvas, alpha = the original frame's outline)"}
            derived["image"], derived["image_sha256"], derived["pixel_size"] = path.name, button_kit.sha256_file(path), [placed.w, placed.h]
            path.with_name(path.stem + ".provenance.json").write_text(json.dumps(derived, indent=2) + "\n", encoding="utf-8")
            print(f"{path.name}  {placed.w} x {placed.h}  dome {fr:.0f} px at {fx:.0f},{fy:.0f}")
    print("SCHOOL KIT place PASS")
    return 0


HALO_LIMITS = "16,40"      # the fit tool's gate is relaxed as in slices 2 and 3 (#3073): mean, 95th percentile; the safety net leaves p95 under 3


def cmd_fit(args: argparse.Namespace) -> int:
    """`fit.py fit plate.<name>` for every button of the group, from the placed sources, into `work/B-BUTTONS/fitted` (the plates before the highlight)."""
    local = args.local_root
    fitted = args.fitted_dir or work_dir(local) / "fitted"
    fitted.mkdir(parents=True, exist_ok=True)
    only = set(args.only.split(",")) if args.only else None
    if only and only - {t.name for t in TABS}:
        raise SchoolError(f"--only names buttons this slice does not hold: {sorted(only - {t.name for t in TABS})}")
    for tab in tabs_of(args.group):
        if only and tab.name not in only:
            continue
        source = placed_dir(local) / f"{tab.name}-placed.png"
        command = [sys.executable, "-B", str(HERE / "fit.py"), "fit", f"plate.{tab.name}", "--source", str(source), "--provenance", str(source.with_name(source.stem + ".provenance.json")),
                   "--masters", str(fitted), "--local-root", str(local), "--halo-limits", HALO_LIMITS, "--key", "none", "--replace"]
        done = subprocess.run(command, capture_output=True, text=True)
        print(f"plate.{tab.name:22s} {'ok' if done.returncode == 0 else 'FAIL ' + (done.stdout + done.stderr).strip()[-300:]}")
        if done.returncode != 0:
            return 1
    print("SCHOOL KIT fit PASS (the fit record now describes the fitted master: run gloss next to refresh the plate masters and the record)")
    return 0


def cmd_gloss(args: argparse.Namespace) -> int:
    """The fitted domes get the original's highlight laid over them (shape and brightness read from the template's lighter cells on the upper left): final plate masters; the fit's own master is kept in the fit record."""
    local = args.local_root
    frames = args.frames or local / "census" / "frames"
    fitted = args.fitted_dir or work_dir(local) / "fitted"
    out = args.out or local / "masters"
    plan = []
    for tab in tabs_of(args.group):
        source = fitted / f"plate.{tab.name}.png"
        if not source.is_file():
            raise SchoolError(f"no fitted master {source} (run `fit` first)")
        plate = pngio.read(source)
        if (plate.w, plate.h) != master_of(tab.name):
            raise SchoolError(f"{source}: {plate.w} x {plate.h}, the plate master is {master_of(tab.name)}")
        _, _, _, _, template = tab_inputs(frames, tab)
        layer = multiply_alpha(gloss_layer(template, (plate.w, plate.h)), plate)
        plan.append((tab, source, plate, layer))
    ring = ring_profile(local)
    for tab, source, plate, layer in plan:
        idle = read_frame(frames, tab, 0)
        cx, cy, radius = disc_of(idle)
        kx, ky = plate.w / idle.w, plate.h / idle.h
        scale = (kx + ky) / 2.0
        final = paint_ring(over(plate, layer), (cx * kx, cy * ky), radius * scale, RING_UNITS * scale, ring)
        covered = sum(1 for i in range(3, len(layer.data), 4) if layer.data[i] > 0) / (plate.w * plate.h)
        print(f"plate.{tab.name:22s} highlight covers {covered * 100:4.1f} % of the canvas")
        if args.dry_run:
            continue
        out.mkdir(parents=True, exist_ok=True)
        target = out / f"plate.{tab.name}.png"
        pngio.write(target, final)
        record_path = local / "provenance" / "fit" / f"plate.{tab.name}.json"
        if args.out is None and record_path.is_file():
            record = json.loads(record_path.read_text(encoding="utf-8"))
            record.setdefault("fit_master", record.get("master"))
            record["master"] = {"file": str(target), "size": list(master_of(tab.name)), "sha256": button_kit.sha256_file(target)}
            record["gloss"] = {"rule": "school_kit gloss", "tool_version": TOOL_VERSION, "date": datetime.date.today().isoformat(), "fitted_master_sha256": button_kit.sha256_file(source),
                               "window_cells": GLOSS_WINDOW, "min_lighter_l": GLOSS_DL, "sigma_cells": GLOSS_SIGMA, "min_alpha": GLOSS_MIN_ALPHA, "ring": {"units": RING_UNITS, "source": RING_SOURCE, "bins": RING_BINS, "angles": RING_ANGLES, "gain": list(RING_GAIN)}, "sources": [f"{tab.folder}/f0", f"{tab.folder}/f1"]}
            record["halo_gate"] = {"relaxed": True, "issue": "#3073", "limits": record.get("limits"), "halo_limits": HALO_LIMITS, "note": "the gate fails the plate's real rim shading by design; fitted with --halo-limits, the repository's limits are unchanged"}
            record_path.write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
    print("SCHOOL KIT gloss " + ("DRY-RUN " if args.dry_run else "") + "PASS")
    return 0


def source_folders(ident: str) -> tuple[Tab, str]:
    for tab in TABS:
        if ident in (f"glyph.{tab.name}", f"plate.{tab.name}_hover", f"plate.{tab.name}_pressed"):
            return tab, ident
    raise SchoolError(f"{ident} belongs to no button of this slice")


def record_of(ident: str, frames: Path, image_path: Path, image: Image) -> dict:
    """The provenance record of a rule-made part (SWI-GEN-6): no generator, the rule and the hashes of the original frames it read."""
    tab, _ = source_folders(ident)
    return {"part": ident, "rule": "school_kit", "tool_version": TOOL_VERSION, "date": datetime.date.today().isoformat(),
            "sources": {f"{tab.folder}/f{k}": button_kit.sha256_file(frames / tab.folder / f"f{k}.png") for k in range(3)},
            "empty": not any(image.data[3::4]), "overrides": {k: (list(v) if isinstance(v, tuple) else v) for k, v in OVERRIDES.get(tab.name, {}).items()},
            "parameters": {"dome_share": DOME_SHARE, "reach_share": REACH_SHARE, "windows": [list(w) for w in WINDOWS], "percentile": PERCENTILE, "min_drop": MIN_DROP, "max_l": MAX_L, "max_l_read": MAX_L_READ, "read": "pressed (override read: idle)", "median": list(DEFAULT_MEDIAN), "median_percentile": MEDIAN_PERCENTILE, "grow_passes": GROW_PASSES, "max_gain": MAX_GAIN, "hole_max": HOLE_MAX, "speck": SPECK, "fill_de": FILL_DE, "fill_near": FILL_NEAR, "fill_window": FILL_WINDOW, "place_scale": PLACE_SCALE, "edge_pull": EDGE_PULL, "enlarge": ENLARGE, "rim_drop": RIM_DROP, "rim_share": RIM_SHARE,
                           "lift_min_alpha": LIFT_MIN_ALPHA, "lift_sigma": LIFT_SIGMA, "dead_de": button_kit.DEAD_DE, "solid_de": button_kit.SOLID_DE, "crisp_sigma": button_kit.CRISP_SIGMA, "edge_half": button_kit.EDGE_HALF, "master": [image.w, image.h]},
            "image_sha256": hashlib.sha256(image_path.read_bytes()).hexdigest(), "image_size": [image.w, image.h], "run_by": "agent"}


def cmd_build(args: argparse.Namespace) -> int:
    local = args.local_root
    frames = args.frames or local / "census" / "frames"
    plate_dir = args.plate_dir or local / "masters"
    out = args.out or local / "masters"
    records = local / "provenance" / "rule" if args.out is None else out / "records"
    only = set(args.only.split(",")) if args.only else None
    tabs = [t for t in tabs_of(args.group) if not only or t.name in only]
    if only and only - {t.name for t in TABS}:
        raise SchoolError(f"--only names buttons this slice does not hold: {sorted(only - {t.name for t in TABS})}")
    missing = [t.name for t in tabs if read_plate(plate_dir, t.name) is None]
    if missing:
        raise SchoolError(f"no plate master in {plate_dir} for {missing}: run fit and gloss first (the overlays are clipped by the plate)")
    parts = build_parts(frames, plate_dir, tabs)
    for ident, image in parts.items():
        covered = sum(1 for i in range(3, len(image.data), 4) if image.data[i] > 0) / (image.w * image.h)
        print(f"{ident:34s} {image.w} x {image.h}  covered {covered * 100:5.1f} %")
    if args.dry_run:
        print(f"SCHOOL KIT build DRY-RUN PASS ({len(parts)} parts); nothing written")
        return 0
    out.mkdir(parents=True, exist_ok=True)
    records.mkdir(parents=True, exist_ok=True)
    for ident, image in parts.items():
        path = out / f"{ident}.png"
        pngio.write(path, image)
        (records / f"{ident}.json").write_text(json.dumps(record_of(ident, frames, path, image), indent=2) + "\n", encoding="utf-8")
    print(f"SCHOOL KIT build PASS ({len(parts)} parts written to {out})")
    return 0


def upscaled(frame: Image, pixels: tuple[int, int]) -> Image:
    """An original frame at the master size: nearest cells (the 'before' row is the original's own pixels)."""
    pw, ph = pixels
    out = Image(pw, ph, bytearray(pw * ph * 4))
    for py in range(ph):
        cy = min(frame.h - 1, int((py + 0.5) * frame.h / ph))
        for px in range(pw):
            cx = min(frame.w - 1, int((px + 0.5) * frame.w / pw))
            out.data[(py * pw + px) * 4:(py * pw + px) * 4 + 4] = frame.data[(cy * frame.w + cx) * 4:(cy * frame.w + cx) * 4 + 4]
    return out


def cmd_sheet(args: argparse.Namespace) -> int:
    """The before / after composite: per button the original's idle, hover and pressed frames (nearest cells) over the kit's faces (plate + glyph + overlays), the same size; `--plate-dir` picks the plates (the glossed masters, or the fitted ones for the look without the highlight)."""
    local = args.local_root
    frames = args.frames or local / "census" / "frames"
    plate_dir = args.plate_dir or local / "masters"
    need_magick()
    only = set(args.only.split(",")) if args.only else None
    if only and only - {t.name for t in TABS}:
        raise SchoolError(f"--only names buttons this slice does not hold: {sorted(only - {t.name for t in TABS})}")
    tabs = [t for t in tabs_of(args.group) if not only or t.name in only]
    if not tabs:
        raise SchoolError("no button selected")
    work = Path(tempfile.mkdtemp(prefix="school-sheet-"))
    try:
        cell = max(max(master_of(t.name)) for t in tabs) * args.zoom
        rows = []
        for tab in tabs:
            idle, pressed, hover, mask, template = tab_inputs(frames, tab)
            plate = read_plate(plate_dir, tab.name)
            if plate is None:
                raise SchoolError(f"no plate master plate.{tab.name}.png in {plate_dir} (fit it first)")
            parts = tab_parts(frames, tab, plate_dir)
            face = over(plate, parts[f"glyph.{tab.name}"])
            shots = [(idle, face), (hover, over(face, parts[f"plate.{tab.name}_hover"])), (pressed, over(face, parts[f"plate.{tab.name}_pressed"]))]
            size = master_of(tab.name)
            if args.layout == "owner":
                cells = []
                for tag, image, nearest in (("o-idle", upscaled(idle, size), True), ("k-idle", shots[0][1], False), ("o-pressed", upscaled(pressed, size), True), ("k-pressed", shots[2][1], False)):
                    path = work / f"{tab.name}-{tag}.png"
                    pngio.write(path, image)
                    subprocess.run(["magick", str(path), "-background", "#202020", "-alpha", "remove", "-alpha", "off", "-filter", "point" if nearest else "Lanczos", "-resize", f"{size[0] * args.zoom}x{size[1] * args.zoom}!",
                                    "-gravity", "center", "-extent", f"{cell}x{cell}", str(path)], check=True)
                    cells.append(path)
                row = work / f"{tab.name}-row.png"
                subprocess.run(["magick", *cells, "+append", str(row)], check=True)
                rows.append(row)
                continue
            columns = []
            for index, (before, after) in enumerate(shots):
                pair = []
                for tag, image, nearest in (("before", upscaled(before, size), True), ("after", after, False)):
                    path = work / f"{tab.name}-{index}-{tag}.png"
                    pngio.write(path, image)
                    subprocess.run(["magick", str(path), "-background", "#202020", "-alpha", "remove", "-alpha", "off", "-filter", "point" if nearest else "Lanczos", "-resize", f"{size[0] * args.zoom}x{size[1] * args.zoom}!",
                                    "-gravity", "center", "-extent", f"{cell}x{cell}", str(path)], check=True)
                    pair.append(path)
                column = work / f"{tab.name}-{index}-col.png"
                subprocess.run(["magick", *pair, "-append", str(column)], check=True)
                columns.append(column)
            row = work / f"{tab.name}-row.png"
            subprocess.run(["magick", *columns, "+append", str(row)], check=True)
            rows.append(row)
        per = max(1, args.columns)
        grid = []
        for k in range(0, len(rows), per):
            line = work / f"line-{k}.png"
            subprocess.run(["magick", *rows[k:k + per], "+append", "-background", "#202020", "-extent", f"{cell * (4 if args.layout == 'owner' else 3) * per}x{cell * (1 if args.layout == 'owner' else 2)}", str(line)], check=True)
            grid.append(line)
        sheet = work / "sheet.png"
        subprocess.run(["magick", *grid, "-background", "#202020", "-append", str(sheet)], check=True)
        head = work / "head.png"
        caption = (f"{args.title}: columns per button: ORIGINAL idle | KIT idle | ORIGINAL pressed | KIT pressed (variant A)" if args.layout == "owner"
                   else f"{args.title}: top of each pair = ORIGINAL (nearest cells), bottom = KIT (plate + glyph + overlays); columns per button: idle, hover, pressed")
        subprocess.run(["magick", "-size", f"{cell * (4 if args.layout == 'owner' else 3) * per}x40", "xc:#202020", "-font", LABEL_FONT, "-fill", "white", "-pointsize", "22", "-gravity", "west", "-annotate", "+10+0", caption, str(head)], check=True)
        subprocess.run(["magick", str(head), str(sheet), "-append", str(args.out)], check=True)
    finally:
        shutil.rmtree(work, ignore_errors=True)
    print(f"SCHOOL KIT sheet PASS: {args.out}")
    return 0


def cmd_masks(args: argparse.Namespace) -> int:
    """The symbol masks over the pressed frames (yellow), the original pressed frame beside each: the review of `OVERRIDES`."""
    local = args.local_root
    frames = args.frames or local / "census" / "frames"
    need_magick()
    work = Path(tempfile.mkdtemp(prefix="school-masks-"))
    try:
        tiles = []
        for tab in tabs_of(args.group):
            idle, pressed = read_frame(frames, tab, 0), read_frame(frames, tab, 1)
            mask = symbol_mask(idle, pressed, override_of(tab))
            shown = Image(pressed.w, pressed.h, bytearray(pressed.data))
            for i, on in enumerate(mask):
                if on:
                    shown.data[i * 4:i * 4 + 3] = bytes((255, 255, 0))
            a, b, t = work / f"{tab.name}-a.png", work / f"{tab.name}-b.png", work / f"{tab.name}.png"
            pngio.write(a, pressed)
            pngio.write(b, shown)
            subprocess.run(["magick", str(a), str(b), "-filter", "point", "-resize", f"{args.zoom * 100}%", "+append", "-background", "#202020", "-extent", f"{2 * 36 * args.zoom}x{34 * args.zoom}", str(t)], check=True)
            tiles.append(t)
        lines = []
        for k in range(0, len(tiles), 3):
            line = work / f"line-{k}.png"
            subprocess.run(["magick", *tiles[k:k + 3], "+append", str(line)], check=True)
            lines.append(line)
        subprocess.run(["magick", *lines, "-background", "#202020", "-append", str(args.out)], check=True)
    finally:
        shutil.rmtree(work, ignore_errors=True)
    print(f"SCHOOL KIT masks PASS: {args.out}")
    return 0


def self_test() -> int:
    """The unit tests, run as the package module (they import this tool relatively, so not in this process when it runs as a script)."""
    code = subprocess.run([sys.executable, "-B", "-m", "unittest", "tools.steamwoven_ui.test_school_kit"], cwd=HERE.parents[1]).returncode
    print("SCHOOL KIT self-test " + ("PASS" if code == 0 else "FAIL"))
    return 0 if code == 0 else 1


def cmd_refs(args: argparse.Namespace) -> int:
    local = args.local_root
    frames = args.frames or local / "census" / "frames"
    refs = local / "refs"
    refs.mkdir(parents=True, exist_ok=True)
    swatch = refs / "plate-round-hud-sheet-1-brass-edge.png"  # the brass edge swatch of the approved sheet (written by the slice-2 refs)
    for group in ([args.group] if args.group else list(GROUPS)):
        prompt = GROUPS[group]
        pngio.write(refs / f"{prompt}-original.png", collage_reference(group, frames))
        (refs / f"{prompt}-layout.json").write_text(json.dumps({"group": group, "canvas": list(collage_size(group)), "scale": COLLAGE_SCALE[group], "ground": list(GROUND), "cells": collage_cells(group)}, indent=2) + "\n", encoding="utf-8")
        if not swatch.is_file():
            raise SchoolError(f"no brass edge swatch {swatch}: run round_kit.py refs first")
        shutil.copyfile(swatch, refs / f"{prompt}-sheet-1-brass-edge.png")
        for path in sorted(refs.glob(f"{prompt}-*.png")):
            print(f"{path.name}  {prompt_library.png_size(path)}  {button_kit.sha256_file(path)[:12]}")
    print("SCHOOL KIT refs PASS: build the inbox with prompt_library.py inbox B-BUTTONS")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("masks", "refs", "place", "fit", "gloss", "build", "sheet"):
        s = sub.add_parser(name)
        s.add_argument("--group", choices=list(GROUPS))
        s.add_argument("--frames", type=Path)
        s.add_argument("--local-root", type=Path, default=None)
        if name == "place":
            s.add_argument("--source", type=Path)
            s.add_argument("--out", type=Path)
        if name in ("fit", "gloss"):
            s.add_argument("--fitted-dir", type=Path)
        if name == "fit":
            s.add_argument("--only")
        if name == "gloss":
            s.add_argument("--out", type=Path)
            s.add_argument("--dry-run", action="store_true")
        if name == "build":
            s.add_argument("--out", type=Path)
            s.add_argument("--only")
            s.add_argument("--plate-dir", type=Path)
            s.add_argument("--dry-run", action="store_true")
        if name in ("sheet", "masks"):
            s.add_argument("--out", type=Path, required=True)
            s.add_argument("--zoom", type=int, default=3 if name == "masks" else 2)
        if name == "sheet":
            s.add_argument("--only")
            s.add_argument("--plate-dir", type=Path)
            s.add_argument("--columns", type=int, default=2)
            s.add_argument("--layout", choices=("states", "owner"), default="states", help="states: idle, hover, pressed with the original above the kit; owner: original idle | kit idle | original pressed | kit pressed in one row")
            s.add_argument("--title", default="ARC-UI-3204, coloured-dome buttons")
    sub.add_parser("self-test")
    args = parser.parse_args(argv)
    try:
        if args.command == "self-test":
            return self_test()
        args.local_root = args.local_root or prompt_library.default_local_root()
        return {"refs": cmd_refs, "masks": cmd_masks, "place": cmd_place, "fit": cmd_fit, "gloss": cmd_gloss, "build": cmd_build, "sheet": cmd_sheet}[args.command](args)
    except (SchoolError, button_kit.ButtonError, pngio.PngError, OSError, subprocess.CalledProcessError, KeyError) as error:
        print(f"SCHOOL KIT FAIL: {error}")
        return 2


if __name__ == "__main__":
    sys.exit(main())
