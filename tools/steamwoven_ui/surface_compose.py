"""ARC-RES-2457 method A as the player sees it (owner pick 2026-11-21): the Codex result (`surface_study.py`, zones as single images) is taken for the WOOD only.
Everything that is not wood (text, counters, digits, fields filled in play, portrait circle, grids, brass and iron parts, buttons) is the ORIGINAL's, by the
ORIGINAL's mask, as a stand-in for the kit parts; the wood of the Codex result is cleaned of its single dark specks (pointwise), the original's rows of dots and, in the top strip, its light vertical
bevel lines are put back, notches of the wood mask are filled, the white chip at the right plate's tip is removed (ARC-UI-3104), and its tone is fitted to the
ORIGINAL's wood (mean L*, contrast, chroma). Needs ImageMagick (`magick`). Local files only, no art in git.

    python3 -B tools/steamwoven_ui/surface_compose.py compose --original O.png --a A.png --out OUT.png --work DIR [--bevel lighten|original]
"""
from __future__ import annotations

import argparse
import datetime
import hashlib
import json
import re
import subprocess
import sys
from bisect import bisect_left, bisect_right
from dataclasses import asdict, dataclass
from pathlib import Path

try:
    from . import method_c, pngio, surface_metrics, tone_check
except ImportError:  # run as a script
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    import method_c  # type: ignore
    import pngio  # type: ignore
    import surface_metrics  # type: ignore
    import tone_check  # type: ignore

SPECK_THRESHOLD = 12      # 0..255: a pixel this much darker than the median of its 9 px horizontal neighbours is a speck candidate
SPECK_MAX_PIXELS = 8      # ARC-UI-3104: only a COMPACT candidate (at most this many pixels, in a box of at most SPECK_MAX_SIDE) is a speck; grain strokes are kept
SPECK_MAX_SIDE = 4
SPECK_SIDE_DIFF = 24      # 0..255: the two row neighbours of a speck differ by at most this in luma (else it is an edge, not a speck in the grain)
SPECK_REACH = 6           # px: the nearest speck-free pixel left and right of a speck pixel, in its row, gives the fill (mean of the two)
DOT_THRESHOLD = 6         # the same for the ORIGINAL, whose rows of dots are kept (low: the dots are faint after the x3.6 upscale)
DOT_MAX_PIXELS = 12       # a dot is a small component
MEDIAN = "9x1"            # horizontal median that only DETECTS specks (a pixel darker than it); it no longer fills them (ARC-UI-3104)
NOTCH_RADIUS = 10         # px: closing radius of the wood weight; fills dark notches of the ORIGINAL's wood (stains, not wood class) that came out as sharp dark blotches
BEVEL_BAND = (0, 148)     # rows (the top strip) where the ORIGINAL's light vertical bevel lines are put back
BEVEL_EXCESS = 10         # 0..255: a pixel this much lighter than its 5 px Gaussian surround is bevel light
BEVEL_OPEN = 30           # px: a bevel line is a vertical run at least this long (an opening by a 1 x N line leaves the horizontal bevel out)
BEVEL_MIN_HEIGHT = 40     # a bevel line is a tall thin component: at least this tall
BEVEL_MAX_WIDTH = 10      # ... and at most this wide
BEVEL_GROW = 3            # px each side the line's columns are grown, so the ORIGINAL's soft 5 px band and its dark inner shadow come back whole
BEVEL_GROW_LIGHTEN = 1    # px: the lightening band is narrow, so the original's lighter wood grain beside the line is not lifted
BEVEL_MODE = "lighten"    # "lighten": the original's band plus the Codex line on top (lighter of the two); "original": the original's pixels only
CHIP_BOXES = ((1900, 0, 1930, 18),)  # x0, y0, x1, y1 of the white chip at the right plate's tip (inventory master 2880 px); bright pixels inside are plate-dark
CHIP_REACH = 8            # px right of the chip whose darkest value fills it
CHIP_MASTER = (2880, 2160)  # the chip box is the inventory master's; other sizes are left alone
CHIP_MIN_RUN = 3          # px: a bright run shorter than this is no chip
CHIP_LUMA = 100           # 0..255 in the result
ROW_TOLERANCE = 3         # px: dots of one row differ in y by at most this
ROW_GAP = 40              # px: neighbouring dots of one row are at most this far apart
ROW_MIN_DOTS = 6
MASK_FEATHER = 3.0
JOINS = (148, 1588)       # rows where the strips meet the window zone (surface_study.ZONES); the ORIGINAL's rows are kept there
JOIN_KEEP = 6             # px each side of a join that are the ORIGINAL's (about 3 px fully, the rest blended by the soft edge)
JOIN_FEATHER = 2.0
SEAM_TARGET = 1.8         # owner condition 4 (2026-11-21): the seam at a strip join is at most this (the original's own are 1.82 and 1.73); SWI-VER-5's gate of 1.5 is recorded beside it
SEAM_GATE = 1.5
ERASE_RING = 2            # px: the ring just outside an erase box whose per-channel median fills it
SCHEMA = "steamwoven-surface/1"
SAFE_ID = re.compile(r"[A-Za-z0-9][A-Za-z0-9._-]*")   # a zone id is a file name: no path parts, no leading dot


@dataclass(frozen=True)
class ZoneSpec:
    """One composition of method A (ARC-UI-3100): a whole screen of the master size (2880 x 2160 for the contexts), its tone-mask screen, the strip joins, the band
    of the light bevel lines, the chip boxes (inventory only), `erase_boxes` (x0, y0, x1, y1: baked text or counters the game draws over the surface; filled by the median
    of the ring outside the box) and `kit_parts` (dicts `id`, `file`, `x`, `y`: a kit master laid over the result at master pixels, replacing the original's stand-in)."""
    id: str
    screen: str = method_c.SCREEN
    joins: tuple[int, ...] = JOINS
    bevel_band: tuple[int, int] | None = BEVEL_BAND
    chip_boxes: tuple[tuple[int, int, int, int], ...] = ()
    erase_boxes: tuple[tuple[int, int, int, int], ...] = ()
    kit_parts: tuple[dict, ...] = ()


INVENTORY = ZoneSpec(id="ctx-inventory", chip_boxes=CHIP_BOXES)


def _is_int(value: object) -> bool:
    return isinstance(value, int) and not isinstance(value, bool)


def load_spec(path: Path) -> ZoneSpec:
    """A zone spec from JSON: `id` (a plain file name) and `screen` are required; the rest default as `ZoneSpec` (no chip boxes, no erase boxes, no kit parts).
    Any wrong shape is a ComposeError, never a traceback."""
    try:
        raw = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as error:
        raise ComposeError(f"cannot read the spec {path}: {error}") from error
    if not isinstance(raw, dict):
        raise ComposeError(f"spec {path}: the top level is an object")
    unknown = set(raw) - {"id", "screen", "joins", "bevel_band", "chip_boxes", "erase_boxes", "kit_parts"}
    if unknown or "id" not in raw or "screen" not in raw:
        raise ComposeError(f"spec {path}: needs id and screen; unknown keys {sorted(unknown)}")
    if not (isinstance(raw["id"], str) and SAFE_ID.fullmatch(raw["id"]) and isinstance(raw["screen"], str)):
        raise ComposeError(f"spec {path}: id is a plain file name ({SAFE_ID.pattern}) and screen a string")

    def boxes(name: str) -> tuple:
        value = raw.get(name, [])
        if not isinstance(value, list):
            raise ComposeError(f"spec {path}: {name} is a list")
        out = []
        for box in value:
            if not (isinstance(box, list) and len(box) == 4 and all(_is_int(v) for v in box) and box[0] < box[2] and box[1] < box[3] and min(box) >= 0):
                raise ComposeError(f"spec {path}: {name} entries are [x0, y0, x1, y1] with x0 < x1, y0 < y1")
            out.append(tuple(box))
        return tuple(out)

    parts_raw = raw.get("kit_parts", [])
    if not isinstance(parts_raw, list):
        raise ComposeError(f"spec {path}: kit_parts is a list")
    parts = []
    for part in parts_raw:
        if not (isinstance(part, dict) and set(part) == {"id", "file", "x", "y"} and isinstance(part["id"], str) and isinstance(part["file"], str) and part["file"]
                and _is_int(part["x"]) and _is_int(part["y"]) and part["x"] >= 0 and part["y"] >= 0):
            raise ComposeError(f"spec {path}: kit_parts entries are {{id, file, x, y}} (strings and integers) with x, y >= 0")
        parts.append(dict(part))
    band = raw.get("bevel_band", list(BEVEL_BAND))
    if band is not None and not (isinstance(band, list) and len(band) == 2 and all(_is_int(v) for v in band) and 0 <= band[0] < band[1]):
        raise ComposeError(f"spec {path}: bevel_band is [top, bottom] or null")
    joins = raw.get("joins", list(JOINS))
    if not (isinstance(joins, list) and joins and all(_is_int(j) and j > 0 for j in joins)):
        raise ComposeError(f"spec {path}: joins are a non-empty list of rows (positive integers)")
    return ZoneSpec(id=raw["id"], screen=raw["screen"], joins=tuple(joins), bevel_band=tuple(band) if band else None, chip_boxes=boxes("chip_boxes"),
                    erase_boxes=boxes("erase_boxes"), kit_parts=tuple(parts))


class ComposeError(Exception):
    pass


def run(command: list[str]) -> None:
    result = subprocess.run(command, capture_output=True, text=True)
    if result.returncode != 0:
        raise ComposeError(f"{command[0]} failed: {result.stderr.strip()[:300]}")


def gray_bytes(path: Path, extra: list[str] | None = None) -> bytes:
    result = subprocess.run(["magick", str(path), "-colorspace", "Gray", *(extra or []), "-depth", "8", "gray:-"], capture_output=True)
    if result.returncode != 0:
        raise ComposeError(f"magick failed: {result.stderr.decode(errors='replace').strip()[:300]}")
    return result.stdout


def components(points: set[int], width: int) -> list[list[int]]:
    """8-connected components of a set of pixel indices."""
    seen: set[int] = set()
    out = []
    for start in sorted(points):
        if start in seen:
            continue
        seen.add(start)
        stack, comp = [start], []
        while stack:
            q = stack.pop()
            comp.append(q)
            y, x = divmod(q, width)
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    if not 0 <= x + dx < width:
                        continue
                    r = (y + dy) * width + x + dx
                    if r in points and r not in seen:
                        seen.add(r)
                        stack.append(r)
        out.append(comp)
    return out


def chains_at(order_by_y: list[int], ys: list[float], centres: list[tuple[float, float]], anchor_y: float, used: set[int], tolerance: float, gap: float) -> list[list[int]]:
    """The chains (dots along x, neighbours at most `gap` apart) among the unused dots whose y is within `tolerance` of `anchor_y`."""
    lo, hi = bisect_left(ys, anchor_y - tolerance), bisect_right(ys, anchor_y + tolerance)
    band = sorted((j for j in order_by_y[lo:hi] if j not in used), key=lambda j: centres[j][1])
    chains: list[list[int]] = []
    for j in band:
        if chains and centres[j][1] - centres[chains[-1][-1]][1] <= gap:
            chains[-1].append(j)
        else:
            chains.append([j])
    return chains


def dot_rows(centres: list[tuple[float, float]], tolerance: float = ROW_TOLERANCE, gap: float = ROW_GAP, min_dots: int = ROW_MIN_DOTS) -> set[int]:
    """Indices of the dots that belong to a row: at least `min_dots` dots whose y is within `tolerance` of the row's y and whose neighbours along x are at
    most `gap` apart. The row with the longest chain is taken first and only its chain's dots are used up, so a stray dot near a row cannot split it.
    `centres` are (y, x)."""
    order = sorted(range(len(centres)), key=lambda i: centres[i][0])
    ys = [centres[i][0] for i in order]
    kept: set[int] = set()
    while True:
        best: list[list[int]] = []
        best_len = 0
        for y in sorted({centres[i][0] for i in order if i not in kept}):
            chains = chains_at(order, ys, centres, y, kept, tolerance, gap)
            longest = max((len(c) for c in chains), default=0)
            if longest > best_len:
                best_len, best = longest, chains
        if best_len < min_dots:
            return kept
        for chain in best:
            if len(chain) >= min_dots:
                kept.update(chain)


def speck_map(path: Path, threshold: int, width: int, height: int, work: Path, name: str) -> bytes:
    """Per pixel 255 where the luma is more than `threshold` darker than its horizontal median, else 0 (`Minus` is the SECOND image minus the first, so
    the gray image goes first and the median second: median - gray is positive where the pixel is dark)."""
    gray, median = work / f"{name}-gray.png", work / f"{name}-median.png"
    run(["magick", str(path), "-colorspace", "Gray", "-alpha", "off", str(gray)])
    run(["magick", str(gray), "-statistic", "Median", MEDIAN, str(median)])
    out = work / f"{name}-speck.png"
    run(["magick", str(gray), str(median), "-compose", "Minus", "-composite", "-threshold", f"{threshold / 255 * 100:.2f}%", str(out)])
    data = gray_bytes(out)
    if len(data) != width * height:
        raise ComposeError(f"{name}: speck map is {len(data)} bytes, expected {width * height}")
    return data


def write_gray(data: bytes, width: int, height: int, out: Path) -> None:
    result = subprocess.run(["magick", "-size", f"{width}x{height}", "-depth", "8", "gray:-", str(out)], input=data, capture_output=True)
    if result.returncode != 0:
        raise ComposeError(f"magick failed: {result.stderr.decode(errors='replace').strip()[:300]}")


def luma(data: bytes, i: int) -> float:
    return 0.299 * data[i] + 0.587 * data[i + 1] + 0.114 * data[i + 2]


def fill_row_pixels(image: "pngio.Image", pixels: set[int]) -> int:
    """Each pixel of `pixels` that is darker than BOTH of its row neighbours outside the set (nearest on each side, within SPECK_REACH) by at least half of
    SPECK_THRESHOLD, the two being alike (within SPECK_SIDE_DIFF), takes the mean of the two: the grain runs along x, so a row-wise fill keeps it. A pixel at an
    edge, beside a bright line (the median there is pulled up by the line; a mean of wood and line would be a tan smear), is left alone. Returns the number filled."""
    w = image.w
    data = image.data
    src = bytes(data)
    filled = 0
    for q in pixels:
        y, x = divmod(q, w)
        sides = []
        for step in (-1, 1):
            for k in range(1, SPECK_REACH + 1):
                xx = x + step * k
                if not 0 <= xx < w:
                    break
                if y * w + xx not in pixels:
                    sides.append((y * w + xx) * 4)
                    break
        if len(sides) < 2:
            continue
        left, right = luma(src, sides[0]), luma(src, sides[1])
        if abs(left - right) > SPECK_SIDE_DIFF or luma(src, q * 4) > min(left, right) - SPECK_THRESHOLD / 2:
            continue                                                       # an edge (the sides differ) or no dip: not a speck
        for c in range(3):
            data[q * 4 + c] = sum(src[i + c] for i in sides) // 2
        filled += 1
    return filled


def speck_pixels(data: bytes, width: int) -> tuple[set[int], int]:
    """The compact speck components among the candidates (`data` is a 0/255 map), each grown by 1 px, and the number of speck components."""
    points = {i for i, b in enumerate(data) if b}
    out: set[int] = set()
    count = 0
    for comp in components(points, width):
        if len(comp) > SPECK_MAX_PIXELS:
            continue
        xs = [q % width for q in comp]
        ys = [q // width for q in comp]
        if max(xs) - min(xs) >= SPECK_MAX_SIDE or max(ys) - min(ys) >= SPECK_MAX_SIDE:
            continue
        count += 1
        for q in comp:
            y, x = divmod(q, width)
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    if 0 <= x + dx < width and 0 <= y + dy:
                        out.add((y + dy) * width + x + dx)
    return out, count


def despeckle(a: Path, out: Path, work: Path, width: int, height: int) -> int:
    """The Codex wood without its single dark specks (ARC-UI-3104): a speck is a small compact component of pixels darker than their 9 px horizontal median
    (the old fill replaced every dark pixel by that median and left corduroy hatching on narrow strips); each speck pixel (the component grown by 1 px) that dips at least half of SPECK_THRESHOLD below both row neighbours
    outside the speck, and whose neighbours are alike, takes their mean. Returns the number of speck components."""
    data = speck_map(a, SPECK_THRESHOLD, width, height, work, "a")
    pixels, count = speck_pixels(data, width)
    image = pngio.read(a)
    pixels = {q for q in pixels if q < width * height}
    fill_row_pixels(image, pixels)
    pngio.write(out, image)
    return count


def original_dots(original: Path, wood_class_mask: Path, work: Path, width: int, height: int) -> tuple[Path, int]:
    """A mask of the ORIGINAL's rows of dots inside its wood (the dots grown by 1 px), written as a grey PNG; also the number of dots kept."""
    specks = speck_map(original, DOT_THRESHOLD, width, height, work, "o")
    wood = gray_bytes(wood_class_mask)
    points = {i for i, b in enumerate(specks) if b and wood[i] > 128}
    comps = [c for c in components(points, width) if len(c) <= DOT_MAX_PIXELS]
    centres = [(sum(q // width for q in c) / len(c), sum(q % width for q in c) / len(c)) for c in comps]
    keep = dot_rows(centres)
    data = bytearray(width * height)
    for i in keep:
        for q in comps[i]:
            data[q] = 255
    out = work / "o-dots.png"
    raw = work / "o-dots-raw.png"
    write_gray(bytes(data), width, height, raw)
    run(["magick", str(raw), "-morphology", "Dilate", "Disk:1", str(out)])
    return out, len(keep)


def wood_cells(original: "pngio.Image", masks: dict, screen_id: str = method_c.SCREEN) -> tuple[list[float], list[float], set[int]]:
    """The ORIGINAL's wood class mask, its coverage and the cells that count, as `tone_check.fit_material` reads a material."""
    screen = masks["screens"][screen_id]
    lab = tone_check.to_lab(tone_check.reduce_to_800x600(original))
    mask = tone_check.material_masks({**screen, "materials": {"wood": screen["materials"]["wood"]}}, masks["strip_counters"], lab)["wood"]
    mask_half = tone_check._half(mask)
    coverage = tone_check.box_blur(mask_half, tone_check.HALF_RADIUS, tone_check.HALF_W, tone_check.HALF_H)
    cells = {i for i in range(tone_check.HALF_W * tone_check.HALF_H) if mask_half[i] >= tone_check.MIN_COVER and coverage[i] >= tone_check.MIN_COVER}
    if len(cells) < tone_check.MIN_CELLS:
        raise ComposeError("no wood in the original")
    return mask, coverage, cells


def wood_stats(image: "pngio.Image", mask: list[float], coverage: list[float], cells: set[int]) -> dict:
    lab = tone_check.to_lab(tone_check.reduce_to_800x600(image))
    blurred = tone_check.masked_blur(lab, mask, coverage)
    return tone_check.tone_stats([v for i, v in enumerate(blurred) if i in cells])


def fit_numbers(original: dict, result: dict) -> dict:
    """The L* shift is applied after scaling the contrast about the result's mean: L' = m_o + s (L - m_r), chroma' = c chroma."""
    s = original["std_l"] / result["std_l"] if result["std_l"] else 1.0
    c = original["mean_chroma"] / result["mean_chroma"] if result["mean_chroma"] else 1.0
    return {"mean_original": original["mean_l"], "mean_result": result["mean_l"], "contrast_scale": min(2.0, max(0.5, s)), "chroma_scale": min(4.0, max(0.25, c))}


def apply_fit(image: "pngio.Image", weight: "pngio.Image", numbers: dict) -> "pngio.Image":
    """L* and chroma of the pixels where the grey `weight` image is not 0, blended by the weight."""
    out = bytearray(image.data)
    wd = weight.data
    for p in range(image.w * image.h):
        w = wd[p * 4] / 255
        if w < 0.01:
            continue
        i = p * 4
        l, a, b = tone_check._pixel_lab(out[i], out[i + 1], out[i + 2])
        l2 = numbers["mean_original"] + numbers["contrast_scale"] * (l - numbers["mean_result"])
        k = 1 + w * (numbers["chroma_scale"] - 1)
        out[i:i + 3] = bytes(tone_check._from_lab(max(0.0, min(100.0, l + w * (l2 - l))), a * k, b * k))
    return pngio.Image(image.w, image.h, out)


def keep_joins(solid: Path, out: Path, width: int, height: int, joins: tuple[int, ...] = JOINS) -> None:
    """The wood weight with the rows around each strip join set to 0 (softly), so the join is the ORIGINAL's designed edge and the Codex wood is not cut
    by two independently generated zones."""
    draw = []
    for j in joins:
        draw += ["-draw", f"rectangle 0,{j - JOIN_KEEP} {width - 1},{j + JOIN_KEEP - 1}"]
    keep = solid.with_name("join-keep.png")
    run(["magick", "-size", f"{width}x{height}", "xc:white", "-fill", "black", *draw, "-blur", f"0x{JOIN_FEATHER}", "-colorspace", "Gray", str(keep)])
    run(["magick", str(solid), str(keep), "-compose", "Multiply", "-composite", str(out)])


def fill_notches(solid: Path, out: Path) -> None:
    """The wood weight with the notches of non-wood that are too narrow to hold a disk of NOTCH_RADIUS filled (a grey closing). The ORIGINAL's darkest wood
    stains are not wood class; left as holes they showed the original's dark smear against the sharp Codex wood as dark blotches."""
    run(["magick", str(solid), "-morphology", "Close", f"Disk:{NOTCH_RADIUS}", str(out)])


def bevel_weight(original: Path, solid: Path, work: Path, width: int, height: int, grow: int = BEVEL_GROW, band: tuple[int, int] | None = BEVEL_BAND) -> tuple[Path, int]:
    """A weight image (white = the ORIGINAL's pixels) over the light vertical bevel lines of the top strip's wood: tall thin components of pixels lighter than
    their surround, grown by BEVEL_GROW px. The Codex wood draws that line thinner (the wood weight is blurred and eroded, so a 5 px line is replaced) and
    loses the original's width. Returns the weight and the number of lines found."""
    gray, blur = work / "o-bevel-gray.png", work / "o-bevel-blur.png"
    run(["magick", str(original), "-alpha", "off", "-colorspace", "Gray", str(gray)])
    run(["magick", str(gray), "-blur", "0x5", str(blur)])
    light = work / "o-bevel-light.png"
    run(["magick", str(blur), str(gray), "-compose", "Minus", "-composite", "-threshold", f"{BEVEL_EXCESS / 255 * 100:.2f}%",
         "-morphology", "Close", "Rectangle:1x5", "-morphology", "Open", f"Rectangle:1x{BEVEL_OPEN}", str(light)])  # vertical runs only: the bottom bevel is not taken
    lit = gray_bytes(light)
    wood = gray_bytes(solid)
    top, bottom = band or (0, 0)
    points = {i for i, b in enumerate(lit) if b and wood[i] > 128 and top <= i // width < bottom}
    keep = bytearray(width * height)
    lines = 0
    for comp in components(points, width):
        xs = [q % width for q in comp]
        ys = [q // width for q in comp]
        if max(ys) - min(ys) + 1 < BEVEL_MIN_HEIGHT or max(xs) - min(xs) + 1 > BEVEL_MAX_WIDTH:
            continue
        lines += 1
        for y in range(min(ys), max(ys) + 1):          # the line's whole band, straight, so the original's width and its inner shadow come back whole
            for x in range(max(0, min(xs) - grow), min(width, max(xs) + grow + 1)):
                keep[y * width + x] = 255
    raw = work / "o-bevel-raw.png"
    out = work / "o-bevel.png"
    write_gray(bytes(keep), width, height, raw)
    run(["magick", str(raw), "-blur", "0x0.8", str(out)])
    return out, lines


def remove_chips(image: "pngio.Image", boxes: tuple | None = None) -> int:
    """In each CHIP_BOXES box, per row, the longest run of bright pixels (luma >= CHIP_LUMA), grown 2 px to the left, takes the darkest values of the CHIP_REACH
    pixels to its right (the plate's dark metal). Returns the number of pixels changed."""
    changed = 0
    w = image.w
    d = image.data
    if (image.w, image.h) != CHIP_MASTER:
        return 0
    for x0, y0, x1, y1 in (CHIP_BOXES if boxes is None else boxes):
        for y in range(y0, y1):
            runs: list[list[int]] = []
            for x in range(x0, x1):
                i = (y * w + x) * 4
                if 0.299 * d[i] + 0.587 * d[i + 1] + 0.114 * d[i + 2] >= CHIP_LUMA:
                    if runs and runs[-1][-1] == x - 1:
                        runs[-1].append(x)
                    else:
                        runs.append([x])
            if not runs:
                continue
            run_x = max(runs, key=len)
            edge = run_x[-1] + 1
            src = [(y * w + x) * 4 for x in range(edge + 1, min(edge + 1 + CHIP_REACH, w))]
            if len(run_x) < CHIP_MIN_RUN or not src:
                continue
            fill = [min(d[i + c] for i in src) for c in range(3)]      # the plate's darkest metal in reach: the rim's highlights are not copied
            if 0.299 * fill[0] + 0.587 * fill[1] + 0.114 * fill[2] >= CHIP_LUMA:
                continue                                               # no dark metal beside it: not the chip
            for x in range(run_x[0] - 2, run_x[-1] + 1):               # 2 px more on the wood side: the rim's soft edge
                for c in range(3):
                    d[(y * w + x) * 4 + c] = fill[c]
                changed += 1
    return changed


def erase_boxes(image: "pngio.Image", boxes: tuple) -> int:
    """Each box (x0, y0, x1, y1; x1, y1 exclusive) is filled with the per-channel median of the ERASE_RING px ring just outside it (inside the image): baked text or
    counters the game draws over the surface (condition 1). Returns the number of pixels filled; a box outside the image is an error."""
    filled = 0
    w, h, d = image.w, image.h, image.data
    for x0, y0, x1, y1 in boxes:
        if x1 > w or y1 > h:
            raise ComposeError(f"erase box {(x0, y0, x1, y1)} is outside the {w} x {h} px image")
        ring = [(x, y) for y in range(max(0, y0 - ERASE_RING), min(h, y1 + ERASE_RING)) for x in range(max(0, x0 - ERASE_RING), min(w, x1 + ERASE_RING))
                if not (x0 <= x < x1 and y0 <= y < y1)]
        if not ring:
            raise ComposeError(f"erase box {(x0, y0, x1, y1)} has no ring to take a fill from")
        fill = [sorted(d[(y * w + x) * 4 + c] for x, y in ring)[len(ring) // 2] for c in range(3)]
        for y in range(y0, y1):
            for x in range(x0, x1):
                i = (y * w + x) * 4
                d[i:i + 3] = bytes(fill)
                d[i + 3] = 255
        filled += (x1 - x0) * (y1 - y0)
    return filled


def lay_kit_parts(image: "pngio.Image", parts: tuple, base: Path | None = None) -> list[dict]:
    """The kit masters of `parts` (dicts id, file, x, y) laid over the image, straight alpha 'over', at master pixels; condition 3: the metal parts come from the kit
    and replace the original's stand-ins. A part that leaves the image, or is not RGBA-readable, is an error. Returns one record per part (id, file hash, size, place)."""
    records = []
    for part in parts:
        path = Path(part["file"])
        path = path if path.is_absolute() or base is None else base / path
        try:
            img = pngio.read(path)
        except (OSError, pngio.PngError) as error:
            raise ComposeError(f"kit part {part['id']}: cannot read {path}: {error}") from error
        x0, y0 = part["x"], part["y"]
        if x0 + img.w > image.w or y0 + img.h > image.h:
            raise ComposeError(f"kit part {part['id']} ({img.w} x {img.h} at {x0}, {y0}) leaves the {image.w} x {image.h} px image")
        for y in range(img.h):
            for x in range(img.w):
                s_i = (y * img.w + x) * 4
                a = img.data[s_i + 3]
                if a == 0:
                    continue
                d_i = ((y0 + y) * image.w + x0 + x) * 4
                for c in range(3):
                    image.data[d_i + c] = (img.data[s_i + c] * a + image.data[d_i + c] * (255 - a) + 127) // 255
                image.data[d_i + 3] = 255
        records.append({"id": part["id"], "file": str(path), "sha256": sha256_file(path), "size": [img.w, img.h], "at": [x0, y0]})
    return records


def sha256_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def compose(original: Path, a: Path, out: Path, work: Path, bevel_mode: str = BEVEL_MODE, spec: ZoneSpec = INVENTORY, kit_base: Path | None = None) -> dict:
    if bevel_mode not in ("lighten", "original"):
        raise ComposeError(f"unknown bevel mode {bevel_mode!r}")
    work.mkdir(parents=True, exist_ok=True)
    img_o = pngio.read(original)
    w, h = img_o.w, img_o.h
    if pngio.read(a).w != w:
        raise ComposeError("the original and the Codex result must have the same size")
    masks = tone_check.load_masks()
    solid, raw = work / "wood-solid.png", work / "wood-class.png"
    method_c.write_mask(original, solid, feather=MASK_FEATHER, solid=True, screen_id=spec.screen)
    method_c.write_mask(original, raw, screen_id=spec.screen)
    fill_notches(solid, solid)
    keep_joins(solid, solid, w, h, spec.joins)
    clean = work / "a-clean.png"
    specks = despeckle(a, clean, work, w, h)
    dots, dot_count = original_dots(original, raw, work, w, h)
    mask, coverage, cells = wood_cells(img_o, masks, spec.screen)
    target = wood_stats(img_o, mask, coverage, cells)
    before = wood_stats(pngio.read(clean), mask, coverage, cells)
    numbers = fit_numbers(target, before)
    fitted_img = apply_fit(pngio.read(clean), pngio.read(solid), numbers)  # pixels outside the wood weight are untouched, and replaced by the original below
    fitted = work / "a-fitted.png"
    pngio.write(fitted, fitted_img)
    # the Codex wood where the ORIGINAL is wood (soft edge); the ORIGINAL's rows of dots on top
    bevel, bevel_lines = bevel_weight(original, solid, work, w, h, BEVEL_GROW_LIGHTEN if bevel_mode == "lighten" else BEVEL_GROW, spec.bevel_band)
    run(["magick", str(original), "-alpha", "off", str(fitted), str(solid), "-composite", str(original), str(dots), "-composite", str(out)])
    if bevel_mode == "lighten":   # the Codex line stays sharp on top of the original's band: the lighter of the two per channel
        base = work / "bevel-base.png"
        run(["magick", str(out), str(base)])
        run(["magick", str(base), str(original), str(bevel), "-composite", str(out)])
        merged, wood_base, weight = pngio.read(out), pngio.read(base), gray_bytes(bevel)
        for p in (i for i, b in enumerate(weight) if b):
            for c in range(3):
                i = p * 4 + c
                merged.data[i] = max(merged.data[i], wood_base.data[i])
        pngio.write(out, merged)
    else:                         # the original's band replaces the Codex line
        run(["magick", str(out), str(original), str(bevel), "-composite", str(out)])
    result = pngio.read(out)
    chip_pixels = remove_chips(result, spec.chip_boxes)
    erased = erase_boxes(result, spec.erase_boxes)
    kit = lay_kit_parts(result, spec.kit_parts, kit_base)
    pngio.write(out, result)
    after = wood_stats(pngio.read(out), mask, coverage, cells)
    return {"out": str(out), "speck_components": specks, "dots_kept": dot_count, "bevel_lines": bevel_lines, "chip_pixels": chip_pixels, "erased_pixels": erased, "kit_parts": kit,
            "fit": numbers, "wood_target": target, "wood_before": before, "wood_after": after}


def evaluate_gates(seams: list[dict], tone: dict) -> dict:
    """The gates of a production surface: every join's seam ratio at most SEAM_TARGET (owner condition 4; SWI-VER-5's SEAM_GATE is recorded beside it, not a gate here:
    the original's own joins are 1.82 and 1.73) and the tone check of the whole screen (`tone_check.compare_screen`) passing."""
    seam_pass = bool(seams) and all(s["ratio"] <= SEAM_TARGET for s in seams)      # no join measured is no seam gate passed
    return {"seam_target": SEAM_TARGET, "seams_pass": seam_pass, "seams_within_swi_ver_5": bool(seams) and all(s["ratio"] <= SEAM_GATE for s in seams),
            "tone_pass": bool(tone["pass"]), "tone_failed": list(tone["failed"]), "passed": seam_pass and bool(tone["pass"])}


def provenance_record(spec: ZoneSpec, original: Path, a: Path, out: Path, info: dict, seams: list[dict], tone: dict, bevel_mode: str, when: datetime.datetime) -> dict:
    """The record of one composition (`provenance/surface/<zone id>.json`, local): hashes of the inputs and the result, the spec, what was kept, removed, erased and
    replaced by kit parts, the wood numbers, the seams, the tone check and the gates. It holds no pixels."""
    regions = {name: {"pass": r["pass"], "values": r.get("values")} for name, r in tone["regions"].items() if "absent" not in r}
    spec_dict = asdict(spec)
    return {"schema": SCHEMA, "tool": "surface_compose.py build", "date": when.strftime("%Y-%m-%dT%H:%M:%SZ"), "zone": spec.id, "spec": spec_dict, "bevel_mode": bevel_mode,
            "inputs": {"original": {"file": original.name, "sha256": sha256_file(original)}, "method_a": {"file": a.name, "sha256": sha256_file(a)}},
            "result": {"file": out.name, "sha256": sha256_file(out)},
            "counts": {"speck_components": info["speck_components"], "dots_kept": info["dots_kept"], "bevel_lines": info["bevel_lines"], "chip_pixels": info["chip_pixels"],
                       "erased_pixels": info["erased_pixels"]},
            "kit_parts": info["kit_parts"], "wood": {"target": info["wood_target"], "before": info["wood_before"], "after": info["wood_after"], "fit": info["fit"]},
            "seams": seams, "tone": {"screen": spec.screen, "pass": tone["pass"], "failed": tone["failed"], "regions": regions}, "gates": evaluate_gates(seams, tone)}


def build(original: Path, a: Path, out: Path, work: Path, provenance: Path, spec: ZoneSpec = INVENTORY, bevel_mode: str = BEVEL_MODE, kit_base: Path | None = None) -> dict:
    """The pipeline step: compose, measure the joins and the tone, write the record. The record is written whatever the gates say (`gates.passed`); the caller decides."""
    info = compose(original, a, out, work, bevel_mode, spec, kit_base)
    image = pngio.read(out)
    seams = [{"join": f"y={j}", "ratio": surface_metrics.join_seam(image, "y", j)} for j in spec.joins]
    tone = tone_check.compare_screen_files(spec.screen, original, out)
    record = provenance_record(spec, original, a, out, info, seams, tone, bevel_mode, datetime.datetime.now(datetime.timezone.utc))
    provenance.parent.mkdir(parents=True, exist_ok=True)
    provenance.write_text(json.dumps(record, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return record


def describe_build(record: dict, path: Path) -> str:
    g = record["gates"]
    seams = ", ".join(f"{s['join']} {s['ratio']:.2f}" for s in record["seams"]) or "none"
    w = record["wood"]
    return (f"SURFACE {'PASS' if g['passed'] else 'FAIL'} {record['zone']}: seams {seams} (target {g['seam_target']}, SWI-VER-5 1.5 {'met' if g['seams_within_swi_ver_5'] else 'not met'}); "
            f"tone {'PASS' if g['tone_pass'] else 'FAIL ' + ','.join(g['tone_failed'])}; wood L* {w['after']['mean_l']:.2f} (original {w['target']['mean_l']:.2f}), "
            f"contrast {w['after']['std_l']:.2f} ({w['target']['std_l']:.2f}); {record['counts']['erased_pixels']} px erased, {len(record['kit_parts'])} kit parts; record {path}")


def describe(info: dict) -> str:
    f = lambda s: f"L* {s['mean_l']:.2f}, contrast {s['std_l']:.2f}, chroma {s['mean_chroma']:.2f}"
    return (f"COMPOSE wrote {info['out']}; {info['speck_components']} specks removed pointwise, {info['dots_kept']} dots of the original's rows kept, "
            f"{info['bevel_lines']} bevel lines of the original put back, {info['chip_pixels']} chip pixels removed\n"
            f"  wood original: {f(info['wood_target'])}\n  wood Codex clean: {f(info['wood_before'])}\n  wood composed: {f(info['wood_after'])}")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("compose", "build"):
        p = sub.add_parser(name)
        p.add_argument("--original", type=Path, required=True)
        p.add_argument("--a", type=Path, required=True, help="method A assembled to the master size (2880 x 2160)")
        p.add_argument("--out", type=Path, required=True)
        p.add_argument("--work", type=Path, required=True)
        p.add_argument("--bevel", choices=("lighten", "original"), default=BEVEL_MODE, help="how the original's light vertical bevel lines are put back")
        p.add_argument("--spec", type=Path, help="a zone spec (JSON); default the inventory screen")
        p.add_argument("--kit-base", type=Path, help="the folder relative kit part files are read from")
        if name == "build":
            p.add_argument("--provenance", type=Path, help="the record to write; default local/steamwoven-ui/provenance/surface/<zone id>.json")
    args = parser.parse_args(argv)
    try:
        spec = load_spec(args.spec) if args.spec else INVENTORY
        if args.command == "compose":
            print(describe(compose(args.original, args.a, args.out, args.work, args.bevel, spec, args.kit_base)))
            return 0
        provenance = args.provenance or default_provenance_path(spec)
        record = build(args.original, args.a, args.out, args.work, provenance, spec, args.bevel, args.kit_base)
    except (ComposeError, method_c.MethodCError, tone_check.ToneError, surface_metrics.MetricError, pngio.PngError, KeyError, OSError) as error:
        print(f"SURFACE COMPOSE ERROR: {error!r}", file=sys.stderr)
        return 2
    print(describe_build(record, provenance))
    return 0 if record["gates"]["passed"] else 1


def default_provenance_path(spec: ZoneSpec) -> Path:
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    from prompt_library import default_local_root  # noqa: PLC0415
    return default_local_root() / "provenance" / "surface" / f"{spec.id}.json"


if __name__ == "__main__":
    raise SystemExit(main())
