#!/usr/bin/env python3
"""ARC-GPAD-1377: measure the smallest text of the original's windows at the whole UI scale of a handheld screen.

The original composes every window inside its 800 x 600 screen; the viewer draws that screen at one whole factor (the largest that fits:
`AreaUiScale.ScreenFactor`). Valve's Deck Verified text rule on the 7 inch 1280 x 800 screen is 9 px at least, 12 px recommended (owner,
2026-11-12: whole scale only, smallest text >= 9 px). This script, per resolution and per window of the viewer, takes the original fonts the
window draws (read from the player's own installation, never copied anywhere), and prints three heights at the window's factor: the font's own
size (the number in its file name, `Garmond9Font` = 9; the gate), its line height (tallest glyph) and its capital height (ink rows of "H").
It also scans the viewer's Godot text sizes and fails on one under 9 px at the factor. Scope of the scan (the rest is not seen): the
literal or constant argument of `AddThemeFontSizeOverride("font_size", ...)` (an expression takes its smallest part; an expression with an
unresolved name is a failure, never a skip), `const int ...FontSize` constants of the files that draw with `DrawString`, and the inventory
and barter drawing's `Math.Max(floor, N * _scale)` sizes (the size at scale 1 is the larger of the floor and N). Labels without an override
(the theme's default size) and a `DrawString` with a computed size elsewhere are not scanned. A font without a number in its file name is
gated on its tallest glyph instead.

    python3 -B tools/research/ui_text_scale.py                       # 1280x800 and 1920x1080, the installation from ARCANUM_GAME_DIR
    python3 -B tools/research/ui_text_scale.py --size 1280x800 --game-dir PATH
    python3 -B tools/research/ui_text_scale.py --self-test

Exit 0 when every window fits and every text is at least 9 px, 1 otherwise, 2 on a usage or source error. Nothing is written.
"""
from __future__ import annotations

import argparse
import math
import os
import re
import sys
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools" / "hd_font"))

SCREEN = (800, 600)            # the original's screen, AreaGameBarLayout.ScreenWidth/ScreenHeight
MINIMUM_PX, PREFERRED_PX = 9, 12
DEFAULT_SIZES = ((1280, 800), (1920, 1080))


def text_only_factor(size: int, factor: int) -> int:
    """Mirror of `AreaUiScale.TextOnlyFactor`: the smallest whole multiplier that brings `size * factor` to the minimum (1 when it is there or unknown)."""
    shown = size * max(1, factor)
    return 1 if size <= 0 or shown >= MINIMUM_PX else -(-MINIMUM_PX // shown)


def screen_factor(width: float, height: float) -> int:
    """Mirror of `AreaUiScale.ScreenFactor` (tools/terrain-viewer/Core/AreaUiScale.cs)."""
    return max(1, math.floor(min(width / SCREEN[0], height / SCREEN[1])))


@dataclass(frozen=True)
class Window:
    name: str
    fonts: tuple[int, ...]       # interface ART numbers of the fonts the window draws
    source: str                  # the file whose constants name them (checked to mention every number)
    text_factor: bool = False    # a font under the minimum may take the text-only whole factor (ARC-UI-1428, the reading windows' fallback panel)
    substitute: int = 0          # a font under the minimum is drawn with this font art instead (ARC-UI-1462, Garmond9 483 in the reading windows' original look)


WINDOWS = (
    Window("main menu, single player, new game", (327,), "tools/terrain-viewer/Core/AreaMenuArt.cs"),
    Window("options window", (27, 229), "tools/terrain-viewer/Core/AreaMenuArt.cs"),
    Window("save and load list", (27,), "tools/terrain-viewer/Core/AreaSaveListArt.cs"),
    Window("character creation and pre-made browser", (27, 171), "tools/terrain-viewer/Core/AreaPremadeBrowserArt.cs"),
    Window("character sheet", (27, 229, 171), "tools/terrain-viewer/Core/AreaCharacterSheetArt.cs"),
    Window("character sheet spells page", (28,), "tools/terrain-viewer/Core/AreaCharacterSheetSpells.cs"),
    Window("character sheet skills and tech pages", (300, 301, 483), "src/Arcanum.World/AreaCharacterSheetPagesLayout.cs"),
    Window("message window, hover names, floaters", (27, 229), "tools/terrain-viewer/Core/AreaTextFonts.cs"),
    Window("combat panel", (230,), "tools/terrain-viewer/Core/AreaCombatPanelLayout.cs"),
    Window("dialog box", (229,), "tools/terrain-viewer/Core/AreaDialogBox.cs"),
    Window("schematic window", (371, 229, 370, 300), "src/Arcanum.World/AreaSchematicWindowLayout.cs"),
    Window("world map", (840,), "tools/terrain-viewer/Core/AreaWorldMapArt.cs"),
    # ARC-UI-1428: the reading windows (telegram 480, note 497, newspaper masthead 485/484/482 (815 alternate) and text 483/492/490, book and plaque
    # markup 478/479/28/37/171/229, Garmond6 481 for the telegram form and any markup that names it); the fallback panel draws them with its
    # fallback font at these sizes, a line under 9 px at the screen factor takes the text-only factor (`AreaReadingText.HeaderLinePx`); the
    # original-look window (ARC-UI-1462) draws such a line with Garmond9 (483, `AreaReadingText.ShownFont`).
    Window("reading windows (telegram, newspaper, note, book, plaque)",
           (480, 481, 482, 483, 484, 485, 815, 492, 497, 490, 478, 479, 28, 37, 171, 229),
           "tools/terrain-viewer/Core/AreaReadingText.cs", text_factor=True, substitute=483),
)

# A line carrying this marker is not text a player reads (the save list's transparent keyboard field keeps size 1).
HIDDEN_MARKER = "ui-text-scale: hidden"
FONT_SIZE = re.compile(r'AddThemeFontSizeOverride\(\s*"(?:normal_)?font_size"\s*,\s*([^)]*?)\s*\)')
FONT_CONSTANT = re.compile(r'\b(?:private\s+|public\s+|internal\s+)?const\s+int\s+([^;]*(?:Font|FontSize)[^;]*);')


@dataclass(frozen=True)
class FontFact:
    number: int
    file: str
    nominal: int | None          # the size in the file name, None when it has none
    line: int                    # tallest glyph
    capital: int                 # ink rows of "H"


def nominal_size(file: str) -> int | None:
    """The size a font's file name states (`Garmond9Font.ART` -> 9, `morph15font.art` -> 15); None without one or above 60 (`Swiss921Font`)."""
    match = re.search(r"(\d+)font(?:\.art)?$", file.lower())
    return int(match.group(1)) if match and int(match.group(1)) <= 60 else None


def ink_rows(width: int, height: int, red: bytes) -> int:
    return sum(1 for row in range(height) if any(red[row * width:(row + 1) * width])) if width and height else 0


def font_fact(font) -> FontFact:
    frames = font.frames
    capital = frames[ord("H") - 31] if len(frames) > ord("H") - 31 else None
    return FontFact(font.number, font.file, nominal_size(font.file), max(frame.height for frame in frames),
                    ink_rows(capital.width, capital.height, capital.red) if capital else 0)


def resolve_size(argument: str, constants: dict[str, int]) -> int | None:
    """The smallest size an expression can show: its integer literals and the constants it names; None when it names something unresolved."""
    names = re.findall(r"[A-Za-z_]\w*", argument)
    values = [int(number) for number in re.findall(r"(?<![\w.])\d+(?![\w.])", argument)]
    for name in names:
        if name in constants:
            values.append(constants[name])
        elif name not in {"text", "Font", "AreaMessageFont", "Title", "Number", "Math", "Round", "int"}:
            return None
    return min(values) if values else None


INVENTORY_FLOOR = re.compile(r"Math\.Max\(\s*(\d+)\s*,\s*\(int\)\s*(?:Math\.Round\()?\(?\s*(\d+)\s*\*\s*_scale")


def scan_label_sizes(root: Path = ROOT) -> tuple[list[tuple[str, int, int]], list[str]]:
    """((file, line, size at scale 1) of every Godot text size the scan covers, unresolved sizes as messages)."""
    found, unresolved = [], []
    for path in sorted((root / "tools" / "terrain-viewer" / "Godot").glob("*.cs")):
        if any(word in path.name for word in ("Smoke", "Test")):
            continue
        text = path.read_text(encoding="utf-8")
        constants: dict[str, int] = {}
        for declaration in FONT_CONSTANT.finditer(text):
            for name, value in re.findall(r"(\w+)\s*=\s*(\d+)", declaration.group(1)):
                constants[name] = int(value)
        relative = path.relative_to(root).as_posix()
        draws = "DrawString(" in text
        for index, line in enumerate(text.splitlines(), 1):
            if HIDDEN_MARKER in line:
                continue
            for match in FONT_SIZE.finditer(line):
                argument = match.group(1).strip()
                value = MINIMUM_PX if "AreaUiScale.MinimumTextPx" in argument or "AreaReadingText.HeaderLinePx(" in argument else resolve_size(argument, constants)
                if value is None:
                    unresolved.append(f"{relative}:{index} font size '{argument}' is not resolvable")
                else:
                    found.append((relative, index, value))
            for floor, base in INVENTORY_FLOOR.findall(line):
                found.append((relative, index, max(int(floor), int(base))))
            if draws and (const := re.search(r"const\s+int\s+FontSize\s*=\s*(\d+)", line)):
                found.append((relative, index, int(const.group(1))))
    return found, unresolved


def check_sources(root: Path = ROOT) -> list[str]:
    """The window table must stay true to the code: every cited file mentions every font number it is listed with."""
    problems = []
    for window in WINDOWS:
        path = root / window.source
        if not path.is_file():
            problems.append(f"{window.name}: {window.source} does not exist")
            continue
        text = path.read_text(encoding="utf-8")
        for number in window.fonts:
            if not re.search(rf"\b{number}\b", text):
                problems.append(f"{window.name}: font art {number} is not named in {window.source}")
    return problems


def report(sizes, facts: dict[int, FontFact], labels, minimum: int = MINIMUM_PX, unresolved=()) -> tuple[list[str], list[str]]:
    lines, failures = [], []
    for width, height in sizes:
        factor = screen_factor(width, height)
        fits = width >= SCREEN[0] and height >= SCREEN[1]
        lines.append(f"== {width}x{height}: whole factor {factor}, the original 800x600 screen {'fits' if fits else 'does NOT fit'} "
                     f"at {SCREEN[0] * factor}x{SCREEN[1] * factor}")
        if not fits:
            failures.append(f"{width}x{height}: the 800x600 screen does not fit")
        lines.append(f"   {'window':<42} {'font (art, file)':<34} {'size':>4} {'line':>4} {'cap':>4}  gate")
        for window in WINDOWS:
            for number in window.fonts:
                fact = facts[number]
                size = fact.nominal if fact.nominal is not None else fact.line
                shown = size * factor
                text = text_only_factor(size, factor) if window.text_factor and fact.nominal is not None and shown < minimum else 1
                # ARC-UI-1462: the original-look window draws a font under the minimum with the substitute font (`AreaReadingText.ShownFont`).
                swap = facts[window.substitute] if window.substitute and fact.nominal is not None and shown < minimum else None
                swap_shown = (swap.nominal if swap.nominal is not None else swap.line) * factor if swap else 0
                ok = swap_shown >= minimum if swap else shown * text >= minimum
                if swap:
                    verdict = f"pass (substitute {swap.file}, {swap_shown} px)" if ok else "FAIL"
                    shown = swap_shown if ok else shown
                elif text > 1:
                    verdict = f"pass (text x{text} = {shown * text} px)"
                else:
                    verdict = "PASS" if ok and shown >= PREFERRED_PX else "pass (below 12 px)" if ok else "FAIL"
                if not ok:
                    failures.append(f"{width}x{height}: {window.name}: {fact.file} is {shown} px")
                lines.append(f"   {window.name[:42]:<42} {number:>3} {fact.file:<30} {shown:>4} {fact.line * factor:>4} {fact.capital * factor:>4}  {verdict}")
        failures.extend(f"{width}x{height}: {message}" for message in unresolved)
        for file, line, value in labels:
            shown = value * factor
            if shown < minimum:
                failures.append(f"{width}x{height}: {file}:{line} Godot label font size {value} -> {shown} px")
        seen = [value for _, _, value in labels]
        lines.append(f"   Godot text sizes: {len(seen)} read, {len(unresolved)} unresolved, smallest {min(seen) * factor if seen else 'none'} px at x{factor}")
    return lines, failures


def parse_size(text: str) -> tuple[int, int]:
    match = re.fullmatch(r"(\d{3,5})x(\d{3,5})", text)
    if not match:
        raise argparse.ArgumentTypeError(f"{text!r} is not WIDTHxHEIGHT")
    return int(match.group(1)), int(match.group(2))


def load_facts(game_dir: Path, numbers: set[int]) -> dict[int, FontFact]:
    import artfont

    content = artfont.Content(artfont.discover_sources(game_dir, "Arcanum"))
    return {number: font_fact(artfont.load_font(content, number)) for number in sorted(numbers)}


def self_test() -> int:
    assert screen_factor(1280, 800) == 1 and screen_factor(1600, 1200) == 2 and screen_factor(1599, 1200) == 1 and screen_factor(1, 1) == 1
    assert screen_factor(1920, 1080) == 1 and screen_factor(3840, 2160) == 3
    assert nominal_size("Garmond9Font.ART") == 9 and nominal_size("morph15font.art") == 15 and nominal_size("Swiss921Font.ART") is None
    assert nominal_size("NewsIconsFont.ART") is None
    assert ink_rows(2, 3, bytes([0, 0, 1, 0, 0, 0])) == 1
    synthetic = {n: FontFact(n, "SyntheticFont.ART", None, 10, 7) for window in WINDOWS for n in window.fonts}
    lines, failures = report([(1280, 800)], synthetic, [("x.cs", 1, 10)])
    assert not failures and any("PASS" in line or "pass" in line for line in lines)
    synthetic[483] = FontFact(483, "Garmond8Font.ART", 8, 10, 7)
    _, failures = report([(1280, 800)], synthetic, [])
    # the sheet's font and, as the substitute that is itself under the minimum, the reading windows' (ARC-UI-1462)
    assert len(failures) == 2 and all("Garmond8Font" in failure for failure in failures)
    _, failures = report([(1280, 800)], {**synthetic, 483: FontFact(483, "Garmond8Font.ART", 8, 10, 7)}, [("a.cs", 5, 8)])
    assert any("a.cs:5" in failure for failure in failures)
    _, failures = report([(640, 480)], {n: FontFact(n, "SyntheticFont.ART", 12, 12, 9) for window in WINDOWS for n in window.fonts}, [])
    assert any("does not fit" in failure for failure in failures)
    assert text_only_factor(8, 1) == 2 and text_only_factor(6, 1) == 2 and text_only_factor(9, 1) == 1 and text_only_factor(8, 2) == 1 and text_only_factor(0, 1) == 1
    reading = {n: FontFact(n, "SyntheticFont.ART", 12, 12, 9) for window in WINDOWS for n in window.fonts}
    reading[482] = FontFact(482, "Garmond8Font.ART", 8, 10, 7)
    reading[481] = FontFact(481, "Garmond6Font.ART", 6, 7, 5)
    lines, failures = report([(1280, 800), (1920, 1080)], reading, [])
    assert not failures and sum("substitute Synthetic" in line for line in lines) >= 2, failures
    # without the substitute font the same lines would take the text-only factor of the fallback panel, and without both they fail
    assert any(window.substitute for window in WINDOWS)
    # a window without the text factor still fails on the same font
    assert any(window.text_factor for window in WINDOWS) and not all(window.text_factor for window in WINDOWS)
    scan, unresolved = scan_label_sizes()
    assert scan and not unresolved, unresolved
    assert all(len(item) == 3 for item in scan)
    assert resolve_size("text.Font == AreaMessageFont.Title ? TitleFontSize : TextFontSize", {"TitleFontSize": 13, "TextFontSize": 12}) == 12
    assert resolve_size("someTable[i]", {}) is None and resolve_size("9", {}) == 9
    assert INVENTORY_FLOOR.findall("var size = Math.Max(8, (int)(11 * _scale));") == [("8", "11")]
    assert INVENTORY_FLOOR.findall("var size = Math.Max(6, (int)Math.Round(12 * _scale));") == [("6", "12")]
    _, failures = report([(1280, 800)], synthetic, [], unresolved=["x.cs:1 font size 'q' is not resolvable"])
    assert any("not resolvable" in failure for failure in failures)
    assert not check_sources(), check_sources()
    print("ui_text_scale: self-test passed")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--size", type=parse_size, action="append", help="WIDTHxHEIGHT of the window (default 1280x800 and 1920x1080)")
    parser.add_argument("--game-dir", help="the installed game folder (default: ARCANUM_GAME_DIR, then the launcher's default)")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args(argv)
    if args.self_test:
        return self_test()
    problems = check_sources()
    if problems:
        print("ui_text_scale: the window table is out of date:\n  " + "\n  ".join(problems), file=sys.stderr)
        return 2
    import hd_font

    game_dir = Path(args.game_dir or os.environ.get("ARCANUM_GAME_DIR") or hd_font.DEFAULT_GAME_DIR).expanduser()
    try:
        facts = load_facts(game_dir, {number for window in WINDOWS for number in window.fonts})
    except Exception as error:  # noqa: BLE001 - a missing installation or font is a usage error, reported once
        print(f"ui_text_scale: cannot read the original fonts: {error}", file=sys.stderr)
        return 2
    labels, unresolved = scan_label_sizes()
    lines, failures = report(args.size or DEFAULT_SIZES, facts, labels, unresolved=unresolved)
    print("\n".join(lines))
    print(f"ui_text_scale: {'FAIL' if failures else 'PASS'} ({len(failures)} failures)")
    for failure in failures:
        print(f"  FAIL {failure}")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
