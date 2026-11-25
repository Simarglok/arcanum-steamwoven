#!/usr/bin/env python3
"""ARC-UI-3261 (batch B-HARDWARE, slice 3): the small hardware of the windows BY RULE from the original's own frames (no Codex): the lens rings, the hotkey slot insets, the wood of the maintain slots and
their plugs, and three small selection frames (arts 198 222 231 232 233 367 670 746, 172 to 181, 188 to 192, 628 to 632, 646, 332, 314; 31 parts).

    python3 -B tools/steamwoven_ui/hardware_kit.py plan
    python3 -B tools/steamwoven_ui/hardware_kit.py rule  [--only PART,...] [--dry-run] [--frames DIR] [--local-root DIR]    (the masters and their rule records)
    python3 -B tools/steamwoven_ui/hardware_kit.py sheet --out FILE [--zoom N] [--frames DIR] [--local-root DIR]            (before / after composite with English labels, needs ImageMagick)
    python3 -B tools/steamwoven_ui/hardware_kit.py self-test

The rules (TRD SWI-KIT, the owner's rules: the original's muted palette and lighting, no added ornament; the old art's wood in a corner is NOT part of a hardware part, SWI-LAY-12 / #3101):

* **Lens ring** (`lens`): the eight lens arts are one ring in eight wood corners. The ring is the original's own: every opaque pixel within 6 px of the transparent circle is put, by its angle and its distance from the circle's edge, into a polar table
  (0.5 degree by 0.25 px, a smooth splat, the eight arts pooled so the eight woods cancel); the master is that table read at every master pixel, so the lighting that falls from the top left stays the original's and the ring is smooth at the master's
  size. Outside the ring the part is transparent (the window's wood shows), inside the circle too: the lens is a window onto the portrait; `schm_pc_lens` (art 367) keeps the original's opaque dark fill, which is part of that art.
* **Slot inset** (`inset`): the ten hotkey slot arts are one inset (a dark shadow along the top and left, a light line along the bottom and right) over ten crops of the strip's wood. The inset is the mean of the ten, each normalised by its own wood, so the
  wood cancels; the part holds only what is not wood: the shadow as black with the original's depth as alpha, the edge lines in their own colour, and a transparent middle (the strip's wood shows through). The ten parts are one picture.
* **Slot wood** and **plug** (`wood`, `plug`): the five maintain slots of the top strip are openings in the strip art, so their back and their plug are wood: a crop of the strip's own wood, flipped to differ, shaded by the original's smooth light
  (the original's low-pass luminance over its mean) and nothing else.
* **Selection frames** (`outline`): the white and black bevel of the portrait highlight and the red frame of the portrait selection are flat colours: every original pixel is a block of the master (the crisp lines stay exact). The roller template
  (`roller`) is a smooth shaded cylinder: resampled smoothly.

Everything stays under the ignored `local/steamwoven-ui/`; nothing here holds art. Standard library only (`pngio`, `fit`); ImageMagick for the composite. Exit 0 = done, 1 = a check failed, 2 = an input could not be used.
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
    from . import fit, pngio, prompt_library
except ImportError:  # run as a script
    sys.path.insert(0, str(HERE))
    import fit  # type: ignore
    import pngio  # type: ignore
    import prompt_library  # type: ignore

TOOL_VERSION = 1
BATCH = "B-HARDWARE"
GRID = 6
LABEL_FONT = "/System/Library/Fonts/Supplemental/Arial.ttf"   # the labels of a composite are English (project rule)
MANIFEST = HERE / "kit.json"

# the lens ring's polar table
TH_BINS = 720                 # angle bins (0.5 degree)
D_MIN, D_MAX, D_STEP = -0.5, 6.0, 0.25   # depth (original px beyond the circle's edge)
SIGMA_T, SIGMA_D = 2.0, 0.40  # the splat's width: degrees, original px
RING_CORE, RING_END = 4.2, 5.0   # the ring is opaque up to RING_CORE px beyond the circle and fades out by RING_END (its outer dark rim melts into the window's wood)

# the strip wood the maintain slots and plugs are cut from (board.inttop master px): the one plain stretch of wood left of the slot openings (the strip's other opaque stretches are filled-in erasures, smeared), 126 px square;
# a smaller tile is cut at a corner or the middle of it, flipped, so the five tiles differ (a 126 px plug fills the area, so its four flips differ and the fifth plug repeats the first)
WOOD_AREA = (878, 12)
WOOD_SIZE = 126
WOOD_CUTS = ((0.0, 0.0), (1.0, 0.0), (0.0, 1.0), (1.0, 1.0), (0.5, 0.5))      # slot i: where in the area (0 = start, 1 = end of the free margin)
WOOD_FLIPS = ((False, False), (True, False), (False, True), (True, True), (False, False))   # slot i: its flip


class KitError(Exception):
    pass


@dataclass(frozen=True)
class Part:
    ident: str
    art: str           # census art number
    name: str          # census art name (the frames folder is `<art>-<name>`)
    family: str        # lens | inset | wood | plug | outline | roller
    index: int = 0     # the slot number of a wood or plug part
    fill: bool = False  # a lens that keeps the original's opaque fill (art 367)


LENS = (("pcwincvr", "198", "PCWinCvr"), ("char_pcc", "222", "Char_PCC"), ("lns_bart", "231", "Lns_Bart"), ("lns_loot", "232", "Lns_Loot"), ("lns_papr", "233", "Lns_Papr"),
        ("schm_pc_lens", "367", "Schm_PC_Lens"), ("optionspclens", "670", "OptionsPCLens"), ("saveloadpclens", "746", "SaveLoadPCLens"))
PARTS: tuple[Part, ...] = (
    *(Part(f"hardware.{i}", art, name, "lens", fill=art == "367") for i, art, name in LENS),
    *(Part(f"hardware.hkslt{n}", str(172 + n), f"HKSlt{n}", "inset", n) for n in range(10)),
    *(Part(f"hardware.splslt{n + 1}", str(188 + n), f"SplSlt{n + 1}", "wood", n) for n in range(5)),
    *(Part(f"hardware.sp_plugged{n + 1}", str(628 + n), f"SP_Plugged{n + 1}", "plug", n) for n in range(5)),
    Part("hardware.portrait_highlight", "646", "Portrait_Highlight", "outline"),
    Part("hardware.portselect", "332", "PortSelect", "outline"),
    Part("hardware.rollertemplate", "314", "RollerTemplate", "roller"),
)
BY_ID = {p.ident: p for p in PARTS}
LENS_ARTS = tuple(p for p in PARTS if p.family == "lens")
INSET_ARTS = tuple(p for p in PARTS if p.family == "inset")
FAMILY_NOTE = {
    "lens": "the lens ring by rule from the original's own ring (a polar table of the eight lens arts, read at the master size); the art's wood corners are not part of it, the circle is open",
    "inset": "the hotkey slot's inset by rule from the original's ten slot arts (shadow along the top and left, light line along the bottom and right; the middle is open for the strip's wood)",
    "wood": "the maintain slot's wood: a crop of the strip's wood shaded by the original's smooth light (the slot is an opening in the strip art)",
    "plug": "the plug of a closed maintain slot: a crop of the strip's wood shaded by the original's smooth light",
    "outline": "a flat-colour selection frame by rule: every original pixel is a block of the master, the lines stay crisp and exact",
    "roller": "the shaded roller by rule from the original's own frame, resampled smoothly",
}


def coverage() -> dict[str, str]:
    """Part id -> how it is made (every part is made by rule, no Codex)."""
    return {p.ident: f"rule:{p.family}" for p in PARTS}


def provenance_of(part: str) -> str:
    return f"rule:hardware_kit:{part}"


def note_of(part: str) -> str:
    return f"{FAMILY_NOTE[BY_ID[part].family]}; hardware_kit.py, ARC-UI-3261, no Codex"


# --- shared helpers -----------------------------------------------------------------------------------------------------------------------

def load_manifest() -> dict:
    return json.loads(MANIFEST.read_text(encoding="utf-8"))


def master_of(part: str, manifest: dict | None = None) -> tuple[int, int]:
    manifest = manifest or load_manifest()
    for row in manifest["parts"]:
        if row["id"] == part:
            size = row.get("size")
            if not size:
                raise KitError(f"{part}: the manifest row has no size")
            return int(size[0]), int(size[1])
    raise KitError(f"{part} is not in the kit manifest")


def sha256_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read_frame(frames: Path, part: Part, frame: int = 0) -> pngio.Image:
    folder = frames / f"{part.art}-{part.name}"
    path = folder / f"f{frame}.png"
    if not path.is_file():
        raise KitError(f"{path} is missing (the census frames of the original, `local/steamwoven-ui/census/frames`)")
    return pngio.read(path)


def lum(r: float, g: float, b: float) -> float:
    return 0.299 * r + 0.587 * g + 0.114 * b


def clamp(v: float, lo: float, hi: float) -> float:
    return lo if v < lo else hi if v > hi else v


def box_blur(plane: list[float], w: int, h: int, radius: int) -> list[float]:
    """A separable box blur with clamped edges (a low-pass for a shading map)."""
    def one(src: list[float], stride: int, step: int, count: int, lines: int) -> list[float]:
        out = [0.0] * len(src)
        for line in range(lines):
            base = line * stride
            for i in range(count):
                lo, hi = max(0, i - radius), min(count - 1, i + radius)
                total = sum(src[base + j * step] for j in range(lo, hi + 1))
                out[base + i * step] = total / (hi - lo + 1)
        return out
    return one(one(plane, w, 1, w, h), 1, w, h, w)


# --- lens ring ----------------------------------------------------------------------------------------------------------------------------

def hole_of(image: pngio.Image) -> tuple[float, float, float]:
    """The centre and radius of the transparent circle of a lens art (the area of the open pixels is the circle's)."""
    pts = [(x, y) for y in range(image.h) for x in range(image.w) if image.pixel(x, y)[3] == 0]
    if len(pts) < 100:
        raise KitError("a lens art has no transparent circle")
    cx = sum(p[0] for p in pts) / len(pts) + 0.5
    cy = sum(p[1] for p in pts) / len(pts) + 0.5
    return cx, cy, math.sqrt(len(pts) / math.pi)


ND = int((D_MAX - D_MIN) / D_STEP) + 1


def ring_table(images: list[pngio.Image]) -> dict:
    """The polar table of the ring: sums of colour and weight per (angle bin, depth bin), every opaque pixel within D_MAX of the circle's edge splatted with a Gaussian."""
    cx, cy, radius = hole_of(images[0])
    num = [[[0.0, 0.0, 0.0] for _ in range(ND)] for _ in range(TH_BINS)]
    den = [[0.0] * ND for _ in range(TH_BINS)]
    bin_deg = 360.0 / TH_BINS
    spread_t = int(3 * SIGMA_T / bin_deg) + 1
    for image in images:
        if (image.w, image.h) != (images[0].w, images[0].h):
            raise KitError("the lens arts differ in size")
        for y in range(image.h):
            for x in range(image.w):
                r, g, b, a = image.pixel(x, y)
                if a < 255:
                    continue
                dx, dy = x + 0.5 - cx, y + 0.5 - cy
                d = math.hypot(dx, dy) - radius
                if d < 0 or d > D_MAX:
                    continue
                th = (math.degrees(math.atan2(dy, dx)) + 360.0) % 360.0
                centre_bin = th / bin_deg
                d_lo = max(0, int((d - 3 * SIGMA_D - D_MIN) / D_STEP))
                d_hi = min(ND - 1, int((d + 3 * SIGMA_D - D_MIN) / D_STEP) + 1)
                wds = [(db, math.exp(-0.5 * ((D_MIN + db * D_STEP - d) / SIGMA_D) ** 2)) for db in range(d_lo, d_hi + 1)]
                for tb in range(int(centre_bin) - spread_t, int(centre_bin) + spread_t + 1):
                    wt = math.exp(-0.5 * (((tb + 0.5) * bin_deg - th) / SIGMA_T) ** 2)
                    t = tb % TH_BINS
                    for db, wd in wds:
                        w = wt * wd
                        den[t][db] += w
                        cell = num[t][db]
                        cell[0] += w * r
                        cell[1] += w * g
                        cell[2] += w * b
    return {"cx": cx, "cy": cy, "radius": radius, "num": num, "den": den, "size": (images[0].w, images[0].h)}


def render_ring(table: dict, size: tuple[int, int], fill: tuple[int, int, int] | None = None) -> pngio.Image:
    """The ring at the master size (the table read at every master pixel); a fill colour makes the circle's inside opaque (art 367)."""
    w, h = size
    ow, oh = table["size"]
    sx, sy = w / ow, h / oh
    s = (sx + sy) / 2
    if abs(sx - sy) / s > 0.01:
        raise KitError("the master's aspect is not the art's")
    cx, cy, radius = table["cx"], table["cy"], table["radius"]
    num, den = table["num"], table["den"]
    out = pngio.Image.blank(w, h)
    bin_deg = 360.0 / TH_BINS
    for my in range(h):
        for mx in range(w):
            ox = (mx + 0.5) / s - cx
            oy = (my + 0.5) / s - cy
            d = math.hypot(ox, oy) - radius
            inside = d * s  # master px beyond the circle's edge (negative inside)
            if d > RING_END and fill is None:
                continue
            r = g = b = 0.0
            alpha = 0.0
            if d <= RING_END and d >= D_MIN:
                th = (math.degrees(math.atan2(oy, ox)) + 360.0) % 360.0
                ft = th / bin_deg - 0.5
                t0 = math.floor(ft)
                wt = ft - t0
                fd = (d - D_MIN) / D_STEP
                d0 = max(0, min(ND - 2, int(math.floor(fd))))
                wd = clamp(fd - d0, 0.0, 1.0)
                acc = [0.0, 0.0, 0.0]
                tot = 0.0
                for tt, w1 in ((t0 % TH_BINS, 1 - wt), ((t0 + 1) % TH_BINS, wt)):
                    for dd, w2 in ((d0, 1 - wd), (d0 + 1, wd)):
                        weight = den[tt][dd]
                        if weight <= 1e-6:
                            continue
                        tot += w1 * w2 * weight
                        cell = num[tt][dd]
                        acc[0] += w1 * w2 * cell[0]
                        acc[1] += w1 * w2 * cell[1]
                        acc[2] += w1 * w2 * cell[2]
                if tot > 0.05:  # a table cell nothing was splatted into (the circle touches the art's edge there) stays open
                    r, g, b = acc[0] / tot, acc[1] / tot, acc[2] / tot
                    a_in = clamp(inside + 0.5, 0.0, 1.0)
                    a_out = 1.0 if d <= RING_CORE else clamp((RING_END - d) / (RING_END - RING_CORE), 0.0, 1.0)
                    alpha = a_in * a_out
            if fill is not None and inside < 0.5:
                cover = clamp(0.5 - inside, 0.0, 1.0)  # the fill's own soft edge, under the ring
                total = alpha + (1 - alpha) * cover
                if total > 0:  # the ring over the fill, in straight (not premultiplied) colour
                    r = (r * alpha + fill[0] * (1 - alpha) * cover) / total
                    g = (g * alpha + fill[1] * (1 - alpha) * cover) / total
                    b = (b * alpha + fill[2] * (1 - alpha) * cover) / total
                alpha = total
            if alpha <= 0:
                continue
            i = (my * w + mx) * 4
            out.data[i:i + 4] = bytes((int(round(clamp(r, 0, 255))), int(round(clamp(g, 0, 255))), int(round(clamp(b, 0, 255))), int(round(alpha * 255))))
    return out


# --- slot inset ---------------------------------------------------------------------------------------------------------------------------

def normalised_mean(images: list[pngio.Image]) -> list[tuple[float, float, float]]:
    """The mean of the slot arts, each scaled by its own wood (the mean luminance of its middle third) so the ten woods cancel."""
    w, h = images[0].w, images[0].h
    lo_x, hi_x, lo_y, hi_y = w // 3, 2 * w // 3, h // 3, 2 * h // 3
    scales = []
    for image in images:
        total = sum(lum(*image.pixel(x, y)[:3]) for y in range(lo_y, hi_y) for x in range(lo_x, hi_x)) / ((hi_x - lo_x) * (hi_y - lo_y))
        scales.append(total)
    ref = sum(scales) / len(scales)
    out = []
    for y in range(h):
        for x in range(w):
            acc = [0.0, 0.0, 0.0]
            for image, scale in zip(images, scales):
                p = image.pixel(x, y)
                for c in range(3):
                    acc[c] += p[c] * ref / scale
            out.append((acc[0] / len(images), acc[1] / len(images), acc[2] / len(images)))
    return out


def inset_layer(images: list[pngio.Image]) -> pngio.Image:
    """The slot inset at the arts' own size: shadow as black with alpha, the edge lines opaque, the middle open. Rows are compared with the wood of their own row (the strip's wood is lit from below), the shadow zone being
    the top and the left (up to a third of the art), the lines the first row, the last row and the last column."""
    w, h = images[0].w, images[0].h
    if w != h or w < 16:
        raise KitError("a slot art is a square of at least 16 px")
    mean = normalised_mean(images)
    mid = w // 3, 2 * w // 3
    wood_rows = []
    for y in range(h):
        row = [lum(*mean[y * w + x]) for x in range(mid[0] + 1, w - 2)]
        wood_rows.append(sum(row) / len(row))
    shadow_end = max(5, int(round(w * 0.30)))   # the shadow zone: the first rows and columns after the first (to about 9 px of 32)
    top_wood = wood_rows[shadow_end + 2]
    out = pngio.Image.blank(w, h)
    for y in range(h):
        for x in range(w):
            r, g, b = mean[y * w + x]
            line = y == 0 or y == h - 1 or x == w - 1
            if line:
                out.data[(y * w + x) * 4:(y * w + x) * 4 + 4] = bytes((int(clamp(r, 0, 255)), int(clamp(g, 0, 255)), int(clamp(b, 0, 255)), 255))
                continue
            if x == 0 or (x > shadow_end + 2 and y > shadow_end + 2):
                continue
            wood = wood_rows[y] if y > shadow_end + 2 else top_wood
            ratio = lum(r, g, b) / wood if wood > 1e-6 else 1.0
            alpha = clamp(1.0 - ratio, 0.0, 0.85)
            if alpha > 0.01:
                out.data[(y * w + x) * 4:(y * w + x) * 4 + 4] = bytes((0, 0, 0, int(round(alpha * 255))))
    return out


# --- wood tiles ---------------------------------------------------------------------------------------------------------------------------

def flipped(image: pngio.Image, flip_x: bool, flip_y: bool) -> pngio.Image:
    out = pngio.Image.blank(image.w, image.h)
    for y in range(image.h):
        sy = image.h - 1 - y if flip_y else y
        for x in range(image.w):
            sx = image.w - 1 - x if flip_x else x
            i, j = (y * image.w + x) * 4, (sy * image.w + sx) * 4
            out.data[i:i + 4] = image.data[j:j + 4]
    return out


def strip_wood(strip: pngio.Image, index: int, size: int) -> pngio.Image:
    """A `size` x `size` tile of the strip's own wood for slot `index` (a cut of the plain area, a flip)."""
    margin = WOOD_SIZE - size
    fx, fy = WOOD_CUTS[index % len(WOOD_CUTS)]
    x0, y0 = WOOD_AREA[0] + int(round(margin * fx)), WOOD_AREA[1] + int(round(margin * fy))
    tile = strip.crop(x0, y0, size, size)
    if any(a != 255 for a in tile.data[3::4]):
        raise KitError(f"the strip's wood at ({x0}, {y0}) is not opaque: the wood area changed with the strip")
    return flipped(tile, *WOOD_FLIPS[index % len(WOOD_FLIPS)])


def light_map(original: pngio.Image, size: tuple[int, int]) -> list[float]:
    """The original's smooth light: its low-pass luminance over its mean, bilinear to the master size (grain and noise stay out)."""
    w, h = original.w, original.h
    plane = [lum(*original.pixel(x, y)[:3]) for y in range(h) for x in range(w)]
    low = box_blur(plane, w, h, 2)
    mean = sum(low) / len(low)
    ratio = [v / mean for v in low]
    out = []
    for my in range(size[1]):
        fy = clamp((my + 0.5) * h / size[1] - 0.5, 0.0, h - 1.0)
        y0 = int(fy)
        y1 = min(h - 1, y0 + 1)
        wy = fy - y0
        for mx in range(size[0]):
            fx = clamp((mx + 0.5) * w / size[0] - 0.5, 0.0, w - 1.0)
            x0 = int(fx)
            x1 = min(w - 1, x0 + 1)
            wx = fx - x0
            top = ratio[y0 * w + x0] * (1 - wx) + ratio[y0 * w + x1] * wx
            bottom = ratio[y1 * w + x0] * (1 - wx) + ratio[y1 * w + x1] * wx
            out.append(top * (1 - wy) + bottom * wy)
    return out


def shaded_wood(tile: pngio.Image, original: pngio.Image) -> pngio.Image:
    """The strip's wood in the original art's tone (each channel's mean the original's) and under the original's smooth light."""
    shade = light_map(original, (tile.w, tile.h))
    n = tile.w * tile.h
    tone = [(sum(original.data[c::4]) / (original.w * original.h)) / max(1.0, sum(tile.data[c:n * 4:4]) / n) for c in range(3)]
    out = pngio.Image.blank(tile.w, tile.h)
    for i, s in enumerate(shade):
        p = tile.data[i * 4:i * 4 + 4]
        out.data[i * 4:i * 4 + 4] = bytes((int(clamp(p[0] * tone[0] * s, 0, 255)), int(clamp(p[1] * tone[1] * s, 0, 255)), int(clamp(p[2] * tone[2] * s, 0, 255)), p[3]))
    return out


# --- flat frames and the roller -------------------------------------------------------------------------------------------------------------

def blocks(original: pngio.Image, size: tuple[int, int]) -> pngio.Image:
    """Every original pixel is a block of the master (nearest): the crisp lines of a flat-colour frame stay exact."""
    w, h = size
    out = pngio.Image.blank(w, h)
    for my in range(h):
        sy = min(original.h - 1, int((my + 0.5) * original.h / h))
        for mx in range(w):
            sx = min(original.w - 1, int((mx + 0.5) * original.w / w))
            i, j = (my * w + mx) * 4, (sy * original.w + sx) * 4
            out.data[i:i + 4] = original.data[j:j + 4]
    return out


def roller(original: pngio.Image, size: tuple[int, int]) -> pngio.Image:
    """The roller's shading runs along its height: smooth that way (a cylinder), and blocks across it so the grooves and the highlight stay crisp lines."""
    tall = fit.resample_image(original, (0, 0, original.w, original.h), original.w, size[1])
    return blocks(tall, size)


# --- the parts ----------------------------------------------------------------------------------------------------------------------------

def build_part(part: Part, frames: Path, masters: Path, size: tuple[int, int], cache: dict) -> tuple[pngio.Image, dict]:
    """The master of one part and what the record says about it. `cache` shares the ring table and the inset among the parts of a family."""
    if part.family == "lens":
        if "ring" not in cache:
            cache["ring"] = ring_table([read_frame(frames, p) for p in LENS_ARTS])
        fill = None
        if part.fill:
            original = read_frame(frames, part)
            r, g, b, a = original.pixel(original.w // 2, original.h // 2)
            if a != 255:
                raise KitError(f"{part.ident}: the original's fill is not opaque")
            fill = (r, g, b)
        table = cache["ring"]
        return render_ring(table, size, fill), {"ring_centre": [round(table["cx"], 2), round(table["cy"], 2)], "circle_radius": round(table["radius"], 2), "pooled_arts": [p.art for p in LENS_ARTS],
                                                 "fill": list(fill) if fill else None}
    if part.family == "inset":
        if "inset" not in cache:
            cache["inset"] = inset_layer([read_frame(frames, p) for p in INSET_ARTS])
        layer = cache["inset"]
        return fit.resample_image(layer, (0, 0, layer.w, layer.h), size[0], size[1]), {"pooled_arts": [p.art for p in INSET_ARTS]}
    if part.family in ("wood", "plug"):
        if "strip" not in cache:
            path = masters / "board.inttop.png"
            if not path.is_file():
                raise KitError(f"{path} is missing: the strip master the wood is cut from")
            cache["strip"] = pngio.read(path)
            cache["strip_sha"] = sha256_file(path)
        tile = strip_wood(cache["strip"], part.index, size[0])
        original = read_frame(frames, part)
        return shaded_wood(tile, original), {"wood_source": ["board.inttop", *WOOD_AREA, WOOD_SIZE], "cut": list(WOOD_CUTS[part.index % len(WOOD_CUTS)]), "flip": list(WOOD_FLIPS[part.index % len(WOOD_FLIPS)]),
                                              "strip_sha256": cache["strip_sha"]}
    original = read_frame(frames, part)
    if part.family == "outline":
        return blocks(original, size), {"mode": "blocks"}
    if part.family == "roller":
        return roller(original, size), {"mode": "smooth along the cylinder, blocks across it"}
    raise KitError(f"unknown family {part.family}")


def gates(part: Part, image: pngio.Image, size: tuple[int, int]) -> list[str]:
    """The checks of a rule master: the size is the manifest's and on the grid, the part is not empty, a solid detail has no see-through pixel inside it (an opaque detail's alpha is 255 where all its
    neighbours are drawn), and the pixels of a translucent part stay a thin edge."""
    failures = []
    if (image.w, image.h) != size:
        failures.append(f"size {image.w}x{image.h} is not the manifest's {size[0]}x{size[1]}")
    if size[0] % GRID or size[1] % GRID:
        failures.append(f"size {size[0]}x{size[1]} is not on the {GRID} px grid")
    alpha = image.data[3::4]
    drawn = sum(1 for a in alpha if a)
    if drawn < 0.001 * len(alpha):
        failures.append("the part is empty")
    if part.family in ("lens", "outline", "roller", "wood", "plug"):
        w, h = image.w, image.h
        hollow = 0
        for y in range(1, h - 1):
            for x in range(1, w - 1):
                a = alpha[y * w + x]
                if 0 < a < 250 and all(alpha[(y + dy) * w + x + dx] for dy in (-1, 0, 1) for dx in (-1, 0, 1)):
                    hollow += 1
        limit = 0.30 if part.family == "lens" else 0.0   # a lens's soft edges (the inner anti-alias, the outer fade) are translucent by design, a flat detail's are not inside it
        if drawn and hollow > limit * drawn:
            failures.append(f"{hollow} translucent pixels inside a solid detail (the detail must be opaque)")
        if part.family in ("wood", "plug") and any(a != 255 for a in alpha):
            failures.append("a wood tile is opaque")
    return failures


def provenance_record(part: Part, frames: Path, extra: dict, size: tuple[int, int], blob: bytes) -> dict:
    sources = {}
    arts = extra.get("pooled_arts") or [part.art]
    names = {p.art: p.name for p in PARTS}
    for art in arts:
        path = frames / f"{art}-{names[art]}" / "f0.png"
        sources[f"{art}-{names[art]}/f0"] = sha256_file(path)
    return {"part": part.ident, "rule": "hardware_kit", "family": part.family, "tool_version": TOOL_VERSION, "date": datetime.date.today().isoformat(), "source": f"{part.art}-{part.name}/f0",
            "sources_sha256": sources, **extra, "image_size": list(size), "image_sha256": hashlib.sha256(blob).hexdigest(), "run_by": "agent"}


def cmd_plan(args: argparse.Namespace) -> int:
    manifest = load_manifest()
    rows = {r["id"]: r for r in manifest["parts"]}
    for part in PARTS:
        row = rows.get(part.ident)
        print(f"{part.ident:34} art {part.art:>3} {part.family:8} {'x'.join(map(str, row['size'])) if row else 'NO ROW'}")
    missing = [p.ident for p in PARTS if p.ident not in rows]
    print(f"HARDWARE KIT plan: {len(PARTS)} parts" + (f", rows missing: {missing}" if missing else ""))
    return 1 if missing else 0


def cmd_rule(args: argparse.Namespace) -> int:
    local = args.local_root
    frames = args.frames or local / "census" / "frames"
    masters = local / "masters"
    manifest = load_manifest()
    only = {x for x in (args.only or "").split(",") if x}
    cache: dict = {}
    done = 0
    for part in PARTS:
        if only and part.ident not in only:
            continue
        size = master_of(part.ident, manifest)
        image, extra = build_part(part, frames, masters, size, cache)
        failures = gates(part, image, size)
        if failures:
            raise KitError(f"{part.ident}: " + "; ".join(failures))
        blob = pngio.encode(image)
        print(f"{part.ident}: {size[0]}x{size[1]} ({part.family}) {sha256_hex_short(blob)}")
        if not args.dry_run:
            target = masters / f"{part.ident}.png"
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(blob)
            record = provenance_record(part, frames, extra, size, blob)
            (local / "provenance" / "rule").mkdir(parents=True, exist_ok=True)
            (local / "provenance" / "rule" / f"{part.ident}.json").write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
        done += 1
    print(f"HARDWARE KIT rule PASS: {done} part(s)" + (" (dry run, nothing written)" if args.dry_run else ""))
    return 0


def sha256_hex_short(blob: bytes) -> str:
    return hashlib.sha256(blob).hexdigest()[:12]


# --- the composite ------------------------------------------------------------------------------------------------------------------------

def run(cmd: list[str]) -> None:
    subprocess.run(cmd, check=True)


def cmd_sheet(args: argparse.Namespace) -> int:
    """The before / after composite: per family the ORIGINAL art (its own pixels, nearest) over the KIT part on the same surface (the strip's own wood), at one zoom."""
    local = args.local_root
    frames = args.frames or local / "census" / "frames"
    masters = local / "masters"
    if shutil.which("magick") is None:
        raise KitError("ImageMagick (magick) is needed for the composite")
    zoom = args.zoom
    work = Path(tempfile.mkdtemp(prefix="hardware-sheet-"))
    try:
        strip = masters / "board.inttop.png"
        wood = work / "wood.png"
        run(["magick", str(strip), "-crop", "126x126+878+12", "+repage", str(wood)])
        sets = (("Lens rings (8 arts: the original's wood corners are gone, the circle is open)", ["hardware.pcwincvr", "hardware.lns_loot", "hardware.optionspclens", "hardware.schm_pc_lens"], 318),
                ("Hotkey slot insets (the strip's wood shows through the middle)", ["hardware.hkslt0", "hardware.hkslt5"], 114),
                ("Maintain slot wood and plugs (top strip)", ["hardware.splslt1", "hardware.splslt3", "hardware.sp_plugged1", "hardware.sp_plugged4"], 126),
                ("Selection frames and roller", ["hardware.portrait_highlight", "hardware.portselect", "hardware.rollertemplate"], 240))
        rows = []
        for title, ids, cell in sets:
            side = int(cell * zoom / 3.6)
            columns = []
            for ident in ids:
                part = BY_ID[ident]
                original = work / f"{ident}-o.png"
                pngio.write(original, read_frame(frames, part))
                master = masters / f"{ident}.png"
                if not master.is_file():
                    raise KitError(f"{master} is missing: run `rule` first")
                for tag, src, nearest in (("o", original, True), ("k", master, False)):
                    out = work / f"{ident}-{tag}-cell.png"
                    # the original sits on its own pixels; the kit part over the strip's wood (a lens or an inset has nothing of its own behind it)
                    if tag == "o":
                        run(["magick", "-size", f"{side}x{side}", "xc:#606060", "(", str(src), "-filter", "point", "-resize", f"{side}x{side}!", ")", "-composite", str(out)])
                    else:
                        base = ["magick", "-size", f"{side}x{side}", "xc:#606060"] if part.family not in ("inset", "lens") else ["magick", str(wood), "-filter", "Lanczos", "-resize", f"{side}x{side}!"]
                        flt = "point" if part.family in ("outline", "roller") else "Lanczos"
                        run([*base, "(", str(src), "-filter", flt, "-resize", f"{side}x{side}!", ")", "-composite", str(out)])
                column = work / f"{ident}-col.png"
                run(["magick", str(work / f"{ident}-o-cell.png"), str(work / f"{ident}-k-cell.png"), "-background", "#202020", "-splice", "0x6", "-append", str(column)])
                columns.append(column)
            row = work / f"row-{len(rows)}.png"
            run(["magick", *columns, "-background", "#202020", "-splice", "6x0", "+append", str(row)])
            head = work / f"head-{len(rows)}.png"
            run(["magick", "-size", f"{max(900, side * len(ids))}x30", "xc:#303030", "-font", LABEL_FONT, "-fill", "white", "-pointsize", "17", "-gravity", "west", "-annotate", "+8+0", title, str(head)])
            rows.append((head, row))
        pieces = []
        for head, row in rows:
            pieces += [str(head), str(row)]
        run(["magick", *pieces, "-background", "#202020", "-gravity", "west", "-append", str(work / "sheet.png")])
        title = work / "title.png"
        run(["magick", "-size", "1500x38", "xc:#202020", "-font", LABEL_FONT, "-fill", "white", "-pointsize", "20", "-gravity", "west", "-annotate", "+8+0",
             "ARC-UI-3261, B-HARDWARE slice 3: top of each pair = ORIGINAL art (its own pixels), bottom = KIT part (on the strip's wood where it has nothing behind it)", str(title)])
        run(["magick", str(title), str(work / "sheet.png"), "-background", "#202020", "-append", str(args.out)])
    finally:
        shutil.rmtree(work, ignore_errors=True)
    print(f"HARDWARE KIT sheet PASS: {args.out}")
    return 0


def cmd_self_test(args: argparse.Namespace) -> int:
    code = subprocess.run([sys.executable, "-B", "-m", "unittest", "tools.steamwoven_ui.test_hardware_kit"], cwd=HERE.parents[1]).returncode
    print("HARDWARE KIT self-test " + ("PASS" if code == 0 else "FAIL"))
    return 0 if code == 0 else 1


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = parser.add_subparsers(dest="command", required=True)
    for name, fn in (("plan", cmd_plan), ("rule", cmd_rule), ("sheet", cmd_sheet), ("self-test", cmd_self_test)):
        s = sub.add_parser(name)
        s.set_defaults(fn=fn)
        s.add_argument("--only")
        s.add_argument("--frames", type=Path)
        s.add_argument("--local-root", type=Path)
        s.add_argument("--dry-run", action="store_true")
        s.add_argument("--out", type=Path)
        s.add_argument("--zoom", type=int, default=2)
    args = parser.parse_args(argv)
    args.local_root = args.local_root or prompt_library.default_local_root()
    try:
        return args.fn(args)
    except (KitError, fit.FitError, fit.GateError, pngio.PngError) as error:
        print(f"HARDWARE KIT {args.command} FAIL: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
