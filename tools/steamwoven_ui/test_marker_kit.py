"""ARC-UI-3207: tests of `marker_kit.py` (map markers and slot covers) on synthetic frames only: no original and no pack data."""
from __future__ import annotations

import contextlib
import io
import json
import math
import tempfile
import unittest
from pathlib import Path

from tools.steamwoven_ui import marker_kit, pngio

Image = pngio.Image
CLEAR = (0, 0, 0, 0)
GROUND, SHAPE, DETAIL = (0, 1, 2, 255), (41, 42, 43, 255), (20, 21, 22, 255)
LINE, LINE_SHAPE = (46, 51, 60, 255), (80, 84, 91, 255)


def picture(w: int, h: int, colour_at) -> Image:
    image = Image.blank(w, h)
    for y in range(h):
        for x in range(w):
            image.data[(y * w + x) * 4:(y * w + x) * 4 + 4] = bytes(colour_at(x, y))
    return image


def disc(size: int, outer: tuple, inner: tuple, split: float) -> Image:
    c = (size - 1) / 2

    def at(x: int, y: int) -> tuple:
        d = math.hypot(x - c, y - c)
        return CLEAR if d > size / 2 - 0.1 else inner if d <= split else outer

    return picture(size, size, at)


def cover_frame() -> Image:
    """32 x 32: a clear top row, grid lines at column 15 and row 31, a square silhouette with a darker block in it."""

    def at(x: int, y: int) -> tuple:
        if y == 0:
            return CLEAR
        inside = 6 <= x < 26 and 6 <= y < 26
        if x == 15 or y == 31:
            return LINE_SHAPE if inside else LINE
        if 18 <= x < 23 and 18 <= y < 23:
            return DETAIL
        return SHAPE if inside else GROUND

    return picture(32, 32, at)


class Table(unittest.TestCase):
    def test_seventeen_arts_give_a_part_per_frame_at_the_snapped_size(self) -> None:
        self.assertEqual(len(marker_kit.ARTS), 17)
        self.assertEqual(len(marker_kit.PARTS), 21)
        self.assertEqual(marker_kit.PARTS["glyph.mm_loc"].size, (30, 30))
        self.assertEqual(marker_kit.PARTS["glyph.mm_loc_lit"].size, (42, 42))
        self.assertEqual(marker_kit.PARTS["glyph.cvr_armor"].size, (462, 576))

    def test_the_picked_mix_names_the_look_in_the_provenance(self) -> None:
        self.assertEqual(marker_kit.provenance_of("glyph.mm_chest"), "pack:marker_kit:glyph.mm_chest")
        self.assertEqual(marker_kit.provenance_of("glyph.mm_wayp_lit"), "rule:marker_kit:glyph.mm_wayp_lit")
        self.assertEqual(marker_kit.provenance_of("glyph.cvr_ring2"), "pack:marker_kit:glyph.cvr_ring2")
        self.assertIn("art 246", marker_kit.note_of("glyph.cvr_ring2"))
        self.assertTrue(marker_kit.RULE_PICK <= {a.name for a in marker_kit.ARTS})

    def test_the_manifest_holds_every_part_as_a_master(self) -> None:
        rows = {r["id"]: r for r in json.loads(marker_kit.small_kit.MANIFEST.read_text(encoding="utf-8"))["parts"]}
        for ident, part in marker_kit.PARTS.items():
            self.assertEqual(rows[ident]["status"], "master", ident)
            self.assertEqual(rows[ident]["provenance"], marker_kit.provenance_of(ident), ident)
            self.assertEqual(rows[ident]["size"], list(part.size) if part.frame == 0 else None, ident)


class Rule(unittest.TestCase):
    def test_a_ring_marker_becomes_true_circles_of_the_original_colours(self) -> None:
        made = marker_kit.round_marker(disc(9, (217, 142, 249, 255), (0, 0, 0, 255), 3.4), (30, 30))
        self.assertEqual(made.pixel(15, 15), (0, 0, 0, 255))
        self.assertEqual(made.pixel(15, 1), (217, 142, 249, 255))
        self.assertEqual(made.pixel(1, 1)[3], 0)

    def test_the_new_place_ring_keeps_its_cross_and_a_clear_inside(self) -> None:
        yellow = (255, 246, 0, 255)
        frame = picture(11, 11, lambda x, y: yellow if 4.4 < math.hypot(x - 5, y - 5) <= 5.4 or (abs(x - 5) + abs(y - 5) <= 1) else CLEAR)
        made = marker_kit.round_marker(frame, (44, 44))
        self.assertEqual(made.pixel(22, 22), yellow)
        self.assertEqual(made.pixel(22, 30)[3], 0, "between the cross and the ring nothing is drawn")
        self.assertEqual(made.pixel(22, 2), yellow)

    def test_a_drawn_marker_keeps_flat_colours_and_its_outline(self) -> None:
        red, black = (255, 0, 0, 255), (0, 0, 0, 255)
        frame = picture(8, 8, lambda x, y: CLEAR if x in (0, 7) or y in (0, 7) else black if x in (1, 6) or y in (1, 6) else red)
        made = marker_kit.smooth_marker(frame, (32, 32))
        self.assertEqual(made.pixel(16, 16), red)
        self.assertEqual(made.pixel(6, 16), black)
        self.assertEqual(made.pixel(1, 16)[3], 0)

    def test_a_cover_keeps_ground_shape_darker_detail_lines_and_the_clear_row(self) -> None:
        made = marker_kit.cover(cover_frame(), (128, 128))
        self.assertEqual(made.pixel(10, 60), GROUND)
        self.assertEqual(made.pixel(40, 40), SHAPE)
        self.assertEqual(made.pixel(62, 40), LINE_SHAPE)
        self.assertEqual(made.pixel(62, 112), LINE)
        self.assertEqual(made.pixel(60, 1)[3], 0)
        detail = made.pixel(82, 82)[0]
        self.assertTrue(GROUND[0] < detail < SHAPE[0], f"the darker block stays a darker step, not {detail}")


class Pack(unittest.TestCase):
    def setUp(self) -> None:
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)
        self.frames, self.pack = self.root / "frames", self.root / "pack"
        self.pack.mkdir()
        self.rows: list[str] = []
        for art in marker_kit.ARTS:
            (self.frames / art.folder).mkdir(parents=True)
            for part in marker_kit.parts_of(art):
                w, h = part.units
                frame = picture(w, h, lambda x, y: CLEAR if (x, y) == (0, 0) else SHAPE if 2 <= x < w - 2 and 2 <= y < h - 2 else GROUND)
                pngio.write(self.frames / art.folder / f"f{part.frame}.png", frame)
                held = art.name not in ("mm_cross", "cvr_ring2") and not (art.name in ("mm_loc", "mm_wayp") and part.frame)
                if held:
                    rel = f"{art.pack_name}-f{part.frame}.png"
                    # a pack frame with a clear black rim: its alpha is not used and its black must not bleed into the part
                    pngio.write(self.pack / rel, picture(w * 4, h * 4, lambda x, y: (0, 0, 0, 0) if x < 4 or y < 4 else (200, 100, 50, 255)))
                    self.rows.append(f"g\tp\t{art.pack_name}\t{part.frame}\t{rel}")
        (self.pack / "index.tsv").write_text("\n".join(self.rows) + "\n", encoding="utf-8")
        self.index = marker_kit.small_kit.pack_index(self.pack)

    def test_a_pack_part_takes_the_originals_alpha_and_no_dark_fringe(self) -> None:
        image, source = marker_kit.build_part(marker_kit.PARTS["glyph.mm_chest"], self.frames, self.pack, self.index, "pick")
        self.assertEqual(source, self.pack / "mm_chest-f0.png")
        self.assertEqual((image.w, image.h), (60, 66))
        self.assertEqual(image.pixel(0, 0)[3], 0, "the original's clear corner")
        self.assertEqual(image.pixel(1, 30), (200, 100, 50, 255), "opaque by the original, the colour bled over the pack's clear rim")

    def test_the_second_ring_takes_the_first_rings_pack_frame(self) -> None:
        _, source = marker_kit.build_part(marker_kit.PARTS["glyph.cvr_ring2"], self.frames, self.pack, self.index, "pick")
        self.assertEqual(source, self.pack / "CVR_Ring1-f0.png")

    def test_the_second_ring_is_refused_when_the_originals_differ(self) -> None:
        pngio.write(self.frames / "247-CVR_Ring2" / "f0.png", Image.blank(32, 32, SHAPE))
        with self.assertRaises(marker_kit.MarkerError):
            marker_kit.build_part(marker_kit.PARTS["glyph.cvr_ring2"], self.frames, self.pack, self.index, "pick")

    def test_a_missing_pack_frame_fails_the_pick_and_falls_back_in_the_pack_look(self) -> None:
        del self.index[("mm_chest", 0)]
        part = marker_kit.PARTS["glyph.mm_chest"]
        with self.assertRaises(marker_kit.MarkerError):
            marker_kit.build_part(part, self.frames, self.pack, self.index, "pick")
        self.assertIsNone(marker_kit.build_part(part, self.frames, self.pack, self.index, "pack")[1])

    def test_a_pack_frame_of_a_wrong_size_is_refused(self) -> None:
        pngio.write(self.pack / "mm_chest-f0.png", Image.blank(10, 10, SHAPE))
        with self.assertRaises(marker_kit.MarkerError):
            marker_kit.build_part(marker_kit.PARTS["glyph.mm_chest"], self.frames, self.pack, self.index, "pick")

    def run_tool(self, *more: str) -> tuple[int, str]:
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            code = marker_kit.main(["build", "--local-root", str(self.root / "local"), "--pack", str(self.pack), "--frames", str(self.frames), "--only", "mm_chest,mm_loc", *more])
        return code, out.getvalue()

    def test_a_dry_run_writes_nothing(self) -> None:
        code, text = self.run_tool("--dry-run")
        self.assertEqual(code, 0, text)
        self.assertFalse((self.root / "local").exists())

    def test_a_build_writes_masters_and_records_that_name_their_sources(self) -> None:
        code, text = self.run_tool()
        self.assertEqual(code, 0, text)
        local = self.root / "local"
        self.assertEqual(sorted(p.name for p in (local / "masters").iterdir()), ["glyph.mm_chest.png", "glyph.mm_loc.png", "glyph.mm_loc_lit.png"])
        packed = json.loads((local / "provenance" / "rule" / "glyph.mm_chest.json").read_text(encoding="utf-8"))
        self.assertEqual(packed["source"]["file"], "mm_chest-f0.png")
        self.assertEqual(len(packed["source"]["sha256"]), 64)
        ruled = json.loads((local / "provenance" / "rule" / "glyph.mm_loc.json").read_text(encoding="utf-8"))
        self.assertIsNone(ruled["source"])
        self.assertEqual(ruled["original"]["frame"], "205-MM_Loc/f0.png")


if __name__ == "__main__":
    unittest.main()
