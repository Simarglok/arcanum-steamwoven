#!/usr/bin/env python3
"""ARC-UI-3098: the kit's fixtures, the required parts that the old window arts painted into themselves (pits, fields, dials, discs, ruler, gears, signs, cell wells).

    python3 -B tools/steamwoven_ui/fixtures.py list
    python3 -B tools/steamwoven_ui/fixtures.py build [--out DIR] [--variant brass|flat] [--only ID,...]
    python3 -B tools/steamwoven_ui/fixtures.py self-test

Owner decision 2026-11-24 (#3098, option A): these parts are kit parts, not a layer of the window's old art. Their shapes follow the original's (the place, the size and the sign of each, TRD "Elements the old arts held") and
are drawn here by rule, deterministically, with no generator and no pixel of the original (the pilot's glyph rule, #3058): the same call gives the same bytes. `build` writes one master PNG per part, RGBA, at the part's master
size (the unit size x 3.6 on the 6 px grid, `kit_check.snap`), to `local/steamwoven-ui/masters/<id>.png` (ignored by git), ready for `bake.py`. The viewer's table of the same parts is `SteamwovenKitTable.FixtureUnits`
(`tools/terrain-viewer/Core/SteamwovenKitComposer.cs`); a test pins it to `FIXTURES` through `kit.json`.

`brass` is the default (owner decision 2026-11-24): the original's grids, fields and doll frame have a thin gold trim, so the pits and boxes get a brass trim; `flat` (black wells, thin grey-blue lines) is the look decided for the button glyphs' cells only. Both draw the same shapes at the same places.
The synthetic signs (the hat, the scroll, the heart, the cross, the five count icons) are stand-in drawings: in production their shapes are taken from the original's mask by the pilot's glyph rule (#3058), not redrawn. Standard library only, on `pngio`.
"""
from __future__ import annotations

import argparse
import math
import sys
from dataclasses import dataclass
from pathlib import Path

try:
    from . import image_archive, pngio, prompt_library
except ImportError:  # run as a script
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    import image_archive  # type: ignore
    import pngio  # type: ignore
    import prompt_library  # type: ignore



def default_out() -> Path:
    """`local/steamwoven-ui/masters` of the MAIN checkout (ARC-DEV-3277: a worktree has no `local/`; masters written there
    are never seen by `bake.py` and `kit_check.py` and are lost with the worktree)."""
    try:
        return prompt_library.default_local_root() / "masters"
    except Exception:  # not inside a git checkout: the old default next to the tools
        return Path(__file__).resolve().parents[2] / "local" / "steamwoven-ui" / "masters"


GRID = 6
VARIANTS = ("flat", "brass")


def snap(units: int) -> int:
    """Original units -> master px on the 6 px grid (units x 3.6, halves up); the same rule as `kit_check.snap`."""
    return GRID * ((6 * units + 5) // 10)


@dataclass(frozen=True)
class Fixture:
    ident: str
    width: int  # original units
    height: int
    windows: tuple[str, ...]  # census windows that draw it
    note: str
    cut: bool = False  # not drawn here: a picture cut from the player's own HD frame by `label_parts.py` (ARC-UI-3299)

    @property
    def master(self) -> tuple[int, int]:
        return snap(self.width), snap(self.height)


INV = ("G01", "G17")
INV_FAMILY = ("G01", "G02", "G03", "G04", "G17", "M10")
FIXTURES: tuple[Fixture, ...] = (
    Fixture("grid.inventory_cells", 32, 32, INV_FAMILY, "one cell well of the item grids (black, thin line on its last column and row of units, where the original has it); the pack's 10 x 12, the doll's 10 x 9 and the holder's 10 x 7 or 10 x 8 are copies of it"),
    Fixture("fixture.pack_pit", 324, 388, INV_FAMILY, "the pack's pit at 366, 6 with its inner line round the 10 x 12 grid at 368, 8"),
    Fixture("fixture.doll_pit", 330, 300, INV, "the paper doll's pit at 18, 100 with its inner line round the 10 x 9 grid at 23, 107"),
    Fixture("fixture.holder_pit_loot", 324, 260, ("G02", "G04"), "the body's or container's grid pit at 6, 134 (10 x 8 cells at 8, 136)"),
    Fixture("fixture.holder_pit_barter", 324, 228, ("G03", "M10"), "the merchant's grid pit at 6, 166 (10 x 7 cells at 8, 168)"),
    Fixture("fixture.name_pit", 223, 55, INV, "the plate behind the weights and speed text at 124, 25"),
    Fixture("fixture.quote_field", 267, 36, ("G03", "M10"), "the field behind the merchant's quote line at 82, 125"),
    Fixture("fixture.count_pit", 61, 17, INV_FAMILY, "the field behind a count (gold, four ammunition) at 727, 71 + 23 k"),
    Fixture("fixture.count_icons", 38, 115, INV_FAMILY, "the five count signs (gold, arrows, bullets, charges, fuel) at 690, 68, one every 23 units"),
    Fixture("fixture.weapon_frame", 112, 97, INV, "the quarter-disc frame of the weapon slot at 10, 303"),
    Fixture("fixture.shield_frame", 114, 97, INV, "the quarter-disc frame of the shield slot at 244, 303"),
    Fixture("fixture.arrange_legend", 78, 32, INV, "the arrange button's legend at 690, 192: scattered dots, an arrow, a grid"),
    Fixture("fixture.sheet_name_box", 360, 87, ("G05",), "the name and info box of the character sheet at 119, 9"),
    Fixture("fixture.sheet_derived_box", 452, 113, ("G05",), "the derived-stats and resistance box at 24, 278 with its divider"),
    Fixture("fixture.stat_oval", 70, 31, ("G05",), "the oval behind a stat number, eight of them at 172 and 315, 111 + 40 r"),
    Fixture("fixture.align_dial", 100, 100, ("G05",), "the alignment dial at 6, 131: brass ring, face with its divisions, hub, the number pit (the letters E and G are written by the sheet)"),
    Fixture("fixture.health_disc", 76, 56, ("G05",), "the health disc with its red heart at 411, 127"),
    Fixture("fixture.fatigue_disc", 76, 56, ("G05",), "the fatigue disc with its blue cross at 411, 197"),
    Fixture("fixture.mt_ruler", 46, 330, ("G05",), "the magic and technology ruler at 747, 60: ticks, the magic sign, the technology gear, two number pits (the divisions 75 50 25 0 25 50 75 are written by the sheet)"),
    Fixture("fixture.sheet_stat_names_l", 50, 160, ("G05",), "the stat names ST CN DX BE of the sheet at 131, 115 + 40 r in the old HD version's look (the part's corner is 120, 108)", cut=True),
    Fixture("fixture.sheet_stat_names_r", 55, 160, ("G05",), "the stat names IN WP PE CH of the sheet at 263, 115 + 40 r in the old HD version's look (the part's corner is 255, 108)", cut=True),
    Fixture("fixture.sheet_dial_letters", 65, 25, ("G05",), "the alignment dial's E&G with its two curls at 45..70, 153 (the part's corner is 25, 146)", cut=True),
    Fixture("fixture.schem_page", 506, 367, ("G15",), "the blueprint page at 201, 13 (paper)"),
    Fixture("grid.schematic", 506, 367, ("G15",), "the page's ruling over the paper: a line every 32 units from 26, 9 of the page"),
    Fixture("fixture.schem_slot", 103, 71, ("G15",), "a component slot box, two of them at 577, 179 and 577, 275"),
    Fixture("fixture.schem_channel", 126, 30, ("G15",), "the assemble channel at 39, 334"),
    Fixture("fixture.gear_hat", 90, 70, ("G15",), "the learned list's legend at 72, 144: a gear with a mortarboard"),
    Fixture("fixture.gear_scroll", 96, 72, ("G15",), "the found list's legend at 71, 231: a gear with a scroll"),
)
BY_ID = {fixture.ident: fixture for fixture in FIXTURES}

# ARC-UI-3342: the original paints a grid line one unit wide on the last column and row of units of each 32-unit cell (and one unit before the first cell); the kit's 2 px line is centred there.
LINE_AT = -0.5
Colour = tuple[int, int, int]
BLACK: Colour = (4, 5, 9)
EDGE: Colour = (62, 74, 98)
BRASS: Colour = (184, 146, 62)
BRASS_DARK: Colour = (104, 76, 28)
BRASS_LIGHT: Colour = (226, 192, 104)
PARCHMENT: Colour = (214, 198, 158)
PAPER: Colour = (238, 226, 188)
RULE: Colour = (206, 184, 132)
INK: Colour = (62, 46, 26)
DARK_WOOD: Colour = (48, 34, 24)
PALE: Colour = (190, 190, 168)
RED: Colour = (190, 30, 26)
BLUE: Colour = (34, 56, 214)
STEEL: Colour = (122, 124, 134)
STEEL_DARK: Colour = (58, 60, 68)


class Canvas:
    """A straight-alpha RGBA canvas with antialiased shapes by signed distance (no randomness, same call same bytes)."""

    def __init__(self, width: int, height: int, units: tuple[int, int]) -> None:
        self.w, self.h = width, height
        self.px = bytearray(width * height * 4)
        self.sx, self.sy = width / units[0], height / units[1]
        self.s = (self.sx + self.sy) / 2

    # unit -> pixel
    def X(self, u: float) -> float:
        return u * self.sx

    def Y(self, v: float) -> float:
        return v * self.sy

    def put(self, x: int, y: int, rgb: Colour, a: float) -> None:
        if a <= 0 or not (0 <= x < self.w and 0 <= y < self.h):
            return
        i = (y * self.w + x) * 4
        da = self.px[i + 3] / 255
        oa = a + da * (1 - a)
        for c in range(3):
            self.px[i + c] = round((rgb[c] * a + self.px[i + c] * da * (1 - a)) / oa)
        self.px[i + 3] = round(oa * 255)

    def rect(self, x0: float, y0: float, x1: float, y1: float, rgb: Colour) -> None:
        """An opaque rectangle on whole pixels (the edges are rounded), the fast path of the big fields."""
        ix0, iy0, ix1, iy1 = max(0, round(x0)), max(0, round(y0)), min(self.w, round(x1)), min(self.h, round(y1))
        if ix1 <= ix0:
            return
        row = bytes((*rgb, 255)) * (ix1 - ix0)
        for y in range(iy0, iy1):
            self.px[(y * self.w + ix0) * 4:(y * self.w + ix1) * 4] = row

    def frame(self, x0: float, y0: float, x1: float, y1: float, t: int, rgb: Colour) -> None:
        self.rect(x0, y0, x1, y0 + t, rgb)
        self.rect(x0, y1 - t, x1, y1, rgb)
        self.rect(x0, y0 + t, x0 + t, y1 - t, rgb)
        self.rect(x1 - t, y0 + t, x1, y1 - t, rgb)

    def shape(self, box: tuple[float, float, float, float], distance, rgb: Colour, alpha: float = 1.0) -> None:
        """Fills where `distance(x, y)` (pixels, negative inside) is below zero, with a one pixel antialiased edge."""
        x0, y0, x1, y1 = box
        for y in range(max(0, int(y0) - 1), min(self.h, int(y1) + 2)):
            for x in range(max(0, int(x0) - 1), min(self.w, int(x1) + 2)):
                coverage = 0.5 - distance(x + 0.5, y + 0.5)
                if coverage > 0:
                    self.put(x, y, rgb, min(1.0, coverage) * alpha)

    def disc(self, cx: float, cy: float, r: float, rgb: Colour, ring: float = 0.0) -> None:
        """A disc of the radius `r` px, or (with `ring` > 0) a ring of that width whose outer radius is `r`."""
        if ring > 0:
            self.shape((cx - r, cy - r, cx + r, cy + r), lambda x, y: abs(math.hypot(x - cx, y - cy) - (r - ring / 2)) - ring / 2, rgb)
        else:
            self.shape((cx - r, cy - r, cx + r, cy + r), lambda x, y: math.hypot(x - cx, y - cy) - r, rgb)

    def round_rect(self, x0: float, y0: float, x1: float, y1: float, r: float, rgb: Colour) -> None:
        cx, cy, hx, hy = (x0 + x1) / 2, (y0 + y1) / 2, (x1 - x0) / 2 - r, (y1 - y0) / 2 - r

        def distance(x: float, y: float) -> float:
            qx, qy = abs(x - cx) - hx, abs(y - cy) - hy
            return math.hypot(max(qx, 0), max(qy, 0)) + min(max(qx, qy), 0) - r
        self.shape((x0, y0, x1, y1), distance, rgb)

    def segment(self, x0: float, y0: float, x1: float, y1: float, width: float, rgb: Colour) -> None:
        dx, dy = x1 - x0, y1 - y0
        length = dx * dx + dy * dy or 1.0

        def distance(x: float, y: float) -> float:
            t = max(0.0, min(1.0, ((x - x0) * dx + (y - y0) * dy) / length))
            return math.hypot(x - (x0 + t * dx), y - (y0 + t * dy)) - width / 2
        self.shape((min(x0, x1) - width, min(y0, y1) - width, max(x0, x1) + width, max(y0, y1) + width), distance, rgb)

    def polygon(self, points: list[tuple[float, float]], rgb: Colour) -> None:
        n = len(points)

        def distance(x: float, y: float) -> float:
            best, inside = 1e9, False
            for i in range(n):
                (ax, ay), (bx, by) = points[i], points[(i + 1) % n]
                if (ay > y) != (by > y) and x < (bx - ax) * (y - ay) / (by - ay) + ax:
                    inside = not inside
                ex, ey = bx - ax, by - ay
                t = max(0.0, min(1.0, ((x - ax) * ex + (y - ay) * ey) / (ex * ex + ey * ey or 1.0)))
                best = min(best, math.hypot(x - (ax + t * ex), y - (ay + t * ey)))
            return -best if inside else best
        xs, ys = [p[0] for p in points], [p[1] for p in points]
        self.shape((min(xs), min(ys), max(xs), max(ys)), distance, rgb)

    def gear(self, cx: float, cy: float, r: float, teeth: int, rgb: Colour) -> None:
        """A toothed wheel of the outer radius `r` px: trapezoid teeth of the height 0.18 r."""
        inner, step = r * 0.82, math.tau / teeth

        def distance(x: float, y: float) -> float:
            rho, phase = math.hypot(x - cx, y - cy), (math.atan2(y - cy, x - cx) % step) / step
            tooth = 1.0 if 0.18 <= phase <= 0.82 else (0.0 if phase < 0.06 or phase > 0.94 else (phase - 0.06) / 0.12 if phase < 0.18 else (0.94 - phase) / 0.12)
            return rho - (inner + (r - inner) * tooth)
        self.shape((cx - r, cy - r, cx + r, cy + r), distance, rgb)

    def image(self) -> pngio.Image:
        return pngio.Image(self.w, self.h, self.px)


def _trim(c: Canvas, x0: float, y0: float, x1: float, y1: float, variant: str) -> None:
    """The edge of a pit or box: grey-blue and thin (flat), or brass with a dark line inside (brass)."""
    if variant == "brass":
        c.frame(x0, y0, x1, y1, max(3, round(c.s * 0.9)), BRASS)
        c.frame(x0 + max(3, round(c.s * 0.9)), y0 + max(3, round(c.s * 0.9)), x1 - max(3, round(c.s * 0.9)), y1 - max(3, round(c.s * 0.9)), 1, BRASS_DARK)
    else:
        c.frame(x0, y0, x1, y1, 2, EDGE)


def _pit(c: Canvas, variant: str, inner: tuple[float, float, float, float] | None = None) -> None:
    c.rect(0, 0, c.w, c.h, BLACK)
    _trim(c, 0, 0, c.w, c.h, variant)
    if inner:
        # The outer lines of the grid laid over this rectangle, half a unit before its edges: the left and top ones show beside the cells, which cover the other two with their own.
        x, y, w, h = inner
        c.frame(c.X(x + LINE_AT) - 1, c.Y(y + LINE_AT) - 1, c.X(x + w + LINE_AT) + 1, c.Y(y + h + LINE_AT) + 1, 2, EDGE)


def _quarter(c: Canvas, right: bool, fill: Colour, variant: str) -> None:
    """A quarter disc whose corner is the part's bottom-left (right) or bottom-right (left) corner, with a brass arc."""
    r = c.Y(97)
    cx, cy = (0.0 if right else c.w), c.h

    def distance(x: float, y: float) -> float:
        inside = y <= cy and (x >= cx if right else x <= cx)
        return math.hypot(x - cx, y - cy) - r if inside else 1e3
    c.shape((min(cx, cx + r if right else cx - r), cy - r, max(cx, cx + r if right else cx - r), cy), distance, fill)
    arc = max(5.0, c.s * 2.2)
    c.shape((min(cx, cx + r if right else cx - r), cy - r, max(cx, cx + r if right else cx - r), cy),
            lambda x, y: abs(math.hypot(x - cx, y - cy) - (r - arc / 2)) - arc / 2 if (y <= cy and (x >= cx if right else x <= cx)) else 1e3, BRASS)
    c.shape((min(cx, cx + r if right else cx - r), cy - r, max(cx, cx + r if right else cx - r), cy),
            lambda x, y: abs(math.hypot(x - cx, y - cy) - (r - arc - 1)) - 1 if (y <= cy and (x >= cx if right else x <= cx)) else 1e3, BRASS_DARK)


def _icons(c: Canvas) -> None:
    """The five count signs, one every 23 units: gold, arrows, bullets, charges, fuel."""
    cell = c.Y(23)
    cx = c.X(19)
    for k in range(5):
        cy = cell * (k + 0.5)
        u = c.s
        if k == 0:  # three gold coins stacked
            for i, dy in enumerate((5, 0, -5)):
                c.disc(cx, cy + dy * u * 0.6, 9 * u * 0.9, BRASS_DARK)
                c.disc(cx, cy + dy * u * 0.6 - u * 0.5, 8 * u * 0.9, BRASS_LIGHT if i == 2 else BRASS)
        elif k == 1:  # two arrows pointing up and right
            for off in (-3, 3):
                x0, y0, x1, y1 = cx - 9 * u + off * u, cy + 8 * u, cx + 9 * u + off * u, cy - 8 * u
                c.segment(x0, y0, x1, y1, u * 1.6, (150, 120, 80))
                c.polygon([(x1, y1), (x1 - 4.5 * u, y1 + 1.2 * u), (x1 - 1.2 * u, y1 + 4.5 * u)], PALE)
                c.polygon([(x0, y0), (x0 + 3 * u, y0 - u), (x0 + 1.4 * u, y0 - 4 * u)], RED)
        elif k == 2:  # two bullets
            for off in (-4, 4):
                c.round_rect(cx + (off - 2.6) * u, cy - 5 * u, cx + (off + 2.6) * u, cy + 8 * u, 1.2 * u, BRASS)
                c.disc(cx + off * u, cy - 5 * u, 2.6 * u, (160, 100, 70))
        elif k == 3:  # a charge cell: a steel can with its contact and a bolt
            c.round_rect(cx - 5.5 * u, cy - 7 * u, cx + 5.5 * u, cy + 8 * u, 1.6 * u, STEEL)
            c.round_rect(cx - 2.2 * u, cy - 9.5 * u, cx + 2.2 * u, cy - 6 * u, 0.8 * u, BRASS_LIGHT)
            c.polygon([(cx + 1.5 * u, cy - 4 * u), (cx - 3 * u, cy + 1 * u), (cx - 0.2 * u, cy + 1 * u), (cx - 1.5 * u, cy + 6 * u), (cx + 3 * u, cy - 0.5 * u), (cx + 0.2 * u, cy - 0.5 * u)], (240, 210, 60))
        else:  # a fuel can
            c.round_rect(cx - 6 * u, cy - 5 * u, cx + 6 * u, cy + 8 * u, 1.6 * u, RED)
            c.round_rect(cx - 2 * u, cy - 8.5 * u, cx + 2 * u, cy - 4 * u, 0.8 * u, BRASS)
            c.rect(cx - 4 * u, cy - 0.5 * u, cx + 4 * u, cy + 1 * u, (90, 14, 12))


def _legend(c: Canvas) -> None:
    """The arrange legend: scattered dots, a red arrow, a small grid (coordinates of the part's units)."""
    for dx, dy in ((0, 4), (6, 0), (6, 9), (12, 5), (14, 12), (19, 1), (20, 9)):
        c.rect(c.X(3 + dx), c.Y(5 + dy), c.X(7 + dx), c.Y(9 + dy), PALE)
    x, y, length, half = 31.0, 13.0, 17.0, 7.0
    c.polygon([(c.X(x), c.Y(y - half * 0.4)), (c.X(x + length * 0.55), c.Y(y - half * 0.4)), (c.X(x + length * 0.55), c.Y(y - half)), (c.X(x + length), c.Y(y)),
               (c.X(x + length * 0.55), c.Y(y + half)), (c.X(x + length * 0.55), c.Y(y + half * 0.4)), (c.X(x), c.Y(y + half * 0.4))], RED)
    for gx in range(4):
        for gy in range(4):
            c.rect(c.X(51 + gx * 5), c.Y(5 + gy * 5), c.X(55 + gx * 5), c.Y(9 + gy * 5), PALE)


def _dial(c: Canvas) -> None:
    cx, cy, r = c.X(50), c.Y(50), c.s * 50
    c.disc(cx, cy, r, BRASS)
    c.disc(cx, cy, r, BRASS_DARK, ring=2)
    c.disc(cx, cy, c.s * 45, PARCHMENT)
    c.disc(cx, cy, c.s * 45, BRASS_DARK, ring=2)
    for i in range(13):  # divisions over the upper half, a long one at each end and in the middle
        angle = math.pi + math.pi * i / 12
        inner = c.s * (34 if i % 6 == 0 else 38)
        c.segment(cx + math.cos(angle) * inner, cy + math.sin(angle) * inner, cx + math.cos(angle) * c.s * 43, cy + math.sin(angle) * c.s * 43, max(2.0, c.s * 0.5), INK)
    c.rect(c.X(34), c.Y(67), c.X(68), c.Y(84), BLACK)
    c.frame(c.X(34), c.Y(67), c.X(68), c.Y(84), 2, EDGE)
    c.disc(cx, cy, c.s * 8, DARK_WOOD)
    c.disc(cx, cy, c.s * 8, BRASS, ring=2)


def _status_disc(c: Canvas, heart: bool, variant: str) -> None:
    cx, cy = c.X(38), c.Y(28)
    c.disc(cx, cy, c.s * 24, BLACK)
    c.disc(cx, cy, c.s * 26, BRASS, ring=c.s * 2.4)
    for dx in (7, 69):  # the two knobs at the sides
        c.disc(c.X(dx), cy, c.s * 5, BRASS_DARK)
        c.disc(c.X(dx), cy, c.s * 3.6, BRASS_LIGHT)
    if heart:
        for dx in (-7, 7):
            c.disc(cx + dx * c.s, cy - 5 * c.s, 7.4 * c.s, RED)
        c.polygon([(cx - 13.6 * c.s, cy - 2 * c.s), (cx + 13.6 * c.s, cy - 2 * c.s), (cx, cy + 15 * c.s)], RED)
    else:
        c.rect(cx - 6.5 * c.s, cy - 14.5 * c.s, cx + 6.5 * c.s, cy + 14.5 * c.s, BLUE)
        c.rect(cx - 14.5 * c.s, cy - 6.5 * c.s, cx + 14.5 * c.s, cy + 6.5 * c.s, BLUE)


def _ruler(c: Canvas, variant: str) -> None:
    c.rect(c.X(0), c.Y(0), c.w, c.h, DARK_WOOD)
    c.rect(c.X(3), c.Y(2), c.X(43), c.Y(326), PARCHMENT)
    c.frame(c.X(3), c.Y(2), c.X(43), c.Y(326), 2, BRASS_DARK)
    for i in range(-17, 18):  # a tick every 6.4 units, a long one at each division (every 5th, 32 units)
        y = c.Y(163 + i * 6.4)
        c.rect(c.X(9), y - 1, c.X(21 if i % 5 == 0 else 15), y + 1, INK)
    cx, cy = c.X(24), c.Y(35)
    c.disc(cx, cy, c.s * 12, (26, 40, 150), ring=c.s * 3)
    c.disc(cx, cy, c.s * 4, (26, 40, 150))
    c.gear(c.X(24), c.Y(292), c.s * 12, 8, (204, 128, 14))
    c.disc(c.X(24), c.Y(292), c.s * 4, DARK_WOOD)
    for y0, y1 in ((6, 22), (306, 323)):
        c.rect(c.X(9), c.Y(y0), c.X(39), c.Y(y1), BLACK)
        c.frame(c.X(9), c.Y(y0), c.X(39), c.Y(y1), 2, EDGE)


def _gear_sign(c: Canvas, cx_u: float, hat: bool) -> None:
    cx, cy = c.X(cx_u), c.Y(35 if hat else 36)
    c.disc(cx, cy, c.s * 35, DARK_WOOD)
    c.gear(cx, cy, c.s * 31, 12, STEEL)
    c.disc(cx, cy, c.s * 21, STEEL_DARK)
    if hat:  # a mortarboard: the board, the cap under it, a tassel
        def at(dx: float, dy: float) -> tuple[float, float]:
            return cx + dx * c.s, cy + dy * c.s
        c.polygon([at(-14, -1), at(-14, 13), at(2, 19), at(18, 13), at(18, -1), at(2, 5)], (96, 98, 110))
        c.polygon([at(-26, -6), at(0, -17), at(28, -6), at(2, 5)], (186, 188, 200))
        c.segment(*at(26, -6), *at(26, 8), c.s * 1.2, BRASS_LIGHT)
        c.disc(*at(26, 9), c.s * 2.2, BRASS_LIGHT)
    else:  # a scroll: a roll with its two curled ends
        c.round_rect(cx - 28 * c.s, cy - 8 * c.s, cx + 28 * c.s, cy + 8 * c.s, c.s * 5, (218, 200, 160))
        c.disc(cx - 28 * c.s, cy, c.s * 8, (200, 178, 136))
        c.disc(cx + 28 * c.s, cy, c.s * 8, (200, 178, 136))
        c.disc(cx - 28 * c.s, cy, c.s * 3, (110, 80, 50))
        c.disc(cx + 28 * c.s, cy, c.s * 3, (110, 80, 50))


def draw(ident: str, variant: str = "brass") -> pngio.Image:
    """The master of one fixture. Raises KeyError for an unknown id, and for a cut one (`label_parts.py` makes those from the player's HD frame)."""
    fixture = BY_ID[ident]
    if fixture.cut:
        raise KeyError(f"{ident} is cut from the player's HD frame by label_parts.py, not drawn here")
    width, height = fixture.master
    c = Canvas(width, height, (fixture.width, fixture.height))
    if ident == "grid.inventory_cells":
        c.rect(0, 0, width, height, BLACK)
        x, y = c.X(fixture.width + LINE_AT), c.Y(fixture.height + LINE_AT)
        c.rect(x - 1, 0, x + 1, height, EDGE)
        c.rect(0, y - 1, width, y + 1, EDGE)
    elif ident == "fixture.pack_pit":
        _pit(c, variant, (2, 2, 320, 384))
    elif ident == "fixture.doll_pit":
        _pit(c, variant, (5, 7, 320, 288))
    elif ident == "fixture.holder_pit_loot":
        _pit(c, variant, (2, 2, 320, 256))
    elif ident == "fixture.holder_pit_barter":
        _pit(c, variant, (2, 2, 320, 224))
    elif ident in ("fixture.name_pit", "fixture.quote_field", "fixture.count_pit", "fixture.sheet_name_box", "fixture.schem_channel"):
        _pit(c, variant)
    elif ident == "fixture.sheet_derived_box":
        _pit(c, variant)
        c.rect(c.X(284), c.Y(16), c.X(287), c.Y(100), EDGE)
    elif ident == "fixture.stat_oval":
        c.round_rect(0, 0, width, height, c.Y(10), EDGE if variant == "flat" else BRASS)
        c.round_rect(2, 2, width - 2, height - 2, c.Y(10) - 2, BLACK)
    elif ident == "fixture.count_icons":
        _icons(c)
    elif ident == "fixture.weapon_frame":
        _quarter(c, True, (52, 12, 12), variant)
    elif ident == "fixture.shield_frame":
        _quarter(c, False, (10, 14, 54), variant)
    elif ident == "fixture.arrange_legend":
        _legend(c)
    elif ident == "fixture.align_dial":
        _dial(c)
    elif ident == "fixture.health_disc":
        _status_disc(c, True, variant)
    elif ident == "fixture.fatigue_disc":
        _status_disc(c, False, variant)
    elif ident == "fixture.mt_ruler":
        _ruler(c, variant)
    elif ident == "fixture.schem_page":
        c.rect(0, 0, width, height, PAPER)
        c.frame(0, 0, width, height, 2, RULE)
    elif ident == "grid.schematic":
        for k in range(15):
            x = c.X(26 + 32 * k)
            if x < width:
                c.rect(x - 1, 0, x + 1, height, RULE)
        for k in range(12):
            y = c.Y(9 + 32 * k)
            if y < height:
                c.rect(0, y - 1, width, y + 1, RULE)
    elif ident == "fixture.schem_slot":
        c.rect(0, 0, width, height, (244, 236, 208))
        c.frame(0, 0, width, height, max(3, round(c.s * 0.9)), INK)
    elif ident == "fixture.gear_hat":
        _gear_sign(c, 45, True)
    elif ident == "fixture.gear_scroll":
        _gear_sign(c, 48, False)
    else:
        raise KeyError(ident)
    return c.image()


def build(out: Path, variant: str, only: set[str] | None = None) -> list[str]:
    out.mkdir(parents=True, exist_ok=True)
    written = []
    for fixture in FIXTURES:
        if fixture.cut or (only and fixture.ident not in only):
            continue
        pngio.write(out / f"{fixture.ident}.png", draw(fixture.ident, variant))
        written.append(fixture.ident)
    return written


def self_test() -> int:
    problems = []
    if len({f.ident for f in FIXTURES}) != len(FIXTURES):
        problems.append("duplicate ids")
    for fixture in FIXTURES:
        width, height = fixture.master
        if width % GRID or height % GRID:
            problems.append(f"{fixture.ident}: master {width} x {height} is not on the 6 px grid")
    for variant in VARIANTS:
        for ident in ("fixture.count_pit", "fixture.health_disc", "fixture.gear_scroll"):
            first, second = draw(ident, variant), draw(ident, variant)
            fixture = BY_ID[ident]
            if first.data != second.data or (first.w, first.h) != fixture.master:
                problems.append(f"{ident} ({variant}): not deterministic or the wrong size")
            if not any(first.data[i] for i in range(3, len(first.data), 4)):
                problems.append(f"{ident} ({variant}): nothing drawn")
    for problem in problems:
        print("FIXTURES FAIL:", problem)
    if problems:
        return 1
    print(f"FIXTURES PASS: {len(FIXTURES)} fixtures, deterministic, on the 6 px grid")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("list")
    build_parser = sub.add_parser("build")
    build_parser.add_argument("--out", type=Path, default=None, help="default: local/steamwoven-ui/masters of the main checkout")
    build_parser.add_argument("--variant", choices=VARIANTS, default="brass")
    build_parser.add_argument("--only", default="")
    sub.add_parser("self-test")
    args = parser.parse_args(argv)
    if args.command == "list":
        for fixture in FIXTURES:
            print(f"{fixture.ident}\t{fixture.width}x{fixture.height} units\t{fixture.master[0]}x{fixture.master[1]} px\t{fixture.note}")
        return 0
    if args.command == "self-test":
        return self_test()
    out = args.out if args.out is not None else default_out()
    image_archive.warn_if_ephemeral(out, "fixtures.py build")
    only = {item for item in args.only.split(",") if item} or None
    if only and not only <= set(BY_ID):
        print("unknown ids:", ", ".join(sorted(only - set(BY_ID))))
        return 2
    for ident in build(out, args.variant, only):
        print("wrote", out / f"{ident}.png")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
