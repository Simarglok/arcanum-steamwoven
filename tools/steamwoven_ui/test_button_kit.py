"""ARC-UI-2482: tests of the button kit tool on synthetic frames (no art)."""
from __future__ import annotations

import io
import json
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path

from . import button_kit as bk
from . import pngio

BASE = (46, 45, 19)
LINE = (92, 90, 38)
SHADOW = (2, 0, 0)
ORANGE = (140, 80, 13)
RED = (240, 50, 30)
N = bk.UNITS


def plate() -> pngio.Image:
    """A synthetic frame: a light line round the edge, a dark line on the right and the bottom, a flat inset."""
    image = pngio.Image(N, N, bytearray(N * N * 4))
    for y in range(N):
        for x in range(N):
            colour = BASE
            if x == 0 or y == 0 or (x == 3 and y > 3) or (y == 3 and x > 3):
                colour = LINE
            if x == N - 1 or y == N - 1:
                colour = SHADOW
            image.data[(y * N + x) * 4:(y * N + x) * 4 + 4] = bytes((*colour, 255))
    return image


def paint(image: pngio.Image, cells: list[tuple[int, int]], colour: tuple[int, int, int]) -> pngio.Image:
    out = pngio.Image(image.w, image.h, bytearray(image.data))
    for x, y in cells:
        out.data[(y * N + x) * 4:(y * N + x) * 4 + 3] = bytes(colour)
    return out


GLYPH = [(x, y) for x in range(12, 20) for y in range(12, 22)]
GLOW = [(x, y) for x in range(10, 22) for y in range(10, 24) if (x, y) not in GLYPH]


def write_frames(root: Path, with_second_on: bool = True) -> None:
    """Five buttons: the same plate, a glyph at a different place in each (so the template vote works), idle orange, hover and pressed red with a faint glow, a lit art."""
    for index, button in enumerate(bk.BUTTONS):
        shift = [(x + index, y) for x, y in GLYPH]
        idle = paint(plate(), shift, ORANGE)
        hover = paint(paint(plate(), [(x + index, y) for x, y in GLOW], (62, 45, 19)), shift, RED)
        pressed = paint(hover, [(26, 26)], SHADOW)
        for folder, frames in ((button.folder, (idle, pressed, hover)), (button.on_folder, (hover, pressed) if with_second_on and button.on_art != 561 else (hover,))):
            (root / folder).mkdir(parents=True)
            for n, frame in enumerate(frames):
                pngio.write(root / folder / f"f{n}.png", frame)


class CellTests(unittest.TestCase):
    def test_template_is_the_plate_whatever_the_symbols(self):
        frames = [paint(plate(), [(x + i, y) for x, y in GLYPH], ORANGE) for i in range(5)]
        template = bk.plate_template(frames)
        self.assertEqual(bytes(template.data), bytes(plate().data))

    def test_template_refuses_frames_that_disagree_on_a_frame_line(self):
        frames = [paint(plate(), [(0, 7)], c) for c in ((1, 2, 3), (4, 5, 6), (7, 8, 9), (10, 11, 12), (13, 14, 15))]
        with self.assertRaises(bk.ButtonError):
            bk.plate_template(frames)

    def test_template_needs_three_frames(self):
        with self.assertRaises(bk.ButtonError):
            bk.plate_template([plate(), plate()])

    def test_a_layer_over_its_base_gives_the_frame_back(self):
        base = plate()
        frame = paint(paint(base, [(15, 15), (16, 15)], (70, 52, 22)), GLYPH, ORANGE)  # a faint cell (a glow) and a solid symbol
        colours, alphas = bk.decompose(frame, base)
        self.assertEqual(alphas[15 * N + 5], 0.0)  # an untouched cell is clear
        self.assertEqual(alphas[14 * N + 14], 1.0)
        for i in range(N * N):  # cell by cell, laid over the base
            a = alphas[i]
            got = tuple(round(a * c + (1 - a) * b) for c, b in zip(colours[i], base.data[i * 4:i * 4 + 3]))
            want = tuple(frame.data[i * 4:i * 4 + 3])
            self.assertLessEqual(max(abs(g - w) for g, w in zip(got, want)), 2, (i % N, i // N))

    def test_a_cell_within_the_dead_zone_is_clear(self):
        base = plate()
        frame = paint(base, [(15, 15)], (BASE[0] + 1, BASE[1], BASE[2]))
        self.assertEqual(bk.decompose(frame, base)[1][15 * N + 15], 0.0)

    def test_axis_weights_sum_to_one_and_repeat_the_edge(self):
        for sigma in (0.3, 0.65):
            for first, weights in bk.axis_weights(N, 120, sigma):
                self.assertAlmostEqual(sum(weights), 1.0, places=6)
                self.assertGreaterEqual(first, 0)

    def test_a_flat_plane_stays_flat(self):
        out = bk.resample_plane([0.7] * (N * N), N, 60, 0.55)
        self.assertLess(max(abs(v - 0.7) for v in out), 1e-9)

    def test_a_clear_layer_renders_clear_and_a_solid_cell_is_solid_in_its_middle(self):
        clear = bk.render_layer([BASE] * (N * N), [0.0] * (N * N), "cells")
        self.assertEqual(set(clear.data[3::4]), {0})
        alphas = [1.0 if (x, y) in set(GLYPH) else 0.0 for y in range(N) for x in range(N)]
        for look in ("sharp", "cells", "smooth"):
            layer = bk.render_layer([ORANGE] * (N * N), alphas, look)
            middle = layer.pixel(int(16 * bk.MASTER / N), int(17 * bk.MASTER / N))
            self.assertEqual(middle[3], 255)
            self.assertEqual(middle[:3], ORANGE)
            self.assertEqual(layer.pixel(2, 2)[3], 0)

    def test_unknown_look_is_refused(self):
        with self.assertRaises(bk.ButtonError):
            bk.render_layer([BASE] * (N * N), [0.0] * (N * N), "blurry")

    def test_composite_of_opaque_and_half_alpha(self):
        base = pngio.Image(1, 1, bytearray((0, 0, 0, 255)))
        over = pngio.Image(1, 1, bytearray((200, 100, 50, 128)))
        out = bk.composite(base, over)
        self.assertEqual(tuple(out.data), (100, 50, 25, 255))
        with self.assertRaises(bk.ButtonError):
            bk.composite(base, pngio.Image(2, 1, bytearray(8)))


class CrispTests(unittest.TestCase):
    def sym(self, cells, colour=ORANGE, holes=()):
        base = plate()
        return paint(paint(base, cells, colour), list(holes), BASE), base

    def at(self, image, ux, uy):
        """The master pixel in the middle of original cell (ux, uy)."""
        return image.pixel(int((ux + 0.5) * bk.MASTER / N), int((uy + 0.5) * bk.MASTER / N))

    def test_symbol_cells_keep_their_own_flat_colour_and_the_ground_stays_clear(self):
        frame, base = self.sym(GLYPH)
        out = bk.render_crisp(frame, base, False)
        self.assertEqual(self.at(out, 15, 16), (*ORANGE, 255))
        self.assertEqual(self.at(out, 3, 20)[3], 0)

    def test_a_one_cell_hole_and_a_one_cell_dot_survive(self):
        hole, dot = (15, 16), (26, 8)
        frame, base = self.sym(GLYPH + [dot], holes=[hole])
        out = bk.render_crisp(frame, base, False)
        self.assertLess(self.at(out, *hole)[3], 128)  # the hole is a hole
        self.assertGreater(self.at(out, *dot)[3], 128)  # the dot is a dot
        self.assertEqual(self.at(out, 14, 16)[3], 255)  # its neighbours are solid

    def test_inner_cells_of_another_colour_stay_as_drawn(self):
        frame, base = self.sym(GLYPH)
        frame = paint(frame, [(15, 16), (16, 16)], RED)
        out = bk.render_crisp(frame, base, False)
        self.assertEqual(self.at(out, 15, 16)[:3], RED)
        self.assertEqual(self.at(out, 13, 16)[:3], ORANGE)

    def test_the_outline_is_a_curve_with_at_most_a_pixel_of_edge_not_a_blur(self):
        frame, base = self.sym(GLYPH)
        out = bk.render_crisp(frame, base, False)
        row = int(17 * bk.MASTER / N)
        alphas = [out.pixel(x, row)[3] for x in range(bk.MASTER)]
        partial = sum(1 for a in alphas if 0 < a < 255)
        self.assertLessEqual(partial, 6)  # two edges of the block, a pixel or two each

    def test_glow_is_a_soft_layer_under_the_symbol_and_the_idle_symbol_has_none(self):
        base = plate()
        frame = paint(paint(base, GLOW, (54, 46, 19)), GLYPH, RED)  # a faint glow: close to the ground, under SOLID_DE
        with_glow = bk.render_crisp(frame, base, True)
        without = bk.render_crisp(frame, base, False)
        near = (int(10.5 * bk.MASTER / N), int(17 * bk.MASTER / N))  # just outside the symbol, inside the glow
        self.assertGreater(with_glow.pixel(*near)[3], 0)
        self.assertEqual(without.pixel(*near)[3], 0)
        self.assertEqual(self.at(with_glow, 15, 16), (*RED, 255))  # the symbol is not blurred by the glow

    def test_the_outermost_frame_lines_are_never_the_symbol_unless_asked(self):
        base = plate()
        frame = paint(base, [(N - 1, 1), (N - 1, 2)], ORANGE)
        self.assertEqual(set(bk.render_crisp(frame, base, True).data[3::4]), {0})
        self.assertNotEqual(set(bk.render_crisp(frame, base, True, rim=True).data[3::4]), {0})


class BuildTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.frames = self.root / "frames"
        write_frames(self.frames)

    def tearDown(self):
        self.tmp.cleanup()

    def test_parts_of_a_button_and_the_lit_face_needs_the_plate_master(self):
        parts = bk.build_parts(self.frames, "crisp", None)
        names = set(parts)
        for button in bk.BUTTONS:
            for ident in (f"glyph.{button.name}", f"glyph.{button.name}_lit", f"plate.{button.name}_hover", f"plate.{button.name}_pressed"):
                self.assertIn(ident, names)
            self.assertNotIn(f"state.{button.on_name}", names)  # no plate master given
        self.assertIn("state.tmap_on_pressed", names)  # a lit art with a second frame has the pressed overlay
        self.assertNotIn("state.char_on_pressed", names)  # Character's lit art has one frame

    def test_lit_face_is_the_plate_and_the_lit_glyph_carries_the_symbol(self):
        plates = self.root / "plates"
        plates.mkdir()
        for button in bk.BUTTONS:
            pngio.write(plates / f"plate.{button.name}.png", bk.upscale_frame(plate()))
        parts = bk.build_parts(self.frames, "crisp", plates, {"log_but"})
        self.assertEqual(set(parts), {"glyph.log_but", "glyph.log_but_lit", "plate.log_but_hover", "plate.log_but_pressed", "state.log_on", "state.log_on_pressed"})
        self.assertEqual(bytes(parts["state.log_on"].data), bytes((plates / "plate.log_but.png").read_bytes() and pngio.read(plates / "plate.log_but.png").data))
        lit = parts["state.log_on"]
        self.assertEqual(lit.pixel(2, 2)[3], 255)  # a face is opaque
        glyph = parts["glyph.log_but_lit"]
        middle = glyph.pixel(int((16 + 2) * bk.MASTER / N), int(17 * bk.MASTER / N))
        self.assertGreater(middle[0], 180)  # the red symbol
        self.assertEqual(middle[3], 255)
        blurred = bk.build_parts(self.frames, "smooth", plates, {"log_but"})["glyph.log_but_lit"]
        self.assertGreater(blurred.pixel(int((16 + 2) * bk.MASTER / N), int(17 * bk.MASTER / N))[0], 180)  # the older looks still build

    def test_a_wrong_sized_plate_master_is_refused(self):
        plates = self.root / "plates"
        plates.mkdir()
        pngio.write(plates / "plate.char_but.png", pngio.Image(6, 6, bytearray(6 * 6 * 4)))
        with self.assertRaises(bk.ButtonError):
            bk.build_parts(self.frames, "cells", plates, {"char_but"})

    def test_a_missing_or_wrong_sized_frame_is_refused(self):
        with self.assertRaises(bk.ButtonError):
            bk.read_frame(self.frames, "no-such-art", 0)
        (self.frames / "odd").mkdir()
        pngio.write(self.frames / "odd" / "f0.png", pngio.Image(4, 4, bytearray(64)))
        with self.assertRaises(bk.ButtonError):
            bk.read_frame(self.frames, "odd", 0)

    def test_same_bytes_on_a_second_run(self):
        first = bk.build_parts(self.frames, "cells", None, {"invn_but"})
        second = bk.build_parts(self.frames, "cells", None, {"invn_but"})
        self.assertEqual({k: bytes(v.data) for k, v in first.items()}, {k: bytes(v.data) for k, v in second.items()})

    def test_owner_of_names_the_button(self):
        self.assertEqual(bk.owner_of("glyph.char_but").name, "char_but")
        self.assertEqual(bk.owner_of("glyph.log_but_lit").name, "log_but")
        self.assertEqual(bk.owner_of("state.wmap_on_pressed").name, "wmap_but")
        self.assertEqual(bk.owner_of("plate.invn_but_hover").name, "invn_but")
        with self.assertRaises(bk.ButtonError):
            bk.owner_of("glyph.key")

    def test_build_command_writes_masters_and_records_and_dry_run_writes_nothing(self):
        local = self.root / "local"
        out = self.root / "out"
        stdout = io.StringIO()
        with redirect_stdout(stdout), redirect_stderr(io.StringIO()):
            code = bk.main(["build", "--frames", str(self.frames), "--local-root", str(local), "--out", str(out), "--only", "char_but", "--dry-run"])
        self.assertEqual(code, 0)
        self.assertFalse(out.exists())
        with redirect_stdout(stdout), redirect_stderr(io.StringIO()):
            code = bk.main(["build", "--frames", str(self.frames), "--local-root", str(local), "--out", str(out), "--only", "char_but"])
        self.assertEqual(code, 0)
        self.assertTrue((out / "glyph.char_but.png").is_file())
        record = json.loads((out / "records" / "glyph.char_but.json").read_text())
        self.assertEqual(record["rule"], "button_kit")
        self.assertIn("169-Char_But/f0", record["sources"])
        self.assertEqual(record["image_size"], [bk.MASTER, bk.MASTER])

    def test_a_misspelt_button_is_refused_and_the_record_names_the_template_and_the_plate(self):
        local = self.root / "local"
        stdout = io.StringIO()
        with redirect_stdout(stdout):
            self.assertEqual(bk.main(["build", "--frames", str(self.frames), "--local-root", str(local), "--only", "char", "--dry-run"]), 2)
        plates = self.root / "plates"
        plates.mkdir()
        pngio.write(plates / "plate.char_but.png", bk.upscale_frame(plate()))
        out = self.root / "out"
        with redirect_stdout(io.StringIO()):
            code = bk.main(["build", "--frames", str(self.frames), "--local-root", str(local), "--out", str(out), "--plate-dir", str(plates), "--only", "char_but"])
        self.assertEqual(code, 0)
        record = json.loads((out / "records" / "state.char_on.json").read_text())
        self.assertIn("plate_master_sha256", record)
        self.assertEqual(len(record["template_sources"]), 5)

    def test_a_bad_input_exits_two(self):
        stdout = io.StringIO()
        with redirect_stdout(stdout):
            code = bk.main(["build", "--frames", str(self.root / "nope"), "--local-root", str(self.root / "local"), "--dry-run"])
        self.assertEqual(code, 2)
        self.assertIn("BUTTON KIT FAIL", stdout.getvalue())


if __name__ == "__main__":
    unittest.main()
