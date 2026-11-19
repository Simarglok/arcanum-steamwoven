"""ARC-DEV-2460: tests of the Steamwoven kit bake on synthetic masters (no game file, no original art).

    python3 -B -m unittest tools.steamwoven_ui.test_bake
"""
from __future__ import annotations

import contextlib
import io
import json
from pathlib import Path
import struct
import tempfile
import unittest
import zlib

from tools.steamwoven_ui import bake, pngio


def rows_image(width: int, height: int, rows: list[bytes]) -> pngio.Image:
    return bake.from_rows(width, height, rows)


def solid(width: int, height: int, rgba: tuple[int, int, int, int]) -> pngio.Image:
    return rows_image(width, height, [bytes(rgba) * width for _ in range(height)])


def pattern(width: int, height: int) -> pngio.Image:
    return bake._synthetic_master(width, height)


def write_master(masters: Path, part_id: str, image: pngio.Image) -> Path:
    path = masters / f"{part_id}.png"
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(pngio.encode(image))
    return path


def pixel(image: pngio.Image, x: int, y: int) -> tuple[int, ...]:
    return tuple(image.data[(y * image.w + x) * 4:(y * image.w + x) * 4 + 4])


def to_linear(code: int) -> float:
    c = code / 255
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def to_srgb(lin: float) -> float:
    return (lin * 12.92 if lin <= 0.0031308 else 1.055 * lin ** (1 / 2.4) - 0.055) * 255


class ResampleTests(unittest.TestCase):
    def test_every_length_is_master_times_step_exactly(self):  # AC1
        for width, height in ((6, 6), (12, 18), (60, 42), (2880 // 12, 2160 // 12)):
            master = pattern(width, height)
            for step, (a, b) in bake.STEPS.items():
                out = bake.resample(master, step)
                self.assertEqual((out.w, out.h), (width * a // b, height * a // b), (step, width, height))
                self.assertEqual((width * a) % b, 0)
                self.assertEqual(bake.baked_size(width, height, step), (out.w, out.h))
                self.assertTrue(all(len(row) == out.w * 4 for row in bake.image_rows(out)))

    def test_a_side_that_is_not_a_multiple_of_the_denominator_is_refused(self):
        with self.assertRaises(ValueError):
            bake.baked_size(7, 6, "x1-3")
        with self.assertRaises(ValueError):
            bake.baked_size(6, 8, "x2-3")
        self.assertEqual(bake.baked_size(7, 8, "x1"), (7, 8))

    def test_tap_weights_sum_to_the_denominator(self):
        for step, (a, b) in bake.STEPS.items():
            taps = bake.step_taps(a, b)
            self.assertEqual(len(taps), a)
            for phase in taps:
                self.assertEqual(sum(w for _, w in phase), b, step)
        self.assertEqual(bake.step_taps(2, 3), [[(0, 2), (1, 1)], [(1, 1), (2, 2)]])

    def test_x1_is_the_master_pixel_for_pixel(self):
        master = pattern(18, 12)
        self.assertEqual(bake.resample(master, "x1").data, master.data)

    def test_uniform_opaque_colour_survives_every_step_exactly(self):
        for colour in ((0, 0, 0, 255), (255, 255, 255, 255), (1, 2, 3, 255), (200, 120, 33, 255), (7, 250, 128, 255)):
            for step in bake.STEPS:
                out = bake.resample(solid(12, 12, colour), step)
                for y in range(out.h):
                    for x in range(out.w):
                        self.assertEqual(pixel(out, x, y), colour, (step, colour))

    def test_srgb_table_round_trips_every_code(self):
        self.assertTrue(all(bake.SRGB8[bake.LIN16[i]] == i for i in range(256)))

    def test_averaging_is_done_in_linear_light(self):
        rows = [bytes((0, 0, 0, 255, 255, 255, 255, 255) * 3), bytes((255, 255, 255, 255, 0, 0, 0, 255) * 3)] * 3
        out = bake.resample(rows_image(6, 6, rows), "x1-2")
        expected = round(to_srgb(0.5))
        for y in range(out.h):
            for x in range(out.w):
                self.assertEqual(pixel(out, x, y), (expected, expected, expected, 255))
        self.assertNotEqual(expected, 128)  # a plain average of the codes would be 128

    def test_two_thirds_step_weights_the_pixels_exactly(self):
        values = [0, 60, 255, 30, 200, 90]
        rows = [bytes(v for value in values for v in (value, value, value, 255)) for _ in range(6)]
        out = bake.resample(rows_image(6, 6, rows), "x2-3")
        self.assertEqual((out.w, out.h), (4, 4))
        lin = [to_linear(v) for v in values]
        expected = [(2 * lin[0] + lin[1]) / 3, (lin[1] + 2 * lin[2]) / 3, (2 * lin[3] + lin[4]) / 3, (lin[4] + 2 * lin[5]) / 3]
        for x, want in enumerate(expected):
            got = pixel(out, x, 0)[0]
            self.assertAlmostEqual(got, to_srgb(want), delta=1.0, msg=f"column {x}")
            self.assertEqual(pixel(out, x, 0), pixel(out, x, 3))

    def test_transparent_colour_does_not_bleed_into_a_neighbour(self):
        row = bytes((255, 0, 0, 255) * 3 + (0, 255, 0, 0) * 3)  # three opaque red, three transparent green
        image = rows_image(6, 6, [row] * 6)
        out = bake.resample(image, "x1-2")  # 3 x 3; the middle pixel covers one red and one transparent green master pixel
        self.assertEqual(pixel(out, 0, 0), (255, 0, 0, 255))
        self.assertEqual(pixel(out, 2, 0), (0, 0, 0, 0))
        r, g, b, a = pixel(out, 1, 0)
        self.assertEqual((r, g, b), (255, 0, 0))  # pure red at half alpha: no green, no dark fringe
        self.assertIn(a, (127, 128))

    def test_a_fully_transparent_result_has_colour_zero(self):
        out = bake.resample(solid(6, 6, (10, 200, 90, 0)), "x1-3")
        self.assertEqual(pixel(out, 0, 0), (0, 0, 0, 0))
        self.assertEqual(bake.resample(solid(6, 6, (10, 200, 90, 0)), "x1").data, solid(6, 6, (10, 200, 90, 0)).data)

    def test_alpha_is_the_plain_average(self):
        rows = [bytes((50, 60, 70, 255, 50, 60, 70, 0, 50, 60, 70, 255, 50, 60, 70, 0, 50, 60, 70, 255, 50, 60, 70, 0))] * 6
        out = bake.resample(rows_image(6, 6, rows), "x1-3")  # 2 x 2: first three columns 2 of 3 opaque, last three 1 of 3
        self.assertEqual(pixel(out, 0, 0)[3], round(255 * 2 / 3))
        self.assertEqual(pixel(out, 1, 0)[3], round(255 * 1 / 3))
        self.assertEqual(pixel(out, 0, 0)[:3], (50, 60, 70))


class PngTests(unittest.TestCase):
    def test_round_trip_and_canonical_bytes(self):
        image = pattern(12, 6)
        data = pngio.encode(image)
        self.assertEqual(pngio.decode(data).data, image.data)
        self.assertEqual(pngio.encode(pngio.decode(data)), data)

    def _png(self, width: int, height: int, raw_rows: list[bytes], depth=8, color=6, interlace=0) -> bytes:
        def chunk(kind, body):
            crc = zlib.crc32(kind + body) & 0xFFFFFFFF
            return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", crc)
        return (pngio.SIGNATURE + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, depth, color, 0, 0, interlace))
                + chunk(b"IDAT", zlib.compress(b"".join(raw_rows))) + chunk(b"IEND", b""))

    def _filtered(self, image: pngio.Image, kind: int) -> list[bytes]:
        out, prev = [], bytes(image.w * 4)
        for row in bake.image_rows(image):
            line = bytearray()
            for i, v in enumerate(row):
                left = row[i - 4] if i >= 4 else 0
                up = prev[i]
                upleft = prev[i - 4] if i >= 4 else 0
                predictor = (0, left, up, (left + up) >> 1, pngio._paeth(left, up, upleft))[kind]
                line.append((v - predictor) & 255)
            out.append(bytes([kind]) + bytes(line))
            prev = row
        return out

    def test_every_row_filter_is_read(self):
        image = pattern(18, 12)
        for kind in range(5):
            self.assertEqual(pngio.decode(self._png(18, 12, self._filtered(image, kind))).data, image.data, kind)

    def test_other_formats_and_damage_are_refused(self):
        rows = [b"\x00" + bytes(12)] * 3
        rgb = pngio.decode(self._png(3, 3, [b"\x00" + bytes(9)] * 3, color=2))
        self.assertFalse(rgb.source_alpha)  # readable, but not an RGBA master: the bake refuses it (below)
        with self.assertRaises(pngio.PngError):
            pngio.decode(self._png(3, 3, rows, interlace=1))
        with self.assertRaises(pngio.PngError):
            pngio.decode(b"not a png")
        with self.assertRaises(pngio.PngError):
            pngio.decode(self._png(3, 3, rows[:2]))


class BakeToolTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.tmp = Path(self._tmp.name)
        self.masters = self.tmp / "masters"
        self.addCleanup(self._tmp.cleanup)

    def run_bake(self, out: Path, **kwargs):
        kwargs.setdefault("use_kit", False)
        return bake.bake(self.masters, out, kwargs.pop("steps", list(bake.STEPS)), **kwargs)

    def test_two_runs_give_identical_bytes(self):  # AC2
        write_master(self.masters, "a.one", pattern(60, 42))
        write_master(self.masters, "sub/b.two", pattern(18, 12))
        first, second = self.tmp / "run1", self.tmp / "run2"
        self.assertEqual(self.run_bake(first).errors, [])
        self.assertEqual(self.run_bake(second).errors, [])
        files = sorted(p.relative_to(first) for p in first.rglob("*") if p.is_file())
        self.assertEqual(files, sorted(p.relative_to(second) for p in second.rglob("*") if p.is_file()))
        self.assertEqual(len(files), 4 * 3)  # four sets of two parts and a manifest
        for relative in files:
            self.assertEqual((first / relative).read_bytes(), (second / relative).read_bytes(), str(relative))

    def test_manifest_holds_sizes_and_hashes_per_set(self):
        write_master(self.masters, "a.one", pattern(60, 42))
        out = self.tmp / "out"
        self.assertEqual(self.run_bake(out).errors, [])
        for step, (a, b) in bake.STEPS.items():
            manifest = json.loads((out / step / "manifest.json").read_text())
            self.assertEqual((manifest["step"], manifest["ratio"]), (step, [a, b]))
            (part,) = manifest["parts"]
            self.assertEqual(part["master"]["size"], [60, 42])
            self.assertEqual(part["size"], [60 * a // b, 42 * a // b])
            data = (out / step / "a.one.png").read_bytes()
            self.assertEqual((part["sha256"], part["bytes"]), (bake.sha256(data), len(data)))
            self.assertEqual(part["pixels_sha256"], bake.sha256(bytes(pngio.decode(data).data)))
        self.assertEqual(bake.verify(self.masters, out, list(bake.STEPS)).errors, [])

    def test_a_second_bake_reuses_unchanged_parts_and_rebakes_a_changed_one(self):
        master = write_master(self.masters, "a.one", pattern(12, 12))
        out = self.tmp / "out"
        self.run_bake(out)
        again = self.run_bake(out)
        self.assertTrue(all(line.endswith("unchanged") for line in again.lines), again.lines)
        write_master(self.masters, "a.one", solid(12, 12, (9, 9, 9, 255)))
        third = self.run_bake(out, steps=["x1-2"])
        self.assertTrue(any("12 x 12 -> 6 x 6" in line for line in third.lines))
        self.assertEqual(bake.verify(self.masters, out, ["x1-2"]).errors, [])
        self.assertIsNotNone(master)

    def test_a_bad_master_is_reported_and_the_others_are_baked(self):
        write_master(self.masters, "good", pattern(12, 12))
        write_master(self.masters, "odd", pattern(10, 12))
        (self.masters / "broken.png").write_bytes(b"junk")
        report = self.run_bake(self.tmp / "out", steps=["x1-2"])
        self.assertEqual(len(report.errors), 2, report.errors)
        self.assertTrue((self.tmp / "out" / "x1-2" / "good.png").is_file())
        self.assertFalse((self.tmp / "out" / "x1-2" / "odd.png").exists())

    def test_a_master_without_an_alpha_channel_is_refused(self):
        rgb = PngTests()._png(6, 6, [b"\x00" + bytes(18)] * 6, color=2)
        (self.masters).mkdir(parents=True, exist_ok=True)
        (self.masters / "rgb.png").write_bytes(rgb)
        report = self.run_bake(self.tmp / "out", steps=["x1-2"])
        self.assertTrue(any("no alpha channel" in e for e in report.errors), report.errors)

    def test_dry_run_writes_nothing(self):
        write_master(self.masters, "a.one", pattern(12, 12))
        out = self.tmp / "out"
        report = self.run_bake(out, dry_run=True)
        self.assertFalse(out.exists())
        self.assertEqual(len(report.lines), 4)
        self.assertTrue(report.lines[1].endswith("would bake 12 x 12 -> 8 x 8"), report.lines)

    def test_kit_check_refuses_unknown_ids_and_a_units_size_mismatch(self):
        write_master(self.masters, "zz.not_in_the_kit", pattern(12, 12))
        report = bake.bake(self.masters, self.tmp / "out", ["x1-2"])
        self.assertTrue(any("not a part of the kit manifest" in e for e in report.errors), report.errors)
        self.assertFalse((self.tmp / "out" / "x1-2" / "zz.not_in_the_kit.png").exists())
        kit = {"p.units": ([12, 12], "units"), "p.prov": ([12, 12], "provisional"), "p.open": (None, "open")}
        probe = bake.Report()
        self.assertFalse(bake.check_against_kit("p.units", (18, 12), kit, probe))
        self.assertTrue(bake.check_against_kit("p.prov", (18, 12), kit, probe))
        self.assertTrue(bake.check_against_kit("p.open", (18, 12), kit, probe))
        self.assertEqual((len(probe.errors), len(probe.warnings)), (1, 1))

    def test_real_manifest_ids_load(self):
        kit = bake.load_kit_sizes()
        self.assertGreater(len(kit), 100)
        self.assertIn("cap.bottom_strip_left", kit)

    def test_a_filtered_run_with_prune_leaves_the_other_masters_sets_alone(self):
        write_master(self.masters, "keep", pattern(12, 12))
        write_master(self.masters, "other", pattern(12, 12))
        out = self.tmp / "out"
        self.run_bake(out, steps=["x1-3"])
        self.run_bake(out, steps=["x1-3"], only={"keep"}, prune=True)
        self.assertTrue((out / "x1-3" / "other.png").is_file())
        manifest = json.loads((out / "x1-3" / "manifest.json").read_text())
        self.assertEqual([p["id"] for p in manifest["parts"]], ["keep", "other"])

    def test_prune_with_all_masters_drops_the_stale_file_and_its_record(self):
        write_master(self.masters, "keep", pattern(12, 12))
        gone = write_master(self.masters, "gone", pattern(12, 12))
        out = self.tmp / "out"
        self.run_bake(out, steps=["x1-3"])
        gone.unlink()
        self.run_bake(out, steps=["x1-3"])  # no prune: the file and its record stay
        self.assertTrue((out / "x1-3" / "gone.png").is_file())
        self.assertEqual(len(json.loads((out / "x1-3" / "manifest.json").read_text())["parts"]), 2)
        self.run_bake(out, steps=["x1-3"], prune=True)
        self.assertFalse((out / "x1-3" / "gone.png").exists())
        self.assertEqual([p["id"] for p in json.loads((out / "x1-3" / "manifest.json").read_text())["parts"]], ["keep"])
        self.assertEqual(bake.verify(self.masters, out, ["x1-3"]).errors, [])

    def test_verify_fails_on_a_changed_file_and_warns_on_a_changed_master(self):
        write_master(self.masters, "a.one", pattern(12, 12))
        out = self.tmp / "out"
        self.run_bake(out, steps=["x1-2"])
        write_master(self.masters, "a.one", solid(12, 12, (1, 2, 3, 255)))
        report = bake.verify(self.masters, out, ["x1-2"])
        self.assertEqual((report.errors, len(report.warnings)), ([], 1))
        target = out / "x1-2" / "a.one.png"
        target.write_bytes(pngio.encode(solid(6, 6, (1, 1, 1, 255))))
        self.assertTrue(bake.verify(self.masters, out, ["x1-2"]).errors)
        self.assertTrue(bake.verify(self.masters, self.tmp / "nowhere", ["x1-2"]).errors)

    def test_lock_takes_over_a_dead_process_and_refuses_a_live_one(self):
        out = self.tmp / "out"
        out.mkdir()
        lock = out / bake.LOCK_NAME
        lock.write_text("999999999")
        with bake.Lock(out):
            self.assertTrue(lock.is_file())
        self.assertFalse(lock.exists())
        import os
        lock.write_text(str(os.getpid()))
        with self.assertRaises(RuntimeError):
            with bake.Lock(out):
                pass
        self.assertTrue(lock.is_file())
        write_master(self.masters, "a", pattern(12, 12))
        self.assertTrue(self.run_bake(out, steps=["x1"]).errors)

    def test_cli_bake_and_verify(self):
        write_master(self.masters, "a.one", pattern(12, 12))
        out = self.tmp / "out"
        base = ["--masters", str(self.masters), "--out", str(out), "--steps", "x1-2,x1-3"]
        for command, extra, code in (("bake", ["--no-kit", "--dry-run"], 0), ("bake", ["--no-kit"], 0), ("verify", [], 0), ("bake", [], 1)):
            if code and command == "bake":
                write_master(self.masters, "zz.not_in_the_kit", pattern(12, 12))
            buffer = io.StringIO()
            with contextlib.redirect_stdout(buffer):
                self.assertEqual(bake.main([command, *base, *extra]), code, buffer.getvalue())
        self.assertIn("BAKE bake FAIL", buffer.getvalue())
        with self.assertRaises(SystemExit), contextlib.redirect_stderr(io.StringIO()):
            bake.main(["bake", "--steps", "x5"])

    def test_a_version_bump_rebakes_parts_a_filtered_run_left_behind(self):
        write_master(self.masters, "a", pattern(12, 12))
        write_master(self.masters, "b", pattern(12, 12))
        out = self.tmp / "out"
        self.run_bake(out, steps=["x1-2"])
        old = bake.BAKE_VERSION
        try:
            bake.BAKE_VERSION = old + 1
            self.run_bake(out, steps=["x1-2"], only={"a"})
            third = self.run_bake(out, steps=["x1-2"])
        finally:
            bake.BAKE_VERSION = old
        self.assertTrue(any(line.endswith("12 x 12 -> 6 x 6") and " b:" in line for line in third.lines), third.lines)

    def test_malformed_manifests_and_foreign_files_are_reported_not_raised(self):
        write_master(self.masters, "a", pattern(12, 12))
        out = self.tmp / "out"
        directory = out / "x1-2"
        directory.mkdir(parents=True)
        (directory / "manifest.json").write_text('{"schema": 1}')
        self.assertTrue(bake.verify(self.masters, out, ["x1-2"]).errors)
        self.assertTrue(self.run_bake(out, steps=["x1-2"]).errors)  # holds a manifest that is not valid: not touched
        (directory / "manifest.json").unlink()
        (directory / "foreign.txt").write_text("x")
        self.assertTrue(any("no valid manifest" in e for e in self.run_bake(out, steps=["x1-2"]).errors))
        (directory / "foreign.txt").unlink()
        self.assertEqual(self.run_bake(out, steps=["x1-2"]).errors, [])
        (directory / "stray.png").write_bytes(b"x")
        self.assertTrue(any("stray.png" in w for w in bake.verify(self.masters, out, ["x1-2"]).warnings))

    def test_ids_with_dots_or_odd_characters_are_refused_everywhere(self):
        for bad in ("../x", "a/../b", "/abs", ".hidden", "a\\b", "", "a b"):
            self.assertFalse(bake.valid_id(bad), bad)
        for good in ("board.top_strip", "sub/b.two", "a-b_c"):
            self.assertTrue(bake.valid_id(good), good)
        write_master(self.masters, "a", pattern(12, 12))
        write_master(self.masters, "has space", pattern(12, 12))
        report = self.run_bake(self.tmp / "out", steps=["x1-2"])
        self.assertTrue(any("not a valid part id" in e for e in report.errors), report.errors)
        out = self.tmp / "out"
        manifest = json.loads((out / "x1-2" / "manifest.json").read_text())
        manifest["parts"][0]["id"] = "../../victim"
        (out / "x1-2" / "manifest.json").write_text(json.dumps(manifest))
        self.assertIsNone(bake.read_manifest(out / "x1-2"))

    def test_an_empty_or_negative_lock_is_stale(self):
        out = self.tmp / "out"
        out.mkdir()
        for content in ("", "-1", "0", "junk"):
            (out / bake.LOCK_NAME).write_text(content)
            with bake.Lock(out):
                pass
        self.assertFalse((out / bake.LOCK_NAME).exists())

    def test_dry_run_counts_sets_and_no_steps_is_an_error(self):
        write_master(self.masters, "a", pattern(12, 12))
        self.assertEqual(self.run_bake(self.tmp / "out", dry_run=True).ok, list(bake.STEPS))
        self.assertTrue(self.run_bake(self.tmp / "out", steps=[]).errors)

    def test_a_changed_kit_size_is_rechecked_for_reused_parts(self):
        write_master(self.masters, "a.one", pattern(12, 12))
        out = self.tmp / "out"
        self.assertEqual(self.run_bake(out, steps=["x1-2"]).errors, [])
        report = bake.Report()
        bake.bake_set(bake.find_masters(self.masters), {"a.one"}, out, "x1-2", False, False, {"a.one": ([18, 12], "units")}, report)
        self.assertTrue(any("differs from the manifest" in e for e in report.errors), report.errors)

    def test_three_and_four_tap_combinations_match_the_plain_sum(self):
        cols = [[1, 2], [3, 4], [5, 6], [7, 8]]
        self.assertEqual(bake._combine(cols[:3], [1, 2, 3]), [1 + 6 + 15, 2 + 8 + 18])
        self.assertEqual(bake._combine(cols, [1, 1, 1, 1]), [16, 20])

    def test_self_test_passes(self):
        self.assertEqual(bake.self_test(), [])


if __name__ == "__main__":
    unittest.main()
