"""ARC-UI-3270: tests of the small brass-ringed button kit tool on synthetic pictures (no art): the table, the brass tone, each family's face, the sheet of the generated set and the commands."""
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
from . import tone_check

PACKED = rk.ring_named("lil_accept")     # art 823, 28 x 29 units, master 102 x 102, three frames, from the pack
TWO = rk.ring_named("tiny_butt")         # art 340, two frames, from the pack
BRASSY, DOME, RED = (170, 120, 50), (30, 30, 8), (200, 10, 10)


def pixel(image: pngio.Image, x: int, y: int) -> tuple[int, ...]:
    return tuple(image.data[(y * image.w + x) * 4:(y * image.w + x) * 4 + 4])


def disc(w: int, h: int, cx: float, cy: float, radius: float, ring=BRASSY, dome=DOME, ground=(30, 200, 30, 0)) -> pngio.Image:
    """A w x h picture: a disc of `dome` colour with a ring of `ring` colour from 0.74 of the radius, on `ground` (RGBA)."""
    image = pngio.Image(w, h, bytearray(bytes(ground) * (w * h)))
    for y in range(h):
        for x in range(w):
            d = math.hypot(x + 0.5 - cx, y + 0.5 - cy)
            if d <= radius:
                image.data[(y * w + x) * 4:(y * w + x) * 4 + 4] = bytes((*(ring if d >= 0.74 * radius else dome), 255))
    return image


def box(image: pngio.Image, x0: int, y0: int, x1: int, y1: int, colour) -> None:
    for y in range(y0, y1):
        for x in range(x0, x1):
            image.data[(y * image.w + x) * 4:(y * image.w + x) * 4 + 4] = bytes((*colour, 255))


def pack_frame(ring: rk.Ring, dome=DOME, hole: bool = False) -> pngio.Image:
    """A pack frame of 4 px per unit with the button's disc (a little wider than the cut) on a transparent ground; `hole` makes the middle transparent."""
    c = ring.circle
    image = disc(c.width * 4, c.height * 4, c.cx * 4, c.cy * 4, c.cut * 4 + 1, dome=dome)
    if hole:
        for y in range(image.h):
            for x in range(image.w):
                if math.hypot(x + 0.5 - c.cx * 4, y + 0.5 - c.cy * 4) < 8:
                    image.data[(y * image.w + x) * 4 + 3] = 0
    return image


def generated_sheet(shift: int = 7) -> pngio.Image:
    """The generated plus and minus sheet at a quarter size: 3 x 2 cells of 128 px, each a disc on grey with a red bar (`shift` px right of the middle); the Minus's ring is brass, the Plus's another colour."""
    sheet = pngio.Image(384, 256, bytearray(bytes((128, 128, 128, 255)) * (384 * 256)))
    for row in range(2):
        for column in range(3):
            cx, cy = column * 128 + 64, row * 128 + 64
            cell = disc(128, 128, 64, 64, 128 * rk.CELL_SHARE, ring=BRASSY if row == 0 else (60, 60, 200), ground=(128, 128, 128, 255))
            box(cell, 64 - 10 + shift, 61, 64 + 10 + shift, 67, (200 + 20 * column, 10, 10))
            rk.paste(sheet, cell, cx - 64, cy - 64)
    return sheet


def original(ring: rk.Ring, cells: dict) -> pngio.Image:
    """An original frame in cells: an olive disc with `cells` {(x, y): colour} on it."""
    image = disc(ring.circle.width, ring.circle.height, 11.5, 11.5, 11.5, ring=(100, 80, 30))
    for (x, y), colour in cells.items():
        box(image, x, y, x + 1, y + 1, colour)
    return image


WEDGE_CELLS = {(x, y): RED for x, half in zip(range(9, 15), (6, 5, 4, 3, 2, 1)) for y in range(11 - half, 12 + half)}          # points right
ARROW_CELLS = {**{(x, y): RED for x in range(7, 12) for y in range(9, 14)}, **{(x, y): RED for x, half in zip(range(12, 18), (4, 4, 3, 2, 1, 0)) for y in range(11 - half, 12 + half)}}


def tree(folder: str) -> Path:
    """`<folder>/local/` with every input: the pack frames of the pack buttons, the generated sheet, the marker sheet and the original frames; returns `<folder>/local/steamwoven-ui`."""
    local = Path(folder) / "local"
    root = local / "steamwoven-ui"
    pack = local / sk.PACK_DIR
    (pack / "interface").mkdir(parents=True)
    rows = ["screen\tkind\tart\tframe\tpath\tlegacy_w\tlegacy_h\thd_w\thd_h\tslice\talpha\treview\tsha12\n"]
    for ring in rk.RINGS:
        for state in ring.states:
            number = rk.STATE_FRAMES[state]
            if ring.method == "pack":
                pngio.write(pack / "interface" / f"{ring.name}-f{number:03d}.png", pack_frame(ring))
                rows.append(f"x\tbuttons\t{ring.name}\t{number}\tinterface/{ring.name}-f{number:03d}.png\t1\t1\t1\t1\ts\tmasked\tunreviewed\tabc123def45{number}\n")
            if ring.method in ("wedge", "arrow"):
                path = root / "census" / "frames" / ring.folder / f"f{number}.png"
                path.parent.mkdir(parents=True, exist_ok=True)
                pngio.write(path, original(ring, WEDGE_CELLS if ring.method == "wedge" else ARROW_CELLS))
    (pack / "index.tsv").write_text("".join(rows), encoding="utf-8")
    sheet = rk.sheet_path(root)
    sheet.parent.mkdir(parents=True)
    pngio.write(sheet, generated_sheet())
    markers = pngio.Image(192, 128, bytearray(bytes((255, 0, 255, 255)) * (192 * 128)))
    for column, row in rk.MARKER_CELLS.values():
        box(markers, column * 64 + 16, row * 64 + 12, column * 64 + 48, row * 64 + 52, (240, 240, 240))
    rk.marker_sheet(root).parent.mkdir(parents=True)
    pngio.write(rk.marker_sheet(root), markers)
    return root


def run(*argv: str) -> tuple[int, str]:
    out, err = io.StringIO(), io.StringIO()
    with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
        code = rk.main(list(argv))
    return code, out.getvalue() + err.getvalue()


def at(ring: rk.Ring, image: pngio.Image, ux: float, uy: float) -> tuple[int, ...]:
    return pixel(image, int(ux * image.w / ring.circle.width), int(uy * image.h / ring.circle.height))


class TableTests(unittest.TestCase):
    def test_the_sixteen_arts_of_the_issue_once_each_by_the_owners_methods(self):
        self.assertEqual(sorted(r.art for r in rk.RINGS), [23, 24, 145, 146, 147, 148, 299, 338, 339, 340, 757, 758, 770, 771, 823, 824])
        by_method = {m: sorted(r.art for r in rk.RINGS if r.method == m) for m in ("codex", "marker", "wedge", "arrow", "pack")}
        self.assertEqual(by_method, {"codex": [23, 24, 770, 771], "marker": [145, 146, 147, 148], "wedge": [757, 758], "arrow": [299], "pack": [338, 339, 340, 823, 824]})

    def test_the_plus_and_minus_stand_on_the_sheets_socket_centres(self):
        centres = {r.art: (r.circle.cx, r.circle.cy, round(2 * r.circle.cut, 3)) for r in rk.RINGS if r.method == "codex"}
        self.assertEqual(centres, {23: (9.5, 9.5, 18.0), 24: (10.5, 9.5, 18.0), 770: (10.5, 10.0, 21.0), 771: (10.5, 10.0, 21.0)})

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
            for ident, state in rk.idents(ring):
                row = manifest[ident]
                prefix = "rule" if state is None or ring.method == "pack" else "codex"
                self.assertEqual((row["status"], row["provenance"], row["batch"]), ("master", f"{prefix}:ring_kit:{ident}", "B-BUTTONS"), ident)
                total += 1
            plate = manifest[f"plate.{ring.name}"]
            self.assertEqual((tuple(plate["size"]), plate["frames"], plate["source_arts"]), (ring.master, ring.frames, [str(ring.art)]), ring.name)
            self.assertEqual(f"plate.{ring.name}_hover" in manifest, ring.frames >= 3, ring.name)
        self.assertEqual(total, 57)

    def test_the_shadow_grows_with_the_parts_diameter(self):
        base = sk.shadow()
        small, large = rk.shadow_of(rk.ring_named("char_minus").circle), rk.shadow_of(sk.Circle(24, 24, 12, 12, 11.35, 0.4))
        self.assertAlmostEqual(large.offset, base.offset, places=4)     # a cut diameter of 23.5 units keeps the accepted numbers
        self.assertAlmostEqual(small.offset, base.offset * 18 / 23.5, places=4)
        self.assertAlmostEqual(small.sigma, base.sigma * 18 / 23.5, places=4)
        self.assertEqual(small.strength, base.strength)


class PartTests(unittest.TestCase):
    def test_bled_colours_the_transparent_pixels_from_their_neighbours_and_keeps_the_opaque_ones(self):
        frame = pngio.Image(3, 1, bytearray(bytes((10, 20, 30, 255)) + bytes((0, 0, 0, 0)) + bytes((90, 90, 90, 100))))
        out = rk.bled(frame)
        self.assertEqual([pixel(out, x, 0) for x in range(3)], [(10, 20, 30, 255)] * 3)

    def test_a_frame_with_nothing_opaque_is_refused(self):
        with self.assertRaises(rk.RingError):
            rk.bled(pngio.Image(2, 2, bytearray(16)))

    def test_the_packs_own_alpha_is_not_used_and_the_cut_is_round(self):
        part = rk.part_of(PACKED, pack_frame(PACKED, hole=True))
        c, (w, h) = PACKED.circle, PACKED.master
        self.assertEqual((part.w, part.h), (w, h))
        self.assertEqual(at(PACKED, part, c.cx, c.cy), (*DOME, 255))                # the pack's hole is filled
        for corner in ((0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)):
            self.assertEqual(pixel(part, *corner)[3], 0)

    def test_only_the_ring_is_brought_to_the_accepted_brass(self):
        part = rk.part_of(PACKED, pack_frame(PACKED))
        out = rk.toned(part, PACKED.circle, rk.INNER["pack"])
        c = PACKED.circle
        self.assertEqual(at(PACKED, out, c.cx, c.cy), (*DOME, 255))
        r, g, b, _ = at(PACKED, out, c.cx - 0.92 * c.cut, c.cy)
        lightness, a, bb = tone_check._pixel_lab(r, g, b)
        self.assertAlmostEqual(lightness, rk.BRASS[0], delta=3)
        self.assertAlmostEqual(math.hypot(a, bb), rk.BRASS[1], delta=3)
        self.assertAlmostEqual(math.degrees(math.atan2(bb, a)), rk.BRASS[2], delta=4)

    def test_the_magenta_ground_is_keyed_out_and_its_colour_leaves_the_soft_edge(self):
        picture = pngio.Image(3, 1, bytearray(bytes((255, 0, 255, 255)) + bytes((10, 10, 10, 255)) + bytes((135, 5, 135, 255))))
        out = rk.keyed_magenta(picture)
        self.assertEqual(pixel(out, 0, 0)[3], 0)
        self.assertEqual(pixel(out, 1, 0), (10, 10, 10, 255))
        soft = pixel(out, 2, 0)
        self.assertTrue(0 < soft[3] < 255)
        self.assertLess(soft[0], 135)

    def test_a_triangle_is_fitted_to_the_red_cells_with_its_tip_at_the_short_end(self):
        wedge = rk.fit_wedge(original(rk.ring_named("mpcyclerightbutton"), WEDGE_CELLS))
        self.assertEqual((wedge.tip, wedge.base, wedge.axis, wedge.colour), (15, 9, 11.5, RED))
        self.assertTrue(wedge.holds(10, 11.5))
        self.assertFalse(wedge.holds(14, 6))
        with self.assertRaises(rk.RingError):
            rk.fit_wedge(original(rk.ring_named("mpcyclerightbutton"), {}))

    def test_an_arrow_is_a_shaft_and_a_head(self):
        arrow = rk.fit_arrow(ARROW_CELLS)
        self.assertEqual((arrow.left, arrow.neck), (7, 12))
        self.assertEqual((arrow.shaft_top, arrow.shaft_bottom), (9, 14))
        self.assertTrue(arrow.holds(8, 11.5) and arrow.holds(12.5, 8.5) and arrow.holds(17.5, 11.5))
        self.assertFalse(arrow.holds(8, 8))
        with self.assertRaises(rk.RingError):
            rk.fit_arrow({(1, 1): RED})


class FaceTests(unittest.TestCase):
    def test_the_generated_set_gets_one_ring_and_a_centred_symbol(self):
        with tempfile.TemporaryDirectory() as folder:
            root = tree(folder)
            frames = root / "census" / "frames"
            minus, plus = rk.ring_named("char_minus"), rk.ring_named("char_htftplus")
            a, b = rk.face_of(root, frames, minus, "idle"), rk.face_of(root, frames, plus, "hover")
            ring_a, ring_b = at(minus, a, minus.circle.cx - 0.9 * minus.circle.cut, minus.circle.cy), at(plus, b, plus.circle.cx - 0.9 * plus.circle.cut, plus.circle.cy)
            self.assertLess(max(abs(x - y) for x, y in zip(ring_a, ring_b)), 6)       # the Plus's own blue ring is replaced by the Minus's brass one
            self.assertGreater(ring_b[0], ring_b[2])
            reds = [x for x in range(a.w) if pixel(a, x, int(minus.circle.cy * a.h / minus.circle.height))[0] > 120 and pixel(a, x, int(minus.circle.cy * a.h / minus.circle.height))[1] < 60]
            self.assertAlmostEqual((min(reds) + max(reds) + 1) / 2, minus.circle.cx * a.w / minus.circle.width, delta=1.5)

    def test_the_symbol_buttons_stand_on_the_sets_plate_and_the_idle_symbol_is_dimmed(self):
        with tempfile.TemporaryDirectory() as folder:
            root = tree(folder)
            frames = root / "census" / "frames"
            ring = rk.ring_named("mmb_chest")
            idle, lit = rk.face_of(root, frames, ring, "idle"), rk.face_of(root, frames, ring, "pressed")
            x0, y0, x1, y1 = rk.SYMBOL_BOXES[ring.name]
            self.assertEqual(at(ring, lit, (x0 + x1) / 2, (y0 + y1) / 2)[:3], (240, 240, 240))
            self.assertAlmostEqual(at(ring, idle, (x0 + x1) / 2, (y0 + y1) / 2)[0], 240 * rk.IDLE_DIM[ring.name], delta=2)
            plate = rk.empty_plate(root, ring)
            centre = at(ring, plate, ring.circle.cx, ring.circle.cy)
            self.assertLess(centre[0], 60)                                             # the bar is gone from the plate
            arrow = rk.ring_named("mpcyclerightbutton")
            self.assertGreater(at(arrow, rk.face_of(root, frames, arrow, "idle"), 11, 11.5)[0], 150)

    def test_a_missing_input_is_a_ring_error(self):
        with tempfile.TemporaryDirectory() as folder:
            root = tree(folder)
            rk.marker_sheet(root).unlink()
            with self.assertRaises(rk.RingError):
                rk.face_of(root, root / "census" / "frames", rk.ring_named("mmb_chest"), "idle")
            rk.sheet_path(root).unlink()
            with self.assertRaises(rk.RingError):
                rk.face_of(root, root / "census" / "frames", rk.ring_named("char_minus"), "idle")

    def test_a_pack_frame_of_another_aspect_is_refused(self):
        with tempfile.TemporaryDirectory() as folder:
            local = sk.pack_local(tree(folder))
            pngio.write(local / sk.PACK_DIR / "interface" / "tiny_butt-f000.png", pngio.Image(100, 40, bytearray(100 * 40 * 4)))
            with self.assertRaises(rk.RingError):
                rk.state_frame(local, TWO, "idle")


class CommandTests(unittest.TestCase):
    def test_a_dry_run_writes_nothing_and_a_build_writes_parts_and_records(self):
        with tempfile.TemporaryDirectory() as folder:
            root = tree(folder)
            only = ("--local-root", str(root), "--only", "tiny_butt,mpcycleleftbutton")
            code, text = run("build", *only, "--dry-run")
            self.assertEqual(code, 0, text)
            self.assertFalse((root / "masters").exists())
            code, text = run("build", *only)
            self.assertEqual(code, 0, text)
            self.assertEqual(len(list((root / "masters").iterdir())), 7)
            record = json.loads((root / "provenance" / "rule" / "plate.tiny_butt_pressed.json").read_text(encoding="utf-8"))
            self.assertEqual((record["rule"], record["source"]), ("ring_kit", "pack"))
            self.assertEqual([(k, v["index_sha12"]) for k, v in record["sources"].items()], [("hd-packs/interface-redraw-4x/tiny_butt-f001", "abc123def451")])
            wedge = json.loads((root / "provenance" / "rule" / "plate.mpcycleleftbutton.json").read_text(encoding="utf-8"))
            self.assertEqual(sorted(wedge["sources"]), ["census/frames/757-MPCycleLeftButton/f0.png", "outbox/B-BUTTONS-RING/pm.png"])
            glyph = json.loads((root / "provenance" / "rule" / "glyph.tiny_butt.json").read_text(encoding="utf-8"))
            self.assertEqual((glyph["empty"], glyph["sources"]), (True, {}))
            self.assertEqual(run("check", *only)[0], 0)
            rk.sheet_path(root).unlink()
            self.assertEqual(run("check", *only)[0], 1)
            self.assertEqual(run("build", *only)[0], 2)

    def test_an_unknown_button_or_a_missing_folder_is_an_input_error(self):
        with tempfile.TemporaryDirectory() as folder:
            self.assertEqual(run("build", "--local-root", folder, "--only", "nothing")[0], 2)
            self.assertEqual(run("check", "--local-root", str(Path(folder) / "absent"))[0], 2)


if __name__ == "__main__":
    unittest.main()
