#!/usr/bin/env python3
"""ARC-UI-3098: the kit's fixtures, the required parts that the old window arts painted into themselves (pits, fields, dials, discs, ruler, gears, signs, cell wells).

    python3 -B tools/steamwoven_ui/fixtures.py list
    python3 -B tools/steamwoven_ui/fixtures.py build [--out DIR] [--only ID,...]
    python3 -B tools/steamwoven_ui/fixtures.py self-test

Owner decision 2026-11-24 (#3098, option A): these parts are kit parts, not a layer of the window's old art. All but one are cut from the player's own HD pack (rule P, pack first): the character sheet's by `label_parts.py`
(ARC-UI-3299) and `sheet_parts.py` (ARC-UI-3321), the inventory family's and the schematic window's by `window_parts.py` (ARC-UI-3474). One is drawn here by rule, deterministically, with no pixel of the original: the item grids'
cell well (`grid.inventory_cells`, ARC-UI-3342). `build` writes its master PNG, RGBA, at the part's master size (the unit size x 3.6 on the 6 px grid, `kit_check.snap`), to `local/steamwoven-ui/masters/<id>.png`
(ignored by git), ready for `bake.py`. The viewer's table of the same parts is `SteamwovenKitTable.FixtureUnits` (`tools/terrain-viewer/Core/SteamwovenKitComposer.cs`); a test pins it to `FIXTURES` through `kit.json`.
Standard library only, on `pngio`.
"""
from __future__ import annotations

import argparse
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
    cut: bool = False  # not drawn here: a picture cut from the player's own HD frame by `label_parts.py` (ARC-UI-3299), `sheet_parts.py` (ARC-UI-3321) or `window_parts.py` (ARC-UI-3474)

    @property
    def master(self) -> tuple[int, int]:
        return snap(self.width), snap(self.height)


INV = ("G01", "G17")
INV_FAMILY = ("G01", "G02", "G03", "G04", "G17", "M10")
FIXTURES: tuple[Fixture, ...] = (
    Fixture("grid.inventory_cells", 32, 32, INV_FAMILY, "one cell well of the item grids (black, thin line on its last column and row); the pack's 10 x 12, the doll's 10 x 9 and the holder's 10 x 7 or 10 x 8 are copies of it"),
    Fixture("fixture.pack_pit", 332, 396, INV_FAMILY, "the pack's pit with its brass rim at 363, 3 under the 10 x 12 grid at 368, 8, its ground pure black (owner 2026-11-26), old HD look", cut=True),
    Fixture("fixture.doll_pit", 350, 299, INV, "the paper doll's figured brass frame with its curls and its grid at 8, 101, old HD look; the viewer draws no cell wells over it", cut=True),
    Fixture("fixture.holder_pit_loot", 354, 270, ("G02", "G04"), "the body's or container's grid pit with its scroll chain at 2, 130 (10 x 8 cells at 8, 136), old HD look", cut=True),
    Fixture("fixture.holder_pit_barter", 353, 236, ("G03", "M10"), "the merchant's grid pit with its scroll chain at 3, 163 (10 x 7 cells at 8, 168), old HD look", cut=True),
    Fixture("fixture.name_pit", 245, 66, INV, "the figured brass plate behind the weights and speed text at 113, 21, old HD look", cut=True),
    Fixture("fixture.quote_field", 272, 43, ("G03", "M10"), "the field behind the merchant's quote line at 79, 122, old HD look", cut=True),
    Fixture("fixture.count_pit", 64, 20, INV_FAMILY, "the field behind a count (gold, four ammunition) at 725, 70 + 23 k, old HD look (the first, drawn five times)", cut=True),
    Fixture("fixture.count_icons", 38, 118, INV_FAMILY, "the five count signs (gold, arrows, bullets, charges, fuel) at 690, 67, one every 23 units, old HD look", cut=True),
    Fixture("fixture.weapon_frame", 101, 89, INV, "the rounded double-rim panel of the weapon slot at 21, 311, old HD look (ARC-UI-3366)", cut=True),
    Fixture("fixture.shield_frame", 101, 89, INV, "the rounded double-rim panel of the shield slot at 246, 311, old HD look (ARC-UI-3366)", cut=True),
    Fixture("fixture.arrange_legend", 68, 34, INV, "the arrange button's legend at 691, 193: scattered tiles, a red arrow, a grid, old HD look", cut=True),
    Fixture("fixture.sheet_name_box", 390, 102, ("G05",), "the name and info box of the character sheet at 106, 2 in the old HD version's look: the figured brass frame with its curls and the black field (ARC-UI-3321)", cut=True),
    Fixture("fixture.sheet_derived_box", 488, 128, ("G05",), "the derived-stats and resistance box at 10, 270 in the old HD version's look: the figured brass frame with its curls, the black field and the divider (ARC-UI-3321)", cut=True),
    Fixture("fixture.stat_oval", 80, 44, ("G05",), "the brass oval round a stat number, two sockets and the black pit, in the old HD look: eight of them, the first at 167, 105, then 143 right and 40 down", cut=True),
    Fixture("fixture.align_dial", 109, 111, ("G05",), "the alignment dial at 3, 128 in the old HD look: brass bezel, paper face with ticks, hub, number pit (E and G are the label part; needle and number are live)", cut=True),
    Fixture("fixture.align_needle", 245, 105, ("G05",), "art 254's 21 needle frames from the HD pack, the baked grey shadow replaced by the soft shadow (#3249, 1.2 blur), 35-unit cells (#3312)", cut=True),
    Fixture("fixture.health_disc", 87, 68, ("G05",), "the health disc, red heart, two sockets, at 405, 122, old HD look; kit +/- are 21 units, centred (original 19: minus 1 left, both 0.5 up)", cut=True),
    Fixture("fixture.fatigue_disc", 87, 67, ("G05",), "the fatigue disc, blue cross, two sockets, at 405, 192, old HD look; kit +/- are 21 units, centred (original 19: minus 1 left, both 0.5 up)", cut=True),
    Fixture("fixture.mt_ruler", 60, 332, ("G05",), "the magic and technology ruler at 740, 60 in the old HD look: parchment strip with ticks and divisions 75 50 25 0 25 50 75, magic sign, gear, two number caps", cut=True),
    Fixture("fixture.sheet_stat_names_l", 50, 160, ("G05",), "the stat names ST CN DX BE of the sheet at 131, 115 + 40 r in the old HD version's look (the part's corner is 120, 108)", cut=True),
    Fixture("fixture.sheet_stat_names_r", 55, 160, ("G05",), "the stat names IN WP PE CH of the sheet at 263, 115 + 40 r in the old HD version's look (the part's corner is 255, 108)", cut=True),
    Fixture("fixture.sheet_dial_letters", 65, 25, ("G05",), "the alignment dial's E&G with its two curls at 45..70, 153 (the part's corner is 25, 146)", cut=True),
    Fixture("fixture.schem_page", 533, 381, ("G15",), "the blueprint page at 193, 6 with its ruling, the two component slot boxes and the bracket to them, old HD look", cut=True),
    Fixture("fixture.schem_channel", 137, 70, ("G15",), "the brass assemble channel at 33, 315, old HD look", cut=True),
    Fixture("fixture.gear_hat", 94, 80, ("G15",), "the learned list's legend at 71, 141: a gear with a mortarboard, old HD look", cut=True),
    Fixture("fixture.gear_scroll", 109, 82, ("G15",), "the found list's legend at 65, 228: a gear with a scroll, old HD look", cut=True),
)
BY_ID = {fixture.ident: fixture for fixture in FIXTURES}

# ARC-UI-3342: the original paints a grid line one unit wide on the last column and row of units of a 32-unit cell, and none before a grid's first cell; the kit's 2 px line is centred there.
LINE_AT = -0.5
Colour = tuple[int, int, int]
GRID_GROUND: Colour = (0, 0, 0)  # the item grids' ground, the original's pure black, as under its empty-slot covers (ARC-UI-3342, owner 2026-11-26) and under the weapon and shield pictures (ARC-UI-3360)
EDGE: Colour = (62, 74, 98)


def draw(ident: str) -> pngio.Image:
    """The master of the fixture drawn by rule. Raises KeyError for an unknown id, and for a cut one (`label_parts.py`, `sheet_parts.py` and `window_parts.py` make those from the player's HD pack)."""
    fixture = BY_ID[ident]
    if fixture.cut:
        raise KeyError(f"{ident} is cut from the player's HD pack by label_parts.py, sheet_parts.py or window_parts.py, not drawn here")
    width, height = fixture.master
    image = pngio.Image.blank(width, height, (*GRID_GROUND, 255))
    x, y = round((fixture.width + LINE_AT) * width / fixture.width), round((fixture.height + LINE_AT) * height / fixture.height)
    line = bytes((*EDGE, 255))
    for row in range(height):
        for column in range(width):
            if x - 1 <= column < x + 1 or y - 1 <= row < y + 1:
                image.data[(row * width + column) * 4:(row * width + column) * 4 + 4] = line
    return image


def build(out: Path, only: set[str] | None = None) -> list[str]:
    out.mkdir(parents=True, exist_ok=True)
    written = []
    for fixture in FIXTURES:
        if fixture.cut or (only and fixture.ident not in only):
            continue
        pngio.write(out / f"{fixture.ident}.png", draw(fixture.ident))
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
    for fixture in (f for f in FIXTURES if not f.cut):
        first, second = draw(fixture.ident), draw(fixture.ident)
        if first.data != second.data or (first.w, first.h) != fixture.master:
            problems.append(f"{fixture.ident}: not deterministic or the wrong size")
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
    for ident in build(out, only):
        print("wrote", out / f"{ident}.png")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
