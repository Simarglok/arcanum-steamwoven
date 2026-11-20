"""ARC-RES-2455: tests of the cut-out and tile tools on synthetic images (no art)."""
from __future__ import annotations

import hashlib
import io
import json
from contextlib import redirect_stderr, redirect_stdout
import shutil
import tempfile
import unittest
from pathlib import Path

from . import cutouts, pngio


def solid(w: int, h: int, rgb: tuple[int, int, int]) -> pngio.Image:
    return pngio.Image(w, h, bytearray(bytes((*rgb, 255)) * (w * h)))


def gradient(w: int, h: int) -> pngio.Image:
    data = bytearray()
    for y in range(h):
        for x in range(w):
            data += bytes((x * 255 // max(1, w - 1), y * 255 // max(1, h - 1), 80, 255))
    return pngio.Image(w, h, data)


class CutoutsTest(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.tmp, True)

    def test_real_list_is_valid(self) -> None:
        self.assertEqual([], cutouts.check(cutouts.load()))

    def test_check_finds_bad_rectangles_sources_and_tiles(self) -> None:
        spec = {"items": [{"id": "a", "source": "ctx-hud", "rect": [0, 0, 900, 10], "material": "x"}, {"id": "a", "source": "nope", "rect": [5, 5, 5, 9], "material": "x"}],
                "tiles": [{"id": "tile-wood", "from": "missing", "reference": {"screen": "ctx-hud", "material": "paper"}}]}
        text = "\n".join(cutouts.check(spec))
        for needle in ("duplicate", "outside the 800 x 600", "not a context screen", "not in the list", "no tone mask"):
            self.assertIn(needle, text)

    def test_pixel_rect_scales_with_the_source_width(self) -> None:
        self.assertEqual((100, 50, 200, 150), cutouts.pixel_rect([100, 50, 200, 150], 800, 600))
        self.assertEqual((181, 90, 362, 272), cutouts.pixel_rect([100, 50, 200, 150], 1448, 1086))
        x0, y0, x1, y1 = cutouts.pixel_rect([10, 10, 10.2, 10.2], 800, 600)  # never empty
        self.assertTrue(x1 > x0 and y1 > y0)

    def test_small_cutouts_get_a_context_crop_inside_the_screen(self) -> None:
        for rect in ([15, 20, 30, 35], [780, 580, 798, 598], [100, 300, 450, 307]):
            around = cutouts.context_rect(rect)
            self.assertTrue(0 <= around[0] < around[2] <= 800 and 0 <= around[1] < around[3] <= 600, around)
            self.assertTrue(around[0] <= rect[0] and around[1] <= rect[1] and around[2] >= rect[2] and around[3] >= rect[3], (rect, around))
            self.assertGreaterEqual(around[2] - around[0], 240)
            self.assertGreaterEqual(around[3] - around[1], 180)

    def test_cut_writes_pixels_and_provenance(self) -> None:
        local = self.tmp / "local"
        (local / "outbox" / cutouts.SOURCE_BATCH).mkdir(parents=True)
        source = gradient(1448, 1086)
        pngio.write(local / "outbox" / cutouts.SOURCE_BATCH / "ctx-hud.png", source)
        spec = {"items": [{"id": "piece", "source": "ctx-hud", "rect": [100, 50, 200, 150], "material": "wood"}], "tiles": []}
        spec["items"].append({"id": "big", "source": "ctx-hud", "rect": [0, 0, 400, 300], "material": "wood", "note": "n"})
        records = cutouts.cut(spec, local)
        record = records["piece"]
        self.assertIn("context_file", record)  # 100 x 100 layout units is small
        self.assertNotIn("context_file", records["big"])
        around = pngio.read(local / "material-sheet" / "cutouts" / "piece.context.png")
        self.assertEqual(tuple(record["context_size"]), (around.w, around.h))
        self.assertFalse((local / "material-sheet" / "cutouts" / "big.context.png").exists())
        stale = local / "material-sheet" / "cutouts" / "old-name.png"
        stale.write_bytes(b"x")
        cutouts.cut(spec, local)
        self.assertFalse(stale.exists())
        self.assertEqual([181, 90, 362, 272], record["pixel_rect"])
        piece = pngio.read(local / "material-sheet" / "cutouts" / "piece.png")
        self.assertEqual((181, 182), (piece.w, piece.h))
        self.assertEqual(source.pixel(181, 90), piece.pixel(0, 0))
        stored = json.loads((local / "material-sheet" / "cutouts" / "cutouts.provenance.json").read_text(encoding="utf-8"))
        self.assertEqual(hashlib.sha256((local / "outbox" / cutouts.SOURCE_BATCH / "ctx-hud.png").read_bytes()).hexdigest(), stored["piece"]["source_sha256"])
        spec["items"][0]["source"] = "ctx-schematic"
        with self.assertRaises(cutouts.CutoutError):
            cutouts.cut(spec, local)

    def test_tiles_inbox_builds_a_batch_with_hashes(self) -> None:
        local = self.tmp / "local"
        spec = cutouts.load()
        with self.assertRaises(cutouts.CutoutError):
            cutouts.tiles_inbox(spec, local)
        cut_dir = local / "material-sheet" / "cutouts"
        cut_dir.mkdir(parents=True)
        for tile in spec["tiles"]:
            pngio.write(cut_dir / f"{tile['from']}.png", solid(20, 20, (90, 70, 50)))
        cutouts.tiles_inbox(spec, local)
        batch = json.loads((local / "inbox" / cutouts.TILE_BATCH / "batch.json").read_text(encoding="utf-8"))
        self.assertEqual(["tile-wood"], [i["id"] for i in batch["items"]])
        for item in batch["items"]:
            text = (local / "inbox" / cutouts.TILE_BATCH / item["prompt_file"]).read_text(encoding="utf-8")
            self.assertEqual(hashlib.sha256(text.encode()).hexdigest(), item["prompt_sha256"])
            self.assertIn("seamless tile", text)
            self.assertEqual("cutout", item["references"][0]["slot"])

    def test_seam_ratio_tells_a_tileable_tile_from_a_cut_one(self) -> None:
        w = h = 64
        wave = bytearray()  # periodic in both directions: seamless
        for y in range(h):
            for x in range(w):
                v = 128 + int(60 * __import__("math").sin(2 * 3.14159265 * x / w) * __import__("math").sin(2 * 3.14159265 * y / h))
                wave += bytes((v, v, v, 255))
        self.assertLess(cutouts.seam_ratio(pngio.Image(w, h, wave)), 1.5)
        self.assertGreater(cutouts.seam_ratio(gradient(w, h)), 5.0)  # a ramp jumps at the wrap-around

    def test_tile_report_flags_tone_and_flat_spread(self) -> None:
        reference = {"mean_l": 55.0, "mean_chroma": 8.0, "std_l": 6.0}
        flat = cutouts.tile_report(solid(160, 160, (200, 190, 160)), reference, 1.81)  # too bright and no spread
        self.assertIn("mean_l_delta", flat["failed"])
        self.assertIn("std_l_ratio", flat["failed"])
        self.assertFalse(flat["pass"])

    def test_seam_ratio_takes_the_worse_axis_and_rejects_a_sliver(self) -> None:
        w = h = 64
        rows = bytearray()  # periodic across the columns, a ramp down the rows: only the row join is bad
        for y in range(h):
            for x in range(w):
                v = 128 + int(40 * __import__("math").sin(2 * 3.14159265 * x / w))
                rows += bytes((v, v, y * 4, 255))
        self.assertGreater(cutouts.seam_ratio(pngio.Image(w, h, rows)), 3.0)
        with self.assertRaises(cutouts.CutoutError):
            cutouts.seam_ratio(solid(1, 40, (1, 2, 3)))
        self.assertEqual(1.0, cutouts.seam_ratio(solid(8, 8, (9, 9, 9))))

    def test_tile_report_passes_a_tile_in_tone(self) -> None:
        tile = solid(256, 256, (150, 140, 120))
        stats = cutouts.tile_tone(tile, 1.81)
        report = cutouts.tile_report(tile, {"mean_l": stats["mean_l"], "mean_chroma": stats["mean_chroma"], "std_l": stats["std_l"]}, 1.81)
        self.assertTrue(report["pass"], report)

    def test_tile_check_refuses_a_stale_or_non_square_tile_and_exits_2(self) -> None:
        local = self.tmp / "local"
        spec = cutouts.load()
        cut_dir = local / "material-sheet" / "cutouts"
        cut_dir.mkdir(parents=True)
        for tile in spec["tiles"]:
            pngio.write(cut_dir / f"{tile['from']}.png", solid(20, 20, (90, 70, 50)))
        cutouts.tiles_inbox(spec, local)
        results = local / "outbox" / cutouts.TILE_BATCH
        results.mkdir(parents=True)
        pngio.write(results / "tile-wood.png", solid(300, 200, (50, 40, 30)))
        (results / "tile-wood.provenance.json").write_text(json.dumps({"prompt_sha256": "stale"}), encoding="utf-8")
        with self.assertRaises(cutouts.CutoutError):
            cutouts.check_provenance("tile-wood", local, results)
        batch = json.loads((local / "inbox" / cutouts.TILE_BATCH / "batch.json").read_text(encoding="utf-8"))
        wanted = next(i["prompt_sha256"] for i in batch["items"] if i["id"] == "tile-wood")
        (results / "tile-wood.provenance.json").write_text(json.dumps({"prompt_sha256": wanted}), encoding="utf-8")
        cutouts.check_provenance("tile-wood", local, results)  # now from the current inbox
        err = io.StringIO()
        with redirect_stdout(io.StringIO()), redirect_stderr(err):
            self.assertEqual(2, cutouts.main(["tile-check", "--local-root", str(local)]))  # non-square 300 x 200
        self.assertIn("CUTOUTS ERROR", err.getvalue())
        self.assertEqual(0, cutouts.main(["check"]))


if __name__ == "__main__":
    unittest.main()
