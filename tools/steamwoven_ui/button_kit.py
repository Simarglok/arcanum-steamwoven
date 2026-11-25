#!/usr/bin/env python3
"""ARC-UI-2482 (batch B-BUTTONS, slice 1): the square HUD buttons of the top strip as kit parts: a generated plate, and the glyph, the state overlays and the lit faces BY RULE from the original's own frames.

    python3 -B tools/steamwoven_ui/button_kit.py refs   [--frames DIR] [--local-root DIR]
    python3 -B tools/steamwoven_ui/button_kit.py flatten [--source PNG] [--out PNG] [--local-root DIR]
    python3 -B tools/steamwoven_ui/button_kit.py build  [--look crisp|sharp|cells|smooth] [--out DIR] [--only NAME,...] [--dry-run] [--frames DIR] [--local-root DIR] [--plate-dir DIR]
    python3 -B tools/steamwoven_ui/button_kit.py sheet  --out FILE [--look crisp|sharp|cells|smooth] ...        (before / after composite with English labels, needs ImageMagick)
    python3 -B tools/steamwoven_ui/button_kit.py self-test

The five buttons (Character 169, Inventory 186, Logbook 187, Map of the town 193, World map 194) are 34 x 34 unit squares: the same frame (brass lines, a dark step, a flat olive inset) with a
painted symbol in it, three frames each (idle 0, pressed 1, hover 2) and a lit art each (561, 559, 560, 558, 195: frame 0 idle and hover, frame 1 pressed; Character has one). Their kit parts
(TRD SWI-KIT-5; the runtime's convention `SteamwovenKitComposer.ButtonLayers`: the face, over it the glyph, over both the overlay `<face>_hover` or `<face>_pressed`):

* `plate.<name>` the empty plate: ONE Codex item (`plate-square-hud` of the prompt library, batch B-BUTTONS), fitted by `fit.py` to the five plate parts. Nothing else of these buttons is generated.
* `glyph.<name>` the symbol, a layer over the plate: what the idle frame shows over the plate's own pixels (the plate template, below). Symbol, size and place are the original's (owner decision #3098: the
  glyph's shape is taken from the original, not redrawn); the canvas is the whole 34-unit frame, so the symbol lies where the original's does. `glyph.<name>_lit` is the same for the lit art's frame 0
  (the red symbol and its glow), the glyph the runtime draws over a lit face.
* `plate.<name>_hover`, `plate.<name>_pressed` overlays: what the hover or pressed frame shows over the IDLE frame (the symbol turns red and glows, the pressed inset darkens), so the overlay over plate + glyph is
  the original's frame (the symbol's cells) with its glow.
* `state.<x>_on` the lit face: the plate again (the lit art has the same frame; the symbol is `glyph.<name>_lit`), `state.<x>_on_pressed` the overlay of the lit art's frame 1 over its frame 0 (the four arts that have one).

A layer is a frame laid over its base frame, cell by cell. The SYMBOL is the cells that differ from the base by `SOLID_DE` (CIE76) or more, in the original's own flat cell colours: so every inner line, dot, fold and
continent survives as it is drawn; the OUTLINE is a smooth sharp curve (a small Gaussian of the mask and a one-pixel soft threshold, `crisp`: the pilot's symbol rule #3058 v4, owner pick 2026-11-25: no stair, no blur, a hole or a dot
of 1 to 2 original pixels is kept). The cells that differ a little (the glow of a lit symbol, the darkening of a pressed inset) are a separate soft layer UNDER the symbol (the idle symbol has none; cells under `DEAD_DE`
are the base). Laid over its base a layer gives the frame's cells back (the symbol's cells exactly, the glow to within the soft layer). The older blurred looks (`sharp`, `cells`, `smooth`: a decomposition of the frame into an
alpha and a colour, resampled by a Gaussian of the whole cell field) stay as `--look` choices for comparison; the owner rejected them. The plate template is the frame without its symbol: outside the inset the colour most of the five
frames agree on, inside it the flat inset colour. Nothing here holds art: every picture is read from and written to the ignored `local/steamwoven-ui/`. Standard library only (`pngio.py`); ImageMagick only for `refs` and `sheet`.
Exit 0 = done, 1 = a check failed, 2 = an input could not be used.
"""
from __future__ import annotations

import argparse
import datetime
import functools
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
    from . import pngio, prompt_library, tone_check
except ImportError:  # run as a script
    sys.path.insert(0, str(HERE))
    import pngio  # type: ignore
    import prompt_library  # type: ignore
    import tone_check  # type: ignore

TOOL_VERSION = 1
UNITS = 34                    # the frame of the square HUD buttons, in original units
INSET = (5, 4, 29, 30)        # x0, y0, x1, y1 (inclusive) of the flat inset inside the frame lines (row 4 is inset colour: a symbol may reach it)
MASTER = 120                  # master px of the 34-unit frame (34 x 3.6 = 122.4 snapped to the 6 px grid, kit.json)
DEAD_DE = 3.0                 # a cell this close to its base is the base (the layer is clear): palette noise
FULL_DE = 14.0                # a cell this far from its base is fully the layer
LOOKS = ("crisp", "sharp", "cells", "smooth")  # `crisp` is the pilot's symbol rule (owner pick 2026-11-25); the three blurred looks are kept as the fallback (`sharp`) and the rejected alternatives
SIGMA = {"sharp": (0.35, 0.18), "cells": (0.55, 0.30), "smooth": (0.65, 0.65)}  # (alpha, colour) Gaussian of the cell field, in cells
RIM = 2                       # `crisp`: the outermost two cell rows and columns of the 34-unit frame are the plate's frame lines
SOLID_DE = 9.0                # `crisp`: a cell this far from its base is part of the symbol (its own colour, a sharp curve); closer ones (a glow, a darkening) are the soft layer under it
CRISP_SIGMA = 0.40            # `crisp`: the Gaussian of a class mask, in cells: small enough that a hole or a dot of one original pixel survives (a 1-cell hole blurs to 0.38 < 0.5, a 1-cell dot to 0.62)
GLOW_SIGMA = 0.90             # `crisp`: the Gaussian of the soft layer, in cells
HALO_MIN_CONTRAST = 60.0      # `crisp` halo: only a glyph layer whose 90th-percentile contrast is this high (the lit symbols, the globe) has a soft edge to fall off
CORE_SHARE = 0.5              # `crisp` halo: the core starts at this share of the layer's 90th-percentile contrast, at most `CORE_DE`
CORE_DE = 45.0                # `crisp` halo: a cell this far from its base is the symbol's core; the cells between SOLID_DE and CORE_DE OUTSIDE the core (the original's soft edge, the glow of a lit symbol) fade out smoothly
DARK_DL = 8.0                 # `crisp` halo: a cell that differs by SOLID_DE or more AND is this much darker (CIE L) than its base is the symbol's dark outline, a core cell (a halo only ever lightens or tints)
HALO_REACH = 3                # `crisp` halo: only cells within this many cells of the core are the halo
HALO_SIGMA = 1.10             # `crisp` halo: the Gaussian of the halo layer, in cells (wider than the glow of an overlay: no plateau, no visible rim)
EDGE_HALF = 0.14              # `crisp`: half width of the silhouette's anti-aliasing step on the blurred mask (about one output pixel at the master scale)
SOLID = 0.85                  # alpha from which a cell counts as solid for the sharpened silhouette
EDGE = (0.30, 0.70)           # the soft threshold of the blurred solid mask
PLATE_PROMPT = "plate-square-hud"
SHADOW = (2, 0, 0)            # the colour of the frame's dark lines (the original's 020000): the notched corners of the generated plate take it
LABEL_FONT = "/System/Library/Fonts/Supplemental/Arial.ttf"  # the labels of a composite are English (project rule); the font file is named so ImageMagick needs no font registry
REFS_SIZE = 1020              # the original plate is shown to Codex at 30 x (1020 px), the size the prompt asks


class ButtonError(Exception):
    pass


@dataclass(frozen=True)
class Button:
    art: int
    folder: str
    name: str          # `plate.<name>`, `glyph.<name>`
    on_art: int
    on_folder: str
    on_name: str       # `state.<on_name>` (the kit manifest's ids of the lit faces)


BUTTONS = (
    Button(169, "169-Char_But", "char_but", 561, "561-Char_ON", "char_on"),
    Button(186, "186-Invn_But", "invn_but", 559, "559-Invn_ON", "invn_on"),
    Button(187, "187-Log_But", "log_but", 560, "560-Log_ON", "log_on"),
    Button(193, "193-TMap_But", "tmap_but", 558, "558-TMap_ON", "tmap_on"),
    Button(194, "194-WMap_But", "wmap_but", 195, "195-WMap_ON", "wmap_on"),
)

Image = pngio.Image


# --- cells --------------------------------------------------------------------------------------------------------------

_LAB: dict[tuple[int, int, int], tuple[float, float, float]] = {}


def lab(r: int, g: int, b: int) -> tuple[float, float, float]:
    key = (r, g, b)
    if key not in _LAB:
        _LAB[key] = tone_check._pixel_lab(r, g, b)
    return _LAB[key]


def distance(a: tuple[int, int, int], b: tuple[int, int, int]) -> float:
    la, lb = lab(*a), lab(*b)
    return math.sqrt(sum((x - y) ** 2 for x, y in zip(la, lb)))


def cells_of(image: Image) -> list[tuple[int, int, int]]:
    return [(image.data[i], image.data[i + 1], image.data[i + 2]) for i in range(0, len(image.data), 4)]


def read_frame(frames: Path, folder: str, number: int) -> Image:
    path = frames / folder / f"f{number}.png"
    if not path.is_file():
        raise ButtonError(f"no frame {path}")
    image = pngio.read(path)
    if (image.w, image.h) != (UNITS, UNITS):
        raise ButtonError(f"{path}: {image.w} x {image.h}, a frame of these buttons is {UNITS} x {UNITS}")
    return image


def inside_inset(x: int, y: int) -> bool:
    return INSET[0] <= x <= INSET[2] and INSET[1] <= y <= INSET[3]


def plate_template(frames: list[Image]) -> Image:
    """The frame without its symbol: outside the inset the colour most of the given frames agree on (a symbol that touches the frame lines is outvoted), inside it the inset's most common colour."""
    if len(frames) < 3:
        raise ButtonError("a plate template needs at least three frames to vote")
    inner: Counter = Counter()
    for frame in frames:
        for y in range(UNITS):
            for x in range(UNITS):
                if inside_inset(x, y):
                    inner[tuple(frame.data[(y * UNITS + x) * 4:(y * UNITS + x) * 4 + 3])] += 1
    base = inner.most_common(1)[0][0]
    out = Image(UNITS, UNITS, bytearray(UNITS * UNITS * 4))
    for y in range(UNITS):
        for x in range(UNITS):
            o = (y * UNITS + x) * 4
            if inside_inset(x, y):
                colour = base
            else:
                votes = Counter(tuple(f.data[o:o + 3]) for f in frames)
                colour, count = votes.most_common(1)[0]
                if count * 2 <= len(frames):
                    raise ButtonError(f"the frames do not agree on the frame line at {x},{y}")
            out.data[o:o + 4] = bytes((*colour, 255))
    return out


def decompose(frame: Image, base: Image) -> tuple[list[tuple[int, int, int]], list[float]]:
    """The layer that, laid over `base`, gives `frame`: per cell a colour and an alpha (see the module text)."""
    colours, alphas = [], []
    for i in range(0, len(frame.data), 4):
        p, b = tuple(frame.data[i:i + 3]), tuple(base.data[i:i + 3])
        d = distance(p, b)
        if d <= DEAD_DE:
            colours.append(b)
            alphas.append(0.0)
        elif d >= FULL_DE:
            colours.append(p)
            alphas.append(1.0)
        else:
            a = d / FULL_DE
            colours.append(tuple(max(0, min(255, int(round(bc + (pc - bc) / a)))) for pc, bc in zip(p, b)))
            alphas.append(a)
    return colours, alphas


# --- the exact Gaussian resampling of a cell field ------------------------------------------------------------------------

def _phi(x: float) -> float:
    return 0.5 * (1.0 + math.erf(x / math.sqrt(2.0)))


@functools.lru_cache(maxsize=None)
def axis_weights(cells: int, pixels: int, sigma: float) -> list[tuple[int, list[float]]]:
    """For each output pixel the first cell and the weights of the cells a Gaussian of `sigma` cells gives it (a cell is a box, the field beyond the frame repeats its edge cell)."""
    result = []
    for x in range(pixels):
        u = (x + 0.5) * cells / pixels
        lo, hi = int(math.floor(u - 3 * sigma - 1)), int(math.ceil(u + 3 * sigma + 1))
        weights: dict[int, float] = {}
        for i in range(lo, hi + 1):
            w = _phi((i + 1 - u) / sigma) - _phi((i - u) / sigma)
            j = min(cells - 1, max(0, i))
            weights[j] = weights.get(j, 0.0) + w
        first, total = min(weights), sum(weights.values())  # the tail beyond three sigma is cut, so the weights are normalized
        result.append((first, [weights.get(j, 0.0) / total for j in range(first, max(weights) + 1)]))
    return result


def resample_plane(plane: list[float], cells: int, pixels: int, sigma: float) -> list[float]:
    wts = axis_weights(cells, pixels, sigma)
    tmp = [0.0] * (cells * pixels)  # rows of cells, columns of pixels
    for y in range(cells):
        row = plane[y * cells:(y + 1) * cells]
        for x, (first, ws) in enumerate(wts):
            tmp[y * pixels + x] = sum(w * row[first + k] for k, w in enumerate(ws))
    out = [0.0] * (pixels * pixels)
    for x in range(pixels):
        column = [tmp[y * pixels + x] for y in range(cells)]
        for y, (first, ws) in enumerate(wts):
            out[y * pixels + x] = sum(w * column[first + k] for k, w in enumerate(ws))
    return out


def smoothstep(edge0: float, edge1: float, x: float) -> float:
    t = min(1.0, max(0.0, (x - edge0) / (edge1 - edge0)))
    return t * t * (3 - 2 * t)


def render_layer(colours: list[tuple[int, int, int]], alphas: list[float], look: str, pixels: int = MASTER) -> Image:
    """A layer's cells as a master canvas (RGBA, straight alpha). `solid` cells keep a sharp but smooth silhouette (a Gaussian of the solid mask, a soft threshold: no stair of original pixels); partial
    alphas (a glow, a shadow) keep their values; the colour is the alpha-weighted Gaussian mean of the cell colours."""
    if look not in SIGMA:
        raise ButtonError(f"look must be one of {sorted(SIGMA)}")
    sigma_a, sigma_c = SIGMA[look]
    solid = resample_plane([1.0 if a >= SOLID else 0.0 for a in alphas], UNITS, pixels, sigma_a)
    partial = resample_plane(alphas, UNITS, pixels, sigma_a)
    weight = resample_plane(alphas, UNITS, pixels, sigma_c)
    channels = [resample_plane([c[k] * a for c, a in zip(colours, alphas)], UNITS, pixels, sigma_c) for k in range(3)]
    out = Image(pixels, pixels, bytearray(pixels * pixels * 4))
    for i in range(pixels * pixels):
        alpha = max(smoothstep(*EDGE, solid[i]), min(1.0, partial[i]))
        if alpha <= 0.002 or weight[i] <= 1e-6:
            continue
        o = i * 4
        out.data[o:o + 3] = bytes(max(0, min(255, int(round(channels[k][i] / weight[i])))) for k in range(3))
        out.data[o + 3] = int(round(alpha * 255))
    return out


def outside_cells(core: list[bool]) -> list[bool]:
    """The cells a 4-connected walk from the frame's border reaches without crossing a core cell: what lies OUTSIDE the symbol (a closed outline keeps its inside out of the set)."""
    out = [False] * (UNITS * UNITS)
    stack = [i for i in range(UNITS * UNITS) if (i % UNITS in (0, UNITS - 1) or i // UNITS in (0, UNITS - 1)) and not core[i]]
    for i in stack:
        out[i] = True
    while stack:
        i = stack.pop()
        x, y = i % UNITS, i // UNITS
        for xx, yy in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
            if 0 <= xx < UNITS and 0 <= yy < UNITS and not out[yy * UNITS + xx] and not core[yy * UNITS + xx]:
                out[yy * UNITS + xx] = True
                stack.append(yy * UNITS + xx)
    return out


def render_crisp(frame: Image, base: Image, glow: bool, rim: bool = False, pixels: int = MASTER, halo: bool = False) -> Image:
    """The pilot's symbol rule (#3058, v4, `glyph_cells.py`) for a layer of `frame` over `base`: the SYMBOL is the cells that differ from the base by `SOLID_DE` or more. Their colours stay the original's flat
    cells (so every inner line, dot, fold and continent of the original survives as it is drawn); the OUTLINE is a smooth sharp curve: a small Gaussian of the symbol's mask and a one-pixel soft threshold
    (no stair of original pixels, no blur; a hole or a dot of one original pixel survives: a 1-cell hole blurs to 0.38 < 0.5). With `glow` the cells that differ a little (the glow of a lit symbol, the darkening of
    a pressed inset) become a separate soft layer UNDER the symbol (a wide Gaussian, the colour that lays the base's colour back to the frame's); without it they are dropped (the idle symbol has no glow).
    With `halo` (the glyph layers of high contrast) the symbol is only the CORE (`CORE_DE` or more, plus whatever the core encloses) and the cells outside it within `HALO_REACH` cells that differ by more than `DEAD_DE` are a soft layer
    under it (alpha = the distance from the base over the core threshold, `CORE_SHARE` of the layer's 90th-percentile contrast but at most `CORE_DE`, the colour that lays the base's colour back to the frame's): the original's soft edge (the globe's shadow ring, the glow of a lit symbol) falls off outward with no plateau
    and no rim."""
    n = UNITS * UNITS
    p_cells, b_cells = cells_of(frame), cells_of(base)
    dist = [distance(p, b) for p, b in zip(p_cells, b_cells)]
    owned = [rim or (RIM <= i % UNITS < UNITS - RIM and RIM <= i // UNITS < UNITS - RIM) for i in range(n)]  # the outermost frame lines are the plate's, never the symbol's or its glow (unless `rim`)
    halo_cells: set[int] = set()
    spread = sorted(d for i, d in enumerate(dist) if d >= SOLID_DE and owned[i])
    contrast = spread[int(len(spread) * 0.9)] if spread else 0.0  # the layer's own contrast: an orange idle symbol reaches 44, a lit red one 86, the globe 131
    halo = halo and contrast >= HALO_MIN_CONTRAST  # a low-contrast layer (the orange idle symbols) has no soft edge to fall off: its cells are the symbol
    if halo:
        core_de = max(SOLID_DE, min(CORE_DE, CORE_SHARE * contrast))
        core = [(d >= core_de or (d >= SOLID_DE and lab(*p_cells[i])[0] < lab(*b_cells[i])[0] - DARK_DL)) and owned[i] for i, d in enumerate(dist)]
        outside = outside_cells(core)
        solid = [core[i] or (not outside[i] and dist[i] >= SOLID_DE and owned[i]) for i in range(n)]
        for i in range(n):
            if solid[i] or not outside[i] or dist[i] <= DEAD_DE or not owned[i]:
                continue
            x, y = i % UNITS, i // UNITS
            if any(core[yy * UNITS + xx] for yy in range(max(0, y - HALO_REACH), min(UNITS, y + HALO_REACH + 1)) for xx in range(max(0, x - HALO_REACH), min(UNITS, x + HALO_REACH + 1))):
                halo_cells.add(i)
    else:
        solid = [d >= SOLID_DE and owned[i] for i, d in enumerate(dist)]
    # the colour of a cell: its own when solid; a cell the smooth outline grows into takes the mean of the solid cells around it (within two cells)
    cell_colour: list[tuple[float, float, float] | None] = [None] * n
    for i in range(n):
        if solid[i]:
            cell_colour[i] = tuple(float(v) for v in p_cells[i])
            continue
        x, y = i % UNITS, i // UNITS
        for reach in (1, 2):
            near = [p_cells[yy * UNITS + xx] for yy in range(max(0, y - reach), min(UNITS, y + reach + 1)) for xx in range(max(0, x - reach), min(UNITS, x + reach + 1)) if solid[yy * UNITS + xx]]
            if near:
                cell_colour[i] = tuple(sum(c[k] for c in near) / len(near) for k in range(3))
                break
    silhouette = resample_plane([1.0 if x else 0.0 for x in solid], UNITS, pixels, CRISP_SIGMA)
    ga = [0.0] * (pixels * pixels)
    gc = [[0.0] * (pixels * pixels) for _ in range(3)]
    sigma = HALO_SIGMA if halo else GLOW_SIGMA
    if glow or halo:
        alphas, colours = [0.0] * n, [(0, 0, 0)] * n
        for i in range(n):
            if solid[i] or not owned[i]:
                continue
            if i in halo_cells:
                a = min(1.0, dist[i] / core_de)  # linear in the distance from the base all the way to the core: no saturation, so no plateau and no rim
                alphas[i] = a
                colours[i] = tuple(max(0, min(255, int(round(bc + (pc - bc) / a)))) for pc, bc in zip(p_cells[i], b_cells[i]))
            elif glow and dist[i] > DEAD_DE and dist[i] < SOLID_DE:
                a = min(1.0, dist[i] / FULL_DE)
                alphas[i] = a
                colours[i] = tuple(max(0, min(255, int(round(bc + (pc - bc) / a)))) for pc, bc in zip(p_cells[i], b_cells[i]))
        ga = resample_plane(alphas, UNITS, pixels, sigma)
        gc = [resample_plane([c[k] * a for c, a in zip(colours, alphas)], UNITS, pixels, sigma) for k in range(3)]
    out = Image(pixels, pixels, bytearray(pixels * pixels * 4))
    for py in range(pixels):
        cy = min(UNITS - 1, int((py + 0.5) * UNITS / pixels))
        for px in range(pixels):
            i = py * pixels + px
            cell = cell_colour[cy * UNITS + min(UNITS - 1, int((px + 0.5) * UNITS / pixels))]
            ac = smoothstep(0.5 - EDGE_HALF, 0.5 + EDGE_HALF, silhouette[i]) if cell is not None else 0.0
            g = min(1.0, ga[i])
            oa = ac + g * (1.0 - ac)
            if oa <= 0.002:
                continue
            crisp = cell if cell is not None else (0.0, 0.0, 0.0)
            soft = tuple(gc[k][i] / ga[i] for k in range(3)) if ga[i] > 1e-9 else (0.0, 0.0, 0.0)
            o = i * 4
            out.data[o:o + 3] = bytes(max(0, min(255, int(round((crisp[k] * ac + soft[k] * g * (1.0 - ac)) / oa)))) for k in range(3))
            out.data[o + 3] = int(round(min(1.0, oa) * 255))
    return out


def composite(base: Image, over: Image) -> Image:
    """`over` laid over `base` (straight alpha, source-over); the same size."""
    if (base.w, base.h) != (over.w, over.h):
        raise ButtonError("composite: the two pictures differ in size")
    out = Image(base.w, base.h, bytearray(base.data))
    for i in range(0, len(base.data), 4):
        a = over.data[i + 3] / 255.0
        if a <= 0.0:
            continue
        ba = base.data[i + 3] / 255.0
        oa = a + ba * (1.0 - a)
        for k in range(3):
            out.data[i + k] = int(round((over.data[i + k] * a + base.data[i + k] * ba * (1.0 - a)) / oa)) if oa > 0 else 0
        out.data[i + 3] = int(round(oa * 255))
    return out


def upscale_frame(frame: Image, pixels: int = MASTER) -> Image:
    """An original frame at the master size with the SAME resampling as a layer (opaque everywhere): the 'original' column of a comparison."""
    colours, alphas = cells_of(frame), [1.0] * (UNITS * UNITS)
    return render_layer(colours, alphas, "cells", pixels)


# --- the parts of one button ----------------------------------------------------------------------------------------------

def button_layers(button: Button, frames: Path, template: Image) -> dict[str, tuple[Image, Image, bool, bool]]:
    """The layers of one button by part id: (the frame, the base frame it lies over, whether its soft cells are a glow, whether the frame's rim cells belong to it). The idle symbol has no glow; a pressed
    overlay owns the rim (the pressed frame darkens the frame lines)."""
    idle, pressed, hover = (read_frame(frames, button.folder, n) for n in (0, 1, 2))
    on0 = read_frame(frames, button.on_folder, 0)
    layers = {
        f"glyph.{button.name}": (idle, template, False, False),
        f"glyph.{button.name}_lit": (on0, template, True, False),
        f"plate.{button.name}_hover": (hover, idle, True, False),
        f"plate.{button.name}_pressed": (pressed, idle, True, True),
    }
    if (frames / button.on_folder / "f1.png").is_file():
        layers[f"state.{button.on_name}_pressed"] = (read_frame(frames, button.on_folder, 1), on0, True, True)
    return layers


def render_part(frame: Image, base: Image, glow: bool, rim: bool, look: str, halo: bool = False) -> Image:
    if look == "crisp":
        return render_crisp(frame, base, glow, rim, halo=halo)
    colours, alphas = decompose(frame, base)
    return render_layer(colours, alphas, look)


def template_of(frames: Path) -> Image:
    return plate_template([read_frame(frames, b.folder, 0) for b in BUTTONS])


def sha256_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def build_parts(frames: Path, look: str, plate_dir: Path | None, only: set[str] | None = None) -> dict[str, Image]:
    """Every part of the slice by id: the glyphs, the overlays and, when the plate masters are in `plate_dir`, the lit faces (the button's own plate: the lit art has the same frame)."""
    template = template_of(frames)
    parts: dict[str, Image] = {}
    for button in BUTTONS:
        if only and button.name not in only:
            continue
        for ident, (frame, base, glow, rim) in button_layers(button, frames, template).items():
            parts[ident] = render_part(frame, base, glow, rim, look, halo=ident.startswith("glyph."))
        plate = plate_dir / f"plate.{button.name}.png" if plate_dir else None
        if plate is not None and plate.is_file():
            master = pngio.read(plate)
            if (master.w, master.h) != (MASTER, MASTER):
                raise ButtonError(f"{plate}: {master.w} x {master.h}, the plate master is {MASTER} x {MASTER}")
            parts[f"state.{button.on_name}"] = Image(master.w, master.h, bytearray(master.data))
    return parts


def record_of(ident: str, look: str, frames: Path, image_path: Path, button: Button, plate_path: Path | None = None) -> dict:
    """The provenance record of a rule-made part (SWI-GEN-6): no generator, the rule and the hashes of the original frames it read."""
    folders = [button.folder, button.on_folder]
    template = {f"template/{b.folder}/f0": sha256_file(frames / b.folder / "f0.png") for b in BUTTONS}  # the plate template is voted from all five idle frames
    extra = {"plate_master_sha256": sha256_file(plate_path)} if plate_path is not None else {}
    return {"part": ident, "template_sources": template, **extra, "rule": "button_kit", "tool_version": TOOL_VERSION, "look": look, "date": datetime.date.today().isoformat(),
            "sources": {f"{folder}/f{n}": sha256_file(frames / folder / f"f{n}.png") for folder in folders for n in range(3) if (frames / folder / f"f{n}.png").is_file()},
            "parameters": ({"dead_de": DEAD_DE, "solid_de": SOLID_DE, "crisp_sigma": CRISP_SIGMA, "glow_sigma": GLOW_SIGMA, "edge_half": EDGE_HALF, "halo_min_contrast": HALO_MIN_CONTRAST, "core_de": CORE_DE, "core_share": CORE_SHARE, "dark_dl": DARK_DL, "halo_reach": HALO_REACH, "halo_sigma": HALO_SIGMA,
                           "master": MASTER, "inset": list(INSET)} if look == "crisp" else {"dead_de": DEAD_DE, "full_de": FULL_DE, "sigma": SIGMA[look], "master": MASTER, "inset": list(INSET)}),
            "image_sha256": hashlib.sha256(image_path.read_bytes()).hexdigest(), "image_size": [MASTER, MASTER], "run_by": "agent"}


def owner_of(ident: str) -> Button:
    for button in BUTTONS:
        if ident.startswith(f"state.{button.on_name}") or ident.split(".", 1)[1] in (button.name, f"{button.name}_lit", f"{button.name}_hover", f"{button.name}_pressed"):
            return button
    raise ButtonError(f"{ident} belongs to no button of this slice")


# --- commands -----------------------------------------------------------------------------------------------------------

def cmd_refs(args: argparse.Namespace) -> int:
    local = args.local_root
    frames = args.frames or local / "census" / "frames"
    if shutil.which("magick") is None:
        raise ButtonError("ImageMagick `magick` is not installed")
    refs = local / "refs"
    refs.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory() as tmp:
        raw = Path(tmp) / "template.png"
        pngio.write(raw, template_of(frames))
        target = refs / f"{PLATE_PROMPT}-original.png"
        subprocess.run(["magick", str(raw), "-filter", "point", "-resize", f"{REFS_SIZE}x{REFS_SIZE}!", str(target)], check=True)
    sheet = local / "material-sheet" / "cutouts" / "brass-edge.png"
    context = local / "outbox" / "style-anchors-contexts" / "ctx-hud.png"
    for src in (sheet, context):
        if not src.is_file():
            raise ButtonError(f"missing {src}")
    shutil.copyfile(sheet, refs / f"{PLATE_PROMPT}-sheet-1-brass-edge.png")
    subprocess.run(["magick", str(context), "-crop", "290x76+0+0", "+repage", "-filter", "Lanczos", "-resize", "300%", str(refs / f"{PLATE_PROMPT}-sheet-2-hud-buttons.png")], check=True)
    for path in sorted(refs.glob(f"{PLATE_PROMPT}-*.png")):
        print(f"{path.name}  {prompt_library.png_size(path)}  {sha256_file(path)[:12]}")
    print(f"BUTTON KIT refs PASS: build the inbox with prompt_library.py inbox B-BUTTONS")
    return 0


def cmd_flatten(args: argparse.Namespace) -> int:
    """The generated plate as an opaque square: the plate fills its whole 34-unit frame, as the original's does, so the generator's notched corners (transparent) take the frame's shadow colour. The result
    is the `--source` of `fit.py fit plate.<name>` (with the generator's own provenance record); the plate parts of the five buttons are the same picture."""
    local = args.local_root
    source = args.source or local / "outbox" / "B-BUTTONS" / f"{PLATE_PROMPT}.png"
    out = args.out or local / "work" / "B-BUTTONS" / f"{PLATE_PROMPT}-opaque.png"
    generator = args.provenance or source.with_name(source.stem + ".provenance.json")
    if not generator.is_file():
        raise ButtonError(f"no generator record {generator} (SWI-GEN-6; --provenance names another)")
    record = json.loads(generator.read_text(encoding="utf-8"))
    if record.get("image_sha256") != sha256_file(source):
        raise ButtonError(f"{source.name} differs from the image its record {generator.name} describes")
    image = pngio.read(source)
    if image.w != image.h:
        raise ButtonError(f"{source} is {image.w} x {image.h}: a square plate is square")
    flat = pngio.Image(image.w, image.h, bytearray(image.w * image.h * 4))
    for i in range(0, len(image.data), 4):
        a = image.data[i + 3] / 255.0
        flat.data[i:i + 3] = bytes(int(round(image.data[i + k] * a + SHADOW[k] * (1.0 - a))) for k in range(3))
        flat.data[i + 3] = 255
    out.parent.mkdir(parents=True, exist_ok=True)
    pngio.write(out, flat)
    derived = out.with_name(out.stem + ".provenance.json")  # the generator's record of the derived file: what fit.py checks, with the step that made it
    record["derived_from"] = {"image": record.get("image"), "image_sha256": record["image_sha256"], "step": "button_kit.py flatten (alpha dropped, transparent corners take the frame's shadow colour)"}
    record["image"], record["image_sha256"] = str(out.relative_to(local)) if out.is_relative_to(local) else out.name, sha256_file(out)
    derived.write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
    print(f"{out.name}  {flat.w} x {flat.h}  {sha256_file(out)[:12]}  (record {derived.name})")
    print("BUTTON KIT flatten PASS: fit it with fit.py fit plate.<name> --source this file --provenance its record")
    return 0


def cmd_build(args: argparse.Namespace) -> int:
    local = args.local_root
    frames = args.frames or local / "census" / "frames"
    plate_dir = args.plate_dir or local / "masters"
    out = args.out or local / "masters"
    records = local / "provenance" / "rule" if args.out is None else out / "records"  # an alternative look written elsewhere leaves the kit's records alone
    only = set(args.only.split(",")) if args.only else None
    if only and not only <= {b.name for b in BUTTONS}:
        raise ButtonError(f"--only names unknown buttons {sorted(only - {b.name for b in BUTTONS})}; the buttons are {[b.name for b in BUTTONS]}")
    parts = build_parts(frames, args.look, plate_dir, only)
    skipped = [f"state.{b.on_name}" for b in BUTTONS if (not only or b.name in only) and f"state.{b.on_name}" not in parts]  # the lit faces are the plate masters
    for ident, image in parts.items():
        covered = sum(1 for i in range(3, len(image.data), 4) if image.data[i] > 0) / (image.w * image.h)
        print(f"{ident:28s} {image.w} x {image.h}  covered {covered * 100:5.1f} %")
    if skipped:
        print(f"skipped (no plate master in {plate_dir}; run fit.py first): {', '.join(skipped)}")
    if args.dry_run:
        print(f"BUTTON KIT build DRY-RUN PASS ({len(parts)} parts, look {args.look}); nothing written")
        return 0
    out.mkdir(parents=True, exist_ok=True)
    records.mkdir(parents=True, exist_ok=True)
    for ident, image in parts.items():
        path = out / f"{ident}.png"
        pngio.write(path, image)
        (records / f"{ident}.json").write_text(json.dumps(record_of(ident, args.look, frames, path, owner_of(ident), plate_dir / f"plate.{owner_of(ident).name}.png" if ident.startswith("state.") and not ident.endswith("_pressed") else None), indent=2) + "\n", encoding="utf-8")
    print(f"BUTTON KIT build PASS ({len(parts)} parts written to {out}, look {args.look})")
    return 0


def cmd_sheet(args: argparse.Namespace) -> int:
    """The before / after composite: per button the original's frames (idle, hover, pressed, lit, lit pressed) upscaled the same way, and below them the kit's layers laid over the plate master."""
    local = args.local_root
    frames = args.frames or local / "census" / "frames"
    plate_dir = args.plate_dir or local / "masters"
    if shutil.which("magick") is None:
        raise ButtonError("ImageMagick `magick` is not installed")
    if args.zoom < 1:
        raise ButtonError("--zoom must be at least 1")
    parts = build_parts(frames, args.look, plate_dir)
    work = Path(tempfile.mkdtemp(prefix="button-sheet-"))
    try:
        return sheet_into(args, frames, plate_dir, parts, work)
    finally:
        shutil.rmtree(work, ignore_errors=True)


def sheet_into(args: argparse.Namespace, frames: Path, plate_dir: Path, parts: dict[str, Image], work: Path) -> int:
    rows = []
    zoom = args.zoom
    labels = ["idle", "hover", "pressed", "lit", "lit pressed"]
    for button in BUTTONS:
        plate = pngio.read(plate_dir / f"plate.{button.name}.png")
        idle = composite(plate, parts[f"glyph.{button.name}"])
        lit = composite(parts[f"state.{button.on_name}"], parts[f"glyph.{button.name}_lit"])
        shots_after = [idle, composite(idle, parts[f"plate.{button.name}_hover"]), composite(idle, parts[f"plate.{button.name}_pressed"]), lit]
        lit_pressed = parts.get(f"state.{button.on_name}_pressed")
        shots_after.append(composite(lit, lit_pressed) if lit_pressed else None)
        originals = [read_frame(frames, button.folder, n) for n in (0, 2, 1)] + [read_frame(frames, button.on_folder, 0)]
        has_second = (frames / button.on_folder / "f1.png").is_file()
        originals.append(read_frame(frames, button.on_folder, 1) if has_second else None)
        cells = []
        for before, after in zip(originals, shots_after):
            for tag, shot, nearest in (("before", before, True), ("after", after, False)):
                path = work / f"{button.name}-{len(cells)}-{tag}.png"
                if shot is None:
                    subprocess.run(["magick", "-size", f"{MASTER * zoom}x{MASTER * zoom}", "xc:#202020", str(path)], check=True)
                else:
                    pngio.write(path, shot)
                    flags = ["-filter", "point", "-resize", f"{MASTER * zoom}x{MASTER * zoom}!"] if nearest else ["-filter", "Lanczos", "-resize", f"{MASTER * zoom}x{MASTER * zoom}!"]
                    subprocess.run(["magick", str(path), "-background", "#202020", "-alpha", "remove", *flags, str(path)], check=True)
                cells.append(path)
        row = work / f"{button.name}-row.png"
        befores, afters = cells[0::2], cells[1::2]
        subprocess.run(["magick", "(", *befores, "+append", ")", "(", *afters, "+append", ")", "-append", str(row)], check=True)
        rows.append(row)
    sheet = work / "sheet.png"
    subprocess.run(["magick", *rows, "-append", str(sheet)], check=True)
    head = work / "head.png"
    width = MASTER * zoom * 5
    subprocess.run(["magick", "-size", f"{width}x40", "xc:#202020", "-font", LABEL_FONT, "-fill", "white", "-pointsize", "22", "-gravity", "west", "-annotate", "+10+0",
                    "ARC-UI-2482 slice 1, look " + args.look + ": top row of each button = ORIGINAL (nearest), bottom row = KIT (plate + glyph + overlays), columns: " + ", ".join(labels), str(head)], check=True)
    subprocess.run(["magick", str(head), str(sheet), "-append", str(args.out)], check=True)
    print(f"BUTTON KIT sheet PASS: {args.out}")
    return 0


def self_test() -> int:
    """The unit tests, run as the package module (they import this tool relatively, so not in this process when it runs as a script)."""
    code = subprocess.run([sys.executable, "-B", "-m", "unittest", "tools.steamwoven_ui.test_button_kit"], cwd=HERE.parents[1]).returncode
    print("BUTTON KIT self-test " + ("PASS" if code == 0 else "FAIL"))
    return 0 if code == 0 else 1


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("refs", "flatten", "build", "sheet"):
        s = sub.add_parser(name)
        s.add_argument("--frames", type=Path)
        s.add_argument("--local-root", type=Path, default=None)
        if name not in ("refs", "flatten"):
            s.add_argument("--look", choices=LOOKS, default="crisp")
            s.add_argument("--plate-dir", type=Path)
        if name == "flatten":
            s.add_argument("--source", type=Path)
            s.add_argument("--provenance", type=Path, help="the generator's record of the source (default: <source stem>.provenance.json beside it)")
            s.add_argument("--out", type=Path)
        if name == "build":
            s.add_argument("--out", type=Path)
            s.add_argument("--only")
            s.add_argument("--dry-run", action="store_true")
        if name == "sheet":
            s.add_argument("--out", type=Path, required=True)
            s.add_argument("--zoom", type=int, default=3)
    sub.add_parser("self-test")
    args = parser.parse_args(argv)
    try:
        if args.command == "self-test":
            return self_test()
        args.local_root = args.local_root or prompt_library.default_local_root()
        return {"refs": cmd_refs, "flatten": cmd_flatten, "build": cmd_build, "sheet": cmd_sheet}[args.command](args)
    except (ButtonError, pngio.PngError, OSError, subprocess.CalledProcessError) as error:
        print(f"BUTTON KIT FAIL: {error}")
        return 2


if __name__ == "__main__":
    sys.exit(main())
