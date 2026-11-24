"""ARC-RENDER-3065: tests of the world map blend and outline check on synthetic pictures (no original data).

Run: python3 -B -m unittest tools.hd_pack.test_world_map_upscale  (needs numpy, opencv-python-headless and Pillow)
"""
from __future__ import annotations

import unittest

try:
    import cv2
    import numpy as np
except ImportError:  # the recipe is a local research tool; the missing libraries are named by the skip reason
    cv2 = None
    np = None
try:
    import PIL  # noqa: F401
except ImportError:
    PIL = None

from tools.hd_pack import world_map_upscale as wm


def _synthetic_map(size: int = 96):
    """Paper with a little noise, one thick dark line and one speck of dark grain."""
    rng = np.random.default_rng(7)
    image = np.full((size, size, 3), (170, 200, 225), dtype=np.uint8)
    image = np.clip(image.astype(np.int16) + rng.integers(-3, 4, image.shape), 0, 255).astype(np.uint8)
    image[40:48, 8:88] = (20, 25, 30)  # a strong ink line
    image[10:13, 10:13] = (5, 5, 5)  # a small isolated dark speck: its edge ring is under the component size
    return image


@unittest.skipIf(cv2 is None or PIL is None, "numpy, opencv-python-headless and Pillow are needed")
class WorldMapUpscaleTests(unittest.TestCase):
    def test_mask_is_one_on_the_line_and_zero_far_from_it(self) -> None:
        original = _synthetic_map()
        mask = wm.line_mask(original)
        self.assertEqual(mask.shape, (96 * wm.SCALE, 96 * wm.SCALE))
        self.assertGreater(float(mask[38 * wm.SCALE : 42 * wm.SCALE, 48 * wm.SCALE].max()), 0.9)  # at the line's edge
        self.assertLess(float(mask[11 * wm.SCALE, 11 * wm.SCALE]), 0.01)  # the speck is dropped as grain
        self.assertLess(float(mask[90 * wm.SCALE, 5 * wm.SCALE]), 0.01)

    def test_the_component_filter_is_what_drops_the_speck(self) -> None:
        original = _synthetic_map()
        saved = wm.MASK_MIN_COMPONENT
        try:
            wm.MASK_MIN_COMPONENT = 0
            unfiltered = wm.line_mask(original)
        finally:
            wm.MASK_MIN_COMPONENT = saved
        self.assertGreater(float(unfiltered[10 * wm.SCALE : 13 * wm.SCALE, 10 * wm.SCALE : 13 * wm.SCALE].max()), 0.5)

    def test_thin_hatching_is_left_to_lanczos_and_a_thick_line_stays(self) -> None:
        original = _synthetic_map()
        original[58:92, 8:88] = (235, 240, 245)
        for row in range(60, 90, 4):  # 2 px mid-grey strokes on white: strong edges, thin, so no core
            original[row : row + 2, 8:88] = (80, 80, 80)
        hatch = (slice(62 * wm.SCALE, 88 * wm.SCALE), slice(20 * wm.SCALE, 76 * wm.SCALE))
        with_gate = wm.line_mask(original)
        saved = wm.MASK_CORE_DISTANCE
        try:
            wm.MASK_CORE_DISTANCE = 0
            without_gate = wm.line_mask(original)
        finally:
            wm.MASK_CORE_DISTANCE = saved
        self.assertGreater(float(without_gate[hatch].max()), 0.3)  # the plain mask takes the thin strokes
        self.assertLess(float(with_gate[hatch].max()), 0.01)  # the thickness gate leaves them to Lanczos
        self.assertGreater(float(with_gate[38 * wm.SCALE : 42 * wm.SCALE, 48 * wm.SCALE].max()), 0.9)  # the 8 px line stays

    def test_blend_takes_the_network_on_lines_and_lanczos_elsewhere(self) -> None:
        original = _synthetic_map()
        lanczos = wm.lanczos_up(original)
        network = np.clip(lanczos.astype(np.int16) + 40, 0, 255).astype(np.uint8)  # a visibly different "network" picture
        out, coverage = wm.blend(original, network, lanczos, 0)  # the plain weighted mix, no back-projection
        self.assertEqual(out.shape, lanczos.shape)
        self.assertTrue(0.0 < coverage < 0.5)
        mask = wm.line_mask(original)
        edge = (int(np.argmax(mask[38 * wm.SCALE : 42 * wm.SCALE, 48 * wm.SCALE])) + 38 * wm.SCALE, 48 * wm.SCALE)
        far = (90 * wm.SCALE, 5 * wm.SCALE)
        weight = float(mask[edge])
        self.assertGreater(weight, 0.9)
        expected = weight * float(network[edge][0]) + (1 - weight) * float(lanczos[edge][0])
        self.assertLessEqual(abs(float(out[edge][0]) - expected), 1.0)  # the weighted mix, mostly the network
        self.assertTrue(np.array_equal(out[far], lanczos[far]))  # Lanczos where the mask is 0

    def test_back_projection_makes_the_picture_agree_with_the_original(self) -> None:
        original = _synthetic_map()
        lanczos = wm.lanczos_up(original)
        blotchy = np.clip(lanczos.astype(np.int16) + 40, 0, 255).astype(np.uint8)  # a "network" picture that is 40 levels too bright
        before = wm.outline_check(original, blotchy)["mae"]
        projected = wm.back_project(original, blotchy, 1)
        self.assertEqual(projected.shape, blotchy.shape)
        self.assertLess(wm.outline_check(original, projected)["mae"], before / 4)
        saved = wm.STRIP
        try:
            wm.STRIP = 100  # several strips: the padded bicubic rows must leave no seam
            self.assertTrue(np.array_equal(wm.back_project(original, blotchy, 1), projected))
        finally:
            wm.STRIP = saved
        self.assertTrue(np.array_equal(wm.back_project(original, blotchy, 0), blotchy))
        with self.assertRaises(SystemExit):
            wm.back_project(original, np.zeros((10, 10, 3), np.uint8))

    def test_blend_back_projects_unless_told_not_to(self) -> None:
        original = _synthetic_map()
        lanczos = wm.lanczos_up(original)
        network = np.clip(lanczos.astype(np.int16) + 40, 0, 255).astype(np.uint8)
        plain, _ = wm.blend(original, network, lanczos, 0)
        projected, _ = wm.blend(original, network, lanczos)
        self.assertLess(wm.outline_check(original, projected)["mae"], wm.outline_check(original, plain)["mae"])

    def test_back_projecting_only_the_network_leaves_flat_paper_as_lanczos(self) -> None:
        original = _synthetic_map()
        lanczos = wm.lanczos_up(original)
        network = np.clip(lanczos.astype(np.int16) + 40, 0, 255).astype(np.uint8)
        out, _ = wm.blend(original, network, lanczos, 1, "network")
        far = (slice(80 * wm.SCALE, 92 * wm.SCALE), slice(2 * wm.SCALE, 10 * wm.SCALE))
        self.assertTrue(np.array_equal(out[far], lanczos[far]))  # mask 0 there: exactly Lanczos
        with self.assertRaises(SystemExit):
            wm.blend(original, network, lanczos, 1, "both")

    def test_cut_writes_the_tiles_the_continent_picture_and_the_sums(self) -> None:
        import hashlib
        import tempfile
        from pathlib import Path

        picture = np.zeros((16, 16, 3), np.uint8)
        with tempfile.TemporaryDirectory() as folder:
            out = Path(folder) / "pictures"
            # scale 1 would make 250 px tiles; the function insists on the real tile size, so a wrong-sized picture is refused
            with self.assertRaises(SystemExit):
                wm.cut_pictures(picture, out)
            big = np.zeros((2 * 250, 2 * 250, 3), np.uint8)
            big[:250, 250:] = (0, 0, 255)  # tile 2 (row 0, column 1) is red
            count = wm.cut_pictures(big, out, scale=1, tiles_per_side=2)
            self.assertEqual(count, 5)  # four tiles and the continent picture
            self.assertEqual(sorted(p.name for p in out.glob("*.png")), ["map_zoomed.png", "smallmapchunks001.png", "smallmapchunks002.png", "smallmapchunks003.png", "smallmapchunks004.png"])
            red = cv2.imread(str(out / "smallmapchunks002.png"))
            self.assertEqual(red.shape, (250, 250, 3))
            self.assertTrue((red == (0, 0, 255)).all())
            self.assertEqual(cv2.imread(str(out / "map_zoomed.png")).shape, (365, 365, 3))
            sums = (out / "SHA256SUMS.txt").read_text().splitlines()
            self.assertEqual(len(sums), 5)
            self.assertIn(hashlib.sha256((out / "smallmapchunks002.png").read_bytes()).hexdigest() + "  ./smallmapchunks002.png", sums)
            with self.assertRaises(SystemExit):  # a folder that already holds files is not overwritten
                wm.cut_pictures(big, out, scale=1, tiles_per_side=2)

    def test_weave_ratio_is_one_for_lanczos_itself(self) -> None:
        original = _synthetic_map()
        self.assertAlmostEqual(wm.weave_ratio(original, wm.lanczos_up(original)), 1.0, places=6)

    def test_blend_rejects_a_picture_of_the_wrong_size(self) -> None:
        original = _synthetic_map()
        with self.assertRaises(SystemExit):
            wm.blend(original, np.zeros((10, 10, 3), np.uint8), wm.lanczos_up(original))

    def test_outline_check_of_the_lanczos_picture_is_near_perfect_and_a_shifted_one_is_worse(self) -> None:
        original = _synthetic_map()
        good = wm.outline_check(original, wm.lanczos_up(original))
        self.assertGreater(good["f1_1px"], 0.95)
        shifted = np.roll(wm.lanczos_up(original), 24, axis=0)  # six original pixels away
        bad = wm.outline_check(original, shifted)
        self.assertLess(bad["f1_1px"], good["f1_1px"])


if __name__ == "__main__":
    unittest.main()
