#!/usr/bin/env python3
"""ARC-UI-3206 and ARC-UI-3323 (batch B-BUTTONS, slice 6): the small buttons (scroll arrows, plus / minus, round skill and slider buttons, follow scrolls and arrows;
ARC-UI-3272: the schematics skill button and arrows and the follower cycle arrows) as kit parts, in six families (owner 2026-11-25 and 2026-11-26).

* `rect` (the owner kept these from the old HD pack, the installed `interface-redraw-4x`, 4 px per original cell): the stone Add / Remove (647, 648) and the follower arrows (501, 502): the kit part IS the pack's frame of each state at the master size, opaque.
* `paper` (568, the schematics skill button): a Codex stone slab and on it the Codex parchment with its symbol, cut along the torn edge into the original's place; the lit and the pressed looks by rule.
* `ink` (372, 373, the schematics page arrows): Codex draws the ink alone on white; it is cut out, put where the original has it, and the states are made by rule.
* `wedge` (827, 828, the follower cycle arrows): the olive Codex plate of #3323 without its symbol, cut to the button's shape, and a straight-edged triangle by rule, one full face per state.
* `dome` (624, 625, 667, 668): by rule, no pack and no Codex: the accepted round face of the bottom strip's buttons (#3202) reduced under the brass ring of the sockets (#2481), the symbol (red checkered cells) by rule E from the original's own frames,
  the hover and pressed overlays as in `round_kit.py`; all four buttons share the plate, so every such button looks the same.
* `codex` (804, 805, 317, 318, 506 to 509, 257, 258): generated. One sheet of cells per family (the original frames enlarged and the neighbouring pack frames as the style sample are the input; the sheet is generated large), each cell cut by equal
  division and reduced to the master size; the olive plus and minus, the follow scrolls and the skill-point buttons are opaque, the scroll arrows (a triangle on a flat magenta ground) are cut from the ground.

    python3 -B tools/steamwoven_ui/small_kit.py plan   [--pack DIR]
    python3 -B tools/steamwoven_ui/small_kit.py build  [--only NAME,...] [--out DIR] [--dry-run] [--pack DIR] [--local-root DIR] [--sheets DIR] [--sp-variant sp-a|sp-b]
    python3 -B tools/steamwoven_ui/small_kit.py sheet  --out FILE [--only NAME,...] [--zoom N] ...   (the composite original idle, pressed | new idle, hover, pressed, English labels, needs ImageMagick)
    python3 -B tools/steamwoven_ui/small_kit.py self-test

Per button (TRD SWI-KIT-5; the runtime's convention `SteamwovenKitComposer.ButtonLayers`: face, over it the glyph, over both the overlay `<face>_hover` or `<face>_pressed`):

* `plate.<name>`: the idle face at the plate's master size.
* `plate.<name>_hover`, `plate.<name>_pressed`: the face of that state; a `rect`, `paper`, `wedge` or `codex` part is a full opaque frame (laid over the idle plate it replaces it); an `ink` frame has the idle frame's alpha and is meant to take the idle frame's place (laid over it, its soft edge doubles; #3341), a `dome` part is an overlay (the lit symbol, the darkened plate) clipped to the plate.
* `glyph.<name>`: the symbol of a `dome` button (rule E); an EMPTY layer at the plate's size for the others (the symbol is in the face; the runtime composes a face only with a glyph of its size).

The shared soft shadow of round parts (#3249) is a separate runtime layer, not baked into these parts. The Codex sheets are copied to `local/steamwoven-ui/small-sheets/<sheet>.png` with the runner's provenance record next to them.
Nothing here holds art: every picture is read from the installed pack, the census frames or the sheets and written to the ignored `local/steamwoven-ui/`. Standard library only (`pngio.py`, `button_kit.py`); ImageMagick only for `sheet`.
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
    from . import button_kit, fit, pngio, prompt_library
except ImportError:  # run as a script
    sys.path.insert(0, str(HERE))
    import button_kit  # type: ignore
    import fit  # type: ignore
    import pngio  # type: ignore
    import prompt_library  # type: ignore

Image = pngio.Image
TOOL_VERSION = 2
MANIFEST = HERE / "kit.json"
PACK_ROOT = Path.home() / "Library" / "Application Support" / "Arcanum Steamwoven Game" / "hd-packs" / "interface-redraw-4x"   # the installed pack (read, never written)
PACK_SCALE = 4               # an HD pack frame is this many times the original
LABEL_FONT = button_kit.LABEL_FONT


class SmallError(Exception):
    pass


@dataclass(frozen=True)
class Small:
    name: str                       # the plate's name in the manifest (`plate.<name>`) and the art's name in the pack's index
    art: int
    folder: str                     # the census frame folder (`<art>-<name>`)
    family: str                     # `rect` (the pack's frame, opaque, as it is), `paper` and `ink` (Codex cut out and states by rule) and `wedge` (the olive Codex plate of #3323 and an analytic triangle), the three of ARC-UI-3272, `dome` (built by rule from the accepted round plate of #3202, a brass ring and the symbol by rule E), `codex` (generated, owner 2026-11-25)
    units: tuple[int, int]          # the original frame, in cells
    master: tuple[int, int]         # the kit master of the plate, in px (kit.json)
    frames: dict = field(default_factory=lambda: {"idle": 0, "pressed": 1, "hover": 2})   # state -> frame number (a state without a frame has no overlay)


def _dome(name: str, art: int, folder: str) -> Small:
    return Small(name, art, folder, "dome", (29, 30), (102, 108))


def _stone(name: str, art: int, folder: str) -> Small:
    return Small(name, art, folder, "rect", (32, 32), (114, 114))


SMALL = (
    _dome("skiladdbut", 624, "624-SkilAddBut"),
    _dome("skilminusbut", 625, "625-SkilMinusBut"),
    _dome("sldrbutt_l_arrow", 667, "667-SldrButt_L_Arrow"),
    _dome("sldrbutt_r_arrow", 668, "668-SldrButt_R_Arrow"),
    _stone("spelltech_add", 647, "647-SpellTech_Add"),
    _stone("spelltech_minus", 648, "648-SpellTech_Minus"),
    Small("followerarrow_dwn", 501, "501-FollowerArrow_DWN", "rect", (67, 13), (240, 48), frames={"idle": 0}),
    Small("followerarrow_up", 502, "502-FollowerArrow_UP", "rect", (67, 13), (240, 48), frames={"idle": 0}),
    Small("char_schem_skills", 568, "568-char_Schem_Skills", "paper", (32, 32), (114, 114)),
    Small("schm_larrow", 372, "372-Schm_LArrow", "ink", (18, 22), (66, 78)),
    Small("schm_rarrow", 373, "373-Schm_RArrow", "ink", (18, 22), (66, 78)),
    Small("followercycleleft", 827, "827-FollowerCycleLeft", "wedge", (12, 19), (42, 66)),
    Small("followercycleright", 828, "828-FollowerCycleRight", "wedge", (12, 19), (42, 66)),
)
# the buttons the owner rejected from the pack (2026-11-25, #3323) are made by Codex (family `codex`): the olive plus and minus, the scroll arrows, the follow scrolls and the skill-point plus and minus
GENERATED = (
    Small("oldchar_minus", 804, "804-OldChar_Minus", "codex", (13, 10), (48, 36)),
    Small("oldchar_plus", 805, "805-OldChar_Plus", "codex", (13, 10), (48, 36)),
    Small("scrllup", 317, "317-ScrllUP", "codex", (11, 15), (42, 54)),
    Small("scrlldwn", 318, "318-ScrllDWN", "codex", (11, 14), (42, 48)),
    Small("follow_scroll_up", 506, "506-Follow_Scroll_up", "codex", (33, 13), (120, 48), frames={"idle": 0, "pressed": 1}),
    Small("follow_scroll_up_off", 507, "507-Follow_Scroll_up_OFF", "codex", (33, 13), (120, 48), frames={"idle": 0}),
    Small("follow_scroll_dwn", 508, "508-Follow_Scroll_dwn", "codex", (34, 13), (120, 48), frames={"idle": 0, "pressed": 1}),
    Small("follow_scroll_dwn_off", 509, "509-Follow_Scroll_dwn_OFF", "codex", (34, 13), (120, 48), frames={"idle": 0}),
    Small("sp_minus", 257, "257-SP_Minus", "codex", (22, 13), (78, 48), frames={"idle": 0, "pressed": 1}),
    Small("sp_plus", 258, "258-SP_Plus", "codex", (22, 13), (78, 48), frames={"idle": 0, "pressed": 1}),
)


def button_named(name: str) -> Small:
    for b in (*SMALL, *GENERATED):
        if b.name == name:
            return b
    raise SmallError(f"no small button {name!r}; the buttons are {[b.name for b in (*SMALL, *GENERATED)]}")


def selected(only: str | None) -> list[Small]:
    return [button_named(n) for n in only.split(",")] if only else [*SMALL, *GENERATED]


# --- the pack's frames -------------------------------------------------------------------------------------------------------

def pack_index(pack: Path | None) -> dict[tuple[str, int], str]:
    """(art name, frame number) -> the frame's path in the pack, from its `index.tsv` (the first row of an art wins)."""
    index = pack / "index.tsv" if pack else None
    if index is None or not index.is_file():
        raise SmallError(f"no pack index {index}: the pack-first parts are read from the installed HD pack (--pack names another)")
    rows: dict[tuple[str, int], str] = {}
    for line in index.read_text(encoding="utf-8").splitlines():
        cols = line.split("\t")
        if len(cols) > 4 and cols[3].isdigit():
            rows.setdefault((cols[2], int(cols[3])), cols[4])
    return rows


def pack_frame(pack: Path, rows: dict[tuple[str, int], str], b: Small, number: int) -> tuple[Image, Path] | None:
    """The pack's frame `number` of the button (4 px per cell), or None when the pack has none."""
    rel = rows.get((b.name, number))
    if rel is None or not (pack / rel).is_file():
        return None
    image = pngio.read(pack / rel)
    expected = (b.units[0] * PACK_SCALE, b.units[1] * PACK_SCALE)
    if (image.w, image.h) != expected:
        raise SmallError(f"{rel}: {image.w} x {image.h}, the art {b.art} at {PACK_SCALE} x is {expected[0]} x {expected[1]}")
    return image, pack / rel


def read_frame(frames: Path, b: Small, number: int) -> Image:
    path = frames / b.folder / f"f{number}.png"
    if not path.is_file():
        raise SmallError(f"no frame {path}")
    image = pngio.read(path)
    if (image.w, image.h) != b.units:
        raise SmallError(f"{path}: {image.w} x {image.h}, the art {b.art} is {b.units[0]} x {b.units[1]}")
    return image


# --- resampling ---------------------------------------------------------------------------------------------------------------------------------------------------

def resample(image: Image, w: int, h: int) -> Image:
    """The picture at another size by area-weighted box averaging (premultiplied), made for a reduction."""
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


def button_parts(pack: Path, rows: dict[tuple[str, int], str], b: Small) -> tuple[dict[str, Image], dict[str, Path]]:
    """`plate.<name>` (the idle frame), `plate.<name>_hover` / `_pressed` for the states the art has, the empty `glyph.<name>`; and the pack file each plate came from. A kept pack frame is opaque and at the master size as it is."""
    found = {state: pack_frame(pack, rows, b, number) for state, number in b.frames.items()}
    missing = [state for state, f in found.items() if f is None]
    if missing:
        raise SmallError(f"{b.name}: the pack has no frame for {missing} (the pack-first parts need every state)")
    parts: dict[str, Image] = {}
    sources: dict[str, Path] = {}
    for state in b.frames:
        frame, path = found[state]  # type: ignore[misc]
        ident = f"plate.{b.name}" if state == "idle" else f"plate.{b.name}_{state}"
        parts[ident] = resample(frame, *b.master)
        sources[ident] = path
    parts[f"glyph.{b.name}"] = Image.blank(*b.master)
    return parts, sources


# --- the dome family (owner 2026-11-25): the accepted round plate of #3202 + the brass ring + the symbol by rule E --------------------------------------------------

DOME_FLAT = 10.5      # cells from the middle: the symbol is looked for inside it (outside it is the ring)
DOME_OWN = 11.5       # cells: what a symbol or a glow may take
DOME_PLATE = "plate.combat_button"     # the accepted round face of the bottom strip (#3202, glossy arc baked in), 138 x 138 px
DOME_RING = "hardware.hud_ring"        # the brass setting of the round sockets (#2481), 198 x 192 px
DOME_SIZE = 84                         # px: the face's diameter inside the ring (the ring's lip covers its edge)


def dome_radius(x: int, y: int, w: int, h: int) -> float:
    return math.hypot(x - (w - 1) / 2.0, y - (h - 1) / 2.0)


def over(dst: Image, src: Image, ox: int, oy: int) -> None:
    """`src` laid over `dst` at (ox, oy), straight alpha."""
    for y in range(src.h):
        for x in range(src.w):
            dx, dy = x + ox, y + oy
            if not (0 <= dx < dst.w and 0 <= dy < dst.h):
                continue
            so, do = (y * src.w + x) * 4, (dy * dst.w + dx) * 4
            sa = src.data[so + 3] / 255.0
            if sa <= 0:
                continue
            da = dst.data[do + 3] / 255.0
            oa = sa + da * (1 - sa)
            for k in range(3):
                dst.data[do + k] = int(round((src.data[so + k] * sa + dst.data[do + k] * da * (1 - sa)) / oa))
            dst.data[do + 3] = int(round(oa * 255))


def dome_plate(local: Path, master: tuple[int, int]) -> Image:
    """The plate of a dome button: the accepted round face of the bottom strip, reduced and centred, the brass ring of the sockets over it."""
    face = pngio.read(local / "masters" / f"{DOME_PLATE}.png")
    ring = pngio.read(local / "masters" / f"{DOME_RING}.png")
    mw, mh = master
    out = Image.blank(mw, mh)
    over(out, resample(face, DOME_SIZE, DOME_SIZE), (mw - DOME_SIZE) // 2, (mh - DOME_SIZE) // 2)
    rh = round(ring.h * mw / ring.w)
    over(out, resample(ring, mw, rh), 0, (mh - rh) // 2)
    return out


RED_DE = 25           # a cell whose red exceeds its green by this much is the (checkered red) symbol; the olive dome is the other way round


def symbol_cells(frame: Image) -> list[bool]:
    """The cells of the red checkered symbol inside the dome (the frame's own colours: red above green)."""
    w, h = frame.w, frame.h
    return [frame.data[i * 4 + 3] > 0 and dome_radius(i % w, i // w, w, h) < DOME_FLAT and frame.data[i * 4] - frame.data[i * 4 + 1] >= RED_DE for i in range(w * h)]


def inpaint(frame: Image, hole: list[bool]) -> Image:
    """The frame with the `hole` cells filled from their surroundings (the dome's own shading, with the symbol taken out), pass by pass from the rim of the hole inward."""
    w, h = frame.w, frame.h
    out = Image(w, h, bytearray(frame.data))
    known = [not x for x in hole]
    for _ in range(w + h):
        fresh = []
        for i in range(w * h):
            if known[i]:
                continue
            x, y = i % w, i // w
            near = [yy * w + xx for yy in range(max(0, y - 1), min(h, y + 2)) for xx in range(max(0, x - 1), min(w, x + 2)) if known[yy * w + xx]]
            if near:
                fresh.append((i, bytes(round(sum(out.data[j * 4 + k] for j in near) / len(near)) for k in range(3))))
        if not fresh:
            break
        for i, c in fresh:
            out.data[i * 4:i * 4 + 3] = c
            known[i] = True
    return out


def symbol_shape(frame: Image, others: list[Image]) -> tuple[list[bool], Image]:
    """(the symbol's mask: the red cells of this frame and of the other states; the frame with the symbol taken out, the dome's own shading and the relief round the symbol kept, so only the symbol differs from it)."""
    red = [any(v) for v in zip(*(symbol_cells(f) for f in [frame, *others]))]
    return red, inpaint(frame, red)


RELIEF_SHIFT = 1.0    # px: the soft relief of the symbol's slot, a shadow up and to the left, a light edge down and to the right (the original's slot, drawn smooth instead of in black and grey cells)
RELIEF_BLUR = 2       # box-blur radius (twice) in px


def blurred(plane: list[float], w: int, h: int, radius: int, shift: tuple[float, float]) -> list[float]:
    """The alpha plane moved by `shift` (rounded, px) and box-blurred twice."""
    sx, sy = round(shift[0]), round(shift[1])
    cur = [0.0] * (w * h)
    for y in range(h):
        for x in range(w):
            xx, yy = x - sx, y - sy
            if 0 <= xx < w and 0 <= yy < h:
                cur[y * w + x] = plane[yy * w + xx]
    for _ in range(2):
        for horizontal in (True, False):
            nxt = [0.0] * (w * h)
            for y in range(h):
                for x in range(w):
                    acc, cnt = 0.0, 0
                    for d in range(-radius, radius + 1):
                        xx, yy = (x + d, y) if horizontal else (x, y + d)
                        if 0 <= xx < w and 0 <= yy < h:
                            acc += cur[yy * w + xx]
                        cnt += 1
                    nxt[y * w + x] = acc / cnt
            cur = nxt
    return cur


def only_symbol(glyph: Image) -> Image:
    """The glyph without the pixels that are not the symbol's red (the Gaussian outline of rule E reaches into the dark and light relief cells beside the symbol and takes their colour at the edge)."""
    out = Image(glyph.w, glyph.h, bytearray(glyph.data))
    for i in range(glyph.w * glyph.h):
        if glyph.data[i * 4] - glyph.data[i * 4 + 1] < RED_DE // 2:
            out.data[i * 4 + 3] = 0
    return out


def with_relief(glyph: Image) -> Image:
    """The symbol with the soft relief of its recess under it (shadow at the upper left, a faint light edge at the lower right), so the slot is smooth and not a staircase of dark and light cells."""
    w, h = glyph.w, glyph.h
    plane = [glyph.data[i * 4 + 3] / 255.0 for i in range(w * h)]
    out = Image.blank(w, h)
    for colour, shift, strength in (((150, 145, 90), (RELIEF_SHIFT, RELIEF_SHIFT), 0.10), ((0, 0, 0), (-RELIEF_SHIFT, -RELIEF_SHIFT), 0.55)):
        soft = blurred(plane, w, h, RELIEF_BLUR, shift)
        layer = Image.blank(w, h)
        for i, a in enumerate(soft):
            layer.data[i * 4:i * 4 + 4] = bytes((*colour, int(round(min(1.0, a) * strength * 255))))
        over(out, layer, 0, 0)
    over(out, glyph, 0, 0)
    return out


def dome_parts(local: Path, frames: Path, group: list[Small]) -> dict[str, Image]:
    """`plate.<name>` (shared), `glyph.<name>`, `plate.<name>_hover`, `plate.<name>_pressed` of every dome button, the last three by rule E from the original's own frames (as `round_kit.round_parts`): the symbol is the
    red checkered cells and their dark outline (a mask read from the three states), over the dome with the symbol taken out, so the dome's own shading is not mistaken for symbol."""
    plate = dome_plate(local, group[0].master)
    parts: dict[str, Image] = {}
    for b in group:
        idle, pressed, hover = (read_frame(frames, b, k) for k in (0, 1, 2))
        mask, base = symbol_shape(idle, [hover, pressed])
        own = lambda everything: (lambda x, y: idle.data[(y * idle.w + x) * 4 + 3] > 0 and (everything or dome_radius(x, y, idle.w, idle.h) <= DOME_OWN))  # noqa: E731
        parts[f"plate.{b.name}"] = plate
        parts[f"glyph.{b.name}"] = with_relief(only_symbol(button_kit.render_crisp(idle, base, False, pixels=b.master, owned_cell=own(False), silhouette_mask=mask)))
        parts[f"plate.{b.name}_hover"] = button_kit.render_crisp(hover, idle, True, pixels=b.master, owned_cell=own(False), clip=plate)
        parts[f"plate.{b.name}_pressed"] = button_kit.render_crisp(pressed, idle, True, pixels=b.master, owned_cell=own(True), shade=hover, clip=plate, solid_cell=own(False))
    return parts


# --- the wedge family (ARC-UI-3272): the follower cycle arrows, whose pack frames are broken ---------------------------------------------------------------------

WEDGE_SAMPLES = 8     # the triangle's coverage of a master pixel is sampled on a WEDGE_SAMPLES x WEDGE_SAMPLES grid


@dataclass(frozen=True)
class Wedge:
    """A sideways isosceles triangle in original cells: the tip and the base's column edge, the axis row and the half height at the base."""
    tip: float
    base: float
    axis: float
    half: float
    colour: tuple[int, int, int]

    def holds(self, x: float, y: float) -> bool:
        along = (x - self.tip) / (self.base - self.tip)
        return 0.0 <= along <= 1.0 and abs(y - self.axis) <= self.half * along


def red_cells(frame: Image) -> list[bool]:
    """The cells of the flat red triangle (red above green; the plate is olive)."""
    return [frame.data[i * 4 + 3] > 0 and frame.data[i * 4] - frame.data[i * 4 + 1] >= RED_DE for i in range(frame.w * frame.h)]


def fit_wedge(frame: Image) -> Wedge:
    """The straight-edged triangle of a frame's staircase of red cells: it spans the cells' columns, stands on their middle row and covers the same area; the tip is at the end with the shorter column."""
    cells = [(i % frame.w, i // frame.w) for i, red in enumerate(red_cells(frame)) if red]
    columns: dict[int, int] = {}
    for x, _ in cells:
        columns[x] = columns.get(x, 0) + 1
    first, last = min(columns, default=0), max(columns, default=0)
    if first == last or columns[first] == columns[last]:
        raise SmallError("no sideways red triangle in the frame")
    left = columns[first] < columns[last]
    rows = [y for _, y in cells]
    colour = tuple(round(sum(frame.pixel(x, y)[k] for x, y in cells) / len(cells)) for k in range(3))
    return Wedge(first if left else last + 1, last + 1 if left else first, (min(rows) + max(rows) + 1) / 2.0, len(cells) / (last + 1 - first), colour)  # type: ignore[arg-type]


def wedge_layer(wedge: Wedge, units: tuple[int, int], master: tuple[int, int]) -> Image:
    out = Image.blank(*master)
    sx, sy, n = units[0] / master[0], units[1] / master[1], WEDGE_SAMPLES
    for py in range(master[1]):
        for px in range(master[0]):
            hits = sum(wedge.holds((px + (i + 0.5) / n) * sx, (py + (j + 0.5) / n) * sy) for j in range(n) for i in range(n))
            if hits:
                o = (py * master[0] + px) * 4
                out.data[o:o + 4] = bytes((*wedge.colour, round(255 * hits / (n * n))))
    return out


# the plate: the accepted olive Codex plate of the minus button (#3323, sheet `ol`) with the symbol left out, put together to the cycle button's shape
PLATE_SOURCE = ("oldchar_minus", "idle")
PLATE_SOURCE_UNITS = (13, 10)          # the minus button's original frame: the plate is reduced at that button's scale, so its bevel and pins keep their size
PLATE_CORNER = 100                     # sheet px: the square corner (bevel and pin) kept as it is
PLATE_TEXTURE = (0.20, 0.60, 0.80, 0.85)   # share of the cell (x0, y0, x1, y1): bare plate under the symbol's slot, the texture of the middle


def transposed(image: Image) -> Image:
    """Mirrored over the diagonal: a wide texture patch becomes a tall one and the light stays at the upper left."""
    out = Image.blank(image.h, image.w)
    for y in range(image.h):
        for x in range(image.w):
            out.data[(x * image.h + y) * 4:(x * image.h + y) * 4 + 4] = image.data[(y * image.w + x) * 4:(y * image.w + x) * 4 + 4]
    return out


def paste(dst: Image, src: Image, ox: int, oy: int) -> None:
    for y in range(src.h):
        dst.data[((oy + y) * dst.w + ox) * 4:((oy + y) * dst.w + ox + src.w) * 4] = src.data[y * src.w * 4:(y + 1) * src.w * 4]


def empty_plate(cell: Image, units: tuple[int, int], master: tuple[int, int]) -> Image:
    """A plate of the button's shape from the sheet cell: its four corners as they are, its sides cut or stretched along themselves, the middle from the bare plate under the symbol; then reduced to the master size."""
    w = round(master[0] * cell.w / (PLATE_SOURCE_UNITS[0] * master[1] / units[1]))
    h = round(master[1] * cell.h / (PLATE_SOURCE_UNITS[1] * master[1] / units[1]))
    c = PLATE_CORNER
    if min(w, h, cell.w, cell.h) <= 2 * c or w > cell.w:
        raise SmallError(f"the plate cell {cell.w} x {cell.h} cannot be cut to {w} x {h} with corners of {c}")
    x0, y0, x1, y1 = PLATE_TEXTURE
    patch = cell.crop(round(cell.w * x0), round(cell.h * y0), round(cell.w * (x1 - x0)), round(cell.h * (y1 - y0)))
    out = Image.blank(w, h)
    paste(out, resample(transposed(patch), w - 2 * c, h - 2 * c), c, c)
    for top, sy in ((0, 0), (h - c, cell.h - c)):
        paste(out, cell.crop(c, sy, w - 2 * c, c), c, top)
        for left, sx in ((0, 0), (w - c, cell.w - c)):
            paste(out, cell.crop(sx, sy, c, c), left, top)
    for left, sx in ((0, 0), (w - c, cell.w - c)):
        paste(out, resample(cell.crop(sx, c, c, cell.h - 2 * c), c, h - 2 * c), left, c)
    return resample(out, *master)


def luminance(p: tuple) -> float:
    return 0.299 * p[0] + 0.587 * p[1] + 0.114 * p[2]


def pressed_shade(idle: Image, pressed: Image, plate: Image) -> Image:
    """The plate pressed in: darkened in straight bands where the original's pressed plate is darker than its idle plate (the triangles' cells left out): a cell takes the darker of its row's and its column's median ratio."""
    a, b = (inpaint(f, red_cells(f)) for f in (idle, pressed))
    cell = [[min(1.0, (luminance(b.pixel(x, y)) + 1.0) / (luminance(a.pixel(x, y)) + 1.0)) for x in range(a.w)] for y in range(a.h)]
    rows = [sorted(row)[a.w // 2] for row in cell]
    columns = [sorted(row[x] for row in cell)[a.h // 2] for x in range(a.w)]
    ratio = Image(a.w, a.h, bytearray(a.w * a.h * 4))
    for y in range(a.h):
        for x in range(a.w):
            v = round(255 * min(rows[y], columns[x]))
            ratio.data[(y * a.w + x) * 4:(y * a.w + x) * 4 + 4] = bytes((v, v, v, 255))
    smooth = resample(ratio, plate.w, plate.h)
    out = Image(plate.w, plate.h, bytearray(plate.data))
    for i in range(plate.w * plate.h):
        for k in range(3):
            out.data[i * 4 + k] = round(out.data[i * 4 + k] * smooth.data[i * 4] / 255)
    return out


def wedge_face(frame: Image, plate: Image) -> Image:
    """The face of one state: the plate, the straight-edged triangle of that state's original frame over it."""
    out = Image(plate.w, plate.h, bytearray(plate.data))
    over(out, wedge_layer(fit_wedge(frame), (frame.w, frame.h), (plate.w, plate.h)), 0, 0)
    return out


def wedge_parts(frames: Path, cell: Image, group: list[Small]) -> dict[str, Image]:
    """`plate.<name>[_hover|_pressed]` of every wedge button, each a full face, and the empty `glyph.<name>`: the plate from the sheet cell (pressed in by rule for the pressed state), the triangle from that state's frame."""
    parts: dict[str, Image] = {}
    for b in group:
        plate = empty_plate(cell, b.units, b.master)
        idle = read_frame(frames, b, b.frames["idle"])
        for state, number in b.frames.items():
            ident = f"plate.{b.name}" if state == "idle" else f"plate.{b.name}_{state}"
            frame = read_frame(frames, b, number)
            parts[ident] = wedge_face(frame, pressed_shade(idle, frame, plate) if state == "pressed" else plate)
        parts[f"glyph.{b.name}"] = Image.blank(*b.master)
    return parts


# --- the paper and ink families (owner 2026-11-26, ARC-UI-3272): Codex draws the object alone, large; it is cut along its own edge and put where the original has it ---------

STRAY = 3              # a lone solid pixel left by the keying (a corner of the ground) is not the object's edge
PAPER_WARM = 25        # an original cell whose red exceeds its blue by this much is parchment or symbol; the stone round it is grey
PAPER_LIGHT = 1.18     # the sheet's tone: the hue of the original sheet's mean colour, this much lighter (light cream; ARC-UI-3272, owner 2026-11-26)
PAPER_TEXTURE = 0.5    # the share of the generated paper's own light and dark that is kept (the owner asked for a weaker texture)
LIT_RIM = 2            # master px of the lit symbol's solid outline
LIT_DARK = 0.68        # the darker cell of a lit symbol, as a share of the bright one's colour
LIT_SAMPLES = 4        # sub-samples a side for a cell edge inside a master pixel
PAPER_SHEET = "plate-schem-skills"     # left the idle look, right the lit one
PAPER_SLAB = "plate-schem-slab"        # the raised stone slab, bare
INK_SHEET = "glyph-schem-arrows"       # left the left arrow, right the right arrow, dark ink on white
INK_DARK = 70          # an original cell darker than this is ink of the dark state; both states take their colour from those cells
# the generator copied a one-cell stair of the original at both arrow heads: along the head's slanting edge (x from, x to, a point of the edge, its slope; px of the sheet) the ink is cut to that line and the notch under it filled.
# The lines are measured on one sheet, so only that file is accepted: a new generation needs new lines.
INK_SHEET_SHA256 = "b5537d5ed85b421766ac6aabce41d434da0b89428275e14f418586f127e699ab"
INK_TRIM = {"schm_larrow": (180, 300, 30, 306, -1.031), "schm_rarrow": (1305, 1425, 1415, 143, 1.025)}
INK_NOTCH = 80         # px: the deepest notch under the line that is filled


def cells_box(frame: Image, keep) -> tuple[int, int, int, int]:
    """(x0, y0, x1, y1) of the original cells `keep` accepts."""
    cells = [(x, y) for y in range(frame.h) for x in range(frame.w) if keep(frame.pixel(x, y))]
    if not cells:
        raise SmallError("the original frame has no cell of the object")
    xs, ys = [c[0] for c in cells], [c[1] for c in cells]
    return min(xs), min(ys), max(xs) + 1, max(ys) + 1


def placed(cut: Image, box: tuple[int, int, int, int], units: tuple[int, int], master: tuple[int, int]) -> Image:
    """The cut object (its opaque bounding box, a column or row of fewer than `STRAY` solid pixels not counted) reduced into the place the original has it, on a clear master canvas."""
    solid = [v >= 128 for v in cut.data[3::4]]
    xs = [x for x in range(cut.w) if sum(solid[x::cut.w]) >= STRAY]
    ys = [y for y in range(cut.h) if sum(solid[y * cut.w:(y + 1) * cut.w]) >= STRAY]
    if not xs or not ys:
        raise SmallError("the sheet cell has no object")
    cropped = cut.crop(min(xs), min(ys), max(xs) + 1 - min(xs), max(ys) + 1 - min(ys))
    x0, x1 = (round(box[k] * master[0] / units[0]) for k in (0, 2))
    y0, y1 = (round(box[k] * master[1] / units[1]) for k in (1, 3))
    out = Image.blank(*master)
    paste(out, resample(cropped, x1 - x0, y1 - y0), x0, y0)
    return out


def cut_from_magenta(cell: Image) -> Image:
    out = Image(cell.w, cell.h, bytearray(cell.data))
    colour, spread = fit.estimate_background(out)
    if not (colour[0] > 200 and colour[2] > 200 and colour[1] < 80) or spread > fit.KEY_SPREAD_MAX:
        raise SmallError("the sheet cell is not on a flat magenta ground")
    fit.key_background(out, colour, spread)
    fit.decontaminate(out, 255, fit.SOURCE_EDGE_RADIUS)
    return out


def is_sheet_cell(p: tuple) -> bool:
    """An original cell of the bare sheet: lit warm tan, not the red or brown symbol and not the grey stone."""
    return p[0] - p[2] >= 40 and p[1] - p[2] >= 22 and p[0] - p[1] < 55 and sum(p[:3]) >= 360


def symbolness(r: int, g: int, b: int) -> float:
    """How much of a generated pixel is the symbol (dark brown, or bright red) rather than the paper: 0 to 1."""
    return min(1.0, max(0.0, (140.0 - luminance((r, g, b))) / 50.0, (r - g - 60.0) / 40.0))


def toned(cut: Image, target: tuple[float, float, float]) -> Image:
    """The cut parchment with its paper moved to the `target` mean colour and its texture kept at `PAPER_TEXTURE`; the symbol's pixels keep their colour."""
    d = cut.data
    paper = [i for i in range(0, len(d), 4) if d[i + 3] == 255 and symbolness(d[i], d[i + 1], d[i + 2]) == 0.0]
    if not paper:
        raise SmallError("the sheet cell has no bare paper")
    mean = [sum(d[i + k] for i in paper) / len(paper) for k in range(3)]
    out = Image(cut.w, cut.h, bytearray(d))
    for i in range(0, len(d), 4):
        if d[i + 3] == 0:
            continue
        w = 1.0 - symbolness(d[i], d[i + 1], d[i + 2])
        for k in range(3):
            v = d[i + k] + w * (target[k] + PAPER_TEXTURE * (d[i + k] - mean[k]) - d[i + k])
            out.data[i + k] = max(0, min(255, round(v)))
    return out


def lit_cells(part: Image, units: tuple[int, int]) -> Image:
    """A lit symbol as the kit draws one (owner 2026-11-26): a solid outline and inside it even cells, a checker one original cell wide, in two tones of its own colour."""
    w, h = part.w, part.h
    red = [part.data[i * 4 + 3] > 0 and symbolness(*part.data[i * 4:i * 4 + 3]) >= 0.5 for i in range(w * h)]
    out = Image(w, h, bytearray(part.data))
    n = LIT_SAMPLES
    for y in range(LIT_RIM, h - LIT_RIM):
        for x in range(LIT_RIM, w - LIT_RIM):
            if not all(red[yy * w + xx] for yy in range(y - LIT_RIM, y + LIT_RIM + 1) for xx in range(x - LIT_RIM, x + LIT_RIM + 1)):
                continue
            dark = sum((int((x + (i + 0.5) / n) * units[0] / w) + int((y + (j + 0.5) / n) * units[1] / h)) % 2 for j in range(n) for i in range(n)) / (n * n)
            for k in range(3):
                o = (y * w + x) * 4 + k
                out.data[o] = round(out.data[o] * (1.0 - dark * (1.0 - LIT_DARK)))
    return out


def is_stone_cell(p: tuple) -> bool:
    """An original cell of the bare stone: grey, neither the parchment nor its symbol."""
    return abs(p[0] - p[2]) < 20


def toned_slab(slab: Image, original: Image) -> Image:
    """The generated stone brought to the original's: each channel scaled so that the face's mean (the middle four fifths) is the mean of the original's stone cells inside its outer ring."""
    stone = [(x, y) for y in range(1, original.h - 1) for x in range(1, original.w - 1) if is_stone_cell(original.pixel(x, y))]
    if not stone:
        raise SmallError("the original frame has no stone cell")
    target = mean_colour(original, stone)
    face = slab.crop(slab.w // 10, slab.h // 10, slab.w * 4 // 5, slab.h * 4 // 5)
    gain = [target[k] * (len(face.data) / 4) / max(1, sum(face.data[k::4])) for k in range(3)]
    out = Image(slab.w, slab.h, bytearray(slab.data))
    for k in range(3):
        out.data[k::4] = bytes(slab.data[k::4]).translate(bytes(min(255, round(v * gain[k])) for v in range(256)))
    return out


def sunk_slab(slab: Image, step: int, edge: tuple[int, int, int]) -> Image:
    """The pressed slab as the original makes it: the raised one `step` px lower (its bottom edge stays), a dark line of `step` px along the top and the left."""
    out = Image(slab.w, slab.h, bytearray(slab.data))
    for y in range(step, slab.h - step):
        out.data[y * slab.w * 4:(y + 1) * slab.w * 4] = slab.data[(y - step) * slab.w * 4:(y - step + 1) * slab.w * 4]
    for y in range(slab.h):
        for x in range(slab.w if y < step else step):
            out.data[(y * slab.w + x) * 4:(y * slab.w + x) * 4 + 3] = bytes(edge)
    return out


def ink_trim(path: Path, name: str) -> tuple:
    if button_kit.sha256_file(path) != INK_SHEET_SHA256:
        raise SmallError(f"{path}: not the arrow sheet its head trim is measured on (INK_SHEET_SHA256)")
    return INK_TRIM[name]


def paper_parts(frames: Path, sheet: Image, slab: Image, b: Small) -> dict[str, Image]:
    """The schematics button as the original builds it: a stone slab, the parchment on it, the symbol on the parchment. Idle and hover lie on the raised slab (the dark symbol, then the lit one), pressed on the sunk slab
    with the lit parchment one original cell lower. Every state is a full opaque frame."""
    original, pressed = (read_frame(frames, b, b.frames[s]) for s in ("idle", "pressed"))
    box = cells_box(original, lambda p: p[0] - p[2] >= PAPER_WARM)
    bare = [(x, y) for y in range(original.h) for x in range(original.w) if is_sheet_cell(original.pixel(x, y))]
    if not bare:
        raise SmallError("the original frame has no bare sheet cell")
    target = tuple(PAPER_LIGHT * v for v in mean_colour(original, bare))
    idle, lit = (placed(toned(cut_from_magenta(sheet.crop(k * (sheet.w // 2), 0, sheet.w // 2, sheet.h)), target), box, b.units, b.master) for k in (0, 1))
    for layer in (idle, lit):
        fit.decontaminate(layer, 255, fit.EDGE_RADIUS)
    lit = lit_cells(lit, b.units)
    raised = resample(toned_slab(slab, original), *b.master)
    step = round(b.master[1] / b.units[1])
    sunk = sunk_slab(raised, step, mean_colour(pressed, [(x, 0) for x in range(pressed.w)]))
    parts = {}
    for ident, base, layer, dy in ((f"plate.{b.name}", raised, idle, 0), (f"plate.{b.name}_hover", raised, lit, 0), (f"plate.{b.name}_pressed", sunk, lit, step)):
        face_image = Image(base.w, base.h, bytearray(base.data))
        over(face_image, layer, 0, dy)
        parts[ident] = face_image
    parts[f"glyph.{b.name}"] = Image.blank(*b.master)
    return parts


def ink_alpha(cell: Image, colour: tuple[int, int, int]) -> Image:
    """Dark ink on white as a layer of one colour: the alpha is how far a pixel is from white toward the ink."""
    lum = [luminance(cell.pixel(x, y)) for y in range(cell.h) for x in range(cell.w)]
    dark = sorted(v for v in lum if v < 128)
    if not dark:
        raise SmallError("the sheet cell has no ink")
    ink = dark[len(dark) // 2]
    out = Image.blank(cell.w, cell.h)
    for i, v in enumerate(lum):
        a = min(1.0, max(0.0, (250.0 - v) / (250.0 - ink - 4.0)))
        if a > 0.03:
            out.data[i * 4:i * 4 + 4] = bytes((*colour, round(255 * a)))
    return out


def mean_colour(frame: Image, cells: list[tuple[int, int]]) -> tuple[int, int, int]:
    return tuple(round(sum(frame.pixel(x, y)[k] for x, y in cells) / len(cells)) for k in range(3))  # type: ignore[return-value]


def trimmed(sheet: Image, trim: tuple) -> Image:
    """The sheet with one straight edge: in the columns of `trim` everything above the line is cleared to white and a notch between the line and the ink under it takes that ink."""
    x0, x1, ax, ay, slope = trim
    out = Image(sheet.w, sheet.h, bytearray(sheet.data))
    for x in range(x0, x1):
        top = max(0, min(sheet.h, math.ceil(ay + slope * (x - ax))))
        for y in range(top):
            out.data[(y * sheet.w + x) * 4:(y * sheet.w + x) * 4 + 4] = b"\xff\xff\xff\xff"
        ink = next((y for y in range(top, min(sheet.h, top + INK_NOTCH)) if luminance(sheet.pixel(x, y)) < 64), None)
        if ink is not None:
            for y in range(top, ink):
                out.data[(y * sheet.w + x) * 4:(y * sheet.w + x) * 4 + 4] = bytes(sheet.pixel(x, ink))
    return out


def ink_parts(frames: Path, sheet: Image, b: Small, trim: tuple | None) -> dict[str, Image]:
    """A page arrow as ink alone: one shape from the sheet in the place of the original's ink, light in the idle state and dark when pointed at or pressed (the colours of the original's own ink cells)."""
    idle, pressed = (read_frame(frames, b, b.frames[s]) for s in ("idle", "pressed"))
    cells = [(x, y) for y in range(pressed.h) for x in range(pressed.w) if luminance(pressed.pixel(x, y)) < INK_DARK]
    box = cells_box(pressed, lambda p: luminance(p) < INK_DARK)
    right = b.name == "schm_rarrow"
    cell = (trimmed(sheet, trim) if trim else sheet).crop(sheet.w - sheet.w // 2 if right else 0, 0, sheet.w // 2, sheet.h)
    parts = {}
    for state, number in b.frames.items():
        colour = mean_colour(read_frame(frames, b, number), cells)
        parts[f"plate.{b.name}" if state == "idle" else f"plate.{b.name}_{state}"] = placed(ink_alpha(cell, colour), box, b.units, b.master)
    parts[f"glyph.{b.name}"] = Image.blank(*b.master)
    return parts


# --- the Codex family (owner 2026-11-25): a sheet of cells generated large, cut, reduced to the part's size ---------------------------------------------------------

@dataclass(frozen=True)
class Sheet:
    id: str                              # the sheet file `<sheets dir>/<id>.png`
    cols: int
    rows: int
    cells: tuple                         # (column, row) -> (button name, state), row by row
    shape: str = "rect"                  # `rect` (opaque) or `triangle` (a triangle button on a flat magenta ground: cut from the ground)


SHEETS = (
    Sheet("ol", 3, 2, (("oldchar_minus", "idle"), ("oldchar_minus", "pressed"), ("oldchar_minus", "hover"), ("oldchar_plus", "idle"), ("oldchar_plus", "pressed"), ("oldchar_plus", "hover"))),
    Sheet("sc", 3, 2, (("scrllup", "idle"), ("scrllup", "pressed"), ("scrllup", "hover"), ("scrlldwn", "idle"), ("scrlldwn", "pressed"), ("scrlldwn", "hover")), shape="triangle"),
    Sheet("fs", 2, 3, (("follow_scroll_up", "idle"), ("follow_scroll_dwn", "idle"), ("follow_scroll_up", "pressed"), ("follow_scroll_dwn", "pressed"), ("follow_scroll_up_off", "idle"), ("follow_scroll_dwn_off", "idle"))),
    Sheet("sp-a", 2, 2, (("sp_minus", "idle"), ("sp_minus", "pressed"), ("sp_plus", "idle"), ("sp_plus", "pressed"))),
    Sheet("sp-b", 2, 2, (("sp_minus", "idle"), ("sp_minus", "pressed"), ("sp_plus", "idle"), ("sp_plus", "pressed"))),
)
SP_VARIANT = "sp-b"                      # the skill-point sheet in use (A grey checkered symbol, B red checkered like the round skill buttons)
TRIANGLE_ERODE = 3                       # px of the cell: the triangle is clipped this far inside the ground (the ground's pink never reaches the part)


def sheet_for(button: str, state: str, sp_variant: str = SP_VARIANT) -> tuple[Sheet, int]:
    for sheet in SHEETS:
        if sheet.id.startswith("sp-") and sheet.id != sp_variant:
            continue
        for k, cell in enumerate(sheet.cells):
            if cell == (button, state):
                return sheet, k
    raise SmallError(f"no sheet cell for {button} {state}")


def is_ground(cell: Image, i: int) -> bool:
    """The flat magenta ground (and its anti-aliased pink): red and blue both well above green."""
    r, g, bl = cell.data[i * 4], cell.data[i * 4 + 1], cell.data[i * 4 + 2]
    return r - g > 60 and bl - g > 60


def triangle_clip(cell: Image) -> Image:
    """The triangle button of a cell cut out of its magenta ground: the ground and the `TRIANGLE_ERODE` px round it become transparent (so the anti-aliased pink of the edge never reaches the part); the colours then come only from the inside."""
    w, h = cell.w, cell.h
    ground = [is_ground(cell, i) for i in range(w * h)]
    e = int(TRIANGLE_ERODE)
    horiz = [False] * (w * h)
    for y in range(h):
        row = ground[y * w:(y + 1) * w]
        prefix = [0]
        for g in row:
            prefix.append(prefix[-1] + (1 if g else 0))
        for x in range(w):
            horiz[y * w + x] = prefix[min(w, x + e + 1)] - prefix[max(0, x - e)] > 0
    out = Image(w, h, bytearray(cell.data))
    for x in range(w):
        prefix = [0]
        for y in range(h):
            prefix.append(prefix[-1] + (1 if horiz[y * w + x] else 0))
        for y in range(h):
            if prefix[min(h, y + e + 1)] - prefix[max(0, y - e)] > 0:
                out.data[(y * w + x) * 4 + 3] = 0
    return out


def sheet_cell(sheet_image: Image, sheet: Sheet, k: int) -> Image:
    cw, ch = sheet_image.w // sheet.cols, sheet_image.h // sheet.rows
    return sheet_image.crop((k % sheet.cols) * cw, (k // sheet.cols) * ch, cw, ch)


def sheet_path(sheets: Path, ident: str) -> Path:
    path = sheets / f"{ident}.png"
    if not path.is_file():
        raise SmallError(f"no sheet {path}: the Codex output of {ident} is copied there (the sheets dir)")
    return path


def codex_parts(sheets: Path, buttons: list[Small], sp_variant: str = SP_VARIANT) -> tuple[dict[str, Image], dict[str, Path]]:
    """`plate.<name>[_hover|_pressed]` of every Codex button cut from its sheet and reduced to the plate's master size, and the empty `glyph.<name>`; the sheet file of each part."""
    parts: dict[str, Image] = {}
    sources: dict[str, Path] = {}
    cache: dict[str, Image] = {}
    for b in buttons:
        for state in b.frames:
            sheet, k = sheet_for(b.name, state, sp_variant)
            path = sheet_path(sheets, sheet.id)
            if sheet.id not in cache:
                cache[sheet.id] = pngio.read(path)
            image = cache[sheet.id]
            cell = sheet_cell(image, sheet, k)
            if sheet.shape == "triangle":
                cell = triangle_clip(cell)
            ident = f"plate.{b.name}" if state == "idle" else f"plate.{b.name}_{state}"
            parts[ident] = resample(cell, *b.master)
            sources[ident] = path
        parts[f"glyph.{b.name}"] = Image.blank(*b.master)
    return parts, sources


def build_parts(args: argparse.Namespace, buttons: list[Small]) -> tuple[dict[str, Image], dict[str, Path | None]]:
    """All parts of the buttons by family: `rect` from the pack's frames, `paper` and `ink` from their Codex sheets, `wedge` by rule on the plate of the Codex sheet `ol`, `dome` by rule from the round plate of #3202, `codex` from the generated sheets. The source file of each part (None for a part made by rule)."""
    parts: dict[str, Image] = {}
    sources: dict[str, Path | None] = {}
    rect = [b for b in buttons if b.family == "rect"]
    if rect:
        rows = pack_index(args.pack)
        for b in rect:
            p, src = button_parts(args.pack, rows, b)
            parts.update(p)
            sources.update(src)
    dome = [b for b in buttons if b.family == "dome"]
    if dome:
        parts.update(dome_parts(args.local_root, args.frames or args.local_root / "census" / "frames", dome))
        sources.update({ident: None for ident in parts if ident.split(".", 1)[1].removesuffix("_hover").removesuffix("_pressed") in {b.name for b in dome}})
    sheets = args.sheets or args.local_root / "small-sheets"
    for b in (b for b in buttons if b.family in ("paper", "ink", "wedge")):
        frames = args.frames or args.local_root / "census" / "frames"
        if b.family == "wedge":
            sheet, k = sheet_for(*PLATE_SOURCE)
            path = sheet_path(sheets, sheet.id)
            p = wedge_parts(frames, sheet_cell(pngio.read(path), sheet, k), [b])
        else:
            path = sheet_path(sheets, PAPER_SHEET if b.family == "paper" else INK_SHEET)
            sheet_image = pngio.read(path)
            if b.family == "paper":
                p = paper_parts(frames, sheet_image, pngio.read(sheet_path(sheets, PAPER_SLAB)), b)
            else:
                p = ink_parts(frames, sheet_image, b, ink_trim(path, b.name))
        parts.update(p)
        sources.update({ident: None if ident.startswith("glyph.") else path for ident in p})
    codex = [b for b in buttons if b.family == "codex"]
    if codex:
        p, src = codex_parts(sheets, codex, args.sp_variant)
        parts.update(p)
        sources.update(src)
    return parts, sources


def owner_of(ident: str) -> Small:
    """The button a part id (`plate.<name>`, `plate.<name>_hover`, `plate.<name>_pressed`, `glyph.<name>`) belongs to."""
    name = ident.split(".", 1)[1]
    for suffix in ("_hover", "_pressed"):
        if name.endswith(suffix) and any(b.name == name[:-len(suffix)] for b in (*SMALL, *GENERATED)):
            name = name[:-len(suffix)]
    return button_named(name)


# --- commands -----------------------------------------------------------------------------------------------------------------

RULES = {
    "rect": "pack-first (owner 2026-11-25): the old HD pack's frame of this state at the master size, opaque (kept by the owner: the stone Add / Remove and the follower arrows)",
    "paper": "Codex and rules (owner 2026-11-26, ARC-UI-3272): a Codex stone slab and on it the Codex parchment (cut from magenta into the original parchment's place, toned by rule); hover = the lit parchment, its symbol an outline with even cells inside; pressed = the slab sunk by rule and the lit parchment one original cell lower",
    "ink": "Codex and rules (owner 2026-11-26, ARC-UI-3272): the arrow's ink drawn on white, the alpha taken from it, put in the place of the original's ink; each state in the colour of the original's ink cells",
    "wedge": "by rule (owner 2026-11-26, ARC-UI-3272): the olive Codex plate of #3323 without its symbol, cut to the button's shape (pressed in by the original's own darkening), and a straight-edged triangle of the original's red cells",
    "dome": "by rule (owner 2026-11-25): the accepted round face of #3202 + the brass ring of the sockets (#2481) + the symbol by rule E from the original's own frames, states as in #3202",
    "codex": "generated (owner 2026-11-25): a sheet of cells from Codex, original and neighbouring pack frames as input, cut and reduced to the master size",
}


def record_of(ident: str, b: Small, source: Path | None, image_path: Path, args: argparse.Namespace) -> dict:
    empty = ident.startswith("glyph.") and b.family != "dome"
    record = {
        "id": ident,
        "tool": "small_kit.py",
        "tool_version": TOOL_VERSION,
        "rule": "an empty glyph layer at the plate's size: the symbol is in the plate" if empty else RULES[b.family],
        "source_art": b.art,
        "source": None,
        "sha256": button_kit.sha256_file(image_path),
        "date": datetime.date.today().isoformat(),
    }
    if source is not None and b.family == "rect":
        record["source"] = {"pack": args.pack.name, "file": str(source.relative_to(args.pack)), "sha256": button_kit.sha256_file(source)}
    elif source is not None:
        provenance = source.with_suffix(".provenance.json")
        record["source"] = {"sheet": source.name, "sha256": button_kit.sha256_file(source)}
        if provenance.is_file():
            try:
                record["source"]["generation"] = json.loads(provenance.read_text(encoding="utf-8"))
            except ValueError as error:
                raise SmallError(f"{provenance}: not a JSON provenance record ({error})") from error
        if b.family in ("paper", "ink", "wedge"):
            record["source"]["frames"] = b.folder
        if b.family == "paper":
            slab = source.parent / f"{PAPER_SLAB}.png"
            record["source"]["slab"] = {"sheet": slab.name, "sha256": button_kit.sha256_file(slab)}
    elif b.family == "dome" and not empty:
        masters = args.local_root / "masters"
        record["source"] = {"face": DOME_PLATE, "face_sha256": button_kit.sha256_file(masters / f"{DOME_PLATE}.png"), "ring": DOME_RING, "ring_sha256": button_kit.sha256_file(masters / f"{DOME_RING}.png"), "frames": b.folder}
    return record


def need_magick() -> None:
    if shutil.which("magick") is None:
        raise SmallError("ImageMagick (`magick`) is needed for this command")


def cmd_plan(args: argparse.Namespace) -> int:
    manifest = {r["id"]: r for r in json.loads(MANIFEST.read_text(encoding="utf-8"))["parts"]}
    rows = pack_index(args.pack) if args.pack.joinpath("index.tsv").is_file() or any(b.family == "rect" for b in SMALL) else {}
    bad = 0
    for b in (*SMALL, *GENERATED):
        row = manifest.get(f"plate.{b.name}")
        have = [state for state, number in b.frames.items() if (b.name, number) in rows]
        ok = row is not None and row["size"] == list(b.master) and (len(have) == len(b.frames) if b.family == "rect" else True)
        bad += 0 if ok else 1
        print(f"plate.{b.name:24s} art {b.art}  {b.family:6s} {b.units[0]:>2} x {b.units[1]:<2} -> {b.master[0]} x {b.master[1]}  states {','.join(b.frames):22s} {'ok' if ok else 'CHECK'}")
    print(f"SMALL KIT plan {'PASS' if not bad else 'FAIL'} ({len(SMALL)} pack, rule and cut-out buttons, {len(GENERATED)} sheet buttons)")
    return 0 if not bad else 1


def cmd_build(args: argparse.Namespace) -> int:
    local = args.local_root
    out = args.out or local / "masters"
    records = local / "provenance" / "rule" if args.out is None else out / "records"
    buttons = selected(args.only)
    parts, sources = build_parts(args, buttons)
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
        (records / f"{ident}.json").write_text(json.dumps(record_of(ident, owner_of(ident), sources.get(ident), path, args), indent=2) + "\n", encoding="utf-8")
    print(f"SMALL KIT build PASS ({len(parts)} parts written to {out})")
    return 0


def upscaled(frame: Image, size: tuple[int, int]) -> Image:
    """An original frame at the master size, nearest cells (the 'original' column shows the original's own pixels)."""
    w, h = size
    out = Image(w, h, bytearray(w * h * 4))
    for py in range(h):
        cy = min(frame.h - 1, int((py + 0.5) * frame.h / h))
        for px in range(w):
            cx = min(frame.w - 1, int((px + 0.5) * frame.w / w))
            out.data[(py * w + px) * 4:(py * w + px) * 4 + 4] = frame.data[(cy * frame.w + cx) * 4:(cy * frame.w + cx) * 4 + 4]
    return out


def face(parts: dict[str, Image], b: Small, state: str) -> Image:
    """The button as the runtime shows it: the plate, the glyph over it, over both the overlay of the state (a full frame of a pack or sheet part replaces the idle plate)."""
    out = Image.blank(*b.master)
    over(out, parts[f"plate.{b.name}"], 0, 0)
    over(out, parts[f"glyph.{b.name}"], 0, 0)
    if state != "idle":
        over(out, parts[f"plate.{b.name}_{state}"], 0, 0)
    return out


def cmd_sheet(args: argparse.Namespace) -> int:
    """The composite for the owner: per button the original (every state, at the master size) | the kit's idle | hover | pressed face."""
    need_magick()
    frames = args.frames or args.local_root / "census" / "frames"
    buttons = selected(args.only)
    parts, _ = build_parts(args, buttons)
    with tempfile.TemporaryDirectory() as tmp:
        work = Path(tmp)
        rows_out = []
        for k, b in enumerate(buttons):
            cells = [(f"orig {state}", upscaled(read_frame(frames, b, number), b.master)) for state, number in b.frames.items()]
            for state in b.frames:
                if state == "idle" or f"plate.{b.name}_{state}" in parts:
                    cells.append((f"new {state}", face(parts, b, state)))
            files = []
            for j, (label, image) in enumerate(cells):
                path = work / f"{k}-{j}.png"
                pngio.write(path, image)
                lab = work / f"{k}-{j}-lab.png"
                subprocess.run(["magick", str(path), "-background", "#3c3c3c", "-flatten", "-filter", "point", "-resize", f"{args.zoom * 100}%", "-gravity", "North", "-background", "#202020", "-fill", "white",
                                "-font", LABEL_FONT, "-pointsize", "13", "-splice", "0x22", "-annotate", "+0+3", label, str(lab)], check=True)
                files.append(lab)
            head = work / f"head-{k}.png"
            subprocess.run(["magick", "-size", "150x40", "-background", "#202020", "-fill", "white", "-font", LABEL_FONT, "-pointsize", "13", f"label:{b.name}", str(head)], check=True)
            files.insert(0, head)
            row = work / f"row-{k}.png"
            subprocess.run(["magick", *[str(f) for f in files], "-background", "#202020", "-gravity", "West", "+append", "-bordercolor", "#202020", "-border", "6", str(row)], check=True)
            rows_out.append(row)
        args.out.parent.mkdir(parents=True, exist_ok=True)
        subprocess.run(["magick", *[str(r) for r in rows_out], "-background", "#202020", "-gravity", "West", "-append", str(args.out)], check=True)
    print(f"SMALL KIT sheet PASS ({len(buttons)} buttons) -> {args.out}")
    return 0


def self_test() -> int:
    cell = Image(20, 20, bytearray(b"\xff\x00\xff\xff" * 400))
    for y in range(4, 16):
        for x in range(4, 16):
            cell.data[(y * 20 + x) * 4:(y * 20 + x) * 4 + 4] = bytes((40, 90, 10, 255))
    clipped = triangle_clip(cell)
    assert clipped.pixel(10, 10)[3] == 255 and clipped.pixel(1, 1)[3] == 0 and clipped.pixel(4, 8)[3] == 0, "the ground and its rim are clear, the inside is solid"
    assert sheet_for("sp_plus", "pressed", "sp-a")[0].id == "sp-a"
    print("SMALL KIT self-test PASS")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("plan", "build", "sheet"):
        s = sub.add_parser(name)
        s.add_argument("--local-root", type=Path, default=None)
        s.add_argument("--pack", type=Path, default=PACK_ROOT, help="the installed HD pack the parts are read from")
        s.add_argument("--sheets", type=Path, default=None, help="the folder of the Codex sheets (default `local/steamwoven-ui/small-sheets`)")
        s.add_argument("--sp-variant", default=SP_VARIANT, choices=("sp-a", "sp-b"), help="the skill-point sheet in use")
        s.add_argument("--frames", type=Path)
        if name != "plan":
            s.add_argument("--only")
        if name == "build":
            s.add_argument("--out", type=Path)
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
        return {"plan": cmd_plan, "build": cmd_build, "sheet": cmd_sheet}[args.command](args)
    except (SmallError, pngio.PngError, OSError, subprocess.CalledProcessError) as error:
        print(f"SMALL KIT FAIL: {error}")
        return 2


if __name__ == "__main__":
    sys.exit(main())
