"""ARC-UI-3206: tests of the small button kit tool on synthetic frames (no art)."""
from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from . import build_kit, button_kit, pngio, prompt_library
from . import small_kit as sk

BASE = (60, 60, 40)
RED = (200, 20, 30)
DARK_RED = (90, 0, 40)


def frame(units: tuple[int, int], cells=(), colour=RED, plate=BASE, dither=None) -> pngio.Image:
    image = pngio.Image(units[0], units[1], bytearray(units[0] * units[1] * 4))
    for i in range(units[0] * units[1]):
        image.data[i * 4:i * 4 + 4] = bytes((*plate, 255))
    for n, (x, y) in enumerate(cells):
        c = dither if dither is not None and (x + y) % 2 else colour
        image.data[(y * units[0] + x) * 4:(y * units[0] + x) * 4 + 4] = bytes((*c, 255))
    return image


def plus(cx: int, cy: int, arm: int = 4, width: int = 3) -> list[tuple[int, int]]:
    half = width // 2
    cells = {(cx + dx, cy + dy) for dx in range(-arm, arm + 1) for dy in range(-half, half + 1)}
    return sorted(cells | {(cx + dy, cy + dx) for dx in range(-arm, arm + 1) for dy in range(-half, half + 1)})


def button(units=(20, 20), master=(72, 72), hue="red", interior=(3, 3, 16, 16), family="stone", **extra) -> sk.Small:
    return sk.Small("probe", 1, "1-probe", family, units, master, hue, interior, **extra)


class SymbolMaskTest(unittest.TestCase):
    def test_plus_is_found_with_its_corners_and_dither_holes_closed(self):
        b = button()
        cells = plus(10, 10)
        mask = sk.symbol_mask(frame(b.units, cells, dither=DARK_RED), b)
        self.assertEqual(sum(mask), len(cells))  # the concave corners of the plus stay corners
        grid = [(i % 20, i // 20) for i in range(400) if mask[i]]
        self.assertEqual(sorted(grid), cells)

    def test_a_hole_in_the_dither_is_filled_but_a_corner_is_not(self):
        b = button()
        cells = [c for c in plus(10, 10) if c != (10, 10)]
        mask = sk.symbol_mask(frame(b.units, cells), b)
        self.assertTrue(mask[10 * 20 + 10])
        self.assertFalse(mask[8 * 20 + 8])

    def test_cells_outside_the_interior_are_not_the_symbol(self):
        b = button()
        mask = sk.symbol_mask(frame(b.units, plus(10, 10) + [(1, 1), (18, 18)]), b)
        self.assertFalse(mask[1 * 20 + 1] or mask[18 * 20 + 18])

    def test_speck_is_dropped(self):
        b = button()
        mask = sk.symbol_mask(frame(b.units, plus(10, 10) + [(15, 4)]), b)
        self.assertFalse(mask[4 * 20 + 15])

    def test_light_hue_reads_a_grey_symbol_on_a_dark_plate(self):
        b = button(hue="light")
        mask = sk.symbol_mask(frame(b.units, [(x, 10) for x in range(6, 14)], colour=(170, 170, 160)), b)
        self.assertEqual(sum(mask), 8)

    def test_hd_frame_scale(self):
        b = button()
        hd = frame((80, 80), [(x, y) for x in range(32, 48) for y in range(36, 44)])
        mask = sk.symbol_mask(hd, b)
        self.assertEqual(sum(mask), 16 * 8)
        self.assertEqual(sum(sk.coarse(mask, 80, 80, 4)), 4 * 2)


class ColourTest(unittest.TestCase):
    def test_dither_of_two_tones_is_their_mean(self):
        b = button()
        cells = plus(10, 10)
        image = frame(b.units, cells, colour=(200, 40, 40), dither=(120, 20, 60))
        mask = sk.symbol_mask(image, b)
        colour = sk.symbol_colour(image, mask, "red")
        for k in range(3):
            self.assertGreater(colour[k], min(120, 200, 20, 40, 60) - 1)
        self.assertTrue(120 < colour[0] < 200)

    def test_ground_showing_through_the_dither_is_left_out(self):
        b = button(hue="green")
        cells = [(x, y) for x in range(6, 14) for y in range(6, 14)]
        image = frame(b.units, cells, colour=(30, 230, 40), dither=(0, 10, 0))
        colour = sk.symbol_colour(image, sk.symbol_mask(image, b), "green")
        self.assertGreater(colour[1], 200)   # the bright half, not the mean with the black cells


class SilhouetteTest(unittest.TestCase):
    def test_solid_inside_clear_outside_and_smooth_between(self):
        b = button()
        mask = sk.symbol_mask(frame(b.units, plus(10, 10)), b)
        alpha = sk.silhouette(mask, b.units, b.master)
        w, h = b.master
        self.assertEqual(alpha[(h // 2) * w + w // 2], 1.0)
        self.assertEqual(alpha[2 * w + 2], 0.0)
        self.assertTrue(any(0.0 < a < 1.0 for a in alpha))   # an anti-aliased edge, no stair

    def test_the_area_stays_the_masks(self):
        b = button()
        cells = plus(10, 10)
        mask = sk.symbol_mask(frame(b.units, cells), b)
        alpha = sk.silhouette(mask, b.units, b.master)
        area = sum(alpha) / (b.master[0] / b.units[0]) / (b.master[1] / b.units[1])
        self.assertLess(abs(area - len(cells)) / len(cells), 0.08)

    def test_triangle_is_exact_and_straight(self):
        b = button(family="strip")
        cells = [(x, y) for y in range(6, 13) for x in range(10 - (y - 6), 11 + (y - 6))]
        mask = sk.symbol_mask(frame(b.units, cells), b)
        alpha = sk.triangle_alpha(mask, b.units, b.master, True)
        w, h = b.master
        sx, sy = w / b.units[0], h / b.units[1]
        # the apex is half a cell above the top row; the width at the middle of the height is half the base
        def width_at(row: int) -> float:
            return sum(alpha[row * w:(row + 1) * w]) / sx
        top = 5.5 * sy
        base = width_at(int(12.9 * sy))
        mid = width_at(int((top / sy + 13 - top / sy) / 2 * sy + 1.5 * sy))
        self.assertGreater(base, mid)
        self.assertEqual(sum(alpha[: int(5 * sy) * w]), 0.0)   # nothing above the apex


class PartsTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.frames = Path(self.tmp.name)
        self.b = button(frames={"idle": 0, "pressed": 1, "hover": 2})
        folder = self.frames / self.b.folder
        folder.mkdir()
        cells = plus(10, 10)
        idle = frame(self.b.units, cells, colour=(120, 10, 30))
        glow_cells = {(x + dx, y + dy) for x, y in cells for dx in (-1, 0, 1) for dy in (-1, 0, 1)}
        hover = frame(self.b.units, cells, colour=(250, 40, 40))
        pressed = frame(self.b.units, cells, colour=(250, 120, 120), plate=(30, 30, 20))
        for n, image in enumerate((idle, pressed, hover)):
            if n == 2:   # a glow round the lit symbol
                for x, y in glow_cells:
                    o = (y * 20 + x) * 4
                    if (x, y) not in cells:
                        image.data[o:o + 3] = bytes((120, 50, 40))
            pngio.write(folder / f"f{n}.png", image)

    def test_glyph_and_overlays_exist_at_the_master_size(self):
        parts = sk.button_parts(self.frames, self.b)
        self.assertEqual(set(parts), {"glyph.probe", "plate.probe_hover", "plate.probe_pressed"})
        for image in parts.values():
            self.assertEqual((image.w, image.h), self.b.master)

    def test_the_glyph_is_solid_in_the_symbol_colour_with_an_engraving(self):
        glyph = sk.button_parts(self.frames, self.b)["glyph.probe"]
        w, h = self.b.master
        centre = glyph.pixel(w // 2, h // 2)
        self.assertEqual(centre[3], 255)
        self.assertEqual(centre[:3], (120, 10, 30))
        # the light rim lies at the upper left of the symbol: a cell column at its left edge is lighter than the body
        row = [glyph.pixel(x, h // 2) for x in range(w)]
        left = next(p for p in row if p[3] > 200)
        self.assertGreater(sum(left[:3]), sum(centre[:3]))

    def test_hover_lights_the_symbol_and_glows(self):
        hover = sk.button_parts(self.frames, self.b)["plate.probe_hover"]
        w, h = self.b.master
        self.assertEqual(hover.pixel(w // 2, h // 2)[:3], (250, 40, 40))
        self.assertTrue(any(0 < hover.pixel(x, h // 2)[3] < 255 for x in range(w)))   # the glow is a soft layer

    def test_pressed_darkens_the_plate(self):
        pressed = sk.button_parts(self.frames, self.b)["plate.probe_pressed"]
        self.assertTrue(any(pressed.data[i] == 0 and 0 < pressed.data[i + 3] < 255 for i in range(0, len(pressed.data), 4)))

    def test_a_missing_symbol_is_an_error(self):
        folder = self.frames / self.b.folder
        pngio.write(folder / "f0.png", frame(self.b.units))
        with self.assertRaises(sk.SmallError):
            sk.button_parts(self.frames, self.b)

    def test_a_frame_of_the_wrong_size_is_an_error(self):
        folder = self.frames / self.b.folder
        pngio.write(folder / "f0.png", frame((10, 10)))
        with self.assertRaises(sk.SmallError):
            sk.button_parts(self.frames, self.b)


class RingTest(unittest.TestCase):
    def make_local(self, root: Path) -> None:
        masters = root / "masters"
        masters.mkdir(parents=True)
        dome = pngio.Image(40, 40, bytearray(40 * 40 * 4))
        ring = pngio.Image(80, 80, bytearray(80 * 80 * 4))
        for y in range(80):
            for x in range(80):
                r = ((x + 0.5 - 40) ** 2 + (y + 0.5 - 40) ** 2) ** 0.5
                if 28 <= r <= 40:
                    t = (40 - r) / 12
                    ring.data[(y * 80 + x) * 4:(y * 80 + x) * 4 + 4] = bytes((int(90 + 100 * t), int(70 + 60 * t), 30, 255))
        for y in range(40):
            for x in range(40):
                if ((x + 0.5 - 20) ** 2 + (y + 0.5 - 20) ** 2) ** 0.5 <= 20:
                    dome.data[(y * 40 + x) * 4:(y * 40 + x) * 4 + 4] = bytes((40, 45, 12, 255))
        pngio.write(masters / f"{sk.DOME_SOURCE}.png", dome)
        pngio.write(masters / f"{sk.RING_SOURCE}.png", ring)

    def test_round_plate_has_the_dome_the_ring_and_clear_corners(self):
        with tempfile.TemporaryDirectory() as tmp:
            local = Path(tmp)
            self.make_local(local)
            b = sk.button_named("skiladdbut")
            plate = sk.round_plate(b, local)
            w, h = b.master
            self.assertEqual((plate.w, plate.h), (w, h))
            self.assertEqual(plate.pixel(0, 0)[3], 0)
            self.assertEqual(plate.pixel(w // 2, h // 2)[:3], (40, 45, 12))   # the dome in the middle
            edge = plate.pixel(w // 2, 4)   # inside the ring, the brass
            self.assertGreater(edge[0], edge[2] + 30)
            ring_px = round(sk.RING_UNITS * (w / b.units[0] + h / b.units[1]) / 2)
            inner = plate.pixel(w // 2, ring_px + 3)
            self.assertEqual(inner[:3], (40, 45, 12))   # the ring is as wide as the original's, not wider

    def test_ring_profile_needs_a_real_ring(self):
        with self.assertRaises(sk.SmallError):
            sk.ring_profile(pngio.Image(10, 10, bytearray(400)))


class TableTest(unittest.TestCase):
    def test_every_button_matches_its_manifest_row(self):
        manifest = {r["id"]: r for r in json.loads(sk.MANIFEST.read_text(encoding="utf-8"))["parts"]}
        for b in sk.SMALL:
            row = manifest[f"plate.{b.name}"]
            self.assertEqual(row["size"], list(b.master), b.name)
            self.assertEqual(row["source_arts"], [str(b.art)], b.name)
            self.assertEqual(b.folder, f"{b.art}-{row_name(b)}", b.name)

    def test_names_are_unique_and_every_codex_plate_has_an_item(self):
        names = [b.name for b in sk.SMALL]
        self.assertEqual(len(names), len(set(names)))
        fitted = {n for item in sk.ITEMS for n in item.parts}
        self.assertEqual(fitted, {b.name for b in sk.SMALL if b.plate == "codex"})
        for b in sk.SMALL:
            if b.plate == "derived":
                self.assertEqual(sk.button_named(b.base).plate, "codex")

    def test_items_are_in_the_prompt_library_at_the_asked_size(self):
        library = json.loads((Path(prompt_library.__file__).parent / "prompts" / "library.json").read_text(encoding="utf-8"))
        entries = {p["id"]: p for p in library["prompts"]}
        self.assertEqual(library["batches"][sk.BATCH]["prompts"], [i.prompt for i in sk.ITEMS])
        for item in sk.ITEMS:
            self.assertEqual(entries[item.prompt]["size"], list(item.size), item.prompt)
            self.assertEqual(entries[item.prompt]["batch"], "B-BUTTONS-SMALL")
            self.assertTrue((Path(prompt_library.__file__).parent / "prompts" / entries[item.prompt]["file"]).is_file())

    def test_item_aspect_is_the_masters(self):
        for item in sk.ITEMS:
            for name in item.parts:
                b = sk.button_named(name)
                self.assertLess(abs(item.size[0] / item.size[1] / (b.master[0] / b.master[1]) - 1.0), 0.14, (item.prompt, name))   # the masters snap to the 6 px grid, so a few % off the original aspect

    def test_build_kit_rows_cover_every_glyph_and_overlay(self):
        plates = {f"plate.{b.name}": {"anchor": "window", "used_by": ["G01"]} for b in sk.SMALL}
        rows = {r["id"]: r for r in build_kit.small_parts(plates)}
        for b in sk.SMALL:
            self.assertIn(f"glyph.{b.name}", rows)
            for state in sk.STATES:
                self.assertEqual(f"plate.{b.name}_{state}" in rows, b.frames.get(state) is not None, (b.name, state))
        self.assertTrue(all(r["status"] == "master" and r["provenance"].startswith("rule:small_kit:") for r in rows.values()))

    def test_self_test_passes(self):
        self.assertEqual(sk.self_test(), 0)


def row_name(b: sk.Small) -> str:
    return b.folder.split("-", 1)[1]


class ColourPredicateTest(unittest.TestCase):
    def test_hues(self):
        self.assertTrue(sk.is_symbol("red", 200, 20, 30, 0))
        self.assertFalse(sk.is_symbol("red", 60, 60, 40, 0))
        self.assertTrue(sk.is_symbol("green", 30, 44, 1, 0))
        self.assertFalse(sk.is_symbol("green", 144, 103, 49, 0))   # brass
        with self.assertRaises(sk.SmallError):
            sk.is_symbol("blue", 0, 0, 255, 0)


if __name__ == "__main__":
    unittest.main()
