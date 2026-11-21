"""ARC-DEV-2454: kit manifest validator and builder on synthetic manifests, and the real manifest against the census (no pixels)."""
from __future__ import annotations

import contextlib
import copy
import io
import json
from pathlib import Path
import tempfile
import unittest

from tools.steamwoven_ui import build_kit, kit_check
from tools.steamwoven_ui import census as cn


def window(anchor: str = "zone") -> dict:
    return {"name": "Inventory", "anchor": anchor}


def part(ident: str, kind: str = "picture", **over) -> dict:
    row = {"id": ident, "kind": kind, "size": [60, 60], "size_basis": "units", "anchor": "window", "layers": [], "states": [],
           "used_by": ["G01"], "source_arts": [], "family": None, "batch": "B-ICONS", "provenance": None, "status": "planned", "frames": 1}
    row.update(over)
    return row


def valid() -> dict:
    return {"schema": 1, "windows": {"G01": window(), "H01": window("top-strip")}, "excluded": {"font": ["27"]},
            "parts": [
                part("picture.sword", source_arts=["10"]),
                part("board.int_top", "board", size=[2880, 150], size_basis="provisional", anchor="top-strip", used_by=["H01"],
                     source_arts=["185"], family="BOARD", batch="B-BOARD", layers=["cap.top_strip_left"]),
                part("cap.top_strip_left", "cap", size=None, size_basis="open", anchor="top-strip-left-end", used_by=["H01"],
                     family="BOARD", batch="B-BOARD"),
                part("backdrop.inventor", "backdrop", size=[2880, 1440], anchor="zone", source_arts=["221"], family="BD-A", batch="BD-A"),
                part("backdrop.main_menu", "backdrop", size=[2880, 2160], anchor="composition", source_arts=["329"], family="BD-H",
                     batch="BD-H"),
                part("plate.ok_but", "plate", size=[132, 132], states=["idle", "hover"], source_arts=["33"], batch="B-BUTTONS"),
                part("material.wood", "material", size=None, size_basis="open", anchor="none", used_by=[], batch=None),
            ]}


def census_arts() -> list[cn.CensusArt]:
    def art(ident, name, kind, batch, size, windows, status="drawn"):
        return cn.CensusArt(ident, name, size, 1, kind, batch, tuple(windows), status)
    return [art("10", "sword", "picture", "B-ICONS", (16, 16), ["G01"]), art("185", "IntTop", "board", "B-BOARD", (800, 41), ["H01"]),
            art("221", "Inventor", "backdrop", "BD-A", (800, 400), ["G01"]), art("329", "MainMenuBack", "backdrop", "BD-H", (800, 600), ["G01"]),
            art("33", "done_big", "plate", "B-BUTTONS", (37, 37), ["G01"]), art("27", "font", "font", "", (16, 16), [], "font (not a kit part)")]


def census_windows() -> list[cn.CensusWindow]:
    return [cn.CensusWindow("G01", "Inventory", "play-view panel 800x400", 5), cn.CensusWindow("H01", "Top strip", "top strip 800x41", 3)]


class SyntheticManifestTests(unittest.TestCase):
    def check(self, manifest, census=True) -> list[str]:
        return kit_check.check_manifest(manifest, census_arts() if census else None, census_windows() if census else None)

    def assertFails(self, manifest, fragment: str, census=False) -> None:
        errors = self.check(manifest, census)
        self.assertTrue(any(fragment in e for e in errors), f"{fragment!r} not in {errors}")

    def test_valid_manifest_passes(self) -> None:
        self.assertEqual(self.check(valid(), census=False), [])
        self.assertEqual(self.check(valid()), [])

    def edit(self, index: int, **over) -> dict:
        manifest = copy.deepcopy(valid())
        manifest["parts"][index].update(over)
        return manifest

    def test_duplicate_id(self) -> None:
        manifest = valid()
        manifest["parts"].append(copy.deepcopy(manifest["parts"][0]))
        manifest["parts"][-1]["source_arts"] = ["11"]
        self.assertFails(manifest, "duplicate id")

    def test_unknown_kind_and_cap_is_known(self) -> None:
        self.assertFails(self.edit(0, kind="board_full"), "unknown kind")
        self.assertIn("cap", kit_check.KINDS)

    def test_id_must_start_with_kind(self) -> None:
        self.assertFails(self.edit(0, id="gauge.sword"), "must start with its kind")

    def test_master_size_multiple_of_six(self) -> None:
        self.assertFails(self.edit(0, size=[61, 60]), "multiple of 6")
        self.assertFails(self.edit(0, size=[60, 0]), "multiple of 6")
        self.assertFails(self.edit(0, size=[60]), "size must be")
        self.assertFails(self.edit(0, size=[True, 60]), "size must be")

    def test_open_size_rules(self) -> None:
        self.assertFails(self.edit(0, size_basis="open"), "open size must be null")
        self.assertFails(self.edit(0, size=None), "size must be")

    def test_larger_than_composition(self) -> None:
        self.assertFails(self.edit(0, size=[3840, 60]), "larger than the composition")

    def test_strip_is_not_full_width_board(self) -> None:
        self.assertFails(self.edit(1, size=[3840, 150]), "larger than the composition")
        self.assertFails(self.edit(1, size=[2400, 150]), "2880 px wide")

    def test_backdrop_is_window_background_or_plate(self) -> None:
        self.assertFails(self.edit(3, size=[2880, 1800]), "window background")
        self.assertFails(self.edit(3, size=[3840, 1440]), "larger than the composition")
        self.assertFails(self.edit(4, anchor="zone"), "plate anchors to the composition")
        self.assertFails(self.edit(3, anchor="composition"), "window background to the zone")

    def test_cap_rules(self) -> None:
        self.assertFails(self.edit(2, anchor="top-strip"), "cap stands at a strip's end")
        self.assertFails(self.edit(2, source_arts=["185"]), "no source art")
        self.assertFails(self.edit(0, anchor="top-strip-left-end"), "only a cap")

    def test_board_anchor(self) -> None:
        self.assertFails(self.edit(1, anchor="zone"), "top or bottom strip")

    def test_layers_name_existing_parts(self) -> None:
        self.assertFails(self.edit(1, layers=["cap.nothing"]), "names no part")
        self.assertFails(self.edit(1, layers=["board.int_top"]), "lists itself")

    def test_layer_cycle(self) -> None:
        manifest = self.edit(1, layers=["cap.top_strip_left"])
        manifest["parts"][2]["layers"] = ["board.int_top"]
        self.assertFails(manifest, "make a cycle")

    def test_used_by_known_windows(self) -> None:
        self.assertFails(self.edit(0, used_by=["G99"]), "unknown window")

    def test_states_known(self) -> None:
        self.assertFails(self.edit(5, states=["idle", "glowing"]), "unknown state")
        self.assertFails(self.edit(5, states=["idle", "idle"]), "duplicate")

    def test_family_batch_status_provenance(self) -> None:
        self.assertFails(self.edit(0, family="BD-Z"), "unknown family")
        self.assertFails(self.edit(0, batch="B-NOPE"), "unknown batch")
        self.assertFails(self.edit(0, batch=None), "only a material")
        self.assertFails(self.edit(0, status="shipped"), "status must be")
        self.assertFails(self.edit(0, provenance="rec1"), "no provenance record yet")
        self.assertFails(self.edit(0, status="master"), "needs its provenance record id")
        self.assertEqual(self.check(self.edit(0, status="master", provenance="rec1"), census=False), [])

    def test_missing_and_unknown_keys(self) -> None:
        manifest = valid()
        del manifest["parts"][0]["anchor"]
        self.assertFails(manifest, "missing key 'anchor'")
        self.assertFails(self.edit(0, pixels="x"), "unknown key 'pixels'")
        manifest = valid()
        manifest["extra"] = 1
        self.assertFails(manifest, "unknown key 'extra'")
        self.assertFails({"schema": 1}, "missing key 'parts'")
        self.assertFails([], "not an object")

    def test_no_pixels(self) -> None:
        self.assertFails(self.edit(0, note="x" * 500), "looks like data")
        self.assertFails(self.edit(0, note="data:image/png;base64,AAAA"), "looks like data")

    def test_source_art_identifiers(self) -> None:
        self.assertFails(self.edit(0, source_arts=["art 10"]), "not an art number")
        manifest = valid()
        manifest["parts"][5]["source_arts"] = ["10"]
        self.assertFails(manifest, "one art is one part")

    def test_malformed_types_are_reported_not_raised(self) -> None:
        self.assertFails(self.edit(0, used_by=[["H01"]]), "used_by must be a list of text")
        self.assertFails(self.edit(1, layers=[["x"]]), "layers must be a list of text")
        self.assertFails(self.edit(0, source_arts=[["10"]]), "source_arts must be a list of text")
        self.assertFails(self.edit(0, states=[3]), "states must be a list of text")
        manifest = valid()
        manifest["schema"] = True
        self.assertFails(manifest, "schema must be 1")
        manifest["schema"] = 1.0
        self.assertFails(manifest, "schema must be 1")

    def test_long_layer_chain_is_checked_without_recursion(self) -> None:
        manifest = valid()
        count = 3000
        for index in range(count):
            manifest["parts"].append(part(f"decor.d{index}", "decor", size=None, size_basis="open", anchor="none", used_by=[], batch="B-HARDWARE",
                                          layers=[f"decor.d{index + 1}"] if index + 1 < count else ["decor.d0"]))
        self.assertFails(manifest, "make a cycle")

    def test_open_size_only_without_source_art(self) -> None:
        self.assertFails(self.edit(1, size=None, size_basis="open"), "from original art has a size")
        self.assertFails(self.edit(0, size=None, size_basis="open"), "from original art has a size")
        self.assertFails(self.edit(2, size=[600, 150], size_basis="units"), "new part (no source art)")
        self.assertFails(self.edit(0, size_basis="provisional"), "only the strips' heights are provisional")

    def test_part_without_source_art_must_be_a_new_part(self) -> None:
        manifest = valid()
        manifest["parts"].append(part("picture.ghost", size=None, size_basis="open"))
        self.assertFails(manifest, "must be a new part")

    def test_plate_and_glyph_without_source_art_are_new_parts(self) -> None:
        # the shared round plate and the button glyphs of the pilot ARC-UI-3058 feed no original art: an open size, a batch, no source art
        manifest = valid()
        manifest["parts"].append(part("plate.round", "plate", size=None, size_basis="open", states=["idle"], batch="B-BUTTONS"))
        manifest["parts"].append(part("glyph.key", "glyph", size=None, size_basis="open", states=["hover"], batch="B-BUTTONS"))
        self.assertEqual(self.check(manifest, census=False), [])
        self.assertFails(self.edit(5, source_arts=["33"], size=None, size_basis="open"), "from original art has a size")

    def test_pilot_parts_are_masters_with_a_fit_record(self) -> None:
        parts = {p["id"]: p for p in kit_check.load_manifest()["parts"]}
        for ident in ("plate.round_button", "plate.round_button_pressed", "glyph.key", "glyph.key_lit", "glyph.gear", "glyph.gear_lit", "glyph.arrows", "glyph.arrows_lit", "glyph.shield", "glyph.shield_lit"):
            self.assertEqual((parts[ident]["status"], parts[ident]["provenance"], parts[ident]["source_arts"]), ("master", f"fit:{ident}", []), ident)

    def test_cap_agrees_with_its_id_strip_and_window(self) -> None:
        self.assertFails(self.edit(2, anchor="bottom-strip-right-end"), "to match its id")
        self.assertFails(self.edit(2, used_by=["G01"]), "used by the H01 strip window alone")
        self.assertFails(self.edit(2, id="cap.middle_strip"), "a cap id is")

    def test_strip_heights(self) -> None:
        self.assertFails(self.edit(1, size=[2880, 6]), "150 (top) or 570 (bottom)")
        self.assertFails(self.edit(1, size=[2880, 570]), "the top-strip art is 150")

    def test_encoded_blob_in_a_note(self) -> None:
        self.assertFails(self.edit(0, note="iVBORw0KGgo" * 5), "looks like data")

    def test_part_must_agree_with_its_census_rows(self) -> None:
        self.assertFails(self.edit(1, used_by=["G01"]), "is not the windows of its arts", census=True)
        self.assertFails(self.edit(3, size=[1440, 1440]), "is not the census size", census=True)
        self.assertFails(self.edit(3, batch="BD-B"), "is not the census batch", census=True)
        self.assertFails(self.edit(0, frames=9), "is not the census frame count", census=True)
        self.assertFails(self.edit(0, kind="gauge", id="gauge.sword"), "is not the census kind", census=True)

    def test_census_every_art_mapped(self) -> None:
        manifest = valid()
        manifest["parts"][0]["source_arts"] = []
        self.assertFails(manifest, "census art 10 (sword): not mapped", census=True)

    def test_census_excluded_rules(self) -> None:
        manifest = valid()
        manifest["excluded"] = {}
        self.assertFails(manifest, "must be excluded as 'font'", census=True)
        manifest = valid()
        manifest["excluded"]["font"].append("10")
        self.assertFails(manifest, "also the source of", census=True)
        manifest = valid()
        manifest["excluded"]["font"].append("999")
        self.assertFails(manifest, "art 999: not in the census", census=True)
        manifest = valid()
        manifest["excluded"]["music"] = []
        self.assertFails(manifest, "unknown reason")

    def test_census_windows(self) -> None:
        manifest = valid()
        del manifest["windows"]["H01"]
        manifest["parts"][1]["used_by"] = []
        manifest["parts"][2]["used_by"] = []
        errors = self.check(manifest)
        self.assertTrue(any("window H01: in the census, not in the manifest" in e for e in errors), errors)
        manifest = valid()
        manifest["windows"]["G02"] = window()
        self.assertFails(manifest, "in the manifest, not in the census", census=True)
        manifest = valid()
        manifest["parts"][1]["used_by"] = []
        manifest["parts"][2]["used_by"] = []
        self.assertFails(manifest, "no part is used by it", census=True)


class BuilderTests(unittest.TestCase):
    def test_snap_to_grid(self) -> None:
        self.assertEqual([build_kit.snap(u) for u in (800, 600, 400, 358, 442, 41, 159, 1, 16)], [2880, 2160, 1440, 1290, 1590, 150, 570, 6, 60])
        self.assertTrue(all(build_kit.snap(u) % 6 == 0 for u in range(1, 900)))

    def test_window_anchor_classes(self) -> None:
        cases = {"top strip 800x41": "top-strip", "bottom strip (replaces message window)": "bottom-strip", "play view, left edge": "window-edge",
                 "full screen 800x600": "composition", "play-view panel 800x400, two parts": "zone", "two strips + 800x400": "zone",
                 "centred pop-up": "overlay", "lens inside a window": "window", "follows the object": "world"}
        for text, expected in cases.items():
            self.assertEqual(build_kit.window_anchor(text), expected, text)

    def test_same_file_is_one_part(self) -> None:
        arts = [cn.CensusArt("32", "cncl_big", (38, 37), 2, "plate", "B-BUTTONS", ("H05",), "drawn"),
                cn.CensusArt("202", "cncl_big", (38, 37), 2, "plate", "B-BUTTONS", ("H09",), "drawn"),
                cn.CensusArt("file:x-y", "x-y", (10, 10), 1, "picture", "B-ICONS", (), "no window row")]
        windows = [cn.CensusWindow("H05", "Quantity", "bottom strip", 1), cn.CensusWindow("H09", "Panes", "bottom strip", 1)]
        manifest = build_kit.build(arts, windows)
        plate = next(p for p in manifest["parts"] if p["id"] == "plate.cncl_big")
        self.assertEqual((plate["source_arts"], plate["used_by"], plate["anchor"], plate["size"]),
                         (["32", "202"], ["H05", "H09"], "bottom-strip", [138, 132]))
        self.assertEqual(plate["states"], build_kit.PLATE_STATES)


class RealManifestTests(unittest.TestCase):
    def test_real_manifest_passes_against_the_census(self) -> None:
        manifest = kit_check.load_manifest()
        self.assertEqual(kit_check.check_manifest(manifest, cn.load_arts(), cn.load_windows()), [])

    def test_real_manifest_is_what_the_census_gives(self) -> None:
        self.assertEqual(build_kit.MANIFEST.read_text(encoding="utf-8"), build_kit.render(build_kit.build(cn.load_arts(), cn.load_windows())))

    def test_new_parts_have_rows_of_their_own(self) -> None:
        parts = {p["id"]: p for p in kit_check.load_manifest()["parts"]}
        for ident in ("cap.top_strip_left", "cap.top_strip_right", "cap.bottom_strip_left", "cap.bottom_strip_right"):
            self.assertEqual((parts[ident]["source_arts"], parts[ident]["batch"]), ([], "B-BOARD"), ident)
        self.assertEqual(parts["board.inttop"]["layers"], ["cap.top_strip_left", "cap.top_strip_right"])
        self.assertEqual(parts["board.intbotom"]["size"], [2880, 570])

    def test_kit_has_no_full_width_board_outward_backdrop_or_gap_wood(self) -> None:
        for part_row in kit_check.load_manifest()["parts"]:
            if part_row["size"]:
                self.assertLessEqual(part_row["size"][0], 2880, part_row["id"])
                self.assertLessEqual(part_row["size"][1], 2160, part_row["id"])
            self.assertNotIn("gap", part_row["id"])

    def test_no_pixels_in_the_manifest(self) -> None:
        text = Path(build_kit.MANIFEST).read_text(encoding="utf-8")
        self.assertLess(len(text), 400_000)
        self.assertNotIn("base64", text)
        self.assertNotIn("data:", text)

    def test_cli_pass_and_fail(self) -> None:
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            self.assertEqual(kit_check.main([]), 0)
        self.assertIn("KIT CHECK PASS", out.getvalue())
        with tempfile.TemporaryDirectory() as tmp:
            broken = Path(tmp) / "kit.json"
            manifest = kit_check.load_manifest()
            manifest["parts"][0]["size"] = [61, 60]
            broken.write_text(json.dumps(manifest), encoding="utf-8")
            out = io.StringIO()
            with contextlib.redirect_stdout(out):
                self.assertEqual(kit_check.main(["--manifest", str(broken)]), 1)
            self.assertIn("KIT CHECK FAIL", out.getvalue())
            broken.write_text("{", encoding="utf-8")
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(kit_check.main(["--manifest", str(broken)]), 1)

    def test_build_check_detects_drift(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            copy_path = Path(tmp) / "kit.json"
            copy_path.write_text("{}", encoding="utf-8")
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(build_kit.main(["--check", "--out", str(copy_path)]), 1)
                self.assertEqual(build_kit.main(["--out", str(copy_path)]), 0)
                self.assertEqual(build_kit.main(["--check", "--out", str(copy_path)]), 0)


if __name__ == "__main__":
    unittest.main()
