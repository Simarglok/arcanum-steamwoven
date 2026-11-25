"""The symbol of a round button read from the player's own HD pack frame (ARC-UI-3204, owner decision 2026-11-25).

The pack's generated HD frame of a college tab or a skill-school button shows the symbol clean and correct (near-black on the coloured dome). This module cuts the symbol
out of that frame (a black top-hat: the symbol is much darker than the dome around it and thicker than the dome's cracks), and fits it to the place the symbol has in the original
frame by a shift and a uniform scale (never a deformation). The fit is soft: its score and the share of the original's contour that lies near the fitted symbol are only
reported (a strong drift is flagged), nothing is rejected; the owner decides by eye whether it is the same symbol.

Pure Python, no Godot, no original data in the repository: the pack frames are read from the local, ignored pack.
"""
from __future__ import annotations

import hashlib
import math
import sys
from dataclasses import dataclass
from pathlib import Path

HERE = Path(__file__).resolve().parent
try:
    from . import button_kit, pngio
except ImportError:  # run as a script
    sys.path.insert(0, str(HERE))
    import button_kit  # type: ignore
    import pngio  # type: ignore

Image = pngio.Image

PACK_DIR = "hd-packs/interface-redraw-4x"   # under `local/`
TOP_HAT_RADIUS = 6          # HD px: the window of the closing that fills the symbol's strokes and the dome's cracks (strokes are narrower than twice this)
TOP_HAT_DEPTH = 12.0        # CIE L: a pixel this much darker than its closing is a candidate
SYMBOL_RATIO = 0.55         # a symbol pixel is darker than this share of the dome's lightness around it (the closing), so the symbol in the highlight, lighter than the rest, is still found
SYMBOL_MAX_L = 60.0         # CIE L: and no lighter than this
REACH = 0.80                # a symbol pixel lies within this share of the dome's radius
FAR_PIECE = 0.55            # a piece of the mask whose nearest pixel is beyond this share of the dome's radius is the dome's rim shade, not a symbol
MIN_AREA = 30               # HD px: a piece with fewer pixels is a speck (a crack)
RECOVER = 2                 # HD px: the thin ends of the symbol that the opening cut away are recovered this far
BLUR_SIGMA = 0.9            # HD px: the Gaussian that smooths the mask's staircase before the outline is cut at one half
FIT_SCALES = (0.94, 0.97, 1.0, 1.03, 1.06)       # the fit's uniform scale about the dome's middle
FIT_SHIFTS = (-1.0, -0.5, 0.0, 0.5, 1.0)         # the fit's shift, original cells
DRIFT_NEAR = 3.0            # original cells: the original's contour counts as met when the fitted symbol is this near
DRIFT_FLAG = 0.55           # a share of the contour under this is flagged (a strong drift), the symbol is still used

N8 = ((-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1))


class PackError(Exception):
    pass


def alpha_at(image: Image, x: int, y: int) -> int:
    return image.data[(y * image.w + x) * 4 + 3]


def pack_path(local: Path, name: str, number: int) -> tuple[Path, str]:
    """The pack frame of an art (`name`, frame `number`) and its sha12 from the pack's index."""
    root = local / PACK_DIR
    index = root / "index.tsv"
    if not index.is_file():
        raise PackError(f"no HD pack index at {index}")
    for line in index.read_text(encoding="utf-8").splitlines():
        fields = line.split("\t")
        if len(fields) > 12 and fields[2] == name and fields[3] == str(number):
            return root / fields[4], fields[12]
    raise PackError(f"the pack has no frame {number} of {name}")


def read_pack(local: Path, name: str, number: int) -> tuple[Image, str]:
    path, sha = pack_path(local, name, number)
    return pngio.read(path), sha


def _grey(values: list, w: int, h: int, r: int, pick) -> list:
    """A separable square max or min filter over the known (non-None) values."""
    tmp: list = [None] * (w * h)
    for y in range(h):
        row = values[y * w:(y + 1) * w]
        for x in range(w):
            window = [v for v in row[max(0, x - r):x + r + 1] if v is not None]
            tmp[y * w + x] = pick(window) if window else None
    out: list = [None] * (w * h)
    for x in range(w):
        for y in range(h):
            window = [tmp[j * w + x] for j in range(max(0, y - r), min(h, y + r + 1)) if tmp[j * w + x] is not None]
            out[y * w + x] = pick(window) if window else None
    return out


def _erode(mask: list[bool], w: int, h: int) -> list[bool]:
    out = [False] * (w * h)
    for i, on in enumerate(mask):
        if not on:
            continue
        x, y = i % w, i // w
        out[i] = all(0 <= x + dx < w and 0 <= y + dy < h and mask[(y + dy) * w + x + dx] for dx, dy in N8)
    return out


def _dilate_within(mask: list[bool], w: int, h: int, within: list[bool]) -> list[bool]:
    out = list(mask)
    for i in range(w * h):
        if out[i] or not within[i]:
            continue
        x, y = i % w, i // w
        if any(0 <= x + dx < w and 0 <= y + dy < h and mask[(y + dy) * w + x + dx] for dx, dy in N8):
            out[i] = True
    return out


def pieces(mask: list[bool], w: int, h: int) -> list[list[int]]:
    seen = [False] * (w * h)
    result = []
    for start in range(w * h):
        if not mask[start] or seen[start]:
            continue
        stack, piece = [start], []
        seen[start] = True
        while stack:
            i = stack.pop()
            piece.append(i)
            x, y = i % w, i // w
            for dx, dy in N8:
                if 0 <= x + dx < w and 0 <= y + dy < h:
                    j = (y + dy) * w + x + dx
                    if mask[j] and not seen[j]:
                        seen[j] = True
                        stack.append(j)
        result.append(piece)
    return result


def cut_symbol(frame: Image, disc: tuple[float, float, float], radius: int = TOP_HAT_RADIUS, depth: float = TOP_HAT_DEPTH, rim_reach: float | None = None) -> list[bool]:
    """The symbol of a pack frame as a mask of its pixels. `disc` is the dome's (x, y, radius) in the frame's own pixels; `radius` is the closing's window (a solid symbol needs a wider one than a line symbol), `depth` the margin in CIE L, `rim_reach` (a share of the dome's radius)
    the lower right's own limit: the dome's rim shade lies there and is no symbol."""
    w, h = frame.w, frame.h
    cx, cy, rd = disc
    light: list = [None] * (w * h)
    for y in range(h):
        for x in range(w):
            if alpha_at(frame, x, y) and math.hypot(x + 0.5 - cx, y + 0.5 - cy) <= 0.9 * rd:
                i = (y * w + x) * 4
                light[y * w + x] = button_kit.lab(*frame.data[i:i + 3])[0]
    closing = _grey(_grey(light, w, h, radius, max), w, h, radius, min)
    dark = [light[i] is not None and closing[i] is not None and math.hypot(i % w + 0.5 - cx, i // w + 0.5 - cy) <= REACH * rd
            and closing[i] - light[i] > depth and light[i] < SYMBOL_MAX_L and light[i] < SYMBOL_RATIO * closing[i]
            and not (rim_reach is not None and i % w + 0.5 > cx and i // w + 0.5 > cy - (i % w + 0.5 - cx) and math.hypot(i % w + 0.5 - cx, i // w + 0.5 - cy) > rim_reach * rd) for i in range(w * h)]
    grown = _dilate_within(_erode(dark, w, h), w, h, dark)   # an opening: the cracks (thin) go, the strokes stay
    for _ in range(RECOVER):
        grown = _dilate_within(grown, w, h, dark)
    out = [False] * (w * h)
    for piece in pieces(grown, w, h):
        if len(piece) < MIN_AREA:
            continue
        if min(math.hypot(i % w + 0.5 - cx, i // w + 0.5 - cy) for i in piece) > FAR_PIECE * rd:
            continue
        for i in piece:
            out[i] = True
    return out


def blurred(mask: list[bool], w: int, h: int, sigma: float = BLUR_SIGMA) -> list[float]:
    """The mask as a field: a separable Gaussian, so that the outline cut at one half is a smooth curve."""
    radius = max(1, int(math.ceil(sigma * 3)))
    kernel = [math.exp(-(k * k) / (2 * sigma * sigma)) for k in range(-radius, radius + 1)]
    total = sum(kernel)
    kernel = [k / total for k in kernel]
    tmp = [0.0] * (w * h)
    for y in range(h):
        for x in range(w):
            acc = 0.0
            for k, weight in enumerate(kernel):
                xx = x + k - radius
                if 0 <= xx < w and mask[y * w + xx]:
                    acc += weight
            tmp[y * w + x] = acc
    out = [0.0] * (w * h)
    for y in range(h):
        for x in range(w):
            acc = 0.0
            for k, weight in enumerate(kernel):
                yy = y + k - radius
                if 0 <= yy < h:
                    acc += weight * tmp[yy * w + x]
            out[y * w + x] = acc
    return out


@dataclass(frozen=True)
class Fit:
    """How the pack's symbol maps onto the original frame: a uniform `scale` about the dome's middle and a shift (`dx`, `dy`) in the original's cells."""
    scale: float
    dx: float
    dy: float
    score: float      # the overlap (IoU) of the fitted symbol and the original's symbol mask
    contour: float    # the share of the original's contour within DRIFT_NEAR cells of the fitted symbol
    flagged: bool     # a strong drift


@dataclass(frozen=True)
class PackSymbol:
    """The cut symbol of a pack frame with the fit that places it on the original frame."""
    mask: tuple              # the symbol's pixels in the pack frame (bool)
    field: tuple             # the same, blurred
    w: int
    h: int
    sx: float                # pack pixels per original cell, along x and y
    sy: float
    centre: tuple[float, float]   # the dome's middle in the original's cells
    fit: Fit
    sha12: str


def to_pack(symbol_centre: tuple[float, float], sx: float, sy: float, scale: float, dx: float, dy: float, ox: float, oy: float) -> tuple[float, float]:
    """A point of the original frame (cells) to the pack frame's pixels: the inverse of the fit (the pack symbol is scaled by `scale` about the middle, then shifted)."""
    cx, cy = symbol_centre
    return (cx + (ox - dx - cx) / scale) * sx, (cy + (oy - dy - cy) / scale) * sy


def coverage_at_cells(field: tuple, w: int, h: int, centre: tuple[float, float], sx: float, sy: float, scale: float, dx: float, dy: float, cw: int, ch: int) -> list[float]:
    """The fitted symbol's coverage of each original cell (3 x 3 samples a cell)."""
    out = [0.0] * (cw * ch)
    for j in range(ch):
        for i in range(cw):
            count = 0
            for b in range(3):
                for a in range(3):
                    px, py = to_pack(centre, sx, sy, scale, dx, dy, i + (a + 0.5) / 3, j + (b + 0.5) / 3)
                    xx, yy = int(px), int(py)
                    if 0 <= xx < w and 0 <= yy < h and field[yy * w + xx] >= 0.5:
                        count += 1
            out[j * cw + i] = count / 9.0
    return out


def _iou(cover: list[float], original: list[bool]) -> float:
    inter = sum(min(c, 1.0) for c, o in zip(cover, original) if o)
    union = sum(max(c, 1.0 if o else 0.0) for c, o in zip(cover, original))
    return inter / union if union else 0.0


def contour_share(cover: list[float], original: list[bool], cw: int, ch: int) -> float:
    """The share of the original symbol's contour cells that lie within DRIFT_NEAR cells of the fitted symbol."""
    drawn = [(i % cw, i // cw) for i, c in enumerate(cover) if c >= 0.5]
    edge = [(i % cw, i // cw) for i, on in enumerate(original) if on and any(
        not (0 <= i % cw + dx < cw and 0 <= i // cw + dy < ch) or not original[(i // cw + dy) * cw + i % cw + dx] for dx, dy in N8)]
    if not edge:
        return 1.0
    if not drawn:
        return 0.0
    met = sum(1 for (x, y) in edge if min(math.hypot(x - u, y - v) for u, v in drawn) <= DRIFT_NEAR)
    return met / len(edge)


def fit_symbol(mask: list[bool], w: int, h: int, original: list[bool], cw: int, ch: int, centre: tuple[float, float]) -> tuple[Fit, tuple]:
    """Search the shift and the uniform scale that put the pack's symbol over the original's symbol mask best; report (soft, never reject) how well it meets the original's contour."""
    sx, sy = w / cw, h / ch
    field = tuple(blurred(mask, w, h))
    best = (-1.0, 1.0, 0.0, 0.0)
    for scale in FIT_SCALES:
        for dx in FIT_SHIFTS:
            for dy in FIT_SHIFTS:
                score = _iou(coverage_at_cells(field, w, h, centre, sx, sy, scale, dx, dy, cw, ch), original)
                if score > best[0] + 1e-9:
                    best = (score, scale, dx, dy)
    score, scale, dx, dy = best
    cover = coverage_at_cells(field, w, h, centre, sx, sy, scale, dx, dy, cw, ch)
    share = contour_share(cover, original, cw, ch)
    return Fit(scale, dx, dy, round(score, 3), round(share, 3), share < DRIFT_FLAG), field


def pack_symbol(local: Path, name: str, idle_size: tuple[int, int], disc: tuple[float, float, float], original: list[bool], cut: dict | None = None) -> PackSymbol:
    """The pack's symbol of button `name`, cut from its idle frame and fitted to the original's symbol mask (`original`, in the idle frame's cells; `disc` = the original dome's (x, y, radius), the dome without the brass ring)."""
    frame, sha = read_pack(local, name, 0)
    cw, ch = idle_size
    sx, sy = frame.w / cw, frame.h / ch
    cx, cy, rd = disc
    mask = cut_symbol(frame, (cx * sx, cy * sy, rd * (sx + sy) / 2), **(cut or {}))
    if not any(mask):
        raise PackError(f"{name}: no symbol found in the pack frame")
    fit, field = fit_symbol(mask, frame.w, frame.h, original, cw, ch, (cx, cy))
    return PackSymbol(tuple(mask), field, frame.w, frame.h, sx, sy, (cx, cy), fit, sha)


def sha12(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()[:12]
