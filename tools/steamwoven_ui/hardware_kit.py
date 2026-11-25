#!/usr/bin/env python3
"""ARC-UI-3261 (batch B-HARDWARE, slice 3): the small hardware of the windows BY RULE from the original's own frames (no Codex): the lens rings, the hotkey slot insets, the wood of the maintain slots and
their plugs, and three small selection frames (arts 198 222 231 232 233 367 670 746, 172 to 181, 188 to 192, 628 to 632, 646, 332, 314; 31 arts, 33 parts: the whole lens ring and its filled look are two parts of their own).

    python3 -B tools/steamwoven_ui/hardware_kit.py plan
    python3 -B tools/steamwoven_ui/hardware_kit.py rule  [--only PART,...] [--dry-run] [--frames DIR] [--local-root DIR]    (the masters and their rule records)
    python3 -B tools/steamwoven_ui/hardware_kit.py sheet --out FILE [--zoom N] [--frames DIR] [--local-root DIR]            (before / after composite with English labels, needs ImageMagick)
    python3 -B tools/steamwoven_ui/hardware_kit.py self-test

The rules (TRD SWI-KIT, the owner's rules: the original's muted palette and lighting, no added ornament; the old art's wood in a corner is NOT part of a hardware part, SWI-LAY-12 / #3101):

* **Lens ring** (`lens_whole`, `lens`): the SHAPE is analytic (owner 2026-11-25: even, exact): a band of 5 original px outside the lens art's transparent circle, anti-aliased, on a canvas 18 px larger on every side so the ring is whole (the old art clipped it
  where the circle touches its edge). Its MATERIAL is polished, cast brass (owner 2026-11-25: narrow sharp highlights, alternating light and dark reflections round the circle, fine metal grain, warm copper reflections): the body tone and the grain are the kit's
  approved brass, the rings of the HUD (#3259) and the menu sockets (#3260) unrolled along their circumference (the two details end to end, longer than the lens ring, so nothing repeats; their slow light is removed, only the grain is kept); the light is
  analytic: a sharp highlight ridge at the top left, a broad sheen, whole-harmonic reflection bands with sharp edges, a copper reflection at the bottom right, a narrow dark chamfer with a thin bright edge along the inner side, a roll-off (no dark outline) at the
  outer side. Parts: `hardware.lens_ring` (354 x 354, the circle open), `hardware.lens_ring_filled` (with the dark fill of art 367) and the eight lens arts' rows, the same ring cut to the art's own 318 x 318 square (a drop-in for the old art; cut where the old art cut it).
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

# the lens ring
RING_WIDTH = 5.0              # the ring's brass band, original px (the old art's band is about 4 px, its dark rim included)
LENS_PAD = 18                 # master px added on every side of a lens art's square so the ring is whole (3 grid steps)
BRASS_ROWS = 18               # the band's width in master px (RING_WIDTH x the lens scale), rows of the brass texture
# the approved brass of the kit the ring's grain is taken from: the HUD socket ring (#3259) and the menu socket ring (#3260), each a circle of brass; (part, first row of a band of BRASS_ROWS rows, counted from the centre)
BRASS_SOURCES = (("hardware.menu_ring", 104), ("hardware.hud_ring", 73))
GRAIN = 1.2                   # how strongly the grain (the brass's detail without its slow light) modulates the ring

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
    Part("hardware.lens_ring", "", "", "lens_whole"),
    Part("hardware.lens_ring_filled", "", "", "lens_whole", fill=True),
    *(Part(f"hardware.hkslt{n}", str(172 + n), f"HKSlt{n}", "inset", n) for n in range(10)),
    *(Part(f"hardware.splslt{n + 1}", str(188 + n), f"SplSlt{n + 1}", "wood", n) for n in range(5)),
    *(Part(f"hardware.sp_plugged{n + 1}", str(628 + n), f"SP_Plugged{n + 1}", "plug", n) for n in range(5)),
    Part("hardware.portrait_highlight", "646", "Portrait_Highlight", "outline"),
    Part("hardware.portselect", "332", "PortSelect", "outline"),
    Part("hardware.rollertemplate", "314", "RollerTemplate", "roller"),
)
BY_ID = {p.ident: p for p in PARTS}
LENS_ARTS = tuple(p for p in PARTS if p.family == "lens")
LENS_BASE = 318                # a lens art's master square (89 units); the whole ring is LENS_BASE + 2 * LENS_PAD
LENS_WHOLE = (LENS_BASE + 2 * LENS_PAD, LENS_BASE + 2 * LENS_PAD)
INSET_ARTS = tuple(p for p in PARTS if p.family == "inset")
FAMILY_NOTE = {
    "lens_whole": "the whole lens ring: analytic shape (5 px band), polished brass with the kit's approved brass grain and tone; the art's square plus 18 px each side, centred; circle open or dark-filled",
    "lens": "the lens art's square with the ring of hardware.lens_ring on it (drop-in for the old art, cut where the old art cut it); no wood corners, circle open",
    "inset": "the hotkey slot's inset by rule from the ten slot arts: shadow top and left, light line bottom and right, the middle open for the strip's wood",
    "wood": "the maintain slot's wood: a crop of the strip's wood shaded by the original's smooth light (the slot is an opening in the strip art)",
    "plug": "the plug of a closed maintain slot: a crop of the strip's wood shaded by the original's smooth light",
    "outline": "a flat-colour selection frame by rule: every original pixel is a block of the master, lines crisp and exact",
    "roller": "the shaded roller by rule from the original's frame: smooth along its height, crisp across",
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


def bilinear(image: pngio.Image, x: float, y: float) -> list[float]:
    x -= 0.5
    y -= 0.5
    x0, y0 = int(math.floor(x)), int(math.floor(y))
    fx, fy = x - x0, y - y0
    out = [0.0, 0.0, 0.0]
    for dx, dy, w in ((0, 0, (1 - fx) * (1 - fy)), (1, 0, fx * (1 - fy)), (0, 1, (1 - fx) * fy), (1, 1, fx * fy)):
        p = image.pixel(min(image.w - 1, max(0, x0 + dx)), min(image.h - 1, max(0, y0 + dy)))
        for c in range(3):
            out[c] += w * p[c]
    return out


def ring_centre(image: pngio.Image) -> tuple[float, float]:
    """The centre of a ring master: the centroid of its transparent pixels near the middle (the hole)."""
    pts = [(x, y) for y in range(image.h) for x in range(image.w) if image.pixel(x, y)[3] < 10 and math.hypot(x - image.w / 2, y - image.h / 2) < image.w * 0.3]
    if not pts:
        raise KitError("a brass ring master has no hole")
    return sum(p[0] for p in pts) / len(pts) + 0.5, sum(p[1] for p in pts) / len(pts) + 0.5


def unwrap(image: pngio.Image, first_row: int) -> list[list[list[float]]]:
    """The brass of a ring master unrolled along its circumference: for every pixel step round the circle at the band's middle, BRASS_ROWS colours from `first_row` px of radius outwards (bilinear)."""
    cx, cy = ring_centre(image)
    n = int(round(2 * math.pi * (first_row + BRASS_ROWS / 2)))
    strip = []
    for i in range(n):
        th = 2 * math.pi * i / n
        cos, sin = math.cos(th), math.sin(th)
        column = []
        for j in range(BRASS_ROWS):
            r = first_row + j + 0.5
            x, y = cx + r * cos, cy + r * sin
            if image.pixel(min(image.w - 1, max(0, int(x))), min(image.h - 1, max(0, int(y))))[3] < 250:   # the unrolled band must be brass all through: no hole, no soft rim
                raise KitError("the brass band of a ring master is not opaque brass all through: the band rows no longer fit the master")
            column.append(bilinear(image, x, y))
        strip.append(column)
    return strip


def grain_of(strip: list[list[list[float]]]) -> list[list[float]]:
    """The strip's detail: its luminance minus its slow light (a Gaussian, 3 px round the circle and 1.5 px across), so the grain is kept and the master's own shading is not."""
    n, rows = len(strip), len(strip[0])
    plane = [[lum(*c) for c in column] for column in strip]
    sigma = 3
    kernel = [(di, dj, math.exp(-(di * di) / (2 * sigma * sigma)) * math.exp(-(dj * dj) / (2 * 1.5 ** 2))) for di in range(-2 * sigma, 2 * sigma + 1) for dj in range(-2, 3)]
    out = []
    for i in range(n):
        row = []
        for j in range(rows):
            acc = total = 0.0
            for di, dj, w in kernel:
                if 0 <= j + dj < rows:
                    acc += w * plane[(i + di) % n][j + dj]
                    total += w
            row.append(plane[i][j] - acc / total)
        out.append(row)
    return out


def brass_material(masters: Path) -> dict:
    """The ring's grain (the two rings' details end to end, longer than the lens ring's circumference, so nothing repeats) and its body tone (the mean colour of the two bands)."""
    grain, tones, hashes = [], [], {}
    for part, first_row in BRASS_SOURCES:
        path = masters / f"{part}.png"
        if not path.is_file():
            raise KitError(f"{path} is missing: the approved brass of the kit (#3259 HUD rings, #3260 socket rings) the lens ring's grain is taken from")
        strip = unwrap(pngio.read(path), first_row)
        grain += grain_of(strip)
        count = len(strip) * BRASS_ROWS
        tones.append([sum(c[k] for column in strip for c in column) / count for k in range(3)])
        hashes[part] = sha256_file(path)
    tone = [sum(t[k] for t in tones) / len(tones) for k in range(3)]
    return {"grain": grain, "tone": tone, "sha256": hashes}


def angle_gap(a: float, b: float) -> float:
    return (a - b + 180.0) % 360.0 - 180.0


def smoothstep(lo: float, hi: float, x: float) -> float:
    t = clamp((x - lo) / (hi - lo), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def light_of(th: float, rho: float) -> tuple[float, float]:
    """The light on polished, cast brass at angle `th` (degrees, image coordinates: the top left is 225) and `rho` (0 inner edge, 1 outer edge of the band): the body's multiplier and the copper mix. Alternating light and dark
    reflections round the circumference (sharp edges, whole harmonics so the circle closes), a broad sheen and a narrow sharp highlight ridge at the top left, a warm copper reflection at the bottom right, a narrow dark chamfer with its thin
    bright edge along the inner side, and a roll-off at the outer edge (no dark outline)."""
    seg = math.sin(math.radians(th * 3 + 30)) + 0.6 * math.sin(math.radians(th * 5 + 110)) + 0.4 * math.sin(math.radians(th * 8 + 10))
    light = 0.95 + 0.34 * math.tanh(3.5 * seg)
    light += 0.55 * math.exp(-(angle_gap(th, 225) / 55) ** 2) * (0.5 + 0.5 * math.sin(math.pi * clamp(rho * 1.2, 0.0, 1.0)))
    light += 2.4 * math.exp(-(angle_gap(th, 222) / 16) ** 2) * math.exp(-((rho - 0.42) / 0.09) ** 2)
    light += 0.7 * math.exp(-(angle_gap(th, 300) / 6) ** 2) * math.exp(-((rho - 0.40) / 0.09) ** 2)
    light -= 0.30 * math.exp(-(angle_gap(th, 315) / 45) ** 2) + 0.25 * math.exp(-(angle_gap(th, 135) / 40) ** 2)
    light *= 0.15 + 0.85 * smoothstep(0.0, 0.14, rho)
    light += 0.45 * math.exp(-((rho - 0.17) / 0.035) ** 2) * (0.4 + 0.6 * math.exp(-(angle_gap(th, 225) / 80) ** 2))
    light *= 1 - 0.45 * smoothstep(0.80, 1.0, rho)
    copper = math.exp(-(angle_gap(th, 48) / 38) ** 2) * smoothstep(0.25, 0.8, rho)
    return max(0.05, light), copper


BRASS_DARK, BRASS_HIGH, COPPER = (24, 14, 6), (240, 194, 106), (214, 118, 44)   # the ramp's ends: shadow, warm highlight (no white), copper reflection


def brass_colour(light: float, copper: float, tone: list[float]) -> list[float]:
    if light < 1:
        c = [BRASS_DARK[k] + (tone[k] - BRASS_DARK[k]) * light for k in range(3)]
    else:
        t = clamp((light - 1) / 1.2, 0.0, 1.0)
        c = [tone[k] + (BRASS_HIGH[k] - tone[k]) * t for k in range(3)]
    if copper > 0:
        w = clamp(copper * 0.85, 0.0, 1.0) * clamp(0.5 + 0.5 * light, 0.0, 1.0)
        c = [c[k] * (1 - w) + COPPER[k] * w * clamp(0.7 + 0.5 * light, 0.0, 1.15) for k in range(3)]
    return c


def render_ring(radius: float, material: dict, size: tuple[int, int], fill: tuple[int, int, int] | None = None, pad: int = LENS_PAD) -> pngio.Image:
    """The ring at the master size: the lens art's square (`size` minus `pad` on every side) with `pad` master px added all round, so the ring is drawn whole (the old art clipped it where the circle touches its edge). The shape is
    analytic: a band of RING_WIDTH original px outside the circle of `radius` original px, anti-aliased, centred on the canvas; its colour is polished brass (`light_of`, the body tone and the grain of the kit's approved brass).
    A fill colour makes the circle's inside opaque (art 367)."""
    w, h = size
    s = (w - 2 * pad) / (2 * radius)            # master px per original px (the art's square is the circle's diameter)
    if len(material["grain"]) < 2 * math.pi * (radius + RING_WIDTH / 2) * s:
        raise KitError("the brass grain is shorter than the lens ring's circumference: it would repeat with a seam")
    cx, cy = w / 2.0, h / 2.0
    grain, tone = material["grain"], material["tone"]
    rows, count = BRASS_ROWS, len(grain)
    middle = (radius + RING_WIDTH / 2) * s
    tone_light = lum(*tone)
    out = pngio.Image.blank(w, h)
    for my in range(h):
        for mx in range(w):
            dx, dy = mx + 0.5 - cx, my + 0.5 - cy
            d = (math.hypot(dx, dy) - radius * s) / s   # original px beyond the circle's edge (negative inside)
            inside_px = d * s
            if d > RING_WIDTH + 0.3 and fill is None:
                continue
            r = g = b = 0.0
            alpha = 0.0
            if -0.3 <= d <= RING_WIDTH + 0.3:
                th = (math.degrees(math.atan2(dy, dx)) + 360.0) % 360.0
                rho = clamp(d / RING_WIDTH, 0.0, 1.0)
                light, copper = light_of(th, rho)
                colour = brass_colour(light, copper, tone)
                k = 1 + GRAIN * grain[int(math.radians(th) * middle) % count][min(rows - 1, int(rho * rows))] / tone_light
                r, g, b = (clamp(v * k, 0.0, 255.0) for v in colour)
                alpha = min(clamp(inside_px + 0.5, 0.0, 1.0), clamp((RING_WIDTH - d) * s + 0.5, 0.0, 1.0))
            if fill is not None and inside_px < 0.5:
                cover = clamp(0.5 - inside_px, 0.0, 1.0)  # the fill's own soft edge, under the ring
                total = alpha + (1 - alpha) * cover
                if total > 0:  # the ring over the fill, in straight (not premultiplied) colour
                    r = (r * alpha + fill[0] * (1 - alpha) * cover) / total
                    g = (g * alpha + fill[1] * (1 - alpha) * cover) / total
                    b = (b * alpha + fill[2] * (1 - alpha) * cover) / total
                alpha = total
            if alpha <= 0:
                continue
            i = (my * w + mx) * 4
            out.data[i:i + 4] = bytes((int(round(r)), int(round(g)), int(round(b)), int(round(alpha * 255))))
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
    if part.family in ("lens", "lens_whole"):
        if "brass" not in cache:
            cache["brass"] = brass_material(masters)
            cache["hole"] = hole_of(read_frame(frames, LENS_ARTS[0]))
        art_of_shape = read_frame(frames, part) if part.family == "lens" else read_frame(frames, LENS_ARTS[0])
        cx, cy, _ = hole_of(art_of_shape) if part.family == "lens" else cache["hole"]
        if abs(cx - art_of_shape.w / 2) > 0.6 or abs(cy - art_of_shape.h / 2) > 0.6:
            raise KitError(f"{part.ident}: the lens circle is not centred in its art (centre {cx:.1f}, {cy:.1f} of {art_of_shape.w}x{art_of_shape.h}); the ring is drawn on the canvas centre")
        fill = None
        if part.fill:
            source = part if part.art else BY_ID["hardware.schm_pc_lens"]
            original = read_frame(frames, source)
            r, g, b, a = original.pixel(original.w // 2, original.h // 2)
            if a != 255:
                raise KitError(f"{part.ident}: the original's fill is not opaque")
            fill = (r, g, b)
        radius = cache["hole"][2]
        whole = render_ring(radius, cache["brass"], LENS_WHOLE, fill)
        image = whole if part.family == "lens_whole" else whole.crop(LENS_PAD, LENS_PAD, LENS_BASE, LENS_BASE)
        return image, {"circle_radius": round(radius, 2), "fill": list(fill) if fill else None, "pad": LENS_PAD, "ring_width": RING_WIDTH, "brass_rows": BRASS_ROWS, "grain": GRAIN,
                       "brass_sources": [list(x) for x in BRASS_SOURCES], "brass_sha256": cache["brass"]["sha256"], "shape_from": f"{LENS_ARTS[0].art}-{LENS_ARTS[0].name}/f0 (the circle)"}
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
    if part.family in ("lens", "lens_whole", "outline", "roller", "wood", "plug"):
        w, h = image.w, image.h
        hollow = 0
        for y in range(1, h - 1):
            for x in range(1, w - 1):
                a = alpha[y * w + x]
                if 0 < a < 250 and all(alpha[(y + dy) * w + x + dx] for dy in (-1, 0, 1) for dx in (-1, 0, 1)):
                    hollow += 1
        limit = 0.30 if part.family.startswith("lens") else 0.0   # a lens's soft edges (the inner anti-alias, the outer fade) are translucent by design, a flat detail's are not inside it
        if drawn and hollow > limit * drawn:
            failures.append(f"{hollow} translucent pixels inside a solid detail (the detail must be opaque)")
        if part.family in ("wood", "plug") and any(a != 255 for a in alpha):
            failures.append("a wood tile is opaque")
    return failures


def provenance_record(part: Part, frames: Path, extra: dict, size: tuple[int, int], blob: bytes) -> dict:
    sources = {}
    arts = list(extra.get("pooled_arts") or ([part.art] if part.art else []))
    if part.family in ("lens", "lens_whole"):          # the circle comes from the first lens art, the filled look from art 367
        arts += [a for a in [LENS_ARTS[0].art] + ([BY_ID["hardware.schm_pc_lens"].art] if part.fill else []) if a not in arts]
    names = {p.art: p.name for p in PARTS}
    for art in arts:
        path = frames / f"{art}-{names[art]}" / "f0.png"
        sources[f"{art}-{names[art]}/f0"] = sha256_file(path)
    return {"part": part.ident, "rule": "hardware_kit", "family": part.family, "tool_version": TOOL_VERSION, "date": datetime.date.today().isoformat(), "source": f"{part.art}-{part.name}/f0" if part.art else f"{LENS_ARTS[0].art}-{LENS_ARTS[0].name}/f0 (the circle) and the kit's brass rings",
            "sources_sha256": sources, **extra, "image_size": list(size), "image_sha256": hashlib.sha256(blob).hexdigest(), "run_by": "agent"}


def cmd_plan(args: argparse.Namespace) -> int:
    manifest = load_manifest()
    rows = {r["id"]: r for r in manifest["parts"]}
    for part in PARTS:
        row = rows.get(part.ident)
        print(f"{part.ident:34} art {part.art:>3} {part.family:10} {'x'.join(map(str, row['size'])) if row and row['size'] else 'open' if row else 'NO ROW'}")
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
        size = LENS_WHOLE if part.family == "lens_whole" else master_of(part.ident, manifest)
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
        sets = (("Lens ring drop-in rows (the art's own square: no wood corners, the circle open, the ring brass band wider; the whole 354 px ring is hardware.lens_ring)", ["hardware.pcwincvr", "hardware.lns_loot", "hardware.optionspclens", "hardware.schm_pc_lens"], 318),
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
