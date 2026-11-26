"""ARC-UI-3270: tests of the small brass-ringed button kit tool on synthetic pictures (no art): the table, the brass tone, each family's face, the sheet of the generated set and the commands."""
from __future__ import annotations

import contextlib
import hashlib
import io
import json
import math
import tempfile
import unittest
from pathlib import Path

from . import marker_kit as mk
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


def generated(path: Path, picture: pngio.Image) -> None:
    """A generated picture and the record the batch runner leaves beside it."""
    path.parent.mkdir(parents=True, exist_ok=True)
    pngio.write(path, picture)
    path.with_suffix(".provenance.json").write_text("{}", encoding="utf-8")


def tree(folder: str, bar_shift: int = 7) -> Path:
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
    generated(rk.sheet_path(root), generated_sheet(bar_shift))
    markers = pngio.Image(192, 128, bytearray(bytes((255, 0, 255, 255)) * (192 * 128)))
    for index, colour in enumerate(((240, 240, 240), (200, 220, 90), (240, 240, 240), (240, 240, 240))):   # the cells in `marker_kit.SHEET_ORDER`
        column, row = index % 3, index // 3
        box(markers, column * 64 + 16, row * 64 + 12, column * 64 + 48, row * 64 + 52, colour)
    generated(rk.marker_sheet(root, "mmb_chest"), markers)
    skull = pngio.Image(64, 64, bytearray(bytes((255, 0, 255, 255)) * (64 * 64)))
    box(skull, 8, 8, 56, 56, (250, 250, 250))
    generated(rk.marker_sheet(root, "mmb_skull"), skull)
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

    def test_a_message_buttons_symbol_is_its_markers_drawing(self):
        self.assertEqual([rk.marker_name(r.name) for r in rk.RINGS if r.method == "marker"], list(mk.SHEET_ORDER[:4]))
        with tempfile.TemporaryDirectory() as folder:
            root = tree(folder)
            symbol = rk.marker_symbol(root, "mmb_note")                                   # the second cell of the sheet, its ground keyed out
            self.assertEqual((symbol.w, symbol.h, pixel(symbol, 0, 0)), (32, 40, (200, 220, 90, 255)))
            self.assertEqual(pixel(rk.marker_symbol(root, "mmb_skull"), 0, 0)[:3], (250, 250, 250))   # the redrawn skull, not the sheet's cell
            self.assertEqual(rk.marker_sheet(root, "mmb_skull").name, "skull-neutral.png")

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


class ArrowStateTests(unittest.TestCase):
    def test_a_checkered_idle_and_a_glowing_state_keep_one_outline(self):
        ring = rk.ring_named("sm_rightarrow")
        checker = {k: (150, 0, 30) if sum(k) % 2 else (124, 0, 25) for k in ARROW_CELLS}
        lit = {**{k: (150, 0, 25) for k in ARROW_CELLS}, **{k: (255, 90, 90) for k in ARROW_CELLS if 8 <= k[0] <= 14}}
        with tempfile.TemporaryDirectory() as folder:
            root = tree(folder)
            frames = root / "census" / "frames"
            for number, cells in ((0, checker), (1, lit)):
                pngio.write(frames / ring.folder / f"f{number}.png", original(ring, cells))
            plate = rk.empty_plate(root, ring)
            faces = [rk.face_of(root, frames, ring, state) for state in ("idle", "pressed")]
            for x, y in ((7.5, 11.5), (12.5, 7.5), (17.9, 11.5), (13.5, 14.5)):   # the shaft's end, the head's top, the tip, the head's lower side
                self.assertTrue(rk.fit_arrow(checker).holds(x, y), (x, y))
                for face in faces:
                    self.assertGreater(at(ring, face, x, y)[0], at(ring, plate, x, y)[0] + 60, (x, y))


class FaceTests(unittest.TestCase):
    def test_every_plus_and_minus_stands_on_the_idle_minus_ring(self):
        with tempfile.TemporaryDirectory() as folder:
            root = tree(folder)
            frames = root / "census" / "frames"
            minus, plus = rk.ring_named("char_minus"), rk.ring_named("char_htftplus")
            a, b = rk.face_of(root, frames, minus, "idle"), rk.face_of(root, frames, plus, "hover")
            ring_a, ring_b = at(minus, a, minus.circle.cx - 0.9 * minus.circle.cut, minus.circle.cy), at(plus, b, plus.circle.cx - 0.9 * plus.circle.cut, plus.circle.cy)
            self.assertLess(max(abs(x - y) for x, y in zip(ring_a, ring_b)), 6)       # the sheet's blue Plus ring is never used
            self.assertGreater(ring_b[0], ring_b[2])

    def test_a_bar_drawn_far_beside_the_middle_still_leaves_the_plate_clean(self):
        with tempfile.TemporaryDirectory() as folder:
            root = tree(folder, bar_shift=17)                                          # past the bar's box unless the dome is centred first
            ring = rk.ring_named("char_minus")
            c = ring.circle
            plate = rk.empty_plate(root, ring)
            reds = [(i % plate.w, i // plate.w) for i in range(plate.w * plate.h)
                    if plate.data[i * 4] > 90 and plate.data[i * 4 + 1] < 40 and math.hypot((i % plate.w + 0.5) * c.width / plate.w - c.cx, (i // plate.w + 0.5) * c.height / plate.h - c.cy) < 0.7 * c.cut]
            self.assertEqual(reds, [])

    def test_one_thick_stroke_for_every_plus_and_minus_and_a_pressed_halo_tight_to_it(self):
        with tempfile.TemporaryDirectory() as folder:
            root = tree(folder)
            frames = root / "census" / "frames"
            for name in ("char_minus", "char_htftminus", "char_plus"):
                ring = rk.ring_named(name)
                c = ring.circle
                idle, hover, pressed = (rk.face_of(root, frames, ring, s) for s in ("idle", "hover", "pressed"))
                length, thickness = rk.sign_size(c)
                self.assertAlmostEqual(thickness / (2 * rk.INNER["codex"] * c.cut), 0.115)
                self.assertAlmostEqual(at(ring, idle, c.cx, c.cy)[0], 163, delta=8)
                self.assertGreater(at(ring, hover, c.cx, c.cy)[0], 240)
                self.assertGreater(at(ring, pressed, c.cx, c.cy)[1], 85)                       # the pink of the pressed sign
                inside, outside = (c.cx + 0.3 * length, c.cy + 0.35 * thickness), (c.cx + 0.3 * length, c.cy + 0.5 * thickness + 0.7)
                self.assertGreater(at(ring, hover, *inside)[0], 240)                           # the stroke is that thick
                self.assertEqual(at(ring, hover, *outside), at(ring, idle, *outside))          # nothing but the sign changes on hover
                self.assertGreater(at(ring, pressed, *outside)[0], at(ring, idle, *outside)[0] + 40)   # the red halo beside a pressed sign
                far = (c.cx + 0.45 * c.cut, c.cy + 0.45 * c.cut)
                self.assertLessEqual(at(ring, pressed, *far)[0], at(ring, idle, *far)[0])      # the dome further out stays dark
                upright = (c.cx, c.cy + 0.4 * length)
                self.assertEqual(at(ring, hover, *upright)[0] > 240, "plus" in name)

    def test_the_symbol_buttons_stand_on_the_socket_dome_and_the_idle_symbol_is_dimmed(self):
        with tempfile.TemporaryDirectory() as folder:
            root = tree(folder)
            frames = root / "census" / "frames"
            ring = rk.ring_named("mmb_chest")
            idle, lit = rk.face_of(root, frames, ring, "idle"), rk.face_of(root, frames, ring, "pressed")
            x0, y0, x1, y1 = rk.SYMBOL_BOXES[ring.name]
            dx, dy = rk.symbol_shift(ring)
            middle = ((x0 + x1) / 2 + dx, (y0 + y1) / 2 + dy)
            self.assertEqual(at(ring, lit, *middle)[:3], (240, 240, 240))
            self.assertAlmostEqual(at(ring, idle, *middle)[0], 240 * rk.IDLE_DIM[ring.name], delta=2)
            plate = rk.dome_plate(root, ring)
            centre = at(ring, plate, ring.circle.cx, ring.circle.cy)
            self.assertLess(centre[0], 60)                                             # the bar is gone from the plate
            arrow = rk.ring_named("mpcyclerightbutton")
            self.assertGreater(at(arrow, rk.face_of(root, frames, arrow, "idle"), 11, 11.5)[0], 150)

    def test_a_socket_dome_has_no_ring_and_fills_the_inside_of_the_sockets_ring(self):
        domes = [r for r in rk.RINGS if r.method in ("marker", "wedge")]
        self.assertEqual({(r.circle.cx, r.circle.cy, round(r.circle.cut, 3)) for r in domes}, {(11.5, 11.5, rk.SOCKET_DOME_CUT)})
        with tempfile.TemporaryDirectory() as folder:
            root = tree(folder)
            for ring in domes:
                c = ring.circle
                plate = rk.dome_plate(root, ring)
                for dx, dy in ((-1, 0), (1, 0), (0, -1), (0, 1)):
                    inside = at(ring, plate, c.cx + 0.95 * c.cut * dx, c.cy + 0.95 * c.cut * dy)
                    self.assertEqual(inside[3], 255, ring.name)
                    self.assertLess(inside[0], 60, ring.name)                          # the dome to its edge: no brass
                    self.assertEqual(at(ring, plate, c.cx + 1.03 * c.cut * dx, c.cy + 1.03 * c.cut * dy)[3], 0, ring.name)

    def test_a_socket_domes_symbol_keeps_its_place_on_the_originals_dome(self):
        with tempfile.TemporaryDirectory() as folder:
            root = tree(folder)
            frames = root / "census" / "frames"
            for name, state in (("mmb_chest", "pressed"), ("mmb_skull", "pressed"), ("mpcyclerightbutton", "idle")):
                ring = rk.ring_named(name)
                c = ring.circle
                face, plate = rk.face_of(root, frames, ring, state), rk.dome_plate(root, ring)
                drawn = [(i % face.w, i // face.w) for i in range(face.w * face.h) if max(abs(face.data[i * 4 + k] - plate.data[i * 4 + k]) for k in range(3)) > 60]
                middle = ((min(x for x, _ in drawn) + max(x for x, _ in drawn) + 1) / 2 * c.width / face.w, (min(y for _, y in drawn) + max(y for _, y in drawn) + 1) / 2 * c.height / face.h)
                if ring.method == "marker":
                    x0, y0, x1, y1 = rk.SYMBOL_BOXES[name]
                    original_middle = ((x0 + x1) / 2, (y0 + y1) / 2)
                else:
                    original_middle = ((9 + 15) / 2, 11.5)                    # the synthetic original's triangle: columns 9 to 14, rows 5 to 17
                x, y = rk.ORIGINAL_DOME[ring.art]
                for axis, (got, was, dome, centre) in enumerate(zip(middle, original_middle, (x, y), (c.cx, c.cy))):
                    self.assertAlmostEqual(got - centre, was - dome, delta=0.2, msg=f"{name} axis {axis}")

    def test_a_missing_input_is_a_ring_error(self):
        with tempfile.TemporaryDirectory() as folder:
            root = tree(folder)
            arrow = rk.ring_named("sm_rightarrow")
            pngio.write(root / "census" / "frames" / arrow.folder / "f1.png", original(arrow, {}))
            with self.assertRaises(rk.RingError):
                rk.face_of(root, root / "census" / "frames", arrow, "pressed")            # a state with no red cells
            rk.marker_sheet(root, "mmb_chest").unlink()
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
            plate = ["outbox/B-BUTTONS-RING/pm.png", "outbox/B-BUTTONS-RING/pm.provenance.json"]
            self.assertEqual(sorted(wedge["sources"]), ["census/frames/757-MPCycleLeftButton/f0.png", *plate])
            self.assertEqual(run("build", "--local-root", str(root), "--only", "mmb_skull")[0], 0)
            skull = json.loads((root / "provenance" / "rule" / "plate.mmb_skull.json").read_text(encoding="utf-8"))
            self.assertEqual(sorted(skull["sources"]), ["outbox/B-BUTTONS-3207-markers/skull-neutral.png", "outbox/B-BUTTONS-3207-markers/skull-neutral.provenance.json", *plate])
            glyph = json.loads((root / "provenance" / "rule" / "glyph.tiny_butt.json").read_text(encoding="utf-8"))
            self.assertEqual((glyph["empty"], glyph["sources"]), (True, {}))
            self.assertEqual(run("check", *only)[0], 0)
            record_beside = rk.sheet_path(root).with_suffix(".provenance.json")
            record_beside.unlink()
            self.assertEqual((run("check", *only)[0], run("build", *only)[0]), (1, 2))     # a picture without its record is a missing input
            record_beside.write_text("{}", encoding="utf-8")
            rk.sheet_path(root).unlink()
            self.assertEqual(run("check", *only)[0], 1)
            self.assertEqual(run("build", *only)[0], 2)

    def test_the_inbox_holds_the_one_sheet_with_its_hashes(self):
        with tempfile.TemporaryDirectory() as folder:
            root = tree(folder)
            pack = sk.pack_local(root.parent.parent / "local" / "steamwoven-ui") / sk.PACK_DIR
            rows = (pack / "index.tsv").read_text(encoding="utf-8")
            for ring in (rk.ring_named(n) for n in rk.SHEET_NAMES):
                for state in rk.SHEET_STATES:
                    number = rk.STATE_FRAMES[state]
                    pngio.write(pack / "interface" / f"{ring.name}-f{number:03d}.png", pack_frame(ring))
                    rows += f"x\tbuttons\t{ring.name}\t{number}\tinterface/{ring.name}-f{number:03d}.png\t1\t1\t1\t1\ts\tmasked\tunreviewed\tabc123def45{number}\n"
                    path = root / "census" / "frames" / ring.folder / f"f{number}.png"
                    path.parent.mkdir(parents=True, exist_ok=True)
                    pngio.write(path, original(ring, {}))
            (pack / "index.tsv").write_text(rows, encoding="utf-8")
            code, text = run("codex-inbox", "--local-root", str(root))
            self.assertEqual(code, 0, text)
            inbox = root / "inbox" / "B-BUTTONS-RING"
            batch = json.loads((inbox / "batch.json").read_text(encoding="utf-8"))
            self.assertEqual([i["id"] for i in batch["items"]], ["pm"])
            item = batch["items"][0]
            self.assertEqual(item["prompt_sha256"], hashlib.sha256((inbox / item["prompt_file"]).read_bytes()).hexdigest())
            self.assertEqual([(r["slot"], r["sha256"]) for r in item["references"]], [(r["slot"], hashlib.sha256((inbox / r["file"]).read_bytes()).hexdigest()) for r in item["references"]])
            self.assertEqual(item["outbox_file"], "outbox/B-BUTTONS-RING/pm.png")

    def test_an_unknown_button_or_a_missing_folder_is_an_input_error(self):
        with tempfile.TemporaryDirectory() as folder:
            self.assertEqual(run("build", "--local-root", folder, "--only", "nothing")[0], 2)
            self.assertEqual(run("check", "--local-root", str(Path(folder) / "absent"))[0], 2)


class ViewerTableTests(unittest.TestCase):
    def test_the_viewers_ringed_buttons_are_this_tools_arts_with_their_circles(self):
        """ARC-UI-3347: `SteamwovenKitTable.RingFaces` names the plate of each ringed button outside the sheet's sockets, and `AreaKitRoundParts` holds its circle, as this tool's table does."""
        import re
        source = (sk.HERE.parents[1] / "tools" / "terrain-viewer" / "Core" / "SteamwovenKitComposer.cs").read_text(encoding="utf-8")
        block = source[source.index("RingFaces { get; }"):]
        block = block[:block.index("};")]
        viewer = {int(art): name for art, name in re.findall(r'\[(\d+)\] = "plate\.([a-z_]+)"', block)}
        rings = {ring.art: ring for ring in rk.RINGS if ring.method != "codex"}     # 23, 24, 770, 771 stand in the sheet's sockets
        self.assertEqual({art: ring.name for art, ring in rings.items()}, viewer)
        table = sk.ROUND_PARTS_SOURCE.read_text(encoding="utf-8")
        rows = {int(a): (int(w), int(h), float(cx), float(cy), float(r)) for a, w, h, cx, cy, r in
                re.findall(r"\[(\d+)\] = new\((\d+), (\d+), ([0-9.]+)f, ([0-9.]+)f, ([0-9.]+)f\)", table)}
        for art, ring in rings.items():
            c = ring.circle
            self.assertEqual(rows[art], (c.width, c.height, c.cx, c.cy, c.radius), ring.name)


if __name__ == "__main__":
    unittest.main()
