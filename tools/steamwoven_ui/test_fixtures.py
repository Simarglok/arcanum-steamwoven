"""ARC-UI-3098: the rule-drawn fixtures of the kit: sizes, determinism, the manifest, the drawn shapes (no original pixel anywhere)."""
from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from tools.steamwoven_ui import build_kit, fixtures, kit_check, pngio


class FixtureTests(unittest.TestCase):
    def test_self_test_passes(self) -> None:
        self.assertEqual(fixtures.self_test(), 0)

    def test_sizes_are_the_census_rule(self) -> None:
        for units in (17, 32, 61, 324, 388, 506):
            self.assertEqual(fixtures.snap(units), kit_check.snap(units))
        self.assertEqual(BY("fixture.pack_pit").master, (1164, 1398))
        self.assertEqual(BY("grid.inventory_cells").master, (114, 114))

    def test_every_fixture_is_a_placed_part_of_the_manifest(self) -> None:
        manifest = {p["id"]: p for p in kit_check.load_manifest()["parts"]}
        for fixture in fixtures.FIXTURES:
            row = manifest[fixture.ident]
            self.assertEqual((row["size"], row["size_basis"], row["source_arts"]), (list(fixture.master), "placed", []), fixture.ident)
            self.assertEqual(row["used_by"], list(fixture.windows), fixture.ident)
        self.assertEqual(sorted(p["id"] for p in manifest.values() if p["size_basis"] == "placed"), sorted(f.ident for f in fixtures.FIXTURES))
        self.assertEqual(manifest["grid.inventory_cells"]["kind"], "grid")
        self.assertEqual(manifest["fixture.mt_ruler"]["kind"], "fixture")

    def test_the_manifest_rows_come_from_the_table(self) -> None:
        self.assertEqual(build_kit.fixture_part(BY("fixture.count_pit"))["size"], [222, 60])

    def test_drawing_is_deterministic_and_draws(self) -> None:
        for variant in fixtures.VARIANTS:
            for fixture in (f for f in fixtures.FIXTURES if not f.cut):
                first = fixtures.draw(fixture.ident, variant)
                self.assertEqual((first.w, first.h), fixture.master, fixture.ident)
                self.assertTrue(any(first.data[3::4]), f"{fixture.ident} {variant}: nothing drawn")
                self.assertEqual(first.data, fixtures.draw(fixture.ident, variant).data, f"{fixture.ident} {variant}: not deterministic")

    def test_brass_is_the_default_look(self) -> None:
        self.assertEqual(fixtures.draw("fixture.count_pit").data, fixtures.draw("fixture.count_pit", "brass").data)
        self.assertNotEqual(fixtures.draw("fixture.count_pit").data, fixtures.draw("fixture.count_pit", "flat").data)

    def test_the_two_looks_differ_in_the_trim_only(self) -> None:
        flat, brass = fixtures.draw("fixture.pack_pit", "flat"), fixtures.draw("fixture.pack_pit", "brass")
        self.assertNotEqual(flat.data, brass.data)
        centre = (flat.h // 2 * flat.w + flat.w // 2) * 4
        self.assertEqual(flat.data[centre:centre + 4], brass.data[centre:centre + 4], "the pit's black is the same")
        self.assertEqual(flat.pixel(0, 0)[3], 255)

    def test_shapes_sit_where_the_original_has_them(self) -> None:
        heart = fixtures.draw("fixture.health_disc")
        r, g, b, a = heart.pixel(round(heart.w / 2), round(heart.h * 0.55))
        self.assertTrue(r > 150 and g < 60 and b < 60 and a == 255, "the heart is red at the disc's centre")
        cross = fixtures.draw("fixture.fatigue_disc")
        r, g, b, _ = cross.pixel(round(cross.w / 2), round(cross.h / 2))
        self.assertTrue(b > 150 and r < 80, "the cross is blue at the disc's centre")
        corner = fixtures.draw("fixture.weapon_frame")
        self.assertEqual(corner.pixel(corner.w - 1, 2)[3], 0, "the quarter frame leaves its far top corner clear")
        ruler = fixtures.draw("fixture.mt_ruler")
        dark = [y for y in range(ruler.h) if ruler.pixel(round(ruler.w * 0.28), y)[0] < 90]
        self.assertGreater(len(dark), 30, "the ruler has its ticks")
        sheet = fixtures.draw("grid.schematic")
        self.assertEqual(sheet.pixel(5, 5)[3], 0, "the ruling leaves the paper clear between its lines")

    def test_the_item_grids_lines_lie_where_the_original_has_them(self) -> None:
        """ARC-UI-3342: the original's lines are one unit wide on the last column and row of units of a cell, and none lies before a grid's first cell."""
        cell = fixtures.draw("grid.inventory_cells")
        across = _line_centres([cell.pixel(x, cell.h // 2)[:3] for x in range(cell.w)], cell.w / 32)
        down = _line_centres([cell.pixel(cell.w // 2, y)[:3] for y in range(cell.h)], cell.h / 32)
        for centres in (across, down):
            self.assertEqual(len(centres), 1, centres)
            self.assertAlmostEqual(centres[0], 31.5, delta=0.1)
        self.assertEqual(cell.pixel(cell.w // 2, cell.h // 2), (0, 0, 0, 255), "the original's black ground")
        # The grid pits share that ground and draw no line of their own round the grid: nothing of the line's colour past their trim.
        for ident in ("fixture.pack_pit", "fixture.doll_pit", "fixture.holder_pit_loot", "fixture.holder_pit_barter"):
            for variant in fixtures.VARIANTS:
                pit, part = fixtures.draw(ident, variant), BY(ident)
                self.assertEqual(pit.pixel(pit.w // 2, pit.h // 2), (0, 0, 0, 255), f"{ident} {variant}: the grid's ground")
                sx, sy = pit.w / part.width, pit.h / part.height
                row = _line_centres([pit.pixel(x, pit.h // 2)[:3] for x in range(pit.w)], sx)
                column = _line_centres([pit.pixel(pit.w // 2, y)[:3] for y in range(pit.h)], sy)
                for found, size in ((row, part.width), (column, part.height)):
                    self.assertTrue(all(c < 1 or c > size - 1 for c in found), f"{ident} {variant}: {found}")

    def test_build_writes_the_masters(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            written = fixtures.build(Path(folder), "flat", {"fixture.count_pit", "grid.inventory_cells"})
            self.assertEqual(sorted(written), ["fixture.count_pit", "grid.inventory_cells"])
            image = pngio.read(Path(folder) / "fixture.count_pit.png")
            self.assertEqual((image.w, image.h), BY("fixture.count_pit").master)


def BY(ident: str) -> fixtures.Fixture:
    return fixtures.BY_ID[ident]


def _line_centres(pixels: list, scale: float) -> list[float]:
    """The centres, in units, of the runs of the grid line's colour along a row or column of pixels at `scale` pixels per unit."""
    centres, start = [], None
    for i, rgb in enumerate([*pixels, None]):
        inside = rgb is not None and tuple(rgb) == fixtures.EDGE
        if inside and start is None:
            start = i
        elif not inside and start is not None:
            centres.append((start + i) / 2 / scale)
            start = None
    return centres


if __name__ == "__main__":
    unittest.main()
