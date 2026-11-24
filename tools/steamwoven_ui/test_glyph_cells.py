"""ARC-UI-3058: tests of the glyph cells tool on synthetic frames (no art)."""
from __future__ import annotations

import io
import json
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path

from . import glyph_cells as gc
from . import pngio, tone_check

DOME = (30, 40, 20)
BRIGHT = (220, 150, 40)
DARK = (60, 40, 20)
CENTRE = (21.5, 18.5)  # the glyph's centre in units (not the frame's centre: the position is the original's)
RADIUS = 7.0


def frame(bright=BRIGHT, dark=DARK, phase=0, radius=RADIUS, centre=CENTRE, hole=0.0) -> pngio.Image:
    """A 39 x 39 frame: a uniform dome and a round glyph of the one-pixel checker (bright cells on the parity `phase`), with a round hole of `hole` units' radius (dome colour) in it."""
    image = pngio.Image(39, 39, bytearray(39 * 39 * 4))
    for y in range(39):
        for x in range(39):
            o = (y * 39 + x) * 4
            inside = (x + 0.5 - centre[0]) ** 2 + (y + 0.5 - centre[1]) ** 2 <= radius ** 2 and (x + 0.5 - centre[0]) ** 2 + (y + 0.5 - centre[1]) ** 2 > hole ** 2
            colour = (bright if (x + y) % 2 == phase else dark) if inside else DOME
            image.data[o:o + 4] = bytes((*colour, 255))
    return image


def alpha_at(image: pngio.Image, x: int, y: int) -> int:
    return image.data[(y * image.w + x) * 4 + 3]


def lightness(image: pngio.Image, x: int, y: int) -> float:
    o = (y * image.w + x) * 4
    return tone_check._pixel_lab(image.data[o], image.data[o + 1], image.data[o + 2])[0]


class GlyphCellsTest(unittest.TestCase):
    def test_mask_follows_the_glyph_and_fills_the_dark_cells(self) -> None:
        mask, _ = gc.glyph_mask(gc.lab_grid(frame()))
        cells = [i for i, on in enumerate(mask) if on]
        self.assertGreater(len(cells), 100)
        cx = sum(i % 39 + 0.5 for i in cells) / len(cells)
        cy = sum(i // 39 + 0.5 for i in cells) / len(cells)
        self.assertAlmostEqual(cx, CENTRE[0], delta=0.6)
        self.assertAlmostEqual(cy, CENTRE[1], delta=0.6)
        centre = int(CENTRE[1]) * 39 + int(CENTRE[0])
        self.assertTrue(mask[centre] and mask[centre + 1], "both checker tones are inside the glyph")
        self.assertFalse(mask[2 * 39 + 2], "the dome far from the glyph is not")

    def test_cells_are_the_originals_pixels_and_alternate(self) -> None:
        original = frame(phase=0)
        image, info = gc.render(original, "flat", 4)
        self.assertEqual((image.w, image.h), (156, 156))
        self.assertEqual(info["phase"], 0)
        # one cell is one flat colour; the next cell is the other tone, the bright one lighter; both lie on the original's grid
        ux, uy = 21, 18  # (21 + 18) % 2 == 1: a dark cell; (22, 18): a bright one
        dark = {lightness(image, ux * 4 + dx, uy * 4 + dy) for dx in range(4) for dy in range(4)}
        self.assertEqual(len(dark), 1)
        self.assertGreater(lightness(image, 22 * 4 + 1, uy * 4 + 1), lightness(image, ux * 4 + 1, uy * 4 + 1) + 20)
        self.assertEqual(alpha_at(image, 2, 2), 0)

    def test_silhouette_is_smooth_not_stairs(self) -> None:
        image, _ = gc.render(frame(), "flat", 4)
        edge = [alpha_at(image, x, 18 * 4 + 1) for x in range(0, 156)]
        self.assertTrue(any(0 < a < 255 for a in edge), "a soft edge")
        # an edge cell of the 39 x 39 mask does not give a straight step of 4 px: the alpha along a diagonal part of the outline takes many values
        diagonal = {alpha_at(image, x, y) for y in range(0, 156) for x in range(0, 156) if 0 < alpha_at(image, x, y) < 255}
        self.assertGreater(len(diagonal), 20)

    def test_bevel_changes_the_edge_only(self) -> None:
        original = frame()
        flat, _ = gc.render(original, "flat", 4)
        bevel, _ = gc.render(original, "bevel", 4)
        self.assertEqual(flat.data[(18 * 4 * 156 + 21 * 4) * 4:(18 * 4 * 156 + 21 * 4) * 4 + 4], bevel.data[(18 * 4 * 156 + 21 * 4) * 4:(18 * 4 * 156 + 21 * 4) * 4 + 4], "the middle is the same")
        differing = [(x, y) for y in range(156) for x in range(156) if flat.data[(y * 156 + x) * 4:(y * 156 + x) * 4 + 3] != bevel.data[(y * 156 + x) * 4:(y * 156 + x) * 4 + 3]]
        self.assertTrue(differing)
        # the upper-left edge lighter, the lower-right edge darker than flat
        left = min(x for x, y in differing if y == 18 * 4)
        right = max(x for x, y in differing if y == 18 * 4)
        self.assertGreater(lightness(bevel, left, 18 * 4), lightness(flat, left, 18 * 4))
        self.assertLess(lightness(bevel, right, 18 * 4), lightness(flat, right, 18 * 4))

    def test_mask_frame_gives_one_shape_in_every_state(self) -> None:
        lit = frame()
        dim = frame(bright=(50, 60, 25), dark=(34, 44, 22))  # an idle look: the checker is weak
        own, _ = gc.render(dim, "flat", 4, mask_frame=lit)
        shared, _ = gc.render(lit, "flat", 4)
        self.assertEqual([own.data[i] for i in range(3, len(own.data), 4)], [shared.data[i] for i in range(3, len(shared.data), 4)])
        self.assertLess(lightness(own, 22 * 4 + 1, 18 * 4 + 1), lightness(shared, 22 * 4 + 1, 18 * 4 + 1) - 20, "the tones are the frame's own")

    def test_phase_is_read_from_the_frame(self) -> None:
        for phase in (0, 1):
            _, info = gc.render(frame(phase=phase), "flat", 4)
            self.assertEqual(info["phase"], phase)

    def test_plate_canvas_has_a_soft_shadow_in_its_alpha(self) -> None:
        master = pngio.Image(138, 138, bytearray(138 * 138 * 4))
        for y in range(138):
            for x in range(138):
                if (x + 0.5 - 69) ** 2 + (y + 0.5 - 69) ** 2 <= 69 ** 2:
                    master.data[(y * 138 + x) * 4:(y * 138 + x) * 4 + 4] = bytes((120, 90, 40, 255))
        canvas = gc.plate_canvas(master, 4, 36.5, shadow=0.55)
        self.assertEqual((canvas.w, canvas.h), (156, 156))
        self.assertEqual(alpha_at(canvas, 78, 78), 255)
        outside = [alpha_at(canvas, x, 78) for x in range(0, 78)]
        self.assertTrue(any(0 < a < 255 for a in outside), "a shadow beyond the plate")
        self.assertEqual(alpha_at(canvas, 0, 0), 0)
        self.assertGreater(alpha_at(canvas, 78 + 75, 78), alpha_at(canvas, 78 - 75 - 1, 78), "the shadow falls to the lower right")
        self.assertLess(alpha_at(canvas, 155, 78), 8, "its tail is nearly gone at the frame's edge")

    def test_tone_fit_moves_the_cells_over_the_plate(self) -> None:
        original = frame()
        glyph, _ = gc.render(original, "flat", 4)
        plate = pngio.Image(156, 156, bytearray(bytes((20, 30, 15, 255)) * (156 * 156)))  # a darker dome than the original's
        fitted, report = gc.fit_tone(original, glyph, plate, None, 4)
        self.assertIn("applied_shift", report)
        self.assertEqual([glyph.data[i] for i in range(3, len(glyph.data), 4)], [fitted.data[i] for i in range(3, len(fitted.data), 4)])
        self.assertLess(lightness(fitted, 22 * 4 + 1, 18 * 4 + 1), lightness(glyph, 22 * 4 + 1, 18 * 4 + 1))

    def test_errors(self) -> None:
        with self.assertRaises(gc.CellsError):
            gc.lab_grid(pngio.Image(8, 8, bytearray(8 * 8 * 4)))
        with self.assertRaises(gc.CellsError):
            gc.render(frame(), "round")
        empty = pngio.Image(39, 39, bytearray(bytes((*DOME, 255)) * (39 * 39)))
        with self.assertRaises(gc.CellsError):
            gc.render(empty, "flat")

    def test_weak_checker_parts_join_by_colour_and_specks_are_dropped(self) -> None:
        image = frame()
        # a part with no checker (a flat brass bar through the dome's lower part) is glyph by its colour; a lone bright cell far away is a speck
        for x in range(12, 28):
            for y in (27, 28):
                image.data[(y * 39 + x) * 4:(y * 39 + x) * 4 + 3] = bytes((180, 160, 90))
        image.data[(5 * 39 + 30) * 4:(5 * 39 + 30) * 4 + 3] = bytes(BRIGHT)
        mask, _ = gc.glyph_mask(gc.lab_grid(image))
        self.assertTrue(mask[27 * 39 + 20] and mask[28 * 39 + 20], "the flat bar is glyph")
        self.assertFalse(mask[5 * 39 + 30], "a lone cell is a speck")

    def test_scale_two_and_bad_inputs_through_main(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            pngio.write(root / "f.png", frame())
            master = pngio.Image(138, 138, bytearray(bytes((120, 90, 40, 255)) * (138 * 138)))
            pngio.write(root / "m.png", master)
            with redirect_stdout(io.StringIO()):
                self.assertEqual(gc.main(["plate", str(root / "m.png"), str(root / "p.png"), "--scale", "2"]), 0)
                self.assertEqual(gc.main(["pressed", str(root / "p.png"), str(root / "q.png"), "--scale", "2"]), 0)
                self.assertEqual(gc.main(["cells", str(root / "f.png"), str(root / "h.png"), "--mask-frame", str(root / "f.png") + ":18.5", "--round-holes"]), 0)
                self.assertEqual(gc.main(["cells", str(root / "f.png"), str(root / "g.png"), "--scale", "2", "--plate", str(root / "p.png"), "--dome-frame", str(root / "f.png"), "--mask-frame", str(root / "f.png")]), 0)
            self.assertEqual(pngio.read(root / "g.png").w, 78)
            with redirect_stderr(io.StringIO()):
                self.assertEqual(gc.main(["cells", str(root / "f.png"), str(root / "x.png"), "--scale", "0"]), 2)
                self.assertEqual(gc.main(["plate", str(root / "m.png"), str(root / "x.png"), "--plate-units", "40"]), 2)

    def test_enclosed_hole_is_regularized_to_a_circle_and_a_small_eye_is_kept(self) -> None:
        ringed = frame(radius=9.0, hole=3.2)
        mask, _ = gc.glyph_mask(gc.lab_grid(ringed))
        holes = gc.enclosed_holes(mask)
        self.assertEqual(len(holes), 1)
        cx, cy, r = holes[0]
        self.assertAlmostEqual(cx, CENTRE[0], delta=0.6)
        self.assertAlmostEqual(cy, CENTRE[1], delta=0.6)
        self.assertAlmostEqual(r, 3.2, delta=0.8)
        plain, _ = gc.render(ringed, "flat", 4)
        round_, info = gc.render(ringed, "flat", 4, round_holes=True)
        self.assertEqual(len(info["round_holes"]), 1)
        # a true circle: the alpha is 0 at the same radius in every direction (left, right, up, down of the centre), and 255 just outside it
        px, py = CENTRE[0] * 4, CENTRE[1] * 4
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            inner = alpha_at(round_, int(px + dx * (r * 4 - 3)), int(py + dy * (r * 4 - 3)))
            outer = alpha_at(round_, int(px + dx * (r * 4 + 4)), int(py + dy * (r * 4 + 4)))
            self.assertEqual((inner, outer), (0, 255))
        self.assertEqual(len(plain.data), len(round_.data))
        # a small dark hole (the key's eye: darker than the dome) is kept, a one-cell speck of a bright glyph is filled
        eyed = frame(radius=8.0)
        for x, y in ((20, 18), (21, 18), (20, 19), (21, 19)):
            eyed.data[(y * 39 + x) * 4:(y * 39 + x) * 4 + 3] = bytes((5, 5, 5))
        speck = frame(radius=8.0)
        speck.data[(17 * 39 + 20) * 4:(17 * 39 + 20) * 4 + 3] = bytes(DOME)
        eye_mask, _ = gc.glyph_mask(gc.lab_grid(eyed))
        self.assertFalse(any(eye_mask[y * 39 + x] for x, y in ((20, 18), (21, 18), (20, 19), (21, 19))), "the eye stays a hole")
        speck_mask, _ = gc.glyph_mask(gc.lab_grid(speck))
        self.assertTrue(speck_mask[17 * 39 + 20], "a speck is filled")

    def test_union_of_masks_reaches_the_rim_only_with_the_wider_radius(self) -> None:
        far = frame(radius=3.0, centre=(34.0, 19.5))  # its tip lies beyond the dome's radius
        narrow, _ = gc.glyph_mask(gc.lab_grid(far), gc.DOME_RADIUS)
        wide, _ = gc.glyph_mask(gc.lab_grid(far), gc.PRESSED_RADIUS)
        self.assertLess(sum(narrow), sum(wide))
        image, info = gc.render(frame(centre=(34.0, 19.5), radius=3.0), "flat", 4, mask_frames=[(far, gc.DOME_RADIUS), (far, gc.PRESSED_RADIUS)])
        self.assertEqual(info["cells"], sum(wide))

    def test_pressed_plate_is_darker_with_an_inner_shadow_on_the_upper_left(self) -> None:
        idle = pngio.Image(156, 156, bytearray(bytes((200, 150, 100, 255)) * (156 * 156)))
        pressed = gc.pressed_plate(idle)
        self.assertEqual(lightness(pressed, 78, 78), lightness(idle, 78, 78), "the middle is the same")
        ul = lightness(pressed, 78 - 54, 78 - 54)  # about 19 units from the centre on the upper left (rim)
        lr = lightness(pressed, 78 + 54, 78 + 54)
        self.assertLess(ul, lightness(idle, 78 - 54, 78 - 54) * 0.5)
        self.assertLess(ul, lr)
        self.assertLess(lr, lightness(idle, 78 + 54, 78 + 54), "the whole rim a little darker")
        # the dome's upper-left ring darker than the lower-right one (the inner shadow)
        self.assertLess(lightness(pressed, 78 - 40, 78 - 40), lightness(pressed, 78 + 40, 78 + 40))
        with self.assertRaises(gc.CellsError):
            gc.pressed_plate(pngio.Image(10, 10, bytearray(400)))

    def test_plate_fills_the_socket_by_default_with_a_darker_edge(self) -> None:
        master = pngio.Image(138, 138, bytearray(138 * 138 * 4))
        for y in range(138):
            for x in range(138):
                if (x + 0.5 - 69) ** 2 + (y + 0.5 - 69) ** 2 <= 69 ** 2:
                    master.data[(y * 138 + x) * 4:(y * 138 + x) * 4 + 4] = bytes((200, 150, 100, 255))
        canvas = gc.plate_canvas(master, 4)
        self.assertEqual(alpha_at(canvas, 78, 3), 255)  # the plate reaches the box's edge (38.6 of 39 units)
        self.assertLess(lightness(canvas, 78, 4), lightness(canvas, 78, 40), "the last unit is a darker edge")
        self.assertEqual(alpha_at(canvas, 0, 0), 0)

    def test_cli(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            pngio.write(root / "f.png", frame())
            out = io.StringIO()
            with redirect_stdout(out):
                self.assertEqual(gc.main(["mask", str(root / "f.png"), "--json"]), 0)
            self.assertGreater(json.loads(out.getvalue())["cells"], 100)
            with redirect_stdout(io.StringIO()):
                self.assertEqual(gc.main(["cells", str(root / "f.png"), str(root / "g.png"), "--look", "bevel"]), 0)
            self.assertTrue((root / "g.png").exists())
            with redirect_stderr(io.StringIO()):
                self.assertEqual(gc.main(["cells", str(root / "missing.png"), str(root / "x.png")]), 2)


if __name__ == "__main__":
    unittest.main()
