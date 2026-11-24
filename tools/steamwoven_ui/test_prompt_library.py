"""ARC-RES-2455: unit tests of the Steamwoven prompt library tool (synthetic prompt folders, no game data, no images)."""
from __future__ import annotations

import json
import shutil
import struct
import tempfile
import unittest
from pathlib import Path

from tools.steamwoven_ui import prompt_library as lib


def fake_png(width: int, height: int) -> bytes:
    return b"\x89PNG\r\n\x1a\n" + struct.pack(">I", 13) + b"IHDR" + struct.pack(">II", width, height) + b"\x08\x06\x00\x00\x00"


class RealLibraryTests(unittest.TestCase):
    def test_real_library_passes_with_the_trd_kinds(self) -> None:
        library = lib.load()
        self.assertEqual([], lib.check(library, trd_text=lib.TRD.read_text(encoding="utf-8")))

    def test_every_context_screen_and_every_kind_has_a_prompt(self) -> None:
        library = lib.load()
        ids = {p["id"] for p in library["prompts"]}
        for screen in ("ctx-hud", "ctx-inventory", "ctx-character", "ctx-logbook", "ctx-schematic", "ctx-reading", "ctx-main-menu"):
            self.assertIn(screen, ids)
        self.assertEqual(set(lib.KINDS), {p["kind"] for p in library["prompts"] if p["kind"]})

    def test_trd_kind_table_is_read(self) -> None:
        self.assertEqual(list(lib.KINDS), lib.trd_kinds(lib.TRD.read_text(encoding="utf-8")))

    def test_composed_prompt_has_rules_body_and_slots_and_no_open_placeholder(self) -> None:
        library = lib.load()
        for entry in library["prompts"]:
            text = lib.compose(library, entry["id"], {"part_id": "P-1", "part_note": "note", "width": "12", "height": "6"})
            self.assertIn("COMMON RULES", text)
            self.assertIn("REFERENCE SLOTS", text)
            self.assertEqual([], lib.PLACEHOLDER.findall(text), entry["id"])

    def test_composed_prompt_hash_is_stable(self) -> None:
        library = lib.load()
        first = lib.sha256_text(lib.compose(library, "ctx-hud"))
        self.assertEqual(first, lib.sha256_text(lib.compose(library, "ctx-hud")))

    def test_references_geometry_names_every_context_screen(self) -> None:
        spec = json.loads((lib.PROMPTS / "references.json").read_text(encoding="utf-8"))
        self.assertEqual(set(lib.load()["context_screens"]), set(spec["screens"]))


class SyntheticFailureTests(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.tmp, True)
        self.prompts = self.tmp / "prompts"
        shutil.copytree(lib.PROMPTS, self.prompts)
        self.library = lib.load(self.prompts)

    def problems(self) -> list[str]:
        return lib.check(self.library, self.prompts)

    def test_valid_copy(self) -> None:
        self.assertEqual([], self.problems())

    def test_duplicate_id(self) -> None:
        self.library["prompts"][1]["id"] = self.library["prompts"][0]["id"]
        self.assertTrue(any("duplicate" in p for p in self.problems()))

    def test_unknown_kind_and_slot_and_group(self) -> None:
        self.library["prompts"][-1]["kind"] = "frame"
        self.library["prompts"][-2]["references"] = ["nothing"]
        self.library["prompts"][-3]["group"] = "other"
        text = "\n".join(self.problems())
        self.assertIn("unknown kind frame", text)
        self.assertIn("unknown reference slot nothing", text)
        self.assertIn("unknown group other", text)

    def test_missing_file_and_empty_and_non_ascii_and_placeholder(self) -> None:
        (self.prompts / "kind-cap.txt").unlink()
        (self.prompts / "kind-glyph.txt").write_text("  \n", encoding="utf-8")
        (self.prompts / "kind-state.txt").write_text("café", encoding="utf-8")
        (self.prompts / "kind-grid.txt").write_text("size {depth}", encoding="utf-8")
        text = "\n".join(self.problems())
        self.assertIn("kind-cap: file kind-cap.txt missing", text)
        self.assertIn("kind-glyph: empty prompt", text)
        self.assertIn("kind-state: non-ASCII", text)
        self.assertIn("unknown placeholder {depth}", text)

    def test_size_off_the_grid_and_wrong_context_size(self) -> None:
        for entry in self.library["prompts"]:
            if entry["id"] == "kind-backdrop-window":
                entry["size"] = [2880, 1441]
            if entry["id"] == "ctx-hud":
                entry["size"] = [3840, 2160]
        text = "\n".join(self.problems())
        self.assertIn("1441 is not a multiple of 6", text)
        self.assertIn("a context image is 2880 x 2160", text)

    def test_missing_kind_and_context_and_batch_member(self) -> None:
        self.library["prompts"] = [p for p in self.library["prompts"] if p["id"] not in ("kind-cursor", "ctx-logbook")]
        text = "\n".join(self.problems())
        self.assertIn("kind cursor has no prompt", text)
        self.assertIn("context screen ctx-logbook has no prompt", text)
        self.assertIn("batch style-anchors-contexts: unknown prompt ctx-logbook", text)

    def test_trd_kind_drift(self) -> None:
        text = "| Kind | What | Examples |\n| --- | --- | --- |\n| material | a | b |\n| frame | c | d |\n"
        self.assertTrue(any("differ from KINDS" in p for p in lib.check(self.library, self.prompts, trd_text=text)))


class InboxProvenanceTests(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.tmp, True)
        self.library = lib.load()
        self.local = self.tmp / "local"
        (self.local / "refs").mkdir(parents=True)
        for screen in self.library["context_screens"]:
            (self.local / "refs" / f"{screen}-800x600.png").write_bytes(fake_png(800, 600))

    def test_inbox_lists_prompts_hashes_and_references(self) -> None:
        info = lib.build_inbox(self.library, "style-anchors-contexts", self.local)
        self.assertEqual(7, info["items"])
        batch = json.loads((self.local / "inbox" / "style-anchors-contexts" / "batch.json").read_text(encoding="utf-8"))
        item = batch["items"][0]
        written = (self.local / "inbox" / "style-anchors-contexts" / item["prompt_file"]).read_text(encoding="utf-8")
        self.assertEqual(lib.sha256_text(written), item["prompt_sha256"])
        self.assertEqual("original", item["references"][0]["slot"])
        self.assertTrue((self.local / "inbox" / "style-anchors-contexts" / item["references"][0]["file"]).is_file())
        self.assertIn("outbox/style-anchors-contexts/ctx-hud.png", (self.local / "inbox" / "style-anchors-contexts" / "README.txt").read_text(encoding="utf-8"))

    def test_context_screen_gets_the_upscaled_original_as_the_one_image_to_edit(self) -> None:
        (self.local / "refs" / "ctx-hud-guide-2880x2160.png").write_bytes(fake_png(2880, 2160))
        lib.build_inbox(self.library, "style-anchors-contexts", self.local)
        batch = json.loads((self.local / "inbox" / "style-anchors-contexts" / "batch.json").read_text(encoding="utf-8"))
        hud = next(i for i in batch["items"] if i["id"] == "ctx-hud")
        self.assertEqual(["refs/ctx-hud/ctx-hud-guide-2880x2160.png"], [r["file"] for r in hud["references"]])
        inventory = next(i for i in batch["items"] if i["id"] == "ctx-inventory")  # no guide yet: falls back to what the prefix holds
        self.assertEqual(["refs/ctx-inventory/ctx-inventory-800x600.png"], [r["file"] for r in inventory["references"]])

    def test_inbox_fails_when_a_reference_is_missing(self) -> None:
        (self.local / "refs" / "ctx-hud-800x600.png").unlink()
        with self.assertRaises(FileNotFoundError):
            lib.build_inbox(self.library, "style-anchors-contexts", self.local)

    def test_unknown_batch(self) -> None:
        with self.assertRaises(KeyError):
            lib.build_inbox(self.library, "nope", self.local)

    def test_sheet_batch_needs_the_approved_contexts(self) -> None:
        with self.assertRaises(FileNotFoundError):
            lib.build_inbox(self.library, "style-anchors-sheet", self.local)
        outbox = self.local / "outbox" / "style-anchors-contexts"
        outbox.mkdir(parents=True)
        (outbox / "ctx-hud.png").write_bytes(fake_png(2880, 2160))
        self.assertEqual(1, lib.build_inbox(self.library, "style-anchors-sheet", self.local)["items"])

    def test_provenance_records_size_hash_and_who(self) -> None:
        lib.build_inbox(self.library, "style-anchors-contexts", self.local)
        batch = json.loads((self.local / "inbox" / "style-anchors-contexts" / "batch.json").read_text(encoding="utf-8"))
        for item in batch["items"]:
            target = self.local / item["outbox_file"]
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(fake_png(2880, 2160))
        paths = lib.write_provenance(self.library, "style-anchors-contexts", self.local, "generator", "1.0", "owner", "2026-11-19")
        self.assertEqual(7, len(paths))
        record = json.loads(Path(paths[0]).read_text(encoding="utf-8"))
        self.assertEqual([2880, 2160], record["image_size"])
        self.assertEqual("owner", record["run_by"])
        self.assertEqual(batch["items"][0]["prompt_sha256"], record["prompt_sha256"])

    def test_provenance_needs_every_result(self) -> None:
        lib.build_inbox(self.library, "style-anchors-contexts", self.local)
        with self.assertRaises(FileNotFoundError):
            lib.write_provenance(self.library, "style-anchors-contexts", self.local, "g", "1", "owner")

    def test_png_size_rejects_other_files(self) -> None:
        bad = self.tmp / "x.png"
        bad.write_bytes(b"not a png at all, really not")
        with self.assertRaises(ValueError):
            lib.png_size(bad)


class RefsAndCliTests(unittest.TestCase):
    def test_magick_args_crop_patch_flip_resize(self) -> None:
        args = lib.magick_args(Path("a.png"), {"crop": "800x200+240+441", "flip": True, "resize": "800x400!",
                                               "patch": [{"from": "90x30+0+330", "to": "+0+374"}]})
        self.assertEqual(["a.png", "-crop", "800x200+240+441", "+repage"], args[:4])
        self.assertIn("-composite", args)
        self.assertLess(args.index("-composite"), args.index("-flip"))
        self.assertEqual(["-filter", "Lanczos", "-resize", "800x400!"], args[-4:])

    def test_build_refs_command_order_and_size_check(self) -> None:
        from unittest import mock
        calls: list[list[str]] = []

        def fake_run(cmd, check):
            calls.append(cmd)
            if len(cmd) > 2 and cmd[-1].endswith("-800x600.png"):
                Path(cmd[-1]).write_bytes(fake_png(800, 600))
            elif cmd[-1].endswith("-2880x2160.png"):
                Path(cmd[-1]).write_bytes(fake_png(2880, 2160))

        with tempfile.TemporaryDirectory() as tmp, mock.patch.object(lib.subprocess, "run", fake_run), mock.patch.object(lib.shutil, "which", lambda _: "magick"):
            written = lib.build_refs(Path("shots"), Path(tmp))
        self.assertEqual(14, len(written))
        hud = next(c for c in calls if c[-1].endswith("ctx-hud-800x600.png"))
        self.assertEqual("shots/H01-top-strip-clock-before.png", hud[1])
        self.assertEqual("-append", hud[-2])
        self.assertIn("shots/H02-bottom-strip-xp-before.png", hud)
        with tempfile.TemporaryDirectory() as tmp, mock.patch.object(lib.subprocess, "run", lambda cmd, check: Path(cmd[-1]).write_bytes(fake_png(801, 600))), mock.patch.object(lib.shutil, "which", lambda _: "magick"):
            with self.assertRaises(ValueError):
                lib.build_refs(Path("shots"), Path(tmp))

    def test_provenance_rejects_a_wrong_size(self) -> None:
        tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, tmp, True)
        library = lib.load()
        (tmp / "refs").mkdir()
        for screen in library["context_screens"]:
            (tmp / "refs" / f"{screen}-800x600.png").write_bytes(fake_png(800, 600))
        lib.build_inbox(library, "style-anchors-contexts", tmp)
        outbox = tmp / "outbox" / "style-anchors-contexts"
        outbox.mkdir(parents=True)
        for screen in library["context_screens"]:
            (outbox / f"{screen}.png").write_bytes(fake_png(2880, 2160))
        (outbox / "ctx-hud.png").write_bytes(fake_png(1920, 1080))
        with self.assertRaises(ValueError):
            lib.write_provenance(library, "style-anchors-contexts", tmp, "g", "1", "owner")
        for bad in (fake_png(0, 0), fake_png(1448, 1080)):
            (outbox / "ctx-hud.png").write_bytes(bad)
            with self.assertRaises(ValueError):
                lib.write_provenance(library, "style-anchors-contexts", tmp, "g", "1", "owner")
        (outbox / "ctx-hud.png").write_bytes(fake_png(1448, 1086))  # smaller, same 4:3 aspect: accepted
        self.assertEqual(7, len(lib.write_provenance(library, "style-anchors-contexts", tmp, "g", "1", "owner")))

    def test_inbox_failure_writes_nothing(self) -> None:
        tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, tmp, True)
        with self.assertRaises(FileNotFoundError):
            lib.build_inbox(lib.load(), "style-anchors-contexts", tmp)
        self.assertFalse((tmp / "inbox").exists())

    def test_cli_errors_are_one_line_failures(self) -> None:
        self.assertEqual(1, lib.main(["show", "nope"]))
        self.assertEqual(1, lib.main(["show", "kind-board", "--width", "1441"]))
        self.assertEqual(0, lib.main(["show", "kind-board", "--width", "2880", "--height", "150"]))

    def test_orphan_prompt_file_and_bad_size_type(self) -> None:
        tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, tmp, True)
        prompts = tmp / "prompts"
        shutil.copytree(lib.PROMPTS, prompts)
        (prompts / "stray.txt").write_text("x", encoding="utf-8")
        library = lib.load(prompts)
        library["prompts"][-1]["size"] = ["12", 6]
        text = "\n".join(lib.check(library, prompts))
        self.assertIn("orphan prompt file stray.txt", text)
        self.assertIn("is not an integer", text)

    def test_main_checkout_of_a_worktree(self) -> None:
        import subprocess
        tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, tmp, True)
        main = (tmp / "main").resolve()
        main.mkdir()
        git = ["git", "-c", "user.name=t", "-c", "user.email=t@t"]
        subprocess.run([*git, "init", "-q"], cwd=main, check=True)
        subprocess.run([*git, "commit", "-q", "--allow-empty", "-m", "x"], cwd=main, check=True)
        subprocess.run([*git, "worktree", "add", "-q", str(tmp / "wt"), "-b", "w"], cwd=main, check=True)
        self.assertEqual(main, lib.main_checkout(tmp / "wt"))


if __name__ == "__main__":
    unittest.main()
