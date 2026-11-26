#!/usr/bin/env python3
"""ARC-UI-3269 (batch B-BUTTONS, slice 3b): the thick brass-ringed round buttons (Bgrn_but 6, M_dnbut 9, M_upbut 10) and the dome and the two arrow wells of the schematic window's wide button (CombineButt 357, CombineIndicator1 to 3 358 to 360).

Two sources (`--source`), by size:

* Parts UNDER ABOUT 40 UNITS (the three ringed buttons, 36 to 38 units, and the wells, 22 units): `codex` (owner rule of 2026-11-26, #3272): each state generated large by Codex from the original (and the pack frame as a hint of the
  materials) on a square canvas of flat grey, registered onto the original's circle (`codex-place`), reduced once to the master; a ringed button's brass takes the tone of the accepted strip brass (#3259, #3260). `pack` keeps the pack cut
  for comparison.
* The wide button's dome (art 357, 51 units), `--dome`: `pack` is pack-first (rule P): the player's own HD-pack frame of each state; `olive` is the accepted olive plate of #3202 (`masters/plate.combat_button.png`), enlarged to the dome's
  circle, its pressed look the darkening of art 357's own pressed frame (rule E, `button_kit.render_crisp`).

Every part is cut by an ANALYTIC round alpha (an even smoothed edge, the colour outside it carried outwards so nothing of an old background is mixed in). The soft shadow under a round button is the run-time layer's (`AreaKitRoundParts`, #3249 and
#3288) and is not baked.

    python3 -B tools/steamwoven_ui/ring_button_kit.py codex-inbox --stage idle|states [--only NAME,...]                     (inbox/B-BUTTONS-3269: prompts and references; run it with codex_batch.py)
    python3 -B tools/steamwoven_ui/ring_button_kit.py codex-place [--only NAME,...]                                         (the generated pictures -> work/B-BUTTONS-3269/placed)
    python3 -B tools/steamwoven_ui/ring_button_kit.py build [--source codex|pack] [--dome pack|olive] [--only NAME,...] [--out DIR] [--dry-run]   (the parts -> masters/ and provenance/rule/)
    python3 -B tools/steamwoven_ui/ring_button_kit.py check [--source codex|pack] [--dome pack|olive] [--only NAME,...]     (every input is there and has the right size)
    python3 -B tools/steamwoven_ui/ring_button_kit.py sheet --out FILE [--only NAME,...] [--zoom N] [--across]              (rows original | old pack frame | after, columns the states, English labels)
    python3 -B tools/steamwoven_ui/ring_button_kit.py self-test

The parts of a button follow the runtime's convention (`SteamwovenKitComposer.ButtonLayers`): `plate.<name>` the idle face, `glyph.<name>` EMPTY (the symbol is in the frame), `plate.<name>_hover` and `_pressed` the other states laid over the face (a state
the art has no frame for has no part). An indicator art holds the two wells side by side: one `picture.<name>` of two discs, transparent between them.

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
    from . import button_kit, pngio, prompt_library, school_kit
except ImportError:  # run as a script
    sys.path.insert(0, str(HERE))
    import button_kit  # type: ignore
    import pngio  # type: ignore
    import prompt_library  # type: ignore
    import school_kit  # type: ignore

Image = pngio.Image
Circle = school_kit.Circle
TOOL_VERSION = 1
MANIFEST = HERE / "kit.json"
OLIVE_PLATE = "plate.combat_button"     # the accepted olive round face of #3202 (glossy arc baked in), 138 x 138 px
OLIVE_EXTRUDE_PX = 1.5                  # px of the olive master: the colour inside this far from its edge is carried outwards
DOMES = ("pack", "olive")               # the dome of the wide button (51 units): the pack's frames (pack-first); `olive` is the variant built from the #3202 plate
SOURCES = ("codex", "pack")             # the source of the parts under ~40 units (the ringed buttons and the wells): owner rule of 2026-11-26 (#3272): Codex by default, generated large and reduced; the pack frame kept for comparison
CODEX_BATCH = "B-BUTTONS-3269"
CODEX_FRAME_PX = 8                      # px per art unit of a placed Codex frame (twice the pack's), reduced once to the master by `cut`
BRASS_REFS = ("hardware.hud_ring", "hardware.menu_ring")   # the accepted brass of the strips' metal (#3259, #3260): a generated ring takes its tone (it came back yellower and lighter)
BRASS_INNER = (0.80, 0.84)              # shares of the cut radius: from nothing to the whole tone change (inside lie the black gap and the dome)
BRASS_DARK = (6.0, 16.0)                # CIE L*: a darker pixel (the ring's outline, the gap) keeps its colour, a lighter one takes the tone
CODEX_PAD = 2                           # art units of flat ground round the piece on its square Codex canvas
GROUND = school_kit.GROUND
LABEL_FONT = button_kit.LABEL_FONT


class RingError(Exception):
    pass


@dataclass(frozen=True)
class Button:
    art: int
    folder: str                  # the folder of the original's frames in the census
    name: str                    # the pack's art name and the parts' name
    kind: str                    # `plate` (a button) or `picture` (the two indicator discs)
    frames: dict[str, int]       # state -> frame number
    circles: tuple[Circle, ...]  # the disc(s) kept, in original units


def _ring(art: int, folder: str, name: str, frames: dict[str, int], width: int, height: int, cx: float, cy: float, radius: float, trim: float) -> Button:
    return Button(art, folder, name, "plate", frames, (Circle(width, height, cx, cy, radius, trim),))


# The circles are the outer edge of the ring (6, 9, 10: the gold's outer radius and the half unit of its dark outline), of the dome with its dark rim (357) and of the two wells (358 to 360), in original units from the art's top-left corner,
# measured on the original's frames.
RINGED = (
    _ring(6, "6-bgrn_but", "bgrn_but", {"idle": 0, "pressed": 1}, 37, 36, 18.2, 17.9, 18.2, 0.4),
    _ring(9, "9-m_dnbut", "m_dnbut", {"idle": 0, "hover": 2, "pressed": 1}, 38, 36, 19.0, 17.8, 18.2, 0.4),
    _ring(10, "10-m_upbut", "m_upbut", {"idle": 0, "hover": 2, "pressed": 1}, 38, 36, 19.0, 17.8, 18.2, 0.4),
)
DOME = _ring(357, "357-CombineButt", "combinebutt", {"idle": 0, "pressed": 1}, 51, 51, 25.5, 25.5, 25.0, 0.36)
WELLS = (Circle(117, 22, 11.0, 11.0, 10.7, 0.25), Circle(117, 22, 105.85, 10.97, 10.75, 0.25))
INDICATORS = tuple(Button(art, f"{art}-CombineIndicator{n}", f"combineindicator{n}", "picture", {"idle": 0}, WELLS) for n, art in enumerate((358, 359, 360), 1))
BUTTONS = (*RINGED, DOME, *INDICATORS)


def coverage() -> dict[str, Button]:
    """Every part this tool makes, by id."""
    return {ident: b for b in BUTTONS for ident in part_ids(b)}


def provenance_of(ident: str) -> str:
    """The manifest's provenance of a part with the default look (`SOURCES[0]`, `DOMES[0]`): `codex:` for what a generation made, `rule:` for the pack cut, the olive dome and the empty glyphs."""
    b = coverage()[ident]
    generated = b != DOME and SOURCES[0] == "codex" and not ident.startswith("glyph.")
    return f"{'codex' if generated else 'rule'}:ring_button_kit:{ident}"


def note_of(ident: str) -> str:
    """The manifest's note of a part with the default look."""
    b = coverage()[ident]
    if ident.startswith("glyph."):
        return f"EMPTY: the face of art {b.art} is whole in the plate (ring_button_kit.py, ARC-UI-3269)"
    state = next(s for s in b.frames if ident_of(b, s) == ident)
    if b == DOME and DOMES[0] == "olive":
        what = "the olive plate of #3202 enlarged to the dome of art 357, round alpha" if state == "idle" else "overlay: the darkening of art 357's pressed frame (rule E)"
    elif b != DOME and SOURCES[0] == "codex":
        what = f"the {state} look of art {b.art} drawn large by Codex (under 40 units), reduced, round alpha"
    else:
        what = f"the old HD pack's {state} frame of art {b.art}, round alpha (pack-first)"
    return f"{what}; ring_button_kit.py, ARC-UI-3269"


def ident_of(b: Button, state: str) -> str:
    return f"{b.kind}.{b.name}" if state == "idle" else f"{b.kind}.{b.name}_{state}"


def part_ids(b: Button) -> list[str]:
    """The parts of a button, in the order they are written."""
    ids = [ident_of(b, state) for state in b.frames]
    if b.kind == "plate":
        ids.insert(1, f"glyph.{b.name}")
    return ids


_MANIFEST: dict | None = None


def master_of(b: Button) -> tuple[int, int]:
    """The master's size of a button: the manifest row of its idle part."""
    global _MANIFEST
    if _MANIFEST is None:
        _MANIFEST = json.loads(MANIFEST.read_text(encoding="utf-8"))
    ident = ident_of(b, "idle")
    for part in _MANIFEST["parts"]:
        if part["id"] == ident and part.get("size"):
            return part["size"][0], part["size"][1]
    raise RingError(f"the manifest has no sized row {ident}")


# --- the pack parts ---------------------------------------------------------------------------------------------------------------

def pack_frame(local: Path, b: Button, state: str) -> tuple[Image, Path]:
    try:
        frame, path = school_kit.read_pack(local, b.name, b.frames[state])
    except school_kit.SchoolError as error:
        raise RingError(str(error)) from error
    circle = b.circles[0]
    ux, uy = frame.w / circle.width, frame.h / circle.height
    if abs(ux - uy) > 0.05 * ux:
        raise RingError(f"{b.name} {state}: the pack frame {frame.w} x {frame.h} is not the art's aspect {circle.width} x {circle.height}")
    return frame, path


def cut(frame: Image, circles: tuple[Circle, ...], master: tuple[int, int]) -> Image:
    """The frame at the master size cut to its disc(s). Two discs lie at the two ends of the art, so each half of the master takes its own disc."""
    if len(circles) == 1:
        return school_kit.round_part(frame, circles[0], master)
    halves = [school_kit.round_part(frame, c, master) for c in circles]
    out = Image(master[0], master[1], bytearray(master[0] * master[1] * 4))
    middle = master[0] // 2
    for y in range(master[1]):
        row = y * master[0] * 4
        out.data[row:row + middle * 4] = halves[0].data[row:row + middle * 4]
        out.data[row + middle * 4:row + master[0] * 4] = halves[1].data[row + middle * 4:row + master[0] * 4]
    return out


# --- the olive dome ---------------------------------------------------------------------------------------------------------------

def opaque_disc(image: Image) -> tuple[float, float, float]:
    """Centre and radius (px) of the opaque disc of a round master (its bounding box)."""
    xs = [x for x in range(image.w) if any(image.data[(y * image.w + x) * 4 + 3] >= 128 for y in range(image.h))]
    ys = [y for y in range(image.h) if any(image.data[(y * image.w + x) * 4 + 3] >= 128 for x in range(image.w))]
    if not xs or not ys:
        raise RingError("the olive plate is empty")
    w, h = xs[-1] + 1 - xs[0], ys[-1] + 1 - ys[0]
    return xs[0] + w / 2.0, ys[0] + h / 2.0, (w + h) / 4.0


def cubic(t: float) -> float:
    """Catmull-Rom."""
    t = abs(t)
    if t < 1:
        return 1.5 * t ** 3 - 2.5 * t ** 2 + 1
    if t < 2:
        return -0.5 * t ** 3 + 2.5 * t ** 2 - 4 * t + 2
    return 0.0


def olive_dome(face: Image, circle: Circle, master: tuple[int, int]) -> Image:
    """The olive face enlarged (bicubic) so that its disc fills the dome's circle, with the analytic round alpha. Samples beyond the face's edge are taken from just inside it."""
    fx, fy, fr = opaque_disc(face)
    mx, my = master[0] / circle.width, master[1] / circle.height
    tx, ty, trx, try_ = circle.cx * mx, circle.cy * my, circle.cut * mx, circle.cut * my
    limit = 1.0 - OLIVE_EXTRUDE_PX / fr
    out = Image(master[0], master[1], bytearray(master[0] * master[1] * 4))
    for y in range(master[1]):
        for x in range(master[0]):
            cover = school_kit.coverage(circle, master, x + 0.5, y + 0.5)
            if cover <= 0:
                continue
            nx, ny = (x + 0.5 - tx) / trx, (y + 0.5 - ty) / try_
            d = math.hypot(nx, ny)
            if d > limit:
                nx, ny = nx / d * limit, ny / d * limit
            sx, sy = fx + nx * fr - 0.5, fy + ny * fr - 0.5
            acc, total = [0.0, 0.0, 0.0], 0.0
            for j in range(int(math.floor(sy)) - 1, int(math.floor(sy)) + 3):
                for i in range(int(math.floor(sx)) - 1, int(math.floor(sx)) + 3):
                    k = (max(0, min(face.h - 1, j)) * face.w + max(0, min(face.w - 1, i))) * 4
                    weight = cubic(sx - i) * cubic(sy - j) * face.data[k + 3] / 255.0
                    total += weight
                    for c in range(3):
                        acc[c] += weight * face.data[k + c]
            if total <= 0:
                continue
            o = (y * master[0] + x) * 4
            out.data[o:o + 4] = bytes((*(max(0, min(255, int(round(v / total)))) for v in acc), int(round(255 * cover))))
    return out


def original_frame(frames: Path, b: Button, number: int) -> Image:
    path = frames / b.folder / f"f{number}.png"
    if not path.is_file():
        raise RingError(f"no frame {path}")
    image = pngio.read(path)
    circle = b.circles[0]
    if (image.w, image.h) != (circle.width, circle.height):
        raise RingError(f"{path}: {image.w} x {image.h}, the art is {circle.width} x {circle.height}")
    return image


def olive_parts(local_root: Path, frames: Path) -> dict[str, Image]:
    """The olive look of the dome: the idle face and the pressed overlay (what art 357's pressed frame darkens and lights over its idle frame, rule E)."""
    plate = local_root / "masters" / f"{OLIVE_PLATE}.png"
    if not plate.is_file():
        raise RingError(f"no master {plate} (the accepted olive plate of #3202)")
    master = master_of(DOME)
    face = olive_dome(pngio.read(plate), DOME.circles[0], master)
    idle, pressed = (original_frame(frames, DOME, DOME.frames[s]) for s in ("idle", "pressed"))
    overlay = button_kit.render_crisp(pressed, idle, True, pixels=master, owned_cell=lambda x, y: idle.data[(y * idle.w + x) * 4 + 3] > 0, shade=True, clip=face, solid_cell=lambda x, y: False)
    return {ident_of(DOME, "idle"): face, f"glyph.{DOME.name}": school_kit.empty_layer(master), ident_of(DOME, "pressed"): overlay}


# --- the brass tone ----------------------------------------------------------------------------------------------------------------

def rgb_of(light: float, a: float, b: float) -> tuple[int, int, int]:
    """CIE L*a*b* (D65) back to sRGB: the inverse of `button_kit.lab`."""
    fy = (light + 16.0) / 116.0

    def inverse(f: float) -> float:
        return f ** 3 if f ** 3 > 216.0 / 24389.0 else (116.0 * f - 16.0) * 27.0 / 24389.0

    x, y, z = inverse(fy + a / 500.0) * 0.95047, inverse(fy), inverse(fy - b / 200.0) * 1.08883
    linear = (3.2404542 * x - 1.5371385 * y - 0.4985314 * z, -0.9692660 * x + 1.8760108 * y + 0.0415560 * z, 0.0556434 * x - 0.2040259 * y + 1.0572252 * z)
    out = []
    for v in linear:
        v = max(0.0, min(1.0, v))
        out.append(int(round(255 * (12.92 * v if v <= 0.0031308 else 1.055 * v ** (1 / 2.4) - 0.055))))
    return out[0], out[1], out[2]


Stats = tuple[tuple[float, float, float], tuple[float, float, float]]   # mean and standard deviation of L*, a*, b*


def stats_of(colours: list[tuple[float, float, float]]) -> Stats:
    if not colours:
        raise RingError("no brass pixel to read a tone from")
    mean = tuple(sum(c[k] for c in colours) / len(colours) for k in range(3))
    spread = tuple(math.sqrt(sum((c[k] - mean[k]) ** 2 for c in colours) / len(colours)) for k in range(3))
    return mean, spread  # type: ignore[return-value]


def reference_brass(local_root: Path) -> Stats:
    """The tone of the accepted brass: the lit pixels of the reference masters."""
    colours = []
    for name in BRASS_REFS:
        path = local_root / "masters" / f"{name}.png"
        if not path.is_file():
            raise RingError(f"no master {path} (the accepted brass tone)")
        image = pngio.read(path)
        for k in range(0, len(image.data), 4):
            if image.data[k + 3] >= 200:
                colour = button_kit.lab(*image.data[k:k + 3])
                if colour[0] >= BRASS_DARK[1]:
                    colours.append(colour)
    return stats_of(colours)


def radius_share(image: Image, circle: Circle, x: int, y: int) -> float:
    ux, uy = image.w / circle.width, image.h / circle.height
    return math.hypot((x + 0.5 - circle.cx * ux) / (circle.cut * ux), (y + 0.5 - circle.cy * uy) / (circle.cut * uy))


def ring_stats(frame: Image, circle: Circle) -> Stats:
    """The tone of a frame's own ring: its lit pixels between the inner share and the cut."""
    return stats_of([colour for y in range(frame.h) for x in range(frame.w) if BRASS_INNER[1] <= radius_share(frame, circle, x, y) <= 1.0
                     for colour in (button_kit.lab(*frame.data[(y * frame.w + x) * 4:(y * frame.w + x) * 4 + 3]),) if colour[0] >= BRASS_DARK[1]])


def ramp(value: float, low: float, high: float) -> float:
    t = max(0.0, min(1.0, (value - low) / (high - low)))
    return t * t * (3.0 - 2.0 * t)


def toned(frame: Image, circle: Circle, source: Stats, target: Stats) -> Image:
    """The ring of `frame` moved from the tone `source` to `target` (mean and spread of each of L*, a*, b*); the dome, the symbol and the dark outline stay."""
    out = Image(frame.w, frame.h, bytearray(frame.data))
    gain = [target[1][k] / source[1][k] if source[1][k] > 1e-6 else 1.0 for k in range(3)]
    for y in range(frame.h):
        for x in range(frame.w):
            share = radius_share(frame, circle, x, y)
            if share <= BRASS_INNER[0] or share > 1.1:
                continue
            o = (y * frame.w + x) * 4
            colour = button_kit.lab(*frame.data[o:o + 3])
            weight = ramp(share, *BRASS_INNER) * ramp(colour[0], *BRASS_DARK)
            if weight <= 0:
                continue
            moved = [colour[k] + weight * (((colour[k] - source[0][k]) * gain[k] + target[0][k]) - colour[k]) for k in range(3)]
            out.data[o:o + 3] = bytes(rgb_of(*moved))
    return out


# --- the Codex frames -------------------------------------------------------------------------------------------------------------

STATE_WORDS = {"idle": "IDLE", "hover": "HOVER (the pointer is over it)", "pressed": "PRESSED (held down)"}
SUBJECTS = {
    "bgrn_but": "one round push button: a thick ring of aged polished brass; inside it a narrow black gap and an empty, very dark olive-black glass dome with one glossy light arc along its upper left. There is NO symbol on the dome.",
    "m_dnbut": "one round push button: a thick ring of aged polished brass; inside it a narrow black gap and a very dark olive-black glass dome with one glossy light arc along its upper left; in the middle of the dome a flat red arrow pointing DOWN.",
    "m_upbut": "one round push button: a thick ring of aged polished brass; inside it a narrow black gap and a very dark olive-black glass dome with one glossy light arc along its upper left; in the middle of the dome a flat red arrow pointing UP.",
    "combineindicator1": "one small round indicator lamp: a domed red glass lens that glows from its middle, with a thin dark edge. There is no ring and no symbol.",
    "combineindicator2": "one small round indicator: a black glass disc, and in its middle a glowing amber-yellow arrow pointing RIGHT, a plain block arrow (a rectangular stem and a triangle head whose back edge is one straight line, without stubs or notches).",
    "combineindicator3": "one small round indicator: a black glass disc with a faint green glow, and in its middle a glowing bright green arrow pointing RIGHT.",
}


def codex_dirs(local_root: Path) -> tuple[Path, Path, Path]:
    return local_root / "inbox" / CODEX_BATCH, local_root / "outbox" / CODEX_BATCH, local_root / "work" / CODEX_BATCH / "placed"


def codex_item(b: Button, state: str) -> str:
    return f"ring-{b.name}-{state}"


def codex_region(b: Button) -> tuple[int, int]:
    """The size in units of the piece of the art that is sent to Codex: the whole button, or an indicator's left well."""
    circle = b.circles[0]
    return (circle.width, circle.height) if b.kind == "plate" else (circle.height, circle.height)


def codex_pad(b: Button) -> tuple[int, int, int]:
    """The side of the square sent (the generator keeps no odd aspect: a 37 x 36 canvas came back refused) and where the piece lies in it: (side, left, top), all in units."""
    width, height = codex_region(b)
    side = max(width, height) + 2 * CODEX_PAD
    return side, (side - width) // 2, (side - height) // 2


def codex_circle(b: Button) -> Circle:
    """The disc that is sent to Codex and registered back, in its square."""
    circle = b.circles[0]
    side, left, top = codex_pad(b)
    return Circle(side, side, circle.cx + left, circle.cy + top, circle.radius, circle.trim)


def codex_square(image: Image, b: Button) -> Image:
    """The sent piece of a frame (`image` at any whole scale of the art) on its square canvas."""
    scale = image.h // b.circles[0].height
    side, left, top = (v * scale for v in codex_pad(b))
    width, height = (v * scale for v in codex_region(b))
    out = Image(side, side, bytearray(side * side * 4))
    for y in range(height):
        at_out, at_in = ((y + top) * side + left) * 4, y * image.w * 4
        out.data[at_out:at_out + width * 4] = image.data[at_in:at_in + width * 4]
    return out


def codex_subject_frames(local: Path, frames: Path, b: Button, state: str) -> tuple[Image, Image]:
    """(original, pack) of a state as the Codex input takes them."""
    return codex_square(original_frame(frames, b, b.frames[state]), b), codex_square(pack_frame(local, b, state)[0], b)


def codex_prompt(b: Button, state: str) -> str:
    lines = [f"Redraw {SUBJECTS[b.name]}",
             "It is a part of a game's steampunk interface, seen straight from the front, and will be shown very small, so it must read at once: large clean shapes, crisp sharp edges, smooth anti-aliased curves, high detail, no blur, no pixel squares, "
             "no checker or dot pattern (the original's dotted dithering stands for a smooth glow: paint it smooth), no noise, no scratches, no added ornament, no text.",
             "Image 1 is the original game art enlarged with hard pixels: it is the truth for the geometry, the proportions of every ring and symbol, the colours (muted, as there) and the light (from the upper left).",
             "Image 2 is an earlier soft redraw of the same thing: use it only as a hint of the materials."]
    if state != "idle":
        lines += [f"Image 3 is the finished IDLE button. Draw the SAME button, with the same ring, dome and symbol shape in the same place, in its {STATE_WORDS[state]} state: only the light on the dome and on the symbol changes, exactly as image 1 shows it for this state."]
    lines += ["The output is one square picture on a perfectly flat uniform grey background (#808080), without any shadow; the round object keeps exactly the same size and position in the picture as in image 1."]
    return "\n".join(lines) + "\n"


def codex_buttons(args: argparse.Namespace) -> list[Button]:
    found = [b for b in selected(args) if b.name in SUBJECTS]
    if not found:
        raise RingError("no button selected that has a Codex redraw")
    return found


def cmd_codex_inbox(args: argparse.Namespace) -> int:
    local_root, frames = args.local_root, frames_of(args)
    local = school_kit.pack_local(local_root)
    inbox, outbox, placed = codex_dirs(local_root)
    (inbox / "prompts").mkdir(parents=True, exist_ok=True)
    items = []
    for b in codex_buttons(args):
        states = [s for s in b.frames if (s == "idle") == (args.stage == "idle")]
        for state in states:
            circle, item = codex_circle(b), codex_item(b, state)
            original, pack = codex_subject_frames(local, frames, b, state)
            refs = [("image", "original-x32.png", school_kit.staged_original(original, circle)), ("sample", "pack-frame.png", school_kit.staged_input(pack, circle))]
            if state != "idle":
                done = placed / f"{b.name}-f{b.frames['idle']}.png"
                if not done.is_file():
                    raise RingError(f"{done} is missing: run the idle stage and `codex-place` first")
                idle = pngio.read(done)
                refs.append(("idle", "idle-sample.png", school_kit.staged_input(codex_square(idle, b), circle)))
            folder = inbox / "refs" / item
            folder.mkdir(parents=True, exist_ok=True)
            rows = []
            for slot, filename, image in refs:
                pngio.write(folder / filename, image)
                rows.append({"slot": slot, "file": f"refs/{item}/{filename}", "sha256": button_kit.sha256_file(folder / filename)})
            prompt = inbox / "prompts" / f"{item}.txt"
            prompt.write_text(codex_prompt(b, state), encoding="ascii")
            items.append({"id": item, "title": f"{b.name} ({state})", "prompt_file": f"prompts/{item}.txt", "prompt_sha256": hashlib.sha256(prompt.read_bytes()).hexdigest(),
                          "size": [circle.width * school_kit.CODEX_SCALE, circle.height * school_kit.CODEX_SCALE], "references": rows, "outbox_file": f"outbox/{CODEX_BATCH}/{item}.png"})
    batch = inbox / "batch.json"
    old = json.loads(batch.read_text(encoding="utf-8")) if batch.is_file() else {"items": []}
    keep = [i for i in old.get("items", []) if i["id"] not in {n["id"] for n in items}]
    batch.write_text(json.dumps({"batch": CODEX_BATCH, "after": None, "items": keep + items}, indent=2) + "\n", encoding="utf-8")
    outbox.mkdir(parents=True, exist_ok=True)
    print(f"RING BUTTON KIT codex-inbox PASS: {len(items)} item(s) in {inbox}: {','.join(i['id'] for i in items)}")
    return 0


def placed_art(generated: Image, b: Button) -> Image:
    """A generated picture as a frame of the whole art at `CODEX_FRAME_PX`: registered onto the geometry of what was sent; an indicator's two wells are the one disc, the right one mirrored when it holds an arrow (as the original's)."""
    circle = codex_circle(b)
    square = school_kit.placed_frame(generated, circle, (circle.width * CODEX_FRAME_PX, circle.height * CODEX_FRAME_PX))
    _, left, top = (v * CODEX_FRAME_PX for v in codex_pad(b))
    width, height = (v * CODEX_FRAME_PX for v in codex_region(b))
    art = b.circles[0]
    out = Image(art.width * CODEX_FRAME_PX, art.height * CODEX_FRAME_PX, bytearray(art.width * art.height * CODEX_FRAME_PX * CODEX_FRAME_PX * 4))
    places = [(0, False)]
    if b.kind == "picture":
        places.append((int(round((b.circles[1].cx - art.cx) * CODEX_FRAME_PX)), b.name != "combineindicator1"))
    for shift, mirrored in places:
        for y in range(height):
            for x in range(width):
                if shift + x >= out.w:
                    break
                k = ((y + top) * square.w + left + (width - 1 - x if mirrored else x)) * 4
                out.data[(y * out.w + shift + x) * 4:(y * out.w + shift + x) * 4 + 4] = square.data[k:k + 4]
    return out


def cmd_codex_place(args: argparse.Namespace) -> int:
    local_root = args.local_root
    _, outbox, placed = codex_dirs(local_root)
    placed.mkdir(parents=True, exist_ok=True)
    count, problems = 0, []
    for b in codex_buttons(args):
        for state, number in b.frames.items():
            generated = outbox / f"{codex_item(b, state)}.png"
            if not generated.is_file():
                problems.append(f"{b.name} {state}: no generated image {generated.name}")
                continue
            try:
                frame = placed_art(pngio.read(generated), b)
            except school_kit.SchoolError as error:
                problems.append(f"{b.name} {state}: {error}")
                continue
            pngio.write(placed / f"{b.name}-f{number}.png", frame)
            count += 1
            print(f"{b.name} {state}: placed {frame.w} x {frame.h}")
    for line in problems:
        print(line)
    print(f"RING BUTTON KIT codex-place {'PASS' if count and not problems else 'FAIL'} ({count} frame(s) in {placed}, {len(problems)} problem(s))")
    return 0 if count and not problems else 1


def codex_frame(local_root: Path, b: Button, state: str) -> Path:
    path = codex_dirs(local_root)[2] / f"{b.name}-f{b.frames[state]}.png"
    if not path.is_file():
        raise RingError(f"no placed Codex frame {path} (codex-inbox, the batch, codex-place)")
    return path


def button_parts(local_root: Path, frames: Path, b: Button, dome: str, source: str = SOURCES[0]) -> dict[str, Image]:
    if b == DOME and dome == "olive":
        return olive_parts(local_root, frames)
    local = school_kit.pack_local(local_root)
    master = master_of(b)

    tone = None
    if b in RINGED and source == "codex":   # one tone change per button, read from its idle ring, so the states keep one ring
        tone = ring_stats(pngio.read(codex_frame(local_root, b, "idle")), b.circles[0]), reference_brass(local_root)

    def frame_of(state: str) -> Image:
        if b != DOME and source == "codex":
            frame = pngio.read(codex_frame(local_root, b, state))
            return toned(frame, b.circles[0], *tone) if tone else frame
        return pack_frame(local, b, state)[0]

    parts = {ident_of(b, state): cut(frame_of(state), b.circles, master) for state in b.frames}
    if b.kind == "plate":
        parts[f"glyph.{b.name}"] = school_kit.empty_layer(master)
    return {ident: parts[ident] for ident in part_ids(b)}


# --- records and commands -----------------------------------------------------------------------------------------------------------

def record_of(local_root: Path, frames: Path, b: Button, ident: str, dome: str, source_choice: str, image_path: Path, image: Image) -> dict:
    """The provenance record of a part (SWI-GEN-6): no generator, the rule, and the hashes of what it was made from."""
    state = next((s for s in b.frames if ident_of(b, s) == ident), None)
    sources: dict[str, dict] = {}
    source = "none (empty layer)"
    if state is not None and b == DOME and dome == "olive":
        source = "the olive plate of #3202" if state == "idle" else "the original's frames (rule E)"
        plate = local_root / "masters" / f"{OLIVE_PLATE}.png"
        sources[f"masters/{OLIVE_PLATE}.png"] = {"sha256": button_kit.sha256_file(plate)}
        if state == "pressed":
            for number in sorted(b.frames.values()):
                sources[f"census/{b.folder}/f{number}"] = {"sha256": button_kit.sha256_file(frames / b.folder / f"f{number}.png")}
    elif state is not None and b != DOME and source_choice == "codex":
        source = "codex"
        _, outbox, _placed = codex_dirs(local_root)
        item = codex_item(b, state)
        generation = outbox / f"{item}.provenance.json"
        sources[f"outbox/{CODEX_BATCH}/{item}.png"] = {"sha256": button_kit.sha256_file(outbox / f"{item}.png"), "generation_record": str(generation.relative_to(local_root)) if generation.is_file() else "MISSING",
                                                      "generation_sha256": button_kit.sha256_file(generation) if generation.is_file() else None}
        sources[f"work/{CODEX_BATCH}/placed/{b.name}-f{b.frames[state]}"] = {"sha256": button_kit.sha256_file(codex_frame(local_root, b, state))}
        if b in RINGED:
            for name in BRASS_REFS:
                sources[f"masters/{name}.png"] = {"sha256": button_kit.sha256_file(local_root / "masters" / f"{name}.png"), "use": "brass tone of the ring"}
    elif state is not None:
        source = "pack"
        path, index_sha = school_kit.pack_path(school_kit.pack_local(local_root), b.name, b.frames[state])
        sources[f"{school_kit.PACK_DIR}/{b.name}-f{b.frames[state]:03d}"] = {"sha256": button_kit.sha256_file(path), "index_sha12": index_sha}
    parameters = {"circles_units": [{"cx": c.cx, "cy": c.cy, "radius": c.radius, "trim": c.trim, "art_size": [c.width, c.height]} for c in b.circles], "master": [image.w, image.h]}
    if b == DOME:
        parameters["dome"] = dome
    return {"part": ident, "rule": "ring_button_kit", "tool_version": TOOL_VERSION, "date": datetime.date.today().isoformat(), "source": source, "sources": sources, "empty": not any(image.data[3::4]),
            "parameters": parameters, "image_sha256": hashlib.sha256(image_path.read_bytes()).hexdigest(), "image_size": [image.w, image.h], "run_by": "agent"}


def selected(args: argparse.Namespace) -> list[Button]:
    only = set(args.only.split(",")) if args.only else None
    if only and only - {b.name for b in BUTTONS}:
        raise RingError(f"--only names buttons this slice does not hold: {sorted(only - {b.name for b in BUTTONS})}")
    return [b for b in BUTTONS if not only or b.name in only]


def frames_of(args: argparse.Namespace) -> Path:
    return args.frames or args.local_root / "census" / "frames"


def cmd_check(args: argparse.Namespace) -> int:
    for b in selected(args):
        master_of(b)
        for number in sorted(b.frames.values()):
            original_frame(frames_of(args), b, number)
        for state in b.frames:
            pack_frame(school_kit.pack_local(args.local_root), b, state)
            if args.source == "codex" and b != DOME:
                codex_frame(args.local_root, b, state)
    if args.dome == "olive" and not (args.local_root / "masters" / f"{OLIVE_PLATE}.png").is_file():
        raise RingError(f"no master {OLIVE_PLATE} (the accepted olive plate of #3202)")
    print(f"RING BUTTON KIT check PASS ({len(selected(args))} arts)")
    return 0


def cmd_build(args: argparse.Namespace) -> int:
    local_root, frames = args.local_root, frames_of(args)
    out = args.out or local_root / "masters"
    records = local_root / "provenance" / "rule" if args.out is None else out / "records"
    built = [(b, button_parts(local_root, frames, b, args.dome, args.source)) for b in selected(args)]
    for _, parts in built:
        for ident, image in parts.items():
            covered = sum(1 for i in range(3, len(image.data), 4) if image.data[i] > 0) / (image.w * image.h)
            print(f"{ident:34s} {image.w} x {image.h}  covered {covered * 100:5.1f} %")
    count = sum(len(parts) for _, parts in built)
    if args.dry_run:
        print(f"RING BUTTON KIT build DRY-RUN PASS ({count} parts, dome {args.dome}); nothing written")
        return 0
    out.mkdir(parents=True, exist_ok=True)
    records.mkdir(parents=True, exist_ok=True)
    for b, parts in built:
        for ident, image in parts.items():
            path = out / f"{ident}.png"
            pngio.write(path, image)
            (records / f"{ident}.json").write_text(json.dumps(record_of(local_root, frames, b, ident, args.dome, args.source, path, image), indent=2) + "\n", encoding="utf-8")
    print(f"RING BUTTON KIT build PASS ({count} parts written to {out}, dome {args.dome})")
    return 0


# --- the sheet --------------------------------------------------------------------------------------------------------------------

def shadowed(part: Image, b: Button, master: tuple[int, int]) -> Image:
    """The part over the wood-like ground with the run-time shadow of each of its discs."""
    circle = b.circles[0]
    if b in RINGED:
        return school_kit.staged(part, circle, master)
    margin = school_kit.margin_of(circle, master)   # the dome and the wells lie inside the wide button's brass: no shadow of their own
    return plain(part, (master[0] + 2 * margin[0], master[1] + 2 * margin[1]))


def plain(image: Image, size: tuple[int, int]) -> Image:
    """A frame as it is, centred over the ground on a canvas of `size` (no cut, no shadow)."""
    out = Image(size[0], size[1], bytearray((*GROUND, 255)) * (size[0] * size[1]))
    left, top = (size[0] - image.w) // 2, (size[1] - image.h) // 2
    for y in range(image.h):
        for x in range(image.w):
            k, o = (y * image.w + x) * 4, ((y + top) * size[0] + x + left) * 4
            a = image.data[k + 3] / 255.0
            for c in range(3):
                out.data[o + c] = int(round(image.data[k + c] * a + out.data[o + c] * (1.0 - a)))
    return out


def composed(parts: dict[str, Image], b: Button, state: str) -> Image:
    """What the runtime shows in a state: the face, and the state's layer over it."""
    face = parts[ident_of(b, "idle")]
    if state == "idle":
        return face
    out = Image(face.w, face.h, bytearray(face.data))
    layer = parts[ident_of(b, state)]
    for o in range(0, len(out.data), 4):
        a = layer.data[o + 3] / 255.0
        under = out.data[o + 3] / 255.0
        total = a + under * (1.0 - a)
        if total <= 0:
            continue
        for c in range(3):
            out.data[o + c] = int(round((layer.data[o + c] * a + out.data[o + c] * under * (1.0 - a)) / total))
        out.data[o + 3] = int(round(255 * total))
    return out


def cmd_sheet(args: argparse.Namespace) -> int:
    """Per button three rows: the original (nearest cells) | the old pack frame as it is | after (the kit parts; round buttons with the run-time shadow); a column per state."""
    local_root, frames = args.local_root, frames_of(args)
    local = school_kit.pack_local(local_root)
    if shutil.which("magick") is None:
        raise RingError("ImageMagick `magick` is not installed")
    buttons = selected(args)
    work = Path(tempfile.mkdtemp(prefix="ring-sheet-"))
    try:
        blocks = []
        columns = max(len(b.frames) for b in buttons)
        sizes = {b.name: tuple(m + 2 * g for m, g in zip(master_of(b), school_kit.margin_of(b.circles[0], master_of(b)))) for b in buttons}
        cell = (max(s[0] for s in sizes.values()) * args.zoom, max(s[1] for s in sizes.values()) * args.zoom)
        order = ("idle", "hover", "pressed")
        for b in buttons:
            master, size = master_of(b), sizes[b.name]
            if b == DOME:
                parts = {d: button_parts(local_root, frames, b, d) for d in DOMES}
            else:
                parts = {s: button_parts(local_root, frames, b, DOMES[0], s) for s in SOURCES}
            rows = [("original", lambda s: plain(school_kit.upscaled(original_frame(frames, b, b.frames[s]), master), size)),
                    ("old pack frame", lambda s: plain(school_kit.resampled(pack_frame(local, b, s)[0], *master), size))]
            if b == DOME:
                rows += [("after: olive plate", lambda s: shadowed(composed(parts["olive"], b, s), b, master)), ("after: pack cut", lambda s: shadowed(composed(parts["pack"], b, s), b, master))]
            else:
                rows += [("after: Codex", lambda s: shadowed(composed(parts["codex"], b, s), b, master)), ("after: pack cut", lambda s: shadowed(composed(parts["pack"], b, s), b, master))]
            lines = []
            for number, (label, make) in enumerate(rows):
                cells = []
                for state in order:
                    if state not in b.frames:
                        continue
                    path = work / f"{b.name}-{number}-{state}.png"
                    pngio.write(path, make(state))
                    subprocess.run(["magick", str(path), "-filter", "point" if label == "original" else "Lanczos", "-resize", f"{size[0] * args.zoom}x{size[1] * args.zoom}!", "-background", "#202020", "-gravity", "center",
                                    "-extent", f"{cell[0]}x{cell[1]}", "-font", LABEL_FONT, "-fill", "white", "-undercolor", "#000000aa", "-pointsize", "26", "-gravity", "southwest", "-annotate", "+4+2",
                                    f"{b.name} {state}: {label}", str(path)], check=True)
                    cells.append(path)
                line = work / f"{b.name}-{number}.png"
                subprocess.run(["magick", *cells, "+append", "-background", "#202020", "-gravity", "west", "-extent", f"{cell[0] * columns}x{cell[1]}", str(line)], check=True)
                lines.append(line)
            block = work / f"{b.name}.png"
            subprocess.run(["magick", *lines, "-background", "#202020", "-append", "-bordercolor", "#505050", "-border", "3", str(block)], check=True)
            blocks.append(block)
        subprocess.run(["magick", *blocks, "-background", "#202020", "+append" if args.across else "-append", str(args.out)], check=True)
    finally:
        shutil.rmtree(work, ignore_errors=True)
    print(f"RING BUTTON KIT sheet PASS: {args.out}")
    return 0


def self_test() -> int:
    """The unit tests, run as the package module (they import this tool relatively, so not in this process when it runs as a script)."""
    code = subprocess.run([sys.executable, "-B", "-m", "unittest", "tools.steamwoven_ui.test_ring_button_kit"], cwd=HERE.parents[1]).returncode
    print("RING BUTTON KIT self-test " + ("PASS" if code == 0 else "FAIL"))
    return 0 if code == 0 else 1


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("build", "check", "sheet", "codex-inbox", "codex-place"):
        s = sub.add_parser(name)
        s.add_argument("--only")
        s.add_argument("--local-root", type=Path, default=None)
        s.add_argument("--frames", type=Path)
        if name in ("build", "check"):
            s.add_argument("--dome", choices=DOMES, default=DOMES[0])
            s.add_argument("--source", choices=SOURCES, default=SOURCES[0], help="of the ringed buttons and the wells")
        if name == "codex-inbox":
            s.add_argument("--stage", choices=("idle", "states"), required=True)
        if name == "build":
            s.add_argument("--out", type=Path)
            s.add_argument("--dry-run", action="store_true")
        if name == "sheet":
            s.add_argument("--out", type=Path, required=True)
            s.add_argument("--zoom", type=int, default=2)
            s.add_argument("--across", action="store_true", help="the buttons side by side instead of one under another")
    sub.add_parser("self-test")
    args = parser.parse_args(argv)
    try:
        if args.command == "self-test":
            return self_test()
        args.local_root = args.local_root or prompt_library.default_local_root()
        return {"build": cmd_build, "check": cmd_check, "sheet": cmd_sheet, "codex-inbox": cmd_codex_inbox, "codex-place": cmd_codex_place}[args.command](args)
    except (RingError, school_kit.SchoolError, button_kit.ButtonError, pngio.PngError, OSError, ValueError, subprocess.CalledProcessError) as error:
        print(f"RING BUTTON KIT FAIL: {error}")
        return 2


if __name__ == "__main__":
    sys.exit(main())
