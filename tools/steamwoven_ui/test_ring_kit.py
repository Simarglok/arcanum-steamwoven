"""ARC-UI-3270: tests of the small brass-ringed button kit tool (the HD pack's frames cut to the ring's circle) on synthetic frames (no art)."""
from __future__ import annotations

import contextlib
import io
import json
import math
import tempfile
import unittest
from pathlib import Path

from . import pngio
from . import ring_kit as rk
from . import school_kit as sk

THREE = rk.ring_named("lil_accept")      # art 823, 28 x 29 units, master 102 x 102, three frames
TWO = rk.ring_named("tiny_butt")         # art 340, two frames
INSIDE, OUTSIDE = (200, 30, 30), (30, 200, 30)
COLOURS = (INSIDE, (30, 30, 200), (200, 200, 30))   # f000 idle, f001 pressed, f002 hover


def pack_frame(ring: rk.Ring, colour=INSIDE, hole: bool = False) -> pngio.Image:
    """A pack frame of 4 px per unit: opaque `colour` inside the circle (a little wider than the cut), transparent green outside it; `hole` makes the middle transparent too."""
    c = ring.circle
    w, h = c.width * 4, c.height * 4
    image = pngio.Image(w, h, bytearray(w * h * 4))
    for y in range(h):
        for x in range(w):
            distance = math.hypot(x + 0.5 - c.cx * 4, y + 0.5 - c.cy * 4)
            opaque = distance <= c.cut * 4 + 1 and not (hole and distance < 8)
            image.data[(y * w + x) * 4:(y * w + x) * 4 + 4] = bytes((*(colour if distance <= c.cut * 4 + 1 else OUTSIDE), 255 if opaque else 0))
    return image


def tree(folder: str, ring: rk.Ring) -> Path:
    """`<folder>/local/hd-packs/interface-redraw-4x/` with `ring`'s frames and their index; returns `<folder>/local/steamwoven-ui`."""
    local = Path(folder) / "local"
    pack = local / sk.PACK_DIR
    (pack / "interface").mkdir(parents=True)
    rows = ["screen\tkind\tart\tframe\tpath\tlegacy_w\tlegacy_h\thd_w\thd_h\tslice\talpha\treview\tsha12\n"]
    for state in ring.states:
        number = rk.STATE_FRAMES[state]
        pngio.write(pack / "interface" / f"{ring.name}-f{number:03d}.png", pack_frame(ring, COLOURS[number]))
        rows.append(f"x\tbuttons\t{ring.name}\t{number}\tinterface/{ring.name}-f{number:03d}.png\t1\t1\t1\t1\ts\tmasked\tunreviewed\tabc123def45{number}\n")
    (pack / "index.tsv").write_text("".join(rows), encoding="utf-8")
    return local / "steamwoven-ui"


def run(*argv: str) -> tuple[int, str]:
    out, err = io.StringIO(), io.StringIO()
    with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
        code = rk.main(list(argv))
    return code, out.getvalue() + err.getvalue()


def pixel(image: pngio.Image, x: int, y: int) -> tuple[int, ...]:
    return tuple(image.data[(y * image.w + x) * 4:(y * image.w + x) * 4 + 4])


class TableTests(unittest.TestCase):
    def test_the_sixteen_arts_of_the_issue_once_each(self):
        self.assertEqual(sorted(r.art for r in rk.RINGS), [23, 24, 145, 146, 147, 148, 299, 338, 339, 340, 757, 758, 770, 771, 823, 824])
        self.assertEqual(len({r.name for r in rk.RINGS}), 16)

    def test_two_frame_arts_have_no_hover_and_the_empty_fourth_frame_has_no_part(self):
        self.assertEqual(TWO.states, ("idle", "pressed"))
        self.assertEqual(rk.ring_named("char_minus").states, ("idle", "pressed", "hover"))
        self.assertEqual([i for i, _ in rk.idents(TWO)], ["plate.tiny_butt", "glyph.tiny_butt", "plate.tiny_butt_pressed"])

    def test_every_circle_lies_in_its_art_but_for_the_stated_overshoot(self):
        for ring in rk.RINGS:
            c = ring.circle
            self.assertGreaterEqual(min(c.cx - c.cut, c.cy - c.cut, c.width - c.cx - c.cut, c.height - c.cy - c.cut), -rk.OVERSHOOT, ring.name)
            self.assertGreater(c.radius, 0.35 * c.width, ring.name)

    def test_the_manifest_rows_of_the_slice(self):
        manifest = {p["id"]: p for p in json.loads(sk.MANIFEST.read_text(encoding="utf-8"))["parts"]}
        total = 0
        for ring in rk.RINGS:
            for ident, _ in rk.idents(ring):
                row = manifest[ident]
                self.assertEqual((row["status"], row["provenance"], row["batch"]), ("master", f"rule:ring_kit:{ident}", "B-BUTTONS"), ident)
                total += 1
            plate = manifest[f"plate.{ring.name}"]
            self.assertEqual((tuple(plate["size"]), plate["frames"], plate["source_arts"]), (ring.master, ring.frames, [str(ring.art)]), ring.name)
            self.assertEqual(f"plate.{ring.name}_hover" in manifest, ring.frames >= 3, ring.name)
        self.assertEqual(total, 57)


class PartTests(unittest.TestCase):
    def test_bled_colours_the_transparent_pixels_from_their_neighbours_and_keeps_the_opaque_ones(self):
        frame = pngio.Image(3, 1, bytearray(bytes((10, 20, 30, 255)) + bytes((0, 0, 0, 0)) + bytes((90, 90, 90, 100))))
        out = rk.bled(frame)
        self.assertEqual([pixel(out, x, 0) for x in range(3)], [(10, 20, 30, 255)] * 3)

    def test_a_frame_with_nothing_opaque_is_refused(self):
        with self.assertRaises(rk.RingError):
            rk.bled(pngio.Image(2, 2, bytearray(16)))

    def test_the_packs_own_alpha_is_not_used_and_the_cut_is_round(self):
        part = rk.part_of(THREE, pack_frame(THREE, hole=True))
        c, (w, h) = THREE.circle, THREE.master
        self.assertEqual((part.w, part.h), (w, h))
        cx, cy = int(c.cx * w / c.width), int(c.cy * h / c.height)
        self.assertEqual(pixel(part, cx, cy), (*INSIDE, 255))                 # the pack's hole is filled
        for corner in ((0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)):
            self.assertEqual(pixel(part, *corner)[3], 0)
        edge = pixel(part, int((c.cx - c.cut) * w / c.width + 2), cy)
        self.assertEqual(edge, (*INSIDE, 255))                                # the old green never reaches the edge

    def test_the_parts_are_the_pack_frames_of_their_states_and_the_glyph_is_empty(self):
        with tempfile.TemporaryDirectory() as folder:
            local = sk.pack_local(tree(folder, THREE))
            parts = rk.ring_parts(local, THREE)
            c, (w, h) = THREE.circle, THREE.master
            centre = (int(c.cx * w / c.width), int(c.cy * h / c.height))
            self.assertEqual(pixel(parts["plate.lil_accept"], *centre)[:3], COLOURS[0])
            self.assertEqual(pixel(parts["plate.lil_accept_pressed"], *centre)[:3], COLOURS[1])
            self.assertEqual(pixel(parts["plate.lil_accept_hover"], *centre)[:3], COLOURS[2])
            self.assertFalse(any(parts["glyph.lil_accept"].data))

    def test_a_missing_frame_or_another_aspect_is_a_ring_error(self):
        with tempfile.TemporaryDirectory() as folder:
            local = sk.pack_local(tree(folder, TWO))
            with self.assertRaises(rk.RingError):
                rk.state_frame(local, TWO, "hover")
            path = local / sk.PACK_DIR / "interface" / "tiny_butt-f000.png"
            pngio.write(path, pngio.Image(100, 40, bytearray(100 * 40 * 4)))
            with self.assertRaises(rk.RingError):
                rk.state_frame(local, TWO, "idle")


class CommandTests(unittest.TestCase):
    def test_a_dry_run_writes_nothing_and_a_build_writes_parts_and_records(self):
        with tempfile.TemporaryDirectory() as folder:
            root = tree(folder, TWO)
            code, text = run("build", "--local-root", str(root), "--only", "tiny_butt", "--dry-run")
            self.assertEqual(code, 0, text)
            self.assertFalse(root.exists())
            code, text = run("build", "--local-root", str(root), "--only", "tiny_butt")
            self.assertEqual(code, 0, text)
            self.assertEqual(sorted(p.name for p in (root / "masters").iterdir()), ["glyph.tiny_butt.png", "plate.tiny_butt.png", "plate.tiny_butt_pressed.png"])
            record = json.loads((root / "provenance" / "rule" / "plate.tiny_butt_pressed.json").read_text(encoding="utf-8"))
            self.assertEqual((record["rule"], record["source"]), ("ring_kit", "pack"))
            self.assertEqual([(k, v["index_sha12"]) for k, v in record["sources"].items()], [("hd-packs/interface-redraw-4x/tiny_butt-f001", "abc123def451")])
            glyph = json.loads((root / "provenance" / "rule" / "glyph.tiny_butt.json").read_text(encoding="utf-8"))
            self.assertEqual((glyph["empty"], glyph["sources"]), (True, {}))
            self.assertEqual(run("check", "--local-root", str(root), "--only", "tiny_butt")[0], 0)

    def test_an_unknown_button_or_a_missing_pack_is_an_input_error(self):
        with tempfile.TemporaryDirectory() as folder:
            self.assertEqual(run("build", "--local-root", folder, "--only", "nothing")[0], 2)
            self.assertEqual(run("build", "--local-root", str(Path(folder) / "steamwoven-ui"), "--only", "tiny_butt")[0], 2)


if __name__ == "__main__":
    unittest.main()
