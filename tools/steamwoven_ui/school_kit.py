#!/usr/bin/env python3
"""ARC-UI-3204 (batch B-BUTTONS, slice 4): the college tabs of the spell window (16 arts, 24 x 24 units) and the skill-school buttons (12 arts, 31 to 34 x 31 to 32 units), round buttons with a coloured dome in a thin brass ring, three
frames each (idle 0, pressed 1, hover 2).

Owner decision 2026-11-25: the kit parts are the player's own HD-pack frames of these buttons AS THEY ARE (symbol, dome and ring; the old generated pack icons read better than any rebuilt dome or symbol and agree with each other), one frame
per state, cut to the circle of the ring by an ANALYTIC round alpha (an even smoothed edge: no corners of the old background, no staircase). The soft shadow under round parts is the run-time layer's (`AreaKitRoundParts`, #3249 and #3288: the
disc moved 13 px down and right, sigma 16 px, strength 0.7 of a 3.6 px unit), cast from the very same circle, so it fits the part. Nothing of the symbol, the dome or the ring is recalculated; no Codex item is used.

    python3 -B tools/steamwoven_ui/school_kit.py build [--only NAME,...] [--out DIR] [--dry-run]               (the parts -> masters/ and provenance/rule/)
    python3 -B tools/steamwoven_ui/school_kit.py check [--only NAME,...]                                       (every button has its three pack frames; the circles lie inside the frames)
    python3 -B tools/steamwoven_ui/school_kit.py codex-inbox --stage idle|states [--only NAME,...]            (the Codex batch B-BUTTONS-FIX for the three symbols the pack reads wrongly: the inbox, prompts and references)
    python3 -B tools/steamwoven_ui/school_kit.py codex-place [--only NAME,...]                               (the generated frames registered onto the pack frames' geometry -> work/B-BUTTONS-FIX/placed)
    python3 -B tools/steamwoven_ui/school_kit.py sheet --out FILE [--only NAME,...] [--zoom N] [--columns N]   (original | idle | hover | pressed over a wood ground with the run-time shadow, English labels)
    python3 -B tools/steamwoven_ui/school_kit.py self-test

The parts of a button (TRD SWI-KIT-5; the runtime's convention `SteamwovenKitComposer.ButtonLayers`: the face, over it the glyph, over both the overlay `<face>_hover` or `<face>_pressed`; all parts of a button are at the face's size, the
manifest row `plate.<name>`):

* `plate.<name>` the pack's idle frame (f000) cut to the ring's circle.
* `glyph.<name>` EMPTY (the symbol is in the pack frame; the runtime composes a face only with a glyph of its size).
* `plate.<name>_hover` the pack's hover frame (f002) in full, cut to the same circle: laid over the idle face it replaces it.
* `plate.<name>_pressed` the pack's pressed frame (f001) in full, cut the same way.

The circle is the one `AreaKitRoundParts` holds per art (the circle of the ring's gold measured on the original's frames plus the cut's half unit): read from that source file, so the part's edge and the run-time shadow's hole are one circle. The frame
is stretched to the master's size (the master is not always the art's aspect: 114 x 114 for a 32 x 31 art), the colour outside the circle is first replaced by the nearest colour inside it, so the smoothed edge takes nothing of the old frame's background.

Nothing here holds art: every picture is read from and written to the ignored `local/` (the pack is `local/hd-packs/interface-redraw-4x`, read only). Standard library only (`pngio.py`); ImageMagick for `sheet`.
Exit 0 = done, 1 = a check failed, 2 = an input could not be used.
"""
from __future__ import annotations

import argparse
import datetime
import hashlib
import json
import math
import re
import shutil
import subprocess
import sys
import tempfile
from dataclasses import dataclass
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
TOOL_VERSION = 2            # 1 was the first, rule-F build (the coloured dome and the symbol rebuilt; rejected 2026-11-25)
MANIFEST = HERE / "kit.json"
ROUND_PARTS_SOURCE = HERE.parents[1] / "tools" / "terrain-viewer" / "Core" / "AreaKitRoundParts.cs"   # the viewer's table of the round parts (the circle of each art) and the shadow's numbers
PACK_DIR = "hd-packs/interface-redraw-4x"   # under `local/`
STATE_FRAMES = {"idle": 0, "hover": 2, "pressed": 1}   # the pack's frame of each state (the original's order: f0 idle, f1 pressed, f2 hover)
OVERSHOOT = 0.5             # units: how far the cut's circle may lie beyond the art's rectangle (the ring of a few arts touches the frame's edge; the frame cuts it there, as at run time)
EXTRUDE_PX = 2.0            # source px: the colour inside this far from the circle's edge is carried outwards (the smoothed edge never mixes in the frame's old background)
LABEL_FONT = button_kit.LABEL_FONT
GROUND = (96, 68, 44)       # the wood-like ground of the sheet
CODEX_BATCH = "B-BUTTONS-FIX"
CODEX_GROUND = (128, 128, 128)   # the flat ground of the Codex input and output
CODEX_SCALE = 32            # input px per art unit (the pack frame is 4 px per unit, so 8 times; the original, 24 x 24 units, 32 times with hard pixels)
CODEX_FG_DE = 36.0          # RGB distance from the ground at which a pixel of the generated image belongs to the button


class SchoolError(Exception):
    pass


@dataclass(frozen=True)
class Tab:
    art: int
    folder: str
    name: str          # `plate.<name>`, `glyph.<name>`
    group: str         # `college` or `school`


def _college(art: int, name: str) -> Tab:
    return Tab(art, f"{art}-{name}", name, "college")


def _school(art: int, folder: str) -> Tab:
    return Tab(art, f"{art}-{folder}", folder.lower(), "school")


TABS = (
    _college(11, "s_conveyance"), _college(38, "s_air"), _college(44, "s_divination"), _college(50, "s_fire"), _college(56, "s_forc"), _college(62, "s_meta"), _college(68, "s_morph"), _college(74, "s_nature"),
    _college(80, "s_phantasm"), _college(86, "s_water"), _college(92, "s_earth"), _college(98, "s_mental"), _college(104, "s_evilnecro"), _college(110, "s_goodnecro"), _college(116, "s_summoning"), _college(122, "s_temporal"),
    _school(302, "Combat_But"), _school(303, "Thieving_But"), _school(304, "Social_But"), _school(305, "Technological_But"), _school(306, "Anatomical_But"), _school(307, "Chemistry_But"),
    _school(308, "Electrical_But"), _school(309, "Explosives_But"), _school(310, "GunSmithy_But"), _school(311, "Mechanical_But"), _school(312, "Smithy_But"), _school(313, "Therapeutics_But"),
)
GROUPS = ("college", "school")


def tabs_of(group: str | None) -> list[Tab]:
    if group is not None and group not in GROUPS:
        raise SchoolError(f"no group {group!r}; the groups are {list(GROUPS)}")
    return [t for t in TABS if group is None or t.group == group]


def tab_named(name: str) -> Tab:
    for t in TABS:
        if t.name == name:
            return t
    raise SchoolError(f"{name} is no button of this slice")


# --- the circle of each round part and the shadow (the viewer's own numbers) -----------------------------------------------------

@dataclass(frozen=True)
class Circle:
    """One round part's art: its size in original units and the circle of its ring's gold (centre and outer radius in units from the art's top-left corner); the cut keeps `trim` more."""
    width: int
    height: int
    cx: float
    cy: float
    radius: float
    trim: float

    @property
    def cut(self) -> float:
        return self.radius + self.trim


@dataclass(frozen=True)
class Shadow:
    offset: float     # units
    sigma: float      # units
    strength: float


_CIRCLES: dict[int, Circle] | None = None
_SHADOW: Shadow | None = None


def _viewer_source() -> str:
    try:
        return ROUND_PARTS_SOURCE.read_text(encoding="utf-8")
    except OSError as error:
        raise SchoolError(f"the viewer's round-part table cannot be read: {error}") from error


def circles() -> dict[int, Circle]:
    """The circle of every art in `AreaKitRoundParts` (one source for the cut and the run-time shadow)."""
    global _CIRCLES
    if _CIRCLES is None:
        text = _viewer_source()
        trim = re.search(r"const float Trim = ([0-9.]+)f", text)
        if trim is None:
            raise SchoolError("AreaKitRoundParts: no Trim constant found")
        rows = re.findall(r"\[(\d+)\] = new\((\d+), (\d+), ([0-9.]+)f, ([0-9.]+)f, ([0-9.]+)f\)", text)
        found = {int(a): Circle(int(w), int(h), float(cx), float(cy), float(r), float(trim.group(1))) for a, w, h, cx, cy, r in rows}
        missing = [t.name for t in TABS if t.art not in found]
        if missing:
            raise SchoolError(f"AreaKitRoundParts: no circle for {missing} (the table's format changed?)")
        _CIRCLES = found
    return _CIRCLES


def shadow() -> Shadow:
    global _SHADOW
    if _SHADOW is None:
        m = re.search(r"ShadowOffset = ([0-9.]+)f / ([0-9.]+)f, ShadowSigma = ([0-9.]+)f / ([0-9.]+)f, ShadowStrength = ([0-9.]+)f", _viewer_source())
        if m is None:
            raise SchoolError("AreaKitRoundParts: no shadow constants found")
        _SHADOW = Shadow(float(m.group(1)) / float(m.group(2)), float(m.group(3)) / float(m.group(4)), float(m.group(5)))
    return _SHADOW


def circle_of(tab: Tab) -> Circle:
    found = circles().get(tab.art)
    if found is None:
        raise SchoolError(f"AreaKitRoundParts has no circle for art {tab.art} ({tab.name})")
    return found


_MANIFEST: dict | None = None


def master_of(name: str) -> tuple[int, int]:
    """The plate master's size of a button (the manifest row `plate.<name>`)."""
    global _MANIFEST
    if _MANIFEST is None:
        _MANIFEST = json.loads(MANIFEST.read_text(encoding="utf-8"))
    for part in _MANIFEST["parts"]:
        if part["id"] == f"plate.{name}" and part.get("size"):
            return part["size"][0], part["size"][1]
    raise SchoolError(f"the manifest has no sized row plate.{name}")


# --- the pack ---------------------------------------------------------------------------------------------------------------------

def pack_path(local: Path, name: str, number: int) -> tuple[Path, str]:
    """The pack frame of an art and the sha12 its index states."""
    root = local / PACK_DIR
    index = root / "index.tsv"
    if not index.is_file():
        raise SchoolError(f"no HD pack index at {index}")
    for line in index.read_text(encoding="utf-8").splitlines():
        fields = line.split("\t")
        if len(fields) > 12 and fields[2] == name and fields[3] == str(number):
            path = (root / fields[4]).resolve()
            if not path.is_relative_to(root.resolve()):
                raise SchoolError(f"the pack's index points outside the pack: {fields[4]}")
            return path, fields[12]
    raise SchoolError(f"the pack has no frame {number} of {name}")


def check_aspect(tab: "Tab", frame: Image, state: str) -> None:
    """A pack frame must have the art's aspect (a frame rebuilt at another one would be stretched out of shape)."""
    circle = circle_of(tab)
    ux, uy = frame.w / circle.width, frame.h / circle.height
    if abs(ux - uy) > 0.05 * ux:
        raise SchoolError(f"{tab.name} {state}: the pack frame {frame.w} x {frame.h} is not the art's aspect {circle.width} x {circle.height}")


def read_pack(local: Path, name: str, number: int) -> tuple[Image, Path]:
    path, _ = pack_path(local, name, number)
    try:
        return pngio.read(path), path
    except (OSError, pngio.PngError) as error:
        raise SchoolError(f"{path}: {error}") from error


# --- the part ---------------------------------------------------------------------------------------------------------------------

def extruded(frame: Image, circle: Circle) -> Image:
    """`frame` with the colour outside the circle's edge (less `EXTRUDE_PX`) replaced by the nearest one inside: the frame's old background never reaches the smoothed edge."""
    ux, uy = frame.w / circle.width, frame.h / circle.height
    cx, cy, rx, ry = circle.cx * ux, circle.cy * uy, circle.cut * ux, circle.cut * uy
    inner = 1.0 - EXTRUDE_PX / min(rx, ry)
    out = Image(frame.w, frame.h, bytearray(frame.data))
    for y in range(frame.h):
        for x in range(frame.w):
            nx, ny = (x + 0.5 - cx) / rx, (y + 0.5 - cy) / ry
            d = math.hypot(nx, ny)
            if d <= inner:
                continue
            sx, sy = cx + nx / d * inner * rx, cy + ny / d * inner * ry
            i = (max(0, min(frame.h - 1, int(sy))) * frame.w + max(0, min(frame.w - 1, int(sx)))) * 4
            out.data[(y * frame.w + x) * 4:(y * frame.w + x) * 4 + 3] = frame.data[i:i + 3]
    return out


def resampled(frame: Image, w: int, h: int) -> Image:
    """The frame at w x h by area averaging (the whole of each source pixel's overlap with the target pixel, weighted by its alpha), straight RGBA."""
    out = Image(w, h, bytearray(w * h * 4))
    sx, sy = frame.w / w, frame.h / h
    for y in range(h):
        y0, y1 = y * sy, (y + 1) * sy
        for x in range(w):
            x0, x1 = x * sx, (x + 1) * sx
            acc = [0.0] * 4
            weight = 0.0
            for j in range(int(y0), min(frame.h, int(math.ceil(y1)))):
                wy = min(y1, j + 1) - max(y0, j)
                for i in range(int(x0), min(frame.w, int(math.ceil(x1)))):
                    area = wy * (min(x1, i + 1) - max(x0, i))
                    k = (j * frame.w + i) * 4
                    a = frame.data[k + 3] / 255.0
                    acc[0] += frame.data[k] * a * area
                    acc[1] += frame.data[k + 1] * a * area
                    acc[2] += frame.data[k + 2] * a * area
                    acc[3] += a * area
                    weight += area
            o = (y * w + x) * 4
            if acc[3] > 0:
                for c in range(3):
                    out.data[o + c] = int(round(acc[c] / acc[3]))
                out.data[o + 3] = int(round(255 * acc[3] / weight))
    return out


def coverage(circle: Circle, master: tuple[int, int], x: float, y: float) -> float:
    """The analytic round alpha at the master pixel (x, y) (its centre): one pixel wide, centred on the circle (an ellipse when the master's aspect is not the art's)."""
    mx, my = master[0] / circle.width, master[1] / circle.height
    rx, ry = circle.cut * mx, circle.cut * my
    d = (math.hypot((x - circle.cx * mx) / rx, (y - circle.cy * my) / ry) - 1.0) * math.sqrt(rx * ry)
    return max(0.0, min(1.0, 0.5 - d))


def round_part(frame: Image, circle: Circle, master: tuple[int, int]) -> Image:
    """A pack frame as a kit part: stretched to the master, cut to the circle by the analytic alpha."""
    out = resampled(extruded(frame, circle), *master)
    for y in range(master[1]):
        for x in range(master[0]):
            k = (y * master[0] + x) * 4 + 3
            out.data[k] = int(round(out.data[k] * coverage(circle, master, x + 0.5, y + 0.5)))
    return out


def empty_layer(master: tuple[int, int]) -> Image:
    return Image(master[0], master[1], bytearray(master[0] * master[1] * 4))


def state_frame(local: Path, tab: Tab, state: str, local_root: Path | None = None) -> tuple[Image, str]:
    """The frame of a state and where it comes from: the Codex-redrawn one (`codex-place`) if the button has it, else the pack's."""
    fixed = fixed_frame(local_root, tab.name, STATE_FRAMES[state])
    if fixed is not None:
        return pngio.read(fixed), "codex"
    return read_pack(local, tab.name, STATE_FRAMES[state])[0], "pack"


def tab_parts(local: Path, tab: Tab, local_root: Path | None = None) -> dict[str, Image]:
    circle, master = circle_of(tab), master_of(tab.name)
    frames = {state: state_frame(local, tab, state, local_root)[0] for state in STATE_FRAMES}
    for state, frame in frames.items():
        check_aspect(tab, frame, state)
    return {f"plate.{tab.name}": round_part(frames["idle"], circle, master),
            f"glyph.{tab.name}": empty_layer(master),
            f"plate.{tab.name}_hover": round_part(frames["hover"], circle, master),
            f"plate.{tab.name}_pressed": round_part(frames["pressed"], circle, master)}


def build_parts(local: Path, tabs: list[Tab], local_root: Path | None = None) -> dict[str, Image]:
    parts: dict[str, Image] = {}
    for tab in tabs:
        parts.update(tab_parts(local, tab, local_root))
    return parts


# --- the shadow of the sheet (the run-time layer's formula, for the composite) ----------------------------------------------------

def shadow_alpha(circle: Circle, units_x: float, units_y: float, pixel: float) -> float:
    """`AreaKitRoundParts.ShadowAt`: the disc moved by the offset, a logistic edge of the blur, times the strength, nothing where the part itself is."""
    s = shadow()
    moved = math.hypot(units_x - circle.cx - s.offset, units_y - circle.cy - s.offset) - circle.cut
    edge = 1.0 / (1.0 + math.exp(max(-60.0, min(60.0, 1.702 * moved / s.sigma))))
    keep = max(0.0, min(1.0, 0.5 - (math.hypot(units_x - circle.cx, units_y - circle.cy) - circle.cut) / max(pixel, 1e-4)))
    return s.strength * edge * (1.0 - keep)


MARGIN_UNITS = 8.0   # art units of the ground left round a part on the sheet (the run-time shadow reaches its offset and a few sigma; the sheet cuts it at about two)


def margin_of(circle: Circle, master: tuple[int, int]) -> tuple[int, int]:
    return int(math.ceil(MARGIN_UNITS * master[0] / circle.width)), int(math.ceil(MARGIN_UNITS * master[1] / circle.height))


def staged(part: Image, circle: Circle, master: tuple[int, int]) -> Image:
    """The part over the wood-like ground with the run-time shadow (the master pixel grid, a margin all round)."""
    mw, mh = margin_of(circle, master)
    w, h = master[0] + 2 * mw, master[1] + 2 * mh
    out = Image(w, h, bytearray(w * h * 4))
    mx, my = master[0] / circle.width, master[1] / circle.height
    for y in range(h):
        for x in range(w):
            px, py = x - mw + 0.5, y - mh + 0.5
            a = shadow_alpha(circle, px / mx, py / my, 1.0 / mx)
            base = [c * (1.0 - a) for c in GROUND]
            if 0 <= px < master[0] and 0 <= py < master[1]:
                k = (int(py) * master[0] + int(px)) * 4
                pa = part.data[k + 3] / 255.0
                base = [part.data[k + c] * pa + base[c] * (1.0 - pa) for c in range(3)]
            o = (y * w + x) * 4
            out.data[o:o + 3] = bytes(int(round(max(0.0, min(255.0, v)))) for v in base)
            out.data[o + 3] = 255
    return out


# --- commands ---------------------------------------------------------------------------------------------------------------------

def need_magick() -> None:
    if shutil.which("magick") is None:
        raise SchoolError("ImageMagick `magick` is not installed")


def pack_local(local_root: Path) -> Path:
    """The `local/` folder the HD pack lies under: the parent of `local/steamwoven-ui` (`--local-root`)."""
    return local_root.resolve().parent


def selected(args: argparse.Namespace) -> list[Tab]:
    only = set(args.only.split(",")) if getattr(args, "only", None) else None
    if only and only - {t.name for t in TABS}:
        raise SchoolError(f"--only names buttons this slice does not hold: {sorted(only - {t.name for t in TABS})}")
    tabs = [t for t in TABS if not only or t.name in only]
    if not tabs:
        raise SchoolError("no button selected")
    return tabs


def record_of(local: Path, tab: Tab, ident: str, image_path: Path, image: Image, local_root: Path | None = None) -> dict:
    """The provenance record of a part (SWI-GEN-6): no generator, the rule, and the hash of the pack frame it was cut from."""
    state = "idle" if ident == f"plate.{tab.name}" else ("hover" if ident.endswith("_hover") else "pressed")
    sources = {}
    source = "none (empty layer)"
    if ident != f"glyph.{tab.name}":
        path, index_sha = pack_path(local, tab.name, STATE_FRAMES[state])
        sources = {f"{PACK_DIR}/{tab.name}-f{STATE_FRAMES[state]:03d}": {"sha256": button_kit.sha256_file(path), "index_sha12": index_sha}}
        source = "pack"
        fixed = fixed_frame(local_root, tab.name, STATE_FRAMES[state])
        if fixed is not None:   # a Codex-redrawn frame: the pack frame was its input, its generation record names the prompt and the references
            source = "codex"
            generation = codex_dirs(local_root)[1] / f"fix-{tab.name}-{state}.provenance.json"
            sources["codex_frame"] = {"sha256": button_kit.sha256_file(fixed), "generation_record": str(generation.relative_to(local_root)) if generation.is_file() else None,
                                      "generation_sha256": button_kit.sha256_file(generation) if generation.is_file() else None, "input": "pack frame + original x32 (+ idle sample for hover and pressed)"}
    circle = circle_of(tab)
    return {"part": ident, "rule": "school_kit", "tool_version": TOOL_VERSION, "date": datetime.date.today().isoformat(), "source": source,
            "sources": sources, "empty": not any(image.data[3::4]),
            "parameters": {"circle_units": {"cx": circle.cx, "cy": circle.cy, "radius": circle.radius, "trim": circle.trim, "art_size": [circle.width, circle.height]}, "extrude_px": EXTRUDE_PX, "master": [image.w, image.h]},
            "image_sha256": hashlib.sha256(image_path.read_bytes()).hexdigest(), "image_size": [image.w, image.h], "run_by": "agent"}


# --- Codex-redrawn frames for the symbols the pack reads wrongly (owner decision 2026-11-25) ------------------------------------------

CODEX_SYMBOLS = {   # per button: what the symbol is (the prompt's own words, ASCII)
    "s_nature": "a dark, nearly black, crisp silhouette of a tree: one straight vertical trunk, a few short branches, and a crown made of a few solid leaf-cluster shapes along the top and the sides of the branches. Every shape is solid and clean with smooth edges: no ragged blotches, no speckles, no pale spots inside the crown.",
    "s_water": "two continuous, parallel, wavy zig-zag lines, the sign of water (like the sign of Aquarius): two unbroken dark lines of the same even width, one above the other, both running the whole width of the symbol, every wave alike, with no breaks, no gaps and no blobs.",
    "s_forc": "the dark symbol of image 1 and image 2 (a short bar with a stem at the upper left, a diagonal stroke, and a curved hook-shaped tail at the bottom). The hook tail at the bottom must be ONE whole, sharp, clean curved hook of an even width, never broken, blurred, cut off or missing.",
}
CODEX_STATE_WORDS = {"idle": "IDLE (the dome as in image 1)", "hover": "HOVER (the dome is lit brighter, exactly as in image 1)", "pressed": "PRESSED (the dome is lit brightest, exactly as in image 1)"}


def codex_prompt(name: str, state: str) -> str:
    first = ("Image 1 is a round game-interface button (a coloured glossy dome in a thin brass ring) of a steampunk role-playing game, on a flat mid-grey ground. Image 2 is the ORIGINAL low-resolution version of the same button, enlarged with hard pixels: "
             "it is the truth for what the symbol is, where it stands and how big it is." + ("" if state == "idle" else " Image 3 is the finished IDLE version of this button: draw the symbol EXACTLY as it is in image 3."))
    return (first + "\n\nEdit image 1. Keep EVERYTHING of image 1 exactly as it is: the dome colour, the surface texture, the light and the highlights, the brass ring, the dark outline, the position and the size of the button and the flat mid-grey ground. "
            "Change only the symbol on the dome and redraw it cleanly.\n\nSYMBOL: " + CODEX_SYMBOLS[name] + "\n\nSame symbol, same position, same size and same proportions as the symbol in image 2; clean even lines, no reinterpretation, no extra details, no text, no numbers, no new glow, no new shine, no shadow outside the button. "
            f"State: {CODEX_STATE_WORDS[state]}.\n\nOutput one PNG with the same aspect and the same framing as image 1: the button in the middle of the flat mid-grey ground, nothing else.\n")


def staged_input(frame: Image, circle: Circle) -> Image:
    """A pack frame at `CODEX_SCALE` px per art unit on the flat ground: bilinear, cut to the circle by the analytic alpha (nothing of the frame's old background)."""
    w, h = circle.width * CODEX_SCALE, circle.height * CODEX_SCALE
    src = extruded(frame, circle)
    out = Image(w, h, bytearray(w * h * 4))
    ux, uy = src.w / w, src.h / h
    for y in range(h):
        fy = max(0.0, min(src.h - 1.0, (y + 0.5) * uy - 0.5))
        y0 = int(fy)
        y1 = min(src.h - 1, y0 + 1)
        ty = fy - y0
        for x in range(w):
            fx = max(0.0, min(src.w - 1.0, (x + 0.5) * ux - 0.5))
            x0 = int(fx)
            x1 = min(src.w - 1, x0 + 1)
            tx = fx - x0
            a = coverage(circle, (w, h), x + 0.5, y + 0.5)
            o = (y * w + x) * 4
            for c in range(3):
                v = (src.data[(y0 * src.w + x0) * 4 + c] * (1 - tx) + src.data[(y0 * src.w + x1) * 4 + c] * tx) * (1 - ty) + (src.data[(y1 * src.w + x0) * 4 + c] * (1 - tx) + src.data[(y1 * src.w + x1) * 4 + c] * tx) * ty
                out.data[o + c] = int(round(CODEX_GROUND[c] * (1 - a) + v * a))
            out.data[o + 3] = 255
    return out


def staged_original(original: Image, circle: Circle) -> Image:
    """The original frame at `CODEX_SCALE` px per art unit with hard pixels (nearest), on the flat ground, cut to the circle."""
    w, h = circle.width * CODEX_SCALE, circle.height * CODEX_SCALE
    out = Image(w, h, bytearray(w * h * 4))
    for y in range(h):
        sy = min(original.h - 1, int((y + 0.5) * original.h / h))
        for x in range(w):
            sx = min(original.w - 1, int((x + 0.5) * original.w / w))
            a = coverage(circle, (w, h), x + 0.5, y + 0.5)
            o = (y * w + x) * 4
            for c in range(3):
                out.data[o + c] = int(round(CODEX_GROUND[c] * (1 - a) + original.data[(sy * original.w + sx) * 4 + c] * a))
            out.data[o + 3] = 255
    return out


def disc_of_image(image: Image) -> tuple[float, float, float]:
    """The button's disc on a flat ground: the centre and radius of the bounding box of the largest piece that differs from the ground."""
    w, h = image.w, image.h
    on = [math.dist(image.data[i * 4:i * 4 + 3], CODEX_GROUND) > CODEX_FG_DE for i in range(w * h)]
    seen = [False] * (w * h)
    best: list[int] = []
    for start in range(w * h):
        if not on[start] or seen[start]:
            continue
        stack, piece = [start], []
        seen[start] = True
        while stack:
            i = stack.pop()
            piece.append(i)
            x, y = i % w, i // w
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                if 0 <= x + dx < w and 0 <= y + dy < h and on[(y + dy) * w + x + dx] and not seen[(y + dy) * w + x + dx]:
                    seen[(y + dy) * w + x + dx] = True
                    stack.append((y + dy) * w + x + dx)
        if len(piece) > len(best):
            best = piece
    if not best:
        raise SchoolError("no button found on the flat ground")
    xs, ys = [i % w for i in best], [i // w for i in best]
    return (min(xs) + max(xs) + 1) / 2, (min(ys) + max(ys) + 1) / 2, (max(xs) - min(xs) + max(ys) - min(ys) + 2) / 4


def placed_frame(generated: Image, sent: Image, size: tuple[int, int], shift: tuple[int, int] = (0, 0)) -> Image:
    """The generated image registered onto the pack frame's geometry (`size` px): the button's disc in it is mapped onto the disc of what was SENT (a shift and one uniform scale), box-averaged down to the frame's pixels."""
    cx_in, cy_in, r_in = disc_of_image(sent)
    cx_out, cy_out, r_out = disc_of_image(generated)
    k = r_out / r_in
    w, h = size
    step = sent.w / w                       # sent px per frame pixel
    foot = max(1, int(round(step * k)))     # generated px per frame pixel
    out = Image(w, h, bytearray(w * h * 4))
    for y in range(h):
        for x in range(w):
            ox = cx_out + ((x + 0.5 + shift[0]) * step - cx_in) * k
            oy = cy_out + ((y + 0.5 + shift[1]) * step - cy_in) * k
            acc = [0.0, 0.0, 0.0]
            count = 0
            for j in range(foot):
                py = int(oy - foot / 2 + j + 0.5)
                for i in range(foot):
                    px = int(ox - foot / 2 + i + 0.5)
                    if 0 <= px < generated.w and 0 <= py < generated.h:
                        k4 = (py * generated.w + px) * 4
                        acc[0] += generated.data[k4]
                        acc[1] += generated.data[k4 + 1]
                        acc[2] += generated.data[k4 + 2]
                        count += 1
            o = (y * w + x) * 4
            for c in range(3):
                out.data[o + c] = int(round(acc[c] / count)) if count else CODEX_GROUND[c]
            out.data[o + 3] = 255
    return out


ALIGN_REACH = 4             # frame px: how far a state's symbol may be shifted onto the idle one
ALIGN_DARK = 0.45           # a symbol pixel is darker than this share of the dome's median lightness (the symbol stays dark in every state)


def dark_symbol(frame: Image, circle: Circle) -> list[bool]:
    """The frame's near-black symbol pixels inside the dome (for the alignment of the states only)."""
    ux, uy = frame.w / circle.width, frame.h / circle.height
    cx, cy, r = circle.cx * ux, circle.cy * uy, circle.radius * ux * 0.78
    lum = {}
    for y in range(frame.h):
        for x in range(frame.w):
            if math.hypot(x + 0.5 - cx, y + 0.5 - cy) <= r:
                k = (y * frame.w + x) * 4
                lum[y * frame.w + x] = 0.299 * frame.data[k] + 0.587 * frame.data[k + 1] + 0.114 * frame.data[k + 2]
    median = sorted(lum.values())[len(lum) // 2]
    return [i in lum and lum[i] < ALIGN_DARK * median for i in range(frame.w * frame.h)]


def aligned_shift(idle: Image, state: Image, circle: Circle) -> tuple[int, int]:
    """The whole-pixel shift of `state` (within `ALIGN_REACH`) that puts its symbol best over the idle one's (overlap of the dark symbol pixels): the states of a redrawn button must show ONE symbol."""
    a, b = dark_symbol(idle, circle), dark_symbol(state, circle)
    w, h = idle.w, idle.h
    best = (-1.0, 0, 0)
    for dy in range(-ALIGN_REACH, ALIGN_REACH + 1):
        for dx in range(-ALIGN_REACH, ALIGN_REACH + 1):
            inter = union = 0
            for i in range(w * h):
                x, y = i % w + dx, i // w + dy
                on_b = 0 <= x < w and 0 <= y < h and b[y * w + x]
                inter += a[i] and on_b
                union += a[i] or on_b
            score = inter / union if union else 0.0
            if score > best[0] + 1e-9 or (abs(score - best[0]) <= 1e-9 and abs(dx) + abs(dy) < abs(best[1]) + abs(best[2])):
                best = (score, dx, dy)
    return best[1], best[2]


def codex_dirs(local_root: Path) -> tuple[Path, Path, Path]:
    return local_root / "inbox" / CODEX_BATCH, local_root / "outbox" / CODEX_BATCH, local_root / "work" / CODEX_BATCH / "placed"


def fixed_frame(local_root: Path | None, name: str, number: int) -> Path | None:
    """The Codex-redrawn frame of a button's state, once `codex-place` made it (None: the pack's own frame stands)."""
    if local_root is None or name not in CODEX_SYMBOLS:
        return None
    path = codex_dirs(local_root)[2] / f"{name}-f{number}.png"
    return path if path.is_file() else None


def cmd_codex_inbox(args: argparse.Namespace) -> int:
    local_root = args.local_root
    local = pack_local(local_root)
    frames = args.frames or local_root / "census" / "frames"
    inbox, outbox, placed = codex_dirs(local_root)
    states = ("idle",) if args.stage == "idle" else ("hover", "pressed")
    names = [t for t in TABS if t.name in CODEX_SYMBOLS and (not args.only or t.name in args.only.split(","))]
    if not names:
        raise SchoolError("no button selected")
    (inbox / "prompts").mkdir(parents=True, exist_ok=True)
    items = []
    for tab in names:
        circle = circle_of(tab)
        for state in states:
            number = STATE_FRAMES[state]
            item = f"fix-{tab.name}-{state}"
            refs_dir = inbox / "refs" / item
            refs_dir.mkdir(parents=True, exist_ok=True)
            pack_frame = read_pack(local, tab.name, number)[0]
            refs = [("image", "pack-frame.png", staged_input(pack_frame, circle)), ("original", "original-x32.png", staged_original(pngio.read(frames / tab.folder / f"f{number}.png"), circle))]
            if state != "idle":
                done = placed / f"{tab.name}-f0.png"
                if not done.is_file():
                    raise SchoolError(f"{done} is missing: run the idle stage and `codex-place` first")
                refs.append(("idle", "idle-sample.png", staged_input(pngio.read(done), circle)))
            ref_rows = []
            for slot, filename, image in refs:
                path = refs_dir / filename
                pngio.write(path, image)
                ref_rows.append({"slot": slot, "file": f"refs/{item}/{filename}", "sha256": button_kit.sha256_file(path)})
            text = codex_prompt(tab.name, state)
            prompt = inbox / "prompts" / f"{item}.txt"
            prompt.write_text(text, encoding="utf-8")
            items.append({"id": item, "title": f"{tab.name} ({state}): the symbol redrawn on the pack frame", "prompt_file": f"prompts/{item}.txt", "prompt_sha256": hashlib.sha256(prompt.read_bytes()).hexdigest(),
                          "size": [circle.width * CODEX_SCALE, circle.height * CODEX_SCALE], "references": ref_rows, "outbox_file": f"outbox/{CODEX_BATCH}/{item}.png"})
    batch = inbox / "batch.json"
    old = json.loads(batch.read_text(encoding="utf-8")) if batch.is_file() else {"batch": CODEX_BATCH, "after": None, "items": []}
    keep = [i for i in old["items"] if i["id"] not in {n["id"] for n in items}]
    batch.write_text(json.dumps({"batch": CODEX_BATCH, "after": None, "items": keep + items}, indent=2) + "\n", encoding="utf-8")
    outbox.mkdir(parents=True, exist_ok=True)
    print(f"SCHOOL KIT codex-inbox PASS: {len(items)} item(s) in {inbox}")
    return 0


def cmd_codex_place(args: argparse.Namespace) -> int:
    local_root = args.local_root
    local = pack_local(local_root)
    frames = args.frames or local_root / "census" / "frames"
    _, outbox, placed = codex_dirs(local_root)
    placed.mkdir(parents=True, exist_ok=True)
    count = 0
    for tab in [t for t in TABS if t.name in CODEX_SYMBOLS and (not args.only or t.name in args.only.split(","))]:
        circle = circle_of(tab)
        for state, number in STATE_FRAMES.items():
            generated = outbox / f"fix-{tab.name}-{state}.png"
            if not generated.is_file():
                continue
            pack_frame = read_pack(local, tab.name, number)[0]
            sent = staged_input(pack_frame, circle)
            generated_image = pngio.read(generated)
            frame = placed_frame(generated_image, sent, (pack_frame.w, pack_frame.h))
            moved = (0, 0)
            if state != "idle" and (placed / f"{tab.name}-f0.png").is_file():   # the states of one button show ONE symbol: shift this state's onto the idle one's
                moved = aligned_shift(pngio.read(placed / f"{tab.name}-f0.png"), frame, circle)
                if moved != (0, 0):
                    frame = placed_frame(generated_image, sent, (pack_frame.w, pack_frame.h), moved)
            pngio.write(placed / f"{tab.name}-f{number}.png", frame)
            count += 1
            print(f"{tab.name} {state}: placed {pack_frame.w} x {pack_frame.h}" + (f", symbol shifted by {moved} px onto the idle one's" if moved != (0, 0) else ""))
    print(f"SCHOOL KIT codex-place PASS ({count} frame(s) in {placed})")
    return 0 if count else 1



def cmd_check(args: argparse.Namespace) -> int:
    local = pack_local(args.local_root)
    bad = []
    for tab in selected(args):
        circle = circle_of(tab)
        if min(circle.cx - circle.cut, circle.cy - circle.cut, circle.width - circle.cx - circle.cut, circle.height - circle.cy - circle.cut) < -OVERSHOOT:
            bad.append(f"{tab.name}: the circle leaves the art by more than {OVERSHOOT} unit")
        for state, number in STATE_FRAMES.items():
            try:
                check_aspect(tab, read_pack(local, tab.name, number)[0], state)
            except SchoolError as error:
                bad.append(str(error))
    for line in bad:
        print(line)
    print("SCHOOL KIT check " + ("FAIL" if bad else f"PASS ({len(selected(args))} buttons, three pack frames each, circles inside the art)"))
    return 1 if bad else 0


def cmd_build(args: argparse.Namespace) -> int:
    local_root = args.local_root
    local = pack_local(local_root)
    out = args.out or local_root / "masters"
    records = local_root / "provenance" / "rule" if args.out is None else out / "records"
    tabs = selected(args)
    parts = build_parts(local, tabs, local_root)
    for ident, image in parts.items():
        covered = sum(1 for i in range(3, len(image.data), 4) if image.data[i] > 0) / (image.w * image.h)
        print(f"{ident:34s} {image.w} x {image.h}  covered {covered * 100:5.1f} %")
    if args.dry_run:
        print(f"SCHOOL KIT build DRY-RUN PASS ({len(parts)} parts); nothing written")
        return 0
    out.mkdir(parents=True, exist_ok=True)
    records.mkdir(parents=True, exist_ok=True)
    by_ident = {f"{kind}.{t.name}{suffix}": t for t in tabs for kind, suffix in (("plate", ""), ("glyph", ""), ("plate", "_hover"), ("plate", "_pressed"))}
    for ident, image in parts.items():
        path = out / f"{ident}.png"
        pngio.write(path, image)
        (records / f"{ident}.json").write_text(json.dumps(record_of(local, by_ident[ident], ident, path, image, local_root), indent=2) + "\n", encoding="utf-8")
    print(f"SCHOOL KIT build PASS ({len(parts)} parts written to {out})")
    return 0


def upscaled(frame: Image, pixels: tuple[int, int]) -> Image:
    """An original frame at the master size: nearest cells (the 'original' column is the original's own pixels)."""
    pw, ph = pixels
    out = Image(pw, ph, bytearray(pw * ph * 4))
    for py in range(ph):
        cy = min(frame.h - 1, int((py + 0.5) * frame.h / ph))
        for px in range(pw):
            cx = min(frame.w - 1, int((px + 0.5) * frame.w / pw))
            out.data[(py * pw + px) * 4:(py * pw + px) * 4 + 4] = frame.data[(cy * frame.w + cx) * 4:(cy * frame.w + cx) * 4 + 4]
    return out


def cmd_sheet(args: argparse.Namespace) -> int:
    """original (nearest cells) | idle | hover | pressed per button, each part over a wood-like ground with the run-time shadow (the original on the same ground, uncut)."""
    local_root = args.local_root
    local = pack_local(local_root)
    frames = args.frames or local_root / "census" / "frames"
    need_magick()
    tabs = selected(args)
    work = Path(tempfile.mkdtemp(prefix="school-sheet-"))
    try:
        rows = []
        cell = (max(master_of(t.name)[0] + 2 * margin_of(circle_of(t), master_of(t.name))[0] for t in tabs) * args.zoom,
                max(master_of(t.name)[1] + 2 * margin_of(circle_of(t), master_of(t.name))[1] for t in tabs) * args.zoom)
        for tab in tabs:
            circle, master = circle_of(tab), master_of(tab.name)
            parts = tab_parts(local, tab, local_root)
            original = upscaled(pngio.read(frames / tab.folder / "f0.png"), master)
            shots = [("original", original), ("idle", parts[f"plate.{tab.name}"]), ("hover", parts[f"plate.{tab.name}_hover"]), ("pressed", parts[f"plate.{tab.name}_pressed"])]
            margin = margin_of(circle, master)
            size = (master[0] + 2 * margin[0], master[1] + 2 * margin[1])
            cells = []
            for tag, image in shots:
                path = work / f"{tab.name}-{tag}.png"
                pngio.write(path, staged(image, circle, master))
                subprocess.run(["magick", str(path), "-filter", "point" if tag == "original" else "Lanczos", "-resize", f"{size[0] * args.zoom}x{size[1] * args.zoom}!", "-background", "#202020", "-gravity", "center",
                                "-extent", f"{cell[0]}x{cell[1]}", str(path)], check=True)
                cells.append(path)
            row = work / f"{tab.name}-row.png"
            subprocess.run(["magick", *cells, "+append", "-font", LABEL_FONT, "-fill", "white", "-undercolor", "#00000099", "-pointsize", "16", "-gravity", "southwest", "-annotate", "+4+2", f"{tab.name} (art {tab.art})", str(row)], check=True)
            rows.append(row)
        per = max(1, args.columns)
        grid = []
        for k in range(0, len(rows), per):
            line = work / f"line-{k}.png"
            subprocess.run(["magick", *rows[k:k + per], "+append", "-background", "#202020", "-extent", f"{cell[0] * 4 * per}x{cell[1]}", str(line)], check=True)
            grid.append(line)
        sheet = work / "sheet.png"
        subprocess.run(["magick", *grid, "-background", "#202020", "-append", str(sheet)], check=True)
        head = work / "head.png"
        caption = f"{args.title}: ORIGINAL idle (nearest cells, uncut) | NEW part idle | hover | pressed (pack frames, analytic round alpha, run-time shadow)"
        subprocess.run(["magick", "-size", f"{cell[0] * 4 * per}x40", "xc:#202020", "-font", LABEL_FONT, "-fill", "white", "-pointsize", "22", "-gravity", "west", "-annotate", "+10+0", caption, str(head)], check=True)
        subprocess.run(["magick", str(head), str(sheet), "-append", str(args.out)], check=True)
    finally:
        shutil.rmtree(work, ignore_errors=True)
    print(f"SCHOOL KIT sheet PASS: {args.out}")
    return 0


def self_test() -> int:
    """The unit tests, run as the package module (they import this tool relatively, so not in this process when it runs as a script)."""
    code = subprocess.run([sys.executable, "-B", "-m", "unittest", "tools.steamwoven_ui.test_school_kit"], cwd=HERE.parents[1]).returncode
    print("SCHOOL KIT self-test " + ("PASS" if code == 0 else "FAIL"))
    return 0 if code == 0 else 1


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("build", "check", "sheet", "codex-inbox", "codex-place"):
        s = sub.add_parser(name)
        s.add_argument("--only")
        s.add_argument("--local-root", type=Path, default=None)
        if name == "codex-inbox":
            s.add_argument("--stage", choices=("idle", "states"), required=True)
        if name in ("codex-inbox", "codex-place"):
            s.add_argument("--frames", type=Path)
        if name == "build":
            s.add_argument("--out", type=Path)
            s.add_argument("--dry-run", action="store_true")
        if name == "sheet":
            s.add_argument("--out", type=Path, required=True)
            s.add_argument("--zoom", type=int, default=3)
            s.add_argument("--columns", type=int, default=2)
            s.add_argument("--frames", type=Path)
            s.add_argument("--title", default="ARC-UI-3204, college tabs and skill-school buttons")
    sub.add_parser("self-test")
    args = parser.parse_args(argv)
    try:
        if args.command == "self-test":
            return self_test()
        args.local_root = args.local_root or prompt_library.default_local_root()
        return {"build": cmd_build, "check": cmd_check, "sheet": cmd_sheet, "codex-inbox": cmd_codex_inbox, "codex-place": cmd_codex_place}[args.command](args)
    except (SchoolError, button_kit.ButtonError, pngio.PngError, OSError, ValueError, subprocess.CalledProcessError) as error:
        print(f"SCHOOL KIT FAIL: {error}")
        return 2


if __name__ == "__main__":
    sys.exit(main())
