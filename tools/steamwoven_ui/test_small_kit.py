"""ARC-UI-3206, ARC-UI-3323: tests of the small button kit tool (pack frames, the parchment, ink and wedge parts, the round dome by rule, Codex sheets) on synthetic pictures (no art)."""
from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from . import build_kit, pngio
from . import small_kit as sk

BASE = (60, 60, 40)
RIM = (200, 150, 60)


def disc_frame(w: int, h: int, colour=BASE, rim=RIM) -> pngio.Image:
    """A synthetic old pack frame: an opaque disc with a rim, the corners transparent AND dark (as the pack's are)."""
    image = pngio.Image(w, h, bytearray(w * h * 4))
    cx, cy, r = w / 2.0, h / 2.0, min(w, h) / 2.0
    for y in range(h):
        for x in range(w):
            d = ((x + 0.5 - cx) ** 2 + (y + 0.5 - cy) ** 2) ** 0.5
            o = (y * w + x) * 4
            if d <= r:
                image.data[o:o + 4] = bytes((*(rim if d > r - 3 else colour), 255))
            else:
                image.data[o:o + 4] = bytes((5, 5, 5, 0))
    return image


def flat_frame(w: int, h: int, colour) -> pngio.Image:
    return pngio.Image(w, h, bytearray(bytes((*colour, 255)) * (w * h)))


def synthetic_pack(root: Path, b: sk.Small, make) -> Path:
    """A pack folder with an index.tsv and one frame per state of `b`; `make(state, w, h)` gives the frame."""
    pack = root / "pack"
    (pack / "interface").mkdir(parents=True)
    lines = ["screen\tkind\tart\tframe\tpath\tlegacy_w\tlegacy_h\thd_w\thd_h\tslice\talpha\treview\tsha12"]
    for state, number in b.frames.items():
        w, h = b.units[0] * sk.PACK_SCALE, b.units[1] * sk.PACK_SCALE
        rel = f"interface/{b.name}-f{number:03d}.png"
        pngio.write(pack / rel, make(state, w, h))
        lines.append(f"scr\tbuttons\t{b.name}\t{number}\t{rel}\t{b.units[0]}\t{b.units[1]}\t{w}\t{h}\tinterface\t\tunreviewed\t000000000000")
    (pack / "index.tsv").write_text("\n".join(lines) + "\n", encoding="utf-8")
    return pack


RECT = sk.Small("slab", 2, "2-slab", "rect", (12, 6), (36, 18), frames={"idle": 0, "pressed": 1})
ARGS = type("Args", (), {})


class PartTest(unittest.TestCase):
    def test_resample_is_exact_on_a_flat_frame_and_keeps_the_colour_of_a_half_covered_pixel(self):
        part = sk.resample(flat_frame(48, 24, (10, 20, 30)), 36, 18)
        self.assertEqual((part.w, part.h), (36, 18))
        self.assertTrue(all(part.pixel(x, y) == (10, 20, 30, 255) for y in range(18) for x in range(36)))

    def test_over_blends_straight_alpha(self):
        dst = pngio.Image.blank(2, 1, (0, 0, 0, 0))
        sk.over(dst, flat_frame(1, 1, (200, 100, 0)), 0, 0)
        self.assertEqual(dst.pixel(0, 0), (200, 100, 0, 255))
        half = pngio.Image(1, 1, bytearray((100, 100, 100, 128)))
        sk.over(dst, half, 0, 0)
        self.assertEqual(dst.pixel(0, 0)[3], 255)
        self.assertTrue(100 < dst.pixel(0, 0)[0] < 200)
        self.assertEqual(dst.pixel(1, 0), (0, 0, 0, 0))


class DomeTest(unittest.TestCase):
    def frame(self, symbol=True) -> pngio.Image:
        """A 29 x 30 round frame: olive dome, brass ring, one red checkered 3 x 3 symbol in the middle."""
        w, h = 29, 30
        image = pngio.Image(w, h, bytearray(w * h * 4))
        for y in range(h):
            for x in range(w):
                r = sk.dome_radius(x, y, w, h)
                o = (y * w + x) * 4
                if r > 14.5:
                    image.data[o:o + 4] = bytes((0, 0, 0, 0))
                elif r >= sk.DOME_FLAT:
                    image.data[o:o + 4] = bytes((150, 110, 40, 255))
                else:
                    image.data[o:o + 4] = bytes((35, 36, 3, 255))
        if symbol:
            for y in range(13, 16):
                for x in range(13, 16):
                    image.data[(y * w + x) * 4:(y * w + x) * 4 + 4] = bytes((110, 0, 0, 255) if (x + y) % 2 else (80, 0, 50, 255))
        return image

    def test_symbol_cells_are_the_red_ones_inside_the_dome_only(self):
        cells = sk.symbol_cells(self.frame())
        self.assertEqual(sum(cells), 9)
        self.assertTrue(cells[14 * 29 + 14])
        self.assertFalse(cells[0])

    def test_symbol_shape_takes_the_symbol_out_of_the_frame(self):
        frame = self.frame()
        mask, base = sk.symbol_shape(frame, [])
        self.assertEqual(sum(mask), 9)
        self.assertEqual(base.pixel(14, 14)[:3], (35, 36, 3))   # inpainted from the dome round it
        self.assertEqual(base.pixel(14, 1), frame.pixel(14, 1))   # the rest is the frame as it is

    def test_inpaint_fills_from_the_rim_inward(self):
        frame = flat_frame(9, 9, (10, 20, 30))
        frame.data[(4 * 9 + 4) * 4:(4 * 9 + 4) * 4 + 3] = bytes((255, 0, 0))
        hole = [False] * 81
        hole[4 * 9 + 4] = True
        self.assertEqual(sk.inpaint(frame, hole).pixel(4, 4)[:3], (10, 20, 30))

    def test_only_symbol_drops_the_pixels_that_are_not_red_and_with_relief_keeps_the_symbol_on_top(self):
        glyph = pngio.Image.blank(8, 8)
        glyph.data[(3 * 8 + 3) * 4:(3 * 8 + 3) * 4 + 4] = bytes((120, 0, 0, 255))
        glyph.data[(3 * 8 + 4) * 4:(3 * 8 + 4) * 4 + 4] = bytes((10, 10, 10, 255))
        clean = sk.only_symbol(glyph)
        self.assertEqual(clean.pixel(3, 3)[3], 255)
        self.assertEqual(clean.pixel(4, 3)[3], 0)
        relief = sk.with_relief(clean)
        self.assertEqual(relief.pixel(3, 3), (120, 0, 0, 255))
        self.assertTrue(any(relief.data[i * 4 + 3] > 0 for i in range(64) if i != 3 * 8 + 3))   # a soft relief round it

    def test_dome_parts_and_their_records_on_synthetic_frames(self):
        with tempfile.TemporaryDirectory() as tmp:
            local = Path(tmp)
            (local / "masters").mkdir()
            pngio.write(local / "masters" / f"{sk.DOME_PLATE}.png", flat_frame(138, 138, (30, 40, 10)))
            pngio.write(local / "masters" / f"{sk.DOME_RING}.png", flat_frame(198, 192, (180, 140, 60)))
            group = [b for b in sk.SMALL if b.name in ("skiladdbut", "skilminusbut")]
            frames = local / "frames"
            for b in group:
                (frames / b.folder).mkdir(parents=True)
                for k in (0, 1, 2):
                    pngio.write(frames / b.folder / f"f{k}.png", self.frame())
            parts = sk.dome_parts(local, frames, group)
            for b in group:
                for ident in (f"plate.{b.name}", f"glyph.{b.name}", f"plate.{b.name}_hover", f"plate.{b.name}_pressed"):
                    self.assertEqual((parts[ident].w, parts[ident].h), b.master, ident)
                self.assertTrue(any(a > 0 for a in parts[f"glyph.{b.name}"].data[3::4]))   # the symbol is there
            args = ARGS()
            args.pack, args.local_root = local, local
            out = local / "g.png"
            pngio.write(out, parts["glyph.skiladdbut"])
            record = sk.record_of("plate.skiladdbut", group[0], None, out, args)
            self.assertEqual(len(record["source"]["face_sha256"]), 64)
            self.assertEqual(len(record["source"]["ring_sha256"]), 64)

    def test_dome_plate_puts_the_ring_over_the_face(self):
        with tempfile.TemporaryDirectory() as tmp:
            local = Path(tmp)
            (local / "masters").mkdir()
            pngio.write(local / "masters" / f"{sk.DOME_PLATE}.png", flat_frame(138, 138, (30, 40, 10)))
            ring = pngio.Image.blank(198, 192)
            for y in range(192):
                for x in range(198):
                    if sk.dome_radius(x, y, 198, 192) > 85:
                        ring.data[(y * 198 + x) * 4:(y * 198 + x) * 4 + 4] = bytes((180, 140, 60, 255))
            pngio.write(local / "masters" / f"{sk.DOME_RING}.png", ring)
            plate = sk.dome_plate(local, (102, 108))
            self.assertEqual((plate.w, plate.h), (102, 108))
            self.assertEqual(plate.pixel(51, 54)[:3], (30, 40, 10))
            self.assertEqual(plate.pixel(2, 54)[:3], (180, 140, 60))
            self.assertEqual(plate.pixel(0, 0)[3], 0)


class SheetTest(unittest.TestCase):
    def test_every_codex_button_state_has_exactly_one_cell(self):
        for b in sk.GENERATED:
            for state in b.frames:
                sheet, k = sk.sheet_for(b.name, state, "sp-b")
                self.assertEqual(sheet.cells[k], (b.name, state))
        self.assertEqual(sk.sheet_for("sp_minus", "idle", "sp-a")[0].id, "sp-a")
        self.assertEqual(sk.sheet_for("sp_minus", "idle", "sp-b")[0].id, "sp-b")
        with self.assertRaises(sk.SmallError):
            sk.sheet_for("sp_minus", "hover")

    def test_the_layout_of_each_sheet_is_pinned(self):
        # the cell of a button state in the sheet as it was laid out for Codex (columns idle, pressed, hover; fs: up and down side by side, rows normal, pressed, off)
        self.assertEqual(sk.sheet_for("oldchar_plus", "hover")[1], 5)
        self.assertEqual(sk.sheet_for("oldchar_minus", "idle")[1], 0)
        self.assertEqual(sk.sheet_for("scrlldwn", "pressed")[1], 4)
        self.assertEqual((sk.sheet_for("follow_scroll_dwn", "pressed")[0].id, sk.sheet_for("follow_scroll_dwn", "pressed")[1]), ("fs", 3))
        self.assertEqual(sk.sheet_for("follow_scroll_up_off", "idle")[1], 4)
        self.assertEqual(sk.sheet_for("sp_plus", "pressed", "sp-a")[1], 3)
        self.assertEqual(sk.sheet_for("sp_minus", "pressed", "sp-b")[1], 1)

    def test_every_sheet_grid_holds_its_cells(self):
        for sheet in sk.SHEETS:
            self.assertEqual(sheet.cols * sheet.rows, len(sheet.cells), sheet.id)

    def test_a_cell_is_cut_by_equal_division(self):
        image = pngio.Image(40, 20, bytearray(40 * 20 * 4))
        for y in range(20):
            for x in range(40):
                image.data[(y * 40 + x) * 4:(y * 40 + x) * 4 + 4] = bytes((x // 20 * 100, y // 10 * 100, 0, 255))
        sheet = sk.Sheet("t", 2, 2, (("a", "idle"),) * 4)
        self.assertEqual(sk.sheet_cell(image, sheet, 3).pixel(0, 0)[:3], (100, 100, 0))
        self.assertEqual(sk.sheet_cell(image, sheet, 1).pixel(0, 0)[:3], (100, 0, 0))

    def test_triangle_clip_cuts_the_ground_and_a_rim_and_keeps_the_inside(self):
        cell = pngio.Image(30, 30, bytearray(b"\xff\x00\xff\xff" * 900))
        for y in range(5, 25):
            for x in range(8, 22):
                cell.data[(y * 30 + x) * 4:(y * 30 + x) * 4 + 4] = bytes((40, 90, 10, 255))
        # a pink anti-aliased edge pixel is ground too
        cell.data[(10 * 30 + 8) * 4:(10 * 30 + 8) * 4 + 3] = bytes((200, 60, 200))
        out = sk.triangle_clip(cell)
        self.assertEqual(out.pixel(15, 15)[3], 255)
        self.assertEqual(out.pixel(0, 0)[3], 0)
        self.assertEqual(out.pixel(8, 10)[3], 0)
        self.assertEqual(out.pixel(8 + sk.TRIANGLE_ERODE + 1, 15)[3], 255)

    def test_a_broken_provenance_record_is_a_small_error_and_a_good_one_is_kept(self):
        b = sk.button_named("sp_plus")
        with tempfile.TemporaryDirectory() as tmp:
            sheet = Path(tmp) / "sp-a.png"
            pngio.write(sheet, flat_frame(8, 8, (1, 2, 3)))
            args = ARGS()
            args.pack, args.local_root = Path(tmp), Path(tmp)
            (Path(tmp) / "sp-a.provenance.json").write_text("{not json", encoding="utf-8")
            with self.assertRaises(sk.SmallError):
                sk.record_of("plate.sp_plus", b, sheet, sheet, args)
            (Path(tmp) / "sp-a.provenance.json").write_text('{"generator": "codex"}', encoding="utf-8")
            record = sk.record_of("plate.sp_plus", b, sheet, sheet, args)
            self.assertEqual(record["source"]["generation"]["generator"], "codex")
            self.assertEqual(record["source"]["sheet"], "sp-a.png")

    def test_codex_parts_cut_reduce_and_give_an_empty_glyph(self):
        b = sk.button_named("sp_plus")
        with tempfile.TemporaryDirectory() as tmp:
            sheets = Path(tmp)
            image = pngio.Image(400, 200, bytearray(400 * 200 * 4))
            colours = {0: (10, 0, 0), 1: (20, 0, 0), 2: (30, 0, 0), 3: (40, 0, 0)}
            for y in range(200):
                for x in range(400):
                    image.data[(y * 400 + x) * 4:(y * 400 + x) * 4 + 4] = bytes((*colours[(y // 100) * 2 + x // 200], 255))
            pngio.write(sheets / "sp-a.png", image)
            parts, sources = sk.codex_parts(sheets, [b], "sp-a")
            self.assertEqual(parts["plate.sp_plus"].pixel(5, 5), (30, 0, 0, 255))
            self.assertEqual(parts["plate.sp_plus_pressed"].pixel(5, 5), (40, 0, 0, 255))
            self.assertEqual((parts["plate.sp_plus"].w, parts["plate.sp_plus"].h), b.master)
            self.assertTrue(all(a == 0 for a in parts["glyph.sp_plus"].data[3::4]))
            self.assertEqual(sources["plate.sp_plus"].name, "sp-a.png")
            with self.assertRaises(sk.SmallError):
                sk.codex_parts(sheets / "none", [b], "sp-a")


class PackTest(unittest.TestCase):
    def test_parts_are_read_from_the_pack_and_the_glyph_is_empty(self):
        with tempfile.TemporaryDirectory() as tmp:
            pack = synthetic_pack(Path(tmp), RECT, lambda state, w, h: flat_frame(w, h, (10, 20, 30) if state == "idle" else (90, 20, 30)))
            parts, sources = sk.button_parts(pack, sk.pack_index(pack), RECT)
            self.assertEqual(set(parts), {"plate.slab", "plate.slab_pressed", "glyph.slab"})
            self.assertEqual(parts["plate.slab_pressed"].pixel(3, 3), (90, 20, 30, 255))
            self.assertEqual(sum(parts["glyph.slab"].data[3::4]), 0)
            self.assertEqual((parts["glyph.slab"].w, parts["glyph.slab"].h), RECT.master)
            self.assertEqual({k: v.name for k, v in sources.items()}, {"plate.slab": "slab-f000.png", "plate.slab_pressed": "slab-f001.png"})

    def test_a_missing_state_is_an_error(self):
        with tempfile.TemporaryDirectory() as tmp:
            pack = synthetic_pack(Path(tmp), RECT, lambda state, w, h: flat_frame(w, h, (1, 2, 3)))
            lines = (pack / "index.tsv").read_text(encoding="utf-8").splitlines()
            (pack / "index.tsv").write_text("\n".join(lines[:2]) + "\n", encoding="utf-8")
            with self.assertRaises(sk.SmallError):
                sk.button_parts(pack, sk.pack_index(pack), RECT)

    def test_a_frame_of_the_wrong_size_is_an_error(self):
        with tempfile.TemporaryDirectory() as tmp:
            pack = synthetic_pack(Path(tmp), RECT, lambda state, w, h: flat_frame(w + 4, h, (1, 2, 3)))
            with self.assertRaises(sk.SmallError):
                sk.button_parts(pack, sk.pack_index(pack), RECT)

    def test_no_pack_index_is_an_error_and_the_first_row_of_an_art_wins(self):
        with self.assertRaises(sk.SmallError):
            sk.pack_index(Path("/nonexistent-pack"))
        with tempfile.TemporaryDirectory() as tmp:
            pack = Path(tmp)
            (pack / "index.tsv").write_text("h\nscr\tk\tslab\t0\tfirst.png\nscr2\tk\tslab\t0\tsecond.png\n", encoding="utf-8")
            self.assertEqual(sk.pack_index(pack)[("slab", 0)], "first.png")

    def test_rect_parts_and_records_carry_the_pack_files_hash(self):
        with tempfile.TemporaryDirectory() as tmp:
            pack = synthetic_pack(Path(tmp), RECT, lambda state, w, h: flat_frame(w, h, (10, 20, 30)))
            args = ARGS()
            args.pack, args.local_root, args.frames, args.sheets, args.sp_variant = pack, Path(tmp), None, None, "sp-b"
            parts, sources = sk.build_parts(args, [RECT])
            out = Path(tmp) / "out"
            out.mkdir()
            pngio.write(out / "plate.slab.png", parts["plate.slab"])
            record = sk.record_of("plate.slab", RECT, sources["plate.slab"], out / "plate.slab.png", args)
            self.assertEqual(record["source"]["file"], "interface/slab-f000.png")
            self.assertEqual(len(record["source"]["sha256"]), 64)
            self.assertEqual(record["source_art"], 2)
            glyph = sk.record_of("glyph.slab", RECT, None, out / "plate.slab.png", args)
            self.assertIsNone(glyph["source"])


class WedgeTest(unittest.TestCase):
    PLATE, RED = (30, 28, 0), (92, 0, 0)

    def stair(self, left: bool) -> pngio.Image:
        """A synthetic follower cycle arrow: a flat olive plate with a staircase triangle of 7 columns of 1, 3, ... 13 cells."""
        frame = flat_frame(12, 19, self.PLATE)
        for k in range(7):
            x = 2 + k if left else 9 - k
            for y in range(9 - k, 10 + k):
                frame.data[(y * 12 + x) * 4:(y * 12 + x) * 4 + 4] = bytes((*self.RED, 255))
        return frame

    def test_the_fit_spans_the_cells_and_keeps_their_area(self):
        for left in (True, False):
            wedge = sk.fit_wedge(self.stair(left))
            self.assertEqual((wedge.tip, wedge.base), (2, 9) if left else (10, 3))
            self.assertEqual((wedge.axis, wedge.colour), (9.5, self.RED))
            self.assertAlmostEqual(abs(wedge.base - wedge.tip) * wedge.half, 49)

    def test_the_face_keeps_the_plate_and_draws_a_straight_triangle(self):
        for left in (True, False):
            plate = flat_frame(48, 76, (7, 8, 9))
            face = sk.wedge_face(self.stair(left), plate)
            self.assertEqual(face.pixel(2, 2), (7, 8, 9, 255))
            self.assertEqual(face.pixel(24, 38), (*self.RED, 255))
            covered = sum(1 for y in range(76) for x in range(48) if face.pixel(x, y)[:3] == self.RED)
            self.assertAlmostEqual(covered / 16, 49, delta=4)
            heights = [sum(1 for y in range(76) if face.pixel(x, y)[0] > 50) for x in (range(10, 34) if left else range(37, 13, -1))]
            self.assertEqual(heights, sorted(heights))
            self.assertLess(max(b - a for a, b in zip(heights, heights[1:])), 4, "no stair of a whole cell (4 px) is left")

    def test_the_empty_plate_keeps_the_corners_and_covers_the_whole_shape(self):
        cell = flat_frame(584, 449, (40, 40, 10))
        for y in range(20):
            for x in range(20):
                cell.data[(y * 584 + x) * 4:(y * 584 + x) * 4 + 4] = bytes((200, 100, 0, 255))
        plate = sk.empty_plate(cell, (12, 19), (42, 66))
        self.assertEqual((plate.w, plate.h), (42, 66))
        self.assertEqual(plate.pixel(0, 0), (200, 100, 0, 255))
        self.assertEqual(plate.pixel(21, 33), (40, 40, 10, 255))
        self.assertEqual(set(plate.data[3::4]), {255})
        with self.assertRaises(sk.SmallError):
            sk.empty_plate(flat_frame(150, 150, (1, 1, 1)), (12, 19), (42, 66))

    def test_the_pressed_plate_darkens_only_where_the_original_does(self):
        idle = flat_frame(12, 19, self.PLATE)
        pressed = pngio.Image(12, 19, bytearray(idle.data))
        pressed.data[:19 * 0 + 12 * 4] = bytes((0, 0, 0, 255)) * 12
        shaded = sk.pressed_shade(idle, pressed, flat_frame(48, 76, (100, 100, 100)))
        self.assertLess(shaded.pixel(24, 1)[0], 20)
        self.assertEqual(shaded.pixel(24, 40), (100, 100, 100, 255))

    def test_the_parts_are_full_faces_with_an_empty_glyph(self):
        b = sk.button_named("followercycleleft")
        with tempfile.TemporaryDirectory() as tmp:
            (Path(tmp) / b.folder).mkdir()
            for number in b.frames.values():
                pngio.write(Path(tmp) / b.folder / f"f{number}.png", self.stair(True))
            parts = sk.wedge_parts(Path(tmp), flat_frame(584, 449, (40, 40, 10)), [b])
        self.assertEqual(set(parts), {f"plate.{b.name}", f"plate.{b.name}_hover", f"plate.{b.name}_pressed", f"glyph.{b.name}"})
        self.assertEqual(sk.face(parts, b, "hover").data, parts[f"plate.{b.name}_hover"].data)
        self.assertEqual(sum(parts[f"glyph.{b.name}"].data[3::4]), 0)

    def test_a_frame_without_a_sideways_triangle_is_an_error(self):
        for colour in (self.PLATE, self.RED):   # no red cell at all; a red rectangle (both ends as tall)
            with self.assertRaises(sk.SmallError):
                sk.fit_wedge(flat_frame(12, 19, colour))

    def test_a_shifted_triangle_keeps_its_shift(self):
        frame = self.stair(True)
        shifted = pngio.Image(12, 19, bytearray(flat_frame(12, 19, self.PLATE).data))
        shifted.data[12 * 4:] = frame.data[:-12 * 4]
        self.assertEqual((sk.fit_wedge(shifted).axis, sk.fit_wedge(shifted).half), (10.5, 7.0))

    def test_the_build_path_and_records_name_the_plate_sheet_and_the_frames(self):
        b = sk.button_named("followercycleleft")
        with tempfile.TemporaryDirectory() as tmp:
            (Path(tmp) / b.folder).mkdir()
            for number in b.frames.values():
                pngio.write(Path(tmp) / b.folder / f"f{number}.png", self.stair(True))
            pngio.write(Path(tmp) / "ol.png", flat_frame(1752, 898, (40, 40, 10)))
            args = ARGS()
            args.pack, args.local_root, args.frames, args.sheets, args.sp_variant = Path(tmp) / "none", Path(tmp), Path(tmp), Path(tmp), "sp-b"
            parts, sources = sk.build_parts(args, [b])
            self.assertEqual(set(sources), set(parts))
            path = Path(tmp) / "part.png"
            pngio.write(path, parts[f"plate.{b.name}"])
            source = sk.record_of(f"plate.{b.name}", b, sources[f"plate.{b.name}"], path, args)["source"]
            self.assertEqual((source["sheet"], source["frames"], len(source["sha256"])), ("ol.png", b.folder, 64))
            glyph = sk.record_of(f"glyph.{b.name}", b, sources[f"glyph.{b.name}"], path, args)
            self.assertIsNone(glyph["source"])
            self.assertIn("empty glyph", glyph["rule"])


class CutOutTest(unittest.TestCase):
    """The parchment and ink families on synthetic sheets (no art)."""

    def frames(self, tmp: Path, b: sk.Small, paint) -> None:
        (tmp / b.folder).mkdir()
        for state, number in b.frames.items():
            pngio.write(tmp / b.folder / f"f{number}.png", paint(state))

    @staticmethod
    def block(image: pngio.Image, x0: int, y0: int, x1: int, y1: int, rgb: tuple) -> pngio.Image:
        for y in range(y0, y1):
            for x in range(x0, x1):
                image.data[(y * image.w + x) * 4:(y * image.w + x) * 4 + 4] = bytes((*rgb, 255))
        return image

    def test_the_parchment_is_cut_into_the_originals_place_and_pressed_moves_it_down(self):
        b = sk.button_named("char_schem_skills")
        sheet = flat_frame(240, 120, (255, 0, 255))
        for k, symbol in enumerate(((90, 45, 10), (230, 20, 10))):
            self.block(sheet, k * 120 + 20, 20, k * 120 + 100, 100, (170, 140, 100))
            self.block(sheet, k * 120 + 50, 45, k * 120 + 70, 65, symbol)
        sheet.data[(119 * 240) * 4:(119 * 240) * 4 + 4] = bytes((90, 90, 90, 255))   # a stray corner pixel is not the parchment's edge
        with tempfile.TemporaryDirectory() as tmp:
            self.frames(Path(tmp), b, lambda state: self.block(flat_frame(32, 32, (80, 80, 82)), 6, 4, 26, 28, (170, 140, 100)))
            parts = sk.paper_parts(Path(tmp), sheet, b)
        idle, hover, pressed = (parts[f"plate.{b.name}{s}"] for s in ("", "_hover", "_pressed"))
        solid = [(x, y) for y in range(114) for x in range(114) if idle.pixel(x, y)[3] >= 128]
        self.assertAlmostEqual(min(x for x, _ in solid), 6 * 114 / 32, delta=1.5)
        self.assertAlmostEqual(max(y for _, y in solid), 28 * 114 / 32, delta=1.5)
        self.assertEqual(idle.pixel(2, 2)[3], 0)
        self.assertEqual(hover.data[3::4], idle.data[3::4])
        self.assertEqual(pressed.data[3::4], idle.data[3::4])
        red_top = lambda image: min(y for y in range(114) if image.pixel(60, y)[0] > 200 and image.pixel(60, y)[1] < 60)  # noqa: E731
        self.assertEqual(red_top(pressed) - red_top(hover), 4)

    def test_a_parchment_sheet_without_the_magenta_ground_is_an_error(self):
        with self.assertRaises(sk.SmallError):
            sk.cut_from_magenta(flat_frame(60, 60, (255, 255, 255)))

    def test_the_arrow_is_one_shape_in_the_two_ink_colours(self):
        b = sk.button_named("schm_larrow")
        sheet = flat_frame(1600, 980, (255, 255, 255))
        self.block(sheet, 100, 200, 700, 800, (30, 5, 0))
        with tempfile.TemporaryDirectory() as tmp:
            self.frames(Path(tmp), b, lambda state: self.block(flat_frame(18, 22, (170, 150, 110)), 2, 3, 16, 20, (120, 85, 30) if state == "idle" else (30, 5, 0)))
            parts = sk.ink_parts(Path(tmp), sheet, b, None)
        idle, pressed = parts[f"plate.{b.name}"], parts[f"plate.{b.name}_pressed"]
        self.assertEqual(idle.data[3::4], pressed.data[3::4])
        self.assertEqual(idle.pixel(33, 39)[:3], (120, 85, 30))
        self.assertEqual(pressed.pixel(33, 39)[:3], (30, 5, 0))
        self.assertEqual(idle.pixel(1, 1)[3], 0)

    def test_the_trim_cuts_to_the_line_and_fills_the_notch_under_it(self):
        sheet = flat_frame(20, 120, (255, 255, 255))
        self.block(sheet, 0, 0, 20, 4, (0, 0, 0))          # ink above the line
        self.block(sheet, 0, 30, 20, 40, (10, 0, 0))       # ink under it, a notch between
        out = sk.trimmed(sheet, (5, 10, 5, 20, 0.0))
        self.assertEqual([out.pixel(7, y)[0] for y in (2, 19, 20, 29, 35, 50)], [255, 255, 10, 10, 10, 255])
        self.assertEqual([out.pixel(2, y)[0] for y in (2, 25)], [0, 255])

    def test_another_arrow_sheet_is_refused(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "glyph-schem-arrows.png"
            pngio.write(path, flat_frame(8, 8, (255, 255, 255)))
            with self.assertRaises(sk.SmallError):
                sk.ink_trim(path, "schm_larrow")

    def test_the_build_path_of_the_parchment_records_its_sheet(self):
        b = sk.button_named("char_schem_skills")
        sheet = flat_frame(240, 120, (255, 0, 255))
        for k in (0, 1):
            self.block(sheet, k * 120 + 20, 20, k * 120 + 100, 100, (170, 140, 100))
        with tempfile.TemporaryDirectory() as tmp:
            self.frames(Path(tmp), b, lambda state: self.block(flat_frame(32, 32, (80, 80, 82)), 6, 4, 26, 28, (170, 140, 100)))
            pngio.write(Path(tmp) / f"{sk.PAPER_SHEET}.png", sheet)
            args = ARGS()
            args.pack, args.local_root, args.frames, args.sheets, args.sp_variant = Path(tmp) / "none", Path(tmp), Path(tmp), Path(tmp), "sp-b"
            parts, sources = sk.build_parts(args, [b])
            path = Path(tmp) / "part.png"
            pngio.write(path, parts[f"plate.{b.name}"])
            record = sk.record_of(f"plate.{b.name}", b, sources[f"plate.{b.name}"], path, args)
            self.assertEqual((record["source"]["sheet"], record["source"]["frames"]), (f"{sk.PAPER_SHEET}.png", b.folder))
            self.assertIsNone(sk.record_of(f"glyph.{b.name}", b, sources[f"glyph.{b.name}"], path, args)["source"])


class TableTest(unittest.TestCase):
    def test_every_button_matches_its_manifest_row(self):
        manifest = {r["id"]: r for r in json.loads(sk.MANIFEST.read_text(encoding="utf-8"))["parts"]}
        for b in (*sk.SMALL, *sk.GENERATED):
            row = manifest[f"plate.{b.name}"]
            self.assertEqual(row["size"], list(b.master), b.name)
            self.assertEqual(row["source_arts"], [str(b.art)], b.name)
            self.assertEqual(b.folder, f"{b.art}-{b.folder.split('-', 1)[1]}", b.name)
            self.assertEqual(b.folder.split("-", 1)[1].lower(), b.name, b.name)

    def test_names_are_unique_and_the_families_are_known(self):
        names = [b.name for b in (*sk.SMALL, *sk.GENERATED)]
        self.assertEqual(len(names), len(set(names)))
        self.assertTrue(all(b.family in ("rect", "paper", "ink", "wedge", "dome") for b in sk.SMALL))
        self.assertEqual({b.name for b in sk.SMALL if b.family == "rect"}, {"spelltech_add", "spelltech_minus", "followerarrow_dwn", "followerarrow_up"})   # the pack frames kept
        self.assertEqual({b.name for b in sk.SMALL if b.family in ("paper", "ink", "wedge")}, {"char_schem_skills", "schm_larrow", "schm_rarrow", "followercycleleft", "followercycleright"})
        self.assertTrue(all(b.family == "codex" for b in sk.GENERATED))
        self.assertTrue(all("idle" in b.frames for b in (*sk.SMALL, *sk.GENERATED)))

    def test_build_kit_rows_cover_every_glyph_and_overlay(self):
        every = (*sk.SMALL, *sk.GENERATED)
        plates = {f"plate.{b.name}": {"anchor": "window", "used_by": ["G01"]} for b in every}
        rows = {r["id"]: r for r in build_kit.small_parts(plates)}
        for b in every:
            self.assertIn(f"glyph.{b.name}", rows)
            for state in ("hover", "pressed"):
                self.assertEqual(f"plate.{b.name}_{state}" in rows, state in b.frames, (b.name, state))
        self.assertTrue(all(r["status"] == "master" and r["provenance"].split(":")[1] == "small_kit" for r in rows.values()))
        self.assertTrue(rows["plate.sp_plus_pressed"]["provenance"].startswith("codex:") and rows["glyph.skiladdbut"]["provenance"].startswith("rule:"))

    def test_the_masters_stretch_a_frame_by_a_few_percent_at_most(self):
        # the masters are snapped to the 6 px grid in kit.json: the original's aspect and the master's differ by at most 12 % (a pack frame is 4 x the units, a Codex cell is cut at the original's aspect)
        for b in (*sk.SMALL, *sk.GENERATED):
            self.assertLess(abs((b.master[0] / b.units[0]) / (b.master[1] / b.units[1]) - 1.0), 0.12, b.name)

    def test_resampling_at_a_fractional_ratio_keeps_a_flat_colour_and_an_edge(self):
        frame = pngio.Image(116, 120, bytearray(116 * 120 * 4))
        for y in range(120):
            for x in range(57):
                frame.data[(y * 116 + x) * 4:(y * 116 + x) * 4 + 4] = bytes((100, 50, 20, 255))
        out = sk.resample(frame, 102, 108)
        self.assertEqual(out.pixel(10, 50), (100, 50, 20, 255))
        self.assertEqual(out.pixel(90, 50)[3], 0)
        mid = out.pixel(50, 50)
        self.assertTrue(0 < mid[3] < 255 and mid[:3] == (100, 50, 20))   # a half-covered pixel keeps the colour, not a darkened one

    def test_owner_of_a_part_id(self):
        self.assertEqual(sk.owner_of("plate.scrllup_hover").name, "scrllup")
        self.assertEqual(sk.owner_of("plate.sp_plus_pressed").name, "sp_plus")
        self.assertEqual(sk.owner_of("glyph.skiladdbut").name, "skiladdbut")
        self.assertEqual(sk.owner_of("plate.follow_scroll_up").name, "follow_scroll_up")

    def test_unknown_button_is_an_error(self):
        with self.assertRaises(sk.SmallError):
            sk.button_named("nope")

    def test_self_test_passes(self):
        self.assertEqual(sk.self_test(), 0)


if __name__ == "__main__":
    unittest.main()
