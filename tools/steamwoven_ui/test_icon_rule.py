"""ARC-UI-2485: tests of the icon rule (ghost repair and upscale) on synthetic pictures only (no original art)."""
from __future__ import annotations

import io
import json
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path

from tools.steamwoven_ui import icon_rule, pack_plan, pngio
from tools.steamwoven_ui.pngio import Image

S = icon_rule.SCALE


def solid(w: int, h: int, rgba) -> Image:
    return Image.blank(w, h, rgba)


def put(image: Image, x: int, y: int, rgba) -> None:
    image.data[(y * image.w + x) * 4:(y * image.w + x) * 4 + 4] = bytes(rgba)


def original_with_object() -> Image:
    """24 x 24, black everywhere but a bright 6 x 6 object in the middle."""
    image = solid(24, 24, (0, 0, 0, 255))
    for y in range(9, 15):
        for x in range(9, 15):
            put(image, x, y, (200, 150, 60, 255))
    return image


def pack_with_ghost(original: Image) -> Image:
    """The original at x4 (nearest) plus a faint grey outline (the ghost) far from the object, inside the black."""
    hd = icon_rule.upscale_square(original, S)
    for x in range(8, hd.w - 8):
        put(hd, x, 20, (90, 90, 90, 255))
    for y in range(8, hd.h - 8):
        put(hd, 20, y, (90, 90, 90, 255))
    return hd


class GhostTests(unittest.TestCase):
    def test_ghost_share_is_measured_and_removed(self) -> None:
        original = original_with_object()
        hd = pack_with_ghost(original)
        before = icon_rule.ghost_percent(original, hd)
        self.assertGreater(before, 1.0)
        fixed = icon_rule.clear_ghost(original, hd)
        self.assertEqual(icon_rule.ghost_percent(original, fixed), 0.0)
        self.assertLess(icon_rule.ghost_percent(original, fixed), icon_rule.GHOST_MAX_PERCENT)

    def test_the_object_is_kept_and_alpha_is_untouched(self) -> None:
        original = original_with_object()
        hd = pack_with_ghost(original)
        put(hd, 20, 30, (90, 90, 90, 180))  # a partial alpha inside the black: clearing must not touch it
        fixed = icon_rule.clear_ghost(original, hd)
        centre = (12 * S + 2, 12 * S + 2)
        self.assertEqual(fixed.pixel(*centre), hd.pixel(*centre))
        self.assertTrue(all(fixed.data[i] == hd.data[i] for i in range(3, len(hd.data), 4)))

    def test_a_clean_frame_stays_identical(self) -> None:
        original = original_with_object()
        hd = icon_rule.upscale_square(original, S)
        self.assertEqual(bytes(icon_rule.clear_ghost(original, hd).data), bytes(hd.data))

    def test_no_measurable_black_gives_none(self) -> None:
        original = solid(8, 8, (120, 90, 60, 255))
        self.assertIsNone(icon_rule.ghost_percent(original, icon_rule.upscale_square(original, S)))

    def test_size_mismatch_is_refused(self) -> None:
        with self.assertRaises(icon_rule.RuleError):
            icon_rule.ghost_percent(original_with_object(), solid(10, 10, (0, 0, 0, 255)))


class UpscaleTests(unittest.TestCase):
    def dots(self) -> Image:
        """4 x 4 with single white dots on a transparent frame (the square dots of tb_td)."""
        image = solid(4, 4, (0, 0, 0, 0))
        for x, y in ((0, 0), (2, 0), (0, 2), (2, 2)):
            put(image, x, y, (255, 255, 255, 255))
        return image

    def test_square_is_exact_blocks(self) -> None:
        original = self.dots()
        hd = icon_rule.upscale(original, "square")
        self.assertEqual((hd.w, hd.h), (16, 16))
        for y in range(16):
            for x in range(16):
                self.assertEqual(hd.pixel(x, y), original.pixel(x // S, y // S))

    def test_smooth_keeps_the_alpha_exactly_and_never_lends_colour_from_a_transparent_cell(self) -> None:
        original = solid(3, 1, (0, 0, 0, 0))
        put(original, 0, 0, (255, 0, 0, 255))
        put(original, 1, 0, (0, 0, 255, 255))
        hd = icon_rule.upscale(original, "smooth")
        for x in range(hd.w):
            self.assertEqual(hd.pixel(x, 0)[3], original.pixel(x // S, 0)[3])
        for x in range(2 * S, 3 * S):  # the transparent cell stays empty
            self.assertEqual(hd.pixel(x, 0), (0, 0, 0, 0))
        left, right = hd.pixel(S - 1, 0), hd.pixel(S, 0)  # across the red/blue seam the colour blends
        self.assertTrue(0 < left[2] < 255 and 0 < right[0] < 255)

    def test_smooth_of_flat_colour_is_flat(self) -> None:
        original = solid(3, 3, (10, 200, 30, 255))
        hd = icon_rule.upscale(original, "smooth")
        self.assertTrue(all(hd.pixel(x, y) == (10, 200, 30, 255) for y in range(hd.h) for x in range(hd.w)))

    def test_unknown_mode(self) -> None:
        with self.assertRaises(icon_rule.RuleError):
            icon_rule.upscale(self.dots(), "round")


class FilesTests(unittest.TestCase):
    def test_refuses_an_output_inside_the_checkout(self) -> None:
        with self.assertRaises(icon_rule.RuleError):
            icon_rule.check_out_dir(icon_rule.REPO / "tools" / "steamwoven_ui" / "out")
        icon_rule.check_out_dir(icon_rule.REPO / "local" / "x")
        with tempfile.TemporaryDirectory() as tmp:
            icon_rule.check_out_dir(Path(tmp))

    def test_written_frames_become_a_plan(self) -> None:
        from tools.steamwoven_ui import census
        art = census.CensusArt("679", "scroll-0", (3, 2), 1, "picture", "B-ICONS", (), "drawn")
        original = solid(3, 2, (9, 9, 9, 255))
        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp)
            icon_rule.write_art(out, art, [icon_rule.upscale(original, "square")], "legacy")
            record = json.loads((out / "679.assembly.json").read_text())
            self.assertEqual((record["art"], record["scale"], record["alphaMode"]), ("679", S, "legacy"))
            plan = pack_plan.build_plan([pack_plan.load_assembly(out / "679.assembly.json")], out, arts={"679": art})
            self.assertEqual(len(plan.document["items"]), 1)

    def test_a_frame_of_the_wrong_size_is_refused_by_the_plan(self) -> None:
        from tools.steamwoven_ui import census
        art = census.CensusArt("679", "scroll-0", (3, 2), 1, "picture", "B-ICONS", (), "drawn")
        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp)
            icon_rule.write_art(out, art, [icon_rule.upscale(solid(5, 2, (9, 9, 9, 255)), "square")], "legacy")
            with self.assertRaises(pack_plan.PlanError):
                pack_plan.build_plan([pack_plan.load_assembly(out / "679.assembly.json")], out, arts={"679": art})

    def test_cli_refuses_an_unknown_art_and_missing_arguments(self) -> None:
        buffer = io.StringIO()
        with redirect_stderr(buffer):
            self.assertEqual(icon_rule.main(["upscale", "--frames", "/nonexistent", "--out", tempfile.gettempdir() + "/icon-rule-unused", "no_such_art_zz"]), 2)
        self.assertIn("not in the census", buffer.getvalue())
        with redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
            icon_rule.main(["upscale"])


if __name__ == "__main__":
    unittest.main()
