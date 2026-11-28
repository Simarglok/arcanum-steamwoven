"""ARC-UI-3474: the fixtures of the inventory family and the schematic window cut from the player's HD pack (synthetic pictures only, no game pixel)."""
from __future__ import annotations

import re
import tempfile
import unittest
from pathlib import Path

from tools.steamwoven_ui import build_kit, fixtures, pngio, window_parts

STAND_INS = Path(__file__).resolve().parents[1] / "terrain-viewer" / "Godot" / "AreaKitStandIns.cs"


class WindowPartTests(unittest.TestCase):
    def test_self_test_passes(self) -> None:
        self.assertEqual(window_parts.self_test(), 0)

    def test_every_part_is_a_cut_fixture_of_the_table_with_the_same_size_and_is_not_drawn(self) -> None:
        manifest = {p["id"]: p for p in build_kit.json.loads(build_kit.MANIFEST.read_text())["parts"]}
        for part in window_parts.PARTS:
            fixture = fixtures.BY_ID[part.ident]
            self.assertTrue(fixture.cut, part.ident)
            self.assertEqual((fixture.width, fixture.height), (part.width, part.height), part.ident)
            row = manifest[part.ident]
            self.assertEqual((row["size"], row["size_basis"]), ([fixtures.snap(part.width), fixtures.snap(part.height)], "placed"), part.ident)
            self.assertIn("window_parts.py", row["note"])
            with self.assertRaises(KeyError):
                fixtures.draw(part.ident)

    def test_the_table_s_notes_name_the_place_of_each_part(self) -> None:
        for part in window_parts.PARTS:
            x, y = part.place
            self.assertRegex(fixtures.BY_ID[part.ident].note, rf"(at|and) {x}, {y}\b|at 725, 70 \+ 23 k" if part.ident != "fixture.pack_pit" else rf"at {x}, {y}\b", part.ident)

    def test_the_viewer_draws_each_part_at_the_corner_of_its_cut(self) -> None:
        source = STAND_INS.read_text(encoding="utf-8")
        for part in window_parts.PARTS:
            x, y = part.place
            if part.ident == "fixture.count_pit":  # five of them, 23 units apart: the viewer's y is 71 + 23 k
                self.assertIn(f'"fixture.count_pit", {x}, y - {71 - y},', source)
                continue
            found = re.findall(rf'"{re.escape(part.ident)}", (\d+), (\d+)', source)
            self.assertEqual(found, [(str(x), str(y))], part.ident)

    def test_a_frame_of_another_size_is_refused(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "interface" / window_parts.FRAMES[223][0]
            path.parent.mkdir(parents=True)
            pngio.write(path, pngio.Image.blank(8, 8, (0, 0, 0, 255)))
            with self.assertRaises(ValueError):
                window_parts.read_frame(Path(folder), 223)

    def test_a_see_through_pixel_becomes_black(self) -> None:
        frame = pngio.Image.blank(2, 1, (200, 100, 50, 255))
        frame.data[4:8] = bytes((200, 100, 50, 0))
        flat = window_parts.flatten(frame)
        self.assertEqual((flat.pixel(0, 0), flat.pixel(1, 0)), ((200, 100, 50, 255), (0, 0, 0, 255)))

    def test_an_unknown_part_is_refused(self) -> None:
        self.assertEqual(window_parts.main(["build", "--pack", "nowhere", "--only", "fixture.nothing"]), 2)


if __name__ == "__main__":
    unittest.main()
