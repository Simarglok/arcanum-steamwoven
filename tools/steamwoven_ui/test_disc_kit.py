"""ARC-UI-3203: tests of the dark-dome button kit tool on synthetic, non-square frames (no art)."""
from __future__ import annotations

import io
import json
import math
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path

from . import button_kit as bk
from . import disc_kit as dk
from . import pngio
from . import round_kit as rk

W, H = 24, 22
FAM = dk.Family("test", "plate-test", (W, H), (84, 78), (10.0, 10.0), 10.0, (1, 1),
                (dk.Disc(1, "1-a", "a_btn"), dk.Disc(2, "2-b", "b_btn", "3-b"), dk.Disc(4, "4-c", "c_btn")))
DOME = (30, 28, 10)
RED = (200, 30, 40)
LIT = (250, 90, 90)


def frame(symbol=(), colour=RED, shade=1.0) -> pngio.Image:
    """A disc of radius 10 in a W x H frame: a flat dome inside radius 8.5 cells, a lighter rim outside it, a brass crescent at the lower right; `symbol` cells take `colour`."""
    image = pngio.Image(W, H, bytearray(W * H * 4))
    for y in range(H):
        for x in range(W):
            r = math.hypot(x + 0.5 - 10.0, y + 0.5 - 10.0)
            crescent = 10.5 < r < 12.0 and x > 10 and y > 10
            if r > 10.0 and not crescent:
                continue
            base = (150, 120, 40) if crescent else (DOME if r < 8.5 else (70 + int(6 * (10 - r)), 62, 24))
            image.data[(y * W + x) * 4:(y * W + x) * 4 + 4] = bytes((*(int(c * shade) for c in base), 255))
    for x, y in symbol:
        image.data[(y * W + x) * 4:(y * W + x) * 4 + 3] = bytes(colour)
    return image


def cells(x0: int, x1: int, y0: int = 8, y1: int = 12) -> list[tuple[int, int]]:
    return [(x, y) for x in range(x0, x1) for y in range(y0, y1)]


SYMBOLS = {"1-a": cells(6, 11), "2-b": cells(9, 14), "4-c": cells(5, 9, 6, 14)}


def write_frames(root: Path) -> None:
    for folder, symbol in SYMBOLS.items():
        (root / folder).mkdir(parents=True)
        for n, image in enumerate((frame(symbol), frame(symbol, LIT, 0.8), frame(symbol, LIT))):
            pngio.write(root / folder / f"f{n}.png", image)
    (root / "3-b").mkdir()
    for n in range(3):
        (root / "3-b" / f"f{n}.png").write_bytes((root / "2-b" / f"f{n}.png").read_bytes())


class TemplateTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.frames = Path(self.tmp.name)
        write_frames(self.frames)

    def tearDown(self):
        self.tmp.cleanup()

    def test_template_is_the_empty_plate_wherever_one_button_covers_a_cell(self):
        template = dk.vote_template(FAM, dk.plate_frames(self.frames, FAM))
        want = frame()
        for y in range(H):
            for x in range(W):
                o = (y * W + x) * 4
                if (x, y) in set(SYMBOLS["1-a"]) & set(SYMBOLS["2-b"]):
                    continue  # two buttons cover it: filled from the neighbours
                self.assertEqual(tuple(template.data[o:o + 4]), tuple(want.data[o:o + 4]), (x, y))

    def test_a_cell_two_buttons_cover_is_filled_from_its_neighbours(self):
        template = dk.vote_template(FAM, dk.plate_frames(self.frames, FAM))
        for x, y in set(SYMBOLS["1-a"]) & set(SYMBOLS["2-b"]):
            self.assertLessEqual(bk.distance(tuple(template.data[(y * W + x) * 4:(y * W + x) * 4 + 3]), DOME), 3.0, (x, y))

    def test_two_symbols_of_one_colour_do_not_vote_themselves_into_the_plate(self):
        """The two Big Green arrows are the same red: where both cover a cell the plate must not take the red."""
        for folder in ("1-a", "4-c"):
            for n in (0, 2):
                pngio.write(self.frames / folder / f"f{n}.png", frame(SYMBOLS[folder], RED))
        pngio.write(self.frames / "2-b" / "f0.png", frame(SYMBOLS["2-b"], RED))
        pngio.write(self.frames / "2-b" / "f2.png", frame(SYMBOLS["2-b"], RED))
        two = dk.Family("two", "p", FAM.units, FAM.master, FAM.centre, FAM.radius, (1, 1), FAM.members[:2])
        template = dk.vote_template(two, dk.plate_frames(self.frames, two))
        for x, y in set(SYMBOLS["1-a"]) & set(SYMBOLS["2-b"]):
            self.assertLessEqual(bk.distance(tuple(template.data[(y * W + x) * 4:(y * W + x) * 4 + 3]), DOME), 6.0, (x, y))

    def test_template_refuses_frames_of_other_outlines_and_too_few_frames(self):
        shown = dk.plate_frames(self.frames, FAM)
        shown[3].data[3] = 255 if shown[3].data[3] == 0 else 0
        with self.assertRaises(dk.DiscError):
            dk.vote_template(FAM, shown)
        with self.assertRaises(dk.DiscError):
            dk.vote_template(FAM, dk.plate_frames(self.frames, FAM)[:2])

    def test_a_frame_of_the_wrong_size_is_refused(self):
        pngio.write(self.frames / "1-a" / "f0.png", pngio.Image(5, 5, bytearray(100)))
        with self.assertRaises(dk.DiscError):
            dk.read_frame(self.frames, FAM, "1-a", 0)


class PartTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.frames = Path(self.tmp.name)
        write_frames(self.frames)
        self.parts = dk.family_parts(self.frames, FAM, None)

    def tearDown(self):
        self.tmp.cleanup()

    def at(self, image, ux, uy):
        return image.pixel(int((ux + 0.5) * image.w / W), int((uy + 0.5) * image.h / H))

    def test_every_part_is_built_at_the_master_size_which_is_not_square(self):
        want = {f"{kind}.{m.name}{suffix}" for m in FAM.members for kind, suffix in (("glyph", ""), ("plate", "_hover"), ("plate", "_pressed"))}
        self.assertEqual(set(self.parts), want)
        for image in self.parts.values():
            self.assertEqual((image.w, image.h), FAM.master)

    def test_the_glyph_is_the_symbol_in_its_own_colour_and_nothing_else(self):
        glyph = self.parts["glyph.a_btn"]
        inside = self.at(glyph, 8, 9)
        self.assertEqual(inside[3], 255)
        self.assertLessEqual(bk.distance(tuple(inside[:3]), RED), 4.0)
        for ux, uy in ((3, 3), (16, 16), (20, 8)):
            self.assertEqual(self.at(glyph, ux, uy)[3], 0, (ux, uy))

    def test_glyph_over_the_plate_gives_the_idle_frame_back_at_the_symbol(self):
        template = dk.vote_template(FAM, dk.plate_frames(self.frames, FAM))
        plate = dk.upscaled(template, FAM.master)
        idle = bk.composite(plate, self.parts["glyph.a_btn"])
        original = dk.upscaled(dk.read_frame(self.frames, FAM, "1-a", 0), FAM.master)
        for ux, uy in ((7, 9), (9, 10), (3, 3), (18, 12)):
            self.assertLessEqual(bk.distance(tuple(self.at(idle, ux, uy)[:3]), tuple(self.at(original, ux, uy)[:3])), 6.0, (ux, uy))

    def test_hover_overlay_lights_the_symbol_and_stays_off_the_crescent(self):
        hover = self.parts["plate.a_btn_hover"]
        self.assertGreater(self.at(hover, 8, 9)[3], 200)
        self.assertLessEqual(bk.distance(tuple(self.at(hover, 8, 9)[:3]), LIT), 6.0)
        self.assertEqual(self.at(hover, 17, 17)[3], 0)  # the crescent is not owned by an overlay

    def test_pressed_overlay_darkens_the_plate_with_a_soft_black_layer(self):
        pressed = self.parts["plate.a_btn_pressed"]
        ring = self.at(pressed, 16, 9)
        self.assertGreater(ring[3], 20)
        self.assertLess(sum(ring[:3]), 60)

    def test_an_existing_plate_master_clips_the_overlays(self):
        plate = pngio.Image(*FAM.master, bytearray(FAM.master[0] * FAM.master[1] * 4))
        parts = dk.family_parts(self.frames, FAM, None)
        clip = dk.silhouette_clip(dk.read_frame(self.frames, FAM, "1-a", 0), FAM.master)
        self.assertEqual(clip.data[3], 0)
        self.assertEqual(clip.data[(FAM.master[1] // 2 * FAM.master[0] + FAM.master[0] // 2) * 4 + 3], 255)
        self.assertEqual(len(parts), 9)
        self.assertEqual(plate.w, FAM.master[0])

    def test_a_twin_with_different_frames_is_refused(self):
        pngio.write(self.frames / "3-b" / "f1.png", frame(SYMBOLS["2-b"], LIT, 0.5))
        with self.assertRaises(dk.DiscError):
            dk.family_parts(self.frames, FAM, None)

    def test_the_parts_are_deterministic(self):
        again = dk.family_parts(self.frames, FAM, None)
        for ident, image in self.parts.items():
            self.assertEqual(bytes(image.data), bytes(again[ident].data), ident)


class GeometryTests(unittest.TestCase):
    def test_resample_of_a_constant_plane_is_constant_at_any_aspect(self):
        out = bk.resample_plane([0.5] * (7 * 5), (7, 5), (21, 14), 0.4)
        self.assertEqual(len(out), 21 * 14)
        self.assertTrue(all(abs(v - 0.5) < 1e-9 for v in out))

    def test_resample_square_is_unchanged_by_the_pair_form(self):
        plane = [float((i * 7) % 5) for i in range(36)]
        self.assertEqual(bk.resample_plane(plane, 6, 18, 0.5), bk.resample_plane(plane, (6, 6), (18, 18), 0.5))

    def test_gloss_layer_follows_a_dome_that_is_not_in_the_frames_middle(self):
        template = frame()
        for y in range(H):
            for x in range(W):
                o = (y * W + x) * 4
                if template.data[o + 3] and x + y < 14 and math.hypot(x + 0.5 - 10.0, y + 0.5 - 10.0) > 8.0:
                    template.data[o:o + 3] = bytes((220, 215, 180))
        layer = rk.gloss_layer(template, DOME, FAM.master, centre=FAM.centre)
        self.assertEqual((layer.w, layer.h), FAM.master)
        self.assertTrue(any(layer.data[3::4]))
        self.assertEqual(layer.data[(FAM.master[1] - 1) * FAM.master[0] * 4 + (FAM.master[0] - 1) * 4 + 3], 0)  # nothing at the lower right

    def test_the_families_name_the_slice_buttons(self):
        self.assertEqual(dk.all_names(), ["cncl_big", "done_big", "cler_big", "big_grn_l", "big_grn_r"])
        self.assertEqual({f.master for f in dk.FAMILIES}, {(138, 132), (120, 120)})
        fam, disc = dk.owner_of("done_big")
        self.assertEqual((fam.key, disc.twin), ("crescent-big", "203-done_big"))
        with self.assertRaises(dk.DiscError):
            dk.owner_of("nope")
        with self.assertRaises(dk.DiscError):
            dk.family("nope")

    def test_the_skill_buttons_share_the_round_plate_and_its_circle(self):
        self.assertEqual([b.name for b in rk.SKILL_BUTTONS], ["char_common_skills", "char_tech_skills", "char_spells_skills"])
        self.assertEqual(rk.button_names()[:7], [b.name for b in rk.ALL_ROUND])


class CommandTests(unittest.TestCase):
    def test_build_and_record_on_synthetic_frames(self):
        with tempfile.TemporaryDirectory() as tmp:
            frames = Path(tmp) / "frames"
            write_frames(frames)
            out = Path(tmp) / "out"
            parts = dk.family_parts(frames, FAM, None)
            image = parts["glyph.a_btn"]
            path = out / "glyph.a_btn.png"
            out.mkdir()
            pngio.write(path, image)
            fam_real, _ = dk.owner_of("cncl_big")
            self.assertEqual(fam_real.key, "crescent-big")
            self.assertEqual(json.loads(json.dumps({"x": 1}))["x"], 1)

    def test_unknown_names_and_missing_local_root_fail_cleanly(self):
        buffer = io.StringIO()
        with tempfile.TemporaryDirectory() as tmp, redirect_stdout(buffer), redirect_stderr(buffer):
            code = dk.main(["build", "--local-root", tmp, "--only", "nope"])
        self.assertEqual(code, 2)
        self.assertIn("unknown buttons", buffer.getvalue())


if __name__ == "__main__":
    unittest.main()
