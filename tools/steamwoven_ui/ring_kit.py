#!/usr/bin/env python3
"""ARC-UI-3270 (batch B-BUTTONS, slice 3c): the small round buttons in a thin brass ring (16 arts, 19 x 19 to 28 x 29 units): the character sheet's plus and minus (two sizes), the message window's four buttons, the multiplayer
cycle arrows and the small right arrow, the three inventory "lil" buttons and the small accept and cancel.

How each is made (owner decisions of 2026-11-26 in the issue's thread; every part is a full face cut to the ring's circle by an analytic round alpha, no shadow baked):

* `codex`  23, 24, 770, 771: ONE generated set (sheet `pm`: Minus and Plus, three states, drawn large and reduced) with ONE ring, the idle Minus's, over every face; both sizes use the same frames.
* `marker` 145 to 148: the symbol cut from the generated marker sheet of the map markers 141 to 144 (#3207, one source for both places), on the set's own ring and dome (the idle Minus with its bar taken out); the idle symbol is
  dimmed as the original's frame 0 is dimmer than frame 1.
* `wedge`  757, 758: that same plate, a straight-edged triangle by rule fitted to the original's red cells (the method of arts 827 and 828, #3272).
* `arrow`  299: that same plate, the arrow by rule: a straight outline fitted to the original's red cells, the original's cells inside as even squares (rule E), a soft glow where the original lights up.
* `pack`   338 to 340, 823, 824: the player's own HD-pack frames, the pack's own alpha not used.

Every ring is brought to the accepted brass (`BRASS`, #3259 and #3260, `hardware_build.brass_match`); the dome inside is not recoloured.

    python3 -B tools/steamwoven_ui/ring_kit.py codex-inbox                                       (the inbox of batch B-BUTTONS-RING: prompt and references)
    python3 -B tools/steamwoven_ui/codex_batch.py run B-BUTTONS-RING                             (the owner's Codex subscription: one sheet)
    python3 -B tools/steamwoven_ui/ring_kit.py codex-place                                       (the sheet's discs registered onto the arts' circles -> work/B-BUTTONS-RING/placed)
    python3 -B tools/steamwoven_ui/ring_kit.py build [--only NAME,...] [--out DIR] [--dry-run]   (the parts -> masters/ and provenance/rule/)
    python3 -B tools/steamwoven_ui/ring_kit.py check [--only NAME,...]                           (every input of every state is there; the circles lie in the arts)
    python3 -B tools/steamwoven_ui/ring_kit.py sheet --out FILE [--real FILE] [--only NAME,...]  (original | new with the shadow; --real: all buttons at their size on a 2560 x 1440 screen; English labels)
    python3 -B tools/steamwoven_ui/ring_kit.py self-test

The parts of a button (TRD SWI-KIT-5; all at the plate's size, the manifest row `plate.<name>`):

* `plate.<name>` the idle face; `glyph.<name>` EMPTY (the symbol is in the face).
* `plate.<name>_pressed` the face of frame 1, `plate.<name>_hover` of frame 2 (arts of two frames have no hover look: the original wraps hover to frame 0). Frame 3 of the four plus and minus arts (the empty plate) is never
  drawn by the original (census, ledger OF9), so it has no part.

The circles are this tool's own table (`RINGS`: centre and outer radius in art units, plus `TRIM` for the cut). Those of the plus and minus stand on the sheet's socket centres (#3321). The viewer does not draw these arts from the
kit yet. The soft shadow under a round part is the run-time layer's; its offset and blur grow with the part's diameter (`shadow_of`, TRD).

Nothing here holds art: every picture is read from and written to the ignored `local/` (the pack is `local/hd-packs/interface-redraw-4x`, read only). Standard library only (`pngio.py`); ImageMagick for `sheet`.
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
    from . import button_kit, hardware_build, pngio, prompt_library, school_kit
except ImportError:  # run as a script
    sys.path.insert(0, str(HERE.parents[1]))
    from tools.steamwoven_ui import button_kit, hardware_build, pngio, prompt_library, school_kit  # type: ignore

Image = pngio.Image
TOOL_VERSION = 2            # 1 was the pack-first build of all sixteen (superseded by the owner on 2026-11-26)
TRIM = 0.4                   # units: the cut keeps this much beyond the ring's radius (the viewer's trim of the round parts)
OVERSHOOT = 1.2              # units: how far the cut's circle may lie beyond the art's rectangle (the ring of the 28 x 27 buttons is cut flat by the top of the frame, as the original's own is)
BLEED_PASSES = 64            # pack px: enough to colour every transparent pixel of the largest frame (112 x 116) next to an opaque one
STATE_FRAMES = {"idle": 0, "pressed": 1, "hover": 2}   # the pack's frame of each state (the original's order, ledger OF9)


class RingError(Exception):
    pass


@dataclass(frozen=True)
class Ring:
    art: int
    folder: str              # the census frames' folder
    name: str                # `plate.<name>`, `glyph.<name>`; the pack's index name
    frames: int              # the original's frame count
    master: tuple[int, int]  # the plate row's size
    circle: school_kit.Circle
    method: str              # `codex`, `marker`, `wedge`, `arrow` or `pack` (the module's header)

    @property
    def states(self) -> tuple[str, ...]:
        return ("idle", "pressed", "hover") if self.frames >= 3 else ("idle", "pressed")


def _ring(method: str, art: int, folder: str, frames: int, master: tuple[int, int], size: tuple[int, int], cx: float, cy: float, cut: float) -> Ring:
    return Ring(art, f"{art}-{folder}", folder.lower(), frames, master, school_kit.Circle(size[0], size[1], cx, cy, round(cut - TRIM, 3), TRIM), method)


RINGS = (
    # the plus and minus stand on the sheet's socket centres (#3321), as large as the art's box allows (half a unit over at one side)
    _ring("codex", 23, "char_minus", 4, (66, 66), (19, 19), 9.5, 9.5, 9.0),
    _ring("codex", 24, "char_plus", 4, (66, 66), (19, 19), 10.5, 9.5, 9.0),
    _ring("codex", 770, "Char_HTFTMinus", 4, (78, 78), (21, 21), 10.5, 10.0, 10.5),
    _ring("codex", 771, "Char_HTFTPlus", 4, (78, 78), (21, 21), 10.5, 10.0, 10.5),
    *(_ring("marker", art, name, 2, (84, 84), (23, 23), 11.5, 11.5, 11.5) for art, name in ((145, "mmb_chest"), (146, "mmb_note"), (147, "mmb_ques"), (148, "mmb_skull"))),
    *(_ring("wedge", art, name, 3, (84, 84), (23, 23), 11.5, 11.5, 11.5) for art, name in ((757, "MPCycleLeftButton"), (758, "MPCycleRightButton"))),
    _ring("arrow", 299, "Sm_RightArrow", 3, (84, 84), (23, 23), 11.5, 11.5, 11.5),
    *(_ring("pack", art, name, 2, (102, 96), (28, 27), 13.4, 12.1, 13.2) for art, name in ((338, "PD_lil_butt"), (339, "Inven_lil_butt"), (340, "tiny_butt"))),
    *(_ring("pack", art, name, 3, (102, 102), (28, 29), 13.35, 13.0, 13.2) for art, name in ((823, "Lil_Accept"), (824, "Lil_Cancel"))),
)


def ring_named(name: str) -> Ring:
    for ring in RINGS:
        if ring.name == name:
            return ring
    raise RingError(f"{name} is no button of this slice")


# --- the part ---------------------------------------------------------------------------------------------------------------------

def bled(frame: Image, passes: int = BLEED_PASSES) -> Image:
    """The frame fully opaque: every pixel that is not (nearly) opaque takes the mean colour of its already coloured neighbours, ring by ring outwards, so the pack's own edge and holes never show through the analytic cut."""
    w, h = frame.w, frame.h
    out = Image(w, h, bytearray(frame.data))
    known = [frame.data[i * 4 + 3] >= 250 for i in range(w * h)]
    if not any(known):
        raise RingError("a frame with no opaque pixel has nothing to cut")
    todo = [i for i in range(w * h) if not known[i]]
    for _ in range(passes):
        if not todo:
            break
        fresh: list[tuple[int, bytes]] = []
        for i in todo:
            x, y = i % w, i // w
            near = [j for j in ((i - 1) if x else -1, (i + 1) if x < w - 1 else -1, (i - w) if y else -1, (i + w) if y < h - 1 else -1) if j >= 0 and known[j]]
            if near:
                fresh.append((i, bytes(round(sum(out.data[j * 4 + k] for j in near) / len(near)) for k in range(3))))
        for i, colour in fresh:
            out.data[i * 4:i * 4 + 3] = colour
            known[i] = True
        todo = [i for i in todo if not known[i]]
    if todo:
        raise RingError(f"{len(todo)} transparent pixel(s) lie further than {passes} px from the frame's opaque ones")
    for i in range(w * h):
        out.data[i * 4 + 3] = 255
    return out


def part_of(ring: Ring, frame: Image) -> Image:
    return school_kit.round_part(bled(frame), ring.circle, ring.master)


def state_frame(local: Path, ring: Ring, state: str) -> tuple[Image, Path]:
    try:
        frame, path = school_kit.read_pack(local, ring.name, STATE_FRAMES[state])
    except school_kit.SchoolError as error:
        raise RingError(str(error)) from error
    ux, uy = frame.w / ring.circle.width, frame.h / ring.circle.height
    if abs(ux - uy) > 0.05 * ux:
        raise RingError(f"{ring.name} {state}: the pack frame {frame.w} x {frame.h} is not the art's aspect {ring.circle.width} x {ring.circle.height}")
    return frame, path


def original_frame(frames: Path, ring: Ring, state: str) -> Image:
    path = frames / ring.folder / f"f{STATE_FRAMES[state]}.png"
    try:
        return pngio.read(path)
    except (OSError, pngio.PngError) as error:
        raise RingError(f"{path}: {error}") from error


def over(target: Image, layer: Image, ox: int = 0, oy: int = 0) -> None:
    """`layer` laid over `target` at (ox, oy), straight alpha; what falls outside the target is dropped."""
    for y in range(layer.h):
        for x in range(layer.w):
            dx, dy = x + ox, y + oy
            if not (0 <= dx < target.w and 0 <= dy < target.h):
                continue
            s, d = (y * layer.w + x) * 4, (dy * target.w + dx) * 4
            a = layer.data[s + 3] / 255.0
            if a <= 0:
                continue
            below = target.data[d + 3] / 255.0
            total = a + below * (1 - a)
            for c in range(3):
                target.data[d + c] = int(round((layer.data[s + c] * a + target.data[d + c] * below * (1 - a)) / total))
            target.data[d + 3] = int(round(total * 255))


# --- the accepted brass -----------------------------------------------------------------------------------------------------------

BRASS = (50.0, 32.0, 78.0)   # L*, C*, hue of the accepted brass: the target `hardware_build.py` gives the menu window to match the HUD strips' rings (#3259, #3260)
INNER = {"codex": 0.74, "pack": 0.79}   # where the ring begins, as a share of the cut's radius: inside it nothing is recoloured


def ring_weight(circle: school_kit.Circle, w: int, h: int, x: int, y: int, inner: float, feather: float = 0.35) -> float:
    """1 on the ring, 0 on the dome, a linear step `feather` units wide at `inner` x the cut's radius; (x, y) a pixel of a w x h picture of the art."""
    d = math.hypot((x + 0.5) * circle.width / w - circle.cx, (y + 0.5) * circle.height / h - circle.cy)
    return max(0.0, min(1.0, (d - inner * circle.cut) / feather + 0.5))


def toned(part: Image, circle: school_kit.Circle, inner: float) -> Image:
    """The ring of a face brought to the accepted brass: only the ring's pixels are measured and shifted, the dome keeps its colours."""
    only = Image(part.w, part.h, bytearray(part.data))
    weights = [ring_weight(circle, part.w, part.h, i % part.w, i // part.w, inner) for i in range(part.w * part.h)]
    for i, weight in enumerate(weights):
        if weight <= 0:
            only.data[i * 4 + 3] = 0
    with tempfile.TemporaryDirectory(prefix="ring-brass-") as folder:
        path = Path(folder) / "ring.png"
        pngio.write(path, only)
        if not hardware_build.brass_match(path, BRASS):
            raise RingError("the ring has too few coloured pixels to be brought to the accepted brass")
        shifted = pngio.read(path)
    out = Image(part.w, part.h, bytearray(part.data))
    for i, weight in enumerate(weights):
        if weight > 0:
            for c in range(3):
                out.data[i * 4 + c] = int(round(shifted.data[i * 4 + c] * weight + part.data[i * 4 + c] * (1 - weight)))
    return out


# --- the accepted plate and the symbols on it -------------------------------------------------------------------------------------

SYMBOL_REACH, SYMBOL_FADE = 0.52, 0.08   # shares of the cut's radius: the middle of the dome that moves with the symbol, and the width over which that fades out


def symbol_offset(frame: Image, circle: school_kit.Circle) -> tuple[int, int]:
    """How far the red symbol's box of a generated frame stands from the circle's centre, in frame px (the generator draws it a little beside the middle)."""
    ux, uy = frame.w / circle.width, frame.h / circle.height
    xs, ys = [], []
    for i in range(frame.w * frame.h):
        r, g = frame.data[i * 4], frame.data[i * 4 + 1]
        x, y = i % frame.w + 0.5, i // frame.w + 0.5
        if r > 90 and r - g > 70 and math.hypot(x / ux - circle.cx, y / uy - circle.cy) < SYMBOL_REACH * circle.cut:
            xs.append(x)
            ys.append(y)
    if not xs:
        raise RingError("no red symbol in the middle of the generated button")
    return round((min(xs) + max(xs)) / 2 - circle.cx * ux), round((min(ys) + max(ys)) / 2 - circle.cy * uy)


def centred(frame: Image, circle: school_kit.Circle, offset: tuple[int, int]) -> Image:
    """The middle of the dome moved by -offset, fading into the unmoved rest, so the symbol stands in the middle of the button."""
    ux, uy = frame.w / circle.width, frame.h / circle.height
    out = Image(frame.w, frame.h, bytearray(frame.data))
    for y in range(frame.h):
        for x in range(frame.w):
            d = math.hypot((x + 0.5) / ux - circle.cx, (y + 0.5) / uy - circle.cy) / circle.cut
            weight = max(0.0, min(1.0, (SYMBOL_REACH + SYMBOL_FADE - d) / SYMBOL_FADE))
            if weight > 0:
                k = (y * frame.w + x) * 4
                j = (max(0, min(frame.h - 1, y + offset[1])) * frame.w + max(0, min(frame.w - 1, x + offset[0]))) * 4
                for c in range(3):
                    out.data[k + c] = int(round(frame.data[j + c] * weight + frame.data[k + c] * (1 - weight)))
    return out


BAR_HALF_WIDTH, BAR_TOP, BAR_BOTTOM = 0.50, -0.22, 0.30   # the box (shares of the cut's radius, from the centre) that holds the idle Minus's bar and its slot's shade on the generated frame


def without_bar(frame: Image, circle: school_kit.Circle) -> Image:
    """The idle Minus's frame with its symbol taken out: every column of the bar's box is a straight blend from the dome pixel above the box to the one below it (the dome is smooth there), softened at the box's two sides."""
    out = Image(frame.w, frame.h, bytearray(frame.data))
    ux, uy = frame.w / circle.width, frame.h / circle.height
    left, right = int((circle.cx - BAR_HALF_WIDTH * circle.cut) * ux), int(math.ceil((circle.cx + BAR_HALF_WIDTH * circle.cut) * ux))
    top, bottom = int((circle.cy + BAR_TOP * circle.cut) * uy), int(math.ceil((circle.cy + BAR_BOTTOM * circle.cut) * uy))
    soft = max(1, (right - left) // 8)
    for x in range(left, right):
        side = min(1.0, (min(x - left, right - 1 - x) + 1) / soft)
        above, below = ((top - 1) * frame.w + x) * 4, (bottom * frame.w + x) * 4
        for y in range(top, bottom):
            t = (y - top + 1) / (bottom - top + 1)
            k = (y * frame.w + x) * 4
            for c in range(3):
                blend = frame.data[above + c] * (1 - t) + frame.data[below + c] * t
                out.data[k + c] = int(round(blend * side + frame.data[k + c] * (1 - side)))
    return out


def empty_plate(local_root: Path, ring: Ring) -> Image:
    """The plate of the buttons that carry a drawn or cut symbol: the SAME ring and glossy dome as the plus and minus (the idle Minus of the generated set, its bar taken out), brought to the accepted brass and cut to this circle."""
    placed = placed_frame_of(local_root, ring_named("char_minus"), "idle", ring.circle)
    frame = without_bar(centred(placed, ring.circle, symbol_offset(placed, ring.circle)), ring.circle)
    return toned(part_of(ring, frame), ring.circle, INNER["codex"])


MARKER_BATCH = "B-BUTTONS-3207-markers"     # the generated sheet of the map markers (#3207): magenta ground, 3 x 2 cells
MARKER_CELLS = {"mmb_chest": (0, 0), "mmb_note": (1, 0), "mmb_ques": (2, 0), "mmb_skull": (0, 1)}
SYMBOL_BOXES = {"mmb_chest": (5, 4, 17, 17), "mmb_note": (5, 3, 16, 18), "mmb_ques": (7, 4, 15, 16), "mmb_skull": (4, 5, 19, 17)}   # the symbol's cells in the original's frames (x0, y0, x1, y1)
IDLE_DIM = {"mmb_chest": 0.77, "mmb_note": 0.67, "mmb_ques": 0.78, "mmb_skull": 0.66}   # frame 0's symbol lightness as a share of frame 1's, measured on the originals


MARKER_REDRAWN = {"mmb_skull": "skull-neutral"}   # symbols #3207 redrew as an item of their own (the owner asked for a neutral skull): the whole picture, not a cell of the sheet


def marker_sheet(local_root: Path, name: str | None = None) -> Path:
    """The picture a symbol is cut from: the marker sheet, or the item of its own that replaced the symbol's cell."""
    return local_root / "outbox" / MARKER_BATCH / f"{MARKER_REDRAWN.get(name, 'marker-sheet')}.png"


def keyed_magenta(picture: Image) -> Image:
    """The magenta ground of a generated sheet made clear (a pixel's magenta share is how far red and blue stand above green), the ground's colour taken out of the soft edge."""
    out = Image(picture.w, picture.h, bytearray(picture.data))
    for i in range(picture.w * picture.h):
        r, g, b = picture.data[i * 4:i * 4 + 3]
        a = 1.0 - max(0.0, min(1.0, (min(r, b) - g - 40) / 180.0))
        if a < 0.02:
            out.data[i * 4:i * 4 + 4] = bytes((0, 0, 0, 0))
        elif a < 1.0:
            out.data[i * 4:i * 4 + 4] = bytes((*(max(0, min(255, int(round((v - (1 - a) * m) / a)))) for v, m in ((r, 255), (g, 0), (b, 255))), int(round(a * 255))))
    return out


def cropped(picture: Image, x0: int, y0: int, x1: int, y1: int) -> Image:
    out = Image(x1 - x0, y1 - y0, bytearray((x1 - x0) * (y1 - y0) * 4))
    for y in range(out.h):
        i = ((y0 + y) * picture.w + x0) * 4
        out.data[y * out.w * 4:(y + 1) * out.w * 4] = picture.data[i:i + out.w * 4]
    return out


def marker_symbol(local_root: Path, name: str) -> Image:
    """The symbol of a message button: its cell of the marker sheet (or the picture that replaced it), keyed and cropped to what is drawn."""
    try:
        sheet = pngio.read(marker_sheet(local_root, name))
    except (OSError, pngio.PngError) as error:
        raise RingError(f"the marker drawing of #3207 cannot be read: {error}") from error
    if name not in MARKER_REDRAWN:
        column, row = MARKER_CELLS[name]
        sheet = cropped(sheet, column * sheet.w // 3, row * sheet.h // 2, (column + 1) * sheet.w // 3, (row + 1) * sheet.h // 2)
    cell = keyed_magenta(sheet)
    on = [i for i in range(cell.w * cell.h) if cell.data[i * 4 + 3] > 40]
    if not on:
        raise RingError(f"{name}: the marker sheet's cell is empty")
    xs, ys = [i % cell.w for i in on], [i // cell.w for i in on]
    return cropped(cell, min(xs), min(ys), max(xs) + 1, max(ys) + 1)


def marker_face(local_root: Path, ring: Ring, state: str) -> Image:
    symbol = marker_symbol(local_root, ring.name)
    x0, y0, x1, y1 = SYMBOL_BOXES[ring.name]
    mx, my = ring.master[0] / ring.circle.width, ring.master[1] / ring.circle.height
    scale = min((x1 - x0) * mx / symbol.w, (y1 - y0) * my / symbol.h)
    w, h = max(1, round(symbol.w * scale)), max(1, round(symbol.h * scale))
    small = school_kit.resampled(symbol, w, h)
    if state == "idle":
        for i in range(w * h):
            for c in range(3):
                small.data[i * 4 + c] = int(round(small.data[i * 4 + c] * IDLE_DIM[ring.name]))
    face = empty_plate(local_root, ring)
    over(face, small, round((x0 + x1) / 2 * mx - w / 2), round((y0 + y1) / 2 * my - h / 2))
    return face


RED_DE = 25                  # a cell whose red exceeds its green by this much is the symbol (the plate is olive), as `small_kit.py`
LIT = 140                    # a symbol cell this red is the symbol's body; a dimmer one is the glow round it
SAMPLES = 6                  # a drawn shape's coverage of a master pixel is sampled on a SAMPLES x SAMPLES grid


def symbol_colours(frame: Image) -> dict[tuple[int, int], tuple[int, int, int]]:
    """The red cells inside the dome and their colours."""
    out = {}
    for y in range(frame.h):
        for x in range(frame.w):
            r, g, b, a = frame.data[(y * frame.w + x) * 4:(y * frame.w + x) * 4 + 4]
            if a and r - g >= RED_DE and math.hypot(x - 11, y - 11) < 9:
                out[(x, y)] = (r, g, b)
    return out


def drawn(ring: Ring, shade) -> Image:
    """A layer at the master size: `shade(x, y)` (art units) returns a colour inside the shape, None outside; the coverage is sampled, the colour the mean of the samples inside."""
    mw, mh = ring.master
    out = Image(mw, mh, bytearray(mw * mh * 4))
    ux, uy = ring.circle.width / mw, ring.circle.height / mh
    for py in range(mh):
        for px in range(mw):
            hits = [c for j in range(SAMPLES) for i in range(SAMPLES) if (c := shade((px + (i + 0.5) / SAMPLES) * ux, (py + (j + 0.5) / SAMPLES) * uy)) is not None]
            if hits:
                o = (py * mw + px) * 4
                out.data[o:o + 4] = bytes((*(round(sum(h[k] for h in hits) / len(hits)) for k in range(3)), round(255 * len(hits) / (SAMPLES * SAMPLES))))
    return out


@dataclass(frozen=True)
class Wedge:
    """A sideways isosceles triangle in original cells: the tip's and the base's column edge, the axis row and the half height at the base (the method of #3272)."""
    tip: float
    base: float
    axis: float
    half: float
    colour: tuple[int, int, int]

    def holds(self, x: float, y: float) -> bool:
        along = (x - self.tip) / (self.base - self.tip)
        return 0.0 <= along <= 1.0 and abs(y - self.axis) <= self.half * along


def fit_wedge(frame: Image) -> Wedge:
    """The straight-edged triangle of a frame's staircase of red cells: it spans the cells' columns, stands on their middle row and covers the same area; the tip is at the end with the shorter column."""
    cells = symbol_colours(frame)
    columns: dict[int, int] = {}
    for x, _ in cells:
        columns[x] = columns.get(x, 0) + 1
    first, last = min(columns, default=0), max(columns, default=0)
    if first == last or columns[first] == columns[last]:
        raise RingError("no sideways red triangle in the frame")
    left = columns[first] < columns[last]
    rows = [y for _, y in cells]
    colour = tuple(round(sum(c[k] for c in cells.values()) / len(cells)) for k in range(3))
    return Wedge(first if left else last + 1, last + 1 if left else first, (min(rows) + max(rows) + 1) / 2.0, len(cells) / (last + 1 - first), colour)  # type: ignore[arg-type]


def wedge_face(local_root: Path, ring: Ring, frame: Image) -> Image:
    wedge = fit_wedge(frame)
    face = empty_plate(local_root, ring)
    over(face, drawn(ring, lambda x, y: wedge.colour if wedge.holds(x, y) else None))
    return face


@dataclass(frozen=True)
class Arrow:
    """A block arrow pointing right, in original cells: the shaft's left edge, top and bottom; the head's base edge, tip edge, top and bottom."""
    left: float
    shaft_top: float
    shaft_bottom: float
    neck: float
    tip: float
    head_top: float
    head_bottom: float

    def holds(self, x: float, y: float) -> bool:
        if self.left <= x < self.neck:
            return self.shaft_top <= y <= self.shaft_bottom
        if self.neck <= x <= self.tip:
            axis, half = (self.head_top + self.head_bottom) / 2, (self.head_bottom - self.head_top) / 2
            return abs(y - axis) <= half * (self.tip - x) / (self.tip - self.neck)
        return False


def fit_arrow(cells: dict[tuple[int, int], tuple[int, int, int]]) -> Arrow:
    """The arrow of a frame's symbol cells (all of them): the shaft is what the second column holds, the head begins where a column grows past it and ends after the last column."""
    body = cells
    if len(body) < 12:
        raise RingError("no arrow in the frame")
    xs = sorted({x for x, _ in body})
    left = xs[0]
    shaft = sorted(y for x, y in body if x == (xs[1] if len(xs) > 1 else left))
    neck = next((x for x in xs if min(y for i, y in body if i == x) < shaft[0] or max(y for i, y in body if i == x) > shaft[-1]), None)
    if neck is None:
        raise RingError("no arrow head in the frame")
    head = [y for x, y in body if x >= neck]
    return Arrow(left, shaft[0], shaft[-1] + 1, neck, xs[-1] + 1.5, min(head) - 0.5, max(head) + 1.5)


def arrow_face(local_root: Path, ring: Ring, frame: Image, idle: Image, glow: bool) -> Image:
    """One outline for every state (the idle frame's, so the arrow neither grows nor moves), each state's own cells inside it."""
    cells = symbol_colours(frame)
    arrow = fit_arrow(symbol_colours(idle))
    body = {k: v for k, v in cells.items() if v[0] >= (LIT if glow else 0)} or cells

    def shade(x: float, y: float):
        if not arrow.holds(x, y):
            return None
        at = (int(x), int(y))
        if at in body:
            return body[at]
        return body[min(body, key=lambda k: (k[0] + 0.5 - x) ** 2 + (k[1] + 0.5 - y) ** 2)]

    face = empty_plate(local_root, ring)
    if glow:
        colour = tuple(round(sum(c[k] for c in body.values()) / len(body)) for k in range(3))
        mx, my = ring.master[0] / ring.circle.width, ring.master[1] / ring.circle.height
        halo = Image(ring.master[0], ring.master[1], bytearray(ring.master[0] * ring.master[1] * 4))
        mask = drawn(ring, lambda x, y: colour if arrow.holds(x, y) else None)
        reach = round(mx)
        plane = [mask.data[i * 4 + 3] / 255.0 for i in range(halo.w * halo.h)]
        for _ in range(2):
            for step in (1, halo.w):
                plane = [sum(plane[i + k * step] for k in range(-reach, reach + 1) if 0 <= i + k * step < len(plane)) / (2 * reach + 1) for i in range(len(plane))]
        for i, a in enumerate(plane):
            halo.data[i * 4:i * 4 + 4] = bytes((*colour, int(round(min(1.0, a) * 150))))
        over(face, halo)
    over(face, drawn(ring, shade))
    return face


def codex_face(local_root: Path, ring: Ring, state: str) -> Image:
    """The placed frame of the state with the ONE ring of the set over it (the idle Minus's, registered on the same circle), brought to the accepted brass."""
    source = ring_named("char_plus" if "plus" in ring.name else "char_minus")
    offset = symbol_offset(placed_frame_of(local_root, source, "idle", ring.circle), ring.circle)   # one offset for the states of a button
    face = centred(placed_frame_of(local_root, source, state, ring.circle), ring.circle, offset)
    donor = placed_frame_of(local_root, ring_named("char_minus"), "idle", ring.circle)
    for y in range(face.h):
        for x in range(face.w):
            weight = ring_weight(ring.circle, face.w, face.h, x, y, INNER["codex"])
            if weight > 0:
                k = (y * face.w + x) * 4
                for c in range(3):
                    face.data[k + c] = int(round(donor.data[k + c] * weight + face.data[k + c] * (1 - weight)))
    return toned(part_of(ring, face), ring.circle, INNER["codex"])


def face_of(local_root: Path, frames: Path, ring: Ring, state: str) -> Image:
    if ring.method == "codex":
        return codex_face(local_root, ring, state)
    if ring.method == "pack":
        return toned(part_of(ring, state_frame(school_kit.pack_local(local_root), ring, state)[0]), ring.circle, INNER["pack"])
    if ring.method == "marker":
        return marker_face(local_root, ring, state)
    frame = original_frame(frames, ring, state)
    if ring.method == "wedge":
        return wedge_face(local_root, ring, frame)
    return arrow_face(local_root, ring, frame, original_frame(frames, ring, "idle"), state != "idle")


def ring_parts(local_root: Path, frames: Path, ring: Ring) -> dict[str, Image]:
    return {ident: school_kit.empty_layer(ring.master) if state is None else face_of(local_root, frames, ring, state) for ident, state in idents(ring)}


# --- the shadow the run-time layer casts (for the sheets) -------------------------------------------------------------------------

SHADOW_DIAMETER = 23.5       # units: the cut's diameter of the college tabs, on which the shared shadow's numbers were accepted (#3288)


def shadow_of(circle: school_kit.Circle) -> school_kit.Shadow:
    """The shared soft shadow for a part of this size: offset and blur in proportion to the diameter (owner 2026-11-26), the strength the same."""
    base, k = school_kit.shadow(), 2 * circle.cut / SHADOW_DIAMETER
    return school_kit.Shadow(base.offset * k, base.sigma * k, base.strength)


def idents(ring: Ring) -> list[tuple[str, str | None]]:
    """The parts of a button and the state each one shows (None: the empty glyph)."""
    return [(f"plate.{ring.name}", "idle"), (f"glyph.{ring.name}", None), *((f"plate.{ring.name}_{s}", s) for s in ring.states if s != "idle")]


def record_of(local_root: Path, frames: Path, ring: Ring, ident: str, image_path: Path, image: Image) -> dict:
    """The provenance record of a part (SWI-GEN-6): the method and the hash of everything it was made from."""
    state = dict(idents(ring))[ident]
    sources: dict = {}

    def add(key: str, path: Path) -> None:
        sources[key] = {"sha256": button_kit.sha256_file(path)} if path.is_file() else {"sha256": None, "missing": True}

    if state is not None:
        if ring.method == "codex":
            add(f"outbox/{CODEX_BATCH}/{SHEETS[0].ident}.png", sheet_path(local_root))
            add(f"outbox/{CODEX_BATCH}/{SHEETS[0].ident}.provenance.json", sheet_path(local_root).with_suffix(".provenance.json"))
        elif ring.method == "pack":
            path, index_sha = school_kit.pack_path(school_kit.pack_local(local_root), ring.name, STATE_FRAMES[state])
            add(f"{school_kit.PACK_DIR}/{ring.name}-f{STATE_FRAMES[state]:03d}", path)
            sources[f"{school_kit.PACK_DIR}/{ring.name}-f{STATE_FRAMES[state]:03d}"]["index_sha12"] = index_sha
        else:
            add(f"outbox/{CODEX_BATCH}/{SHEETS[0].ident}.png", sheet_path(local_root))   # the plate under the symbol
            if ring.method == "marker":
                picture = marker_sheet(local_root, ring.name)
                add(f"outbox/{MARKER_BATCH}/{picture.name}", picture)
                add(f"outbox/{MARKER_BATCH}/{picture.stem}.provenance.json", picture.with_suffix(".provenance.json"))
            else:
                for number in sorted({STATE_FRAMES[state], *([0] if ring.method == "arrow" else [])}):   # the arrow's outline is the idle frame's
                    add(f"census/frames/{ring.folder}/f{number}.png", frames / ring.folder / f"f{number}.png")
    c = ring.circle
    return {"part": ident, "rule": "ring_kit", "tool_version": TOOL_VERSION, "date": datetime.date.today().isoformat(), "source": "none (empty layer)" if state is None else ring.method, "sources": sources,
            "empty": not any(image.data[3::4]), "parameters": {"circle_units": {"cx": c.cx, "cy": c.cy, "radius": c.radius, "trim": c.trim, "art_size": [c.width, c.height]}, "brass_target": list(BRASS), "master": [image.w, image.h]},
            "image_sha256": hashlib.sha256(image_path.read_bytes()).hexdigest(), "image_size": [image.w, image.h], "run_by": "agent"}


# --- the Codex sheets (owner rule 2026-11-26: a detail under about 40 units is generated large and reduced) ------------------------

CODEX_BATCH = "B-BUTTONS-RING"
CELL_SHARE = 0.4             # the disc's radius on a sent sheet, as a share of a cell's side (the rest is ground)
PLACE_SCALE = 12             # px per art unit of the placed frame: the generated disc registered onto the circle, before the reduction to the master


@dataclass(frozen=True)
class Sheet:
    """One generated sheet: its buttons, the states in the original's order, whether the buttons are the columns (else the rows), the pixel size asked, and the prompt's own words."""
    ident: str
    names: tuple[str, ...]
    states: tuple[str, ...]
    by_column: bool
    size: tuple[int, int]
    what: str

    @property
    def grid(self) -> tuple[int, int]:
        return (len(self.names), len(self.states)) if self.by_column else (len(self.states), len(self.names))

    def cells(self) -> list[tuple[int, int, str, str]]:
        """(column, row, button, state) of every cell, row by row."""
        out = []
        columns, rows = self.grid
        for row in range(rows):
            for column in range(columns):
                name, state = (self.names[column], self.states[row]) if self.by_column else (self.names[row], self.states[column])
                out.append((column, row, name, state))
        return out


_THREE = ("idle", "pressed", "hover")
SHEETS = (
    Sheet("pm", ("char_minus", "char_plus"), _THREE, False, (1536, 1024),
          "the small Minus (top row) and Plus (bottom row) buttons of a character sheet. Symbol: a red enamel Minus (one straight horizontal bar) or Plus (two equal straight bars), small, in the middle of the dome, in a shallow dark slot. "
          "Columns, left to right: IDLE (deep red symbol), PRESSED (the symbol lit, pale pink-red core, a soft red light on the dome round it, as in image 1), HOVER (bright pure red symbol)."),
)


def sheet_prompt(sheet: Sheet) -> str:
    columns, rows = sheet.grid
    return (f"Image 1 is the ORIGINAL low-resolution game art, enlarged with hard pixels: a grid of {columns} columns and {rows} rows of small round buttons on a flat mid-grey ground. It is the truth for the layout, for what each symbol is, where it stands, how big it is, and for the colour of every state. "
            "Image 2 shows the same buttons in the same grid from a blurry earlier upscale; use it ONLY as a hint of the materials and colours. Its blur and smeared edges are wrong and must not be copied.\n\n"
            f"TASK: redraw image 1 as one clean, sharp, high-definition sheet of the same {columns} x {rows} grid. The buttons are {sheet.what}\n\n"
            "EVERY BUTTON: a perfectly circular button seen straight from the front: a thin ring of aged darkened brass with a crisp bevel (a light edge at the upper left, a dark lower right, a thin dark outline), and inside it a smooth convex glossy dome with one narrow curved reflection at the upper left, as in image 1. "
            "The ring keeps the same width relative to the button as in image 1. All buttons of the sheet share exactly the same ring, dome, light and size relative to image 1; the states of one button differ only in the way image 1 shows, and the symbol keeps exactly the same shape, size and place in all of them. "
            "Late-1800s steampunk-gothic game interface, painted, muted dusty palette and the brightness of image 1.\n\n"
            "FRAMING: every button in the same place and at the same size as in image 1. Nothing outside the round buttons: NO drop shadow, no dark crescent, no halo, no stone, no parchment, no frame, no grid lines; the ground stays one perfectly flat mid-grey (RGB 128, 128, 128). "
            "No text, no letters, no watermark, no added ornament, no rivets, no studs, no noise, no grain that looks like dirt, no lens blur.\n\n"
            f"Output one PNG {sheet.size[0]} x {sheet.size[1]} px. It is reduced about six times afterwards, so every edge must be perfectly clean, sharp and even.\n")


def staged_cell(source: Image, circle: school_kit.Circle, side: int, smooth: bool) -> Image:
    """One cell of a sent sheet: the button of `source` (any scale of the art) with its circle in the middle at `CELL_SHARE` of the side, cut round, on the flat ground; `smooth` samples bilinearly (the pack), else by hard cells (the original)."""
    src = bled(source)
    ground = school_kit.CODEX_GROUND
    radius = CELL_SHARE * side
    per_unit = radius / circle.cut
    sx, sy = src.w / circle.width, src.h / circle.height
    out = Image(side, side, bytearray(bytes((*ground, 255)) * (side * side)))
    for y in range(side):
        for x in range(side):
            a = max(0.0, min(1.0, 0.5 - (math.hypot(x + 0.5 - side / 2, y + 0.5 - side / 2) - radius)))
            if a <= 0:
                continue
            fx = (circle.cx + (x + 0.5 - side / 2) / per_unit) * sx
            fy = (circle.cy + (y + 0.5 - side / 2) / per_unit) * sy
            if smooth:
                gx, gy = max(0.0, min(src.w - 1.0, fx - 0.5)), max(0.0, min(src.h - 1.0, fy - 0.5))
                x0, y0 = int(gx), int(gy)
                x1, y1, tx, ty = min(src.w - 1, x0 + 1), min(src.h - 1, y0 + 1), gx - x0, gy - y0
                colour = [(src.data[(y0 * src.w + x0) * 4 + c] * (1 - tx) + src.data[(y0 * src.w + x1) * 4 + c] * tx) * (1 - ty) + (src.data[(y1 * src.w + x0) * 4 + c] * (1 - tx) + src.data[(y1 * src.w + x1) * 4 + c] * tx) * ty for c in range(3)]
            else:
                i = (max(0, min(src.h - 1, int(math.floor(fy)))) * src.w + max(0, min(src.w - 1, int(math.floor(fx))))) * 4
                colour = list(src.data[i:i + 3])
            o = (y * side + x) * 4
            out.data[o:o + 3] = bytes(int(round(ground[c] * (1 - a) + colour[c] * a)) for c in range(3))
    return out


def paste(target: Image, picture: Image, ox: int, oy: int) -> None:
    for y in range(picture.h):
        o = ((oy + y) * target.w + ox) * 4
        target.data[o:o + picture.w * 4] = picture.data[y * picture.w * 4:(y + 1) * picture.w * 4]


def cell_of(sheet_image: Image, sheet: Sheet, column: int, row: int) -> Image:
    columns, rows = sheet.grid
    x0, x1 = round(column * sheet_image.w / columns), round((column + 1) * sheet_image.w / columns)
    y0, y1 = round(row * sheet_image.h / rows), round((row + 1) * sheet_image.h / rows)
    out = Image(x1 - x0, y1 - y0, bytearray((x1 - x0) * (y1 - y0) * 4))
    for y in range(out.h):
        i = ((y0 + y) * sheet_image.w + x0) * 4
        out.data[y * out.w * 4:(y + 1) * out.w * 4] = sheet_image.data[i:i + out.w * 4]
    return out


def registered(cell: Image, circle: school_kit.Circle, scale: int = PLACE_SCALE) -> Image:
    """The generated disc of a cell mapped onto the art's circle (its centre onto the centre, its radius onto the cut's radius) at `scale` px per unit, box-averaged; a disc whose size is far from the sent one's is refused."""
    cx, cy, radius = school_kit.disc_of_image(cell)
    share = radius / min(cell.w, cell.h) / CELL_SHARE
    if not school_kit.CODEX_FRAME_SCALE[0] - 0.04 <= share <= school_kit.CODEX_FRAME_SCALE[1] + 0.04:
        raise RingError(f"the generated button's size differs from the sent one's (share {share:.3f})")
    w, h = circle.width * scale, circle.height * scale
    per_unit = radius / circle.cut                        # generated px per art unit
    step = per_unit / scale                               # generated px per placed px
    foot = max(1, int(round(step)))
    out = Image(w, h, bytearray(w * h * 4))
    for y in range(h):
        oy = cy + ((y + 0.5) / scale - circle.cy) * per_unit
        for x in range(w):
            ox = cx + ((x + 0.5) / scale - circle.cx) * per_unit
            acc, count = [0, 0, 0], 0
            for j in range(foot):
                py = int(oy + (j + 0.5) * step / foot - step / 2)
                for i in range(foot):
                    px = int(ox + (i + 0.5) * step / foot - step / 2)
                    if 0 <= px < cell.w and 0 <= py < cell.h:
                        k = (py * cell.w + px) * 4
                        acc[0] += cell.data[k]
                        acc[1] += cell.data[k + 1]
                        acc[2] += cell.data[k + 2]
                        count += 1
            o = (y * w + x) * 4
            out.data[o:o + 3] = bytes(int(round(acc[c] / count)) for c in range(3)) if count else bytes(school_kit.CODEX_GROUND)
            out.data[o + 3] = 255
    return out


def codex_dirs(local_root: Path) -> tuple[Path, Path]:
    return local_root / "inbox" / CODEX_BATCH, local_root / "outbox" / CODEX_BATCH


def sheet_path(local_root: Path) -> Path:
    return codex_dirs(local_root)[1] / f"{SHEETS[0].ident}.png"


_SHEET_CACHE: dict[tuple[str, int], Image] = {}


def placed_frame_of(local_root: Path, source: Ring, state: str, circle: school_kit.Circle) -> Image:
    """The generated disc of `source`'s state registered onto `circle` (the sheet's cell of that button and state)."""
    sheet, path = SHEETS[0], sheet_path(local_root)
    try:
        key = (str(path), path.stat().st_mtime_ns)
        if key not in _SHEET_CACHE:
            _SHEET_CACHE[key] = pngio.read(path)
    except (OSError, pngio.PngError) as error:
        raise RingError(f"no generated sheet {path} (run codex-inbox, then codex_batch.py run {CODEX_BATCH}): {error}") from error
    for column, row, name, cell_state in sheet.cells():
        if name == source.name and cell_state == state:
            return registered(cell_of(_SHEET_CACHE[key], sheet, column, row), circle)
    raise RingError(f"the sheet {sheet.ident} has no cell of {source.name} {state}")


def cmd_codex_inbox(args: argparse.Namespace) -> int:
    local_root = args.local_root
    local = school_kit.pack_local(local_root)
    frames = args.frames or local_root / "census" / "frames"
    inbox, outbox = codex_dirs(local_root)
    (inbox / "prompts").mkdir(parents=True, exist_ok=True)
    items = []
    for sheet in SHEETS:
        columns, rows = sheet.grid
        side = sheet.size[0] // columns
        refs = inbox / "refs" / sheet.ident
        refs.mkdir(parents=True, exist_ok=True)
        pictures = {slot: Image(side * columns, side * rows, bytearray(side * columns * side * rows * 4)) for slot in ("original", "style")}
        for column, row, name, state in sheet.cells():
            ring = ring_named(name)
            paste(pictures["original"], staged_cell(original_frame(frames, ring, state), ring.circle, side, False), column * side, row * side)
            paste(pictures["style"], staged_cell(state_frame(local, ring, state)[0], ring.circle, side, True), column * side, row * side)
        ref_rows = []
        for slot, picture in pictures.items():
            path = refs / f"{slot}.png"
            pngio.write(path, picture)
            ref_rows.append({"slot": slot, "file": f"refs/{sheet.ident}/{slot}.png", "sha256": button_kit.sha256_file(path)})
        prompt = inbox / "prompts" / f"{sheet.ident}.txt"
        prompt.write_text(sheet_prompt(sheet), encoding="ascii")
        items.append({"id": sheet.ident, "title": f"small brass-ringed buttons, sheet {sheet.ident}", "prompt_file": f"prompts/{sheet.ident}.txt", "prompt_sha256": hashlib.sha256(prompt.read_bytes()).hexdigest(),
                      "size": list(sheet.size), "references": ref_rows, "outbox_file": f"outbox/{CODEX_BATCH}/{sheet.ident}.png"})
    (inbox / "batch.json").write_text(json.dumps({"batch": CODEX_BATCH, "after": None, "items": items}, indent=2) + "\n", encoding="utf-8")
    outbox.mkdir(parents=True, exist_ok=True)
    print(f"RING KIT codex-inbox PASS: {len(items)} sheet(s) in {inbox}")
    return 0


# --- commands ---------------------------------------------------------------------------------------------------------------------

def selected(args: argparse.Namespace) -> list[Ring]:
    only = [n for n in args.only.split(",") if n] if args.only else None
    if only is None:
        return list(RINGS)
    unknown = sorted(set(only) - {r.name for r in RINGS})
    if unknown or not only:
        raise RingError(f"--only names buttons this slice does not hold: {unknown}")
    return [r for r in RINGS if r.name in only]


def cmd_check(args: argparse.Namespace) -> int:
    local_root = args.local_root
    frames = args.frames or local_root / "census" / "frames"
    if not local_root.is_dir():
        raise RingError(f"no local folder {local_root}")
    bad = []
    rings = selected(args)
    for ring in rings:
        c = ring.circle
        if min(c.cx - c.cut, c.cy - c.cut, c.width - c.cx - c.cut, c.height - c.cy - c.cut) < -OVERSHOOT:
            bad.append(f"{ring.name}: the circle leaves the art by more than {OVERSHOOT} unit")
        for state in ring.states:
            try:
                face_of(local_root, frames, ring, state)
            except (RingError, school_kit.SchoolError) as error:
                bad.append(f"{ring.name} {state}: {error}")
    for line in bad:
        print(line)
    print("RING KIT check " + ("FAIL" if bad else f"PASS ({len(rings)} buttons, a face for every state, circles inside the arts)"))
    return 1 if bad else 0


def cmd_build(args: argparse.Namespace) -> int:
    local_root = args.local_root
    frames = args.frames or local_root / "census" / "frames"
    out = args.out or local_root / "masters"
    records = local_root / "provenance" / "rule" if args.out is None else out / "records"
    built = [(ring, ring_parts(local_root, frames, ring)) for ring in selected(args)]
    total = 0
    for ring, parts in built:
        for ident, image in parts.items():
            covered = sum(1 for i in range(3, len(image.data), 4) if image.data[i] > 0) / (image.w * image.h)
            print(f"{ident:34s} {image.w} x {image.h}  {ring.method:6s}  covered {covered * 100:5.1f} %")
            total += 1
    if args.dry_run:
        print(f"RING KIT build DRY-RUN PASS ({total} parts); nothing written")
        return 0
    out.mkdir(parents=True, exist_ok=True)
    records.mkdir(parents=True, exist_ok=True)
    for ring, parts in built:
        for ident, image in parts.items():
            path = out / f"{ident}.png"
            pngio.write(path, image)
            (records / f"{ident}.json").write_text(json.dumps(record_of(local_root, frames, ring, ident, path, image), indent=2) + "\n", encoding="utf-8")
    print(f"RING KIT build PASS ({total} parts written to {out})")
    return 0


# --- the sheets for the owner ---------------------------------------------------------------------------------------------------------

SHEET_MARGIN = 5.0           # units of ground round a button on the sheets
REAL_PX = 2.4                # px per art unit on a 2560 x 1440 screen (1440 / 600 units)


def staged(picture: Image, ring: Ring, shadow: school_kit.Shadow | None) -> Image:
    """A picture of the art's box (any size) on the sheet's ground with `SHEET_MARGIN` units round it, and the run-time layer's shadow under it when one is given (`AreaKitRoundParts.ShadowAt`'s formula)."""
    c = ring.circle
    ux, uy = picture.w / c.width, picture.h / c.height
    mw, mh = int(round(SHEET_MARGIN * ux)), int(round(SHEET_MARGIN * uy))
    w, h = picture.w + 2 * mw, picture.h + 2 * mh
    out = Image(w, h, bytearray(bytes((*school_kit.GROUND, 255)) * (w * h)))
    for y in range(h):
        for x in range(w):
            o = (y * w + x) * 4
            if shadow is not None:
                px, py = (x - mw + 0.5) / ux, (y - mh + 0.5) / uy
                moved = math.hypot(px - c.cx - shadow.offset, py - c.cy - shadow.offset) - c.cut
                edge = 1.0 / (1.0 + math.exp(max(-60.0, min(60.0, 1.702 * moved / shadow.sigma))))
                keep = max(0.0, min(1.0, 0.5 - (math.hypot(px - c.cx, py - c.cy) - c.cut) * ux))
                a = shadow.strength * edge * (1.0 - keep)
                out.data[o:o + 3] = bytes(int(round(g * (1 - a))) for g in school_kit.GROUND)
    over(out, picture, mw, mh)
    return out


def magick(*command: str) -> None:
    subprocess.run(["magick", *command], check=True)


def label(path: Path, width: int, height: int, text: str, size: int = 18) -> Path:
    magick("-size", f"{width}x{height}", "xc:#202020", "-font", school_kit.LABEL_FONT, "-fill", "white", "-pointsize", str(size), "-gravity", "center", "-annotate", "+0+0", text, str(path))
    return path


def cmd_sheet(args: argparse.Namespace) -> int:
    """The composite for the owner: per button ORIGINAL (idle, pressed, hover) | NEW (the same, with the shadow), two columns of buttons; `--real`: every button idle and pressed at its size on a 2560 x 1440 screen."""
    local_root = args.local_root
    frames = args.frames or local_root / "census" / "frames"
    if shutil.which("magick") is None:
        raise RingError("ImageMagick `magick` is not installed")
    rings = selected(args)
    states = ("idle", "pressed", "hover")
    per_unit = 4 * args.zoom
    cell = int((29 + 2 * SHEET_MARGIN) * per_unit)
    label_w, gap = 170, 10
    parts = {ring.name: ring_parts(local_root, frames, ring) for ring in rings}

    def part(ring: Ring, state: str) -> Image:
        return parts[ring.name][f"plate.{ring.name}" if state == "idle" else f"plate.{ring.name}_{state}"]

    work = Path(tempfile.mkdtemp(prefix="ring-sheet-"))
    try:
        blank, spacer = label(work / "blank.png", cell, cell, "no hover"), work / "spacer.png"
        magick("-size", f"{gap}x{cell}", "xc:#d0d0d0", str(spacer))
        rows = []
        for ring in rings:
            cells: list[Path] = [label(work / f"{ring.name}-label.png", label_w, cell, f"{ring.name}\nart {ring.art}\n{ring.method}")]
            for new in (False, True):
                cells.append(spacer)
                for state in states:
                    if state not in ring.states:
                        cells.append(blank)
                        continue
                    box = (ring.circle.width * per_unit, ring.circle.height * per_unit)
                    picture = part(ring, state) if new else school_kit.upscaled(original_frame(frames, ring, state), ring.master)
                    shot = staged(picture, ring, shadow_of(ring.circle) if new else None)
                    path = work / f"{ring.name}-{int(new)}-{state}.png"
                    pngio.write(path, shot)
                    show = (round(shot.w * box[0] / ring.master[0]), round(shot.h * box[1] / ring.master[1]))
                    magick(str(path), "-filter", "Lanczos" if new else "point", "-resize", f"{show[0]}x{show[1]}!", "-background", "#202020", "-gravity", "center", "-extent", f"{cell}x{cell}", str(path))
                    cells.append(path)
            row = work / f"{ring.name}-row.png"
            magick(*map(str, cells), "+append", str(row))
            rows.append(row)
        width = label_w + 2 * (gap + 3 * cell)
        head = work / "head.png"
        magick("-size", f"{width}x44", "xc:#202020", "-font", school_kit.LABEL_FONT, "-fill", "white", "-pointsize", "22", "-annotate", f"+{label_w + gap + 10}+30", "ORIGINAL: idle | pressed | hover",
               "-annotate", f"+{label_w + 2 * gap + 3 * cell + 10}+30", "NEW (with shadow): idle | pressed | hover", str(head))
        half = (len(rows) + 1) // 2
        columns = []
        for k, chunk in enumerate((rows[:half], rows[half:])):
            if chunk:
                path = work / f"column-{k}.png"
                magick(str(head), *map(str, chunk), "-background", "#202020", "-append", str(path))
                columns.append(path)
        magick(*map(str, columns), "-background", "#202020", "-splice", "16x0", "+append", "-chop", "16x0", str(args.out))
        if args.real:
            real_sheet(work, args.real, rings, frames, part)
    finally:
        shutil.rmtree(work, ignore_errors=True)
    print(f"RING KIT sheet PASS: {args.out}" + (f", {args.real}" if args.real else ""))
    return 0


def real_sheet(work: Path, out: Path, rings: list[Ring], frames: Path, part) -> None:
    """Every button at `REAL_PX` px per unit, 1:1 pixels: original and new, idle and pressed."""
    cell = int(round((29 + 2 * SHEET_MARGIN) * REAL_PX))
    label_w = 150
    lines = []
    for state in ("idle", "pressed"):
        for new in (False, True):
            cells = [label(work / f"real-{state}-{int(new)}-label.png", label_w, cell, f"{'NEW' if new else 'ORIGINAL'} {state}", 14)]
            for ring in rings:
                path = work / f"real-{ring.name}-{state}-{int(new)}.png"
                size = (round(ring.circle.width * REAL_PX), round(ring.circle.height * REAL_PX))
                if new:
                    shot = staged(part(ring, state), ring, shadow_of(ring.circle))
                    pngio.write(path, shot)
                    magick(str(path), "-filter", "Lanczos", "-resize", f"{round(shot.w * size[0] / ring.master[0])}x{round(shot.h * size[1] / ring.master[1])}!", "-background", "#202020", "-gravity", "center", "-extent", f"{cell}x{cell}", str(path))
                else:
                    pngio.write(path, staged(school_kit.upscaled(original_frame(frames, ring, state), size), ring, None))
                    magick(str(path), "-background", "#202020", "-gravity", "center", "-extent", f"{cell}x{cell}", str(path))
                cells.append(path)
            line = work / f"real-line-{state}-{int(new)}.png"
            magick(*map(str, cells), "+append", str(line))
            lines.append(line)
    title = label(work / "real-title.png", label_w + cell * len(rings), 30, "Actual size on a 2560 x 1440 screen (view at 1:1 zoom)", 16)
    magick(str(title), *map(str, lines), "-background", "#202020", "-append", str(out))


def self_test() -> int:
    code = subprocess.run([sys.executable, "-B", "-m", "unittest", "tools.steamwoven_ui.test_ring_kit"], cwd=HERE.parents[1]).returncode
    print("RING KIT self-test " + ("PASS" if code == 0 else "FAIL"))
    return code


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("build", "check", "sheet", "codex-inbox", "self-test"):
        p = sub.add_parser(name)
        if name == "self-test":
            continue
        p.add_argument("--local-root", type=Path, default=None, help="the steamwoven-ui folder of the main checkout's local/ (default)")
        p.add_argument("--frames", type=Path, help="the original's frames (default: census/frames of the local root)")
        if name != "codex-inbox":
            p.add_argument("--only", help="button names, comma separated")
        if name == "build":
            p.add_argument("--out", type=Path)
            p.add_argument("--dry-run", action="store_true")
        if name == "sheet":
            p.add_argument("--out", type=Path, required=True)
            p.add_argument("--real", type=Path, help="also write the actual-size strip here")
            p.add_argument("--zoom", type=int, default=1, choices=range(1, 5))
    args = parser.parse_args(argv)
    if args.command == "self-test":
        return self_test()
    if args.local_root is None:
        args.local_root = prompt_library.default_local_root()
    try:
        return {"build": cmd_build, "check": cmd_check, "sheet": cmd_sheet, "codex-inbox": cmd_codex_inbox}[args.command](args)
    except (RingError, school_kit.SchoolError, OSError, pngio.PngError, subprocess.CalledProcessError) as error:
        print(f"RING KIT {args.command} ERROR: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
