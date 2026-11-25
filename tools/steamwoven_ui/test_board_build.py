"""ARC-UI-2927: tests of the B-BOARD helpers (cap shadow and mirror, strip ids, grid sizes) on synthetic pixels only (no original art)."""
from __future__ import annotations

import json
import unittest
from pathlib import Path

from tools.steamwoven_ui import board_build, build_kit, pngio


def bar(height: int) -> pngio.Image:
    return pngio.Image(board_build.BAR_W, height, bytearray(bytes((200, 160, 60, 255)) * (board_build.BAR_W * height)))


class BoardBuildTests(unittest.TestCase):
    def test_self_test(self):
        self.assertEqual(board_build.self_test(), 0)

    def test_shadow_ramp_rises_to_the_bar(self):
        ramp = board_build.shadow_ramp()
        outer = board_build.CAP_W - board_build.BAR_W
        self.assertEqual(len(ramp), board_build.CAP_W)
        self.assertLess(ramp[0], 0.01)
        self.assertAlmostEqual(ramp[outer - 1], board_build.SHADOW_ALPHA, delta=0.01)
        self.assertTrue(all(a == 0 for a in ramp[outer:]))

    def test_left_cap_has_the_bar_inside_and_right_cap_mirrors_it(self):
        left, right = board_build.cap_image(bar(12), 12, False), board_build.cap_image(bar(12), 12, True)
        self.assertEqual(left.pixel(board_build.CAP_W - 1, 5), (200, 160, 60, 255))
        self.assertEqual(right.pixel(0, 5), (200, 160, 60, 255))
        for x in range(board_build.CAP_W):
            self.assertEqual(left.pixel(x, 3), right.pixel(board_build.CAP_W - 1 - x, 3))

    def test_wrong_bar_size_is_refused(self):
        with self.assertRaises(board_build.BoardError):
            board_build.cap_image(bar(6), 12, False)

    def test_zones_tile_the_composition_and_sit_on_the_grid(self):
        self.assertEqual(board_build.TOP_H + board_build.WINDOW_H + board_build.BOTTOM_H, 2160)
        for part, (_, w, h, _) in board_build.STRIPS.items():
            self.assertEqual((w % 6, h % 6), (0, 0), part)
        self.assertEqual(board_build.CAP_W, build_kit.CAP_WIDTH)
        self.assertEqual(board_build.BAR_W, build_kit.BAR_WIDTH)
        self.assertEqual((board_build.TOP_H, board_build.BOTTOM_H), (build_kit.STRIP_HEIGHT["top"], build_kit.STRIP_HEIGHT["bottom"]))

    def test_manifest_rows_of_the_batch(self):
        parts = {p["id"]: p for p in json.loads((Path(__file__).parent / "kit.json").read_text(encoding="utf-8"))["parts"]}
        for part in list(board_build.STRIPS) + list(board_build.CAPS):
            self.assertEqual(parts[part]["batch"], "B-BOARD", part)
            self.assertEqual(parts[part]["status"], "master", part)
            self.assertEqual(parts[part]["provenance"], f"fit:{part}", part)
        for part, (height, _) in board_build.CAPS.items():
            self.assertIn(f"size {board_build.CAP_W} x {height}", parts[part]["note"])


if __name__ == "__main__":
    unittest.main()
