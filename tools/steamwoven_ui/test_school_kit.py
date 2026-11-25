"""ARC-UI-3204: tests of the college tab / skill-school button kit tool (the HD pack's frames cut to the ring's circle) on synthetic frames (no art)."""
from __future__ import annotations

import json
import math
import tempfile
import unittest
from pathlib import Path

from . import pngio
from . import school_kit as sk

TAB = sk.TABS[0]            # art 11, 24 x 24 units, master 84 x 84
HD = 96                     # pack pixels (4 per unit)
INSIDE, OUTSIDE = (200, 30, 30), (30, 200, 30)


def pack_frame(colour=INSIDE) -> pngio.Image:
    """A 96 x 96 pack frame: the colour inside the art's circle (a little wider than the cut), the old background (green) outside it."""
    circle = sk.circle_of(TAB)
    image = pngio.Image(HD, HD, bytearray(HD * HD * 4))
    for y in range(HD):
        for x in range(HD):
            inside = math.hypot(x + 0.5 - circle.cx * 4, y + 0.5 - circle.cy * 4) <= circle.cut * 4 + 3
            image.data[(y * HD + x) * 4:(y * HD + x) * 4 + 4] = bytes((*(colour if inside else OUTSIDE), 255))
    return image


def tree(folder: str, escape: bool = False) -> Path:
    """`<folder>/local/hd-packs/interface-redraw-4x/` with the three frames of TAB (idle red, pressed blue, hover yellow) and their index; returns `<folder>/local`."""
    local = Path(folder) / "local"
    pack = local / sk.PACK_DIR
    (pack / "interface").mkdir(parents=True)
    header = "screen\tkind\tart\tframe\tpath\tlegacy_w\tlegacy_h\thd_w\thd_h\tslice\talpha\treview\tsha12\n"
    rows = []
    for number, colour in ((0, INSIDE), (1, (30, 30, 200)), (2, (200, 200, 30))):
        pngio.write(pack / "interface" / f"{TAB.name}-f{number:03d}.png", pack_frame(colour))
        rows.append(f"x\tbuttons\t{TAB.name}\t{number}\t{'../../x.png' if escape else f'interface/{TAB.name}-f{number:03d}.png'}\t24\t24\t{HD}\t{HD}\ts\t\tunreviewed\tabc123def45{number}\n")
    (pack / "index.tsv").write_text(header + "".join(rows), encoding="utf-8")
    return local


class CircleTests(unittest.TestCase):
    def test_every_button_has_a_circle_in_the_viewers_table(self):
        for tab in sk.TABS:
            circle = sk.circle_of(tab)
            self.assertGreater(circle.radius, 10.0, tab.name)
            self.assertAlmostEqual(circle.trim, 0.4)
            self.assertAlmostEqual(circle.cx, circle.width / 2, delta=2.0, msg=tab.name)
        self.assertEqual(len(sk.circles()), 28)

    def test_the_shadow_numbers_are_the_viewers(self):
        s = sk.shadow()
        self.assertAlmostEqual(s.offset, 13 / 3.6, places=4)
        self.assertAlmostEqual(s.sigma, 16 / 3.6, places=4)
        self.assertAlmostEqual(s.strength, 0.7)

    def test_coverage_is_one_inside_zero_outside_and_half_on_the_edge(self):
        circle = sk.circle_of(TAB)
        master = sk.master_of(TAB.name)
        mx = master[0] / circle.width
        centre = (circle.cx * mx, circle.cy * mx)
        self.assertEqual(sk.coverage(circle, master, *centre), 1.0)
        self.assertEqual(sk.coverage(circle, master, centre[0] + circle.cut * mx + 2, centre[1]), 0.0)
        self.assertAlmostEqual(sk.coverage(circle, master, centre[0] + circle.cut * mx, centre[1]), 0.5, places=3)

    def test_the_shadow_lies_lower_right_and_never_under_the_part(self):
        circle = sk.circle_of(TAB)
        under = sk.shadow_alpha(circle, circle.cx, circle.cy, 0.3)
        lower_right = sk.shadow_alpha(circle, circle.cx + circle.cut + 1.0, circle.cy + circle.cut + 1.0, 0.3)
        upper_left = sk.shadow_alpha(circle, circle.cx - circle.cut - 1.0, circle.cy - circle.cut - 1.0, 0.3)
        self.assertEqual(under, 0.0)
        self.assertGreater(lower_right, upper_left * 3)
        self.assertLessEqual(lower_right, sk.shadow().strength)


class PartTests(unittest.TestCase):
    def test_the_old_background_never_reaches_the_edge(self):
        circle = sk.circle_of(TAB)
        part = sk.round_part(pack_frame(), circle, sk.master_of(TAB.name))
        for y in range(part.h):
            for x in range(part.w):
                k = (y * part.w + x) * 4
                if part.data[k + 3]:
                    self.assertGreater(part.data[k], 150, (x, y))       # red
                    self.assertLess(part.data[k + 1], 90, (x, y))       # no green of the old background

    def test_the_part_is_round_cut_smooth_and_at_the_masters_size(self):
        circle, master = sk.circle_of(TAB), sk.master_of(TAB.name)
        part = sk.round_part(pack_frame(), circle, master)
        self.assertEqual((part.w, part.h), master)
        alpha = lambda x, y: part.data[(y * part.w + x) * 4 + 3]
        self.assertEqual(alpha(master[0] // 2, master[1] // 2), 255)
        self.assertEqual(alpha(0, 0), 0)
        self.assertEqual(alpha(master[0] - 1, 0), 0)
        partial = {part.data[i] for i in range(3, len(part.data), 4)} - {0, 255}
        self.assertGreater(len(partial), 8)                              # an even smoothed edge, not a staircase

    def test_a_frame_of_another_aspect_is_stretched_to_the_master(self):
        circle = sk.circle_of(next(t for t in sk.TABS if t.name == "anatomical_but"))        # 32 x 31 art on a 114 x 114 master
        master = sk.master_of("anatomical_but")
        frame = pngio.Image(128, 124, bytearray(bytes((90, 120, 150, 255)) * (128 * 124)))
        part = sk.round_part(frame, circle, master)
        self.assertEqual((part.w, part.h), master)
        mx, my = master[0] / circle.width, master[1] / circle.height
        self.assertEqual(part.data[(int(circle.cy * my) * part.w + int(circle.cx * mx)) * 4 + 3], 255)

    def test_the_three_states_are_the_three_pack_frames_and_the_glyph_is_empty(self):
        with tempfile.TemporaryDirectory() as folder:
            parts = sk.tab_parts(tree(folder), TAB)
        self.assertEqual(set(parts), {f"plate.{TAB.name}", f"glyph.{TAB.name}", f"plate.{TAB.name}_hover", f"plate.{TAB.name}_pressed"})
        master = sk.master_of(TAB.name)
        centre = ((master[1] // 2) * master[0] + master[0] // 2) * 4
        self.assertEqual(tuple(parts[f"plate.{TAB.name}"].data[centre:centre + 3]), INSIDE)
        self.assertEqual(tuple(parts[f"plate.{TAB.name}_pressed"].data[centre:centre + 3]), (30, 30, 200))     # frame 1
        self.assertEqual(tuple(parts[f"plate.{TAB.name}_hover"].data[centre:centre + 3]), (200, 200, 30))       # frame 2
        self.assertFalse(any(parts[f"glyph.{TAB.name}"].data[3::4]))
        for image in parts.values():
            self.assertEqual((image.w, image.h), master)


class PackTests(unittest.TestCase):
    def test_a_missing_index_or_frame_is_a_school_error(self):
        with tempfile.TemporaryDirectory() as folder:
            with self.assertRaises(sk.SchoolError):
                sk.pack_path(Path(folder), TAB.name, 0)
            local = tree(folder)
            with self.assertRaises(sk.SchoolError):
                sk.pack_path(local, TAB.name, 7)
            with self.assertRaises(sk.SchoolError):
                sk.pack_path(local, "no_such_art", 0)
            (local / sk.PACK_DIR / "interface" / f"{TAB.name}-f000.png").write_bytes(b"not a png")
            with self.assertRaises(sk.SchoolError):
                sk.read_pack(local, TAB.name, 0)

    def test_an_index_that_points_outside_the_pack_is_refused(self):
        with tempfile.TemporaryDirectory() as folder:
            local = tree(folder, escape=True)
            with self.assertRaises(sk.SchoolError):
                sk.pack_path(local, TAB.name, 0)

    def test_the_record_names_the_pack_frame_it_was_cut_from(self):
        with tempfile.TemporaryDirectory() as folder:
            local = tree(folder)
            parts = sk.tab_parts(local, TAB)
            out = Path(folder) / "out"
            out.mkdir()
            records = {}
            for ident, image in parts.items():
                path = out / f"{ident}.png"
                pngio.write(path, image)
                records[ident] = sk.record_of(local, TAB, ident, path, image)
        idle = records[f"plate.{TAB.name}"]
        self.assertEqual(idle["source"], "pack")
        (key, source), = idle["sources"].items()
        self.assertTrue(key.endswith(f"{TAB.name}-f000"))
        self.assertEqual(len(source["sha256"]), 64)
        self.assertEqual(source["index_sha12"], "abc123def450")
        self.assertIn("f001", next(iter(records[f"plate.{TAB.name}_pressed"]["sources"])))
        self.assertIn("f002", next(iter(records[f"plate.{TAB.name}_hover"]["sources"])))
        self.assertTrue(records[f"glyph.{TAB.name}"]["empty"])
        self.assertEqual(records[f"glyph.{TAB.name}"]["sources"], {})


class DataTests(unittest.TestCase):
    def test_names_and_arts_are_unique_and_every_button_has_a_sized_plate_row(self):
        self.assertEqual(len({t.name for t in sk.TABS}), 28)
        self.assertEqual(len({t.art for t in sk.TABS}), 28)
        for tab in sk.TABS:
            self.assertEqual(sk.circle_of(tab).width / sk.circle_of(tab).height > 0, True)
            self.assertTrue(all(v > 0 for v in sk.master_of(tab.name)), tab.name)

    def test_the_manifest_rows_of_the_slice(self):
        manifest = json.loads(sk.MANIFEST.read_text(encoding="utf-8"))
        rows = {p["id"]: p for p in manifest["parts"]}
        for tab in sk.TABS:
            for ident in (f"plate.{tab.name}", f"glyph.{tab.name}", f"plate.{tab.name}_hover", f"plate.{tab.name}_pressed"):
                self.assertIn(ident, rows)
                self.assertEqual(rows[ident]["provenance"], f"rule:school_kit:{ident}")
                self.assertEqual(rows[ident]["status"], "master")

    def test_a_group_filter_and_a_wrong_name_are_refused(self):
        self.assertEqual(len(sk.tabs_of("college")), 16)
        self.assertEqual(len(sk.tabs_of("school")), 12)
        with self.assertRaises(sk.SchoolError):
            sk.tabs_of("nope")
        with self.assertRaises(sk.SchoolError):
            sk.tab_named("nope")


if __name__ == "__main__":
    unittest.main()
