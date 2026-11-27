"""ARC-DEV-3277: the per-task image archive, its registry, the out-path guard and the rescue helpers (synthetic images, temp folders)."""
from __future__ import annotations

import io
import struct
import tempfile
import unittest
from contextlib import redirect_stderr
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

    def finals(self, task: str) -> list[str]:
        folder = self.root / task / "final"
        return sorted(p.name for p in folder.iterdir()) if folder.is_dir() else []

    def test_leaving_final_moves_the_copy_to_superseded_and_it_stays_gone_after_save(self) -> None:
        src = self.image("m.png")
        row = ia.add_file(self.reg, "ARC-UI-2", src, kind="master", status="final")
        self.assertEqual(["m.png"], self.finals("ARC-UI-2"))  # `add --status final` copies like `mark` does
        self.reg.save()  # the copy row is on disk: the next save must not read it back
        ia.mark(self.reg, "ARC-UI-2", row["file"], "intermediate", "superseded by ARC-UI-3")
        self.reg.save()
        self.assertEqual([], self.finals("ARC-UI-2"))
        self.assertEqual(src.read_bytes(), (self.root / "ARC-UI-2" / "superseded" / "m.png").read_bytes())
        again = ia.Registry(self.root)
        self.assertNotIn(("archive", "ARC-UI-2", "final/m.png"), again.rows)
        moved = again.rows[("archive", "ARC-UI-2", "superseded/m.png")]
        self.assertEqual(("intermediate", "superseded by ARC-UI-3"), (moved["status"], moved["note"]))
        with self.assertRaises(ValueError):
            ia.mark(again, "ARC-UI-2", "superseded/m.png", "final")

    def test_accept_replace_accept_again_never_overwrites_a_superseded_file(self) -> None:
        row = ia.add_file(self.reg, "ARC-UI-2", self.image("m.png"), kind="master")
        for status in ("final", "rejected", "final", "intermediate"):
            ia.mark(self.reg, "ARC-UI-2", row["file"], status)
        self.assertEqual([], self.finals("ARC-UI-2"))
        self.assertEqual(2, len(list((self.root / "ARC-UI-2" / "superseded").iterdir())))
        self.assertEqual(3, len(self.reg.rows))
        for r in self.reg.rows.values():
            self.assertTrue((self.root / r["task"] / r["file"]).is_file(), r["file"])

    def test_add_with_a_non_final_status_retires_the_copy_of_a_final_row(self) -> None:
        src = self.image("m.png")
        ia.add_file(self.reg, "ARC-UI-2", src, kind="master", status="final")
        ia.add_file(self.reg, "ARC-UI-2", src, kind="master", status="rejected")
        self.assertEqual([], self.finals("ARC-UI-2"))
        self.assertEqual(["rejected", "rejected"], [r["status"] for r in self.reg.rows.values()])

    def test_the_copy_stays_while_another_file_of_the_same_content_is_final(self) -> None:
        row = ia.add_file(self.reg, "ARC-UI-2", self.image("m.png"), kind="master", status="final")
        twin = self.root / "ARC-UI-2" / "composite" / "twin.png"
        twin.parent.mkdir()
        twin.write_bytes((self.root / "ARC-UI-2" / row["file"]).read_bytes())
        ia.scan(self.reg, "ARC-UI-2", kind="composite")
        ia.mark(self.reg, "ARC-UI-2", "composite/twin.png", "final")
        self.assertEqual(["m.png"], self.finals("ARC-UI-2"))  # one content, one copy
        ia.mark(self.reg, "ARC-UI-2", row["file"], "intermediate")
        self.assertEqual(["m.png"], self.finals("ARC-UI-2"))
        ia.mark(self.reg, "ARC-UI-2", "composite/twin.png", "intermediate")
        self.assertEqual([], self.finals("ARC-UI-2"))

    def test_a_final_file_without_an_original_row_is_marked_by_its_own_path(self) -> None:
        (self.root / "ARC-UI-9" / "final").mkdir(parents=True)
        (self.root / "ARC-UI-9" / "final" / "built.png").write_bytes(fake_png(8, 8))
        ia.scan(self.reg, kind="composite")
        with self.assertRaises(ValueError):
            ia.mark(self.reg, "ARC-UI-9", "final/built.png", "final")
        moved = ia.mark(self.reg, "ARC-UI-9", "final/built.png", "rejected", "superseded")
        self.assertEqual("superseded/built.png", moved["file"])
        self.assertEqual([], self.finals("ARC-UI-9"))
        self.assertEqual([("archive", "ARC-UI-9", "superseded/built.png")], list(self.reg.rows))

    def test_marking_final_again_never_overwrites_accepted_bytes(self) -> None:
        src = self.image("m.png")
        row = ia.add_file(self.reg, "ARC-UI-2", src, kind="master", status="final")
        original = self.root / "ARC-UI-2" / row["file"]
        original.write_bytes(fake_png(40, 30, 7))  # a tool rewrote the file in place
        ia.mark(self.reg, "ARC-UI-2", row["file"], "final")
        ia.add_file(self.reg, "ARC-UI-2", src, kind="master", status="final")
        self.assertEqual(src.read_bytes(), (self.root / "ARC-UI-2" / "final" / "m.png").read_bytes())
        (self.root / "ARC-UI-2" / "final" / "m.png").unlink()
        row["status"] = "intermediate"
        with self.assertRaises(ValueError):  # the registered bytes are gone: the new ones are not accepted in their place
            ia.mark(self.reg, "ARC-UI-2", row["file"], "final")
        self.assertEqual("intermediate", row["status"])

    def test_no_superseded_row_is_left_unknown(self) -> None:
        row = ia.add_file(self.reg, "ARC-UI-2", self.image("m.png"), kind="master", status="final")
        ia.mark(self.reg, "ARC-UI-2", row["file"], "unknown")
        self.assertEqual("intermediate", self.reg.rows[("archive", "ARC-UI-2", "superseded/m.png")]["status"])
        (self.root / "ARC-UI-3" / "superseded").mkdir(parents=True)
        (self.root / "ARC-UI-3" / "superseded" / "o.png").write_bytes(fake_png(8, 8, 3))
        ia.scan(self.reg, "ARC-UI-3", kind="master")
        orphan = self.reg.rows[("archive", "ARC-UI-3", "superseded/o.png")]
        self.assertEqual("intermediate", orphan["status"])
        ia.mark(self.reg, "ARC-UI-3", "superseded/o.png", "rejected")
        self.assertEqual("rejected", orphan["status"])
        self.assertTrue((self.root / "ARC-UI-3" / "superseded" / "o.png").is_file())

    def test_a_new_rejected_file_retires_a_final_file_of_its_content(self) -> None:
        (self.root / "ARC-UI-9" / "final").mkdir(parents=True)
        (self.root / "ARC-UI-9" / "final" / "x.png").write_bytes(self.image("x.png").read_bytes())
        ia.scan(self.reg, "ARC-UI-9", kind="composite")
        ia.add_file(self.reg, "ARC-UI-9", self.image("x.png"), kind="composite", status="rejected")
        self.assertEqual([], self.finals("ARC-UI-9"))

    def test_the_cli_takes_no_new_row_of_an_unknown_kind(self) -> None:
        for argv in (["add", "ARC-UI-1", "a.png"], ["add", "ARC-UI-1", "a.png", "--kind", "unknown"]):
            with self.assertRaises(SystemExit), redirect_stderr(io.StringIO()):
                ia.build_parser().parse_args(argv)

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
        skipped: list[str] = []
        self.assertEqual(1, ia.scan(self.reg, skipped=skipped))  # the folder and the name give x.png no kind
        self.assertEqual(["ARC-UI-9/final/x.png"], skipped)
        self.assertEqual(1, ia.scan(self.reg, kind="master"))
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
