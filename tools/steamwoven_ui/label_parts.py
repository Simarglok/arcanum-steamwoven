#!/usr/bin/env python3
"""ARC-UI-3299: the character sheet's permanent labels as kit parts, cut out of the player's own HD frame of the sheet.

    python3 -B tools/steamwoven_ui/label_parts.py list
    python3 -B tools/steamwoven_ui/label_parts.py build --hd-frame HD_char_maint.png --original-frame ORIG_char_maint.png [--out DIR] [--only ID,...]
    python3 -B tools/steamwoven_ui/label_parts.py self-test

The sheet's art 22 bakes in the eight stat names (ST CN DX BE, IN WP PE CH) and the alignment dial's "E&G" with its two curls. The kit surface carries none of them (ARC-UI-3165), so the sheet needs them back
in the old HD version's look: light silvery letters with their embossed edge and shadow, in the heading typeface (owner decision 2026-11-25, option "v", #3299). The game's own fonts do not give that look and cannot
give the dial's curls, so the labels are kit parts (fixed English text: another language needs its own set of parts) cut from the frame of the earlier generative HD version, which the owner likes:

  1. **Rebuild the ground under the lettering.** In the label's box the letters (and their edge and shadow) are found in the HD picture itself (brighter than the wood by 40 levels, or darker than the paper) inside the place the original's own letters
     of art 22 give (grown by 3.5 units: the wood has bright grain streaks that are no lettering), the find is grown (2.5 units for the wood, 1.75 for the dial, which holds scale ticks nearby), and the ground there is rebuilt row by row by a straight blend between the clean pixels at the two ends of each gap, so the horizontal grain
     of the wood and the paper's slow shading survive; a light blur (0.8 px) of the rebuilt part takes out the banding.
  2. **Difference frame.** HD picture minus the rebuilt ground, with a dead zone for the rebuild's residue: where the picture is lighter the difference is the letter's fill and lit edge (alpha by the strength of
     the difference), where it is darker it is the embossed shadow (a dark translucent layer, alpha by the strength). The colour of each layer pixel is solved so that layer over the rebuilt ground gives the HD
     picture back (to within the dead zone and the rebuild's residue, a few levels), so over the kit's own wood the letter keeps its shape, edge and shadow and takes the new grain.
  3. **Master.** Each part is one transparent picture at the part's master size (the unit size x 3.6 on the 6 px grid, `kit_check.snap`), the area average of the 4 px per unit cut (premultiplied), written to
     `local/steamwoven-ui/masters/<id>.png` (ignored by git: derived from the player's own pack, SWI-PRV-1), ready for `bake.py`. The viewer draws it at its place and writes the text itself only where the kit
     has no part (`AreaKitStandIns.SheetLabelParts`).

Standard library only, on `pngio`. Same input gives the same bytes. No original or derived pixel is in git: this file holds the rule and the places.
"""
from __future__ import annotations

import argparse
import math
import sys
from dataclasses import dataclass
from pathlib import Path

try:
    from . import fixtures, pngio
except ImportError:  # run as a script
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    import fixtures  # type: ignore
    import pngio  # type: ignore

HD = 4  # HD frame pixels per original unit (the pack's 4x art)
ALPHA_FLOOR = 0.12  # a layer pixel fainter than this is the rebuild's residue, not lettering
SPECK = 60  # a connected piece of the layer under this many HD px is a speck of grain
PAD = 6  # px kept clean at both ends of a box so the rebuild has clean ground to blend between


@dataclass(frozen=True)
class Part:
    ident: str
    x: int  # place in the sheet's units
    y: int
    width: int
    height: int
    boxes: tuple[tuple[int, int, int, int], ...]  # one cut per label (x0, y0, x1, y1) in sheet units, inside the part
    kind: str  # "wood" (light letters on wood) or "paper" (dark letters on the dial's paper)
    note: str


STAT_ROWS = tuple(108 + 40 * r for r in range(4))
PARTS: tuple[Part, ...] = (
    Part("fixture.sheet_stat_names_l", 120, 108, 50, 160, tuple((122, y, 168, y + 38) for y in STAT_ROWS), "wood",
         "the sheet's stat names ST CN DX BE at 131, 115 + 40 r, light silver letters with their embossed edge and shadow on the wood, cut from the old HD frame (ARC-UI-3299)"),
    Part("fixture.sheet_stat_names_r", 255, 108, 55, 160, tuple((256, y, 310, y + 38) for y in STAT_ROWS), "wood",
         "the sheet's stat names IN WP PE CH at 263, 115 + 40 r, the same cut (ARC-UI-3299)"),
    Part("fixture.sheet_dial_letters", 25, 146, 65, 25, ((28, 146, 90, 170),), "paper",
         "the alignment dial's E&G with its two curls at 45..70, 153, dark ink on the dial's paper, cut from the old HD frame (ARC-UI-3299)"),
)
BY_ID = {part.ident: part for part in PARTS}


# ---------------------------------------------------------------- small numeric helpers on rows of floats

def luma(r: float, g: float, b: float) -> float:
    return 0.299 * r + 0.587 * g + 0.114 * b


def gauss_kernel(sigma: float) -> list[float]:
    radius = max(1, int(math.ceil(3 * sigma)))
    weights = [math.exp(-(i * i) / (2 * sigma * sigma)) for i in range(-radius, radius + 1)]
    total = sum(weights)
    return [w / total for w in weights]


def blur(rows: list[list[float]], sigma: float) -> list[list[float]]:
    """Separable Gaussian, edges replicated."""
    kernel = gauss_kernel(sigma)
    radius = len(kernel) // 2
    height, width = len(rows), len(rows[0])
    horizontal = []
    for row in rows:
        padded = [row[0]] * radius + row + [row[-1]] * radius
        horizontal.append([sum(kernel[k] * padded[x + k] for k in range(len(kernel))) for x in range(width)])
    out = [[0.0] * width for _ in range(height)]
    for x in range(width):
        column = [horizontal[0][x]] * radius + [horizontal[y][x] for y in range(height)] + [horizontal[-1][x]] * radius
        for y in range(height):
            out[y][x] = sum(kernel[k] * column[y + k] for k in range(len(kernel)))
    return out


def dilate(mask: list[list[bool]], steps: int) -> list[list[bool]]:
    """Cross-shaped dilation by `steps` pixels (L1 distance), exact, by a two-pass distance transform."""
    height, width = len(mask), len(mask[0])
    far = height + width + steps + 1
    dist = [[0 if mask[y][x] else far for x in range(width)] for y in range(height)]
    for y in range(height):
        for x in range(width):
            d = dist[y][x]
            if y and dist[y - 1][x] + 1 < d: d = dist[y - 1][x] + 1
            if x and dist[y][x - 1] + 1 < d: d = dist[y][x - 1] + 1
            dist[y][x] = d
    for y in range(height - 1, -1, -1):
        for x in range(width - 1, -1, -1):
            d = dist[y][x]
            if y < height - 1 and dist[y + 1][x] + 1 < d: d = dist[y + 1][x] + 1
            if x < width - 1 and dist[y][x + 1] + 1 < d: d = dist[y][x + 1] + 1
            dist[y][x] = d
    return [[dist[y][x] <= steps for x in range(width)] for y in range(height)]


def drop_specks(alpha: list[list[float]], least: int) -> None:
    """Zero every 8-connected piece of the layer that is smaller than `least` pixels (a speck of grain, not lettering)."""
    height, width = len(alpha), len(alpha[0])
    seen = [[False] * width for _ in range(height)]
    for y0 in range(height):
        for x0 in range(width):
            if alpha[y0][x0] <= 0.0 or seen[y0][x0]:
                continue
            piece, stack = [], [(y0, x0)]
            seen[y0][x0] = True
            while stack:
                y, x = stack.pop()
                piece.append((y, x))
                for ny in range(max(y - 1, 0), min(y + 2, height)):
                    for nx in range(max(x - 1, 0), min(x + 2, width)):
                        if alpha[ny][nx] > 0.0 and not seen[ny][nx]:
                            seen[ny][nx] = True
                            stack.append((ny, nx))
            if len(piece) < least:
                for y, x in piece:
                    alpha[y][x] = 0.0


def row_interpolate(chan: list[list[float]], mask: list[list[bool]]) -> list[list[float]]:
    """Fill the masked pixels of every row by a straight blend between the mean of the 6 clean pixels at each end of a gap (the horizontal grain survives)."""
    out = [row[:] for row in chan]
    width = len(chan[0])
    for y, row in enumerate(mask):
        x = 0
        while x < width:
            if not row[x]:
                x += 1
                continue
            end = x
            while end + 1 < width and row[end + 1]:
                end += 1
            a, b = x - 1, end + 1
            left = sum(chan[y][max(a - 5, 0):a + 1]) / len(chan[y][max(a - 5, 0):a + 1]) if a >= 0 else None
            right = sum(chan[y][b:b + 6]) / len(chan[y][b:b + 6]) if b < width else None
            if left is None: left = right
            if right is None: right = left
            for i in range(x, end + 1):
                t = (i - a) / max(b - a, 1)
                out[y][i] = left * (1 - t) + right * t
            x = end + 1
    return out


# ---------------------------------------------------------------- the cut

@dataclass
class Cut:
    """RGBA straight-alpha result of one label box at HD px, plus what the checks compare."""
    width: int
    height: int
    rgb: list[list[list[float]]]  # [channel][y][x]
    alpha: list[list[float]]  # 0..1
    ground: list[list[list[float]]]  # the rebuilt ground [channel][y][x]


def cut_box(rgb: list[list[list[float]]], kind: str, geometry=None, region: list[list[bool]] | None = None) -> Cut:
    """`rgb` is the box of the HD frame, [channel][y][x] floats. `kind` "wood" or "paper"; `geometry(x, y) -> bool` limits where the paper's letters can be (the dial keeps scale ticks near its corners); `region` (the original's letters grown by 3.5 units) limits where the wood's letters, their edge and shadow can be: the wood has bright grain streaks that are no lettering."""
    height, width = len(rgb[0]), len(rgb[0][0])
    light = [[luma(rgb[0][y][x], rgb[1][y][x], rgb[2][y][x]) for x in range(width)] for y in range(height)]
    flat = sorted(v for row in light for v in row)
    if kind == "wood":
        median = flat[len(flat) // 2]
        rough = [[light[y][x] > median + 40 and (region is None or region[y][x]) for x in range(width)] for y in range(height)]
        grow, dead, light_to, dark_to = 10, 8.0, 200.0, 10.0
    else:
        paper = flat[int(len(flat) * 0.7)]
        rough = [[light[y][x] < paper - 25 and (geometry is None or geometry(x, y)) for x in range(width)] for y in range(height)]
        grow, dead, light_to, dark_to = 7, 5.0, 245.0, 25.0
    mask = dilate(rough, grow)
    if region is not None:
        mask = [[mask[y][x] and region[y][x] for x in range(width)] for y in range(height)]
    for y in range(height):  # a clean margin at both ends
        for x in list(range(PAD)) + list(range(width - PAD, width)):
            mask[y][x] = False
    ground = []
    for channel in rgb:
        rebuilt = row_interpolate(channel, mask)
        soft = blur(rebuilt, 0.8)
        ground.append([[soft[y][x] if mask[y][x] else rebuilt[y][x] for x in range(width)] for y in range(height)])
    base = [[luma(ground[0][y][x], ground[1][y][x], ground[2][y][x]) for x in range(width)] for y in range(height)]
    diff = []
    for y in range(height):
        row = []
        for x in range(width):
            d = light[y][x] - base[y][x]
            row.append(math.copysign(max(abs(d) - dead, 0.0), d))
        diff.append(row)
    diff = blur(diff, 0.5)
    feather = blur([[1.0 if mask[y][x] else 0.0 for x in range(width)] for y in range(height)], 2.0)
    alpha = []
    for y in range(height):
        row = []
        for x in range(width):
            d, b = diff[y][x], base[y][x]
            a_light = min(max(d / max(light_to - b, 20.0), 0.0), 1.0)
            a_dark = min(max(-d / max(b - dark_to, 20.0), 0.0), 1.0)
            row.append(min(max(max(a_light, a_dark) * feather[y][x], 0.0), 1.0))
        alpha.append(row)
    # the residue of the rebuild (grain the interpolation could not give back) is a faint alpha whose solved colour is a wild amplification of a few levels: drop it
    for y in range(height):
        for x in range(width):
            if alpha[y][x] < ALPHA_FLOOR:
                alpha[y][x] = 0.0
    drop_specks(alpha, SPECK)
    # a layer pixel must be able to give the picture back with a colour in 0..255: a thin shadow that the blur thinned gets the alpha its darkness needs
    for y in range(height):
        for x in range(width):
            if alpha[y][x] > 0.0:
                need = 0.0
                for c in range(3):
                    g, h = ground[c][y][x], rgb[c][y][x]
                    need = max(need, (g - h) / max(g, 1.0) if h < g else (h - g) / max(255.0 - g, 1.0))
                alpha[y][x] = min(max(alpha[y][x], need), 1.0)
    layer = []
    for c in range(3):
        layer.append([[min(max((rgb[c][y][x] - (1 - alpha[y][x]) * ground[c][y][x]) / max(alpha[y][x], 0.04), 0.0), 255.0) for x in range(width)] for y in range(height)])
    return Cut(width, height, layer, alpha, ground)


def dial_geometry(box: tuple[int, int, int, int]):
    """Where the dial's letters and curls can be inside the box (sheet units x 4 px): the band between the scale ticks and the hub, without the dark bezel corners (left: a diagonal; right: the top rows)."""
    x0, y0 = box[0], box[1]

    def inside(px: int, py: int) -> bool:
        ux, uy = x0 + px / HD, y0 + py / HD
        return 33 <= ux <= 83 and 151 <= uy <= 164.5 and ux >= 44 - 1.4 * (uy - 148) and not (uy <= 153 and ux >= 78)
    return inside


def recompose(cut: Cut) -> list[list[list[float]]]:
    """Layer over the rebuilt ground: reproduces the HD box (the check of the rule)."""
    return [[[cut.ground[c][y][x] * (1 - cut.alpha[y][x]) + cut.rgb[c][y][x] * cut.alpha[y][x] for x in range(cut.width)] for y in range(cut.height)] for c in range(3)]


# ---------------------------------------------------------------- parts

def read_box(image: pngio.Image, box: tuple[int, int, int, int]) -> list[list[list[float]]]:
    x0, y0, x1, y1 = (v * HD for v in box)
    rows = [[[0.0] * (x1 - x0) for _ in range(y1 - y0)] for _ in range(3)]
    for y in range(y0, y1):
        base = (y * image.w + x0) * 4
        for x in range(x1 - x0):
            i = base + x * 4
            for c in range(3):
                rows[c][y - y0][x] = float(image.data[i + c])
    return rows


def area_resample(planes: list[list[list[float]]], width: int, height: int, window: tuple[float, float, float, float] | None = None) -> list[list[list[float]]]:
    """Exact area average of every plane to width x height (any ratio), separably. `window` (x0, x1, y0, y1) in source px is the span the output covers (default the whole plane; what lies outside the plane counts as zero)."""
    src_h, src_w = len(planes[0]), len(planes[0][0])
    x0, x1, y0, y1 = window if window is not None else (0.0, float(src_w), 0.0, float(src_h))

    def weights(src: int, dst: int, first: float, last: float) -> list[list[tuple[int, float]]]:
        scale = (last - first) / dst
        table = []
        for d in range(dst):
            lo, hi = first + d * scale, first + (d + 1) * scale
            cells = []
            for s in range(max(int(math.floor(lo)), 0), min(int(math.ceil(hi)), src)):
                overlap = min(hi, s + 1) - max(lo, s)
                if overlap > 0:
                    cells.append((s, overlap / scale))
            table.append(cells)
        return table

    wx, wy = weights(src_w, width, x0, x1), weights(src_h, height, y0, y1)
    out = []
    for plane in planes:
        mid = [[sum(plane[y][s] * w for s, w in cells) for cells in wx] for y in range(src_h)]
        out.append([[sum(mid[s][x] * w for s, w in cells) for x in range(width)] for cells in wy])
    return out


def original_region(original: pngio.Image, box: tuple[int, int, int, int]) -> list[list[bool]]:
    """Where the lettering of a wood box can be, at 4 px per unit: the original's own letters of art 22 (light and grey: brighter than 100, spread of the channels under 32) in the box, grown by 3.5 units."""
    x0, y0, x1, y1 = box
    height, width = (y1 - y0), (x1 - x0)
    letters = [[False] * (width * HD) for _ in range(height * HD)]
    for y in range(height):
        for x in range(width):
            i = ((y0 + y) * original.w + x0 + x) * 4
            r, g, b = original.data[i], original.data[i + 1], original.data[i + 2]
            if luma(r, g, b) > 100 and max(r, g, b) - min(r, g, b) < 32:
                for dy in range(HD):
                    for dx in range(HD):
                        letters[y * HD + dy][x * HD + dx] = True
    return dilate(letters, 14)


def build_part(frame: pngio.Image, part: Part, original: pngio.Image | None = None) -> pngio.Image:
    """The master of one part: its label cuts laid on a transparent canvas at 4 px per unit, premultiplied, reduced to the part's master size, straight again."""
    px_w, px_h = part.width * HD, part.height * HD
    planes = [[[0.0] * px_w for _ in range(px_h)] for _ in range(4)]  # premultiplied r, g, b and alpha (0..255)
    for box in part.boxes:
        region = original_region(original, box) if part.kind == "wood" and original is not None else None
        cut = cut_box(read_box(frame, box), part.kind, dial_geometry(box) if part.kind == "paper" else None, region)
        ox, oy = (box[0] - part.x) * HD, (box[1] - part.y) * HD
        for y in range(cut.height):
            for x in range(cut.width):
                a = cut.alpha[y][x]
                if a <= 0:
                    continue
                for c in range(3):
                    planes[c][oy + y][ox + x] = cut.rgb[c][y][x] * a
                planes[3][oy + y][ox + x] = a * 255.0
    master_w, master_h = fixtures.snap(part.width), fixtures.snap(part.height)
    small = area_resample(planes, master_w, master_h)
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


def build(frame_path: Path, original_path: Path, out: Path, only: set[str] | None = None) -> list[str]:
    frame = pngio.read(frame_path)
    original = pngio.read(original_path)
    if (original.w, original.h) != (800, 400):
        raise ValueError(f"the original's art 22 must be 800 x 400, got {original.w} x {original.h}")
    if (frame.w, frame.h) != (3200, 1600):
        raise ValueError(f"the HD frame of the sheet must be 3200 x 1600 (4x of art 22), got {frame.w} x {frame.h}")
    out.mkdir(parents=True, exist_ok=True)
    written = []
    for part in PARTS:
        if only and part.ident not in only:
            continue
        pngio.write(out / f"{part.ident}.png", build_part(frame, part, original))
        written.append(part.ident)
    return written


# ---------------------------------------------------------------- self-test on a synthetic frame (no game pixel)

def _synthetic_wood(width: int, height: int) -> list[list[list[float]]]:
    """Dark wood with horizontal grain, a slow gradient and a bright slanted bar with a dark shadow beside it (a stand-in letter)."""
    rgb = [[[0.0] * width for _ in range(height)] for _ in range(3)]
    for y in range(height):
        grain = 6.0 * math.sin(y * 0.9) + 3.0 * math.sin(y * 2.3)
        for x in range(width):
            base = 62.0 + 0.05 * x + grain
            rgb[0][y][x], rgb[1][y][x], rgb[2][y][x] = base * 1.15, base * 0.85, base * 0.6
    for y in range(40, 120):
        for x in range(80, 92):
            shade = 0.45 if (x >= 90 or y >= 118) else 1.0  # shadow rim down-right
            if x < 90 and y < 118:
                rgb[0][y][x], rgb[1][y][x], rgb[2][y][x] = 190.0, 188.0, 184.0
            else:
                for c in range(3): rgb[c][y][x] *= shade
    return rgb


def self_test() -> int:
    problems = []
    for part in PARTS:
        if part.x + part.width < max(b[2] for b in part.boxes) or part.y + part.height < max(b[3] for b in part.boxes) or part.x > min(b[0] for b in part.boxes) or part.y > min(b[1] for b in part.boxes):
            problems.append(f"{part.ident}: a label box lies outside the part")
        if (part.width * 36) % 60 or (part.height * 36) % 60:
            problems.append(f"{part.ident}: {part.width} x {part.height} units is not a whole number of 6 px grid cells")
    rgb = _synthetic_wood(184, 152)
    cut = cut_box(rgb, "wood")
    again = cut_box(rgb, "wood")
    if cut.alpha != again.alpha:
        problems.append("cut is not deterministic")
    back = recompose(cut)
    worst = max(abs(back[c][y][x] - rgb[c][y][x]) for c in range(3) for y in range(cut.height) for x in range(cut.width))
    if worst > 6.0:
        problems.append(f"layer over the rebuilt ground does not give the picture back (worst {worst:.1f})")
    if cut.alpha[80][85] < 0.5:
        problems.append("the bright letter body has no alpha")
    if cut.alpha[10][10] != 0.0 and cut.alpha[10][10] > 0.02:
        problems.append("alpha far from the letter is not zero")
    if any(0.0 < a < ALPHA_FLOOR for row in cut.alpha for a in row):
        problems.append("a layer pixel fainter than the floor survived (its solved colour would be a wild amplification)")
    if cut.alpha[80][91] <= 0.02:
        problems.append("the dark shadow beside the letter has no alpha")
    reduced = area_resample([[[10.0] * 40 for _ in range(40)]], 36, 36)
    if abs(reduced[0][5][5] - 10.0) > 1e-9:
        problems.append("area resample changes a flat plane")
    for problem in problems:
        print("LABEL PARTS FAIL:", problem)
    if problems:
        return 1
    print(f"LABEL PARTS PASS: {len(PARTS)} parts, deterministic cut, layer over the rebuilt ground reproduces the picture (worst {worst:.2f} levels)")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("list")
    build_parser = sub.add_parser("build")
    build_parser.add_argument("--hd-frame", type=Path, required=True, help="the HD pack's frame of art 22 (3200 x 1600)")
    build_parser.add_argument("--original-frame", type=Path, required=True, help="the original's art 22 frame (800 x 400): tells where the wood's letters are")
    build_parser.add_argument("--out", type=Path, default=None, help="default: local/steamwoven-ui/masters of the main checkout")
    build_parser.add_argument("--only", default="")
    sub.add_parser("self-test")
    args = parser.parse_args(argv)
    if args.command == "list":
        for part in PARTS:
            master = (fixtures.snap(part.width), fixtures.snap(part.height))
            print(f"{part.ident}  at {part.x}, {part.y}  {part.width} x {part.height} units  master {master[0]} x {master[1]}  {part.kind}")
        return 0
    if args.command == "self-test":
        return self_test()
    only = {item for item in args.only.split(",") if item}
    unknown = only - set(BY_ID)
    if unknown:
        print("unknown part:", ", ".join(sorted(unknown)), file=sys.stderr)
        return 2
    try:
        done = build(args.hd_frame, args.original_frame, args.out or fixtures.default_out(), only or None)
    except (ValueError, pngio.PngError, OSError) as error:
        print("LABEL PARTS FAIL:", error, file=sys.stderr)
        return 2
    print("LABEL PARTS wrote", ", ".join(done))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
