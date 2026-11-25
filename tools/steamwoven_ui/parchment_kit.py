#!/usr/bin/env python3
"""ARC-UI-3205 (batch B-BUTTONS, slice 5): parchment buttons as kit parts. Part one: the three OVAL BOXES of the inventory, loot and barter windows (Drop 224, Use 342, Gamble 345; 80 x 43 units, one frame each) and their
lit (ON 225, 343, 344) and refused (ILL 348, 347, 346) states: a brass-ringed oval with a parchment field and ONE painted symbol (the red arrow, the hand, the two dice).

    python3 -B tools/steamwoven_ui/parchment_kit.py masks  --out FILE [--zoom N] [--frames DIR] [--local-root DIR]      (the symbol masks over the idle frames, for a look; needs ImageMagick)
    python3 -B tools/steamwoven_ui/parchment_kit.py refs   [--frames DIR] [--local-root DIR]                           (the reference of the plate prompt: the original oval without its symbol; needs ImageMagick)
    python3 -B tools/steamwoven_ui/parchment_kit.py tone   [--source PNG] [--out PNG] [--frames DIR] [--local-root DIR]   (the generated plate's field takes the original's mean colour -> work/B-BUTTONS/, with the derived record)
    python3 -B tools/steamwoven_ui/parchment_kit.py build  [--look cells|smooth] [--out DIR] [--only NAME,...] [--dry-run] [--frames DIR] [--local-root DIR] [--plate-dir DIR]
    python3 -B tools/steamwoven_ui/parchment_kit.py sheet  --out FILE [--zoom N] [--plate-dir DIR] [--frames DIR] [--local-root DIR]     (before / after composite with English labels, the two looks side by side; needs ImageMagick)
    python3 -B tools/steamwoven_ui/parchment_kit.py self-test

The parts of a box (TRD SWI-KIT-5; the runtime's convention `SteamwovenKitComposer.ButtonLayers`: the face, over it the glyph; all parts of a button are at the face's size, 288 x 156 px):

* `plate.<name>` the empty oval: ONE Codex item (`plate-parchment-oval`: the brass ring, the dark inner bevel, the parchment field and the dark corners, no symbol), fitted by `fit.py` to the three plate parts (Drop, Use and Gamble stand on the very same oval).
* `glyph.<name>` the symbol as the idle frame shows it over the plate, a layer on the whole frame. The SHAPE is the original's mask (below). `--look cells`: the symbol's cells in the original's own colours inside a smooth sharp outline (rule E of
  slice 1, `button_kit.render_crisp`: the dice's dots, the hand's folds survive as the original draws them). `--look smooth`: the same smooth outline filled with the original's colours resampled by a small Gaussian (a painted look, no cells).
* `glyph.<on_name>` the lit symbol (the lit frame's cells in the same mask: the arrow turns a brighter red).
* `state.<on_name>` the LIT face: the plate where the original's lit frame lightens its inside (the shadowed bevel fills with parchment, the parchment brightens: a smooth gain field read from the two frames' templates, laid on the generated plate, so the plate's own texture shows through) and the four corners
  green. `state.<ill_name>` the REFUSED face: the plate with the four corners red. Nothing else of the lit and refused frames differs (the symbol of a refused frame is the idle one).

The symbol mask: the painted symbols differ from the parchment by little (the hand's skin on beige), so the mask is read in two passes against a smooth model of the parchment: a Gaussian-weighted local mean of the field's cells that are not yet symbol (four passes), then a model pooled from the other boxes' cells
(their symbols lie elsewhere), a colour distance above a threshold, small pieces dropped, a closing, the holes filled (a closed outline holds its body), an opening. A box's threshold, closing and an optional hull (a loose polygon round a symbol, when the parchment's own mottle would otherwise flood the mask) are
in `MASKS`. Every mask is looked at (`masks`). The template (the frame without its symbol) is the frame with the mask's cells replaced by the model.
Nothing here holds art: every picture is read from and written to the ignored `local/steamwoven-ui/`. Standard library only (`pngio.py`); ImageMagick for `masks`, `refs` and `sheet`.
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
from collections import Counter
from dataclasses import dataclass
from pathlib import Path

HERE = Path(__file__).resolve().parent
try:
    from . import button_kit, pngio, prompt_library
except ImportError:  # run as a script
    sys.path.insert(0, str(HERE))
    import button_kit  # type: ignore
    import pngio  # type: ignore
    import prompt_library  # type: ignore

TOOL_VERSION = 1
UNITS = (80, 43)                 # the frame of a box, in original units
MASTER = (288, 156)              # 80 x 3.6 = 288, 43 x 3.6 = 154.8 snapped to the 6 px grid (kit.json)
PAPER = (43.0, 22.0, 32.0, 17.0)  # the parchment field as an ellipse (centre x, centre y, semi-axes), in cells: where a symbol can lie
MODEL_SIGMA = 4.0                # the Gaussian of the parchment model, in cells
POOL_SIGMA = 3.0                 # the Gaussian of the model pooled over the boxes
MODEL_PASSES = 4
SPECK = 6                        # a piece of the first pass of this many cells or fewer is a speck, dropped
PIECE = 8                        # the same in the second pass
CORNER_ELLIPSE = (39.5, 21.0, 40.5, 22.0)  # the original ring's ellipse, in cells: the corners (the state's flat colour) lie outside it
CORNER_DE = 38.0                 # a cell this close to the state's flat corner colour is a corner cell
WEDGE_GROW = 2                   # cells the corner wedge is grown by (the frame's own noise leaves holes in a flat colour)
RING_OUTER = (0.493, 0.491)      # the generated ring's outer ellipse, as shares of the canvas: the corner layer lies outside it
CORNER_LIMIT = 0.97              # a cell beyond this share of RING_OUTER is a corner cell of the gain field (it keeps no lightening)
GAIN_SIGMA = 1.0                 # the Gaussian of the lit face's gain field, in cells
GAIN_MAX = 4.0                   # the gain field never lifts a colour by more than this (the shadowed bevel lifts by about 2.8)
GAIN_FLOOR = 8                   # a template channel below this reads as this in a ratio
EDGE_SIGMA = button_kit.CRISP_SIGMA
SMOOTH_SIGMA = 0.65              # `--look smooth`: the Gaussian of the colour field, in cells
TONE_RING = (0.47, 0.45)         # the ring's ellipse in the generated plate, as shares of the canvas (semi-axes from the middle)
TONE_FIELD = 0.55                # the field's mean is measured within this share of the ring's ellipse (the shaded bevel lies beyond)
TONE_FULL = 0.80                 # the gain is full up to this share, and fades out to TONE_END
TONE_END = 0.97
PROMPT = "plate-parchment-oval"
REFS_SIZE = (1680, 912)          # the original oval is shown to Codex at 21 x (the master's 24 : 13)
LOOKS = ("cells", "smooth", "solid")
ENGRAVE = 2                      # `--look solid`: the engraving's offset, in master px
LABEL_FONT = button_kit.LABEL_FONT

Image = pngio.Image


class ParchmentError(Exception):
    pass


@dataclass(frozen=True)
class Box:
    art: int
    folder: str
    name: str          # `plate.<name>`, `glyph.<name>`
    on_art: int
    on_folder: str
    on_name: str       # `state.<on_name>`, `glyph.<on_name>`
    ill_art: int
    ill_folder: str
    ill_name: str      # `state.<ill_name>`


BOXES = (
    Box(224, "224-Drop_OFF", "drop_off", 225, "225-Drop_ON", "drop_on", 348, "348-Drop_ILL", "drop_ill"),
    Box(342, "342-Use_Box_OFF", "use_box_off", 343, "343-Use_Box_ON", "use_box_on", 347, "347-Use_Box_ILL", "use_box_ill"),
    Box(345, "345-Gamble_Box_OFF", "gamble_box_off", 344, "344-Gamble_Box_ON", "gamble_box_on", 346, "346-Gamble_Box_ILL", "gamble_box_ill"),
)

# per box: the first pass (threshold in CIE76, closing radius), the second pass (the same), an optional hull (a loose polygon in cells round the symbol)
MASKS = {
    "drop_off": {"first": (11.0, 1), "second": (11.0, 1), "hull": None},
    "use_box_off": {"first": (8.0, 2), "second": (6.0, 2), "hull": ((31, 10), (36, 6), (47, 7), (51, 12), (54, 19), (56, 26), (62, 27), (64, 31), (56, 35), (48, 39), (34, 40), (28, 36), (28, 20))},
    "gamble_box_off": {"first": (9.0, 2), "second": (9.0, 2), "hull": None},
}


# --- frames and cells ------------------------------------------------------------------------------------------------------

def read_frame(frames: Path, folder: str, number: int = 0) -> Image:
    path = frames / folder / f"f{number}.png"
    if not path.is_file():
        raise ParchmentError(f"no frame {path}")
    image = pngio.read(path)
    if (image.w, image.h) != UNITS:
        raise ParchmentError(f"{path}: {image.w} x {image.h}, a frame of a box is {UNITS[0]} x {UNITS[1]}")
    return image


def cell(image: Image, x: int, y: int) -> tuple[int, int, int]:
    i = (y * image.w + x) * 4
    return image.data[i], image.data[i + 1], image.data[i + 2]


def in_paper(x: float, y: float) -> bool:
    cx, cy, ax, ay = PAPER
    return ((x - cx) / ax) ** 2 + ((y - cy) / ay) ** 2 < 1.0


def in_polygon(x: float, y: float, polygon) -> bool:
    inside = False
    for i, (x1, y1) in enumerate(polygon):
        x2, y2 = polygon[(i + 1) % len(polygon)]
        if (y1 > y) != (y2 > y) and x < (x2 - x1) * (y - y1) / (y2 - y1) + x1:
            inside = not inside
    return inside


# --- the symbol masks ------------------------------------------------------------------------------------------------------

Mask = list[list[bool]]


def blank(value=False) -> Mask:
    return [[value] * UNITS[0] for _ in range(UNITS[1])]


def local_model(frames: list[Image], masks: list[Mask] | None, sigma: float):
    """The parchment model: per cell the Gaussian-weighted mean colour of the paper cells of the frames that are not masked (None where nothing is in reach)."""
    w, h = UNITS
    reach = int(sigma * 2.5)
    cols = [[[cell(f, x, y) for x in range(w)] for y in range(h)] for f in frames]
    use = [[[in_paper(x, y) and not (masks and masks[k][y][x]) for x in range(w)] for y in range(h)] for k in range(len(frames))]
    out = [[None] * w for _ in range(h)]
    for y in range(h):
        for x in range(w):
            weight, total = 0.0, [0.0, 0.0, 0.0]
            for k in range(len(frames)):
                for yy in range(max(0, y - reach), min(h, y + reach + 1)):
                    for xx in range(max(0, x - reach), min(w, x + reach + 1)):
                        if use[k][yy][xx]:
                            g = math.exp(-((xx - x) ** 2 + (yy - y) ** 2) / (2 * sigma * sigma))
                            weight += g
                            c = cols[k][yy][xx]
                            total[0] += g * c[0]
                            total[1] += g * c[1]
                            total[2] += g * c[2]
            if weight > 1e-6:
                out[y][x] = tuple(v / weight for v in total)
    return out


def far_from(model, frame: Image, threshold: float, polygon=None) -> Mask:
    out = blank()
    for y in range(UNITS[1]):
        for x in range(UNITS[0]):
            if not in_paper(x, y) or (polygon is not None and not in_polygon(x + 0.5, y + 0.5, polygon)):
                continue
            m = model[y][x]
            out[y][x] = m is None or button_kit.distance(cell(frame, x, y), tuple(int(round(v)) for v in m)) > threshold
    return out


def dilate(mask: Mask, r: int) -> Mask:
    w, h = UNITS
    out = blank()
    for y in range(h):
        for x in range(w):
            if mask[y][x]:
                for yy in range(max(0, y - r), min(h, y + r + 1)):
                    for xx in range(max(0, x - r), min(w, x + r + 1)):
                        if (xx - x) ** 2 + (yy - y) ** 2 <= r * r + 1:
                            out[yy][xx] = True
    return out


def erode(mask: Mask, r: int) -> Mask:
    return [[not v for v in row] for row in dilate([[not v for v in row] for row in mask], r)]


def fill_holes(mask: Mask) -> Mask:
    w, h = UNITS
    seen = blank()
    stack = []
    for y in range(h):
        for x in range(w):
            if not in_paper(x, y) and not mask[y][x]:
                seen[y][x] = True
                stack.append((x, y))
    while stack:
        x, y = stack.pop()
        for xx, yy in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if 0 <= xx < w and 0 <= yy < h and not seen[yy][xx] and not mask[yy][xx]:
                seen[yy][xx] = True
                stack.append((xx, yy))
    return [[(mask[y][x] or not seen[y][x]) and in_paper(x, y) for x in range(w)] for y in range(h)]


def drop_small(mask: Mask, size: int) -> Mask:
    w, h = UNITS
    seen, out = blank(), blank()
    for y in range(h):
        for x in range(w):
            if mask[y][x] and not seen[y][x]:
                stack, piece = [(x, y)], []
                seen[y][x] = True
                while stack:
                    a, b = stack.pop()
                    piece.append((a, b))
                    for dx in (-1, 0, 1):
                        for dy in (-1, 0, 1):
                            xx, yy = a + dx, b + dy
                            if 0 <= xx < w and 0 <= yy < h and mask[yy][xx] and not seen[yy][xx]:
                                seen[yy][xx] = True
                                stack.append((xx, yy))
                if len(piece) >= size:
                    for a, b in piece:
                        out[b][a] = True
    return out


def close_fill(mask: Mask, size: int, radius: int, polygon=None) -> Mask:
    out = fill_holes(dilate(drop_small(mask, size), radius))
    out = erode(out, radius)
    if polygon is not None:
        out = [[out[y][x] and in_polygon(x + 0.5, y + 0.5, polygon) for x in range(UNITS[0])] for y in range(UNITS[1])]
    return out


def symbol_masks(idles: list[Image], boxes=BOXES) -> dict[str, Mask]:
    """The symbol mask of every box (by `name`), read from the idle frames in the two passes of the module's docstring."""
    first = []
    for frame, box in zip(idles, boxes):
        mask = blank()
        for _ in range(MODEL_PASSES):
            model = local_model([frame], [mask], MODEL_SIGMA)
            mask = far_from(model, frame, MASKS[box.name]["first"][0])
        first.append(close_fill(mask, SPECK, MASKS[box.name]["first"][1]))
    grown = [dilate(m, 2) for m in first]
    model = local_model(idles, grown, POOL_SIGMA)
    out = {}
    for frame, box in zip(idles, boxes):
        threshold, radius = MASKS[box.name]["second"]
        hull = MASKS[box.name]["hull"]
        out[box.name] = close_fill(far_from(model, frame, threshold, hull), PIECE, radius, hull)
    return out


def flat(mask: Mask) -> list[bool]:
    return [v for row in mask for v in row]


def template_of(frame: Image, mask: Mask) -> Image:
    """The frame without its symbol: the mask's cells (grown by a cell) take the parchment model of this frame alone."""
    model = local_model([frame], [dilate(mask, 1)], MODEL_SIGMA)
    out = Image(frame.w, frame.h, bytearray(frame.data))
    for y, row in enumerate(dilate(mask, 1)):
        for x, v in enumerate(row):
            if v and model[y][x] is not None:
                i = (y * frame.w + x) * 4
                out.data[i:i + 3] = bytes(int(round(c)) for c in model[y][x])
    return out


# --- layers ----------------------------------------------------------------------------------------------------------------

def render_smooth(frame: Image, mask: Mask, pixels: tuple[int, int] = MASTER) -> Image:
    """`--look smooth`: the mask's smooth sharp silhouette filled with the frame's own colours resampled by a small Gaussian (alpha-weighted, so the colour does not bleed in from outside the symbol)."""
    w, h = UNITS
    pw, ph = pixels
    solid = [1.0 if v else 0.0 for v in flat(mask)]
    silhouette = button_kit.resample_plane(solid, (w, h), (pw, ph), EDGE_SIGMA)
    weight = button_kit.resample_plane(solid, (w, h), (pw, ph), SMOOTH_SIGMA)
    cells = button_kit.cells_of(frame)
    channels = [button_kit.resample_plane([c[k] * s for c, s in zip(cells, solid)], (w, h), (pw, ph), SMOOTH_SIGMA) for k in range(3)]
    out = Image(pw, ph, bytearray(pw * ph * 4))
    for i in range(pw * ph):
        alpha = button_kit.smoothstep(0.5 - button_kit.EDGE_HALF, 0.5 + button_kit.EDGE_HALF, silhouette[i])
        if alpha <= 0.002 or weight[i] <= 1e-6:
            continue
        o = i * 4
        out.data[o:o + 3] = bytes(max(0, min(255, int(round(channels[k][i] / weight[i])))) for k in range(3))
        out.data[o + 3] = int(round(alpha * 255))
    return out


def render_solid(frame: Image, mask: Mask, pixels: tuple[int, int] = MASTER) -> Image:
    """`--look solid` (the owner's rule for round-button symbols, #3204, tried on the one flat symbol here): the mask's smooth sharp silhouette, ONE solid colour (the median of the mask's cells in the frame), a light engraving:
    a light edge at the upper left, a shadow at the lower right (the silhouette shifted by `ENGRAVE` px)."""
    w, h = UNITS
    pw, ph = pixels
    sil = button_kit.resample_plane([1.0 if v else 0.0 for v in flat(mask)], (w, h), (pw, ph), EDGE_SIGMA)
    core = erode(mask, 1)
    cells = [cell(frame, x, y) for y, row in enumerate(core) for x, v in enumerate(row) if v] or [cell(frame, x, y) for y, row in enumerate(mask) for x, v in enumerate(row) if v]
    base = tuple(sorted(c[k] for c in cells)[len(cells) // 2] for k in range(3))
    out = Image(pw, ph, bytearray(pw * ph * 4))
    def at(x: int, y: int) -> float:
        return sil[min(ph - 1, max(0, y)) * pw + min(pw - 1, max(0, x))]
    for y in range(ph):
        for x in range(pw):
            alpha = button_kit.smoothstep(0.5 - button_kit.EDGE_HALF, 0.5 + button_kit.EDGE_HALF, sil[y * pw + x])
            if alpha <= 0.002:
                continue
            light = 1.0 - button_kit.smoothstep(0.35, 0.65, at(x - ENGRAVE, y - ENGRAVE))   # the upper-left neighbour lies outside the symbol: a light edge
            shade = 1.0 - button_kit.smoothstep(0.35, 0.65, at(x + ENGRAVE, y + ENGRAVE))   # the lower-right neighbour lies outside: a shadow
            o = (y * pw + x) * 4
            out.data[o:o + 3] = bytes(max(0, min(255, int(round(c + (255 - c) * 0.45 * light - c * 0.45 * shade)))) for c in base)
            out.data[o + 3] = int(round(alpha * 255))
    return out


def glyph_layer(frame: Image, template: Image, mask: Mask, look: str) -> Image:
    if look == "cells":
        return button_kit.render_crisp(frame, template, glow=False, rim=True, pixels=MASTER, silhouette_mask=flat(mask), owned_cell=lambda x, y: in_paper(x, y))
    if look == "smooth":
        return render_smooth(frame, mask)
    if look == "solid":
        return render_solid(frame, mask)
    raise ParchmentError(f"look must be one of {LOOKS}")


def corner_cells(state: Image, colour: tuple[int, int, int]) -> Mask:
    """The corner cells of a state frame: the cells outside the original ring's ellipse that are the state's flat colour, grown by `WEDGE_GROW` cells (the wedge's extent along the wood; the ring side is cut by the generated ring's own ellipse in `corner_layer`)."""
    cx, cy, ax, ay = CORNER_ELLIPSE
    out = blank()
    for y in range(UNITS[1]):
        for x in range(UNITS[0]):
            out[y][x] = ((x - cx) / ax) ** 2 + ((y - cy) / ay) ** 2 > 0.97 and button_kit.distance(cell(state, x, y), colour) <= CORNER_DE
    return dilate(out, WEDGE_GROW)


def outside_ring(x: int, y: int) -> bool:
    """Whether the cell of the original frame lies outside the ring's ellipse (the cells that keep no lightening)."""
    cx, cy, ax, ay = CORNER_ELLIPSE
    return ((x - cx) / ax) ** 2 + ((y - cy) / ay) ** 2 > CORNER_LIMIT


def state_colour(state: Image) -> tuple[int, int, int]:
    """The flat colour of a state's corners: the most common colour of the cells outside the ring's ellipse."""
    cx, cy, ax, ay = CORNER_ELLIPSE
    counts = Counter(cell(state, x, y) for y in range(UNITS[1]) for x in range(UNITS[0]) if ((x - cx) / ax) ** 2 + ((y - cy) / ay) ** 2 > 0.97)
    return counts.most_common(1)[0][0]


def corner_layer(mask: Mask, colour: tuple[int, int, int]) -> Image:
    """The corners as a layer: the state's flat colour inside the wedge the original's corner cells span (a smooth sharp edge) and outside the generated ring's outer ellipse (a smooth edge one master pixel wide along the ring, which stays the plate's)."""
    w, h = UNITS
    pw, ph = MASTER
    wedge = button_kit.resample_plane([1.0 if v else 0.0 for v in flat(mask)], (w, h), (pw, ph), EDGE_SIGMA)
    out = Image(pw, ph, bytearray(pw * ph * 4))
    for y in range(ph):
        for x in range(pw):
            r = math.hypot(((x + 0.5) / pw - 0.5) / RING_OUTER[0], ((y + 0.5) / ph - 0.5) / RING_OUTER[1])
            alpha = button_kit.smoothstep(1.0, 1.0 + 1.5 / min(pw, ph), r) * button_kit.smoothstep(0.5 - button_kit.EDGE_HALF, 0.5 + button_kit.EDGE_HALF, wedge[y * pw + x])
            if alpha > 0.002:
                o = (y * pw + x) * 4
                out.data[o:o + 3] = bytes(colour)
                out.data[o + 3] = int(round(alpha * 255))
    return out


def gain_field(idle_template: Image, lit_template: Image, keep: Mask):
    """The lit frame's lightening as a per-channel gain, cell by cell (lit template over idle template, clamped), smoothed and resampled to the master: three planes. `keep` says the cells that take it (the corners do not)."""
    w, h = UNITS
    planes = []
    for k in range(3):
        plane = []
        for y in range(h):
            for x in range(w):
                if keep[y][x]:
                    a, b = cell(idle_template, x, y)[k], cell(lit_template, x, y)[k]
                    plane.append(max(1.0 / GAIN_MAX, min(GAIN_MAX, max(b, GAIN_FLOOR) / max(a, GAIN_FLOOR))))
                else:
                    plane.append(1.0)
        planes.append(button_kit.resample_plane(plane, (w, h), MASTER, GAIN_SIGMA))
    return planes


def lit_face(plate: Image, gains, corners: Image) -> Image:
    """The plate lightened by the gain field, the corners laid over it."""
    out = Image(plate.w, plate.h, bytearray(plate.data))
    for i in range(plate.w * plate.h):
        o = i * 4
        if plate.data[o + 3] == 0:
            continue
        for k in range(3):
            out.data[o + k] = max(0, min(255, int(round(plate.data[o + k] * gains[k][i]))))
    return button_kit.composite(out, corners)


def refused_face(plate: Image, corners: Image) -> Image:
    return button_kit.composite(plate, corners)


# --- the parts of the slice ------------------------------------------------------------------------------------------------

def build_parts(frames: Path, look: str, plate_dir: Path | None, only: set[str] | None = None) -> dict[str, Image]:
    """Every part of the slice by id: the symbols (idle and lit) and, when the plate masters are in `plate_dir`, the lit and the refused faces."""
    idles = [read_frame(frames, b.folder) for b in BOXES]
    masks = symbol_masks(idles)
    parts: dict[str, Image] = {}
    for box, idle in zip(BOXES, idles):
        if only and box.name not in only:
            continue
        mask = masks[box.name]
        lit = read_frame(frames, box.on_folder)
        ill = read_frame(frames, box.ill_folder)
        idle_template, lit_template = template_of(idle, mask), template_of(lit, mask)
        parts[f"glyph.{box.name}"] = glyph_layer(idle, idle_template, mask, look)
        parts[f"glyph.{box.on_name}"] = glyph_layer(lit, lit_template, mask, look)
        plate_path = plate_dir / f"plate.{box.name}.png" if plate_dir else None
        if plate_path is not None and plate_path.is_file():
            plate = pngio.read(plate_path)
            if (plate.w, plate.h) != MASTER:
                raise ParchmentError(f"{plate_path}: {plate.w} x {plate.h}, the plate master is {MASTER[0]} x {MASTER[1]}")
            green, red = corner_cells(lit, state_colour(lit)), corner_cells(ill, state_colour(ill))
            keep = [[not outside_ring(x, y) for x in range(UNITS[0])] for y in range(UNITS[1])]
            parts[f"state.{box.on_name}"] = lit_face(plate, gain_field(idle_template, lit_template, keep), corner_layer(green, state_colour(lit)))
            parts[f"state.{box.ill_name}"] = refused_face(plate, corner_layer(red, state_colour(ill)))
    return parts


def sha256_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def owner_of(ident: str) -> Box:
    for box in BOXES:
        if ident.split(".", 1)[1] in (box.name, box.on_name, box.ill_name):
            return box
    raise ParchmentError(f"{ident} belongs to no box of this slice")


def record_of(ident: str, look: str, frames: Path, image_path: Path, box: Box, plate_path: Path | None = None) -> dict:
    """The provenance record of a rule-made part (SWI-GEN-6): no generator, the rule and the hashes of the original frames it read."""
    folders = [box.folder, box.on_folder, box.ill_folder]
    extra = {"plate_master_sha256": sha256_file(plate_path)} if plate_path is not None else {}
    return {"part": ident, "rule": "parchment_kit", "tool_version": TOOL_VERSION, "look": look, "date": datetime.date.today().isoformat(), **extra,
            "template_sources": {f"{b.folder}/f0": sha256_file(frames / b.folder / "f0.png") for b in BOXES},  # the masks are pooled over the three idle frames
            "sources": {f"{folder}/f0": sha256_file(frames / folder / "f0.png") for folder in folders},
            "parameters": {"paper": list(PAPER), "model_sigma": MODEL_SIGMA, "pool_sigma": POOL_SIGMA, "gain_sigma": GAIN_SIGMA, "gain_max": GAIN_MAX, "ring_outer": list(RING_OUTER), "corner_de": CORNER_DE, "wedge_grow": WEDGE_GROW, "smooth_sigma": SMOOTH_SIGMA, "master": list(MASTER)},
            "image_sha256": hashlib.sha256(image_path.read_bytes()).hexdigest(), "image_size": list(MASTER), "run_by": "agent"}


# --- commands --------------------------------------------------------------------------------------------------------------

def need_magick() -> None:
    if shutil.which("magick") is None:
        raise ParchmentError("ImageMagick (`magick`) is needed for this command")


def zoomed(src: Path, dst: Path, zoom: int) -> None:
    subprocess.run(["magick", str(src), "-filter", "point", "-resize", f"{zoom * 100}%", str(dst)], check=True)


def cmd_masks(args: argparse.Namespace) -> int:
    need_magick()
    frames = args.frames or args.local_root / "census" / "frames"
    idles = [read_frame(frames, b.folder) for b in BOXES]
    masks = symbol_masks(idles)
    with tempfile.TemporaryDirectory() as tmp:
        tiles = []
        for box, idle in zip(BOXES, idles):
            over = Image(idle.w, idle.h, bytearray(idle.data))
            for y, row in enumerate(masks[box.name]):
                for x, v in enumerate(row):
                    if v:
                        i = (y * idle.w + x) * 4
                        over.data[i:i + 3] = bytes(int(0.5 * a + 0.5 * b) for a, b in zip(cell(idle, x, y), (255, 0, 255)))
            raw, big = Path(tmp) / f"{box.name}.png", Path(tmp) / f"{box.name}-z.png"
            pngio.write(raw, over)
            zoomed(raw, big, args.zoom)
            tiles.append(str(big))
            print(f"{box.name:16s} {sum(sum(r) for r in masks[box.name])} cells")
        args.out.parent.mkdir(parents=True, exist_ok=True)
        subprocess.run(["magick", "montage", *tiles, "-tile", "1x", "-geometry", "+3+3", "-background", "#202020", str(args.out)], check=True)
    print(f"PARCHMENT KIT masks written to {args.out}")
    return 0


def cmd_refs(args: argparse.Namespace) -> int:
    need_magick()
    local = args.local_root
    frames = args.frames or local / "census" / "frames"
    refs = local / "refs"
    refs.mkdir(parents=True, exist_ok=True)
    idles = [read_frame(frames, b.folder) for b in BOXES]
    masks = symbol_masks(idles)
    template = template_of(idles[0], masks[BOXES[0].name])   # the template of the Drop box with the other symbols' places also cleared
    for box, idle in zip(BOXES[1:], idles[1:]):
        other = template_of(idle, masks[box.name])
        for y, row in enumerate(dilate(masks[BOXES[0].name], 1)):
            for x, v in enumerate(row):
                if not v:
                    continue
                i = (y * template.w + x) * 4
                template.data[i:i + 3] = bytes(other.data[i:i + 3])
    with tempfile.TemporaryDirectory() as tmp:
        raw = Path(tmp) / "template.png"
        pngio.write(raw, template)
        subprocess.run(["magick", str(raw), "-filter", "point", "-resize", f"{REFS_SIZE[0]}x{REFS_SIZE[1]}!", str(refs / f"{PROMPT}-original.png")], check=True)
    sheets = local / "material-sheet" / "cutouts"
    for number, name in enumerate(("brass-edge", "parchment"), 1):
        if not (sheets / f"{name}.png").is_file():
            raise ParchmentError(f"missing {sheets / name}.png")
        shutil.copyfile(sheets / f"{name}.png", refs / f"{PROMPT}-sheet-{number}-{name}.png")
    for path in sorted(refs.glob(f"{PROMPT}-*.png")):
        print(f"{path.name}  {prompt_library.png_size(path)}  {sha256_file(path)[:12]}")
    print("PARCHMENT KIT refs PASS: build the inbox with prompt_library.py inbox B-BUTTONS")
    return 0


def inside_share(u: float, v: float) -> float:
    """How far (u, v), a point of the frame in 0..1 each way, lies from the middle in units of the ring's ellipse (1.0 = on the ring)."""
    return math.hypot((u - 0.5) / TONE_RING[0], (v - 0.5) / TONE_RING[1])


def field_mean(image: Image, sample) -> tuple[float, float, float]:
    """The mean colour of the pixels `sample(u, v)` selects."""
    total, count = [0.0, 0.0, 0.0], 0
    for y in range(image.h):
        for x in range(image.w):
            if sample((x + 0.5) / image.w, (y + 0.5) / image.h):
                c = image.data[(y * image.w + x) * 4:(y * image.w + x) * 4 + 3]
                total[0] += c[0]
                total[1] += c[1]
                total[2] += c[2]
                count += 1
    return tuple(v / count for v in total)


def tone_gain(plate: Image, original_template: Image) -> tuple[float, float, float]:
    """The per-channel gain that brings the generated plate's field to the original's (the mean colour of the cells within `TONE_FIELD` of the ring's ellipse, both measured the same way)."""
    inner = lambda u, v: inside_share(u, v) < TONE_FIELD
    want = field_mean(original_template, inner)
    have = field_mean(plate, inner)
    return tuple(w / h for w, h in zip(want, have))


def apply_tone(plate: Image, gain: tuple[float, float, float]) -> Image:
    """The gain inside the ring (full up to `TONE_FULL` of the ellipse, fading out to the ring), the ring and the corners as they are."""
    out = Image(plate.w, plate.h, bytearray(plate.data))
    for y in range(plate.h):
        for x in range(plate.w):
            weight = 1.0 - button_kit.smoothstep(TONE_FULL, TONE_END, inside_share((x + 0.5) / plate.w, (y + 0.5) / plate.h))
            if weight <= 0.0:
                continue
            i = (y * plate.w + x) * 4
            for k in range(3):
                out.data[i + k] = max(0, min(255, int(round(plate.data[i + k] * (1.0 + (gain[k] - 1.0) * weight)))))
    return out


def cmd_tone(args: argparse.Namespace) -> int:
    """The generated plate's field takes the original's mean colour (the generator draws a yellower, darker parchment than the original's grey beige): a gain inside the ring -> `work/B-BUTTONS/plate-parchment-oval-toned.png` with the derived provenance record; `fit.py` takes that file."""
    local = args.local_root
    frames = args.frames or local / "census" / "frames"
    source = args.source or local / "outbox" / "B-BUTTONS" / f"{PROMPT}.png"
    out = args.out or local / "work" / "B-BUTTONS" / f"{PROMPT}-toned.png"
    generator = source.with_name(source.stem + ".provenance.json")
    if not generator.is_file():
        raise ParchmentError(f"no generator record {generator} (SWI-GEN-6)")
    record = json.loads(generator.read_text(encoding="utf-8"))
    if record.get("image_sha256") != sha256_file(source):
        raise ParchmentError(f"{source.name} differs from the image its record {generator.name} describes")
    idles = [read_frame(frames, b.folder) for b in BOXES]
    masks = symbol_masks(idles)
    templates = [template_of(i, masks[b.name]) for i, b in zip(idles, BOXES)]
    # the three templates' means, averaged (each is the same parchment with a symbol painted over: the symbol's place is replaced by the model)
    wants = [field_mean(t, lambda u, v: inside_share(u, v) < TONE_FIELD) for t in templates]
    want = tuple(sum(w[k] for w in wants) / len(wants) for k in range(3))
    plate = pngio.read(source)
    have = field_mean(plate, lambda u, v: inside_share(u, v) < TONE_FIELD)
    gain = tuple(w / h for w, h in zip(want, have))
    toned = apply_tone(plate, gain)
    out.parent.mkdir(parents=True, exist_ok=True)
    pngio.write(out, toned)
    derived = out.with_name(out.stem + ".provenance.json")
    record["derived_from"] = {"image": record.get("image"), "image_sha256": record["image_sha256"], "pixel_size": record.get("pixel_size"),
                              "step": f"parchment_kit.py tone (per-channel gain {', '.join(f'{g:.3f}' for g in gain)} inside the ring: the field's mean colour taken from the original's three idle frames)"}
    record["image"], record["image_sha256"] = str(out.relative_to(local)) if out.is_relative_to(local) else out.name, sha256_file(out)
    record["pixel_size"] = [toned.w, toned.h]
    derived.write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
    print(f"gain {gain[0]:.3f} {gain[1]:.3f} {gain[2]:.3f}  ->  {out.name}  {sha256_file(out)[:12]}  (record {derived.name})")
    print("PARCHMENT KIT tone PASS")
    return 0


def cmd_build(args: argparse.Namespace) -> int:
    local = args.local_root
    frames = args.frames or local / "census" / "frames"
    plate_dir = args.plate_dir or local / "masters"
    out = args.out or local / "masters"
    records = local / "provenance" / "rule" if args.out is None else out / "records"
    only = set(args.only.split(",")) if args.only else None
    if only and not only <= {b.name for b in BOXES}:
        raise ParchmentError(f"--only names unknown boxes {sorted(only - {b.name for b in BOXES})}; the boxes are {[b.name for b in BOXES]}")
    parts = build_parts(frames, args.look, plate_dir, only)
    for ident, image in parts.items():
        covered = sum(1 for i in range(3, len(image.data), 4) if image.data[i] > 0) / (image.w * image.h)
        print(f"{ident:24s} {image.w} x {image.h}  covered {covered * 100:5.1f} %")
    faces = [f"state.{n}" for b in BOXES if not only or b.name in only for n in (b.on_name, b.ill_name)]
    skipped = [f for f in faces if f not in parts]
    if skipped:
        print(f"skipped (no plate master in {plate_dir}; run fit.py first): {', '.join(skipped)}")
    if args.dry_run:
        print(f"PARCHMENT KIT build DRY-RUN PASS ({len(parts)} parts, look {args.look}); nothing written")
        return 0
    out.mkdir(parents=True, exist_ok=True)
    records.mkdir(parents=True, exist_ok=True)
    for ident, image in parts.items():
        path = out / f"{ident}.png"
        pngio.write(path, image)
        box = owner_of(ident)
        plate = plate_dir / f"plate.{box.name}.png" if ident.startswith("state.") else None
        (records / f"{ident}.json").write_text(json.dumps(record_of(ident, args.look, frames, path, box, plate), indent=2) + "\n", encoding="utf-8")
    print(f"PARCHMENT KIT build PASS ({len(parts)} parts written to {out}, look {args.look})")
    return 0


def upscale(frame: Image) -> Image:
    """An original frame at the master size, opaque, by the same Gaussian a layer uses (the 'original' column of a comparison)."""
    cells = button_kit.cells_of(frame)
    w, h = UNITS
    channels = [button_kit.resample_plane([float(c[k]) for c in cells], (w, h), MASTER, 0.45) for k in range(3)]
    out = Image(MASTER[0], MASTER[1], bytearray(MASTER[0] * MASTER[1] * 4))
    for i in range(MASTER[0] * MASTER[1]):
        out.data[i * 4:i * 4 + 4] = bytes([max(0, min(255, int(round(channels[k][i])))) for k in range(3)] + [255])
    return out


def labelled(src: Path, dst: Path, label: str, zoom: int) -> None:
    subprocess.run(["magick", str(src), "-filter", "point", "-resize", f"{zoom * 100}%", "-background", "#202020", "-fill", "white", "-font", LABEL_FONT, "-pointsize", "18",
                    "-gravity", "North", "-splice", "0x28", "-annotate", "+0+4", label, str(dst)], check=True)


def cmd_sheet(args: argparse.Namespace) -> int:
    """The before / after composite: per box the original idle | kit cells | kit smooth | original lit | kit lit | original refused | kit refused (English labels)."""
    need_magick()
    local = args.local_root
    frames = args.frames or local / "census" / "frames"
    plate_dir = args.plate_dir or local / "masters"
    for box in BOXES:
        if not (plate_dir / f"plate.{box.name}.png").is_file():
            raise ParchmentError(f"no plate master {plate_dir / f'plate.{box.name}.png'}: fit the plate first")
    rows = []
    with tempfile.TemporaryDirectory() as tmp:
        work = Path(tmp)
        built = {look: build_parts(frames, look, plate_dir) for look in LOOKS}
        for box in BOXES:
            plate = pngio.read(plate_dir / f"plate.{box.name}.png")
            cols = [("original idle", upscale(read_frame(frames, box.folder))),
                    ("kit cells", button_kit.composite(plate, built["cells"][f"glyph.{box.name}"])),
                    ("kit smooth", button_kit.composite(plate, built["smooth"][f"glyph.{box.name}"])),
                    ("original lit", upscale(read_frame(frames, box.on_folder))),
                    ("kit lit (cells)", button_kit.composite(built["cells"][f"state.{box.on_name}"], built["cells"][f"glyph.{box.on_name}"])),
                    ("kit lit (smooth)", button_kit.composite(built["smooth"][f"state.{box.on_name}"], built["smooth"][f"glyph.{box.on_name}"])),
                    ("original refused", upscale(read_frame(frames, box.ill_folder))),
                    ("kit refused (cells)", button_kit.composite(built["cells"][f"state.{box.ill_name}"], built["cells"][f"glyph.{box.name}"]))]
            if box.name == "drop_off":  # the flat arrow also in the owner's round-button rule (solid fill, engraving)
                cols.insert(3, ("kit solid", button_kit.composite(plate, built["solid"][f"glyph.{box.name}"])))
            tiles = []
            for n, (label, image) in enumerate(cols):
                raw, tile = work / f"{box.name}-{n}.png", work / f"{box.name}-{n}-l.png"
                pngio.write(raw, image)
                labelled(raw, tile, label, args.zoom)
                tiles.append(str(tile))
            row = work / f"{box.name}-row.png"
            subprocess.run(["magick", "montage", *tiles, "-tile", "5x", "-geometry", "+4+4", "-background", "#202020", str(row)], check=True)
            rows.append(str(row))
        args.out.parent.mkdir(parents=True, exist_ok=True)
        subprocess.run(["magick", "montage", *rows, "-tile", "1x", "-geometry", "+0+10", "-background", "#202020", str(args.out)], check=True)
    print(f"PARCHMENT KIT sheet written to {args.out}")
    return 0


def self_test() -> int:
    """Checks that need no original art: the geometry helpers on synthetic masks and the gain field on synthetic templates."""
    assert in_paper(43, 22) and not in_paper(2, 2)
    assert in_polygon(1.5, 1.5, ((0, 0), (4, 0), (4, 4), (0, 4))) and not in_polygon(5, 1, ((0, 0), (4, 0), (4, 4), (0, 4)))
    mask = blank()
    for y in range(18, 26):
        for x in range(38, 48):
            mask[y][x] = True
    ring = [row[:] for row in mask]
    for y in range(20, 24):
        for x in range(40, 46):
            ring[y][x] = False
    assert fill_holes(ring)[21][42], "a closed outline holds its body"
    assert not any(drop_small(blank(), 3)[0])
    flat_a = Image(UNITS[0], UNITS[1], bytearray([100, 100, 100, 255] * (UNITS[0] * UNITS[1])))
    flat_b = Image(UNITS[0], UNITS[1], bytearray([200, 150, 100, 255] * (UNITS[0] * UNITS[1])))
    planes = gain_field(flat_a, flat_b, blank(True))
    assert abs(planes[0][5000] - 2.0) < 0.01 and abs(planes[2][5000] - 1.0) < 0.01
    print("PARCHMENT KIT self-test PASS")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("masks", "refs", "tone", "build", "sheet"):
        s = sub.add_parser(name)
        s.add_argument("--frames", type=Path, default=None)
        s.add_argument("--local-root", type=Path, default=None)
        if name in ("masks", "sheet"):
            s.add_argument("--out", type=Path, required=True)
            s.add_argument("--zoom", type=int, default=4 if name == "masks" else 2)
        if name == "tone":
            s.add_argument("--source", type=Path, default=None)
            s.add_argument("--out", type=Path, default=None)
        if name in ("build", "sheet"):
            s.add_argument("--plate-dir", type=Path, default=None)
        if name == "build":
            s.add_argument("--look", choices=LOOKS, default="cells")
            s.add_argument("--out", type=Path, default=None)
            s.add_argument("--only", default=None)
            s.add_argument("--dry-run", action="store_true")
    sub.add_parser("self-test")
    args = parser.parse_args(argv)
    try:
        if args.command == "self-test":
            return self_test()
        args.local_root = args.local_root or prompt_library.default_local_root()
        return {"masks": cmd_masks, "refs": cmd_refs, "tone": cmd_tone, "build": cmd_build, "sheet": cmd_sheet}[args.command](args)
    except (ParchmentError, button_kit.ButtonError, OSError, subprocess.CalledProcessError) as error:
        print(f"PARCHMENT KIT FAIL: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
