"""ARC-UI-3205: tests of the parchment oval box kit tool (pack-first) on a synthetic HD pack (no art)."""
from __future__ import annotations

import io
import json
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path

from . import parchment_kit as pk
from . import pngio

W, H = pk.MASTER


def colour_of(name: str) -> tuple[int, int, int]:
    return (40 + (sum(map(ord, name)) * 7) % 200, 90, 60 + (len(name) * 13) % 150)


def make_local(root: Path, skip: str | None = None, size: tuple[int, int] = pk.PACK_SIZE) -> Path:
    """A synthetic pack beside a local root: every frame is flat in a colour of its name."""
    local = root / "steamwoven-ui"
    local.mkdir()
    for box in pk.BOXES:
        for ident in pk.part_ids(box):
            name = pk.pack_name(ident)
            if name == skip:
                continue
            folder = root / "hd-packs" / "interface-redraw-4x" / "interface" / "inventory" / ("panels" if name.endswith("_ill") else "buttons")
            folder.mkdir(parents=True, exist_ok=True)
            pngio.write(folder / f"{name}-f000.png", pngio.Image(size[0], size[1], bytearray(bytes((*colour_of(name), 255)) * (size[0] * size[1]))))
    return local


def at(image: pngio.Image, x: int, y: int) -> tuple[int, int, int, int]:
    return tuple(image.data[(y * image.w + x) * 4:(y * image.w + x) * 4 + 4])


class AlphaTests(unittest.TestCase):
    def test_the_alpha_is_the_ring_united_with_the_corner_triangles(self) -> None:
        self.assertEqual(pk.coverage(0, 0), 1.0)                       # the very corner: a triangle
        self.assertEqual(pk.coverage(W - 1, H - 1), 1.0)
        self.assertEqual(pk.coverage(W // 2, H // 2), 1.0)             # inside the ring
        self.assertEqual(pk.coverage(W // 2, 1), 1.0)                  # the ring reaches the middle of the top ...
        self.assertEqual(pk.coverage(W // 2, H - 2), 1.0)              # ... and of the bottom
        self.assertEqual(pk.coverage(W - 1, H // 2), 0.0)              # beside the ring at the middle of a side: old wood, clear
        self.assertEqual(pk.coverage(0, H // 2), 0.0)

    def test_between_the_ring_and_a_corner_there_is_old_wood_that_is_clear(self) -> None:
        gaps = [(x, y) for y in range(H) for x in range(W) if pk.coverage(x, y) == 0.0]
        self.assertGreater(len(gaps), 200)
        for x, y in gaps:                                              # a clear pixel is clear of both: outside the ring, inside the corner ellipse
            px, py = x + 0.5 - pk.CENTRE[0], y + 0.5 - pk.CENTRE[1]
            self.assertGreater((px / pk.RING[0]) ** 2 + (py / pk.RING[1]) ** 2, 0.97)
            self.assertLess((px / pk.CORNER[0]) ** 2 + (py / pk.CORNER[1]) ** 2, 1.03)

    def test_the_edges_are_anti_aliased_and_the_plane_has_one_value_per_pixel(self) -> None:
        plane = pk.alpha_plane()
        self.assertEqual(len(plane), W * H)
        partial = [v for v in plane if 0.0 < v < 1.0]
        self.assertGreater(len(partial), 100)                          # an analytic curve, not a stair
        self.assertLess(len(partial), W * H // 8)

    def test_the_four_corners_are_alike(self) -> None:
        self.assertGreater(sum(1 for y in range(H // 2) for x in range(W // 2) if pk.coverage(x, y) == 0.0), 0)
        left = sum(pk.coverage(x, y) for y in range(H) for x in range(20))
        right = sum(pk.coverage(W - 1 - x, y) for y in range(H) for x in range(20))
        self.assertAlmostEqual(left, right, delta=left * 0.15)         # the ring is a pixel and a half off the middle: nearly the same


class BuildTests(unittest.TestCase):
    def test_every_part_is_its_pack_frame_with_the_analytic_alpha(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            parts = pk.build_parts(make_local(Path(tmp)))
            self.assertEqual(sorted(parts), sorted(pk.all_part_ids()))
            for ident, image in parts.items():
                self.assertEqual((image.w, image.h), pk.MASTER, ident)
                r, g, b, a = at(image, W // 2, H // 2)
                self.assertEqual(a, 255, ident)
                for got, want in zip((r, g, b), colour_of(pk.pack_name(ident))):
                    self.assertAlmostEqual(got, want, delta=2, msg=ident)   # the frame's colour (a flat frame stays flat through the resampling)
                self.assertEqual(at(image, 0, 0)[3], 255, ident)
                self.assertEqual(at(image, W - 1, H // 2), (0, 0, 0, 0), ident)   # old wood: clear, stored as 0 0 0 0

    def test_only_builds_one_box(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            self.assertEqual(sorted(pk.build_parts(make_local(Path(tmp)), {"use_box_off"})), ["plate.use_box_off", "state.use_box_ill", "state.use_box_on"])

    def test_a_missing_pack_frame_or_one_of_the_wrong_size_is_refused(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(pk.ParchmentError) as caught:
                pk.build_parts(make_local(Path(tmp), skip="use_box_ill"))
            self.assertIn("use_box_ill", str(caught.exception))
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(pk.ParchmentError):
                pk.build_parts(make_local(Path(tmp), size=(100, 50)))

    def test_main_build_writes_parts_and_records_and_dry_run_writes_nothing(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            local, out = make_local(root), root / "out"
            with redirect_stdout(io.StringIO()):
                self.assertEqual(pk.main(["build", "--local-root", str(local), "--out", str(out), "--dry-run"]), 0)
                self.assertFalse(out.exists())
                self.assertEqual(pk.main(["build", "--local-root", str(local), "--out", str(out)]), 0)
            self.assertEqual(len(list(out.glob("*.png"))), 9)
            record = json.loads((out / "records" / "state.drop_on.json").read_text(encoding="utf-8"))
            self.assertEqual(record["rule"], "parchment_kit")
            self.assertEqual(record["method"], "pack-first")
            self.assertEqual(record["image_sha256"], pk.sha256_file(out / "state.drop_on.png"))
            (source, digest), = record["sources"].items()
            self.assertTrue(source.endswith("drop_on-f000.png"))
            self.assertEqual(digest, pk.sha256_file(root / source))

    def test_an_unknown_box_or_a_missing_pack_exits_two(self) -> None:
        with tempfile.TemporaryDirectory() as tmp, redirect_stderr(io.StringIO()):
            root = Path(tmp)
            local = make_local(root)
            self.assertEqual(pk.main(["build", "--local-root", str(local), "--only", "nobox", "--dry-run"]), 2)
            self.assertEqual(pk.main(["build", "--local-root", str(root / "nowhere" / "steamwoven-ui"), "--dry-run"]), 2)

    def test_self_test(self) -> None:
        with redirect_stdout(io.StringIO()):
            self.assertEqual(pk.self_test(), 0)


class ManifestTests(unittest.TestCase):
    def test_the_manifest_lists_the_nine_parts_as_pack_first_masters_and_no_glyph(self) -> None:
        kit = json.loads((pk.HERE / "kit.json").read_text(encoding="utf-8"))
        rows = {p["id"]: p for p in kit["parts"]}
        for ident in pk.all_part_ids():
            self.assertEqual(rows[ident]["status"], "master", ident)
            self.assertEqual(rows[ident]["provenance"], f"rule:parchment_kit:{ident}")
            self.assertEqual(rows[ident]["size"], list(pk.MASTER))
        for box in pk.BOXES:
            self.assertNotIn(f"glyph.{box.name}", rows)               # the symbol is in the pack frame

    def test_the_inventory_surface_lays_the_two_ovals_and_has_no_interim_plate(self) -> None:
        spec = json.loads((pk.HERE / "zone_specs" / "ctx-inventory.json").read_text(encoding="utf-8"))
        self.assertNotIn("plate_boxes", spec)
        self.assertEqual([(p["id"], p["x"], p["y"]) for p in spec["kit_parts"]], [("plate.use_box_off", 2530, 1191), ("plate.drop_off", 2530, 1385)])
        for p in spec["kit_parts"]:
            self.assertEqual(p["file"], f"{p['id']}.png")


if __name__ == "__main__":
    unittest.main()
