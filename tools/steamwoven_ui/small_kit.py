#!/usr/bin/env python3
"""ARC-UI-3206 (batch B-BUTTONS, slice 6): the small stone and dark buttons (scroll arrows, plus / minus, follower arrows, the shift lamp) as kit parts: a plate, and the glyph and the state overlays BY RULE from the original's own frames.

    python3 -B tools/steamwoven_ui/small_kit.py plan   [--local-root DIR]
    python3 -B tools/steamwoven_ui/small_kit.py refs   [--frames DIR] [--local-root DIR]
    python3 -B tools/steamwoven_ui/small_kit.py cut    [--only PROMPT,...] [--dry-run]    (the Codex results -> keyed, cut, fitted plate masters)
    python3 -B tools/steamwoven_ui/small_kit.py plates [--only NAME,...] [--out DIR]     (the round buttons' plates: the bottom strip's dome with the brass ring laid over it; the OFF plates: darkened by rule)
    python3 -B tools/steamwoven_ui/small_kit.py build  [--only NAME,...] [--out DIR] [--dry-run] [--frames DIR] [--local-root DIR] [--plate-dir DIR]
    python3 -B tools/steamwoven_ui/small_kit.py sheet  --out FILE [--only NAME,...] [--zoom N] ...                 (before / after composite with English labels, needs ImageMagick)
    python3 -B tools/steamwoven_ui/small_kit.py self-test

Owner rule of 2026-11-25 (round 3 of #3204, "rule F"; TRD next to rule E): a symbol of a round or small button is NOT carried over as the original's cells (they are the low-resolution noise of the
original, "pixel mush"). Its SHAPE is read from the original's mask (the cells of the symbol's colour in the frame, the dither of the original's checkerboard closed), drawn as a smooth sharp curve (a small
Gaussian of the mask and a one-pixel soft threshold: a line stays straight, a corner stays a corner), filled SOLID in the symbol's own colour in the frame (the brighter half of its cells: the dither shows the symbol's colour
in its lighter cells), with a light engraving: a light rim at the upper left and a shade at the lower right. Hover and pressed light the symbol up (the colour of the state's frame) with a soft glow where the
original glows, and darken the plate where the original does (a soft black layer, alpha = the share of lightness lost). The checkerboard of cells stays only on the buttons of the strips (pilot #3058, rule E).

Per button (TRD SWI-KIT-5; the runtime's convention `SteamwovenKitComposer.ButtonLayers`: face, over it the glyph, over both the overlay `<face>_hover` or `<face>_pressed`):

* `plate.<name>` the empty plate. The ROUND buttons (the olive dome with a brass ring: 624, 625, 667, 668) take the dome of the bottom strip's round button (`plate.char_common_skills`, one Codex item) and the brass ring is drawn BY RULE at the original's width in the tone of the
  hardware rings (`hardware.menu_ring`, #3259: the same cross-section and light). Every other plate is one Codex item per family, fitted by `fit.py` (the prompt library, batch B-BUTTONS).
* `glyph.<name>` the symbol on a transparent canvas the size of the plate; `plate.<name>_hover` and `plate.<name>_pressed` the overlays.

Nothing here holds art: every picture is read from and written to the ignored `local/steamwoven-ui/`. Standard library only (`pngio.py`, `button_kit.py`); ImageMagick only for `refs` and `sheet`.
Exit 0 = done, 1 = a check failed, 2 = an input could not be used.
"""
from __future__ import annotations

import argparse
import datetime
import json
import math
import shutil
import subprocess
import sys
import tempfile
from dataclasses import dataclass, field
from pathlib import Path

HERE = Path(__file__).resolve().parent
try:
    from . import button_kit, pngio, prompt_library
except ImportError:  # run as a script
    sys.path.insert(0, str(HERE))
    import button_kit  # type: ignore
    import pngio  # type: ignore
    import prompt_library  # type: ignore

Image = pngio.Image
TOOL_VERSION = 1
MANIFEST = HERE / "kit.json"
SYMBOL_SIGMA = 0.32          # cells of the ORIGINAL frame: the Gaussian that turns the mask's staircase into a smooth curve
HD_SIGMA = 1.00              # pixels of an HD pack frame (4 per cell): the Gaussian of its mask (it is already a smooth shape; only the paint's fringe is rounded)
SYMBOL_LEVEL = 0.50          # the field's level of the outline (below 0.5: a thin line keeps its width)
EDGE_HALF = 0.12             # half width of the outline's soft step on the field (about one master pixel)
ENGRAVE_PX = 1               # master pixels: the light rim (upper left) and the shade (lower right) of the engraved symbol
ENGRAVE_LIGHT = 0.45         # the rim: this share of the way from the symbol's colour to white
ENGRAVE_SHADE = 0.55         # the shade: the symbol's colour times this
GLOW_SIGMA = 1.15            # cells: the Gaussian of the glow round a lit symbol
GLOW_MAX = 0.60              # the glow's alpha at the symbol's edge at most
GLOW_MIN = 0.10              # a state whose frame glows less than this has no glow
DARK_MIN = 0.05              # a state that loses less than this share of the plate's lightness (mean over the frame's interior) has no darkening layer
DARK_MAX = 0.75              # the darkening layer's alpha at most
DARK_SIGMA = 2.20            # cells: the Gaussian of the darkening layer (wide: the original's dome texture would blotch a narrow one)
DARK_DISC = 0.76             # a round button's darkening stays inside this share of its radius (the brass ring does not darken)
CLOSE_PASSES = 1             # the dither's holes are closed by this many dilations and erosions (an HD pack frame: twice as many)
SPECK = 2                    # a piece of the symbol's mask of this many ORIGINAL cells or fewer is dropped
ASPECT_TOLERANCE = 0.14      # a cut-out is stretched to its master's aspect when it is within this share of it
RING_UNITS = 2.6             # the brass ring's width in the original's cells (the same width as the school buttons' ring, #3204)
RING_SOURCE = "hardware.menu_ring"   # the master whose brass the ring takes (the hardware ring of the menu strips, #3259): its cross-section and its light over the angles
RING_BINS = 12
RING_ANGLES = 24
RING_GAIN = (0.78, 1.28)
DOME_SOURCE = "plate.char_common_skills"   # the bottom strip's round dome (plate-round-hud): the olive dome the round buttons stand on
LABEL_FONT = button_kit.LABEL_FONT
STATES = {"hover": "hover", "pressed": "pressed"}
PACK_ROOT = Path.home() / "Library" / "Application Support" / "Arcanum Steamwoven Game" / "hd-packs" / "interface-redraw-4x"   # the installed pack: its old generative HD frames are the first source of a symbol's shape (owner 2026-11-25)
PACK_SCALE = 4               # an HD pack frame is this many times the original


class SmallError(Exception):
    pass


@dataclass(frozen=True)
class Small:
    name: str                       # the plate's name in the manifest (`plate.<name>`)
    art: int
    folder: str                     # the census frame folder (`<art>-<name>`)
    family: str                     # `round`, `stone`, `pill`, `strip`, `tri`, `lamp`
    units: tuple[int, int]          # the original frame, in cells
    master: tuple[int, int]         # the kit master of the plate, in px (kit.json)
    hue: str                        # `red`, `green`, `light`: which cells of a frame are the symbol
    interior: tuple[int, int, int, int]   # x0, y0, x1, y1 (inclusive): the cells the symbol may lie in
    frames: dict = field(default_factory=lambda: {"idle": 0, "pressed": 1, "hover": 2})   # state -> frame number (a state without a frame has no overlay)
    outline: str = ""               # `tri_up` / `tri_down`: the symbol is an exact isosceles triangle fitted to the mask; empty: the mask's own smooth outline
    shape: str = "mask"             # where the symbol's shape is read: `mask` (the original's mask: plain plus, minus and arrow shapes read well from it) or `pack` (the old HD frame of the pack, the owner's first source)
    plate: str = "codex"            # `dome` (the bottom strip's dome + the brass ring by rule), `rule` (the scroll arrows' triangle by rule), `derived` (the plate of `base` darkened by rule) or `codex` (fitted by fit.py into the masters)
    base: str = ""                  # for `derived`: the button whose plate this one is
    sigma: float = SYMBOL_SIGMA     # cells: the Gaussian of the mask (smaller: sharper corners, for the one-cell strokes)
    level: float = SYMBOL_LEVEL     # the outline's level on the blurred mask: lower = a bolder line (the one-cell strokes of the small olive buttons)
    engrave: bool = True            # False: the symbol is too thin for a rim and a shade (a one-cell line of the small olive buttons), it is plain solid
    note: str = ""


def _round(name: str, art: int, folder: str) -> Small:
    return Small(name, art, folder, "round", (29, 30), (102, 108), "red", (7, 8, 21, 22), plate="dome")


def _stone(name: str, art: int, folder: str) -> Small:
    return Small(name, art, folder, "stone", (32, 32), (114, 114), "red", (6, 6, 25, 25))


SMALL = (
    _round("skiladdbut", 624, "624-SkilAddBut"),
    _round("skilminusbut", 625, "625-SkilMinusBut"),
    _round("sldrbutt_l_arrow", 667, "667-SldrButt_L_Arrow"),
    _round("sldrbutt_r_arrow", 668, "668-SldrButt_R_Arrow"),
    _stone("spelltech_add", 647, "647-SpellTech_Add"),
    _stone("spelltech_minus", 648, "648-SpellTech_Minus"),
    Small("sp_minus", 257, "257-SP_Minus", "pill", (22, 13), (78, 48), "light", (4, 3, 17, 9), frames={"idle": 0, "pressed": 1}, sigma=0.2, level=0.28, engrave=False),
    Small("sp_plus", 258, "258-SP_Plus", "pill", (22, 13), (78, 48), "light", (4, 3, 17, 9), frames={"idle": 0, "pressed": 1}, sigma=0.2, level=0.28, engrave=False),
    Small("oldchar_minus", 804, "804-OldChar_Minus", "pill", (13, 10), (48, 36), "light", (2, 2, 10, 7), sigma=0.2, level=0.28, engrave=False),
    Small("oldchar_plus", 805, "805-OldChar_Plus", "pill", (13, 10), (48, 36), "light", (2, 2, 10, 7), sigma=0.2, level=0.28, engrave=False),
    Small("scrllup", 317, "317-ScrllUP", "tri", (11, 15), (42, 54), "green", (2, 2, 8, 12), outline="tri_up", plate="rule"),
    Small("scrlldwn", 318, "318-ScrllDWN", "tri", (11, 14), (42, 48), "green", (2, 2, 8, 11), outline="tri_down", plate="rule"),
    Small("followerarrow_dwn", 501, "501-FollowerArrow_DWN", "strip", (67, 13), (240, 48), "red", (20, 2, 46, 10), frames={"idle": 0}, outline="tri_down"),
    Small("followerarrow_up", 502, "502-FollowerArrow_UP", "strip", (67, 13), (240, 48), "green", (20, 2, 46, 10), frames={"idle": 0}, outline="tri_up"),
    Small("follow_scroll_up", 506, "506-Follow_Scroll_up", "strip", (33, 13), (120, 48), "green", (5, 2, 28, 11), frames={"idle": 0, "pressed": 1}, outline="tri_up"),
    Small("follow_scroll_up_off", 507, "507-Follow_Scroll_up_OFF", "strip", (33, 13), (120, 48), "green", (5, 2, 28, 11), frames={"idle": 0}, outline="tri_up", plate="derived", base="follow_scroll_up"),
    Small("follow_scroll_dwn", 508, "508-Follow_Scroll_dwn", "strip", (34, 13), (120, 48), "green", (5, 2, 29, 11), frames={"idle": 0, "pressed": 1}, outline="tri_down"),
    Small("follow_scroll_dwn_off", 509, "509-Follow_Scroll_dwn_OFF", "strip", (34, 13), (120, 48), "green", (5, 2, 29, 11), frames={"idle": 0}, outline="tri_down", plate="derived", base="follow_scroll_dwn"),
)


@dataclass(frozen=True)
class Item:
    prompt: str                     # the prompt library's id (batch B-BUTTONS-SMALL)
    ref: str                        # the button whose idle frame (its symbol removed) is the ORIGINAL reference
    parts: tuple[str, ...]          # the buttons whose `plate.<name>` this item is fitted to
    ground: str                     # `full` (the plate fills the picture) or `margin` (a cut-out on a flat magenta ground, cut by `cut`)
    size: tuple[int, int]           # the asked pixel size (the prompt library's)


BATCH = "B-BUTTONS-SMALL"
MARGIN = 0.06                       # the share of the picture left to the magenta ground on every side
GROUND = (255, 0, 255)
ITEMS = (
    Item("plate-stone-square-hud", "spelltech_add", ("spelltech_add", "spelltech_minus"), "full", (1020, 1020)),
    Item("plate-olive-pill-hud", "sp_plus", ("sp_minus", "sp_plus"), "margin", (1014, 600)),
    Item("plate-olive-pill-small-hud", "oldchar_plus", ("oldchar_minus", "oldchar_plus"), "margin", (1014, 780)),
    Item("plate-stone-strip-hud", "followerarrow_dwn", ("followerarrow_dwn", "followerarrow_up"), "full", (2880, 558)),
    Item("plate-stone-strip-short-hud", "follow_scroll_up", ("follow_scroll_up", "follow_scroll_dwn"), "full", (1260, 504)),
)
TRI_FILL = (20, 20, 18)             # the empty field of a scroll triangle, where the green fill was


def button_named(name: str) -> Small:
    for b in SMALL:
        if b.name == name:
            return b
    raise SmallError(f"no small button {name!r}; the buttons are {[b.name for b in SMALL]}")


def selected(only: str | None) -> list[Small]:
    if not only:
        return list(SMALL)
    return [button_named(n) for n in only.split(",")]


# --- frames and symbol masks ------------------------------------------------------------------------------------------------

def read_frame(frames: Path, b: Small, number: int) -> Image:
    path = frames / b.folder / f"f{number}.png"
    if not path.is_file():
        raise SmallError(f"no frame {path}")
    image = pngio.read(path)
    if (image.w, image.h) != b.units:
        raise SmallError(f"{path}: {image.w} x {image.h}, the art {b.art} is {b.units[0]} x {b.units[1]}")
    return image


def lightness(r: int, g: int, b: int) -> float:
    return button_kit.lab(r, g, b)[0]


def is_symbol(hue: str, r: int, g: int, b: int, base_l: float) -> bool:
    if hue == "red":
        return r - g > 35 and r > 1.4 * g
    if hue == "green":
        return g - r > 8 and g - b > 20
    if hue == "light":
        return lightness(r, g, b) > base_l + 22.0
    raise SmallError(f"unknown hue {hue}")


def neighbours4(i: int, w: int, h: int) -> list[int]:
    x, y = i % w, i // w
    return [j for j, ok in ((i - 1, x > 0), (i + 1, x < w - 1), (i - w, y > 0), (i + w, y < h - 1)) if ok]


def components(mask: list[bool], w: int, h: int) -> list[list[int]]:
    seen = [False] * len(mask)
    out = []
    for start in range(len(mask)):
        if not mask[start] or seen[start]:
            continue
        stack, part = [start], []
        seen[start] = True
        while stack:
            i = stack.pop()
            part.append(i)
            x, y = i % w, i // w
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    nx, ny = x + dx, y + dy
                    if (dx or dy) and 0 <= nx < w and 0 <= ny < h:
                        j = ny * w + nx
                        if mask[j] and not seen[j]:
                            seen[j] = True
                            stack.append(j)
        out.append(part)
    return out


def dilate(mask: list[bool], w: int, h: int) -> list[bool]:
    return [mask[i] or any(mask[j] for j in neighbours4(i, w, h)) for i in range(len(mask))]


def base_lightness(frame: Image, b: Small) -> float:
    k = frame.w // b.units[0]
    x0, y0, x1, y1 = (v * k + (0 if i < 2 else k - 1) for i, v in enumerate(b.interior))
    values = sorted(lightness(*frame.pixel(x, y)[:3]) for y in range(y0, y1 + 1) for x in range(x0, x1 + 1) if frame.pixel(x, y)[3])
    return values[len(values) // 2] if values else 0.0


def symbol_mask(frame: Image, b: Small) -> list[bool]:
    """The cells of the symbol's colour inside the button's interior, the dither's holes closed, specks dropped. `frame` is the original (1 px per cell) or an HD pack frame (`PACK_SCALE` px per cell)."""
    w, h = frame.w, frame.h
    k = w // b.units[0]
    if (w, h) != (b.units[0] * k, b.units[1] * k):
        raise SmallError(f"{b.name}: a frame of {w} x {h} is not a whole multiple of {b.units}")
    base_l = base_lightness(frame, b)
    x0, y0, x1, y1 = (v * k + (0 if i < 2 else k - 1) for i, v in enumerate(b.interior))
    raw = [False] * (w * h)
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            r, g, bl, a = frame.pixel(x, y)
            raw[y * w + x] = a > 0 and is_symbol(b.hue, r, g, bl, base_l)
    # close the dither's holes: a cell with three or four of its four neighbours in the symbol joins it (a concave corner, which has two, stays a corner)
    mask = raw
    for _ in range(CLOSE_PASSES * k):
        mask = [mask[i] or sum(mask[j] for j in neighbours4(i, w, h)) >= 3 for i in range(w * h)]
    for part in components(mask, w, h):
        if len(part) <= SPECK * k * k:
            for i in part:
                mask[i] = False
    return mask


def coarse(mask: list[bool], w: int, h: int, k: int) -> list[bool]:
    """A mask of an HD frame reduced to the original's cells: a cell is symbol when at least half its pixels are."""
    if k == 1:
        return mask
    cw, ch = w // k, h // k
    return [sum(mask[(cy * k + dy) * w + cx * k + dx] for dy in range(k) for dx in range(k)) * 2 >= k * k for cy in range(ch) for cx in range(cw)]


GROUND_L = 28.0              # CIE L: a dither cell this dark (on average, the darker half of the symbol's cells) is the ground showing through, not a tone of the symbol


def symbol_colour(frame: Image, mask: list[bool], hue: str) -> tuple[int, int, int]:
    """The symbol's colour: the mean of its cells in the frame (the dither of the original is two tones of the symbol's colour; their mean is the colour it shows); when the darker half of the cells is
    near black (the ground showing through the dither) the colour is the mean of the brighter half."""
    cells = [frame.pixel(i % frame.w, i // frame.w)[:3] for i in range(len(mask)) if mask[i]]
    if not cells:
        raise SmallError("an empty symbol mask has no colour")
    cells.sort(key=lambda c: lightness(*c))
    half = len(cells) // 2
    if half and sum(lightness(*c) for c in cells[:half]) / half < GROUND_L:
        cells = cells[half:]
    return tuple(int(round(sum(c[k] for c in cells) / len(cells))) for k in range(3))  # type: ignore[return-value]


# --- rendering ---------------------------------------------------------------------------------------------------------------

def smooth(edge0: float, edge1: float, x: float) -> float:
    return button_kit.smoothstep(edge0, edge1, x)


def triangle_polygon_alpha(top: float, bottom: float, cx: float, half: float, size: tuple[int, int], master: tuple[int, int], up: bool) -> list[float]:
    """An isosceles triangle (apex at `top` when `up`, else at `bottom`; the base `half` * 2 wide at the other end) in cell coordinates of a `size` frame, anti-aliased 4 x 4 per master pixel."""
    w, h = size
    mw, mh = master
    out = [0.0] * (mw * mh)
    sx, sy = w / mw, h / mh
    ss = 4
    for py in range(mh):
        for px in range(mw):
            hit = 0
            for j in range(ss):
                y = (py + (j + 0.5) / ss) * sy
                if not top <= y <= bottom:
                    continue
                t = (y - top) / (bottom - top) if up else (bottom - y) / (bottom - top)
                reach = half * t
                for i in range(ss):
                    x = (px + (i + 0.5) / ss) * sx
                    if abs(x - cx) <= reach:
                        hit += 1
            out[py * mw + px] = hit / (ss * ss)
    return out


def triangle_alpha(mask: list[bool], size: tuple[int, int], master: tuple[int, int], up: bool) -> list[float]:
    """An isosceles triangle with the mask's box (the apex half a cell above its top row): exact straight edges, one sharp apex."""
    w, h = size
    biggest = max(components(mask, w, h), key=len)   # a stray cell beside the triangle is no part of it
    cells = [(i % w, i // w) for i in biggest]
    top, bottom = min(y for _, y in cells), max(y for _, y in cells) + 1
    left, right = min(x for x, _ in cells), max(x for x, _ in cells) + 1
    # the apex row of the original is one cell wide: the true apex lies half a cell beyond it
    return triangle_polygon_alpha(top - 0.5 if up else top, bottom if up else bottom + 0.5, (left + right) / 2.0, (right - left) / 2.0, size, master, up)


def silhouette(mask: list[bool], size: tuple[int, int], master: tuple[int, int], sigma: float = SYMBOL_SIGMA, level: float = SYMBOL_LEVEL) -> list[float]:
    """The symbol's alpha plane at the master size: a small Gaussian of the closed mask and a one-pixel soft step (no stair, a line stays straight, a corner stays a corner)."""
    field_ = button_kit.resample_plane([1.0 if m else 0.0 for m in mask], size, master, sigma)
    return [smooth(level - EDGE_HALF, level + EDGE_HALF, v) for v in field_]


def shifted(plane: list[float], w: int, h: int, dx: int, dy: int) -> list[float]:
    """out[x, y] = plane[x + dx, y + dy] (zero outside)."""
    out = [0.0] * (w * h)
    for y in range(h):
        sy = y + dy
        if 0 <= sy < h:
            for x in range(w):
                sx = x + dx
                if 0 <= sx < w:
                    out[y * w + x] = plane[sy * w + sx]
    return out


def paint_plane(alpha: list[float], rgb: tuple[int, int, int], size: tuple[int, int]) -> Image:
    w, h = size
    out = Image(w, h, bytearray(w * h * 4))
    for i, a in enumerate(alpha):
        if a > 0.002:
            o = i * 4
            out.data[o:o + 3] = bytes(rgb)
            out.data[o + 3] = int(round(min(1.0, a) * 255))
    return out


def engraved(alpha: list[float], colour: tuple[int, int, int], size: tuple[int, int], rim: bool = True) -> Image:
    """The solid symbol with its light rim (upper left) and shade (lower right) laid UNDER it, as one layer."""
    w, h = size
    light = tuple(int(round(c + (255 - c) * ENGRAVE_LIGHT)) for c in colour)
    shade = tuple(int(round(c * ENGRAVE_SHADE)) for c in colour)
    rim_on = rim
    rim_layer = paint_plane([0.85 * a for a in shifted(alpha, w, h, ENGRAVE_PX, ENGRAVE_PX)], light, size)       # the symbol moved to the upper left
    dark = paint_plane([0.85 * a for a in shifted(alpha, w, h, -ENGRAVE_PX, -ENGRAVE_PX)], shade, size)    # moved to the lower right
    body = paint_plane(alpha, colour, size)
    if not rim_on:
        return body
    return button_kit.composite(button_kit.composite(dark, rim_layer), body)


def glow_alpha(mask: list[bool], units: tuple[int, int], master: tuple[int, int], strength: float) -> list[float]:
    field_ = button_kit.resample_plane([1.0 if m else 0.0 for m in mask], units, master, GLOW_SIGMA)
    peak = max(field_) or 1.0
    return [min(1.0, strength * v / peak) for v in field_]


def glow_strength(idle: Image, state: Image, mask: list[bool], b: Small) -> float:
    """How much the state's frame brightens the ring of cells round the symbol (CIE L gain, scaled): 0 = no glow."""
    w, h = idle.w, idle.h
    near = mask
    for _ in range(3):
        near = dilate(near, w, h)
    own = dilate(symbol_mask(state, b), w, h)   # the state's own, bolder symbol is no glow
    gains = [lightness(*state.pixel(i % w, i // w)[:3]) - lightness(*idle.pixel(i % w, i // w)[:3]) for i in range(w * h) if near[i] and not mask[i] and not own[i] and idle.pixel(i % w, i // w)[3]]
    if not gains:
        return 0.0
    top = sorted(gains)[len(gains) * 3 // 4:]   # the brightest quarter of the ring: a glow that is only on one side (the lit side) still counts
    return max(0.0, min(GLOW_MAX, sum(top) / len(top) / 45.0))


def frame_clip(frame: Image, master: tuple[int, int]) -> list[float]:
    """The button's own outline at the master size (a soft step of the frame's alpha): an overlay never spills outside the plate."""
    field_ = button_kit.resample_plane([1.0 if frame.pixel(i % frame.w, i // frame.w)[3] else 0.0 for i in range(frame.w * frame.h)], (frame.w, frame.h), master, 0.35)
    return [smooth(0.30, 0.70, v) for v in field_]


def clipped(layer: Image, clip: list[float]) -> Image:
    out = Image(layer.w, layer.h, bytearray(layer.data))
    for i, c in enumerate(clip):
        o = i * 4 + 3
        out.data[o] = int(round(out.data[o] * c))
    return out


def dark_layer(idle: Image, state: Image, mask: list[bool], b: Small, master: tuple[int, int]) -> Image | None:
    """Where the state's frame is darker than the idle one outside the symbol and its glow: a soft black layer, alpha = the share of lightness lost."""
    w, h = idle.w, idle.h
    near = mask
    for _ in range(3):
        near = dilate(near, w, h)
    alphas = [0.0] * (w * h)
    x0, y0, x1, y1 = 0, 0, w - 1, h - 1
    total, count = 0.0, 0
    for i in range(w * h):
        x, y = i % w, i // w
        if near[i] or not idle.pixel(x, y)[3]:
            continue
        li, ls = lightness(*idle.pixel(x, y)[:3]), lightness(*state.pixel(x, y)[:3])
        if li > 3.0:
            alphas[i] = max(0.0, min(DARK_MAX, 1.0 - ls / li))
            total += alphas[i]
            count += 1
    if not count or total / count < DARK_MIN:
        return None
    # the cells under the symbol take their neighbours' darkening so the layer has no hole there
    known = [a if not near[i] else None for i, a in enumerate(alphas)]
    mean = total / count
    plane = [a if a is not None else mean for a in known]
    field_ = button_kit.resample_plane(plane, b.units, master, DARK_SIGMA)
    w, h = master
    alpha = [max(0.0, min(DARK_MAX, v)) for v in field_]
    if b.family == "round":
        for y in range(h):
            for x in range(w):
                rho = math.hypot((x + 0.5) / w * 2 - 1, (y + 0.5) / h * 2 - 1)
                alpha[y * w + x] *= smooth(DARK_DISC + 0.04, DARK_DISC - 0.04, rho)
    return paint_plane(alpha, (0, 0, 0), master)


# --- the brass ring of the round buttons ----------------------------------------------------------------------------------------

def read_master(local: Path, name: str) -> Image | None:
    path = local / "masters" / f"{name}.png"
    return pngio.read(path) if path.is_file() else None


def ring_profile(ring: Image) -> tuple[list[tuple[float, float, float]], list[float]]:
    """The cross-section of the hardware ring (its mean colour from the outer edge to the hole, `RING_BINS` bins) and the light over its angles."""
    cells = [(i % ring.w, i // ring.w) for i in range(ring.w * ring.h) if ring.data[i * 4 + 3] > 128]
    if not cells:
        raise SmallError(f"{RING_SOURCE} is empty")
    cx = (min(x for x, _ in cells) + max(x for x, _ in cells) + 1) / 2.0
    cy = (min(y for _, y in cells) + max(y for _, y in cells) + 1) / 2.0
    radii = [math.hypot(x + 0.5 - cx, y + 0.5 - cy) for x, y in cells]
    r_out, r_in = max(radii), min(radii)
    if r_out - r_in < 2.0:
        raise SmallError(f"{RING_SOURCE}: the ring is thinner than two pixels")
    sums = [[0.0, 0.0, 0.0, 0] for _ in range(RING_BINS)]
    light = [[0.0, 0] for _ in range(RING_ANGLES)]
    for (x, y), r in zip(cells, radii):
        t = (r_out - r) / (r_out - r_in)
        bin_ = min(RING_BINS - 1, int(t * RING_BINS))
        colour = ring.pixel(x, y)[:3]
        for k in range(3):
            sums[bin_][k] += colour[k]
        sums[bin_][3] += 1
        if 0.2 < t < 0.8:
            a = min(RING_ANGLES - 1, int((math.atan2(y + 0.5 - cy, x + 0.5 - cx) + math.pi) / (2 * math.pi) * RING_ANGLES))
            light[a][0] += sum(colour) / 3.0
            light[a][1] += 1
    profile: list = [tuple(c[k] / c[3] for k in range(3)) if c[3] else None for c in sums]
    for i in range(RING_BINS):
        if profile[i] is None:
            profile[i] = next(profile[i + d * s] for d in range(1, RING_BINS) for s in (-1, 1) if 0 <= i + d * s < RING_BINS and profile[i + d * s] is not None)
    filled = [v / n for v, n in light if n]
    if not filled:
        raise SmallError(f"{RING_SOURCE}: no cells in the ring's lit and shaded bands")
    means = [v / n if n else sum(filled) / len(filled) for v, n in light]
    overall = sum(means) / len(means)
    gains = [max(RING_GAIN[0], min(RING_GAIN[1], m / overall)) for m in means]
    return profile, [(gains[k - 1] + 2 * gains[k] + gains[(k + 1) % len(gains)]) / 4.0 for k in range(len(gains))]


def resample_bilinear(image: Image, w: int, h: int) -> Image:
    """The picture at another size by area-weighted box averaging (premultiplied), for a reduction; a plain bilinear when it grows."""
    out = Image(w, h, bytearray(w * h * 4))
    sx, sy = image.w / w, image.h / h
    for y in range(h):
        y0, y1 = y * sy, (y + 1) * sy
        for x in range(w):
            x0, x1 = x * sx, (x + 1) * sx
            acc = [0.0, 0.0, 0.0, 0.0]
            wsum = 0.0
            for iy in range(int(y0), min(image.h, int(math.ceil(y1)))):
                wy = min(y1, iy + 1) - max(y0, iy)
                for ix in range(int(x0), min(image.w, int(math.ceil(x1)))):
                    wt = wy * (min(x1, ix + 1) - max(x0, ix))
                    p = image.pixel(ix, iy)
                    a = p[3] / 255.0
                    acc[0] += p[0] * a * wt
                    acc[1] += p[1] * a * wt
                    acc[2] += p[2] * a * wt
                    acc[3] += a * wt
                    wsum += wt
            if acc[3] > 0 and wsum > 0:
                o = (y * w + x) * 4
                out.data[o:o + 3] = bytes(max(0, min(255, int(round(acc[k] / acc[3])))) for k in range(3))
                out.data[o + 3] = int(round(255 * acc[3] / wsum))
    return out


def round_plate(b: Small, local: Path) -> Image:
    """The round button's plate: the strip's olive dome over the whole circle, the brass ring (the hardware ring's cross-section and light, the original's width) laid over its outer edge."""
    dome = read_master(local, DOME_SOURCE)
    ring = read_master(local, RING_SOURCE)
    if dome is None or ring is None:
        raise SmallError(f"the round plates need the masters {DOME_SOURCE} and {RING_SOURCE} in {local / 'masters'}")
    profile, gains = ring_profile(ring)
    w, h = b.master
    sx, sy = w / b.units[0], h / b.units[1]
    scale = (sx + sy) / 2.0
    r_out = min(w, h) / 2.0
    width = RING_UNITS * scale
    under = resample_bilinear(dome, w, h)
    out = Image(w, h, bytearray(w * h * 4))
    cx, cy = w / 2.0, h / 2.0
    for y in range(h):
        for x in range(w):
            dx, dy = (x + 0.5 - cx) * (r_out / (w / 2.0)), (y + 0.5 - cy) * (r_out / (h / 2.0))
            r = math.hypot(dx, dy)
            a_out = max(0.0, min(1.0, r_out - r + 0.5))
            if a_out <= 0.0:
                continue
            weight = max(0.0, min(1.0, r - (r_out - width) + 0.5))
            t = max(0.0, min(1.0, (r_out - r) / width)) * RING_BINS - 0.5
            lo = max(0, min(RING_BINS - 1, int(math.floor(t))))
            hi = min(RING_BINS - 1, lo + 1)
            f = max(0.0, min(1.0, t - lo))
            ang = (math.atan2(dy, dx) + math.pi) / (2 * math.pi) * RING_ANGLES - 0.5
            g0 = int(math.floor(ang)) % RING_ANGLES
            g1 = (g0 + 1) % RING_ANGLES
            gain = gains[g0] + (gains[g1] - gains[g0]) * (ang - math.floor(ang))
            brass = [max(0.0, min(255.0, (profile[lo][k] * (1 - f) + profile[hi][k] * f) * gain)) for k in range(3)]
            p = under.pixel(x, y)
            base = p[:3] if p[3] else brass
            i = (y * w + x) * 4
            out.data[i:i + 3] = bytes(int(round(base[k] * (1 - weight) + brass[k] * weight)) for k in range(3))
            out.data[i + 3] = int(round(a_out * 255))
    return out


# --- the parts of one button ---------------------------------------------------------------------------------------------------

def read_plate(plate_dir: Path | None, b: Small) -> Image | None:
    if plate_dir is None:
        return None
    path = plate_dir / f"plate.{b.name}.png"
    return pngio.read(path) if path.is_file() else None


def pack_frame(pack: Path | None, b: Small, number: int) -> Image | None:
    """The old generative HD frame of the button in the installed pack (`index.tsv`: art name, frame number, path), or None when the pack has none (or is not there)."""
    index = pack / "index.tsv" if pack else None
    if index is None or not index.is_file():
        return None
    for line in index.read_text(encoding="utf-8").splitlines():
        cols = line.split("\t")
        if len(cols) > 4 and cols[2] == b.name and cols[3] == str(number):
            path = pack / cols[4]
            if not path.is_file():
                return None
            image = pngio.read(path)
            return image if (image.w, image.h) == (b.units[0] * PACK_SCALE, b.units[1] * PACK_SCALE) else None
    return None


@dataclass
class Shape:
    mask: list[bool]            # the symbol's mask at `size`
    size: tuple[int, int]
    source: str                 # `pack` (the old HD frame of the pack), `mask` (the original's mask, the symbol is plain geometry), `codex`, `manual`
    sigma: float


def symbol_shape(frames: Path, pack: Path | None, b: Small, shape: str | None = None) -> Shape:
    """Where a symbol's shape comes from (owner priority 2026-11-25): the pack's old HD frame of this button when it has one, else the original's mask (plain plus, minus and arrow shapes read well from it)."""
    hd = pack_frame(pack, b, b.frames["idle"]) if (shape or b.shape) == "pack" else None
    if hd is not None:
        mask = symbol_mask(hd, b)
        if any(mask):
            return Shape(mask, (hd.w, hd.h), "pack", HD_SIGMA)
    idle = read_frame(frames, b, b.frames["idle"])
    mask = symbol_mask(idle, b)
    if not any(mask):
        raise SmallError(f"{b.name}: no symbol found in the idle frame (hue {b.hue})")
    return Shape(mask, (idle.w, idle.h), "mask", b.sigma)


def button_parts(frames: Path, b: Small, pack: Path | None = None, shape_source: str | None = None) -> dict[str, Image]:
    """`glyph.<name>`, and `plate.<name>_hover` / `plate.<name>_pressed` for the states the art has frames for."""
    idle = read_frame(frames, b, b.frames["idle"])
    shape = symbol_shape(frames, pack, b, shape_source)
    mask = coarse(shape.mask, shape.size[0], shape.size[1], shape.size[0] // b.units[0])   # the symbol on the original's cells: the colours, the glow and the darkening are read there
    alpha = triangle_alpha(shape.mask, shape.size, b.master, b.outline == "tri_up") if b.outline else silhouette(shape.mask, shape.size, b.master, shape.sigma, b.level)
    parts = {f"glyph.{b.name}": engraved(alpha, symbol_colour(idle, mask, b.hue), b.master, b.engrave)}
    for state in STATES:
        number = b.frames.get(state)
        if number is None:
            continue
        frame = read_frame(frames, b, number)
        own = symbol_mask(frame, b) if b.hue == "light" else mask   # a grey symbol moves a cell when pressed: its own cells give the lit colour
        lit_colour = symbol_colour(frame, own if any(own) else mask, b.hue)
        lit = engraved(alpha, lit_colour, b.master, b.engrave)
        layers = []
        clip = frame_clip(idle, b.master)
        dark = dark_layer(idle, frame, mask, b, b.master)
        if dark is not None:
            layers.append(clipped(dark, clip))
        strength = glow_strength(idle, frame, mask, b)
        if strength >= GLOW_MIN:
            layers.append(clipped(paint_plane(glow_alpha(mask, b.units, b.master, strength), lit_colour, b.master), clip))
        layers.append(lit)
        out = layers[0]
        for layer in layers[1:]:
            out = button_kit.composite(out, layer)
        parts[f"plate.{b.name}_{state}"] = out
    return parts


def build_parts(frames: Path, buttons: list[Small], pack: Path | None = None, shape_source: str | None = None) -> dict[str, Image]:
    parts: dict[str, Image] = {}
    for b in buttons:
        parts.update(button_parts(frames, b, pack, shape_source))
    return parts


def upscaled(frame: Image, size: tuple[int, int]) -> Image:
    """An original frame at the master size, nearest cells (the 'before' column shows the original's own pixels)."""
    w, h = size
    out = Image(w, h, bytearray(w * h * 4))
    for py in range(h):
        cy = min(frame.h - 1, int((py + 0.5) * frame.h / h))
        for px in range(w):
            cx = min(frame.w - 1, int((px + 0.5) * frame.w / w))
            out.data[(py * w + px) * 4:(py * w + px) * 4 + 4] = frame.data[(cy * frame.w + cx) * 4:(cy * frame.w + cx) * 4 + 4]
    return out


# --- commands -----------------------------------------------------------------------------------------------------------------

def sha256_file(path: Path) -> str:
    return button_kit.sha256_file(path)


def record_of(ident: str, b: Small, frames: Path, image_path: Path) -> dict:
    return {
        "id": ident,
        "tool": "small_kit.py",
        "tool_version": TOOL_VERSION,
        "rule": "smooth solid symbol (rule F, owner 2026-11-25): the shape from the original's mask, solid in the symbol's colour, engraving; states light the symbol and glow / darken like the original",
        "source_art": b.art,
        "source_frames": [str(frames / b.folder / f"f{n}.png") for n in b.frames.values()],
        "sha256": sha256_file(image_path),
        "date": datetime.date.today().isoformat(),
    }


def need_magick() -> None:
    if shutil.which("magick") is None:
        raise SmallError("ImageMagick (`magick`) is needed for this command")


# --- the Codex items: references, cut, fit ---------------------------------------------------------------------------------------

def symbol_free(frame: Image, b: Small) -> Image:
    """The idle frame without its symbol (and the recess round it): the cells of the symbol, one cell wider, take the median of the nearest plain interior cells (a scroll triangle's field takes `TRI_FILL`)."""
    w, h = frame.w, frame.h
    mask = symbol_mask(frame, b)
    grown = dilate(mask, w, h)
    out = Image(w, h, bytearray(frame.data))
    x0, y0, x1, y1 = b.interior
    for i in range(w * h):
        if not grown[i]:
            continue
        x, y = i % w, i // w
        if b.family == "tri":
            out.data[i * 4:i * 4 + 3] = bytes(TRI_FILL)
            continue
        for reach in (2, 3, 4, 6):
            near = [frame.pixel(nx, ny)[:3] for ny in range(max(0, y - reach), min(h, y + reach + 1)) for nx in range(max(0, x - reach), min(w, x + reach + 1))
                    if not grown[ny * w + nx] and frame.pixel(nx, ny)[3]]
            if near:
                near.sort(key=lambda c: lightness(*c))
                out.data[i * 4:i * 4 + 3] = bytes(near[len(near) // 2])
                break
    return out


def cmd_refs(args: argparse.Namespace) -> int:
    local = args.local_root
    frames = args.frames or local / "census" / "frames"
    need_magick()
    refs = local / "refs"
    refs.mkdir(parents=True, exist_ok=True)
    for item in ITEMS:
        b = button_named(item.ref)
        free = symbol_free(read_frame(frames, b, b.frames["idle"]), b)
        w, h = item.size
        with tempfile.TemporaryDirectory() as tmp:
            raw = Path(tmp) / "free.png"
            pngio.write(raw, free)
            out = refs / f"{item.prompt}-original.png"
            if item.ground == "full":
                cmd = ["magick", str(raw), "-background", "#000000", "-alpha", "remove", "-alpha", "off", "-filter", "point", "-resize", f"{w}x{h}!", str(out)]
            else:
                iw, ih = round(w * (1 - 2 * MARGIN)), round(h * (1 - 2 * MARGIN))
                cmd = ["magick", str(raw), "-filter", "point", "-resize", f"{iw}x{ih}!", "-background", "#FF00FF", "-gravity", "center", "-extent", f"{w}x{h}", "-alpha", "remove", "-alpha", "off", str(out)]
            subprocess.run(cmd, check=True)
        print(f"{out.name}  {prompt_library.png_size(out)}  {sha256_file(out)[:12]}")
    print("SMALL KIT refs PASS: build the inbox with prompt_library.py inbox B-BUTTONS-SMALL")
    return 0


def is_ground(r: int, g: int, b: int) -> float:
    """0 = plate, 1 = the magenta ground (a soft step by the distance from FF00FF)."""
    d = math.sqrt((r - GROUND[0]) ** 2 + (g - GROUND[1]) ** 2 + (b - GROUND[2]) ** 2)
    return 1.0 - smooth(40.0, 110.0, d)


def key_cutout(image: Image) -> Image:
    """The generator's cut-out on the magenta ground as an RGBA picture cut to its bounding box: the ground is clear, a mixed edge pixel has the share of plate as its alpha."""
    out = Image(image.w, image.h, bytearray(image.data))
    xs, ys = [], []
    for i in range(image.w * image.h):
        o = i * 4
        r, g, b = image.data[o:o + 3]
        a = 1.0 - is_ground(r, g, b)
        spill = min(r - g, b - g) / 255.0   # a magenta lean (the ground's light on the plate's edge): the ground's share is taken out of the colour
        if spill > 0.04 and abs(r - b) < 40:
            out.data[o:o + 3] = bytes(max(0, min(255, int(round(v - spill * GROUND[k])))) for k, v in enumerate((r, g, b)))
        out.data[o + 3] = int(round(a * 255))
        if a > 0.5:
            xs.append(i % image.w)
            ys.append(i // image.w)
    if not xs:
        raise SmallError("the cut-out has no plate (all magenta)")
    return out.crop(min(xs), min(ys), max(xs) - min(xs) + 1, max(ys) - min(ys) + 1)


def generator_record(local: Path, prompt: str) -> dict:
    path = local / "outbox" / BATCH / f"{prompt}.provenance.json"
    if not path.is_file():
        raise SmallError(f"the generator record {path} is missing: run the batch first (codex_batch.py run {BATCH})")
    return json.loads(path.read_text(encoding="utf-8"))


HALO_TIERS = ("8,20", "16,40")   # the recorded overrides of fit.py's halo gate tried in this order for a keyed cut-out the standard gate refuses (as the gauge batch)


def fit_plate(part: str, source: Path, provenance: Path, local: Path, dry: bool) -> dict:
    try:
        from . import fit as fit_tool  # noqa: PLC0415 (imported here: fit.py is not needed by the other commands)
    except ImportError:  # run as a script
        import fit as fit_tool  # type: ignore  # noqa: PLC0415
    last = None
    for halo in (None, *HALO_TIERS):
        ns = argparse.Namespace(part=part, batch=None, prompt=None, source=str(source), provenance=str(provenance), no_provenance=False, size=None, crop=None, key="none",
                                material=None, lock_strength=1.0, force_lock=False, min_speck=fit_tool.MIN_SPECK, soft_shadow=False, date=None, local_root=str(local), masters=None,
                                replace=True, dry_run=dry, manifest=None, json=False, halo_limits=halo)
        try:
            return fit_tool.run_fit(ns)
        except fit_tool.GateError as error:
            last = error
            if not all(f.startswith("edge halo") for f in error.failures):
                raise
            print(f"{part}: halo gate refused at {halo or 'the standard limits'} ({error.failures[0]})")
    raise last  # type: ignore[misc]


def salvaged_band(local: Path, item: Item, record: dict, work: Path) -> tuple[Path, dict]:
    """A strip the generator refused for its aspect but saved (a canvas with black bars round the band, `refused` in its record) is used through the band crop of the batch runner (TRD SWI-GEN-8: the cropped band
    is accepted when the tool step produced it); the record says so. Anything else missing is an error."""
    picture = local / "codex-work" / BATCH / str(record.get("run")) / f"{item.prompt}.png"
    if record.get("outcome") != "refused" or item.size[0] / item.size[1] < 4.0 or not picture.is_file():
        raise SmallError(f"the result of {item.prompt} is missing in the outbox (run the batch first: codex_batch.py run {BATCH} --crop-band)")
    out = work / f"{item.prompt}-band.png"
    subprocess.run([sys.executable, "-B", str(HERE / "codex_batch.py"), "crop", str(picture), "--size", f"{item.size[0]}x{item.size[1]}", "--out", str(out)], check=True, capture_output=True)
    accepted = dict(record)
    accepted["accepted"] = {"by": "small_kit.py cut", "why": "the generator refused the aspect but saved a canvas with the band between black bars; the band was cut by codex_batch.py crop (aspect error within the tool's limits)",
                            "image": str(picture.relative_to(local)), "image_sha256": sha256_file(picture), "band": out.name}
    accepted["image"], accepted["image_sha256"] = out.name, sha256_file(out)
    return out, accepted


def cmd_cut(args: argparse.Namespace) -> int:
    """The Codex results -> the sources of fit.py (a magenta-ground cut-out is keyed and cut to its box, stretched to its master's aspect within `ASPECT_TOLERANCE`) -> the plate masters."""
    local = args.local_root
    work = local / "work" / BATCH
    work.mkdir(parents=True, exist_ok=True)
    manifest = {r["id"]: r for r in json.loads(MANIFEST.read_text(encoding="utf-8"))["parts"]}
    only = set(args.only.split(",")) if args.only else None
    n = 0
    for item in ITEMS:
        if only and item.prompt not in only:
            continue
        source = local / "outbox" / BATCH / f"{item.prompt}.png"
        record = generator_record(local, item.prompt)
        if not source.is_file():
            source, record = salvaged_band(local, item, record, work)
        for name in item.parts:
            part = f"plate.{name}"
            size = manifest[part]["size"]
            cut_png = work / f"{item.prompt}-{name}.png"
            if item.ground == "full":
                src = pngio.read(source)
                ratio = (src.w / src.h) / (size[0] / size[1])
                if abs(ratio - 1.0) <= 0.005:
                    shutil.copyfile(source, cut_png)
                    step = "none (the picture is the plate)"
                elif abs(ratio - 1.0) <= ASPECT_TOLERANCE:
                    subprocess.run(["magick", str(source), "-filter", "lanczos", "-resize", f"{src.w}x{round(src.w * size[1] / size[0])}!", str(cut_png)], check=True)
                    step = f"small_kit.py cut (stretched {abs(ratio - 1.0) * 100:.1f} % to the master's grid-snapped aspect {size[0]} x {size[1]})"
                else:
                    raise SmallError(f"{part}: the picture is {src.w} x {src.h}, {abs(ratio - 1.0) * 100:.0f} % off the master's aspect {size[0]} x {size[1]} (limit {ASPECT_TOLERANCE * 100:.0f} %)")
            else:
                keyed = key_cutout(pngio.read(source))
                ratio = (keyed.w / keyed.h) / (size[0] / size[1])
                if abs(ratio - 1.0) > ASPECT_TOLERANCE:
                    raise SmallError(f"{part}: the cut-out is {keyed.w} x {keyed.h}, {abs(ratio - 1.0) * 100:.0f} % off the master's aspect {size[0]} x {size[1]} (limit {ASPECT_TOLERANCE * 100:.0f} %): draw it again")
                tmp = work / f"{item.prompt}-{name}-keyed.png"
                pngio.write(tmp, keyed)
                subprocess.run(["magick", str(tmp), "-filter", "point", "-resize", f"{keyed.w}x{round(keyed.w * size[1] / size[0])}!", str(cut_png)], check=True)
                tmp.unlink()
                step = f"small_kit.py cut (magenta ground keyed, cut to the plate's box {keyed.w} x {keyed.h}, stretched {abs(ratio - 1.0) * 100:.1f} % to the master's aspect)"
            derived = dict(record)
            derived["derived_from"] = {"image": record.get("image"), "image_sha256": record.get("image_sha256"), "step": step}
            derived["image"], derived["image_sha256"] = cut_png.name, sha256_file(cut_png)
            prov = cut_png.with_suffix(".provenance.json")
            prov.write_text(json.dumps(derived, indent=2) + "\n", encoding="utf-8")
            report = fit_plate(part, cut_png, prov, local, args.dry_run)
            print(f"{part:30s} fitted from {item.prompt}  {report.get('size', '')}")
            n += 1
    print(f"SMALL KIT cut PASS ({n} plates{' (dry run)' if args.dry_run else ''})")
    return 0


def cmd_plan(args: argparse.Namespace) -> int:
    manifest = {r["id"]: r for r in json.loads(MANIFEST.read_text(encoding="utf-8"))["parts"]}
    bad = 0
    for b in SMALL:
        row = manifest.get(f"plate.{b.name}")
        how = {"dome": "dome + ring by rule", "rule": "triangle + brass by rule", "derived": f"{b.base} darkened by rule"}.get(b.plate, "codex plate")
        size = list(b.master)
        ok = row is not None and row["size"] == size
        bad += 0 if ok else 1
        print(f"plate.{b.name:24s} art {b.art}  {b.family:6s} {b.units[0]:>2} x {b.units[1]:<2} -> {b.master[0]} x {b.master[1]}  {how:20s} {'ok' if ok else 'MANIFEST SIZE DIFFERS'}")
    print(f"SMALL KIT plan {'PASS' if not bad else 'FAIL'} ({len(SMALL)} buttons)")
    return 0 if not bad else 1


TRI_BAND = (0.9, 2.3)       # cells from the plate's edge: the dark outline ends at the first, the brass band at the second (the original's band is about 1.4 cells)
TRI_OUTLINE = (34, 24, 12)  # the dark outline round the brass edge
TRI_FIELD = (20, 22, 10)    # the empty dark field inside the band (the green fill is the symbol, a separate layer)
TRI_LIGHT = 0.22            # the brass is this much lighter on the left and darker on the right (the light comes from the upper left)


def tri_plate(b: Small, frames: Path, local: Path) -> Image:
    """The scroll arrows' plate BY RULE (the generator copied the original's staircase outline, so a Codex item is no use here): an exact isosceles triangle on the frame's box, a dark outline, a brass band
    of the original's width in the brass of the hardware rings (`RING_SOURCE`, its lit band) and the empty dark field inside."""
    ring = read_master(local, RING_SOURCE)
    if ring is None:
        raise SmallError(f"{RING_SOURCE} is needed for the brass of the scroll arrows")
    profile, _ = ring_profile(ring)
    brass = tuple(sum(profile[k][c] for k in range(3, 8)) / 5.0 for c in range(3))
    frame = read_frame(frames, b, b.frames["idle"])
    cells = [(i % frame.w, i // frame.w) for i in range(frame.w * frame.h) if frame.data[i * 4 + 3]]
    left, right = min(x for x, _ in cells), max(x for x, _ in cells) + 1
    top, bottom = min(y for _, y in cells), max(y for _, y in cells) + 1
    up = b.outline == "tri_up"
    cx, half = (left + right) / 2.0, (right - left) / 2.0
    w, h = b.master
    alpha = triangle_polygon_alpha(top, bottom, cx, half, (frame.w, frame.h), b.master, up)
    sx, sy = frame.w / w, frame.h / h
    apex = (cx, top if up else bottom)
    corners = ((left, bottom), (right, bottom)) if up else ((left, top), (right, top))
    edges = [(apex, corners[0]), (apex, corners[1]), (corners[0], corners[1])]
    out = Image(w, h, bytearray(w * h * 4))
    for py in range(h):
        for px in range(w):
            a = alpha[py * w + px]
            if a <= 0.0:
                continue
            x, y = (px + 0.5) * sx, (py + 0.5) * sy
            d = min(abs((q[0] - p[0]) * (p[1] - y) - (p[0] - x) * (q[1] - p[1])) / math.hypot(q[0] - p[0], q[1] - p[1]) for p, q in edges)
            gain = 1.0 + TRI_LIGHT * (0.5 - (x - left) / (right - left)) * 2.0
            band = tuple(min(255.0, c * gain) for c in brass)
            into_band = smooth(TRI_BAND[0] - 0.2, TRI_BAND[0] + 0.2, d)
            into_field = smooth(TRI_BAND[1] - 0.2, TRI_BAND[1] + 0.2, d)
            colour = [TRI_OUTLINE[k] * (1 - into_band) + band[k] * into_band for k in range(3)]
            colour = [colour[k] * (1 - into_field) + TRI_FIELD[k] * into_field for k in range(3)]
            o = (py * w + px) * 4
            out.data[o:o + 3] = bytes(max(0, min(255, int(round(c)))) for c in colour)
            out.data[o + 3] = int(round(a * 255))
    return out


def derived_plate(b: Small, frames: Path, local: Path) -> Image:
    """The OFF plate of a follower scroll button: the base button's plate with the original's darkening laid over it (opaque, as the strip is)."""
    base = button_named(b.base)
    plate = read_master(local, f"plate.{base.name}")
    if plate is None:
        raise SmallError(f"plate.{base.name} is not fitted yet (the OFF plate is made from it)")
    idle = read_frame(frames, base, base.frames["idle"])
    off = read_frame(frames, b, b.frames["idle"])
    mask = [x or y for x, y in zip(symbol_mask(idle, base), symbol_mask(off, b))]
    dark = dark_layer(idle, off, mask, base, base.master)
    return plate if dark is None else button_kit.composite(plate, dark)


def cmd_plates(args: argparse.Namespace) -> int:
    out = args.out or args.local_root / "masters"
    records = args.local_root / "provenance" / "rule" if args.out is None else out / "records"
    out.mkdir(parents=True, exist_ok=True)
    records.mkdir(parents=True, exist_ok=True)
    frames = args.frames or args.local_root / "census" / "frames"
    n = 0
    for b in selected(args.only):
        if b.plate == "rule":
            image = tri_plate(b, frames, args.local_root)
            path = out / f"plate.{b.name}.png"
            pngio.write(path, image)
            record = record_of(f"plate.{b.name}", b, frames, path)
            record["rule"] = f"an exact isosceles triangle on the frame's box with a dark outline and a brass band of the original's width in the brass of the hardware rings ({RING_SOURCE}), by rule"
            (records / f"plate.{b.name}.json").write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
            print(f"plate.{b.name:24s} {image.w} x {image.h}")
            n += 1
            continue
        if b.plate == "derived":
            image = derived_plate(b, frames, args.local_root)
            path = out / f"plate.{b.name}.png"
            pngio.write(path, image)
            record = record_of(f"plate.{b.name}", b, frames, path)
            record["rule"] = f"the plate of {b.base} darkened where the original's OFF frame is darker than its idle frame (a soft black layer, alpha = the share of lightness lost), by rule"
            (records / f"plate.{b.name}.json").write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
            print(f"plate.{b.name:24s} {image.w} x {image.h}")
            n += 1
            continue
        if b.plate != "dome":
            continue
        image = round_plate(b, args.local_root)
        path = out / f"plate.{b.name}.png"
        pngio.write(path, image)
        record = record_of(f"plate.{b.name}", b, frames, path)
        record["rule"] = f"the bottom strip's olive dome ({DOME_SOURCE}) with the brass ring of the hardware rings ({RING_SOURCE}) at the original's width, by rule"
        (records / f"plate.{b.name}.json").write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
        print(f"plate.{b.name:24s} {image.w} x {image.h}")
        n += 1
    print(f"SMALL KIT plates PASS ({n} written to {out})")
    return 0


def cmd_build(args: argparse.Namespace) -> int:
    local = args.local_root
    frames = args.frames or local / "census" / "frames"
    out = args.out or local / "masters"
    records = local / "provenance" / "rule" if args.out is None else out / "records"
    buttons = selected(args.only)
    parts = build_parts(frames, buttons, args.pack, args.shape)
    owner = {f"{kind}.{b.name}{suffix}": b for b in buttons for kind, suffix in (("glyph", ""), ("plate", "_hover"), ("plate", "_pressed"))}
    for ident, image in parts.items():
        covered = sum(1 for i in range(3, len(image.data), 4) if image.data[i] > 0) / (image.w * image.h)
        print(f"{ident:36s} {image.w} x {image.h}  covered {covered * 100:5.1f} %")
    if args.dry_run:
        print(f"SMALL KIT build DRY-RUN PASS ({len(parts)} parts); nothing written")
        return 0
    out.mkdir(parents=True, exist_ok=True)
    records.mkdir(parents=True, exist_ok=True)
    for ident, image in parts.items():
        path = out / f"{ident}.png"
        pngio.write(path, image)
        (records / f"{ident}.json").write_text(json.dumps(record_of(ident, owner[ident], frames, path), indent=2) + "\n", encoding="utf-8")
    print(f"SMALL KIT build PASS ({len(parts)} parts written to {out})")
    return 0


def sheet_rows(local: Path, frames: Path, plate_dir: Path, parts: dict[str, Image], buttons: list[Small]) -> list[tuple[str, list[tuple[str, Image, Image | None]]]]:
    rows = []
    for b in buttons:
        plate = read_plate(plate_dir, b)
        if plate is None and b.plate == "dome":
            plate = round_plate(b, local)
        if plate is None:
            continue
        idle = button_kit.composite(plate, parts[f"glyph.{b.name}"])
        shots = [("idle", upscaled(read_frame(frames, b, b.frames["idle"]), b.master), idle)]
        for state in STATES:
            if b.frames.get(state) is not None and f"plate.{b.name}_{state}" in parts:
                shots.append((state, upscaled(read_frame(frames, b, b.frames[state]), b.master), button_kit.composite(idle, parts[f"plate.{b.name}_{state}"])))
        rows.append((b.name, shots))
    return rows


def cmd_sheet(args: argparse.Namespace) -> int:
    need_magick()
    local = args.local_root
    frames = args.frames or local / "census" / "frames"
    plate_dir = args.plate_dir or local / "masters"
    buttons = selected(args.only)
    parts = build_parts(frames, buttons, args.pack, args.shape)
    rows = sheet_rows(local, frames, plate_dir, parts, buttons)
    if not rows:
        raise SmallError("no button has its plate yet: nothing to show (run `plates`, fit the Codex plates)")
    with tempfile.TemporaryDirectory() as tmp:
        work = Path(tmp)
        lines = []
        for k, (name, shots) in enumerate(rows):
            cells = []
            for j, (label, before, after) in enumerate(shots):
                for kind, image in (("original", before), ("kit", after)):
                    if image is None:
                        continue
                    path = work / f"{k}-{j}-{kind}.png"
                    pngio.write(path, image)
                    big = work / f"{k}-{j}-{kind}-big.png"
                    subprocess.run(["magick", str(path), "-background", "#3c3c3c", "-flatten", "-filter", "point", "-resize", f"{args.zoom * 100}%", str(big)], check=True)
                    labelled = work / f"{k}-{j}-{kind}-lab.png"
                    subprocess.run(["magick", str(big), "-gravity", "North", "-background", "#202020", "-fill", "white", "-font", LABEL_FONT, "-pointsize", "14", "-splice", "0x22", "-annotate", "+0+3", f"{name} {label} {kind}", str(labelled)], check=True)
                    cells.append(labelled)
            row = work / f"row-{k}.png"
            subprocess.run(["magick", *[str(c) for c in cells], "-background", "#202020", "-gravity", "North", "+append", "-bordercolor", "#202020", "-border", "6", str(row)], check=True)
            lines.append(row)
        args.out.parent.mkdir(parents=True, exist_ok=True)
        subprocess.run(["magick", *[str(r) for r in lines], "-background", "#202020", "-gravity", "West", "-append", str(args.out)], check=True)
    print(f"SMALL KIT sheet PASS ({len(rows)} buttons) -> {args.out}")
    return 0


def self_test() -> int:
    mask = [False] * 25
    for i in (7, 11, 12, 13, 17):
        mask[i] = True
    alpha = silhouette(mask, (5, 5), (20, 20))
    assert max(alpha) > 0.95 and min(alpha) == 0.0, "the symbol's silhouette must be solid inside and clear outside"
    assert len(components(mask, 5, 5)) == 1
    print("SMALL KIT self-test PASS")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("plan", "refs", "plates", "build", "sheet", "cut"):
        s = sub.add_parser(name)
        s.add_argument("--local-root", type=Path, default=None)
        if name != "plan":
            s.add_argument("--frames", type=Path)
            s.add_argument("--only")
            s.add_argument("--shape", choices=("mask", "pack"), help="where the symbols' shapes are read, whatever the table says")
            s.add_argument("--pack", type=Path, default=PACK_ROOT, help="the installed HD pack (the first source of a symbol's shape)")
        if name in ("plates", "build"):
            s.add_argument("--out", type=Path)
        if name in ("build", "cut"):
            s.add_argument("--dry-run", action="store_true")
        if name == "sheet":
            s.add_argument("--out", type=Path, required=True)
            s.add_argument("--plate-dir", type=Path)
            s.add_argument("--zoom", type=int, default=4)
    sub.add_parser("self-test")
    args = parser.parse_args(argv)
    try:
        if args.command == "self-test":
            return self_test()
        args.local_root = args.local_root or prompt_library.default_local_root()
        return {"plan": cmd_plan, "refs": cmd_refs, "cut": cmd_cut, "plates": cmd_plates, "build": cmd_build, "sheet": cmd_sheet}[args.command](args)
    except (SmallError, pngio.PngError, OSError, subprocess.CalledProcessError) as error:
        print(f"SMALL KIT FAIL: {error}")
        return 2


if __name__ == "__main__":
    sys.exit(main())
