#!/usr/bin/env python3
"""ARC-UI-3278 (batch B-HARDWARE, slice 4): the pop-up panel bases and their frames as kit parts, PACK-FIRST (owner rule 2026-11-25) and otherwise BY RULE from the original's own arts (no Codex): the 410 x 107 "Rot" family
(arts 7 128 200 274 288 290 291 354 564 642 842) and the framed panels (822 DialogueBox, 298 MultiMove_Base, 826 FollwerComandMenu, 292 Fate_Base, 565 Sleep_Base, 328 MenuPlaque): 17 arts, one master each (the art's whole
rectangle at the kit's 3.6 scale). An art with a good frame in the old HD pack is that frame resampled plus an analytic alpha (`pack_master`); an art with none, or whose frame does not fit the original's window, is built by rule (`render`).

    python3 -B tools/steamwoven_ui/popup_kit.py plan
    python3 -B tools/steamwoven_ui/popup_kit.py build [--only ID,...] [--dry-run] [--pack DIR] [--alt-dir DIR] [--frames DIR] [--local-root DIR]   (the masters, their records; --alt-dir: the rule build of the pack-first arts, for the composite)
    python3 -B tools/steamwoven_ui/popup_kit.py sheet --out FILE [--only ID,...] [--zoom N] [--alt-dir DIR] [--frames DIR] [--local-root DIR]    (composite original | kit master | alternative, English labels, needs ImageMagick)
    python3 -B tools/steamwoven_ui/popup_kit.py self-test

Each art is an iron plate with brass frames, dark text windows, recessed slots and rows, and round sockets. The rule build rebuilds every one of them at the master's resolution (TRD SWI-KIT, the owner's rules of #3259: opaque details, the light from the
top left, thin dark slot frames, the fill visible through a window, the muted original palette, nothing added):

* **Shapes** come from the original's own pixels: each pixel of the art is classified (brass, window dark, olive socket, iron, transparent), the classes are smoothed and enlarged with a cubic filter and cut with a steep threshold, so every outline is the original's own
  and sub-pixel smooth at 3.6 times the size (no stair, no halo). The curls of art 822, the tabs of arts 7 and 291 and the sockets are not drawn by hand, they are the original's shapes.
* **Iron** (the plate): the plate is made new, a dark neutral iron of the strip end sections (HUD top plate, #3259: mean 46, sd 9) from seeded noise (blotches 6.5, mid grain 3.2, fine grain 4.2) (coarse blotches, mid grain, fine grain, a few light specks); the old art's marbled iron is not used. The art's
  own outer bevel (a light line at the top left, a dark one at the bottom right) is laid by rule.
* **Brass** (frames, tabs, rings): a height field from the distance to the shape's edge, a short bevel, lit from the top left (a sharp highlight ridge, a warm copper bounce at the bottom right, no smooth gradient, no dark outline) with the tone and the fine
  grain of the kit's approved brass (the HUD socket ring of #3259, unrolled along its circumference). Rivets are domes lit the same way.
* **Wells and rows** (slots, list rows): a recessed rectangle by rule (a dark shadow along the top and the left, a light line along the bottom and the right, the iron a little darker inside), the positions are the original's.
* **Windows**: the original's dark fill, kept (the screen's text shows on it); a stray baked detail inside a window (the blue bar, a sample icon) is not kept.
* **Sockets, glyphs, small shaded details** (the round buttons' faces and glyphs, the icon medallions, the odometer, the puzzle pieces): the original's pixels of that spot, enlarged with a cubic filter inside the shape's own mask; the ring around is brass by rule.

Everything stays under the ignored `local/steamwoven-ui/`; nothing here holds art. Standard library only (`pngio`, `fit`); ImageMagick for the composite. Exit 0 = done, 1 = a gate of a master failed, 2 = an input could not be used.
"""
from __future__ import annotations

import argparse
import datetime
import hashlib
import json
import math
import random
import shutil
import subprocess
import sys
import tempfile
from dataclasses import dataclass
from itertools import accumulate
from pathlib import Path

HERE = Path(__file__).resolve().parent
try:
    from . import fit, pngio
except ImportError:  # run as a script
    sys.path.insert(0, str(HERE))
    import fit  # type: ignore
    import pngio  # type: ignore

TOOL_VERSION = 2
BATCH = "B-HARDWARE"
GRID = 6
LABEL_FONT = "/System/Library/Fonts/Supplemental/Arial.ttf"   # the labels of a composite are English (project rule)
MANIFEST = HERE / "kit.json"

IRON_FLOOR = 14.0         # the kit's iron is never darker than 17 (the bevel and the noise are kept above 14)
IRON_MEAN = 47.0          # the kit's iron (HUD top plate: mean 46, sd 9 in all): blotches sd 6.5, mid grain 3.2, fine grain 4.2
GRAIN_SHARE = 0.3          # the share of the approved brass's fine-grain sd the plates carry (a big plate at the master scale reads sandy at 1.0)
GRAIN_DETAIL = 0.204      # the detail sd (pixel minus the mean of its 8 neighbours) of blur121(N(0,1)), measured
BEVEL = 5.0               # brass bevel width, master px
EDGE_K = 4.0              # steepness of the shape threshold: transition about 1.8 master px
LIGHT = (-0.50, -0.62, 0.60)
COPPER = (214, 118, 44)
BRASS_DARK = (24, 14, 6)
BRASS_HIGH = (240, 194, 106)
BRASS_SOURCES = ("hardware.hud_ring", "hardware.hud_top_clock", "hardware.menu_window")    # the approved brass of the kit the tone and the grain are taken from


class KitError(Exception):
    """An input could not be used (exit 2)."""


class GateError(Exception):
    """A check of a master failed (exit 1)."""


@dataclass(frozen=True)
class Spec:
    ident: str
    art: str
    name: str
    kind: str                          # rot | frame | list | plain
    rivets: tuple = ()                 # (x, y) original px: a rivet dome on the brass
    wells: tuple = ()                  # (x0, y0, x1, y1) original px: a recessed slot
    rows: tuple = ()                   # (x0, x1, y0, pitch, count, height): recessed list rows
    rings: tuple = ()                  # (cx, cy, r_mid): a round socket's brass ring, analytic (the original's centre and radius, band RING_BAND wide); the original's face inside it is kept
    copies: tuple = ()                 # ("ellipse"|"rect", ...) original px: the original's pixels enlarged inside this shape
    emboss: tuple = ()                 # (x0, y0, x1, y1): the original's strong relief (a piece, a sign) inside this box, added to the new iron
    fills: tuple = ()                  # (x0, y0, x1, y1, (r, g, b)): a flat fill (a window that is dark grey)
    force_w: tuple = ()                # boxes made window dark (a baked detail inside a window)
    force_i: tuple = ()                # boxes made iron
    pack: str = ""                     # the old HD pack's frame of this art (path under the pack's `interface/`, no `-f000.png`): pack-first, see below; empty = no good frame, the rule build is the master
    pack_clear: tuple = ()             # (x0, y0, x1, y1) original px: a stand-in baked into the pack's frame (a sample icon) is painted out with the window's colour
    note: str = ""


def box(cx: float, cy: float, r: float) -> tuple:
    return (cx - r, cy - r, cx + r, cy + r)


RING_BAND = 3.6        # a socket ring's brass band, original px
RIV_L = ((14.5, 30.0), (14.5, 56.5), (14.5, 79.5))
def face(cx: float, cy: float, rm: float) -> tuple:
    return ("ellipse", cx, cy, rm - RING_BAND / 2 + 0.3, rm - RING_BAND / 2 + 0.3)     # the original's face inside a ring (a little under the band)

SPECS: tuple[Spec, ...] = (
    Spec("hardware.mess_rot", "7", "mess_rot", "rot", pack="dialogue/buttons/mess_rot", rivets=RIV_L + ((354.0, 30.0), (354.0, 56.0), (354.0, 79.5)),
         rings=((383.0, 32.3, 17.2), (383.0, 75.7, 17.2)), copies=(face(383.0, 32.3, 17.2), face(383.0, 75.7, 17.2)), note="the big message window with the riveted side tabs and two round sockets"),
    Spec("hardware.skill_ro", "128", "skill_ro", "plain", note="an olive noise art, no brass, no window, no sockets: classified unused, the master is the plain iron plate of its size"),
    Spec("hardware.mpwt_rot", "200", "MpWt_Rot", "rot", pack="multiplayer/buttons/mpwt_rot", rings=((101.7, 74.3, 19.3), (216.7, 75.3, 19.3), (331.7, 75.3, 19.3), (29.3, 27.8, 14.3), (28.5, 54.5, 14.3), (28.4, 81.3, 14.3)),
         copies=(face(101.7, 74.3, 19.3), face(216.7, 75.3, 19.3), face(331.7, 75.3, 19.3), face(29.3, 27.8, 14.3), face(28.5, 54.5, 14.3), face(28.4, 81.3, 14.3)), rivets=((52.0, 28.0), (381.0, 28.0)), note="the L-shaped brass bar with three icon medallions, its dark window and three round buttons"),
    Spec("hardware.skill_rot", "274", "Skill_Rot", "rot", pack="logbook/buttons/skill_rot", wells=((85, 16, 129, 60), (148, 16, 192, 60), (211, 16, 255, 60), (274, 16, 318, 60)), note="four recessed square slots over a text window"),
    Spec("hardware.combine_rot", "288", "Combine_Rot", "rot", pack="schematics/buttons/combine_rot", rings=((277.6, 42.1, 17.3),), wells=((9, 7, 106, 72), (155, 7, 254, 72), (303, 7, 402, 72)),
         emboss=((35, 18, 76, 62), (190, 18, 220, 62), (328, 18, 376, 62), (120, 28, 142, 50)), note="three recessed piece wells, a plus sign, a round socket opening and a text window"),
    Spec("hardware.trap_rot", "290", "Trap_Rot", "rot", pack="logbook/buttons/trap_rot", note="four square openings (transparent) over a text window"),
    Spec("hardware.broadcast_rot", "291", "Broadcast_Rot", "rot", rivets=RIV_L + ((396.0, 30.0), (396.0, 56.0), (396.0, 79.5)), note="the big window with the riveted side tabs (the pack frame's window is smaller than the original's, so no pack frame)"),
    Spec("hardware.dialoguewindow", "354", "DialogueWindow", "rot", note="the big window in a brass frame (the pack frame's window is smaller than the original's, so no pack frame)"),
    Spec("hardware.magickweapon_rot", "564", "MagickWeapon_Rot", "rot", pack="logbook/buttons/magickweapon_rot", pack_clear=((33, 58, 127, 94),), force_w=((32, 58, 128, 95),), wells=((155, 48, 191, 84), (204, 48, 240, 84), (253, 48, 289, 84), (303, 48, 339, 84), (352, 48, 388, 84)), note="a window, a picture field, a text window and five recessed slots"),
    Spec("hardware.privmes_window", "642", "PrivMes_Window", "rot", force_w=((90, 68, 366, 94),), rings=((391.5, 28.4, 11.9),), copies=(face(391.5, 28.4, 11.9),), note="the private message window with a small round socket"),
    Spec("hardware.mp_kickbanrotwindow", "842", "MP_KickBanRotWindow", "rot", pack="multiplayer/panels/mp_kickbanrotwindow", rings=((119.0, 63.0, 32.4), (288.0, 63.0, 32.4)), copies=(face(119.0, 63.0, 32.4), face(288.0, 63.0, 32.4)), note="two big round buttons on the plain plate"),
    Spec("hardware.dialoguebox", "822", "DialogueBox", "frame", pack="dialogue/panels/dialoguebox", note="the thin brass window frame with the original's curled corners over an iron plate"),
    Spec("hardware.multimove_base", "298", "MultiMove_Base", "frame", pack="hud-bar/panels/multimove_base", rings=((179.2, 67.4, 12.0), (365.7, 31.3, 19.3), (365.7, 82.2, 19.3)),
         copies=(face(179.2, 67.4, 12.0), face(365.7, 31.3, 19.3), face(365.7, 82.2, 19.3), ("rect", 207, 56, 283, 75), ("rect", 283, 55, 295, 76)),
         force_i=((70, 42, 155, 90),), wells=((71, 43, 154, 89),), fills=((72, 44, 153, 88, (3, 3, 10)),), note="the dark window, the counter and two round sockets"),
    Spec("hardware.follwercomandmenu", "826", "FollwerComandMenu", "list", rows=((6, 125, 20, 36, 4, 16),), note="the follower command menu: recessed list rows"),
    Spec("hardware.fate_base", "292", "Fate_Base", "list", pack="character-sheet/panels/fate_base", rows=((6, 218, 20, 36, 6, 16),), note="the fate list: recessed rows; the square openings at the right are transparent as in the original"),
    Spec("hardware.sleep_base", "565", "Sleep_Base", "list", pack="world-map/panels/sleep_base", rows=((6, 217, 20, 36, 4, 16),), note="the sleep list: recessed rows"),
    Spec("hardware.menuplaque", "328", "MenuPlaque", "frame", pack="menus/panels/menuplaque", fills=((34, 242, 201, 309, (30, 30, 32)),), note="the plain iron plaque with a thin brass-framed box"),
)
BY_ID = {s.ident: s for s in SPECS}


def coverage() -> dict[str, str]:
    return {s.ident: (f"pack:{s.kind}" if s.pack else f"rule:{s.kind}") for s in SPECS}


def provenance_of(part: str) -> str:
    return f"{'pack' if BY_ID[part].pack else 'rule'}:popup_kit:{part}"


def note_of(part: str) -> str:
    spec = BY_ID[part]
    if not spec.pack:
        how = "no good pack frame: iron, brass, wells and sockets by rule from the original art's own shapes"
    else:
        how = "the old HD pack's frame of the art (pack-first) at the master's size with the original's alpha enlarged analytically"
        if spec.pack_clear:
            how += "; the pack frame's baked sample icon painted out"
    return f"{spec.note}; {how}; popup_kit.py, ARC-UI-3278, no Codex"


# --- small numeric helpers -----------------------------------------------------------------------------------------------------------

def clamp(v: float, lo: float, hi: float) -> float:
    return lo if v < lo else hi if v > hi else v


def lum(r: float, g: float, b: float) -> float:
    return 0.299 * r + 0.587 * g + 0.114 * b


def sha256_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


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


def read_frame(frames: Path, spec: Spec) -> pngio.Image:
    path = frames / f"{spec.art}-{spec.name}" / "f0.png"
    if not path.is_file():
        raise KitError(f"{path} is missing (the census frames of the original, `local/steamwoven-ui/census/frames`)")
    return pngio.read(path)


def blur121(plane: list[float], w: int, h: int) -> list[float]:
    """[1 2 1] / 4 in both directions (edges clamped)."""
    rows = []
    for y in range(h):
        row = plane[y * w:(y + 1) * w]
        left, right = [row[0]] + row[:-1], row[1:] + [row[-1]]
        rows.append([(a + 2 * b + c) * 0.25 for a, b, c in zip(left, row, right)])
    out: list[list[float]] = []
    for y in range(h):
        up, dn = rows[max(0, y - 1)], rows[min(h - 1, y + 1)]
        out.append([(a + 2 * b + c) * 0.25 for a, b, c in zip(up, rows[y], dn)])
    return [v for row in out for v in row]


def box_blur(plane: list[float], w: int, h: int, radius: int) -> list[float]:
    """A separable box blur with clamped edges by running sums (a low-pass for a shadow)."""
    def line(values: list[float]) -> list[float]:
        n = len(values)
        padded = [values[0]] * radius + values + [values[-1]] * radius
        cs = [0.0] + list(accumulate(padded))
        size = 2 * radius + 1
        return [(cs[i + size] - cs[i]) / size for i in range(n)]
    rows = [line(plane[y * w:(y + 1) * w]) for y in range(h)]
    cols = [line([rows[y][x] for y in range(h)]) for x in range(w)]
    return [cols[x][y] for y in range(h) for x in range(w)]


def edt(inside: list[bool], w: int, h: int) -> list[float]:
    """The exact Euclidean distance (px) from each True pixel to the nearest False pixel (0 for a False pixel); Felzenszwalb-Huttenlocher, two passes."""
    inf = 1e12
    f = [0.0 if not v else inf for v in inside]

    def pass1d(vals: list[float]) -> list[float]:
        n = len(vals)
        v = [0] * n
        z = [0.0] * (n + 1)
        k = 0
        z[0], z[1] = -inf, inf
        for q in range(1, n):
            while True:
                p = v[k]
                s = ((vals[q] + q * q) - (vals[p] + p * p)) / (2.0 * q - 2.0 * p)
                if s <= z[k]:
                    k -= 1
                else:
                    break
            k += 1
            v[k] = q
            z[k], z[k + 1] = s, inf
        out = [0.0] * n
        k = 0
        for q in range(n):
            while z[k + 1] < q:
                k += 1
            out[q] = (q - v[k]) ** 2 + vals[v[k]]
        return out
    cols = [pass1d([f[y * w + x] for y in range(h)]) for x in range(w)]
    out = [0.0] * (w * h)
    for y in range(h):
        row = pass1d([cols[x][y] for x in range(w)])
        base = y * w
        for x in range(w):
            out[base + x] = math.sqrt(row[x]) if row[x] < inf / 2 else 1e6
    return out


# --- the original's classes ----------------------------------------------------------------------------------------------------------

I, WIN, BRASS, OLIVE, HOLE = 0, 1, 2, 3, 4


def classify(r: int, g: int, b: int, a: int) -> int:
    if a == 0:
        return HOLE
    m = max(r, g, b)
    if m <= 22 and r - b <= 14:
        return WIN
    if (r >= 100 and r - b >= 36) or (r >= 45 and r - b >= 0.30 * r and g <= 0.86 * r):
        return BRASS
    if r >= g and r - b >= 10 and m < 100:
        return OLIVE
    return I


def keep_solid_windows(cmap: list[int], w: int, h: int) -> None:
    """A window is a big solid dark field: a dark outline (a slot's, the art's own), a shadow under a bar or a speck is iron (a component of window pixels that is small or hollow becomes iron)."""
    seen = [False] * (w * h)
    for start in range(w * h):
        if cmap[start] != WIN or seen[start]:
            continue
        stack, comp = [start], []
        seen[start] = True
        x0 = x1 = start % w
        y0 = y1 = start // w
        while stack:
            i = stack.pop()
            comp.append(i)
            y, x = divmod(i, w)
            x0, x1, y0, y1 = min(x0, x), max(x1, x), min(y0, y), max(y1, y)
            for j, ok in ((i - 1, x > 0), (i + 1, x < w - 1), (i - w, y > 0), (i + w, y < h - 1)):
                if ok and cmap[j] == WIN and not seen[j]:
                    seen[j] = True
                    stack.append(j)
        if len(comp) < 120 or len(comp) < 0.6 * (x1 - x0 + 1) * (y1 - y0 + 1):
            for i in comp:
                cmap[i] = I


def class_map(original: pngio.Image, spec: Spec) -> list[int]:
    w, h = original.w, original.h
    out = [classify(*original.pixel(x, y)) for y in range(h) for x in range(w)]
    for x in range(w):                       # the art's own outline (its outer dark line) is not a window
        for y in (0, h - 1):
            if out[y * w + x] in (WIN, OLIVE):
                out[y * w + x] = I
    for y in range(h):
        for x in (0, w - 1):
            if out[y * w + x] in (WIN, OLIVE):
                out[y * w + x] = I

    def paint(boxes: tuple, cls: int) -> None:
        for x0, y0, x1, y1 in boxes:
            for y in range(max(0, int(math.floor(y0))), min(h, int(math.ceil(y1)))):
                for x in range(max(0, int(math.floor(x0))), min(w, int(math.ceil(x1)))):
                    if out[y * w + x] != HOLE:
                        out[y * w + x] = cls
    keep_solid_windows(out, w, h)
    paint(spec.force_w, WIN)
    paint(spec.force_i, I)
    paint(tuple(box(x, y, 3.5) for x, y in spec.rivets), BRASS)   # a rivet's dark pixels are brass under the dome
    return out


def soft_masks(cmap: list[int], w: int, h: int, size: tuple[int, int]) -> dict[int, list[float]]:
    """A smooth, sub-pixel exact mask per class at the master's size: blur once, enlarge with a cubic filter, cut steeply."""
    out = {}
    for cls in (WIN, BRASS, HOLE):
        plane = [1.0 if v == cls else 0.0 for v in cmap]
        if not any(plane):
            out[cls] = [0.0] * (size[0] * size[1])
            continue
        plane = blur121(plane, w, h)
        up = fit.resample_planes([plane], w, (0, 0, w, h), size[0], size[1], "cubic")[0]
        out[cls] = [clamp((v - 0.5) * EDGE_K + 0.5, 0.0, 1.0) for v in up]
    return out


def ring_mask(rings: tuple, w: int, h: int, kx: float, ky: float) -> list[float]:
    """The analytic brass band of the round sockets at the master's size, sub-pixel exact (the distance from the band's middle, scaled to master px)."""
    out = [0.0] * (w * h)
    k = (kx + ky) / 2
    for cx, cy, rm in rings:
        cxm, cym, rmm, half = cx * kx, cy * ky, rm * k, RING_BAND * k / 2
        reach = int(rmm + half + 3)
        for y in range(max(0, int(cym) - reach), min(h, int(cym) + reach + 1)):
            for x in range(max(0, int(cxm) - reach), min(w, int(cxm) + reach + 1)):
                c = clamp(half - abs(math.hypot(x + 0.5 - cxm, y + 0.5 - cym) - rmm) + 0.5, 0.0, 1.0)
                if c > out[y * w + x]:
                    out[y * w + x] = c
    return out


# --- the iron ------------------------------------------------------------------------------------------------------------------------

def noise_plane(w: int, h: int, cell: int, sd: float, rng: random.Random) -> list[float]:
    """Smooth noise: a coarse grid of Gaussian values, one per `cell` master px, enlarged with a cubic filter."""
    gw, gh = w // cell + 3, h // cell + 3
    grid = [rng.gauss(0.0, sd) for _ in range(gw * gh)]
    return _upsample(grid, gw, gh, cell, w, h)


def _upsample(grid: list[float], gw: int, gh: int, cell: int, w: int, h: int) -> list[float]:
    big = fit.resample_planes([grid], gw, (0, 0, gw, gh), gw * cell, gh * cell, "cubic")[0]
    bw = gw * cell
    out = []
    for y in range(h):
        out.extend(big[(y + cell) * bw + cell:(y + cell) * bw + cell + w])
    return out


def iron_plane(w: int, h: int, seed: int) -> list[float]:
    """The iron's luminance: blotches, mid grain, fine grain and a few light specks (seeded, so the same inputs give the same bytes)."""
    rng = random.Random(seed)
    blotch = noise_plane(w, h, 26, 6.5, rng)
    mid = noise_plane(w, h, 7, 3.2, rng)
    fine = [rng.gauss(0.0, 4.2) for _ in range(w * h)]
    out = [IRON_MEAN + a + b + c for a, b, c in zip(blotch, mid, fine)]
    for _ in range(int(w * h * 0.0035)):
        i = rng.randrange(w * h)
        out[i] += rng.uniform(14.0, 32.0)
        if i + 1 < w * h and rng.random() < 0.5:
            out[i + 1] += rng.uniform(6.0, 16.0)
    return out


# --- the brass -----------------------------------------------------------------------------------------------------------------------

def brass_stats(image: pngio.Image) -> tuple[list[float], float, int]:
    """The brass of a kit part: the sum of its brass pixels' colours, the sum of the squares of their detail (the pixel minus the mean of its 3 x 3 neighbours, in luminance) and the count."""
    total, detail, n = [0.0, 0.0, 0.0], 0.0, 0
    for y in range(1, image.h - 1):
        for x in range(1, image.w - 1):
            r, g, b, a = image.pixel(x, y)
            if a != 255 or r - b < 30 or r < 60:
                continue
            around = [image.pixel(x + dx, y + dy) for dx in (-1, 0, 1) for dy in (-1, 0, 1) if dx or dy]
            if any(p[3] != 255 or p[0] - p[2] < 30 for p in around):
                continue
            mean = sum(lum(*p[:3]) for p in around) / 8
            detail += (lum(r, g, b) - mean) ** 2
            for k, v in enumerate((r, g, b)):
                total[k] += v
            n += 1
    return total, detail, n


def brass_material(masters: Path) -> dict:
    """The approved brass: its mean colour (the pooled mean of the HUD socket ring #3259, the HUD clock frame #3259 and the menu message window frame #3260) and the sd of its fine grain (the detail of a pixel against its neighbours)."""
    total, detail, n, hashes = [0.0, 0.0, 0.0], 0.0, 0, {}
    for part in BRASS_SOURCES:
        path = masters / f"{part}.png"
        if not path.is_file():
            raise KitError(f"{path} is missing: the approved brass of the kit (#3259, #3260) the tone and the grain are taken from")
        t, d, c = brass_stats(pngio.read(path))
        if c < 500:
            raise KitError(f"{part}: too little brass found ({c} px) to take the tone from")
        total = [a + b for a, b in zip(total, t)]
        detail += d
        n += c
        hashes[part] = sha256_file(path)
    return {"tone": [t / n for t in total], "grain_sd": math.sqrt(detail / n) * 0.9, "sha256": hashes}


def brass_colour(light: float, copper: float, spec: float, tone: list[float]) -> list[float]:
    if light < 1:
        c = [BRASS_DARK[k] + (tone[k] - BRASS_DARK[k]) * light for k in range(3)]
    else:
        t = clamp((light - 1) / 1.1, 0.0, 1.0)
        c = [tone[k] + (BRASS_HIGH[k] - tone[k]) * t for k in range(3)]
    if copper > 0:
        w = clamp(copper, 0.0, 1.0) * 0.8
        c = [c[k] * (1 - w) + COPPER[k] * w * clamp(0.6 + 0.5 * light, 0.0, 1.1) for k in range(3)]
    if spec > 0:
        s = clamp(spec, 0.0, 1.0)
        c = [c[k] + (BRASS_HIGH[k] + 14 - c[k]) * s for k in range(3)]
    return c


def shade_brass(mask: list[float], w: int, h: int, brass: dict, seed: int) -> tuple[list, list, list]:
    """Colour planes (r, g, b) of the brass where the mask is on: height from the distance to the edge, a short bevel, light from the top left, copper bounce, sharp highlight, the approved grain."""
    hard = [m >= 0.5 for m in mask]
    d_in = edt(hard, w, h)
    d_out = edt([not v for v in hard], w, h)
    sd = [(di if hv else -do) for di, do, hv in zip(d_in, d_out, hard)]
    sd = box_blur(sd, w, h, 1)
    ln = math.sqrt(sum(c * c for c in LIGHT))
    lx, ly, lz = (c / ln for c in LIGHT)
    lxy_n = math.hypot(lx, ly)
    lxy = (lx / lxy_n, ly / lxy_n)           # the light's direction in the plane: an outward normal that points at it faces the light
    rng = random.Random(seed)
    sheen = noise_plane(w, h, 70, 0.05, rng)
    grain = blur121([rng.gauss(0.0, 1.0) for _ in range(w * h)], w, h)         # fine metal grain: seeded noise correlated over a pixel, the approved brass's sd
    pits = {rng.randrange(w * h) for _ in range(int(w * h * 0.002))}            # cast brass has a few dark pits
    tone = brass["tone"]
    grain_k = GRAIN_SHARE * brass["grain_sd"] / GRAIN_DETAIL
    tone_l = lum(*tone)
    r_plane = [0.0] * (w * h)
    g_plane = [0.0] * (w * h)
    b_plane = [0.0] * (w * h)
    for y in range(h):
        base = y * w
        up, down = max(y - 1, 0) * w, min(y + 1, h - 1) * w
        for x in range(w):
            i = base + x
            if mask[i] <= 0.002:
                continue
            gx = (sd[base + min(x + 1, w - 1)] - sd[base + max(x - 1, 0)]) * 0.5
            gy = (sd[down + x] - sd[up + x]) * 0.5
            gl = math.hypot(gx, gy)
            s = sd[i]
            tilt = (1.0 - clamp(s / BEVEL, 0.0, 1.0)) ** 1.25 * (math.pi / 2) if s < BEVEL else 0.0
            sp = cu = 0.0
            if gl > 1e-6 and tilt > 0:
                ux, uy = -gx / gl, -gy / gl          # outward
                st, ct = math.sin(tilt), math.cos(tilt)
                nx, ny, nz = ux * st, uy * st, ct
                facing = ux * lxy[0] + uy * lxy[1]    # 1 on an edge that faces the light (top left), -1 on the opposite edge
                sp = math.exp(-((math.degrees(tilt) - 24.0) / 7.0) ** 2) * max(0.0, facing) ** 1.2          # the sharp highlight ridge
                cu = math.exp(-((math.degrees(tilt) - 45.0) / 22.0) ** 2) * max(0.0, -facing) ** 1.5 * 0.9    # the warm bounce on the far edge
            else:
                nx = ny = 0.0
                nz = 1.0
            lam = max(0.0, nx * lx + ny * ly + nz * lz) / lz
            light = 0.30 + 0.78 * lam
            if 0 < tilt:
                light *= 1.0 + 0.16 * math.tanh(3.0 * math.sin(tilt * 4.0 + 0.6))       # reflection bands across the bevel (sharp, not a gradient)
            light *= 1.0 + sheen[i]
            light *= 1.0 + 0.07 * math.tanh(3.0 * math.sin((x * 0.62 + y * 0.78) * 0.0095 + 0.9))     # a few sharp-edged, low reflections across a wide plate (polished, not a gradient)
            gdev = grain[i] * grain_k - (14.0 if i in pits else 0.0)
            c = brass_colour(light, cu * 0.9, sp, tone)
            k = 1.0 + gdev / tone_l
            r_plane[i], g_plane[i], b_plane[i] = c[0] * k, c[1] * k, c[2] * k
    return r_plane, g_plane, b_plane


def rivet_dome(planes: tuple, cx: float, cy: float, radius: float, w: int, h: int, tone: list[float]) -> None:
    """A rivet: a lit dome in a dark seat ring, over the brass."""
    r_p, g_p, b_p = planes
    ln = math.sqrt(sum(c * c for c in LIGHT))
    lx, ly, lz = (c / ln for c in LIGHT)
    hn = math.sqrt(lx * lx + ly * ly + (lz + 1) ** 2)
    hx, hy, hz = lx / hn, ly / hn, (lz + 1) / hn
    reach = radius + 3.0
    for y in range(max(0, int(cy - reach)), min(h, int(cy + reach) + 1)):
        for x in range(max(0, int(cx - reach)), min(w, int(cx + reach) + 1)):
            dist = math.hypot(x + 0.5 - cx, y + 0.5 - cy)
            i = y * w + x
            if dist < radius + 1.5:
                rho = min(dist / radius, 1.0)
                nz = math.sqrt(max(0.0, 1 - rho * rho))
                nx, ny = (x + 0.5 - cx) / radius, (y + 0.5 - cy) / radius
                lam = max(0.0, nx * lx + ny * ly + nz * lz) / lz
                sp = max(0.0, nx * hx + ny * hy + nz * hz) ** 30
                light = 0.22 + 0.62 * lam
                c = brass_colour(min(light, 1.15), max(0.0, nx * 0.6 + ny * 0.6) ** 2 * 0.5, sp * 0.35, tone)
                cover = clamp(radius + 0.5 - dist, 0.0, 1.0)
                for plane, v in zip((r_p, g_p, b_p), c):
                    plane[i] = plane[i] * (1 - cover) + v * cover
            elif dist < radius + 3.0:                      # the seat: a dark ring
                t = 1.0 - (dist - radius - 1.5) / 1.5
                k = 1.0 - 0.45 * clamp(t, 0.0, 1.0)
                for plane in (r_p, g_p, b_p):
                    plane[i] *= k


# --- rendering one art ---------------------------------------------------------------------------------------------------------------

def well(lum_plane: list[float], w: int, h: int, rect: tuple[float, float, float, float], depth: float = 1.0) -> None:
    """A recessed slot by rule in the iron's luminance: darker inside, a dark shadow along the top and the left, a light line along the bottom and the right, a thin dark outline."""
    x0, y0, x1, y1 = rect
    for y in range(max(0, int(y0) - 2), min(h, int(math.ceil(y1)) + 2)):
        for x in range(max(0, int(x0) - 2), min(w, int(math.ceil(x1)) + 2)):
            cx, cy = x + 0.5, y + 0.5
            inside = clamp(min(cx - x0, x1 - cx, cy - y0, y1 - cy) + 0.5, 0.0, 1.0)
            if inside <= 0:
                continue
            i = y * w + x
            dt, dl, db, dr = cy - y0, cx - x0, y1 - cy, x1 - cx
            v = lum_plane[i] * (1 - 0.30 * depth)
            shadow = max(math.exp(-dt / 3.4), math.exp(-dl / 3.4)) * 0.45 * depth
            light = max(math.exp(-db / 1.6), math.exp(-dr / 1.6)) * 20.0 * depth
            outline = math.exp(-min(dt, dl, db, dr) / 1.1) * 0.35
            v = v * (1 - shadow) * (1 - outline) + light
            lum_plane[i] = lum_plane[i] * (1 - inside) + v * inside


def relief(original: pngio.Image, size: tuple[int, int]) -> list[float]:
    """The original's strong relief (a puzzle piece, a sign) at the master's size: its luminance minus its local mean, small speckle cut away, enlarged with a cubic filter."""
    ow, oh = original.w, original.h
    plane = [lum(*original.pixel(x, y)[:3]) for y in range(oh) for x in range(ow)]
    low = box_blur(plane, ow, oh, 4)
    detail = []
    for a, b in zip(plane, low):
        d = a - b
        detail.append(math.copysign(max(0.0, abs(d) - 9.0), d))
    return list(fit.resample_planes([detail], ow, (0, 0, ow, oh), size[0], size[1], "cubic")[0])


def emboss(lum_plane: list[float], up: list[float], original: pngio.Image, rect: tuple, size: tuple[int, int]) -> None:
    """The relief inside a box, added to the new iron (feathered 6 master px at the box's edge)."""
    kx, ky = size[0] / original.w, size[1] / original.h
    x0, y0, x1, y1 = rect
    for y in range(max(0, int(y0 * ky)), min(size[1], int(y1 * ky))):
        for x in range(max(0, int(x0 * kx)), min(size[0], int(x1 * kx))):
            edge = min(x - x0 * kx, x1 * kx - x, y - y0 * ky, y1 * ky - y)
            lum_plane[y * size[0] + x] += up[y * size[0] + x] * clamp(edge / 6.0, 0.0, 1.0)


def hole_edge(lum_plane: list[float], hole: list[float], w: int, h: int) -> None:
    """The thin dark line the original draws round a transparent opening, on the iron beside it."""
    if not any(m > 0.5 for m in hole):
        return
    soft = box_blur(hole, w, h, 3)
    for i, v in enumerate(soft):
        if hole[i] < 0.5 and v > 0.02:
            lum_plane[i] *= 1.0 - 0.75 * clamp(v * 2.4, 0.0, 1.0)


def bevel(lum_plane: list[float], w: int, h: int) -> None:
    """The art's outer bevel: a light line at the top left, a dark one at the bottom right."""
    for y in range(h):
        for x in range(w):
            i = y * w + x
            dtl = min(x, y)
            dbr = min(w - 1 - x, h - 1 - y)
            v = lum_plane[i]
            if dtl < 5:
                v += 34.0 * (1 - dtl / 5.0) ** 1.5
            if dbr < 5:
                v -= 20.0 * (1 - dbr / 5.0) ** 1.5
            lum_plane[i] = max(v, IRON_FLOOR)


def copy_region(canvas: tuple, original: pngio.Image, shape: tuple, size: tuple[int, int]) -> None:
    """The original's pixels of one spot, enlarged with a cubic filter, inside the shape's own soft mask."""
    kx, ky = size[0] / original.w, size[1] / original.h
    if shape[0] == "ellipse":
        _, cx, cy, rx, ry = shape
        bbox = (cx - rx - 1, cy - ry - 1, cx + rx + 1, cy + ry + 1)
    else:
        _, x0, y0, x1, y1 = shape
        bbox = (x0, y0, x1, y1)
    ox0, oy0 = max(0, int(math.floor(bbox[0]))), max(0, int(math.floor(bbox[1])))
    ox1, oy1 = min(original.w, int(math.ceil(bbox[2]))), min(original.h, int(math.ceil(bbox[3])))
    if ox1 <= ox0 or oy1 <= oy0:
        raise KitError(f"a copied shape {shape} lies outside the art")
    mx0, my0 = int(round(ox0 * kx)), int(round(oy0 * ky))
    mx1, my1 = int(round(ox1 * kx)), int(round(oy1 * ky))
    sub = fit.resample_image(original, (ox0, oy0, ox1 - ox0, oy1 - oy0), mx1 - mx0, my1 - my0)
    r_p, g_p, b_p = canvas
    w = size[0]
    for y in range(sub.h):
        for x in range(sub.w):
            px = sub.pixel(x, y)
            ox, oy = ox0 + (x + 0.5) * (ox1 - ox0) / sub.w, oy0 + (y + 0.5) * (oy1 - oy0) / sub.h
            if shape[0] == "ellipse":
                e = math.hypot((ox - cx) / rx, (oy - cy) / ry)
                cover = clamp((1.0 - e) * min(rx, ry) * ((kx + ky) / 2) + 0.5, 0.0, 1.0)
            else:
                cover = 1.0
            cover *= px[3] / 255.0
            if cover <= 0:
                continue
            i = (my0 + y) * w + (mx0 + x)
            for plane, v in zip((r_p, g_p, b_p), px[:3]):
                plane[i] = plane[i] * (1 - cover) + v * cover


def window_colour(original: pngio.Image, cmap: list[int]) -> tuple[float, float, float]:
    total, n = [0.0, 0.0, 0.0], 0
    for i, c in enumerate(cmap):
        if c == WIN:
            p = original.data[i * 4:i * 4 + 3]
            for k in range(3):
                total[k] += p[k]
            n += 1
    if not n:
        return (2.0, 2.0, 8.0)
    return tuple(t / n for t in total)  # type: ignore[return-value]


def render(spec: Spec, original: pngio.Image, size: tuple[int, int], brass: dict) -> tuple[pngio.Image, dict]:
    w, h = size
    kx, ky = w / original.w, h / original.h
    cmap = class_map(original, spec)
    masks = soft_masks(cmap, original.w, original.h, size)
    seed = int(spec.art) * 7919 + 17
    lum_plane = iron_plane(w, h, seed)
    for rect in spec.wells:
        well(lum_plane, w, h, (rect[0] * kx, rect[1] * ky, rect[2] * kx, rect[3] * ky))
    for x0, x1, y0, pitch, count, height in spec.rows:
        for k in range(count):
            well(lum_plane, w, h, (x0 * kx, (y0 + k * pitch) * ky, x1 * kx, (y0 + k * pitch + height) * ky), 1.15)
    if spec.emboss:
        up = relief(original, size)
        for box_ in spec.emboss:
            emboss(lum_plane, up, original, box_, size)
    hole_edge(lum_plane, masks[HOLE], w, h)
    bevel(lum_plane, w, h)
    r_p = list(lum_plane)
    g_p = [v * 0.985 for v in lum_plane]
    b_p = [v * 0.99 for v in lum_plane]
    canvas = (r_p, g_p, b_p)
    # the shadow of the brass on the iron: shifted to the bottom right, blurred
    bm = masks[BRASS]
    if spec.rings:
        bm = [max(a, b) for a, b in zip(bm, ring_mask(spec.rings, w, h, kx, ky))]
    if any(bm):
        shift_x, shift_y = int(round(1.1 * kx)), int(round(1.4 * ky))
        shifted = [0.0] * (w * h)
        for y in range(h - shift_y):
            shifted[(y + shift_y) * w + shift_x:(y + shift_y) * w + w] = bm[y * w:y * w + w - shift_x]
        soft = box_blur(shifted, w, h, 3)
        for i, s in enumerate(soft):
            if s > 0.004:
                k = 1.0 - 0.38 * clamp(s, 0.0, 1.0)
                r_p[i] *= k
                g_p[i] *= k
                b_p[i] *= k
    win = window_colour(original, cmap)
    for i, m in enumerate(masks[WIN]):
        if m > 0.0:
            r_p[i] = r_p[i] * (1 - m) + win[0] * m
            g_p[i] = g_p[i] * (1 - m) + win[1] * m
            b_p[i] = b_p[i] * (1 - m) + win[2] * m
    for x0, y0, x1, y1, colour in spec.fills:
        for y in range(int(y0 * ky), int(y1 * ky)):
            for x in range(int(x0 * kx), int(x1 * kx)):
                i = y * w + x
                r_p[i], g_p[i], b_p[i] = colour
    if any(bm):
        br, bg, bb = shade_brass(bm, w, h, brass, seed + 1)
        for i, m in enumerate(bm):
            if m > 0.002:
                r_p[i] = r_p[i] * (1 - m) + br[i] * m
                g_p[i] = g_p[i] * (1 - m) + bg[i] * m
                b_p[i] = b_p[i] * (1 - m) + bb[i] * m
    for cx, cy in spec.rivets:
        rivet_dome(canvas, cx * kx, cy * ky, 3.1 * (kx + ky) / 2, w, h, brass["tone"])
    for shape in spec.copies:
        copy_region(canvas, original, shape, size)
    holes = masks[HOLE]
    out = bytearray(w * h * 4)
    out[0::4] = bytes(int(clamp(v + 0.5, 0, 255)) for v in r_p)
    out[1::4] = bytes(int(clamp(v + 0.5, 0, 255)) for v in g_p)
    out[2::4] = bytes(int(clamp(v + 0.5, 0, 255)) for v in b_p)
    alpha = [clamp(1.0 - m, 0.0, 1.0) for m in holes]
    out[3::4] = bytes(0 if a <= 0.02 else 255 if a >= 0.98 else int(a * 255 + 0.5) for a in alpha)
    for i in range(w * h):
        if out[i * 4 + 3] == 0:
            out[i * 4:i * 4 + 3] = b"\x00\x00\x00"
    image = pngio.Image(w, h, out)
    if any(0 < a < 255 for a in image.data[3::4]):
        fit.decontaminate(image, 250, fit.EDGE_RADIUS)          # an edge pixel takes the colour of the plate beside it (SWI-KIT-4: no halo)
    return image, {"window_fill": [round(c, 1) for c in win], "brass_pixels": sum(1 for m in bm if m >= 0.5), "iron_seed": seed}


# --- pack-first ---------------------------------------------------------------------------------------------------------------------

PACK_SCALE = 4                # the old pack's frames are 4 times the original's size


def hole_mask(original: pngio.Image, size: tuple[int, int]) -> list[float]:
    """The original's transparent places as a smooth, sub-pixel exact mask at the master's size (the analytic alpha): blurred once, enlarged with a cubic filter, cut steeply."""
    plane = blur121([1.0 if original.data[i * 4 + 3] == 0 else 0.0 for i in range(original.w * original.h)], original.w, original.h)
    up = fit.resample_planes([plane], original.w, (0, 0, original.w, original.h), size[0], size[1], "cubic")[0]
    return [clamp((v - 0.5) * EDGE_K + 0.5, 0.0, 1.0) for v in up]


FIT_TOLERANCE = 3         # original px: how far the pack frame's biggest brass frame may lie from the original's (the game draws its text in the original's window)


def biggest_brass_box(brass: list[bool], w: int, h: int) -> tuple[int, int, int, int]:
    """The bounding box (x0, y0, x1, y1) of the biggest 4-connected brass component and of every component at least a quarter of its size (a frame the shading splits in two is one frame)."""
    seen = [False] * (w * h)
    found: list[tuple[int, tuple[int, int, int, int]]] = []
    for start in range(w * h):
        if not brass[start] or seen[start]:
            continue
        stack, n = [start], 0
        seen[start] = True
        x0 = x1 = start % w
        y0 = y1 = start // w
        while stack:
            i = stack.pop()
            n += 1
            y, x = divmod(i, w)
            x0, x1, y0, y1 = min(x0, x), max(x1, x), min(y0, y), max(y1, y)
            for j, ok in ((i - 1, x > 0), (i + 1, x < w - 1), (i - w, y > 0), (i + w, y < h - 1)):
                if ok and brass[j] and not seen[j]:
                    seen[j] = True
                    stack.append(j)
        found.append((n, (x0, y0, x1, y1)))
    top = max(n for n, _ in found)
    boxes = [box_ for n, box_ in found if n * 4 >= top]
    return (min(b[0] for b in boxes), min(b[1] for b in boxes), max(b[2] for b in boxes), max(b[3] for b in boxes))


def check_pack_fit(spec: Spec, frame: pngio.Image, original: pngio.Image) -> None:
    """A pack frame is a drop-in only if its biggest brass frame lies where the original's does (within FIT_TOLERANCE original px on every side): the screens draw text and slots in the original's rectangles."""
    ow, oh = original.w, original.h
    mine = [classify(*original.pixel(x, y)) == BRASS for y in range(oh) for x in range(ow)]
    if not any(mine):
        return                                  # an art with no brass frame (list rows, plain plates): nothing to compare
    theirs = [classify(*frame.pixel(x * PACK_SCALE + PACK_SCALE // 2, y * PACK_SCALE + PACK_SCALE // 2)) == BRASS for y in range(oh) for x in range(ow)]
    a, b = biggest_brass_box(mine, ow, oh), biggest_brass_box(theirs, ow, oh)
    if any(abs(p - q) > FIT_TOLERANCE for p, q in zip(a, b)):
        raise KitError(f"{spec.ident}: the pack frame's brass frame {b} is not where the original's {a} is (more than {FIT_TOLERANCE} px): not a drop-in, use the rule build")


def pack_master(spec: Spec, frame: pngio.Image, original: pngio.Image, size: tuple[int, int]) -> tuple[pngio.Image, dict]:
    """Pack-first: the old HD pack's frame of the art at the master's size (a premultiplied, linear-light resample, so what is transparent in the pack bleeds no colour), a stand-in baked into the frame
    painted out with the window's colour, and the ALPHA analytic: the original's transparent places, enlarged smoothly (the pack's own alpha is not used)."""
    if abs(frame.w - original.w * PACK_SCALE) > 1 or abs(frame.h - original.h * PACK_SCALE) > 1:
        raise KitError(f"{spec.ident}: the pack frame is {frame.w}x{frame.h}, not {PACK_SCALE} times the original's {original.w}x{original.h}")
    frame = pngio.Image(frame.w, frame.h, bytearray(frame.data))        # the caller's frame stays as it was
    check_pack_fit(spec, frame, original)
    w, h = size
    for x0, y0, x1, y1 in spec.pack_clear:
        sx, sy = frame.w / original.w, frame.h / original.h
        probe = frame.pixel(int((x0 + x1) / 2 * sx), max(0, int((y0 - 12) * sy)))      # the window's dark fill just above the stand-in's box
        if probe[3] != 255 or max(probe[:3]) > 40:
            raise KitError(f"{spec.ident}: the pack frame has no dark window fill above the stand-in's box")
        for y in range(int(y0 * sy), int(y1 * sy)):
            for x in range(int(x0 * sx), int(x1 * sx)):
                frame.data[(y * frame.w + x) * 4:(y * frame.w + x) * 4 + 4] = bytes(probe)
    for y in range(frame.h):                                  # what the original leaves open is open in the pack frame too before the resample: whatever colour the pack keeps there bleeds nothing
        oy = min(original.h - 1, y * original.h // frame.h)
        for x in range(frame.w):
            if original.data[(oy * original.w + min(original.w - 1, x * original.w // frame.w)) * 4 + 3] == 0:
                frame.data[(y * frame.w + x) * 4 + 3] = 0
    image = fit.resample_image(frame, (0, 0, frame.w, frame.h), w, h)
    hole = hole_mask(original, size)
    alpha = bytearray(w * h)
    repair = bytearray(image.data[3::4])                      # the alpha the colour repair sees: where the resample had little pack colour (beside a hole) it is a band pixel even though the final alpha is opaque
    for i, m in enumerate(hole):
        a = 1.0 - m
        alpha[i] = 0 if a <= 0.02 else 255 if a >= 0.98 else int(a * 255 + 0.5)
        if alpha[i] == 0:
            repair[i] = 0
            image.data[i * 4:i * 4 + 3] = b"\x00\x00\x00"
        elif alpha[i] < 255:
            repair[i] = max(1, min(repair[i], alpha[i]))           # the edge band the gate measures
        elif repair[i] < 250:
            repair[i] = max(1, repair[i])
        else:
            repair[i] = 255
    if any(0 < a < 255 for a in repair):
        image.data[3::4] = repair
        fit.decontaminate(image, 250, fit.EDGE_RADIUS)
    image.data[3::4] = alpha
    return image, {"pack_frame": f"{spec.pack}-f000", "pack_frame_size": [frame.w, frame.h], "pack_scale": PACK_SCALE, "alpha": "analytic (the original's transparent places, enlarged)",
                   "stand_in_cleared": [list(b) for b in spec.pack_clear]}


# --- the gates and the records ---------------------------------------------------------------------------------------------------------

def gates(spec: Spec, image: pngio.Image, size: tuple[int, int], original: pngio.Image, rule_build: bool = True) -> list[str]:
    """Size on the manifest's and the 6 px grid; the transparent places are the original's (at most 2 % of the pixels differ from the original's holes, enlarged as blocks); at most 1.2 % translucent
    pixels; no magenta fringe and, in a rule build, no black border pixel (an unshaded edge; a pack frame has its own dark outline); the edge halo gate of `fit.py` on the translucent band."""
    failures = []
    w, h = image.w, image.h
    if (w, h) != size:
        failures.append(f"size {w}x{h} is not the manifest's {size[0]}x{size[1]}")
    if size[0] % GRID or size[1] % GRID:
        failures.append(f"size {size[0]}x{size[1]} is not on the {GRID} px grid")
    mismatch = 0
    for y in range(h):
        oy = min(original.h - 1, y * original.h // h)
        for x in range(w):
            hole = original.pixel(min(original.w - 1, x * original.w // w), oy)[3] == 0
            if hole != (image.data[(y * w + x) * 4 + 3] == 0):
                mismatch += 1
    if mismatch > 0.02 * w * h:
        failures.append(f"{mismatch} pixels where the transparent places differ from the original's (more than 2 %)")
    hole_share = sum(1 for p in range(original.w * original.h) if original.data[p * 4 + 3] == 0)
    partial = sum(1 for a in image.data[3::4] if 0 < a < 255)
    if partial > 0.012 * w * h:
        failures.append(f"{partial} translucent pixels (more than 1.2 % of the art): the plate must be opaque")
    magenta = sum(1 for i in range(0, w * h * 4, 4) if image.data[i + 3] and image.data[i] > 170 and image.data[i + 2] > 170 and image.data[i + 1] < 90)
    if magenta:
        failures.append(f"{magenta} magenta pixels (a keyed fringe)")
    border = [(x, y) for y in (0, h - 1) for x in range(w)] + [(x, y) for x in (0, w - 1) for y in range(1, h - 1)]
    black = sum(1 for x, y in border if image.pixel(x, y)[3] == 255 and sum(image.pixel(x, y)[:3]) < 12) if rule_build else 0
    if black:
        failures.append(f"{black} black pixels on the art's border (an edge that was not shaded)")
    if hole_share:
        halo = fit.edge_halo(image)
        if halo["mean_de"] > fit.HALO_MEAN_DE or halo["p95_de"] > fit.HALO_P95_DE:
            failures.append(f"edge halo mean {halo['mean_de']:.1f} p95 {halo['p95_de']:.1f} over the limits")
    return failures


def provenance_record(spec: Spec, frames: Path, extra: dict, size: tuple[int, int], blob: bytes, brass: dict | None, pack_file: Path | None) -> dict:
    src = frames / f"{spec.art}-{spec.name}" / "f0.png"
    record = {"part": spec.ident, "rule": "popup_kit", "how": "pack-first" if pack_file else "rule", "kind": spec.kind, "tool_version": TOOL_VERSION, "date": datetime.date.today().isoformat(),
              "source": f"{spec.art}-{spec.name}/f0", "sources_sha256": {f"{spec.art}-{spec.name}/f0": sha256_file(src)}, **extra, "image_size": list(size), "image_sha256": hashlib.sha256(blob).hexdigest(), "run_by": "agent"}
    if pack_file:
        record["sources_sha256"][f"pack:{spec.pack}-f000"] = sha256_file(pack_file)
    if brass:
        record.update({"brass_sources": list(BRASS_SOURCES), "brass_sha256": brass["sha256"], "brass_tone": [round(t, 1) for t in brass["tone"]], "grain_sd": round(brass["grain_sd"], 2)})
    return record


def chosen(text: str | None) -> set[str]:
    """The ids of `--only` (all parts when empty); an id that is not a part of the batch is an error."""
    only = {x for x in (text or "").split(",") if x}
    unknown = sorted(only - set(BY_ID))
    if unknown:
        raise KitError(f"not parts of this batch: {', '.join(unknown)}")
    return only


def cmd_plan() -> int:
    manifest = load_manifest()
    rows = {r["id"]: r for r in manifest["parts"]}
    for spec in SPECS:
        row = rows.get(spec.ident)
        print(f"{spec.ident:34} art {spec.art:>3} {spec.kind:6} {'x'.join(map(str, row['size'])) if row and row['size'] else 'open' if row else 'NO ROW'}")
    missing = [s.ident for s in SPECS if s.ident not in rows]
    print(f"POPUP KIT plan: {len(SPECS)} parts" + (f", rows missing: {missing}" if missing else ""))
    return 1 if missing else 0


def default_pack() -> Path:
    return Path.home() / "Library" / "Application Support" / "Arcanum Steamwoven Game" / "hd-packs" / "interface-redraw-4x"


def cmd_build(args: argparse.Namespace) -> int:
    """The masters: pack-first where the art has a good frame in the old pack, the rule build where not. `--alt-dir` also writes the rule build of every pack-first art there (the composite's alternative)."""
    local = args.local_root
    frames = args.frames or local / "census" / "frames"
    masters = local / "masters"
    pack_root = args.pack or default_pack()
    if args.alt_dir and args.alt_dir.resolve() == masters.resolve():
        raise KitError("--alt-dir is the masters folder: the alternative would overwrite the kit masters")
    manifest = load_manifest()
    only = chosen(args.only)
    brass = None
    done = 0
    for spec in SPECS:
        if only and spec.ident not in only:
            continue
        size = master_of(spec.ident, manifest)
        original = read_frame(frames, spec)
        pack_file = pack_root / "interface" / f"{spec.pack}-f000.png" if spec.pack else None
        if pack_file is not None and not pack_file.is_file():
            raise KitError(f"{pack_file} is missing (the old HD pack, --pack)")
        variants = []                                    # (image, extra, brass used, pack file, folder)
        if pack_file is not None:
            image, extra = pack_master(spec, pngio.read(pack_file), original, size)
            variants.append((image, extra, None, pack_file, masters))
        if pack_file is None or args.alt_dir:
            brass = brass or brass_material(masters)
            image, extra = render(spec, original, size, brass)
            variants.append((image, extra, brass, None, masters if pack_file is None else args.alt_dir))
        for image, extra, used, source, folder in variants:
            failures = gates(spec, image, size, original, rule_build=source is None)
            if failures and folder != masters:                 # the alternative is not a kit part: its failure is a warning
                print(f"warning: {spec.ident}: the rule alternative is not written: " + "; ".join(failures))
                continue
            if failures:
                raise GateError(f"{spec.ident}: " + "; ".join(failures))
            blob = pngio.encode(image)
            print(f"{spec.ident}: {size[0]}x{size[1]} ({'pack-first' if source else 'rule'}) {hashlib.sha256(blob).hexdigest()[:12]}" + ("" if folder == masters else f" -> {folder}"))
            if not args.dry_run:
                folder.mkdir(parents=True, exist_ok=True)
                (folder / f"{spec.ident}.png").write_bytes(blob)
                if folder == masters:
                    (local / "provenance" / "rule").mkdir(parents=True, exist_ok=True)
                    record = provenance_record(spec, frames, extra, size, blob, used, source)
                    (local / "provenance" / "rule" / f"{spec.ident}.json").write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
        done += 1
    print(f"POPUP KIT build PASS: {done} part(s)" + (" (dry run, nothing written)" if args.dry_run else ""))
    return 0


# --- the composite -------------------------------------------------------------------------------------------------------------------

def run(cmd: list[str]) -> None:
    try:
        subprocess.run(cmd, check=True, capture_output=True)
    except (subprocess.CalledProcessError, OSError) as exc:
        detail = exc.stderr.decode("utf-8", "replace")[:300] if isinstance(exc, subprocess.CalledProcessError) and exc.stderr else str(exc)
        raise KitError(f"{cmd[0]} failed: {detail}") from exc


def cmd_sheet(args: argparse.Namespace) -> int:
    """The before / after composite: per art the ORIGINAL (its own pixels, nearest) above the KIT master, both at the same width, transparent places on a magenta ground."""
    local = args.local_root
    frames = args.frames or local / "census" / "frames"
    masters = local / "masters"
    if shutil.which("magick") is None:
        raise KitError("ImageMagick (magick) is needed for the composite")
    only = chosen(args.only)
    if args.zoom <= 0:
        raise KitError("--zoom must be above 0")
    if not Path(LABEL_FONT).is_file():
        raise KitError(f"the label font {LABEL_FONT} is missing")
    work = Path(tempfile.mkdtemp(prefix="popup-sheet-"))
    try:
        cells = []
        for spec in SPECS:
            if only and spec.ident not in only:
                continue
            master = masters / f"{spec.ident}.png"
            if not master.is_file():
                raise KitError(f"{master} is missing: run `build` first")
            original = work / f"{spec.art}-o.png"
            pngio.write(original, read_frame(frames, spec))
            width = int(round(read_frame(frames, spec).w * args.zoom))
            cell = work / f"{spec.art}-cell.png"
            pieces = [("ORIGINAL", original, "point"), ("KIT MASTER, " + ("pack-first: pack frame + analytic alpha" if spec.pack else "rule build (no good pack frame)"), master, "Lanczos")]
            alt = args.alt_dir / f"{spec.ident}.png" if args.alt_dir else None
            if alt is not None and alt.is_file() and spec.pack:
                pieces.append(("ALTERNATIVE, rule build (not in the kit)", alt, "Lanczos"))
            labelled = []
            for k, (caption, src, flt) in enumerate(pieces):
                part = work / f"{spec.art}-{k}.png"
                run(["magick", str(src), "-background", "#7a3a8a", "-alpha", "remove", "-filter", flt, "-resize", f"{width}x", "-gravity", "NorthWest", "-font", LABEL_FONT, "-fill", "white",
                     "-pointsize", "14", "-undercolor", "#000000b0", "-annotate", "+4+3", f"{spec.art} {spec.name}  {caption}", str(part)])
                labelled.append(str(part))
            run(["magick", *labelled, "-background", "#202020", "-splice", "0x4", "-append", str(cell)])
            cells.append(cell)
        run(["magick", *[str(c) for c in cells], "-background", "#202020", "-splice", "0x10", "-append", str(work / "sheet.png")])
        title = work / "title.png"
        run(["magick", "-size", "1500x36", "xc:#202020", "-font", LABEL_FONT, "-fill", "white", "-pointsize", "19", "-gravity", "west", "-annotate", "+8+0",
             "ARC-UI-3278, B-HARDWARE slice 4: ORIGINAL | KIT MASTER | ALTERNATIVE per art, top to bottom (transparent places magenta)", str(title)])
        run(["magick", str(title), str(work / "sheet.png"), "-background", "#202020", "-append", str(args.out)])
    finally:
        shutil.rmtree(work, ignore_errors=True)
    print(f"POPUP KIT sheet PASS: {args.out}")
    return 0


# --- self-test -----------------------------------------------------------------------------------------------------------------------

def self_test() -> int:
    original = pngio.Image.blank(40, 24, (60, 60, 60, 255))
    for y in range(4, 20):                   # a brass frame round a dark window, and a hole
        for x in range(4, 36):
            original.data[(y * 40 + x) * 4:(y * 40 + x) * 4 + 4] = bytes((180, 140, 50, 255))
    for y in range(8, 16):
        for x in range(8, 32):
            original.data[(y * 40 + x) * 4:(y * 40 + x) * 4 + 4] = bytes((0, 0, 6, 255))
    for y in range(0, 3):
        for x in range(0, 6):
            original.data[(y * 40 + x) * 4 + 3] = 0
    spec = Spec("hardware.test", "0", "test", "rot", wells=((10, 5, 14, 7),))
    brass = {"tone": [150.0, 110.0, 40.0], "grain_sd": 3.0, "sha256": {}}
    image, _ = render(spec, original, (144, 84), brass)
    assert (image.w, image.h) == (144, 84)
    assert image.pixel(1, 1)[3] == 0, "the hole is transparent"
    assert image.pixel(72, 42)[3] == 255 and image.pixel(72, 42)[0] < 12, "the window keeps its dark fill"
    r, g, b, _ = image.pixel(72, 20)
    assert r > g > b and r > 100, f"the frame is brass: {(r, g, b)}"
    assert not gates(spec, image, (144, 84), original), "the gates hold"
    assert render(spec, original, (144, 84), brass)[0].data == image.data, "the same inputs give the same bytes"
    assert [round(v) for v in edt([False, True, True, True, False], 5, 1)] == [0, 1, 2, 1, 0]
    print("POPUP KIT self-test PASS")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = parser.add_subparsers(dest="cmd", required=True)
    for name in ("plan", "build", "sheet", "self-test"):
        p = sub.add_parser(name)
        if name in ("build", "sheet"):
            p.add_argument("--only")
            p.add_argument("--frames", type=Path)
            p.add_argument("--local-root", type=Path, default=fit.default_local_root())
        if name == "build":
            p.add_argument("--dry-run", action="store_true")
            p.add_argument("--pack", type=Path)
            p.add_argument("--alt-dir", type=Path)
        if name == "sheet":
            p.add_argument("--out", type=Path, required=True)
            p.add_argument("--alt-dir", type=Path)
            p.add_argument("--zoom", type=float, default=2.0)
    args = parser.parse_args(argv)
    try:
        if args.cmd == "plan":
            return cmd_plan()
        if args.cmd == "build":
            return cmd_build(args)
        if args.cmd == "sheet":
            return cmd_sheet(args)
        return self_test()
    except GateError as exc:
        print(f"POPUP KIT FAIL: {exc}", file=sys.stderr)
        return 1
    except (KitError, pngio.PngError) as exc:
        print(f"POPUP KIT FAIL: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
