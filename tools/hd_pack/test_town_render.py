"""ARC-RENDER-3063: tools/hd_pack/town_render.py on synthetic data (no original or derived art). Needs numpy and Pillow: run with the venv
`local/res094/venv/bin/python -B -m unittest tools.hd_pack.test_town_render` from the repository root; without them the module skips."""
from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

try:
    import numpy as np
    from PIL import Image
    from tools.hd_pack import town_render as tr
except ImportError:  # no numpy / Pillow in this interpreter
    np = None

TMI = {"width": 5120, "height": 2560, "columns": 20, "rows": 20, "originX": 640, "originY": 640, "scale": 0.25}


@unittest.skipIf(np is None, "numpy and Pillow are needed")
class TownRenderTests(unittest.TestCase):
    def test_origin_pixel_is_the_origin_location(self):
        fx, fy = tr.sector_fraction(np.array([TMI["width"] / 2]), np.array([TMI["height"] / 2 + 20]), TMI)
        self.assertAlmostEqual(float(fx[0]), 10.0, places=6)      # 640 / 64
        self.assertAlmostEqual(float(fy[0]), 10.0, places=6)

    def test_every_needed_sector_has_every_lattice_window_that_holds_it(self):
        needed = {(10, 10), (11, 10), (12, 10), (10, 11)}
        centres = set(tr.lattice_windows(needed))
        step_ok = {(cx % 2, cy % 2) for cx, cy in centres}
        self.assertEqual(len(step_ok), 1)                           # one lattice phase
        for sx, sy in needed:
            holding = {c for c in centres if abs(c[0] - sx) <= 1 and abs(c[1] - sy) <= 1}
            self.assertTrue(holding)
            # a sector on the edge of one window (next to its centre) is the middle or the opposite edge of another, so no seam is every window's edge
            self.assertTrue(any(abs(cx - sx) == 0 or (cx + 2, cy) in centres or (cx - 2, cy) in centres for cx, cy in holding))

    def test_depth_is_zero_on_the_window_edge_and_grows_inside(self):
        fx, fy = np.array([9.0, 10.0, 10.5]), np.array([10.0, 10.0, 10.5])
        d = tr.depth(fx, fy, (10, 10))
        self.assertEqual(float(d[0]), 0.0)
        self.assertEqual(float(d[1]), 1.0)
        self.assertEqual(float(d[2]), 1.5)

    def test_box_reduction_averages_two_by_two(self):
        full = np.zeros((4, 4, 3), np.uint8)
        full[0:2, 0:2] = 100
        full[0, 2] = 200
        out = tr.reduce_picture(full, 0.5)
        self.assertEqual(out.shape, (2, 2, 3))
        self.assertEqual(int(out[0, 0, 0]), 100)
        self.assertEqual(int(out[0, 1, 0]), 50)

    def test_nearest_reduction_takes_phase_two(self):
        full = np.arange(8 * 8 * 3, dtype=np.uint8).reshape(8, 8, 3)
        out = tr.reduce_picture(full, 0.25)
        self.assertTrue((out[0, 0] == full[2, 2]).all())

    def test_window_metric_flags_a_local_gap_the_mean_hides(self):
        # a town of 2 x 2 tiles (128 x 64 at 0.25); the original is flat, ours matches except a 20 x 20 block (at 0.25)
        plan = {"town": "T", "tiles": [0, 1, 2, 3], "tmi": dict(TMI, columns=2, rows=2, width=512, height=256)}
        with tempfile.TemporaryDirectory() as folder:
            for index in range(4):
                Image.fromarray(np.full((32, 64, 3), 200, np.uint8)).save(Path(folder) / f"T{index:06d}.png")
            full = np.full((256, 512, 3), 200, np.uint8)
            report = tr.compare_original(full, plan, Path(folder))
            self.assertTrue(report["window_ok"])
            self.assertEqual(report["mean_error"], 0.0)
            full[24:104, 24:104] = 10                                 # a gap at 1:1 -> a 20 x 20 block at 0.25
            report = tr.compare_original(full, plan, Path(folder))
            self.assertFalse(report["window_ok"])
            self.assertLess(report["mean_error"], tr.WINDOW_LIMIT)    # the whole picture stays under the limit: only the window metric sees it

    def test_plan_selects_only_windows_and_shots_that_hold_required_tiles(self):
        tiles = list(range(1, 400))                                    # every tile of the town but the stand-in 000000
        needed, windows = tr.plan_windows(TMI, tiles)
        self.assertTrue(needed)
        self.assertTrue(windows)
        for centre, shots in windows.items():
            self.assertTrue(shots)
            for shot in shots:
                self.assertRegex(shot["name"], r"^r\d+c\d+$")


if __name__ == "__main__":
    unittest.main()
