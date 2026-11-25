"""ARC-UI-3204: tests of the HD-pack symbol source (`pack_symbol.py` and its use in `school_kit.tab_symbol`) on synthetic frames (no art)."""
from __future__ import annotations

import math
import tempfile
import unittest
from pathlib import Path

from . import pack_symbol as ps
from . import pngio
from . import school_kit as sk
from .test_school_kit import N, SYMBOL, frame

S = 4                       # pack pixels per original cell
W = N * S
RING = 41.6
CROSS = {(x, y) for y in range(32, 68) for x in range(44, 52)} | {(x, y) for y in range(44, 52) for x in range(32, 64)}   # the original's cross x 4


def pack_frame(shift=(0, 0), crack=True, crescent=True) -> pngio.Image:
    """A 96 x 96 pack frame: a brass ring, a dome that is lighter towards the upper left, a thick black cross, a one-pixel crack and a dark crescent of the dome's rim at the lower right."""
    image = pngio.Image(W, W, bytearray(W * W * 4))
    c = W / 2
    for y in range(W):
        for x in range(W):
            r = math.hypot(x + 0.5 - c, y + 0.5 - c)
            if r > 46.4:
                continue
            if r > RING:
                colour = (150, 120, 50)
            else:
                lift = 1.0 + 0.5 * (-(x + 0.5 - c) - (y + 0.5 - c)) / 80.0
                colour = tuple(max(0, min(255, int(v * lift))) for v in (40, 90, 160))
                if crescent and r > 34 and x + 0.5 > c and y + 0.5 > c - (x + 0.5 - c):
                    colour = (6, 12, 24)
            image.data[(y * W + x) * 4:(y * W + x) * 4 + 4] = bytes((*colour, 255))
    dx, dy = shift
    for x, y in CROSS:
        i = ((y + dy) * W + x + dx) * 4
        image.data[i:i + 3] = bytes((2, 2, 2))
    if crack:
        for x in range(60, 80):
            i = (24 * W + x) * 4
            image.data[i:i + 3] = bytes((3, 6, 12))
    return image


def tree(folder: str, tab: sk.Tab, shift=(0, 0), with_pack=True) -> Path:
    """`<folder>/local/steamwoven-ui/census/frames/<tab>/f0-2.png` and, when asked, `<folder>/local/hd-packs/interface-redraw-4x/` with the tab's pack frame and its index."""
    root = Path(folder) / "local"
    frames = root / "steamwoven-ui" / "census" / "frames"
    (frames / tab.folder).mkdir(parents=True)
    for k, shade in enumerate((1.0, 1.7, 1.3)):
        pngio.write(frames / tab.folder / f"f{k}.png", frame(shade=shade))
    if with_pack:
        pack = root / ps.PACK_DIR
        (pack / "interface").mkdir(parents=True)
        pngio.write(pack / "interface" / f"{tab.name}-f000.png", pack_frame(shift))
        header = "screen\tkind\tart\tframe\tpath\tlegacy_w\tlegacy_h\thd_w\thd_h\tslice\talpha\treview\tsha12\n"
        (pack / "index.tsv").write_text(header + f"x\tbuttons\t{tab.name}\t0\tinterface/{tab.name}-f000.png\t{N}\t{N}\t{W}\t{W}\ts\t\tunreviewed\tabc123def456\n", encoding="utf-8")
    return frames


DISC = (12.0, 12.0, 10.6)   # the original dome's (x, y, radius) as `school_kit.dome_radius` gives it for the test frames


class CutTests(unittest.TestCase):
    def test_cut_finds_the_thick_cross_and_drops_the_crack_and_the_rim_shade(self):
        mask = ps.cut_symbol(pack_frame(), (48.0, 48.0, 10.6 * S))
        found = {i for i, on in enumerate(mask) if on}
        truth = {y * W + x for x, y in CROSS}
        self.assertGreaterEqual(len(found & truth) / len(truth), 0.9)
        self.assertEqual([i for i in found if i // W < 28], [])          # the crack lies up there
        self.assertEqual([i for i in found if i % W > 56 and i // W > 56], [])   # the crescent lies at the lower right

    def test_a_plain_dome_has_no_symbol(self):
        mask = ps.cut_symbol(self.plain(), (48.0, 48.0, 10.6 * S))
        self.assertFalse(any(mask))

    @staticmethod
    def plain() -> pngio.Image:
        image = pack_frame(crack=False, crescent=False)
        for x, y in CROSS:
            c = W / 2
            lift = 1.0 + 0.5 * (-(x + 0.5 - c) - (y + 0.5 - c)) / 80.0
            image.data[(y * W + x) * 4:(y * W + x) * 4 + 3] = bytes(max(0, min(255, int(v * lift))) for v in (40, 90, 160))
        return image

    def test_the_pack_frame_is_read_through_the_index(self):
        tab = sk.TABS[0]
        with tempfile.TemporaryDirectory() as folder:
            tree(folder, tab)
            frame_read, sha = ps.read_pack(Path(folder) / "local", tab.name, 0)
            self.assertEqual((frame_read.w, frame_read.h, sha), (W, W, "abc123def456"))
            with self.assertRaises(ps.PackError):
                ps.read_pack(Path(folder) / "local", tab.name, 1)
            with self.assertRaises(ps.PackError):
                ps.read_pack(Path(folder) / "local", "no_such_art", 0)


class FitTests(unittest.TestCase):
    def fit_of(self, original):
        mask = ps.cut_symbol(pack_frame(), (48.0, 48.0, 10.6 * S))
        return ps.fit_symbol(mask, W, W, original, N, N, (12.0, 12.0))[0]

    def test_the_fit_of_a_symbol_that_sits_right_is_the_identity(self):
        original = [False] * (N * N)
        for x, y in SYMBOL:
            original[y * N + x] = True
        fit = self.fit_of(original)
        self.assertLessEqual(abs(fit.scale - 1.0), 0.035)
        self.assertEqual((fit.dx, fit.dy), (0.0, 0.0))
        self.assertGreater(fit.score, 0.8)
        self.assertFalse(fit.flagged)

    def test_the_fit_follows_a_shift_of_the_original_symbol(self):
        original = [False] * (N * N)
        for x, y in SYMBOL:
            original[y * N + x + 1] = True      # one cell to the right
        fit = self.fit_of(original)
        self.assertEqual((fit.dx, fit.dy), (1.0, 0.0))
        self.assertFalse(fit.flagged)

    def test_a_far_symbol_is_flagged_but_still_fitted(self):
        original = [False] * (N * N)
        for y in range(1, 4):
            for x in range(18, 22):
                original[y * N + x] = True       # somewhere the pack's symbol is not
        fit = self.fit_of(original)
        self.assertTrue(fit.flagged)
        self.assertLess(fit.contour, ps.DRIFT_FLAG)


class TabSymbolTests(unittest.TestCase):
    def test_a_college_tab_takes_its_symbol_from_the_pack(self):
        tab = sk.TABS[0]
        with tempfile.TemporaryDirectory() as folder:
            frames = tree(folder, tab)
            symbol = sk.tab_symbol(frames, tab, sk.master_of(tab.name))
            self.assertEqual(symbol.source, "pack")
            self.assertEqual(symbol.pack.sha12, "abc123def456")
            k = symbol.dark.w / N
            at = lambda x, y: symbol.dark.cover[int(y * k) * symbol.dark.w + int(x * k)]
            self.assertEqual(at(12.0, 12.0), 1.0)     # the cross's middle
            self.assertEqual(at(4.0, 4.0), 0.0)       # the dome beside it
            self.assertEqual(at(17.0, 6.0), 0.0)      # the crack is no symbol
            self.assertEqual(sk.symbol_record(frames, tab)["source"], "pack")
            self.assertEqual(sk.symbol_record(frames, tab)["pack_frame"]["sha12"], "abc123def456")

    def test_without_a_pack_the_mask_reconstruction_stays(self):
        tab = sk.TABS[0]
        with tempfile.TemporaryDirectory() as folder:
            frames = tree(folder, tab, with_pack=False)
            symbol = sk.tab_symbol(frames, tab, sk.master_of(tab.name))
            self.assertEqual(symbol.source, "mask")
            self.assertIsNone(symbol.pack)

    def test_a_school_keeps_the_mask_unless_its_pack_symbol_is_better(self):
        self.assertEqual(sk.source_of(next(t for t in sk.TABS if t.name == "social_but")), "mask")
        self.assertEqual(sk.source_of(next(t for t in sk.TABS if t.name == "gunsmithy_but")), "pack")
        self.assertTrue(all(sk.source_of(t) == "pack" for t in sk.TABS if t.group == "college"))

    def test_the_parts_of_a_pack_symbol_keep_the_master_size(self):
        tab = sk.TABS[0]
        with tempfile.TemporaryDirectory() as folder:
            frames = tree(folder, tab)
            parts = sk.tab_parts(frames, tab, None)
            for image in parts.values():
                self.assertEqual((image.w, image.h), sk.master_of(tab.name))
            self.assertTrue(any(parts[f"glyph.{tab.name}"].data[3::4]))


if __name__ == "__main__":
    unittest.main()
