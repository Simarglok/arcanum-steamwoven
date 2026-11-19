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


if __name__ == "__main__":
    unittest.main()
