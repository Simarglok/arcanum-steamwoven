"""ARC-UI-3077: tests of the assembled-frames-to-importer-plan tool on synthetic pictures only (no original art)."""
from __future__ import annotations

import io
import json
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path

from tools.steamwoven_ui import assemble, census, pack_plan, pngio
from tools.steamwoven_ui.pngio import Image

SCALE = 4
UNITS = (10, 8)  # the synthetic original frame
HD = (UNITS[0] * SCALE, UNITS[1] * SCALE)


def plate(rgba=(90, 60, 30, 255), width=HD[0], height=HD[1], hole_rows: int = 0) -> Image:
    """A round opaque plate on a transparent frame (alpha 0 in the corners, a soft ring), so the mask is not the whole frame."""
    image = Image.blank(width, height)
    cx, cy, r = (width - 1) / 2, (height - 1) / 2, min(width, height) / 2 - 0.5
    for y in range(height):
        for x in range(width):
            d = ((x - cx) ** 2 + (y - cy) ** 2) ** 0.5
            alpha = 255 if d <= r - 1 else 128 if d <= r else 0
            if alpha:
                image.data[(y * width + x) * 4:(y * width + x) * 4 + 4] = bytes((*rgba[:3], alpha))
    return image


def row(art: str, name: str, frames: int, size=(10, 8)) -> census.CensusArt:
    return census.CensusArt(art, name, size, frames, "plate", "B-BUTTONS", ("H02",), "drawn")


class Folder:
    """A folder of assembled frames and their records, the way `assemble.py --out` leaves them."""

    def __init__(self, case: unittest.TestCase) -> None:
        self.tmp = tempfile.TemporaryDirectory()
        case.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name).resolve()

    def art(self, art: str, frames: int = 3, mode: str | None = "masked", scale: int = SCALE, size=HD, image: Image | None = None) -> Path:
        states = ["idle", "pressed", "hover", "disabled"]
        record = {"art": art, "scale": scale, "masterCanvas": [36, 29]}
        if mode:
            record["alphaMode"] = mode
        record["frames"] = []
        for index in range(frames):
            file = f"{art}-f{index:03d}.png"
            pngio.write(self.root / file, image or plate((60 + index * 40, 60, 30, 255), size[0], size[1]))
            record["frames"].append({"frame": index, "state": states[index % 4], "file": file, "size": list(size), "placements": []})
        path = self.root / f"{art}{pack_plan.RECORD_SUFFIX}"
        path.write_text(json.dumps(record), encoding="utf-8")
        return path


def plan_of(folder: Folder, paths, **kw) -> pack_plan.Plan:
    assemblies = [pack_plan.load_assembly(p) for p in paths]
    names = {"470": "Combat_Button", "471": "Schematics_Button"}
    arts = kw.pop("arts", {a.art: row(a.art, names.get(a.art, "Art" + a.art), len(a.frames)) for a in assemblies})
    return pack_plan.build_plan(assemblies, folder.root, arts=arts, **kw)


class PlanTests(unittest.TestCase):
    def test_masked_frames_become_ready_items_with_masks(self) -> None:
        folder = Folder(self)
        plan = plan_of(folder, [folder.art("470")])
        self.assertEqual(plan.document["kind"], "arcanum-steamwoven-hd-interface-plan")
        self.assertEqual((plan.document["schema"], plan.document["scale"], plan.document["packId"]), (1, 4, "interface-redraw-4x"))
        self.assertEqual(len(plan.document["items"]), 3)
        for index, item in enumerate(plan.document["items"]):
            self.assertEqual(item, {"image": f"470-f00{index}.png", "mode": "ready", "images": [f"470-f00{index}.png"], "lock": False,
                                    "tiles": [{"art": "Combat_Button.ART", "frame": index, "alphaMask": f"masks/470-f00{index}.png"}]})
        self.assertEqual(sorted(p.relative_to(folder.root).as_posix() for p in plan.files), [f"masks/470-f00{i}.png" for i in range(3)])
        self.assertEqual([(a.art, a.art_file, a.source, a.frames, a.size, a.masks) for a in plan.arts], [("470", "Combat_Button.ART", "census", 3, HD, True)])

    def test_the_mask_is_the_frames_alpha_as_an_8_bit_grey_png(self) -> None:
        folder = Folder(self)
        plan = plan_of(folder, [folder.art("470", frames=1)])
        blob = plan.files[folder.root / "masks/470-f000.png"]
        self.assertEqual(blob[24:29], bytes((8, 0, 0, 0, 0)), "8-bit grey, no interlace")
        mask = pngio.decode(blob)
        frame = pngio.read(folder.root / "470-f000.png")
        self.assertEqual((mask.w, mask.h), HD)
        for i in range(HD[0] * HD[1]):
            self.assertEqual(tuple(mask.data[i * 4:i * 4 + 3]), (frame.data[i * 4 + 3],) * 3)
        self.assertIn(0, set(mask.data[0::4]))
        self.assertIn(255, set(mask.data[0::4]))

    def test_a_smooth_or_legacy_frame_keeps_the_original_alpha_unless_asked(self) -> None:
        folder = Folder(self)
        path = folder.art("470", mode="legacySmooth")
        auto = plan_of(folder, [path])
        self.assertEqual(auto.files, {})
        self.assertTrue(all("alphaMask" not in item["tiles"][0] for item in auto.document["items"]))
        self.assertFalse(auto.arts[0].masks)
        self.assertEqual(len(plan_of(folder, [path], masks="always").files), 3)
        masked = folder.art("471", mode="masked")
        self.assertEqual(plan_of(folder, [masked], masks="never").files, {})

    def test_an_older_record_without_the_alpha_mode_gets_masks(self) -> None:
        folder = Folder(self)
        plan = plan_of(folder, [folder.art("470", mode=None)])
        self.assertEqual(len(plan.files), 3)

    def test_arts_are_in_number_order_and_one_plan_holds_one_scale(self) -> None:
        folder = Folder(self)
        arts = {"27": row("27", "A", 1), "229": row("229", "B", 1), "Slot": row("Slot", "Slot", 1)}
        folder.art("229", frames=1)
        folder.art("27", frames=1)
        found = pack_plan.find_records([folder.root])
        self.assertEqual([p.name for p in found], ["27.assembly.json", "229.assembly.json"])
        plan = plan_of(folder, found, arts=arts)
        self.assertEqual([a.art for a in plan.arts], ["27", "229"])
        other = folder.art("5", frames=1, scale=2, size=(20, 16))
        with self.assertRaisesRegex(pack_plan.PlanError, "different scales"):
            plan_of(folder, found + [other], arts={**arts, "5": row("5", "C", 1)})

    def test_art_names(self) -> None:
        arts = {"470": row("470", "Combat_Button", 3)}
        self.assertEqual(pack_plan.art_file_name("470", {}, arts), ("Combat_Button.ART", "census"))
        self.assertEqual(pack_plan.art_file_name("470", {"470": "Other.ART"}, arts), ("Other.ART", "--art-name"))
        self.assertEqual(pack_plan.art_file_name("Slot_Row", {}, arts), ("Slot_Row.ART", "art name"))
        self.assertEqual(pack_plan.art_file_name("Slot_Row.art", {}, arts), ("Slot_Row.art", "art name"))
        with self.assertRaisesRegex(pack_plan.PlanError, "not in the census"):
            pack_plan.art_file_name("999", {}, arts)
        self.assertEqual(pack_plan.parse_art_names(["471=Schematics_Button.ART"]), {"471": "Schematics_Button.ART"})
        for bad in ("471", "471=Name", "471=a/b.ART", "=X.ART", "../x=X.ART"):
            with self.assertRaises(pack_plan.PlanError, msg=bad):
                pack_plan.parse_art_names([bad])
        with self.assertRaises(pack_plan.PlanError):
            pack_plan.parse_art_names(["1=A.ART", "1=B.ART"])

    def test_the_census_row_is_checked(self) -> None:
        folder = Folder(self)
        with self.assertRaisesRegex(pack_plan.PlanError, "3 assembled frames, the census says 2"):
            plan_of(folder, [folder.art("470")], arts={"470": row("470", "Combat_Button", 2)})
        with self.assertRaisesRegex(pack_plan.PlanError, "expected 80x64"):
            plan_of(folder, [folder.art("470")], arts={"470": row("470", "Combat_Button", 3, size=(20, 16))})
        with self.assertRaisesRegex(pack_plan.PlanError, "census"):
            plan_of(folder, [folder.art("470")], arts={})
        # an override replaces the census row, so a differing census size does not apply to it
        plan = plan_of(folder, [folder.art("470")], arts={}, overrides={"470": "Combat_Button.ART"})
        self.assertEqual(plan.arts[0].source, "--art-name")

    def test_two_arts_naming_one_file_are_refused(self) -> None:
        folder = Folder(self)
        with self.assertRaisesRegex(pack_plan.PlanError, "both name"):
            plan_of(folder, [folder.art("470"), folder.art("471")], overrides={"470": "Same.ART", "471": "same.art"})

    def test_frame_files_are_checked_against_their_record(self) -> None:
        folder = Folder(self)
        path = folder.art("470")
        (folder.root / "470-f001.png").unlink()
        with self.assertRaisesRegex(pack_plan.PlanError, "frame 1"):
            plan_of(folder, [path])
        folder = Folder(self)
        path = folder.art("470")
        pngio.write(folder.root / "470-f002.png", plate(width=HD[0] + 4))
        with self.assertRaisesRegex(pack_plan.PlanError, "is 44x32"):
            plan_of(folder, [path])
        folder = Folder(self)
        path = folder.art("470")
        pngio.write(folder.root / "470-f000.png", Image.blank(*HD))
        with self.assertRaisesRegex(pack_plan.PlanError, "no visible pixel"):
            plan_of(folder, [path])
        folder = Folder(self)
        path = folder.art("470", mode="legacy")
        pngio.write(folder.root / "470-f000.png", Image.blank(*HD))  # nothing to mask in the legacy mode: the plan is still written
        self.assertEqual(len(plan_of(folder, [path]).document["items"]), 3)

    def test_a_png_without_alpha_is_refused(self) -> None:
        folder = Folder(self)
        path = folder.art("470", frames=1)
        raw = pack_plan.encode_grey(*HD, bytes(range(256))[: 1] * (HD[0] * HD[1]))
        (folder.root / "470-f000.png").write_bytes(raw)
        with self.assertRaisesRegex(pack_plan.PlanError, "no alpha channel"):
            plan_of(folder, [path])

    def test_bad_records_are_refused(self) -> None:
        folder = Folder(self)
        path = folder.art("470")
        good = json.loads(path.read_text())

        def refuse(change, text: str) -> None:
            data = json.loads(json.dumps(good))
            change(data)
            path.write_text(json.dumps(data), encoding="utf-8")
            with self.assertRaisesRegex(pack_plan.PlanError, text):
                pack_plan.load_assembly(path)

        refuse(lambda d: d.update(art="../x"), "art must be")
        refuse(lambda d: d.update(scale=1), "scale")
        refuse(lambda d: d.update(scale=True), "scale")
        refuse(lambda d: d.update(alphaMode="soft"), "alphaMode")
        refuse(lambda d: d.update(frames=[]), "frames must list")
        refuse(lambda d: d["frames"][1].update(frame=2), r"frames\[1\].frame must be 1")
        refuse(lambda d: d["frames"][0].update(file="../x.png"), "plain .png")
        refuse(lambda d: d["frames"][0].update(file="a/b.png"), "plain .png")
        refuse(lambda d: d["frames"][0].update(size=[0, 5]), "size")
        refuse(lambda d: d["frames"][0].update(size=[1.5, 5]), "size")
        path.write_text("{", encoding="utf-8")
        with self.assertRaises(pack_plan.PlanError):
            pack_plan.load_assembly(path)
        path.write_text("[]", encoding="utf-8")
        with self.assertRaisesRegex(pack_plan.PlanError, "not an assembly record"):
            pack_plan.load_assembly(path)

    def test_options_are_checked(self) -> None:
        folder = Folder(self)
        path = folder.art("470")
        for kw, text in (({"pack_id": "Bad Id"}, "pack-id"), ({"masks": "some"}, "masks")):
            with self.assertRaisesRegex(pack_plan.PlanError, text):
                plan_of(folder, [path], **kw)
        with self.assertRaisesRegex(pack_plan.PlanError, "no assembly record"):
            pack_plan.build_plan([], folder.root, arts={})
        with self.assertRaisesRegex(pack_plan.PlanError, "same art"):
            plan_of(folder, [path, path])

    def test_paths_are_relative_to_the_plan_when_it_goes_elsewhere(self) -> None:
        folder = Folder(self)
        path = folder.art("470", frames=1)
        elsewhere = folder.root / "plans" / "one"
        plan = pack_plan.build_plan([pack_plan.load_assembly(path)], elsewhere, arts={"470": row("470", "Combat_Button", 1)})
        self.assertEqual(plan.document["items"][0]["image"], "../../470-f000.png")
        self.assertEqual(plan.document["items"][0]["tiles"][0]["alphaMask"], "masks/470-f000.png")
        self.assertEqual(list(plan.files), [elsewhere / "masks/470-f000.png"])


class WriteTests(unittest.TestCase):
    def test_write_plan_writes_masks_then_the_plan(self) -> None:
        folder = Folder(self)
        plan = plan_of(folder, [folder.art("470")])
        written = pack_plan.write_plan(plan, folder.root)
        self.assertEqual(written[-1], folder.root / "plan.json")
        self.assertEqual(json.loads((folder.root / "plan.json").read_text()), plan.document)
        for index in range(3):
            self.assertEqual(pngio.read(folder.root / f"masks/470-f00{index}.png").w, HD[0])
        # again: replaces its own plan
        pack_plan.write_plan(plan, folder.root)

    def test_it_never_replaces_another_file_named_plan_json(self) -> None:
        folder = Folder(self)
        plan = plan_of(folder, [folder.art("470")])
        (folder.root / "plan.json").write_text(json.dumps({"kind": "something-else"}), encoding="utf-8")
        with self.assertRaisesRegex(pack_plan.PlanError, "not an importer plan"):
            pack_plan.write_plan(plan, folder.root)
        self.assertEqual(json.loads((folder.root / "plan.json").read_text()), {"kind": "something-else"})
        self.assertFalse((folder.root / "masks").exists(), "nothing written before the refusal")

    def test_out_folder_inside_the_checkout_is_refused_outside_local(self) -> None:
        with self.assertRaisesRegex(pack_plan.PlanError, "inside the checkout"):
            pack_plan.check_out_dir(pack_plan.REPO / "docs" / "ui")
        pack_plan.check_out_dir(pack_plan.REPO / "local" / "steamwoven-ui" / "x")
        pack_plan.check_out_dir(Path(tempfile.gettempdir()))


class EndToEndTests(unittest.TestCase):
    """assemble.py to pack_plan.py on synthetic pictures: the plan names the frames and masks the assembly wrote."""

    def assembled(self, mode: str) -> Folder:
        folder = Folder(self)
        legacy = Image.blank(*UNITS)
        for y in range(UNITS[1]):
            for x in range(UNITS[0]):
                if 1 <= x <= 8 and 1 <= y <= 6:
                    legacy.data[(y * UNITS[0] + x) * 4:(y * UNITS[0] + x) * 4 + 4] = bytes((128, 128, 128, 255))
        pngio.write(folder.root / "legacy.png", legacy)
        pngio.write(folder.root / "base.png", Image.blank(UNITS[0] * SCALE, UNITS[1] * SCALE, (40, 90, 140, 255)))
        layout = {"kind": assemble.KIND, "version": 1, "target": {"art": "Test_But", "size": list(UNITS), "frames": 2}, "scale": SCALE,
                  "alpha": {"mode": mode, "legacy": "legacy.png"}, "base": {"image": "base.png"},
                  "frames": [{"state": "idle"}, {"state": "pressed"}]}
        (folder.root / "layout.json").write_text(json.dumps(layout), encoding="utf-8")
        out, err = io.StringIO(), io.StringIO()
        with redirect_stdout(out), redirect_stderr(err):
            code = assemble.main([str(folder.root / "layout.json"), "--out", str(folder.root / "frames")])
        self.assertEqual(code, 0, err.getvalue())
        return folder

    def run_main(self, args: list[str]) -> tuple[int, str, str]:
        out, err = io.StringIO(), io.StringIO()
        with redirect_stdout(out), redirect_stderr(err):
            code = pack_plan.main(args)
        return code, out.getvalue(), err.getvalue()

    def test_the_assembly_record_carries_the_alpha_mode(self) -> None:
        folder = self.assembled("masked")
        record = json.loads((folder.root / "frames" / "Test_But.assembly.json").read_text())
        self.assertEqual(record["alphaMode"], "masked")
        self.assertNotIn("part", record)

    def test_assembled_masked_frames_to_plan_and_masks(self) -> None:
        folder = self.assembled("masked")
        frames = folder.root / "frames"
        code, out, err = self.run_main([str(frames)])
        self.assertEqual((code, err), (0, ""))
        self.assertIn("PACK-PLAN PASS", out)
        self.assertIn("Test_But -> Test_But.ART", out)
        plan = json.loads((frames / "plan.json").read_text())
        self.assertEqual([i["tiles"][0] for i in plan["items"]],
                         [{"art": "Test_But.ART", "frame": n, "alphaMask": f"masks/Test_But-f00{n}.png"} for n in range(2)])
        for n in range(2):
            frame = pngio.read(frames / f"Test_But-f00{n}.png")
            mask = pngio.read(frames / f"masks/Test_But-f00{n}.png")
            self.assertEqual(bytes(mask.data[0::4]), bytes(frame.data[3::4]))
        self.assertEqual({p.name for p in frames.iterdir()}, {"Test_But-f000.png", "Test_But-f001.png", "Test_But.assembly.json", "plan.json", "masks"})

    def test_dry_run_writes_nothing_and_a_legacy_smooth_frame_has_no_mask(self) -> None:
        folder = self.assembled("legacySmooth")
        frames = folder.root / "frames"
        before = sorted(p.name for p in frames.iterdir())
        code, out, _ = self.run_main([str(frames), "--dry-run"])
        self.assertEqual(code, 0)
        self.assertIn("DRY-RUN PASS", out)
        self.assertIn("the original alpha", out)
        self.assertEqual(sorted(p.name for p in frames.iterdir()), before)
        code, _, err = self.run_main([str(frames / "Test_But.assembly.json"), "--out", str(folder.root / "plan-out"), "--pack-id", "kit-test"])
        self.assertEqual((code, err), (0, ""))
        plan = json.loads((folder.root / "plan-out" / "plan.json").read_text())
        self.assertEqual(plan["packId"], "kit-test")
        self.assertEqual(plan["items"][0]["image"], "../frames/Test_But-f000.png")
        self.assertNotIn("alphaMask", plan["items"][0]["tiles"][0])
        self.assertFalse((folder.root / "plan-out" / "masks").exists())

    def test_failure_exits_1_and_writes_nothing(self) -> None:
        folder = self.assembled("masked")
        frames = folder.root / "frames"
        (frames / "Test_But-f001.png").unlink()
        code, _, err = self.run_main([str(frames)])
        self.assertEqual(code, 1)
        self.assertIn("PACK-PLAN FAIL", err)
        self.assertFalse((frames / "plan.json").exists())
        self.assertFalse((frames / "masks").exists())
        code, _, err = self.run_main([str(folder.root / "nothing")])
        self.assertEqual(code, 1)
        code, _, err = self.run_main([str(folder.root)])  # a folder with no record
        self.assertEqual(code, 1)
        self.assertIn("no *.assembly.json", err)
        code, _, err = self.run_main([str(frames), "--art-name", "bad"])
        self.assertEqual(code, 1)

    def test_art_name_option_names_a_numeric_art(self) -> None:
        folder = self.assembled("masked")
        frames = folder.root / "frames"
        record = frames / "Test_But.assembly.json"
        data = json.loads(record.read_text())
        data["art"] = "98765"
        record.write_text(json.dumps(data), encoding="utf-8")
        code, _, err = self.run_main([str(frames), "--dry-run"])
        self.assertEqual(code, 1)
        self.assertIn("not in the census", err)
        code, out, _ = self.run_main([str(frames), "--dry-run", "--art-name", "98765=Test_But.ART"])
        self.assertEqual(code, 0)
        self.assertIn("98765 -> Test_But.ART", out)
        self.assertIn("--art-name", out)


if __name__ == "__main__":
    unittest.main()
