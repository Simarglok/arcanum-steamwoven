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

    def test_void_grey_is_filler_only_in_regions_not_single_pixels(self):
        image = np.zeros((20, 20, 3), np.uint8)
        image[2:12, 2:12] = tr.VOID_GREY                      # a grey region: filler
        image[15, 15] = tr.VOID_GREY                          # one grey pixel of a texture: content
        image[2:4, 16:19] = tr.VOID_GREY                      # a strip thinner than 5 pixels: content
        void = tr.grey_void(image)
        self.assertTrue(void[2:12, 2:12].all())
        self.assertFalse(void[15, 15])
        self.assertFalse(void[2:4, 16:19].any())
        self.assertFalse(tr.grey_void(np.zeros((8, 8, 3), np.uint8)).any())

    def test_a_whole_map_is_one_window_without_a_sector(self):
        # ARC-RENDER-3107: a map that loads whole has no window edge, so one run holds every shot and the stitch takes every pixel from it
        tiles = list(range(1, 400))
        needed, windows = tr.plan_windows(TMI, tiles, whole=True)
        self.assertEqual(list(windows), [(None, None)])
        self.assertEqual(tr.window_name((None, None)), "whole")
        self.assertEqual(tr.window_name((504, 976)), "504_976")
        self.assertTrue(windows[(None, None)])
        _, lattice = tr.plan_windows(TMI, tiles)
        self.assertEqual(sum(len(s) for s in windows.values()) <= sum(len(s) for s in lattice.values()), True)

    def test_the_map_is_the_one_whose_sectors_lie_nearest_the_origin(self):
        # a town map number can sit on two maps (one town of the module): the origin location of the .tmi decides
        tmi = {"originX": 1498 * 64 + 5, "originY": 1443 * 64 + 5}
        main = {"map": "arcanum1-024-fixed", "min_x": "1496", "min_y": "1441", "max_x": "1499", "max_y": "1442", "sectors": "8"}
        interior = {"map": "k'na tha", "min_x": "5", "min_y": "11", "max_x": "8", "max_y": "14", "sectors": "16"}
        best, distance = tr.pick_map(tmi, [interior, main])
        self.assertEqual(best["map"], "arcanum1-024-fixed")
        self.assertEqual(distance, 1.0)
        best, _ = tr.pick_map({"originX": 6 * 64, "originY": 12 * 64}, [main, interior])
        self.assertEqual(best["map"], "k'na tha")

    def test_town_table_and_results_row(self):
        with tempfile.TemporaryDirectory() as tmp:
            scan = Path(tmp)
            (scan / "tiles-all").mkdir()
            (scan / "tmi-all.tsv").write_text("number\tname\twidth\theight\tcolumns\trows\toriginX\toriginY\tscale\n"
                                              "7\tInner Room\t5120\t2560\t20\t20\t96\t96\t0.25\n8\tLost\t5120\t2560\t20\t20\t96\t96\t0.25\n")
            (scan / "tiles-all" / "7.txt").write_text("\n".join(str(i) for i in range(0, 60)) + "\n")
            (scan / "tiles-all" / "8.txt").write_text("0\n")
            (scan / "townmap-sectors.tsv").write_text("map\ttownmap\tsectors\tmin_x\tmin_y\tmax_x\tmax_y\ninner\t7\t1\t1\t1\t1\t1\n")
            table = scan / "towns.tsv"
            tr.cmd_towns(type("A", (), {"scan": str(scan), "module": "Arcanum", "out": str(table)})())
            rows = {r["name"]: r for r in tr.read_tsv(table)}
            self.assertEqual((rows["Inner Room"]["map"], rows["Inner Room"]["kind"], rows["Inner Room"]["status"]), ("inner", "whole", "render"))
            self.assertEqual(rows["Inner Room"]["origin_sector"], "1,1")
            self.assertEqual(rows["Lost"]["status"], "no-map")                  # no sector carries its number: listed, never rendered
            out = scan / "town"
            out.mkdir()
            (out / "render.json").write_text('{"stitch_seconds": 2.5, "bytes": 99, "against_original_0.25": {"mean_error": 1.5, "worst_64x64_windows": [{"mean_error": 3.0}]}}')
            (out / "capture.json").write_text('{"windows": 1, "shots": 2, "seconds": 20.0}')
            result = tr.town_result(out, rows["Inner Room"], "pass")
            self.assertEqual((result["capture_s"], result["worst_window"], result["bytes"], result["result"]), (20.0, 3.0, 99, "pass"))


if __name__ == "__main__":
    unittest.main()
