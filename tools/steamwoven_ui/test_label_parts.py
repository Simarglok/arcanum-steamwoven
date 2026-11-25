"""ARC-UI-3299: the sheet's label parts cut from the HD frame: places, sizes, the cut rule on a synthetic frame (no game pixel)."""
from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from tools.steamwoven_ui import build_kit, fixtures, label_parts, pngio


def frame_with_letters() -> pngio.Image:
    """A 3200 x 1600 frame: dark wood with a bright bar (a letter) and a dark shadow rim in each stat box, dark ink on pale paper in the dial box."""
    image = pngio.Image.blank(3200, 1600, (70, 52, 38, 255))
    for part in label_parts.PARTS:
        for (x0, y0, x1, y1) in part.boxes:
            if part.kind == "wood":
                for y in range((y0 + 8) * 4, (y0 + 30) * 4):
                    for x in range((x0 + 10) * 4, (x0 + 14) * 4):
                        i = (y * 3200 + x) * 4
                        image.data[i:i + 3] = bytes((190, 188, 184))
            else:
                for y in range(y0 * 4, y1 * 4):
                    for x in range(x0 * 4, x1 * 4):
                        i = (y * 3200 + x) * 4
                        image.data[i:i + 3] = bytes((200, 196, 180))
                for y in range(155 * 4, 160 * 4):
                    for x in range(50 * 4, 54 * 4):
                        i = (y * 3200 + x) * 4
                        image.data[i:i + 3] = bytes((40, 34, 28))
    return image


def original_with_letters() -> pngio.Image:
    """An 800 x 400 stand-in of art 22: the same bright bar (grey, light) on wood in each stat box, at 1 px per unit."""
    image = pngio.Image.blank(800, 400, (70, 52, 38, 255))
    for part in label_parts.PARTS:
        if part.kind != "wood":
            continue
        for (x0, y0, x1, y1) in part.boxes:
            for y in range(y0 + 8, y0 + 30):
                for x in range(x0 + 10, x0 + 14):
                    i = (y * 800 + x) * 4
                    image.data[i:i + 3] = bytes((190, 188, 184))
    return image


class LabelPartTests(unittest.TestCase):
    def test_self_test_passes(self) -> None:
        self.assertEqual(label_parts.self_test(), 0)

    def test_every_part_is_a_cut_fixture_of_the_table_with_the_same_size(self) -> None:
        for part in label_parts.PARTS:
            fixture = fixtures.BY_ID[part.ident]
            self.assertTrue(fixture.cut, part.ident)
            self.assertEqual((fixture.width, fixture.height), (part.width, part.height), part.ident)
        self.assertEqual(sorted(f.ident for f in fixtures.FIXTURES if f.cut), sorted(p.ident for p in label_parts.PARTS))

    def test_a_cut_fixture_is_a_placed_part_of_the_manifest_and_is_not_drawn(self) -> None:
        manifest = {p["id"]: p for p in build_kit.json.loads(build_kit.MANIFEST.read_text())["parts"]}
        for part in label_parts.PARTS:
            row = manifest[part.ident]
            self.assertEqual((row["size"], row["size_basis"], row["used_by"]), ([fixtures.snap(part.width), fixtures.snap(part.height)], "placed", ["G05"]))
            self.assertIn("label_parts.py", row["note"])
            with self.assertRaises(KeyError):
                fixtures.draw(part.ident)

    def test_the_boxes_lie_inside_their_part_and_do_not_overlap(self) -> None:
        for part in label_parts.PARTS:
            for (x0, y0, x1, y1) in part.boxes:
                self.assertTrue(part.x <= x0 and x1 <= part.x + part.width and part.y <= y0 and y1 <= part.y + part.height, part.ident)
        rows = [b for part in label_parts.PARTS if part.kind == "wood" for b in part.boxes]
        self.assertEqual(len(rows), 8)

    def test_build_writes_masters_of_the_part_size_with_letters_and_nothing_elsewhere(self) -> None:
        frame = frame_with_letters()
        for part in label_parts.PARTS:
            master = label_parts.build_part(frame, part, original_with_letters())
            self.assertEqual((master.w, master.h), (fixtures.snap(part.width), fixtures.snap(part.height)), part.ident)
            alphas = master.data[3::4]
            self.assertTrue(any(a > 200 for a in alphas), f"{part.ident}: the letter is opaque in the master")
            self.assertEqual(master.pixel(1, 1)[3], 0, f"{part.ident}: the corner far from any letter is clear")
            self.assertEqual(master.data, label_parts.build_part(frame, part, original_with_letters()).data, f"{part.ident}: not deterministic")

    def test_the_light_letter_keeps_its_colour_and_the_dark_one_stays_dark(self) -> None:
        frame = frame_with_letters()
        wood = label_parts.build_part(frame, label_parts.BY_ID["fixture.sheet_stat_names_l"], original_with_letters())
        # the first row's bar: x 122 + 10 .. 14, y 108 + 8 .. 30 units -> master (units - part corner) x 3.6
        r, g, b, a = wood.pixel(round((122 + 12 - 120) * 3.6), round((108 + 19 - 108) * 3.6))
        self.assertTrue(a > 200 and r > 150 and g > 150 and b > 150, (r, g, b, a))
        paper = label_parts.build_part(frame, label_parts.BY_ID["fixture.sheet_dial_letters"])
        r, g, b, a = paper.pixel(round((52 - 25) * 3.6), round((157 - 146) * 3.6))
        self.assertTrue(a > 150 and r < 120, (r, g, b, a))

    def test_grain_far_from_the_original_s_letters_is_not_cut(self) -> None:
        frame = frame_with_letters()
        part = label_parts.BY_ID["fixture.sheet_stat_names_l"]
        # a bright streak of grain (wider than a speck) in the first box, 30 units right of the bar and outside the original's letter region
        for y in range(108 * 4 + 40, 108 * 4 + 56):
            for x in range(150 * 4, 163 * 4):
                i = (y * 3200 + x) * 4
                frame.data[i:i + 3] = bytes((150, 120, 100))
        master = label_parts.build_part(frame, part, original_with_letters())
        self.assertEqual(master.pixel(round((156 - 120) * 3.6), round((108 + 12 - 108) * 3.6))[3], 0, "the streak is not lettering")

    def test_build_refuses_a_frame_of_the_wrong_size(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "small.png"
            pngio.write(path, pngio.Image.blank(64, 64, (0, 0, 0, 255)))
            with self.assertRaises(ValueError):
                label_parts.build(path, path, Path(folder) / "out")


if __name__ == "__main__":
    unittest.main()
