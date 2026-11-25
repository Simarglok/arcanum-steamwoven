"""ARC-UI-2481: tests of the hardware batch helpers (grid rounding, shapes, the erase fill, the part table and its sync with the runtime table) on synthetic pixels only (no original art)."""
from __future__ import annotations

import json
import re
import unittest
from pathlib import Path

from tools.steamwoven_ui import hardware_build as hb, pngio

ROOT = Path(__file__).resolve().parent.parent.parent
COMPOSER = ROOT / "tools" / "terrain-viewer" / "Core" / "SteamwovenKitComposer.cs"


def flat(w: int, h: int, rgba: tuple[int, int, int, int]) -> pngio.Image:
    return pngio.Image(w, h, bytearray(bytes(rgba) * (w * h)))


class HardwareBuildTests(unittest.TestCase):
    def test_self_test(self):
        self.assertEqual(hb.self_test(), 0)

    def test_grid_rounding(self):
        self.assertEqual(hb.ceil_grid(1), 6)
        self.assertEqual(hb.ceil_grid(6), 6)
        self.assertEqual(hb.ceil_grid(7.2), 12)
        self.assertEqual(hb.round_grid(8.9), 6)
        self.assertEqual(hb.round_grid(9), 12)

    def test_bbox_and_shift_follow_every_shape_kind_and_skip_holes(self):
        shapes = [("rect", 10, 20, 50, 60), ("ellipse", 100, 100, 10, 20), ("poly", 5, 70, 15, 90, 0, 80), ("-rect", 0, 0, 500, 500)]
        self.assertEqual(hb.bbox_of(shapes), (0, 20, 110, 120))
        self.assertEqual(hb.shift(("rect", 10, 20, 50, 60), 10, 20), ("rect", 0, 0, 40, 40))
        self.assertEqual(hb.shift(("ellipse", 100, 100, 10, 20), 10, 20), ("ellipse", 90, 80, 10, 20))

    def test_an_unknown_shape_is_refused(self):
        with self.assertRaises(hb.HardwareError):
            hb.shape_args([("star", 1, 2, 3)])

    def test_the_erase_fill_mirrors_wood_from_beside_the_run_and_leaves_the_rest(self):
        image = flat(12, 1, (100, 90, 80, 255))
        for x in range(0, 4):
            image.data[x * 4:x * 4 + 3] = bytes((10 * (x + 1), 0, 0))     # wood grain left of the run: 10, 20, 30, 40
        erase = flat(12, 1, (0, 0, 0, 255))
        for x in (4, 5):
            erase.data[x * 4] = 255
        wood = flat(12, 1, (255, 255, 255, 255))   # a wood mask: white = wood
        filled = hb.mirror_fill(image, erase, wood)
        self.assertEqual(filled, 2)
        self.assertEqual(image.pixel(4, 0)[0], 40)      # the first erased pixel is the wood pixel just left of the run
        self.assertEqual(image.pixel(5, 0)[0], 30)
        self.assertEqual(image.pixel(6, 0), (100, 90, 80, 255))

    def test_every_part_stands_on_the_grid_in_its_strip_and_the_draw_order_is_the_table_s(self):
        self.assertEqual(sorted(hb.HARDWARE_ORDER), sorted(hb.PARTS))
        for part_id, part in hb.PARTS.items():
            self.assertIn(part["strip"], hb.STRIPS, part_id)
            self.assertTrue(part["instances"], part_id)
            for strip, *_ in part["instances"]:
                self.assertIn(strip, hb.STRIPS, part_id)
        self.assertEqual(hb.layers_of("board.inttop")[0], "hardware.hud_plate_left")
        self.assertEqual(set().union(*(hb.layers_of(strip) for strip in hb.STRIPS)), set(hb.PARTS))
        # ARC-UI-3260: the two HUD strips and the two menu strips are made of their own parts, except the rivet the top strips share
        shared = set(hb.layers_of("board.inttop")) & set(hb.layers_of("board.charcreatetopbar"))
        self.assertEqual(shared, {"hardware.hud_top_rivet"})
        self.assertFalse(set(hb.layers_of("board.inttop")) & set(hb.layers_of("board.intbotom")))
        self.assertFalse(set(hb.layers_of("board.charcreatetopbar")) & set(hb.layers_of("board.charcreatebottombar")))
        self.assertEqual(hb.layers_of("board.charcreatetopbar"), ["hardware.menu_plate_left", "hardware.menu_plate_right", "hardware.hud_top_rivet"])   # the rivets stand over the plates
        self.assertEqual(hb.layers_of("board.charcreatebottombar"), ["hardware.menu_window", "hardware.menu_ring"])

    def test_the_manifest_holds_the_hardware_parts_and_the_strips_list_them_as_layers(self):
        kit = json.loads((ROOT / "tools" / "steamwoven_ui" / "kit.json").read_text(encoding="utf-8"))
        parts = {row["id"]: row for row in kit["parts"]}
        for part_id in hb.PARTS:
            self.assertEqual(parts[part_id]["kind"], "hardware", part_id)
            self.assertEqual(parts[part_id]["status"], "master", part_id)
        for strip in hb.STRIPS:
            layers = parts[strip].get("layers", [])
            self.assertEqual([layer for layer in layers if layer.startswith("hardware.")], hb.layers_of(strip), strip)
            for part_id in hb.layers_of(strip):
                self.assertIn(part_id, layers, strip)

    def test_the_runtime_table_lays_each_part_as_often_as_the_batch_stands_it_and_in_the_same_order(self):
        text = COMPOSER.read_text(encoding="utf-8")
        for marker, strip in (("[AreaBarStrip.Top]", "board.inttop"), ("[AreaBarStrip.Bottom]", "board.intbotom"), ("MenuTopLayers { get; } =", "board.charcreatetopbar"), ("MenuBottomLayers { get; } =", "board.charcreatebottombar")):
            start = text.index(marker)
            block = text[start:text.index("];" if "Layers {" in marker else "]),", start)]
            laid = re.findall(r'new\("(hardware\.[a-z_]+)", (\d+), (\d+)\)', block)
            expected = [part for part in hb.layers_of(strip) for instance in hb.PARTS[part]["instances"] if instance[0] == strip]
            self.assertEqual([part for part, _, _ in laid], expected, strip)
            for _, x, y in laid:
                self.assertEqual((int(x) % hb.GRID, int(y) % hb.GRID), (0, 0), strip)


if __name__ == "__main__":
    unittest.main()
