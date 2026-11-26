"""ARC-UI-3207: tests of `marker_kit.py` (map markers and slot covers) on synthetic frames only: no original, no pack and no generated data."""
from __future__ import annotations

import contextlib
import io
import json
import math
import tempfile
import unittest
from pathlib import Path

from tools.steamwoven_ui import kit_check, marker_kit, pngio

Image = pngio.Image
CLEAR = (0, 0, 0, 0)
MAGENTA = (255, 0, 255, 255)
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


def cover_frame(w: int = 32, h: int = 32) -> Image:
    """A clear first cell, grid lines at column 15 and row 31, a silhouette in the middle with a darker block in it, a dim left edge column."""

    def at(x: int, y: int) -> tuple:
        if (x, y) == (0, 0):
            return CLEAR
        inside = w // 5 <= x < w * 4 // 5 and h // 5 <= y < h * 4 // 5
        if x == 15 or y == 31:
            return LINE_SHAPE if inside else LINE
        if x == 0:
            return (22, 23, 24, 255)
        if w * 3 // 5 <= x < w * 7 // 10 and h * 3 // 5 <= y < h * 7 // 10:
            return DETAIL
        return SHAPE if inside else GROUND

    return picture(w, h, at)


class Table(unittest.TestCase):
    def test_seventeen_arts_give_a_part_per_frame(self) -> None:
        self.assertEqual(len(marker_kit.ARTS), 17)
        self.assertEqual(len(marker_kit.PARTS), 21)
        self.assertEqual(marker_kit.PARTS["glyph.mm_loc"].size, (30, 30))
        self.assertEqual(marker_kit.PARTS["glyph.mm_loc_lit"].size, (42, 42))
        self.assertEqual(marker_kit.PARTS["glyph.cvr_armor"].size, (462, 576))

    def test_every_part_has_the_owners_source(self) -> None:
        by_source: dict[str, set[int]] = {}
        for part in marker_kit.PARTS.values():
            if not part.frame:
                by_source.setdefault(part.source, set()).add(part.art.art)
        self.assertEqual(by_source["pack"], {241, 242, 243, 245, 246, 247})
        self.assertEqual(by_source["codex"], {141, 142, 143, 144, 204, 244, 248, 249})
        self.assertEqual(by_source["rule"], {205, 206, 814})
        self.assertEqual({p.source for p in marker_kit.PARTS.values() if p.frame}, {"rule"})
        self.assertEqual(marker_kit.provenance_of("glyph.mm_cross_lit"), "rule:marker_kit:glyph.mm_cross_lit")
        self.assertEqual(marker_kit.provenance_of("glyph.cvr_armor"), "codex:marker_kit:glyph.cvr_armor")
        self.assertIn("art 246", marker_kit.note_of("glyph.cvr_ring2"))

    def test_the_manifest_holds_every_part_as_a_master(self) -> None:
        rows = {r["id"]: r for r in json.loads(marker_kit.small_kit.MANIFEST.read_text(encoding="utf-8"))["parts"]}
        for ident, part in marker_kit.PARTS.items():
            self.assertEqual(rows[ident]["status"], "master", ident)
            self.assertEqual(rows[ident]["provenance"], marker_kit.provenance_of(ident), ident)
            self.assertEqual(rows[ident]["size"], list(part.size) if part.frame == 0 else None, ident)

    def test_the_violet_place_marker_is_not_a_leaked_ground(self) -> None:
        made = marker_kit.round_marker(disc(9, (217, 142, 249, 255), (0, 0, 0, 255), 3.4), (30, 30))
        self.assertTrue(kit_check.ground_pixels(made.data, made.w), "the colour alone would read as the generation ground")
        self.assertIn("glyph.mm_loc", kit_check.OWN_MAGENTA)


class Rule(unittest.TestCase):
    def test_a_ring_marker_becomes_true_circles_of_the_original_colours(self) -> None:
        made = marker_kit.round_marker(disc(9, (217, 142, 249, 255), (0, 0, 0, 255), 3.4), (30, 30))
        self.assertEqual(made.pixel(15, 15), (0, 0, 0, 255))
        self.assertEqual(made.pixel(15, 1), (217, 142, 249, 255))
        self.assertEqual(made.pixel(1, 1)[3], 0)

    def test_the_new_place_ring_keeps_its_cross_and_a_clear_inside(self) -> None:
        yellow = (255, 246, 0, 255)
        frame = picture(11, 11, lambda x, y: yellow if 4.4 < math.hypot(x - 5, y - 5) <= 5.4 or (abs(x - 5) + abs(y - 5) <= 1) else CLEAR)
        made = marker_kit.round_marker(frame, (42, 42))
        self.assertEqual(made.pixel(21, 21), yellow)
        self.assertEqual(made.pixel(21, 30)[3], 0, "between the cross and the ring nothing is drawn")
        lit = marker_kit.highlight(made, (54, 54))
        self.assertEqual(lit.pixel(27, 3), yellow, "the contour outside the ring")
        self.assertEqual(lit.pixel(27, 36)[3], 0, "the clear inside of the ring stays clear")

    def test_the_highlight_is_the_first_frame_over_a_contour_of_one_width(self) -> None:
        red = (200, 0, 0, 255)
        base = picture(30, 30, lambda x, y: red if 3 <= x < 27 and 3 <= y < 27 else CLEAR)
        lit = marker_kit.highlight(base, (42, 42))
        self.assertEqual(lit.pixel(21, 21), red)
        yellow = (*marker_kit.YELLOW, 255)
        # the square begins at 9 on the new canvas; the contour is the RING px before it, on every side
        for point in ((21, 9 - marker_kit.RING + 1), (21, 41 - 9 + marker_kit.RING - 1), (9 - marker_kit.RING + 1, 21), (41 - 9 + marker_kit.RING - 1, 21)):
            self.assertEqual(lit.pixel(*point), yellow, point)
        self.assertEqual(lit.pixel(21, 9 - marker_kit.RING - 2)[3], 0)


class Covers(unittest.TestCase):
    def test_the_layers_drop_the_grid_and_the_shaded_edge_and_keep_a_darker_detail(self) -> None:
        coverage, columns, rows = marker_kit.cover_layers(cover_frame())
        self.assertEqual((columns, rows), ([15], [31]))
        self.assertEqual(coverage[16 * 32 + 15], 1.0, "silhouette under a line")
        self.assertEqual(coverage[31 * 32 + 3], 0.0, "ground under a line")
        self.assertEqual(coverage[16 * 32], 0.0, "the shaded left edge is no silhouette")
        self.assertTrue(0 < coverage[20 * 32 + 20] < 1)

    def test_lines_are_painted_out_between_their_neighbours(self) -> None:
        image = picture(20, 4, lambda x, y: (250, 250, 250, 255) if 8 <= x < 12 else (40, 40, 40, 255))
        clean = marker_kit.remove_lines(image, [(8, 12)], [])
        self.assertEqual({clean.pixel(x, 2) for x in range(20)}, {(40, 40, 40, 255)})

    def test_an_overlay_is_the_picture_over_black_and_lets_a_line_show_through(self) -> None:
        image = picture(10, 10, lambda x, y: (41, 42, 43, 255) if 2 <= x < 8 else (1, 1, 1, 255))
        made = marker_kit.overlay(image, [True] * 100)
        self.assertEqual(made.pixel(0, 0)[3], 0, "the ground is clear")
        black, line = Image.blank(10, 10, (0, 0, 0, 255)), Image.blank(10, 10, (46, 51, 60, 255))
        for ground in (black, line):
            marker_kit.small_kit.over(ground, made, 0, 0)
        self.assertLessEqual(max(abs(a - b) for a, b in zip(black.pixel(5, 5), (41, 42, 43, 255))), 1)
        self.assertGreater(line.pixel(5, 5)[0], 70, "the line shows through the silhouette, brighter, as in the original")

    def test_nothing_outside_the_kept_area_is_drawn(self) -> None:
        image = Image.blank(4, 4, (90, 90, 90, 255))
        keep = [False] * 8 + [True] * 8
        made = marker_kit.overlay(Image(4, 4, bytearray(bytes((0, 0, 0, 255)) * 8) + image.data[32:]), keep)
        self.assertEqual([made.data[i * 4 + 3] for i in range(8)], [0] * 8)


class Markers(unittest.TestCase):
    def test_the_key_leaves_black_outlines_black_and_the_ground_clear(self) -> None:
        cell = picture(3, 1, lambda x, y: (MAGENTA, (128, 0, 128, 255), (255, 0, 0, 255))[x])
        keyed = marker_kit.keyed(cell)
        self.assertEqual(keyed.pixel(0, 0)[3], 0)
        mixed = keyed.pixel(1, 0)
        self.assertEqual(mixed[:3], (0, 0, 0), "half black, half ground: black")
        self.assertTrue(100 < mixed[3] < 160)
        self.assertEqual(keyed.pixel(2, 0), (255, 0, 0, 255))


class Chain(unittest.TestCase):
    """The whole tool on a synthetic census, pack and generated pictures."""

    def setUp(self) -> None:
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)
        self.frames, self.pack, self.local = self.root / "frames", self.root / "pack", self.root / "local"
        self.pack.mkdir()
        rows = []
        for art in marker_kit.ARTS:
            (self.frames / art.folder).mkdir(parents=True)
            w, h = art.units
            if art.family == "cover":
                frame = cover_frame(w, h)
            else:
                frame = disc(w, (217, 142, 249, 255), (0, 0, 0, 255), w / 2 - 1.2) if art.source == "rule" else picture(w, h, lambda x, y: (200, 0, 0, 255) if 1 <= x < w - 1 and 1 <= y < h - 1 else CLEAR)
            for number in range(art.frames):
                pngio.write(self.frames / art.folder / f"f{number}.png", frame)
            if art.source == "pack" and art.name != "cvr_ring2":
                rel = f"{art.pack_name}-f0.png"
                pngio.write(self.pack / rel, marker_kit.small_kit.upscaled(frame, (w * 4, h * 4)))
                rows.append(f"g\tp\t{art.pack_name}\t0\t{rel}")
        (self.pack / "index.tsv").write_text("\n".join(rows) + "\n", encoding="utf-8")

    def tool(self, *arguments: str) -> tuple[int, str]:
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            code = marker_kit.main([arguments[0], "--local-root", str(self.local), "--pack", str(self.pack), "--frames", str(self.frames), *arguments[1:]])
        return code, out.getvalue()

    def generate(self) -> None:
        folder = self.local / "outbox" / marker_kit.MARKER_BATCH
        folder.mkdir(parents=True)
        cell = 100

        def sheet_at(x: int, y: int) -> tuple:
            inside = 20 <= x % cell < 80 and 20 <= y % cell < 80 and not (x >= 200 and y >= 100)
            return (10, 200, 10, 255) if inside else MAGENTA

        pngio.write(folder / f"{marker_kit.MARKER_ITEM}.png", picture(cell * 3, cell * 2, sheet_at))
        folder = self.local / "outbox" / marker_kit.COVER_BATCH
        folder.mkdir(parents=True)
        for item in ("cover-armor", "cover-shield", "cover-weapon"):
            pngio.write(folder / f"{item}.png", picture(96, 120, lambda x, y: (150, 150, 150, 255) if 30 <= x < 66 and 20 <= y < 100 else (0, 0, 0, 255)))
        (folder / "cover-armor.provenance.json").write_text('{"generator": "test"}', encoding="utf-8")

    def test_the_inboxes_are_batches_the_runner_reads(self) -> None:
        code, text = self.tool("codex-inbox")
        self.assertEqual(code, 0, text)
        for batch, count in ((marker_kit.MARKER_BATCH, 1), (marker_kit.COVER_BATCH, 3)):
            inbox = self.local / "inbox" / batch
            data = json.loads((inbox / "batch.json").read_text(encoding="utf-8"))
            self.assertEqual(len(data["items"]), count)
            for item in data["items"]:
                self.assertEqual(item["prompt_sha256"], marker_kit.hashlib.sha256((inbox / item["prompt_file"]).read_bytes()).hexdigest())
                for reference in item["references"]:
                    self.assertEqual(reference["sha256"], marker_kit.button_kit.sha256_file(inbox / reference["file"]))

    def test_a_missing_generated_picture_fails_with_the_batch_to_run(self) -> None:
        code, text = self.tool("build", "--only", "cvr_armor", "--dry-run")
        self.assertEqual(code, 1)
        self.assertIn(marker_kit.COVER_BATCH, text)

    def test_a_dry_run_writes_nothing(self) -> None:
        code, text = self.tool("build", "--only", "cvr_helmet,mm_loc", "--dry-run")
        self.assertEqual(code, 0, text)
        self.assertFalse(self.local.exists())

    def test_the_second_ring_is_refused_when_the_originals_differ(self) -> None:
        pngio.write(self.frames / "247-CVR_Ring2" / "f0.png", Image.blank(32, 32, SHAPE))
        code, text = self.tool("build", "--only", "cvr_ring2", "--dry-run")
        self.assertEqual(code, 1, text)

    def test_a_build_writes_every_part_with_a_record_that_names_its_source(self) -> None:
        self.generate()
        code, text = self.tool("build")
        self.assertEqual(code, 0, text)
        masters, records = self.local / "masters", self.local / "provenance" / "rule"
        self.assertEqual(sorted(p.stem for p in masters.iterdir()), sorted(marker_kit.PARTS))
        for ident, part in marker_kit.PARTS.items():
            image = pngio.read(masters / f"{ident}.png")
            self.assertEqual((image.w, image.h), part.size, ident)
            self.assertEqual(image.pixel(0, 0)[3], 0, f"{ident}: a transparent ground")
        def read(ident: str) -> dict:
            return json.loads((records / f"{ident}.json").read_text(encoding="utf-8"))

        self.assertEqual(read("glyph.cvr_ring2")["source"]["file"], "CVR_Ring1-f0.png")
        self.assertEqual(read("glyph.cvr_armor")["source"]["generation"], {"generator": "test"})
        self.assertEqual(read("glyph.mm_chest")["source"]["picture"], f"{marker_kit.MARKER_ITEM}.png")
        self.assertIsNone(read("glyph.mm_cross_lit")["source"])


if __name__ == "__main__":
    unittest.main()
