"""ARC-UI-3205: tests of the parchment button kit tool (oval boxes) on synthetic 80 x 43 frames (no art)."""
from __future__ import annotations

import io
import json
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path

from . import button_kit as bk
from . import parchment_kit as pk
from . import pngio
from . import prompt_library

W, H = pk.UNITS
PAPER = (156, 140, 115)
DARK = (47, 41, 41)
RED = (170, 40, 40)
SYMBOLS = {"drop_off": (14, 24, 16, 28), "use_box_off": (36, 48, 14, 30), "gamble_box_off": (54, 66, 18, 30)}   # x0, x1, y0, y1 of each box's symbol
GREEN, ILL_RED = (0, 115, 33), (156, 8, 0)


def noise(x: int, y: int) -> int:
    return ((x * 7 + y * 13 + x * y) % 5) - 2   # deterministic parchment grain


def frame(symbol=None, lift=1.0, corners=None) -> pngio.Image:
    """An 80 x 43 frame: dark outside a paper ellipse, grained parchment inside (times `lift`), `symbol` a box of RED, `corners` the colour of the cells outside the ring's ellipse."""
    image = pngio.Image(W, H, bytearray(W * H * 4))
    cx, cy, ax, ay = pk.CORNER_ELLIPSE
    for y in range(H):
        for x in range(W):
            if pk.in_paper(x, y):
                colour = tuple(max(0, min(255, int((c + noise(x, y)) * lift))) for c in PAPER)
            elif ((x - cx) / ax) ** 2 + ((y - cy) / ay) ** 2 > 0.97:
                colour = corners or DARK
            else:
                colour = (146, 108, 57)   # the ring
            image.data[(y * W + x) * 4:(y * W + x) * 4 + 4] = bytes((*colour, 255))
    if symbol:
        x0, x1, y0, y1 = symbol
        for y in range(y0, y1):
            for x in range(x0, x1):
                image.data[(y * W + x) * 4:(y * W + x) * 4 + 3] = bytes(RED)
    return image


def write_frames(root: Path) -> Path:
    frames = root / "frames"
    for box in pk.BOXES:
        for folder, img in ((box.folder, frame(SYMBOLS[box.name])), (box.on_folder, frame(SYMBOLS[box.name], 1.4, GREEN)), (box.ill_folder, frame(SYMBOLS[box.name], 1.0, ILL_RED))):
            (frames / folder).mkdir(parents=True)
            pngio.write(frames / folder / "f0.png", img)
    return frames


def write_plates(root: Path) -> Path:
    plates = root / "plates"
    plates.mkdir()
    for box in pk.BOXES:
        image = pngio.Image(*pk.MASTER, bytearray(bytes((120, 100, 80, 255)) * (pk.MASTER[0] * pk.MASTER[1])))
        pngio.write(plates / f"plate.{box.name}.png", image)
    return plates


def covered(mask: pk.Mask, box: tuple[int, int, int, int]) -> float:
    x0, x1, y0, y1 = box
    return sum(mask[y][x] for y in range(y0, y1) for x in range(x0, x1)) / ((x1 - x0) * (y1 - y0))


class MaskTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.idles = [frame(SYMBOLS[b.name]) for b in pk.BOXES]
        cls.masks = pk.symbol_masks(cls.idles)

    def test_the_masks_find_each_symbol_and_nothing_else(self) -> None:
        for box in pk.BOXES:
            mask = self.masks[box.name]
            self.assertGreater(covered(mask, SYMBOLS[box.name]), 0.9, box.name)
            outside = sum(v for row in mask for v in row) - sum(mask[y][x] for y in range(SYMBOLS[box.name][2], SYMBOLS[box.name][3]) for x in range(SYMBOLS[box.name][0], SYMBOLS[box.name][1]))
            self.assertLess(outside, 40, f"{box.name}: {outside} cells outside the symbol")

    def test_the_template_hides_the_symbol(self) -> None:
        box = pk.BOXES[0]
        template = pk.template_of(self.idles[0], self.masks[box.name])
        x0, x1, y0, y1 = SYMBOLS[box.name]
        for y in range(y0 + 2, y1 - 2):
            for x in range(x0 + 2, x1 - 2):
                self.assertLess(bk.distance(pk.cell(template, x, y), PAPER), 8.0)

    def test_a_hull_confines_a_mask(self) -> None:
        hull = ((34, 12), (50, 12), (50, 32), (34, 32))
        old = pk.MASKS["use_box_off"]["hull"]
        pk.MASKS["use_box_off"]["hull"] = hull
        try:
            mask = pk.symbol_masks(self.idles)["use_box_off"]
        finally:
            pk.MASKS["use_box_off"]["hull"] = old
        self.assertTrue(all(not v for y, row in enumerate(mask) for x, v in enumerate(row) if not pk.in_polygon(x + 0.5, y + 0.5, hull)))

    def test_holes_are_filled_and_specks_dropped(self) -> None:
        ring = pk.blank()
        for y in range(18, 26):
            for x in range(38, 48):
                ring[y][x] = not (20 <= y < 24 and 40 <= x < 46)
        self.assertTrue(pk.fill_holes(ring)[21][42])
        speck = pk.blank()
        speck[20][40] = True
        self.assertFalse(any(any(row) for row in pk.drop_small(speck, 3)))


class LayerTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.frame = frame(SYMBOLS["drop_off"])
        cls.mask = pk.blank()
        x0, x1, y0, y1 = SYMBOLS["drop_off"]
        for y in range(y0, y1):
            for x in range(x0, x1):
                cls.mask[y][x] = True
        cls.template = pk.template_of(cls.frame, cls.mask)

    def at(self, image: pngio.Image, ux: float, uy: float) -> tuple[int, int, int, int]:
        px, py = int(ux * image.w / W), int(uy * image.h / H)
        return tuple(image.data[(py * image.w + px) * 4:(py * image.w + px) * 4 + 4])

    def test_every_look_lays_the_symbol_over_the_plate_and_leaves_the_rest_clear(self) -> None:
        for look in pk.LOOKS:
            layer = pk.glyph_layer(self.frame, self.template, self.mask, look)
            self.assertEqual((layer.w, layer.h), pk.MASTER, look)
            inside = self.at(layer, 19.0, 22.0)
            self.assertEqual(inside[3], 255, look)
            self.assertEqual(self.at(layer, 10.0, 8.0)[3], 0, look)

    def test_the_cells_look_keeps_the_original_colour(self) -> None:
        layer = pk.glyph_layer(self.frame, self.template, self.mask, "cells")
        r, g, b, _ = self.at(layer, 19.0, 22.0)
        self.assertLess(bk.distance((r, g, b), RED), 3.0)

    def test_the_layer_over_its_base_gives_the_frames_symbol_back(self) -> None:
        plate = pngio.Image(*pk.MASTER, bytearray(bytes((*PAPER, 255)) * (pk.MASTER[0] * pk.MASTER[1])))
        over = bk.composite(plate, pk.glyph_layer(self.frame, self.template, self.mask, "cells"))
        self.assertLess(bk.distance(tuple(self.at(over, 19.0, 22.0)[:3]), RED), 3.0)
        self.assertLess(bk.distance(tuple(self.at(over, 10.0, 8.0)[:3]), PAPER), 1.0)

    def test_the_solid_look_is_one_colour_with_an_engraving(self) -> None:
        layer = pk.glyph_layer(self.frame, self.template, self.mask, "solid")
        middle = self.at(layer, 19.0, 22.0)
        light = self.at(layer, 14.2, 16.4)
        shade = self.at(layer, 23.8, 27.6)
        self.assertGreater(sum(light[:3]), sum(middle[:3]))
        self.assertLess(sum(shade[:3]), sum(middle[:3]))

    def test_an_empty_mask_cannot_be_filled_solid(self) -> None:
        with self.assertRaises(pk.ParchmentError):
            pk.render_solid(self.frame, pk.blank())

    def test_an_unknown_look_is_refused(self) -> None:
        with self.assertRaises(pk.ParchmentError):
            pk.glyph_layer(self.frame, self.template, self.mask, "blurry")


class StateTests(unittest.TestCase):
    def test_the_corner_colour_is_the_most_common_outside_the_ring(self) -> None:
        self.assertEqual(pk.state_colour(frame(None, 1.4, GREEN)), GREEN)
        self.assertEqual(pk.state_colour(frame(None, 1.0, ILL_RED)), ILL_RED)

    def test_the_wedge_lies_in_the_corners_only(self) -> None:
        wedge = pk.corner_cells(frame(None, 1.4, GREEN), GREEN)
        self.assertTrue(wedge[1][1] and wedge[H - 2][W - 2])
        self.assertFalse(wedge[H // 2][W // 2] or wedge[1][W // 2])

    def test_the_corner_layer_is_flat_colour_outside_the_ring_and_clear_inside(self) -> None:
        layer = pk.corner_layer(pk.corner_cells(frame(None, 1.4, GREEN), GREEN), GREEN)
        self.assertEqual(tuple(layer.data[(3 * layer.w + 3) * 4:(3 * layer.w + 3) * 4 + 4]), (*GREEN, 255))
        middle = (layer.h // 2) * layer.w + layer.w // 2
        self.assertEqual(layer.data[middle * 4 + 3], 0)

    def test_the_gain_field_lifts_by_the_ratio_and_leaves_kept_out_cells(self) -> None:
        idle = pngio.Image(W, H, bytearray(bytes((100, 100, 100, 255)) * (W * H)))
        lit = pngio.Image(W, H, bytearray(bytes((200, 150, 100, 255)) * (W * H)))
        planes = pk.gain_field(idle, lit, pk.blank(True))
        centre = (pk.MASTER[1] // 2) * pk.MASTER[0] + pk.MASTER[0] // 2
        self.assertAlmostEqual(planes[0][centre], 2.0, places=2)
        self.assertAlmostEqual(planes[2][centre], 1.0, places=2)
        kept_out = pk.gain_field(idle, lit, pk.blank(False))
        self.assertAlmostEqual(kept_out[0][centre], 1.0, places=2)

    def test_the_gain_never_exceeds_its_limit(self) -> None:
        dark = pngio.Image(W, H, bytearray(bytes((2, 2, 2, 255)) * (W * H)))
        bright = pngio.Image(W, H, bytearray(bytes((250, 250, 250, 255)) * (W * H)))
        centre = (pk.MASTER[1] // 2) * pk.MASTER[0] + pk.MASTER[0] // 2
        self.assertLessEqual(pk.gain_field(dark, bright, pk.blank(True))[0][centre], pk.GAIN_MAX + 1e-6)

    def test_the_tone_gain_brings_the_field_to_the_target(self) -> None:
        plate = pngio.Image(200, 100, bytearray(bytes((200, 160, 100, 255)) * (200 * 100)))
        target = pngio.Image(80, 43, bytearray(bytes((150, 140, 120, 255)) * (80 * 43)))
        gain = pk.tone_gain(plate, target)
        self.assertAlmostEqual(gain[0], 0.75, places=3)
        toned = pk.apply_tone(plate, gain)
        self.assertEqual(tuple(toned.data[(50 * 200 + 100) * 4:(50 * 200 + 100) * 4 + 3]), (150, 140, 120))
        self.assertEqual(tuple(toned.data[0:3]), (200, 160, 100))   # the corner is untouched


class ToneCommandTests(unittest.TestCase):
    def test_tone_writes_the_toned_plate_and_a_derived_record_and_refuses_bad_inputs(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            frames = write_frames(root)
            source = root / "outbox" / "B-BUTTONS" / f"{pk.PROMPT}.png"
            source.parent.mkdir(parents=True)
            pngio.write(source, pngio.Image(200, 100, bytearray(bytes((200, 160, 100, 255)) * (200 * 100))))
            record = {"id": pk.PROMPT, "image": "x", "image_sha256": pk.sha256_file(source), "pixel_size": [200, 100]}
            generator = source.with_name(source.stem + ".provenance.json")
            generator.write_text(json.dumps(record), encoding="utf-8")
            out = root / "work" / "toned.png"
            argv = ["tone", "--frames", str(frames), "--local-root", str(root), "--source", str(source), "--out", str(out)]
            with redirect_stdout(io.StringIO()):
                self.assertEqual(pk.main(argv), 0)
            derived = json.loads(out.with_name("toned.provenance.json").read_text(encoding="utf-8"))
            self.assertEqual(derived["derived_from"]["image_sha256"], record["image_sha256"])
            self.assertEqual(derived["image_sha256"], pk.sha256_file(out))
            self.assertLess(pngio.read(out).data[(50 * 200 + 100) * 4], 200)   # the field was darkened towards the frames' parchment
            with redirect_stderr(io.StringIO()):
                self.assertEqual(pk.main(argv[:-1] + [str(source)]), 2)          # --out must not be the source
                generator.write_text("[1, 2]", encoding="utf-8")
                self.assertEqual(pk.main(argv), 2)                                  # not a generator record
                generator.write_text("{broken", encoding="utf-8")
                self.assertEqual(pk.main(argv), 2)                                  # not JSON
            junk = root / "frames" / pk.BOXES[0].folder / "f0.png"
            junk.write_bytes(b"junk")
            with redirect_stderr(io.StringIO()):
                self.assertEqual(pk.main(["build", "--frames", str(frames), "--local-root", str(root), "--dry-run"]), 2)   # a corrupt frame

    def test_a_black_plate_field_is_refused(self) -> None:
        plate = pngio.Image(200, 100, bytearray(bytes((0, 0, 0, 255)) * (200 * 100)))
        with self.assertRaises(pk.ParchmentError):
            pk.tone_gain(plate, pngio.Image(80, 43, bytearray(bytes((150, 140, 120, 255)) * (80 * 43))))


class BuildTests(unittest.TestCase):
    def test_build_makes_every_part_of_every_box(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            parts = pk.build_parts(write_frames(root), "cells", write_plates(root))
            self.assertEqual(len(parts), 12)
            for box in pk.BOXES:
                for ident in (f"glyph.{box.name}", f"glyph.{box.on_name}", f"state.{box.on_name}", f"state.{box.ill_name}"):
                    self.assertIn(ident, parts)
                    self.assertEqual((parts[ident].w, parts[ident].h), pk.MASTER)
                lit = parts[f"state.{box.on_name}"]
                self.assertGreater(sum(lit.data[0:3]), 0)
                idle_plate = pngio.read(Path(tmp) / "plates" / f"plate.{box.name}.png")
                middle = ((pk.MASTER[1] // 2) * pk.MASTER[0] + pk.MASTER[0] // 2) * 4
                self.assertGreater(lit.data[middle], idle_plate.data[middle])   # the lit face is lighter inside
                self.assertEqual(tuple(parts[f"state.{box.ill_name}"].data[(3 * pk.MASTER[0] + 3) * 4:(3 * pk.MASTER[0] + 3) * 4 + 3]), ILL_RED)

    def test_without_plates_only_the_glyphs_are_built(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            parts = pk.build_parts(write_frames(Path(tmp)), "smooth", None, {"drop_off"})
            self.assertEqual(sorted(parts), ["glyph.drop_off", "glyph.drop_on"])

    def test_main_build_writes_parts_and_records_and_dry_run_writes_nothing(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            frames, plates, out = write_frames(root), write_plates(root), root / "out"
            base = ["build", "--frames", str(frames), "--local-root", str(root), "--plate-dir", str(plates), "--out", str(out)]
            with redirect_stdout(io.StringIO()):
                self.assertEqual(pk.main(base + ["--dry-run"]), 0)
            self.assertFalse(out.exists())
            with redirect_stdout(io.StringIO()):
                self.assertEqual(pk.main(base + ["--only", "use_box_off"]), 0)
            self.assertEqual(len(list(out.glob("*.png"))), 4)
            record = json.loads((out / "records" / "state.use_box_on.json").read_text(encoding="utf-8"))
            self.assertEqual(record["rule"], "parchment_kit")
            self.assertIn("plate_master_sha256", record)
            self.assertIn(f"{pk.BOXES[1].on_folder}/f0", record["sources"])

    def test_an_unknown_box_or_missing_frame_is_refused(self) -> None:
        with tempfile.TemporaryDirectory() as tmp, redirect_stderr(io.StringIO()):
            root = Path(tmp)
            code = pk.main(["build", "--frames", str(write_frames(root)), "--local-root", str(root), "--only", "nobox", "--dry-run"])
            self.assertEqual(code, 2)
            self.assertEqual(pk.main(["build", "--frames", str(root / "missing"), "--local-root", str(root), "--dry-run"]), 2)

    def test_owner_of(self) -> None:
        self.assertEqual(pk.owner_of("state.drop_ill").art, 224)
        self.assertEqual(pk.owner_of("glyph.gamble_box_on").art, 345)
        with self.assertRaises(pk.ParchmentError):
            pk.owner_of("glyph.nothing")

    def test_self_test(self) -> None:
        with redirect_stdout(io.StringIO()):
            self.assertEqual(pk.self_test(), 0)


class ManifestTests(unittest.TestCase):
    def test_the_manifest_and_the_prompt_library_list_the_slice(self) -> None:
        kit = json.loads((pk.HERE / "kit.json").read_text(encoding="utf-8"))
        rows = {p["id"]: p for p in kit["parts"]}
        for box in pk.BOXES:
            self.assertEqual(rows[f"plate.{box.name}"]["status"], "master")
            self.assertEqual(rows[f"plate.{box.name}"]["provenance"], f"fit:plate.{box.name}")
            for ident in (f"glyph.{box.name}", f"glyph.{box.on_name}", f"state.{box.on_name}", f"state.{box.ill_name}"):
                self.assertEqual(rows[ident]["status"], "master", ident)
                self.assertEqual(rows[ident]["provenance"], f"rule:parchment_kit:{ident}")
        library = prompt_library.load()
        self.assertIn(pk.PROMPT, library["batches"]["B-BUTTONS"]["prompts"])
        entry = next(p for p in library["prompts"] if p["id"] == pk.PROMPT)
        self.assertEqual(tuple(entry["size"]), (pk.REFS_SIZE[0], pk.REFS_SIZE[1]))
        self.assertTrue((pk.HERE / "prompts" / entry["file"]).is_file())


if __name__ == "__main__":
    unittest.main()
