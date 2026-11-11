"""Synthetic-fixture tests of tools/hd_font (ARC-DEV-355).  Every font here is drawn by the test: no game byte, no system font.

Run inside the locked venv:  python3 -B tools/hd_font/hd_font.py self-test
(a missing numpy/scipy/Pillow/potracer is a failed run, never a skip)."""

from __future__ import annotations

import hashlib
import io
import json
import os
import re
import struct
import sys
import tempfile
import unittest
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parents[0] / "research"))

import artfont  # noqa: E402
import dat_probe  # noqa: E402
import hd_font  # noqa: E402
import outline  # noqa: E402

# main() re-executes itself inside the locked venv unless this is set; the tests only ever run inside it (self-test).
os.environ.setdefault(hd_font.IN_VENV, "1")


# --- synthetic data ----------------------------------------------------------------------------------------------------

def rle_encode(indices: bytes) -> bytes:
    """Literal runs only (control 0x80 | count), plus repeat runs for any stretch of 4+ equal bytes."""
    out = bytearray()
    position = 0
    literal = bytearray()

    def flush() -> None:
        while literal:
            chunk = literal[:127]
            del literal[:127]
            out.append(0x80 | len(chunk))
            out.extend(chunk)

    while position < len(indices):
        run = 1
        while position + run < len(indices) and indices[position + run] == indices[position] and run < 127:
            run += 1
        if run >= 4:
            flush()
            out.append(run)
            out.append(indices[position])
        else:
            literal.extend(indices[position:position + run])
        position += run
    flush()
    return bytes(out)


def make_art(frames: list[tuple[int, int, bytes, int]], raw_frames: frozenset[int] = frozenset(), tail: bytes = b"",
             coloured_index: int | None = None) -> bytes:
    """frames: (width, height, indices, centre_x).  Palette slot 0 is a grey ramp (index i has B=G=R=i); `coloured_index` makes
    that entry red-only.  Header word 0 = 1 (one group)."""
    header = [0] * 33
    header[0] = 1
    header[3] = 1
    header[8] = len(frames)
    palette = bytearray()
    for index in range(256):
        if index == coloured_index:
            palette += bytes((0, 0, index, 0))
        else:
            palette += bytes((index, index, index, 0))
    tables = bytearray()
    payloads = bytearray()
    for number, (width, height, indices, centre_x) in enumerate(frames):
        assert len(indices) == width * height
        payload = bytes(indices) if number in raw_frames else rle_encode(bytes(indices))
        if number in raw_frames:
            assert len(payload) == width * height
        tables += struct.pack("<IIIiiii", width, height, len(payload), centre_x, 0, 0, 0)
        payloads += payload
    return struct.pack("<33I", *header) + bytes(palette) + bytes(tables) + bytes(payloads) + tail


def levels_to_index(coverage: np.ndarray) -> bytes:
    """A 1x coverage array to 16-level red values (0, 17, ..., 255), as a game font's mask."""
    return bytes((np.rint(np.clip(coverage, 0, 1) * 15).astype(np.uint8) * 17).ravel().tolist())


def draw_mask(width: int, height: int, painter) -> np.ndarray:
    """Draws with Pillow at 16x and box-reduces to the 1x coverage in [0, 1]."""
    image = Image.new("L", (width * 16, height * 16), 0)
    painter(ImageDraw.Draw(image), 16)
    big = np.asarray(image, dtype=np.float64) / 255.0
    return big.reshape(height, 16, width, 16).mean(axis=(1, 3))


def disc(draw, s):
    draw.ellipse([2.0 * s, 2.0 * s, 12.0 * s, 12.0 * s], fill=255)


def ring(draw, s):
    draw.ellipse([1.0 * s, 1.0 * s, 13.0 * s, 13.0 * s], fill=255)
    draw.ellipse([4.5 * s, 4.5 * s, 9.5 * s, 9.5 * s], fill=0)


def thin_bar(draw, s):
    draw.rectangle([1.0 * s, 5.6 * s, 11.0 * s, 6.4 * s], fill=255)  # 0.8 px tall


def dot(draw, s):
    draw.ellipse([5.1 * s, 5.1 * s, 6.9 * s, 6.9 * s], fill=255)  # about 1.8 px wide


def aligned_rect(draw, s):
    draw.rectangle([3 * s, 2 * s, 10 * s - 1, 11 * s - 1], fill=255)


def synthetic_font() -> tuple[bytes, dict[str, np.ndarray]]:
    masks = {"disc": draw_mask(14, 14, disc), "ring": draw_mask(14, 14, ring), "bar": draw_mask(12, 12, thin_bar),
             "dot": draw_mask(12, 12, dot), "rect": draw_mask(13, 13, aligned_rect)}
    frames = [(14, 14, levels_to_index(masks["disc"]), 15), (14, 14, levels_to_index(masks["ring"]), 15),
              (12, 12, levels_to_index(masks["bar"]), 13), (12, 12, levels_to_index(masks["dot"]), 13),
              (13, 13, levels_to_index(masks["rect"]), 14), (3, 2, bytes(6), 4)]
    return make_art(frames), masks


def make_dat(entries: dict[str, bytes]) -> bytes:
    return dat_probe.make_archive([(name.encode("ascii"), dat_probe.PLAIN, len(data), data) for name, data in entries.items()])


def mes(*items: tuple[int, str]) -> bytes:
    return "".join(f"{{{number}}}{{{text}}}\r\n" for number, text in items).encode("latin-1")


def build_game(root: Path, art: bytes) -> Path:
    game = root / "game"
    (game / "modules").mkdir(parents=True)
    (game / "arcanum1.dat").write_bytes(make_dat({"art\\interface\\interface.mes": mes((7, "TestFont.ART")),
                                                  "art\\interface\\testfont.art": art}))
    return game


# --- tests -------------------------------------------------------------------------------------------------------------

class ArtDecodeTests(unittest.TestCase):
    def test_rle_and_raw_frames_decode_to_red_coverage_with_index_zero_clear(self) -> None:
        indices = bytes([0, 0, 0, 0, 17, 34, 255, 0, 85, 85, 85, 85])
        data = make_art([(4, 3, indices, 6), (2, 2, bytes([0, 16, 32, 255]), 3)], raw_frames=frozenset({1}))
        frames = artfont.decode_font_art(data)
        self.assertEqual(len(frames), 2)
        self.assertEqual((frames[0].width, frames[0].height, frames[0].centre_x), (4, 3, 6))
        self.assertEqual(list(frames[0].red), list(indices))  # grey ramp: red == index, index 0 stays 0
        self.assertEqual(list(frames[1].red), [0, 16, 32, 255])
        self.assertTrue(all(frame.grey for frame in frames))

    def test_a_coloured_palette_entry_is_not_grey(self) -> None:
        frame = artfont.decode_font_art(make_art([(2, 1, bytes([0, 200]), 2)], coloured_index=200))[0]
        self.assertFalse(frame.grey)
        self.assertEqual(frame.red[1], 200)

    def test_a_tail_after_the_last_frame_is_allowed(self) -> None:
        data = make_art([(2, 1, bytes([5, 6]), 2)], raw_frames=frozenset({0}), tail=b"\x00" * 5)
        self.assertEqual(len(artfont.decode_font_art(data)), 1)

    def test_malformed_files_are_refused(self) -> None:
        good = make_art([(4, 1, bytes([1, 2, 3, 4]), 4)])
        with self.assertRaises(artfont.FontError):
            artfont.decode_font_art(good[:100])  # shorter than the header
        with self.assertRaises(artfont.FontError):
            artfont.decode_font_art(good[:-1])  # payload past the end
        with self.assertRaises(artfont.FontError):
            artfont.decode_font_art(make_art([(4, 2, bytes(8), 4)])[:-1])
        words = list(struct.unpack_from("<33I", good))
        words[3] = 0
        with self.assertRaises(artfont.FontError):
            artfont.decode_font_art(struct.pack("<33I", *words) + good[132:])  # no palette slot 0
        with self.assertRaises(artfont.FontError):
            artfont.decode_rle(bytes([0x00, 1]), 1, "zero run")
        with self.assertRaises(artfont.FontError):
            artfont.decode_rle(bytes([0x85, 1, 2]), 5, "truncated literal")
        with self.assertRaises(artfont.FontError):
            artfont.decode_rle(bytes([0x05, 9]), 4, "overrun")
        with self.assertRaises(artfont.FontError):
            artfont.decode_rle(bytes([0x03, 9]), 4, "short")


class ContentTests(unittest.TestCase):
    def test_mes_grammar_first_message_wins_and_comments_are_skipped(self) -> None:
        self.assertEqual(artfont.parse_mes(b"// note\r\n{1}{A.art}\r\n{2}{ B.art }\r\n{1}{Other.art}\r\n"), {1: "A.art", 2: "B.art"})

    def test_search_order_highest_priority_first(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            game = Path(temp) / "game"
            (game / "modules" / "Mod").mkdir(parents=True)
            (game / "data").mkdir()
            name = "art\\interface\\x.art"
            (game / "arcanum1.dat").write_bytes(make_dat({name: b"one"}))
            (game / "ARCANUM2.DAT").write_bytes(make_dat({name: b"two"}))
            (game / "modules" / "Mod.dat").write_bytes(make_dat({name: b"module"}))
            (game / "modules" / "Mod.patch0").write_bytes(make_dat({name: b"patch0"}))
            (game / "modules" / "Mod.patch3").write_bytes(make_dat({"other": b"p3"}))
            sources = artfont.discover_sources(game, "Mod")
            self.assertEqual([source.label for source in sources],
                             ["module folder Mod", "Mod patch3", "Mod patch0", "module Mod", "data folder", "arcanum2.dat", "arcanum1.dat", "game folder"])
            content = artfont.Content(sources)
            self.assertEqual(content.find("ART/Interface/X.ART"), (b"patch0", "Mod patch0"))
            (game / "modules" / "Mod" / "art" / "interface").mkdir(parents=True)
            (game / "modules" / "Mod" / "art" / "interface" / "X.ART").write_bytes(b"loose")
            self.assertEqual(artfont.Content(artfont.discover_sources(game, "Mod")).find("art/interface/x.art"), (b"loose", "module folder Mod"))
            (game / "modules" / "Mod.patch0").unlink()
            (game / "modules" / "Mod" / "art" / "interface" / "X.ART").unlink()
            self.assertEqual(artfont.Content(artfont.discover_sources(game, "Mod")).find(name), (b"module", "module Mod"))
            (game / "modules" / "Mod.dat").unlink()  # patches and the module need the module archive; the base archives remain
            self.assertEqual(artfont.Content(artfont.discover_sources(game, "Mod")).find(name), (b"two", "arcanum2.dat"))
            self.assertIsNone(artfont.Content(artfont.discover_sources(game, "Mod")).find("art/interface/absent.art"))

    def test_load_font_resolves_the_number_through_interface_mes(self) -> None:
        art, _masks = synthetic_font()
        with tempfile.TemporaryDirectory() as temp:
            game = build_game(Path(temp), art)
            content = artfont.Content(artfont.discover_sources(game, "Arcanum"))
            font = artfont.load_font(content, 7)
            self.assertEqual((font.number, font.file, font.source, len(font.frames)), (7, "TestFont.ART", "arcanum1.dat", 6))
            with self.assertRaises(artfont.FontError):
                artfont.load_font(content, 8)

    def test_a_capitalised_modules_folder_is_found_and_a_path_like_name_is_refused(self) -> None:
        art, _ = synthetic_font()
        with tempfile.TemporaryDirectory() as temp:
            game = build_game(Path(temp), art)
            (game / "modules").rename(game / "Modules")
            (game / "Modules" / "Arcanum.dat").write_bytes(make_dat({"art\\interface\\interface.mes": mes((7, "TestFont.ART"), (8, "../secret.art"))}))
            sources = artfont.discover_sources(game, "Arcanum")
            self.assertIn("module Arcanum", [source.label for source in sources])
            content = artfont.Content(sources)
            self.assertEqual(artfont.load_font(content, 7).file, "TestFont.ART")
            with self.assertRaises(artfont.FontError):
                artfont.load_font(content, 8)

    def test_a_missing_game_folder_is_a_typed_failure(self) -> None:
        with self.assertRaises(artfont.FontError):
            artfont.discover_sources(Path("/nonexistent/arcanum-hd-font-test"))


class OutlineTests(unittest.TestCase):
    def trace(self, mask: np.ndarray) -> outline.GlyphTrace:
        return outline.trace_mask(np.rint(np.clip(mask, 0, 1) * 15) * 17 / 255.0)

    def test_shapes_trace_with_conserved_ink_and_a_small_round_trip_error(self) -> None:
        for name, mask in synthetic_font_masks().items():
            with self.subTest(shape=name):
                result = self.trace(mask)
                self.assertEqual(result.coverage.shape, (mask.shape[0] * 4, mask.shape[1] * 4))
                self.assertFalse(result.vanished)
                self.assertGreater(result.nodes, 0)
                self.assertLessEqual(result.level, outline.LEVEL_CAP)
                ratio = result.traced_mass / result.mass
                # A stroke under one pixel is quantized to whole 8x rows before the curve fit (docs/research/font-vectorization.md,
                # section 5: one-pixel strokes recover poorly), so the bar gets a wider bound than the solid shapes.  The level cap
                # 0.42 (ARC-UI-145, the owner's choice of a tighter edge) takes the binarization below the mass-preserving level
                # wherever that is above 0.42, so the glyphs come out up to a third bolder in area (about 11 % over the player's
                # fonts, a local observation); the next test holds the cap-free (0.5) trace to the conserved-ink bounds.
                upper = 1.4 if name == "bar" else 1.3
                lower = 0.85 if name == "bar" else 0.92
                self.assertGreater(ratio, lower, f"{name} lost ink: {ratio}")
                self.assertLess(ratio, upper, f"{name} gained ink: {ratio}")
                # The cap's extra ink also raises the round-trip error of the thin shapes (0.14 and 0.16 here, 0.08 over the real fonts).
                self.assertLess(result.mean_abs_error, 0.2 if name in ("bar", "dot") else 0.1, f"{name} round trip error {result.mean_abs_error}")

    def test_a_cap_of_one_half_conserves_the_ink_as_before_ui_145(self) -> None:
        saved = outline.LEVEL_CAP
        outline.LEVEL_CAP = 0.5
        try:
            for name, mask in synthetic_font_masks().items():
                with self.subTest(shape=name):
                    result = self.trace(mask)
                    ratio = result.traced_mass / result.mass
                    self.assertLess(ratio, 1.2 if name == "bar" else 1.08, f"{name}: {ratio}")
                    self.assertGreater(ratio, 0.85 if name == "bar" else 0.92, f"{name}: {ratio}")
        finally:
            outline.LEVEL_CAP = saved

    def test_the_ring_keeps_its_hole_and_the_disc_is_filled(self) -> None:
        masks = synthetic_font_masks()
        ring_result = self.trace(masks["ring"]).coverage
        disc_result = self.trace(masks["disc"]).coverage
        centre = (ring_result.shape[0] // 2, ring_result.shape[1] // 2)
        self.assertEqual(ring_result[centre], 0.0)
        self.assertGreater(disc_result[centre], 0.99)
        self.assertGreater(ring_result[4 * 7, 4 * 2], 0.99)  # on the ring's left band (x = 2 px, y = 7 px)

    def test_a_thin_stroke_and_a_small_dot_do_not_vanish(self) -> None:
        masks = synthetic_font_masks()
        for name in ("bar", "dot"):
            with self.subTest(shape=name):
                result = self.trace(masks[name])
                self.assertFalse(result.vanished)
                self.assertGreater(result.traced_mass, 0.85 * result.mass)

    def test_a_pixel_aligned_rectangle_keeps_its_edges(self) -> None:
        result = self.trace(synthetic_font_masks()["rect"]).coverage
        self.assertGreater(result[4 * 6, 4 * 6], 0.99)  # inside
        self.assertEqual(result[4 * 6, 4 * 1], 0.0)  # left of the rectangle
        self.assertEqual(result[4 * 12, 4 * 6], 0.0)  # below it
        inside = result[4 * 2 + 1:4 * 11 - 1, 4 * 3 + 1:4 * 10 - 1]
        self.assertGreater(inside.min(), 0.9)

    def test_rasterizer_is_exact_for_aligned_squares_holes_and_triangles(self) -> None:
        def square(low: float, high: float) -> np.ndarray:
            return np.array([[low, low], [high, low], [high, high], [low, high]], dtype=float)

        area = lambda polygons, shape: outline.rasterize(polygons, shape, 4).sum() / 16  # 4x pixels -> 1x pixels
        self.assertEqual(area([square(1, 5)], (8, 8)), 16.0)  # no inclusive edge: not 16.5
        self.assertEqual(area([square(2, 3)], (6, 6)), 1.0)
        self.assertEqual(area([square(0, 6), square(2, 4)], (8, 8)), 32.0)  # even-odd: the inner square is a hole
        triangle = np.array([[1.3, 1.1], [6.7, 2.2], [3.1, 7.9]])
        x, y = triangle[:, 0], triangle[:, 1]
        exact = 0.5 * abs(np.dot(x, np.roll(y, -1)) - np.dot(y, np.roll(x, -1)))
        self.assertAlmostEqual(area([triangle], (10, 10)), exact, delta=0.02 * exact / 17)
        covered = outline.rasterize([square(1, 5)], (8, 8), 4)
        self.assertEqual(float(covered[4 * 5:, :].sum() + covered[:, 4 * 5:].sum()), 0.0)  # nothing past the right and bottom edges
        self.assertEqual(float(covered[:4, :].sum() + covered[:, :4].sum()), 0.0)  # nor before the left and top edges

    def test_an_empty_mask_gives_an_empty_glyph(self) -> None:
        result = outline.trace_mask(np.zeros((5, 4)))
        self.assertEqual(result.coverage.shape, (20, 16))
        self.assertEqual((result.nodes, float(result.coverage.sum())), (0, 0.0))
        self.assertFalse(result.vanished)

    def test_tracing_is_deterministic(self) -> None:
        mask = synthetic_font_masks()["ring"]
        first = outline.png_bytes(outline.frame_pixels(self.trace(mask).coverage))
        second = outline.png_bytes(outline.frame_pixels(self.trace(mask).coverage))
        self.assertEqual(first, second)

    def test_glyph_frame_rule_rgb_equal_coverage_alpha_only_on_ink(self) -> None:
        pixels = outline.frame_pixels(self.trace(synthetic_font_masks()["disc"]).coverage)
        self.assertEqual(pixels.dtype, np.uint8)
        self.assertTrue((pixels[..., 0] == pixels[..., 1]).all() and (pixels[..., 1] == pixels[..., 2]).all())
        self.assertEqual(set(np.unique(pixels[..., 3]).tolist()), {0, 255})
        self.assertTrue(((pixels[..., 0] > 0) == (pixels[..., 3] == 255)).all())
        decoded = Image.open(io.BytesIO(outline.png_bytes(pixels))).convert("RGBA")
        self.assertEqual(np.asarray(decoded).tolist(), pixels.tolist())


_MASKS: dict[str, np.ndarray] = {}


def synthetic_font_masks() -> dict[str, np.ndarray]:
    if not _MASKS:
        _MASKS.update(synthetic_font()[1])
    return _MASKS


class CommandTests(unittest.TestCase):
    @staticmethod
    def checkout(temp: str) -> Path:
        """A fake checkout (it holds .git) with its ignored local/hd-font folder; returns the output folder."""
        base = Path(temp).resolve()
        (base / ".git").mkdir(exist_ok=True)
        return base / "local" / "hd-font" / "out"

    def run_trace(self, game: Path, out: Path, *extra: str) -> int:
        return hd_font.main(["trace", "--game-dir", str(game), "--module", "Arcanum", "--art", "7", "--out", str(out), *extra])

    def test_out_folder_must_be_under_local_and_never_in_a_pack(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp).resolve()
            good = self.checkout(temp)
            self.assertEqual(hd_font.checked_out_dir(str(good)), good)
            elsewhere = root / "elsewhere"
            elsewhere.mkdir()
            (root / "local" / "hd-font").mkdir(parents=True)
            (root / "local" / "res095").mkdir()
            (root / "local" / "link").symlink_to(elsewhere)
            (root / "local" / "hd-font" / "via-link").symlink_to(root / "local" / "res095")
            for bad in (root / "local" / "out", root / "local" / "hd-packs" / "out", root / "local" / "res095" / "pack-out" / "interface-redraw-4x",
                        root / "local" / "hd-font-x", root / "out", root / "docs" / "local" / "hd-font" / "out",
                        root / "local" / "link" / "hd-font", root / "local" / "hd-font" / "via-link", Path("/usr/local/hd-font/out"),
                        Path("/tmp/local/hd-font/out")):
                with self.subTest(out=str(bad)), self.assertRaises(SystemExit):
                    hd_font.checked_out_dir(str(bad))
            (root / ".git").rmdir()
            with self.assertRaises(SystemExit):  # not inside a checkout
                hd_font.checked_out_dir(str(good))

    def test_art_list_validation(self) -> None:
        self.assertEqual(hd_font.parse_arts("27, 229"), [27, 229])
        for bad in ("", "x", "1,1", "-3"):
            with self.subTest(arts=bad), self.assertRaises(SystemExit):
                hd_font.parse_arts(bad)

    def test_dry_run_writes_nothing(self) -> None:
        art, _ = synthetic_font()
        with tempfile.TemporaryDirectory() as temp:
            game = build_game(Path(temp), art)
            out = self.checkout(temp)
            self.assertEqual(self.run_trace(game, out, "--dry-run"), 0)
            self.assertFalse(out.exists())

    def test_trace_writes_4x_frames_metadata_and_a_receipt(self) -> None:
        art, masks = synthetic_font()
        with tempfile.TemporaryDirectory() as temp:
            game = build_game(Path(temp), art)
            out = self.checkout(temp)
            self.assertEqual(self.run_trace(game, out), 0)
            folder = out / "7-testfont"
            meta = json.loads((folder / "font.json").read_text())
            self.assertEqual((meta["scale"], meta["art"], meta["file"], meta["source"]), (4, 7, "TestFont.ART", "arcanum1.dat"))
            self.assertEqual(meta["artSha256"], hashlib.sha256(art).hexdigest())
            self.assertEqual(len(meta["frames"]), 6)
            self.assertEqual(sorted(path.name for path in folder.glob("frame-*.png")), [f"frame-{index:03d}.png" for index in range(6)])
            first = meta["frames"][0]
            self.assertEqual((first["width"], first["height"], first["centreX"], first["character"], first["ink"]), (14, 14, 15, 31, True))
            for entry in meta["frames"]:
                image = Image.open(folder / entry["file"])
                self.assertEqual(image.size, (entry["width"] * 4, entry["height"] * 4))
                self.assertEqual(hashlib.sha256((folder / entry["file"]).read_bytes()).hexdigest(), entry["pngSha256"])
            self.assertFalse(meta["frames"][5]["ink"])  # the blank frame stays transparent
            self.assertEqual(np.asarray(Image.open(folder / "frame-005.png")).max(), 0)
            self.assertEqual(meta["summary"]["vanished"], [])
            receipt = json.loads((out / "receipt.json").read_text())
            self.assertEqual(receipt["lockSha256"], hashlib.sha256(hd_font.LOCK.read_bytes()).hexdigest())
            self.assertEqual(receipt["fonts"][0]["framesSha256"], meta["framesSha256"])
            first_run = meta["framesSha256"]
            self.assertEqual(self.run_trace(game, out), 0)  # re-running replaces only the tool's own folder, same bytes
            self.assertEqual(json.loads((folder / "font.json").read_text())["framesSha256"], first_run)
            self.assertEqual(len(masks), 5)

    def test_the_command_line_defaults_are_the_tracers_constants_and_a_custom_run_is_recorded_and_undone(self) -> None:
        defaults = hd_font.build_parser().parse_args(["trace", "--out", "x"])
        self.assertEqual((defaults.alpha_max, defaults.level_cap, defaults.opt_tolerance),
                         (outline.ALPHA_MAX, outline.LEVEL_CAP, outline.OPT_TOLERANCE))
        art, _ = synthetic_font()
        with tempfile.TemporaryDirectory() as temp:
            game = build_game(Path(temp), art)
            out = self.checkout(temp)
            self.assertEqual(self.run_trace(game, out, "--alpha-max", "0.2", "--level-cap", "0.3", "--opt-tolerance", "0.1"), 0)
            parameters = json.loads((out / "receipt.json").read_text())["parameters"]
            self.assertEqual((parameters["alphaMax"], parameters["optTolerance"], parameters["levelRule"]),
                             (0.2, 0.1, "mass-preserving, at most 0.3"))
            self.assertEqual((outline.ALPHA_MAX, outline.LEVEL_CAP, outline.OPT_TOLERANCE), (0.6, 0.42, 0.2))  # restored for the next run
            with self.assertRaises(SystemExit):
                self.run_trace(game, out, "--level-cap", "0.99")

    def test_a_foreign_receipt_and_a_linked_frame_are_not_written_through(self) -> None:
        art, _ = synthetic_font()
        with tempfile.TemporaryDirectory() as temp:
            game = build_game(Path(temp), art)
            out = self.checkout(temp)
            out.mkdir(parents=True)
            (out / "receipt.json").write_text("{}")
            with self.assertRaises(SystemExit):
                self.run_trace(game, out)
            self.assertEqual((out / "receipt.json").read_text(), "{}")
            (out / "receipt.json").unlink()
            self.assertEqual(self.run_trace(game, out), 0)
            victim = Path(temp) / "victim.png"
            victim.write_bytes(b"keep")
            frame = out / "7-testfont" / "frame-000.png"
            frame.unlink()
            frame.symlink_to(victim)
            with self.assertRaises(SystemExit):
                self.run_trace(game, out)
            self.assertEqual(victim.read_bytes(), b"keep")

    def test_a_foreign_venv_is_not_replaced(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            foreign = Path(temp) / "venv"
            foreign.mkdir()
            (foreign / "pyvenv.cfg").write_text("home = x\n")
            self.assertFalse(hd_font.replaceable_venv(foreign))
            (foreign / hd_font.VENV_MARKER).write_text("ours\n")
            self.assertTrue(hd_font.replaceable_venv(foreign))
            self.assertTrue(hd_font.replaceable_venv(Path(temp) / "absent"))

    def test_a_foreign_non_empty_folder_is_not_overwritten(self) -> None:
        art, _ = synthetic_font()
        with tempfile.TemporaryDirectory() as temp:
            game = build_game(Path(temp), art)
            out = self.checkout(temp)
            (out / "7-testfont").mkdir(parents=True)
            (out / "7-testfont" / "mine.txt").write_text("keep")
            with self.assertRaises(SystemExit):
                self.run_trace(game, out)
            self.assertEqual((out / "7-testfont" / "mine.txt").read_text(), "keep")


class LockTests(unittest.TestCase):
    def test_every_requirement_is_pinned_with_hashes(self) -> None:
        text = hd_font.LOCK.read_text()
        requirements = re.findall(r"^([A-Za-z0-9_.-]+)==([0-9][^\s\\]*) \\\n((?:    --hash=sha256:[0-9a-f]{64}(?: \\)?\n?)+)", text, re.M)
        self.assertEqual(sorted(name for name, _version, _hashes in requirements), ["numpy", "pillow", "potracer", "scipy"])
        for name, _version, hashes in requirements:
            self.assertGreaterEqual(len(re.findall(r"sha256:", hashes)), 1, name)
        code_lines = [line for line in text.splitlines() if line and not line.startswith("#") and not line.startswith("    --hash=")]
        self.assertEqual(len(code_lines), 4, code_lines)  # nothing unpinned between the four pins


if __name__ == "__main__":
    unittest.main()
