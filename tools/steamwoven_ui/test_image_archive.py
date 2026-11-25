"""ARC-DEV-3277: the per-task image archive, its registry, the out-path guard and the rescue helpers (synthetic images, temp folders)."""
from __future__ import annotations

import io
import struct
import tempfile
import unittest
from pathlib import Path

from tools.steamwoven_ui import fixtures, image_archive as ia, image_rescue as rescue


def fake_png(width: int, height: int, tag: int = 0) -> bytes:
    return b"\x89PNG\r\n\x1a\n" + struct.pack(">I", 13) + b"IHDR" + struct.pack(">II", width, height) + b"\x08\x06\x00\x00\x00" + bytes([tag])


class ArchiveTests(unittest.TestCase):
    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.tmp = Path(self._tmp.name)
        self.root = self.tmp / "archive"
        self.reg = ia.Registry(self.root)

    def tearDown(self) -> None:
        self._tmp.cleanup()

    def image(self, name: str, tag: int = 0) -> Path:
        path = self.tmp / "src" / name
        path.parent.mkdir(exist_ok=True)
        path.write_bytes(fake_png(40, 30, tag))
        return path

    def test_task_names(self) -> None:
        for good in ("ARC-UI-3040", "ARC-UI-3101-b", "_unsorted/codex-2026-11"):
            self.assertEqual(good, ia.check_task(good))
        for bad in ("3040", "arc-ui-3040", "ARC-UI-3040/../x", "../ARC-UI-1", "_unsorted", "ARC-UI-1\n", "_unsorted/..", "_unsorted/."):
            with self.assertRaises(ValueError):
                ia.check_task(bad)

    def test_add_copies_registers_and_never_touches_the_source(self) -> None:
        src = self.image("a.png")
        before = src.read_bytes()
        row = ia.add_file(self.reg, "ARC-UI-3040", src, kind="composite", note="n")
        self.reg.save()
        self.assertEqual(before, src.read_bytes())
        self.assertEqual(before, (self.root / "ARC-UI-3040" / "composite" / "a.png").read_bytes())
        self.assertEqual([40, 30], row["size"])
        self.assertEqual("unknown", row["status"])
        again = ia.Registry(self.root)
        self.assertEqual(1, len(again.rows))
        self.assertEqual(ia.sha256_file(src), next(iter(again.rows.values()))["sha256"])

    def test_same_content_adds_a_source_not_a_copy_and_a_taken_name_gets_the_hash(self) -> None:
        a, b = self.image("a.png"), self.tmp / "src" / "b.png"
        b.write_bytes(a.read_bytes())
        ia.add_file(self.reg, "ARC-UI-1", a, kind="composite")
        row = ia.add_file(self.reg, "ARC-UI-1", b, kind="composite")
        self.assertEqual(1, len(self.reg.rows))
        self.assertEqual(2, len(row["sources"]))
        other = self.tmp / "other" / "a.png"
        other.parent.mkdir()
        other.write_bytes(fake_png(40, 30, 9))
        row2 = ia.add_file(self.reg, "ARC-UI-1", other, kind="composite")
        self.assertNotEqual("composite/a.png", row2["file"])
        self.assertTrue((self.root / "ARC-UI-1" / row2["file"]).is_file())

    def test_mark_final_copies_into_final_and_verify_detects_change(self) -> None:
        row = ia.add_file(self.reg, "ARC-UI-2", self.image("m.png"), kind="master")
        ia.mark(self.reg, "ARC-UI-2", row["file"], "final")
        self.assertEqual("final", self.reg.rows[("archive", "ARC-UI-2", row["file"])]["status"])
        self.assertTrue((self.root / "ARC-UI-2" / "final" / "m.png").is_file())
        self.assertIn(("archive", "ARC-UI-2", "final/m.png"), self.reg.rows)
        with self.assertRaises(KeyError):
            ia.mark(self.reg, "ARC-UI-2", "nope.png", "final")

    def test_mark_note_replaces_the_note_and_final_copy_carries_it(self) -> None:
        row = ia.add_file(self.reg, "ARC-UI-2", self.image("m.png"), kind="master", note="old")
        ia.mark(self.reg, "ARC-UI-2", row["file"], "final", "matches kit master x")
        self.assertEqual("matches kit master x", self.reg.rows[("archive", "ARC-UI-2", row["file"])]["note"])
        self.assertEqual("matches kit master x", self.reg.rows[("archive", "ARC-UI-2", "final/m.png")]["note"])
        ia.mark(self.reg, "ARC-UI-2", row["file"], "intermediate")  # no note given: the note stays
        self.assertEqual("matches kit master x", self.reg.rows[("archive", "ARC-UI-2", row["file"])]["note"])

    def test_mark_keeps_two_accepted_files_of_one_name_and_refuses_a_final_copy(self) -> None:
        a = ia.add_file(self.reg, "ARC-UI-2", self.image("A.png", 1), kind="composite")
        b = ia.add_file(self.reg, "ARC-UI-2", self.image("sub/A.png", 2) if False else self.image("A.png", 2), kind="collage")
        ia.mark(self.reg, "ARC-UI-2", a["file"], "final")
        ia.mark(self.reg, "ARC-UI-2", b["file"], "final")
        finals = [p for p in (self.root / "ARC-UI-2" / "final").iterdir()]
        self.assertEqual(2, len(finals))
        with self.assertRaises(ValueError):
            ia.mark(self.reg, "ARC-UI-2", "final/A.png", "final")

    def test_add_validates_the_task_and_restores_a_missing_copy(self) -> None:
        with self.assertRaises(ValueError):
            ia.add_file(self.reg, "../../x", self.image("a.png"), kind="composite")
        src = self.image("a.png")
        row = ia.add_file(self.reg, "ARC-UI-3", src, kind="composite")
        (self.root / "ARC-UI-3" / row["file"]).unlink()
        ia.add_file(self.reg, "ARC-UI-3", src, kind="composite", status="rejected")
        self.assertTrue((self.root / "ARC-UI-3" / row["file"]).is_file())
        self.assertEqual("rejected", row["status"])

    def test_save_keeps_rows_another_writer_added(self) -> None:
        other = ia.Registry(self.root)
        ia.add_file(other, "ARC-UI-4", self.image("x.png", 5), kind="composite")
        other.save()
        ia.add_file(self.reg, "ARC-UI-5", self.image("y.png", 6), kind="composite")
        self.reg.save()
        self.assertEqual(2, len(ia.Registry(self.root).rows))

    def test_scan_registers_hand_copied_files(self) -> None:
        (self.root / "ARC-UI-9" / "final").mkdir(parents=True)
        (self.root / "ARC-UI-9" / "final" / "x.png").write_bytes(fake_png(8, 8))
        (self.root / "ARC-UI-9" / "before-after.png").write_bytes(fake_png(8, 8, 1))
        self.assertEqual(2, ia.scan(self.reg))
        self.assertEqual(0, ia.scan(self.reg))
        self.assertEqual("final", self.reg.rows[("archive", "ARC-UI-9", "final/x.png")]["status"])
        self.assertEqual("composite", self.reg.rows[("archive", "ARC-UI-9", "before-after.png")]["kind"])

    def test_guess_kind(self) -> None:
        self.assertEqual("screenshot", ia.guess_kind("cap-brass/x.png"))
        self.assertEqual("collage", ia.guess_kind("contact-sheet.png"))
        self.assertEqual("composite", ia.guess_kind("before-after.png"))
        self.assertEqual("master", ia.guess_kind("local/masters/a.png"))
        self.assertEqual("unknown", ia.guess_kind("zzz.png"))


class GuardTests(unittest.TestCase):
    def test_worktree_temp_and_main_paths(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            main = Path(tmp) / "main"
            (main / ".claude/worktrees/w/local").mkdir(parents=True)
            (main / "local/steamwoven-ui/archive").mkdir(parents=True)
            self.assertEqual("worktree", ia.ephemeral_kind(main / ".claude/worktrees/w/local/out", main))
            self.assertIsNone(ia.ephemeral_kind(main / "local/steamwoven-ui/archive/ARC-UI-1", main))
            self.assertEqual("temp", ia.ephemeral_kind(Path(tmp) / "elsewhere", main))

    def test_warning_text_and_no_warning(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            out = io.StringIO()
            self.assertEqual("temp", ia.warn_if_ephemeral(Path(tmp) / "x", "tool.py", stream=out))
            self.assertIn("WARNING tool.py", out.getvalue())
            self.assertIn("image_archive.py add", out.getvalue())
        self.assertIsNone(ia.warn_if_ephemeral(None, "tool.py"))

    def test_fixtures_default_is_the_main_checkouts_masters(self) -> None:
        main = ia.prompt_library.main_checkout()
        self.assertEqual(main / "local/steamwoven-ui/masters", fixtures.default_out())


class RescueTests(unittest.TestCase):
    def test_batch_table_targets_are_valid_tasks(self) -> None:
        for task in rescue.BATCH_TASK.values():
            self.assertTrue(ia.TASK_RE.match(task), task)

    def test_number_hints_ignore_screen_sizes(self) -> None:
        tm = rescue.TaskMap.__new__(rescue.TaskMap)
        tm.by_num = {"3104": "ARC-UI-3104", "2560": "ARC-UI-2560", "1200": "ARC-UI-1200"}
        self.assertEqual("ARC-UI-3104", tm.from_name("composite-3104.png"))
        self.assertIsNone(tm.from_name("p-after-2560x1440.png"))
        self.assertIsNone(tm.from_name("shot-1200.png"))
        self.assertEqual("ARC-SCRIPT-2899", tm.from_name("junk-arc-script-2899.png".replace("junk-arc-script-", "ARC-SCRIPT-")))


if __name__ == "__main__":
    unittest.main()
