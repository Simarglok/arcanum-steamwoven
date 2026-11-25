"""ARC-UI-3204: tests of the coloured-dome button kit tool (college tabs, skill-school buttons) on synthetic frames (no art)."""
from __future__ import annotations

import json
import math
import tempfile
import unittest
from pathlib import Path

from . import button_kit as bk
from . import kit_check
from . import pngio
from . import prompt_library
from . import school_kit as sk

N = 24
CENTRE = 12.0
DOME = (40, 90, 160)
SYMBOL = [(x, y) for y in range(8, 17) for x in range(11, 13)] + [(x, y) for y in range(11, 13) for x in range(8, 16)]  # a black cross


def frame(shade=1.0, symbol=True, lit_symbol=(2, 2, 2)) -> pngio.Image:
    """A disc of radius 11.6 in a 24 x 24 frame: a brass ring outside radius 10.4, a glossy dome inside it (lighter towards the upper left), a black cross."""
    image = pngio.Image(N, N, bytearray(N * N * 4))
    for y in range(N):
        for x in range(N):
            r = math.hypot(x + 0.5 - CENTRE, y + 0.5 - CENTRE)
            if r > 11.6:
                continue
            if r > 10.4:
                colour = (150, 120, 50)
            else:
                lift = 1.0 + 0.5 * (-(x + 0.5 - CENTRE) - (y + 0.5 - CENTRE)) / 20.0
                colour = tuple(max(0, min(255, int(c * lift * shade))) for c in DOME)
            image.data[(y * N + x) * 4:(y * N + x) * 4 + 4] = bytes((*colour, 255))
    if symbol:
        for x, y in SYMBOL:
            image.data[(y * N + x) * 4:(y * N + x) * 4 + 3] = bytes(lit_symbol)
    return image


class MaskTests(unittest.TestCase):
    def test_symbol_mask_finds_the_black_cross(self):
        idle, pressed = frame(), frame(shade=1.7)
        mask = sk.symbol_mask(idle, pressed)
        found = {i for i, on in enumerate(mask) if on}
        truth = {y * N + x for x, y in SYMBOL}
        self.assertGreaterEqual(len(found & truth) / len(truth), 0.9)
        self.assertLessEqual(len(found - truth) / len(truth), 0.2)

    def test_a_plain_dome_has_no_symbol(self):
        self.assertFalse(any(sk.symbol_mask(frame(symbol=False), frame(shade=1.7, symbol=False))))

    def test_override_adds_and_drops_cells(self):
        idle = frame()
        base = sk.symbol_mask(idle, None, {})
        x, y = next((i % N, i // N) for i, on in enumerate(base) if on)
        mask = sk.symbol_mask(idle, None, {"add": ((12, 12),), "drop": ((x, y),)})
        self.assertTrue(mask[12 * N + 12])
        self.assertTrue(base[y * N + x])
        self.assertFalse(mask[y * N + x])

    def test_override_cells_outside_the_frame_are_refused(self):
        for key in ("add", "drop"):
            for cell in ((-1, 3), (N, 3), (3, N)):
                with self.assertRaises(sk.SchoolError):
                    sk.symbol_mask(frame(), None, {key: (cell,)})

    def test_the_template_has_no_symbol_left(self):
        idle = frame()
        mask = [False] * (N * N)
        for x, y in SYMBOL:
            mask[y * N + x] = True
        template = sk.plate_template(idle, mask)
        plain = frame(symbol=False)
        worst = max(bk.distance(sk.colour_at(template, x, y), sk.colour_at(plain, x, y)) for x, y in SYMBOL)
        self.assertLess(worst, 8.0)
        self.assertEqual([sk.alpha_at(template, i % N, i // N) for i in range(N * N)], [sk.alpha_at(idle, i % N, i // N) for i in range(N * N)])


class ShapeAndRingTests(unittest.TestCase):
    def test_shape_cells_inside_the_shapes_only(self):
        w = h = 20
        rect = sk.shape_cells(("rect", 2.0, 3.0, 6.0, 5.0), w, h)
        self.assertEqual(sorted(rect), sorted(y * w + x for y in range(3, 5) for x in range(2, 6)))
        ellipse = sk.shape_cells(("ellipse", 10.0, 10.0, 3.0, 2.0), w, h)
        self.assertIn(10 * w + 10, ellipse)
        self.assertNotIn(10 * w + 14, ellipse)
        poly = sk.shape_cells(("poly", [(2.0, 2.0), (8.0, 2.0), (8.0, 8.0), (2.0, 8.0)]), w, h)
        self.assertEqual(len(poly), 36)
        line = sk.shape_cells(("line", 2.0, 10.0, 12.0, 10.0, 2.0), w, h)
        self.assertIn(9 * w + 7, line)
        self.assertNotIn(5 * w + 7, line)
        gear = sk.shape_cells(("gear", 10.0, 10.0, 3.0, 5.0, 8, 0.0), w, h)
        self.assertGreater(len(gear), math.pi * 9 * 0.9)
        self.assertLess(len(gear), math.pi * 25)
        with self.assertRaises(sk.SchoolError):
            sk.shape_cells(("star", 1.0), w, h)

    def test_override_shapes_and_roi_change_the_mask(self):
        idle = frame(symbol=False)
        shape = ("rect", 10.0, 10.0, 13.0, 13.0)
        added = sk.symbol_mask(idle, None, {"shapes": (shape,)})
        self.assertTrue(all(added[i] for i in sk.shape_cells(shape, N, N)))
        removed = sk.symbol_mask(frame(), None, {"shapes_drop": (("rect", 0.0, 0.0, float(N), float(N)),)})
        self.assertFalse(any(removed))
        roi = sk.symbol_mask(frame(), frame(shade=1.7), {"roi": ([(0.0, 0.0), (float(N), 0.0), (float(N), float(N)), (0.0, float(N))], 60.0)})
        self.assertTrue(roi[12 * N + 12])

    def test_the_coloured_body_of_a_symbol_joins_the_mask(self):
        idle = frame()
        pressed = frame(shade=1.7)
        for x, y in ((10, 9), (10, 10), (13, 9), (13, 10)):   # a body of another hue beside the black strokes
            pressed.data[(y * N + x) * 4:(y * N + x) * 4 + 3] = bytes((230, 120, 20))
        self.assertFalse(sk.symbol_mask(idle, pressed, {"fill": 0})[9 * N + 10])
        self.assertTrue(sk.symbol_mask(idle, pressed)[9 * N + 10])

    def test_the_ring_is_laid_over_the_plate_edge(self):
        plate = pngio.Image(40, 40, bytearray(b"\x20\x40\x80\xff" * 1600))
        ring = ([(100.0, 80.0, 40.0)] * sk.RING_BINS, [1.0] * sk.RING_ANGLES)
        out = sk.paint_ring(plate, (20.0, 20.0), 18.0, 4.0, ring)
        self.assertEqual(sk.colour_at(out, 20, 20), (32, 64, 128))              # the dome inside the ring stays
        self.assertEqual(sk.colour_at(out, 20, 2), (100, 80, 40))              # the ring itself
        self.assertEqual((sk.alpha_at(out, 20, 20), sk.alpha_at(out, 0, 0)), (255, 0))   # transparent outside the outer circle
        self.assertGreaterEqual(sk.alpha_at(out, 20, 2), 250)
        self.assertEqual(sk.alpha_at(out, 20, 1), 0)   # r = 19: outside the circle of radius 18

    def test_ring_profile_reads_the_hardware_ring(self):
        with tempfile.TemporaryDirectory() as folder:
            local = Path(folder)
            (local / "masters").mkdir()
            ring = pngio.Image(60, 60, bytearray(60 * 60 * 4))
            for y in range(60):
                for x in range(60):
                    r = math.hypot(x + 0.5 - 30, y + 0.5 - 30)
                    if 20 <= r <= 30:
                        ring.data[(y * 60 + x) * 4:(y * 60 + x) * 4 + 4] = bytes((100, 80, 40, 255))
            pngio.write(local / "masters" / f"{sk.RING_SOURCE}.png", ring)
            profile, gains = sk.ring_profile(local)
            self.assertEqual(len(profile), sk.RING_BINS)
            self.assertEqual(len(gains), sk.RING_ANGLES)
            self.assertTrue(all(abs(c - v) < 3 for c, v in zip(profile[len(profile) // 2], (100, 80, 40))))
            with self.assertRaises(sk.SchoolError):
                sk.ring_profile(Path(folder) / "nowhere")


class PartsTests(unittest.TestCase):
    def setUp(self):
        self.idle, self.pressed, self.hover = frame(), frame(shade=1.7), frame(shade=1.3)
        self.mask = sk.symbol_mask(self.idle, self.pressed)
        self.template = sk.plate_template(self.idle, self.mask)
        self.px = (84, 84)
        self.clip = sk.silhouette_clip(self.idle, self.px)

    def plate(self):
        """A stand-in plate master: the template, smoothly enlarged, inside the frame's outline."""
        out = pngio.Image(*self.px, bytearray(self.px[0] * self.px[1] * 4))
        for py in range(self.px[1]):
            for px in range(self.px[0]):
                x, y = min(N - 1, px * N // self.px[0]), min(N - 1, py * N // self.px[1])
                o = (py * self.px[0] + px) * 4
                out.data[o:o + 4] = bytes((*sk.colour_at(self.template, x, y), self.clip.data[o + 3]))
        return out

    def test_the_glyph_over_the_plate_gives_the_idle_symbol_back(self):
        glyph = sk.symbol_layer(self.idle, self.template, self.mask, self.px)
        face = sk.over(self.plate(), glyph)
        k = self.px[0] / N
        dark = [(x, y) for x, y in SYMBOL if self.mask[y * N + x]]
        sample = [(int((x + 0.5) * k), int((y + 0.5) * k)) for x, y in dark if abs(x - 11.5) < 1 and abs(y - 11.5) < 4]
        self.assertTrue(sample)
        for px, py in sample:
            self.assertLess(bk.distance(tuple(face.data[(py * self.px[0] + px) * 4:(py * self.px[0] + px) * 4 + 3]), (2, 2, 2)), 25.0)

    def test_the_glyph_leaves_the_dome_alone(self):
        glyph = sk.symbol_layer(self.idle, self.template, self.mask, self.px)
        corner = (4 * self.px[0] // N, 6 * self.px[1] // N)
        self.assertEqual(glyph.data[(corner[1] * self.px[0] + corner[0]) * 4 + 3], 0)

    def test_the_lit_overlay_lights_the_dome_and_gives_the_frame_back(self):
        plate = self.plate()
        glyph = sk.symbol_layer(self.idle, self.template, self.mask, self.px)
        face = sk.over(plate, glyph)
        lit = sk.over(face, sk.lit_overlay(self.hover, self.idle, self.mask, self.px, plate))
        k = self.px[0] / N
        errors = []
        for y in range(5, 19):
            for x in range(5, 19):
                if self.mask[y * N + x] or any(self.mask[j] for j in sk.neighbours8(y * N + x, N, N)):
                    continue
                px, py = int((x + 0.5) * k), int((y + 0.5) * k)
                o = (py * self.px[0] + px) * 4
                errors.append(bk.distance(tuple(lit.data[o:o + 3]), sk.colour_at(self.hover, x, y)))
        self.assertLess(sum(errors) / len(errors), 6.0)
        self.assertLess(max(errors), 20.0)

    def test_the_overlay_never_leaves_the_plate(self):
        overlay = sk.lit_overlay(self.pressed, self.idle, self.mask, self.px, self.plate())
        self.assertEqual(overlay.data[3], 0)  # the corner

    def test_lift_of_gives_the_colour_back(self):
        for base, lit in (((40, 90, 160), (90, 160, 230)), ((120, 10, 10), (230, 40, 40)), ((200, 200, 200), (120, 120, 140))):
            a, c = sk.lift_of(base, lit)
            self.assertGreaterEqual(a, sk.LIFT_MIN_ALPHA)
            self.assertLessEqual(a, 1.0)
            back = tuple(b + a * (cc - b) for b, cc in zip(base, c))
            self.assertLess(bk.distance(tuple(int(round(v)) for v in back), lit), 3.0)
            self.assertTrue(all(0.0 <= v <= 255.0 for v in c))

    def test_lift_of_the_same_colour_is_nothing(self):
        self.assertEqual(sk.lift_of((10, 20, 30), (11, 20, 30))[0], 0.0)

    def test_the_gloss_lies_on_the_upper_left_only(self):
        idle = frame(symbol=False)
        for y in range(3, 8):
            for x in range(4, 9):
                idle.data[(y * N + x) * 4:(y * N + x) * 4 + 3] = bytes((250, 250, 250))
        layer = sk.gloss_layer(idle, (84, 84))
        self.assertGreater(layer.data[(int(6 * 3.5) * 84 + int(6 * 3.5)) * 4 + 3], 0)
        self.assertEqual(layer.data[(int(18 * 3.5) * 84 + int(18 * 3.5)) * 4 + 3], 0)


class DataTests(unittest.TestCase):
    def test_every_button_has_a_sized_plate_row(self):
        for tab in sk.TABS:
            w, h = sk.master_of(tab.name)
            self.assertEqual((w % 6, h % 6), (0, 0), tab.name)

    def test_names_and_arts_are_unique_and_the_overrides_name_buttons(self):
        self.assertEqual(len({t.name for t in sk.TABS}), len(sk.TABS))
        self.assertEqual(len({t.art for t in sk.TABS}), len(sk.TABS))
        self.assertLessEqual(set(sk.OVERRIDES), {t.name for t in sk.TABS})
        self.assertEqual((len(sk.tabs_of("college")), len(sk.tabs_of("school"))), (16, 12))
        with self.assertRaises(sk.SchoolError):
            sk.tabs_of("nothing")
        with self.assertRaises(sk.SchoolError):
            sk.tab_named("nothing")

    def test_the_collage_fits_its_prompt_and_its_cells_do_not_overlap(self):
        library = prompt_library.load()
        for group, prompt in sk.GROUPS.items():
            entry = next(p for p in library["prompts"] if p["id"] == prompt)
            self.assertEqual(list(sk.collage_size(group)), entry["size"])
            cells = sk.collage_cells(group)
            self.assertEqual(len(cells), len(sk.tabs_of(group)))
            for cell in cells:
                x, y, w, h = cell["rect"]
                self.assertLessEqual(x + w, sk.collage_size(group)[0])
                self.assertLessEqual(y + h, sk.collage_size(group)[1])
            widest = max(sk.disc_of(frame())[2] for _ in (0,)) * 2 * sk.COLLAGE_SCALE[group]
            self.assertLess(widest, min(sk.COLLAGE_PITCH[group]))

    def test_locate_finds_each_dome_of_a_collage(self):
        group = "college"
        cells = sk.collage_cells(group)[:4]
        layout = {"canvas": list(sk.collage_size(group)), "cells": cells}
        w, h = sk.collage_size(group)
        image = pngio.Image(w, h, bytearray(bytes((*sk.GROUND, 255)) * (w * h)))
        for cell in cells:
            cx, cy = cell["centre"]
            for y in range(int(cy) - 100, int(cy) + 100):
                for x in range(int(cx) - 100, int(cx) + 100):
                    if math.hypot(x - cx, y - cy) <= 100:
                        image.data[(y * w + x) * 4:(y * w + x) * 4 + 3] = bytes((150, 60, 60))
        found = sk.locate(image, layout)
        for cell in cells:
            fx, fy, fr = found[cell["name"]]
            self.assertAlmostEqual(fx, cell["centre"][0], delta=3)
            self.assertAlmostEqual(fy, cell["centre"][1], delta=3)
            self.assertAlmostEqual(fr, 100, delta=3)

    def test_locate_refuses_a_square_piece_that_is_no_circle(self):
        cells = sk.collage_cells("college")[:1]
        layout = {"canvas": list(sk.collage_size("college")), "cells": cells}
        w, h = sk.collage_size("college")
        image = pngio.Image(w, h, bytearray(bytes((*sk.GROUND, 255)) * (w * h)))
        cx, cy = cells[0]["centre"]
        for y in range(int(cy) - 40, int(cy) + 40):
            for x in range(int(cx) - 110, int(cx) + 110):
                image.data[(y * w + x) * 4:(y * w + x) * 4 + 3] = bytes((150, 60, 60))
        with self.assertRaises(sk.SchoolError):
            sk.locate(image, layout)

    def test_the_manifest_rows_of_the_slice(self):
        manifest = json.loads(sk.MANIFEST.read_text(encoding="utf-8"))
        parts = {p["id"]: p for p in manifest["parts"]}
        for tab in sk.TABS:
            for ident in (f"plate.{tab.name}", f"glyph.{tab.name}", f"plate.{tab.name}_hover", f"plate.{tab.name}_pressed"):
                self.assertEqual(parts[ident]["status"], "master", ident)
                self.assertTrue(parts[ident]["provenance"], ident)
        self.assertEqual(parts["plate.s_air"]["provenance"], "fit:plate.s_air")
        self.assertTrue(parts["glyph.s_air"]["provenance"].startswith("rule:school_kit:"))

    def test_own_magenta_is_a_narrow_exemption(self):
        self.assertEqual(kit_check.OWN_MAGENTA, {"plate.s_phantasm_hover", "plate.s_phantasm_pressed"})

    def test_the_record_of_a_rule_part_names_its_sources(self):
        with tempfile.TemporaryDirectory() as tmp:
            frames = Path(tmp)
            tab = sk.tab_named("s_air")
            (frames / tab.folder).mkdir()
            for k in range(3):
                pngio.write(frames / tab.folder / f"f{k}.png", frame())
            path = frames / "x.png"
            image = frame()
            pngio.write(path, image)
            record = sk.record_of("glyph.s_air", frames, path, image)
        self.assertEqual(sorted(record["sources"]), [f"{tab.folder}/f{k}" for k in range(3)])
        self.assertEqual(record["rule"], "school_kit")


if __name__ == "__main__":
    unittest.main()
