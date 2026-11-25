"""ARC-UI-3321: the sheet's solid fixtures cut from the HD frame: places, sizes, the cut rule on a synthetic frame (no game pixel)."""
from __future__ import annotations

import math
import tempfile
import unittest
from pathlib import Path

from tools.steamwoven_ui import build_kit, fixtures, label_parts, pngio, sheet_parts


def frame_with_parts() -> pngio.Image:
    """A 3200 x 1600 frame: dark wood with horizontal grain, and in each part's box a brass disc (a black pit in it) with a cast shadow down and to the right."""
    image = pngio.Image.blank(3200, 1600, (70, 52, 38, 255))
    for y in range(1600):
        grain = int(5 * math.sin(y * 0.9))
        for x in range(3200):
            i = (y * 3200 + x) * 4
            image.data[i:i + 3] = bytes((70 + grain, 52 + grain, 38 + grain))
    for part in sheet_parts.PARTS:
        cx, cy = (part.x + part.width // 2) * 4, (part.y + part.height // 2) * 4
        radius = min(part.width, part.height) * 4 // 2 - 36
        for y in range(cy - radius - 20, cy + radius + 20):
            for x in range(cx - radius - 20, cx + radius + 20):
                d = math.hypot(x - cx - 4, y - cy - 4) - radius
                i = (y * 3200 + x) * 4
                if 0 < d < 10:
                    k = 1.0 - 0.6 * (1.0 - d / 10.0)
                    image.data[i:i + 3] = bytes(int(v * k) for v in image.data[i:i + 3])
                r = math.hypot(x - cx, y - cy)
                if r < radius * 0.5:
                    image.data[i:i + 3] = bytes((2, 2, 2))
                elif r < radius:
                    image.data[i:i + 3] = bytes((170, 125, 55))
    return image


class SheetPartTests(unittest.TestCase):
    def test_self_test_passes(self) -> None:
        self.assertEqual(sheet_parts.self_test(), 0)

    def test_every_part_is_a_cut_fixture_of_the_table_with_the_same_size_and_is_not_drawn(self) -> None:
        manifest = {p["id"]: p for p in build_kit.json.loads(build_kit.MANIFEST.read_text())["parts"]}
        for part in sheet_parts.PARTS:
            fixture = fixtures.BY_ID[part.ident]
            self.assertTrue(fixture.cut, part.ident)
            self.assertEqual((fixture.width, fixture.height), (part.width, part.height), part.ident)
            row = manifest[part.ident]
            self.assertEqual((row["size"], row["size_basis"], row["used_by"]), ([fixtures.snap(part.width), fixtures.snap(part.height)], "placed", ["G05"]))
            self.assertIn("sheet_parts.py", row["note"])
            with self.assertRaises(KeyError):
                fixtures.draw(part.ident)

    def test_a_part_is_cut_by_one_cutter_only(self) -> None:
        self.assertEqual(set(sheet_parts.BY_ID) & set(label_parts.BY_ID), set())

    def test_the_boxes_lie_on_the_sheet_and_the_anchor_inside_its_own_box(self) -> None:
        for part in sheet_parts.PARTS:
            self.assertTrue(0 <= part.x and part.x + part.width <= 800 and 0 <= part.y and part.y + part.height <= 400, part.ident)
            self.assertTrue(part.x <= part.anchor[0] < part.x + part.width and part.y <= part.anchor[1] < part.y + part.height, part.ident)

    def test_the_dial_master_has_no_lettering_where_the_label_part_draws_it(self) -> None:
        frame = frame_with_parts()
        dial = sheet_parts.BY_ID["fixture.align_dial"]
        box = dial.repaint[0]
        for y in range(box[1] * 4 + 12, box[1] * 4 + 40):  # dark ink in the label box, inside the synthetic disc's brass
            for x in range(box[0] * 4 + 100, box[0] * 4 + 120):
                i = (y * 3200 + x) * 4
                frame.data[i:i + 3] = bytes((20, 20, 20))
        master = sheet_parts.build_part(frame, dial)
        self.assertEqual((master.w, master.h), (fixtures.snap(dial.width), fixtures.snap(dial.height)))
        mx, my = round((box[0] + 27 - dial.x) * 3.6), round((box[1] + 7 - dial.y) * 3.6)
        r, g, b, a = master.pixel(mx, my)
        self.assertFalse(a > 200 and r < 60, f"the ink mark in the label box is rebuilt to ground, not kept: {(r, g, b, a)}")

    def test_the_dial_rebuilds_the_lettering_the_label_part_draws(self) -> None:
        dial = sheet_parts.BY_ID["fixture.align_dial"]
        label = label_parts.BY_ID["fixture.sheet_dial_letters"]
        for box in dial.repaint:
            self.assertTrue(any(box == b for b in label.boxes), "the rebuilt box is the label part's box")
            self.assertTrue(dial.x <= box[0] and box[2] <= dial.x + dial.width and dial.y <= box[1] and box[3] <= dial.y + dial.height)

    def test_build_writes_masters_of_the_part_size_opaque_inside_clear_at_the_corner(self) -> None:
        frame = frame_with_parts()
        for part in sheet_parts.PARTS:
            master = sheet_parts.build_part(frame, part)
            self.assertEqual((master.w, master.h), (fixtures.snap(part.width), fixtures.snap(part.height)), part.ident)
            centre = master.pixel(master.w // 2, master.h // 2)
            self.assertTrue(centre[3] == 255 and centre[0] < 20, (part.ident, centre))
            self.assertEqual(master.pixel(0, 0)[3], 0, f"{part.ident}: the corner far from the part is clear")
            self.assertEqual(master.data, sheet_parts.build_part(frame, part).data, f"{part.ident}: not deterministic")

    def test_the_shadow_is_dark_translucent_black_down_and_right_and_gone_up_and_left(self) -> None:
        frame = frame_with_parts()
        part = sheet_parts.BY_ID["fixture.health_disc"]
        master = sheet_parts.build_part(frame, part)
        cx, cy = (part.width // 2) * 3.6, (part.height // 2) * 3.6
        radius = (min(part.width, part.height) * 4 // 2 - 36) / 4 * 3.6  # master px
        down_right = master.pixel(round(cx + (radius + 9) * 0.7071), round(cy + (radius + 9) * 0.7071))
        up_left = master.pixel(round(cx - (radius + 14) * 0.7071), round(cy - (radius + 14) * 0.7071))
        self.assertTrue(40 < down_right[3] < 255 and down_right[0] < 8, f"a translucent black shadow down and right: {down_right}")
        self.assertLess(up_left[3], 30, f"little or no shadow up and left: {up_left}")

    def test_the_shadow_fades_out_at_the_box_edge(self) -> None:
        # a disc whose shadow reaches the box's bottom edge: the last rows are clear with the fade and not without it
        wood = [[[60.0] * 120 for _ in range(110)] for _ in range(3)]
        for y in range(110):
            for x in range(120):
                r = math.hypot(x - 60, y - 50)
                d = math.hypot(x - 62, y - 52) - 40
                for c in range(3):
                    if r < 40:
                        wood[c][y][x] = 170.0 if c == 0 else 120.0
                    elif d < 18:
                        wood[c][y][x] = 60.0 * (1.0 - 0.6 * max(0.0, 1.0 - d / 18.0))
        plain = sheet_parts.cut_solid(wood, (60, 50))
        faded = sheet_parts.cut_solid(wood, (60, 50), (0, 0, 120, 100))
        self.assertGreater(max(plain.alpha[99]), 0.1, "without the fade the shadow reaches the box's bottom row")
        self.assertEqual(max(faded.alpha[99]), 0.0)
        self.assertLess(max(faded.alpha[96]), max(plain.alpha[96]), "and it fades over the last pixels")

    def test_build_refuses_a_frame_of_the_wrong_size(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "small.png"
            pngio.write(path, pngio.Image.blank(64, 64, (0, 0, 0, 255)))
            with self.assertRaises(ValueError):
                sheet_parts.build(path, Path(folder) / "out")

    def test_a_find_without_a_part_is_refused(self) -> None:
        wood = [[[50.0] * 64 for _ in range(64)] for _ in range(3)]
        with self.assertRaises(ValueError):
            sheet_parts.cut_solid(wood, (32, 32))


if __name__ == "__main__":
    unittest.main()
