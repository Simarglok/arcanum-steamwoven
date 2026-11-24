"""ARC-UI-3058 (round 3, owner decision 2026-11-21): a button glyph drawn by a RULE from the ORIGINAL's own frame, no Codex.

The ORIGINAL's glyph is a checker one original pixel wide (the cell is the pixel; weak in the idle frame, strong in the hover and pressed frames), a part of the
picture and not a dither to smooth away. The kit's glyph keeps that and redraws what the 39 x 39 grid cannot hold:

* **silhouette** from the ORIGINAL's glyph mask of the same frame (its position is the original's coordinates, not a frame's centre): the cells that stand out from the
  plate (Otsu split of the lightness inside the dome), the dark cells of the checker inside it filled, small specks dropped; the 39 x 39 mask is turned into a smooth outline at 4 x
  (a Gaussian of the mask, then a soft threshold), so an edge is no stair of original pixels;
* **cells**: every cell of the 39 x 39 grid is one of the two checker tones by the original's own parity (the phase read from the frame); a tone is the local colour of that kind
  of cell in the ORIGINAL (a window of 3 x 3 cells, grown when it holds none), so the contrast of the cells in each state (idle, hover, pressed) is the original's and the glyph keeps
  its shading (the shield's brass bar, the key's hole, the hover glow);
* **two looks**: `flat` (crisp square cells, a soft-edged silhouette; the pilot's look) and `bevel` (the same with a thin bevel, dropped by the owner in round four: a light edge on the upper left, a dark one on the lower right, as the
  kit's light falls);
* **tone** per state against the plate by numbers: the glyph is then shifted as one (the L*a*b* mean over its cells) so that its brightness over the kit plate's dome equals the ORIGINAL's
  over its own dome (`glyph_tone.py`, `--stat mean` because the cells are drawn now).

    python3 -B tools/steamwoven_ui/glyph_cells.py mask FRAME.png [--json]
    python3 -B tools/steamwoven_ui/glyph_cells.py cells FRAME.png OUT.png [--look flat|bevel] [--scale 4] [--mask-frame FRAME.png[:RADIUS]]... [--round-holes] [--plate PLATE_CANVAS.png --dome-frame IDLE_FRAME.png]
    python3 -B tools/steamwoven_ui/glyph_cells.py plate PLATE_MASTER.png OUT.png [--scale 4] [--plate-units 38.6] [--shadow 0] [--edge-dark 0.4]
    python3 -B tools/steamwoven_ui/glyph_cells.py pressed IDLE_PLATE_CANVAS.png OUT.png [--scale 4] [--plate-units 38.6]

`mask` prints the glyph's mask (rows of # and .) and its centroid in units; `cells` writes the glyph canvas, the whole 39-unit frame at `--scale` px per unit (alpha is the
silhouette, nothing else is drawn), so a layout places it at 0,0 and the glyph lies where the ORIGINAL's does; `plate` writes the kit plate master centred on a 39-unit canvas
at the same scale (38.6 units across by default, filling the socket, its last unit a little darker; an optional soft shadow in the alpha with `--shadow`); `pressed` writes the plate's pressed look from its idle canvas. Standard library only; exit 0, 2 = an input could not be used.
"""
from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path

try:
    from . import assemble, glyph_tone, pngio, tone_check
except ImportError:  # run as a script
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    import assemble  # type: ignore
    import glyph_tone  # type: ignore
    import pngio  # type: ignore
    import tone_check  # type: ignore

GRID = 39
DOME_RADIUS = 15.5  # units from the frame's centre: inside the plate's rim (its rim starts at about 16.5)
MIN_PART_CELLS = 6  # a connected piece of the mask smaller than this is a speck, dropped
DOME_WINDOW = 4  # the dome colour a cell is compared with: the mean of the cells that are no glyph in a 9 x 9 window
COLOUR_DISTANCE = 24.0  # a cell this far (dE76) from the dome colour is glyph even where the checker does not alternate (the shield's bar, a dim arrow); the dark cells of a checker (about 8 to 14 away) join by the fill rule
QUIET = 4.0  # a cell whose lightness differs from the mean of its four neighbours by less than this is quiet (dome, not checker)
HOLE_DARKER = 4.0  # a cell this much darker (L*) than the dome is a hole of the glyph (the key's eye), never filled
PRESSED_RADIUS = 18.5  # the radius (units) a mask of the pressed frame is taken to: the glyph may reach the rim, whose pressed look is dark
ROUND_HOLE_MIN_CELLS = 10  # an enclosed hole of this many cells or more is regularized to a circle by `--round-holes`
SIGMA_CELLS = 0.6  # the Gaussian of the mask, in cells
EDGE_WIDTH = 0.2  # the soft threshold's half width (of the Gaussian's 0..1)
WINDOW = 1  # the colour window of a cell tone: 3 x 3 cells (grown up to MAX_WINDOW when it holds no cell of the kind)
MAX_WINDOW = 4
BEVEL_SIGMA_CELLS = 0.55
BEVEL_LIGHT = 0.30
BEVEL_SHADE = 0.34


class CellsError(Exception):
    pass


def lab_grid(frame: "pngio.Image") -> list[tuple[float, float, float]]:
    if (frame.w, frame.h) != (GRID, GRID):
        raise CellsError(f"a frame of {frame.w} x {frame.h} px is not the {GRID} x {GRID} original")
    return [tone_check._pixel_lab(frame.data[i], frame.data[i + 1], frame.data[i + 2]) for i in range(0, len(frame.data), 4)]


def otsu(values: list[float], bins: int = 100) -> float:
    """The lightness threshold that splits `values` (0..100) into two classes of the largest between-class variance."""
    hist = [0] * bins
    for v in values:
        hist[min(bins - 1, max(0, int(v * bins / 100.0)))] += 1
    total = len(values)
    if total == 0:
        raise CellsError("no pixel inside the dome")
    sum_all = sum(i * h for i, h in enumerate(hist))
    best, threshold, w0, sum0 = -1.0, 0.0, 0, 0.0
    for i, h in enumerate(hist):
        w0 += h
        if w0 == 0:
            continue
        w1 = total - w0
        if w1 == 0:
            break
        sum0 += i * h
        m0, m1 = sum0 / w0, (sum_all - sum0) / w1
        between = w0 * w1 * (m0 - m1) ** 2
        if between > best:
            best, threshold = between, (i + 1) * 100.0 / bins
    return threshold


def interior(ux: int, uy: int, radius: float = DOME_RADIUS) -> bool:
    return math.hypot(ux + 0.5 - GRID / 2, uy + 0.5 - GRID / 2) <= radius


def neighbours4(i: int) -> list[int]:
    x, y = i % GRID, i // GRID
    return [j for j in ((i - 1) if x > 0 else -1, (i + 1) if x < GRID - 1 else -1, (i - GRID) if y > 0 else -1, (i + GRID) if y < GRID - 1 else -1) if j >= 0]


def components(mask: list[bool]) -> list[list[int]]:
    seen = [False] * len(mask)
    out = []
    for start, on in enumerate(mask):
        if not on or seen[start]:
            continue
        stack, part = [start], []
        seen[start] = True
        while stack:
            i = stack.pop()
            part.append(i)
            for j in neighbours4(i):
                if mask[j] and not seen[j]:
                    seen[j] = True
                    stack.append(j)
        out.append(part)
    return out


def lab_distance(a: tuple[float, float, float], b: tuple[float, float, float]) -> float:
    return math.sqrt((a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2 + (a[2] - b[2]) ** 2)


def dilate(mask: list[bool], cells: int = 1) -> list[bool]:
    out = list(mask)
    for _ in range(cells):
        step = list(out)
        for i, on in enumerate(out):
            if on:
                for j in neighbours4(i):
                    step[j] = True
        out = step
    return out


def erode(mask: list[bool]) -> list[bool]:
    return [on and all(mask[j] for j in neighbours4(i)) and len(neighbours4(i)) == 4 for i, on in enumerate(mask)]


def outside_cells(mask: list[bool]) -> list[bool]:
    """The non-glyph cells that a path of non-glyph cells joins to the frame's edge."""
    outside = [False] * len(mask)
    stack = [i for i in range(len(mask)) if not mask[i] and (i % GRID in (0, GRID - 1) or i // GRID in (0, GRID - 1))]
    for i in stack:
        outside[i] = True
    while stack:
        i = stack.pop()
        for j in neighbours4(i):
            if not mask[j] and not outside[j]:
                outside[j] = True
                stack.append(j)
    return outside


SPECK_CELLS = 2  # an enclosed hole of this many cells or fewer is a speck of the checker, filled (the key's eye is larger)


def glyph_mask(labs: list[tuple[float, float, float]], radius: float = DOME_RADIUS) -> tuple[list[bool], float]:
    """The cells of the glyph (True) and the alternation threshold that found them, up to `radius` units from the frame's centre (the dome, or further for a frame whose rim is dark).
    First the cells of the checker: a bright cell is lighter than the mean of its four neighbours by at least the threshold (Otsu split of that difference inside the dome: a smooth dome
    varies little, a checker a lot). Then the dome's colour near each cell (the mean of the cells that are not near the checker, a 9 x 9 window) and every cell COLOUR_DISTANCE away from
    it joins (the dark cells of a checker lie above the dome; the shield's bar and a dim idle arrow do not alternate). A cell HOLE_DARKER darker than the dome is a hole of the glyph
    (the key's eye) and is never filled. The other cells whose four neighbours (three at the edge) are glyph are filled in, the mask is closed (grown, then shrunk by one cell), and
    connected pieces under MIN_PART_CELLS are dropped."""
    n = GRID * GRID
    core = [interior(i % GRID, i // GRID) for i in range(n)]
    inside = [interior(i % GRID, i // GRID, max(radius, DOME_RADIUS)) for i in range(n)]
    diff = [0.0] * n
    for i in range(n):
        near = neighbours4(i)
        diff[i] = labs[i][0] - sum(labs[j][0] for j in near) / len(near)
    threshold = otsu([abs(diff[i]) for i in range(n) if core[i]])
    if threshold <= 0.0:
        raise CellsError("no glyph found in the frame (no checker inside the dome)")
    bright = [inside[i] and diff[i] >= threshold for i in range(n)]
    away = dilate(bright, 1)
    dome_cells = [core[i] and not away[i] and abs(diff[i]) < QUIET for i in range(n)]  # quiet cells only: a checker's weak body cells must not become the dome
    pool = [labs[i] for i in range(n) if dome_cells[i]]
    if not pool:
        raise CellsError("no dome cell outside the checker")
    global_dome = tuple(sorted(v[k] for v in pool)[len(pool) // 2] for k in range(3))
    reference: list = [global_dome] * n
    mask = list(bright)
    for i in range(n):
        if not inside[i]:
            continue
        x, y = i % GRID, i // GRID
        window = [labs[yy * GRID + xx] for yy in range(max(0, y - DOME_WINDOW), min(GRID, y + DOME_WINDOW + 1))
                  for xx in range(max(0, x - DOME_WINDOW), min(GRID, x + DOME_WINDOW + 1)) if dome_cells[yy * GRID + xx]]
        if window:
            reference[i] = tuple(sum(v[k] for v in window) / len(window) for k in range(3))
        if not bright[i] and lab_distance(labs[i], reference[i]) >= COLOUR_DISTANCE:
            mask[i] = True
    solid = [labs[i][0] >= reference[i][0] - HOLE_DARKER for i in range(n)]
    for i in range(n):
        if inside[i] and not mask[i] and solid[i] and sum(1 for j in neighbours4(i) if mask[j]) >= 3:
            mask[i] = True
    closed = erode(dilate(mask, 1))
    mask = [(m or (c and solid[i])) and inside[i] for i, (m, c) in enumerate(zip(mask, closed))]
    out_of = outside_cells(mask)
    for part in components([not m and not o for m, o in zip(mask, out_of)]):
        if len(part) <= SPECK_CELLS:
            for i in part:
                mask[i] = True
    out = [False] * n
    for part in components(mask):
        if len(part) >= MIN_PART_CELLS:
            for i in part:
                out[i] = True
    return out, threshold


def enclosed_holes(mask: list[bool]) -> list[tuple[float, float, float]]:
    """The holes of the glyph that no path of non-glyph cells joins to the frame's edge, as circles (centre x, y in units, radius in units: the circle of the hole's area); only holes of
    ROUND_HOLE_MIN_CELLS cells or more (the gear's hole, not the key's eye)."""
    outside = outside_cells(mask)
    holes = []
    for part in components([not m and not o for m, o in zip(mask, outside)]):
        if len(part) >= ROUND_HOLE_MIN_CELLS:
            holes.append((sum(i % GRID + 0.5 for i in part) / len(part), sum(i // GRID + 0.5 for i in part) / len(part), math.sqrt(len(part) / math.pi)))
    return holes


def phase_of(mask: list[bool], labs: list[tuple[float, float, float]]) -> int:
    """The parity ((x + y) % 2) of the bright cells of the checker: the one whose cells in the mask are lighter on average."""
    sums, counts = [0.0, 0.0], [0, 0]
    for i, on in enumerate(mask):
        if on:
            parity = (i % GRID + i // GRID) % 2
            sums[parity] += labs[i][0]
            counts[parity] += 1
    means = [sums[k] / counts[k] if counts[k] else -1.0 for k in (0, 1)]
    return 0 if means[0] >= means[1] else 1


def tone_fields(labs: list[tuple[float, float, float]], mask: list[bool], phase: int) -> list[tuple[tuple[float, float, float], tuple[float, float, float]] | None]:
    """For every cell of the mask (and the cells near it) the pair (bright tone, dark tone): the mean L*a*b* of the mask's cells of that parity in a window around it."""
    reach = MAX_WINDOW
    fields: list = [None] * (GRID * GRID)
    for i in range(GRID * GRID):
        x, y = i % GRID, i // GRID
        near = any(mask[(yy * GRID + xx)] for yy in range(max(0, y - 2), min(GRID, y + 3)) for xx in range(max(0, x - 2), min(GRID, x + 3)))
        if not near:
            continue
        pair = []
        for kind in (phase, 1 - phase):
            tone = None
            for r in range(WINDOW, reach + 1):
                sl = sa = sb = 0.0
                n = 0
                for yy in range(max(0, y - r), min(GRID, y + r + 1)):
                    for xx in range(max(0, x - r), min(GRID, x + r + 1)):
                        j = yy * GRID + xx
                        if mask[j] and (xx + yy) % 2 == kind:
                            sl += labs[j][0]; sa += labs[j][1]; sb += labs[j][2]; n += 1
                if n:
                    tone = (sl / n, sa / n, sb / n)
                    break
            pair.append(tone)
        if pair[0] is None or pair[1] is None:
            fallback = pair[0] or pair[1]
            if fallback is None:
                continue
            pair = [pair[0] or fallback, pair[1] or fallback]
        fields[i] = (pair[0], pair[1])
    return fields


def gaussian_kernel(sigma: float) -> list[float]:
    radius = max(1, int(math.ceil(sigma * 3)))
    weights = [math.exp(-(k * k) / (2 * sigma * sigma)) for k in range(-radius, radius + 1)]
    total = sum(weights)
    return [w / total for w in weights]


def blur(plane: list[float], w: int, h: int, sigma: float) -> list[float]:
    kernel = gaussian_kernel(sigma)
    radius = len(kernel) // 2
    tmp = [0.0] * (w * h)
    for y in range(h):
        row = y * w
        for x in range(w):
            acc = 0.0
            for k, weight in enumerate(kernel):
                xx = x + k - radius
                if 0 <= xx < w:
                    acc += plane[row + xx] * weight
            tmp[row + x] = acc
    out = [0.0] * (w * h)
    for y in range(h):
        for x in range(w):
            acc = 0.0
            for k, weight in enumerate(kernel):
                yy = y + k - radius
                if 0 <= yy < h:
                    acc += tmp[yy * w + x] * weight
            out[y * w + x] = acc
    return out


def smoothstep(edge0: float, edge1: float, x: float) -> float:
    t = min(1.0, max(0.0, (x - edge0) / (edge1 - edge0)))
    return t * t * (3 - 2 * t)


def silhouette(mask: list[bool], scale: int, holes: "list[tuple[float, float, float]] | tuple" = ()) -> list[float]:
    """The smooth outline of the 39 x 39 mask at `scale` px per cell: alpha 0..1 per output pixel. `holes` (circles, see `enclosed_holes`) are filled in the mask and cut out again as
    true circles, soft by one device pixel."""
    size = GRID * scale
    filled = list(mask)
    for cx, cy, r in holes:
        for i in range(len(filled)):
            if math.hypot(i % GRID + 0.5 - cx, i // GRID + 0.5 - cy) <= r + 1.2:
                filled[i] = True
    plane = [1.0 if filled[(y // scale) * GRID + (x // scale)] else 0.0 for y in range(size) for x in range(size)]
    soft = blur(plane, size, size, SIGMA_CELLS * scale)
    alpha = [smoothstep(0.5 - EDGE_WIDTH, 0.5 + EDGE_WIDTH, v) for v in soft]
    for cx, cy, r in holes:
        px, py, pr = cx * scale, cy * scale, r * scale
        for y in range(max(0, int(py - pr - 3)), min(size, int(py + pr + 3))):
            for x in range(max(0, int(px - pr - 3)), min(size, int(px + pr + 3))):
                d = math.hypot(x + 0.5 - px, y + 0.5 - py)
                alpha[y * size + x] *= smoothstep(pr - 0.75, pr + 0.75, d)
    return alpha


def render(frame: "pngio.Image", look: str = "flat", scale: int = 4, mask_frame: "pngio.Image | None" = None,
           mask_frames: "list[tuple[pngio.Image, float]] | None" = None, round_holes: bool = False) -> tuple["pngio.Image", dict]:
    """The glyph canvas of one ORIGINAL frame: GRID x scale square, RGBA, alpha the silhouette, colour the cells (and for `bevel` the edge light). The silhouette (and the checker's
    phase) is read from `mask_frame` when given (the lit frame: its checker is strong, and the glyph is one shape in all three states), else from the frame itself.
    `mask_frames` ((frame, radius in units) pairs) takes the union of the masks of all of them (the first gives the phase): the lit frames' masks, the pressed one out to the rim.
    `round_holes` regularizes the enclosed holes of the glyph to circles (a simple shape is aligned after the mask is taken, the gear's hole)."""
    if look not in ("flat", "bevel"):
        raise CellsError("--look must be flat or bevel")
    labs = lab_grid(frame)
    if mask_frames is None:
        mask_frames = [(mask_frame if mask_frame is not None else frame, DOME_RADIUS)]
    mask_labs = lab_grid(mask_frames[0][0])
    mask, threshold = glyph_mask(mask_labs, mask_frames[0][1])
    for other, radius in mask_frames[1:]:
        extra, _ = glyph_mask(lab_grid(other), radius)
        mask = [a or b for a, b in zip(mask, extra)]
    if not any(mask):
        raise CellsError("no glyph found in the frame")
    phase = phase_of(mask, mask_labs)
    fields = tone_fields(labs, mask, phase)
    size = GRID * scale
    holes = enclosed_holes(mask) if round_holes else []
    alpha = silhouette(mask, scale, holes)
    out = pngio.Image(size, size, bytearray(size * size * 4))
    for y in range(size):
        for x in range(size):
            ux, uy = x // scale, y // scale
            pair = fields[uy * GRID + ux]
            a = alpha[y * size + x]
            if pair is None or a <= 0.0:
                continue
            lab = pair[0] if (ux + uy) % 2 == phase else pair[1]
            o = (y * size + x) * 4
            out.data[o:o + 3] = bytes(tone_check._from_lab(*lab))
            out.data[o + 3] = int(round(a * 255))
    if look == "bevel":
        bevel(out, scale)
    cx = sum(i % GRID + 0.5 for i, on in enumerate(mask) if on) / sum(mask)
    cy = sum(i // GRID + 0.5 for i, on in enumerate(mask) if on) / sum(mask)
    info = {"alternation_threshold": threshold, "phase": phase, "cells": sum(mask), "centroid_units": (cx, cy), "look": look, "round_holes": [list(h) for h in holes]}
    return out, info


def bevel(image: "pngio.Image", scale: int) -> None:
    """A thin bevel on the glyph's edge: the colour goes toward white where the edge faces the upper left and toward black where it faces the lower right (the kit's light)."""
    size = image.w
    height = blur([image.data[i + 3] / 255.0 for i in range(0, len(image.data), 4)], size, size, BEVEL_SIGMA_CELLS * scale)
    for y in range(1, size - 1):
        for x in range(1, size - 1):
            o = (y * size + x) * 4
            if image.data[o + 3] == 0:
                continue
            gx = (height[y * size + x + 1] - height[y * size + x - 1]) * 0.5
            gy = (height[(y + 1) * size + x] - height[(y - 1) * size + x]) * 0.5
            facing = (gx + gy) / math.sqrt(2) * scale * 1.1  # + toward the upper left edge, - toward the lower right
            facing = max(-1.0, min(1.0, facing))
            for c in range(3):
                v = image.data[o + c]
                v = v + (255 - v) * BEVEL_LIGHT * facing if facing > 0 else v * (1 + BEVEL_SHADE * facing)
                image.data[o + c] = int(round(max(0.0, min(255.0, v))))


def plate_canvas(plate: "pngio.Image", scale: int = 4, plate_units: float = 38.6, shadow: float = 0.0, sigma_units: float = 0.45,
                 offset_units: tuple[float, float] = (0.2, 0.3), edge_dark: float = 0.4) -> "pngio.Image":
    """The kit plate master brought to `plate_units` across and centred on the 39-unit canvas (`scale` px per unit): by default it fills the socket as the original's plate does (38.6 units,
    the rim's last unit softened to a darker edge by `edge_dark`, no shadow). With `shadow` above 0 a soft dark shadow (its alpha `shadow`,
    blurred by `sigma_units`, moved by `offset_units`) lies under it in the alpha, beyond the plate's edge (it needs a plate smaller than the canvas, or the frame's box cuts it)."""
    if not 0 < plate_units <= GRID:
        raise CellsError(f"--plate-units {plate_units} must be above 0 and at most {GRID}")
    if sigma_units <= 0 or shadow < 0 or edge_dark < 0:
        raise CellsError("--sigma must be above 0; --shadow and --edge-dark must not be negative")
    size = GRID * scale
    target = max(1, int(round(plate_units * scale)))
    resized = assemble.resample(plate, target, target)
    left = (size - target) // 2
    plane = [0.0] * (size * size)
    for y in range(target):
        for x in range(target):
            plane[(y + left) * size + x + left] = resized.data[(y * target + x) * 4 + 3] / 255.0
    dx, dy = int(round(offset_units[0] * scale)), int(round(offset_units[1] * scale))
    shifted = [0.0] * (size * size)
    if shadow > 0:
        for y in range(size):
            for x in range(size):
                sx, sy = x - dx, y - dy
                if 0 <= sx < size and 0 <= sy < size:
                    shifted[y * size + x] = plane[sy * size + sx]
    shade = blur(shifted, size, size, sigma_units * scale) if shadow > 0 else shifted
    edge = plate_units / 2
    out = pngio.Image(size, size, bytearray(size * size * 4))
    for y in range(size):
        for x in range(size):
            o = (y * size + x) * 4
            p = plane[y * size + x]
            sa = min(1.0, shade[y * size + x]) * shadow
            if p > 0:
                ro = ((y - left) * target + (x - left)) * 4
                r = math.hypot((x + 0.5) / scale - GRID / 2, (y + 0.5) / scale - GRID / 2)
                dark = 1.0 - edge_dark * smoothstep(edge - 1.6, edge - 0.2, r)
                pr, pg, pb = (resized.data[ro + k] * dark for k in range(3))
                a = p + sa * (1 - p)
                out.data[o:o + 3] = bytes(int(round(max(0.0, min(255.0, (c * p + 8 * sa * (1 - p)) / a)))) for c in (pr, pg, pb))
                out.data[o + 3] = int(round(a * 255))
            elif sa > 0.004:
                out.data[o:o + 3] = bytes((8, 7, 5))
                out.data[o + 3] = int(round(sa * 255))
    return out


# The pressed look of the original's plate against its idle look, by numbers (mean L* of the plate's pixels, the glyph left out; upper left = on the side of the light): the dome's upper-left
# ring at 12-15 units goes from 14.2 to 9.3 (x0.65), the rim at 15-17 from 17.5 to 7.4 (x0.42) and at 17-19.5 from 22.0 to 6.4 (x0.29); the lower-right side barely changes at the dome
# (19.8 to 19.8) and darkens a little at the rim (x0.94 at 15-17, x0.81 at 17-19.5). `pressed_plate` is that rule: an inner shadow on the upper left that grows toward the rim, and the whole rim a little darker.
PRESSED_SHADOW = ((9.0, 0.0), (12.0, 0.13), (14.0, 0.35), (16.0, 0.58), (18.0, 0.71), (19.5, 0.71))  # (radius in original units, shadow depth on the upper-left side)
PRESSED_RIM = ((14.5, 0.0), (16.0, 0.06), (18.0, 0.19), (19.5, 0.19))  # (radius, extra darkening of the whole rim)


def ramp(table: tuple, r: float) -> float:
    if r <= table[0][0]:
        return table[0][1]
    for (r0, v0), (r1, v1) in zip(table, table[1:]):
        if r <= r1:
            return v0 + (v1 - v0) * (r - r0) / (r1 - r0)
    return table[-1][1]


def pressed_plate(idle: "pngio.Image", scale: int = 4, plate_units: float = 38.6) -> "pngio.Image":
    """The plate's pressed look from its idle canvas: the original's rule (see PRESSED_SHADOW): the whole plate darker toward the rim and an inner shadow on the upper left. The radius
    is taken in the original's units, so the rule follows the plate's size (`plate_units` is the diameter the idle canvas was made at)."""
    if not 0 < plate_units <= GRID:
        raise CellsError(f"--plate-units {plate_units} must be above 0 and at most {GRID}")
    size = GRID * scale
    if idle.w != size or idle.h != size:
        raise CellsError(f"the idle plate canvas is {idle.w} x {idle.h} px, not {size} x {size} (39 units at {scale} px)")
    out = pngio.Image(size, size, bytearray(idle.data))
    plate_radius = plate_units / 2
    for y in range(size):
        for x in range(size):
            o = (y * size + x) * 4
            if idle.data[o + 3] == 0:
                continue
            dx, dy = (x + 0.5) / scale - GRID / 2, (y + 0.5) / scale - GRID / 2
            r = math.hypot(dx, dy)
            rho = r / plate_radius * 19.5
            t = (-dx - dy) / (math.sqrt(2) * r) if r > 1e-6 else 0.0
            factor = (1.0 - ramp(PRESSED_SHADOW, rho) * smoothstep(-0.15, 1.0, t)) * (1.0 - ramp(PRESSED_RIM, rho))
            for c in range(3):
                out.data[o + c] = int(round(idle.data[o + c] * factor))
    return out


def fit_tone(original: "pngio.Image", glyph: "pngio.Image", plate: "pngio.Image", dome_frame: "pngio.Image | None", scale: int) -> tuple["pngio.Image", dict]:
    """The glyph shifted (one L*a*b* shift of all its cells) so that its mean brightness over the plate's dome is the ORIGINAL's over its own dome: the numbers of `glyph_tone.py`
    with `--stat mean` (the cells are drawn, so the plain mean of the glyph's pixels is the measure)."""
    return glyph_tone.fit(original, glyph, (GRID / 2, GRID / 2), 1.0, float(scale), plate, "mean", dome_frame)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = parser.add_subparsers(dest="command", required=True)
    m = sub.add_parser("mask")
    m.add_argument("frame")
    m.add_argument("--json", action="store_true")
    c = sub.add_parser("cells")
    c.add_argument("frame")
    c.add_argument("out")
    c.add_argument("--look", choices=("flat", "bevel"), default="flat")
    c.add_argument("--mask-frame", action="append", help="a frame whose glyph mask is used, PATH or PATH:RADIUS (units from the centre, default 15.5; the pressed frame's rim is dark, so 18.5 reaches a glyph "
                   "that touches it); repeated: the union of the masks, the first gives the checker's phase (the lit frame: one shape in all three states)")
    c.add_argument("--round-holes", action="store_true", help="regularize the glyph's enclosed holes to circles (the gear's hole)")
    c.add_argument("--scale", type=int, default=4)
    c.add_argument("--plate", help="the plate canvas (`plate` output) the glyph sits on: fit the tone against its dome")
    c.add_argument("--dome-frame", help="another frame of the ORIGINAL whose dome is the reference (the idle frame)")
    c.add_argument("--json", action="store_true")
    p = sub.add_parser("plate")
    p.add_argument("plate_master")
    p.add_argument("out")
    p.add_argument("--scale", type=int, default=4)
    p.add_argument("--plate-units", type=float, default=38.6)
    p.add_argument("--shadow", type=float, default=0.0)
    p.add_argument("--sigma", type=float, default=0.45)
    p.add_argument("--offset", default="0.2,0.3")
    p.add_argument("--edge-dark", type=float, default=0.4)
    q = sub.add_parser("pressed")
    q.add_argument("idle_canvas", help="the `plate` output of the idle look")
    q.add_argument("out")
    q.add_argument("--scale", type=int, default=4)
    q.add_argument("--plate-units", type=float, default=38.6)
    args = parser.parse_args(argv)
    try:
        if getattr(args, "scale", 4) < 1:
            raise CellsError("--scale must be at least 1")
        if args.command == "mask":
            frame = pngio.read(args.frame)
            labs = lab_grid(frame)
            mask, threshold = glyph_mask(labs)
            cx = sum(i % GRID + 0.5 for i, on in enumerate(mask) if on) / max(1, sum(mask))
            cy = sum(i // GRID + 0.5 for i, on in enumerate(mask) if on) / max(1, sum(mask))
            if args.json:
                print(json.dumps({"alternation_threshold": threshold, "cells": sum(mask), "centroid_units": [cx, cy]}))
            else:
                for y in range(GRID):
                    print("".join("#" if mask[y * GRID + x] else "." for x in range(GRID)))
                print(f"alternation threshold {threshold:.1f} L*, {sum(mask)} cells, centroid {cx:.2f},{cy:.2f} units")
        elif args.command == "cells":
            frame = pngio.read(args.frame)
            frames = []
            for spec in args.mask_frame or []:
                path, colon, radius = spec.rpartition(":")
                if not colon or not radius.replace(".", "", 1).isdigit():
                    path, radius = spec, ""
                frames.append((pngio.read(path), float(radius) if radius else DOME_RADIUS))
            image, info = render(frame, args.look, args.scale, mask_frames=frames or None, round_holes=args.round_holes)
            if args.plate:
                image, tone = fit_tone(frame, image, pngio.read(args.plate), pngio.read(args.dome_frame) if args.dome_frame else None, args.scale)
                info["tone"] = {k: tone[k] for k in ("original_glyph_mean", "original_dome", "plate_dome", "target_glyph", "applied_shift")}
            pngio.write(args.out, image)
            print(json.dumps(info) if args.json else f"GLYPH CELLS {args.look}: {info['cells']} cells, centroid {info['centroid_units'][0]:.2f},{info['centroid_units'][1]:.2f} units -> {args.out}")
        elif args.command == "plate":
            offset = args.offset.split(",")
            if len(offset) != 2:
                raise CellsError(f"--offset {args.offset!r} is not X,Y in units")
            ox, oy = (float(v) for v in offset)
            pngio.write(args.out, plate_canvas(pngio.read(args.plate_master), args.scale, args.plate_units, args.shadow, args.sigma, (ox, oy), args.edge_dark))
            print(f"GLYPH CELLS plate canvas -> {args.out}")
        else:
            pngio.write(args.out, pressed_plate(pngio.read(args.idle_canvas), args.scale, args.plate_units))
            print(f"GLYPH CELLS pressed plate -> {args.out}")
    except (OSError, pngio.PngError, CellsError, glyph_tone.ToneError, ValueError) as error:
        print(f"GLYPH CELLS error: {error}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
