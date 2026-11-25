#!/usr/bin/env python3
"""ARC-UI-2483 (batch B-GAUGES): the Steamwoven masters of gauges and readouts, 56 parts (TRD SWI-KIT-6).

    python3 -B tools/steamwoven_ui/gauge_kit.py plan
    python3 -B tools/steamwoven_ui/gauge_kit.py refs   [--only ID,...] [--frames DIR] [--local-root DIR]
    python3 -B tools/steamwoven_ui/gauge_kit.py cut    [--only ID,...] [--dry-run] [--local-root DIR]      (the Codex results -> fitted masters, composites included)
    python3 -B tools/steamwoven_ui/gauge_kit.py rule   [--only PART,...] [--dry-run] [--local-root DIR]      (the parts made by rule from the original frames, no Codex)
    python3 -B tools/steamwoven_ui/gauge_kit.py sheet  --out FILE [--local-root DIR]                           (before / after composite with English labels, needs ImageMagick)
    python3 -B tools/steamwoven_ui/gauge_kit.py self-test

Two ways make a master (TRD SWI-GEN-4; the owner's rules: the muted palette of the original, no added ornament, no synthetic stains):

* **Codex collages.** The painted parts (the message bars, the experience pips, the clock, the ammunition counters, the pocket watch, the scroll thumb and chain, the pointers) are given to Codex
  in ONE collage per family (`GROUPS`): every part of the family on a flat magenta ground, each cell the original's frame enlarged to the cell size (`k` collage px per master px) as the layout truth.
  `refs` writes the collage and its layout (`refs/<id>-original.png`, `refs/<id>-layout.json`, local, never in git); the prompt library (`B-GAUGES`) carries the text.
  `cut` finds each cell in the generator's result (the layout scaled to the result's size, then the content's bounding box), keys the ground (the fit tool's keying), makes the exact master
  aspect and lets `fit.py` write the master and its record (`fit:<part id>`). A piece that is a layer of another part (a moon disc over the night sky, a picture over a counter panel) is
  composed by rule from two generated cells at the original's own place, then fitted the same way.
* **Rule.** The flat or gradient parts (the vial liquids and shading ramps, the thin follower bars, the experience line, the item damage ramp) are the original's own colours: the rows (or columns)
  of one band are one colour profile, so the dither of the 28-pixel original is replaced by its median and the profile is resampled smoothly (the fit tool's premultiplied Catmull-Rom) to
  the master size. Record `rule:gauge_kit:<part id>`. Nothing is invented: no highlight, no bubble, no texture.

Frame sequences (the vial bubbles, the alignment needle's 21 angles, the 30 watch hands, the 9 damage colours) are the original's animation of a VALUE; the kit master is the idle frame 0 (the
full-damage frame 8 for `gauge.item_dam`) and the runtime draws the value (SWI-KIT-6); the other frames are not kit parts. Standard library only (`pngio.py`, `fit.py`); ImageMagick for the collages.
Exit 0 = done, 1 = a check failed, 2 = an input could not be used.
"""
from __future__ import annotations

import argparse
import datetime
import hashlib
import json
import math
import shutil
import statistics
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
BATCH = "B-GAUGES"
KEY = (255, 0, 255)           # the flat ground of every collage (no part of the batch is magenta)
KEY_HEX = "#FF00FF"
UNIT = 3.6                    # master px per original unit (TRD SWI-KIT-1)
GAP = 72                      # px of ground between cells and round the collage (a multiple of 6)
GRID = 6
MAX_DRIFT = 0.08              # the two axis scales of the art in the result against each other (the generator may squash a collage a little; a cell is found by its content)
ASPECT_FIX = 0.10             # an opaque cut cell is stretched to its master aspect when it is within 10 % of it (a layout drift), else the cut is refused
PAD_FIX = 0.15                # a keyed cut-out is centred on a transparent canvas of the master's aspect when within 15 % of it (nothing is stretched)
SEARCH = 0.25                 # a cell is searched for in its expected rectangle grown by this share of the gap
MAGENTA_MIN = 100             # a pixel is magenta ground or its fringe when red and blue are both above this ...
MAGENTA_LEAN = 90             # ... and the smaller of them is this far above green (a violet of the art, such as 120 60 140, stays art)
KEY_FUZZ = "14%"
BAND_FUZZ = 40                # the colour distance from the key (of 441) that is still ground when the bounding box of a cell is taken


class GaugeError(Exception):
    pass


# --- the plan ----------------------------------------------------------------------------------------------------------------------------

@dataclass(frozen=True)
class Cell:
    cid: str                              # the cell id inside its collage
    folder: str                           # the census frames folder of the original art, e.g. "623-SkilGauge"
    part: str | None = None               # the kit part this cell becomes (None: a layer used by a composite)
    frame: int = 0
    rect: tuple[int, int, int, int] | None = None   # x, y, w, h in original units (None: the whole frame)
    out: tuple[int, int] | None = None    # master px of the cell (None: the manifest's master size of `part`)
    mode: str = "rect"                    # rect: an opaque rectangle; key: a cut-out on the ground; placed: a cut-out whose place inside its cell matters
    k: float | None = None                # collage px per master px of this cell (default: the group's)
    mask: str | None = None               # diff:<folder>: what equals that frame (the empty panel, the sky without a moon) is ground


@dataclass(frozen=True)
class Group:
    gid: str                              # the prompt id
    title: str
    rows: tuple[tuple[Cell, ...], ...]
    k: float = 2.0                        # collage px per master px
    bleed: bool = False                   # a single part that fills its picture edge to edge (no ground, no margin)
    notes: str = ""


def bars(*names: str) -> tuple[tuple[Cell, ...], ...]:
    return tuple((Cell(n, folder, part=f"gauge.{n}", mode="rect"),) for n, folder in ((n, FOLDER[n]) for n in names))


# census frames folder of each art (id-name), for the parts this module reads
FOLDER = {
    "meshealthbar_full": "463-MesHealthBar_Full", "meshealthbar_empty": "464-MesHealthBar_Empty",
    "mesfatiguebar_full": "465-MesFatigueBar_Full", "mesfatiguebar_empty": "466-MesFatigueBar_Empty",
    "mespoisonedbar_full": "665-MesPoisonedBar_Full", "mespoisonedbar_empty": "666-MesPoisonedBar_Empty",
    "skilgauge": "623-SkilGauge", "clk_timestrip": "207-Clk_timestrip", "clk_moon1": "208-Clk_Moon1",
    "amocnt_b": "165-AmoCnt_B", "amocnt_s": "5-amocnt_s", "amo_rol": "167-Amo_Rol", "amo_rolx": "168-Amo_RolX",
    "loadindicator": "601-loadindicator", "scrllslidet": "238-ScrllSlideT", "scrllslidem": "239-ScrllSlideM", "scrllslideb": "240-ScrllSlideB",
    "scrllslidem1": "787-ScrllSlideM1", "scrllsld_off": "316-ScrllSLD_OFF", "align": "254-align", "mt_apt": "255-MT_Apt", "timepnts": "216-TimePnts",
}
for _n in range(1, 11):
    FOLDER[f"xp_pip{_n}"] = f"{772 + _n}-XP_Pip{_n}"
for _n in range(2, 9):
    FOLDER[f"clk_moon{_n}"] = f"{207 + _n}-Clk_Moon{_n}"
AMMO_PICTURES = {"arrows": "x-amocnt_arrows", "bullets": "x-amocnt_bullets", "charges": "x-amocnt_charges", "fuel": "x-amocnt_fuel"}
MOON_FOLDERS = tuple(FOLDER[f"clk_moon{n}"] for n in range(2, 9))
CHAIN_SEGMENTS = 6

GROUPS: tuple[Group, ...] = (
    Group("gauge-bars", "Message health, fatigue and poison bars (full, empty) and the skill gauge (arts 463-466, 665, 666, 623)",
          bars("meshealthbar_full", "meshealthbar_empty", "mesfatiguebar_full", "mesfatiguebar_empty", "mespoisonedbar_full", "mespoisonedbar_empty", "skilgauge"), k=2.0,
          notes="seven bars in one column on the ground; every bar keeps the original's frame lines, rows and end caps; the full and empty bars of one colour match"),
    Group("gauge-xp-pips", "Experience strip pieces, ten pips (arts 773-782)",
          tuple(tuple(Cell(f"xp_pip{n}", FOLDER[f"xp_pip{n}"], part=f"gauge.xp_pip{n}", mode="rect") for n in (a, a + 1)) for a in (1, 3, 5, 7, 9)), k=3.0,
          notes="ten lamp pieces, two columns of five; each keeps its own width (36 to 40 units) and its brass ends"),
    Group("gauge-clock", "Time strip of the clock (art 207) and its night piece without a moon (art 208)",
          ((Cell("clk_timestrip", FOLDER["clk_timestrip"], part="gauge.clk_timestrip", mode="rect", k=1.5),), (Cell("clk_moon1", FOLDER["clk_moon1"], part="gauge.clk_moon1", mode="rect", k=3.0),)), k=2.0,
          notes="two bands on the ground (the generator re-composes a lone band of this aspect and pads a lone strip, so they are asked together); the night piece is the base of the eight moon pieces"),
    Group("gauge-clock-moons", "Moon phases of the clock, lit part only (arts 209-215)",
          tuple(tuple(Cell(f"moon{n}", MOON_FOLDERS[n - 1], mode="placed", mask="diff:" + FOLDER["clk_moon1"], rect=(0, 0, 0, 0)) for n in row) for row in ((1, 2, 3, 4), (5, 6, 7))), k=6.0,
          notes="seven phases, the lit part of the disc only on the ground; the unlit part is black sky and is not drawn"),
    Group("gauge-ammo", "Ammunition counters: two panels, two rollers, four pictures (arts 165, 5, 167, 168, and the arrow, bullet, charge and fuel pictures)",
          ((Cell("amocnt_b", FOLDER["amocnt_b"], part="gauge.amocnt_b", mode="rect"), Cell("amocnt_s", FOLDER["amocnt_s"], part="gauge.amocnt_s", mode="rect")),
           (Cell("amo_rol", FOLDER["amo_rol"], part="gauge.amo_rol", mode="rect"), Cell("amo_rolx", FOLDER["amo_rolx"], part="gauge.amo_rolx", mode="rect")),
           tuple(Cell(f"pic_{n}", folder, mode="placed", mask="diff:" + FOLDER["amocnt_b"], rect=(0, 0, 0, 0)) for n, folder in AMMO_PICTURES.items())), k=2.0,
          notes="the empty panel and the small panel with its inset, the metal roller and the wood roller, and the four pictures on the ground (cut out of the counters)"),
    Group("gauge-watch", "Pocket watch load indicator (art 601, frame 0)", ((Cell("loadindicator", FOLDER["loadindicator"], part="gauge.loadindicator", mode="key"),),), k=6.0,
          notes="one watch on the ground, no hand"),
    Group("gauge-scroll", "Scroll thumb pieces and the chain strip (arts 238, 239, 240, 787, 316)",
          (tuple(Cell(f"chain{n}", FOLDER["scrllsld_off"], part=None, mode="placed", rect=(0, n * 100, 13, 100), out=(48, 360)) for n in range(CHAIN_SEGMENTS)),
           (Cell("scrllslidet", FOLDER["scrllslidet"], part="gauge.scrllslidet", mode="key"), Cell("scrllslidem", FOLDER["scrllslidem"], part="gauge.scrllslidem", mode="rect"),
            Cell("scrllslideb", FOLDER["scrllslideb"], part="gauge.scrllslideb", mode="key"), Cell("scrllslidem1", FOLDER["scrllslidem1"], part="gauge.scrllslidem1", mode="rect"))), k=3.0,
          notes="six chain segments side by side (top to bottom of the strip, joined afterwards) and the four small thumb pieces"),
    Group("gauge-pointers", "Alignment needle, aptitude arrow and time marker (arts 254, 255, 216)",
          ((Cell("align", FOLDER["align"], part="gauge.align", mode="key"), Cell("mt_apt", FOLDER["mt_apt"], part="gauge.mt_apt", mode="key"), Cell("timepnts", FOLDER["timepnts"], part="gauge.timepnts", mode="key")),), k=6.0,
          notes="three small pointers on the ground"),
)

# the parts made by rule (no Codex): part id -> (census folder, frame, axis of the uniform band)
RULE_PARTS = {
    "gauge.yelovial": ("17-yelovial", 0, "y"), "gauge.redvial": ("18-redvial", 0, "y"), "gauge.bluvial": ("19-bluvial", 0, "y"), "gauge.clrvial": ("20-clrvial", 0, "y"),
    "gauge.yelwvial": ("x-yelwvial", 0, "y"), "gauge.bar": ("4-bar", 0, "y"),
    "gauge.followerframe_ft": ("504-FollowerFrame_FT", 0, "x"), "gauge.followerframe_hp": ("505-FollowerFrame_HP", 0, "x"), "gauge.followerframe_po": ("616-FollowerFrame_PO", 0, "x"),
    "gauge.followerframe_poison": ("x-followerframe_poison", 0, "x"), "gauge.xp_gaugemag": ("772-XP_GaugeMag", 0, "x"), "gauge.item_dam": ("256-Item_Dam", 8, "flat"),
    # small detailed parts the owner wanted built from the original itself (the generated ones shifted the tone by dE 11 to 35): the frame resampled smoothly, nothing redrawn
    "gauge.scrllslidem1": ("787-ScrllSlideM1", 0, "smooth"), "gauge.timepnts": ("216-TimePnts", 0, "smooth"), "gauge.mt_apt": ("255-MT_Apt", 0, "smooth"),
    "gauge.amo_rol": ("167-Amo_Rol", 0, "smooth"),
}
# parts that are the same picture as another part (the original's two chain strips differ in 5 of 7800 pixels; ON is unused by any window)
COPIES = {"gauge.scrllsld_on": "gauge.scrllsld_off"}
# composites: part -> (base part, layer cell id, the layer's place: centre in original units of the base, and the layer's cell size in units)
MOON_PARTS = {f"gauge.clk_moon{n}": f"moon{n - 1}" for n in range(2, 9)}
AMMO_PARTS = {f"gauge.amocnt_{n}": f"pic_{n}" for n in AMMO_PICTURES}


def load_manifest() -> dict:
    return json.loads((HERE / "kit.json").read_text(encoding="utf-8"))


def parts_of_batch(manifest: dict | None = None) -> dict[str, dict]:
    manifest = manifest or load_manifest()
    return {p["id"]: p for p in manifest["parts"] if p.get("batch") == BATCH}


def coverage(manifest: dict | None = None) -> dict[str, str]:
    """Part id -> how it is made. Every part of the batch must appear once (checked by the self-test and `plan`)."""
    how: dict[str, str] = {}
    for g in GROUPS:
        for row in g.rows:
            for c in row:
                if c.part:
                    how[c.part] = f"codex:{g.gid}"
    for part in MOON_PARTS:
        how[part] = "composite:gauge-clock+gauge-clock-moons"
    for part in AMMO_PARTS:
        how[part] = "composite:gauge-ammo"
    how["gauge.scrllsld_off"] = "codex:gauge-scroll (six chain segments joined)"
    for part, source in COPIES.items():
        how[part] = f"copy of {source}"
    for part in RULE_PARTS:
        how[part] = "rule"
    return how


def provenance_of(part: str) -> str:
    """The `provenance` id of a part of the batch in the manifest: the fit record of a part the fit tool wrote, the rule record of a part made by rule."""
    return f"rule:gauge_kit:{part}" if part in RULE_PARTS else f"fit:{part}"


def note_of(part: str, frames: int = 1) -> str:
    """The manifest note of a part of the batch (what it is made of, and what the original's frame sequence becomes)."""
    how = coverage()[part]
    if part in RULE_PARTS:
        folder, frame, axis = RULE_PARTS[part]
        text = ("the original's flat colour of the last damage frame; the nine frames are a linear ramp the runtime draws from the value" if axis == "flat"
                else "by rule from the original's frame, resampled smoothly, nothing redrawn or added; no Codex" if axis == "smooth"
                else "by rule from the original's frame: dither replaced by the band median, resampled smoothly; no Codex, nothing added")
        text += " (gauge_kit.py rule)"
    elif how.startswith("composite:"):
        text = ("the base master with the generated layer laid over it at the original's place (gauge_kit.py cut)")
    elif part in COPIES:
        text = f"the same picture as {COPIES[part]} (the originals differ in 5 of 7800 pixels; ON is unused)"
    else:
        text = f"Codex collage {how.split(':', 1)[1].split(' ')[0]}, cut and fitted (gauge_kit.py cut)"
    if frames > 1 and part != "gauge.item_dam":
        text += f"; frame 0 of the original's {frames}, the runtime draws the value (SWI-KIT-6)"
    return text


# --- helpers -----------------------------------------------------------------------------------------------------------------------------

def sha256_file(path: Path) -> str:
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def default_frames(local: Path) -> Path:
    return local / "census" / "frames"


def read_frame(frames: Path, folder: str, frame: int = 0) -> pngio.Image:
    path = frames / folder / f"f{frame}.png"
    if not path.is_file():
        raise GaugeError(f"original frame {path} not found")
    return pngio.read(path)


def master_of(part: str, manifest: dict | None = None) -> tuple[int, int]:
    info = parts_of_batch(manifest).get(part)
    if info is None or not info.get("size"):
        raise GaugeError(f"{part} has no master size in the manifest")
    return info["size"][0], info["size"][1]


def round6(v: float) -> int:
    return max(GRID, int(math.ceil(v / GRID)) * GRID)


def run(cmd: list[str]) -> str:
    done = subprocess.run(cmd, capture_output=True, text=True)
    if done.returncode != 0:
        raise GaugeError(f"{cmd[0]} failed: {done.stderr.strip()[:300]}")
    return done.stdout


def need_magick() -> None:
    if shutil.which("magick") is None:
        raise GaugeError("ImageMagick `magick` is not installed")


# --- regions of the originals (moon discs, pictures) -----------------------------------------------------------------------------

DIFF_MIN = 60                 # summed channel difference above which a pixel of a counter or a moon frame is what the picture adds to its base


def foreground(image: pngio.Image, base: pngio.Image, grow: int = 1, minimum: int = DIFF_MIN) -> set[tuple[int, int]]:
    """The pixels of `image` that differ from `base` by more than DIFF_MIN (summed over the channels), grown by `grow` pixels (the ground's texture noise is smaller than that)."""
    hit = {(x, y) for y in range(image.h) for x in range(image.w)
           if sum(abs(p - q) for p, q in zip(image.pixel(x, y)[:3], base.pixel(x, y)[:3])) > minimum}
    out = set(hit)
    for _ in range(grow):
        out |= {(x + dx, y + dy) for x, y in out for dx in (-1, 0, 1) for dy in (-1, 0, 1) if 0 <= x + dx < image.w and 0 <= y + dy < image.h}
    return out


def largest_part(pixels: set[tuple[int, int]]) -> set[tuple[int, int]]:
    """The largest 8-connected group of pixels (the moon disc; the stray specks of the sky's grain that differ between frames are dropped)."""
    left, best = set(pixels), set()
    while left:
        stack = [left.pop()]
        group = set(stack)
        while stack:
            x, y = stack.pop()
            for dx in (-1, 0, 1):
                for dy in (-1, 0, 1):
                    n = (x + dx, y + dy)
                    if n in left:
                        left.discard(n)
                        group.add(n)
                        stack.append(n)
        if len(group) > len(best):
            best = group
    return best


def moon_disc(frames: Path) -> set[tuple[int, int]]:
    """The whole moon disc (units of the night art): what the full moon (art 209) adds to the sky. A phase frame shows the part of this disc that is lit and, dimly, the rest of it, so every phase
    is cut with this one silhouette (the sky's grain differs a little between frames and would otherwise leak into a dim side)."""
    return largest_part(foreground(read_frame(frames, FOLDER["clk_moon2"]), read_frame(frames, FOLDER["clk_moon1"]), 1))


def moon_region(frames: Path) -> tuple[int, int, int, int]:
    """The square (x, y, w, h in units) that holds the moon disc in every Clk_Moon frame: the lit pixels of the full moon (art 209), grown by two units."""
    base = read_frame(frames, FOLDER["clk_moon1"])
    xs, ys = [], []
    for n in range(2, 9):
        image = read_frame(frames, FOLDER[f"clk_moon{n}"])
        for x, y in largest_part(foreground(image, base, 1)):
            xs.append(x)
            ys.append(y)
    if not xs:
        raise GaugeError("no moon found: the moon frames equal the night sky")
    cx, cy = (min(xs) + max(xs) + 1) / 2, (min(ys) + max(ys) + 1) / 2
    r = max(max(xs) - min(xs) + 1, max(ys) - min(ys) + 1) / 2 + 1
    side = int(math.ceil(2 * r))
    x0 = int(round(cx - side / 2))
    y0 = max(0, min(base.h - side, int(round(cy - side / 2))))
    return x0, y0, side, min(side, base.h)


def picture_region(frames: Path, name: str) -> tuple[int, int, int, int]:
    """The bounding box (units) of what a counter's picture adds to the empty panel (art 165), grown by one unit."""
    blank = read_frame(frames, FOLDER["amocnt_b"])
    full = read_frame(frames, AMMO_PICTURES[name])
    xs, ys = [], []
    for x, y in foreground(full, blank, 0):
        xs.append(x)
        ys.append(y)
    if not xs:
        raise GaugeError(f"counter {name} equals the empty panel")
    x0, y0 = max(0, min(xs) - 1), max(0, min(ys) - 1)
    return x0, y0, min(full.w, max(xs) + 2) - x0, min(full.h, max(ys) + 2) - y0


def resolve_cell(cell: Cell, frames: Path, manifest: dict | None = None) -> dict:
    """The cell with its region, collage size and mask folder worked out."""
    rect = cell.rect
    if cell.mode == "placed" and rect == (0, 0, 0, 0):
        if cell.cid.startswith("moon"):
            rect = moon_region(frames)
        else:
            rect = picture_region(frames, cell.cid[len("pic_"):])
    if cell.out is not None:
        out = cell.out
    elif cell.part is not None:
        out = master_of(cell.part, manifest)
    else:
        out = (round6(rect[2] * UNIT), round6(rect[3] * UNIT))
    if cell.mode == "placed" and cell.out is None:
        out = (round6(rect[2] * UNIT), round6(rect[3] * UNIT))
    return {"cell": cell, "rect": rect, "out": out}


def layout(group: Group, frames: Path, manifest: dict | None = None) -> dict:
    """The collage geometry: canvas size, and for each cell its place and size in collage px (rows centred, gaps of ground)."""
    cells = [[resolve_cell(c, frames, manifest) for c in row] for row in group.rows]
    sized = [[(round(r["out"][0] * (r["cell"].k or group.k)), round(r["out"][1] * (r["cell"].k or group.k))) for r in row] for row in cells]
    margin = 0 if group.bleed else GAP
    gap = 0 if group.bleed else GAP
    row_w = [sum(w for w, _ in row) + gap * (len(row) - 1) for row in sized]
    row_h = [max(h for _, h in row) for row in sized]
    width = round6(max(row_w) + 2 * margin)
    height = round6(sum(row_h) + gap * (len(row_h) - 1) + 2 * margin)
    out: list[dict] = []
    y = (height - (sum(row_h) + gap * (len(row_h) - 1))) // 2
    for ri, row in enumerate(cells):
        x = (width - row_w[ri]) // 2
        for ci, r in enumerate(row):
            w, h = sized[ri][ci]
            out.append({"id": r["cell"].cid, "part": r["cell"].part, "mode": r["cell"].mode, "folder": r["cell"].folder, "frame": r["cell"].frame, "mask": r["cell"].mask,
                        "rect": list(r["rect"]) if r["rect"] else None, "master": list(r["out"]), "x": x, "y": y + (row_h[ri] - h) // 2, "w": w, "h": h})
            x += w + gap
        y += row_h[ri] + gap
    return {"group": group.gid, "k": group.k, "bleed": group.bleed, "canvas": [width, height], "cells": out}


# --- collages (refs) -----------------------------------------------------------------------------------------------------------------

def masked_frame(frames: Path, cell: dict, tmp: Path) -> Path:
    """The original cell as a PNG at its own pixel size: the rectangle of the frame, with the ground cells made transparent."""
    image = read_frame(frames, cell["folder"], cell["frame"])
    rect = tuple(cell["rect"]) if cell["rect"] else (0, 0, image.w, image.h)
    crop = image.crop(*rect)
    if cell["mask"] and cell["mask"].startswith("diff:"):
        base = read_frame(frames, cell["mask"][5:])
        keep = moon_disc(frames) if cell["id"].startswith("moon") else foreground(image, base)
        for y in range(crop.h):
            for x in range(crop.w):
                if (x + rect[0], y + rect[1]) not in keep:
                    crop.data[(y * crop.w + x) * 4 + 3] = 0
    path = tmp / f"{cell['id']}.png"
    pngio.write(path, crop)
    return path


def build_collage(group: Group, geometry: dict, frames: Path, out: Path) -> None:
    need_magick()
    width, height = geometry["canvas"]
    with tempfile.TemporaryDirectory() as t:
        tmp = Path(t)
        cmd = ["magick", "-size", f"{width}x{height}", f"xc:{KEY_HEX}"]
        for cell in geometry["cells"]:
            src = masked_frame(frames, cell, tmp)
            cmd += ["(", str(src), "-filter", "Lanczos", "-resize", f"{cell['w']}x{cell['h']}!", ")", "-geometry", f"+{cell['x']}+{cell['y']}", "-composite"]
        run([*cmd, str(out)])


def cmd_refs(args: argparse.Namespace) -> int:
    local = args.local_root
    frames = args.frames or default_frames(local)
    refs = local / "refs"
    refs.mkdir(parents=True, exist_ok=True)
    manifest = load_manifest()
    for group in selected_groups(args):
        geometry = layout(group, frames, manifest)
        target = refs / f"{group.gid}-original.png"
        build_collage(group, geometry, frames, target)
        (refs / f"{group.gid}-layout.json").write_text(json.dumps(geometry, indent=1) + "\n", encoding="utf-8")
        print(f"{target.name}  {geometry['canvas'][0]}x{geometry['canvas'][1]}  {len(geometry['cells'])} cell(s)  {sha256_file(target)[:12]}")
    print("GAUGE KIT refs PASS: build the inbox with prompt_library.py inbox B-GAUGES")
    return 0


def selected_groups(args: argparse.Namespace) -> list[Group]:
    only = {x for x in (args.only or "").split(",") if x}
    chosen = [g for g in GROUPS if not only or g.gid in only]
    if only and len(chosen) != len(only):
        raise GaugeError(f"unknown group in --only: {sorted(only - {g.gid for g in GROUPS})}")
    return chosen


# --- cut: the generator's result -> cells -> fitted masters --------------------------------------------------------------------------

def key_ground_bbox(path: Path, rect: tuple[int, int, int, int]) -> tuple[int, int, int, int] | None:
    """The bounding box (x, y, w, h in the image's own pixels) of what is not ground inside the rectangle."""
    x, y, w, h = rect
    text = run(["magick", str(path), "-crop", f"{w}x{h}+{x}+{y}", "+repage", "-fuzz", KEY_FUZZ, "-fill", "none", "-opaque", KEY_HEX, "-alpha", "extract", "-threshold", "50%",
                "-trim", "-format", "%w %h %X %Y", "info:"]).split()
    if len(text) != 4 or int(text[0]) <= 1 or int(text[1]) <= 1:
        return None
    bw, bh, ox, oy = (int(v) for v in text)
    return x + ox, y + oy, bw, bh


def content_bbox(path: Path) -> tuple[int, int, int, int]:
    text = run(["magick", str(path), "-fuzz", KEY_FUZZ, "-fill", "none", "-opaque", KEY_HEX, "-alpha", "extract", "-threshold", "50%", "-trim", "-format", "%w %h %X %Y", "info:"]).split()
    w, h, x, y = (int(v) for v in text)
    return x, y, w, h


def locate(path: Path, geometry: dict, size: tuple[int, int], guide: Path) -> dict[str, dict]:
    """The place of every cell in the generator's result. The generator often returns another canvas than asked (another aspect, the art centred and scaled), so the layout is mapped
    onto the result by the bounding box of everything that is not ground (an affine map per axis), each cell is searched for in its mapped rectangle grown a little, and its own content
    box is taken there."""
    cells = geometry["cells"]
    if geometry["bleed"]:
        return {c["id"]: {"expect": [0, 0, size[0], size[1]], "box": [0, 0, size[0], size[1]], "scale": [size[0] / geometry["canvas"][0], size[1] / geometry["canvas"][1]]} for c in cells}
    lx0, ly0, lw, lh = content_bbox(guide)        # what the collage we sent shows (cut-outs do not fill their cells)
    lx1, ly1 = lx0 + lw, ly0 + lh
    cx, cy, cw, ch = content_bbox(path)
    sx, sy = cw / (lx1 - lx0), ch / (ly1 - ly0)
    if abs(sx / sy - 1) > MAX_DRIFT:
        raise GaugeError(f"the art in the result is {cw}x{ch}, not the layout's {lx1 - lx0}x{ly1 - ly0} (axis scales {sx:.3f} and {sy:.3f}) within {MAX_DRIFT:.0%}: the generator re-composed the collage")
    found: dict[str, dict] = {}
    grow = int(GAP * SEARCH * sx)
    for cell in cells:
        ex, ey = cx + (cell["x"] - lx0) * sx, cy + (cell["y"] - ly0) * sy
        ew, eh = cell["w"] * sx, cell["h"] * sy
        x0, y0 = max(0, int(ex - grow)), max(0, int(ey - grow))
        search = (x0, y0, min(size[0], int(ex + ew + grow)) - x0, min(size[1], int(ey + eh + grow)) - y0)
        box = key_ground_bbox(path, search)
        if box is None:
            raise GaugeError(f"cell {cell['id']}: nothing but ground where it should be")
        found[cell["id"]] = {"expect": [round(ex), round(ey), round(ew), round(eh)], "box": list(box), "scale": [sx, sy]}
    return found


def cell_image(path: Path, found: dict, cell: dict) -> pngio.Image:
    """The cell as RGBA at the generator's resolution. rect: the content box, opaque; key: the box keyed against the ground; placed: the expected rectangle keyed, not trimmed."""
    box = found["box"]
    if cell["mode"] == "placed":
        ex, ey, ew, eh = found["expect"]
        pad = 6
        bx, by, bw, bh = box
        x0, y0 = max(0, min(ex, bx) - pad), max(0, min(ey, by) - pad)
        x1, y1 = max(ex + ew, bx + bw) + pad, max(ey + eh, by + bh) + pad       # the expected cell and the content found near it (a cell the generator moved is not clipped)
        region = (x0, y0, x1 - x0, y1 - y0)
        found["region"] = list(region)
    else:
        pad = 6 if cell["mode"] == "key" else 0
        region = (max(0, box[0] - pad), max(0, box[1] - pad), box[2] + 2 * pad, box[3] + 2 * pad)
    with tempfile.TemporaryDirectory() as t:
        crop = Path(t) / "crop.png"
        run(["magick", str(path), "-crop", f"{region[2]}x{region[3]}+{region[0]}+{region[1]}", "+repage", "-alpha", "off", "-depth", "8", f"PNG24:{crop}"])
        image = pngio.read(crop)
    if cell["mode"] == "rect":
        return peel_ground(image)
    colour, spread = (KEY, 0.0)
    for i in range(0, len(image.data), 4):   # a pixel that leans magenta (red and blue well above green) is ground: no part of the batch has such a colour
        r, g, b = image.data[i], image.data[i + 1], image.data[i + 2]
        if min(r, b) > MAGENTA_MIN and min(r, b) - g > MAGENTA_LEAN:
            image.data[i:i + 3] = bytes(KEY)
    ring = fit.border_ring(image)
    spread = fit.percentile([math.dist(p, KEY) for p in ring], 0.99)
    fit.key_background(image, colour, min(spread, fit.KEY_SPREAD_MAX))
    if cell["mode"] == "placed":
        if cell["id"].startswith("moon"):
            keep = largest_part({(i % image.w, i // image.w) for i in range(image.w * image.h) if image.data[i * 4 + 3] > 8})
            for i in range(image.w * image.h):
                if (i % image.w, i // image.w) not in keep:
                    image.data[i * 4 + 3] = 0
        return pngio.Image(image.w, image.h, image.data)  # the region keeps its extent (pad included): the caller maps it back to the expected rectangle
    return trim_alpha(image)


PEEL_MAX = 8        # most lines taken off one side of an opaque cell (generator pixels)
PEEL_SHARE = 0.02   # a line is ground bleed when this share of its pixels lean magenta ...
PEEL_LEAN = 40      # ... (red and blue above 30, the smaller of them this far above green: a dark blend of the ground into the art as well as the ground itself)
PEEL_MIN = 30


def peel_ground(image: pngio.Image) -> pngio.Image:
    """An opaque cell without its ground bleed: the outer lines (rows, columns) in which the magenta ground shows or blends into the art are cut off, and one more line, for the blend the
    resampling would take from it. The master of an opaque cell has no transparency to hide ground in, so no magenta may reach it (ARC-UI-2483)."""
    def leaning(x: int, y: int) -> bool:
        r, g, b = image.data[(y * image.w + x) * 4:(y * image.w + x) * 4 + 3]
        return min(r, b) > PEEL_MIN and min(r, b) - g > PEEL_LEAN

    def share(line: int, row: bool, left: int, top: int, right: int, bottom: int) -> float:
        quarter_w, quarter_h = (right - left) // 4, (bottom - top) // 4      # the middle half of the line: the side's own corners are another side's bleed
        cells = [(x, line) for x in range(left + quarter_w, right - quarter_w)] if row else [(line, y) for y in range(top + quarter_h, bottom - quarter_h)]
        return sum(leaning(x, y) for x, y in cells) / max(1, len(cells))

    left, top, right, bottom = 0, 0, image.w, image.h
    for side in ("top", "bottom", "left", "right"):
        taken = 0
        while right - left > 4 and bottom - top > 4:
            line = {"top": top, "bottom": bottom - 1, "left": left, "right": right - 1}[side]
            if share(line, side in ("top", "bottom"), left, top, right, bottom) < PEEL_SHARE:
                break
            if taken == PEEL_MAX:
                raise GaugeError(f"the ground reaches more than {PEEL_MAX} px into the {side} side of an opaque cell: the cell was not found where the layout puts it")
            top += side == "top"
            bottom -= side == "bottom"
            left += side == "left"
            right -= side == "right"
            taken += 1
        if taken:       # the line after the last bleed line: the resampling mixes it in
            top += side == "top"
            bottom -= side == "bottom"
            left += side == "left"
            right -= side == "right"
    return image.crop(left, top, right - left, bottom - top) if (left, top, right, bottom) != (0, 0, image.w, image.h) else image


def trim_alpha(image: pngio.Image) -> pngio.Image:
    xs = [i % image.w for i in range(image.w * image.h) if image.data[i * 4 + 3] > 8]
    ys = [i // image.w for i in range(image.w * image.h) if image.data[i * 4 + 3] > 8]
    if not xs:
        raise GaugeError("the keyed cell is empty")
    return image.crop(min(xs), min(ys), max(xs) - min(xs) + 1, max(ys) - min(ys) + 1)


def to_aspect(image: pngio.Image, want: tuple[int, int]) -> pngio.Image:
    """The image on a canvas of the master's aspect: the missing side is transparent (a keyed part) or stretched (an opaque one, within `ASPECT_FIX`)."""
    have = image.w / image.h
    target = want[0] / want[1]
    if abs(have / target - 1) <= fit.ASPECT_TOLERANCE:
        return image
    opaque = min(image.data[3::4]) == 255
    if abs(have / target - 1) > (ASPECT_FIX if opaque else PAD_FIX):
        raise GaugeError(f"a cut cell is {image.w}x{image.h} (aspect {have:.3f}), the master {want[0]}x{want[1]} ({target:.3f}): beyond the {(ASPECT_FIX if opaque else PAD_FIX):.0%} that a layout drift explains")
    if opaque:
        new_w, new_h = (image.w, round(image.w / target)) if have > target else (round(image.h * target), image.h)
        return fit.resample_image(image, (0, 0, image.w, image.h), new_w, new_h)
    if have > target:
        canvas = pngio.Image.blank(image.w, round(image.w / target))
        ox, oy = 0, (canvas.h - image.h) // 2
    else:
        canvas = pngio.Image.blank(round(image.h * target), image.h)
        ox, oy = (canvas.w - image.w) // 2, 0
    for y in range(image.h):
        canvas.data[((y + oy) * canvas.w + ox) * 4:((y + oy) * canvas.w + ox + image.w) * 4] = image.data[y * image.w * 4:(y + 1) * image.w * 4]
    return canvas


def generator_record(local: Path, gid: str) -> dict:
    path = local / "outbox" / BATCH / f"{gid}.provenance.json"
    if not path.is_file():
        raise GaugeError(f"the generator record {path} is missing: run the batch first")
    return json.loads(path.read_text(encoding="utf-8"))


def write_source(work: Path, name: str, image: pngio.Image, derived: dict) -> tuple[Path, Path]:
    """The cut image and a provenance file for `fit.py` (the generator's record plus how this file was derived from it; fit checks the hash)."""
    work.mkdir(parents=True, exist_ok=True)
    png = work / f"{name}.png"
    pngio.write(png, image)
    record = dict(derived)
    record["image"] = str(png.name)
    record["image_sha256"] = sha256_file(png)
    prov = work / f"{name}.provenance.json"
    prov.write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
    return png, prov


HALO_TIERS = ("8,20", "16,40")   # MEAN,P95 of the recorded overrides tried, in this order, for a keyed cut-out the standard gate refuses (see fit.gates)


def fit_part(part: str, source: Path, provenance: Path, local: Path, dry: bool) -> dict:
    """fit.py on one cut. A keyed cut-out whose only failure is the halo gate (a bumpy rim, a needle a few pixels wide: the band is measured before the safety net) is fitted again with the least
    recorded override (capped by fit.py) that passes."""
    tiers: list[str | None] = [None, *(HALO_TIERS if coverage().get(part, "").startswith("codex") else ())]
    for halo in tiers:
        try:
            return fit_with(part, source, provenance, local, dry, halo)
        except fit.GateError as error:
            if halo == tiers[-1] or not all(f.startswith("edge halo") for f in error.failures):
                raise
            print(f"{part}: halo gate refused at {halo or 'the standard limits'} ({error.failures[0]})")
    raise AssertionError("unreachable")


def fit_with(part: str, source: Path, provenance: Path, local: Path, dry: bool, halo: str | None) -> dict:
    ns = argparse.Namespace(part=part, batch=None, prompt=None, source=str(source), provenance=str(provenance), no_provenance=False, size=None, crop=None, key="none",
                            material=None, lock_strength=1.0, force_lock=False, min_speck=fit.MIN_SPECK, soft_shadow=False, date=None, local_root=str(local), masters=None,
                            replace=True, dry_run=dry, manifest=None, json=False, halo_limits=halo)
    return fit.run_fit(ns)


def result_path(local: Path, gid: str) -> tuple[Path, dict]:
    """The generator's picture of a group and its record. A run the generator called refused for its aspect (it saved the picture, but not at the asked aspect) leaves the picture in the run's work
    folder; it is used as it is (the cells are found by their content) and the record says so."""
    record = generator_record(local, gid)
    path = local / "outbox" / BATCH / f"{gid}.png"
    if path.is_file():
        return path, record
    canvas = local / "codex-work" / BATCH / str(record.get("run")) / f"{gid}.png"
    if record.get("outcome") == "refused" and canvas.is_file():
        record = dict(record)
        record["accepted"] = {"by": "gauge_kit.py cut", "why": "the generator refused the aspect but saved the picture; its cells are found by content, the layout drift is within the allowance",
                              "image": f"codex-work/{BATCH}/{record.get('run')}/{gid}.png", "image_sha256": sha256_file(canvas)}
        record["pixel_size"] = list(prompt_library.png_size(canvas))
        return canvas, record
    raise GaugeError(f"the result of {gid} is missing (run the Codex batch first)")


def cut_group(group: Group, local: Path, frames: Path, dry: bool, manifest: dict, cache: dict) -> list[str]:
    """Cuts one collage's result. Returns the part ids fitted (the parts of composites are done by `compose_parts` once both layers are cut)."""
    geometry = json.loads((local / "refs" / f"{group.gid}-layout.json").read_text(encoding="utf-8"))
    path, record = result_path(local, group.gid)
    size = tuple(record.get("pixel_size") or prompt_library.png_size(path))
    found = locate(path, geometry, size, local / "refs" / f"{group.gid}-original.png")  # type: ignore[arg-type]
    work = local / "work" / BATCH
    done: list[str] = []
    cache[group.gid] = {"geometry": geometry, "found": found, "path": path, "record": record}
    for cell in geometry["cells"]:
        if cell["part"] in RULE_PARTS:      # a cell of a part made by rule stays in the collage's layout but is not cut (a deep ground bleed in it is no matter)
            continue
        image = cell_image(path, found[cell["id"]], cell)
        cache[group.gid].setdefault("images", {})[cell["id"]] = image
        if cell["part"] is None or cell["mode"] == "placed":
            continue
        want = tuple(cell["master"])
        image = to_aspect(image, want)  # type: ignore[arg-type]
        derived = dict(record)
        derived["derived_from"] = {"image": path.relative_to(local).as_posix(), "image_sha256": sha256_file(path), "cell": cell["id"], "found": found[cell["id"]],
                                   "step": "gauge_kit.py cut (cell located by the layout and the content box, ground keyed by fit.key_background, brought to the master aspect)"}
        png, prov = write_source(work, cell["part"], image, derived)
        report = fit_part(cell["part"], png, prov, local, dry)
        print(f"{cell['part']}: {report['gates']} {report['size'][0]}x{report['size'][1]}")
        done.append(cell["part"])
    return done


def join_chain(cache: dict, local: Path, dry: bool) -> list[str]:
    """The chain strip: the six segments (each keyed cell at the master's scale, kept at its own place in its 48 x 360 cell) one under the other on a transparent 48 x 2160 canvas, fitted as
    `gauge.scrllsld_off`; `gauge.scrllsld_on` is its copy."""
    data = cache["gauge-scroll"]
    strip = pngio.Image.blank(48, 2160)
    for n in range(CHAIN_SEGMENTS):
        cid = f"chain{n}"
        layer, ox, oy = placed_layer(data, cid, data["found"][cid]["expect"][2] / 48)
        over(strip, layer, ox, n * 360 + oy)
    derived = dict(data["record"])
    derived["derived_from"] = {"image": data["path"].relative_to(local).as_posix(), "image_sha256": sha256_file(data["path"]), "cells": [f"chain{n}" for n in range(CHAIN_SEGMENTS)],
                               "step": "gauge_kit.py cut (six keyed cells joined top to bottom)"}
    png, prov = write_source(local / "work" / BATCH, "gauge.scrllsld_off", strip, derived)
    report = fit_part("gauge.scrllsld_off", png, prov, local, dry)
    print(f"gauge.scrllsld_off: {report['gates']} {report['size'][0]}x{report['size'][1]}")
    out = ["gauge.scrllsld_off"]
    if not dry:
        for part, source in COPIES.items():
            copy_record = dict(data["record"])
            copy_record["derived_from"] = {"part": source, "master_sha256": sha256_file(local / "masters" / f"{source}.png"),
                                           "step": "gauge_kit.py cut (the same picture as the chain strip's master, fitted again by the same pipeline)"}
            png2, prov2 = write_source(local / "work" / BATCH, part, pngio.read(local / "masters" / f"{source}.png"), copy_record)
            rep = fit_part(part, png2, prov2, local, dry)
            print(f"{part}: {rep['gates']} {rep['size'][0]}x{rep['size'][1]} (a copy of {source})")
            out.append(part)
    return out


def over(base: pngio.Image, layer: pngio.Image, x: int, y: int) -> None:
    """Source-over of a straight-alpha layer on a straight-alpha base, in place."""
    for ly in range(layer.h):
        by = y + ly
        if not 0 <= by < base.h:
            continue
        for lx in range(layer.w):
            bx = x + lx
            if not 0 <= bx < base.w:
                continue
            li, bi = (ly * layer.w + lx) * 4, (by * base.w + bx) * 4
            a = layer.data[li + 3] / 255
            if a == 0:
                continue
            b = base.data[bi + 3] / 255
            out = a + b * (1 - a)
            for c in range(3):
                base.data[bi + c] = round((layer.data[li + c] * a + base.data[bi + c] * b * (1 - a)) / out)
            base.data[bi + 3] = round(out * 255)


def placed_layer(data: dict, cid: str, px_per_master: float) -> tuple[pngio.Image, int, int]:
    """A keyed cell (not trimmed) at the master's scale: the layer, and where its top left lies relative to the cell's expected top left, in master px (negative where the region grew)."""
    image = data["images"][cid]
    found = data["found"][cid]
    layer = fit.resample_image(image, (0, 0, image.w, image.h), max(1, round(image.w / px_per_master)), max(1, round(image.h / px_per_master)))
    return layer, round((found["region"][0] - found["expect"][0]) / px_per_master), round((found["region"][1] - found["expect"][1]) / px_per_master)


def original_box(frames: Path, cell: dict) -> tuple[int, int, int, int]:
    """The bounding box (x0, y0, x1, y1 exclusive, units of the base art) of what the original frame of a placed cell adds to its base (the moon's lit part, a counter's picture)."""
    image = read_frame(frames, cell["folder"], cell["frame"])
    base = read_frame(frames, cell["mask"][5:])
    hit = moon_disc(frames) if cell["id"].startswith("moon") else foreground(image, base, 1)
    xs, ys = [x for x, _ in hit], [y for _, y in hit]
    return min(xs), min(ys), max(xs) + 1, max(ys) + 1


def compose_parts(base_part: str, layer_group: str, layer_cells: dict[str, str], frames: Path, cache: dict, local: Path, dry: bool, discs: bool = False) -> list[str]:
    """The base part's master with a keyed layer cell laid over it where the original has it. The generator redraws a cell a little larger or moved, so the layer is placed by its content:
    the scale makes the generated content box as large as the original's (a picture: its own scale; the moon phases: one scale for all, from the first), and the box is laid on the original's
    (a picture: centre on centre; a moon: the whole disc's left edge and bottom, because the top of the disc is cut by the frame)."""
    base_path = local / "masters" / f"{base_part}.png"
    if not base_path.is_file():
        if dry:
            print(f"{layer_group}: dry run, the base master {base_part} is not on disk yet, so its layers are not composed")
            return []
        raise GaugeError(f"the base master {base_part} is not made yet: cut its group first (the layers of {layer_group} lie over it)")
    base_master = pngio.read(base_path)
    data = cache[layer_group]
    cells = {c["id"]: c for c in data["geometry"]["cells"]}
    plan = {}
    for part, cid in layer_cells.items():
        found = data["found"][cid]
        region = found["region"]
        gx0, gy0, gw, gh = found["box"][0] - region[0], found["box"][1] - region[1], found["box"][2], found["box"][3]
        ox0, oy0, ox1, oy1 = original_box(frames, cells[cid])
        plan[part] = (cid, (gx0, gy0, gw, gh), (ox0, oy0, ox1, oy1))
    common = None
    if discs:
        full = plan[next(iter(plan))]   # the first moon cell is the full disc (art 209)
        common = ((full[2][2] - full[2][0]) * UNIT) / full[1][2]
    done = []
    for part, (cid, (gx0, gy0, gw, gh), (ox0, oy0, ox1, oy1)) in plan.items():
        scale = common if common is not None else ((ox1 - ox0) * UNIT / gw + (oy1 - oy0) * UNIT / gh) / 2    # master px per result px
        image = data["images"][cid]
        layer = fit.resample_image(image, (0, 0, image.w, image.h), max(1, round(image.w * scale)), max(1, round(image.h * scale)))
        bw, bh = gw * scale, gh * scale
        if discs:
            ex, top = ox0 * UNIT, oy1 * UNIT - bh          # the whole disc: left edges and bottoms together (the disc's top is cut by the frame)
        else:
            ex, top = (ox0 + ox1) * UNIT / 2 - bw / 2, (oy0 + oy1) * UNIT / 2 - bh / 2
        out = pngio.Image(base_master.w, base_master.h, bytearray(base_master.data))
        over(out, layer, round(ex - gx0 * scale), round(top - gy0 * scale))
        derived = {"composite": {"base_part": base_part, "base_master_sha256": sha256_file(base_path), "layer_cell": cid, "layer_group": layer_group, "layer_image_sha256": sha256_file(data["path"]),
                                 "original_box_units": [ox0, oy0, ox1, oy1], "generated_box_px": [gx0, gy0, gw, gh], "scale_master_per_result_px": round(scale, 5)},
                   "generator": data["record"], "step": "gauge_kit.py cut (the layer keyed against the ground and laid over the base master, its content box on the original's)"}
        png, prov = write_source(local / "work" / BATCH, part, out, derived)
        report = fit_part(part, png, prov, local, dry)
        print(f"{part}: {report['gates']} {report['size'][0]}x{report['size'][1]} (composite)")
        done.append(part)
    return done


def cmd_cut(args: argparse.Namespace) -> int:
    local = args.local_root
    frames = args.frames or default_frames(local)
    manifest = load_manifest()
    cache: dict = {}
    fitted: list[str] = []
    for group in selected_groups(args):
        fitted += cut_group(group, local, frames, args.dry_run, manifest, cache)
    if "gauge-scroll" in cache:
        fitted += join_chain(cache, local, args.dry_run)
    if "gauge-clock-moons" in cache:
        fitted += compose_parts("gauge.clk_moon1", "gauge-clock-moons", MOON_PARTS_BY_CELL, frames, cache, local, args.dry_run, discs=True)
    if "gauge-ammo" in cache:
        fitted += compose_parts("gauge.amocnt_b", "gauge-ammo", AMMO_PARTS, frames, cache, local, args.dry_run)
    print(f"GAUGE KIT cut PASS: {len(fitted)} master(s)" + (" (dry run, nothing written)" if args.dry_run else ""))
    return 0


MOON_PARTS_BY_CELL = {f"gauge.clk_moon{n + 1}": f"moon{n}" for n in range(1, 8)}


# --- rule parts ---------------------------------------------------------------------------------------------------------------------------

def bands(image: pngio.Image, axis: str) -> list[tuple[int, int]]:
    """The runs of rows (axis y) or columns (axis x) whose mean colour stays within 24 (summed channel difference) of the run's first line."""
    n = image.h if axis == "y" else image.w
    means = []
    for i in range(n):
        cells = [(image.pixel(x, i) if axis == "y" else image.pixel(i, x)) for x in range(image.w if axis == "y" else image.h)]
        opaque = [c for c in cells if c[3] > 128]
        means.append(tuple(sum(c[k] for c in opaque) / len(opaque) for k in range(3)) if opaque else None)
    out, start = [], 0
    for i in range(1, n + 1):
        if i == n or (means[i] is None) != (means[start] is None) or (means[i] is not None and means[start] is not None and sum(abs(a - b) for a, b in zip(means[i], means[start])) > 24):
            out.append((start, i))
            start = i
    return out


def denoise(image: pngio.Image, axis: str) -> pngio.Image:
    """Every band of rows (axis y: columns keep their own colour) replaced by the per-column median of its rows; axis x the same turned round. Transparent pixels stay transparent."""
    out = pngio.Image(image.w, image.h, bytearray(image.data))
    runs = bands(image, axis)
    lines = image.w if axis == "y" else image.h
    for start, stop in runs:
        for line in range(lines):
            px = [(image.pixel(line, i) if axis == "y" else image.pixel(i, line)) for i in range(start, stop)]
            solid = [p for p in px if p[3] > 128]
            if not solid:
                median = (0, 0, 0, 0)
            else:
                median = tuple(int(statistics.median(p[k] for p in solid)) for k in range(3)) + (255 if len(solid) * 2 >= len(px) else 0,)
            for i in range(start, stop):
                o = ((i * image.w + line) if axis == "y" else (line * image.w + i)) * 4
                out.data[o:o + 4] = bytes(median)
    return out


def rule_part(part: str, frames: Path, manifest: dict) -> tuple[pngio.Image, dict]:
    folder, frame, axis = RULE_PARTS[part]
    want = master_of(part, manifest)
    image = read_frame(frames, folder, frame)
    if axis == "flat":
        last = image
        steps = [read_frame(frames, folder, n).pixel(0, 0) for n in range(9)]
        # the nine frames are one colour ramp (the damage of an item): the master is the last frame's colour, the ramp is the runtime's value
        ramp_ok = all(abs(steps[n][0] - (steps[0][0] + (steps[8][0] - steps[0][0]) * n / 8)) <= 2 for n in range(9))
        if not ramp_ok:
            raise GaugeError(f"{folder}: the nine damage frames are not one linear ramp, so the last frame's colour alone does not stand for them")
        r, g, b, a = last.pixel(0, 0)
        out = pngio.Image(want[0], want[1], bytearray(bytes((r, g, b, 255)) * (want[0] * want[1])))
        return out, {"ramp_linear": ramp_ok, "colour": [r, g, b]}
    if axis == "smooth":
        return fit.resample_image(image, (0, 0, image.w, image.h), want[0], want[1]), {"axis": axis, "source_size": [image.w, image.h]}
    cleaned = denoise(image, axis)
    out = fit.resample_image(cleaned, (0, 0, cleaned.w, cleaned.h), want[0], want[1])
    return out, {"bands": len(bands(image, axis)), "axis": axis}


def cmd_rule(args: argparse.Namespace) -> int:
    local = args.local_root
    frames = args.frames or default_frames(local)
    manifest = load_manifest()
    only = {x for x in (args.only or "").split(",") if x}
    done = 0
    for part in RULE_PARTS:
        if only and part not in only:
            continue
        image, extra = rule_part(part, frames, manifest)
        folder, frame, axis = RULE_PARTS[part]
        blob = pngio.encode(image)
        print(f"{part}: {image.w}x{image.h} from {folder}/f{frame} ({axis}) {extra}")
        if not args.dry_run:
            target = local / "masters" / f"{part}.png"
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(blob)
            record = {"part": part, "rule": "gauge_kit", "tool_version": TOOL_VERSION, "date": datetime.date.today().isoformat(), "source": f"{folder}/f{frame}",
                      "source_sha256": sha256_file(frames / folder / f"f{frame}.png"), "axis": axis, **extra, "image_size": [image.w, image.h],
                      "image_sha256": hashlib.sha256(blob).hexdigest(), "run_by": "agent"}
            (local / "provenance" / "rule").mkdir(parents=True, exist_ok=True)
            (local / "provenance" / "rule" / f"{part}.json").write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
        done += 1
    print(f"GAUGE KIT rule PASS: {done} part(s)" + (" (dry run, nothing written)" if args.dry_run else ""))
    return 0


def cmd_plan(args: argparse.Namespace) -> int:
    manifest = load_manifest()
    parts = parts_of_batch(manifest)
    how = coverage(manifest)
    missing = sorted(set(parts) - set(how))
    extra = sorted(set(how) - set(parts))
    for part in sorted(parts):
        print(f"{part:34} {how.get(part, '-')}")
    if missing or extra:
        print(f"GAUGE KIT plan FAIL: not covered {missing}; not in the manifest {extra}")
        return 1
    print(f"GAUGE KIT plan PASS: {len(parts)} part(s), {len(GROUPS)} Codex collage(s), {len(RULE_PARTS)} rule part(s)")
    return 0


def cmd_self_test(args: argparse.Namespace) -> int:
    manifest = load_manifest()
    assert not set(parts_of_batch(manifest)) ^ set(coverage(manifest)), "every part of B-GAUGES is made exactly one way"
    # a collage of a bleed group is its cell; a grid group is centred and rounded to the grid
    first = Group("t", "t", ((Cell("a", "x", part=None, out=(60, 30), mode="rect", rect=(0, 0, 1, 1)),), (Cell("b", "x", part=None, out=(30, 30), mode="rect", rect=(0, 0, 1, 1)),)), k=2.0)
    geometry = layout(first, Path("."), manifest)
    assert geometry["canvas"][0] % GRID == 0 and geometry["canvas"][1] % GRID == 0, geometry
    a, b = geometry["cells"]
    assert a["x"] + a["w"] <= geometry["canvas"][0] and b["y"] >= a["y"] + a["h"] + GAP - 1, geometry
    # denoise keeps a vertically uniform band and removes its dither
    image = pngio.Image.blank(3, 6)
    for y in range(6):
        for x in range(3):
            v = 100 + (3 if (x + y) % 2 else 0)
            image.data[(y * 3 + x) * 4:(y * 3 + x) * 4 + 4] = bytes((v, v, v, 255))
    clean = denoise(image, "y")
    assert all(clean.pixel(x, y) == clean.pixel(x, 0) for x in range(3) for y in range(6)), "bands are vertically uniform after denoise"
    # an aspect within 6 % is brought to the master aspect, beyond it is refused
    wide = pngio.Image.blank(103, 100, (9, 9, 9, 255))
    assert to_aspect(wide, (100, 100)).w == 103 or to_aspect(wide, (100, 100)).h == 103
    try:
        to_aspect(pngio.Image.blank(130, 100, (9, 9, 9, 255)), (100, 100))
    except GaugeError:
        pass
    else:
        raise AssertionError("a 30 % drift must be refused")
    print("GAUGE KIT self-test PASS")
    return 0


# --- before / after sheet ---------------------------------------------------------------------------------------------------------------

LABEL_FONT = "/System/Library/Fonts/Supplemental/Arial.ttf"   # the labels of a composite are English (project rule)
SHEET_WIDTH = 2400
SHEET_ORDER = ("gauge.yelovial", "gauge.redvial", "gauge.bluvial", "gauge.clrvial", "gauge.yelwvial", "gauge.bar", "gauge.followerframe_ft", "gauge.followerframe_hp",
               "gauge.followerframe_po", "gauge.followerframe_poison", "gauge.item_dam", "gauge.xp_gaugemag",
               "gauge.meshealthbar_full", "gauge.meshealthbar_empty", "gauge.mesfatiguebar_full", "gauge.mesfatiguebar_empty", "gauge.mespoisonedbar_full", "gauge.mespoisonedbar_empty", "gauge.skilgauge",
               "gauge.xp_pip1", "gauge.xp_pip2", "gauge.xp_pip3", "gauge.xp_pip4", "gauge.xp_pip5", "gauge.xp_pip6", "gauge.xp_pip7", "gauge.xp_pip8", "gauge.xp_pip9", "gauge.xp_pip10",
               "gauge.clk_timestrip", "gauge.clk_moon1", "gauge.clk_moon2", "gauge.clk_moon3", "gauge.clk_moon4", "gauge.clk_moon5", "gauge.clk_moon6", "gauge.clk_moon7", "gauge.clk_moon8", "gauge.timepnts",
               "gauge.amocnt_b", "gauge.amocnt_s", "gauge.amocnt_arrows", "gauge.amocnt_bullets", "gauge.amocnt_charges", "gauge.amocnt_fuel", "gauge.amo_rol", "gauge.amo_rolx",
               "gauge.loadindicator", "gauge.align", "gauge.mt_apt", "gauge.scrllslidet", "gauge.scrllslidem", "gauge.scrllslideb", "gauge.scrllslidem1", "gauge.scrllsld_off", "gauge.scrllsld_on", "gauge.item_dam")


def original_folder(part: dict, frames: Path) -> str:
    art = part["source_arts"][0]
    if art.startswith("file:"):
        return "x-" + art[5:]
    found = sorted(p.name for p in frames.iterdir() if p.name.startswith(f"{art}-"))
    if not found:
        raise GaugeError(f"no original frames for art {art}")
    return found[0]


def cmd_sheet(args: argparse.Namespace) -> int:
    """The look-check composite: for each part the ORIGINAL (frame 0, nearest, at the master's pixel size) above the KIT master (as the runtime draws it, on the same dark ground)."""
    need_magick()
    local = args.local_root
    frames = args.frames or default_frames(local)
    parts = parts_of_batch()
    only = {x for x in (args.only or "").split(",") if x}
    cells: list[Path] = []
    work = Path(tempfile.mkdtemp(prefix="gauge-sheet-"))
    try:
        seen: set[str] = set()
        for ident in SHEET_ORDER:
            if ident in seen or (only and ident not in only):
                continue
            seen.add(ident)
            part = parts[ident]
            master = local / "masters" / f"{ident}.png"
            if not master.is_file():
                continue
            w, h = part["size"]
            fr = 8 if ident == "gauge.item_dam" else 0
            crop = "-crop", f"{w}x540+0+0", "+repage"
            tall = h > 600                                  # the chain strips: only the top 540 px are shown
            scale = min(1.0, (SHEET_WIDTH // 2 - 40) / w)
            dw, dh = max(1, round(w * scale)), max(1, round((540 if tall else h) * scale))
            before, after, cell = work / f"{ident}-b.png", work / f"{ident}-a.png", work / f"{ident}.png"
            run(["magick", str(frames / original_folder(part, frames) / f"f{fr}.png"), "-background", "#202020", "-alpha", "remove", "-filter", "point", "-resize", f"{w}x{h}!", *(crop if tall else ()),
                 "-resize", f"{dw}x{dh}!", str(before)])
            run(["magick", str(master), "-background", "#202020", "-alpha", "remove", *(crop if tall else ()), "-filter", "Lanczos", "-resize", f"{dw}x{dh}!", str(after)])
            run(["magick", "-size", f"{max(dw, 150)}x24", "xc:#303030", "-font", LABEL_FONT, "-fill", "white", "-pointsize", "14", "-gravity", "west", "-annotate", "+4+0", ident.removeprefix("gauge.") + (" (top 540 px)" if tall else ""),
                 "(", str(before), "-background", "#303030", "-extent", f"{max(dw, 150)}x{dh}", ")", "(", str(after), "-background", "#303030", "-extent", f"{max(dw, 150)}x{dh}", ")",
                 "-append", "-bordercolor", "#303030", "-border", "6", str(cell)])
            cells.append(cell)
        if not cells:
            raise GaugeError("no master of the batch to show")
        rows: list[list[Path]] = [[]]
        used = 0
        for cell in cells:
            width = int(run(["magick", "identify", "-format", "%w", str(cell)]))
            if used and used + width > SHEET_WIDTH:
                rows.append([])
                used = 0
            rows[-1].append(cell)
            used += width
        row_files = []
        for i, row in enumerate(rows):
            path = work / f"row{i}.png"
            run(["magick", *row, "-background", "#303030", "-gravity", "north", "+append", "-extent", f"{SHEET_WIDTH}x0", str(path)])
            row_files.append(path)
        head = work / "head.png"
        run(["magick", "-size", f"{SHEET_WIDTH}x40", "xc:#202020", "-font", LABEL_FONT, "-fill", "white", "-pointsize", "20", "-gravity", "west", "-annotate", "+10+0",
             "ARC-UI-2483 B-GAUGES: for each part, top = ORIGINAL (frame 0, nearest), bottom = KIT master; same pixel size (wide parts reduced equally, chain strips cropped)", str(head)])
        run(["magick", str(head), *row_files, "-background", "#303030", "-append", str(args.out)])
    finally:
        shutil.rmtree(work, ignore_errors=True)
    print(f"GAUGE KIT sheet PASS: {args.out} ({len(cells)} parts)")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = parser.add_subparsers(dest="command", required=True)
    for name, fn in (("plan", cmd_plan), ("refs", cmd_refs), ("cut", cmd_cut), ("rule", cmd_rule), ("sheet", cmd_sheet), ("self-test", cmd_self_test)):
        s = sub.add_parser(name)
        s.set_defaults(fn=fn)
        s.add_argument("--only")
        s.add_argument("--frames", type=Path)
        s.add_argument("--local-root", type=Path)
        s.add_argument("--dry-run", action="store_true")
        s.add_argument("--out", type=Path)
    args = parser.parse_args(argv)
    args.local_root = args.local_root or prompt_library.default_local_root()
    try:
        return args.fn(args)
    except (GaugeError, fit.FitError, fit.GateError, pngio.PngError) as error:
        print(f"GAUGE KIT {args.command} FAIL: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
