#!/usr/bin/env python3
"""ARC-DEV-2972: the kit assembly tool of the Steamwoven interface (docs/ui/steamwoven-kit-assembly.md).

A repeated element is never drawn twice: a clean base of the material and a library of unique details are laid together
at the original coordinates, and the result is baked into the HD frame of the same original ART, one frame per original
frame, with the ART's size and alpha geometry (the pack's `legacy`, `legacySmooth` or `masked` rule). The layout file
(JSON, below) names the images; nothing here holds or produces original art, the images and outputs are local files.

    python3 -B tools/steamwoven_ui/assemble.py LAYOUT.json --out DIR [--dry-run] [--json]

`--dry-run` does everything (reads the images, composes, bakes, runs the alpha gates, prints the plan) and writes nothing.
Standard library only.
"""
from __future__ import annotations

import argparse
import json
import math
import sys
from dataclasses import dataclass, field
from fractions import Fraction
from pathlib import Path

try:
    from tools.steamwoven_ui import pngio
    from tools.steamwoven_ui.pngio import Image
except ImportError:  # run as a script: python3 tools/steamwoven_ui/assemble.py
    import pngio  # type: ignore[no-redef]
    from pngio import Image  # type: ignore[no-redef]

KIND = "arcanum-steamwoven-kit-layout"
VERSION = 1
ALPHA_MODES = ("legacy", "legacySmooth", "masked")
MIN_IOU_PERMILLE = 900  # the legacySmooth gate, fixed by the pack format
ANCHORS = ("tl", "center")
POSITIONS = ("snap", "exact")
MAX_SCALE = 16
DEFAULT_STATE = "default"
_TOP = {"kind", "version", "target", "scale", "master", "alpha", "base", "details", "placements", "frames"}


class LayoutError(Exception):
    """A layout file or an image that cannot be assembled; the message names the field."""


# --- exact arithmetic -------------------------------------------------------------------------------------------------

def parse_number(value, what: str) -> Fraction:
    """An integer, a decimal such as 3.6 or a ratio such as "18/5", read exactly (a float is read by its shortest text)."""
    if isinstance(value, bool):
        raise LayoutError(f"{what}: not a number")
    try:
        if isinstance(value, int):
            return Fraction(value)
        if isinstance(value, float):
            return Fraction(repr(value))
        if isinstance(value, str):
            return Fraction(value.strip())
    except (ValueError, ZeroDivisionError) as error:
        raise LayoutError(f"{what}: {value!r} is not a number") from error
    raise LayoutError(f"{what}: not a number")


def round_half_up(value: Fraction) -> int:
    return math.floor(value + Fraction(1, 2))


def units_to_px(units: Fraction, scale: Fraction, snap: int, position: str, what: str) -> int:
    """Original units to master pixels on the `snap` grid. `snap` rounds half up to the grid; `exact` demands a grid pixel."""
    exact = units * scale / snap
    if position == "exact":
        if exact.denominator != 1:
            raise LayoutError(f"{what}: {units} units at {scale} px per unit is {float(units * scale):g} px, not a whole multiple of {snap} px")
        return int(exact) * snap
    return round_half_up(exact) * snap


# --- the layout -------------------------------------------------------------------------------------------------------

@dataclass(frozen=True)
class ImageSet:
    """An image for every state (`images`), or one for all of them (`image`). Paths are resolved against the layout file."""
    default: Path | None
    by_state: dict[str, Path]

    def path_for(self, state: str, what: str) -> Path:
        if state in self.by_state:
            return self.by_state[state]
        if self.default is not None:
            return self.default
        raise LayoutError(f"{what}: no image for state {state!r}")


@dataclass(frozen=True)
class Placement:
    detail: str
    at: tuple[Fraction, Fraction]
    anchor: str
    frames: frozenset[int] | None
    states: frozenset[str] | None
    ident: str


@dataclass(frozen=True)
class Layout:
    art: str
    size: tuple[int, int]
    frame_states: tuple[str, ...]
    scale: int
    master_scale: Fraction
    snap: int
    position: str
    alpha_mode: str
    legacy: tuple[Path, ...]
    base: ImageSet
    details: dict[str, ImageSet]
    placements: tuple[Placement, ...]


def _object(value, what: str, allowed: set[str]) -> dict:
    if not isinstance(value, dict):
        raise LayoutError(f"{what}: must be an object")
    extra = sorted(set(value) - allowed)
    if extra:
        raise LayoutError(f"{what}: unknown field {extra[0]!r}")
    return value


def _int(value, what: str, low: int, high: int) -> int:
    if isinstance(value, bool) or not isinstance(value, int) or not low <= value <= high:
        raise LayoutError(f"{what}: must be an integer from {low} to {high}")
    return value


def _path(value, what: str, root: Path) -> Path:
    if not isinstance(value, str) or not value:
        raise LayoutError(f"{what}: must be a file path")
    path = Path(value)
    return path if path.is_absolute() else root / path


def _image_set(value, what: str, root: Path) -> ImageSet:
    value = _object(value, what, {"image", "images"})
    if ("image" in value) == ("images" in value):
        raise LayoutError(f"{what}: give exactly one of image and images")
    if "image" in value:
        return ImageSet(_path(value["image"], f"{what}.image", root), {})
    states = _object(value["images"], f"{what}.images", set(value["images"]) if isinstance(value["images"], dict) else set())
    if not states:
        raise LayoutError(f"{what}.images: no states")
    by_state = {str(state): _path(item, f"{what}.images.{state}", root) for state, item in states.items()}
    return ImageSet(by_state.pop(DEFAULT_STATE, None), by_state)


def parse_layout(data, root: Path) -> Layout:
    top = _object(data, "layout", _TOP)
    if top.get("kind") != KIND or top.get("version") != VERSION:
        raise LayoutError(f"layout: kind must be {KIND!r} and version {VERSION}")
    target = _object(top.get("target"), "target", {"art", "size", "frames"})
    art = target.get("art")
    if isinstance(art, int) and not isinstance(art, bool):
        art = str(art)
    if not isinstance(art, str) or not art or any(c in art for c in "/\\") or art in (".", ".."):
        raise LayoutError("target.art: must be an art identifier (a name or a number, no path)")
    size = target.get("size")
    if not isinstance(size, list) or len(size) != 2:
        raise LayoutError("target.size: must be [width, height] of the original frame")
    width, height = _int(size[0], "target.size[0]", 1, 4096), _int(size[1], "target.size[1]", 1, 4096)
    count = _int(target.get("frames"), "target.frames", 1, 255)
    scale = _int(top.get("scale"), "scale", 1, MAX_SCALE)

    master = _object(top.get("master", {}), "master", {"scale", "snap", "position"})
    master_scale = parse_number(master["scale"], "master.scale") if "scale" in master else Fraction(scale)
    if master_scale <= 0:
        raise LayoutError("master.scale: must be above 0")
    snap = _int(master.get("snap", 1), "master.snap", 1, 64)
    position = master.get("position", "snap")
    if position not in POSITIONS:
        raise LayoutError(f"master.position: must be one of {', '.join(POSITIONS)}")

    frames = top.get("frames")
    if frames is None:
        states = (DEFAULT_STATE,) * count
    else:
        if not isinstance(frames, list) or len(frames) != count:
            raise LayoutError(f"frames: must list one entry for each of the {count} original frames")
        states = tuple(str(_object(item, f"frames[{i}]", {"state"}).get("state", DEFAULT_STATE)) for i, item in enumerate(frames))

    alpha = _object(top.get("alpha"), "alpha", {"mode", "legacy"})
    mode = alpha.get("mode")
    if mode not in ALPHA_MODES:
        raise LayoutError(f"alpha.mode: must be one of {', '.join(ALPHA_MODES)}")
    legacy = alpha.get("legacy")
    if isinstance(legacy, str):
        legacy = [legacy] * count
    if not isinstance(legacy, list) or len(legacy) != count:
        raise LayoutError(f"alpha.legacy: must be one picture of the original frame's alpha, or one for each of the {count} frames")
    legacy_paths = tuple(_path(item, f"alpha.legacy[{i}]", root) for i, item in enumerate(legacy))

    base = _image_set(top.get("base"), "base", root)
    details_raw = top.get("details", {})
    if not isinstance(details_raw, dict):
        raise LayoutError("details: must be an object of detail ids")
    details = {str(key): _image_set(item, f"details.{key}", root) for key, item in details_raw.items()}

    placements = []
    for i, item in enumerate(top.get("placements", [])):
        item = _object(item, f"placements[{i}]", {"id", "detail", "at", "anchor", "frames", "states"})
        detail = item.get("detail")
        if detail not in details:
            raise LayoutError(f"placements[{i}].detail: {detail!r} is not in details")
        at = item.get("at")
        if not isinstance(at, list) or len(at) != 2:
            raise LayoutError(f"placements[{i}].at: must be [x, y] in original units")
        anchor = item.get("anchor", "tl")
        if anchor not in ANCHORS:
            raise LayoutError(f"placements[{i}].anchor: must be one of {', '.join(ANCHORS)}")
        only = item.get("frames")
        if only is not None:
            if not isinstance(only, list):
                raise LayoutError(f"placements[{i}].frames: must be a list of frame numbers")
            only = frozenset(_int(f, f"placements[{i}].frames", 0, count - 1) for f in only)
        named = item.get("states")
        if named is not None:
            if not isinstance(named, list) or not all(isinstance(s, str) for s in named):
                raise LayoutError(f"placements[{i}].states: must be a list of state names")
            named = frozenset(named)
        placements.append(Placement(detail, (parse_number(at[0], f"placements[{i}].at[0]"), parse_number(at[1], f"placements[{i}].at[1]")),
                                    anchor, only, named, str(item.get("id", i))))
    if len({p.ident for p in placements}) != len(placements):
        raise LayoutError("placements: ids must be unique")
    return Layout(art, (width, height), states, scale, master_scale, snap, position, mode, legacy_paths, base, details, tuple(placements))


def load_layout(path: Path) -> Layout:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as error:
        raise LayoutError(f"{path}: {error}") from error
    return parse_layout(data, path.resolve().parent)


# --- pixels -----------------------------------------------------------------------------------------------------------

def canvas_size(layout: Layout) -> tuple[int, int]:
    """The master canvas: the original frame size at the master scale, in whole pixels (half up)."""
    return (max(1, round_half_up(layout.size[0] * layout.master_scale)), max(1, round_half_up(layout.size[1] * layout.master_scale)))


def blit(canvas: Image, detail: Image, x0: int, y0: int) -> None:
    """Straight-alpha source over, in integers (the same pixels give the same bytes). An opaque pixel is copied exactly."""
    dst, src = canvas.data, detail.data
    for row in range(detail.h):
        s = row * detail.w * 4
        d = ((y0 + row) * canvas.w + x0) * 4
        for _ in range(detail.w):
            sa = src[s + 3]
            if sa == 255:
                dst[d:d + 4] = src[s:s + 4]
            elif sa:
                da = dst[d + 3]
                total = sa * 255 + da * (255 - sa)
                for c in range(3):
                    dst[d + c] = (2 * (src[s + c] * sa * 255 + dst[d + c] * da * (255 - sa)) + total) // (2 * total)
                dst[d + 3] = (2 * total + 255) // 510
            s += 4
            d += 4


def resample(image: Image, w: int, h: int) -> Image:
    """Area average of premultiplied pixels with exact integer weights (the whole input covers the whole output)."""
    if (image.w, image.h) == (w, h):
        return Image(w, h, bytearray(image.data))

    def spans(n_in: int, n_out: int) -> list[list[tuple[int, int]]]:
        result = []
        for i in range(n_out):
            lo, hi = i * n_in, (i + 1) * n_in
            result.append([(j, min(hi, (j + 1) * n_out) - max(lo, j * n_out)) for j in range(lo // n_out, min(n_in - 1, (hi - 1) // n_out) + 1)])
        return result

    columns, rows = spans(image.w, w), spans(image.h, h)
    middle = []  # per input row, per output column: (sum w*a, sum w*c*a for r, g, b)
    for y in range(image.h):
        line = []
        base = y * image.w * 4
        for column in columns:
            a_sum = r_sum = g_sum = b_sum = 0
            for j, weight in column:
                i = base + j * 4
                a = image.data[i + 3]
                a_sum += weight * a
                r_sum += weight * image.data[i] * a
                g_sum += weight * image.data[i + 1] * a
                b_sum += weight * image.data[i + 2] * a
            line.append((a_sum, r_sum, g_sum, b_sum))
        middle.append(line)
    out = bytearray(w * h * 4)
    full = image.w * image.h
    for oy, row in enumerate(rows):
        for ox in range(w):
            a_sum = r_sum = g_sum = b_sum = 0
            for j, weight in row:
                a, r, g, b = middle[j][ox]
                a_sum += weight * a
                r_sum += weight * r
                g_sum += weight * g
                b_sum += weight * b
            o = (oy * w + ox) * 4
            if a_sum:
                out[o] = (2 * r_sum + a_sum) // (2 * a_sum)
                out[o + 1] = (2 * g_sum + a_sum) // (2 * a_sum)
                out[o + 2] = (2 * b_sum + a_sum) // (2 * a_sum)
            out[o + 3] = (2 * a_sum + full) // (2 * full)
    return Image(w, h, out)


# --- alpha rules of the pack (docs/formats/hd-pack.md, src/Arcanum.Data/Hd/HdAlphaCheck.cs) --------------------------------

def _mask(legacy: Image, x: int, y: int) -> bool:
    return legacy.data[(y * legacy.w + x) * 4 + 3] >= 128


def _uniform_side(legacy: Image, x: int, y: int, side: bool) -> bool:
    for ny in range(max(0, y - 1), min(legacy.h, y + 2)):
        for nx in range(max(0, x - 1), min(legacy.w, x + 2)):
            if _mask(legacy, nx, ny) != side:
                return False
    return True


def _isolated(legacy: Image, x: int, y: int, side: bool) -> bool:
    near = 0
    for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
        if nx < 0 or ny < 0 or nx >= legacy.w or ny >= legacy.h:
            continue
        if _mask(legacy, nx, ny) == side:
            return False
        near += 1
    return near >= 2


def _near_opaque(legacy: Image, x: int, y: int) -> bool:
    return any(_mask(legacy, nx, ny) for ny in range(max(0, y - 1), min(legacy.h, y + 2)) for nx in range(max(0, x - 1), min(legacy.w, x + 2)))


def check_alpha(hd: Image, legacy: Image, scale: int, mode: str) -> list[str]:
    """Problems of an HD frame against the Legacy alpha under `mode` (empty: the frame passes the pack's check)."""
    if (hd.w, hd.h) != (legacy.w * scale, legacy.h * scale):
        return [f"size {hd.w}x{hd.h} is not {scale} x {legacy.w}x{legacy.h} = {legacy.w * scale}x{legacy.h * scale}"]
    inter = union = 0
    for y in range(hd.h):
        py = y // scale
        for x in range(hd.w):
            px = x // scale
            parent = legacy.data[(py * legacy.w + px) * 4 + 3]
            alpha = hd.data[(y * hd.w + x) * 4 + 3]
            if mode == "legacy":
                if alpha != parent:
                    return [f"alpha at {x},{y} is {alpha}, the original's is {parent}"]
                continue
            if mode == "masked":
                if alpha and parent < 128 and not _near_opaque(legacy, px, py):
                    return [f"alpha {alpha} at {x},{y} lies more than one original texel from the silhouette"]
                continue
            side = parent >= 128
            if _uniform_side(legacy, px, py, side):
                if alpha != parent:
                    return [f"alpha at {x},{y} is {alpha} inside a uniform area whose original alpha is {parent}"]
            elif _isolated(legacy, px, py, side):
                core = abs(2 * (x % scale) + 1 - scale) * 2 <= scale and abs(2 * (y % scale) + 1 - scale) * 2 <= scale
                if core and (alpha >= 128) != side:
                    return [f"the centre of the isolated texel {px},{py} (hole or speck) lost its side at {x},{y}"]
            a, b = alpha >= 128, parent >= 128
            inter += a and b
            union += a or b
    if mode == "legacySmooth" and inter * 1000 < union * MIN_IOU_PERMILLE:
        return [f"mask overlap {inter * 1000 // max(1, union)} permille is below {MIN_IOU_PERMILLE}"]
    return []


def _bilinear(plane: list[float], w: int, h: int, x: float, y: float) -> float:
    x = min(max(x, 0.0), w - 1.0)
    y = min(max(y, 0.0), h - 1.0)
    x0, y0 = int(x), int(y)
    x1, y1 = min(x0 + 1, w - 1), min(y0 + 1, h - 1)
    fx, fy = x - x0, y - y0
    top = plane[y0 * w + x0] * (1 - fx) + plane[y0 * w + x1] * fx
    bottom = plane[y1 * w + x0] * (1 - fx) + plane[y1 * w + x1] * fx
    return top * (1 - fy) + bottom * fy


def bake_alpha(color: Image, legacy: Image, scale: int, mode: str) -> tuple[Image, bool, int]:
    """The frame's alpha by `mode`, from the composed colour. Returns (frame, fell_back_to_exact_alpha, uncovered pixels).
    `uncovered` counts pixels that end up visible although nothing was drawn under them (the base must cover the silhouette).
    The smooth alpha is the Legacy mask enlarged bilinearly and steepened to about one HD pixel; a frame whose smooth alpha fails
    the gate takes the exact Legacy alpha, as the importer does."""
    w, h = legacy.w * scale, legacy.h * scale
    out = Image(w, h, bytearray(color.data))
    uncovered = 0
    if mode == "masked":
        for y in range(h):
            for x in range(w):
                o = (y * w + x) * 4 + 3
                if out.data[o] and not _near_opaque(legacy, x // scale, y // scale):
                    out.data[o] = 0
        return out, False, 0
    for y in range(h):
        for x in range(w):
            parent = legacy.data[((y // scale) * legacy.w + x // scale) * 4 + 3]
            out.data[(y * w + x) * 4 + 3] = parent
            if parent and color.data[(y * w + x) * 4 + 3] == 0:
                uncovered += 1
    if mode == "legacy":
        return out, False, uncovered
    plane = [1.0 if legacy.data[i * 4 + 3] >= 128 else 0.0 for i in range(legacy.w * legacy.h)]
    band = []
    for y in range(h):
        py = y // scale
        for x in range(w):
            px = x // scale
            parent = legacy.data[(py * legacy.w + px) * 4 + 3]
            if _uniform_side(legacy, px, py, parent >= 128):
                continue
            u = _bilinear(plane, legacy.w, legacy.h, (x + 0.5) / scale - 0.5, (y + 0.5) / scale - 0.5)
            alpha = min(max((u - 0.5) * scale + 0.5, 0.0), 1.0)
            out.data[(y * w + x) * 4 + 3] = min(max(round(255.0 * alpha), 0), 255)
            if parent < 128:
                band.append((x, y))
    # a band pixel whose parent is transparent takes the nearest opaque-parent colour (within one texel): the edge shows the art
    for x, y in band:
        best = None
        best_distance = 1 << 60
        for ny in range(max(0, y - scale), min(h - 1, y + scale) + 1):
            for nx in range(max(0, x - scale), min(w - 1, x + scale) + 1):
                if legacy.data[((ny // scale) * legacy.w + nx // scale) * 4 + 3] < 128:
                    continue
                distance = (nx - x) ** 2 + (ny - y) ** 2
                if distance < best_distance:
                    best_distance, best = distance, (nx, ny)
        if best is not None:
            o, s = (y * w + x) * 4, (best[1] * w + best[0]) * 4
            out.data[o:o + 3] = color.data[s:s + 3]
            if color.data[s + 3] == 0:
                uncovered += 1
        elif out.data[(y * w + x) * 4 + 3] and color.data[(y * w + x) * 4 + 3] == 0:
            uncovered += 1
    if check_alpha(out, legacy, scale, mode):
        for i in range(w * h):
            out.data[i * 4 + 3] = legacy.data[((i // w // scale) * legacy.w + (i % w) // scale) * 4 + 3]
        return out, True, uncovered
    return out, False, uncovered


# --- assembling -------------------------------------------------------------------------------------------------------

@dataclass(frozen=True)
class Box:
    ident: str
    detail: str
    x: int
    y: int
    w: int
    h: int


@dataclass
class FrameReport:
    frame: int
    state: str
    file: str
    size: tuple[int, int]
    placed: list[str] = field(default_factory=list)
    alpha_fallback: bool = False


@dataclass
class Report:
    art: str
    scale: int
    canvas: tuple[int, int]
    frames: list[FrameReport]
    boxes: list[Box]
    images: dict[int, Image] = field(default_factory=dict)
    written: list[str] = field(default_factory=list)

    def to_json(self) -> dict:
        return {"art": self.art, "scale": self.scale, "canvas": list(self.canvas),
                "frames": [{"frame": f.frame, "state": f.state, "file": f.file, "size": list(f.size), "placed": f.placed,
                            "alphaFallbackToExact": f.alpha_fallback} for f in self.frames],
                "placements": [{"id": b.ident, "detail": b.detail, "x": b.x, "y": b.y, "w": b.w, "h": b.h} for b in self.boxes]}


def frame_file(art: str, frame: int) -> str:
    return f"{art}-f{frame:03d}.png"


def _load(path: Path, what: str, cache: dict[Path, Image]) -> Image:
    if path not in cache:
        try:
            cache[path] = pngio.read(path)
        except (OSError, pngio.PngError) as error:
            raise LayoutError(f"{what}: {path}: {error}") from error
    return cache[path]


def assemble(layout: Layout, out_dir: Path | None = None) -> Report:
    """Composes every frame at the master scale, bakes it to the pack scale and the original's alpha, and checks the pack's gates.
    With `out_dir` None nothing is written (the dry run). A failure raises LayoutError; nothing is written then either."""
    width, height = layout.size
    cw, ch = canvas_size(layout)
    cache: dict[Path, Image] = {}  # one file is read once: a detail used in many places is one picture
    boxes: list[Box] = []
    frames: list[FrameReport] = []
    images: dict[int, Image] = {}
    for index, state in enumerate(layout.frame_states):
        what = f"frame {index} ({state})"
        base = _load(layout.base.path_for(state, "base"), "base", cache)
        if (base.w, base.h) != (cw, ch):
            raise LayoutError(f"base: {what} is {base.w}x{base.h}, the canvas is {cw}x{ch} ({width}x{height} units at {layout.master_scale} px per unit)")
        canvas = Image(cw, ch, bytearray(base.data))
        report = FrameReport(index, state, frame_file(layout.art, index), (width * layout.scale, height * layout.scale))
        frame_boxes: list[Box] = []
        for placement in layout.placements:
            if placement.frames is not None and index not in placement.frames:
                continue
            if placement.states is not None and state not in placement.states:
                continue
            detail = _load(layout.details[placement.detail].path_for(state, f"detail {placement.detail}"), f"detail {placement.detail}", cache)
            x = units_to_px(placement.at[0], layout.master_scale, layout.snap, layout.position, f"placement {placement.ident} x")
            y = units_to_px(placement.at[1], layout.master_scale, layout.snap, layout.position, f"placement {placement.ident} y")
            if placement.anchor == "center":
                x, y = x - detail.w // 2, y - detail.h // 2
            if x < 0 or y < 0 or x + detail.w > cw or y + detail.h > ch:
                raise LayoutError(f"placement {placement.ident}: the {detail.w}x{detail.h} detail at {x},{y} leaves the {cw}x{ch} canvas")
            blit(canvas, detail, x, y)
            frame_boxes.append(Box(placement.ident, placement.detail, x, y, detail.w, detail.h))
            report.placed.append(placement.ident)
        if not boxes:
            boxes = frame_boxes
        legacy = _load(layout.legacy[index], f"alpha.legacy[{index}]", cache)
        if (legacy.w, legacy.h) != (width, height):
            raise LayoutError(f"alpha.legacy[{index}]: is {legacy.w}x{legacy.h}, the original frame is {width}x{height}")
        baked, fallback, uncovered = bake_alpha(resample(canvas, width * layout.scale, height * layout.scale), legacy, layout.scale, layout.alpha_mode)
        if uncovered:
            raise LayoutError(f"{what}: {uncovered} visible pixels have nothing drawn under them (the base must cover the original's silhouette)")
        problems = check_alpha(baked, legacy, layout.scale, layout.alpha_mode)
        if problems:
            raise LayoutError(f"{what}: fails the {layout.alpha_mode} gate: {problems[0]}")
        report.alpha_fallback = fallback
        frames.append(report)
        images[index] = baked
    result = Report(layout.art, layout.scale, (cw, ch), frames, boxes, images)
    if out_dir is not None:
        out_dir.mkdir(parents=True, exist_ok=True)
        for item in frames:
            pngio.write(out_dir / item.file, images[item.frame])
            result.written.append(item.file)
        name = f"{layout.art}.assembly.json"
        (out_dir / name).write_text(json.dumps(result.to_json(), indent=2) + "\n", encoding="utf-8")
        result.written.append(name)
    return result


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("layout", type=Path, help="the layout JSON (kind " + KIND + ")")
    parser.add_argument("--out", type=Path, help="folder for the HD frames (<art>-fNNN.png) and the report")
    parser.add_argument("--dry-run", action="store_true", help="compose, bake and gate-check everything, write nothing")
    parser.add_argument("--json", action="store_true", help="print the report as JSON")
    args = parser.parse_args(argv)
    if args.out is None and not args.dry_run:
        parser.error("give --out DIR, or --dry-run")
    try:
        layout = load_layout(args.layout)
        report = assemble(layout, None if args.dry_run else args.out)
    except LayoutError as error:
        print(f"ASSEMBLE FAIL: {error}", file=sys.stderr)
        return 1
    if args.json:
        print(json.dumps(report.to_json(), indent=2))
    else:
        print(f"ASSEMBLE {'DRY-RUN ' if args.dry_run else ''}PASS: art {report.art}, {len(report.frames)} frame(s) at x{report.scale} "
              f"from a {report.canvas[0]}x{report.canvas[1]} canvas, {len(report.boxes)} placement(s)")
        for item in report.frames:
            print(f"  frame {item.frame} [{item.state}] {item.file} {item.size[0]}x{item.size[1]} details: {', '.join(item.placed) or '-'}"
                  + (" (smooth alpha refused, exact original alpha)" if item.alpha_fallback else ""))
    return 0


if __name__ == "__main__":
    sys.exit(main())
