"""ARC-UI-3058 (round 3, owner decision 2026-11-21): a button glyph drawn by a RULE from the ORIGINAL's own frame, no Codex.

The ORIGINAL's glyph is a checker one original pixel wide (the cell is the pixel; weak in the idle frame, strong in the hover and pressed frames), a part of the
picture and not a dither to smooth away. The kit's glyph keeps that and redraws what the 39 x 39 grid cannot hold:

* **silhouette** from the ORIGINAL's glyph mask of the same frame (its position is the original's coordinates, not a frame's centre): the cells that stand out from the
  plate (Otsu split of the lightness inside the dome), the dark cells of the checker inside it filled, small specks dropped; the 39 x 39 mask is turned into a smooth outline at 4 x
  (a Gaussian of the mask, then a soft threshold), so an edge is no stair of original pixels;
* **cells**: every cell of the 39 x 39 grid is one of the two checker tones by the original's own parity (the phase read from the frame); a tone is the local colour of that kind
  of cell in the ORIGINAL (a window of 3 x 3 cells, grown when it holds none), so the contrast of the cells in each state (idle, hover, pressed) is the original's and the glyph keeps
  its shading (the shield's brass bar, the key's hole, the hover glow);
* **two looks**: `flat` (crisp square cells, a soft-edged silhouette) and `bevel` (the same with a thin bevel: a light edge on the upper left, a dark one on the lower right, as the
  kit's light falls);
* **tone** per state against the plate by numbers: the glyph is then shifted as one (the L*a*b* mean over its cells) so that its brightness over the kit plate's dome equals the ORIGINAL's
  over its own dome (`glyph_tone.py`, `--stat mean` because the cells are drawn now).

    python3 -B tools/steamwoven_ui/glyph_cells.py mask FRAME.png [--json]
    python3 -B tools/steamwoven_ui/glyph_cells.py cells FRAME.png OUT.png [--look flat|bevel] [--scale 4] [--mask-frame LIT_FRAME.png] [--plate PLATE_CANVAS.png --dome-frame IDLE_FRAME.png]
    python3 -B tools/steamwoven_ui/glyph_cells.py plate PLATE_MASTER.png OUT.png [--scale 4] [--plate-units 36.5] [--shadow 0.55] [--sigma 0.45] [--offset 0.2,0.3]

`mask` prints the glyph's mask (rows of # and .) and its centroid in units; `cells` writes the glyph canvas, the whole 39-unit frame at `--scale` px per unit (alpha is the
silhouette, nothing else is drawn), so a layout places it at 0,0 and the glyph lies where the ORIGINAL's does; `plate` writes the kit plate master centred on a 39-unit canvas
at the same scale, with a soft shadow in its alpha (the original's brass rim has a soft shadow). Standard library only; exit 0, 2 = an input could not be used.
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
MIN_PART_CELLS = 6
DOME_WINDOW = 4  # the dome colour a cell is compared with: the mean of the cells that are no glyph in a 9 x 9 window
COLOUR_DISTANCE = 24.0  # a cell this far (dE76) from the dome colour is glyph even where the checker is too weak to alternate (the shield's bar, a dim idle arrow)  # a connected piece of the mask smaller than this is a speck, dropped
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


def interior(ux: int, uy: int) -> bool:
    return math.hypot(ux + 0.5 - GRID / 2, uy + 0.5 - GRID / 2) <= DOME_RADIUS


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


def glyph_mask(labs: list[tuple[float, float, float]]) -> tuple[list[bool], float]:
    """The cells of the glyph (True) and the alternation threshold that found them. First the cells of the checker: a bright cell is lighter than the mean of its four neighbours
    by at least the threshold (Otsu split of that difference inside the dome: a smooth dome varies little, a checker a lot). Then the dome's colour near each cell (the mean of the cells
    that are not near the checker, a 9 x 9 window) and every cell COLOUR_DISTANCE away from it joins (a part whose checker is too weak to alternate, the shield's bar, a dim idle arrow).
    The dark cells of the checker, whose four neighbours (three at the edge) are glyph, are filled in, the mask is closed (grown, then shrunk by one cell), and connected pieces
    under MIN_PART_CELLS are dropped."""
    n = GRID * GRID
    inside = [interior(i % GRID, i // GRID) for i in range(n)]
    diff = [0.0] * n
    for i in range(n):
        near = neighbours4(i)
        diff[i] = labs[i][0] - sum(labs[j][0] for j in near) / len(near)
    threshold = otsu([abs(diff[i]) for i in range(n) if inside[i]])
    bright = [inside[i] and diff[i] >= threshold for i in range(n)]
    away = dilate(bright, 1)
    dome_cells = [inside[i] and not away[i] for i in range(n)]
    pool = [labs[i] for i in range(n) if dome_cells[i]]
    if not pool:
        raise CellsError("no dome cell outside the checker")
    global_dome = tuple(sorted(v[k] for v in pool)[len(pool) // 2] for k in range(3))
    mask = list(bright)
    for i in range(n):
        if not inside[i] or bright[i]:
            continue
        x, y = i % GRID, i // GRID
        window = [labs[yy * GRID + xx] for yy in range(max(0, y - DOME_WINDOW), min(GRID, y + DOME_WINDOW + 1))
                  for xx in range(max(0, x - DOME_WINDOW), min(GRID, x + DOME_WINDOW + 1)) if dome_cells[yy * GRID + xx]]
        reference = tuple(sum(v[k] for v in window) / len(window) for k in range(3)) if window else global_dome
        if lab_distance(labs[i], reference) >= COLOUR_DISTANCE:
            mask[i] = True
    for i in range(n):
        if inside[i] and not mask[i] and sum(1 for j in neighbours4(i) if mask[j]) >= 3:
            mask[i] = True
    closed = erode(dilate(mask, 1))
    mask = [(m or c) and inside[i] for i, (m, c) in enumerate(zip(mask, closed))]
    out = [False] * n
    for part in components(mask):
        if len(part) >= MIN_PART_CELLS:
            for i in part:
                out[i] = True
    return out, threshold


def phase_of(mask: list[bool], labs: list[tuple[float, float, float]], threshold: float) -> int:
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


def silhouette(mask: list[bool], scale: int) -> list[float]:
    """The smooth outline of the 39 x 39 mask at `scale` px per cell: alpha 0..1 per output pixel."""
    size = GRID * scale
    plane = [1.0 if mask[(y // scale) * GRID + (x // scale)] else 0.0 for y in range(size) for x in range(size)]
    soft = blur(plane, size, size, SIGMA_CELLS * scale)
    return [smoothstep(0.5 - EDGE_WIDTH, 0.5 + EDGE_WIDTH, v) for v in soft]


def render(frame: "pngio.Image", look: str = "flat", scale: int = 4, mask_frame: "pngio.Image | None" = None) -> tuple["pngio.Image", dict]:
    """The glyph canvas of one ORIGINAL frame: GRID x scale square, RGBA, alpha the silhouette, colour the cells (and for `bevel` the edge light). The silhouette (and the checker's
    phase) is read from `mask_frame` when given (the lit frame: its checker is strong, and the glyph is one shape in all three states), else from the frame itself."""
    if look not in ("flat", "bevel"):
        raise CellsError("--look must be flat or bevel")
    labs = lab_grid(frame)
    mask_labs = lab_grid(mask_frame) if mask_frame is not None else labs
    mask, threshold = glyph_mask(mask_labs)
    if not any(mask):
        raise CellsError("no glyph found in the frame")
    phase = phase_of(mask, mask_labs, threshold)
    fields = tone_fields(labs, mask, phase)
    size = GRID * scale
    alpha = silhouette(mask, scale)
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
    info = {"alternation_threshold": threshold, "phase": phase, "cells": sum(mask), "centroid_units": (cx, cy), "look": look}
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


def plate_canvas(plate: "pngio.Image", scale: int = 4, plate_units: float = 36.5, shadow: float = 0.55, sigma_units: float = 0.45,
                 offset_units: tuple[float, float] = (0.2, 0.3)) -> "pngio.Image":
    """The kit plate master brought to `plate_units` across and centred on the 39-unit canvas (`scale` px per unit), a soft dark shadow (its alpha `shadow`, blurred by
    `sigma_units`, moved by `offset_units`: down and right, the kit's light falls from the upper left) under it in the alpha."""
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
    for y in range(size):
        for x in range(size):
            sx, sy = x - dx, y - dy
            if 0 <= sx < size and 0 <= sy < size:
                shifted[y * size + x] = plane[sy * size + sx]
    shade = blur(shifted, size, size, sigma_units * scale)
    out = pngio.Image(size, size, bytearray(size * size * 4))
    for y in range(size):
        for x in range(size):
            o = (y * size + x) * 4
            p = plane[y * size + x]
            sa = min(1.0, shade[y * size + x]) * shadow
            if p > 0:
                ro = ((y - left) * target + (x - left)) * 4
                pr, pg, pb = resized.data[ro], resized.data[ro + 1], resized.data[ro + 2]
                a = p + sa * (1 - p)
                out.data[o:o + 3] = bytes(int(round((c * p + 8 * sa * (1 - p)) / a)) for c in (pr, pg, pb))
                out.data[o + 3] = int(round(a * 255))
            elif sa > 0.004:
                out.data[o:o + 3] = bytes((8, 7, 5))
                out.data[o + 3] = int(round(sa * 255))
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
    c.add_argument("--mask-frame", help="the frame whose glyph mask and checker phase are used (the lit frame: one shape in all three states)")
    c.add_argument("--scale", type=int, default=4)
    c.add_argument("--plate", help="the plate canvas (`plate` output) the glyph sits on: fit the tone against its dome")
    c.add_argument("--dome-frame", help="another frame of the ORIGINAL whose dome is the reference (the idle frame)")
    c.add_argument("--json", action="store_true")
    p = sub.add_parser("plate")
    p.add_argument("plate_master")
    p.add_argument("out")
    p.add_argument("--scale", type=int, default=4)
    p.add_argument("--plate-units", type=float, default=36.5)
    p.add_argument("--shadow", type=float, default=0.55)
    p.add_argument("--sigma", type=float, default=0.45)
    p.add_argument("--offset", default="0.2,0.3")
    args = parser.parse_args(argv)
    try:
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
            image, info = render(frame, args.look, args.scale, pngio.read(args.mask_frame) if args.mask_frame else None)
            if args.plate:
                image, tone = fit_tone(frame, image, pngio.read(args.plate), pngio.read(args.dome_frame) if args.dome_frame else None, args.scale)
                info["tone"] = {k: tone[k] for k in ("original_glyph_mean", "original_dome", "plate_dome", "target_glyph", "applied_shift")}
            pngio.write(args.out, image)
            print(json.dumps(info) if args.json else f"GLYPH CELLS {args.look}: {info['cells']} cells, centroid {info['centroid_units'][0]:.2f},{info['centroid_units'][1]:.2f} units -> {args.out}")
        else:
            ox, oy = (float(v) for v in args.offset.split(","))
            pngio.write(args.out, plate_canvas(pngio.read(args.plate_master), args.scale, args.plate_units, args.shadow, args.sigma, (ox, oy)))
            print(f"GLYPH CELLS plate canvas -> {args.out}")
    except (OSError, pngio.PngError, CellsError, glyph_tone.ToneError, ValueError) as error:
        print(f"GLYPH CELLS error: {error}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
