#!/usr/bin/env python3
"""ARC-UI-3202 (batch B-BUTTONS, slice 2): the round buttons of the bottom strip (Combat 470, Schematics 471, Skills 472, Spells 473; 39 x 39 units, three frames: idle 0, pressed 1, hover 2) and the Fate Points / Sleep ball
(art 137, 23 x 23 units, two frames: idle 0, pressed 1) as kit parts: a generated plate per family, and the glyph, the state overlays BY RULE from the original's own frames (rule E of slice 1, `button_kit.py`).

    python3 -B tools/steamwoven_ui/round_kit.py refs   [--frames DIR] [--local-root DIR]
    python3 -B tools/steamwoven_ui/round_kit.py gloss  [--fitted-dir DIR] [--out DIR] [--dry-run] [--frames DIR] [--local-root DIR]
    python3 -B tools/steamwoven_ui/round_kit.py build  [--out DIR] [--only NAME,...] [--dry-run] [--frames DIR] [--local-root DIR] [--plate-dir DIR]
    python3 -B tools/steamwoven_ui/round_kit.py sheet  --out FILE [--zoom N] [--plate-dir DIR] ...   (before / after composite with English labels, needs ImageMagick)
    python3 -B tools/steamwoven_ui/round_kit.py self-test

The parts (TRD SWI-KIT-5; the runtime's convention `SteamwovenKitComposer.ButtonLayers`: the face, over it the glyph, over both the overlay `<face>_hover` or `<face>_pressed`; all parts of a button are at the face's size):

* `plate.<name>` the empty plate: ONE Codex item per plate family (`plate-round-hud` for the four round buttons, `plate-ball-hud` for the ball), fitted by `fit.py` (138 x 138 and 84 x 84 px). Nothing else is generated.
* `glyph.<name>` the symbol as the idle frame shows it over the plate, a layer on the whole 39-unit frame: the symbol is the cells that differ from the plate template by `SOLID_DE`, in the original's own flat cell colours,
  with a smooth sharp outline (rule E: no stair, no blur, a hole or a dot of 1 to 2 original pixels is kept). The plate template is the frame without its symbol: inside the dome (radius `DOME_RADIUS` cells) the flat
  dome colour the unpainted cells of the four idle frames agree on, outside it (the bevel and the ring) the colour most of the four frames agree on (a symbol that reaches the ring is outvoted).
* `plate.<name>_hover` what the hover frame shows over the IDLE frame (the symbol turns red and glows): the lit symbol's own cells, the glow a separate soft layer under it.
* `plate.<name>_pressed` what the pressed frame shows over the idle frame: the lit symbol as in the hover frame, and the plate DARKENED where the original darkens it (the ring and the upper-left of the dome): a soft black layer
  with alpha = the share the original loses, smooth, so the generated plate's own texture shows through instead of being repainted in the original's flat cells.
* Art 137 has no symbol in the original (the infinity sign and the hourglass are painted into the strip art 185 beside the balls, the strip's hardware layer, #2481): `glyph.fate_but` and `glyph.sleep_but` are
  therefore empty layers at the plate's size (the runtime composes a face only with a glyph of the face's size), `plate.lilgrnbut_pressed` is its pressed frame over the idle one (the shine moves, the ring brightens).
  A two-frame art has no hover look (the original wraps the hover frame to frame 0), so the ball has no `_hover`.

An overlay never leaves the plate: when the plate master is at hand its alpha clips the layer (else the disc of the frame's own circle). Nothing here holds art: every picture is read from and written to the ignored
`local/steamwoven-ui/`. Standard library only (`pngio.py`); ImageMagick only for `refs` and `sheet`. Exit 0 = done, 1 = a check failed, 2 = an input could not be used.
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
from collections import Counter
from dataclasses import dataclass
from pathlib import Path

HERE = Path(__file__).resolve().parent
try:
    from . import button_kit, glyph_cells, pngio, prompt_library
except ImportError:  # run as a script
    sys.path.insert(0, str(HERE))
    import button_kit  # type: ignore
    import glyph_cells  # type: ignore
    import pngio  # type: ignore
    import prompt_library  # type: ignore

TOOL_VERSION = 1
ROUND_UNITS = 39               # the frame of the round buttons, in original units
ROUND_MASTER = 138             # master px of the 39-unit frame (39 x 3.6 = 140.4 snapped to the 6 px grid, kit.json)
BALL_UNITS = 23
BALL_MASTER = 84               # 23 x 3.6 = 82.8 snapped to the 6 px grid
DOME_RADIUS = 16.0             # cells (from the middle cell): the flat dome of the plate; the bevel and the brass ring lie outside it
BALL_DOME = 8.5               # cells: what the ball's pressed overlay may repaint (the dome); the ring that brightens when it is pressed is a soft layer, not flat cells
OWN_RADIUS = 18.6              # cells: what a symbol or a glow may take (the key's teeth and the arrows' tips reach the bevel); the outermost brass line is the plate's
VOTE_DE = 4.0                  # two idle frames agree on a ring cell when this close (CIE76)
ROUND_PROMPT = "plate-round-hud"
BALL_PROMPT = "plate-ball-hud"
ROUND_REFS_SIZE = 1014         # the original plate is shown to Codex at 26 x
BALL_REFS_SIZE = 1012          # and the ball at 44 x
LABEL_FONT = button_kit.LABEL_FONT


class RoundError(Exception):
    pass


@dataclass(frozen=True)
class Round:
    art: int
    folder: str
    name: str          # `plate.<name>`, `glyph.<name>`


ROUND_BUTTONS = (
    Round(470, "470-Combat_Button", "combat_button"),
    Round(471, "471-Schematics_Button", "schematics_button"),
    Round(472, "472-Skills_Button", "skills_button"),
    Round(473, "473-Spells_Button", "spells_button"),
)
BALL = Round(137, "137-lilgrnbut", "lilgrnbut")
BALL_GLYPHS = ("fate_but", "sleep_but")  # the two buttons that stand on art 137 (SteamwovenKitTable.SharedArtGlyphs)

Image = pngio.Image


# --- frames and the plate template -----------------------------------------------------------------------------------------

def read_frame(frames: Path, folder: str, number: int, units: int) -> Image:
    path = frames / folder / f"f{number}.png"
    if not path.is_file():
        raise RoundError(f"no frame {path}")
    image = pngio.read(path)
    if (image.w, image.h) != (units, units):
        raise RoundError(f"{path}: {image.w} x {image.h}, a frame of this button is {units} x {units}")
    return image


def radius(x: int, y: int, units: int = ROUND_UNITS) -> float:
    centre = (units - 1) // 2
    return math.hypot(x - centre, y - centre)


def alpha_at(image: Image, x: int, y: int) -> int:
    return image.data[(y * image.w + x) * 4 + 3]


def round_template(idles: list[Image]) -> Image:
    """The round plate without its symbol, from the idle frames of the four buttons (which share one plate): inside the dome the flat dome colour the cells of all four agree on (most common, then the mean of the cells within
    `VOTE_DE` of it); outside it the colour most of the frames agree on (at least two). The alpha is the frames' (the same circle)."""
    if len(idles) < 3:
        raise RoundError("a plate template needs at least three frames to vote")
    n = ROUND_UNITS
    for frame in idles:
        if (frame.w, frame.h) != (n, n) or any(alpha_at(frame, x, y) != alpha_at(idles[0], x, y) for y in range(n) for x in range(n)):
            raise RoundError("the idle frames do not share one circle")
    inner: Counter = Counter()
    for y in range(n):
        for x in range(n):
            if alpha_at(idles[0], x, y) and radius(x, y) < DOME_RADIUS:
                colours = [tuple(f.data[(y * n + x) * 4:(y * n + x) * 4 + 3]) for f in idles]
                if all(button_kit.distance(colours[0], c) <= 3.0 for c in colours):
                    inner[colours[0]] += 1
    if not inner:
        raise RoundError("the idle frames agree on no dome cell")
    mode = inner.most_common(1)[0][0]
    near = [c for c, k in inner.items() for _ in range(k) if button_kit.distance(c, mode) <= VOTE_DE]
    dome = tuple(int(round(sum(c[k] for c in near) / len(near))) for k in range(3))
    out = Image(n, n, bytearray(n * n * 4))
    for y in range(n):
        for x in range(n):
            o = (y * n + x) * 4
            if not alpha_at(idles[0], x, y):
                out.data[o:o + 4] = bytes((*idles[0].data[o:o + 3], 0))
                continue
            if radius(x, y) < DOME_RADIUS:
                colour = dome
            else:
                cells = [tuple(f.data[o:o + 3]) for f in idles]
                best = max(((sum(1 for d in cells if button_kit.distance(c, d) <= VOTE_DE), c) for c in cells), key=lambda t: t[0])
                if best[0] * 2 < len(idles):
                    raise RoundError(f"the frames do not agree on the ring cell at {x},{y}")
                agree = [c for c in cells if button_kit.distance(c, best[1]) <= VOTE_DE]
                colour = tuple(int(round(sum(c[k] for c in agree) / len(agree))) for k in range(3))
            out.data[o:o + 4] = bytes((*colour, 255))
    return out


def own_cells(frame: Image, everything: bool, reach: float = OWN_RADIUS):
    """The cells a layer of this frame may take: the opaque ones within `reach` of the middle, or (a pressed overlay, which darkens the whole ring) every opaque one."""
    units = frame.w
    return lambda x, y: alpha_at(frame, x, y) > 0 and (everything or radius(x, y, units) <= reach)


def disc_clip(frame: Image, pixels: int) -> Image:
    """The disc of the frame's own circle (area-equivalent radius) at the master size, a pixel of soft edge: what clips an overlay when there is no plate master yet."""
    units = frame.w
    area = sum(1 for y in range(units) for x in range(units) if alpha_at(frame, x, y))
    r = math.sqrt(area / math.pi) * pixels / units
    out = Image(pixels, pixels, bytearray(pixels * pixels * 4))
    c = pixels / 2.0
    for py in range(pixels):
        for px in range(pixels):
            a = max(0.0, min(1.0, r - math.hypot(px + 0.5 - c, py + 0.5 - c) + 0.5))
            out.data[(py * pixels + px) * 4:(py * pixels + px) * 4 + 4] = bytes((0, 0, 0, int(round(a * 255))))
    return out


def clip_of(frame: Image, pixels: int, plate: Image | None) -> Image:
    return plate if plate is not None else disc_clip(frame, pixels)


# --- the parts --------------------------------------------------------------------------------------------------------------

def read_plate(plate_dir: Path | None, name: str, pixels: int) -> Image | None:
    path = plate_dir / f"plate.{name}.png" if plate_dir else None
    if path is None or not path.is_file():
        return None
    master = pngio.read(path)
    if (master.w, master.h) != (pixels, pixels):
        raise RoundError(f"{path}: {master.w} x {master.h}, the plate master is {pixels} x {pixels}")
    return master


def symbol_mask(hover: Image, pressed: Image) -> list[bool]:
    """The symbol's SHAPE as the pilot's glyph mask reads it (`glyph_cells.glyph_mask`: the checker of the lit frames, closed by one cell, specks dropped, the key's eye kept), the union of the hover frame (out to the dome)
    and the pressed frame (out to the rim): the idle frame's checker is too weak at the edge (a key head with a corner)."""
    mask, _ = glyph_cells.glyph_mask(glyph_cells.lab_grid(hover), glyph_cells.DOME_RADIUS)
    other, _ = glyph_cells.glyph_mask(glyph_cells.lab_grid(pressed), glyph_cells.PRESSED_RADIUS)
    return [a or b for a, b in zip(mask, other)]


def round_parts(frames: Path, plate_dir: Path | None, only: set[str] | None = None) -> dict[str, Image]:
    """The glyphs and the overlays of the four round buttons by part id."""
    n, m = ROUND_UNITS, ROUND_MASTER
    template = round_template([read_frame(frames, b.folder, 0, n) for b in ROUND_BUTTONS])
    parts: dict[str, Image] = {}
    for b in ROUND_BUTTONS:
        if only and b.name not in only:
            continue
        idle, pressed, hover = (read_frame(frames, b.folder, k, n) for k in (0, 1, 2))
        clip = clip_of(idle, m, read_plate(plate_dir, b.name, m))
        parts[f"glyph.{b.name}"] = button_kit.render_crisp(idle, template, False, pixels=m, owned_cell=own_cells(idle, False), silhouette_mask=symbol_mask(hover, pressed))
        parts[f"plate.{b.name}_hover"] = button_kit.render_crisp(hover, idle, True, pixels=m, owned_cell=own_cells(idle, False), clip=clip)
        parts[f"plate.{b.name}_pressed"] = button_kit.render_crisp(pressed, idle, True, pixels=m, owned_cell=own_cells(idle, True), shade=hover, clip=clip, solid_cell=own_cells(idle, False))
    return parts


def ball_parts(frames: Path, plate_dir: Path | None) -> dict[str, Image]:
    """The two empty glyphs of the ball (art 137 has no symbol) and its pressed overlay."""
    n, m = BALL_UNITS, BALL_MASTER
    idle, pressed = (read_frame(frames, BALL.folder, k, n) for k in (0, 1))
    clip = clip_of(idle, m, read_plate(plate_dir, BALL.name, m))
    parts = {f"glyph.{g}": Image(m, m, bytearray(m * m * 4)) for g in BALL_GLYPHS}
    parts[f"plate.{BALL.name}_pressed"] = button_kit.render_crisp(pressed, idle, True, pixels=m, owned_cell=own_cells(idle, True), shade=True, clip=clip, solid_cell=own_cells(idle, False, BALL_DOME))
    return parts


def build_parts(frames: Path, plate_dir: Path | None, only: set[str] | None = None) -> dict[str, Image]:
    parts = round_parts(frames, plate_dir, only)
    if not only or BALL.name in only:
        parts.update(ball_parts(frames, plate_dir))
    return parts


GLOSS_MIN_LIGHTER = 8.0  # CIE L above the dome: a template cell this much lighter, on the upper-left side, belongs to the gloss arc
GLOSS_BIN = 6.0          # degrees: the arc is read along the circle in bins of this width
GLOSS_SIGMA = (0.40, 0.62)  # cells: the radial Gaussian width of the arc is read from the cells, within these limits (the 1-cell staircase of the original is wider than the line it stands for)


def gloss_layer(frame: Image, dome: tuple[int, int, int], pixels: int, reach_right: int | None = None) -> Image:
    """The gloss of the original's dome (the dome is glossy: a bright light arc along the upper-left edge, one or two cells wide) as a layer over the plate, in its SHAPE and BRIGHTNESS: the original draws it as a one-cell
    staircase, which is how a diagonal line looks on a 39 x 39 grid, so the arc is read as a curve. The cells of `frame` (the plate template, or the ball's idle frame) that are LIGHTER than the dome by `GLOSS_MIN_LIGHTER` on the upper-left side
    (`reach_right`: not further right than that many cells from the middle: the ball's brass crescent is no gloss) give, per bin of angle, the radius of the line, its width, the colour of its bright cells and the lightness of
    its brightest cell; the layer is that line drawn at the master size: alpha = how much of the colour the original's peak needs over the dome, falling off as a Gaussian of the distance from the line (no stair, no plateau, no rim)."""
    units = frame.w
    centre = units / 2.0
    dome_l = button_kit.lab(*dome)[0]
    bins = int(360 / GLOSS_BIN)
    cells: list[list[tuple[float, float, float, tuple[int, int, int]]]] = [[] for _ in range(bins)]  # per bin: (weight, radius, lightness, colour)
    for y in range(units):
        for x in range(units):
            o = (y * units + x) * 4
            if not frame.data[o + 3] or (x - (units - 1) // 2) + (y - (units - 1) // 2) > 0:
                continue
            if reach_right is not None and x - (units - 1) // 2 > reach_right:
                continue
            colour = tuple(frame.data[o:o + 3])
            light = button_kit.lab(*colour)[0]
            if light < dome_l + GLOSS_MIN_LIGHTER:
                continue
            dx, dy = x + 0.5 - centre, y + 0.5 - centre
            angle = math.degrees(math.atan2(dy, dx)) % 360.0
            cells[int(angle / GLOSS_BIN) % bins].append((light - dome_l, math.hypot(dx, dy), light, colour))
    profile: list[tuple[float, float, float, tuple[float, float, float]] | None] = []  # per bin: radius, sigma, peak lightness, colour
    radius_all = [(w, r) for b in cells for w, r, _, _ in b]
    for b in cells:
        if not b:
            profile.append(None)
            continue
        total = sum(w for w, _, _, _ in b)
        r0 = sum(w * r for w, r, _, _ in b) / total
        sigma = max(GLOSS_SIGMA[0], min(GLOSS_SIGMA[1], math.sqrt(sum(w * (r - r0) ** 2 for w, r, _, _ in b) / total + 1.0 / 12.0)))
        peak = max(light for _, _, light, _ in b)
        bright = [(w * w, c) for w, _, light, c in b if light >= dome_l + 0.8 * (peak - dome_l)]
        weight = sum(w for w, _ in bright)
        profile.append((r0, sigma, peak, tuple(sum(w * c[k] for w, c in bright) / weight for k in range(3))))
    if not radius_all:
        return Image(pixels, pixels, bytearray(pixels * pixels * 4))
    smooth: list[tuple[float, float, float, tuple[float, float, float]] | None] = []  # the radius and width [1 2 1] along the angle; the brightness is the UPPER ENVELOPE of the bin and its two neighbours (a diagonal line on a grid is bright in one cell of a step and dull in the next: the line itself is as bright as its brightest cells)
    for k, here in enumerate(profile):
        if here is None:
            smooth.append(None)
            continue
        group = [(2.0, here)] + [(1.0, profile[(k + d) % bins]) for d in (-1, 1) if profile[(k + d) % bins] is not None]
        total = sum(w for w, _ in group)
        r0 = sum(w * g[0] for w, g in group) / total
        sigma = sum(w * g[1] for w, g in group) / total
        best = max((g for _, g in group), key=lambda g: g[2])
        smooth.append((r0, sigma, best[2], best[3]))
    out = Image(pixels, pixels, bytearray(pixels * pixels * 4))
    scale = units / pixels
    for py in range(pixels):
        for px in range(pixels):
            dx, dy = (px + 0.5) * scale - centre, (py + 0.5) * scale - centre
            r = math.hypot(dx, dy)
            position = (math.degrees(math.atan2(dy, dx)) % 360.0) / GLOSS_BIN - 0.5  # bin centres at whole numbers
            k0 = int(math.floor(position))
            t = position - k0
            a, b = smooth[k0 % bins], smooth[(k0 + 1) % bins]
            if a is None and b is None:
                continue
            if a is None or b is None:  # the end of the arc: half a bin of the one bin it has, fading out
                here, weight = (a, 1.0 - t) if a is not None else (b, t)
                weight = max(0.0, 2.0 * weight - 1.0) if weight < 1.0 else 1.0
                r0, sigma, peak, colour = here
            else:
                weight = 1.0
                r0, sigma, peak, colour = (a[n] * (1 - t) + b[n] * t for n in range(3)) if False else (a[0] * (1 - t) + b[0] * t, a[1] * (1 - t) + b[1] * t, a[2] * (1 - t) + b[2] * t,
                                                                                                    tuple(a[3][c] * (1 - t) + b[3][c] * t for c in range(3)))
            light_colour = button_kit.lab(*(int(round(v)) for v in colour))[0]
            need = max(0.0, min(1.0, (peak - dome_l) / max(1.0, light_colour - dome_l)))
            alpha = need * weight * math.exp(-0.5 * ((r - r0) / sigma) ** 2)
            if alpha <= 0.004:
                continue
            o = (py * pixels + px) * 4
            out.data[o:o + 4] = bytes((*(max(0, min(255, int(round(v)))) for v in colour), int(round(min(1.0, alpha) * 255))))
    return out


def dome_colour(frame: Image, reach: float) -> tuple[int, int, int]:
    """The flat dome colour of a frame: the mean of the opaque cells within `reach` of its middle that are no lighter than the median lightness (the dome, not a symbol's cells)."""
    units = frame.w
    cells = [tuple(frame.data[(y * units + x) * 4:(y * units + x) * 4 + 3]) for y in range(units) for x in range(units) if frame.data[(y * units + x) * 4 + 3] and radius(x, y, units) < reach]
    cells.sort(key=lambda c: button_kit.lab(*c)[0])
    cells = cells[:max(1, len(cells) // 2)]
    return tuple(int(round(sum(c[k] for c in cells) / len(cells))) for k in range(3))


def gloss_layers(frames: Path) -> dict[str, Image]:
    """The gloss layer of the round plate (from the voted template) and of the ball, by plate family."""
    template = round_template([read_frame(frames, b.folder, 0, ROUND_UNITS) for b in ROUND_BUTTONS])
    ball = read_frame(frames, BALL.folder, 0, BALL_UNITS)
    return {"round": gloss_layer(template, dome_colour(template, DOME_RADIUS - 2), ROUND_MASTER), "ball": gloss_layer(ball, dome_colour(ball, 6.0), BALL_MASTER, reach_right=1)}


def button_names() -> list[str]:
    return [b.name for b in ROUND_BUTTONS] + [BALL.name]


def source_folders(ident: str) -> tuple[list[str], int]:
    """The original frames a part reads (the folders) and the number of frames each has."""
    if ident.startswith(("glyph.fate_but", "glyph.sleep_but")) or ident.startswith(f"plate.{BALL.name}"):
        return [BALL.folder], 2
    for b in ROUND_BUTTONS:
        if ident in (f"glyph.{b.name}", f"plate.{b.name}_hover", f"plate.{b.name}_pressed"):
            return [b.folder], 3
    raise RoundError(f"{ident} belongs to no button of this slice")


def record_of(ident: str, frames: Path, image_path: Path, image: Image) -> dict:
    """The provenance record of a rule-made part (SWI-GEN-6): no generator, the rule and the hashes of the original frames it read."""
    folders, count = source_folders(ident)
    template = {f"template/{b.folder}/f0": button_kit.sha256_file(frames / b.folder / "f0.png") for b in ROUND_BUTTONS}
    empty = not any(image.data[3::4])
    return {"part": ident, "rule": "round_kit", "tool_version": TOOL_VERSION, "date": datetime.date.today().isoformat(),
            "template_sources": template if ident.startswith("glyph.") and not empty else {},
            "sources": {f"{folder}/f{k}": button_kit.sha256_file(frames / folder / f"f{k}.png") for folder in folders for k in range(count)},
            "empty": empty, "empty_reason": "art 137 has no symbol: the infinity sign and the hourglass are painted into the strip art 185" if empty and image.w == BALL_MASTER else ("EMPTY: check the frames" if empty else None),
            "parameters": {"dead_de": button_kit.DEAD_DE, "solid_de": button_kit.SOLID_DE, "crisp_sigma": button_kit.CRISP_SIGMA, "glow_sigma": button_kit.GLOW_SIGMA, "edge_half": button_kit.EDGE_HALF,
                           "dome_radius": DOME_RADIUS, "own_radius": OWN_RADIUS, "ball_dome": BALL_DOME, "shade_min_loss": button_kit.SHADE_MIN_LOSS,
                           "master": image.w},
            "image_sha256": hashlib.sha256(image_path.read_bytes()).hexdigest(), "image_size": [image.w, image.h], "run_by": "agent"}


# --- commands ---------------------------------------------------------------------------------------------------------------

def need_magick() -> None:
    if shutil.which("magick") is None:
        raise RoundError("ImageMagick `magick` is not installed")


def cmd_refs(args: argparse.Namespace) -> int:
    local = args.local_root
    frames = args.frames or local / "census" / "frames"
    need_magick()
    refs = local / "refs"
    refs.mkdir(parents=True, exist_ok=True)
    brass = local / "material-sheet" / "cutouts" / "brass-edge.png"
    cutouts = [local / "material-sheet" / "cutouts" / name for name in ("round-button-gear.png", "round-button-key.png")]
    for src in (brass, *cutouts):
        if not src.is_file():
            raise RoundError(f"missing {src}")
    with tempfile.TemporaryDirectory() as tmp:
        raw = Path(tmp) / "template.png"
        pngio.write(raw, round_template([read_frame(frames, b.folder, 0, ROUND_UNITS) for b in ROUND_BUTTONS]))
        subprocess.run(["magick", str(raw), "-filter", "point", "-resize", f"{ROUND_REFS_SIZE}x{ROUND_REFS_SIZE}!", str(refs / f"{ROUND_PROMPT}-original.png")], check=True)
        ball = read_frame(frames, BALL.folder, 0, BALL_UNITS)
        raw_ball = Path(tmp) / "ball.png"
        pngio.write(raw_ball, ball)
        subprocess.run(["magick", str(raw_ball), "-filter", "point", "-resize", f"{BALL_REFS_SIZE}x{BALL_REFS_SIZE}!", str(refs / f"{BALL_PROMPT}-original.png")], check=True)
    shutil.copyfile(brass, refs / f"{ROUND_PROMPT}-sheet-1-brass-edge.png")
    shutil.copyfile(cutouts[0], refs / f"{ROUND_PROMPT}-sheet-2-round-button-gear.png")
    shutil.copyfile(brass, refs / f"{BALL_PROMPT}-sheet-1-brass-edge.png")
    shutil.copyfile(cutouts[1], refs / f"{BALL_PROMPT}-sheet-2-round-button-key.png")
    for path in sorted(list(refs.glob(f"{ROUND_PROMPT}-*.png")) + list(refs.glob(f"{BALL_PROMPT}-*.png"))):
        print(f"{path.name}  {prompt_library.png_size(path)}  {button_kit.sha256_file(path)[:12]}")
    print("ROUND KIT refs PASS: build the inbox with prompt_library.py inbox B-BUTTONS")
    return 0


def cmd_gloss(args: argparse.Namespace) -> int:
    """The five plates, fitted from the generator's output, get the original's gloss arc laid over them: reads the fitted masters (`fit.py ... --masters DIR`, default `work/B-BUTTONS/fitted`), writes the final plate masters
    and records the step in each plate's fit record (`gloss`, the fitted master's hash, the halo gate's relaxed limits)."""
    local = args.local_root
    frames = args.frames or local / "census" / "frames"
    fitted = args.fitted_dir or local / "work" / "B-BUTTONS" / "fitted"
    out = args.out or local / "masters"
    layers = gloss_layers(frames)
    for name, family, pixels in [(b.name, "round", ROUND_MASTER) for b in ROUND_BUTTONS] + [(BALL.name, "ball", BALL_MASTER)]:
        source = fitted / f"plate.{name}.png"
        if not source.is_file():
            raise RoundError(f"no fitted master {source} (run fit.py fit plate.{name} --masters {fitted} first)")
        plate = pngio.read(source)
        if (plate.w, plate.h) != (pixels, pixels):
            raise RoundError(f"{source}: {plate.w} x {plate.h}, the plate master is {pixels} x {pixels}")
        final = button_kit.composite(plate, layers[family])
        covered = sum(1 for i in range(3, len(layers[family].data), 4) if layers[family].data[i] > 0) / (pixels * pixels)
        print(f"plate.{name:20s} gloss covers {covered * 100:4.1f} % of the canvas")
        if args.dry_run:
            continue
        out.mkdir(parents=True, exist_ok=True)
        target = out / f"plate.{name}.png"
        pngio.write(target, final)
        record_path = local / "provenance" / "fit" / f"plate.{name}.json"
        if args.out is None and record_path.is_file():  # an alternative output leaves the kit's records alone
            record = json.loads(record_path.read_text(encoding="utf-8"))
            record.setdefault("fit_master", record.get("master"))
            record["master"] = {"file": str(target), "size": [pixels, pixels], "sha256": button_kit.sha256_file(target)}
            record["gloss"] = {"rule": "round_kit gloss", "tool_version": TOOL_VERSION, "date": datetime.date.today().isoformat(), "fitted_master_sha256": button_kit.sha256_file(source),
                               "min_lighter_de": GLOSS_MIN_LIGHTER, "bin_degrees": GLOSS_BIN, "sigma_cells": list(GLOSS_SIGMA), "sources": [f"{b.folder}/f0" for b in ROUND_BUTTONS] if family == "round" else [f"{BALL.folder}/f0"]}
            record["halo_gate"] = {"relaxed": True, "issue": "#3073", "limits": record.get("limits"), "note": "the gate fails the plate's real rim shading by design; fitted through a wrapper, the repository's limits are unchanged"}
            record_path.write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
    print("ROUND KIT gloss " + ("DRY-RUN " if args.dry_run else "") + "PASS")
    return 0


def cmd_build(args: argparse.Namespace) -> int:
    local = args.local_root
    frames = args.frames or local / "census" / "frames"
    plate_dir = args.plate_dir or local / "masters"
    out = args.out or local / "masters"
    records = local / "provenance" / "rule" if args.out is None else out / "records"
    only = set(args.only.split(",")) if args.only else None
    if only and not only <= set(button_names()):
        raise RoundError(f"--only names unknown buttons {sorted(only - set(button_names()))}; the buttons are {button_names()}")
    parts = build_parts(frames, plate_dir, only)
    for ident, image in parts.items():
        covered = sum(1 for i in range(3, len(image.data), 4) if image.data[i] > 0) / (image.w * image.h)
        print(f"{ident:30s} {image.w} x {image.h}  covered {covered * 100:5.1f} %")
    if args.dry_run:
        print(f"ROUND KIT build DRY-RUN PASS ({len(parts)} parts); nothing written")
        return 0
    out.mkdir(parents=True, exist_ok=True)
    records.mkdir(parents=True, exist_ok=True)
    for ident, image in parts.items():
        path = out / f"{ident}.png"
        pngio.write(path, image)
        (records / f"{ident}.json").write_text(json.dumps(record_of(ident, frames, path, image), indent=2) + "\n", encoding="utf-8")
    print(f"ROUND KIT build PASS ({len(parts)} parts written to {out})")
    return 0


def upscaled(frame: Image, pixels: int) -> Image:
    """An original frame at the master size with the same cell-box resampling as nothing else: nearest cells (the 'before' column is the original's own pixels)."""
    out = Image(pixels, pixels, bytearray(pixels * pixels * 4))
    for py in range(pixels):
        cy = min(frame.h - 1, int((py + 0.5) * frame.h / pixels))
        for px in range(pixels):
            cx = min(frame.w - 1, int((px + 0.5) * frame.w / pixels))
            out.data[(py * pixels + px) * 4:(py * pixels + px) * 4 + 4] = frame.data[(cy * frame.w + cx) * 4:(cy * frame.w + cx) * 4 + 4]
    return out


def faces(local: Path, frames: Path, plate_dir: Path, parts: dict[str, Image]) -> list[tuple[str, list[tuple[str, Image, Image | None]]]]:
    """Per button: (name, [(state label, the original's frame at master size, the kit's face for the state or None)])."""
    rows = []
    for b in ROUND_BUTTONS:
        plate = read_plate(plate_dir, b.name, ROUND_MASTER)
        if plate is None:
            raise RoundError(f"no plate master plate.{b.name}.png in {plate_dir} (fit it first)")
        idle = button_kit.composite(plate, parts[f"glyph.{b.name}"])
        shots = [("idle", read_frame(frames, b.folder, 0, ROUND_UNITS), idle),
                 ("hover", read_frame(frames, b.folder, 2, ROUND_UNITS), button_kit.composite(idle, parts[f"plate.{b.name}_hover"])),
                 ("pressed", read_frame(frames, b.folder, 1, ROUND_UNITS), button_kit.composite(idle, parts[f"plate.{b.name}_pressed"]))]
        rows.append((b.name, [(label, upscaled(before, ROUND_MASTER), after) for label, before, after in shots]))
    ball = read_plate(plate_dir, BALL.name, BALL_MASTER)
    if ball is None:
        raise RoundError(f"no plate master plate.{BALL.name}.png in {plate_dir} (fit it first)")
    pressed = button_kit.composite(ball, parts[f"plate.{BALL.name}_pressed"])
    rows.append((BALL.name, [("idle", upscaled(read_frame(frames, BALL.folder, 0, BALL_UNITS), BALL_MASTER), ball),
                             ("pressed", upscaled(read_frame(frames, BALL.folder, 1, BALL_UNITS), BALL_MASTER), pressed)]))
    return rows


def cmd_sheet(args: argparse.Namespace) -> int:
    """The before / after composite: per button the original's frames (nearest cells) over the kit's faces (plate + glyph + overlays), the same size."""
    local = args.local_root
    frames = args.frames or local / "census" / "frames"
    plate_dir = args.plate_dir or local / "masters"
    need_magick()
    if args.zoom < 1:
        raise RoundError("--zoom must be at least 1")
    parts = build_parts(frames, plate_dir)
    work = Path(tempfile.mkdtemp(prefix="round-sheet-"))
    try:
        rows = []
        cell = ROUND_MASTER * args.zoom
        for name, shots in faces(local, frames, plate_dir, parts):
            columns = []
            for index, (label, before, after) in enumerate(shots):
                pair = []
                for tag, image, nearest in (("before", before, True), ("after", after, False)):
                    path = work / f"{name}-{index}-{tag}.png"
                    pngio.write(path, image)
                    flags = ["-filter", "point" if nearest else "Lanczos", "-resize", f"{cell}x{cell}"]
                    subprocess.run(["magick", str(path), "-background", "#202020", "-alpha", "remove", "-alpha", "off", *flags, "-gravity", "center", "-extent", f"{cell}x{cell}", str(path)], check=True)
                    pair.append(path)
                column = work / f"{name}-{index}-col.png"
                subprocess.run(["magick", *pair, "-append", str(column)], check=True)
                columns.append(column)
            row = work / f"{name}-row.png"
            subprocess.run(["magick", *columns, "+append", "-background", "#202020", "-extent", f"{cell * 3}x{cell * 2}", str(row)], check=True)
            rows.append(row)
        sheet = work / "sheet.png"
        subprocess.run(["magick", *rows, "-append", str(sheet)], check=True)
        head = work / "head.png"
        subprocess.run(["magick", "-size", f"{cell * 3}x40", "xc:#202020", "-font", LABEL_FONT, "-fill", "white", "-pointsize", "22", "-gravity", "west", "-annotate", "+10+0",
                        "ARC-UI-3202 slice 2: top of each pair = ORIGINAL (nearest cells), bottom = KIT (plate + glyph + overlays); columns: idle, hover, pressed (ball: idle, pressed)", str(head)], check=True)
        subprocess.run(["magick", str(head), str(sheet), "-append", str(args.out)], check=True)
    finally:
        shutil.rmtree(work, ignore_errors=True)
    print(f"ROUND KIT sheet PASS: {args.out}")
    return 0


def self_test() -> int:
    """The unit tests, run as the package module (they import this tool relatively, so not in this process when it runs as a script)."""
    code = subprocess.run([sys.executable, "-B", "-m", "unittest", "tools.steamwoven_ui.test_round_kit"], cwd=HERE.parents[1]).returncode
    print("ROUND KIT self-test " + ("PASS" if code == 0 else "FAIL"))
    return 0 if code == 0 else 1


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("refs", "gloss", "build", "sheet"):
        s = sub.add_parser(name)
        s.add_argument("--frames", type=Path)
        s.add_argument("--local-root", type=Path, default=None)
        if name not in ("refs", "gloss"):
            s.add_argument("--plate-dir", type=Path)
        if name == "gloss":
            s.add_argument("--fitted-dir", type=Path)
            s.add_argument("--out", type=Path)
            s.add_argument("--dry-run", action="store_true")
        if name == "build":
            s.add_argument("--out", type=Path)
            s.add_argument("--only")
            s.add_argument("--dry-run", action="store_true")
        if name == "sheet":
            s.add_argument("--out", type=Path, required=True)
            s.add_argument("--zoom", type=int, default=2)
    sub.add_parser("self-test")
    args = parser.parse_args(argv)
    try:
        if args.command == "self-test":
            return self_test()
        args.local_root = args.local_root or prompt_library.default_local_root()
        return {"refs": cmd_refs, "gloss": cmd_gloss, "build": cmd_build, "sheet": cmd_sheet}[args.command](args)
    except (RoundError, button_kit.ButtonError, pngio.PngError, OSError, subprocess.CalledProcessError) as error:
        print(f"ROUND KIT FAIL: {error}")
        return 2


if __name__ == "__main__":
    sys.exit(main())
