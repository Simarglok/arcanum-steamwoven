#!/usr/bin/env python3
"""ARC-UI-3321: the character sheet's solid fixtures as kit parts, cut out of the player's own HD frame of the sheet.

    python3 -B tools/steamwoven_ui/sheet_parts.py list
    python3 -B tools/steamwoven_ui/sheet_parts.py build --hd-frame HD_char_maint.png [--out DIR] [--only ID,...]
    python3 -B tools/steamwoven_ui/sheet_parts.py self-test

Art 22 of the sheet bakes in the stat ovals (brass, with their two sockets), the alignment dial, the health and fatigue discs and the magic-technology ruler. The kit's surface carries none of them, and the
rule-drawn stand-ins of ARC-UI-3098 are poor beside the earlier generative HD version that the owner likes (#3321, pack-first). So each is cut from that HD frame the way `label_parts.py` cuts the labels, but
for a solid part that sits on the wood with a cast shadow instead of lettering that is the wood's own tone:

  1. **Find the part.** The HD frame's own colours tell the part from the wood: the black pits (luma under 10), brass (a yellow cast, not the wood's red-brown), parchment and bright metal (luma over 105). The
     find is closed over the small gaps of the rim, its holes are filled (the dial's face, the ruler's strip), the specks of grain are opened away, and only the pieces of a real size are kept. The piece that holds the
     part's anchor point is the part; the other pieces in the read box (the next oval, a label) only keep their own shadow out of its measurements.
  2. **The wood under the shadow.** Around the part the wood is darkened by the part's cast shadow (down and right of every part of the sheet, a few units long, dark at the rim). The darkening is measured, not
     differenced pixel by pixel (the wood's grain is rougher than the shadow is deep): the pixels around the part are grouped by their direction away from the nearest part pixel (16 sectors) and their distance, the
     median luma of each (sector, distance) bin is compared with the median of the unshadowed ring beyond the shadow in the same sector, and the ratio is the shadow's alpha profile. It is smoothed along the distance,
     made flat toward the rim and non-increasing outward, and cleared under a floor, so the shadow comes out as a clean translucent dark layer without the old wood's grain.
  3. **Layers.** The part itself is one opaque layer, its own pixels at the HD picture's colours with a soft edge (the found piece, blurred); round it lies the dark shadow layer (black, alpha by the profile, interpolated between
     the sectors). The two are one RGBA picture: part over shadow. Over the kit's own wood the part keeps its shading and shape and its shadow darkens the new grain the way the old one darkened the old wood.
  4. **Master.** The part's box (sheet units, in the HD frame's own coordinates, at 4 px per unit) is reduced by an exact area average of the premultiplied picture to the master size on the 6 px grid
     (`kit_check.snap`), written to `local/steamwoven-ui/masters/<id>.png` (ignored by git: derived from the player's own pack, SWI-PRV-1), ready for `bake.py`.

What the runtime draws on top stays out: the needle, the numbers in the pits and the dial's number pit text, the heart's and cross's live fill, the plus and minus buttons. The dial's E&G with its curls is the label part
of ARC-UI-3299: the dial part holds the paper under it rebuilt by `label_parts` (the same rule, the ground without the letters). The ruler's divisions 75 50 25 0 25 50 75 are in the ruler part (digits carry no language).

Standard library only, on `pngio`. Same input gives the same bytes. No original or derived pixel is in git: this file holds the rule and the places.
"""
from __future__ import annotations

import argparse
import math
import sys
from dataclasses import dataclass
from pathlib import Path

try:
    from . import fixtures, label_parts, pngio
except ImportError:  # run as a script
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    import fixtures  # type: ignore
    import label_parts  # type: ignore
    import pngio  # type: ignore

HD = label_parts.HD  # HD frame pixels per original unit
READ_MARGIN = 10  # units read around a part's box: the ring of unshadowed wood the shadow is measured against
SECTORS = 16  # directions the shadow's profile is measured in
REACH = 16  # px (4 units): the shadow is measured and drawn out to here
REFERENCE = (26, 40)  # px: the ring of unshadowed wood beyond the shadow (distance from the part)
MIN_BIN = 12  # a (sector, distance) bin with fewer pixels than this takes the profile of all sectors
MIN_PIECE = 150  # px: a found piece smaller than this is grain, not a part
FLOOR = 0.06  # a shadow alpha under this is the grain's noise, not a shadow
EDGE_SIGMA = 0.9  # px: softness of the part's edge
SHAPE_SIGMA = 2.0  # px: the found rim is smoothed this much before the shadow's distance is taken from it (a ragged rim would spike the shadow)
SHADOW_SIGMA = 1.2  # px: softness of the drawn shadow
EDGE_FADE = 6  # px: the shadow fades out over this much at the part's box edge, so a box that cuts the shadow shows no straight line
BLACK_LUMA, BRIGHT_LUMA, BRASS_CAST, BRASS_FLOOR = 10.0, 105.0, 0.5, 22.0


@dataclass(frozen=True)
class SolidPart:
    ident: str
    x: int  # the part's box in sheet units (the HD frame's coordinates)
    y: int
    width: int
    height: int
    anchor: tuple[int, int]  # a point inside the part itself (sheet units): picks the found piece
    repaint: tuple[tuple[int, int, int, int], ...]  # boxes (x0, y0, x1, y1) of lettering rebuilt to bare ground first (the dial's E&G is the label part of ARC-UI-3299)
    note: str


PARTS: tuple[SolidPart, ...] = (
    SolidPart("fixture.stat_oval", 167, 105, 80, 44, (207, 127), (),
              "the brass oval round a stat number with its two sockets and the black pit, cut from the first oval (ST) of the old HD frame and drawn eight times (ARC-UI-3321)"),
    SolidPart("fixture.align_dial", 3, 128, 109, 111, (56, 190), ((28, 146, 90, 170),),
              "the alignment dial: brass bezel, paper face with its ticks and the two end glyphs, the hub with its screw, the brass lower plate, the number pit; no needle, no E&G (ARC-UI-3321)"),
    SolidPart("fixture.health_disc", 405, 122, 87, 68, (448, 140), (),
              "the health disc: brass ring with two sockets, black pit, the red heart (ARC-UI-3321)"),
    SolidPart("fixture.fatigue_disc", 405, 192, 87, 67, (448, 225), (),
              "the fatigue disc: brass ring with two sockets, black pit, the blue cross (ARC-UI-3321)"),
    SolidPart("fixture.mt_ruler", 740, 60, 60, 332, (770, 223), (),
              "the magic-technology ruler: parchment strip with its ticks and the divisions 75 50 25 0 25 50 75, the magic sign, the technology gear, the two number caps (ARC-UI-3321)"),
)
BY_ID = {part.ident: part for part in PARTS}


# ---------------------------------------------------------------- small raster helpers (flat row-major lists, width w)

def classify(rgb: list[list[list[float]]]) -> list[list[bool]]:
    """Where the HD picture is part and not wood: a black pit, brass (a yellow cast), parchment or bright metal."""
    height, width = len(rgb[0]), len(rgb[0][0])
    out = []
    for y in range(height):
        row = []
        for x in range(width):
            r, g, b = rgb[0][y][x], rgb[1][y][x], rgb[2][y][x]
            light = label_parts.luma(r, g, b)
            cast = (r - b) / (r + 1.0)
            row.append(light < BLACK_LUMA or (cast > BRASS_CAST and light > BRASS_FLOOR) or light > BRIGHT_LUMA)
        out.append(row)
    return out


def erode(mask: list[list[bool]], steps: int) -> list[list[bool]]:
    inverse = [[not v for v in row] for row in mask]
    grown = label_parts.dilate(inverse, steps)
    return [[not v for v in row] for row in grown]


def fill_holes(mask: list[list[bool]]) -> list[list[bool]]:
    """Every pixel that is not reached from the border through the background is part of the shape."""
    height, width = len(mask), len(mask[0])
    outside = [[False] * width for _ in range(height)]
    stack = [(y, x) for y in range(height) for x in (0, width - 1) if not mask[y][x]] + [(y, x) for x in range(width) for y in (0, height - 1) if not mask[y][x]]
    for y, x in stack:
        outside[y][x] = True
    while stack:
        y, x = stack.pop()
        for ny, nx in ((y - 1, x), (y + 1, x), (y, x - 1), (y, x + 1)):
            if 0 <= ny < height and 0 <= nx < width and not mask[ny][nx] and not outside[ny][nx]:
                outside[ny][nx] = True
                stack.append((ny, nx))
    return [[not outside[y][x] for x in range(width)] for y in range(height)]


def pieces(mask: list[list[bool]], least: int) -> tuple[list[list[int]], list[int]]:
    """4-connected pieces of at least `least` pixels: label image (0 = none, 1.. = piece) and the size of each piece."""
    height, width = len(mask), len(mask[0])
    label = [[0] * width for _ in range(height)]
    seen = [[False] * width for _ in range(height)]
    sizes: list[int] = []
    for y0 in range(height):
        for x0 in range(width):
            if not mask[y0][x0] or seen[y0][x0]:
                continue
            piece, stack = [], [(y0, x0)]
            seen[y0][x0] = True
            while stack:
                y, x = stack.pop()
                piece.append((y, x))
                for ny, nx in ((y - 1, x), (y + 1, x), (y, x - 1), (y, x + 1)):
                    if 0 <= ny < height and 0 <= nx < width and mask[ny][nx] and not seen[ny][nx]:
                        seen[ny][nx] = True
                        stack.append((ny, nx))
            if len(piece) >= least:
                sizes.append(len(piece))
                for y, x in piece:
                    label[y][x] = len(sizes)
    return label, sizes


def nearest_piece(label: list[list[int]]) -> tuple[list[list[float]], list[list[int]], list[list[int]], list[list[int]]]:
    """Distance (px) from every pixel to the nearest pixel of any piece, and that pixel's piece and coordinates (a two-pass chamfer that carries the seed along: Euclidean to within a fraction of a pixel)."""
    height, width = len(label), len(label[0])
    far = float(height + width) * 2
    dist = [[0.0 if label[y][x] else far for x in range(width)] for y in range(height)]
    owner = [row[:] for row in label]
    sy = [[y if label[y][x] else -1 for x in range(width)] for y in range(height)]
    sx = [[x if label[y][x] else -1 for x in range(width)] for y in range(height)]

    def relax(y: int, x: int, ny: int, nx: int) -> None:
        if not (0 <= ny < height and 0 <= nx < width) or sy[ny][nx] < 0:
            return
        d = math.hypot(y - sy[ny][nx], x - sx[ny][nx])
        if d < dist[y][x]:
            dist[y][x], owner[y][x], sy[y][x], sx[y][x] = d, owner[ny][nx], sy[ny][nx], sx[ny][nx]
    for y in range(height):
        for x in range(width):
            if label[y][x]:
                continue
            relax(y, x, y - 1, x - 1); relax(y, x, y - 1, x); relax(y, x, y - 1, x + 1); relax(y, x, y, x - 1)
    for y in range(height - 1, -1, -1):
        for x in range(width - 1, -1, -1):
            if label[y][x]:
                continue
            relax(y, x, y + 1, x + 1); relax(y, x, y + 1, x); relax(y, x, y + 1, x - 1); relax(y, x, y, x + 1)
    return dist, owner, sy, sx


def median(values: list[float]) -> float:
    ordered = sorted(values)
    return ordered[len(ordered) // 2]


# ---------------------------------------------------------------- the cut

@dataclass
class Solid:
    """RGBA straight-alpha result of one part at HD px, plus what the checks compare."""
    width: int
    height: int
    rgb: list[list[list[float]]]  # [channel][y][x]
    alpha: list[list[float]]  # 0..1
    core: list[list[bool]]  # the found part
    profile: list[list[float]]  # [sector][distance px] shadow alpha


def find_part(rgb: list[list[list[float]]], anchor: tuple[int, int]) -> tuple[list[list[int]], int, list[list[int]]]:
    """The labelled pieces of the read box and the number of the one that holds `anchor` (px of the read box; the nearest piece if the anchor sits on a gap)."""
    found = classify(rgb)
    found = erode(label_parts.dilate(found, 3), 3)  # close the small gaps of the rim
    found = fill_holes(found)
    found = label_parts.dilate(erode(found, 3), 3)  # open the specks of grain away
    label, sizes = pieces(found, MIN_PIECE)
    if not sizes:
        raise ValueError("no part found in the read box")
    ax, ay = anchor
    target = label[ay][ax]
    if not target:
        dist, owner, _, _ = nearest_piece(label)
        target = owner[ay][ax]
    return label, target, label


def smooth_pieces(label: list[list[int]]) -> list[list[int]]:
    """The pieces with their rims smoothed (each piece blurred by SHAPE_SIGMA and cut at one half), so the distance to the rim, and the shadow drawn by it, do not follow the grain of the find."""
    height, width = len(label), len(label[0])
    count = max((v for row in label for v in row), default=0)
    out = [[0] * width for _ in range(height)]
    best = [[0.0] * width for _ in range(height)]
    for piece in range(1, count + 1):
        soft = label_parts.blur([[1.0 if label[y][x] == piece else 0.0 for x in range(width)] for y in range(height)], SHAPE_SIGMA)
        for y in range(height):
            for x in range(width):
                if soft[y][x] > 0.5 and soft[y][x] > best[y][x]:
                    best[y][x], out[y][x] = soft[y][x], piece
    return out


def shadow_profile(light: list[list[float]], dist: list[list[float]], owner: list[list[int]], sy: list[list[int]], sx: list[list[int]], target: int) -> list[list[float]]:
    """The shadow's alpha by (sector, distance px): the median luma of the bin against the median of the unshadowed ring beyond, smoothed, flat toward the rim, non-increasing outward, under a floor cleared, tapered at the reach."""
    height, width = len(light), len(light[0])
    bins = [[[] for _ in range(REACH + 1)] for _ in range(SECTORS)]
    rings: list[list[float]] = [[] for _ in range(SECTORS)]
    for y in range(height):
        for x in range(width):
            if owner[y][x] != target:
                continue
            d = dist[y][x]
            if d < 0.5 or d >= REFERENCE[1]:
                continue
            angle = math.atan2(y - sy[y][x], x - sx[y][x]) % (2 * math.pi)
            sector = int(angle / (2 * math.pi) * SECTORS) % SECTORS
            if d <= REACH + 0.5:
                bins[sector][int(round(d))].append(light[y][x])
            elif d >= REFERENCE[0]:
                rings[sector].append(light[y][x])
    everything_ring = [v for ring in rings for v in ring]
    if len(everything_ring) < MIN_BIN:
        raise ValueError("no ring of unshadowed wood beyond the part: the read box is too small")
    general_ring = median(everything_ring)
    general = [median([v for s in range(SECTORS) for v in bins[s][d]]) if any(bins[s][d] for s in range(SECTORS)) else general_ring for d in range(REACH + 1)]
    profile = []
    for sector in range(SECTORS):
        ground = median(rings[sector]) if len(rings[sector]) >= MIN_BIN * 4 else general_ring
        raw = [0.0] * (REACH + 1)
        for d in range(1, REACH + 1):
            tone = median(bins[sector][d]) if len(bins[sector][d]) >= MIN_BIN else general[d]
            raw[d] = min(max(1.0 - tone / max(ground, 1.0), 0.0), 1.0)
        smooth = raw[:]
        for d in range(1, REACH + 1):
            smooth[d] = 0.25 * raw[max(d - 1, 1)] + 0.5 * raw[d] + 0.25 * raw[min(d + 1, REACH)]
        peak = max(range(1, min(REACH, 9) + 1), key=lambda i: smooth[i])
        for d in range(1, peak):  # the rim's own edge blur makes the nearest bins read light: flat toward the rim
            smooth[d] = smooth[peak]
        for d in range(peak + 1, REACH + 1):  # non-increasing outward
            smooth[d] = min(smooth[d], smooth[d - 1])
        for d in range(1, REACH + 1):
            taper = min(max((REACH - d) / 4.0, 0.0), 1.0)
            smooth[d] = smooth[d] * taper
            if smooth[d] < FLOOR:
                smooth[d] = 0.0
        profile.append(smooth)
    return profile


def cut_solid(rgb: list[list[list[float]]], anchor: tuple[int, int], keep: tuple[int, int, int, int] | None = None) -> Solid:
    """`rgb` is the read box of the HD frame ([channel][y][x] floats, with unshadowed wood round the part); `anchor` a px inside the part; `keep` the part's own box (x0, y0, x1, y1) in px of the read box, the shadow fades out toward its edge."""
    height, width = len(rgb[0]), len(rgb[0][0])
    label, target, _ = find_part(rgb, anchor)
    label = smooth_pieces(label)
    dist, owner, sy, sx = nearest_piece(label)
    light = [[label_parts.luma(rgb[0][y][x], rgb[1][y][x], rgb[2][y][x]) for x in range(width)] for y in range(height)]
    profile = shadow_profile(light, dist, owner, sy, sx, target)
    core = [[label[y][x] == target for x in range(width)] for y in range(height)]
    soft = label_parts.blur([[1.0 if core[y][x] else 0.0 for x in range(width)] for y in range(height)], EDGE_SIGMA)
    shadow = [[0.0] * width for _ in range(height)]
    for y in range(height):
        for x in range(width):
            d = dist[y][x]
            if owner[y][x] == target and 0.0 < d <= REACH + 0.5:
                t = (math.atan2(y - sy[y][x], x - sx[y][x]) % (2 * math.pi)) / (2 * math.pi) * SECTORS - 0.5
                i0 = int(math.floor(t)) % SECTORS
                frac = t - math.floor(t)
                i1 = (i0 + 1) % SECTORS
                k = min(int(round(d)), REACH)
                shadow[y][x] = profile[i0][k] * (1 - frac) + profile[i1][k] * frac
    shadow = label_parts.blur(shadow, SHADOW_SIGMA)
    if keep is not None:
        kx0, ky0, kx1, ky1 = keep
        for y in range(height):
            for x in range(width):
                if shadow[y][x] > 0.0:
                    shadow[y][x] *= min(max(min(x - kx0, kx1 - 1 - x, y - ky0, ky1 - 1 - y) / EDGE_FADE, 0.0), 1.0)
    layer = [[[0.0] * width for _ in range(height)] for _ in range(3)]
    alpha = [[0.0] * width for _ in range(height)]
    for y in range(height):
        for x in range(width):
            a_core = min(max((soft[y][x] - 0.25) / 0.5, 0.0), 1.0)
            total = a_core + (1.0 - a_core) * (0.0 if core[y][x] else shadow[y][x])
            if total <= 0.0:
                continue
            alpha[y][x] = total
            for c in range(3):  # part over shadow; the shadow is black
                layer[c][y][x] = min(max(a_core * rgb[c][y][x] / total, 0.0), 255.0)
    return Solid(width, height, layer, alpha, core, profile)


# ---------------------------------------------------------------- parts

def build_part(frame: pngio.Image, part: SolidPart) -> pngio.Image:
    """The master of one part: the read box cut, cropped to the part's box on a transparent canvas at 4 px per unit, premultiplied, reduced to the part's master size, straight again."""
    x0, y0 = max(part.x - READ_MARGIN, 0), max(part.y - READ_MARGIN, 0)
    x1, y1 = min(part.x + part.width + READ_MARGIN, frame.w // HD), min(part.y + part.height + READ_MARGIN, frame.h // HD)
    read = label_parts.read_box(frame, (x0, y0, x1, y1))
    for box in part.repaint:  # lettering rebuilt to bare ground (the label part of ARC-UI-3299 draws it)
        ground = label_parts.cut_box(label_parts.read_box(frame, box), "paper", label_parts.dial_geometry(box)).ground
        ox, oy = (box[0] - x0) * HD, (box[1] - y0) * HD
        for c in range(3):
            for y, row in enumerate(ground[c]):
                read[c][oy + y][ox:ox + len(row)] = row
    anchor = ((part.anchor[0] - x0) * HD, (part.anchor[1] - y0) * HD)
    px_w, px_h = part.width * HD, part.height * HD
    ox, oy = (part.x - x0) * HD, (part.y - y0) * HD
    solid = cut_solid(read, anchor, (ox, oy, ox + px_w, oy + px_h))
    planes = [[[0.0] * px_w for _ in range(px_h)] for _ in range(4)]
    for y in range(px_h):
        for x in range(px_w):
            a = solid.alpha[oy + y][ox + x]
            if a <= 0.0:
                continue
            for c in range(3):
                planes[c][y][x] = solid.rgb[c][oy + y][ox + x] * a
            planes[3][y][x] = a * 255.0
    master_w, master_h = fixtures.snap(part.width), fixtures.snap(part.height)
    small = label_parts.area_resample(planes, master_w, master_h)
    out = pngio.Image.blank(master_w, master_h)
    for y in range(master_h):
        for x in range(master_w):
            a = small[3][y][x]
            i = (y * master_w + x) * 4
            if a < 0.5:
                continue
            for c in range(3):
                out.data[i + c] = int(min(max(round(small[c][y][x] * 255.0 / a), 0), 255))
            out.data[i + 3] = int(min(round(a), 255))
    return out


def build(frame_path: Path, out: Path, only: set[str] | None = None) -> list[str]:
    frame = pngio.read(frame_path)
    if (frame.w, frame.h) != (3200, 1600):
        raise ValueError(f"the HD frame of the sheet must be 3200 x 1600 (4x of art 22), got {frame.w} x {frame.h}")
    out.mkdir(parents=True, exist_ok=True)
    written = []
    for part in PARTS:
        if only and part.ident not in only:
            continue
        pngio.write(out / f"{part.ident}.png", build_part(frame, part))
        written.append(part.ident)
    return written


# ---------------------------------------------------------------- self-test on a synthetic frame (no game pixel)

def _synthetic(width: int, height: int, centre: tuple[int, int], radius: int):
    """Dark grainy wood with a brass disc (a black pit in it) and a cast shadow down and to the right, dark at the rim and gone 8 px beyond the shifted disc; returns the picture and the bare wood."""
    wood = [[[0.0] * width for _ in range(height)] for _ in range(3)]
    for y in range(height):
        grain = 5.0 * math.sin(y * 0.9) + 3.0 * math.sin(y * 2.3)
        for x in range(width):
            base = 52.0 + 0.04 * x + grain
            wood[0][y][x], wood[1][y][x], wood[2][y][x] = base * 1.15, base * 0.85, base * 0.62
    picture = [[row[:] for row in plane] for plane in wood]
    cx, cy = centre
    for y in range(height):
        for x in range(width):
            d = math.hypot(x - cx - 4, y - cy - 4) - radius  # the shadow's own disc is shifted down and right
            if d < 8.0:
                a = 0.6 if d <= 0 else 0.6 * (1.0 - d / 8.0)
                for c in range(3):
                    picture[c][y][x] = wood[c][y][x] * (1.0 - a)
    for y in range(height):
        for x in range(width):
            r = math.hypot(x - cx, y - cy)
            if r < radius:
                if r < radius * 0.55:
                    picture[0][y][x], picture[1][y][x], picture[2][y][x] = 1.0, 1.0, 1.0
                else:
                    shade = 1.0 - 0.3 * (y - cy) / radius
                    picture[0][y][x], picture[1][y][x], picture[2][y][x] = 170.0 * shade, 125.0 * shade, 55.0 * shade
    return picture, wood


def self_test() -> int:
    problems = []
    for part in PARTS:
        if part.x + part.width > 800 or part.y + part.height > 400:
            problems.append(f"{part.ident}: the part's box leaves the sheet's art")
        if not (part.x <= part.anchor[0] < part.x + part.width and part.y <= part.anchor[1] < part.y + part.height):
            problems.append(f"{part.ident}: the anchor is outside the part's box")
        if part.ident not in fixtures.BY_ID or fixtures.BY_ID[part.ident].master != (fixtures.snap(part.width), fixtures.snap(part.height)):
            problems.append(f"{part.ident}: not a fixture of the kit or its size differs from the fixture's")
    width, height, centre, radius = 240, 240, (120, 120), 70
    picture, wood = _synthetic(width, height, centre, radius)
    solid = cut_solid(picture, centre)
    again = cut_solid(picture, centre)
    if solid.alpha != again.alpha or solid.rgb != again.rgb:
        problems.append("cut is not deterministic")
    if not solid.core[120][120] or not solid.core[120][70] or solid.core[5][5]:
        problems.append("the part is not found where it is (the pit, the rim) or is found where the wood is")
    if abs(sum(1 for row in solid.core for v in row if v) - math.pi * radius * radius) > 0.04 * math.pi * radius * radius:
        problems.append("the found part's area is not the disc's")
    if solid.alpha[120][120] < 0.999 or solid.alpha[120][70 + 5] < 0.999:
        problems.append("the part's inside is not opaque")
    if solid.alpha[5][5] != 0.0:
        problems.append("alpha far from the part is not zero")
    # the shadow: strongest down and right, none up and left, non-increasing outward, tapered to zero at the reach
    sector_down_right = int((math.atan2(1, 1) % (2 * math.pi)) / (2 * math.pi) * SECTORS) % SECTORS
    sector_up_left = (sector_down_right + SECTORS // 2) % SECTORS
    if solid.profile[sector_down_right][2] < 0.3:
        problems.append("the shadow on the lit-away side is not found")
    if max(solid.profile[sector_up_left]) > 0.25:
        problems.append("a shadow is found on the lit side")
    for s, row in enumerate(solid.profile):
        if any(row[d] > row[d - 1] + 1e-9 for d in range(max(row.index(max(row)), 1) + 1, REACH + 1)):
            problems.append(f"sector {s}: the shadow's profile rises outward")
        if row[REACH] != 0.0 and row[REACH] > 0.01:
            problems.append(f"sector {s}: the shadow does not taper to zero at the reach")
    # over the bare wood the layer gives the shadow back: wood * (1 - alpha) near the rim down-right is close to the picture
    worst = 0.0
    for y in range(height):
        for x in range(width):
            if solid.core[y][x] or solid.alpha[y][x] <= 0.0:
                continue
            back = [wood[c][y][x] * (1.0 - solid.alpha[y][x]) + solid.rgb[c][y][x] * solid.alpha[y][x] for c in range(3)]
            worst = max(worst, max(abs(back[c] - picture[c][y][x]) for c in range(3)))
    if worst > 22.0:
        problems.append(f"layer over the bare wood does not give the shadow back (worst {worst:.1f} levels)")
    for problem in problems:
        print("SHEET PARTS FAIL:", problem)
    if problems:
        return 1
    print(f"SHEET PARTS PASS: {len(PARTS)} parts, deterministic cut, the part found, the shadow measured by sector (down-right {solid.profile[sector_down_right][2]:.2f} at 2 px), layer over bare wood within {worst:.0f} levels of the picture")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("list")
    build_parser = sub.add_parser("build")
    build_parser.add_argument("--hd-frame", type=Path, required=True, help="the HD pack's frame of art 22 (3200 x 1600)")
    build_parser.add_argument("--out", type=Path, default=None, help="default: local/steamwoven-ui/masters of the main checkout")
    build_parser.add_argument("--only", default="")
    sub.add_parser("self-test")
    args = parser.parse_args(argv)
    if args.command == "list":
        for part in PARTS:
            master = (fixtures.snap(part.width), fixtures.snap(part.height))
            print(f"{part.ident}  at {part.x}, {part.y}  {part.width} x {part.height} units  master {master[0]} x {master[1]}")
        return 0
    if args.command == "self-test":
        return self_test()
    only = {item for item in args.only.split(",") if item}
    unknown = only - set(BY_ID)
    if unknown:
        print("unknown part:", ", ".join(sorted(unknown)), file=sys.stderr)
        return 2
    try:
        done = build(args.hd_frame, args.out or fixtures.default_out(), only or None)
    except (ValueError, pngio.PngError, OSError) as error:
        print("SHEET PARTS FAIL:", error, file=sys.stderr)
        return 2
    print("SHEET PARTS wrote", ", ".join(done))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
