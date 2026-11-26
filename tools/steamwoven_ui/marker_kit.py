"""ARC-UI-3207: the kit parts of B-BUTTONS slice 7: the map markers (arts 141 to 144, 204 to 206, 814) and the covers of the empty worn slots (241 to 249).

    python3 -B tools/steamwoven_ui/marker_kit.py plan
    python3 -B tools/steamwoven_ui/marker_kit.py codex-inbox                       the two Codex batches (inboxes, prompts, references)
    python3 -B tools/steamwoven_ui/marker_kit.py build [--only NAMES] [--out DIR] [--dry-run]
    python3 -B tools/steamwoven_ui/marker_kit.py sheet --out PNG [--parts DIR]      the composite original | new
    python3 -B tools/steamwoven_ui/marker_kit.py self-test

Where each part comes from is the owner's pick (2026-11-26):
  pack    the covers of the helmet, boots, gauntlets, medallion and both rings: the old HD pack's frame with its baked grid lines and black ground removed;
  codex   the covers of the armour, shield and weapon (the pack drew solid objects there, #3105): a flat grey silhouette with a light engraving, the shape of
          the original, in the style of the accepted pack covers; and the markers 141 to 144 and 204, drawn together on one sheet;
  rule    the round markers 205, 206 and 814 (true circles of the original's radii and colours), and the second frame of arts 204, 205 and 206: the first
          frame with a yellow contour of one thickness round it. The two frames of art 814 are one picture in the original, so its second part is the first.
The skull of art 144 on the accepted sheet frowned; the owner asked for the original's neutral hollow eyes, so it is redrawn by an item of its own
(`skull-neutral`, an edit of the sheet's cell) and read from that picture.
A cover is a translucent grey overlay on a transparent ground, so the kit's grid layer (`grid.inventory_cells`) draws the grid and shows through it as in
the original. The originals, the pack and the generated sheets are local and never committed.
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
    from . import button_kit, kit_check, pngio, small_kit
except ImportError:  # run as a script
    sys.path.insert(0, str(HERE))
    import button_kit  # type: ignore
    import kit_check  # type: ignore
    import pngio  # type: ignore
    import small_kit  # type: ignore

Image = pngio.Image
TOOL_VERSION = 2
PACK_SCALE = small_kit.PACK_SCALE
SS = 4                      # the analytic shapes are sampled SS x SS times per master pixel
YELLOW = (255, 246, 0)      # the original's highlight
RING = 4                    # master px: the width of the highlight contour (one original pixel is 3.6)
LIT_PAD = 6                 # master px added on each side of a highlighted frame
SHEET_CELL = 512            # the marker sheet: 3 x 2 cells of this side
SHEET_ZOOM = 22             # the original marker pixels on the reference sheet, one scale for all so the outlines come out one thickness
COVER_ZOOM = 8              # the original cover cells on the reference picture
REFERENCE_LEVEL = 120       # the grey of a silhouette on the Codex references (the originals' 41 is too dark to read)
GROUND_SLACK = 4            # a grey this close above the black ground is ground
SILHOUETTE_SIGMA = 0.45     # in original cells: the smoothing of a silhouette's outline taken from the original
MARKER_BATCH, COVER_BATCH = "B-BUTTONS-3207-markers", "B-BUTTONS-3207-covers"
MARKER_ITEM = "marker-sheet"
REDRAWN = {"mm_skull": "skull-neutral"}   # markers read from an item of their own instead of their cell of the sheet


class MarkerError(Exception):
    pass


@dataclass(frozen=True)
class Art:
    name: str                # the part's name: `glyph.<name>` and, for a second frame, `glyph.<name>_lit`
    art: int
    folder: str              # the census frame folder
    pack_name: str           # the art's name in the pack's index
    family: str              # "marker" | "cover" (an empty worn slot)
    source: str              # where the first frame comes from: "pack" | "codex" | "rule"
    units: tuple[int, int]   # original units of the first frame
    frames: int


def _a(name: str, art: int, file: str, family: str, source: str, units: tuple[int, int], frames: int = 1) -> Art:
    return Art(name, art, f"{art}-{file}", file, family, source, units, frames)


ARTS = (
    _a("mm_chest", 141, "mm_chest", "marker", "codex", (16, 18)),
    _a("mm_note", 142, "mm_note", "marker", "codex", (13, 17)),
    _a("mm_ques", 143, "mm_ques", "marker", "codex", (11, 14)),
    _a("mm_skull", 144, "mm_skull", "marker", "codex", (19, 18)),
    _a("mm_cross", 204, "MM_Cross", "marker", "codex", (11, 11), 2),
    _a("mm_loc", 205, "MM_Loc", "marker", "rule", (9, 9), 2),
    _a("mm_wayp", 206, "MM_WayP", "marker", "rule", (9, 9), 2),
    _a("mm_locnew", 814, "MM_LocNew", "marker", "rule", (11, 11), 2),
    _a("cvr_helmet", 241, "CVR_Helmet", "cover", "pack", (62, 64)),
    _a("cvr_boot", 242, "CVR_Boot", "cover", "pack", (64, 63)),
    _a("cvr_gauntlet", 243, "CVR_Gauntlet", "cover", "pack", (63, 64)),
    _a("cvr_armor", 244, "CVR_Armor", "cover", "codex", (128, 160)),
    _a("cvr_medalion", 245, "CVR_Medalion", "cover", "pack", (64, 31)),
    _a("cvr_ring1", 246, "CVR_Ring1", "cover", "pack", (32, 32)),
    _a("cvr_ring2", 247, "CVR_Ring2", "cover", "pack", (32, 32)),
    _a("cvr_shield", 248, "CVR_Shield", "cover", "codex", (95, 127)),
    _a("cvr_weapon", 249, "CVR_Weapon", "cover", "codex", (96, 129)),
)
SAME_AS = {"cvr_ring2": "cvr_ring1"}   # arts whose original frames are pixel for pixel another's: the pack frame of that one serves them
SHEET_ORDER = ("mm_chest", "mm_note", "mm_ques", "mm_skull", "mm_cross")   # the cells of the marker sheet, row by row
TONE_ARTS = ("cvr_helmet", "cvr_boot", "cvr_gauntlet")   # the large pack covers whose grey the generated ones take


@dataclass(frozen=True)
class Part:
    ident: str
    art: Art
    frame: int
    size: tuple[int, int]    # the master

    @property
    def source(self) -> str:
        return "rule" if self.frame else self.art.source


SAME_FRAMES = frozenset({"mm_locnew"})   # arts whose two frames are one picture in the original


def parts_of(art: Art) -> list[Part]:
    base = (kit_check.snap(art.units[0]), kit_check.snap(art.units[1]))
    pad = 0 if art.name in SAME_FRAMES else LIT_PAD
    lit = (base[0] + 2 * pad, base[1] + 2 * pad)
    return [Part(f"glyph.{art.name}" + ("" if n == 0 else "_lit"), art, n, base if n == 0 else lit) for n in range(art.frames)]


PARTS = {p.ident: p for a in ARTS for p in parts_of(a)}


def provenance_of(ident: str) -> str:
    return f"{PARTS[ident].source}:marker_kit:{ident}"


def note_of(ident: str) -> str:
    part = PARTS[ident]
    art = part.art
    what = ("the cover of an empty worn slot" if art.family == "cover" else "the map marker") + f" of art {art.art}"
    if part.frame and art.name in SAME_FRAMES:
        how = "frame 1: one picture with frame 0, as in the original"
    elif part.frame:
        how = "highlighted: the first frame with a yellow contour round it, by rule"
    elif part.source == "rule":
        how = "by rule: true circles of the original's radii and colours"
    elif part.source == "pack":
        how = "the old HD pack's frame" + (f" of art {art_named(SAME_AS[art.name]).art} (the same original)" if art.name in SAME_AS else "") + ", grid and ground removed"
    elif art.family == "cover":
        how = "a flat engraved silhouette by Codex on the original's shape, no grid"
    elif art.name in REDRAWN:
        how = "redrawn by Codex from the marker sheet's cell with neutral eyes, placed on the original's outline"
    else:
        how = "one Codex sheet for arts 141 to 144 and 204, placed on the original's outline"
    return f"{what}: {how} (marker_kit.py, ARC-UI-3207)"


def art_named(name: str) -> Art:
    for a in ARTS:
        if a.name == name:
            return a
    raise MarkerError(f"no art {name!r}; the arts are {[a.name for a in ARTS]}")


def selected(only: str | None) -> list[Art]:
    return [art_named(n) for n in only.split(",")] if only else list(ARTS)


def read_original(frames: Path, art: Art, number: int = 0) -> Image:
    path = frames / art.folder / f"f{number}.png"
    if not path.is_file():
        raise MarkerError(f"no frame {path}")
    image = pngio.read(path)
    if number == 0 and (image.w, image.h) != art.units:
        raise MarkerError(f"{path}: {image.w} x {image.h}, the art {art.art} is {art.units[0]} x {art.units[1]}")
    return image


# --- the rule look -----------------------------------------------------------------------------------------------------------

def mean_colour(colours: list[tuple[int, int, int]]) -> tuple[int, int, int]:
    return tuple(int(round(sum(c[k] for c in colours) / len(colours))) for k in range(3))  # type: ignore[return-value]


def round_layers(frame: Image) -> tuple[list[tuple[float, tuple[int, int, int]]], tuple[int, int, int] | None]:
    """A circle marker as rings from the outside in, `(radius in cells, colour)`, and the colour of its centre cross (or None). A coloured outline (yellow,
    violet) is one cell wide, as in the original; an outer rim reaches half the frame (the violet one: the radius of a disc with the area of the original's
    cells, cut to half the frame), and the bright disc of a waypoint has the area of its bright cells; its rim takes the mean of the original's dark cells."""
    cells: dict[tuple[int, int, int], int] = {}
    for y in range(frame.h):
        for x in range(frame.w):
            r, g, b, a = frame.pixel(x, y)
            if a >= 128:
                cells[(r, g, b)] = cells.get((r, g, b), 0) + 1
    if not cells:
        raise MarkerError("an empty circle marker frame")
    half = min(frame.w, frame.h) / 2

    def radius(area: float) -> float:
        return min(half, math.sqrt(area / math.pi))

    yellow = [c for c in cells if c[0] > 200 and c[1] > 200 and c[2] < 80]
    rest = {c: k for c, k in cells.items() if c not in yellow}
    if not rest:  # the "new location" ring: a yellow ring one cell wide round a centre cross
        return [(half, yellow[0]), (half - 1.0, (-1, -1, -1))], yellow[0]
    rings: list[tuple[float, tuple[int, int, int]]] = []
    if yellow:
        rings.append((half, yellow[0]))
    inner_half = half - (1.0 if yellow else 0.0)
    purple = [c for c in rest if c[2] > 200]
    if purple:  # a coloured ring round a black centre
        outer = min(inner_half, radius(sum(rest.values())))
        rings.append((outer, purple[0]))
        rings.append((outer - 1.0, (0, 0, 0)))
    else:       # a bright disc in a dark rim
        bright = max(rest, key=lambda c: c[0])
        rim = [c for c, k in rest.items() if c != bright for _ in range(k)]
        rings.append((inner_half, mean_colour(rim) if rim else bright))
        rings.append((radius(rest[bright]), bright))
    return rings, None


def round_marker(frame: Image, size: tuple[int, int]) -> Image:
    rings, cross = round_layers(frame)
    w, h = size
    cx, cy = frame.w / 2, frame.h / 2
    out = Image(w, h, bytearray(w * h * 4))
    for py in range(h):
        for px in range(w):
            acc = [0, 0, 0]
            hits = 0
            for sy in range(SS):
                v = (py + (sy + 0.5) / SS) * frame.h / h - cy
                for sx in range(SS):
                    u = (px + (sx + 0.5) / SS) * frame.w / w - cx
                    colour = None
                    d = math.hypot(u, v)
                    for r, c in rings:  # outside in: the innermost ring that holds the sample wins
                        if d <= r:
                            colour = None if c[0] < 0 else c
                    if cross is not None and ((abs(u) <= 1.5 and abs(v) <= 0.5) or (abs(v) <= 1.5 and abs(u) <= 0.5)):
                        colour = cross
                    if colour is not None:
                        hits += 1
                        for k in range(3):
                            acc[k] += colour[k]
            if hits:
                o = (py * w + px) * 4
                out.data[o:o + 4] = bytes((*(int(round(acc[k] / hits)) for k in range(3)), int(round(255 * hits / (SS * SS)))))
    return out


def is_grid(p: tuple[int, int, int, int]) -> bool:
    return p[3] >= 128 and p[2] - p[0] >= 8


def cover_layers(frame: Image) -> tuple[list[float], list[int], list[int]]:
    """A slot cover without its grid: the silhouette's coverage per cell (0..1), and the grid's columns and rows."""
    n = frame.w * frame.h
    pixels = [frame.pixel(i % frame.w, i // frame.w) for i in range(n)]
    grid = [is_grid(p) for p in pixels]
    columns = [x for x in range(frame.w) if sum(grid[y * frame.w + x] for y in range(frame.h)) > frame.h * 0.8]
    rows = [y for y in range(frame.h) if sum(grid[y * frame.w + x] for x in range(frame.w)) > frame.w * 0.8]

    def common(want_grid: bool, low: int, high: int) -> int | None:
        counts: dict[int, int] = {}
        for p, g in zip(pixels, grid):
            if p[3] >= 128 and g == want_grid and low <= p[0] <= high:
                counts[p[0]] = counts.get(p[0], 0) + 1
        return max(counts, key=counts.__getitem__) if counts else None

    shape = common(False, 20, 120)
    if shape is None:
        raise MarkerError("a slot cover without a silhouette colour")
    ground = common(False, 0, 10) or 0
    line = common(True, 0, 62)
    line_shape = common(True, 63, 255)
    coverage = []
    for i, (p, g) in enumerate(zip(pixels, grid)):
        on_line = (i % frame.w) in columns or (i // frame.w) in rows
        low, high = (line if line is not None else ground, line_shape if line_shape is not None else shape) if g and on_line else (ground, shape)
        coverage.append(min(1.0, max(0.0, (p[0] - low) / max(1, high - low))) if p[3] >= 128 else 0.0)
    # no silhouette reaches the frame's edge: a covered outermost line is the shaded edge of the original's slot (the left column of art 249)
    edges = [[y * frame.w for y in range(frame.h)], [y * frame.w + frame.w - 1 for y in range(frame.h)], list(range(frame.w)), list(range(n - frame.w, n))]
    for line_cells in edges:
        if sum(coverage[i] > 0.25 for i in line_cells) > len(line_cells) * 0.8:
            for i in line_cells:
                coverage[i] = 0.0
    return coverage, columns, rows


def opaque_box(solid: list[bool], w: int, h: int, least: int = 1) -> tuple[int, int, int, int]:
    """(x0, y0, x1, y1) of the rows and columns that hold at least `least` solid pixels."""
    xs = [x for x in range(w) if sum(solid[y * w + x] for y in range(h)) >= least]
    ys = [y for y in range(h) if sum(solid[y * w + x] for x in range(w)) >= least]
    if not xs or not ys:
        raise MarkerError("nothing is drawn: no outline to place")
    return xs[0], ys[0], xs[-1] + 1, ys[-1] + 1


def paste_into(piece: Image, box: tuple[float, float, float, float], size: tuple[int, int], background: tuple[int, int, int, int] = (0, 0, 0, 0)) -> Image:
    """`piece` resampled to `box` (master px) on a canvas of `size`."""
    x0, y0, x1, y1 = (int(round(v)) for v in box)
    scaled = small_kit.resample(piece, max(1, x1 - x0), max(1, y1 - y0))
    out = Image(size[0], size[1], bytearray(bytes(background) * (size[0] * size[1])))
    for y in range(scaled.h):
        for x in range(scaled.w):
            if 0 <= x + x0 < size[0] and 0 <= y + y0 < size[1]:
                k = ((y + y0) * size[0] + x + x0) * 4
                out.data[k:k + 4] = scaled.data[(y * scaled.w + x) * 4:(y * scaled.w + x) * 4 + 4]
    return out


# --- the covers -------------------------------------------------------------------------------------------------------------

def silhouette_mask(frame: Image, size: tuple[int, int]) -> list[bool]:
    coverage, _, _ = cover_layers(frame)
    return [v >= 0.5 for v in button_kit.resample_plane(coverage, (frame.w, frame.h), size, SILHOUETTE_SIGMA)]


def dilate(mask: list[bool], w: int, h: int, reach: int) -> list[bool]:
    rows = [False] * (w * h)
    for y in range(h):
        for x in range(w):
            if mask[y * w + x]:
                for k in range(max(0, x - reach), min(w, x + reach + 1)):
                    rows[y * w + k] = True
    out = [False] * (w * h)
    for x in range(w):
        for y in range(h):
            if rows[y * w + x]:
                for k in range(max(0, y - reach), min(h, y + reach + 1)):
                    out[k * w + x] = True
    return out


def bands(cells: list[int], units: int, pixels: int, grow: int) -> list[tuple[int, int]]:
    """The master pixel spans [a, b) the grid lines at the original `cells` cover along one axis, `grow` px wider each side, touching spans joined."""
    spans: list[tuple[int, int]] = []
    for c in sorted(cells):
        a, b = max(0, int(c * pixels / units) - grow), min(pixels, int(math.ceil((c + 1) * pixels / units)) + grow)
        if spans and a <= spans[-1][1]:
            spans[-1] = (spans[-1][0], max(spans[-1][1], b))
        else:
            spans.append((a, b))
    return spans


def remove_lines(image: Image, columns: list[tuple[int, int]], rows: list[tuple[int, int]]) -> Image:
    """The baked grid lines painted out: across each span the colour runs straight from the pixel before it to the pixel after it (at an edge, the one there is)."""
    out = Image(image.w, image.h, bytearray(image.data))
    for spans, length, other, at in ((columns, image.w, image.h, lambda i, j: (j * image.w + i) * 4), (rows, image.h, image.w, lambda i, j: (i * image.w + j) * 4)):
        for a, b in spans:
            if a == 0 and b == length:
                raise MarkerError("a grid span covers the whole picture")
            for j in range(other):
                before = bytes(out.data[at(a - 1, j):at(a - 1, j) + 3]) if a > 0 else None
                after = bytes(out.data[at(b, j):at(b, j) + 3]) if b < length else None
                for i in range(a, b):
                    t = (i - a + 1) / (b - a + 1)
                    first, second = before or after, after or before
                    out.data[at(i, j):at(i, j) + 3] = bytes(int(round(first[k] * (1 - t) + second[k] * t)) for k in range(3))
    return out


def overlay(picture: Image, keep: list[bool]) -> Image:
    """A grey picture on black as a translucent overlay on a transparent ground: laid over black it is the picture again, and what lies under it (the grid's
    lines) shows through, as in the original. The ground (outside `keep`, or no lighter than the ground's own grey) is clear."""
    grey = [max(picture.data[i * 4:i * 4 + 3]) for i in range(picture.w * picture.h)]
    outside = sorted(v for v, k in zip(grey, keep) if not k)
    floor = (outside[len(outside) // 2] if outside else 0) + GROUND_SLACK
    out = Image(picture.w, picture.h, bytearray(picture.w * picture.h * 4))
    for i, v in enumerate(grey):
        if keep[i] and v > floor:
            out.data[i * 4:i * 4 + 4] = bytes((*(min(255, int(round(picture.data[i * 4 + k] * 255 / v))) for k in range(3)), v))
    return out


def level_of(part: Image, mask: list[bool]) -> float:
    """The median grey of an overlay inside the original's silhouette."""
    values = sorted(part.data[i * 4 + 3] for i, m in enumerate(mask) if m)
    if not values:
        raise MarkerError("an empty silhouette")
    return float(values[len(values) // 2])


def pack_source(pack: Path, rows: dict[tuple[str, int], str], art: Art) -> Path:
    name = art_named(SAME_AS[art.name]).pack_name if art.name in SAME_AS else art.pack_name
    rel = next((v for (n, f), v in rows.items() if n.lower() == name.lower() and f == 0), None)
    if rel is None or not (pack / rel).is_file():
        raise MarkerError(f"glyph.{art.name}: the pack has no frame of {name}; --pack names the installed pack")
    return pack / rel


def pack_cover(part: Part, frames: Path, pack: Path, rows: dict[tuple[str, int], str]) -> tuple[Image, Path]:
    art = part.art
    frame = read_original(frames, art)
    if art.name in SAME_AS and frame.data != read_original(frames, art_named(SAME_AS[art.name])).data:
        raise MarkerError(f"{part.ident}: the original frame is not {SAME_AS[art.name]}'s, so that pack frame does not serve it")
    source = pack_source(pack, rows, art)
    image = pngio.read(source)
    expected = (art.units[0] * PACK_SCALE, art.units[1] * PACK_SCALE)
    if (image.w, image.h) != expected:
        raise MarkerError(f"{source.name}: {image.w} x {image.h}, the frame at {PACK_SCALE} x is {expected[0]} x {expected[1]}")
    picture = small_kit.resample(image, *part.size)
    _, columns, lines = cover_layers(frame)
    picture = remove_lines(picture, bands(columns, art.units[0], part.size[0], 2), bands(lines, art.units[1], part.size[1], 2))
    keep = dilate(silhouette_mask(frame, part.size), *part.size, 6)
    return overlay(picture, keep), source


def pack_level(frames: Path, pack: Path, rows: dict[tuple[str, int], str]) -> float:
    """The tone of the large accepted pack covers (`TONE_ARTS`; the thin medallion and rings are lighter): the mean of their median greys. The generated
    silhouettes, large ones too, take it."""
    levels = []
    for name in TONE_ARTS:
        part = PARTS[f"glyph.{name}"]
        levels.append(level_of(pack_cover(part, frames, pack, rows)[0], silhouette_mask(read_original(frames, part.art), part.size)))
    return sum(levels) / len(levels)


def codex_cover(part: Part, frames: Path, generated: Image, level: float) -> tuple[Image, float]:
    """The generated silhouette placed on the original's outline (its bounding box), in the tone `level`, as a translucent overlay; and the overlap of its
    outline with the original's (intersection over union)."""
    art = part.art
    frame = read_original(frames, art)
    coverage, _, _ = cover_layers(frame)
    grey = [max(generated.data[i * 4:i * 4 + 3]) for i in range(generated.w * generated.h)]
    strong = sorted(grey)[int(len(grey) * 0.98)]
    if strong < 30:
        raise MarkerError(f"{part.ident}: the generated picture is black")
    solid = [v > strong * 0.35 for v in grey]
    gx0, gy0, gx1, gy1 = opaque_box(solid, generated.w, generated.h, 3)
    ox0, oy0, ox1, oy1 = opaque_box([v >= 0.5 for v in coverage], frame.w, frame.h)
    sx, sy = part.size[0] / frame.w, part.size[1] / frame.h
    placed = paste_into(generated.crop(gx0, gy0, gx1 - gx0, gy1 - gy0), (ox0 * sx, oy0 * sy, ox1 * sx, oy1 * sy), part.size, (0, 0, 0, 255))
    mask = silhouette_mask(frame, part.size)
    inside = sorted(max(placed.data[i * 4:i * 4 + 3]) for i, m in enumerate(mask) if m)
    gain = level / max(1, inside[len(inside) // 2])
    for i in range(len(placed.data)):
        if i % 4 != 3:
            placed.data[i] = min(255, int(round(placed.data[i] * gain)))
    own = [max(placed.data[i * 4:i * 4 + 3]) > level * 0.35 for i in range(placed.w * placed.h)]
    overlap = sum(a and b for a, b in zip(own, mask)) / max(1, sum(a or b for a, b in zip(own, mask)))
    return overlay(placed, dilate(mask, *part.size, 6)), overlap


# --- the markers ------------------------------------------------------------------------------------------------------------

def keyed(cell: Image) -> Image:
    """A cell of the marker sheet cut from its flat magenta ground: a pixel that mixes the ground in takes that share as transparency and loses that colour."""
    out = Image(cell.w, cell.h, bytearray(cell.w * cell.h * 4))
    for i in range(cell.w * cell.h):
        r, g, b = cell.data[i * 4:i * 4 + 3]
        share = max(0, min(r, b) - g) / 255.0
        alpha = min(1.0, max(0.0, (1.0 - share - 0.08) / 0.84))
        if alpha <= 0.0:
            continue
        keep = max(1.0 - share, 1e-3)
        colour = (min(255, max(0, int(round((r - 255 * share) / keep)))), min(255, int(round(g / keep))), min(255, max(0, int(round((b - 255 * share) / keep)))))
        out.data[i * 4:i * 4 + 4] = bytes((*colour, int(round(alpha * 255))))
    return out


def sheet_cell(sheet: Image, name: str) -> Image:
    index = SHEET_ORDER.index(name)
    cw, ch = sheet.w // 3, sheet.h // 2
    return sheet.crop((index % 3) * cw, (index // 3) * ch, cw, ch)


def codex_marker(part: Part, frames: Path, picture: Image) -> tuple[Image, float]:
    """The marker's cell (of the sheet; the whole picture for a `REDRAWN` marker) placed on the original's outline (its bounding box); and how much the
    placing stretched it (x scale over y scale)."""
    cell = keyed(picture if part.art.name in REDRAWN else sheet_cell(picture, part.art.name))
    solid = [cell.data[i * 4 + 3] >= 128 for i in range(cell.w * cell.h)]
    gx0, gy0, gx1, gy1 = opaque_box(solid, cell.w, cell.h, 3)
    if gx0 == 0 or gy0 == 0 or gx1 == cell.w or gy1 == cell.h:
        raise MarkerError(f"{part.ident}: the drawing touches the edge of its cell on the sheet")
    frame = read_original(frames, part.art)
    ox0, oy0, ox1, oy1 = opaque_box([frame.data[i * 4 + 3] >= 128 for i in range(frame.w * frame.h)], frame.w, frame.h)
    sx, sy = part.size[0] / frame.w, part.size[1] / frame.h
    stretch = ((ox1 - ox0) * sx / (gx1 - gx0)) / ((oy1 - oy0) * sy / (gy1 - gy0))
    return paste_into(cell.crop(gx0, gy0, gx1 - gx0, gy1 - gy0), (ox0 * sx, oy0 * sy, ox1 * sx, oy1 * sy), part.size), stretch


def highlight(base: Image, size: tuple[int, int]) -> Image:
    """The highlighted frame by rule: the first frame in the middle of a larger canvas over a yellow contour of one width round its outline. A closed hole
    of the marker (the clear inside of a ring) stays clear."""
    n = 3  # samples per pixel and axis
    canvas = Image.blank(*size)
    small_kit.over(canvas, base, (size[0] - base.w) // 2, (size[1] - base.h) // 2)
    w, h = size[0] * n, size[1] * n

    def alpha_at(x: float, y: float) -> float:
        x0, y0 = int(math.floor(x - 0.5)), int(math.floor(y - 0.5))
        fx, fy = x - 0.5 - x0, y - 0.5 - y0
        total = 0.0
        for dx, wx in ((0, 1 - fx), (1, fx)):
            for dy, wy in ((0, 1 - fy), (1, fy)):
                if 0 <= x0 + dx < size[0] and 0 <= y0 + dy < size[1]:
                    total += wx * wy * canvas.data[((y0 + dy) * size[0] + x0 + dx) * 4 + 3]
        return total

    inside = [alpha_at((i % w + 0.5) / n, (i // w + 0.5) / n) >= 128 for i in range(w * h)]
    exterior = button_kit.outside_cells(inside, w, h)
    reach = RING * n
    disc = [(dx, dy) for dy in range(-reach, reach + 1) for dx in range(-reach, reach + 1) if dx * dx + dy * dy <= reach * reach]
    near = [False] * (w * h)
    for i in range(w * h):
        x, y = i % w, i // w
        if inside[i] and any(0 <= xx < w and 0 <= yy < h and exterior[yy * w + xx] for xx, yy in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1))):
            for dx, dy in disc:
                if 0 <= x + dx < w and 0 <= y + dy < h:
                    near[(y + dy) * w + x + dx] = True
    out = Image.blank(*size)
    for py in range(size[1]):
        for px in range(size[0]):
            hits = sum(near[(py * n + j) * w + px * n + i] and (exterior[(py * n + j) * w + px * n + i] or inside[(py * n + j) * w + px * n + i]) for j in range(n) for i in range(n))
            if hits:
                out.data[(py * size[0] + px) * 4:(py * size[0] + px) * 4 + 4] = bytes((*YELLOW, int(round(255 * hits / (n * n)))))
    small_kit.over(out, canvas, 0, 0)
    return out


# --- the Codex batches ------------------------------------------------------------------------------------------------------

MARKER_PROMPT = """Draw one sheet of five small map marker icons for a steampunk role-playing game, in one consistent style.

Input image ORIGINAL: the five original pixel markers, enlarged, on a flat pure magenta (#FF00FF) ground, in a grid of 3 columns by 2 rows (the sixth cell is empty). It is the layout truth and the shape truth.

Cells, row by row:
1. a small wooden treasure chest seen from a corner, rounded lid of lighter planks, darker lower body, one vertical yellow brass lock bar on the front;
2. a written paper note: an upright pale parchment sheet with five short rows of grey-brown writing and a thin dark ragged rim;
3. a red question mark with darker red shading on its left and lower sides;
4. a skull over two crossed bones: a large bright white rounded cranium, two dark eye sockets, a small dark nose and a jaw with teeth below it, the two bones crossing diagonally behind with rounded double knob ends showing in all four corners, cool bluish-violet grey shadows on the undersides. Keep every one of these details clearly readable;
5. a red cross pattee emblem (four bright red arms that widen outward) on a dark maroon rounded octagon, with small black accents between the arms.

Style for all five: crisp hand-painted icon, clean flat colours with two or three shading steps, sharp edges, a bold clean black outline of the same thickness round every icon, no blur, no soft glow, no drop shadow, no texture noise, readable when reduced to 25 to 60 pixels. Keep the colours of ORIGINAL.

Layout rules: the output has the same 3 by 2 grid. Each icon keeps the place, the size, the proportions and the outline shape it has in ORIGINAL. The ground everywhere else is flat pure magenta #FF00FF, with no gradient. No grid lines, no frames, no text, no numbers. The sixth cell stays plain magenta."""

COVER_TEXT = {
    "cvr_armor": "a breastplate: a deep round neck opening at the top, two shoulder points, a narrow waist with pointed side flanges, a wide lower edge ending in a shallow point. ORIGINAL shows a few small rivets along the shoulders, the sides and the lower edge: engrave them as small simple rivets",
    "cvr_shield": "a heater shield with a flat top edge, crossed by a torch that runs from the lower left to the upper right, with tongues of flame at its upper right end. ORIGINAL separates the torch and the flames from the shield by thin dark lines: keep those lines as clean engraved gaps",
    "cvr_weapon": "an upright sword, point down: a small knob pommel and a grip with a few bands at the top, a straight crossguard with curled ends, a long straight blade with a rounded point. ORIGINAL shows a small V-shaped notch on the blade just under the crossguard: keep it as a thin engraved line",
}
COVER_PROMPT = """Draw the picture of one empty equipment slot of a steampunk role-playing game inventory: a flat grey silhouette on a pure black ground.

Input image ORIGINAL: the original silhouette, enlarged, with its brightness raised so you can read it. It is the shape truth: {what}.
Input image STYLE: four already accepted silhouettes of the same set (helmet, gauntlet, boot, medallion). Match their style exactly: one flat even grey with a very light fine engraving, thin slightly darker engraved lines and small rivets only where ORIGINAL has detail, clean smooth sharp edges.

Rules: it must read as an empty slot's faint sign, not as a real object. No three-dimensional shading, no bevel, no highlights, no metallic shine, no strong texture, no colour, no gradient. Keep the outline, the proportions, the size and the position of ORIGINAL inside the picture; smooth the pixel staircase into clean curves and straight edges. The ground is pure flat black #000000. No grid lines, no frame, no text."""

SKULL_PROMPT = """Edit one small map marker icon of a steampunk role-playing game: a skull over two crossed bones.

Input image CURRENT: the accepted icon. Input image ORIGINAL: the original pixel marker, enlarged; it shows the expression wanted.

Change only the eyes and brow: remove the angry frowning brow lines and the slanted eye shapes. Draw two plain rounded hollow eye sockets, dark, level and symmetrical, with a smooth white forehead above them: a neutral, expressionless skull as in ORIGINAL. Keep everything else of CURRENT exactly: the cranium, the nose, the jaw and teeth, the crossed bones with their knob ends, the bluish-violet shading, the bold black outline of the same thickness, the colours, the size and the position in the picture.

The ground everywhere else is flat pure magenta #FF00FF, with no gradient. No text, no frame, no shadow."""

COVER_ASK = {"cvr_armor": (1024, 1280), "cvr_shield": (960, 1280), "cvr_weapon": (960, 1280)}
STYLE_ARTS = ("cvr_helmet", "cvr_gauntlet", "cvr_boot", "cvr_medalion")


def marker_reference(frames: Path) -> Image:
    sheet = Image(SHEET_CELL * 3, SHEET_CELL * 2, bytearray(bytes((255, 0, 255, 255)) * (SHEET_CELL * SHEET_CELL * 6)))
    for index, name in enumerate(SHEET_ORDER):
        frame = read_original(frames, art_named(name))
        big = small_kit.upscaled(frame, (frame.w * SHEET_ZOOM, frame.h * SHEET_ZOOM))
        small_kit.over(sheet, big, (index % 3) * SHEET_CELL + (SHEET_CELL - big.w) // 2, (index // 3) * SHEET_CELL + (SHEET_CELL - big.h) // 2)
    return sheet


def single_reference(frames: Path, name: str) -> Image:
    """One original marker as a cell of the reference sheet."""
    frame = read_original(frames, art_named(name))
    cell = Image(SHEET_CELL, SHEET_CELL, bytearray(bytes((255, 0, 255, 255)) * (SHEET_CELL * SHEET_CELL)))
    big = small_kit.upscaled(frame, (frame.w * SHEET_ZOOM, frame.h * SHEET_ZOOM))
    small_kit.over(cell, big, (SHEET_CELL - big.w) // 2, (SHEET_CELL - big.h) // 2)
    return cell


def cover_reference(frames: Path, art: Art, asked: tuple[int, int]) -> Image:
    """The original without its grid, brightened, enlarged and centred on black at the asked aspect."""
    frame = read_original(frames, art)
    coverage, _, _ = cover_layers(frame)
    grey = Image(frame.w, frame.h, bytearray(b for v in coverage for b in (int(round(v * REFERENCE_LEVEL)),) * 3 + (255,)))
    big = small_kit.upscaled(grey, (frame.w * COVER_ZOOM, frame.h * COVER_ZOOM))
    w = max(big.w, int(math.ceil(big.h * asked[0] / asked[1])))
    h = max(big.h, int(round(w * asked[1] / asked[0])))
    out = Image(w, h, bytearray(bytes((0, 0, 0, 255)) * (w * h)))
    small_kit.over(out, big, (w - big.w) // 2, (h - big.h) // 2)
    return out


def style_reference(frames: Path, pack: Path, rows: dict[tuple[str, int], str]) -> Image:
    """The accepted pack covers side by side on black, brightened to the reference level."""
    parts = [PARTS[f"glyph.{n}"] for n in STYLE_ARTS]
    gap = 48
    out_w = sum(p.size[0] for p in parts) * 2 + gap * (len(parts) + 1)
    out_h = max(p.size[1] for p in parts) * 2 + gap * 2
    out = Image(out_w, out_h, bytearray(bytes((0, 0, 0, 255)) * (out_w * out_h)))
    x = gap
    for part in parts:
        image = pack_cover(part, frames, pack, rows)[0]
        gain = REFERENCE_LEVEL / level_of(image, silhouette_mask(read_original(frames, part.art), part.size))
        for i in range(3, len(image.data), 4):
            image.data[i] = min(255, int(round(image.data[i] * gain)))
        big = small_kit.upscaled(image, (part.size[0] * 2, part.size[1] * 2))
        small_kit.over(out, big, x, (out_h - big.h) // 2)
        x += big.w + gap
    return out


def write_batch(local_root: Path, batch: str, items: list[tuple[str, str, str, tuple[int, int], list[tuple[str, Image]]]]) -> Path:
    inbox = local_root / "inbox" / batch
    (inbox / "prompts").mkdir(parents=True, exist_ok=True)
    rows = []
    for ident, title, text, size, references in items:
        folder = inbox / "refs" / ident
        folder.mkdir(parents=True, exist_ok=True)
        listed = []
        for slot, image in references:
            path = folder / f"{slot}.png"
            pngio.write(path, image)
            listed.append({"slot": slot, "file": f"refs/{ident}/{slot}.png", "sha256": button_kit.sha256_file(path)})
        prompt = inbox / "prompts" / f"{ident}.txt"
        prompt.write_text(text + "\n", encoding="ascii")
        rows.append({"id": ident, "title": title, "prompt_file": f"prompts/{ident}.txt", "prompt_sha256": hashlib.sha256(prompt.read_bytes()).hexdigest(),
                     "size": list(size), "references": listed, "outbox_file": f"outbox/{batch}/{ident}.png"})
    (inbox / "batch.json").write_text(json.dumps({"batch": batch, "items": rows}, indent=2) + "\n", encoding="utf-8")
    return inbox


def cmd_codex_inbox(args: argparse.Namespace) -> int:
    frames, rows = context(args)
    markers = [(MARKER_ITEM, "map markers 141 to 144 and 204 on one sheet", MARKER_PROMPT, (SHEET_CELL * 3, SHEET_CELL * 2), [("original", marker_reference(frames))])]
    sheet = args.local_root / "outbox" / MARKER_BATCH / f"{MARKER_ITEM}.png"
    if sheet.is_file():   # the redrawn markers edit the sheet's cells, so they follow it
        markers += [(item, f"{name} redrawn from its cell of the sheet", SKULL_PROMPT, (1024, 1024), [("current", sheet_cell(pngio.read(sheet), name)), ("original", single_reference(frames, name))])
                    for name, item in REDRAWN.items()]
    write_batch(args.local_root, MARKER_BATCH, markers)
    style = style_reference(frames, args.pack, rows)
    items = []
    for name, what in COVER_TEXT.items():
        art = art_named(name)
        items.append((name.replace("cvr_", "cover-"), f"empty slot silhouette of art {art.art}", COVER_PROMPT.format(what=what), COVER_ASK[name],
                      [("original", cover_reference(frames, art, COVER_ASK[name])), ("style", style)]))
    write_batch(args.local_root, COVER_BATCH, items)
    print(f"MARKER KIT codex-inbox PASS: {MARKER_BATCH} ({len(markers)} item(s)), {COVER_BATCH} ({len(items)} items)")
    return 0


# --- the build ----------------------------------------------------------------------------------------------------------------

def generated(local_root: Path, part: Part) -> Path:
    batch, item = (COVER_BATCH, part.art.name.replace("cvr_", "cover-")) if part.art.family == "cover" else (MARKER_BATCH, REDRAWN.get(part.art.name, MARKER_ITEM))
    path = local_root / "outbox" / batch / f"{item}.png"
    if not path.is_file():
        raise MarkerError(f"{part.ident}: no generated picture {path}: run `codex-inbox`, then `codex_batch.py run {batch}`")
    return path


@dataclass
class Made:
    part: Part
    image: Image
    source: Path | None      # the pack frame or the generated picture it comes from (a highlighted frame: its first part's); None: by rule alone
    note: str = ""


def build_parts(args: argparse.Namespace, arts: list[Art]) -> list[Made]:
    frames, rows = context(args)
    level = pack_level(frames, args.pack, rows) if any(a.source == "codex" and a.family == "cover" for a in arts) else 0.0
    made = []
    for art in arts:
        parts = parts_of(art)
        first = parts[0]
        if first.source == "pack":
            image, source = pack_cover(first, frames, args.pack, rows)
            made.append(Made(first, image, source))
        elif first.source == "rule":
            made.append(Made(first, round_marker(read_original(frames, art), first.size), None))
        else:
            source = generated(args.local_root, first)
            if art.family == "cover":
                image, overlap = codex_cover(first, frames, pngio.read(source), level)
                made.append(Made(first, image, source, f"outline overlap {overlap:.2f}"))
            else:
                image, stretch = codex_marker(first, frames, pngio.read(source))
                made.append(Made(first, image, source, f"stretch {stretch:.2f}"))
        for lit in parts[1:]:
            if art.name in SAME_FRAMES:
                if read_original(frames, art, lit.frame).data != read_original(frames, art).data:
                    raise MarkerError(f"{lit.ident}: the original's frames of art {art.art} differ, so the first part does not serve the second")
                made.append(Made(lit, Image(*lit.size, bytearray(made[-1].image.data)), None))
            else:
                made.append(Made(lit, highlight(made[-1].image, lit.size), made[-1].source))   # the record traces it to the first part's picture
    return made


def record_of(made: Made, image_path: Path, frames: Path, args: argparse.Namespace) -> dict:
    part = made.part
    original = frames / part.art.folder / "f0.png"   # every part is made from the first frame: a second part is the first, highlighted or copied
    record = {
        "id": part.ident,
        "tool": "marker_kit.py",
        "tool_version": TOOL_VERSION,
        "rule": note_of(part.ident),
        "source_art": part.art.art,
        "source": None,
        "made_from": parts_of(part.art)[0].ident if part.frame else None,
        "original": {"frame": f"{part.art.folder}/f0.png", "sha256": button_kit.sha256_file(original)},
        "sha256": button_kit.sha256_file(image_path),
        "date": datetime.date.today().isoformat(),
    }
    if made.source is not None and part.source == "pack":
        record["source"] = {"pack": args.pack.name, "file": str(made.source.relative_to(args.pack)), "sha256": button_kit.sha256_file(made.source)}
    elif made.source is not None:
        record["source"] = {"picture": made.source.name, "sha256": button_kit.sha256_file(made.source)}
        provenance = made.source.with_suffix(".provenance.json")
        if provenance.is_file():
            try:
                record["source"]["generation"] = json.loads(provenance.read_text(encoding="utf-8"))
            except ValueError as error:
                raise MarkerError(f"{provenance}: not a JSON provenance record ({error})") from error
    return record


def context(args: argparse.Namespace) -> tuple[Path, dict[tuple[str, int], str]]:
    return args.frames or args.local_root / "census" / "frames", small_kit.pack_index(args.pack)


def cmd_plan(args: argparse.Namespace) -> int:
    frames, rows = context(args)
    manifest = {r["id"]: r for r in json.loads(small_kit.MANIFEST.read_text(encoding="utf-8"))["parts"]}
    bad = 0
    for part in PARTS.values():
        row = manifest.get(part.ident)
        read_original(frames, part.art, part.frame)
        if part.source == "pack":
            pack_source(args.pack, rows, part.art)
        ok = row is not None and row["size"] == (list(part.size) if part.frame == 0 else None) and row["provenance"] == provenance_of(part.ident)
        bad += 0 if ok else 1
        print(f"{part.ident:26s} art {part.art.art} f{part.frame}  -> {part.size[0]} x {part.size[1]}  {part.source:5s}  {'ok' if ok else 'CHECK'}")
    print(f"MARKER KIT plan {'PASS' if not bad else 'FAIL'} ({len(PARTS)} parts)")
    return 0 if not bad else 1


def cmd_build(args: argparse.Namespace) -> int:
    frames, _ = context(args)
    out = args.out or args.local_root / "masters"
    records = args.local_root / "provenance" / "rule" if args.out is None else out / "records"
    made = build_parts(args, selected(args.only))
    for m in made:
        covered = sum(1 for i in range(3, len(m.image.data), 4) if m.image.data[i]) / (m.image.w * m.image.h)
        print(f"{m.part.ident:26s} {m.image.w} x {m.image.h}  {m.part.source:5s}  covered {covered * 100:5.1f} %  {m.note}")
    if args.dry_run:
        print(f"MARKER KIT build DRY-RUN PASS ({len(made)} parts); nothing written")
        return 0
    out.mkdir(parents=True, exist_ok=True)
    records.mkdir(parents=True, exist_ok=True)
    for m in made:
        path = out / f"{m.part.ident}.png"
        pngio.write(path, m.image)
        (records / f"{m.part.ident}.json").write_text(json.dumps(record_of(m, path, frames, args), indent=2) + "\n", encoding="utf-8")
    print(f"MARKER KIT build PASS ({len(made)} parts written to {out})")
    return 0


def grid_ground(frames: Path, part: Part) -> Image:
    """The slot's black ground with straight grid lines at the original's places (how the original shows a cover; the kit's grid layer takes this place)."""
    frame = read_original(frames, part.art)
    _, columns, lines = cover_layers(frame)
    out = Image(*part.size, bytearray(bytes((0, 1, 2, 255)) * (part.size[0] * part.size[1])))
    for spans, vertical in ((bands(columns, frame.w, part.size[0], 0), True), (bands(lines, frame.h, part.size[1], 0), False)):
        for a, b in spans:
            for y in range(0 if vertical else a, part.size[1] if vertical else b):
                for x in range(a if vertical else 0, b if vertical else part.size[0]):
                    out.data[(y * part.size[0] + x) * 4:(y * part.size[0] + x) * 4 + 3] = bytes((46, 51, 60))
    return out


def cmd_sheet(args: argparse.Namespace) -> int:
    """The composite for the owner: per frame the original | the new part (`--parts` DIR of built masters, else built now)."""
    if shutil.which("magick") is None:
        raise MarkerError("ImageMagick (`magick`) is needed for this command")
    frames, _ = context(args)
    arts = selected(args.only)
    if args.parts:
        images = {p.ident: pngio.read(args.parts / f"{p.ident}.png") for a in arts for p in parts_of(a)}
    else:
        images = {m.part.ident: m.image for m in build_parts(args, arts)}
    covers = all(a.family == "cover" for a in arts)
    zoom = 1 if covers else args.zoom
    width = max(p.size[0] for a in arts for p in parts_of(a)) * zoom + 24
    with tempfile.TemporaryDirectory() as tmp:
        work = Path(tmp)

        def label(path: Path, text: str, w: int, h: int) -> Path:
            subprocess.run(["magick", "-size", f"{w}x{h}", "xc:#202020", "-font", small_kit.LABEL_FONT, "-fill", "white", "-pointsize", "16", "-gravity", "center", "-annotate", "+0+0", text, str(path)], check=True)
            return path

        def joined(path: Path, items: list[Path], how: str) -> Path:
            subprocess.run(["magick", *[str(i) for i in items], "-background", "#202020", how, str(path)], check=True)
            return path

        lines = [joined(work / "head.png", [label(work / f"h{k}.png", t, w, 30) for k, (t, w) in enumerate((("art", 170), ("original", width), ("new", width)))], "+append")]
        for art in arts:
            for part in parts_of(art):
                original = read_original(frames, art, part.frame)
                old = Image.blank(*part.size)
                first = parts_of(art)[0].size
                big = small_kit.upscaled(original, (int(round(original.w * first[0] / art.units[0])), int(round(original.h * first[1] / art.units[1]))))
                small_kit.over(old, big, (part.size[0] - big.w) // 2, (part.size[1] - big.h) // 2)
                new = images[part.ident]
                if art.family == "cover":
                    shown = grid_ground(frames, part)
                    small_kit.over(shown, new, 0, 0)
                    new = shown
                cells = [label(work / f"{part.ident}-l.png", f"{art.art} {art.name}\nframe {part.frame}: {part.source}", 170, part.size[1] * zoom + 24)]
                for k, image in enumerate((old, new)):
                    raw = work / f"{part.ident}-{k}.png"
                    pngio.write(raw, image)
                    cell = work / f"{part.ident}-{k}c.png"
                    subprocess.run(["magick", str(raw), "-filter", "point", "-resize", f"{zoom * 100}%", "-background", "#606060" if covers else "#b9a47c", "-flatten", "-gravity", "center",
                                    "-extent", f"{width - 6}x{part.size[1] * zoom + 18}", "-bordercolor", "#202020", "-border", "3", str(cell)], check=True)
                    cells.append(cell)
                lines.append(joined(work / f"{part.ident}-row.png", cells, "+append"))
        args.out.parent.mkdir(parents=True, exist_ok=True)
        joined(args.out, lines, "-append")
    print(f"MARKER KIT sheet PASS ({len(arts)} arts) -> {args.out}")
    return 0


def self_test() -> int:
    frame = Image(9, 9, bytearray(9 * 9 * 4))
    for y in range(9):
        for x in range(9):
            d = math.hypot(x - 4, y - 4)
            if d <= 4.4:
                frame.data[(y * 9 + x) * 4:(y * 9 + x) * 4 + 4] = bytes((255, 0, 0, 255) if d <= 3.2 else (74, 0, 0, 255))
    made = round_marker(frame, (30, 30))
    assert made.pixel(15, 15) == (255, 0, 0, 255) and made.pixel(0, 0)[3] == 0, "a red disc in the middle, clear corners"
    lit = highlight(made, (42, 42))
    assert lit.pixel(21, 21) == (255, 0, 0, 255) and lit.pixel(21, 3) == (*YELLOW, 255) and lit.pixel(0, 0)[3] == 0, "a yellow contour round the first frame"
    print("MARKER KIT self-test PASS")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("plan", "codex-inbox", "build", "sheet"):
        s = sub.add_parser(name)
        s.add_argument("--local-root", type=Path, default=None)
        s.add_argument("--pack", type=Path, default=small_kit.PACK_ROOT, help="the installed HD pack the accepted covers are read from")
        s.add_argument("--frames", type=Path)
        if name in ("build", "sheet"):
            s.add_argument("--only")
        if name == "build":
            s.add_argument("--out", type=Path)
            s.add_argument("--dry-run", action="store_true")
        if name == "sheet":
            s.add_argument("--out", type=Path, required=True)
            s.add_argument("--parts", type=Path, help="a folder of built masters to show")
            s.add_argument("--zoom", type=int, default=3)
    sub.add_parser("self-test")
    args = parser.parse_args(argv)
    try:
        if args.command == "self-test":
            return self_test()
        if args.local_root is None:
            try:
                from . import fit
            except ImportError:
                import fit  # type: ignore
            args.local_root = fit.default_local_root()
        return {"plan": cmd_plan, "codex-inbox": cmd_codex_inbox, "build": cmd_build, "sheet": cmd_sheet}[args.command](args)
    except (MarkerError, small_kit.SmallError, button_kit.ButtonError, pngio.PngError) as error:
        print(f"MARKER KIT FAIL: {error}")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
