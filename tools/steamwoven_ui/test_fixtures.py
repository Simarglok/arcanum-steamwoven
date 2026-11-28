"""ARC-UI-3098: the fixtures of the kit: sizes, the manifest, and the one drawn by rule, the item grids' cell well (no original pixel anywhere). The cut ones have their cutters' tests."""
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
        self.assertEqual(BY("fixture.pack_pit").master, (1194, 1428))
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
        self.assertEqual(build_kit.fixture_part(BY("fixture.count_pit"))["size"], [228, 72])

    def test_only_the_cell_well_is_drawn_by_rule(self) -> None:
        self.assertEqual([f.ident for f in fixtures.FIXTURES if not f.cut], ["grid.inventory_cells"])
        first = fixtures.draw("grid.inventory_cells")
        self.assertEqual((first.w, first.h), BY("grid.inventory_cells").master)
        self.assertEqual(first.data, fixtures.draw("grid.inventory_cells").data, "not deterministic")
        with self.assertRaises(KeyError):
            fixtures.draw("fixture.pack_pit")

    def test_the_item_grids_lines_lie_where_the_original_has_them(self) -> None:
        """ARC-UI-3342: the original's lines are one unit wide on the last column and row of units of a cell, and none lies before a grid's first cell."""
        cell = fixtures.draw("grid.inventory_cells")
        across = _line_centres([cell.pixel(x, cell.h // 2)[:3] for x in range(cell.w)], cell.w / 32)
        down = _line_centres([cell.pixel(cell.w // 2, y)[:3] for y in range(cell.h)], cell.h / 32)
        for centres in (across, down):
            self.assertEqual(len(centres), 1, centres)
            self.assertAlmostEqual(centres[0], 31.5, delta=0.1)
        self.assertEqual(cell.pixel(cell.w // 2, cell.h // 2), (0, 0, 0, 255), "the original's black ground")
    def test_every_grid_s_lines_meet_the_original_s(self) -> None:
        """ARC-UI-3342: the cell well laid as the viewer lays it gives the first and last grey line of the original's art, per grid, read from the player's arts 221, 223, 234 and 235 (numbers only)."""
        cell = fixtures.draw("grid.inventory_cells")
        (across,), (down,) = (_line_centres([cell.pixel(x, cell.h // 2)[:3] for x in range(cell.w)], cell.w / 32),
                              _line_centres([cell.pixel(cell.w // 2, y)[:3] for y in range(cell.h)], cell.h / 32))
        # (grid, corner, cells, the viewer draws the far edges, the original's first and last line column, first and last line row: window unit indexes)
        for name, (x0, y0), (columns, rows), far, xs, ys in (("pack", (368, 8), (10, 12), True, (399, 687), (39, 391)), ("doll", (23, 107), (10, 9), False, (54, 310), (138, 362)),
                                                             ("loot", (8, 136), (10, 8), True, (39, 327), (167, 391)), ("barter", (8, 168), (10, 7), True, (39, 327), (199, 391))):
            for origin, count, centre, (first, last) in ((x0, columns, across, xs), (y0, rows, down, ys)):
                laid = [origin + 32 * k + centre for k in range(count if far else count - 1)]
                original = [unit + 0.5 for unit in range(first, last + 1, 32)]
                self.assertEqual(len(laid), len(original), name)
                for ours, theirs in zip(laid, original):
                    self.assertAlmostEqual(ours, theirs, delta=0.1, msg=name)

    def test_build_writes_the_masters_it_draws(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            written = fixtures.build(Path(folder), {"fixture.count_pit", "grid.inventory_cells"})
            self.assertEqual(written, ["grid.inventory_cells"], "a cut part is its cutter's to write")
            image = pngio.read(Path(folder) / "grid.inventory_cells.png")
            self.assertEqual((image.w, image.h), BY("grid.inventory_cells").master)


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
