"""ARC-UI-3269: tests of the brass-ringed button, wide-button dome and indicator kit tool (tools/steamwoven_ui/ring_button_kit.py) on synthetic frames (no art)."""
from __future__ import annotations

import contextlib
import io
import json
import math
import tempfile
import unittest
from pathlib import Path

from . import pngio
from . import ring_button_kit as rk
from . import school_kit

INSIDE, OUTSIDE = (200, 30, 30), (30, 200, 30)
COLOURS = {0: INSIDE, 1: (30, 30, 200), 2: (200, 200, 30)}


def discs(b: rk.Button, scale: int, colour, outside=OUTSIDE, alpha: int = 255) -> pngio.Image:
    """A frame of the art at `scale` px per unit: the colour in the button's disc(s), the old background (transparent in an original frame when `alpha` is 0) outside."""
    w, h = b.circles[0].width * scale, b.circles[0].height * scale
    image = pngio.Image(w, h, bytearray(w * h * 4))
    for y in range(h):
        for x in range(w):
            inside = any(math.hypot((x + 0.5) / scale - c.cx, (y + 0.5) / scale - c.cy) <= c.cut for c in b.circles)
            image.data[(y * w + x) * 4:(y * w + x) * 4 + 4] = bytes((*(colour if inside else outside), 255 if inside else alpha))
    return image


SIGN_COLOURS = {"m_dnbut": (255, 0, 0), "m_upbut": (255, 0, 0), "combineindicator1": (200, 30, 30), "combineindicator2": (220, 220, 60), "combineindicator3": (100, 200, 90)}


def signed(b: rk.Button, scale: int, half: float, alpha: int = 255, checker: bool = False) -> pngio.Image:
    """Dark discs, each with a square sign of `half` units round its centre; `checker`: every other unit cell of the sign is dimmer (the original's dither)."""
    image = discs(b, scale, (10, 10, 4), alpha=alpha)
    colour = SIGN_COLOURS.get(b.name, (200, 30, 30))
    for c in b.circles:
        for y in range(image.h):
            for x in range(image.w):
                ux, uy = (x + 0.5) / scale, (y + 0.5) / scale
                if abs(ux - c.cx) <= half and abs(uy - c.cy) <= half:
                    dim = checker and (int(ux) + int(uy)) % 2
                    image.data[(y * image.w + x) * 4:(y * image.w + x) * 4 + 4] = bytes((*(tuple(v * 3 // 4 for v in colour) if dim else colour), 255))
    return image


def tree(folder: str, buttons) -> Path:
    """`<folder>/local/steamwoven-ui` with the pack frames (4x), the original frames and the olive plate; returns that folder."""
    root = Path(folder) / "local" / "steamwoven-ui"
    pack = root.parent / school_kit.PACK_DIR
    (pack / "interface").mkdir(parents=True)
    rows = ["screen\tkind\tart\tframe\tpath\tlegacy_w\tlegacy_h\thd_w\thd_h\tslice\talpha\treview\tsha12"]
    for b in buttons:
        frames = root / "census" / "frames" / b.folder
        frames.mkdir(parents=True)
        for number in b.frames.values():
            pngio.write(pack / "interface" / f"{b.name}-f{number:03d}.png", discs(b, 4, COLOURS[number]))
            lit = any(rk.uses_cells(b, s) and n == number for s, n in b.frames.items())
            pngio.write(frames / f"f{number}.png", signed(b, 1, 3.0, alpha=0, checker=True) if lit else discs(b, 1, COLOURS[number], alpha=0))
            rows.append(f"x\tbuttons\t{b.name}\t{number}\tinterface/{b.name}-f{number:03d}.png\t1\t1\t4\t4\ts\t\tunreviewed\tabc123def456")
    (pack / "index.tsv").write_text("\n".join(rows) + "\n", encoding="utf-8")
    (root / "masters").mkdir()
    face = pngio.Image(138, 138, bytearray(138 * 138 * 4))
    for y in range(138):
        for x in range(138):
            if math.hypot(x + 0.5 - 69, y + 0.5 - 69) <= 69:
                face.data[(y * 138 + x) * 4:(y * 138 + x) * 4 + 4] = bytes((60, 60, 20, 255))
    pngio.write(root / "masters" / f"{rk.OLIVE_PLATE}.png", face)
    for name in rk.BRASS_REFS:
        pngio.write(root / "masters" / f"{name}.png", pngio.Image(4, 4, bytearray((120, 90, 40, 255, 90, 65, 30, 255) * 8)))
    return root


def at(image: pngio.Image, x: int, y: int) -> tuple[int, ...]:
    return tuple(image.data[(y * image.w + x) * 4:(y * image.w + x) * 4 + 4])


class TableTests(unittest.TestCase):
    def test_a_button_has_a_part_per_frame_and_an_empty_glyph(self):
        self.assertEqual(rk.part_ids(rk.RINGED[0]), ["plate.bgrn_but", "glyph.bgrn_but", "plate.bgrn_but_pressed"])
        self.assertEqual(rk.part_ids(rk.RINGED[1]), ["plate.m_dnbut", "glyph.m_dnbut", "plate.m_dnbut_hover", "plate.m_dnbut_pressed"])
        self.assertEqual(rk.part_ids(rk.DOME), ["plate.combinebutt", "glyph.combinebutt", "plate.combinebutt_pressed"])
        self.assertEqual([rk.part_ids(b) for b in rk.INDICATORS], [[f"picture.combineindicator{n}"] for n in (1, 2, 3)])
        self.assertEqual(len(rk.coverage()), 17)

    def test_the_discs_lie_inside_their_arts(self):
        for b in rk.BUTTONS:
            for c in b.circles:
                self.assertGreaterEqual(min(c.cx - c.cut, c.cy - c.cut, c.width - c.cx - c.cut, c.height - c.cy - c.cut), -0.8, b.name)

    def test_every_part_is_a_master_row_of_the_manifest_with_the_tools_provenance(self):
        rows = {p["id"]: p for p in json.loads(rk.MANIFEST.read_text(encoding="utf-8"))["parts"]}
        for ident in rk.coverage():
            self.assertEqual(rows[ident]["status"], "master", ident)
            self.assertEqual(rows[ident]["provenance"], rk.provenance_of(ident), ident)


class CutTests(unittest.TestCase):
    def test_a_round_part_takes_nothing_of_the_old_background(self):
        b = rk.RINGED[1]
        master = rk.master_of(b)
        part = rk.cut(discs(b, 4, INSIDE), b.circles, master)
        for y in range(part.h):
            for x in range(part.w):
                r, g, _, a = at(part, x, y)
                if a > 0:
                    self.assertGreater(r, g, (x, y))
        self.assertEqual(at(part, 0, 0)[3], 0)
        self.assertEqual(at(part, part.w // 2, part.h // 2), (*INSIDE, 255))

    def test_the_two_wells_are_opaque_and_the_gap_is_clear(self):
        b = rk.INDICATORS[0]
        part = rk.cut(discs(b, 4, INSIDE), b.circles, rk.master_of(b))
        mid = part.h // 2
        self.assertEqual(at(part, 39, mid)[3], 255)
        self.assertEqual(at(part, part.w - 40, mid)[3], 255)
        self.assertTrue(all(at(part, x, y)[3] == 0 for x in range(90, part.w - 90) for y in range(part.h)))

    def test_the_olive_dome_fills_the_dome_circle(self):
        with tempfile.TemporaryDirectory() as folder:
            root = tree(folder, [rk.DOME])
            master = rk.master_of(rk.DOME)
            part = rk.olive_dome(pngio.read(root / "masters" / f"{rk.OLIVE_PLATE}.png"), rk.DOME.circles[0], master)
        self.assertEqual(at(part, 93, 93), (60, 60, 20, 255))
        self.assertEqual(at(part, 93, 2), (60, 60, 20, 255))      # the dome reaches the circle: nothing darker is mixed in at its edge
        self.assertEqual(at(part, 2, 2)[3], 0)


class CodexTests(unittest.TestCase):
    def well(self, b: rk.Button) -> pngio.Image:
        """A generated picture of a well: the sent square, the disc with a red left half and a blue right half, on the grey ground."""
        circle = rk.codex_circle(b)
        side = circle.width * school_kit.CODEX_SCALE
        image = pngio.Image(side, side, bytearray(side * side * 4))
        for y in range(side):
            for x in range(side):
                d = math.hypot((x + 0.5) / school_kit.CODEX_SCALE - circle.cx, (y + 0.5) / school_kit.CODEX_SCALE - circle.cy)
                colour = school_kit.CODEX_GROUND if d > circle.cut else (INSIDE if x < circle.cx * school_kit.CODEX_SCALE else (30, 30, 200))
                image.data[(y * side + x) * 4:(y * side + x) * 4 + 4] = bytes((*colour, 255))
        return image

    def test_the_right_well_is_mirrored_for_the_arrows_and_not_for_the_lamp(self):
        for b, right_left_half in ((rk.INDICATORS[0], INSIDE), (rk.INDICATORS[1], (30, 30, 200))):
            frame = rk.placed_art(self.well(b), b)
            self.assertEqual((frame.w, frame.h), (117 * rk.CODEX_FRAME_PX, 22 * rk.CODEX_FRAME_PX))
            y = frame.h // 2
            self.assertEqual(at(frame, 5 * rk.CODEX_FRAME_PX, y)[:3], INSIDE, b.name)
            self.assertEqual(at(frame, int((b.circles[1].cx - 6) * rk.CODEX_FRAME_PX), y)[:3], right_left_half, b.name)

    def test_a_ringed_button_goes_out_square_and_comes_back_on_its_art(self):
        b = rk.RINGED[0]
        circle = rk.codex_circle(b)
        self.assertEqual(circle.width, circle.height)
        sent = school_kit.staged_input(rk.codex_square(discs(b, 4, INSIDE), b), circle)
        frame = rk.placed_art(sent, b)
        self.assertEqual((frame.w, frame.h), (37 * rk.CODEX_FRAME_PX, 36 * rk.CODEX_FRAME_PX))
        self.assertEqual(at(frame, frame.w // 2, frame.h // 2)[:3], INSIDE)
        self.assertEqual(at(frame, 0, 0)[:3], school_kit.CODEX_GROUND)

    def test_the_prompts_are_ascii_and_name_the_state(self):
        for b in rk.BUTTONS:
            if b.name in rk.SUBJECTS:
                for state in b.frames:
                    rk.codex_prompt(b, state).encode("ascii")
        self.assertIn("PRESSED", rk.codex_prompt(rk.RINGED[1], "pressed"))
        self.assertNotIn(rk.DOME.name, rk.SUBJECTS)

    def test_the_inbox_holds_one_item_per_state_with_hashed_references(self):
        with tempfile.TemporaryDirectory() as folder:
            root = tree(folder, rk.BUTTONS)
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(rk.main(["codex-inbox", "--stage", "idle", "--local-root", str(root)]), 0)
            batch = json.loads((root / "inbox" / rk.CODEX_BATCH / "batch.json").read_text(encoding="utf-8"))
        self.assertEqual([i["id"] for i in batch["items"]], [f"ring-{b.name}-idle" for b in rk.BUTTONS if b != rk.DOME])
        self.assertTrue(all(len(r["sha256"]) == 64 for i in batch["items"] for r in i["references"]))


class BrassTests(unittest.TestCase):
    def test_lab_goes_back_to_the_colour(self):
        for colour in ((0, 0, 0), (255, 255, 255), (180, 130, 40), (20, 22, 8)):
            back = rk.rgb_of(*rk.button_kit.lab(*colour))
            self.assertTrue(all(abs(a - b) <= 1 for a, b in zip(back, colour)), (colour, back))

    def test_the_ring_takes_the_reference_tone_and_the_dome_keeps_its_own(self):
        b = rk.RINGED[0]
        frame = discs(b, 4, (230, 180, 40))
        target = rk.stats_of([rk.button_kit.lab(120, 90, 40)])
        out = rk.toned(frame, b.circles[0], rk.ring_stats(frame, b.circles[0]), target)
        c = b.circles[0]
        middle = at(out, int(c.cx * 4), int(c.cy * 4))
        ring = at(out, int((c.cx + c.cut * 0.93) * 4), int(c.cy * 4))
        self.assertEqual(middle[:3], (230, 180, 40))
        self.assertTrue(all(abs(a - e) <= 2 for a, e in zip(ring[:3], (120, 90, 40))), ring)


class CellTests(unittest.TestCase):
    def test_a_wells_sign_takes_the_originals_cells_and_share(self):
        b = rk.INDICATORS[1]
        scale = rk.CODEX_FRAME_PX
        original = signed(b, 1, 3.0, alpha=0, checker=True)
        out = rk.celled(signed(b, scale, 5.0), original, b)
        for circle in b.circles:
            cells = rk.sign_cells(original, circle, rk.SIGNS[b.name][0], rk.SIGN_REACH[b.kind])
            x0, y0, x1, y1 = rk.box_of(cells)
            for cx, cy in ((x0, y0), (x0 + 1, y0), (x1 - 1, y1 - 1)):
                self.assertEqual(at(out, int((cx + 0.5) * scale), int((cy + 0.5) * scale))[:3], cells[(cx, cy)], (cx, cy))
            self.assertNotEqual(cells[(x0, y0)], cells[(x0 + 1, y0)])                      # the dither survives
            self.assertEqual(at(out, int((x0 - 1.0) * scale), int(circle.cy * scale))[:3], (10, 10, 4))   # the larger generated sign is painted out

    def test_a_buttons_sign_keeps_the_generated_outline(self):
        b = rk.RINGED[1]
        scale = rk.CODEX_FRAME_PX
        out = rk.celled(signed(b, scale, 4.0), signed(b, 1, 3.0, alpha=0, checker=True), b)
        c = b.circles[0]
        self.assertIn(at(out, int((c.cx - 3.8) * scale), int(c.cy * scale))[:3], ((255, 0, 0), (191, 0, 0)))
        self.assertEqual(at(out, int((c.cx - 5.0) * scale), int(c.cy * scale))[:3], (10, 10, 4))

    def test_the_pale_corner_of_a_red_signs_bevel_is_left_out(self):
        cells = {(x, y): (200, 0, 0) for x in range(3) for y in range(3)}
        cells[(1, 1)] = cells[(2, 2)] = (255, 150, 150)
        kept = rk.without_pale_rim(cells)
        self.assertIn((1, 1), kept)          # the highlight in the middle stays
        self.assertNotIn((2, 2), kept)
        self.assertFalse(rk.RED_SIGN[0](176, 86, 73))      # the orange bevel is no sign cell

    def test_only_burning_signs_and_the_lamp_take_cells(self):
        self.assertTrue(rk.uses_cells(rk.RINGED[1], "hover") and rk.uses_cells(rk.INDICATORS[0], "idle"))
        self.assertFalse(rk.uses_cells(rk.RINGED[1], "idle") or rk.uses_cells(rk.RINGED[0], "pressed") or rk.uses_cells(rk.DOME, "idle"))


class BuildTests(unittest.TestCase):
    def test_the_pack_build_writes_every_part_and_its_record(self):
        with tempfile.TemporaryDirectory() as folder:
            root = tree(folder, rk.BUTTONS)
            out = Path(folder) / "out"
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(rk.main(["build", "--source", "pack", "--dome", "olive", "--local-root", str(root), "--out", str(out)]), 0)
            for ident, b in rk.coverage().items():
                image = pngio.read(out / f"{ident}.png")
                self.assertEqual((image.w, image.h), rk.master_of(b), ident)
                record = json.loads((out / "records" / f"{ident}.json").read_text(encoding="utf-8"))
                self.assertEqual(record["empty"], ident.startswith("glyph."), ident)

    def test_the_default_build_tones_the_ring_and_records_what_it_used(self):
        with tempfile.TemporaryDirectory() as folder:
            root = tree(folder, rk.BUTTONS)
            _, outbox, placed = rk.codex_dirs(root)
            placed.mkdir(parents=True)
            outbox.mkdir(parents=True)
            for b in rk.BUTTONS:
                if b == rk.DOME:
                    continue
                for state, number in b.frames.items():
                    pngio.write(placed / f"{b.name}-f{number}.png", signed(b, rk.CODEX_FRAME_PX, 5.0) if rk.uses_cells(b, state) else discs(b, rk.CODEX_FRAME_PX, (230, 180, 40)))
                    pngio.write(outbox / f"{rk.codex_item(b, state)}.png", discs(b, 1, (230, 180, 40)))
            out = Path(folder) / "out"
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(rk.main(["check", "--local-root", str(root)]), 0)
                self.assertEqual(rk.main(["build", "--local-root", str(root), "--out", str(out)]), 0)
            b = rk.RINGED[1]
            part = pngio.read(out / "plate.m_dnbut.png")
            self.assertEqual(at(part, part.w // 2, part.h // 2)[:3], (230, 180, 40))
            self.assertLess(at(part, int((b.circles[0].cx + b.circles[0].cut * 0.93) * part.w / 38), part.h // 2)[0], 160)      # the ring took the references' darker brass
            hover = json.loads((out / "records" / "plate.m_dnbut_hover.json").read_text(encoding="utf-8"))
            dome = json.loads((out / "records" / "plate.combinebutt.json").read_text(encoding="utf-8"))
            (root / "masters" / f"{rk.BRASS_REFS[1]}.png").unlink()
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(rk.main(["check", "--local-root", str(root)]), 2)
        self.assertEqual(hover["source"], "codex")
        self.assertIn(f"work/{rk.CODEX_BATCH}/placed/m_dnbut-f0", hover["sources"])
        self.assertEqual((dome["source"], dome["parameters"]["dome"]), ("pack", "pack"))

    def test_the_codex_build_refuses_without_placed_frames(self):
        with tempfile.TemporaryDirectory() as folder:
            root = tree(folder, rk.BUTTONS)
            with contextlib.redirect_stdout(io.StringIO()) as text:
                self.assertEqual(rk.main(["build", "--source", "codex", "--local-root", str(root), "--dry-run"]), 2)
        self.assertIn("no placed Codex frame", text.getvalue())


if __name__ == "__main__":
    unittest.main()
