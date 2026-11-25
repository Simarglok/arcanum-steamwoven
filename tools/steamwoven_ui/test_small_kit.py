"""ARC-UI-3206: tests of the small button kit tool (pack-first rule) on a synthetic pack (no art)."""
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


ROUND = sk.Small("probe", 1, "1-probe", "round", (10, 10), (30, 30))
RECT = sk.Small("slab", 2, "2-slab", "rect", (12, 6), (36, 18), frames={"idle": 0, "pressed": 1})
OWN = sk.Small("arrow", 3, "3-arrow", "own", (6, 8), (20, 28))


class AlphaTest(unittest.TestCase):
    def test_ellipse_is_solid_inside_clear_outside_and_smooth_between(self):
        a = sk.ellipse_alpha((0.0, 0.0, 40.0, 40.0), (40, 40), (60, 60))
        self.assertEqual(a[30 * 60 + 30], 1.0)
        self.assertEqual(a[0], 0.0)
        edge = [v for v in a[30 * 60:31 * 60] if 0.0 < v < 1.0]
        self.assertLessEqual(len(edge), 2)    # one pixel of anti-aliasing per side of the outline
        self.assertEqual(a[30 * 60 + 5], a[30 * 60 + 54])   # symmetric

    def test_ellipse_follows_a_wider_box(self):
        a = sk.ellipse_alpha((0.0, 0.0, 40.0, 20.0), (40, 20), (80, 40))
        self.assertEqual(a[20 * 80 + 40], 1.0)
        self.assertEqual(a[0], 0.0)

    def test_opaque_box_is_the_silhouette(self):
        self.assertEqual(sk.opaque_box(disc_frame(40, 40)), (0.0, 0.0, 40.0, 40.0))
        with self.assertRaises(sk.SmallError):
            sk.opaque_box(pngio.Image(4, 4, bytearray(64)))

    def test_bleed_fills_the_transparent_ring_with_the_edge_colour_and_keeps_opaque_pixels(self):
        image = disc_frame(40, 40)
        out = sk.bleed(image)
        self.assertEqual(out.pixel(20, 20), image.pixel(20, 20))
        corner = out.pixel(4, 4)
        self.assertEqual(corner[3], 255)
        self.assertNotEqual(corner[:3], (5, 5, 5))   # the dark of the old corner is gone near the disc
        near = [out.pixel(x, y) for y in range(40) for x in range(40) if image.pixel(x, y)[3] == 0 and out.pixel(x, y)[3] == 255]
        self.assertTrue(near)


class PartTest(unittest.TestCase):
    def test_round_part_has_an_ellipse_alpha_and_no_dark_fringe(self):
        box = sk.opaque_box(disc_frame(40, 40))
        part = sk.part_from_frame(ROUND, disc_frame(40, 40), box)
        self.assertEqual((part.w, part.h), (30, 30))
        self.assertEqual(part.pixel(15, 15)[3], 255)
        self.assertEqual(part.pixel(0, 0)[3], 0)
        for x in range(30):   # every pixel that shows is the plate or the rim, never the dark of the old corner
            p = part.pixel(x, 15)
            if p[3] > 0:
                self.assertGreater(p[0] + p[1] + p[2], 150)

    def test_every_state_has_the_idle_frames_edge(self):
        idle = disc_frame(40, 40)
        pressed = disc_frame(40, 40, colour=(20, 20, 10))
        box = sk.opaque_box(idle)
        a = [p for p in sk.part_from_frame(ROUND, idle, box).data[3::4]]
        b = [p for p in sk.part_from_frame(ROUND, pressed, box).data[3::4]]
        self.assertEqual(a, b)

    def test_rect_part_is_the_frame_resampled_opaque(self):
        part = sk.part_from_frame(RECT, flat_frame(48, 24, (10, 20, 30)), (0.0, 0.0, 48.0, 24.0))
        self.assertEqual((part.w, part.h), (36, 18))
        self.assertTrue(all(part.pixel(x, y) == (10, 20, 30, 255) for y in range(18) for x in range(36)))

    def test_own_part_keeps_the_frames_alpha(self):
        frame = pngio.Image(24, 32, bytearray(24 * 32 * 4))
        for y in range(32):
            for x in range(8, 16):
                frame.data[(y * 24 + x) * 4:(y * 24 + x) * 4 + 4] = bytes((40, 80, 20, 255))
        part = sk.part_from_frame(OWN, frame, sk.opaque_box(frame))
        self.assertEqual((part.w, part.h), (20, 28))
        self.assertEqual(part.pixel(10, 14), (40, 80, 20, 255))
        self.assertEqual(part.pixel(1, 14)[3], 0)


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

    def test_build_writes_masters_and_records_with_the_pack_files_hash(self):
        with tempfile.TemporaryDirectory() as tmp:
            pack = synthetic_pack(Path(tmp), RECT, lambda state, w, h: flat_frame(w, h, (10, 20, 30)))
            parts, sources = sk.build_parts(pack, [RECT])
            out = Path(tmp) / "out"
            out.mkdir()
            pngio.write(out / "plate.slab.png", parts["plate.slab"])
            record = sk.record_of("plate.slab", RECT, sources["plate.slab"], out / "plate.slab.png", pack)
            self.assertEqual(record["source"]["file"], "interface/slab-f000.png")
            self.assertEqual(len(record["source"]["sha256"]), 64)
            self.assertEqual(record["source_art"], 2)
            glyph = sk.record_of("glyph.slab", RECT, None, out / "plate.slab.png", pack)
            self.assertIsNone(glyph["source"])


class TableTest(unittest.TestCase):
    def test_every_button_matches_its_manifest_row(self):
        manifest = {r["id"]: r for r in json.loads(sk.MANIFEST.read_text(encoding="utf-8"))["parts"]}
        for b in (*sk.SMALL, *sk.GENERATED):
            row = manifest[f"plate.{b.name}"]
            self.assertEqual(row["size"], list(b.master), b.name)
            self.assertEqual(row["source_arts"], [str(b.art)], b.name)
            self.assertEqual(b.folder, f"{b.art}-{b.folder.split('-', 1)[1]}", b.name)
            self.assertEqual(b.folder.split("-", 1)[1].lower(), b.name, b.name)

    def test_names_are_unique_and_the_generated_ones_have_no_pack_family(self):
        names = [b.name for b in (*sk.SMALL, *sk.GENERATED)]
        self.assertEqual(len(names), len(set(names)))
        self.assertTrue(all(b.family != "none" for b in sk.SMALL))
        self.assertTrue(all(b.family == "none" for b in sk.GENERATED))
        self.assertTrue(all("idle" in b.frames for b in (*sk.SMALL, *sk.GENERATED)))

    def test_build_kit_rows_cover_every_glyph_and_overlay(self):
        plates = {f"plate.{b.name}": {"anchor": "window", "used_by": ["G01"]} for b in sk.SMALL}
        rows = {r["id"]: r for r in build_kit.small_parts(plates)}
        for b in sk.SMALL:
            self.assertIn(f"glyph.{b.name}", rows)
            for state in ("hover", "pressed"):
                self.assertEqual(f"plate.{b.name}_{state}" in rows, state in b.frames, (b.name, state))
        self.assertTrue(all(r["status"] == "master" and r["provenance"].startswith("rule:small_kit:") for r in rows.values()))

    def test_owner_of_a_part_id(self):
        self.assertEqual(sk.owner_of("plate.scrllup_hover").name, "scrllup")
        self.assertEqual(sk.owner_of("glyph.skiladdbut").name, "skiladdbut")
        self.assertEqual(sk.owner_of("plate.follow_scroll_up").name, "follow_scroll_up")

    def test_unknown_button_is_an_error(self):
        with self.assertRaises(sk.SmallError):
            sk.button_named("nope")

    def test_self_test_passes(self):
        self.assertEqual(sk.self_test(), 0)


if __name__ == "__main__":
    unittest.main()
