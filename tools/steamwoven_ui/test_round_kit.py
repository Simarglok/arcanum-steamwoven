"""ARC-UI-3202: tests of the round button kit tool on synthetic frames (no art)."""
from __future__ import annotations

import io
import json
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path

from . import button_kit as bk
from . import pngio
from . import round_kit as rk

N = rk.ROUND_UNITS
M = rk.ROUND_MASTER
DOME = (40, 39, 12)
ORANGE = (140, 80, 13)
RED = (240, 50, 30)


def ring_colour(x: int, y: int) -> tuple[int, int, int]:
    """A ring that is not flat: lighter at the upper left, darker at the lower right."""
    t = (x + y) / (2.0 * (N - 1))
    return (int(150 - 120 * t), int(130 - 100 * t), int(40 - 25 * t))


def disc(units: int = N, pixel=None) -> pngio.Image:
    """A round frame: transparent corners, a ring from the radius 16 cells out, a flat dome inside."""
    image = pngio.Image(units, units, bytearray(units * units * 4))
    for y in range(units):
        for x in range(units):
            r = rk.radius(x, y, units)
            if r > (units - 1) / 2 + 0.5:
                continue
            colour = DOME if r < rk.DOME_RADIUS * units / N else ring_colour(x, y)
            image.data[(y * units + x) * 4:(y * units + x) * 4 + 4] = bytes((*colour, 255))
    return image


def paint(image: pngio.Image, cells, colour) -> pngio.Image:
    out = pngio.Image(image.w, image.h, bytearray(image.data))
    for x, y in cells:
        out.data[(y * image.w + x) * 4:(y * image.w + x) * 4 + 3] = bytes(colour)
    return out


def symbol(shift: int) -> list[tuple[int, int]]:
    return [(x + shift, y) for x in range(14, 21) for y in range(14, 24)]


def darken(image: pngio.Image, share: float, left_of: int) -> pngio.Image:
    """The pressed look: every cell left of `left_of` keeps `share` of its colour."""
    out = pngio.Image(image.w, image.h, bytearray(image.data))
    for y in range(image.h):
        for x in range(left_of):
            o = (y * image.w + x) * 4
            if out.data[o + 3]:
                out.data[o:o + 3] = bytes(int(c * share) for c in out.data[o:o + 3])
    return out


def write_frames(root: Path) -> None:
    for index, b in enumerate(rk.ROUND_BUTTONS):
        cells = symbol(index)
        idle = paint(disc(), cells, ORANGE)
        hover = paint(paint(disc(), [(x, y) for x in range(12 + index, 23 + index) for y in range(12, 26) if (x, y) not in set(cells)], (62, 45, 19)), cells, RED)
        pressed = darken(hover, 0.5, 14)
        (root / b.folder).mkdir(parents=True)
        for n, frame in enumerate((idle, pressed, hover)):
            pngio.write(root / b.folder / f"f{n}.png", frame)
    ball = disc(rk.BALL_UNITS)
    (root / rk.BALL.folder).mkdir(parents=True)
    pngio.write(root / rk.BALL.folder / "f0.png", ball)
    pngio.write(root / rk.BALL.folder / "f1.png", darken(ball, 0.5, 9))


class TemplateTests(unittest.TestCase):
    def frames(self):
        return [paint(disc(), symbol(i), ORANGE) for i in range(4)]

    def test_template_is_the_empty_plate_whatever_the_symbols(self):
        template = rk.round_template(self.frames())
        self.assertEqual(bytes(template.data), bytes(disc().data))

    def test_a_symbol_that_reaches_the_ring_is_outvoted(self):
        frames = self.frames()
        frames[0] = paint(frames[0], [(36, 19)], ORANGE)
        self.assertEqual(bytes(rk.round_template(frames).data), bytes(disc().data))

    def test_template_refuses_a_ring_the_frames_do_not_agree_on_and_other_circles(self):
        frames = self.frames()
        cell = (36, 19)
        for frame, colour in zip(frames, ((1, 2, 3), (90, 5, 6), (7, 200, 9), (10, 11, 255))):
            frame.data[(cell[1] * N + cell[0]) * 4:(cell[1] * N + cell[0]) * 4 + 3] = bytes(colour)
        with self.assertRaises(rk.RoundError):
            rk.round_template(frames)
        other = self.frames()
        other[1].data[3] = 255  # a corner that is not clear
        with self.assertRaises(rk.RoundError):
            rk.round_template(other)
        with self.assertRaises(rk.RoundError):
            rk.round_template(self.frames()[:2])


class PartTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.frames = self.root / "frames"
        write_frames(self.frames)

    def tearDown(self):
        self.tmp.cleanup()

    def at(self, image, ux, uy, units=N, master=M):
        return image.pixel(int((ux + 0.5) * master / units), int((uy + 0.5) * master / units))

    def test_every_part_of_the_slice_is_built_at_the_face_size(self):
        parts = rk.build_parts(self.frames, None)
        want = {f"{kind}.{b.name}{suffix}" for b in rk.ROUND_BUTTONS for kind, suffix in (("glyph", ""), ("plate", "_hover"), ("plate", "_pressed"))}
        want |= {"glyph.fate_but", "glyph.sleep_but", "plate.lilgrnbut_pressed"}
        self.assertEqual(set(parts), want)
        for ident, image in parts.items():
            self.assertEqual((image.w, image.h), (rk.BALL_MASTER,) * 2 if ident.endswith(("fate_but", "sleep_but", "lilgrnbut_pressed")) else (M, M), ident)
        self.assertNotIn("plate.lilgrnbut_hover", parts)  # a two-frame art has no hover look

    def test_the_glyph_is_the_idle_symbol_in_its_own_colours_and_the_plate_stays_clear(self):
        glyph = rk.build_parts(self.frames, None, {"combat_button"})["glyph.combat_button"]
        self.assertEqual(self.at(glyph, 17, 18), (*ORANGE, 255))
        self.assertEqual(self.at(glyph, 8, 18)[3], 0)  # the dome is the plate's
        self.assertEqual(self.at(glyph, 37, 19)[3], 0)  # so is the ring

    def test_the_ball_has_empty_glyphs_and_a_record_that_says_why(self):
        parts = rk.build_parts(self.frames, None, {"lilgrnbut"})
        self.assertEqual(set(parts["glyph.fate_but"].data[3::4]), {0})
        out = self.root / "out"
        with redirect_stdout(io.StringIO()):
            self.assertEqual(rk.main(["build", "--frames", str(self.frames), "--local-root", str(self.root / "local"), "--out", str(out), "--only", "lilgrnbut"]), 0)
        record = json.loads((out / "records" / "glyph.sleep_but.json").read_text())
        self.assertTrue(record["empty"])
        self.assertIn("137-lilgrnbut/f1", record["sources"])
        self.assertEqual(record["image_size"], [rk.BALL_MASTER, rk.BALL_MASTER])

    def test_a_glyph_over_the_template_gives_the_idle_symbol_cells_back(self):
        template = rk.round_template([rk.read_frame(self.frames, b.folder, 0, N) for b in rk.ROUND_BUTTONS])
        idle = rk.read_frame(self.frames, rk.ROUND_BUTTONS[0].folder, 0, N)
        glyph = rk.build_parts(self.frames, None, {"combat_button"})["glyph.combat_button"]
        plate = rk.upscaled(template, M)
        over = bk.composite(plate, glyph)
        for ux, uy in ((15, 16), (19, 20), (17, 23)):
            cell = tuple(idle.data[(uy * N + ux) * 4:(uy * N + ux) * 4 + 3])
            self.assertEqual(self.at(over, ux, uy)[:3], cell)

    def test_the_pressed_overlay_darkens_the_plate_it_does_not_repaint_it(self):
        parts = rk.build_parts(self.frames, None, {"combat_button"})
        pressed = parts["plate.combat_button_pressed"]
        ring = self.at(pressed, 3, 19)  # a left ring cell: the plate's brightness x 0.5
        self.assertEqual(ring[:3], (0, 0, 0))  # a soft black layer
        self.assertAlmostEqual(ring[3] / 255.0, 0.5, delta=0.1)
        self.assertEqual(self.at(pressed, 30, 8)[3], 0)  # the right side keeps the idle look
        self.assertEqual(self.at(pressed, 16, 19)[:3], RED)  # the lit symbol keeps its own cells
        # laid over a plate of the idle frame's colours it gives the pressed frame back, to within the soft layer
        idle = rk.read_frame(self.frames, rk.ROUND_BUTTONS[0].folder, 0, N)
        want = rk.read_frame(self.frames, rk.ROUND_BUTTONS[0].folder, 1, N)
        over = bk.composite(rk.upscaled(idle, M), pressed)
        for ux, uy in ((3, 19), (6, 12), (24, 24), (16, 19)):
            cell = tuple(want.data[(uy * N + ux) * 4:(uy * N + ux) * 4 + 3])
            self.assertLessEqual(max(abs(a - b) for a, b in zip(self.at(over, ux, uy)[:3], cell)), 14, (ux, uy))

    def test_the_hover_overlay_is_the_lit_symbol_and_its_glow_only(self):
        hover = rk.build_parts(self.frames, None, {"combat_button"})["plate.combat_button_hover"]
        self.assertEqual(self.at(hover, 15, 16)[:3], RED)
        self.assertGreater(self.at(hover, 12, 19)[3], 0)  # the glow
        self.assertEqual(self.at(hover, 3, 19)[3], 0)  # the ring is untouched

    def test_an_overlay_never_leaves_the_plate(self):
        plates = self.root / "plates"
        plates.mkdir()
        small = pngio.Image(M, M, bytearray(M * M * 4))
        for py in range(M):
            for px in range(M):
                if (px - M / 2) ** 2 + (py - M / 2) ** 2 <= 40 ** 2:
                    small.data[(py * M + px) * 4:(py * M + px) * 4 + 4] = bytes((50, 50, 20, 255))
        pngio.write(plates / "plate.combat_button.png", small)
        pressed = rk.build_parts(self.frames, plates, {"combat_button"})["plate.combat_button_pressed"]
        self.assertEqual(self.at(pressed, 1, 19)[3], 0)  # a ring cell outside the small plate: clipped
        self.assertGreater(self.at(pressed, 8, 19)[3], 0)

    def test_a_wrong_sized_plate_master_or_frame_is_refused(self):
        plates = self.root / "plates"
        plates.mkdir()
        pngio.write(plates / "plate.combat_button.png", pngio.Image(6, 6, bytearray(144)))
        with self.assertRaises(rk.RoundError):
            rk.build_parts(self.frames, plates, {"combat_button"})
        (self.frames / "odd").mkdir()
        pngio.write(self.frames / "odd" / "f0.png", pngio.Image(4, 4, bytearray(64)))
        with self.assertRaises(rk.RoundError):
            rk.read_frame(self.frames, "odd", 0, N)
        with self.assertRaises(rk.RoundError):
            rk.read_frame(self.frames, "no-such-art", 0, N)

    def test_same_bytes_on_a_second_run(self):
        first = rk.build_parts(self.frames, None, {"skills_button"})
        second = rk.build_parts(self.frames, None, {"skills_button"})
        self.assertEqual({k: bytes(v.data) for k, v in first.items()}, {k: bytes(v.data) for k, v in second.items()})

    def test_build_command_dry_run_writes_nothing_and_a_misspelt_button_is_refused(self):
        local, out = self.root / "local", self.root / "out"
        base = ["build", "--frames", str(self.frames), "--local-root", str(local), "--out", str(out)]
        with redirect_stdout(io.StringIO()):
            self.assertEqual(rk.main(base + ["--only", "combat_button", "--dry-run"]), 0)
            self.assertFalse(out.exists())
            self.assertEqual(rk.main(base + ["--only", "combat"]), 2)
            self.assertEqual(rk.main(base + ["--only", "combat_button"]), 0)
        record = json.loads((out / "records" / "glyph.combat_button.json").read_text())
        self.assertEqual(record["rule"], "round_kit")
        self.assertEqual(len(record["template_sources"]), 4)
        self.assertEqual(record["image_size"], [M, M])
        with redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()):
            self.assertEqual(rk.main(["build", "--frames", str(self.root / "nope"), "--local-root", str(local), "--dry-run"]), 2)

    def test_source_folders_name_the_frames_a_part_reads(self):
        self.assertEqual(rk.source_folders("glyph.fate_but"), ([rk.BALL.folder], 2))
        self.assertEqual(rk.source_folders("plate.skills_button_pressed"), (["472-Skills_Button"], 3))
        with self.assertRaises(rk.RoundError):
            rk.source_folders("glyph.key")


class ShadeTests(unittest.TestCase):
    """The shade, solid_cell and clip options of `button_kit.render_crisp` that this slice added."""

    def setUp(self):
        self.base = disc()
        self.frame = darken(paint(self.base, symbol(0), RED), 0.5, 14)

    def at(self, image, ux, uy):
        return image.pixel(int((ux + 0.5) * M / N), int((uy + 0.5) * M / N))

    def test_without_shade_a_darkened_cell_is_repainted_flat(self):
        flat = bk.render_crisp(self.frame, self.base, True, pixels=M, owned_cell=rk.own_cells(self.base, True))
        self.assertGreater(self.at(flat, 3, 19)[3], 200)  # a solid cell in the original's colour
        self.assertNotEqual(self.at(flat, 3, 19)[:3], (0, 0, 0))

    def test_shade_makes_it_a_soft_black_layer(self):
        soft = bk.render_crisp(self.frame, self.base, True, pixels=M, owned_cell=rk.own_cells(self.base, True), shade=True)
        self.assertEqual(self.at(soft, 3, 19)[:3], (0, 0, 0))
        self.assertLess(self.at(soft, 3, 19)[3], 200)

    def test_the_footprint_frame_keeps_the_symbol_out_of_the_shade(self):
        hover = paint(self.base, symbol(0), RED)
        shaded = bk.render_crisp(self.frame, self.base, True, pixels=M, owned_cell=rk.own_cells(self.base, True), shade=hover)
        self.assertEqual(self.at(shaded, 16, 19)[:3], RED)
        self.assertEqual(self.at(shaded, 16, 19)[3], 255)

    def test_solid_cell_turns_a_brightened_ring_into_a_soft_layer(self):
        ring = [(x, 19) for x in range(33, 37)]
        frame = paint(self.base, ring, (230, 200, 90))
        flat = bk.render_crisp(frame, self.base, True, pixels=M, owned_cell=rk.own_cells(self.base, True))
        soft = bk.render_crisp(frame, self.base, True, pixels=M, owned_cell=rk.own_cells(self.base, True), solid_cell=rk.own_cells(self.base, False, 8.5))
        self.assertEqual(self.at(flat, 34, 19)[:3], (230, 200, 90))
        self.assertGreater(self.at(soft, 34, 19)[3], 0)
        self.assertLess(self.at(soft, 34, 19)[3], 255)

    def test_clip_multiplies_the_alpha_and_shade_with_halo_is_refused(self):
        clip = pngio.Image(M, M, bytearray(M * M * 4))
        for i in range(3, len(clip.data), 4):
            clip.data[i] = 128
        half = bk.render_crisp(self.frame, self.base, True, pixels=M, owned_cell=rk.own_cells(self.base, True), shade=True, clip=clip)
        full = bk.render_crisp(self.frame, self.base, True, pixels=M, owned_cell=rk.own_cells(self.base, True), shade=True)
        self.assertAlmostEqual(self.at(half, 16, 19)[3], self.at(full, 16, 19)[3] * 128 / 255, delta=2)
        with self.assertRaises(bk.ButtonError):
            bk.render_crisp(self.frame, self.base, True, pixels=M, shade=True, halo=True)

    def test_clip_thins_the_alpha_and_leaves_the_colour_alone(self):
        clip = pngio.Image(M, M, bytearray(M * M * 4))
        for i in range(3, len(clip.data), 4):
            clip.data[i] = 128
        hover = paint(self.base, symbol(0), RED)
        clipped = bk.render_crisp(hover, self.base, True, pixels=M, owned_cell=rk.own_cells(self.base, False), clip=clip)
        pixel = self.at(clipped, 16, 19)
        self.assertEqual(pixel[:3], RED)
        self.assertAlmostEqual(pixel[3], 128, delta=1)

    def test_without_a_plate_master_the_disc_of_the_frames_own_circle_clips(self):
        clip = rk.disc_clip(self.base, M)
        self.assertEqual(clip.pixel(M // 2, M // 2)[3], 255)
        self.assertEqual(clip.pixel(1, 1)[3], 0)  # the corner of the picture
        parts = rk.build_parts(self.root_frames(), None, {"combat_button"})
        self.assertEqual(self.at(parts["plate.combat_button_pressed"], 0, 0)[3], 0)

    def root_frames(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        write_frames(Path(self.tmp.name))
        return Path(self.tmp.name)

    def test_the_ball_pressed_overlay_darkens_the_dome_and_lightens_the_rim_softly(self):
        ball = disc(rk.BALL_UNITS)
        rim = [(x, 11) for x in range(19, 22)]
        pressed = darken(paint(ball, rim, (230, 200, 90)), 0.5, 9)
        out = bk.render_crisp(pressed, ball, True, pixels=rk.BALL_MASTER, owned_cell=rk.own_cells(ball, True), shade=True, solid_cell=rk.own_cells(ball, False, rk.BALL_DOME))
        at = lambda ux, uy: out.pixel(int((ux + 0.5) * rk.BALL_MASTER / rk.BALL_UNITS), int((uy + 0.5) * rk.BALL_MASTER / rk.BALL_UNITS))
        self.assertEqual(at(5, 11)[:3], (0, 0, 0))  # the darkened dome: a soft black layer
        self.assertGreater(at(20, 11)[3], 0)
        self.assertLess(at(20, 11)[3], 255)  # the brightened rim: soft, no flat cell

    def test_shade_share(self):
        self.assertAlmostEqual(bk.shade_share((20, 20, 6), (40, 39, 12)), 0.505, places=2)
        self.assertIsNone(bk.shade_share((40, 39, 12), (40, 39, 12)))  # not darker
        self.assertIsNone(bk.shade_share((250, 250, 250), (40, 39, 12)))  # brighter
        self.assertIsNone(bk.shade_share((0, 0, 0), (2, 2, 2)))  # too dark to say a share


if __name__ == "__main__":
    unittest.main()
