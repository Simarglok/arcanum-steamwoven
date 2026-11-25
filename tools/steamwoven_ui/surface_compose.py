"""ARC-RES-2457 method A as the player sees it (owner pick 2026-11-21): the Codex result (`surface_study.py`, zones as single images) is taken for the WOOD only.
Everything that is not wood (text, counters, digits, fields filled in play, portrait circle, grids, brass and iron parts, buttons) is the ORIGINAL's, by the
ORIGINAL's mask, as a stand-in for the kit parts; the wood of the Codex result is cleaned of its single dark specks (pointwise), the original's rows of dots and, in the top strip, its light vertical
bevel lines are put back, notches of the wood mask are filled, the white chip at the right plate's tip is removed (ARC-UI-3104), and its tone is fitted to the
ORIGINAL's wood (mean L*, contrast, chroma). Needs ImageMagick (`magick`). Local files only, no art in git.

    python3 -B tools/steamwoven_ui/surface_compose.py compose --original O.png --a A.png --out OUT.png --work DIR [--bevel lighten|original]
"""
from __future__ import annotations

import argparse
import copy
import datetime
import hashlib
import json
import math
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
ISLAND_ERODE = 6          # px: ARC-UI-3164: the weight is eroded by this before the islands are counted (a bay joined to the wood by a few pixels becomes an island) and grown back after
ISLAND_CONNECTIVITY = 4   # ARC-UI-3164: connected-components rule of the wood weight's island removal
FILL_BRIGHT = 105         # 0..255 luma of the ORIGINAL: letters, brass and paper are lighter than this, the wood of a stats panel is not (panel wood: 90 % under 75)
FILL_DARK = 14            # ... and black fields are darker than this
FILL_DILATE = 4           # px: ARC-UI-3164: the items (letters, plates, fields) inside a fill box are grown by this much before the wood is put between them
FILL_CLOSE = 4           # px: ... and closed by this, so strokes of a letter and a plate's rim and field are one item, never wood in between
ERASE_RING = 2            # px: the ring just outside an erase box whose per-channel median fills it
TEXT_DELTA = 35           # ARC-UI-3165: 0..255 luma distance from the ground's (the ring outside the box) that makes a pixel text (strokes, digits, signs on a field or on paper)
TEXT_SIGMAS = 3.0         # the text band is at least this many robust standard deviations (1.4826 x the median deviation) of the ring's luma wide (mottled paper), TEXT_DELTA at the least
TEXT_PAPER_WINDOW = 21    # px: the closing of a paper box removes dark strokes narrower than this (letters, a strike-through, a page number)
TEXT_PAPER_GROW = 3       # px: a paper's text is dense, so its halo is taken narrowly (the box would be all text otherwise)
TEXT_PAPER_DELTA = 28     # a stroke is this much darker, in red or in green, than the closing of that channel
TEXT_RING = 3             # px: the ring just outside a text box whose median luma is the ground's
TEXT_GROWS = (8, 6, 4, 3, 2)   # px: a text pixel's soft halo is taken with it (the mask is grown by this much, in a square; the widest that leaves TEXT_MIN_GROUND of the box)
TEXT_MIN_GROUND = 0.2     # share of a box that must stay ground after the halo, or the next narrower halo is taken
TEXT_RADII = (8, 16, 32, 64)   # px: the windows (radius) of the fill from the pixels that are no text; a wider one only for pixels the narrower left without a neighbour
TEXT_MIN_SHARE = 0.12     # a fill window holds at least this share of ground pixels (and TEXT_MIN_KNOWN of them) or the next, wider one is taken: no fill from a few stray pixels
TEXT_MIN_KNOWN = 8
TEXT_SMOOTH = 3           # px: radius of the one smoothing pass over the filled pixels
TEXT_GRAIN = 3            # px: radius of the neighbourhood the grain of a ground pixel is measured against
TEXT_GRAIN_STEPS = ((23, 0), (-23, 0), (0, 29), (0, -29), (47, 31), (-47, 31), (47, -31), (-47, -31), (95, 0), (-95, 0), (0, 95), (0, -95))
TEXT_MAX_SHARE = 0.8      # a box whose text (before the halo) covers more than this share is not a box of text on a ground: an error, nothing is guessed
SCHEMA = "steamwoven-surface/1"
SAFE_ID = re.compile(r"[A-Za-z0-9][A-Za-z0-9._-]*")   # a zone id is a file name: no path parts, no leading dot


@dataclass(frozen=True)
class ZoneSpec:
    """One composition of method A (ARC-UI-3100): a whole screen of the master size (2880 x 2160 for the contexts), its tone-mask screen, the strip joins, the band
    of the light bevel lines, the chip boxes (inventory only), `erase_boxes` (x0, y0, x1, y1: baked text or counters the game draws over the surface; filled by the median
    of the ring outside the box) and `kit_parts` (dicts `id`, `file`, `x`, `y`: a kit master laid over the result at master pixels, replacing the original's stand-in).
    ARC-UI-3164 added `island_min` (drop wood-weight islands smaller than this many pixels, so a leak of the wood class into metal or a page edge is the original's again),
    `fill_boxes` (boxes whose wood between the items the tone mask excludes is the Codex wood as well, so the original's lighter wood is no patch) and `dot_bands`
    (the rows where the original's rows of dots are kept: the strips; in the window the faint grain specks chain into false rows of dots).
    ARC-UI-3165 (owner condition 1) added `text_boxes` (x0, y0, x1, y1: text, digits and counters the game draws over a ground that is no flat colour, a paper, a pit with a
    gradient, a disc: only the pixels that differ from the box's median luma by TEXT_DELTA, grown by up to TEXT_GROWS[0] px, are replaced, by the mean of the box's other pixels near them,
    so the ground's own shading stays), `paper_boxes` (the same for dark text on a paper whose light changes across the box: the text is what a closing of the channels
    removes, TEXT_PAPER_WINDOW px wide) and `wood_boxes` (boxes whose wood weight is 1 throughout: letters baked into the wood, the Codex wood shows there)."""
    id: str
    screen: str = method_c.SCREEN
    joins: tuple[int, ...] = JOINS
    bevel_band: tuple[int, int] | None = BEVEL_BAND
    chip_boxes: tuple[tuple[int, int, int, int], ...] = ()
    erase_boxes: tuple[tuple[int, int, int, int], ...] = ()
    kit_parts: tuple[dict, ...] = ()
    island_min: int = 0                                          # ARC-UI-3164: wood-weight islands under this many pixels are dropped (the ORIGINAL's pixels stand there): metal and paper edges the class leaks into
    fill_boxes: tuple[tuple[int, int, int, int], ...] = ()       # ARC-UI-3164: master-pixel boxes whose wood between the items (letters, plates, fields) is the Codex wood too, not the ORIGINAL's patch
    dot_bands: tuple[tuple[int, int], ...] | None = None         # ARC-UI-3164: rows (top, bottom) where the ORIGINAL's rows of dots are kept; None = everywhere
    wood_boxes: tuple[tuple[int, int, int, int], ...] = ()       # ARC-UI-3165: master-pixel boxes where the Codex wood shows whole: the letters baked into the wood go
    text_boxes: tuple[tuple[int, int, int, int], ...] = ()       # ARC-UI-3165: master-pixel boxes whose text pixels are replaced by their ground (black fields, discs, gradient pits, flat parchment)
    paper_boxes: tuple[tuple[int, int, int, int], ...] = ()      # ARC-UI-3165: master-pixel boxes of dark text on a shaded paper (the book pages): the text is found against the paper's own local light


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
    unknown = set(raw) - {"id", "screen", "joins", "bevel_band", "chip_boxes", "erase_boxes", "kit_parts", "island_min", "fill_boxes", "dot_bands", "wood_boxes", "text_boxes", "paper_boxes"}
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
    island_min = raw.get("island_min", 0)
    if not (_is_int(island_min) and island_min >= 0):
        raise ComposeError(f"spec {path}: island_min is a non-negative integer")
    bands_raw = raw.get("dot_bands")
    bands = None
    if bands_raw is not None:
        if not (isinstance(bands_raw, list) and all(isinstance(b, list) and len(b) == 2 and all(_is_int(v) for v in b) and 0 <= b[0] < b[1] for b in bands_raw)):
            raise ComposeError(f"spec {path}: dot_bands is null or a list of [top, bottom] rows")
        bands = tuple(tuple(b) for b in bands_raw)
    return ZoneSpec(id=raw["id"], screen=raw["screen"], joins=tuple(joins), bevel_band=tuple(band) if band else None, chip_boxes=boxes("chip_boxes"),
                    erase_boxes=boxes("erase_boxes"), kit_parts=tuple(parts), island_min=island_min, fill_boxes=boxes("fill_boxes"), dot_bands=bands,
                    wood_boxes=boxes("wood_boxes"), text_boxes=boxes("text_boxes"), paper_boxes=boxes("paper_boxes"))


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


def original_dots(original: Path, wood_class_mask: Path, work: Path, width: int, height: int, bands: tuple[tuple[int, int], ...] | None = None) -> tuple[Path, int]:
    """A mask of the ORIGINAL's rows of dots inside its wood (the dots grown by 1 px), written as a grey PNG; also the number of dots kept. `bands`: only dots in
    these rows (top, bottom) count (None = every row)."""
    specks = speck_map(original, DOT_THRESHOLD, width, height, work, "o")
    wood = gray_bytes(wood_class_mask)
    points = {i for i, b in enumerate(specks) if b and wood[i] > 128 and (bands is None or any(top <= i // width < bottom for top, bottom in bands))}
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


def _grow(mask: bytearray, width: int, height: int, radius: int) -> bytearray:
    """The mask grown by `radius` px in a square (a separable window count over prefix sums)."""
    rows = bytearray(width * height)
    for j in range(height):
        row = mask[j * width:(j + 1) * width]
        prefix = [0]
        for v in row:
            prefix.append(prefix[-1] + v)
        base = j * width
        for i in range(width):
            if prefix[min(width, i + radius + 1)] - prefix[max(0, i - radius)]:
                rows[base + i] = 1
    out = bytearray(width * height)
    for i in range(width):
        column = rows[i::width]
        prefix = [0]
        for v in column:
            prefix.append(prefix[-1] + v)
        for j in range(height):
            if prefix[min(height, j + radius + 1)] - prefix[max(0, j - radius)]:
                out[j * width + i] = 1
    return out


def _closing(channels: list[list[int]], width: int, height: int, window: int) -> list[list[int]]:
    """The grey-scale closing of each channel by a square of `window` px (ImageMagick's morphology on the raw RGB bytes)."""
    raw = bytearray(width * height * 3)
    for c in range(3):
        raw[c::3] = bytes(channels[c])
    try:
        body = subprocess.run(["magick", "-size", f"{width}x{height}", "-depth", "8", "rgb:-", "-morphology", "Close", f"Square:{window}", "-depth", "8", "rgb:-"],
                              input=bytes(raw), capture_output=True)
    except OSError as error:
        raise ComposeError(f"the closing of the paper box needs ImageMagick (magick): {error}") from error
    if body.returncode != 0 or len(body.stdout) != width * height * 3:
        raise ComposeError(f"the closing of the paper box failed: {body.stderr.decode('utf-8', 'replace').strip()[:200]}")
    return [list(body.stdout[c::3]) for c in range(3)]

def _grain(i: int, width: int, height: int, known: list[int], counts: list[int], sums: list[list[int]], channels: list[list[int]], stride: int) -> tuple[int, int, int]:
    """The ground's own fine texture for a filled pixel: the deviation of a ground pixel from its TEXT_GRAIN-radius neighbourhood, taken from the first of TEXT_GRAIN_STEPS (fixed
    offsets, nearest first) that lands on a ground pixel; (0, 0, 0) when none does, so a flat ground stays flat and no texture is made up."""
    j, k = divmod(i, width)
    for dx, dy in TEXT_GRAIN_STEPS:
        jj, kk = j + dy, k + dx
        if not (0 <= jj < height and 0 <= kk < width) or not known[jj * width + kk]:
            continue
        ya, yb, xa, xb = max(0, jj - TEXT_GRAIN), min(height, jj + TEXT_GRAIN + 1), max(0, kk - TEXT_GRAIN), min(width, kk + TEXT_GRAIN + 1)
        n = counts[yb * stride + xb] - counts[ya * stride + xb] - counts[yb * stride + xa] + counts[ya * stride + xa]
        at = jj * width + kk
        return tuple(channels[c][at] - (sums[c][yb * stride + xb] - sums[c][ya * stride + xb] - sums[c][yb * stride + xa] + sums[c][ya * stride + xa]) // n for c in range(3))
    return (0, 0, 0)


def _integral(values, width: int, height: int) -> list[int]:
    """A summed-area table of `values` (a flat list of width * height), (width + 1) * (height + 1) entries."""
    stride = width + 1
    table = [0] * (stride * (height + 1))
    for j in range(height):
        running = 0
        row, above = (j + 1) * stride, j * stride
        base = j * width
        for i in range(width):
            running += values[base + i]
            table[row + i + 1] = table[above + i + 1] + running
    return table


def erase_text(image: "pngio.Image", boxes: tuple, paper: bool = False) -> int:
    """ARC-UI-3165 (owner condition 1): in each box (x0, y0, x1, y1; x1, y1 exclusive) the text, digits and counters the game draws over the surface are replaced by their ground.
    The ground's luma is the median of the TEXT_RING px ring outside the box (digits can fill half a box, so the box's own median would be text); a pixel is text when its luma
    differs from it by more than TEXT_DELTA (light text on a black field or a disc, dark text on paper), the mask is grown by the widest of TEXT_GROWS that leaves TEXT_MIN_GROUND of the box (the soft halo), and each text pixel
    of the box takes the mean colour of the ground pixels (the box's and the ring's that are no text) in a window of TEXT_RADII (the narrowest that holds one), so a paper's or
    a pit's shading stays and nothing is painted that was not there. With `paper` the text is dark on a paper whose light changes across the box (the page's shading toward the spine
    is no text): a pixel is text when its luma is TEXT_PAPER_DELTA under the closing (TEXT_PAPER_WINDOW px) of the channel in the red or the green channel (every ink is dark there, blue ink is not in blue). Returns the pixels replaced.
    A box outside the image, or one that is mostly text, is an error."""
    w, h, d = image.w, image.h, image.data
    replaced = 0
    for x0, y0, x1, y1 in boxes:
        if x1 > w or y1 > h:
            raise ComposeError(f"text box {(x0, y0, x1, y1)} is outside the {w} x {h} px image")
        ex0, ey0, ex1, ey1 = max(0, x0 - TEXT_RING), max(0, y0 - TEXT_RING), min(w, x1 + TEXT_RING), min(h, y1 + TEXT_RING)
        ew, eh = ex1 - ex0, ey1 - ey0
        channels = [[], [], []]
        for j in range(eh):
            base = ((ey0 + j) * w + ex0) * 4
            row = d[base:base + ew * 4]
            for c in range(3):
                channels[c].extend(row[c::4])
        luma = [(r * 299 + g * 587 + b * 114) // 1000 for r, g, b in zip(*channels)]
        inside = [(j, k) for j in range(y0 - ey0, y1 - ey0) for k in range(x0 - ex0, x1 - ex0)]
        ring = [luma[j * ew + k] for j in range(eh) for k in range(ew) if not (x0 - ex0 <= k < x1 - ex0 and y0 - ey0 <= j < y1 - ey0)]
        sample = ring or [luma[j * ew + k] for j, k in inside]       # the ground is read from the ring outside the box: digits can fill half the box
        ground = sorted(sample)[len(sample) // 2]
        if paper:
            closed = _closing(channels, ew, eh, TEXT_PAPER_WINDOW)
            core = bytearray(1 if max(cr - r, cg - g) > TEXT_PAPER_DELTA else 0
                             for cr, cg, r, g in zip(closed[0], closed[1], channels[0], channels[1]))      # red and green: every ink of a page is dark there (blue ink is bright in blue)
        else:
            spread = 1.4826 * sorted(abs(v - ground) for v in sample)[len(sample) // 2]    # a mottled ground needs a wider band than a black field (the median deviation: a needle or a tick in the ring does not widen it)
            delta = max(TEXT_DELTA, TEXT_SIGMAS * spread)
            core = bytearray(1 if abs(v - ground) > delta else 0 for v in luma)
        share = sum(core[j * ew + k] for j, k in inside) / len(inside)
        if share > TEXT_MAX_SHARE:
            raise ComposeError(f"text box {(x0, y0, x1, y1)}: {share * 100:.0f} % of the box differs from the ground, it is no text on a ground")
        for grow in ((TEXT_PAPER_GROW,) if paper else TEXT_GROWS):           # the widest halo that leaves enough ground in the box to fill from
            mask = _grow(core, ew, eh, grow)
            if sum(mask[j * ew + k] for j, k in inside) <= (1 - TEXT_MIN_GROUND) * len(inside):
                break
        todo = [j * ew + k for j, k in inside if mask[j * ew + k]]
        if not todo:
            continue
        known = [1 - m for m in mask]
        counts = _integral(known, ew, eh)
        sums = [_integral([v * k for v, k in zip(channels[c], known)], ew, eh) for c in range(3)]
        stride = ew + 1
        fill: dict[int, tuple[int, int, int]] = {}
        pending = todo
        for radius in TEXT_RADII:
            left = []
            need = max(TEXT_MIN_KNOWN, int(TEXT_MIN_SHARE * (2 * radius + 1) ** 2))
            for i in pending:
                j, k = divmod(i, ew)
                ya, yb, xa, xb = max(0, j - radius), min(eh, j + radius + 1), max(0, k - radius), min(ew, k + radius + 1)
                n = counts[yb * stride + xb] - counts[ya * stride + xb] - counts[yb * stride + xa] + counts[ya * stride + xa]
                if n < need and radius != TEXT_RADII[-1]:
                    left.append(i)
                    continue
                if not n:
                    raise ComposeError(f"text box {(x0, y0, x1, y1)}: no ground pixel within {TEXT_RADII[-1]} px of a text pixel")
                fill[i] = tuple((sums[c][yb * stride + xb] - sums[c][ya * stride + xb] - sums[c][yb * stride + xa] + sums[c][ya * stride + xa] + n // 2) // n for c in range(3))
            pending = left
            if not pending:
                break
        # the windows differ in size from pixel to pixel: one smoothing pass over the filled pixels (the ground's pixels stay as they were) takes the steps out
        for i, rgb in fill.items():
            j, k = divmod(i, ew)
            for c in range(3):
                channels[c][i] = rgb[c]
        total = [_integral(channels[c], ew, eh) for c in range(3)]
        for i in fill:
            j, k = divmod(i, ew)
            ya, yb, xa, xb = max(0, j - TEXT_SMOOTH), min(eh, j + TEXT_SMOOTH + 1), max(0, k - TEXT_SMOOTH), min(ew, k + TEXT_SMOOTH + 1)
            n = (yb - ya) * (xb - xa)
            at = ((ey0 + j) * w + ex0 + k) * 4
            grain = _grain(i, ew, eh, known, counts, sums, channels, stride)
            for c in range(3):
                t = total[c]
                value = (t[yb * stride + xb] - t[ya * stride + xb] - t[yb * stride + xa] + t[ya * stride + xa] + n // 2) // n + grain[c]
                d[at + c] = 0 if value < 0 else 255 if value > 255 else value
            d[at + 3] = 255
        replaced += sum(1 for j, k in inside if mask[j * ew + k])
    return replaced


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


def remove_islands(solid: Path, out: Path, min_area: int) -> None:
    """The wood weight without its white islands smaller than `min_area` pixels (ImageMagick connected components): a class leak inside a gear, a plate or a book's page
    edge is a blob of a few thousand pixels, real wood is a region of hundreds of thousands. The weight is eroded by ISLAND_ERODE first, so a bay that opens to the wood through
    a few pixels is an island too, and grown back by the same disk inside the original weight. Only white is removed, never a hole of the wood."""
    if min_area <= 0:
        return
    binary, kept, dropped = solid.with_name("island-bin.png"), solid.with_name("island-kept.png"), solid.with_name("island-dropped.png")
    run(["magick", str(solid), "-colorspace", "Gray", "-threshold", "50%", "-morphology", "Erode", f"Disk:{ISLAND_ERODE}", str(binary)])
    run(["magick", str(binary), "-define", f"connected-components:area-threshold={min_area}", "-define", "connected-components:mean-color=true",
         "-connected-components", str(ISLAND_CONNECTIVITY), "-colorspace", "Gray", str(kept)])
    # only the dropped islands (white in the eroded weight, black after the count) are taken out, grown back and feathered: the rest of the soft weight, its edges included, is untouched
    run(["magick", str(binary), str(kept), "-compose", "Difference", "-composite", "-morphology", "Dilate", f"Disk:{ISLAND_ERODE + 2}", "-blur", f"0x{MASK_FEATHER}", "-negate", str(dropped)])
    run(["magick", str(solid), str(dropped), "-colorspace", "Gray", "-compose", "Multiply", "-composite", str(out)])


def fill_between_items(original: Path, solid: Path, boxes: tuple, out: Path, width: int, height: int) -> None:
    """ARC-UI-3164: in each fill box the wood weight is 1 wherever no item is: an item is an ORIGINAL pixel lighter than FILL_BRIGHT or darker than FILL_DARK in luma (letters,
    brass rims, black fields), grown by FILL_DILATE, closed by FILL_CLOSE and filled inside (a knob in a rim), all inside the box only. The tone mask excludes these boxes
    whole, which left the ORIGINAL's lighter panel wood there as a patch with a sharp edge against the Codex wood (the colour class cannot be used: the panel wood is no wood class)."""
    if not boxes:
        return
    draw = []
    for x0, y0, x1, y1 in boxes:
        draw += ["-draw", f"rectangle {x0},{y0} {x1 - 1},{y1 - 1}"]
    inside, items, protect, fill = (solid.with_name(n) for n in ("fill-box.png", "fill-items.png", "fill-protect.png", "fill-weight.png"))
    run(["magick", "-size", f"{width}x{height}", "xc:black", "-fill", "white", *draw, "-colorspace", "Gray", str(inside)])
    bright, dark = f"{FILL_BRIGHT / 255 * 100:.2f}", f"{FILL_DARK / 255 * 100:.2f}"
    run(["magick", str(original), "-alpha", "off", "-colorspace", "Gray", "(", "+clone", "-threshold", f"{bright}%", ")", "(", "-clone", "0", "-threshold", f"{dark}%", "-negate", ")",
         "-delete", "0", "-compose", "Lighten", "-composite", str(inside), "-compose", "Multiply", "-composite",
         "-morphology", "Dilate", f"Disk:{FILL_DILATE}", "-morphology", "Close", f"Disk:{FILL_CLOSE}", str(items)])
    # holes of the items (a knob inside a rim): the background reached from the corner is outside; whatever black is not reached is inside an item
    # (a black 1 px border first, so the corner pixel the flood starts from is background even when a box and an item touch the image corner)
    run(["magick", str(items), "-bordercolor", "black", "-border", "1", "-fill", "gray50", "-draw", "color 0,0 floodfill", "-fill", "white", "-opaque", "black", "-fill", "black", "-opaque", "gray50",
         "-shave", "1x1", str(protect)])
    run(["magick", str(protect), "-negate", str(inside), "-compose", "Multiply", "-composite", "-blur", f"0x{MASK_FEATHER}", str(fill)])
    run(["magick", str(solid), str(fill), "-colorspace", "Gray", "-compose", "Lighten", "-composite", str(out)])


def force_wood(solid: Path, boxes: tuple, out: Path, width: int, height: int) -> None:
    """ARC-UI-3165: in each wood box the wood weight is 1 (feathered at the box's edge as the rest of the weight): letters baked into the wood (the stat names of the character
    sheet) are not kept as items, so the Codex wood shows in their place and no patch of the original's wood or flat fill stands there. A box outside the image is an error."""
    if not boxes:
        return
    draw = []
    for x0, y0, x1, y1 in boxes:
        if x1 > width or y1 > height:
            raise ComposeError(f"wood box {(x0, y0, x1, y1)} is outside the {width} x {height} px image")
        draw += ["-draw", f"rectangle {x0},{y0} {x1 - 1},{y1 - 1}"]
    run(["magick", str(solid), "(", "-size", f"{width}x{height}", "xc:black", "-fill", "white", *draw, "-colorspace", "Gray", "-blur", f"0x{MASK_FEATHER}", ")",
         "-colorspace", "Gray", "-compose", "Lighten", "-composite", str(out)])


def compose(original: Path, a: Path, out: Path, work: Path, bevel_mode: str = BEVEL_MODE, spec: ZoneSpec = INVENTORY, kit_base: Path | None = None) -> dict:
    if bevel_mode not in ("lighten", "original"):
        raise ComposeError(f"unknown bevel mode {bevel_mode!r}")
    work.mkdir(parents=True, exist_ok=True)
    img_o = pngio.read(original)
    w, h = img_o.w, img_o.h
    for kind in ("erase_boxes", "text_boxes", "paper_boxes", "wood_boxes"):       # a box outside the image is found before the minutes of work, not after
        for x0, y0, x1, y1 in getattr(spec, kind):
            if x1 > w or y1 > h:
                raise ComposeError(f"{kind[:-1].replace('_', ' ')} {(x0, y0, x1, y1)} is outside the {w} x {h} px image")
    if pngio.read(a).w != w:
        raise ComposeError("the original and the Codex result must have the same size")
    masks = tone_check.load_masks()
    solid, raw = work / "wood-solid.png", work / "wood-class.png"
    method_c.write_mask(original, solid, feather=MASK_FEATHER, solid=True, screen_id=spec.screen)
    method_c.write_mask(original, raw, screen_id=spec.screen)
    fill_notches(solid, solid)
    remove_islands(solid, solid, spec.island_min)
    if spec.fill_boxes:
        fill_between_items(original, solid, spec.fill_boxes, solid, w, h)
    force_wood(solid, spec.wood_boxes, solid, w, h)
    keep_joins(solid, solid, w, h, spec.joins)
    clean = work / "a-clean.png"
    specks = despeckle(a, clean, work, w, h)
    dots, dot_count = original_dots(original, raw, work, w, h, spec.dot_bands)
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
    text = erase_text(result, spec.text_boxes) + erase_text(result, spec.paper_boxes, paper=True)
    kit = lay_kit_parts(result, spec.kit_parts, kit_base)
    pngio.write(out, result)
    after = wood_stats(pngio.read(out), mask, coverage, cells)
    return {"out": str(out), "speck_components": specks, "dots_kept": dot_count, "bevel_lines": bevel_lines, "chip_pixels": chip_pixels, "erased_pixels": erased, "text_pixels": text, "kit_parts": kit,
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
                       "erased_pixels": info["erased_pixels"], "text_pixels": info["text_pixels"]},
            "kit_parts": info["kit_parts"], "wood": {"target": info["wood_target"], "before": info["wood_before"], "after": info["wood_after"], "fit": info["fit"]},
            "seams": seams, "tone": {"screen": spec.screen, "pass": tone["pass"], "failed": tone["failed"], "regions": regions}, "gates": evaluate_gates(seams, tone)}


def tone_masks_for(spec: ZoneSpec, width: int = 2880, height: int = 2160) -> dict | None:
    """The tone masks of the check with the spec's text and paper boxes added to the screen's excluded content (ARC-UI-3165): the text the game draws is taken out of the
    surface on purpose, so the tone of the paper and of the black fields is measured where the surface keeps the original's own (a page with its text erased would read as a
    brighter paper otherwise). Boxes are master pixels of a `width` x `height` image (the contexts' 2880 x 2160); the masks are the 800 x 600 layout, so a box is divided by the layout scale (3.6 px a unit for the contexts) and rounded outward.
    None (the file's masks as they are) when the spec has no such box."""
    boxes = spec.text_boxes + spec.paper_boxes
    if not boxes:
        return None
    masks = copy.deepcopy(tone_check.load_masks())
    if spec.screen not in masks["screens"]:
        return None                                       # the check names the missing screen itself
    sx, sy = width / tone_check.TARGET_W, height / tone_check.TARGET_H
    masks["screens"][spec.screen].setdefault("exclude", []).extend(
        [math.floor(x0 / sx), math.floor(y0 / sy), math.ceil(x1 / sx), math.ceil(y1 / sy)] for x0, y0, x1, y1 in boxes)
    return masks


def build(original: Path, a: Path, out: Path, work: Path, provenance: Path, spec: ZoneSpec = INVENTORY, bevel_mode: str = BEVEL_MODE, kit_base: Path | None = None) -> dict:
    """The pipeline step: compose, measure the joins and the tone, write the record. The record is written whatever the gates say (`gates.passed`); the caller decides."""
    info = compose(original, a, out, work, bevel_mode, spec, kit_base)
    image = pngio.read(out)
    seams = [{"join": f"y={j}", "ratio": surface_metrics.join_seam(image, "y", j)} for j in spec.joins]
    tone = tone_check.compare_screen_files(spec.screen, original, out, tone_masks_for(spec, image.w, image.h))
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
            f"contrast {w['after']['std_l']:.2f} ({w['target']['std_l']:.2f}); {record['counts']['erased_pixels']} px erased, {record['counts']['text_pixels']} text px replaced, {len(record['kit_parts'])} kit parts; record {path}")


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
