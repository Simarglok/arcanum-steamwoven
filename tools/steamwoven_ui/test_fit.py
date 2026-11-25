"""ARC-DEV-2459: tests of the fit tool on synthetic images (no game file, no original art).

    python3 -B -m unittest tools.steamwoven_ui.test_fit
"""
from __future__ import annotations

import contextlib
import io
import json
import math
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from tools.steamwoven_ui import fit, pngio, tone_check


def solid(w: int, h: int, rgba: tuple[int, int, int, int], alpha_source: bool = True) -> pngio.Image:
    return pngio.Image(w, h, bytearray(bytes(rgba) * (w * h)), alpha_source)


def textured(w: int, h: int, base: tuple[int, int, int], amplitude: int = 12) -> pngio.Image:
    """A grain of fixed pattern around a base colour (opaque)."""
    data = bytearray()
    for y in range(h):
        for x in range(w):
            n = ((x * 7 + y * 13) % 11 - 5) * amplitude // 5
            data += bytes((max(0, min(255, base[0] + n)), max(0, min(255, base[1] + n)), max(0, min(255, base[2] + n)), 255))
    return pngio.Image(w, h, data)


def naive_keyed(image: pngio.Image, background: tuple[int, int, int], low: float = 10, high: float = 60) -> pngio.Image:
    """What a plain key does: alpha from the distance, the colours left as they are (the halo the fit tool must not leave)."""
    out = pngio.Image(image.w, image.h, bytearray(image.data), True)
    for i in range(image.w * image.h):
        o = i * 4
        d = math.dist(out.data[o:o + 3], background)
        out.data[o + 3] = 0 if d <= low else 255 if d >= high else round(255 * (d - low) / (high - low))
    return out


def part_manifest(tmp: Path, parts: list[dict]) -> Path:
    path = tmp / "kit.json"
    path.write_text(json.dumps({"schema": 1, "windows": {}, "parts": parts, "excluded": {}}), encoding="utf-8")
    return path


def make_sheet(local: Path, cutouts: dict[str, tuple[str, pngio.Image]]) -> str:
    """A fake approved sheet under `local`: sheet.png, cutouts and their provenance record; returns the sheet's sha256."""
    folder = local / "material-sheet" / "cutouts"
    folder.mkdir(parents=True, exist_ok=True)
    record = {}
    for cut_id, (material, image) in cutouts.items():
        blob = pngio.encode(image)
        (folder / f"{cut_id}.png").write_bytes(blob)
        record[cut_id] = {"material": material, "sha256": fit.sha256_bytes(blob)}
    (folder / "cutouts.provenance.json").write_text(json.dumps(record), encoding="utf-8")
    sheet = local / "material-sheet" / "sheet.png"
    sheet.write_bytes(pngio.encode(solid(12, 12, (1, 2, 3, 255))))
    return fit.sha256_file(sheet)


def mean_lab(image: pngio.Image) -> tuple[float, float, float]:
    sums, n = [0.0, 0.0, 0.0], 0
    for i in range(image.w * image.h):
        o = i * 4
        if image.data[o + 3] == 255:
            lab = tone_check._pixel_lab(*image.data[o:o + 3])
            for c in range(3):
                sums[c] += lab[c]
            n += 1
    return tuple(s / n for s in sums)  # type: ignore[return-value]


class ResampleTest(unittest.TestCase):
    def test_taps_sum_to_one(self) -> None:
        for src, dst in ((100, 36), (36, 100), (60, 60), (7, 60), (60, 7)):
            for kind in ("auto", "linear"):
                idx, wts = fit.axis_taps(src, dst, 5, kind)
                for o in range(dst):
                    self.assertAlmostEqual(sum(w[o] for w in wts), 1.0, places=6, msg=f"{src}->{dst} {kind} {o}")
                    for k in idx:
                        self.assertTrue(5 <= k[o] < 5 + src)

    def test_constant_stays_constant_and_size_is_exact(self) -> None:
        for src, dst in (((48, 36), (120, 90)), ((300, 225), (60, 48)), ((50, 50), (50, 50))):
            out = fit.resample_image(solid(*src, (90, 120, 150, 255)), (0, 0, *src), *dst)
            self.assertEqual((out.w, out.h), dst)
            for x, y in ((0, 0), (dst[0] - 1, dst[1] - 1), (dst[0] // 2, dst[1] // 2)):
                self.assertEqual(out.pixel(x, y), (90, 120, 150, 255))

    def test_identity_copy(self) -> None:
        image = textured(24, 18, (100, 80, 60))
        self.assertEqual(bytes(fit.resample_image(image, (0, 0, 24, 18), 24, 18).data), bytes(image.data))

    def test_crop_takes_only_the_rectangle(self) -> None:
        image = solid(40, 40, (200, 0, 0, 255))
        for y in range(10, 30):
            for x in range(10, 30):
                image.data[(y * 40 + x) * 4:(y * 40 + x) * 4 + 4] = bytes((0, 0, 200, 255))
        out = fit.resample_image(image, (10, 10, 20, 20), 12, 12)
        self.assertEqual(out.pixel(0, 0), (0, 0, 200, 255))
        self.assertEqual(out.pixel(11, 11), (0, 0, 200, 255))

    def test_transparent_colour_does_not_bleed(self) -> None:
        image = solid(20, 20, (0, 255, 0, 0))  # transparent green
        for y in range(20):
            for x in range(10):
                image.data[(y * 20 + x) * 4:(y * 20 + x) * 4 + 4] = bytes((200, 50, 50, 255))
        out = fit.resample_image(image, (0, 0, 20, 20), 60, 60)
        for y in range(60):
            for x in range(60):
                p = out.pixel(x, y)
                if p[3] > 0:
                    self.assertGreater(p[0], 180, f"green bled into {x},{y}: {p}")


class AlphaTest(unittest.TestCase):
    def test_keyed_disc_has_no_halo_and_naive_key_has(self) -> None:
        magenta = (255, 0, 255)
        source = fit.synthetic_disc(96, magenta)
        naive = naive_keyed(source, magenta)
        self.assertGreater(fit.edge_halo(naive)["mean_de"], fit.HALO_MEAN_DE * 3)
        master, report = fit.fit_image(source, (180, 180), key="border")
        self.assertEqual(fit.gates(report, (180, 180)), [])
        self.assertLessEqual(report["halo"]["mean_de"], fit.HALO_MEAN_DE)
        self.assertGreater(report["halo"]["measured"], 50)
        self.assertEqual(master.pixel(0, 0), (0, 0, 0, 0))  # background: fully transparent, stored as 0 0 0 0
        self.assertEqual(master.pixel(90, 90)[3], 255)

    def test_explicit_key_colour(self) -> None:
        source = fit.synthetic_disc(96, (0, 255, 0))
        master, report = fit.fit_image(source, (96, 96), key="#00FF00")
        self.assertEqual(report["alpha"], "key")
        self.assertEqual(fit.gates(report, (96, 96)), [])
        self.assertEqual(master.pixel(2, 2)[3], 0)

    def test_contaminated_alpha_edge_is_cleaned(self) -> None:
        """A source with real alpha whose edge pixels carry the colour of the matte they were composited on."""
        source = fit.synthetic_disc(96, None)
        for i in range(96 * 96):
            o = i * 4
            a = source.data[o + 3]
            if 0 < a < 255:
                share = a / 255
                for c, matte in enumerate((255, 0, 255)):
                    source.data[o + c] = round(source.data[o + c] * share + matte * (1 - share))
        self.assertGreater(fit.edge_halo(source)["mean_de"], 10)
        master, report = fit.fit_image(source, (192, 192))
        self.assertEqual(report["alpha"], "source")
        self.assertEqual(fit.gates(report, (192, 192)), [])
        self.assertGreater(report["master_decontaminated"] + report["source_decontaminated"], 0)

    def test_the_halo_gate_is_not_a_tautology(self) -> None:
        """Without the decontamination a contaminated source fails the gate on what the pipeline made (not on the safety net's own result)."""
        source = fit.synthetic_disc(96, None)
        for i in range(96 * 96):
            o = i * 4
            if 0 < source.data[o + 3] < 255:
                source.data[o:o + 3] = bytes((255, 0, 255))
        with mock.patch.object(fit, "decontaminate", return_value=0):
            _, report = fit.fit_image(source, (96, 96))
        self.assertTrue(any("edge halo" in f for f in fit.gates(report, (96, 96))), report["halo"])
        _, clean = fit.fit_image(source, (96, 96))
        self.assertEqual(fit.gates(clean, (96, 96)), [])

    def test_explicit_key_needs_a_border_of_that_colour(self) -> None:
        full = textured(60, 60, (100, 80, 60))  # fills the frame: nothing of it is background
        with self.assertRaises(fit.FitError):
            fit.fit_image(full, (60, 60), key="#00FF00")

    def test_empty_masters_fail_the_gates(self) -> None:
        _, report = fit.fit_image(solid(60, 60, (0, 0, 0, 0)), (60, 60))
        self.assertTrue(any("empty" in f for f in fit.gates(report, (60, 60))))
        _, flat = fit.fit_image(solid(60, 60, (255, 0, 255, 255), False), (60, 60), key="border")
        self.assertTrue(any("empty" in f for f in fit.gates(flat, (60, 60))))

    def test_orphans_are_gated(self) -> None:
        source = solid(60, 60, (0, 0, 0, 0))  # a faint glow: no opaque pixel anywhere
        for y in range(20, 40):
            for x in range(20, 40):
                source.data[(y * 60 + x) * 4:(y * 60 + x) * 4 + 4] = bytes((200, 150, 60, 120))
        _, report = fit.fit_image(source, (60, 60))
        self.assertTrue(any("no opaque pixel" in f for f in fit.gates(report, (60, 60))))

    def test_soft_shadow_is_not_an_orphan_but_the_art_edge_is_still_measured(self) -> None:
        source = solid(60, 60, (0, 0, 0, 0))   # an opaque bar with a wide soft shadow beside it (an end cap)
        for y in range(10, 50):
            for x in range(40, 60):
                source.data[(y * 60 + x) * 4:(y * 60 + x) * 4 + 4] = bytes((180, 140, 60, 255))
            for x in range(0, 40):
                source.data[(y * 60 + x) * 4:(y * 60 + x) * 4 + 4] = bytes((10, 7, 5, round(140 * x / 40)))
        _, plain = fit.fit_image(source, (60, 60))
        self.assertTrue(any("no opaque pixel" in f for f in fit.gates(plain, (60, 60))))
        _, soft = fit.fit_image(source, (60, 60), soft_shadow=True)
        self.assertEqual(fit.gates(soft, (60, 60)), [])
        self.assertGreater(soft["halo"]["shadow_pixels"], 0)
        self.assertEqual(soft["halo"]["orphans"], 0)

    def test_opaque_source_stays_opaque(self) -> None:
        master, report = fit.fit_image(textured(60, 45, (100, 80, 60)), (120, 90))
        self.assertEqual(report["alpha"], "opaque")
        self.assertEqual(set(master.data[3::4]), {255})
        self.assertEqual(report["halo"]["band_pixels"], 0)

    def test_border_must_be_flat(self) -> None:
        noisy = textured(64, 64, (128, 128, 128), amplitude=400)
        with self.assertRaises(fit.FitError):
            fit.fit_image(noisy, (60, 60), key="border")

    def test_bad_key_text(self) -> None:
        with self.assertRaises(fit.FitError):
            fit.fit_image(solid(12, 12, (1, 2, 3, 255), False), (12, 12), key="magenta")

    def test_crush_and_specks(self) -> None:
        image = solid(40, 40, (0, 0, 0, 0))
        for y in range(10, 30):
            for x in range(10, 30):
                image.data[(y * 40 + x) * 4:(y * 40 + x) * 4 + 4] = bytes((120, 90, 50, 255))
        image.data[3 + 4 * (2 * 40 + 2)] = 2     # alpha 2 -> 0
        image.data[3 + 4 * (12 * 40 + 12)] = 253  # alpha 253 -> 255
        for x in (3, 4, 5):                       # a 3 px speck, clear of the body
            image.data[(35 * 40 + x) * 4:(35 * 40 + x) * 4 + 4] = bytes((120, 90, 50, 255))
        self.assertEqual(fit.crush_alpha(image), 2)
        self.assertEqual(image.data[3 + 4 * (2 * 40 + 2)], 0)
        self.assertEqual(image.data[3 + 4 * (12 * 40 + 12)], 255)
        self.assertEqual(fit.remove_specks(image, 24), 3)
        self.assertEqual(image.pixel(4, 35), (0, 0, 0, 0))
        self.assertEqual(image.pixel(20, 20), (120, 90, 50, 255))  # the body stays

    def test_speck_joined_to_the_body_by_a_corner_only_is_a_separate_group(self) -> None:
        image = solid(20, 20, (0, 0, 0, 0))
        for y in range(5, 15):
            for x in range(5, 15):
                image.data[(y * 20 + x) * 4:(y * 20 + x) * 4 + 4] = bytes((1, 2, 3, 255))
        image.data[(4 * 20 + 4) * 4 + 3] = 255  # touches the body at a corner (8-connected, not 4-connected)
        self.assertEqual(fit.remove_specks(image, 24), 1)


class LockTest(unittest.TestCase):
    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.local = Path(self._tmp.name) / "local"
        self.wood = textured(40, 40, (95, 62, 40))
        self.brass = textured(40, 40, (150, 120, 50))
        sheet_hash = make_sheet(self.local, {"wood-a": ("wood", self.wood), "brass-a": ("brass", self.brass), "bronze-a": ("bronze", textured(40, 40, (146, 116, 66)))})
        old = fit.APPROVED_SHEET_SHA256
        fit.APPROVED_SHEET_SHA256 = sheet_hash
        self.addCleanup(setattr, fit, "APPROVED_SHEET_SHA256", old)

    def test_reference_means(self) -> None:
        ref = fit.sheet_reference(self.local, ["wood"])
        want = mean_lab(self.wood)
        for got, expected in zip(ref["materials"]["wood"]["lab"], want):
            self.assertAlmostEqual(got, expected, delta=1.5)
        self.assertEqual(ref["materials"]["wood"]["cutouts"], ["wood-a"])

    def test_lock_moves_the_mean_and_keeps_the_texture(self) -> None:
        source = textured(120, 90, (112, 74, 50), amplitude=14)  # a little too light and too orange
        ref = fit.sheet_reference(self.local, ["wood"])
        master, report = fit.fit_image(source, (120, 90), reference=ref)
        self.assertEqual(fit.gates(report, (120, 90)), [])
        shift = report["lock"]["materials"][0]["shift_de"]
        self.assertGreater(shift, 3)
        for got, want in zip(mean_lab(master), ref["materials"]["wood"]["lab"]):
            self.assertAlmostEqual(got, want, delta=1.0)
        for key in ("l", "a", "b", "total"):  # SWI-VER-3: the high-frequency energy within 10 %
            self.assertLess(abs(report["hf"]["ratio"][key] - 1), 0.10, key)
        self.assertTrue(report["hf"]["within_tolerance"])

    def test_two_materials_follow_their_regions(self) -> None:
        w, h = 160, 80
        source = pngio.Image(w, h, bytearray(w * h * 4))
        for y in range(h):
            for x in range(w):
                base = (108, 72, 48) if x < w // 2 else (160, 128, 56)
                n = ((x * 7 + y * 13) % 11 - 5) * 2
                source.data[(y * w + x) * 4:(y * w + x) * 4 + 4] = bytes((base[0] + n, base[1] + n, base[2] + n, 255))
        ref = fit.sheet_reference(self.local, ["wood", "brass"])
        master, report = fit.fit_image(source, (w, h), reference=ref)
        self.assertEqual({m["material"] for m in report["lock"]["materials"]}, {"wood", "brass"})
        left, right = master.crop(0, 0, 60, h), master.crop(100, 0, 60, h)
        for part, material in ((left, "wood"), (right, "brass")):
            for got, want in zip(mean_lab(part), ref["materials"][material]["lab"]):
                self.assertAlmostEqual(got, want, delta=2.5)

    def test_lock_on_a_cutout_with_edges_keeps_the_halo_gate(self) -> None:
        source = fit.synthetic_disc(96, None)
        ref = fit.sheet_reference(self.local, ["bronze"])
        _, report = fit.fit_image(source, (96, 96), reference=ref)
        self.assertEqual(fit.gates(report, (96, 96)), [])

    def test_off_tone_generation_is_refused(self) -> None:
        source = textured(60, 60, (30, 90, 200))  # blue for a wood part
        ref = fit.sheet_reference(self.local, ["wood"])
        with self.assertRaises(fit.FitError) as caught:
            fit.fit_image(source, (60, 60), reference=ref)
        self.assertIn("off-tone", str(caught.exception))
        master, _ = fit.fit_image(source, (60, 60), reference=ref, force_lock=True)  # forced
        self.assertEqual((master.w, master.h), (60, 60))

    def test_unknown_material_and_wrong_sheet(self) -> None:
        with self.assertRaises(fit.FitError):
            fit.sheet_reference(self.local, ["glass"])
        fit.APPROVED_SHEET_SHA256 = "0" * 64
        with self.assertRaises(fit.FitError):
            fit.sheet_reference(self.local, ["wood"])

    def test_changed_cutout_is_refused(self) -> None:
        (self.local / "material-sheet" / "cutouts" / "wood-a.png").write_bytes(pngio.encode(solid(8, 8, (1, 1, 1, 255))))
        with self.assertRaises(fit.FitError):
            fit.sheet_reference(self.local, ["wood"])

    def test_strength_is_bounded(self) -> None:
        ref = fit.sheet_reference(self.local, ["wood"])
        for bad in (-0.1, 1.5, 3.0):
            with self.assertRaises(fit.FitError):
                fit.fit_image(textured(30, 30, (112, 74, 50)), (30, 30), reference=ref, strength=bad)

    def test_strength_scales_the_shift(self) -> None:
        source = textured(60, 60, (112, 74, 50))
        ref = fit.sheet_reference(self.local, ["wood"])
        _, full = fit.fit_image(source, (60, 60), reference=ref)
        _, half = fit.fit_image(source, (60, 60), reference=ref, strength=0.5)
        self.assertAlmostEqual(half["lock"]["materials"][0]["shift_de"] * 2, full["lock"]["materials"][0]["shift_de"], delta=0.1)

    def test_hf_check_flags_a_changed_texture(self) -> None:
        before = fit.hf_energy(textured(80, 80, (100, 80, 60), amplitude=20))
        flat = fit.hf_energy(textured(80, 80, (100, 80, 60), amplitude=4))
        self.assertFalse(fit.hf_check(before, flat)["within_tolerance"])
        self.assertTrue(fit.hf_check(before, before)["within_tolerance"])


class SizeTest(unittest.TestCase):
    def test_master_size_rules(self) -> None:
        self.assertEqual(fit.master_size({"id": "p", "size": [60, 42]}, None), (60, 42))
        with self.assertRaises(fit.FitError):
            fit.master_size({"id": "p", "size": [60, 42]}, (66, 42))
        with self.assertRaises(fit.FitError):
            fit.master_size({"id": "p", "size": None}, None)
        self.assertEqual(fit.master_size({"id": "p", "size": None}, (120, 60)), (120, 60))
        with self.assertRaises(fit.FitError):
            fit.master_size({"id": "p", "size": None}, (100, 60))  # not on the 6 px grid

    def test_aspect_and_crop(self) -> None:
        with self.assertRaises(fit.FitError):
            fit.source_rect(100, 100, (120, 60), None)
        self.assertEqual(fit.source_rect(1000, 500, (120, 60), None), (0, 0, 1000, 500))
        self.assertEqual(fit.source_rect(1004, 500, (120, 60), None), (0, 0, 1004, 500))  # within 1 %
        self.assertEqual(fit.source_rect(100, 100, (120, 60), (0, 20, 100, 50)), (0, 20, 100, 50))
        with self.assertRaises(fit.FitError):
            fit.source_rect(100, 100, (120, 60), (50, 50, 80, 40))  # outside the source
        with self.assertRaises(fit.FitError):
            fit.source_rect(100, 100, (60, 60), (0, 0, 60, 10))  # a crop of the wrong aspect would be stretched 6 times

    def test_crop_pipeline_gives_the_exact_size(self) -> None:
        source = textured(100, 100, (100, 80, 60))
        master, report = fit.fit_image(source, (120, 60), crop=(0, 25, 100, 50))
        self.assertEqual((master.w, master.h), (120, 60))
        self.assertEqual(report["crop"], [0, 25, 100, 50])

    def test_each_scale_gives_the_exact_size(self) -> None:
        for src, want in (((90, 54), (30, 18)), ((30, 18), (150, 90)), ((60, 60), (60, 60)), ((100, 40), (30, 12))):
            master, report = fit.fit_image(fit.synthetic_disc(src[0], (255, 0, 255)) if src[0] == src[1] else textured(*src, (100, 80, 60)), want)
            self.assertEqual((master.w, master.h), want)
            self.assertEqual(report["size"], list(want))


class CliTest(unittest.TestCase):
    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.root = Path(self._tmp.name)
        self.local = self.root / "local"
        self.manifest = part_manifest(self.root, [
            {"id": "plate.round", "kind": "plate", "size": [180, 180]},
            {"id": "plate.open", "kind": "plate", "size": None},
        ])
        self.source = self.root / "disc.png"
        self.source.write_bytes(pngio.encode(fit.synthetic_disc(96, (255, 0, 255))))
        self.record = self.root / "disc.json"
        self.record.write_text(json.dumps({"image_sha256": fit.sha256_file(self.source), "prompt_id": "kind-plate", "prompt_sha256": "p" * 64,
                                           "references": [{"file": "refs/a.png", "sha256": "r" * 64}], "generator": "test generator", "generator_version": "1",
                                           "date": "2026-11-20", "run_by": "owner"}), encoding="utf-8")

    def run_cli(self, *args: str) -> tuple[int, str, str]:
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            code = fit.main(["fit", *args, "--manifest", str(self.manifest), "--local-root", str(self.local), "--date", "2026-11-20"])
        return code, out.getvalue(), err.getvalue()

    def test_dry_run_writes_nothing(self) -> None:
        code, out, _ = self.run_cli("plate.round", "--source", str(self.source), "--provenance", str(self.record), "--key", "border", "--dry-run")
        self.assertEqual(code, 0, out)
        self.assertIn("DRY-RUN", out)
        self.assertIn("PASS", out)
        self.assertFalse((self.local / "masters").exists())
        self.assertFalse((self.local / "provenance").exists())

    def test_fit_writes_the_master_and_the_provenance(self) -> None:
        code, out, err = self.run_cli("plate.round", "--source", str(self.source), "--provenance", str(self.record), "--key", "border")
        self.assertEqual(code, 0, err)
        master = pngio.read(self.local / "masters" / "plate.round.png")
        self.assertEqual((master.w, master.h), (180, 180))
        record = json.loads((self.local / "provenance" / "fit" / "plate.round.json").read_text())
        self.assertEqual(record["record"], "fit:plate.round")
        self.assertEqual(record["generator"]["generator"], "test generator")
        self.assertEqual(record["generator"]["prompt_id"], "kind-plate")
        self.assertEqual(record["master"]["sha256"], fit.sha256_file(self.local / "masters" / "plate.round.png"))
        self.assertEqual(record["source"]["sha256"], fit.sha256_file(self.source))
        self.assertEqual(record["source"]["size"], [96, 96])
        self.assertEqual(record["source"]["alpha"], "key")
        self.assertEqual(record["tool_version"], fit.TOOL_VERSION)
        self.assertLessEqual(record["halo"]["mean_de"], fit.HALO_MEAN_DE)

    def test_second_run_gives_the_same_bytes_and_needs_replace(self) -> None:
        args = ("plate.round", "--source", str(self.source), "--provenance", str(self.record), "--key", "border")
        self.assertEqual(self.run_cli(*args)[0], 0)
        first = (self.local / "masters" / "plate.round.png").read_bytes()
        code, _, err = self.run_cli(*args)
        self.assertEqual(code, 2)
        self.assertIn("--replace", err)
        self.assertEqual(self.run_cli(*args, "--replace")[0], 0)
        self.assertEqual((self.local / "masters" / "plate.round.png").read_bytes(), first)

    def test_the_master_is_a_valid_kit_master(self) -> None:
        self.assertEqual(self.run_cli("plate.round", "--source", str(self.source), "--provenance", str(self.record), "--key", "border")[0], 0)
        master = pngio.read(self.local / "masters" / "plate.round.png")
        self.assertEqual(master.w % 6 + master.h % 6, 0)  # SWI-KIT-2
        blob = (self.local / "masters" / "plate.round.png").read_bytes()
        self.assertEqual(blob[25], 6)  # colour type RGBA

    def test_provenance_is_required_and_checked(self) -> None:
        code, _, err = self.run_cli("plate.round", "--source", str(self.source), "--key", "border")
        self.assertEqual(code, 2)
        self.assertIn("provenance", err)
        self.record.write_text(json.dumps({"image_sha256": "0" * 64}), encoding="utf-8")
        code, _, err = self.run_cli("plate.round", "--source", str(self.source), "--provenance", str(self.record), "--key", "border")
        self.assertEqual(code, 2)
        self.assertIn("hash mismatch", err)
        code, _, err = self.run_cli("plate.round", "--source", str(self.source), "--no-provenance", "--key", "border")
        self.assertEqual(code, 0, err)

    def test_dry_run_sees_an_existing_master_and_writes_no_temp_files(self) -> None:
        args = ("plate.round", "--source", str(self.source), "--provenance", str(self.record), "--key", "border")
        self.assertEqual(self.run_cli(*args)[0], 0)
        self.assertEqual(self.run_cli(*args, "--dry-run")[0], 2)  # the real run would refuse too
        self.assertEqual(self.run_cli(*args, "--dry-run", "--replace")[0], 0)
        self.assertEqual([p.name for p in self.local.rglob("*.tmp")], [])
        self.assertEqual(self.run_cli("plate.round", *args[1:], "--crop", "1,2,a,4")[0], 2)  # bad crop text: exit 2, no traceback

    def test_thin_feature_keying_does_not_depend_on_raster_order(self) -> None:
        image = pngio.Image(8, 8, bytearray(bytes((0, 255, 0, 255)) * 64), False)
        for y in range(3, 6):
            for x in range(3, 6):
                image.data[(y * 8 + x) * 4:(y * 8 + x) * 4 + 4] = bytes((200, 40, 40, 255))
        flipped = pngio.Image(8, 8, bytearray(), False)
        flipped.data = bytearray(b"".join(bytes(image.data[(y * 8 + x) * 4:(y * 8 + x) * 4 + 4]) for y in range(7, -1, -1) for x in range(7, -1, -1)))
        for img in (image, flipped):
            fit.key_background(img, (0, 255, 0), 0.0)
        self.assertEqual(bytes(image.data[3::4]), bytes(flipped.data[3::4])[::-1])

    def test_lock_strength_is_checked_without_a_material_too(self) -> None:
        code, _, err = self.run_cli("plate.round", "--source", str(self.source), "--provenance", str(self.record), "--key", "border", "--lock-strength", "5")
        self.assertEqual(code, 2)
        self.assertIn("lock-strength", err)

    def test_real_alpha_source_records_no_key(self) -> None:
        alpha_source = self.root / "alpha.png"
        alpha_source.write_bytes(pngio.encode(fit.synthetic_disc(96, None)))
        self.assertEqual(self.run_cli("plate.round", "--source", str(alpha_source), "--no-provenance", "--key", "border")[0], 0)
        record = json.loads((self.local / "provenance" / "fit" / "plate.round.json").read_text())
        self.assertEqual(record["source"]["alpha"], "source")
        self.assertEqual(record["source"]["key"], "none")

    def test_open_size_unknown_part_and_bad_id(self) -> None:
        base = ("--source", str(self.source), "--provenance", str(self.record), "--key", "border")
        self.assertEqual(self.run_cli("plate.open", *base)[0], 2)
        code, out, err = self.run_cli("plate.open", *base, "--size", "120x120")
        self.assertEqual(code, 0, err)
        self.assertTrue((self.local / "masters" / "plate.open.png").is_file())
        self.assertEqual(self.run_cli("plate.nope", *base)[0], 2)
        self.assertEqual(self.run_cli("../escape", *base)[0], 2)

    def test_gate_failure_writes_nothing(self) -> None:
        old = fit.HALO_MEAN_DE, fit.HALO_P95_DE
        self.addCleanup(lambda: (setattr(fit, "HALO_MEAN_DE", old[0]), setattr(fit, "HALO_P95_DE", old[1])))
        fit.HALO_MEAN_DE, fit.HALO_P95_DE = -1.0, -1.0
        code, out, err = self.run_cli("plate.round", "--source", str(self.source), "--provenance", str(self.record), "--key", "border")
        self.assertEqual(code, 1)
        self.assertIn("GATE FAILED", err)
        self.assertFalse((self.local / "masters").exists())

    def test_outbox_form(self) -> None:
        outbox = self.local / "outbox" / "b1"
        outbox.mkdir(parents=True)
        (outbox / "kit-plate.png").write_bytes(self.source.read_bytes())
        prov = self.local / "provenance" / "b1"
        prov.mkdir(parents=True)
        (prov / "kit-plate.json").write_text(self.record.read_text(), encoding="utf-8")
        code, out, err = self.run_cli("plate.round", "--batch", "b1", "--prompt", "kit-plate", "--key", "border")
        self.assertEqual(code, 0, err)
        record = json.loads((self.local / "provenance" / "fit" / "plate.round.json").read_text())
        self.assertEqual(record["source"]["file"], "outbox/b1/kit-plate.png")

    def test_self_test_command(self) -> None:
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            self.assertEqual(fit.main(["self-test"]), 0)
        self.assertIn("PASS", out.getvalue())


class RealManifestTest(unittest.TestCase):
    def test_find_part_in_the_real_manifest(self) -> None:
        part = fit.find_part("material.brass")
        self.assertIsNone(part["size"])
        with self.assertRaises(fit.FitError):
            fit.find_part("no.such.part")


if __name__ == "__main__":
    unittest.main()
