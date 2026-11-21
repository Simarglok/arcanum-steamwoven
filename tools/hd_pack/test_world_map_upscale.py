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

from tools.hd_pack import world_map_upscale as wm


def _synthetic_map(size: int = 96):
    """Paper with a little noise, one thick dark line and one speck of dark grain."""
    rng = np.random.default_rng(7)
    image = np.full((size, size, 3), (170, 200, 225), dtype=np.uint8)
    image = np.clip(image.astype(np.int16) + rng.integers(-3, 4, image.shape), 0, 255).astype(np.uint8)
    image[40:48, 8:88] = (20, 25, 30)  # a strong ink line
    image[10, 10] = (120, 130, 140)  # a single weak speck, below the component size
    return image


@unittest.skipIf(cv2 is None, "numpy, opencv-python-headless and Pillow are needed")
class WorldMapUpscaleTests(unittest.TestCase):
    def test_mask_is_one_on_the_line_and_zero_far_from_it(self) -> None:
        original = _synthetic_map()
        mask = wm.line_mask(original)
        self.assertEqual(mask.shape, (96 * wm.SCALE, 96 * wm.SCALE))
        self.assertGreater(float(mask[38 * wm.SCALE : 42 * wm.SCALE, 48 * wm.SCALE].max()), 0.9)  # at the line's edge
        self.assertLess(float(mask[10 * wm.SCALE, 10 * wm.SCALE]), 0.01)
        self.assertLess(float(mask[90 * wm.SCALE, 5 * wm.SCALE]), 0.01)

    def test_blend_takes_the_network_on_lines_and_lanczos_elsewhere(self) -> None:
        original = _synthetic_map()
        lanczos = wm.lanczos_up(original)
        network = np.clip(lanczos.astype(np.int16) + 40, 0, 255).astype(np.uint8)  # a visibly different "network" picture
        out, coverage = wm.blend(original, network, lanczos)
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
