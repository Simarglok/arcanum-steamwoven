"""ARC-DEV-2972: tests of the kit assembly tool on synthetic pictures only (no original art)."""
from __future__ import annotations

import io
import json
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from fractions import Fraction
from pathlib import Path

from tools.steamwoven_ui import assemble, pngio
from tools.steamwoven_ui.pngio import Image


def solid(w: int, h: int, rgba) -> Image:
    return Image.blank(w, h, rgba)


def noisy(w: int, h: int, seed: int) -> Image:
    """An opaque picture whose every pixel differs, so a wrong copy cannot pass by accident."""
    image = Image.blank(w, h)
    for i in range(w * h):
        image.data[i * 4:i * 4 + 4] = bytes(((i * 37 + seed) % 256, (i * 91 + seed * 3) % 256, (i * 13 + seed * 7) % 256, 255))
    return image


def disc(w: int, h: int, hole: tuple[int, int] | None = None) -> Image:
    """A round silhouette (alpha 255 inside, 0 outside, colour grey) with an optional single-texel hole."""
    image = Image.blank(w, h)
    cx, cy, r = (w - 1) / 2, (h - 1) / 2, min(w, h) / 2 - 0.5
    for y in range(h):
        for x in range(w):
            if (x - cx) ** 2 + (y - cy) ** 2 <= r * r and (x, y) != hole:
                image.data[(y * w + x) * 4:(y * w + x) * 4 + 4] = bytes((128, 128, 128, 255))
    return image


class Workspace:
    """Writes pictures into a temporary folder and a layout beside them."""

    def __init__(self, case: unittest.TestCase) -> None:
        self.dir = tempfile.TemporaryDirectory()
        case.addCleanup(self.dir.cleanup)
        self.root = Path(self.dir.name)

    def png(self, name: str, image: Image) -> str:
        pngio.write(self.root / name, image)
        return name

    def layout(self, data: dict, name: str = "layout.json") -> Path:
        path = self.root / name
        path.write_text(json.dumps(data), encoding="utf-8")
        return path


def layout_dict(**extra) -> dict:
    data = {"kind": assemble.KIND, "version": 1, "target": {"art": "Test_But", "size": [10, 8], "frames": 1}, "scale": 4,
            "alpha": {"mode": "legacy", "legacy": "legacy.png"}, "base": {"image": "base.png"}}
    data.update(extra)
    return data


def hand_png(color: int, width: int, rows: list[bytes], filters: list[int], palette: bytes = b"", trns: bytes = b"") -> bytes:
    """A CRC-valid PNG written with the given filter on each row, so the decoder's filter paths are read from bytes it did not write."""
    import struct
    import zlib
    channels = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}[color]
    raw = bytearray()
    previous = bytes(width * channels)
    for row, kind in zip(rows, filters):
        raw.append(kind)
        for i, value in enumerate(row):
            left = row[i - channels] if i >= channels else 0
            up = previous[i]
            corner = previous[i - channels] if i >= channels else 0
            guess = (0, left, up, (left + up) >> 1, _paeth(left, up, corner))[kind]
            raw.append((value - guess) & 255)
        previous = row

    def chunk(name: bytes, body: bytes) -> bytes:
        return struct.pack(">I", len(body)) + name + body + struct.pack(">I", zlib.crc32(name + body) & 0xFFFFFFFF)

    return (pngio.SIGNATURE + chunk(b"IHDR", struct.pack(">IIBBBBB", width, len(rows), 8, color, 0, 0, 0))
            + (chunk(b"PLTE", palette) if palette else b"") + (chunk(b"tRNS", trns) if trns else b"")
            + chunk(b"IDAT", zlib.compress(bytes(raw))) + chunk(b"IEND", b""))


def _paeth(a: int, b: int, c: int) -> int:
    p = a + b - c
    pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
    return a if pa <= pb and pa <= pc else b if pb <= pc else c


class PngTests(unittest.TestCase):
    def test_every_filter_and_colour_type_decodes(self) -> None:
        rows = [bytes((i * 29 + j * 53 + 7) % 256 for j in range(12)) for i in range(5)]
        for kind in range(5):
            image = pngio.decode(hand_png(6, 3, rows, [kind] * 5))
            self.assertEqual(bytes(image.data), b"".join(rows), f"RGBA filter {kind}")
        rgb = [bytes((i * 31 + j * 17) % 256 for j in range(9)) for i in range(4)]
        out = pngio.decode(hand_png(2, 3, rgb, [0, 1, 2, 4]))
        self.assertEqual(out.pixel(1, 2), (*rgb[2][3:6], 255))
        self.assertFalse(out.source_alpha)
        grey_alpha = [bytes((i * 11 + j * 5) % 256 for j in range(6)) for i in range(3)]
        ga = pngio.decode(hand_png(4, 3, grey_alpha, [3, 4, 1]))
        self.assertEqual(ga.pixel(2, 1), (*(grey_alpha[1][4],) * 3, grey_alpha[1][5]))
        self.assertTrue(ga.source_alpha)

    def test_palette_and_colour_keys(self) -> None:
        pal = pngio.decode(hand_png(3, 3, [bytes((0, 1, 2))], [0], palette=bytes((9, 8, 7, 1, 2, 3, 4, 5, 6)), trns=bytes((0, 128))))
        self.assertEqual([pal.pixel(x, 0) for x in range(3)], [(9, 8, 7, 0), (1, 2, 3, 128), (4, 5, 6, 255)])
        grey = pngio.decode(hand_png(0, 3, [bytes((7, 8, 9))], [0], trns=bytes((0, 7))))
        self.assertEqual([grey.pixel(x, 0)[3] for x in range(3)], [0, 255, 255])
        self.assertTrue(grey.source_alpha)
        rgb = pngio.decode(hand_png(2, 2, [bytes((1, 2, 3, 4, 5, 6))], [0], trns=bytes((0, 1, 0, 2, 0, 3))))
        self.assertEqual([rgb.pixel(x, 0)[3] for x in range(2)], [0, 255])
        self.assertFalse(pngio.decode(hand_png(0, 2, [bytes((1, 2))], [0])).source_alpha)

    def test_malformed_files_raise_png_error(self) -> None:
        good = hand_png(6, 1, [bytes(4)], [0])
        broken_header = good[:16] + good[17:]  # a 12 byte IHDR body
        for blob in (b"", b"x" * 40, good[:30], broken_header):
            with self.assertRaises(pngio.PngError):
                pngio.decode(blob)

    def test_round_trip_and_reproducible(self) -> None:
        image = noisy(7, 5, 1)
        image.data[3] = 17
        blob = pngio.encode(image)
        self.assertEqual(pngio.decode(blob), image)
        self.assertEqual(blob, pngio.encode(pngio.decode(blob)))

    def test_rejects_garbage(self) -> None:
        with self.assertRaises(pngio.PngError):
            pngio.decode(b"not a png")


class ArithmeticTests(unittest.TestCase):
    def test_round_half_up(self) -> None:
        self.assertEqual([assemble.round_half_up(Fraction(n, 2)) for n in (-3, -1, 1, 3, 5)], [-1, 0, 1, 2, 3])

    def test_parse_number_is_exact(self) -> None:
        self.assertEqual(assemble.parse_number(3.6, "x"), Fraction(18, 5))
        self.assertEqual(assemble.parse_number("18/5", "x"), Fraction(18, 5))
        for bad in (True, "abc", None, [1]):
            with self.assertRaises(assemble.LayoutError):
                assemble.parse_number(bad, "x")

    def test_units_map_to_whole_pixels(self) -> None:
        scale = Fraction(18, 5)
        self.assertEqual(assemble.units_to_px(Fraction(5), scale, 1, "exact", "x"), 18)
        with self.assertRaises(assemble.LayoutError):  # 7 units are 25.2 px
            assemble.units_to_px(Fraction(7), scale, 1, "exact", "x")
        self.assertEqual(assemble.units_to_px(Fraction(7), scale, 1, "snap", "x"), 25)
        self.assertEqual(assemble.units_to_px(Fraction(7), scale, 6, "snap", "x"), 24)  # the TRD's 6 px grid
        self.assertEqual(assemble.units_to_px(Fraction(5), scale, 6, "snap", "x"), 18)
        self.assertEqual(assemble.units_to_px(Fraction(1), Fraction(1, 2), 1, "snap", "x"), 1)  # halves go up


class PixelTests(unittest.TestCase):
    def test_blit_opaque_copies_and_translucent_blends(self) -> None:
        canvas = solid(4, 4, (0, 0, 200, 255))
        assemble.blit(canvas, solid(2, 2, (100, 100, 100, 255)), 1, 1)
        self.assertEqual(canvas.pixel(1, 1), (100, 100, 100, 255))
        self.assertEqual(canvas.pixel(0, 0), (0, 0, 200, 255))
        assemble.blit(canvas, solid(1, 1, (200, 0, 0, 128)), 0, 0)
        r, g, b, a = canvas.pixel(0, 0)
        self.assertEqual((a, r, g, b), (255, 100, 0, 100))
        empty = solid(2, 1, (0, 0, 0, 0))
        assemble.blit(empty, solid(1, 1, (50, 60, 70, 100)), 0, 0)
        self.assertEqual(empty.pixel(0, 0), (50, 60, 70, 100))

    def test_resample_keeps_flat_colour_and_alpha(self) -> None:
        flat = solid(9, 5, (10, 20, 30, 255))
        for w, h in ((9, 5), (4, 2), (12, 7), (1, 1)):
            out = assemble.resample(flat, w, h)
            self.assertEqual((out.w, out.h), (w, h))
            self.assertTrue(all(out.pixel(x, y) == (10, 20, 30, 255) for y in range(h) for x in range(w)))

    def test_resample_averages_premultiplied(self) -> None:
        pair = Image(2, 1, bytearray((255, 0, 0, 255, 0, 255, 0, 0)))  # an opaque red and a transparent green
        out = assemble.resample(pair, 1, 1)
        self.assertEqual(out.pixel(0, 0), (255, 0, 0, 128))  # the transparent pixel's colour does not bleed in


class AssembleTests(unittest.TestCase):
    def test_detail_placed_many_times_is_pixel_identical(self) -> None:
        """AC1: one socket picture placed five times is the same pixels at every place; the base under it does not show."""
        ws = Workspace(self)
        w, h, scale = 40, 20, 4
        gradient = Image.blank(w * scale, h * scale)
        for y in range(h * scale):
            for x in range(w * scale):
                gradient.data[(y * w * scale + x) * 4:(y * w * scale + x) * 4 + 4] = bytes((x % 256, y * 3 % 256, (x + y) % 256, 255))
        socket = noisy(12, 12, 5)
        ws.png("base.png", gradient)
        ws.png("socket.png", socket)
        ws.png("legacy.png", solid(w, h, (0, 0, 0, 255)))
        spots = [[0, 0], [7, 2], [13, 5], [20.5, 1], [28, 4]]
        data = layout_dict(target={"art": "Strip", "size": [w, h], "frames": 1}, details={"socket": {"image": "socket.png"}},
                           placements=[{"id": f"s{i}", "detail": "socket", "at": at} for i, at in enumerate(spots)])
        report = assemble.assemble(assemble.load_layout(ws.layout(data)))
        out = report.images[0]
        self.assertEqual(len(report.boxes), 5)
        for box in report.boxes:
            self.assertEqual((box.w, box.h), (12, 12))
            self.assertEqual(out.crop(box.x, box.y, box.w, box.h), socket)
        self.assertEqual([(b.x, b.y) for b in report.boxes], [(0, 0), (28, 8), (52, 20), (82, 4), (112, 16)])
        self.assertEqual(out.pixel(40, 70), gradient.pixel(40, 70))  # the base outside the details is untouched

    def test_master_scale_maps_units_and_resamples_to_the_pack_scale(self) -> None:
        ws = Workspace(self)
        # 10 x 8 units at 3.6 px per unit is a 36 x 29 canvas (28.8 rounds up); the output is 4 x the frame
        ws.png("base.png", solid(36, 29, (9, 9, 9, 255)))
        ws.png("dot.png", solid(6, 6, (200, 0, 0, 255)))
        ws.png("legacy.png", solid(10, 8, (0, 0, 0, 255)))
        data = layout_dict(master={"scale": "18/5", "snap": 6, "position": "snap"}, details={"dot": {"image": "dot.png"}},
                           placements=[{"detail": "dot", "at": [5, 5]}])
        report = assemble.assemble(assemble.load_layout(ws.layout(data)))
        self.assertEqual(report.canvas, (36, 29))
        self.assertEqual((report.boxes[0].x, report.boxes[0].y), (20, 20))  # 18 master px are 20 output px
        out = report.images[0]
        self.assertEqual((out.w, out.h), (40, 32))

    def test_multi_frame_button_takes_the_state_of_each_frame(self) -> None:
        """AC2: one output frame per original frame (idle 0, pressed 1, hover 2), states from the layout."""
        ws = Workspace(self)
        ws.png("idle.png", solid(40, 32, (100, 100, 100, 255)))
        ws.png("pressed.png", solid(40, 32, (60, 60, 60, 255)))
        ws.png("hover.png", solid(40, 32, (140, 140, 140, 255)))
        ws.png("glyph.png", solid(8, 8, (255, 255, 255, 255)))
        ws.png("glyph_pressed.png", solid(8, 8, (200, 200, 200, 255)))
        ws.png("legacy.png", solid(10, 8, (0, 0, 0, 255)))
        data = layout_dict(target={"art": "Btn", "size": [10, 8], "frames": 3},
                           frames=[{"state": "idle"}, {"state": "pressed"}, {"state": "hover"}],
                           base={"images": {"idle": "idle.png", "pressed": "pressed.png", "hover": "hover.png"}},
                           details={"glyph": {"images": {"default": "glyph.png", "pressed": "glyph_pressed.png"}},
                                    "spark": {"image": "glyph.png"}},
                           placements=[{"id": "g", "detail": "glyph", "at": [2, 2]},
                                       {"id": "spark", "detail": "spark", "at": [0, 0], "states": ["hover"]}])
        out = ws.root / "out"
        report = assemble.assemble(assemble.load_layout(ws.layout(data)), out)
        self.assertEqual([f.file for f in report.frames], ["Btn-f000.png", "Btn-f001.png", "Btn-f002.png"])
        frames = [pngio.read(out / name) for name in ("Btn-f000.png", "Btn-f001.png", "Btn-f002.png")]
        self.assertEqual([f.pixel(39, 31) for f in frames], [(100, 100, 100, 255), (60, 60, 60, 255), (140, 140, 140, 255)])
        self.assertEqual([f.pixel(10, 10) for f in frames], [(255,) * 4, (200, 200, 200, 255), (255,) * 4])  # the glyph follows the state
        self.assertEqual([f.pixel(0, 0) for f in frames], [(100, 100, 100, 255), (60, 60, 60, 255), (255,) * 4])  # the spark is hover only
        self.assertEqual([f.placed for f in report.frames], [["g"], ["g"], ["g", "spark"]])
        self.assertTrue((out / "Btn.assembly.json").is_file())

    def test_frame_numbers_select_placements(self) -> None:
        ws = Workspace(self)
        ws.png("base.png", solid(40, 32, (1, 2, 3, 255)))
        ws.png("d.png", solid(4, 4, (9, 9, 9, 255)))
        ws.png("legacy.png", solid(10, 8, (0, 0, 0, 255)))
        data = layout_dict(target={"art": "Two", "size": [10, 8], "frames": 2}, details={"d": {"image": "d.png"}},
                           placements=[{"detail": "d", "at": [1, 1], "frames": [1]}])
        report = assemble.assemble(assemble.load_layout(ws.layout(data)))
        self.assertEqual(report.images[0].pixel(5, 5), (1, 2, 3, 255))
        self.assertEqual(report.images[1].pixel(5, 5), (9, 9, 9, 255))

    def _plate(self, mode: str, hole: tuple[int, int] | None = (5, 4)) -> tuple[assemble.Report, Image]:
        ws = Workspace(self)
        legacy = disc(11, 11, hole)
        ws.png("legacy.png", legacy)
        ws.png("base.png", solid(44, 44, (80, 120, 160, 255)))  # the material covers the whole frame
        data = layout_dict(target={"art": "Plate", "size": [11, 11], "frames": 1}, alpha={"mode": mode, "legacy": "legacy.png"})
        return assemble.assemble(assemble.load_layout(ws.layout(data))), legacy

    def test_output_size_alpha_and_holes_match_the_original(self) -> None:
        """AC3: size is scale x the frame; every mode passes the pack's gate; the hole and the outside stay as the original's."""
        for mode in ("legacy", "legacySmooth", "masked"):
            report, legacy = self._plate(mode)
            out = report.images[0]
            self.assertEqual((out.w, out.h), (44, 44), mode)
            self.assertEqual(assemble.check_alpha(out, legacy, 4, mode), [], mode)
            self.assertEqual(out.pixel(0, 0)[3], 0, mode)  # the corner outside the plate
            if mode != "masked":
                self.assertEqual(out.pixel(5 * 4 + 2, 4 * 4 + 2)[3], 0, mode)  # the centre of the hole texel
                self.assertEqual(out.pixel(5 * 4 + 2, 6 * 4 + 2), (80, 120, 160, 255), mode)
        report, legacy = self._plate("legacy")
        self.assertTrue(all(report.images[0].data[i * 4 + 3] == legacy.data[(i // 44 // 4 * 11 + i % 44 // 4) * 4 + 3] for i in range(44 * 44)))
        smooth, _ = self._plate("legacySmooth")
        self.assertTrue(any(0 < smooth.images[0].data[i * 4 + 3] < 255 for i in range(44 * 44)))  # an anti-aliased edge

    def test_gate_refuses_a_broken_frame(self) -> None:
        report, legacy = self._plate("legacySmooth")
        frame = report.images[0]
        self.assertEqual(assemble.check_alpha(frame, legacy, 4, "legacySmooth"), [])
        frame.data[(33 * 44 + 21) * 4 + 3] = 0  # a blot in the middle of a uniform area
        self.assertTrue(assemble.check_alpha(frame, legacy, 4, "legacySmooth"))
        small = Image.blank(10, 10)
        self.assertIn("size", assemble.check_alpha(small, legacy, 4, "legacySmooth")[0])
        mask = disc(11, 11)
        far = assemble.resample(solid(11, 11, (1, 1, 1, 255)), 44, 44)
        self.assertTrue(assemble.check_alpha(far, mask, 4, "masked"))  # opaque far outside the silhouette

    def test_uncovered_silhouette_is_refused(self) -> None:
        ws = Workspace(self)
        ws.png("legacy.png", solid(10, 8, (0, 0, 0, 255)))
        ws.png("base.png", Image.blank(40, 32))  # a base that draws nothing
        with self.assertRaisesRegex(assemble.LayoutError, "nothing drawn under"):
            assemble.assemble(assemble.load_layout(ws.layout(layout_dict())))

    def test_wrong_sizes_and_placements_are_refused(self) -> None:
        ws = Workspace(self)
        ws.png("legacy.png", solid(10, 8, (0, 0, 0, 255)))
        ws.png("base.png", solid(40, 30, (1, 1, 1, 255)))
        with self.assertRaisesRegex(assemble.LayoutError, "canvas is 40x32"):
            assemble.assemble(assemble.load_layout(ws.layout(layout_dict())))
        ws.png("base.png", solid(40, 32, (1, 1, 1, 255)))
        ws.png("big.png", solid(8, 8, (1, 1, 1, 255)))
        data = layout_dict(details={"big": {"image": "big.png"}}, placements=[{"id": "p", "detail": "big", "at": [9, 7]}])
        with self.assertRaisesRegex(assemble.LayoutError, "leaves the 40x32 frame"):
            assemble.assemble(assemble.load_layout(ws.layout(data)))
        ws.png("legacy.png", solid(9, 8, (0, 0, 0, 255)))
        with self.assertRaisesRegex(assemble.LayoutError, "original frame is 10x8"):
            assemble.assemble(assemble.load_layout(ws.layout(layout_dict())))

    def test_scales_that_differ_keep_every_place_identical(self) -> None:
        """AC1 with a master scale of 3.6 and a pack scale of 4: the detail is brought to the pack scale once, so the five places match."""
        ws = Workspace(self)
        base = Image.blank(108, 36)  # 30 x 10 units at 3.6 px per unit
        for i in range(108 * 36):
            base.data[i * 4:i * 4 + 4] = bytes((i % 251, i * 7 % 253, i * 3 % 241, 255))
        detail = noisy(12, 12, 9)
        ws.png("base.png", base)
        ws.png("socket.png", detail)
        ws.png("legacy.png", solid(30, 10, (0, 0, 0, 255)))
        spots = ["0", "10/3", "20/3", "10", "40/3"]  # 0, 12, 24, 36, 48 master px: side by side on the 6 px grid, at different phases of the x4/3.6 step
        data = layout_dict(target={"art": "Row", "size": [30, 10], "frames": 1}, master={"scale": "18/5", "snap": 6},
                           details={"socket": {"image": "socket.png"}},
                           placements=[{"id": f"s{i}", "detail": "socket", "at": [at, "5/3"]} for i, at in enumerate(spots)])
        report = assemble.assemble(assemble.load_layout(ws.layout(data)))
        out = report.images[0]
        expected = assemble.resample(detail, 13, 13)  # 12 master px are 13.3 output px
        self.assertEqual([(b.x, b.w) for b in report.boxes], [(0, 13), (13, 13), (27, 13), (40, 13), (53, 13)])
        for box in report.boxes:
            self.assertEqual(out.crop(box.x, box.y, box.w, box.h), expected)

    def test_centre_anchor_and_per_frame_boxes(self) -> None:
        ws = Workspace(self)
        ws.png("base.png", solid(40, 32, (1, 2, 3, 255)))
        ws.png("d.png", solid(6, 6, (9, 9, 9, 255)))
        ws.png("legacy.png", solid(10, 8, (0, 0, 0, 255)))
        data = layout_dict(target={"art": "Two", "size": [10, 8], "frames": 2}, details={"d": {"image": "d.png"}},
                           placements=[{"id": "c", "detail": "d", "at": [5, 4], "anchor": "center", "frames": [1]}])
        report = assemble.assemble(assemble.load_layout(ws.layout(data)))
        self.assertEqual(report.frames[0].boxes, [])
        self.assertEqual((report.frames[1].boxes[0].x, report.frames[1].boxes[0].y), (17, 13))  # 20 - 3, 16 - 3
        self.assertEqual(report.to_json()["frames"][1]["placements"][0]["x"], 17)

    def test_legacy_picture_without_alpha_is_refused(self) -> None:
        ws = Workspace(self)
        ws.png("base.png", solid(40, 32, (1, 1, 1, 255)))
        (ws.root / "legacy.png").write_bytes(hand_png(2, 10, [bytes(30)] * 8, [0] * 8))
        with self.assertRaisesRegex(assemble.LayoutError, "no alpha channel"):
            assemble.assemble(assemble.load_layout(ws.layout(layout_dict())))

    def test_missing_state_image_is_named(self) -> None:
        ws = Workspace(self)
        ws.png("legacy.png", solid(10, 8, (0, 0, 0, 255)))
        ws.png("base.png", solid(40, 32, (1, 1, 1, 255)))
        data = layout_dict(frames=[{"state": "hover"}], base={"images": {"idle": "base.png"}})
        with self.assertRaisesRegex(assemble.LayoutError, "no image for state 'hover'"):
            assemble.assemble(assemble.load_layout(ws.layout(data)))


class LayoutTests(unittest.TestCase):
    def check(self, mutate, message: str) -> None:
        ws = Workspace(self)
        data = layout_dict(details={"d": {"image": "d.png"}}, placements=[{"detail": "d", "at": [0, 0]}])
        mutate(data)
        with self.assertRaisesRegex(assemble.LayoutError, message):
            assemble.load_layout(ws.layout(data))

    def test_validation(self) -> None:
        self.check(lambda d: d.update(kind="x"), "kind must be")
        self.check(lambda d: d.update(extra=1), "unknown field 'extra'")
        self.check(lambda d: d.update(scale=0), "scale")
        self.check(lambda d: d["target"].update(art="a/b"), "target.art")
        self.check(lambda d: d["target"].update(art="a\x00b"), "target.art")
        self.check(lambda d: d["target"].update(art=".."), "target.art")
        self.check(lambda d: d["target"].update(size=[4096, 4096]), "more than")
        self.check(lambda d: d.update(master={"scale": "1e9999999"}), "master.scale")
        self.check(lambda d: (d["target"].update(frames=3), d.update(frames=[{"state": "a"}])), r"frames: must list one entry for each of the 3")
        self.check(lambda d: d["alpha"].update(mode="soft"), "alpha.mode")
        self.check(lambda d: d["alpha"].update(legacy=["a", "b"]), "alpha.legacy")
        self.check(lambda d: d["placements"][0].update(detail="zz"), "not in details")
        self.check(lambda d: d["placements"][0].update(anchor="top"), "anchor")
        self.check(lambda d: d["placements"][0].update(at=[1]), "at")
        self.check(lambda d: d["placements"][0].update(frames=[5]), "frames")
        self.check(lambda d: d["base"].update(images={"a": "b.png"}), "exactly one of image and images")
        self.check(lambda d: d.update(master={"position": "x"}), "master.position")
        self.check(lambda d: d.update(placements=[{"id": "x", "detail": "d", "at": [0, 0]}] * 2), "unique")

    def test_not_json(self) -> None:
        ws = Workspace(self)
        path = ws.root / "bad.json"
        path.write_text("{", encoding="utf-8")
        with self.assertRaises(assemble.LayoutError):
            assemble.load_layout(path)


class CommandLineTests(unittest.TestCase):
    def run_main(self, args: list[str]) -> tuple[int, str, str]:
        out, err = io.StringIO(), io.StringIO()
        with redirect_stdout(out), redirect_stderr(err):
            code = assemble.main(args)
        return code, out.getvalue(), err.getvalue()

    def good(self) -> Workspace:
        ws = Workspace(self)
        ws.png("legacy.png", solid(10, 8, (0, 0, 0, 255)))
        ws.png("base.png", solid(40, 32, (1, 1, 1, 255)))
        ws.layout(layout_dict())
        return ws

    def test_dry_run_writes_nothing(self) -> None:
        ws = self.good()
        code, out, _ = self.run_main([str(ws.root / "layout.json"), "--dry-run"])
        self.assertEqual(code, 0)
        self.assertIn("ASSEMBLE DRY-RUN PASS", out)
        self.assertEqual(sorted(p.name for p in ws.root.iterdir()), ["base.png", "layout.json", "legacy.png"])

    def test_writes_frames_and_report(self) -> None:
        ws = self.good()
        code, out, _ = self.run_main([str(ws.root / "layout.json"), "--out", str(ws.root / "o"), "--json"])
        self.assertEqual(code, 0)
        self.assertEqual(json.loads(out)["frames"][0]["file"], "Test_But-f000.png")
        self.assertEqual(sorted(p.name for p in (ws.root / "o").iterdir()), ["Test_But-f000.png", "Test_But.assembly.json"])

    def test_failure_exits_1_and_writes_nothing(self) -> None:
        ws = self.good()
        ws.png("base.png", solid(40, 31, (1, 1, 1, 255)))
        code, _, err = self.run_main([str(ws.root / "layout.json"), "--out", str(ws.root / "o")])
        self.assertEqual(code, 1)
        self.assertIn("ASSEMBLE FAIL", err)
        self.assertFalse((ws.root / "o").exists())

    def test_needs_out_or_dry_run(self) -> None:
        with self.assertRaises(SystemExit):
            self.run_main(["x.json"])


if __name__ == "__main__":
    unittest.main()
