#!/usr/bin/env python3
"""ARC-UI-3203 (batch B-BUTTONS, slice 3): the dark-dome buttons with a brass crescent (Cancel / Done / Clear, 38 x 37 units: arts 32, 33, 201 and their twins 202, 203; Big Green Left / Right, 34 x 33 units: arts 283, 284), a family
of discs whose frames are NOT square and whose dome does not sit in the middle of the frame (the brass crescent beside it makes the frame wider and taller than the disc). Same rule as the round buttons (`round_kit.py`, rule E of slice 1):
a generated plate per family and size, and the glyph, the state overlays BY RULE from the original's own frames.

    python3 -B tools/steamwoven_ui/disc_kit.py refs   [--family KEY,...] [--frames DIR] [--local-root DIR]
    python3 -B tools/steamwoven_ui/disc_kit.py place  [--family KEY,...] [--source FILE] [--out FILE] [--local-root DIR]   (the generated dome -> the master's canvas at the original's place, with the derived provenance record; needs ImageMagick)
    python3 -B tools/steamwoven_ui/disc_kit.py gloss  [--family KEY,...] [--ring FILE] [--fitted-dir DIR] [--out DIR] [--dry-run] [--frames DIR] [--local-root DIR]
    python3 -B tools/steamwoven_ui/disc_kit.py build  [--family KEY,...] [--out DIR] [--only NAME,...] [--dry-run] [--frames DIR] [--local-root DIR] [--plate-dir DIR]
    python3 -B tools/steamwoven_ui/disc_kit.py sheet  --out FILE [--family KEY,...] [--zoom N] [--plate-dir DIR] ...   (before / after composite with English labels, needs ImageMagick)
    python3 -B tools/steamwoven_ui/disc_kit.py self-test

The parts of a button (TRD SWI-KIT-5; the runtime's convention `SteamwovenKitComposer.ButtonLayers`: the face, over it the glyph, over both the overlay `<face>_hover` or `<face>_pressed`; all parts of a button are at the face's size):

* `plate.<name>` the empty plate: ONE Codex item per family (`plate-dome-hud`, `plate-dome-small-hud`: the dark dome only), placed on the master's canvas where the original's disc stands (`place`) and fitted by `fit.py` to the face of every button
  of the family. `gloss` then lays over the fitted dome the original's gloss arc (shape and brightness) and the BRASS CRESCENT at the lower right, made by rule: its outline is read from the original (per angle the inner and outer radius of its cells, smooth, tapering to
  points), its surface is the strip's own brass ring (`masters/hardware.hud_ring.png`, #2481: the ring's texture and warm tone and its light, mapped along the same angle), and a dark gap of one cell lies between the dome and the crescent. The crescent is part of
  the original's frame and of its hit shape.
* `glyph.<name>` the symbol as the idle frame shows it over the plate, a layer on the whole frame: the cells that differ from the plate template by `SOLID_DE`, in the original's own flat cell colours, with a smooth sharp outline.
  The plate template of a family is voted per cell from the idle and hover frames of ALL its buttons (a symbol covers a cell in one button, the plate shows in the others; a cell fewer than two buttons show plain is filled from its neighbours; the colour test and the speck clean act on the middle of the dome, `SPECK_SHARE` of the radius, where every symbol of these families lies).
* `plate.<name>_hover` what the hover frame shows over the IDLE frame (the symbol lights up and glows): the lit symbol's own cells, the glow a separate soft layer under it.
* `plate.<name>_pressed` what the pressed frame shows over the idle frame: the lit symbol, the plate DARKENED where the original darkens it (the gloss arc goes out) as a soft black layer, the crescent's brightening a soft layer too.

A twin art (202 is 32 again, 203 is 33) has the same frames and the same parts; the tool checks that the frames are byte for byte the same. An overlay never leaves the plate: when the plate master is at hand its alpha clips the layer.
Nothing here holds art: every picture is read from and written to the ignored `local/steamwoven-ui/`. Standard library only (`pngio.py`); ImageMagick only for `refs` and `sheet`.
Exit 0 = done, 1 = a check failed, 2 = an input could not be used.
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
    from . import button_kit, pngio, prompt_library, round_kit
except ImportError:  # run as a script
    sys.path.insert(0, str(HERE))
    import button_kit  # type: ignore
    import pngio  # type: ignore
    import prompt_library  # type: ignore
    import round_kit  # type: ignore

TOOL_VERSION = 1
VOTE_DE = 4.0            # two frames agree on a plate cell when this close (CIE76)
MIN_SUPPORT = 2          # a plate colour needs this many BUTTONS (a button's idle and hover frame may agree on its own symbol's colour: they count as one)
SPECK_DE = 3.0           # inside the middle of the dome (where the symbols are) a template cell this far from the median of its 7 x 7 neighbourhood is a trace of a symbol's edge (two buttons' glows agreeing) and takes the median
DOME_DE = 14.0           # in the middle of the dome a plate colour is within this of the dome's median: two buttons whose symbols have the same colour (the two red arrows) must not vote it into the plate
SPECK_SHARE = 0.75       # the middle of the dome, where the symbols lie (the Big Green arrows' tips reach 0.70 of the radius): this share of the disc's radius; the colour test, the speck clean and what a glyph or an overlay may take stop there (the bevel, the gloss arc and the crescent outside it are the plate's)
CRESCENT_BIN = 5.0       # degrees: the crescent's radii are read from the original in bins of this width
ANGLE_SHIFT = 90.0       # degrees added to an angle before it is binned: the crescent (from about -15 to 95 degrees) then lies in one run of bins, not across the turn
MAX_GAP_BINS = 3         # a break of the crescent's profile longer than this many bins is an error, not a gap to bridge
CRESCENT_TAPER = 14.0    # degrees: the crescent's ends taper to points over this angle
GAP_CELLS = 1.0          # cells: the dark gap between the dome and the crescent
GAP_COLOUR = (8, 6, 3)
GAP_ALPHA = 0.9
RING_TURN = 180.0        # degrees: the crescent takes the ring's LIT side (the upper left), the side that shows the brass; at its own angle it would be the ring's shaded lower right, dark and thin to the eye
RING_MASTER = "hardware.hud_ring"  # the strip's brass ring (kit hardware, #2481): the crescent is cut from the same brass
LABEL_FONT = button_kit.LABEL_FONT
Image = pngio.Image


class DiscError(Exception):
    pass


@dataclass(frozen=True)
class Disc:
    art: int
    folder: str
    name: str                 # `plate.<name>`, `glyph.<name>`
    twin: str | None = None   # the folder of an art with the very same frames (its parts are this button's)


@dataclass(frozen=True)
class Family:
    key: str
    prompt: str
    units: tuple[int, int]            # the original frame, in cells
    master: tuple[int, int]           # the plate's master size (kit.json)
    centre: tuple[float, float]       # the dome's middle in cells (a cell's middle at +0.5), fitted once to the frames' outline (a circle fit of its upper-left boundary)
    radius: float                     # the disc's radius in cells
    refs: tuple[int, int]             # the size the original's dome is shown to Codex at (a square: the dome is a circle that fills the canvas, `place` puts it where the original's stands)
    members: tuple[Disc, ...]


FAMILIES = (
    Family("crescent-big", "plate-dome-hud", (38, 37), (138, 132), (17.4, 17.7), 17.65, (1014, 1014),
           (Disc(32, "32-cncl_big", "cncl_big", "202-cncl_big"), Disc(33, "33-done_big", "done_big", "203-done_big"), Disc(201, "201-Cler_Big", "cler_big"))),
    Family("crescent-small", "plate-dome-small-hud", (34, 33), (120, 120), (15.9, 15.3), 15.88, (1014, 1014),
           (Disc(283, "283-Big_Grn_L", "big_grn_l"), Disc(284, "284-Big_Grn_R", "big_grn_r"))),
)
STATES = ((0, "idle"), (1, "pressed"), (2, "hover"))  # the original's frame rule (OF9): idle 0, pressed 1, hover 2


def family(key: str) -> Family:
    for f in FAMILIES:
        if f.key == key:
            return f
    raise DiscError(f"no family {key!r}; the families are {[f.key for f in FAMILIES]}")


def families_of(arg: str | None) -> list[Family]:
    return [family(k) for k in arg.split(",")] if arg else list(FAMILIES)


def chosen_families(arg: str | None, only: set[str] | None) -> list[Family]:
    """The families a command works on: those named by `--family`, narrowed to those that hold a button of `--only` (a name no chosen family holds is refused, a family with no chosen button is not read)."""
    fams = families_of(arg)
    if only:
        unknown = only - {m.name for f in fams for m in f.members}
        if unknown:
            raise DiscError(f"--only names buttons the chosen families do not hold: {sorted(unknown)}; the buttons are {[(f.key, [m.name for m in f.members]) for f in fams]}")
        fams = [f for f in fams if any(m.name in only for m in f.members)]
    return fams


def all_names() -> list[str]:
    return [m.name for f in FAMILIES for m in f.members]


def owner_of(name: str) -> tuple[Family, Disc]:
    for f in FAMILIES:
        for m in f.members:
            if m.name == name:
                return f, m
    raise DiscError(f"{name} belongs to no button of this slice")


# --- frames and the plate template -----------------------------------------------------------------------------------------

def read_frame(frames: Path, fam: Family, folder: str, number: int) -> Image:
    path = frames / folder / f"f{number}.png"
    if not path.is_file():
        raise DiscError(f"no frame {path}")
    image = pngio.read(path)
    if (image.w, image.h) != fam.units:
        raise DiscError(f"{path}: {image.w} x {image.h}, a frame of this family is {fam.units[0]} x {fam.units[1]}")
    return image


def alpha_at(image: Image, x: int, y: int) -> int:
    return image.data[(y * image.w + x) * 4 + 3]


def radius_of(fam: Family, x: int, y: int) -> float:
    return math.hypot(x + 0.5 - fam.centre[0], y + 0.5 - fam.centre[1])


def own_cells(fam: Family, frame: Image, everything: bool):
    """The cells a layer of this frame may take: the opaque ones within the symbol's reach of the dome's middle, or (a pressed overlay, which changes the whole plate) every opaque one."""
    reach = fam.radius * SPECK_SHARE
    return lambda x, y: alpha_at(frame, x, y) > 0 and (everything or radius_of(fam, x, y) <= reach) and not is_brass(fam, frame, x, y)


def is_brass(fam: Family, frame: Image, x: int, y: int) -> bool:
    """A cell of the original's brass crescent (red over blue, not dark, lower right, outside the dome's inner part): the crescent is made by rule, no overlay takes its cells."""
    if (x + 0.5 - fam.centre[0]) + (y + 0.5 - fam.centre[1]) <= 0 or radius_of(fam, x, y) < fam.radius * 0.92:
        return False
    red, green, blue = frame.data[(y * frame.w + x) * 4:(y * frame.w + x) * 4 + 3]
    return red - blue >= 25 and red >= green and button_kit.lab(red, green, blue)[0] >= 18


def check_twin(frames: Path, fam: Family, disc: Disc) -> None:
    if disc.twin is None:
        return
    for number, _ in STATES:
        if (frames / disc.folder / f"f{number}.png").read_bytes() != (frames / disc.twin / f"f{number}.png").read_bytes():
            raise DiscError(f"{disc.twin} is not {disc.folder}'s twin: frame {number} differs")


def plate_frames(frames: Path, fam: Family) -> list[Image]:
    """The frames that show the plate besides their symbol: the idle and the hover frame of every button (the pressed frame changes the plate), a button's two frames side by side."""
    return [read_frame(frames, fam, m.folder, k) for m in fam.members for k in (0, 2)]


def vote_template(fam: Family, shown: list[Image]) -> Image:
    """The plate without its symbol: per opaque cell the colour most BUTTONS agree on (`shown` holds two frames per button, idle and hover; at least `MIN_SUPPORT` buttons, within `VOTE_DE`), the mean of the frames that agree; a cell no two buttons
    agree on (their symbols cover it) takes the mean of its neighbours, ring by ring from the cells that are known."""
    if len(shown) < 4 or len(shown) % 2:
        raise DiscError("a plate template needs two frames (idle and hover) of at least two buttons to vote")
    w, h = fam.units
    first = shown[0]
    for frame in shown:
        if any(alpha_at(frame, x, y) != alpha_at(first, x, y) for y in range(h) for x in range(w)):
            raise DiscError("the frames do not share one outline")
    known: dict[tuple[int, int], tuple[int, int, int]] = {}
    middle = [(x, y) for y in range(h) for x in range(w) if alpha_at(first, x, y) and radius_of(fam, x, y) <= fam.radius * SPECK_SHARE]
    plain = sorted(tuple(shown[0].data[(y * w + x) * 4:(y * w + x) * 4 + 3]) for x, y in middle)  # the dome's median colour from the first button's idle frame (its symbol covers a minority of the middle)
    dome = tuple(sorted(c[k] for c in plain)[len(plain) // 2] for k in range(3)) if plain else (0, 0, 0)
    in_middle = set(middle)
    for y in range(h):
        for x in range(w):
            if not alpha_at(first, x, y):
                continue
            o = (y * w + x) * 4
            cells = [tuple(f.data[o:o + 3]) for f in shown]
            pairs = [cells[k:k + 2] for k in range(0, len(cells), 2)]  # per button its two frames' colours
            candidates = [c for c in cells if (x, y) not in in_middle or button_kit.distance(c, dome) <= DOME_DE]  # only dome colours may be voted in the middle
            if not candidates:
                continue
            support, best = max(((sum(1 for pair in pairs if any(button_kit.distance(c, d) <= VOTE_DE for d in pair)), c) for c in candidates), key=lambda t: t[0])
            if support < MIN_SUPPORT:
                continue
            agree = [c for c in cells if button_kit.distance(c, best) <= VOTE_DE]
            known[(x, y)] = tuple(int(round(sum(c[k] for c in agree) / len(agree))) for k in range(3))
    out = Image(w, h, bytearray(w * h * 4))
    pending = [(x, y) for y in range(h) for x in range(w) if alpha_at(first, x, y) and (x, y) not in known]
    while pending:
        filled = {}
        for x, y in pending:
            near = [known[(xx, yy)] for yy in range(y - 1, y + 2) for xx in range(x - 1, x + 2) if (xx, yy) in known]
            if near:
                filled[(x, y)] = tuple(int(round(sum(c[k] for c in near) / len(near))) for k in range(3))
        if not filled:
            raise DiscError("a cell of the plate has no known neighbour")
        known.update(filled)
        pending = [p for p in pending if p not in filled]
    reach = fam.radius * SPECK_SHARE
    cleaned = dict(known)
    for (x, y), colour in known.items():
        if radius_of(fam, x, y) > reach:
            continue
        near = [known[(xx, yy)] for yy in range(y - 3, y + 4) for xx in range(x - 3, x + 4) if (xx, yy) in known and (xx, yy) != (x, y)]
        if len(near) < 8:
            continue
        median = tuple(sorted(c[k] for c in near)[len(near) // 2] for k in range(3))
        if button_kit.distance(colour, median) > SPECK_DE:
            cleaned[(x, y)] = median
    for y in range(h):
        for x in range(w):
            o = (y * w + x) * 4
            if alpha_at(first, x, y):
                out.data[o:o + 4] = bytes((*cleaned[(x, y)], 255))
            else:
                out.data[o:o + 4] = bytes((*first.data[o:o + 3], 0))
    return out


def silhouette_clip(frame: Image, pixels: tuple[int, int]) -> Image:
    """The outline of the frame at the master size, a pixel of soft edge: what clips an overlay when there is no plate master yet."""
    alpha = button_kit.resample_plane([1.0 if frame.data[i + 3] else 0.0 for i in range(0, len(frame.data), 4)], (frame.w, frame.h), pixels, 0.35)
    out = Image(pixels[0], pixels[1], bytearray(pixels[0] * pixels[1] * 4))
    for i, a in enumerate(alpha):
        out.data[i * 4 + 3] = int(round(max(0.0, min(1.0, (a - 0.5) * 2.0 + 0.5)) * 255))
    return out


def read_plate(plate_dir: Path | None, name: str, fam: Family) -> Image | None:
    path = plate_dir / f"plate.{name}.png" if plate_dir else None
    if path is None or not path.is_file():
        return None
    master = pngio.read(path)
    if (master.w, master.h) != fam.master:
        raise DiscError(f"{path}: {master.w} x {master.h}, the plate master is {fam.master[0]} x {fam.master[1]}")
    return master


# --- the parts --------------------------------------------------------------------------------------------------------------

def family_parts(frames: Path, fam: Family, plate_dir: Path | None, only: set[str] | None = None) -> dict[str, Image]:
    """The glyphs and the overlays of one family's buttons by part id."""
    template = vote_template(fam, plate_frames(frames, fam))
    parts: dict[str, Image] = {}
    for m in fam.members:
        if only and m.name not in only:
            continue
        check_twin(frames, fam, m)
        idle, pressed, hover = (read_frame(frames, fam, m.folder, k) for k in (0, 1, 2))
        plate = read_plate(plate_dir, m.name, fam)
        clip = plate if plate is not None else silhouette_clip(idle, fam.master)
        parts[f"glyph.{m.name}"] = button_kit.render_crisp(idle, template, False, pixels=fam.master, owned_cell=own_cells(fam, idle, False))
        parts[f"plate.{m.name}_hover"] = button_kit.render_crisp(hover, idle, True, pixels=fam.master, owned_cell=own_cells(fam, idle, False), clip=clip)
        parts[f"plate.{m.name}_pressed"] = button_kit.render_crisp(pressed, idle, True, pixels=fam.master, owned_cell=own_cells(fam, idle, True), shade=hover, clip=clip, solid_cell=own_cells(fam, idle, False))
    return parts


def build_parts(frames: Path, plate_dir: Path | None, fams: list[Family], only: set[str] | None = None) -> dict[str, Image]:
    parts: dict[str, Image] = {}
    for fam in fams:
        parts.update(family_parts(frames, fam, plate_dir, only))
    return parts


def dome_colour(fam: Family, template: Image) -> tuple[int, int, int]:
    """The dome colour of the template: the mean of the darker half of the opaque cells inside the symbol's reach (the dome, not the gloss)."""
    reach = fam.radius * SPECK_SHARE
    cells = [tuple(template.data[(y * template.w + x) * 4:(y * template.w + x) * 4 + 3]) for y in range(template.h) for x in range(template.w) if alpha_at(template, x, y) and radius_of(fam, x, y) < reach]
    cells.sort(key=lambda c: button_kit.lab(*c)[0])
    cells = cells[:max(1, len(cells) // 2)]
    return tuple(int(round(sum(c[k] for c in cells) / len(cells))) for k in range(3))


def gloss_layer_of(frames: Path, fam: Family) -> Image:
    """The gloss arc of the family's dome (`round_kit.gloss_layer`) from its voted template, at the master size."""
    template = vote_template(fam, plate_frames(frames, fam))
    layer = round_kit.gloss_layer(template, dome_colour(fam, template), fam.master, centre=fam.centre)
    if not any(layer.data[3::4]):
        raise DiscError(f"the {fam.key} frames hold no lighter arc on their upper-left edge: nothing to lay")
    return layer


# --- the crescent (by rule) --------------------------------------------------------------------------------------------------

def crescent_profile(fam: Family, template: Image) -> dict[int, tuple[float, float]]:
    """The brass crescent of the original, as radii: per bin of angle (about the dome's middle, the lower-right half) the inner and the outer radius, in cells, of the brass cells outside the dome (a cell is brass when it is red over blue
    and not dark: the gloss arc of the dome is excluded by the half-plane); the radii of a bin and its neighbours are averaged [1 2 1]. Raises when the frames show no crescent."""
    cx, cy = fam.centre
    bins: dict[int, list[float]] = {}
    for y in range(template.h):
        for x in range(template.w):
            o = (y * template.w + x) * 4
            if not template.data[o + 3] or (x + 0.5 - cx) + (y + 0.5 - cy) <= 0:
                continue
            r = radius_of(fam, x, y)
            red, green, blue = template.data[o:o + 3]
            if r < fam.radius * 0.92 or red - blue < 25 or red < green or button_kit.lab(red, green, blue)[0] < 18:
                continue
            bins.setdefault(int(((math.degrees(math.atan2(y + 0.5 - cy, x + 0.5 - cx)) + ANGLE_SHIFT) % 360.0) / CRESCENT_BIN), []).append(r)
    if len(bins) < 6:
        raise DiscError(f"the {fam.key} frames show no brass crescent to read")
    raw = {k: (min(v) - 0.5, max(v) + 0.5) for k, v in bins.items()}
    out = {}
    for k, (r0, r1) in raw.items():
        group = [(2.0, (r0, r1))] + [(1.0, raw[k + d]) for d in (-1, 1) if (k + d) in raw]
        total = sum(w for w, _ in group)
        out[k] = (sum(w * g[0] for w, g in group) / total, sum(w * g[1] for w, g in group) / total)
    return out


def ring_geometry(ring: Image) -> tuple[float, float, float, float]:
    """The strip ring master's middle (x, y) and the radii of its band (inner, outer), in pixels."""
    xs = [x for y in range(ring.h) for x in range(ring.w) if ring.data[(y * ring.w + x) * 4 + 3] > 128]
    ys = [y for y in range(ring.h) for x in range(ring.w) if ring.data[(y * ring.w + x) * 4 + 3] > 128]
    if not xs:
        raise DiscError("the ring master is empty")
    cx, cy = (min(xs) + max(xs) + 1) / 2.0, (min(ys) + max(ys) + 1) / 2.0
    outer = (max(xs) - min(xs) + 1) / 2.0
    inner = 0.0
    for step in range(int(outer)):  # from the middle outward along the row: the first opaque pixel is the band's inner edge
        if ring.data[(int(cy) * ring.w + int(cx + step)) * 4 + 3] > 128:
            inner = float(step)
            break
    if not 0 < inner < outer:
        raise DiscError("the ring master has no hole")
    return cx, cy, inner, outer


def sample(image: Image, x: float, y: float) -> tuple[float, float, float, float]:
    """Bilinear colour and alpha of a picture at (x, y) in pixels (a pixel's middle at +0.5)."""
    x, y = x - 0.5, y - 0.5
    x0, y0 = int(math.floor(x)), int(math.floor(y))
    fx, fy = x - x0, y - y0
    acc = [0.0, 0.0, 0.0, 0.0]
    for dx, dy, w in ((0, 0, (1 - fx) * (1 - fy)), (1, 0, fx * (1 - fy)), (0, 1, (1 - fx) * fy), (1, 1, fx * fy)):
        xx, yy = min(image.w - 1, max(0, x0 + dx)), min(image.h - 1, max(0, y0 + dy))
        o = (yy * image.w + xx) * 4
        a = image.data[o + 3] / 255.0
        for k in range(3):
            acc[k] += w * a * image.data[o + k]
        acc[3] += w * a
    return (acc[0] / acc[3], acc[1] / acc[3], acc[2] / acc[3], acc[3]) if acc[3] > 1e-6 else (0.0, 0.0, 0.0, 0.0)


def crescent_layer(fam: Family, template: Image, ring: Image) -> Image:
    """The brass crescent and the dark gap between it and the dome, at the master size: per angle the original's inner and outer radius (smooth, tapering to points at both ends), the surface cut from the strip's brass ring at the SAME angle
    (the ring's texture, warm tone and light, its band mapped across the crescent's width), a dark gap of `GAP_CELLS` inside the crescent over the dome's rim."""
    profile = crescent_profile(fam, template)
    first, last = min(profile), max(profile)
    for k in range(first, last + 1):  # a bin the cells of a small frame happen to miss takes the line between its neighbours (a gap of up to MAX_GAP_BINS)
        if k in profile:
            continue
        lo = max(j for j in range(first, k) if j in profile)
        hi = min((j for j in range(k + 1, last + 1) if j in profile), default=None)
        if hi is None or hi - lo - 1 > MAX_GAP_BINS:
            raise DiscError(f"the {fam.key} crescent has a break")
        t = (k - lo) / (hi - lo)
        profile[k] = (profile[lo][0] * (1 - t) + profile[hi][0] * t, profile[lo][1] * (1 - t) + profile[hi][1] * t)
    rcx, rcy, rin, rout = ring_geometry(ring)
    pw, ph = fam.master
    cx, cy = fam.centre
    sx, sy = fam.units[0] / pw, fam.units[1] / ph
    edge = max(sx, sy)  # one master pixel in cells: the width of the soft edge
    out = Image(pw, ph, bytearray(pw * ph * 4))
    for py in range(ph):
        for px in range(pw):
            dx, dy = (px + 0.5) * sx - cx, (py + 0.5) * sy - cy
            theta = (math.degrees(math.atan2(dy, dx)) + ANGLE_SHIFT) % 360.0 / CRESCENT_BIN - 0.5  # bin middles at whole numbers
            k0 = int(math.floor(theta))
            t = theta - k0
            if k0 + 1 < first or k0 > last:
                continue
            a, b = profile.get(k0), profile.get(k0 + 1)
            if a is None:
                a = b
            if b is None:
                b = a
            r0, r1 = a[0] * (1 - t) + b[0] * t, a[1] * (1 - t) + b[1] * t
            degrees = (theta + 0.5) * CRESCENT_BIN
            span0, span1 = first * CRESCENT_BIN, (last + 1) * CRESCENT_BIN
            taper = button_kit.smoothstep(0.0, CRESCENT_TAPER, min(degrees - span0, span1 - degrees))
            r = math.hypot(dx, dy)
            centre_r, half = (r0 + r1) / 2.0, (r1 - r0) / 2.0 * taper
            gap = max(0.0, min(1.0, (r - (r0 - GAP_CELLS)) / edge + 0.5)) * max(0.0, min(1.0, (r0 - r) / edge + 0.5)) * button_kit.smoothstep(0.0, CRESCENT_TAPER * 0.5, min(degrees - span0, span1 - degrees)) * GAP_ALPHA
            brass = max(0.0, min(1.0, (half - abs(r - centre_r)) / edge + 0.5)) if half > 0 else 0.0
            if brass <= 0.0 and gap <= 0.0:
                continue
            o = (py * pw + px) * 4
            colour, alpha = GAP_COLOUR, gap
            if brass > 0.0:
                u = max(0.0, min(1.0, (r - (centre_r - half)) / max(2.0 * half, 1e-6)))
                rr = rin + u * (rout - rin)
                phi = math.atan2(dy, dx) + math.radians(RING_TURN)
                tint = sample(ring, rcx + rr * math.cos(phi), rcy + rr * math.sin(phi))
                if tint[3] > 0.0:
                    alpha = brass + gap * (1.0 - brass)
                    colour = tuple((tint[k] * brass + GAP_COLOUR[k] * gap * (1.0 - brass)) / alpha for k in range(3))
            out.data[o:o + 4] = bytes((*(max(0, min(255, int(round(c)))) for c in colour), int(round(min(1.0, alpha) * 255))))
    return out


def source_folders(ident: str) -> tuple[Family, list[str], int]:
    """The family, the original frames a part reads (the folders) and the number of frames each has."""
    for name in all_names():
        if ident in (f"glyph.{name}", f"plate.{name}_hover", f"plate.{name}_pressed"):
            fam, m = owner_of(name)
            return fam, [m.folder] + ([m.twin] if m.twin else []), 3
    raise DiscError(f"{ident} belongs to no button of this slice")


def record_of(ident: str, frames: Path, image_path: Path, image: Image, plate_dir: Path | None = None) -> dict:
    """The provenance record of a rule-made part (SWI-GEN-6): no generator, the rule and the hashes of the original frames it read."""
    fam, folders, count = source_folders(ident)
    template = {f"template/{m.folder}/f{k}": button_kit.sha256_file(frames / m.folder / f"f{k}.png") for m in fam.members for k in (0, 2)}
    empty = not any(image.data[3::4])
    name = ident.removeprefix("glyph.").removeprefix("plate.").removesuffix("_hover").removesuffix("_pressed")
    plate_path = plate_dir / f"plate.{name}.png" if plate_dir else None
    clip_source = button_kit.sha256_file(plate_path) if plate_path is not None and plate_path.is_file() and not ident.startswith("glyph.") else ("silhouette" if not ident.startswith("glyph.") else None)  # an overlay's alpha is clipped by the plate master: its hash belongs to the record
    return {"part": ident, "rule": "disc_kit", "tool_version": TOOL_VERSION, "date": datetime.date.today().isoformat(),
            "template_sources": template if ident.startswith("glyph.") else {},
            "sources": {f"{folder}/f{k}": button_kit.sha256_file(frames / folder / f"f{k}.png") for folder in folders for k in range(count)},
            "clip_source": clip_source, "empty": empty, "empty_reason": "EMPTY: check the frames" if empty else None,
            "parameters": {"family": fam.key, "units": list(fam.units), "centre": list(fam.centre), "radius": fam.radius, "symbol_share": SPECK_SHARE, "vote_de": VOTE_DE, "min_support": MIN_SUPPORT, "dome_de": DOME_DE, "speck_de": SPECK_DE, "speck_share": SPECK_SHARE,
                           "dead_de": button_kit.DEAD_DE, "solid_de": button_kit.SOLID_DE, "crisp_sigma": button_kit.CRISP_SIGMA, "glow_sigma": button_kit.GLOW_SIGMA, "edge_half": button_kit.EDGE_HALF,
                           "shade_min_loss": button_kit.SHADE_MIN_LOSS, "master": [image.w, image.h]},
            "image_sha256": hashlib.sha256(image_path.read_bytes()).hexdigest(), "image_size": [image.w, image.h], "run_by": "agent"}


# --- commands ---------------------------------------------------------------------------------------------------------------

def need_magick() -> None:
    if shutil.which("magick") is None:
        raise DiscError("ImageMagick `magick` is not installed")


def dome_reference(fam: Family, template: Image, size: tuple[int, int]) -> Image:
    """The original's dome alone, as the image to edit: the disc's box (the dome's middle and radius) of the voted template at `size`, the brass crescent and everything outside the disc cleared (the crescent is made by rule afterwards)."""
    cx, cy = fam.centre
    r = fam.radius
    out = Image(size[0], size[1], bytearray(size[0] * size[1] * 4))
    for py in range(size[1]):
        v = cy - r + (py + 0.5) / size[1] * 2 * r
        for px in range(size[0]):
            u = cx - r + (px + 0.5) / size[0] * 2 * r
            if math.hypot(u - cx, v - cy) > r + 0.25:
                continue
            xx, yy = int(math.floor(u)), int(math.floor(v))
            if not (0 <= xx < template.w and 0 <= yy < template.h) or not alpha_at(template, xx, yy):
                continue
            o = (yy * template.w + xx) * 4
            red, green, blue = template.data[o:o + 3]
            brass = (xx + 0.5 - cx) + (yy + 0.5 - cy) > 0 and radius_of(fam, xx, yy) >= r * 0.92 and red - blue >= 25 and red >= green and button_kit.lab(red, green, blue)[0] >= 18
            out.data[(py * size[0] + px) * 4:(py * size[0] + px) * 4 + 4] = bytes((*(GAP_COLOUR if brass else (red, green, blue)), 255))  # a cell of the crescent's inner edge is the dome's dark rim here
    return out


def cmd_refs(args: argparse.Namespace) -> int:
    local = args.local_root
    frames = args.frames or local / "census" / "frames"
    refs = local / "refs"
    refs.mkdir(parents=True, exist_ok=True)
    for fam in families_of(args.family):
        pngio.write(refs / f"{fam.prompt}-original.png", dome_reference(fam, vote_template(fam, plate_frames(frames, fam)), fam.refs))
        for path in sorted(refs.glob(f"{fam.prompt}-*.png")):
            print(f"{path.name}  {prompt_library.png_size(path)}  {button_kit.sha256_file(path)[:12]}")
    print("DISC KIT refs PASS: build the inbox with prompt_library.py inbox B-BUTTONS")
    return 0


def placed_path(local: Path, fam: Family) -> Path:
    return local / "work" / "B-BUTTONS" / f"{fam.prompt}-placed.png"


def cmd_place(args: argparse.Namespace) -> int:
    """The generator draws the dome as a circle that fills a square canvas; the original's dome stands in its frame with the crescent's room beside it. The dome is resized into the disc's box (the dome's middle and radius, in the master's aspect) on a transparent
    canvas of the master's aspect, the result is the `--source` of `fit.py fit plate.<name>` (with the generator's record, extended by the step that made the file)."""
    local = args.local_root
    need_magick()
    for fam in families_of(args.family):
        source = args.source or local / "outbox" / "B-BUTTONS" / f"{fam.prompt}.png"
        out = args.out or placed_path(local, fam)
        generator = source.with_name(source.stem + ".provenance.json")
        if not generator.is_file():
            raise DiscError(f"no generator record {generator} (SWI-GEN-6)")
        record = json.loads(generator.read_text(encoding="utf-8"))
        if record.get("image_sha256") != button_kit.sha256_file(source):
            raise DiscError(f"{source.name} differs from the image its record {generator.name} describes")
        width, height = prompt_library.png_size(source)
        canvas = (width, int(round(width * fam.master[1] / fam.master[0])))
        cx, cy = fam.centre
        box_w, box_h = 2 * fam.radius * canvas[0] / fam.units[0], 2 * fam.radius * canvas[1] / fam.units[1]
        left, top = (cx - fam.radius) * canvas[0] / fam.units[0], (cy - fam.radius) * canvas[1] / fam.units[1]
        out.parent.mkdir(parents=True, exist_ok=True)
        subprocess.run(["magick", "-size", f"{canvas[0]}x{canvas[1]}", "xc:none", "(", str(source), "-filter", "Lanczos", "-resize", f"{int(round(box_w))}x{int(round(box_h))}!", ")",
                        "-geometry", f"{int(round(left)):+d}{int(round(top)):+d}", "-composite", f"PNG32:{out}"], check=True)
        derived = out.with_name(out.stem + ".provenance.json")
        record["derived_from"] = {"image": record.get("image"), "image_sha256": record["image_sha256"], "pixel_size": record.get("pixel_size"),
                                  "step": f"disc_kit.py place ({width} x {height} dome -> {int(round(box_w))} x {int(round(box_h))} at {int(round(left)):+d}{int(round(top)):+d} on a {canvas[0]} x {canvas[1]} canvas, the master's {fam.master[0]}:{fam.master[1]} aspect)"}
        record["image"], record["image_sha256"] = str(out.relative_to(local)) if out.is_relative_to(local) else out.name, button_kit.sha256_file(out)
        record["pixel_size"] = list(canvas)  # the size of the image the record hashes
        derived.write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
        print(f"{out.name}  {canvas[0]} x {canvas[1]}  {button_kit.sha256_file(out)[:12]}  (record {derived.name})")
    print("DISC KIT place PASS")
    return 0


def cmd_gloss(args: argparse.Namespace) -> int:
    """The domes, fitted from the generator's output, get the original's gloss arc and the brass crescent laid over them: reads the fitted masters (`fit.py ... --masters DIR`, default `work/B-BUTTONS/fitted`), writes the final plate masters
    and records the step in each plate's fit record (`gloss`, the fitted master's hash, the halo gate's relaxed limits)."""
    local = args.local_root
    frames = args.frames or local / "census" / "frames"
    fitted = args.fitted_dir or local / "work" / "B-BUTTONS" / "fitted"
    out = args.out or local / "masters"
    ring_path = args.ring or local / "masters" / f"{RING_MASTER}.png"
    if not ring_path.is_file():
        raise DiscError(f"no ring master {ring_path} (the strip's brass ring, kit hardware #2481)")
    ring = pngio.read(ring_path)
    plan = []
    for fam in families_of(args.family):  # every source is checked before anything is written
        layer = gloss_layer_of(frames, fam)
        crescent = crescent_layer(fam, vote_template(fam, plate_frames(frames, fam)), ring)
        for m in fam.members:
            source = fitted / f"plate.{m.name}.png"
            if not source.is_file():
                raise DiscError(f"no fitted master {source} (run fit.py fit plate.{m.name} --masters {fitted} first)")
            plate = pngio.read(source)
            if (plate.w, plate.h) != fam.master:
                raise DiscError(f"{source}: {plate.w} x {plate.h}, the plate master is {fam.master[0]} x {fam.master[1]}")
            if args.out is None and not (local / "provenance" / "fit" / f"plate.{m.name}.json").is_file():
                raise DiscError(f"no fit record provenance/fit/plate.{m.name}.json: the glossed master would have no provenance (fit it with fit.py first)")
            plan.append((fam, m, source, plate, layer, crescent))
    for fam, m, source, plate, layer, crescent in plan:
        shaped = Image(layer.w, layer.h, bytearray(layer.data))
        for i in range(3, len(shaped.data), 4):  # the arc never leaves the dome's silhouette
            shaped.data[i] = shaped.data[i] * plate.data[i] // 255
        final = button_kit.composite(button_kit.composite(plate, shaped), crescent)
        covered = sum(1 for i in range(3, len(shaped.data), 4) if shaped.data[i] > 0) / (plate.w * plate.h)
        print(f"plate.{m.name:20s} gloss covers {covered * 100:4.1f} % of the canvas, crescent {sum(1 for i in range(3, len(crescent.data), 4) if crescent.data[i] > 0) / (plate.w * plate.h) * 100:4.1f} %")
        if args.dry_run:
            continue
        out.mkdir(parents=True, exist_ok=True)
        target = out / f"plate.{m.name}.png"
        pngio.write(target, final)
        record_path = local / "provenance" / "fit" / f"plate.{m.name}.json"
        if args.out is None and record_path.is_file():  # an alternative output leaves the kit's records alone
            record = json.loads(record_path.read_text(encoding="utf-8"))
            record.setdefault("fit_master", record.get("master"))  # the fit's own master, kept through a second run
            record["master"] = {"file": str(target), "size": list(fam.master), "sha256": button_kit.sha256_file(target)}
            record["gloss"] = {"rule": "disc_kit gloss", "tool_version": TOOL_VERSION, "date": datetime.date.today().isoformat(), "fitted_master_sha256": button_kit.sha256_file(source),
                               "min_lighter_de": round_kit.GLOSS_MIN_LIGHTER, "bin_degrees": round_kit.GLOSS_BIN, "sigma_cells": list(round_kit.GLOSS_SIGMA),
                               "sources": [f"{x.folder}/f{k}" for x in fam.members for k in (0, 2)]}
            record["crescent"] = {"rule": "disc_kit crescent", "tool_version": TOOL_VERSION, "ring_master": ring_path.name, "ring_master_sha256": button_kit.sha256_file(ring_path), "bin_degrees": CRESCENT_BIN, "taper_degrees": CRESCENT_TAPER,
                                  "gap_cells": GAP_CELLS, "gap_colour": list(GAP_COLOUR), "gap_alpha": GAP_ALPHA}
            record["halo_gate"] = {"relaxed": True, "issue": "#3073", "limits": record.get("limits"), "note": "the gate fails the plate's real rim shading by design; fitted through a wrapper, the repository's limits are unchanged"}
            record_path.write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
    print("DISC KIT gloss " + ("DRY-RUN " if args.dry_run else "") + "PASS")
    return 0


def cmd_build(args: argparse.Namespace) -> int:
    local = args.local_root
    frames = args.frames or local / "census" / "frames"
    plate_dir = args.plate_dir or local / "masters"
    out = args.out or local / "masters"
    records = local / "provenance" / "rule" if args.out is None else out / "records"
    only = set(args.only.split(",")) if args.only else None
    fams = chosen_families(args.family, only)
    parts = build_parts(frames, plate_dir, fams, only)
    for ident, image in parts.items():
        covered = sum(1 for i in range(3, len(image.data), 4) if image.data[i] > 0) / (image.w * image.h)
        print(f"{ident:30s} {image.w} x {image.h}  covered {covered * 100:5.1f} %")
    if args.dry_run:
        print(f"DISC KIT build DRY-RUN PASS ({len(parts)} parts); nothing written")
        return 0
    out.mkdir(parents=True, exist_ok=True)
    records.mkdir(parents=True, exist_ok=True)
    for ident, image in parts.items():
        path = out / f"{ident}.png"
        pngio.write(path, image)
        (records / f"{ident}.json").write_text(json.dumps(record_of(ident, frames, path, image, plate_dir), indent=2) + "\n", encoding="utf-8")
    print(f"DISC KIT build PASS ({len(parts)} parts written to {out})")
    return 0


def upscaled(frame: Image, pixels: tuple[int, int]) -> Image:
    """An original frame at the master size: nearest cells (the 'before' column is the original's own pixels)."""
    pw, ph = pixels
    out = Image(pw, ph, bytearray(pw * ph * 4))
    for py in range(ph):
        cy = min(frame.h - 1, int((py + 0.5) * frame.h / ph))
        for px in range(pw):
            cx = min(frame.w - 1, int((px + 0.5) * frame.w / pw))
            out.data[(py * pw + px) * 4:(py * pw + px) * 4 + 4] = frame.data[(cy * frame.w + cx) * 4:(cy * frame.w + cx) * 4 + 4]
    return out


def faces(frames: Path, plate_dir: Path, parts: dict[str, Image], fams: list[Family], only: set[str] | None = None) -> list[tuple[str, tuple[int, int], list[tuple[str, Image, Image]]]]:
    """Per button: (name, master size, [(state label, the original's frame at master size, the kit's face for the state)])."""
    rows = []
    for fam in fams:
        for m in fam.members:
            if only and m.name not in only:
                continue
            plate = read_plate(plate_dir, m.name, fam)
            if plate is None:
                raise DiscError(f"no plate master plate.{m.name}.png in {plate_dir} (fit it first)")
            idle = button_kit.composite(plate, parts[f"glyph.{m.name}"])
            shots = [("idle", read_frame(frames, fam, m.folder, 0), idle),
                     ("hover", read_frame(frames, fam, m.folder, 2), button_kit.composite(idle, parts[f"plate.{m.name}_hover"])),
                     ("pressed", read_frame(frames, fam, m.folder, 1), button_kit.composite(idle, parts[f"plate.{m.name}_pressed"]))]
            rows.append((m.name, fam.master, [(label, upscaled(before, fam.master), after) for label, before, after in shots]))
    return rows


def cmd_sheet(args: argparse.Namespace) -> int:
    """The before / after composite: per button the original's frames (nearest cells) over the kit's faces (plate + glyph + overlays), the same size."""
    local = args.local_root
    frames = args.frames or local / "census" / "frames"
    plate_dir = args.plate_dir or local / "masters"
    need_magick()
    if args.zoom < 1:
        raise DiscError("--zoom must be at least 1")
    only = set(args.only.split(",")) if args.only else None
    fams = chosen_families(args.family, only)
    parts = build_parts(frames, plate_dir, fams, only)
    work = Path(tempfile.mkdtemp(prefix="disc-sheet-"))
    try:
        rows = []
        cell_w = max(f.master[0] for f in fams) * args.zoom
        cell_h = max(f.master[1] for f in fams) * args.zoom
        for name, size, shots in faces(frames, plate_dir, parts, fams, only):
            columns = []
            for index, (label, before, after) in enumerate(shots):
                pair = []
                for tag, image, nearest in (("before", before, True), ("after", after, False)):
                    path = work / f"{name}-{index}-{tag}.png"
                    pngio.write(path, image)
                    flags = ["-filter", "point" if nearest else "Lanczos", "-resize", f"{size[0] * args.zoom}x{size[1] * args.zoom}!"]
                    subprocess.run(["magick", str(path), "-background", "#202020", "-alpha", "remove", "-alpha", "off", *flags, "-gravity", "center", "-extent", f"{cell_w}x{cell_h}", str(path)], check=True)
                    pair.append(path)
                column = work / f"{name}-{index}-col.png"
                subprocess.run(["magick", *pair, "-append", str(column)], check=True)
                columns.append(column)
            row = work / f"{name}-row.png"
            subprocess.run(["magick", *columns, "+append", "-background", "#202020", "-extent", f"{cell_w * 3}x{cell_h * 2}", str(row)], check=True)
            rows.append(row)
        sheet = work / "sheet.png"
        subprocess.run(["magick", *rows, "-append", str(sheet)], check=True)
        head = work / "head.png"
        subprocess.run(["magick", "-size", f"{cell_w * 3}x40", "xc:#202020", "-font", LABEL_FONT, "-fill", "white", "-pointsize", "22", "-gravity", "west", "-annotate", "+10+0",
                        "ARC-UI-3203, dark-dome buttons: top of each pair = ORIGINAL (nearest cells), bottom = KIT (plate + glyph + overlays); columns: idle, hover, pressed", str(head)], check=True)
        subprocess.run(["magick", str(head), str(sheet), "-append", str(args.out)], check=True)
    finally:
        shutil.rmtree(work, ignore_errors=True)
    print(f"DISC KIT sheet PASS: {args.out}")
    return 0


def self_test() -> int:
    """The unit tests, run as the package module (they import this tool relatively, so not in this process when it runs as a script)."""
    code = subprocess.run([sys.executable, "-B", "-m", "unittest", "tools.steamwoven_ui.test_disc_kit"], cwd=HERE.parents[1]).returncode
    print("DISC KIT self-test " + ("PASS" if code == 0 else "FAIL"))
    return 0 if code == 0 else 1


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("refs", "place", "gloss", "build", "sheet"):
        s = sub.add_parser(name)
        s.add_argument("--family")
        if name == "place":
            s.add_argument("--source", type=Path)
            s.add_argument("--out", type=Path)
        s.add_argument("--frames", type=Path)
        s.add_argument("--local-root", type=Path, default=None)
        if name not in ("refs", "gloss", "place"):
            s.add_argument("--plate-dir", type=Path)
        if name == "gloss":
            s.add_argument("--fitted-dir", type=Path)
            s.add_argument("--ring", type=Path)
            s.add_argument("--out", type=Path)
            s.add_argument("--dry-run", action="store_true")
        if name == "build":
            s.add_argument("--out", type=Path)
            s.add_argument("--only")
            s.add_argument("--dry-run", action="store_true")
        if name == "sheet":
            s.add_argument("--out", type=Path, required=True)
            s.add_argument("--zoom", type=int, default=2)
            s.add_argument("--only")
    sub.add_parser("self-test")
    args = parser.parse_args(argv)
    try:
        if args.command == "self-test":
            return self_test()
        args.local_root = args.local_root or prompt_library.default_local_root()
        return {"refs": cmd_refs, "place": cmd_place, "gloss": cmd_gloss, "build": cmd_build, "sheet": cmd_sheet}[args.command](args)
    except (DiscError, button_kit.ButtonError, pngio.PngError, OSError, subprocess.CalledProcessError) as error:
        print(f"DISC KIT FAIL: {error}")
        return 2


if __name__ == "__main__":
    sys.exit(main())
